# Solver witness routes of the two suspect V6 worlds

Status: recorded, **not fixed**. These are the routes the solver believed (see `docs/SOLVER_WITNESS.md`), regenerated from the Seed Code with the finished witness build; the regenerated packages matched the stored ones file by file (second world, Skill Issue Mode on as when it was made: all 73 game files identical, only `run-manifest.json` differs; first world: every file Fog writes and every event file identical, only `GameParam` differs because the stored copy predates the final Skill Issue loadout). The routes must be attempted in the game, in order. The first step that cannot be done identifies the graph/model defect; only then should anything be changed.

How to read a line: `FROM <Area> · <gate object> "<catalog description>"` is the gate you leave, `TO ...` the gate you arrive at (a different object when the pairing is *shuffled*). `[BOSS-ENCOUNTER SIDE]` marks the side that starts the boss fight, `[boss arena]` an Area tagged boss. `ONE-WAY` is what the graph has. `(first be in X)` is the solver's assumption that any Area it already reached can be entered again. `REQUIRES ... (step N)` names the earlier step that provided each prerequisite. In-map links are fixed routes of the graph (the catalog has no ladder/drop/elevator tags, so none is named).

## Common model assumptions worth testing first (visible in both routes)

- A boss arena entered through its **boss-encounter side** is followed by an in-map link out of the arena that the solver allows as soon as the arena was *reached* (step 3 below, steps 24 and 65 of the first world): the solver does not require the boss to be defeated before that link, only that the arena was reached.
- A randomized *warp* is used as a transition (the Painted World return, the Catacombs coffin, the Tomb of the Giants return, the Firelink crow): the route assumes the warp event can be triggered at that moment.
- The Lordvessel gate, the Kiln link (the four Lord Souls and the Lordvessel) and Gwyn's fog are reached at the end through Firelink.

## World 2 (newest; the applied world, 40 steps)

```
# DS1TR1-06NWCQQ3-K1GX8E56-2T3ECSK6-2TKAD8SN-SSM8TCWM-D8EE9F2F-B5TG6BHR-8TNSMRAH-TM001NQQ-0RP0
profile ds1tr-gen-v6 · Fog seed 2732646000 · Fog attempt 1/combined 1 · options: boss lords scale v4 warp world · glitched logic: off · 131/131 areas · target kiln_gwyn · 40 steps
1. START — asylum
2. RANDOMIZED BOSS FOG: FROM asylum · o8952_0000 "asylum-oscar-cell-wall"  →  TO catacombs_pinwheel · o3954_0000 "catacombs-pinwheel-boss-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
3. FIXED ROUTE (IN-MAP): FROM catacombs_pinwheel [boss arena]  →  TO totg · ONE-WAY (no way back) · REQUIRES area catacombs_pinwheel(step 2)
4. FIXED ROUTE (IN-MAP): FROM totg  →  TO catacombs · ONE-WAY (no way back)
5. FIXED ROUTE (IN-MAP): FROM catacombs  →  TO catacombs_prepinwheel · two-way
6. RANDOMIZED BOSS FOG: FROM catacombs_prepinwheel · o3954_0000 "catacombs-pinwheel-boss-boundary"  →  TO dlc_postartorias · o2916_0000 "After Artorias" · two-way, shuffled
7. FIXED ROUTE (IN-MAP): FROM dlc_postartorias  →  TO darkroot · ONE-WAY (no way back) · REQUIRES area dlc_postartorias(step 6)
8. FIXED ROUTE (IN-MAP): FROM darkroot  →  TO parish_andre · two-way
9. FIXED ROUTE (IN-MAP): FROM parish_andre  →  TO parish_church · two-way
10. RANDOMIZED BOSS FOG: FROM parish_church · o1403_0000 "parish-gargoyles-boundary"  →  TO demonruins_bedofchaos · o4972_0000 "demonruins-bed-of-chaos-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
11. ACQUIRE Lord Soul (lord_soul_goods_2501) — in demonruins_bedofchaos, item lot 2580 [catalog default Area: dukes_seath2]
12. RANDOMIZED WARP (first be in catacombs): FROM catacombs · catacombs-coffin-totg "The Catacombs coffin transport relocates the player to pristine Tomb of the Giants region 1312110."  →  TO anorlondo_rafters · o5864_0000 "anorlondo-rafters-elevator-wall" · two-way, shuffled
13. FIXED ROUTE (IN-MAP): FROM anorlondo_rafters  →  TO anorlondo_prerafters · two-way
14. FIXED ROUTE (IN-MAP): FROM anorlondo_prerafters  →  TO anorlondo_start · two-way
15. ACQUIRE Lordvessel (lordvessel) — in anorlondo_start, item lot 1090
16. ACQUIRE Peculiar Doll (peculiar_doll_goods_384) — in anorlondo_start, no lot (default area)
17. RANDOMIZED FOG (first be in darkroot): FROM darkroot · o2906_0000 "darkroot-stone-knights-wall"  →  TO totg_lower · o3962_0000 "tomb-upper-lower-wall" · two-way, shuffled
18. FIXED ROUTE (IN-MAP) (first be in parish_andre): FROM parish_andre  →  TO firelink_elevator · ONE-WAY (no way back)
19. FIXED ROUTE (IN-MAP): FROM firelink_elevator  →  TO firelink_crow · two-way
20. FIXED ROUTE (IN-MAP): FROM firelink_crow  →  TO firelink · two-way
21. FIXED ROUTE (IN-MAP): FROM firelink  →  TO kiln_start · two-way
22. RANDOMIZED WARP (first be in firelink_crow): FROM firelink_crow · firelink-crow-asylum "The Firelink crow transport event relocates the player to pristine Asylum region 1812110; this optional return route is paired with the later crow return event."  →  TO totg_nito · o3964_0000 "totg-nito-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
23. ACQUIRE Lord Soul (lord_soul_goods_2500) — in totg_nito, item lot 2560
24. LORDVESSEL GATE (first be in totg_lower): FROM totg_lower · o3963_0000 "totg-lordvessel-gate"  →  TO totg_lordvessel · o3963_0000 "totg-lordvessel-gate" · two-way (back needs (lordvessel AND kiln_start)), vanilla · REQUIRES area kiln_start(step 21) + lordvessel(step 15)
25. FIXED ROUTE (IN-MAP): FROM totg_lordvessel  →  TO totg_coffin · two-way
26. RANDOMIZED WARP: FROM totg_coffin · totg-coffin-catacombs-return "The Tomb of the Giants return event relocates the player to pristine Catacombs region 1302010."  →  TO anorlondo_cathedral · o5860_0000 "anor-londo-os-boundary" · two-way, shuffled
27. FIXED ROUTE (IN-MAP): FROM anorlondo_cathedral  →  TO anorlondo_blacksmith · two-way
28. FIXED ROUTE (IN-MAP): FROM anorlondo_blacksmith  →  TO anorlondo_front · two-way
29. FIXED ROUTE (IN-MAP): FROM anorlondo_front  →  TO anorlondo_elevator2 · two-way
30. FIXED ROUTE (IN-MAP): FROM anorlondo_elevator2  →  TO anorlondo_elevator1 · two-way
31. FIXED ROUTE (IN-MAP): FROM anorlondo_elevator1  →  TO anorlondo_building · two-way
32. RANDOMIZED BOSS FOG: FROM anorlondo_building · o5865_0000 "anorlondo-main-building-wall"  →  TO newlondo_fourkings · o6606_0000 "newlondo-four-kings-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
33. ACQUIRE Bequeathed Lord Soul Shard (bequeathed_lord_soul_shard_goods_2502) — in newlondo_fourkings, item lot 2630 [catalog default Area: demonruins_bedofchaos]
34. FIXED ROUTE (IN-MAP) (first be in anorlondo_cathedral): FROM anorlondo_cathedral  →  TO anorlondo_painting · two-way
35. FIXED ROUTE (IN-MAP): FROM anorlondo_painting  →  TO paintedworld · ONE-WAY (no way back) · REQUIRES peculiar_doll_goods_384(step 16)
36. RANDOMIZED BOSS FOG: FROM paintedworld · o1600_0000 "painted-world-priscilla-boundary"  →  TO dukes_seath2 · o7900_0000 "dukes-seath-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
37. ACQUIRE Bequeathed Lord Soul Shard (bequeathed_lord_soul_shard_goods_2503) — in dukes_seath2, item lot 2640 [catalog default Area: newlondo_fourkings]
38. FIXED ROUTE (IN-MAP) (first be in firelink): FROM firelink  →  TO kiln · ONE-WAY (no way back) · REQUIRES bequeathed_lord_soul_shard_goods_2502(step 33) + bequeathed_lord_soul_shard_goods_2503(step 37) + lord_soul_goods_2500(step 23) + lord_soul_goods_2501(step 11) + lordvessel(step 15)
39. FIXED ROUTE (IN-MAP): FROM kiln  →  TO kiln_stairs · two-way
40. FIXED BOSS FOG: FROM kiln_stairs · o8050_0000 "kiln-gwyn-boundary"  →  TO kiln_gwyn · o8050_0000 "kiln-gwyn-boundary" [BOSS-ENCOUNTER SIDE] · two-way, vanilla
```

## World 1 (the V6 smoke world, 73 steps)

```
# DS1TR1-06NWCQQ3-K2GXEE39-K5WRJEDT-CNKBNHDS-Z7CTKEJ6-0S36D1M6-0S3A7NEF-D3TK6PQX-HHB3ZGX2-Z80G12X8-GYBG
profile ds1tr-gen-v6 · Fog seed 2873971204 · Fog attempt 1/combined 1 · options: boss lords scale v4 warp world · glitched logic: off · 131/131 areas · target kiln_gwyn · 73 steps
1. START — asylum
2. RANDOMIZED FOG: FROM asylum · o8952_0000 "asylum-oscar-cell-wall"  →  TO paintedworld · o1603_0000 "painted-world-courtyard-wall" · two-way, shuffled
3. RANDOMIZED WARP: FROM paintedworld · paintedworld-anorlondo-return "The Painted World return event relocates the player to pristine Anor Londo region 1512500."  →  TO dlc_forest · o2919_0000 "dlc-kalameet-boundary" · two-way, shuffled
4. RANDOMIZED BOSS FOG: FROM dlc_forest · o2915_0000 "dlc-artorias-boundary"  →  TO catacombs · o3952_0000 "catacombs-patches-bridge-wall" · two-way, shuffled
5. FIXED ROUTE (IN-MAP): FROM catacombs  →  TO catacombs_prepinwheel · two-way
6. RANDOMIZED BOSS FOG: FROM catacombs_prepinwheel · o3954_0000 "catacombs-pinwheel-boss-boundary"  →  TO dukes_caves · o7900_0000 "dukes-seath-boundary" · two-way, shuffled
7. FIXED ROUTE (IN-MAP): FROM dukes_caves  →  TO dukes_seath1 · two-way
8. RANDOMIZED WARP: FROM dukes_seath1 · 1702901 "To Duke's Archives prison"  →  TO dukes_prison · 1702901 "To Duke's Archives prison" · ONE-WAY (no way back), vanilla
9. FIXED ROUTE (IN-MAP): FROM dukes_prison  →  TO dukes_hallway · two-way
10. FIXED ROUTE (IN-MAP): FROM dukes_hallway  →  TO dukes_crystalgolem · two-way
11. FIXED ROUTE (IN-MAP): FROM dukes_crystalgolem  →  TO dukes_library · two-way
12. FIXED ROUTE (IN-MAP): FROM dukes_library  →  TO dukes_start · two-way
13. ACQUIRE Archive Tower Giant Door Key (archive_tower_giant_door_key_goods_2005) — in dukes_start, item lot 1700630 [catalog default Area: dukes_prison]
14. ACQUIRE Broken Pendant (broken_pendant_goods_2520) — in dukes_start, item lot 27100200 [catalog default Area: darkroot_forest]
15. RANDOMIZED BOSS FOG (first be in dukes_caves): FROM dukes_caves · o7906_0000 "dukes-crystal-caves-wall"  →  TO totg_nito · o3964_0000 "totg-nito-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
16. ACQUIRE Lord Soul (lord_soul_goods_2500) — in totg_nito, item lot 2560
17. FIXED ROUTE (IN-MAP) (first be in dukes_hallway): FROM dukes_hallway  →  TO anorlondo_start · ONE-WAY (no way back)
18. ACQUIRE Lordvessel (lordvessel) — in anorlondo_start, item lot 1090
19. FIXED ROUTE (IN-MAP) (first be in dukes_prison): FROM dukes_prison  →  TO dukes_extra · two-way (back needs archive_tower_giant_door_key_goods_2005) · REQUIRES archive_tower_giant_door_key_goods_2005(step 13)
20. FIXED ROUTE (IN-MAP): FROM dukes_extra  →  TO dukes_giantcell · two-way
21. FIXED ROUTE (IN-MAP): FROM dukes_giantcell  →  TO dukes_library2 · two-way
22. FIXED ROUTE (IN-MAP): FROM dukes_library2  →  TO dukes_machines · two-way
23. RANDOMIZED BOSS FOG: FROM dukes_machines · o7906_0000 "dukes-crystal-caves-wall"  →  TO dlc_manus · o2911_0001 "dlc-manus-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
24. FIXED ROUTE (IN-MAP): FROM dlc_manus [boss arena]  →  TO dlc_top · two-way (back needs dlc_manus)
25. FIXED ROUTE (IN-MAP): FROM dlc_top  →  TO dlc_bottom · two-way
26. FIXED ROUTE (IN-MAP): FROM dlc_bottom  →  TO dlc_postartorias · two-way
27. FIXED ROUTE (IN-MAP): FROM dlc_postartorias  →  TO darkroot · ONE-WAY (no way back) · REQUIRES area dlc_postartorias(step 26)
28. FIXED ROUTE (IN-MAP): FROM darkroot  →  TO parish_andre · two-way
29. ACQUIRE Crest of Artorias (crest_of_artorias) — in parish_andre, item lot 6190 [catalog default Area: darkroot]
30. FIXED ROUTE (IN-MAP): FROM parish_andre  →  TO firelink_elevator · ONE-WAY (no way back)
31. FIXED ROUTE (IN-MAP): FROM firelink_elevator  →  TO firelink_crow · two-way
32. FIXED ROUTE (IN-MAP): FROM firelink_crow  →  TO firelink · two-way
33. FIXED ROUTE (IN-MAP): FROM firelink  →  TO newlondo · two-way
34. ACQUIRE Key to the Seal (key_to_the_seal_goods_2013) — in newlondo, item lot 1100 [catalog default Area: newlondo_building]
35. RANDOMIZED FOG: FROM newlondo · o6604_0000 "new-londo-shortcut-building-wall"  →  TO anorlondo_postrafters · o5864_0000 "anorlondo-rafters-elevator-wall" · two-way, shuffled
36. FIXED ROUTE (IN-MAP): FROM anorlondo_postrafters  →  TO anorlondo_main · two-way
37. RANDOMIZED BOSS FOG: FROM anorlondo_main · o5865_0000 "anorlondo-main-building-wall"  →  TO newlondo_lower · o6606_0000 "newlondo-four-kings-boundary" · two-way, shuffled
38. RANDOMIZED BOSS FOG (first be in dlc_postartorias): FROM dlc_postartorias · o2916_0000 "After Artorias"  →  TO dukes_seath2 · o7900_0000 "dukes-seath-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
39. ACQUIRE Bequeathed Lord Soul Shard (bequeathed_lord_soul_shard_goods_2503) — in dukes_seath2, item lot 2640 [catalog default Area: newlondo_fourkings]
40. DOOR/GATE (first be in darkroot): FROM darkroot · o2908_0000 "darkroot-crest-gate"  →  TO darkroot_forest · o2908_0000 "darkroot-crest-gate" · two-way (back needs crest_of_artorias), vanilla · REQUIRES crest_of_artorias(step 29)
41. FIXED ROUTE (IN-MAP): FROM darkroot_forest  →  TO dlc_start · ONE-WAY (no way back) · REQUIRES broken_pendant_goods_2520(step 14)
42. RANDOMIZED BOSS FOG: FROM dlc_start · o2909_0001 "dlc-guardian-boundary"  →  TO newlondo_building · o6604_0000 "new-londo-shortcut-building-wall" · two-way, shuffled
43. RANDOMIZED FOG (first be in newlondo_lower): FROM newlondo_lower · o6605_0000 "new-londo-seal-building-wall"  →  TO sens_mid · o5153_0000 "sens-roof-wall" · two-way (back needs (newlondo_building AND key_to_the_seal_goods_2013)), shuffled · REQUIRES key_to_the_seal_goods_2013(step 34) + area newlondo_building(step 42)
44. RANDOMIZED FOG: FROM sens_mid · o5152_0000 "sens-midpoint-stair-wall"  →  TO burg_upper · o1419_0000 "parish-upper-burg-wall" · two-way, shuffled
45. FIXED ROUTE (IN-MAP): FROM burg_upper  →  TO burg_upperresidence · two-way
46. FIXED ROUTE (IN-MAP): FROM burg_upperresidence  →  TO burg_bonfire · two-way
47. FIXED ROUTE (IN-MAP): FROM burg_bonfire  →  TO burg_above_bonfire · two-way
48. FIXED ROUTE (IN-MAP): FROM burg_above_bonfire  →  TO burg_ladder · two-way
49. FIXED ROUTE (IN-MAP): FROM burg_ladder  →  TO burg_lower · two-way
50. RANDOMIZED BOSS FOG: FROM burg_lower · o1413_0000 "burg-capra-boundary"  →  TO burg_capra · o1413_0000 "burg-capra-boundary" [BOSS-ENCOUNTER SIDE] · two-way, vanilla
51. RANDOMIZED BOSS FOG (first be in burg_upper): FROM burg_upper · o1405_0000 "burg-taurus-boundary"  →  TO sens_upper · o5153_0000 "sens-roof-wall" · two-way, shuffled
52. RANDOMIZED BOSS FOG: FROM sens_upper · o5150_0000 "sens-irongolem-boundary"  →  TO anorlondo_building · o5865_0000 "anorlondo-main-building-wall" · two-way, shuffled
53. FIXED ROUTE (IN-MAP): FROM anorlondo_building  →  TO anorlondo_elevator1 · two-way
54. FIXED ROUTE (IN-MAP): FROM anorlondo_elevator1  →  TO anorlondo_elevator2 · two-way
55. FIXED ROUTE (IN-MAP): FROM anorlondo_elevator2  →  TO anorlondo_front · two-way
56. FIXED ROUTE (IN-MAP): FROM anorlondo_front  →  TO anorlondo_blacksmith · two-way
57. FIXED ROUTE (IN-MAP): FROM anorlondo_blacksmith  →  TO anorlondo_cathedral · two-way
58. RANDOMIZED BOSS FOG: FROM anorlondo_cathedral · o5860_0000 "anor-londo-os-boundary"  →  TO dlc_artorias · o2915_0000 "dlc-artorias-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
59. BOSS COMPLETION ASSUMED boss_defeated_11210001 — dlc_artorias
60. RANDOMIZED BOSS FOG: FROM dlc_artorias · o2916_0000 "After Artorias" [boss arena]  →  TO demonruins_centipede · o4964_0000 "demonruins-centipede-boundary" [BOSS-ENCOUNTER SIDE] · two-way (back needs boss_defeated_11210001), shuffled · REQUIRES boss boss_defeated_11210001(step 59)
61. ACQUIRE orange_charred_ring (orange_charred_ring) — in demonruins_centipede, item lot 2670/4030
62. FIXED ROUTE (IN-MAP): FROM demonruins_centipede [boss arena]  →  TO demonruins_izalithprelava · two-way (back needs demonruins_centipede)
63. FIXED ROUTE (IN-MAP): FROM demonruins_izalithprelava  →  TO demonruins_izalithlava · two-way · REQUIRES orange_charred_ring(step 61)
64. FIXED ROUTE (IN-MAP): FROM demonruins_izalithlava  →  TO demonruins_izalith · two-way (back needs orange_charred_ring)
65. RANDOMIZED BOSS FOG: FROM demonruins_izalith · o4972_0000 "demonruins-bed-of-chaos-boundary"  →  TO darkroot_stone · o2906_0000 "darkroot-stone-knights-wall" · two-way, shuffled
66. RANDOMIZED BOSS FOG: FROM darkroot_stone · o2904_0000 "darkroot-butterfly-boundary"  →  TO newlondo_fourkings · o6606_0000 "newlondo-four-kings-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
67. ACQUIRE Bequeathed Lord Soul Shard (bequeathed_lord_soul_shard_goods_2502) — in newlondo_fourkings, item lot 2630 [catalog default Area: demonruins_bedofchaos]
68. FIXED ROUTE (IN-MAP) (first be in burg_lower): FROM burg_lower  →  TO depths · two-way · REQUIRES area burg_capra(step 50)
69. RANDOMIZED BOSS FOG: FROM depths · o1400_0000 "To Gaping Dragon"  →  TO demonruins_bedofchaos · o4972_0000 "demonruins-bed-of-chaos-boundary" [BOSS-ENCOUNTER SIDE] · two-way, shuffled
70. ACQUIRE Lord Soul (lord_soul_goods_2501) — in demonruins_bedofchaos, item lot 2580 [catalog default Area: dukes_seath2]
71. FIXED ROUTE (IN-MAP) (first be in firelink): FROM firelink  →  TO kiln · ONE-WAY (no way back) · REQUIRES bequeathed_lord_soul_shard_goods_2502(step 67) + bequeathed_lord_soul_shard_goods_2503(step 39) + lord_soul_goods_2500(step 16) + lord_soul_goods_2501(step 70) + lordvessel(step 18)
72. FIXED ROUTE (IN-MAP): FROM kiln  →  TO kiln_stairs · two-way
73. FIXED BOSS FOG: FROM kiln_stairs · o8050_0000 "kiln-gwyn-boundary"  →  TO kiln_gwyn · o8050_0000 "kiln-gwyn-boundary" [BOSS-ENCOUNTER SIDE] · two-way, vanilla
```
