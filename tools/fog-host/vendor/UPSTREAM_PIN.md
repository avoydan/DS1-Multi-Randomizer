# Upstream pins (FogHost)

Selective vendor of the engine sources FogHost compiles. Nothing else from
these repositories is used. The `.gitattributes` in this directory keeps every
vendored file byte-identical to its pinned blob: some upstream files are CRLF,
some are LF, and some are mixed.

## FogMod

- Source: https://github.com/thefifthmatt/FogMod
- Pinned commit: `6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f` (2020-01-21, "Enemy
  randomizer support"). This is the last DS1-only commit.
- **Not `28711c3`** (current `master`): its DS3 update broke DS1R. Entrance
  keying changed to `Area_ID`, and the DS1 connector loop never exits. The
  breakage was verified at runtime (`docs/FOGHOST_IMPLEMENTATION_CHECKLIST.md`
  §0).
- License: Apache-2.0, `FogMod/LICENSE`, taken from the repository root at
  the pin. There is no NOTICE file.

| Vendored file | Upstream path | Blob at pin | State |
|---|---|---|---|
| `FogMod/LICENSE` | `LICENSE` | `e454a52586f29b8ce8a6799163eac1f875e9ac01` | identical |
| `FogMod/AnnotationData.cs` | `FogMod/AnnotationData.cs` | `327f7432f5360ca1603b4360abeaa6a120a5c2dc` | **modified** (below) |
| `FogMod/GameDataWriter.cs` | `FogMod/GameDataWriter.cs` | `8f66afdb6eea4d6c9a073c2a58b09acfac682fbb` | **modified** (below) |
| `FogMod/Graph.cs` | `FogMod/Graph.cs` | `7abe971f08312583d2463bdfa6dcebca2a1611a1` | **modified** (below) |
| `FogMod/GraphChecker.cs` | `FogMod/GraphChecker.cs` | `4fde4bb06c85f5cb0e7b4ce6a19ce013d3508064` | **modified** (below) |
| `FogMod/GraphConnector.cs` | `FogMod/GraphConnector.cs` | `a0e12abac04074ac3ae92f33b8ceedfbc4103dbf` | **modified** (below) |
| `FogMod/ItemReader.cs` | `FogMod/ItemReader.cs` | `a38b6705e4f12e42c6ec3e31947fb1a9eb7818d5` | identical |
| `FogMod/Randomizer.cs` | `FogMod/Randomizer.cs` | `b434e39bea2c15c2c6e55af774c2e7cffe83a939` | identical (compiled, not called) |
| `FogMod/RandomizerOptions.cs` | `FogMod/RandomizerOptions.cs` | `f376cf7a52f3dbd0457375686794a6d0100794ea` | identical |
| `FogMod/Util.cs` | `FogMod/Util.cs` | `fa5351862467db00aeaf9a37d42a87d964d317b8` | identical |

Excluded: `Program.cs` (its CLI path is not trusted), `MainForm*`, `*.resx`,
`Properties/`, `App.config`, `packages.config`, `FogMod.csproj`, and the icon.
FogMod's `dist/` data is **not** vendored. It is FromSoftware-derived, so the
user supplies it and FogHost verifies it against the manifest in
`src/DataRoot.cs`.

### `GameDataWriter.cs` modifications

The file carries an Apache-2.0 §4(b) change notice at the top. All lines are
CRLF after patching.

1. **SoulsFormats API port** for `JuicerMV.SoulsFormats` 0.1.3. These are the
   same renames upstream made in `28711c3`:
   - `layouts["SP_EFFECT_PARAM_ST"]` / `layouts["NPC_PARAM_ST"]` become
     `Params[...].AppliedParamdef`;
   - `cell.Name` becomes `cell.Def.InternalName`;
   - `MSB1.Shape.` becomes `MSB.Shape.`;
   - `.UnkT0C` becomes `.InitAnimID`;
   - `con.Placeholder = null;` is removed.
2. **No `.bak` backups.** The `GetAllBaseFiles`/`SFUtil.Backup` loop is
   removed.
3. **Deferred writes.** Every write (EMEVD, MSB, GameParam, talk ESD copies,
   `menu.msgbnd`) goes through `FogHostHooks.Defer`. A dry run writes
   nothing, and merge conflicts or invariant violations are raised before any
   file changes. The event and ESD directory listings are ordinal-sorted.
4. **Preserved NpcParam rows** (`FogHostHooks.PreservedNpcParams`) are
   skipped by the restore-base-ID pass, kept by the delete pass, and never
   scaled. This is the v1 scaling rule.
5. **EMEVD merge hook.** `Hooks.MergeEvents` runs after FogMod's per-file
   edits and before the file is written.
6. **Unannotated-collision scaling fix** (FogHost 0.2.0). In the scale pass,
   an enemy whose collision has no area annotation got `area = null`, which
   upstream then used as a `Dictionary` key (`ArgumentNullException`). The
   group is now keyed by a sentinel that is never in `g.AreaRatios`, so it
   stays unscaled, as upstream's own null-area handling intended.
   Randomized enemy placements exposed this in 11 of 40 real-format seeds.
7. **Boss-trigger skip fix** (FogHost 0.3.1). FogMod's boss-trigger rewrite
   replaces one `IF In/Outside Area` (`3[02]`) in place with `1 + (1 + alternates)`
   instructions. It did not adjust enclosing DS1 skips, so they covered different
   instructions. On real data this hit 11 events in every seed: `11015393`,
   `11205383`, `11215002/3/12/13`, `11405393`, `11415343/383`, `11505393`,
   `11515393`. One call to `FogHost.SkipRanges.BeforeReplace` now grows every
   enclosing skip by the net line change. It refuses a control-flow target, a
   parameterized count, or an overflow (`FOG_ENGINE_FAILED`). This is the same
   principle as the combined engine's `EventSkipRanges`.
8. **Asylum Demon removal** (FogHost 0.4.0). In `11810310`, FogMod's
   `RemoveAll` of `2003[18]`/`2004[41]` now removes the same instructions one at
   a time through `FogHost.SkipRanges.Remove`. That call shrinks enclosing skips
   and re-indexes event parameters. It refuses control-flow targets and
   parameterized instructions. The pinned template event has no skips or
   parameters, so the output is unchanged today; the patch keeps the same
   principle as patch 7.

### Boss-completion progression modifications

`AnnotationData.cs`, `Graph.cs`, `GraphChecker.cs`, and `GraphConnector.cs` each
carry an Apache-2.0 section 4(b) modification notice at the file header.

- `AnnotationData.cs` adds FogHost-only, YAML-ignored boss-completion data,
  including `BossEncounterStartEventId` on an entrance side. These properties
  are not emitted to FogMod YAML. The start event is distinct from the boss
  death/completion event.
- `Graph.cs` recognizes only explicitly declared `boss_defeated_<eventId>`
  condition atoms; unknown atoms remain errors.
- `GraphChecker.cs` maintains boss-completion state separately from visited
  Areas and items. It derives an encounter as reachable only from a traversed
  directed edge whose destination side matches the exact entrance, host-only
  encounter-start event, and verified default landing semantics. For Ceaseless,
  the V5 side must have no BossTrigger, BossTriggerArea, or CustomWarp. The
  pristine boss trigger region is used to validate the start event relationship;
  it is not a player spawn coordinate. Boss completion dependencies resolve
  through the encounter Area; loop convergence includes Area and completion
  state. Its optional initial-completion argument supports a post-defeat check
  that starts at the encounter and treats that boss flag as set. This is a
  reachability abstraction: the game still sets the real completion flag only
  when the player defeats the boss.
- `GraphConnector.cs` includes unmet boss-completion route conditions in its
  bounded-solver failure details. With no completion records, v1-v4 graph
  behavior and error text are unchanged.

The project-owned `BossProgressionValidator` runs only for Compact V2 (profile
v5), after `GraphConnector` finishes. It checks that every destination behind
a boss-completion condition is reachable from that boss's encounter area with
the boss flag set, after taking the encounter's named exit entrance and then
using only fixed physical transitions. A different randomized Fog entrance
cannot substitute for that route. The optional `GraphChecker` edge predicate
is unrestricted when omitted; Compact V1 and legacy profile paths retain their
previous behavior. Failure returns retryable `FOG_SEED_UNSOLVABLE` at
`solve-graph`, and the backend tries the next deterministic Fog seed.

Tests: `BossProgressionTest` covers Area-only, wrong entrance, custom warp,
wrong encounter-start event, and the exact default-landing Ceaseless edge;
it also verifies post-boss reachability from the encounter and the retryable
failure for a sealed pocket;
`FogDsrFactsTest` checks the pristine encounter-start region; the Compact v2
real-generation test decodes the emitted Ceaseless player part and compares
its position and yaw to the pinned upstream writer calculation.

## SoulsIds

- Source: https://github.com/thefifthmatt/SoulsIds
- Pinned commit: `e31a6fdb516491b6c731cb2847f22faaabbcf925` (2022-02-15). Later
  commits target `SoulsFormatsNEXT`.
- License: Apache-2.0, `SoulsIds/LICENSE`, taken from the repository root.

| Vendored file | Blob at pin | State |
|---|---|---|
| `SoulsIds/LICENSE` | `e454a52586f29b8ce8a6799163eac1f875e9ac01` | identical |
| `SoulsIds/EMEDF.cs` | `2c3240dc7c5671ef38d3545db8008c6bbe2ba817` | identical |
| `SoulsIds/Events.cs` | `02a67d94ebdc8b3e3c876d7d6a8ce3c149cff1d8` | identical |
| `SoulsIds/GameEditor.cs` | `83506148542ada232df9eec3c86a8ee87da5752b` | identical |
| `SoulsIds/GameSpec.cs` | `b258d5be6fc8f39c7d479305d3221c844d32de79` | identical |
| `SoulsIds/Scraper.cs` | `518ba365d1eb133dd3edd2871fb9af39d50fffee` | identical |
| `SoulsIds/Universe.cs` | `55594659c2ff211cf1743277a74b5e2805fbb35a` | identical |

## NuGet packages (not vendored)

| Package | Version | License |
|---|---|---|
| `JuicerMV.SoulsFormats` | 0.1.3 (source `juicermv/SoulsFormats@c3f100941fa67756407e08b2793381edaabd466f`) | GPL-3.0-only |
| `YamlDotNet` | 7.0.0 | MIT |
| `Newtonsoft.Json` | 13.0.3 (SoulsIds pins 12.0.3, which has advisory GHSA-5crp-9r3c-p9vr) | MIT |

## Verification

Run this from the repository root. Each ID must equal the upstream
`git rev-parse <pin>:<path>`. The `-text` attribute makes `hash-object` hash
the raw bytes.

```sh
git hash-object tools/fog-host/vendor/FogMod/*.cs tools/fog-host/vendor/FogMod/LICENSE
git hash-object tools/fog-host/vendor/SoulsIds/*
```
