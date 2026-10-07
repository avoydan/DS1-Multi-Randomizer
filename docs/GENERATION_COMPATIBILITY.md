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
| New *optional* setting whose neutral value reproduces the old behavior | no; it becomes an **extension key**: stored in a Seed Code only when non-neutral (`docs/SEED_FORMAT.md`), old codes keep their text and decode to the neutral value. Proof required before it ships: the golden fixtures, the lock hashes and the sampling pins stay byte-identical with the engine change in place. Used twice: Part D (`bossReplacement`, `uniqueBosses`) and Part E1 (`bossChanceInNormalSlots`, `roamingBossSouls`, `roamingBossesStayDead`, plus the new `normalReplacement` values `bosses` / `both`, which an earlier code cannot contain). Worlds that use a non-neutral value are pinned by their own fixtures (`ds1tr-gen-v1.boss-sampling.json`, `ds1tr-gen-v1.roaming-bosses.json`). |
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

## Recorded exception: the roaming boss respawn bit (backend 0.35.1, 2026-10-06)

**What changed.** A roaming boss (Part E1) is a copy of a boss NpcParam row, and boss rows carry `disableRespawn = 1`. DsrDataTool only ever *set* the bit (stay dead on), so with
"Roaming bosses stay dead" **off** the rows kept the inherited 1: the option did nothing in the data. The tool now writes the bit for every roaming row from the option
(on = 1, off = 0) and asserts it (`patches/0015-roaming-respawn-bit.patch`).

**What changes, proven.** The same worlds generated with the tool before and after the fix (`ds1tr-gen-v1`, same Seed Codes):

| World | Package |
|---|---|
| roaming bosses, stay dead **off** (`quick-roaming-basic`, `quick-roaming-repeated`, golden `roaming-both-combined`, `roaming-both-boss-sampling-fog`, Part E extended worlds) | **different**: `GameParam.parambnd.dcx` only; inside it only the `disableRespawn` cell of the roaming rows, 1 -> 0 (53 rows in `quick-roaming-basic`, 96 in `quick-roaming-repeated`); every other file and cell is identical |
| roaming bosses, stay dead **on** (`quick-roaming-souls`, golden `roaming-bosses-souls-stay-dead`) | identical |
| no roaming bosses: classic, sampling, boss sampling (Part D), `quick-sampling-asylum`, `quick-boss-asylum-demon`, all 15 earlier golden fixtures | identical |

So **a Seed Code of a roaming world with stay dead off keeps its text and its settings and gets a package that differs in that one cell**; nothing else a Seed Code can express is affected.

**Decision: no new generation profile; a narrow, recorded exception to the rule above.** The rule exists so that a shared Seed Code keeps its world. Here the option never worked as
documented (the world a person got did not match the setting they chose), Part E1 and every roaming world are younger than the day of this fix and have never been public (the
only worlds played are the test worlds), and the bit is the only difference. Because the world before the fix was wrong against its own setting, keeping it byte-stable under a new
profile would preserve a defect. Consequences: the two affected golden cases were re-recorded (the file `ds1tr-gen-v1.roaming-bosses.json` is the only fixture changed; its stay-dead case is
byte-identical to the old record, the 15 older fixtures and the Part D fixtures were not touched), and the played test records say which packages changed after they were played.
**This is not a precedent**: it applies to options introduced after the last public release only; once the app is released, any such change needs a new profile.

## Recorded exception 2: the Darkroot Basin event owner (backend 0.37.1, 2026-10-07)

**What changed.** The Darkroot Basin (`m12_00_00_01`) has no event file; its generated events were dropped. DsrDataTool now resolves the event owner (`m12_00_00_00`) for legacy-style sampling worlds (`docs/EVENT_ALLOCATION.md`, "Event owners";
`patches/0018-event-owners.patch`). Hostile NPCs may now stand in the Basin (their wake-up exists).

**What changes, proven** (the 27 golden fixtures regenerated before and after, same Seed Codes):

| Worlds | Package |
|---|---|
| the classic permutation (`enemies-classic-permutation`, `items-enemies-bosses`, `items-enemies-bosses-fog`, `broad-items-shops-fog`, `second-gargoyle-keep`), items-only, world options, and the three Part E2 fixtures with no Basin placement | **identical** |
| 12 legacy-style sampling worlds (`enemies-sampling-v1`, `custom-enemy-pool`, `custom-pool-model-grace`, `sampling-bosses-fog`, `sampling-bosses-combined-only`, `boss-sampling-fog`, `boss-sampling-unique-combined`, `type-anywhere-relocated-window`, `roaming-both-combined`, `roaming-bosses-souls-stay-dead`, `roaming-both-boss-sampling-fog`, `encounter-body-roaming-both`) | **different in exactly one file**: the owner event file (`event/m12_00_00_00.emevd.dcx`): the events that were missing. Nothing else |
| 5 hostile NPC worlds (recorded the day before; Part F1 is younger than this fix) | different: the Basin may now take a hostile NPC (`m12_00_00_01.msb`, `GameParam`, the AI bundle, the plan) besides the events |

The quick worlds that were played in the game (`quick-roaming-*`, `quick-sampling-asylum`, `quick-boss-asylum-demon`) are Asylum worlds; their packages now differ in the Garden's event file only (extra events for Basin parts that the test never visits); the Asylum content is unchanged.

**Decision.** Same reasoning as exception 1: the missing events were a defect against the world's own plan, sampling-v1, Part E and Part F are younger than any public release (there is none), and the change is the missing events and nothing else. The 12 + 5 affected fixtures were re-recorded
(the five classic-family fixtures and the others are untouched, checked per case against the previous record). **Classic stays frozen: its Basin placements keep having no events** (a documented quirk, not fixed because it would change old Seed Codes). **This is not a precedent**: after the first public release any such change needs a new profile.

## Golden fixtures

`apps/backend/fixtures/golden/<profile>.json` holds, per scenario: the Seed Code, the decoded canonical settings, the profile, the
master seed, the SHA-256 of every package file (and one digest over all of them) and the attempt results. Recorded for
`ds1tr-gen-v1` from a clean Dark Souls Remastered copy:

`items-only`, `enemies-classic-permutation`, `enemies-sampling-v1`, `items-enemies-bosses`, `items-enemies-bosses-fog`,
`broad-items-shops-fog`, `custom-enemy-pool`, `second-gargoyle-keep`, `custom-pool-model-grace` (the one-model grace of the
custom pool, `docs/ENEMY_PLACEMENT_MODEL.md` 10.5) and `world-options` (Polish messages, PvP fogs, Lordvessel gates,
disconnected fogs, glitched logic). Further sets were recorded the day their option appeared and never touched again: `event-window`, `boss-baseline`, `boss-sampling` (Part D),
`roaming-bosses` (Part E1), `hostile-npcs` (Part F1: basic, with Fog, with roaming bosses, with Type replacement, with boss bodies) and `encounter-bodies` (Part E2: basic, with Fog, with roaming bosses and `both`, a Custom Pool edge). **Part F1 changed no earlier world**: all 22 earlier fixtures verify byte for byte with the F1 engine, data tool and presets; the new option is an extension key whose neutral value (`hostileNpcs` false) adds no type, no draw and no stream. **Part E2 changed no earlier world**: all 18 earlier fixtures verify byte for byte with the E2 engine and data tool; the new option is an extension key whose neutral value (`bossChanceInBossSlots` 90, `bossReplacement` `classic`) is the old behavior.

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

## Part G: the default changed, no generation did (desktop 0.26.0)

New configurations start with `sampling-v1`. **This is a default, not a generation change:** a Seed Code carries its algorithm, a record without the key still decodes to the classic permutation, and no package of an existing Seed Code differs because of it. Verified: all 27 golden fixtures regenerate from their Seed Codes (classic and unaffected cases byte-identical; the recorded exception 2 above is the only difference, and it predates this part), `placementDefault.test.mjs`, `seedUx.test.mjs`. The classic quirks that stay frozen (a Basin placement of the classic permutation has no events; Vanilla scaling keeps the replacement's drops) are listed in `docs/ENEMY_PLACEMENT_MODEL.md` 10.10.
