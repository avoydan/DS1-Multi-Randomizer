# DLC audit and the DLC switch

Status: implemented (backend 0.45.0, contracts 0.31.0, desktop 0.33.0). Generation profile `ds1tr-gen-v1` is unchanged.

## What "the DLC" is

Dark Souls Remastered has exactly one DLC map: `m12_01_00_00` (Artorias of the Abyss: Oolacile Sanctuary, Chasm of the Abyss, Royal Wood, Oolacile Township, Battle of Stoicism/Sanctuary Guardian, Artorias, Manus, Kalameet and their enemies, items and rewards). Everything below follows from that map id and from what the data tool records about it.

## Semantics

| Setting | Meaning |
|---|---|
| **DLC content ON** (default, `output.includeDlc = true`) | Exactly what DS1 Triple Randomizer has always done. Nothing runs differently; every earlier world, Seed Code, preset and golden fixture is unchanged. |
| **DLC content OFF** (`output.includeDlc = false`) | DS1TR does not randomize or use DLC content: no DLC enemy slot, DLC boss, DLC pickup, DLC merchant shop, DLC NPC gift, DLC boss reward, DLC-only enemy drop or DLC-exclusive starting equipment takes part, as a source or as a destination. **The DLC is never removed or edited in the game**: its map, event, param rows and files stay byte-identical to the clean game (verified by SmokeVerify `dlc-untouched`). |
| **Fog randomizer + DLC OFF** | **Not supported.** FogMod has no DLC option: its graph always contains the DLC areas and warps. A world randomizer run cannot leave the DLC out, so the combination is refused by validation (`Excluding DLC content needs the fog randomizer to be off: …`) and the UI keeps the switch on and locked while the fog randomizer is on. There is no fake compatibility. |

## How it is done

The vendored engine is not changed. The data tool scan produces a catalog of everything the engine may read. With the DLC off, `excludeDlcFromCatalog` (`apps/backend/src/adapters/dlcExclusion.ts`) removes the DLC records from a copy of that catalog before `core.generate`. The engine cannot place what it does not know, and the data tool writes only what the engine placed, so the DLC is untouched. With the DLC on the function is not called, so the ON path is byte-for-byte what it was.

The one new piece of scan data is `dlcOnly` on enemy drop lots (fork patch 0025): a drop lot is DLC-only when every enemy that awards it stands in the DLC map and no other enemy or event awards it.

## Audit matrix

Questions: (1) is there DLC content in the subsystem; (2) can it be a source; (3) a destination; (4) how it is left out; (5) is the DLC untouched in the game; (6) does anything depend on it being on; (7) progression; (8) hints; (9) Seed Code / preset / history; (10) test coverage.

| Subsystem | DLC content | OFF behavior | Evidence |
|---|---|---|---|
| Items, world pickups | 56 world item lots + 38 treasures in the DLC map | not a destination, and their items are not in the pool (they stay where the game puts them) | `dlcSweep`, matrix `items-only`, `dlc-untouched` lot rows |
| Items, shops | the DLC has merchants: 32 shop rows are sold by DLC merchants (the Artorias set rows 1581-1584, 6410-6426, 6500-6507, 6600-6611; identified by the DLC sell-condition flag area 1121xxxx and by the row ranges the DLC talk script opens) | not a destination and not a source; the rows stay as the game has them | `dlcShopRowIds`, `dlcExclusion.test.mjs`, `dlcSweep`, `SmokeVerify dlc-untouched` (ShopLineupParam rows) |
| Items, gifts | the gifts of the DLC NPCs Hawkeye Gough (his model c4110 stands only in the DLC map) and Elizabeth: item lots 1510 and 1520 (treated as DLC on the evidence of the NPCs; the safe side) | not a destination and not a source | `DLC_GIFT_LOT_IDS`, the same tests, ItemLotParam rows compared by `dlc-untouched` |
| Gifts / shops of the base game | every other gift and shop | unchanged | catalog |
| Items, enemy drops | 16 drop lots only DLC enemies award, plus DLC boss reward rows | not touched | `dlcOnly` tag, `dlc-untouched` |
| Items, starting equipment | 9 entries of 20 DLC-exclusive items in the starting pools | not offered by Starting Class / Starting Equipment | `dlcExclusion.test.mjs` |
| Soul items (transposed) | 2 recipes of DLC boss souls, 4 DLC boss rewards | DLC boss souls are not in the pool; the recipes are left out | `dlcSweep` `items-only`, `race` |
| Enemies (regular) | 210 enemy slots, 63 archetypes that stand nowhere else | DLC enemies are never placed in other maps and never replaced | `dlcSweep`, matrix `enemies-*` |
| Bosses | 5 boss slots (Sanctuary Guardian, Artorias, Manus, Kalameet, their phases) | not randomized; the DLC bosses are neither a source nor a target of any boss/enemy placement | matrix `bosses-*` |
| Progression, keys, Race / Race+ | DLC pickups are removed from the candidate locations | no DLC location is a key destination (checked for every placement of the world) | matrix `keys-lords`, `race`, `race-plus` |
| Item difficulty (Very Unfair) | DLC pickups are not tiered | not used | matrix `very-unfair-transposed` |
| Laundromat Mixup | NPC armor only; no DLC | unchanged | matrix `laundromat-npc-armor` (the starting-equipment variant trips an unrelated lots-verifier limit: starting-equipment lots are not in the plan lists; starting equipment is covered by the unit test and the engine-level `items-class-equipment` sweep, not by the real pipeline) |
| Fog randomizer | the whole DLC graph | **refused** with DLC OFF (see above) | validation test, UI lock |
| Hints | derived from the placement plan | the plan names no DLC content, so no hint names it | hint tests on DLC OFF worlds |
| Smoke tests (SmokeVerify) | audits read the final package | new `dlc-untouched` command; the other audits are unchanged | matrix `dlc-off` |
| Seed Code | `output.includeDlc` was already part of the canonical settings | OFF is a different code; a code or preset without the field means ON | `seedCode.test.mjs`, `preset.test.mjs` |
| Presets | `output.includeDlc` is exported | OFF round-trips; with Fog it is refused | `preset.test.mjs` |
| Run history | settings are stored with the run | Load Seed / Regenerate files / Restore-and-create-world reproduce the world with its DLC choice | `seedPersistence` test (Recreate, Regenerate files and the persisted settings with DLC off); Load seed and Restore-and-create-world in the packaged app (`FINAL_CONFIG_AUDIT.md`); `restoreCreateWorld.test.mjs` itself was not extended |
| Renderer | the switch | enabled, default ON; locked ON with a short note when Fog is on; EN + PL | `locales.test.mjs`, UI audit |

## Results (AUTOMATED VERIFIED)

Real pipeline (real data tool, packaged FogHost not involved because the combination is refused), clean game copy, `placementMatrix.mjs dlc-off --reruns 2`: **165 worlds, 0 failures, 0 retries, 0 invariant violations, 0 non-deterministic reruns** (each world is generated three times and the packages are identical). 160 of them have the DLC off, in 11 cases: items only, enemies only (sampling and permutation), bosses classic, bosses random + bodies, full Triple without fog, shuffled keys + Lordvessel + Lord Souls, Race Mode, Race Mode +, Very Unfair + Soul items Transposed, Laundromat + starting equipment; 5 control worlds have the DLC on. For every DLC-off world SmokeVerify `dlc-untouched` compared the final package with the clean game: the DLC map and event files are absent from the package or byte-identical, **6,880 NpcParam rows** of DLC-only enemies and **12,480 item lot rows** (DLC pickups, DLC-only enemy drops, DLC boss reward rows) are cell for cell the clean game's, and the plan names the DLC map nowhere. Beside it the standing audits ran on the same packages: 78,060 item lot rows, 900 boss reward rows, the progression model of 45 key worlds with 690 keys placed, all without a failure.

The final broad sweep (`placementMatrix final-rc`) added **200 more DLC-off worlds** (full Triple without fog with all pools, hostile NPCs and bosses in both kinds of slot; shuffled keys + Lords + Race Mode): 0 failures, 8,600 more DLC NpcParam rows and 15,600 more DLC item lot rows identical to the clean game. In total **360 DLC-off worlds** went through the real data tool. The packaged app (0.34.0 build) applied a DLC-off world to a copy of the game (51 files changed, none a DLC map file, `dlc-untouched` passed) and Restore returned the copy to the clean game; the installed final release candidate (0.34.1) reproduces a DLC-off golden world (`dlc-off-triple-without-fog`) byte for byte.

Engine level (`dlcSweep.mjs`, real extracted catalog, **810 DLC-off worlds** in the final run; the DLC-on controls find the DLC content in 132 / 132 worlds): the plan of every DLC-off world contains no DLC model, no DLC-exclusive item, no DLC boss reward, no DLC-only drop row; every pickup is written once and every item is taken once; and with the DLC on the same searches do find DLC content (so the audit cannot pass by looking in the wrong place). The final sweep numbers are in `docs/IMPLEMENTATION_STATUS.md`.

## Compatibility

* The switch already existed in the canonical settings (always `true`), so every Seed Code made before this change decodes as DLC ON and keeps its exact text. A settings document without the field also means ON.
* The ON path is untouched: all 47 golden fixtures are byte-identical (see the golden section of `docs/IMPLEMENTATION_STATUS.md`).
* New golden fixtures cover DLC OFF only (`goldenCompatibility`, case names starting `dlc-off`).

## Drop rates of the DLC enemies (found after the 0.45.2 audit; backend 0.45.3)

"Guaranteed enemy drops" (`items.guaranteedEnemyDrops`) raises the item chances of every drop table to a total of 100% in `ItemLotParam`. With the DLC off the data tool still did that for the DLC's own drop lots and for the reward rows of DLC bosses, so the DLC's enemies dropped differently although the DLC was meant to be left alone. The first two audits missed it for the same reason as the shops: `dlc-untouched` compared the item ids of the lot rows and skipped the `lotItemBasePoint` cells (the chances), copied from the verifier of the ordinary lots. Found by running the verifier against a DLC-off world made WITH guaranteed drops (`dlcVerifierControl.mjs`, `CONTROL_DLC_OFF=1`: 16 problems).

Fix: `excludeDlcFromCatalog` returns `excludedDropLots` (the DLC-only lots and the reward rows of DLC bosses), the bridge passes them as `dlcExcludedDropLots`, and `DsrDataTool` leaves exactly those rows alone when it scales the chances (fork-side tool patch 0026). `dlc-untouched` now compares the `lotItemBasePoint` cells as well. Worlds with the DLC on are unchanged (47 of 47 fixtures byte for byte), as are DLC-off worlds without guaranteed drops.

Evidence (real pipeline, real data tool, `placementMatrix`): **365 DLC-off worlds with guaranteed enemy drops on (95 + 70 in `dlc-off`, 100 + 100 in `final-rc`): 0 failures, 0 retries, 0 non-deterministic reruns, every DLC row (enemies, lots incl. chances, shops) identical to the clean game.**

## Deferred / unsupported

* Fog randomizer with the DLC off (FogMod has no such option; a separate fog graph would be a FogMod change).
* Removing or hiding the DLC in the installed game: out of scope and a data-loss risk; DS1TR never edits what it does not randomize.
