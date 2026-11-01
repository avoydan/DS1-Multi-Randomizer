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

    /// <summary>Frozen v2 projection with a Ceaseless arrival inside its verified boss-start trigger.</summary>
    public sealed class FogCompactCatalogV2
    {
        public const string Version = "ds1tr-compact-full-fog-v2";
        public FogAnnotationCatalog Catalog { get; }
        public IReadOnlyList<FogCompactCategoryCoverage> Coverage { get; }
        public IReadOnlyList<FogBossGatedRoute> BossGatedRoutes { get; }
        public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" + Catalog.Fingerprint))).ToLowerInvariant();
        public bool IsComplete => Coverage.All(category => category.State == FogCompactCategoryState.Complete);

        internal FogCompactCatalogV2(FogAnnotationCatalog catalog, IEnumerable<FogCompactCategoryCoverage> coverage,
            IEnumerable<FogBossGatedRoute> bossGatedRoutes)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Coverage = Array.AsReadOnly((coverage ?? throw new ArgumentNullException(nameof(coverage))).ToArray());
            BossGatedRoutes = Array.AsReadOnly((bossGatedRoutes ?? throw new ArgumentNullException(nameof(bossGatedRoutes))).ToArray());
        }

        public Annotations CreateFreshCandidateAnnotations() => Catalog.ToFogModAnnotations();

        public FogCompactBackendProjection CreateBackendProjection() => new FogCompactBackendProjection(
            Version, Fingerprint,
            Array.AsReadOnly(Catalog.KeyItems
                .Where(item => !item.Tags.HasFlag(FogKeyItemTags.RandomOnly) && !item.Tags.HasFlag(FogKeyItemTags.Hard))
                .Select(item => new FogTrackedItemProjection(item.GameItemId.Category, item.GameItemId.ItemId, true)).ToArray()),
            Array.AsReadOnly(Catalog.LotLocations.Select(lot => lot.FirstLot.Value).ToArray()));
    }

    /// <summary>
    /// Frozen v3 catalog (generation profile ds1tr-gen-v6): v2 plus a physically correct Undead Asylum start. The starting hallway is separated
    /// from Oscar's cell by a randomizable wall, so it has no unconditional exit of its own: leaving the Asylum interior needs the balcony and
    /// Oscar's cell first (upstream FogMod's own conditions), and the crow departure is already the fixed or randomized warp of asylum_loop.
    /// v1 and v2 gave the hallway two unconditional fixed exits, which let the solver accept layouts whose only hallway exit pairs with a
    /// dead-end pocket. Ceaseless needs no separate post-boss route proof in v3: its lower route is an ordinary boss-completion edge.
    /// </summary>
    public sealed class FogCompactCatalogV3
    {
        public const string Version = "ds1tr-compact-full-fog-v3";
        public FogAnnotationCatalog Catalog { get; }
        public IReadOnlyList<FogCompactCategoryCoverage> Coverage { get; }
        public IReadOnlyList<FogBossGatedRoute> BossGatedRoutes { get; }
        public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" + Catalog.Fingerprint))).ToLowerInvariant();
        public bool IsComplete => Coverage.All(category => category.State == FogCompactCategoryState.Complete);

        internal FogCompactCatalogV3(FogAnnotationCatalog catalog, IEnumerable<FogCompactCategoryCoverage> coverage,
            IEnumerable<FogBossGatedRoute> bossGatedRoutes)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Coverage = Array.AsReadOnly((coverage ?? throw new ArgumentNullException(nameof(coverage))).ToArray());
            BossGatedRoutes = Array.AsReadOnly((bossGatedRoutes ?? throw new ArgumentNullException(nameof(bossGatedRoutes))).ToArray());
        }

        public Annotations CreateFreshCandidateAnnotations() => Catalog.ToFogModAnnotations();

        public FogCompactBackendProjection CreateBackendProjection() => new FogCompactBackendProjection(
            Version, Fingerprint,
            Array.AsReadOnly(Catalog.KeyItems
                .Where(item => !item.Tags.HasFlag(FogKeyItemTags.RandomOnly) && !item.Tags.HasFlag(FogKeyItemTags.Hard))
                .Select(item => new FogTrackedItemProjection(item.GameItemId.Category, item.GameItemId.ItemId, true)).ToArray()),
            Array.AsReadOnly(Catalog.LotLocations.Select(lot => lot.FirstLot.Value).ToArray()));
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

        public static FogCompactCatalogV2 BuildV2(FogDsrFacts facts)
        {
            FogCompactCatalogV1 v1 = Build(facts);
            // Remove V1's unconditioned Ceaseless/lava shortcuts. V2 adds a boss-gated route from
            // the main Demon Ruins side; the arena's randomized fog boundary is its only exit.
            FogArea[] areas = v1.Catalog.Areas.Select(area =>
            {
                if (area.Id.Value != "demonruins_ceaseless" && area.Id.Value != "demonruins_lava") return area;
                FogAreaLink[] links = (area.To ?? Array.Empty<FogAreaLink>())
                    .Where(link => !(area.Id.Value == "demonruins_ceaseless" && link.Destination.Value == "demonruins_lava")
                        && !(area.Id.Value == "demonruins_lava" && link.Destination.Value == "demonruins_ceaseless"))
                    .ToArray();
                return area with { To = Array.AsReadOnly(links) };
            }).ToArray();

            FogBossGatedRoute[] bossGatedRoutes = ResolveV2BossGatedRoutes(facts, v1.Catalog).ToArray();
            foreach (FogBossGatedRoute route in bossGatedRoutes)
            {
                int sourceIndex = Array.FindIndex(areas, area => area.Id == route.Source);
                if (sourceIndex < 0) throw new InvalidDataException($"Boss-gated route source '{route.Source}' is absent from Compact Fog v2.");
                FogCondition condition = new FogCondition(FogConditionKind.Atom,
                    Array.AsReadOnly(new[] { FogConditionReference.BossCompletion(route.CompletionEventId) }));
                FogArea sourceArea = areas[sourceIndex];
                areas[sourceIndex] = sourceArea with
                {
                    To = Array.AsReadOnly(sourceArea.To.Concat(new[] { new FogAreaLink(route.Destination, condition, FogAreaLinkTags.None) }).ToArray()),
                };
            }
            FogAreaLink mainToLava = areas.Single(area => area.Id.Value == "demonruins")
                .To.SingleOrDefault(link => link.Destination.Value == "demonruins_lava");
            bool ceaselessToLava = areas.Single(area => area.Id.Value == "demonruins_ceaseless")
                .To.Any(link => link.Destination.Value == "demonruins_lava");
            bool lavaToCeaseless = areas.Single(area => area.Id.Value == "demonruins_lava")
                .To.Any(link => link.Destination.Value == "demonruins_ceaseless");
            if (ceaselessToLava || lavaToCeaseless
                || mainToLava?.Condition is not { Kind: FogConditionKind.Atom, Terms.Count: 1 }
                || mainToLava.Condition.Terms[0] != FogConditionReference.BossCompletion(11410410))
                throw new InvalidDataException("Compact Fog v2 must gate the lower Demon Ruins route from the main Demon Ruins side and remove both Ceaseless/lava shortcuts.");

            FogProjectEntranceResolution ceaselessResolution = FogAnnotationPolicyResolver.ResolveEntrancePoliciesV1(facts)
                .Single(resolution => resolution.Policy.Id == "demonruins-ceaseless-boundary");
            if (ceaselessResolution.Policy.InteractionEventId != 11415371
                || ceaselessResolution.Policy.BossTriggerRegionEntityId != 1412698
                || !facts.Maps[ceaselessResolution.Policy.Map].EventActionButtonFacts.Any(check =>
                    check.EventId == ceaselessResolution.Policy.InteractionEventId.Value
                    && check.TargetEntityId == ceaselessResolution.Policy.BossTriggerRegionEntityId.Value))
                throw new InvalidDataException("Compact Fog v2 requires pristine Ceaseless encounter-start event 11415371 to check literal region 1412698.");

            FogEntrance[] projectEntrances = v1.Catalog.Entrances.Select(entrance =>
            {
                FogEntranceSide ProjectSide(FogEntranceSide side, bool isASide)
                {
                    if (side == null) return null;
                    if (entrance.Name == "o2915_0000" && side.Area.Value == "dlc_artorias")
                    {
                        // Keep the vanilla encounter-start area check, use the source-side completion
                        // flag for the post-fight exit, and retain FogMod's object-relative landing.
                        return side with
                        {
                            Flag = 11210001,
                            BossTrigger = 0,
                            BossTriggerArea = null,
                            CustomWarp = null,
                            BossEncounterStartEventId = 11215011,
                        };
                    }
                    if (entrance.Name == ceaselessResolution.ResolvedObjectPartName
                        && side.Area == ceaselessResolution.Policy.DestinationArea)
                    {
                        // Keep the trigger volume in host-only completion metadata. The original
                        // event already checks region 1412698; a copied region and a center CustomWarp
                        // are neither needed nor equivalent to FogMod's object-relative landing.
                        return side with
                        {
                            BossTrigger = 0,
                            BossTriggerArea = null,
                            CustomWarp = null,
                            BossEncounterStartEventId = checked((int)ceaselessResolution.Policy.InteractionEventId.Value),
                        };
                    }
                    if (IsV2HigherSide(entrance.Name, isASide))
                        return side with { Tags = side.Tags | FogEntranceSideTags.Higher };
                    // V2 may carry boss-trigger metadata for event behavior, but the trigger volume
                    // is not a player landing. Keep FogMod's default object-relative placement
                    // unless the side itself has an explicit, independently reviewed CustomWarp.
                    return side;
                }

                return entrance with { ASide = ProjectSide(entrance.ASide, true), BSide = ProjectSide(entrance.BSide, false) };
            }).Concat(new[]
            {
                ResolveV2GapingDragonBoundary(facts),
                ResolveV2PostArtoriasExit(facts),
            }).ToArray();

            FogEntrance[] pvpEntrances = FogCompactV2PvpPolicy.BuildGraphEntrances(facts, v1.Catalog).ToArray();
            var existingEntranceNames = projectEntrances.Select(entrance => entrance.Name).ToHashSet(StringComparer.Ordinal);
            string[] duplicatePvpEntrances = pvpEntrances.Where(entrance => existingEntranceNames.Contains(entrance.Name))
                .Select(entrance => entrance.Name).ToArray();
            if (duplicatePvpEntrances.Length > 0)
                throw new InvalidDataException("Compact Fog v2 PvP entries duplicate existing project Entrances: " + string.Join(", ", duplicatePvpEntrances));

            FogKeyItem orangeCharredRing = ResolveV2OrangeCharredRing(facts, v1.Catalog);
            FogKeyItem[] v2KeyItems = v1.Catalog.KeyItems.Concat(new[] { orangeCharredRing }).ToArray();
            FogEntrance[] entrances = projectEntrances.Concat(pvpEntrances).ToArray();
            FogWriterSupportEntrance[] writerSupportEntrances = v1.Catalog.WriterSupportEntrances
                .Where(entrance => !string.Equals(entrance.Name, "o5869_0000", StringComparison.Ordinal))
                .ToArray();

            FogEntranceSide ceaselessSide = entrances.Single(entrance => entrance.Name == "o4961_0000").BSide;
            if (ceaselessSide.Area.Value != "demonruins_ceaseless" || ceaselessSide.BossTrigger != 0
                || ceaselessSide.BossTriggerArea != null || ceaselessSide.CustomWarp != null
                || ceaselessSide.BossEncounterStartEventId != 11415371)
                throw new InvalidDataException("Compact Fog v2 must retain FogMod's default object-relative landing on the Ceaseless side.");

            HashSet<int> fixedPostBossRoutes = bossGatedRoutes.Select(route => route.CompletionEventId).ToHashSet();
            HashSet<int> requiredCompletions = fixedPostBossRoutes.Concat(new[] { 11000001, 11210001 }).ToHashSet();
            FogBossCompletionTransition[] bossCompletions = ResolveV2BossCompletionTransitions(
                facts, entrances, requiredCompletions, fixedPostBossRoutes).ToArray();
            if (!bossCompletions.Any(transition => transition.EventId == 11410410
                && transition.EncounterArea.Value == "demonruins_ceaseless"
                && transition.TriggerEntrance.Value == "o4961_0000"
                && transition.TriggerEventId == 11415371
                && transition.TriggerRegionEntityId == 1412698))
                throw new InvalidDataException("Compact Fog v2 requires Ceaseless completion 11410410 to be reachable only after the exact default-landing boundary whose start event 11415371 checks region 1412698.");

            FogAnnotationCatalog source = v1.Catalog;
            var catalog = new FogAnnotationCatalog(areas, v2KeyItems, source.Warps, entrances,
                source.CustomStarts, source.DefaultCosts, source.Enemies, source.LotLocations,
                source.DefaultFlagCols, writerSupportEntrances, source.LotRows, bossCompletions);
            return new FogCompactCatalogV2(catalog, v1.Coverage, bossGatedRoutes);
        }

        /// <summary>
        /// The v3 catalog: <see cref="BuildV2"/> with four groups of physically unsound fixed routes corrected: the Asylum starting hallway's two unconditional exits,
        /// the Lost Izalith lava crossing (Orange Charred Ring), the Sen's Fortress cage (Cage Key, no bypass to the roof) and the twelve links from a post-encounter
        /// neighbour back into a boss arena (the arena must have been reached first). See
        /// <see cref="FogCompactCatalogV3"/> and docs/COMPACT_FULL_FOG_V3.md; v2 itself is untouched and keeps its frozen fingerprint.
        /// </summary>
        public static FogCompactCatalogV3 BuildV3(FogDsrFacts facts)
        {
            FogCompactCatalogV2 v2 = BuildV2(facts);
            FogAnnotationCatalog source = v2.Catalog;
            FogCondition leaveAsylum = new FogCondition(FogConditionKind.All, Array.AsReadOnly(new[]
            {
                FogConditionReference.Area(new FogAreaId("asylum_balcony")),
                FogConditionReference.Area(new FogAreaId("asylum_oscar")),
            }));
            FogArea[] areas = source.Areas.Select(area =>
            {
                if (area.Id.Value != "asylum") return area;
                // The tutorial departure to Firelink is the crow warp of asylum_loop (fixed edge, or the randomized warp); the hallway-owned
                // fixed route to Firelink duplicated it and was available at the very start. The hallway's exit to the Asylum exterior needs
                // the interior first.
                FogAreaLink[] links = (area.To ?? Array.Empty<FogAreaLink>())
                    .Where(link => link.Destination.Value != "firelink")
                    .Select(link => link.Destination.Value == "asylum_outside" && link.Condition == null ? link with { Condition = leaveAsylum } : link)
                    .ToArray();
                return area with { To = Array.AsReadOnly(links) };
            }).ToArray();

            FogArea hallway = areas.Single(area => area.Id.Value == "asylum");
            if (hallway.To.Any(link => link.Condition == null) || hallway.To.Any(link => link.Destination.Value == "firelink")
                || !hallway.To.Any(link => link.Destination.Value == "asylum_outside"))
                throw new InvalidDataException("Compact Fog v3 must not give the Asylum starting hallway an unconditional exit and must keep its conditioned exterior route.");

            // ---- Lost Izalith lava crossing: the Orange Charred Ring ----------------------------------------------------------------------------
            // Both entries into the lava segment need the ring (upstream FogMod models exactly this; leaving the segment is free). v1 and v2 chained the
            // segment into the Demon Ruins list with unconditional routes, although the v2 catalog already declares the ring as "the lava-gate condition".
            // Where the ring comes from: item lot 2670 is awarded by m14_01 event 11415386 once the whole Centipede Demon encounter (c5200 and its ten
            // spawn, all on collision h0062B1, the demonruins_centipede Area) is dead; lot 4030 is a dormant relief treasure (no part, start disabled) that
            // is not an acquisition. The ring is therefore found in demonruins_centipede, which is also fixed-adjacent to demonruins_izalithprelava.
            const int ringLot = 2670;
            const long ringAwardEvent = 11415386;
            if (!facts.ItemLotAwards.Any(award => award.ItemLotId == ringLot && award.EventId == ringAwardEvent
                && string.Equals(award.EventArchiveName, "m14_01_00_00.emevd.dcx", StringComparison.Ordinal)))
                throw new InvalidDataException("Compact Fog v3 requires the pristine Demon Ruins event 11415386 to award the Orange Charred Ring lot 2670.");
            FogCondition ringGate = new FogCondition(FogConditionKind.Atom,
                Array.AsReadOnly(new[] { FogConditionReference.KeyItem(new FogItemName("orange_charred_ring")) }));
            FogAreaLink GateLava(FogArea area, FogAreaLink link) =>
                link.Destination.Value == "demonruins_izalithlava" && link.Condition == null ? link with { Condition = ringGate } : link;
            areas = areas.Select(area => area.Id.Value == "demonruins_izalith" || area.Id.Value == "demonruins_izalithprelava"
                ? area with { To = Array.AsReadOnly(area.To.Select(link => GateLava(area, link)).ToArray()) } : area).ToArray();

            // ---- Sen's Fortress cage: the Cage Key, and the cage is a closed room ---------------------------------------------------------------
            // Upstream FogMod: sens_mid -> sens_cage is a key door (Cage Key), sens_cage has no way out except back, and the key lies in sens_upper
            // (item lot 1500150 is a treasure on collision h0032B0_0000, an upper-Sen's collision). v1 and v2 chained mid, cage and upper by list order
            // (AddLocalSequence only skipped a gate pair for CONSECUTIVE names, so the cage let mid and upper bypass their randomizable wall o5153_0000)
            // and left the door unlocked. v3 locks the door, keeps the way back out, and removes the two links between the cage and the roof.
            const int cageKeyLot = 1500150;
            if (!facts.TreasureLots.Any(treasure => treasure.ItemLotId == cageKeyLot && treasure.PartCollisionName == "h0032B0_0000"))
                throw new InvalidDataException("Compact Fog v3 requires the pristine Sen's Cage Key lot 1500150 to be a treasure on collision h0032B0_0000.");
            FogCondition cageGate = new FogCondition(FogConditionKind.Atom,
                Array.AsReadOnly(new[] { FogConditionReference.KeyItem(new FogItemName("cage_key_goods_2003")) }));
            areas = areas.Select(area =>
            {
                if (area.Id.Value == "sens_mid")
                    return area with { To = Array.AsReadOnly(area.To.Select(link =>
                        link.Destination.Value == "sens_cage" && link.Condition == null ? link with { Condition = cageGate } : link).ToArray()) };
                if (area.Id.Value == "sens_cage")
                    return area with { To = Array.AsReadOnly(area.To.Where(link => link.Destination.Value != "sens_upper").ToArray()) };
                if (area.Id.Value == "sens_upper")
                    return area with { To = Array.AsReadOnly(area.To.Where(link => link.Destination.Value != "sens_cage").ToArray()) };
                return area;
            }).ToArray();

            FogArea mid = areas.Single(area => area.Id.Value == "sens_mid");
            FogArea cage = areas.Single(area => area.Id.Value == "sens_cage");
            FogArea roof = areas.Single(area => area.Id.Value == "sens_upper");
            FogArea izalith = areas.Single(area => area.Id.Value == "demonruins_izalith");
            FogArea prelava = areas.Single(area => area.Id.Value == "demonruins_izalithprelava");
            FogArea lava = areas.Single(area => area.Id.Value == "demonruins_izalithlava");
            if (!mid.To.Any(link => link.Destination.Value == "sens_cage" && link.Condition == cageGate)
                || mid.To.Any(link => link.Destination.Value == "sens_cage" && link.Condition == null)
                || cage.To.Any(link => link.Destination.Value == "sens_upper") || roof.To.Any(link => link.Destination.Value == "sens_cage")
                || !cage.To.Any(link => link.Destination.Value == "sens_mid"))
                throw new InvalidDataException("Compact Fog v3 must lock the Sen's cage door, keep the way back to sens_mid and remove every cage link to sens_upper.");
            if (izalith.To.Any(link => link.Destination.Value == "demonruins_izalithlava" && link.Condition != ringGate)
                || prelava.To.Any(link => link.Destination.Value == "demonruins_izalithlava" && link.Condition != ringGate)
                || !izalith.To.Any(link => link.Destination.Value == "demonruins_izalithlava")
                || !prelava.To.Any(link => link.Destination.Value == "demonruins_izalithlava")
                || !lava.To.Any(link => link.Destination.Value == "demonruins_izalith") || !lava.To.Any(link => link.Destination.Value == "demonruins_izalithprelava"))
                throw new InvalidDataException("Compact Fog v3 must gate both lava entries with the Orange Charred Ring and keep both exits from the lava segment free.");

            // ---- boss arenas: no free way back into a living boss's arena ----------------------------------------------------------------------
            // AddLocalSequence never links INTO a boss Area ("an ordinary Area.To edge cannot bypass its entrance") but AddLocalRoute is two-way, so every
            // boss Area that owns a post-encounter route also got the reverse link from that neighbour back into the arena, unconditionally. When the
            // neighbour is reachable some other way (Quelaag's bell room from Demon Ruins, confirmed in the owner's world), the checker walked into the
            // arena and left through its randomized boss-side fog, which the game does not open while the boss lives. v3 asks for the arena to have been
            // reached already: the only other way in is the boss's own fog, so the reverse link only works after an arrival through the gate. The link
            // out of the arena stays free, and forward-only encounter routes (Gwyndolin, the hard Gaping Dragon drop) have no reverse link and are untouched.
            FogArea[] beforeBossEntries = areas;
            HashSet<string> bossAreaIds = FogProjectAnnotationPolicy.BossAreaTagPolicies.Select(policy => policy.Area.Value).ToHashSet(StringComparer.Ordinal);
            areas = beforeBossEntries.Select(area => area with
            {
                To = Array.AsReadOnly((area.To ?? Array.Empty<FogAreaLink>()).Select(link =>
                {
                    if (!bossAreaIds.Contains(link.Destination.Value) || link.Condition != null || link.Tags.HasFlag(FogAreaLinkTags.Hard)) return link;
                    FogArea boss = beforeBossEntries.Single(candidate => candidate.Id == link.Destination);
                    if (!(boss.To ?? Array.Empty<FogAreaLink>()).Any(back => back.Destination == area.Id)) return link;
                    return link with { Condition = new FogCondition(FogConditionKind.Atom, Array.AsReadOnly(new[] { FogConditionReference.Area(link.Destination) })) };
                }).ToArray()),
            }).ToArray();
            FogAreaLink quelaagBack = areas.Single(area => area.Id.Value == "blighttown_bell").To.SingleOrDefault(link => link.Destination.Value == "blighttown_quelaag");
            if (quelaagBack?.Condition is not { Kind: FogConditionKind.Atom, Terms.Count: 1 } || quelaagBack.Condition.Terms[0] != FogConditionReference.Area(new FogAreaId("blighttown_quelaag"))
                || !areas.Single(area => area.Id.Value == "blighttown_quelaag").To.Any(link => link.Destination.Value == "blighttown_bell" && link.Condition == null))
                throw new InvalidDataException("Compact Fog v3 must require Quelaag's arena to be reached before the bell room can walk back into it, and keep the way out free.");

            // ---- the Gaping Dragon arena is entered through its own fog -------------------------------------------------------------------------
            // Upstream FogMod puts both sides of the Depths wall o1420_0000 ("near the area with the Channeler") in `depths`; the project's v1 segmentation
            // labelled its far side `depths_gaping`, the Area of the arena. The wall lies on collision h0035B0, about 25 m from the arena collision h0074B0
            // that holds the boss fog o1400_0000 and the Gaping Dragon, so arriving through it does not put the player in the arena: it lets the graph
            // reach the arena's boss-side fog exit (flag 2, set when Gaping dies) and the Depths key inside it without the encounter. In 131 of 300 sampled
            // layouts progression depended on that. v3 follows upstream: the wall joins `depths` to `depths`, and `depths_gaping` is entered through o1400_0000
            // (or the hard-logic drop, which is off by default).
            const string gapingWall = "o1420_0000";
            FogEntrance wall = source.Entrances.Single(entrance => entrance.Name == gapingWall);
            if (wall.ASide?.Area.Value != "depths" || wall.BSide?.Area.Value != "depths_gaping")
                throw new InvalidDataException("Compact Fog v3 expects the v2 Depths wall o1420_0000 to join depths and depths_gaping before it is corrected.");
            FogEntrance[] correctedEntrances = source.Entrances.Select(entrance => entrance.Name == gapingWall
                ? entrance with { BSide = entrance.BSide with { Area = new FogAreaId("depths") } } : entrance).ToArray();

            // ---- where the two keys are found (graph location of an item = the Area of its source lot) --------------------------------------------
            var keyLotAreas = new Dictionary<int, FogAreaId>
            {
                [ringLot] = new FogAreaId("demonruins_centipede"),
                [cageKeyLot] = new FogAreaId("sens_upper"),
            };
            FogLotRowLocation[] lotRows = source.LotRows.Select(row => keyLotAreas.TryGetValue(row.Lot.Value, out FogAreaId area) ? row with { Area = area } : row).ToArray();
            FogLotLocation[] lotLocations = source.LotLocations.Select(lot => keyLotAreas.TryGetValue(lot.FirstLot.Value, out FogAreaId area) ? lot with { Area = area } : lot).ToArray();
            if (keyLotAreas.Keys.Any(lot => lotRows.Count(row => row.Lot.Value == lot) != 1))
                throw new InvalidDataException("Compact Fog v3 requires the Orange Charred Ring and Cage Key source lots in the project lot index.");
            // The default Area of a RandomOnly key is only used when no lot holds it; keep it where the key is found.
            FogKeyItem[] keyItems = source.KeyItems.Select(item =>
                item.Name.Value == "orange_charred_ring" ? item with { DefaultArea = keyLotAreas[ringLot] }
                : item.Name.Value == "cage_key_goods_2003" ? item with { DefaultArea = keyLotAreas[cageKeyLot] } : item).ToArray();

            FogBossCompletionTransition[] bossCompletions = source.BossCompletions
                .Select(transition => transition with { RequireFixedPostBossRoute = false }).ToArray();
            var catalog = new FogAnnotationCatalog(areas, keyItems, source.Warps, correctedEntrances,
                source.CustomStarts, source.DefaultCosts, source.Enemies, lotLocations,
                source.DefaultFlagCols, source.WriterSupportEntrances, lotRows, bossCompletions);
            return new FogCompactCatalogV3(catalog, v2.Coverage, v2.BossGatedRoutes);
        }

        private static bool IsV2HigherSide(string entranceName, bool isASide) =>
            entranceName == "o2915_0000" || entranceName == "o2916_0000" || (entranceName == "o4972_0000" && !isASide);

        private static FogEntrance ResolveV2GapingDragonBoundary(FogDsrFacts facts)
        {
            const string objectName = "o1400_0000";
            const int entityId = 1001990;
            const int completionEventId = 11000001;
            const int completionFlag = 2;
            FogMapId mapId = new FogMapId("depths");
            FogDsrFacts.Map map = facts.Maps.TryGetValue(mapId, out FogDsrFacts.Map found)
                ? found
                : throw new KeyNotFoundException("Compact V2 Gaping boundary requires the pristine Depths map.");
            FogDsrObjectFact[] objects = map.ObjectDetails
                .Where(part => string.Equals(part.Name, objectName, StringComparison.Ordinal)).ToArray();
            if (objects.Length != 1 || objects[0].EntityId != entityId
                || !facts.BossRewardCompletionFlags.Contains(completionFlag)
                || !map.EventFlagWrites.Contains(new FogDsrEventFlagWrite(completionEventId, completionFlag))
                || !map.EventActionButtonFacts.Any(check => check.EventId == 11005391
                    && check.InstructionId == 19 && check.TargetEntityId == 1002998
                    && check.LineSegmentEntityId == entityId)
                || map.RegionDetails.Count(region => region.EntityId == 1002998) != 1)
                throw new InvalidDataException("Compact V2 Gaping boundary must resolve pristine o1400_0000/entity 1001990, completion flag 2 written by event 11000001, and event 11005391's 3[19] boss-room action bound to target 1002998 and line-segment entity 1001990.");

            FogEntranceSide Side(FogAreaId area, int flag) => new FogEntranceSide(
                area, flag, 0, 0, 0, 0, null, 0, null, null, 0, 0, null, null, FogEntranceSideTags.None);
            return new FogEntrance(objectName, entityId, mapId, "To Gaping Dragon", null,
                FogEntranceTags.Boss,
                Side(new FogAreaId("depths"), 0),
                Side(new FogAreaId("depths_gaping"), completionFlag) with
                {
                    BossEncounterStartEventId = 11005391,
                });
        }

        private static FogEntrance ResolveV2PostArtoriasExit(FogDsrFacts facts)
        {
            const string objectName = "o2916_0000";
            const int entityId = 1211892;
            const int completionFlag = 11210001;
            FogMapId mapId = new FogMapId("dlc");
            FogDsrFacts.Map map = facts.Maps.TryGetValue(mapId, out FogDsrFacts.Map found)
                ? found
                : throw new KeyNotFoundException("Compact V2 Artorias exit requires the pristine DLC map.");
            FogDsrObjectFact[] objects = map.ObjectDetails
                .Where(part => string.Equals(part.Name, objectName, StringComparison.Ordinal)).ToArray();
            FogBossAreaTagPolicy[] boss = FogProjectAnnotationPolicy.BossAreaTagPolicies
                .Where(policy => policy.Area.Value == "dlc_artorias" && policy.BossEventId == completionFlag && policy.Map == mapId).ToArray();
            if (objects.Length != 1 || objects[0].EntityId != entityId || boss.Length != 1
                || !facts.BossRewardCompletionFlags.Contains(completionFlag))
                throw new InvalidDataException("Compact V2 post-Artorias exit must resolve pristine o2916_0000/entity 1211892 and the verified Artorias completion flag 11210001.");

            FogCondition completion = new FogCondition(FogConditionKind.Atom,
                Array.AsReadOnly(new[] { FogConditionReference.BossCompletion(completionFlag) }));
            FogEntranceSide arenaSide = new FogEntranceSide(new FogAreaId("dlc_artorias"), completionFlag,
                0, 0, 0, 0, null, 0, completion, null, 0, 0, null, null, FogEntranceSideTags.Higher);
            FogEntranceSide beyondArenaSide = new FogEntranceSide(new FogAreaId("dlc_postartorias"),
                0, 0, 0, 0, 0, null, 0, null, null, 0, 0,
                new FogEntranceKey("o2920_0000"), null, FogEntranceSideTags.Higher);
            return new FogEntrance(objectName, entityId, mapId, "After Artorias", null,
                FogEntranceTags.Boss, arenaSide, beyondArenaSide);
        }

        private static FogKeyItem ResolveV2OrangeCharredRing(FogDsrFacts facts, FogAnnotationCatalog v1Catalog)
        {
            FogGameItemId item = facts.RequireUniqueGameItem(FogGameItemCategory.Accessory, "Orange Charred Ring");
            var sourceLotIds = facts.ItemLots.Values
                .Where(lot => lot.Items.Any(entry => entry.Item == item))
                .Select(lot => lot.Id)
                .ToHashSet();
            var indexedLots = v1Catalog.LotRows
                .Where(lot => sourceLotIds.Contains(lot.Lot.Value))
                .OrderBy(lot => lot.Lot.Value)
                .ToArray();
            if (indexedLots.Length == 0)
                throw new InvalidDataException("Compact Fog v2 cannot model the Orange Charred Ring condition without a pristine ItemLotParam source row in the project-owned exact lot index.");

            // The ring is a read-only logic fact. RandomOnly keeps both vanilla lots in place and
            // excludes the ring from Fog's randomized item identity while allowing the lava-gate
            // condition to follow the first verified Compact-owned source lot.
            return new FogKeyItem(new FogItemName("orange_charred_ring"), item,
                indexedLots[0].Area, FogKeyItemTags.RandomOnly);
        }

        private static IReadOnlyList<FogBossGatedRoute> ResolveV2BossGatedRoutes(FogDsrFacts facts, FogAnnotationCatalog baseCatalog)
        {
            FogBossGatedRoute[] policies =
            {
                new FogBossGatedRoute(new FogAreaId("demonruins"), new FogAreaId("demonruins_lava"), 11410410,
                    new FogAreaId("demonruins_ceaseless"), new FogEntranceKey("o4961_0000"),
                    new FogMapId("demonruins"), "h0018B1",
                    "Pristine m14_01_00_00 MSB places the Ceaseless boss on collision h0018B1 and places entrance object o4961_0000 at the boundary between main Demon Ruins and the encounter area. The boundary is the arena's return route and is randomizable; after defeating Ceaseless, the player must exit to its main-side area before traversing the newly passable lower route. h0018B1 is only a boss-completion identity anchor, not the route itself.")
            };
            var result = new List<FogBossGatedRoute>();
            var seen = new HashSet<(FogAreaId, FogAreaId)>();
            foreach (FogBossGatedRoute policy in policies)
            {
                if (!seen.Add((policy.Source, policy.Destination))) throw new InvalidDataException("Duplicate v2 boss-gated physical route.");
                if (string.IsNullOrWhiteSpace(policy.PhysicalRationale)) throw new InvalidDataException("A v2 boss-gated route must have separate physical evidence.");
                FogBossAreaTagPolicy[] bossPolicies = FogProjectAnnotationPolicy.BossAreaTagPolicies.Where(candidate =>
                    candidate.Area == policy.EncounterArea && candidate.BossEventId == policy.CompletionEventId && candidate.Map == policy.Map).ToArray();
                if (bossPolicies.Length != 1 || policy.Source == policy.EncounterArea)
                    throw new InvalidDataException($"Boss-gated route '{policy.Source}' must be distinct from its unique verified encounter area '{policy.EncounterArea}' for event {policy.CompletionEventId}.");
                FogAreaTagPolicy[] resolvedBoss = FogAnnotationPolicyResolver.ResolveBossAreaTagPolicies(facts, bossPolicies).ToArray();
                bool hasFlag = facts.BossRewardCompletionFlags.Contains(policy.CompletionEventId);
                FogDefaultFlagCollision[] collisionFlags = baseCatalog.DefaultFlagCols.Where(flag => flag.Map == policy.Map
                    && flag.Collision == policy.BossCompletionCollision && flag.BossFlag == policy.CompletionEventId).ToArray();
                FogEntrance boundary = baseCatalog.Entrances.SingleOrDefault(entrance => entrance.Name == policy.SeparatingEntrance.Value);
                bool boundarySeparatesEncounter = boundary != null && boundary.Map == policy.Map
                    && boundary.ASide != null && boundary.BSide != null
                    && ((boundary.ASide.Area == policy.Source && boundary.BSide.Area == policy.EncounterArea)
                        || (boundary.BSide.Area == policy.Source && boundary.ASide.Area == policy.EncounterArea));
                if (resolvedBoss.Length != 1 || !hasFlag || collisionFlags.Length != 1 || !boundarySeparatesEncounter)
                    throw new InvalidDataException($"Boss-gated route '{policy.Source}' -> '{policy.Destination}' lacks unique pristine boss-completion evidence or the verified separating entrance '{policy.SeparatingEntrance}'.");
                FogMapId sourceMap = FogProjectAnnotationPolicy.ResolveAreaMap(policy.Source);
                FogMapId destinationMap = FogProjectAnnotationPolicy.ResolveAreaMap(policy.Destination);
                if (sourceMap != policy.Map || destinationMap != policy.Map || !facts.Maps[policy.Map].Collisions.Contains(policy.BossCompletionCollision))
                    throw new InvalidDataException($"Boss-gated route '{policy.Source}' -> '{policy.Destination}' lacks the verified same-map boss-completion anchor '{policy.Map}/{policy.BossCompletionCollision}'.");
                result.Add(policy);
            }
            return Array.AsReadOnly(result.ToArray());
        }

        private static IReadOnlyList<FogBossCompletionTransition> ResolveV2BossCompletionTransitions(
            FogDsrFacts facts, FogEntrance[] entrances, IReadOnlySet<int> requiredCompletionEvents,
            IReadOnlySet<int> fixedPostBossRoutes)
        {
            var result = new List<FogBossCompletionTransition>();
            foreach (FogProjectEntranceResolution resolution in FogAnnotationPolicyResolver.ResolveEntrancePoliciesV1(facts)
                .Where(value => value.Policy.Kind == FogProjectEntranceKind.BossBoundary
                    && requiredCompletionEvents.Contains(checked((int)value.BossTriggerEventId))))
            {
                FogBossAreaTagPolicy boss = FogProjectAnnotationPolicy.BossAreaTagPolicies.Single(value => value.Area == resolution.Policy.DestinationArea);
                if (resolution.BossTriggerEventId != boss.BossEventId || resolution.BossTriggerArea == null
                    || !facts.BossRewardCompletionFlags.Contains(checked((int)boss.BossEventId)))
                    throw new InvalidDataException($"Boss completion {boss.BossEventId} has no matched trigger and common event-970 completion fact.");
                if (!resolution.Policy.InteractionEventId.HasValue
                    || !facts.Maps[resolution.Policy.Map].EventActionButtonFacts.Any(check =>
                        check.EventId == resolution.Policy.InteractionEventId.Value
                        && check.TargetEntityId == resolution.Policy.BossTriggerRegionEntityId))
                    throw new InvalidDataException($"Boss completion {boss.BossEventId} has no pristine encounter-start event checking its authored trigger region.");
                FogEntrance entrance = entrances.Single(value => value.Name == resolution.ResolvedObjectPartName);
                FogEntranceSide side = new[] { entrance.ASide, entrance.BSide }
                    .Single(value => value.Area == resolution.Policy.DestinationArea);
                if (side.CustomWarp != null || side.BossTrigger != 0 || side.BossTriggerArea != null
                    || side.BossEncounterStartEventId != resolution.Policy.InteractionEventId)
                    throw new InvalidDataException($"Boss completion {boss.BossEventId} does not use the verified FogMod-default landing.");
                result.Add(new FogBossCompletionTransition(checked((int)boss.BossEventId),
                    checked((int)resolution.Policy.InteractionEventId.Value), boss.Area,
                    new FogEntranceKey(resolution.ResolvedObjectPartName), resolution.Policy.Map,
                    resolution.Policy.BossTriggerRegionEntityId.Value,
                    fixedPostBossRoutes.Contains(checked((int)boss.BossEventId))));
            }
            if (requiredCompletionEvents.Contains(11000001))
                result.Add(ResolveV2GapingDragonCompletionTransition(facts, entrances));
            return Array.AsReadOnly(result.ToArray());
        }

        private static FogBossCompletionTransition ResolveV2GapingDragonCompletionTransition(
            FogDsrFacts facts, FogEntrance[] entrances)
        {
            const int completionEventId = 11000001;
            const int encounterStartEventId = 11005391;
            const int triggerRegionEntityId = 1002998;
            const int completionFlag = 2;
            var mapId = new FogMapId("depths");
            FogDsrFacts.Map map = facts.Maps[mapId];
            FogEntrance entrance = entrances.Single(value => value.Name == "o1400_0000");
            FogEntranceSide bossSide = entrance.BSide?.Area.Value == "depths_gaping" ? entrance.BSide
                : entrance.ASide?.Area.Value == "depths_gaping" ? entrance.ASide
                : throw new InvalidDataException("Compact V2 Gaping Dragon completion must resolve the boss side of o1400_0000.");
            bool hasCompletion = facts.BossRewardCompletionFlags.Contains(completionFlag)
                && map.EventFlagWrites.Contains(new FogDsrEventFlagWrite(completionEventId, completionFlag));
            bool hasEncounterTrigger = map.EventActionButtonFacts.Any(check =>
                check.EventId == encounterStartEventId && check.InstructionId == 19
                && check.TargetEntityId == triggerRegionEntityId && check.LineSegmentEntityId == entrance.Id)
                && map.RegionDetails.Count(region => region.EntityId == triggerRegionEntityId) == 1;
            bool defaultLanding = bossSide.BossTrigger == 0 && bossSide.BossTriggerArea == null
                && bossSide.CustomWarp == null && bossSide.BossEncounterStartEventId == encounterStartEventId;
            if (!hasCompletion || !hasEncounterTrigger || !defaultLanding)
                throw new InvalidDataException("Compact V2 Gaping Dragon completion must be anchored to pristine event 11000001/flag 2, event 11005391's boss-room action for target 1002998 and fog entity 1001990, and the o1400_0000 default fog landing.");
            return new FogBossCompletionTransition(completionEventId, encounterStartEventId,
                new FogAreaId("depths_gaping"), new FogEntranceKey("o1400_0000"), mapId,
                triggerRegionEntityId, RequireFixedPostBossRoute: false);
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
