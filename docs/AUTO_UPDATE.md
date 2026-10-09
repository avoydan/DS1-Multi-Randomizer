# Updates and releases (GitHub Releases, no server of our own)

Status labels: **STATICALLY VERIFIED**, **AUTOMATED VERIFIED**, **PACKAGED VERIFIED**, **GITHUB VERIFIED**, **END-TO-END VERIFIED**, **DEFERRED**, **UNSUPPORTED**. Section 12 lists what has been proven and what has not; the updater is **not production-ready until an installed version N has updated itself to N+1 from the real update source** (section 11, QA).

Short checklist for every release: [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).

## 1. Architecture

```
Developer ── version commit ──> GitHub repository
                                     │  Actions > "Release (draft)" > Run workflow   (by hand, never on push)
                                     v
                          GitHub Actions, windows-latest
                          npm ci, backend tests, typecheck, desktop tests, package, verify
                                     │
                    electron-builder (ONE build) writes together:
                    NSIS installer · portable EXE · latest.yml · installer .blockmap
                    + release-manifest.json (hashes, provenance)
                                     │
                                     v
                          GitHub DRAFT release  (visible only to people with write access)
                                     │  OWNER downloads, tests, reviews, clicks "Publish release"
                                     v
                          Public GitHub release  (the newest non-prerelease one is "Latest")
                                     │
                                     v
        Installed DS1 Multi Randomizer ── checks github.com/<owner>/<repo>/releases/latest ──> latest.yml
                                     │  newer? "Download update" > progress > "Restart and update"
                                     v
                          NSIS installer replaces the application (settings, history and the game are untouched)
```

No VPS, no custom API, no database, no update server. The only network address an installed client contacts for updates is `github.com/<owner>/<repo>/releases` (the same host serves the files). Nothing is sent about the person or the computer except what any HTTPS request to github.com carries.

## 2. Where updates come from: the update source

The update source is written **explicitly** in two places that must agree, and never inferred from `.git/config` or from the CI environment:

| Place | Content |
|---|---|
| `apps/desktop/electron/updateSource.ts` | `provider: 'github'`, `owner`, `repo`: what the application passes to the updater (`setFeedURL`) |
| `apps/desktop/electron-builder.yml` (`publish:`) | the same three values; electron-builder writes them into `resources/app-update.yml` of the package |

`tools/release/release-verify.mjs` fails when they differ, when `app-update.yml` of the package says something else, or (for a build that is going to be published) while the owner is still the placeholder `REPLACE_WITH_GITHUB_OWNER`. A build with the placeholder has **no update source**: it never contacts GitHub, the Updates card says so, and a public release cannot be made from it.

### Which repository? (an owner decision, with a recommendation)

No git remote is configured in this working copy, so whether the source repository is public is not known here.

* **Recommended: one public repository** that holds the source and the releases. DS1 Multi Randomizer is GPL-3.0-only and its own notices promise "corresponding source with every release"; distributing binaries publicly means the recipients must be able to get the matching source (GPL-3.0 section 6; this is not legal advice). One public repository does that with no extra work, needs **no secret** (the workflow's own `GITHUB_TOKEN` is enough) and gives every release a tag that points at its source commit.
* **Alternative: a separate public release repository** (conceptually `DS1-Multi-Randomizer-Releases`), if the source repository must stay private. It works for updates (the clients follow the release repository), but: the workflow then needs the secret `RELEASE_REPO_TOKEN` (a fine-grained token limited to that one repository, Contents read/write) and the variable `RELEASE_REPOSITORY`, the tag of a release is only a label, and the GPL source obligation still has to be met somewhere else (a link or a written offer in the release notes). Do not choose this only to avoid publishing source.

Either way the application contains **no token** and a normal user never authenticates to GitHub: public releases are read anonymously.

### Treat the update source as infrastructure

The owner/repository pair is baked into every installed copy. If that repository is **renamed**, GitHub redirects old release URLs for a while, but an updater following a redirect chain is not something to rely on; if it is **transferred**, redirects exist only until a new repository takes the old name; if it is **deleted**, every installed copy silently stops finding updates and can only be rescued by a manual download. So: choose the name once, do not change it after the first public release, and if it must ever change, publish a final release **in the old repository** whose application points at the new one, and keep the old repository alive until the installed base has moved.

## 3. What the clients do

| | Installed (NSIS) | Portable |
|---|---|---|
| Looks for updates | yes, automatically (section 4) and by hand | yes, by hand and automatically |
| Downloads | yes, on request, with progress; differential download through the blockmap when possible | **no** |
| Installs | yes, on request: "Restart and update" runs the installer silently and starts the new version | **no**: it never replaces its own running EXE |
| Offers instead | | "Open the download page": the trusted release page (built from the update source, never read from release metadata) |
| Text | "DS1 Multi Randomizer X.Y.Z is available." | "... Portable version must be updated manually: download the new version, then replace this file." |

A development run (`npm run dev`) and a build without an update source do not update at all ("unsupported"). A **public** build is stable-only: `allowPrerelease = false`, `allowDowngrade = false`, and semver-suffixed versions are refused. An **internal** build opts into GitHub prereleases for controlled QA; it still refuses downgrades and semver suffixes.

The GitHub **pre-release** checkbox is separate from a semver suffix. Release tags and `latest.yml` stay numeric (`vX.Y.Z` / `X.Y.Z`); marking that GitHub release as a prerelease keeps it out of the stable `/releases/latest` feed. Public artifacts are always built with `--channel=public`. Internal QA packages (`--channel=internal`) may read the newest GitHub release feed. Missing or unreadable build metadata defaults to `public`, and the updater loads the channel before it is configured. A public N+1 update replaces the internal N test build with a public build, so later prerelease checks require installing a fresh internal baseline.

## 4. Checking policy

* **Automatic checks are ON by default**, set in App settings > Updates ("Automatic update checks"). The first one runs about 20 seconds after the window is up (never on the first paint), and a session left open checks again every 6 hours **but** the throttle lets a real request through at most **once per 24 hours after a successful check and once per hour after a failed one**. The time of the last check is kept in the settings file (`updates.lastCheckAt`, `updates.lastCheckOk`).
* **Check for updates** (App settings and About) always checks, ignoring the throttle.
* Changing pages never triggers a request. Concurrent requests share one.
* The updater uses `github.com/<owner>/<repo>/releases/latest` and `releases.atom`, not the REST API, so there is no API rate limit for anonymous users.

## 5. The user experience

1. A one-line notice appears at the top of the page ("Update available: version X.Y.Z.") with **Download update**, **What's new** (opens the trusted GitHub release page) and **Later**. The same card is in App settings and About.
2. While downloading, a progress bar. A failed check or download is a calm sentence ("Unable to check for updates. You can keep using the app."); the application works exactly as before.
3. When the download is complete: "Update ready. DS1 Multi Randomizer X.Y.Z starts after the restart." with **Restart and update** and **Later**.
4. **Nothing installs without the click**: `autoInstallOnAppQuit` is off, so closing the window never installs anything, and nothing restarts the application by itself.

### An update never interrupts a game-file operation

Before it restarts, the shell asks the backend (`GET /api/activity`) whether a **generation, Apply, Restore, import of the pristine copy, preparation of the world data, support bundle or deletion of a run** is running. If one is, "Restart and update" does nothing except show "Finish the current operation before updating." and the downloaded update waits. If the backend is running but cannot be asked, the answer is "busy", never "idle"; if no backend is running, nothing is running. (A request that starts in the fraction of a second between the answer and the restart is the one case this cannot exclude; the backend's own writes are atomic where it matters, see docs/ARCHITECTURE.md.)

### An update never touches the game

An applied randomized world lives in the game folder, independent of the application process. Updating DS1 Multi Randomizer replaces the application only: it does not restore, re-apply or edit game files, and it keeps the settings, run history, presets and state (they live in the user data folder, which an update and an uninstall both keep).

## 6. Security and integrity

* **Integrity of the download.** `latest.yml` carries the sha512 and size of the installer; electron-updater verifies the downloaded file against it and rejects a mismatch. The metadata is fetched over HTTPS from github.com.
* **IPC.** The page can call six functions and listen to one event (`updates:get`, `updates:check`, `updates:download`, `updates:install`, `updates:open-release`, `updates:set-auto-check`, event `updates:state`). Only the main window is accepted as a caller, none of them takes a path or an address, and the updater object itself never reaches the page (`electron/updater.test.mjs` checks the preload and the handlers).
* **No secrets.** No token or key is in the application, `app-update.yml`, `latest.yml`, the manifest or any log; `release-verify.mjs` scans the package for token and key patterns.
* **The release page is built, not read.** "What's new" opens `https://github.com/<owner>/<repo>/releases/tag/v<version>` from the explicit source and a validated version. Release-note HTML is never rendered in the application.

### Unsigned builds (the situation today)

The project has no trusted code-signing certificate yet. An unsigned release therefore has weaker trust than a signed one, and this document does not pretend otherwise:

* The updater checks the **sha512 from `latest.yml`**, so a corrupted or truncated download is rejected. It does **not** check who made the installer: `app-update.yml` has no `publisherName` unless the build was made with code signing, and electron-updater only verifies a publisher when that field exists. Whoever can publish a release in the update repository can ship any installer to every installed copy; protect the repository account (two-factor authentication) like a signing key.
* Windows SmartScreen and antivirus products may warn about an unsigned installer the person downloads for the first time (`docs/RELEASE_TRUST.md`). An update downloaded by the application itself is not a browser download.
* **Future Authenticode signing needs no change to the updater.** Build with `--release` (signing mandatory, `forceCodeSigning`): electron-builder then computes the publisher name from the certificate and writes it as `publisherName` into `app-update.yml`, and electron-updater verifies the signature and the publisher of every downloaded installer. `release-verify.mjs --require-signature` already demands a valid, trusted, timestamped signature on every DS1TR executable and artifact. Nothing in this implementation weakens that.

## 7. The release workflow (`.github/workflows/release.yml`)

* **Trigger:** only `workflow_dispatch` (Actions > Release (draft) > Run workflow). No push, pull request, schedule or tag starts it.
* **Inputs:** `version` (X.Y.Z, must equal `apps/desktop/package.json`), `golden_attestation` (the first 7 characters of the commit whose golden fixtures **you** verified, see below), `create_draft` (untick for a dry run that only keeps the files as a workflow artifact).
* **Runner:** `windows-latest`, Node 24 with the npm cache, .NET 8 for the two helper programs, `npm ci` (exactly the lockfile).
* **Steps:** request check > checkout (no stored credentials) > version/source/target-repository agreement > `npm ci` > backend build and tests > typecheck > desktop build and tests > `package:win --channel=public --unsigned-release` (which runs the release verification) > `release-verify.mjs --publication --tag vX.Y.Z` > `release-assets.mjs` (stages and cross-checks the six files) > upload as workflow artifact > **create the draft**. A failing step stops everything after it; the draft is the last step.
* **Permissions:** `contents: read` for the workflow, `contents: write` for the single job (to create the draft and upload files). Nothing else.
* **Secrets:** none for the single-repository setup (`GITHUB_TOKEN` suffices). Only `RELEASE_REPO_TOKEN` (and the variable `RELEASE_REPOSITORY`) for a separate release repository. Never put a secret anywhere else.
* **Golden fixtures cannot run on a hosted runner**, because they need a clean Dark Souls Remastered copy (`goldenCompatibility.mjs verify`, `DS1TR_GOLDEN_GAME_SOURCE`). They are part of the owner's checklist; the workflow forces the step by requiring the commit hash in `golden_attestation` (a human gate, not a proof).
* **Action versions** are pinned by major tag (`@v4`). Pinning by full commit SHA is the stricter practice and a possible hardening once the repository exists.

### The assets of a release

| File | What |
|---|---|
| `DS1-Multi-Randomizer-X.Y.Z-Setup-x64.exe` | the NSIS installer (the recommended download, and what the updater downloads) |
| `DS1-Multi-Randomizer-X.Y.Z-Setup-x64.exe.blockmap` | the block map of that installer (differential downloads) |
| `latest.yml` | the update metadata an installed client reads; generated by electron-builder **from the same build** as the installer |
| `DS1-Multi-Randomizer-X.Y.Z-Portable-x64.exe` | the portable build (secondary download; manual updates) |
| `release-manifest-X.Y.Z.json` | what the release verification recorded: hashes, versions, provenance (commit, clean tree, toolchain), update source |
| `SHA256SUMS.txt` | sha256 of the five files above |

`latest.yml` is never written by hand. `release-assets.mjs` refuses to stage a release whose `latest.yml` does not match the installer (version, file name, size, sha512), whose blockmap is missing, whose manifest describes other files, or whose tag is not the packaged version.

## 8. One-time setup (owner)

1. **Decide the repository** (section 2) and create it on GitHub (public for releases).
2. **Write the owner and repository name** into `apps/desktop/electron/updateSource.ts` and `apps/desktop/electron-builder.yml` (`publish:`), the same three values in both; commit. Until then the project builds, but nothing can be released or updated.
3. **Push the repository** (this is a release action: it is not done by the assistant).
4. **Settings > Actions > General > Workflow permissions:** "Read repository contents and packages permissions" is enough (the workflow requests its own `contents: write`). Leave "Allow GitHub Actions to create pull requests" off.
5. **Separate release repository only:** create a fine-grained personal access token limited to that repository (Contents: read and write), store it as the Actions secret `RELEASE_REPO_TOKEN`, and set the Actions variable `RELEASE_REPOSITORY` to `owner/repo`.
6. Turn on two-factor authentication for the account and consider branch protection for `main`.
7. Run and record the QA of section 11 before calling the updater end-to-end verified. It uses an internal build and a public prerelease; it never requires a stable release.

## 9. Normal release procedure

See [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md). In short: bump the versions and commit, verify the golden fixtures locally, run the workflow with the version and commit hash, review the draft, then publish it as a prerelease by default. Publish stable only when explicitly requested.

## 10. After publication, and when something goes wrong

* **Publishing** creates the tag `vX.Y.Z`. A prerelease stays out of the stable "Latest" release; public clients ignore it. A stable release becomes "Latest" and is visible to public clients on their next check (within about a day, or at once by hand).
* **A bad release was published:** do not edit the files. In GitHub, delete the release (or mark it as a prerelease, which hides it from stable clients, whose updater follows only the "Latest" non-prerelease release) and publish a fixed version with a higher number. A client that already installed the bad version moves forward to the next one; the updater never downgrades.
* **A draft is wrong:** delete the draft and run the workflow again (it refuses to run when a release with the same tag exists).
* **GitHub is unreachable or rate-limits:** checks fail quietly ("Unable to check for updates. You can keep using the app."), the next automatic check is tried after an hour.
* **A client cannot update** (a build without a source, a repository that moved): download the installer from the release page and run it over the old installation; settings and history are kept.

## 11. QA: proving that N updates to N+1

A real updater cannot be called verified by unit tests alone. Release N+1 as a **public GitHub prerelease**; never publish it briefly as stable just to test it.

1. Build N for the scratch test installation with `npm run package:win -w @ds1tr/desktop -- --channel=internal`. Confirm the build info says `channel: internal`, then install the Setup artifact in a scratch folder and start it. This channel alone enables prerelease checks; it does not alter the public release build.
2. Build N+1 through the normal workflow, review the draft, and publish it with GitHub's **Set as a pre-release** option enabled. The assets still use numeric `X.Y.Z` version tags and the files listed in section 7.
3. In the internal N installation, confirm About says N and preserve test settings, history and game path. Check for updates; expect N+1, then **Download update** with progress.
4. While a generation, Apply or Restore is active, press **Restart and update**. Expect the operation warning and no restart. Finish the operation, press it again and expect N+1 to start.
5. Confirm About says N+1, the app opens normally, and settings, history, game path and user data remain. Confirm the game files were not changed by updating.
6. Separately install public N and check for updates while N+1 is a prerelease. The public build must not offer it. A portable build remains manual and only opens the release page.

The published N+1 app is a public build and returns to stable-only updates after restart. To repeat QA, install a fresh internal N build. A local `latest.yml` test can check the installer and relaunch path, but it is not a GitHub-provider E2E result.

## 12. Verification status

See the table at the end of this file, kept current by whoever changes the updater.

| What | Status |
|---|---|
| Update policy (states, throttle, portable, prerelease, operation block, errors, IPC surface, translations) | AUTOMATED VERIFIED: `electron/updater.test.mjs`, `tools/release/update-lib.test.mjs`, backend `activity.test.mjs` and the integration test |
| Release configuration, workflow structure, update-source agreement | AUTOMATED VERIFIED (`update-lib.test.mjs`) |
| electron-updater and its dependencies are packaged; `app-update.yml` and `latest.yml` / blockmap are generated by the installed electron-builder | PACKAGED VERIFIED: see "Packaged build" below (the asar contents are also checked by every release verification) |
| Workflow run on GitHub, draft creation, asset upload | GITHUB VERIFIED (2026-10-09, run on commit `d29c1c7`; see "The first real draft") |
| Internal installed N updates to public GitHub prerelease N+1; public N ignores it | DEFERRED: section 11 |
| Authenticode publisher verification of updates | DEFERRED: needs a trusted certificate |
| Self-updating portable | UNSUPPORTED by design |

### Packaged build (what was actually run on a real package)

Built with `npm run package:win -w @ds1tr/desktop` (development mode, version 0.37.0, update source github.com/remohomer/DS1-Multi-Randomizer; first checked with the placeholder, then repackaged with the real owner) and inspected:

* **PACKAGED VERIFIED:** `DS1-Multi-Randomizer-Setup-0.36.0.exe`, its `.blockmap`, `latest.yml` (version, file url/sha512/size, path, sha512, releaseDate; generated by electron-builder from the same build) and the portable `DS1-Multi-Randomizer-0.36.0-x64.exe` are produced together; `resources/app-update.yml` is written with `provider: github`, the owner/repo of the `publish:` block and `updaterCacheDirName`; there is no `publisherName` (unsigned) and no token.
* **PACKAGED VERIFIED:** `electron-updater` 6.8.9 and its twelve-package dependency closure are inside `app.asar` under `dist-electron/node_modules`. (They cannot sit in a root `node_modules` folder: the build sets `beforeBuild` to return `false`, so electron-builder does not collect dependencies itself, and its file walker never descends into a root `node_modules`. `apps/desktop/scripts/package-win.mjs` stages the closure from the lockfile-resolved workspace tree; `tools/release/release-verify.mjs` fails a package whose asar lacks any of them.)
* **PACKAGED VERIFIED:** the unpacked application starts (throw-away user-data folder), the real preload bridge exposes `state, check, download, install, openReleasePage, setAutoCheck, onState`, and with the placeholder source (before the owner was set) `state()` and `check()` answered `unsupported / no-update-source` without error.
* **NOT run:** an installed NSIS copy updating to a newer one, the portable build's manual-update flow, and any request to github.com. Those need a real repository (section 11).

### The first real draft (GitHub-verified, 2026-10-09)

Repository `github.com/remohomer/DS1-Multi-Randomizer` (public, default branch `main`); workflow `Release (draft)` on `main`.

* **GITHUB VERIFIED:** the workflow is listed by Actions, starts by hand only, repository workflow permissions are `read` (no approval of pull requests), and the default `GITHUB_TOKEN` created the draft: no personal access token and no secret were set.
* **GITHUB VERIFIED:** the first run (commit `38fdb2c`) passed the request check, the version/update-source/target agreement, `npm ci`, backend tests, typecheck and desktop tests, and **failed in packaging verification**: `powershell.exe` started from the runner's PowerShell 7 inherited `PSModulePath` and could not load `Get-AuthenticodeSignature`. `tools/release/release-lib.mjs` now starts it without that variable (commit `d29c1c7`, with a test). The second run (`d29c1c7`) passed every step and created the draft `v0.37.0` (**Draft**, not a prerelease, `latest` still resolves to no release, no tag exists). Published releases are not touched by the workflow.
* **GITHUB VERIFIED / PACKAGED VERIFIED on the real draft assets** (downloaded to a folder outside the repository, not installed): `latest.yml` names `DS1-Multi-Randomizer-Setup-0.37.0.exe`, version 0.37.0, and its sha512 and size equal those of the downloaded installer; the blockmap covers exactly the installer size; `SHA256SUMS.txt` matches all five other files; the manifest lists the same installer/portable hashes; `app-update.yml` inside the installer says `provider github, owner remohomer, repo DS1-Multi-Randomizer`; the executable's product name is `DS1 Multi Randomizer`, version 0.37.0; `release-verify --publication --tag v0.37.0` on those files: OK (unsigned-release mode); no credential pattern in `app.asar`, `app-update.yml`, `latest.yml` or `build-info.json`.
* **Verifier fix found on the way:** `release-verify` read `latest.yml` from the local build folder even when `--installer` pointed at downloaded files; it now reads the `latest.yml` next to the given installer.
* **Not done on purpose:** the draft is not published; no tag exists.

### The 0.37.0 public prerelease (GitHub-verified, 2026-10-09)

The earlier draft was subsequently published as a **public prerelease** at `https://github.com/remohomer/DS1-Multi-Randomizer/releases/tag/v0.37.0` (not a draft; `v0.38.0` is still unused at this point). GitHub reports six uploaded assets: the canonical Setup and Portable executables, Setup blockmap, `latest.yml`, `release-manifest-0.37.0.json`, and `SHA256SUMS.txt`. This gives the internal QA channel a published prerelease to find; it does not make a stable release available.

### Asset names (since 0.37.0)

The two downloads are named by their kind first, so that nobody reads "x64" as a property of only one of them: `DS1-Multi-Randomizer-X.Y.Z-Setup-x64.exe` (installer, recommended, in-app updates) and `DS1-Multi-Randomizer-X.Y.Z-Portable-x64.exe` (no installation, manual updates). The names live in `electron-builder.yml` (`nsis.artifactName`, `portable.artifactName`); electron-builder derives `latest.yml` (`url`, `path`) and `<Setup name>.blockmap` from them, and `release-policy.json`, `release-verify.mjs`, `release-assets.mjs` and the tests expect exactly these patterns. The earlier drafts of 0.37.0 used `DS1-Multi-Randomizer-Setup-0.37.0.exe` and `DS1-Multi-Randomizer-0.37.0-x64.exe`.

## 13. Current end-to-end status

The real installed N to N+1 test remains **DEFERRED** until two numeric-version GitHub releases exist and N+1 is published as a public prerelease. Follow section 11, then record the exact installed versions, checks, progress, operation guard, preserved user data, restart result and public-build prerelease refusal here and in `docs/IMPLEMENTATION_STATUS.md`. Unit tests and a green release workflow do not count as that E2E result.
