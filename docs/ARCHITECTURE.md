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
        Multi Randomizer Core
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

The combined engine receives a named SHA-256 derivation of the master seed:
the first 32 lowercase hex characters of
`SHA-256("ds1-triple-randomizer:v1:<trimmed master seed>:combined")`. This
satisfies upstream's `/^[\w.-]{1,64}$/` seed charset for any free-text master
seed. The combined engine owns its versioned internal item/enemy streams from
that token. Derive Fog and retry streams in the application with stable
SHA-256 namespaces. Record both derivation versions.

> Superseded (2026-09-28, Task 6): this section previously said to pass the
> normalized master seed directly to the combined engine. See
> `docs/DECISIONS.md` — "The combined-engine seed is a named SHA-256
> derivation of the master seed".

## Seed Codes and generation profiles (backend 0.30.0)
The user-visible identity of a world is a **Seed Code** (`DS1TR1-...`), not the master seed. The code carries the generation
profile id, the internal master seed (the derivation above runs from it unchanged) and the complete canonical settings
(`docs/SEED_FORMAT.md`). The backend owns encode/decode, settings persistence and run history; the renderer only displays
them. A **generation profile** (`ds1tr-gen-v1`, `apps/backend/src/core/generationProfiles.ts`) is an immutable compatibility
contract naming the exact deterministic components (seed derivation, retry bounds, combined engine version, FogHost/FogMod
commit/world data, item tracking rules); the application version is not part of it and is only recorded in the manifest. The
pipeline takes derivation and bounds from the profile and stops with `GENERATION_PROFILE_MISMATCH` if an engine it ran is not on
the profile's list. Rules for when a new profile is required: `docs/GENERATION_COMPATIBILITY.md`; permanent golden fixtures:
`apps/backend/fixtures/golden/`.

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
      "inputSeed": "<32-hex combined derivation of masterSeed>",
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

## Language, hints, diagnostics and the support bundle (desktop 0.32, backend 0.44, contracts 0.30)

* **Language** (`docs/I18N.md`): the main process resolves the language (saved choice, else the operating system) before the first render; the renderer uses i18next with bundled `locales/en|pl/*.json` (English canonical, type-checked keys); the backend sends stable codes and structured keys, never sentences to be parsed. The language is not part of the Seed Code, the settings, any hash or the history's world identity.
* **Hint sheet** (`docs/HINT_SHEET.md`): generation writes `hints.json` once next to the run (never into the package); `filterHints` in the contracts is the one pure spoiler filter used by the viewer and the three exports; exports are built with the export language's resources and saved through a main-process dialog.
* **Diagnostics and logging** (`docs/DIAGNOSTICS.md`): structured JSON logs with stages and a correlation id per operation, operation records that survive failures, one `Diagnostics` model shared by the backend, the interface and the bundle.
* **Support bundle** (`docs/SUPPORT_BUNDLE.md`): an allowlist of named entries, redaction (known values and patterns), a leak scan over the finished content, size limits and a ZIP read-back; works after failed generation or Apply and with no world.
* **Data ownership:** the manifest, the placements document, `hints.json`, `diagnostics/last-run.json` and the logs are written once by the backend; the interface only reads them (through the API) and never re-reads game files.

## DLC content switch (backend 0.45.0, contracts 0.31.0, desktop 0.33.0)

`output.includeDlc` (default `true`) decides whether the DLC map (`m12_01_00_00`) takes part in the randomization. The renderer only offers the switch (locked on while the fog randomizer is on). The backend validates the combination (`configValidation`), passes `includeDlc` to the combined adapter, and the adapter bridge filters the scanned catalog (`adapters/dlcExclusion.ts`) before `core.generate`; with the switch on the catalog is passed through untouched. The engine and the data tool's writing are unchanged: what the engine does not know, it cannot place, and the data tool writes only what was placed, so the DLC files stay byte-identical to the clean game. Details, the audit of every subsystem and the evidence: `docs/DLC_AUDIT.md`; the decision: `docs/DECISIONS.md`.
