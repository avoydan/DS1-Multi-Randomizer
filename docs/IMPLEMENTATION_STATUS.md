# Implementation status

## Compact Full Fog V1 production path (2026-10-14; desktop 0.39.0 retained)

FogCompactCatalogBuilder compiles verified pristine DSR facts plus project rules into the existing typed FogAnnotationCatalog; FogLogicalAreaResolver resolves map defaults, boss entities/events/collisions, custom-start regions, and authored entrance objects. Production Compact projection is enabled for profile ds1tr-gen-v3 under fingerprint 8c03fd3de1cd7c772d5c933753ae54eab35f9e8b70a1aef2954322330d0002d4. It does not read, bundle, or download fog.txt.

The offline manifest contains 58 verified inputs: 19 copied from the clean game snapshot and 39 locally reconstructed files, with zero upstream entries. The catalog covers 131 Areas, 64 Entrances, 6 project Warps, 22 key-item identities (9 required / 13 random-only / 0 hard-only), 723 safe lots, 69 enemy rules, 22 completion flags, and 131 costs. V1/v2 continue using the frozen 59-file legacy data and item-tracking behavior.

Verification: Compact candidate graph test passed (14 graph profiles, 16 starts, 1000-seed progression stress). Real GameDataWriter matrix passed 18/18 configurations with 54 outputs decoded each, plus deterministic repeat. Cold-cache full Items + Enemies + Fog generation produced a 73-file package in one attempt. New v3 golden fixture regenerated from the clean QA DSR source with an empty cache and matched all 73 package file hashes. Focused backend tests: 36 passed, 0 failed, 1 general golden regeneration test skipped by design; the v3 golden was separately regenerated and passed. FogHost Release build passed with NU1900 because NuGet's vulnerability index was unreachable. No DSR gameplay launch, app version bump, commit, push, or release was done.
## Local game convenience: free cursor shortcut (desktop 0.39.0; 2026-10-13)

The default-off **Free cursor / Swobodny kursor** card is in **Your game / Twoja gra**. Its configurable shortcut defaults to F10 and is stored with Electron's local `userData/settings.json` settings, outside generation settings and Seed Codes. With the option enabled, the detached Windows `GameCursorHelper.exe` installs a low-level keyboard hook and reacts only when the configured key and modifiers are pressed while a visible `DarkSoulsRemastered.exe` window is foreground. After a 65 ms delay it calls `ClipCursor(NULL)` and activates the most recently used sensible non-DSR top-level window; if none is remembered, it selects the first eligible window. It excludes the launcher and helper, does not minimize DSR, and clicking the game restores focus normally. The helper starts when the enabled setting is loaded (even before the game is launched), survives launcher exit, and exits after it has observed DSR and the game process closes; if DSR never starts, it exits five minutes after the launcher closes. Changing the shortcut restarts the helper immediately. No injection or administrator rights are used. The launcher-only run still permits all randomizers OFF, applies an empty package, writes no DSR files, and advances to **Launch game**. Cursor helper tests 5/5, UI check 1/1, launcher-only checks 3/3, and Windows helper `--self-test` passed; the owner manually verified the configured shortcut in-game.

## Fog offline reconstruction development history (2026-10-13; superseded by production status above)

Added a development-only `audit-transition-facts` report that reads 132 pristine MSB `ConnectCollision` parts across 17 maps and embeds the existing typed ObjAct/EMEVD/flag/warp evidence. All 132 exact/wildcard target map IDs match one supported map; the facts are explicitly marked as map-load metadata, not traversal. No Area/Entrance/Warp relation was inferred from this source. The prior graph remains incomplete: 98 Area records unresolved; 2 static links, 3 Entrance candidates, no authored Warps, 23 no-static dispositions, and 104 unresolved Area dispositions.

DefaultFlagCols now has 22 authored project-policy entries. Each records its expected boss completion event, anchor kind (collision enemy, encoded negative PlayRegionID, or both), and rationale; the resolver validates those anchors against pristine MSB/EMEVD and the common event-970 flag flow. Post-derivation comparison classifies 19 as E and 3 as project-only B. The three B cases have a same-collision boss enemy mapped to a unique HandleBossDefeat event consumed by event 970; they are retained as intentional project policy rather than omitted to match the reference. Eighty-eight reference-active rows remain outside this project policy, and the Firelink sentinel remains unused. KeyItem identity policy now covers all 22 independently observed pristine Goods possession-check candidates (including exact GameParam IDs, English FMG names, and map/event anchors); the 16 identity matches are a subset, while six are project-only candidates. This is identity progress only: candidate completeness is not claimed, and default Area, mechanic disposition (required/random-only/hard), and complete Fog item records remain open pending the separate progression-item pass. Talk/ESD and interaction-derived additions have not yet been closed. LotLocations remain 0/440; source facts exist for treasure, enemy drops, and event awards, but source owner-to-Area policy is still open. Enemies remain 0/392; map defaults, collision overrides, and `Includes` still need authored ownership. DefaultCost remains 0/127; the project tier curve is still pending the stable graph. Backend project-catalog projection, provider comparison, progression matrices, and production offline switch remain gated.

`FogProjectAreaPolicyV1` now materializes all 131 stable Area IDs as typed project records with owner map, ordinary/boss/optional/trivial classification, rationale, map-membership anchor, and additional verified boss/start/gate/static-link anchors. Field counts: map 131/131, classification 131/131, rationale 131/131, at least one anchor 131/131, and 2 authored static-link sources. Full transition topology is still incomplete: 129 IDs have no authored static `Area.To` edge and remain explicitly pending Entrance/Warp route ownership; empty link lists are not treated as evidence that no physical connection exists.

FogHost's .NET 8 SDK is available at `C:\Users\Adam\dn8sdk\dotnet.exe`. A focused Release build of FogHost.Tests passes with two NU1900 warnings because NuGet's vulnerability index is unreachable. With the valid local Fog data root and byte-verified clean DSR copy, FogHost.Tests passes 117/117, including the 22 DefaultFlagCols policy anchors, 22 item identity/GameParam/FMG/EMEVD checks, all 131 Area policy records, and the Entrance V1 checks below. A first run used an incomplete local root and reported 14 fixture-path failures; that was corrected by using the existing complete root, and the full test run then passed. Backend typecheck and packaging from the previous continuation remain unchanged; no runtime Fog provider change, version bump, commit, push, tag, or release has been made in this pass.

Entrance policy authoring now has a typed V1 slice for the two fixed key doors and the Pinwheel boss boundary. All three have authored logical sides, classifications, rationale, and pristine object anchors; the Crest door and Pinwheel gate also validate action regions, and all three validate their interaction events. The key-door conditions validate the exact required Goods IDs against their event checks. Field completeness is 3/3 within this authored slice; the full project Entrance inventory remains open, and the Pinwheel project policy is not yet projected to runtime FogMod DTOs. The focused clean-DSR suite passes **117/117** after this addition.

The current Warp investigation corrected `WarpPlayer` initial-area decoding in `AreaGateEvidenceAudit`: the field is read from its aligned argument offset, with a short-argument guard and self-test fixture. This fixes evidence parsing only; it does not author or infer a randomizable Warp route. The focused reconstruction self-test and Release build passed before the work was paused.

## Roaming-boss 100% placement correctness (2026-10-12)

New runs use `ds1tr-gen-v2`. The public prerelease 0.38.4 had already exposed the roaming-boss Seed Code options, so `ds1tr-gen-v1` remains frozen with its prior protected-slot behavior and its existing golden fixtures were not changed. V2 reuses the existing safe handlers for passive, event-model-locked, tangible ghost and Darkroot butterfly-path destinations. For Both + 100% boss chance it skips the normal assignment the boss pass would discard; the distinct-model budget itself remains intact. Seed Code format and combined engine RNG salt `0.5.66` are unchanged. The six full-playthrough QA Seed Codes in `FINAL_GAMEPLAY_QA.md` were regenerated from their recipes under v2; the manually pinned `SMOKE_TESTS.md` v1 codes were left untouched.

The real-catalog core matrix ran three seeds each for Bosses only 0/50/100 and Both 0/50/100. Results: 0% = 0 bosses; Bosses only 50% = 2,600/5,148; Bosses only 100% = 5,148/5,148; Both 50% = 2,533 bosses plus normal fallback/model-budget cases; Both 100% = 5,148/5,148 with zero normal draws and zero unchanged reasons. A paired run with the same master seed produced identical sorted Bosses only / Both + 100% slot-ID sets (1,716 each); boss models per slot can differ due to Both's category draw. All 18 core runs completed without retries, event-window overruns, event-owner errors or nondeterministic reruns. Additional runs covered stay-dead ON/OFF, fit, representative curves, Prevent same ON/OFF, Type replacement ON/OFF and Fog ON/OFF. The final output audit contains all 1,716 eligible ordinary slots; planned boss models match the final encoded output for all slots, and the event-owner audit reports zero missing/duplicate owners. Details and CSV: `docs/BOSSES_ONLY_AUDIT.md` and `docs/placement-audits/bosses-only-100-seed-d4a649e430e4222ab4546ecb20a38c3b.csv`.

**Verification:** backend typecheck passes; all backend tests except the environment-dependent `supportBundle.test.mjs` pass (581 pass, 2 skipped). The full backend test command reports one file-load failure because this sandbox returns `uv_os_get_passwd ENOMEM` from `os.userInfo()`. `DsrDataTool.Tests` passes 14/14. All four v2 roaming-boss golden cases pass. Full local golden verification has 26 unrelated QA-environment failures: Fog-enabled digest drift against the local world-data root and 16 key-placement cases without progression-model JSON. Desktop stays 0.39.0. No game was launched; gameplay AI/navigation/grounding/performance remain unverified. Fog checkpoint `870aa577a3a282206645ef63753dca0c37c90e4b` and Fog reconstruction files remain untouched. No release, commit or push was made, per task instruction.

## Fog annotation reconstruction update (2026-10-12)

The 16 project-owned custom starts now resolve to named pristine DSR spawn regions and match the reference locations, stable ordering, and logical-area assignments (16/16 for each). Runtime inspection confirms that a selected custom start sets the reachability origin, resolves a spawn region, and supplies the in-game return label. The Depths start additionally sets ObjAct entity 11000120's ObjActParamID to -1 to bypass its entrance key; no other start has an event/flag prerequisite. Two display labels are reviewed as representation-only (R). Six start-area records now have explicit, independently reviewed area semantics (ordinary progression region, no area-static links or scaling-base override); the Lost Izalith pre-lava node has a fact-anchored `trivial` connector tag. A comparison-only audit reports 6/16 complete consumed area records, 16/16 tag matches, 16/16 scaling-base matches, and 16 reference links across the ten remaining start nodes. This reduces unresolved Areas from 131 to 125; full graph connectivity remains open. Clean Darkroot EMEVD tracing separates the Crest-sensitive `11200100`/`11200110` gate interactions from the distinct two-sided `11200090` CP1 short-warp wall; their logical-area endpoints and relationship to the start node's static link remain unassigned. The cross-map candidate audit indexes 249 name-marked transition regions; a focused pass over seven unresolved start maps characterized 74 of them and added yaw/shape extents to the evidence report. These remain candidates, not authored graph edges. The typed model separates project area IDs from FogMod projection keys and resolves references consistently across categories. The Asylum `h0016B1` boss flag now resolves through a named project rule linking its pristine `HandleBossDefeat` event to common event 970's flag read; `DefaultFlagCols` is 1 project-policy, 1 unused, and 106 unresolved. All 16 starts resolve to a declared node with matching pristine map membership. `FogDsrFacts` labels EMEVD provenance and reads pristine EMEVD when the clean game path is supplied, after byte-checking all maps and GameParam. FogHost full tests passed with the pinned data root and clean QA copy: 117/117, including the spatial-anchor assertion. The production downloader/data root remain unchanged, with no version bump or release.

**Area inventory audit (2026-10-12):** added a development-only `audit-area-catalog` report. It resolves the existing 16 start-area anchors from pristine DSR and emits each project field, evidence, completeness status, and per-entry R/E/B/U classification across the 131-entry comparison inventory; it does not emit or import oracle graph fields. The current report is 16 derived anchors, 16 partial project policies, 6 complete records, and 125 unresolved (R=6, E=0, B=0, U=125). FogReconstruction self-tests pass, including checks for unresolved inventory and representation-only differences. A separate EMEVD candidate scan covers all 17 maps: 249 name-marked regions, 985 nearby-object associations, and 950 aligned raw-int32 candidate uses in its initial run. The scanner now checks every byte offset; those matches remain untyped leads and have not been assigned to project areas or entrances. The local .NET 8 SDK is available at `C:\Users\Adam\dn8sdk\dotnet.exe`; the global `dotnet` command does not find it.

# CURRENT TASK: OFFLINE FOG DATA RECONSTRUCTION — IN PROGRESS (2026-10-12; baseline 0.39.0 retained)

**2026-10-12 continuation:** the `Only where they fit` roaming-boss audit found filtering happens before candidate selection; no fitting boss is drawn and rejected. On the fixed production plan, 562/1,716 ordinary destinations received a boss and the other 1,154 have the explicit `fit` reason because no candidate fits their measured size class. Per-class and representative-map distributions, plus planner-to-final MSB/PARAM comparison (562/562 exact), are recorded in `docs/BOSSES_ONLY_AUDIT.md`. No size override or generation change was justified; v1 stays frozen and local `v0.38.4` contains only v1. External GitHub release listing was unavailable, so this relies on the repository's recorded public baseline and local tag.

**Fog continuation:** the 2026-10-12 world-transition and treasure-location audit is recorded at the top of `docs/FOG_OFFLINE_RECONSTRUCTION_ANALYSIS.md`. Areas still have 98 unresolved semantics; combined fixed-default/fixed-hard graphs remain B despite zero node/unpaired-exit differences. The current inventory has 2 static links, 3 entrance candidates, no authored warps, 23 no-static dispositions, and 104 unresolved area dispositions. KeyItems remain 0/27 and LotLocations 0/440 because pristine spatial evidence does not provide logical ownership for many references. DefaultFlagCols (106 unresolved), Enemies (0/392 complete), and DefaultCost (0/127) remain open. The production downloader/provider remains unchanged; no production switch or offline-network test was made.

**Verification and local package:** backend typecheck passed; roaming-boss focused tests passed 14/14. FogReconstruction Release build and self-test passed (14 existing obsolete-API warnings, 0 errors). FogHost Release packaging succeeded; FogHost.Tests passed 147/148 when given the clean DSR copy and current QA data root. The one failure is the environment's Goods FMG lookup for “Peculiar Doll”; no code was changed for it. The standard desktop packaging workflow and development release verifier passed for unsigned internal-channel 0.39.0. Artifacts: `apps/desktop/release/DS1-Multi-Randomizer-0.39.0-Setup-x64.exe` (182,237,218 bytes) and `apps/desktop/release/DS1-Multi-Randomizer-0.39.0-Portable-x64.exe` (181,600,369 bytes). No version bump, tag, push, or public release was made.

**Annotation follow-up (2026-10-12):** the pinned DS1 annotation-consumption audit is recorded in `docs/FOG_ANNOTATION_CONSUMPTION.md`. The typed semantic model now separates warp sides from ordinary entrance sides, records logical-area map membership and validates active entrance/warp endpoint map ownership, adapts to fresh FogMod DTOs, and has consumed-field and per-category fingerprints (v2 now includes the in-game custom-start return label). The development-only category comparer emits exact/unresolved/reviewed classifications and is covered by synthetic self-tests; it has not been run against a populated catalog. The DSR fact index reads all 17 supported maps, objects/collisions/enemies, 3,755 region IDs/transforms/shapes (Point=2,123, Box=1,191, Cylinder=177, Sphere=264), spawn points, direct `SetEventFlag` writes from map/common EMEVD (including the shared Darkroot archive mapping), and treasure-part transforms; 427/558 treasure events and 407/534 slot-1 ItemLot references have unique part transforms. It also reads GameParam item rows/slots and, when given the clean game path, English item names from FMG. The name resolver supports an explicit DSR item category, requires all four English FMG item tables, rejects duplicate table IDs plus wrong-category and unknown names, and resolves goods `3:384` to Peculiar Doll. A typed `FogKeyItemPolicy` resolver now maps authored semantic names plus English DSR item names to GameParam IDs while retaining policy order, default area and tags. A typed custom-start policy similarly resolves an authored map/region name to the DSR spawn-point ID and rejects absent or ambiguous regions. A project-owned 16-entry custom-start policy now resolves named DSR spawn regions, passes pristine fact validation, and matches the development oracle location set and authored order exactly. All 16 start area IDs and graph memberships now match the reference and validate against pristine MSB facts. FogMod starts reachability from that area and consumes the start display label in its return message; the project adapter now supplies it. Two clearer labels are reviewed as R (Anor Londo building and Painted World), with all IDs, areas, respawns, and ordering exact. The Depths start explicitly bypasses the entrance key requirement at ObjAct entity 11000120, while the other 15 starts have no per-start event/flag prerequisite. Full area topology remains unresolved, and the catalog is not connected to production. `FogTreasureCollisionAreaPolicy` groups slot-1 lot locations by authored map/collision rules, preserves rule order, rejects conflicting assignments, and reports unresolved references. A fact-backed enemy-scaling resolver now validates authored map/collision assignments and included enemy part names against pristine MSB facts, applies a map-wide default to otherwise-unassigned collisions, and reports still-unassigned groups; this is resolver infrastructure only, not a complete policy. A dev-only oracle audit found 286 map/collision groups: 273 with one annotated area, 11 spanning multiple areas and 2 with no annotated area; this is comparison evidence, not copied policy. A separate development-only query matched all 108 DefaultFlagCols entries to DSR collisions, but none of the assigned boss flags equal the collision PlayRegionID or DisableBonfireEntityID; all 108 source PlayRegionIDs are below 10, so those MSB fields alone do not derive the boss-flag association. Direct map-archive `SetEventFlag` scanning found same-map writers for 25/27 raw map/flag pairs (106/108 collision rows); the `firelink/h0017B2_0000` sentinel row is shadowed by an earlier writer special case, while `asylum/h0016B1` → `11810900` has no literal `SetEventFlag` writer. The clean-source trace links its collision enemy to `HandleBossDefeat` event `11810900`; common event 0 passes that ID to event 970, whose mapped conditions read the parameter as an event flag before its item-lot award instructions. This association is implemented and tested as project policy; see the C classification below. The development-only FogDsrSpatialIndex tests point containment for all four MSB shapes, including yaw-rotated boxes; its geometric candidates do not establish logical-area semantics. A verified-QA report covered 543 positive treasure slots: 412 have part transforms, 216 positioned references match no region volume, 72 match multiple regions, the maximum overlap was 10 regions for one reference. It distinguishes all five MSB treasure slots: 534 positive slot-1 references include 421 ItemLotParam matches and 113 distinct unmatched IDs; nine positive values are in slots 2-5. The unmatched IDs are absent from the pinned oracle's active `LotLocations`, so they are preserved as map facts but excluded from the consumed annotation model. The dev-only oracle scope is 131 areas, 27 keys, 15 warps, 109 entrances, 16 custom starts, 127 costs, 392 enemy rules, 440 lot starts and 108 default collision flags. The synthetic catalog test passes, but no complete project-owned policy has been authored yet. There is no production runtime switch, backend migration, or new-catalog/reference generation comparison. The production data root remains 58/59 and the downloader is still required. The existing oracle/oracle matrices do not validate the new catalog. Current verification: .NET 8 SDK 8.0.425 FogHost build and tests passed (117/117); FogReconstruction build and self-test passed (14 existing obsolete API warnings, 0 errors). Next: author the independent policy from verified DSR facts and project semantics, connect all consumers, then run new-vs-reference matrices and cold-offline verification. No release or version bump is authorized before those gates pass.

The previous “blocked at event 5700” status is superseded. FogHost now uses a named-instruction Event 5700 builder with a bounded contract harness and an eight-profile call matrix (1,335 initializers, four modes, 12 call shapes). All 20 added event bodies now have project-authored implementations: Event 5700 is checked for behavioral contract parity; the other 19 are checked against the pinned reference's operation and parameter semantics. This includes the eight Anor Londo/Battle of Stoicism events. Common events 5690, 8900, 8901 and 8950 and two Bed of Chaos helpers were already covered; Bed of Chaos helpers are derived from the pristine archive only when `noFallBedOfChaos` is enabled. Event 8960 reconstructs class-gated starting rewards, the one-time guard, delayed optional rewards and fixed Estus lot. The current temporary runtime verifier passes all 13 small added-event builders, including all eight Anor Londo events. Talk/ESD normalization now reaches 178/178 semantic parity across 17 archives; raw-model parity is 105/178. The full FogHost suite passes 143 tests (0 failed, 0 skipped) against a clean DSR staging copy. The current environment has the .NET 8 runtime but no SDK, so I compiled to a temporary directory with the installed Roslyn compiler and ran the suite on the .NET 8 runtime; standard dotnet build was unavailable.

Added/removed caller audit scanned all 18 reference archives: 20 added events (12 with direct reference callers; 8 with no static archive caller) and 96 removed events (zero remaining direct callers, including cross-archive candidates). Added-event reconstruction is complete. One hundred sixty-six of the 174 modified bodies now have project-authored transforms matching pinned-reference event semantics; 8 remain unresolved, all protected BossOverlay bodies (including 11810310 and 11815382). Common event 0 now reconstructs its randomized-start initializers under full source/target fingerprints and preserves the generated dynamic suffix when the known Fog target body is the exact prefix. M10 Parish event 0 removes the obsolete object/SFX and initializer sequences and corrects three skip spans, also preserving generated suffix instructions. The last ordinary body, m12_01 event 11210700, is now reconstructed exactly at the semantic level with named PvP arena mode, participant, prompt, watcher and gate initializers under strict source and target fingerprints. M12 event 0 restores exact object/SFX and skip changes; M13 base event 0 restores exact object/initializer removals and condition changes; M15 DLC event 0 restores exact object/initializer, skip and object/SFX changes; M14 DLC event 0 restores exact gate condition, object/SFX and initializer changes. They use full source/target fingerprints; M14 additionally accepts only the validated skip-count and entity-remap variants. DLC elevator state watchers 11210110, 11210120, 11210130 and 11210140 now receive their exact state guards and zero-frame delay under anchor/count guards; event 11210831 reconstructs its exact DLC message check and five parameter bindings; event 11210837 reconstructs its exact 16-instruction message watcher body; event 11210840 reconstructs its exact 30-instruction DLC event and 11 parameter bindings under a strict full-body/parameter guard; event 11210850 replaces its message block and reindexes eight bindings to five under a strict full-body/parameter guard; event 11210800 reconstructs its exact six-instruction body and reindexes four parameter bindings to three under a strict full-body/parameter guard; event 11210890 reconstructs its exact nine-instruction body and adds two parameter bindings under a strict full-body/parameter guard; event 11210849 reconstructs its exact activation and cleanup body under a strict full-body guard; event 11210900 removes an exact obsolete 11-instruction condition block while preserving its event parameter; event 11210886 reconstructs its exact message checks and six parameter bindings; event 11210839 now rebuilds its six-group multiplayer area watcher with named conditions and flag operations under full source/target semantic fingerprints. Common event 840 removes its exact eight flag conditions and reindexes seven parameter bindings to six under a strict full-body/parameter guard. The 23 explicit EndUnconditionally edits, ten conditional watcher prefixes, and 37 exact adjacent DeactivateObject plus DeleteMapSFX/SpawnMapSFX removals across 27 events have parser/diff verification showing only the expected operation changes. M11 event `11100000` now removes its exact object/SFX pair; event `11100710` reconstructs its exact progression body under a strict full-body guard; event 50 removes only the reference-omitted initializer for event 11105030; event-50 cleanup now removes 30 exact omitted initializer calls across 11 map archives; M13-2 event 0 removes its exact four-instruction omitted initializer block; M18 event 0 removes its exact multiplayer-state/object/SFX setup sequence; M11 event 0 removes exact Fog-omitted initializers and object/SFX operations while correcting the affected skip span under full source/target semantic fingerprints; M15 event 0 removes its exact multiplayer/object-SFX block and initializer calls and shortens the remaining skip span. M10 event 0 now restores its exact three initializer calls, removes omitted object/SFX and initializer operations, and adjusts its two affected skip operands under strict source/target semantic fingerprints. M12 DLC event 11210875 now reconstructs its complete multiplayer-area watcher body and clears its obsolete parameter table under exact source/target body and parameter checks. Event 11210100 now reconstructs its complete area-state watcher body under strict source/target semantic fingerprints. Events 11210830 and 11210870 now reconstruct their exact DLC area-enemy and guidance bodies; 11210870 restores all 31 ordered parameter records from its 115-instruction pristine body under strict source/target fingerprints; 11210435 restores its 72-instruction seven-state watcher and all 49 ordered parameter records from its pristine body under full semantic fingerprints. M18 DLC event 0 now applies its two exact flag replacements and removes the pinned object/SFX and initializer operations under full source/target fingerprints. M13 DLC event 0 removes its exact condition, object/SFX and initializer operations and changes one skip target under strict source/target fingerprints. M14 event 0 removes its exact gate, object/SFX and initializer operations and corrects one skip condition under strict source/target fingerprints. M10 Parish events `11010001` and `11010901` each remove their exact sequence of two object/SFX pairs; Depths event `11200000` removes its two exact object/SFX operations; M12 event `11200900` removes its exact two-pair object/SFX sequence; gate condition/object-state edits are reconstructed in `11005030`, `11005033`, `11015100` and `11015103`; `11005391` also gets the reference's leading common operation and condition conversion; `11005392` removes its exact two conditions and adjacent skip; `11010000` removes the Parish condition/skip block; and `m10_02` event 50 now reconstructs its exact event flag and initializer changes under full source/target semantic fingerprints; events `11020001`, transition `11020021` plus DLC event `11020899` are now reconstructed; event `11020001` removes its exact conditional skip block and related flag check; the latter removes the exact area conditions and reindexes both parameter bindings. M12 event `11200005` reconstructs its exact guarded body; event `11200006` reconstructs its exact progression body under a strict full-body guard; DLC event `11210401` reconstructs its exact region, delay, event-call and wait changes; event `11210404` removes three obsolete condition instructions, corrects the initial flag check and reindexes eight parameter bindings under a strict full-body/parameter guard; event `11210430` reconstructs its exact 15-instruction body and reindexes six parameter bindings to three under a strict full-body/parameter guard; event `11210701` reconstructs its exact 34-instruction progression body and remaps five condition parameters to nine under a strict full-body/parameter guard; event `11210434` reconstructs two exact area checks and removes the known obsolete condition block under a strict full-body guard; event `11210831` reconstructs its exact message check and five parameter bindings under a strict full-body/parameter guard; event `11210835` removes five duplicate checks and reindexes four parameters under a strict full-body/parameter guard; event `11210836` reconstructs its exact conditions and sound sequence under a strict full-body guard; event `11210837` reconstructs its exact 16-instruction DLC message watcher body under a strict full-body guard; event `11210840` reconstructs its exact 30-instruction body and 11 parameter bindings under a strict full-body/parameter guard; event `11210850` replaces its message block and reindexes eight bindings to five under a strict full-body/parameter guard; event `11210849` reconstructs the exact DLC event activation/cleanup body under a strict full-body guard; event `11210900` removes an exact obsolete 11-instruction condition block while preserving its event parameter; DLC events `11210000`, `11210838`, `11210439`, `11210407`, `11210878` and `11210402` remove exact operation/condition sequences; M12 event `11200900` removes its two object/SFX pairs; events `11215007` and `11215009` prepend the exact missing map condition. M13 event 50 removes its two exact obsolete initializer calls, and event `11305000` reconstructs its exact watcher and cleanup body under a strict full-body guard; M13 DLC event 50 removes only the reference-omitted initializer for event 11315030; events `11310052`, `11315393`, `11315394` and `11315398` now reconstruct their exact flag-state, delay, area-check and object-operation changes; the latter three use full-body guards because their changes affect control flow or object operations. M10 event `11005150` replaces its exact 15-instruction prefix with the reference skip, removes two exact flag writes, and remaps ten parameter bindings under full source/target semantic fingerprints while preserving the reference skip operand; M17 event `11700000` now replaces its exact first object/SFX pair with an unconditional end and removes its second exact pair; M14 events `11400001`, `11410410` and `11410901` each remove two exact adjacent object/SFX pairs; M14 DLC event `11410001` restores the gate-control body and removes two obsolete waits under an exact full-body guard; `11400200` reconstructs the operation removal and `11415373` one-frame wait, while `11415380` accepts only its exact validated FogMod runtime form. M15 DLC event `11510001` removes its exact three-pair object/SFX sequence; event `11510900` reconstructs its exact delay and cleanup body under a strict full-body guard; event `11500210` reconstructs the exact Demon Ruins progression watcher body under a strict full-body guard; events `11510502` and `11510240` reconstruct the exact parameterized flag-write removal and gate-area transition respectively; event `11510543` removes six exact adjacent sound-effect pairs and reindexes four parameter bindings under a strict full-body/parameter guard; `11515381` reconstructs two exact operation substitutions under a full-body guard; the former reindexes its following parameter record. Common event `260` now removes its exact flag condition and reindexes both remaining parameter references. M18 DLC event `11815394` now restores the exact final flag write before its event end. The real-data FogHost parity test covers all 166 implemented event bodies and passes against the pinned reference; the full test executable also passes 143/143 tests, including deterministic writes across representative seeds and option profiles. Complete offline-root parity and network-blocked generation proof are outstanding. Production downloader, data root, generation version, Seed Code, and release remain unchanged; baseline stays 0.39.0 and 0.40.0 is not created or published.

# CURRENT TASK: BUILD-TIME NEXUS DISTRIBUTION (desktop 0.39.0; 2026-10-10)

Added an immutable `distribution: github | nexus` build field, independent from `channel: public | internal`. Valid legacy build-info without `distribution` remains GitHub; malformed metadata or an explicit invalid value disables updater behavior. GitHub builds retain stable-only public updates and internal prerelease QA. Nexus builds do not eagerly load the updater or GitHub API code, instantiate an updater, configure a feed, or schedule update checks. Update IPC remains local and unavailable; the EN/PL Updates card shows the installed version and Nexus guidance, with no check/download/install controls. `autoCheck` is retained without being rewritten. A future Nexus link can be provided through validated `DS1TR_NEXUS_PAGE_URL`; none is configured currently.

Both packages built locally on Electron 44.7.0. GitHub uses the existing public unsigned release flow and canonical artifact names. Nexus uses `apps/desktop/release/nexus/`, separate `Nexus-Setup` / `Nexus-Portable` artifact names, and no `app-update.yml` or `latest.yml`. The packaged verifier inspected `app.asar` and confirmed its Nexus main entry has no eager updater or GitHub API imports. Nexus files remain local and were not uploaded. The variants retain the same app ID, package identity, user-data path, and updater dependency; Fog data networking, generation, Seed Code, and signing are unchanged.

**Verification:** root typecheck and desktop tests 122/122 passed. All 51 golden compatibility cases passed against the clean QA game copy. Release workflow [38051084653](https://github.com/avoydan/DS1-Multi-Randomizer/actions/runs/38051084653) passed backend tests, typecheck, desktop tests, public unsigned NSIS/Portable packaging, release verification, asset staging, and draft creation. Published [v0.39.0 as a public prerelease](https://github.com/avoydan/DS1-Multi-Randomizer/releases/tag/v0.39.0); GitHub API reports `draft=false`, `prerelease=true`, and the tag resolves to `feb0499c271cbb665c969e33366125f47600fe61`. Downloaded all six assets from the published tag; API SHA-256 digests and sizes match, all five `SHA256SUMS.txt` entries pass, `latest.yml` matches the Setup installer SHA-512, and the manifest matches the release commit, repository, and artifact hashes. Local Nexus Setup/Portable packages and manifest are verified and remain unpublished; the packaged verifier confirms Nexus has no updater imports or `app-update.yml` / `latest.yml`. No signing or Nexus upload occurred.

**PUBLIC PRERELEASE 0.38.4 AND ELECTRON BASELINE (2026-10-10; release commit `baffdb4e90aca2b20fd03ff69981da13aa0a1e68`)** - Packaged Electron upgraded from 34.5.8 to 44.7.0; `electron-updater` 6.8.9 and `electron-builder` 26.15.3 remain unchanged. The game-folder dialog now passes an explicit start path and remembers the visited parent for Electron 43 behavior; the release inventory includes Electron 44's `dxcompiler.dll` and `dxil.dll`. Published [v0.38.4 as a public prerelease](https://github.com/avoydan/DS1-Multi-Randomizer/releases/tag/v0.38.4); GitHub reports `draft=false`, `prerelease=true`, and tag `v0.38.4` points to the release commit. All six assets are uploaded; GitHub API SHA-256 digests agree with all five entries in `SHA256SUMS.txt`, and the downloaded sums/manifest/latest.yml match their API digests. The manifest records Electron 44.7.0, public unsigned mode, `avoydan/DS1-Multi-Randomizer`, the exact commit, and a clean workflow tree. `latest.yml` names the 0.38.4 Setup installer and its size/hash; the release verifier checked it against the packaged installer. The README download CTA points to v0.38.4. **Verification:** workflow [38039032950](https://github.com/avoydan/DS1-Multi-Randomizer/actions/runs/38039032950) passed backend tests, root typecheck, desktop tests, public unsigned NSIS/Portable packaging, release verification and asset staging; local desktop tests 115/115 and all 51 golden fixtures passed. The local full backend test invocation had one sandbox-specific `os.userInfo()` `ENOMEM` failure in `supportBundle.test.mjs`; excluding that environment-dependent file yielded 577 pass / 2 skipped, and the full GitHub-hosted backend step passed. Packaged GUI smoke could not complete because the Windows AppContainer sandbox denied access under workspace/temp ACLs; the sandbox was not disabled. The packaged Electron executable successfully ran its existing `ELECTRON_RUN_AS_NODE=1` backend path as Electron 44.7.0 / Node 24.21.0. Lockfile audit went from 14 advisories (2 critical, 4 high, 8 moderate) to 17 (2 critical, 12 high, 3 moderate); production-only audit is 0 and no Electron runtime advisory remains. Remaining findings are in build/development tools and were not bulk-updated. SignPath status is unchanged; all release artifacts remain unsigned.

**PUBLIC PRERELEASE 0.38.3 AND TRUST PRECHECK (2026-10-10; app commits `6107f604f542e4419262ca6275c5123f5141990a`, `f829c2d0ee6cafe1cf88f6e068332c4c2c60d184`)** - Fixed the public updater's no-stable-release case while keeping public prerelease checks disabled; genuine network/API failures remain errors and internal QA still opts into prereleases. Added a narrow Electron popup/navigation guard for the one external license link. Published [v0.38.3 as a public prerelease](https://github.com/avoydan/DS1-Multi-Randomizer/releases/tag/v0.38.3); GitHub reports `draft=false`, `prerelease=true`, and the tag points to `f829c2d`. The six assets match GitHub SHA-256 digests and `SHA256SUMS.txt`; the release manifest and `latest.yml` match version, source repository, payload hashes and provenance. Old-owner asset URLs return HTTP 200 after redirect. **Verification:** release workflow [38008781155](https://github.com/avoydan/DS1-Multi-Randomizer/actions/runs/38008781155) passed backend tests, desktop typecheck/tests, unsigned packaging, release verification and draft staging; desktop tests 113/113, typecheck/build and all 51 golden fixtures passed locally. Local backend run had one environment-only `os.userInfo()` `ENOMEM` failure; the hosted workflow backend suite passed (585). A focused dependency audit recorded 14 advisories including unsupported Electron 34.5.8; the popup/navigation exposure was fixed, while the Electron major upgrade remains an explicit residual trust limitation in `docs/RELEASE_TRUST.md`. SignPath preflight and Nexus page materials are prepared; no SignPath application/terms acceptance, Nexus upload, or antivirus submission was made. An isolated internal 0.38.0 install is prepared for the published 0.38.3 update test, but the actual UI download/restart has not been run.

**GITHUB OWNER MIGRATION / PUBLIC PRERELEASE 0.38.2 (2026-10-10; app commit `f546d4eea0668d60c0868910a7b7760224d1dd57`)** - The active updater source and electron-builder publish target now use `avoydan/DS1-Multi-Randomizer`; README links directly to [v0.38.2](https://github.com/avoydan/DS1-Multi-Randomizer/releases/tag/v0.38.2). GitHub reports `draft=false`, `prerelease=true`; the tag points to this app commit. The release workflow [38004245414](https://github.com/avoydan/DS1-Multi-Randomizer/actions/runs/38004245414) passed backend tests, typecheck, desktop tests, public packaging, publication verification and asset staging; its default `GITHUB_TOKEN` created the draft under the new owner. **Verification:** local backend tests 585 passed / 2 skipped, desktop tests 104/104, desktop typecheck, public package and release verifier passed; all 51 golden fixtures passed on the clean game copy. Downloaded all six public assets and matched their digests against GitHub API; the five payload files also match `SHA256SUMS.txt`. The manifest records version 0.38.2, public channel, new update owner/repository, clean tree and the tagged commit. Public updates remain stable-only; internal QA prerelease opt-in is unchanged. Generation logic and Seed Code compatibility are unchanged.

**PUBLIC UPDATER NO-STABLE STATE (2026-10-10; desktop 0.38.1; app commit `040f07171266408f16ab563d967cd8324c331df0`)** - A public check now treats only electron-updater's wrapped 404 for the configured GitHub `/releases/latest` endpoint, with at least one Atom release entry, as “no stable release”. The app shows a translated informational state, records the check as successful and keeps public prereleases disabled. Network/API errors remain errors; the internal prerelease opt-in is unchanged. **Verification:** desktop build, typecheck, tests 104/104, local unsigned publication verifier, and all 51 golden fixtures passed. GitHub Actions [37995727832](https://github.com/remohomer/DS1-Multi-Randomizer/actions/runs/37995727832) passed backend tests, typecheck, desktop tests, public unsigned packaging, release verification and asset staging. Published [v0.38.1 as a public prerelease](https://github.com/remohomer/DS1-Multi-Randomizer/releases/tag/v0.38.1), targeting the app commit; all six release assets are uploaded. `/releases/latest` still returns 404 because no stable release exists, as expected. An isolated internal 0.38.0 setup build from the exact v0.38.0 commit is prepared at `C:\Users\Adam\ds1tr-qa\update-test-0.38.0-to-0.38.1`; its launcher keeps QA app data separate. **Installed N→N+1 UI test is prepared but not yet run.**

**0.38.0 PUBLIC PRERELEASE (2026-10-09; desktop 0.38.0, backend 0.46.0, contracts 0.33.0)** - The UI reorganization from commit `2b2f377` and internal-only prerelease updater gate are included in commit `1cda4f51e38a8485af7c520caa54342fd4ed3f4a`, fast-forward pushed to `main`. Clean DSR source verified at `C:\Users\Adam\AppData\Local\Temp\ds1tr-clean-game\DARK SOULS REMASTERED`; four local copies had the same 199-file structure and matching executable/GameParam hashes, and none were under `steamapps`. The first golden attempt exposed a stale local FogHost helper; rebuilding from the committed source with `C:\Users\Adam\dn8sdk\dotnet.exe` fixed it. **Verification:** full `goldenCompatibility.mjs verify` passed; release workflow [37987526637](https://github.com/remohomer/DS1-Multi-Randomizer/actions/runs/37987526637) passed backend tests, typecheck, desktop tests, public unsigned packaging, release verifier and asset staging. Published [v0.38.0 as a GitHub prerelease](https://github.com/remohomer/DS1-Multi-Randomizer/releases/tag/v0.38.0), not stable/latest. Its six assets are Setup, Setup blockmap, Portable, `latest.yml`, release manifest and `SHA256SUMS.txt`; downloaded hashes matched the sums file and GitHub asset digests. The manifest records the exact release commit, a clean workflow tree, public channel and unsigned status. Public updater builds still ignore prereleases; internal builds opt in. **N to N+1 installed update E2E remains untested:** v0.37.0 predates the internal QA opt-in and v0.38.0 is a prerelease of the same app version, so there is not yet a distinct internal N build followed by a published N+1.

**SETTINGS UX ORGANIZATION (2026-10-09; desktop 0.38.0, contracts 0.33.0)** - Advanced mode has a clearer `combat` panel with subsections for regular enemies, regular world slots/roaming bosses, and arena bosses. Enemy-only legacy options sit with regular enemies; Gargoyle/Gwyn options sit with arena bosses. Toggle rows use two columns above 960 CSS px; selects, ranges and the pool editor span the panel; smaller windows use one column. Dependency reasons include a direct jump to their enabling setting. EN/PL help remains covered; setting IDs, values, Seed Codes, presets and generation are unchanged. **Verification:** focused UI/localization tests 35/35, desktop typecheck and production build passed. The full desktop suite was 96/97: its release-verifier test reads stale local 0.37.0 package artifacts after the version bump to 0.38.0. **Visual check:** not completed; the Vite dev server could not read its parent directory outside the permitted workspace. The CSS breakpoint and full-width behavior are covered by a static UI test.

**RELEASE ARTIFACT NAMING (2026-10-09; commit `26f0066`; desktop 0.37.0, backend 0.46.0, contracts 0.32.1)** - canonical files are `DS1-Multi-Randomizer-X.Y.Z-Setup-x64.exe` and `DS1-Multi-Randomizer-X.Y.Z-Portable-x64.exe`. The local 0.37.0 build emitted both names, `latest.yml` points at the Setup filename and its size/SHA-512, the matching blockmap exists, and the release manifest records the installer/portable SHA-256 values. `release-verify` returned OK in development mode; focused release checks passed **20/20**. Release notes point users to the generated `SHA256SUMS.txt` instead of embedding stale checksums. This package is internal-channel and is not a publishable release artifact. GitHub could not be rechecked from the takeover environment (Git transport had no credentials and public API requests failed); the cached `origin/main` ref points to `26f0066`, which is not proof of live remote state. No remote assets were changed and nothing was published. The pre-existing GitHub release notes below are historical until remote state is verified. Packaging-only change; no generation or golden-fixture impact.

**FIRST GITHUB DRAFT RELEASE (state previously verified 2026-10-09; live state not rechecked during takeover; desktop 0.37.0, backend 0.46.0 unchanged; gameplay and goldens unchanged; NOT published at that verification)** - repository `github.com/remohomer/DS1-Multi-Randomizer` (public, `main`), update source `remohomer/DS1-Multi-Randomizer` in `updateSource.ts` and `electron-builder.yml`. The workflow `Release (draft)` created the DRAFT `v0.37.0` (installer, blockmap, portable, `latest.yml`, manifest, SHA256SUMS) with the default `GITHUB_TOKEN` after a first run failed on a runner-only PowerShell issue (fixed, `d29c1c7`). GITHUB VERIFIED at that time: workflow, draft state, assets; downloaded assets verified (latest.yml vs installer, blockmap size, hashes, app-update.yml owner/repo, product name, release-verify OK). **N to N+1 update test: DEFERRED** (procedure in `docs/AUTO_UPDATE.md`, section 13). Remaining manual step at that time: review the draft and press Publish release; confirm live state before taking a release action.

**GITHUB RELEASES AND AUTO-UPDATE (2026-10-09; desktop 0.37.0, backend 0.46.0, contracts 0.32.1; generation profile `ds1tr-gen-v1` and every golden fixture unchanged; builds UNSIGNED)** - design, limits, owner setup and QA: `docs/AUTO_UPDATE.md`; per-release steps: `docs/RELEASE_CHECKLIST.md`.
- **What it is.** electron-updater 6.8.9 (exact; electron-builder stays 26.15.3, no upgrade needed) in the Electron main process only; the renderer gets six narrow IPC calls (state, check, download, install, openReleasePage, setAutoCheck) and a state event. Source: explicit `provider: github, owner, repo` in `electron/updateSource.ts` and `electron-builder.yml` (`avoydan/DS1-Multi-Randomizer`; a build with the placeholder owner `REPLACE_WITH_GITHUB_OWNER` reports "updates unavailable" and cannot be published). Installed NSIS = download in app, user clicks "Restart and update"; portable = detects a newer release and opens the release page. Stable clients ignore prereleases and drafts; never a downgrade; no silent install; no token anywhere.
- **Safety.** The backend counts running mutating requests (`GET /api/activity`, `core/activity.ts`); Restart and update is refused while a generation, apply or restore runs (or when the state is unknown). An update never touches the game, the applied world or the user data.
- **Release automation.** `.github/workflows/release.yml`: manual only, creates a DRAFT release (installer, blockmap, portable, `latest.yml`, manifest, SHA256SUMS) from one build, after backend/desktop tests, typecheck, packaging and `release-verify --publication --tag`; the tag/requested version must equal the packaged version; the golden fixtures cannot run on a hosted runner, so the workflow demands the commit hash the owner verified locally. `GITHUB_TOKEN` (`contents: write` on the one job) is enough in a single repository.
- **Packaging fix found on the way.** The first package did not contain electron-updater (a root `node_modules` is never walked by electron-builder and `beforeBuild` returns false); the closure is now staged into `dist-electron/node_modules` and `release-verify` fails any package whose asar lacks it.
- **Verification.** AUTOMATED: `electron/updater.test.mjs` (26), `tools/release/update-lib.test.mjs` (8), backend `activity.test.mjs` and the integration test, locales parity. PACKAGED VERIFIED: the installer, blockmap, `latest.yml`, `app-update.yml`, the unpacked app starting and answering `unsupported/no-update-source` through the real bridge. **NOT verified:** a GitHub workflow run, a draft/published release, an installed N to N+1 update, the portable flow against a real release (all DEFERRED until a repository exists); not called production-ready for that reason.

**UNIT PARTICIPATION (G-03 / F-01) AND THE RENAME TO "DS1 Multi Randomizer" (2026-10-09; desktop 0.36.0, backend 0.45.6, contracts 0.32.1; generation profile `ds1tr-gen-v1` unchanged; builds UNSIGNED, nothing built)** - evidence: `docs/GENERATION_GUARD_AUDIT.md` section 12, `docs/ENEMY_PLACEMENT_MODEL.md` 10.11, `docs/GENERATION_COMPATIBILITY.md` "Recorded exception 5"
- **Part A (a real correctness bug).** The large regular enemies planned as whole units (dragon-class units, hydras, the Mass of Souls groups) ignored Replacement chance and "Nothing" (about 130 records, about 50 visible body changes per world at 0%). They now take part only when the mode is "Regular enemies only" or "Bosses or regular enemies" and the first draw of the root slot is within the chance (the draw the sampler and the roaming pass already make: no new stream). At 0%, at "Nothing" and under "Bosses only" no unit is exchanged; at 100% with the two ordinary modes nothing changed. AUTOMATED VERIFIED (`enemyUnitParticipation.test.mjs`, 10 tests); REAL PIPELINE VERIFIED (`placementMatrix.mjs unit-participation`: 172/172, with and without Fog, 0 retries, 0 overruns, 0 non-deterministic reruns). One defect found on the way and fixed: a subset made the Parish bonfire Titanite Demon's think-row id collide with a boss think row.
- **Compatibility.** 12 of 51 golden fixtures change (all with a chance below 100), re-recorded in seven files; 39 verify as before. Seed Codes, presets and the profile id are unchanged.
- **Help.** "Nothing" and Replacement chance now say that the large regular enemies follow them (EN, PL); the limits of "Bosses only" name the units with linked parts.
- **Part B (rename, user-facing only).** The product is "DS1 Multi Randomizer" (it was "DS1 Triple Randomizer"). Updated: window title, sidebar heading, About, diagnostics and support-bundle headings, hint sheet, error messages, installer/shortcut/uninstaller names, release policy, Windows file properties of the helper programs, README and the current-state documents. Retained on purpose: the Seed Code prefix `DS1TR1-`, `ds1tr-gen-v1`, `appId com.ds1tr.desktop`, the package name `ds1-triple-randomizer-desktop` (Electron derives the user data folder from it: settings, history, presets and state stay where they are), the seed-derivation namespace, config keys, schema identifiers, historical documents, source-file modification notices. `branding.test.mjs` guards the active surfaces. The icon is unchanged. Expected future artifacts: `DS1-Multi-Randomizer-Setup-0.36.0.exe` and `DS1-Multi-Randomizer-0.36.0-x64.exe`. PACKAGED BEHAVIOUR UNVERIFIED until the next build.

**GENERATION GUARD AUDIT (2026-10-08; no code or version change: desktop 0.35.1, backend 0.45.5, contracts 0.32.0; generation profile `ds1tr-gen-v1` unchanged)** - full evidence and the registry: `docs/GENERATION_GUARD_AUDIT.md`
- **Verdict: the cap of ten was not isolated, and no other fixed count of bosses or events remains.** 55 guards were registered (code, history, docs, legacy `83f008d`): 15 correctness requirements, 9 product rules, 7 legacy requirements, 7 safe-subset deviations, 5 real engine constraints, 2 temporary guards already removed (the cap and the dragon-plan reservation), **1 temporary guard still active, 1 obsolete conservatism, 8 suspicious**. Every bounded search ends in an explicit, typed error; no retry exhaustion leaves content vanilla; there is no candidate-pool truncation and no category or fit fallback (the placeholders of the fallback ladder are never produced).
- **Still open (reported, nothing fixed, no golden or Seed Code touched):** the unit planners ignore Replacement chance and "Nothing" (127 placement records, about 80 visible enemies, per world at 0%; the help says they are not replaced); `HOSTILE_NPC_MAP_CAP = 12` (the sibling of the removed cap: 19.5 instead of 49.2 hostile NPCs per world with Type replacement); the model budget counts every model a map holds while the legacy counts only new ones (about 21 against 30 new models per map, Blighttown 25 against 50); Easy Asylum + Bosses only (the legacy puts Capra, Taurus or the Bell Gargoyles in twelve Asylum slots, DS1TR leaves the Asylum); Pinwheel Chaos removes Pinwheel from the roaming pool; 157 destinations per world never take a boss with no recorded boss-specific reason. Follow-ups F-01 to F-08 in the document. Two sentences of the help committed in 77227da are wrong for these reasons ("rarely matters" under the curve modes; "Nothing" and the Titanite/Drake line while the units move): F-08, not changed here. The earlier statement that Easy Asylum + Bosses only is intended was incomplete (G-12b).
- **Added:** `generationGuards.test.mjs` (a new numeric limit or bounded search in the engine fails until it is registered; the samplers never downgrade a category or relax fit over 64 mode combinations) and `apps/backend/scripts/guardAuditProbe.mjs` (the measurements). Nothing was built, pushed or published; the live installation was not touched.

**BOSSES ONLY AUDIT AND FIX 0.35.1 (2026-10-08; desktop 0.35.1, backend 0.45.5, contracts 0.32.0; generation profile `ds1tr-gen-v1` unchanged; builds UNSIGNED)** - full evidence: `docs/BOSSES_ONLY_AUDIT.md`
- **Root cause (a real gameplay bug).** With Bosses only at 100% about one ordinary enemy in ten changed. (1) A fixed cap of **10 roaming bosses per map** (`ROAMING_BOSS_MAP_CAP`, Part E1, `387f155`) stopped the planner: of 1,702 destinations 170 got a boss, 1,351 were counted as left by the cap. It was a guardrail, not a game constraint; the legacy Enemy Randomizer `83f008d` has no cap (its only limit is the number of DISTINCT models per map, `MAX_UNIQUE` 30, after which bosses already in the map are reused; a slot that finds no boss stays as it is, never an ordinary enemy with Bosses only). (2) The dragon plan exchanged the ordinary dragon-class enemies (Titanite Demons, Drakes, the Bounding Demons) for each other whenever Enemy randomization was on, which put an ordinary enemy in an ordinary slot under Bosses only (60 placements in the owner's world) and reserved those slots against bosses. (3) Easy Asylum keeps the 26 eligible Asylum destinations as they are (intended, local).
- **Fix.** The cap is removed (the model budget counts distinct models only, so 189 bosses in a map use at most 16 models); the dragon-class ordinary slots without linked parts are destinations of the roaming pass (the bridge Hellkite, the Anor Londo gargoyles and the hydras stay with the dragon plan); per-map diagnostics (destinations, protected by reason, selected, boss, normal draw, unchanged by reason, distinct boss models) go into the run manifest. The help of Replace regular enemies with, Replacement chance, Size of replacements and Difficulty matching now says exactly this (EN and PL).
- **Verification.** AUTOMATED: `enemyBossesOnly.test.mjs` (the matrix A-G: 100% with and without Easy Asylum, 50%, both at 0% and 100%, regular enemies only, restrictive fit; stress 24/60/150/260 destinations in a map = 20+/50+/100+/200+ bosses; stay dead on and off; dragon-class slots), `enemyRoamingBosses.test.mjs` updated, `roamingSweep.mjs` on the real catalog (18 configurations x 20 seeds, all invariants, including the non-worsening model budget and determinism). REAL PIPELINE VERIFIED on a safe game copy (real data tool, FogHost, owner-file audit): 16 worlds without and 12 with the world randomizer, 28/28, 0 failures, 0 event-window overruns, 0 non-deterministic reruns, 1,559 roaming bosses in a world, up to 189 in a map (207 events, all unique), no `FOG_MERGE_CONFLICT`.
- **Compatibility.** Only sampling-v1 worlds with Bosses only or Bosses or regular enemies can change (the E1 modes, pre-public); Seed Code format, presets and the profile id are unchanged. **4 of 51 fixtures change** (`roaming-both-combined`, `roaming-bosses-souls-stay-dead`, `roaming-both-boss-sampling-fog`, `triple-modes`), re-recorded in their own files with every other case identical (seed code and digest); the other 47 verify byte for byte before and after (`GENERATION_COMPATIBILITY.md`, "Recorded exception 4").
- **GAMEPLAY UNVERIFIED.** Wake-up, navigation, killability, souls and stay-dead behavior of this many bosses were never played. The one-minute QA check ("Every enemy is a boss", a Seed Code and the exact settings) is in `docs/FINAL_GAMEPLAY_QA.md`.

**EARLY-OPTIONS AUDIT (2026-10-08; no code or version change: desktop 0.35.0, backend 0.45.4, contracts 0.32.0; generation profile `ds1tr-gen-v1` unchanged)**
- **"Guarantee early weapon" and "Balanced early loot" are LEGACY DEAD / NO-OP, and were never lost in a port** (`docs/EARLY_OPTIONS_AUDIT.md`). LEGACY CODE VERIFIED: neither the Item Randomizer `f7cabbd` (complete option set read from `randomizer_gui.py` / `randomizer_options.py` / README) nor the Enemy Randomizer `83f008d` has such an option. The flags come from the vendored engine `f120efe`, whose only implementation (`randomizeItems`, vendored `generator.js:3359-3453`) works on its eight-item, eight-location placeholder data set and is unreachable in DS1TR (`combinedRandomizerBridge.ts:492` refuses a non-extracted result); with real game data the real-data function (`randomizeExtractedItemLots`) never reads them, in upstream's own product too. The 2026-10-05 parity audit that marked them FULL was wrong; corrected in `FEATURE_PARITY.md`.
- **Nothing was restored or changed** (the condition "legacy proves meaningful behavior DS1TR lacks" is not met). Both flags default to true everywhere (engine, `DEFAULT_GENERATION_SETTINGS`, canonical Seed Code text, presets, all 51 golden fixtures), so making them functional would change every world and need a new generation profile; a new early-game guarantee would be a new feature, not a port. Controls stay hidden, fields stay in presets and Seed Codes. Seed Codes, presets and goldens: unchanged (no fixture touched).
- **Evidence.** AUTOMATED: `apps/backend/src/adapters/combinedEarlyOptions.test.mjs` (all four combinations produce identical placements and hash on real-data-shaped catalogs for several seeds and pool combinations; the flags are read only inside the placeholder function); the real pipeline (`settingEffectReal.mjs`, safe game copy): early weapon, balanced early loot and both: SAME game files, the two controls (scaling vanilla vs area, progression logic) DIFFERENT as they must.

**UI / UX PRODUCT CLEANUP 0.35.0 (2026-10-08; desktop 0.35.0, backend 0.45.4, contracts 0.32.0; generation profile `ds1tr-gen-v1` unchanged; builds UNSIGNED; CORE FEATURE FREEZE kept)**
- **What changed.** One settings registry (`packages/contracts/src/settingsRegistry.ts`) drives the whole settings screen; Basic and Advanced are two views of one configuration (the Basic view never rewrites an advanced value and says "Custom advanced settings are active." / "Aktywne są niestandardowe ustawienia zaawansowane."); every one of the 59 drawn options has contextual help (`?`: tooltip on hover / focus, full panel on click or Enter, every choice of a dropdown explained, usable on disabled options) in English and Polish; every disabled option shows why (19 reasons; Starting weapon types: "Włącz „Wyposażenie startowe”, aby zmienić tę opcję."); new sidebar (Your game, Presets, Seed Code, Import / Export, App settings, About); terminology rewritten in EN and PL (`docs/UI_ARCHITECTURE.md`, table); the button is "Generate world" / "Generuj świat"; the withdrawn header brand mark is gone; new application icon (burning sword, ICO 16-256 px); build channel `public | internal` (public hides the internal test worlds). Full description: `docs/UI_ARCHITECTURE.md`; every option: `docs/SETTINGS_INVENTORY.md` (generated, tested for freshness).
- **Options that do nothing are not drawn.** "Guarantee early weapon" and "Balanced early loot" are read only by the older prototype path; the real pipeline writes identical files with them on and off (`apps/backend/scripts/settingEffectReal.mjs`). The fields stay in presets and Seed Codes; the documentation was corrected (`FEATURE_PARITY.md`, `OPTION_MATRIX.md`). "Progressive" enemy scaling produces the same files as "By area"; its help says so.
- **Correction of an earlier claim (DLC, backend 0.45.3).** The earlier DLC audits reported every DLC row "identical"; the verifier skipped the drop-chance cells (`lotItemBasePoint`), so with the DLC off and "Guaranteed enemy drops" on the DLC's own drop tables were still scaled. Found with `dlcVerifierControl.mjs` (16 problems), fixed (fork patch 0026, `dlcExcludedDropLots`), and the verifier now compares the chance cells. **365 DLC-off real-pipeline worlds with guaranteed drops: 0 failures.** The unreleased `ds1tr-gen-v1.dlc-off.json` was recorded a third time (two of four worlds changed); the 47 DLC-on fixtures are byte for byte unchanged (`GENERATION_COMPATIBILITY.md`, "Recorded exception 3").
- **Verification.** AUTOMATED: backend tests, desktop tests (57 / 57 in the clean clone), typecheck, locales parity, help coverage **59 / 59** options (15 selects with every choice, both languages), disabled-state audit (145,140 states over a deterministic walk: every disabled option has a reason written in both languages, all 19 reasons reached), Basic never changes an advanced value, language / view / help change nothing, inventory freshness, goldens: **51 / 51 PASS** on the final commit `92f99c1` (47 DLC-on fixtures byte for byte unchanged, 4 DLC-off after the third recording); backend tests on `92f99c1`: **553 pass, 0 fail, 1 skipped**. VISUAL + AUTOMATED in the packaged **public-channel** portable and installed builds on **safe game copies** (driven over the DevTools protocol: 29 / 29 behavioural checks; window menu `HMENU = 0`; the internal test worlds are absent in the public build; PL, EN and Auto; Basic / Advanced; values preserved across switching view and language; keyboard help: focus shows the tooltip, Enter opens the panel, Escape closes it; every control has an accessible name): a golden Seed Code with fog (`items-enemies-bosses-fog`, portable), a DLC-off one (`dlc-off-items`, portable) and an older one (`enemies-sampling-v1`, installed) were loaded through the Seed Code view, generated, applied and restored on the safe copies, and the package digests equal the golden digests (the spoiler file, an output preference, excluded); preset export and import; run history; hint sheet. The new icon is on the portable, the installer, the installed exe and both shortcuts. Screenshots (PL / EN, Basic / Advanced with help, 1366x768 at 100 / 125 / 150 %, 1600x900, 1920x1080; no horizontal overflow at any of them): `C:\Users\Adam\ds1tr-qa\shots-0.35.0\`.
- **Not verified / limits.** The optional shortcut Ctrl+Shift+Alt+F12 is covered by a unit test of the key predicate and by reading the wiring; synthetic keystrokes did not reach the window in this environment, so the real keyboard path is for the owner to try once. Exporting a support bundle from the UI opens a native save dialog and was not driven (the backend side is covered by tests). The uninstaller of the installed QA copy was refused by Windows ("Odmowa dostępu", unsigned file), so its shortcuts and registry entry were removed by hand and `C:\Users\Adam\ds1tr-qa\installed-3` was left on disk (as in 0.34.2). No signing certificate: UNSIGNED. The differences from the mockup are listed in `UI_ARCHITECTURE.md`.
- **Incident to report.** Three windows of mine auto-discovered the LIVE Steam installation and READ it (validation, state check, copying the managed folders into the test profile's own state folder): the first dev window, and two packaged runs whose test `settings.json` was invalid (a UTF-8 BOM, then a lost backslash escape) so the saved safe-copy path was ignored. No generate / apply / restore request was made by those sessions, and the dev backend log shows no request; the live folder's files were re-created at 17:32:37 by something else (their modification times are the original install time, their creation time is 17:32), which this session cannot attribute (it had restored nothing). Every later run used a validated settings file pointing at a safe copy.
- **Artifacts (INTERNAL candidate, UNSIGNED, public channel, built from a clean clone at commit `92f99c1`).** `C:\Users\Adam\ds1tr-qa\artifacts-0.35.0\`: installer `DS1-Triple-Randomizer-Setup-0.35.0.exe` 128,575,915 bytes SHA-256 `4b2db86d43bd6cd83edbe689c702bea80f850ebe18c7ca91d6603246b444e7c7`; portable `DS1-Triple-Randomizer-0.35.0-x64.exe` 127,939,067 bytes SHA-256 `9ee87fb060aeed9071ebd19dd0d28eb681887e1905db0a6c93736b377b4a1b80`. Nothing was pushed or published.

**RELEASE CANDIDATE 0.34.2 - DLC switch, final UX / configuration audit, QA plan (2026-10-08; desktop 0.34.2, backend 0.45.2, contracts 0.31.2, FogHost project 0.6.0 with HostVersion 0.5.0, fork patch 0025; generation profile `ds1tr-gen-v1` unchanged; builds UNSIGNED; CORE FEATURE FREEZE)**
- **DLC content switch (`docs/DLC_AUDIT.md`, `docs/DECISIONS.md`).** `output.includeDlc` was a dead control; it is now real. ON (default) is exactly what every earlier world was. OFF leaves the DLC map (`m12_01_00_00`: its enemies, bosses, pickups, boss rewards, DLC-only enemy drops, DLC-exclusive starting equipment) out of the randomization as source and destination and never edits the DLC in the game. Done by filtering the catalog before `core.generate` (engine unchanged; fork patch 0025 adds one optional scan tag `dlcOnly`). **Fog randomizer + DLC OFF is UNSUPPORTED** (FogMod always contains the DLC areas): validation refuses it, the interface locks the switch on while the fog randomizer is on. Seed Codes: the field already existed (always `true`), so every earlier code, preset and history entry means DLC on and keeps its text; no new profile. EN "DLC content" / PL "Zawartość DLC"; DLC chip and a world-summary line only for a world without the DLC; test world "DLC content left out - items, enemies and bosses".
- **Golden fixtures (AUTOMATED VERIFIED).** All **47 / 47** earlier fixtures regenerated byte for byte with the DLC changes in place (run before any fixture was recorded) and again, together with the four DLC-off fixtures, at the end (**51 / 51 PASS**). `ds1tr-gen-v1.dlc-off.json` was recorded twice (an unreleased file): the second recording follows the fix below; two of its four worlds changed, the 47 others never did (`GENERATION_COMPATIBILITY.md`, "Recorded exception 3"). In the **installed final release candidate** the Seed Codes of two DLC-off fixtures (`dlc-off-triple-without-fog`, `dlc-off-items`) and one DLC-on fog fixture (`items-enemies-bosses-fog`) were loaded and their package digests are identical to the fixtures (spoiler log off; with it on the only difference is the extra `spoiler.txt`).
- **A gap the release-candidate audit found and closed (backend 0.45.2).** The first version of the exclusion treated every shop and gift as base-game, but the DLC has its own merchants (ShopLineupParam rows 1581-1584, 6410-6426, 6500-6507, 6600-6611) and NPC gifts (item lots 1510 and 1520, Hawkeye Gough and Elizabeth): with the DLC off and the shop or gift pools on they were still randomized, and neither the audit nor `dlc-untouched` could notice because both shared the same blind spot. They are now left out, `dlc-untouched` compares the shop rows and gift lots too, and `dlcVerifierControl.mjs` shows that the verifier FAILS on a DLC-on world (so the check can fail). Shop ownership is not in the scan: it is recognized by the DLC sell-condition flag area and the row ranges the DLC talk script opens, and gifts by their NPCs; every rule errs on the side of leaving a shop or gift as the game has it. Everything below was run after the fix.
- **DLC audit results (AUTOMATED VERIFIED).** Real pipeline (real data tool): **360 DLC-off worlds** (160 in `placementMatrix dlc-off`, 200 in `final-rc`, all run again after the fix) and the DLC-on controls: 0 failures, 0 retries, 0 non-deterministic reruns; SmokeVerify `dlc-untouched` compared **15,480 NpcParam rows of DLC-only enemies, 28,800 item lot rows** (DLC pickups, DLC-only enemy drops, DLC boss reward rows, DLC NPC gifts) **and 13,680 ShopLineupParam rows of the DLC merchants** of every package with the clean game: all identical, the DLC map files are never in a package. Engine level `dlcSweep` (real catalog, shops and gifts searched too): **810 DLC-off worlds**, 0 failures; the same searches find DLC content in 132 / 132 DLC-on controls, and the hint sheet built from a DLC-off plan names the DLC map in 0 hints (84 / 132 control worlds name it). In the final installed release candidate a DLC-off test world applied to a safe copy changed 51 files, none of them a DLC map file, and `dlc-untouched` passed (43 NpcParam rows, 80 lot rows, 38 shop rows identical); Restore returned the copy to the clean game (0 differences).
- **Final broad sweep (AUTOMATED VERIFIED).** *Engine level (real catalog, `defaultSweep`, `roamingSweep`, `bodySweep`, `npcSweep`, `itemModesSweep`, `dlcSweep`):* 500 default (sampling) + 500 classic, 1,800 E1 (bosses in regular slots), 1,800 E2 (normal enemies in boss slots), 1,500 hostile-NPC, 3,800 Race / Race + / difficulty / Laundromat / keys + fog, 810 DLC off = **10,710 worlds, 0 failures, 0 non-deterministic, 0 over the model limit**. *Real pipeline (`placementMatrix final-rc`, real data tool and FogHost, 14 cases x 100):* full Triple without Fog 200 (default and chaos), full Triple with Fog 200 (default and shuffled keys + Lords), Race 100, Race + 100, Very Unfair 100, keys + Lordvessel + Lord Souls 100, E1 100, E2 100, hostile NPCs 100, boss sampling 100, DLC off 200 = **1,400 worlds**: 0 retries, 0 event-window overruns, 0 non-deterministic reruns (two reruns of the first seeds of every case), 238,580 event-owner checks, 709,059 item lot rows, 473,859 placements, 499 key-progression checks by FogMod's own checker (7,983 keys placed), 7,600 Race destination checks, 0 failures of any audit. **Two worlds first failed with `DATA_TOOL_FAILED` ("the process cannot access the file ... .msb because it is being used by another process")**: a transient file lock: root cause UNIDENTIFIED (Windows Defender is not the active antivirus here; another security product is the likely holder, unproven). Both seeds passed alone, unchanged. A rare lock like this can show the player the "game data could not be processed, try again" message; the same Seed Code is deliberately not reseeded or retried automatically, so trying again gives the same world.
- **Final UX / product audit (`docs/FINAL_CONFIG_AUDIT.md`).** Scripted DOM audit of the packaged app (the 0.34.0 build; the two fixes below were verified again on the final installer and portable) in English and Polish (small text, clipped text, key leaks, untranslated leftovers, horizontal scroll, empty buttons) over READY, GENERATING, GENERATED, APPLYING, APPLIED, RESTORING, RESTORED, FAILED (invalid Seed Code, invalid game folder), Items / Enemies / Fog / Advanced / Settings preset / Run history and its row menu / Test worlds / Hint Sheet (low and full) / Diagnostics, Load seed while a world is applied and **Restore and create world**: **0 problems**. Defects found and fixed: a world made from untouched default settings showed "You changed the settings after this world was created" (key-order comparison; `isLoadedSeedModified` now compares by meaning), a permanently disabled "Protected items" placeholder was removed, an 8 px badge is 11 px, 15 failure codes got their own plain messages, 17 preset-problem sentences are translated. Saved game path / invalid saved path / moved install / manual browse / explicit path wins: the existing `gameDiscovery` and `gameSelection` tests, plus the invalid-folder flow in the packaged app (a bad folder is explained in plain language and never replaces the saved one). **DEFERRED:** a preview of the support bundle before saving (a second code path next to the allowlist/redaction/leak-scan path could disagree with it).
- **Configuration consistency (AUTOMATED VERIFIED).** `configConsistency.test.mjs` walks all 61 world-deciding settings: default, preset export, Seed Code round trip, interface control, English/Polish resources, neutral value; no dead, missing or ambiguous setting. Neutral settings never change an old world (DLC on, Laundromat off, hostile NPCs off, E1/E2 classic, Race off, Fair); the spoiler preference, the game path, the language, the hint level and the diagnostics are not part of a world.
- **Tests (AUTOMATED VERIFIED).** Clean clone of commit `2e0f88f`: backend **554 tests, 551 pass, 0 fail, 3 skipped** (need the real helpers / game), desktop **37 / 37**, root typecheck clean. New this phase: `dlcExclusion`, DLC cases in `configValidation` / `preset` / `seedCode` / `seedPersistence` / `generationPipeline`, `configConsistency`, `finalQaDoc`, the stale-notice test, the error-code, problem-text, DLC-string and 11 px tests in `locales.test.mjs`.
- **Release candidate (RELEASE CANDIDATE, UNSIGNED).** Built from a **clean clone of the committed tree** (`workingTreeClean: true`, commit `2e0f88fefe2367179b9ecf5e0b793bc32057d1db`; node_modules copied from the working tree, workspace links re-pointed), `npm run package:win`, `release-verify` OK in development mode, release manifest written. Installer `DS1-Triple-Randomizer-Setup-0.34.2.exe` 130,252,248 bytes, SHA-256 `8a4f556a9896e737f78d81227cccd6705e19241dd04a8c9f4d9f7af3aa54381a`; portable `DS1-Triple-Randomizer-0.34.2-x64.exe` 129,736,097 bytes, SHA-256 `33679b2f77149fd60c674e702bbc5dc070c6e10b89e1d523531502967c998c4c`. The earlier candidates 0.34.0 and 0.34.1 are superseded (0.34.1 fixed the stale-settings notice, 0.34.2 the DLC shops and gifts); commits after `2e0f88f` change documentation only. Signature state UNSIGNED, signing readiness unchanged (`--release` still refuses without a certificate). Windows Defender (checked on 0.34.1): **NO RESULT** (it is not the active antivirus on this machine; nothing was changed, nothing uploaded).
- **Installer, verified on the final artifacts (safe game copies only; the live Steam installation was never read or written).** 0.34.1: silent per-user install (exit 0, no elevation, `HKCU` uninstall entry only, no `HKLM`, no service, no task, Start menu and desktop shortcut), window title and icon, no Electron menu bar, default Triple generation, Apply / Restore, clean quit, **silent uninstall verified** (folder, shortcuts and registry entry removed, user data kept). 0.34.2 (final): silent install (exit 0), version 0.34.2, mode installed, English and Polish audit of the main states, the DLC switch, a DLC-off test world created, applied (DLC rows identical) and restored (0 differences), three golden worlds reproduced byte for byte, no stale notice, clean quit. **Windows answered "access denied" to the first launch of a freshly installed unsigned executable** (the app on 0.34.2, which started a few seconds later, and the 0.34.2 uninstaller): another security product on this machine is the likely cause, unproven; nothing about the system security was changed. **PARTIAL for 0.34.2:** its uninstaller could not be executed at all (denied for the file in place and for a copy, over several minutes), so the uninstall was verified on 0.34.1 only (the same NSIS mechanism). The shortcuts and the uninstall entry of that test install were removed by hand; its folder `C:UsersAdamds1tr-qainstalled-2` is left behind. **STATICALLY VERIFIED:** the English installer wizard (Windows here is pl-PL and the installer follows the Windows language; no safe override exists).
- **Portable, verified on the final artifact (0.34.2; the Auto language on 0.34.1).** Launch with a fresh English profile, mode `portable`, version 0.34.2, generation without a stale notice, hint sheet, Apply and Restore on a copy (0 differences), clean quit; Auto language resolved to Polish on this pl-PL Windows (0.34.1). **Observed, not reproduced:** during the 0.34.1 portable run the interface language changed from English to Polish about a minute after the start (`app.log`: "language set to pl"); none of the scripts touched the language control and a repeat on 0.34.2 with a fresh profile stayed English for the whole run. Most likely a manual click on the visible window; reported as unexplained, not as a defect. **PARTIAL:** exporting the support bundle goes through the native save dialog, which was not driven; the bundle builder is covered by `supportBundle.test.mjs` and was exercised on the previous packaged build.
- **Gameplay QA plan (`docs/FINAL_GAMEPLAY_QA.md`).** 17 quick smoke tests and 8 targeted extended tests under their exact titles in the app, the DLC checks, six full-playthrough worlds as Seed Codes (Recommended Triple, Chaotic Triple, Fog with shuffled keys, Race Mode, Race Mode +, DLC content left out; each generates twice identically through the real pipeline), estimates, what to look for, pass / fail, and which worlds the owner already played. **GAMEPLAY UNVERIFIED** for everything not marked: nothing was played for this phase.
- **CORE FEATURE FREEZE.** From this version only blocker, correctness, data-loss, serious UX and security fixes. Not started, by decision: Seed Hub, auto-update, accounts, telemetry, more languages, more Race parity, more E2 bosses, more Part F NPCs, new modes. **Deferred list:** Polish names of game content (game content names stay English, `I18N.md`), more languages, the full legacy difficulty tables, Race destination parity, unsupported Race + skips, the Purple Coward's Crystal skip, extended Laundromat Mixup, broader E2 / Part F coverage, the DLC off with the fog randomizer (needs a FogMod change), a support bundle preview, Seed Hub, accounts, leaderboards, Daily / Weekly seeds, auto-update (telemetry: none).

**Product UX / support / localization - language (English / Polish), Hint Sheet, diagnostics, support bundle (2026-10-08; backend 0.44.2, contracts 0.30.2, desktop 0.32.3, FogHost project 0.6.0 with HostVersion 0.5.0, patch 0024; generation profile `ds1tr-gen-v1` unchanged; no generation change)**
- **Language** (`docs/I18N.md`): i18next + react-i18next, bundled `locales/en|pl/<area>.json` (13 areas, English canonical, keys type-checked), Auto / English / Polski resolved by the main process before the first render (saved choice, else Windows language; unsupported -> English), persisted in `settings.json`, switched without a restart. Backend errors are stable codes (about 70 translated), plan lines carry keys, the 40 test worlds (quick + extended) and the checklist are translated, dates/numbers follow the language. **The language is not in the Seed Code, the settings or any hash**; the Fog "game language" stays a world setting, relabelled *In-game text language*. Installer: `nsis.installerLanguages [en_US, pl_PL]`.
- **Hint sheet** (`docs/HINT_SHEET.md`): `hints.json` written once next to the run (never in the package); three levels (Low spoilers / Progression / Full spoilers, names only in the resources) as one pure filter; viewer with world summary, search and sections; HTML (self-contained, CSP, no script, escaped), TXT, JSON (`schemaVersion 1`, stable ids); old history entries rebuilt only from placements that still exist. With vanilla keys (the default) Low / Progression are empty by design and say so.
- **Diagnostics and logging** (`docs/DIAGNOSTICS.md`): `Diagnostics` model (versions, install mode, signing state from `build-info.json`, game state, world, package summary, fog retries, timings, event windows), structured JSON logs with the stages GAME_DETECTION ... RESTORE, correlation id per operation (diagnostics only), `diagnostics/last-run.json` that survives failures.
- **Support bundle** (`docs/SUPPORT_BUNDLE.md`): allowlist of 10 named entries, redaction of user / host / folders (every spelling) / secrets / tokens / addresses, leak scan that refuses a bundle, size limits (2 MiB total), ZIP read-back; works after a failed generation, a failed Apply and with no world.
- **History**: row menu has View hints, Export hints, View settings, Diagnostics, Export support bundle, spoiler, delete; "Restore and create world" keeps the old entry (restored), gives the new world its own hint data and diagnostics (backend regression test + packaged app).
- **Tests (AUTOMATED VERIFIED).** Backend 537 tests: 535 pass, 0 fail, 2 skipped (needs the real game); desktop 33/33 (`release-tools` included); root typecheck clean. New: `locales.test.mjs` (parity, plurals, variables, fallback, literal detector), `language.test.mjs`, `hintSheet` / `hintStore` / `hintData`, `redaction`, `supportBundle` (synthetic secrets searched in every ZIP entry), `operations`, `diagnosticsText`, `smokeLocale`, `planBehaviors`, `restoreCreateWorld`, `saveFile`. The seven UX source-pin tests were moved to the resources (key used + English text unchanged).
- **Golden fixtures (AUTOMATED VERIFIED).** `goldenCompatibility.mjs verify` on the clean game copy with the packaged FogHost and DsrDataTool: **47 / 47 PASS, byte for byte** (RESULT: PASS). The whole phase's diff in the generation path is two optional log tags (`component`) and the `appVersion` string; no engine file, no seed or settings code changed.
- **Same world in both languages (AUTOMATED, packaged app).** The same Seed Code generated twice (Polish UI, then English UI): every package file and hash identical (5 files compared; `randomizer.json` compared without `config.outputDirectory`, the workspace path, like the golden check; `config.json` / `patch-manifest.json` excluded for the same reason), same attempts.
- **Packaged-app checks (clean game copy only; the live installation was not touched).** Fresh profile on this pl-PL Windows: Auto resolved to Polish, English / Polski / Auto switched live, the choice survived a restart (no flash: `html lang` correct at first look). A scripted audit of the DOM found, after the 11 px font fix, **0 problems** in every checked screen in both languages (ready, advanced with diagnostics and test worlds, generated, hint dialog at each level and its export section, diagnostics dialog, applied, pending "Przywróć i utwórz nowy świat", history): no raw keys, no `[object Object]`, no text of the other language, no clipping, no text below 11 px, no horizontal scroll. Apply / Restore / Load seed while applied -> restore-and-create / history / hints of both worlds / diagnostics of the old world: all as expected. Real exports through the native save dialog (Enter pressed): the HTML (132,714 bytes; doctype, no script, CSP) and the support-bundle ZIP (12,202 bytes, the 10 allowlisted entries) were written; the ZIP contains no user name, no clean-game folder, no AppData path and not the live session token; the Seed Code is kept. The portable build logged `started (portable)`; a silent install of the installer started, reported `installed / unsigned / 0.32.3` and uninstalled. The installer wizard opened in **Polish** ("Instalator DS1 Triple Randomizer", "Wybierz opcje instalacji", "Dalej >", "Anuluj") on this Polish Windows.
- **Packaged builds (UNSIGNED).** Installer `apps/desktop/release/DS1-Triple-Randomizer-Setup-0.32.3.exe` (130,242,913 bytes, SHA-256 `b5ef88eb4d61c380f1297b9bd6c3fd4e919ff9210ed09440bc0014eba1de7308`), portable `DS1-Triple-Randomizer-0.32.3-x64.exe` (129,726,777 bytes, SHA-256 `e8dc219ab1b4ecfbbd7414fcaec05dc6e27fe23fae47f1e169b3b8c6d17772fe`); `release-verify`: RESULT OK for development mode (UNSIGNED; a public release still needs the owner's certificate, `docs/RELEASE_TRUST.md`).
- **Fixed on the way.** The first hint wording called boss-reward / gift / drop / shop slots by a boss or NPC; the engine names them by the item that lies there in the clean game (now said exactly); spells (equip type 4) were unhintable (16 per world) and are now a hint kind of their own; the diagnostics panel re-reads when the world changes; the 9 / 10 px labels are 11 px.
- **Not done / limits.** PARTIAL: preset and validator problem lines and a few dynamic engine messages stay English technical text under a localized lead; redaction is pattern- and known-value-based. NOT SEEN: the English installer wizard (Windows here is pl-PL; STATICALLY configured) and the live use of the exported hint sheets in the game (GAMEPLAY UNVERIFIED: nothing here was played). DEFERRED: Polish names of game content, more languages, a preview of the bundle before saving. Commit `fb6badc` also contained the owner's uncommitted one-line edit of `docs/AI_EXECUTION_PLAN.md`; commit `f28efd0` takes it back out of the committed file and the edit is in the working tree, uncommitted, as it was.

**Part I: final item randomizer feature-parity pass - Race Mode, Race Mode +, item Difficulty, Laundromat Mixup (2026-10-07; backend 0.41.0, contracts 0.27.0, desktop 0.29.0, FogHost project 0.6.0 with `HostVersion` 0.5.0; patch 0024; generation profile `ds1tr-gen-v1` unchanged; presets version 15). AUTOMATED VERIFIED on real data / STATICALLY VERIFIED; GAMEPLAY UNVERIFIED. All four features are SAFE SUBSETS, none claims exact legacy parity. Builds: UNSIGNED.**
- **Audit first** (`docs/RACE_MODE.md`, `docs/ITEM_DIFFICULTY_MODEL.md`, `docs/LAUNDROMAT_MIXUP.md`, committed alone): the twenty questions per option from the pinned legacy sources (`HotPocketRemix/DarkSoulsItemRandomizer` `f7cabbd`, behavior only). Race Mode = Shuffled with the key candidates limited to the race locations; Race Mode + = the same plus skip edges, no prison check, no Sen's rule, the Purple Coward's Crystal as a key (the GUI forces Fair); Difficulty = an ordered preference of location tiers per item tier (keys HARD, small souls EASY, big and boss souls HARD), Fair = uniform = the legacy default; Laundromat = the armor cells of the NPC rows of CharaInitParam only (the starting classes are not part of it).
- **Race and Race +** (strategies of the one key planner, `KEY_PLACEMENT_STRATEGIES`): race destinations derived from the game data (boss rewards, chests = treasure events with the chest model read by the scan, vanilla pickups of keys and embers; 69 destinations, 87 of the 103 legacy rows agree); the embers, the Rite of Kindling, the Sewer Chamber Key and the Mystery Key are race items (stream `item-race`); Race + uses the vanilla graph plus 11 named skip edges of FogMod's glitched logic (Capra, Lower Burg, Annex jump, Seal skip, Four Kings OOB, crystal edges); 10.3% of Race + worlds need a skip (0% of Race). Excluded and documented: Black Knight and other enemy drops, Andre / Merchant / Ingward rows, the Golem drop, DLC boss drops, the Orange Charred Ring reward, the skips FogMod does not model. With the fog randomizer Race + is made as Race Mode (measured `FOG_KEY_CONFLICT` otherwise).
- **Difficulty** (`difficulty.js`): the policy is exact; the tiers are DS1TR-derived from the game data (agreement with the legacy tables 40% / 39%, chance 33%); measured distribution (200 worlds per mode): HARD items in HARD locations 14.1% (Fair) -> 32.8% (Unfair) -> 50.1% (Very Unfair), HARD items in EASY locations 38.2% -> 3.9% -> 3.9%, EASY items in EASY locations 40.6% -> 81.6% -> 81.6%. No mode can make a world unwinnable. A guard keeps "random only" items (Cast Light) out of the area they open.
- **Laundromat Mixup** (`laundromat.js` + DsrDataTool + `SmokeVerify laundromat`): 60 NPC characters wear pieces other NPCs already wear (no computed id), hollowed variants untouched; 25,757 planned rows read back from written packages (Fog included), 0 differences from the clean game outside the plan.
- **Compatibility.** Extension fields only (`race`, `race-plus`, `itemDifficulty` neutral `fair`, `laundromat` neutral off); presets version 15; **all 36 earlier golden worlds regenerate byte for byte** with the final engine, helpers and FogHost; 11 new `item-modes` worlds recorded and regenerate. Zero RNG when neutral (`combinedItemDifficulty.test.mjs`).
- **Proofs.** Engine level (`itemModesSweep.mjs`, 3,800 worlds, independent audits): 0 failures. Real data tool and FogHost (`placementMatrix.mjs item-modes-h`): **645 worlds, 0 failures, 0 non-deterministic reruns, 1 key placement attempt each, 0 fog retries**. Backend 483 tests (481 pass, 2 skipped by design), typechecks clean.
- **Hint-sheet data** (`core/hintData.ts`): one record per placement (item, kind, importance, original and new location with acquisition type and semantic owner), readiness test over generated worlds; no hint sheet is built; no spoiler severity exists in the architecture.
- **UI.** Items card: Key placement (Vanilla / Shuffled / Race Mode / Race Mode +), the Lordvessel and Lord Soul switches, "Item difficulty" group (Fair / Unfair / Very Unfair; disabled and forced to Fair under Race Mode +), Laundromat Mixup toggle; the "planned" group is gone (nothing planned is left in the card); the pipeline preview lists Key items, Item difficulty and Laundromat Mixup.
- **Quick test worlds (GAMEPLAY UNVERIFIED, statically verified):** `quick-race`, `quick-race-plus`, `quick-fair`, `quick-unfair`, `quick-very-unfair` (item lot 1810080 in the first cell, 2.8 m from the start) and the extended `laundromat-npcs`.
- **Packaged builds (UNSIGNED; clean game copy only, never the live installation).** Installer `apps/desktop/release/DS1-Triple-Randomizer-Setup-0.29.0.exe` (130,225,051 bytes, SHA-256 `cd75e76e8d93cfcccd2abf13fe184dab05d41534d5f355ff1733978aa72d7a3d`) and portable `apps/desktop/release/DS1-Triple-Randomizer-0.29.0-x64.exe` (129,635,344 bytes, SHA-256 `4e810be2c181034f10d63a4dff29eb0062ab38561d6c92f37e60f6f9f1a92ac2`); `release-verify`: RESULT OK for development mode, UNSIGNED, 146 files; desktop tests 14/14. Packaged application (fresh profile, its own UI): all the new controls (Race Mode + forces and disables Fair, leaving the key modes clears the switches, the planned group is gone), a full Triple with Race Mode + / Laundromat (planned as Race Mode with the fog randomizer), Apply and Restore, Load seed while the game is applied (a Race Mode / Very Unfair code: the new world is created with the settings kept, one extra history entry, no duplicate), Load seed of an old golden code (Vanilla / Fair / Laundromat off), the settings after a restart. **Installer:** silent per-user install exits 0, one HKCU uninstall entry, the installed application starts; the silent uninstaller exits 0 and leaves no folder and no entry. **Portable:** starts. Not re-checked in this phase: the icon, the title image and the missing Electron menu (unchanged files).
- **Remaining item parity gaps:** the legacy's hand-rated tiers (exact Difficulty), the skips FogMod does not model, Black Knight / shop destinations in Race, the Purple Coward's Crystal as a movable key, the hat chances and flagged sets of the Laundromat, keys in shops; the hint sheet itself.

**Part H: item and progression parity - Soul items Transposed, key placement, Lordvessel and Lord Souls (2026-10-07; backend 0.40.0, contracts 0.26.0, desktop 0.28.0, FogHost project 0.6.0 with `HostVersion` 0.5.0; patches 0021 to 0023; generation profile `ds1tr-gen-v1` unchanged; presets version 14). AUTOMATED VERIFIED on real data / STATICALLY VERIFIED; GAMEPLAY UNVERIFIED. Builds: UNSIGNED.**
- **Soul items Transposed** (backend 0.39.0): the legacy semantics from the pinned sources and the game data; boss reward catalog (event 970; ownership follows the encounter flag, never a body); 380/380 real-data worlds; 4 golden worlds; a quick test world. `docs/ITEM_PROGRESSION_MODEL.md` sections 1 to 4.
- **Key placement** (`items.keyPlacement`: `vanilla` / `shuffled`): one world model (FogMod's graph exported by `FogHost progression-model`, evaluated by `keys.js`, cross-checked against FogMod's `GraphChecker` on 3000 assignments), assumed fill over certain world and boss pickups, displaced items paired with the freed pickups, bounded and deterministic (streams `item-keys:<n>`, `item-keys-fill`), typed failures (`KEY_PLACEMENT_*`, `FOG_KEY_CONFLICT`), works with the fog randomizer off and on. **Lordvessel and Lord Souls** are two switches inside it (no event is rewritten). Race Mode / Race Mode +: UNSUPPORTED. DLC: decision C unchanged (`includeDlc` fixed on; no key in the DLC).
- **Compatibility.** The new options are extension fields (written to a Seed Code only when non-neutral); every earlier Seed Code, preset and settings record keeps its text and meaning; all 31 earlier golden worlds regenerate byte for byte; 5 new `key-placement` worlds recorded. Neutral engine runs are byte-identical.
- **Proofs.** `placementMatrix.mjs keys-h`: **980 worlds, 0 failures** (510 without Fog, 250 with Fog, 220 full Triple; 460 of them with Lordvessel / Lord Souls), 1 key placement attempt in every world, 0 `FOG_KEY_CONFLICT`, 0 unsatisfiable, 0 duplicates or missing items, 0 non-deterministic reruns; FogMod's reader and checker over the lots of every written package. One measurement artifact (Cast Light, a `randomonly` item) found, explained and corrected in the script (section 9). Backend 461 tests (459 pass, 2 skipped by design), typechecks clean.
- **UI.** Items card: "Key items" group (Key placement Vanilla / Shuffled, Lordvessel placement, Lord Soul placement; the two switches are cleared when Key placement leaves Shuffled), "Legacy parity planned" keeps Difficulty, Race modes ("Not built") and Laundromat Mixup; the pipeline preview lists Soul items, Key items, Lordvessel and Lord Souls.
- **Quick test worlds (GAMEPLAY UNVERIFIED):** `quick-transposed-soul`, `quick-key-placement` (Cage Key in the cell), `quick-lordvessel`, `quick-lord-soul`; each names the exact pickup (item lot 1810080, 2.8 m from the start), the item expected there and what to check; static check `smokeTestCatalog.mjs verify` passes.
- **Packaged builds (UNSIGNED; checked against the clean game copy, never the live installation).** Installer `apps/desktop/release/DS1-Triple-Randomizer-Setup-0.28.0.exe` (130,206,997 bytes, SHA-256 `4bbbf59e668a6ba2111c51da28b1c17bca4124efb59835fa9f7142f426c3c52d`) and portable `apps/desktop/release/DS1-Triple-Randomizer-0.28.0-x64.exe` (129,617,225 bytes, SHA-256 `ce6558e00ee84e7f65fc906769fe50b14404f05a84a382f9979ffe0a1ccf967f`); `release-verify`: RESULT OK for development mode, UNSIGNED, 143 files, no elevation helper; desktop tests 14/14. Packaged application (`win-unpacked`, fresh user data folder, driven through its own UI): the Key items group, the switches (inactive and cleared when Key placement is Vanilla), the warning without World items and the plain refusal at Randomize, a full Triple with Key placement + Lordvessel + Lord Soul placement (the Seed Code decodes to `keyPlacement shuffled`, both switches on), Apply and Restore, the pipeline preview lines, Load seed of an **old golden code** (Vanilla, switches off) and of the keys code (Shuffled, both on), and the settings after a restart. **Not repeated in this phase:** the silent installer install/uninstall and the portable start (they were exercised on the previous builds; the contents are the same packaging path).
- **Remaining parity gaps:** Race modes, Difficulty classes, Laundromat Mixup, the hint sheet (data exists), excluding the DLC, keys from enemy drops / NPC drops / the Orange Charred Ring pickup. Next phase recommended: play the four quick worlds and one full run of a keys world; then Difficulty or the hint sheet.

**Parts A + G + Release trust (2026-10-07; backend 0.38.0 (0.37.1: event owners), contracts 0.24.0, desktop 0.26.0; patch 0018; generation profile `ds1tr-gen-v1` unchanged; RANDOMIZER_VERSION 0.5.66 and FogHost 0.5.0 unchanged; presets version 13). Event owners: AUTOMATED + STATICALLY VERIFIED. Default flip: AUTOMATED VERIFIED. Builds: UNSIGNED (SIGNING READY, never TRUSTED SIGNED).**
- **A. Darkroot Basin event owner.** The Basin (`m12_00_00_01`) has no event file of its own: its EMEVD is the Garden's (`m12_00_00_00`). Proven from the game data by a one-off analysis of a clean copy (every MSB's entity IDs against every event file; the analysis tool was a scratch program and is not in the repository, the finding is recorded in `docs/EVENT_ALLOCATION.md`), guarded in the repository by `EnsureEventOwnersExist` and `SmokeVerify audit`, resolved centrally in DsrDataTool (`EventOwnerMap`, `EnsureEventOwnersExist`, `docs/EVENT_ALLOCATION.md` "Event owners"), event IDs allocated per owner file, ten event filters use the owner, the catalog event source is de-duplicated. Mode `shared` is chosen by the backend for legacy-style sampling worlds; **classic stays frozen** (its Basin placements keep having no events: a documented quirk). Hostile NPCs may now stand in the Basin (the restriction is gone). Verifier: `SmokeVerify audit` and the matrix fail on any placement whose events are missing from its owner file. **Recorded exception 2** in `docs/GENERATION_COMPATIBILITY.md` (12 sampling + 5 hostile fixtures re-recorded; every classic and unaffected fixture byte-identical). Patch `0018-event-owners.patch`.
- **B. Part G, the default.** New configurations start with `sampling-v1` and the legacy defaults ("Sampling (recommended)" / "Classic permutation"; serialized values unchanged); Seed Codes, presets, saved settings and history keep their algorithm; the test worlds use the frozen `SMOKE_TEST_BASE_SETTINGS`; no dead controls (the sampling options are shown only with sampling; the "planned" placeholder is gone). Ordering of the planners is documented and locked by tests (`enemyPlacementOrdering.test.mjs`); a neutral option draws nothing. The placement validation summary is recorded in the run manifest (`enemyPlacement.validation`). Details: `docs/ENEMY_PLACEMENT_MODEL.md` 10.10, `docs/SEED_FORMAT.md` "Defaults and old records", `docs/DECISIONS.md`. **Status wording: Enemy Placement core complete with documented safe-subset deviations.**
- **C. Release trust and antivirus hardening.** `docs/RELEASE_TRUST.md`, `docs/SECURITY_AND_PRIVACY.md`, `tools/release/` (policy, verifier, public-release gate, optional Defender scan, helper format experiment, tests). Installer (per-user NSIS, no administrator, no elevation helper) is the primary download, portable the secondary; helpers are an **uncompressed** single file (entropy 6.71 instead of 7.81 bits per byte; start time DsrDataTool 1,054 to 373 ms, FogHost 315 to 384 ms); executable metadata names this project, not the toolchain; `npm run package:win -- --release` refuses to run without credentials from the environment (tested), signs the application, both helpers, installer, uninstaller and portable when it has them, and ends with `release-verify --require-signature`. **Pipeline tested with a throwaway certificate in a scratch folder** (all five DS1TR files signed by electron-builder; the verifier says `TEST-SIGNED (untrusted root)` and fails public mode; the certificate and the signed output were deleted). A bug found by that test: `signingHashAlgorithms` must be an array (moved to `electron-builder.yml`). No certificate exists: **every build is UNSIGNED.** The tooling folder was previously ignored by `.gitignore` (`release/`): fixed with an explicit exception, and `*.pfx`, `*.p12`, `*.pem`, `*.key`, `.env` are now ignored.
- **Proofs (clean copy of the game only; the live Steam installation was not touched).**
  - Real data tool, event owners (`placementMatrix.mjs event-owners`): **810/810** (E1 roaming + both stay dead, hostile NPCs, Type replacement enabled / except roaming, general sampling: 100 seeds each; Part D boss sampling, E2 bodies, Second Gargoyle: 60 each; classic frozen: 30): 0 failures, 0 event owner failures over about 230,000 checked events, 0 non-deterministic reruns, 0 window overruns.
  - Real data tool, the product default (`placementMatrix.mjs product-default`): Items + Enemies + Bosses 100/100; Enemies + Bosses 100/100; **the full Triple with the real FogHost 100/100**.
  - Fog real format (`fogHostE2e.mjs`, `verify-skips`): default, E1 both + stay dead, hostile NPCs + Type enabled, E2 boss bodies: 4 x 25 = **100/100**, 0 retries, 0 conflicts, 100/100 skip checks, 0 event mismatches (including Basin worlds).
  - Engine sweeps: default 500/500 and classic 500/500 (`defaultSweep.mjs`); feature matrix 384 combinations x 30 seeds (`featureMatrix.mjs`): 7,680 worlds, 3,840 typed refusals, 0 failures.
  - Golden: **27/27** regenerate from their Seed Codes.
- **Packaged builds (UNSIGNED; all checked against the clean game copy, never the live installation).** Installer `apps/desktop/release/DS1-Triple-Randomizer-Setup-0.26.0.exe` (130,184,556 bytes, SHA-256 `ead503ab536b1984cb614d64060934926432da38be912416fe2238bfc9cfd02b`) and portable `apps/desktop/release/DS1-Triple-Randomizer-0.26.0-x64.exe` (129,594,845 bytes, SHA-256 `c97e4251b827fce60d0a1d605720790a4de204e8a4fa74a900901854b78f2a85`); `release-verify` reports UNSIGNED, structure and metadata OK, no `elevate.exe`. In the packaged app with a fresh user data folder: the algorithm select says "Sampling (recommended)", all sampling controls are present, Randomize makes a full Triple (Items + Enemies + Bosses + Fog) whose Seed Code decodes to `sampling-v1`, Apply and Restore succeed, after a restart the settings and history persist (one entry, "Restored"). An **old classic Seed Code** (golden `enemies-classic-permutation`) loaded with Load seed makes that world with "Classic permutation" selected and the sampling controls hidden; after a restart the classic choice persists; Randomize with it makes a code that decodes to `permutation-v1`; "Restore default settings" returns to sampling. **Installer:** silent per-user install exits 0 without an elevation prompt, writes the install folder, one desktop and one Start menu shortcut and one HKCU uninstall entry (no HKLM, no `elevate.exe`); the installed application generates a full Triple, Applies and Restores; the silent uninstaller removes the folder, shortcuts and the entry and keeps the user data. **Portable:** starts and passes the same checks; it runs from a random folder under `%TEMP%` (self-extraction, as documented). The UI/installer checks above were run on the previous build of the same sources (backend 0.37.1 -> 0.38.0 changed only the version strings and the final package was rebuilt from the committed tree and re-verified with `release-verify`; the UI checks were not repeated on it). Local Defender scan: NO RESULT (Defender is not the active antivirus here). **Norton 360 blocked the two throwaway-signed test copies of the helpers** (`IDP.HELU.PSD11`); it did not block any unsigned packaged file in this session (`docs/RELEASE_TRUST.md` section 8).
- **Tests:** backend 425 tests (423 pass, 2 skipped by design, 0 fail), desktop 13/13 (includes the release tooling tests), both typechecks clean. Found and fixed on the way: the integration test picked a random port that can fall into a range Windows reserves (it now asks the operating system for a free port), and two smoke-test assertions that predated the recorded gameplay results.
- **Gameplay status (honest).** *Runtime-verified by the owner:* the main E1 quick tests, boss sampling (first boss), sampling first enemies, E2 Asylum Demon, E2 Taurus Demon. *Not verified by playing:* Part F (hostile NPCs), E2 Capra Demon, the extended Fog / Gargoyle / Parish worlds, the Darkroot Basin test worlds. **No gameplay was claimed or inferred for any of them.**
- **Not done (by design):** no certificate was bought, requested or generated for release; nothing was pushed, published or uploaded to a scanner; the local Defender scan has no result (Defender is not the active antivirus on the machine); no setting of the security software was changed.

**Part F1: hostile NPCs, a safe subset (2026-10-07; backend 0.37.0, contracts 0.23.0, desktop 0.25.0; patch 0017; generation profile `ds1tr-gen-v1` unchanged; RANDOMIZER_VERSION 0.5.66, FogHost 0.5.0; presets version 13). PART F SAFE SUBSET - STATICALLY VERIFIED / GAMEPLAY UNVERIFIED.**
- **What it is.** The legacy "Hostile NPCs" option: the Undead Merchant (Undead Burg), Andre of Astora and Vamos join the pool of normal enemies as hostile fighters (a toggle "Hostile NPCs", shown only where it can apply). The real NPCs are never touched. The legacy "NPC replacement" (E3) is **not built, by decision**: its six slots are services (shops, blacksmiths,
  the Fair Lady) and replacing them removes them; the placeholder control was removed. The legacy has no hostile destination slots, NPC-to-NPC rule, invaders or summons (`c0000` is in no table). Reconstruction of the 20 questions, the classification of every non-regular slot and the cell ownership table: `docs/ENEMY_PLACEMENT_MODEL.md` 10.9.
- **Safety.** Three of the legacy's four sources (Gough and the Lower Burg Merchant are left out, with reasons). The slot owns the actor: team, `npcType`, covenant mark, respawn, drops, humanity, item lots and souls come from the slot's row (asserted by the data tool); the body, animations and AI are the NPC's. No talk script is read, written or present in any package. At most twelve per map (an uncapped run had up to 117 in one map: the cap bounds the wake-up events),
  none in the Darkroot Basin (no event file there; found by the verifier), no wake-up for dummy parts, the Lower Burg Merchant's missing logic script (found by the verifier) is why it is not a source.
- **Compatibility.** `hostileNpcs` is an extension key (neutral false); no new stream, no new constant; the option off adds nothing: all 22 earlier golden fixtures verify byte for byte; 5 new fixtures (`ds1tr-gen-v1.hostile-npcs.json`). No Seed Code changed.
- **Quick test** `quick-hostile-npc` ("Hostile NPC - Andre as an enemy", `smoke-q8-npc-562`, about 30 s): Andre of Astora in the first Hollow's place, 8.7 m from the start. **Not played.** Five extended worlds: `npc-world`, `npc-fog`, `npc-roaming`, `npc-type`, `npc-bosses`.
- **Proofs (real catalog / data tool / FogHost; clean game copy only; the live Steam installation was not touched).**
  - Engine `npcSweep.mjs`: 15 configurations x 100 seeds = 1500/1500. E1 `roamingSweep.mjs` 1800/1800 and E2 `bodySweep.mjs` 1800/1800 unchanged with the option off.
  - Fog (real format, `verify-skips` on every package): **100/100**, 0 retries, 0 conflicts, 0 skip mismatches; every overlay inside the existing 15-event whitelist, the same 12 merges: no new overlay.
  - Static verifier: 29 checks for the quick world; a 100%-replacement world passes 41/41 expectations (rows equal the slot's, no ghost transition, think row, battle and logic script in the map's AI bundle, wake-up event).
  - Real data tool matrix (`placementMatrix.mjs hostile-npc-f1`): 10 cases x 40 seeds (E1 used 100 and 60): **399/400** on the first run and 400/400 with the one exception rerun: seed 36 of `npc-vanilla-scaling` failed once with a Windows file-lock error while the data tool wrote a map file (`The process cannot access the file ... m13_02_00_00.msb because it is being used by another process`, an environment error, not a generation error) and passed when run alone (`--only 36`). 0 retries, 0 event-window overruns, 0 non-deterministic reruns; at most 108 custom events in one map (Demon Ruins, Type replacement except roaming bosses + roaming bosses; the window moved off the Bed of Chaos floor event in 3 seeds, which is what `docs/EVENT_ALLOCATION.md` provides); maximum model count 30 (31 in a map whose fixed models already exceed 30, the non-worsening rule).
- **Tests:** backend 411 tests (409 pass, 2 skipped by design, 0 fail), desktop 6/6, desktop typecheck (both tsconfigs) clean, `goldenCompatibility.mjs verify` 22/22 earlier fixtures plus the 5 new ones recorded after, `smokeTestCatalog.mjs verify` for the quick worlds..
- **Packaged build:** fresh portable EXE `apps/desktop/release/DS1-Triple-Randomizer-0.25.0-x64.exe` (105,550,117 bytes, SHA-256 `43601824a7bb1ea2c8e3ef7c3223a02582a1e910e347d17d656a3b01c584cc08`). In the packaged app: custom icon and no menu bar (`HMENU` 0), a decorative brand mark in the header (withdrawn in 0.35.0), run history collapsed, the game folder remembered after a restart, the "Hostile NPCs" toggle exists only with legacy-style sampling and normal enemies replaced (`normals` / `both`) and no custom pool, goes back to off when the mode is left, and the disabled "NPC replacement" / "Hostile NPCs" placeholders are gone (only "Allow T-pose behavior" is still planned); the quick tests are grouped under "Quick smoke tests" and the new one shows target / expected / time / checks and "not played"; "Create this test world" and Load seed generate the world automatically, the Seed Code equals the one shown, and the loaded settings switch the toggle on; the package the packaged app produced passes the static checks..
- **Not verified by playing:** everything runtime for a hostile NPC (wake-up, hostility, no T-pose, death, only the slot's rewards, respawn like a regular enemy). **E2 was not changed**; the user's E2 tests are separate.

**Part E2: a regular enemy as the body of a boss encounter, a safe subset of three (2026-10-07; backend 0.36.0, contracts 0.22.0, desktop 0.24.0; patch 0016; generation profile `ds1tr-gen-v1` unchanged; RANDOMIZER_VERSION 0.5.66, FogHost 0.5.0 and presets version 12). STATICALLY VERIFIED / GAMEPLAY UNVERIFIED.**
- **What it is.** Boss replacement gains "Only normal enemies (early fights)" (`normals`) and "Bosses or normal enemies" (`both`, with "Boss chance in boss slots", default 90, shown only then). A normal enemy stands in the arena of the **Asylum Demon,
  Taurus Demon or Capra Demon**; the encounter stays the boss's (entity, events, fog gate, what the death unlocks). Every other boss keeps its boss (`normals`) or gets a boss (`both`). This is a subset on purpose: the audit of
  all 24 boss encounters (class and reason for each exclusion) and the reconstruction of the legacy semantics (20 questions) are `docs/ENEMY_PLACEMENT_MODEL.md` 10.8. **Not claimed:** "full E2".
- **Design (decision in `docs/DECISIONS.md`).** Entity-preserving body through the existing Part D boss path: no new event, no new event ID, no new Fog overlay; the body's scaled NpcParam row takes the encounter-owned cells (`dropType`,
  `getSoul`, `disableInitializeDead`, item lots) from the boss row and sets `disableRespawn` 1 (`EncounterOwnedCells`, data tool patch 0016). The health bar keeps the boss's name. Scaling follows the existing setting. Streams `category:slot:<id>` and
  `body:slot:<id>` are new; with the chance at 100 `both` equals boss sampling.
- **Compatibility.** `bossChanceInBossSlots` is an extension key (neutral 90); presets version 12. All 18 earlier golden fixtures verify byte for byte (re-verified after the last engine change); 4 new fixtures (`ds1tr-gen-v1.encounter-bodies.json`: basic,
  Fog, `both` + roaming bosses, Custom Pool edge). No Seed Code changed.
- **Quick test** `quick-encounter-body` ("First boss fight - a normal enemy", `smoke-q7-body-43`, about 90 s): Balder Knight in the Asylum Demon arena, 22.5 m from the start, 13.3 m up; the health bar must say "Asylum Demon", the knight takes boss health,
  the fight completes and the way on opens. **Not played.** Five extended worlds: `body-burg-fights`, `body-fog`, `body-roaming`, `body-both`, `body-gargoyle` (`docs/SMOKE_TESTS.md`, regenerated).
- **Proofs (real catalog / real data tool / real FogHost; clean game copy only; the live Steam installation was not touched).**
  - Engine: `bodySweep.mjs`, 18 configurations x 100 seeds = 1800/1800 (bodies only in the audited slots with the audited models, entity ID and scaled row, scaling as set, model count never above `max(30, vanilla)`, `both` at 100 = boss
    sampling, `both` at 0 = the bodies of `normals`, no body outside a Custom Pool). E1 regression `roamingSweep.mjs`: 18 configurations x 100 seeds = 1800/1800.
  - Fog (real format, `fogHostE2e.mjs`, `verify-skips` on every package): **100/100** generated over 10 configurations x 10 seeds (`normals`, `both`, curve/fits/anywhere, replacement chance 60-100, Unique bosses, Type replacement enabled / except
    roaming bosses, roaming bosses at the same time, Custom Pool), 0 retries, 0 conflicts, 0 skip mismatches over 11,526 edited events / 33,914 surviving skips, **every overlay ID is inside the 15-event boss overlay whitelist (no new overlay)**; the maximum
    model count is 33, the Undead Parish baseline.
  - Static verifier (`tools/smoke-verify`, 23 checks per encounter): read back from the real package of one world with a body in all three slots, **all three slots pass** (entity ID, rows equal the boss row, respawn bit, events kept, no condition / flag-write /
    initializer removed; the only removed instructions are the model-specific forced animations `2003[18]` aimed at the encounter's own entity, which is Part D's sanitization: the Asylum Demon drop and the Taurus Demon entrance).
  - Real data tool matrix (`placementMatrix.mjs encounter-e2`, 10 cases x 40 seeds; E1 used 100 and 60): **400/400** generated (10 cases: `normals` anywhere / fits + curve / chance 50, `both` 40% / Unique, + roaming bosses, Type replacement enabled, Custom Pool, Vanilla scaling, Second Gargoyle + Gwyn strong), 0 retries, 0 window overruns, 0 non-deterministic reruns, maximum model count 30, at most 86 custom events in one map (Demon Ruins, Type replacement) against the 100-ID window.
- **Tests:** backend 405 tests (403 pass, 2 skipped by design, 0 fail; one server-start timeout was seen once while the Fog sweeps were running, it passed alone and in the final run), desktop 6/6, desktop typecheck (both tsconfigs) clean, `goldenCompatibility.mjs verify` 22/22 after the last engine change, `smokeTestCatalog.mjs verify` 7/7 quick worlds.
- **Packaged build:** fresh portable EXE `DS1-Triple-Randomizer-0.24.0-x64.exe` (built twice; the proof is on the second build). In the packaged app: custom icon and no menu bar (`HMENU` 0), a decorative brand mark in the header (withdrawn in 0.35.0), run history collapsed, the game folder remembered after a restart, Boss replacement offers the four modes, "Boss chance in boss slots" exists only with "Bosses or normal enemies" and returns to 90 when the mode is left (Unique bosses is reset when it no longer applies), the quick tests are grouped under "Quick smoke tests" and the new one shows target / expected / time / checks and "not played"; "Create this test world" and Load seed generate the E2 world automatically and the Seed Code equals the one shown (`...TMPK9NTD-S97TKN1N-...-1450`); the package the packaged app produced passes the 23 static checks.
- **Not verified by playing:** everything runtime for a body in the three arenas (wake-up, killability, health bar name, the way on, rewards, no respawn, crashes).

**Runtime smoke-test worlds (2026-10-06; backend 0.33.0, desktop 0.20.0, contracts 0.18.0).**
Advanced has a collapsed "Test worlds for playtesting" panel: eight fixed, reproducible worlds (classic full Triple, sampling of regular enemies,
the second Bell Gargoyle, the Undead Parish, boss sampling, a boss repeated in several places, Unique bosses, partial boss replacement) with a
Seed Code, what to look at in that world and a checklist (model, AI, navigation, floor, health bar, kill, fog exit, progression, rewards, respawn,
crashes). They are created through the normal Load seed path and pinned to `ds1tr-gen-v1`. `docs/SMOKE_TESTS.md` is generated from the same data
(`node apps/backend/scripts/smokeTestCatalog.mjs docs docs/SMOKE_TESTS.md`) and a test fails when a documented code differs from the app's.
Nothing is checked automatically and nothing is marked as passed: the worlds exist so a person can play them. The main screen now starts from
the same `DEFAULT_GENERATION_SETTINGS` as the test worlds (one definition in contracts).

**The game folder you chose is remembered (2026-10-06; desktop 0.19.1, contracts 0.17.0).**
After a restart the app used the Steam copy whenever Steam detection found one, even if the person had chosen another folder, because
the saved choice was honoured only when it was among the detected installations (and a typed folder was never saved at all). Now a folder
chosen by hand (Browse, "Use this folder", or one of several detected copies) is saved after the installation check accepted it and wins at
the next start; Steam detection is only the fallback when nothing is saved, the saved folder is gone, or it no longer passes the check.
The saved choice is machine-local (`userData/settings.json`), never part of a Seed Code, a preset or a generation configuration, and the
per-installation state is keyed by the installation path (`state/installs/<id>`), so state of one copy is never applied to another.
The decision is a pure function (`decideStartupInstallation`, contracts) with tests; a packaged-app proof drove the cases through the UI.

**Fix: custom event window vs the Bed of Chaos floor event (2026-10-06; backend 0.32.1, data tool only; `ds1tr-gen-v1` unchanged).**
`sampling-v1` with Type replacement `enabled`, Placement `anywhere` and bosses on sometimes failed in `patch-enemies` with
`Randomizer event ID is already used: 11419980`. Root cause (reproduced deterministically, details in `docs/EVENT_ALLOCATION.md`): the
per-map window of randomizer events (`x19900`...) is chosen before the fixed-ID Bed of Chaos floor event `11419980` is added, so a
Lost Izalith with more than 80 event-requiring placements (96 in the reproduction) ran into it. The floor event ID is now reserved before the
window is chosen (`FindFreeCustomEventRange(..., reserved)`), and the tool asserts that no map uses more IDs than were reserved. Windows of up
to 80 events, and every other map, are unchanged: the audit shows `IDs used == IDs reserved` in every map of every run.

- **Before / after on the same seed:** index 59 of the 100-seed set (and `golden-v1-boss-combined`) fails with the old tool exactly with
  the 11419980 message and succeeds with the fixed one (window moved to `11419981`). 1 of 100 seeds triggers it in this configuration.
- **100 seeds** of the formerly failing configuration, isolated run: 100/100 success, first attempt, deterministic reruns, 0 window overruns.
  A first run of the same 100 seeds, made while other heavy jobs ran in parallel, had one `DATA_TOOL_FAILED` (index 26) whose details were not
  captured; it did not recur in the isolated run, in three more runs of the same seed, nor in 52 runs under four-fold contention. Not
  explained; the matrix runner now records the tool's message for every failure.
- **Fog:** 30 seeds Type enabled + anywhere + bosses + Fog: 30/30, `verify-skips` clean on every package, the same 15 boss overlay events.
- **Compatibility:** all 14 earlier golden fixtures, the 24 lock hashes and the sampling pins are identical with the fixed tool; one new
  permanent fixture (`ds1tr-gen-v1.event-window.json`) pins the relocated window. A colliding run ended in `DATA_TOOL_FAILED` (one attempt,
  no reseeding), so no package or Seed Code of such a world ever existed; no profile change is needed.
- **Tooling:** `apps/backend/scripts/placementMatrix.mjs` (configuration x seed matrix with window audit, retries, deterministic reruns).

**Roaming boss respawn fix and the first real gameplay results (2026-10-06; backend 0.35.1, contracts 0.21.0, desktop 0.23.0; patch 0015; generation profile `ds1tr-gen-v1` unchanged).**
- **Gameplay milestone.** The five quick smoke tests (roaming boss - basic, roaming boss - souls and respawn, three roaming bosses at once, regular enemies - first three change, boss
  sampling - the first boss) were **played by hand in the real game and passed**: the replacements appeared and rendered, woke up, fought and could be killed, boss and non-boss health bars
  behaved as expected, the souls were as expected, the stay-dead boss stayed dead after the reset, the Asylum Demon encounter completed with the replacement boss, no crash or T-pose.
  This is real gameplay validation of Part D boss sampling on the Asylum Demon, of sampling-v1 regular enemies, and of roaming bosses with stay dead **on**. The extended worlds (Fog,
  Second Bell Gargoyle, Undead Parish, Type + roaming in the Burg, ...) were **not** played and are not marked as verified. Two of the five worlds (`quick-roaming-basic`,
  `quick-roaming-repeated`) were played with the package from before the respawn fix and have not been played again; their record says so.
- **Respawn bug fixed.** Root cause: a roaming boss row is a copy of a boss row (`disableRespawn = 1`) and DsrDataTool only set the bit when stay dead was on, so "stay dead off" never
  cleared it. The tool now writes the bit for every roaming row from the option (1 or 0) and asserts it. Compatibility: only roaming worlds with stay dead off change, and only in that
  cell of `GameParam`; the stay-dead-on fixture, the 15 earlier fixtures and Part D are byte-identical; recorded as a narrow exception without a new profile
  (`docs/GENERATION_COMPATIBILITY.md`). The UI hint "Off: it respawns like a normal enemy" is now true in the data.
- **New quick test `quick-roaming-respawn`** (Capra Demon in place of the Hollow c2500_0007, 11.4 m from the start, stay dead off): kill it, reset the area by dying once, return; the boss must
  be alive again. Statically verified (`disableRespawn = 0`); **not played yet**.
- **Regression (real catalog, real data tool):** golden 18 fixtures: 16 identical, the 2 stay-dead-off roaming cases changed exactly as predicted (`GameParam` only) and were re-recorded, then 18/18; quick static verification 6/6 (packages identical on a second run);
  Part E representative matrix 125/125 (5 cases x 25 seeds), Part D baseline 200/200 (boss sampling and curve, 100 seeds each, same figures as before), Fog 30/30 with `verify-skips` clean (15 boss-overlay events, 12 merges, 0 conflicts);
  0 event-window overruns, 0 non-deterministic reruns, no map above its own model baseline (Undead Parish 33 as before). Backend 397 tests, desktop 6/6.

**Smoke tests redesigned: quick vs extended (2026-10-06; backend 0.35.0, desktop 0.22.0, contracts 0.20.0; engine, data tool and generation profile `ds1tr-gen-v1` unchanged).**
The test worlds under Advanced are split into five **quick smoke tests** and twelve **extended gameplay tests**. A quick test puts the feature next to where the player wakes up
in the Undead Asylum (first visit): a Capra Demon where the first Hollow stood (8.7 m), a Taurus Demon with 100% souls that stays dead, the same Smough in three neighbouring slots,
three different regular enemies instead of the first three Hollows, and a Taurus Demon in place of the Asylum Demon. Each shows its exact slot, the distance from the start, the expected
replacement, an estimated time (20-90 s) and specific checks. **No override or forced placement was needed:** the quick worlds are ordinary worlds whose master seeds were *found* by
`apps/backend/scripts/smokeWorldSearch.mjs` (engine only, real catalog) from normal settings; they stay normal Seed Codes, so nothing in generation, Seed Codes or history changed.
`smokeTestCatalog.mjs verify` generates each quick world twice through the normal pipeline (identical package) and `tools/smoke-verify` reads the package: the MSB part has the promised
model, the model is loaded by the map, the NpcParam and NpcThinkParam rows exist, souls and the respawn bit are as promised, a wake-up event was added for the entity, event ids are
unique and the model count is within the limit. That proves the files contain the target; nothing was played. Details: `docs/SMOKE_TESTS.md`.
- **Found by the static check and fixed in backend 0.35.1 (see the entry above this one):** a roaming boss row is a copy of the boss row, and boss rows carry `disableRespawn = 1`; Part E1 only ever *set* the bit, so with "Roaming bosses stay dead" **off** it stayed 1.
- **Also seen, not changed:** the roaming pass treats dummy slots (`c2500_0004`, `c2500_0008` in the Asylum) as ordinary slots, so a boss can be placed on a part that is never spawned.

**Enemy placement Part E1: bosses in regular enemy slots (2026-10-06; backend 0.34.0, desktop 0.21.0, contracts 0.19.0, patch 0014; RANDOMIZER_VERSION 0.5.66 and FogHost 0.5.0 unchanged; generation profile `ds1tr-gen-v1` unchanged).**
`sampling-v1` with Normal enemy replacement "Only bosses" or "Bosses or normal enemies" places bosses in regular enemy slots ("roaming bosses"), with a
"Boss chance in normal slots" (only with "either"), "Roaming boss souls" (percent of the authored fight souls, written to the scaled NpcParam row's `getSoul`) and
"Roaming bosses stay dead" (the row's own `disableRespawn` bit; **not yet confirmed in the game**). Type replacement "except roaming bosses" is now real. The
controls appear only when they apply and are put back to their defaults when they stop applying. 17 boss archetypes can roam; Bed of Chaos, Ceaseless Discharge,
Dark Sun Gwyndolin, Lord Gwyn and every multi-body boss never do; at most 10 roam in a map; the model limit never gets worse (no Custom Pool grace). **Part E2 (a
regular enemy in a boss slot) is not built** and shows no control; the reasons and what a safe version needs are in `docs/ENEMY_PLACEMENT_MODEL.md` 10.7. Part F (NPC /
hostile NPC replacement) is untouched. Details, legacy semantics, deviations and measurements: `docs/ENEMY_PLACEMENT_MODEL.md` 10.7.

- **Seed Codes:** the three options are extension keys (stored only when non-neutral); `normalReplacement` gained two values an older code cannot contain. All 15 earlier golden
  fixtures verify byte for byte with the Part E engine and data tool; three new fixtures pin roaming worlds (`ds1tr-gen-v1.roaming-bosses.json`). Presets are version 11.
- **Tooling:** `apps/backend/scripts/roamingSweep.mjs` (engine on a real catalog, invariants), `roamingProbe.mjs` (keeps one package), `placementMatrix.mjs roaming-e1`
  (real data tool), six Part E smoke worlds in Advanced (`docs/SMOKE_TESTS.md`).
- **Measurements:** engine 1800/1800 worlds (18 configurations x 100 seeds); real data tool 1780/1780 runs (including 300 baseline runs); Fog 120/120 with `verify-skips` clean, boss overlay unchanged (15 events), 0 conflicts; no map above its own model baseline (Undead Parish 33 as before); 15/15 earlier golden fixtures byte-identical; full table in `docs/ENEMY_PLACEMENT_MODEL.md` 10.7.
- **Not verified in the game (needs playtesting):** that a roaming boss wakes up, navigates, can be killed and drops the intended souls; that "stay dead" holds after resting at a bonfire;
  that no map shows missing textures or crashes with up to ten bosses and 30+ models; the Fog interplay at runtime. Use the Part E worlds under Advanced, Test worlds for playtesting.

**Enemy placement Part D: boss sampling (2026-10-06; backend 0.32.0, desktop 0.19.0, contracts 0.16.0, patch 0012; RANDOMIZER_VERSION 0.5.66 and FogHost 0.5.0 unchanged; generation profile `ds1tr-gen-v1` unchanged).**
`sampling-v1` can draw a replacement for every single-body boss slot from the boss pool (`Boss replacement: Random bosses`), with the
replacement chance per boss slot, the Gwyn nerf per candidate and an optional `Unique bosses` cycle; the controls replace two PLANNED
placeholders. Linked units (Priscilla, Kalameet, Gaping Dragon, Seath, Sanctuary Guardian, Bell Gargoyles, Centipede Demon) stay with their classic planners (unit participation is not built). Details,
evidence and open points: `docs/ENEMY_PLACEMENT_MODEL.md` 10.6. The two new options are **extension keys** of the Seed Code (stored only when
non-neutral), so no existing code changed and none can decode to another world (`docs/SEED_FORMAT.md`, `docs/DECISIONS.md`); presets are version 10.

- **Real format:** 100 boss-sampling seeds with Fog (40 chance 100, 40 Unique at chance 60, 20 with Enemy scaling `vanilla`): 100/100 success,
  boss overlay set unchanged (the same 15 events), 12 instruction merges as before, 0 conflicts, `verify-skips` clean on every package, maximum 30
  models per map; 40/40 for the classic reference. A first run failed 36/40 (`Conflicting custom NpcThinkParam definition`); fixed (per-slot ids).
- **Compatibility:** 24 permutation lock hashes and the sampling pins identical; the 10 original golden fixtures, the two sampling-v1 + classic-boss baselines
  recorded before this change and the two new boss-sampling fixtures reproduce from their Seed Codes (also with a simulated future app version).
- **Found, not fixed:** `sampling-v1` with Type replacement `enabled` and Placement fit `anywhere` fails for some seeds in `patch-enemies`
  ("Randomizer event ID is already used: 11419980", a Bed of Chaos floor event registered twice); present before this change.
- **Tests:** new boss sampler tests, Seed Code extension-key tests, preset v10 migration.

**State-based run area, branding, Fog wording (2026-10-06; desktop 0.18.0, backend 0.31.0, contracts 0.13.0; generation profile ds1tr-gen-v1 unchanged).**
The run area now follows the state of the game (`deriveRunPhase`): original game shows Randomize and Load seed; a generated world shows
Apply to game with Copy seed secondary; an applied game shows Launch game with Restore original game secondary; an interrupted change
shows only Restore. One current message at a time (old success banners are cleared when the next action starts). Load seed generates
the shared world immediately (no "Generate this world" step); when the game is not original, restoring is the one thing asked, and the
world is created automatically afterwards. Run history is collapsed by default (`Run history  N  v`); a row shows time, status, a
shortened Seed Code and Items/Enemies/Bosses/Fog chips with Apply / Play (or Launch game), Copy seed and an overflow menu (View
settings, View spoiler log, Regenerate files, Delete from history). **Regenerate files** (formerly Recreate) refreshes the existing
history entry (same id, Seed Code and creation time) instead of adding one; so does loading a Seed Code that is already in the history.
The user-facing wording is Fog again ("Fog randomizer settings"); the master seed stays under Advanced. The packaged app has no
File/Edit/View/Window/Help menu bar. Branding: the approved icon (`apps/desktop/assets/branding/ds1tr-icon-source.png`) is converted by
`apps/desktop/scripts/make-icon.ps1` into `apps/desktop/build/icon.ico` (16, 24, 32, 48, 64, 128, 256 px) and used by electron-builder
and the window; a small decorative brand mark sat at the right of the header (withdrawn in 0.35.0; the interface has none now).

- **Packaged proof (0.18.0):** menu bar absent (window client area equals window minus frame, `GetMenu` = 0); window, taskbar and EXE
  icons are the DS1TR icon; the whole flow Randomize, Apply, Restore, restart (settings and history persist), Load seed (auto
  generation, same Seed Code, settings restored, history still 1 entry), Regenerate files (same entry), Load seed while applied
  (restore-and-continue) was driven through the UI.
- **Tests:** backend 360 (358 pass, 2 skipped), desktop 5; new tests for the phase logic, history refresh/delete and the UX contract.

**Seed Codes, generation profiles, history and launch (2026-10-06; backend 0.30.0, desktop 0.17.0, contracts 0.13.0; RANDOMIZER_VERSION 0.5.66 and FogHost 0.5.0 unchanged).**
A world is now named by a self-contained Seed Code that carries an immutable generation profile (`ds1tr-gen-v1`), the internal
master seed and the complete canonical settings; the app version is not an input of generation. New: Randomize / Load seed (the
master seed moved to Advanced), persistent settings and run history (Apply, Regenerate files without the run folder, Copy seed, View
settings), explicit Apply / Restore success messages and a Launch game button (Steam, executable fallback). Formats and rules:
`docs/SEED_FORMAT.md`, `docs/GENERATION_COMPATIBILITY.md`, `docs/DECISIONS.md`.

- **Golden fixtures:** `apps/backend/fixtures/golden/ds1tr-gen-v1.json`, 10 permanent cases recorded from a clean game copy (items;
  enemies classic; sampling-v1; items+enemies+bosses; +Fog; broad items with shops; custom pool; second Gargoyle; one-model
  grace; world options). Verified from the Seed Codes alone, also with a simulated application version (`99.1.0-future`):
  identical profile, settings, master seed and package hashes, 10/10.
- **Engines untouched:** no change in `engines/`, seed derivation or retry bounds; only the profile plumbing and drift guard.
- **Tests:** backend 354 (352 pass, 2 skipped: the pre-existing one and the golden regeneration that needs a clean game copy);
  new `seedCode`, `seedPersistence`, `generationProfiles`, `goldenCompatibility`, `seedUx`; desktop 5 (settings migration, launch game).

**World randomizer item tracking (2026-10-06; backend 0.29.1, desktop 0.16.1, FogHost 0.5.0, RANDOMIZER_VERSION 0.5.66).**
Fixed a real failure of the packaged app: with more item categories on, Fog stopped with `Couldn't find darkmoon_seance_ring in item
lots` after three identical combined attempts and the app said "try another seed". FogMod needs exactly one copy of each
required key item in the item lots it annotates and at most one of any; the global item shuffle put the Seance Ring and the
Peculiar Doll outside them (8% to 93% of the seeds depending on the categories) and put the shop copies of the Crest of
Artorias, Residence Key and Cast Light inside them. The shuffle now gives every source of a tracked key item a role (the one
copy: annotated lots only; extra copies: shops only) read from the verified `fog.txt`; nothing is duplicated and no item
category is blocked. `FOG_UPSTREAM_INCOMPATIBLE` is structural (no combined reseeding) with a plain message. Details and
evidence: `docs/FOG_ITEM_TRACKING.md`. Patch `0011`.

- **Real format:** 40 seeds, all item categories, bosses on, Fog on: 40/40 on the first attempt, 0 merge conflicts, same 15 overlay
  events, `verify-skips` clean, deterministic rerun, activation and restore; packaged 0.16.1 through its own UI: Randomize, Apply,
  Restore succeed.
- **Tests:** backend 327 (326 pass, 1 skipped); new `fogTrackedItems.test.mjs` (13) and pipeline tests.
- **Test harness note:** the Fog end-to-end script reconstructs the game from the live installation. Once the app has applied a run to
  the live game that copy is no longer clean (duplicate events in the boss-name patch), so the script can now take a clean copy
  (`DS1TR_E2E_GAME_SOURCE`, for example the app's pristine snapshot).

**User-friendly runtime / Fog bootstrap (2026-10-05; backend 0.29.0, desktop 0.16.0, contracts 0.12.0, FogHost 0.5.0).**
A player no longer picks a FogMod folder or installs .NET. The app finds Dark Souls Remastered through Steam, checks it,
imports the clean snapshot and prepares the world randomizer data itself: 19 of the 59 files are copied from the verified
clean snapshot, 40 are fetched once over HTTPS from the pinned FogMod commit (SHA-256 per file, size bound, temp folder,
whole-set verification, atomic install), after which no network is needed. FogHost and DsrDataTool are self-contained
win-x64 single-file executables. The main screen uses plain wording and readiness indicators; failures are phrased for
players and the exact reason stays in the log and manifest. Details: `docs/FOG_DATA_PROVENANCE.md`,
`docs/DESKTOP_PACKAGING.md`, `docs/DECISIONS.md`.

- **Proof through the packaged path (no `DS1TR_FOG_DATA_ROOT`):** a full Items + Enemies + Bosses + Fog proof with the
  self-contained helpers and an empty state folder (first-run download): generation, byte-identical rerun, activation,
  restore, live installation unchanged. The packaged 0.16.0 app, driven through its own UI on a clean copy of the game: game
  folder check, world data preparation, Randomize, Apply to game, Restore original game.
- **Fixed on the way:** the 0.15.0 portable executable could not start its backend (`@ds1tr/contracts` was not staged).
- **Tests:** backend 312 (311 pass, 1 skipped; new: world data bootstrap, game discovery, prerequisites, plain messages),
  desktop settings migration 2.

**Enemy placement Part C2: custom enemy pool for regular enemies (2026-10-05; contracts 0.12.0, backend 0.28.0, desktop 0.15.0).**
`sampling-v1` gains the custom enemy pool (**PARTIAL**: regular enemies; the original's boss and NPC entries need later
parts). A pool is a canonical sorted list of model ids (`null` = the full pool); it enters only through the candidate
boundary, restricts replacements only (unselected enemies stay where they are, including as the vanilla fallback) and
changes none of fit, Prevent same, Easy Asylum, Type replacement, drop ownership or determinism. Presets are version 9
(1-8 migrate to `permutation-v1` with the full pool, a stale id is an error). The backend lists the regular enemies from the
clean game data (`POST /api/enemy-pool`, cached per snapshot) and the desktop gets a filterable editor. Details: model
document 10.5. Patch `0010`.

- **Model budget (decided 2026-10-05):** nominal 30; the baseline rule `max(30, baseline)` is unchanged; a restricted pool may
  use a **one-model grace to 31** only on a map whose base cap is 30, when 30 is proven impossible and 31 proven sufficient.
  A baseline already above 30 is never raised and nothing above 31 is ever granted. Recorded per map (`nominalCap`,
  `minimumFeasibleModels`, `customPoolModelGrace`). 31 is not a new general limit.
- **Accounting fixes found by the 100-seed validation:** the pool never reached the coverage reserve; the reserve could pick a
  model too big for the smallest slot; the Type ledger could not serve the first group of a map that starts above its cap.
  A coverage witness (the fewest models that keep every participant servable) replaced the reserve of restricted pools;
  slots no pool model can replace join the baseline. The retry bound (3) was not changed.
- **Errors:** structural problems and, at replacement chance 100%, a map whose proven-minimal cover cannot fit even with the
  grace (`map-infeasible`) raise the non-retryable `ENEMY_PLACEMENT_CUSTOM_POOL_INVALID`; a participation draw that does not fit is the
  ordinary `ENEMY_PLACEMENT_INFEASIBLE` (at most 3 map attempts). No percentage or size heuristic rejects pools.
- **Catalog sweep (100 seeds x 18 maps, many pools):** A, B, C, E, N 100/100 in every tested configuration; D at "anywhere"
  100/100, D at "fits" 100% refused or served exactly where provable; 0 fit / Prevent-same / pool-membership / cap violations;
  the grace was used only on Parish and Lost Izalith and ended at 31 models; no baseline above 30 was increased.
- **Real format (Items + Enemies + Fog + bosses):** 50 seeds in 5 pool configurations + 3 full proofs: all pass, 0 merge
  conflicts, same 15 overlay events, `verify-skips` 0 failures, live installation unchanged.
- **Proofs:** `permutation-v1` unchanged (24 hashes, package files of combined-only and combined + Fog runs); the full pool
  equals Part C1 (synthetic pins and 700 real-catalog outputs, same `classTableHash` and retry counts).
- **Parish risk unchanged** (32-33-model baselines of 10.3 are separate and not worsened).
- **Tests:** backend 282 (281 pass, 1 skipped); new `enemyCustomPool.test.mjs` (28 tests); typecheck and desktop build pass.

**Enemy placement Part C1: Type replacement for regular enemies (2026-10-05; contracts 0.11.0, backend 0.27.0, desktop 0.14.0).**
`sampling-v1` gains the legacy Type replacement (**FULL** for regular enemies: disabled = legacy default, enabled, enabled
except roaming bosses = enabled until boss sources exist). The group is the original model within a map; it is decided once
(replacement chance, difficulty class, candidate) from its own stream `type:<map>:<model>` (attempt-aware), every member
slot gets the same replacement and draws only its variant; the candidate fits every member and differs from the
other groups of the map. The model budget is now reference counted for groups (`ModelLedger`: a replaced group frees its
original model). Presets are version 8 (1-7 migrate to `permutation-v1` with Type replacement disabled). Details: model
document 10.4. Patch `0009`. Custom pool (C2) is not started; the candidate pool is an injectable boundary.

- **Measured conflict and decision inside the task:** with strict legacy injectivity 93 of 100 seeds failed at 100% chance
  (the Parish: 15-16 fixed + 17 groups = 32 models, cap 30, deterministic, retries cannot help). Injectivity is now the last
  rung to yield (`shared model`, about 1.6 groups per seed at 100%, 0.01 at 60%, 0 at 25%); fit, budget and fixed entities
  are never relaxed. The retry budget (3) was not changed.
- **Proofs:** `permutation-v1` unchanged (24 hashes; 56/56 and 73/73 package files); with Type replacement off the output
  equals Part B (32 synthetic pins, and 700 real-catalog outputs with identical `classTableHash` and retry counts).
- **Catalog sweep:** 10 variants x 100 seeds, 0 final failures, 0 fit / Prevent-same / cap violations, retry pressure
  at or below Part B. **Real format:** 50 seeds (Items + Enemies + Fog + bosses) + 3 full proofs: all pass, 0 merge
  conflicts, same 15 overlay events, `verify-skips` 0 failures.
- **Parish risk unchanged** (not solved here): with Type replacement the Parish ends at 28-32 models; seeds `parish-26` (31),
  `parish-1` (32), `parish-11`/`parish-34` (map retry) in 10.4.
- **Tests:** backend 253 (252 pass, 1 skipped); new `enemyTypeReplacement.test.mjs` (24 tests); typecheck and desktop build pass.

**Enemy placement Part B: difficulty and map-local retries for regular enemies (2026-10-05; contracts 0.10.0, backend 0.26.0, desktop 0.13.0).**
`sampling-v1` gains Difficulty mode (curve, fully random, and both easy-Asylum modes: legacy default random +
easy Asylum) and Difficulty strictness (strict, a bit loose, very loose), both **FULL** for regular enemies. Classes are
derived by DS1TR (`regular-class-v2`: map progression tier and HP rank, in `classTableHash`), not copied; strictness
tables and the Easy Asylum cap are authored. A map that cannot be placed is replanned alone with attempt streams
(`attempt:<n>:slot:<id>`, at most 3 attempts; attempt 0 equals Part A), so the former whole-run retry for
`ENEMY_PLACEMENT_INFEASIBLE` is now almost never needed. Presets are version 7 (1-6 migrate to `permutation-v1` with
neutral difficulty). Details, eligibility matrix, filtering order and results: model document 10.3. Patch `0008`.

- **Proofs:** `permutation-v1` unchanged (24 hashes; 56/56 and 73/73 package files); 24 Part A synthetic hashes
  identical; real catalog: of 700 Part A outputs 681 are identical and the other 19 (the Part A first-attempt failures) are now solved by a map retry; none differs.
- **Catalog sweep:** 13 variants x 100 seeds: 0 final failures, 0 sampler overflows, 0 fit / Prevent-same / Easy
  Asylum violations. **Real format:** 50 seeds (Items + Enemies + Fog + bosses) + 3 full proofs incl. retried Parish
  maps: all pass, 0 merge conflicts, same 15 overlay events, `verify-skips` 0 failures.
- **Parish risk unchanged:** 31-33 models are reproducible with `parish-12` (31), `parish-11` (32), `parish-28`
  and `parish-92` (33) (config in 10.3); not tested in the game.
- **Tests:** backend 228 (227 pass, 1 skipped); new `enemyDifficulty.test.mjs` (18 tests); typecheck and desktop build pass.

**Enemy placement Part A: `sampling-v1` for regular enemy slots (2026-10-05; contracts 0.9.0, backend 0.25.0, desktop 0.12.0).**
`sampling-v1` is implemented and opt-in (UI: "Legacy-style sampling"); `permutation-v1` stays the default and its
output is unchanged (24 placement hashes; 56/56 and 73/73 package files). It samples regular enemy slots only;
bosses, units and NPCs are planned as before. Controls: Replacement chance (**FULL**, 0-100), Normal enemy
replacement (`none`, `normals`: PARTIAL), Placement fit (**FULL** for regular slots, 0 violations), Prevent same
(FULL). Presets are version 6 (1-5 migrate to `permutation-v1` with neutral settings). Engine:
`vendor/src/core/sampling.js` (patches `0006`, `0007`), class table `regular-size-v1` with `classTableHash`,
coverage reserve, fallback ladder, per-slot streams, slot-owned drops, summary/detail diagnostics.

- **Model budget (decided): non-worsening.** sampling-v1 uses a non-worsening model-budget policy. The normal target is 30 distinct models per map. If out-of-scope/fixed plans already require more than 30, that unavoidable baseline becomes the effective cap. The regular sampler may preserve or reduce that overflow but may not increase it. Details: model document 10.2, `docs/DECISIONS.md`.
- **Catalog validation:** 100 seeds x 17 variants (0/25/60/90/100% x fits/anywhere/Prevent same): 0 sampler
  overflows, 0 fit violations, 0 Prevent-same violations, deterministic. With `fits` at 25%/60% a first attempt
  fails in about 9%/5% of seeds with the retryable `ENEMY_PLACEMENT_INFEASIBLE` (zero headroom in the Parish).
- **Real-format validation:** 55 seeds (100%, 60%, 25%, Prevent same, mode `none`) through the full pipeline with
  Fog and bosses: 55/55 generated, 0 merge conflicts, boss overlay unchanged (15 events), `verify-skips` 0
  failures, byte-identical reruns, live install untouched.
- **Known risk:** the Parish can end at 32-33 models (baseline) when vanilla regular models are kept; not tested in-game.
- **Tests:** backend 210 (209 pass, 1 skipped); `enemySampling.test.mjs` (34 tests), preset v1-v6, mapper,
  bridge and pipeline classification tests; root typecheck and desktop build pass.

**Enemy placement Part 0: infrastructure only (2026-10-05; backend 0.23.0, contracts 0.8.0, desktop 0.11.0).**
Prepares the contract, backend, engine and tests for the future `sampling-v1` placement algorithm without
changing any placement. `permutation-v1` remains the default and the only algorithm that runs; the sampler is
**not implemented**, and selecting `sampling-v1` is refused with `ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE` at
validation, mapper, bridge and engine level (never a silent fallback). Presets are version 5
(`enemies.placement.algorithm`); versions 1 to 4 migrate to `permutation-v1`. Engine: new
`vendor/src/core/placement.js` (patch `0005`, listed in `UPSTREAM_PIN.md` and the module-set test), the config
key `enemyPlacementAlgorithm`, the dispatch `planExtractedEnemies`, per-slot RNG substreams
(`enemy-placement-v1`, `slot:<id>`), `ModelBudget` (cap 30, no per-map exception), diagnostics and
drop-ownership helpers. Model document: section 10.1.

- **Unchanged output (proof):** 24 placement hashes identical; two combined-only packages identical in all 56
  non-metadata files and the combined + Fog package identical in all 73 (metadata files embed the temporary
  path and gain only the new fields). `RANDOMIZER_VERSION` 0.5.66 and FogHost 0.5.0 are untouched; the
  portable EXE was not rebuilt.
- **Model budget:** checked on the real catalog; vanilla maximum 28 distinct models, none above 30, so the
  safety-first cap of 30 rejects no vanilla map.
- **Known and unchanged:** `permutation-v1` with Enemy scaling `vanilla` still duplicates single-owner
  equipment drops (model document 9.1); `sampling-v1` must always create a scaled row.
- **Tests:** backend 168 (167 pass, 1 skipped), including the new `enemyPlacementInfrastructure.test.mjs`
  (gating, RNG substreams, budget, diagnostics, drop ownership, permutation untouched), preset v1-v5 migration,
  mapper and bridge gating; root typecheck and desktop build pass.

**Enemy placement design (2026-10-05).** `docs/ENEMY_PLACEMENT_MODEL.md` reconstructs the legacy Enemy
Randomizer's per-slot placement model, documents the current permutation engine, compares four
architectures and fixes the target: a new `sampling-v1` planner beside the frozen `permutation-v1`
(`docs/DECISIONS.md`), with an incremental plan (batches 0, A-G) for Replacement chance, the replacement
modes, Boss chance, Placement fit, Difficulty mode and strictness, Type replacement, and the options that
build on them. **No runtime behavior, default, preset, FogHost code or packaging changed.**

- **Prototype** (`engines/combined/prototypes/enemy-placement-sampler.mjs`, scratch, not packaged: the packager
  copies only `engines/combined/vendor/src`): regular-enemy sampler on the real catalog, 1,703 slots, 40 seeds
  per variant, every run repeated for determinism. With a coverage reserve: 0 fit violations, 0 budget errors,
  0 non-deterministic runs, 6-8 ms per seed. Without it: 0.13% of placements end above their size class.
  Curve adherence inside a full model set is 30-31% under the literal legacy rule and 72-77% with the
  documented refinement.
- **Finding (current engine, not fixed):** with Enemy scaling `vanilla` and drops not randomized,
  `permutation-v1` duplicates single-owner equipment drops in 95 of 100 seeds (92 from the Titanite Demon
  group's Titanite Catch Pole; the rest from archetype-duplicated bosses: Pinwheel's masks, Taurus's axe,
  Super Ornstein's ring, Capra's weapon), because placements without a scaled row keep the replacement's own
  drops. Gwyn nerf and Second Gargoyle add about 0.05-0.10 lots per seed. The default `area` scaling shows none
  (0 of 100). None of FogMod's 27 tracked items is reachable through an NpcParam drop lot, so there is no Fog
  or progression impact. The new design makes "drops follow the slot" a rule (model document 6.9).
- **Derived classes** versus the legacy ones as a yardstick: size rank correlation 0.74 (79 of 82 within one
  class), difficulty 0.58 (73 of 82 within two).
- **Open product questions** are listed in section 13 of the model document (default algorithm, model budget
  definition, difficulty overrides, boss scaling, NPC allowlist).
- **Verification:** unchanged runtime, so no new runtime tests were needed; the prototype is not part of
  `npm test`. The earlier backend (152), FogHost (74), typecheck and desktop-build results stand.

**Replace second Gargoyle (2026-10-05; backend 0.22.0, contracts 0.7.0, desktop 0.10.0).**
The option now follows the legacy Enemy Randomizer: legacy default Replace; the second Bell Gargoyle
(`m10_01_00_00:c5350_0001`) is replaced by an independently drawn boss, or left alone with Do not
replace. A third value, Single boss, is the earlier DS1TR behavior and exists only so older presets
reproduce their output. Semantics: `docs/FEATURE_PARITY.md` 6a; policy: `docs/DECISIONS.md`;
patch `engines/combined/vendor/patches/0004-legacy-parity-second-gargoyle.patch`. Presets are version 4
(versions 1 to 3 migrate to Single boss). Parity is now 29 FULL of 54.

- **No FogHost change (FogHost stays 0.5.0).** The Batch 1 stop was based on a wrong premise: FogMod makes
  no edit of its own to events `11015396` and `11015382` (it copies the game directory's instructions
  into them), so the normal merge passes the combined forms through. The 15-event boss overlay is
  unchanged. Instruction-level comparison: `docs/FOGHOST_INTEGRATION.md`, "Second Gargoyle events".
- **FogHost tests:** 74/74 (68 existing + 6 new), run with a manually compiled test assembly because no
  .NET SDK is installed: pass-through of both events without the overlay, hold / 60% / release order,
  exact 15-event overlay set, five divergent FogMod forms refused (added control flow, missing anchor,
  duplicate anchor, added parameter, changed threshold), vanilla forms merge to nothing, and a real FogMod
  run writes both legacy forms out unchanged.
- **Engine checks (real catalog, in-process):** over 40 seeds Replace drew 16 distinct bosses, was
  deterministic, produced no duplicate slot, entity or scaled-NpcParam ids, kept the Parish model count
  at most 30, never drew a Bell Gargoyle, linked unit, excluded Gwyn or (with Chaos on) Pinwheel, left the
  third body and second tail disabled, and added no forced combat activation. Keep produced no placement for
  the body or its tail. Single boss (the engine default) leaves the 24 baseline placement hashes identical.
- **Real `patch-enemies` / `verify-skips`:** Replace, Keep and Single boss accepted, 0 skip mismatches.
  Event `11015396` with Replace holds the body (AI off, invincible), waits for 1010800 at 60%, releases
  it and renames the boss bar; with Keep it is byte-identical to vanilla.
- **Proof run** (generate, byte-identical rerun, activate, restore): all pass, and events `11015396`
  and `11015382` in the final Fog package are identical to the combined package's. Live install untouched.
- **Fog sweeps** (Items + Enemies + Bosses + Fog, live install unchanged, `verify-skips` on every attempt):

  | Option | Seeds | Success | Merge conflicts | Key-item retries | Skip checks | Mismatches |
  |---|---|---|---|---|---|---|
  | Replace | 20 | 20 | 0 | 0 of 20 seeds | 20 | 0 |
  | Do not replace | 8 | 8 | 0 | 2 of 8 seeds | 10 | 0 |
  | Single boss (regression) | 8 | 8 | 0 | 0 of 8 seeds | 8 | 0 |

  Boss overlays stayed on the 15 known events; neither Gargoyle event appears under merges or overlays.
- **Tests:** backend 152 passed, 0 failed, 1 skipped (needs a user Fog data root); root typecheck clean;
  desktop build passed. The portable EXE was not rebuilt.
- **Residual risk:** whether each portable boss's AI wakes correctly from the vanilla release (`set_ai(on)`,
  plus the combined engine's usual wake-up rows) cannot be checked without running the game.

**Legacy parity Batch 2 (2026-10-05; backend 0.21.0, contracts 0.6.0, desktop 0.9.0).**
Soul items (Shuffled and Replaced), Starting items, Pinwheel Chaos and Gwyn nerf are functional end
to end with their legacy defaults (soul items shuffled, starting items Shield + 1H/2H, Pinwheel Chaos
off, Gwyn nerf Medium). Semantics, deviations and legacy references: `docs/FEATURE_PARITY.md` 6b;
policy: `docs/DECISIONS.md`; patch: `engines/combined/vendor/patches/0003-legacy-parity-batch-2.patch`.
Presets are now version 3 (versions 1 and 2 migrate to the behavior-preserving values).

- **Not implemented:** Soul items *Transposed* (boss souls are in none of the catalog's pools; shown
  as a disabled choice), Replace second Gargoyle (still blocked, see Batch 1) and the hintsheet.
- **Include DLC was a dead control** (found by the trace: no backend mapping, no engine reader, no
  FogMod switch). The backend now rejects `includeDlc: false`; the UI shows it as a disabled planned
  control. A real implementation is medium work and cannot cover Fog.
- **A bug the Fog sweep caught:** a first version of the Replaced-souls pool included the unique
  Peculiar Doll, producing `Item peculiar_doll found in both ...` (FogMod tracks 27 key items by id)
  in 10 of 10 seeds. The pool now only holds goods that occur in at least two lots; none of the
  pool's 31 goods is among FogMod's key items.
- **Engine defaults unchanged:** the 24 Batch 1 placement hashes are still identical.
- **Real-catalog checks (in-process):** Replaced souls removed all 3380 lesser souls over the runs and
  kept Fire Keeper Souls; shield starting modes gave shields in every class with unique pickups and
  no assignment failures over 180 seeds per mode; Gwyn appears in about 18% (strong) and 40%
  (medium) of 100 seeds; Pinwheel Chaos replaced exactly the 14 clones with the Catacombs model count
  at most 30 and the main Pinwheel untouched.
- **Real `patch-enemies` / `verify-skips`** (live install, scratch output): all options accepted,
  0 skip mismatches. Pinwheel Chaos edits fewer events in `m13_00` than the default
  (`11305392/396/398/250` are no longer edited); `11305100` is never edited.
- **Fog sweeps** (live install unchanged): product defaults 20/20 (0 conflicts, 21 skip checks, 0
  mismatches, 1 retry in 21 attempts); every Batch 1 and Batch 2 option non-default 12/12 (0
  conflicts, 15 skip checks, 0 mismatches, 2 seeds needed a combined retry). Boss overlays stayed on
  the 15 whitelisted events.
- **Tests:** backend 144 passed, 0 failed, 1 skipped (needs a user Fog data root); new engine tests
  cover every option on synthetic catalogs; root typecheck clean; desktop build passed. The portable
  EXE was not rebuilt.
- **Remaining:** see the gaps in `docs/FEATURE_PARITY.md` section 6.

**Legacy parity Batch 1 (2026-10-05; backend 0.20.0, contracts 0.5.0, desktop 0.8.0).**
Four legacy options are now functional end to end, each with its original legacy default:
Fashion Souls (on), Replace mimics (on), Replace respawning mosquitoes (on) and Prevent same
enemy (off). The vendored core was extended (`patches/0002-legacy-parity-batch-1.patch`);
semantics, deviations and the legacy source references are in `docs/FEATURE_PARITY.md`
section 6a. Presets are now version 2; version 1 presets migrate with the legacy defaults.

- **Not implemented:** Replace second Gargoyle (stop condition: it would edit events
  `11015382` and `11015396`, which FogMod rewrites and which are outside the 15-event boss
  overlay whitelist) and the hintsheet (it reports shuffled key locations, which DS1TR does
  not produce). Both remain documented as UI-only / partial. The portable EXE was not rebuilt.
- **Default output unchanged:** 24 placement hashes (three configurations x eight seeds) from
  the real-format catalog are identical before and after the change.
- **Real-catalog checks (in-process, 60 seeds):** prevent-same left 0 same-model regular
  placements (about 47 per seed before) with at most 30 models per map and deterministic
  output; excluded mimic and mosquito slots were never placed; with Fashion Souls off, all 31
  regular armor homes held a complete, distinct vanilla set in each of 40 seeds.
- **Targeted Bosses + Fog sweep** (5 seeds, mimics off, mosquitoes off, prevent-same on,
  Fashion Souls off): 5/5 succeeded; 0 merge conflicts; boss overlays applied only to the 15
  whitelisted events; 9 combined-package skip checks, 0 mismatches; no Fog/combined event
  mismatches; live installation unchanged. 3 of 5 seeds needed a combined key-item retry
  (4 of 9 attempts), within the range recorded earlier (10 of 50); none exhausted the budget.
  No full 40-seed sweep was run because defaults are unchanged.
- **Tests:** backend 124 passed, 0 failed, 1 skipped (needs a user Fog data root); new
  `combinedLegacyParity.test.mjs` covers the engine on synthetic catalogs (no game data is
  committed); root typecheck clean; desktop build passed. Two runs immediately after the
  sweep hit the integration test's 10 s server-start timeout; four later runs were clean.
- **Remaining:** see the engine gaps in `docs/FEATURE_PARITY.md` section 6; the next batch
  should be the A-B items (Soul items, Starting items, Pinwheel Chaos, Gwyn nerf).

**Feature-parity audit and settings presets (2026-10-05).** `docs/FEATURE_PARITY.md` audits
54 original options (Item 11, Enemy 27, FogMod 16) plus 16 combined-engine native options.
FogMod parity is complete. 21 original options are FULL, 30 are UI-only placeholders that
need new engine behavior, 1 is partial (hintsheet), 1 is intentionally unsupported (PTDE)
and 1 is a duplicate (copy enemy placement). The vendored combined engine reads none of the
legacy Item/Enemy options, so no `PLANNED EXTENSION` control became functional and none was
faked.

- **Newly wired:** settings preset import/export (`/api/preset/*`, backend 0.19.0, contracts
  0.4.0, desktop 0.7.0) and a "Restore default settings" button.
- **Verification:** root typecheck clean; backend tests 110 passed, 1 skipped (needs a user
  Fog data root); desktop build passed. No engine or real-data sweeps were run because no
  engine behavior changed.
- **Remaining:** engine gaps G1-G23 in `docs/FEATURE_PARITY.md`, classed A/B/C by effort and
  Fog risk. The portable executable has not been rebuilt for desktop 0.7.0.

**Windows desktop packaging (2026-09-29):** Electron now starts and stops the backend,
resolves bundled runtime files, stores mutable data outside the app directory, and
offers a FogMod data-root picker. Desktop 0.6.1 and backend 0.18.0. A portable x64
artifact is built at `apps/desktop/release/DS1-Triple-Randomizer-0.6.1-x64.exe`.
It bundles DsrDataTool, FogHost, the combined JS core and license notices; FogMod's
game-derived DS1R data is excluded. See `docs/DESKTOP_PACKAGING.md` for the layout,
build command and .NET 8 runtime prerequisite. Packaged-runtime API/tool smoke checks
passed on staging data. The final portable executable launched outside the restricted
sandbox: the renderer showed `Backend online` and first-run DSR/Fog guidance; health
returned 200 with backend 0.18.0; a missing DSR path returned an explicit problem;
the plan reported the combined engine and FogHost ready. Closing the window stopped
the portable wrapper, Electron main process and backend child. The sandboxed preload
had to compile as CommonJS (`preload.cts`); desktop 0.6.1 includes that fix. No public
GPL fork or release tag exists yet.

- Verification: root typecheck clean; backend tests 108 passed, 1 skipped (requires a
  user Fog data root); desktop, backend, DsrDataTool and FogHost builds passed;
  `package:win` completed; production-dependency audit found 0 advisories.
- Portable smoke: 0.6.1 executable launched from `release/`, renderer preload
  connected, backend health returned 200, missing DSR returned
  `DarkSoulsRemastered.exe not found.`, both packaged tools were reported ready,
  first-run DSR/Fog guidance was visible, and normal window close ended the wrapper,
  Electron main and backend child. Bundled DsrDataTool and FogHost executables ran
  and printed their usage messages. A staged-data run without a Fog root returned
  `FOG_PREREQUISITES_MISSING` before randomization. No live game files were touched.
- Release prerequisites: install .NET 8 runtime on the user machine; supply FogMod
  DS1R data locally; create the public GPL corresponding-source fork and matching
  release tag. Code signing and a custom icon are not configured.


**Boss randomization + Fog works (FogHost 0.5.0, backend 0.17.0, desktop 0.5.0,
2026-09-29).** The `FOG_BOSS_RANDOMIZATION_UNSUPPORTED` rejection is removed, and bosses
are on by default again.

- **Mechanism.** A whitelisted boss-event overlay: FogMod's event is the control-flow base,
  and the combined engine's known boss mutations are re-applied by content. It covers the
  15 boss events both engines edit. Details: `docs/FOGHOST_INTEGRATION.md`,
  `docs/DECISIONS.md`.
- **Also:**
  - FogMod patch 8 (Asylum Demon removal keeps skips consistent);
  - merge refinements: unambiguous-result alignments, skip range = covered lines, and
    event-0 initialization removal;
  - refused events log their base, combined and FogMod versions.
- **Verification:**
  - FogHost 68/68;
  - backend 109/109;
  - typecheck clean;
  - Items + Enemies + Bosses + Fog 40/40 (no conflicts, invariants pass, all skip checks
    clean);
  - 3 full proofs with byte-identical rerun and activation/restore;
  - regressions: Items + Enemies + Fog 5/5 and Items + Fog 5/5.
- **Remaining:**
  - the key-item combined-retry rate with bosses + Fog is 10 of 50 attempts, and no seed
    has exhausted the budget, so the budget is unchanged;
  - the overlay whitelist is fixed to the 15 measured events, so any other boss event
    edited by both engines stays a `FOG_MERGE_CONFLICT`;
  - the FogHost and DsrDataTool test consoles run outside `npm test`;
  - there is no public GPL fork yet;
  - desktop packaging has not started.

FogHost 0.3.1 (2026-09-29): FogMod's boss-trigger rewrite now keeps enclosing DS1 SKIP
counts consistent (vendored patch 7, `tools/fog-host/src/SkipRanges.cs`).

- **Verification:**
  - FogHost self-tests 52/52, including 5 new ones;
  - each of the 11 previously broken FogMod-only events (`11015393`, `11205383`,
    `11215002/3/12/13`, `11405393`, `11415343/383`, `11505393`, `11515393`) checked
    explicitly: 0 mismatches.
- **40-seed sweeps** (live install unchanged):
  - Items + Enemies + Fog 40/40;
  - Items + Fog 40/40.

  Neither had merge conflicts or skip mismatches, in combined packages or in final Fog
  packages (checked against FogMod's templates). Key-item retries are unchanged: 7 of 47
  and 8 of 48 attempts, and no seed exhausted the budget.
- **Remaining:**
  - bosses + Fog is still rejected;
  - the Asylum `11810310` FogMod removal is not skip-adjusted, but it runs only for
    bosses + Fog;
  - the FogHost and DsrDataTool test consoles run outside `npm test`;
  - there is no public GPL fork yet.

FogHost 0.3.0 and backend 0.16.2 (2026-09-29). **Items + Enemies + Fog works on real-format
data: 40/40 seeds.**

- **Changes:**
  - derived SKIP-count adjustments are accepted by the merge under strict conditions;
  - the Fog bridge now reads `randomizer.json` `placements` (the preserved-row list had
    been empty in production);
  - the model-declaration invariant false positive is fixed.
- **Verification:**
  - FogHost self-tests 47/47;
  - DsrDataTool tests 14/14;
  - backend 109/109 with the data root;
  - typecheck clean.
- **Sweep** (`fogHostE2e.mjs`, 40 seeds; live install unchanged):
  - 40/40 succeed, with post-write invariants passing;
  - 0 merge conflicts;
  - combined `verify-skips` 47/47;
  - final Fog packages have 0 skip mismatches in combined or merged events;
  - key items fell outside annotated lots on 7 of 47 attempts (5 seeds retried, none
    exhausted).
- **Remaining:**
  - 11 FogMod-only boss-trigger events have skip ranges broken by stock FogMod's own
    runtime rewrite. This is an upstream observation, not fixed here.
  - Bosses + Fog is still rejected.
  - The FogHost and DsrDataTool test consoles run outside `npm test`.
  - There is no public GPL fork yet.

DsrDataTool skip-count fix (2026-09-28; backend 0.16.1; user-authorized local vendored
modification). The combined engine now keeps DS1 "skip N lines" counts consistent when it
removes or inserts event instructions. Details: `docs/DECISIONS.md`.

- **Tests:**
  - DsrDataTool tests 14/14 (`engines/combined/tests/DsrDataTool.Tests`);
  - FogHost 37/37;
  - backend 108/108;
  - typecheck clean.
- **Real-format 40-seed sweeps**, with every combined package checked by `verify-skips`
  (live install unchanged):

  | Configuration | Result |
  |---|---|
  | Combined-only, Items + Enemies + Bosses | **40/40**; 40/40 packages with 0 skip mismatches (about 134 skips adjusted per package) |
  | Items + Fog | **40/40**; 48/48 skip checks pass; key-item retries on 8 of 48 attempts |
  | Items + Enemies + Fog | **0/40**; 47/47 skip checks pass (38 skips adjusted per package) |

  The Items + Enemies + Fog failures are FogHost merge refusals, not engine errors:
  - `11505392`: rule 4, because the combined engine now correctly changes a SKIP count
    (40 seeds);
  - `11600400`: the adjacency rule (39 seeds).
- **Open decision:** whether rule 4 may accept a skip-count change that is exactly
  explained by the same side's own edits inside that range.

FogHost 0.2.0 (2026-09-28): instruction-level EMEVD merge (user option B), a FogMod
scaling crash fix (patch 6), and boss randomization off by default on the desktop
(desktop 0.4.1).

- **Merge policy:** `docs/FOGHOST_INTEGRATION.md` rule 3 and `docs/DECISIONS.md`.
- **Verification:**
  - FogHost self-tests 37/37 (15 new merge tests, including the `11505392` pattern);
  - backend 108/108;
  - root typecheck clean.
- **Real-format sweeps** (`fogHostE2e.mjs`, `DS1TR_E2E_SWEEP=40`; live install
  unchanged):

  | Configuration | Result |
  |---|---|
  | Items + Fog | **40/40**. Key items fell outside annotated lots on 8 of 48 combined attempts (16.7%); 6 of 40 seeds needed a combined retry; none exhausted the budget. |
  | Items + regular enemies + Fog | **0/40**. 39 are `FOG_MERGE_CONFLICT`: `m15_00_00_00:11505392` in 39 seeds and `m16_00_00_00:11600400` in 38. `m15_00_00_00:11500001` merged at instruction level in 39 seeds. Key items fell outside annotated lots on 7 of 47 attempts (14.9%); 5 seeds needed a combined retry. One transient `DATA_TOOL_FAILED` did not reproduce on rerun. |

  Before patch 6, 11 of 40 enemy seeds crashed with `FOG_ENGINE_FAILED`.
- **Blocker:** both remaining conflicts are caused by the combined engine.
  `DsrDataTool.RemoveEventInstruction` deletes instructions inside SKIP ranges without
  adjusting the line count. One package had 38 such skips in 21 events, and this
  affects combined-only packages too. FogHost refuses to merge them by design. Fixing it
  needs a change to the vendored `DsrDataTool`, which is a user decision.

FogHost v1 (2026-09-28): `tools/fog-host` (FogHost 0.1.0, .NET 8) is built and verified;
`@ds1tr/backend` is 0.16.0.

- **Engine.** FogMod is re-pinned to `6e6dc78`, because `28711c3` cannot run DS1R. The
  host uses SoulsIds `e31a6fd` and `JuicerMV.SoulsFormats` 0.1.3. `GameDataWriter.cs`
  carries a documented patch set: API port, no backups, deferred writes, preserved
  NpcParam rows, and an EMEVD merge hook.
- **Host behavior:**
  - the schema-v2 contract is unchanged;
  - DS1R only, with `dryRun` and `write` modes, and writes only to staging;
  - deterministic output, SHA-256 output list;
  - three-way EMEVD merge;
  - in-memory and post-write invariants;
  - typed failures.
- **Data root.** The FogMod data root is supplied by the user (`DS1TR_FOG_DATA_ROOT`),
  never bundled. It is verified against a 59-file SHA-256 manifest.
- **Backend changes:**
  - a new default `getFogPrerequisites` that receives the Fog language (fixes the
    staged `msg/<language>` file);
  - boss randomization + Fog is rejected with `FOG_BOSS_RANDOMIZATION_UNSUPPORTED`.
- **Verification:**
  - FogHost self-tests: 22/22 with a data root, 12 passed + 10 skipped without one;
  - backend `npm test`: 108/108;
  - root typecheck clean;
  - real-format proof `apps/backend/scripts/fogHostE2e.mjs`:
    - Items + Fog passes 7/7 seeds, including Fog package activation and restore on
      a temp copy (live install unchanged);
    - **regular enemies + Fog fails 5/5**: four seeds hit `FOG_MERGE_CONFLICT` on
      `m15_00_00_00` event `11505392` (both engines edit it), and one seed exhausted
      the key-item retries.
- **Remaining:**
  - enemies + Fog needs an instruction-level merge or a product rule (user
    decision);
  - the desktop default (bosses on, Fog on) is now rejected by design;
  - the FogHost self-tests are not part of `npm test`;
  - no public GPL fork yet.

  Details: `docs/FOGHOST_INTEGRATION.md`, "FogHost v1 implementation".

FogHost preparation (2026-09-28, docs only, no code): the implementation
handoff is `docs/FOGHOST_IMPLEMENTATION_CHECKLIST.md`. It covers:
- the exact pins: FogMod `28711c3`, SoulsIds `e31a6fd` (Apache-2.0, verified),
  and the SoulsFormats candidates;
- the 60-file FogMod data root;
- the project layout, build commands and test plan;
- the `GameDataWriter.cs` patch points.

A throwaway build probe (outside the repo) showed that the audited FogMod
pin `28711c3` cannot generate DS1R (upstream DS3 regression). FogMod
`6e6dc78` + SoulsIds `e31a6fd` + NuGet `JuicerMV.SoulsFormats` 0.1.3, with a
six-rule API port of `GameDataWriter.cs`, builds on net8.0 and runs a
deterministic DS1R dry run and write. Source reading closed two open design
items. FogMod never removes enemy
parts. FogMod `scale` skips non-vanilla NpcParam rows and re-points
`NPCParamID` to clone rows, so the Task 11 "composed scaling" rule and
invariants 1 and 3 need a user decision before the host is built. No plan
task builds FogHost yet.

Task 16 is the final end-to-end audit. `@ds1tr/backend` is 0.15.1.

Fixed:
- **Pristine snapshot omitted `paramdef/`.** The snapshot is the only
  generation source, but `managedRoots` lacked `paramdef`. Without
  `paramdef/paramdef.paramdefbnd.dcx`, DsrDataTool `scan` silently produces
  a catalog with no gifts, drop lots, world lots, starting classes or enemy
  metadata. `patch-enemies` then fails reading it. Task 8 missed this because
  its hand-built copy included `paramdef/`. `paramdef` is now a managed root,
  and a gameState test pins the read-set roots. No snapshots existed yet, so
  no migration is needed.
- `npm test` failed when run from `apps/backend` (npm's cwd). The API
  integration test resolved `apps/backend` from cwd, and Node 24 crashed
  natively on the failed spawn. It now resolves relative to the test file.
- Manifest `appVersion` and `/api/health` report 0.15.1.

Verified (2026-09-28):
- Backend `npm test`: 103/103.
- `apps/backend/scripts/releaseAuditE2e.mjs` (not part of `npm test`; needs
  the live install, built DsrDataTool and .NET 8) passed 18/18. The live
  install was only read. A temp reconstruction of its managed roots (legacy
  `.bak` moved over originals, as in Task 8) went through the production
  path with a temp state root:
  - `importPristine`: 198 files, including paramdef;
  - `generateRun`: combined Items + Enemies with all item options, bosses and
    area scaling, Fog off, 54 patched files including GameParam;
  - a second run with the same seed is byte-identical in `mod/`;
  - no write into the game during generation;
  - `activate`: state `active`, and a second activation is refused;
  - `restore`: the game matches the reconstruction and the pristine store
    still verifies;
  - the live install is byte-identical afterwards.
- Manifest: records the master seed, per-attempt derived seeds and results,
  combined engine version, validation, file hashes, snapshot binding and
  appVersion.
- Licensing: no legacy Python engine code and no FogMod sources are
  tracked. The only vendored code is `kellernz1/ds-randomizer`
  (GPL-3.0-only) with notices. SoulsFormats (GPL-3.0-only, NuGet) is noted in
  both notice files. No game data is tracked.
- Retries are bounded and deterministic, and are recorded per attempt (unit
  tests). Combined retries are exercised only on `GENERATION_FAILED`.

Not verified / release blockers (not fixed here; outside a correctness fix):
- **Fog is not functional.** FogHost is unbuilt and no data root exists.
  No planned task builds it (the Task 12 prerequisite has no numbered task),
  so Task 17's FogHost availability check cannot pass until one is added.
  Fog-enabled generation always returns `FOG_PREREQUISITES_MISSING`. The
  combined -> Fog bridge, Fog retries, the Fog package layout
  (`gameTreeRoot: ''`) and Fog activation have never run. When FogHost is
  built, its read set must fall within the snapshot roots, or staging fails
  closed. The desktop default config enables Fog, so a default run fails
  until then.
- **No in-app recovery from `modified`.** `/api/game/restore` does not expose
  `overwriteUnrecordedChanges`, and the desktop enables restore only for
  `active`/`interrupted`. A Steam file verification leaves stray files in
  managed roots, so the state stays `modified`.
- **No re-import after a game update.** `version-changed` refuses restore,
  and `importPristine` refuses with `SNAPSHOT_EXISTS`. The only way out is
  deleting the state directory by hand.
- Provenance remains "no legacy artifacts", not "proven vanilla" (no
  reference hashes). The real-data proof used reconstructed, not
  verified-vanilla, data.
- Distribution: a public GPL fork with source tags, and FogMod `dist/` data
  licensing, are unresolved (`docs/LICENSING.md`).
- `engines/item/README.md` and `engines/enemy/README.md` still describe
  legacy vendor probes that the backend no longer uses. These are docs only.

Task 15 completes the desktop controls for the supported combined and Fog
options. The run panel now shows the active game package, generation stage and
percentage, logs, failures, and a persistent generated-package summary. A
generated package can be activated only while the current settings still match
the settings used to generate it; changing options also clears a stale plan
preview. Empty seeds and a configuration with no enabled engine cannot start
generation. Desktop version 0.4.0. Desktop build and typecheck passed
(2026-09-28). Fog-enabled generation remains dependent on an unbuilt FogHost;
Task 16 owns the final end-to-end audit.

Task 14 connects the existing backend pipeline and game-state authority to
the desktop application. The backend exposes state, pristine import, streamed
generation, activation, and vanilla restore endpoints. The desktop sends the
typed configuration, displays progress/logs/errors and the generated package,
and enables activation or restore according to the reported game state. A
backend-owned run directory keeps workspace paths out of the renderer. The
new mutation routes reject ordinary foreign browser origins; packaged file
origin remains accepted. Backend 0.15.0, desktop and contracts 0.3.0.
Focused API integration (1/1), pipeline (4/4), backend/desktop builds and
desktop typecheck passed (2026-09-28). FogHost is still unbuilt, so Fog-enabled
generation reports `FOG_PREREQUISITES_MISSING` until that prerequisite exists.
Desktop packaging/startup and full UI polish remain later tasks.

Task 13 adds the single activation/restore authority in
`apps/backend/src/core/gameState.ts` (see `docs/DECISIONS.md`, "One
backend-owned activation/restore authority"). It covers the following:

- a hash-verified pristine snapshot import that refuses legacy `.bak` chains;
- state detection;
- journaled, atomic, verified activation of a generated package;
- automatic rollback;
- vanilla restore.

Destructive operations refuse unknown, stale or running-game states. The
snapshot is now the pipeline's trusted pristine source: `server.ts` injects
`getPristineSource`. The run manifest records `pristineSnapshotId` and
`gameTreeRoot`. No HTTP/IPC endpoints for import/activate/restore exist yet;
that wiring is Task 14. Reference vanilla hashes do not exist, so provenance
is "no legacy artifacts", not "proven vanilla". Backend `npm test` passed
102/102, including 15 focused authority tests (2026-09-28). `@ds1tr/backend`
is 0.14.0.

Task 12 wires the backend generation sequence in
`apps/backend/src/core/generationPipeline.ts`: DSR verification, a verified
pristine-source gate, combined generation in an isolated workspace, output
hash checks, Fog staging and bounded deterministic retries, final output
checks, a package manifest, and typed progress/log events. `/api/randomize`
accepts the generation request and can stream events with
`Accept: text/event-stream`. It never activates files into the game.

The default pristine-source and Fog-prerequisite providers intentionally
return typed `PRISTINE_IMPORT_REQUIRED` and `FOG_PREREQUISITES_MISSING`
failures. No backend pristine authority or built FogHost/data root exists yet;
supplying an arbitrary path is not proof of clean game data. The pipeline can
be exercised with trusted injected providers, but production generation
requires the two separately recorded prerequisite tasks in
`docs/AI_EXECUTION_PLAN.md`. Combined package checks currently cover the
shared patched files described by the bridge; Fog output checks cover files
declared in the host response. Focused pipeline tests and backend build and
typecheck passed (2026-09-28). `@ds1tr/backend` is 0.13.0.

Task 11 defined and implemented the backend side of the Combined -> Fog
compatibility bridge:

- `apps/backend/src/adapters/fogCompatibility.ts` (pure, no I/O):
  - `buildFogCompatibility` derives the schema-v2 `compatibility` object
    from `patch-manifest.json` and the placements file;
  - `fogReadSet` and `planFogStaging` plan the per-attempt staging and
    merge-base directories;
  - `decideFogRetry` is the bounded, deterministic retry table.
- `seeds.ts` gained `deriveAttemptSeeds`; attempt 0 equals `deriveSeeds`.
- `fogHostProcess.ts` moved to schema v2 and validates `compatibility`. It
  also accepts the new codes `FOG_MERGE_CONFLICT` and
  `FOG_COMPATIBILITY_VIOLATED`.

The host-side rules are specified in `docs/FOGHOST_INTEGRATION.md`
("Compatibility bridge"). They cover the three-way EMEVD merge, preserved
NpcParam rows, stock key-item handling and post-write invariants. Fog
scaling and combined scaling compose (decision in `docs/DECISIONS.md`).

Verification: backend tests 83/83 pass, including the build (2026-09-28).
`@ds1tr/backend` is 0.12.0.

Still open:
- the C# host is unbuilt, so none of the host rules is compiled or tested;
- boss randomization together with Fog is unproven (`11810310` conflict);
- the key-item retry rate is unmeasured;
- FogMod's `scale` mechanism is unread, so scaling composition is a
  hypothesis (open item 8).

Task 10 implemented the backend FogHost process adapter in
`apps/backend/src/adapters/fogHostProcess.ts`. It writes a typed schema-v1
request to a fresh JSON path, launches `FogHost.exe generate`, reads the
authoritative response, forwards structured progress and plain log text,
and reports host crashes and timeouts as non-retryable typed failures. It
rejects stale response paths and invalid request options before launch. The
Fog status probe now points to the planned `tools/fog-host` project and
executable. Seven focused tests, backend build, and backend typecheck passed
(2026-09-28). `@ds1tr/backend` is 0.11.0. The C# host is still unbuilt;
Task 11 owns compatibility inputs and Task 12 owns pipeline wiring.

Task 9 designed the FogHost integration (`docs/FOGHOST_INTEGRATION.md`;
decision in `docs/DECISIONS.md`). It is docs only: no code changed and there
is no version bump. The design is a console host that compiles FogMod's
engine sources at the audited commit `28711c3`, never FogMod's CLI (which
forces DS3) or WinForms. It forces DS1R and exchanges JSON request/response
files. FogMod writes DS1 output in place, so each attempt gets a fresh staging
directory, and the host cleans up FogMod's `.bak` files. Errors are typed and
retry-scoped (`combined` vs `fog`). The overlap analysis shows stock FogMod is
lossy on combined output:
- it replaces EMEVD and keeps only legacy-enemy-randomizer event IDs;
- it deletes the combined engine's scaled `NpcParam` rows;
- it finds key items only in annotated item lots.

FogHost therefore carries a minimal patch set in `GameDataWriter.cs` and
`ItemReader.cs`, and Task 11 defines the bridge inputs. Open items:
- no task builds or vendors the C# host yet;
- FogMod's `dist/` data is game-derived and its distribution is unresolved;
- the SoulsFormats/SoulsIds pins and SoulsIds' license are still to be checked.

Task 8 ran the combined Items + Enemies engine end to end for the first time:
typed request → config → vendored JS generator → real `DsrDataTool`
`scan`/`patch-enemies` → workspace package, via
`apps/backend/scripts/combinedEngineE2eProof.mjs` (harness commit
`75c214dc2656693b49c2861c39f3f2a7e6b8e7a8`; not part of `npm test`). The live Steam install is
legacy-modified (legacy item/enemy randomizers and FogMod applied in place);
against it `patch-enemies` correctly refuses (`DATA_TOOL_FAILED`, enemy model
declaration pruning), and the install stayed byte-identical. On a temp copy
of the tool's read set with the legacy `.bak` files restored, all 47 checks
passed (2026-09-28): generation completes, the package's 54 patched files
match `patch-manifest.json`, reruns are byte-identical apart from embedded
workspace paths and timestamps, real catalogs verify and reuse, failures are
typed and recoverable in the same workspace, and neither game directory
changed. This proves the pipeline on real-format data, not on verified
vanilla data. Findings handed to later tasks (up-front detection of
legacy-modified installs, partial seed directories after a failed patch,
missing dll reported as `DATA_TOOL_FAILED`, volatile JSON fields) are in
`docs/COMBINED_ENGINE_INTEGRATION.md` ("End-to-end generation proof").
No application code changed; `@ds1tr/backend` stays `0.10.0`.

Task 7 connected `CombinedRandomizerAdapter.generate()` to the existing
workspace-only execution bridge. It forwards the typed request and
progress/log events, returns the generated seed package directory on success,
and preserves typed bridge failures including retryability. Two focused
adapter tests passed; backend build and typecheck passed (2026-09-28).
`@ds1tr/backend` is `0.10.0`. Implementation commit:
`57d2f846ca362b91673ffcaf3ae025802e5efb64`. The adapter has not yet
run `DsrDataTool` against a real DSR installation (Task 8); generation is
not connected to activation.

Task 6 resolved the remaining combined-engine blockers that had to be fixed
before a real generation run can be attempted: the combined seed is now a
32-hex-character SHA-256 derivation of the master seed that always satisfies
upstream's charset (Blocker 2); `CombinedRandomizerRequest` gained
`generateSpoilerLog`, mapped straight through instead of pinned off
(Blocker 3); and a reused workspace catalog is verified file-by-file (size +
SHA-256) against the game directory before generation, failing with
`CATALOG_SOURCE_MISMATCH` and never rescanning automatically (Blocker 6).
Backend tests 66/66 pass and the full workspace typecheck is clean
(2026-09-28). `@ds1tr/backend` is `0.9.0`. Implementation commit:
`e2664164d1aa1131952e1ddb1a19a87d960198af`. The user confirmed the seed
derivation (option A); `ARCHITECTURE.md`, `source-analysis.md`, and
`CODEX_PROGRESS.md` now mark the earlier pass-through direction superseded.
Task 7 completed adapter wiring. Still open: a real `DsrDataTool` run
(Task 8), the public GPL fork.

Task 5 extended the focused combined bridge tests (commit
`abeb4c2f56ade130a39e4d42837bf333d586e3e0`); no production code changed.

## Backend adapters

- The minimal `CombinedRandomizerAdapter` boundary is defined for combined item
  and enemy generation requests, progress/log events, and typed results.
- The adapter delegates generation to the workspace-only execution bridge and
  maps its package directory and typed failures to the public adapter result.
- The adapter is not yet invoked by the application pipeline. When called, it
  delegates workspace generation to the bridge; activation remains separate.
- A pure, deterministic mapping layer converts combined requests to the
  upstream item/enemy configuration shape, applies parent `enabled` flags, and
  rejects requests with no enabled randomization category. Task 3 verified
  all required item/enemy combinations, parent gating, deterministic output,
  and the case where only safeguard options are selected. Six focused mapper
  tests and backend build/typecheck passed on 2026-09-28. No generation,
  filesystem, subprocess, or game access was added.
- A pure, deterministic workspace planner
  (`combinedRandomizerWorkspace.ts`) computes the catalog path, per-seed
  output directory, and `scan`/`patch-enemies` argument vectors for a
  generation run, and refuses to plan a workspace located inside the live
  game directory. It performs no filesystem I/O and never constructs
  `install`/`restore` arguments. See
  `docs/COMBINED_ENGINE_INTEGRATION.md` ("Workspace execution proof").
- `kellernz1/ds-randomizer` is selectively vendored (the files needed for
  `DsrDataTool.dll`, plus the minimal JavaScript generation core) and pinned
  at commit
  `f120efe0e95b957b7e76109868c3c9966e22720c` under
  `engines/combined/vendor/` (`UPSTREAM_PIN.md` has the full file list and
  update procedure). `DsrDataTool.dll` builds successfully from this
  vendor directory with a locally-installed .NET 8 SDK; see
  `docs/COMBINED_ENGINE_INTEGRATION.md` ("Vendored build proof") for the
  exact command and artifact path.
- `DsrDataToolExecutor` runs only the planner-provided `scan` and
  `patch-enemies` argument vectors through Node `child_process.spawn`, streams
  typed stdout/stderr log events, emits coarse process-status events, and
  returns typed success, process-failure, or spawn-failure results. It is not
  wired into the adapter or generation pipeline yet. Task 1's implementation
  is in commit `5402b447f3d53a1896ca59267fc5fe10461bc071`; its seven
  focused tests, backend build, and backend typecheck passed on 2026-09-27.
  The task remains blocked because Git could not create `.git/index.lock` to
  commit the task's plan/status update under the current filesystem policy.
- The upstream JavaScript generation core is vendored at the same pinned
  commit under `engines/combined/vendor/src/` — five files, the transitive
  closure of `generate()` and `writeOutput()`
  (`core/{config,generator,output,rng}.js`, `data/prototype-data.js`), with a
  closed import graph of Node builtins only. CLI, HTTP, Electron, share-link,
  catalog-loading, and game-detection modules are deliberately excluded.
- `combinedRandomizerCore.ts` is the backend's single loading boundary for
  that core: a computed dynamic `import()` of `file:` URLs resolved from
  `import.meta.url`, hand-written types for the six upstream exports (so the
  vendored bytes stay byte-identical), and typed `JS_CORE_MISSING` /
  `JS_CORE_INCOMPLETE` failures instead of thrown errors. It loads and
  shape-checks the core; it does not call `generate()` or `writeOutput()`
  yet. See `docs/COMBINED_ENGINE_INTEGRATION.md` ("JS core vendor proof").
- Vendored-file identity is verified by comparing Git blob object IDs against
  the pinned upstream commit, not by `diff`: `core.autocrlf` rewrites line
  endings on Windows checkout. All twelve vendored files verified; object IDs
  recorded in `engines/combined/vendor/UPSTREAM_PIN.md`.
- Documentation correction: upstream `validateConfig(config, { requireGame })`
  **returns** a `string[]` of errors and never throws. The earlier analysis
  said it throws; consumers must check the returned array.
- A minimal combined-engine execution bridge
  (`apps/backend/src/adapters/combinedRandomizerBridge.ts`,
  `executeCombinedGeneration`) now chains all five previously independent
  pieces into one internal path: typed request → pure config mapping → the
  vendored JS core (`normalizeConfig`/`validateConfig`/`generate`/
  `writeOutput`) → the pure workspace planner → the `DsrDataTool` executor
  (`scan` when the workspace has no catalog, then `patch-enemies`). It
  returns a typed success carrying the package directory, placements path,
  `patch-manifest.json` path, upstream version, placement hash, and seed, or
  one of fifteen documented failure codes (`CATALOG_SOURCE_MISMATCH` added
  by Task 6). Only `GENERATION_FAILED` is
  marked retryable. See `docs/COMBINED_ENGINE_INTEGRATION.md` ("Execution
  bridge proof") for the exact data flow.
- Generation is proven workspace-only against fixtures: 19 focused tests run
  the real vendored `generate()`/`writeOutput()` in a temporary workspace
  with a synthetic schema-17 catalog, faking only the `DsrDataTool` process
  layer. They assert a real package is produced with
  `dataStatus: "extracted"`, that the process layer receives the planner's
  exact `scan`/`patch-enemies` argument vectors and nothing else, that the
  placements file exists before `patch-enemies` is invoked, that a fixture
  game directory is byte-identical before and after a successful run, and
  that the same request twice yields the same placement hash. Backend tests
  54/54 pass; backend build and the full workspace typecheck are clean
  (2026-09-28).
- The bridge rejects `dataStatus !== "extracted"` before writing any file and
  before spawning any process, so integration Blocker 1's silent prototype
  fallback can no longer reach a package directory. It also pre-checks
  `catalog.schemaVersion !== 17` so an obsolete catalog reports
  `CATALOG_STALE` instead of an opaque generation error.
- The bridge plans the workspace from the **normalized** upstream seed rather
  than the raw request seed, because upstream derives its own
  `seed_<safeSeed>` package directory from the normalized value; planning
  from the raw value would point `--placements` at a file upstream never
  wrote. See `docs/DECISIONS.md`.
- Three upstream config keys are pinned by the bridge rather than inherited
  from `defaultConfig`: `generateSpoilerLog: false` (no request field exists —
  Blocker 3 — and the upstream default `true` would emit an unrequested
  spoiler file), plus `useExtractedData: true` and `dryRun: false`, both
  verified inert at the pinned commit (read nowhere in the vendored core or
  `DsrDataTool`).
- Fixed: `core/pipeline.ts`'s combined-engine probe pointed at
  `vendor/src/cli.js` and `vendor/DSR-Randomizer.exe`, neither of which
  exists or should, so engine status always reported the combined engine as
  missing. It now probes `vendor/src/core/generator.js` and the built
  `vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll`, with a test
  asserting the combined engine reports `source` mode. The bridge itself
  never consults `ExternalAdapter`.
- Historical status at this checkpoint: the bridge had not yet executed the
  real `DsrDataTool` against a real Dark Souls installation. This was superseded
  on 2026-10-14 by the fresh packaged Nexus v3 generation against the clean QA
  DSR copy: the packaged backend completed the real Items + Enemies + Fog
  pipeline and produced a 73-file package; packaged Apply and Restore then
  returned all 73 managed paths to their pristine snapshot hashes. This is
  real pipeline evidence, beyond unit tests. Blockers 2, 3, and 6 were resolved
  by Task 6 (see `docs/COMBINED_ENGINE_INTEGRATION.md`, "Blocker resolution");
  the public GPL fork remains open.

## Fog offline reconstruction task — 2026-10-11

- Project-authored reconstruction was verified against the pinned FogMod
  reference using a clean local DSR copy: **174/174 modified EMEVD bodies**,
  **96/96 removed event IDs**, and semantic parity across all 18 event
  archives; 20 added events have project-authored builders/replacements.
- Talk reconstruction passed all **17/17 archives** and **178/178 scripts**;
  Layout reconstruction passed all **4/4 ParamDef layouts** and decoded and
  re-encoded all four GameParam tables. Fresh reruns were byte-deterministic:
  EMEVD 18/18, Talk 17/17, Layouts 4/4.
- The development-only root assembled from pristine game data and those
  reconstructions contains **58/59 required files**. The only missing file is
  `dist/fog.txt`. Its license scope is unresolved; see
  `docs/FOG_DATA_PROVENANCE.md`. No reference cache file or generated binary
  has been added to the repository.
- FogHost self-tests against the pinned reference root: **113 passed, 0
  failed, 0 skipped**. This validates the existing host and generated event
  builders with the reference root; it does not establish operation from a
  cold network-blocked start.
- The user declined contacting the FogMod maintainer and chose a project-owned
  independent equivalent for `fog.txt`. That equivalent has not been authored
  yet. The development root still contains the reference annotation solely as
  a comparison oracle; it is not a candidate product file.
- Old/new generation comparison passed **100/100 baseline seeds** and an
  additional **100/100 seeds across five option profiles**. Each compared 54
  output files; EMEVD and Talk were compared semantically, and other outputs by
  size/hash. Deterministic reruns passed for the baseline seeds and one sample
  in each of the five option profiles.
- A separate graph/progression comparison passed **100/100 seeds across five
  profiles**, including key-item area assignment, randomized connections and
  reachability. Each world had 131 graph areas, 27 key items, and no required
  areas left unreachable. Five profile samples were deterministic. These
  comparisons still use the same reference `fog.txt` in both roots and do not
  satisfy the independent-annotation parity gate.
- `FogReconstruction self-test` passed. Root `npm run typecheck` passed.
  Backend tests reported **577 passed, 1 failed, 2 skipped**; the isolated
  failure is `supportBundle.test.mjs`, where `os.userInfo()` returns Windows
  `uv_os_get_passwd: ENOMEM`. Desktop tests reported **121 passed, 1 failed**;
  the failure is the existing settings-inventory snapshot mismatch in
  `docs/SETTINGS_INVENTORY.json`. The desktop production build passed.
- The pinned progression verifier rejects the reconstructed root's changed
  reference hashes, as intended; production verification was not weakened.
  The production downloader and strict validation remain in place. The root is
  still **58/59 files**, missing the independent `dist/fog.txt`. Cold
  network-blocked generation, zero-fetch proof, standard SDK build, packaging,
  golden, release verifier and release gates therefore remain pending. No
  version bump, commit, push or release was made.

### Offline annotation continuation — area map membership

- The area inventory audit now applies the project-owned area-ID namespace
  policy to an explicit, ordered 131-ID project inventory and validates its
  identity/order against the comparison inventory. Every resolved map exists
  in the byte-verified pristine DSR map set, all compatibility projections are
  unique, `burg_*` maps to Parish, and unknown namespaces fail closed.
- This resolves only source-map membership. The Areas gate remains open:
  125 records still lack complete tags, scaling-base policy, or static graph
  semantics, so the audit remains U=125 and does not mark them complete.
- `FogReconstruction self-test` passed, including 29 area-audit assertions;
  the real-data audit against the clean DSR copy validated all 131 map
  memberships. No production provider, downloader, version, or release state
  changed.
- The audit now emits aggregate reference shape only (without field values):
  59 areas carry active tags (24 `boss`, 4 `optional`, 35 `trivial`), 9 have
  scaling bases, and 85 static links span 57 areas (38 conditioned links).
  This narrows the remaining graph reconstruction surface and does not import
  those annotations into project policy.
- A focused pristine-MSB pass found 9 name-marked transition candidates in
  Depths. Raw event matching associates the two CP1 regions with map event 0's
  generic initialization instruction; without schema-confirmed runtime use,
  those matches do not establish a logical endpoint or static edge. No edge
  was authored from these candidates.
- Added a development-only pristine boss-event evidence audit. It found 28
  `HandleBossDefeat` rows, 27 with a unique matching MSB enemy part; one
  New Londo event has no matching enemy part and remains unresolved. The audit
  writes only to an external report and assigns no area tags or edges.

### Offline annotation continuation — area classification policy

- Authored explicit project-owned classifications for all active Area tags:
  24 boss encounter areas are validated against their exact pristine
  `HandleBossDefeat` event and MSB enemy parts; 35 trivial connector areas and
  four optional branches use named project balancing rules, with every area
  checked against its verified DSR map owner. Optional and trivial overlap on
  four areas, matching their combined runtime roles.
- The external area audit resolves all 59 tag-bearing area identities
  (`boss=24`, `optional=4`, `trivial=35`) and, as a comparison-only check,
  reports **59/59 tag matches and 0 differences** without emitting reference
  field values. Overall Areas remain open:
  6 complete, 125 unresolved, `R=6/E=0/B=0/U=125`. The ten custom-start nodes
  still have 16 static links without independently established endpoints or
  conditions; the other 115 areas still need complete graph/scaling records.
- The clean DSR FogHost suite passed **117/117**. `FogReconstruction self-test`
  passed, including **30 area-audit assertions**. The audit report and oracle
  comparison remain outside the repository; no reference field values were
  imported into runtime policy.

### Offline annotation continuation — typed transition-gate evidence

- Added a development-only EMEVD/MSB audit for typed `IfPlayerHasItem`,
  `IfActionButton`, `IfActionButtonLineSegment`, event-flag and warp operations.
  It resolves parameterized event values per `InitializeEvent` call site and
  associates action IDs only through exact same-map MSB entity identity.
- On clean DSR it found **53 item-check observations**, **175 action-target
  observations**, and **13 call-site rows containing both an item check and an
  action target** across 17 maps. Three rows retain unresolved parameter
  values and are not used to infer policy.
- The known Crest-of-Artorias event resolves to item `Goods 2002` and its two
  Darkroot gate regions. Depths event `11000110` ties Blighttown Key `Goods
  2007` to object `o1309` / entity `1001200`; event `11000120` checks Sewer
  Chamber Key `Goods 2018`. These are transition/gate facts for later category
  assignment, not evidence of an `Area.To` edge. No area topology was inferred
  from them.
- `FogReconstruction self-test` passes with a known pristine Crest-item
  argument-layout fixture. The full EMEVD/MSB evidence JSON remains in the
  system temp directory and is not added to the repository.

### Offline annotation continuation — first static Area link

- Added one independently authored static route: `depths` to `depths_gaping`,
  tagged `hard` and without a condition. The project rationale is the
  hazardous same-map drop into the Gaping Dragon encounter; the destination
  resolves to the pristine boss-defeat event `11000001` and MSB enemy part
  `c5260_0000`. This is separate from the arena fog-gate Entrance and from the
  Depths key-door EMEVD observations. The existing Race+ skip whitelist
  continues to exclude this FogMod hard edge, preserving its current behavior.
- The area catalog now includes route endpoints and compares only consumed
  static-link tags. The comparison confirms the route's destination,
  condition, and active `hard` behavior; the record is classified R because
  the reference also carries an unconsumed `drop` label, which is not copied.
  Overall Areas move to 7 complete and 124 unresolved, `R=7/E=0/B=0/U=124`;
  all 59 project-owned tag identities still match the comparison inventory.
- Verification: FogHost build and data-backed tests pass **117/117** against
  the clean DSR copy; `FogReconstruction` build succeeds with 14 existing
  SoulsFormats obsolete-API warnings and zero errors; self-test passes with
  30 area-audit assertions. Real area audit confirms 131 inventory entries,
  `B=0`, and `U=124`. Reports remain outside the repository. Production data,
  generation behavior/version, and release state are unchanged.

### Offline annotation continuation — explicit glitch-link policy

- The resolver now distinguishes ordinary `Static` routes from project-owned
  `CrossMapGlitch` graph routes. Ordinary routes must stay within one verified
  map; only explicit CrossMapGlitch routes require the consumed `hard` tag and
  may connect two verified maps. Same-map hard routes remain ordinary Static
  links. Both endpoints, condition operators, condition areas, and resolved
  key-item records backed by pristine GameParam are validated. This
  classification is policy-only and is not emitted to the FogMod DTO;
  Entrances and Warps remain the physical transition records.
- The regression fixture allows a documented-shape Darkroot-to-DLC wrong-warp
  only when marked `CrossMapGlitch` and `hard`; it rejects the same cross-map
  edge as ordinary `Static`, a missing key fact, a non-hard crossing, and a
  same-map CrossMapGlitch. The
  fixture is not itself an authored catalog edge. The real project policy
  still contains only the separately verified Depths-to-Gaping route.
- FogHost build and clean-DSR tests pass **117/117**. `FogReconstruction`
  builds with 14 existing obsolete-API warnings and zero errors; self-test
  passes (30 area-audit assertions). Re-running the external area report keeps
  the Depths route validated and the aggregate at `R=7/E=0/B=0/U=124`.
- The full Area tag policy now assigns a reviewed state to all 131 IDs: special
  boss/optional/trivial tags are explicit and the project default is an ordinary
  progression node. The oracle comparison confirms 131/131 tag sets matched.
  The project intentionally emits no `ScalingBase` overrides: the shipped
  option contract never enables FogMod's debug-only `scalingbase` or `dumpdist`
  flags, and a graph test confirms those fields are ignored with shipped
  options. The audit records this rationale and classifies a scaling-base-only
  difference as behavior-equivalent. The report's `ProjectPolicy` count now
  covers the full 131-entry inventory; `Derived` counts the catalog nodes with
  supporting project evidence in the current slice. Topology remains open at
  124 U.

### Offline annotation continuation — full Area model inventory

- The development Area audit now constructs all 131 typed Area nodes in the
  stable project order, validates their map memberships against the pristine
  DSR facts, applies the complete tag policy, and validates the catalog before
  comparison. A data-backed FogHost test also constructs a 131-node FogMod
  graph from this model; this checks graph construction only, not route
  reachability.
- Each audit entry now reports `UnresolvedFields`. All 124 open records name
  `staticLinks` as the remaining unresolved field; the other project fields
  are present and have documented policy. The external report is at
  `C:\Users\Adam\AppData\Local\Temp\ds1tr-area-catalog-continuation.json`.
- Latest Area audit: 131 total, 40 with pristine-source anchors, 131 under
  project policy, 131/131 tag matches, 7 complete, 124 unresolved,
  `R=7/E=0/B=0/U=124`. Only the independently authored Depths-to-Gaping route
  is present; no oracle edge values were emitted or imported.
- Verification after this slice: FogHost build and clean-DSR tests passed
  **117/117**; FogReconstruction build passed with 14 existing SoulsFormats
  obsolete-API warnings and 0 errors; self-test passed with 30 Area audit
  assertions; focused `git diff --check` passed. Areas remain open and phases B
  and C have not started.

### Offline annotation continuation — ToTG route evidence (2026-10-12)

- The `totg -> totg_lower` hard route now has a clean-source anchor: pristine
  MSB region `1313400` is the Patches kick trigger and region `1312121` is the
  post-kick landing point. Pristine ToTG event `11310002` references the trigger
  at instruction 7 and the landing region in its movement operations at
  instructions 24, 32, and 39. A regression test verifies the map regions,
  event archive, opcode identities, and argument values from the clean DSR
  copy. The route remains a project-owned hard-only static edge, separate from
  fog-wall/door transitions.
- The custom-start fixture now includes the `totg_lower` route endpoint as a
  graph node without incorrectly declaring it a custom start.
- Refreshed Area audit: 131 total, 40 with pristine-source anchors, all 131
  under project policy, 131/131 tag matches, 8 complete, 123 unresolved,
  `R=8/E=0/B=0/U=123`. The additional route is comparison class R due an
  unconsumed reference tag; no reference endpoint or condition was imported.
- Area report format 6 includes only each row's comparison link count, never
  destinations or conditions: 74 areas have zero, 38 have one, 13 have two,
  and 9 have three or four (85 total). This is a review-priority signal only;
  it does not populate project route policy.
- Verification: FogHost tests pass **117/117**; FogHost test build and
  FogReconstruction build passes with 14 existing SoulsFormats obsolete-API
  warnings and 0 errors; FogReconstruction
  self-test passes (31 Area audit assertions); the external Area report was
  regenerated. Entrances, Warps, KeyItems, and LotLocations remain untouched.

### Offline annotation continuation — Depths transition evidence (2026-10-12)

- The pristine EMEVD/MSB regression now also verifies the Depths object
  `o1309` / entity `1001200` against event `11000110`: the event checks the
  English DSR Goods item `Blighttown Key` (`3:2007`), accepts interaction with
  that object, and issues the short-warp request. This is recorded as a
  physical transition candidate for the later Entrance model, never as an
  `Area.To` edge.
- FogHost test build passed with 0 warnings/errors; clean-DSR FogHost tests
  passed **117/117**. The Area inventory remains at 131 total, 8 complete and
  123 unresolved; no route policy was inferred from the new candidate alone.

### Offline annotation continuation — Area edge ownership review (2026-10-12)

- Source review of the pinned FogMod `Graph.Construct` and `GraphConnector`
  confirms that `Area.To` creates fixed graph edges, while Entrances and Warps
  create a separate set of edges that may be shuffled. A physical boundary
  therefore needs exactly one project-owned representation; the later presence
  of an Entrance/Warp cannot by itself justify omitting a required static Area
  route.
- The Depths key-door event `11000110` remains an Entrance candidate, separate
  from the already verified hard-only `depths -> depths_gaping` drop. ToTG's
  Patches descent remains the independently verified hard-only Area route.
  Neither candidate adds an Area edge solely from proximity or a name marker.
- Current Area audit remains 131 total, 8 complete and 123 unresolved
  (`R=8/E=0/B=0/U=123`). This is a design/evidence review only; no production
  generation, data-root, version, or release behavior changed.

### Offline annotation continuation — Area graph behavior audit (2026-10-12)

- Added a development-only Area graph behavior audit that runs the pinned FogMod
  Graph, ItemReader, GraphConnector, and GraphChecker with reference Areas and
  with the project Area projection while holding all other categories equal.
  It writes aggregate results only and never serializes reference edge
  endpoints or conditions. The report is outside the repository at
  `C:\Users\Adam\AppData\Local\Temp\ds1tr-area-graph-behavior-audit.json`.
- The 15-case sample (three profiles by five fixed seeds) has 15/15 reference
  connector passes and 15/15 project connector failures with the current
  two-route project Area policy. In the fixed-default profile, the project
  graph reports 125 areas without a connector route. This confirms that the
  current Area model is incomplete; it is not an equivalence result.
- FogReconstruction builds with 14 existing SoulsFormats obsolete-API warnings
  and zero errors. Its self-test passes, including the new unreachable-count
  parser assertion. No application generation, production data root, version,
  package, or release state changed.

### Offline annotation continuation — boss-area static-route policy (2026-10-12)

- Authored an explicit empty-static-route policy for 23 boss encounter Areas.
  Each entry is validated against its project boss Area, pristine HandleBossDefeat
  event, and exact MSB enemy-part set. These nodes use the separate
  Entrance/Warp boundary model. Catacombs Pinwheel is intentionally excluded
  because its post-boss cave passage remains a static Area route to reconstruct.
  Static route resolution rejects a source that also has a no-static-links
  disposition.
- Refreshed Area audit: 131 total, 40 derived, all 131 under project policy,
  131/131 active tags matched, 31 complete, 100 unresolved,
  `R=31/E=0/B=0/U=100`. The report still contains aggregate reference counts
  only; no oracle endpoints or conditions were emitted.
- Release/runtime data is unchanged. FogHost Release build passed with 0
  warnings/errors; clean-DSR FogHost tests passed **117/117**. The latest
  FogReconstruction build passed with 14 pre-existing SoulsFormats
  obsolete-API warnings and 0 errors. The 15-case Area graph comparison still
  shows the partial project route inventory cannot connect the full graph; this
  no-static policy is a classification step, not an Area closure claim.

### Offline annotation continuation — project-unreachable graph diagnostics (2026-10-12)

- The development-only Area graph behavior report now includes the sorted set
  of project-owned Area IDs left unreachable by the project graph. It still
  excludes all reference edge endpoints and conditions. This makes connector
  failures actionable without copying oracle topology.
- The same 15 fixed profile/seed cases still fail project graph connection;
  the project model reports 125 unreachable Areas in the fixed-default case.
  Area completeness remains 31 complete / 100 unresolved (`R=31/E=0/B=0/U=100`);
  no route policy was inferred from the diagnostic IDs.
- FogReconstruction Release build passed with 14 existing SoulsFormats
  obsolete-API warnings and 0 errors. Its self-test passed, including the new
  unreachable-ID parser test. Fresh clean-DSR area-gate and transition-event
  evidence reports were written outside the repository. Production data,
  generation behavior, version, and release state are unchanged.

### Offline annotation continuation — boss route neighborhood evidence (2026-10-12)

- The pristine boss-fact report now includes up to 40 nearby entity-backed MSB
  objects/regions per matched boss, ordered by 3D distance. It remains
  evidence-only and writes to an external development report.
- The Pinwheel anchor resolves to pristine M13 enemy part `c3320_0000` in
  collision `h0036B0`; its local region set includes boss warp points and the
  boss-room logic region. This confirms the encounter anchor but does not prove
  an independent static exit to another Area, so `catacombs_pinwheel` remains
  unresolved pending route evidence. No Area policy was changed.
- The Release build and real-data report generation passed. The report is at
  `C:\Users\Adam\AppData\Local\Temp\ds1tr-boss-facts-continuation.json`.

### Offline annotation continuation — Pinwheel gate ownership (2026-10-12)

- The empty-static-route policy now covers all 24 boss encounter Areas. For
  `catacombs_pinwheel`, it is anchored to pristine Catacombs object entity
  `1301990` and trigger region `1302998`; the boss event still anchors the
  encounter Area. Project semantics assign this boss-gate boundary to the
  Entrance model instead of duplicating it in `Area.To`.
- Typed EMEVD inspection resolved event `11305350`'s nearby short-warp requests
  to skeleton enemies `c3320_0001/0002`; those are enemy movement, not a player
  exit. No additional static return route was inferred from them.
- Refreshed Area audit: 131 total, 40 derived, 131 under project policy,
  131/131 tag matches, 32 complete, 99 unresolved; structural comparison is
  `R=31/E=0/B=0/U=100`. The Pinwheel row is complete by project policy but
  retains structural `U` until combined Entrance behavior is compared.
- FogHost test build passed with 0 warnings/errors; the full clean-DSR FogHost
  test harness passed. FogReconstruction Release build passed with 14 existing
  SoulsFormats obsolete-API warnings and 0 errors; self-test passed.
  Production data, version, package, and release state are unchanged.

### Offline annotation continuation — Pinwheel approach ownership (2026-10-12)

- The same pristine Catacombs fog-gate boundary is now assigned to the Entrance
  model from both `catacombs_prepinwheel` and `catacombs_pinwheel`. The policy
  validates map-local object entity `1301990`, trigger region `1302998`, and
  interaction event `11305391`; it rejects a static Area route from either
  side. Boss-event evidence remains required for the arena node.
- Area inventory audit: 131 total, 40 derived, 131 under project policy,
  33 complete, 98 unresolved. Structural comparison remains
  `R=31/E=0/B=0/U=100`; the gate-side link differences await the combined
  Entrance comparison.
- FogHost Release build and test build passed with zero warnings/errors. The
  full clean-DSR FogHost suite passed 117/117. FogReconstruction Release build
  passed with 14 existing SoulsFormats obsolete-API warnings and zero errors;
  self-test passed, including all 31 Area catalog assertions. No production
  data, generation behavior, version, packaging, or release state changed.

### Offline annotation continuation — pristine ObjAct evidence (2026-10-12)

- The pristine DSR fact index now records MSB ObjAct object-part identity,
  action entity, parameter, event flag, and state. The development-only Area
  gate report includes those rows alongside typed EMEVD evidence. The clean
  audit found 147 ObjActs across 17 maps; the report is outside the repo at
  `C:\Users\Adam\AppData\Local\Temp\ds1tr-area-gates-objacts.json`.
- In Catacombs, candidate objects `o3020_0000` and `o3021_0000` have no
  corresponding ObjAct row. This does not prove they are not transitions, so
  no Area route was inferred from their names or proximity. Area inventory
  remains 33 complete / 98 unresolved; structural comparison remains
  `R=31/E=0/B=0/U=100`.
- FogHost Release/test builds passed with zero warnings/errors; the full
  clean-DSR suite passed 117/117, including MSB ObjAct/object identity checks.
  FogReconstruction Release build passed with 14 existing SoulsFormats
  obsolete-API warnings and zero errors; self-test passed. No production data,
  generation behavior, version, packaging, or release state changed.

### Offline annotation continuation — ObjAct flag correlation (2026-10-12)

- The gate-evidence report now includes all 940 map-local EMEVD event/flag
  writes so ObjAct enable flags can be compared to pristine script writers.
  Of 147 ObjActs, 43 have a positive event flag and 4 have a direct writer in
  that same map archive. This is a correlation index only; it does not equate
  an enable flag with a door-state transition.
- Catacombs' candidate objects `o3020_0000` and `o3021_0000` still have no
  ObjAct record and no route was inferred from that absence. Areas remain
  33 complete / 98 unresolved; structural comparison remains
  `R=31/E=0/B=0/U=100`. Version stays 0.39.0; production and release remain
  unchanged.

### Offline annotation continuation — combined transition inventory (2026-10-12)

- The work order now treats Areas, Entrances, and Warps as one world-transition
  model. Added a development-only inventory command that reads current project
  policies and byte-verified pristine DSR facts without reading reference
  transition endpoints or conditions.
- The current authored inventory contains two static Area routes, two
  Entrance boundary candidates (the Depths-to-Blighttown key door and the
  Pinwheel fog gate), no Warp policies, 23 explicit no-static-link Area
  dispositions, and 104 unresolved Area dispositions. No duplicate physical
  owners were found. These counts are an inventory checkpoint, not a closure
  result; Entrance runtime projections and all Warp policies remain open.
- The Depths-to-Blighttown boundary policy is anchored to pristine map object
  `o1309` / entity `1001200`, event `11000110`, and Goods `3:2007` (Blighttown
  Key). The existing clean-source event test verifies that the item check and
  object action belong to the same event; the inventory resolver separately
  checks the exact object, event, item identity, and project Area/map owners.
  Its item appears in pristine ItemLotParam rows `2500` and `4006`, with no
  MSB treasure or NpcParam enemy-drop source. Pristine common EMEVD event 970
  also awards lot `2500` through instruction `2003[36]`; common event 0
  instruction 42 supplies that lot ID. Row `4006` still has no identified
  consumer. The fact index now records typed `2003[4]`/`2003[36]` item-lot
  awards and resolves their `InitializeEvent` arguments per caller. The
  key-door Entrance DTO is now authored as a fixed, two-sided FogMod door with
  the logical `blighttown_key` requirement. Its KeyItem policy and the other
  Entrance/Warps remain open.
- Verification: FogHost test build passed with zero warnings/errors; the full
  clean-DSR FogHost suite passed **148/148** with both `DS1TR_TEST_GAME_ROOT`
  and `DS1TR_PRISTINE_DSR_ROOT` set to the QA copy. FogReconstruction Release
  build passed with 14 existing SoulsFormats obsolete-API warnings and zero
  errors; self-test passed, including three new duplicate-anchor inventory
  assertions. The external report is
  `C:\Users\Adam\AppData\Local\Temp\ds1tr-world-transition-inventory.json`.
  Production data, generation behavior, version, packaging, and release state
  are unchanged.

### Offline annotation continuation — boss reward lot provenance (2026-10-12)

- Confirmed from pristine EMEVD bytes that common event 970's `2003[36]` item
  award receives ItemLot `2500` from event 0's initializer at instruction 42.
  That row contains the Blighttown Key. The same initializer passes completion
  flag `2`; Depths boss event `11000001` sets that flag and is anchored to the
  Gaping Dragon area, supporting `depths_gaping` as the key's later default-area
  policy. The numerically separate row `4006` still has no confirmed runtime
  consumer; it is not treated as a second key placement.
- Added a typed item-lot award fact index for `2003[4]` and `2003[36]`, resolving
  integer arguments from same-archive `InitializeEvent` callers. The clean DSR
  integration test now guards the event-970/lot-2500 mapping.
- FogHost.Tests Release build passed with zero warnings/errors; the full
  clean-DSR suite passed **148/148**. FogReconstruction Release build passed
  with 14 existing SoulsFormats obsolete-API warnings and zero errors;
  self-test passed. The clean-DSR test builds the key-door catalog and confirms
  FogMod constructs fixed entrance edges gated by the resolved key. The
  regenerated external inventory shows the event-970 reward source and keeps
  `4006` unresolved; combined transition counts remain 2 static links, 2
  Entrance candidates, 0 Warps, 23 no-static Area dispositions, and 104
  unresolved Area dispositions. No version, production data, packaging,
  release, or commit changes were made.

### Offline annotation continuation — key-door family and short-warp facts (2026-10-12)

- Added the Darkroot Crest gate to the project-owned `FIXED_KEY_DOOR` policy
  family. Clean DSR anchors are object `o2908_0000` / entity `1201986`, event
  `11200100`, action region `1202500`, and Goods `3:2002` (Crest of Artorias).
  The authored project sides are `darkroot` and `darkroot_forest`; the clean
  test checks the item check and resolves the paired event's region parameter.
  FogMod constructs a fixed, bidirectional, Crest-gated edge. This does not
  resolve Darkroot's separate static Area relation.
- Corrected the development area-gate reader's `IssueShortWarpRequest`
  argument layout: entity ID, warp type and destination entity ID are aligned
  Int32 fields at offsets 0, 4 and 8. Offset 5 had produced bogus destination
  values. A focused self-test uses pristine Sen's event `11500102` and expects
  player destination object `o5301_0000` / entity `1501011`.
- The clean-data short-warp report now yields 62 unique player targets: 39
  type 1, 23 type 0; 59 target a unique MSB region/object, and 3 use destination
  ID 0. These are evidence candidates, not 62 authored Warp policies.
- Current combined inventory: 2 static Area routes, 3 Entrance boundary entries
  (including the Pinwheel candidate), 0 authored Warps, 23 no-static Area
  dispositions, 104 unresolved Area dispositions, 0 duplicate owners. Phase 1
  is still open; Phase 2 has not started.
- Verification: clean-DSR FogHost suite **148 passed, 0 failed, 0 skipped**;
  FogReconstruction self-test passed. The reconstruction build retains 14
  pre-existing SoulsFormats obsolete-API warnings. Desktop version and
  production runtime/data root remain unchanged; no commit or push.

### Offline annotation continuation — combined transition comparison (2026-10-12)

- Added a comparison-only graph audit that constructs the pinned FogMod graph
  for combined `Area.To + Entrances + Warps` behavior in fixed-default and hard
  profiles. It compares traversable directed routes and conditions, ignores
  the annotation category when evaluating route equivalence, and emits counts
  only; reference endpoints and conditions are not written to the report.
- Added regression cases proving that reciprocal static routes and one fixed
  two-sided Door compare as behavior-equivalent, a fixed Warp participates as
  a directed edge, and a one-way static route versus a bidirectional Door
  remains behavior-changing. The area-catalog audit
  now includes this combined graph result and projects the two independently
  validated key-door policies into its graph comparison.
- Current partial-model comparison: fixed-default has 286 reference routes
  and 4 project routes (1 shared); hard has 298 reference routes and 6 project
  routes (3 shared, including 2 category-only equivalents). Both profiles
  still show behavior-changing differences; this is a measurement gate, not
  Phase 1 closure. The separate Area field inventory remains unresolved and
  no Warp policy has been authored.
- FogReconstruction Release self-test passes, including the combined graph
  fixtures. The release build has 14 existing SoulsFormats obsolete-API
  warnings and zero errors. No production catalog, generator, version,
  package, release, or Git history changes were made.

### Offline Fog checkpoint — fixed-transition graph audit (2026-10-12)

- Closed the current comparator-correctness fragment. Combined fixed-profile
  routes now use `Graph.Edge.LinkedExpr`, which is the condition evaluated by
  FogMod's progression checker and includes both sides of a connection. The
  graph clone preserves `Entrance.Area`. The audit separately compares node
  inventories and marks a profile U when nodes differ or named exits remain
  unlinked. Exact condition-string comparison is intentionally conservative;
  this is a fixed-default/fixed-hard diagnostic, not proof for randomized
  entrance/warp behavior or executable warp targets.
- Added fixtures for a conditional fixed Entrance, a conditional fixed Warp,
  paired `dnofts` door conditions, a missing isolated node, and unclassified
  unlinked endpoints. Existing reciprocal-route, one-way-route, and Warp
  fixtures remain. FogReconstruction self-test passes.
- Latest Area audit: **33/131 complete**, 40 with resolved DSR identity facts,
  project tag policy on 131/131, and 98 unresolved. Per-area classes are
  R=31/E=0/B=0/U=100; U includes the two Pinwheel Area records whose fields are
  complete but whose combined Entrance behavior is still open. The combined
  fixed-profile comparison is B in both profiles: default 288 reference routes
  versus 4 project routes, hard 300 versus 6. Both have zero node differences
  and zero unlinked named endpoints. These counts do not close Phase 1.
- Current annotation inventory: Areas 33/131 complete; Entrances 0/109
  category-complete (2 authored fixed-key-door DTO policies and one anchored
  Pinwheel candidate, with the rest of the category open); Warps 0/15;
  CustomStarts 16/16 CLOSED (R=2/E=14); KeyItems 0/27; LotLocations 0/440;
  DefaultFlagCols 1 project-policy, 1 unused, 106 unresolved; Enemies 0/392
  policy-complete (resolver infrastructure only); DefaultCost 0/127; Options
  11/11 unused and CLOSED. No Entrances category-level R/E/B/U comparison has
  run; the combined graph result above is B in both fixed profiles. The current
  world inventory reports 2 static Area routes, 3 Entrance candidates, 0 Warp
  policies, 23 no-static dispositions, 104 unresolved transition-owner
  dispositions and 0 duplicate project physical owners. Phase 2 remains
  NOT STARTED.
- Verification on the available local .NET SDK **8.0.425**:
  FogHost Release build passed (0 warnings/errors); the FogHost self-test
  console passed **148/148** against the clean QA DSR and verified Fog data;
  FogReconstruction Release build passed (14 existing SoulsFormats obsolete-API
  warnings, 0 errors); FogReconstruction self-test passed. `dotnet test` is not
  the test runner for FogHost.Tests (the project is a console self-test); the
  documented `dotnet run` workflow was used instead. No game launch, seed
  matrix, production data/downloader change, version bump, package, release or
  push was made.

### Compact Full Fog V1 delivery sprint checkpoint (2026-10-13)

- Corrected the clean-DSR Area disposition assertion to match the current candidate: 4 Areas touch 2 authored static links, 5 additional Areas are Entrance-owned, 122 have explicit no-static-link dispositions, and no Area disposition is unknown. This does not prove that the 131-node graph is traversable.
- Current candidate: 131 Areas, 23 Entrances (2 fixed key doors, Pinwheel, 20 PvP arena gates), 0 Warps, 22 key identities (9 required / 13 random-only), 723 safe LotLocations with 224 unresolved/conflicting sources excluded, 69 enemy rules, 22 project collision flags, 131 costs. Fingerprint `fb7ad72ea5cb888c5718e7504da87d48c171e364a20c4da3580a03e23a6cac55`.
- Batch 1 passed: FogHost Release build; full FogHost self-test **117 passed, 0 failed, 0 skipped** using clean QA DSR + verified Fog data; FogReconstruction self-test; backend typecheck/build; focused backend Fog tests **34 passed, 0 failed, 1 skipped** (integration data-root variable absent).
- Production remains unchanged: HostRunner and backend still consume `fog.txt`, and world-data preparation/downloader remain enabled. Batch 2, stress, real generation, offline proof, and oracle comparison were not run because there is no validated Warp pair, Entrance families/graph connectivity are incomplete, and progression completeness is unproven. Production gate remains closed.
- No app version bump, commit, push, tag, or release.

### Compact Full Fog V1 final content pass (2026-10-13)

- Added project-owned fixed topology using verified DSR transition anchors; the authored policy now resolves 187 static `Area.To` links, including 38 cross-map `ConnectCollision` anchors. The Key to the Seal is assigned to accessible `newlondo_building`.
- Modeled legacy graph key `kiln_start` as Compact V1's Firelink Lordvessel-altar milestone so fixed golden gates require the Lordvessel without cycling through the final Kiln. The physical Kiln route remains gated by the Lordvessel and all four Lord Souls. This is project semantic policy; no FogMod or generator code changed.
- Assigned verified DLC return part `h0037B1_0000` to the project Darkroot hub Area. Corrected random-start graph validation to build a separate seeded graph for each selected custom start; all 16 starts now reach every required Area.
- Latest candidate fingerprint: `bd075f6c36994e8adbd0fde78a1fe3ba1b405360a1f5fdc884ecc671fdeb7fc4`. It contains 131 Areas, 64 Entrances (17 traversable, 22 boss, 16 major / 4 minor PvP, 3 Lordvessel, 2 fixed/key), 0 Warps, 22 KeyItems (9 required / 13 random-only), 723 safe LotLocations, 69 Enemy rules, 22 project Flags, and 131 Costs. All 22 project-used collision flags are covered. Two transition candidates remain unresolved only because physical targets are missing: New Londo event 11605381 / region 1602898 and Demon Ruins event 11410090.
- Warp inventory: 261 actor/NPC short-warps are `NOT_RANDOMIZABLE`. The separate player short-warp inventory has 62 unique `IssueShortWarpRequest` targets (39 type 1 / 23 type 0; 59 resolve to same-map MSB entities, 3 to destination ID 0); these are script relocation effects, not standalone persistent Area Warp pairs. The sole extracted `WarpPlayer` is DLC event 11210849 (caller 11210700) with destination entity 0. Area Warps remain blocked; no destination ID or pair is invented.
- Graph matrix: default and hard graphs each have one structural component; 13 tested graph profiles pass required-Area reachability, including all 16 custom starts. Area Warps are the remaining blocked option family.
- Final verification: FogHost self-test **148 passed, 0 failed, 0 skipped**; FogReconstruction self-test passed; backend typecheck/build passed; focused backend Fog tests **39 passed, 0 failed, 0 skipped**. The 1000-seed progression stress, real-generation matrix, and cold offline generation were not run because Area Warps remain blocked.
- Production remains on `fog.txt`; Compact provider/backend projection are inactive. Offline read/download counts were not instrumented; no cold offline generation or download attempt was made by this task. Desktop version remains 0.39.0. No commit, push, release, profile boundary, or production switch was made, as explicitly requested.

### Compact Full Fog V1 — Area Warps continuation (2026-10-14)

- Added six project-owned Warp directions from pristine cutscene/player relocation facts, forming reciprocal Firelink crow/Asylum, Anor Londo painting/Painted World, and Catacombs/Tomb of the Giants coffin connections. All destination maps and region IDs validate against clean DSR MSBs. `FogWarp` remains the runtime DTO; no generator, seed format, profile, or provider wiring changed.
- Clean relocation inventory: 27 cutscene-warp event families + 62 player short-warp instruction anchors + one direct `WarpPlayer` event = 90 player-relocation anchors. Six are `PROJECT_WARP`; 84 are `NOT_RANDOMIZABLE`. Event `11210849` has no parameters, caller `11210700` passes an unused zero, and its actual destination is region `1218146` on `m12_01`; it is an arena disband/system return. Event `11705100` only sets respawn and saves.
- Candidate fingerprint: `98cf74edcd1c29b4e39d1c848eabb434e2c0dd48efe1150c38e5e6fcd1b9c7b1`; 131 Areas, 64 Entrances, 6 Warps, 22 KeyItems, 723 LotLocations, 69 Enemies, 22 Flags, 131 Costs; all candidate categories report Complete.
- Verification: FogHost full suite **117 passed, 0 failed, 0 skipped**; 14 graph profiles, all 16 random starts, deterministic Warp shuffle, and **1000/1000** Warp/Lord-Soul progression seeds passed (500 randomized starts; prison escape checked each seed). NuGet emitted NU1900 because its vulnerability index was unreachable.
- Real-generation preflight stopped before writing: FogMod `GameDataWriter` unconditionally indexes Entrance `o5869_0000` during Anor Londo processing, but that Entrance is absent from the candidate. No out-of-scope Entrance policy was added. Therefore production Compact provider/backend projection was not activated, no cold-offline generation was run, and desktop version stays 0.39.0. No commit, push, package, or release.

### Nexus release-hardening checkpoint (2026-10-14; local only)

- The current worktree selects `ds1tr-gen-v3` for new runs. `/api/world-data/prepare` now resolves a supplied Seed Code's generation profile before preparing prerequisites; a normal new run uses the current v3 profile. Nexus preparation never opts into upstream downloads. The v3 cold-cache path reconstructs 58 inputs locally and the packaged run generated a 73-file Items + Enemies + Fog package.
- Source-side QA: root typecheck passed; desktop tests **126/126**; FogHost self-test **148/148**; FogReconstruction self-test passed; DsrDataTool.Tests **14/14**; focused profile/API tests passed; v3 golden regeneration passed. The latest backend full suite had **593 passed, 2 skipped, 1 environment failure**: supportBundle.test.mjs could not load because Node 24's `os.userInfo()` raised `uv_os_get_passwd ENOMEM`; isolated and final reruns reproduced the same environment error. Release tooling tests passed **21/21**. Production-only npm audit reported **0 vulnerabilities**; development/build-tool findings remain outside runtime scope.
- Frozen profile verification remains blocked. Of 55 available v1/v2 cases, **39 matched and 16 v1 Fog cases drifted**; the differing outputs were confined to `event/common.emevd.dcx` and `event/m12_01_00_00.emevd.dcx`. The v1/v2 profile gate remains pinned to FogHost 0.5.0; the current Nexus package contains FogHost 0.6.0 only. Do not allow 0.6.0 for those profiles or rewrite fixtures. Until a separately verified compatible legacy host and its data path are available, Nexus v1/v2 Fog Seed Codes are not release-ready.
- The fresh local Nexus Setup and Portable artifacts passed `release-verify.mjs`. Both were exercised through their packaged backend against only `C:\Users\Adam\ds1tr-qa\game-inst\DARK SOULS REMASTERED`: v3 generation, Apply, Restore, and original hashes for all 73 generated paths matched the pristine snapshot. Setup installation and silent uninstall also passed. The test launched the packaged backend in Electron's Node mode rather than automating the visible renderer; no gameplay was claimed. A process-scoped network guard observed no blocked request, the cold v3 cache contained 58 files and no `fog.txt`, and static helper-source review found no HTTP/socket client path.
- Package inventory: 168 unpacked files; DsrDataTool, FogHost, and GameCursorHelper are present; no `fog.txt`, DSR executable, `app-update.yml`, `latest.yml`, upstream Fog `dist`, QA fixture path, or detected secret was found. Nexus updater/GitHub checks remain disabled. Artifacts are unsigned local staging only.
- The reported .NET error window was not reproduced; the prior 48-hour Windows application-event query found no matching .NET Runtime/Application Error entry. There is no evidence to justify a speculative runtime change. No separate long-run generation soak was run because the frozen v1 Fog compatibility gate is still red.
- This checkpoint does not claim owner gameplay QA. Six v3 owner worlds remain listed in `FINAL_GAMEPLAY_QA.md`. App version remains 0.39.0; no commit, push, tag, release, upload, or signing action was performed.

### Legacy FogHost compatibility restoration (2026-10-15; local worktree)

- The authentic FogHost 0.5.0 source is commit `b2ba7d6148810b367bd01d955688364cad30c845`, subtree `tools/fog-host`, tree `438b66455982060760419a755283acaa23997979`. The drift came from sending Compact-only schema-3 fields to this strict schema-2 host; the backend now builds the correct request contract from the frozen generation profile.
- All **55/55 v1/v2 golden cases** and the **1/1 v3 Compact golden** passed with frozen hashes and zero fixture edits. The 16 drifting v1 Fog cases now use the existing legacy 59-file cache and legacy item tracking with host 0.5.0. Key-placement runs use the 0.6.0 read-only progression exporter on the same legacy data; Fog generation itself stays on 0.5.0. The existing four v2 fixtures have Fog disabled.
- Routing is profile-derived and fail-closed: v1/v2 -> FogHost 0.5.0/schema 2; v3 -> FogHost 0.6.0/schema 3. The package builds the historical executable from its pinned source tree and ships it at `resources/runtime/tools/fog-host-legacy/FogHost.exe`; the current host remains at `resources/runtime/tools/fog-host/FogHost.exe`.
- Packaged backend QA on the clean `ds1tr-qa` DSR copy confirmed v1 used 0.5.0 + legacy data (59 files, `fog.txt`), 73 package files, digest `c63cc4ec4c1d97a3e2c6dab784a4aa4cbd57c2af5c68bee7f06adfbf4910cad8` (golden match). A Fog-enabled v2 smoke used 0.5.0 + the same legacy data, 55 package files, digest `ce3b956401841e0b740753249e08a7d84a823fe37f9b474a00ccdf76e0ab1de8`; no Fog-enabled v2 golden exists to compare. V3 used 0.6.0 + Compact data (58 files, no `fog.txt`), 73 package files, digest `dcb3ac2be4b79a64b33e1c793a1ff24795f0c8170f469babfcfd11540d02cd31` (golden match).
- Offline policy is unchanged: Nexus v1/v2 Fog needs an already available, hash-verified 59-file legacy cache; that data is not bundled or fetched by Nexus. V3 remains reconstructable from the verified local game files with zero downloads.
- Final verification: root typecheck passed; desktop suite **127/127**; backend full suite **595 passed, 2 skipped, 1 environment failure** (`supportBundle.test.mjs`, Node `os.userInfo()` -> `uv_os_get_passwd ENOMEM`); all other backend tests passed when that environment-dependent file was excluded (**595 passed, 2 skipped**). Current FogHost self-test **148/148**, pinned legacy FogHost self-test **74/74**, DsrDataTool.Tests **14/14**, and the packaged Nexus verifier passed. Backend profile/Seed Code tests include the simulated future app-version check and passed.
- Local unsigned Nexus artifacts are `apps/desktop/release/nexus/DS1-Multi-Randomizer-0.39.0-Nexus-Setup-x64.exe` (205,539,307 bytes; SHA-256 `f8333252f28601cde16844898b939ac7ebfd6bb0225787d1239f79f65c23b820`) and `apps/desktop/release/nexus/DS1-Multi-Randomizer-0.39.0-Nexus-Portable-x64.exe` (204,902,445 bytes; SHA-256 `fcceba7bd13b922b917264d236237c84f6b4129a6fc7ca67091fa031e49d7b96`). These are ready for local owner gameplay QA, subject to the legacy-cache requirement for v1/v2 Fog. Version remains 0.39.0; no commit, push, tag, public release, upload, fixture edit, or live Steam DSR write was made.

# Owner-approved roaming-boss fit correction (2026-10-15; local only)

- The pre-change boss fit snapshot is preserved at `docs/placement-audits/boss-fit-classification-pre-nexus-2026-10-15.md` and `.json`. It records 17 candidates, 15 explicit excluded models, source rules, exact restore instructions and the 562/1,716 (32.75%) baseline. Current owner minimums are Artorias 0, Capra 0, Pinwheel 0, Taurus 2, and Gwyndolin 0. Other boss rules and Moonlight Butterfly's class-3 override are unchanged.
- Same-catalog deterministic fit audit: 562 -> 1,716 bosses, 1,154 -> 0 fit survivors. Candidate availability is 4 in classes 0-1, 5 in class 2, 11 in class 3, 16 in class 4 (no destinations), and 18 in class 5. Eight engine-level option cases reran deterministically. Full details and a 5-case manual owner QA Seed Code list are in `docs/placement-audits/boss-fit-validation-2026-10-15.json` and `docs/BOSSES_ONLY_AUDIT.md`.
- Production package matrix on the safe QA copy passed for Bosses-only 100% fit and Both + Fog. The Fog merge completed; no event-window overrun, missing/duplicate event owner, or nondeterministic rerun was reported. The direct final-output audit for Seed Code `DS1TR1-06NWCQQ3-32HXEE10-CZNTCSCP-X3K2DSMN-4VKEG5H6-X8D1G60H-NHYX02YS-T7N6FA2P-7ZMEBH0G-NNZS0JR2-T8N1YT66-GQAKVR2J-CGGEP7B8-R265CF2M-N1W00N7B-0G10` matched all 1,716 planned roaming placements to the scanned final MSB model, scaled NpcParam ID, and NpcThinkParam ID. Gwyndolin's class-0 output also passed SmokeVerify for loaded model, parameter rows, souls, respawn bit, unique map event IDs, wake-up event and map model count. Event-owner audit: 1,716 checked, zero missing or duplicate owners.
- No gameplay safety claim: the five narrow/corridor borderline placements are prepared for owner testing. No Steam installation was used. Focused boss regression tests passed 56/56; root typecheck passed; DsrDataTool.Tests passed 14/14. The full backend suite had 596 passed, 2 skipped, and 1 environment failure: `supportBundle.test.mjs` could not call Node `os.userInfo()` because `uv_os_get_passwd` returned ENOMEM. Desktop version is bumped to 0.39.1 per the project SemVer rule; no release, commit, push, or tag was created.
