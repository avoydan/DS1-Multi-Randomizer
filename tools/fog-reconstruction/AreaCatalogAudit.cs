using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FogHost;
using FogMod;
using YamlDotNet.Serialization;
using static FogMod.AnnotationData;

namespace FogReconstruction
{
    public sealed record AreaStaticLinkReport(string To, string Condition, IReadOnlyList<string> Tags);

    public sealed record AreaConditionReport(string To, string Condition);

    public sealed record AreaCatalogEntryReport(
        string AreaId,
        string Projection,
        IReadOnlyList<string> Maps,
        int ReferenceStaticLinkCount,
        IReadOnlyList<string> Tags,
        string ScalingBase,
        string ScalingBaseEvidence,
        IReadOnlyList<AreaStaticLinkReport> StaticLinks,
        IReadOnlyList<AreaConditionReport> Conditions,
        IReadOnlyList<string> UnresolvedFields,
        string Evidence,
        string Classification,
        string Status,
        string Comparison);

    public sealed record AreaCatalogSummaryReport(
        int Total,
        int Derived,
        int ProjectPolicy,
        int ResolvedTagPolicies,
        int TagPoliciesCompared,
        int TagPoliciesMatched,
        int TagPoliciesDifferent,
        int Unresolved,
        int R,
        int E,
        int B,
        int U);

    public sealed record AreaOracleShapeReport(
        int AreasWithTags,
        IReadOnlyDictionary<string, int> AreaTagCounts,
        int AreasWithScalingBase,
        int AreasWithStaticLinks,
        int StaticLinks,
        int ConditionedStaticLinks,
        IReadOnlyDictionary<string, int> StaticLinkTagCounts);

    public sealed record AreaCatalogAuditReport(
        string Format,
        string OracleUse,
        AreaOracleShapeReport ReferenceShape,
        CombinedTransitionGraphComparison CombinedTransitionGraph,
        AreaCatalogSummaryReport Summary,
        IReadOnlyList<AreaCatalogEntryReport> Areas);

    public sealed record AreaTagIdentityReport(
        string Format,
        string OracleUse,
        IReadOnlyDictionary<string, IReadOnlyList<string>> AreasByTag);

    /// <summary>
    /// Development-only completeness report for project-owned Areas. The oracle supplies only the
    /// comparison inventory and per-entry comparison result; its graph fields are never emitted.
    /// </summary>
    public static class AreaCatalogAudit
    {
        public const string Format = "ds1tr-fog-area-catalog-audit-8";

        public static void Write(string fogDataRoot, string pristineGameDirectory, string outputPath)
        {
            string output = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot != null && IsWithin(repositoryRoot, output))
                throw new InvalidOperationException("The area audit report must be written outside the repository.");

            DataRoot.Verify(fogDataRoot);
            FogDsrFacts facts = FogDsrFacts.Read(fogDataRoot, pristineGameDirectory);
            Annotations oracle;
            using (StreamReader reader = File.OpenText(Path.Combine(fogDataRoot, "dist", "fog.txt")))
                oracle = new DeserializerBuilder().Build().Deserialize<Annotations>(reader);

            IReadOnlyList<FogAreaTagPolicy> resolvedCompleteTagPolicies =
                FogProjectAnnotationPolicy.ResolveCompleteAreaTagPolicies(facts);
            var tagPolicies = resolvedCompleteTagPolicies.ToDictionary(policy => policy.Area);
            IReadOnlyList<FogArea> areas = BuildAuditedAreaSlice(facts, tagPolicies);
            IReadOnlyList<FogCustomStart> starts = FogAnnotationPolicyResolver.ResolveCustomStarts(facts, FogProjectAnnotationPolicy.CustomStarts);
            var catalog = new FogAnnotationCatalog(
                areas,
                Array.Empty<FogKeyItem>(),
                Array.Empty<FogWarp>(),
                Array.Empty<FogEntrance>(),
                starts,
                Array.Empty<FogDefaultCost>(),
                Array.Empty<FogEnemyScalingRule>(),
                Array.Empty<FogLotLocation>(),
                Array.Empty<FogDefaultFlagCollision>());
            string[] factErrors = facts.Validate(catalog).ToArray();
            if (factErrors.Length > 0)
                throw new InvalidDataException("Project-owned Area facts failed validation: " + string.Join("; ", factErrors));

            IReadOnlyList<FogAreaNoStaticLinksPolicy> noStaticLinkPolicies =
                FogAnnotationPolicyResolver.ResolveAreaNoStaticLinksPolicies(facts);
            var reviewed = FogProjectAnnotationPolicy.ReviewedStartAreaRecords.Select(area => area.Id)
                .Concat(noStaticLinkPolicies.Select(policy => policy.Area))
                .ToHashSet();
            var evidence = new Dictionary<FogAreaId, string>();
            foreach (FogCustomStartPolicy start in FogProjectAnnotationPolicy.CustomStarts)
            {
                string text = $"Pristine DSR spawn region '{start.SpawnPointRegionName}' on map '{start.Map}' anchors the start-area map membership.";
                if (reviewed.Contains(start.Area))
                {
                    FogAreaLinkPolicy[] staticRoutes = FogProjectAnnotationPolicy.AreaLinkPolicies
                        .Where(policy => policy.Source == start.Area).ToArray();
                    text += staticRoutes.Length == 0
                        ? " Project policy treats this as a progression node; cross-area travel is represented by Entrance/Warp records, and review found no additional static Area.To link."
                        : " Project policy treats this as a progression node and records its independently authored same-map static route separately from Entrance/Warp transitions: "
                            + string.Join("; ", staticRoutes.Select(route => route.Rationale));
                }
                if (evidence.TryGetValue(start.Area, out string previous) && !string.Equals(previous, text, StringComparison.Ordinal))
                    evidence[start.Area] = previous + " " + text;
                else evidence[start.Area] = text;
            }

            foreach (FogAreaTagPolicy policy in tagPolicies.Values)
            {
                if (evidence.TryGetValue(policy.Area, out string previous))
                    evidence[policy.Area] = previous + " " + policy.Rationale;
                else
                    evidence[policy.Area] = policy.Rationale;
            }

            foreach (FogAreaNoStaticLinksPolicy policy in noStaticLinkPolicies)
            {
                if (evidence.TryGetValue(policy.Area, out string previous))
                    evidence[policy.Area] = previous + " " + policy.Rationale;
                else
                    evidence[policy.Area] = policy.Rationale;
            }

            ValidateAreaIdentityPolicies(oracle, facts);
            IReadOnlyList<FogProjectEntranceBoundaryPolicy> entrancePolicies =
                FogAnnotationPolicyResolver.ResolveEntranceBoundaryPolicies(facts);
            Annotations projectTransitionModel = BuildTransitionGraphProjection(catalog, entrancePolicies);
            AreaCatalogAuditReport report = BuildReport(oracle, catalog, reviewed, evidence, facts, tagPolicies, projectTransitionModel);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static IReadOnlyList<FogArea> BuildAuditedAreaSlice(
            FogDsrFacts facts,
            IReadOnlyDictionary<FogAreaId, FogAreaTagPolicy> tagPolicies)
        {
            IReadOnlyList<FogAreaLinkPolicy> policies = FogProjectAnnotationPolicy.AreaLinkPolicies;
            IReadOnlyList<FogAreaLink> resolvedLinks = FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(
                facts, policies, Array.Empty<FogKeyItem>());
            var areas = FogProjectAnnotationPolicy.AreaIdentityOrder
                .Select(id => new FogArea(
                    id,
                    FogProjectAnnotationPolicy.ResolveAreaProjection(id),
                    Array.AsReadOnly(new[] { FogProjectAnnotationPolicy.ResolveAreaMap(id) }),
                    tagPolicies.TryGetValue(id, out FogAreaTagPolicy tagPolicy) ? tagPolicy.Tags : FogAreaTags.None,
                    null,
                    Array.Empty<FogAreaLink>()))
                .ToList();
            var indexes = areas.Select((area, index) => (area.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
            var linksBySource = new Dictionary<FogAreaId, List<FogAreaLink>>();

            void EnsureArea(FogAreaId id)
            {
                if (indexes.ContainsKey(id)) return;
                throw new InvalidDataException($"Area route policy references '{id}', which is absent from the stable 131-area project inventory.");
            }

            for (int index = 0; index < policies.Count; index++)
            {
                FogAreaLinkPolicy policy = policies[index];
                EnsureArea(policy.Source);
                EnsureArea(policy.Destination);
                if (!linksBySource.TryGetValue(policy.Source, out List<FogAreaLink> sourceLinks))
                {
                    sourceLinks = new List<FogAreaLink>();
                    linksBySource.Add(policy.Source, sourceLinks);
                }
                sourceLinks.Add(resolvedLinks[index]);
            }

            foreach (KeyValuePair<FogAreaId, List<FogAreaLink>> entry in linksBySource)
            {
                int index = indexes[entry.Key];
                FogArea area = areas[index];
                areas[index] = new FogArea(area.Id, area.FogModName, area.Maps, area.Tags, area.ScalingBase,
                    Array.AsReadOnly(entry.Value.ToArray()));
            }
            return Array.AsReadOnly(areas.ToArray());
        }

        public static void WriteReferenceTagIdentityReport(string fogDataRoot, string outputPath)
        {
            string output = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot != null && IsWithin(repositoryRoot, output))
                throw new InvalidOperationException("Area tag comparison report must be written outside the repository.");

            DataRoot.Verify(fogDataRoot);
            Annotations oracle;
            using (StreamReader reader = File.OpenText(Path.Combine(fogDataRoot, "dist", "fog.txt")))
                oracle = new DeserializerBuilder().Build().Deserialize<Annotations>(reader);
            if (oracle?.Areas == null) throw new InvalidDataException("The comparison oracle has no Areas inventory.");

            string[] tags = { "boss", "optional", "trivial" };
            var byTag = tags.ToDictionary(tag => tag,
                tag => (IReadOnlyList<string>)Array.AsReadOnly(oracle.Areas
                    .Where(area => ActiveTags(area?.TagList).Contains(tag))
                    .Select(area => area.Name)
                    .ToArray()),
                StringComparer.Ordinal);
            var report = new AreaTagIdentityReport(
                "ds1tr-fog-area-tag-identities-1",
                "Tag identity comparison only; do not use these lists as project policy. Validate each project rule independently against pristine DSR and balancing semantics.",
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<string>>(byTag));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static AreaCatalogAuditReport BuildReport(
            Annotations oracle,
            FogAnnotationCatalog catalog,
            IReadOnlySet<FogAreaId> completeAreas,
            IReadOnlyDictionary<FogAreaId, string> evidence,
            FogDsrFacts facts = null,
            IReadOnlyDictionary<FogAreaId, FogAreaTagPolicy> tagPolicies = null,
            Annotations projectTransitionModel = null)
        {
            if (oracle?.Areas == null) throw new ArgumentException("The comparison oracle has no Areas inventory.", nameof(oracle));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            completeAreas ??= new HashSet<FogAreaId>();
            evidence ??= new Dictionary<FogAreaId, string>();

            var projectByProjection = catalog.Areas.ToDictionary(area => area.FogModName.Value, StringComparer.Ordinal);
            Annotations projection = catalog.ToFogModAnnotations();
            var projectionByName = projection.Areas.ToDictionary(area => area.Name, StringComparer.Ordinal);
            var entries = new List<AreaCatalogEntryReport>();
            foreach (Area referenceArea in oracle.Areas)
            {
                if (referenceArea == null || string.IsNullOrWhiteSpace(referenceArea.Name))
                    throw new InvalidDataException("The comparison oracle contains an unnamed Area entry.");
                if (!projectByProjection.TryGetValue(referenceArea.Name, out FogArea projectArea))
                {
                    FogAreaId unresolvedId = new FogAreaId(referenceArea.Name);
                    string map = facts == null ? null : FogProjectAnnotationPolicy.ResolveAreaMap(unresolvedId).Value;
                    string mapEvidence = map == null
                        ? null
                        : $"Project-owned area ID namespace policy assigns this area to DSR map '{map}'; tags, scaling base, and static links still need resolution.";
                    FogAreaTagPolicy tagPolicy = null;
                    if (tagPolicies != null) tagPolicies.TryGetValue(unresolvedId, out tagPolicy);
                    string[] projectTags = Tags(tagPolicy?.Tags ?? FogAreaTags.None);
                    string projectEvidence = tagPolicy == null
                        ? mapEvidence
                        : string.Join(" ", new[] { mapEvidence, tagPolicy.Rationale }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    entries.Add(new AreaCatalogEntryReport(
                        unresolvedId.Value,
                        FogProjectAnnotationPolicy.ResolveAreaProjection(unresolvedId).Value,
                        map == null ? Array.Empty<string>() : new[] { map },
                        referenceArea.To?.Count ?? 0,
                        projectTags,
                        null,
                        FogProjectAnnotationPolicy.AreaScalingBasePolicyRationale,
                        Array.Empty<AreaStaticLinkReport>(),
                        Array.Empty<AreaConditionReport>(),
                        new[] { "mapMembership", "tags", "staticLinks" },
                        projectEvidence,
                        "PROJECT_POLICY",
                        "UNRESOLVED",
                        "U"));
                    continue;
                }

                Area projectedArea = projectionByName[projectArea.FogModName.Value];
                bool hasCompletePolicy = completeAreas.Contains(projectArea.Id);
                string comparison = Compare(referenceArea, projectedArea);
                IReadOnlyList<AreaStaticLinkReport> links = (projectedArea.To ?? new List<Side>())
                    .Select(link => new AreaStaticLinkReport(link.Area, link.Cond, Tags(link.TagList)))
                    .ToArray();
                IReadOnlyList<AreaConditionReport> conditions = links
                    .Where(link => !string.IsNullOrWhiteSpace(link.Condition))
                    .Select(link => new AreaConditionReport(link.To, link.Condition))
                    .ToArray();
                entries.Add(new AreaCatalogEntryReport(
                    projectArea.Id.Value,
                    projectArea.FogModName.Value,
                    projectArea.Maps.Select(map => map.Value).ToArray(),
                    referenceArea.To?.Count ?? 0,
                    Tags(projectedArea.TagList),
                    projectArea.ScalingBase?.Value,
                    FogProjectAnnotationPolicy.AreaScalingBasePolicyRationale,
                    links,
                    conditions,
                    hasCompletePolicy ? Array.Empty<string>() : new[] { "staticLinks" },
                    evidence.TryGetValue(projectArea.Id, out string rationale) ? rationale : null,
                    "PROJECT_POLICY",
                    hasCompletePolicy ? "COMPLETE" : "UNRESOLVED",
                    hasCompletePolicy ? comparison : "U"));
            }

            if (entries.Select(entry => entry.AreaId).Distinct(StringComparer.Ordinal).Count() != entries.Count)
                throw new InvalidDataException("Area IDs in the comparison inventory are not unique.");
            int r = entries.Count(entry => entry.Comparison == "R");
            int e = entries.Count(entry => entry.Comparison == "E");
            int b = entries.Count(entry => entry.Comparison == "B");
            int u = entries.Count(entry => entry.Comparison == "U");
            int derived = catalog.Areas.Count(area => evidence.TryGetValue(area.Id, out string rationale)
                && (rationale.Contains("Pristine DSR", StringComparison.OrdinalIgnoreCase)
                    || rationale.Contains("pristine event", StringComparison.OrdinalIgnoreCase)));
            int projectPolicy = entries.Count(entry => string.Equals(entry.Classification, "PROJECT_POLICY", StringComparison.Ordinal));
            int resolvedTagPolicies = tagPolicies?.Count ?? 0;
            int tagPoliciesCompared = 0;
            int tagPoliciesMatched = 0;
            int tagPoliciesDifferent = 0;
            if (tagPolicies != null)
            {
                foreach (KeyValuePair<FogAreaId, FogAreaTagPolicy> entry in tagPolicies)
                {
                    string projectionName = FogProjectAnnotationPolicy.ResolveAreaProjection(entry.Key).Value;
                    Area referenceArea = oracle.Areas.SingleOrDefault(area =>
                        string.Equals(area?.Name, projectionName, StringComparison.Ordinal));
                    if (referenceArea == null) continue;
                    tagPoliciesCompared++;
                    if (ActiveTags(referenceArea.TagList).SetEquals(Tags(entry.Value.Tags))) tagPoliciesMatched++;
                    else tagPoliciesDifferent++;
                }
            }
            int unresolved = entries.Count(entry => entry.Status == "UNRESOLVED");
            CombinedTransitionGraphComparison combinedGraph = projectTransitionModel == null
                ? CombinedTransitionGraphAudit.Compare(oracle, catalog)
                : CombinedTransitionGraphAudit.Compare(oracle, projectTransitionModel);
            return new AreaCatalogAuditReport(
                Format,
                "Inventory and comparison only; reference tags, scaling bases, links, and conditions are not emitted or imported. Combined traversal compares FogMod.Graph output for Area.To, Entrances, and Warps. Missing project records use only the area ID namespace policy for map membership.",
                SummarizeReferenceShape(oracle),
                combinedGraph,
                new AreaCatalogSummaryReport(entries.Count, derived, projectPolicy, resolvedTagPolicies,
                    tagPoliciesCompared, tagPoliciesMatched, tagPoliciesDifferent, unresolved, r, e, b, u),
                Array.AsReadOnly(entries.ToArray()));
        }

        private static Annotations BuildTransitionGraphProjection(
            FogAnnotationCatalog catalog,
            IReadOnlyList<FogProjectEntranceBoundaryPolicy> entrancePolicies)
        {
            Annotations projection = catalog.ToFogModAnnotations();
            projection.Entrances = entrancePolicies.Select(policy =>
            {
                FogEntrance entrance = policy.ToFogEntrance();
                return new Entrance
                {
                    Name = entrance.Name,
                    ID = entrance.Id,
                    Area = entrance.Map.Value,
                    Text = entrance.Text,
                    DoorCond = entrance.DoorCondition?.ToFogModCondition(FogProjectAnnotationPolicy.ResolveAreaProjection),
                    Tags = EntranceTags(entrance.Tags),
                    ASide = EntranceSide(entrance.ASide),
                    BSide = EntranceSide(entrance.BSide),
                };
            }).ToList();
            projection.Warps = new List<Entrance>();
            return projection;
        }

        private static Side EntranceSide(FogEntranceSide side)
        {
            if (side == null) return null;
            return new Side
            {
                Area = FogProjectAnnotationPolicy.ResolveAreaProjection(side.Area).Value,
                Flag = side.Flag,
                TrapFlag = side.TrapFlag,
                EntryFlag = side.EntryFlag,
                BeforeWarpFlag = side.BeforeWarpFlag,
                BossTrigger = side.BossTrigger,
                BossTriggerArea = side.BossTriggerArea == null ? null : string.Join(" ", new[]
                {
                    side.BossTriggerArea.X, side.BossTriggerArea.Y, side.BossTriggerArea.Z,
                    side.BossTriggerArea.Yaw, side.BossTriggerArea.Width, side.BossTriggerArea.Height, side.BossTriggerArea.Depth,
                }.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))),
                WarpFlag = side.WarpFlag,
                Cond = side.Condition?.ToFogModCondition(FogProjectAnnotationPolicy.ResolveAreaProjection),
                CustomWarp = side.CustomWarp == null ? null : side.CustomWarp.Map.Value + " " + string.Join(" ", new[]
                {
                    side.CustomWarp.Position.X, side.CustomWarp.Position.Y, side.CustomWarp.Position.Z, side.CustomWarp.Position.Yaw,
                }.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))),
                CustomActionWidth = side.CustomActionWidth,
                ActionRegion = side.ActionRegion,
                ExcludeIfRandomized = side.ExcludeIfRandomized?.Value,
                DestinationMap = side.DestinationMap?.Value,
                Tags = EntranceSideTags(side.Tags),
            };
        }

        private static string EntranceTags(FogEntranceTags tags) => string.Join(" ", new[]
        {
            tags.HasFlag(FogEntranceTags.Boss) ? "boss" : null,
            tags.HasFlag(FogEntranceTags.Door) ? "door" : null,
            tags.HasFlag(FogEntranceTags.Lordvessel) ? "lordvessel" : null,
            tags.HasFlag(FogEntranceTags.Major) ? "major" : null,
            tags.HasFlag(FogEntranceTags.Pvp) ? "pvp" : null,
            tags.HasFlag(FogEntranceTags.Unused) ? "unused" : null,
            tags.HasFlag(FogEntranceTags.World) ? "world" : null,
        }.Where(value => value != null));

        private static string EntranceSideTags(FogEntranceSideTags tags) => string.Join(" ", new[]
        {
            tags.HasFlag(FogEntranceSideTags.Dnofts) ? "dnofts" : null,
            tags.HasFlag(FogEntranceSideTags.Higher) ? "higher" : null,
        }.Where(value => value != null));

        public static AreaOracleShapeReport SummarizeReferenceShape(Annotations oracle)
        {
            if (oracle?.Areas == null) throw new ArgumentException("The comparison oracle has no Areas inventory.", nameof(oracle));
            string[] areaTags = { "boss", "optional", "trivial" };
            string[] linkTags = { "hard", "shortcut", "temp" };
            var areaTagCounts = areaTags.ToDictionary(tag => tag,
                tag => oracle.Areas.Count(area => ActiveTags(area?.TagList).Contains(tag)), StringComparer.Ordinal);
            Side[] links = oracle.Areas.Where(area => area != null)
                .SelectMany(area => area.To ?? new List<Side>())
                .Where(side => side != null)
                .ToArray();
            var linkTagCounts = linkTags.ToDictionary(tag => tag,
                tag => links.Count(side => ActiveTags(side.TagList).Contains(tag)), StringComparer.Ordinal);
            return new AreaOracleShapeReport(
                oracle.Areas.Count(area => ActiveTags(area?.TagList).Count > 0),
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(areaTagCounts),
                oracle.Areas.Count(area => !string.IsNullOrWhiteSpace(area?.ScalingBase)),
                oracle.Areas.Count(area => area?.To?.Any() == true),
                links.Length,
                links.Count(side => !string.IsNullOrWhiteSpace(side.Cond)),
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(linkTagCounts));
        }

        private static void ValidateAreaIdentityPolicies(Annotations oracle, FogDsrFacts facts)
        {
            Area[] inventory = oracle.Areas.ToArray();
            string[] expectedOrder = FogProjectAnnotationPolicy.AreaIdentityOrder.Select(id => id.Value).ToArray();
            if (expectedOrder.Length != 131 || expectedOrder.Distinct(StringComparer.Ordinal).Count() != 131)
                throw new InvalidDataException($"The project area identity policy must contain 131 unique IDs; found {expectedOrder.Length}.");
            string[] oracleOrder = inventory.Where(area => area != null).Select(area => area.Name).ToArray();
            if (!oracleOrder.SequenceEqual(expectedOrder, StringComparer.Ordinal))
                throw new InvalidDataException("The project area identity/order inventory differs from the comparison inventory.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var projections = new HashSet<string>(StringComparer.Ordinal);
            foreach (FogAreaId id in FogProjectAnnotationPolicy.AreaIdentityOrder)
            {
                if (string.IsNullOrWhiteSpace(id.Value)) throw new InvalidDataException("The project area identity inventory contains an empty ID.");
                if (!ids.Add(id.Value)) throw new InvalidDataException($"The project area identity inventory contains duplicate ID '{id}'.");
                FogMapId map = FogProjectAnnotationPolicy.ResolveAreaMap(id);
                if (!facts.Maps.ContainsKey(map))
                    throw new InvalidDataException($"Project area '{id}' resolves to missing pristine DSR map '{map}'.");
                FogModAreaName projection = FogProjectAnnotationPolicy.ResolveAreaProjection(id);
                if (!projections.Add(projection.Value))
                    throw new InvalidDataException($"Project area '{id}' does not have a unique compatibility projection name '{projection}'.");
            }
        }

        private static string Compare(Area reference, Area project)
        {
            HashSet<string> referenceTags = ActiveTags(reference.TagList);
            HashSet<string> projectTags = ActiveTags(project.TagList);
            if (!referenceTags.SetEquals(projectTags)) return "U";
            bool scalingBaseDifferent = !string.Equals(reference.ScalingBase, project.ScalingBase, StringComparison.Ordinal);
            bool productEnablesScalingBase = FogHost.HostRequest.OptionFlags.Any(option =>
                string.Equals(option.Flag, "scalingbase", StringComparison.Ordinal)
                    || string.Equals(option.Flag, "dumpdist", StringComparison.Ordinal));
            if (scalingBaseDifferent && productEnablesScalingBase) return "U";
            bool representationDifference = !string.Equals(reference.Tags, project.Tags, StringComparison.Ordinal);

            Side[] referenceLinks = (reference.To ?? new List<Side>()).ToArray();
            Side[] projectLinks = (project.To ?? new List<Side>()).ToArray();
            if (referenceLinks.Length != projectLinks.Length) return "U";
            for (int index = 0; index < referenceLinks.Length; index++)
            {
                Side left = referenceLinks[index];
                Side right = projectLinks[index];
                if (!string.Equals(left.Area, right.Area, StringComparison.Ordinal)
                    || !string.Equals(left.Cond, right.Cond, StringComparison.Ordinal)
                    || !ActiveLinkTags(left.TagList).SetEquals(ActiveLinkTags(right.TagList))) return "U";
                if (!string.Equals(left.Tags, right.Tags, StringComparison.Ordinal)) representationDifference = true;
            }
            bool referenceNullLinks = reference.To == null;
            bool projectNullLinks = project.To == null;
            if (referenceNullLinks != projectNullLinks) representationDifference = true;
            return representationDifference ? "R" : "E";
        }

        private static HashSet<string> ActiveTags(IEnumerable<string> tags) => (tags ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.Ordinal);

        private static HashSet<string> ActiveLinkTags(IEnumerable<string> tags) => (tags ?? Array.Empty<string>())
            .Where(tag => tag == "hard" || tag == "shortcut" || tag == "temp")
            .ToHashSet(StringComparer.Ordinal);

        private static string[] Tags(IEnumerable<string> source) => (source ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        private static string[] Tags(FogAreaTags tags)
        {
            var values = new List<string>();
            if (tags.HasFlag(FogAreaTags.Boss)) values.Add("boss");
            if (tags.HasFlag(FogAreaTags.Optional)) values.Add("optional");
            if (tags.HasFlag(FogAreaTags.Trivial)) values.Add("trivial");
            return values.ToArray();
        }

        private static string[] Tags(FogAreaLinkTags tags)
        {
            var values = new List<string>();
            if (tags.HasFlag(FogAreaLinkTags.Hard)) values.Add("hard");
            if (tags.HasFlag(FogAreaLinkTags.Shortcut)) values.Add("shortcut");
            if (tags.HasFlag(FogAreaLinkTags.Temporary)) values.Add("temp");
            return values.ToArray();
        }

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

        private static bool IsWithin(string parent, string candidate)
        {
            string relative = Path.GetRelativePath(parent, candidate);
            return relative == "." || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        }
    }

    internal static class AreaCatalogAuditSelfTests
    {
        public static void Run()
        {
            if (FogProjectAnnotationPolicy.AreaIdentityOrder.Count != 131
                || FogProjectAnnotationPolicy.AreaIdentityOrder.Select(id => id.Value).Distinct(StringComparer.Ordinal).Count() != 131)
                throw new InvalidOperationException("The project area identity/order inventory must contain 131 unique IDs.");

            foreach (FogAreaMapMembershipPolicy policy in FogProjectAnnotationPolicy.AreaMapMembershipPolicies)
            {
                FogMapId resolved = FogProjectAnnotationPolicy.ResolveAreaMap(new FogAreaId(policy.Prefix + "_test"));
                if (resolved != policy.Map)
                    throw new InvalidOperationException($"Area namespace '{policy.Prefix}' resolved to '{resolved}' instead of '{policy.Map}'.");
            }
            bool unknownPrefixRejected = false;
            try { _ = FogProjectAnnotationPolicy.ResolveAreaMap(new FogAreaId("unknown_test_area")); }
            catch (InvalidDataException) { unknownPrefixRejected = true; }
            if (!unknownPrefixRejected) throw new InvalidOperationException("Unknown area namespaces must not be assigned a guessed DSR map.");
            if (FogProjectAnnotationPolicy.ResolveAreaProjection(new FogAreaId("burg_start")).Value != "burg_start")
                throw new InvalidOperationException("Area compatibility projection must preserve the project ID unless an explicit alias exists.");
            var area = new FogArea(new FogAreaId("home"), new FogModAreaName("home"),
                new[] { new FogMapId("asylum") }, FogAreaTags.None, null, Array.Empty<FogAreaLink>());
            var catalog = new FogAnnotationCatalog(
                new[] { area }, Array.Empty<FogKeyItem>(), Array.Empty<FogWarp>(), Array.Empty<FogEntrance>(),
                Array.Empty<FogCustomStart>(), Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(),
                Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
            var oracle = new Annotations
            {
                Areas = new List<Area>
                {
                    new Area { Name = "home", To = new List<Side>() },
                    new Area { Name = "missing", To = new List<Side>() },
                }
            };
            AreaOracleShapeReport shape = AreaCatalogAudit.SummarizeReferenceShape(new Annotations
            {
                Areas = new List<Area>
                {
                    new Area
                    {
                        Name = "shape",
                        Tags = "boss",
                        ScalingBase = "shape",
                        To = new List<Side> { new Side { Area = "target", Cond = "key", Tags = "hard" } },
                    },
                    new Area { Name = "plain", To = new List<Side>() },
                }
            });
            if (shape.AreasWithTags != 1
                || shape.AreaTagCounts["boss"] != 1
                || shape.AreasWithScalingBase != 1
                || shape.AreasWithStaticLinks != 1
                || shape.StaticLinks != 1
                || shape.ConditionedStaticLinks != 1
                || shape.StaticLinkTagCounts["hard"] != 1)
                throw new InvalidOperationException("Area reference shape summary did not count aggregate fields without exposing their values.");

            AreaCatalogAuditReport report = AreaCatalogAudit.BuildReport(oracle, catalog,
                new HashSet<FogAreaId> { area.Id },
                new Dictionary<FogAreaId, string> { [area.Id] = "synthetic evidence" });
            if (report.Summary.Total != 2 || report.Summary.ProjectPolicy != 2 || report.Summary.R != 1 || report.Summary.U != 1
                || report.Areas.Single(entry => entry.AreaId == "home").Status != "COMPLETE"
                || report.Areas.Single(entry => entry.AreaId == "home").UnresolvedFields.Count != 0
                || !report.Areas.Single(entry => entry.AreaId == "missing").UnresolvedFields.Contains("staticLinks")
                || report.Areas.Single(entry => entry.AreaId == "missing").Projection != "missing")
                throw new InvalidOperationException($"Area audit did not separate a reviewed project record from an unresolved inventory entry (total={report.Summary.Total}, E={report.Summary.E}, U={report.Summary.U}, home={report.Areas.Single(entry => entry.AreaId == "home").Status}/{report.Areas.Single(entry => entry.AreaId == "home").Comparison}, missing projection={(report.Areas.Single(entry => entry.AreaId == "missing").Projection ?? "null")}).");

            var linkCountOracle = new Annotations
            {
                Areas = new List<Area>
                {
                    new Area
                    {
                        Name = "home",
                        To = new List<Side> { new Side { Area = "candidate-a" }, new Side { Area = "candidate-b" } },
                    },
                },
            };
            AreaCatalogAuditReport linkCountReport = AreaCatalogAudit.BuildReport(linkCountOracle, catalog,
                new HashSet<FogAreaId> { area.Id }, new Dictionary<FogAreaId, string> { [area.Id] = "synthetic evidence" });
            if (linkCountReport.Areas.Single().ReferenceStaticLinkCount != 2 || linkCountReport.Summary.U != 1)
                throw new InvalidOperationException("Area audit must expose the comparison edge count without exposing reference endpoints.");

            var scalingArea = new FogArea(new FogAreaId("home"), new FogModAreaName("home"),
                new[] { new FogMapId("asylum") }, FogAreaTags.None, null, null);
            var scalingCatalog = new FogAnnotationCatalog(
                new[] { scalingArea }, Array.Empty<FogKeyItem>(), Array.Empty<FogWarp>(), Array.Empty<FogEntrance>(),
                Array.Empty<FogCustomStart>(), Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(),
                Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
            var debugScaleOracle = new Annotations
            {
                Areas = new List<Area> { new Area { Name = "home", Tags = string.Empty, ScalingBase = "some-base", To = null } },
            };
            report = AreaCatalogAudit.BuildReport(debugScaleOracle, scalingCatalog,
                new HashSet<FogAreaId> { scalingArea.Id }, new Dictionary<FogAreaId, string> { [scalingArea.Id] = "synthetic evidence" });
            if (report.Summary.E != 1 || report.Summary.B != 0 || report.Summary.U != 0
                || report.Areas.Single().Comparison != "E"
                || report.Areas.Single().ScalingBaseEvidence != FogProjectAnnotationPolicy.AreaScalingBasePolicyRationale)
                throw new InvalidOperationException($"A debug-only ScalingBase difference was not classified as behavior-equivalent under the shipped option contract (R={report.Summary.R}, E={report.Summary.E}, B={report.Summary.B}, U={report.Summary.U}, comparison={report.Areas.Single().Comparison}, scalingBase={report.Areas.Single().ScalingBase ?? "null"}, evidence={report.Areas.Single().ScalingBaseEvidence ?? "null"}).");

            var mismatchedTagPolicy = new FogAreaTagPolicy(area.Id, FogAreaTags.Boss, "synthetic policy comparison");
            report = AreaCatalogAudit.BuildReport(oracle, catalog,
                new HashSet<FogAreaId> { area.Id }, new Dictionary<FogAreaId, string>(),
                tagPolicies: new Dictionary<FogAreaId, FogAreaTagPolicy> { [area.Id] = mismatchedTagPolicy });
            if (report.Summary.ResolvedTagPolicies != 1 || report.Summary.TagPoliciesCompared != 1
                || report.Summary.TagPoliciesMatched != 0 || report.Summary.TagPoliciesDifferent != 1)
                throw new InvalidOperationException("Area audit did not report aggregate project-tag comparison counts correctly.");

            oracle.Areas[0].To = null;
            report = AreaCatalogAudit.BuildReport(oracle, catalog,
                new HashSet<FogAreaId> { area.Id }, new Dictionary<FogAreaId, string>());
            if (report.Summary.R != 1 || report.Summary.U != 1)
                throw new InvalidOperationException("Area audit did not classify null/empty edge-list representation differences as R.");
            Console.WriteLine($"Area catalog audit self-tests passed ({FogProjectAnnotationPolicy.AreaMapMembershipPolicies.Count + 13} assertions).");
        }
    }
}
