# Third-party notices

DS1 Triple Randomizer is licensed under GPL-3.0-only.

No Dark Souls Remastered game data is included in this repository. A
selective, pinned vendor of `kellernz1/ds-randomizer` (the files needed to
build `DsrDataTool.dll`, plus the minimal JavaScript generation core) is
included under `engines/combined/vendor/` — see
`engines/combined/vendor/UPSTREAM_PIN.md` for the exact pinned commit, the
file list, and the blob-identity verification record. FogHost
(`tools/fog-host/`) vendors selected FogMod and SoulsIds engine sources at
pinned commits; see `tools/fog-host/vendor/UPSTREAM_PIN.md`. FogMod's
game-derived `dist/` data is not included; the user supplies it.

| Component | Upstream | License | Distribution policy |
| --- | --- | --- | --- |
| DSR Randomizer | kellernz1/ds-randomizer | GPL-3.0-only | Selectively vendored at a pinned commit under `engines/combined/vendor/`; `tools/DsrDataTool/Program.cs` carries a documented local modification (skip-count fix, GPL-3.0 §5(a) notice, `vendor/patches/`); preserve source, history where practical, license, and notices. A public fork with a source tag matching each distributed binary is still required before distribution (see `docs/LICENSING.md`). |
| SoulsFormats (via `JuicerMV.SoulsFormats` 0.1.3 NuGet package, used by `DsrDataTool` and FogHost) | JKAnderson/SoulsFormats (distributed as JuicerMV/SoulsFormats) | GPL-3.0-only | Not vendored; restored from NuGet at build time. Dependency notices and license terms must accompany distributed binaries. |
| FogMod | thefifthmatt/FogMod | Apache-2.0 | Engine sources vendored at `6e6dc78` under `tools/fog-host/vendor/FogMod/` with its LICENSE; `GameDataWriter.cs` is modified and carries an Apache-2.0 §4(b) change notice. `dist/` data (FromSoftware-derived) is not vendored or distributed. |
| SoulsIds | thefifthmatt/SoulsIds | Apache-2.0 | Sources vendored unmodified at `e31a6fd` under `tools/fog-host/vendor/SoulsIds/` with its LICENSE. |
| YamlDotNet | aaubry/YamlDotNet | MIT | Not vendored; NuGet 7.0.0, used by FogHost. License text must accompany distributed binaries. |
| Newtonsoft.Json | JamesNK/Newtonsoft.Json | MIT | Not vendored; NuGet 13.0.3, used by FogHost (via SoulsIds). License text must accompany distributed binaries. |
| DarkSoulsItemRandomizer | HotPocketRemix/DarkSoulsItemRandomizer | No explicit repository license found | Reference behavior only; do not vendor or redistribute without written permission. |
| Dark-Souls-Enemy-Randomizer | everKalle/Dark-Souls-Enemy-Randomizer | No explicit repository license found | Reference behavior only; do not vendor or redistribute without written permission. |

Dark Souls, Dark Souls Remastered, and related names and assets are property of
their respective owners. This project is unofficial and is not affiliated with
or endorsed by FromSoftware or Bandai Namco Entertainment. The application must
operate on a user's own installation and must not distribute game binaries,
assets, or generated packages containing copyrighted game data.
