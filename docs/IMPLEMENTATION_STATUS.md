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
- Not yet done: vendoring the upstream JS core, a public fork (required by
  `docs/LICENSING.md` before any distribution), and an actual subprocess
  executor that spawns `scan`/`patch-enemies` using the workspace plan.
  The adapter's `generate()` is still `NOT_IMPLEMENTED`; nothing in this
  repository invokes `DsrDataTool.dll` yet.
