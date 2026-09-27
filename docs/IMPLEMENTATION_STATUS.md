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
