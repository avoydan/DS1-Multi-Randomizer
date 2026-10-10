# Release trust and Windows security

## Current status

- Public Windows releases are **unsigned**. No trusted certificate or approved SignPath project is active. The planned v0.38.3 release remains unsigned unless a future, separately verified signing setup is approved.
- An unsigned download can show “Unknown publisher” in Windows SmartScreen and can receive heuristic antivirus warnings. A checksum match establishes file integrity against the published checksum; it does not authenticate the publisher or certify that the program is safe.
- We do not advise disabling antivirus, adding exclusions, bypassing SmartScreen, packing/obfuscating executables, or using UPX.

## What is distributed

The source of truth is [`tools/release/release-policy.json`](../tools/release/release-policy.json). Paths below are relative to the installed/unpacked app unless noted.

| File | Origin and role | Present signing status | Future SignPath treatment |
| --- | --- | --- | --- |
| `DS1 Multi Randomizer.exe` | Electron executable with this app's name, icon, and resources; Electron itself is upstream | Unsigned in current public builds | Do not sign as a first-party binary without SignPath's written approval; it is based on upstream Electron. Prefer an upstream signature if packaging can preserve one. |
| `resources/runtime/engines/combined/vendor/tools/DsrDataTool/DsrDataTool.exe` | .NET tool built from the selectively vendored GPL `kellernz1/ds-randomizer` code plus project changes | Unsigned | Approval-dependent modified-upstream binary. A project-controlled public fork/source relationship and the Foundation's other upstream conditions are not yet satisfied/verified. |
| `resources/runtime/tools/fog-host/FogHost.exe` | Project host compiled with selected, modified FogMod sources and SoulsIds sources | Unsigned | Approval-dependent modified-upstream binary. Do not sign until eligible upstream signed builds, visible fork lineage, and change-review requirements are verified. |
| `DS1-Multi-Randomizer-<version>-Setup-x64.exe` | Project's electron-builder/NSIS installer | Unsigned | Possible project package candidate, subject to SignPath acceptance of the artifact configuration and its embedded executables. |
| `DS1-Multi-Randomizer-<version>-Portable-x64.exe` | electron-builder portable wrapper/package | Unsigned | Possible project package candidate, subject to the same review. |
| `Uninstall DS1 Multi Randomizer.exe` | Generated as part of the NSIS installation | Unsigned | Approval-dependent generated executable; not currently submitted for signing. |
| Electron/Chromium DLLs and `d3dcompiler_47.dll` | Upstream runtime files; Microsoft signs `d3dcompiler_47.dll` | Retained as provided by their respective publishers | Never sign third-party binaries with this project's certificate. Preserve any upstream signature. |

The package also includes source notices and license texts for GPL-3.0-only code, Apache-2.0 FogMod/SoulsIds, and MIT NuGet dependencies. The combined engine and FogHost pin documents list pinned commits, modified files, and third-party terms. Dark Souls Remastered files and FogMod's game-derived `dist/` data are not included. The user supplies their own game files and the backend downloads the required pinned Fog data on demand with SHA-256 checks. See [`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md), [`docs/LICENSING.md`](LICENSING.md), and the two `UPSTREAM_PIN.md` files.

## Runtime and installer behavior checked in source

- The installer is per-user and does not request elevation. It creates shortcuts and a per-user uninstall entry. There are no services, scheduled tasks, startup entries, drivers, firewall changes, or hidden persistence.
- The portable build extracts to a temporary folder when it starts. This is an electron-builder behavior and can attract heuristic attention; it remains available as a secondary download, while the installer is recommended.
- The app starts the two .NET helpers with fixed paths and argument arrays, without a shell. It also uses fixed `reg query`, `tasklist`, and `taskkill` commands. The app does not call PowerShell, `cmd.exe`, `wscript`, `mshta`, `rundll32`, or `certutil`.
- Fog/world randomization downloads 40 pinned data files from `raw.githubusercontent.com` if the local cache is absent. The updater contacts GitHub Releases for manual checks and, when enabled, automatic checks; it downloads only after the user requests an update. No application data is uploaded to the project.
- Applying a generated world replaces managed game data only after the user chooses **Apply to game**. The app imports a pristine snapshot for **Restore original game**. Those game-file writes, large self-contained .NET helpers, lack of a signing identity, portable extraction, and HTTPS downloads are expected product behavior, not AV workarounds to remove.

## Dependency audit and renderer boundary

`npm audit --package-lock-only` on 2026-10-10 reported 14 advisories in the lockfile (2 critical, 4 high, 8 moderate). The count includes build and development dependencies; it is not a count of vulnerabilities in the shipped application. The installed Electron runtime is `34.5.8`, and the audit reports multiple Electron advisories. Electron's release schedule no longer lists that major in a supported line.

The app's packaged window uses `contextIsolation: true`, `nodeIntegration: false`, and `sandbox: true`; it loads the bundled local `index.html`, exposes a narrow Promise-returning preload API, and does not register custom protocols, use `<webview>`, or install permission handlers. The GPL license link was the one external page requested in a child window. v0.38.3 now sends only that exact URL to the system browser, denies every Electron child window, and blocks renderer navigation or redirects away from the app document. Unit tests cover the URL policy and that guards are installed before loading content.

The audit's critical `concurrently`/`shell-quote` findings and high `extract-zip`, `http-cache-semantics`, and `source-map-js` findings are in development/build tooling rather than code paths shipped as app features; no untrusted command input or untrusted archive extraction path is exposed by this app. This focused mitigation does not make Electron 34.5.8 vulnerability-free and is not a substitute for moving the packaged runtime to a currently supported Electron line. A major Electron upgrade was not mixed into this updater patch because it needs its own compatibility and packaged-app verification.

## File metadata and integrity checks

The application and helpers use truthful project metadata: `DS1 Multi Randomizer` product name, `DS1 Multi Randomizer project` company/publisher text where the format requires a value, file descriptions, version, and copyright. No game publisher is named as the software publisher. `release-verify.mjs` checks the executable metadata and expected product versions. The project does not claim that “DS1 Multi Randomizer project” is a registered company.

The release workflow builds from the matching source commit and checks the update owner/repository, tag/version, package files, `latest.yml` against the installer, manifest hashes, and release assets. `SHA256SUMS.txt` is generated from the final staged files. The updater verifies the downloaded installer against `latest.yml` SHA-512; this is integrity checking, not a publisher identity check.

Use [ANTIVIRUS_FALSE_POSITIVES.md](ANTIVIRUS_FALSE_POSITIVES.md) for checksum instructions and vendor submission steps. The current release is not represented as signed or antivirus-cleared.

## Build pipeline and permission review

`.github/workflows/release.yml` runs only by manual `workflow_dispatch` on a GitHub-hosted Windows runner. Workflow-wide `GITHUB_TOKEN` permission is `contents: read`; only the release job gets `contents: write`, to create a draft and upload its assets. The workflow uses `npm ci` with `package-lock.json`, Node 24, .NET 8, runs backend tests, desktop typecheck/tests, builds the public unsigned package, runs the release verifier, and stages hashes from the finished package. The four GitHub Actions in this workflow are pinned to full commit SHAs. The .NET selector is `8.0.x`, so the SDK patch version follows Microsoft's current 8.0 servicing release; the current manifest does not record the exact SDK patch, and no NuGet `packages.lock.json` was found in the helper projects. Treat those as provenance limits to resolve before adding a signing workflow. Builds are not claimed byte-for-byte reproducible.

The workflow does not print signing secrets because it has no signing secrets and signing is not configured. The optional release-repository token is not needed for this single-repository setup. No secrets are embedded in the app. The renderer cannot provide an updater URL or token; the updater source is explicit in `updateSource.ts` and `electron-builder.yml` and the release verifier checks their agreement with `avoydan/DS1-Multi-Randomizer`.

For the readiness assessment and proposed signed-artifact flow, see [CODE_SIGNING.md](CODE_SIGNING.md). Signing is not enabled by these documents, and the existing unsigned release workflow remains the active path.
