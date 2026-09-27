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
