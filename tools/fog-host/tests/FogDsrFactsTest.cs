using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Buffers.Binary;
using FogHost;
using FogMod;
using YamlDotNet.Serialization;

namespace FogHost.Tests
{
    public static class FogDsrFactsTest
    {
        public static void Verify()
        {
            string root = Environment.GetEnvironmentVariable("DS1TR_FOG_DATA_ROOT");
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                throw new InvalidOperationException("DS1TR_FOG_DATA_ROOT is required for pristine DSR fact verification.");

            string pristineGameRoot = Environment.GetEnvironmentVariable("DS1TR_PRISTINE_DSR_ROOT");
            FogDsrFacts facts = FogDsrFacts.Read(root, pristineGameRoot);
            FogDsrEventFactSource expectedEventFactSource = string.IsNullOrWhiteSpace(pristineGameRoot)
                ? FogDsrEventFactSource.VerifiedFogReference
                : FogDsrEventFactSource.PristineGameInstall;
            if (facts.EventFactSource != expectedEventFactSource)
                throw new InvalidDataException($"Expected EMEVD facts from {expectedEventFactSource}, got {facts.EventFactSource}.");
            foreach (FogDsrFacts.Map map in facts.Maps.Values)
            {
                foreach (FogDsrObjectActFact act in map.ObjectActDetails)
                {
                    FogDsrObjectFact[] parts = map.ObjectDetails.Where(part =>
                        string.Equals(part.Name, act.PartName, StringComparison.Ordinal)).ToArray();
                    int? expectedEntityId = parts.Length == 1 ? parts[0].EntityId : null;
                    if (parts.Length > 1 || act.ObjectEntityId != expectedEntityId)
                        throw new InvalidDataException($"Map '{map.Id}' ObjAct '{act.PartName}' does not resolve to its unique pristine object part.");
                }
                HashSet<int> expectedObjectActFlags = map.ObjectActDetails.Select(act => act.EventFlagId)
                    .Where(flag => flag > 0).ToHashSet();
                if (!map.ObjectActFlags.SetEquals(expectedObjectActFlags))
                    throw new InvalidDataException($"Map '{map.Id}' ObjAct flag inventory does not match its typed MSB ObjAct records.");
            }
            string eventDirectory = Path.Combine(string.IsNullOrWhiteSpace(pristineGameRoot)
                ? Path.Combine(root, "dist", "DS1R")
                : Path.GetFullPath(pristineGameRoot), "event");
            if (facts.EventFactSource == FogDsrEventFactSource.PristineGameInstall)
            {
                IReadOnlyList<FogAreaTagPolicy> bossAreaTags = FogAnnotationPolicyResolver.ResolveBossAreaTagPolicies(
                    facts, FogProjectAnnotationPolicy.BossAreaTagPolicies);
                if (bossAreaTags.Count != 24 || bossAreaTags.Any(tag => tag.Tags != FogAreaTags.Boss)
                    || bossAreaTags.Select(tag => tag.Area).Distinct().Count() != 24)
                    throw new InvalidDataException("The project boss-area policy did not resolve 24 unique pristine encounter nodes.");
                Console.WriteLine("Project boss-area policy: 24 area tags resolve to the expected pristine HandleBossDefeat event and MSB enemy-part anchors.");
                VerifyBlighttownKeyRewardProvenance(facts, eventDirectory);

                IReadOnlyList<FogAreaLink> areaLinks = FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(
                    facts, FogProjectAnnotationPolicy.AreaLinkPolicies);
                FogAreaLinkPolicy[] depthsLinks = FogProjectAnnotationPolicy.AreaLinkPolicies
                    .Where(policy => policy.Source == new FogAreaId("depths")).ToArray();
                FogAreaLinkPolicy[] tombOfTheGiantsLinks = FogProjectAnnotationPolicy.AreaLinkPolicies
                    .Where(policy => policy.Source == new FogAreaId("totg")).ToArray();
                if (depthsLinks.Length != 1 || tombOfTheGiantsLinks.Length != 1 || areaLinks.Count != 2
                    || depthsLinks[0].Destination != new FogAreaId("depths_gaping")
                    || depthsLinks[0].Kind != FogAreaLinkKind.Static
                    || areaLinks.Single(link => link.Destination == new FogAreaId("depths_gaping")).Condition != null
                    || areaLinks.Single(link => link.Destination == new FogAreaId("depths_gaping")).Tags != FogAreaLinkTags.Hard
                    || !depthsLinks[0].Rationale.Contains("hazardous drop", StringComparison.Ordinal)
                    || tombOfTheGiantsLinks[0].Destination != new FogAreaId("totg_lower")
                    || tombOfTheGiantsLinks[0].Kind != FogAreaLinkKind.Static
                    || areaLinks.Single(link => link.Destination == new FogAreaId("totg_lower")).Condition != null
                    || areaLinks.Single(link => link.Destination == new FogAreaId("totg_lower")).Tags != FogAreaLinkTags.Hard
                    || !tombOfTheGiantsLinks[0].Rationale.Contains("event 11310002", StringComparison.Ordinal))
                    throw new InvalidDataException("A project-owned hazardous hard route is incomplete or does not resolve against its stable area/map anchors.");
                VerifyTotgAreaDropEvidence(facts, eventDirectory);
                VerifyDepthsKeyDoorEvidence(facts, eventDirectory);
                VerifyDarkrootCrestGateEvidence(facts, eventDirectory);
                Console.WriteLine("Project static-route policy: the Depths-to-Gaping drop and the upper-to-lower Tomb of the Giants descent resolve as hard-only same-map edges.");
                Console.WriteLine("Project traversal ownership evidence: the Blighttown-Key door and the Darkroot Crest gate are pristine item-gated Entrance boundaries, separate from Area.To.");

                var purpleCrystalCondition = new FogCondition(FogConditionKind.Atom, Array.AsReadOnly(new[]
                {
                    FogConditionReference.KeyItem(new FogItemName("purple_cowards_crystal")),
                }));
                var purpleCrystalFixture = new FogKeyItem(
                    new FogItemName("purple_cowards_crystal"),
                    facts.RequireUniqueGameItem(FogGameItemCategory.Goods, "Purple Coward's Crystal"),
                    new FogAreaId("darkroot"),
                    FogKeyItemTags.None);
                var crossMapGlitch = new FogAreaLinkPolicy(
                    new FogAreaId("darkroot"), new FogAreaId("dlc_hallway"), purpleCrystalCondition,
                    FogAreaLinkTags.Hard, "Synthetic regression fixture for the documented Purple Crystal wrong-warp policy.",
                    Kind: FogAreaLinkKind.CrossMapGlitch);
                IReadOnlyList<FogAreaLink> resolvedCrossMapGlitch = FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(
                    facts, new[] { crossMapGlitch }, new[] { purpleCrystalFixture });
                if (resolvedCrossMapGlitch.Count != 1
                    || resolvedCrossMapGlitch[0].Destination != new FogAreaId("dlc_hallway")
                    || resolvedCrossMapGlitch[0].Condition == null
                    || resolvedCrossMapGlitch[0].Tags != FogAreaLinkTags.Hard)
                    throw new InvalidDataException("An explicitly classified cross-map glitch route did not resolve with its prerequisite and hard tag.");

                bool rejectedUnclassifiedCrossMap = false;
                try
                {
                    FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(facts, new[] { crossMapGlitch with { Kind = FogAreaLinkKind.Static } },
                        new[] { purpleCrystalFixture });
                }
                catch (InvalidDataException) { rejectedUnclassifiedCrossMap = true; }
                bool rejectedMissingKeyCondition = false;
                try { FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(facts, new[] { crossMapGlitch }); }
                catch (KeyNotFoundException) { rejectedMissingKeyCondition = true; }
                bool rejectedNonHardGlitch = false;
                try { FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(facts, new[] { crossMapGlitch with { Tags = FogAreaLinkTags.None } },
                    new[] { purpleCrystalFixture }); }
                catch (InvalidDataException) { rejectedNonHardGlitch = true; }
                bool rejectedSameMapCrossMapGlitch = false;
                try
                {
                    FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(facts, new[]
                    {
                        crossMapGlitch with { Destination = new FogAreaId("darkroot_stone") },
                    }, new[] { purpleCrystalFixture });
                }
                catch (InvalidDataException) { rejectedSameMapCrossMapGlitch = true; }
                if (!rejectedUnclassifiedCrossMap || !rejectedMissingKeyCondition || !rejectedNonHardGlitch || !rejectedSameMapCrossMapGlitch)
                    throw new InvalidDataException("The static-route policy accepted an unclassified cross-map edge, missing key reference, non-hard cross-map route, or same-map CrossMapGlitch.");
                Console.WriteLine("Project static-route guards: explicit hard cross-map routes may cross verified maps; missing key facts, unclassified crossings, non-hard crossings, and same-map CrossMapGlitch routes are rejected.");

                IReadOnlyList<FogAreaTagPolicy> areaTags = FogAnnotationPolicyResolver.ResolveAreaTagPolicies(
                    facts, FogProjectAnnotationPolicy.AreaTagPolicies);
                FogAreaId[] optionalAreaIds =
                {
                    new FogAreaId("dlc_postartorias"), new FogAreaId("greathollow_start"),
                    new FogAreaId("anorlondo_front"), new FogAreaId("asylum_loop"),
                };
                if (areaTags.Count != 35 || areaTags.Select(tag => tag.Area).Distinct().Count() != 35
                    || areaTags.Count(tag => tag.Tags.HasFlag(FogAreaTags.Trivial)) != 35
                    || areaTags.Count(tag => tag.Tags.HasFlag(FogAreaTags.Optional)) != 4
                    || !areaTags.Where(tag => tag.Tags.HasFlag(FogAreaTags.Optional)).Select(tag => tag.Area).ToHashSet().SetEquals(optionalAreaIds))
                    throw new InvalidDataException("The project optional/trivial area classification policy is incomplete, duplicated, or assigned to an unexpected area.");
                Console.WriteLine("Project area classification policy: 35 trivial connectors and 4 optional areas resolve to verified DSR map owners.");

                IReadOnlyList<FogAreaTagPolicy> completeAreaTags = FogProjectAnnotationPolicy.ResolveCompleteAreaTagPolicies(facts);
                if (completeAreaTags.Count != 131
                    || completeAreaTags.Select(tag => tag.Area).Distinct().Count() != 131
                    || !completeAreaTags.Select(tag => tag.Area).ToHashSet().SetEquals(FogProjectAnnotationPolicy.AreaIdentityOrder)
                    || completeAreaTags.Any(tag => tag.Tags == FogAreaTags.None && string.IsNullOrWhiteSpace(tag.Rationale)))
                    throw new InvalidDataException("The full project Area tag policy did not assign all 131 unique IDs with explicit default rationale.");
                Console.WriteLine("Project Area tag policy: all 131 IDs resolve; untagged nodes use the documented ordinary-progression default.");

                IReadOnlyList<FogAreaLink> completeAreaLinks = FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(
                    facts, FogProjectAnnotationPolicy.AreaLinkPolicies, Array.Empty<FogKeyItem>());
                IReadOnlyList<FogProjectEntranceBoundaryPolicy> entranceBoundaries =
                    FogAnnotationPolicyResolver.ResolveEntranceBoundaryPolicies(facts);
                FogProjectEntranceBoundaryPolicy keyDoorPolicy = entranceBoundaries.SingleOrDefault(policy =>
                    policy.ProjectTransitionId == "depths-blighttown-key-door");
                FogProjectEntranceBoundaryPolicy crestGatePolicy = entranceBoundaries.SingleOrDefault(policy =>
                    policy.ProjectTransitionId == "darkroot-crest-gate");
                if (entranceBoundaries.Count != 2
                    || keyDoorPolicy?.RuntimeId != 1001200
                    || keyDoorPolicy.InteractionEventId != 11000110
                    || keyDoorPolicy.RequiredItemName != new FogItemName("blighttown_key")
                    || keyDoorPolicy.RequiredItem != new FogGameItemId((int)FogGameItemCategory.Goods, 2007)
                    || crestGatePolicy?.RuntimeId != 1201986
                    || crestGatePolicy.InteractionEventId != 11200100
                    || crestGatePolicy.RequiredItemName != new FogItemName("crest_of_artorias")
                    || crestGatePolicy.RequiredItem != new FogGameItemId((int)FogGameItemCategory.Goods, 2002))
                    throw new InvalidDataException("The project fixed-key-door family did not resolve both keyed Entrance boundaries against pristine DSR facts.");
                FogEntrance keyDoor = keyDoorPolicy.ToFogEntrance();
                FogEntrance crestGate = crestGatePolicy.ToFogEntrance();
                var linksByArea = FogProjectAnnotationPolicy.AreaLinkPolicies
                    .Select((policy, index) => (policy.Source, Link: completeAreaLinks[index]))
                    .GroupBy(entry => entry.Source)
                    .ToDictionary(group => group.Key, group => (IReadOnlyList<FogAreaLink>)Array.AsReadOnly(group.Select(entry => entry.Link).ToArray()));
                FogArea[] completeAreaNodes = FogProjectAnnotationPolicy.AreaIdentityOrder.Select(id => new FogArea(
                    id,
                    FogProjectAnnotationPolicy.ResolveAreaProjection(id),
                    Array.AsReadOnly(new[] { FogProjectAnnotationPolicy.ResolveAreaMap(id) }),
                    completeAreaTags.Single(policy => policy.Area == id).Tags,
                    null,
                    linksByArea.TryGetValue(id, out IReadOnlyList<FogAreaLink> links) ? links : Array.Empty<FogAreaLink>())).ToArray();
                FogKeyItem keyDoorItem = new FogKeyItem(
                    keyDoorPolicy.RequiredItemName,
                    facts.RequireUniqueGameItem(FogGameItemCategory.Goods, keyDoorPolicy.RequiredItemEnglishName),
                    new FogAreaId("depths_gaping"),
                    FogKeyItemTags.None);
                FogKeyItem crestGateItem = new FogKeyItem(
                    crestGatePolicy.RequiredItemName,
                    facts.RequireUniqueGameItem(FogGameItemCategory.Goods, crestGatePolicy.RequiredItemEnglishName),
                    new FogAreaId("darkroot"),
                    FogKeyItemTags.None);
                var keyDoorCatalog = new FogAnnotationCatalog(
                    completeAreaNodes, new[] { keyDoorItem, crestGateItem }, Array.Empty<FogWarp>(), new[] { keyDoor, crestGate },
                    Array.Empty<FogCustomStart>(), Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(),
                    Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
                string[] keyDoorCatalogErrors = facts.Validate(keyDoorCatalog).ToArray();
                if (keyDoorCatalogErrors.Length > 0)
                    throw new InvalidDataException("The project-authored Depths key-door Entrance projection failed pristine fact validation: " + string.Join("; ", keyDoorCatalogErrors));
                var keyDoorGraph = new Graph();
                keyDoorGraph.Construct(new RandomizerOptions(), keyDoorCatalog.ToFogModAnnotations());
                Graph.Edge keyDoorExit = keyDoorGraph.graph["depths"].To.SingleOrDefault(edge =>
                    edge.Name == "o1309" && edge.To == "blighttown" && edge.Type == Graph.EdgeType.Exit);
                if (keyDoorExit == null || !keyDoorExit.IsFixed || keyDoorExit.Expr == null || keyDoorExit.LinkedExpr == null
                    || !keyDoorGraph.graph["blighttown"].To.Any(edge =>
                        edge.Name == "o1309" && edge.To == "depths" && edge.Type == Graph.EdgeType.Exit))
                    throw new InvalidDataException("The project-authored Blighttown-key door did not construct a fixed, bidirectional, key-gated FogMod graph entrance.");
                Graph.Edge crestGateExit = keyDoorGraph.graph["darkroot"].To.SingleOrDefault(edge =>
                    edge.Name == "o2908_0000" && edge.To == "darkroot_forest" && edge.Type == Graph.EdgeType.Exit);
                if (crestGateExit == null || !crestGateExit.IsFixed || crestGateExit.Expr == null || crestGateExit.LinkedExpr == null
                    || !keyDoorGraph.graph["darkroot_forest"].To.Any(edge =>
                        edge.Name == "o2908_0000" && edge.To == "darkroot" && edge.Type == Graph.EdgeType.Exit))
                    throw new InvalidDataException("The project-authored Crest gate did not construct a fixed, bidirectional, key-gated FogMod graph entrance.");
                Console.WriteLine("Project Entrance projection: both independently anchored fixed key doors construct bidirectional FogMod graph edges with their resolved game-item conditions.");
                var completeAreaCatalog = new FogAnnotationCatalog(
                    completeAreaNodes, Array.Empty<FogKeyItem>(), Array.Empty<FogWarp>(), Array.Empty<FogEntrance>(),
                    Array.Empty<FogCustomStart>(), Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(),
                    Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
                string[] completeAreaErrors = facts.Validate(completeAreaCatalog).ToArray();
                if (completeAreaErrors.Length > 0)
                    throw new InvalidDataException("The complete Area inventory failed pristine DSR validation: " + string.Join("; ", completeAreaErrors));
                var fullAreaGraph = new Graph();
                fullAreaGraph.Construct(new RandomizerOptions(), completeAreaCatalog.ToFogModAnnotations());
                if (fullAreaGraph.graph.Count != 131 || !fullAreaGraph.graph.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(FogProjectAnnotationPolicy.AreaIdentityOrder.Select(id => id.Value)))
                    throw new InvalidDataException($"FogMod constructed {fullAreaGraph.graph.Count} nodes from the full project Area inventory instead of 131.");
                Console.WriteLine("Project Area model: all 131 typed nodes validate against pristine maps and construct in FogMod Graph; the current route inventory is still incomplete.");

                if (HostRequest.OptionFlags.Any(option => option.Flag == "scalingbase" || option.Flag == "dumpdist"))
                    throw new InvalidDataException("The product option contract unexpectedly exposes FogMod's debug-only ScalingBase switches.");
                AnnotationData.Annotations MakeScalingFixture(string scalingBase) => new AnnotationData.Annotations
                {
                    Areas = new List<AnnotationData.Area>
                    {
                        new AnnotationData.Area { Name = "start", To = new List<AnnotationData.Side> { new AnnotationData.Side { Area = "target" } } },
                        new AnnotationData.Area { Name = "target", ScalingBase = scalingBase },
                    },
                    KeyItems = new List<AnnotationData.Item>(),
                    Entrances = new List<AnnotationData.Entrance>(),
                    Warps = new List<AnnotationData.Entrance>(),
                };
                var productOptions = new RandomizerOptions();
                productOptions["scale"] = true;
                var withDebugScalingBase = new Graph();
                withDebugScalingBase.Construct(productOptions.Copy(), MakeScalingFixture("start"));
                var withNoScalingBase = new Graph();
                withNoScalingBase.Construct(productOptions.Copy(), MakeScalingFixture(null));
                if (productOptions["scalingbase"] || productOptions["dumpdist"]
                    || withDebugScalingBase.graph["target"].ScalingBase != null
                    || withNoScalingBase.graph["target"].ScalingBase != null
                    || withDebugScalingBase.graph["target"].Cost != withNoScalingBase.graph["target"].Cost
                    || withDebugScalingBase.graph["target"].To.Count != withNoScalingBase.graph["target"].To.Count)
                    throw new InvalidDataException("The active product graph does not ignore ScalingBase consistently with its debug-only option contract.");
                Console.WriteLine("Project ScalingBase policy: all production annotations omit this debug-only override; with shipped options enabled, FogMod constructs identical graph nodes.");

                IReadOnlyList<FogDefaultFlagCollision> asylumBossFlags = FogAnnotationPolicyResolver.ResolveBossDefeatFlagCollisions(
                    facts, new[] { new FogBossDefeatFlagCollisionPolicy(new FogMapId("asylum"), "h0016B1") });
                if (asylumBossFlags.Count != 1 || asylumBossFlags[0].BossFlag != 11810900
                    || !facts.BossRewardCompletionFlags.Contains(11810900)
                    || !facts.Maps[new FogMapId("asylum")].BossDefeatHandlers.Any(handler => handler.EventId == 11810900))
                    throw new InvalidDataException("The pristine Asylum boss-defeat event ID was not resolved as the completion flag read by common event 970 for collision h0016B1.");
                FogDsrFacts.Map asylumFacts = facts.Maps[new FogMapId("asylum")];
                FogDsrCollisionFact noBossCollision = asylumFacts.CollisionDetails.FirstOrDefault(collision =>
                    !asylumFacts.EnemyDetails.Where(enemy => enemy.CollisionName == collision.Name)
                        .Any(enemy => asylumFacts.BossDefeatHandlers.Any(handler => handler.EnemyEntityId == enemy.EntityId
                            && handler.EventId > 0 && handler.EventId <= int.MaxValue
                            && facts.BossRewardCompletionFlags.Contains((int)handler.EventId))));
                if (noBossCollision == null) throw new InvalidDataException("Asylum has no collision fixture without a boss completion event.");
                bool rejectedNonBossCollision = false;
                try
                {
                    FogAnnotationPolicyResolver.ResolveBossDefeatFlagCollisions(facts,
                        new[] { new FogBossDefeatFlagCollisionPolicy(new FogMapId("asylum"), noBossCollision.Name) });
                }
                catch (InvalidDataException) { rejectedNonBossCollision = true; }
                if (!rejectedNonBossCollision)
                    throw new InvalidDataException($"The boss-defeat flag policy invented a flag for non-boss collision '{noBossCollision.Name}'.");
                Console.WriteLine("Project-owned boss-flag rule: asylum/h0016B1 resolves to 11810900 from its HandleBossDefeat event and common event 970 flag read.");
            }
            if (facts.Maps.Count != 17) throw new InvalidDataException($"Expected 17 supported DS1 maps, found {facts.Maps.Count}.");
            if (!facts.Maps.ContainsKey(new FogMapId("asylum"))) throw new InvalidDataException("The verified map facts are missing Asylum.");
            if (facts.Maps[new FogMapId("asylum")].SpawnPoints.Count == 0) throw new InvalidDataException("Asylum has no indexed spawn points.");
            if (facts.Maps.Values.SelectMany(map => map.EnemyDetails).Any(enemy => enemy.Transform == null
                || !float.IsFinite(enemy.Transform.X) || !float.IsFinite(enemy.Transform.Y) || !float.IsFinite(enemy.Transform.Z)))
                throw new InvalidDataException("The pristine MSB enemy fact index is missing finite world positions.");
            if (!facts.Maps.Values.Any(map => map.ObjectDetails.Count > 0)
                || !facts.Maps.Values.Any(map => map.CollisionDetails.Count > 0)
                || !facts.Maps.Values.Any(map => map.EnemyDetails.Count > 0)
                || !facts.Maps.Values.Any(map => map.RegionDetails.Count > 0))
                throw new InvalidDataException("The detailed MSB object, collision, enemy and region facts were not indexed.");
            if (!facts.Maps.Values.SelectMany(map => map.Treasures).Any(treasure => treasure.ItemLots.Count > 0))
                throw new InvalidDataException("No item-lot references were indexed from pristine MSB treasure events.");
            FogDsrRegionFact[] regions = facts.Maps.Values.SelectMany(map => map.RegionDetails).ToArray();
            if (Enum.GetValues<FogDsrRegionShapeKind>().Any(kind => !regions.Any(region => region.Shape.Kind == kind)))
                throw new InvalidDataException("The pristine MSB region index did not retain all observed point, box, cylinder and sphere shapes.");
            if (regions.Any(region => region.Shape.Kind == FogDsrRegionShapeKind.Box
                    && (!float.IsFinite(region.Shape.Width.Value) || !float.IsFinite(region.Shape.Height.Value) || !float.IsFinite(region.Shape.Depth.Value)))
                || regions.Any(region => region.Shape.Kind == FogDsrRegionShapeKind.Cylinder
                    && (!float.IsFinite(region.Shape.Height.Value) || !float.IsFinite(region.Shape.Radius.Value)))
                || regions.Any(region => region.Shape.Kind == FogDsrRegionShapeKind.Sphere && !float.IsFinite(region.Shape.Radius.Value)))
                throw new InvalidDataException("The pristine MSB region index contains non-finite shape dimensions.");
            if (!facts.Maps.Values.SelectMany(map => map.Treasures).Any(treasure => treasure.PartTransform != null))
                throw new InvalidDataException("The pristine MSB treasure facts were not linked to their referenced part transforms.");
            if (facts.ItemLotIds.Count == 0) throw new InvalidDataException("No pristine ItemLotParam rows were indexed.");
            if (facts.ItemLots.Count != facts.ItemLotIds.Count) throw new InvalidDataException("The detailed ItemLotParam index is incomplete.");
            if (facts.NpcDropLots.Count == 0 || !facts.NpcDropLots.Any(drop => facts.ItemLotIds.Contains(drop.ItemLotId)))
                throw new InvalidDataException("The pristine NpcParam drop-lot index did not resolve any ItemLotParam rows.");
            if (facts.EventFactSource == FogDsrEventFactSource.PristineGameInstall
                && !facts.ItemLotAwards.Any(award => award.ItemLotId.HasValue || award.InitializerEventId.HasValue))
                throw new InvalidDataException("No schema-confirmed AwardItemLot calls were indexed from the pristine DSR EMEVD archives.");
            if (facts.ItemLotAwards.Any(award => award.InitializerEventId.HasValue != award.InitializerInstructionIndex.HasValue))
                throw new InvalidDataException("An EMEVD item-lot award has incomplete InitializeEvent caller provenance.");
            if (facts.ItemsInLots.Count == 0) throw new InvalidDataException("No item IDs were indexed from pristine ItemLotParam.");
            if (facts.GameItemIds.Count <= facts.ItemsInLots.Count) throw new InvalidDataException("The GameParam item tables were not indexed independently of ItemLotParam.");
            FogGameItemId peculiarDoll = new FogGameItemId(3, 384);
            if (!string.IsNullOrWhiteSpace(pristineGameRoot)
                && (facts.RequireUniqueGameItem("Peculiar Doll") != peculiarDoll || !facts.GameItemIds.Contains(peculiarDoll)))
                throw new InvalidDataException("The clean DSR English message bundle and GameParam did not resolve goods item 3:384 as Peculiar Doll.");
            if (!string.IsNullOrWhiteSpace(pristineGameRoot)
                && facts.RequireUniqueGameItem(FogGameItemCategory.Goods, "Peculiar Doll") != peculiarDoll)
                throw new InvalidDataException("The category-scoped DSR item resolver did not resolve goods item 3:384 as Peculiar Doll.");
            if (facts.EventFactSource == FogDsrEventFactSource.PristineGameInstall)
            {
                FogGameItemId blighttownKey = facts.RequireUniqueGameItem(FogGameItemCategory.Goods, "Blighttown Key");
                int[] keyRewardLots = facts.ItemLots.Values
                    .Where(lot => lot.Items.Any(item => item.Item == blighttownKey))
                    .Select(lot => lot.Id)
                    .OrderBy(id => id)
                    .ToArray();
                if (blighttownKey != new FogGameItemId(3, 2007)
                    || !keyRewardLots.Contains(2500)
                    || !facts.ItemLotAwards.Any(award => award.EventArchiveName == "common.emevd.dcx"
                        && award.EventId == 970 && award.ItemLotId == 2500
                        && award.InitializerEventId == 0))
                    throw new InvalidDataException("The pristine common boss-reward event no longer resolves ItemLot 2500 as a Blighttown Key reward through its event-0 initializer.");
            }
            IReadOnlyList<FogKeyItem> resolvedPolicy = FogAnnotationPolicyResolver.ResolveKeyItems(facts,
                new[]
                {
                    new FogKeyItemPolicy(new FogItemName("peculiar_doll"), "Peculiar Doll", FogGameItemCategory.Goods,
                        new FogAreaId("home"), FogKeyItemTags.None),
                });
            if (resolvedPolicy.Count != 1 || resolvedPolicy[0].GameItemId != peculiarDoll
                || resolvedPolicy[0].Name != new FogItemName("peculiar_doll")
                || resolvedPolicy[0].DefaultArea != new FogAreaId("home"))
                throw new InvalidDataException("The authored key-item policy was not resolved from pristine game facts while preserving its semantic name and area.");
            KeyValuePair<int, string> startingSpawn = facts.Maps[new FogMapId("asylum")].SpawnPoints.OrderBy(entry => entry.Key).First();
            IReadOnlyList<FogCustomStart> resolvedStarts = FogAnnotationPolicyResolver.ResolveCustomStarts(facts,
                new[] { new FogCustomStartPolicy("fact-derived-start", "Fact-derived start", new FogAreaId("home"), new FogMapId("asylum"), startingSpawn.Value) });
            if (resolvedStarts.Count != 1 || resolvedStarts[0].Respawn.SpawnPoint.Value != startingSpawn.Key
                || resolvedStarts[0].Respawn.Map != new FogMapId("asylum"))
                throw new InvalidDataException("The authored custom-start policy did not resolve its named DSR spawn region to the pristine spawn-point entity ID.");
            bool rejectedMissingSpawnRegion = false;
            try
            {
                FogAnnotationPolicyResolver.ResolveCustomStarts(facts,
                    new[] { new FogCustomStartPolicy("missing-start", "Missing start", new FogAreaId("home"), new FogMapId("asylum"), "DS1TR missing spawn region") });
            }
            catch (KeyNotFoundException) { rejectedMissingSpawnRegion = true; }
            if (!rejectedMissingSpawnRegion) throw new InvalidDataException("The authored custom-start policy accepted a spawn region absent from pristine DSR.");
            FogDsrTreasureLotFact mappedTreasure = facts.FunctionalTreasureLots.First(treasure =>
                facts.ItemLotIds.Contains(treasure.ItemLotId) && !string.IsNullOrWhiteSpace(treasure.PartCollisionName));
            var collisionPolicy = new FogTreasureCollisionAreaPolicy(mappedTreasure.Map, mappedTreasure.PartCollisionName, new FogAreaId("home"));
            FogTreasureLotResolution partialLots = FogAnnotationPolicyResolver.ResolveTreasureLotLocations(facts, new[] { collisionPolicy });
            if (partialLots.IsComplete || !partialLots.Locations.Any(lot => lot.FirstLot.Value == mappedTreasure.ItemLotId && lot.Area == new FogAreaId("home"))
                || partialLots.UnresolvedReferences.Count == 0)
                throw new InvalidDataException("The treasure-lot policy resolver must map authored collision groups and report missing policies explicitly.");
            bool rejectedDuplicateCollisionPolicy = false;
            try { FogAnnotationPolicyResolver.ResolveTreasureLotLocations(facts, new[] { collisionPolicy, collisionPolicy }); }
            catch (InvalidDataException) { rejectedDuplicateCollisionPolicy = true; }
            if (!rejectedDuplicateCollisionPolicy) throw new InvalidDataException("The treasure-lot policy resolver accepted duplicate map/collision rules.");
            var scopedEnemy = facts.Maps.Values.OrderBy(map => map.Id.Value, StringComparer.Ordinal)
                .SelectMany(map => map.EnemyDetails.Where(enemy => !string.IsNullOrWhiteSpace(enemy.CollisionName))
                    .Select(enemy => (Map: map.Id, Enemy: enemy)))
                .First();
            var enemyFallback = new FogEnemyCollisionAreaPolicy(scopedEnemy.Map, scopedEnemy.Enemy.CollisionName,
                new FogAreaId("home"), null);
            var enemyOverride = new FogEnemyCollisionAreaPolicy(scopedEnemy.Map, scopedEnemy.Enemy.CollisionName,
                new FogAreaId("split"), new[] { scopedEnemy.Enemy.Name });
            FogEnemyScalingResolution partialEnemies = FogAnnotationPolicyResolver.ResolveEnemyScalingRules(facts, new[] { enemyFallback, enemyOverride });
            if (partialEnemies.IsComplete || partialEnemies.Rules.Count != 2 || !partialEnemies.Rules.Any(rule => rule.Map == scopedEnemy.Map
                    && rule.Collision == scopedEnemy.Enemy.CollisionName && rule.Area == new FogAreaId("split")
                    && rule.Includes?.SequenceEqual(new[] { scopedEnemy.Enemy.Name }) == true)
                || partialEnemies.Rules.Any(rule => rule.Map == scopedEnemy.Map && rule.Collision == scopedEnemy.Enemy.CollisionName && rule.Includes == null)
                || partialEnemies.UnresolvedCollisionGroups.Any(group => group.Map == scopedEnemy.Map && group.Collision == scopedEnemy.Enemy.CollisionName))
                throw new InvalidDataException("The enemy-scaling policy resolver failed to map a pristine map/collision group or report other unassigned groups.");
            FogEnemyScalingResolution mapDefault = FogAnnotationPolicyResolver.ResolveEnemyScalingRules(facts,
                new[] { new FogEnemyCollisionAreaPolicy(scopedEnemy.Map, string.Empty, new FogAreaId("home"), null) });
            if (mapDefault.UnresolvedCollisionGroups.Any(group => group.Map == scopedEnemy.Map)
                || mapDefault.UnresolvedCollisionGroups.Count == 0)
                throw new InvalidDataException("The enemy-scaling policy resolver did not apply a map default to that map's collisions while preserving other maps as unresolved.");
            bool rejectedIneffectiveEnemyInclude = false;
            try
            {
                FogAnnotationPolicyResolver.ResolveEnemyScalingRules(facts, new[] { enemyOverride });
            }
            catch (InvalidDataException) { rejectedIneffectiveEnemyInclude = true; }
            if (!rejectedIneffectiveEnemyInclude) throw new InvalidDataException("The enemy-scaling policy resolver accepted Includes on a single-rule collision where FogMod ignores them.");
            bool rejectedUnknownEnemyInclude = false;
            try
            {
                FogAnnotationPolicyResolver.ResolveEnemyScalingRules(facts,
                    new[] { enemyFallback, enemyOverride with { IncludedEnemyNames = new[] { "DS1TR_unknown_enemy" } } });
            }
            catch (KeyNotFoundException) { rejectedUnknownEnemyInclude = true; }
            if (!rejectedUnknownEnemyInclude) throw new InvalidDataException("The enemy-scaling policy resolver accepted an include absent from pristine DSR.");
            bool rejectedWrongCollisionInclude = false;
            string otherCollision = facts.Maps[scopedEnemy.Map].EnemyDetails
                .FirstOrDefault(enemy => !string.IsNullOrWhiteSpace(enemy.CollisionName)
                    && !string.Equals(enemy.CollisionName, scopedEnemy.Enemy.CollisionName, StringComparison.Ordinal))?.CollisionName;
            if (otherCollision != null)
            {
                try
                {
                    FogAnnotationPolicyResolver.ResolveEnemyScalingRules(facts,
                        new[]
                        {
                            enemyFallback with { Collision = otherCollision },
                            enemyOverride with { Collision = otherCollision },
                        });
                }
                catch (InvalidDataException) { rejectedWrongCollisionInclude = true; }
                if (!rejectedWrongCollisionInclude) throw new InvalidDataException("The enemy-scaling policy resolver accepted an include from a different pristine DSR collision.");
            }
            if (!string.IsNullOrWhiteSpace(pristineGameRoot))
            {
                foreach (FogGameItemCategory category in Enum.GetValues<FogGameItemCategory>())
                {
                    KeyValuePair<FogGameItemId, string>[] uniqueNames = facts.GameItemNames
                        .Where(entry => entry.Key.Category == (int)category)
                        .GroupBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
                        .Where(group => group.Count() == 1)
                        .Select(group => group.Single())
                        .ToArray();
                    if (uniqueNames.Length == 0) throw new InvalidDataException($"The clean DSR item messages have no unique item name in category {category}.");
                    KeyValuePair<FogGameItemId, string> candidate = uniqueNames[0];
                    if (facts.RequireUniqueGameItem(category, candidate.Value) != candidate.Key)
                        throw new InvalidDataException($"The category-scoped resolver returned the wrong game item for {category} name '{candidate.Value}'.");
                }

                bool rejectedWrongCategory = false;
                try { facts.RequireUniqueGameItem(FogGameItemCategory.Weapon, "Peculiar Doll"); }
                catch (KeyNotFoundException) { rejectedWrongCategory = true; }
                if (!rejectedWrongCategory) throw new InvalidDataException("The category-scoped game-fact resolver accepted an item from the wrong category.");

                bool rejectedInvalidCategory = false;
                try { facts.RequireUniqueGameItem((FogGameItemCategory)99, "Peculiar Doll"); }
                catch (ArgumentOutOfRangeException) { rejectedInvalidCategory = true; }
                if (!rejectedInvalidCategory) throw new InvalidDataException("The category-scoped game-fact resolver accepted an unknown category.");
            }
            if (!string.IsNullOrWhiteSpace(pristineGameRoot))
            {
                bool rejectedUnknownItem = false;
                try { facts.RequireUniqueGameItem("DS1TR nonexistent item sentinel"); }
                catch (KeyNotFoundException) { rejectedUnknownItem = true; }
                if (!rejectedUnknownItem) throw new InvalidDataException("The game-fact resolver accepted an unknown item name.");
            }
            if (facts.TreasureLots.Count == 0 || facts.FunctionalTreasureLots.Count == 0 || facts.ResolvedTreasureLots.Count == 0)
                throw new InvalidDataException("MSB treasure ItemLot references were not indexed and resolved against pristine ItemLotParam.");
            if (!facts.ResolvedTreasureLots.Any(treasure => treasure.PartTransform != null))
                throw new InvalidDataException("Resolved MSB treasure ItemLot references were not linked to their pristine part transforms.");
            if (!facts.ResolvedTreasureLots.Any(treasure => !string.IsNullOrWhiteSpace(treasure.PartCollisionName)))
                throw new InvalidDataException("Resolved MSB treasure ItemLot references were not linked to their pristine part collision names.");
            if (facts.FunctionalTreasureLots.Count + facts.AdditionalTreasureSlotLots.Count != facts.TreasureLots.Count
                || facts.ResolvedTreasureLots.Any(treasure => !facts.ItemLotIds.Contains(treasure.ItemLotId))
                || facts.UnresolvedTreasureLots.Any(treasure => facts.ItemLotIds.Contains(treasure.ItemLotId)))
                throw new InvalidDataException("The MSB treasure lot references were not partitioned into active, resolved, unresolved and additional slots consistently.");
            if (facts.AdditionalTreasureSlotLots.Any(treasure => treasure.Slot is < 2 or > 5))
                throw new InvalidDataException("An additional MSB treasure ItemLot reference has an invalid slot index.");
            var unmatchedSlotOne = facts.UnresolvedTreasureLots;
            AnnotationData.Annotations oracle;
            using (StreamReader reader = File.OpenText(Path.Combine(root, "dist", "fog.txt")))
                oracle = new DeserializerBuilder().Build().Deserialize<AnnotationData.Annotations>(reader);
            IReadOnlyList<FogCustomStart> projectStarts = FogAnnotationPolicyResolver.ResolveCustomStarts(facts, FogProjectAnnotationPolicy.CustomStarts);
            if (projectStarts.Count != 16 || projectStarts.Select(start => start.Id).Distinct(StringComparer.Ordinal).Count() != 16)
                throw new InvalidDataException("The initial project-owned custom-start policy did not resolve 16 uniquely named pristine DSR spawn points.");
            FogCustomStartPolicy[] keylessDepthsStarts = FogProjectAnnotationPolicy.CustomStarts
                .Where(start => start.Semantics.HasFlag(FogCustomStartSemantics.BypassDepthsEntryKeyDoor)).ToArray();
            if (keylessDepthsStarts.Length != 1 || keylessDepthsStarts[0].Id != "depths-bonfire"
                || keylessDepthsStarts[0].Area != new FogAreaId("depths")
                || keylessDepthsStarts[0].Map != new FogMapId("depths")
                || projectStarts.Single(start => start.Id == "depths-bonfire").Semantics != FogCustomStartSemantics.BypassDepthsEntryKeyDoor
                || projectStarts.Where(start => start.Id != "depths-bonfire").Any(start => start.Semantics != FogCustomStartSemantics.None))
                throw new InvalidDataException("The Depths key-door progression exception must apply only to the Depths bonfire custom start.");
            var projectStartLocations = projectStarts.Select(start => $"{start.Respawn.Map.Value}:{start.Respawn.SpawnPoint.Value}")
                .ToHashSet(StringComparer.Ordinal);
            string[] oracleStartOrder = oracle.CustomStarts.Select(start =>
            {
                string[] parts = start.Respawn.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || !AnnotationData.nameSpecs.ContainsKey(parts[0])
                    || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int spawnId))
                    throw new InvalidDataException($"The development oracle contains an invalid custom-start respawn '{start.Respawn}'.");
                return $"{parts[0]}:{spawnId}";
            }).ToArray();
            HashSet<string> oracleStartLocations = oracleStartOrder.ToHashSet(StringComparer.Ordinal);
            bool startLocationsMatch = projectStartLocations.SetEquals(oracleStartLocations);
            bool startOrderMatches = projectStarts.Select(start => $"{start.Respawn.Map.Value}:{start.Respawn.SpawnPoint.Value}")
                .SequenceEqual(oracleStartOrder, StringComparer.Ordinal);
            string[] projectStartAreas = FogProjectAnnotationPolicy.CustomStarts.Select(start => start.Area.Value).ToArray();
            string[] oracleStartAreas = oracle.CustomStarts.Select(start => start.Area).ToArray();
            bool startAreasMatch = projectStartAreas.SequenceEqual(oracleStartAreas, StringComparer.Ordinal);
            string[] displayNameDifferences = projectStarts.Zip(oracle.CustomStarts,
                    (project, reference) => project.DisplayName == reference.Name ? null : $"{project.Id}: '{reference.Name}' -> '{project.DisplayName}'")
                .Where(difference => difference != null).ToArray();
            bool displayNamesPresent = projectStarts.All(start => !string.IsNullOrWhiteSpace(start.DisplayName));
            bool displayChangesAreReviewed = displayNameDifferences.Length == 2
                && displayNameDifferences[0].StartsWith("anorlondo-cathedral-bonfire:", StringComparison.Ordinal)
                && displayNameDifferences[1].StartsWith("paintedworld-bonfire:", StringComparison.Ordinal);
            Console.WriteLine($"Project-owned custom-start DSR resolution: {projectStarts.Count} named regions resolved; reference locations exact={startLocationsMatch}, authored order exact={startOrderMatches}, owning areas exact={startAreasMatch}, display names present={displayNamesPresent}; all starts have graph nodes and matching source maps; Depths key-door bypass is explicit.");
            Console.WriteLine($"Custom-start display comparison: {projectStarts.Count - displayNameDifferences.Length}/{projectStarts.Count} exact; {displayNameDifferences.Length} reviewed text-only clarifications classified R: {string.Join("; ", displayNameDifferences)}.");
            if (!startLocationsMatch || !startOrderMatches || !startAreasMatch || !displayNamesPresent || !displayChangesAreReviewed)
                throw new InvalidDataException("The project-owned custom-start locations, deterministic order, owning areas, display names, or reviewed label differences are invalid.");
            FogArea[] startAreas = FogProjectAnnotationPolicy.CustomStartAreaNodes.ToArray();
            if (startAreas.Length != FogProjectAnnotationPolicy.CustomStarts.Select(start => start.Area).Distinct().Count()
                || FogProjectAnnotationPolicy.CustomStarts.Any(start =>
                    !startAreas.Any(area => area.Id == start.Area && area.Maps.Contains(start.Map))))
                throw new InvalidDataException("The project-owned start-area slice does not contain every start's logical node and pristine map membership.");
            FogArea[] reviewedStartAreas = FogProjectAnnotationPolicy.ReviewedStartAreaRecords.ToArray();
            var expectedReviewedAreaIds = new HashSet<FogAreaId>
            {
                new FogAreaId("firelink"),
                new FogAreaId("parish_andre"),
                new FogAreaId("blighttown"),
                new FogAreaId("anorlondo_start"),
                new FogAreaId("anorlondo_building"),
                new FogAreaId("catacombs"),
                new FogAreaId("totg"),
                new FogAreaId("depths"),
            };
            bool reviewedRoutesAreValid = reviewedStartAreas.All(area => area.Id switch
            {
                var id when id == new FogAreaId("depths") => area.To.Count == 1 && area.To[0].Destination == new FogAreaId("depths_gaping")
                    && area.To[0].Condition == null && area.To[0].Tags == FogAreaLinkTags.Hard,
                var id when id == new FogAreaId("totg") => area.To.Count == 1 && area.To[0].Destination == new FogAreaId("totg_lower")
                    && area.To[0].Condition == null && area.To[0].Tags == FogAreaLinkTags.Hard,
                _ => area.To.Count == 0,
            });
            if (reviewedStartAreas.Length != 8
                || !reviewedStartAreas.Select(area => area.Id).ToHashSet().SetEquals(expectedReviewedAreaIds)
                || reviewedStartAreas.Select(area => area.Id).Distinct().Count() != reviewedStartAreas.Length
                || reviewedStartAreas.Any(area => area.Tags != FogAreaTags.None || area.ScalingBase != null)
                || !reviewedRoutesAreValid
                || reviewedStartAreas.Any(reviewed => !startAreas.Any(area => area == reviewed)))
                throw new InvalidDataException("The independently reviewed start-area records are incomplete or differ from the declared start-area slice.");
            IReadOnlyList<FogAreaNoStaticLinksPolicy> noStaticBossAreas =
                FogAnnotationPolicyResolver.ResolveAreaNoStaticLinksPolicies(facts);
            var expectedNoStaticBossAreas = FogProjectAnnotationPolicy.BossAreaTagPolicies
                .Select(policy => policy.Area)
                .Append(new FogAreaId("catacombs_prepinwheel"))
                .ToHashSet();
            FogAreaNoStaticLinksPolicy pinwheelPolicy = noStaticBossAreas.SingleOrDefault(policy =>
                policy.Area == new FogAreaId("catacombs_pinwheel"));
            FogAreaNoStaticLinksPolicy prepinwheelPolicy = noStaticBossAreas.SingleOrDefault(policy =>
                policy.Area == new FogAreaId("catacombs_prepinwheel"));
            if (noStaticBossAreas.Count != expectedNoStaticBossAreas.Count
                || !noStaticBossAreas.Select(policy => policy.Area).ToHashSet().SetEquals(expectedNoStaticBossAreas)
                || noStaticBossAreas.Any(policy => string.IsNullOrWhiteSpace(policy.Rationale)
                    || FogProjectAnnotationPolicy.AreaLinkPolicies.Any(route => route.Source == policy.Area))
                || pinwheelPolicy == null
                || pinwheelPolicy.EntranceMap != new FogMapId("catacombs")
                || pinwheelPolicy.EntranceObjectEntityId != 1301990
                || pinwheelPolicy.EntranceTriggerRegionEntityId != 1302998
                || pinwheelPolicy.EntranceEventId != 11305391
                || prepinwheelPolicy == null
                || prepinwheelPolicy.BossEventId.HasValue
                || prepinwheelPolicy.EntranceMap != new FogMapId("catacombs")
                || prepinwheelPolicy.EntranceObjectEntityId != 1301990
                || prepinwheelPolicy.EntranceTriggerRegionEntityId != 1302998
                || prepinwheelPolicy.EntranceEventId != 11305391)
                throw new InvalidDataException("The project Area empty-static-route policy is incomplete, unanchored, or conflicts with a separate static route.");
            Console.WriteLine($"Project Area topology policy: {noStaticBossAreas.Count} boss/Entrance-boundary nodes use the separate Entrance/Warp model; both sides of the Pinwheel gate are anchored to pristine Catacombs facts.");
            FogAreaTagPolicy[] startTagPolicies = FogProjectAnnotationPolicy.AreaTagPolicies
                .Where(policy => policy.Area == new FogAreaId("demonruins_izalithprelava")).ToArray();
            if (startTagPolicies.Length != 1
                || startTagPolicies[0].Area != new FogAreaId("demonruins_izalithprelava")
                || startTagPolicies[0].Tags != FogAreaTags.Trivial
                || string.IsNullOrWhiteSpace(startTagPolicies[0].Rationale)
                || startAreas.Single(area => area.Id == startTagPolicies[0].Area).Tags != FogAreaTags.Trivial)
                throw new InvalidDataException("The independently authored pre-lava connector tag rule is missing or ambiguous.");
            FogCustomStart izalithStart = projectStarts.Single(start => start.Id == "izalith-approach-bonfire");
            FogDsrFacts.Map izalithMap = facts.Maps[izalithStart.Respawn.Map];
            string izalithSpawnRegionName = izalithMap.SpawnPoints[izalithStart.Respawn.SpawnPoint.Value];
            FogDsrRegionFact izalithSpawn = izalithMap.RegionDetails.Single(region => region.Name == izalithSpawnRegionName);
            FogDsrEnemyFact nearbyBoss = izalithMap.EnemyDetails.Single(enemy => enemy.EntityId
                == izalithMap.BossDefeatHandlers.Single(handler => handler.EventId == 11410901).EnemyEntityId);
            FogDsrObjectFact[] bossBoundaryObjects = izalithMap.ObjectDetails.Where(obj =>
                (obj.Name == "o4965_0000" || obj.Name == "o4970_0000") && obj.CollisionName == nearbyBoss.CollisionName).ToArray();
            FogDsrRegionFact postBossWall = izalithMap.RegionDetails.Single(region => region.Name.Contains("\u30e0\u30ab\u30c7\u5f8c", StringComparison.Ordinal));
            FogDsrRegionFact bossRoomEntrance = izalithMap.RegionDetails.Single(region => region.EntityId == 1412897);
            double bossDistance = Distance(izalithSpawn.Transform, nearbyBoss.Transform);
            double boundaryDistance = bossBoundaryObjects.Select(obj => Distance(izalithSpawn.Transform, obj.Transform)).DefaultIfEmpty(double.PositiveInfinity).Min();
            if (nearbyBoss.Name != "c5200_0000" || nearbyBoss.CollisionName != "h0062B1"
                || bossBoundaryObjects.Length != 2 || !(boundaryDistance > 0 && boundaryDistance < bossDistance)
                || Distance(postBossWall.Transform, bossBoundaryObjects[0].Transform) > 0.1
                || Distance(nearbyBoss.Transform, bossRoomEntrance.Transform) >= Distance(izalithSpawn.Transform, bossRoomEntrance.Transform))
                throw new InvalidDataException("The pre-lava connector tag lacks its pristine DSR boss-boundary spatial anchors.");
            FogArea[] startFactAreas = startAreas.Concat(new[]
            {
                new FogArea(new FogAreaId("depths_gaping"), FogProjectAnnotationPolicy.ResolveAreaProjection(new FogAreaId("depths_gaping")),
                    new[] { new FogMapId("depths") }, FogAreaTags.Boss, null, Array.Empty<FogAreaLink>()),
                new FogArea(new FogAreaId("totg_lower"), FogProjectAnnotationPolicy.ResolveAreaProjection(new FogAreaId("totg_lower")),
                    new[] { new FogMapId("totg") }, FogAreaTags.None, null, Array.Empty<FogAreaLink>()),
            }).ToArray();
            var startCatalog = new FogAnnotationCatalog(startFactAreas, Array.Empty<FogKeyItem>(), Array.Empty<FogWarp>(),
                Array.Empty<FogEntrance>(), projectStarts, Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(),
                Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
            IReadOnlyList<string> startCatalogProblems = facts.Validate(startCatalog);
            if (startCatalogProblems.Count != 0)
                throw new InvalidDataException("The project-owned custom-start slice failed pristine fact validation: " + string.Join("; ", startCatalogProblems));
            int unmatchedAnnotated = unmatchedSlotOne.Select(treasure => treasure.ItemLotId).Distinct()
                .Count(id => oracle.LotLocations.ContainsKey(id));
            if (unmatchedAnnotated > 0)
                throw new InvalidDataException($"The pinned Fog oracle annotates {unmatchedAnnotated} map treasure IDs with no pristine ItemLotParam row.");
            Console.WriteLine($"DSR fact index: {facts.FunctionalTreasureLots.Count} slot-1 references; {facts.ResolvedTreasureLots.Count} resolve to ItemLotParam, {unmatchedSlotOne.Select(treasure => treasure.ItemLotId).Distinct().Count()} distinct slot-1 IDs do not ({unmatchedSlotOne.Where(treasure => !treasure.StartDisabled).Select(treasure => treasure.ItemLotId).Distinct().Count()} not StartDisabled, {unmatchedSlotOne.Where(treasure => treasure.StartDisabled).Select(treasure => treasure.ItemLotId).Distinct().Count()} StartDisabled); none are in the pinned oracle's active LotLocations. {facts.AdditionalTreasureSlotLots.Count} positive values are in slots 2-5. ItemLotParam tables: {string.Join(", ", facts.ItemLotParamTableIds.Select(table => $"{table.Key}={table.Value.Count}"))}.");
            Console.WriteLine($"Pinned oracle category sizes (development report only): Areas={oracle.Areas.Count}, KeyItems={oracle.KeyItems.Count}, Warps={oracle.Warps.Count}, Entrances={oracle.Entrances.Count}, CustomStarts={oracle.CustomStarts.Count}, DefaultCost={oracle.DefaultCost.Count}, Enemies={oracle.Enemies.Count}, LotLocations={oracle.LotLocations.Count}, DefaultFlagCols={oracle.DefaultFlagCols.Count}.");
            var collisionFlagFacts = new List<(FogMapId Map, FogDsrCollisionFact Collision, int BossFlag)>();
            int unresolvedFlagCollisionFacts = 0;
            foreach (KeyValuePair<string, int> entry in oracle.DefaultFlagCols)
            {
                FogMapId mapId = facts.Maps.Keys.OrderByDescending(map => map.Value.Length)
                    .FirstOrDefault(map => entry.Key.StartsWith(map.Value + "_", StringComparison.Ordinal));
                if (string.IsNullOrEmpty(mapId.Value)) { unresolvedFlagCollisionFacts++; continue; }
                string collisionName = entry.Key.Substring(mapId.Value.Length + 1);
                FogDsrCollisionFact collision = facts.Maps[mapId].CollisionDetails
                    .FirstOrDefault(fact => string.Equals(fact.Name, collisionName, StringComparison.Ordinal));
                if (collision == null) { unresolvedFlagCollisionFacts++; continue; }
                collisionFlagFacts.Add((mapId, collision, entry.Value));
            }
            Console.WriteLine($"DefaultFlagCols DSR fact correlation (development-only oracle query): entries={oracle.DefaultFlagCols.Count}, matchedCollisionFacts={collisionFlagFacts.Count}, unresolved={unresolvedFlagCollisionFacts}, exactPlayRegionFlag={collisionFlagFacts.Count(pair => pair.Collision.PlayRegionId == pair.BossFlag)}, exactDisableBonfireFlag={collisionFlagFacts.Count(pair => pair.Collision.DisableBonfireEntityId == pair.BossFlag)}, originalPlayRegionBelow10={collisionFlagFacts.Count(pair => pair.Collision.PlayRegionId < 10)}.");
            var localFlagWrites = new Dictionary<FogMapId, HashSet<int>>();
            var localEventArchives = new Dictionary<FogMapId, SoulsFormats.EMEVD>();
            int sharedMapEventArchives = 0;
            foreach (FogDsrFacts.Map map in facts.Maps.Values)
            {
                string mapCode = AnnotationData.nameSpecs[map.Id.Value].Map;
                string eventPath = Path.Combine(eventDirectory, mapCode + ".emevd.dcx");
                if (!File.Exists(eventPath) && mapCode.EndsWith("_01", StringComparison.Ordinal))
                {
                    eventPath = Path.Combine(eventDirectory, mapCode.Substring(0, mapCode.Length - 2) + "00.emevd.dcx");
                    sharedMapEventArchives++;
                }
                if (!File.Exists(eventPath)) throw new FileNotFoundException($"Pristine map event archive for {map.Id} is missing.", eventPath);
                if (!string.Equals(Path.GetFileName(eventPath), map.EventArchiveName, StringComparison.Ordinal))
                    throw new InvalidDataException($"The DSR event-fact index selected {map.EventArchiveName} for {map.Id}, but the test expected {Path.GetFileName(eventPath)}.");
                byte[] eventBytes = File.ReadAllBytes(eventPath);
                if (SoulsFormats.DCX.Is(eventBytes)) eventBytes = SoulsFormats.DCX.Decompress(eventBytes);
                SoulsFormats.EMEVD archive = SoulsFormats.EMEVD.Read(eventBytes);
                var writes = new HashSet<int>();
                foreach (SoulsFormats.EMEVD.Instruction instruction in archive.Events.SelectMany(evt => evt.Instructions)
                    .Where(instruction => instruction.Bank == 2003 && instruction.ID == 2))
                {
                    object[] args = instruction.UnpackArgs(new[]
                    {
                        (SoulsFormats.EMEVD.Instruction.ArgType)5,
                        (SoulsFormats.EMEVD.Instruction.ArgType)1,
                    }).ToArray();
                    int flagId = Convert.ToInt32(args[0]);
                    if (flagId > 0) writes.Add(flagId);
                }
                HashSet<int> indexedWrites = map.EventFlagWrites.Select(write => write.FlagId).ToHashSet();
                if (!indexedWrites.SetEquals(writes))
                    throw new InvalidDataException($"The typed DSR event-flag fact index disagrees with the independently parsed map archive for {map.Id}.");
                localFlagWrites.Add(map.Id, writes);
                localEventArchives.Add(map.Id, archive);
            }
            var defaultFlagPairs = collisionFlagFacts.Select(pair => (pair.Map, pair.BossFlag)).Distinct().ToArray();
            Console.WriteLine($"DefaultFlagCols direct map-event write correlation (development-only oracle query): mapsParsed={localFlagWrites.Count}, sharedMapEventArchives={sharedMapEventArchives}, distinctMapFlagPairs={defaultFlagPairs.Length}, pairsWrittenInSameMapEmevd={defaultFlagPairs.Count(pair => localFlagWrites[pair.Map].Contains(pair.BossFlag))}, collisionRowsWithSameMapWrite={collisionFlagFacts.Count(pair => localFlagWrites[pair.Map].Contains(pair.BossFlag))}.");
            string unmappedDirectWrites = string.Join(", ", collisionFlagFacts
                .Where(pair => !localFlagWrites[pair.Map].Contains(pair.BossFlag))
                .Select(pair => $"{pair.Map.Value}/{pair.Collision.Name}->{pair.BossFlag}")
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));
            Console.WriteLine($"DefaultFlagCols without a direct same-map SetEventFlag writer (development-only oracle query): {unmappedDirectWrites}.");
            string commonEventPath = Path.Combine(eventDirectory, "common.emevd.dcx");
            byte[] commonEventBytes = File.ReadAllBytes(commonEventPath);
            if (SoulsFormats.DCX.Is(commonEventBytes)) commonEventBytes = SoulsFormats.DCX.Decompress(commonEventBytes);
            SoulsFormats.EMEVD commonEventArchive = SoulsFormats.EMEVD.Read(commonEventBytes);
            Dictionary<int, HashSet<long>> commonFlagWriterEvents = ReadDirectFlagWriterEvents(commonEventArchive);
            if (!facts.CommonEventFlagWrites.Select(write => write.FlagId).ToHashSet().SetEquals(commonFlagWriterEvents.Keys))
                throw new InvalidDataException("The typed common EMEVD event-flag fact index disagrees with the independently parsed archive.");
            string unresolvedFlagTrace = string.Join("; ", collisionFlagFacts
                .Where(pair => !localFlagWrites[pair.Map].Contains(pair.BossFlag))
                .GroupBy(pair => (pair.Map, pair.BossFlag))
                .Select(group =>
                {
                    int flag = group.Key.BossFlag;
                    string collisions = string.Join(",", group.Select(pair => pair.Collision.Name).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal));
                    string otherMaps = string.Join(",", localFlagWrites.Where(entry => entry.Key != group.Key.Map && entry.Value.Contains(flag))
                        .Select(entry => entry.Key.Value).OrderBy(name => name, StringComparer.Ordinal));
                    string commonEvents = commonFlagWriterEvents.TryGetValue(flag, out HashSet<long> eventIds)
                        ? string.Join(",", eventIds.OrderBy(id => id)) : string.Empty;
                    return $"{group.Key.Map.Value}/{collisions}->{flag}:otherMapWriters=[{otherMaps}] commonWriters=[{commonEvents}]";
                })
                .OrderBy(value => value, StringComparer.Ordinal));
            Console.WriteLine($"DefaultFlagCols nonlocal direct writer trace (development-only oracle query): {unresolvedFlagTrace}.");
            string parameterizedFlagTrace = string.Join("; ", collisionFlagFacts
                .Where(pair => !localFlagWrites[pair.Map].Contains(pair.BossFlag))
                .GroupBy(pair => (pair.Map, pair.BossFlag))
                .Select(group =>
                {
                    int flag = group.Key.BossFlag;
                    var setters = FindInitializedFlagSetters(localEventArchives[group.Key.Map], false, localEventArchives[group.Key.Map], commonEventArchive, flag)
                        .Concat(FindInitializedFlagSetters(commonEventArchive, true, localEventArchives[group.Key.Map], commonEventArchive, flag))
                        .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);
                    return $"{group.Key.Map.Value}->{flag}:initializedSetters=[{string.Join(",", setters)}]";
                })
                .OrderBy(value => value, StringComparer.Ordinal));
            Console.WriteLine($"DefaultFlagCols initializer-to-SetEventFlag parameter trace (development-only oracle query): {parameterizedFlagTrace}.");
            int unresolvedAsylumFlag = collisionFlagFacts
                .Where(pair => pair.Map == new FogMapId("asylum") && pair.BossFlag == 11810900)
                .Select(pair => pair.BossFlag)
                .Distinct()
                .SingleOrDefault();
            if (unresolvedAsylumFlag != 0)
            {
                string allArchiveFlagSites = FindLiteralFlagSites(
                    eventDirectory, unresolvedAsylumFlag);
                Console.WriteLine($"DefaultFlagCols all-archive literal flag scan (development-only oracle query): flag={unresolvedAsylumFlag}, sites=[{allArchiveFlagSites}].");
            }
            if (facts.ResolvedTreasureLots.Any(treasure => !facts.ItemLots.ContainsKey(treasure.ItemLotId)))
                throw new InvalidDataException("A resolved MSB treasure ItemLot has no detailed ItemLotParam row.");

            FogDsrTreasureLotFact treasureSource = facts.ResolvedTreasureLots
                .Where(treasure => facts.ItemLots[treasure.ItemLotId].Items.Count > 0)
                .OrderBy(treasure => treasure.Map.Value, StringComparer.Ordinal)
                .ThenBy(treasure => treasure.TreasureEntityId)
                .First();
            FogGameItemId item = facts.ItemLots[treasureSource.ItemLotId].Items[0].Item;
            KeyValuePair<int, string> spawn = facts.Maps[new FogMapId("asylum")].SpawnPoints.OrderBy(value => value.Key).First();
            int chainEnd = treasureSource.ItemLotId;
            while (chainEnd < int.MaxValue && facts.ItemLotIds.Contains(chainEnd + 1)) chainEnd++;
            FogMapId[] homeMaps = facts.ResolvedTreasureLots
                .Where(treasure => treasure.ItemLotId >= treasureSource.ItemLotId && treasure.ItemLotId <= chainEnd)
                .Select(treasure => treasure.Map)
                .Append(new FogMapId("asylum"))
                .Distinct()
                .ToArray();
            var catalog = new FogAnnotationCatalog(
                new[] { new FogArea(new FogAreaId("home"), new FogModAreaName("home"), homeMaps, FogAreaTags.None, null, Array.Empty<FogAreaLink>()) },
                new[] { new FogKeyItem(new FogItemName("fact-derived-item"), item, new FogAreaId("home"), FogKeyItemTags.None) },
                Array.Empty<FogWarp>(), Array.Empty<FogEntrance>(),
                new[] { new FogCustomStart("fact-derived-start", "Fact-derived start", new FogAreaId("home"), new FogRespawn(new FogMapId("asylum"), new FogSpawnPointId(spawn.Key))) },
                Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(),
                new[] { new FogLotLocation(new FogItemLotId(treasureSource.ItemLotId), new FogAreaId("home")) }, Array.Empty<FogDefaultFlagCollision>());
            if (facts.Validate(catalog).Count != 0) throw new InvalidDataException("A catalog built entirely from indexed pristine facts failed validation.");

            var badStart = new FogAnnotationCatalog(catalog.Areas, catalog.KeyItems, catalog.Warps, catalog.Entrances,
                new[] { new FogCustomStart("bad-start", "Bad start", new FogAreaId("home"), new FogRespawn(new FogMapId("asylum"), new FogSpawnPointId(int.MaxValue))) },
                catalog.DefaultCosts, catalog.Enemies, catalog.LotLocations, catalog.DefaultFlagCols);
            if (!facts.Validate(badStart).Any(problem => problem.Contains("bad-start", StringComparison.Ordinal)))
                throw new InvalidDataException("The DSR fact validator accepted a nonexistent custom-start spawn point.");

            FogMapId parishMap = new FogMapId("parish");
            FogMapId asylumMap = new FogMapId("asylum");
            string entranceObjectName = facts.Maps[parishMap].Objects.OrderBy(name => name, StringComparer.Ordinal).First();
            int warpRegionId = facts.Maps[parishMap].RegionEntityIds.OrderBy(id => id).First();
            var endpointCatalog = new FogAnnotationCatalog(
                new[]
                {
                    new FogArea(new FogAreaId("home"), new FogModAreaName("home"), new[] { asylumMap }, FogAreaTags.None, null, Array.Empty<FogAreaLink>()),
                    new FogArea(new FogAreaId("parish-side"), new FogModAreaName("parish-side"), new[] { parishMap }, FogAreaTags.None, null, Array.Empty<FogAreaLink>()),
                },
                Array.Empty<FogKeyItem>(),
                new[]
                {
                    new FogWarp("warp-endpoint-map-check", warpRegionId, parishMap, "Synthetic endpoint check", FogWarpTags.Unique,
                        new FogWarpSide(new FogAreaId("home"), 0, null, null, null, FogWarpSideTags.None),
                        new FogWarpSide(new FogAreaId("parish-side"), 0, null, null, null, FogWarpSideTags.None)),
                },
                new[]
                {
                    new FogEntrance("entrance-endpoint-map-check", 700000, parishMap, "Synthetic endpoint check", null, FogEntranceTags.None,
                        new FogEntranceSide(new FogAreaId("home"), 0, 0, 0, 0, 0, null, 0, null, null, 0, 0, null, null, FogEntranceSideTags.None),
                        new FogEntranceSide(new FogAreaId("parish-side"), 0, 0, 0, 0, 0, null, 0, null, null, 0, 0, null, null, FogEntranceSideTags.None)),
                },
                Array.Empty<FogCustomStart>(), Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(),
                Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
            IReadOnlyList<string> endpointProblems = facts.Validate(endpointCatalog);
            if (!endpointProblems.Any(problem => problem.Contains("Entrance entrance-endpoint-map-check side", StringComparison.Ordinal)
                    && problem.Contains("endpoint map parish", StringComparison.Ordinal))
                || !endpointProblems.Any(problem => problem.Contains("Warp warp-endpoint-map-check side", StringComparison.Ordinal)
                    && problem.Contains("endpoint map parish", StringComparison.Ordinal)))
                throw new InvalidDataException("The DSR fact validator accepted entrance or warp sides whose logical area omits their endpoint map.");
        }

        private static Dictionary<int, HashSet<long>> ReadDirectFlagWriterEvents(SoulsFormats.EMEVD archive)
        {
            var writers = new Dictionary<int, HashSet<long>>();
            foreach (SoulsFormats.EMEVD.Event evt in archive.Events)
            {
                foreach (SoulsFormats.EMEVD.Instruction instruction in evt.Instructions)
                {
                    if (instruction.Bank != 2003 || instruction.ID != 2) continue;
                    object[] args = instruction.UnpackArgs(new[]
                    {
                        (SoulsFormats.EMEVD.Instruction.ArgType)5,
                        (SoulsFormats.EMEVD.Instruction.ArgType)1,
                    }).ToArray();
                    int flag = Convert.ToInt32(args[0]);
                    if (flag <= 0) continue;
                    if (!writers.TryGetValue(flag, out HashSet<long> eventIds))
                    {
                        eventIds = new HashSet<long>();
                        writers.Add(flag, eventIds);
                    }
                    eventIds.Add(evt.ID);
                }
            }
            return writers;
        }

        private static IReadOnlyList<string> FindInitializedFlagSetters(
            SoulsFormats.EMEVD sourceArchive,
            bool sourceIsCommon,
            SoulsFormats.EMEVD localTargetArchive,
            SoulsFormats.EMEVD commonTargetArchive,
            int flag)
        {
            SoulsFormats.EMEVD.Event eventZero = sourceArchive.Events.FirstOrDefault(evt => evt.ID == 0);
            if (eventZero == null) return Array.Empty<string>();
            var matches = new List<string>();
            foreach (SoulsFormats.EMEVD.Instruction initializer in eventZero.Instructions)
            {
                if (initializer.Bank != 2000 || initializer.ID is not (0 or 6) || initializer.ArgData == null || initializer.ArgData.Length < 12)
                    continue;
                long targetId = BitConverter.ToUInt32(initializer.ArgData, 4);
                SoulsFormats.EMEVD targetArchive = sourceIsCommon || initializer.ID == 6 ? commonTargetArchive : localTargetArchive;
                SoulsFormats.EMEVD.Event target = targetArchive.Events.FirstOrDefault(evt => evt.ID == targetId);
                if (target == null) continue;

                int payloadLength = initializer.ArgData.Length - 8;
                for (int sourceOffset = 0; sourceOffset <= payloadLength - sizeof(int); sourceOffset += sizeof(int))
                {
                    if (BitConverter.ToInt32(initializer.ArgData, 8 + sourceOffset) != flag) continue;
                    foreach (SoulsFormats.EMEVD.Parameter parameter in target.Parameters)
                    {
                        if (parameter.SourceStartByte != sourceOffset || parameter.TargetStartByte != 0 || parameter.ByteCount != sizeof(int)
                            || parameter.InstructionIndex < 0 || parameter.InstructionIndex >= target.Instructions.Count)
                            continue;
                        SoulsFormats.EMEVD.Instruction setter = target.Instructions[checked((int)parameter.InstructionIndex)];
                        if (setter.Bank == 2003 && setter.ID == 2)
                            matches.Add($"event={target.ID}@payload+{sourceOffset}");
                    }
                }
            }
            return Array.AsReadOnly(matches.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }

        private static string FindLiteralFlagSites(string eventDirectory, int flag)
        {
            var sites = new List<string>();
            foreach (string path in Directory.GetFiles(eventDirectory, "*.emevd.dcx").OrderBy(value => value, StringComparer.Ordinal))
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (SoulsFormats.DCX.Is(bytes)) bytes = SoulsFormats.DCX.Decompress(bytes);
                SoulsFormats.EMEVD archive = SoulsFormats.EMEVD.Read(bytes);
                foreach (SoulsFormats.EMEVD.Event evt in archive.Events)
                {
                    for (int instructionIndex = 0; instructionIndex < evt.Instructions.Count; instructionIndex++)
                    {
                        SoulsFormats.EMEVD.Instruction instruction = evt.Instructions[instructionIndex];
                        for (int offset = 0; offset <= instruction.ArgData.Length - sizeof(int); offset += sizeof(int))
                        {
                            if (BitConverter.ToInt32(instruction.ArgData, offset) == flag)
                            {
                                string detail = string.Empty;
                                if (instruction.Bank == 2000 && instruction.ID == 0 && instruction.ArgData.Length >= 8)
                                {
                                    long targetId = BitConverter.ToUInt32(instruction.ArgData, 4);
                                    SoulsFormats.EMEVD.Event target = archive.Events.FirstOrDefault(candidate => candidate.ID == targetId);
                                    int sourceOffset = offset - 8;
                                    string targetParameters = target == null ? "missing" : string.Join("|", target.Parameters.Select(parameter =>
                                        $"src={parameter.SourceStartByte},dst={parameter.TargetStartByte},bytes={parameter.ByteCount},ins={parameter.InstructionIndex}"));
                                    string mappedParameters = target == null ? "none" : string.Join("|", target.Parameters
                                        .Where(parameter => parameter.SourceStartByte == sourceOffset)
                                        .Select(parameter => $"dst={parameter.TargetStartByte},bytes={parameter.ByteCount},ins={parameter.InstructionIndex}"));
                                    string targetOps = target == null ? "missing" : string.Join("|", target.Instructions
                                        .Select((operation, index) => $"{index}:{operation.Bank}[{operation.ID}]"));
                                    detail = $"->target={targetId},sourceOffset={sourceOffset},mapped=[{mappedParameters}],parameters=[{targetParameters}],ops=[{targetOps}]";
                                }
                                sites.Add($"{Path.GetFileName(path)}:{evt.ID}@{instructionIndex}={instruction.Bank}[{instruction.ID}]+{offset}{detail}");
                            }
                        }
                    }
                }
            }
            return string.Join(",", sites);
        }

        private static double Distance(FogDsrTransform left, FogDsrTransform right)
        {
            double x = left.X - right.X;
            double y = left.Y - right.Y;
            double z = left.Z - right.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static void VerifyTotgAreaDropEvidence(FogDsrFacts facts, string eventDirectory)
        {
            FogDsrFacts.Map map = facts.Maps[new FogMapId("totg")];
            FogDsrRegionFact trigger = map.RegionDetails.SingleOrDefault(region => region.EntityId == 1313400);
            FogDsrRegionFact landing = map.RegionDetails.SingleOrDefault(region => region.EntityId == 1312121);
            if (trigger == null || landing == null
                || !trigger.Name.Contains("蹴落とされる判定", StringComparison.Ordinal)
                || !landing.Name.Contains("蹴落とした後にいる場所", StringComparison.Ordinal)
                || map.EventArchiveName != "m13_01_00_00.emevd.dcx")
                throw new InvalidDataException("The pristine ToTG MSB no longer has the reviewed Patches trigger/landing region anchors.");

            string eventPath = Path.Combine(eventDirectory, map.EventArchiveName);
            byte[] bytes = File.ReadAllBytes(eventPath);
            if (SoulsFormats.DCX.Is(bytes)) bytes = SoulsFormats.DCX.Decompress(bytes);
            SoulsFormats.EMEVD archive = SoulsFormats.EMEVD.Read(bytes);
            SoulsFormats.EMEVD.Event evt = archive.Events.SingleOrDefault(candidate => candidate.ID == 11310002);
            if (evt == null || evt.Instructions.Count <= 39)
                throw new InvalidDataException("The pristine ToTG Patches event 11310002 is missing or has an unexpected instruction count.");

            bool IsRegionUse(int instructionIndex, int bank, int instructionId, int regionId)
            {
                if (instructionIndex >= evt.Instructions.Count) return false;
                SoulsFormats.EMEVD.Instruction instruction = evt.Instructions[instructionIndex];
                return instruction.Bank == bank && instruction.ID == instructionId
                    && instruction.ArgData.Length >= 12
                    && BinaryPrimitives.ReadInt32LittleEndian(instruction.ArgData.AsSpan(8, sizeof(int))) == regionId;
            }

            if (!IsRegionUse(7, 3, 2, trigger.EntityId)
                || !new[] { 24, 32, 39 }.All(index => IsRegionUse(index, 2004, 3, landing.EntityId)))
                throw new InvalidDataException("The pristine ToTG Patches event no longer links its trigger region to the reviewed landing region through the expected operations.");
        }

        private static void VerifyDepthsKeyDoorEvidence(FogDsrFacts facts, string eventDirectory)
        {
            FogDsrFacts.Map map = facts.Maps[new FogMapId("depths")];
            FogDsrObjectFact door = map.ObjectDetails.SingleOrDefault(obj => obj.EntityId == 1001200);
            FogGameItemId key = facts.RequireUniqueGameItem(FogGameItemCategory.Goods, "Blighttown Key");
            string eventPath = Path.Combine(eventDirectory, map.EventArchiveName);
            byte[] bytes = File.ReadAllBytes(eventPath);
            if (SoulsFormats.DCX.Is(bytes)) bytes = SoulsFormats.DCX.Decompress(bytes);
            SoulsFormats.EMEVD archive = SoulsFormats.EMEVD.Read(bytes);
            SoulsFormats.EMEVD.Event evt = archive.Events.SingleOrDefault(candidate => candidate.ID == 11000110);
            if (door == null || door.Name != "o1309" || key != new FogGameItemId(3, 2007) || evt == null)
                throw new InvalidDataException("The pristine Depths Blighttown-Key door, object, or English item identity is missing.");

            bool hasKeyCheck = evt.Instructions.Any(instruction => instruction.Bank == 3 && instruction.ID == 4
                && instruction.ArgData.Length >= 12
                && instruction.ArgData[1] == key.Category
                && BinaryPrimitives.ReadInt32LittleEndian(instruction.ArgData.AsSpan(4, sizeof(int))) == key.ItemId
                && instruction.ArgData[8] == 1);
            bool hasDoorAction = evt.Instructions.Any(instruction => instruction.Bank == 3 && instruction.ID == 5
                && instruction.ArgData.Length >= 12
                && BinaryPrimitives.ReadInt32LittleEndian(instruction.ArgData.AsSpan(8, sizeof(int))) == door.EntityId);
            bool hasShortWarp = evt.Instructions.Any(instruction => instruction.Bank == 2004 && instruction.ID == 41);
            if (!hasKeyCheck || !hasDoorAction || !hasShortWarp)
                throw new InvalidDataException("The pristine Depths transition event no longer ties the Blighttown Key check and object action to the short-warp door behavior.");
        }

        private static void VerifyDarkrootCrestGateEvidence(FogDsrFacts facts, string eventDirectory)
        {
            FogDsrFacts.Map map = facts.Maps[new FogMapId("darkroot")];
            FogDsrObjectFact gate = map.ObjectDetails.SingleOrDefault(obj => obj.EntityId == 1201986);
            FogDsrRegionFact actionRegion = map.RegionDetails.SingleOrDefault(region => region.EntityId == 1202500);
            FogGameItemId crest = facts.RequireUniqueGameItem(FogGameItemCategory.Goods, "Crest of Artorias");
            string eventPath = Path.Combine(eventDirectory, map.EventArchiveName);
            byte[] bytes = File.ReadAllBytes(eventPath);
            if (SoulsFormats.DCX.Is(bytes)) bytes = SoulsFormats.DCX.Decompress(bytes);
            SoulsFormats.EMEVD archive = SoulsFormats.EMEVD.Read(bytes);
            SoulsFormats.EMEVD.Event keyCheckEvent = archive.Events.SingleOrDefault(candidate => candidate.ID == 11200100);
            SoulsFormats.EMEVD.Event gateActionEvent = archive.Events.SingleOrDefault(candidate => candidate.ID == 11200110);
            bool hasKeyCheck = keyCheckEvent != null && keyCheckEvent.Instructions.Any(instruction => instruction.Bank == 3 && instruction.ID == 4
                && instruction.ArgData.Length >= 12
                && instruction.ArgData[1] == crest.Category
                && BinaryPrimitives.ReadInt32LittleEndian(instruction.ArgData.AsSpan(4, sizeof(int))) == crest.ItemId
                && instruction.ArgData[8] == 1);
            bool hasResolvedGateAction = false;
            if (gateActionEvent != null)
            {
                for (int instructionIndex = 0; instructionIndex < gateActionEvent.Instructions.Count; instructionIndex++)
                {
                    SoulsFormats.EMEVD.Instruction instruction = gateActionEvent.Instructions[instructionIndex];
                    if (instruction.Bank != 3 || instruction.ID != 18) continue;
                    SoulsFormats.EMEVD.Parameter[] bindings = gateActionEvent.Parameters.Where(parameter =>
                        parameter.InstructionIndex == instructionIndex && parameter.TargetStartByte == 8
                        && parameter.ByteCount == sizeof(int)).ToArray();
                    foreach (SoulsFormats.EMEVD.Parameter binding in bindings)
                    {
                        int sourceOffset = checked(8 + checked((int)binding.SourceStartByte));
                        foreach (SoulsFormats.EMEVD.Event caller in archive.Events)
                        {
                            foreach (SoulsFormats.EMEVD.Instruction call in caller.Instructions.Where(candidate => candidate.Bank == 2000
                                && candidate.ID == 0 && candidate.ArgData.Length >= 8
                                && BinaryPrimitives.ReadInt32LittleEndian(candidate.ArgData.AsSpan(4, sizeof(int))) == gateActionEvent.ID))
                            {
                                if (actionRegion != null && sourceOffset + sizeof(int) <= call.ArgData.Length
                                    && BinaryPrimitives.ReadInt32LittleEndian(call.ArgData.AsSpan(sourceOffset, sizeof(int))) == actionRegion.EntityId)
                                    hasResolvedGateAction = true;
                            }
                        }
                    }
                }
            }

            if (gate == null || gate.Name != "o2908_0000" || actionRegion == null
                || crest != new FogGameItemId(3, 2002) || !hasKeyCheck || !hasResolvedGateAction)
                throw new InvalidDataException("The pristine Darkroot Crest gate, logical boundary action region, or item-check chain no longer resolves.");
        }

        private static void VerifyBlighttownKeyRewardProvenance(FogDsrFacts facts, string eventDirectory)
        {
            string commonPath = Path.Combine(eventDirectory, "common.emevd.dcx");
            byte[] bytes = File.ReadAllBytes(commonPath);
            if (SoulsFormats.DCX.Is(bytes)) bytes = SoulsFormats.DCX.Decompress(bytes);
            SoulsFormats.EMEVD common = SoulsFormats.EMEVD.Read(bytes);
            SoulsFormats.EMEVD.Event startup = common.Events.Single(evt => evt.ID == 0);
            int[] initializers = startup.Instructions.Select((instruction, index) => (instruction, index))
                .Where(entry => entry.instruction.Bank == 2000 && entry.instruction.ID == 0
                    && entry.instruction.ArgData.Length >= 16
                    && BinaryPrimitives.ReadInt32LittleEndian(entry.instruction.ArgData.AsSpan(4, sizeof(int))) == 970
                    && BinaryPrimitives.ReadInt32LittleEndian(entry.instruction.ArgData.AsSpan(8, sizeof(int))) == 2
                    && BinaryPrimitives.ReadInt32LittleEndian(entry.instruction.ArgData.AsSpan(12, sizeof(int))) == 2500)
                .Select(entry => entry.index)
                .ToArray();
            FogGameItemId key = facts.RequireUniqueGameItem(FogGameItemCategory.Goods, "Blighttown Key");
            FogDsrFacts.Map depths = facts.Maps[new FogMapId("depths")];
            bool lotContainsKey = facts.ItemLots.TryGetValue(2500, out FogDsrItemLotFact lot)
                && lot.Items.Any(item => item.Item == key);
            bool awardFactMatches = initializers.Length == 1 && facts.ItemLotAwards.Any(award => award.EventArchiveName == "common.emevd.dcx"
                && award.EventId == 970 && award.ItemLotId == 2500
                && award.InitializerEventId == 0 && award.InitializerInstructionIndex == initializers[0]);
            bool gapingDefeatUnlocksReward = facts.BossRewardCompletionFlags.Contains(2)
                && depths.EventFlagWrites.Contains(new FogDsrEventFlagWrite(11000001, 2))
                && FogProjectAnnotationPolicy.BossAreaTagPolicies.Any(policy =>
                    policy.Area == new FogAreaId("depths_gaping") && policy.BossEventId == 11000001);
            if (key != new FogGameItemId(3, 2007) || initializers.Length != 1 || !lotContainsKey
                || !awardFactMatches || !gapingDefeatUnlocksReward)
                throw new InvalidDataException("The pristine Gaping Dragon reward chain no longer proves that common event 970 awards Blighttown Key from ItemLot 2500 after the boss event sets completion flag 2.");
        }
    }
}
