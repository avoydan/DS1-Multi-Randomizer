# Implementation roadmap

## Milestone 1 - shell and contracts (started)

- [x] React + TypeScript + Vite renderer
- [x] Electron desktop host
- [x] Separate local backend process/API
- [x] Shared contracts
- [x] DS1R directory validation
- [x] deterministic master seed -> three engine seeds
- [x] pipeline preview

## Milestone 2 - Fog adapter

Fog is the cleanest engine boundary and should be integrated first.

- [ ] Create `FogMod.Host` console project.
- [ ] JSON request on stdin or request file.
- [ ] Instantiate `RandomizerOptions` without WinForms.
- [ ] Call `Randomizer.Randomize` for `FromGame.DS1R`.
- [ ] Support `dryrun` before write.
- [ ] Normalize errors into structured JSON codes (`UNSOLVABLE_ITEM_LOGIC`, `INVALID_MSB`, `IO_ERROR`, etc.).
- [ ] Stream progress lines to the backend.

## Milestone 3 - Item adapter

- [ ] Pin a compatible Python runtime.
- [ ] Copy/fork only after redistribution terms are resolved; otherwise load an external upstream installation.
- [ ] Extract GUI-independent configuration mapping.
- [ ] Add explicit input/output paths; never operate directly on the real game directory.
- [ ] Disable engine-owned `.bak` behavior.
- [ ] Produce cheatsheet/hintsheet/run metadata into our run directory.

## Milestone 4 - Enemy adapter

- [ ] Split `Randomizer.__init__` inspection/setup side effects.
- [ ] Remove Tkinter dependencies from engine path.
- [ ] Inject game root, engine data root, workspace root and logger.
- [ ] Keep original MSB/Lua/FFX algorithms.
- [ ] Make backups a no-op inside the engine; backend owns transaction state.

## Milestone 5 - transactional pipeline

- [ ] Vanilla baseline snapshot/fingerprints.
- [ ] Per-run workspace.
- [ ] Items -> Enemies -> Fog dry-run -> Fog write.
- [ ] Automatic scoped retries.
- [ ] Final validation.
- [ ] Atomic install/copy into DS1R.
- [ ] Restore Vanilla button.
- [ ] Run manifest with upstream versions, options and derived seeds.

## Milestone 6 - product polish

- [ ] Complete option parity with upstream GUIs.
- [ ] Presets/import/export.
- [ ] Live progress + logs via SSE/WebSocket.
- [ ] One-click launch DS1R offline warning.
- [ ] Build signed/portable Windows release.
