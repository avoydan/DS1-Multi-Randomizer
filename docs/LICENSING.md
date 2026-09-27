# Licensing and distribution policy

DS1 Triple Randomizer is licensed under GPL-3.0-only.

## Combined engine

The production integration may use and modify `kellernz1/ds-randomizer` under
GPL-3.0-only. A distributed binary must have corresponding source available for
the exact released version, including our modifications and required build and
installation scripts.

The fork must preserve:

- upstream copyright and license notices;
- Git history where practical;
- `THIRD_PARTY_NOTICES.md`;
- an exact pinned commit in the application manifest;
- a public source tag matching each distributed binary.

## FogMod

FogMod is Apache-2.0 and is compatible with a GPLv3 distribution. When it is
vendored, preserve its license, copyright notices, NOTICE file if present, and
clearly identify modified files.

## Legacy randomizers

`HotPocketRemix/DarkSoulsItemRandomizer` and
`everKalle/Dark-Souls-Enemy-Randomizer` do not currently expose an explicit
repository license. Do not vendor, modify, or redistribute their code or
binaries without written permission.

Legacy behavior may be used as a product requirement. Missing behavior must be
implemented independently in the GPL combined fork without copying legacy
source.

## Game data

The repository and releases must not contain:

- Dark Souls Remastered executables;
- maps, events, scripts, params, textures, audio, or other game assets;
- extracted game catalogs containing redistributable game content;
- pre-generated randomized packages containing modified game files;
- DRM or security-circumvention code.

Users must provide their own installation. All extraction and patch generation
must happen locally in a disposable workspace. Releases should include a clear
unofficial-project disclaimer and recommend offline play with modified data.
