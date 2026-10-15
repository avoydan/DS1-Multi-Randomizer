# Pre-Nexus roaming-boss fit classification snapshot

Captured from production code before changing fit classes. This document and its JSON sibling preserve the prior behavior; they are not inputs to generation.

- Captured: 2026-10-15
- Repository HEAD at capture: 870aa577a3a282206645ef63753dca0c37c90e4b
- Branch: release/nexus-0.39.0
- Source catalog: C:\Users\Adam\ds1tr-qa\profile-0350-inst\state\runs\6cab6d1f-375c-41fe-85c4-606f8aca0a7d\combined-0\dsr-catalog.json
- Catalog SHA-256: 76863fbdec11b0e38560cceb60e623d065ae9c7686e0bf48189f785997645e79
- Catalog generated: 2026-10-08T18:27:09.0217455+00:00

## Production baseline

Configuration: Bosses only, 100% boss chance, Only where they fit, Random difficulty, Type replacement disabled, Prevent same OFF, Pinwheel Chaos OFF.

- Eligible destinations: 1,716
- Bosses placed: 562
- Vanilla fit survivors: 1,154
- Placement: 32.75%

## Effective candidate classification

Current candidate archetypes: 17. Per-class counts: class 0: 0; class 1: 0; class 2: 1; class 3: 9; class 4: 5; class 5: 2.

- Pinwheel — c3320, NPC 332000, class 2
- Capra Demon — c2240, NPC 224000, class 3
- Chaos Witch Quelaag — c5280, NPC 528000, class 3
- Dragon Slayer Ornstein — c5270, NPC 527000, class 3
- Executioner Smough — c2360, NPC 236000, class 3
- Knight Artorias — c4100, NPC 410000, class 3
- Manus, Father of the Abyss — c4500, NPC 450000, class 3
- Moonlight Butterfly — c3230, NPC 323000, class 3 (authored override)
- Super Smough — c2360, NPC 236001, class 3
- Taurus Demon — c2250, NPC 225000, class 3
- Demon Firesage — c2231, NPC 223100, class 4
- Gravelord Nito — c5220, NPC 522000, class 4
- Great Grey Wolf Sif — c5210, NPC 521000, class 4
- Iron Golem — c2320, NPC 232000, class 4
- Super Ornstein / alternate Ornstein — c5271, NPC 527100, class 4
- Asylum Demon — c2232, NPC 223200, class 5
- Stray Demon — c2230, NPC 223000, class 5

## Explicit current exclusions

The source exclusion list contains 15 model IDs:

- Bed of Chaos — c5230, NPC 523000 — Bed of Chaos: immobile, floor script
- Bed of Chaos — c5401, NPC 540100 — Bed of Chaos core
- Bell Gargoyle — c5350, NPC 535000, 535001 — Bell Gargoyles
- Black Dragon Kalameet — c4510, NPC 451000, 451001 — Black Dragon Kalameet tail/two bodies
- Anor Londo Gargoyle — c5351, NPC 535100 — Anor Londo Gargoyles
- Ceaseless Discharge — c5250, NPC 525000 — Ceaseless Discharge: immobile, 10 m tall lava body
- Centipede Demon — c5200, NPC 520000 — Centipede Demon linked
- Crossbreed Priscilla — c2730, NPC 273000 — Crossbreed Priscilla tail
- Dark Sun Gwyndolin — c5320, NPC 532000 — Dark Sun Gwyndolin: neutral team, stationary, scripted
- Four Kings — c5390, NPC 539000, 539001 — Four Kings linked bodies
- Gaping Dragon — c5260, NPC 526000 — Gaping Dragon tail
- Gwyn, Lord of Cinder — c5370, NPC 537000 — Lord Gwyn: scripted entrance, arena-bound
- Hellkite Drake — c3430, NPC 343000 — Hellkite Drake bridge dragon bodies
- Sanctuary Guardian — c3471, NPC 347100, 347101 — Sanctuary Guardian tail
- Seath the Scaleless — c5290, NPC 529000, 529001 — Seath the Scaleless tail/arena script

Other effective boss slots without a bossKillSouls row are recorded in the JSON under otherBossesExcludedBySoulGate. The production candidate gate requires a known soul reward, excludes the models above, and deduplicates by modelName:npcParamId. Pinwheel Chaos also removes the main Pinwheel source slot before pool construction when enemy randomization is enabled.

## Root-cause audit of the old fit model

The classes are determined by SIZE_EDGES = [0.9, 1.7, 2.6, 4.2, 7]. A slot footprint is max(2 * hitRadius, hitHeight), and its class is the count of thresholds it reaches. For destination classes 0 and 1, maximum permitted footprints are below 0.9 and 1.7 game units. No boss in the old candidate pool has a source capsule below 1.7.

The old destination-class-2 pool had only Pinwheel because its source boss slot has hitHeight=2.0, hitRadius=0.6, footprint 2.0, and therefore class 2. Every other currently included boss reaches at least the 2.6 edge and is class 3 or higher. Artorias (2.6), Capra (2.8), and Taurus (3.5) therefore became class 3 under the same generic footprint thresholds.

The concrete cause was the combination of generic class edges and direct boss-slot capsule measurements, with no authored lower-class overrides for those bosses. The thresholds intentionally form the regular class scale, but they did not encode the owner's desired boss-placement policy. This was not a planner candidate-selection defect, and these values were not derived from the legacy randomizer. The historical special override was Moonlight Butterfly class 3 because its 0.4-by-0.19 capsule does not describe its body. Gwyndolin had a separate explicit exclusion (neutral team, stationary/scripted), not a size-class cause.

For excluded records, the effective minimum fit class is not applicable (null in JSON); the recorded source capsule class is diagnostic only and did not make that boss a candidate. Candidate and excluded records point to their exact controlling source symbols in JSON. This audit records the old model only. It does not override the owner-approved classifications in the follow-up task.

## Restore instructions

To restore the exact pre-change production classification, use the values in the JSON restore object and restore these production locations:

1. engines/combined/vendor/src/core/sampling.js: SIZE_EDGES.
2. engines/combined/vendor/src/core/generator.js: bossKillSouls, roamingExcludedModels, roamingSizeClassOverrides.
3. engines/combined/vendor/src/core/generator.js: restore c5320 to `roamingExcludedModels`, restore `roamingSizeClassOverrides` to only c3230 => class 3, and restore the shared `bossSourceSlots` Pinwheel Chaos exclusion (including its effect on the roaming source pool).
4. Restore the explicit Bed of Chaos source-slot exclusion and the original source gates recorded in the JSON.

The JSON stores the full current values of these constants plus the relevant source-selection rule. Do not restore unrelated generator files or modify Seed Code/profile formats.
