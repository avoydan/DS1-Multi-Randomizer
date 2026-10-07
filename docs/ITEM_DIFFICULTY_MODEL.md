# Item difficulty: Fair, Unfair, Very Unfair

Status labels: **AUTOMATED VERIFIED**, **STATICALLY VERIFIED**, **GAMEPLAY UNVERIFIED**, **SAFE SUBSET**, **UNSUPPORTED**, **DEFERRED**. This is the ITEM randomizer's difficulty. It has nothing to do with the enemy placement difficulty mode (`docs/ENEMY_PLACEMENT_MODEL.md`).

Source of the behavior: the pinned legacy Item Randomizer (`HotPocketRemix/DarkSoulsItemRandomizer`, `f7cabbd`, no license: read for **behavior only**). Files read: `randomizer_options.py`, `randomizer_gui.py`, `randomize_item_table.py` (`make_difficulty_order_dict`, `create_random_placement_list`, `place_non_key_fixed_items`, `place_key_items`), and the *shape* of `items_setup.py` / `locations_setup.py`. **Nothing from them is copied.**

## 1. What the legacy does (audit)

**Wording and values.** GUI "Difficulty", three radio buttons: `Fair` (`RandOptDifficulty.EASY` 0, the **legacy default**), `Unfair` (`MEDIUM` 1), `Very Unfair` (`HARD` 2). The GUI text: Fair - "Perfectly fair. Items have an equal chance to be placed anywhere."; Unfair - "Slightly biased. Desirable items are not placed in plain view."; Very Unfair - "Heavily biased. Desirable items are hidden, and are rarely in early areas." The README: "Determines how much the Randomizer is biased against you. The more unfair it is, the more it attempts to hide desirable items (good weapons, lighter armor, keys) in harder-to-reach or less well-known locations."

**What is scored.** Every item (an item lot part) has an **item class**: `EASY`, `MEDIUM`, `HARD` (plus `NPC_*` and `SALABLE_*` variants for gift and shop stock) and the special classes `KEY` (ranked **HARD**), `SMALL_SOUL` (ranked **EASY**), `BIG_SOUL` and `BOSS_SOUL` (both ranked **HARD**). Every location has a **location class** `EASY`, `MEDIUM`, `HARD` (plus `NPC_*`, `SHOP_*`). The classes are hand-assigned per lot in the legacy tables (`items_setup.py`, 1709 entries; `locations_setup.py`, 1430 entries). The location class reflects how reachable or well known the pickup is, the item class how desirable the item is.

**How the mode acts** (`make_difficulty_order_dict(difficulty, item_class)`): it returns an ordered list of **groups of location classes**. For one item the candidate locations are taken group by group (group 0 first); inside a group they are shuffled; the legacy then **stable-sorts the whole list by how full a location already is** (empty locations first) and takes the first location. So the order of priority is: *emptiness, then the difficulty group, then chance.* The tables:

| Mode | EASY item | MEDIUM item | HARD item (also keys, big and boss souls) |
|---|---|---|---|
| Fair | one group: all locations | one group: all | one group: all |
| Unfair | EASY locations, then MEDIUM+HARD | MEDIUM locations, then EASY+HARD | MEDIUM+HARD locations, then EASY |
| Very Unfair | EASY, then MEDIUM, then HARD | MEDIUM, then EASY+HARD | HARD, then MEDIUM, then EASY |

(the NPC and shop variants follow the same pattern inside their own location classes). So Fair is the **uniform** placement; Unfair keeps hard-class items out of EASY locations while putting easy-class items into easy ones; Very Unfair is a strict preference ladder.

**Ordering with keys.** The legacy places, in this order: ignored items, upgrade materials, **key items (difficulty-ordered as HARD)**, starting equipment, then every other item (largest items first). The validity check of the keys (`check_key_locations_are_valid`) takes no difficulty input, so **difficulty can never make a world unwinnable**: it changes only *which* of the allowed locations are preferred. Under Race Mode + the GUI forces Fair.

**Other facts.** The boss souls become boss items (Transposed) *before* placement but the item keeps its class (`BOSS_SOUL`, so HARD). Soul "Replaced" turns small/big souls into consumables before placement (the item keeps its soul class). Starting equipment is placed before and separately, so difficulty never moves it. Fashion Souls only changes which items follow one another (armor sets), not the classes. The legacy has no RNG stream of its own for difficulty (one shared source). **Quirks:** the legacy ties difficulty to hand-rated classes; the NPC/shop classes make shops behave differently from the world pool; the emptiness-first ordering means the bias is weaker for items placed late.

**The twenty questions.**

| # | Question | Answer |
|---|---|---|
| 1-3 | Wording, value, default | Fair / Unfair / Very Unfair; `0` / `1` / `2`; default Fair |
| 4-5 | Item pool, destination pool | every pooled item; every pooled location (class-matched for NPC/shop) |
| 6 | Selection or legality | neither: it only **orders** the candidate locations of each item |
| 7 | Progression constraints | none (the key validity check is independent of difficulty) |
| 8-11 | Shops / drops / gifts / boss rewards | each has its own location classes; boss drops are HARD locations |
| 12 | Starting items | not touched |
| 13-14 | Key placement, Lordvessel/Lord Souls | keys are ranked HARD and take the same ordering; validity unchanged |
| 15 | Boss souls | HARD class; a transposed weapon keeps HARD |
| 16 | DLC | no special rule in the legacy |
| 17 | Spoiler knowledge | no |
| 18 | Own RNG stream | no |
| 19 | Quirks | hand-rated classes; emptiness-first ordering |
| 20 | Reproducible safely in DS1TR | **SAFE SUBSET**: the policy is reproduced exactly; the class tables cannot be copied (below) |

## 2. What DS1TR builds (SAFE SUBSET)

**The policy is exact.** `ItemDifficultyPolicy` (`difficulty.js`, repository-authored) is the table above: for a mode and an item tier it returns the ordered groups of location tiers. Fair has no groups: **Fair is the neutral value and means exactly the current behavior** (uniform permutation, zero RNG, byte-identical output; proven by a test and by the golden fixtures). Unfair and Very Unfair use the policy.

**The tier tables cannot be copied, so DS1TR derives its own.** The per-lot ratings of the legacy are hand-assigned data of an unlicensed project; AGENTS.md forbids copying them, and the game's data has no field that reproduces them (measured in scratch: price fields are flat, and the rarity of an item explains 28% of the legacy item classes where chance gives 33%). DS1TR therefore classifies from **data the game itself provides**, documented rule by rule; this is a **documented deviation**, never "exact legacy difficulty":

*Item tiers* (source side): a progression key (FogMod's annotation, and any pooled lot flagged protected) is **HARD**; boss souls (goods 700-711), **big souls** (407-409) and every transposed boss weapon are **HARD**; small souls (400-406) are **EASY**. Equipment is ranked **by its own parameters**: weapons by the sum of their base attack values, armor by defense per weight ("lighter armor" is desirable), each split into terciles over the pool; rings, spells and other unique goods (an item that exists once in the clean game's pools) are **MEDIUM or HARD** by rarity; repeated consumables are **EASY**. The exact rule lives in `difficulty.js` and is tested.

*Location tiers* (target side): boss rewards **HARD** (the legacy rates them so), world pickups by **depth in the world graph** (the distance from the Undead Asylum over FogMod's graph, terciles over the pool), shops **EASY** (plain view), NPC gifts and enemy drops **MEDIUM**.

**Agreement with the legacy** (measured in scratch against the legacy tables on the lots both know; the tables are never committed): reported in section 5.

**Ordering with the other options.** Key placement first (the keys take the best-tier destination among the **reachable** ones, uniformly, exactly like the legacy's HARD-ranked keys); then the remaining pool is assigned with the policy, and only then the existing repairs run (DLC, fog tracking, armor sets). Transposed weapons keep the HARD tier of the soul. Starting equipment is a separate pool and is never touched. Fashion Souls does not change the tiers. Difficulty draws only from the stream `item-difficulty`; when it is Fair nothing is drawn and no stream exists.

The implementation and its measurements are recorded in sections 3 to 6 below as they are built.

## 3. What is built

* **Contracts and UI.** `items.itemDifficulty`: `fair` (neutral), `unfair`, `very-unfair`; the select "Difficulty" in the "Item difficulty" group of the Items card (the legacy order and wording). Race Mode + forces and disables Fair, as the legacy GUI does.
* **Engine** (`difficulty.js`, `generator.js`, patch 0024). `buildTierModel` derives the tiers (section 2) once, before anything moves, from the pool and the progression model (the vanilla variant: the depth of a pickup is the number of areas between the start and its area, every edge counted as open). The key planner prefers, for every key, the best tier that has a *reachable* destination (`preferByTier`, a key is HARD) and draws uniformly inside it; the rest of the pool is assigned by `difficultyShuffle`: the sources in a seeded order (stream `item-difficulty:<attempt>`, at most 32 attempts), each taking a free target from the first group of its policy that has one, with the hard constraints of the plain permutation (no source on its own target, no DLC-restricted item in the DLC). The existing repairs (keeping consumable souls out of finite shops, the fog tracked-item rules) run afterwards, exactly as for the plain permutation. Armor sets that move as sets (Fashion Souls off) and the bulk shop consumables keep their own streams and are not tiered.
* **Ordering with the other options (locked by `combinedItemDifficulty.test.mjs`).** Key placement first (with the difficulty preference), then the pool. Transposed: a transposed boss weapon is HARD (it keeps the tier of the soul). Starting items: a separate pool, never touched. Fashion Souls: no effect on the tiers. A neutral difficulty draws nothing and reports nothing.
* **Needs the world data** (the depth of a pickup comes from the model), so the pipeline prepares it for any non-Fair difficulty, with or without the fog randomizer.
* **Typed outcome.** `DIFFICULTY_POLICY_UNSATISFIED` (an assignment that cannot be made; retryable with another combined seed; never seen in the sweeps). The key validity check takes no difficulty input: **no mode can make a world unwinnable.**

## 4. Measurements (AUTOMATED VERIFIED; engine level, 200 worlds per mode, all pools, Transposed; the shares are measured on the finished layout after every repair)

Where the items of each tier end, by tier of location (EASY / MEDIUM / HARD, % of the items of that tier):

| Mode | EASY items | MEDIUM items | HARD items |
|---|---|---|---|
| Fair (baseline) | 40.6 / 45.9 / 13.5 | 38.9 / 46.9 / 14.1 | 38.2 / 47.7 / 14.1 |
| Unfair | 81.6 / 8.4 / 10.0 | 4.9 / 94.2 / 0.9 | 3.9 / 63.2 / 32.8 |
| Very Unfair | 81.6 / 18.4 / 0.0 | 4.9 / 95.1 / 0.0 | 3.9 / 45.9 / 50.1 |

Fair is uniform (each tier of item lands in a tier of location in proportion to the size of the tiers: 40 / 46 / 14%). Under Unfair the HARD items in EASY locations fall from **38.2% to 3.9%** and in HARD locations rise from **14.1% to 32.8%**; under Very Unfair they reach **50.1%** and nothing EASY lies in a HARD location (**13.5% to 0.0%**). So Fair, Unfair and Very Unfair differ in the intended direction at every step. The ordering is the same with Race Mode, with the Lord switches and with the fog randomizer's tracked-item rules (Very Unfair: HARD in HARD 49.1% with Race Mode, 46.5% with Fog rules). The location tiers hold few HARD places (14% of the pool), which is why even Very Unfair cannot put every HARD item in a HARD place; the rest go to the next group of the policy.

**Agreement with the legacy tables** (measured in scratch against the legacy's per-lot ratings on the lots both know; the tables are not in the repository): item tier **40.3%** of 335 lots, location tier **39.4%** of 373 lots; chance is 33%. DS1TR's tiers are a data-derived model of the same idea, not the legacy ratings.

## 5. What is still needed

GAMEPLAY UNVERIFIED (nobody has played a Very Unfair world to see whether the early game feels poorer). Not built: the legacy's separate NPC and shop classes (DS1TR's pool permutes the pools together and gives gifts and shops a single tier), the emptiness-first ordering of the legacy (every DS1TR target holds one item), the hand-rated classes.

## 6. Real data tool and FogHost sweeps (AUTOMATED VERIFIED)

The same `item-modes-h` run (645 worlds, 0 failures): Unfair 40 and Very Unfair 40 with the item pool, Unfair with all pools and the keys 30, Very Unfair with the keys, the Lordvessel and the Lord Souls 30, Very Unfair with Fog 30, Race with each difficulty 90, and the full Triple with Unfair / Very Unfair 50; each world passed the lot verifier (the planned rows are in the package, no other lot changed, event 970 is the clean game's) and the tier counters of the finished layout were read from the written plan. **Guard:** the item "Cast Light" opens the hidden Oolacile Township; FogMod found it in that area in one Very Unfair world (a HARD unique spell placed in the deepest pickup). Every mode of Part I now keeps a "random only" item out of the area it opens (`guardRandomOnlyItems`, one swap with the next free place of a seeded order, stream `item-guard` created only when needed); Fair and Shuffled keep their worlds byte for byte.
