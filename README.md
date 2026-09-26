# DS1 Triple Randomizer

Early integration scaffold for a single Dark Souls Remastered randomizer pipeline combining:

- DarkSoulsItemRandomizer
- Dark-Souls-Enemy-Randomizer
- FogMod

## Architecture

The UI is deliberately isolated from randomizer implementation details.

```text
Electron shell
  └─ React + TypeScript renderer
       └─ HTTP (127.0.0.1:43177)
            └─ Node/TypeScript backend
                 ├─ pipeline / validation / backups / seeds
                 ├─ Item adapter -> Python engine
                 ├─ Enemy adapter -> Python engine
                 └─ Fog adapter -> C# engine
```

The React renderer has no filesystem or Python/C# access. Electron preload is used only for desktop OS capabilities such as the folder picker. All randomization logic belongs behind the backend API.

## Current v0.1 status

Implemented:

- Electron + React + TypeScript + Vite frontend scaffold.
- Separate Node/TypeScript local backend.
- Shared TypeScript contracts.
- DS1R installation validation endpoint.
- Deterministic master-seed derivation for Items / Enemies / Fog.
- Pipeline preview with fixed order: Items -> Enemies -> Fog.
- Adapter abstraction for three upstream engines.
- Initial UI for common options.

Not implemented yet:

- Upstream engine source/binaries are intentionally not vendored yet.
- Item Python bridge.
- Enemy Python bridge/refactor away from Tkinter dependencies.
- Fog C# host/CLI patch.
- Transactional workspace + backup manager.
- Cross-engine validation/retry logic.
- Final installation of generated files into the game directory.

## Why the upstream source is not included yet

FogMod includes an Apache-2.0 license. The Item and Enemy repositories do not expose a root `LICENSE` file in the repositories checked during the initial analysis. Until redistribution terms are clarified, this scaffold keeps upstream engines external and uses adapter boundaries instead of copying their source into this repository.

## Development

Requirements:

- Node.js 22+
- npm
- Windows for Electron + final DS1R integration
- Python version compatible with the old Python randomizers (to be pinned after adapter work)
- .NET tooling for the FogMod host

Install dependencies:

```bash
npm install
```

Start frontend + backend:

```bash
npm run dev
```

Backend API:

- `GET /api/health`
- `POST /api/installation/validate`
- `POST /api/pipeline/plan`
- `POST /api/randomize` (intentionally returns 501 until the adapters are connected)

## Project layout

```text
apps/
  backend/      pipeline + adapters + local API
  desktop/      Electron + React frontend
packages/
  contracts/    shared request/response types
engines/
  item/         external Item Randomizer integration point
  enemy/        external Enemy Randomizer integration point
  fog/          external FogMod integration point
docs/
  source-analysis.md
  roadmap.md
```
