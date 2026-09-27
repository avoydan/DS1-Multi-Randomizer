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

State: BLOCKED

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

---

# TASK 2 — CLAUDE

State: PENDING

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

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 3 — CODEX

State: PENDING

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

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 4 — CLAUDE

State: PENDING

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

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 5 — CODEX

State: PENDING

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

- Commit:
- Verification:
- Result:
- Blockers:

---

# TASK 6 — CLAUDE

State: PENDING

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

- Commit:
- Verification:
- Result:
- Blockers:

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
