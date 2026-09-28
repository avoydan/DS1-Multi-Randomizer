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
  generateSpoilerLog -> config.generateSpoilerLog          (added by Blocker 3 resolution)
  (not in request)   -> config.useExtractedData=true, config.dryRun=false  (pinned by the bridge)
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

Status (2026-09-28): Blockers 1, 2, 3, and 6 are resolved — see "Execution
bridge proof" (1) and "Blocker resolution" (2, 3, 6) below. Blocker 4 is
Task 7 (adapter wiring); Blocker 5's build half is resolved, and running the
real tool against a real game is the end-to-end proof (Task 8). The original
text is kept for the record.

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
in the backend's own activation stage (Task 13 of `docs/AI_EXECUTION_PLAN.md`), which is intentionally a
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

## Execution bridge proof

Resolves "no executor exists yet" and the `core/pipeline.ts` probe defect from
the previous two sections. This is the first section that describes code which
actually *runs* a generation: `generate()` and `writeOutput()` are called for
real, against a synthetic catalog, in a temporary workspace.

Implemented by `apps/backend/src/adapters/combinedRandomizerBridge.ts`
(`executeCombinedGeneration`). It is an internal path, deliberately not yet
wired into `CombinedRandomizerAdapter.generate()` (Task 7) or the pipeline.

### Exact data flow

```
CombinedRandomizerRequest
  |
  |-1- mapCombinedRandomizerConfig(request)            pure, no I/O
  |      items.enabled && items.*     -> config.*
  |      enemies.enabled && enemies.* -> config.*
  |      nothing enabled              -> NO_RANDOMIZATION_FEATURES_ENABLED
  |
  |-2- loadCombinedRandomizerCore()                    dynamic import of file: URLs
  |      absent tree                  -> JS_CORE_MISSING
  |      exports missing              -> JS_CORE_INCOMPLETE
  |
  |-3- { ...mapped, outputDirectory: resolve(workspacePath),
  |      useExtractedData: true, dryRun: false }      (spoiler flag comes from the request)
  |      core.normalizeConfig(input)                   trims/coerces seed
  |      core.validateConfig(config, { requireGame: false })
  |      non-empty error array         -> INVALID_CONFIG
  |
  |-4- planCombinedRandomizerWorkspace({ gamePath, workspacePath,
  |      combinedSeed: config.seed })                  pure, no I/O
  |      workspace inside game dir    -> WORKSPACE_INSIDE_GAME_DIRECTORY
  |      => catalogPath, seedDirectory, scanArgs, patchEnemiesArgs
  |
  |-5- catalog present? -- no --> DsrDataToolExecutor.execute(scanArgs)
  |      spawn failure               -> DATA_TOOL_MISSING
  |      non-zero exit               -> DATA_TOOL_FAILED
  |      still no catalog file       -> CATALOG_UNAVAILABLE
  |      unparseable                 -> CATALOG_UNREADABLE
  |      schemaVersion !== 17        -> CATALOG_STALE
  |      reused catalog's sourceFiles differ from gamePath
  |                                  -> CATALOG_SOURCE_MISMATCH  (no rescan)
  |
  |-6- core.generate(config, { gameCatalog })          pure, synchronous
  |      throws                      -> GENERATION_FAILED (retryable)
  |      dataStatus !== extracted    -> PROTOTYPE_RESULT_REJECTED
  |
  |-7- core.writeOutput(result, plan.workspaceDirectory)
  |      throws                      -> OUTPUT_WRITE_FAILED
  |      directory != planned        -> OUTPUT_LAYOUT_MISMATCH
  |      => <workspace>/seed_<safeSeed>/{randomizer,config}.json, README.txt
  |
  |-8- DsrDataToolExecutor.execute(patchEnemiesArgs)
  |      spawn failure               -> DATA_TOOL_MISSING
  |      non-zero exit               -> DATA_TOOL_FAILED
  |      => <seed dir>/mod/**, <seed dir>/patch-manifest.json
  |
  +--> { ok: true, seedDirectory, workspaceDirectory, catalogPath,
         placementsPath, patchManifestPath, randomizerVersion,
         placementHash, seed, catalogScanned }
```

Activation is *not* part of this flow. The bridge stops at a validated package
inside the workspace; copying `mod/**` over the live installation stays with
the backend's own single backup authority, in a later stage.

### Ordering constraints that are load-bearing, not incidental

1. **Normalize the config before planning the workspace.** Upstream
   `normalizeConfig` trims and string-coerces `seed`, and `writeOutput`
   derives its directory as `seed_<safeSeed(result.seed)>` from that
   *normalized* value. Planning from the raw `request.combinedSeed` desyncs
   the two — `"  abc  "` plans `seed__abc__` while upstream writes
   `seed_abc`, and `--placements` then points at a file that was never
   written. The planner is therefore called with `config.seed`, and the
   directory upstream reports back is asserted to equal the planned one
   (`OUTPUT_LAYOUT_MISMATCH` guards against a future pin drifting).
2. **Reject `dataStatus !== "extracted"` before writing anything and before
   spawning anything.** Otherwise a prototype package — which upstream's own
   README marks "must not be installed into the game" — reaches a package
   directory that later stages treat as installable.
3. **Check `schemaVersion` before calling `generate()`.** Upstream throws on
   an obsolete schema, which would otherwise arrive as an opaque
   `GENERATION_FAILED` rather than an actionable "re-import the game data".
4. **Load the vendored core and validate the config before any filesystem or
   process work.** An absent vendor tree or an unusable seed is reported
   without having scanned a game directory.

### Three config keys the bridge pins rather than inherits

(Superseded for `generateSpoilerLog` by "Blocker resolution" below: it is now
a request field and is no longer pinned. `useExtractedData`/`dryRun` are
still pinned exactly as described.)

`CombinedRandomizerRequest` carries none of them, so upstream `defaultConfig`
would otherwise decide:

- `generateSpoilerLog: false` — upstream defaults to `true`, which would emit
  `spoiler.txt` no caller asked for. Integration Blocker 3 (the missing
  request field) is unchanged; this only stops a silent default.
- `useExtractedData: true`, `dryRun: false` — as specified in the data flow
  earlier in this document. Verified inert at the pinned commit: neither key
  is read anywhere in the vendored core or in `DsrDataTool/Program.cs`. They
  are set so the recorded `config.json` states them explicitly rather than by
  omission.

The seed itself is **not** rewritten. An upstream-incompatible seed returns
`INVALID_CONFIG`; deriving a compliant token belongs to the boundary that
produces `combinedSeed` (Blocker 2), not to this bridge, so the seam stays
visible instead of being silently papered over.

### `DsrDataTool` location

`dotnetExecutablePath` defaults to `dotnet` on `PATH`, overridable per call or
via the `DS1TR_DOTNET_PATH` environment variable — the documented local SDK
lives outside the repository tree (`docs/DECISIONS.md`, Windows `MAX_PATH`).
`dataToolDllPath` defaults to the vendored build output at
`engines/combined/vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll`.
Both are gitignored/environment-local, so their absence is reported as
`DATA_TOOL_MISSING` rather than crashing.

### Verification performed

`npm run test -w @ds1tr/backend` — 54 tests pass (19 new); backend `build` and
the full workspace `typecheck` are clean (2026-09-28).

The 19 new tests run the **real** vendored `generate()`/`writeOutput()` in a
real temporary workspace against a synthetic schema-17 catalog (three enemy
slots, two archetypes — the minimum that yields `dataStatus: "extracted"`).
Only the `DsrDataTool` process layer is faked, because running it needs both
the built .NET assembly and a real Dark Souls installation. What that proves:

- a complete package is produced: `randomizer.json` (with
  `dataStatus: "extracted"` and three real enemy placements), `config.json`,
  `README.txt`, plus the manifest the fake patch step writes;
- the argument vectors handed to the process layer are **byte-for-byte the
  planner's** `scanArgs`/`patchEnemiesArgs`; no ad-hoc paths, and
  `install`/`restore` never appear;
- the placements file already exists at the moment `patch-enemies` is
  invoked — asserted from *inside* the fake, the only place ordering is
  observable;
- a fixture game directory is **byte-identical before and after** a full
  successful run;
- the same request run twice in different workspaces yields the same
  `placementHash` and identical placements;
- `scan` runs first, with the planner's exact arguments, when the workspace
  has no catalog, and is skipped when one is present;
- a whitespace-padded seed still lands in the planned package directory
  (ordering constraint 1 above, made executable);
- prototype data, an obsolete schema, a malformed catalog, an
  upstream-rejected seed, a workspace inside the game directory, and a
  request with nothing enabled each return their documented code **with the
  process layer never invoked and no package directory created**;
- `spawn-failure` maps to `DATA_TOOL_MISSING`, a non-zero exit to
  `DATA_TOOL_FAILED` with stdout/stderr preserved, and an upstream
  `generate()` throw to a **retryable** `GENERATION_FAILED`.

No real Dark Souls installation was involved, and no file outside the
repository and the OS temp directory was read or written.

### The `pipeline.ts` probe, fixed here

`ExternalAdapter` for the combined engine probed `vendor/src/cli.js` and
`vendor/DSR-Randomizer.exe`, neither of which exists or should, so
engine-presence status always reported the combined engine as `missing`. The
two literals now point at `vendor/src/core/generator.js` and the built
`vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll`, and a test
asserts the combined engine now reports `source` mode. The generator is
committed, so that assertion is deterministic; the DLL is gitignored build
output and is deliberately not asserted.

This was a documented handoff from the vendoring task, not a dependency of the
bridge: `executeCombinedGeneration` never consults `ExternalAdapter` and
resolves its own paths.

### Blockers unresolved by this task

- **Blocker 2 (seed charset)** — deliberately left open and now observable:
  `deriveSeeds()` still passes the raw master seed through, so an
  upstream-incompatible master seed surfaces as `INVALID_CONFIG` from the
  bridge. Fixing it belongs to the seed boundary.
- **Blocker 3 (`generateSpoilerLog` request field)** — unchanged; the bridge
  pins the value off rather than adding a contract field.
- **Blocker 6 (catalog/game-dir coupling)** — unchanged. The bridge reuses an
  existing catalog without verifying it was scanned against this `gamePath`;
  `verifyCatalogSources` is not vendored. `forceCatalogRescan` exists as a
  caller-side escape hatch, which is not the same as staleness detection.
- **No public GPL fork** — unchanged.
- **`DsrDataTool` runtime dependency** — unchanged. Nothing in this task
  executed the real tool, so a genuine `scan`/`patch-enemies` round trip
  against real game data is still unproven; that is the end-to-end task.

## Blocker resolution

Resolves integration Blockers 2, 3, and 6 — the remaining blockers that had
to be fixed before a real combined-engine generation run can be attempted.
Blocker 1 was already resolved by the execution bridge.

### Blocker 2 — seed charset

`deriveSeeds()` (`apps/backend/src/core/seeds.ts`) now derives `combined` as
the first 32 hex characters of
`SHA-256("ds1-triple-randomizer:v1:<trimmed master seed>:combined")`, the same
named derivation scheme the Fog seed already used. Every master seed —
unicode, emoji, spaces, path separators, 1000 characters — now yields a token
matching upstream's `/^[\w.-]{1,64}$/`, asserted by `seeds.test.mjs`. The
master seed stays free text at the API boundary. The bridge still never
rewrites seeds: a non-compliant `combinedSeed` handed to it directly still
reports `INVALID_CONFIG`. Rationale in `docs/DECISIONS.md`.

### Blocker 3 — spoiler-log request field

`CombinedRandomizerRequest.generateSpoilerLog: boolean` was added and mapped
straight through `mapCombinedRandomizerConfig` to upstream
`generateSpoilerLog`. The bridge's former hard-coded `generateSpoilerLog:
false` was removed (it sat after the spread of the mapped config and would
have silently overridden the new field). A bridge test proves `spoiler.txt`
is written exactly when requested, `config.json` records the flag, and the
placement hash is identical either way. Filling the field from
`RandomizerConfig.output.generateSpoilerLog` is pipeline wiring (Blocker 4,
Task 7).

### Blocker 6 — catalog/game-directory coupling

`verifyCatalogSources(gamePath, catalog)`
(`apps/backend/src/adapters/combinedRandomizerCatalog.ts`, an independent
implementation — upstream's JS function of the same name is not vendored)
checks every `sourceFiles` record `DsrDataTool scan` wrote: the relative path
must stay inside the game directory (absolute and `..` paths are refused),
the file must exist, and its size and SHA-256 must match. `lastWriteTimeUtc`
is ignored. The bridge runs it after the schema check and before
`generate()`, only when the catalog was **reused**; a failure returns
non-retryable `CATALOG_SOURCE_MISMATCH` with per-file `catalogProblems`, with
no process spawned, no package written, and no automatic rescan. A catalog
recording no source files is treated as unverifiable and rejected.

What it does and does not prove: it catches a catalog being reused against a
different or since-modified installation, before generation. It does not
prove the catalog was scanned from vanilla data — a catalog scanned from an
already-modded installation verifies against that installation. Pristine
provenance belongs to the backend's pristine import/backup authority.
`patch-enemies` independently re-asserts the hashes of the files it touches.

### Verification performed

`npm run test -w @ds1tr/backend` — 66/66 pass (12 new or changed: 4 seed, 1
mapper, 6 bridge/catalog, plus updated fixtures); root `npm run typecheck`
(contracts + backend + desktop) clean (2026-09-28). No real Dark Souls
installation was involved.

### Still open

- `CombinedRandomizerAdapter.generate()` now delegates to the bridge
  (Blocker 4 resolved by Task 7); the application pipeline does not yet call it.
- ~~`DsrDataTool` has never run against a real installation (Task 8).~~
  Resolved by Task 8 — see "End-to-end generation proof".
- No public GPL fork (external action, `docs/LICENSING.md`).

## End-to-end generation proof

Task 8. First real run of the whole Items + Enemies path — typed request →
config mapping → vendored JS `generate()`/`writeOutput()` → real
`DsrDataTool scan`/`patch-enemies` (.NET 8.0.28 runtime, `DsrDataTool.dll`
SHA-256 `eb6e9b24…8376`, built 2026-09-27 after the last `Program.cs`
change) → workspace package. Harness:
`apps/backend/scripts/combinedEngineE2eProof.mjs` (not part of `npm test`;
needs a real game directory, the built tool, and a .NET 8 runtime). All
workspaces live in the OS temp directory and were deleted afterwards —
extracted catalogs and patched game files never enter the repository
(`docs/LICENSING.md`).

### Test data: the live install is legacy-modified

The only installation on the machine (Steam `DARK SOULS REMASTERED`) had
already been modified in place by the legacy Item Randomizer, the legacy
Enemy Randomizer, and FogMod (`.bak` chains in `map/MapStudio`, `event`,
`script`, `param/GameParam`, `sfx`; `random-seed-*` directories). Its MSBs
carry the legacy enemy randomizer's extra model declarations — e.g.
`m10_00_00_00.msb` declares 146 enemy models (26 duplicate names that
SoulsFormats disambiguates as `c1200 {2}`) against 15 in its `.bak`.

**Run against the live install (read-only):** `scan` and `generate()`
accepted it (`dataStatus: "extracted"`, catalog schema 17, 56 source files),
then `patch-enemies` refused with its own post-write verification:
`Enemy model declaration pruning did not persist: m10_00_00_00`
(`DATA_TOOL_FAILED`, `retryable: false`). A full before/after snapshot of the
install (6,658 files, path + size + SHA-256, plus the directory set) was
identical.

**Proof data:** a reconstructed copy in the temp directory — plain copy (no
hardlinks) of exactly the read set of `scan` and `patch-enemies`
(`DarkSoulsRemastered.exe`, `param/`, `paramdef/`, `msg/`, `event/`,
`script/`, `sfx/`, `map/MapStudio/`; 210 files, 146 MB), with every legacy
`X.bak` moved over `X`. All reconstructed MSBs read back with zero
duplicate model names. Provenance limit: the `.bak` files date from the
last legacy run (2026-09-26), so vanilla status cannot be asserted, and any
file a legacy tool changed without leaving a `.bak` stays modified. The
claim is therefore **"pipeline proven on real-format game data"**, not
"proven on vanilla data". Reconstruction was a one-off test-data step, not
product behaviour.

### Results (all 47 checks pass)

Live install passed as a guard directory, reconstructed copy as `--game`.

| Run | Proves |
|---|---|
| A — fresh workspace | `scan` → `generate` → `patch-enemies` succeed (~12 s). `catalogScanned: true`, seed = derived 32-hex combined seed, `dataStatus: "extracted"`, upstream `validation.valid: true`, `finalBossReachable: true`. Placements: 1,861 enemies, 32 bosses, 514 items, 18 gifts, 312 enemy drops, 387 shop entries; `patch-manifest.json` reports 1,831 changed slots. Package: 60 files, 54 under `mod/` (18 MSBs, 17 Lua bundles, 17 EMEVDs, `CommonEffects` FFX bundle, `GameParam`). Every manifest entry's `output` is package-relative, exists, and matches its `outputSha256`; every `mod/` file is listed; none is empty; every `source` is game-relative. Progress and log events reach the callback. |
| B — second fresh workspace, same master seed | Catalog identical except `generatedAt`; same `placementHash`; **every package file byte-identical**, including all 54 patched binaries. The only differences anywhere are `outputDirectory` (absolute workspace path) and `generatedAt` in the JSON files. |
| C — workspace seeded with A's catalog | `verifyCatalogSources` accepts a real catalog (57 source files) — first run against real `sourceFiles` data, no false mismatch; `catalogScanned: false`; same `placementHash`. |
| D — different master seed | Different `placementHash`. |
| E1 — nonexistent game directory | Real process failure → `DATA_TOOL_FAILED`, non-retryable, tool stderr captured (`Directory not found: …`). |
| E2 — nonexistent `DsrDataTool.dll` | `DATA_TOOL_FAILED` (see findings). |
| E3 — catalog with one tampered source hash | `CATALOG_SOURCE_MISMATCH`, one precise problem. |
| E4 — same workspace, `forceCatalogRescan` | Recovers after E1–E3; same `placementHash` as A. |
| S1 — workspace left by the failed live-install run (foreign catalog, partial seed directory without `patch-manifest.json`) | `CATALOG_SOURCE_MISMATCH` (52 problems); no automatic rescan. |
| S2 — same stale workspace, `forceCatalogRescan` | Recovers in place; resulting package identical to A's (no stale leftovers — the partial MSB was overwritten). |
| F — public `CombinedRandomizerAdapter.generate()` | Succeeds, returns the package directory inside the workspace. |
| Guards | Reconstructed copy and live install both identical before/after (full SHA-256 + directory set). No stray files in the repository. |

The S1/S2 stale workspace came from a separate read-only rerun of the same
request against the live install (identical failure). That rerun was not
inside a hash snapshot; it is covered instead by an mtime sweep
(`find "<live install>" -newermt 2026-09-28`) that returned nothing — no file
or directory in the live install was created or written on the day of the
runs (the newest writes are the legacy tools' from 2026-09-26).

### Reproducing

```bash
LIVE="<DSR install>"; R="$TEMP/ds1tr-e2e-reconstructed-game"
# 1. Failed run against the live install; keep workspace A as the stale workspace.
DS1TR_E2E_KEEP=1 node apps/backend/scripts/combinedEngineE2eProof.mjs "$LIVE"
# 2. Reconstructed copy of the tool's read set, legacy backups restored.
mkdir -p "$R/map" && cp "$LIVE/DarkSoulsRemastered.exe" "$R/"   && cp -r "$LIVE"/{param,paramdef,msg,event,script,sfx} "$R/"   && cp -r "$LIVE/map/MapStudio" "$R/map/"
find "$R" -name '*.bak' | while read -r b; do mv -f "$b" "${b%.bak}"; done
# 3. The proof run.
DS1TR_E2E_GUARD_PATHS="$LIVE" DS1TR_E2E_STALE_WORKSPACE="<kept workspace A>"   node apps/backend/scripts/combinedEngineE2eProof.mjs "$R"
# Delete $R and any kept workspaces afterwards (game-derived data).
```

### Findings for later tasks (recorded, not fixed here)

1. **Legacy-modified installs are detected late and opaquely.** `scan` and
   `generate()` accept them as `extracted` data; only `patch-enemies`'
   post-write verification rejects them, with a message that does not
   mention legacy mods. Any user who ran the legacy Enemy Randomizer in
   place hits this. Pristine import/verification (Task 13 / the backup
   authority) must detect non-vanilla game data up front — e.g. against known
   vanilla hashes — and say so.
2. **A failed `patch-enemies` leaves a partial seed directory** (upstream
   output files plus some `mod/` files, no `patch-manifest.json`).
   Activation must require `patch-manifest.json` and consume only the files it
   lists (with `outputSha256` checks). Seed-changing retries will leave such
   partial directories behind in the workspace; the pipeline should clean or
   ignore them.
3. **A missing `DsrDataTool.dll` reports `DATA_TOOL_FAILED`, not
   `DATA_TOOL_MISSING`.** `dotnet <missing.dll>` spawns successfully and
   exits non-zero, so the "build the tool" hint is never shown. The bridge or
   packaging checks (Task 17) should test the dll's existence before spawning.
4. **Output JSON embeds absolute workspace paths and timestamps**
   (`outputDirectory`, `generatedAt`). Determinism comparisons and the run
   manifest must exclude them; the patched game files themselves are fully
   deterministic.
