# Release trust

How a DS1 Multi Randomizer release is built, what is in it, what is signed, what Windows and antivirus products are likely to say, and what the owner has to do before a **public** release.

> **State today (desktop 0.26.0): every build is UNSIGNED.** No code-signing certificate exists for this project and none was bought, requested or generated for release. The signing pipeline is built and tested with a throwaway certificate (never shipped, never committed); a public release is **refused** by the tooling until a trusted identity is supplied. Nothing here claims that a build is trusted, antivirus-clean or SmartScreen-approved.

Honesty labels used in the reports of this project:

| Label | Meaning |
|---|---|
| AUTOMATED VERIFIED | a test or script in the repository passes |
| STATICALLY VERIFIED | checked on files and data without running the game |
| GAMEPLAY VERIFIED / UNVERIFIED | played in Dark Souls Remastered / not played |
| SIGNING READY | the pipeline signs and verifies; no trusted identity is installed |
| TRUSTED SIGNED | signed with a certificate that chains to a trusted root. **No build has this label.** |

## 1. Executable inventory

Everything that Windows can execute and that DS1TR ships or writes. Paths are relative to the install folder (`win-unpacked` in the build).

| File | Role | Started by | Signed by the pipeline |
|---|---|---|---|
| `DS1 Multi Randomizer.exe` | the Electron application (renamed `electron.exe`, DS1TR icon and version resource) | the person (shortcut, Start menu) | yes |
| `resources/runtime/engines/combined/vendor/tools/DsrDataTool/DsrDataTool.exe` | reads and patches the game files into a workspace (self-contained .NET 8, one file, **not compressed**) | the backend, a child process, argument array, no shell | yes |
| `resources/runtime/tools/fog-host/FogHost.exe` | runs the fog randomizer (FogMod) on prepared data (self-contained .NET 8, one file, not compressed) | the backend, same way | yes |
| `DS1-Multi-Randomizer-<version>-Setup-x64.exe` | the installer (NSIS, per-user) - the **recommended** download | the person | yes |
| `Uninstall DS1 Multi Randomizer.exe` | the uninstaller the installer writes | the person (Windows Settings) | yes (electron-builder signs it with the installer; not separately verified here) |
| `DS1-Multi-Randomizer-<version>-Portable-x64.exe` | the portable build - the **secondary** download | the person | yes |
| `d3dcompiler_47.dll` | Direct3D shader compiler, already signed by Microsoft | Chromium | not re-signed |
| `ffmpeg.dll`, `libEGL.dll`, `libGLESv2.dll`, `vk_swiftshader.dll`, `vulkan-1.dll` | Electron / Chromium libraries, shipped as their authors built them | Chromium | not re-signed (the pipeline never alters third-party binaries) |

Source of truth: `tools/release/release-policy.json` (the verifier fails when a binary appears that is not listed, or a forbidden one appears: `elevate.exe`, symbols, `.map`, logs, keys, `.env`).

DLL and native module policy: DS1TR adds **no** native Node module and no DLL of its own; the only native code is Electron's and the two .NET single-file helpers. The backend runs as Node code inside Electron (`ELECTRON_RUN_AS_NODE`), not as a separate `node.exe`.

## 2. Process, shell and temp audit (statically verified in the source)

* **Processes DS1TR starts:** the backend (Electron's own binary in Node mode, argument vector), `DsrDataTool.exe`, `FogHost.exe`, the game executable when the person presses Play/Launch (detached), and four Windows tools with fixed arguments and `windowsHide`: `reg query` (read the Steam install path), `tasklist` (is the game running), `taskkill /T` (stop the backend tree when the application closes). Nothing uses `shell: true`, `cmd.exe`, PowerShell, `wscript`, `mshta`, `rundll32`, `certutil`, `bitsadmin`, `schtasks` or `Start-Process`. Searched in `apps/backend/src` and `apps/desktop/electron`.
* **URLs opened:** the `steam://` launch of the person's own game through `shell.openExternal`, and the GPL page link in the About text.
* **Temp and extraction:** the application writes data only (JSON caches) to the temp folder and never executes anything from it. Its runtime lives in the install folder (`resources/runtime`) and its state in the user data folder. **The portable build is different by construction:** it extracts itself to a temporary folder at every start and runs from there (an electron-builder behaviour). Run-from-temp is a pattern antivirus heuristics treat with suspicion, which is the main reason the installer is the recommended download.
* **Privilege:** `requestedExecutionLevel asInvoker` on every executable; the installer is per-user (`perMachine: false`, `allowElevation: false`), writes under `%LOCALAPPDATA%\Programs\DS1 Multi Randomizer` by default (tested), never asks for administrator rights, and the package contains no elevation helper (`packElevateHelper: false`). Checked by `release-verify.mjs`.
* **Persistence:** the application installs no service, no scheduled task, no startup entry and no driver. The installer adds shortcuts and one per-user uninstall entry.
* **Network (audited):** the backend listens on `127.0.0.1` on a random port, protected by a per-session token. The only outbound request is one HTTPS download of 40 world-data files from the pinned FogMod commit on `raw.githubusercontent.com`, each file verified by SHA-256 before it is used (only when the world randomizer is switched on and the cache is missing). No telemetry, no accounts, no auto-update, no upload of anything.
* **Heuristic red flags audit:** no UPX or other packer, no obfuscation, no self-modifying code, no `.pdb`, no embedded scripts in resources, no hidden windows beyond `windowsHide` for the helper tools listed above, no anti-debug code. The two remaining *heuristic* properties are honest ones: the helpers are large self-contained .NET single files, and the application edits game files (that is its purpose; it works on a verified copy first and keeps one restore authority).

## 3. Helper packaging experiment

Question: should DsrDataTool and FogHost be a compressed single file (the former format), an uncompressed single file, or a folder? Measured by `node tools/release/helper-format-experiment.mjs <folder>` (builds each, never touches the release folder; start time = mean of 5 runs with every .NET location hidden):

| Helper | Format | Files | Size | Start |
|---|---|---|---|---|
| DsrDataTool | A single file, compressed | 1 | 34 MB | 1,054 ms |
| DsrDataTool | B single file, uncompressed | 1 | 65.9 MB | 373 ms |
| DsrDataTool | C self-contained folder | 188 | 72.2 MB | 192 ms |
| FogHost | A single file, compressed | 1 | 34.4 MB | 315 ms |
| FogHost | B single file, uncompressed | 1 | 66.8 MB | 384 ms |
| FogHost | C self-contained folder | 190 | 73.1 MB | 198 ms |

Entropy of the executable (bits per byte, `shannon entropy of the file`): compressed 7.81, uncompressed 6.71 (the Electron executable itself is about 6.7). A 34 MB block of 7.8 bits-per-byte data is exactly what scanners associate with packed malware. **Decision: B** - one file per helper (the application keeps a simple layout and signs two files, not 380), uncompressed, 32 MB more on disk; start time is a wash (DsrDataTool three times faster, FogHost 70 ms slower). A folder (C) starts fastest but puts 378 loose files in the install folder and in the antivirus's view. Local scan: see section 8.

## 4. Build modes

| Command | Result |
|---|---|
| `npm run package:win -w @ds1tr/desktop` | **development / test build**: installer + portable, **not signed**, verified by `release-verify.mjs` in development mode (an unsigned build reports `UNSIGNED` and passes). This is what every build to date is. |
| `npm run package:win -w @ds1tr/desktop -- --release` | **public release build**: signing is mandatory. Without credentials in the environment it stops **before building anything** (tested). With them, electron-builder signs the application, both helpers, the installer, the uninstaller and the portable, and the build ends with `release-verify.mjs --require-signature`. |
| `npm run package:win -w @ds1tr/desktop -- --channel=public --unsigned-release` | **publication build without a signature**: what the GitHub release workflow builds today. Public channel, update checks become failures (update source set, `latest.yml` matches the installer), reported as `unsigned-release`, never as signed. See [AUTO_UPDATE.md](AUTO_UPDATE.md). |
| `node tools/release/release-gate.mjs [--public --expected-publisher "<text>"]` | the hard-fail checklist of section 7 (public mode refuses a dirty tree, a skipped golden check and any unsigned artifact) |

The default unsigned development package is on the **internal** channel. That channel displays internal test tools and opts into GitHub prereleases for controlled updater QA. `--channel=public` disables that opt-in; every publishable package is forced to public by the release workflow and verifier.

Credentials come **only from the environment** and are never read from, written to or committed in the repository (`.gitignore` also excludes `*.pfx`, `*.p12`, `*.pem`, `*.key`; the verifier fails when such a file is inside a package):

| Variable | Meaning |
|---|---|
| `CSC_LINK`, `CSC_KEY_PASSWORD` | a code-signing certificate file (`.pfx`) and its password (electron-builder's own variables) |
| `DS1TR_SIGN_CERT_SHA1` or `DS1TR_SIGN_CERT_SUBJECT` | a certificate in the Windows certificate store, e.g. on a **hardware token** (see section 5: new OV and EV certificates are normally issued on hardware) |
| `DS1TR_ARTIFACT_SIGNING=1` + `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `DS1TR_ARTIFACT_SIGNING_ENDPOINT`, `DS1TR_ARTIFACT_SIGNING_ACCOUNT`, `DS1TR_ARTIFACT_SIGNING_PROFILE` | Azure Artifact Signing (formerly Trusted Signing) |
| `DS1TR_EXPECTED_PUBLISHER` | **required** in release mode: the text the signer's subject must contain, so the verifier can tell the intended publisher from any other signer |
| `DS1TR_TIMESTAMP_URL` | optional RFC 3161 timestamp server (default of the tooling: `http://timestamp.digicert.com`). A timestamp is what keeps a signature valid after the certificate expires |

Configuration (electron-builder 26.15.3 schema): `win.signtoolOptions.signingHashAlgorithms: [sha256]`, `signExecutable` false by default and `true` only by the release script, `forceCodeSigning` true in release mode (a failed signature fails the build), `requestedExecutionLevel: asInvoker`, `nsis.packElevateHelper: false`. electron-builder signs the application executable, **every `.exe` of `extraResources` (both helpers)**, the installer, the uninstaller and the portable: this was **tested** with a throwaway certificate (in a scratch folder, deleted afterwards): all five DS1TR files came out `TEST-SIGNED (untrusted root)`, and the verifier failed the same build in public mode, as it must. Also tested on copies: the **signed single-file helpers still start** (`DsrDataTool.exe --help` exits 0, `FogHost.exe` exits 2 with no input, exactly as unsigned). **Not tested:** timestamping (the throwaway signatures carry none: no timestamp server was contacted), and any real certificate, token or Azure Artifact Signing account.

## 5. What the owner has to do for a public signed release

1. **Obtain a trusted code-signing identity.** The options, honestly compared (verify current terms with the provider; this was written without buying anything):
   * **OV (organization-validated) or individual-validated certificate** from a public CA. Enough for a signed release. Since mid-2023 the CA/Browser Forum requires the private key to live on a hardware token or in a cloud HSM, so a plain downloadable `.pfx` is no longer what new certificates come as: expect a USB token (use `DS1TR_SIGN_CERT_SHA1`) or a cloud-signing service. Roughly 100-400 USD per year.
   * **EV certificate.** More expensive, stricter vetting, always on hardware. **Not needed merely for SmartScreen**: Microsoft no longer gives EV certificates an instant SmartScreen reputation, so an OV certificate gets to the same place by the same slow route (below). Buy EV only if there is another reason.
   * **Azure Artifact Signing** (formerly Trusted Signing): Microsoft's service; short-lived certificates issued per signing, identity validated once. Cheapest of the three (about 10 USD per month at the time of writing) and no hardware. Availability depends on the owner's country and on whether the owner is an individual or an organization; **check eligibility first**. Supported by electron-builder through `azureSignOptions` (wired to the variables above).
2. **Set the environment** of one machine (never a committed file): the credentials of the chosen route plus `DS1TR_EXPECTED_PUBLISHER`, then `npm run package:win -w @ds1tr/desktop -- --release`.
3. **Run the gate:** `node tools/release/release-gate.mjs --public --expected-publisher "<the same text>"` with `DS1TR_GOLDEN_GAME_SOURCE` set to a clean game copy.
4. **Attach the release manifest** (`release-manifest.json`: version, commit, versions of the parts, SHA-256 of every executable and artifact, signer and timestamp) and publish its hashes next to the downloads.
5. Publish only from a tagged commit with the corresponding GPL source (`docs/LICENSING.md`).

CI: nothing signs in CI and no secret is stored there. A CI job may run the development build and `release-verify.mjs` (no credentials needed); the signing step stays on the owner's machine or in a protected environment the owner configures.

## 6. What users will see (SmartScreen and antivirus expectations)

* **Unsigned (today).** Downloaded in a browser, an unsigned executable carries the "Mark of the Web" and Windows SmartScreen shows "Windows protected your PC / Unknown publisher"; the person must choose "More info" and "Run anyway". Some antivirus products flag unsigned, large, self-extracting executables by heuristics. This is **expected** and cannot be "fixed" by packaging alone.
* **Signed with an OV or individual certificate.** The publisher name shows instead of "Unknown publisher". SmartScreen still warns until the *file or certificate* has built reputation through downloads by many people over days or weeks; the warning fades gradually. A signed build is not an instant pass.
* **Signed with Azure Artifact Signing.** The same reputation model; the identity is Microsoft-validated.
* **The installer** does not by itself prevent antivirus reports; what it avoids is run-from-temp extraction and the compressed packer-like blobs. Do not promise users "no warnings".

## 7. Release gate (public, hard fail)

`tools/release/release-gate.mjs --public` fails when any of these is not true; development mode runs the same checks except the signature one and the clean tree:

1. the working tree is clean and the commit is recorded;
2. desktop typecheck, desktop tests and backend tests pass;
3. golden compatibility passes (a public release cannot skip it);
4. `release-verify.mjs`: expected executables only (no `elevate.exe`), version of the application, FogHost and the Fog pin consistent with the sources and the world-data commit, DS1TR company/description/copyright in the PE resources (not the toolchain's), icon, no Electron menu, no baggage (`.pdb`, `.map`, logs, keys, `.env`), license files present, per-user installer without elevation; and in public mode a **valid trusted Authenticode signature with a timestamp, whose signer contains `DS1TR_EXPECTED_PUBLISHER`, on every DS1TR executable and artifact**;
5. the release manifest with hashes is written.

Verified here: in public mode the current unsigned build fails with 10 findings (no signature on any of five files, no expected publisher); a throwaway-signed build fails with `TEST-SIGNED (untrusted root)` and with a wrong publisher; `--allow-untrusted-root` exists only to test the pipeline and labels such builds TEST-SIGNED.

## 8. Local antivirus check and false positives

* `node tools/release/defender-scan.mjs <file-or-folder>` asks the installed **Microsoft Defender** for an on-demand scan (`MpCmdRun.exe -Scan -ScanType 3 -File ... -DisableRemediation`). It never changes a setting, never adds an exclusion, never turns protection off, never quarantines or deletes (remediation is disabled) and never uploads. If Defender is not the active antivirus or refuses, it says "NO RESULT", not "clean".
* **Result on the development machine: no result.** Microsoft Defender is not the active antivirus there (another product is registered), and `MpCmdRun` did not scan (exit 2, `0x80004005`). No clean or dirty verdict exists for any build to date; none is claimed.
* **Observed detection (2026-10-07, Norton 360 on the development machine).** While testing the signing pipeline, Norton blocked **both test copies** (`DsrDataTool.exe` and `FogHost.exe`) that had been signed with a throwaway self-signed certificate and started once (heuristic `IDP.HELU.PSD11`, file and process, two notifications; the copies were in a scratch folder and have been deleted). Nothing was excluded, restored, submitted or uploaded, and no Norton setting was touched. What is known: the **unsigned** packaged helpers, installer and portable were run many times on the same machine in the same session (full generations, Apply, Restore, install, uninstall) without a Norton block; the packaged files are intact (SHA-256 equal to the release manifest). What is **not** known: whether the self-signed signature (an unknown signer, which reputation heuristics distrust) or the freshly modified file caused it, and whether a build signed by a trusted certificate would be treated differently. This is exactly the case the false-positive workflow below is for; a self-signed signature must never be shipped, and the throwaway-certificate test must not be repeated on this machine without expecting a block.
* **VirusTotal policy:** nothing is uploaded to VirusTotal or any scanner automatically, ever. A private or unreleased build is never uploaded (uploads are shared with vendors and indexed). Only after a public release, and only by the owner, may the *published* hash be looked up (a hash lookup uploads nothing).
* **If a scanner flags a release (false-positive workflow):** (1) do not publish or keep distributing that build; preserve the artifact and its SHA-256 from the release manifest; (2) confirm the signature and the hash against the manifest (is this really our file?); (3) identify the product and the signature name; (4) the owner submits the file to the vendor as the software author: for Microsoft Defender through the Microsoft Security Intelligence "Submit a file" form (as a developer), for others through their false-positive forms; (5) rebuild from the tagged source if the file is not the one in the manifest; (6) record the case in `docs/IMPLEMENTATION_STATUS.md`. Never ask users to disable their antivirus or add exclusions.

## 9. Installer versus portable

| | Installer (recommended) | Portable (secondary) |
|---|---|---|
| Rights | none beyond the user's (per-user, `%LOCALAPPDATA%\Programs`, folder choice) | none |
| Start | normal start from the install folder; shortcuts; uninstaller | self-extracts to a temporary folder at **every** start and runs from there |
| Heuristics | install-folder execution, a standard installer | run-from-temp self-extraction, larger report surface |
| Updates | install the newer installer over it (tested: 0.25.0 then 0.26.0 silently, one uninstall entry, version 0.26.0, user data folder byte-for-byte unchanged); settings, history and runs live in the user data folder | replace the file |
| Uninstall | Windows Settings; keeps what the person generated (`deleteAppDataOnUninstall: false`) | delete the file; user data stays in the user data folder |
| Claim | does not prevent antivirus reports | same |

## 10. Provenance

`release-manifest.json` records the git commit, whether the tree was clean, the Electron, electron-builder and Node versions, the versions of every part, the Fog pin and the SHA-256 of every executable and artifact. It is build provenance, **not a claim of reproducible builds** (the installer embeds timestamps and the .NET single-file bundles are not bit-reproducible).

## 11. Not done, on purpose

No certificate was bought or requested; no self-signed certificate was installed anywhere or shipped; nothing was uploaded to a scanner; no setting of the security software was changed; no release was published and nothing was pushed.
