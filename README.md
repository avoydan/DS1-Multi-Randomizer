# DS1 Multi Randomizer

DS1 Multi Randomizer is a Windows desktop app for creating one deterministic Dark Souls Remastered world with randomized items, enemies and bosses, and world transitions.

## Download

[Download latest release — v0.40.0](https://github.com/avoydan/DS1-Multi-Randomizer/releases/tag/v0.40.0) · [All releases](https://github.com/avoydan/DS1-Multi-Randomizer/releases)

The release is a **public prerelease**. The recommended file is `DS1-Multi-Randomizer-0.40.0-Setup-x64.exe`. A portable executable is also provided; it extracts to a temporary folder each time it starts, which can trigger more antivirus warnings.

## What it does

- Combines item, ordinary enemy, boss, and Fog/world-transition randomization in one run.
- Uses one master seed. A Seed Code records the settings needed to reproduce the same world and can be loaded by another player.
- Keeps run history, presets, and a spoiler hint sheet; supports English and Polish.
- Generates into a workspace and can restore the original managed game files from its pristine snapshot.

The **Your game** screen also has an optional, local **Skill Issue Mode** convenience. When enabled, it gives new characters an overpowered starting loadout for exploration. It is off by default, stays on this device, and does not change the randomized world or Seed Code.

## Requirements and safe use

- 64-bit Windows and your own installed copy of Dark Souls Remastered.
- No separate Node.js or .NET installation is needed for the packaged app.
- Back up important saves before using mods. The app edits managed game data only after **Apply to game**; use **Restore original game** to put back the pristine files.
- Play offline while randomized game data is active. The project is unofficial and is not affiliated with FromSoftware or Bandai Namco Entertainment.

## Verify a download

Each release includes `SHA256SUMS.txt` and a versioned release manifest. In PowerShell, calculate a file's hash with:

```powershell
Get-FileHash .\DS1-Multi-Randomizer-0.40.0-Setup-x64.exe -Algorithm SHA256
```

Compare the `Hash` value with the line for that exact filename in `SHA256SUMS.txt`. A matching hash shows that the file matches the published checksum; it is not a code signature or a safety certification.

### Verify where a release came from (releases after 0.40.0)

Releases built after v0.40.0 carry a GitHub **build provenance attestation** for the Setup, the Portable build and the other release files. With the [GitHub CLI](https://cli.github.com/) (`gh`; run `gh auth login` first if it asks you to sign in), run this in the folder with the download:

```powershell
gh attestation verify .\DS1-Multi-Randomizer-X.Y.Z-Setup-x64.exe --repo avoydan/DS1-Multi-Randomizer --signer-workflow avoydan/DS1-Multi-Randomizer/.github/workflows/release.yml
```

Replace `X.Y.Z` with the version you downloaded, and repeat the command for `DS1-Multi-Randomizer-X.Y.Z-Portable-x64.exe`. A success means that GitHub's records show this exact file was produced by that workflow in that repository (the attestation names the commit). It is **not** an Authenticode signature, not an antivirus scan and not a safety review, and it does not remove Windows SmartScreen or antivirus warnings. **v0.40.0 and earlier releases have no attestation**, so the command fails for them.

## Code signing and antivirus

**The latest release is unsigned.** No SignPath application has been approved and no trusted code-signing certificate is active. Windows SmartScreen or antivirus software may warn about the download. We do not recommend disabling protection or adding an exclusion. See [false-positive reporting and verification](docs/ANTIVIRUS_FALSE_POSITIVES.md), [release trust](docs/RELEASE_TRUST.md), and the [code-signing preparation](docs/CODE_SIGNING.md).

## Privacy and network use

There is no telemetry, analytics, account, crash reporting, or automatic support-bundle upload. Update checks contact GitHub Releases; automatic checks can be turned off, and update files are downloaded only after the user requests them. Fog/world randomization downloads 40 checksum-verified data files from a pinned FogMod commit when needed and caches them locally. Network requests expose ordinary connection metadata to GitHub. Read the full [privacy and security statement](docs/SECURITY_AND_PRIVACY.md).

## Source and license

The source code for each release is available in this repository at its matching tag. DS1 Multi Randomizer is licensed under [GPL-3.0-only](LICENSE). See [third-party notices](THIRD_PARTY_NOTICES.md) and the [licensing policy](docs/LICENSING.md) for upstream components and distribution terms.

Issues: [GitHub issue tracker](https://github.com/avoydan/DS1-Multi-Randomizer/issues).

## Further information

- [Installation, updates, and release process](docs/AUTO_UPDATE.md)
- [Release trust and executable inventory](docs/RELEASE_TRUST.md)
- [Nexus Mods publication draft and review status](docs/NEXUS_MODS.md)
