# AGENTS.md

## Project
DS1 Triple Randomizer is a Windows desktop application for Dark Souls Remastered.

Goal: one reliable application combining item randomization, enemy/boss randomization, and fog/world randomization without manually chaining three tools.

## Product rules
- Dark Souls Remastered first. PTDE is out of scope for the first release.
- One master seed.
- Deterministic, reproducible runs.
- One backup/restore authority.
- Automatic compatibility handling and retries.
- No automation of legacy GUIs when callable engine code exists.
- Source code, identifiers, comments, and logs should be English.
- User-facing communication may be Polish.

## Frontend
Required stack:
- Electron
- React
- TypeScript
- Vite

Frontend must be separate from backend/randomization logic.

Renderer must NOT:
- modify game files directly
- import Python modules
- call FogMod classes directly
- own backup/restore
- contain randomization algorithms

## Backend
Backend owns:
- DSR verification
- pristine data import
- workspace management
- seed derivation
- pipeline orchestration
- adapter execution
- validation
- retries
- activation
- restore
- logs
- run manifests

Logical flow:

UI -> typed boundary -> application/backend -> randomization engines -> validation -> activation

## Updated engine strategy
Do NOT assume all three legacy projects must be embedded directly.

Primary candidate for ITEMS + ENEMIES:
`kellernz1/ds-randomizer`

Why:
- DSR support
- item randomization
- enemy/boss randomization
- deterministic seeds
- progression safeguards
- spoiler output
- vanilla restoration
- Electron packaging
- Node.js core
- .NET data tool

Primary candidate for FOG:
`thefifthmatt/FogMod`

Legacy behavior references only:
- HotPocketRemix/DarkSoulsItemRandomizer
- everKalle/Dark-Souls-Enemy-Randomizer

Do not copy, modify, vendor, or redistribute the legacy Python engines without
written permission from their copyright holders. Missing behavior must be
implemented independently in the GPL combined fork.

## FogMod findings
- C# project.
- Real engine is separate from WinForms.
- `Randomizer.Randomize(...)` can be invoked without GUI.
- `RandomizerOptions` is separate.
- Apache-2.0 license is present.
- Existing `Program.cs` CLI path has suspicious DS1/DS1R handling and should not be trusted without testing.
- Preferred integration: a small dedicated C# Fog host with JSON input/output.
- Do not rewrite FogMod in JavaScript.

## Legacy Enemy Randomizer findings
The legacy Enemy Randomizer has a `Randomizer` class, but core code is side-effect heavy.

Known issues:
- Tkinter imports in core module
- constructor performs environment checks and setup
- current-working-directory assumptions
- internal backups/copies
- direct game-file writes
- UI/logging concerns mixed with engine logic

Use this project only as a behavior and option reference. Do not refactor or
reuse its source without written permission.

## Legacy Item Randomizer findings
More modular than legacy Enemy Randomizer.

Useful engine modules include:
- randomize_item_table.py
- item_table.py
- item_lot_param.py
- shop_lineup_param.py
- chr_init_param.py
- items_setup.py
- key_items_setup.py
- locations_setup.py
- shops_setup.py

Treat documented behavior as a compatibility requirement. Do not copy or adapt
the implementation without written permission.

## Backup policy
There must be ONE backup authority.

Preferred model:
clean install -> pristine snapshot/import -> run workspace -> generation -> validation -> generated package -> activation

Do not allow independent authoritative `.bak` chains from legacy tools.

## Workspace policy
Avoid modifying the live DSR installation during intermediate generation.

Generate into a controlled workspace where possible.
Only activate after successful validation.

## Seed policy
The user sees ONE master seed.

Use stable named derivation such as:
- SHA256(master + ":items")
- SHA256(master + ":enemies")
- SHA256(master + ":fog")

Do not use Python `hash()`, .NET `GetHashCode()`, or unstable runtime hashes.

Manifest must record:
- master seed
- derived seeds
- app version
- engine versions
- options
- retry attempts
- validation results
- generated files

## Retry policy
Fog/world generation may reject upstream randomized state.

Retries must:
- be automatic where safe
- be deterministic
- be bounded
- be recorded in the manifest
- expose a clear reason

## API / boundary
Use typed DTOs.

Possible operations:
- verify game
- import pristine data
- generate run
- activate run
- restore vanilla
- get run status/logs
- import/export preset

Long operations should expose progress through typed Electron IPC, WebSocket, or SSE.

## Safety
Before activation:
- verify DSR directory
- verify `DarkSoulsRemastered.exe`
- verify expected data paths
- ensure a pristine restore source exists
- refuse destructive actions when state is unknown

Recommend offline play for modified game data.

## Before major Codex changes
1. Read this file.
2. Read `docs/ARCHITECTURE.md`.
3. Read `docs/MOD_ANALYSIS.md`.
4. Inspect current repo state.
5. Run build/tests.
6. Explain architecture-changing decisions.
7. Do not rewrite working engines merely for language consistency.
