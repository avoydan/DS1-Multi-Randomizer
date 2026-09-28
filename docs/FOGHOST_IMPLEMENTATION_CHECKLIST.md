# FogHost implementation checklist

Prepared 2026-09-28 as the handoff for the task that builds the C# FogHost.
The design is in `docs/FOGHOST_INTEGRATION.md`, the backend contract is in
`apps/backend/src/adapters/fogHostProcess.ts` (schema v2) and
`fogCompatibility.ts`. This document adds the source-verified facts that the
design left open, and the exact build and test steps. Nothing in it is
implemented yet.

Read before starting: `AGENTS.md`, this file, `docs/FOGHOST_INTEGRATION.md`
("Host design", "Compatibility bridge (Task 11)"), `docs/LICENSING.md`.

## 1. Pinned sources

| Component | Repository | Commit | License |
|---|---|---|---|
| FogMod engine + data | `https://github.com/thefifthmatt/FogMod` | `28711c3f8b1f7f8c1f329ef9a7f4d9c1e55ce526` (2022-02-15, "DS3 update") | Apache-2.0, file `FogMod/LICENSE` (blob `e454a525…`). No NOTICE file. |
| SoulsIds | `https://github.com/thefifthmatt/SoulsIds` | `e31a6fdb516491b6c731cb2847f22faaabbcf925` (2022-02-15, "Update for mods") | Apache-2.0 (root `LICENSE`). **Verified**; this closes the "SoulsIds license unverified" item. |
| SoulsFormats | see §3 | see §3 | GPL-3.0, with a caveat (§3) |
| YamlDotNet | NuGet | `7.0.0` (FogMod `packages.config`) | MIT |
| Newtonsoft.Json | NuGet | `12.0.3` (SoulsIds at the pin) | MIT |

Why `e31a6fd`: it has the same date as the FogMod commit and is the commit
that adds the `GameEditor`/`Events` APIs FogMod calls. Later SoulsIds commits
(2022-04 onward) target net6.0 and `SoulsFormatsNEXT`, which is a different
library line. **Do not use SoulsIds `master`.**

Local research clones (temporary; re-create them if they are gone):

```sh
T="$LOCALAPPDATA/Temp"
git clone https://github.com/thefifthmatt/FogMod "$T/fogmod-src" && git -C "$T/fogmod-src" checkout 28711c3f8b1f7f8c1f329ef9a7f4d9c1e55ce526
git clone https://github.com/thefifthmatt/SoulsIds "$T/foghost-deps/SoulsIds"
git clone https://github.com/thefifthmatt/SoulsFormats "$T/foghost-deps/SoulsFormats"
git clone https://github.com/JKAnderson/SoulsFormats "$T/foghost-deps/jk-SoulsFormats"
```

## 2. Files the host needs from each source

**FogMod, compiled into the host** (from `FogMod/`): `AnnotationData.cs`,
`EventConfig.cs`, `GameDataWriter.cs`, `GameDataWriter3.cs`,
`GenerateConfig.cs`, `Graph.cs`, `GraphChecker.cs`, `GraphConnector.cs`,
`ItemReader.cs`, `Randomizer.cs`, `RandomizerOptions.cs`, `Util.cs`, plus
`LICENSE`. None of these reference WinForms (verified by grep).

- `GameDataWriter3.cs` and `GenerateConfig.cs` are DS3-only. `Randomizer.cs`
  references `GameDataWriter3` and `GenerateConfig`, so either compile them or
  do not compile `Randomizer.cs` (see §6: the host reimplements the DS1 branch
  of `Randomize`, so `Randomizer.cs` can be dropped). Prefer the smallest set
  that compiles, and record it in `UPSTREAM_PIN.md`.
- Excluded: `Program.cs`, `MainForm*.cs`, `*.resx`, `Properties/**`,
  `App.config`, `packages.config`, `foggate.ico`.

**SoulsIds at `e31a6fd`** (`SoulsIds/`): `EMEDF.cs`, `Events.cs`,
`GameEditor.cs`, `GameSpec.cs`, `Scraper.cs`, `Universe.cs`, `LICENSE`.
FogMod uses `GameEditor`, `GameSpec`/`FromGame`, `Universe.LotTypes`, and
`Events` (DS3 path only). The upstream csproj is an old-style net461 project
with a WinForms reference and a Newtonsoft `HintPath` into a sibling repo.
Do not use it. Compile the `.cs` files directly into the host, or give them
a new SDK-style project.

**SoulsFormats**: the host needs `MSB1`, `EMEVD`, `PARAM` (both the
deprecated `PARAM.Layout` and `PARAMDEF`/`AppliedParamdef`), `FMG`, `BND3`,
`DCX`, and `SFUtil`. SoulsIds at the pin calls both `LoadLayouts()`
(`PARAM.Layout`, file `Formats/PARAM/Deprecated/Layout.cs`), and the
`PARAMDEF` overloads. FogMod's DS1 writer uses `AppliedParamdef`.

## 3. SoulsFormats choice (decide from the build probe)

| Candidate | Has `PARAM.Layout` | LICENSE in tree | Notes |
|---|---|---|---|
| (a) NuGet `JuicerMV.SoulsFormats` 0.1.3 (already used by `DsrDataTool`) | unknown | per package | If it compiles SoulsIds + FogMod, one SoulsFormats line for both tools. Listed as GPL-3.0-only in `THIRD_PARTY_NOTICES.md`. |
| (b) JKAnderson `9c0da6b` (HEAD, 2025-05-26) | yes | **GPL-3.0** | Targets `net462;netstandard2.1`, adds Zstd. API drift since 2021 is possible. |
| (c) thefifthmatt fork `master` = JKAnderson `106e54a` (2021-11-21) | yes | **none** | The contemporaneous revision for FogMod. It is also a commit in JKAnderson's repository. |

**License caveat:** JKAnderson's repository had no license until 2023. Commit
`44ffed4` ("license") added a joke `LICENSE.md` plus an image, not a real
license. The GPL-3.0 `LICENSE` was added in `9c0da6b` (2025-05-26). Using (c)
means distributing a 2021 tree that has no license text, on the strength of
the author's later GPL grant for the repository. Prefer (a) or (b). If only
(c) compiles, record why in `DECISIONS.md` and flag it for the user before
anything is distributed.

The probe result is recorded in §12.

## 4. FogMod data root

`Randomizer`/`GameDataWriter` read paths relative to the **process working
directory**:

- `dist\fog.txt` (annotations; blob `d1ae0175dbeef25e4da73a910c3ff80323f7563c`);
- `dist\DS1R\**`: 59 files, tree `bc86fb5aee292c653bbddce26bdfc003182ed1f0`:
  - `event\*.emevd.dcx`: 18 (`common` plus 17 maps);
  - `map\MapStudio\*.msb`: 18 (m10_00, m10_01, m10_02, m11_00, m12_00,
    m12_00_00_01, m12_01, m13_00, m13_01, m13_02, m14_00, m14_01, m15_00,
    m15_01, m16_00, m17_00, m18_00, m18_01);
  - `script\talk\*.talkesdbnd.dcx`: 17;
  - `Layouts\*.xml`: 4 (GAME_AREA, ITEMLOT, NPC, SP_EFFECT);
  - `param\GameParam\GameParam.parambnd.dcx`: 1;
  - `msg\ENGLISH\menu.msgbnd.dcx`: 1 (used only by FogMod's `.bak` loop).
- 60 files in total, about 5.1 MB. `fogdist\` is DS3-only, and `dist\DS1\`
  is PTDE-only. Neither is needed.
- `SoulsIds.GameSpec` DS1R `LayoutDir` is `dist\DS1R\Layouts`
  (CWD-relative), so `ItemReader`'s `LoadLayouts()` reads the data root, not
  staging. `NameDir` (`dist\DS1R\Names`) is only used by the debug
  `dumpcols` flag and is not required.

The data root must be **version-locked to the engine commit**. `fog.txt`
annotations and `dist\DS1R` templates must match the compiled code. A FogMod
Nexus release is not a substitute unless its files hash-match the pin.
Pin evidence (Git blob IDs, same method as `engines/combined/vendor/UPSTREAM_PIN.md`):

```sh
git -C "$LOCALAPPDATA/Temp/fogmod-src" ls-files -s dist/fog.txt dist/DS1R
```

**Open user decision (licensing, `docs/LICENSING.md`):** `dist\DS1R` is
FromSoftware-derived data (MSB/EMEVD/param/ESD/FMG with FogMod edits).
Apache-2.0 covers FogMod's code, not that data. The options are:

1. commit it into the repo and bundle it;
2. have the user provision it: the app verifies a user-supplied FogMod
   download against the pinned hash manifest;
3. fetch it at the pinned commit on first use and verify the hashes.

The host implementation does not depend on this choice: `fogDataRoot` is an
input. For local development and tests, use the temp clone above; do not
commit `dist\DS1R` until the user decides.

## 5. Repository layout to create

```text
tools/fog-host/
  FogHost.csproj                     # SDK-style console, net8.0 (see §7)
  FogHost.sln                        # optional
  src/
    Program.cs                       # entry: `generate --request --response`
    HostRequest.cs / HostResponse.cs # schema-v2 DTOs
    HostErrors.cs                    # code table, message-prefix classifier
    HostRunner.cs                    # DS1-only reimplementation of Randomize (§6)
    Staging.cs                       # safety checks, .bak cleanup, output hashing
    EmevdMerge.cs                    # three-way merge (FOGHOST_INTEGRATION.md rules 1–4)
    Invariants.cs                    # post-write invariants 1–4
  vendor/
    UPSTREAM_PIN.md                  # commits, blob IDs, file list, patch list
    FogMod/                          # the §2 files at 28711c3, plus LICENSE
    SoulsIds/                        # the §2 files at e31a6fd, plus LICENSE
    SoulsFormats/                    # only if candidate (b) or (c); plus LICENSE
tools/fog-host.tests/                # xUnit, or a console self-test; see §10
```

- `.gitignore` gotchas:
  - The root rule `dist/` ignores **every** directory named `dist`. Do not
    put FogMod data under a path segment named `dist` inside the repo
    without a negation rule. The data root itself must contain `dist\` (§4),
    so a committed data root would need `!tools/fog-host/data/dist/`.
  - `bin/` and `obj/` are only ignored under `engines/*/vendor/**`. Add
    `tools/fog-host/**/bin/` and `tools/fog-host/**/obj/`.
- The backend probe (`apps/backend/src/core/pipeline.ts:25-26`) expects
  `tools/fog-host/FogHost.csproj` and
  `tools/fog-host/bin/Release/net8.0/FogHost.exe`. If the TFM differs, update
  both the probe and `engines/fog/README.md`.
- Apache-2.0 §4(b): every modified FogMod file carries a prominent change
  notice at the top, such as
  `// Modified by ds1-triple-randomizer: <summary>. Original: thefifthmatt/FogMod@28711c3.`
  The patch set stays confined to `GameDataWriter.cs` (and `ItemReader.cs`
  only if ever needed; the current rules need no `ItemReader` change).
- Update `THIRD_PARTY_NOTICES.md` (SoulsIds row, SoulsFormats revision,
  YamlDotNet, Newtonsoft.Json) and `engines/fog/README.md`.

## 6. Host program flow

The host does **not** call `Randomizer.Randomize`. For DS1 it returns before
the writer when `dryrun` is set, so the dry-run merge check would be
impossible. Instead, `HostRunner` reimplements its DS1 branch
(`Randomizer.cs:15-66`, about 20 lines), which also yields the design's
progress stages:

1. `validate-request`: parse JSON (camelCase, reject unknown or missing
   keys), and check the following:
   - `schemaVersion == 2` and `game == "DS1R"`;
   - `seed` fits uint32;
   - all 13 option keys are present;
   - `language` is in the `FogLanguage` set;
   - absolute paths;
   - `stagingDirectory` has no `DarkSoulsRemastered.exe`
     (`FOG_STAGING_UNSAFE`);
   - the data-root layout from §4 is present (`FOG_DATA_MISSING`);
   - the `compatibility` hashes match (`FOG_REQUEST_INVALID`).
2. Set `CultureInfo.DefaultThreadCurrentCulture` and the current thread
   culture to `InvariantCulture`. Register
   `CodePagesEncodingProvider.Instance` (on .NET Core, SoulsFormats needs
   Shift-JIS). Save the original stdout, then redirect `Console.Out` and
   `Console.Error` to `logPath`. Set
   `Environment.CurrentDirectory = fogDataRoot` and restore it in `finally`.
3. Build the options:
   - `opt = new RandomizerOptions { Game = FromGame.DS1R, Seed = unchecked((int)seed), Language = language }`;
   - map the 13 option flags (table in `FOGHOST_INTEGRATION.md`);
   - keep `v5`;
   - never set debug flags.
4. `load-annotations`: deserialize `dist\fog.txt` with YamlDotNet into
   `AnnotationData`, `ann.SetGame(FromGame.DS1R)`, then
   `new Graph().Construct(opt, ann)`.
5. `read-key-items`: `new ItemReader().FindItems(opt, ann, g, null, stagingDirectory, FromGame.DS1R)`.
   This sets `keyItems.randomized` and `keyItems.hash` (`ItemHash`).
6. `solve-graph`: `new GraphConnector().Connect(opt, g, ann)`.
7. `write-game-data`: `new GameDataWriter().Write(opt, ann, g, stagingDirectory, FromGame.DS1R)`
   with the host patch set (§8). In `dryRun`, pass a compute-only flag that
   runs the merge and conflict checks but skips every write.
8. Post-write invariants (`write` only), `.bak` cleanup in staging, then
   `hash-outputs`: sort the file list ordinally, use forward slashes, and
   include no absolute paths or timestamps. Response `fogOptions` is
   `opt.ToString()`. `annotationSha256` is the SHA-256 of `dist\fog.txt`.
9. Write `<response>.tmp` and rename it. Exit 0 for success **and** for a
   typed failure. Only a crash exits non-zero.

Progress lines go to the saved original stdout, one JSON object per line:
`{"type":"progress","stage":"…","message":"…"}`.

## 7. Target framework and determinism

- Target **net8.0** first (one runtime with `DsrDataTool`; the backend probe
  already expects `net8.0`). Fall back to net48 only if the SoulsFormats
  candidate cannot build for netstandard2.1 or net8.0.
- Keep `<CheckForOverflowUnderflow>` off. FogMod computes `opt.Seed - 1`
  and `opt.Seed + tries` unchecked.
- `System.Random(int)` with an explicit seed uses the same legacy algorithm
  on .NET Framework and .NET 8.
- String hashing is randomized per process on .NET Core. The only hash
  override is `Graph.Connection.GetHashCode` (`Graph.cs:507`, used as a
  `Dictionary`/`HashSet` key in `Graph.cs:395,425` and
  `GraphConnector.cs:526`). Hash values do not change enumeration order
  (entries are kept in insertion order), so this is low risk. The
  two-process determinism test (§10) is still the authority.
- Set `<Deterministic>true</Deterministic>` and `<Nullable>disable</Nullable>`
  for vendored sources, and `<LangVersion>latest</LangVersion>`. FogMod uses
  C# 7-era syntax.

## 8. Patch set in `GameDataWriter.cs` (line numbers at `28711c3`)

| Rule (`FOGHOST_INTEGRATION.md`) | Location | Change |
|---|---|---|
| Compute-only for `dryRun` | 68 (`SFUtil.Backup`), 1074 (`evd.Write`), 1082 (MSB write), 1085 (param `OverrideBnd`), 1089 (ESD `File.Copy`), 1097 (msg `OverrideBnd`) | Guard each write with the host flag. |
| NpcParam "restore base ID" skip | 165-197 | `continue` for enemies whose `NPCParamID` is a preserved `id`. |
| NpcParam delete pass keeps preserved | 198 | Add `&& !preserved.Contains(id)` to the `RemoveAll` predicate. |
| Scaling of preserved rows | 200-429 | **Decision needed; see §9.** |
| EMEVD three-way merge | 945-1075 (per-file loop) | After FogMod's carry-over and its own edits, and before `evd.Write` (1074): apply merge rules 1–4 for files in `combinedFiles`. B comes from `compatibility.baseDirectory` and C from staging. Note that FogMod reads `gameEvd` from staging, which is already C. |

Event equality for the merge compares `ID`, `RestBehavior`, and every
instruction (`Bank`, `ID`, `Layer`, `ArgData` bytes) and parameter. Compare
structurally, not by file bytes: C was written by `DsrDataTool`'s
SoulsFormats line, so byte-level layout can differ.

## 9. Findings on the design's open items

**Open item 8 (FogMod `scale`): resolved as fact. The design's composition
hypothesis is wrong as written.** Source: `GameDataWriter.cs:157-456`.

- FogMod does not edit combat stats. It adds SpEffect "rank" rows
  7200–7399 (`SpEffectParam`; 5 damage tiers × 41 ratios, derived from
  vanilla rank 7015). It then writes one rank SpEffect into the NpcParam row:
  - `spEffectID4` if `row.ID >= 120000`;
  - otherwise the first zero `spEffectID0..7`.
  It also rescales `getSoul`, or `GameAreaParam.bonusSoul_*` for rows with
  `getSoul == 0`.
- Enemies are grouped by `NPCParamID` and then by logical area
  (`fog.txt` `Enemies` cols).
  - Groups whose ratio is 1 are skipped.
  - **Rows whose ID is not vanilla are skipped** (`:317-320`, "If it's not
    from the base game, don't otherwise touch it").
  - If an ID occurs in one area, the row is edited **in place**.
  - If it occurs in several areas, FogMod **creates a new row**: the first
    free ID ≥ the original, cloned from the **vanilla** row, with the
    original ID hint-encoded in `pcAttrB/W/L/R`. It then **re-points those
    MSB parts' `NPCParamID`** (`:363-385`).
- Consequences:
  1. Combined scaled rows (IDs `9_100_000+` and `9_200_000+`,
     `generator.js:1163,1300`) are never scaled by stock FogMod, even when
     they are preserved. The "composed scaling" rule is not what happens.
  2. A naive `baseId` patch would hit the `row.ID >= 120000` branch for
     every scaled row and overwrite `spEffectID4`. That slot holds the
     destination's SpEffect, which `AddScaledNpcRows` copies. The branch
     would have to key on `baseId`.
  3. **Invariant 1 as written fails on almost every `scale` run.** Vanilla-ID
     enemies in combined MSBs that span more than one area are legitimately
     re-pointed to FogMod clone rows.
  4. Invariant 3 ("changed cells equal the changes FogMod made to its
     `baseId` row") has no counterpart in FogMod's model, because changes are
     per (ID, area) group, not per row.
  5. FogMod's clones copy the **vanilla** row, so a combined edit to a
     vanilla NpcParam row would be lost in a clone. Check whether
     `DsrDataTool` edits vanilla NpcParam rows in place. It adds rows, but
     confirm it before relying on this.
- **Recommended v1 rule (needs user approval; it changes the Task 11
  decision in `DECISIONS.md`):**
  - Leave preserved rows unscaled by FogMod, which is the stock skip.
  - Narrow invariant 1: parts that reference a preserved ID must keep it.
    Other parts may be re-pointed only to a FogMod clone row, one whose ID
    is not in the input and whose `pcAttr` hint decodes to the part's input
    ID.
  - Replace invariant 3 with "preserved rows are identical to the input".
  - Consequence: with Fog `scale` on, randomized enemies keep the combined
    engine's destination-slot normalization but get no fog-graph tier.
  - Alternative: a real composition patch in the scale loop. For preserved
    rows, key the lookup on `baseId`, use the `>= 120000` branch by
    `baseId`, and clone from the preserved row, not the vanilla one. This
    is more code in the loop FogMod tunes by hand (Quelaag special case at
    `:333`).

**Open item 6 (does FogMod remove enemy parts?): resolved, no.** At this
commit the writer removes only regions named `Boss start for `/`FR: `/`BR: `/
`Region for `, player parts `c0000_0050+` (`:82-99`), and collisions
`h9950B1`/`h7800B1` in specific maps. It adds players, objects, regions and
connect-collisions. It never adds or removes `Parts.Enemies` and never
changes enemy `ModelName`/`ThinkParamID`. Enemy `NPCParamID` changes only via
the restore pass (`:165-197`) and scale clones (see above).

**Open item 5 (`11810310`): mechanism confirmed, conflict set not measured.**
FogMod takes `11810310` from its **template** (`dist\DS1R\event\m18_01_00_00.emevd.dcx`).
If the staging copy lacks instruction `2003[18]`, it treats the Asylum Demon
as randomized (`asylumDemonRandomized`, `:965-968`) and removes every
`2003[18]` and `2004[41]` from the template's event (`:1010-1013`). With
merge rule 3 the outcome depends on the bytes. First implementation step:
write a small EMEVD diff tool (host test utility) that lists, for each of the
18 event files, the event IDs where template ≠ vanilla (FogMod edits) and
where combined ≠ vanilla (from a real package). Their intersection is the
real both-sides conflict set. Record it in `FOGHOST_INTEGRATION.md`.

**Other writer facts relevant to the bridge:**
- `GameAreaParam` is replaced wholesale by FogMod's `dist` copy (`:164`).
- `SpEffectParam` 7200–7399 is cleared on every run (`:157`).
- ESDs are plain copies.
- Only `common.emevd` event 0 receives FogMod's own `2000[00]`
  initializations (`:1015-1031`). In `m14_01_00_00` with `bboc`, FogMod
  re-targets inits `11410200/201` to `+20`.
- `copyRandoEvents` (`:928-944`) copies instructions from the staging file
  for 8 event IDs. If the combined engine edits one of them, T == C and rule 3
  keeps it.

## 10. Test plan

Data needed: the temp FogMod clone (data root), a pristine DS1R read set, and
a real combined package. The Task 8/16 harnesses show how to build those
under the OS temp directory without touching the live install
(`apps/backend/scripts/combinedEngineE2eProof.mjs`, `releaseAuditE2e.mjs`).

1. **Unit/fixture (no game data):**
   - request validation: each `FOG_REQUEST_INVALID` branch, unknown and
     missing option keys, uint32 bounds, non-`DS1R` game;
   - `FOG_STAGING_UNSAFE` when the staging directory contains
     `DarkSoulsRemastered.exe`;
   - `FOG_DATA_MISSING` for each required data-root path;
   - the error classifier maps every message prefix in the
     `FOGHOST_INTEGRATION.md` table;
   - atomic response write;
   - progress lines are valid JSON;
   - the response round-trips through the backend `parseResponse`
     (`fogHostProcess.ts:101`), for example with a Node test that spawns
     the built host with an invalid request.
2. **Stock parity (pristine staging, empty `compatibility`):** `write` output
   matches upstream FogMod behavior for the same seed and options. No
   `.bak` files are left. Nothing is written outside staging or `logPath`.
3. **`dryRun`:** the staging tree is byte-identical before and after, and
   `files` is empty.
4. **Determinism:** two separate host processes with the same request and
   identical staging produce byte-identical responses and file hashes. Seeds
   `0`, `0x7FFFFFFF`, `0x80000000` and `0xFFFFFFFF`, with `scale` on and off.
5. **Combined staging (bridge):**
   - preserved NpcParam rows survive;
   - the merge appends custom events and event-0 inits;
   - invariants 1–4 pass (as amended by §9);
   - a manufactured conflict yields `FOG_MERGE_CONFLICT`;
   - a key item moved into a shop yields `FOG_UPSTREAM_INCOMPATIBLE`;
   - a real both-sides event (§9, `11810310`) is observed.
6. **Backend integration:** after the host builds, the
   `getFogPrerequisites` default in
   `apps/backend/src/core/generationPipeline.ts:60` still returns `null`.
   Wiring a real provider (executable, verified data root, `readSet` from
   `fogReadSet`) is a separate step and must be in the task's scope
   explicitly.

## 11. Build commands

The .NET 8 SDK lives outside the repo at `C:\Users\Adam\dn8sdk`
(`DECISIONS.md`, "build with a local SDK outside the repo path"). The
machine-wide `C:\Program Files\dotnet` has runtimes only.

```sh
~/dn8sdk/dotnet restore tools/fog-host/FogHost.csproj
~/dn8sdk/dotnet build   tools/fog-host/FogHost.csproj -c Release
~/dn8sdk/dotnet test    tools/fog-host.tests -c Release   # if xUnit is used
```

Output: `tools/fog-host/bin/Release/net8.0/FogHost.exe` (framework-dependent;
the machine has `Microsoft.NETCore.App 8.0.28`). Do not run
`dotnet build` from the repo root. There is no `.sln` there, and the
`DsrDataTool` build is separate.

## 12. Build probe result

Pending. A throwaway probe outside the repo was run in the same session. Its
result is recorded below when available.

## 13. Blockers and user decisions

1. **Plan task missing.** No numbered task in `docs/AI_EXECUTION_PLAN.md`
   builds FogHost, and Task 17 (Codex) is marked `ACTIVE` in the working
   tree. The user must insert a FogHost task (proposed text below) and
   decide whether Task 17 waits for it.
2. **Scaling rule (§9)**: approve the recommended v1 rule or require a real
   composition patch. Either way, `DECISIONS.md` and invariants 1 and 3 in
   `FOGHOST_INTEGRATION.md` change.
3. **FogMod data-root distribution (§4)**: bundle, user-provision, or
   fetch-and-verify.
4. **SoulsFormats revision (§3)**: prefer one with GPL text in the tree.
5. **Public GPL fork**: unchanged, required before any binary distribution.
6. **Boss randomization + Fog**: unproven until the §9 conflict set is
   measured.

### Proposed plan entry (for the user to place)

```text
# TASK 16a — CLAUDE
State: PENDING
## Build FogHost (C# host around pinned FogMod)
Implement tools/fog-host per docs/FOGHOST_IMPLEMENTATION_CHECKLIST.md
§§2–8: vendor FogMod 28711c3 engine sources, SoulsIds e31a6fd and the chosen
SoulsFormats; the schema-v2 JSON boundary; the GameDataWriter.cs patch set
with the approved scaling rule; .bak cleanup, invariants, output hashing.
Do NOT: commit FogMod dist data unless the user decided to bundle it; wire
getFogPrerequisites; touch the live game; change the backend contract.
Verification: checklist §10 items 1–5.
```
