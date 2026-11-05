# Nexus Mods release readiness

Status as of 2026-11-01 (version 0.40.0). This is a preparation dossier for the owner, not an upload record. **Nothing has been uploaded, published or submitted to Nexus Mods, and no Terms of Service were accepted.** The copy-ready mod page is in [NEXUS_RELEASE_DRAFT.md](NEXUS_RELEASE_DRAFT.md); the gates are in [PUBLICATION_CHECKLIST.md](PUBLICATION_CHECKLIST.md).

Verdict: the **Nexus distribution build** (`npm run package:win:nexus -w @ds1tr/desktop`) is designed to meet Nexus's internet-communication rule: it has no application-update network code path and downloads nothing. Two things still need the owner before upload: written confirmation from Nexus staff is recommended (the rule is discretionary), and the Nexus package has **no CI-built provenance or attestation yet** (see "Open gaps").

## Sources used (read 2026-11-01)

Official Nexus help pages: [File Submission Guidelines](https://help.nexusmods.com/article/28-file-submission-guidelines) (page footer: last updated 2026-10-27), [Why has my mod been quarantined?](https://help.nexusmods.com/article/117-why-has-my-mod-been-quarantined) (2026-10-19), [Moderation Policy](https://help.nexusmods.com/article/27-moderation-policy) (2025-07-16), [Terms of Service](https://help.nexusmods.com/article/18-terms-of-service) (2026-05-12), [Best Practices for Mod Authors](https://help.nexusmods.com/article/136-best-practices-for-mod-authors) (2024-08-05; may be outdated). The Nexus Dark Souls Remastered game page and `/about` returned HTTP 403 to the research tool, so **any DSR-specific Nexus rules were not checked**. Policies change: re-read the File Submission Guidelines on the day of upload.

## Compliance matrix

| # | Requirement (Nexus, quoted or paraphrased) | Current project status | Risk | Action |
|---|---|---|---|---|
| 1 | Executables: the guidelines do not say whether executables are allowed outright; "Submitted files will be subject to virus scanning"; self-extracting files "will be marked for review"; "Public mod pages should contain only functional mod files, modding tools or modding guides." | A Windows Electron tool (a modding tool). Setup and Portable are NSIS executables; the Portable one self-extracts at each start. | Medium: unsigned executables, self-extracting wrapper, large (about 205 MB) | Upload the Setup as Main; Portable only as Optional (see Files layout). Expect moderation review; do not repack to dodge the scanner. |
| 2 | Internet: "Files (especially executables) that connect to the internet to download or send information and/or files are prohibited unless where it is crucial for the functioning of the mod/utility. Please note that "auto update" functionality does not qualify as crucial." Authors with a need are told to email support@nexusmods.com with reasoning and source code. | **Nexus build: no outbound traffic by design** (see Network audit). The GitHub build checks GitHub for updates and is **not** the file to upload. | Low for the Nexus build; High if the GitHub build were uploaded by mistake | Upload only a `-Nexus-` named file. Verify `build-info.json` says `"distribution": "nexus"` (checklist). Optionally email staff with the source link (suggested text below). |
| 3 | Auto-update does not qualify as crucial. | Nexus build: updater not loaded, no `app-update.yml`, no `latest.yml`, timers gated; Updates screen only shows the installed version and (if configured) opens the Nexus page in the browser on a click. | Low | None; keep `DS1TR_NEXUS_PAGE_URL` unset unless the real mod page URL is known. |
| 4 | Telemetry/analytics/crash reporting: not named; falls under "send information". | None in the code (no analytics/crash-reporter/Sentry calls found). | Low | State it on the page only as far as proven (see below). |
| 5 | Runtime downloads / external fetching: "connect to the internet to download". | Nexus build: none. GitHub build: legacy-only (see Network audit). Nexus build refuses legacy v1/v2 Fog Seed Codes that need a downloaded cache (`WORLD_DATA_LEGACY_UNAVAILABLE`). | Low | Document the limitation on the page. |
| 6 | Copyrighted game assets: "User submissions which include copyrighted content, be it game files or images, are prohibited unless proper permission has been granted." ToS: the uploader warrants no infringement of the game owner's copyright and compliance with the game's EULA. | No Dark Souls files, saves or QA data are packaged (Content audit). The tool reads the player's own installed game and writes modified files only on the player's machine. The Fog helper reconstructs its input data locally from the player's own files. | Low-Medium: the tool modifies a copyrighted game; DSR's EULA position on modding is not something this repo can prove | Keep "unofficial, not affiliated" wording. Do not ship screenshots/art that are not your own or properly permitted (game screenshots of gameplay are normally what mod pages show; the owner judges that). Owner reads the ToS upload licence. |
| 7 | Archive/format: "strongly recommended to use ZIP or 7z"; RAR may cause false positives; no password-protected archives; "we reject any upload that features additional archives inside the mod structure." | Release files are raw `.exe`. | Medium | Upload ZIP files (see Files layout). |
| 8 | Virus scanning: every file is scanned; a VirusTotal icon is shown on each file; failures are quarantined for moderator review; authors may contact vendors about flags. | Unsigned binaries bundling .NET single-file helpers and Electron: false-positive prone (see ANTIVIRUS_FALSE_POSITIVES.md). | Medium | Expect a possible quarantine; do not disable protection or hide anything; keep SHA-256 and source links ready; follow Nexus's review path (support@nexusmods.com, do not delete the blocked file). |
| 9 | Required disclosures / description accuracy: descriptions must "accurately reflect the file's nature and functionality"; claims "must be truthful and supported by reasonable evidence". "Files must be presented in good faith and in a functional state with respect to any caveats or requirements that you must detail." | Draft page states the proven limits (occasional Fog reroll, no PTDE, unsigned, offline play, Fog not fully played through). | Low | Owner reviews the draft against the final build. Do not claim "safe" or "virus-free". |
| 10 | AI-generated content: tags **AI-Generated Content** (includes AI-generated code and user interface), **AI Media** (AI mod-page descriptions, thumbnails, promo art) and **AI Assisted** (developer-led code with limited AI involvement, requiring evidence such as version-control history and genuine expertise). "Failure to correctly tag mods, including undisclosed generative AI usage, may be subject to moderation action." | **Applies.** AGENTS.md records that ChatGPT, Codex and Claude work on this repository, and this page copy was drafted with AI. | Medium: wrong or missing tag is a moderation risk | Owner decides the honest tag for the code from how it was actually written (do not choose AI Assisted unless you can meet its evidence requirements; the git history is the evidence). Tag **AI Media** if the AI-drafted description is posted. |
| 11 | Licensing/credits: "Proper credit to the original author(s) must be given"; credit is no substitute for permission; "Submissions must adhere to the licences and agreements associated with the modified or featured title(s)." | GPL-3.0-only project; vendored `kellernz1/ds-randomizer` (GPL-3.0), FogMod and SoulsIds (Apache-2.0), SoulsFormats (GPL-3.0), YamlDotNet/Newtonsoft.Json/ZstdSharp (MIT). Legacy Python randomizers are behaviour references only, not copied. Notices ship in the package. | Low-Medium: Nexus ToS grants Nexus a broad licence over uploads; combine with GPL obligations | Owner reads ToS upload licence before accepting. Credits are in the draft. Keep a source tag for the exact uploaded version. |
| 12 | Requirements/dependencies: recommended, not mandatory, to state requirements. | Requires 64-bit Windows and the player's own Dark Souls Remastered (PC). No .NET/Node install needed. | Low | Fill Nexus "Requirements" with Dark Souls Remastered only. |
| 13 | Multiplayer: "Files that may be disruptive to any persistent multiplayer environment are prohibited." | The tool recommends offline play with modified data; it does not touch network play. | Low-Medium | Keep the offline-play warning prominent. |
| 14 | Placeholders: "Public placeholders are prohibited." | Not applicable until there is a working file. | Low | Create the page only when uploading a working file. |

## Network audit (production code, not documentation)

Method: grep of every `fetch`/HTTP/socket/`autoUpdater`/`openExternal`/crash-reporter/analytics use in `apps/desktop`, `apps/backend`, `packages`, the C# helpers and the vendored engine; reading the build metadata gate; a locally built Nexus package; and a 42-second live observation of that package's process tree (TCP and UDP endpoints sampled every 3 s with `autoCheck` setting on).

**NETWORK BEHAVIOR (Nexus distribution):** none. Only loopback traffic between the window and the local backend (`127.0.0.1`, random port, per-session token) was observed. No update checker, GitHub API client, `electron-updater` or downloader is loaded: `main.ts` creates them only when the immutable build metadata says `distribution: "github"`; a Nexus or unreadable-metadata build gets inert update operations. The package contains no `app-update.yml` and no `latest.yml`. `release-verify.mjs --distribution nexus` checks these facts. The updater code files exist inside `app.asar` but are never loaded. The only deliberate external action is the user clicking a link: the GPL license page (`gnu.org`) and, only if the build was made with `DS1TR_NEXUS_PAGE_URL`, the Nexus page; both open in the default browser, not through the app.

**NETWORK BEHAVIOR (GitHub distribution, for comparison):**

| Destination | Purpose | When | Data sent |
|---|---|---|---|
| `api.github.com` (repository metadata, `releases/latest`) and the GitHub release asset hosts | Update check; the download only after the user clicks Download | Automatic check unless disabled in Updates; stable releases only for public builds | Ordinary HTTPS metadata: IP address, request time, `User-Agent`; no settings, seeds, saves or game data |
| `raw.githubusercontent.com/thefifthmatt/FogMod/<pinned commit>/` (40 files, SHA-256 verified) | Legacy Fog data for generation profiles v1/v2 **only** | Only when such a Seed Code is used and the local cache is incomplete | Ordinary HTTPS metadata |

New worlds use profile v7 (and v3 to v6 Seed Codes). V7 is fully functional in the Nexus build: it reads FogMod's own `fog.txt`, which ships inside the application unmodified (Apache-2.0, `tools/fog-host/upstream/PROVENANCE.md`), and rebuilds the other 58 inputs locally from the player's game files, so it needs no cache, no earlier run and no network. The Nexus build keeps making no network request and loading no updater. Earlier sentence, for v3 to v5: their Fog data is reconstructed locally from the player's own game files and nothing is downloaded (`worldData.ts`, covered by `fogPrerequisites.test.mjs`). Earlier project documents said Fog randomization "downloads 40 files when needed"; that is true only for legacy v1/v2 GitHub builds and has been corrected in README and `SECURITY_AND_PRIVACY.md`.

**AUTO UPDATE:** GitHub build enabled (on by default, can be turned off, download and install need a click); Nexus build **disabled** (no network path at all).

**USER DATA SENT:** none by the application in either build. Not sent: settings, seeds, run history, saves, game files, support bundles (a support bundle is a local ZIP that the user exports and shares by hand). Not present: telemetry, analytics, accounts, crash reporting.

Limits of this evidence: the live observation was a 42-second idle sample of connection tables (short-lived connections between samples could be missed) and did not exercise generation; the claim rests mainly on the code review above. Chromium/Electron's own behaviour was not audited beyond the observation. The C# helpers contain no network API usage (searched sources; the only matches were generated implicit-using files).

## Content audit of the packaged Nexus build

Inspected `apps/desktop/release/nexus/win-unpacked` (171 files outside `app.asar`, 347 entries inside it; the Setup and Portable are built from this same tree by electron-builder).

- **Game content: none found.** No file with a Dark Souls container extension (`.dcx .bnd .bhd .bdt .msb .emevd .esd .tpf .flver .param .msgbnd ...`) outside Chromium's `icudtl.dat` (an ICU text-data file), none in `app.asar`. No DSR executable, save, QA runtime or telemetry-tool file. `runtime/engines/combined/vendor` contains JavaScript only (generation logic and a 4 KB `prototype-data.js`). The five project executables are `DS1 Multi Randomizer.exe` (Electron), `DsrDataTool.exe`, two `FogHost.exe` builds and `GameCursorHelper.exe`; `DsrDataTool` and `FogHost` read the player's own game files at run time. FogMod's own `dist/` data (partly FromSoftware-derived, edited by its author) is **not** bundled; see [FOG_DATA_PROVENANCE.md](FOG_DATA_PROVENANCE.md). The package carries SHA-256 manifests and provenance notes, not game bytes.
- **Project code and data vs FromSoftware content:** everything in the package is project code, third-party software (Electron/Chromium, .NET single-file runtime, npm/NuGet components) or notices. The only FromSoftware-derived artefacts are what the tool *writes into the player's own game folder* at Apply time; they are never in the installer.
- **Secrets:** pattern scan for GitHub/AWS/Slack tokens and private-key headers found only false positives (random base64 inside the 246 MB Electron binary). No credentials or tokens; no `.env`, key or certificate files.
- **Developer/local data:** no local paths found. Public GitHub account names appear in updater code (`remohomer`, `adamwojdan` as accepted former repository owners, in `githubReleaseApi.js`; present but never loaded in the Nexus build); "Adam" appears only inside Chromium's license text and binary strings. Nothing is a secret; these are public identifiers.
- **Signatures:** all five authored executables are unsigned (`UNSIGNED`); Electron/Chromium DLLs keep their upstream state.

Nexus package built for this audit (local, unsigned, not for upload as is): `DS1-Multi-Randomizer-0.40.0-Nexus-Setup-x64.exe` 205,576,844 bytes SHA-256 `8e42ad6aa8a61f418f5608dc29b285489f75ff91e7348499544f85d85875e997`; `...-Nexus-Portable-x64.exe` 204,939,985 bytes `13e93f1a75383b5f0b505c9a6d72bd342ff95930a48b940dd1ce7815f0100a3d`; verifier result OK for development mode. **These hashes are for a local build; the file you upload must be rebuilt (or CI-built) from the release commit and hashed again.**

## Open gaps (owner-visible)

1. **No provenance for the Nexus package.** The GitHub workflow builds only the GitHub distribution; `package:win:nexus` is a local build ("local unsigned staging artifacts") and cannot use the GitHub publication modes. So the new GitHub attestations do **not** cover the file a Nexus user downloads, and the Nexus file's SHA-256 differs from the GitHub Setup's. Recommended follow-up (not done, because it changes the release workflow and cannot be tested before a real run): build the Nexus distribution in the same workflow, upload it as a workflow artifact, and add its files to the attested subjects. Until then, state on the page that the Nexus file is built from the tagged source and publish its SHA-256, and do not imply it is attested.
2. **Unsigned.** See [CODE_SIGNING.md](CODE_SIGNING.md) for the decision brief.
3. **Staff confirmation.** The internet rule is discretionary and enforcement is by report and moderation; written confirmation reduces risk of later removal but is not required to upload.
4. **DSR-specific Nexus rules** were not readable (HTTP 403); check the Dark Souls Remastered game page's rules yourself.

## Nexus Files tab layout (recommended)

| Slot | File | Why |
|---|---|---|
| **Main file** | `DS1-Multi-Randomizer-X.Y.Z-Nexus-Setup-x64.zip` containing `DS1-Multi-Randomizer-X.Y.Z-Nexus-Setup-x64.exe`, `SHA256.txt` (the exe's hash) and `LICENSE` | Per-user, no-admin installer; recommended path; ZIP is Nexus's recommended container |
| **Optional file** | `DS1-Multi-Randomizer-X.Y.Z-Nexus-Portable-x64.zip` containing the Portable exe and `SHA256.txt` | For people who do not want to install. It self-extracts to a temp folder on each start and is more likely to draw heuristic warnings, so keep it optional and say so in its description |

ZIP versus raw `.exe`: the Nexus pages say "strongly recommended to use ZIP or 7z", warn that RAR causes false positives, ban password-protected archives, reject archives nested in the mod structure, and send self-extracting files to review; they do not state a preference for raw executables. A plain ZIP (Deflate, no password, one executable plus text files, no inner archive) follows that guidance. The ZIP does not hide anything: the scanner scans the contents, and the executable inside must be byte-identical to the one whose hash you publish. This is the format Nexus recommends, not a repack to influence detection; if staff say they prefer a raw EXE, follow staff. Do not use RAR or password protection. Give each file its own description, and mark nothing as "Old". Do not upload the GitHub `-Setup-x64` file to Nexus.

Build the ZIPs from the final, approved Nexus build with a standard tool (for example `Compress-Archive`) and record the SHA-256 of the ZIP too, because users download the ZIP.

## Suggested support request (optional, send before uploading)

> We maintain DS1 Multi Randomizer, a GPL-3.0-only Windows tool for Dark Souls Remastered. Source: https://github.com/avoydan/DS1-Multi-Randomizer. Our Nexus distribution does not load any updater, makes no network requests, and downloads nothing: new Fog worlds reconstruct their data locally from the player's own game files, and legacy Fog Seed Codes that would need a download are refused in this build. We observed only loopback traffic between the app window and its local backend. We have not uploaded anything. Could you confirm that this exact utility may be hosted under the internet-communication rule, and tell us if any change is needed? We will tag the page with the applicable AI categories.

Send from the owner's own account to support@nexusmods.com; attach nothing secret.

## Page media (screenshot plan, 6 to 8 images)

Use real screenshots from the final Nexus build. Do not fabricate screenshots; crop out usernames, folder paths, Steam names and save data.

1. **Main screen** with the navigation visible: shows what the app is.
2. **Items configuration**: the item randomization options, to show how choices are made.
3. **Enemies and bosses configuration**: the placement and pool options.
4. **Fog configuration**: gate types (ordinary, boss, optional PvP gates), noting safe defaults.
5. **Result screen with the Seed Code** (blur or crop spoiler content): shows reproducibility and the Apply to game / Restore original game safety buttons.
6. **In-game, randomized enemy or boss** in an unexpected place (real gameplay capture).
7. **In-game, a fog gate leading to an interesting destination** (caption which area it connects).
8. **Optional: Skill Issue Mode** on the Your game screen, captioned as optional and off by default.
