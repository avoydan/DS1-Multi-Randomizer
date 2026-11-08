# Project workspace and repository layout

## One location, one repository

The only project location is `C:\Users\Adam\VisualStudioProjects\ds1-triple-randomizer-v5-isolation` (the folder name is historical and is not to be renamed). It is a **standalone Git repository**: its `.git` is the real git directory (`git rev-parse --git-common-dir` prints `.git`), not a pointer into another checkout, and it has no linked worktrees. History, branches, tags and the `origin` remote are the repository's; nothing lives in a sibling folder.

Consequences, as policy:

- **No sibling copies.** No `…-backup-…`, `…-pre-…`, `…-v5-…` or similar folders next to the project, and no extra clones, anywhere under `C:\Users\Adam\`. A snapshot of old source is a Git commit, tag or branch, not a copied directory.
- **Parallel development uses branches.** If a second working tree is ever needed, use `git worktree add .workspace/worktrees/<name> <branch>` (inside the project, git-ignored) and remove it with `git worktree remove` when done; never a clone or a copied folder.
- **Project-owned temp, QA, tooling and archive material lives inside the project**, under `.workspace/`. Nothing of the project is kept in the user profile (no `ds1tr-*`, `dn8sdk*`, `fog-host*`, `fogmod*` folders under `C:\Users\Adam\`). Genuinely ephemeral files a test creates and deletes itself may use the OS temp folder.
- Before a destructive cleanup, make a temporary `git bundle create <file> --all`, verify it, and delete it once the result is verified; do not leave a second full copy beside the project.

## `.workspace` (git-ignored)

| Folder | Purpose |
|---|---|
| `.workspace/sdk` | the project's .NET 8 SDK (`dotnet.exe`, 8.0.425); a short enough path for the SDK's deepest files (`docs/DECISIONS.md`: MAX_PATH) |
| `.workspace/nuget` | `NUGET_PACKAGES` for builds with that SDK |
| `.workspace/tooling` | other tooling state (`dotnet-cli-home` = `DOTNET_CLI_HOME`) |
| `.workspace/qa` | QA data that is not source: `game-inst/DARK SOULS REMASTERED` (a clean read-only subset of the game files, the source for golden fixtures and real-generation tests: `DS1TR_GOLDEN_GAME_SOURCE`), `ceaseless-attestations` |
| `.workspace/tmp` | scratch space for QA runs and builds (`TEMP` in `tools/devenv/dev-env.ps1`) |
| `.workspace/cache` | caches that may be deleted at any time |
| `.workspace/archive` | evidence extracted from retired folders (see its `README.md`); not needed to build, test or release |
| `.workspace/worktrees` | linked worktrees, only if ever needed |

`.workspace/` is in `.gitignore`. To use it in a shell: `. .\tools\workspace\dev-env.ps1`. `apps/desktop/scripts/package-win.mjs` and `tools/release/helper-format-experiment.mjs` look for `.workspace/sdk/dotnet.exe` first (override with `DS1TR_DOTNET_SDK`, else `dotnet` from PATH).

A new machine: clone the repository, install the SDK into `.workspace/sdk` (`dotnet-install` with `-InstallDir`), copy or create `.workspace/qa/game-inst/DARK SOULS REMASTERED` from a clean game install (`DarkSoulsRemastered.exe`, `event`, `map`, `msg`, `param`, `paramdef`, `script`, `sfx`).

## What was retired (2026-11-08)

The former main checkout `ds1-triple-randomizer` (it held the git directory the project was a worktree of), the two dated backup folders and the QA/tooling folders in the user profile (`ds1tr-qa`, `ds1tr-ui`, `dn8sdk*`) were folded into this layout. Older documents (`docs/IMPLEMENTATION_STATUS.md`, `docs/CEASELESS_QA_TELEMETRY.md`, audits) still quote the old absolute paths as the record of what was run; they are history, not instructions. The Ceaseless telemetry tool (`tools/qa-telemetry`) now points at `.workspace/qa/...`; its two 7 GB game copies were not kept (they are regenerable build output of that investigation; the attestations were).
