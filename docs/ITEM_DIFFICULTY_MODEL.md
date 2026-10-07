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
