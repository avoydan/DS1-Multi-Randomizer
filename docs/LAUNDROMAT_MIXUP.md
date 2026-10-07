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

## 3. What is built

* **Contracts and UI.** `items.laundromat` (neutral off); the toggle "Laundromat Mixup" in the Legacy options of the Items card. It counts as an item feature on its own (a run with only the Laundromat Mixup is valid; it needs no item pool and no world data).
* **Engine** (`laundromat.js`, patch 0024). `planLaundromat` takes the scan's `npcArmor` (the CharaInitParam rows from 6000 with the four armor ids), groups the rows by `npcPlayerFaceGenId` into characters, drops the characters that have a hollowed piece or no armor, and gives every remaining character one outfit from the pieces NPCs already wear (stream `item-laundromat`, characters in face order): Fashion Souls off, the four pieces of one donor row that wears all four; on, each slot independently from the pool of that slot. A row keeps an empty slot empty; only rows whose armor changes are planned. `placements.laundromat` exists only with the option.
* **Data tool** (`patch-enemies`, patch 0024). The planned rows are checked against the scan (a changed game is refused) and written into CharaInitParam (the four cells `equip_Helm`, `equip_Armer`, `equip_Gaunt`, `equip_Leg`); nothing else is touched. A catalog that predates the scan section is refused with a plain message (`LAUNDROMAT_CATALOG_OUTDATED`).
* **Verifier** `SmokeVerify laundromat`: every planned row wears the planned armor, every planned piece is worn by some NPC in the clean game, no empty slot was filled, no hollowed piece is involved, and **no other cell of any NPC row differs from the clean game**.
* **Fog.** The CharaInitParam rows survive the merge into the Fog package (the verifier runs on the final Fog tree in the sweeps).

## 4. Measurements

On the clean game the scan finds 205 NPC rows (60 characters), 202 with armor, 752 non-hollow pieces whose base id exists in `EquipParamProtector`; the eligible characters leave about 157 planned rows per world (**31,398 rows in 200 engine-level worlds**, 0 audit problems: every piece worn by an NPC in the clean game, no hollowed piece, no empty slot filled). Real data tool and FogHost sweeps: `docs/IMPLEMENTATION_STATUS.md` (Part I).

## 5. What is still needed

GAMEPLAY UNVERIFIED: that a plain NPC model shows another NPC's armor correctly (the pieces are loaded on NPC models in the clean game, but hat and hair interactions are a visual judgement) and that the armor weight of an NPC does not change a fight in a way a player notices. There is no quick Asylum target (no dressed NPC stands within reach of the start): the test world is an extended gameplay test.

## 6. Real data tool and FogHost sweeps (AUTOMATED VERIFIED)

`item-modes-h`: the Laundromat Mixup with the item pool 30, with Fashion Souls off 20, alone (no item pool at all) 20, with the fog randomizer 20 and in the three full-Triple cases 75: **185 worlds, 0 failures; 25,757 planned CharaInitParam rows read back from the written packages (the final Fog tree included) and compared with the clean game cell by cell: 0 problems** (every planned piece worn by some NPC in the clean game, no empty slot filled, no hollowed row touched, no other cell of any NPC row changed). The packages are deterministic (reruns identical) and the verifier is the same code for the Fog and the combined-only package.
