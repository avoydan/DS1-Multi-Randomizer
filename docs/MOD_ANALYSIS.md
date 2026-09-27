# Mod Analysis

## Summary
The project should no longer assume that the three original randomizers must all be integrated directly.

A newer combined project, `kellernz1/ds-randomizer`, is the approved primary
Items + Enemies engine, subject to the Fog compatibility gate.

---

## 1. Newer combined DSR randomizer
Repository:
`kellernz1/ds-randomizer`

### Confirmed documented capabilities
- regular enemy randomization
- boss randomization
- world item randomization
- enemy/NPC drops
- gifts
- shops
- starting classes/equipment
- progression safeguards
- reproducible seeds
- spoiler/report generation
- safe package activation
- vanilla restoration

### Technology
Observed structure:

```text
src/
  cli.js
  server.js
  core/
    config.js
    generator.js
    output.js
    rng.js
    share.js
  desktop/
    main.cjs
    preload.cjs
  game/
    detection.js

tools/
  DsrDataTool/
```

`package.json` shows:
- Node.js >= 20
- Electron
- electron-builder
- .NET 8 build step
- automated tests
- seed testing
- portable Windows build

This is much closer to our target architecture than either legacy Python tool.

### License
Repository declares GPL-3.0-only.
Its third-party notices also identify GPL-licensed SoulsFormats-derived components.

Do not copy this code into a differently licensed proprietary codebase without reviewing GPL obligations.

### Approved role
Primary engine for:
- item engine
- enemy engine
- game data handling
- deterministic generation
- package activation/restore concepts

Missing legacy-visible behavior will be implemented independently in the GPL
fork after the Fog compatibility spike.

---

## 2. FogMod
Repository:
`thefifthmatt/FogMod`

### Technology
C#/.NET with WinForms frontend.

### Key finding
Core engine is separable from GUI.

Relevant files include:
- Program.cs
- Randomizer.cs
- RandomizerOptions.cs
- GameDataWriter.cs
- graph/connectivity logic
- item reading logic

The engine exposes a `Randomize(...)` method that can be called without WinForms.

### CLI caveat
Inspected `Program.cs` contains suspicious game-selection behavior where the selected game appears to be overwritten with DS3 in the command-line path.

Therefore existing CLI should not be trusted as our integration boundary without testing.

### Recommended role
Create a small C# host around the engine with structured JSON input/output.

Do not rewrite FogMod in JS.

### License
Apache-2.0 license file is present.

---

## 3. Legacy DarkSoulsItemRandomizer
Repository:
`HotPocketRemix/DarkSoulsItemRandomizer`

### Technology
Python + Tkinter.

### Structure
Relatively modular.

Important modules:
- randomize_item_table.py
- item_table.py
- item_lot_param.py
- shop_lineup_param.py
- chr_init_param.py
- items_setup.py
- key_items_setup.py
- locations_setup.py
- shops_setup.py
- bnd_rebuilder.py
- dcx_handler.py

### Integration implications
Use this project only as a behavior and option reference. Do not copy, modify,
or redistribute its source without written permission.

### Current role
Behavior and option reference only.

### License caveat
No clear root LICENSE file was found during initial inspection.
Public GitHub source does not imply unrestricted redistribution.

---

## 4. Legacy Dark-Souls-Enemy-Randomizer
Repository:
`everKalle/Dark-Souls-Enemy-Randomizer`

### Technology
Python with Tkinter dependencies.

### Key finding
A `Randomizer` class exists, but it is not a clean library.

Observed issues:
- Tkinter imports in core module
- direct filesystem assumptions
- working-directory dependence
- executable checks
- setup work in constructor
- copies/backups
- `.bak` restore logic
- direct MSB/Lua/event modifications
- engine, environment and UI concerns mixed together

### Integration implications
Do not wrap it blindly.

Use this project only as a behavior and option reference. Do not copy, modify,
or redistribute its source without written permission.

### Current role
Behavior and option reference only.

### License caveat
No clear root LICENSE file was found during initial inspection.

---

## 5. Problem we are solving

Legacy workflow is fragile because each tool tends to assume ownership of game state.

Typical pain:

```text
Item randomizer
-> Enemy randomizer
-> Fog randomizer
-> failure
-> unclear backups
-> manual retries
```

Target workflow:

```text
one master seed
one controlled workspace
one backup authority
automatic validation
deterministic retry
one final activation
```

---

## 6. Revised decision

OLD:
```text
legacy Item adapter
+ legacy Enemy adapter
+ Fog adapter
```

APPROVED:
```text
combined DSR randomizer core
(items + enemies)
+ FogMod adapter
```

Legacy Item/Enemy projects remain:
- behavior references
- parity sources

The remaining gate is combined-to-Fog compatibility, not Python integration.

---

## 7. Integration gates

1. Create and pin the public GPL fork.
2. Add a stable headless boundary without adopting the upstream UI/server.
3. Preserve combined event changes through FogMod.
4. Pass explicit key-location data into Fog compatibility logic.
5. Validate 100-1000 deterministic seeds on a clean DSR workspace.
6. Implement legacy parity options independently and expose each only after its
   behavior and compatibility tests pass.
