# Audit: "Replace regular enemies with: Bosses only"

## Owner-approved "Only where they fit" correction (2026-10-15; unreleased)

This is the current worktree behavior. The exact old classification remains preserved in `placement-audits/boss-fit-classification-pre-nexus-2026-10-15.md` and `.json`; the real-catalog before/after, diversity, matrix, and encoded-output evidence is in `placement-audits/boss-fit-validation-2026-10-15.json`.

The old classes came from the regular-slot footprint thresholds applied to each boss source slot's measured capsule. No old roaming boss had a source capsule below class 2; only Pinwheel reached class 2. This left 164 class-0 slots and 990 class-1 slots with no eligible archetype. This was a policy gap in the fit classes, not a planner failure. The owner set the following minimum destination classes; the existing class-based rule remains `bossMinimumClass <= destinationClass`:

| Boss | Old minimum / pool status | Current minimum |
|---|---:|---:|
| Knight Artorias | 3 | 0 |
| Capra Demon | 3 | 0 |
| Pinwheel | 2 | 0 |
| Taurus Demon | 3 | 2 |
| Dark Sun Gwyndolin | excluded | 0 |

All other roaming classes are unchanged, including Moonlight Butterfly's authored class-3 override. No other excluded boss was added. Pinwheel Chaos still filters the original Pinwheel source from the arena-boss pool; the roaming candidate builder now explicitly keeps the archetype in the roaming pool in either setting.

| Destination class | Old slots | Old fitting archetypes | Old `fit` survivors | Current slots | Current fitting archetypes | Current `fit` survivors |
|---:|---:|---:|---:|---:|---:|---:|
| 0 | 164 | 0 | 164 | 164 | 4 | 0 |
| 1 | 990 | 0 | 990 | 990 | 4 | 0 |
| 2 | 387 | 1 | 0 | 387 | 5 | 0 |
| 3 | 166 | 10 | 0 | 166 | 11 | 0 |
| 4 | 0 | 15 | 0 | 0 | 16 | 0 |
| 5 | 9 | 17 | 0 | 9 | 18 | 0 |

The same real catalog and exact Bosses-only, 100%, Random difficulty, type replacement disabled, Prevent same off configuration changed from **562/1,716 (32.75%; 1,154 fit survivors)** to **1,716/1,716 (100%; zero fit survivors)**. This is fit eligibility; separate production package seeds may still have explicit model-budget survivors.

The real-catalog engine matrix is deterministic on rerun for all eight cases: Bosses-only 100% and 50%; Both 100% and 50%; Prevent same on; a loose difficulty curve; Type replacement on; Pinwheel Chaos on. The 50% Bosses-only case placed 862 bosses and left 854 not participating. Both 50% placed 694 bosses, drew 818 normal enemies, and recorded 204 model-budget survivors. The loose curve recorded 683 difficulty survivors. These are explicit chance/model-budget/difficulty outcomes; none were `fit` survivors. Real package validation also passed for Bosses-only 100% fit and Both + Fog: zero non-deterministic reruns, event-owner failures, or event-window overruns, and the Fog merge completed without conflict. One production package seed had 3 model-budget survivors; those are independent of fit.

Across the baseline-seed Bosses-only 100% output, the frequency of all 18 archetypes is recorded in the JSON report. Current counts are: Asylum Demon 1, Stray Demon 2, Capra 378, Taurus 106, Smough 16, Super Smough 18, Moonlight Butterfly 16, Pinwheel 352, Artorias 386, Manus 14, Sif 0, Nito 0, Ornstein 19, Super Ornstein 1, Quelaag 12, Firesage 0, Iron Golem 0, and Gwyndolin 395. Gwyndolin is most common; among those that appeared, Asylum Demon and Super Ornstein are least common at one each. Pinwheel occupies 20.51% of placements. No destination class or map is limited to one archetype; classes 0 and 1 have four candidates each, class 2 has five.

Gwyndolin passed static integration and final-output validation through the generic roaming path: its model is present in the destination MSB, its scaled NpcParam and cloned NpcThinkParam rows exist, its wake-up event is owned once, and the generated output uses generic roaming souls/respawn handling. No dependency on his original arena event script or arena object was found. This does **not** establish collision, movement, or overall gameplay safety; the manual owner QA cases below are still required.

### Borderline owner gameplay QA

Settings for all cases: Items OFF; Fog OFF; ordinary enemies ON; arena-boss randomization OFF; replacement mode Bosses only; boss chance 100%; Only where they fit; Random difficulty; Type replacement disabled; Prevent same OFF; Pinwheel Chaos OFF. Roaming boss souls remain at the default 50%, and roaming bosses respawn (default). These outputs are candidate placements for manual inspection, not a claim of physical safety.

| Boss | Seed Code | Map / destination slot | Class | Why inspect |
|---|---|---|---:|---|
| Artorias | `DS1TR1-06NWCQQ3-32HXEE10-CZNTCSCP-X3K2DSMN-4VKEG5H6-X8D1GP0G-NHYX02YS-T7N6FA2P-7ZMEBH0G-NNZS0JR2-T8N1YT66-GQAKVR2J-CGGEP7B8-R265CF2M-N1W00KMX-X8G0` | Blighttown `m14_00_00_00:c3090_0034` | 0 | Narrow scaffold / ledge |
| Capra Demon | `DS1TR1-06NWCQQ3-32HXEE10-CZNTCSCP-X3K2DSMN-4VKEG5H6-X8D1G60J-NHYX02YS-T7N6FA2P-7ZMEBH0G-NNZS0JR2-T8N1YT66-GQAKVR2J-CGGEP7B8-R265CF2M-N1W0076Z-1YQ0` | Blighttown `m14_00_00_00:c3090_0036` | 0 | Adjacent narrow scaffold |
| Pinwheel | `DS1TR1-06NWCQQ3-32HXEE10-CZNTCSCP-X3K2DSMN-4VKEG5H6-X8D1G60H-NHYX02YS-T7N6FA2P-7ZMEBH0G-NNZS0JR2-T8N1YT66-GQAKVR2J-CGGEP7B8-R265CF2M-N1W00N7B-0G10` | Tomb of the Giants `m13_01_00_00:c2940_0000` | 0 | Tight bonewheel route / corridor |
| Dark Sun Gwyndolin | `DS1TR1-06NWCQQ3-32HXEE10-CZNTCSCP-X3K2DSMN-4VKEG5H6-X8D1G60H-NHYX02YS-T7N6FA2P-7ZMEBH0G-NNZS0JR2-T8N1YT66-GQAKVR2J-CGGEP7B8-R265CF2M-N1W00N7B-0G10` | Catacombs `m13_00_00_00:c3300_0000` | 0 | Narrow corridor; also exercises his generic wake-up and cloned-AI path |
| Taurus Demon | `DS1TR1-06NWCQQ3-32HXEE10-CZNTCSCP-X3K2DSMN-4VKEG5H6-X8D1H60H-NHYX02YS-T7N6FA2P-7ZMEBH0G-NNZS0JR2-T8N1YT66-GQAKVR2J-CGGEP7B8-R265CF2M-N1W00PGW-MMF0` | Blighttown `m14_00_00_00:c2810_0000` | 2 | Smallest selected class-2 approach slot |

No local golden fixture was edited. The Seed Code wire format, generation profile identifiers, and randomizer RNG salt remain unchanged; the unreleased worktree intentionally uses the corrected classifications for current local generation. The pre-change snapshot remains the manual restore reference. No release or external upload was performed.

## Follow-up: real-catalog correctness audit and profile v2 (2026-10-12)

This section supersedes the `0.45.5` conclusions below for **new runs**. The earlier engine behavior is now frozen as `ds1tr-gen-v1`; `ds1tr-gen-v2` is current because these roaming-boss settings were already in public prerelease `0.38.4`. See `GENERATION_COMPATIBILITY.md`.

### What the offline reproduction establishes

The reported in-game result of only 3–4 Parish bosses was **not reproduced** by the production planner on the clean extracted catalog. Under the old v1 policy, Parish had 198 ordinary destinations; nine were protected (eight `eventModelLocked`, one passive Titanite Demon), and the other 189 all became bosses at 100%. Blighttown had 168 destinations and no protected slots; all 168 became bosses. This leaves that exact in-game Parish observation unresolved without gameplay/runtime evidence.

The broader 100% contract did have two proven gaps:

1. **G-15..G-18 hid 157 destinations per world** from roaming bosses in v1: passive slots 20, event-model-locked 49, tangible ghosts 34, Darkroot butterfly path 54. The ordinary-replacement handlers already support those behaviors. V2 routes those four groups through the corresponding sanitation, grounding/tangibility, path activation, and passive-until-attacked logic. A Parish roaming boss in the passive bonfire slot also has its AI held until first attack. V1 retains all four exclusions.
2. **`both` + boss chance 100% built and discarded a full ordinary assignment before the boss pass.** The ordinary models it introduced consumed the map's distinct-model budget, leaving 35 model-budget misses across the three v1 reproductions (4,642 of 4,677 eligible destinations got bosses). V2 skips that discarded assignment only for `both` + 100%; it does not remove or raise the model budget. In the new profile both `bosses` + 100% and `both` + 100% produce the same boss destination set.

The real safety invariant remains: final distinct models per map may not exceed `max(30, model-set size before the roaming pass)`. A repeated boss whose model is already loaded adds zero distinct models; an additional boss model consumes one new model slot. This is a distinct-model bound, not a per-boss count cap. `stay dead` only changes the respawn field and has no effect on destination selection.

### Per-map diagnostics and real-catalog matrix

Counts below are from `placementMatrix.mjs bosses-only` on the clean QA catalog. A/B refer to the two user configurations (Bosses only / Both). Three deterministic seeds were run per core chance case; each eligible slot was audited through the production data tool and final files.

| Case | Eligible destinations | Bosses | Normal draw | Unchanged reasons | Event/window / determinism |
|---|---:|---:|---:|---|---|
| Bosses only, 0% | 5,148 across 3 seeds | 0 | 0 | 5,148 not participating | no window overflow or nondeterminism |
| Bosses only, 50% | 5,148 | 2,600 | 0 | sampled participation; no unexplained 100% survivor | no window overflow or nondeterminism |
| Bosses only, 100% | 5,148 | 5,148 | 0 | all reason counters 0 | no window overflow or nondeterminism |
| Both, boss chance 0% | 5,148 | 0 | normal sampler | no bosses selected | no window overflow or nondeterminism |
| Both, boss chance 50% | 5,148 | 2,533 | normal fallback retained | 53 boss candidates blocked by the existing model budget | no window overflow or nondeterminism |
| Both, boss chance 100% | 5,148 | 5,148 | 0 | all reason counters 0 | max 208 events; no window overflow or nondeterminism |

For the 100% rows, all eligible slots participated; `easyAsylum`, `fit`, `preventSame`, `difficulty`, `modelBudget`, `eventLimit`, and `other` are zero. Aggregate destinations are 1,716 per seed. On the Parish map (`m10_01_00_00`), v2 covers all 198/198 destinations; on Blighttown (`m14_00_00_00`), all 168/168. The old v1 Parish result was 189 bosses plus its nine protected slots; Blighttown was already 168/168 under v1. Additional matrix runs covered stay-dead ON/OFF, Only where they fit, representative difficulty curves, Prevent same ON/OFF, Type replacement ON/OFF and Fog ON/OFF. The stay-dead option did not change counts. The Fog rows completed without merge conflict; the wider run's owner audit reported no missing or duplicate event owners.

Detailed v2 diagnostics for the retained 100% seed:

| Map | Destinations | Protected by reason | Eligible | Not participating | Selected / boss | Normal draw | Unchanged (all reasons) | Distinct boss models | Final models / limit |
|---|---:|---|---:|---:|---:|---:|---|---:|---:|
| Undead Parish `m10_01_00_00` | 198 | none | 198 | 0 | 198 / 198 | 0 | 0 | 14 | 27 / 31 |
| Blighttown `m14_00_00_00` | 168 | none | 168 | 0 | 168 / 168 | 0 | 0 | 16 | 20 / 30 |

The model-set baseline before roaming was 31 in Parish (effective limit 31), 18 in Blighttown (minimum limit 30), and 8 in the Kiln map used by the Black Knight sample (minimum limit 30). At 100%, both `easyAsylum` and all seven unchanged-reason counters (`easyAsylum`, `fit`, `preventSame`, `difficulty`, `modelBudget`, `eventLimit`, `other`) were zero for both reported maps.

A paired production run used the same master seed for Bosses only and Both + 100%: each produced 1,716 boss slots and the sorted slot-ID sets were exactly equal (zero IDs present in only one set). Chosen boss models per slot may differ because Both consumes its category draw; the user-visible destination contract is the same set of slots.

### Slot and candidate forensics

For the unrestricted Random/Anywhere case, the planner starts with 17 portable roaming archetypes (listed in section “What the legacy tool does”/the candidate description below); the model-budget check then removes only candidates that would exceed the distinct-model bound. At v2's 100% configuration every destination receives a valid candidate, and its model budget has no unchanged rows. Representative choices from the final-output audit:

| Slot | Map | Original → planned/final boss | V2 eligibility/result | Model-budget interpretation |
|---|---|---|---|---|
| `m18_00_00_00:c2790_0001` | Kiln | Black Knight `c2790` → Quelaag `c5280` | selected; final MSB/params encode `c5280` | source model count 10; model set 8→9, limit 30; boss model is new (+1). This Black Knight is not a last copy, so this sample shows no special rarity advantage |
| `m10_01_00_00:c2500_0000` | Undead Parish | Hollow `c2500` → Artorias `c4100` | selected; final output matches plan | source count 59; model set 30→31, limit 31; new boss model costs +1 distinct slot |
| `m10_01_00_00:c2560_0000` | Undead Parish | Balder Knight `c2560` → Manus `c4500` | selected; final output matches plan | source count 8; model set 30→31, limit 31; new boss model costs +1 |
| `m14_00_00_00:c2810_0000` | Blighttown | Infested Barbarian `c2810` → Taurus Demon `c2250` | selected; final output matches plan | source count 12; model set 29→29, limit 30; Taurus is already loaded, so the boss adds +0 |
| `m10_01_00_00:c2793_0000` | Undead Parish | rare source `c2793` → Stray Demon `c2230` | selected; final output matches plan | source count 1; model set 29→29, limit 31; last `c2793` is removed (-1) while new `c2230` is added (+1), net 0 |

With Anywhere, Random difficulty, and Prevent same off, the unbudgeted pool is 17 archetype choices represented by these 16 model IDs: Stray Demon `c2230`, Demon Firesage `c2231`, Asylum Demon `c2232`, Capra Demon `c2240`, Taurus Demon `c2250`, Iron Golem `c2320`, Smough variants `c2360`, Moonlight Butterfly `c3230`, Pinwheel `c3320`, Artorias `c4100`, Manus `c4500`, Sif `c5210`, Nito `c5220`, Ornstein variants `c5270`/`c5271`, and Quelaag `c5280`. The original-model counts and live model-set sizes above were captured at each slot's planner decision, not inferred from final counts.

Candidate telemetry does show a **loaded-model acceptance bias near the cap**, not a hard-coded Pinwheel/Butterfly preference. Once an archetype is already in a map, another copy costs no new model slot and remains admissible; a not-yet-loaded archetype may be rejected if it would exceed the bound. In the recorded candidate attempts: Moonlight Butterfly `c3230` was accepted 122 times and budget-rejected 4; Pinwheel `c3320` 110/26; Smough `c2360` 221/22; Ornstein `c5270` 109/36. These are planner attempts over the audited worlds, not uniform world-wide spawn percentages. The observed repetition is explained by the distinct-model constraint plus the random order in which models first enter a map; it does not bias participation by the source enemy.

The rare-model hypothesis is **mechanically confirmed but not sufficient to explain Black Knight placement**: the single-copy `c2793` trace removes its original model while adding the new boss model, so the set size is unchanged. The sampled Black Knight has ten copies and its replacement increases the set size by one. The planner does not prefer Black Knight slots, and the new profile removes the separate 100%-Both discarded-normal-pass bias. In the v2 unrestricted 100% runs every ordinary destination, common or rare, was selected.

### Planner vs final output and compatibility

The full-slot CSV at [`placement-audits/bosses-only-100-seed-d4a649e430e4222ab4546ecb20a38c3b.csv`](placement-audits/bosses-only-100-seed-d4a649e430e4222ab4546ecb20a38c3b.csv) records every one of 1,716 ordinary destinations: original model, planned boss, final encoded boss, eligibility, selection, reason, output verification and event-owner result. All 1,716 planned boss models match the final encoded MSB/NpcParam/NpcThinkParam data; an independent `SmokeVerify audit` checked 1,757 placements with zero missing or duplicate event owners. This is a planner success, not a DataTool drop: **the earlier failures were planner-side; v2 now closes both audited planner causes**.

The four `ds1tr-gen-v2` roaming-boss golden cases pass. No v1 golden fixture was edited. Full golden verification remains partly blocked by the local QA environment: Fog-enabled fixture hashes differ from its current Fog data root (including the Fog-only `world-options` control), and 16 key/progression cases lack the progression model JSON. These failures do not involve the new v2 fixtures or the offline Fog reconstruction checkpoint, which was not modified. Desktop stays 0.39.0; no game was launched. Navigation, AI activation, grounding, cramped geometry, extreme-density performance and reload behavior remain gameplay-unverified.

### Focused audit: "Only where they fit", 100%, Random

**Result: no planner defect reproduced and no boss size override is justified by the available game data.** The planner computes `sizeOk` before drawing. On the clean DSR catalog, every selected destination with at least one fitting boss received one; every vanilla survivor was counted as `fit`. `preventSame`, difficulty, model budget, event limits and other reasons were all zero. The reported ~95% result is reproducible for the combined Undead Burg/Parish MSB, but it is not representative of the whole world: that MSB contains 187 class-1 slots and only one class-0 slot, and no current boss candidate fits either class.

The audit added read-only per-map `fitCandidateAnalysis` diagnostics for ungrouped fit runs. It reports destinations by size class, fitting archetype count, slots with zero/one/two/three-or-more candidates, and unchanged `fit` slots. It reads existing planner inputs before any draws and does not affect RNG or placement.

On the fixed-seed production plan (`bosses only`, 100%, `fits`, Random, Type replacement disabled, Prevent same off), the planner saw 1,716 destinations: 1,702 regular destinations plus 14 supported ordinary dragon destinations. It placed 562 bosses (32.75%) and kept 1,154 (67.25%) for the explicit `fit` reason. Candidate classes below are the effective boss size class after retaining the existing Moonlight Butterfly override; there are 17 archetypes represented by 16 model IDs.

| Destination class | Ordinary destinations | Fitting boss archetypes | Slots with 0 / 1 / 2 / 3+ fitting bosses | Vanilla `fit` survivors |
|---:|---:|---:|---:|---:|
| 0 | 164 | 0 | 164 / 0 / 0 / 0 | 164 |
| 1 | 990 | 0 | 990 / 0 / 0 / 0 | 990 |
| 2 | 387 | 1 | 0 / 387 / 0 / 0 | 0 |
| 3 | 166 | 10 | 0 / 0 / 0 / 166 | 0 |
| 4 | 0 | 15 | 0 / 0 / 0 / 0 | 0 |
| 5 | 9 | 17 | 0 / 0 / 0 / 9 | 0 |
| **Total** | **1,716** | — | **1,154 / 387 / 0 / 175** | **1,154** |

The 17 candidate archetypes classify as: class 2: Pinwheel (1); class 3: Capra, Taurus, two Smough NPC variants, Moonlight Butterfly, Artorias, Manus, Ornstein, Quelaag (9); class 4: Firesage, Iron Golem, Sif, Nito, alternate Ornstein (5); class 5: Stray Demon and Asylum Demon (2). Classes 0 and 1 have no fitting archetype. Pinwheel is already the only class-2 candidate, which explains its repetition in those slots when Prevent same is off.

| DSR MSB/map | Destinations by class (0/1/2/3/4/5) | Boss | Vanilla `fit` | `fit` share |
|---|---|---:|---:|---:|
| Undead Burg / Undead Parish (`m10_01_00_00`) | 1 / 187 / 8 / 1 / 0 / 1 | 10 | 188 | 94.9% |
| Darkroot Garden and Basin (`m12_00_00_00` + `m12_00_00_01`) | 2 / 110 / 10 / 46 / 0 / 4 | 60 | 112 | 65.1% |
| Sen's Fortress (`m15_00_00_00`) | 0 / 13 / 32 / 5 / 0 / 3 | 40 | 13 | 24.5% |
| Anor Londo (`m15_01_00_00`) | 0 / 26 / 32 / 32 / 0 / 0 | 64 | 26 | 28.9% |
| Blighttown / Quelaag's Domain (`m14_00_00_00`) | 46 / 104 / 12 / 5 / 0 / 1 | 18 | 150 | 89.3% |

Undead Burg and Parish are one MSB/catalog map, so the available source data cannot split their counts. Darkroot Garden and Basin are represented by two IDs with matching diagnostic totals. No lower size override was applied to Pinwheel, Capra, Artorias, or Ornstein: their extracted capsules put them in classes 2/3, and neither a smaller DSR collision capsule nor existing placement evidence supports lowering them into class 0/1. Moonlight Butterfly's existing class-3 override remains. There is therefore no generation-output change from this task, and `ds1tr-gen-v1` remains untouched.

The production `roaming-e1 / roaming-bosses-fits` one-seed run passed (1/1, zero retries, zero event-window overruns, zero event-owner failures). The generated package was rescanned with DsrDataTool: all 562 planned roaming placements matched final MSB model/NpcParam/AI identifiers and final NPC Param soul values; mismatches 0. Public profile check: the local `v0.38.4` tag has only `ds1tr-gen-v1`; the recorded public baseline is 0.38.4. GitHub network access was unavailable in this session, so the remote release list could not be queried independently.

Date: 2026-10-08 (backend 0.45.5, desktop 0.35.1). Trigger: a real generated world (Enemy randomization on, Pick a replacement for every spot, Replacement chance 100%, **Bosses only**, Anywhere, Random + Easy Asylum, Boss randomization on with the classic arena shuffle, Roaming boss souls 50%, stay dead on) in which about 90% of the ordinary enemies were unchanged and some changed ones were another ordinary enemy.

## Root cause

Three independent things, found in this order of size.

1. **A fixed cap of 10 roaming bosses per map** (`ROAMING_BOSS_MAP_CAP = 10`, `sampling.js`, introduced in Part E1, commit `387f155`, 2026-10-06). The planner stopped placing bosses after ten in a map. A world has 1,702 ordinary destinations in 17 maps; 170 got a boss (the engine counted `roamingMapLimitReached: 1351`). That is the "1 in 10". The cap was an interim guardrail ("bounds the growth of the custom event window and keeps a map playable", `ENEMY_PLACEMENT_MODEL.md` 10.7 deviation 2, `EVENT_ALLOCATION.md`), not a game constraint, and the legacy tool has nothing like it.
2. **The dragon plan permuted ordinary dragon-class enemies among themselves**, whatever "Replace regular enemies with" said (`buildDragonPlan`, `generator.js`: `simpleRegularDragons` is exchanged whenever Enemy randomization is on). Titanite Demons, Drakes and the hostile Bounding Demons were exchanged for each other (60 `dragon-only-group-permutation` placements in the owner's world): **ordinary enemy replaced by another ordinary enemy under "Bosses only"**. Those slots were also reserved (`reservedSlotIds`), so the roaming pass could not give them a boss.
3. **Easy Asylum** keeps the Undead Asylum as it is: bosses are never among the easiest candidates (class <= 1), so all 26 eligible Asylum destinations stay vanilla. That one is intended (and explained below); it is not a cause of the rest. **Correction (2026-10-08, `docs/GENERATION_GUARD_AUDIT.md`, G-12b):** "intended" was incomplete: with Bosses only the legacy puts Capra, Taurus or the Bell Gargoyles into twelve Asylum slots and an ordinary boss into the rest, while DS1TR leaves the whole Asylum vanilla; that difference is unreviewed.

Nothing in the boss-arena options (Boss randomization, the classic arena shuffle) restricts the ordinary destinations, except that the dragon plan reserves dragon-class slots (point 2): the arena options and the ordinary-enemy options were coupled there.

## What the legacy tool does (LEGACY CODE VERIFIED, `everKalle/Dark-Souls-Enemy-Randomizer` at `83f008d`, `randomizer_rng.py`)

* Per ordinary creature (`creatureType == "0"`, line 1521 ff.): `if randint(1, 100) <= replaceChance` (the participation draw), then `enemyMode == 1` ("replace with bosses only") calls `GetBossEnemy(..., canBeNormal=False)` (line 1540); mode 2 `GetNormalEnemy`; mode 3 `GetNormalOrBossEnemy` (a boss with probability `bossChance`, otherwise a normal enemy).
* **No count limit on bosses.** The only limit is the number of DISTINCT replacement models per map, `MAX_UNIQUE` (30; 50 in Blighttown on the Remaster, lines 203, 1363-1367). When `len(uniqueIndices) >= MAX_UNIQUE` the choice is made from `uniqueBosses[size]`, the bosses already used in that map (lines 950, 962-967): **the same boss is reused as often as it comes up**. There are about 22 boss models, so with Bosses only the limit is never reached.
* A slot where no boss is found (`GetEnemyFromListWithRetry` returns -4, an empty difficulty list gives -6, `newChar < 0`) is **left unchanged**: the `if newChar >= 0` block (line ~1598) writes the replacement only for a found enemy. With `canBeNormal=False` there is **no fallback to a normal enemy**; with mode 3 (`canBeNormal=True`) there is.
* Size: `maxCreatureSize = creatureSize` under "fit", else 5 (any); the lists are per size class (`validSizeBosses`).
* Difficulty: `getDifficultyList` for the curve modes; Easy Asylum (`diffmode >= 3`) only for `mapname == "m18_01_00_00"` and only for the twelve `EASYASYLUM_TARGETS` (lines 77-81, 931, 969): those get one of the hardcoded Asylum lists (`HARDCODED_ASYLUM_BOSSES = [8, 9, 118]` with Bosses only).

So legacy "Bosses only" at 100% gives every ordinary slot a boss, reuses bosses freely, and leaves a slot as it is only when nothing fits.

## The current pipeline for one ordinary destination (sampling-v1, 100%, Bosses only), before and after

| # | Step | Where | Before | After |
|---|---|---|---|---|
| 1 | Destination eligibility | `regularEnemyContext`, `roamingProtectedReasons` (`generator.js`) | regular slots, minus dragon-class slots reserved by the dragon plan, minus protected slots (passive, event-model-locked, tangible ghosts, the Darkroot butterfly path) | same, **plus** the dragon-class ordinary slots that have no linked parts (Titanite Demons, Drakes, the Bounding/Undead Dragon bodies) are destinations of the roaming pass |
| 2 | Replacement chance | `planRoamingBosses`, `subject.first()` | the first draw of the slot's stream, independent of everything else | unchanged |
| 3 | Candidate pool | `deriveRoamingBossTypes` | 17 roaming boss archetypes | unchanged |
| 4 | Normal-enemy handling | `sampleExtractedEnemies` | the sampler runs with `normalReplacement: "none"` for Bosses only: no ordinary replacement exists to fall back to | unchanged; the dragon-class permutation no longer touches slots that take a boss |
| 5 | Boss candidate handling | `planRoamingBosses` | uniform draw with list shrink | unchanged |
| 6 | Roaming classification | `decorateEnemyPlacements` | `sampled-roaming-boss`, vanilla scaling, souls percent, stay-dead bit, wake-up event | unchanged |
| 7 | Model budget | `planRoamingBosses`, `limit = max(30, size before)` | distinct models only (instances are not counted); repeated bosses cost nothing | unchanged |
| 8 | Event allocation | DsrDataTool custom window | one wake-up event per roaming boss | unchanged |
| 9 | Map-level caps | `ROAMING_BOSS_MAP_CAP` | **10 per map** | **removed** |
| 10 | Fallback | `planRoamingBosses` | a slot with no boss keeps its own enemy ("bosses") or the sampler's normal enemy ("both") | same, with the reason counted |
| 11 | Easy Asylum | `EASY_ASYLUM_MAP`, `EASY_ASYLUM_MAX_CLASS` | Asylum slots only take candidates of class <= 1 | unchanged (explicit protection, counted as `easyAsylum`) |
| 12 | Emission | `generate()` | roaming placements + dragon plan placements | roaming placements + dragon plan placements **minus the ones whose slot took a boss** |

## Every limit that touches bosses in ordinary slots

| Limit | Where | Value | Counts | Reason introduced | Status |
|---|---|---|---|---|---|
| `ROAMING_BOSS_MAP_CAP` | `sampling.js` | 10 | boss INSTANCES per map | E1 guardrail: "bounds the activation events of a map and keeps a map playable" | **removed**; not a game constraint: a map with 189 roaming bosses writes up to 207 events, all unique (real pipeline, below); Lost Izalith had needed 122 in earlier sweeps |
| Model budget `max(30, baseline)` | `planRoamingBosses`, `MODEL_BUDGET_CAP` | 30 distinct models per map | DISTINCT MODELS, not instances | the game's loaded-model limit; the legacy `MAX_UNIQUE` (30 / 50 in Blighttown) | **kept**: a real constraint. It counts distinct models, so 189 bosses use at most 16 models. It binds only in "Bosses or regular enemies" at 100% (65 of 1,559 spots per world keep the regular enemy the sampler chose) because there the sampler has already filled the model set; with Bosses only it never bound |
| Easy Asylum class limit | `EASY_ASYLUM_MAX_CLASS` | 1 | Asylum destinations | legacy Easy Asylum | kept, Asylum only |
| Protected slots | `roamingProtectedReasons` | 157 destinations per world | passive (Asylum, New Londo, the Kiln knight, the Parish bonfire Titanite Demon: 20), event-model-locked (49), tangible ghosts (34), the Darkroot butterfly path (54) | slots whose events or behavior a boss would break | kept; counted by reason |
| Hostile NPC cap | `HOSTILE_NPC_MAP_CAP` | 12 | NPC bodies | Part F1 | unrelated, untouched |
| Event window | `docs/EVENT_ALLOCATION.md` | grows with the number of events | events | one window per owner file | **no ceiling found** |

## What changed (backend 0.45.5)

1. The per-map cap is removed. `planRoamingBosses` has no count limit; every destination that takes part and can take a boss gets one. The counter `roamingMapLimitReached` no longer exists.
2. The dragon-class ordinary slots without linked parts are destinations of the roaming pass (`roamingDragonSlots`, `buildDragonPlan.overridableSlots`). A boss replaces the dragon the plan had put there; that dragon is simply not placed. The bridge Hellkite, the Anor Londo gargoyles and the hydras (linked parts), and the slots that are dummy parts or neutral-team bodies, stay with the dragon plan. With the ordinary modes the dragon plan is unchanged.
3. Per-map diagnostics (`placementDiagnostics.summary.roaming`, in the run manifest): destinations, protected by reason, eligible, not participating, selected, boss, normal draw, unchanged by reason (`easyAsylum`, `fit`, `preventSame`, `difficulty`, `modelBudget`, `eventLimit`, `other`), distinct boss models, final model count and limit. They are internal (the manifest and the log line), not a screen.

## Boss arena randomization (A) against roaming bosses (B) against ordinary replacement (C)

A decides which boss stands in a boss arena (`planSampledBosses`, the classic shuffle, the dragon plan for boss dragons). B is a boss body in an ordinary destination (`planRoamingBosses`). C is an ordinary body in an ordinary destination (`planSampledRegularEnemies`). The only coupling was the dragon plan: it reserved ordinary dragon-class slots (C and B destinations) whenever Enemy randomization was on. That is what point 2 removes for B. Nothing in A (the arena options) decides whether an ordinary slot may take a boss.

## Invariants (AUTOMATED VERIFIED, `enemyBossesOnly.test.mjs`, `enemyRoamingBosses.test.mjs`)

* Bosses only, 100%, Anywhere, Easy Asylum off: every eligible destination takes a boss, zero ordinary replacements, in every map.
* Easy Asylum on: only the Asylum differs (26 destinations stay as they are, counted `easyAsylum`), every other map is the same world as with Easy Asylum off.
* 50%: about half take part; every one that does gets a boss.
* Bosses or regular enemies at 0%: ordinary replacements only, identical to Regular enemies only; at 100%: the same destinations take a boss as with Bosses only.
* Regular enemies only: no boss. Size "Only where they fit": a destination no boss fits stays as it is (no placement at all), never an ordinary enemy.
* Stress: 24, 60, 150 and 260 destinations in one map give 20+, 50+, 100+ and 200+ roaming bosses, at most six archetypes, deterministic, final model count within the limit, generated entity ids and scaled NpcParam rows unique.
* Every unchanged selected destination has a counted reason; the numbers add up in every map.

## Real pipeline (REAL PIPELINE VERIFIED; safe copy of the game, real data tool, FogHost, owner-file audit of every placement; `placementMatrix.mjs bosses-only`)

| Case | Worlds | Bosses per world | Unchanged | Largest map | Result |
|---|---|---|---|---|---|
| Bosses only 100%, Easy Asylum off | 4 | 1,559 of 1,559 eligible | 0 | 189 bosses, 16 models, 200 events | 4/4, 6,624 placements audited, 0 failures, 0 non-deterministic reruns |
| Bosses only 100%, Easy Asylum on | 4 | 1,533 of 1,559 | 26 Easy Asylum | 189, 204 events | 4/4, 0 failures |
| Bosses or regular enemies, boss chance 100% | 4 | 1,494 of 1,559 | 65 model budget (they keep the regular enemy) | 189, 201 events | 4/4, 0 failures |
| Owner's configuration (boss arena shuffle on, stay dead, souls 50%) | 4 | 1,533 | 26 Easy Asylum | 189, 207 events | 4/4, 0 failures |
| Owner's configuration with the world randomizer | 6 | 1,533 | 26 Easy Asylum | 189, 201 events | 6/6, no `FOG_MERGE_CONFLICT`, 0 failures |
| Bosses only 100% with the world randomizer, stay dead off | 6 | 1,559 | 0 | 189, 207 events | 6/6, 0 failures |

In the engine-level reproduction of the owner's world (real catalog): before 170 roaming bosses (10 per map) and 60 ordinary-to-ordinary dragon-class exchanges; after 1,533 roaming bosses, 26 Easy Asylum destinations unchanged, 157 protected, 49 dragon-class placements left (the bridge Hellkite, the Anor Londo gargoyles, the hydras, dummy parts and neutral bodies).

## Event IDs, rewards and stay dead

* One wake-up event per roaming boss, inside the owner file's custom window; the window grows past the old 100 IDs as it always could. Unique IDs: `SmokeVerify audit` (called by the matrix for every world) found no duplicate and no missing owner in about 1,650 placements per world.
* A roaming boss inherits nothing of the encounter: no boss bar, no fog gate, no `awardItemLotId`, no arena flags; its souls are `trunc(authored fight souls * percent / 100)` on a scaled NpcParam row (checked for every placement in the tests), stay dead sets the row's `disableRespawn` bit (ON and OFF both tested at density).
* Not played: wake-up, navigation, killability and stay-dead behavior of this many bosses are **GAMEPLAY UNVERIFIED**.

## Compatibility

**Correction to the original 0.45.5 note:** the E1 settings were publicly exposed in prerelease 0.38.4. The old v1 behavior remains available for those Seed Codes; v2 is used for new runs. The compatibility paragraph below records the 0.45.5 fixture impact only; the current profile split is in `GENERATION_COMPATIBILITY.md`, exception 6.

Affected: sampling-v1 worlds with "Replace regular enemies with" = Bosses only or Bosses or regular enemies (the E1 modes, in the product since 2026-10-06, publicly exposed in prerelease 0.38.4), whose old plan either hit the cap of ten or contained a dragon-class slot that now takes a boss. Not affected: Regular enemies only, Nothing, the classic permutation, every item, Fog and DLC world, boss-arena-only worlds. Seed Code format and presets unchanged; v1 remains frozen for existing codes and new runs use `ds1tr-gen-v2` (see `GENERATION_COMPATIBILITY.md`, "Recorded exception 6"). Goldens: 4 of 51 fixtures change (3 "both" worlds by 1-2 dragon-class slots, one "bosses 40%" world by the removed cap); 47 are byte for byte unchanged.

## The QA configuration

`docs/FINAL_GAMEPLAY_QA.md`, "Every enemy is a boss": a Seed Code and the exact settings. GAMEPLAY UNVERIFIED until the owner has played it.
