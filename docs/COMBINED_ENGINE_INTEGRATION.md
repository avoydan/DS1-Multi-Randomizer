# Combined engine execution integration

Scope: execution integration only, for `kellernz1/ds-randomizer` behind
`CombinedRandomizerEngine.generate(...)`
([apps/backend/src/adapters/combinedRandomizerAdapter.ts](../apps/backend/src/adapters/combinedRandomizerAdapter.ts)).
This does not repeat the feature-parity analysis in
[docs/MOD_ANALYSIS.md](MOD_ANALYSIS.md) or
[docs/source-analysis.md](source-analysis.md).

Analyzed against upstream commit `f120efe0e95b957b7e76109868c3c9966e22720c`
(2026-08-20), originally cloned read-only for this analysis. Upstream files
have since been selectively vendored at that exact commit under
`engines/combined/vendor/` — see "Vendored build proof" and "JS core vendor
proof" below, and `engines/combined/vendor/UPSTREAM_PIN.md` for the file
list.

## Upstream files/functions/entry points

| Concern | File | Entry point |
|---|---|---|
| Config normalization/validation | `src/core/config.js` | `normalizeConfig(input)`, `validateConfig(config, { requireGame })`, `defaultConfig`, `RANDOMIZER_VERSION` |
| Generation (pure) | `src/core/generator.js` | `generate(config, { gameCatalog })` |
| Package writing | `src/core/output.js` | `writeOutput(result, baseDirectory)` |
| Deterministic RNG | `src/core/rng.js` | `deriveSeed`, `createStream` (internal to `generate`, not called directly) |
| Game data extraction / patch / activation | `tools/DsrDataTool/Program.cs` | CLI commands `scan`, `patch-enemies`, `install`, `restore`, `inspect-boss-bars` |
| Game install detection | `src/game/detection.js` | `detectGame(gameDirectory)`, `verifyCatalogSources(gameDirectory, catalog)` |
| Catalog loading | `src/data/catalog.js` | `loadGameCatalog(catalogPath)` |
| CLI orchestration (reference only, not imported) | `src/cli.js` | `generate` command wires the four pieces above |
| HTTP/IPC orchestration (reference only, not imported) | `src/server.js` | `runDataTool`, `api()`, `dispatchApi()` |

`src/core/{config,generator,output,rng}.js` are dependency-free ESM
(`node:crypto`, `node:fs/promises`, `node:path` only — verified via
`package.json`, which lists `electron`/`electron-builder` as the only
`devDependencies`, used solely by `src/desktop/*` and the `dist:win`
script). `generate()` performs no filesystem or network I/O and is
synchronous.

## Answers to the execution questions

1. **Narrowest callable entry point**: `generate(config, { gameCatalog })` in
   `src/core/generator.js`. It is pure — same `(config, gameCatalog)` in,
   same result out, no I/O, no timers, no randomness beyond the seed.
2. **Runs without Electron UI**: yes. `src/core/*.js` never imports
   `electron`, `src/desktop/*`, or `src/server.js`. `package.json`'s `main`
   points at the Electron entry (`src/desktop/main.cjs`), so importing must
   use deep paths (`ds-randomizer/src/core/generator.js`, not the package
   root).
3. **Generates into an arbitrary workspace**: yes, and it is enforced, not
   just possible.
   - `writeOutput(result, baseDirectory)` accepts any directory.
   - `DsrDataTool patch-enemies --output <dir>` explicitly rejects an
     `<dir>` that resolves inside `--game` (`PatchEnemies`, checked before
     any write): `Path.GetRelativePath(gameDirectory, outputDirectory)` must
     start with `..` or be rooted elsewhere, or it throws
     `"Patch output cannot be placed inside the game installation."`
   - `DsrDataTool scan --output <catalog.json>` also accepts any path.
4. **Generation separable from activation/install**: yes, at the process
   level. `scan` and `patch-enemies` never write under `--game`.
   `install`/`restore` are separate commands that are the only ones that
   write into `--game`, and only they call `EnsureGameClosed()`. Do not
   call `install`/`restore` — see [Blockers](#blockers).
5. **Exact inputs generation requires**:
   - `config`: an object matching `normalizeConfig`'s key set (seed,
     `gameDirectory` only used by later patch steps — not read by
     `generate` itself, `outputDirectory`, the boolean randomize-* flags,
     `enemyScaling`, `includeDlc`, `generateSpoilerLog`, `useExtractedData`,
     `dryRun`). Unknown keys are ignored; missing keys fall back to
     `defaultConfig`. `config.seed` must match `/^[\w.-]{1,64}$/`, or
     `validateConfig` reports it. **Correction (2026-09-28, verified
     against the vendored source):** `validateConfig` *returns* a
     `string[]` of human-readable errors and never throws — callers must
     check the returned array, not catch an exception.
   - `gameCatalog`: a JSON object with `schemaVersion === 17` and non-empty
     `enemySlots`/`enemyArchetypes` arrays, produced only by
     `DsrDataTool scan`. **If omitted or incomplete, `generate` does not
     error — it silently falls back to `dataStatus: "prototype"` and
     fabricated placements.** See [Blockers](#blockers).
6. **Exact files/directories read**:
   - `DsrDataTool scan --game <dir>`: `<dir>/DarkSoulsRemastered.exe`
     (existence + SHA-256, required), `<dir>/map/MapStudio/*.msb`
     (required directory), `<dir>/event/*.emevd.dcx`,
     `<dir>/param/GameParam/GameParam.parambnd.dcx`,
     `<dir>/paramdef/paramdef.paramdefbnd.dcx`,
     `<dir>/msg/ENGLISH/item.msgbnd.dcx` (required). Non-gameplay `.msb`
     files are skipped and listed under `ignoredFiles`.
   - `DsrDataTool patch-enemies --game <dir>`: reads the same map/event/
     param families plus AI (`LuaBundles`) and effect (`FfxBundle`)
     archives, resolved through `ResolveGamePath(gameDirectory, relative)`
     using the relative paths recorded in the catalog by `scan` (not
     re-derived independently) — so `patch-enemies` and `scan` must agree
     on the same `--game` snapshot (see `verifyCatalogSources`).
     `DarkSoulsRemastered.exe` is **not** required by `patch-enemies`.
   - `generate()` itself reads nothing from disk; it only reads the
     in-memory `gameCatalog` object passed to it.
7. **Exact files/directories written**:
   - `writeOutput(result, baseDirectory)` writes
     `<baseDirectory>/seed_<safeSeed>/{randomizer.json, config.json,
     README.txt, spoiler.txt?, cheat-locations.txt?}` (`spoiler.txt` only
     if `generateSpoilerLog`; `cheat-locations.txt` only if any
     item/gift/drop/shop/boss placements exist). It never touches
     `--game`.
   - `DsrDataTool patch-enemies --output <dir>` writes patched copies
     mirroring game-relative structure under `<dir>/mod/...` (verified:
     e.g. `<dir>/mod/map/MapStudio/<mapId>.msb`), plus
     `<dir>/patch-manifest.json` (schema: `PatchReport` — see
     [Result mapping](#resulterrorprogress-mapping)). It never writes
     under `--game`.
   - `install`/`restore` (not to be called) write directly into `--game`
     and into `<package>/backup/` + `<package>/activation-manifest.json`.

## Recommended integration method

**Hybrid, not the CLI, not the HTTP server:**

- **Direct ESM import** of `core/config.js` (`normalizeConfig`,
  `validateConfig`) and `core/generator.js` (`generate`) from the pinned
  fork, called in-process from the backend. Optionally `core/output.js`
  (`writeOutput`) if its file layout is adopted as-is (recommended — no
  reason to reimplement it).
- **Spawn `DsrDataTool.dll`** (via a pinned, bundled .NET 8 runtime, the
  same pattern upstream uses at `.tools/dotnet/dotnet.exe`) for `scan` and
  `patch-enemies` only. Never `install`/`restore`.

Why not `src/cli.js`: it hard-codes the catalog path to
`<upstream-root>/data/dsr-catalog.json` and the dotnet path to
`<upstream-root>/.tools/dotnet/dotnet.exe` relative to its own package
root, and calls `startServer()`/`runDataTool` with those fixed roots. Using
it as-is would require either running our backend from inside the vendored
package tree or patching those two path constants in the fork regardless —
at which point importing the core functions directly is simpler and keeps
more of our own control over paths, error codes, and progress events.

Why not `install`/`restore`: they are a second backup authority
(`<package>/backup/`, hash-verified, atomic copy into `--game`), which
directly conflicts with the backend's own stated design
(`engines/combined/README.md`: *"Its own installer and restore flow must
not become a second backup authority"*; `docs/ARCHITECTURE.md`:
`PristineSnapshot`/`RunWorkspace`/`GeneratedPackage`/`ActivePackage` are
application-level concepts). The backend should read `patch-manifest.json`
(written by `patch-enemies`) itself and drive its own activation using its
own backup mechanism.

`data/dsr-catalog.json` is confirmed **not** tracked in upstream git (it is
gitignored: `.gitignore` has `data/dsr-catalog.json`), consistent with
[docs/LICENSING.md](LICENSING.md)'s ban on distributing extracted game
catalogs. The fork must preserve that ignore entry.

## Request → upstream generator data flow

```
CombinedRandomizerRequest
  gamePath          -> scan --game <gamePath>              (only when catalog missing/stale)
                    -> patch-enemies --game <gamePath>       (read-only source)
  workspacePath      -> catalog.json under <workspacePath>
                    -> writeOutput(result, <workspacePath>)
                    -> patch-enemies --output <same base>
  combinedSeed       -> config.seed                          (see Blockers: charset)
  items.enabled &&
    items.randomizeWorldItems      -> config.randomizeItems
  items.enabled &&
    items.randomizeProtectedItems  -> config.randomizeProtectedItems
  items.enabled && items.progressionLogic       -> config.progressionLogic
  items.enabled && items.guaranteeEarlyWeapon   -> config.guaranteeEarlyWeapon
  items.enabled && items.balancedEarlyLoot      -> config.balancedEarlyLoot
  items.enabled && items.randomizeStartingClass -> config.randomizeStartingClass
  items.enabled && items.randomizeStartingEquipment -> config.randomizeStartingEquipment
  items.enabled && items.randomizeGifts         -> config.randomizeGifts
  items.enabled && items.randomizeEnemyDrops    -> config.randomizeEnemyDrops
  items.enabled && items.guaranteedEnemyDrops   -> config.guaranteedEnemyDrops
  items.enabled && items.randomizeShops         -> config.randomizeShops
  enemies.enabled && enemies.randomizeRegularEnemies -> config.randomizeEnemies
  enemies.enabled && enemies.randomizeBosses         -> config.randomizeBosses
  enemies.scaling                                     -> config.enemyScaling  (enum matches exactly)
  (not in request today)  -> config.generateSpoilerLog, config.useExtractedData=true,
                              config.dryRun=false          (see Blockers: missing fields)
```

Every `items.*` sub-flag must be ANDed with `items.enabled` (and likewise
`enemies.*` with `enemies.enabled`): upstream has no master on/off switch
per capability, only the individual flags. If both engines end up fully
disabled, `validateConfig` returns `["Enable at least one category to
randomize."]` (it returns errors, it does not throw), which the adapter must
translate to a request-validation error before ever touching upstream, not a
generation failure.

## Workspace strategy

1. Backend materializes/reuses a `RunWorkspace` at `request.workspacePath`.
2. If no valid, schema-17, source-verified catalog exists for
   `request.gamePath` in that workspace, run
   `scan --game <gamePath> --output <workspacePath>/dsr-catalog.json`.
   Reuse `verifyCatalogSources(gameDirectory, catalog)` logic (or its C#
   equivalent — it is JS-only today) to detect a stale catalog before
   trusting it, rather than re-scanning on every generation.
3. Call `normalizeConfig` + `validateConfig`, then `generate(config, {
   gameCatalog })` in-process. Reject results where
   `result.dataStatus !== "extracted"` (see Blockers).
4. `writeOutput(result, workspacePath)` — produces
   `<workspacePath>/seed_<seed>/`.
5. `patch-enemies --game <gamePath> --catalog <catalog.json> --placements
   <workspacePath>/seed_<seed>/randomizer.json --output
   <workspacePath>/seed_<seed>`.
6. Backend's own activation stage later reads
   `<workspacePath>/seed_<seed>/patch-manifest.json` and copies
   `mod/<Output>` over `<gamePath>/<Source>` under its own backup
   authority (`ActivePackage`) — outside this adapter's `generate()` call.

## Result/error/progress mapping

**Result → `CombinedRandomizerResult`**:
- Success: `{ ok: true, workspacePath: <workspacePath>/seed_<seed> }`. The
  manifest needed for activation (`patch-manifest.json`, with
  `Source`/`Output`/`SourceSha256`/`OutputSha256` per changed map, AI
  bundle, effect bundle, and `GameParam`) already lives inside that
  directory — no contract change needed to expose it.
- Failure codes (none exist upstream; all are adapter-invented, mapped
  from where the pipeline stage fails):
  - `INVALID_CONFIG` — a non-empty `validateConfig` error array, or
    request-shape problems caught before calling upstream at all.
  - `CATALOG_UNAVAILABLE` — no catalog for `gamePath`/`workspacePath` and
    `scan` was not requested or failed.
  - `CATALOG_STALE` — `verifyCatalogSources` mismatch; caller must
    re-`scan` (mirrors the existing `/api/scan` guard).
  - `PROTOTYPE_RESULT_REJECTED` — `generate()` returned
    `dataStatus !== "extracted"` despite a catalog being supplied (schema
    mismatch on `gameCatalog.schemaVersion`, or empty `enemySlots`).
  - `GENERATION_FAILED` — `generate()` threw (e.g. `"Could not create a
    safe boss permutation."` from `shuffledPortableBosses` after 128
    attempts) — **retryable** with a different derived seed/attempt, per
    the retry design in `docs/source-analysis.md`.
  - `DATA_TOOL_MISSING` — `DsrDataTool.dll`/local dotnet runtime not
    present (upstream's own `runDataTool` distinguishes this via
    `ENOENT`).
  - `DATA_TOOL_FAILED` — non-zero exit from `scan`/`patch-enemies`;
    message from stderr.
- Upstream's `result.validation.valid` is **hard-coded to `true`** in
  extracted mode (`randomizeExtractedEnemies`/bosses/items never set it
  false) — it is not real validation and must not be trusted as a
  generation-success signal. The backend's own validation stage must run
  independently, as already implied by `docs/ARCHITECTURE.md`.

**Progress/log capture**: coarse, stage-level only — `DsrDataTool` prints a
handful of summary lines (map/slot/file counts) to stdout **only at
completion**, not incrementally, for every command including
`patch-enemies`. There is no way to get true percentage progress out of
the upstream tool as written.
- Emit `progress` events at the JS-layer stage boundaries the adapter
  controls: config validated → catalog ready → `generate()` returned →
  `writeOutput()` done → data-tool process started → data-tool process
  exited.
- Use `child_process.spawn` (not the upstream pattern of
  `execFile`/`promisify`, which buffers all output until exit) so stdout
  and stderr lines can be relayed as `log` events as they arrive, and so a
  hang is distinguishable from silence rather than only surfacing at
  process exit.

## Licensing implications

- The whole `ds1-triple-randomizer` repo (root `package.json` and
  `apps/backend/package.json`) is already `GPL-3.0-only`, so in-process
  import of the GPL-3.0-only upstream JS is license-compatible without
  relicensing anything.
- `DsrDataTool` links `JuicerMV.SoulsFormats` (GPL-3.0-only,
  `THIRD_PARTY_NOTICES.md` in upstream). Distributing our own compiled
  `DsrDataTool.dll` requires our own `THIRD_PARTY_NOTICES.md` to carry
  that same entry (it currently only has the top-level `kellernz1/
  ds-randomizer` row).
- Whether the JS core is imported in-process or `DsrDataTool` is spawned
  as a subprocess is a technical choice, not a licensing one here — both
  sides are GPL-3.0-only, so there is no "mere aggregation" boundary to
  preserve.
- Standard GPLv3 obligations already captured in `docs/LICENSING.md` apply
  unchanged: pinned public fork commit, preserved upstream copyright/
  license notices and git history, corresponding source for the exact
  released version including our modifications, and — new, concrete
  requirement surfaced here — build scripts sufficient to reproduce
  `DsrDataTool.dll` from source (currently a PowerShell script,
  `scripts/dotnet-local.ps1`, plus `dotnet build`) must also be published,
  since GPLv3 "Corresponding Source" includes the scripts needed to
  control compilation.

## Blockers

1. **Prototype fallback is silent.** `generate()` never throws when
   `gameCatalog` is missing or has empty `enemySlots`/`enemyArchetypes` —
   it just returns fabricated placements with `dataStatus: "prototype"`.
   The adapter must treat `dataStatus !== "extracted"` as a hard failure
   before ever writing output or invoking `patch-enemies`.
2. **Seed charset is not guaranteed.** Upstream requires
   `config.seed` to match `/^[\w.-]{1,64}$/`
   (`src/core/config.js:validateConfig`). The backend's
   `deriveSeeds()` ([apps/backend/src/core/seeds.ts](../apps/backend/src/core/seeds.ts))
   passes `masterSeed.trim()` straight through as `combined`, and
   `configValidation.ts` only rejects an empty `masterSeed` — arbitrary
   characters or length are currently allowed at the API boundary. Either
   tighten `masterSeed` validation to upstream's charset/length, or have
   the adapter derive a compliant token (e.g. hex/base36 of a hash) instead
   of passing `combinedSeed` through unchanged. This changes what
   "receives the user's normalized master seed directly" means in
   practice and should be resolved before wiring the adapter, not inside
   it silently.
3. **`CombinedRandomizerRequest` has no field for `generateSpoilerLog`.**
   `output.generateSpoilerLog` exists on the full `RandomizerConfig`
   ([packages/contracts/src/index.ts](../packages/contracts/src/index.ts))
   but is not threaded into `CombinedRandomizerRequest`, which currently
   only carries `items`/`enemies`. (`includeDlc` is defined in upstream
   `config.js` but is dead — not read anywhere in `generator.js` — so it
   is *not* a blocker and does not need a request field.) This is a
   minimal, additive field, not a contract redesign: add
   `generateSpoilerLog: boolean` to `CombinedRandomizerRequest`.
4. **No wiring exists yet from the full pipeline into this adapter.**
   `apps/backend/src/core/pipeline.ts` does not reference
   `CombinedRandomizerRequest`/`CombinedRandomizerAdapter` at all today —
   confirmed by search. Task 1 below is self-contained and does not
   depend on this wiring existing.
5. **Runtime dependency.** `DsrDataTool` requires the .NET 8 SDK/runtime
   and the `JuicerMV.SoulsFormats` NuGet package to build
   (`npm run build:data-tool` upstream). This must become a documented,
   reproducible build step in this repo (framework-dependent build vs.
   self-contained publish is an open choice, not resolved here).
6. **Catalog/game-dir coupling.** `patch-enemies` resolves game-relative
   paths using the relative paths recorded inside the catalog by `scan`,
   not by re-deriving them from `--game` independently. A catalog scanned
   against one game directory must not silently be reused against a
   different or modified one — `verifyCatalogSources` exists for this in
   JS but is not currently invoked from `patch-enemies` itself, so the
   workspace strategy (step 2 above) must enforce this at the orchestration
   layer.

## Implementation split into small sequential tasks

1. Pure `CombinedRandomizerRequest` → upstream `config` mapping function
   (+ unit tests), including the `enabled`-AND-ing rules and the
   "everything disabled" pre-check. No fork, no `.NET`, no I/O — testable
   today, independent of blockers 4–6.
2. Resolve blocker 2 (seed charset) at the boundary that produces
   `combinedSeed`, with a test asserting the derived value always matches
   `/^[\w.-]{1,64}$/`.
3. Add the pinned GPL fork as a vendored/submoduled dependency (external
   action: create the fork under an org-controlled account), pin the
   analyzed commit, and add the `JuicerMV.SoulsFormats` row to
   `THIRD_PARTY_NOTICES.md`.
4. In-process `generate()` call: normalize/validate config, call
   `generate`, reject non-`extracted` `dataStatus`, return a typed
   planning result (no filesystem writes yet).
5. `writeOutput()` integration into the backend's workspace layout,
   producing `<workspacePath>/seed_<seed>/randomizer.json` etc.
6. `DsrDataTool` build step (blocker 5) + `scan` invocation with staleness
   check (`verifyCatalogSources` equivalent), wired into workspace catalog
   management.
7. `patch-enemies` spawn integration with stage-level `progress`/`log`
   event emission, completing `CombinedRandomizerAdapter.generate()`
   end-to-end against a real or fixture game directory.
8. Backend activation stage reading `patch-manifest.json` and applying it
   under the backend's own backup authority (outside `generate()` itself,
   but the natural next consumer of its result).

## Workspace execution proof

Resolves task 1 above plus the specific question: can the combined engine
generate against a controlled workspace without ever calling upstream
`install`/`restore` and without writing to the live game directory? This
section documents that proof and the pure module that implements it
(`apps/backend/src/adapters/combinedRandomizerWorkspace.ts`).

### Required workspace layout

```
<workspacePath>/
  dsr-catalog.json                 <- written by `scan`
  seed_<sanitizedCombinedSeed>/
    randomizer.json                <- written by generate()+writeOutput() (task 4/5, not this task)
    config.json
    (spoiler.txt / cheat-locations.txt / README.txt)
    patch-manifest.json            <- written by `patch-enemies`
    mod/                            <- written by `patch-enemies`
      map/MapStudio/<mapId>.msb
      event/... , param/... , (AI/effect bundles)
```

`seed_<...>` uses upstream's own `safeSeed()` transform
(`seed.replace(/[^\w.-]/gu, "_")`, `src/core/output.js`), reimplemented here
as `sanitizeUpstreamSeed()` — this is independent of the still-open seed
charset blocker (Blocker 2): it only affects the directory name, not
`config.seed` itself.

### Required source game files

Read-only, from `--game` (never written): the same set already documented
under "Exact files/directories read" above — `DarkSoulsRemastered.exe`,
`map/MapStudio/*.msb`, `event/*.emevd.dcx`,
`param/GameParam/GameParam.parambnd.dcx`,
`paramdef/paramdef.paramdefbnd.dcx`, `msg/ENGLISH/item.msgbnd.dcx` (scan
only), plus AI/effect bundles (patch-enemies only). No files are copied out
of the game directory ahead of time beyond what `scan` reads into
`dsr-catalog.json` — there is no separate "pristine snapshot" copy step
inside the upstream tool itself; that concept (`PristineSnapshot` in
`docs/ARCHITECTURE.md`) remains an application-level responsibility, not an
upstream one.

### Allowed upstream calls

- `DsrDataTool scan --game <gamePath> --output <workspace>/dsr-catalog.json`
- `DsrDataTool patch-enemies --game <gamePath> --catalog <catalog> --placements <workspace>/seed_<seed>/randomizer.json --output <workspace>/seed_<seed>`
- In-process `generate()` / `writeOutput()` (already covered above; not
  re-verified in this task)

### Forbidden upstream calls

- `DsrDataTool install` — writes into `--game`, owns a second backup
  authority (`<package>/backup/`), calls `EnsureGameClosed()`.
- `DsrDataTool restore` — same category as `install`.
- `src/cli.js ui`, `src/server.js` (`startServer`/`dispatchApi`) — Electron/
  HTTP orchestration, hard-codes its own catalog/dotnet paths, out of scope
  per the original integration doc.

`combinedRandomizerWorkspace.ts` structurally cannot build `install`/
`restore` argument vectors — it has no function that emits them, by
construction, not by a runtime check.

### Read/write boundaries

- `gameDirectory`: read-only for `scan` and `patch-enemies`. Never written
  by either command (verified against `Program.cs`: only `InstallPackage`/
  `RestorePackage` call `File.Copy`/`AtomicCopy` against a
  `ResolveGamePath(gameDirectory, ...)` target).
- `workspaceDirectory`: read/write for both allowed commands and for
  `writeOutput()`. `planCombinedRandomizerWorkspace()` refuses to plan a
  workspace equal to, or nested inside, the game directory — enforced in
  TypeScript *before* any path is ever handed to a subprocess, ahead of and
  independent from `patch-enemies`'s own equivalent guard
  (`PatchEnemies`, "Patch output cannot be placed inside the game
  installation.").

### Can live-game modification be fully avoided during generation?

**Yes, for the generation stage.** Every path both `scan` and
`patch-enemies` write to is derived from `workspaceDirectory`, never from
`gameDirectory`, and this is now enforced twice: once by upstream's own
runtime check, and once ahead of that, in pure, unit-tested TypeScript that
never touches the filesystem. Live-game modification only happens later,
in the backend's own activation stage (task 8), which is intentionally a
separate, later step outside `generate()`.

This task does **not** prove that a full generation run succeeds end to
end — that requires the pinned fork to be vendored and `DsrDataTool.dll`
to be built (Blocker 5, untouched by this task), plus a real or fixture
DSR installation to scan and patch against, neither of which exists in
this environment. What is proven here is narrower and load-bearing on its
own: the *shape* of every path and argument vector the workspace-only run
would use is fully determined, workspace-contained, and testable without
that binary or a real game install.

### Exact next implementation task

Vendor the pinned GPL fork under `engines/combined/vendor/` (matching the
`expectedSource`/`expectedExecutable` paths already read by
`apps/backend/src/core/pipeline.ts`'s `ExternalAdapter`), build
`DsrDataTool.dll`, and wire a `child_process.spawn`-based executor that
consumes `CombinedRandomizerWorkspacePlan.scanArgs`/`patchEnemiesArgs` and
emits `progress`/`log` events — this is Blocker 5 plus task 6/7 from the
implementation split above.

**Update:** the vendoring and build half of the task above is now done —
see "Vendored build proof" below. What remains is only the
`child_process.spawn`-based executor and its wiring into the adapter/
pipeline.

## Vendored build proof

Resolves the vendoring and reproducible-build half of Blocker 5. Does
**not** wire anything into the runtime pipeline and does not implement the
Node executor — see "Exact next implementation task" above for what is
still open.

### What was vendored

A selective vendor (not a full mirror) of `kellernz1/ds-randomizer` at
pinned commit `f120efe0e95b957b7e76109868c3c9966e22720c`
(2026-08-20T16:13:04-03:00), under `engines/combined/vendor/`. Every file
was verified byte-identical to that commit at vendor time. Full file list,
purpose, and update procedure: `engines/combined/vendor/UPSTREAM_PIN.md`.

Only the files needed to build `DsrDataTool.dll` were vendored:
`LICENSE`, `THIRD_PARTY_NOTICES.md`, `NuGet.Config`,
`scripts/{install-dotnet.ps1,dotnet-local.ps1}`,
`tools/DsrDataTool/{DsrDataTool.csproj,Program.cs}`. The upstream JS core
(`src/core/*.js`) is intentionally **not** vendored yet — deferred to the
task that wires the in-process `generate()` call, so nothing sits unused
and unbuilt in the tree.

`.gitignore`'s `engines/*/vendor/` blanket rule (which would have silently
prevented ever committing vendored source at all) was narrowed to
`engines/*/vendor/**/{.tools,.tmp,bin,obj}/`, so only actual build output
is excluded, not the vendored source itself.

### Build command

Reproduced upstream's own `npm run build:data-tool`
(`dotnet build tools/DsrDataTool/DsrDataTool.csproj --configuration Release
--configfile NuGet.Config`) using a locally-installed .NET 8 SDK, isolated
from any machine-wide dotnet/NuGet state:

```powershell
$env:DOTNET_CLI_HOME = "<short-path>\dn8sdk-clihome"
$env:NUGET_PACKAGES  = "<short-path>\dn8sdk-nuget"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

<short-path>\dn8sdk\dotnet.exe build `
  engines\combined\vendor\tools\DsrDataTool\DsrDataTool.csproj `
  --configuration Release `
  --configfile engines\combined\vendor\NuGet.Config
```

Result: `Build succeeded. 0 Warning(s). 0 Error(s).` Restored dependency:
`JuicerMV.SoulsFormats` 0.1.3 from `nuget.org` (the only source
`NuGet.Config` allows). SDK used: `8.0.425` (`dotnet --list-sdks`),
matching the vendored `DsrDataTool.csproj`'s `net8.0` target.

**Windows `MAX_PATH` deviation from upstream's own convention:** upstream's
`scripts/install-dotnet.ps1` installs the local SDK to
`<project root>/.tools/dotnet`. Running that unmodified against
`engines/combined/vendor/.tools/dotnet` failed during SDK extraction
(`DirectoryNotFoundException` on a ~245-character `dotnet-watch` tool
path — Windows' legacy 260-character path limit, not raised on this
machine, which requires an administrator-level registry change and was
out of scope for this task). The SDK was instead installed to a short
path outside the repository entirely
(`C:\Users\<user>\dn8sdk`, well under the limit even for the SDK's longest
internal paths) and referenced explicitly rather than via `PATH`. This is
an environment-local build-tool location, not a vendored file, and is not
committed. See `docs/DECISIONS.md` for why this was chosen over enabling
long paths.

### Resulting artifact

```
engines/combined/vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll
```

(alongside `DsrDataTool.exe`, `SoulsFormats.dll`, `ZstdSharp.dll`,
`DsrDataTool.deps.json`, `DsrDataTool.runtimeconfig.json`, `DsrDataTool.pdb`
in the same directory — all gitignored build output, reproducible from the
vendored `.csproj`/`.cs` plus the pinned NuGet dependency, not committed).

Verified by running `dotnet DsrDataTool.dll --help` from that directory:
it printed the exact five-command usage text documented earlier in this
file (`scan`, `patch-enemies`, `install`, `restore`, `inspect-boss-bars`)
and exited 0, with no `--game`/`--output` arguments supplied — so no file
of any kind, live game or otherwise, was read or written by this
verification step.

### Blockers unresolved by this task

- **No public fork exists.** `docs/LICENSING.md` requires "a public source
  tag matching each distributed binary." Creating and maintaining a fork
  under a project-controlled GitHub account is an account-owning action
  this task could not take. The local vendor directory satisfies "exact
  pinned commit" and "preserved notices" today; it does not yet satisfy
  the public-tag requirement, which only matters once a binary is
  distributed to end users.
- **The JS core is still unvendored.** `generate()`/`writeOutput()` remain
  analyzed-but-not-present in this repository.
- **No executor exists yet.** Nothing in this repository can currently
  invoke the built `DsrDataTool.dll`; `combinedRandomizerAdapter.ts`'s
  `generate()` is still `NOT_IMPLEMENTED`. This was explicitly out of
  scope for this task.
- Blockers 1-4 and 6 from the original integration analysis remain
  untouched, as instructed.

## JS core vendor proof

Resolves "the JS core is still unvendored" from the previous section, and
answers the concrete question it left open: what exactly does the backend
import, from where, and how. Does **not** call `generate()` or
`writeOutput()` — executing a generation run is a later task.

### The callable JS-core boundary

Everything this repository needs from the upstream JavaScript core is six
exports across three modules, all now vendored under
`engines/combined/vendor/src/`:

| Vendored module | Export | Signature | Notes |
|---|---|---|---|
| `core/config.js` | `RANDOMIZER_VERSION` | `string` | `"0.5.66"` at the pinned commit. Record it in the run manifest as the combined engine version. |
| `core/config.js` | `defaultConfig` | frozen object | Every key the mapping layer may set; unknown keys are dropped by `normalizeConfig`. |
| `core/config.js` | `normalizeConfig` | `(input?) => config` | Fills defaults, coerces booleans, trims strings, clamps `enemyScaling` to `vanilla \| area \| progressive`, and substitutes a random seed when `seed` is falsy. |
| `core/config.js` | `validateConfig` | `(config, { requireGame? }) => string[]` | **Returns** an error array; never throws. Empty array means acceptable. Call with `requireGame: false` — `generate()` does not read `gameDirectory`. |
| `core/generator.js` | `generate` | `(config, { gameCatalog? }) => result` | Pure and synchronous: no filesystem, network, timers, or ambient randomness. Throws only on internal impossibility (e.g. `"Could not create a safe boss permutation."`). |
| `core/output.js` | `writeOutput` | `(result, baseDirectory) => Promise<string>` | The only async, filesystem-touching export. Writes `<baseDirectory>/seed_<safeSeed(result.seed)>/…` and resolves to that directory. |

The result object keys this repository depends on are `schemaVersion`,
`randomizerVersion`, `dataStatus`, `seed`, `config`, and `placementHash`.
`dataStatus` is the load-bearing one: it is `"extracted"` only when a usable
catalog was supplied, and `"prototype"` otherwise (Blocker 1).
`result.validation.valid` remains hard-coded `true` upstream and must not be
trusted.

### Minimal module set and closed import graph

Five files, which is the transitive closure of `generate()` and
`writeOutput()`:

```
core/generator.js      generate()            <- entry point
  core/rng.js          (static import)
  core/config.js       (static import)       <- also an entry point
  data/prototype-data.js (static import)
core/output.js         writeOutput()         <- entry point, imports nothing local
```

Every module specifier appearing anywhere in those five files is either a
Node builtin (`node:crypto`, `node:fs/promises`, `node:path`) or one of the
three relative specifiers above. There are no dynamic `import()` calls, no
`require()` calls, and no third-party packages, so the vendored set needs no
`node_modules` and installs nothing.

`core/rng.js` and `data/prototype-data.js` are vendored **only because
`generator.js` statically imports them**, not because either is used on an
accepted path: the prototype data set exists to feed upstream's
catalog-less fallback, which must be rejected via `dataStatus`.

Deliberately excluded: `src/cli.js`, `src/server.js`, `src/desktop/**`
(CLI/HTTP/Electron layers), `src/core/share.js` (share links for upstream's
own UI), and `src/data/catalog.js` + `src/game/detection.js` (catalog
loading and staleness verification — a separate open concern, Blocker 6).
Rationale per file: `engines/combined/vendor/UPSTREAM_PIN.md`.

### Revision verification

All twelve vendored files (the seven from the previous task plus these five)
were verified equal to pinned commit
`f120efe0e95b957b7e76109868c3c9966e22720c` by comparing Git blob object IDs
rather than raw bytes, because `core.autocrlf` rewrites line endings on
checkout on Windows and makes `diff -q` unreliable. The exact object IDs and
the verification commands are recorded in
`engines/combined/vendor/UPSTREAM_PIN.md` ("Verification method and recorded
blob identities").

### How the backend loads it

`apps/backend/src/adapters/combinedRandomizerCore.ts` is the single loading
boundary. Three mechanics are forced by the environment rather than chosen:

1. **Dynamic `import()`, not a static TypeScript import.** The vendor tree
   lies outside the backend's `rootDir: src`, so a static import would
   either pull a file outside the program root into compilation (TS6059) or
   fail to resolve as typed (TS2307). The loader instead computes the path
   at runtime.
2. **`pathToFileURL()` around the resolved path.** A bare Windows absolute
   path (`C:\…`) is not a valid ESM specifier and is rejected with
   `ERR_UNSUPPORTED_ESM_URL_SCHEME`, so every specifier is a `file:` URL.
3. **An ESM scope marker at `engines/combined/vendor/package.json`.** The
   vendored `.js` files are ES modules, but the nearest enclosing
   `package.json` is this repository's root manifest, which is not
   `"type": "module"`. Without the marker Node emits
   `MODULE_TYPELESS_PACKAGE_JSON` and reparses each file. This one file is
   repository-authored, not vendored: upstream's own `package.json` carries
   Electron/`electron-builder` configuration that is out of scope, so only
   its `"type": "module"` declaration is reproduced.

The vendor root is resolved from `import.meta.url` with the same four-level
`../../../../` pattern `core/pipeline.ts` already uses, which resolves to the
repository root identically from `src/adapters` (under `tsx`) and
`dist/adapters` (compiled), so no build-time path rewriting is needed.

Types for the upstream exports are hand-written in the loader rather than
placed in `.d.ts` files beside the vendored code, so the vendored bytes stay
byte-identical to the pinned commit.

`loadCombinedRandomizerCore()` returns a typed result instead of throwing:

- `{ ok: true, core, coreRoot }`
- `{ ok: false, code: 'JS_CORE_MISSING', message, specifier }` — a module
  failed to import (absent vendor tree, unreadable file).
- `{ ok: false, code: 'JS_CORE_INCOMPLETE', message, missingExports }` — the
  tree loaded but does not expose the six documented exports, which means it
  no longer matches the pinned commit.

Both codes are new additions to the failure-code list under
[Result mapping](#resulterrorprogress-mapping). `coreRoot` and
`importModule` are injectable so both failures are testable without
disturbing the real vendor directory.

### Verification performed

`npm run test -w @ds1tr/backend` (35 tests, all passing), including seven new
tests that make the claims above executable rather than read off the source:

- the vendored `src/` tree contains exactly the five expected files;
- no vendored module references any specifier outside the allowed set, and
  every allowed specifier is actually used (so the set cannot silently drift
  in either direction);
- `engines/combined/vendor/package.json` declares `"type": "module"`;
- core modules are addressed as `file:` URLs;
- a real load succeeds and exposes all six exports, with
  `RANDOMIZER_VERSION === "0.5.66"` pinning the vendored revision
  behaviourally;
- `normalizeConfig`/`validateConfig` are callable with no game directory and
  no filesystem access, and `validateConfig` returns `[]` rather than
  throwing;
- an absent vendor root yields `JS_CORE_MISSING`, and a stubbed importer
  yields `JS_CORE_INCOMPLETE` with the exact missing-export list.

Backend `npm run typecheck` and `npm run build` both pass. `generate()` was
never called, no game directory was touched, and no file outside the
repository was read or written.

### Blockers unresolved by this task

- **`core/pipeline.ts` still probes the wrong path.** `ExternalAdapter` for
  the combined engine is configured with
  `expectedSource: 'vendor/src/cli.js'` and
  `expectedExecutable: 'vendor/DSR-Randomizer.exe'`
  ([apps/backend/src/core/pipeline.ts](../apps/backend/src/core/pipeline.ts)).
  Neither exists and neither should: `src/cli.js` is deliberately excluded,
  and there is no upstream portable `.exe` in this integration model. The
  engine-presence probe therefore keeps reporting the combined engine as
  missing even though the core is now vendored. Correct targets are
  `vendor/src/core/generator.js` and the built
  `vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll`. Changing
  them is pipeline wiring, explicitly out of scope here, and belongs to the
  task that wires the execution bridge.
- **No public fork exists** — unchanged; see the previous section and
  `docs/LICENSING.md`. Vendoring the JS core does not change this: it is the
  same upstream repository under the same GPL-3.0-only terms.
- **Blockers 1, 2, 3, and 6** (silent prototype fallback, seed charset,
  missing `generateSpoilerLog` request field, catalog/game-dir coupling) are
  untouched by this task. Blocker 5's build half stays resolved; the
  `DsrDataTool.dll` runtime dependency is unchanged.
