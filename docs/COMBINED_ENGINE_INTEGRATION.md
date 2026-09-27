# Combined engine execution integration

Scope: execution integration only, for `kellernz1/ds-randomizer` behind
`CombinedRandomizerEngine.generate(...)`
([apps/backend/src/adapters/combinedRandomizerAdapter.ts](../apps/backend/src/adapters/combinedRandomizerAdapter.ts)).
This does not repeat the feature-parity analysis in
[docs/MOD_ANALYSIS.md](MOD_ANALYSIS.md) or
[docs/source-analysis.md](source-analysis.md).

Analyzed against upstream commit `f120efe0e95b957b7e76109868c3c9966e22720c`
(2026-08-20), cloned read-only for this analysis. No upstream files were
copied into this repository.

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
     `defaultConfig`. `config.seed` must match `/^[\w.-]{1,64}$/` or
     `validateConfig` throws.
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
disabled, `validateConfig` throws (`"Enable at least one category to
randomize."`), which the adapter must translate to a request-validation
error before ever touching upstream, not a generation failure.

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
  - `INVALID_CONFIG` — `validateConfig` errors, or request-shape problems
    caught before calling upstream at all.
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
