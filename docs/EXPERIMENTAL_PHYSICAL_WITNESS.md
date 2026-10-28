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
