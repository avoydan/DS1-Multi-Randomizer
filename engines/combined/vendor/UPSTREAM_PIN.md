# Upstream pin

Source: https://github.com/kellernz1/ds-randomizer
Pinned commit: `f120efe0e95b957b7e76109868c3c9966e22720c`
Commit date: 2026-08-20T16:13:04-03:00
Commit subject: "Simplify README and add Nexus download"
License: GPL-3.0-only (see `LICENSE` in this directory)

This is a **selective** vendor, not a full mirror. Two groups of files are
vendored, per `docs/COMBINED_ENGINE_INTEGRATION.md`:

1. the files required to build `DsrDataTool.dll` (the game-data
   extraction/patch tool);
2. the minimal JavaScript generation core required to call
   `generate(config, { gameCatalog })` and `writeOutput(result, dir)`
   in-process (`src/core/*.js` plus the one data module `generator.js`
   statically imports).

Electron, CLI, HTTP-server, and share/link layers are still excluded.

## Vendored paths

| Vendored path | Upstream path | Purpose |
|---|---|---|
| `LICENSE` | `LICENSE` | GPL-3.0-only license text (must be preserved). |
| `THIRD_PARTY_NOTICES.md` | `THIRD_PARTY_NOTICES.md` | Upstream's own third-party notice (`JuicerMV.SoulsFormats`, GPL-3.0-only) — must be preserved and mirrored into this repo's own `THIRD_PARTY_NOTICES.md`. |
| `NuGet.Config` | `NuGet.Config` | Pins package restore to `nuget.org` only, matching upstream's reproducible-build configuration. |
| `scripts/install-dotnet.ps1` | `scripts/install-dotnet.ps1` | Installs .NET 8 SDK locally into `.tools/dotnet` under this vendor directory (no admin rights, no global `PATH` change, no system-wide install). |
| `scripts/dotnet-local.ps1` | `scripts/dotnet-local.ps1` | Thin wrapper that runs the locally-installed `dotnet.exe` with isolated `DOTNET_CLI_HOME`/`NUGET_PACKAGES` environment variables, so builds never touch the developer machine's global NuGet cache or dotnet CLI state. |
| `tools/DsrDataTool/DsrDataTool.csproj` | `tools/DsrDataTool/DsrDataTool.csproj` | The .NET 8 project file for the data tool (`net8.0`, single dependency: `JuicerMV.SoulsFormats` 0.1.3). |
| `tools/DsrDataTool/Program.cs` | `tools/DsrDataTool/Program.cs` | The data tool's full implementation (`scan`, `patch-enemies`, `install`, `restore`, `inspect-boss-bars` commands). Vendored in full because the project file only builds if the source is present; `install`/`restore` remain in the binary but are never invoked by this repository (see `docs/COMBINED_ENGINE_INTEGRATION.md`, "Forbidden upstream calls"). |
| `src/core/config.js` | `src/core/config.js` | `normalizeConfig`, `validateConfig`, `defaultConfig`, `RANDOMIZER_VERSION` (`0.5.66`). |
| `src/core/generator.js` | `src/core/generator.js` | The pure generation entry point `generate(config, { gameCatalog })`. No filesystem, network, or clock access. |
| `src/core/output.js` | `src/core/output.js` | `writeOutput(result, baseDirectory)` — writes `seed_<safeSeed>/{randomizer.json, config.json, README.txt, spoiler.txt?, cheat-locations.txt?}` and returns that directory. Vendored because `patch-enemies --placements` consumes exactly this layout, which the committed workspace planner already encodes. |
| `src/core/rng.js` | `src/core/rng.js` | Deterministic seed derivation/streams. Vendored **only** because `generator.js` statically imports it; never imported directly by this repository. |
| *(none, repository-authored)* | `src/core/placement.js` | Part 0 of the `sampling-v1` enemy placement work: algorithm identity, per-slot RNG substreams, model budget, diagnostics and drop-ownership helpers. Not part of the upstream pin; the module-set test lists it explicitly. |
| *(none, repository-authored)* | `src/core/sampling.js` | Part A of the `sampling-v1` work: class table, planner and fallback ladder for regular enemy slots. Not part of the upstream pin; the module-set test lists it explicitly. |
| `src/data/prototype-data.js` | `src/data/prototype-data.js` | Placeholder enemy/item data. Vendored **only** because `generator.js` statically imports it. It is never the source of an accepted run: a result with `dataStatus !== "extracted"` must be rejected (`docs/COMBINED_ENGINE_INTEGRATION.md`, Blocker 1). |
| `package.json` | *(repository-authored — no upstream counterpart)* | Minimal ESM scope marker (`"type": "module"`) plus the upstream engine version, so Node loads the vendored `src/**/*.js` as ES modules instead of reparsing them as typeless CommonJS. Upstream's own `package.json` is deliberately **not** vendored: it carries Electron/`electron-builder` configuration that is out of scope. Not covered by the byte-identity verification below. |

## Not vendored (and why)

- `src/cli.js`, `src/server.js`, `src/desktop/**` — CLI, HTTP, and Electron
  orchestration layers. Excluded per `AGENTS.md` ("No automation of legacy
  GUIs when callable engine code exists"); `src/cli.js` additionally
  hard-codes its own catalog and `dotnet` paths relative to the upstream
  package root, which this repository does not reproduce.
- `src/core/share.js` — seed share/import links for upstream's own UI. It
  imports `generate`, so it would pull nothing new in, but no part of this
  repository calls it.
- `src/data/catalog.js`, `src/game/detection.js` — catalog loading and game
  install detection/staleness verification. Not in `generate()`'s import
  graph; catalog handling is a separate, still-open concern (see
  `docs/COMBINED_ENGINE_INTEGRATION.md`, Blocker 6).
- `public/**`, upstream `package.json`, `package-lock.json` — Electron/browser
  configurator UI and build configuration; out of scope. (A minimal
  repository-authored `package.json` supplies only the `"type": "module"`
  declaration — see the table above.)
- `test/**` — upstream's own test suite; not exercised here.
- `README.md` — informational only, not a license or notice file.
- `data/dsr-catalog.json` — gitignored upstream, and
  `docs/LICENSING.md` forbids distributing extracted game catalogs. This
  repository's `.gitignore` must keep excluding it.


## Verification method and recorded blob identities

`core.autocrlf` is enabled on the development machine, so vendored files are
checked out with CRLF line endings even though upstream stores them with LF.
A raw `diff -q` against a fresh clone is therefore **not** a reliable
byte-identity check on Windows. Verification instead compares Git blob
object IDs, which are computed on the normalized (LF) content that this
repository actually stores and distributes:

```sh
git -C <clone-of-pinned-commit> rev-parse <pin>:<upstream path>
git hash-object -- engines/combined/vendor/<vendored path>
```

Both must print the same object ID. Recorded upstream blob IDs at pinned
commit `f120efe0e95b957b7e76109868c3c9966e22720c` (all twelve verified
equal to this repository's stored blobs):

| Upstream path | Blob object ID |
|---|---|
| `LICENSE` | `fa5de3594a70344c0687270e5a5af7f84f7bbc83` |
| `THIRD_PARTY_NOTICES.md` | `6ab889fcded91dc98e2e1a05f6eb3fe37c9aada9` |
| `NuGet.Config` | `765346e5343d46ac83654843869dc06b65b2bdc4` |
| `scripts/install-dotnet.ps1` | `009d49c3a1547d6810d4c4f2b54344c986f621f4` |
| `scripts/dotnet-local.ps1` | `78ef46ac464f1fd12c893f72b5352b5f3a03b122` |
| `tools/DsrDataTool/DsrDataTool.csproj` | `65d5aeb15ded3248683d49af92f5baec9479a10b` |
| `tools/DsrDataTool/Program.cs` | `0e5842a8d6d72a07d9236d4718378e30051203ee` |
| `src/core/config.js` | `58c240a191af814fc16240fb16a94e5cebf5789f` |
| `src/core/generator.js` | `449992fb318121a0b7b6bfdc664c1d6c1b9fad3e` |
| `src/core/output.js` | `a13b892fe9a3d3382e86d900d56bbd6deb56cc4c` |
| `src/core/rng.js` | `e0f8dee6a7d2369040858bb9b6efebeb64e2d908` |
| `src/data/prototype-data.js` | `992b81eb69c710749643aa0db1b38806537a0418` |

Vendored files are not edited in place, except for the local modifications
listed below. Until the public GPL fork exists (see "Known gap"), a required
correctness fix may be applied here. It must carry a GPL-3.0 §5(a) notice in
the file, be listed below, and have its diff kept under `patches/` so it can
be applied to the fork. A modified file no longer matches its blob ID above;
the ID records the unmodified upstream bytes.

## Local modifications

| File | Change | Patch | Tests |
|---|---|---|---|
| `tools/DsrDataTool/Program.cs` (upstream blob `0e5842a8…`) | 2026-09-28, user-authorized. `InsertEventInstruction`/`RemoveEventInstruction` keep DS1 "skip N lines" counts (`1000[01/03/05/07]`, `1003[01/03/05/07]`, `1005[01]`) consistent with instruction-count-changing edits. Insertions now state an anchor (`InsertAnchor.Before`/`After`); anchor ambiguity, removal of a skip, parameterized counts and overflow throw. Upstream deleted instructions inside skip ranges without adjusting counts, so skips covered different instructions (38 skip ranges in 21 events of one real-format package). | `patches/0001-dsrdatatool-skip-ranges.patch` | `engines/combined/tests/DsrDataTool.Tests` (unit tests and `verify-skips`) |
| `tools/DsrDataTool/EventSkipRanges.cs` | New, repository-authored (GPL-3.0-only), holds the logic above. | same | same |
| `src/core/config.js` (upstream blob `58c240a1…`), `src/core/generator.js` (upstream blob `449992fb…`) | 2026-10-05, legacy-parity Batch 1 (a feature extension, authorized by the parity task; see `docs/DECISIONS.md`). New config keys `fashionSouls` (default on), `replaceMimics` (on), `replaceRespawningMosquitoes` (on), `preventSameEnemy` (off). With every key at its default the output is byte-identical to upstream (placement hashes compared over 24 seeds, three configurations, before and after). Off/on behavior: Mimic slots (model `c2780`) and the six respawning Blighttown mosquito slots stay vanilla and leave the enemy pool; `separateSameModelSources` removes same-model replacements after the map model budget; complete world armor sets are exchanged between same-size set locations (`findArmorSetHomes`, `permutedArmorSets`). A GPL-3.0 §5(a) notice is at the top of `generator.js`. `RANDOMIZER_VERSION` is deliberately unchanged because it salts every seed stream. | `patches/0002-legacy-parity-batch-1.patch` | `apps/backend/src/adapters/combinedLegacyParity.test.mjs` |
| `src/core/config.js`, `src/core/generator.js` (same upstream blobs) | 2026-10-05, legacy-parity Batch 2 (feature extension, authorized by the parity task; `docs/DECISIONS.md`). New config keys `soulItems` (`shuffled`/`replaced`), `startingItems` (`shield-1h`/`shield-2h`/`mixed-pool`), `pinwheelChaos`, `gwynNerf` (`none`/`medium`/`strong`). **Engine defaults** (`shuffled`, `mixed-pool`, off, `none`) keep the output byte-identical to Batch 1 (24 placement hashes compared). `replaceLesserSouls` swaps goods 400-409 for catalog consumables before the shuffle; `isShieldWeapon` and the primary/offhand pools implement the starting-item modes; `chooseChaosPinwheelSources` plus a boss-shuffle exclusion implement Pinwheel Chaos; `shuffledPortableBosses` takes an excluded-model set for the Gwyn nerf. `RANDOMIZER_VERSION` still unchanged. | `patches/0003-legacy-parity-batch-2.patch` | `apps/backend/src/adapters/combinedLegacyParity.test.mjs` |
| `src/core/config.js`, `src/core/generator.js` (same upstream blobs) | 2026-10-05, legacy-parity Replace second Gargoyle (feature extension; `docs/DECISIONS.md`). New config key `secondGargoyle`: `single-boss` (engine default, the earlier behavior, byte-identical output), `replace` (legacy value 1) and `keep` (legacy value 0). `replace`: when the Bell Gargoyle unit is swapped for another linked unit, body `m10_01_00_00:c5350_0001` is not disabled; the boss plan draws an independent boss for it (own stream `second-gargoyle`, same portable pool, Gwyn nerf and Pinwheel Chaos exclusions honored, no forced combat activation) and appends the placement to the linked-boss plan's enemy list. `keep`: the body and its tail are left vanilla. `buildBossPlacement` was factored out of the boss planner for this. `RANDOMIZER_VERSION` unchanged. | `patches/0004-legacy-parity-second-gargoyle.patch` | `apps/backend/src/adapters/combinedLegacyParity.test.mjs` |
| `src/core/config.js`, `src/core/generator.js` (same upstream blobs), **new** `src/core/placement.js` (repository-authored, not upstream; GPL-3.0-only) | 2026-10-05, enemy placement Part 0 (infrastructure only; `docs/DECISIONS.md`, `docs/ENEMY_PLACEMENT_MODEL.md`). New config key `enemyPlacementAlgorithm` (`permutation-v1`; deliberately **not** in the enum-fallback list of `normalizeConfig`). `generate()` resolves the algorithm first and refuses an unknown or unimplemented one (`sampling-v1`) with `EnemyPlacementUnavailableError` (`code` `ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE`); `planExtractedEnemies` dispatches to the unchanged `randomizeExtractedEnemies`. `placement.js` holds the sampling vocabulary only: slot-id validation and per-slot RNG substreams (`enemy-placement-v1`, name `slot:<id>`), `ModelBudget` (cap 30, fixed models count, no per-map exception), diagnostics and drop-ownership helpers. Result gains `placementDiagnostics` (outside `placementHash`). Output for `permutation-v1` is unchanged (24 placement hashes identical; 56 of 56 non-metadata package files identical in two combined-only packages, 73 of 73 in a Fog package). `RANDOMIZER_VERSION` unchanged. | `patches/0005-enemy-placement-infrastructure.patch` | `apps/backend/src/adapters/enemyPlacementInfrastructure.test.mjs` |
| `src/core/config.js`, `src/core/generator.js` (same upstream blobs), `src/core/placement.js`, **new** `src/core/sampling.js` (repository-authored, GPL-3.0-only) | 2026-10-05, enemy placement Part A (`docs/ENEMY_PLACEMENT_MODEL.md`): `sampling-v1` for REGULAR enemy slots. New config keys `enemyReplacementChance` (100), `enemyNormalReplacement` (`normals`; `none`), `enemyPlacementFit` (`anywhere`; `fits`, `anywhere-except-npcs`) and `placementDiagnosticsLevel` (`summary`/`detail`); the defaults are the values under which `permutation-v1` runs, any other value is refused there (`EnemyPlacementOptionsError`). `generator.js`: `randomizeExtractedEnemies` was split into `regularEnemyContext` (slots, fixed models, Pinwheel clones), the permutation body and `decorateEnemyPlacements` (shared; the permutation output is byte-identical, 24 placement hashes) and `sampleExtractedEnemies`; `enemyPlacement` accepts an instruction `forceScaledNpcParam` (never passed by permutation-v1). `sampling.js`: class table (`regular-size-v1`, footprint edges), `classTableHash`, `buildMapModelSet` (coverage reserve), `fallbackLadder`, `planSampledRegularEnemies`. Slots that are not replaced get no placement. `RANDOMIZER_VERSION` unchanged. | `patches/0006-sampling-v1-regular-enemies.patch` | `apps/backend/src/adapters/enemySampling.test.mjs` |
| `src/core/sampling.js` (repository-authored) | 2026-10-05, enemy placement Part A, non-worsening model budget (`docs/DECISIONS.md`): `effectiveModelBudget(baseline) = max(30, baseline)` where the baseline is the fixed plans plus the vanilla models of slots the replacement chance leaves alone; the sampler never takes a map above it. The `fit-relaxed` fallback rung is removed (a slot with no fitting type is the retryable `ENEMY_PLACEMENT_INFEASIBLE`). Diagnostics gain per-map `baseline`/`effectiveCap` and the counter `mapsAboveCap`. `RANDOMIZER_VERSION` unchanged. | `patches/0007-sampling-v1-non-worsening-budget.patch` | `apps/backend/src/adapters/enemySampling.test.mjs` |
| `src/core/config.js`, `src/core/placement.js`, `src/core/sampling.js` (repository-authored where noted in the file header) | 2026-10-05, enemy placement Part B (`docs/ENEMY_PLACEMENT_MODEL.md` 10.3): difficulty for regular enemies. New config keys `enemyDifficultyMode` (`random`; `curve`, `random-easy-asylum`, `curve-easy-asylum`) and `enemyDifficultyStrictness` (`loose`; `strict`, `very-loose`), refused with non-neutral values under `permutation-v1`. `sampling.js`: class table `regular-class-v2` (difficulty from the map progression tier and HP rank, in `classTableHash`), the strictness distributions and eligibility matrix, Easy Asylum, nearest-class difficulty lapse under budget pressure, and bounded map-local retries (`MAP_ATTEMPTS` = 3; attempt streams `attempt:<n>:slot:<id>`). `placement.js`: attempt-aware slot streams, diagnostics `absorb`/`peak`/`addRetry`/`options`. Attempt 0 output equals Part A. `RANDOMIZER_VERSION` unchanged. | `patches/0008-sampling-v1-difficulty-and-map-retries.patch` | `apps/backend/src/adapters/enemyDifficulty.test.mjs` |
| `src/core/config.js`, `src/core/placement.js`, `src/core/sampling.js` (repository-authored where noted in the file header) | 2026-10-05, enemy placement Part C1 (`docs/ENEMY_PLACEMENT_MODEL.md` 10.4): legacy Type replacement for regular enemies. New config key `enemyTypeReplacement` (`disabled`; `enabled`, `enabled-except-roaming-bosses`), refused when not `disabled` under `permutation-v1`. `sampling.js`: the shared `sampleCandidate` (slots and groups), `planMapByType` (group = original model within a map, one decision per group from its own stream, candidate valid for every member, injectivity, Prevent same, difficulty, reference-counted budget), `sharedFallback` (last rung: a model another group already has) and `createCandidatePool` (hook for a custom pool). `placement.js`: type streams `type:<map>:<model>` / `attempt:<n>:type:<map>:<model>`, `ModelLedger`, group diagnostics. With the option off the output equals Part B (attempt 0, retries and `classTableHash` unchanged). `RANDOMIZER_VERSION` unchanged. | `patches/0009-sampling-v1-type-replacement.patch` | `apps/backend/src/adapters/enemyTypeReplacement.test.mjs` |
| `src/core/config.js`, `src/core/placement.js`, `src/core/sampling.js`, `src/core/generator.js` (repository-authored where noted in the file header) | 2026-10-05, enemy placement Part C2 (`docs/ENEMY_PLACEMENT_MODEL.md` 10.5): custom enemy pool for regular enemies. New config key `enemyCustomPool` (`null` = full pool; otherwise a non-empty list of unique model ids), refused when not `null` under `permutation-v1`. `placement.js`: `canonicalCustomPool`, `EnemyPlacementCustomPoolError` (`ENEMY_PLACEMENT_CUSTOM_POOL_INVALID`, never retryable). `sampling.js`: the pool enters through `createCandidatePool`/`resolveCustomPool` (unknown or non-regular ids, no slot able to take any selected enemy), `analyzePoolCoverage`, `customPoolHash`; slots no pool model can ever replace (`cannotBeReplaced`) stay vanilla and join the map baseline; the coverage reserve of a restricted pool is a coverage witness (`coverWitness`/`coverWitnessSearch`: the fewest models that let every participating slot or Type group keep its model or take a fitting pool model), trimmed to the room under the unchanged non-worsening cap; `resolveModelCap`: a controlled one-model grace - the map cap is 31 only when its base cap is the nominal 30, a proven-minimal cover shows 30 is impossible and exactly 31 suffices (an existing baseline above 30 is never raised); at replacement chance 100% a map whose proven-minimal cover cannot fit even then is refused up front (`map-infeasible`, never retried). `placement.js`: `ModelLedger` gained insurance (`reserve`/`dropReserve`) for Type groups. `generator.js`: `describeRegularEnemyPool` for the editor. With the full pool the output equals Part C1 (attempt 0, retries, `classTableHash`). `RANDOMIZER_VERSION` unchanged. | `patches/0010-sampling-v1-custom-pool.patch` | `apps/backend/src/adapters/enemyCustomPool.test.mjs` |
| `src/core/config.js`, `src/core/generator.js` (repository-authored where noted in the file header) | 2026-10-06, world randomizer item tracking (`docs/FOG_ITEM_TRACKING.md`). New config key `fogTrackedItems` (`null` or `{ items: [{ equipType, itemId, required }], lotStarts: [rowId] }`). `generator.js` `randomizeExtractedItemLots`: after the global item shuffle (and the finite-shop soul rule) a repair pass gives every source of a tracked key item a role - the single copy FogMod must find may only go into a lot FogMod annotates (a start row or the rows that follow it without a gap), every extra copy (the shop copies of the Crest of Artorias, Residence Key and Cast Light, items FogMod does not require) may only go into a shop - and swaps violators with an eligible donor, keeping the other shuffle rules. The RNG is only used when a swap is needed, so every shuffle that already satisfied the rule keeps its exact placement, and with `null` the output is unchanged. `FOG_TRACKED_ITEM_UNPLACEABLE` when no place exists. `RANDOMIZER_VERSION` unchanged. | `patches/0011-fog-tracked-item-lots.patch` | `apps/backend/src/adapters/fogTrackedItems.test.mjs` |
| `src/core/config.js`, `src/core/placement.js`, `src/core/sampling.js`, `src/core/generator.js` | 2026-10-06, sampling-v1 boss sampling (Part D, `docs/ENEMY_PLACEMENT_MODEL.md` 10.6). New config keys `enemyBossReplacement` (`classic`, neutral, or `bosses`) and `enemyUniqueBosses`; `sampling.js` `planSampledBosses` draws a replacement for every single-body boss slot from the portable boss pool (per-slot stream, replacement chance, Gwyn nerf per candidate, unique-boss cycle); `generator.js` `randomizeExtractedBosses` uses it only for `bosses` (the permutation path is untouched; sampled bosses get their own think-param ids and slot-owned drops). With the neutral values the output is byte-identical (golden fixtures, 24 lock hashes, sampling pins). `RANDOMIZER_VERSION` unchanged. | `patches/0012-sampling-v1-boss-sampling.patch` | `apps/backend/src/adapters/enemyBossSampling.test.mjs` |
| `tools/DsrDataTool/Program.cs` | 2026-10-06, event window vs fixed-ID events (`docs/EVENT_ALLOCATION.md`). `FindFreeCustomEventRange` takes the IDs that will be added later with a fixed ID (the Bed of Chaos floor event `11419980`) as reserved, the floor event ID is chosen before the per-map custom event window, and the tool throws if a map uses more IDs than it reserved. With `DS1TR_AUDIT_EVENTS=1` it prints `audit-events <map> count= consumed= base= floor=` per map. Windows that never reached the reserved ID keep their exact IDs. Fixes `Randomizer event ID is already used: 11419980` (Type replacement enabled + Placement anywhere + bosses). | `patches/0013-event-window-reserve.patch` | `apps/backend/scripts/placementMatrix.mjs` (real catalog) |
| `src/core/config.js`, `src/core/placement.js`, `src/core/sampling.js`, `src/core/generator.js`, `tools/DsrDataTool/Program.cs` | 2026-10-06, roaming bosses (Part E1, `docs/ENEMY_PLACEMENT_MODEL.md` 10.7). New config keys `enemyBossChanceNormalSlots` (10), `enemyRoamingBossSouls` (50), `enemyRoamingBossesStayDead` (false); `enemyNormalReplacement` accepts `bosses` and `both` (sampling-v1 only). `planRoamingBosses` / `deriveRoamingBossTypes` (sampling.js) are a second pass after the unchanged regular sampler; generator.js holds the souls table, the safety exclusions, the protected slots and the roaming placement fields. `PatchPlacement` gains `roamingBossSouls` and `disableRespawn`, written to the scaled NpcParam row (asserted). Every earlier option value and output is unchanged (15 golden fixtures byte-identical). | `patches/0014-roaming-bosses.patch` | `apps/backend/scripts/roamingSweep.mjs`, `placementMatrix.mjs roaming-e1` (real catalog and data tool) |
| `tools/DsrDataTool/Program.cs` | 2026-10-06, roaming boss respawn bit (Part E1 fix). A roaming boss row is a copy of a boss row, whose `disableRespawn` is 1; the tool now writes the bit for every roaming row from the option (stay dead on = 1, off = 0) instead of only ever setting it, and asserts it. Rows of ordinary replacements are untouched. Changes the GameParam of roaming worlds with stay dead off (every roaming row 1 -> 0) and nothing else; see `docs/GENERATION_COMPATIBILITY.md`. | `patches/0015-roaming-respawn-bit.patch` | `apps/backend/scripts/smokeTestCatalog.mjs verify` (quick-roaming-respawn), `goldenCompatibility.mjs verify` |

## Updating this pin

1. Re-clone `kellernz1/ds-randomizer` at the new commit.
2. Re-copy only the paths listed above (`git cat-file blob <pin>:<path>`
   writes the exact upstream bytes), verifying each against the new
   clone's blob object IDs as described under "Verification method".
3. Update the commit hash/date/subject and the blob ID table above.
   Re-check `RANDOMIZER_VERSION` in `src/core/config.js`, which
   `apps/backend/src/adapters/combinedRandomizerCore.test.mjs` asserts.
4. Re-run `npm run test -w @ds1tr/backend`: the JS-core tests fail loudly
   if the vendored module set or its import graph changed.
5. Re-run the build in `docs/COMBINED_ENGINE_INTEGRATION.md` ("Vendored
   build proof") and confirm `DsrDataTool.dll` still produces successfully.
6. Record the change in `docs/DECISIONS.md` if the update changes any
   documented behavior (command names, file paths, schema version).

## Known gap

`docs/LICENSING.md` requires "a public source tag matching each
distributed binary." No public fork of `kellernz1/ds-randomizer` has been
created yet — that is an account-owning action (creating and maintaining a
fork under a project-controlled GitHub account) outside what this task
vendors locally. This vendor directory satisfies "exact pinned commit" and
"preserved upstream copyright/license notices" today; the public fork
remains open before any binary built from this vendor directory is
distributed to end users.
| `src/core/config.js`, `src/core/placement.js`, `src/core/sampling.js`, `src/core/generator.js`, `tools/DsrDataTool/Program.cs` | 2026-10-07, a regular enemy as the body of a boss encounter (Part E2, `docs/ENEMY_PLACEMENT_MODEL.md` 10.8). New config key `enemyBossChanceBossSlots`, `enemyBossReplacement` values `normals` / `both`; `planSampledBosses` places a normal enemy as the body of the Asylum Demon, Taurus Demon and Capra Demon encounters (own streams `category:slot:<id>` and `body:slot:<id>`); the data tool writes the encounter-owned cells (`dropType`, `getSoul`, `disableInitializeDead`, item lots, `disableRespawn` = 1) to the body's scaled NpcParam row. No event is added or changed beyond Part D's. Neutral configurations (`classic`, `bosses`) are byte-identical: all 18 earlier golden fixtures verify. | `patches/0016-encounter-body.patch` | `apps/backend/scripts/bodySweep.mjs`, `smokeTestCatalog.mjs verify` (quick-encounter-body), `goldenCompatibility.mjs verify` (set `encounter-bodies`) |
| `src/core/config.js`, `src/core/placement.js`, `src/core/sampling.js`, `src/core/generator.js`, `tools/DsrDataTool/Program.cs` | 2026-10-07, hostile NPCs (Part F1, `docs/ENEMY_PLACEMENT_MODEL.md` 10.9). New config key `enemyHostileNpcs`; the Undead Merchant (Undead Burg), Andre of Astora and Vamos are appended to the regular candidate table (`deriveHostileNpcTypes`, `HOSTILE_NPC_MAP_CAP` 12 per map, none in the Darkroot Basin) and placed as bodies in regular slots with the slot-owned row (the data tool copies `npcType`, `vowType`, `disableRespawn`, `dropType`, `humanityLotId`, `getSoul` from the slot row and asserts them); the existing wake-up event is the legacy allegiance event; `classifyNpcSlot` gives every NPC slot a role. Option off adds no type, no draw, no stream: all 22 earlier golden fixtures verify byte for byte. | `patches/0017-hostile-npcs.patch` | `apps/backend/scripts/npcSweep.mjs`, `smokeTestCatalog.mjs verify` (quick-hostile-npc), `goldenCompatibility.mjs verify` (set `hostile-npcs`), `placementMatrix.mjs hostile-npc-f1` |
| `src/core/generator.js`, `src/core/sampling.js`, `tools/DsrDataTool/Program.cs` | 2026-10-07, event owners (docs/EVENT_ALLOCATION.md). `EventOwnerMap` resolves the map whose event file serves an MSB (the Darkroot Basin -> the Garden) for every generated-event path of a `sampling-v1` world (`scan --event-owners shared`, recorded in the catalog); `EnsureEventOwnersExist`; hostile NPCs may stand in the Basin. The classic permutation is unchanged. | `patches/0018-event-owners.patch` | `SmokeVerify audit`, `placementMatrix.mjs event-owners`, `goldenCompatibility.mjs verify` |
| `src/core/generator.js` | 2026-10-07, Part G: `result.validation.enemyPlacement`, the placement validation summary (plain counters of what was placed, wake-ups, own rows, retries, fallbacks and safety exclusions; no per-slot detail; not part of `placementHash`; no draw, no stream). | `patches/0019-placement-validation-summary.patch` | `enemyPlacementOrdering.test.mjs`, `defaultSweep.mjs`, `featureMatrix.mjs` |
| `tools/DsrDataTool/DsrDataTool.csproj` | 2026-10-07, release trust: the executable names this project (company, product, description, copyright) instead of the toolchain's defaults, so a scanner or a person reading the file properties sees who made it. Metadata only; no code. | `patches/0020-pe-metadata.patch` | `tools/release/release-verify.mjs` (metadata check) |
| `tools/DsrDataTool/Program.cs` | 2026-10-07, Part H: `scan` also records `bossRewards` (the 27 initialisations of the common boss reward event 970 with the chain of rows awarded with each lot, the map that sets the trigger flag, the DLC flag; typed scan errors when the event changes shape or an NPC row names a reward lot) and `transpositionRecipes` (the weapons each boss soul can be turned into, from EquipMtrlSetParam x EquipParamWeapon.materialSetId); `common.emevd.dcx` becomes a catalog source; item assignments also read `bossRewards`. | `patches/0021-boss-rewards.patch` | `SmokeVerify lots`, `placementMatrix.mjs souls-h`, golden `soul-items` |
| `src/core/config.js`, `src/core/generator.js`, `src/core/output.js` | 2026-10-07, Part H: Soul items `transposed` (the boss soul rows join the pool, 75% become a boss weapon of their recipe; stream `soul-items-transposed`); `placements.bossRewards` exists only with the option (every other world keeps its placement hash); spoiler section BOSS REWARDS only with the option. | `patches/0022-soul-items-transposed.patch` | `combinedSoulsTransposed.test.mjs`, golden `soul-items`, `placementMatrix.mjs souls-h` |
| `src/core/config.js`, `src/core/generator.js`, `src/core/output.js`, **new** `src/core/keys.js` (repository-authored, GPL-3.0-only; not part of the upstream pin; the module-set test lists it) | 2026-10-07, Part H: key placement. New config keys `keyPlacement` (`vanilla`, neutral, or `shuffled`), `keyRandomizeLordvessel`, `keyRandomizeLordSouls` (neutral `false`, refused without `shuffled`) and `progressionModel` (the exported world graph, kept in the result as its identity only); `keys.js` evaluates the model and places the keys with an assumed fill (streams `item-keys:<n>` and `item-keys-fill`, at most 32 attempts, typed `KEY_PLACEMENT_*` failures); the planner removes the movable keys from the item pool before the shuffle and writes the key and displaced-item placements after it; `validation.keyPlacement` and the spoiler section KEY PLACEMENT exist only with the option (every other world keeps its placement hash). | `patches/0023-key-placement.patch` (the new `keys.js` is not part of a patch: it has no upstream blob) | `combinedKeyPlacement.test.mjs`, `progressionDifferential.mjs`, golden `key-placement`, `placementMatrix.mjs keys-h` |
