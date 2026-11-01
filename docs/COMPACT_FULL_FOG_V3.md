# Compact Fog v3 and generation profile `ds1tr-gen-v6`

`ds1tr-gen-v6` (annotation source `compact-v3`, catalog `ds1tr-compact-full-fog-v3`) fixes the original Fog hard-lock at its root: a start region that the solver believed was connected to the world, but physically was not. It replaces the Ceaseless-specific post-boss route check of v5 with a general start-reachability rule. V1 to V5 are untouched and replay exactly as before. Nothing here has been played in the game yet; see "What is not proven".

## The original hard-lock

Source evidence: support bundle `DS1TR-support-06NWCQQ3-20261020-181751Z.zip` (profile `ds1tr-gen-v4`, master seed 332055808, Fog seed 4009337993, Fog attempt 0), the 2026-10-20 diagnosis (`dcc5848`) and a replay of the frozen v1 catalog graph for that seed.

- The Asylum starting hallway (Area `asylum`) is separated from Oscar's cell by a randomizable wall, `o8952_0000`. The wall is the hallway's only randomized exit.
- In the original layout the wall paired with the Ceaseless boss pocket (`demonruins_ceaseless`), whose only randomized exit (`o4961_0000`) returns to the hallway: a closed two-area loop. The player enters, can only walk back, and nothing else is reachable.
- FogMod's solver accepted the layout because the project's own v1 and v2 catalog gave the hallway **two unconditional fixed exits** that upstream FogMod does not have: `asylum > asylum_outside` (made by `AddLocalSequence`) and `asylum > firelink` (a `MapRoute` called the tutorial departure). Upstream FogMod conditions the first on having visited the balcony and Oscar's cell and has no hallway route to Firelink (the crow departure is the fixed or randomized warp of `asylum_loop`).
- The 2026-10-20 diagnosis blamed a second false edge, an unconditional `demonruins_ceaseless > demonruins_lava` route. Removing that edge alone changes nothing for this seed: the hallway's own false exits still make every Area look reachable. The later Ceaseless post-boss route work (`7766d69`, `a24cc36`, the v5 validator) therefore addressed a different problem. It rejects the original loop only incidentally, because the pocket's exit area has no fixed route to the lava region.

Quantified on the frozen catalogs, reading the connected graph with "no unconditional fixed exit leaves the hallway" (raw Fog seeds, the five original options, scratch measurement of 2026-11-01): 53.5% of 200 v4 layouts and 45.7% of 300 v5 layouts leave the player in a closed start region (42.0% and 36.0% severe, 15 Areas or fewer). The Ceaseless pocket was one of many dead-end destinations. Of the 14 v5 worlds the Ceaseless check accepted in that sample, 5 were start-trapped by this reading. The V5 owner smoke seed (Fog seed 6843) and the V5 support-reproduction golden are among them.

## What v6 changes

1. **Catalog v3** (`FogCompactCatalogBuilder.BuildV3`, built from the unchanged v2 output): the hallway keeps no unconditional exit. `asylum > asylum_outside` requires the balcony and Oscar's cell to have been visited; the hallway-owned `asylum > firelink` route is removed (the crow warp of `asylum_loop` remains the departure, with and without area warps). Catalog construction fails if the hallway ever gains an unconditional exit. All other links are v2's. The v2 fingerprint `73f3c842...` is unchanged; v3 is `df7aa8f9db20ac309a57dce26ecdc5691e1e6c45c9fa56920d61cbac002802a8`.
2. **The rule lives in the graph, not in a stricter runtime check.** FogMod's connector repairs layouts for up to 100 tries against what the graph says. With a physically correct hallway it steers the Asylum wall toward a region that reaches the world, instead of the validator rejecting after the fact.
3. **`StartReachabilityValidator`** (v6 only) re-asserts after the connector that every Area is reachable from the start Area. This single statement covers start traps, terminal pockets and dead-end cycles for any region or boss. A failure is the same retryable `FOG_SEED_UNSOLVABLE` (`solve-graph`, scope `fog`) as the connector's own failures.
4. **The Ceaseless post-boss route proof is retired for v6.** Its boss-completion rule no longer requires a fixed post-boss route, and `BossProgressionValidator` is not called for `compact-v3`. The lower Demon Ruins route stays an ordinary boss-completion edge (`demonruins > demonruins_lava` gated by event `11410410`) and the default landing fix for Ceaseless is kept. `compact-v2` (v5) still runs its frozen check.
5. **Host compatibility identifier unchanged.** `Program.HostVersion` stays `0.7.3`; v6 is selected by its annotation source and catalog fingerprint, so no earlier profile's allow-list changed. A v6 request is refused by an older host as an unknown annotation source. The FogHost project version is `0.8.0`.
6. **The retry budget is unchanged** (3 combined, 5 Fog attempts).

## Regression coverage

| Case | What it proves | Where |
|---|---|---|
| A: original confirmed hard-lock (Fog seed 4009337993, v1 catalog) | The frozen graph accepts it, the hallway's only randomized exit pairs with the pocket, the pocket returns to the hallway, and the sealed hallway reaches at most five Areas. On v3 the same seed reaches every Area. | `StartReachabilityTest.VerifyOriginalHardLockSeed`; golden `v6-original-hardlock-reproduction` |
| B: V5 owner smoke seed (Fog seed 6843, v2 catalog) | The accepted v5 world traps the sealed hallway in a small region; on v3 the same Fog seed reaches every Area. | `StartReachabilityTest.VerifySmokeSeedStartTrap`; golden `v6-owner-smoke-start-trap` |
| C: valid world rejected by the Ceaseless rule (Fog seed 31341427) | v5's post-boss rule still rejects it (frozen), the start reaches the world, and v6 accepts it. | `StartReachabilityTest.VerifyWorldRejectedByCeaselessRuleIsAcceptedByV6`; golden `v6-v5-rejected-route-search-2` (all five v5 Fog attempts rejected, v6 accepts the first) |
| Synthetic trap | A start whose only exit pairs with a dead-end pocket is rejected, retryably; an unconditional fixed exit out of the start hides it (the defect). | `StartReachabilityTest` |
| Catalog seal | v3 has no unconditional hallway exit, v2 still has both (v5 frozen), only the hallway changed, no completion rule asks for the retired proof. | `StartReachabilityTest.VerifyV3CatalogSealsTheAsylumStart` |
| V1 to V5 goldens | Unchanged: every earlier fixture file is untouched and verifies. | `goldenCompatibility.mjs verify` |

## Acceptance

Raw Fog seeds 1 to 300, the five original options, through the real generation path (including writing and decoding every output file):

| | v5 (`compact-v2`) | v6 (`compact-v3`) |
|---|---|---|
| Fog seeds accepted | 14 of 300 (4.7%) | 300 of 300 (100%) |
| Rejected by the Ceaseless rule | 286 | 0 (rule retired) |
| Start-trapped worlds by the sealed-hallway reading | 137 (5 of them accepted) | 0 by construction (no unconditional hallway exit; the validator re-checks reachability) |

Master seeds on the Triple defaults through the real pipeline (packaged helpers, masters `sample-ds1tr-gen-v6-1` to `-100`): 100 of 100 generated, every one on its first Fog attempt. The v5 study (`docs/COMPACT_FULL_FOG_V2.md`, masters 1 to 1000, five Fog attempts each) accepted 248 of 1000 (24.8%); a v5 master seed that needed all five attempts is now a first-attempt success (golden `v6-v5-rejected-route-search-2`). The two samples use different master seeds, so they are comparable in order of magnitude only. The Fog retry budget was not changed.

## What is not proven

- No v6 world has been played. The start-trap diagnosis is a model-based finding; the quickest in-game confirmation is to load the V5 owner smoke code (`DS1TR1-06NWCQQ3-K1HD8E66-2S3AD5N6-CTHZBKB8-FMSNNQWC-TRVW78NY-040F30XX-F0`, which stays V5) and look at where the first Asylum wall leads, then play the V6 smoke code.
- Only the Asylum start was corrected. A read-only comparison of the project's fixed Area routes with upstream FogMod's `Area.To` list found 11 further routes the project made unconditional where upstream has a condition (for example Asylum prebalcony to F2 west needs the key; the Burg residences need the residence key; the Lava Izalith crossings need the orange charred ring; DLC hallway to Gough needs the crest key and Artorias; DLC township hidden area needs the cast light; Duke's library to the crystal golem needs Darkroot; New Londo Drakes to Beatrice needs the Four Kings; Sen's cage needs the cage key; Anor Londo painting to the chandelier needs the rafters) and 157 project-only fixed routes that were not cross-checked (upstream expresses many map links through entrances and warps instead). Any of these can still make the solver optimistic mid-game. They are residual risk and a follow-up, not part of v6.
- Removing the Ceaseless post-boss check relies on the topology argument: boss completion is a persistent event flag, the Ceaseless pocket is a leaf, and the lava route belongs to main Demon Ruins. No gameplay trace shows that route being needed or unneeded.
