# FogHost implementation checklist

Prepared 2026-09-28 as the handoff for the task that builds the C# FogHost.

> **Status: implemented** as FogHost v1 (`tools/fog-host`). This checklist is kept as the
> research record. Differences found while implementing (for example 54 written files,
> not 55, and backups disabled rather than cleaned) are in `docs/FOGHOST_INTEGRATION.md`,
> "FogHost v1 implementation".

- Design: `docs/FOGHOST_INTEGRATION.md`.
- Backend contract: `apps/backend/src/adapters/fogHostProcess.ts` (schema v2)
  and `fogCompatibility.ts`.

This document adds the facts that the design left open. They were verified
against the source and **by a throwaway build and run outside the
repository**. It also gives the exact build and test steps. No host code is
in the repository yet.

Read before starting: `AGENTS.md`, this file, `docs/FOGHOST_INTEGRATION.md`
("Host design", "Compatibility bridge (Task 11)"), `docs/LICENSING.md`.

## 0. Headline findings

1. **The audited FogMod pin `28711c3` cannot generate DS1R.** Its "DS3
   update" broke the DS1 path in two places:
   - `Entrance.FullName` became `Area + "_" + ID`, while DS1 annotations
     and the DS1 writer look entrances up by name (`o2920_0000`,
     `o5869_0000`, `1702901`). The result is a `KeyNotFoundException` in
     `Graph.Construct`.
   - Even with that patched, `GraphConnector.Connect` only exits its loop
     inside a DS3-only branch (`g.Areas.ContainsKey("firelink_cemetery")`),
     so every DS1 seed throws `Could not find edge into unreachable areas []`.

   Both failures were observed at runtime on 10 seeds. The DS1 path is
   already broken at `76ab1ef` (2020-07, first DS3 commit).
2. **Use FogMod `6e6dc78` instead** (2020-01-21, "Enemy randomizer
   support"), the last DS1-only commit. Its `dist/DS1R` tree is identical to
   `28711c3`'s, and its `GameDataWriter.cs` has the same logic. Only
   identifier renames and 6 SoulsFormats API names differ, and the line
   numbers are the same. So the Task 9/11 findings (event carry-over,
   NpcParam passes, scaling) and the line numbers below hold for `6e6dc78`.
3. **Build proven:**
   - `6e6dc78` + SoulsIds `e31a6fd` + NuGet `JuicerMV.SoulsFormats`
     0.1.3 (the same package `DsrDataTool` uses) + a 6-rule API port of
     `GameDataWriter.cs` builds on **net8.0**.
   - It runs a full DS1R `dryrun` and `write`.
   - Two separate processes give byte-identical output.
   - See §12.
4. FogMod `scale` does not compose with the combined engine's scaled rows as
   Task 11 assumed. That needs a user decision (§9).

## 1. Pinned sources

| Component | Repository | Commit | License |
|---|---|---|---|
| FogMod engine + data | `https://github.com/thefifthmatt/FogMod` | **`6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f`** (2020-01-21) | Apache-2.0, root `LICENSE` at that commit (blob `e454a525…`, same text later moved to `FogMod/LICENSE`). No NOTICE file. |
| SoulsIds | `https://github.com/thefifthmatt/SoulsIds` | `e31a6fdb516491b6c731cb2847f22faaabbcf925` (2022-02-15) | Apache-2.0 (root `LICENSE`), verified |
| SoulsFormats | NuGet `JuicerMV.SoulsFormats` | `0.1.3` (nuspec: source `juicermv/SoulsFormats@c3f100941fa67756407e08b2793381edaabd466f`) | `GPL-3.0-only` (nuspec license expression). Already listed in `THIRD_PARTY_NOTICES.md`. |
| YamlDotNet | NuGet | `7.0.0` (FogMod `packages.config`) | MIT |
| Newtonsoft.Json | NuGet | **`13.0.3`**. SoulsIds pins `12.0.3`, which has advisory GHSA-5crp-9r3c-p9vr (High). 13.0.3 builds and runs in the probe, with no NU1903. | MIT |

- **SoulsIds `e31a6fd`:** SoulsIds had no commit contemporaneous with
  `6e6dc78` that compiles against the modern SoulsFormats. `e31a6fd` compiles
  with both FogMod commits and JuicerMV. Later SoulsIds commits target
  `SoulsFormatsNEXT`; do not use SoulsIds `master`.
- **SoulsFormats rejected alternatives:** thefifthmatt's fork `106e54a` has
  no license text. JKAnderson's repository had only a joke license
  (`44ffed4`) until the GPL-3.0 `LICENSE` in `9c0da6b` (2025). JuicerMV
  avoids both problems.
- **FogMod `28711c3`** may still be kept for reference only; do not vendor
  it.

Local research clones (temporary; re-create them if they are gone):

```sh
T="$LOCALAPPDATA/Temp"
git clone https://github.com/thefifthmatt/FogMod "$T/fogmod-src"        # use: git -C ... archive 6e6dc78
git clone https://github.com/thefifthmatt/SoulsIds "$T/foghost-deps/SoulsIds"
```

The probe projects from this session are `$T/fhp2` (working, `6e6dc78`) and
`$T/fhp` (`28711c3`, fails at runtime). They are scratch, not deliverables.

## 2. Files the host needs

**FogMod at `6e6dc78`**, compiled into the host (`git archive 6e6dc78 FogMod LICENSE`):

- Files: `AnnotationData.cs`, `GameDataWriter.cs` (API-ported, §5),
  `Graph.cs`, `GraphChecker.cs`, `GraphConnector.cs`, `ItemReader.cs`,
  `Randomizer.cs`, `RandomizerOptions.cs`, `Util.cs`, plus the root `LICENSE`.
  This commit has no DS3 files. None of these reference WinForms.
- Excluded: `Program.cs`, `MainForm*.cs`, `*.resx`, `Properties/**`,
  `App.config`, `packages.config`, `FogMod.csproj`, `foggate.ico`.
- API at this commit, which differs from `28711c3`:
  - the annotation class is `Annotations`;
  - `RandomizerOptions` has no `Game` property, and its default flag is
    **`v4`** (not `v5` as `FOGHOST_INTEGRATION.md` says);
  - `Randomizer.Randomize(opt, game, gameDir)` takes three arguments;
  - `ItemReader.FindItems(opt, ann, g, gameDir, game)`;
  - `GameDataWriter.Write(opt, ann, g, gameDir, game)`;
  - `fog.txt` is read as `"dist/fog.txt"` relative to the CWD.

**SoulsIds at `e31a6fd`** (`SoulsIds/`): `EMEDF.cs`, `Events.cs`,
`GameEditor.cs`, `GameSpec.cs`, `Scraper.cs`, `Universe.cs`, plus the root
`LICENSE`. Compile the sources directly. The upstream csproj is old-style
net461 with a WinForms reference; do not use it. They built unchanged in the
probe.

## 3. Option flags (`6e6dc78`)

All 13 contract flags exist as option names at this commit: `world boss warp
major minor lordvessel start unconnected bboc scale hard lords pacifist`. The
mapping table in `FOGHOST_INTEGRATION.md` holds. Keep the default `v4`. The
response `fogOptions` string will contain `v4`.

## 4. FogMod data root

`Randomizer`/`GameDataWriter` read paths relative to the **process working
directory**:

- `dist/fog.txt`: at `6e6dc78` this is blob
  **`83beace06434ce98e33eaeecde86f17c810dd096`**. It differs from `28711c3`
  by one line (`kiln_gwyn` `Tags: boss` vs `boss end`). Use the `6e6dc78`
  version with the `6e6dc78` code.
- `dist/DS1R/**`: 59 files, tree **`bc86fb5aee292c653bbddce26bdfc003182ed1f0`**
  (identical at both commits):
  - `event/*.emevd.dcx`: 18 (`common` plus 17 maps);
  - `map/MapStudio/*.msb`: 18 (m10_00, m10_01, m10_02, m11_00, m12_00,
    m12_00_00_01, m12_01, m13_00, m13_01, m13_02, m14_00, m14_01, m15_00,
    m15_01, m16_00, m17_00, m18_00, m18_01);
  - `script/talk/*.talkesdbnd.dcx`: 17;
  - `Layouts/*.xml`: 4;
  - `param/GameParam/GameParam.parambnd.dcx`: 1;
  - `msg/ENGLISH/menu.msgbnd.dcx`: 1 (only used by FogMod's `.bak` loop).
- 60 files in total, about 5.1 MB. `dist/DS1/` (PTDE) and `dist/Names/` are
  not needed.
- SoulsIds' DS1R `LayoutDir` is `dist\DS1R\Layouts` (CWD-relative), so
  `LoadLayouts()` reads the data root, never staging.

The data root must be **version-locked** to the engine commit. A FogMod
Nexus download is acceptable only if its files hash-match. Pin evidence uses
Git blob IDs, the same method as `engines/combined/vendor/UPSTREAM_PIN.md`:

```sh
git -C "$LOCALAPPDATA/Temp/fogmod-src" ls-tree -r 6e6dc78 dist/fog.txt dist/DS1R
```

When verifying files on disk against those IDs, use
`git hash-object --no-filters <file>` or a raw SHA-256. The machine's global
`core.autocrlf=true` makes a plain `git hash-object` rewrite line endings in
the `Layouts/*.xml` and `fog.txt` input, which produces false mismatches.
`git archive 6e6dc78 dist/fog.txt dist/DS1R` extracts byte-identical files.

**Open user decision (licensing, `docs/LICENSING.md`):** `dist/DS1R` is
FromSoftware-derived data (MSB/EMEVD/param/ESD/FMG with FogMod edits).
Apache-2.0 covers the code, not that data. The options are:

1. commit and bundle it;
2. have the user provision it, and the app verifies it against the pinned
   blob/SHA-256 manifest;
3. fetch it at the pinned commit on first use and verify it.

The host does not depend on the choice (`fogDataRoot` is an input). For
development and tests, use the temp clone. Do not commit `dist/DS1R` before
the user decides.

## 5. Repository layout to create

```text
tools/fog-host/
  FogHost.csproj                     # SDK-style console, net8.0
  src/
    Program.cs                       # entry: `generate --request --response`
    HostRequest.cs / HostResponse.cs # schema-v2 DTOs
    HostErrors.cs                    # code table, message-prefix classifier
    HostRunner.cs                    # DS1 reimplementation of Randomize (§6)
    Staging.cs                       # safety checks, .bak cleanup, hashing
    EmevdMerge.cs                    # three-way merge (FOGHOST_INTEGRATION.md rules 1–4)
    Invariants.cs                    # post-write invariants
  vendor/
    UPSTREAM_PIN.md                  # commits, blob IDs, file list, patch list
    FogMod/                          # §2 files at 6e6dc78, plus LICENSE
    SoulsIds/                        # §2 files at e31a6fd, plus LICENSE
tools/fog-host.tests/                # xUnit (or a console self-test), §10
```

**Mandatory API port of `GameDataWriter.cs`** (this is what made the probe
build). Every rule is an exact textual replacement:

| From (`6e6dc78`, 2019 SoulsFormats API) | To (JuicerMV 0.1.3) |
|---|---|
| `layouts["SP_EFFECT_PARAM_ST"]` | `Params["SpEffectParam"].AppliedParamdef` |
| `layouts["NPC_PARAM_ST"]` | `Params["NpcParam"].AppliedParamdef` |
| `cell.Name` | `cell.Def.InternalName` |
| `MSB1.Shape.` | `MSB.Shape.` |
| `.UnkT0C = ` | `.InitAnimID = ` |
| `con.Placeholder = null;` | removed. Upstream `28711c3` does the same: "No longer valid after SoulsFormat update". |

These are the same replacements upstream made in `28711c3`. The build emits
28 × CS0612 (obsolete `PARAM.Layout` in SoulsIds/ItemReader); suppress them
in the csproj.

Also required:

- **Apache-2.0 §4(b):** every modified FogMod file carries a prominent change
  notice at the top, such as
  `// Modified by ds1-triple-randomizer: <summary>. Original: thefifthmatt/FogMod@6e6dc78.`
  The patch set stays confined to `GameDataWriter.cs`. `ItemReader.cs` needs
  no change under the current rules.
- **csproj:** set `<EnableDefaultItems>false</EnableDefaultItems>`. Otherwise
  vendored `*.resx` files are globbed in and the build fails with MSB3822.
  Also set `<Nullable>disable</Nullable>` and `<LangVersion>latest</LangVersion>`,
  and leave `<CheckForOverflowUnderflow>` off, because FogMod uses unchecked
  `Seed - 1` and `Seed + tries`.
- **`.gitignore` gotchas:**
  - The root rule `dist/` ignores **every** directory named `dist`. A
    committed data root would need `!tools/fog-host/data/dist/`.
  - `bin/` and `obj/` are only ignored under `engines/*/vendor/**`. Add
    `tools/fog-host/**/bin/` and `tools/fog-host/**/obj/`.
- **Backend probe:** `apps/backend/src/core/pipeline.ts:25-26` expects
  `tools/fog-host/FogHost.csproj` and
  `tools/fog-host/bin/Release/net8.0/FogHost.exe`. net8.0 is confirmed, so no
  change is needed.
- **Docs to update:** `THIRD_PARTY_NOTICES.md` (FogMod commit, SoulsIds,
  YamlDotNet, Newtonsoft.Json), `engines/fog/README.md`, and `DECISIONS.md`
  (the FogMod re-pin from `28711c3` to `6e6dc78`, with the §0 evidence).

## 6. Host program flow

The host does **not** call `Randomizer.Randomize`. For DS1 it returns before
the writer when `dryrun` is set, so the dry-run merge check would be
impossible. `HostRunner` reimplements its DS1 body (`Randomizer.cs` at
`6e6dc78`, lines 14-36, about 15 lines):

1. **`validate-request`:** parse the JSON (camelCase; reject unknown and
   missing keys) and check the following:
   - `schemaVersion == 2` and `game == "DS1R"`;
   - `seed` fits uint32;
   - all 13 option keys are present;
   - `language` is in the `FogLanguage` set;
   - paths are absolute;
   - staging has no `DarkSoulsRemastered.exe` (`FOG_STAGING_UNSAFE`);
   - the data-root layout from §4 is present (`FOG_DATA_MISSING`);
   - the `compatibility` hashes match (`FOG_REQUEST_INVALID`).
2. **Process setup:**
   - `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`;
   - InvariantCulture for the default thread culture and the current culture;
   - save the original stdout;
   - `Console.SetOut`/`SetError` to `logPath`;
   - `Environment.CurrentDirectory = fogDataRoot`, restored in `finally`.
3. **Options:** `opt = new RandomizerOptions { Seed = unchecked((int)seed) }`
   and `opt.Language = language`. Set the 13 flags, keep `v4`, and never
   set debug flags.
4. **`load-annotations`:** YamlDotNet-deserialize `dist/fog.txt` into
   `Annotations`, then `new Graph().Construct(opt, ann)`.
5. **`read-key-items`:** `new ItemReader().FindItems(opt, ann, g, stagingDirectory, FromGame.DS1R)`,
   which yields `keyItems.randomized` and `keyItems.hash`.
6. **`solve-graph`:** `new GraphConnector().Connect(opt, g, ann)`.
7. **`write-game-data`:** `new GameDataWriter().Write(opt, ann, g, stagingDirectory, FromGame.DS1R)`
   with the §8 patch set. In `dryRun`, pass a compute-only flag.
8. **Finish (`write` only):**
   - check the post-write invariants;
   - delete the `.bak` files. The probe produced **55 `.bak`**, one per
     written file;
   - **`hash-outputs`:** relative paths, forward slashes, sorted
     ordinally, no absolute paths or timestamps.

   Set `fogOptions = opt.ToString()` and
   `annotationSha256 = SHA-256(dist/fog.txt)`.
9. **Response:** write `<response>.tmp`, then rename it. Exit 0 for success
   and for a typed failure; exit non-zero only on a crash.

Progress lines go to the saved stdout, one JSON object per line:
`{"type":"progress","stage":"…","message":"…"}`.

### Error classifier at `6e6dc78`

The error table in `FOGHOST_INTEGRATION.md` was read at `28711c3`. The
`throw` sites at `6e6dc78` are:

| Message prefix | File | Code |
|---|---|---|
| `Couldn't find … in item lots`, `Item … found in both …` | `ItemReader.cs:72,43` | `FOG_UPSTREAM_INCOMPATIBLE` (`combined`) |
| `Couldn't solve seed …`, `Ran out of eligible edges`, `Could not find edge into unreachable areas …`, **`Bad seed: Can't find edge to self-link …`** (new here) | `GraphConnector.cs:249,58,141,225,240` | `FOG_SEED_UNSOLVABLE` (`fog`) |
| `Internal error…`, `Connected edge still left…`, `No outgoing edge found to change` | `GraphConnector.cs` | `FOG_ENGINE_FAILED` |

`No swappable edge found to inaccessible areas…` does **not** exist at
`6e6dc78`; drop it from the classifier. The key-item rule is unchanged:
`ItemReader` scans only `ann.LotLocations` (`:24`), and `randomonly` and
`hard` (without `opt["hard"]`) are exempt (`:64-72`).

## 7. Target framework and determinism

- **net8.0 confirmed** (§12). `System.Random(int)` is the legacy seeded
  algorithm on .NET 8.
- String hashes are randomized per process. At `28711c3` the only override
  is `Graph.Connection.GetHashCode`. `Dictionary`/`HashSet` enumeration order
  does not depend on hash values. The two-process test is the authority; it
  passed in the probe.

## 8. Patch set in `GameDataWriter.cs` (line numbers valid at `6e6dc78` and `28711c3`)

| Rule (`FOGHOST_INTEGRATION.md`) | Location | Change |
|---|---|---|
| Compute-only for `dryRun` | 68 (`SFUtil.Backup`), 1074 (`evd.Write`), 1082 (MSB write), 1085 (param `OverrideBnd`), 1089 (ESD `File.Copy`), 1097 (msg `OverrideBnd`) | Guard each write with the host flag. |
| NpcParam "restore base ID" skip | 165-197 | `continue` for enemies whose `NPCParamID` is a preserved `id`. |
| Keep preserved rows in the delete pass | 198 | Add `&& !preserved.Contains(id)` to the `RemoveAll` predicate. |
| Scaling of preserved rows | 200-429 | **Decision needed; see §9.** |
| EMEVD three-way merge | 945-1075 (per-file loop) | After FogMod's carry-over and its own edits, and before `evd.Write` (1074), apply merge rules 1–4 to files in `combinedFiles`. B comes from `compatibility.baseDirectory` and C from staging. FogMod reads `gameEvd` from staging, so that copy is C. |

Event equality for the merge compares `ID`, `RestBehavior`, and each
instruction (`Bank`, `ID`, `Layer`, `ArgData` bytes) and parameter.
Compare structurally, not file bytes.

## 9. Findings on the design's open items

**Open item 8 (FogMod `scale`): resolved as fact. The Task 11 composition
hypothesis is wrong.** Source: `GameDataWriter.cs:157-456`.

**What FogMod's scaling does:**

- It does not touch combat stats. It adds SpEffect "rank" rows 7200–7399
  (5 damage tiers × 41 ratios, based on vanilla rank 7015). It then writes
  one rank SpEffect into the NpcParam row:
  - `spEffectID4` if `row.ID >= 120000`;
  - otherwise the first zero `spEffectID0..7`.
- It rescales `getSoul`, or `GameAreaParam.bonusSoul_*` when `getSoul == 0`.
- It groups enemies by `NPCParamID` and then by logical area (`fog.txt`
  `Enemies`). Groups with ratio 1 are skipped.
- It **skips rows whose ID is not vanilla** (`:316`).
- An ID used in one area is edited **in place**.
- An ID used in several areas gets a **new row**:
  - the ID is the first free one ≥ the original;
  - the row is cloned from the **vanilla** row;
  - the original ID is stored in `pcAttrB/W/L/R`;
  - those MSB parts' **`NPCParamID` is re-pointed** to the new row
    (`:365-385`).

**Consequences:**

1. The combined scaled rows (IDs `9_100_000+`/`9_200_000+`,
   `generator.js:1163,1300`) are never scaled by stock FogMod.
2. A naive `baseId` patch would take the `row.ID >= 120000` branch and
   overwrite `spEffectID4`, the destination SpEffect that
   `AddScaledNpcRows` copies.
3. **Invariant 1 as written fails on almost every `scale` run**, because
   vanilla-ID enemies spanning several areas are legitimately re-pointed.
4. Invariant 3 has no counterpart in FogMod's model.
5. Clones copy the vanilla row, so a combined in-place edit to a vanilla
   NpcParam row would be lost in a clone. `DsrDataTool` adds rows; confirm
   that it never edits vanilla NpcParam rows in place.

**Recommended v1 rule (needs user approval; it changes the Task 11
decision):**

- Preserved rows stay unscaled by FogMod (the stock skip).
- Invariant 1 narrows: parts that reference a preserved ID keep it. Other
  parts may be re-pointed only to a FogMod clone, meaning an ID that is not
  in the input and whose `pcAttr` hint decodes to the part's input ID.
- Invariant 3 becomes "preserved rows are identical to the input".
- Effect: randomized enemies keep the combined destination-slot
  normalization but get no fog-graph tier.

The alternative is a real composition patch in the scale loop:

- key the lookup and the `>= 120000` branch on `baseId`;
- clone from the preserved row.

**Open item 6 (enemy part removal): resolved, no.** The writer removes only:

- regions named `Boss start for `/`FR: `/`BR: `/`Region for `;
- player parts `c0000_0050+` (`:82-99`);
- collisions `h9950B1`/`h7800B1` in specific maps.

It never adds or removes `Parts.Enemies` and never changes an enemy's
`ModelName`/`ThinkParamID`. `NPCParamID` changes only in the restore pass and
in scale clones.

**Open item 5 (`11810310`): mechanism confirmed, conflict set not measured.**

- FogMod takes `11810310` from its template (`dist/DS1R/event/m18_01_00_00.emevd.dcx`).
- If the staging copy lacks `2003[18]`, it removes all `2003[18]` and
  `2004[41]` from the template event (`:965-968`, `:1010-1013`).
- **First implementation step:** a host test utility that lists, per event
  file:
  - template ≠ vanilla event IDs;
  - combined ≠ vanilla event IDs (from a real package).

  The intersection is the real conflict set; record it in
  `FOGHOST_INTEGRATION.md`.

**Other writer facts:**

- `GameAreaParam` is replaced by FogMod's copy (`:164`).
- SpEffect 7200–7399 is cleared every run (`:157`).
- ESDs are plain copies.
- Only `common` event 0 gets FogMod's own `2000[00]` inits (`:1015-1031`).
- With `bboc`, the `m14_01` inits `11410200/201` are re-targeted to `+20`.
- `copyRandoEvents` (`:928-944`) copies the staging instructions for 8 IDs.

## 10. Test plan

Data needed:

- the FogMod temp clone as the data root;
- a pristine DS1R read set;
- a real combined package.

The Task 8/16 harnesses show how to build these under the OS temp directory
without touching the live install (`apps/backend/scripts/combinedEngineE2eProof.mjs`,
`releaseAuditE2e.mjs`).

1. **Unit/fixture (no game data):**
   - every `FOG_REQUEST_INVALID` branch (unknown or missing keys, uint32
     bounds, non-`DS1R`);
   - `FOG_STAGING_UNSAFE`;
   - `FOG_DATA_MISSING` for each required path;
   - the error classifier for each message prefix;
   - the atomic response write;
   - progress lines are valid JSON;
   - the response is accepted by the backend `parseResponse`
     (`fogHostProcess.ts:101`), via a Node test that spawns the built host.
2. **Stock parity (pristine staging, empty `compatibility`):**
   - no `.bak` left;
   - nothing written outside staging or `logPath`.
3. **`dryRun`:** staging is byte-identical before and after, and `files` is
   empty.
4. **Determinism:** two separate processes give identical responses and
   hashes for seeds `0`, `0x7FFFFFFF`, `0x80000000` and `0xFFFFFFFF`, with
   `scale` both on and off.
5. **Combined staging (bridge):**
   - preserved NpcParam rows survive;
   - custom events and event-0 inits are merged;
   - the invariants hold (as amended in §9);
   - a manufactured conflict gives `FOG_MERGE_CONFLICT`;
   - a key item moved into a shop gives `FOG_UPSTREAM_INCOMPATIBLE`;
   - the `11810310` measurement from §9.
6. **Backend wiring** (a separate, explicitly scoped step): a real
   `getFogPrerequisites` provider in
   `apps/backend/src/core/generationPipeline.ts:60`, supplying the
   executable, the verified data root and `readSet` from `fogReadSet`.

## 11. Build commands

The .NET 8 SDK is outside the repo at `C:\Users\Adam\dn8sdk`
(`DECISIONS.md`). `C:\Program Files\dotnet` has runtimes only
(`Microsoft.NETCore.App 8.0.28`).

```sh
~/dn8sdk/dotnet build tools/fog-host/FogHost.csproj -c Release   # restores NuGet
~/dn8sdk/dotnet test  tools/fog-host.tests -c Release            # if xUnit
```

The output is `tools/fog-host/bin/Release/net8.0/FogHost.exe`
(framework-dependent). NuGet restore needs network access the first time.

## 12. Build probe results (2026-09-28, outside the repository)

| Probe | Result |
|---|---|
| `28711c3` + SoulsIds `e31a6fd` + JuicerMV 0.1.3, net8.0 | Builds, with only CS0612/NU1903 warnings. **Runtime:** `KeyNotFoundException 'o2920_0000'` in `Graph.Construct`. With `FullName` patched to `Name ?? ID`, every one of 10 seeds fails in `GraphConnector` (§0). |
| `6e6dc78`, unported, + the same deps | 17 compile errors, all in `GameDataWriter.cs`, all covered by the §5 port table. |
| `6e6dc78` + §5 port + the same deps, net8.0 | Builds, also with Newtonsoft.Json 13.0.3. |
| `dryrun`, options `world boss warp scale lords` | Seeds 0, 1, 42, 2147483647, 2147483648, 4294967295 and 3141592653 all solve, and staging is byte-identical afterwards. |
| Data root | `git archive 6e6dc78 dist/fog.txt dist/DS1R` (60 files, byte-identical to the §4 blobs). All runs below were repeated with this exact pinned root. |
| `write`, seed 3141592653 | 55 files (18 event, 18 MSB, 1 GameParam, 1 msg, 17 talk) plus 55 `.bak`, in about 1.7 s. |
| Determinism | Two separate processes produce byte-identical output hashes; seed 7 produces different output. |

**Probe limitation:** the staging input was FogMod's own `dist/DS1R` files,
not a pristine game tree, and key items came out as not randomized
(vanilla lots). This proves the build, the runtime and determinism, not
game-data correctness. Test-plan items 2–5 need real pristine and combined
data.

Probe driver: an SDK-style console with
`<Compile Include>` pointing at the vendored sources and a `Main` that
performs §6 steps 2–7 through `Randomizer.Randomize`. It is recreatable from
this section; it is not committed.

## 13. Blockers and user decisions

1. **Plan task missing.** No numbered task in `docs/AI_EXECUTION_PLAN.md`
   builds FogHost, and Task 17 (Codex) is `ACTIVE` in the working tree. The
   user must insert a FogHost task (proposed text below) and decide whether
   Task 17 waits.
2. **FogMod re-pin to `6e6dc78`** (§0): forced by evidence. Record it in
   `DECISIONS.md` when implementing.
3. **Scaling rule (§9):** approve the v1 rule or require a composition
   patch.
4. **FogMod data-root distribution (§4):** bundle, user-provision, or
   fetch-and-verify.
5. **Public GPL fork:** unchanged; required before any binary distribution.
6. **Boss randomization + Fog:** unproven until the §9 conflict set is
   measured.

### Proposed plan entry (for the user to place)

```text
# TASK 16a — CLAUDE
State: PENDING
## Build FogHost (C# host around pinned FogMod)
Implement tools/fog-host per docs/FOGHOST_IMPLEMENTATION_CHECKLIST.md
§§1–8: vendor FogMod 6e6dc78 engine sources (with the §5 API port) and
SoulsIds e31a6fd, NuGet JuicerMV.SoulsFormats 0.1.3; the schema-v2 JSON
boundary; the GameDataWriter.cs patch set with the approved scaling rule;
.bak cleanup, invariants, output hashing. Record the FogMod re-pin in
DECISIONS.md.
Do NOT: commit FogMod dist data unless the user decided to bundle it; wire
getFogPrerequisites; touch the live game; change the backend contract.
Verification: checklist §10 items 1–5.
```
