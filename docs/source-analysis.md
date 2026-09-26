# Upstream source analysis

## DarkSoulsItemRandomizer

Repository: https://github.com/HotPocketRemix/DarkSoulsItemRandomizer

Key observations:

- Python project with Tkinter UI in `randomizer_gui.py`.
- Actual data logic is split across modules such as `randomize_item_table.py`, `item_table.py`, `item_lot_param.py`, `shop_lineup_param.py`, and `chr_init_param.py`.
- DS1R output targets `param/GameParam/GameParam.parambnd.dcx`.
- The current implementation creates `.bak` files itself. Triple Randomizer should bypass/disable this behavior and own backups centrally.
- Best integration strategy: extract a headless `generate(config, seed, inputPath, outputPath)` service from the existing code, preserving the randomization algorithms.

## Dark-Souls-Enemy-Randomizer

Repository: https://github.com/everKalle/Dark-Souls-Enemy-Randomizer

Key observations:

- Python project. Tkinter UI is primarily in `randomizer.py`.
- Main engine exists as `Randomizer` in `randomizer_rng.py`.
- Engine code still imports Tkinter and directly assumes working-directory-relative game and `enemyRandomizerData` paths.
- Constructor performs environment checks, setup, backup/copy creation, and may patch the game executable. This means creating `Randomizer()` currently has side effects.
- It modifies MSB, LuaBND, EMEVD, FFX and GameParam-related data depending on options.
- Best integration strategy: introduce an explicit environment/context object and split constructor side effects into `inspect()` / `prepare()` / `generate()` phases. Replace Tkinter message/progress dependencies with callbacks/logger events.

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

FogMod's own documentation recommends Item -> Enemy -> Fog order. It also notes that some Item seeds are not solvable by Fog logic and some Enemy outputs can produce invalid MSB data for Fog processing. Therefore the final pipeline needs engine-specific retry scopes rather than just running three commands once.

Proposed retry behavior:

```text
create clean workspace snapshot
  -> items(seed_i)
  -> enemies(seed_e)
  -> fog dry-run(seed_f)
       item-logic failure -> regenerate Items (derived retry seed), then Enemy, then Fog
       invalid-MSB failure -> regenerate Enemy only, then Fog
       success -> Fog write -> validate -> atomic install
```

## Licensing boundary

FogMod: Apache-2.0 is present.

At the time of this analysis, no root `LICENSE` file was found through the GitHub repository interface for:

- HotPocketRemix/DarkSoulsItemRandomizer
- everKalle/Dark-Souls-Enemy-Randomizer

That does not prevent local technical experimentation, but redistribution of copied/modified upstream source should be clarified before a public release. The architecture therefore keeps each engine behind an adapter so we can initially require the user to provide/download the upstream mod independently if necessary.
