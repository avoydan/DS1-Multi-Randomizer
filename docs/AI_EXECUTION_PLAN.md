# Combined Engine Implementation Plan

## Purpose

This document is the shared execution queue for Claude and Codex.

The repository is the source of truth.

Agents must not reconstruct previous chat conversations.
Read `AGENTS.md` first and follow all workflow, safety, Git, licensing,
testing, and efficiency rules defined there.

This plan intentionally alternates larger integration/research work
with smaller implementation/testing work.

---

# Execution protocol

Only ONE task may be ACTIVE at a time.

Task states:

- `PENDING`
- `ACTIVE`
- `DONE`
- `BLOCKED`

An agent may work only on:
- the first non-DONE task,
- and only if that task is assigned to that agent.

Before beginning:

1. Read `AGENTS.md`.
2. Read `docs/IMPLEMENTATION_STATUS.md`.
3. Read `docs/DECISIONS.md`.
4. Read this document.
5. Find the first task whose state is not `DONE`.
6. If it is assigned to another agent, STOP without changing anything.
7. If it is assigned to you, change its state to `ACTIVE` and execute it.

After successful completion:

1. Run verification required by that task.
2. Update `docs/IMPLEMENTATION_STATUS.md`.
3. Update `docs/DECISIONS.md` only for actual architectural decisions.
4. Change the current task state to `DONE`.
5. Record:
   - commit hash
   - verification result
   - important result
   - blockers discovered
6. Commit all current-task changes, including this plan update.
7. STOP.

Do NOT begin the next task, even if it is assigned to the same agent.

If blocked:

1. Change state to `BLOCKED`.
2. Document the exact blocker.
3. Do not guess or broaden scope.
4. Commit the status/documentation change if appropriate.
5. STOP.

Never modify the state of another task except when activating your own task.

---

# TASK 1 — CODEX

State: DONE

## DsrDataTool executor

Implement only the backend executor for the already-vendored
`DsrDataTool.dll`.

Use the argument vectors produced by the existing workspace planner.

Requirements:

- use Node `child_process.spawn`
- support only:
  - `scan`
  - `patch-enemies`
- consume existing workspace-planner arguments
- do not duplicate path planning
- accept:
  - dotnet executable path
  - DsrDataTool.dll path
  - planned arguments
  - optional event callback
- capture:
  - stdout
  - stderr
  - exit code
  - spawn failure
- expose typed results
- expose typed log/progress events where reasonable
- normal process failures should return typed failures instead of throwing
  where practical

Do NOT:

- wire into the generation pipeline
- integrate upstream JS core
- invoke `install`
- invoke `restore`
- work on FogMod
- modify frontend
- touch live game files
- inspect upstream broadly

Testing:

- successful process
- non-zero exit
- spawn failure
- stdout forwarding
- stderr forwarding
- argument forwarding
- no game filesystem access

Verification:

- focused tests
- relevant backend typecheck/build

Completion record:

- Commit: `5402b447f3d53a1896ca59267fc5fe10461bc071` (`feat(backend): add DsrDataTool executor`)
- Verification: 7 focused executor tests passed; backend build and typecheck passed (2026-09-27).
- Result: The existing committed executor accepts planner arguments, runs only `scan` and `patch-enemies`, and returns typed process results and events. Implementation and verification are complete; task completion remains pending.
- Blockers: Git cannot create `.git/index.lock` (`Permission denied`) under the current filesystem policy, so this plan/status update cannot be committed. The plan file was already untracked at task start.
- None. Git sandbox limitation was resolved by the user committing the status update manually.

---

# TASK 2 — CLAUDE

State: DONE

## Vendor and pin the upstream JavaScript generation core

Using the already-pinned upstream revision:

`kellernz1/ds-randomizer`
`f120efe0e95b957b7e76109868c3c9966e22720c`

vendor only the JavaScript files required to execute the pure combined
randomizer generation core.

Goals:

- identify the minimal required JS module set
- preserve upstream license/notices
- verify vendored files against the pinned revision
- determine the exact callable entry point
- determine its dependency graph
- establish how it will be loaded from the backend
- keep Electron/server/CLI UI layers excluded unless technically required

Do NOT:

- execute full generation yet
- wire pipeline
- implement FogMod
- solve unrelated compatibility blockers
- vendor unrelated upstream files

Update `docs/COMBINED_ENGINE_INTEGRATION.md` with the concrete callable
JS-core boundary.

Verification:

- exact revision verification
- minimal dependency/import verification
- appropriate focused checks

Completion record:

- Commit: `a969d2a8896b106b9b11a1c00d2e6d9f963db4cb`
  (`feat(combined): vendor pinned upstream JS generation core`).
- Verification (2026-09-28):
  - **Exact revision**: all twelve vendored files verified equal to pinned
    commit `f120efe0e95b957b7e76109868c3c9966e22720c` by comparing Git blob
    object IDs (upstream `rev-parse <pin>:<path>` vs. local
    `hash-object`). Raw `diff` is unreliable here because `core.autocrlf`
    rewrites line endings on Windows checkout. The staged blobs in the
    commit above were re-checked against upstream after staging. Object IDs
    are recorded in `engines/combined/vendor/UPSTREAM_PIN.md`.
  - **Minimal dependency/import verification**: every module specifier in
    the five vendored files is either a Node builtin (`node:crypto`,
    `node:fs/promises`, `node:path`) or one of three relative specifiers
    inside the vendored set; no dynamic `import()`, no `require()`, no
    third-party packages, so the set needs no `node_modules`. Asserted
    executably, not by inspection, in
    `apps/backend/src/adapters/combinedRandomizerCore.test.mjs`.
  - **Focused checks**: `npm run test -w @ds1tr/backend` — 35/35 pass
    (7 new); `npm run typecheck -w @ds1tr/backend` and
    `npm run build -w @ds1tr/backend` both clean. `generate()` was never
    called; no game directory was touched.
- Result:
  - **Minimal module set** (the transitive closure of `generate()` and
    `writeOutput()`), vendored under `engines/combined/vendor/src/`:
    `core/config.js`, `core/generator.js`, `core/output.js`, `core/rng.js`,
    `data/prototype-data.js`. `rng.js` and `prototype-data.js` are present
    only because `generator.js` statically imports them.
  - **Excluded**: `src/cli.js`, `src/server.js`, `src/desktop/**`,
    `src/core/share.js`, `src/data/catalog.js`, `src/game/detection.js`,
    `public/**`, upstream `package.json`, `test/**`. Rationale per file in
    `UPSTREAM_PIN.md`.
  - **Callable entry point**: `generate(config, { gameCatalog })` in
    `core/generator.js` — pure and synchronous. Six exports form the whole
    boundary: `generate`, `normalizeConfig`, `validateConfig`,
    `defaultConfig`, `RANDOMIZER_VERSION` (`0.5.66`), `writeOutput`.
  - **Loading from the backend**:
    `apps/backend/src/adapters/combinedRandomizerCore.ts` — a computed
    dynamic `import()` of `pathToFileURL()` specifiers resolved from
    `import.meta.url` (a static TS import cannot reach a tree outside the
    backend `rootDir`; a bare Windows path is not a valid ESM specifier).
    Upstream types are hand-written in the backend so vendored bytes stay
    byte-identical. Returns typed `JS_CORE_MISSING` /
    `JS_CORE_INCOMPLETE` failures instead of throwing.
  - **Licenses/notices preserved**: the vendored `LICENSE` and
    `THIRD_PARTY_NOTICES.md` are unchanged (same upstream repository, same
    GPL-3.0-only terms, so the JS core adds no new notice obligations);
    this repository’s `THIRD_PARTY_NOTICES.md` and `UPSTREAM_PIN.md`
    were updated to describe the wider vendor scope. `.gitignore` now also
    excludes `engines/*/vendor/**/data/dsr-catalog.json`, matching
    upstream’s own ignore and `docs/LICENSING.md`’s ban on
    distributing extracted game catalogs.
  - **Doc correction**: upstream `validateConfig(config, { requireGame })`
    *returns* a `string[]` of errors and never throws. The earlier analysis
    said it throws; Task 3 must check the returned array.
  - `engines/combined/vendor/package.json` is repository-authored (not
    vendored) and supplies only `"type": "module"`, without which Node
    reparses the vendored ESM (`MODULE_TYPELESS_PACKAGE_JSON`).
  - Concrete boundary documented in
    `docs/COMBINED_ENGINE_INTEGRATION.md` ("JS core vendor proof").
- Blockers:
  - **New, for Task 4/7**: `apps/backend/src/core/pipeline.ts` probes the
    combined engine at `vendor/src/cli.js` and `vendor/DSR-Randomizer.exe`.
    Neither exists and neither should — `src/cli.js` is deliberately
    excluded and there is no upstream portable `.exe` in this integration
    model — so `ExternalAdapter` keeps reporting the combined engine as
    missing even though the core is vendored. Correct targets:
    `vendor/src/core/generator.js` and the built
    `vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll`. Not
    fixed here: changing it is pipeline wiring, explicitly out of scope.
  - **Unchanged**: no public GPL fork exists yet (account-owning action;
    `docs/LICENSING.md` requires it before distributing any binary).
    Vendoring the JS core does not change this.
  - **Unchanged**: integration Blockers 1, 2, 3, and 6 (silent prototype
    fallback, seed charset, missing `generateSpoilerLog` request field,
    catalog/game-dir coupling) were out of scope and remain open.

---

# TASK 3 — CODEX

State: DONE

## Combined request → upstream config mapping

Implement a pure deterministic mapping from
`CombinedRandomizerRequest`
to the vendored upstream combined-randomizer configuration.

Requirements:

- item option mapping
- enemy option mapping
- documented `enabled` AND-ing behavior
- validate/reject the "everything disabled" case
- no filesystem access
- no subprocess execution
- no game access
- no pipeline wiring

Testing must include:

- items enabled / enemies disabled
- enemies enabled / items disabled
- both enabled
- nested feature enabled while parent disabled
- everything disabled
- deterministic mapping

Completion record:

- Commit: `ecbefa07c5dc096a17b7d81ca5e322149f8463c1`
  (`test(backend): verify combined config mapping`).
- Verification: 6 focused mapper tests passed; contracts and backend builds,
  and backend typecheck passed (2026-09-28).
- Result: The existing pure mapper satisfies the Task 3 mapping and parent
  gating requirements. Added a test rejecting requests that select only
  non-category safeguards. No runtime mapping changes were needed.
- Blockers: None.

---

# TASK 4 — CLAUDE

State: DONE

## Build the minimal combined-generation execution bridge

Connect:

1. typed request
2. config mapping
3. vendored JS generator
4. workspace planner
5. DsrDataTool executor

into a minimal internal combined-engine execution path.

Goal:

Prove that Items + Enemies generation can execute entirely through the
controlled workspace model without upstream `install` or `restore`.

Do NOT:

- connect FogMod
- activate generated files into live DSR
- implement frontend wiring
- redesign backup architecture
- broaden into feature-parity work

If a previously documented blocker prevents correct implementation,
resolve only the blocker required for this execution path.

Verification should use fixtures/mocks where real DSR data is not required.

Document the exact data flow in
`docs/COMBINED_ENGINE_INTEGRATION.md`.

Completion record:

- Commit: `99c0288ddd9d25339889f32108ead6e9e116f355`
  (`feat(backend): add combined-engine execution bridge`).
- Verification (2026-09-28):
  - `npm run test -w @ds1tr/backend` — 54/54 pass (19 new bridge tests).
  - `npm run build -w @ds1tr/backend` clean; root `npm run typecheck`
    (contracts + backend + desktop) clean.
  - The new tests execute the **real** vendored `generate()` and
    `writeOutput()` in a real temporary workspace against a synthetic
    schema-17 catalog (three enemy slots, two archetypes — the minimum that
    yields `dataStatus: "extracted"`). Only the `DsrDataTool` process layer
    is faked; running it needs both the built .NET assembly and a real Dark
    Souls installation, neither of which exists here.
  - Proven by assertion, not inspection: a real package is produced with
    `dataStatus: "extracted"` and three enemy placements; the process layer
    receives the planner's exact `scan`/`patch-enemies` argument vectors and
    nothing else (no `install`/`restore`, no ad-hoc paths); the placements
    file already exists when `patch-enemies` is invoked (asserted from
    *inside* the fake — the only place ordering is observable); a fixture
    game directory is byte-identical before and after a successful run; the
    same request twice yields the same `placementHash`; `scan` runs first
    with the planner's arguments when no catalog exists and is skipped when
    one does; a whitespace-padded seed still lands in the planned package
    directory; and prototype data, an obsolete schema, a malformed catalog,
    an upstream-rejected seed, a workspace inside the game directory, and a
    request with nothing enabled each return their documented code with the
    process layer never invoked and no package directory created.
  - No real Dark Souls installation was involved; nothing outside the
    repository and the OS temp directory was read or written.
- Result:
  - `apps/backend/src/adapters/combinedRandomizerBridge.ts`
    (`executeCombinedGeneration`) is the minimal internal execution path. It
    chains all five pieces in this order: pure config mapping → vendored JS
    core load → `normalizeConfig`/`validateConfig` → workspace plan →
    `scan` (only when the workspace has no catalog) → `generate` →
    `writeOutput` → `patch-enemies`. Returns a typed success carrying
    `seedDirectory`, `placementsPath`, `patchManifestPath`,
    `randomizerVersion`, `placementHash`, `seed`, and `catalogScanned`, or
    one of fourteen documented failure codes; only `GENERATION_FAILED` is
    marked retryable. Exact data flow in
    `docs/COMBINED_ENGINE_INTEGRATION.md` ("Execution bridge proof").
  - **Items + Enemies generation is proven to run entirely through the
    controlled workspace model without upstream `install`/`restore`**, which
    was this task's goal. Activation into the live game is untouched and
    remains a later stage under the backend's own backup authority.
  - **Blocker resolved (integration Blocker 1, required by this path):** the
    silent prototype fallback is now rejected before any file is written and
    before any process is spawned, so a `dataStatus: "prototype"` package can
    never reach a package directory. Also pre-checks
    `catalog.schemaVersion !== 17` so an obsolete catalog reports
    `CATALOG_STALE` instead of an opaque `GENERATION_FAILED` from upstream's
    own throw.
  - **Blocker resolved (handed to this task by Task 2):** `core/pipeline.ts`
    probed the combined engine at `vendor/src/cli.js` and
    `vendor/DSR-Randomizer.exe`, neither of which exists or should, so engine
    status always reported the combined engine as missing. It now probes
    `vendor/src/core/generator.js` and the built
    `vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll`, with a
    test asserting `source` mode. This was a documented-handoff cleanup, not
    a dependency: the bridge never consults `ExternalAdapter` and resolves
    its own paths.
  - **Ordering defect found and avoided:** upstream `writeOutput` derives its
    `seed_<safeSeed>` directory from the *normalized* seed, while the
    workspace planner derives it from whatever seed it is handed. Planning
    from the raw `request.combinedSeed` desyncs the two whenever
    normalization changes anything (`"  abc  "` → planner `seed__abc__`,
    upstream `seed_abc`), pointing `--placements` at a file that was never
    written. The bridge therefore plans from `config.seed` and asserts the
    returned directory equals the planned one
    (`OUTPUT_LAYOUT_MISMATCH`). Recorded in `docs/DECISIONS.md`.
  - Three upstream config keys are pinned rather than inherited from
    `defaultConfig`: `generateSpoilerLog: false` (upstream defaults to
    `true`, which would emit a spoiler file no caller requested),
    `useExtractedData: true`, and `dryRun: false`. The latter two were
    verified inert at the pinned commit — read nowhere in the vendored core
    or in `DsrDataTool/Program.cs` — and are set only so the recorded
    `config.json` states them explicitly.
  - `@ds1tr/backend` bumped to `0.8.0`.
- Blockers:
  - **Blocker 2 (seed charset) — deliberately left open, now observable.**
    `deriveSeeds()` still passes the raw master seed through, so a master
    seed outside upstream's `/^[\w.-]{1,64}$/` surfaces as `INVALID_CONFIG`
    from the bridge. The bridge does *not* silently rewrite the seed:
    choosing a compliant derivation is a reproducibility decision for the
    seed boundary (Task 6), and hiding it here would make the manifest seed
    differ from the seed the user typed.
  - **Blocker 3 (`generateSpoilerLog` request field)** — unchanged; the
    bridge pins the value off rather than adding a contract field.
  - **Blocker 6 (catalog/game-directory coupling)** — unchanged. An existing
    catalog is reused without verifying it was scanned against this
    `gamePath`; `verifyCatalogSources` is not vendored. The
    `forceCatalogRescan` option is a caller-side escape hatch, not staleness
    detection.
  - **`DsrDataTool` has still never actually run from this repository.** The
    process layer was faked in every test, so a genuine `scan` /
    `patch-enemies` round trip against real game data remains unproven —
    that is the end-to-end task's job, not this one's.
  - **No public GPL fork** — unchanged; `docs/LICENSING.md` requires one
    before distributing any binary.

---

# TASK 5 — CODEX

State: DONE

## Combined engine integration tests and failure handling

Review the execution bridge implemented in Task 4.

Add focused tests for:

- successful execution flow
- JS generation failure
- scan process failure
- patch-enemies failure
- invalid workspace
- malformed configuration
- stdout/stderr propagation
- deterministic seed/config behavior
- no activation of live game files

Fix only defects directly exposed by these tests.

Do not refactor unrelated code.

Completion record:

- Commit: `abeb4c2f56ade130a39e4d42837bf333d586e3e0` (`test(backend): cover combined bridge failure paths`); task documentation committed separately.
- Verification: 20/20 focused bridge tests passed; backend build and typecheck passed (2026-09-28).
- Result: Added scan failure and log propagation coverage, strengthened deterministic config and refusal checks, and fixed a test assertion that depended on the runner's working directory. No production code changed.
- Blockers: None for Task 5. Real-game execution and existing integration blockers remain for later tasks.

---

# TASK 6 — CLAUDE

State: ACTIVE

## Resolve remaining combined-engine blockers

Review the blockers documented in:

- `docs/COMBINED_ENGINE_INTEGRATION.md`
- `docs/IMPLEMENTATION_STATUS.md`

Address only blockers required before a real combined-engine generation
run can be attempted.

Known candidates may include:

- seed charset compatibility
- catalog handling
- spoiler-log configuration
- silent prototype fallback

Treat the documentation/current code as authoritative; do not assume this
list is still complete or current.

Do NOT begin FogMod integration.

Completion record:

- Commit: `e2664164d1aa1131952e1ddb1a19a87d960198af`
  (`fix(backend): resolve combined-engine seed, spoiler, and catalog blockers`);
  task documentation committed separately.
- Verification (2026-09-28): `npm run test -w @ds1tr/backend` — 66/66 pass;
  root `npm run typecheck` (contracts + backend + desktop) clean. No real
  Dark Souls installation involved.
- Result:
  - Reviewed blockers 1–6. Blocker 1 (silent prototype fallback) was already
    resolved by Task 4; Blocker 4 is Task 7; Blocker 5's build half was
    already resolved. Blockers 2, 3, and 6 were resolved here.
  - **Blocker 2 (seed charset):** `deriveSeeds().combined` is now the first
    32 hex characters of
    `SHA-256("ds1-triple-randomizer:v1:<master>:combined")`, so every master
    seed satisfies upstream's `/^[\w.-]{1,64}$/`. Fog derivation unchanged.
    Recorded in `docs/DECISIONS.md`.
  - **Blocker 3 (spoiler log):** `generateSpoilerLog` added to
    `CombinedRandomizerRequest` and mapped through; the bridge no longer pins
    it to `false`.
  - **Blocker 6 (catalog/game coupling):** new `verifyCatalogSources`
    checks a reused catalog's `sourceFiles` (containment, existence, size,
    SHA-256) against `gamePath` before generation; mismatch returns
    non-retryable `CATALOG_SOURCE_MISMATCH` with no automatic rescan.
    Recorded in `docs/DECISIONS.md`.
  - `@ds1tr/backend` bumped to `0.9.0`.
- Blockers:
  - **Awaiting a user decision on the Blocker 2 approach.** The implemented
    SHA-256 derivation conflicts with `docs/ARCHITECTURE.md` § Master seed
    and `docs/source-analysis.md` ("approved engine direction"), which say
    the combined engine receives the normalized master seed directly.
    Options put to the user:
    - **(A)** keep the derivation (commit `e266416`); then add superseded
      notes to `ARCHITECTURE.md`, `source-analysis.md`, and
      `CODEX_PROGRESS.md`, drop the "proposed" status in `DECISIONS.md`, and
      mark this task DONE.
    - **(B)** follow `ARCHITECTURE.md`: in a new commit revert
      `seeds.ts`/`seeds.test.mjs` to pass-through, reject a trimmed
      `masterSeed` outside `/^[\w.-]{1,64}$/` (ASCII only) in
      `configValidation.ts` with tests, rewrite the `DECISIONS.md` entry, and
      bump the backend version.
    Blockers 3 and 6 are resolved regardless of the answer.
  - Otherwise none for this task. Remaining before real use: adapter wiring (Task 7),
    a real `DsrDataTool` run against an installation (Task 8), and the
    public GPL fork (external action). The catalog check proves the catalog
    matches the directory, not that the directory is vanilla — pristine
    provenance stays with the pristine import/backup authority.

---

# TASK 7 — CODEX

State: PENDING

## Combined engine runnable adapter

Connect the proven combined execution bridge to the existing
`CombinedRandomizerEngine.generate(...)` implementation.

Requirements:

- preserve typed request/result/event contracts
- progress/log propagation
- deterministic seeds
- typed failures
- workspace-only generation
- no live-game activation
- no FogMod work

Add focused adapter tests.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 8 — CLAUDE

State: PENDING

## Combined engine end-to-end generation proof

Perform the first practical end-to-end validation of the combined
Items + Enemies engine using appropriate test data or a real DSR
installation only if safely available.

Goal:

request
→ config
→ JS generator
→ scan/patch
→ workspace output

Prove:

- generation completes
- expected outputs exist
- live game files are not modified
- deterministic rerun behavior
- failures remain recoverable

Do NOT activate generated output into the live game yet.

Document results thoroughly.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 9 — CLAUDE

State: PENDING

## FogHost integration design

Begin the FogMod phase.

Use the existing FogMod research.

Design the minimal dedicated C# host around FogMod's callable engine.

Requirements:

- DS1R explicitly selected
- no reliance on the suspicious existing FogMod CLI path
- JSON request/response boundary
- workspace input/output
- typed errors/progress where practical
- no GUI automation

Analyze concrete MSB/EMEVD/GameParam overlap with the combined engine.

Do not implement broad UI work.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 10 — CODEX

State: PENDING

## Implement FogHost process adapter

Implement the backend-side process boundary defined by Task 9.

Keep the implementation small and typed.

Add focused process/contract tests.

Do not implement compatibility policy itself unless explicitly defined
by the preceding task.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 11 — CLAUDE

State: PENDING

## Combined → Fog compatibility bridge

Implement or define the concrete compatibility layer required for
FogMod to consume the combined Items + Enemies output safely.

Focus specifically on overlapping:

- MSB
- EMEVD
- GameParam

Establish deterministic retry/rejection behavior where required.

Do not begin frontend work.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 12 — CODEX

State: PENDING

## Full backend pipeline wiring

Wire:

verify/import
→ workspace
→ Items + Enemies
→ validation
→ Fog
→ final validation
→ generated package

Do NOT activate into the game yet.

Implement progress/status propagation and manifest recording using
existing contracts.

Add focused pipeline tests.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 13 — CLAUDE

State: PENDING

## Activation and restore architecture implementation

Implement the single-authority activation/restore workflow.

Requirements:

- pristine restore source
- generated package activation
- safe state detection
- refusal on unknown state
- vanilla restore
- no upstream independent backup chains

Destructive behavior must be guarded and tested carefully.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 14 — CODEX

State: PENDING

## Desktop/backend integration

Connect the backend pipeline to the Electron application through the
existing typed boundary.

Implement:

- generate action
- progress
- logs
- errors
- result
- activation
- restore

Do not redesign randomizer algorithms.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 15 — CODEX

State: PENDING

## User interface completion

Implement the user-facing controls required by the currently supported
combined + fog options.

Include:

- master seed
- item options
- enemy options
- fog/world options
- generation progress
- logs/errors
- generated-run status
- activate
- restore

Use the existing React/TypeScript architecture.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 16 — CLAUDE

State: PENDING

## Final end-to-end audit

Perform a final integration audit of the complete application.

Verify:

- deterministic runs
- combined engine
- Fog engine
- retries
- manifests
- pristine restore
- activation
- failure recovery
- licensing/notices
- no unlicensed legacy code
- no accidental live-game writes during intermediate generation

Fix only issues required for correctness/release readiness.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 17 — CODEX

State: PENDING

## Packaging and release readiness

Finish desktop packaging and release checks.

Verify:

- Electron production build
- bundled required runtime/assets
- DsrDataTool availability
- FogHost availability
- vendored license notices
- clean-install startup
- useful errors when DSR is not found
- version metadata

Do not publish or push a release unless explicitly authorized.

Completion record:

- Commit:
- Verification:
- Result:
- Blockers:
