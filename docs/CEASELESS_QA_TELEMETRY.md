# Ceaseless single-case QA preparation (2026-10-29)

## State

**ISOLATED_QA_GAME_LAUNCH_READY.** The selected package is installed in a complete isolated QA runtime. The revised launcher verified same-user Steam, provisioned the QA-only AppID sidecar, directly started the exact isolated executable with the correct working directory, verified its process image path/hash, and a post-launch preflight returned `QA_RUNTIME_COMPLETE`. The game is open at its title window. Telemetry remains `UNVERIFIED` until the owner confirms the marker fields; no marker values or gameplay result were collected.

Production validator, V1–V5 semantics, generation profile, retry limit, public version, and safe baseline tag were not changed. The Steam installation was read-only; no Steam-library DSR executable was launched and no Steam DSR file was modified. Only the separately copied isolated QA executable was launched. Runtime copies and local manifests are outside Git.

## Repository and baseline checks

- V5 worktree: `C:\Users\Adam\VisualStudioProjects\ds1-triple-randomizer-v5-isolation`
- branch: `fix/compact-fog-v5-progression`
- HEAD: `94cd804dc838437f4c91969fb8ccf6d2be0cb7c6`
- `v5-safe-ceaseless-baseline` still points to `abc6a5286353618a1087b8363bf9438236b8fb10`.
- The source package was produced at `c7440e685221ec04b7856a317aa8037c7142ce8d`; this task did not move the baseline tag.

## Selected package and corrected report hash

External package:

`C:\Users\Adam\AppData\Local\Temp\ds1tr-ceaseless-certified-v2-lord-souls-off-05634a7e78b443e6910b317721e86b50\sweep-1-certified-lord-souls-off-1\package`

Original external descriptor: `C:\Users\Adam\AppData\Local\Temp\ds1tr-ceaseless-certified-v2-lord-souls-off-05634a7e78b443e6910b317721e86b50\sweep-1-certified-lord-souls-off-1\qa-telemetry\case.json`; descriptor SHA-256 `2b1ff04c7e8b8cef8b6fa61859128911a195dee0cf7ee31b5e9d3f480ddae9ab`. A separate `case-runtime.json` binds the same certified case to the approved isolated runtimes. Both descriptors are pinned in the helper; changes to package identity, runtime paths, or options fail closed.

| Identity | Verified value |
|---|---|
| Seed Code | `DS1TR1-06NWCQQ3-K2HDEE69-N52JB6B9-K6MJKEN0-EJM0PAGA-HDTZ7MYJ-EG6METX0-J590788Q-2PHNBM38-2MTF0N8G-5K6N1A8E-08042ASS-WW` |
| Master seed | `certified-lord-souls-off-1` |
| Combined seed | `8a3d20e4760831c1eccf67c75a417193` |
| Raw Fog seed / accepted attempt | `3695053466` / `4` |
| Profile | `ds1tr-gen-v5` |
| Package tree SHA-256 | `32f0c2afeff25c9341405989b80ba7dc6a72da1f3e90ecdf90cf37ca2b5f79a9` |
| `run-manifest.json` SHA-256 | `e81a504da9c251d9e784eb6708133aa3e98a34cd4df9757d4eee3e259cf7324d` |
| Contemporaneous V2 attestation SHA-256 | `6d4a2fb7eb0445b92c5b729b16b84ae23b6316bb56b53107a3d282a5870ae4a4` |
| Files / bytes | 74 / 78,082,827 |
| Per-file verification | 0 mismatches against the run manifest and V2 attestation; 0 reparse points |

The earlier short report `32f0c2afeff25c93478?` was truncated while formatting. The complete value above was recomputed from the current package inventory using the same canonical path/size/file-hash digest used by the attestation, then compared with the attestation. The manifest and attestation hashes were also recomputed. The V2 attestation explicitly says it has no external signature; it is local contemporaneous evidence, not a cryptographic signature from an independent authority. Descriptor, attestation, manifest, and package files are read in separate filesystem operations, so these checks are not an atomic snapshot and do not protect against an adversary mutating files concurrently. Run verification against quiescent local inputs.

The package records backend `0.46.4`, FogHost project `0.7.4`, FogHost host `0.7.3`, FogMod pin `6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f`, catalog fingerprint `73f3c842d51490d45a836a489105b37f57d702eaa2ffda084f42781e5aaef00f`, semantic fingerprint `6c73ee8777629c43759403d0c156cd928107cfcc8d21439ca5f1eff09686e5f0`, and world-data ID `ef625777d6aa5abf`.

Expected route identity: event flag `11410410`, entrance `o4961_0000`, graph-side destination `Demon Ruins Lava`. That is the generated graph destination label; actual landing and physical traversal remain unverified.

## Isolated runtime completion (2026-10-29)

The earlier 199-file QA data snapshot remains unchanged and is still not a playable runtime. Steam library metadata identified the installed DSR source (AppID 570940, build 10943698); its files were inventoried and read only. A separate runtime copy was created, with no symlink, junction, reparse-point, or hard-link dependency on Steam. The copy has 6,013 files totaling 7,366,427,395 bytes, includes the executable and required support/runtime directories, and its inventory agrees with the local Steam appmanifest `SizeOnDisk`. This establishes a complete local copy of the installed source, not official depot authentication or a guarantee that the installed source is vanilla.

Two independent runtime trees are retained outside the repository: a clean baseline and a separate package-applied test runtime. Local canonical manifests and their SHA-256 sidecars are stored beside those trees. The baseline digest was recorded before package application. The certified package was reverified before application; its declared game files were applied only to the separate test runtime. All 73 changed runtime paths are package-listed, the executable is unchanged, and the clean baseline remains recoverable. A full read-only inventory/hash verification passed for both trees.

The final helper preflight verified the pinned package (74 files, 78,082,827 bytes), both runtime manifests, executable identity, required runtime assets, and package-applied file identities. Result: `QA_RUNTIME_COMPLETE`, no blockers, `processRightsRequested: 0`, `gameLaunched: false`, and `telemetryStatus: UNVERIFIED`. The selected case descriptor is externally bound to the isolated runtime; its local paths and runtime hashes are not committed.

The helper fails closed for an unbound or non-approved runtime path, the known Steam path, executable path/hash mismatch, changed clean or package runtime, changed package identity, and reparse points. Package application writes only to the dedicated QA runtime. It never repairs or modifies the Steam source, launches a game, or opens a process.

## Telemetry implementation and boundaries

**Current implementation status (2026-10-29, QaTelemetry 0.3.0):** The bullets immediately below describe the earlier 0.2.0 preparation checkpoint and are retained as historical context. They are superseded by the 0.3.0 implementation record at the end of this document.

- Exact-build static audit, query/read-only process attachment, candidate map/XYZ/event-flag readers, guided owner marker validation, profile binding/revocation, append-only live JSONL recording, and exact-case shadow import are now implemented in `tools/qa-telemetry/`.
- The locked executable SHA-256 remains `a45aaa36dd2f6cc151670a639ea5547043cf38ea79ff4178b963c6ed71f98d7b`; package-applied runtime digest remains `5f182859f78f285a89a31a608ea28dc5b1cb069b082aa906637da11aea2873ef`. Exact-build signature anchors have unique static matches, but pointer-chain meaning and all live values remain candidate until the owner completes the marker guide.
- Static preflight requests no process rights. Live attach requests only query-limited information plus read-only memory (`0x1010`). No process-write/injection API, automatic launch, game input automation, or live process access occurred while building/testing this helper.
- The marker guide asks for three owner-confirmed maps, stationary/moving coordinates, one known map transition, and independently known false/true flags. It stores a profile under Temp only after all checks pass. A pointer/field validation failure invalidates that profile.
- The trace sampler continues during owner prompts. Checkpoint keys annotate only owner-reported arrival; outcome values require explicit yes/no answers. Death, warp, loading, and sampling gaps can be marked. The importer keeps synthetic and unverified data at UNKNOWN certainty and can only classify the exact pinned package after all route requirements pass.
- No runtime observations, marker profile, or gameplay trace have been produced. Owner marker validation is the next gate; Ceaseless gameplay must wait for that gate.

Standalone tool: `tools/qa-telemetry`, version `0.2.0`, .NET 8, BCL-only, no reference to production projects. It is not wired into the desktop app, backend, activation, or release packaging.

- Process rights requested by this build: **none (`0x0000`)**.
- The documented future allowlist is query-limited-information (`0x1000`) plus read-only memory (`0x0010`) only. No process-open/read-memory call exists in this build.
- No input automation, memory mutation, injection, or game-file writes are implemented.
- Map identity, player XYZ, event flag `11410410`, map transitions, barrier/object state, interaction state, and all four required marker validations are explicitly `UNVERIFIED`. No offset source, pointer chain, type, or validation procedure was guessed.
- The live trace writer is disabled. Its synthetic-only writer uses `CreateNew`, appends compact one-record-per-line JSONL, and creates a session summary plus separate summary SHA-256 file. These hashes detect post-write changes; they do not make local data tamper-proof.
- The analysis importer verifies trace/session/package hashes, sequence, session ID, schema, provenance, and declared observation-status fields. It does not independently validate known-marker procedures. A synthetic trace returns `SYNTHETIC_TEST_PASS` only; any live-provenance input remains `TELEMETRY_UNVERIFIED`. This build has no `GAMEPLAY_CONFIRMED` promotion path.
- The importer associates a future validated route trace with these analysis selectors: `boss-completion:11410410`, `post-boss-approach:o4961_0000`, `post-boss-crossing:o4961_0000`, `arrival:main-demon-ruins`, and `route:main-to-lower-lava`. It emits `UNKNOWN` for each unless a future independently validated profile is pinned and every route checkpoint passes. These selectors are analysis-side classifications, not a claim that current opaque per-package witness edge IDs have already been matched. The importer does not modify the physical-witness graph or production validator.

## Demon Ruins collision and navigation asset audit

The package changes `map/MapStudio/m14_01_00_00.msb` and `event/m14_01_00_00.emevd.dcx`. Its corresponding `MapStudioNew` MSB, H/L collision archives, MCP, MCG, and NVM bundle are byte-identical to the clean baseline.

The clean runtime contains `map/MapStudio/m14_01_00_00.msb`, `map/m14_01_00_00.mcp`, `map/m14_01_00_00.mcg`, `map/h14_01_00_00.hkxbhd` + `.hkxbdt`, `map/l14_01_00_00.hkxbhd` + `.hkxbdt`, and `map/m14_01_00_00.nvmbnd.dcx`. The MSB parses as MSB1 (119 collision models and 119 collision parts); MCP has 49 rooms; MCG has 51 nodes and 65 edges. H/L collision archives are BXF3, each with 119 entries. The NVM bundle is DCX containing BND3 with 52 `.nvm` entries.

The pinned `JuicerMV.SoulsFormats` dependency decodes these containers and the MSB/MCP/MCG structures, but the available dependency does not decode the HKX collision mesh triangles. No continuous player traversability proof can be made from approximate coordinates or navmesh connectivity alone. The Ceaseless arena-to-`o4961_0000`-to-main-Demon-Ruins-to-lower-lava physical route therefore remains `UNKNOWN` pending trustworthy geometry/engine evidence or owner gameplay.

## Marker validation required before telemetry can be trusted

The locked executable needs an independently derived profile and a complete isolated runtime first. Then validate, in this order, before gameplay evidence is trusted:

1. a deterministic known map such as Firelink;
2. player coordinates change consistently during ordinary movement;
3. one known vanilla map transition changes map identity correctly;
4. a reproducible known event-flag state reads correctly, including the `11410410` set state after the boss;
5. if barrier/action markers are later added, validate them independently against an observable known state.

The profile source, full pointer chain, value type, bounds, marker procedure, and exact executable SHA must be reviewed and recorded before setting any field or profile to `VERIFIED`. The telemetry reader must not certify its own offsets by comparing only against values it read through those same offsets.

## One gameplay case and outcome rules

After the runtime/profile gate is genuinely cleared, collect one case with these checkpoints:

`CEASELESS_PRE_FIGHT` → `CEASELESS_BOSS_ACTIVE` → `CEASELESS_DEFEATED` → `EXIT_APPROACH` → `EXIT_PROMPT_PRESENT` → `EXIT_CROSSED` → `DESTINATION_LANDED` → `MAIN_DEMON_RUINS_REACHED` → `LOWER_LAVA_REACHED` → `POST_RELOAD_EXIT_VERIFIED`.

The intended gameplay sequence is to use the exact locked package, enter and defeat Ceaseless, observe flag `11410410`, approach and interact with the expected post-boss exit, cross it, verify valid landing and reach both the main Demon Ruins and lower lava route without an artificial warp, then reload and repeat the exit check. Pair observations with owner video/screenshots and record death, teleport, loading, or sampling gaps so those cannot be mistaken for traversal.

Pass requires every checkpoint and marker to be verified, flag `11410410` observed set, the exit actionable, a successful transition and valid landing, the expected physical route reached, and post-reload progression retained.

Keep these failures distinct: `NO_EXIT_PROMPT`, `EXIT_NOT_ACTIONABLE`, `TRANSITION_FAILED`, `WRONG_DESTINATION`, `UNDER_MAP_LANDING`, `LANDING_ISOLATED`, `MAIN_ROUTE_UNREACHABLE`, `LOWER_LAVA_UNREACHABLE`, `POST_RELOAD_FAILURE`, `TELEMETRY_UNVERIFIED`, and `PACKAGE_IDENTITY_MISMATCH`.

## Upstream comparison preparation

The case descriptor preserves raw Fog seed `3695053466` and the accepted attempt `4`. The corresponding selected V5 settings are: traversable fogs, boss fogs, area warps, scaling, and DLC enabled; Major/Minor PvP, Lordvessel gates, random start, disconnected fogs, glitched logic, and Lord Souls requirement disabled; pacifist mode disabled. These are ready to map to the pinned FogMod settings for a later single-seed comparison. No upstream FogMod run was performed, and no topology parity is claimed.

## Tests and remaining action (historical 0.2.0 checkpoint)

Release build passed with 0 warnings and 0 errors; the focused helper suite passed 30/30. The actual certified package passed `verify-case`, and the final runtime preflight passed as `QA_RUNTIME_COMPLETE`. The updated launcher was smoke-tested against that preflight and remains preflight-only. No FogReconstruction or production suite was needed because production code was not changed.

The runtime-preparation blocker is resolved. Remaining work before telemetry-backed gameplay is to derive and independently validate markers against this exact executable, then implement/review a read-only process reader and ordered live-trace path. No marker offset or gameplay result has been fabricated. Do not treat `QA_RUNTIME_COMPLETE` as a gameplay pass or use the telemetry helper to claim a verified route.

## Prior 0.3.0 implementation result (historical)

At this checkpoint the standalone tool was version 0.3.0. Release build succeeded with 0 warnings/errors. The offline self-test console passed 48/48 after the exact-build process/profile/trace changes; it includes source-level absence checks for every requested process-write and injection API. No DSR process was launched or attached during that checkpoint.

The exact executable audit scans both `.text` sections and requires one match per candidate signature plus a `.data`/`.bss` target. The flag reader reports candidate event flag 11410410 at candidate byte offset `0x3c30`, mask `0x20`. Player XYZ and current map use candidate pointer chains; the map candidate is an OnlineAreaID, not yet a canonical map identity. These signals become usable only after the owner guide independently confirms map locations, movement, a known transition, and known false/true flag states.

At that checkpoint the launcher did not launch Steam or DSR. That behavior was superseded by the isolated launch implementation below. Production validator, generation behavior, retry limits, V1-V5 semantics, baseline tag, and game files were not changed.

## Isolated QA Steam initialization and launch (2026-10-29; QaTelemetry 0.4.0)

### Finding and correction

The selected isolated runtime initially had `steam_api64.dll` but no root-level `steam_appid.txt`. The earlier launcher also left executable selection and working directory to a manual owner action. Before the corrected run, the existing isolated DSR process showed a `Fatal error` window; its image path was verified as the isolated QA path and it was closed normally. No process under the normal Steam DSR path was launched or closed.

The corrected launch gate requires the Steam client at `C:\Program Files (x86)\Steam\steam.exe` and verifies its process token SID matches the launcher user. It verifies package/runtime attestations and required DLLs, starts `DarkSoulsRemastered.exe` directly with `WorkingDirectory` set to the package-applied QA runtime, then checks the new PID's image path and executable hash using `PROCESS_QUERY_LIMITED_INFORMATION` only. A live Steam-library DSR image, another unknown DSR image, or any already-running DSR process blocks launch. No `steam://run/570940`, game input, process memory write, or process injection is used.

### Launch sidecar and immutable identities

The launch sidecar was created only at:

`C:\Users\Adam\ds1tr-qa\runtime\DARK SOULS REMASTERED - CEASELESS QA\steam_appid.txt`

It contains exactly six ASCII bytes `570940`, no BOM or line ending. SHA-256: `31e26c0b0251cf381c0afbceba381c68f9a5aebb9637ef93a4cb41ee9f17ea91` (bytes `35 37 30 39 34 30`). The file is excluded only from the root inventory for the locked package-applied runtime; its exact path, content, hash, regular-file status, and single-link identity are verified separately. A wrong pre-existing sidecar is preserved and rejected. The existing runtime canonical digest remains `5f182859f78f285a89a31a608ea28dc5b1cb069b082aa906637da11aea2873ef` (6,013 files; 7,371,056,989 bytes), and the selected package tree remains `32f0c2afeff25c9341405989b80ba7dc6a72da1f3e90ecdf90cf37ca2b5f79a9`.

### Verification result

- Steam client was running from the locked Steam client path. The launch command verified the Steam process token SID matched the current owner SID before the game was started.
- Launch result: `ISOLATED_QA_GAME_LAUNCH_READY`, PID `62764`, working directory `C:\Users\Adam\ds1tr-qa\runtime\DARK SOULS REMASTERED - CEASELESS QA`, process image path equal to the isolated executable, and executable SHA-256 `a45aaa36dd2f6cc151670a639ea5547043cf38ea79ff4178b963c6ed71f98d7b`. The visible title was `DARK SOULS™: REMASTERED` and the process was responding.
- Post-sidecar post-launch preflight returned `QA_RUNTIME_COMPLETE`, zero blockers, required runtime assets present, and no process rights requested. Independent `verify-case` returned `PACKAGE_IDENTITY_VERIFIED` with the expected package tree digest and run-manifest hash.
- QaTelemetry 0.4.0 Release build: 0 warnings, 0 errors. Focused self-tests: 55 passed, 0 failed. Tests cover absent Steam, same-user enforcement, wrong working directory, missing/wrong/correct AppID sidecar, live Steam DSR rejection, isolated executable selection, sidecar hash, and invariant runtime/package identity. Source audit continues to reject process-writing/injection APIs.
- No world was loaded and no gameplay or marker values were collected. Marker profile remains `UNVERIFIED`; the owner may resume marker validation in the already running isolated QA game.
