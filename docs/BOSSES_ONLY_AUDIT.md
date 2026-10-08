# Audit: "Replace regular enemies with: Bosses only"

Date: 2026-10-08 (backend 0.45.5, desktop 0.35.1). Trigger: a real generated world (Enemy randomization on, Pick a replacement for every spot, Replacement chance 100%, **Bosses only**, Anywhere, Random + Easy Asylum, Boss randomization on with the classic arena shuffle, Roaming boss souls 50%, stay dead on) in which about 90% of the ordinary enemies were unchanged and some changed ones were another ordinary enemy.

## Root cause

Three independent things, found in this order of size.

1. **A fixed cap of 10 roaming bosses per map** (`ROAMING_BOSS_MAP_CAP = 10`, `sampling.js`, introduced in Part E1, commit `387f155`, 2026-10-06). The planner stopped placing bosses after ten in a map. A world has 1,702 ordinary destinations in 17 maps; 170 got a boss (the engine counted `roamingMapLimitReached: 1351`). That is the "1 in 10". The cap was an interim guardrail ("bounds the growth of the custom event window and keeps a map playable", `ENEMY_PLACEMENT_MODEL.md` 10.7 deviation 2, `EVENT_ALLOCATION.md`), not a game constraint, and the legacy tool has nothing like it.
2. **The dragon plan permuted ordinary dragon-class enemies among themselves**, whatever "Replace regular enemies with" said (`buildDragonPlan`, `generator.js`: `simpleRegularDragons` is exchanged whenever Enemy randomization is on). Titanite Demons, Drakes and the hostile Bounding Demons were exchanged for each other (60 `dragon-only-group-permutation` placements in the owner's world): **ordinary enemy replaced by another ordinary enemy under "Bosses only"**. Those slots were also reserved (`reservedSlotIds`), so the roaming pass could not give them a boss.
3. **Easy Asylum** keeps the Undead Asylum as it is: bosses are never among the easiest candidates (class <= 1), so all 26 eligible Asylum destinations stay vanilla. That one is intended (and explained below); it is not a cause of the rest.

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

Affected: sampling-v1 worlds with "Replace regular enemies with" = Bosses only or Bosses or regular enemies (the E1 modes, in the product since 2026-10-06, pre-public), whose old plan either hit the cap of ten or contained a dragon-class slot that now takes a boss. Not affected: Regular enemies only, Nothing, the classic permutation, every item, Fog and DLC world, boss-arena-only worlds. Seed Code format and presets unchanged; the generation profile `ds1tr-gen-v1` is not renamed (see `GENERATION_COMPATIBILITY.md`, "Recorded exception 4"). Goldens: 4 of 51 fixtures change (3 "both" worlds by 1-2 dragon-class slots, one "bosses 40%" world by the removed cap); 47 are byte for byte unchanged.

## The QA configuration

`docs/FINAL_GAMEPLAY_QA.md`, "Every enemy is a boss": a Seed Code and the exact settings. GAMEPLAY UNVERIFIED until the owner has played it.
