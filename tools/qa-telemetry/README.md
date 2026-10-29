# Ceaseless QA telemetry helper

QaTelemetry is a standalone .NET 8 owner-QA tool, version 0.5.0. It has no reference to the application, backend, FogHost, or randomizer projects and is not included in product packaging.

## Trust boundary

The helper uses two deliberately separate identities:

**Executable telemetry profile:** calibration is performed only against the manifest-verified clean runtime at `C:\Users\Adam\ds1tr-qa\runtime\DARK SOULS REMASTERED`. The EXE SHA-256 is `a45aaa36dd2f6cc151670a639ea5547043cf38ea79ff4178b963c6ed71f98d7b`; the clean manifest SHA-256 is `89a9dc2a0fa7dbf990521e7adb9f27586c140334676639a88edeb1bd4b2d3729`. The profile binds that exact executable, a stable executable-profile ID, the tool version, and a canonical hash covering signature patterns, candidate pointer/field structures, and flag layout. Its evidence records vanilla Asylum/Firelink confirmations, movement/position checks, the Asylum-to-Firelink transition, and independently known true/false flags. It contains no Ceaseless package, run-manifest, runtime digest, or Seed Code identity.

**Ceaseless gameplay session:** recording and import remain bound to the exact package-applied QA runtime, package, selected case, Seed Code, process path, and executable profile identity:

- Executable: C:\Users\Adam\ds1tr-qa\runtime\DARK SOULS REMASTERED - CEASELESS QA\DarkSoulsRemastered.exe
- Executable SHA-256: a45aaa36dd2f6cc151670a639ea5547043cf38ea79ff4178b963c6ed71f98d7b
- Runtime digest: 5f182859f78f285a89a31a608ea28dc5b1cb069b082aa906637da11aea2873ef
- Selected package tree SHA-256: 32f0c2afeff25c9341405989b80ba7dc6a72da1f3e90ecdf90cf37ca2b5f79a9
- Selected run-manifest.json SHA-256: e81a504da9c251d9e784eb6708133aa3e98a34cd4df9757d4eee3e259cf7324d

Preflight and static executable audit do not open a game process. `launch-calibration` verifies the pinned clean-runtime manifest and same-user Steam, provisions or verifies the clean-root AppID sidecar, and starts only the clean QA executable with its runtime as the working directory. `launch-qa` independently verifies the certified package and package-applied runtime before it starts the Ceaseless executable. Each command checks the resulting PID image path and executable hash. Neither uses a Steam URI. Calibration and recording attach only to their mode's exact executable path and hash. Launch path checks request PROCESS_QUERY_LIMITED_INFORMATION only; the live reader requests PROCESS_QUERY_LIMITED_INFORMATION and PROCESS_VM_READ (0x1010) only. Static preflight requests no game process rights (0x0000). No process-writing, memory-writing, injection, or game-input API is present.

The exact ASCII bytes `570940` at either approved isolated QA runtime's root `steam_appid.txt` are launch-only sidecars. Each is excluded from that runtime's immutable manifest inventory only after its exact root path, bytes, SHA-256, non-reparse status, and single-link identity are checked separately. The clean manifest SHA and package tree/runtime identities stay stable. A malformed existing file is not overwritten, and no sidecar is written to the Steam installation. The clean-root sidecar is provisioned only when the owner runs the calibration launcher; it is currently absent.

The three exact-build signatures are unique in the locked executable's .text sections and resolve to .data targets. This proves exact-build byte-anchor uniqueness, not pointer-chain meaning. Candidate pointer and flag layouts are included in the telemetry-definition hash. The player position and event-flag chains remain candidates until owner calibration. The map reader observes a candidate OnlineAreaID and must be confirmed in the clean vanilla world at Northern Undead Asylum and Firelink Shrine; it does not infer location from coordinates.

Candidate signature and flag-layout evidence is recorded in ExactBuild.cs:

- worldChrMan and eventFlags signatures plus candidate pointer-chain/flag-bank semantics were compared with the Remastered.cs implementation in SoulSplitter: https://github.com/FrankvdStam/SoulSplitter/blob/main/src/SoulMemory/DarkSouls1/Remastered.cs
- The candidate OnlineAreaID chain was compared with the DarkSoulsRemastered.CT record in RemasterCETable: https://raw.githubusercontent.com/JohrnaJohrna/RemasterCETable/master/DarkSoulsRemastered.CT

These community files are evidence for candidate definitions only. They are not runtime dependencies, are not copied into this project, and do not replace the project-owned exact-build checks or owner marker procedure.

## Build and offline tests

Use the installed .NET 8 SDK. Both projects are BCL-only.

    dotnet build tools/qa-telemetry/QaTelemetry.csproj --configuration Release --output tools/qa-telemetry/bin/QA-0.5.0
    $out = Join-Path (Get-Location) 'tools\qa-telemetry\bin\QA-0.5.0-tests\'
    dotnet build tools/qa-telemetry/tests/QaTelemetry.SelfTest.csproj --configuration Release --no-incremental "-p:BaseOutputPath=$out"
    dotnet tools/qa-telemetry/bin/QA-0.5.0-tests/Release/net8.0/QaTelemetry.SelfTest.dll

The self-test console checks package/runtime fail-closed behavior, process path/hash and rights policy, signature uniqueness rules, pointer/coordinate/map/flag bounds, marker-profile binding and revocation, JSONL hash/import checks, synthetic-data isolation, and a source scan for process-writing/injection APIs. It does not launch DSR or access live process memory.

## Commands

    QaTelemetry verify-calibration-runtime
    QaTelemetry audit-build --target clean
    QaTelemetry launch-calibration
    QaTelemetry calibrate-markers
    QaTelemetry verify-profile --profile <executable-profile.json>
    QaTelemetry launch-qa --case <case-runtime.json>
    QaTelemetry audit-build --target ceaseless
    QaTelemetry record --case <case-runtime.json> --profile <verified-profile.json> [--trace <temp-path.jsonl>]
    QaTelemetry verify-case --case <case-runtime.json>
    QaTelemetry preflight --case <case-runtime.json>
    QaTelemetry trace-import --case <case-runtime.json> --trace <session.jsonl> --summary <session.summary.json> --summary-sha <session.summary.sha256>

audit-build hashes and scans only the locked executable. Each signature must match exactly once and its RIP-relative target must be in .data or .bss; zero matches, multiple matches, a wrong image hash, or a changed target section leaves the audit unverified and disables live modes.

verify-case verifies the exact package tree, run manifest, package file hashes, and pinned local V2 attestation. preflight verifies the separate clean and package-applied runtime attestations and requires the package-applied runtime path. It rejects Steam, clean-runtime-as-target, reparse points, hard links, and identity mismatches.

The launcher invokes `launch-qa` after the static checks. That command refuses to continue if Steam is absent, belongs to a different OS user, or cannot be identified; if any DSR process is already running; if package/runtime/DLL/sidecar verification fails; or if the resulting process image is not the locked isolated executable. The command starts the exact executable directly with `WorkingDirectory` set to the QA runtime. It never starts the installed Steam-library DSR and never invokes `steam://run/570940`.

After launch-path verification, `calibrate-markers` attaches only to the already-running clean QA executable. It asks the owner to load a vanilla world and guides these checks:

1. confirm Northern Undead Asylum from the vanilla in-game location;
2. stand still for stable XYZ readings, then walk at least 1.5 m and confirm coherent movement;
3. use vanilla progression from the Asylum to Firelink Shrine and confirm the candidate map value changes and matches the Firelink location;
4. provide one independently known false and one independently known true vanilla event flag, with the in-game reason for each.

Only a successful complete calibration writes a VERIFIED executable telemetry profile and SHA-256 sidecar under `%TEMP%\ds1tr-qa-telemetry\marker-profiles`. The profile is independent of gameplay package/runtime/Seed Code identity. It records clean-runtime calibration provenance and evidence but can be reused only when the exact EXE hash, executable-profile ID, tool version, and telemetry-definition hash match. A later live pointer/field read failure creates a separate `.invalid` marker and blocks further use.

record rechecks package/runtime/profile/executable identity, then reads the process on a background sampler while the console accepts owner checkpoint annotations. Sampling continues while explicit y/n outcome prompts are answered. Checkpoint keypresses mark only the owner's report that the checkpoint was reached; they never imply that an exit was actionable, a transition succeeded, a landing was safe, or a route milestone was reached. W opens an explicit interruption annotation for death, warp/teleport, loading, or a sampling gap. No keypress is sent to DSR.

The four primary signals are map/area, XYZ, event flag 11410410, and observed map transitions. Map/position/flag observations remain CANDIDATE_UNVERIFIED until marker validation, then are labelled SESSION_VERIFIED for the exact process and profile. Unknown area IDs stay candidate-only. Flag 11410410 resolves to the candidate DSR flag word at byte offset 0x3c30, mask 0x20; the owner procedure checks both a known false and a known true flag before the field can be trusted. Barrier/object state and action-prompt memory are not implemented.

JSONL traces are created with CreateNew under %TEMP%, one record per line, and cannot overwrite an existing trace. Each record links the executable, runtime, package tree, run manifest, marker profile, process identity, field trust, actual sample time, and owner annotation time. A completed session writes a summary plus summary SHA-256. These local hashes detect accidental or post-write changes but are not signatures and cannot prevent a determined local user from fabricating data.

trace-import accepts only the pinned external case and exact package identity. Synthetic traces are returned as SYNTHETIC_TEST_PASS with every shadow edge UNKNOWN; unverified live input returns TELEMETRY_UNVERIFIED; neither can promote gameplay certainty. A verified trace can classify only this package's Ceaseless route after the ordered owner checkpoints, verified boss flag, actionable/crossed exit, map transition, valid landing, physical route milestones, and no recorded death/warp/loading/discontinuity. It does not modify the production validator or certify a global mechanism class.

## Launcher and owner next step

Run `VALIDATE-TELEMETRY-MARKERS.cmd` first. It verifies the clean runtime, audits the clean executable, checks same-user Steam, provisions/verifies the clean-QA AppID sidecar, launches only that executable, verifies its process path, and starts vanilla calibration. It does not read or launch the certified package.

After calibration emits a verified profile, run `START-CEASELESS-QA.cmd <profile-path>`. The launcher requires and verifies that profile before package checks and launch. The live trace binds case ID, exact Seed Code, runtime digest and runtime-manifest SHA, package tree and run-manifest SHA, process image path/PID/start time, and executable-profile ID plus profile SHA. Import refuses any mismatch. At this checkpoint no owner calibration has run and no gameplay trace exists.

## Ceaseless route annotations

The fixed owner checkpoint order is:

CEASELESS_PRE_FIGHT -> CEASELESS_BOSS_ACTIVE -> CEASELESS_DEFEATED -> EXIT_APPROACH -> EXIT_PROMPT_PRESENT -> EXIT_CROSSED -> DESTINATION_LANDED -> MAIN_DEMON_RUINS_REACHED -> LOWER_LAVA_REACHED -> POST_RELOAD_EXIT_VERIFIED.

The exact expected route identity remains event flag 11410410, entrance o4961_0000, and the generated graph-side destination label Demon Ruins Lava. This tooling does not claim that destination is physically reachable.
