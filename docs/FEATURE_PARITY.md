# Feature parity matrix

Audit date: 2026-10-05. Scope: every user-visible option of the supported original
applications, for Dark Souls Remastered. Updated for the Second Gargoyle batch (backend 0.22.0, desktop
0.10.0, contracts 0.7.0).

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
| FULL | 31 (21 + 4 in Batch 1 + 3 in Batch 2 + 1 Second Gargoyle + 2 in Part A: Placement fit, Replacement chance) |
| UI_ONLY (engine gap) | 18 |
| PARTIAL | 3 (I11 hintsheet; I5 Soul items, where Transposed is unavailable; E2 Normal enemy replacement, added in Part A) |
| INTENTIONALLY_UNSUPPORTED | 1 |
| DUPLICATE | 1 |
| Newly wired in the audit task | 2 (settings preset import/export; restore default settings) |
| Newly wired in Batch 1 | 4 (Fashion Souls, Replace mimics, Replace respawning mosquitoes, Prevent same enemy) |
| Newly wired in Batch 2 | 4 (Soul items: Shuffled and Replaced; Starting items; Pinwheel Chaos; Gwyn nerf) |
| Newly wired for Second Gargoyle | 1 (Replace second Gargoyle) |
| Combined-engine native options audited (separate table) | 16 (15 FULL, 1 INTENTIONALLY_UNSUPPORTED) |

Every `UI_ONLY` option is an engine-level gap. The vendored combined engine had no
concept of difficulty, key placement, replacement percentages, fit modes or custom pools,
so none of them could be enabled by contract/mapping/UI wiring alone. Batch 1 added the
four lowest-risk ones to the engine itself (section 6a). No control is enabled without the
full chain UI -> contract -> backend -> engine -> tests.

## 1. Item Randomizer (HotPocketRemix)

| # | Original option | Original behavior | UI | Contract | Backend | Engine | Status | Proposed DS1TR control / default | Notes |
|---|---|---|---|---|---|---|---|---|---|
| I1 | Seed | Free-text seed string | yes | yes (`masterSeed`) | yes (SHA-256 derivation) | yes | FULL | Master seed | One master seed replaces per-tool seeds. |
| I2 | Game version | PTDE or Remastered | no | no | no | n/a | INTENTIONALLY_UNSUPPORTED | none | PTDE is out of scope for the first release (AGENTS.md). |
| I3 | Difficulty | Fair / Unfair / Very Unfair; controls how far key and progression items are pushed from vanilla placement | placeholder | no | no | no | UI_ONLY | select, default Fair | See gap G1. |
| I4 | Key placement | Not Shuffled / Shuffled / Race Mode / Race Mode+ | placeholder | no | no | no | UI_ONLY | select, default Shuffled | The combined engine has its own progression logic (`progressionLogic`). See G2. |
| I5 | Soul items | Shuffled / Replaced (turned into consumables) / Transposed | yes (Transposed shown, disabled) | `items.soulItems`: `shuffled`, `replaced` | yes | yes for two of three (Batch 2) | PARTIAL | select, default **Shuffled** (legacy default) | Shuffled and Replaced work. Transposed (boss souls 700-711 become boss weapons 75% of the time) cannot change anything: the catalog holds no boss souls in any pool. See 6b and G3. |
| I6 | Starting items | Shield + 1H, Shield + 1H/2H, shield/weapon pool + weapon | yes | `items.startingItems` | yes | yes (Batch 2) | FULL | select, default **Shield + 1H/2H** (legacy default) | Acts only with Starting equipment on. Semantics and the mixed-pool deviation in 6b. |
| I7 | Fashion Souls | Legacy GUI default **on**. On: armor pieces are shuffled individually. Off: armor sets stay together, so a whole set is found at once. | yes | `items.fashionSouls` | yes | yes (Batch 1) | FULL | toggle, default **on** (legacy default) | Semantics in section 6a. The earlier audit listed the default as off and the class as A; both were wrong. |
| I8 | Laundromat Mixup | Randomizes NPC armor appearance (`randomize_npc_armor`) | placeholder | no | no | no | UI_ONLY | toggle, default off | Touches NPC equipment params. See G6. |
| I9 | Senile Gwynevere (randomize Lordvessel) | Lordvessel item may be placed in the world | placeholder | no | no | no | UI_ONLY | toggle, default off | The original label is "Senile Gwynevere". Interacts with Fog `lordvesselGates`. See G7. |
| I10 | Senile Primordial Serpents (randomize Lord Souls) | Lord Souls may be placed in the world | placeholder | no | no | no | UI_ONLY | toggle, default off | Interacts with Fog `requireLordSouls`. See G7. |
| I11 | Write seed info / cheatsheet / hintsheet | Writes placement spoilers and a hint sheet | partial | yes (`generateSpoilerLog`) | yes | yes (`spoiler.txt`) | PARTIAL | existing toggle | The spoiler log is functional. The legacy `build_hintsheet` (read in Batch 1) reports where each *key item* was shuffled to, grouped by the legacy area table (`AREA_HINT_NAMES`, `KEY_HINT_NAMES`). DS1TR keeps key items in vanilla locations (protected-item randomization is blocked), so there is nothing to hint. It needs the legacy Key placement model (G2) first. Not implemented, not invented. |

## 2. Enemy Randomizer (everKalle)

Defaults come from `RestoreDefaultSettings`: `1/-/2/-/0/-/1/-/0/-/3/-/100/-/10/-/90/-/1/-/1/-/0/-/50/-/1/-/1/-/1/-/1/-/0/-/0/-/0/-/1`.
All 21 configuration fields map to one position in the original config string. The
"Proposed default" column uses the original default.

| # | Original option | Original values | Proposed DS1TR control / default | UI | Contract | Backend | Engine | Status |
|---|---|---|---|---|---|---|---|---|
| E1 | Replace bosses | 0 don't, 1 only with bosses, 2 only with normal enemies, 3 either | select, `1` | placeholder | no | no | no | UI_ONLY |
| E2 | Replace normal enemies | same four values | select, `2` | yes (`enemies.placement.normalReplacement`, `sampling-v1`) | yes | yes | yes (Part A: `none`, `normals`; `bosses` and `both` need boss sources) | PARTIAL |
| E3 | Replace NPCs | same four values | select, `0` | placeholder | no | no | no | UI_ONLY |
| E4 | Replace mimics | 0 no, 1 yes | toggle, on | yes (`enemies.replaceMimics`) | yes | yes | yes (Batch 1) | FULL |
| E5 | Placement fit | 0 only where they fit, 1 anywhere, 2 anywhere except when replacing NPCs | select, `0` | yes (`enemies.placement.fit`, `sampling-v1`) | yes | yes | yes (Part A, regular slots; value 2 equals 1 until NPCs are replaced) | FULL |
| E6 | Difficulty mode | 4 curve + easy Asylum, 1 random with curve, 3 random + easy Asylum, 2 fully random | select, `3` | placeholder | no | no | no | UI_ONLY |
| E7 | Replacement chance | 0-100 % | slider, 100 | yes (`enemies.placement.replacementChance`, `sampling-v1`) | yes | yes | yes (Part A, whole range, non-worsening budget) | FULL |
| E8 | Boss chance for normal-enemy slots | 0-100 % | slider, 10 | placeholder | no | no | no | UI_ONLY |
| E9 | Boss chance for boss slots | 0-100 % | slider, 90 | placeholder | no | no | no | UI_ONLY |
| E10 | Replace second Gargoyle | 0 no, 1 yes | select, Replace (legacy default); also Do not replace and Single boss (earlier DS1TR behavior) | yes (`enemies.secondGargoyle`) | yes | yes | yes | FULL |
| E11 | Difficulty strictness | 0 strict, 1 a bit loose, 2 very loose | select, `1` | placeholder | no | no | no | UI_ONLY |
| E12 | T-pose behavior | 0 enabled, 1 disabled | toggle, enabled | placeholder | no | no | no | UI_ONLY |
| E13 | Roaming boss soul percentage | 0-100 % | slider, 50 | placeholder | no | no | no | UI_ONLY |
| E14 | Pinwheel Chaos | 0 enabled, 1 disabled | toggle, disabled (default off) | yes (`enemies.pinwheelChaos`) | yes | yes | yes (Batch 2) | FULL |
| E15 | Type replacement | 0 enabled, 2 enabled except roaming bosses, 1 disabled | select, disabled | placeholder | no | no | no | UI_ONLY |
| E16 | Gwyn spawn nerf | 0 strong, 1 medium, 2 none | select, medium (legacy default) | yes (`enemies.gwynNerf`) | yes | yes | yes (Batch 2) | FULL |
| E17 | Prevent replacement by the same enemy | 0 enabled, 1 disabled | toggle, disabled (default off) | yes (`enemies.preventSameEnemy`) | yes | yes | yes (Batch 1) | FULL |
| E18 | Unique bosses | 1 enabled, 0 disabled | toggle, off | placeholder | no | no | no | UI_ONLY |
| E19 | Roaming boss respawn | 0 enabled, 1 disabled | toggle, enabled | placeholder | no | no | no | UI_ONLY |
| E20 | Hostile NPCs | 1 enabled, 0 disabled | toggle, off | placeholder | no | no | no | UI_ONLY |
| E21 | Replace respawning mosquitoes | 1 enabled, 0 disabled | toggle, on | yes (`enemies.replaceRespawningMosquitoes`) | yes | yes | yes (Batch 1) | FULL |
| E22 | Custom enemy pool (config editor) | Per-enemy enable/disable lists with named, saved configs | picker, "Default" | read-only "Default" field | no | no | no | UI_ONLY |
| E23 | Seed | free text | master seed | yes | yes | yes | yes | FULL |
| E24 | Copy enemy placement from a reference game | Copies a placement from another install | n/a | no | no | n/a | n/a | DUPLICATE |
| E25 | Config text import / export (config string) | Compact `/-/`-separated string | settings preset | yes | yes | n/a | n/a | FULL (new) |
| E26 | Restore default settings | Resets all options | button | yes | n/a | n/a | n/a | FULL (new) |
| E27 | Revert to normal | Restores original MSB / luabnd files | Restore vanilla | yes | yes | yes | yes | FULL |

Notes:

- Batch 1 restored the original defaults for the options it made real (mimics on,
  mosquitoes on, prevent-same off, Fashion Souls on). Placeholders still showing a
  different default than the original: "Allow T-pose behavior" and "Respawning roaming
  bosses" (both on in the original). "Replace second Gargoyle" is now real and uses the
  original default (Replace).
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
| Include DLC | `output.includeDlc` | **UI_ONLY**: nothing reads it. Found in Batch 2 and made honest: the backend rejects `false`, and the control is a disabled planned extension that always includes the DLC. |
| Spoiler log | `output.generateSpoilerLog` | FULL |

Note: `output.includeDlc` is carried in the contract. Whether the combined engine honors
it in every code path was not re-verified in this audit.

## 5. Newly wired in this task

| Item | What | Tests |
|---|---|---|
| Settings preset import/export | `POST /api/preset/export`, `POST /api/preset/import`, `core/preset.ts`, `PresetDocument` in contracts, collapsible UI panel. The preset excludes `gamePath`. Both directions go through `validateRandomizerConfig`, so presets cannot carry unsupported or unsafe options. | `core/preset.test.mjs` (round-trip determinism, strict keys, unsafe/invalid rejection); route check in `server.integration.test.mjs` |
| Restore default settings | Renderer button; keeps game path and master seed. | Typecheck and build only; no component test harness exists in the desktop app. |

## 6. Engine gaps (Phase 3 classification, updated for Batch 1)

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
| G3 | Soul items (I5), **Replaced done in Batch 2; Transposed open** | Soul items shuffled, replaced by consumables, or transposed among souls. | Item-class filter in the lot shuffle, plus a value-matching transposition. `isConsumableSoul` and `keepConsumableSoulsInFiniteShops` exist as seams. | A-B | Low |
| G4 | Starting items (I6) | Chooses the starting shield/weapon rules. | **Done in Batch 2.** | done | none measured |
| G5 | Fashion Souls (I7) | **Done in Batch 1.** | See 6a. | done | none measured |
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
| G8 | Replacement chance (E7) | done (Part A) | Per-slot keep/replace decision before the permutation. A count-preserving permutation must be rebuilt over only the replaced subset. |
| G9 | Boss replace/normal replace/NPC replace modes (E1, E2, E3) | B-C | Source/target class partitions. NPC replacement is the hardest: friendly NPCs are deliberately kept vanilla by the engine today. |
| G10 | Boss chance for normal / boss slots (E8, E9) | B | Cross-class sampling probabilities. Depends on G9. |
| G11 | Placement fit (E5) | done for regular slots (Part A) | The engine already checks compatibility via `pickCompatibleEnemy` and `bossGrounding`; "anywhere" requires relaxing these and accepting the crash and softlock risk, which the 40/40 sweeps do not cover. |
| G12 | Difficulty mode and strictness (E6, E11) | B | Needs a per-enemy tier table and area tiers. The prototype data shows tiers, but the real catalog needs them for all enemies. |
| G13 | Mimics, second Gargoyle, mosquitoes (E4, E10, E21) | done | See 6a. |
| G14 | T-pose behavior (E12) | B | Relates to Anor Londo / New Londo event scripts. It needs an event-level change and interacts with the Fog EMEVD merge. |
| G15 | Roaming boss souls % (E13) and roaming boss respawn (E19) | B | Edit NpcParam/ItemLot soul values and event flags. NpcParam is preserved across Fog, so the merge must be checked. |
| G16 | Pinwheel Chaos, Gwyn nerf (E14, E16) | done | See 6b. |
| G17 | Type replacement (E15) | B | Sub-mode of G9. |
| G18 | Prevent same enemy (E17) | done | See 6a. |
| G19 | Unique bosses (E18) | B | Each boss at most once. Boss planning currently uses `shuffledPortableBosses`, so this needs a no-duplicate constraint. |
| G20 | Hostile NPCs (E20) | B-C | Needs NPC AI and event flag handling; shares G9's NPC difficulty. |
| G21 | Custom enemy pool (E22) | C | Requires a named-pool data model, an editor UI (the original has a full scrolling editor with per-enemy toggles), persistence, and validation that every map keeps a feasible pool. Likely last. |

### 6a. Batch 1 (implemented)

The vendored core was extended (`patches/0002-legacy-parity-batch-1.patch`, recorded in
`UPSTREAM_PIN.md`). All four options default to the original legacy default, which is the
unmodified engine behavior: placement hashes over 24 seeds (three configurations) were
identical before and after, so none of the earlier sweeps is invalidated.

| Option | Confirmed legacy semantics (pinned source) | DS1TR behavior | Deviation |
|---|---|---|---|
| **Fashion Souls** (default on) | `randomize_item_table.place_non_key_fixed_items`: with it on, `follow_items` is cleared for EASY/MEDIUM/HARD and soul items, so each piece is placed on its own. Off keeps follow chains, so "Armor sets ARE kept together". | On = today's behavior (every armor entry is an independent single-entry target). Off = the 31 regular world armor sets (runs of consecutive item-lot rows in one map with one set number; 4-piece and 3-piece) are exchanged as whole sets between same-size set locations, piece by piece in slot order. Own seed stream (`armor-sets`), no fixed points. | The legacy tool may put a set at any location with enough capacity; DS1TR exchanges sets between set locations and removes them from the global pool, so armor sets never land in ordinary pickups and vice versa. Irregular sets (for example Guardian, scattered rows) and armor in shops, gifts and drops stay ordinary pieces. |
| **Replace mimics** (default on) | `randomizer_rng`: tested on the source slot (`creatureId` contains `c2780`) when it is a normal enemy being replaced. Off: the mimic is skipped. | Off: the nine `c2780` slots stay vanilla and leave the pool. | In a count-preserving permutation, a slot left out is also a source left out, so no mimic appears elsewhere either. The legacy tool picks replacements from its own catalog and has no such coupling. |
| **Replace respawning mosquitoes** (default on) | `disableRespawningMosquitoes` skips exactly `c3090_0058/0059/0085/0086/0090/0091`. | Off: those six slots (all in `m14_00_00_00`, the Blighttown swamp) stay vanilla and leave the pool. The other eight `c3090` mosquitoes are not controlled. | Same pool coupling as mimics. |
| **Prevent same enemy** (default off) | `disallowSameReplacement`: reject a replacement whose model id (`c`-number) is contained in the original's id. | On: after the per-map model budget is satisfied, `separateSameModelSources` swaps sources (at most 8 passes, one swap per offending slot per pass, random start from its own stream `prevent-same-enemy`) so no regular slot receives its own model, keeping each map within the 30-model budget. An impossible pool throws `PREVENT_SAME_ENEMY_INFEASIBLE`; an unresolved residue throws `PREVENT_SAME_ENEMY_UNRESOLVED`. | Bosses already never receive the same boss archetype (`shuffledPortableBosses`), so the option changes only regular enemies. Dragons and linked groups are separate plans and untouched. |

Parent gating: all four act only while their parent is on (`items.randomizeWorldItems`,
`enemies.randomizeRegularEnemies`). Otherwise the backend sends the legacy defaults, and
the UI shows the controls inactive.

#### Replace second Gargoyle (E10) - implemented

Batch 1 stopped on this option because it appeared to need a change to events `11015396` and
`11015382` that FogMod "also rewrites". That premise was wrong: FogMod makes no edit of its own to
either event (it copies the game directory's instructions into them), so there is no Fog change to
reconcile. No FogHost overlay was added. Evidence and the instruction-level comparison:
`docs/FOGHOST_INTEGRATION.md`, "Second Gargoyle events".

Legacy semantics (pinned `83f008d`; `randomizer_rng.py`, `event_tools.py`, `eventTemplates.dkscript`):

- **Controlled entity:** `m10_01_00_00:c5350_0001` (entity 1010801, NPC 535001), the second Bell
  Gargoyle, which joins the fight when the first (`c5350_0000`, 1010800) falls to 60% health. In the
  legacy data all three Bell bodies are type 1 (boss), size large, difficulty 3.
- **Values:** `0` Do not replace, `1` Replace. **Legacy default: 1** (GUI `IntVar` set to 1, the
  "Restore default settings" string, and the user's own `lastConfig.txt` all agree).
- **Mode 0:** the slot is skipped (`specialCase`); the Gargoyle and the vanilla events stay.
- **Mode 1:** the slot is replaced like any boss slot, **independently of the first body** (its own draw;
  with the default "bosses replaced with bosses" it is another boss, repeats allowed), moved to
  `(10.69, 48.92, 124.35)` / `(0, 1.84, 0)`; and when the replacement is not a `c5350`,
  `ApplyGargoyle2Fix` swaps events `11015396` and `11015382` for the templates in
  `eventTemplates.dkscript`. Those equal the vanilla events minus `2004[41]` + `2003[18]`
  (11015396, entity 1010801) and minus one `2003[18]` (11015382, entity 1010700, the Taurus intro).
  No other MSB or event edit belongs to the option.

DS1TR behavior (`secondGargoyle`, contract values `replace` default, `keep`, `single-boss`):

| Value | Behavior |
|---|---|
| `replace` (legacy 1, product default) | When the Bell unit is swapped for another linked unit, body `_0001` is drawn an independent boss from the portable boss pool (own stream `second-gargoyle`; Gwyn nerf and Pinwheel Chaos exclusions honored; repeats allowed) and placed at the legacy rooftop position. The vanilla event `11015396` releases it when the first body drops to 60%; the combined engine's standard replaced-boss edit leaves the hold / wait / release structure intact and renames the boss bar. No forced combat activation is added, because it would wake the body at once. The third body (`_0002`) and the second tail stay disabled as before. |
| `keep` (legacy 0) | The body and its tail get no placement; event `11015396` stays byte-identical to vanilla. |
| `single-boss` | The earlier DS1TR behavior: the extra Bell bodies are disabled so the fight has one boss. Not a legacy value. Kept so older presets reproduce their output; the UI offers it as "Single boss (earlier DS1TR behavior)". |

Deviations from the legacy tool:

- The legacy tool's pool for the draw is its own catalog (and difficulty settings); DS1TR draws from the
  boss pool the combined engine already uses. Linked and dragon bosses (for example Sanctuary Guardian,
  Centipede Demon, Seath, Gaping Dragon) cannot be placed as a lone body and are not offered.
- The combined engine's replaced-boss edit removes a few more Gargoyle-specific rows than the legacy
  template (standby animations, the overhead health-bar hide) and may add wake-up rows for bosses that
  need them; the structure is the legacy one. Event `11015382` is edited by the generic Taurus handling
  whenever Taurus is replaced, independent of this option (the legacy tool bundles it with the fix).
- The release depends on the new boss's AI waking up from `set_ai(on)`; the combined engine adds its
  usual wake-up rows for the bosses that need them. This cannot be verified without running the game.
- Needs boss randomization on (the Bell fight is a boss encounter); otherwise the value is ignored and
  the backend sends `single-boss`.

Older presets: versions 1 to 3 migrate `secondGargoyle` to `single-boss`, because that is what they
produced; presets are now version 4.

### 6b. Batch 2 (implemented)

The vendored core was extended again (`patches/0003-legacy-parity-batch-2.patch`, recorded in
`UPSTREAM_PIN.md`). The engine's own defaults reproduce the unmodified behavior (the 24
placement hashes from Batch 1 are still identical). The **product** defaults in the contract and
the UI are the legacy defaults; they differ from the engine defaults for two options, so older
presets migrate to the behavior-preserving values (see "Defaults" below).

| Option | Confirmed legacy semantics (pinned source) | DS1TR behavior | Deviation |
|---|---|---|---|
| **Soul items** (legacy default Shuffled) | `randomize_item_table`: *Shuffled* places soul items like any item. *Replaced* turns every item lot part classed `SMALL_SOUL` or `BIG_SOUL` (the goods 400-409 lots) into a random consumable from a fixed list, with a random count, **before** shuffling. *Transposed* gives each `BOSS_SOUL` lot (goods 700-711) a 75% chance to become a boss weapon of that boss (Sanctuary Guardian has none). Humanity is not a soul item; Fire Keeper Souls (390-396) are not touched. | *Shuffled* is today's behavior (the lesser souls are ordinary pool items). *Replaced* swaps goods 400-409 in the world, NPC-gift and enemy-drop pools for another consumable before the shuffle (own stream `soul-items`, no draws otherwise). Boss souls are not in the pool, so nothing else is touched. | The replacement pool is derived from the catalog's own unprotected world goods (with their vanilla quantities, in item-id order), not the legacy list: the legacy repository has no license, so its table is not reused. Ammunition and key items can therefore not appear. **Transposed is not available**: the catalog has no boss souls in any pool (they are granted by boss rewards that the scan does not extract), so the option could not change anything; it would need a scan/patch extension for boss reward lots. The UI shows it as a disabled choice. |
| **Starting items** (legacy default Shield + 1H/2H) | `create_starting_class_weapon_data`: the right hand is a weapon usable at base stats, one-handed for *Shield + 1H* and with the 1.5x strength allowance for *Shield + 1H/2H* and *Shield/weapon + weapon*; the left hand is a shield in the first two modes and, in the third, any weapon from an expanded left-hand pool. Hunter/Sorcerer extras (arrows, spells, a second weapon) do not depend on the mode. | Acts only with Starting equipment on. `shield-1h`: primary weapon strength <= base strength, left hand a shield. `shield-2h` (product default): primary <= floor(1.5 x base strength), left hand a shield. `mixed-pool`: today's behavior. Shields are EquipParamWeapon ids 1,400,000-1,599,999 (checked against the real pool: every shield-named weapon is in the range and every item in it is a shield). Pickups stay unique and class stat requirements are still enforced. | `mixed-pool` is the closest match to *Shield/weapon + weapon* but not identical: the engine applies the 1.5x allowance to every primary weapon (legacy only to two-handable ones) and its left-hand pool is every usable one-handed weapon. Starting equipment is Asylum pickups; no key or progression item is involved, so there is no boundary with the protected-item model. |
| **Pinwheel Chaos** (legacy default off) | The Catacombs fight has the damageable main Pinwheel `c3320_0000` and 14 one-hit clones. Off: the main Pinwheel is replaced like any boss and the clones are skipped. On: the main Pinwheel is skipped and the clones are replaced as normal enemies, with normal HP instead of the clone's 1 HP. MSB (parts) and the usual per-entity event edits; no dedicated event. | On: the main boss slot leaves the boss shuffle (it stays vanilla and Pinwheel is not offered elsewhere as a boss) and the 14 clones (`m13_00_00_00:c3320_0001`-`0014`) join the regular pool as targets. Their sources are drawn independently from the regular permutation (a source can feed several clones; model budget kept at 30 for the Catacombs; prevent-same honored). Their placements use scaling `vanilla`, so the replacement keeps its own combat stats while drops and team still follow the slot. Needs regular enemies on. | The clones' sources are drawn with repetition, as in the legacy tool, instead of being part of the count-preserving permutation. The main Pinwheel is not offered as a boss elsewhere while the option is on. |
| **Gwyn nerf** (legacy default Medium) | `isCombinationInvalid`: when the candidate replacement is `c5370` (Lord Gwyn), it is rejected with probability 85% (Strong) or 60% (Medium) and another candidate is drawn; None never rejects. It changes **no** stats, AI, event or equipment: it only changes how often Gwyn is chosen. (A separate, unlabelled 65% rejection for `c2510` exists in the legacy tool and is not part of this option.) | With the legacy probability, Lord Gwyn is removed from the boss candidates for the seed, **including at the Kiln**; another boss fills the gap, so one boss may appear in two places. One draw from its own stream (`gwyn-nerf`); the note in the spoiler says whether it applied. Needs boss randomization on. | Gwyn appears once in a permutation, never several times, so the nerf is a per-seed probability rather than a per-slot one. Expected Gwyn appearances fall by the same factor (about 15% / 40% of seeds keep Gwyn in play). Duplicated bosses had never been generated before; real `patch-enemies` and the Fog sweeps below accept them. No NpcParam is touched, so there is no scaling interaction. |

Fog and event surfaces:

- **Soul items, Starting items:** ItemLotParam / starting pickups only (GameParam). No MSB or EMEVD.
- **Pinwheel Chaos:** MSB parts and the per-entity event edits in `m13_00_00_00`. A real
  `patch-enemies` run edits **fewer** events than the default: `11305392`, `11305396`, `11305398` and
  `11305250` are no longer touched (the boss events stop being edited because the main Pinwheel
  stays); only event 0 and `11305045` remain in `m13_00`. `11305100` (a FogMod `copyRandoEvents`
  event) is never edited, and none of the 15 whitelisted overlay events is involved.
- **Gwyn nerf:** boss selection only; it reaches the same events as any other boss permutation.

Defaults:

| Option | Legacy default | Engine default | Product default (contract, UI) | Older presets migrate to |
|---|---|---|---|---|
| Soul items | Shuffled | shuffled | shuffled | shuffled |
| Starting items | Shield + 1H/2H | mixed-pool | shield-2h | mixed-pool |
| Pinwheel Chaos | off | off | off | off |
| Gwyn nerf | Medium | none | medium | none |

The product defaults for Starting items and Gwyn nerf change the output of otherwise unchanged
configurations that use those features (starting equipment on; bosses on), so version 1 and 2
presets migrate to the values that reproduce their old output.

### Cross-cutting

| ID | Gap | Class | Notes |
|---|---|---|---|
| G22 | Boss overlay whitelist | B | The FogHost whitelist is fixed to the 15 measured events. Any option that edits another boss event produces `FOG_MERGE_CONFLICT` until measured and added. |
| G23 | Hintsheet export (I11) | blocked by G2 | The legacy hintsheet reports shuffled key locations; DS1TR does not shuffle keys. |

## 7. Intentionally unsupported / duplicate / ambiguous

| Option | Status | Reason |
|---|---|---|
| I2 Game version (PTDE) | INTENTIONALLY_UNSUPPORTED | PTDE is out of scope for the first release. |
| E24 Copy enemy placement | DUPLICATE | The master seed plus a preset reproduces a placement deterministically. |
| Protected items (native) | INTENTIONALLY_UNSUPPORTED | Backend rejects it until progression safety is proven. A product-safety decision. |
| FogMod debug flags | not user options | Not in the pinned `MainForm`. |
| I11 hintsheet | PARTIAL | The format is known, but it reports shuffled key locations, and DS1TR does not shuffle key items. Needs G2 first. |
| Legacy config string compatibility | not supported | Legacy `/-/` strings are not imported. DS1TR has its own versioned preset. |
