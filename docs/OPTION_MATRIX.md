# Randomizer option matrix

This document is the product requirement for the settings UI. An option being
listed here does not imply that the current adapter implements it.

## Combined DSR engine: native options

Items:

- world items;
- protected/progression items;
- progression safeguards;
- guaranteed early weapon (accepted and stored; no effect in this version, not drawn: see SETTINGS_INVENTORY.md);
- balanced early loot (accepted and stored; no effect in this version, not drawn: see SETTINGS_INVENTORY.md);
- starting class;
- starting equipment;
- NPC gifts;
- enemy drops;
- guaranteed enemy drops;
- shops.

Enemies:

- regular enemies;
- bosses;
- enemy scaling: vanilla, area, progressive.

General:

- include DLC;
- spoiler log.

## Item parity extensions

These are requirements taken from the legacy Item Randomizer. They must be
implemented independently in the GPL combined fork or explicitly reported as
unsupported. Legacy source must not be copied without permission.

- difficulty: Fair, Unfair, Very Unfair;
- key placement: Not Shuffled, Shuffled, Race Mode, Race Mode+;
- soul items: Shuffled, Replaced, Transposed;
- starting items: Shield + 1H, Shield + 1H/2H, mixed shield/weapon pool;
- Fashion Souls;
- Laundromat Mixup;
- randomize Lordvessel;
- randomize Lord Souls.

## Enemy parity extensions

- boss replacement: disabled, bosses, normal enemies, both;
- normal enemy replacement: disabled, bosses, normal enemies, both;
- NPC replacement: disabled, bosses, normal enemies, both;
- placement fit mode;
- difficulty mode, including easy-Asylum variants;
- replacement percentage;
- boss probability for normal destinations;
- boss probability for boss destinations;
- mimic replacement;
- second Gargoyle replacement;
- difficulty strictness;
- T-pose behavior;
- roaming boss soul percentage;
- type replacement;
- Pinwheel Chaos;
- Gwyn spawn nerf;
- prevent replacement with the same enemy;
- unique bosses;
- roaming boss respawn/permadeath;
- hostile NPCs;
- respawning mosquito replacement;
- custom enemy pool.

## FogMod options

The UI must expose every DS1 FogMod setting:

- traversable non-boss fog gates;
- boss fog gates;
- warps between areas;
- major PvP fog gates;
- minor PvP fog gates;
- Lordvessel gates;
- random start outside the Asylum;
- disconnected fog gates;
- no-fall Bed of Chaos;
- enemy and boss scaling;
- glitched logic;
- require Lord Souls;
- Pacifist Mode;
- game language.

FogMod's internal version marker and dry-run flag are backend concerns, not
user-facing gameplay settings.

## Support states

Each option shown by the UI must have one explicit support state. Planned
options are UI metadata and must not be part of an executable run request:

- `native`: implemented by the pinned upstream engine;
- `extension`: implemented and tested in our GPL fork;
- `planned`: visible as roadmap information but cannot be enabled;
- `incompatible`: blocked with an explanation for the selected combination.

The backend must reject configurations containing unsupported options. It must
never silently ignore or approximate them.
