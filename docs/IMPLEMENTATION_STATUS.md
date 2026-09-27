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
- Not yet done: vendoring the pinned fork, building `DsrDataTool.dll`, and
  an actual subprocess executor that spawns `scan`/`patch-enemies` using
  this plan. The adapter's `generate()` is still `NOT_IMPLEMENTED`.
