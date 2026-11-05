# V7: the Fog world model comes from the original FogMod, not from our reconstruction

Status: **implemented** as generation profile `ds1tr-gen-v7` (desktop 0.42.0, 2026-11-05). V7 is registered and is the profile for new worlds; V1-V6 are frozen and unchanged (goldens 68/68). The first host-side step (commit `c03887b`) added the `upstream-v7` annotation source; the owner then approved the distribution and item-model decisions (below) and the profile was registered, tested and sampled.

## Why

Gameplay confirmed that the Compact catalog contains player-traversal edges that FogMod does not have. The latest one: Seed `DS1TR1-06NWCQQ3-K2GXEE76-2RKED9N6-GR36D8RN-SSM8ACWM-58EE9B2F-B5PG6BHQ-8SNSMRAH-TC00004E-NW40`, Asylum fog to the Priscilla arena (boss side), then the modelled `paintedworld_priscilla -> anorlondo_main` fixed route: the jump kills the player.

## Phase 1: where the V6 (Compact v3) transitions come from

Measured by comparing the Compact v3 annotations (`FogCompactCatalogBuilder.BuildV3(...).CreateFreshCandidateAnnotations()`) with the pinned upstream `dist/fog.txt` (FogMod `6e6dc78`, SHA-256 `77a10b40...`). Both are `AnnotationData.Annotations`, so the comparison is row by row.

| | Upstream | V6 | A identical | B adapted | C/D project-made | E unknown |
|---|---|---|---|---|---|---|
| Areas | 131 | 131 | 131 (names and tags equal) | 0 | 0 | 0 |
| `Area.To` links | 85 | 183 | 5 | 24 | 154 (37 `ConnectCollision` only, 117 other) | 0 |
| Entrances | 109 | 99 | 45 | 33 (20 differ only in side flags/ids, 13 in Area/tags/conditions) | 21 (20 DLC township PvP doors, `o1309`) | 0 |
| Warps | 15 | 7 | 0 | 7 (3 flags only, 4 sides moved or flags removed) | 0 | 0 |
| **Transitions** | 209 | **289** | **50** | **64** | **175** | **0** |

So **17 percent of V6 transitions are upstream-identical, 39 percent come from upstream, 61 percent are our own reconstruction.** V6 also leaves out 55 of 85 upstream links, 31 of 109 entrances and 8 of 15 warps.

The 175 project-made transitions:

- 37 exist only because of a `ConnectCollision` anchor (`MapRoute` in `FogAnnotationPolicy`). 17 of them have an upstream Entrance or Warp joining the same two Areas, 20 have nothing upstream. Examples: `paintedworld_priscilla > anorlondo_main`, `firelink > catacombs_start`, `firelink > newlondo`, `blighttown_upper > parish_andre`, `dukes_* > anorlondo_start`, `sens_irongolem > anorlondo_start`.
- 117 are in-map adjacency inferred from the shared map: 41 are backed by an upstream Entrance/Warp between the same Areas, 20 are the reverse of an upstream link, 56 have no upstream counterpart (all in one map family). They include `blighttown_bell > blighttown_quelaag [blighttown_quelaag]`, the shape of the Quelaag back-entry defect.
- 21 Entrances are not in upstream.

Of the 24 adapted links, 8 dropped an upstream condition (`burg_upper > burg_upperresidence` and `burg_lower > burg_lowerresidence` lost `residence_key`, `dlc_hallway > dlc_gough` lost `crest_key AND dlc_artorias`, `dlc_township > dlc_townshiphidden` lost `cast_light`, `anorlondo_painting > anorlondo_chandelier` lost `anorlondo_rafters`, `newlondo_drakes > newlondo_drakesbeatrice` lost `newlondo_fourkings`, `dukes_library > dukes_crystalgolem` lost `darkroot`, `asylum_prebalcony > asylum_f2west` lost `undead_asylum_f2_west_key`) and most of the others lost the `drop`/`shortcut`/`door` tags upstream uses for routing.

### Why V6 diverged

Compact Fog was introduced to remove the dependency on FogMod's `dist` (59 files, FromSoftware-derived, supplied or downloaded by the user): it rebuilds the 58 inputs from the player's own DSR files and ships a project-authored catalog (`docs/COMPACT_FULL_FOG_V1.md`, DECISIONS "Compact Fog"). Without `fog.txt` there was nothing to say which map-to-map moves are real, so the catalog **inferred** them from `ConnectCollision`, map membership and adjacency. FogMod's own graph is deliberately sparse (85 links): the rest of the connectivity is in Entrances, Warps and `trivial` Areas that its checker does not require. The catalog made every Area required and filled the gaps with inferred links, and each inference is a place where the model can differ from the game.

## Phase 2: what upstream already encodes

All in `dist/fog.txt` (`AnnotationData.Annotations`) and in the vendored engine:

| Concern | Upstream structure |
|---|---|
| Areas, which are required | `Areas[].Name`, `Tags` (`trivial`, `optional`, `boss`) |
| In-map links, directionality | `Areas[].To[]` with `Cond`, `Tags` (`drop` one-way, `shortcut` bidirectional via `Graph.Construct`, `door`, `hard` glitched, `temp` ignored) |
| Fog gates, both sides | `Entrances[]` with `ASide`/`BSide` (`Area`, `Cond`, `Flag`, `TrapFlag`, `EntryFlag`, `BossTrigger`, `ExcludeIfRandomized`), `Tags` (`boss`, `pvp`, `major`, `world`, `door`, `lordvessel`, `unused`) |
| Doors and gates | `DoorCond`, `door` tag (one per unordered Area pair, `Graph.Construct`) |
| Scripted transports | `Warps[]` with `ASide.WarpFlag`/`Cutscene`/`Cond`, `BSide.BeforeWarpFlag`/`DestinationMap`, tags `unique` (connected by the connector's UNPAIRED silo), `norandom`, `unused` |
| Progression conditions | `KeyItems[]` and `LotLocations` (`ItemReader` reads the staged lots), `Cond` expressions parsed by `ParseExpr`, `Option lords/lordvessel` handling in `Graph.Construct` |
| Solver | `GraphConnector.Connect` (bounded tries by seed offset) and `GraphChecker.Check` |

Nothing is rewritten: the vendored `Graph`, `GraphConnector`, `GraphChecker`, `ItemReader` and `GameDataWriter` run on the unmodified annotations (the project's changes to them are inert for non-Compact sources, `vendor/UPSTREAM_PIN.md`).

## Phase 3: architecture

```
dist/fog.txt (pinned, hash-verified)  ->  FogMod Graph / Connector / Checker   (physical semantics, unchanged)
Items randomizer's ItemLotParam       ->  ItemReader                            (where each key item is)
FogHost `upstream-v7` adapter         ->  start-reachability check, solver witness with provenance, events merge
```

Implemented in FogHost (HostVersion stays `0.7.3`: `upstream-v7` is its own annotation source, so no earlier profile's allow-list changes):

- `HostRequest.UpstreamV7Annotations = "upstream-v7"`: accepted request source; the fingerprint is the SHA-256 of `fog.txt`, and the host refuses any other file.
- `HostRunner`: loads `dist/fog.txt` unchanged, requires the full verified data root, **skips every Compact event reconstruction** (the upstream writer works on upstream data, as `0.5.0` did: its hook was only `merge.Merge`), runs the start-reachability search with the witness observer.
- `SolverWitness`: every transition carries `source` (`upstream-fogmod`); a key item the Items randomizer moved out of its upstream Area is `project-combined-overlay`; `project-verified-route` exists for future use and is never produced. The route text prints `SOURCE:` / `LOCATION SOURCE:` lines.

The combined Items randomizer is the one legitimate place where the project adds logic: the physical route and its condition names stay upstream; only the Area of the item changes (`ItemReader` already reads the staged lots), and the witness says so.

## Priscilla

- V6: Painted World exit modelled as the fixed route `paintedworld_priscilla > anorlondo_main` from `ConnectCollision h0041B0_0000`, unconditional. The upstream warp 1512500 was kept but its A side was moved to `paintedworld` and its `WarpFlag 5302` removed.
- V7: no in-map link leaves the arena. The only way out is the upstream unique warp `1512500` (A side `paintedworld_priscilla`, `WarpFlag 5302`, B side `anorlondo_painting`), which the connector joins like any unpaired exit (`VerifyPriscillaExitIsTheUpstreamWarp`).

## Results

FogHost tests: 192 passed, 0 failed (182 before, 10 new in `UpstreamV7Test.cs`):

- the graph FogMod builds from `fog.txt` equals the topology derived independently from the file's rows (with and without glitched logic): every link present, no invented in-map edge, every Entrance on its sides' Areas, every Warp exiting on its A side and entering on its B side, unique warps unpaired;
- historical regressions on the V7 graph: Asylum (no unconditional hallway exit, no `ConnectCollision` link), Quelaag (no in-map link into or out of the arena, `o4950_0000` carries the boss trigger), Gaping (`o1420_0000` is a Depths wall, the arena is entered by `o1400_0000` only, plus the hard drop when glitched logic is on), Priscilla (above);
- witness: deterministic bytes on every connection for the historical seeds and a sample, every transition `upstream-fogmod`, none project-verified, no fixed route out of the Priscilla arena;
- transition inventory under the default Triple options: **182 transitions** (69 in-map links, 102 entrances, 11 warps): **149 direct upstream, 33 upstream plus combined-mode condition overlay** (conditions naming a key item), **0 project-specific verified, 0 unsupported**.

Raw-seed acceptance with FogMod's own connector, upstream annotations, Triple Fog options (`world boss warp lords scale`), vanilla item lots, seeds 1-300: **299 accepted, 1 exhausted (seed 180, "Couldn't solve seed"), 0 rejected by the start-reachability search.** The connector's internal tries per accepted seed ranged from 1 to 28 (median 8).

## Distribution and upstream data policy (owner decision 2026-11-05)

V7 works **offline in both the GitHub and the Nexus distribution**: install the application, have a valid Dark Souls Remastered installation, and V7 works. No legacy FogMod cache, no earlier V1/V2 run, no runtime download, and the Nexus build stays offline, updater-free and network-disabled. (Proof: a first V7 generation on an empty state folder built the data set from the installed game alone; `upstreamV7.test.mjs` prepares a data set with a fetcher that fails if it is ever called.)

Classification of the 59 files FogMod reads (`docs/FOG_DATA_PROVENANCE.md`):

| Files | Provenance | V7 handling |
|---|---|---|
| `dist/fog.txt` | A: directly authored upstream metadata, Apache-2.0 verified | **bundled**, unmodified (`tools/fog-host/upstream/`, hash-pinned) |
| 4 layouts, 18+1 event files, 17 talk scripts | B: derived from game data / scope unclear | **rebuilt locally** from the installed game by the existing offline reconstructors |
| 18 maps + `GameParam` | C: game data | **copied** from the verified pristine snapshot |

Bundled redistributable files: `tools/fog-host/upstream/fog.txt` (+ `PROVENANCE.md`, `.gitattributes`). Locally rebuilt/read from DSR: the other 58 inputs (19 copied from the game, 39 rebuilt). Unclear or unresolved: none that is bundled; the equivalence of 73 rebuilt talk scripts to upstream is **not proven** (see "Open items").

The local rebuild is the file-level reconstruction that the Compact profiles already use, and it is not Compact's physical model: measured against the upstream originals it reproduces all 1281 events instruction for instruction (id, bank/id/arguments, parameters; only the parameter record order and the byte serialization differ), and V7 does not run any Compact event reconstruction, catalog, 64-entrance or 7-warp assumption, `ConnectCollision` or `Area.To` inference.

## Item model

V7 uses the full upstream item model: `KeyItems` (27, of which 25 constrain placement) and `LotLocations` (440 lots), read from `fog.txt` at run time by `FogHost upstream-projection` and handed to the combined engine as its tracked items (`trackedItemsFromUpstreamProjection`). The Compact "required 9" projection is not used. The frozen legacy snapshot (profiles v1/v2) is checked to be equal to this projection (`upstreamV7.test.mjs`), so `fog-item-tracking-v1` could only ever have been a transport of the same data; V7 does not use it. The Items randomizer places progression items; FogMod's own `ItemReader` then finds where each key item ended up in the staged lots, and the solver uses that Area for the unchanged upstream requirement. The witness marks such an item `project-combined-overlay`. The physical route, the key identities, upstream directionality and upstream warp semantics are never touched.

## Registration and identity

- Profile `ds1tr-gen-v7`: annotation source `upstream-v7`, item tracking `upstream-fog-item-tracking-v7`, host `0.7.3` (schema 3), seed derivation, retry bounds, combined engine and roaming-boss policies as V6.
- World data identity `82f7a04c33a2e42c` = SHA-256 of (profile, upstream data version, local reconstruction version `ds1tr-local-fog-data-v1`, pinned commit, every file hash). The profile also pins the `fog.txt` hash `77a10b40...`; a different `fog.txt`, data set, host or engine refuses the run (`checkProfileComponents`).
- Seed Codes name the profile, so a V7 Seed Code replays V7 and a V6 code replays V6.

## Results

- **Raw Fog** (FogMod's own connector, upstream annotations, Triple Fog options, vanilla item lots, seeds 1-300): 299 accepted, 1 exhausted (seed 180, the connector's "Couldn't solve seed"), 0 rejected by the start-reachability search. Unchanged; one connector exhaustion is not treated as a defect and retry semantics are not changed to reach 300.
- **Triple, 100 master seeds** (`v7-triple-1` to `v7-triple-100`, the real combined pipeline: Items ON, Enemies/Bosses ON, Fog ON, V7 defaults, glitched logic OFF): **100 accepted, 100 on the first combined and Fog attempt, 0 pipeline retries, 0 exhausted, 0 failures.** (FogMod's connector makes its own internal tries inside the first Fog attempt, from 1 to 28 on the raw seeds; they are not pipeline retries.)
- **Goldens:** all 68 cases pass (65 existing, unchanged, none re-recorded; 3 new V7: Triple defaults, Fog alone, shuffled progression items with Lord Souls).
- **Gate** (`gate-v7`): typecheck clean; backend 646 tests (645 passed, 0 failed, 1 skipped); desktop 150 (148 passed, 0 failed, 2 skipped, apart from the release-verifier test that checks the previously built package and passes on a fresh build); FogHost 194/194; golden verify PASS.
- **Priscilla** (V7 equivalent of the owner's hard-lock): raw Fog seeds 140, 243, 265 have the Asylum hallway paired with the boss side of Priscilla's fog. In all three the only way the solver leaves the arena is the upstream unique warp `1512500` (`WarpFlag 5302` preserved, source `upstream-fogmod`), and no in-map link leaves the arena. No gameplay proof is claimed: the owner has to play it.

## Open items

1. **Talk scripts.** 73 of 178 rebuilt scripts (the author's bonfire and conversation edits) differ from upstream in the state-machine dump (literal encoding); equivalence is not proven. They are the same rebuilt files the published V6 ships and hold no Fog graph data.
2. **Item lots in the witness.** For V7 the route says "no lot (default area)" for an item because the witness's lot lookup reads the Compact lot table; the Area is correct, the lot id is not shown.
3. **Parity with the `0.5.0` executable** on the same request (file-level equality of what is written) is not run.
4. **Gameplay.** Nothing was played in the game.
