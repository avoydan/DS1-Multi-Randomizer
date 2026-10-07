# Item and progression model: soul items, key placement, Lordvessel and Lord Souls

Status labels used here: **AUTOMATED VERIFIED** (a test or script in the repository passes), **STATICALLY VERIFIED** (checked on files and game data, the game was not played), **GAMEPLAY VERIFIED / UNVERIFIED**, **UNSUPPORTED**, **DEFERRED**.
Nothing in this document is GAMEPLAY VERIFIED.

Sources: the pinned legacy Item Randomizer (`HotPocketRemix/DarkSoulsItemRandomizer`, commit `f7cabbd`, no license: **read for behavior only, nothing is copied**; the lists below are rebuilt from the game's own data), the vendored combined engine (kellernz1 `ds-randomizer`, GPL-3.0), the pinned FogMod (Apache-2.0), and a clean copy of Dark Souls Remastered. Every game-data fact below is reproduced by `DsrDataTool scan` (the catalog fields `bossRewards` and `transpositionRecipes`, patch 0021) or by the static verifiers named in the text; the one-off analysis programs used to find them were scratch tools and are not in the repository.

## 1. What the legacy tool means (behavior reference)

### 1.1 Item classes and the global placement

Every item lot part of the legacy table has a class: EASY / MEDIUM / HARD, NPC_*, SALABLE_* (shop stock), SMALL_SOUL, BIG_SOUL (goods 400-409), BOSS_SOUL (goods 700-711), KEY (the key list below) and IGNORE / NOT_IN_POOL. Locations have the matching classes (and an *area* and the flags `is_transient`, `is_race_key_loc`). Placement order: ignored items stay, upgrade items, **key items**, starting equipment, then every other item (largest first) into a random location of a class order given by the *Difficulty* option. SMALL_SOUL, BIG_SOUL and BOSS_SOUL are treated as HARD items. Key items are placed before everything else.

### 1.2 Soul items (option "Soul items")

* **Shuffled**: soul items are placed like any item. In the legacy this includes the **boss souls**: the twelve BOSS_SOUL lots are both pool items and locations.
* **Replaced** ("consumable"): before placement every SMALL_SOUL / BIG_SOUL part (goods 400-409) becomes a random consumable from a fixed table with a random count. Boss souls are untouched.
* **Transposed**: each BOSS_SOUL part, with probability **0.75**, becomes a *boss item of that boss*: a list is drawn uniformly from the soul's lists, then one item uniformly from the list; each list is a boss weapon at its reinforcement levels (ids in steps of 100) (Gwyn's soul also has a one-item list holding the miracle Sunlight Spear; the Guardian Soul has only itself). With probability 0.25 the soul stays. The (possibly transposed) item is then placed like every other HARD item. Lesser souls are not touched.

"Transposition" is the game's own word: the Giant Blacksmith turns a boss soul into a boss weapon. So Transposed *rewrites the boss soul into one of the weapons it could be turned into*, and moves it.

### 1.3 Key placement (option "Key placement"; internal: difficulty of keys)

* **Not Shuffled**: the 39 keys sit in their vanilla locations (`KEY_NAMES`: lordvessel, key_to_the_seal, blighttown_key, key_to_depths, covenant_of_artorias, rite_of_kindling, orange_charred_ring, sewer_chamber_key, large_ember, mystery_key, basement_key, undead_asylum_f2_west_key, annex_key, watchtower_basement_key, darkmoon_seance_ring, key_to_new_londo_ruins, cage_key, the three Archive Tower keys, peculiar_doll, broken_pendant, crest_key, crest_of_artorias, residence_key, ten embers, cast_light and the four Lord Soul items).
* **Shuffled**, **Race Mode**, **Race Mode +**: all keys that are not pinned are placed *at once* by a trial: for every key (in name order) a location is chosen at random among the non-transient locations that (a) pass the item/location difficulty ordering, (b) are in the key's allowed areas (a short list for three keys, every area otherwise), (c) are not used by another key of the trial; **Race** modes additionally only use locations flagged `is_race_key_loc`. A trial is accepted only if a flood fill over the hand-authored **area graph** (areas, doors opened by named keys, "logic" edges for bells and the Kiln) reaches every area from the start with the keys it finds on the way; the legacy also checks that the Duke's Archives can be escaped from the prison without any key. A rejected trial is repeated until one is accepted (**unbounded**). Race Mode + adds sequence-break edges (Capra skip, master key, Seal skip ...) and the Purple Coward's Crystal as a 40th key.
* Keys may land in **shops** (with a price chosen by the location's class).
* The Lordvessel option **"Senile Gwynevere"** (`use_lordvessel`) and the Lord Souls option **"Senile Primordial Serpents"** (`use_lord_souls`) decide whether those keys take part: when an option is off the key is placed in its vanilla location before the trial and stays.
* The Kiln is reachable in the check only when every other area has been reached and the four Lord Soul items have been found.

### 1.4 What the legacy has no concept of

A DLC switch (there is no DLC option anywhere in the legacy options; the DLC is simply part of the location table). The `output.includeDlc` control of DS1TR comes from the vendored engine's configuration and is read by nothing (section 7).

## 2. What the game data says (clean copy of DSR, scan fields of patch 0021)

### 2.1 Where boss rewards come from

Boss rewards are **not** NpcParam drops (no NpcParam row, including the scaled copies the data tool adds, names a boss reward lot) and not map treasure. They are awarded by **one shared event**: `event/common.emevd.dcx`, event **970**. The event waits for a flag (a boss defeat) and awards up to three item lots (`AwardItemLot`, `2003[36]`). Every boss of the game is one initialisation of it (27 initialisations in `common.emevd` event 0: trigger flag, primary lot, two bonus lots, almost all of them Humanity or a Homeward Bone). So:

* the reward belongs to the **trigger flag of the encounter**, not to the body that stands in the arena: Part D boss sampling, E1 roaming bosses, E2 encounter bodies, the Second Gargoyle and Pinwheel Chaos change the arena's body and never touch event 970, its initialisations or the lots (asserted below);
* the lot is awarded **with the rows that follow it consecutively** (the chain rule FogMod's `ItemReader` uses: it walks `lot++` while the row exists). Example: initialisation 6 (Sif, flag 5) awards lot 2540, the ring *Covenant of Artorias*, and 2541, *Soul of Sif*. Initialisation 21 (flag 16) awards lot 2660, the *Big Pilgrim's Key*, with 2661, *Demon's Great Hammer*;
* a few rewards are awarded by a map event instead (the Centipede Demon's *Orange Charred Ring*, lot 2670, event 11415386 of `m14_01`).

The map of an encounter is read from the data: the trigger flag decodes as `1AABBxxxx` (a map flag) or, for the small global flags, is the flag that one map's event file sets with `SetEventFlag` (`2003[02]`). The four DLC rewards (Guardian Soul, Soul of Artorias, Soul of Manus, the Calamity Ring) are in `m12_01`.

**Catalog (`bossRewards`)**, 27 entries, each: id, slot (the initialisation's slot), trigger flag, map, DLC flag, and per awarded lot the role (primary / bonus) and the **chain of rows with their entries**. The scan fails with a typed scan error when event 970 no longer has the shape it understands (a flag wait and three parameterised awards) or when an NPC row names a chain row. The twelve boss soul rows (goods 700-711):

| Soul | Lot row | Reward | Trigger map |
|---|---|---|---|
| 700 Soul of Quelaag | 2570 | 9 | m14_00 |
| 701 Soul of Sif | 2541 (chain of 2540, the Covenant of Artorias) | 6 | m12_00 |
| 702 Soul of Gwyn | 2650 | 20 | m18_00 |
| 703 Core of an Iron Golem | 2590 | 14 | m15_00 |
| 704 Soul of Ornstein | 2610 | 16 | m15_01 |
| 705 Soul of the Moonlight Butterfly | 2530 | 5 | m12_00 |
| 706 Soul of Smough | 2620 | 17 | m15_01 |
| 707 Soul of Priscilla | 2520 | 4 | m11_00 |
| 708 Soul of Gwyndolin | 2600 | 15 | m15_01 |
| 709 Guardian Soul | 2680 | 23 | m12_01 (DLC) |
| 710 Soul of Artorias | 2690 | 24 | m12_01 (DLC) |
| 711 Soul of Manus | 2700 | 25 | m12_01 (DLC) |

(The legacy item table numbers two of these lots 2611 and 2621; the DSR rows are 2610 and 2620. Every id here is read from the DSR game data.)

### 2.2 Transposition recipes are game data

A boss weapon asks for its soul through `EquipParamWeapon.materialSetId` -> `EquipMtrlSetParam.materialId01-05`. The scan derives, for every soul, the weapons that name it (`transpositionRecipes`), grouped into **families** (a run of ids in steps of 100: one weapon at its reinforcement levels). **Cross-check against the legacy lists:** for all twelve souls the derived weapon set is *identical* to the legacy's ranges (700: 9 weapons, 701: 57, 702: 18, 703: 9, 704: 11, 705: 48, 706: 12, 707: 9, 708: 10, 709: none, 710: 17, 711: 6). Two differences in how the draw is shaped (both documented deviations):

1. the legacy puts *two runs into one list* for the souls of Ornstein (704) and Artorias (710), so inside that list every weapon has the same chance; here each run is a family of its own, so the single weapon 1054000 of Ornstein's soul is chosen with probability 1/2, not 1/11;
2. the legacy offers Gwyn's soul one more list, the miracle Sunlight Spear. No parameter ties the miracle to the soul (it is a talk-script exchange), so it cannot be derived and is **not offered**.

### 2.3 Where the keys come from

The 25 key items of FogMod's annotation (27 entries, `cast_light` and the Purple Coward's Crystal are "random only"), their vanilla sources, classified by mechanism:

| Class | Keys |
|---|---|
| map treasure lot (world lot) | Basement Key, Undead Asylum F2 West Key, Annex Key, Watchtower Basement Key, Darkmoon Seance Ring (a ring), Key to New Londo Ruins, Cage Key, the three Archive Tower keys, Peculiar Doll, Crest Key (DLC) |
| NPC gift lot (common event 911) | Lordvessel (lot 1090), Key to the Seal (1100) |
| **boss reward** (event 970) | Blighttown Key (2500), Key to Depths (2510), Covenant of Artorias (2540), the four Lord Soul items (2560, 2580, 2630, 2640) |
| boss reward by a map event | Orange Charred Ring (2670) |
| NPC / enemy drop lot | Broken Pendant (27100200), Crest of Artorias (6190), Residence Key (6230 chain) |

Lots 4000-4077 are **not** part of the game: they are the treasures of a hidden debug item room in `m10_02` (garbled debug names), they hold every key (that is where the *Lift Chamber Key* lives) and no event awards them. The *Rite of Kindling* (lot 2550, the Pinwheel's reward) and the *Big Pilgrim's Key* (2660, the Asylum Demon's) are boss rewards that FogMod does not track.

### 2.4 How the game consumes progression items

By **possession**, not by place: the Kiln events check the four Lord Soul items and the Lordvessel in the inventory (`3[4]`, category goods) and remove them (`2003[24]`); doors check their keys the same way. The Lordvessel is awarded by the gift event with lot 1090 and sets flag 1090. So moving a key to another item lot keeps every consumer working, **as long as the new place is awarded for certain** (a map treasure, a gift, a boss reward) and the world can still be completed.

### 2.5 FogMod already holds a progression graph

`fog.txt` (Apache-2.0, pinned) has 150+ areas, every door and fog gate with its key condition, the key items with their vanilla area, and `LotLocations`: **440 item lot rows mapped to an area**. FogMod's `ItemReader` reads the (randomized) lots, puts every key into the area of the lot that holds it, and its solver (`GraphChecker`) makes the world completable around them. Built with *no* randomization option on, the same graph is the vanilla world: prototype result (scratch tool referencing the built FogHost): **vanilla placement reaches all areas (unvisited: 0), a check takes under a millisecond, and uniformly random placement of the keys over the lots is valid about half of the time (93 of 200 in the corrected prototype; an earlier figure of 0 of 200 came from a prototype error that dropped Cast Light, which FogMod tags `randomonly`)**. Retrying random placements would therefore sometimes work, but it would give no guarantee, no reason for a failure and no bound; the placement is constructive instead (section 5).

## 3. Transposed in DS1TR (built, section 4)

Implemented exactly as the legacy: the twelve boss soul rows join the item pool **only** when Soul items is Transposed; each soul has a 75% chance (stream `soul-items-transposed`, drawn per soul in reward order) to become a weapon of its recipe (a family drawn uniformly, then one reinforcement level), then the whole pool is shuffled. The Guardian Soul never changes. Shuffled and Replaced do not know the option exists (their worlds are byte for byte what they were).

**Deviations from the legacy (all deliberate):** (1) in Shuffled and Replaced the boss souls stay at their bosses (the legacy shuffles them in every mode) - changing that would change every existing Seed Code; (2) the two list partitions of 2.2; (3) no Sunlight Spear; (4) the replacement targets are lots only (no price logic) and the shuffle is DS1TR's global permutation, not the legacy's difficulty-ordered fill (no "Fair / Unfair" classes).

**Compatibility.** `items.soulItems` already exists in every Seed Code, preset and settings file; the third value `transposed` is new, so no old record changes (their value is `shuffled` or `replaced`). A record with Transposed was not possible before, so nothing is reinterpreted. Presets keep their version (13): the value is an extension of an existing enum.

**Ownership.** A boss soul is the reward of an *encounter flag*. Randomizing a body cannot move it, duplicate it or delete it (the data tool never writes event 970, its initialisations or an NPC row naming a reward lot; `SmokeVerify lots` asserts all three on every generated package). The only moves are the ones Transposed itself makes, and they are the legacy's: the soul leaves its boss (`transposedFrom` in the placement says which), a weapon or any pool item takes its place.

**Fog.** FogMod reads item lots to find its key items and never writes them; its tracked copies may land in any covered lot, the boss soul rows (all in `LotLocations`) included; the `fogPlacementRules` repair applies to the enlarged pool unchanged. DLC rule: the DLC boss rows carry `m12_01_00_00`, so progression, Titanite, Ember and the other restricted items never land there.

## 4. Quick map of what exists

| Piece | Where |
|---|---|
| scan: `bossRewards`, `transpositionRecipes` | `DsrDataTool` (patch 0021) |
| engine: Transposed | `generator.js` (patch 0022), config value `soulItems: "transposed"`, stream `soul-items-transposed` |
| apply: boss reward rows are written like any item lot row | `DsrDataTool` item assignments (`bossRewards` list, patch 0021) |
| verifier | `tools/smoke-verify` `lots`: planned rows in the package, no other item lot changed, no NpcParam names a reward, event 970 and its initialisations equal the clean game's |
| tests | `combinedSoulsTransposed.test.mjs`, `soulItemsSeed.test.mjs`, `enemyPlacementOrdering.test.mjs` |
| real-data sweep | `placementMatrix.mjs souls-h` |
| hint metadata | every placement keeps `rowId`, `sourceRowId`, `itemName`, `originalLocation`, `randomizedLocation`; a boss reward placement adds `bossReward` (the reward id) and `transposedFrom` |

## 5. Key placement (built)

**Option.** `items.keyPlacement`: `vanilla` (neutral: every key stays where the game puts it, the behavior of every earlier version) or `shuffled`. It needs World items and cannot be combined with Protected items (the configuration is refused with a plain message). The legacy's **Race Mode** and **Race Mode +** are **UNSUPPORTED** (not built, not offered; the UI says "Not built"). The legacy's "keys may land in shops" is UNSUPPORTED on purpose (below).

**One model of the world.** FogMod's graph (`fog.txt`, pinned) built with every randomization option off is the vanilla world: areas, the doors and gates between them with the key condition FogMod's own `GraphChecker` evaluates, the key items with their vanilla area, `LotLocations` (item lot row -> area) and the prison rule. `FogHost progression-model` exports it (`ds1tr-progression-model-1`, one JSON, deterministic bytes; it writes no game data, `HostVersion` stays `0.5.0` so generation does not change, only the project version moves to 0.6.0). The backend caches it beside the prepared world data and refuses a model of another annotation or FogMod commit (`adapters/progressionModel.ts`). The engine (`keys.js`) only **evaluates** it: the least fixed point of the usable edges, where a name in a condition is true when it is a visited area, or a key that lies in a visited area. This evaluation is the second implementation of the same rule, and it is **gated against FogMod's own checker**: `apps/backend/scripts/progressionDifferential.mjs` compares completable / unvisited areas / prison escape on 3000 assignments (random, partial and arbitrary-area), **3000 of 3000 agree** (AUTOMATED VERIFIED, real data). Every swept package is then asked again by FogMod's own item reader (`FogHost progression-check --game` reads the item lots **of the written package** into the graph, exactly as FogMod does before it randomizes) and FogMod's checker; the sweeps additionally compare the area FogMod reads for every key with the area the engine decided (two independent derivations of one fact) and refuse a world in which no key moved. This check uses the **vanilla world graph**: it proves the keys of the written lots are reachable in the base game's world. With the fog randomizer on, the world that is played is a different graph, and the guarantee there is FogMod's own: its generation solves the layout around the key lots it read, and a layout it cannot solve is a failed (retried, bounded) attempt, never a written world.

**Which keys move** (decided from the catalog and the model, never from a label; `validation.keyPlacement.notMovable` states the reason for every other key):

| Class | Keys | Reason |
|---|---|---|
| movable (12) | Basement Key, Undead Asylum F2 West Key, Annex Key, Watchtower Basement Key, Darkmoon Seance Ring, Key to New Londo Ruins, Cage Key, Peculiar Doll, Blighttown Key, Key to Depths, Covenant of Artorias, Key to the Seal | one certain catalog pickup (a map treasure, a boss reward or a gift lot), covered by the model, in the area the model says |
| movable with their own switch (5) | Lordvessel, the four Lord Souls | section 6 |
| not movable | Orange Charred Ring | its reward is awarded by a map event, not a lot the scan models (`no-catalog-pickup`) |
| not movable | Broken Pendant, Crest of Artorias, Residence Key | lots of NPC or enemy drops the model does not cover (`no-catalog-pickup`) |
| not movable | Crest Key | DLC (`no-catalog-pickup`) |
| not movable | the three Archive Tower keys | the lots are permanently fixed by the game data (`permanently-fixed`) |
| not movable | Cast Light, Purple Coward's Crystal | FogMod tags them `randomonly` (`random-only`) |

A key that stays is still a fixed part of the graph: the others are placed relative to it.

**Destinations.** A pickup can hold a key when it is *certain* (a lot with exactly one entry, so no probability and no "one of several"), is in the model (`LotLocations`), is in the world or boss pool (**never** a shop, an enemy drop or a gift: a drop can be missed, a shop needs souls, a gift needs a NPC who can die or leave), is not in the DLC, and is not a member of an armor set that Fashion Souls off keeps together. On the clean game that is **450 destinations** for the 12 movable keys (454 with the Lord Souls). The freed vanilla pickups of the moved keys are destinations too.

**Algorithm: assumed fill, bounded and deterministic.** Keys are taken in a seeded order; each key is placed only in a destination that is reachable when **every key that is not placed yet is assumed to be in hand** and the keys placed so far lie where they were put. The finished placement is checked once more as a whole (every area visited, the Seath prison escapable); a placement that fails that check is `KEY_PLACEMENT_INVALID` and nothing is written (this cannot happen by construction; it is the guard against a disagreement between the model and the code). A fill that finds no destination for a key tries again with the next stream `item-keys:<n>`, at most 32 times; then `KEY_PLACEMENT_UNSATISFIABLE` (retryable with a new combined seed, bounded by the pipeline). In the prototype, uniform random placement was valid 93 of 200 times and the assumed fill 200 of 200 on the first attempt; on the real catalog the fill needed one attempt in every world measured (section 9).

**What the keys displace.** Each key takes the place of one item of the pool; that item goes to the pickup the key left (`item-keys-fill`, DLC rule: restricted items never reach the DLC). The pool is a permutation: nothing is created or lost (checked on every sweep world: every pickup is written once, every item is taken once, every key is placed once).

**With the fog randomizer.** The fog randomizer reads the written lots and places its own gates around the key items where they lie (section 2.5); the shuffled keys are just different lots to it. The pipeline prepares the world data also when the fog randomizer is off. A fog layout that cannot be found for the shuffled keys is retried (bounded) and then reported as `FOG_KEY_CONFLICT`. Results of the sweeps: section 9.

**Typed outcomes.** `KEY_PLACEMENT_UNSATISFIABLE` and `KEY_PLACEMENT_FILL_IMPOSSIBLE` (retryable: another combined seed can solve them), `KEY_PLACEMENT_INVALID`, `KEY_PLACEMENT_MODEL_INVALID`, `KEY_PLACEMENT_MODEL_MISSING`, `KEY_PLACEMENT_NOTHING_TO_PLACE`, `KEY_PLACEMENT_MODEL_UNAVAILABLE` (the world data could not be prepared), `FOG_KEY_CONFLICT`, and `NO_REACHABLE_DESTINATION` (the reason of an unsatisfiable fill: the keys that had no reachable destination are named in the dead ends of the diagnostics, never a location). Each has a plain message in `userMessages.ts`; the exact reason stays in the log and the manifest.

## 6. Lordvessel and Lord Souls (built, behind their own switches)

The legacy's "Senile Gwynevere" and "Senile Primordial Serpents" are two switches **inside** Key placement. DS1TR: `items.randomizeLordvessel` and `items.randomizeLordSouls` (both off = neutral; they need `keyPlacement: shuffled`, otherwise the configuration is refused). Off, the key is a fixed part of the graph at its vanilla place; on, it joins the fill with the other keys.

* **Ownership.** The four Lord Souls are boss rewards (common event 970, section 2.1): ownership follows the **encounter flag**, never a body. Moving a Lord Soul changes **only the item lots**: the row of the soul (`2560`, `2580`, `2630`, `2640`, the three-lot chain rule applies) receives the displaced item and the soul lies in the pickup it was placed in. Event 970 and its initialisations stay byte for byte the clean game's (`SmokeVerify lots` asserts it on every sweep world). No event is rewritten.
* **Consumers.** The Kiln checks the four Lord Soul items and the Lordvessel **by possession** (section 2.4), so a moved item works wherever it lies, as long as the pickup is awarded for certain (which the destination rule guarantees) and the world is completable (which the model guarantees). The Lordvessel is awarded by the gift event with lot 1090, which sets flag 1090; with the switch on, the gift row holds **another item of the pool** (the displaced items are shuffled over the freed key pickups, so it is not necessarily the item the Lordvessel's new pickup held) and the Lordvessel lies in its new pickup; the gift event itself is untouched. Static check of the clean game's event 911 (the six instructions every gift runs: two flag conditions, the award of the lot, a flag set; no `3[04]` item-possession condition among them, dumped with the scratch tool `EvDump`): it holds **no possession condition**, so having the Lordvessel already does not make the event skip or lose its row, and the displaced item is not at risk by that route (STATICALLY VERIFIED; the usual rule of every gift applies: it is received only if the NPC is alive and reached).
* **Fog.** FogMod tracks the Lord Souls and the Lordvessel as key items (`requireLordSouls`, `lordvesselGates` are separate **fog** options that stay in the fog section of the UI; the item switches are in the item section under "Key items" and say so).
* **Cycles.** A Lord Soul placed behind the area it opens is excluded by the fill (only reachable destinations); the assumed fill cannot create a cycle, and the final check would reject one.
* **No broad event rewriting was needed**, so the hard stop did not apply. GAMEPLAY UNVERIFIED: nobody has played a world with the Lordvessel or a Lord Soul moved.

## 7. DLC (decision C, unchanged)

`output.includeDlc` is read by no engine and was a dead control; it stays a fixed `true`, the UI shows it on and disabled with that reason, and a preset with `false` is refused (docs/DECISIONS.md). What the DLC *does* get is the existing rule: DLC pickups (`m12_01_00_00`) never receive progression, Titanite, Ember or other restricted items, and **no key is ever placed in the DLC** (destinations exclude it; the DLC key `crest_key` is not movable). Excluding the DLC from the world is a separate feature (it needs the DLC maps, events and warps removed together, which neither source tool nor FogMod provides); nothing here changes it. DEFERRED.

## 8. Diagnostics, random streams, hint-sheet data

**Streams** (each from `createStream(seed, name, version)`, so no option shifts another): `soul-items-transposed`, `item-keys:<attempt>`, `item-keys-fill`. Key placement draws nothing from a stream another option uses; with the option off no key stream is created and the placements of every earlier option are byte for byte what they were (golden fixtures, section 9).

**Structured data** (diagnostics-ready, hint-sheet-ready; **no hint sheet is implemented**). Every placement keeps `rowId`, `sourceRowId`, `itemName`, `originalLocation` (area, map, lot), `randomizedLocation`; a key placement adds `progression: true` and `keyName`; a boss reward adds `bossReward` and `transposedFrom`. `validation.keyPlacement` (and `manifest.keyPlacement.summary`) holds counters only: `mode`, `movableKeys`, `fixedKeys`, `notMovable` (reason per key), `attempts`, `deadEnds`, `destinations`, `displacedItems`, `freedPickups`, `completable`, `prisonEscapable`, `annotationSha256`. The spoiler log gets a `KEY PLACEMENT` section (`<key>: <vanilla area> -> <new area> (item lot <row>)`) only when the option is on; every other spoiler keeps its exact text.

**Seed Codes and profiles.** The three item options are **extension fields**: complete in memory, written into a Seed Code only when they differ from their neutral value (`vanilla`, `false`, `false`), exactly like the placement extension keys. A code made before this phase decodes to the neutral values and re-encodes to the same text; a preset below version 14 imports with the neutral values (a version-13 document that states them is refused). The generation profile `ds1tr-gen-v1` is unchanged (the pinned FogHost `HostVersion` is 0.5.0). See `docs/SEED_FORMAT.md` and `docs/GENERATION_COMPATIBILITY.md`.

## 9. Verification (all on a clean copy of the game; the live installation was not touched; nothing here is GAMEPLAY VERIFIED)

**AUTOMATED VERIFIED, real data** (`placementMatrix.mjs keys-h`, real DsrDataTool and real FogHost; every world is generated through the product pipeline, then checked: item lots of the final package against the plan (`SmokeVerify lots`), the placements as a whole (every pickup written once, every item taken once, every key once), FogMod's own reader and checker over the lots of the written package, the engine's area of every key against FogMod's reading of the lots, "at least one key moved", event owners where enemies are on; the first two seeds of every case are regenerated twice and compared byte for byte):

| Set | Worlds | Result |
|---|---|---|
| keys, no Fog: items only 150, with enemies and bosses 60, all pools + Transposed 60, Lordvessel 80, Lord Souls 80, Lordvessel + Lord Souls 80 | 510 | 510 ok |
| keys with Fog: items 150, with Lordvessel + Lord Souls (Fog default options: Require Lord Souls on) 100 | 250 | 250 ok, 1 fog retry in total |
| full Triple (items + enemies + bosses + Fog) with keys, Lordvessel, Lord Souls and Transposed 120; with the default Fog options and keys only 100 | 220 | 220 ok |

Together **980 worlds, 0 failures**; every world needed **1 key placement attempt** (the fill never had to retry), **0 unsatisfiable**, **0 `FOG_KEY_CONFLICT`**, **0 cycles** (impossible by construction, and the final check passed in every world), **0 duplicated pickups or items, 0 missing items, 0 lot verifier problems** (about 537,000 lot targets checked), **0 non-deterministic reruns**, 0 event owner failures over about 58,900 checked placements; about 13,600 keys placed, 13,634 of them in a pickup other than their vanilla one. Lordvessel / Lord Soul configurations: 460 worlds (the minimum asked for was 200). Full Triple with progression options: 220.

*Recorded measurement artifact.* In the first run of the "all pools" case the key-area comparison reported 18 differences, all for Cast Light. Cast Light is tagged `randomonly` by FogMod: an ordinary pool item that key placement never moves, so the engine keeps it at its vanilla area while FogMod follows it where the item shuffle put it (it gates only a hidden DLC area). The comparison now skips `randomonly` keys; the case was run again on the corrected script: 60 of 60, 0 differences. No world was wrong.

**AUTOMATED VERIFIED, other:** the evaluation of the world graph against FogMod's `GraphChecker` on 3000 assignments, 3000 agree (`progressionDifferential.mjs`); the assumed fill on a synthetic world with 200 seeds, all completable, and its bounded failure with the typed reason; 461 backend tests (459 pass, 2 skipped by design, 0 fail), both typechecks clean; golden: **all 31 earlier worlds regenerate from their Seed Codes byte for byte** with the Part H engine and helpers, and the 5 new `key-placement` worlds were recorded and regenerate; the preset, Seed Code and canonical-settings tests prove an old code, preset or settings record keeps its text and means the neutral values.

**STATICALLY VERIFIED:** the four quick worlds of this phase (Transposed, key, Lordvessel, Lord Soul) are generated twice through the pipeline, are byte-identical and contain what they promise (`smokeTestCatalog.mjs verify`; `SmokeVerify`'s item-lot check on a goods item as well as on a weapon). The Lordvessel gift event (section 6).

## 10. What is not done

* Race Mode and Race Mode +: UNSUPPORTED. Difficulty classes (Fair / Unfair / Very Unfair): not built. Laundromat Mixup: DEFERRED.
* Keys in shops, from enemy drops or from gifts: deliberate (section 5). Keys that cannot move: section 5.
* Hint sheet: only the structured data exists (section 8); not implemented.
* The world played with the fog randomizer on is checked by FogMod's own generation, not by this model (section 5).
* **GAMEPLAY UNVERIFIED**: no world of this phase has been played. The four quick worlds exist to check, in a minute each, that a moved key, the Lordvessel and a Lord Soul can be picked up from the Asylum cell, and that Transposed hands out a weapon. Still to be played: using a moved key on its door, the Kiln with moved Lord Souls, Gwynevere's gift with the Lordvessel moved.
