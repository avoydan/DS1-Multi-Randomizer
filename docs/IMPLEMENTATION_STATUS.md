# Implementation status

## Backend adapters

- The minimal `CombinedRandomizerAdapter` boundary is defined for combined item
  and enemy generation requests, progress/log events, and typed results.
- The adapter is currently a side-effect-free placeholder that returns
  `NOT_IMPLEMENTED`.
- The adapter is not connected to the generation pipeline and does not execute
  an external randomizer or access game/workspace files.
- A pure, deterministic mapping layer converts combined requests to the
  upstream item/enemy configuration shape, applies parent `enabled` flags, and
  rejects requests with no enabled randomization category.
- A pure, deterministic workspace planner
  (`combinedRandomizerWorkspace.ts`) computes the catalog path, per-seed
  output directory, and `scan`/`patch-enemies` argument vectors for a
  generation run, and refuses to plan a workspace located inside the live
  game directory. It performs no filesystem I/O and never constructs
  `install`/`restore` arguments. See
  `docs/COMBINED_ENGINE_INTEGRATION.md` ("Workspace execution proof").
- `kellernz1/ds-randomizer` is selectively vendored (files needed for
  `DsrDataTool.dll` only) and pinned at commit
  `f120efe0e95b957b7e76109868c3c9966e22720c` under
  `engines/combined/vendor/` (`UPSTREAM_PIN.md` has the full file list and
  update procedure). `DsrDataTool.dll` builds successfully from this
  vendor directory with a locally-installed .NET 8 SDK; see
  `docs/COMBINED_ENGINE_INTEGRATION.md` ("Vendored build proof") for the
  exact command and artifact path.
- `DsrDataToolExecutor` runs only the planner-provided `scan` and
  `patch-enemies` argument vectors through Node `child_process.spawn`, streams
  typed stdout/stderr log events, emits coarse process-status events, and
  returns typed success, process-failure, or spawn-failure results. It is not
  wired into the adapter or generation pipeline yet. Task 1's implementation
  is in commit `5402b447f3d53a1896ca59267fc5fe10461bc071`; its seven
  focused tests, backend build, and backend typecheck passed on 2026-09-27.
  The task remains blocked because Git could not create `.git/index.lock` to
  commit the task's plan/status update under the current filesystem policy.
- Not yet done: vendoring the upstream JS core, a public fork (required by
  `docs/LICENSING.md` before any distribution), and an actual subprocess
  orchestration sequence connecting the existing planner and executor.
  The adapter's `generate()` is still `NOT_IMPLEMENTED`; the pipeline does not
  invoke `DsrDataTool.dll` yet.
