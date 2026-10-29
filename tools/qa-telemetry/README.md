# Ceaseless QA telemetry helper

QaTelemetry is a standalone .NET 8 owner-QA tool, version 0.3.0. It has no reference to the application, backend, FogHost, or randomizer projects and is not included in product packaging.

## Trust boundary

The helper is pinned to this package-applied QA executable and runtime:

- Executable: C:\Users\Adam\ds1tr-qa\runtime\DARK SOULS REMASTERED - CEASELESS QA\DarkSoulsRemastered.exe
- Executable SHA-256: a45aaa36dd2f6cc151670a639ea5547043cf38ea79ff4178b963c6ed71f98d7b
- Runtime digest: 5f182859f78f285a89a31a608ea28dc5b1cb069b082aa906637da11aea2873ef
- Selected package tree SHA-256: 32f0c2afeff25c9341405989b80ba7dc6a72da1f3e90ecdf90cf37ca2b5f79a9
- Selected run-manifest.json SHA-256: e81a504da9c251d9e784eb6708133aa3e98a34cd4df9757d4eee3e259cf7324d

Preflight and static executable audit do not open a game process. Live marker validation and recording attach only after checking the exact executable path, executable hash, process creation identity, package identity, and package-applied runtime manifests. The live reader requests PROCESS_QUERY_LIMITED_INFORMATION and PROCESS_VM_READ (0x1010) only. Static preflight requests no process rights (0x0000). No process-writing or injection API is present. The helper never launches a game or sends game input; the owner launches the isolated QA executable manually.

The three exact-build signatures are unique in the locked executable's .text sections and resolve to .data targets. This proves exact-build byte-anchor uniqueness, not pointer-chain meaning. The player position and event-flag chains remain candidates until owner marker validation. The map reader observes a candidate OnlineAreaID and must be confirmed by visiting Firelink Shrine, Northern Undead Asylum, and Demon Ruins; it does not infer location from coordinates.

Candidate signature and flag-layout evidence is recorded in ExactBuild.cs:

- worldChrMan and eventFlags signatures plus candidate pointer-chain/flag-bank semantics were compared with the Remastered.cs implementation in SoulSplitter: https://github.com/FrankvdStam/SoulSplitter/blob/main/src/SoulMemory/DarkSouls1/Remastered.cs
- The candidate OnlineAreaID chain was compared with the DarkSoulsRemastered.CT record in RemasterCETable: https://raw.githubusercontent.com/JohrnaJohrna/RemasterCETable/master/DarkSoulsRemastered.CT

These community files are evidence for candidate definitions only. They are not runtime dependencies, are not copied into this project, and do not replace the project-owned exact-build checks or owner marker procedure.

## Build and offline tests

Use the installed .NET 8 SDK. Both projects are BCL-only.

    dotnet build tools/qa-telemetry/QaTelemetry.csproj --configuration Release
    dotnet build tools/qa-telemetry/tests/QaTelemetry.SelfTest.csproj --configuration Release
    dotnet run --project tools/qa-telemetry/tests/QaTelemetry.SelfTest.csproj --configuration Release --no-build

The self-test console checks package/runtime fail-closed behavior, process path/hash and rights policy, signature uniqueness rules, pointer/coordinate/map/flag bounds, marker-profile binding and revocation, JSONL hash/import checks, synthetic-data isolation, and a source scan for process-writing/injection APIs. It does not launch DSR or access live process memory.

## Commands

    QaTelemetry audit-build
    QaTelemetry marker-guide --case <case-runtime.json>
    QaTelemetry record --case <case-runtime.json> --profile <verified-profile.json> [--trace <temp-path.jsonl>]
    QaTelemetry verify-case --case <case-runtime.json>
    QaTelemetry preflight --case <case-runtime.json>
    QaTelemetry trace-import --case <case-runtime.json> --trace <session.jsonl> --summary <session.summary.json> --summary-sha <session.summary.sha256>

audit-build hashes and scans only the locked executable. Each signature must match exactly once and its RIP-relative target must be in .data or .bss; zero matches, multiple matches, a wrong image hash, or a changed target section leaves the audit unverified and disables live modes.

verify-case verifies the exact package tree, run manifest, package file hashes, and pinned local V2 attestation. preflight verifies the separate clean and package-applied runtime attestations and requires the package-applied runtime path. It rejects Steam, clean-runtime-as-target, reparse points, hard links, and identity mismatches.

marker-guide never starts the game. It first verifies the package, runtime, and exact-build signature scan, then asks the owner to launch the isolated executable and load the selected world. It guides these checks:

1. visit Firelink Shrine, Northern Undead Asylum, and Demon Ruins and confirm each displayed candidate area ID;
2. stand still for stable XYZ readings, then walk at least 1.5 m and confirm coherent movement;
3. cross an ordinary known transition and confirm the candidate map value changes;
4. provide one independently known false and one independently known true event flag, with the in-game reason for each.

Only a successful complete session writes a VERIFIED marker profile and SHA-256 sidecar under %TEMP%\ds1tr-qa-telemetry\marker-profiles. The profile binds executable hash, package-applied runtime digest, tool version, signature-definition hash, owner-confirmed map IDs, flag checks, transition, process identity, and validation timestamps. If a later trace encounters an invalid pointer/field value, it creates a separate .invalid marker and refuses further use of that profile. Any hash, runtime, tool, or signature-definition mismatch also prevents profile loading.

record rechecks package/runtime/profile/executable identity, then reads the process on a background sampler while the console accepts owner checkpoint annotations. Sampling continues while explicit y/n outcome prompts are answered. Checkpoint keypresses mark only the owner's report that the checkpoint was reached; they never imply that an exit was actionable, a transition succeeded, a landing was safe, or a route milestone was reached. W opens an explicit interruption annotation for death, warp/teleport, loading, or a sampling gap. No keypress is sent to DSR.

The four primary signals are map/area, XYZ, event flag 11410410, and observed map transitions. Map/position/flag observations remain CANDIDATE_UNVERIFIED until marker validation, then are labelled SESSION_VERIFIED for the exact process and profile. Unknown area IDs stay candidate-only. Flag 11410410 resolves to the candidate DSR flag word at byte offset 0x3c30, mask 0x20; the owner procedure checks both a known false and a known true flag before the field can be trusted. Barrier/object state and action-prompt memory are not implemented.

JSONL traces are created with CreateNew under %TEMP%, one record per line, and cannot overwrite an existing trace. Each record links the executable, runtime, package tree, run manifest, marker profile, process identity, field trust, actual sample time, and owner annotation time. A completed session writes a summary plus summary SHA-256. These local hashes detect accidental or post-write changes but are not signatures and cannot prevent a determined local user from fabricating data.

trace-import accepts only the pinned external case and exact package identity. Synthetic traces are returned as SYNTHETIC_TEST_PASS with every shadow edge UNKNOWN; unverified live input returns TELEMETRY_UNVERIFIED; neither can promote gameplay certainty. A verified trace can classify only this package's Ceaseless route after the ordered owner checkpoints, verified boss flag, actionable/crossed exit, map transition, valid landing, physical route milestones, and no recorded death/warp/loading/discontinuity. It does not modify the production validator or certify a global mechanism class.

## Launcher and owner next step

Run START-CEASELESS-QA.cmd. It verifies the exact package, package-applied runtime, and executable signature audit before entering guided marker validation. It never launches Steam or DSR. To record later, pass the verified marker-profile path as the launcher's first argument; the owner must already have the exact isolated DSR process open.

Do not start the Ceaseless gameplay case until the marker guide creates a profile and preflight/audit-build remain clean. The current implementation has not attached to a game process, observed runtime values, or produced a gameplay trace.

## Ceaseless route annotations

The fixed owner checkpoint order is:

CEASELESS_PRE_FIGHT -> CEASELESS_BOSS_ACTIVE -> CEASELESS_DEFEATED -> EXIT_APPROACH -> EXIT_PROMPT_PRESENT -> EXIT_CROSSED -> DESTINATION_LANDED -> MAIN_DEMON_RUINS_REACHED -> LOWER_LAVA_REACHED -> POST_RELOAD_EXIT_VERIFIED.

The exact expected route identity remains event flag 11410410, entrance o4961_0000, and the generated graph-side destination label Demon Ruins Lava. This tooling does not claim that destination is physically reachable.
