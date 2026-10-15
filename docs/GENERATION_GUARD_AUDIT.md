# Generation guard audit

## G-14 roaming-pool follow-up and owner-approved fit classes (2026-10-15)

G-14's Pinwheel Chaos coupling is corrected for roaming bosses. `bossSourceSlots` retains its original Chaos gate for arena boss shuffling, while `roamingBossCandidates` explicitly requests the main Pinwheel source even when Pinwheel Chaos is enabled. The arena encounter behavior remains unchanged. The focused boss test and a real-catalog Chaos-on generation confirm Pinwheel remains available to roaming placement.

The owner-approved minimum fit classes are now Artorias 0, Capra 0, Pinwheel 0, Taurus 2, and Gwyndolin 0. No other boss class or exclusion changed; Moonlight Butterfly remains class 3. The old production classification and restore instructions are preserved in `docs/placement-audits/boss-fit-classification-pre-nexus-2026-10-15.md` and `.json`. Real-catalog fit, deterministic matrix, and final encoded-output evidence is in `docs/placement-audits/boss-fit-validation-2026-10-15.json` and `docs/BOSSES_ONLY_AUDIT.md`.

Gwyndolin passes the generic roaming output path, including model loading, scaled NpcParam, cloned NpcThinkParam and a uniquely owned wake-up event. This is static/output validation only; movement and gameplay safety remain for owner in-game QA. No geometry solver, RNG salt, profile identifier, or Seed Code format changed. Because this is pre-Nexus, the existing local-only fit behavior was not retained as a compatibility profile.

## G-15..G-18 status update (2026-10-12)

The original audit below records the 2026-10-08 `ds1tr-gen-v1` state. Its four roaming-boss exclusions are retained for v1 Seed Codes, but are resolved for new `ds1tr-gen-v2` runs. The real-catalog check found safe existing handlers for all four categories: model-specific event sanitation for `eventModelLocked`, grounding/tangibility for ghosts, the dedicated activation/path handling for Darkroot butterfly-path slots, and passive-until-attacked behavior for passive groups. Parish's bonfire Titanite Demon also holds AI until attack. No generator, Seed Code format or Fog reconstruction behavior is changed by this audit note; see `docs/BOSSES_ONLY_AUDIT.md` for the output-level proof and remaining gameplay-only questions.

Date: 2026-10-08 (backend 0.45.5, desktop 0.35.1, contracts 0.32.0; generation profile `ds1tr-gen-v1`). Trigger: `ROAMING_BOSS_MAP_CAP = 10`, a conservative guardrail of Part E1 (commit `387f155`) that stayed in the product and made "Bosses only" at 100% change about one ordinary enemy in ten (`docs/BOSSES_ONLY_AUDIT.md`, removed in `5756097`). The question of this audit: **was that an isolated mistake, or are other temporary or conservative implementation guards still silently weakening the randomizer?**

At the time of the original audit, it changed no generation: nothing was removed, no fixture was re-recorded, no Seed Code or preset moved. The durable results are this registry, the tripwire test `apps/backend/src/adapters/generationGuards.test.mjs` and the measurement script `apps/backend/scripts/guardAuditProbe.mjs`.

## Verdict

**Original 2026-10-08 verdict; later status follows.** No other fixed count of bosses or events was found, and no event ceiling exists (189 bosses and 207 events in one map were generated, unique, merged with Fog). At that time the same kind of problem appeared in four other forms. Since then G-15..G-18 were corrected for v2 with profile-gated safe handlers; the model budget G-05/G-09, Easy Asylum G-12b and Pinwheel Chaos G-14 remain separate follow-up questions.

| # | Finding | Same class as the cap? | Size |
|---|---|---|---|
| G-04 | `HOSTILE_NPC_MAP_CAP = 12`: the direct sibling. Introduced in Part F1 (`fd979eb`) for the same reason (an event-count fear) that 207 unique events per map refutes. Disclosed in the help ("at most twelve"), not counted in the diagnostics. | yes | with Type replacement 19.5 hostile NPCs per world instead of 49.2 |
| G-03 (fixed in 0.45.6) | The unit planners (dragon plan, Mass of Souls, hydras) ignored **Replacement chance** and **Replace regular enemies with: Nothing**. A deferred part of the design ("unit participation", Part D2), not a number. The help says "Regular enemy spots are not replaced". | a silent label violation | 127 placement records (about 80 visible enemies) per world at chance 0% |
| G-05 | The model budget counts every model a map holds (30); the legacy counts only NEW models (30, Blighttown 50). Documented as an open question since Part 0. | a conservative definition, never verified in the game | about 21 new models per map instead of 30, Blighttown 25 instead of 50 |
| G-12b, G-14 | Easy Asylum + Bosses only differs from the legacy; Pinwheel Chaos removes Pinwheel from the roaming pool. | suspicious siblings from the same commit | see the rows |
| G-15..G-18 | Four former exclusions hid 157 ordinary destinations under v1. | resolved for v2; v1 behavior frozen | safe handlers and real output audit in the status update above |

Everything else inspected is either a real constraint, a legacy rule, an explicit refusal, a deliberate and documented safe subset, or a bounded search whose exhaustion is an explicit error.

### Counts

55 registry rows (G-01 to G-54, with G-12 split in two). 10 of them are a finding (not a requirement, a rule or a subset).

| Classification | Rows |
|---|---|
| CORRECTNESS REQUIREMENT | 15 |
| INTENTIONAL PRODUCT RULE | 9 |
| SUSPICIOUS — NEEDS GAMEPLAY VERIFICATION | 4 (G-05, G-09, G-12b, G-14) |
| RESOLVED FOR `ds1tr-gen-v2`; RETAINED FOR V1 COMPATIBILITY | 4 (G-15, G-16, G-17, G-18) |
| LEGACY REQUIREMENT | 7 |
| INTENTIONAL SAFE-SUBSET DEVIATION | 7 |
| REAL ENGINE CONSTRAINT | 5 |
| TEMPORARY DEVELOPMENT GUARD — ALREADY REMOVED | 3 (G-01, G-02, G-03) |
| TEMPORARY DEVELOPMENT GUARD — STILL ACTIVE | 0 (G-03 was the one; fixed in 0.45.6) |
| OBSOLETE CONSERVATISM — CANDIDATE FOR REMOVAL | 1 (G-04) |
| UNKNOWN | 0 |

## How this was done

* **Code:** every file of the generation path was read or scanned: `engines/combined/vendor/src/core/*.js` (generator, sampling, placement, keys, difficulty, laundromat, config), `engines/combined/vendor/tools/DsrDataTool/Program.cs` (event windows, owners, model-specific events), `tools/fog-host/src` (option mapping, boss overlay, merge, error mapping), `apps/backend/src` (configuration validation, retry bounds, pipeline, DLC exclusion, Fog item rules, bridge). Every top-level numeric constant and every `for`/`while` with a literal bound is enumerated by the tripwire test, so the list cannot go stale unnoticed. The scan covers `engines/combined/vendor/src/core/*.js` and the three named backend bounds only; the data tool (`Program.cs`), FogHost and the rest of the backend were covered by reading, not by the test.
* **History:** `git log -S` for each guard (the "Introduced" column), the commit messages, and `docs/` (DECISIONS, ENEMY_PLACEMENT_MODEL, EVENT_ALLOCATION, ITEM_PROGRESSION_MODEL, RACE_MODE, FEATURE_PARITY, DLC_AUDIT, FOG_ITEM_TRACKING, FINAL_GAMEPLAY_QA).
* **Legacy:** the pinned Enemy Randomizer `83f008d` (`randomizer_rng.py`, `valid_new.txt`) read for behavior only (nothing copied); the item side is `f7cabbd` as recorded in `docs/RACE_MODE.md`. **LEGACY CODE VERIFIED** marks a statement checked against that source in this audit.
* **Measurements:** `guardAuditProbe.mjs` on the real extracted catalog (the clean game, 1,702 ordinary destinations in 18 maps). **AUTOMATED VERIFIED** (engine level). Nothing here was played: every gameplay statement is **GAMEPLAY UNVERIFIED** unless a row says otherwise.
* **Not audited, by instruction:** pristine snapshots, Apply / Restore, live Steam protection, path validation, hashes, downloaded-file integrity, signatures, Support Bundle redaction, configuration schema validation, public/internal build gating, data-loss prevention.

Evidence labels: GIT HISTORY VERIFIED, LEGACY CODE VERIFIED, STATICALLY VERIFIED, AUTOMATED VERIFIED, REAL PIPELINE VERIFIED, GAMEPLAY UNVERIFIED, SUSPICIOUS, UNKNOWN.

Rule for upstream FogMod behavior: what FogMod's engine does and DS1TR does not control (its graph, its item reader, its solver, what two tools writing the same event can merge) is a REAL ENGINE CONSTRAINT; a rule of FogMod's or of a legacy tool's interface or option set that DS1TR copied is a LEGACY REQUIREMENT.

Classifications (exactly one per row): REAL ENGINE CONSTRAINT; LEGACY REQUIREMENT; CORRECTNESS REQUIREMENT; INTENTIONAL PRODUCT RULE; INTENTIONAL SAFE-SUBSET DEVIATION; TEMPORARY DEVELOPMENT GUARD — STILL ACTIVE; TEMPORARY DEVELOPMENT GUARD — ALREADY REMOVED; OBSOLETE CONSERVATISM — CANDIDATE FOR REMOVAL; SUSPICIOUS — NEEDS GAMEPLAY VERIFICATION; UNKNOWN.

## 1. The guard registry

`Location` names the engine entry as the tripwire test scans it ("file: name"; a bound that occurs several times in a file says how many). Impact numbers are per world on the real catalog unless stated. "Action": **none**, **follow-up F-n** (section 11), or the commit that already removed it.

### Enemies

| Guard / restriction | System | Location | Introduced | Original reason | Current reason | Gameplay impact | Classification | Action |
|---|---|---|---|---|---|---|---|---|
| G-01 Per-map cap of 10 roaming bosses (`ROAMING_BOSS_MAP_CAP`) | roaming bosses | `sampling.js` (removed) | `387f155` 2026-10-06 (E1) | bound the custom event window and keep a map playable | none: the window grows, 189 bosses and 207 unique events in a map are generated and merged with Fog | was 170 bosses per world at "Bosses only" 100%, now 1,559 | TEMPORARY DEVELOPMENT GUARD — ALREADY REMOVED | removed `5756097` |
| G-02 The dragon plan reserved the ordinary dragon-class slots (Titanite Demons, Drakes, Bounding Demons) away from the roaming pass | roaming bosses, dragon plan | `generator.js` `buildDragonPlan` (`reservedSlotIds`, now `overridableSlots`) | upstream design `a969d2a`; coupling found 0.45.5 | whole units swap with whole units | the roaming pass may take dragon-class slots without linked parts; bridge Hellkite, Anor Londo gargoyles, hydras stay with the plan | was 60 ordinary-to-ordinary exchanges under "Bosses only" | TEMPORARY DEVELOPMENT GUARD — ALREADY REMOVED | removed `5756097` (the rest of the coupling is G-03) |
| G-03 The unit planners ignore Replacement chance and "Nothing": dragon-class units, Mass of Souls wisps, hydras and their parts are exchanged whenever Enemy randomization is on | dragon plan, Mass of Souls | `generator.js` `buildDragonPlan`, `buildMassOfSoulsPlan`, `sampleExtractedEnemies` | upstream `a969d2a`; "unit participation by chance" designed (model doc 5, decision 6: "one chance draw per unit") and deferred as Part D2 (DECISIONS 2026-10-06, model doc 10.6 "Open (a)") | whole units cannot be partly replaced; participation was left for later | still deferred; the only mention is "Linked units stay classic" | AUTOMATED VERIFIED: 127 placement records per world at chance 0% and at "Nothing", of which about 80 are visible enemies that change (59 dragon-class exchanges, 14 Mass of Souls wisps, 3 hydras, 1 Parish bonfire Titanite Demon, 1 bridge Hellkite, 2 linked parts) and 47 are disabled or synthetic part records (hydra heads, unmatched parts). The help says, for "Replace regular enemies with": "Regular enemy spots are not replaced" (choice Nothing) and "Regular enemy spots, including large regular enemies such as the Titanite Demons and the Drakes" (what it affects); for "Replacement chance": "How many regular enemies stay as in the original game". Under "Bosses only" the units that no boss takes are still ordinary-to-ordinary | TEMPORARY DEVELOPMENT GUARD — ALREADY REMOVED | fixed in backend 0.45.6 (2026-10-09), `docs/ENEMY_PLACEMENT_MODEL.md` 10.11, `GENERATION_COMPATIBILITY.md` "Recorded exception 5": a unit takes part only by the mode and by the first draw of its root slot; 12 of 51 fixtures re-recorded. Was: follow-up F-01 |
| G-04 At most 12 hostile NPCs per map (`HOSTILE_NPC_MAP_CAP`); a Type replacement group counts every slot | hostile NPCs | `sampling.js: HOSTILE_NPC_MAP_CAP` (used by `planMapAttempt`, `planMapByType`) | `fd979eb` 2026-10-07 (F1) | "every one has a wake-up event, so this bounds the events of a map" (an uncapped run put up to 117 in one map) | the reason is gone (EVENT_ALLOCATION: the window grows; 207 events); the legacy has no such cap | AUTOMATED VERIFIED: mean 65.3 instead of 69.7 per world by default, 46.8 instead of 50.2 (curve strict), 57.7 instead of 69.2 (anywhere), **19.5 instead of 49.2 with Type replacement enabled**; largest map 12 instead of 25 to 33. Disclosed in the help ("at most twelve per map"); not counted in the diagnostics; hostile NPCs have never been played | OBSOLETE CONSERVATISM — CANDIDATE FOR REMOVAL | follow-up F-03 (not fixed: the feature is unplayed, up to 5 fixtures and the help text would change) |
| G-05 The model budget counts every distinct model a map holds after the run (fixed, kept and placed): 30 | all sampling-v1 placement | `placement.js: MODEL_BUDGET_CAP` (`ModelBudget`, `ModelLedger`) | `1253726` 2026-10-05 (Part 0) | "Safety first" (decision D-2): "Whether Blighttown may exceed 30 is open"; "cannot be answered without running the game" (model doc 13.2) | unchanged; the legacy counts only NEW types (`MAX_UNIQUE` 30, Blighttown 50 on the Remaster) and keeps the originals in the map (LEGACY CODE VERIFIED: `randomizer_rng.py` lines 203, 263-264, 1363-1367, 1595-1596; its author set 60 for the Remaster and reverted to 30); vanilla Undead Parish holds 28 and 30-33 after the dragon and boss plans | AUTOMATED VERIFIED: 14 to 16 of 18 maps end exactly at the cap; mean room for new models 21.0 per map (legacy 30), Blighttown 25 (legacy 50), at 50% replacement 12.7 (Undead Parish 1, Demon Ruins 2); 1,300 to 4,100 candidate draws per world rejected for the budget | SUSPICIOUS — NEEDS GAMEPLAY VERIFICATION | follow-up F-02 |
| G-06 Effective cap `max(30, baseline)` (the "non-worsening" rule) | sampling, roaming | `placement.js`, `sampling.js` (`resolveModelCap`, `planRoamingBosses`) | Part A (model doc 10.2) | the Undead Parish already holds 30 to 33 models after the dragon and boss plans | unchanged | a map is never made worse than it already is | CORRECTNESS REQUIREMENT | none |
| G-07 30 distinct models per map for the classic permutation | permutation-v1 | `generator.js: MAX_ENEMY_MODELS_PER_MAP` (`limitUniqueEnemyModelsByMap`) | upstream `a969d2a` | upstream limiter | frozen: the permutation is the compatibility path of old Seed Codes | none new | INTENTIONAL PRODUCT RULE | none |
| G-08 The other budget consumers count DISTINCT models, a repeated model costs nothing | roaming pass, E2 bodies, hostile NPCs | `planRoamingBosses`, `chooseBody`, `ModelBudget.evaluate` (`alreadyPresent`) | E1 / E2 | the lesson of G-01: instances are not models | AUTOMATED VERIFIED: 189 bosses use at most 16 models; `enemyPlacementInfrastructure.test.mjs` pins reuse; the E2 limit uses `max(30, vanilla size)` where the roaming pass uses the size before the pass (no failure seen: 3 of 3 bodies placed in 6 seeds) | none | CORRECTNESS REQUIREMENT | none |
| G-09 Difficulty lapses and class fallbacks: when the budget removes every candidate of the wanted class the class widens, nearest first | curve modes | `sampling.js` `sampleCandidate` (`difficultyLapses`, `classFallbacks`) | Part B `6e0aeb0` (deviation D-1) | a literal legacy lapse gave 30 to 31% same-class placements | counted, never silent in the diagnostics; a consequence of G-05 | AUTOMATED VERIFIED: 190 lapses of 1,702 slots at "curve, strict, anywhere"; with Bosses only and a very loose curve 187 destinations stay because of the budget (the help written for 0.35.1 says this "rarely matters": true for Random, not for the curve modes) | SUSPICIOUS — NEEDS GAMEPLAY VERIFICATION | follow-up F-02, F-08 |
| G-10 Three map attempts (the first and two map-local retries) | sampling-v1 | `sampling.js: MAP_ATTEMPTS` | `6e0aeb0` 2026-10-05 | bounded, deterministic retry | exhaustion is the typed `ENEMY_PLACEMENT_INFEASIBLE`, retryable at the combined level, then `COMBINED_RETRIES_EXHAUSTED` | no retry was needed in any sweep | CORRECTNESS REQUIREMENT | none |
| G-11 Exact cover search bounded at 20,000 nodes, then the greedy cover | custom pool | `sampling.js: WITNESS_NODE_LIMIT` | `e184131` 2026-10-05 (C2) | keep the search bounded and deterministic | the verdict "infeasible" is claimed only for a proven minimal cover | only with a restricted custom pool | CORRECTNESS REQUIREMENT | none |
| G-12a Easy Asylum applies to every regular slot of the Asylum (29: the 26 destinations and the 3 passive Hollows) with candidate classes up to 1 | difficulty | `sampling.js: EASY_ASYLUM_MAX_CLASS`, `EASY_ASYLUM_MAP` | `6e0aeb0` | authored independently of the legacy (model doc 12) | unchanged; the legacy default is "Random + Easy Asylum" | LEGACY CODE VERIFIED: the legacy applies it to twelve hard-coded targets (`EASYASYLUM_TARGETS`), the other Asylum slots follow the mode | INTENTIONAL PRODUCT RULE | none |
| G-12b Easy Asylum + Bosses only leaves the Asylum vanilla (no boss has class 1 or less) | roaming bosses, difficulty | `sampling.js` `planRoamingBosses` (`easyAsylum`) | `387f155` | bosses are never "easy" | LEGACY CODE VERIFIED (`GetBossEnemy`, `randomizer_rng.py` lines 936-972): with Bosses only the twelve targets receive `HARDCODED_ASYLUM_BOSSES = [8, 9, 118]` = Capra Demon, Taurus Demon, Bell Gargoyles (index base verified: no row of `valid_new.txt` is skipped), and every other Asylum slot goes through the ordinary boss draw (`validSizeBosses`, no class filter in mode 3): **in the legacy, "Random + Easy Asylum" + Bosses only fills the Asylum with bosses; in DS1TR the Asylum stays vanilla**. The E1 legacy table of the model doc never mentions this list | the owner's own configuration ("Random + Easy Asylum" + Bosses only) keeps 26 destinations vanilla (counted `easyAsylum`; the 0.35.1 help says it; `docs/BOSSES_ONLY_AUDIT.md` called it intended: this row corrects that). Also the legacy marks Asylum Demon (`c2232`) and Moonlight Butterfly (`c3230`) as ignored sources ("AI does not activate properly", "stuck in the ground") while DS1TR lets both roam; the QA seed puts both in the Asylum | SUSPICIOUS — NEEDS GAMEPLAY VERIFICATION | follow-up F-04 (an authored easy-boss rule is a design decision) |
| G-13 Roaming boss pool: 17 archetypes (a souls entry in `bossKillSouls`, not in `roamingExcludedModels`) | roaming bosses | `generator.js` `roamingBossCandidates`, `roamingExcludedModels`, `bossKillSouls` | `387f155` | fights that need scripted terrain, immobile bodies, linked parts, neutral team | unchanged | LEGACY CODE VERIFIED: the legacy pool has 24 bosses on the Remaster (23 not ignored plus Artorias, marked Remaster-only); DS1TR leaves out Priscilla, Sanctuary Guardian, Kalameet, Centipede Demon, Gaping Dragon, Seath, Bell Gargoyles, Lightning Gargoyles, Four Kings and Gwyn, and adds Asylum Demon and Moonlight Butterfly, which the legacy ignores. Documented as "a conscious restriction, not a legacy rule" | INTENTIONAL SAFE-SUBSET DEVIATION | follow-up F-07 |
| G-14 Pinwheel Chaos removes the main Pinwheel from the roaming pool | roaming bosses | `generator.js` `bossSourceSlots` (feeds `roamingBossCandidates`) | `9d1517e` 2026-10-05 (Batch 2, the exclusion), `387f155` (the coupling with the roaming pool) | with Chaos the main Pinwheel keeps its place as a boss | an unintended coupling: the roaming candidates drop from 17 to 16 (the legacy source list does not depend on Pinwheel Chaos, which only changes how the Pinwheel destination slots of `m13_00` are treated, lines 1477-1512) | AUTOMATED VERIFIED: with "Bosses only" and "Only where they fit" the bosses of a world drop from 510 to 168 (Pinwheel is the only boss of size class 1); no effect with "Anywhere" | SUSPICIOUS — NEEDS GAMEPLAY VERIFICATION | follow-up F-06 |
| G-15 Passive slots never take a boss (the Asylum Hollows, New Londo, the Kiln knight, the Parish bonfire Titanite Demon) | roaming bosses | `generator.js` `roamingProtectedReasons` (`passiveSlot`) | `387f155` | "special handling that a boss would break" (the only reason recorded in the commit and the docs) | v1 keeps 20 protected; v2 routes them through passive behavior; Parish explicitly holds AI until attacked | 20 v1 destinations per world; v2 has no protection counter for them | RESOLVED FOR V2; v1 retained | profile-gated; gameplay behavior still unverified |
| G-16 Slots whose events lock their model never take a boss (`eventModelLocked`) | roaming bosses | `roamingProtectedReasons` (`eventModelLocked`); derived by the data tool from model-specific event instructions | `387f155` (flag since `9a33c16`) | as G-15 | ordinary replacements already use data-tool model-specific event sanitation; v2 reuses that sanitation for roaming bosses | 49 v1 destinations per world; none excluded by this reason in v2 | RESOLVED FOR V2; v1 retained | focused regression and real output audit; gameplay still unverified |
| G-17 Tangible ghost slots never take a boss (Male and Female Ghost models) | roaming bosses | `roamingProtectedReasons` (`tangibleGhost`) | `387f155` | as G-15 | v2 reuses the ordinary replacement grounding/tangibility decoration | 34 v1 destinations per world; none excluded by this reason in v2 | RESOLVED FOR V2; v1 retained | focused regression and real output audit; gameplay still unverified |
| G-18 The Darkroot butterfly path (the Demonic Foliage slots of the Garden and the Basin) never takes a boss | roaming bosses | `roamingProtectedReasons` (`darkrootButterflyPath`) | `387f155` | as G-15 | v2 reuses the ordinary replacement butterfly-path activation handling | 54 v1 destinations per world; none excluded by this reason in v2 | RESOLVED FOR V2; v1 retained | focused regression and real output audit; gameplay still unverified |
| G-19 The roaming pass leaves linked parts, dummy parts, neutral-team bodies and the dragon bodies of units alone (bridge Hellkite, Anor Londo gargoyles, hydras) | roaming bosses | `generator.js` `roamingDragonSlots`, `buildDragonPlan` | `5756097` | a boss in place of one body of a unit leaves the other bodies inconsistent; a dummy is not spawned; a neutral dragon is hostile only when attacked | unchanged | 49 dragon-class placements per world stay with the plan | CORRECTNESS REQUIREMENT | none |
| G-20 The regular pool is derived from the regular slots of the clean game: no boss-family model, no dragon body, no linked part, no helper model, no tangible dummy, Mimic and respawning mosquito by option | all regular placement | `generator.js` `regularEnemyContext` | `a969d2a`, parity batches `f3aabcf` | models that need their own planner | the legacy also ignores Titanite Demon, Undead Dragon, Bounding Demon and Hydra as sources (LEGACY CODE VERIFIED, `IsIgnored` 1) | 86 models | LEGACY REQUIREMENT | none |
| G-21 Placement fit uses derived size classes and is never relaxed; the Moonlight Butterfly has an authored class | fit | `sampling.js` `SIZE_EDGES`, `roamingSizeClassOverrides` | `1faecce` 2026-10-05 | the label "Only where they fit" | there is deliberately no "fit relaxed" rung | AUTOMATED VERIFIED: with Bosses only and "Only where they fit" 510 of 1,559 destinations take a boss and 1,049 stay (counted `fit`) | INTENTIONAL PRODUCT RULE | none |
| G-22 The difficulty curve: an empty target class falls back to the destination's class; with Bosses only a destination whose class has no boss stays | difficulty | `sampling.js` `sampleCandidate`, `planRoamingBosses` | `6e0aeb0`, `387f155` | the label "comparable difficulty" | the early-area protection is the curve itself, and it is counted | AUTOMATED VERIFIED: Bosses only, 100%, curve strict: 372 of 1,559 destinations stay (counted `difficulty`); loose 323; the Random modes protect nothing | INTENTIONAL PRODUCT RULE | none |
| G-23 Prevent same enemy: a candidate equal to the slot's own model is rejected; a slot with no other candidate keeps its enemy | prevent same | `sampling.js`, `generator.js` `separateSameModelSources` (classic) | Batch 1 `f3aabcf` | legacy `disallowSameReplacement` | unchanged | a kept slot is unchanged, which satisfies the label | LEGACY REQUIREMENT | none |
| G-24 A regular enemy as the body of a boss encounter exists for 3 of 24 boss encounters (Asylum Demon, Taurus Demon, Capra Demon) | E2 | `generator.js: encounterBodySupportedSlots` | `5b34a38` 2026-10-06 | audited single-entity encounters in maps the legacy needed no event surgery for | Demon Firesage and Iron Golem are classified "SAFE_WITH_ADAPTER (candidate)" and kept for lack of a game test | AUTOMATED VERIFIED: 3 bodies and 21 `bossSlotsKeptUnsupported` in every world; the help says "three early fights"; the legacy puts normal enemies into every boss arena | INTENTIONAL SAFE-SUBSET DEVIATION | follow-up F-07 |
| G-25 E2 body candidates: 11 plain ground melee models | E2 | `generator.js: encounterBodyModels` | `5b34a38` | passive, ambush, ghost, mimic, stationary, flier and scripted models are left out | unchanged | variety of the three fights | INTENTIONAL SAFE-SUBSET DEVIATION | none |
| G-26 Hostile NPC sources: 3 of the legacy's 4 models (not Gough, not the Lower Burg Merchant); refused with a custom pool or without normal enemies | hostile NPCs | `generator.js` `classifyNpcSlot`, `hostileNpcSources`; `configValidation.ts` | `fd979eb` | Gough: think row 0 and 20,000 souls, "unproven"; Lower Burg Merchant: its logic script is not copied by the data tool; the custom pool lists enemy models | the Merchant is a data tool gap, Gough was never tested | 2 of 5 NPC models | INTENTIONAL SAFE-SUBSET DEVIATION | follow-up F-03 |
| G-27 NPC replacement (legacy E3) is not built | NPC | FEATURE_PARITY E3 | Part F decision | it would remove shops, blacksmiths and a Fire Keeper | unchanged | none | INTENTIONAL PRODUCT RULE | none |
| G-28 Bounded searches of the classic permutation: `generator.js: attempt < 128` (safe boss permutation), `generator.js: attempt < 256` (Titanite Demons out of native Titanite slots), `generator.js: pass < 8` (Prevent same repair), `generator.js: pass < 200` (model limiter) | permutation-v1 | `generator.js` | upstream `a969d2a`, Batch 1 `f3aabcf` | bounded and deterministic | exhaustion throws (`Could not create a safe boss permutation`, `PREVENT_SAME_ENEMY_UNRESOLVED`, `did not converge`); frozen with the profile | none | CORRECTNESS REQUIREMENT | none |
| G-29 Type replacement injectivity is relaxed as a last rung (`shared-fit`) | Type replacement | `sampling.js` `sharedFallback` | `29560d1` | the "two kinds never get the same replacement" rule is a variety preference of the legacy | counted | rare | CORRECTNESS REQUIREMENT | none |
| G-30 Gwyn spawn nerf default Medium (60% rejection) | boss pool | `generator.js` `gwynNerfRejection` | Batch 2 | legacy default | an explicit option | Gwyn is withheld from 60% of boss-slot draws | INTENTIONAL PRODUCT RULE | none |

### Items

| Guard / restriction | System | Location | Introduced | Original reason | Current reason | Gameplay impact | Classification | Action |
|---|---|---|---|---|---|---|---|---|
| G-31 Permanently fixed lots (Dungeon Cell Key, Asylum F2 East Key, the Archive keys, goods 2010 and 2015) never move | items | `generator.js` `randomizableWorldItemLots`, `keySourceEntries` | upstream `a969d2a` | the Archive capture and the Asylum escape | unchanged | 5 lots | CORRECTNESS REQUIREMENT | none |
| G-32 Protected lots and protected shop items stay unless "Protected items" is on, and the backend refuses that option ("blocked until progression safety is proven") | items | `configValidation.ts`; `randomizableProtectedShopItems`, `protectedGiftRows` (upstream `a969d2a`) | `0bc4810` 2026-09-27 | progression safety was not proven | key placement (Part H) is the safe form of the same idea and cannot be combined; FEATURE_PARITY records it as INTENTIONALLY_UNSUPPORTED; no control is drawn | none (an explicit refusal; the message wording is interim) | INTENTIONAL PRODUCT RULE | follow-up F-08 (wording only) |
| G-33 Escape Death (unfinished miracle) is not placed | items | `generator.js` `isUnsupportedCutItem` | upstream | no usable name | unchanged | 1 item | CORRECTNESS REQUIREMENT | none |
| G-34 Shop purchase limits (99 bulk, 1 for equipment and spells, 10 otherwise); bulk consumables only among shops | shops | `generator.js` `shopPurchaseLimit`, `bulkShopGoodNames` | upstream | shop semantics | unchanged | none | LEGACY REQUIREMENT | none |
| G-35 DLC-restricted items (progression, Titanite, embers, Havel's Ring, Fire Keeper Soul, Rusted Iron Ring) never go to the DLC; with the DLC off the DLC content takes no part | items, DLC | `generator.js` `isDlcRestrictedItem`; `dlcExclusion.ts` | upstream; `1750fb7` 2026-10-08 | the DLC is optional content | unchanged | AUTOMATED VERIFIED (`dlcSweep`) | CORRECTNESS REQUIREMENT | none |
| G-36 `shuffledWithoutFixedPoints` and `shuffledWithoutDlcRestrictedItems`: `generator.js: attempt < 32 (x2)`; after 32 tries the first returns a deterministic rotation | items | `generator.js` | upstream `a969d2a` | a derangement without an unbounded loop | the rotation is a silent weaker fallback, reached with probability about 4e-7 per call; the second throws | none in practice | CORRECTNESS REQUIREMENT | none |
| G-37 Item difficulty: ordered preference of tiers (falls back to the whole list), `difficulty.js: attempt < 32`, exhaustion is `DIFFICULTY_POLICY_UNSATISFIED` (retryable); the tiers are DS1TR-derived terciles; Race Mode + needs Fair | items | `difficulty.js` | `5e20366` | the documented policy of the legacy | explicit failure | none | LEGACY REQUIREMENT | none |
| G-38 Key placement: only a single-entry, model-covered, non-DLC, non-armor-set pickup can hold a key; never a shop, gift or drop; `keys.js: MAX_ATTEMPTS` (32) then `KEY_PLACEMENT_UNSATISFIABLE` (retryable) | keys | `generator.js` `planKeys`, `keys.js` | `2e1d051` 2026-10-07 | an assumed fill needs a pickup awarded for certain | explicit failure; 1 attempt in every measured world | the legacy allows keys in some shop and drop rows | CORRECTNESS REQUIREMENT | none |
| G-39 Race destinations are derived from the game data: 69 against the legacy's 103 (no Black Knight drops, no Ingward, Andre or Merchant rows, no DLC boss drops) | Race | `planKeys` (`isRaceDestination`) | `5e20366` | an enemy drop belongs to whatever body stands in the slot | unchanged | 16 legacy locations are not destinations | INTENTIONAL SAFE-SUBSET DEVIATION | none |
| G-40 Race Mode +: 11 skip edges of FogMod's graph; the Sen's Gate skip, the Master Key routes, killing Gwynevere and the Duke skip are not assumed; the Purple Coward's Crystal is not a movable key; with Fog on it is made as Race Mode (disclosed in the help and the manifest) | Race+ | `keys.js`, `generationPipeline.ts` (`engineKeyMode`) | `5e20366`, `0e4dab2` | FogMod's graph does not model four skips; with Fog the skips mean nothing | every Race+ world is valid for the skips it uses | 31 of 300 Race+ worlds need a skip | INTENTIONAL SAFE-SUBSET DEVIATION | none |
| G-41 Fog item tracking: the first copy of a required key item only in an annotated lot, extra copies only in shops | items with Fog | `fogItemRules.ts`, `generator.js` `repairFogTrackedItems` | `6f62505` 2026-10-06 | FogMod's `ItemReader.FindItems` stops with "Couldn't find ... in item lots" | measured: 85% of seeds broke Fog without it | 28 key items | REAL ENGINE CONSTRAINT | none |
| G-42 Replaced souls draw from goods that occur in at least two lots, with or without Fog | soul items | `generator.js` `replaceLesserSouls` | Batch 2 | FogMod rejects a second copy of a unique item | applied always so that Fog does not change the item world | a smaller replacement pool | INTENTIONAL PRODUCT RULE | none |
| G-43 Soul items Transposed: 75% as the legacy, DLC boss souls and the Sanctuary Guardian soul have no recipe | soul items | `generator.js` `transposedBossSouls` | Part H | legacy rule; data-driven recipes | unchanged | none | LEGACY REQUIREMENT | none |
| G-44 Laundromat Mixup draws only pieces some NPC already wears; hollowed variants are untouched | Laundromat | `laundromat.js` | `5e20366` | every written id is already loaded on an NPC model | a safe subset of the legacy | 60 characters | INTENTIONAL SAFE-SUBSET DEVIATION | none |
| G-45 `normalizeConfig` replaces an unknown enum value with the default (soul items, starting items, Gwyn nerf, Second Gargoyle, key placement, item difficulty, scaling) | engine configuration | `config.js` | upstream | defensive | the backend validates every enum before the engine sees it; unreachable from the product | none | CORRECTNESS REQUIREMENT | none |

### Fog and world

| Guard / restriction | System | Location | Introduced | Original reason | Current reason | Gameplay impact | Classification | Action |
|---|---|---|---|---|---|---|---|---|
| G-46 A random start needs boss or traversable fog gates | Fog | `configValidation.ts` | `0bc4810` | the FogMod UI validates the same | unchanged | an explicit refusal | LEGACY REQUIREMENT | none |
| G-47 Excluding the DLC needs Fog off (the FogMod graph always contains the DLC areas and warps) | Fog, DLC | `configValidation.ts` | `1750fb7` 2026-10-08 | `docs/DLC_AUDIT.md` | unchanged | an explicit refusal | REAL ENGINE CONSTRAINT | none |
| G-48 Retry bounds `maxCombinedAttempts` 3 and `maxFogAttempts` 5, frozen in the profile; only `FOG_SEED_UNSOLVABLE` is retried; FogMod's own `while (tries++ < 100)` throws "Couldn't solve seed", which FogHost maps to `FOG_SEED_UNSOLVABLE` | retries | `generationProfiles.ts`, `fogCompatibility.ts` (`decideFogRetry`), `GraphConnector.cs` | `9e575bf` 2026-09-28 | bounded, deterministic, recorded | exhaustion is `FOG_RETRIES_EXHAUSTED`, `FOG_KEY_CONFLICT` or `COMBINED_RETRIES_EXHAUSTED`, never a weaker world | STATICALLY VERIFIED | CORRECTNESS REQUIREMENT | none |
| G-49 The Fog boss overlay accepts 15 whitelisted boss events and a fixed set of removable and insertable instructions; anything else is `FOG_MERGE_CONFLICT` | Fog, bosses | `tools/fog-host/src/BossOverlay.cs` | FogHost 0.5.0 | two tools edit the same events | measured: the union of the overlays of every Part D/E/F world stayed inside the whitelist, no new merge | a boss feature that needs another event edit cannot ship with Fog without extending the overlay | REAL ENGINE CONSTRAINT | none |
| G-50 Thirteen of FogMod's flags are controls; the debug and developer flags are not exposed | Fog | `tools/fog-host/src/HostRequest.cs` (`OptionFlags`) | FogHost | `FEATURE_PARITY` section 3 | no DS1TR-authored entrance exclusion list, disconnected-gate rule, PvP or Lordvessel rule exists: they are FogMod's | none | LEGACY REQUIREMENT | none |

### Infrastructure

| Guard / restriction | System | Location | Introduced | Original reason | Current reason | Gameplay impact | Classification | Action |
|---|---|---|---|---|---|---|---|---|
| G-51 One custom event window per owner file, sized to the events needed; `consumed <= reserved` or the tool throws; the window moves past used and reserved IDs | events | `Program.cs` (`FindFreeCustomEventRange`) | `9a33c16` (vendored tool), reserved IDs `3637d0b` 2026-10-06 | unique event IDs | no ceiling found | REAL PIPELINE VERIFIED: up to 207 events in a map, all unique, merged with Fog | REAL ENGINE CONSTRAINT | none |
| G-52 The Darkroot Basin has no event file; `EventOwnerMap` sends its events to the Garden's, `EnsureEventOwnersExist` fails loudly for any other map | events | `Program.cs` | `c9be54b` 2026-10-07 | a game data fact (the Basin has no `m12_00_00_01.emevd`) | the classic permutation keeps its frozen quirk | none | REAL ENGINE CONSTRAINT | none |
| G-53 Entity ids for slots without one start at 16,099,000; scaled NpcParam rows at 9,150,000 (bosses 9,250,000) | placement ids | `generator.js` | Part E | outside the vanilla ranges | the owner-file audit finds no duplicate in about 1,650 placements per world | none | CORRECTNESS REQUIREMENT | none |
| G-54 At most 512 custom-pool entries | configuration | `enemyPlacement.ts: MAX_CUSTOM_POOL_ENTRIES` | `dde0732` 2026-10-05 | a validation bound | the regular pool has 86 models | none | INTENTIONAL PRODUCT RULE | none |

Not limits (listed by the tripwire test with the reason): `HOLLOW_FROM` (a data id), `DIFFICULTY_CLASS_COUNT` (the scale), `AREA_WEIGHT` and `HP_WEIGHT` (the class formula).

## 2. Hard-coded counts (category 1)

| What | N | Counts | Real engine limit? | After G-01 |
|---|---|---|---|---|
| `sampling.js: HOSTILE_NPC_MAP_CAP` | 12 | NPC bodies (instances) | no (events refuted) | the one remaining per-map instance cap: G-04 |
| `placement.js: MODEL_BUDGET_CAP` | 30 | distinct models (all of them) | unknown (G-05) | counts models, not instances |
| `generator.js: MAX_ENEMY_MODELS_PER_MAP` | 30 | distinct models (classic) | as above | frozen |
| `sampling.js: MAP_ATTEMPTS` | 3 | retries per map | no | explicit failure |
| `keys.js: MAX_ATTEMPTS` | 32 | key fill trials | no | explicit failure |
| `sampling.js: WITNESS_NODE_LIMIT` | 20000 | search nodes | no | greedy cover |
| `sampling.js: EASY_ASYLUM_MAX_CLASS` | 1 | a difficulty class | no (a rule) | G-12 |
| `maxCombinedAttempts` 3, `maxFogAttempts` 5 | | retries | no | explicit failure |
| `MAX_CUSTOM_POOL_ENTRIES` | 512 | configuration entries | no | validation only |

No other per-map or per-world count of bosses, NPCs, special bodies, events or locations exists in the engine, the data tool or the backend.

## 3. Silent fallbacks (category 2)

| Requested behavior that fails | What the generator does | Counted? | Classification |
|---|---|---|---|
| boss requested, none fits (size, Prevent same, difficulty class, budget, protected) | the destination keeps its own enemy, never an ordinary one (roaming); `bossesKeptVanilla` (boss slots) | yes (`roaming.unchanged`, counters) | CORRECTNESS REQUIREMENT (the legacy leaves such a slot unchanged) |
| "Only normal enemies" for a boss arena that is not one of the three | the boss stays | yes (`bossSlotsKeptUnsupported`) | INTENTIONAL SAFE-SUBSET DEVIATION (G-24) |
| a replacement does not fit the budget | the slot keeps its model, or the nearest in-set fitting type; the class widens | yes (`stages`, `difficultyLapses`) | G-05, G-09 |
| hostile NPC limit reached | hostile NPCs stop being candidates in that map | **no** | G-04 |
| regular enemy options with units | the unit planners run anyway | no (kinds only) | G-03 |
| category fallback, fit relaxed | **do not exist**: `category-fallback` and `fit-relaxed` of `FALLBACK_STAGES` and `categoryDowngrades` are placeholders that no code produces; `generationGuards.test.mjs` asserts 0 over 64 combinations | n/a | CORRECTNESS REQUIREMENT |
| 32 trials of a derangement fail | a deterministic rotation (G-36) | no | CORRECTNESS REQUIREMENT (unreachable) |
| an unknown enum reaches the engine | the default (G-45) | no | CORRECTNESS REQUIREMENT (unreachable from the product) |
| the catalog is unusable | the upstream prototype placements are rejected (`PROTOTYPE_RESULT_REJECTED`) | explicit error | CORRECTNESS REQUIREMENT |

## 4. Retry limits (category 3)

| Retry | Count | Failure condition | On exhaustion |
|---|---|---|---|
| map attempts (sampling) | 3 | a slot has no placement within the budget | `ENEMY_PLACEMENT_INFEASIBLE`, retryable by a new combined seed |
| combined attempts | 3 | a retryable engine failure (`KEY_PLACEMENT_UNSATISFIABLE`, `DIFFICULTY_POLICY_UNSATISFIED`, `ENEMY_PLACEMENT_INFEASIBLE`, `GENERATION_FAILED`) | `COMBINED_RETRIES_EXHAUSTED` |
| Fog attempts | 5 | `FOG_SEED_UNSOLVABLE` only (merge conflicts and `FOG_UPSTREAM_INCOMPATIBLE` are structural) | `FOG_RETRIES_EXHAUSTED` / `FOG_KEY_CONFLICT` |
| FogMod `tries++ < 100` | 100 | the graph checker still finds unreachable areas | exception "Couldn't solve seed", mapped to `FOG_SEED_UNSOLVABLE` |
| key fill | 32 | no reachable destination | `KEY_PLACEMENT_UNSATISFIABLE` |
| item difficulty | 32 | a source has no allowed target | `DIFFICULTY_POLICY_UNSATISFIED` |
| classic loops | 128, 256, 200, 8, 32 | see G-28, G-36 | an exception (the 32-trial derangement is the one silent rotation) |

**No retry exhaustion leaves content vanilla or disables a requested feature.** Every one ends in an explicit, typed error (STATICALLY VERIFIED: `generationPipeline.ts`, `combinedRandomizerBridge.ts`).

## 5. Candidate pool truncation (category 4)

None. The only `slice(0, n)` in the generation path trim the coverage reserve to the room under the cap and a message; draws use the whole list with list shrink. Sorting is deterministic ordering, not truncation. What does narrow a pool is listed as a guard: G-13, G-20, G-24, G-25, G-26, G-39.

## 6. Model budget (category 5) and event allocation (category 6)

* Instances against models: the roaming pass, the E2 bodies and the sampler count DISTINCT models and a model already in the map costs nothing (G-08). The only instance count left is G-04.
* The definition of the budget itself is the open question (G-05): DS1TR counts the models that stay, the legacy does not. Evidence for "more than 30 loads": vanilla Parish 28, 30 to 33 after the dragon and boss plans, and the legacy author's own Remaster setting (60, reverted to 30 NEW models plus the originals). Evidence against: none recorded. It needs a game experiment (F-02).
* Events: the window is sized to the events written, grows without a ceiling, and an overrun is an exception (G-51). The two guards that were justified by "too many events" are G-01 (removed) and G-04 (remaining). No other feature has an event-count limit.

## 7. Safe subsets (category 7)

| Subset | Excluded | Why | Proven or "not tested yet"? | Infrastructure now | Legacy |
|---|---|---|---|---|---|
| E2 bodies (G-24) | 21 of 24 boss encounters | single entity, death-only completion, no legacy event surgery | Firesage and Iron Golem: not tested; Priscilla, Kalameet, Pinwheel, Four Kings, Seath: structural (several bodies, scripts, transformation) | event sanitization exists for portable bosses | every boss slot |
| Hostile NPCs (G-26) | Gough, the Lower Burg Merchant | Gough unproven; the Merchant's logic script is not copied | Merchant: a data tool gap, removable; Gough: not tested | none new | four models |
| Roaming pool (G-13) | 10 bosses the legacy allows | multi-body, immobile, scripted | tails and linked parts: structural for DS1TR's single-entity model; Gwyn and the Sanctuary Guardian: not tested | none new | allowed |
| Race / Race+ (G-39, G-40) | Black Knight drops, shops, four skips | body ownership; FogMod's graph | the skips: structural (not in the graph) | none new | allowed |
| Laundromat (G-44) | any piece an NPC does not wear | every id is already loaded | structural for safety | none new | any armor |
| Fog and DLC off (G-47) | DLC off with Fog | FogMod graph | upstream limitation | none | same tool |

Nothing was expanded.

## 8. Map-specific lists (category 8)

| List | Count | Proven structural? |
|---|---|---|
| passive slots (Asylum 3, New Londo 15, Kiln 1, Parish 1) | v1: 20 protected; v2: 20 handled | passive behavior retained; roaming boss also held passive, Parish AI waits for attack |
| `eventModelLocked` | v1: 49 protected; v2: 0 excluded | sanitized by the existing model-specific event path |
| tangible ghosts | v1: 34 protected; v2: 0 excluded | grounded and made tangible by the existing replacement handler |
| Darkroot butterfly path | v1: 54 protected; v2: 0 excluded | existing active-path decoration reused |
| dragon bodies, hydras, gargoyles, bridge Hellkite | 49 placements stay | yes (linked parts, G-19) |
| Asylum protection (Easy Asylum) | 26 destinations | a rule (G-12) |
| `deferredBossActivationRegions`, `safeBossSpawnPositions`, `containedBossCombatRegions` | 4 + 9 + 1 slots | yes: boss arenas with scripted entrances; they apply to boss slots only |

## 9. Difficulty and hidden protection (category 9)

The only protections of the early game are the ones the labels name: Easy Asylum (a mode of the difficulty option), the difficulty curve (counted `difficulty`), and Placement fit. There is no hidden early-area rule in `generator.js` or `sampling.js` other than passive behavior (which remains after a roaming boss is placed). Basic mode protects through defaults, not through the generator. The original v1 path had a model budget (G-09) and 157 protected destinations (G-15..G-18, 9.2%); v2 keeps the non-worsening model budget but has no exclusions for those four categories.

## 10. Where DS1TR restricts something the legacy allows (category 10)

Every difference below is documented in the row named; none is assumed to be an improvement of the legacy. "Intentional" means a recorded decision, "accidental" means no record of a decision was found.

| Row | LEGACY (LEGACY CODE VERIFIED unless said) | DS1TR | WHY (known) | Classification |
|---|---|---|---|---|
| G-03 | the slots of Titanite Demon `c2300`, Drake `c3520` and Bounding Demon `c3421` are type-0 destinations in `valid_replacements.txt` and are replaced like any other enemy under the replace chance; the three are ignored as sources | those slots only exchange with each other as units, whatever the chance or the mode says | upstream unit design, "unit participation" deferred (Part D2) | accidental (a deferral), TEMPORARY DEVELOPMENT GUARD — ALREADY REMOVED (0.45.6: the units follow the mode and the chance; the legacy treats the Hydra, Hellkite, Undead Dragon and Lightning Gargoyles as boss-type destinations, DS1TR plans them as regular units) |
| G-04 | no cap on hostile NPCs | 12 per map | event-count fear, refuted | accidental in effect, disclosed: OBSOLETE CONSERVATISM — CANDIDATE FOR REMOVAL |
| G-05 | `MAX_UNIQUE` 30 counts NEW types, Blighttown 50 (Remaster), the originals stay | 30 models in all, Blighttown 30 | "safety first", decision D-2; never verified in the game | intentional, SUSPICIOUS — NEEDS GAMEPLAY VERIFICATION |
| G-12a | Easy Asylum for twelve hard-coded targets | every regular Asylum slot | authored independently | intentional |
| G-12b | with Bosses only the twelve targets get Capra, Taurus or Bell Gargoyles and the other Asylum slots an ordinary boss | the Asylum stays vanilla; Asylum Demon and the Moonlight Butterfly (ignored sources in the legacy) may roam | bosses are never class 1 or less | accidental (the E1 legacy table does not mention the list), SUSPICIOUS — NEEDS GAMEPLAY VERIFICATION |
| G-13 | 24 bosses (Remaster) may stand in a regular slot | 17; not Priscilla, Sanctuary Guardian, Kalameet, Centipede Demon, Gaping Dragon, Seath, Bell and Lightning Gargoyles, Four Kings, Gwyn | multi-body, immobile or scripted | intentional ("a conscious restriction, not a legacy rule"), INTENTIONAL SAFE-SUBSET DEVIATION |
| G-14 | the boss source list does not depend on Pinwheel Chaos | Pinwheel leaves the roaming pool with Chaos | none recorded | accidental, SUSPICIOUS — NEEDS GAMEPLAY VERIFICATION |
| G-15 to G-18 | no protected slots: any normal slot may get a boss | v1 excluded 157 destinations; v2 releases them through safe handlers | "special handling that a boss would break", no case recorded | resolved for v2; v1 frozen |
| G-24 | a normal enemy may stand in every boss arena | 3 of 24 | audited encounters only | intentional, INTENTIONAL SAFE-SUBSET DEVIATION |
| G-26, G-27 | four hostile NPC models; NPC replacement (six NPC slots) | three models; no NPC replacement | Gough unproven, Merchant script not copied; services would vanish | intentional, INTENTIONAL SAFE-SUBSET DEVIATION / INTENTIONAL PRODUCT RULE |
| G-38 | a key may lie in some shop and drop rows | never in a shop, gift or drop | the fill needs a pickup awarded for certain | intentional, CORRECTNESS REQUIREMENT |
| G-39, G-40 | 103 race locations; Race+ with eleven skips and four more | 69 destinations; Race+ with the eleven skips FogMod's graph models; with Fog it is Race | body ownership; FogMod's graph | intentional, INTENTIONAL SAFE-SUBSET DEVIATION |
| G-44 | any NPC armor set | pieces an NPC already wears | every id is already loaded | intentional, INTENTIONAL SAFE-SUBSET DEVIATION |
| G-47 | (FogMod itself has no DLC option) | refused | FogMod's graph | not a regression: the same tool, REAL ENGINE CONSTRAINT |

## 11. Recommended follow-up tasks (each its own focused task, in this order)

1. **F-01 Unit participation (G-03). DONE in backend 0.45.6** (resolved section below keeps the original text). Make the dragon-class units, Mass of Souls and the hydras respect Replacement chance and "Nothing", or state in the help that they do not. Largest label violation. It changes every sampling world with chance below 100 or "Nothing": a recorded exception or a new profile decision first. Until then the help of "Replace regular enemies with" and "Replacement chance" is wrong for about 80 enemies per world.
2. **F-02 Model-budget experiment (G-05, G-09).** Play Blighttown and the Undead Parish with 40 to 50 models. If it holds, count only new models or give Blighttown a higher cap; either is a generation change.
3. **F-03 Hostile NPCs (G-04, G-26).** After the first hostile-NPC playtest: remove or raise the cap, add a counter for it, decide the Lower Burg Merchant (data tool) and Gough.
4. **F-04 Easy Asylum with bosses (G-12b).** Decide whether the Asylum may get an authored easy-boss list (legacy: Capra, Taurus, Bell Gargoyles) and whether Asylum Demon and the Butterfly may roam there.
5. **F-05 Protected slots (G-15..G-18). DONE for v2 (2026-10-12).** Reused the existing event sanitation, grounding/tangibility, butterfly-path and passive handlers; v1 stays frozen. See the status update and `BOSSES_ONLY_AUDIT.md`.
6. **F-06 Pinwheel Chaos and the roaming pool (G-14).**
7. **F-07 Pool expansions** (G-13, G-24): only with game tests.
8. **F-08 Help and message wording:** "this rarely matters" (budget, G-09), the unit planners (G-03), the protected-items message (G-32).

Reproduce the numbers: `node apps/backend/scripts/guardAuditProbe.mjs <dsr-catalog.json>`.

## 12. Resolution of G-03 (2026-10-09, backend 0.45.6)

* **Cause, traced in the code.** Dragon-class units entered `buildDragonPlan` through `simpleRegularDragons` (the bridge Hellkite, the Anor Londo gargoyles, every Titanite Demon and Drake, the valley Undead Dragon, the hydras, the Bounding Demons of the Demon Ruins) and `units` (the painted Undead Dragon among the boss-or-regular units); the only gate was `config.randomizeEnemies`. "Nothing" is `options.normalReplacement === "none"`, which only the regular sampler read. The Mass of Souls groups were gated by `config.randomizeEnemies` alone. All of it came from the classic permutation, where the unit exchange has no chance.
* **Rule and result.** `docs/ENEMY_PLACEMENT_MODEL.md` 10.11. On the real catalog (engine level, `guardAuditProbe.mjs`, seed `g03-1`): chance 0% 129 -> 0 unit records (50 -> 0 visible), "Nothing" 129 -> 0, chance 50% 129 -> 56 (50 -> 16 visible), chance 100% 129 -> 129 (unchanged), "Bosses only" 100% 117 -> 0 unit records with the same 1,559 roaming bosses, "Bosses only" 0% 129 -> 0. Real pipeline (data tool, FogHost, owner-file audit, reruns): `placementMatrix.mjs unit-participation` 172/172, 13 cases, 0 retries, 0 event-window overruns, 0 non-deterministic reruns.
* **Compatibility.** `GENERATION_COMPATIBILITY.md`, "Recorded exception 5": 12 of 51 fixtures re-recorded (all with a chance below 100), 18 other cases of the same files identical, 21 cases of four files untouched.
* **What is still true.** A lone unit keeps itself; the two Mass of Souls groups exchange only together; a Titanite Demon a subset cannot place stays (counted); the boss units follow the boss options; hydras, the bridge Hellkite, the Anor Londo gargoyles and the Mass of Souls cannot take a boss and stay as they are under "Bosses only".
