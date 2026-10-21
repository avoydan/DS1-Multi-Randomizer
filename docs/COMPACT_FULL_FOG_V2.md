# Compact Full Fog V2: boss progression correction

Status: **promoted for local owner gameplay QA on 2026-10-21**. New worlds use
`ds1tr-gen-v5`; v1-v4 compatibility is unchanged. This promotes the existing
V5 behavior without changing its semantics. Gameplay QA remains outstanding.
V2 uses the existing FogHost schema 3 with the project-owned Compact catalog
and adds no game-data files.

Catalog fingerprint: `701f5aa84085ac2361bee382c43a05c4813e11878fac8a462dd1341a859a1d7a`.
World-data identity: `221c01fa780daba7`.

## Boss-completion model

The Compact catalog adds a typed `BossCompletion` condition, represented as
`boss_defeated_<eventId>`. The route condition is separate from Area reachability,
item ownership, and `DefaultFlagCols`. Unknown boss-completion references fail
catalog validation.

The graph checker keeps completion state separately from visited Areas and
items. It can derive a boss encounter as defeatable only after traversing a
specific directed randomized edge into that encounter, where the generated
FogMod side has the expected boss event, entrance identity, trigger map/region,
and a custom spawn centered on the verified trigger volume. A broad visit to a
boss Area, an off-center spawn, or a non-boss side does not grant this state.
The checker treats an accessible encounter as defeatable for reachability; it
does not simulate combat. In the actual game, vanilla boss logic sets the real
completion flag only after the boss is killed.

The boss identity comes from the project-owned `BossAreaTagPolicies`, resolved
against pristine DSR `HandleBossDefeat` event and MSB enemy facts. The event-970
completion-flow check and the independently authored collision anchor are
validated separately. A collision mapping alone never creates a graph route.

## Ceaseless route and spawn

The owner-reproduced graph edge is
`demonruins_ceaseless -> demonruins_lava`, available only after
`boss_defeated_11410410`. The reverse edge is absent. The owner’s gameplay report
for the exact Seed Code says the player landed in the Ceaseless pocket and had
no other usable exit before defeating Ceaseless. Pristine facts independently
resolve event `11410410` to enemy `c2231_0000` on collision `h0018B1`, and verify
that the completion event enters the common event-970 completion flow. This is
the route-specific project policy; it is not inferred automatically from the
collision row.

The randomized Ceaseless arrival is also made safe: entrance `o4961_0000`,
Demon Ruins side B, targets the verified `m14_01_00_00` trigger region
`1412698`, which event `11415371` checks. V2 places that arrival at the trigger
center (rather than FogMod's ordinary offset, which the reproduced output put
outside the trigger). Catalog construction refuses the side if its event,
region, entrance, or center no longer matches pristine facts.

Event `11410090` is classified as **unrelated/system behavior, non-randomizable**
for this route audit. The pristine `m14_01_00_00.emevd.dcx` contains ten event
parameter bindings, two `3[18]` action-region checks, and two parameter-bound
`2004[41]` short-warp requests. Both short-warp instructions have target field
`0`, which is supplied through event parameters. A scan of every pristine DSR
`.emevd.dcx` archive found no `InitializeEvent` or `InitializeEventNoSkip`
caller for `11410090`; without a call site, the parameters do not resolve to a
runtime source, destination, direction, or prerequisite. Therefore these
instructions do not establish a player transition from the Ceaseless pocket
and are not a route the randomizer may assign. The separate actual encounter
start is event `11415371`, whose `3[18]` check references region `1412698`.

## Boss-separated static-route audit

The audit enumerated outgoing static Area links whose source is a project boss
Area. The exact owner-reproduced Ceaseless route is the only one currently
shown by gameplay evidence to depend on boss defeat. For the other rows below,
the project keeps the existing same-map Area subdivision and does not infer a
boss gate from the existence of a boss completion event or collision mapping.
No additional boss condition is added without route-specific physical evidence.

| Static route | Boss event | Pristine completion identity | V2 disposition |
|---|---:|---|---|
| `burg_taurus -> burg_posttaurus` | 11010902 | common event-970 completion flow verified | retain existing same-map adjacency; no boss-gated passage demonstrated |
| `parish_gargoyles -> parish_bell` | 11010001 | no matching event-970 completion fact in current fact index | retain; no route-specific gate evidence |
| `darkroot_butterfly -> darkroot_postbutterfly` | 11200900 | common event-970 completion flow verified | retain; no route-specific gate evidence |
| `dlc_guardian -> dlc_sanctuary` | 11210000 | common event-970 completion flow verified | retain; no route-specific gate evidence |
| `dlc_artorias -> dlc_hallway` | 11210001 | common event-970 completion flow verified | retain; no route-specific gate evidence |
| `dlc_kalameetdeath -> dlc_township` | 11210005 | no matching event-970 completion fact in current fact index | retain; no route-specific gate evidence |
| `dlc_manus -> dlc_top` | 11210002 | no matching event-970 completion fact in current fact index | retain; no route-specific gate evidence |
| `blighttown_quelaag -> blighttown_bell` | 11400001 | no matching event-970 completion fact in current fact index | retain; no route-specific gate evidence |
| `demonruins_ceaseless -> demonruins_lava` | 11410410 | event-970 flow and same-collision boss anchor verified | require `boss_defeated_11410410`; reverse edge absent |
| `demonruins_firesage -> demonruins_stairs` | 11410900 | common event-970 completion flow verified | retain; no route-specific gate evidence |
| `demonruins_centipede -> demonruins_izalithprelava` | 11410901 | common event-970 completion flow verified | retain; no route-specific gate evidence |
| `newlondo_fourkings -> newlondo_drakes` | 11600001 | no matching event-970 completion fact in current fact index | retain; no route-specific gate evidence |

This is an explicit candidate audit, not a claim that every static Area link
has been physically traversed in gameplay. Routes without independent evidence
remain outside the boss-gating policy; the static-route inventory still has
other unresolved physical relationships tracked by the offline reconstruction
analysis.

## Upstream comparison

For analysis only, `C:\Users\Adam\AppData\Local\Temp\fogmod-src` was verified
at exact commit `6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f`. Comparison evidence:
`dist/fog.txt`'s `demonruins` Area record links to
`demonruins_ceaselesslava` under the `demonruins_ceaseless` Area condition, and
its `o4961_0000` Entrance record names the Ceaseless side and its legacy flag;
the record does not classify event `11410090` or express boss-death state.
`FogMod/Graph.cs` and `GraphChecker.cs` show that these Area conditions are
evaluated as graph predicates, while `GameDataWriter.cs` lines 468-514 and
553-606 show how boss trigger regions and custom warps are authored. These
sources supported the conclusion that V5 needs project-owned boss state and a
trigger-centered arrival; they did not supply the route policy. `GraphConnector.cs`
was checked for bounded graph-solver behavior. No upstream annotation or
`dist` file was copied into the repository or used as a substitute for
project-owned policy.

## Verification

- Local SDK `C:\Users\Adam\dn8sdk\dotnet.exe`: .NET SDK 8.0.425.
- Fresh FogHost and test project Release build: passed with 0 warnings and 0
  errors using .NET SDK 8.0.425.
- Full FogHost suite with `DS1TR_FOG_DATA_ROOT`, read-only
  `DS1TR_PRISTINE_DSR_ROOT`, and `DS1TR_TEST_GAME_ROOT` set: **149 passed, 0
  failed, 0 skipped**. This includes the Ceaseless boss-completion and arrival
  checks, event 11410090 classification, and clean-DSR facts.
- With real data enabled, the V5 FogHost suite and a clean baseline suite were
  both run against the same pinned FogMod data root and read-only QA DSR. The
  baseline v4 suite passed **117/117**. The first V5 run had the same apparent
  ToTG anchor failure, but baseline comparison isolated the difference to two
  Japanese region-name literals that were corrupted during patch transport.
  Those literals were restored from the baseline source; the corrected V5
  suite then passed **117/117**, including the ToTG anchor assertion and the
  boss-progression/event-11410090 checks. This was a porting defect, not a QA
  data mismatch or a V5 product regression.
- The focused Compact candidate check passed. It verified the V2 route/spawn
  policy, all 14 graph profiles / 16 random starts, and the existing 1000-seed
  progression/Warp stress.
- Real-format Compact V2 matrix: **17/17 configurations passed**, each writing
  and decoding 54 Fog output files in fresh temporary staging; a deterministic
  repeat also passed.
- Recovered support reproduction package: the old staging matched all **55/55**
  fixture hashes. Current pipeline regeneration from its Seed Code produced 55
  package files with digest
  `4ff13748e6c52ac10b845c6dfb9ba2c253dff8387fb70e5c493627b4a5cdfc34`, Fog
  seed `4009337993`, and success on attempt 0. Two independent regeneration
  passes matched the replacement golden.
- Full Items + Enemies + Fog generation produced 73 files with digest
  `c70b79d3fe7333b83ba7250babf78237d8a1c13d0d8067f95f2a16367c2f6024` and Fog
  seed `2366392824`, success on attempt 0. It matched the replacement golden
  in two independent pre-record runs and two later Seed-Code verification
  passes.
- V1 fingerprint remained
  `8c03fd3de1cd7c772d5c933753ae54eab35f9e8b70a1aef2954322330d0002d4`.
- Root typecheck passed for contracts, backend, desktop renderer, and Electron
  main. Full backend suite: **615 total, 613 passed, 0 failed, 2 skipped**.
  V1-v4 fixture files remain unchanged; the backend profile/Seed-Code/golden
  metadata tests pass for their compatibility contracts.
- The real QA DSR source was read-only. All generated staging outputs were
  under `%TEMP%`; the live Steam installation was not accessed.

## Golden provenance and remaining scope

The old unpublished development fixture digest was
`7c22e36272eb9abdc45d50e87e45037582605d5d408fdac59a85b3d6c19d8838`. Its
package was recovered under `%TEMP%` and verified 55/55 against its recorded
hashes. Semantic decoding showed the four MSB differences were only 24 enemy
`NPCParamID` assignments (8 in `m13_01`, 7 in `m13_02`, 1 in `m15_00`, and 8
in `m17_00`); all object, region, collision, ConnectCollision, transform,
entity, and event-related data matched. `GameParam.parambnd.dcx` differences
were confined to `GameAreaParam`/`NpcParam` scaling and reward rows. The old
package used annotation fingerprint
`77ab12f68523a83d4a77f28d34d9b34b9658df2f96720bb75e5a43dc5c2abab9`; current
V5 uses `701f5aa84085ac2361bee382c43a05c4813e11878fac8a462dd1341a859a1d7a`.
It is classified **STALE DEVELOPMENT FIXTURE**, not unrecoverable. Its backup
remains in `%TEMP%`; the retired copy was replaced through the normal golden
recorder after semantic gates passed.

The recorder now encodes each fixture's declared profile into its Seed Code,
so V5 can be recorded and verified while ordinary new runs still select V4.
The new support-reproduction digest is
`4ff13748e6c52ac10b845c6dfb9ba2c253dff8387fb70e5c493627b4a5cdfc34` (55
files). The full-triple digest is
`c70b79d3fe7333b83ba7250babf78237d8a1c13d0d8067f95f2a16367c2f6024` (73
files). Both regenerate identically from their recorded Seed Codes.

The dirty reference worktree was not modified. Its older compiled constants
(`77ab...` / `c724000d431a5e98`) fail the profile drift guard against the
current isolated projection; current V5 conclusions use the isolated source,
verified clean DSR facts, and project-owned policy. No gameplay launch is
claimed. (At the time of that analysis, `CURRENT_GENERATION_PROFILE` was v4;
the later local QA promotion is recorded above.)
- The reachability checker assumes a reachable boss encounter can be defeated;
  it does not model combat outcomes. No gameplay launch was made for this
  corrected v5 package.
- `11410090` is closed as non-route evidence as described above. Other
  unresolved physical Area relationships remain open; no boss condition is
  inferred for them.
