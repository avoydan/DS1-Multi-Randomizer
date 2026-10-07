# Laundromat Mixup (NPC armor)

Status labels: **AUTOMATED VERIFIED**, **STATICALLY VERIFIED**, **GAMEPLAY UNVERIFIED**, **SAFE SUBSET**, **DEFERRED**, **UNSUPPORTED**.

Source of the behavior: the pinned legacy Item Randomizer (`HotPocketRemix/DarkSoulsItemRandomizer`, `f7cabbd`, no license: behavior only, nothing copied). Files read: `randomizer_options.py`, `randomizer_gui.py`, `chr_setup.py` (`randomize_chr_armor`, the *structure* of the data it uses), `randomize_item_table.py` (`build_table`), the README.

## 1. What the legacy does (audit)

**Wording and value.** GUI check box "Laundromat Mixup"; internal `randomize_npc_armor` (bool), default **off**. GUI text: "NPCs wear randomly chosen armor instead of their normal sets. If Fashion Souls is on, NPCs will also mix-and-match their armor." README: "most human NPCs will wear random armor instead of their usual sets. If Fashion Souls is not active, NPCs will wear randomly chosen full sets of armor. If Fashion Souls is active, NPCs will wear independently chosen random pieces of armor."

**What it touches.** It rewrites the four armor slots (head, chest, hands, legs) of **CharaInitParam rows of NPC characters** (`chr_init.armor_head/chest/hand/leg`), nothing else. The same function also handles the *starting classes*, but those entries are processed whenever Fashion Souls logic applies and are **not** part of Laundromat (`if links.is_npc and not rand_options.randomize_npc_armor: continue` only skips NPC entries; the class entries are always randomized). Laundromat is therefore **NPCs only**: no item lots, no shops, no weapons, no shields, no quantities, no key items, no boss rewards, no starting equipment of the player, no item pool, no spoiler knowledge, no progression constraint, no DLC rule.

**How.** For every NPC character in the legacy list, a random *armor set* is chosen from the list of sets flagged usable on NPCs (Fashion Souls off: one set for all four slots; on: an independent set per slot, hats from a hat-capable subset). The piece id is the set's base id plus the *upgrade level the NPC already had* (an offset table per set). A hat is dropped with probability 5% for characters that normally wear one, and with probability 60% for those that do not. All draws come from the one shared random source. **Why "Laundromat"**: the NPCs' clothes are put through a washing machine and come out mixed up.

**Quirks.** The usable-on-NPC flag of each set is a hand judgement (not derivable from data); the legacy writes the new ids into the NPC rows without checking that the NPC's model shows the armor correctly; the defense and weight class of the NPC change with the armor ("NPCs have their familiar look, weight class and defense stats" is the *off* text).

**The twenty questions.** 1-3 wording / value / default: "Laundromat Mixup", bool, off. 4-5 pools: NPC armor pieces / NPC CharaInitParam rows. 6 selection or legality: neither (appearance only). 7 progression: none. 8-11 shops / drops / gifts / boss rewards: none. 12 starting items: none (the starting *classes* are separate). 13-15 keys / Lordvessel / boss souls: none. 16 DLC: none. 17 spoiler knowledge: none. 18 own RNG stream: no (shared). 19 quirks: above. 20 reproducible safely: **SAFE SUBSET** (below).

## 2. What DS1TR builds

**Decision: SAFE SUBSET.** The legacy list of NPC characters and its flagged sets are hand-made data under no license and cannot be copied; they cannot be derived either (which armor looks right on an NPC model is a judgement). DS1TR instead derives everything from the **clean game's own data**:

* *NPC characters* = the CharaInitParam rows with ids from 6000 up (the NPC characters of the world). Measured on the clean game: **205 rows, 60 distinct characters** (grouped by `npcPlayerFaceGenId`), 202 with armor, **752 armor pieces**, and **every piece is `base + level`** where `base` (a multiple of 1000) exists in `EquipParamProtector` (levels 0 to 10; the game has no rows for the levelled ids). Rows that use the hollowed ids (`2xxxxxx`: 8 rows, 4 characters) are **left alone**, with every other row of the same character, so a character never ends up half changed.
* *candidate pieces* = **only armor pieces some vanilla NPC already wears in that slot** (head, chest, hands, legs separately): every written id is an id the game already loads on an NPC model, at the level it was worn at. DS1TR never computes an id, so an invalid id or a level the game cannot load cannot be produced.
* *choice*: Fashion Souls off: one donor row's four pieces (a full set); on: each slot independently. A slot a character normally leaves empty (`-1`) stays empty. The hat-drop probabilities of the legacy are not reproduced (no slot is emptied).
* *stream*: `item-laundromat`; nothing is drawn when the option is off.

What is **not** included: the **starting classes** (not part of Laundromat), weapons, shields, rings, anything of the item pool, boss/NPC rewards, the player. The unique equipment the audit asked to protect (covenant rewards, boss soul weapons, Orange Charred Ring, Artorias items, DLC access items, keys, Lordvessel, Lord Souls) are **items in lots**, which Laundromat never touches; the armor of an NPC is cosmetic and is not an item the player can obtain from the NPC row.

Documented deviations: no hat-drop chance, no legacy "usable on NPC" flags (only armor NPCs already wear), hollowed variants skipped, DS1TR's own draw order.

The implementation and its measurements are recorded below as they are built.
