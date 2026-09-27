# Third-party notices

DS1 Triple Randomizer is licensed under GPL-3.0-only.

No Dark Souls Remastered game data is included in this repository. A
selective, pinned vendor of `kellernz1/ds-randomizer` (the files needed to
build `DsrDataTool.dll`, plus the minimal JavaScript generation core) is
included under `engines/combined/vendor/` — see
`engines/combined/vendor/UPSTREAM_PIN.md` for the exact pinned commit, the
file list, and the blob-identity verification record.

| Component | Upstream | License | Distribution policy |
| --- | --- | --- | --- |
| DSR Randomizer | kellernz1/ds-randomizer | GPL-3.0-only | Selectively vendored at a pinned commit under `engines/combined/vendor/`; preserve source, history where practical, license, and notices. A public fork with a source tag matching each distributed binary is still required before distribution (see `docs/LICENSING.md`). |
| SoulsFormats (via `JuicerMV.SoulsFormats` NuGet package, used by `DsrDataTool`) | JKAnderson/SoulsFormats (distributed as JuicerMV/SoulsFormats) | GPL-3.0-only | Not vendored; restored from NuGet at build time. Dependency notices and license terms must accompany distributed binaries. |
| FogMod | thefifthmatt/FogMod | Apache-2.0 | Preserve its LICENSE, NOTICE if present, attribution, and modification notices. |
| DarkSoulsItemRandomizer | HotPocketRemix/DarkSoulsItemRandomizer | No explicit repository license found | Reference behavior only; do not vendor or redistribute without written permission. |
| Dark-Souls-Enemy-Randomizer | everKalle/Dark-Souls-Enemy-Randomizer | No explicit repository license found | Reference behavior only; do not vendor or redistribute without written permission. |

Dark Souls, Dark Souls Remastered, and related names and assets are property of
their respective owners. This project is unofficial and is not affiliated with
or endorsed by FromSoftware or Bandai Namco Entertainment. The application must
operate on a user's own installation and must not distribute game binaries,
assets, or generated packages containing copyrighted game data.
