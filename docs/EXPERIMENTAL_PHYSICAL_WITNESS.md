# Experimental Physical Witness

Status: partial shadow-analysis tool; not production validation and not yet ready to certify gameplay route classes.

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
- Development-only FogReconstruction version: 0.2.0

The tag points at the exact baseline commit and was not moved. The experimental code lives under tools/fog-reconstruction and has no production dependency.

## Model and certainty rules

Each directed edge carries source provenance, locator, and SHA-256. The only certainty values are PROVEN_ACTIONABLE, PROVEN_NON_ACTIONABLE, and UNKNOWN.

Positive search traverses only PROVEN_ACTIONABLE edges whose typed event-flag and item conditions hold. A reachable UNKNOWN edge prevents an impossibility claim. A negative result is permitted only after a complete physical inventory with no reachable UNKNOWN escape and no exhausted search budget. Non-actionability is state-scoped unless direct telemetry proves a global refusal.

PROVEN_ACTIONABLE validation requires separate source-action-region, generated-dispatcher, and destination-player evidence. Area.To and ConnectCollision by themselves are rejected. Witness replay recomputes the route fingerprint and compares every edge, state transition, mechanism, and evidence record to the supplied graph.

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

| Measure | Result |
|---|---:|
| PROVEN_ACTIONABLE edges | 0 |
| PROVEN_NON_ACTIONABLE edges | 0 |
| UNKNOWN edges | 2,823 |
| Safe REJECT / PHYSICALLY_SOLVABLE_PROVEN | 0 |
| Safe REJECT / PHYSICALLY_UNSOLVABLE_PROVEN | 0 |
| Safe REJECT / UNRESOLVED_MODEL_GAP | 331 |
| Cases with a reachable UNKNOWN escape | 331 |
| Positive witness classes | 0 |
| Manual QA candidates | 0 |

The safe REJECT disposition is inherited from the certified dataset selection and was not rerun. UNRESOLVED_MODEL_GAP is not a finding that those worlds are physically impossible.

The 1,019 structurally complete transition rows are dataset claims only: 864 ordinary, 131 PvP, and 24 boss-boundary rows. None is promoted to PROVEN_ACTIONABLE because the bytes are not authenticated and trigger-state actionability and physical approach are unverified. The post-boss graph contains 695 case-specific trigger-approach candidates. It joins route hints by source object identity only; ambiguous identities remain UNKNOWN. It never uses logical Area labels as physical destinations.

Mechanism coverage:

| Mechanism | Status | Rows |
|---|---|---:|
| All structurally complete randomized transitions | STRUCTURAL_CLAIMS_ONLY | 1,019 |
| Ordinary fog | STRUCTURAL_CLAIMS_ONLY | 864 |
| PvP fog | STRUCTURAL_CLAIMS_ONLY | 131 |
| Boss-boundary randomized fog | STRUCTURAL_CLAIMS_ONLY | 24 |
| Seath p0=5308 | TRACE_ROWS_ONLY | 331 |
| Boss completion state 11410410 | TRACE_ROWS_ONLY | 331 |
| Fixed links | UNKNOWN | 0 |
| Doors | UNKNOWN | 0 |
| Elevators | UNKNOWN | 0 |
| Mode-2 p0=0 warps | UNKNOWN | 0 |
| Ceaseless exit and route to lower lava | CANDIDATES_ONLY | 695 |
| Collision and walkability | UNKNOWN | 0 |

Reports are outside the repository:

- C:\Users\Adam\AppData\Local\Temp\ds1tr-physical-witness-shadow-final3-20261028\physical-witness-331.json
- C:\Users\Adam\AppData\Local\Temp\ds1tr-physical-witness-shadow-final3-20261028\physical-witness-331.md

A third complete audit run after the final build produced byte-identical JSON and Markdown. JSON SHA-256: 71C2EC05ABC6933F5D79257405BF59CB7EC2D58AF74FFBBB330B156753E07DEB. Markdown SHA-256: A92DB9E3192DD0641925E57871472FD4E806C609888511A58B26F1E2F37DFFD7. The replay report is in C:\Users\Adam\AppData\Local\Temp\ds1tr-physical-witness-shadow-final2-replay-20261028 and had the same hashes. JSON includes complete step and evidence arrays for any future positive witness.

## Read-only QA telemetry design

No telemetry helper was implemented or run in this task. No game process was opened, no QA DSR files were read, and no Steam installation was accessed.

A future helper belongs in a separate development-only tools/qa-telemetry project, with no application dependency. It should accept only an explicitly selected QA executable path and a recorded executable/data identity, verify hashes before attaching, and refuse any process whose image path or hashes differ. The Windows process handle should request query/read rights only; the implementation must not request VM_WRITE or VM_OPERATION, patch memory, or send input. The helper should emit append-only JSONL to a caller-selected Temp directory.

Potential observations are current map, player XYZ, map transitions, boss completion flags, and explicitly selected relevant event flags. Memory offsets are currently unverified and must not be guessed. They must be derived reproducibly for the exact binary and validated against known markers: Firelink map identity and coordinates, a known boss flag, and a known map transition. The helper must report UNVERIFIED and refuse to emit gameplay evidence until every marker passes. A changed executable or data hash must fail closed. Manual play is the input method; no bot or automation is in scope.

## Verification and invariants

- FogReconstruction Release build: passed, 0 errors; 14 existing SoulsFormats obsolete-API warnings.
- Development tool self-test: passed, including 6 physical-witness rule groups, 4 report path-safety cases, and existing tool self-tests.
- 331-case read-only audit: 331/331 unresolved; deterministic repeat hashes match.
- Report-path safety rejects output aliases to the dataset, outputs under staging, pre-existing output files, and colliding JSON/Markdown paths. Input traversal rejects reparse points and checks all ancestors.
- V1-V5 generation semantics, production validator, retry limits, package versions, and app/backend/FogHost versions were not changed.
- The verified QA DSR and live Steam DSR were not accessed or modified.
- No positive witness classes exist, so there are no manual QA candidates yet.