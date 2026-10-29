# Ceaseless QA telemetry helper

`QaTelemetry` is a standalone .NET 8 development tool. It has no application, backend, FogHost, or game-data project reference. It is not included in product packaging.

## Current trust state

This build verifies the selected package and external runtime identity, and it validates synthetic JSONL trace mechanics. **It does not open, launch, or read a game process.** No offsets or pointer chains have been verified for the locked executable. The only process-access rights represented by the policy are query-limited-information (`0x1000`) and memory-read (`0x0010`); this build requests `0x0000` because no live reader is enabled.

The case descriptor intentionally records the QA runtime profile and all ten telemetry fields as `UNVERIFIED`. The trace importer has no gameplay-promotion path. Synthetic or otherwise unverified traces always retain `UNKNOWN` shadow certainty and can never produce `GAMEPLAY_CONFIRMED`.

## Build and test

Use the portable .NET 8 SDK and a NuGet configuration with package sources cleared; the projects have no package dependencies.

```powershell
dotnet restore tools/qa-telemetry/QaTelemetry.csproj --configfile <empty-nuget.config>
dotnet build tools/qa-telemetry/QaTelemetry.csproj --no-restore --configuration Release
dotnet restore tools/qa-telemetry/tests/QaTelemetry.SelfTest.csproj --configfile <empty-nuget.config>
dotnet run --project tools/qa-telemetry/tests/QaTelemetry.SelfTest.csproj --no-restore --configuration Release
```

## Commands

```text
QaTelemetry verify-case --case <case.json>
QaTelemetry preflight --case <case.json>
QaTelemetry trace-import --case <case.json> --trace <session.jsonl> --summary <session.summary.json> --summary-sha <session.summary.sha256>
```

`verify-case` requires the external descriptor SHA-256 `2b1ff04c7e8b8cef8b6fa61859128911a195dee0cf7ee31b5e9d3f480ddae9ab`, then hashes the exact package, its `run-manifest.json`, its per-file manifest, and the pinned contemporaneous V2 attestation. It rejects Steam paths, reparse points, hard links, unknown files, path escapes, and identity mismatches. A matching local attestation is a hash anchor, not an external signature.

`preflight` never starts the game. It verifies the locked executable path/hash and checks for a matching top-level `.bhd`/`.bdt` pair. It still refuses to proceed while the runtime inventory profile or telemetry profile is unverified.

`trace-import` verifies JSONL sequence/session identity, package and executable hashes, a completion summary, trace SHA-256, and a separate summary SHA-256. These hashes detect changes after recording; they do not make local files tamper-proof. It checks declared observation status values but does not independently validate marker procedures. This build always returns `UNKNOWN` shadow certainty for imported observations and never emits `GAMEPLAY_CONFIRMED`. Enabling that result requires independently validated markers plus a reviewed importer for ordered checkpoints, event-flag transition, sampling continuity, process identity, and absence of artificial warps.

## One-launcher path

Run [`START-CEASELESS-QA.cmd`](START-CEASELESS-QA.cmd). It invokes only package/runtime preflight and pauses on failure. The current checked QA root is a data-only snapshot, so the launcher will report `QA_RUNTIME_INCOMPLETE` and will not start the game. That is intentional.

## Fixed gameplay checkpoints

1. `CEASELESS_PRE_FIGHT`
2. `CEASELESS_BOSS_ACTIVE`
3. `CEASELESS_DEFEATED`
4. `EXIT_APPROACH`
5. `EXIT_PROMPT_PRESENT`
6. `EXIT_CROSSED`
7. `DESTINATION_LANDED`
8. `MAIN_DEMON_RUINS_REACHED`
9. `LOWER_LAVA_REACHED`
10. `POST_RELOAD_EXIT_VERIFIED`

The gameplay pass requires the locked package, validated telemetry markers, flag `11410410` observed set after the Ceaseless defeat, an actionable exit, a successful transition and valid landing, reaching the main Demon Ruins and lower lava route without an artificial warp, and the same progression remaining available after reload.

Failure categories are kept distinct: `NO_EXIT_PROMPT`, `EXIT_NOT_ACTIONABLE`, `TRANSITION_FAILED`, `WRONG_DESTINATION`, `UNDER_MAP_LANDING`, `LANDING_ISOLATED`, `MAIN_ROUTE_UNREACHABLE`, `LOWER_LAVA_UNREACHABLE`, `POST_RELOAD_FAILURE`, `TELEMETRY_UNVERIFIED`, and `PACKAGE_IDENTITY_MISMATCH`.

Raw Fog seed `3695053466`, accepted attempt `4`, and the mapped V5 Fog options are stored in the external case descriptor for a later single-seed upstream comparison. No upstream run or parity claim is made by this preparation.
