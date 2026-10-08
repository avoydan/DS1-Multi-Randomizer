# Hint sheet

Status labels: **AUTOMATED VERIFIED**, **STATICALLY VERIFIED**, **GAMEPLAY UNVERIFIED**, **PARTIAL**, **DEFERRED**, **UNSUPPORTED**.

A read-only report of where this world put things. **It never influences generation**: it is built from placement data the generator already wrote, after the world exists, and reading or exporting it changes nothing.

## 1. Where it is

* After a world exists (generated, applied, loaded from a Seed Code, regenerated): **… → View hints** next to the main buttons (secondary action, not a main button), and in each **Run history** row menu: *View hints*, *Export hints*. Apply is not needed. For an old entry whose hint data cannot be rebuilt the actions are disabled with a reason.
* The viewer is a dialog: header (Seed Code, generation profile, creation time), a collapsible **world settings** summary (items/enemies/bosses/fog on or off, key placement, item difficulty, soul items, Laundromat, enemy algorithm, in-game text language), the **spoiler level** switch, a **search**, the sections, and **Export**.

## 2. Spoiler levels (names exist only in the translation resources)

| Level | English / Polish | Shows | Does not show |
|---|---|---|---|
| `low` | Low spoilers / Mało spoilerów | only the important progression items (keys, Lordvessel, Lord Souls, race items), each with the **broad area** (the map-level area for a world pickup) or just the **kind of place** (a boss reward, an NPC gift, an enemy drop, a shop) | which slot (the clean-game item of a boss reward, gift, drop or shop slot), the item that lay there, map ids, the source, key names |
| `progression` | Progression / Progresja | **every progression item** (those plus the fog-tracked and other progression-critical items) with the **exact named place**: area and the item that lies there in the clean game for a world pickup; for a boss reward, NPC gift, enemy drop or shop slot, the slot named by the item it holds in the clean game | the clean-game source of the item, map ids |
| `full` | Full spoilers / Pełne spoilery | **every hintable placement**: destination, **source** (where it lay in the clean game), acquisition type, item category, progression marker, key name, the soul a transposed weapon was made from, map id | nothing is hidden |

Nothing is invented: the engine names a boss-reward, gift, drop or shop slot by the item that lies there in the clean game (it does not record the boss or the NPC), so those slots are described exactly that way ("the boss reward that holds Blighttown Key in the clean game"); and it records an area per *map* (e.g. "Undead Burg / Undead Parish", "Depths"), so a world pickup is named by that area plus the item that lay at the pickup in the clean game; Low spoilers is exactly that area (or only the kind of place for non-world acquisitions). No ambiguity is fabricated. Switching level is a **pure filter** (`filterHints`): no regeneration, the records are not touched (AUTOMATED VERIFIED: levels nest, Low leaks no owner/clean-game/map/key name, Full contains every record, the input is unchanged).

Sections: **Key items**, **Lordvessel and Lord Souls**, **Other progression items**, **Boss souls**, **Other items** (the last two only in Full).

## 3. Data

* `generation` writes **`hints.json`** once, next to the run (`<run folder>/hints.json`, **never inside the package**, which is hashed and applied to the game; the golden fixtures are unchanged). Built by `core/hintStore.ts` from `randomizer.json` (`placements`) via `core/hintData.ts` (`HintRecord`: item, kind, importance, original/randomized location, key name, transposedFrom, `placedAt`).
* `RunHistoryEntry.hintsPath` (additive) points at it; `hintsAvailable` is computed when the history is listed.
* **Old entries** (no `hintsPath`): rebuilt from the `randomizer.json` the run folder still holds (looked for only inside the run root); if it is gone, the hint sheet is unavailable and says so. Nothing is made up.
* Fog-only worlds (no combined stage) have no item placements: the sheet states that.
* `schemaVersion` 1 for both `hints.json` (`ds1tr-hint-data`) and the export; an unknown/newer schema is not guessed at.

## 4. Exports

Made in the interface with the **export language** you pick (current app language, English or Polski) at the level shown. Saved through a main-process save dialog (`file:save`); the renderer never chooses a path. Names: `DS1TR-<short-seed>-hints-<level>.<ext>` (`short-seed` = letters and digits only).

* **HTML** – one self-contained file: a `Content-Security-Policy` that allows nothing but inline style, **no script, no remote resource, no link, no network**; responsive (`viewport`), light/dark, printable (`@media print`), Seed Code, spoiler level, language, generation profile, creation time, the world settings, the sections; **every dynamic value is HTML-escaped** (`escapeHtml`; test with `<script>`, `&`, quotes, backticks and an `<img onerror>` in item names, areas, seed, profile, version).
* **TXT** – human readable top to bottom (headings, one `* item: where` line per hint, an indented "In the clean game: …" line in Full), not a JSON dump.
* **JSON** – `{ format: "ds1tr-hint-sheet", schemaVersion, seedCode, generationProfile, spoilerLevel, language, createdAt, appVersion, settings, placements[] }` with **stable, non-localized ids** (`world-pickup`, `boss-soul-weapon`, …). The placements are identical in English and Polish exports.
* No game file, parameter table or copyrighted data is exported: only names of items/areas/bosses the generator already wrote, and counts.

## 5. Tests

`backend/src/core/hintSheet.test.mjs` (levels, sections, search, world summary, file names, exports in both languages with the real resources, HTML security and escaping, JSON schema, text readability), `hintStore.test.mjs` (persistence, damaged/foreign/newer files, old entries, run-root containment), `hintData.test.mjs`, `restoreCreateWorld.test.mjs`, `electron/saveFile.test.mjs` (safe names, accepted kinds).

## 6. Limits

* **PARTIAL:** "fog-tracked progression" is whatever the generator flagged `progression` (docs/FOG_ITEM_TRACKING.md); the hint sheet does not compute reachability.
* **GAMEPLAY UNVERIFIED:** that the statements match what a player finds in the game is the generator's contract (the same placements that become the spoiler log); the hint sheet adds no check of its own.
* **DEFERRED:** localized game-content names; hints for DLC toggles (not implemented in the product).
