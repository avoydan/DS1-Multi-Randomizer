# Compact Full Fog V1

**Status (2026-10-14):** production Offline Compact Fog is wired in the current worktree under `ds1tr-gen-v3`. The application version remains 0.39.0; no release, commit, or push was made.

## Runtime boundary

`FogDsrFacts` reads verified pristine DSR data. `FogProjectAnnotationPolicy` owns project Area semantics, progression classification, routes, starts, boss/collision rules, and cost tiers. Production uses the existing typed annotation catalog and backend projection types. `fog.txt` is not read, bundled, or downloaded by the Compact profile.

Compact v3 prepares 58 inputs from the verified game snapshot: 19 game-derived files and 39 locally reconstructed event, Talk/ESD, and layout files. The manifest has no upstream entries. The existing 59-file `fog.txt` path and frozen tracked-item snapshot remain available to v1/v2 Seed Codes.

## Authored content

- **Areas:** all 131 stable IDs have explicit ownership and topology dispositions. The policy has 187 static `Area.To` links, including 38 cross-map `ConnectCollision` anchors. `kiln_start` is a project marker for the accessible Firelink Lordvessel altar; the physical Kiln remains behind the Lordvessel and all four Lord Souls.
- **Entrances:** 64 project Entrances: 17 traversable fogs, 22 boss gates, 20 PvP gates (16 major / 4 minor), 3 Lordvessel gates, and 2 fixed/key doors. Each projected boundary resolves its authored pristine anchor.
- **Non-randomizable / unresolved transitions:** 94 map-load-only candidates and 4 tutorial/script/duplicate candidates are `NOT_RANDOMIZABLE` as standalone transitions. Two physical transition targets remain unresolved: New Londo event `11605381` / region `1602898`, and Demon Ruins event `11410090`.
- **Warps:** 6 project-directed Warps (3 reciprocal world connections), all backed by pristine cutscene+player relocations and destination regions verified in the destination MSB. The player-relocation inventory contains 27 `PlayCutsceneAndWarpPlayer` event families, 62 player-target `IssueShortWarpRequest` instruction anchors, and one direct `WarpPlayer` event: 90 relocation anchors total. Six are `PROJECT_WARP`; the remaining 84 are `NOT_RANDOMIZABLE` (21 cutscene event families, 62 short-warp anchors, one direct `WarpPlayer`). The 261 actor/NPC short-warps are excluded from this player-only count and remain `NOT_RANDOMIZABLE`. Event `11210849` has no event-parameter bindings; caller `11210700` passes an unused zero; the actual `WarpPlayer` destination is nonzero region `1218146` on `m12_01`, but it is an arena disband/system return and is not a world Warp.
- **Progression:** 22 verified item identities: 9 required, 13 random-only, 0 hard-only, with no unresolved mechanics in this set. The Key to the Seal is placed on accessible `newlondo_building`. The Firelink altar marker is separate from the physical Kiln; required Lord Soul branches and golden-gate conditions remain represented.
- **Lots:** 723 safe locations. The 224 unresolved/conflicting source rows remain excluded; required key items use safe logical-Area-owned locations.
- **Flags:** all 22 project-used boss-collision mappings are covered (22/22) by the verified common event-0 to event-970 completion-flag path.
- **Other catalog rows:** 16 custom starts, 69 enemy-scaling rules, and 131 default-cost entries.

## Graph and option families

The default and hard structural graphs each have one connected component. All 14 graph profiles (13 existing profiles plus Area Warps) reach required Areas, including every one of the 16 random-start locations. The DLC return anchor `h0037B1_0000` is assigned to the project Darkroot hub Area so a DLC start can rejoin the main progression graph.

| Profile / current option family | Result |
|---|---|
| Default | Working |
| Hard / glitched routes | Working |
| Random start (16 starts) | Working |
| Traversable fogs | Working |
| Boss fogs | Working |
| Major PvP gates | Working |
| Minor PvP gates | Working |
| Lordvessel gates | Working |
| Disconnected fogs | Working |
| Require Lord Souls | Working |
| No Fall Bed of Chaos | Working |
| Pacifist | Working |
| Enemy scaling | Working |
| Area Warps | Working in the candidate graph: six eligible directions in three reciprocal pairs; 6 connected edges and deterministic shuffle verified |

## Verification and remaining gate

- Catalog fingerprint: `8c03fd3de1cd7c772d5c933753ae54eab35f9e8b70a1aef2954322330d0002d4`.
- Compact candidate check passes: 131 Areas, 64 Entrances, 6 project Warps, 22 item identities, 723 safe lots, 69 enemy rules, 22 flags, and 131 costs. All 14 graph profiles and 16 random starts reach required Areas; the 1000-seed progression/Warp stress passes.
- Real `GameDataWriter` matrix: **18/18 runs passed**, 54 outputs decoded each. A repeated seed/options run produced identical output hashes.
- Cold-cache production path: generation prepared all 58 inputs locally with zero upstream files, then generated a full Items + Enemies + Fog package of **73 files** in one successful attempt. The golden `v3-compact-full-triple` regenerated from a clean QA copy with the same package digest.
- Focused Node verification: **36 passed, 0 failed, 1 skipped** (the general all-golden regeneration test is skipped without its opt-in environment variable); the separately invoked v3 golden regeneration passed. FogHost Release build, Compact candidate check, and real-generation matrix passed. NU1900 was emitted by the build because NuGet's vulnerability index was unreachable.
- Runtime/gameplay verification remains owner-driven; this work did not launch DSR or claim an in-game playthrough.

## Area Warp source facts (2026-10-14)

Every active record uses FogMod's existing `FogWarp` DTO. The destination map, region entity and cutscene ID are literal operands in a pristine `PlayCutsceneAndWarpPlayer` instruction (`2002[4]`); the destination region is present exactly once in that destination map's MSB. No ID is inferred from `fog.txt`.

| Project Area transition | Pristine event | Mechanism / destination |
|---|---:|---|
| `firelink_crow` -> `asylum_loop` | `11020021` | Cutscene `100230`; destination `m18_01_00_00`, region `1812110` |
| `asylum_loop` -> `firelink_crow` | `11810150` | Cutscene `180126`; destination `m10_02_00_00`, region `1022121` |
| `anorlondo_painting` -> `paintedworld` | `11510230` | Cutscene `150135`; destination `m11_00_00_00`, region `1102510` |
| `paintedworld` -> `anorlondo_painting` | `11100710` | Cutscene `110035`; destination `m15_01_00_00`, region `1512500` |
| `catacombs` -> `totg_coffin` | `11305000` | Cutscene `130020`; destination `m13_01_00_00`, region `1312110` |
| `totg_coffin` -> `catacombs` | `11310052` | Cutscene `130121`; destination `m13_00_00_00`, region `1302010` |

The other nine cross-map cutscene relocations are progression/tutorial routes (DLC access/return, Kiln and serpent routes, Asylum tutorial departure) or the Sen's/Anor pair whose literal destination anchor does not resolve on its declared target map. Twelve same-map cutscene relocations are arena/system positioning. None is independently randomized as an Area Warp. The Duke's prison event `11705100` sets a respawn point and saves but does not immediately relocate the player.


## Nexus packaging and offline run (2026-10-14)

The current Nexus package selects ds1tr-gen-v3 and has no updater feed metadata. Against a fresh temporary app-data/cache root and the clean QA DSR copy, its packaged backend reconstructed the 58 Compact inputs locally, made a full 73-file Items + Enemies + Fog package, applied it, and restored the original hashes for all 73 target paths. The process-scoped network guard logged no blocked request; the cache held 58 files and no fog.txt. This is packaged generation/apply/restore evidence, not gameplay evidence or an OS-wide firewall test. The same v3 generation/apply/restore/hash check passed via the installed Setup build.

Legacy v1/v2 remain a separate open release gate: their frozen Fog profile allows Host 0.5.0, while this Nexus package ships Host 0.6.0. Do not admit the newer host until untouched golden fixtures prove identical output.
