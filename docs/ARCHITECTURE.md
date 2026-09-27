# Architecture

## Goal
One coherent Dark Souls Remastered randomizer providing:
- items
- enemies/bosses
- fog/world
- deterministic seeds
- safe activation/restore
- automatic compatibility handling

## Approved direction
Initial idea:

legacy Item + legacy Enemy + FogMod

Approved direction after additional research:

new combined DSR randomizer (items + enemies) + FogMod

The primary engine is a pinned GPL-3.0-only fork of
`kellernz1/ds-randomizer`.

It already overlaps heavily with infrastructure we planned to build.

## High-level architecture

```text
Electron + React + TypeScript + Vite
                |
                v
        typed application boundary
                |
                v
        Triple Randomizer Core
          /             \
         v               v
Combined DSR engine    Fog adapter
(items + enemies)      (FogMod C#)
          \             /
           v           v
            Validation
                |
                v
        Generated package
                |
                v
            Activation
```

Legacy Item/Enemy projects remain references/fallbacks.

## Frontend responsibilities
- game directory selection
- configuration
- master seed
- progress
- logs
- run history
- restore controls
- preset import/export
- spoiler log access

Frontend must not edit game files or implement randomization logic.

## Backend/application responsibilities
- installation verification
- pristine import/snapshot
- workspace creation
- seed derivation
- engine orchestration
- retry logic
- validation
- run manifest
- activation
- restore

## Suggested modules

```text
apps/
  desktop/
    electron/
    renderer/

packages/
  contracts/
  application/
  core/
  game-state/
  engines/
    combined/
    fog/
  infrastructure/

tools/
  fog-host/
```

Exact folders may differ; boundaries matter more than names.

## Generation lifecycle

```text
VERIFY INSTALLATION
        |
LOAD PRISTINE SOURCE
        |
CREATE RUN WORKSPACE
        |
DERIVE SEEDS
        |
GENERATE ITEMS + ENEMIES
        |
VALIDATE UPSTREAM DATA
        |
GENERATE FOG WORLD
        |
    success?
     /   \
   yes    no
    |      |
    |   classify failure
    |   deterministic retry
    +------+
        |
FINAL VALIDATION
        |
WRITE MANIFEST
        |
ACTIVATE PACKAGE
```

Activation should remain separate from generation if practical.

## Master seed
Use a stable cross-version algorithm, preferably SHA-256.

Pass the normalized user master seed directly to the combined engine. It owns
its versioned internal item/enemy streams. Derive Fog and retry streams in the
application with stable SHA-256 namespaces. Record both derivation versions.

## Retry derivation
Retries must remain reproducible.

Examples:
- `SHA256(master + ":combined-retry:" + attempt)`
- `SHA256(master + ":fog-retry:" + attempt)`

The manifest stores the attempt used.

## Run manifest
Conceptual schema:

```json
{
  "masterSeed": "123456",
  "appVersion": "0.2.0",
  "game": "DSR",
  "engines": {
    "combined": {
      "version": "...",
      "inputSeed": "123456",
      "attempt": 0
    },
    "fog": {
      "version": "...",
      "seed": 456,
      "attempt": 0
    }
  },
  "options": {},
  "validation": {},
  "files": []
}
```

## Fog integration
Do not automate WinForms.

Preferred host:

```text
FogHost.exe generate --request request.json --response response.json
```

or stdin/stdout JSON.

Host should:
- force DS1 Remastered explicitly
- build `RandomizerOptions`
- invoke FogMod engine
- emit structured success/failure data
- preserve raw diagnostics in backend logs

## Backup/restore
Application-level concepts:
- PristineSnapshot
- RunWorkspace
- GeneratedPackage
- ActivePackage

Legacy adapter backups, if unavoidable, are temporary implementation details only.

## Migration from current prototype
Do not discard a working frontend/backend split.

Instead:
1. keep React/Vite/Electron structure
2. keep typed contracts
3. add one `CombinedRandomizerAdapter` for both capabilities
4. pin a GPL fork of `kellernz1/ds-randomizer`
5. keep legacy Python projects as behavior references only
6. build FogHost separately
7. decide after feature-parity testing

## First meaningful milestone
1. Electron + React app starts.
2. DSR path can be selected and verified.
3. pristine game data can be imported safely.
4. master seed is deterministic.
5. combined item/enemy engine generates into a workspace.
6. run manifest is created.
7. live game files remain untouched during generation.

Then integrate FogMod.
