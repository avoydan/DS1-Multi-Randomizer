# Implementation status

**Enemy placement Part D: boss sampling (2026-10-06; backend 0.32.0, desktop 0.19.0, contracts 0.16.0, patch 0012; RANDOMIZER_VERSION 0.5.66 and FogHost 0.5.0 unchanged; generation profile `ds1tr-gen-v1` unchanged).**
`sampling-v1` can draw a replacement for every single-body boss slot from the boss pool (`Boss replacement: Random bosses`), with the
replacement chance per boss slot, the Gwyn nerf per candidate and an optional `Unique bosses` cycle; the controls replace two PLANNED
placeholders. Linked units (Priscilla, Kalameet, Gaping Dragon, Seath, Sanctuary Guardian, Bell Gargoyles, Centipede Demon) stay with their classic planners (unit participation is not built). Details,
evidence and open points: `docs/ENEMY_PLACEMENT_MODEL.md` 10.6. The two new options are **extension keys** of the Seed Code (stored only when
non-neutral), so no existing code changed and none can decode to another world (`docs/SEED_FORMAT.md`, `docs/DECISIONS.md`); presets are version 10.

- **Real format:** 100 boss-sampling seeds with Fog (40 chance 100, 40 Unique at chance 60, 20 with Enemy scaling `vanilla`): 100/100 success,
  boss overlay set unchanged (the same 15 events), 12 instruction merges as before, 0 conflicts, `verify-skips` clean on every package, maximum 30
  models per map; 40/40 for the classic reference. A first run failed 36/40 (`Conflicting custom NpcThinkParam definition`); fixed (per-slot ids).
- **Compatibility:** 24 permutation lock hashes and the sampling pins identical; the 10 original golden fixtures, the two sampling-v1 + classic-boss baselines
  recorded before this change and the two new boss-sampling fixtures reproduce from their Seed Codes (also with a simulated future app version).
- **Found, not fixed:** `sampling-v1` with Type replacement `enabled` and Placement fit `anywhere` fails for some seeds in `patch-enemies`
  ("Randomizer event ID is already used: 11419980", a Bed of Chaos floor event registered twice); present before this change.
- **Tests:** new boss sampler tests, Seed Code extension-key tests, preset v10 migration.

**State-based run area, branding, Fog wording (2026-10-06; desktop 0.18.0, backend 0.31.0, contracts 0.13.0; generation profile ds1tr-gen-v1 unchanged).**
The run area now follows the state of the game (`deriveRunPhase`): original game shows Randomize and Load seed; a generated world shows
Apply to game with Copy seed secondary; an applied game shows Launch game with Restore original game secondary; an interrupted change
shows only Restore. One current message at a time (old success banners are cleared when the next action starts). Load seed generates
the shared world immediately (no "Generate this world" step); when the game is not original, restoring is the one thing asked, and the
world is created automatically afterwards. Run history is collapsed by default (`Run history  N  v`); a row shows time, status, a
shortened Seed Code and Items/Enemies/Bosses/Fog chips with Apply / Play (or Launch game), Copy seed and an overflow menu (View
settings, View spoiler log, Regenerate files, Delete from history). **Regenerate files** (formerly Recreate) refreshes the existing
history entry (same id, Seed Code and creation time) instead of adding one; so does loading a Seed Code that is already in the history.
The user-facing wording is Fog again ("Fog randomizer settings"); the master seed stays under Advanced. The packaged app has no
File/Edit/View/Window/Help menu bar. Branding: the approved icon (`apps/desktop/assets/branding/ds1tr-icon-source.png`) is converted by
`apps/desktop/scripts/make-icon.ps1` into `apps/desktop/build/icon.ico` (16, 24, 32, 48, 64, 128, 256 px) and used by electron-builder
and the window; the approved LUDOS artwork (`apps/desktop/src/assets/branding/ludos.png`) is a small muted accent at the right of the header.

- **Packaged proof (0.18.0):** menu bar absent (window client area equals window minus frame, `GetMenu` = 0); window, taskbar and EXE
  icons are the DS1TR icon; the whole flow Randomize, Apply, Restore, restart (settings and history persist), Load seed (auto
  generation, same Seed Code, settings restored, history still 1 entry), Regenerate files (same entry), Load seed while applied
  (restore-and-continue) was driven through the UI.
- **Tests:** backend 360 (358 pass, 2 skipped), desktop 5; new tests for the phase logic, history refresh/delete and the UX contract.

**Seed Codes, generation profiles, history and launch (2026-10-06; backend 0.30.0, desktop 0.17.0, contracts 0.13.0; RANDOMIZER_VERSION 0.5.66 and FogHost 0.5.0 unchanged).**
A world is now named by a self-contained Seed Code that carries an immutable generation profile (`ds1tr-gen-v1`), the internal
master seed and the complete canonical settings; the app version is not an input of generation. New: Randomize / Load seed (the
master seed moved to Advanced), persistent settings and run history (Apply, Regenerate files without the run folder, Copy seed, View
settings), explicit Apply / Restore success messages and a Launch game button (Steam, executable fallback). Formats and rules:
`docs/SEED_FORMAT.md`, `docs/GENERATION_COMPATIBILITY.md`, `docs/DECISIONS.md`.

- **Golden fixtures:** `apps/backend/fixtures/golden/ds1tr-gen-v1.json`, 10 permanent cases recorded from a clean game copy (items;
  enemies classic; sampling-v1; items+enemies+bosses; +Fog; broad items with shops; custom pool; second Gargoyle; one-model
  grace; world options). Verified from the Seed Codes alone, also with a simulated application version (`99.1.0-future`):
  identical profile, settings, master seed and package hashes, 10/10.
- **Engines untouched:** no change in `engines/`, seed derivation or retry bounds; only the profile plumbing and drift guard.
- **Tests:** backend 354 (352 pass, 2 skipped: the pre-existing one and the golden regeneration that needs a clean game copy);
  new `seedCode`, `seedPersistence`, `generationProfiles`, `goldenCompatibility`, `seedUx`; desktop 5 (settings migration, launch game).

**World randomizer item tracking (2026-10-06; backend 0.29.1, desktop 0.16.1, FogHost 0.5.0, RANDOMIZER_VERSION 0.5.66).**
Fixed a real failure of the packaged app: with more item categories on, Fog stopped with `Couldn't find darkmoon_seance_ring in item
lots` after three identical combined attempts and the app said "try another seed". FogMod needs exactly one copy of each
required key item in the item lots it annotates and at most one of any; the global item shuffle put the Seance Ring and the
Peculiar Doll outside them (8% to 93% of the seeds depending on the categories) and put the shop copies of the Crest of
Artorias, Residence Key and Cast Light inside them. The shuffle now gives every source of a tracked key item a role (the one
copy: annotated lots only; extra copies: shops only) read from the verified `fog.txt`; nothing is duplicated and no item
category is blocked. `FOG_UPSTREAM_INCOMPATIBLE` is structural (no combined reseeding) with a plain message. Details and
evidence: `docs/FOG_ITEM_TRACKING.md`. Patch `0011`.

- **Real format:** 40 seeds, all item categories, bosses on, Fog on: 40/40 on the first attempt, 0 merge conflicts, same 15 overlay
  events, `verify-skips` clean, deterministic rerun, activation and restore; packaged 0.16.1 through its own UI: Randomize, Apply,
  Restore succeed.
- **Tests:** backend 327 (326 pass, 1 skipped); new `fogTrackedItems.test.mjs` (13) and pipeline tests.
- **Test harness note:** the Fog end-to-end script reconstructs the game from the live installation. Once the app has applied a run to
  the live game that copy is no longer clean (duplicate events in the boss-name patch), so the script can now take a clean copy
  (`DS1TR_E2E_GAME_SOURCE`, for example the app's pristine snapshot).

**User-friendly runtime / Fog bootstrap (2026-10-05; backend 0.29.0, desktop 0.16.0, contracts 0.12.0, FogHost 0.5.0).**
A player no longer picks a FogMod folder or installs .NET. The app finds Dark Souls Remastered through Steam, checks it,
imports the clean snapshot and prepares the world randomizer data itself: 19 of the 59 files are copied from the verified
clean snapshot, 40 are fetched once over HTTPS from the pinned FogMod commit (SHA-256 per file, size bound, temp folder,
whole-set verification, atomic install), after which no network is needed. FogHost and DsrDataTool are self-contained
win-x64 single-file executables. The main screen uses plain wording and readiness indicators; failures are phrased for
players and the exact reason stays in the log and manifest. Details: `docs/FOG_DATA_PROVENANCE.md`,
`docs/DESKTOP_PACKAGING.md`, `docs/DECISIONS.md`.

- **Proof through the packaged path (no `DS1TR_FOG_DATA_ROOT`):** a full Items + Enemies + Bosses + Fog proof with the
  self-contained helpers and an empty state folder (first-run download): generation, byte-identical rerun, activation,
  restore, live installation unchanged. The packaged 0.16.0 app, driven through its own UI on a clean copy of the game: game
  folder check, world data preparation, Randomize, Apply to game, Restore original game.
- **Fixed on the way:** the 0.15.0 portable executable could not start its backend (`@ds1tr/contracts` was not staged).
- **Tests:** backend 312 (311 pass, 1 skipped; new: world data bootstrap, game discovery, prerequisites, plain messages),
  desktop settings migration 2.

**Enemy placement Part C2: custom enemy pool for regular enemies (2026-10-05; contracts 0.12.0, backend 0.28.0, desktop 0.15.0).**
`sampling-v1` gains the custom enemy pool (**PARTIAL**: regular enemies; the original's boss and NPC entries need later
parts). A pool is a canonical sorted list of model ids (`null` = the full pool); it enters only through the candidate
boundary, restricts replacements only (unselected enemies stay where they are, including as the vanilla fallback) and
changes none of fit, Prevent same, Easy Asylum, Type replacement, drop ownership or determinism. Presets are version 9
(1-8 migrate to `permutation-v1` with the full pool, a stale id is an error). The backend lists the regular enemies from the
clean game data (`POST /api/enemy-pool`, cached per snapshot) and the desktop gets a filterable editor. Details: model
document 10.5. Patch `0010`.

- **Model budget (decided 2026-10-05):** nominal 30; the baseline rule `max(30, baseline)` is unchanged; a restricted pool may
  use a **one-model grace to 31** only on a map whose base cap is 30, when 30 is proven impossible and 31 proven sufficient.
  A baseline already above 30 is never raised and nothing above 31 is ever granted. Recorded per map (`nominalCap`,
  `minimumFeasibleModels`, `customPoolModelGrace`). 31 is not a new general limit.
- **Accounting fixes found by the 100-seed validation:** the pool never reached the coverage reserve; the reserve could pick a
  model too big for the smallest slot; the Type ledger could not serve the first group of a map that starts above its cap.
  A coverage witness (the fewest models that keep every participant servable) replaced the reserve of restricted pools;
  slots no pool model can replace join the baseline. The retry bound (3) was not changed.
- **Errors:** structural problems and, at replacement chance 100%, a map whose proven-minimal cover cannot fit even with the
  grace (`map-infeasible`) raise the non-retryable `ENEMY_PLACEMENT_CUSTOM_POOL_INVALID`; a participation draw that does not fit is the
  ordinary `ENEMY_PLACEMENT_INFEASIBLE` (at most 3 map attempts). No percentage or size heuristic rejects pools.
- **Catalog sweep (100 seeds x 18 maps, many pools):** A, B, C, E, N 100/100 in every tested configuration; D at "anywhere"
  100/100, D at "fits" 100% refused or served exactly where provable; 0 fit / Prevent-same / pool-membership / cap violations;
  the grace was used only on Parish and Lost Izalith and ended at 31 models; no baseline above 30 was increased.
- **Real format (Items + Enemies + Fog + bosses):** 50 seeds in 5 pool configurations + 3 full proofs: all pass, 0 merge
  conflicts, same 15 overlay events, `verify-skips` 0 failures, live installation unchanged.
- **Proofs:** `permutation-v1` unchanged (24 hashes, package files of combined-only and combined + Fog runs); the full pool
  equals Part C1 (synthetic pins and 700 real-catalog outputs, same `classTableHash` and retry counts).
- **Parish risk unchanged** (32-33-model baselines of 10.3 are separate and not worsened).
- **Tests:** backend 282 (281 pass, 1 skipped); new `enemyCustomPool.test.mjs` (28 tests); typecheck and desktop build pass.

**Enemy placement Part C1: Type replacement for regular enemies (2026-10-05; contracts 0.11.0, backend 0.27.0, desktop 0.14.0).**
`sampling-v1` gains the legacy Type replacement (**FULL** for regular enemies: disabled = legacy default, enabled, enabled
except roaming bosses = enabled until boss sources exist). The group is the original model within a map; it is decided once
(replacement chance, difficulty class, candidate) from its own stream `type:<map>:<model>` (attempt-aware), every member
slot gets the same replacement and draws only its variant; the candidate fits every member and differs from the
other groups of the map. The model budget is now reference counted for groups (`ModelLedger`: a replaced group frees its
original model). Presets are version 8 (1-7 migrate to `permutation-v1` with Type replacement disabled). Details: model
document 10.4. Patch `0009`. Custom pool (C2) is not started; the candidate pool is an injectable boundary.

- **Measured conflict and decision inside the task:** with strict legacy injectivity 93 of 100 seeds failed at 100% chance
  (the Parish: 15-16 fixed + 17 groups = 32 models, cap 30, deterministic, retries cannot help). Injectivity is now the last
  rung to yield (`shared model`, about 1.6 groups per seed at 100%, 0.01 at 60%, 0 at 25%); fit, budget and fixed entities
  are never relaxed. The retry budget (3) was not changed.
- **Proofs:** `permutation-v1` unchanged (24 hashes; 56/56 and 73/73 package files); with Type replacement off the output
  equals Part B (32 synthetic pins, and 700 real-catalog outputs with identical `classTableHash` and retry counts).
- **Catalog sweep:** 10 variants x 100 seeds, 0 final failures, 0 fit / Prevent-same / cap violations, retry pressure
  at or below Part B. **Real format:** 50 seeds (Items + Enemies + Fog + bosses) + 3 full proofs: all pass, 0 merge
  conflicts, same 15 overlay events, `verify-skips` 0 failures.
- **Parish risk unchanged** (not solved here): with Type replacement the Parish ends at 28-32 models; seeds `parish-26` (31),
  `parish-1` (32), `parish-11`/`parish-34` (map retry) in 10.4.
- **Tests:** backend 253 (252 pass, 1 skipped); new `enemyTypeReplacement.test.mjs` (24 tests); typecheck and desktop build pass.

**Enemy placement Part B: difficulty and map-local retries for regular enemies (2026-10-05; contracts 0.10.0, backend 0.26.0, desktop 0.13.0).**
`sampling-v1` gains Difficulty mode (curve, fully random, and both easy-Asylum modes: legacy default random +
easy Asylum) and Difficulty strictness (strict, a bit loose, very loose), both **FULL** for regular enemies. Classes are
derived by DS1TR (`regular-class-v2`: map progression tier and HP rank, in `classTableHash`), not copied; strictness
tables and the Easy Asylum cap are authored. A map that cannot be placed is replanned alone with attempt streams
(`attempt:<n>:slot:<id>`, at most 3 attempts; attempt 0 equals Part A), so the former whole-run retry for
`ENEMY_PLACEMENT_INFEASIBLE` is now almost never needed. Presets are version 7 (1-6 migrate to `permutation-v1` with
neutral difficulty). Details, eligibility matrix, filtering order and results: model document 10.3. Patch `0008`.

- **Proofs:** `permutation-v1` unchanged (24 hashes; 56/56 and 73/73 package files); 24 Part A synthetic hashes
  identical; real catalog: of 700 Part A outputs 681 are identical and the other 19 (the Part A first-attempt failures) are now solved by a map retry; none differs.
- **Catalog sweep:** 13 variants x 100 seeds: 0 final failures, 0 sampler overflows, 0 fit / Prevent-same / Easy
  Asylum violations. **Real format:** 50 seeds (Items + Enemies + Fog + bosses) + 3 full proofs incl. retried Parish
  maps: all pass, 0 merge conflicts, same 15 overlay events, `verify-skips` 0 failures.
- **Parish risk unchanged:** 31-33 models are reproducible with `parish-12` (31), `parish-11` (32), `parish-28`
  and `parish-92` (33) (config in 10.3); not tested in the game.
- **Tests:** backend 228 (227 pass, 1 skipped); new `enemyDifficulty.test.mjs` (18 tests); typecheck and desktop build pass.

**Enemy placement Part A: `sampling-v1` for regular enemy slots (2026-10-05; contracts 0.9.0, backend 0.25.0, desktop 0.12.0).**
`sampling-v1` is implemented and opt-in (UI: "Legacy-style sampling"); `permutation-v1` stays the default and its
output is unchanged (24 placement hashes; 56/56 and 73/73 package files). It samples regular enemy slots only;
bosses, units and NPCs are planned as before. Controls: Replacement chance (**FULL**, 0-100), Normal enemy
replacement (`none`, `normals`: PARTIAL), Placement fit (**FULL** for regular slots, 0 violations), Prevent same
(FULL). Presets are version 6 (1-5 migrate to `permutation-v1` with neutral settings). Engine:
`vendor/src/core/sampling.js` (patches `0006`, `0007`), class table `regular-size-v1` with `classTableHash`,
coverage reserve, fallback ladder, per-slot streams, slot-owned drops, summary/detail diagnostics.

- **Model budget (decided): non-worsening.** sampling-v1 uses a non-worsening model-budget policy. The normal target is 30 distinct models per map. If out-of-scope/fixed plans already require more than 30, that unavoidable baseline becomes the effective cap. The regular sampler may preserve or reduce that overflow but may not increase it. Details: model document 10.2, `docs/DECISIONS.md`.
- **Catalog validation:** 100 seeds x 17 variants (0/25/60/90/100% x fits/anywhere/Prevent same): 0 sampler
  overflows, 0 fit violations, 0 Prevent-same violations, deterministic. With `fits` at 25%/60% a first attempt
  fails in about 9%/5% of seeds with the retryable `ENEMY_PLACEMENT_INFEASIBLE` (zero headroom in the Parish).
- **Real-format validation:** 55 seeds (100%, 60%, 25%, Prevent same, mode `none`) through the full pipeline with
  Fog and bosses: 55/55 generated, 0 merge conflicts, boss overlay unchanged (15 events), `verify-skips` 0
  failures, byte-identical reruns, live install untouched.
- **Known risk:** the Parish can end at 32-33 models (baseline) when vanilla regular models are kept; not tested in-game.
- **Tests:** backend 210 (209 pass, 1 skipped); `enemySampling.test.mjs` (34 tests), preset v1-v6, mapper,
  bridge and pipeline classification tests; root typecheck and desktop build pass.

**Enemy placement Part 0: infrastructure only (2026-10-05; backend 0.23.0, contracts 0.8.0, desktop 0.11.0).**
Prepares the contract, backend, engine and tests for the future `sampling-v1` placement algorithm without
changing any placement. `permutation-v1` remains the default and the only algorithm that runs; the sampler is
**not implemented**, and selecting `sampling-v1` is refused with `ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE` at
validation, mapper, bridge and engine level (never a silent fallback). Presets are version 5
(`enemies.placement.algorithm`); versions 1 to 4 migrate to `permutation-v1`. Engine: new
`vendor/src/core/placement.js` (patch `0005`, listed in `UPSTREAM_PIN.md` and the module-set test), the config
key `enemyPlacementAlgorithm`, the dispatch `planExtractedEnemies`, per-slot RNG substreams
(`enemy-placement-v1`, `slot:<id>`), `ModelBudget` (cap 30, no per-map exception), diagnostics and
drop-ownership helpers. Model document: section 10.1.

- **Unchanged output (proof):** 24 placement hashes identical; two combined-only packages identical in all 56
  non-metadata files and the combined + Fog package identical in all 73 (metadata files embed the temporary
  path and gain only the new fields). `RANDOMIZER_VERSION` 0.5.66 and FogHost 0.5.0 are untouched; the
  portable EXE was not rebuilt.
- **Model budget:** checked on the real catalog; vanilla maximum 28 distinct models, none above 30, so the
  safety-first cap of 30 rejects no vanilla map.
- **Known and unchanged:** `permutation-v1` with Enemy scaling `vanilla` still duplicates single-owner
  equipment drops (model document 9.1); `sampling-v1` must always create a scaled row.
- **Tests:** backend 168 (167 pass, 1 skipped), including the new `enemyPlacementInfrastructure.test.mjs`
  (gating, RNG substreams, budget, diagnostics, drop ownership, permutation untouched), preset v1-v5 migration,
  mapper and bridge gating; root typecheck and desktop build pass.

**Enemy placement design (2026-10-05).** `docs/ENEMY_PLACEMENT_MODEL.md` reconstructs the legacy Enemy
Randomizer's per-slot placement model, documents the current permutation engine, compares four
architectures and fixes the target: a new `sampling-v1` planner beside the frozen `permutation-v1`
(`docs/DECISIONS.md`), with an incremental plan (batches 0, A-G) for Replacement chance, the replacement
modes, Boss chance, Placement fit, Difficulty mode and strictness, Type replacement, and the options that
build on them. **No runtime behavior, default, preset, FogHost code or packaging changed.**

- **Prototype** (`engines/combined/prototypes/enemy-placement-sampler.mjs`, scratch, not packaged: the packager
  copies only `engines/combined/vendor/src`): regular-enemy sampler on the real catalog, 1,703 slots, 40 seeds
  per variant, every run repeated for determinism. With a coverage reserve: 0 fit violations, 0 budget errors,
  0 non-deterministic runs, 6-8 ms per seed. Without it: 0.13% of placements end above their size class.
  Curve adherence inside a full model set is 30-31% under the literal legacy rule and 72-77% with the
  documented refinement.
- **Finding (current engine, not fixed):** with Enemy scaling `vanilla` and drops not randomized,
  `permutation-v1` duplicates single-owner equipment drops in 95 of 100 seeds (92 from the Titanite Demon
  group's Titanite Catch Pole; the rest from archetype-duplicated bosses: Pinwheel's masks, Taurus's axe,
  Super Ornstein's ring, Capra's weapon), because placements without a scaled row keep the replacement's own
  drops. Gwyn nerf and Second Gargoyle add about 0.05-0.10 lots per seed. The default `area` scaling shows none
  (0 of 100). None of FogMod's 27 tracked items is reachable through an NpcParam drop lot, so there is no Fog
  or progression impact. The new design makes "drops follow the slot" a rule (model document 6.9).
- **Derived classes** versus the legacy ones as a yardstick: size rank correlation 0.74 (79 of 82 within one
  class), difficulty 0.58 (73 of 82 within two).
- **Open product questions** are listed in section 13 of the model document (default algorithm, model budget
  definition, difficulty overrides, boss scaling, NPC allowlist).
- **Verification:** unchanged runtime, so no new runtime tests were needed; the prototype is not part of
  `npm test`. The earlier backend (152), FogHost (74), typecheck and desktop-build results stand.

**Replace second Gargoyle (2026-10-05; backend 0.22.0, contracts 0.7.0, desktop 0.10.0).**
The option now follows the legacy Enemy Randomizer: legacy default Replace; the second Bell Gargoyle
(`m10_01_00_00:c5350_0001`) is replaced by an independently drawn boss, or left alone with Do not
replace. A third value, Single boss, is the earlier DS1TR behavior and exists only so older presets
reproduce their output. Semantics: `docs/FEATURE_PARITY.md` 6a; policy: `docs/DECISIONS.md`;
patch `engines/combined/vendor/patches/0004-legacy-parity-second-gargoyle.patch`. Presets are version 4
(versions 1 to 3 migrate to Single boss). Parity is now 29 FULL of 54.

- **No FogHost change (FogHost stays 0.5.0).** The Batch 1 stop was based on a wrong premise: FogMod makes
  no edit of its own to events `11015396` and `11015382` (it copies the game directory's instructions
  into them), so the normal merge passes the combined forms through. The 15-event boss overlay is
  unchanged. Instruction-level comparison: `docs/FOGHOST_INTEGRATION.md`, "Second Gargoyle events".
- **FogHost tests:** 74/74 (68 existing + 6 new), run with a manually compiled test assembly because no
  .NET SDK is installed: pass-through of both events without the overlay, hold / 60% / release order,
  exact 15-event overlay set, five divergent FogMod forms refused (added control flow, missing anchor,
  duplicate anchor, added parameter, changed threshold), vanilla forms merge to nothing, and a real FogMod
  run writes both legacy forms out unchanged.
- **Engine checks (real catalog, in-process):** over 40 seeds Replace drew 16 distinct bosses, was
  deterministic, produced no duplicate slot, entity or scaled-NpcParam ids, kept the Parish model count
  at most 30, never drew a Bell Gargoyle, linked unit, excluded Gwyn or (with Chaos on) Pinwheel, left the
  third body and second tail disabled, and added no forced combat activation. Keep produced no placement for
  the body or its tail. Single boss (the engine default) leaves the 24 baseline placement hashes identical.
- **Real `patch-enemies` / `verify-skips`:** Replace, Keep and Single boss accepted, 0 skip mismatches.
  Event `11015396` with Replace holds the body (AI off, invincible), waits for 1010800 at 60%, releases
  it and renames the boss bar; with Keep it is byte-identical to vanilla.
- **Proof run** (generate, byte-identical rerun, activate, restore): all pass, and events `11015396`
  and `11015382` in the final Fog package are identical to the combined package's. Live install untouched.
- **Fog sweeps** (Items + Enemies + Bosses + Fog, live install unchanged, `verify-skips` on every attempt):

  | Option | Seeds | Success | Merge conflicts | Key-item retries | Skip checks | Mismatches |
  |---|---|---|---|---|---|---|
  | Replace | 20 | 20 | 0 | 0 of 20 seeds | 20 | 0 |
  | Do not replace | 8 | 8 | 0 | 2 of 8 seeds | 10 | 0 |
  | Single boss (regression) | 8 | 8 | 0 | 0 of 8 seeds | 8 | 0 |

  Boss overlays stayed on the 15 known events; neither Gargoyle event appears under merges or overlays.
- **Tests:** backend 152 passed, 0 failed, 1 skipped (needs a user Fog data root); root typecheck clean;
  desktop build passed. The portable EXE was not rebuilt.
- **Residual risk:** whether each portable boss's AI wakes correctly from the vanilla release (`set_ai(on)`,
  plus the combined engine's usual wake-up rows) cannot be checked without running the game.

**Legacy parity Batch 2 (2026-10-05; backend 0.21.0, contracts 0.6.0, desktop 0.9.0).**
Soul items (Shuffled and Replaced), Starting items, Pinwheel Chaos and Gwyn nerf are functional end
to end with their legacy defaults (soul items shuffled, starting items Shield + 1H/2H, Pinwheel Chaos
off, Gwyn nerf Medium). Semantics, deviations and legacy references: `docs/FEATURE_PARITY.md` 6b;
policy: `docs/DECISIONS.md`; patch: `engines/combined/vendor/patches/0003-legacy-parity-batch-2.patch`.
Presets are now version 3 (versions 1 and 2 migrate to the behavior-preserving values).

- **Not implemented:** Soul items *Transposed* (boss souls are in none of the catalog's pools; shown
  as a disabled choice), Replace second Gargoyle (still blocked, see Batch 1) and the hintsheet.
- **Include DLC was a dead control** (found by the trace: no backend mapping, no engine reader, no
  FogMod switch). The backend now rejects `includeDlc: false`; the UI shows it as a disabled planned
  control. A real implementation is medium work and cannot cover Fog.
- **A bug the Fog sweep caught:** a first version of the Replaced-souls pool included the unique
  Peculiar Doll, producing `Item peculiar_doll found in both ...` (FogMod tracks 27 key items by id)
  in 10 of 10 seeds. The pool now only holds goods that occur in at least two lots; none of the
  pool's 31 goods is among FogMod's key items.
- **Engine defaults unchanged:** the 24 Batch 1 placement hashes are still identical.
- **Real-catalog checks (in-process):** Replaced souls removed all 3380 lesser souls over the runs and
  kept Fire Keeper Souls; shield starting modes gave shields in every class with unique pickups and
  no assignment failures over 180 seeds per mode; Gwyn appears in about 18% (strong) and 40%
  (medium) of 100 seeds; Pinwheel Chaos replaced exactly the 14 clones with the Catacombs model count
  at most 30 and the main Pinwheel untouched.
- **Real `patch-enemies` / `verify-skips`** (live install, scratch output): all options accepted,
  0 skip mismatches. Pinwheel Chaos edits fewer events in `m13_00` than the default
  (`11305392/396/398/250` are no longer edited); `11305100` is never edited.
- **Fog sweeps** (live install unchanged): product defaults 20/20 (0 conflicts, 21 skip checks, 0
  mismatches, 1 retry in 21 attempts); every Batch 1 and Batch 2 option non-default 12/12 (0
  conflicts, 15 skip checks, 0 mismatches, 2 seeds needed a combined retry). Boss overlays stayed on
  the 15 whitelisted events.
- **Tests:** backend 144 passed, 0 failed, 1 skipped (needs a user Fog data root); new engine tests
  cover every option on synthetic catalogs; root typecheck clean; desktop build passed. The portable
  EXE was not rebuilt.
- **Remaining:** see the gaps in `docs/FEATURE_PARITY.md` section 6.

**Legacy parity Batch 1 (2026-10-05; backend 0.20.0, contracts 0.5.0, desktop 0.8.0).**
Four legacy options are now functional end to end, each with its original legacy default:
Fashion Souls (on), Replace mimics (on), Replace respawning mosquitoes (on) and Prevent same
enemy (off). The vendored core was extended (`patches/0002-legacy-parity-batch-1.patch`);
semantics, deviations and the legacy source references are in `docs/FEATURE_PARITY.md`
section 6a. Presets are now version 2; version 1 presets migrate with the legacy defaults.

- **Not implemented:** Replace second Gargoyle (stop condition: it would edit events
  `11015382` and `11015396`, which FogMod rewrites and which are outside the 15-event boss
  overlay whitelist) and the hintsheet (it reports shuffled key locations, which DS1TR does
  not produce). Both remain documented as UI-only / partial. The portable EXE was not rebuilt.
- **Default output unchanged:** 24 placement hashes (three configurations x eight seeds) from
  the real-format catalog are identical before and after the change.
- **Real-catalog checks (in-process, 60 seeds):** prevent-same left 0 same-model regular
  placements (about 47 per seed before) with at most 30 models per map and deterministic
  output; excluded mimic and mosquito slots were never placed; with Fashion Souls off, all 31
  regular armor homes held a complete, distinct vanilla set in each of 40 seeds.
- **Targeted Bosses + Fog sweep** (5 seeds, mimics off, mosquitoes off, prevent-same on,
  Fashion Souls off): 5/5 succeeded; 0 merge conflicts; boss overlays applied only to the 15
  whitelisted events; 9 combined-package skip checks, 0 mismatches; no Fog/combined event
  mismatches; live installation unchanged. 3 of 5 seeds needed a combined key-item retry
  (4 of 9 attempts), within the range recorded earlier (10 of 50); none exhausted the budget.
  No full 40-seed sweep was run because defaults are unchanged.
- **Tests:** backend 124 passed, 0 failed, 1 skipped (needs a user Fog data root); new
  `combinedLegacyParity.test.mjs` covers the engine on synthetic catalogs (no game data is
  committed); root typecheck clean; desktop build passed. Two runs immediately after the
  sweep hit the integration test's 10 s server-start timeout; four later runs were clean.
- **Remaining:** see the engine gaps in `docs/FEATURE_PARITY.md` section 6; the next batch
  should be the A-B items (Soul items, Starting items, Pinwheel Chaos, Gwyn nerf).

**Feature-parity audit and settings presets (2026-10-05).** `docs/FEATURE_PARITY.md` audits
54 original options (Item 11, Enemy 27, FogMod 16) plus 16 combined-engine native options.
FogMod parity is complete. 21 original options are FULL, 30 are UI-only placeholders that
need new engine behavior, 1 is partial (hintsheet), 1 is intentionally unsupported (PTDE)
and 1 is a duplicate (copy enemy placement). The vendored combined engine reads none of the
legacy Item/Enemy options, so no `PLANNED EXTENSION` control became functional and none was
faked.

- **Newly wired:** settings preset import/export (`/api/preset/*`, backend 0.19.0, contracts
  0.4.0, desktop 0.7.0) and a "Restore default settings" button.
- **Verification:** root typecheck clean; backend tests 110 passed, 1 skipped (needs a user
  Fog data root); desktop build passed. No engine or real-data sweeps were run because no
  engine behavior changed.
- **Remaining:** engine gaps G1-G23 in `docs/FEATURE_PARITY.md`, classed A/B/C by effort and
  Fog risk. The portable executable has not been rebuilt for desktop 0.7.0.

**Windows desktop packaging (2026-09-29):** Electron now starts and stops the backend,
resolves bundled runtime files, stores mutable data outside the app directory, and
offers a FogMod data-root picker. Desktop 0.6.1 and backend 0.18.0. A portable x64
artifact is built at `apps/desktop/release/DS1-Triple-Randomizer-0.6.1-x64.exe`.
It bundles DsrDataTool, FogHost, the combined JS core and license notices; FogMod's
game-derived DS1R data is excluded. See `docs/DESKTOP_PACKAGING.md` for the layout,
build command and .NET 8 runtime prerequisite. Packaged-runtime API/tool smoke checks
passed on staging data. The final portable executable launched outside the restricted
sandbox: the renderer showed `Backend online` and first-run DSR/Fog guidance; health
returned 200 with backend 0.18.0; a missing DSR path returned an explicit problem;
the plan reported the combined engine and FogHost ready. Closing the window stopped
the portable wrapper, Electron main process and backend child. The sandboxed preload
had to compile as CommonJS (`preload.cts`); desktop 0.6.1 includes that fix. No public
GPL fork or release tag exists yet.

- Verification: root typecheck clean; backend tests 108 passed, 1 skipped (requires a
  user Fog data root); desktop, backend, DsrDataTool and FogHost builds passed;
  `package:win` completed; production-dependency audit found 0 advisories.
- Portable smoke: 0.6.1 executable launched from `release/`, renderer preload
  connected, backend health returned 200, missing DSR returned
  `DarkSoulsRemastered.exe not found.`, both packaged tools were reported ready,
  first-run DSR/Fog guidance was visible, and normal window close ended the wrapper,
  Electron main and backend child. Bundled DsrDataTool and FogHost executables ran
  and printed their usage messages. A staged-data run without a Fog root returned
  `FOG_PREREQUISITES_MISSING` before randomization. No live game files were touched.
- Release prerequisites: install .NET 8 runtime on the user machine; supply FogMod
  DS1R data locally; create the public GPL corresponding-source fork and matching
  release tag. Code signing and a custom icon are not configured.


**Boss randomization + Fog works (FogHost 0.5.0, backend 0.17.0, desktop 0.5.0,
2026-09-29).** The `FOG_BOSS_RANDOMIZATION_UNSUPPORTED` rejection is removed, and bosses
are on by default again.

- **Mechanism.** A whitelisted boss-event overlay: FogMod's event is the control-flow base,
  and the combined engine's known boss mutations are re-applied by content. It covers the
  15 boss events both engines edit. Details: `docs/FOGHOST_INTEGRATION.md`,
  `docs/DECISIONS.md`.
- **Also:**
  - FogMod patch 8 (Asylum Demon removal keeps skips consistent);
  - merge refinements: unambiguous-result alignments, skip range = covered lines, and
    event-0 initialization removal;
  - refused events log their base, combined and FogMod versions.
- **Verification:**
  - FogHost 68/68;
  - backend 109/109;
  - typecheck clean;
  - Items + Enemies + Bosses + Fog 40/40 (no conflicts, invariants pass, all skip checks
    clean);
  - 3 full proofs with byte-identical rerun and activation/restore;
  - regressions: Items + Enemies + Fog 5/5 and Items + Fog 5/5.
- **Remaining:**
  - the key-item combined-retry rate with bosses + Fog is 10 of 50 attempts, and no seed
    has exhausted the budget, so the budget is unchanged;
  - the overlay whitelist is fixed to the 15 measured events, so any other boss event
    edited by both engines stays a `FOG_MERGE_CONFLICT`;
  - the FogHost and DsrDataTool test consoles run outside `npm test`;
  - there is no public GPL fork yet;
  - desktop packaging has not started.

FogHost 0.3.1 (2026-09-29): FogMod's boss-trigger rewrite now keeps enclosing DS1 SKIP
counts consistent (vendored patch 7, `tools/fog-host/src/SkipRanges.cs`).

- **Verification:**
  - FogHost self-tests 52/52, including 5 new ones;
  - each of the 11 previously broken FogMod-only events (`11015393`, `11205383`,
    `11215002/3/12/13`, `11405393`, `11415343/383`, `11505393`, `11515393`) checked
    explicitly: 0 mismatches.
- **40-seed sweeps** (live install unchanged):
  - Items + Enemies + Fog 40/40;
  - Items + Fog 40/40.

  Neither had merge conflicts or skip mismatches, in combined packages or in final Fog
  packages (checked against FogMod's templates). Key-item retries are unchanged: 7 of 47
  and 8 of 48 attempts, and no seed exhausted the budget.
- **Remaining:**
  - bosses + Fog is still rejected;
  - the Asylum `11810310` FogMod removal is not skip-adjusted, but it runs only for
    bosses + Fog;
  - the FogHost and DsrDataTool test consoles run outside `npm test`;
  - there is no public GPL fork yet.

FogHost 0.3.0 and backend 0.16.2 (2026-09-29). **Items + Enemies + Fog works on real-format
data: 40/40 seeds.**

- **Changes:**
  - derived SKIP-count adjustments are accepted by the merge under strict conditions;
  - the Fog bridge now reads `randomizer.json` `placements` (the preserved-row list had
    been empty in production);
  - the model-declaration invariant false positive is fixed.
- **Verification:**
  - FogHost self-tests 47/47;
  - DsrDataTool tests 14/14;
  - backend 109/109 with the data root;
  - typecheck clean.
- **Sweep** (`fogHostE2e.mjs`, 40 seeds; live install unchanged):
  - 40/40 succeed, with post-write invariants passing;
  - 0 merge conflicts;
  - combined `verify-skips` 47/47;
  - final Fog packages have 0 skip mismatches in combined or merged events;
  - key items fell outside annotated lots on 7 of 47 attempts (5 seeds retried, none
    exhausted).
- **Remaining:**
  - 11 FogMod-only boss-trigger events have skip ranges broken by stock FogMod's own
    runtime rewrite. This is an upstream observation, not fixed here.
  - Bosses + Fog is still rejected.
  - The FogHost and DsrDataTool test consoles run outside `npm test`.
  - There is no public GPL fork yet.

DsrDataTool skip-count fix (2026-09-28; backend 0.16.1; user-authorized local vendored
modification). The combined engine now keeps DS1 "skip N lines" counts consistent when it
removes or inserts event instructions. Details: `docs/DECISIONS.md`.

- **Tests:**
  - DsrDataTool tests 14/14 (`engines/combined/tests/DsrDataTool.Tests`);
  - FogHost 37/37;
  - backend 108/108;
  - typecheck clean.
- **Real-format 40-seed sweeps**, with every combined package checked by `verify-skips`
  (live install unchanged):

  | Configuration | Result |
  |---|---|
  | Combined-only, Items + Enemies + Bosses | **40/40**; 40/40 packages with 0 skip mismatches (about 134 skips adjusted per package) |
  | Items + Fog | **40/40**; 48/48 skip checks pass; key-item retries on 8 of 48 attempts |
  | Items + Enemies + Fog | **0/40**; 47/47 skip checks pass (38 skips adjusted per package) |

  The Items + Enemies + Fog failures are FogHost merge refusals, not engine errors:
  - `11505392`: rule 4, because the combined engine now correctly changes a SKIP count
    (40 seeds);
  - `11600400`: the adjacency rule (39 seeds).
- **Open decision:** whether rule 4 may accept a skip-count change that is exactly
  explained by the same side's own edits inside that range.

FogHost 0.2.0 (2026-09-28): instruction-level EMEVD merge (user option B), a FogMod
scaling crash fix (patch 6), and boss randomization off by default on the desktop
(desktop 0.4.1).

- **Merge policy:** `docs/FOGHOST_INTEGRATION.md` rule 3 and `docs/DECISIONS.md`.
- **Verification:**
  - FogHost self-tests 37/37 (15 new merge tests, including the `11505392` pattern);
  - backend 108/108;
  - root typecheck clean.
- **Real-format sweeps** (`fogHostE2e.mjs`, `DS1TR_E2E_SWEEP=40`; live install
  unchanged):

  | Configuration | Result |
  |---|---|
  | Items + Fog | **40/40**. Key items fell outside annotated lots on 8 of 48 combined attempts (16.7%); 6 of 40 seeds needed a combined retry; none exhausted the budget. |
  | Items + regular enemies + Fog | **0/40**. 39 are `FOG_MERGE_CONFLICT`: `m15_00_00_00:11505392` in 39 seeds and `m16_00_00_00:11600400` in 38. `m15_00_00_00:11500001` merged at instruction level in 39 seeds. Key items fell outside annotated lots on 7 of 47 attempts (14.9%); 5 seeds needed a combined retry. One transient `DATA_TOOL_FAILED` did not reproduce on rerun. |

  Before patch 6, 11 of 40 enemy seeds crashed with `FOG_ENGINE_FAILED`.
- **Blocker:** both remaining conflicts are caused by the combined engine.
  `DsrDataTool.RemoveEventInstruction` deletes instructions inside SKIP ranges without
  adjusting the line count. One package had 38 such skips in 21 events, and this
  affects combined-only packages too. FogHost refuses to merge them by design. Fixing it
  needs a change to the vendored `DsrDataTool`, which is a user decision.

FogHost v1 (2026-09-28): `tools/fog-host` (FogHost 0.1.0, .NET 8) is built and verified;
`@ds1tr/backend` is 0.16.0.

- **Engine.** FogMod is re-pinned to `6e6dc78`, because `28711c3` cannot run DS1R. The
  host uses SoulsIds `e31a6fd` and `JuicerMV.SoulsFormats` 0.1.3. `GameDataWriter.cs`
  carries a documented patch set: API port, no backups, deferred writes, preserved
  NpcParam rows, and an EMEVD merge hook.
- **Host behavior:**
  - the schema-v2 contract is unchanged;
  - DS1R only, with `dryRun` and `write` modes, and writes only to staging;
  - deterministic output, SHA-256 output list;
  - three-way EMEVD merge;
  - in-memory and post-write invariants;
  - typed failures.
- **Data root.** The FogMod data root is supplied by the user (`DS1TR_FOG_DATA_ROOT`),
  never bundled. It is verified against a 59-file SHA-256 manifest.
- **Backend changes:**
  - a new default `getFogPrerequisites` that receives the Fog language (fixes the
    staged `msg/<language>` file);
  - boss randomization + Fog is rejected with `FOG_BOSS_RANDOMIZATION_UNSUPPORTED`.
- **Verification:**
  - FogHost self-tests: 22/22 with a data root, 12 passed + 10 skipped without one;
  - backend `npm test`: 108/108;
  - root typecheck clean;
  - real-format proof `apps/backend/scripts/fogHostE2e.mjs`:
    - Items + Fog passes 7/7 seeds, including Fog package activation and restore on
      a temp copy (live install unchanged);
    - **regular enemies + Fog fails 5/5**: four seeds hit `FOG_MERGE_CONFLICT` on
      `m15_00_00_00` event `11505392` (both engines edit it), and one seed exhausted
      the key-item retries.
- **Remaining:**
  - enemies + Fog needs an instruction-level merge or a product rule (user
    decision);
  - the desktop default (bosses on, Fog on) is now rejected by design;
  - the FogHost self-tests are not part of `npm test`;
  - no public GPL fork yet.

  Details: `docs/FOGHOST_INTEGRATION.md`, "FogHost v1 implementation".

FogHost preparation (2026-09-28, docs only, no code): the implementation
handoff is `docs/FOGHOST_IMPLEMENTATION_CHECKLIST.md`. It covers:
- the exact pins: FogMod `28711c3`, SoulsIds `e31a6fd` (Apache-2.0, verified),
  and the SoulsFormats candidates;
- the 60-file FogMod data root;
- the project layout, build commands and test plan;
- the `GameDataWriter.cs` patch points.

A throwaway build probe (outside the repo) showed that the audited FogMod
pin `28711c3` cannot generate DS1R (upstream DS3 regression). FogMod
`6e6dc78` + SoulsIds `e31a6fd` + NuGet `JuicerMV.SoulsFormats` 0.1.3, with a
six-rule API port of `GameDataWriter.cs`, builds on net8.0 and runs a
deterministic DS1R dry run and write. Source reading closed two open design
items. FogMod never removes enemy
parts. FogMod `scale` skips non-vanilla NpcParam rows and re-points
`NPCParamID` to clone rows, so the Task 11 "composed scaling" rule and
invariants 1 and 3 need a user decision before the host is built. No plan
task builds FogHost yet.

Task 16 is the final end-to-end audit. `@ds1tr/backend` is 0.15.1.

Fixed:
- **Pristine snapshot omitted `paramdef/`.** The snapshot is the only
  generation source, but `managedRoots` lacked `paramdef`. Without
  `paramdef/paramdef.paramdefbnd.dcx`, DsrDataTool `scan` silently produces
  a catalog with no gifts, drop lots, world lots, starting classes or enemy
  metadata. `patch-enemies` then fails reading it. Task 8 missed this because
  its hand-built copy included `paramdef/`. `paramdef` is now a managed root,
  and a gameState test pins the read-set roots. No snapshots existed yet, so
  no migration is needed.
- `npm test` failed when run from `apps/backend` (npm's cwd). The API
  integration test resolved `apps/backend` from cwd, and Node 24 crashed
  natively on the failed spawn. It now resolves relative to the test file.
- Manifest `appVersion` and `/api/health` report 0.15.1.

Verified (2026-09-28):
- Backend `npm test`: 103/103.
- `apps/backend/scripts/releaseAuditE2e.mjs` (not part of `npm test`; needs
  the live install, built DsrDataTool and .NET 8) passed 18/18. The live
  install was only read. A temp reconstruction of its managed roots (legacy
  `.bak` moved over originals, as in Task 8) went through the production
  path with a temp state root:
  - `importPristine`: 198 files, including paramdef;
  - `generateRun`: combined Items + Enemies with all item options, bosses and
    area scaling, Fog off, 54 patched files including GameParam;
  - a second run with the same seed is byte-identical in `mod/`;
  - no write into the game during generation;
  - `activate`: state `active`, and a second activation is refused;
  - `restore`: the game matches the reconstruction and the pristine store
    still verifies;
  - the live install is byte-identical afterwards.
- Manifest: records the master seed, per-attempt derived seeds and results,
  combined engine version, validation, file hashes, snapshot binding and
  appVersion.
- Licensing: no legacy Python engine code and no FogMod sources are
  tracked. The only vendored code is `kellernz1/ds-randomizer`
  (GPL-3.0-only) with notices. SoulsFormats (GPL-3.0-only, NuGet) is noted in
  both notice files. No game data is tracked.
- Retries are bounded and deterministic, and are recorded per attempt (unit
  tests). Combined retries are exercised only on `GENERATION_FAILED`.

Not verified / release blockers (not fixed here; outside a correctness fix):
- **Fog is not functional.** FogHost is unbuilt and no data root exists.
  No planned task builds it (the Task 12 prerequisite has no numbered task),
  so Task 17's FogHost availability check cannot pass until one is added.
  Fog-enabled generation always returns `FOG_PREREQUISITES_MISSING`. The
  combined -> Fog bridge, Fog retries, the Fog package layout
  (`gameTreeRoot: ''`) and Fog activation have never run. When FogHost is
  built, its read set must fall within the snapshot roots, or staging fails
  closed. The desktop default config enables Fog, so a default run fails
  until then.
- **No in-app recovery from `modified`.** `/api/game/restore` does not expose
  `overwriteUnrecordedChanges`, and the desktop enables restore only for
  `active`/`interrupted`. A Steam file verification leaves stray files in
  managed roots, so the state stays `modified`.
- **No re-import after a game update.** `version-changed` refuses restore,
  and `importPristine` refuses with `SNAPSHOT_EXISTS`. The only way out is
  deleting the state directory by hand.
- Provenance remains "no legacy artifacts", not "proven vanilla" (no
  reference hashes). The real-data proof used reconstructed, not
  verified-vanilla, data.
- Distribution: a public GPL fork with source tags, and FogMod `dist/` data
  licensing, are unresolved (`docs/LICENSING.md`).
- `engines/item/README.md` and `engines/enemy/README.md` still describe
  legacy vendor probes that the backend no longer uses. These are docs only.

Task 15 completes the desktop controls for the supported combined and Fog
options. The run panel now shows the active game package, generation stage and
percentage, logs, failures, and a persistent generated-package summary. A
generated package can be activated only while the current settings still match
the settings used to generate it; changing options also clears a stale plan
preview. Empty seeds and a configuration with no enabled engine cannot start
generation. Desktop version 0.4.0. Desktop build and typecheck passed
(2026-09-28). Fog-enabled generation remains dependent on an unbuilt FogHost;
Task 16 owns the final end-to-end audit.

Task 14 connects the existing backend pipeline and game-state authority to
the desktop application. The backend exposes state, pristine import, streamed
generation, activation, and vanilla restore endpoints. The desktop sends the
typed configuration, displays progress/logs/errors and the generated package,
and enables activation or restore according to the reported game state. A
backend-owned run directory keeps workspace paths out of the renderer. The
new mutation routes reject ordinary foreign browser origins; packaged file
origin remains accepted. Backend 0.15.0, desktop and contracts 0.3.0.
Focused API integration (1/1), pipeline (4/4), backend/desktop builds and
desktop typecheck passed (2026-09-28). FogHost is still unbuilt, so Fog-enabled
generation reports `FOG_PREREQUISITES_MISSING` until that prerequisite exists.
Desktop packaging/startup and full UI polish remain later tasks.

Task 13 adds the single activation/restore authority in
`apps/backend/src/core/gameState.ts` (see `docs/DECISIONS.md`, "One
backend-owned activation/restore authority"). It covers the following:

- a hash-verified pristine snapshot import that refuses legacy `.bak` chains;
- state detection;
- journaled, atomic, verified activation of a generated package;
- automatic rollback;
- vanilla restore.

Destructive operations refuse unknown, stale or running-game states. The
snapshot is now the pipeline's trusted pristine source: `server.ts` injects
`getPristineSource`. The run manifest records `pristineSnapshotId` and
`gameTreeRoot`. No HTTP/IPC endpoints for import/activate/restore exist yet;
that wiring is Task 14. Reference vanilla hashes do not exist, so provenance
is "no legacy artifacts", not "proven vanilla". Backend `npm test` passed
102/102, including 15 focused authority tests (2026-09-28). `@ds1tr/backend`
is 0.14.0.

Task 12 wires the backend generation sequence in
`apps/backend/src/core/generationPipeline.ts`: DSR verification, a verified
pristine-source gate, combined generation in an isolated workspace, output
hash checks, Fog staging and bounded deterministic retries, final output
checks, a package manifest, and typed progress/log events. `/api/randomize`
accepts the generation request and can stream events with
`Accept: text/event-stream`. It never activates files into the game.

The default pristine-source and Fog-prerequisite providers intentionally
return typed `PRISTINE_IMPORT_REQUIRED` and `FOG_PREREQUISITES_MISSING`
failures. No backend pristine authority or built FogHost/data root exists yet;
supplying an arbitrary path is not proof of clean game data. The pipeline can
be exercised with trusted injected providers, but production generation
requires the two separately recorded prerequisite tasks in
`docs/AI_EXECUTION_PLAN.md`. Combined package checks currently cover the
shared patched files described by the bridge; Fog output checks cover files
declared in the host response. Focused pipeline tests and backend build and
typecheck passed (2026-09-28). `@ds1tr/backend` is 0.13.0.

Task 11 defined and implemented the backend side of the Combined -> Fog
compatibility bridge:

- `apps/backend/src/adapters/fogCompatibility.ts` (pure, no I/O):
  - `buildFogCompatibility` derives the schema-v2 `compatibility` object
    from `patch-manifest.json` and the placements file;
  - `fogReadSet` and `planFogStaging` plan the per-attempt staging and
    merge-base directories;
  - `decideFogRetry` is the bounded, deterministic retry table.
- `seeds.ts` gained `deriveAttemptSeeds`; attempt 0 equals `deriveSeeds`.
- `fogHostProcess.ts` moved to schema v2 and validates `compatibility`. It
  also accepts the new codes `FOG_MERGE_CONFLICT` and
  `FOG_COMPATIBILITY_VIOLATED`.

The host-side rules are specified in `docs/FOGHOST_INTEGRATION.md`
("Compatibility bridge"). They cover the three-way EMEVD merge, preserved
NpcParam rows, stock key-item handling and post-write invariants. Fog
scaling and combined scaling compose (decision in `docs/DECISIONS.md`).

Verification: backend tests 83/83 pass, including the build (2026-09-28).
`@ds1tr/backend` is 0.12.0.

Still open:
- the C# host is unbuilt, so none of the host rules is compiled or tested;
- boss randomization together with Fog is unproven (`11810310` conflict);
- the key-item retry rate is unmeasured;
- FogMod's `scale` mechanism is unread, so scaling composition is a
  hypothesis (open item 8).

Task 10 implemented the backend FogHost process adapter in
`apps/backend/src/adapters/fogHostProcess.ts`. It writes a typed schema-v1
request to a fresh JSON path, launches `FogHost.exe generate`, reads the
authoritative response, forwards structured progress and plain log text,
and reports host crashes and timeouts as non-retryable typed failures. It
rejects stale response paths and invalid request options before launch. The
Fog status probe now points to the planned `tools/fog-host` project and
executable. Seven focused tests, backend build, and backend typecheck passed
(2026-09-28). `@ds1tr/backend` is 0.11.0. The C# host is still unbuilt;
Task 11 owns compatibility inputs and Task 12 owns pipeline wiring.

Task 9 designed the FogHost integration (`docs/FOGHOST_INTEGRATION.md`;
decision in `docs/DECISIONS.md`). It is docs only: no code changed and there
is no version bump. The design is a console host that compiles FogMod's
engine sources at the audited commit `28711c3`, never FogMod's CLI (which
forces DS3) or WinForms. It forces DS1R and exchanges JSON request/response
files. FogMod writes DS1 output in place, so each attempt gets a fresh staging
directory, and the host cleans up FogMod's `.bak` files. Errors are typed and
retry-scoped (`combined` vs `fog`). The overlap analysis shows stock FogMod is
lossy on combined output:
- it replaces EMEVD and keeps only legacy-enemy-randomizer event IDs;
- it deletes the combined engine's scaled `NpcParam` rows;
- it finds key items only in annotated item lots.

FogHost therefore carries a minimal patch set in `GameDataWriter.cs` and
`ItemReader.cs`, and Task 11 defines the bridge inputs. Open items:
- no task builds or vendors the C# host yet;
- FogMod's `dist/` data is game-derived and its distribution is unresolved;
- the SoulsFormats/SoulsIds pins and SoulsIds' license are still to be checked.

Task 8 ran the combined Items + Enemies engine end to end for the first time:
typed request → config → vendored JS generator → real `DsrDataTool`
`scan`/`patch-enemies` → workspace package, via
`apps/backend/scripts/combinedEngineE2eProof.mjs` (harness commit
`75c214dc2656693b49c2861c39f3f2a7e6b8e7a8`; not part of `npm test`). The live Steam install is
legacy-modified (legacy item/enemy randomizers and FogMod applied in place);
against it `patch-enemies` correctly refuses (`DATA_TOOL_FAILED`, enemy model
declaration pruning), and the install stayed byte-identical. On a temp copy
of the tool's read set with the legacy `.bak` files restored, all 47 checks
passed (2026-09-28): generation completes, the package's 54 patched files
match `patch-manifest.json`, reruns are byte-identical apart from embedded
workspace paths and timestamps, real catalogs verify and reuse, failures are
typed and recoverable in the same workspace, and neither game directory
changed. This proves the pipeline on real-format data, not on verified
vanilla data. Findings handed to later tasks (up-front detection of
legacy-modified installs, partial seed directories after a failed patch,
missing dll reported as `DATA_TOOL_FAILED`, volatile JSON fields) are in
`docs/COMBINED_ENGINE_INTEGRATION.md` ("End-to-end generation proof").
No application code changed; `@ds1tr/backend` stays `0.10.0`.

Task 7 connected `CombinedRandomizerAdapter.generate()` to the existing
workspace-only execution bridge. It forwards the typed request and
progress/log events, returns the generated seed package directory on success,
and preserves typed bridge failures including retryability. Two focused
adapter tests passed; backend build and typecheck passed (2026-09-28).
`@ds1tr/backend` is `0.10.0`. Implementation commit:
`57d2f846ca362b91673ffcaf3ae025802e5efb64`. The adapter has not yet
run `DsrDataTool` against a real DSR installation (Task 8); generation is
not connected to activation.

Task 6 resolved the remaining combined-engine blockers that had to be fixed
before a real generation run can be attempted: the combined seed is now a
32-hex-character SHA-256 derivation of the master seed that always satisfies
upstream's charset (Blocker 2); `CombinedRandomizerRequest` gained
`generateSpoilerLog`, mapped straight through instead of pinned off
(Blocker 3); and a reused workspace catalog is verified file-by-file (size +
SHA-256) against the game directory before generation, failing with
`CATALOG_SOURCE_MISMATCH` and never rescanning automatically (Blocker 6).
Backend tests 66/66 pass and the full workspace typecheck is clean
(2026-09-28). `@ds1tr/backend` is `0.9.0`. Implementation commit:
`e2664164d1aa1131952e1ddb1a19a87d960198af`. The user confirmed the seed
derivation (option A); `ARCHITECTURE.md`, `source-analysis.md`, and
`CODEX_PROGRESS.md` now mark the earlier pass-through direction superseded.
Task 7 completed adapter wiring. Still open: a real `DsrDataTool` run
(Task 8), the public GPL fork.

Task 5 extended the focused combined bridge tests (commit
`abeb4c2f56ade130a39e4d42837bf333d586e3e0`); no production code changed.

## Backend adapters

- The minimal `CombinedRandomizerAdapter` boundary is defined for combined item
  and enemy generation requests, progress/log events, and typed results.
- The adapter delegates generation to the workspace-only execution bridge and
  maps its package directory and typed failures to the public adapter result.
- The adapter is not yet invoked by the application pipeline. When called, it
  delegates workspace generation to the bridge; activation remains separate.
- A pure, deterministic mapping layer converts combined requests to the
  upstream item/enemy configuration shape, applies parent `enabled` flags, and
  rejects requests with no enabled randomization category. Task 3 verified
  all required item/enemy combinations, parent gating, deterministic output,
  and the case where only safeguard options are selected. Six focused mapper
  tests and backend build/typecheck passed on 2026-09-28. No generation,
  filesystem, subprocess, or game access was added.
- A pure, deterministic workspace planner
  (`combinedRandomizerWorkspace.ts`) computes the catalog path, per-seed
  output directory, and `scan`/`patch-enemies` argument vectors for a
  generation run, and refuses to plan a workspace located inside the live
  game directory. It performs no filesystem I/O and never constructs
  `install`/`restore` arguments. See
  `docs/COMBINED_ENGINE_INTEGRATION.md` ("Workspace execution proof").
- `kellernz1/ds-randomizer` is selectively vendored (the files needed for
  `DsrDataTool.dll`, plus the minimal JavaScript generation core) and pinned
  at commit
  `f120efe0e95b957b7e76109868c3c9966e22720c` under
  `engines/combined/vendor/` (`UPSTREAM_PIN.md` has the full file list and
  update procedure). `DsrDataTool.dll` builds successfully from this
  vendor directory with a locally-installed .NET 8 SDK; see
  `docs/COMBINED_ENGINE_INTEGRATION.md` ("Vendored build proof") for the
  exact command and artifact path.
- `DsrDataToolExecutor` runs only the planner-provided `scan` and
  `patch-enemies` argument vectors through Node `child_process.spawn`, streams
  typed stdout/stderr log events, emits coarse process-status events, and
  returns typed success, process-failure, or spawn-failure results. It is not
  wired into the adapter or generation pipeline yet. Task 1's implementation
  is in commit `5402b447f3d53a1896ca59267fc5fe10461bc071`; its seven
  focused tests, backend build, and backend typecheck passed on 2026-09-27.
  The task remains blocked because Git could not create `.git/index.lock` to
  commit the task's plan/status update under the current filesystem policy.
- The upstream JavaScript generation core is vendored at the same pinned
  commit under `engines/combined/vendor/src/` — five files, the transitive
  closure of `generate()` and `writeOutput()`
  (`core/{config,generator,output,rng}.js`, `data/prototype-data.js`), with a
  closed import graph of Node builtins only. CLI, HTTP, Electron, share-link,
  catalog-loading, and game-detection modules are deliberately excluded.
- `combinedRandomizerCore.ts` is the backend's single loading boundary for
  that core: a computed dynamic `import()` of `file:` URLs resolved from
  `import.meta.url`, hand-written types for the six upstream exports (so the
  vendored bytes stay byte-identical), and typed `JS_CORE_MISSING` /
  `JS_CORE_INCOMPLETE` failures instead of thrown errors. It loads and
  shape-checks the core; it does not call `generate()` or `writeOutput()`
  yet. See `docs/COMBINED_ENGINE_INTEGRATION.md` ("JS core vendor proof").
- Vendored-file identity is verified by comparing Git blob object IDs against
  the pinned upstream commit, not by `diff`: `core.autocrlf` rewrites line
  endings on Windows checkout. All twelve vendored files verified; object IDs
  recorded in `engines/combined/vendor/UPSTREAM_PIN.md`.
- Documentation correction: upstream `validateConfig(config, { requireGame })`
  **returns** a `string[]` of errors and never throws. The earlier analysis
  said it throws; consumers must check the returned array.
- A minimal combined-engine execution bridge
  (`apps/backend/src/adapters/combinedRandomizerBridge.ts`,
  `executeCombinedGeneration`) now chains all five previously independent
  pieces into one internal path: typed request → pure config mapping → the
  vendored JS core (`normalizeConfig`/`validateConfig`/`generate`/
  `writeOutput`) → the pure workspace planner → the `DsrDataTool` executor
  (`scan` when the workspace has no catalog, then `patch-enemies`). It
  returns a typed success carrying the package directory, placements path,
  `patch-manifest.json` path, upstream version, placement hash, and seed, or
  one of fifteen documented failure codes (`CATALOG_SOURCE_MISMATCH` added
  by Task 6). Only `GENERATION_FAILED` is
  marked retryable. See `docs/COMBINED_ENGINE_INTEGRATION.md` ("Execution
  bridge proof") for the exact data flow.
- Generation is proven workspace-only against fixtures: 19 focused tests run
  the real vendored `generate()`/`writeOutput()` in a temporary workspace
  with a synthetic schema-17 catalog, faking only the `DsrDataTool` process
  layer. They assert a real package is produced with
  `dataStatus: "extracted"`, that the process layer receives the planner's
  exact `scan`/`patch-enemies` argument vectors and nothing else, that the
  placements file exists before `patch-enemies` is invoked, that a fixture
  game directory is byte-identical before and after a successful run, and
  that the same request twice yields the same placement hash. Backend tests
  54/54 pass; backend build and the full workspace typecheck are clean
  (2026-09-28).
- The bridge rejects `dataStatus !== "extracted"` before writing any file and
  before spawning any process, so integration Blocker 1's silent prototype
  fallback can no longer reach a package directory. It also pre-checks
  `catalog.schemaVersion !== 17` so an obsolete catalog reports
  `CATALOG_STALE` instead of an opaque generation error.
- The bridge plans the workspace from the **normalized** upstream seed rather
  than the raw request seed, because upstream derives its own
  `seed_<safeSeed>` package directory from the normalized value; planning
  from the raw value would point `--placements` at a file upstream never
  wrote. See `docs/DECISIONS.md`.
- Three upstream config keys are pinned by the bridge rather than inherited
  from `defaultConfig`: `generateSpoilerLog: false` (no request field exists —
  Blocker 3 — and the upstream default `true` would emit an unrequested
  spoiler file), plus `useExtractedData: true` and `dryRun: false`, both
  verified inert at the pinned commit (read nowhere in the vendored core or
  `DsrDataTool`).
- Fixed: `core/pipeline.ts`'s combined-engine probe pointed at
  `vendor/src/cli.js` and `vendor/DSR-Randomizer.exe`, neither of which
  exists or should, so engine status always reported the combined engine as
  missing. It now probes `vendor/src/core/generator.js` and the built
  `vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll`, with a test
  asserting the combined engine reports `source` mode. The bridge itself
  never consults `ExternalAdapter`.
- Still not done: the bridge has never executed the real `DsrDataTool` against
  a real Dark Souls installation, so a genuine `scan`/`patch-enemies` round trip is
  unproven. Blockers 2, 3, and 6 were resolved by Task 6 (see
  `docs/COMBINED_ENGINE_INTEGRATION.md`, "Blocker resolution"); the public
  GPL fork remains open.
