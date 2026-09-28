# Decisions

Architectural decisions that are not fully derivable from reading the code
alone. Keep entries short; link to the analysis doc that has the detail.

## Workspace containment is enforced before any upstream call, not only by upstream itself

Date: 2026-09-27

`kellernz1/ds-randomizer`'s `patch-enemies` already refuses to write
outside a caller-supplied output directory into the live game directory
(`docs/COMBINED_ENGINE_INTEGRATION.md`, "Workspace execution proof"). We do
not rely on that upstream guard alone. `planCombinedRandomizerWorkspace()`
(`apps/backend/src/adapters/combinedRandomizerWorkspace.ts`) independently
rejects a workspace path equal to, or nested inside, the game directory in
pure TypeScript, before any path is ever handed to a subprocess.

Why: defense in depth for a irreversible-if-wrong operation (writing into
a user's live game install), and it keeps the safety check testable without
the upstream binary present. Consistent with the workspace policy in
`AGENTS.md` ("Avoid modifying the live DSR installation during
intermediate generation").

This module never constructs `install`/`restore` argument vectors at all —
those commands are the only upstream commands that write into `--game`,
and are intentionally not called from generation.

## Vendor upstream selectively, and build with a local SDK outside the repo path

Date: 2026-09-27

Two related decisions made while vendoring `kellernz1/ds-randomizer` and
building `DsrDataTool.dll` (`docs/COMBINED_ENGINE_INTEGRATION.md`,
"Vendored build proof"):

1. **Selective vendor, not a full mirror.** Only the files required to
   build `DsrDataTool.dll` were copied into `engines/combined/vendor/`
   (recorded with their exact pinned commit in
   `engines/combined/vendor/UPSTREAM_PIN.md`). The upstream JS core is
   analyzed but not copied in until the task that actually imports it.
   Why: avoids vendoring code this repository does not yet build or
   import, per `AGENTS.md`'s "do not implement future tasks in advance."
   `.gitignore`'s `engines/*/vendor/` blanket rule was narrowed to only
   ignore `.tools/`, `.tmp/`, `bin/`, `obj/` under vendor directories, so
   vendored source itself is actually committable — the previous rule
   would have silently discarded it.

2. **Local .NET 8 SDK installed outside the repository tree
   (`C:\Users\<user>\dn8sdk`), not at upstream's own
   `<project>/.tools/dotnet` convention.** Installing to
   `engines/combined/vendor/.tools/dotnet` failed with
   `DirectoryNotFoundException` while extracting the SDK archive — a
   ~245-character path under that location exceeded Windows' legacy
   260-character `MAX_PATH` limit on this machine. Enabling long paths
   requires an administrator-level registry change
   (`HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled`),
   which was out of scope for a vendoring task and affects the whole
   machine, not just this repository. Installing the SDK to a short path
   outside the repo sidesteps this without any system-wide change; it is
   a build-tool location, not a vendored or committed file, and any
   contributor/agent hitting the same failure should do the same rather
   than request the registry change.

## The vendored JS core is loaded by computed dynamic `import()`, and includes `writeOutput`

Date: 2026-09-28

Three choices made while vendoring the upstream JavaScript generation core
(`docs/COMBINED_ENGINE_INTEGRATION.md`, "JS core vendor proof"):

1. **`writeOutput` is part of the vendored boundary, not just `generate`.**
   `generate()` alone is the pure core, but `patch-enemies --placements`
   consumes `seed_<safeSeed>/randomizer.json` — exactly the layout
   `core/output.js` writes and the layout the committed workspace planner
   already encodes. Reimplementing it would fork that layout in two places
   with nothing gained. `core/rng.js` and `data/prototype-data.js` come
   along only because `generator.js` statically imports them.
   `data/catalog.js` and `game/detection.js` stay out: catalog loading and
   staleness verification are a separate, still-open concern.

2. **Loading uses a runtime-computed dynamic `import()` of a `file:` URL,
   with types hand-written in the backend.** A static TypeScript import
   cannot reach the vendor tree — it sits outside the backend's `rootDir`,
   so it either drags a file outside the program root into compilation
   (TS6059) or fails to resolve as typed (TS2307). `pathToFileURL()` is
   mandatory rather than cosmetic: a bare Windows `C:\…` path is not a
   valid ESM specifier (`ERR_UNSUPPORTED_ESM_URL_SCHEME`). Types live in
   `apps/backend/src/adapters/combinedRandomizerCore.ts` instead of `.d.ts`
   files beside the vendored code, so vendored bytes stay byte-identical to
   the pinned commit and a pin update is a pure file replacement.

3. **Vendor identity is verified by Git blob object ID, not by `diff`.**
   `core.autocrlf` is enabled on the development machine, so vendored files
   are checked out with CRLF while upstream stores LF — a raw byte
   comparison against a fresh clone reports false mismatches on Windows.
   Blob object IDs are computed on the normalized content this repository
   actually stores and distributes, which is what the GPL "corresponding
   source" obligation is about. The IDs are recorded in
   `engines/combined/vendor/UPSTREAM_PIN.md` so a future pin update has a
   fixed baseline. A `.gitattributes` override was considered and rejected:
   the stored blobs already match upstream exactly, so it would only change
   working-copy bytes while adding churn to another task's files.

A fourth, smaller consequence: `engines/combined/vendor/package.json` is
**repository-authored, not vendored**. The vendored `.js` files are ES
modules and the nearest enclosing manifest is this repository's root
`package.json`, which is not `"type": "module"`, so Node would reparse each
file (`MODULE_TYPELESS_PACKAGE_JSON`). Upstream's own `package.json` is not
vendored because it carries Electron/`electron-builder` configuration that
is out of scope; only the `"type": "module"` declaration is reproduced.

## The workspace is planned from the normalized upstream seed, and the package layout is asserted

Date: 2026-09-28

`executeCombinedGeneration()`
(`apps/backend/src/adapters/combinedRandomizerBridge.ts`) calls
`normalizeConfig` **before** `planCombinedRandomizerWorkspace()`, and plans
with `config.seed` rather than `request.combinedSeed`
(`docs/COMBINED_ENGINE_INTEGRATION.md`, "Execution bridge proof").

Why: two independent pieces of code derive the same `seed_<...>` directory
name. The planner applies `sanitizeUpstreamSeed()` to the seed it is given;
upstream `writeOutput` applies its own `safeSeed()` to `result.seed`, which
`normalizeConfig` has already trimmed and string-coerced. Feeding the planner
the raw request seed makes the two disagree whenever normalization changes
anything — `"  abc  "` plans `seed__abc__` while upstream writes `seed_abc` —
and the `patch-enemies --placements` argument then points at a file that was
never written. The process fails with an upstream error message that describes
a missing file rather than the actual cause.

Normalizing first makes the planner's input identical to upstream's, so the
two derivations cannot diverge. As a backstop against a future pin changing
`safeSeed`, the directory `writeOutput` returns is compared against the
planned one and a mismatch is reported as `OUTPUT_LAYOUT_MISMATCH` instead of
being silently carried into the patch step.

The alternative — dropping `sanitizeUpstreamSeed()` and using whatever
directory `writeOutput` returns — was rejected: the planner must produce the
complete `patch-enemies` argument vector while staying pure and
filesystem-free, which it cannot do if one of its paths is only knowable after
an upstream async write.

A related consequence: the bridge does **not** normalize the seed into
upstream's `/^[\w.-]{1,64}$/` charset. A non-compliant seed is reported as
`INVALID_CONFIG` and left to the boundary that produces `combinedSeed`
(integration Blocker 2). Trimming is upstream's own documented normalization;
rewriting a user's seed into a different value is a product decision about
reproducibility, and hiding it inside the execution bridge would make the
recorded manifest seed differ from the seed the user typed.

## The combined-engine seed is a named SHA-256 derivation of the master seed

Date: 2026-09-28

Status: **accepted** (user confirmed option A, 2026-09-28). Supersedes the
earlier direction in `docs/ARCHITECTURE.md` § Master seed and
`docs/source-analysis.md` of passing the normalized master seed directly to
the combined engine; the rejected alternative was keeping pass-through and
restricting master seeds to upstream's ASCII charset.

`deriveSeeds()` (`apps/backend/src/core/seeds.ts`) now derives `combined` as
the first 32 lowercase hex characters of
`SHA-256("ds1-triple-randomizer:v1:<trimmed master seed>:combined")` instead
of passing the trimmed master seed through. Resolves integration Blocker 2.

Why: upstream `validateConfig` accepts only `/^[\w.-]{1,64}$/`, while the
master seed is free text at the API boundary. The two alternatives were
tightening master-seed validation to upstream's charset, or deriving a
compliant token. Derivation was chosen because `AGENTS.md`'s seed policy
already prescribes stable named SHA-256 derivation per engine, it matches how
the Fog seed was already derived, and it keeps the master seed free text
without coupling the user-facing seed format to one engine's charset. 32 hex
characters (128 bits) satisfy upstream for every input and keep the
`seed_<token>` package directory short under the Windows `MAX_PATH` limit.

Consequences:

- The upstream `seed` recorded in a generated package's `config.json` is the
  derived token, not the typed master seed; the run manifest must record both
  (as `AGENTS.md` already requires). A run of standalone upstream
  `ds-randomizer` with the same typed seed does not reproduce our layout —
  that was never a goal.
- No compatibility break: the combined adapter still returns
  `NOT_IMPLEMENTED`, so no run has ever been produced with the old value.
- The Fog derivation is byte-identical to before.
- The execution bridge still never rewrites seeds; a non-compliant
  `combinedSeed` handed to it directly still reports `INVALID_CONFIG`.

## A reused catalog that does not match the game directory is a hard failure, never an automatic rescan

Date: 2026-09-28

Before generating, `executeCombinedGeneration()` verifies a **reused**
workspace catalog against `request.gamePath`
(`apps/backend/src/adapters/combinedRandomizerCatalog.ts`): every
`sourceFiles` entry recorded by `DsrDataTool scan` must resolve inside the
game directory and match by size and SHA-256. Any mismatch — or a catalog
recording no sources at all — returns non-retryable
`CATALOG_SOURCE_MISMATCH` with per-file `catalogProblems`. Resolves
integration Blocker 6 at the orchestration layer.

Why not rescan automatically: the most likely cause of a mismatch is a game
directory that has since been modified (e.g. a previous randomized package is
active). Rescanning it would silently capture modified data as the new
baseline. The user must restore clean files, or explicitly request a rescan
(`forceCatalogRescan`) if the directory really is the intended clean install.

`lastWriteTimeUtc` is ignored because copying or restoring a clean install
changes it without changing content. A catalog this run just scanned is not
re-verified.

Limit: this proves the catalog matches the directory, not that the directory
is vanilla. Pristine provenance stays with the backend's pristine
import/backup authority. `patch-enemies` independently re-checks the hashes
of the files it touches; this check exists to fail early and explain why.

## FogHost compiles pinned FogMod engine sources with a minimal patch set, behind a file-based JSON boundary

Date: 2026-09-28

Fog runs in a dedicated console host, `tools/fog-host` (`FogHost.exe
generate --request <json> --response <json>`). The host compiles FogMod's
engine sources at the audited commit `28711c3f…e526` directly.
(**Superseded pin:** FogHost uses `6e6dc78`; see "FogHost v1" below.) It never
uses `Program.cs` (its CLI path forces `FromGame.DS3`), `MainForm*` or
WinForms. It forces `FromGame.DS1R`, sets the CWD to a supplied FogMod data
root, points FogMod's `gameDir` at a fresh per-attempt staging directory
(DS1 `Write()` ignores `outDir` and writes in place), redirects FogMod's
console output to a log, and returns typed, retry-scoped errors. Full
contract: `docs/FOGHOST_INTEGRATION.md`.

Stock FogMod is lossy on combined output. It replaces EMEVD with its own
templates and carries over only hardcoded legacy-Enemy-Randomizer event IDs,
it deletes non-vanilla `NpcParam` rows (the combined engine's scaled rows),
and it finds key items only in annotated item lots. FogHost therefore
carries a patch set **confined to `GameDataWriter.cs` and `ItemReader.cs`**,
driven by the request's `compatibility` object. Task 11 defines the exact
inputs and rules. Modified files carry Apache-2.0 §4(b) change notices.

Why: the alternatives were running Fog before the combined engine (the
combined key-item logic would ignore the fog graph, which breaks progression
safety) and reconstructing combined changes after an unmodified Fog run (a
3-way merge on binary formats outside any engine, which is more fragile than
a small patch at the exact carry-over points). `AGENTS.md` already forbids
rewriting FogMod and prefers a small dedicated C# host.

Consequences: the fog probe in `apps/backend/src/core/pipeline.ts` and
`engines/fog/README.md` are superseded (replaced in Task 10). FogMod's
`dist/` data is game-derived, and its distribution is an open licensing
question. Building the host is not yet assigned to any task.

## Combined -> Fog bridge: three-way EMEVD merge, preserved NpcParam rows, composed scaling, backend-owned retry table

Date: 2026-09-28

FogHost request/response schema is now v2. Its `compatibility` object
carries three things:

- the combined MSB/EMEVD/GameParam files with their base and output
  hashes;
- a pristine merge-base directory;
- the combined engine's scaled NpcParam rows, each with the vanilla row it
  stands in for.

The host patch set (`GameDataWriter.cs` only; `ItemReader.cs` stays stock):

- merges EMEVD three ways (base / combined / FogMod output): combined
  custom events and event-0 `2000[00]` registrations are carried over, and
  combined edits to vanilla events win where FogMod left the event
  unchanged;
- keeps the preserved NpcParam rows and scales them like their base row;
- checks post-write invariants.

Both-sides event edits are `FOG_MERGE_CONFLICT`. Full rules:
`docs/FOGHOST_INTEGRATION.md` ("Compatibility bridge").

The decisions:

- **Scaling composes; no option combination is rejected (hypothesis on
  the FogMod side).** This corrects the Task 9 "double-scales" note.
  `DsrDataTool` scaling (`area` and `progressive` are identical there)
  normalizes a replacement to the destination slot's vanilla strength. That
  should be what FogMod's `scale` expects before it re-tiers by fog
  position, but FogMod's scaling code has not been read. This must be
  confirmed when the host is built (`FOGHOST_INTEGRATION.md` open item 8);
  the fallback is to reject the combination or to pick one owner.
- **Retry scope comes from a backend table, not from the host.**
  `FOG_SEED_UNSOLVABLE` retries Fog. `FOG_UPSTREAM_INCOMPATIBLE` retries
  the combined stage and resets the Fog attempt. Merge conflicts and
  invariant violations never retry: custom event IDs are chosen from the
  base EMEVD, so a new combined seed cannot remove a structural conflict.
- **Retry seeds**: `deriveAttemptSeeds`. Attempt 0 equals `deriveSeeds`, so
  existing seeds are unchanged; later attempts use the named streams
  `combined-retry:<n>` and `fog-retry:<n>`.

Why not the alternatives:

- Rejecting Fog scaling together with combined scaling would have broken
  the desktop default configuration without a real conflict.
- Letting the host decide retry scope would spread the policy across two
  languages.

Consequence: boss randomization together with Fog stays unproven until the
host exists and the both-sides conflict set (e.g. `11810310`) is measured.

## One backend-owned activation/restore authority with a hash-verified pristine snapshot and a pre-write journal

Date: 2026-09-28

`apps/backend/src/core/gameState.ts` (`GameStateAuthority`) is the only code
that writes into a live DSR installation. Its state lives in a backend
directory outside the game (`DS1TR_STATE_DIR`, default
`%LOCALAPPDATA%\ds1-triple-randomizer\state\installs\<path hash>`):

- **Pristine snapshot.** `importPristine` copies `DarkSoulsRemastered.exe`
  and every file under the managed roots (`event`, `map/MapStudio`, `msg`,
  `param/GameParam`, `paramdef`, `script`, `sfx`) into `pristine/`, hash-verifying each
  copy. `snapshot.json` is written last and commits the snapshot. Import is
  refused when the managed roots contain `*.bak` or our temp files (legacy
  randomizer chains). The snapshot directory is the pipeline's only trusted
  pristine source (`getPristineSource`, wired in `server.ts`). It is
  re-verified before every use. Because it is the generation source, the
  roots must cover DsrDataTool's whole read set (`paramdef` added by Task 16).
- **Package binding.** The run manifest records `pristineSnapshotId` and an
  explicit `gameTreeRoot` (`mod` for combined-only packages, `''` for the
  Fog game tree). Activation is driven only by `run-manifest.json` `files`,
  never by a directory walk. It refuses packages from another snapshot. It
  also refuses any file that fails its hash, lies outside the snapshot, or is
  a `.bak`/temp file. Combined packages must agree exactly with
  `patch-manifest.json` (outputs and snapshot `sourceSha256`). Every check
  runs before the first write.
- **State detection** (`invalid`, `no-snapshot`, `version-changed`,
  `vanilla`, `active`, `interrupted`, `modified`) hashes the snapshot file
  set against the game and the journal. Mutations re-detect under a PID lock
  file and refuse unless the state allows them. Activation requires
  `vanilla`. Restore accepts `active`/`interrupted`, is a no-op on `vanilla`,
  and refuses `version-changed` (stale snapshot). It refuses `modified`
  unless `overwriteUnrecordedChanges` is set. That override also removes
  unrecorded files in managed roots. Both refuse while the game is running.
- **Crash safety.** An `activation.json` journal (`activating` -> `active`)
  is written atomically before the first game write. Each file is copied to
  `<target>.ds1tr-tmp`, verified, then renamed over the target. A failed
  activation rolls back from the snapshot. If rollback also fails, the
  journal stays and the state reads `interrupted` until restore.

Why not reuse DsrDataTool `install`/`restore` or FogMod `.bak`: they are
independent backup authorities (`AGENTS.md` backup policy).

Limit: without reference vanilla hashes, a snapshot proves only that no
known legacy artifacts were present, not that the data is vanilla
(`provenance.referenceHashesVerified: false`). The verifier is injectable
(`verifyReferenceHashes`); no reference hash set exists yet.

Limit: state is keyed by the game path hash, and activation leaves no marker
in the game. If the library moves or the state directory is lost, an
installation with an active package reads `no-snapshot`. `importPristine`
would then accept the modded files as pristine. Reference hashes (or an
in-game marker) are needed to close this.

## FogHost v1: FogMod re-pinned to 6e6dc78; user-supplied data root; no Fog rescaling of combined rows; boss randomization + Fog rejected

Date: 2026-09-28. Implemented in `tools/fog-host` (FogHost 0.1.0), backend 0.16.0.

1. **FogMod pin `6e6dc78`, not `28711c3`.** `28711c3`'s DS3 update breaks DS1R:
   entrances are keyed `Area_ID` while DS1 code looks them up by name, and the DS1
   connector loop only exits in a DS3 branch (every seed throws). Both failures were
   observed at runtime. `6e6dc78` is the last DS1-only commit; `dist/DS1R` is identical,
   and `GameDataWriter.cs` differs only by renames. SoulsIds `e31a6fd` and
   `JuicerMV.SoulsFormats` 0.1.3 (the package `DsrDataTool` already uses, GPL-3.0-only)
   build it on .NET 8 after a six-rename API port.
2. **Data root is user-supplied and verified, never bundled or downloaded.** FogMod
   `dist/DS1R` is FromSoftware-derived. FogHost verifies the exact 59-file read set
   against an embedded SHA-256 manifest on every run (LF-normalized for `.txt`/`.xml`).
   Unexpected files in directories FogMod enumerates are rejected. A missing, altered or
   extra file is `FOG_DATA_MISSING` with the file list and the pin. The backend locates
   the root from `DS1TR_FOG_DATA_ROOT`; unset means `FOG_PREREQUISITES_MISSING`.
3. **Scaling v1: FogMod never rescales combined NpcParam rows.** Source reading showed
   FogMod scales per (NpcParam ID, area) via SpEffects, skips non-vanilla rows, and
   re-points MSB parts to clone rows for IDs used in several areas. The Task 11
   "composed scaling" rule is withdrawn. Preserved rows are kept, never restored or
   scaled, and must be byte-for-byte cell-identical after the write. Invariant 1 now
   allows a part whose input row is not preserved to move only to a FogMod scale clone
   (a new row whose `pcAttr` hint encodes the input row). Preserved IDs must not be
   vanilla rows. Randomized enemies keep the combined destination-slot normalization
   and get no fog-graph tier.
4. **Boss randomization + Fog is rejected** before any engine runs, with
   `FOG_BOSS_RANDOMIZATION_UNSUPPORTED` (`generationPipeline.ts`). It is never silently
   disabled; the manifest records the options as requested.

Why: the three product decisions were made by the user. The pin change follows from the
runtime evidence. Writes are deferred inside the patched writer, so dry runs write
nothing, and merge conflicts or invariant violations are raised before staging changes.

Measured on real-format data (`apps/backend/scripts/fogHostE2e.mjs`, live install only
read):

- Items + Fog passed 7/7 seeds. One seed needed one combined retry for a key item
  outside FogMod's annotated lots.
- **Regular-enemy randomization + Fog failed on every seed tried (5/5)** with
  `FOG_MERGE_CONFLICT` on event `11505392` (Sen's Fortress). The combined engine removes
  one `2004[17]` instruction, and FogMod rewrites the same event's `3[02]` boss-trigger
  region check at runtime. The event-level merge rule refuses this, as designed; no
  corrupt package is produced.
- One enemy seed exhausted all three combined attempts on `peculiar_doll`.

Resolving the conflict needs either an instruction-level merge (control-flow and skip
aware) or a product rule for enemies + Fog; that is an open decision.

## FogHost 0.2.0: instruction-level EMEVD merge, unannotated-collision scaling fix, bosses off by default

Date: 2026-09-28. User chose option (B) for regular enemies + Fog.

1. **Instruction-level merge for events edited by both engines.** The merge
   succeeds only when the two edits are provably independent; otherwise it
   reports `FOG_MERGE_CONFLICT`, listing every conflicting event. It requires:
   - identical rest behavior;
   - unambiguous LCS edit scripts;
   - no shared or adjacent base position;
   - no control-flow instruction (banks 1000–1999) touched by either side;
   - condition instructions (banks 0–999) changed by at most one side;
   - no line-count-changing edit within `[p+1, p+n+1]` of any DS1 skip
     instruction;
   - unchanged, re-indexable event parameters.

   Instruction classes follow the DS1 EMEDF (DarkScript3, reference only). A
   count-preserving replacement inside a skip range is allowed, because
   positions do not move. Post-write invariant 4 is unchanged: the merged
   event must be written exactly. Details: `docs/FOGHOST_INTEGRATION.md`,
   rule 3.
2. **Vendored FogMod patch 6.** FogMod's scale pass used `null` as a
   `Dictionary` key for enemies on collisions without an area annotation.
   That crashed 11 of 40 real-format enemy seeds (`FOG_ENGINE_FAILED`). A
   sentinel key keeps those groups unscaled, which is upstream's intended
   behavior.
3. **Desktop default: boss randomization off.** Fog is on by default, and
   boss randomization + Fog is rejected. Only the initial state changes
   (desktop 0.4.1). A user who turns bosses on with Fog still gets
   `FOG_BOSS_RANDOMIZATION_UNSUPPORTED`.

**Finding: the combined engine breaks skip ranges.** `DsrDataTool.RemoveEventInstruction`
shifts event parameters but never adjusts the line count of an enclosing
`SKIP` (DS1 `1000[01/03/05/07]`, `1003[01/03/05/07]`, `1005[01]`). In one
real-format package (seed `enemies-1`), 38 skip ranges in 21 events lose
lines without the count changing. The skip then covers different
instructions, and this happens in combined-only packages as well. Two
examples:

- In `11505392`, the SKIP 7 now also skips `2004[01]` (set AI state).
- In `11505011`, each SKIP 1 now skips the next condition registration.

FogHost correctly refuses to merge such events when FogMod also edits them.
On real data that is always `11505392`, and nearly always `11600400`. So
regular enemies + Fog remains 0/40 until the combined engine adjusts skip
counts. That fix belongs in the vendored `DsrDataTool`, and it is an open
user decision.

