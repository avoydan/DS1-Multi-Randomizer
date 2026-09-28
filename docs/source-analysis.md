# Upstream source analysis

## Approved engine direction

`kellernz1/ds-randomizer` is the primary Items + Enemies engine. It receives
a named SHA-256 derivation of the master seed (32 hex characters, compliant
with upstream's seed charset) and performs its own versioned, named-stream
derivation from it. The application derives a separate Fog retry stream.

> Superseded (2026-09-28, Task 6): this previously said the combined engine
> receives the normalized master seed directly. See `docs/DECISIONS.md`.

The combined engine and FogMod are one sequential pipeline:

```text
combined package -> materialized workspace -> compatibility bridge -> FogMod
```

Fog compatibility is not yet proven. In particular, FogMod must preserve
combined EMEVD changes and receive reliable key-location information.

## DarkSoulsItemRandomizer

Repository: https://github.com/HotPocketRemix/DarkSoulsItemRandomizer

Key observations:

- Python project with Tkinter UI in `randomizer_gui.py`.
- Actual data logic is split across modules such as `randomize_item_table.py`, `item_table.py`, `item_lot_param.py`, `shop_lineup_param.py`, and `chr_init_param.py`.
- DS1R output targets `param/GameParam/GameParam.parambnd.dcx`.
- The current implementation creates `.bak` files itself. Triple Randomizer should bypass/disable this behavior and own backups centrally.
- Role: behavior and option reference only. Do not copy, modify, or redistribute
  source without written permission.

## Dark-Souls-Enemy-Randomizer

Repository: https://github.com/everKalle/Dark-Souls-Enemy-Randomizer

Key observations:

- Python project. Tkinter UI is primarily in `randomizer.py`.
- Main engine exists as `Randomizer` in `randomizer_rng.py`.
- Engine code still imports Tkinter and directly assumes working-directory-relative game and `enemyRandomizerData` paths.
- Constructor performs environment checks, setup, backup/copy creation, and may patch the game executable. This means creating `Randomizer()` currently has side effects.
- It modifies MSB, LuaBND, EMEVD, FFX and GameParam-related data depending on options.
- Role: behavior and option reference only. Do not copy, modify, or redistribute
  source without written permission.

## FogMod

Repository: https://github.com/thefifthmatt/FogMod

Key observations:

- C# project.
- Core is already cleanly callable through `Randomizer.Randomize(RandomizerOptions, FromGame, gameDir, outDir)`.
- `Randomizer.cs` already supports dry-run behavior before writing game files.
- `RandomizerOptions` is independent of WinForms.
- The repository includes Apache License 2.0 in `FogMod/LICENSE`.
- Current `Program.cs` command-line path contains a suspicious forced assignment `game = FromGame.DS3;` after parsing DS1/PTDE selection. Therefore the existing CLI entrypoint must not be trusted as-is for DS1R integration.
- Best integration strategy: create a tiny dedicated console host around the existing `Randomizer` class with JSON input/output. Do not automate WinForms.

## Compatibility facts that shape our pipeline

FogMod's documentation recommends legacy Item -> Enemy -> Fog order, but this
does not establish compatibility with the combined engine. Both combined and
FogMod modify MSB, EMEVD, and GameParam data. File-overlay merging is not safe.

Proposed retry behavior:

```text
create clean workspace snapshot
  -> combined(master seed, attempt)
  -> materialize combined package
  -> compatibility validation/bridge
  -> fog dry-run(derived retry seed)
       upstream incompatibility -> retry the combined stage as one unit
       fog-only rejection -> retry Fog with a deterministic attempt
       success -> Fog write -> final validation -> atomic activation
```

## Licensing boundary

FogMod: Apache-2.0 is present.

At the time of this analysis, no root `LICENSE` file was found through the GitHub repository interface for:

- HotPocketRemix/DarkSoulsItemRandomizer
- everKalle/Dark-Souls-Enemy-Randomizer

Their behavior can inform independently implemented GPL extensions, but their
source and binaries are not distribution dependencies.
