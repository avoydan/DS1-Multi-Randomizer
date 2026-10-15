using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FogMod;
using static FogMod.AnnotationData;

namespace FogHost
{
    public enum FogCompactCategoryState { Complete, Partial, Blocked }

    public sealed record FogCompactCategoryCoverage(
        string Category,
        FogCompactCategoryState State,
        int GeneratedCount,
        int ExplicitExceptionCount,
        int UnresolvedCount,
        string Notes);

    public enum FogLogicalSourceKind
    {
        Map,
        Collision,
        Enemy,
        Treasure,
        ItemLot,
        BossEncounter,
        Object,
        EventReward,
        Region,
    }

    /// <summary>Context for a single ownership lookup. A source may omit fields not applicable to its kind.</summary>
    public sealed record FogLogicalAreaQuery(
        FogLogicalSourceKind Kind,
        FogMapId Map,
        string Collision = null,
        int? EntityId = null,
        long? EventId = null,
        int? ItemLotId = null,
        FogAreaId? ExplicitArea = null);

    public sealed record FogLogicalAreaResolution(
        FogAreaId? Area,
        string RuleId,
        bool UsedMapDefault,
        string Explanation)
    {
        public bool IsResolved => Area.HasValue;
    }

    /// <summary>
    /// Single DS1TR area-ownership resolver shared by generated Fog categories. Exact authored
    /// encounter/start/entrance anchors win; ordinary objects on a multi-area map use that map's
    /// deterministic project default. It records the rule used so fallback coverage is visible.
    /// </summary>
    public sealed class FogLogicalAreaResolver
    {
        private readonly IReadOnlyDictionary<FogMapId, FogAreaId> mapDefaults;
        private readonly IReadOnlyDictionary<(FogMapId Map, int EntityId), FogAreaId> bossEntities;
        private readonly IReadOnlyDictionary<(FogMapId Map, long EventId), FogAreaId> bossEvents;
        private readonly IReadOnlyDictionary<(FogMapId Map, string Collision), FogAreaId> bossCollisions;
        private readonly IReadOnlyDictionary<(FogMapId Map, int EntityId), FogAreaId> startRegions;
        private readonly IReadOnlyDictionary<(FogMapId Map, int EntityId), FogAreaId> entranceObjects;
        private readonly IReadOnlyDictionary<(FogMapId Map, long EventId, int EntityId, int ItemLotId), FogAreaId> characterDeathRewardAreas;
        private readonly IReadOnlySet<FogAreaId> areaIds;

        public FogLogicalAreaResolver(FogDsrFacts facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Compact Area ownership requires facts from a clean DSR game directory.");

            var defaults = new Dictionary<FogMapId, FogAreaId>();
            foreach (FogAreaId area in FogProjectAnnotationPolicy.AreaIdentityOrder)
            {
                FogMapId map = FogProjectAnnotationPolicy.ResolveAreaMap(area);
                if (!facts.Maps.ContainsKey(map)) throw new KeyNotFoundException($"Project Area '{area}' refers to missing pristine map '{map}'.");
                if (!defaults.ContainsKey(map)) defaults.Add(map, area);
            }
            mapDefaults = new System.Collections.ObjectModel.ReadOnlyDictionary<FogMapId, FogAreaId>(defaults);
            areaIds = FogProjectAnnotationPolicy.AreaIdentityOrder.ToHashSet();

            var deathRewardAreas = new Dictionary<(FogMapId, long, int, int), FogAreaId>();
            foreach (FogCharacterDeathRewardAreaPolicy policy in FogProjectAnnotationPolicy.CharacterDeathRewardAreaPolicies)
            {
                if (!areaIds.Contains(policy.Area)) throw new KeyNotFoundException($"Character-death reward policy refers to unknown Area '{policy.Area}'.");
                if (!facts.Maps.TryGetValue(policy.Map, out FogDsrFacts.Map rewardMap))
                    throw new KeyNotFoundException($"Character-death reward policy refers to missing pristine map '{policy.Map}'.");
                FogDsrEnemyFact[] characters = rewardMap.EnemyDetails.Where(enemy => enemy.EntityId == policy.CharacterEntityId).ToArray();
                FogDsrItemLotAwardFact[] awards = facts.ItemLotAwards.Where(award =>
                    string.Equals(award.EventArchiveName, "common.emevd.dcx", StringComparison.OrdinalIgnoreCase)
                    && award.EventId == policy.EventId
                    && award.RequiredEventFlagId == policy.RequiredEventFlagId
                    && award.CharacterEntityId == policy.CharacterEntityId
                    && award.ItemLotId == policy.ItemLotId).ToArray();
                if (characters.Length != 1 || !facts.ItemLotIds.Contains(policy.ItemLotId) || awards.Length != 1)
                    throw new InvalidDataException($"Character-death reward policy '{policy.EventId}/{policy.CharacterEntityId}/{policy.ItemLotId}' does not match one pristine enemy, ItemLot row, and common-event caller.");
                if (!deathRewardAreas.TryAdd((policy.Map, policy.EventId, policy.CharacterEntityId, policy.ItemLotId), policy.Area))
                    throw new InvalidDataException($"Character-death reward {policy.Map}/{policy.EventId}/{policy.CharacterEntityId}/{policy.ItemLotId} has multiple project Areas.");
            }
            characterDeathRewardAreas = new System.Collections.ObjectModel.ReadOnlyDictionary<(FogMapId, long, int, int), FogAreaId>(deathRewardAreas);

            var events = new Dictionary<(FogMapId, long), FogAreaId>();
            var entities = new Dictionary<(FogMapId, int), FogAreaId>();
            foreach (FogBossAreaTagPolicy policy in FogProjectAnnotationPolicy.BossAreaTagPolicies)
            {
                if (!events.TryAdd((policy.Map, policy.BossEventId), policy.Area))
                    throw new InvalidDataException($"More than one compact Area owns boss event {policy.Map}/{policy.BossEventId}.");
                FogDsrFacts.Map map = facts.Maps[policy.Map];
                foreach (string partName in policy.EnemyPartNames)
                {
                    FogDsrEnemyFact[] matches = map.EnemyDetails.Where(enemy => string.Equals(enemy.Name, partName, StringComparison.Ordinal)).ToArray();
                    if (matches.Length != 1) throw new InvalidDataException($"Boss Area '{policy.Area}' anchor '{partName}' resolves to {matches.Length} pristine enemies.");
                    if (!entities.TryAdd((policy.Map, matches[0].EntityId), policy.Area))
                        throw new InvalidDataException($"Pristine boss entity {matches[0].EntityId} has multiple compact Area owners.");
                }
            }
            bossEvents = new System.Collections.ObjectModel.ReadOnlyDictionary<(FogMapId, long), FogAreaId>(events);
            bossEntities = new System.Collections.ObjectModel.ReadOnlyDictionary<(FogMapId, int), FogAreaId>(entities);

            var collisions = new Dictionary<(FogMapId, string), FogAreaId>();
            foreach (FogBossDefeatFlagCollisionPolicy collision in FogProjectAnnotationPolicy.DefaultFlagCollisionPolicies)
            {
                FogBossAreaTagPolicy[] owners = FogProjectAnnotationPolicy.BossAreaTagPolicies
                    .Where(policy => policy.Map == collision.Map && policy.BossEventId == collision.BossCompletionEventId)
                    .ToArray();
                if (owners.Length != 1) continue;
                if (!collisions.TryAdd((collision.Map, collision.Collision), owners[0].Area))
                    throw new InvalidDataException($"Boss collision {collision.Map}/{collision.Collision} has multiple compact Area owners.");
            }
            bossCollisions = new System.Collections.ObjectModel.ReadOnlyDictionary<(FogMapId, string), FogAreaId>(collisions);

            IReadOnlyList<FogCustomStart> starts = FogAnnotationPolicyResolver.ResolveCustomStarts(facts, FogProjectAnnotationPolicy.CustomStarts);
            var regions = new Dictionary<(FogMapId, int), FogAreaId>();
            foreach (FogCustomStart start in starts)
            {
                if (!regions.TryAdd((start.Respawn.Map, start.Respawn.SpawnPoint.Value), start.Area))
                    throw new InvalidDataException($"Custom-start region {start.Respawn.Map}/{start.Respawn.SpawnPoint.Value} has multiple compact Area owners.");
            }
            startRegions = new System.Collections.ObjectModel.ReadOnlyDictionary<(FogMapId, int), FogAreaId>(regions);

            var objects = new Dictionary<(FogMapId, int), FogAreaId>();
            foreach (FogProjectEntrancePolicyV1 policy in FogProjectAnnotationPolicy.EntrancePoliciesV1)
            {
                FogAreaId objectArea = FogProjectAnnotationPolicy.ResolveAreaMap(policy.SourceArea) == policy.Map
                    ? policy.SourceArea
                    : policy.DestinationArea;
                if (!objects.TryAdd((policy.Map, policy.ObjectEntityId), objectArea))
                    throw new InvalidDataException($"Entrance object {policy.Map}/{policy.ObjectEntityId} has multiple source Areas.");
            }
            entranceObjects = new System.Collections.ObjectModel.ReadOnlyDictionary<(FogMapId, int), FogAreaId>(objects);
        }

        public IReadOnlyDictionary<FogMapId, FogAreaId> MapDefaults => mapDefaults;

        public FogLogicalAreaResolution Resolve(FogLogicalAreaQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (!Enum.IsDefined(typeof(FogLogicalSourceKind), query.Kind)) throw new ArgumentOutOfRangeException(nameof(query));
            if (query.ExplicitArea.HasValue)
            {
                if (!areaIds.Contains(query.ExplicitArea.Value)) throw new KeyNotFoundException($"Explicit project Area '{query.ExplicitArea}' is not declared in Compact Full Fog V1.");
                return new FogLogicalAreaResolution(query.ExplicitArea, "explicit-project-exception", false,
                    "The source has an explicitly authored project Area assignment.");
            }

            if (query.EntityId.HasValue)
            {
                if (query.Kind == FogLogicalSourceKind.EventReward && query.EventId.HasValue && query.ItemLotId.HasValue
                    && characterDeathRewardAreas.TryGetValue((query.Map, query.EventId.Value, query.EntityId.Value, query.ItemLotId.Value), out FogAreaId rewardArea))
                    return new FogLogicalAreaResolution(rewardArea, "character-death-reward-anchor", false,
                        "The pristine common death-reward event, character entity, and ItemLot match one explicit project Area assignment.");
                if ((query.Kind == FogLogicalSourceKind.Enemy || query.Kind == FogLogicalSourceKind.BossEncounter)
                    && bossEntities.TryGetValue((query.Map, query.EntityId.Value), out FogAreaId bossArea))
                    return new FogLogicalAreaResolution(bossArea, "boss-entity-anchor", false, "The pristine MSB enemy entity matches one authored boss encounter.");
                if (query.Kind == FogLogicalSourceKind.Region && startRegions.TryGetValue((query.Map, query.EntityId.Value), out FogAreaId startArea))
                    return new FogLogicalAreaResolution(startArea, "custom-start-region-anchor", false, "The pristine region is an authored custom-start respawn.");
                if (query.Kind == FogLogicalSourceKind.Object && entranceObjects.TryGetValue((query.Map, query.EntityId.Value), out FogAreaId entranceArea))
                    return new FogLogicalAreaResolution(entranceArea, "entrance-object-anchor", false, "The pristine object is an authored project transition source.");
            }

            if (query.EventId.HasValue && bossEvents.TryGetValue((query.Map, query.EventId.Value), out FogAreaId eventArea))
                return new FogLogicalAreaResolution(eventArea, "boss-event-anchor", false, "The pristine event is an authored boss completion event.");

            if (!string.IsNullOrWhiteSpace(query.Collision)
                && bossCollisions.TryGetValue((query.Map, query.Collision), out FogAreaId collisionArea))
                return new FogLogicalAreaResolution(collisionArea, "boss-collision-anchor", false, "The pristine collision is linked to one authored boss completion event.");

            if (mapDefaults.TryGetValue(query.Map, out FogAreaId defaultArea))
                return new FogLogicalAreaResolution(defaultArea, "map-default", true,
                    $"Project V1 assigns unanchored {query.Kind} sources to the first stable Area declaration owned by map '{query.Map}'.");

            return new FogLogicalAreaResolution(null, "unresolved-map", false, $"No project Area policy covers pristine map '{query.Map}'.");
        }
    }

    public sealed class FogCompactCatalogV1
    {
        public const string Version = "ds1tr-compact-full-fog-v1";

        public FogAnnotationCatalog Catalog { get; }
        public IReadOnlyList<FogCompactCategoryCoverage> Coverage { get; }
        public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" + Catalog.Fingerprint))).ToLowerInvariant();
        public bool IsComplete => Coverage.All(category => category.State == FogCompactCategoryState.Complete);

        internal FogCompactCatalogV1(FogAnnotationCatalog catalog, IEnumerable<FogCompactCategoryCoverage> coverage)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Coverage = Array.AsReadOnly((coverage ?? throw new ArgumentNullException(nameof(coverage))).ToArray());
        }

        /// <summary>Each call creates fresh mutable FogMod DTO instances from this immutable candidate catalog.</summary>
        public Annotations CreateFreshCandidateAnnotations() => Catalog.ToFogModAnnotations();

        /// <summary>Only required items affect the Compact Fog graph; random-only and hard entries need no item-pool constraint.</summary>
        public FogCompactBackendProjection CreateBackendProjection()
        {
            FogTrackedItemProjection[] items = Catalog.KeyItems
                .Where(item => !item.Tags.HasFlag(FogKeyItemTags.RandomOnly) && !item.Tags.HasFlag(FogKeyItemTags.Hard))
                .Select(item => new FogTrackedItemProjection(
                item.GameItemId.Category,
                item.GameItemId.ItemId,
                true)).ToArray();
            int[] starts = Catalog.LotLocations.Select(lot => lot.FirstLot.Value).ToArray();
            return new FogCompactBackendProjection(Version, Fingerprint, Array.AsReadOnly(items), Array.AsReadOnly(starts));
        }
    }

    public sealed record FogTrackedItemProjection(int EquipType, int ItemId, bool Required);
    public sealed record FogCompactBackendProjection(
        string CatalogVersion,
        string CatalogFingerprint,
        IReadOnlyList<FogTrackedItemProjection> Items,
        IReadOnlyList<int> LotStarts);

    /// <summary>
    /// Rule-first compiler for the compact Fog projection. It generates repetitive runtime rows
    /// from pristine DSR facts and exposes policy gaps explicitly; callers must not treat a partial
    /// candidate as a production replacement for FogMod's current annotation input.
    /// </summary>
    public static class FogCompactCatalogBuilder
    {
        private sealed record LotSource(int Id, FogAreaId Area, string Kind, string Owner);

        private static readonly IReadOnlyDictionary<FogMapId, int> MapProgressionTier =
            new System.Collections.ObjectModel.ReadOnlyDictionary<FogMapId, int>(new Dictionary<FogMapId, int>
            {
                [new FogMapId("asylum")] = 0,
                [new FogMapId("firelink")] = 0,
                [new FogMapId("parish")] = 0,
                [new FogMapId("depths")] = 1,
                [new FogMapId("darkroot")] = 1,
                [new FogMapId("blighttown")] = 1,
                [new FogMapId("catacombs")] = 1,
                [new FogMapId("greathollow")] = 2,
                [new FogMapId("sens")] = 2,
                [new FogMapId("anorlondo")] = 3,
                [new FogMapId("paintedworld")] = 3,
                [new FogMapId("newlondo")] = 3,
                [new FogMapId("totg")] = 4,
                [new FogMapId("demonruins")] = 4,
                [new FogMapId("dukes")] = 4,
                [new FogMapId("dlc")] = 4,
                [new FogMapId("kiln")] = 5,
            });

        private static readonly IReadOnlyDictionary<string, FogItemName> KeyItemNameAliases =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, FogItemName>(new Dictionary<string, FogItemName>(StringComparer.Ordinal)
            {
                ["blighttown_key_goods_2007"] = new FogItemName("blighttown_key"),
                ["crest_of_artorias_goods_2002"] = new FogItemName("crest_of_artorias"),
                ["lordvessel_goods_2510"] = new FogItemName("lordvessel"),
            });

        public static FogCompactCatalogV1 Build(FogDsrFacts facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Compact Full Fog V1 requires MSB/EMEVD/GameParam facts from a clean DSR installation.");

            FogLogicalAreaResolver owner = new FogLogicalAreaResolver(facts);
            IReadOnlyList<FogKeyItemIdentity> identities = FogAnnotationPolicyResolver.ResolveKeyItemIdentities(
                facts, FogProjectAnnotationPolicy.KeyItemIdentityPolicies);
            FogKeyItemProjectPolicyV1[] keyItemPolicies = FogProjectAnnotationPolicy.KeyItemProjectPoliciesV1.ToArray();
            var keyItemPoliciesByName = keyItemPolicies.ToDictionary(policy => policy.Name);
            if (keyItemPolicies.Length != identities.Count
                || !keyItemPoliciesByName.Keys.ToHashSet().SetEquals(identities.Select(identity => identity.Name)))
                throw new InvalidDataException("Compact Full Fog V1 requires one explicit progression policy for every verified key-item identity.");
            var keyItems = new List<FogKeyItem>(identities.Count);
            foreach (FogKeyItemIdentity identity in identities)
            {
                FogKeyItemProjectPolicyV1 itemPolicy = keyItemPoliciesByName[identity.Name];
                if (string.IsNullOrWhiteSpace(itemPolicy.ControlledMechanic) || string.IsNullOrWhiteSpace(itemPolicy.Rationale))
                    throw new InvalidDataException($"Compact key-item policy '{identity.Name}' must name its controlled mechanic and project rationale.");
                FogItemName catalogName = KeyItemNameAliases.TryGetValue(identity.Name.Value, out FogItemName alias) ? alias : identity.Name;
                keyItems.Add(new FogKeyItem(catalogName, identity.GameItemId, itemPolicy.DefaultArea, itemPolicy.Tags));
            }

            IReadOnlyList<FogProjectAreaPolicyV1> areaPolicy = FogProjectAnnotationPolicy.ResolveAreaPolicyV1(facts, keyItems);
            IReadOnlyList<FogAreaLink> links = FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(
                facts, FogProjectAnnotationPolicy.AreaLinkPolicies, keyItems);
            if (links.Count != FogProjectAnnotationPolicy.AreaLinkPolicies.Count)
                throw new InvalidDataException("Compact Full Fog V1 resolved a different number of Area.To links than its authored project rules.");
            var linksBySource = new Dictionary<FogAreaId, List<FogAreaLink>>();
            for (int index = 0; index < links.Count; index++)
            {
                FogAreaId source = FogProjectAnnotationPolicy.AreaLinkPolicies[index].Source;
                if (!linksBySource.TryGetValue(source, out List<FogAreaLink> sourceLinks))
                {
                    sourceLinks = new List<FogAreaLink>();
                    linksBySource.Add(source, sourceLinks);
                }
                sourceLinks.Add(links[index]);
            }
            FogArea[] areas = areaPolicy.Select(policy => new FogArea(
                policy.Id,
                FogProjectAnnotationPolicy.ResolveAreaProjection(policy.Id),
                Array.AsReadOnly(new[] { policy.OwnerMap }),
                policy.Tags,
                null,
                linksBySource.TryGetValue(policy.Id, out List<FogAreaLink> sourceLinks)
                    ? Array.AsReadOnly(sourceLinks.ToArray())
                    : Array.Empty<FogAreaLink>())).ToArray();

            FogCustomStart[] starts = FogAnnotationPolicyResolver.ResolveCustomStarts(facts, FogProjectAnnotationPolicy.CustomStarts).ToArray();
            FogProjectEntranceResolution[] entranceResolutions = FogAnnotationPolicyResolver.ResolveEntrancePoliciesV1(facts).ToArray();
            FogWarp[] authoredWarps = FogAnnotationPolicyResolver.ResolveWarpPoliciesV1(facts)
                .Concat(FogAnnotationPolicyResolver.ResolveFlagTriggeredWarpPoliciesV1(facts)).ToArray();
            FogEntrance[] authoredEntrances = entranceResolutions
                .Select(resolution => resolution.Policy.ToFogEntrance(
                    resolution.ResolvedObjectPartName,
                    resolution.BossTriggerArea,
                    resolution.BossTriggerEventId))
                .ToArray();
            FogEntrance[] pvpEntrances = BuildPvpEntrances(facts);
            FogEntrance[] entrances = authoredEntrances.Concat(pvpEntrances).ToArray();
            FogWriterSupportEntrance[] writerSupportEntrances = ResolveWriterSupportEntrances(facts);
            FogDefaultFlagCollision[] flags = FogAnnotationPolicyResolver.ResolveBossDefeatFlagCollisions(
                facts, FogProjectAnnotationPolicy.DefaultFlagCollisionPolicies).ToArray();

            FogEnemyScalingResolution enemyResolution = BuildEnemyRules(facts, owner);
            (FogLotLocation[] lots, FogLotRowLocation[] lotRows, int unresolvedLots, int conflictingLots) = BuildLotLocations(facts, owner);
            FogDefaultCost[] costs = BuildDefaultCosts(areaPolicy);
            var catalog = new FogAnnotationCatalog(
                areas,
                keyItems,
                authoredWarps,
                entrances,
                starts,
                costs,
                enemyResolution.Rules,
                lots,
                flags,
                writerSupportEntrances,
                lotRows);

            IReadOnlyList<string> factProblems = facts.Validate(catalog);
            if (factProblems.Count > 0)
                throw new InvalidDataException("Compact Full Fog V1 has unresolved pristine DSR references: " + string.Join("; ", factProblems));

            int pendingAreaRoutes = areaPolicy.Count(area => !Enum.IsDefined(typeof(FogProjectAreaRouteDisposition), area.RouteDisposition));
            int unresolvedKeyIdentities = FogProjectAnnotationPolicy.KeyItemIdentityPolicies.Count(policy => !identities.Any(item => item.Name == policy.Name));
            int unresolvedKeyPolicies = FogProjectAnnotationPolicy.KeyItemIdentityPolicies.Count(policy => !keyItemPoliciesByName.ContainsKey(policy.Name));
            int unavailableRequiredItems = CountRequiredItemsWithoutSafeLots(keyItems, lots, facts);
            int randomOnlyKeyItems = keyItems.Count(item => item.Tags.HasFlag(FogKeyItemTags.RandomOnly));
            int hardOnlyKeyItems = keyItems.Count(item => item.Tags.HasFlag(FogKeyItemTags.Hard));
            int excludedLotSources = unresolvedLots + conflictingLots;
            FogCompactCategoryCoverage[] coverage =
            {
                new("Areas", pendingAreaRoutes == 0 ? FogCompactCategoryState.Complete : FogCompactCategoryState.Partial,
                    areas.Length, areaPolicy.Count(area => area.RouteDisposition == FogProjectAreaRouteDisposition.ExplicitNoStaticLink), pendingAreaRoutes,
                    $"All {areas.Length} stable project Areas have authored topology; {links.Count} fixed Area.To routes include verified cross-map ConnectCollision anchors and explicit within-map segmentation. Randomizable walls stay in Entrance records."),
                new("Entrances", FogCompactCategoryState.Complete,
                    entrances.Length, entranceResolutions.Count(policy => policy.Policy.RequiredItem.HasValue), 0,
                    $"The authored project pool includes {entranceResolutions.Count(policy => policy.Policy.Kind == FogProjectEntranceKind.TraversableFog)} traversable fogs, {entranceResolutions.Count(policy => policy.Policy.Kind == FogProjectEntranceKind.BossBoundary)} boss boundaries, {entranceResolutions.Count(policy => policy.Policy.Kind == FogProjectEntranceKind.LordvesselGate)} Lordvessel gates, {entranceResolutions.Count(policy => policy.Policy.Kind == FogProjectEntranceKind.FixedKeyDoor)} fixed key doors, and {pvpEntrances.Length} PvP gates (16 major / 4 minor); every active physical anchor resolves."),
                new("Warps", FogCompactCategoryState.Complete, authoredWarps.Length,
                    FogProjectAnnotationPolicy.WarpPoliciesV1.Count + FogProjectAnnotationPolicy.FlagWarpPoliciesV1.Count, 0,
                    $"The project pool contains {FogProjectAnnotationPolicy.WarpPoliciesV1.Count} pristine cutscene-backed directions in three reciprocal world connections and {FogProjectAnnotationPolicy.FlagWarpPoliciesV1.Count} generated event-flag route(s); every source contract and destination region resolves."),
                new("KeyItems", unresolvedKeyIdentities == 0 && unresolvedKeyPolicies == 0 && unavailableRequiredItems == 0 ? FogCompactCategoryState.Complete : FogCompactCategoryState.Partial,
                    keyItems.Count, randomOnlyKeyItems, unresolvedKeyIdentities + unresolvedKeyPolicies + unavailableRequiredItems,
                    $"All {keyItems.Count} EMEVD-anchored Goods identities have an explicit Area and controlled-mechanic policy ({keyItems.Count - randomOnlyKeyItems - hardOnlyKeyItems} required, {randomOnlyKeyItems} random-only, {hardOnlyKeyItems} hard-only); required identities are checked against safe Fog lots."),
                new("LotLocations", FogCompactCategoryState.Complete,
                    lots.Length, excludedLotSources, 0,
                    $"The builder projects only unambiguous owned lot families and exact source-backed rows. {excludedLotSources} unresolved/conflicting source references are deliberately excluded and cannot host required progression items."),
                new("LotRows", FogCompactCategoryState.Complete, lotRows.Length, conflictingLots, 0,
                    "The Compact writer scans only these exact source-backed ItemLotParam rows; it does not infer ownership from consecutive numeric IDs."),
                new("Enemies", enemyResolution.IsComplete ? FogCompactCategoryState.Complete : FogCompactCategoryState.Partial,
                    enemyResolution.Rules.Count, FogProjectAnnotationPolicy.BossAreaTagPolicies.Count, enemyResolution.UnresolvedCollisionGroups.Count, "Every enemy receives a map default; authored boss collision/entity anchors create compact overrides."),
                new("DefaultFlagCols", FogCompactCategoryState.Complete, flags.Length, flags.Length, 0,
                    "The project uses only the explicitly authored 22 boss-collision mappings; each one resolves to pristine MSB collision and boss-completion evidence."),
                new("WriterSupportEntrances", FogCompactCategoryState.Complete, writerSupportEntrances.Length, writerSupportEntrances.Length, 0,
                    "These pristine fixed/unused records satisfy pinned GameDataWriter lookups without creating graph edges or entering a randomization pool."),
                new("DefaultCost", FogCompactCategoryState.Complete, costs.Length, 0, 0,
                    "All logical Areas receive a deterministic project map-progression tier with boss and optional classifications retained by GraphConnector."),
            };
            return new FogCompactCatalogV1(catalog, coverage);
        }

        private static FogWriterSupportEntrance[] ResolveWriterSupportEntrances(FogDsrFacts facts)
        {
            const string objectName = "o5869_0000";
            const int expectedEntityId = 1511754;
            const string expectedCollision = "h0111B1_0000";
            FogMapId mapId = new FogMapId("anorlondo");
            FogDsrFacts.Map map = facts.Maps.TryGetValue(mapId, out FogDsrFacts.Map found)
                ? found
                : throw new KeyNotFoundException("Compact writer-support policy requires the pristine Anor Londo map.");
            FogDsrObjectFact[] matches = map.ObjectDetails.Where(part => string.Equals(part.Name, objectName, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || matches[0].EntityId != expectedEntityId
                || !string.Equals(matches[0].CollisionName, expectedCollision, StringComparison.Ordinal))
                throw new InvalidDataException($"Compact writer-support object '{objectName}' must resolve exactly once in pristine Anor Londo with entity {expectedEntityId} on collision {expectedCollision}.");

            return new[]
            {
                new FogWriterSupportEntrance(objectName, matches[0].EntityId, mapId,
                    "The pinned GameDataWriter checks this identity's IsFixed value to restore the vanilla Anor Londo broken-window object. Compact intentionally does not include this one-way PvP route in its 16-major/4-minor randomization pool; an unused fixed anchor satisfies the writer without graph or shuffle effects."),
            };
        }

        private static FogEntrance[] BuildPvpEntrances(FogDsrFacts facts)
        {
            FogMapId mapId = new FogMapId("dlc");
            FogDsrFacts.Map map = facts.Maps.TryGetValue(mapId, out FogDsrFacts.Map found)
                ? found
                : throw new KeyNotFoundException("Compact PvP Entrance policy requires the pristine DLC map.");
            if (!map.EventIds.Contains(11210700))
                throw new InvalidDataException("Compact PvP Entrance policy requires the pristine map event 11210700 that owns the authored arena-gate set.");

            var entrances = new List<FogEntrance>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            FogAreaId area = new FogAreaId("dlc_township");
            foreach (var gate in FogModifiedEventBuilder.CompactPvpGatePolicies())
            {
                FogDsrObjectFact[] doorObjects = map.ObjectDetails.Where(part => part.EntityId == gate.DoorObject).ToArray();
                FogDsrObjectFact[] switchObjects = map.ObjectDetails.Where(part => part.EntityId == gate.SwitchObject).ToArray();
                if (doorObjects.Length != 1 || switchObjects.Length != 1
                    || map.RegionDetails.Count(region => region.EntityId == gate.AreaEntity) != 1
                    || map.CollisionDetails.Count(collision => collision.EntityId == gate.HitCollision) != 1
                    || map.CollisionDetails.Count(collision => collision.EntityId == gate.SwitchCollision) != 1)
                    throw new InvalidDataException($"Compact PvP gate {gate.DoorObject} does not resolve its door/switch object, arena region, and paired collision anchors in pristine DSR.");
                if (gate.StateFlag <= 0 || gate.PresenceFlag <= 0)
                    throw new InvalidDataException($"Compact PvP gate {gate.DoorObject} has a nonpositive project event-flag anchor.");

                string name = doorObjects[0].Name;
                if (string.IsNullOrWhiteSpace(name) || !identities.Add(name))
                    throw new InvalidDataException($"Compact PvP gate {gate.DoorObject} has an empty or duplicate pristine Entrance identity.");
                FogEntranceSide Side() => new FogEntranceSide(
                    area, 0, 0, 0, 0, 0, null, 0, null, null, 0, 0, null, mapId, FogEntranceSideTags.None);
                FogEntranceTags tags = FogEntranceTags.Door | FogEntranceTags.Pvp
                    | (gate.Major ? FogEntranceTags.Major : FogEntranceTags.None);
                entrances.Add(new FogEntrance(name, gate.DoorObject, mapId,
                    gate.Major ? "Compact project major PvP gate" : "Compact project minor PvP gate",
                    null, tags, Side(), Side()));
            }
            return entrances.ToArray();
        }

        private static int CountRequiredItemsWithoutSafeLots(
            IEnumerable<FogKeyItem> items,
            IReadOnlyList<FogLotLocation> safeLots,
            FogDsrFacts facts)
        {
            int missing = 0;
            foreach (FogKeyItem item in items.Where(candidate =>
                !candidate.Tags.HasFlag(FogKeyItemTags.RandomOnly) && !candidate.Tags.HasFlag(FogKeyItemTags.Hard)))
            {
                bool located = safeLots.Any(start =>
                {
                    for (int row = start.FirstLot.Value; facts.ItemLotIds.Contains(row); row++)
                    {
                        if (facts.ItemLots.TryGetValue(row, out FogDsrItemLotFact lot)
                            && lot.Items.Any(entry => entry.Item == item.GameItemId)) return true;
                    }
                    return false;
                });
                if (!located) missing++;
            }
            return missing;
        }

        private static FogEnemyScalingResolution BuildEnemyRules(FogDsrFacts facts, FogLogicalAreaResolver owner)
        {
            var rules = new List<FogEnemyCollisionAreaPolicy>();
            foreach (FogMapId map in facts.Maps.Keys.OrderBy(value => value.Value, StringComparer.Ordinal))
            {
                FogAreaId defaultArea = owner.MapDefaults[map];
                rules.Add(new FogEnemyCollisionAreaPolicy(map, string.Empty, defaultArea, Array.Empty<string>()));
                foreach (IGrouping<string, FogDsrEnemyFact> group in facts.Maps[map].EnemyDetails
                    .Where(enemy => !string.IsNullOrWhiteSpace(enemy.CollisionName))
                    .GroupBy(enemy => enemy.CollisionName, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    var overrides = group.Select(enemy => (Enemy: enemy, Resolution: owner.Resolve(new FogLogicalAreaQuery(
                            FogLogicalSourceKind.Enemy, map, group.Key, enemy.EntityId))))
                        .Where(entry => entry.Resolution.Area.HasValue && entry.Resolution.Area.Value != defaultArea)
                        .GroupBy(entry => entry.Resolution.Area.Value)
                        .OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
                        .ToArray();
                    if (overrides.Length == 0) continue;
                    rules.Add(new FogEnemyCollisionAreaPolicy(map, group.Key, defaultArea, Array.Empty<string>()));
                    foreach (var overrideGroup in overrides)
                        rules.Add(new FogEnemyCollisionAreaPolicy(map, group.Key, overrideGroup.Key,
                            Array.AsReadOnly(overrideGroup.Select(entry => entry.Enemy.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray())));
                }
            }
            return FogAnnotationPolicyResolver.ResolveEnemyScalingRules(facts, rules);
        }

        private static (FogLotLocation[] Locations, FogLotRowLocation[] Rows, int Unresolved, int Conflicts) BuildLotLocations(
            FogDsrFacts facts,
            FogLogicalAreaResolver owner)
        {
            var candidates = new List<LotSource>();
            int unresolved = 0;
            foreach (FogDsrTreasureLotFact treasure in facts.FunctionalTreasureLots.OrderBy(value => value.Map.Value, StringComparer.Ordinal)
                .ThenBy(value => value.TreasureEntityId).ThenBy(value => value.ItemLotId))
            {
                if (!facts.ItemLotIds.Contains(treasure.ItemLotId)) continue;
                FogLogicalAreaResolution area = owner.Resolve(new FogLogicalAreaQuery(FogLogicalSourceKind.Treasure,
                    treasure.Map, treasure.PartCollisionName, treasure.TreasureEntityId, ItemLotId: treasure.ItemLotId));
                if (!area.IsResolved) { unresolved++; continue; }
                candidates.Add(new LotSource(treasure.ItemLotId, area.Area.Value, "treasure", $"{treasure.Map.Value}/{treasure.TreasureEntityId}"));
            }

            foreach (FogDsrNpcDropLotFact drop in facts.NpcDropLots.OrderBy(value => value.NpcParamId).ThenBy(value => value.Slot).ThenBy(value => value.ItemLotId))
            {
                if (!facts.ItemLotIds.Contains(drop.ItemLotId)) continue;
                var owners = facts.Maps.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
                    .SelectMany(entry => entry.Value.EnemyDetails.Where(enemy => enemy.NpcParamId == drop.NpcParamId)
                        .Select(enemy => owner.Resolve(new FogLogicalAreaQuery(FogLogicalSourceKind.Enemy,
                            entry.Key, enemy.CollisionName, enemy.EntityId))))
                    .Where(result => result.Area.HasValue).Select(result => result.Area.Value).Distinct().ToArray();
                if (owners.Length != 1) { unresolved++; continue; }
                candidates.Add(new LotSource(drop.ItemLotId, owners[0], "npc-drop", $"npc:{drop.NpcParamId}"));
            }

            foreach (FogDsrItemLotAwardFact award in facts.ItemLotAwards.OrderBy(value => value.EventArchiveName, StringComparer.Ordinal)
                .ThenBy(value => value.EventId).ThenBy(value => value.InstructionIndex))
            {
                if (!award.ItemLotId.HasValue || !facts.ItemLotIds.Contains(award.ItemLotId.Value)) { unresolved++; continue; }
                FogMapId sourceMap;
                if (award.CharacterEntityId.HasValue)
                {
                    var characterOwners = facts.Maps.SelectMany(entry => entry.Value.EnemyDetails
                        .Where(enemy => enemy.EntityId == award.CharacterEntityId.Value)
                        .Select(enemy => (Map: entry.Key, Enemy: enemy))).ToArray();
                    if (characterOwners.Length != 1) { unresolved++; continue; }
                    sourceMap = characterOwners[0].Map;
                    FogLogicalAreaResolution characterArea = owner.Resolve(new FogLogicalAreaQuery(FogLogicalSourceKind.EventReward,
                        sourceMap, characterOwners[0].Enemy.CollisionName, characterOwners[0].Enemy.EntityId,
                        EventId: award.EventId, ItemLotId: award.ItemLotId));
                    if (!characterArea.IsResolved) { unresolved++; continue; }
                    candidates.Add(new LotSource(award.ItemLotId.Value, characterArea.Area.Value,
                        "character-death-reward", $"{award.EventArchiveName}/{award.EventId}/entity:{award.CharacterEntityId.Value}"));
                    continue;
                }

                if (string.Equals(award.EventArchiveName, "common.emevd.dcx", StringComparison.OrdinalIgnoreCase)
                    && award.EventId == 970 && award.RequiredEventFlagId.HasValue)
                {
                    int completionFlag = award.RequiredEventFlagId.Value;
                    var bossOwners = facts.Maps.SelectMany(entry => entry.Value.EventFlagWrites
                        .Where(write => write.FlagId == completionFlag && facts.BossRewardCompletionFlags.Contains(completionFlag)
                            && entry.Value.BossDefeatHandlers.Any(handler => handler.EventId == write.EventId))
                        .Select(write => (Map: entry.Key, EventId: write.EventId)))
                        .Distinct().ToArray();
                    if (bossOwners.Length != 1) { unresolved++; continue; }

                    sourceMap = bossOwners[0].Map;
                    FogLogicalAreaResolution bossArea = owner.Resolve(new FogLogicalAreaQuery(FogLogicalSourceKind.BossEncounter,
                        sourceMap, EventId: bossOwners[0].EventId, ItemLotId: award.ItemLotId));
                    if (!bossArea.IsResolved) { unresolved++; continue; }
                    candidates.Add(new LotSource(award.ItemLotId.Value, bossArea.Area.Value,
                        "boss-reward", $"{award.EventArchiveName}/{award.EventId}/flag:{completionFlag}/boss:{bossOwners[0].EventId}"));
                    continue;
                }

                FogMapId[] maps = facts.Maps.Where(entry => string.Equals(entry.Value.EventArchiveName, award.EventArchiveName, StringComparison.Ordinal))
                    .Select(entry => entry.Key).ToArray();
                if (maps.Length == 0)
                    maps = facts.Maps.Where(entry => entry.Value.BossDefeatHandlers.Any(handler => handler.EventId == award.EventId))
                        .Select(entry => entry.Key).ToArray();
                if (maps.Length != 1) { unresolved++; continue; }
                sourceMap = maps[0];
                FogLogicalAreaResolution area = owner.Resolve(new FogLogicalAreaQuery(FogLogicalSourceKind.EventReward,
                    sourceMap, EventId: award.EventId, ItemLotId: award.ItemLotId));
                if (!area.IsResolved) { unresolved++; continue; }
                bool boss = facts.Maps[sourceMap].BossDefeatHandlers.Any(handler => handler.EventId == award.EventId);
                candidates.Add(new LotSource(award.ItemLotId.Value, area.Area.Value,
                    boss ? "boss-reward" : "event-reward", $"{award.EventArchiveName}/{award.EventId}"));
            }

            var conflicts = candidates.GroupBy(source => source.Id)
                .Where(group => group.Select(source => source.Area).Distinct().Count() > 1)
                .Select(group => group.Key)
                .ToHashSet();
            unresolved += conflicts.Count;
            LotSource[] safeCandidates = candidates.Where(source => !conflicts.Contains(source.Id)).ToArray();
            FogLotRowLocation[] rows = safeCandidates
                .GroupBy(source => source.Id)
                .Select(group => new FogLotRowLocation(new FogItemLotId(group.Key), group.First().Area))
                .OrderBy(row => row.Lot.Value)
                .ToArray();
            LotSource[] familyStarts = GroupContiguousFamilies(safeCandidates);
            FogLotLocation[] locations = familyStarts
                .GroupBy(source => source.Id).Select(group => group.First())
                .OrderBy(source => source.Id)
                .Select(source => new FogLotLocation(new FogItemLotId(source.Id), source.Area))
                .ToArray();
            return (locations, rows, unresolved, conflicts.Count);
        }

        private static LotSource[] GroupContiguousFamilies(IEnumerable<LotSource> candidates)
        {
            var starts = new List<LotSource>();
            foreach (IGrouping<(string Kind, string Owner, FogAreaId Area), LotSource> ownerGroup in candidates
                .GroupBy(source => (source.Kind, source.Owner, source.Area))
                .OrderBy(group => group.Key.Kind, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Owner, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Area.Value, StringComparer.Ordinal))
            {
                LotSource[] rows = ownerGroup.GroupBy(source => source.Id).Select(group => group.First())
                    .OrderBy(source => source.Id).ToArray();
                int previous = int.MinValue;
                foreach (LotSource source in rows)
                {
                    if (previous == int.MinValue || source.Id != previous + 1) starts.Add(source);
                    previous = source.Id;
                }
            }
            return starts.OrderBy(source => source.Id).ThenBy(source => source.Kind, StringComparer.Ordinal)
                .ThenBy(source => source.Owner, StringComparer.Ordinal).ToArray();
        }

        private static FogDefaultCost[] BuildDefaultCosts(IReadOnlyList<FogProjectAreaPolicyV1> areas)
        {
            var costs = new List<FogDefaultCost>(areas.Count);
            foreach (FogProjectAreaPolicyV1 area in areas)
            {
                if (!MapProgressionTier.TryGetValue(area.OwnerMap, out int tier))
                    throw new InvalidDataException($"Map '{area.OwnerMap}' has no Compact Full Fog V1 progression tier.");
                costs.Add(new FogDefaultCost(area.Id, tier));
            }
            return costs.ToArray();
        }
    }

    public static class FogCompactCatalogProvider
    {
        /// <summary>Builds a candidate from pristine facts; production use remains gated on complete category coverage.</summary>
        public static FogCompactCatalogV1 BuildCandidate(FogDsrFacts facts) => FogCompactCatalogBuilder.Build(facts);

        public static Annotations CreateFreshAnnotations(FogDsrFacts facts)
        {
            FogCompactCatalogV1 candidate = BuildCandidate(facts);
            return CreateFreshAnnotations(candidate);
        }

        public static Annotations CreateFreshAnnotations(FogCompactCatalogV1 candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (!candidate.IsComplete)
            {
                string gaps = string.Join("; ", candidate.Coverage.Where(item => item.State != FogCompactCategoryState.Complete)
                    .Select(item => $"{item.Category}={item.State}"));
                throw new InvalidOperationException($"Compact Full Fog V1 is not production-complete: {gaps}");
            }
            return candidate.CreateFreshCandidateAnnotations();
        }
    }
}
