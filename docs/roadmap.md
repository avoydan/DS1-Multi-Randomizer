# Implementation roadmap

## Milestone 1 - shell and contracts (started)

- [x] React + TypeScript + Vite renderer
- [x] Electron desktop host
- [x] Separate local backend process/API
- [x] Shared contracts
- [x] DS1R directory validation
- [x] deterministic master seed -> combined master input + Fog retry stream
- [x] pipeline preview

## Milestone 2 - combined engine compatibility spike

- [x] Compare combined engine with both legacy randomizers.
- [x] Adopt GPL-3.0-only for the application.
- [x] Model native and legacy-parity settings.
- [ ] Create the pinned public fork.
- [ ] Define a stable headless JSON/CLI boundary.
- [ ] Generate into a disposable full-game workspace.
- [ ] Prove deterministic output across a seed matrix.
- [ ] Prove or implement combined-to-Fog event and key-location compatibility.

## Milestone 3 - Fog adapter

Fog is integrated after the combined compatibility boundary is proven.

- [ ] Create `FogMod.Host` console project.
- [ ] JSON request on stdin or request file.
- [ ] Instantiate `RandomizerOptions` without WinForms.
- [ ] Call `Randomizer.Randomize` for `FromGame.DS1R`.
- [ ] Support `dryrun` before write.
- [ ] Normalize errors into structured JSON codes (`UNSOLVABLE_ITEM_LOGIC`, `INVALID_MSB`, `IO_ERROR`, etc.).
- [ ] Stream progress lines to the backend.

## Milestone 4 - Item parity extensions

- [ ] Specify each legacy-visible behavior without copying legacy source.
- [ ] Implement missing behavior in the GPL combined fork.
- [ ] Add deterministic characterization tests for every option.
- [ ] Expose an option only after its adapter mapping and tests pass.

## Milestone 5 - Enemy parity extensions

- [ ] Specify each legacy-visible behavior without copying legacy source.
- [ ] Implement missing behavior in the GPL combined fork.
- [ ] Add seed-matrix and map/event validation for every option.
- [ ] Add custom enemy-pool import with an independently defined format.

## Milestone 6 - transactional pipeline

- [ ] Vanilla baseline snapshot/fingerprints.
- [ ] Per-run workspace.
- [ ] Combined Items+Enemies -> compatibility bridge -> Fog dry-run -> Fog write.
- [ ] Automatic scoped retries.
- [ ] Final validation.
- [ ] Atomic install/copy into DS1R.
- [ ] Restore Vanilla button.
- [ ] Run manifest with upstream versions, options and derived seeds.

## Milestone 7 - product polish

- [ ] Complete option parity with upstream GUIs.
- [ ] Presets/import/export.
- [ ] Live progress + logs via SSE/WebSocket.
- [ ] One-click launch DS1R offline warning.
- [ ] Build signed/portable Windows release.
