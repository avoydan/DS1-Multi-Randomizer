# Compact Full Fog V2: boss progression correction

Status: **promoted for local owner gameplay QA on 2026-10-21; Ceaseless landing and post-boss physical-route validation corrected on 2026-10-23; PvP-off gates and landing parity audited on 2026-10-25; Gaping/Artorias actionability and o5865-to-o1413 landing evidence recorded; catalog fingerprint reconciled and 1,000-master end-to-end study completed on 2026-10-26.** Real-format output tests pass. Gameplay remains blocked because measured generation success is 24.8% and the physical-itinerary false-positive question is unresolved. V1-V4 behavior remains unchanged.

Current catalog fingerprint: `73f3c842d51490d45a836a489105b37f57d702eaa2ffda084f42781e5aaef00f`.
Current world-data identity: `ef625777d6aa5abf`.
Current FogHost compatibility version: 0.7.3 (implementation/file 0.7.4).

## Catalog fingerprint reconciliation and backend generation study (2026-10-26)

The generated Compact V2 projection was `73f3c842d51490d45a836a489105b37f57d702eaa2ffda084f42781e5aaef00f`, while the unpublished V5 profile still pinned `774ed7c1ecaaad006646eaaf0dd6bef86aafe033a3881076d9cd3bda7ffcd36c`. The old/current semantic fingerprints are `6d01e20879755c5d1368a99c747cdc4b8a5b794a7b7dd9e0b97881db5a5c5b0d` and `6c73ee8777629c43759403d0c156cd928107cfcc8d21439ca5f1eff09686e5f0`.

The semantic diff was reconstructed and classified in full:

- Added records: `bossCompletions[11000001]` (Gaping, trigger `11005391`, entrance `o1400_0000`); `bossCompletions[11210001]` (Artorias, trigger `11215011`, entrance `o2915_0000`); active entrances `o1400_0000` and `o2916_0000`.
- Removed record: `writerSupportEntrances[o2916_0000]`; that identity is now the active Artorias arena exit entrance.
- Changed fields: `bossCompletions[11410410].requireFixedPostBossRoute` is now `true`; `o2915_0000` BSide `bossEncounterStartEventId` changed `0 -> 11215011`, `bossTrigger` changed `11210001 -> 0`, `bossTriggerArea` changed from the authored trigger box to `null`, `flag` changed `0 -> 11210001`, and `tags` changed `Higher -> None`.
- Record order changes: zero. Classification: EXPECTED_FROM_BOSS_FOG_FIX for the four added records, the removed writer-support record, and the five changed o2915_0000 fields; EXPECTED_FROM_PREVIOUS_APPROVED_V5_FIX for bossCompletions[11410410].requireFixedPostBossRoute; UNEXPECTED: zero. Three derived fingerprint values changed as a consequence of the semantic records.

The added Gaping/Artorias records and the `o2915_0000` values are expected from the previously approved boss-fog actionability correction. Converting `o2916_0000` from writer support into an active exit is expected from the approved post-defeat Artorias exit correction. Requiring a fixed post-boss route for event `11410410` is expected from the earlier approved Ceaseless physical-return rule. No separate catalog edit was made to bypass validation.

Three separate export processes from the clean, read-only QA DSR source reproduced the new fingerprint. The normalized semantic JSON was byte-identical across separate output/temp directories (503,325 bytes; SHA-256 `E83D21BF79E1C8384C79E414B6735E4CCD792207C2CB34D4EC0170C90A94E586`). The projection uses key-addressed records, so filesystem enumeration order does not affect the result. No Steam path, network fetch, prior output, or stale audit file was used as an input.

The previously blocked owner QA Seed Code `DS1TR1-06NWCQQ3-K1HD8E66-2S3AD5N6-CTHZBKB8-FMSNNQWC-TRVW78NY-040F30XX-F0` (master `13825956`) now completed full backend generation on Fog attempt 2, Fog seed `6843`. Attempts 0 and 1 were rejected by the V5 Ceaseless rule. The successful 73-file package had both expected combined/Fog validations and all 73 manifest file hashes verified. The QA DSR source remained read-only.

A real backend study then exercised master seeds `1..1000`, one combined attempt per master, unchanged Fog retry bounds, Triple settings with bosses and Fog enabled, and the owner overlay off. Results: 248 successes, 752 failures (24.8% success). There were 4,489 Fog calls: 4,241 rejected with the same Ceaseless physical-return reason and 248 accepted. Every failure exhausted all five Fog attempts; no other rejection category or technical failure occurred. The successful attempt indices 0-4 were respectively 50, 52, 55, 45, and 46; successful worlds used a mean 2.94 Fog attempts, median 3, p95 5. All 248 manifests verified 73/73 output files and both combined/Fog hash validations. Retry limits were not changed.

The 20-seed differential sample of rejected raw Fog seeds was accepted by pinned FogMod but rejected by V5's fixed-only Ceaseless postcondition. This is an intentional policy divergence; upstream graph acceptance does not establish a physically safe post-boss itinerary, so these results do not prove 20 false positives. No false positive was confirmed, but the sample also cannot prove the validator has none. At 24.8% end-to-end success, ordinary Randomize use is unreliable. Portable creation and new manual QA seeds remain gated until the owner decides whether to keep the fixed-route guarantee or authorize a separately proven itinerary-based validator. Full metrics and evidence hashes are in `docs/IMPLEMENTATION_STATUS.md`; differential method and seeds are in `docs/FOGHOST_INTEGRATION.md`.

## PvP-off physical gates and full player-landing parity (2026-10-25)

Compact V2 removes disabled PvP entrances from the randomization graph while
retaining the vanilla fixed Area links. FogMod's writer normally emits
Event 5700 mode-1 calls for fixed PvP gates; removing those records from the
projected annotation also removes that writer behavior. `FogCompactV2PvpPolicy`
now restores those calls for the unprojected physical gates in common event 0.
For Parish-to-Andre `o1408_0000` / entity `1011996` and the Parish/Firelink
elevator `o1410_0000` / entity `1011988`, real-format upstream and Compact
outputs both contain the mode-1 initializer and preserve the same pristine MSB
object transform. With the relevant option enabled, eligible gates remain in
the randomization graph and use the normal FogMod writer path.

Pinned upstream has 46 PvP records: 15 major and 31 minor. Twelve are doors and
one is unused, so 33 non-door records are eligible for graph projection.
Compact V2 adds 20 project-owned Anor Londo arena gates to those 33, yielding 53
records: 31 major and 22 minor. The 13 upstream door/unused records are not
shuffle inputs; their physical solo initializers are handled separately.

The complete real-format landing comparison covered 194 Entrance sides:

- 144 matched upstream's resulting map, player XYZ, and yaw: 130 default
  object-relative placements, 2 custom warps, 3 destination-map overrides,
  and 9 special-tag offsets.
- 42 sides had no upstream identity: both sides of project-owned `o1309` and
  the 20 project arena entrances `o2780_*`. These are explicit project-owned
  exceptions, not parity failures.
- 8 sides across door entrances `o2908_0000`, `o3963_0000`, `o4960_0000`, and
  `o7907_0000` produce no player part in the upstream writer's door branch;
  these are checked as door transitions rather than player landings.
- Unexplained parity failures: **0**.

The full machine-readable CSV, including game-derived positions, is kept at
`%TEMP%\fogmod-v5-physical-audit-harness-20261025-a\reports-current-rebuilt-seed4009338015\compact-v2-landing-parity.csv`
and is not committed as extracted DSR data.
This audit establishes Compact-to-upstream placement parity for its tested
real-format seed.

The owner later supplied V5 Seed Code
`DS1TR1-06NWCQQ3-K2HDEE66-RS30D9N6-2R31C11B-3KT74XF8-TRW6G1J0-5CE38MF4-582WV6E4-AGVH0B8M-TGV4HJ8W-JKKMNWTJ-HBS764X1-NV3N20D1-QDJM9NA0-S1JG2TEN-9X97WJ86-QKQ035S2-YJN7E80N-16X6QG5H-MAHG8BAS-82RG8KA0-C0FJ02D7-2R3SE92P-QAJ2D9P0-HBDD2TMC-HHN0RPQT-2ANCA016-ZGP0P`
(master seed `332055808`, PvP options off). The supplied support bundle's
manifest instead records a V4 Seed Code ending `...2ANCA013-N99NW`; it is not
the generated-package manifest for this V5 code.

The saved V5 package from FogHost 0.7.0/0.7.1 used Fog seed `4009337993` on
attempt 0. Replaying that graph and writer path identifies a candidate Parish
arrival: `o4972_0000` BSide (`demonruins_izalith`) routes to `o1419_0000`
ASide (`parish`). Generated player entity `1815829` is in
`m10_01_00_00.msb` at `(-50.22262, -21.81, -32.293694)`, yaw `155`. The
pristine `o1419_0000` object is entity `1011700` at
`(-49.8, -21.81, -33.2)`, yaw `-25`; upstream uses the ordinary one-meter
object-relative landing. The real-format parity report records the same map,
position, yaw, and action region `1012600` for upstream and Compact. This rules
out a Compact-only landing-transform mismatch for this candidate. The negative
Y coordinate alone does not prove that the spawn is below walkable geometry.

Local saved manifests also show FogHost 0.7.2 rejecting attempts 0–3 with
`FOG_SEED_UNSOLVABLE` and accepting attempt 4 with Fog seed `31341427`. That
accepted package is a different generated world from the historical 0.7.0/
0.7.1 attempt-0 output above. The screenshots and V4 support bundle did not identify the later V5 package.
The owner subsequently supplied the exact V5 gameplay code, and the saved
accepted-attempt package matches its manifest hashes 55/55. That run and its
o5865 to o1413 under-map landing are analyzed below. The corrected 0.7.3
output passes a real-format regression; gameplay on that corrected output
remains pending. Do not build or promote another gameplay Portable until that
gameplay check is complete.

## Owner-confirmed o5865 to o1413 under-map landing (2026-10-25)

The exact owner-provided V5 code matches the saved attempt-4 package manifest:
55/55 generated-file hashes match. The package records app 0.46.2, FogHost
0.7.2, master seed 1653231178, combined seed
84332a4856ced43d8ca0538e789a36a0, and accepted Fog seed 2831875538. Both Major
and Minor PvP fog options are off, so this route is separate from the optional
PvP-gate issue.

The confirmed edge is Anor Londo main approach, entrance o5865_0000 / side
anorlondo_main, to Lower Undead Burg, entrance o1413_0000 / side burg_capra.
The owner-reported under-map arrival is the destination of this edge.

At pinned FogMod commit 6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f, the
dist/fog.txt o1413 record has Entrance Tags boss and BSide Area burg_capra.
The BSide Flag is 11010902; Cond is null; TrapFlag, EntryFlag,
BeforeWarpFlag, and WarpFlag default to 0. BossTrigger and BossTriggerArea are
absent (0/null); CustomWarp and DestinationMap are null; ActionRegion and
CustomActionWidth default to 0; Side.Tags is empty. The evidence used the
pinned dist/fog.txt record and FogMod/GameDataWriter.cs only; upstream data
was not copied into the repository.

The pinned writer selects side.DestinationMap or the entrance map, which is
parish and resolves to m10_01_00_00.msb. The pristine source object
o1413_0000 / entity 1011790 is at (-72.75, -43.57, -16.5), yaw 150.
The BSide default warp moves one metre opposite the object yaw, retains yaw
150 for player facing, and adds no vertical offset because there is no higher
side tag. Its effective player position is (-73.25, -43.57, -15.633975),
yaw 150. Separately, its default action region is one metre opposite object
yaw, one metre lower because this is not a world-tagged Entrance: position
(-73.25, -44.57, -15.633975), yaw -30, with the standard 1.5 x 3 x 1.5 box.

The saved FogHost 0.7.2 package instead put player c0000_0059, entity 1815927,
at (-72.25, -44, -17.2), yaw 150. That is exactly the center of the
project-owned Capra BossTriggerArea, not FogMod's fog-object-relative landing.
The earlier Compact code generalized BossTriggerCenteredSpawn and treated a
boss trigger volume as a player warp. Commit 15e58e9 removed that generic
trigger-centered placement for every boss side; it did not add an o1413-specific
position or route exception.

A real-format V2 regression now reruns Fog seed 2831875538 with the accepted
world options and checks the route log, the default landing calculated from
the read-only QA MSB, the absence of any generated player at the boss-trigger
center, and the separately preserved action and boss-start regions. The
FogHost 0.7.3 output retains player c0000_0059 / entity 1815927 at
(-73.25, -43.57, -15.633975), yaw 150; its action region is entity 1815926
at (-73.25, -44.57, -15.633975), and its separate Capra boss-start region is
entity 1815925 at (-72.25, -44, -17.2). The trigger remains available to the
encounter event and is not used as the spawn. Because the old coordinates did
not match upstream, the conditional investigation of other event or collision
state was not needed to explain this landing.

The full Compact V2 placement audit remains the 194-side real-format CSV
described above: 144 exact upstream output matches, 42 explicit
project-owned sides without upstream identities, and 8 door sides that create
no player. It has zero unexplained failures. The CSV remains in the recorded
temporary report path because it contains game-derived coordinates. The
confirmed run had PvP options off; this finding does not resolve or alter the
separate PvP-gate audit. No replacement Portable was built.
## Boss-completion model

The Compact catalog adds a typed `BossCompletion` condition, represented as
`boss_defeated_<eventId>`. The route condition is separate from Area reachability,
item ownership, and `DefaultFlagCols`. Unknown boss-completion references fail
catalog validation.

The graph checker keeps completion state separately from visited Areas and
items. It can derive a boss encounter as defeatable only after traversing a
specific directed randomized edge into that encounter, where the generated
FogMod side has the expected encounter-start event, entrance identity, and
FogMod-default landing semantics. For Ceaseless, the exact entrance side has no
BossTrigger, BossTriggerArea, or CustomWarp; its player landing is derived from
the Fog object's transform. A broad visit to a boss Area, a custom warp, or a
non-boss side does not grant this state.
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

The randomized Ceaseless arrival uses entrance `o4961_0000`, Demon Ruins side
B, in `m14_01_00_00`. Pinned FogMod's complete annotation record is: ID
`1411790`, Area `demonruins`, Text `To Ceaseless Discharge`, Tags `boss`;
ASide Area `demonruins`; BSide Area `demonruins_ceaseless`, Flag `11410900`.
The BSide has no BossTrigger, BossTriggerArea, CustomWarp, ActionRegion,
DestinationMap, or side Tags. Upstream `GameDataWriter` therefore uses the
Fog object's transform for the player warp: with pristine `o4961_0000` at
`(249, -283.15, 70.4)`, yaw `-136`, the BSide moves one metre along yaw `44`
and keeps player yaw `-136`. The generated player arrival is
`(249.69466, -283.15, 71.11934)`, yaw `-136` (Player part `c0000_0051`), not
the center `(248.5, -284, 69.7)` of trigger region `1412698`.

In the exact Fog-seed `4009337993` real-format support reproduction, V2 emits
Player part `c0000_0051` and action-region entity `1815856` at
`(249.69466, -284.15, 71.11934)`, yaw `44`. The player part is at the same
X/Z, Y `-283.15`, yaw `-136`. The generated region ID is allocated by the
writer for that output; it is not a fixed value from the upstream record.

That arrival is not inside the trigger at spawn: relative to the trigger's
`-135` yaw it is about 1.85 units along the depth axis, beyond the 0.75
half-depth (the box is 5 x 2 x 1.5). The correction restores the original
landing semantics and avoids the under-map spawn; it does not establish that
the encounter starts immediately on arrival. Reaching the encounter trigger,
boss activation, kill, and post-boss passage remain in-game QA checks.

The trigger region remains a separate boss-start volume: encounter-start
event `11415371` checks region `1412698`, while boss-death/progression event
`11410410` is a distinct identity. Compact V2 retains the ordinary action
region generated beside the fog and the default object-relative player warp;
the trigger geometry is never used as a spawn coordinate. Catalog construction
checks the pristine boss-start event/region and requires this exact Ceaseless
side to have no BossTrigger, BossTriggerArea, or CustomWarp. No hand-authored
coordinate is used.

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
evaluated as graph predicates. `FogMod/GameDataWriter.cs` lines 483-487,
517-547, and 553-599 show the actual ordinary placement path: find the source
fog object, construct an action region one metre to the side, and create a
player part one metre from the object's position using the side-relative
rotation. For this BSide the writer's result is
`(249.69466, -283.15, 71.11934)`, yaw `-136`. This disproved the earlier V5
trigger-centered arrival policy; the Compact projection had incorrectly
turned BossTriggerArea into CustomWarp, bypassing the default writer branch.
The current correction restores that branch and keeps the separate project
boss-completion graph policy. `GraphConnector.cs` was checked for bounded
graph-solver behavior. No upstream annotation or `dist` file was copied into
the repository or used as a substitute for project-owned policy.

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

## Ceaseless landing correction verification (2026-10-22)

The preceding V2 package digests and centered-spawn description are the
pre-correction record and are superseded for current V5 by this section.
Pinned upstream was read-only at
`C:\Users\Adam\AppData\Local\Temp\fogmod-src`, exact HEAD
`6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f`. `dist/fog.txt`'s exact
`o4961_0000` BSide has `Area: demonruins_ceaseless` and `Flag: 11410900`,
with no custom warp, action region, boss trigger, destination-map override, or
side tag. `GameDataWriter.cs` uses the fog object position, one-metre
side-relative offset, and fog yaw for the player rotation. Clean DSR object
transform `(249, -283.15, 70.4)`, yaw `-136`, produces
`(249.69466, -283.15, 71.11934)`, yaw `-136`. The separate action region is
generated using the normal one-metre offset and the non-world Y adjustment.

The implementation now validates encounter-start event `11415371` and its
region `1412698` from pristine EMEVD facts without using the region as spawn
geometry. It keeps completion event `11410410` separate; the graph route still
requires `boss_defeated_11410410`, and visiting the boss Area alone remains
insufficient. No manual coordinate was added.

FogHost `0.7.1` Release build passed with 0 warnings and 0 errors. The full
FogHost test suite passed **149/149** against the pinned read-only upstream
fixture and clean QA DSR. Focused backend generation-profile, projection, and
prerequisite tests passed **18/18**; final full backend/typecheck results are
recorded in `IMPLEMENTATION_STATUS.md`. Real-format golden verification passed
both existing V5 cases: support reproduction, 55 files, digest
`6bdd3ab7f11e5e1fc4e46d906942cb819b0b5862e739cf7bbcf45e1be3d908a9`; full
Items + Enemies + Bosses + Fog, 73 files, digest
`52765e667a9d23ff6079e9aa494da981608750b7a171d2be38e7bd79aff72934`.
Each differs from its pre-correction output in `event/common.emevd.dcx` and
the 13 MSBs listed in `GENERATION_COMPATIBILITY.md`. V1-V4 fixtures were not
changed. These checks establish deterministic real-format package output,
not in-game safety; owner gameplay QA remains required.

## Ceaseless post-boss physical route correction verification (2026-10-23)

The owner-reported hard-lock is represented by the project's verified physical
route facts: the lower route is from main `demonruins` to `demonruins_lava`,
gated by boss completion event `11410410`. It is not a local route from the
Ceaseless pocket. The Ceaseless boundary `o4961_0000` can be randomized, so
after killing the boss the player must leave through that actual boundary and
reach main Demon Ruins over fixed physical transitions before the event-gated
lower route is considered usable.

FogHost `0.7.2` adds a V5 postcondition after FogMod's general graph solver.
For every boss-gated destination, the validator starts at the associated
encounter with its completion state set, and checks a constrained graph in
which the encounter may use its named exit entrance and subsequent traversal
is limited to fixed physical routes. General reachability through another
randomized Fog gate is logged for diagnosis but does not pass this
postcondition. An ambiguous or missing physical route returns retryable
`FOG_SEED_UNSOLVABLE` at `solve-graph`; ordinary Fog retries choose the next
deterministic Fog seed. The vendored `GraphChecker` gained an optional edge
predicate; default calls remain unrestricted for V1-V4.

Real-format replay used the exact owner Seed Code and pristine read-only QA
source. Fog attempts 0 through 3 were rejected, including original Fog seed
`4009337993`, whose first Asylum Fog led directly into `demonruins_ceaseless`.
Attempt 4, Fog seed `31341427`, passed. The accepted package had 55
manifest-listed files, all independently verified by size and SHA-256; its
manifest SHA-256 was
`2a0788c054bce3f6463ab1ba7c9fb3bbaffc7518d308d21aa42788c4f0264132`. An
independent process rerun reproduced the same attempt sequence, package file
hashes, and manifest digest. Unit coverage proves the sealed encounter is
rejected even if the destination is globally reachable only through an
unrelated randomized entrance, while a fixed physical return succeeds.

FogHost Release build and **118/118** tests passed; the backend suite and root
typecheck are rerun for the final task record in `IMPLEMENTATION_STATUS.md`.
This validates real-format package generation and progression graph policy;
it is not a substitute for the owner's in-game boss-fight and post-boss route
check. The read-only QA source was never modified, and the live Steam install
was not accessed.

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
