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
