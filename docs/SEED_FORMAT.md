# Seed Codes, run history and saved settings

A **Seed Code** names one exact world: it contains everything needed to generate it again, on any machine, offline, in any later
version of the application that still supports its generation profile (`docs/GENERATION_COMPATIBILITY.md`).

```
DS1TR1-06NJDCP6-977WYJ81-9FM6D9H6-MP6XCEN3-PQ76HB9K-BBNGS2TT-0W030W1B-2G
```

(Items + Enemies + Bosses + world randomizer with every default. A broad configuration with a custom enemy pool is about 200
characters.)

## What a code contains

| In the code | Why |
|---|---|
| seed format version (the `1` of `DS1TR1`) | the container itself can evolve without breaking old codes |
| generation profile id (`ds1tr-gen-v1`) | selects the exact deterministic behavior (engines, seed derivation, retries) |
| internal master seed | the existing named seed derivation runs from it unchanged (`SHA256(master + ":items")`, ...) |
| the **complete resolved settings** | every generation-affecting option, explicitly; **not** a difference from today's defaults |
| checksum | detects typos and truncation before any work is done |

Settings are the canonical set (`packages/contracts/src/generation.ts`): all item options, all enemy options including the
sampling placement options and the custom pool, all world randomizer options including the Fog message language, and `includeDlc`.

### Not in the code (and why)

| Field | Reason |
|---|---|
| `gamePath` | machine specific; the world does not depend on where the game is installed |
| `generateSpoilerLog` | only decides whether `spoiler.txt` is written; every placement is identical with or without it |
| UI language, window state, run folders, backup state | not part of generation; never stored |
| application version | deliberately not an input of generation |

`GENERATION_FIELD_POLICY` lists **every** leaf of `RandomizerConfig` as `seed`, `envelope` or `excluded: <reason>`. The test
`seedCode.test.mjs` fails when a new option is added to the configuration without a decision, so a setting that changes the
world cannot be forgotten in the Seed Code.

## Encoding (format 1) — frozen

1. Envelope JSON, fixed key order: `{"profile":..., "masterSeed":..., "settings":{items, enemies{... placement{...}}, fog, output{includeDlc}}}`.
   All keys of the settings are present in a fixed order (`canonicalSettings.ts`); the custom enemy pool is a list of stable model
   ids sorted lexicographically (`null` = full pool). Same configuration, same bytes, whatever order the UI or a preset built it in.
   **Extension keys.** An option added after format 1 existed (the first ones are `enemies.placement.bossReplacement` and
   `uniqueBosses`, Part D) is complete in memory, in history and in the settings file, but a Seed Code stores it **only when it
   differs from its neutral value**, appended after `customPool` in a fixed order (`CANONICAL_PLACEMENT_EXTENSION_KEYS`). The neutral
   value is "what the world did before the option existed" and is frozen forever. So every code made before the option keeps its exact
   text, decoding fills the neutral value back in, and re-encoding gives the identical code. A code that uses a non-neutral extension
   value is refused by an application that does not know the option ("settings cannot be used"), never silently reinterpreted.
2. `deflate-raw` (level 9) with a fixed preset dictionary (`SEED_DICTIONARY`, part of the format; it only makes codes short).
3. Bytes = `[0x01 format][deflated envelope][first 4 bytes of SHA-256 of the preceding bytes]`.
4. Crockford base32 (`0123456789ABCDEFGHJKMNPQRSTVWXYZ`) in groups of 8, prefix `DS1TR1-`.

The decoder is tolerant about transport only: upper/lower case, spaces and line breaks (also in place of dashes), missing
dashes, and the Crockford look-alikes (`O`=`0`, `I`/`L`=`1`). Inflated size is capped at 64 KB. A decoded code is re-encoded
canonically, so equal worlds always display the same code.

Errors (plain wording for the person, exact reason in `technical`):

| Code | Meaning |
|---|---|
| `MALFORMED` | not a Seed Code / unreadable payload |
| `CHECKSUM` | invalid or damaged (a character was changed or lost) |
| `UNSUPPORTED_FORMAT` | made by a newer container format: "update the app" |
| `UNSUPPORTED_PROFILE` | the profile is unknown to this application: "needs a newer version" |
| `INVALID_SETTINGS` | the settings cannot be used (for example a removed option value) |

A change to any of the format-1 definitions above (dictionary, alphabet, key order, checksum, layout) would break every existing
code. Such a change is a new **format** (`DS1TR2-...`), with format 1 still decoded forever.

## Randomize, Load seed and the master seed

* **Randomize** — the backend makes a fresh internal master seed *when the run starts* (never at startup, never on import) and
  freezes it with the current settings into a Seed Code; the code is returned with the result and shown with a Copy button.
* **Load seed** — decodes the code, puts its settings into the controls (the loaded code is authoritative) and generates exactly
  that world. The machine's game path is never touched.
* Changing any generation setting after loading ends the loaded seed's identity: the next Randomize makes a new world and a new
  code. The spoiler preference does not count as a change.
* The master seed is shown only in *Advanced*. Configurations that carry an explicit master seed (older presets and scripts) keep
  working; a preset stores settings, a Seed Code stores the exact world.

## Run history

`<state>/history/runs.json` (schema 1, newest first, at most 200 entries, atomic writes, a damaged file is set aside).
Each entry: timestamp, Seed Code, profile, master seed, settings snapshot, status `generated | applied | restored`, package /
manifest / spoiler paths and a `packageExists` flag computed when the list is read. The list is collapsed by default. Quick actions:
Apply / Play (Launch game for the applied world), Copy seed, and a menu (View settings, View spoiler log, Delete from history).
**Regenerate files** uses only the stored Seed Code, so it works after the run folder was cleaned up; it is offered as the main
action only when the files are gone. It is not a new run: **generating a Seed Code that is already in the history (Regenerate files,
or Load seed of a known code) refreshes that entry** (same id, Seed Code and creation time; new files) instead of adding another.
The spoiler log of a world randomizer run is the one of its item/enemy stage (recorded by path, never copied into the package).

## Saved settings

`<state>/settings/ui-settings.json` (schema 1): the canonical settings and the spoiler preference, saved by the backend when the
person changes them and loaded on start. The game path is machine state and is managed separately. A missing file is normal; a
damaged, invalid or newer-than-this-app file is replaced by the defaults with the plain note "Your saved settings could not be
read (...) and were reset to the defaults.". A later schema adds its migration in `settingsStore.ts` before validation.
