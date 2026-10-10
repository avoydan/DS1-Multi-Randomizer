# Nexus Mods publication draft

This is copy-ready page text and a launch checklist. **Do not upload or publish the current executable yet.** Nexus Mods' current [File Submission Guidelines](https://help.nexusmods.com/article/28-file-submission-guidelines) say internet-connected executables that download or send information/files are not allowed unless that network behavior is crucial to the utility, and explicitly say auto-update functionality does not qualify as crucial. They advise developers whose functionality depends on network transfers to contact staff and provide reasoning and source code. This app checks GitHub Releases for updates and can download 40 pinned Fog data files when needed, so the owner must obtain written staff guidance for this exact behavior before hosting a binary. The [Terms of Service](https://help.nexusmods.com/article/18-terms-of-service) also grant Nexus Mods and its partners a broad, transferable license to uploaded content; the owner must review that grant against GPL and upstream component rights before accepting it. The source-code page can be prepared locally; no Nexus account action or Terms acceptance was performed.

The draft description was prepared with AI assistance. Nexus's current categorization identifies AI-assisted/generated mod-page descriptions as **AI Media**; include that tag if this wording or an edited version is posted. Review whether the AI assistance in this task's code change also falls under **AI Assisted** and use that tag if required by Nexus staff. The project has substantial human-led source history and can provide it for review; do not claim the page or code was created without AI assistance.

## Mod title

**DS1 Multi Randomizer**

## Short summary

Create a reproducible Dark Souls Remastered world with randomized items, enemies, bosses, and world transitions using one Windows desktop app and one shareable Seed Code.

## Full description (draft)

DS1 Multi Randomizer combines item, enemy/boss, and world-transition randomization for Dark Souls Remastered on PC. Choose your options, generate a world from one master seed, and share its Seed Code so another player can reproduce the same settings and world.

The app generates files in a separate workspace. It does not change the game until you choose **Apply to game**. It keeps a pristine snapshot for **Restore original game**. Back up important saves before modding and play offline while randomized game data is active.

### Features

- Item and equipment randomization.
- Ordinary enemy and boss randomization, including configurable placement behavior.
- Fog/world-transition randomization.
- One master seed and shareable Seed Codes.
- Run history, presets, spoiler hint sheet, English and Polish interface.
- Local generation workspace and a restore path for managed game files.

### Requirements

- 64-bit Windows.
- Your own installed PC copy of Dark Souls Remastered.
- No separate Node.js or .NET installation is needed for the packaged app.
- PTDE is not supported by this release.

### Installation

1. Download the installer from the **Files** section after the Nexus-hosted build is approved for upload.
2. Run the installer as your normal Windows user. It installs per-user and does not require administrator access.
3. Start DS1 Multi Randomizer from the Start menu or desktop shortcut.

The installer is the recommended package. The portable build extracts to a temporary directory on each start and may receive more heuristic security warnings. Do not repackage or disable security software to address a warning.

### First run

1. Confirm the detected Dark Souls Remastered installation path.
2. Review the default settings or create a preset.
3. Choose **Randomize** and wait for validation to finish.
4. Back up important saves, then choose **Apply to game** when you are ready.
5. Launch the game and play offline while randomized data is active.

### Updating

The app checks public GitHub release metadata manually and, when enabled in settings, automatically. Automatic checks can be turned off. Update files are downloaded only after choosing **Download update**; installation requires **Restart and update**. The current build also retrieves pinned Fog data over HTTPS the first time Fog/world randomization needs files that are not already cached. Nexus Mods staff approval for this network behavior is a prerequisite to hosting a binary here.

### Uninstall

Use Windows **Installed apps** to uninstall. Uninstalling does not automatically restore the game's files. If you want to return the game to its original state, use **Restore original game** before uninstalling and keep the app's user data until the restore is complete.

### Restore original game

Open the app and choose **Restore original game**. The app restores managed files from its pristine snapshot. Do this before uninstalling if you want to remove the randomizer while returning the game to vanilla data.

### Compatibility and limitations

- Dark Souls Remastered for PC is the target. PTDE is outside the supported scope.
- Do not use the app while the game is running or while another tool is changing the same game files.
- Only the game files managed by the randomizer are restored; unrelated files are not changed.
- Some settings are limited by the game's data structure and are explained in the app's tooltips.
- An internet connection is needed for update checks and, when the Fog cache is empty, for the pinned Fog data download. Generated game files are otherwise local.

### Online-play warning and save backups

Do not connect to online play with modified game data. Back up important saves before using mods. The randomizer changes game data after an explicit Apply action; it does not change the user's save files.

### FAQ

**Does the app include Dark Souls Remastered files?** No. It uses the user's own installed game. No game executables, maps, events, parameter files, or generated game packages are included.

**Can I share a world?** Yes. Share its Seed Code. It contains the master seed and settings needed to reproduce the world; it does not contain personal data.

**Does uninstalling restore the game?** No. Choose **Restore original game** first if you want to return managed game files to their pristine state.

**Why does Windows show a publisher or antivirus warning?** Public releases are currently unsigned. Check the published SHA-256 and use the vendor's official false-positive process if a specific file appears incorrectly detected. Do not disable protection or add an exclusion.

**Does the app send my settings, saves, or game files to the developer?** No. The app has no telemetry, analytics, account, crash reporting, or automatic support upload. GitHub receives ordinary network metadata for release checks and pinned Fog data downloads.

### Source code and issue tracker

- Source and matching release tags: https://github.com/avoydan/DS1-Multi-Randomizer
- Issues: https://github.com/avoydan/DS1-Multi-Randomizer/issues
- License and third-party notices: `LICENSE`, `THIRD_PARTY_NOTICES.md`, and `docs/LICENSING.md` in the source repository.

### Credits and licenses

DS1 Multi Randomizer is GPL-3.0-only. The combined Items/Enemies integration selectively vendors `kellernz1/ds-randomizer` under GPL-3.0-only. Fog randomization uses a headless host around pinned `thefifthmatt/FogMod` and `thefifthmatt/SoulsIds` sources under Apache-2.0. The application also includes/notices JuicerMV.SoulsFormats (GPL-3.0-only), YamlDotNet, Newtonsoft.Json and ZstdSharp.Port (MIT), Electron, Chromium, Node.js, and the .NET runtime under their respective licenses. The release contains the complete notices and license files. Credits do not replace any license or permission requirement.

This is an unofficial community tool and is not affiliated with or endorsed by FromSoftware or Bandai Namco Entertainment.

### Permissions

The project source is available under GPL-3.0-only. Reuse and redistribution of project code must follow that license and preserve applicable notices. Third-party components retain their own licenses and notices. This page does not grant permission to redistribute Dark Souls Remastered content or another mod author's files; none is included in the release.

### Version and checksums

When approved for Nexus hosting, identify the exact version and link the matching public source tag. The release's `SHA256SUMS.txt` lists the checksums for the published Setup, Portable, blockmap, update metadata, and release manifest. On Windows, calculate a file hash with `Get-FileHash <filename> -Algorithm SHA256` and compare the result to the same filename in the sums file.

### Changelog template

**vX.Y.Z — YYYY-MM-DD**

- Added or changed:
- Fixed:
- Compatibility notes:
- Source tag:
- SHA-256 list:

### SmartScreen and antivirus note

Current public builds are unsigned, so Windows or antivirus software may show warnings. A matching SHA-256 verifies file integrity against the release checksum, not publisher identity or safety. See the project's false-positive reporting guide. Never disable antivirus, add exclusions, or bypass SmartScreen to install this tool.

## Recommended file layout

No official guideline reviewed here prefers an installer EXE or portable EXE format. If Nexus staff approves the current application behavior, use the **Setup x64 installer as the main file** because it installs per-user and is the recommended user path; add Portable as an optional file only if it serves a clear audience. Do not wrap or repack executables merely to influence scanning. Wait for Nexus scanning/review and resolve any request through staff; do not attempt to bypass the scanner. Do not upload until there is a working, approved file—Nexus does not permit public placeholder pages.

## Materials checklist for the owner

1. Header/banner using project-owned artwork and no copyrighted game art unless rights are confirmed.
2. Square project icon using the current project icon or original project artwork.
3. Screenshots in this order: (1) main randomizer screen with options visible, (2) example generated-world summary or hint sheet with spoiler level disclosed, (3) Apply/Restore safety explanation or app About/version screen. Use screenshots from the actual public build; crop out usernames, paths, and save data.
4. Exact release version, matching source tag, `SHA256SUMS.txt`, credits, license links, and a verified functional download.

## Manual steps on Nexus Mods

1. Contact `support@nexusmods.com` with the source code and a precise explanation of the update-check and Fog-data requests. Ask whether Nexus permits hosting this exact utility/build under the current rule and what, if any, product change would make it eligible.
2. Wait for written staff guidance. Do not create a public file page or upload the executable before receiving it.
3. Review the [Terms of Service, especially its upload license](https://help.nexusmods.com/article/18-terms-of-service), with the project's GPL and third-party license obligations. Do not accept if the owner cannot grant the required rights in every uploaded component.
4. If both the policy review and license review are acceptable, sign in, select Dark Souls Remastered, create the mod page, choose category/tags, paste the reviewed text, add project-owned images, upload the exact approved release files, and complete Nexus's required file/permissions fields.
5. Apply the applicable Nexus AI tags (AI Media for AI-assisted page copy; assess AI Assisted for code), review the page and scanner result, and make sure all claims and credits are accurate.
6. Accept Nexus terms and publish only when the owner personally reviews and confirms the final page and file.

### Suggested support request

> We maintain DS1 Multi Randomizer, a GPL-3.0-only Windows tool for Dark Souls Remastered. Its source is https://github.com/avoydan/DS1-Multi-Randomizer. It performs optional GitHub release checks (automatic checks can be disabled; downloads require user action) and downloads 40 pinned, SHA-256-verified FogMod data files when the user selects Fog/world randomization and the local cache is absent. It does not upload game files, settings, saves, or support bundles. The app cannot perform Fog randomization without those external data files. Could Nexus review this exact network behavior and advise whether the tool may be hosted, and whether any changes are required? We have not uploaded a binary or published a page pending your guidance.
