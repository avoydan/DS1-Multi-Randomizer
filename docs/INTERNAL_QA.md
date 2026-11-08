# Internal QA mode

Status: 0.42.0. Decided after the owner's full V7 playthrough: **the solver route is internal QA material, not a product feature.**

## What is internal

The **Solver Route** (`docs/SOLVER_WITNESS.md`) is a complete route to Gwyn: the order of areas, gates, key items and boss completions the solver believes. It is a spoiler of the whole world and a debugging aid for us. A normal build of the application therefore has none of it:

| Surface | Normal (public) build | Internal QA build |
|---|---|---|
| "Solver route" menu items, the viewer, **Copy** | not rendered (not hidden by CSS: the backend reports no capability and the components are not mounted) | present |
| `/api/solver-route/get` | not routed | served |
| `solverRouteAvailable` in the history list | field absent | present |
| `solver-witness.json`, `solver-route.md` in the run folder | never written | written next to the run |
| FogHost sidecar `fog-N-M/solver-witness.json` | never written | written |
| FogMod's own route listing in `foghost.log` ("From X (gate) to Y" lines, the area/predecessor tree) | not printed | printed |
| `generation/solver-route.md`, `generation/solver-witness.json` in a support bundle | not on the allowlist: a bundle that is offered them is refused (`BUNDLE_INVALID_ENTRY`) | allowed |

What stays in every build: the **solvability check itself** (every area is reachable from the start, with FogMod's own search and the V7 GraphChecker; a layout that fails is retried with the next deterministic Fog seed exactly as before), the validation results and attempt records in the run manifest, and the summary numbers.

## What does not change

The mode only decides whether route diagnostics are produced and shown. For one Seed Code a public and an internal build generate **the same world**: same Seed Code, same Fog topology, same package, same replay. The accepting search (`StartReachabilityValidator.Validate` with its observer) runs in both; only the writing of the witness and the console listing is conditional. This is checked by the golden fixtures (V1-V7 verify with and without the switch) and by `tools/fog-host/tests` (`internal QA: a host run writes no route listing unless enabled, and the same files either way`).

## How it is switched on

Both must hold:

1. the backend process is started with `DS1TR_INTERNAL_QA=1` (exactly `1`);
2. the build is not public: a packaged build whose `build-info.json` says `channel: "public"`, or whose build-info is missing, unreadable or malformed, **never** enables it whatever the environment says. A backend without a packaged runtime (development from source, the test suites) has nothing to ask and follows the variable.

So an internal QA build is an `internal`-channel package (`npm run package:win -w @ds1tr/desktop`) started with `DS1TR_INTERNAL_QA=1`. There is no Settings option, no checkbox, no URL parameter, and the renderer cannot turn it on: it only asks `GET /api/capabilities` (`{ internalQa }`) and renders what the backend reports.

FogHost never reads the player's environment for this: the backend sets (or removes) `DS1TR_INTERNAL_QA` in the host's environment from its own decision (`apps/backend/src/core/internalQa.ts`, `hostEnvironment`).

## Where the code is

- `apps/backend/src/core/internalQa.ts`: the decision (`internalQaEnabled`) and the host environment.
- `apps/backend/src/server.ts`: `INTERNAL_QA` gates the witness persistence, `/api/solver-route/get`, the history flag; `/api/capabilities` reports it.
- `apps/backend/src/core/support.ts`, `supportBundle.ts`: the two route entries live in `INTERNAL_QA_BUNDLE_ENTRIES`, outside `BUNDLE_ALLOWLIST`.
- `tools/fog-host/src/InternalQa.cs`: the host's switch. `HostRunner.cs` writes the sidecar only behind it; `vendor/FogMod/GraphConnector.cs` prints FogMod's predecessor listing only behind it (a marked change to the Apache-2.0 vendored file; no other behavior of the file is touched).
- `apps/desktop/src/App.tsx`, `RunHistoryPanel.tsx`: the entries are rendered only when the backend reports the capability.

Tests: `apps/backend/src/core/internalQa.test.mjs`, `solverWitness.test.mjs`, `apps/desktop/src/ui/solverRoute.test.mjs`, `tools/fog-host/tests/InternalQaTest.cs`.

## Old runs

A run folder written by an older application (0.41.x and earlier QA builds) may already hold a `solver-witness.json`/`solver-route.md` or a `foghost.log` with FogMod's listing. A public build does not read them (no route is served, no bundle carries them). "Clear history" removes the run folders.
