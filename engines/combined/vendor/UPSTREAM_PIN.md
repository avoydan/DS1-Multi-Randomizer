# Upstream pin

Source: https://github.com/kellernz1/ds-randomizer
Pinned commit: `f120efe0e95b957b7e76109868c3c9966e22720c`
Commit date: 2026-08-20T16:13:04-03:00
Commit subject: "Simplify README and add Nexus download"
License: GPL-3.0-only (see `LICENSE` in this directory)

This is a **selective** vendor, not a full mirror. Two groups of files are
vendored, per `docs/COMBINED_ENGINE_INTEGRATION.md`:

1. the files required to build `DsrDataTool.dll` (the game-data
   extraction/patch tool);
2. the minimal JavaScript generation core required to call
   `generate(config, { gameCatalog })` and `writeOutput(result, dir)`
   in-process (`src/core/*.js` plus the one data module `generator.js`
   statically imports).

Electron, CLI, HTTP-server, and share/link layers are still excluded.

## Vendored paths

| Vendored path | Upstream path | Purpose |
|---|---|---|
| `LICENSE` | `LICENSE` | GPL-3.0-only license text (must be preserved). |
| `THIRD_PARTY_NOTICES.md` | `THIRD_PARTY_NOTICES.md` | Upstream's own third-party notice (`JuicerMV.SoulsFormats`, GPL-3.0-only) — must be preserved and mirrored into this repo's own `THIRD_PARTY_NOTICES.md`. |
| `NuGet.Config` | `NuGet.Config` | Pins package restore to `nuget.org` only, matching upstream's reproducible-build configuration. |
| `scripts/install-dotnet.ps1` | `scripts/install-dotnet.ps1` | Installs .NET 8 SDK locally into `.tools/dotnet` under this vendor directory (no admin rights, no global `PATH` change, no system-wide install). |
| `scripts/dotnet-local.ps1` | `scripts/dotnet-local.ps1` | Thin wrapper that runs the locally-installed `dotnet.exe` with isolated `DOTNET_CLI_HOME`/`NUGET_PACKAGES` environment variables, so builds never touch the developer machine's global NuGet cache or dotnet CLI state. |
| `tools/DsrDataTool/DsrDataTool.csproj` | `tools/DsrDataTool/DsrDataTool.csproj` | The .NET 8 project file for the data tool (`net8.0`, single dependency: `JuicerMV.SoulsFormats` 0.1.3). |
| `tools/DsrDataTool/Program.cs` | `tools/DsrDataTool/Program.cs` | The data tool's full implementation (`scan`, `patch-enemies`, `install`, `restore`, `inspect-boss-bars` commands). Vendored in full because the project file only builds if the source is present; `install`/`restore` remain in the binary but are never invoked by this repository (see `docs/COMBINED_ENGINE_INTEGRATION.md`, "Forbidden upstream calls"). |
| `src/core/config.js` | `src/core/config.js` | `normalizeConfig`, `validateConfig`, `defaultConfig`, `RANDOMIZER_VERSION` (`0.5.66`). |
| `src/core/generator.js` | `src/core/generator.js` | The pure generation entry point `generate(config, { gameCatalog })`. No filesystem, network, or clock access. |
| `src/core/output.js` | `src/core/output.js` | `writeOutput(result, baseDirectory)` — writes `seed_<safeSeed>/{randomizer.json, config.json, README.txt, spoiler.txt?, cheat-locations.txt?}` and returns that directory. Vendored because `patch-enemies --placements` consumes exactly this layout, which the committed workspace planner already encodes. |
| `src/core/rng.js` | `src/core/rng.js` | Deterministic seed derivation/streams. Vendored **only** because `generator.js` statically imports it; never imported directly by this repository. |
| `src/data/prototype-data.js` | `src/data/prototype-data.js` | Placeholder enemy/item data. Vendored **only** because `generator.js` statically imports it. It is never the source of an accepted run: a result with `dataStatus !== "extracted"` must be rejected (`docs/COMBINED_ENGINE_INTEGRATION.md`, Blocker 1). |
| `package.json` | *(repository-authored — no upstream counterpart)* | Minimal ESM scope marker (`"type": "module"`) plus the upstream engine version, so Node loads the vendored `src/**/*.js` as ES modules instead of reparsing them as typeless CommonJS. Upstream's own `package.json` is deliberately **not** vendored: it carries Electron/`electron-builder` configuration that is out of scope. Not covered by the byte-identity verification below. |

## Not vendored (and why)

- `src/cli.js`, `src/server.js`, `src/desktop/**` — CLI, HTTP, and Electron
  orchestration layers. Excluded per `AGENTS.md` ("No automation of legacy
  GUIs when callable engine code exists"); `src/cli.js` additionally
  hard-codes its own catalog and `dotnet` paths relative to the upstream
  package root, which this repository does not reproduce.
- `src/core/share.js` — seed share/import links for upstream's own UI. It
  imports `generate`, so it would pull nothing new in, but no part of this
  repository calls it.
- `src/data/catalog.js`, `src/game/detection.js` — catalog loading and game
  install detection/staleness verification. Not in `generate()`'s import
  graph; catalog handling is a separate, still-open concern (see
  `docs/COMBINED_ENGINE_INTEGRATION.md`, Blocker 6).
- `public/**`, upstream `package.json`, `package-lock.json` — Electron/browser
  configurator UI and build configuration; out of scope. (A minimal
  repository-authored `package.json` supplies only the `"type": "module"`
  declaration — see the table above.)
- `test/**` — upstream's own test suite; not exercised here.
- `README.md` — informational only, not a license or notice file.
- `data/dsr-catalog.json` — gitignored upstream, and
  `docs/LICENSING.md` forbids distributing extracted game catalogs. This
  repository's `.gitignore` must keep excluding it.


## Verification method and recorded blob identities

`core.autocrlf` is enabled on the development machine, so vendored files are
checked out with CRLF line endings even though upstream stores them with LF.
A raw `diff -q` against a fresh clone is therefore **not** a reliable
byte-identity check on Windows. Verification instead compares Git blob
object IDs, which are computed on the normalized (LF) content that this
repository actually stores and distributes:

```sh
git -C <clone-of-pinned-commit> rev-parse <pin>:<upstream path>
git hash-object -- engines/combined/vendor/<vendored path>
```

Both must print the same object ID. Recorded upstream blob IDs at pinned
commit `f120efe0e95b957b7e76109868c3c9966e22720c` (all twelve verified
equal to this repository's stored blobs):

| Upstream path | Blob object ID |
|---|---|
| `LICENSE` | `fa5de3594a70344c0687270e5a5af7f84f7bbc83` |
| `THIRD_PARTY_NOTICES.md` | `6ab889fcded91dc98e2e1a05f6eb3fe37c9aada9` |
| `NuGet.Config` | `765346e5343d46ac83654843869dc06b65b2bdc4` |
| `scripts/install-dotnet.ps1` | `009d49c3a1547d6810d4c4f2b54344c986f621f4` |
| `scripts/dotnet-local.ps1` | `78ef46ac464f1fd12c893f72b5352b5f3a03b122` |
| `tools/DsrDataTool/DsrDataTool.csproj` | `65d5aeb15ded3248683d49af92f5baec9479a10b` |
| `tools/DsrDataTool/Program.cs` | `0e5842a8d6d72a07d9236d4718378e30051203ee` |
| `src/core/config.js` | `58c240a191af814fc16240fb16a94e5cebf5789f` |
| `src/core/generator.js` | `449992fb318121a0b7b6bfdc664c1d6c1b9fad3e` |
| `src/core/output.js` | `a13b892fe9a3d3382e86d900d56bbd6deb56cc4c` |
| `src/core/rng.js` | `e0f8dee6a7d2369040858bb9b6efebeb64e2d908` |
| `src/data/prototype-data.js` | `992b81eb69c710749643aa0db1b38806537a0418` |

Vendored files are not edited in place, except for the local modifications
listed below. Until the public GPL fork exists (see "Known gap"), a required
correctness fix may be applied here. It must carry a GPL-3.0 §5(a) notice in
the file, be listed below, and have its diff kept under `patches/` so it can
be applied to the fork. A modified file no longer matches its blob ID above;
the ID records the unmodified upstream bytes.

## Local modifications

| File | Change | Patch | Tests |
|---|---|---|---|
| `tools/DsrDataTool/Program.cs` (upstream blob `0e5842a8…`) | 2026-09-28, user-authorized. `InsertEventInstruction`/`RemoveEventInstruction` keep DS1 "skip N lines" counts (`1000[01/03/05/07]`, `1003[01/03/05/07]`, `1005[01]`) consistent with instruction-count-changing edits. Insertions now state an anchor (`InsertAnchor.Before`/`After`); anchor ambiguity, removal of a skip, parameterized counts and overflow throw. Upstream deleted instructions inside skip ranges without adjusting counts, so skips covered different instructions (38 skip ranges in 21 events of one real-format package). | `patches/0001-dsrdatatool-skip-ranges.patch` | `engines/combined/tests/DsrDataTool.Tests` (unit tests and `verify-skips`) |
| `tools/DsrDataTool/EventSkipRanges.cs` | New, repository-authored (GPL-3.0-only), holds the logic above. | same | same |
| `src/core/config.js` (upstream blob `58c240a1…`), `src/core/generator.js` (upstream blob `449992fb…`) | 2026-10-05, legacy-parity Batch 1 (a feature extension, authorized by the parity task; see `docs/DECISIONS.md`). New config keys `fashionSouls` (default on), `replaceMimics` (on), `replaceRespawningMosquitoes` (on), `preventSameEnemy` (off). With every key at its default the output is byte-identical to upstream (placement hashes compared over 24 seeds, three configurations, before and after). Off/on behavior: Mimic slots (model `c2780`) and the six respawning Blighttown mosquito slots stay vanilla and leave the enemy pool; `separateSameModelSources` removes same-model replacements after the map model budget; complete world armor sets are exchanged between same-size set locations (`findArmorSetHomes`, `permutedArmorSets`). A GPL-3.0 §5(a) notice is at the top of `generator.js`. `RANDOMIZER_VERSION` is deliberately unchanged because it salts every seed stream. | `patches/0002-legacy-parity-batch-1.patch` | `apps/backend/src/adapters/combinedLegacyParity.test.mjs` |

## Updating this pin

1. Re-clone `kellernz1/ds-randomizer` at the new commit.
2. Re-copy only the paths listed above (`git cat-file blob <pin>:<path>`
   writes the exact upstream bytes), verifying each against the new
   clone's blob object IDs as described under "Verification method".
3. Update the commit hash/date/subject and the blob ID table above.
   Re-check `RANDOMIZER_VERSION` in `src/core/config.js`, which
   `apps/backend/src/adapters/combinedRandomizerCore.test.mjs` asserts.
4. Re-run `npm run test -w @ds1tr/backend`: the JS-core tests fail loudly
   if the vendored module set or its import graph changed.
5. Re-run the build in `docs/COMBINED_ENGINE_INTEGRATION.md` ("Vendored
   build proof") and confirm `DsrDataTool.dll` still produces successfully.
6. Record the change in `docs/DECISIONS.md` if the update changes any
   documented behavior (command names, file paths, schema version).

## Known gap

`docs/LICENSING.md` requires "a public source tag matching each
distributed binary." No public fork of `kellernz1/ds-randomizer` has been
created yet — that is an account-owning action (creating and maintaining a
fork under a project-controlled GitHub account) outside what this task
vendors locally. This vendor directory satisfies "exact pinned commit" and
"preserved upstream copyright/license notices" today; the public fork
remains open before any binary built from this vendor directory is
distributed to end users.
