# Upstream pin

Source: https://github.com/kellernz1/ds-randomizer
Pinned commit: `f120efe0e95b957b7e76109868c3c9966e22720c`
Commit date: 2026-08-20T16:13:04-03:00
Commit subject: "Simplify README and add Nexus download"
License: GPL-3.0-only (see `LICENSE` in this directory)

This is a **selective** vendor, not a full mirror: only the files required
to build `DsrDataTool.dll` (the game-data extraction/patch tool) are
vendored, per `docs/COMBINED_ENGINE_INTEGRATION.md`. The upstream JS core
(`src/core/*.js`) is analyzed there but intentionally **not** vendored yet
— it will be added in the task that wires the in-process `generate()` call,
to avoid vendoring files this repository does not yet import or build.

Every file below was verified byte-identical to the pinned commit at
vendor time (`diff -q` against a clone of that commit).

## Vendored paths

| Vendored path | Upstream path | Purpose |
|---|---|---|
| `LICENSE` | `LICENSE` | GPL-3.0-only license text (must be preserved). |
| `THIRD_PARTY_NOTICES.md` | `THIRD_PARTY_NOTICES.md` | Upstream's own third-party notice (`JuicerMV.SoulsFormats`, GPL-3.0-only) — must be preserved and mirrored into this repo's own `THIRD_PARTY_NOTICES.md`. |
| `NuGet.Config` | `NuGet.Config` | Pins package restore to `nuget.org` only, matching upstream's reproducible-build configuration. |
| `scripts/install-dotnet.ps1` | `scripts/install-dotnet.ps1` | Installs .NET 8 SDK locally into `.tools/dotnet` under this vendor directory (no admin rights, no global `PATH` change, no system-wide install). |
| `scripts/dotnet-local.ps1` | `scripts/dotnet-local.ps1` | Thin wrapper that runs the locally-installed `dotnet.exe` with isolated `DOTNET_CLI_HOME`/`NUGET_PACKAGES` environment variables, so builds never touch the developer machine's global NuGet cache or dotnet CLI state. |
| `tools/DsrDataTool/DsrDataTool.csproj` | `tools/DsrDataTool/DsrDataTool.csproj` | The .NET 8 project file for the data tool (`net8.0`, single dependency: `JuicerMV.SoulsFormats` 0.1.3). |
| `tools/DsrDataTool/Program.cs` | `tools/DsrDataTool/Program.cs` | The data tool's full implementation (`scan`, `patch-enemies`, `install`, `restore`, `inspect-boss-bars` commands). Vendored in full because the project file only builds if the source is present; `install`/`restore` remain in the binary but are never invoked by this repository (see `docs/COMBINED_ENGINE_INTEGRATION.md`, "Forbidden upstream calls"). |

## Not vendored (and why)

- `src/**` (Node/Electron core, CLI, server, desktop) — not yet imported or
  built by this repository; deferred to the task that wires `generate()`.
- `public/**`, `package.json`, `package-lock.json` — Electron/browser
  configurator UI; out of scope (`AGENTS.md`: "No automation of legacy
  GUIs when callable engine code exists").
- `test/**` — upstream's own test suite; not exercised here.
- `README.md` — informational only, not a license or notice file.

## Updating this pin

1. Re-clone `kellernz1/ds-randomizer` at the new commit.
2. Re-copy only the paths listed above, verifying each with `diff -q`
   against the new clone.
3. Update the commit hash/date/subject above.
4. Re-run the build in `docs/COMBINED_ENGINE_INTEGRATION.md` ("Vendored
   build proof") and confirm `DsrDataTool.dll` still produces successfully.
5. Record the change in `docs/DECISIONS.md` if the update changes any
   documented behavior (command names, file paths, schema version).

## Known gap

`docs/LICENSING.md` requires "a public source tag matching each
distributed binary." No public fork of `kellernz1/ds-randomizer` has been
created yet — that is an account-owning action (creating and maintaining a
fork under a project-controlled GitHub account) outside what this task
vendors locally. This vendor directory satisfies "exact pinned commit" and
"preserved upstream copyright/license notices" today; the public fork
remains open before any binary built from this vendor directory is
distributed to end users.
