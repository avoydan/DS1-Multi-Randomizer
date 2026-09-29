# Implementation status

**Windows desktop packaging (2026-09-29):** Electron now starts and stops the backend,
resolves bundled runtime files, stores mutable data outside the app directory, and
offers a FogMod data-root picker. Desktop 0.6.1 and backend 0.18.0. A portable x64
artifact is built at `apps/desktop/release/DS1-Triple-Randomizer-0.6.1-x64.exe`.
It bundles DsrDataTool, FogHost, the combined JS core and license notices; FogMod's
game-derived DS1R data is excluded. See `docs/DESKTOP_PACKAGING.md` for the layout,
build command and .NET 8 runtime prerequisite. Packaged-runtime API/tool smoke checks
passed on staging data. The final portable executable launched outside the restricted
sandbox: the renderer showed `Backend online` and first-run DSR/Fog guidance; health
returned 200 with backend 0.18.0; a missing DSR path returned an explicit problem;
the plan reported the combined engine and FogHost ready. Closing the window stopped
the portable wrapper, Electron main process and backend child. The sandboxed preload
had to compile as CommonJS (`preload.cts`); desktop 0.6.1 includes that fix. No public
GPL fork or release tag exists yet.

- Verification: root typecheck clean; backend tests 108 passed, 1 skipped (requires a
  user Fog data root); desktop, backend, DsrDataTool and FogHost builds passed;
  `package:win` completed; production-dependency audit found 0 advisories.
- Portable smoke: 0.6.1 executable launched from `release/`, renderer preload
  connected, backend health returned 200, missing DSR returned
  `DarkSoulsRemastered.exe not found.`, both packaged tools were reported ready,
  first-run DSR/Fog guidance was visible, and normal window close ended the wrapper,
  Electron main and backend child. Bundled DsrDataTool and FogHost executables ran
  and printed their usage messages. A staged-data run without a Fog root returned
  `FOG_PREREQUISITES_MISSING` before randomization. No live game files were touched.
- Release prerequisites: install .NET 8 runtime on the user machine; supply FogMod
  DS1R data locally; create the public GPL corresponding-source fork and matching
  release tag. Code signing and a custom icon are not configured.


**Boss randomization + Fog works (FogHost 0.5.0, backend 0.17.0, desktop 0.5.0,
2026-09-29).** The `FOG_BOSS_RANDOMIZATION_UNSUPPORTED` rejection is removed, and bosses
are on by default again.

- **Mechanism.** A whitelisted boss-event overlay: FogMod's event is the control-flow base,
  and the combined engine's known boss mutations are re-applied by content. It covers the
  15 boss events both engines edit. Details: `docs/FOGHOST_INTEGRATION.md`,
  `docs/DECISIONS.md`.
- **Also:**
  - FogMod patch 8 (Asylum Demon removal keeps skips consistent);
  - merge refinements: unambiguous-result alignments, skip range = covered lines, and
    event-0 initialization removal;
  - refused events log their base, combined and FogMod versions.
- **Verification:**
  - FogHost 68/68;
  - backend 109/109;
  - typecheck clean;
  - Items + Enemies + Bosses + Fog 40/40 (no conflicts, invariants pass, all skip checks
    clean);
  - 3 full proofs with byte-identical rerun and activation/restore;
  - regressions: Items + Enemies + Fog 5/5 and Items + Fog 5/5.
- **Remaining:**
  - the key-item combined-retry rate with bosses + Fog is 10 of 50 attempts, and no seed
    has exhausted the budget, so the budget is unchanged;
  - the overlay whitelist is fixed to the 15 measured events, so any other boss event
    edited by both engines stays a `FOG_MERGE_CONFLICT`;
  - the FogHost and DsrDataTool test consoles run outside `npm test`;
  - there is no public GPL fork yet;
  - desktop packaging has not started.

FogHost 0.3.1 (2026-09-29): FogMod's boss-trigger rewrite now keeps enclosing DS1 SKIP
counts consistent (vendored patch 7, `tools/fog-host/src/SkipRanges.cs`).

- **Verification:**
  - FogHost self-tests 52/52, including 5 new ones;
  - each of the 11 previously broken FogMod-only events (`11015393`, `11205383`,
    `11215002/3/12/13`, `11405393`, `11415343/383`, `11505393`, `11515393`) checked
    explicitly: 0 mismatches.
- **40-seed sweeps** (live install unchanged):
  - Items + Enemies + Fog 40/40;
  - Items + Fog 40/40.

  Neither had merge conflicts or skip mismatches, in combined packages or in final Fog
  packages (checked against FogMod's templates). Key-item retries are unchanged: 7 of 47
  and 8 of 48 attempts, and no seed exhausted the budget.
- **Remaining:**
  - bosses + Fog is still rejected;
  - the Asylum `11810310` FogMod removal is not skip-adjusted, but it runs only for
    bosses + Fog;
  - the FogHost and DsrDataTool test consoles run outside `npm test`;
  - there is no public GPL fork yet.

FogHost 0.3.0 and backend 0.16.2 (2026-09-29). **Items + Enemies + Fog works on real-format
data: 40/40 seeds.**

- **Changes:**
  - derived SKIP-count adjustments are accepted by the merge under strict conditions;
  - the Fog bridge now reads `randomizer.json` `placements` (the preserved-row list had
    been empty in production);
  - the model-declaration invariant false positive is fixed.
- **Verification:**
  - FogHost self-tests 47/47;
  - DsrDataTool tests 14/14;
  - backend 109/109 with the data root;
  - typecheck clean.
- **Sweep** (`fogHostE2e.mjs`, 40 seeds; live install unchanged):
  - 40/40 succeed, with post-write invariants passing;
  - 0 merge conflicts;
  - combined `verify-skips` 47/47;
  - final Fog packages have 0 skip mismatches in combined or merged events;
  - key items fell outside annotated lots on 7 of 47 attempts (5 seeds retried, none
    exhausted).
- **Remaining:**
  - 11 FogMod-only boss-trigger events have skip ranges broken by stock FogMod's own
    runtime rewrite. This is an upstream observation, not fixed here.
  - Bosses + Fog is still rejected.
  - The FogHost and DsrDataTool test consoles run outside `npm test`.
  - There is no public GPL fork yet.

DsrDataTool skip-count fix (2026-09-28; backend 0.16.1; user-authorized local vendored
modification). The combined engine now keeps DS1 "skip N lines" counts consistent when it
removes or inserts event instructions. Details: `docs/DECISIONS.md`.

- **Tests:**
  - DsrDataTool tests 14/14 (`engines/combined/tests/DsrDataTool.Tests`);
  - FogHost 37/37;
  - backend 108/108;
  - typecheck clean.
- **Real-format 40-seed sweeps**, with every combined package checked by `verify-skips`
  (live install unchanged):

  | Configuration | Result |
  |---|---|
  | Combined-only, Items + Enemies + Bosses | **40/40**; 40/40 packages with 0 skip mismatches (about 134 skips adjusted per package) |
  | Items + Fog | **40/40**; 48/48 skip checks pass; key-item retries on 8 of 48 attempts |
  | Items + Enemies + Fog | **0/40**; 47/47 skip checks pass (38 skips adjusted per package) |

  The Items + Enemies + Fog failures are FogHost merge refusals, not engine errors:
  - `11505392`: rule 4, because the combined engine now correctly changes a SKIP count
    (40 seeds);
  - `11600400`: the adjacency rule (39 seeds).
- **Open decision:** whether rule 4 may accept a skip-count change that is exactly
  explained by the same side's own edits inside that range.

FogHost 0.2.0 (2026-09-28): instruction-level EMEVD merge (user option B), a FogMod
scaling crash fix (patch 6), and boss randomization off by default on the desktop
(desktop 0.4.1).

- **Merge policy:** `docs/FOGHOST_INTEGRATION.md` rule 3 and `docs/DECISIONS.md`.
- **Verification:**
  - FogHost self-tests 37/37 (15 new merge tests, including the `11505392` pattern);
  - backend 108/108;
  - root typecheck clean.
- **Real-format sweeps** (`fogHostE2e.mjs`, `DS1TR_E2E_SWEEP=40`; live install
  unchanged):

  | Configuration | Result |
  |---|---|
  | Items + Fog | **40/40**. Key items fell outside annotated lots on 8 of 48 combined attempts (16.7%); 6 of 40 seeds needed a combined retry; none exhausted the budget. |
  | Items + regular enemies + Fog | **0/40**. 39 are `FOG_MERGE_CONFLICT`: `m15_00_00_00:11505392` in 39 seeds and `m16_00_00_00:11600400` in 38. `m15_00_00_00:11500001` merged at instruction level in 39 seeds. Key items fell outside annotated lots on 7 of 47 attempts (14.9%); 5 seeds needed a combined retry. One transient `DATA_TOOL_FAILED` did not reproduce on rerun. |

  Before patch 6, 11 of 40 enemy seeds crashed with `FOG_ENGINE_FAILED`.
- **Blocker:** both remaining conflicts are caused by the combined engine.
  `DsrDataTool.RemoveEventInstruction` deletes instructions inside SKIP ranges without
  adjusting the line count. One package had 38 such skips in 21 events, and this
  affects combined-only packages too. FogHost refuses to merge them by design. Fixing it
  needs a change to the vendored `DsrDataTool`, which is a user decision.

FogHost v1 (2026-09-28): `tools/fog-host` (FogHost 0.1.0, .NET 8) is built and verified;
`@ds1tr/backend` is 0.16.0.

- **Engine.** FogMod is re-pinned to `6e6dc78`, because `28711c3` cannot run DS1R. The
  host uses SoulsIds `e31a6fd` and `JuicerMV.SoulsFormats` 0.1.3. `GameDataWriter.cs`
  carries a documented patch set: API port, no backups, deferred writes, preserved
  NpcParam rows, and an EMEVD merge hook.
- **Host behavior:**
  - the schema-v2 contract is unchanged;
  - DS1R only, with `dryRun` and `write` modes, and writes only to staging;
  - deterministic output, SHA-256 output list;
  - three-way EMEVD merge;
  - in-memory and post-write invariants;
  - typed failures.
- **Data root.** The FogMod data root is supplied by the user (`DS1TR_FOG_DATA_ROOT`),
  never bundled. It is verified against a 59-file SHA-256 manifest.
- **Backend changes:**
  - a new default `getFogPrerequisites` that receives the Fog language (fixes the
    staged `msg/<language>` file);
  - boss randomization + Fog is rejected with `FOG_BOSS_RANDOMIZATION_UNSUPPORTED`.
- **Verification:**
  - FogHost self-tests: 22/22 with a data root, 12 passed + 10 skipped without one;
  - backend `npm test`: 108/108;
  - root typecheck clean;
  - real-format proof `apps/backend/scripts/fogHostE2e.mjs`:
    - Items + Fog passes 7/7 seeds, including Fog package activation and restore on
      a temp copy (live install unchanged);
    - **regular enemies + Fog fails 5/5**: four seeds hit `FOG_MERGE_CONFLICT` on
      `m15_00_00_00` event `11505392` (both engines edit it), and one seed exhausted
      the key-item retries.
- **Remaining:**
  - enemies + Fog needs an instruction-level merge or a product rule (user
    decision);
  - the desktop default (bosses on, Fog on) is now rejected by design;
  - the FogHost self-tests are not part of `npm test`;
  - no public GPL fork yet.

  Details: `docs/FOGHOST_INTEGRATION.md`, "FogHost v1 implementation".

FogHost preparation (2026-09-28, docs only, no code): the implementation
handoff is `docs/FOGHOST_IMPLEMENTATION_CHECKLIST.md`. It covers:
- the exact pins: FogMod `28711c3`, SoulsIds `e31a6fd` (Apache-2.0, verified),
  and the SoulsFormats candidates;
- the 60-file FogMod data root;
- the project layout, build commands and test plan;
- the `GameDataWriter.cs` patch points.

A throwaway build probe (outside the repo) showed that the audited FogMod
pin `28711c3` cannot generate DS1R (upstream DS3 regression). FogMod
`6e6dc78` + SoulsIds `e31a6fd` + NuGet `JuicerMV.SoulsFormats` 0.1.3, with a
six-rule API port of `GameDataWriter.cs`, builds on net8.0 and runs a
deterministic DS1R dry run and write. Source reading closed two open design
items. FogMod never removes enemy
parts. FogMod `scale` skips non-vanilla NpcParam rows and re-points
`NPCParamID` to clone rows, so the Task 11 "composed scaling" rule and
invariants 1 and 3 need a user decision before the host is built. No plan
task builds FogHost yet.

Task 16 is the final end-to-end audit. `@ds1tr/backend` is 0.15.1.

Fixed:
- **Pristine snapshot omitted `paramdef/`.** The snapshot is the only
  generation source, but `managedRoots` lacked `paramdef`. Without
  `paramdef/paramdef.paramdefbnd.dcx`, DsrDataTool `scan` silently produces
  a catalog with no gifts, drop lots, world lots, starting classes or enemy
  metadata. `patch-enemies` then fails reading it. Task 8 missed this because
  its hand-built copy included `paramdef/`. `paramdef` is now a managed root,
  and a gameState test pins the read-set roots. No snapshots existed yet, so
  no migration is needed.
- `npm test` failed when run from `apps/backend` (npm's cwd). The API
  integration test resolved `apps/backend` from cwd, and Node 24 crashed
  natively on the failed spawn. It now resolves relative to the test file.
- Manifest `appVersion` and `/api/health` report 0.15.1.

Verified (2026-09-28):
- Backend `npm test`: 103/103.
- `apps/backend/scripts/releaseAuditE2e.mjs` (not part of `npm test`; needs
  the live install, built DsrDataTool and .NET 8) passed 18/18. The live
  install was only read. A temp reconstruction of its managed roots (legacy
  `.bak` moved over originals, as in Task 8) went through the production
  path with a temp state root:
  - `importPristine`: 198 files, including paramdef;
  - `generateRun`: combined Items + Enemies with all item options, bosses and
    area scaling, Fog off, 54 patched files including GameParam;
  - a second run with the same seed is byte-identical in `mod/`;
  - no write into the game during generation;
  - `activate`: state `active`, and a second activation is refused;
  - `restore`: the game matches the reconstruction and the pristine store
    still verifies;
  - the live install is byte-identical afterwards.
- Manifest: records the master seed, per-attempt derived seeds and results,
  combined engine version, validation, file hashes, snapshot binding and
  appVersion.
- Licensing: no legacy Python engine code and no FogMod sources are
  tracked. The only vendored code is `kellernz1/ds-randomizer`
  (GPL-3.0-only) with notices. SoulsFormats (GPL-3.0-only, NuGet) is noted in
  both notice files. No game data is tracked.
- Retries are bounded and deterministic, and are recorded per attempt (unit
  tests). Combined retries are exercised only on `GENERATION_FAILED`.

Not verified / release blockers (not fixed here; outside a correctness fix):
- **Fog is not functional.** FogHost is unbuilt and no data root exists.
  No planned task builds it (the Task 12 prerequisite has no numbered task),
  so Task 17's FogHost availability check cannot pass until one is added.
  Fog-enabled generation always returns `FOG_PREREQUISITES_MISSING`. The
  combined -> Fog bridge, Fog retries, the Fog package layout
  (`gameTreeRoot: ''`) and Fog activation have never run. When FogHost is
  built, its read set must fall within the snapshot roots, or staging fails
  closed. The desktop default config enables Fog, so a default run fails
  until then.
- **No in-app recovery from `modified`.** `/api/game/restore` does not expose
  `overwriteUnrecordedChanges`, and the desktop enables restore only for
  `active`/`interrupted`. A Steam file verification leaves stray files in
  managed roots, so the state stays `modified`.
- **No re-import after a game update.** `version-changed` refuses restore,
  and `importPristine` refuses with `SNAPSHOT_EXISTS`. The only way out is
  deleting the state directory by hand.
- Provenance remains "no legacy artifacts", not "proven vanilla" (no
  reference hashes). The real-data proof used reconstructed, not
  verified-vanilla, data.
- Distribution: a public GPL fork with source tags, and FogMod `dist/` data
  licensing, are unresolved (`docs/LICENSING.md`).
- `engines/item/README.md` and `engines/enemy/README.md` still describe
  legacy vendor probes that the backend no longer uses. These are docs only.

Task 15 completes the desktop controls for the supported combined and Fog
options. The run panel now shows the active game package, generation stage and
percentage, logs, failures, and a persistent generated-package summary. A
generated package can be activated only while the current settings still match
the settings used to generate it; changing options also clears a stale plan
preview. Empty seeds and a configuration with no enabled engine cannot start
generation. Desktop version 0.4.0. Desktop build and typecheck passed
(2026-09-28). Fog-enabled generation remains dependent on an unbuilt FogHost;
Task 16 owns the final end-to-end audit.

Task 14 connects the existing backend pipeline and game-state authority to
the desktop application. The backend exposes state, pristine import, streamed
generation, activation, and vanilla restore endpoints. The desktop sends the
typed configuration, displays progress/logs/errors and the generated package,
and enables activation or restore according to the reported game state. A
backend-owned run directory keeps workspace paths out of the renderer. The
new mutation routes reject ordinary foreign browser origins; packaged file
origin remains accepted. Backend 0.15.0, desktop and contracts 0.3.0.
Focused API integration (1/1), pipeline (4/4), backend/desktop builds and
desktop typecheck passed (2026-09-28). FogHost is still unbuilt, so Fog-enabled
generation reports `FOG_PREREQUISITES_MISSING` until that prerequisite exists.
Desktop packaging/startup and full UI polish remain later tasks.

Task 13 adds the single activation/restore authority in
`apps/backend/src/core/gameState.ts` (see `docs/DECISIONS.md`, "One
backend-owned activation/restore authority"). It covers the following:

- a hash-verified pristine snapshot import that refuses legacy `.bak` chains;
- state detection;
- journaled, atomic, verified activation of a generated package;
- automatic rollback;
- vanilla restore.

Destructive operations refuse unknown, stale or running-game states. The
snapshot is now the pipeline's trusted pristine source: `server.ts` injects
`getPristineSource`. The run manifest records `pristineSnapshotId` and
`gameTreeRoot`. No HTTP/IPC endpoints for import/activate/restore exist yet;
that wiring is Task 14. Reference vanilla hashes do not exist, so provenance
is "no legacy artifacts", not "proven vanilla". Backend `npm test` passed
102/102, including 15 focused authority tests (2026-09-28). `@ds1tr/backend`
is 0.14.0.

Task 12 wires the backend generation sequence in
`apps/backend/src/core/generationPipeline.ts`: DSR verification, a verified
pristine-source gate, combined generation in an isolated workspace, output
hash checks, Fog staging and bounded deterministic retries, final output
checks, a package manifest, and typed progress/log events. `/api/randomize`
accepts the generation request and can stream events with
`Accept: text/event-stream`. It never activates files into the game.

The default pristine-source and Fog-prerequisite providers intentionally
return typed `PRISTINE_IMPORT_REQUIRED` and `FOG_PREREQUISITES_MISSING`
failures. No backend pristine authority or built FogHost/data root exists yet;
supplying an arbitrary path is not proof of clean game data. The pipeline can
be exercised with trusted injected providers, but production generation
requires the two separately recorded prerequisite tasks in
`docs/AI_EXECUTION_PLAN.md`. Combined package checks currently cover the
shared patched files described by the bridge; Fog output checks cover files
declared in the host response. Focused pipeline tests and backend build and
typecheck passed (2026-09-28). `@ds1tr/backend` is 0.13.0.

Task 11 defined and implemented the backend side of the Combined -> Fog
compatibility bridge:

- `apps/backend/src/adapters/fogCompatibility.ts` (pure, no I/O):
  - `buildFogCompatibility` derives the schema-v2 `compatibility` object
    from `patch-manifest.json` and the placements file;
  - `fogReadSet` and `planFogStaging` plan the per-attempt staging and
    merge-base directories;
  - `decideFogRetry` is the bounded, deterministic retry table.
- `seeds.ts` gained `deriveAttemptSeeds`; attempt 0 equals `deriveSeeds`.
- `fogHostProcess.ts` moved to schema v2 and validates `compatibility`. It
  also accepts the new codes `FOG_MERGE_CONFLICT` and
  `FOG_COMPATIBILITY_VIOLATED`.

The host-side rules are specified in `docs/FOGHOST_INTEGRATION.md`
("Compatibility bridge"). They cover the three-way EMEVD merge, preserved
NpcParam rows, stock key-item handling and post-write invariants. Fog
scaling and combined scaling compose (decision in `docs/DECISIONS.md`).

Verification: backend tests 83/83 pass, including the build (2026-09-28).
`@ds1tr/backend` is 0.12.0.

Still open:
- the C# host is unbuilt, so none of the host rules is compiled or tested;
- boss randomization together with Fog is unproven (`11810310` conflict);
- the key-item retry rate is unmeasured;
- FogMod's `scale` mechanism is unread, so scaling composition is a
  hypothesis (open item 8).

Task 10 implemented the backend FogHost process adapter in
`apps/backend/src/adapters/fogHostProcess.ts`. It writes a typed schema-v1
request to a fresh JSON path, launches `FogHost.exe generate`, reads the
authoritative response, forwards structured progress and plain log text,
and reports host crashes and timeouts as non-retryable typed failures. It
rejects stale response paths and invalid request options before launch. The
Fog status probe now points to the planned `tools/fog-host` project and
executable. Seven focused tests, backend build, and backend typecheck passed
(2026-09-28). `@ds1tr/backend` is 0.11.0. The C# host is still unbuilt;
Task 11 owns compatibility inputs and Task 12 owns pipeline wiring.

Task 9 designed the FogHost integration (`docs/FOGHOST_INTEGRATION.md`;
decision in `docs/DECISIONS.md`). It is docs only: no code changed and there
is no version bump. The design is a console host that compiles FogMod's
engine sources at the audited commit `28711c3`, never FogMod's CLI (which
forces DS3) or WinForms. It forces DS1R and exchanges JSON request/response
files. FogMod writes DS1 output in place, so each attempt gets a fresh staging
directory, and the host cleans up FogMod's `.bak` files. Errors are typed and
retry-scoped (`combined` vs `fog`). The overlap analysis shows stock FogMod is
lossy on combined output:
- it replaces EMEVD and keeps only legacy-enemy-randomizer event IDs;
- it deletes the combined engine's scaled `NpcParam` rows;
- it finds key items only in annotated item lots.

FogHost therefore carries a minimal patch set in `GameDataWriter.cs` and
`ItemReader.cs`, and Task 11 defines the bridge inputs. Open items:
- no task builds or vendors the C# host yet;
- FogMod's `dist/` data is game-derived and its distribution is unresolved;
- the SoulsFormats/SoulsIds pins and SoulsIds' license are still to be checked.

Task 8 ran the combined Items + Enemies engine end to end for the first time:
typed request → config → vendored JS generator → real `DsrDataTool`
`scan`/`patch-enemies` → workspace package, via
`apps/backend/scripts/combinedEngineE2eProof.mjs` (harness commit
`75c214dc2656693b49c2861c39f3f2a7e6b8e7a8`; not part of `npm test`). The live Steam install is
legacy-modified (legacy item/enemy randomizers and FogMod applied in place);
against it `patch-enemies` correctly refuses (`DATA_TOOL_FAILED`, enemy model
declaration pruning), and the install stayed byte-identical. On a temp copy
of the tool's read set with the legacy `.bak` files restored, all 47 checks
passed (2026-09-28): generation completes, the package's 54 patched files
match `patch-manifest.json`, reruns are byte-identical apart from embedded
workspace paths and timestamps, real catalogs verify and reuse, failures are
typed and recoverable in the same workspace, and neither game directory
changed. This proves the pipeline on real-format data, not on verified
vanilla data. Findings handed to later tasks (up-front detection of
legacy-modified installs, partial seed directories after a failed patch,
missing dll reported as `DATA_TOOL_FAILED`, volatile JSON fields) are in
`docs/COMBINED_ENGINE_INTEGRATION.md` ("End-to-end generation proof").
No application code changed; `@ds1tr/backend` stays `0.10.0`.

Task 7 connected `CombinedRandomizerAdapter.generate()` to the existing
workspace-only execution bridge. It forwards the typed request and
progress/log events, returns the generated seed package directory on success,
and preserves typed bridge failures including retryability. Two focused
adapter tests passed; backend build and typecheck passed (2026-09-28).
`@ds1tr/backend` is `0.10.0`. Implementation commit:
`57d2f846ca362b91673ffcaf3ae025802e5efb64`. The adapter has not yet
run `DsrDataTool` against a real DSR installation (Task 8); generation is
not connected to activation.

Task 6 resolved the remaining combined-engine blockers that had to be fixed
before a real generation run can be attempted: the combined seed is now a
32-hex-character SHA-256 derivation of the master seed that always satisfies
upstream's charset (Blocker 2); `CombinedRandomizerRequest` gained
`generateSpoilerLog`, mapped straight through instead of pinned off
(Blocker 3); and a reused workspace catalog is verified file-by-file (size +
SHA-256) against the game directory before generation, failing with
`CATALOG_SOURCE_MISMATCH` and never rescanning automatically (Blocker 6).
Backend tests 66/66 pass and the full workspace typecheck is clean
(2026-09-28). `@ds1tr/backend` is `0.9.0`. Implementation commit:
`e2664164d1aa1131952e1ddb1a19a87d960198af`. The user confirmed the seed
derivation (option A); `ARCHITECTURE.md`, `source-analysis.md`, and
`CODEX_PROGRESS.md` now mark the earlier pass-through direction superseded.
Task 7 completed adapter wiring. Still open: a real `DsrDataTool` run
(Task 8), the public GPL fork.

Task 5 extended the focused combined bridge tests (commit
`abeb4c2f56ade130a39e4d42837bf333d586e3e0`); no production code changed.

## Backend adapters

- The minimal `CombinedRandomizerAdapter` boundary is defined for combined item
  and enemy generation requests, progress/log events, and typed results.
- The adapter delegates generation to the workspace-only execution bridge and
  maps its package directory and typed failures to the public adapter result.
- The adapter is not yet invoked by the application pipeline. When called, it
  delegates workspace generation to the bridge; activation remains separate.
- A pure, deterministic mapping layer converts combined requests to the
  upstream item/enemy configuration shape, applies parent `enabled` flags, and
  rejects requests with no enabled randomization category. Task 3 verified
  all required item/enemy combinations, parent gating, deterministic output,
  and the case where only safeguard options are selected. Six focused mapper
  tests and backend build/typecheck passed on 2026-09-28. No generation,
  filesystem, subprocess, or game access was added.
- A pure, deterministic workspace planner
  (`combinedRandomizerWorkspace.ts`) computes the catalog path, per-seed
  output directory, and `scan`/`patch-enemies` argument vectors for a
  generation run, and refuses to plan a workspace located inside the live
  game directory. It performs no filesystem I/O and never constructs
  `install`/`restore` arguments. See
  `docs/COMBINED_ENGINE_INTEGRATION.md` ("Workspace execution proof").
- `kellernz1/ds-randomizer` is selectively vendored (the files needed for
  `DsrDataTool.dll`, plus the minimal JavaScript generation core) and pinned
  at commit
  `f120efe0e95b957b7e76109868c3c9966e22720c` under
  `engines/combined/vendor/` (`UPSTREAM_PIN.md` has the full file list and
  update procedure). `DsrDataTool.dll` builds successfully from this
  vendor directory with a locally-installed .NET 8 SDK; see
  `docs/COMBINED_ENGINE_INTEGRATION.md` ("Vendored build proof") for the
  exact command and artifact path.
- `DsrDataToolExecutor` runs only the planner-provided `scan` and
  `patch-enemies` argument vectors through Node `child_process.spawn`, streams
  typed stdout/stderr log events, emits coarse process-status events, and
  returns typed success, process-failure, or spawn-failure results. It is not
  wired into the adapter or generation pipeline yet. Task 1's implementation
  is in commit `5402b447f3d53a1896ca59267fc5fe10461bc071`; its seven
  focused tests, backend build, and backend typecheck passed on 2026-09-27.
  The task remains blocked because Git could not create `.git/index.lock` to
  commit the task's plan/status update under the current filesystem policy.
- The upstream JavaScript generation core is vendored at the same pinned
  commit under `engines/combined/vendor/src/` — five files, the transitive
  closure of `generate()` and `writeOutput()`
  (`core/{config,generator,output,rng}.js`, `data/prototype-data.js`), with a
  closed import graph of Node builtins only. CLI, HTTP, Electron, share-link,
  catalog-loading, and game-detection modules are deliberately excluded.
- `combinedRandomizerCore.ts` is the backend's single loading boundary for
  that core: a computed dynamic `import()` of `file:` URLs resolved from
  `import.meta.url`, hand-written types for the six upstream exports (so the
  vendored bytes stay byte-identical), and typed `JS_CORE_MISSING` /
  `JS_CORE_INCOMPLETE` failures instead of thrown errors. It loads and
  shape-checks the core; it does not call `generate()` or `writeOutput()`
  yet. See `docs/COMBINED_ENGINE_INTEGRATION.md` ("JS core vendor proof").
- Vendored-file identity is verified by comparing Git blob object IDs against
  the pinned upstream commit, not by `diff`: `core.autocrlf` rewrites line
  endings on Windows checkout. All twelve vendored files verified; object IDs
  recorded in `engines/combined/vendor/UPSTREAM_PIN.md`.
- Documentation correction: upstream `validateConfig(config, { requireGame })`
  **returns** a `string[]` of errors and never throws. The earlier analysis
  said it throws; consumers must check the returned array.
- A minimal combined-engine execution bridge
  (`apps/backend/src/adapters/combinedRandomizerBridge.ts`,
  `executeCombinedGeneration`) now chains all five previously independent
  pieces into one internal path: typed request → pure config mapping → the
  vendored JS core (`normalizeConfig`/`validateConfig`/`generate`/
  `writeOutput`) → the pure workspace planner → the `DsrDataTool` executor
  (`scan` when the workspace has no catalog, then `patch-enemies`). It
  returns a typed success carrying the package directory, placements path,
  `patch-manifest.json` path, upstream version, placement hash, and seed, or
  one of fifteen documented failure codes (`CATALOG_SOURCE_MISMATCH` added
  by Task 6). Only `GENERATION_FAILED` is
  marked retryable. See `docs/COMBINED_ENGINE_INTEGRATION.md` ("Execution
  bridge proof") for the exact data flow.
- Generation is proven workspace-only against fixtures: 19 focused tests run
  the real vendored `generate()`/`writeOutput()` in a temporary workspace
  with a synthetic schema-17 catalog, faking only the `DsrDataTool` process
  layer. They assert a real package is produced with
  `dataStatus: "extracted"`, that the process layer receives the planner's
  exact `scan`/`patch-enemies` argument vectors and nothing else, that the
  placements file exists before `patch-enemies` is invoked, that a fixture
  game directory is byte-identical before and after a successful run, and
  that the same request twice yields the same placement hash. Backend tests
  54/54 pass; backend build and the full workspace typecheck are clean
  (2026-09-28).
- The bridge rejects `dataStatus !== "extracted"` before writing any file and
  before spawning any process, so integration Blocker 1's silent prototype
  fallback can no longer reach a package directory. It also pre-checks
  `catalog.schemaVersion !== 17` so an obsolete catalog reports
  `CATALOG_STALE` instead of an opaque generation error.
- The bridge plans the workspace from the **normalized** upstream seed rather
  than the raw request seed, because upstream derives its own
  `seed_<safeSeed>` package directory from the normalized value; planning
  from the raw value would point `--placements` at a file upstream never
  wrote. See `docs/DECISIONS.md`.
- Three upstream config keys are pinned by the bridge rather than inherited
  from `defaultConfig`: `generateSpoilerLog: false` (no request field exists —
  Blocker 3 — and the upstream default `true` would emit an unrequested
  spoiler file), plus `useExtractedData: true` and `dryRun: false`, both
  verified inert at the pinned commit (read nowhere in the vendored core or
  `DsrDataTool`).
- Fixed: `core/pipeline.ts`'s combined-engine probe pointed at
  `vendor/src/cli.js` and `vendor/DSR-Randomizer.exe`, neither of which
  exists or should, so engine status always reported the combined engine as
  missing. It now probes `vendor/src/core/generator.js` and the built
  `vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll`, with a test
  asserting the combined engine reports `source` mode. The bridge itself
  never consults `ExternalAdapter`.
- Still not done: the bridge has never executed the real `DsrDataTool` against
  a real Dark Souls installation, so a genuine `scan`/`patch-enemies` round trip is
  unproven. Blockers 2, 3, and 6 were resolved by Task 6 (see
  `docs/COMBINED_ENGINE_INTEGRATION.md`, "Blocker resolution"); the public
  GPL fork remains open.
