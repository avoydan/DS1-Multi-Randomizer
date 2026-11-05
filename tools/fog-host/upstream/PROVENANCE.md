# Bundled upstream annotation: `fog.txt`

`fog.txt` in this folder is **FogMod's `dist/fog.txt`, unmodified**, bundled for generation profile `ds1tr-gen-v7` so that V7 works offline in every distribution (GitHub and Nexus) with no download and no prior cache.

| | |
|---|---|
| Repository | https://github.com/thefifthmatt/FogMod |
| Pinned commit | `6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f` (2020-01-21, the last DS1-only commit) |
| Path | `dist/fog.txt` |
| Git blob (SHA-1) | `83beace06434ce98e33eaeecde86f17c810dd096`, 147516 bytes: identical to the entry GitHub's tree API reports for that path at that commit |
| SHA-256 (CRLF normalized to LF, as FogHost pins it) | `77a10b4015c51bdfbe1499f38344c85540a63bfa1ef6370ad9c1ebdc9a2d2af6` |
| Licence | Apache-2.0: the repository's root `LICENSE` (git blob `e454a52586f29b8ce8a6799163eac1f875e9ac01`, the standard Apache-2.0 text, shipped as `FogMod-Apache-2.0.txt`); there is no `NOTICE`, no per-directory licence and no exclusion for `dist/` |

## Why this is redistributable (the verification, 2026-11-05)

- It is a **directly authored annotation file**, not game data: Areas, links, tags, Entrances, Warps, key items, lot locations, enemy groups and conditions that the author wrote by hand. It contains no FromSoftware binary, script or extracted asset.
- All 9 commits that ever touched `dist/fog.txt` up to the pin are by one author, the repository owner (Matthew Gruen), so the licensor of the repository is the author of the file.
- The repository has one licence, at its root, and nothing excludes `dist/`; the author's own release package (`FogMod.zip`, README "Installation") ships `dist` next to the program.
- The bytes in this folder are the bytes of the pinned blob (checked against GitHub's tree API), so what is bundled is exactly what was licensed.

## What is NOT bundled

Every other file of FogMod's `dist` (4 layouts, 18 event files plus `common`, 17 talk scripts, and the game-derived maps and `GameParam`) is **not** bundled: events and talk scripts are FromSoftware data edited by the author, and the layouts' scope is not separately stated. V7 rebuilds them on the player's machine from the installed game (`docs/V7_UPSTREAM_PARITY.md`, "Locally rebuilt files").

## Changes

None. Apache-2.0 section 4(b) does not apply (no modification). The file is read by FogHost through the same YAML reader FogMod uses.
