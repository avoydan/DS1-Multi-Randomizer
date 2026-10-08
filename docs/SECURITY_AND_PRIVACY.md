# Security and privacy

What DS1 Multi Randomizer does with your computer, your files and the network. The signing and antivirus side is in `docs/RELEASE_TRUST.md`.

## Privacy

* **No telemetry, no accounts, no analytics, no crash reporting, no update check.** The application never sends information about you, your game, your settings or your runs anywhere. There is no server of ours.
* **One network request, only if you switch the world randomizer (Fog) on:** a one-time HTTPS download of 40 data files from the pinned FogMod commit on `raw.githubusercontent.com`. Each file is checked against a built-in SHA-256 before it is used; the files are cached in the user data folder and the app works offline afterwards. GitHub sees an ordinary download request from your address. Nothing is uploaded.
* **Seed Codes are not secret and contain no personal data:** they hold the options and the master seed. Run history, settings and the game path are stored only on your computer, in the user data folder (`%APPDATA%\ds1-triple-randomizer-desktop`, the same for the installed and the portable build).
* **The local API** the interface talks to listens on `127.0.0.1` only, on a random port, and every request needs a per-session token that only the application's own window has. It is not reachable from other computers.

## What it changes on your computer

* **Your Dark Souls Remastered installation, and only when you press Apply:** the managed data files of the game are replaced by the generated package. Before the first run the application imports a **pristine snapshot** of the original files and keeps it as the single restore authority; **Restore original game** puts the original files back. Generation happens in a separate workspace and never touches the game folder; activation is refused when the game folder, `DarkSoulsRemastered.exe` or the pristine snapshot cannot be verified, or when the state is unknown. The application's tests and the packaged-app checks use copies of the game, not your installation.
* **Nothing else:** no registry entries (the installer adds one per-user uninstall entry and shortcuts), no services, no scheduled tasks, no startup entries, no drivers, no firewall rules. The application never asks for administrator rights.
* **Play offline.** Modified game data and online play do not mix; the application recommends offline play and does not touch the game's online components.

## Process model

* The window (renderer) is sandboxed with context isolation and no Node integration; it can only call the typed backend API and pick folders through a small preload bridge. It cannot read or write game files, run programs or import code.
* The backend (Node code run by Electron) owns all file operations. It starts two helpers with argument arrays and no shell: `DsrDataTool.exe` (reads and patches game data in the workspace) and `FogHost.exe` (the fog randomizer). It also uses `reg query` (find Steam), `tasklist` (is the game running) and `taskkill` (stop its own children), each with fixed arguments. No PowerShell, `cmd.exe` or scripting host is used by the application. The release tooling (`tools/release`) uses Windows PowerShell only on the build machine, to read Authenticode signatures, and never ships.
* Everything runs as the user who started it (`asInvoker`).

## Supply chain

* The generated worlds depend only on the code in this repository, the vendored engines (GPL combined engine, FogMod under Apache-2.0, pinned commits listed in the `UPSTREAM_PIN.md` files) and the packages in the lock file. Release builds record the commit, the versions and the SHA-256 of every executable (`release-manifest.json`).
* Builds are **not code-signed yet**; check the published SHA-256. See `docs/RELEASE_TRUST.md` for what that means and for the plan.

## Reporting a problem

If you think a build was tampered with or flagged wrongly, keep the file, note its SHA-256 and compare it with the published manifest; do not disable your antivirus. Open an issue in the project repository with the hash and the product that flagged it.
