# Security and privacy

This page describes the network and local-file behavior of DS1 Multi Randomizer. See [Release trust](RELEASE_TRUST.md) and [Code signing](CODE_SIGNING.md) for distribution and signing status.

## Network behavior

- **No telemetry, accounts, analytics, crash reporting, or automatic support uploads.** The project has no application server. The app does not send the user's game files, settings, seeds, run history, or support bundle to the project.
- **Update checks contact GitHub Releases.** Automatic checks are enabled by default and can be disabled in the app's Updates settings; a manual check remains available. A check reads public release metadata. It does not download an update until the user chooses **Download update**. Installation requires a later **Restart and update** action. GitHub can see ordinary connection metadata such as the source IP address and request timing.
- **Fog data is fetched only when needed.** When Fog/world randomization is used and its cache is missing, the backend downloads 40 data files over HTTPS from the pinned `thefifthmatt/FogMod` commit on `raw.githubusercontent.com`. Every file is checked against a built-in SHA-256 before use. The files are cached locally; subsequent runs can work offline. GitHub can see ordinary connection metadata for these requests.
- **Support bundles are user initiated.** Export creates a local ZIP at a location chosen by the user. It is not uploaded by the application.
- **Local state stays on this computer.** Settings, run history, presets, and game path are stored under `%APPDATA%\ds1-triple-randomizer-desktop`. Seed Codes contain settings and a master seed, not personal information.

## Changes to the computer

- Generation uses a separate workspace. The application imports a pristine snapshot and only replaces managed Dark Souls Remastered files when the user chooses **Apply to game**. **Restore original game** uses that snapshot as the single restore source.
- The application does not install services, scheduled tasks, startup entries, drivers, firewall rules, or registry configuration beyond the installer's per-user uninstall entry and shortcuts. It runs as the current user and does not request administrator access.
- The app recommends offline play when the game contains randomized data.

## Process model

The Electron renderer is sandboxed, has context isolation enabled, and has no Node integration. It cannot write game files, start processes, or choose arbitrary network destinations. The backend owns game-file operations and starts `DsrDataTool.exe` and `FogHost.exe` with fixed executable paths and argument arrays, without a shell. It also uses fixed Windows commands (`reg query`, `tasklist`, and `taskkill`) for game detection and child-process cleanup. The application does not invoke PowerShell, `cmd.exe`, or a scripting host.

The local backend API listens on `127.0.0.1` on a random port and checks a per-session token. It is not exposed to other computers on the network.

## Releases and verification

Public releases are currently **unsigned**. A valid SHA-256 match proves that a file matches the published checksum; it does not prove who built it or certify that it is safe. The release manifest and `SHA256SUMS.txt` are published beside the files. Follow [the verification instructions](ANTIVIRUS_FALSE_POSITIVES.md) and do not disable security software or add exclusions.

The source repository is public under GPL-3.0-only. The release workflow records the source commit and checks package contents, versions, update source, and hashes. See [Release trust](RELEASE_TRUST.md) for the current limits of those checks.

## Reporting an issue

For a suspected tampered release, preserve the file, calculate its SHA-256, and compare it with the checksum for the same version. For a detection that appears incorrect, record the security product, detection name, alert identifier, file version, release URL, and hash, then use that vendor's official submission process. Never share private game files or a support bundle unless you have reviewed it and chosen to do so.
