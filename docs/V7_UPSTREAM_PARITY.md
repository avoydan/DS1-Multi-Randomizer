# V7: the Fog world model comes from the original FogMod, not from our reconstruction

Status: **PARTIAL.** The host side of generation profile `ds1tr-gen-v7` is implemented and tested (annotation source `upstream-v7`). The profile is **not registered** in the backend yet, so no Seed Code can name it and V1-V6 are untouched. What is missing, and the decisions the owner has to take first, are in "What is not done".

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

## What is not done

1. **Profile registration** (`ds1tr-gen-v7` in contracts and `generationProfiles.ts`), runtime contract (host `0.7.3`, schema 3, source `upstream-v7`), world data (the legacy `f394fa536ba9b66d` set), progression-model export for key placement, golden fixture for V7, `CURRENT_GENERATION_PROFILE` switch. Nothing of this can start before the decisions below.
2. **100 Triple master seeds**: needs the profile in the backend (items randomized by the combined engine, then Fog).
3. **Parity against the legacy `0.5.0` executable on the same request** (byte or semantic equality of the written files), which would prove that skipping the Compact reconstructions reproduces upstream output.
4. The UI: the route panel already shows the new `SOURCE` lines; no profile name is shown yet.

## Decisions needed from the owner

1. **Data distribution.** `upstream-v7` needs the verified `dist` (`fog.txt` plus 58 other files). The GitHub edition may download it as it does for V1/V2; the Nexus edition "does not download or bundle legacy Fog data" (DECISIONS), so V7 on Nexus would need a user-supplied cache, as V1/V2 do today. Alternatively `fog.txt` alone could be bundled (it is in the Apache-2.0 repository) and the rest reconstructed locally as Compact does, but the Compact reconstructions are written for the Compact catalog's 64 Entrances and 7 Warps, not for upstream's 109 and 15, so that would be new work.
2. **Item model.** V7 reads items with upstream `LotLocations` and the legacy tracked-item model (`fog-item-tracking-v1`), not the Compact required-9 projection.
3. **Version.** Desktop stays `0.42.0` as asked; a local QA build of `0.42.0` from `e4e0d07` already exists, so a build made after this change is a different artifact with the same version.
