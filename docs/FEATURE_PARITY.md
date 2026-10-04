# Feature parity matrix

Audit date: 2026-10-05. Scope: every user-visible option of the supported original
applications, for Dark Souls Remastered. Written against DS1TR backend 0.19.0, desktop
0.7.0, contracts 0.4.0.

## Sources audited

| Application | Source | Pin / revision | Notes |
|---|---|---|---|
| Item Randomizer | `HotPocketRemix/DarkSoulsItemRandomizer` (`randomizer_gui.py`, `randomizer_options.py`) | `f7cabbd` (master, last push 2021-06-08), version 0.3 | No license file. Reference for **behavior only**; nothing is copied. |
| Enemy Randomizer | `everKalle/Dark-Souls-Enemy-Randomizer` (`randomizer.py` widgets, `BuildConfigString`, `RestoreDefaultSettings`) | `83f008d` (master, last push 2022-12-29) | No license file. Reference for **behavior only**. |
| FogMod | `thefifthmatt/FogMod` `MainForm.Designer.cs` | `6e6dc78` (the commit FogHost pins) | Apache-2.0. Control names are the option keys. |
| Combined engine | `kellernz1/ds-randomizer`, vendored in `engines/combined/vendor` | `config.js` v0.5.66 | Native options, listed separately. |

The original sources are not vendored in this repository. They were read from GitHub into
a scratch directory for this audit only. The commit ids above are the revisions read.

## Status legend

| Status | Meaning |
|---|---|
| FULL | Shown in the UI, in the typed contract, forwarded by the backend, implemented by the engine, and covered by tests. |
| UI_ONLY | A disabled "planned" placeholder in the UI only. No contract field, no backend mapping, no engine behavior. |
| BACKEND_ONLY | Contract/backend support exists without a working UI. (None found.) |
| MISSING | Nothing exists anywhere. (None remain.) |
| PARTIAL | A functional equivalent exists, but not the full original behavior. |
| INTENTIONALLY_UNSUPPORTED | Deliberately not supported, with a documented reason. |
| DUPLICATE | Superseded by another DS1TR setting or by the architecture. |
| OBSOLETE | Obsolete for the supported product. (None; PTDE is classed as unsupported by scope.) |

## Summary

| | Count |
|---|---|
| Original options audited (Item 11 + Enemy 27 + FogMod 16) | **54** |
| FULL | 21 |
| UI_ONLY (engine gap) | 30 |
| PARTIAL | 1 |
| INTENTIONALLY_UNSUPPORTED | 1 |
| DUPLICATE | 1 |
| Newly wired in this task | 2 (settings preset import/export; restore default settings) |
| Combined-engine native options audited (separate table) | 16 (15 FULL, 1 INTENTIONALLY_UNSUPPORTED) |

Every one of the 30 `UI_ONLY` options is an engine-level gap. The vendored combined
engine reads only the keys in its `config.js` (items, enemy scaling, bosses, spoiler,
DLC) and has no concept of difficulty, key placement, replacement percentages, fit modes
or custom pools. None of the 30 can be turned on by contract/mapping/UI wiring alone, so
none was wired. No `PLANNED EXTENSION` control became functional in this task, and none
was faked.

## 1. Item Randomizer (HotPocketRemix)

| # | Original option | Original behavior | UI | Contract | Backend | Engine | Status | Proposed DS1TR control / default | Notes |
|---|---|---|---|---|---|---|---|---|---|
| I1 | Seed | Free-text seed string | yes | yes (`masterSeed`) | yes (SHA-256 derivation) | yes | FULL | Master seed | One master seed replaces per-tool seeds. |
| I2 | Game version | PTDE or Remastered | no | no | no | n/a | INTENTIONALLY_UNSUPPORTED | none | PTDE is out of scope for the first release (AGENTS.md). |
| I3 | Difficulty | Fair / Unfair / Very Unfair; controls how far key and progression items are pushed from vanilla placement | placeholder | no | no | no | UI_ONLY | select, default Fair | See gap G1. |
| I4 | Key placement | Not Shuffled / Shuffled / Race Mode / Race Mode+ | placeholder | no | no | no | UI_ONLY | select, default Shuffled | The combined engine has its own progression logic (`progressionLogic`). See G2. |
| I5 | Soul items | Shuffled / Replaced (turned into consumables) / Transposed | placeholder | no | no | no | UI_ONLY | select, default Shuffled | The combined engine already has `isConsumableSoul` handling for shops only. See G3. |
| I6 | Starting items | Shield + 1H, Shield + 1H/2H, shield/weapon pool + weapon | placeholder | no | no | no | UI_ONLY | select, default Shield + 1H | Relates to `randomizeStartingEquipment`, whose semantics differ. See G4. |
| I7 | Fashion Souls | Armor drops randomized without restriction to set | placeholder | no | no | no | UI_ONLY | toggle, default off | See G5. |
| I8 | Laundromat Mixup | Randomizes NPC armor appearance (`randomize_npc_armor`) | placeholder | no | no | no | UI_ONLY | toggle, default off | Touches NPC equipment params. See G6. |
| I9 | Senile Gwynevere (randomize Lordvessel) | Lordvessel item may be placed in the world | placeholder | no | no | no | UI_ONLY | toggle, default off | The original label is "Senile Gwynevere". Interacts with Fog `lordvesselGates`. See G7. |
| I10 | Senile Primordial Serpents (randomize Lord Souls) | Lord Souls may be placed in the world | placeholder | no | no | no | UI_ONLY | toggle, default off | Interacts with Fog `requireLordSouls`. See G7. |
| I11 | Write seed info / cheatsheet / hintsheet | Writes placement spoilers and a hint sheet | partial | yes (`generateSpoilerLog`) | yes | yes (`spoiler.txt`) | PARTIAL | existing toggle | The spoiler log is functional. The separate hintsheet format was not reproduced. The original hintsheet's exact content could not be established without running the tool, so no behavior is invented here. |

## 2. Enemy Randomizer (everKalle)

Defaults come from `RestoreDefaultSettings`: `1/-/2/-/0/-/1/-/0/-/3/-/100/-/10/-/90/-/1/-/1/-/0/-/50/-/1/-/1/-/1/-/1/-/0/-/0/-/0/-/1`.
All 21 configuration fields map to one position in the original config string. The
"Proposed default" column uses the original default.

| # | Original option | Original values | Proposed DS1TR control / default | UI | Contract | Backend | Engine | Status |
|---|---|---|---|---|---|---|---|---|
| E1 | Replace bosses | 0 don't, 1 only with bosses, 2 only with normal enemies, 3 either | select, `1` | placeholder | no | no | no | UI_ONLY |
| E2 | Replace normal enemies | same four values | select, `2` | placeholder | no | no | no | UI_ONLY |
| E3 | Replace NPCs | same four values | select, `0` | placeholder | no | no | no | UI_ONLY |
| E4 | Replace mimics | 0 no, 1 yes | toggle, on | placeholder | no | no | no | UI_ONLY |
| E5 | Placement fit | 0 only where they fit, 1 anywhere, 2 anywhere except when replacing NPCs | select, `0` | placeholder | no | no | no | UI_ONLY |
| E6 | Difficulty mode | 4 curve + easy Asylum, 1 random with curve, 3 random + easy Asylum, 2 fully random | select, `3` | placeholder | no | no | no | UI_ONLY |
| E7 | Replacement chance | 0-100 % | slider, 100 | placeholder | no | no | no | UI_ONLY |
| E8 | Boss chance for normal-enemy slots | 0-100 % | slider, 10 | placeholder | no | no | no | UI_ONLY |
| E9 | Boss chance for boss slots | 0-100 % | slider, 90 | placeholder | no | no | no | UI_ONLY |
| E10 | Replace second Gargoyle | 0 no, 1 yes | toggle, on | placeholder | no | no | no | UI_ONLY |
| E11 | Difficulty strictness | 0 strict, 1 a bit loose, 2 very loose | select, `1` | placeholder | no | no | no | UI_ONLY |
| E12 | T-pose behavior | 0 enabled, 1 disabled | toggle, enabled | placeholder | no | no | no | UI_ONLY |
| E13 | Roaming boss soul percentage | 0-100 % | slider, 50 | placeholder | no | no | no | UI_ONLY |
| E14 | Pinwheel Chaos | 0 enabled, 1 disabled | toggle, disabled | placeholder | no | no | no | UI_ONLY |
| E15 | Type replacement | 0 enabled, 2 enabled except roaming bosses, 1 disabled | select, disabled | placeholder | no | no | no | UI_ONLY |
| E16 | Gwyn spawn nerf | 0 strong, 1 medium, 2 none | select, medium | placeholder | no | no | no | UI_ONLY |
| E17 | Prevent replacement by the same enemy | 0 enabled, 1 disabled | toggle, disabled | placeholder | no | no | no | UI_ONLY |
| E18 | Unique bosses | 1 enabled, 0 disabled | toggle, off | placeholder | no | no | no | UI_ONLY |
| E19 | Roaming boss respawn | 0 enabled, 1 disabled | toggle, enabled | placeholder | no | no | no | UI_ONLY |
| E20 | Hostile NPCs | 1 enabled, 0 disabled | toggle, off | placeholder | no | no | no | UI_ONLY |
| E21 | Replace respawning mosquitoes | 1 enabled, 0 disabled | toggle, on | placeholder | no | no | no | UI_ONLY |
| E22 | Custom enemy pool (config editor) | Per-enemy enable/disable lists with named, saved configs | picker, "Default" | read-only "Default" field | no | no | no | UI_ONLY |
| E23 | Seed | free text | master seed | yes | yes | yes | yes | FULL |
| E24 | Copy enemy placement from a reference game | Copies a placement from another install | n/a | no | no | n/a | n/a | DUPLICATE |
| E25 | Config text import / export (config string) | Compact `/-/`-separated string | settings preset | yes | yes | n/a | n/a | FULL (new) |
| E26 | Restore default settings | Resets all options | button | yes | n/a | n/a | n/a | FULL (new) |
| E27 | Revert to normal | Restores original MSB / luabnd files | Restore vanilla | yes | yes | yes | yes | FULL |

Notes:

- The placeholder defaults in the current UI differ from the original defaults in several
  places: the original starts with "Replace mimics", "Replace second Gargoyle",
  "Replace respawning mosquitoes", "Allow T-pose behavior" and "Respawning roaming bosses"
  all on, and the UI placeholders start them off. When the options become real, adopt the
  original defaults above.
- Original value 1 means "off" for several options whose label reads "Disabled"
  (E12, E14, E17, E19). The inverted numbering is an original quirk. The DS1TR contract
  should use booleans named for the behavior (`allowTPose`, `pinwheelChaos`,
  `preventSameEnemy`, `respawningRoamingBosses`), never the legacy integers.
- E24 is a legacy workflow for reproducing a placement on another machine. DS1TR's
  deterministic master seed plus a preset (E25) reproduces the same result, so the option
  is a DUPLICATE and intentionally not built. The legacy `/-/` string itself is not
  imported: the DS1TR preset is a different, versioned JSON format, and the engines'
  semantics differ.
- E25's preset also covers Items and Fog, which the original string did not.

## 3. FogMod (thefifthmatt)

All fourteen controls are wired end to end. FogHost maps contract keys to FogMod flags
in `tools/fog-host/src/HostRequest.cs` (`OptionFlags`).

| # | Original control (flag) | DS1TR contract key | Default | Status |
|---|---|---|---|---|
| F1 | Traversable fog gates, non-boss (`world`) | `traversableFogs` | on | FULL |
| F2 | Boss fog gates (`boss`) | `bossFogs` | on | FULL |
| F3 | Warps between areas (`warp`) | `areaWarps` | on | FULL |
| F4 | Major PvP fog gates (`major`) | `majorPvpFogs` | off | FULL |
| F5 | Minor PvP fog gates (`minor`) | `minorPvpFogs` | off | FULL |
| F6 | Lordvessel gates (`lordvessel`) | `lordvesselGates` | off | FULL |
| F7 | Random start outside Asylum (`start`) | `randomStart` | off | FULL |
| F8 | Disconnected fog gates (`unconnected`) | `disconnectedFogs` | off | FULL |
| F9 | No-Fall Bed of Chaos (`bboc`) | `noFallBedOfChaos` | off | FULL |
| F10 | Scale enemies and bosses (`scale`) | `scaling` | on | FULL |
| F11 | Glitched logic (`hard`) | `glitchedLogic` | off | FULL |
| F12 | Require Lord Souls (`lords`) | `requireLordSouls` | on | FULL |
| F13 | Pacifist Mode (`pacifist`) | `pacifistMode` | off | FULL |
| F14 | Game language | `language` (13 values) | ENGLISH | FULL |
| F15 | Fixed seed | `masterSeed` (derived) | n/a | FULL |
| F16 | Restore backups | Restore vanilla | n/a | FULL |

`v4` (version marker) and `dryrun` are backend concerns. Other flags in the
vendored `RandomizerOptions` usage (`vanilla`, `explain`, `alwaysshow`, `patchkiln`,
`scalingbase`, `dump*`, `msbinfo`, `debug*`) are not controls in the pinned
`MainForm`. They are developer/debug switches, so they are deliberately not exposed
and not counted. The FogMod UI also validates that a random start needs boss or
traversable fog gates; DS1TR enforces the same rule in `configValidation.ts`.

## 4. Combined-engine native options (not original-app options)

| Option | Contract key | Status |
|---|---|---|
| World items | `items.randomizeWorldItems` | FULL |
| Protected items | `items.randomizeProtectedItems` | INTENTIONALLY_UNSUPPORTED: the engine implements it, but the backend rejects it ("blocked until progression safety is proven") and the UI control is disabled. Enabling is a product-safety decision, not wiring. |
| Progression safeguards | `items.progressionLogic` | FULL |
| Guaranteed early weapon | `items.guaranteeEarlyWeapon` | FULL |
| Balanced early loot | `items.balancedEarlyLoot` | FULL |
| Starting class | `items.randomizeStartingClass` | FULL |
| Starting equipment | `items.randomizeStartingEquipment` | FULL |
| NPC gifts | `items.randomizeGifts` | FULL |
| Enemy drops | `items.randomizeEnemyDrops` | FULL |
| Guaranteed enemy drops | `items.guaranteedEnemyDrops` | FULL |
| Shops | `items.randomizeShops` | FULL |
| Regular enemies | `enemies.randomizeRegularEnemies` | FULL |
| Bosses | `enemies.randomizeBosses` | FULL |
| Enemy scaling (vanilla / area / progressive) | `enemies.scaling` | FULL |
| Include DLC | `output.includeDlc` | FULL |
| Spoiler log | `output.generateSpoilerLog` | FULL |

Note: `output.includeDlc` is carried in the contract. Whether the combined engine honors
it in every code path was not re-verified in this audit.

## 5. Newly wired in this task

| Item | What | Tests |
|---|---|---|
| Settings preset import/export | `POST /api/preset/export`, `POST /api/preset/import`, `core/preset.ts`, `PresetDocument` in contracts, collapsible UI panel. The preset excludes `gamePath`. Both directions go through `validateRandomizerConfig`, so presets cannot carry unsupported or unsafe options. | `core/preset.test.mjs` (round-trip determinism, strict keys, unsafe/invalid rejection); route check in `server.integration.test.mjs` |
| Restore default settings | Renderer button; keeps game path and master seed. | Typecheck and build only; no component test harness exists in the desktop app. |

## 6. Engine gaps (Phase 3 classification)

Effort labels: **A** small engine extension, **B** medium behavioral port, **C** large or
complex legacy feature. These are planning estimates from reading the vendored
`generator.js` structure and the original option semantics. None was prototyped.

General compatibility constraint for all of them: the Fog pipeline merges the combined
engine's EMEVD/MSB/GameParam output by content, with a measured retry rate for key items
(10 of 50 attempts with bosses + Fog). Anything that moves key items, boss events or
their arenas changes that surface and must be re-validated with the 40-seed sweeps.
Every new option must be added to the contract, `configValidation.ts` key lists, the
combined config mapping, the manifest and the preset, and must derive from the named
seed stream.

### Item engine

| ID | Option(s) | What the original does | What DS1TR would need | Class | Fog risk |
|---|---|---|---|---|---|
| G1 | Difficulty (I3) | Biases how far items and keys can land from vanilla locations/areas. | A tiering model over item lots in `randomizeExtractedItemLots`. The combined engine already has `balancedEarlyLoot` and annotated areas, so this is a weighting parameter on existing shuffles, but "Unfair/Very Unfair" semantics are not specified in the sources read here. | B | Low-medium (item lots only) |
| G2 | Key placement (I4) | Not Shuffled / Shuffled / Race (keys confined to a set of locations) / Race+ | Per-mode candidate-location sets for key items, feeding `progressionLogic`. Already interacts with the key-item retry loop. | B | **High**: key placement drives the combined-stage key-item retry (7-10 of ~50 attempts in the recorded sweeps). |
| G3 | Soul items (I5) | Soul items shuffled, replaced by consumables, or transposed among souls. | Item-class filter in the lot shuffle, plus a value-matching transposition. `isConsumableSoul` and `keepConsumableSoulsInFiniteShops` exist as seams. | A-B | Low |
| G4 | Starting items (I6) | Chooses the starting shield/weapon rules. | Extend `randomizeStartingClasses`. Existing `randomizeStartingEquipment` has different semantics, so the two must be reconciled (gating). | A-B | Low |
| G5 | Fashion Souls (I7) | Removes armor-set coherence restrictions. | Filter change in armor drop pool. | A | Low |
| G6 | Laundromat Mixup (I8) | Randomizes NPC armor appearance. | Needs NPC equipment params (NpcParam / ChrInit). `DsrDataTool` already preserves NpcParam rows for Fog, so conflicts must be checked. | B | Medium (NpcParam rows are preserved across the merge) |
| G7 | Randomize Lordvessel / Lord Souls (I9, I10) | Treat those items as ordinary shuffle candidates. | Remove them from the protected list and adjust the key-item logic. **Must be coupled with Fog**: `lordvesselGates` and `requireLordSouls` assume vanilla delivery. Needs explicit incompatibility rules. | B | **High** |

### Enemy engine

The combined engine's enemy randomization is a count-preserving global permutation with
a 30-model-per-map budget (`MAX_ENEMY_MODELS_PER_MAP`, `limitUniqueEnemyModelsByMap`),
with separate boss planning (`buildDragonPlan`, `buildMassOfSoulsPlan`,
`randomizeExtractedBosses`). The legacy tool instead decides per slot with
probabilities. The two models differ in kind, so the legacy percentages cannot be
mapped one-to-one.

| ID | Option(s) | Class | Notes and risk |
|---|---|---|---|
| G8 | Replacement chance (E7) | B | Per-slot keep/replace decision before the permutation. A count-preserving permutation must be rebuilt over only the replaced subset. |
| G9 | Boss replace/normal replace/NPC replace modes (E1, E2, E3) | B-C | Source/target class partitions. NPC replacement is the hardest: friendly NPCs are deliberately kept vanilla by the engine today. |
| G10 | Boss chance for normal / boss slots (E8, E9) | B | Cross-class sampling probabilities. Depends on G9. |
| G11 | Placement fit (E5) | B | The engine already checks compatibility via `pickCompatibleEnemy` and `bossGrounding`; "anywhere" requires relaxing these and accepting the crash and softlock risk, which the 40/40 sweeps do not cover. |
| G12 | Difficulty mode and strictness (E6, E11) | B | Needs a per-enemy tier table and area tiers. The prototype data shows tiers, but the real catalog needs them for all enemies. |
| G13 | Mimics, second Gargoyle, mosquitoes (E4, E10, E21) | A | Toggle individual slot groups. The Gargoyle already has bespoke handling (`bell-gargoyles`, `disabled-extra-bell-gargoyle-body`). |
| G14 | T-pose behavior (E12) | B | Relates to Anor Londo / New Londo event scripts. It needs an event-level change and interacts with the Fog EMEVD merge. |
| G15 | Roaming boss souls % (E13) and roaming boss respawn (E19) | B | Edit NpcParam/ItemLot soul values and event flags. NpcParam is preserved across Fog, so the merge must be checked. |
| G16 | Pinwheel Chaos, Gwyn nerf (E14, E16) | A-B | Each is a single-boss special-case. Gwyn is a boss fight with scripted spawns. Both interact with the boss-event overlay whitelist. |
| G17 | Type replacement (E15) | B | Sub-mode of G9. |
| G18 | Prevent same enemy (E17) | A | Fixed-point rejection already exists (`shuffledWithoutFixedPoints`). The option would toggle it, but its relation to the map budget must be checked. |
| G19 | Unique bosses (E18) | B | Each boss at most once. Boss planning currently uses `shuffledPortableBosses`, so this needs a no-duplicate constraint. |
| G20 | Hostile NPCs (E20) | B-C | Needs NPC AI and event flag handling; shares G9's NPC difficulty. |
| G21 | Custom enemy pool (E22) | C | Requires a named-pool data model, an editor UI (the original has a full scrolling editor with per-enemy toggles), persistence, and validation that every map keeps a feasible pool. Likely last. |

### Cross-cutting

| ID | Gap | Class | Notes |
|---|---|---|---|
| G22 | Boss overlay whitelist | B | The FogHost whitelist is fixed to the 15 measured events. Any option that edits another boss event produces `FOG_MERGE_CONFLICT` until measured and added. |
| G23 | Hintsheet export (I11) | A | Spoiler log exists. Reproducing the original hint format needs a defined format. |

## 7. Intentionally unsupported / duplicate / ambiguous

| Option | Status | Reason |
|---|---|---|
| I2 Game version (PTDE) | INTENTIONALLY_UNSUPPORTED | PTDE is out of scope for the first release. |
| E24 Copy enemy placement | DUPLICATE | The master seed plus a preset reproduces a placement deterministically. |
| Protected items (native) | INTENTIONALLY_UNSUPPORTED | Backend rejects it until progression safety is proven. A product-safety decision. |
| FogMod debug flags | not user options | Not in the pinned `MainForm`. |
| I11 hintsheet | PARTIAL / ambiguous | The legacy hint format could not be determined from the GUI sources. Stopped short of inventing it. |
| Legacy config string compatibility | not supported | Legacy `/-/` strings are not imported. DS1TR has its own versioned preset. |
