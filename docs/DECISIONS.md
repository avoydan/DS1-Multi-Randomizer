# Decisions

## Owner-approved roaming-boss fit is scoped to generation profile v4

Date: 2026-10-15

The updated fit classifications and Pinwheel Chaos roaming-candidate behavior change generated enemy placements. They therefore belong to `ds1tr-gen-v4`; v1, v2, and v3 explicitly select the pre-change candidate exclusions and fit classes. This preserves all three existing profile contracts, including local v3 Seed Codes, while making the owner-approved rules the behavior for new worlds. V4 inherits v3's Compact Fog runtime and data contract. The `RANDOMIZER_VERSION` salt and canonical user settings stay unchanged; the profile supplies the internal policy.

Three existing v2 golden outputs changed when the owner-approved rules were first applied globally. The change was not released. The profile-aware behavior restores those golden outputs and records the new v4 results in a separate fixture file; no existing golden is modified. Intermediate unreleased worlds created under the transient global behavior while labeled v3 cannot be inferred from their Seed Codes and must be recreated under v4 if their exact output matters.

## Compact Fog uses a new offline generation profile

Date: 2026-10-14

Compact Fog was introduced for new runs in profile v3. Its 58-file world-data set is assembled from verified DSR files and deterministic local reconstruction; it has no upstream annotation files and does not read or package `fog.txt`. Profiles v1/v2 retain their original 59-file data manifest and frozen item-tracking rules; v4 inherits the exact v3 Compact Fog contract.

The Compact catalog contains 22 key-item identities, but only its 9 `required` items constrain backend item placement before Fog runs. `RandomOnly` and `Hard` entries remain in the Fog catalog but are omitted from that backend-required set. The catalog has 13 RandomOnly and no Hard entries; FogMod's reader excludes RandomOnly entries from its item hash and handles their multiple source lots as optional, while Hard entries remain controlled by Fog's hard-logic option. Applying the old `required:false => shop-only` backend rule made a valid full run fail for Eye of Death when its source was a world lot or enemy drop. This policy was introduced in `ds1tr-gen-v3` and is inherited by v4; v1/v2 behavior and fixtures remain frozen.

The v3 generation profile pins FogHost 0.6.0, the Compact catalog fingerprint, offline world-data identity, and Compact item tracking. V4 inherits these values while adding its independent roaming-boss fit policy. The original Compact change was implemented under desktop 0.39.0; the later profile-scoping patch is prepared as desktop 0.39.2.

## Nexus packages do not download legacy Fog data

Date: 2026-10-14

The Nexus build receives its immutable distribution from packaged build metadata and passes it to the backend at process start. Only the explicit `github` distribution may download missing upstream Fog inputs. Nexus reuses a complete, hash-verified v1/v2 legacy cache when present; if that cache is cold or invalid, the request fails with a localized explanation and leaves the cache untouched. It never substitutes Compact v3 data for an older Seed Code. V3 remains fully local through its 58-input reconstruction path. This preserves old-profile behavior while honoring the Nexus no-download requirement; cold Nexus installs cannot run v1/v2 Fog codes unless a valid legacy cache already exists.

## Compact Fog catalog reuses the typed adapter and fails closed until complete

Date: 2026-10-13 (development decision; superseded 2026-10-14)

Compact Full Fog V1 compiles pristine DSR facts and project-owned rules into the existing `FogAnnotationCatalog`; it does not introduce another runtime DTO architecture or use `fog.txt` as a source. This was the gate while category coverage was incomplete. The catalog and production gates have since passed; the current runtime decision is recorded above.

## Free cursor is a local launcher preference with an owned Windows watcher

Date: 2026-10-12

The free-cursor option is stored only in Electron's `userData/settings.json`; it is never added to the world configuration, generation profiles, presets, or Seed Codes. A separate self-contained .NET Windows helper is started only while the option is enabled. It verifies that the visible foreground window belongs to the `DarkSoulsRemastered.exe` process and checks system cursor visibility before removing a clip. It polls at 40 ms because the game may reapply its clip rectangle. On a verified DSR focus loss, it releases clipping once even if the cursor was hidden during normal gameplay, then rechecks the foreground to reduce the unavoidable race from the global `ClipCursor` API. The renderer controls the helper through narrow main-process IPC; the app stops it on disable and shutdown. It does not inject code, patch the game, change saves, or require elevation.

## Roaming-boss safety changes use a new generation profile

Date: 2026-10-12

The roaming-boss modes were already available in public prerelease 0.38.4, so changing their output under `ds1tr-gen-v1` would break the published Seed Code contract. New runs use `ds1tr-gen-v2`; v1 explicitly retains its protected destination set. V2 enables the four audited safe destination handlers and avoids making a discarded normal assignment before `both` at 100% boss chance. The Seed Code envelope and format stay the same, and the combined engine's 0.5.66 seed-stream salt stays pinned. Profile compatibility is selected by a validated internal engine config field, not a user setting. Evidence and frozen fixtures are recorded in `docs/GENERATION_COMPATIBILITY.md` and `docs/BOSSES_ONLY_AUDIT.md`.

## Distribution is an immutable build capability, independent of update channel

Date: 2026-10-10

Represent `distribution: github | nexus` separately from `channel: public | internal` in packaged build metadata. GitHub remains the default for schema-valid legacy metadata with no distribution field. Missing or malformed metadata and an explicitly invalid distribution fail closed: the app reports updates unavailable and does not load updater code.

Only a positively identified GitHub distribution dynamically loads `electron-updater`, the GitHub source, and the no-stable-release API confirmation helper, then configures the existing channel policy and schedules its checks. Nexus uses a local inert IPC implementation, keeps the user's saved `autoCheck` preference untouched, and exposes no update controls. An optional Nexus page URL is build-time configuration validated against the official HTTPS Nexus host and DSR mod-page path; unset means no button.

The variants share `appId`, product metadata, user-data identity, runtime, and source tree. Nexus packaging clears electron-builder's publish configuration and uses separate local artifact names and output path; the GitHub artifact contract stays unchanged. This gate covers application updates only. The Fog data downloader remains a separate subsystem and is unchanged.

## GitHub prerelease updates are limited to the internal build channel

Date: 2026-10-09

The updater remains stable-only in every public build (`allowPrerelease = false`). A packaged internal build may inspect GitHub prereleases for controlled N to N+1 QA; missing or unreadable build metadata defaults to public. The main process must load the packaged channel before it constructs the updater. Public release tags remain numeric `X.Y.Z`; GitHub's prerelease flag is separate from a semver suffix, which the application continues to reject. After an internal N installation updates to public N+1, the installed app returns to stable-only behavior; repeat QA from a fresh internal N build.

This reuses the existing `public` / `internal` build-channel distinction and adds no user setting, server, telemetry or persistent QA enrollment. The release workflow continues to package only `--channel=public`.

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
- **Budget as an invariant with a checked precondition.** DS1TR keeps counting every model present in a map
  (cap 30) because DsrDataTool prunes unused models. A per-class coverage reserve (the vanilla model of the
  smallest participating slot of each class) guarantees a fitting in-map type; if the precondition cannot
  hold, slots are demoted deterministically, and only a map already over the cap through the special plans
  fails (as `permutation-v1` does). The prototype showed the failure the reserve prevents (0.13% of
  placements above their size class) and the fix (0 of 68,120).
- **Drops and team always follow the slot.** Every changed sampling placement carries a scaled NpcParam row
  whatever the scaling mode; otherwise a sampled type's own drops are duplicated or lost. Measured on the
  shipped engine: with scaling `vanilla`, single-owner equipment drops are already duplicated in 95 of 100
  seeds (mostly the Titanite Demon group), with the default `area` scaling in none. Not fixed here.
- **Fallback is bounded and ordered, and respects categories:** sampled, the legacy category fallback (only
  in "both" modes), keep vanilla (allowed under Prevent same; the legacy outcome), a type of the slot's own
  class that fits (a requested boss that cannot be placed downgrades to a normal enemy, never to a
  vacancy; NPC slots stop at keep vanilla), fit relaxed for normal slots (a recorded diagnostic, expected to
  be unreachable), then a typed retryable error. No unbounded retries.
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
- **Soul items "Transposed" was not offered in Batch 2** because the boss souls were in none of the catalog's pools; it is built in Part H (see "Soul items Transposed and the boss reward catalog" at the end of this file).
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

## Enemy placement algorithm is explicit; sampling-v1 infrastructure exists but cannot run (Part 0)

Decision: the enemy placement algorithm is a typed, versioned part of the configuration
(`enemies.placement.algorithm`: `permutation-v1` | `sampling-v1`), and presets move to version 5 to state it.

- `permutation-v1` is the production default and is frozen; presets of versions 1-4 migrate to it explicitly
  (never to `sampling-v1`), and a v5 preset must state the algorithm. Its output is unchanged.
- `sampling-v1` is a known identifier but is not in the implemented list; every layer refuses it with the same
  stable code, `ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE`, and nothing falls back to another algorithm. The key
  deliberately bypasses the engine's enum-fallback in `normalizeConfig`.
- Sampling RNG streams use the version string `enemy-placement-v1` and the name `slot:<stable-slot-id>`,
  independent of `RANDOMIZER_VERSION` (frozen at 0.5.66) and of product versions. The permutation path derives
  none of them.
- Model budget policy for `sampling-v1`: safety first. The target (30) applies to the final distinct models of a
  map, counts fixed and vanilla-kept models, admits deterministically, records the reason `model-budget`, has no
  Blighttown exception and not the legacy quirk of counting only new models. (Vanilla maximum measured: 28.)
  **Refined by the Part A decision below: 30 is a target, not an unconditional invariant.**
- Drop-ownership compatibility policy: `permutation-v1` keeps its documented legacy behavior (duplicated
  single-owner drops under Enemy scaling `vanilla`) because fixing it would change existing presets' packages;
  `sampling-v1` must give every changed placement a scaled NpcParam row, so drops and team follow the slot.
- The new engine module `placement.js` is repository-authored (GPL-3.0-only) inside the vendored source tree,
  recorded as patch `0005` and in `UPSTREAM_PIN.md`.

Why: the sampler changes every enemy seed, so it must never be reached implicitly; the infrastructure lands
first so that Part A is only the planner and its options, with the regression proof for the old algorithm
already in place.

## sampling-v1 for regular enemy slots (Part A): scope, classes and what is deliberately not decided

- `sampling-v1` is implemented for regular enemy slots only and is opt-in; the default stays `permutation-v1`.
  Slots that are not replaced get no placement. Everything else is planned by the unchanged code.
- Presets move to version 6 (`enemies.placement` = algorithm, `replacementChance`, `normalReplacement`, `fit`);
  versions 1-5 migrate to `permutation-v1` with the neutral values (100, `normals`, `anywhere`). Under
  `permutation-v1` any other value is rejected by validation and by the engine.
- Size classes are derived from the catalog capsule (`regular-size-v1`, model footprint = largest usable slot
  footprint) and fingerprinted by `classTableHash`; no curated table.
- Every changed `sampling-v1` placement carries a scaled NpcParam row, so the destination slot's drops and team
  win in every scaling mode.
- The reserve repair of the design (demote participants) cannot lower the model count; it is replaced by
  trimming the reserve, with the fallback ladder as the safety net.

## sampling-v1 model budget is non-worsening (Part A, decided 2026-10-05)

Decision: sampling-v1 uses a non-worsening model-budget policy. The normal target is 30 distinct models per map. If out-of-scope/fixed plans already require more than 30, that unavoidable baseline becomes the effective cap. The regular sampler may preserve or reduce that overflow but may not increase it.

- Baseline of a map = the distinct models of the fixed plans (bosses, dragons, units, Mass of Souls, special and
  other non-regular slots, at their final models) plus the vanilla models of regular slots that the replacement
  chance (or mode `none`) leaves alone. `effectiveCap = max(30, baseline)`. No separate allowance for the
  sampler, no Blighttown exception, no legacy "only new models count".
- Final-model accounting is exact by construction (baseline plus admitted models; a replaced slot's vanilla
  model is never added), so a model whose last slot is replaced stops counting without reference counting.
- Why: the earlier hard cap rested on vanilla data only. After the dragon/boss plans the Undead Parish has
  30-31 models (88 of 100 seeds with `permutation-v1`, regular enemies off), and with kept vanilla models up to
  33, so a hard 30 rejected valid configurations the shipped algorithm already produces. 32-33 models in the
  Parish exceed anything shipped before and have not been tested in the game.
- Consequence for fit: with zero headroom a slot can be left without a fitting type. The `fit-relaxed` rung was
  removed instead of violating the fit invariant; the slot raises the retryable `ENEMY_PLACEMENT_INFEASIBLE`
  (about 9% of seeds at 25% and 5% at 60% with `fits`; none with `anywhere` or from 90%), and the pipeline
  retries with a new combined seed (3 attempts).
- Error classification: `ENEMY_PLACEMENT_INFEASIBLE` is retryable and typed; `ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE`,
  `ENEMY_PLACEMENT_CLASS_UNDERIVABLE`, `ENEMY_PLACEMENT_OPTIONS_INVALID` and
  `ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE` are deterministic and never retried. The Fog retries are unchanged.

## sampling-v1 difficulty: derived classes, authored curve, map-local retries (Part B)

- Difficulty mode and strictness keep the legacy values and meanings (modes 1-4, strictness 0-2, strictness only in the
  curve modes); new sampling configurations default to the legacy defaults (random + easy Asylum, a bit loose);
  `permutation-v1` requires `random` / `loose`. Presets move to version 7.
- Difficulty classes are derived (`regular-class-v2`): map progression tier (authored play-order table) and HP rank;
  the destination class is the class of the slot's own model. No legacy class table; agreement with the original
  classes is moderate by design. The class table, progression table and strictness tables are fingerprinted/versioned.
- The strictness offset tables and the Easy Asylum rule (every regular Asylum slot accepts only classes 0-1) are
  DS1TR-authored; the legacy tables and hard-coded Asylum lists are not used.
- Under budget pressure the difficulty filter widens to the nearest class (counted); physical Placement fit is never
  relaxed, and no `fit-relaxed` placement exists.
- An infeasible map is replanned alone with attempt-specific streams (`attempt:<n>:slot:<id>`, attempt 0 = the
  unchanged `slot:<id>` stream), at most 3 attempts; after that `ENEMY_PLACEMENT_INFEASIBLE` carries map, attempts,
  settings, baseline and effective cap and stays retryable at the pipeline level. Structural errors stay non-retryable.
- Residual risk, deliberately not addressed here: a Parish with 31-33 models has not been tried in the game.

## sampling-v1 Type replacement: map-level groups, injectivity yields to the model budget (Part C1)

- Type replacement keeps the legacy values and default (disabled; `permutation-v1` requires disabled). Presets move to
  version 8. For regular enemies "enabled, except roaming bosses" behaves as "enabled" (no boss source exists yet).
- The group is the original model within one map (the original resets its type map per map), decided once from its own
  stream (`type:<map>:<model>`, attempt-aware); the group candidate must satisfy every member. Later members never
  bypass fit or difficulty as they do in the original.
- The model count of a group decision is reference counted (`ModelLedger`): a replaced group frees its original model unless
  something else still owns it. `effectiveCap = max(30, baseline)` is unchanged.
- The original's rule that two kinds in one map never get the same replacement is kept as a preference. It is the last
  thing to yield (shared-model rung): measured, it makes the Parish infeasible at high replacement chances under the
  DS1TR cap (93 of 100 seeds), deterministically, so retries cannot solve it. Fit, the budget and fixed entities never yield.
- The map-local retry limit stays 3: Type replacement does not raise retry pressure above Part B.
- A custom enemy pool enters through `createCandidatePool` / `candidateFilter` (implemented in Part C2, below).

## sampling-v1 custom enemy pool: restricts replacements, provable validation, one-model grace (Part C2)

- The pool is a set of model ids for **regular enemies**; the original's boss and NPC entries are a documented gap (parity
  PARTIAL). It restricts replacement candidates only: unselected enemies stay in the game and may be the vanilla fallback;
  fit, Prevent same, Easy Asylum, Type replacement, drop ownership and determinism are untouched. Presets move to version 9.
- Validation rejects a pool only when the impossibility can be **proven** (malformed or stale ids, no slot can take any
  selected enemy, or - at replacement chance 100% - a map whose proven-minimal model cover cannot fit). Percentage and
  "smallest model larger than a slot" heuristics were measured to reject healthy pools and are not used. Structural errors
  are never retried; a participation draw that does not fit stays a bounded map-local retry. The retry count stays 3.
- Slots no pool model can ever replace stay vanilla and belong to the baseline; the coverage reserve of a restricted pool is the
  fewest models that keep every participant servable (a coverage witness).
- **Model grace:** the nominal target stays 30 and the baseline rule `max(30, baseline)` stays. A restricted pool may use an
  effective cap of **31**, and only when the base cap is 30, 30 is proven impossible and 31 is proven sufficient. A baseline above 30
  gets no grace (it may be kept or reduced, never raised: 31 stays 31, 32 stays 32, 33 stays 33), and nothing above 31 is ever
  granted. Reason: without it a unique single-slot model that participates on a map already filled to 30 by the slots
  left alone has no solution at any draw, a failure retries cannot remove (about 2 of 100 seeds at 25% for a small healthy
  pool); 31 is a model count `permutation-v1` already produces in valid output. This is not a general raise of the limit and the
  Parish 32-33 baseline risk stays a separate item. Use of the grace is recorded per map in the diagnostics.

## The world randomizer data is prepared by the application; helpers are self-contained (backend 0.29.0, desktop 0.16.0)

Date: 2026-10-05. Supersedes point 2 of "FogHost v1: ... user-supplied data root" (the data root is no longer chosen by the
player) and the "Fog data root" blocker of the packaging notes.

1. **Product rule:** a player needs Windows, Dark Souls Remastered and DS1TR only. No folder of another tool, no .NET, no
   Node, no command line.
2. **Provenance first** (`docs/FOG_DATA_PROVENANCE.md`): of the 59 files FogHost reads, 19 (18 maps, GameParam) are
   byte-identical to a clean installation and are copied from the verified pristine snapshot; 40 (fog.txt, 4 layouts, 18 event
   and 17 talk files) are not vanilla (the events and talk scripts are FromSoftware data edited by the FogMod author) and
   cannot be derived.
3. **Nothing from FogMod's `dist` is bundled.** The 40 files are fetched by the player's computer, once, over HTTPS, from the
   FogMod repository at the pinned commit (SHA-256 per file, size bound, atomic install, no execution of anything
   downloaded). This replaces "never bundled or downloaded" with "never bundled, fetched from upstream by the player's own
   machine". It carries a legal caveat for the project owner (the fetched scripts are FromSoftware-derived material the
   upstream repository already publishes); if upstream disappears or objects, the world randomizer is refused with a plain
   message and no copy is ever shipped.
4. **Offline:** a valid cache needs no network; only the first run of the world randomizer does. An unsupported game build
   (a derived file whose hash differs) is reported as such, never worked around.
5. **Self-contained .NET:** DsrDataTool and FogHost are published `win-x64`, `--self-contained`, single-file with compression.
   Each carries its own runtime; no PATH, global dotnet or `dotnet` CLI is used at run time. The packager fails if a helper
   does not start with all .NET locations hidden.
6. **Plain-language errors:** the primary message is for players; the code and exact reason go to the run log and manifest.
   `DS1TR_FOG_DATA_ROOT` stays as a developer override only.

## The item shuffle keeps FogMod's key items where FogMod can follow them (backend 0.29.1)

Date: 2026-10-06.

1. **Cause, not seed luck.** FogMod derives the logic area of a key item from the annotated item lot that holds it
   (`ItemReader.FindItems`), needs exactly one copy of each required item there and rejects a second copy in another area.
   It cannot represent shops, NPC gifts or enemy drops that it does not annotate, nor the starting inventory. The combined
   engine shuffled everything globally, so the failure was likely (up to 93% of the seeds) and identical on every reseed.
2. **Fix in the planner, not in the user's settings.** The engine receives the key items and annotated lots from the verified
   `fog.txt` and keeps the single copy of each required item inside annotated lots and every extra copy (shop duplicates of
   the Crest of Artorias and Residence Key, Cast Light, non-required items) inside shops. The items still move; nothing is
   duplicated; no item category is disabled and nothing is blocked in the UI. A shuffle that already satisfied the rule is
   unchanged, so earlier working seeds keep their placement.
3. **Retry classification:** `FOG_UPSTREAM_INCOMPATIBLE` is structural and not retried with another combined seed (the
   shuffle already honors the rule, so a repeat means the data or the configuration cannot work). `FOG_SEED_UNSOLVABLE` stays
   retryable with another Fog seed.
4. **Message:** "These item and world settings cannot be combined yet ..." with no item or lot names; the exact reason stays in
   the log and the manifest. The case should no longer occur.

## Seed Codes and immutable generation profiles (backend 0.30.0, desktop 0.17.0, contracts 0.13.0)

Date: 2026-10-06.

1. **A world is identified by a Seed Code, offline and self-contained.** `DS1TR1-` + Crockford base32 of format byte, deflated
   canonical envelope (profile id, master seed, complete settings) and a 4-byte SHA-256 checksum. No server, no database.
   Settings are stored in full (not as a diff from defaults) so a changed default can never change an old code.
2. **Application version and generation behavior are separate.** `ds1tr-gen-v1` = the behavior of desktop 0.16.1 (combined
   engine 0.5.66, seed derivation v1, retries 3/5, FogHost 0.5.0 / FogMod 6e6dc78 / world data f394fa53, item tracking v1).
   Profile ids never contain an app version, the app version never influences output (recorded in the manifest only), and an
   app refuses a code only if it does not know the profile.
3. **Core rule:** any change that alters the output of an existing seed needs a new profile; published profiles and golden
   fixtures are never edited and fixtures are never regenerated to make a test pass (`docs/GENERATION_COMPATIBILITY.md`).
4. **Drift is loud.** The pipeline compares the engines it actually used with the profile's allow-lists and stops with
   `GENERATION_PROFILE_MISMATCH` rather than silently generating another world.
5. **The master seed is made at Randomize** by the backend when the run starts (never at startup or import); it is an Advanced
   detail. Loading a Seed Code makes its settings authoritative; any later change to a generation setting ends that identity.
   Numeric/explicit master seeds of older configurations still work. Presets keep storing settings only.
6. **Backend owns persistence.** Settings (`settings/ui-settings.json`, schema 1, with fallback warning) and run history
   (`history/runs.json`, schema 1, 200 entries) live in the backend state folder; the game path stays separate machine state.
   Regenerate files (was Recreate) uses only the Seed Code and refreshes the existing entry; a Seed Code that is already in the
   history never creates a second entry.
7. **Spoiler preference and game path are excluded** from the code (documented per field in `GENERATION_FIELD_POLICY`; a test
   fails if an option is added without a decision).
8. **Launch game** is a desktop (Electron) concern: Steam `steam://rungameid/570940` first, the game executable as a fallback,
   and the method used is reported. The renderer never starts processes.

## Event owners: one resolver for which event file serves a map (backend 0.37.1)

Date: 2026-10-07.

1. **The bug.** The Darkroot Basin has no event file; custom events for its parts were silently dropped (roaming bosses, hostile NPCs, deferred and disabled entities, model-specific locks). Proven from the game data: it shares the Garden's 108 entity IDs and the Garden's file names 96 of them.
2. **One resolver, in the data tool.** `EventOwnerMap`; no `if m12_00_00_01` anywhere else. `EnsureEventOwnersExist` fails loudly when a map would have no owner. Allocation is per owner file.
3. **The classic permutation stays frozen.** The mode is chosen by the backend for `sampling-v1`, travels in the catalog, and the patch step reads it from there. 10 of the 27 golden fixtures are identical, 12 sampling fixtures change in the owner event file only, 5 hostile NPC fixtures change because the Basin is now a legal destination. Recorded exception 2.
4. **The Basin is not excluded.** The earlier exclusion of hostile NPCs from the Basin was a workaround for this bug and is removed.
5. **A guard that fails on the old behavior.** `SmokeVerify audit` runs in every matrix world and every quick-world check.

## Hostile NPCs: a body in a regular slot, owned by the slot; NPC replacement not built (Part F1; backend 0.37.0, desktop 0.25.0, contracts 0.23.0)

Date: 2026-10-07.

1. **Scope: F1 only.** The legacy has two NPC options (reconstruction in `ENEMY_PLACEMENT_MODEL.md` 10.9). **Hostile NPCs** is built. **NPC replacement** is **not built, by decision**: its six slots (two Merchants, Andre, Vamos, Gough, the Fair Lady) are services and the legacy
   removes them; friendly NPC support would need talk-script, quest-event and service work that cannot be proven without playing (the hard stop of the task). No control is shown for it and the planned placeholder was removed. The legacy has no hostile NPC destination slots, no NPC-to-NPC rule,
   no invaders or summons (`c0000` is in no table): there is nothing to build for them.
2. **Three of four sources.** The Undead Merchant (Undead Burg), Andre and Vamos. Gough (his part has think 0, his AI is event driven, a giant capsule, 20,000 souls) and the Lower Burg Merchant (a logic script the data tool does not copy) are left out, each for a documented reason; an unknown NPC defaults to unsafe (`classifyNpcSlot`).
3. **The slot owns the actor.** The row of a hostile NPC takes its team, `npcType`, covenant mark, respawn bit, drop type, humanity lot, item lots and `getSoul` (in every scaling mode) from the slot; the body, animations and AI are the NPC's. Rejected: letting the NPC carry its row as in the legacy (a hostile Andre would drop Andre's loot and
   never respawn).
4. **Pool membership, not a second pass.** The legacy adds the models to the normal pool, so they are appended to the candidate table after the regular classes are derived and drawn from the existing streams. No new stream, no new constant; the option off adds nothing and the earlier worlds are byte-identical.
5. **Wake-up events with a limit.** Every live hostile NPC gets the existing wake-up event (it also sets team 6, the legacy allegiance event); the limit of 12 per map bounds the events (the uncapped numbers are in the event allocation document). Hostile NPCs are refused with a custom pool (the pool lists enemies) and with `normalReplacement` `none` / `bosses`.
6. **Seed Codes and presets.** `hostileNpcs` is an extension key (neutral false), presets version 13; no existing code changes (22 golden fixtures byte-identical).
7. **No talk file.** No ESD is read or written by Part F; the package file list has none.

## A regular enemy as the body of a boss encounter: a safe subset of three (Part E2; backend 0.36.0, desktop 0.24.0, contracts 0.22.0)

Date: 2026-10-07.

1. **Scope: a subset, honestly named.** Legacy "Boss replacement: only with normal enemies / either" puts a normal enemy into every boss arena. DS1TR builds it for **the Asylum Demon, Taurus Demon and Capra Demon
   only**. The audit of every boss encounter, with a class and a reason for each exclusion, is `ENEMY_PLACEMENT_MODEL.md` 10.8. Adding an encounter needs a documented decision and a game test; "full E2" is not
   claimed. (This supersedes decision 1 of Part E1, which deferred E2 entirely.)
2. **Entity-preserving body, no adapter.** The slot keeps its boss entity ID and its events; only the body that stands in the arena changes, through the existing Part D boss placement path. Rejected: a new
   wake-up/death/fog adapter per boss (new events, new Fog overlay rules, a second source of truth for the same encounter) and a model-specific event rewrite per boss (the "broad bespoke per-boss rewrite" that
   is a hard stop). Proof: EMEVD diff against Part D packages, no new events.
3. **The encounter owns the reward and the respawn.** The body's scaled NpcParam row takes `dropType`, `getSoul`, `disableInitializeDead` and the item-lot cells from the boss row and sets `disableRespawn` to 1
   (`EncounterOwnedCells`). A body never respawns and never drops its own loot instead of the encounter's.
4. **The health bar keeps the boss's name.** Functional completion beats visual parity: a body model has no boss-name id; the quick test says so explicitly.
5. **Scaling is the existing setting.** Area and Progressive give the body the boss's HP and strength (a boss's fight length), Vanilla keeps its own stats; no slot-specific rule.
6. **RNG.** Two new named streams under `enemy-placement-v1`: `category:slot:<id>` (the `both` draw) and `body:slot:<id>`. No stream that exists today is consumed differently: neutral worlds are identical,
   and with the chance at 100 `both` equals Part D's boss sampling exactly.
7. **Seed Codes.** `bossChanceInBossSlots` (neutral 90) is an extension key, `bossReplacement` gains `normals` / `both`; no existing code changes (18 earlier golden fixtures byte-identical); presets version 12.
8. **Model budget.** `max(30, the map's vanilla model count)` against the running counts of the pass; no Custom Pool grace, no relaxing of any event collision check.
9. **UI.** The existing Boss replacement select gains two options; "Boss chance in boss slots" appears only with `both`; the hint names the three encounters and the kept bosses. No control for a thing that does not happen.

## Roaming boss respawn bit written for both values (backend 0.35.1)

Date: 2026-10-06. The roaming-boss row inherits `disableRespawn = 1` from the boss row, so only writing the bit when stay dead is on left "stay dead off" without effect. DsrDataTool now writes
the bit for every roaming row (1 for stay dead, 0 otherwise) and asserts it. Only roaming worlds with stay dead off change, in one cell of `GameParam`; no new generation profile (pre-release,
narrow, proven; `docs/GENERATION_COMPATIBILITY.md`, "Recorded exception"). A sixth quick test (`quick-roaming-respawn`) exercises the respawning variant; the stay-dead variant was played and passed.

## Smoke tests: quick worlds by seed search, no forced placements (backend 0.35.0, desktop 0.22.0, contracts 0.20.0)

Date: 2026-10-06.

1. **Why.** The first Part E test worlds put their targets in the Undead Burg and Parish, an hour of play away. A smoke test has to show one feature within about a minute.
2. **Quick vs extended.** Quick worlds name an exact slot, the distance from the player start and the expected replacement, and promise 20-90 s from gaining control. Everything that needs
   travel or progression, or exercises a whole system (Fog, the Bell Gargoyle event, a map full of bosses), stays as an *extended gameplay test* in its own group.
3. **No test-only override.** A forced-placement mechanism (a config key that overwrites chosen slots) was considered and rejected: it would add an engine path that production never takes,
   put a second definition of a world next to the Seed Code, and make history, Copy seed and Regenerate files able to drop the override silently. A search over normal master seeds finds
   worlds with the same properties (the roaming pass visits slots in a fixed order, the Part D sampler at 100% always replaces the Asylum Demon) using only normal settings. The quick worlds
   are therefore ordinary Seed Codes of `ds1tr-gen-v1`; nothing about Seed Codes, the engine, the data tool or normal Randomize changed.
4. **First visit.** The Asylum map holds both visits' enemies (event 11810350 switches the sets); a target must have entity id 1810101-1810113 to exist the first time. Dummy slots and the
   passive Asylum Hollows are not targets.
5. **The definition is the guard.** A quick world's expectations (slot, model, souls, respawn bit, wake-up event) live next to its seed in `packages/contracts/src/smokeTests.ts`; unit tests pin
   their shape and the documented Seed Codes, and `smokeTestCatalog.mjs verify` regenerates each world and fails loudly if the files no longer contain the promised target.
6. **Type replacement is not a quick test.** The Asylum's Hollow kind contains the protected passive slots, so the planner never replaces the whole kind there; the Type + roaming world stays
   extended (Undead Burg). The "except roaming bosses" behavior (each slot decides) is what the three-Smough quick world runs under.

## Roaming bosses: bosses in regular enemy slots, Part E1 (backend 0.34.0, desktop 0.21.0, contracts 0.19.0)

Date: 2026-10-06.

1. **Scope.** Part E = cross-class replacement. E1 (a boss in a regular enemy slot, "roaming boss") is built; **E2 (a regular enemy in a boss slot) is deliberately not
   built**: a boss arena's completion depends on the boss entity (boss-bar name, health, fog gate and kill-flag events, the Fog boss overlay), and a safe version needs a
   wrapper that cannot be proven without playing. No control is shown for it (a control without an effect is a bug).
2. **A second pass, not a rewrite.** The regular sampler is unchanged and runs with `normals` / `none`; `planRoamingBosses` overrides slots afterwards from named streams
   that did not exist before. A slot that gets no boss keeps exactly what it had (proved byte for byte in the sweeps), so enabling the option never reshuffles the normal
   enemies that stay.
3. **Safety boundary (DS1TR, not legacy).** Bosses with an authored souls entry may roam, except those that need scripted terrain, cannot move or are neutral (Bed of Chaos,
   Ceaseless Discharge, Dark Sun Gwyndolin, Lord Gwyn) and every multi-body boss. Slots with special handling never take a boss. At most 10 per map.
4. **Souls and respawn are properties of the placement's scaled NpcParam row** (decision D-7 of the model document, now built): `getSoul` = percent of an authored table of the
   fight's souls (the real boss rows have almost no `getSoul`); "stay dead" sets the row's own `disableRespawn` bit instead of the legacy per-entity event (no vanilla event is
   touched). The bit is unverified in the game and the UI says so.
5. **Model budget:** the non-worsening rule of Part B (`max(30, size before the pass)`), exact admission per boss, **no Custom Pool grace**; a boss that would raise a map above the
   limit is not placed.
6. **Seed Codes and profile:** extension keys again (`bossChanceInNormalSlots` 10, `roamingBossSouls` 50, `roamingBossesStayDead` false); `normalReplacement` gains `bosses` and
   `both`. No new generation profile: the change is additive and gated behind options no earlier code can contain, proven by the 15 earlier golden fixtures, the 24 lock hashes
   and the sampling pins. Worlds that use the new values are pinned by `ds1tr-gen-v1.roaming-bosses.json`.
7. **Unique bosses** does not apply to roaming bosses (legacy: it only concerns a boss replacing a boss). **Type replacement:** `enabled` gives a whole kind one boss;
   `enabled-except-roaming-bosses` lets every slot of the kind decide on its own - the legacy distinction.

## Boss sampling (Part D) and extension keys (backend 0.32.0, desktop 0.19.0, contracts 0.16.0)

Date: 2026-10-06.

1. **Scope.** `sampling-v1` gains `enemies.placement.bossReplacement` (`classic` = the existing archetype shuffle, the neutral value; `bosses`
   = every single-body boss slot draws its own replacement from the portable boss pool, with repeats) and `uniqueBosses` (the pool is
   used up before a boss repeats). The replacement chance applies to every boss slot; Gwyn follows the Gwyn spawn nerf per candidate.
   **Linked units (Priscilla, Kalameet, Gaping Dragon, Seath, the Sanctuary Guardian, the Bell Gargoyles and the Centipede Demon) stay with their own planners in both modes** (Four Kings and Bed of Chaos are members of the sampled pool): unit
   participation by chance and unit sampling are not part of this step. `bossChanceInBossSlots` and the cross-class modes belong to
   Part E and are not exposed (a control without an effect is a bug).
2. **No new generation profile.** The new options default to a neutral value that reproduces the earlier behavior exactly, and the
   engine change was proven byte-neutral (10 + 2 golden fixtures from the Seed Codes alone, 24 permutation lock hashes, the sampling
   pins). Rule for such options: **extension keys**. The canonical settings are complete in memory; a Seed Code stores an extension key
   only when it is non-neutral, so no existing code changes its text and none can decode to a different world. The profile id stays
   `ds1tr-gen-v1`; worlds that use a non-neutral value are frozen from the moment they are shared and have their own golden fixtures
   (`ds1tr-gen-v1.boss-sampling.json`).
3. **Unique bosses** is defined as a cycle over the boss pool (restarts once every boss was used); the legacy reset after 22 uses is tied
   to its own pool size and is not copied. The Second Gargoyle independent draw keeps its own stream and its per-seed Gwyn coin.
4. **A boss slot that takes part draws from its own stream** (`slot:<id>`, `enemy-placement-v1`), the chance is its first draw, so lowering
   the chance only removes slots and never changes the boss another slot gets. Boss slots left vanilla emit no placement (their events
   are not edited).
5. **Known defect found (not part of this change):** `sampling-v1` with Type replacement `enabled` and Placement fit `anywhere` fails for
   some seeds in `DsrDataTool patch-enemies` with "Randomizer event ID is already used: 11419980". Logged in `IMPLEMENTATION_STATUS.md`.

## Event windows reserve fixed IDs (backend 0.32.1)

Date: 2026-10-06.

1. **Root cause, not a retry.** The `Randomizer event ID is already used: 11419980` failure came from the allocator, not from a seed: the
   custom event window of a map is chosen before the fixed-ID Bed of Chaos floor event is added. The tool now reserves every fixed ID it
   will add before it chooses the window, and asserts that no map uses more IDs than it reserved. The duplicate check at registration stays
   (no overwrite is ever allowed). Details and the namespace table: `docs/EVENT_ALLOCATION.md`.
2. **No behaviour change for valid worlds.** Windows that did not reach the reserved ID get the same base and IDs as before; only worlds that
   could not be created before are different (they are now created). Therefore no generation profile change.
3. **Rule for new events:** an event with a fixed ID must be added to the reserved set before the window is chosen; a feature that can add
   many events must be measured with the audit (`DS1TR_AUDIT_EVENTS=1`).

## A chosen game folder wins over detection (desktop 0.19.1)

Date: 2026-10-06.

1. A folder the person chose by hand and that passed the installation check is saved as machine-local state and used at the next start,
   even when Steam detection finds another copy. Detection is only the fallback (nothing saved, saved folder gone or no longer valid).
   An automatically detected copy is never saved, so a later Steam change is followed.
2. "Change game folder" updates the saved choice once the new folder passed the check. An invalid or vanished saved folder is kept (a
   disk that is temporarily offline wins again when it returns) but not used.
3. The choice is not part of any Seed Code, preset or configuration.


## Legacy-style sampling is the default of new configurations (Part G, desktop 0.26.0)

Date: 2026-10-07.

1. **Decision.** A new configuration (first start, "Restore defaults", the base of a new preset) uses `sampling-v1` with the legacy defaults. Reason: Parts A-F made the sampler complete for the safe subsets, the legacy defaults are what people expect from the original tool, and the classic permutation stays one click away ("Classic permutation").
2. **Nothing existing is reinterpreted.** What a record without the key means is the classic permutation, forever. Seed Codes, presets, saved settings and history keep the algorithm they state; only "nothing saved" starts from the default. The test worlds have their own frozen base. No generation profile change (`docs/SEED_FORMAT.md`, "Defaults and old records").
3. **No dead controls.** A control that cannot do anything on the chosen algorithm is not shown (the classic permutation hides the sampling options); the "planned" placeholder is removed.
4. **Order of the planners is a contract**, locked by tests (`docs/ENEMY_PLACEMENT_MODEL.md` 10.10): items, boss plan, class tables, hostile NPC types, regular sampler, roaming pass, own rows and events; every draw comes from a named stream, a neutral option draws nothing.
5. **The placement validation summary is recorded** in the run manifest (`enemyPlacement.validation`), outside the placement hash.
6. **Status wording.** "Enemy Placement core complete with documented safe-subset deviations": E2 (three boss encounters), F (hostile NPCs only), E1 (supported single-body bosses) and the frozen classic quirks. Part F, E2 Capra and the extended worlds remain not verified by playing.

## Release trust: unsigned until the owner supplies a trusted certificate (desktop 0.26.0)

Date: 2026-10-07.

1. **Primary download is the installer** (per-user NSIS, no administrator, `requestedExecutionLevel asInvoker`); the portable executable is the secondary download. The installer is not claimed to prevent antivirus reports.
2. **Signing is architecture, not a claim.** `npm run package:win -w @ds1tr/desktop -- --release` is the only mode that signs; it refuses to run without credentials from the environment (`CSC_LINK` / `CSC_KEY_PASSWORD`, or Azure Artifact Signing variables) and without `DS1TR_EXPECTED_PUBLISHER`, and it verifies every DS1TR executable (application, DsrDataTool, FogHost), the installer and the portable with `tools/release/release-verify.mjs --require-signature`. No certificate, key or password is ever stored in the repository. The pipeline was exercised with a throwaway certificate in a scratch folder (results: TEST-SIGNED, never trusted; nothing from it was kept).
3. **Helpers are published as an uncompressed single file** (a compressed bundle is one 34 MB block of 7.8 bits-per-byte data, the profile antivirus heuristics associate with packers, and it decompresses at every start: DsrDataTool started in 1,054 ms against 373 ms uncompressed, FogHost in 315 ms against 384 ms). No UPX, no obfuscation, no elevation helper in the package.
4. **No automatic uploads** of any build to an online scanner; no telemetry; no network use besides the one-time world data download.
5. See `docs/RELEASE_TRUST.md` and `docs/SECURITY_AND_PRIVACY.md`.

## Soul items Transposed and the boss reward catalog (Part H, backend 0.38.0 -> 0.39.0, contracts 0.25.0)

Date: 2026-10-07.

1. **Boss rewards belong to the encounter, not to the body.** They are awarded by the shared common event 970 (a trigger flag, three item lots), never by an NPC row. The scan records them as `bossRewards` (27, with the chain of rows awarded with each lot, the map that sets the trigger flag and the DLC flag) and fails with a typed scan error when event 970 changes shape or an NPC row names a reward lot. Boss sampling, roaming bosses, encounter bodies, the Second Gargoyle and Pinwheel Chaos cannot move, duplicate or delete a reward: the data tool never writes event 970 or its initialisations (asserted on every generated package, `SmokeVerify lots`).
2. **Transposed = the legacy's behavior.** The twelve soul rows join the item pool only with Soul items Transposed; each soul with a recipe has a 75% chance (stream `soul-items-transposed`) to become a weapon of its boss (a family drawn uniformly, then a reinforcement level), then the whole pool is shuffled. The recipes are game data and equal the legacy lists for every soul. Shuffled and Replaced are byte-identical to before (the boss souls stay), so no Seed Code changes; `transposed` is the third value of the existing `items.soulItems`.
3. **Recorded deviations** (all in `docs/ITEM_PROGRESSION_MODEL.md`): the two list partitions (Ornstein, Artorias), no Sunlight Spear for Gwyn's soul (a talk-script exchange, not game parameters), the boss souls stay put in the two older modes, a global permutation instead of the legacy's difficulty-ordered fill.
4. **DLC.** The DLC boss rows carry `m12_01_00_00`, so the existing rule (no progression, Titanite, Ember or other restricted item in the DLC) holds for them.
5. **includeDlc stays a fixed `true`** (decision C, section 7 of `docs/ITEM_PROGRESSION_MODEL.md`): no source has such an option; excluding the DLC is a separate phase.

## Key placement, Lordvessel and Lord Souls (Part H; backend 0.40.0, contracts 0.26.0, desktop 0.28.0, FogHost project 0.6.0)

Date: 2026-10-07. Full description: `docs/ITEM_PROGRESSION_MODEL.md` sections 5 to 8.

1. **One model of the world: FogMod's own graph.** There is exactly one reachability model. FogHost exports it (`progression-model`, FogMod's graph built with every randomization option off, `ds1tr-progression-model-1`), the backend caches it beside the world data and refuses one of another annotation or commit, and the combined engine only *evaluates* it (`keys.js`, a closure over edge conditions). Rejected: a hand-written list of key rules in JavaScript (a second world that would drift from FogMod's), and calling FogMod from inside the item shuffle (the engine stays a Node module with no process dependency). The two evaluations are a hard gate: `progressionDifferential.mjs` compares them with FogMod's `GraphChecker` on 3000 assignments, and every swept package is read back by FogMod's own item reader.
2. **Assumed fill, not random retries.** A key is placed only where the player can already be with every unplaced key assumed in hand; the finished world is checked as a whole. Bounded (32 attempts, streams `item-keys:<n>`), deterministic, typed on failure. Displaced items go to the freed key pickups from their own stream (`item-keys-fill`), DLC rule respected.
3. **Destinations are certain pickups only.** World and boss lots with exactly one entry, covered by the model, not in the DLC. Never a shop, an enemy drop or a gift (a drop can be missed, a shop needs souls, a gift needs an NPC who can die or leave). This deliberately differs from the legacy (keys may land in shops).
4. **Extension fields, no new generation profile.** `items.keyPlacement` (`vanilla`), `items.randomizeLordvessel` (`false`), `items.randomizeLordSouls` (`false`) are stored in a Seed Code only when non-neutral; presets are version 14; the pinned `HostVersion` of FogHost stays `0.5.0` (only the project version moves). The UI default is `vanilla` although the legacy default is Shuffled: the neutral value of a new option must be the behavior of every earlier world.
5. **Lordvessel and Lord Souls are switches inside Key placement.** Off = fixed in the graph at the vanilla place; on = part of the fill. Lord Souls are boss rewards: ownership stays with the encounter flag (event 970 untouched), only item lots change. The Lordvessel gift event (lot 1090) is untouched and holds another pool item. **No event is rewritten**, so the hard stop for broad event rewriting never applied.
6. **Not built, with a reason.** Race Mode and Race Mode+ (UNSUPPORTED: they need per-mode candidate location sets that FogMod's model does not have), the legacy Difficulty classes (I3), keys in shops (deliberate), Orange Charred Ring / Broken Pendant / Crest of Artorias / Residence Key / Crest Key / Archive Tower keys (their pickups are not certain lots the model covers).
7. **Failure handling.** `KEY_PLACEMENT_UNSATISFIABLE` and `KEY_PLACEMENT_FILL_IMPOSSIBLE` are retryable with a new combined seed; the structural codes are not. With the fog randomizer on, a layout it cannot solve around the shuffled keys is retried (bounded) and then reported as `FOG_KEY_CONFLICT`; the exact reason stays in the log and the manifest.
8. **A guard that also stays.** `FogHost progression-check --game` checks the **vanilla world graph** against the written lots. With the fog randomizer on the played world is another graph; there the guarantee is FogMod's own generation (it fails rather than write an unsolvable layout). The documents say so.

## Race Mode, Race Mode +, item difficulty and the Laundromat Mixup (Part I; backend 0.41.0, contracts 0.27.0, desktop 0.29.0)

Date: 2026-10-07. Full description: `docs/RACE_MODE.md`, `docs/ITEM_DIFFICULTY_MODEL.md`, `docs/LAUNDROMAT_MIXUP.md`.

1. **The modes are strategies of one planner, not a second engine.** `KEY_PLACEMENT_STRATEGIES` (`keys.js`) says, per mode, which destinations are allowed, which model variant the fill reasons over and whether the race items are held to the race destinations; the assumed fill, the pairing of the displaced items, the typed failures, the final check and the streams are shared. A world that needs the skips of Race Mode + is produced by evaluating the *same* graph with the skip edges added (`FogHost progression-model --variant race-plus`, one more option of the one model), never by a second reachability implementation.
2. **Race destinations are derived from the game data, never copied from the legacy list.** Boss rewards, treasure chests (the objects of the map's treasure events with the chest model, read by the scan) and the vanilla pickups of keys and embers, intersected with the existing rule (certain, model-covered, non-DLC world or boss lot). Excluded with a reason: Black Knight drops and the other enemy drops (a drop belongs to whatever body stands in the slot), the Ingward, Andre and Undead Merchant gift / drop / shop rows, the Duke's Archives Golem drop, the DLC boss drops, the Orange Charred Ring reward (an event, not a lot). The embers, the Rite of Kindling, the Sewer Chamber Key and the Mystery Key (which the legacy places "as keys" in Race modes) are race items with a location restriction only.
3. **Race Mode + is a SAFE SUBSET.** FogMod's glitched logic models part of the legacy skip list; only the edges that correspond to a skip of that list are used (Capra, Lower Burg, Annex, Seal and Four Kings, the Purple Coward's Crystal wrong warp and Kiln edge). The skips FogMod does not model are not assumed, so no world needs an unlisted skip; the prison check stays (stricter than the legacy). The crystal itself has no item lot the data tool can move (the game awards it by an event), so it stays where the game puts it. With the fog randomizer on, Race + is made as Race Mode: FogMod, which assumes no skips, is the authority for the played world (a measured failure otherwise, `docs/FOG_ITEM_TRACKING.md`). Race + with an item difficulty other than Fair is refused, as the legacy GUI forces Fair.
4. **Item difficulty: the policy is exact, the tiers are ours.** The ordered groups of location tiers per item tier are reimplemented (Fair has none: it is the neutral value, zero draws, byte-identical). The legacy's per-lot item and location ratings are hand-made data under no license (AGENTS.md forbids copying), and the game has no field that reproduces them (prices are flat; rarity explains 28% of the legacy item classes). DS1TR derives its tiers from the game's data (keys, souls, equipment parameters, rarity, depth in the world graph); they agree with the legacy on about 40% of the lots both know. This is declared a SAFE SUBSET everywhere; difficulty only orders locations, the key validity check does not depend on it, so no mode can make a world unwinnable (hard stop 4 never applied).
5. **Laundromat Mixup is a SAFE SUBSET built from the clean game.** NPC characters (CharaInitParam rows from 6000, grouped by face) wear pieces some NPC already wears in the same slot, so every written id is one the game already loads on an NPC model at the level it was worn; hollowed variants are left alone; the data tool checks every row against the scan before it writes, and `SmokeVerify laundromat` compares every NPC cell with the clean game. Rejected: porting the legacy's list of NPCs and its armor sets (hand-made, unlicensed, and "usable on an NPC" is a judgement no data answers).
6. **Compatibility.** No new generation profile; every value is an extension (`race`, `race-plus` are new values of an existing enumeration, `itemDifficulty` neutral `fair`, `laundromat` neutral off); presets are version 15; all earlier golden worlds regenerate byte for byte.
7. **Typed outcomes.** `RACE_LOCATION_INVALID`, `RACE_PLUS_LOCATION_INVALID` (internal guards), `DIFFICULTY_POLICY_UNSATISFIED` (retryable), the existing `KEY_PLACEMENT_*` and `FOG_KEY_CONFLICT`; plain messages in `userMessages.ts`.
8. **Hint-sheet data** (`core/hintData.ts`): one record per placement (item, kind, importance, original and new location with the acquisition type and the semantic owner), tested for readiness; no hint sheet is built.

## Language, hint sheet, diagnostics and the support bundle (desktop 0.32.x, backend 0.44.x, contracts 0.30.x)

1. **i18next + react-i18next, resources bundled, English canonical.** Reasons: mature plural rules (Polish one/few/many/other), typed keys, runtime switching, no server. Resources are files per area bundled as one namespace so keys are type-checked.
2. **Language lives in the presentation layer only.** Not in the Seed Code, settings, hashes, run manifest identity or history identity (a stored status enum is rendered at display time). Consequence: the in-game text language of FogMod stays a separate world setting, relabelled *In-game text language*.
3. **Codes, not sentences.** The backend keeps its English text as fallback/log but every user-visible failure has a stable code; the renderer translates by code; unknown codes show a localized generic message with the technical text expandable. Plan lines carry keys.
4. **Game content names stay English** (no trustworthy localized source); they are parameters of translated sentences so a later localized lookup is possible.
5. **Hint data is built once, outside the package.** `hints.json` next to the run; old history entries are rebuilt only from placements that still exist, otherwise the action is disabled (no fabricated data). Spoiler levels are a pure filter in the contracts; the three levels do not invent ambiguity (the engine's area is per map).
6. **Exports are made in the interface, saved by the main process.** Single source for labels (the interface resources); the renderer cannot choose a path or a file type outside a short list. HTML carries a CSP that allows nothing but inline style and escapes every dynamic value.
7. **Support bundle by allowlist + redaction + leak scan.** Named entries only; refuse rather than send when a known identifier survives; deflated ZIP written in-house (no archive dependency); size caps documented. Correlation ids are diagnostics-only.
8. **Installer languages** limited to English and Polish (`nsis.installerLanguages`), chosen by the Windows display language; no language dialog.

## DLC content switch (backend 0.45.0, contracts 0.31.0, desktop 0.33.0)

Date: 2026-10-08. Full audit: `docs/DLC_AUDIT.md`.

1. **"DLC off" means "DS1TR neither uses nor writes DLC content"; it never removes or edits the DLC in the game.** The DLC is one map (`m12_01_00_00`). With the switch off its enemies, bosses, pickups, boss rewards, DLC-only enemy drops and DLC-exclusive starting equipment are not candidates, sources or destinations; their game files and param rows stay byte-identical to the clean game (SmokeVerify `dlc-untouched`).
2. **Done by filtering the catalog, not by changing the engine.** `excludeDlcFromCatalog` (`apps/backend/src/adapters/dlcExclusion.ts`) removes the DLC records from the data the combined engine reads, once, before `generate`. The vendored engine is unchanged; with the switch on the function is not called, so every earlier world is generated exactly as before (all 47 golden fixtures byte-identical).
3. **One new scan field.** `dlcOnly` on enemy drop lots (fork patch 0025): every enemy awarding the lot stands in the DLC map and no other enemy or event awards it. Omitted otherwise, so no other record changes.
4. **Fog randomizer + DLC off is refused, not emulated.** FogMod has no DLC option and its graph always contains the DLC areas and warps. Validation refuses the combination (`Excluding DLC content needs the fog randomizer to be off`), the interface locks the switch on while the fog randomizer is on and turning the fog randomizer on puts the DLC back. Revisiting this needs a FogMod change, not a flag.
5. **No new generation profile.** `output.includeDlc` was already part of the canonical settings (always `true` until now), so every earlier Seed Code, preset and history entry means DLC on and keeps its exact text; a document that lacks the field is read as DLC on. Turning the switch off is a different world and a different Seed Code.
6. **Hints and diagnostics follow the plan.** A DLC-off plan names no DLC content, so no hint does; the world summary (hint sheet, diagnostics) states "DLC content: off" only for such a world, and the world summary chips show a DLC chip only then.
7. **Shops and gifts of the DLC are DLC content (found by the audit of the release candidate; backend 0.45.2).** The first version of the exclusion treated every shop and gift as base-game; the DLC has merchants (shop rows 1581-1584, 6410-6426, 6500-6507, 6600-6611) and NPC gifts (Hawkeye Gough 1510, Elizabeth 1520), so a DLC-off world with the shop or gift pools on still wrote into them. They are now left out too. Ownership is not in the scan: shops are recognized by the DLC sell-condition flag area (1121xxxx) with their neighbouring rows and by the row ranges the DLC's own talk script opens (6500, 6600); the gifts by their NPCs (the models c4090-c4190 stand only in the DLC map). Every rule errs on the side of leaving a shop or gift as the game has it. The verifier compares those rows, and `dlcVerifierControl.mjs` proves it fails on a DLC-on world. The unreleased `ds1tr-gen-v1.dlc-off.json` fixtures were recorded again for this reason (two of four worlds change); no earlier fixture was touched.
8. **The DLC's drop tables are left out of "Guaranteed enemy drops" (backend 0.45.3).** With the DLC off the 100% scaling of drop chances no longer touches the DLC-only lots or the reward rows of DLC bosses (`dlcExcludedDropLots`, patch 0026); the verifier compares the chance cells. A correctness fix of the unreleased DLC-off feature: no new generation profile, DLC-on worlds are unchanged (`GENERATION_COMPATIBILITY.md`, "Recorded exception 3").

## UI / UX product cleanup (desktop 0.35.0, contracts 0.32.0, backend 0.45.4; 2026-10-08)

Full description: `docs/UI_ARCHITECTURE.md`; every option: `docs/SETTINGS_INVENTORY.md`.

1. **One registry describes every option.** `packages/contracts/src/settingsRegistry.ts` is the single source for the interface, the tests and the generated inventory: the field, the kind, the section, the Basic placement, when an option is available and why not, and the resets that belong to a change. The generation never reads it; the backend validates the same combinations on its own. Labels and help are translation resources under the same id, so a new option without a name, a help text and every choice explained in both languages fails a test.
2. **Basic and Advanced are two views of one configuration.** The Basic view never rewrites an advanced value; when advanced values differ from the recommended ones it says so and offers the way to them. The chosen view is a preference of the window (local storage), not part of the settings, a preset, a Seed Code or a history entry; switching view or language consumes no randomness and changes no value (tested).
3. **Options that do nothing are not drawn; their fields stay.** `items.guaranteeEarlyWeapon` and `items.balancedEarlyLoot` are read only by the older prototype path of the combined engine; the extracted-data path ignores them (the real pipeline writes identical files with them on and off). Showing a switch that does nothing is a lie, resetting the stored value would silently reinterpret an old Seed Code or preset. So the controls are not drawn, the fields stay in the configuration, in presets and Seed Codes, and the documentation says what happened. `items.randomizeProtectedItems` stays refused by validation. No generation change, no new profile.
4. **A disabled option is never silent.** Unavailable options stay visible, keep their value and show the reason (19 reasons, both languages); `?` works on them. Sampling-only options are shown disabled with "Only used with the placement method ..." instead of being hidden.
5. **Build channel.** `build-info.json` carries `channel: public | internal`, independent of signing. A public build hides the internal test worlds; an internal build shows them; Ctrl+Shift+Alt+F12 toggles them for a session. It decides only what the interface shows and is not a security boundary (the backend endpoints are the ordinary ones). A packaged app that cannot read its build information is public. A signed release must be a public-channel build (`release-verify.mjs`).
6. **The button is "Generate world" / "Generuj świat"** (it was "Randomize / Losuj świat"); the backend's plain-language messages that name the button were changed with it (text only). Config keys, enums and serialized values are unchanged.
7. **The brand mark that sat in the header is withdrawn** (file, import, styles, documentation); the application icon is the burning-sword artwork from the approved asset sheet (clean crop, ICO 16-256 px).

## Early-weapon / balanced-early-loot audit (2026-10-08; no code change)

Full evidence: `docs/EARLY_OPTIONS_AUDIT.md`.

1. **They are not legacy features that were lost.** Neither the Item Randomizer (`f7cabbd`) nor the Enemy Randomizer (`83f008d`) has an early-weapon or early-loot option. The flags come from the vendored engine (`f120efe`), which implements them only for its placeholder data set (`randomizeItems`, a toy catalog with a `sphere` per location); with real game data it never reads them, in upstream's own product too.
2. **No behavior is restored and nothing is invented.** The condition for a port (legacy code proves meaningful behavior that DS1TR lacks) is not met. Both flags default to true in every configuration, preset, Seed Code and golden fixture, so making them functional would change every world and need a new generation profile; a new early-game guarantee would be a new feature designed from real item and area data, not a port.
3. **The controls stay hidden, the fields stay.** Classification: LEGACY DEAD / NO-OP. `apps/backend/src/adapters/combinedEarlyOptions.test.mjs` pins the fact (identical placements and hashes for all four combinations on real-data-shaped catalogs; flags read only by the placeholder function), so changing it must be deliberate.

## Bosses only (2026-10-08, backend 0.45.5)

Full evidence: `docs/BOSSES_ONLY_AUDIT.md`.

1. **There is no limit on the number of roaming bosses of a map.** The cap of ten (E1) was a guardrail, not a game constraint, and the legacy tool has none; the limits that remain are the real ones: distinct models per map (the model budget, which counts models, never instances) and the candidates a destination can take. At 100% "Bosses only" gives every eligible ordinary destination a boss; a destination that cannot take one stays as it is, with a counted reason, and is never given an ordinary enemy (legacy `canBeNormal=False`).
2. **Ordinary dragon-class enemies are ordinary destinations.** Titanite Demons, Drakes and the other dragon-class bodies without linked parts take bosses in the roaming modes; the dragon plan keeps the bridge Hellkite, the Anor Londo gargoyles, the hydras and everything with linked parts, and with the ordinary modes it is unchanged.
3. **Easy Asylum is explicit and local.** It keeps the Undead Asylum as it is (bosses are not "easy"), and nothing else; it never gives the Asylum an ordinary enemy under Bosses only.
4. **A pre-public correctness exception, not a new profile.** Four fixtures change (`GENERATION_COMPATIBILITY.md`, "Recorded exception 4"); Seed Codes, presets and `ds1tr-gen-v1` keep their identity.
5. **The diagnostics are internal.** Per-map counters (destinations, protected by reason, selected, boss, normal draw, unchanged by reason, distinct boss models) live in the run manifest and the log line, not in a screen.

## Unit participation: the large regular enemies obey Replacement chance and "Nothing" (2026-10-09, backend 0.45.6)

1. **One decision per logical unit, taken at its root.** The dragon-class units, the hydras and the Mass of Souls groups take part in their exchange when the mode lets an ordinary enemy replace an ordinary enemy (`normals`, `both`) and the first draw of the root slot's stream is within the Replacement chance: the draw the regular sampler and the roaming pass already make. No new stream, no extra draw, so nothing else moves. Parts and helper records follow the root.
2. **The structural planners stay.** The exchange, the parts, the synthetic heads, the static bridge and the Titanite Demon rule are unchanged; only who takes part changed. A unit that does not take part is reserved (never an ordinary enemy of the regular pool) and emits nothing.
3. **A lone unit keeps itself; a Titanite Demon that a subset cannot place stays.** Both are counted. They are the honest limits of exchanging whole units (the Mass of Souls pair exchanges only when both groups take part).
4. **A pre-public correctness exception, not a new profile.** Twelve fixtures in seven files change (`GENERATION_COMPATIBILITY.md`, "Recorded exception 5"); Seed Codes, presets and `ds1tr-gen-v1` keep their identity. Chance 100 with `normals` or `both` is byte for byte unchanged.
5. **The help states it.** "Nothing" and Replacement chance now say that the large regular enemies follow them (EN and PL), and the limits of "Bosses only" name the units with linked parts.

## Offline Fog annotation model (in progress, 2026-10-11; baseline 0.39.0 retained)

1. **Keep the proven FogMod graph and writer; adapt in memory.** The project-owned typed catalog will create a fresh `AnnotationData.Annotations` DTO per run. This keeps FogMod's established graph and output behavior while YAML is removed from production input.
2. **Model warps and ordinary entrances separately.** Their active fields differ by section and side: warps use cutscenes and player/region destinations, while ordinary entrances use event flags, action regions, boss triggers and custom geometry. Separate types prevent unused fields from entering authored policy.
3. **Separate verified game facts from project policy.** A root verifier gates reads of pristine DSR map and GameParam facts. The catalog records logical-area map membership and policy explicitly; once cut over, the pinned reference file will remain only as a development oracle. The catalog will have its own profile and fingerprint, distinct from the old `fog.txt` SHA.
4. **No production switch or release yet.** The typed model and fact index are foundations only. A complete catalog, FogHost/backend migration, new-vs-reference generation proof and cold-offline validation remain required before removing the downloader, changing the external data manifest, bumping to 0.40.0, or publishing.
5. **Classify graph links separately from physical transitions.** `Entrance` and `Warp` remain the models for physical randomized transitions. An ordinary static `Area.To` route must stay within one verified map; only an explicitly classified `CrossMapGlitch` route may cross maps, and it must carry the consumed `hard` tag, have a rationale, and resolve both map endpoints. Same-map hard routes are valid static links because `hard` describes availability, not the route mechanism. Conditions are checked against known project area IDs and resolved key-item records whose game IDs exist in pristine GameParam. This permits a project-owned cross-map route when gameplay evidence supports it without allowing an unmarked physical crossing to be authored as a graph shortcut. The classification exists only in reconstruction policy and is not emitted to FogMod.
6. **Use explicit Area tag defaults and omit inactive scaling overrides.** Untagged Areas are ordinary progression nodes unless a verified boss or authored optional/trivial rule overrides that default. The project leaves every `ScalingBase` unset: the shipped DS1TR option contract does not enable FogMod's debug-only `scalingbase`/`dumpdist` flags, and a graph test verifies that these overrides are ignored under shipped options. A reference-only scaling-base difference is therefore behavior-equivalent for production generation and is not imported as balancing policy.

## Fog project-policy authoring V1 (in progress, 2026-10-13; desktop 0.39.0 retained)

1. **Policy membership and values are project-owned.** Pristine MSB/EMEVD/GameParam/English FMG facts validate authored values; `fog.txt` is consulted only after the project inventory is fixed. A mismatch with the reference is not by itself a reason to remove a valid project rule.
2. **Promote boss collision mappings only with explicit stable anchors.** `DefaultFlagCols` entries name the expected completion event and declare whether the anchor is the same-collision boss enemy, the encoded negative `PlayRegionID`, or both. The resolver validates the unique event-970 completion flag. The 3 independently anchored project-only collision cases are retained as intentional B policy; no event is guessed when an anchor is ambiguous.
3. **Separate item identity from progression placement.** A typed identity with exact GameParam ID, FMG name, and pristine possession-check events is useful progress, but does not make a full Fog `KeyItem`; default Area and required/random-only/hard intent are separate authored fields. ESD/ObjAct progression inventory remains open.
4. **Keep Area V1 complete as a per-ID policy record while exposing unknown routes.** All 131 stable IDs receive a map-family owner, classification, rationale and at least a map-membership anchor. Existing boss, start, gate and static-link anchors are added where established. The current two `Area.To` links are explicit; an empty link collection is not treated as proof that a physical route does not exist. Unresolved transition ownership stays explicit until the Entrance/Warp families are authored.
5. **No production catalog or compatibility switch follows from these foundations.** The `FogProjectAreaPolicyV1` and item identities are development policy inputs; they do not change the current catalog profile, generation semantics, Seed Codes, backend projection, downloader or public behavior. Production migration remains gated on complete consumers, progression matrix, deterministic reruns, understood semantic B cases and compatibility review.

## Nexus generation preflight and frozen Fog profiles (2026-10-14)

1. `/api/world-data/prepare` resolves the generation profile from a supplied Seed Code; without one it uses the current profile. This keeps prerequisite preparation aligned with the exact Fog data/provider selected for the run.
2. Nexus does not download upstream Fog data. The current v3 path reconstructs its 58 inputs from the verified local DSR snapshot. Legacy v1/v2 still require their hash-verified Fog data cache.
3. Keep the v1/v2 FogHost compatibility gate at 0.5.0. Golden verification found output drift with 0.6.0, so the newer host must not be admitted under frozen Seed Codes. The current Nexus package contains only 0.6.0; legacy Fog support is a release blocker until a separately verified compatible host path is packaged. Do not rewrite fixtures or disguise a changed host as 0.5.0.

## Frozen FogHost runtime routing (2026-10-15)

1. Keep the exact historical FogHost 0.5.0 tree as a pinned build input, not as a hand-maintained source fork. The source identity is commit `b2ba7d6148810b367bd01d955688364cad30c845`, subtree `tools/fog-host`, tree `438b66455982060760419a755283acaa23997979`; `tools/fog-host-legacy/source-pin.json` is the versioned identity record.
2. Select the generation executable and wire schema from the frozen Seed Code profile components. `ds1tr-gen-v1/v2` use 0.5.0/schema 2 and the legacy 59-file world data plus legacy item tracking. `ds1tr-gen-v3/v4` use 0.6.0/schema 3 and Compact data plus Compact item tracking. Missing selected hosts fail closed; there is no cross-version fallback.
3. FogHost 0.5.0 predates `progression-model`. For key-placement runs only, 0.6.0 may export the read-only model from the legacy data root. The generation executable remains 0.5.0, and this helper does not write or transform Fog outputs. The package still requires both hosts; Compact reconstruction and Compact projection remain on 0.6.0.
4. Nexus does not download or bundle legacy Fog data. A v1/v2 Fog run there requires a complete hash-verified local 59-file cache. V3 remains independently reconstructable from the player's verified DSR files with zero downloads.

# Owner-authored roaming boss minimum fit classes (2026-10-15)

1. Keep the existing class-based fit model and interpret a boss class as its minimum destination class. The owner sets Artorias, Capra Demon, Pinwheel, and Dark Sun Gwyndolin to class 0, and Taurus Demon to class 2. No other boss classification or explicit exclusion changes; Moonlight Butterfly keeps its authored class-3 override.
2. The old local classification is preserved before mutation in `docs/placement-audits/boss-fit-classification-pre-nexus-2026-10-15.md` and `.json`, with the real-catalog before/after evidence in `boss-fit-validation-2026-10-15.json`. Do not treat this local pre-Nexus snapshot as a generation input.
3. Pinwheel Chaos remains an arena encounter option. Its gate still applies to arena boss sources, but roaming boss candidate construction includes Pinwheel independently of that gate.
4. Gwyndolin uses the generic roaming implementation: destination-team override, scaled NpcParam, cloned destination-perception ThinkParam, generated wake-up event, ordinary soul scaling and stay-dead policy. No arena EMEVD or arena object dependency was found in the output path. In-game movement/collision safety is unproven until owner QA.
5. No geometry engine is introduced. No Seed Code or profile encoding changes; the RNG salt is unchanged. Since this is before the first Nexus release and the old fit behavior has no external Nexus users, the current worktree applies the corrected classes without retaining the old local output as a compatibility mode.
