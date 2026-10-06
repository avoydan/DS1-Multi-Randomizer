# Generation compatibility

> **Core project rule.** A Seed Code created today must never silently produce a different world in a future version that
> supports its generation profile. **If a change alters the output of an existing seed, it needs a new generation profile.
> A published profile and its golden fixtures are never edited.**

## Application version vs generation profile

The application version (`0.30.0`, ...) describes the software. A **generation profile** (`ds1tr-gen-v1`, ...) describes the
deterministic behavior that turns *(master seed, canonical settings)* into a world. They are independent:

* the profile id never contains an application version;
* the application version is recorded in the run manifest and **never** read by generation;
* every Seed Code carries its profile id; old profiles stay runnable in newer applications; an application refuses a code only
  when it does not know the profile (explicit "needs a newer version" message), never because of an application version.

## What a profile freezes (`apps/backend/src/core/generationProfiles.ts`)

| Component | `ds1tr-gen-v1` |
|---|---|
| Seed derivation | `seed-derivation-v1`: named streams from `SHA256("ds1-triple-randomizer:v1" ...)`, retry attempts included |
| Retry bounds | combined engine 3 attempts, Fog 5 attempts (every attempt draws different seeds, so the bounds are semantics) |
| Combined engine (items, enemies, bosses) | `RANDOMIZER_VERSION` `0.5.66` |
| World randomizer | FogHost `0.5.0`, FogMod commit `6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f`, world data id `f394fa536ba9b66d` |
| Item tracking for the world randomizer | `fog-item-tracking-v1` (`docs/FOG_ITEM_TRACKING.md`) |
| Settings completion | `completeSettings`: options added after the profile get the value that reproduces the profile's behavior |

`ds1tr-gen-v1` is the behavior of desktop 0.16.1 / backend 0.29.1 / contracts 0.12.0.

### Drift guard

A run made from a profile verifies at run time that the engines it actually used are on the profile's lists (`checkProfileComponents`):
the combined engine version after the combined result, FogHost version and FogMod commit after the Fog answer, the world data id
after the prerequisites. A mismatch stops the run with `GENERATION_PROFILE_MISMATCH` (plain message, exact reason in the log and
manifest) instead of producing a different world under the same code.

## Which changes need a new profile?

| Change | New profile? |
|---|---|
| UI, wording, layout, packaging, installer, logs | no |
| Error text, new error classification of a run that already failed | no |
| Speed-up with **proven** byte-identical output (golden fixtures pass) | no; add the version to the profile's allow-list only after the fixtures pass |
| New *optional* setting whose "off" value reproduces the old behavior | no; old codes are completed with that value (`completeSettings`) |
| Any change of placement, candidate order, RNG consumption or order, seed derivation, retry semantics | **yes** |
| Fog transformation, FogMod/FogHost behavior change, world data change that affects output | **yes** |
| Item placement rules, key item handling, shuffle role assignment | **yes** |
| New default for an existing option | no (codes carry every option explicitly; defaults only affect new runs) |

Making a new profile (`ds1tr-gen-v2`):

1. Add the entry to the registry (copy v1, change what changed); keep v1 untouched, including its engine allow-lists.
2. The code that produces the new behavior must be selectable by profile (profile-keyed branch or a parallel engine path); v1 must
   still select the old behavior. Where the old engine files are replaced, the old engine must remain available.
3. Set `CURRENT_GENERATION_PROFILE` to the new id; new runs and new Seed Codes use it.
4. Record `fixtures/golden/ds1tr-gen-v2.json` with the new tooling; **never touch** `ds1tr-gen-v1.json`.
5. Keep verifying v1 in the golden test; both profiles must pass in the same build.

## Golden fixtures

`apps/backend/fixtures/golden/<profile>.json` holds, per scenario: the Seed Code, the decoded canonical settings, the profile, the
master seed, the SHA-256 of every package file (and one digest over all of them) and the attempt results. Recorded for
`ds1tr-gen-v1` from a clean Dark Souls Remastered copy:

`items-only`, `enemies-classic-permutation`, `enemies-sampling-v1`, `items-enemies-bosses`, `items-enemies-bosses-fog`,
`broad-items-shops-fog`, `custom-enemy-pool`, `second-gargoyle-keep`, `custom-pool-model-grace` (the one-model grace of the
custom pool, `docs/ENEMY_PLACEMENT_MODEL.md` 10.5) and `world-options` (Polish messages, PvP fogs, Lordvessel gates,
disconnected fogs, glitched logic).

Package digest: SHA-256 over the sorted `path:sha256` list of the package files except `config.json`, `patch-manifest.json` and
`run-manifest.json` (they carry run folder paths and the application version); `randomizer.json` is replaced by the hash of its
placements (it embeds the run folder path). Verified stable across workspaces.

* `apps/backend/src/core/goldenCompatibility.test.mjs` always checks that every Seed Code decodes to its recorded profile, master
  seed and settings and re-encodes to the identical code.
* `node apps/backend/scripts/goldenCompatibility.mjs verify [--app-version X]` regenerates every world **from the Seed Code alone**
  and compares the package hashes. It needs a clean game copy (`DS1TR_GOLDEN_GAME_SOURCE`) and the FogHost/DsrDataTool
  environment of the e2e proofs. `--app-version` simulates another application version and must give the same result.
* The script refuses to overwrite an existing fixture file: fixtures are recorded once. **Never regenerate a fixture to make a
  test pass.** A disagreement means the change belongs in a new profile.

## Proof status (ds1tr-gen-v1)

* **v1 is the pre-change behavior, not just self-consistent.** The fixtures were recorded by backend 0.30.0, so they were
  cross-checked against the code that existed before profiles: a worktree at `afec8bf` (backend 0.29.1, desktop 0.16.1) built on its
  own, its plain `generateRun({config, workspacePath})` fed with each fixture's decoded settings and master seed, same clean game
  copy and helpers: **10 of 10 package digests are identical** to the recorded ones.
* **Verified from the Seed Codes alone**, also under the simulated application version `99.1.0-future`: identical profile,
  settings, master seed and package hashes, 10/10 (`goldenCompatibility.mjs verify --app-version 99.1.0-future`).
* **The spoiler preference does not change a world** (it is excluded from Seed Codes): the engine's `placementHash` and plan are
  identical with and without it for 30 seed/option combinations (it is only read when the output files are written), and
  `scripts/spoilerIndependence.mjs` shows the package digest of golden cases generated with the spoiler log on equals the
  recorded one once `spoiler.txt` is left out. (The world randomizer packages do not contain `spoiler.txt` at all.)
* **Packaged app:** a fresh profile of the packaged 0.17.0 build, Randomize, restart, Load seed, Apply, Restore through its own UI:
  the Seed Code made by Randomize and the one loaded after the restart produce byte-identical packages.
