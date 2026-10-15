# Windows desktop packaging

`npm run package:win:github -w @ds1tr/desktop` builds the contracts, backend and desktop, the current .NET 8 tools, and the pinned historical FogHost 0.5.0, stages an isolated runtime, and creates an **unsigned** x64 **installer** (NSIS, per-user, no administrator) and an unsigned **portable** executable under `apps/desktop/release/`, then verifies them (`tools/release/release-verify.mjs`). `npm run package:win` remains a GitHub-flavor alias. The separate `npm run package:win:nexus -w @ds1tr/desktop` target builds public-channel Nexus Setup and Portable artifacts under `apps/desktop/release/nexus/`, clears GitHub publishing metadata, and verifies that application updater networking is disabled. Neither target publishes or installs anything. Signing, the release mode (`-- --release`), the gate and what users will see are in `docs/RELEASE_TRUST.md`. Build prerequisites are Node/npm and the .NET 8 SDK (`DS1TR_DOTNET_SDK` may point to a specific `dotnet.exe`). The helpers are self-contained, so users need **no** .NET installation; the desktop itself does not need a separate Node installation.

The installer copies Electron and its `resources/runtime` payload to the install folder; the portable executable extracts them to a temporary folder at every start. The same payload is visible in `release/win-unpacked` for inspection. It includes:

- compiled backend and vendored combined JavaScript core;
- DsrDataTool and current FogHost 0.6.0 as **self-contained win-x64 single-file executables** (about 66 MB each, **not compressed** - a compressed bundle is one block of packer-like data and is decompressed at every start; see `docs/RELEASE_TRUST.md` section 3);
- frozen FogHost 0.5.0 at `resources/runtime/tools/fog-host-legacy/FogHost.exe`, rebuilt from the exact Git commit/tree in `tools/fog-host-legacy/source-pin.json` during packaging. The package build extracts this history-only source to a temporary directory, verifies its project and runtime versions, publishes it self-contained, smoke-starts it with .NET locations hidden, then removes the temporary source. No duplicate legacy source tree is maintained;
- the packager smoke-tests every helper with every .NET location hidden (no `DOTNET_ROOT`, a PATH that holds only System32) and fails the build if one does not start;
- optional `GameCursorHelper.exe`, a self-contained .NET 8 Windows watcher started only when its local app setting is enabled; the packager probes it with `--self-test`;
- `@ds1tr/contracts`, which the backend imports at run time. electron-builder never copies a `node_modules` folder through
  `extraResources`, so `scripts/stage-runtime-modules.cjs` (`afterPack`) places it in `resources/runtime/node_modules`;
- GPL, Apache and third-party notices plus upstream pins and the world randomizer data provenance under `runtime/licenses`.

It deliberately excludes game files, catalogs, generated packages and every file of FogMod's `dist` folder. The world
legacy v1/v2 profiles' 59 data files are prepared by the app (`apps/backend/src/adapters/worldData.ts`, inventory in
`docs/FOG_DATA_PROVENANCE.md`): 19 map and parameter files are copied from the verified clean snapshot of the player's own
installation, and the 40 FogMod-authored or FogMod-edited files are fetched once over HTTPS from the pinned FogMod commit,
SHA-256 verified, assembled in a temporary folder and moved into `userData/state/world-data/<id>` atomically. The player
selects no data folder; once the cache is valid, generation needs no network. FogHost still verifies the complete pinned
hash manifest during generation. `DS1TR_FOG_DATA_ROOT` remains a developer override.

Electron starts the backend as a child using its bundled Node mode, an argument vector, a random loopback port, and a per-session API token. It supplies packaged runtime paths through environment variables. The backend owns snapshots, runs, activation and restore under Electron's user-data directory, not the installation directory. The app stops the backend process tree at shutdown. The renderer only calls the typed backend API and selects directories through Electron IPC. The sandboxed preload is compiled as CommonJS (`preload.cts` to `preload.cjs`).

First run: the app looks for Dark Souls Remastered through Steam (registry, library folders and the game's app manifest), checks
it, imports the clean snapshot and, when the world randomizer is switched on, prepares its data, showing plain progress
("Dark Souls Remastered found", "World randomizer ready"). Only when the game cannot be found does it ask for the game
folder, and a choice is offered when several copies exist. Failures are phrased for players and the exact reason is kept
under Advanced and in the run manifest. A backend startup failure is shown in the app. The builds are unsigned; public GPL corresponding source and a matching release tag remain prerequisites before any external release.

The build uses a minimal staged Electron app with no runtime npm dependencies: React is bundled by Vite, and the backend is separate in `extraResources`. The full source repository and its pre-existing orchestrator files are not copied into the package.

The backend imports `@ds1tr/contracts` at run time. electron-builder never copies a `node_modules` directory through `extraResources`, so `scripts/stage-runtime-modules.cjs` (`afterPack`) places the staged contracts package into `resources/runtime/node_modules` before the portable target is built. Desktop 0.15.0 lacked it and its backend exited with `ERR_MODULE_NOT_FOUND`; 0.15.1 fixes that.

## Language, build information and the installer (desktop 0.32.x)

* `package-win.mjs` writes `runtime/build-info.json` (version, git commit, `development | public`, component versions, build time). The application reads it for the diagnostics (the signature state is derived from the mode: a public build is only made after the release verification demanded a trusted signature; a development build is unsigned).
* `electron-builder.yml` -> `nsis.installerLanguages: [en_US, pl_PL]`: the NSIS installer and uninstaller follow the Windows display language (Polish -> Polish, otherwise English); `displayLanguageSelector` stays off. Verified on this Polish Windows: the wizard opens in Polish ("Instalator DS1 Triple Randomizer", "Wybierz opcje instalacji", "Dalej >", "Anuluj"). The English wizard is the NSIS default for any other language and was not shown here (the Windows display language was not changed).
* The main process starts the backend with `DS1TR_USER_DATA`, `DS1TR_APP_LOG`, `DS1TR_DESKTOP_VERSION` and `DS1TR_INSTALL_MODE` (`installed | portable | development`, from `PORTABLE_EXECUTABLE_DIR`) for the diagnostics; none of them influences generation.

## Updates (desktop 0.37.x)

* `electron-updater` is the one runtime dependency of the shell. `beforeBuild` returns `false` (dependencies are not handled by electron-builder), and electron-builder's file walker never descends into a root `node_modules`, so `package-win.mjs` stages the lockfile-resolved closure into `release/package-app/dist-electron/node_modules` (`scripts/stage-dependencies.mjs`); it ends up inside `app.asar` and Node resolves it from `dist-electron/main.js`. `release-verify.mjs` fails a package whose asar lacks any of it.
* `electron-builder.yml` `publish:` (explicit `provider: github`, owner, repo) makes electron-builder write `resources/app-update.yml` and `latest.yml` + `.blockmap` next to the installer; nothing is uploaded by the builder (`--publish never`).
* Release steps: [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md); design and limits: [AUTO_UPDATE.md](AUTO_UPDATE.md).

## Versioned Fog runtime (desktop 0.39.0)

The Seed Code profile selects the Fog generation executable and request protocol; the app version and the presence of `fog.txt` do not select a host:

| Profile | Generation host | Wire schema | Packaged path |
|---|---|---:|---|
| `ds1tr-gen-v1` / `ds1tr-gen-v2` | FogHost 0.5.0 | 2 | `resources/runtime/tools/fog-host-legacy/FogHost.exe` |
| `ds1tr-gen-v3` | FogHost 0.6.0 | 3 | `resources/runtime/tools/fog-host/FogHost.exe` |

Both hosts are required package contents. The backend fails closed if the selected executable is missing; it never falls back across versions. Legacy profiles still require their complete hash-verified 59-file data cache. Nexus builds do not download or bundle it, so a v1/v2 Fog run needs that cache already available. V3 keeps the zero-download Compact path and reconstructs its 58 inputs from the player's verified local game snapshot.

FogHost 0.5.0 predates the read-only `progression-model` command. For legacy profiles that use key placement, FogHost 0.6.0 may export this model from the legacy 59-file data root; that helper does not generate or modify Fog outputs. The actual v1/v2 Fog generation request and output still use schema 2 and FogHost 0.5.0. Compact v3 data is never passed to the legacy host.
