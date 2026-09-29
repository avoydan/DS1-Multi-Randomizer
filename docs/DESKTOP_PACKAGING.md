# Windows desktop packaging

`npm run package:win -w @ds1tr/desktop` builds the contracts, backend and desktop, builds the two .NET 8 tools, stages an isolated runtime, and creates an unsigned x64 portable executable under `apps/desktop/release/`. It does not publish or install anything. Build prerequisites are Node/npm and the .NET 8 SDK (`DS1TR_DOTNET_SDK` may point to a specific `dotnet.exe`). The package uses framework-dependent .NET 8 binaries because the Windows self-contained runtime packs were unavailable in this build environment. Users need the **.NET 8 runtime** installed. The desktop itself does not need a separate Node installation.

The portable executable extracts Electron and its `resources/runtime` payload. The same payload is visible in `release/win-unpacked` for inspection. It includes:

- compiled backend and vendored combined JavaScript core;
- DsrDataTool and FogHost executables, runtime configuration, and managed dependencies;
- GPL, Apache and third-party notices plus upstream pins under `runtime/licenses`.

It deliberately excludes FogMod's game-derived `dist/DS1R` data, game files, catalogs, and generated packages. Before a Fog run, select the user-provided FogMod 6e6dc78 data root in the desktop app. The picker checks the required `dist/DS1R/event` and `dist/DS1R/map/MapStudio` directories; FogHost checks the complete pinned file hash manifest during generation.

Electron starts the backend as a child using its bundled Node mode, an argument vector, a random loopback port, and a per-session API token. It supplies packaged runtime paths through environment variables. The backend owns snapshots, runs, activation and restore under Electron's user-data directory, not the installation directory. The app stops the backend process tree at shutdown. The renderer only calls the typed backend API and selects directories through Electron IPC. The sandboxed preload is compiled as CommonJS (`preload.cts` to `preload.cjs`).

First run needs no DSR installation. Select the DSR directory and validate it before import. Missing DSR paths return explicit validation problems; missing Fog data returns a visible prerequisite message. A backend startup failure is shown in the app. The executable is unsigned and currently uses the default Electron icon; public GPL corresponding source and a matching release tag remain prerequisites before any external release.

The build uses a minimal staged Electron app with no runtime npm dependencies: React is bundled by Vite, and the backend is separate in `extraResources`. The full source repository and its pre-existing orchestrator files are not copied into the package.
