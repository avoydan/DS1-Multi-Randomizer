# Race Mode and Race Mode + (key placement)

Status labels: **AUTOMATED VERIFIED**, **STATICALLY VERIFIED**, **GAMEPLAY VERIFIED / UNVERIFIED**, **SAFE SUBSET**, **UNSUPPORTED**, **DEFERRED**. Nothing here is GAMEPLAY VERIFIED.

Source of the behavior: the pinned legacy Item Randomizer (`HotPocketRemix/DarkSoulsItemRandomizer`, commit `f7cabbd`, no license: read for **behavior only**, nothing is copied; the files read are `randomizer_options.py`, `randomizer_gui.py`, `randomize_item_table.py`, `key_items_setup.py` and the README). Companion documents: `docs/ITEM_PROGRESSION_MODEL.md` (the world model and the assumed fill that Race reuses), `docs/ITEM_DIFFICULTY_MODEL.md`.

## 1. What the legacy does (audit)

**Wording and values.** The GUI "Key Placement" has four radio values: `Not Shuffled` (`LEAVE_ALONE` 0), `Shuffled` (`RANDOMIZE` 1, the **legacy default**), `Race Mode` (`RACE_MODE` 2) and `Race Mode +` (`SPEEDRUN_MODE` 3). The serialized value is that integer. The legacy GUI text: Race Mode - "Key items are shuffled but can be placed only in certain locations. Good for races / short runs. Average run is ~4hrs"; Race Mode + - "... May require skips and glitches to complete. SOFTLOCKING IS POSSIBLE."

**What Race Mode changes (and only that).** In `place_key_items` the trial draws, for every key in name order, a location from the same candidate list as Shuffled (difficulty-ordered, key restrictions, not transient, not used by another key of the trial). Race adds **one filter**: the location must carry `is_race_key_loc`. The validity check (`check_key_locations_are_valid`) is **identical to Shuffled** (including the Duke's Prison capture check and the Sen's Fortress "both bells" rule). So Race is *Shuffled with a restricted set of locations*; it changes no item selection, no progression rule, no shop/gift/drop/boss-reward rule other than membership of the set, no starting-item rule, no DLC rule, and consumes no RNG stream of its own (the legacy has one shared random source; the trial just draws from a shorter list).

**The Race location set** (documented in the README as "boss drops, in chests, dropped by Black Knights, and in locations where key items (including embers) are normally", 102 entries / 103 lot ids in the table): the boss reward rows (including the four DLC bosses), every treasure chest, the vanilla pickups of the keys and embers, the drops of the seven Black Knights, the Ingward gift row, the Andre and Undead Merchant drop/shop rows, the three Andre statues, and the Duke's Archives special Golem drop.

**Which keys are placed.** *Every name of `KEY_NAMES`* (39 names) that is not pinned: the 25 progression keys **and** the embers (Large Ember, Dark Ember, Divine Ember, Large Divine Ember, Enchanted Ember, Large Flame Ember, Chaos Flame Ember, Very Large Ember, Large Magic Ember, Crystal Ember), Rite of Kindling, Sewer Chamber Key, Mystery Key and Cast Light. In Shuffled the same 39 are placed anywhere; in Race they must all fit the 103 race lot ids.

**Race Mode +.** Same location set and same filter as Race; the delta is only in the validity model and the key list:
1. `get_connections` adds **skip edges**: Capra skip (Undead Burg -> Depths), Lower Burg skip, Master Key routes (Watchtower basement, Undead Burg residence, Darkroot Basin, Valley of Drakes / New Londo), Sen's Gate skip (Parish -> Sen's Fortress), Annex jump (Painted World -> Annex), "Gwynevere can be killed" (Anor Londo -> Darkmoon Tomb), Seal skip and Four Kings out of bounds, Duke skip (the Prison reaches the Archives), re-added Sif ladder / Valley of Drakes elevator / Basin routes, the Purple Coward's Crystal wrong warp (Darkroot -> Oolacile Township) and (PTDE only) the Firesage drop.
2. The **prison check is skipped** (the Duke skip makes "escape the Prison" unnecessary) and the "Sen's Fortress opens after the Parish and Quelaag's Domain" logic rule is not applied.
3. `purple_cowards_crystal` is added to the keys that are placed (in Shuffled and Race it is an ordinary pool item).
4. The Kiln rule: with the crystal in hand the four Lord Souls are **not** required (a wrong-warp quirk, kept by the legacy).
5. The GUI **forces Difficulty = Fair** while Race Mode + is selected (the controls are disabled); `randomize_item_table.py` itself does not enforce it.
The README states it plainly: Race+ worlds "may require skips and glitches to complete; softlocking is possible" for a player who does not know them.

**The twenty questions.**

| # | Question | Race Mode | Race Mode + |
|---|---|---|---|
| 1-3 | Wording, serialized value, default | "Race Mode", `2`; default is Shuffled (`1`) | "Race Mode +", `3` |
| 4 | Candidate item pool | the 39 `KEY_NAMES` | the 39 + Purple Coward's Crystal |
| 5 | Candidate destination pool | race locations (103 lot ids) | same |
| 6 | Item selection or location legality | location legality only | location legality + validity model |
| 7 | Progression constraints | same as Shuffled | skip edges, no prison check, no Sen's rule, Kiln rule with the crystal |
| 8-11 | Shops / drops / gifts / boss rewards | membership of the set only: boss drops yes, Black Knight drops yes, Ingward/Andre/Merchant yes, other shops no | same |
| 12 | Starting items | no effect | no effect |
| 13-14 | Keys / Lordvessel and Lord Souls | the Lordvessel and the Lord Souls follow their own switches exactly as in Shuffled | same; the crystal is a key |
| 15 | Boss souls | no effect on souls; boss reward rows are race locations | same |
| 16 | DLC | the four DLC boss drops are race locations | same |
| 17 | Depends on spoiler knowledge | the player is told the list of locations (README) | the list of locations and the list of skips |
| 18 | Own RNG stream | none (the shared source) | none |
| 19 | Known quirks | key may land in a shop with a price (Andre/Merchant rows); Ingward's row depends on dialogue | crystal as Kiln bypass; softlock possible without the skips; difficulty forced only in the GUI |
| 20 | Reproducible safely in DS1TR | **SAFE SUBSET** (below) | **SAFE SUBSET** (below) |

## 2. What DS1TR builds (SAFE SUBSET, with a reason for every difference)

**Serialized values** (extension field `items.keyPlacement`, neutral `vanilla`): `race` and `race-plus` join `vanilla` and `shuffled`. The legacy order Not Shuffled / Shuffled / Race Mode / Race Mode + is kept in the UI; nothing is renamed, no old value changes meaning (`shuffled` stays `shuffled`).

**Strategies, not conditionals.** `keys.js` gets one mode table, `KEY_PLACEMENT_STRATEGIES`: `vanilla` (nothing), `shuffled` (all eligible destinations, vanilla graph), `race` (destinations restricted to the race set, vanilla graph), `race-plus` (race set, **skip-extended graph**, crystal as a key). The fill, the displaced-item pairing, the typed failures, the final check and the streams are shared; a strategy supplies `{ destinationFilter, graphVariant, extraKeys }`. Lordvessel / Lord Souls switches work in all three shuffled-family modes (the legacy disables them only for Not Shuffled).

**The race destination set** is *derived from the game data*, never copied from the legacy table. A destination must first satisfy the existing rule (a certain one-entry world or boss lot, covered by the model, not in the DLC); inside that, it is a race destination when it is
* a **boss reward** row (non-DLC; the DLC boss drops are excluded because the DLC is out of every placement rule),
* a **treasure chest**: a treasure event whose object model is the chest model (`o0110`), read from the map files by the data tool scan (the 58 chest rows the legacy README labels "Chest" are exactly the lots of `o0110` treasure events; the data tool derives them, nothing is listed in the repository),
* the **vanilla pickup of a key or an ember**: the lot a progression key, an ember, the Rite of Kindling, the Sewer Chamber Key, the Mystery Key or Cast Light occupies in the clean game (the scan knows them by item id).
Excluded on purpose, each with a reason: **Black Knight drops** (an enemy drop belongs to whatever body stands in the slot, so enemy placement can move or remove it: boss/body ownership rule), the **Ingward gift, the Andre and Undead Merchant drop/shop rows and the Andre statue rows** (gift/shop/drop destinations are never keys in DS1TR), the **Duke's Archives special Golem drop** (an enemy drop), and the **DLC boss drops**. The number of excluded legacy locations and the share the set keeps are measured in section 5.

**Which keys are placed.** DS1TR's keys are FogMod's annotation keys (movable subset, section 5 of `docs/ITEM_PROGRESSION_MODEL.md`). The embers, the Rite of Kindling, the Sewer Chamber Key, the Mystery Key and Cast Light are **ordinary pool items** in DS1TR; they have no graph conditions. In Race they get a **location restriction only** (they must end up in a race destination), which is what the legacy does to them; this is implemented as a *second assumed-fill pass without graph conditions* (each such item takes a random free race destination from the `item-race` stream). In Shuffled and Vanilla they are untouched (byte-identical).

**Race Mode + (SAFE SUBSET).** FogMod's own graph has a "hard" (glitched logic) edge set. It covers part of the legacy skip list: Capra skip, Lower Burg skip, Annex jump, the Seal skip and Four Kings out of bounds, the Purple Coward's Crystal wrong warp and Kiln-with-the-crystal; the Sen's Gate skip, the Master Key routes, the Gwynevere kill and the Duke skip are **not** in FogMod's graph. DS1TR builds the Race+ model from the *same* graph (one model, one more option), keeping only the hard edges that correspond to a skip of the legacy list (`depths -> depths_gaping`, `totg -> totg_lower` and `dukes_library -> dukes_library2` are hard edges the legacy list does not name and are **not** used). The four skips FogMod does not model are **not assumed**, so every Race+ world DS1TR makes is valid with the skips of the list it actually uses; a world that needs the unmodelled skips is never produced (a subset of the legacy's worlds). The prison check **stays** (stricter than the legacy). Purple Coward's Crystal is placed as a key; the Kiln-bypass quirk is kept because FogMod's graph has the same edge (the crystal opens the Kiln from the DLC areas). Difficulty is forced to Fair under Race+ in validation and in the UI, as in the legacy GUI.

**With the fog randomizer on** the skips mean nothing (FogMod rebuilds the topology around the key lots it reads): Race+ with Fog is the Race location set plus the crystal as a key, and FogMod's own generation is the authority for the played world. The documents and the UI say it.

**Determinism.** Same Seed Code => same world; the race pass draws only from `item-race` (and the shared fill from `item-keys:<n>`); no file order, no time, no machine state.

**Typed outcomes** (the existing ones plus): `RACE_LOCATION_INVALID` (a key outside the race set; an internal guard, never produced by the fill), `RACE_PLUS_LOCATION_INVALID`. `KEY_PLACEMENT_UNSATISFIABLE` also covers "the race set cannot hold the keys" (retryable, bounded).

The implementation and its measurements are recorded in sections 3 to 6 below as they are built.
