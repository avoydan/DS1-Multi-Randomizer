# Codex progress report

## 1. Analysis completed

- Read the project instructions and architecture documents.
- Inspected the existing Electron/React/TypeScript/Vite frontend-backend split.
- Compared `kellernz1/ds-randomizer` with both legacy Item and Enemy randomizers.
- Inspected FogMod's configuration and relevant compatibility code.
- Assessed feature parity, modified game formats, generation flow, output model,
  seeds, activation/restore, FogMod compatibility, and licensing.
- Reviewed the first implementation changes with the six-step workflow and an
  architect review.

## 2. Concrete findings

- `kellernz1/ds-randomizer` is the approved primary Items + Enemies direction,
  but it is not yet a complete behavioral replacement for the legacy tools.
- Missing legacy-visible Item options include Race/Race+, difficulty modes,
  soul transformations, Fashion Souls, Laundromat Mixup, and Lordvessel/Lord
  Soul controls.
- Missing legacy-visible Enemy options include cross-category replacement,
  percentages, fit modes, custom pools, unique/respawning bosses, hostile NPCs,
  and several special-case toggles.
- Combined and FogMod both modify MSB, EMEVD, and GameParam data. FogMod may
  overwrite combined event changes, so a compatibility bridge is required.
- Combined should receive the user's normalized master seed directly. Fog uses
  its own deterministically derived stream.
  *(Superseded 2026-09-28, Task 6: combined now receives a named SHA-256
  derivation of the master seed; see `docs/DECISIONS.md`.)*
- Combined is GPL-3.0-only. FogMod is Apache-2.0. The legacy Item and Enemy
  repositories have no explicit repository license and must remain behavior
  references unless written permission is obtained.
- No game assets or unlicensed upstream source were added.

## 3. Files and repositories inspected

Project files:

- `AGENTS.md`
- `README.md`
- `docs/ARCHITECTURE.md`
- `docs/MOD_ANALYSIS.md`
- `docs/source-analysis.md`
- `docs/roadmap.md`
- root and workspace `package.json` files
- shared contracts
- backend pipeline, seed, installation, server, and adapter files
- desktop React application, API client, and styles

Upstream repositories:

- `kellernz1/ds-randomizer`
- `HotPocketRemix/DarkSoulsItemRandomizer`
- `everKalle/Dark-Souls-Enemy-Randomizer`
- `thefifthmatt/FogMod`

Relevant upstream areas included configuration, RNG, generation, output,
activation/restore, Item options, Enemy GUI/core options, FogMod
`RandomizerOptions`, `MainForm`, `ItemReader`, and `GameDataWriter`.

## 4. Unfinished work

- Runtime request validation was being tightened to reject unknown fields,
  missing fields, and incorrect primitive types. The stricter implementation
  was added, but its new negative test and final verification were interrupted.
- The Fog language setter still needs the already identified TypeScript
  narrowing/cast.
- A few remaining documentation contradictions must be removed, especially
  legacy reuse wording in `AGENTS.md` and stale retry/manifest examples.
- The full build and typecheck remain unverified because dependency installation
  was blocked by an npm TLS certificate error.
- No combined engine fork, adapter execution, workspace implementation, Fog host,
  or compatibility bridge has been implemented yet.
- No public release or game-file generation has been attempted.

## 5. Modified files

Yes. The working tree contains uncommitted changes to:

- shared option and pipeline contracts;
- backend seed derivation, pipeline planning, validation, server handling, and
  tests;
- desktop settings UI and styles;
- package versions and license metadata;
- README and project documentation.

No commit was created because the critical review still had unresolved blockers.

## 6. Documentation created or updated

Created:

- `LICENSE` with the full GPLv3 text;
- `THIRD_PARTY_NOTICES.md`;
- `docs/LICENSING.md`;
- `docs/OPTION_MATRIX.md`;
- `engines/combined/README.md`;
- this progress report.

Updated:

- `README.md`;
- `docs/roadmap.md`;
- `docs/source-analysis.md`;
- `docs/ARCHITECTURE.md`;
- `docs/MOD_ANALYSIS.md`.

## 7. Smallest next task

Finish the interrupted validation slice only: add the malformed-payload test,
apply the Fog language type narrowing, align the few stale documentation lines,
then run the existing tests and typecheck when npm dependencies are available.
Do not begin the combined adapter or Fog compatibility bridge before that slice
is clean and committed.
