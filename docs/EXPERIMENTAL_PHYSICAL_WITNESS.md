# Experimental Physical Witness

Status: development-only shadow audit that certifies selected staged transition edges from exact bytes. It does not establish complete physical routes or production validity.

## Safe baseline

- Branch: fix/compact-fog-v5-progression
- Baseline commit: abc6a5286353618a1087b8363bf9438236b8fb10
- Local annotated tag: v5-safe-ceaseless-baseline
- Tag object: 556632c37f8fd33d804819b6f1bbe4939a076b5b
- Catalog fingerprint: 73f3c842d51490d45a836a489105b37f57d702eaa2ffda084f42781e5aaef00f
- Semantic fingerprint: 6c73ee8777629c43759403d0c156cd928107cfcc8d21439ca5f1eff09686e5f0
- World data ID: ef625777d6aa5abf
- FogHost project version: 0.7.4; HostVersion: 0.7.3; protocol: 3
- Backend version: 0.46.4
- Development-only FogReconstruction version: 0.3.0

The tag points at the exact baseline commit and was not moved. The experimental code lives under tools/fog-reconstruction and has no production dependency.

## Model and certainty rules

Each directed edge carries source provenance, locator, and SHA-256. The only certainty values are PROVEN_ACTIONABLE, PROVEN_NON_ACTIONABLE, and UNKNOWN.

Positive search traverses only PROVEN_ACTIONABLE edges whose typed event-flag and item conditions hold. A reachable UNKNOWN edge prevents an impossibility claim. A negative result is permitted only after a complete physical inventory with no reachable UNKNOWN escape and no exhausted search budget. Non-actionability is state-scoped unless direct telemetry proves a global refusal.

Ordinary Event5700 Mode 0 certification requires staged source object and ActionRegion evidence, a unique matching event-0 initializer, exact dispatcher bytes and body contract, destination player/map evidence, and a matching default landing. Source and destination maps are resolved using project-owned Area-to-Map policy; dataset map claims are only a fail-closed cross-check. PvP certification additionally requires an exact project-owned PvP gate identity and side; conditional sides remain UNKNOWN and Mode 1 cleanup calls are excluded. CustomWarp certification is limited to two project-owned Firelink sides and their exact landing anchors.

Seath Mode 2 p0=5308 uses a separate proof chain: the staged crystal object, the map event-0 initializer, event 11700005 producing flag 5308, the common dispatcher call, destination player/map, and the runtime prison landing region must all agree. This specific chain is not generalized to other Mode 2 calls. Boss-boundary calls remain UNKNOWN until encounter entry, completion, exit-side actionability, and lifecycle semantics are established. Area.To and ConnectCollision by themselves are rejected. Witness replay recomputes the route fingerprint and compares every edge, state transition, mechanism, and evidence record to the supplied graph.

The Ceaseless query starts in the arena after flag 11410410 is set, requires the main Demon Ruins physical-side waypoint, then requires the lower Demon Ruins/lava goal. The model does not create a Ceaseless-to-lava shortcut.

## Certified 331-case shadow run

Inputs were the existing canonical STRICT dataset and its existing staging trees; no worlds were regenerated and the production validator was not rerun.

- Dataset: C:\Users\Adam\AppData\Local\Temp\ds1tr-ceaseless-physical-witness-certified-20261026\canonical-strict-331.json
- Dataset SHA-256: c59ac51177ceaed2c9ee8668f535ff0f60dcba6273bcbf743405de2fabbc4340
- Staging root: C:\Users\Adam\AppData\Local\Temp\ds1tr-ceaseless-physical-witness-certified-20261026\sweep-final-20261026\staging
- Cases: 331, all selected from the existing STRICT / safe-reject set
- Files per current staging tree: 59
- Shared relative-path inventory SHA-256: 06591a3eca2f68e9043ea255f61e78a481072a682105bd2ba06caf0d624b053f

The dataset does not contain historical per-case package manifests or expected package hashes. The tool hashes current staging trees for repeatability and records each hash as an unanchored observation; it does not authenticate current bytes against the dataset's historical hash claims. The dataset's generation/hash/decode booleans are treated as source claims.

### Results

The audit re-read the same 331 existing STRICT cases and staging trees. No worlds were regenerated. There were zero per-case input errors.

| Measure | Result |
|---|---:|
| PROVEN_ACTIONABLE edges | 483 |
| PROVEN_NON_ACTIONABLE edges | 0 |
| UNKNOWN edges | 4,639 |
| Safe REJECT / PHYSICALLY_SOLVABLE_PROVEN | 0 |
| Safe REJECT / PHYSICALLY_UNSOLVABLE_PROVEN | 0 |
| Safe REJECT / UNRESOLVED_MODEL_GAP | 331 |
| Cases with a reachable UNKNOWN escape | 331 |
| Positive witness classes | 0 |
| Manual QA candidates | 0 |

The 331 SAFE REJECT dispositions are inherited from the selected dataset; the production validator was not rerun. `UNRESOLVED_MODEL_GAP` is not a finding that those worlds are physically impossible. All 331 remain unresolved because every explored world has a reachable UNKNOWN escape. The certification is about current observed staging bytes only: the dataset has no historical per-package hashes, so those bytes are not authenticated against the earlier generation run.

Mechanism coverage (candidate evidence rows can overlap across mechanisms):

| Mechanism | Status | Candidates | Certified | Unknown | Worlds | Worlds with unknown |
|---|---|---:|---:|---:|---:|---:|
| Ordinary Event5700 Mode 0 | STAGED_BYTES_CERTIFIED | 178 | 132 | 46 | 118 | 42 |
| PvP randomized Mode 0 | STAGED_BYTES_CERTIFIED | 130 | 105 | 25 | 65 | 25 |
| Project CustomWarp | STAGED_BYTES_CERTIFIED | 7 | 6 | 1 | 7 | 1 |
| Seath Mode 2 p0=5308 | STAGED_BYTES_CERTIFIED | 331 | 240 | 91 | 331 | 91 |
| Boss-boundary Mode 0 | UNKNOWN | 787 | 0 | 787 | 241 | 241 |
| Mode-2 p0=0 | UNKNOWN | 0 | 0 | 0 | 0 | 0 |
| Boss completion state 11410410 | TRACE_ROWS_ONLY | 331 | 0 | 0 | 331 | 0 |
| Ceaseless post-boss approach/route | CANDIDATES_ONLY | 1,703 | 0 | 1,703 | 331 | 331 |

Ordinary and PvP UNKNOWN rows mostly lack a unique staged source object/ActionRegion, disagree with project-owned source-map policy, or have a destination-map/landing mismatch. The Seath unknowns are 86 parameter-to-player/map binding mismatches and 5 prison-landing mismatches. Boss-boundary candidates include 738 calls whose entry/completion/exit semantics remain unverified, 31 missing or ambiguous source objects, and 11 source-map-policy mismatches. No boss transition is certified.

The 1,703 Ceaseless route candidates break down into 1,459 trigger-approach/identity edges, 122 arrival-to-main-side edges, and 122 main-to-lower-lava edges. Every one remains UNKNOWN because fixed physical walking and approach evidence is absent. Event 11410410 is only a completion-state trace; its presence does not prove an exit or route. Fixed walkable links, doors, elevators, generic Mode-2 p0=0 warps, and collision/walkability still have no certified edges; this audit does not establish that they are irrelevant.

Final reports are outside the repository:

- JSON: `C:\Users\Adam\AppData\Local\Temp\ds1tr-v5-short-shift-physical-witness-report-20261028-final-v5.json` — SHA-256 `68421EA830AC0EAC3ABE19DE1CFECD37BDC7D040AE64A0B990E9257685F2DC53`
- Markdown: `C:\Users\Adam\AppData\Local\Temp\ds1tr-v5-short-shift-physical-witness-report-20261028-final-v5.md` — SHA-256 `EDA76BB225BAB8BABBEC5DE7C5C5A19556CA2B4E7F7B6D0AE880803F4E41E3FA`

A separate complete repeat against the same inputs produced byte-identical JSON and Markdown, with the same hashes. The repeat reports are under `C:\Users\Adam\AppData\Local\Temp\ds1tr-v5-short-shift-physical-witness-report-20261028-repeat.*`. JSON retains complete per-case step and evidence arrays for future positive witnesses.

## Read-only QA telemetry design

No telemetry helper was implemented or run in this task. No game process was opened, no QA DSR files were read, and no Steam installation was accessed.

A future helper belongs in a separate development-only tools/qa-telemetry project, with no application dependency. It should accept only an explicitly selected QA executable path and a recorded executable/data identity, verify hashes before attaching, and refuse any process whose image path or hashes differ. The Windows process handle should request query/read rights only; the implementation must not request VM_WRITE or VM_OPERATION, patch memory, or send input. The helper should emit append-only JSONL to a caller-selected Temp directory.

Potential observations are current map, player XYZ, map transitions, boss completion flags, and explicitly selected relevant event flags. Memory offsets are currently unverified and must not be guessed. They must be derived reproducibly for the exact binary and validated against known markers: Firelink map identity and coordinates, a known boss flag, and a known map transition. The helper must report UNVERIFIED and refuse to emit gameplay evidence until every marker passes. A changed executable or data hash must fail closed. Manual play is the input method; no bot or automation is in scope.

## Verification and invariants

- FogReconstruction Release build with .NET 8: passed, 0 errors; 14 existing SoulsFormats obsolete-API warnings.
- Development tool self-test: passed, including 7 experimental physical-witness groups, 7 generated-transition certification groups, two exact Firelink CustomWarp anchors with four rejection cases, 4 report path-safety cases, and existing tool self-tests.
- 331-case read-only audit: 0 input errors, 483 actionable / 0 non-actionable / 4,639 unknown edges, and 331/331 worlds unresolved; deterministic repeat JSON/Markdown hashes match.
- Report-path safety rejects output aliases to the dataset, outputs under staging, pre-existing output files, and colliding JSON/Markdown paths. Input traversal rejects reparse points and checks all ancestors.
- V1-V5 generation semantics, production validator, retry limits, package versions, and app/backend/FogHost versions were not changed.
- The verified QA DSR and live Steam DSR were not accessed or modified.
- No positive whole-world witness classes exist, so there are no manual QA candidates yet, despite the newly certified individual transition edges.

## Focused shift: boss-boundary lifecycle and Ceaseless exit (2026-10-28)

This was a read-only source/staging audit. It did not modify production generation, the safe Ceaseless validator, retry limits, profile semantics, or live Steam DSR. The designated QA DSR source was `C:\Users\Adam\ds1tr-qa\game-inst\DARK SOULS REMASTERED`; its project reference subset had previously verified 19/19 files. Event and MSB hashes below are observations from that source, not hashes anchored by the project reference manifest.

### Boss-boundary classification

The existing 331-case shadow dataset contains 787 UNKNOWN boss-boundary Mode-0 rows in 241 worlds. Resolving against the project's 21 `BossEntrancePoliciesV1` entries and the existing dispatcher classifier yielded:

| Directional endpoint role | Rows | Worlds | Dispatcher classification |
|---|---:|---:|---|
| Boss-policy/arena endpoint appears only as source | 303 | 223 | 303 Mode 0 |
| Policy exterior endpoint appears only as source | 371 | 210 | 343 Mode 0; 28 without a dispatcher |
| Boss-policy/arena endpoint appears only as target | 86 | 73 | 81 Mode 0; 5 without a dispatcher |
| Arena-to-arena | 27 | 25 | 27 Mode 0 |
| **Total** | **787** | **241 unique** | **754 Mode 0; 33 without a dispatcher** |

These are endpoint roles, not evidence that a side is an alive-state entrance, defeated-state exit, or re-entry. No subtype was promoted. The dataset contains no per-candidate lifecycle metadata except the Ceaseless 11410410 trace rows. The 21-policy resolver also does not resolve Pinwheel's null `ObjectPartName`: 15 of 24 Pinwheel-tagged rows (14 FR, 1 BR) fail at both ends; nine can match through another endpoint. Those rows remain a separate coverage gap. Three policy-matched rows dispatched as already-certified CustomWarp are not included in the 787 boss-boundary UNKNOWN rows.

Future certification must separately prove pre-fight entry, post-defeat exit, re-entry, and arena-to-arena cases. Required evidence includes exact gate object/collision and action region; source-side physical approach in the relevant state; event-0 initialization and dispatcher bindings; destination player/map/landing; boss-start and completion flags; entity death, AI/backread/HP-bar behavior; and gate/collision state after defeat and reload. Without the complete applicable chain, retain UNKNOWN.

### Ceaseless lifecycle and exit evidence

Observed pristine QA EMEVD `event/m14_01_00_00.emevd.dcx` SHA-256: `dc0e348fc53082dd8dfbefa6573c774d0157c2880afaa640daa4e59a819b1deb`. This is an observed source hash only.

- Event 11415371 (boss start) retains the same instruction and event-parameter content under the preservation scan in all **331/331** current staging packages. Its action-line-segment check uses region 1412698; its following behavior rotates/animates the player and does not warp. A randomized spawn or logical transition alone therefore does not establish that the player can activate this region.
- Event 11410410 waits for boss entity 1410400 to be dead, sets completion flag 11410410, and calls `HandleBossDefeat`. Vanilla then deactivates barrier objects 1411410 and 1411412 and deletes their associated map SFX. The objects are `o4962_0000` / collision `m1030B1` and `o4963_0000` / collision `h0018B1`; both are light-wall barriers.
- Event 11415342 is a restartable boss-state controller initialized twice from event 0. Its vanilla defeated-state branch also deactivates those two barriers while handling boss AI/backread/health-bar state.
- Vanilla event 0 has a defeated-state cleanup block for the same barrier pair. In generated staging, both barriers and collision names remain present, while their deactivation calls are absent from event 0, 11410410, and 11415342.
- Event 11410400, the elevator controller, is initialized and instruction-equivalent to vanilla in **331/331** staging packages. It checks completion flag 11410410 and the four elevator regions. Event 11410402 is also identical in 331/331 and displays the English message “Contraption does not move” when that flag is off. Its direct body is a dialog/condition check, not a movement or warp event.
- Event 11410090 has two `IssueShortWarpRequest` instructions and two action-line-segment checks, but an exhaustive scan found no `InitializeEvent` call targeting it in either pristine QA EMEVD (32 archives, 1,401 events, 3,153 initializer sites) or the 331 staging corpus (18 archives, 1,281 events, 3,051 sites). It is instruction-identical to vanilla in 331/331 staging packages. This supports `NOT_ROUTE_RELEVANT_PROVEN IN THIS PINNED CORPUS`, not a universal dead-code claim.

The 331-package preservation scan returned no mismatches: 11415371, 11410400, 11410402, and 11410090 are all instruction-equivalent; the elevator/completion/controller initializers remain; both barriers remain in the MSB; and all three defeated-state barrier cleanup blocks are missing in all 331. At that checkpoint the omission appeared to be an unintended generated-package regression. The fresh differential documented below shows that the staging corpus contained Fog-transformed outputs: combined-only workspaces retain the cleanup, while successful Fog outputs remove it in the same way as pinned FogMod. The earlier count remains a correct observation, but the omission alone does not establish a generator defect. Physical connectivity and post-defeat actionability remain unproven.

EMEDF instruction names used in interpreting the event bodies: `IfActionButtonLineSegment` 3[18], `IfEventFlag` 3[0], `IfMultiplayerState` 3[6], `IfCharacterDeadalive` 4[0], `InitializeEvent` 2000[0], `SetNetworkSyncState` 2000[2], `HandleBossDefeat` 2003[12], `RotateCharacter` 2004[14], `DeactivateObject` 2005[3], `DeleteMapSFX` 2006[1], and `IssueShortWarpRequest` 2004[41]. These IDs are documented in the DS1 EMEDF reference.

### Same-input 331-case rerun and interpretation

The same canonical dataset and existing staging trees were reread; no world was regenerated. Fresh reports are `C:\Users\Adam\AppData\Local\Temp\ds1tr-ceaseless-focused-shift-20261028\audit-331.json` (SHA-256 `68421ea830ac0eac3abe19de1cfecd37bdc7d040ae64a0b990e9257685f2dc53`) and `C:\Users\Adam\AppData\Local\Temp\ds1tr-ceaseless-focused-shift-20261028\audit-331.md` (SHA-256 `eda76bb225bab8babbec5de7c5c5a19556ca2b4e7f7b6d0ae880803f4e41e3fa`). JSON matched the prior canonical report byte-for-byte. Results remain 483 PROVEN_ACTIONABLE, 0 PROVEN_NON_ACTIONABLE, 4,639 UNKNOWN; all 331 cases remain UNRESOLVED with reachable UNKNOWN escapes. There are no positive whole-world witnesses.

Even if the post-boss barrier-release issue were hypothetically fixed, all 331 cases remain unresolved because UNKNOWN physical approach/trigger identity edges occur in all 331 worlds (1,459 rows), in addition to arrival-to-main-side and main-to-lower-lava route gaps. Hypothetically accepting every boss-boundary row would also leave 331/331 unresolved: a boss crossing classification does not prove approach to the gate or fixed Demon Ruins walkability. These are impact calculations, not certifications.

### Staging authenticity recommendation

At generation time, preserve a canonical per-case attestation outside the mutable staging tree: Seed Code and profile; attempt and derived seeds; exact app/backend/engine and input-data identities; sorted relative-path inventory with size and SHA-256 for every package file; and a digest over a canonical serialization. Anchor that attestation in an immutable, access-controlled CI artifact or sign it with a key unavailable to the staging writer. The auditor should verify the attestation before parsing or classifying staging bytes. Keep the canonical dataset, attestation, tool build identity, and audit report together. A hash computed during a later audit is useful for repeatability only; without a contemporaneous trusted expected digest it cannot authenticate historical package content.

No production code, validator, retry limit, generation profile, package version, or DECISIONS entry changed. No game process or live Steam installation was accessed.

## Fresh Ceaseless lifecycle differential (2026-10-29; current V5 code)

This follow-up resolves the cleanup-block ambiguity with fresh outputs and contemporaneous local attestations. It does not change production generation, validation, retries, profile semantics, or the physical-witness certainty rules.

### Source and attestation identity

- Worktree: `C:\Users\Adam\VisualStudioProjects\ds1-triple-randomizer-v5-isolation`
- Branch and source HEAD: `fix/compact-fog-v5-progression`, `c7440e685221ec04b7856a317aa8037c7142ce8d`
- Safe tag: `v5-safe-ceaseless-baseline` -> `abc6a5286353618a1087b8363bf9438236b8fb10` (unchanged)
- Profile: `ds1tr-gen-v5`; catalog fingerprint `73f3c842d51490d45a836a489105b37f57d702eaa2ffda084f42781e5aaef00f`; semantic fingerprint `6c73ee8777629c43759403d0c156cd928107cfcc8d21439ca5f1eff09686e5f0`; worldDataId `ef625777d6aa5abf`
- FogHost project 0.7.4 / HostVersion 0.7.3 / protocol 3; backend 0.46.4; FogReconstruction 0.3.2
- Read-only QA DSR: `C:\Users\Adam\ds1tr-qa\game-inst\DARK SOULS REMASTERED`
- QA input tree: 199 files, inventory SHA-256 `2d056b2adfda65372f3115c98afd6f9366961f73d2392694969ab025151931a0`
- Compact Fog input: 59 files, inventory SHA-256 `d6fd8aeca8c07c1f1ddb3cd77d8a5543a0b8ffd2c31bb081aa371331313627ba`

Each attestation is classified `CONTEMPORANEOUS_HASH_ATTESTATION`; it is not an externally trusted signature. The six audit files are outside the repository under `C:\Users\Adam\AppData\Local\Temp\ds1tr-ceaseless-certified-v2-*`. Their SHA-256 values are:

| Sweep | Cases | Packages | Exhausted | `audit.json` SHA-256 |
|---|---:|---:|---:|---|
| baseline | 5 | 0 | 5 | `b61e9ea150a66adcd1a1afc00de7b05e8380830fff0c0a734aaeb4ff8c81a3b0` |
| both PvP | 5 | 2 | 3 | `f58b6f32499ebe9a5db4e7476a359348eac9da914ec3de4a7dc0e7eecd974a35` |
| disconnected | 5 | 1 | 4 | `a51187591c4b33ec84c760c7448084fb7aa04fa89df78a8ce972328e63ed34fa` |
| Lord Souls off | 5 | 2 | 3 | `3f778e138118e53b62dd689f77ec9866df118c74d48c13631a16ac111e98d35e` |
| Major PvP | 5 | 0 | 5 | `c9187bb0b51a088b92ee4f5d291b17756a36e8bc200e211ab4dc129249397658` |
| Minor PvP | 5 | 2 | 3 | `6ca4969119ad36cf12063c710e87ea47842e6765955e8e9f9daae0b0ed8a9dc6` |
| **Total** | **30** | **7** | **23** | |

The 23 exhausted cases each had five rejected Fog attempts. There were 17 further rejected attempts before the seven accepted attempts, for 139 attempts total: 132 rejected and 7 accepted. An independent log classification found that all 132 rejected attempts failed specifically at `solve-graph` with the event-11410410 post-boss fixed-route error; zero rejected attempts had a different error. All 139 attempt logs contain the V5 post-boss route summary. No Fog writer ran on the 23 exhausted cases; each successful package has one write log.

All seven packages have two instruction merges (`m15_00_00_00.emevd.dcx:11500001` and `m16_00_00_00.emevd.dcx:11600400`) and no merge conflicts. Fog/combined event mismatch arrays are empty. Combined SKIP verification, Fog SKIP verification, and Fog post-write invariants pass. An independent rehash verified 518 retained package files, seven package tree digests, and seven `run-manifest.json` hashes; all matched the contemporaneous attestations. No package `.bak` or unexpected output was reported.

### Lifecycle differential and pinned upstream comparison

The fresh output separates two states that the historical 331 staging did not distinguish:

1. The 23 final rejected cases retain the combined-only workspace. The three vanilla Ceaseless defeated-state cleanup blocks remain in event 0, event 11410410, and event 11415342, matching the pristine QA source's logical cleanup operations.
2. In each of the seven accepted Fog outputs, the cleanup blocks are removed by the known M14 DLC reconstruction. The corresponding cleanup removal agrees with pinned FogMod's transformed event records. The barrier objects remain in the MSB, while Fog's randomized gate dispatch is added. This is an observed engine transformation, not evidence that the final gate is physically reachable or usable after boss defeat.

The pinned FogMod checkout used as read-only oracle is `C:\Users\Adam\AppData\Local\Temp\FogMod-pvp-audit-6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f`. With a command-scoped `safe.directory` exception, `git rev-parse HEAD` returned exactly `6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f`; `git status --short` was empty. The evidence files were `dist/DS1R/event/m14_01_00_00.emevd.dcx` (SHA-256 `26fa087386ae61522cd5262bcd5e44a2cbc4c914e7ac5b915d84b70b1f7848f7`) and `dist/fog.txt` (SHA-256 `15c665bb6c577219a2b36ffd9e1a7eb2fa2b789c9862fce5340b6a3116d24ec2`). The corresponding FogHost reconstruction code is in `FogModifiedEventBuilder.cs` (`ReconstructKnownM14DlcEvents`, `ReconstructM14DlcBossEvent11415342`, and `ReconstructM14DlcEvent0`); event 11410410 uses `RemoveInstructionSequence(11410410, 12, ...)`. These records support that accepted Fog outputs perform the same cleanup-block removal as pinned FogMod. No upstream `dist` or annotation data was copied into the repository, and upstream remains an oracle rather than project policy.

Event 11415371, 11410400, and 11410402 remain source-equivalent in the fresh staging scan; the `event0` initializer to 11410410 remains. The successful-run route log says `exit 'o4961_0000' from 'demonruins_ceaseless'`, with logical fixed-route destination `demonruins_lava`. This log records project route policy, not a traversable collision path. No defect correction is supported by the lifecycle evidence alone.

### Current physical-witness result

The shadow evaluator ran against all seven fresh accepted packages; the 23 no-package rejects were not treated as generated worlds. The result is **0 PROVEN_ACTIONABLE, 0 PROVEN_NON_ACTIONABLE, 28 UNKNOWN edges, 0 solvable, 0 unsolvable, and 7 UNRESOLVED_MODEL_GAP**. Each package has four unresolved stages: (1) physical approach to `o4961_0000`, (2) Mode-0 actionability after completion flag 11410410, (3) generated landing to the main Demon Ruins side, and (4) physical route from that side to lower lava. No witness class was promoted; the prior 331-case result remains 483 actionable, 0 non-actionable, 4,639 unknown, and 331 unresolved.

The minimum evidence gap for a positive Ceaseless witness is either authenticated collision geometry sufficient to establish a continuous, state-correct route and gate approach/landing for one exact generated package, or validated read-only runtime observation of those same states. The verified QA DSR contains no collision/NVM/HKX source assets; no telemetry code or process-memory access was used. The old 331 staging inventory has no historical expected package hashes, so its bytes still cannot be authenticated to the original generation moment.

### Manual QA candidate

The accepted Lord-Souls-off case is suitable for an owner gameplay check, but not yet certified as physically traversable:

- Seed Code: `DS1TR1-06NWCQQ3-K2HDEE69-N52JB6B9-K6MJKEN0-EJM0PAGA-HDTZ7MYJ-EG6METX0-J590788Q-2PHNBM38-2MTF0N8G-5K6N1A8E-08042ASS-WW`
- Master seed: `certified-lord-souls-off-1`
- Combined seed: `8a3d20e4760831c1eccf67c75a417193`
- Fog attempt: 4; raw Fog seed: `3695053466`
- Package tree SHA-256: `32f0c2afeff25c9341405989b80ba7dc6a72da1f3e90ecdf90cf37ca2b5f79a9`
- Seed Code decode/round-trip: passed; recomputed combined/Fog seeds match the attestation.

Suggested evidence checkpoints: start the fresh world; reach the Ceaseless arena and trigger; defeat Ceaseless; verify the post-defeat fog prompt and crossing; record destination map/player position; then verify a physically traversable route to the lower Demon Ruins/lava route, quit/reload, and repeat the relevant post-defeat checks. A manual PASS would be gameplay evidence for this exact package, not a generalized geometry rule.

### Verification and non-changes

- FogHost tests: 124 passed, 0 failed, 0 skipped.
- Backend tests: 621 total, 618 passed, 0 failed, 3 skipped.
- Contracts, backend, and desktop no-emit typechecks passed. The aggregate root typecheck did not complete because the root script's contracts build tries to write `packages/contracts/dist` outside the writable worktree.
- FogReconstruction existing .NET 8 Release binary self-test passed. `dotnet run` could not read the protected user NuGet.Config, so no rebuild was performed for this documentation/harness-only continuation.
- No production behavior, retry limit, profile semantics, version, safe baseline tag, V1-V4 behavior, live Steam DSR, game process, or game memory changed. No `DECISIONS.md` entry was warranted.

## Overnight physical-witness audit (2026-10-28; shadow-only)

The completed focused boss-boundary lifecycle shift is recorded in commit `fb1c312092384399d225fea9f995ea7ceabee556`. The overnight continuation used the same baseline, canonical dataset, and staging trees. It did not regenerate worlds. The local tag `v5-safe-ceaseless-baseline` still resolves to `abc6a5286353618a1087b8363bf9438236b8fb10`.

The read-only source was `C:\Users\Adam\ds1tr-qa\game-inst\DARK SOULS REMASTERED`; its project reference subset had previously verified 19/19 files. This continuation reread the relevant `map/MapStudio/m14_01_00_00.msb`. The QA `map/` tree contains 35 loose MSB files and no collision mesh/archive assets, so its MSB positions and collision names cannot prove a continuous walkable route. The live Steam installation and game process were not accessed.

### Result evolution

| Checkpoint | PROVEN_ACTIONABLE | PROVEN_NON_ACTIONABLE | UNKNOWN | Solvable | Unsolvable | Unresolved |
|---|---:|---:|---:|---:|---:|---:|
| Focused-shift start / 0.3.0 report | 483 | 0 | 4,639 | 0 | 0 | 331 |
| Overnight final / 0.3.2 report | 483 | 0 | 4,639 | 0 | 0 | 331 |

No physical mechanism class was newly promoted. The correction from 0.3.0 to 0.3.1 fixed the Mode-2 p0=0 coverage display; it did not change edge certainty. Version 0.3.2 adds each world's full `CandidateEdges` to schema-4 JSON, making the blocker frontier reproducible from per-case graph/evidence records. These edges remain subject to the existing strict certification rules.

Fresh audit: `C:\Users\Adam\AppData\Local\Temp\ds1tr-ceaseless-focused-shift-20261028\audit-331-v4-final.json` (SHA-256 `DA16DDF3376CB667FE43AB4FDB7760B26516411CD8DE9A970FFC7F56FB040DEB`), and `.md` (SHA-256 `61AE337F0E19B5B1DBD559749079259B53429676EA435C7A1EE4910D9028A521`). The JSON retains 5,122 per-case candidate edges. A second final run produced byte-identical JSON and Markdown. Results are 483 actionable, 0 non-actionable, 4,639 unknown, 0 positive whole-world witnesses, and 331 unresolved. Historical package hashes are not anchored by the dataset; current staging-tree hashes remain untrusted observations.

### Fixed-transition evidence rules and current status

No row is classified from `Area.To`, a matching map name, or `ConnectCollision` alone. The required evidence and current classification are:

| Mechanism class | Current status in this corpus | Evidence required before promotion |
|---|---|---|
| Fixed map-boundary traversal | UNKNOWN; no candidate rows | Exact directed boundary anchors, connected collision/approach, destination-side anchor, and state conditions. |
| Doors | UNKNOWN; no candidate rows | Exact door and collision identity, interaction/open event, lock/item/flag condition, opened-state persistence, and destination-side connection. |
| Elevators | UNKNOWN as physical edges; no candidate rows | Platform and floor positions, call/ride/arrival state, collision ownership, and reachable approach at both ends. A live controller is not proof that the player can reach it. |
| Ladders | UNKNOWN; no candidate rows | Exact ladder identity, top/bottom anchors, interaction and attachment behavior, state/one-way rules, and connected approaches. |
| One-way drops | UNKNOWN; no candidate rows | Reachable source platform, collision-supported landing, survival/no-clipping evidence, and a connected route after landing. |
| Boss-unlocked routes | UNKNOWN as physical edges; Ceaseless evidence below | Boss-defeat flag producer, barrier/door state transition, post-defeat actionability, and a connected physical route in that state. |
| Item-gated doors | UNKNOWN; no candidate rows | Exact required item and event condition, possession/consumption semantics, door state transition, and both physical approaches. |
| Lordvessel-gated routes | UNKNOWN; no candidate rows | Exact Lordvessel state/flag, route activation event, physical entrance and destination proof. A warp/menu option is not a physical edge. |
| Fixed fog boundaries | UNKNOWN; no candidate rows | Exact object, ActionRegion, event binding, gating state, destination player/map, landing, and physical approach. |
| Scripted non-random transitions | UNKNOWN generally; event 11410090 is not route-relevant in this pinned corpus | Exact caller chain, trigger conditions, warp target and resulting player/map state. The event-11410090 finding is corpus-scoped and does not classify other scripts. |

“No candidate rows” means this 331-case query graph did not contain a row for that class; it does not mean the class is absent from Lordran. Existing exact certifications remain limited to ordinary Mode 0, the enumerated PvP gate subset, two Firelink CustomWarp sides, and the Seath Mode-2 p0=5308 chain. No certification was generalized to unobserved mechanisms.

### Ceaseless route and minimum candidate frontier

The exact QA MSB positions corroborate object identity, not reachability: boss-side object `o4961_0000` / entity 1411790 is at `(249, -283.15, 70.4)` with collision `h0005B1`; boss-start region 1412698 is centered at `(248.5, -284, 69.7)`. The two boss-completion light walls are `o4962_0000` / entity 1411410 / collision `m1030B1` at `(148.9, -341.04, 94.6)` and `o4963_0000` / entity 1411412 / collision `h0018B1` at `(110.38, -341, 140.5)`. Elevator regions 1412400–1412403 are present and the controller is preserved, but map coordinates cannot prove walkability between those locations.

Vanilla event 11410410 and the defeated-state branches in event 11415342/event 0 release the two light walls. All 331 current staging trees labeled as generated packages retain the barrier objects but omit those three known release blocks. The dataset has no historical per-package hashes, so this is an observed source-to-staging lifecycle mismatch, not authenticated proof about the original run bytes and not proof that every alternate route is blocked or that a post-defeat physical exit exists. Event 11410090 has no initializer caller in the scanned pristine or staged corpus and is not used as a route edge.

The canonical dataset's `postBossPath` has 1,459 random steps: 574 uniquely match a `randomTransitions` identity, 885 match none, and zero match multiple rows. By world, 94 paths match every step, 41 have a mix of matching and unmatched steps, and 196 have no exact matching step. These mismatches are an identity/provenance blocker; they are not evidence that generation itself is wrong.

Using schema-4 per-case candidate edges and the recorded start/goal anchors, 119 worlds have a candidate directed graph path from post-Ceaseless arena to lower Demon Ruins/lava when UNKNOWN edges are treated only as possible. **None is a witness.** The shortest candidate paths contain six UNKNOWN edges in 13 worlds. Across the 119 selected shortest graph paths, every path requires all four classes below; these are potential candidate worlds, not worlds shown playable:

| Required unresolved class | Candidate paths/worlds affected | Evidence still missing |
|---|---:|---|
| Physical approach to each post-boss Fog trigger | 119 worlds; 506 candidate edges on selected shortest paths | Reachable player-side/collision evidence at each trigger, after boss completion where applicable. |
| Boss-boundary Mode 0 transitions | 119 worlds; 413 candidate edges on selected shortest paths | Full gate lifecycle and exact event/landing/actionability proof for each edge. |
| Arrival player to main Demon Ruins physical side | 119 paths (122 raw candidate rows exist) | Collision geometry and a connected walkable route from the exact generated landing. |
| Main Demon Ruins side to lower lava route | 119 paths (122 raw candidate rows exist) | A connected, state-correct physical route; Area.To and collision linkage are insufficient. |

Some shortest candidates also include unresolved path-to-transition identity (34 worlds) or ordinary Mode 0 edges (15 worlds). For representative `A-disconnected-off-lords-on/5351`, the six-edge lower-bound path is: approach `o4961_0000` on `demonruins_ceaseless` → its boss-boundary Mode 0 edge to Painted World player 1815827 → approach `o1600_0000` → its boss-boundary Mode 0 edge back to Demon Ruins player 1815855 → landing-to-main route → main-to-lower-lava route. Each arrow remains UNKNOWN. The invalid `demonruins_ceaseless -> demonruins_lava` shortcut is not present.

The smallest evidence package for a first trustworthy positive witness is one contemporaneously authenticated generated package plus a complete proof of those six edges for a six-edge candidate world: boss-state and barrier behavior; both trigger approaches and boss-boundary transitions/landings; and collision-backed player traversal from player 1815855 to the main route and then lower lava. The current QA `map/` read set lacks collision meshes, and historical staging has no package hashes. Static files available in this corpus therefore cannot close the physical route; read-only runtime observation or a separately verified collision-geometry source is required. This is a candidate-level frontier, not a global shortest physical path.

### Mode-2 p0=0 families

The corrected report counts 1,986 Mode-2 p0=0 edges across all 331 worlds (six families per world), all UNKNOWN. They remain classified D: unresolved. No conclusion that zero means unconditional or broken is supported. Flag zero appears in vanilla conditions and as a parameter value, while no direct source producer was established for the generated p0=0 calls. The six source families retained in the dataset are:

| Family | Source event | Observed source flags | Landing |
|---|---|---|---|
| Firelink crow → Asylum | m10_02_00_00 / 11020021 | 5301, 11020000 | asylum / 1812110 |
| Asylum crow → Firelink | m18_01_00_00 / 11810150 | 5307 | firelink / 1022121 |
| Anor Londo painting entry | m15_01_00_00 / 11510230 | 5305 | paintedworld / 1102510 |
| Painted World return | m11_00_00_00 / 11100710 | 5302, 11105380 | anorlondo / 1512500 |
| Catacombs coffin → Tomb | m13_00_00_00 / 11305000 | 5309 | totg / 1312110 |
| Tomb coffin → Catacombs | m13_01_00_00 / 11310052 | 5310 | catacombs / 1302010 |

Event5700Builder passes p0 unchanged; pinned FogMod does the same for mode 2. Those are implementation facts, not proof of runtime sentinel semantics. Comparable vanilla flag-zero conditions and parameterized callers prevent classifying p0=0 as automatically invalid. These six classes stay out of the actionable graph until a full source/caller/runtime proof exists.

### Staged authenticity, telemetry, and verification

The existing package hashes are still current staging observations, not historical authentication. A future analysis manifest proposal (not implemented) is: schema and dataset identity; Seed Code/profile/options; raw and derived seeds; attempt; app/backend/FogHost/DsrDataTool versions; source commit plus dirty-tree fingerprint; catalog and world-data fingerprints; sorted relative file paths, sizes and SHA-256 values; and a canonical package digest. A self-hash alone is not trust. The manifest must be anchored by an immutable CI artifact or signature key unavailable to the staging writer before an audit can treat it as provenance.

Read-only telemetry is needed to prove the missing runtime collision/actionability facts if verified geometry is unavailable. A future helper must be locked to the exact QA executable and package hashes, request query/read process rights only, and record current map, player XYZ, boss flag 11410410, transition/action observations, and barrier state with timestamps. Offsets must be independently derived and validated against known map/position/flag markers before observations are accepted. It must fail closed on hash/marker mismatch and must never request write rights. No telemetry helper was implemented or run in this shift.

Verification for the development-only FogReconstruction tool v0.3.2: .NET 8 Release build succeeded with 0 errors and 14 pre-existing obsolete-SoulsFormats API warnings; full tool self-test passed, including the new five-case Mode-2 p0=0 coverage check; the 331-case audit passed twice with byte-identical schema-4 JSON/Markdown. No worlds were regenerated. V1–V5 production semantics, production validator, retry limits, production catalog, safe baseline tag, and live Steam DSR were unchanged. No game process was opened and no process memory was read or written. No architectural decision was made; `docs/DECISIONS.md` is unchanged.
