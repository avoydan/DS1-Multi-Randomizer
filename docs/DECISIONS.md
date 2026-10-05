# Decisions

## Enemy placement: a second, explicit sampling algorithm beside a frozen permutation

Date: 2026-10-05

Chosen for the next legacy-parity group (Replacement chance, the Boss / Normal enemy / NPC modes, Boss
chance, Placement fit, Difficulty mode and strictness, Type replacement and what depends on them). Full
reasoning, the algorithm and the plan are in `docs/ENEMY_PLACEMENT_MODEL.md`. Nothing is implemented yet.

- **Two cores, one frozen.** The current count-preserving permutation becomes `permutation-v1` and is not
  edited again; every existing preset keeps it. A new slot-by-slot planner, `sampling-v1`, implements the
  group. The legacy tool is not count preserving and decides per slot, so the group cannot be expressed on a
  bijection (a boss in a normal slot needs a duplicate; a partial replacement leaves nothing to permute).
  Approaches that emulate the options around the permutation, or replace it outright, were rejected: the
  first cannot reproduce the options, the second breaks every old preset.
- **Algorithm identity is explicit and separate from product versions.** Sampling draws use their own stream
  version string (`enemy-placement-v1`), so `RANDOMIZER_VERSION` stays 0.5.66 and no existing stream moves.
  The contract carries `enemies.placement.algorithm`; presets move to version 5 and migrate versions 1-4 to
  `permutation` with the neutral value of every new option. The backend rejects non-neutral values under
  `permutation` instead of ignoring them.
- **Per-slot streams** isolate how many draws a slot consumes. Outcomes remain order dependent inside a map;
  this is not claimed to be independence.
- **Budget by construction.** DS1TR keeps counting every model present in a map (cap 30) because DsrDataTool
  prunes unused models; a coverage reserve (the vanilla model of the smallest participating slot) guarantees
  a fitting in-map type, replacing first-come luck. The prototype showed the failure it prevents (0.13% of
  placements above their size class) and the fix (0 of 68,120).
- **Fallback is bounded and ordered:** sampled, keep vanilla (the legacy outcome), in-set type that fits,
  in-set type with fit relaxed (a recorded diagnostic, expected to be unreachable), then a typed retryable
  error. No unbounded retries.
- **Classes are derived, not copied.** Size and difficulty classes come from catalog data. Against the legacy
  classes (used only as a yardstick) size agrees well (rank correlation 0.74) and difficulty only roughly
  (0.58); weights were deliberately not tuned against the legacy table.
- **Deviation D-1:** the difficulty filter stays active inside a full model set. Taken literally, the legacy
  rule drops it, which left 30-31% of full-set placements on the slot's own class against 72-77% with the
  refinement.
- **Linked and multi-body bosses stay units.** They participate as a whole and swap with other units; they
  are not sampled into normal slots.

Not decided here, listed in section 13 of the model document for a later task: whether `sampling` becomes the
default for new presets, the model-budget definition and a Blighttown exception, curated difficulty
overrides, how bosses placed in normal slots interact with Enemy scaling, the NPC and hostile-NPC allowlists,
the Easy Asylum rule, and the in-game behavior of respawning roaming bosses.

## Replace second Gargoyle follows the legacy tool; three engine values; no FogHost overlay

Date: 2026-10-05

Product decision (user): the authoritative target is the original Enemy Randomizer behavior, not the
combined engine's earlier approximation. This supersedes the Batch 1 stop for this option.

- **The Batch 1 premise was wrong.** It said FogMod rewrites events `11015396` and `11015382`, so
  supporting the legacy edits would need a new FogHost overlay. FogMod's own source says the opposite:
  both events "have no modifications in fog rando" and are copied from the game directory. FogMod's
  template for them is the vanilla form, and the normal merge (rule 3, "already identical") takes
  the combined form unchanged. **No FogHost code changed.** The 15-event boss overlay is untouched and
  a test pins its exact membership; the two events are explicitly not part of it. The comparison is
  in `docs/FOGHOST_INTEGRATION.md`.
- **Three engine values.** `replace` (legacy 1, the product default), `keep` (legacy 0) and
  `single-boss` (the earlier behavior). `single-boss` is not a legacy value; it exists so that
  older presets keep reproducing their output, and it is the vendored engine's own default so the
  Batch 1 and 2 hash locks still hold. The UI shows it as "Single boss (earlier DS1TR behavior)".
- **Product default changes default output.** With bosses on, the default Bell Gargoyle fight now has
  two independently drawn bosses instead of one. That is the legacy default and the requested target.
  Presets of versions 1 to 3 migrate to `single-boss`; presets are now version 4.
- **No forced combat activation for the replaced body.** The generic forced-activation event would set
  its AI on at map load and defeat the vanilla hold (event `11015396` switches it off and invincible
  until the first body reaches 60% health). The release uses the vanilla event plus the combined engine's
  usual wake-up rows for the bosses that need them.
- **Placement list.** The replaced body is not a catalogued boss slot, so DsrDataTool refuses it in the
  boss list (`A boss placement targets a protected slot`). Its placement is appended to the regular
  list through the linked-boss plan, which also counts its model in the per-map budget.
- **Untestable here:** whether every portable boss's AI wakes correctly from the vanilla release needs
  the game; that is the one accepted residual risk.

## Legacy-parity Batch 2: legacy product defaults, behavior-preserving engine defaults, honest scope

Date: 2026-10-05

Soul items (Shuffled and Replaced), Starting items, Pinwheel Chaos and Gwyn nerf are implemented in
the vendored core (`patches/0003-legacy-parity-batch-2.patch`, same policy as Batch 1).

- **Two sets of defaults.** The contract and the UI use the legacy defaults (soul items shuffled,
  starting items Shield + 1H/2H, Pinwheel Chaos off, Gwyn nerf Medium). The vendored engine's own
  defaults keep the old behavior (`mixed-pool`, Gwyn `none`), so the engine stays byte-identical
  without the options and the Batch 1 hash lock still holds. The backend always sends explicit
  values, so the product default wins. Consequence: with Starting equipment on, the default now
  gives shields in the left hand, and with bosses on, Lord Gwyn is withheld from the boss pool in
  60% of seeds. Presets of version 1 and 2 migrate to the old-behavior values, so they reproduce
  their old output; presets are now version 3.
- **Soul items "Transposed" is not offered.** It acts on boss souls (goods 700-711), which are in
  none of the catalog's pools (boss rewards are not extracted), so it could not change anything. It
  stays a disabled choice until the scan/patch can see boss reward lots.
- **Replaced souls use a catalog-derived pool, not the legacy list.** Reusing the unlicensed legacy
  table is excluded by project policy. The pool is the unprotected world goods that already occur in
  at least two lots. The two-lot rule exists because FogMod tracks 27 key items by id and rejects a
  second copy (`Item peculiar_doll found in both ...`); a first version of the rule without it broke
  every Fog run.
- **Gwyn nerf is a boss-candidate exclusion, not a pin.** Pinning Gwyn in place would make "Gwyn at
  the Kiln" common, the opposite of the legacy rejection. With the legacy probability Gwyn is
  removed from the candidates (the Kiln included) and another boss may appear twice.
- **Pinwheel Chaos draws clone sources with repetition**, as the legacy tool does, and replaced
  clones keep their own stats (scaling `vanilla` for those 14 placements) so they do not inherit the
  clone's 1 HP.
- **Include DLC was a dead control** (nothing reads `output.includeDlc`, and FogMod has no matching
  switch). It is now a disabled planned control and the backend rejects `false` instead of
  ignoring it. Importing a preset with `includeDlc: false` therefore fails. A real implementation
  is medium work and cannot cover Fog.

## Legacy-parity Batch 1 extends the vendored core, with legacy defaults and a stop at the Gargoyle

Date: 2026-10-05

Fashion Souls, Replace mimics, Replace respawning mosquitoes and Prevent same enemy are
implemented inside the vendored combined core (`patches/0002-legacy-parity-batch-1.patch`,
`UPSTREAM_PIN.md` "Local modifications"). They cannot be built from outside: the enemy slot
filter, the per-map model budget and the item permutation are internal to the unmodified
`generator.js`, and AGENTS.md requires missing legacy behavior to be implemented in the GPL
combined fork. The earlier policy allowed only "required correctness fixes"; this extends it
to parity features and is recorded here.

- **Defaults are the legacy defaults, which equal the old engine behavior.** Fashion Souls
  on, mimics on, mosquitoes on, prevent-same off. With every option at its default, placement
  hashes were identical over 24 seeds in three configurations, so earlier sweep evidence
  still applies. New code consumes no rng and reorders nothing at the defaults.
- **`RANDOMIZER_VERSION` stays `0.5.66`.** It salts every seed stream; changing it would
  reshuffle every seed and fail the pin assertion.
- **Presets move to version 2.** Strict validation makes the new keys required. Version 1
  presets are migrated with the legacy defaults, which reproduce their output.
- **Pool coupling for mimics and mosquitoes.** The combined engine permutes sources among
  slots, so a slot left in place is also a source left out of the pool. The legacy tool
  chooses replacements from its own catalog. We keep the permutation model and document the
  difference.
- **Fashion Souls off exchanges whole sets between set locations** (runs of consecutive
  item-lot rows in one map) instead of placing a set at any location with spare capacity.
  Expressing "together" at an arbitrary location would need multi-entry lots in DsrDataTool.
- **Replace second Gargoyle is not implemented.** Legacy mode 1 replaces events `11015382`
  and `11015396`, which FogMod also rewrites and which are outside the 15-event overlay
  whitelist. The engine's linked-unit design matches neither legacy value, and the legacy
  default would change default Bosses + Fog output. A product decision and event measurement
  are needed first (`docs/FEATURE_PARITY.md`, section 6a). The whitelist was not broadened.

## Settings presets are a backend-validated JSON document, not the legacy config string

Date: 2026-10-05

DS1TR exports and imports settings as a versioned JSON document (`ds1tr-preset`, version 1)
covering Items, Enemies, Fog and output options, without the game path. Both directions
pass through `validateRandomizerConfig`, so a preset can never carry an unsupported or
unsafe option. The Enemy Randomizer's `/-/` config string is not imported: its semantics
(per-slot probabilities) do not match the combined engine, and the legacy "copy enemy
placement" workflow is superseded by the deterministic master seed plus a preset. The
full option audit is in `docs/FEATURE_PARITY.md`.

## The Windows desktop ships a portable Electron payload with a separate packaged runtime

Date: 2026-09-29

The desktop package carries the compiled backend, the vendored combined JavaScript core,
and framework-dependent .NET 8 builds of DsrDataTool and FogHost in `resources/runtime`.
Electron launches the backend in Node mode on a random loopback port with a per-session
token. Electron's user-data directory holds mutable state. FogMod's game-derived DS1R
data remains outside the package and is selected by the user. See
`docs/DESKTOP_PACKAGING.md`.

Why: the desktop needs one process authority for generation and activation, while the
renderer stays isolated from game files. A framework-dependent build can be produced
from the available SDK and cached dependencies; self-contained Windows runtime packs
were unavailable in this build environment. .NET 8 runtime is therefore an explicit
installation prerequisite.

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

## DsrDataTool keeps DS1 skip counts consistent (local vendored modification)

Date: 2026-09-28. User-authorized correctness fix to the combined engine, not a Fog workaround.

- **Removals and insertions.** `RemoveEventInstruction` and `InsertEventInstruction` now
  adjust every enclosing DS1 "skip N lines" instruction (`1000[01/03/05/07]`,
  `1003[01/03/05/07]`, `1005[01]`) through the new `EventSkipRanges.cs`:
  - a removal inside a range shrinks it;
  - an insertion grows every skip that covers its **anchor**. Every call site now states
    an anchor: boss health-bar companions use `After` the health bar, and the Asylum AI
    re-enable uses `Before` the terminal instruction. A position alone is ambiguous at a
    range's last line.
- **Unchanged cases.** Same-length replacements (in-place argument edits) and edits
  outside a range leave skips unchanged. Nested and overlapping skips are each adjusted.
- **Refusals.** The tool throws if an edit would:
  - remove a skip;
  - insert right after a skip instruction;
  - change a parameterized count;
  - overflow a count.
- **Documentation.** The modification is documented in
  `engines/combined/vendor/UPSTREAM_PIN.md` ("Local modifications"), with a GPL-3.0 §5(a)
  notice in `Program.cs` and a patch in `vendor/patches/`. That document previously
  forbade in-place edits; they are now allowed only as listed and patched until the
  public GPL fork exists. Backend 0.16.1, because combined packages change for the same
  seed.
- **Verification** (`engines/combined/tests/DsrDataTool.Tests`):
  - 14 unit tests, including the `11505392` and `11600400` cases;
  - `verify-skips`, which checks that every surviving base skip covers exactly its
    original surviving instructions. It flags the unpatched tool's output.
  - Combined-only 40-seed sweep with bosses: 40/40 packages, 0 mismatches, about 134
    skips adjusted per package.

**Effect on Fog (measured, merge rules unchanged).** Items + Enemies + Fog is still 0/40:

- `11505392`: the combined engine now changes SKIP 7 to 6, and merge rule 4 refuses any
  control-flow change. This happens in 40 of 40 seeds.
- `11600400`: the corrected skip at base 2 is adjacent to FogMod's edits at 0–1, so the
  adjacency rule refuses it. This happens in 39 of 40 seeds.

Allowing a skip-count change that is exactly explained by the same side's own
count-changing edits in its range would be a change to rule 4. That is an open user
decision; the rules were not changed.

## FogHost 0.3.0: derived SKIP-count adjustments are not control-flow edits; bridge reads nested placements

Date: 2026-09-29. User-approved narrowing of merge rules 3 (overlap/adjacency) and 4
(control flow). Details: `docs/FOGHOST_INTEGRATION.md`, "Derived skip-count adjustment".

- **Alignment.** Skips are aligned ignoring only their line-count byte.
- **When a count change is accepted.** A skip whose count changed is a derived
  structural adjustment only if all of these hold:
  - opcode, other arguments and layer are unchanged;
  - every same-side edit touching the range lies wholly inside it;
  - the adjusted skip covers exactly the surviving base lines plus the side's own
    inserted lines, which also proves the count delta;
  - the other side does not edit `[p+1, p+n+1]` or the skip;
  - the alignment is unambiguous;
  - the count is not taken from an event parameter.
- **What the exception covers.** Only the hunks accounted for by such an adjustment are
  exempt from rules 3 and 6. Every other skip change, and every other control-flow edit,
  is still `FOG_MERGE_CONFLICT`.
- **Bridge bug fixed (backend 0.16.2).** `buildFogCompatibility` read `enemies`/`bosses`
  at the top level of `randomizer.json`, but they live under `placements`. So
  `preservedNpcParams` was always empty in production, and FogMod restored vanilla
  NpcParam IDs over the combined engine's scaled rows. Top-level lists are now rejected
  rather than silently yielding nothing.
- **Invariant 1 false positive fixed.** DS1 human enemies use `c0000`, declared as a
  Player model; the check only looked at Enemy models, which also fails on vanilla data.
  A model must now keep every declaration category it had in the input, and must be
  declared somewhere.

**Result on real-format data** (40 seeds, Items + Enemies + Fog):

- 40/40 succeed, with post-write invariants passing on all;
- `11505392` and `11500001` merge at instruction level in 40 seeds, `11600400` in 39;
- no conflicts remain;
- combined packages pass `verify-skips` 47/47;
- final Fog packages, checked against FogMod's own templates, have 0 skip mismatches in
  any event the combined engine or the merge touched.

**Upstream FogMod observation (fixed by FogHost 0.3.1; see the next entry).** In every seed, 11
boss-trigger events that only FogMod edits end up with skip ranges covering different
instructions: `11015393`, `11205383`, `11215002/3/12/13`, `11405393`, `11415343/383`,
`11505393`, `11515393`. This comes from FogMod's runtime boss-trigger rewrite, which
replaces a `3[02]` with several instructions without adjusting the enclosing skip.
Stock FogMod does the same, so it is pre-existing FogMod behavior.

## FogHost 0.3.1: FogMod's boss-trigger rewrite keeps enclosing SKIP counts consistent

Date: 2026-09-29. Correctness fix in the vendored FogMod engine (patch 7), not a merge
workaround.

- **Root cause.** `GameDataWriter.cs` replaces one `IF In/Outside Area` (`3[02]`) in
  place with `1 + (1 + alternates)` instructions (region checks plus an
  `IF Condition Group`). Enclosing DS1 "skip N lines" instructions were not adjusted, so
  they covered different instructions.
- **Fix.** One call, `FogHost.SkipRanges.BeforeReplace(evt, index, inserted)`, runs
  before the rewrite. It grows every skip covering the replaced instruction by the net
  line change, which is the same principle as the combined engine's `EventSkipRanges`.
  The replacement sits exactly where the covered original was, so the adjustment is
  unambiguous. It refuses, with `FOG_ENGINE_FAILED`:
  - a control-flow target;
  - a parameterized skip count;
  - an overflowing count.
- **Scope.** No other FogMod behavior changed. FogMod's other count-changing edits are
  appends after the end of event 0 or whole-event copies. The Asylum `11810310`
  `RemoveAll` runs only for boss randomization + Fog, which v1 rejects, and is left
  unchanged.
- **Verification:**
  - 5 new focused FogHost tests (inside a range, last covered line, outside, nested,
    refusals);
  - each of the 11 events checked explicitly: all present, 0 mismatches;
  - 40-seed sweeps: Items + Enemies + Fog 40/40 and Items + Fog 40/40, with no merge
    conflicts and 0 skip mismatches in combined packages and in final Fog packages
    (against FogMod's templates, combined-edited and FogMod-only events alike). The
    key-item retry budget is unchanged.
- **Documentation.** `tools/fog-host/vendor/UPSTREAM_PIN.md` (patch 7), with the
  change notice in `GameDataWriter.cs`.

## Boss randomization + Fog: a whitelisted boss-event overlay on FogMod's event (FogHost 0.5.0)

Date: 2026-09-29. User-approved. Boss randomization + Fog is now supported; the
`FOG_BOSS_RANDOMIZATION_UNSUPPORTED` rejection is removed (backend 0.17.0), and the
desktop default for bosses is on again (desktop 0.5.0).

Measured (Items + Enemies + Bosses + Fog, real-format data): 15 boss events are edited by
both engines in every seed, and the normal instruction merge cannot combine them. FogMod
changes their control flow: it removes host-only gates (`4[03]` ×2 + `1000[02]`), shortens
WAITs, and disables vanilla events with END. Its edits are also adjacent to or overlap the
combined engine's.

**Decision.** For those events only (explicit ID whitelist in
`tools/fog-host/src/BossOverlay.cs`), and only after the normal merge refuses, FogMod's
event is the control-flow and world-logic base. The combined engine's boss mutations are
re-applied by content, and only these operations are allowed:

1. replace a boss health bar (`2003[11]`, same enable/entity/slot, new name) whose
   original instruction is uniquely present;
2. remove an instruction kind the combined engine removes for replaced enemies (its
   model-specific, portable-boss and static-lifecycle lists: `2003[18]`, `2004[1,5,8,9,10,12,
   13,15,17,21,22,26,27,28,29,30,31,32,35,36,40,41,42]`), matched exactly. If the base had
   several identical copies, all of them must be removed;
3. insert boss-support instructions (`2004[01]` AI state, `2004[20]` AI re-plan, `2003[18]`
   force animation) after a unique content anchor: the nearest preceding combined-side
   instruction present once in FogMod's event. Anything skipped on the way must be an
   absent command, never control flow or a condition;
4. a mutation FogMod already made (same instruction removed, same instruction present at
   the insertion point) is satisfied, never applied twice.

**Guards.**

- An interpretation of the combined edit is valid only if its operations, applied to the
  base event, reproduce the combined event exactly, skip counts included.
- All valid interpretations must give the identical result.
- Every insertion and deletion adjusts enclosing skips and event parameters
  (`SkipRanges`).
- The overlay fails with `FOG_MERGE_CONFLICT` if:
  - any other mutation is needed;
  - an anchor or health bar is missing or ambiguous;
  - FogMod has extra copies of a removed instruction;
  - an inserted or replaced instruction would be unreachable (static control-flow check);
  - a support instruction would be duplicated;
  - FogMod's control-flow and condition skeleton would change.
- The overlaid event must then be written exactly (invariant 4).

The normal instruction merge is unchanged for every other event.

Why: FogMod's boss-event rewrites are deliberate world logic (who can start a fight, and
which events FogMod replaces). The combined engine's boss edits are a small, known set of
command-level mutations. Re-applying those by content keeps both intents without
position-based merging across FogMod's control-flow changes.

Also: the Asylum Demon `RemoveAll` in FogMod goes through `SkipRanges.Remove` (patch 8).
Merge refinements, each provable: ambiguous alignments with an identical result, the skip
range defined as `[p+1, p+n]`, and event-0 initialization removal.
