# Audit: "Guarantee early weapon" and "Balanced early loot"

Date: 2026-10-08 (desktop 0.35.0, backend 0.45.4, contracts 0.32.0). Question: are `items.guaranteeEarlyWeapon` and `items.balancedEarlyLoot` dead legacy configuration, or real functionality that DS1TR failed to port?

**Result: neither option ever worked in real game data, in any of the three lineages DS1TR draws from. Nothing was lost in the port, so nothing was restored.**

| Option | Legacy Item Randomizer `f7cabbd` | Legacy Enemy Randomizer `83f008d` | Upstream engine `f120efe` (vendored) | DS1TR before the audit | Classification |
|---|---|---|---|---|---|
| Guarantee early weapon (`guaranteeEarlyWeapon`) | no such option | no such option | flag + checkbox; implemented only for the placeholder data set | accepted, stored, mapped, no effect | **LEGACY DEAD / NO-OP** |
| Balanced early loot (`balancedEarlyLoot`) | no such option | no such option | flag + checkbox; implemented only for the placeholder data set | accepted, stored, mapped, no effect | **LEGACY DEAD / NO-OP** |

The sources are not vendored here (docs/FEATURE_PARITY.md, "Sources audited"); they were read from GitHub into a scratch directory, and only the revisions named below were used.

## 1. What the original randomizers have

**Item Randomizer** (`HotPocketRemix/DarkSoulsItemRandomizer`, `f7cabbd`). The complete option set is the one the GUI builds (`randomizer_gui.py`, the `Radiobutton` / `Checkbutton` widgets, lines 153-267) and the `RandomizerOptions` constructor (`randomizer_options.py:83-92`: `difficulty, fashion_souls, key_placement, use_lordvessel, use_lord_souls, soul_items_diff, start_items_diff, game_version, randomize_npc_armor`). That is: Difficulty (3), Key Difficulty (4), Soul Items (3), Starting Items (3), Fashion Souls, Laundromat Mixup, Senile Gwynevere (Lordvessel), Senile Primordial Serpents (Lord Souls), and the game version. A search of every `.py` file and of both READMEs for `early`, `balanced`, `guarantee`, `ensure`, `at least` finds only the Difficulty text ("Desirable items are hidden, and are rarely in early areas", `randomizer_gui.py:32`). The item placement code (`randomize_item_table.py`: `build_table`, `place_key_items`, `place_non_key_fixed_items`, `place_starting_equipment`) contains no step that reserves a weapon or a set of categories for early locations. What the legacy tool does for the same player concerns is different and DS1TR has it: **Difficulty** (biases desirable items away from early areas) is `items.itemDifficulty`, and **Starting Items** (class-usable starting weapons and shields) is `items.startingItems` with Starting equipment.

**Enemy Randomizer** (`everKalle/Dark-Souls-Enemy-Randomizer`, `83f008d`). Its options are enemy and boss options only (`randomizer.py`, `BuildConfigString` at line 1507, `RestoreDefaultSettings` at line 1500); no option concerns items.

## 2. Where the two flags come from

They come from the **vendored combined engine** (`kellernz1/ds-randomizer`, pin `f120efe`, `engines/combined/vendor/UPSTREAM_PIN.md`), not from the Python tools.

* Upstream declares both (`src/core/config.js:17-18`, default **true**, and `:37-38` in the key list; `src/core/share.js:19-20`) and exposes both as checkboxes ("Guarantee an early weapon", "Balanced early loot", `public/index.html:76-77`).
* Upstream implements them in exactly one function, `randomizeItems(config)` (upstream `src/core/generator.js:2176-2184`, vendored `engines/combined/vendor/src/core/generator.js:3359-3453`, the flags at lines 3424-3434). That function works on the **placeholder data set** `src/data/prototype-data.js` (eight toy items `items[]`, eight toy locations `itemLocations[]` with a `sphere` of 0, 1 or 2) and is only called when no game data was extracted (`generate()`: `items: extractedData ? itemLotPlan.items : randomizeItems(config)`, vendored line 3599; upstream line 2256).
* With real game data `generate()` calls `randomizeExtractedItemLots` (upstream line 1509, vendored line 2059). That function never reads either flag, and has no notion of `sphere`. Upstream's own tests do not test the flags (`test/rng.test.js` tests `guaranteedEnemyDrops` only).

So in upstream's own product the two checkboxes are as dead as in DS1TR: they only have a meaning for its development placeholder data.

### What the placeholder implementation does (the only existing semantics)

`promoteCategory(category, sphere, startIndex)`: after the placeholder items have been shuffled, find the first not-yet-promoted location whose `sphere` is <= the given sphere and the first not-yet-promoted item of the given category, and swap both to position `startIndex`. Then every location receives `shuffled[index]`.

* Guarantee early weapon: one item of category `weapon` goes to a location of sphere 0 (Firelink Shrine or Undead Burg in the toy data).
* Balanced early loot: one `shield` goes to a location of sphere <= 1 and one `upgrade` item (the toy "Titanite Shard x3") to another location of sphere <= 1.

It checks no stats or usability, no DLC, no upgrade level; it does not reserve locations and does not interact with keys (keys are placed before), shops, gifts, drops or starting equipment (none of those exist in the placeholder path). Spheres exist only in the toy data: the real catalog has no `sphere`, and the real-data function has no equivalent notion (its closest relatives are the area tiers of `items.itemDifficulty`).

## 3. Why DS1TR does nothing with them

The flags were added to the DS1TR contract on 2026-09-27 (`0bc4810`, `packages/contracts/src/index.ts:42-43`; mapping `1bbd08e`, `combinedRandomizerConfig.ts:89-90`; integration document `a7392cd`) because the engine's configuration has them, and the engine was vendored on 2026-09-28 (`a969d2a`) unchanged in this respect. DS1TR always runs the engine with real extracted data and refuses a placeholder result (`combinedRandomizerBridge.ts:492`, `result.dataStatus !== 'extracted'`), so the only code that reads the flags is unreachable. Cause: **an inherited flag whose only implementation belongs to a development data set**; not a lost port, not a mapping slip (the flags are mapped and reach the engine), not replaced by later logic. The parity audit of 2026-10-05 (`98bb3de`) marked both rows FULL because the contract, the mapping and the engine config key all existed; it did not check that the real-data path reads them. That claim was corrected in 0.35.0 and is refined here.

## 4. Defaults, Seed Codes, presets, goldens (Tasks 8, 9)

Both flags default to **true** everywhere: the engine (`config.js:17-18`), DS1TR's `DEFAULT_GENERATION_SETTINGS` (`packages/contracts/src/smokeTests.ts:20-21`), the canonical Seed Code text (`apps/backend/src/core/seedCode.ts:25`: `"guaranteeEarlyWeapon":true,"balancedEarlyLoot":true`), every golden fixture, every exported preset. Because they are no-ops, no world depends on them. Making them functional would change essentially every world (all 51 golden fixtures, every Seed Code made so far) and would need a new generation profile.

## 5. Decision

The condition for restoring behavior ("legacy code proves that an option had meaningful behavior and DS1TR lacks it") is not met: the Python tools never had it, and the only existing implementation works on placeholder data. Inventing a modern reinterpretation is explicitly out of scope. Therefore:

* no engine, generation, Seed Code, preset or fixture change; byte-identical to 0.35.0 by construction (`apps/backend/src/adapters/combinedEarlyOptions.test.mjs` proves that all four on/off combinations write identical placements and hashes for real-data-shaped catalogs, several seeds and pool combinations, and that the flags are read only by the placeholder function);
* the controls stay hidden (`hidden: 'no-effect'` in the settings registry); the fields stay in the configuration, in presets and in Seed Codes, so old codes keep their text;
* documentation corrected (`FEATURE_PARITY.md`, `DECISIONS.md`, `IMPLEMENTATION_STATUS.md`, this file).

If the owner wants an early-game guarantee as a **new** feature (for example "one weapon the starting class can wield lies in an early area"), it must be designed from the real item and area data, with its own option key (or a changed default), and recorded as a new generation profile; it is not a port.
