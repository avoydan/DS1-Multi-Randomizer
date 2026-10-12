using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FogHost;

namespace FogReconstruction
{
    public sealed record WorldTransitionInventoryEntry(
        string ProjectTransitionId,
        string Kind,
        string SourceMap,
        string SourceArea,
        string DestinationArea,
        string AnchorMap,
        string AnchorObjectEntity,
        string AnchorRegionEntity,
        string AnchorEvent,
        string Conditions,
        string RuntimeConsumer,
        string ProjectRationale,
        string Status);

    public sealed record WorldTransitionInventorySummary(
        int StaticAreaLinks,
        int EntranceCandidates,
        int WarpCandidates,
        int NoProgressAreaDispositions,
        int UnresolvedAreaDispositions,
        int DuplicatePhysicalOwners);

    public sealed record WorldTransitionTreasureSource(string Map, string TreasurePart, string Collision, int ItemLotId);
    public sealed record WorldTransitionEnemyDropSource(string Map, string EnemyPart, int NpcParamId, int DropSlot, int ItemLotId);
    public sealed record WorldTransitionEventAwardSource(
        string EventArchive,
        long EventId,
        int InstructionIndex,
        int ItemLotId,
        long? InitializerEventId,
        int? InitializerInstructionIndex);

    public sealed record WorldTransitionItemEvidence(
        string ProjectTransitionId,
        string GameItemId,
        string EnglishName,
        IReadOnlyList<int> ItemLotRows,
        IReadOnlyList<WorldTransitionTreasureSource> LocatedTreasureSources,
        IReadOnlyList<WorldTransitionEnemyDropSource> LocatedEnemyDropSources,
        IReadOnlyList<WorldTransitionEventAwardSource> LocatedEventAwardSources);

    public sealed record WorldTransitionInventoryReport(
        string Format,
        string Method,
        WorldTransitionInventorySummary Summary,
        IReadOnlyList<WorldTransitionInventoryEntry> Transitions,
        IReadOnlyList<WorldTransitionInventoryEntry> AreaDispositions,
        IReadOnlyList<WorldTransitionItemEvidence> RequiredItems);

    /// <summary>
    /// Development inventory of project-authored world boundaries. It reads no reference
    /// annotation data; missing project policies remain visible as unresolved dispositions.
    /// </summary>
    public static class WorldTransitionInventory
    {
        public const string Format = "ds1tr-fog-world-transition-inventory-1";

        public static void Write(string dataRoot, string pristineGameRoot, string outputPath)
        {
            string output = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot != null && IsWithin(repositoryRoot, output))
                throw new InvalidOperationException("The world transition inventory must be written outside the repository.");

            DataRoot.Verify(dataRoot);
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("The world transition inventory requires byte-verified pristine DSR EMEVD facts.");

            IReadOnlyList<FogAreaLink> staticLinks = FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(
                facts, FogProjectAnnotationPolicy.AreaLinkPolicies, Array.Empty<FogKeyItem>());
            IReadOnlyList<FogAreaNoStaticLinksPolicy> noStatic = FogAnnotationPolicyResolver.ResolveAreaNoStaticLinksPolicies(facts);
            IReadOnlyList<FogProjectEntranceBoundaryPolicy> authoredEntrances = FogAnnotationPolicyResolver.ResolveEntranceBoundaryPolicies(facts);
            var transitions = new List<WorldTransitionInventoryEntry>();
            foreach ((FogAreaLinkPolicy policy, FogAreaLink resolved) in FogProjectAnnotationPolicy.AreaLinkPolicies.Zip(staticLinks))
            {
                transitions.Add(new WorldTransitionInventoryEntry(
                    $"area:{policy.Source.Value}->{policy.Destination.Value}",
                    "STATIC_AREA_LINK",
                    FogProjectAnnotationPolicy.ResolveAreaMap(policy.Source).Value,
                    policy.Source.Value,
                    policy.Destination.Value,
                    null,
                    null,
                    null,
                    policy.DestinationBossEventId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    resolved.Condition?.ToFogModCondition(FogProjectAnnotationPolicy.ResolveAreaProjection),
                    "FogMod.Graph.Construct / Area.To",
                    policy.Rationale,
                    "PROJECT_POLICY_VALIDATED"));
            }

            // The Pinwheel gate is declared from both Area sides in the current policy. Collapse
            // that paired ownership declaration to one physical boundary in the inventory.
            FogAreaNoStaticLinksPolicy pinwheel = noStatic.Single(policy =>
                policy.Area == new FogAreaId("catacombs_pinwheel") && policy.EntranceObjectEntityId.HasValue);
            FogAreaNoStaticLinksPolicy approach = noStatic.Single(policy =>
                policy.Area == new FogAreaId("catacombs_prepinwheel") && policy.EntranceObjectEntityId.HasValue);
            if (pinwheel.EntranceMap != approach.EntranceMap
                || pinwheel.EntranceObjectEntityId != approach.EntranceObjectEntityId
                || pinwheel.EntranceTriggerRegionEntityId != approach.EntranceTriggerRegionEntityId
                || pinwheel.EntranceEventId != approach.EntranceEventId)
                throw new InvalidDataException("The two Pinwheel Area-side policies no longer identify one physical Entrance boundary.");
            transitions.Add(new WorldTransitionInventoryEntry(
                "entrance:catacombs-pinwheel-fog-gate",
                "ENTRANCE",
                "catacombs",
                "catacombs_prepinwheel",
                "catacombs_pinwheel",
                pinwheel.EntranceMap.Value.Value,
                pinwheel.EntranceObjectEntityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pinwheel.EntranceTriggerRegionEntityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pinwheel.EntranceEventId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                "FogMod.Graph.Construct / Entrances; FogMod.GameDataWriter",
                "One pristine Catacombs fog-gate boundary is owned by the Entrance model from both Area sides; the corresponding Area.To edge is intentionally absent.",
                "PHYSICAL_ANCHOR_VALIDATED; CONSUMED_ENTRANCE_FIELDS_OPEN"));

            foreach (FogProjectEntranceBoundaryPolicy entrance in authoredEntrances)
            {
                transitions.Add(new WorldTransitionInventoryEntry(
                    $"entrance:{entrance.ProjectTransitionId}",
                    "ENTRANCE",
                    entrance.Map.Value,
                    entrance.ASideArea.Value,
                    entrance.BSideArea.Value,
                    entrance.Map.Value,
                    entrance.InteractionObjectEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    entrance.InteractionEventId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"item:{entrance.RequiredItemName.Value} ({entrance.RequiredItemEnglishName})",
                    "FogMod.Graph.Construct / Entrances; FogMod.GameDataWriter",
                    entrance.Rationale,
                    "GAME_FACTS_VALIDATED; ENTRANCE_DTO_AUTHORED; KEY_ITEM_POLICY_OPEN"));
            }

            var areaDispositions = new List<WorldTransitionInventoryEntry>();
            var staticallyOwnedAreas = FogProjectAnnotationPolicy.AreaLinkPolicies.Select(policy => policy.Source).ToHashSet();
            var noStaticByArea = noStatic.ToDictionary(policy => policy.Area);
            foreach (FogAreaId area in FogProjectAnnotationPolicy.AreaIdentityOrder)
            {
                if (staticallyOwnedAreas.Contains(area)) continue;
                if (noStaticByArea.TryGetValue(area, out FogAreaNoStaticLinksPolicy policy))
                {
                    if (area == new FogAreaId("catacombs_pinwheel") || area == new FogAreaId("catacombs_prepinwheel")) continue;
                    areaDispositions.Add(new WorldTransitionInventoryEntry(
                        $"area:{area.Value}:no-static-link",
                        "NO_PROGRESS_RELATION",
                        FogProjectAnnotationPolicy.ResolveAreaMap(area).Value,
                        area.Value,
                        null,
                        policy.EntranceMap?.Value,
                        policy.EntranceObjectEntityId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        policy.EntranceTriggerRegionEntityId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        policy.BossEventId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? policy.EntranceEventId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        null,
                        policy.BossEventId.HasValue ? "FogMod.Graph.Construct / Area.To; boss-event policy" : "FogMod.Graph.Construct / Area.To",
                        policy.Rationale,
                        "PROJECT_POLICY_VALIDATED"));
                }
                else
                {
                    areaDispositions.Add(new WorldTransitionInventoryEntry(
                        $"area:{area.Value}:transition-owner-unresolved",
                        "UNRESOLVED",
                        FogProjectAnnotationPolicy.ResolveAreaMap(area).Value,
                        area.Value,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "FogMod.Graph.Construct / Area.To, Entrances, Warps",
                        "No project-owned transition disposition is authored for this Area yet.",
                        "UNRESOLVED_PROJECT_POLICY"));
                }
            }

            string[] physicalOwners = transitions
                .Where(HasPhysicalAnchor)
                .Select(PhysicalAnchorKey)
                .ToArray();
            int duplicates = physicalOwners.Length - physicalOwners.Distinct(StringComparer.Ordinal).Count();
            if (duplicates != 0) throw new InvalidDataException($"The project world transition inventory assigns {duplicates} physical boundary anchors more than once.");

            var summary = new WorldTransitionInventorySummary(
                transitions.Count(entry => entry.Kind == "STATIC_AREA_LINK"),
                transitions.Count(entry => entry.Kind == "ENTRANCE"),
                transitions.Count(entry => entry.Kind == "WARP"),
                areaDispositions.Count(entry => entry.Kind == "NO_PROGRESS_RELATION"),
                areaDispositions.Count(entry => entry.Kind == "UNRESOLVED"),
                duplicates);
            WorldTransitionItemEvidence[] requiredItems = authoredEntrances
                .Select(entrance =>
                {
                    int[] itemLotRows = facts.ItemLots.Values
                        .Where(row => row.Items.Any(item => item.Item == entrance.RequiredItem))
                        .Select(row => row.Id)
                        .OrderBy(id => id)
                        .ToArray();
                    var itemLotSet = itemLotRows.ToHashSet();
                    WorldTransitionTreasureSource[] sources = facts.ResolvedTreasureLots
                        .Where(treasure => itemLotSet.Contains(treasure.ItemLotId))
                        .Select(treasure => new WorldTransitionTreasureSource(
                            treasure.Map.Value,
                            treasure.PartName,
                            treasure.PartCollisionName,
                            treasure.ItemLotId))
                        .Distinct()
                        .OrderBy(source => source.Map, StringComparer.Ordinal)
                        .ThenBy(source => source.ItemLotId)
                        .ThenBy(source => source.TreasurePart, StringComparer.Ordinal)
                        .ToArray();
                    var npcDropLots = facts.NpcDropLots
                        .Where(drop => itemLotSet.Contains(drop.ItemLotId))
                        .GroupBy(drop => drop.NpcParamId)
                        .ToDictionary(group => group.Key, group => group.ToArray());
                    WorldTransitionEnemyDropSource[] enemySources = facts.Maps.Values
                        .SelectMany(map => map.EnemyDetails
                            .Where(enemy => npcDropLots.ContainsKey(enemy.NpcParamId))
                            .SelectMany(enemy => npcDropLots[enemy.NpcParamId].Select(drop => new WorldTransitionEnemyDropSource(
                                map.Id.Value,
                                enemy.Name,
                                enemy.NpcParamId,
                                drop.Slot,
                                drop.ItemLotId))))
                        .Distinct()
                        .OrderBy(source => source.Map, StringComparer.Ordinal)
                        .ThenBy(source => source.EnemyPart, StringComparer.Ordinal)
                        .ThenBy(source => source.DropSlot)
                        .ToArray();
                    WorldTransitionEventAwardSource[] eventSources = facts.ItemLotAwards
                        .Where(award => award.ItemLotId.HasValue && itemLotSet.Contains(award.ItemLotId.Value))
                        .Select(award => new WorldTransitionEventAwardSource(
                            award.EventArchiveName,
                            award.EventId,
                            award.InstructionIndex,
                            award.ItemLotId.Value,
                            award.InitializerEventId,
                            award.InitializerInstructionIndex))
                        .OrderBy(source => source.EventArchive, StringComparer.Ordinal)
                        .ThenBy(source => source.EventId)
                        .ThenBy(source => source.InstructionIndex)
                        .ToArray();
                    return new WorldTransitionItemEvidence(
                        entrance.ProjectTransitionId,
                        entrance.RequiredItem.ToString(),
                        entrance.RequiredItemEnglishName,
                        Array.AsReadOnly(itemLotRows),
                        Array.AsReadOnly(sources),
                        Array.AsReadOnly(enemySources),
                        Array.AsReadOnly(eventSources));
                })
                .ToArray();
            var report = new WorldTransitionInventoryReport(
                Format,
            "Enumerates current project-owned static routes, evidence-backed Entrance boundary policies, the anchored Pinwheel Entrance candidate, and Area no-static-link dispositions. It validates numeric game anchors against pristine DSR. No reference transition endpoint or condition is read. Entrance candidates whose consumed fields have not been authored remain visibly open; no Warp policy is currently authored.",
                summary,
                Array.AsReadOnly(transitions.ToArray()),
                Array.AsReadOnly(areaDispositions.ToArray()),
                Array.AsReadOnly(requiredItems));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Project world-transition inventory: static={summary.StaticAreaLinks}, entrances={summary.EntranceCandidates}, warps={summary.WarpCandidates}, no-static={summary.NoProgressAreaDispositions}, unresolved-area-dispositions={summary.UnresolvedAreaDispositions}, duplicate-owners={summary.DuplicatePhysicalOwners}. Report: {output}");
        }

        public static void SelfTest()
        {
            var entry = new WorldTransitionInventoryEntry("a", "ENTRANCE", "catacombs", "from", "to", "catacombs", "1301990", "1302998", "11305391", null, "test consumer", "test rationale", "validated");
            var same = entry with { ProjectTransitionId = "b" };
            int duplicateOwners = new[] { entry, same }
                .Where(HasPhysicalAnchor)
                .Select(PhysicalAnchorKey)
                .Count() - new[] { entry, same }
                    .Where(HasPhysicalAnchor)
                    .Select(PhysicalAnchorKey)
                    .Distinct(StringComparer.Ordinal).Count();
            if (duplicateOwners != 1) throw new InvalidOperationException("World transition inventory did not detect duplicate ownership of one physical boundary.");

            var noAnchor = entry with { ProjectTransitionId = "c", AnchorMap = null, AnchorObjectEntity = null, AnchorRegionEntity = null };
            if (new[] { entry, noAnchor }.Where(HasPhysicalAnchor).Select(PhysicalAnchorKey).Distinct(StringComparer.Ordinal).Count() != 1)
                throw new InvalidOperationException("An unanchored project-policy transition should not collide with an evidence-backed boundary identity.");

            var door = entry with
            {
                ProjectTransitionId = "door-a",
                AnchorMap = "depths",
                AnchorObjectEntity = "1001200",
                AnchorRegionEntity = null,
                AnchorEvent = "11000110",
            };
            var sameDoor = door with { ProjectTransitionId = "door-b" };
            if (new[] { door, sameDoor }.Where(HasPhysicalAnchor).Select(PhysicalAnchorKey).Distinct(StringComparer.Ordinal).Count() != 1)
                throw new InvalidOperationException("An object/event boundary without a region anchor was not recognized as one physical owner.");
            Console.WriteLine("World transition inventory self-tests passed (3 assertions).");
        }

        private static bool HasPhysicalAnchor(WorldTransitionInventoryEntry entry) =>
            !string.IsNullOrWhiteSpace(entry.AnchorMap)
            && (entry.AnchorObjectEntity != null || entry.AnchorRegionEntity != null || entry.AnchorEvent != null);

        private static string PhysicalAnchorKey(WorldTransitionInventoryEntry entry) =>
            $"{entry.AnchorMap}:{entry.AnchorObjectEntity ?? "-"}:{entry.AnchorRegionEntity ?? "-"}:{entry.AnchorEvent ?? "-"}";

        private static string FindRepositoryRoot(string start)
        {
            DirectoryInfo current = new DirectoryInfo(Path.GetFullPath(start));
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
                current = current.Parent;
            }
            return null;
        }

        private static bool IsWithin(string root, string path)
        {
            string relative = Path.GetRelativePath(root, path);
            return relative == "." || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        }
    }
}
