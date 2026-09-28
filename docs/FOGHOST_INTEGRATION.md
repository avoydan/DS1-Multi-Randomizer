# FogHost integration design

Task 9. Design of the dedicated C# host around FogMod's callable engine, the
JSON process boundary Task 10 implements, and the concrete MSB/EMEVD/GameParam
overlap with the combined Items + Enemies engine that Task 11 must bridge.

Nothing here is implemented yet. Source facts cite the audited FogMod commit
and the vendored `DsrDataTool` (`engines/combined/vendor/tools/DsrDataTool/Program.cs`).

## Audited source

- FogMod: `https://github.com/thefifthmatt/FogMod`, commit
  `28711c3f8b1f7f8c1f329ef9a7f4d9c1e55ce526` ("DS3 update", current `master`
  at audit time). The DS1R path is still present and separate from the DS3
  path at this commit.
- `FogMod/FogMod.csproj`: .NET Framework 4.6.1 WinExe, NuGet `YamlDotNet`
  7.0.0, and **sibling project references** `..\..\SoulsFormats` and
  `..\..\SoulsIds` with no revision pin.
- Engine files (no WinForms dependency): `Randomizer.cs`,
  `RandomizerOptions.cs`, `AnnotationData.cs`, `Graph.cs`,
  `GraphConnector.cs`, `GraphChecker.cs`, `ItemReader.cs`,
  `GameDataWriter.cs`, `GameDataWriter3.cs`, `EventConfig.cs`,
  `GenerateConfig.cs`, `Util.cs`.
- UI/entry files (excluded from the host): `Program.cs`, `MainForm*.cs`,
  `MainForm3*.cs`, `*.resx`.

## Why not the existing CLI

`Program.cs` parses `ptde`/DS1R and then unconditionally executes
`game = FromGame.DS3;` before calling `Randomize`. It also derives the game
directory from `SoulsIds.GameSpec.ForGame(game).GameDir` (a developer
default) and writes in place into it. It cannot be used as the boundary. The
host never calls `Program.Main`.

## Engine call facts that shape the host

`Randomizer.Randomize(RandomizerOptions opt, FromGame game, string gameDir, string outDir)`:

1. Reads `dist\fog.txt` (DS1) **relative to the process working
   directory**, deserializes `AnnotationData`, builds the `Graph`.
2. `ItemReader.FindItems(...)` loads `ItemLotParam` from
   `gameDir\param\GameParam\GameParam.parambnd.dcx` and locates key items
   only in the lots listed in `fog.txt` `LotLocations`.
3. `GraphConnector.Connect(...)` solves the entrance graph using
   `new Random(opt.Seed)`, `new Random(opt.Seed - 1)` (custom start) and
   `new Random(opt.Seed + tries)`.
4. `opt["dryrun"]` returns after the graph solve, before any write
   ("Success (dry run)"). `opt["bonedryrun"]` returns earlier.
5. DS1 write: `new GameDataWriter().Write(opt, ann, g, gameDir, game)` —
   **`outDir` is ignored for DS1; all output is written into `gameDir`.**
6. All diagnostics go through `Console.WriteLine`; failures are plain
   `System.Exception` with a message string.

`GameDataWriter.Write` (DS1R) reads and writes:

| Input | Source |
|---|---|
| Base MSBs, layouts, base params | `dist\DS1R\...` (FogMod's bundled copies, CWD-relative) |
| Game MSBs (`map\MapStudio\*.msb`, only maps annotated in `fog.txt`) | `gameDir` |
| Game params (`param\GameParam\GameParam.parambnd.dcx`) | `gameDir` |
| `msg\<lang>\menu.msgbnd.dcx` (`Event_text_`) | `gameDir` |
| Game EMEVD (`event\<map>.emevd.dcx`, for each file in `dist\DS1R\event`) | `gameDir` (read for carry-over only) |
| FogMod EMEVD templates | `dist\DS1R\event\*.emevd.dcx` |
| FogMod talk ESDs | `dist\DS1R\script\talk\*.talkesdbnd.dcx` |

| Output (all written into `gameDir`) |
|---|
| `event\common.emevd.dcx` + 17 `event\m*.emevd.dcx` (FogMod's template plus carry-over) |
| 18 `map\MapStudio\m*.msb` (including `m12_00_00_01`) |
| `param\GameParam\GameParam.parambnd.dcx` |
| 17 `script\talk\m*.talkesdbnd.dcx` (plain copy from `dist`) |
| `msg\<lang>\menu.msgbnd.dcx` |
| `.bak` next to every file matching a `dist\DS1R` base file (`SFUtil.Backup`) |

## Host design

### Project

`tools/fog-host/` (name per `docs/ARCHITECTURE.md`): a console project that
**compiles the pinned FogMod engine sources directly** (the engine file list
above; never `Program.cs` or `MainForm*`) plus a small `FogHost` entry point.
It does not reference `FogMod.exe`, WinForms or `System.Windows.Forms`.

- Target framework: to be fixed when the host is built. FogMod targets
  net461; `DsrDataTool` targets .NET 8. A .NET 8 console host is preferred
  (one runtime for both tools) if the pinned SoulsFormats/SoulsIds build
  there; otherwise net48.
- Do **not** enable checked arithmetic (`<CheckForOverflowUnderflow>` stays
  off): FogMod relies on unchecked `opt.Seed - 1` / `opt.Seed + tries`.
- `System.Random(int)` with an explicit seed is the legacy deterministic
  algorithm on both .NET Framework and .NET 8; determinism is still verified
  empirically (see Verification).
- `CultureInfo.InvariantCulture` is set on the thread before any engine call
  (as `Program.Main` does).

### Dependencies (two SoulsFormats lines in one pipeline)

FogMod's code uses the older SoulsFormats API (`PARAM.Layout`,
`GameEditor.LoadLayouts`, `SoulsIds.GameSpec`). `DsrDataTool` uses
`JuicerMV.SoulsFormats` 0.1.3 (`PARAMDEF`). Assume two different SoulsFormats
builds touch the same MSB/EMEVD/param files:

- the SoulsFormats and SoulsIds revisions used by FogHost must be pinned to
  exact commits when the host is vendored (the upstream csproj pins none);
- SoulsIds' license is **unverified** — check it before vendoring;
- because MSB/param round-trips cross libraries, Task 11 must re-verify the
  combined enemy placements (MSB `ModelName`/`NPCParamID`/`ThinkParamID`,
  model declarations) after FogHost writes.

### Process boundary

```text
FogHost.exe generate --request <request.json> --response <response.json>
```

- The backend owns the timeout and kills the process on expiry.
- The host writes the response to `<response.json>.tmp` and renames it.
- **Exit code 0 means the response file is authoritative** — for success
  *and* for a typed failure. A non-zero exit, or exit 0 without a response
  file, means the host crashed (`FOG_HOST_CRASHED`).
- Before calling FogMod, the host redirects `Console.Out` (and
  `Console.Error`) to the request's `logPath`. FogMod text is **never**
  parsed as protocol. The host emits its own progress lines on the saved
  original stdout, one JSON object per line:

  ```json
  {"type":"progress","stage":"load-annotations","message":"..."}
  ```

  Stages: `validate-request`, `load-annotations`, `read-key-items`,
  `solve-graph`, `write-game-data`, `hash-outputs`. Unknown stdout lines are
  treated as log text by the backend.

### Working directory and data root

FogMod resolves `dist\fog.txt` and `dist\DS1R\...` relative to the CWD. The
request carries `fogDataRoot`: the directory **containing** `dist` (i.e.
`<fogDataRoot>\dist\fog.txt` must exist). The host sets
`Environment.CurrentDirectory = fogDataRoot` before the engine call and
restores it afterwards. The host verifies `dist\fog.txt`,
`dist\DS1R\event`, `dist\DS1R\map\MapStudio`, `dist\DS1R\param\GameParam`,
`dist\DS1R\script\talk` and `dist\DS1R\Layouts` exist before running.

### Staging directory (workspace output)

Because DS1 `Write()` writes into `gameDir`, the host's `gameDir` **is** a
per-attempt staging directory owned by the backend:

- one fresh staging directory per Fog attempt; never reused across attempts;
- built by the backend (Task 11/12) from pristine files overlaid with the
  combined package's `mod/` output. It must contain every file FogMod reads:
  `param\GameParam\GameParam.parambnd.dcx`, `msg\<lang>\menu.msgbnd.dcx`,
  every `event\*.emevd.dcx` present in `dist\DS1R\event` (including
  `common`), every annotated `map\MapStudio\*.msb`, and an existing
  `script\talk\` directory;
- the host refuses a staging directory that contains
  `DarkSoulsRemastered.exe` (`FOG_STAGING_UNSAFE`) — it must never be a live
  installation;
- after a successful `write`, the host deletes every `*.bak` created in the
  staging directory. FogMod's `.bak` files are never a backup authority
  (`AGENTS.md` backup policy).

### Request (schema version 1)

```json
{
  "schemaVersion": 1,
  "game": "DS1R",
  "mode": "dryRun",
  "seed": 3141592653,
  "options": {
    "traversableFogs": true,
    "bossFogs": true,
    "areaWarps": true,
    "majorPvpFogs": false,
    "minorPvpFogs": false,
    "lordvesselGates": false,
    "randomStart": false,
    "disconnectedFogs": false,
    "noFallBedOfChaos": false,
    "scaling": true,
    "glitchedLogic": false,
    "requireLordSouls": true,
    "pacifistMode": false
  },
  "language": "ENGLISH",
  "fogDataRoot": "C:\\...\\fog-data",
  "stagingDirectory": "C:\\...\\workspace\\fog\\attempt-0\\game",
  "logPath": "C:\\...\\workspace\\fog\\attempt-0\\foghost.log",
  "compatibility": {}
}
```

- `game`: only `"DS1R"` is accepted (`FOG_REQUEST_INVALID` otherwise).
  The host sets `opt.Game = FromGame.DS1R` and passes `FromGame.DS1R`
  explicitly; nothing is derived from `GameSpec` defaults.
- `mode`: `"dryRun"` sets FogMod's `dryrun` flag (graph solved, nothing
  written) — this is the Fog dry-run stage of the retry flow in
  `docs/source-analysis.md`. `"write"` runs the full writer.
- `seed`: uint32 (`DerivedSeeds.fog`, or the Fog retry derivation). Cast
  unchecked to `int` for `opt.Seed`, as `Program.cs` does; `opt.DisplaySeed`
  echoes it back as uint.
- `options`: exactly the keys of `FogOptions` in
  `packages/contracts/src/index.ts` minus `enabled` and `language`. Unknown
  or missing keys are rejected. Mapping to FogMod flags (from
  `MainForm.Designer.cs` control names read by `Graph`/`GameDataWriter`):

  | Contract key | FogMod flag |
  |---|---|
  | `traversableFogs` | `world` |
  | `bossFogs` | `boss` |
  | `areaWarps` | `warp` |
  | `majorPvpFogs` | `major` |
  | `minorPvpFogs` | `minor` |
  | `lordvesselGates` | `lordvessel` |
  | `randomStart` | `start` |
  | `disconnectedFogs` | `unconnected` |
  | `noFallBedOfChaos` | `bboc` |
  | `scaling` | `scale` |
  | `glitchedLogic` | `hard` |
  | `requireLordSouls` | `lords` |
  | `pacifistMode` | `pacifist` |

  `v5` stays enabled (the `RandomizerOptions` default). Debug/diagnostic
  flags (`dryrun`, `bonedryrun`, `dumpgraph`, `explain`, `debuglots`,
  `msbinfo`, `dumpcols`, …) are never settable through `options`; `dryrun`
  is set only via `mode`.
- `language`: one of `FogLanguage`; sets `opt.Language`.
- `compatibility`: the Task 11 bridge inputs. Task 11 bumped request and
  response `schemaVersion` to **2**; see "Compatibility bridge (Task 11)".

### Response

```json
{
  "schemaVersion": 1,
  "ok": true,
  "hostVersion": "0.1.0",
  "fogModCommit": "28711c3f8b1f7f8c1f329ef9a7f4d9c1e55ce526",
  "annotationSha256": "<sha256 of dist/fog.txt>",
  "mode": "write",
  "seed": 3141592653,
  "fogOptions": "boss lords scale v5 warp world 3141592653",
  "keyItems": { "randomized": true, "hash": "01234" },
  "files": [
    { "path": "event/common.emevd.dcx", "size": 123, "sha256": "..." }
  ],
  "error": null
}
```

- `fogOptions` is `opt.ToString()` for the manifest (options + display seed).
- `files`: every file the writer produced, staging-relative, forward
  slashes, sorted ordinally, with SHA-256; `.bak` files excluded. Empty for
  `dryRun`.
- No absolute paths or timestamps in the response (Task 8 finding 4), so
  two runs' responses compare byte-for-byte.

Failure (`ok: false`, exit code 0):

```json
{ "schemaVersion": 1, "ok": false, "error": {
  "code": "FOG_UPSTREAM_INCOMPATIBLE",
  "stage": "read-key-items",
  "retryable": true,
  "retryScope": "combined",
  "message": "Couldn't find Lordvessel in item lots"
} }
```

### Error mapping

The host catches exceptions per stage and classifies FogMod's known throw
sites by message prefix. Everything unrecognized is non-retryable.

| FogMod message (source) | Code | `retryScope` |
|---|---|---|
| `Couldn't find … in item lots` (`ItemReader.cs`) | `FOG_UPSTREAM_INCOMPATIBLE` | `combined` |
| `Item … found in both … and …` (`ItemReader.cs`) | `FOG_UPSTREAM_INCOMPATIBLE` | `combined` |
| `Couldn't solve seed …` (`GraphConnector.cs`) | `FOG_SEED_UNSOLVABLE` | `fog` |
| `No swappable edge found to inaccessible areas…` (`GraphConnector.cs`) | `FOG_SEED_UNSOLVABLE` | `fog` |
| `Could not find edge into unreachable areas…` (`GraphConnector.cs`) | `FOG_SEED_UNSOLVABLE` | `fog` |
| `Ran out of eligible edges` (`GraphConnector.cs`) | `FOG_SEED_UNSOLVABLE` | `fog` |
| request/data-root/staging validation | `FOG_REQUEST_INVALID` / `FOG_DATA_MISSING` / `FOG_STAGING_UNSAFE` | none |
| file I/O and format errors | `FOG_IO_FAILED` | none |
| anything else (`Internal error…`, `Unknown map…`, …) | `FOG_ENGINE_FAILED` | none |
| process crash / timeout (backend-side) | `FOG_HOST_CRASHED` / `FOG_HOST_TIMEOUT` | none |

`retryScope: "combined"` means the combined stage is retried as one unit;
`"fog"` means only the Fog seed advances
(`SHA256(master + ":fog-retry:" + attempt)`). Retry bounds and manifest
recording belong to the pipeline (Task 12).

## Overlap with the combined engine

The combined package (Task 8) contains 54 patched files under `mod/`:
18 MSBs, 17 EMEVDs, 17 Lua bundles (`script/*.luabnd.dcx`),
`sfx/FRPG_SfxBnd_CommonEffects.ffxbnd.dcx`, and `GameParam`. FogMod writes
18 MSBs, 18 EMEVDs, `GameParam`, 17 talk ESDs and `menu.msgbnd`.

**Stock FogMod is lossy on combined output.** Three hard incompatibilities:

### 1. EMEVD — combined event changes are discarded

FogMod does not edit the game's EMEVD; it writes its own template from
`dist\DS1R\event` and carries over only specific events from the game file
(`GameDataWriter.cs`, event loop near the end of `Write`):

- whole events whose ID is in the hardcoded legacy-Enemy-Randomizer set
  `enemyRandoEvents` (`11x09000 ± 200` per map, plus `11211600`,
  `11505600`, `11515600`, `11700800`);
- instructions of the events in `copyRandoEvents` (`11205382`, `11015382`,
  `11015396`, `11705396`, `11415392`, `11415397`, `11410250`, `11305100`);
- trailing event-0 init instructions — walking **backwards** from the end of
  event 0 and **stopping at the first** instruction that is not `2004[02]`
  or a `2000[00]` for an `enemyRandoEvents` ID;
- `11810310` (Asylum Demon intro): a legacy-specific heuristic removes
  `2003[18]`/`2004[41]` if the game copy lacks `2003[18]`.

The combined engine (`PatchBossNames` in `DsrDataTool/Program.cs`) instead:

- adds custom events at `10_000_000 + map*100_000 + 19_900` upward (e.g.
  `11019900…` for m10), plus a floor event near `11419980`, via
  `FindFreeCustomEventRange`, which **shifts IDs on collision** — so the
  real IDs must be read from the produced package, not assumed;
- registers each custom event by appending `2000[00]` to event 0;
- removes/inserts instructions in vanilla events (model-specific enemy
  instructions, portable boss lifecycle, backread `2004[29]` for the
  Daughter of Chaos, boss health-bar names, animation/standby fixes);
- edits `11810310` itself when the Asylum Demon slot is randomized.

Result with stock FogMod: none of the custom events survive (IDs not in
`enemyRandoEvents`), the appended event-0 registrations are dropped (the
backwards walk stops immediately), and every edit to a vanilla event is
replaced by FogMod's template. `11810310` is a known both-sides conflict.

### 2. GameParam/NpcParam — combined scaled rows are deleted

Unconditionally (with or without `scale`), `GameDataWriter.Write`:

- for every MSB enemy whose `NPCParamID >= 1000` is not a vanilla row
  (FogMod's `dist` params) and not in the legacy `enemyRandoBossCopies`
  list, tries to restore a "base" ID (a `pcAttr*` hint, or the vanilla
  map's enemy of the same name **and same model**), otherwise leaves it;
- then deletes every `NpcParam` row that is not vanilla and not in
  `enemyRandoBossCopies`.

The combined engine adds scaled `NpcParam` rows (`AddScaledNpcRows`, IDs
from `scaledNpcParamId`) and points MSB enemies at them. Randomized enemies
change model, so FogMod's restore fails and the enemy is left referencing a
**deleted row**; unrandomized-model cases are silently reverted to unscaled.
Also: `GameAreaParam` is replaced by FogMod's vanilla copy and
`SpEffectParam` rows `7200–7399` are cleared — the combined engine touches
neither (no references in `DsrDataTool`), so these are safe today but must
stay on the bridge's watch list. `NpcThinkParam` custom rows
(`AddCustomThinkRows`) are not touched by FogMod.

Option rule (resolved by Task 11, see "Compatibility bridge"): the two
scaling mechanisms compose and are not mutually exclusive.

### 3. Key items — FogMod only looks in annotated item lots

`ItemReader.FindItems` (DS1) scans only the `ItemLotParam` rows listed in
`fog.txt` `LotLocations` and throws `Couldn't find <item> in item lots`
for any key item not found there (unless tagged `randomonly`/`hard`).
Combined placements in shops, starting equipment, gifts outside annotated
lots, or enemy drops therefore fail the Fog run, and FogMod's key-item
logic cannot see where the combined engine put progression items. This is
`docs/MOD_ANALYSIS.md` gate 4 ("pass explicit key-location data").

### Safe pass-through

| File class | Combined | FogMod | Overlap |
|---|---|---|---|
| `script/*.luabnd.dcx` (AI) | writes | — | none |
| `sfx/*.ffxbnd.dcx` | writes | — | none |
| `script/talk/*.talkesdbnd.dcx` | — | overwrites from `dist` | none |
| `msg/<lang>/menu.msgbnd.dcx` | reads `item.msgbnd` only | writes `Event_text_` | none |
| `map/MapStudio/*.msb` | enemy parts, model declarations | loads the game MSB, removes/adds regions, players, objects, collisions; rewrites `NPCParamID` (see 2) | shared file, edits largely disjoint except `NPCParamID` |
| `event/*.emevd.dcx` | edits | replaces (see 1) | **conflict** |
| `GameParam` | ItemLot, ShopLineup, CharaInit, NpcParam, NpcThinkParam | NpcParam, SpEffect, GameArea | **conflict on NpcParam** |

### Final-package rule (for Task 12)

- combined-only files (Lua bundles, FFX) come from the combined package;
- shared files (MSB, EMEVD, GameParam) come from the post-bridge Fog output
  (FogHost `write` on the staging directory);
- Fog-only files (talk ESDs, `menu.msgbnd`) come from the Fog output.

## Decision: FogHost carries a minimal FogMod patch set

Because of incompatibilities 1–3, FogHost cannot run FogMod unmodified on
combined output. The host compiles the pinned FogMod engine sources with a
patch set **confined to `GameDataWriter.cs` and `ItemReader.cs`**, driven
by the request's `compatibility` object (Task 11 defines the exact inputs
and rules). Apache-2.0 §4(b) requires a prominent change notice in each
modified file; the upstream license and notices are preserved
(`docs/LICENSING.md`). Recorded in `docs/DECISIONS.md`.

Host-side handling that needs **no** FogMod edits: explicit DS1R, CWD/data
root, staging-directory writes, `.bak` cleanup, console redirection, error
classification, output hashing.

The current fog probe in `apps/backend/src/core/pipeline.ts`
(`vendor/FogMod/Randomizer.cs` / `vendor/FogMod.exe`) and
`engines/fog/README.md` are superseded by this design; they are left
unchanged here and replaced by Task 10.

## Verification the host must pass (when built)

- DS1R forced: the response echoes DS1R-only behavior; a request with any
  other `game` is rejected.
- Determinism: two **separate host processes** with the same request and
  identical staging inputs produce byte-identical `files` hashes and
  responses. Include boundary seeds `0`, `0x7FFFFFFF`, `0x80000000`,
  `0xFFFFFFFF` (FogMod computes `Seed - 1` and `Seed + tries`).
- `dryRun` writes nothing to the staging directory.
- No write outside `stagingDirectory` and `logPath`; no `.bak` left behind.
- Each mapped error code is reachable by a fixture (e.g. a staging
  GameParam with a key item moved out of annotated lots →
  `FOG_UPSTREAM_INCOMPATIBLE`).

## Blockers and open items (not blocking Task 9)

1. **No task builds or vendors the C# FogHost.** Task 10 is the
   backend-side process adapter, Task 11 the bridge, Task 17 only checks
   FogHost availability. Building `tools/fog-host` (vendoring FogMod at the
   pinned commit, pinning SoulsFormats/SoulsIds, the Program replacement) is
   unassigned. The plan needs a task for it, or Task 11 must absorb it.
2. **FogMod `dist/` is game-derived data.** `dist\DS1R` contains
   FromSoftware MSBs, EMEVDs, GameParam, talk ESDs and `menu.msgbnd` with
   FogMod edits. Apache-2.0 covers FogMod's code, not that data, and
   `docs/LICENSING.md` forbids shipping maps/events/params. These files are
   FogMod's own variants and cannot be regenerated from the user's install.
   How users obtain them (e.g. pointing at a locally downloaded FogMod
   release) is an open packaging/licensing decision; the contract therefore
   takes `fogDataRoot` as an input path.
3. **Dependency pins and SoulsIds license** are unresolved (see
   Dependencies).
4. **Legacy-modified installs** (Task 8 finding 1) also affect FogMod: its
   carry-over code actively imports legacy Enemy Randomizer events from the
   game EMEVD. Staging must be built only from verified pristine data.

## Compatibility bridge (Task 11)

Implemented backend side in `apps/backend/src/adapters/fogCompatibility.ts`
(pure, no I/O) and `fogHostProcess.ts` (schema v2). The host-side rules
below are the specification for FogHost's `GameDataWriter.cs` patch set;
they cannot be compiled or tested until the host is built (open item 1).

### Schema v2 `compatibility`

```json
{
  "baseDirectory": "C:\\...\\workspace\\fog\\c0-f0\\base",
  "combinedFiles": [
    { "path": "event/m10_00_00_00.emevd.dcx",
      "baseSha256": "<pristine>", "combinedSha256": "<combined output>" }
  ],
  "preservedNpcParams": [ { "id": 910000, "baseId": 223000 } ]
}
```

- `combinedFiles`: the MSB, EMEVD and GameParam entries of the combined
  `patch-manifest.json` (`patchedMaps`, `events`, `gameParam`). Lua and FFX
  entries are not shared with FogMod and are excluded. `source` must be a
  game-relative forward-slash path and `output` must equal `mod/<source>`.
  Entries are sorted ordinally and unique, with lowercase hashes.
  `generatedAt` and `outputDirectory` are never copied, so the object is
  stable across reruns.
- `preservedNpcParams`: every numeric `scaledNpcParamId` in the placements
  file's `enemies` and `bosses` lists. `baseId` is the placement's
  `sourceNpcParamId`, i.e. the **destination slot's vanilla** NpcParam row.
  `AddScaledNpcRows` copies that row's non-combat fields and, when scaling
  is not `vanilla`, its combat fields. A scaled ID that maps to two base
  rows is rejected. Scaled rows also exist with `enemyScaling: "vanilla"`
  (drop, team and tangibility overrides), so the list is not gated on the
  scaling option.
- `baseDirectory`: holds the pristine copy of every `combinedFiles` path,
  which is the three-way merge base.

### Staging (per attempt)

`planFogStaging` builds `<attempt>/game`, which is FogMod's `gameDir`:

- the FogMod read set (`fogReadSet`), copied from verified pristine data:
  GameParam, `msg/<lang>/menu.msgbnd.dcx`, every `dist/DS1R/event` file
  and every annotated MSB;
- overlaid with `mod/<path>` for each combined file. Combined shared files
  outside the read set are staged too;
- an empty `game/script/talk`.

It also plans `<attempt>/base/<path>`, a pristine copy of each combined
file. `buildFogCompatibility` takes that base directory as an input, so the
caller (Task 12) computes `<attempt>/base` before it builds the
compatibility object; it does not need a finished staging plan for that.

**Final package (refines the Task 9 rule):** a `combinedFiles` path that is
missing from the response `files` (FogMod did not rewrite it) is taken from
staging. Staging holds the combined output, which the host has
hash-verified. The attempt directory must not overlap the pristine directory or the
combined package. The copying itself and the retry loop belong to Task 12.

### Host rules

The host validates inputs first, and fails with `FOG_REQUEST_INVALID` if:

- a `combinedFiles` path does not hash to `combinedSha256` in staging or to
  `baseSha256` in `baseDirectory`;
- a `baseId` is not a vanilla NpcParam row.

**EMEVD: three-way merge.** It applies only to event files listed in
`combinedFiles`; other files keep stock FogMod behavior. Let B be the base,
C the combined file in staging, and T FogMod's output for that file after
its stock carry-over. Events are compared by ID and full content
(instructions, parameters, rest behavior).

1. **Custom event** (in C, not in B, e.g. `11019900…`): append it to T in
   C's order. If T already has that ID with different content, fail with
   `FOG_MERGE_CONFLICT`.
2. **Deleted event** (in B, not in C): `FOG_MERGE_CONFLICT`. The combined
   engine never deletes events, so anything else is unsupported.
3. **Edited vanilla event** (in both, C ≠ B):
   - T's copy equals B's (FogMod did not change it): T takes C's event.
   - T's copy equals C's: keep it.
   - Otherwise: `FOG_MERGE_CONFLICT`. No per-event winner table exists yet;
     `11810310` (Asylum Demon) is the known instance (open item 5).
4. **Event 0**: every instruction in C₀ that is not in B₀ must be a
   `2000[00]` initialization. These are appended to T₀ in C's order,
   skipping any already present. Any other event-0 difference is a
   `FOG_MERGE_CONFLICT`.

The merge and conflict check also run in **`dryRun`**. There, T is computed
in memory (FogMod's template plus the stock carry-over) and FogMod's
`Write()` is never called. A successful `dryRun` therefore guarantees that
`write` hits no merge conflict.

**NpcParam and MSB**

- FogMod's "restore base NpcParam ID" pass skips every MSB enemy whose
  `NPCParamID` is a preserved `id`.
- FogMod's "delete non-vanilla NpcParam rows" pass keeps every preserved
  `id`.
- With FogMod `scale`, each preserved row receives exactly the change
  FogMod's scaling applies to its `baseId` row.

**Key items: no patch.** `ItemReader` stays stock and finds key items
wherever the combined engine placed them among the annotated lots. A key
item outside those lots (shop, starting gear, gift, unannotated drop)
raises `FOG_UPSTREAM_INCOMPATIBLE` with retry scope `combined`.

**Post-write invariants** are checked in `write` mode before outputs are
hashed. Any violation is `FOG_COMPATIBILITY_VIOLATED`:

1. Every enemy part from a combined MSB that still exists in the output
   keeps its `ModelName`, `NPCParamID` and `ThinkParamID`, and its model
   stays declared. If FogMod removed a combined-changed enemy part, that is
   also a violation.
2. `ItemLotParam`, `ShopLineupParam`, `CharaInitParam` and `NpcThinkParam`
   are identical to the staging input.
3. Each preserved NpcParam row exists. Without `scale` it is identical to
   the input. With `scale`, its changed cells equal the changes FogMod made
   to its `baseId` row.
4. Every event merged by rules 1, 3 and 4 is present and identical in the
   written EMEVD.

### Scaling rule

The two scaling mechanisms compose; they do not double-scale.

- In `DsrDataTool`, `area` and `progressive` behave identically. Any
  non-`vanilla` value copies the **destination slot's vanilla** combat
  fields and SpEffects into the scaled row (`AddScaledNpcRows`). This
  normalizes a replacement to the strength of the enemy it replaces.
- FogMod's `scale` then re-tiers enemies by their fog-graph position
  relative to their vanilla area.
- The `baseId` mapping makes FogMod treat each scaled row as the vanilla
  row it stands in for.

No option combination is rejected. **This is a hypothesis on the FogMod
side:** FogMod's `scale` code has not been read (open item 8).

### Retry and rejection

`decideFogRetry` is the single retry authority. It maps host error codes
through a backend table and ignores the host's own `retryScope` field:

| Code | Action |
|---|---|
| `FOG_SEED_UNSOLVABLE` | retry Fog: `fogAttempt + 1`, same combined attempt |
| `FOG_UPSTREAM_INCOMPATIBLE` | retry combined: `combinedAttempt + 1`, `fogAttempt` resets to 0 |
| `FOG_MERGE_CONFLICT`, `FOG_COMPATIBILITY_VIOLATED`, all others | fail, no retry |

Merge conflicts are not retryable. `FindFreeCustomEventRange` picks IDs
from the base EMEVD, so every combined attempt produces the same custom
IDs; retrying a structural conflict would only burn the retry budget.
Bounds are passed in by the caller. The suggested default is 3 combined ×
5 Fog attempts; Task 12 owns the final values and the manifest record.

Attempt seeds come from `deriveAttemptSeeds(master, c, f)`:

- attempt 0 is exactly `deriveSeeds()`;
- attempt n > 0 uses the named streams `combined-retry:<n>` (32 hex
  characters) and `fog-retry:<n>` (uint32 LE);
- the Fog seed depends only on the Fog attempt.

### Open items added by Task 11

5. **`11810310` and other events edited on both sides** fail with
   `FOG_MERGE_CONFLICT` until a verified per-event rule exists, which needs
   the FogMod template diff. As a result, **boss randomization combined with
   Fog is unproven** until the host exists and the conflict set has been
   measured.
6. It is unverified whether FogMod removes enemy parts from MSBs. If it
   does, invariant 1 needs an allow-list derived from FogMod's source.
7. The key-item retry rate is unmeasured. If combined placements often put
   key items outside annotated lots, the combined retry budget runs out.
   The remedy would be to constrain combined key-item locations when Fog is
   enabled, which is a product decision.
8. **FogMod's `scale` mechanism is unread.** The composition rule and
   invariants 1 and 3 assume that FogMod scales by editing NpcParam rows in
   place. If it instead creates tier rows and re-points each part's
   `NPCParamID`, invariant 1 fails on every scale-enabled run, and that is
   the desktop default (Fog on, `scaling: true`). This must be confirmed
   against the FogMod source when the host is built. The fallback is either
   to reject Fog `scale` together with combined scaling other than
   `vanilla`, or to make one mechanism the owner.
