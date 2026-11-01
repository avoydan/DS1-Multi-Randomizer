# Nexus Mods page draft: DS1 Multi Randomizer

**Draft for the owner. Not published.** Lines marked **[OWNER]** need a decision or a fact only the owner has. This text was drafted with AI assistance: if you post it (even edited), apply Nexus's **AI Media** tag, and choose the honest code tag as described in [NEXUS_MODS.md](NEXUS_MODS.md). Check every claim against the final build before posting. Nexus's current rules and the readiness status are in [NEXUS_MODS.md](NEXUS_MODS.md); the gates are in [PUBLICATION_CHECKLIST.md](PUBLICATION_CHECKLIST.md).

Page title: **DS1 Multi Randomizer**

Short summary (one line): *One Windows app that randomizes items, enemies, bosses and fog-gate/world transitions in Dark Souls Remastered from a single shareable Seed Code.*

Requirements to enter in Nexus's requirements field: Dark Souls Remastered (PC). No other mod is required.

Tags/flags: [OWNER] AI Media (page text) and the code tag; no adult content.

---

# Overview

DS1 Multi Randomizer is a desktop tool for **Dark Souls Remastered on PC**. It combines three kinds of randomization that are usually separate tools:

- **Items**: where items and equipment are found.
- **Enemies and bosses**: which enemies and bosses appear where.
- **Fog / world transitions**: where fog gates lead, so the world itself is rearranged.

You choose your options, press one button, and the app prepares a complete randomized game from **one master seed**. The result is described by a **Seed Code** you can share, so a friend gets the same world and settings. Nothing in the game is changed until you choose **Apply to game**, and **Restore original game** puts the managed files back.

This is an unofficial community tool, not affiliated with or endorsed by FromSoftware or Bandai Namco. It contains no Dark Souls game files: it works on your own installed copy.

# Key features

- Item, enemy, boss and fog-gate randomization in one run, from one master seed.
- Shareable, reproducible Seed Codes.
- Presets, run history and a spoiler hint sheet.
- Generates into a separate workspace; changes the game only when you press **Apply to game**.
- **Restore original game** from a pristine snapshot kept by the app.
- English and Polish interface.
- Optional **Skill Issue Mode** for exploring (off by default).
- Open source (GPL-3.0-only).

# Requirements

- Windows, 64-bit.
- Your own installed copy of **Dark Souls Remastered** (PC). Dark Souls: Prepare to Die Edition is **not** supported.
- You do not need to install Node.js or .NET separately.
- Back up your saves first, and play **offline** while randomized game data is in place.

# Installation

1. Download the **Main file** (the Setup, in a ZIP) and extract the ZIP.
2. Run `DS1-Multi-Randomizer-X.Y.Z-Nexus-Setup-x64.exe`. It installs just for your Windows user and does **not** need administrator rights.
3. Start **DS1 Multi Randomizer** from the Start menu.

Windows may show an "unknown publisher" or SmartScreen warning because the file is not code-signed. See "Antivirus and SmartScreen" below. Do not turn off your antivirus to install it.

# Portable version (optional)

The **Optional file** is a portable executable that does not install anything. It unpacks itself to a temporary folder every time it starts, which is slower and is more likely to trigger antivirus warnings. Most people should use the Setup.

# First run

1. Let the app find your Dark Souls Remastered folder, or point it to the folder that contains `DarkSoulsRemastered.exe`.
2. Review the default options or load a preset.

# How to generate a randomized game

1. Choose options for Items, Enemies/Bosses and Fog.
2. Press **Randomize** and wait for the checks to finish. Your game is not changed yet.
3. Note the **Seed Code**.
4. Close the game, back up your saves, then press **Apply to game**.
5. Start the game and play **offline**.
6. To go back to vanilla, press **Restore original game**.

# Seed Codes

A Seed Code (it starts with `DS1TR1-`) contains the master seed, the options and the version of the generator rules that made your world, and nothing personal. Give it to another player of the same app version family and they can load it to reproduce the same world. Worlds are deterministic: the same Seed Code gives the same result.

# Skill Issue Mode

An **optional** convenience that gives a new character a strong starting loadout so you can explore the randomized world. It is **off by default**, stays on your computer, and does not change the randomized world or the Seed Code.

# Compatibility

- Supported: Dark Souls Remastered on PC, including its Artorias of the Abyss content; the app can leave the DLC out of the randomization.
- Not supported: Dark Souls: Prepare to Die Edition, other games, and other platforms.
- Do not run the app while the game is running or while another tool is editing the same game files. This tool changes game data, so it does not mix with other mods that edit the same files.
- Seed Codes created by older generator versions still load. **The Nexus edition cannot reproduce the very oldest Fog Seed Codes** (versions 1 and 2) because those need data that only the GitHub edition downloads; the app tells you so.

# Known limitations

- **Occasionally a seed needs a reroll.** Fog randomization checks, before it gives you a world, that the whole map can be reached from the Asylum start. If it cannot find a layout that passes within its five automatic attempts, the app says so and asks you to try another seed or change Fog options. In the project's tests this is rare, but it is not impossible, and it is not a crash.
- Fog layouts have been checked by automated generation tests; they have not been played through completely in every combination. Report anything that looks wrong.
- Randomized worlds can be hard or unfair. That is part of randomizers.

# Updates

The Nexus edition **does not check for updates and does not use the internet at all**. To update, download the new version from this page. The separate edition on GitHub can check GitHub for new releases; that option is not in the file you downloaded here. Each new version is a new file here; older versions stay available under Old files.

# Security and trust

- **Open source:** the full source code is at https://github.com/avoydan/DS1-Multi-Randomizer, with a tag for each release. [OWNER: link the exact tag for the uploaded version.]
- **Checksums:** every file's SHA-256 is listed below. In PowerShell: `Get-FileHash .\FILE.zip -Algorithm SHA256`.
- **Nexus scan:** this page shows Nexus's virus scan result next to each file. A clean scan is not a guarantee, and an alert on a new unsigned file is possible; see the next section.
- **No telemetry:** the application contains no analytics, tracking, account or crash-reporting code, and does not upload your settings, saves, seeds or game files. The source is public so you can check.
- **Network:** this edition makes no network connections of its own. We observed only local traffic between the app window and its local backend (127.0.0.1). The GitHub edition checks GitHub for updates.
- **No administrator rights:** the installer is per-user and the app runs as your normal Windows user.
- **GitHub build provenance:** releases published on GitHub after v0.40.0 carry GitHub build provenance attestations (which repository and workflow built the file, from which commit). **The file on this page is built separately and is not covered by those attestations. [OWNER: replace this sentence if the Nexus build is later attested.]** Provenance is not a signature or a safety guarantee in any case.
- **Not code-signed:** see below.

# Antivirus and SmartScreen

The files are **not code-signed** and are new, so Windows SmartScreen and some antivirus programs may warn about them or flag them wrongly, especially the portable version. A matching SHA-256 tells you the file is the one that was published; it does not prove it is safe. **Do not disable your antivirus or add exclusions to run it.** If you are unsure, build it yourself from the public source, or wait. If a product flags a file, you can report it as a false positive through that vendor's official process; details are in the repository: docs/ANTIVIRUS_FALSE_POSITIVES.md.

# Backup and restore

- The app first takes a **pristine snapshot** of the game files it manages. Generation happens in a separate workspace, and the game is touched only when you press **Apply to game**.
- It changes game data files (maps, events, parameters and similar) inside your Dark Souls Remastered folder. It does not change your save files, but you should back up saves before using any mod.
- **Restore original game** puts the managed files back from the snapshot. Do this before uninstalling if you want a vanilla game again.
- If you ever need to go back by hand, use Steam's **Verify integrity of game files**.

# Uninstall

1. In the app, press **Restore original game**.
2. Uninstall **DS1 Multi Randomizer** in Windows **Settings > Apps > Installed apps** (portable: just delete the executable).
3. Uninstalling does not delete the app's data. To remove it too, delete the folder `%APPDATA%\ds1-triple-randomizer-desktop` after you have restored the game.

# Source code

https://github.com/avoydan/DS1-Multi-Randomizer (GPL-3.0-only). Issues and suggestions are welcome there.

# Verify your download

1. SHA-256 of the file you downloaded: `Get-FileHash .\FILE.zip -Algorithm SHA256`; compare with the list here. [OWNER: paste the exact file names and hashes of the uploaded ZIPs and of the executables inside them.]
2. GitHub edition only (not this file): releases after v0.40.0 can be checked with the GitHub CLI: `gh attestation verify .\DS1-Multi-Randomizer-X.Y.Z-Setup-x64.exe --repo avoydan/DS1-Multi-Randomizer --signer-workflow avoydan/DS1-Multi-Randomizer/.github/workflows/release.yml`. This shows which workflow and commit built that file. It is not a signature, a virus scan or a safety review.

# Bug reports

Report problems on GitHub (https://github.com/avoydan/DS1-Multi-Randomizer/issues) or in the Nexus comments. Please include:

- the **application version** (About screen);
- your **Seed Code** and, if shown, the generation profile;
- the **exact error message** and what you did just before;
- optionally the **support bundle** from the app. Look through it first: remove anything personal, and never post saves or your Windows user name or folder paths publicly.

Security problems: do not post them publicly; see SECURITY.md in the repository.

# Credits

- **kellernz1/ds-randomizer** (GPL-3.0): the item and enemy randomization engine this project builds on.
- **thefifthmatt**: FogMod and SoulsIds (Apache-2.0), the basis of the fog/world randomization.
- **JKAnderson's SoulsFormats** (via JuicerMV.SoulsFormats, GPL-3.0): reading and writing Dark Souls file formats.
- **YamlDotNet**, **Newtonsoft.Json**, **ZstdSharp.Port** (MIT), **Electron**, **React** and the npm/.NET ecosystem.
- Earlier randomizers (HotPocketRemix's Item Randomizer and everKalle's Enemy Randomizer) inspired the feature set. No code from them is included.
- FromSoftware made Dark Souls; this project is unofficial.

Full notices: THIRD_PARTY_NOTICES.md in the repository and the licenses folder inside the app.

# Changelog

**X.Y.Z [OWNER: final version]**

- New worlds use the newest generation rules (`ds1tr-gen-v6`). They fix a start trap: the first wall in the Asylum could lead to a dead-end area whose only way out led back, which left the whole run stuck. The Fog safety check is now a general "everything is reachable from the start" rule and the earlier Ceaseless-only check is retired. Earlier Seed Codes keep generating exactly as before.
- Older (v3 and v4) Seed Codes with Fog work again.
- The optional free-cursor helper (F10) now starts only while Dark Souls Remastered is running, and only if you turned it on.
- Optional Skill Issue Mode.
- Source tag: [OWNER].
- SHA-256: [OWNER].
