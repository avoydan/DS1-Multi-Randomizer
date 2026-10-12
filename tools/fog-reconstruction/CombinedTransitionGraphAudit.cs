using System;
using System.Collections.Generic;
using System.Linq;
using FogHost;
using FogMod;
using static FogMod.AnnotationData;

namespace FogReconstruction
{
    public sealed record CombinedTransitionProfileComparison(
        string Profile,
        int ReferenceRoutes,
        int ProjectRoutes,
        int NodeDifferences,
        int ReferenceUnlinkedEndpoints,
        int ProjectUnlinkedEndpoints,
        int SharedRoutes,
        int RepresentationOnlyRoutes,
        int RouteDifferences,
        string Classification);

    public sealed record CombinedTransitionGraphComparison(
        string Format,
        string Method,
        int Profiles,
        int EquivalentProfiles,
        int BehaviorChangingProfiles,
        int UnresolvedProfiles,
        IReadOnlyList<CombinedTransitionProfileComparison> Results);

    /// <summary>
    /// Compares fixed-profile traversable routes produced by FogMod.Graph after combining Area.To,
    /// Entrances, and Warps. It deliberately reports aggregates only: reference transition
    /// endpoints and conditions stay in memory for comparison and are never serialized by this audit.
    /// This diagnostic does not establish equivalence for randomized profiles or executable warp data.
    /// </summary>
    public static class CombinedTransitionGraphAudit
    {
        public const string Format = "ds1tr-fog-combined-transition-graph-audit-2";

        private sealed record Profile(string Name, bool Hard);
        private sealed record RouteSet(IReadOnlyDictionary<string, HashSet<string>> Owners,
            IReadOnlySet<string> Nodes, int UnlinkedEndpoints);

        private static readonly Profile[] Profiles =
        {
            new Profile("fixed-default", false),
            new Profile("fixed-hard", true),
        };

        public static CombinedTransitionGraphComparison Compare(Annotations reference, FogAnnotationCatalog project)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (project == null) throw new ArgumentNullException(nameof(project));
            return Compare(reference, project.ToFogModAnnotations());
        }

        public static CombinedTransitionGraphComparison Compare(Annotations reference, Annotations project)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (project == null) throw new ArgumentNullException(nameof(project));

            Annotations projectProjection = project;
            var results = new List<CombinedTransitionProfileComparison>(Profiles.Length);
            foreach (Profile profile in Profiles)
            {
                RouteSet referenceRoutes = null;
                RouteSet projectRoutes = null;
                try
                {
                    referenceRoutes = ConstructRoutes(reference, profile);
                }
                catch
                {
                    results.Add(new CombinedTransitionProfileComparison(
                        profile.Name, 0, 0, 0, 0, 0, 0, 0, 0, "U"));
                    continue;
                }

                try
                {
                    // Hold non-transition annotations equal while replacing the complete
                    // project-owned Areas/Entrances/Warps projection under comparison.
                    Annotations effectiveProject = CloneGraphInputs(reference);
                    effectiveProject.Areas = CloneAreas(projectProjection.Areas);
                    effectiveProject.Entrances = CloneEntrances(projectProjection.Entrances);
                    effectiveProject.Warps = CloneEntrances(projectProjection.Warps);
                    projectRoutes = ConstructRoutes(effectiveProject, profile);
                }
                catch
                {
                    results.Add(new CombinedTransitionProfileComparison(
                        profile.Name, referenceRoutes.Owners.Count, 0, 0,
                        referenceRoutes.UnlinkedEndpoints, 0, 0, 0, 0, "U"));
                    continue;
                }

                string[] referenceKeys = referenceRoutes.Owners.Keys.ToArray();
                string[] projectKeys = projectRoutes.Owners.Keys.ToArray();
                int shared = referenceKeys.Intersect(projectKeys, StringComparer.Ordinal).Count();
                int differences = referenceKeys.Except(projectKeys, StringComparer.Ordinal).Count()
                    + projectKeys.Except(referenceKeys, StringComparer.Ordinal).Count();
                int representationOnly = referenceKeys.Intersect(projectKeys, StringComparer.Ordinal)
                    .Count(key => !referenceRoutes.Owners[key].SetEquals(projectRoutes.Owners[key]));
                int nodeDifferences = referenceRoutes.Nodes.Except(projectRoutes.Nodes, StringComparer.Ordinal).Count()
                    + projectRoutes.Nodes.Except(referenceRoutes.Nodes, StringComparer.Ordinal).Count();

                // A route can move between Area.To, Entrance, and Warp while preserving the
                // actual graph behavior. Such a category change is E by design.
                // An unmatched endpoint or missing/extra node makes the comparison incomplete,
                // even if the remaining route set happens to match.
                string classification = nodeDifferences > 0 || referenceRoutes.UnlinkedEndpoints > 0
                    || projectRoutes.UnlinkedEndpoints > 0 ? "U"
                    : differences == 0 ? "E" : "B";
                results.Add(new CombinedTransitionProfileComparison(
                    profile.Name,
                    referenceKeys.Length,
                    projectKeys.Length,
                    nodeDifferences,
                    referenceRoutes.UnlinkedEndpoints,
                    projectRoutes.UnlinkedEndpoints,
                    shared,
                    representationOnly,
                    differences,
                    classification));
            }

            return new CombinedTransitionGraphComparison(
                Format,
                "Fixed default and hard profiles compare FogMod.Graph routes using LinkedExpr (the condition actually evaluated by GraphChecker). Route category and annotation identity are ignored so equivalent Area.To/Entrance/Warp representations compare as E. Node inventory differences or unlinked named endpoints classify U. This is not evidence for randomized profiles or executable warp targets; those require separate behavioral and pristine-data validation. Oracle values are comparison-only and are not emitted.",
                results.Count,
                results.Count(result => result.Classification == "E"),
                results.Count(result => result.Classification == "B"),
                results.Count(result => result.Classification == "U"),
                Array.AsReadOnly(results.ToArray()));
        }

        private static RouteSet ConstructRoutes(Annotations source, Profile profile)
        {
            Annotations annotations = CloneGraphInputs(source);
            var options = new RandomizerOptions { Seed = 1 };
            options["hard"] = profile.Hard;
            options["world"] = false;
            options["boss"] = false;
            options["major"] = false;
            options["minor"] = false;
            options["lordvessel"] = false;
            options["warp"] = false;
            options["unconnected"] = false;

            var graph = new Graph();
            graph.Construct(options, annotations);
            var routes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (Graph.Edge edge in graph.graph.Values.SelectMany(node => node.To)
                .Where(edge => edge.Type == Graph.EdgeType.Exit && edge.From != null && edge.To != null))
            {
                string condition = edge.LinkedExpr?.ToString() ?? "true";
                string key = RouteKey(edge.From, edge.To, condition);
                if (!routes.TryGetValue(key, out HashSet<string> owners))
                {
                    owners = new HashSet<string>(StringComparer.Ordinal);
                    routes.Add(key, owners);
                }
                owners.Add(edge.Name == null ? "Area.To" :
                    annotations.Warps.Any(warp => string.Equals(warp.EdgeName, edge.Name, StringComparison.Ordinal))
                        ? "Warp" : "Entrance");
            }
            int unlinkedEndpoints = graph.graph.Values.SelectMany(node => node.To)
                .Count(edge => edge.Type == Graph.EdgeType.Exit && edge.Name != null
                    && (edge.To == null || edge.Link == null));
            return new RouteSet(routes,
                new HashSet<string>(graph.graph.Keys, StringComparer.Ordinal), unlinkedEndpoints);
        }

        private static string RouteKey(string from, string to, string condition) =>
            string.Concat(from.Length, ":", from, to.Length, ":", to, condition.Length, ":", condition);

        private static Annotations CloneGraphInputs(Annotations source) => new Annotations
        {
            Areas = CloneAreas(source.Areas),
            KeyItems = (source.KeyItems ?? new List<Item>())
                .Where(item => item != null)
                .Select(item => new Item { Name = item.Name })
                .ToList(),
            Entrances = CloneEntrances(source.Entrances),
            Warps = CloneEntrances(source.Warps),
        };

        private static List<Area> CloneAreas(IEnumerable<Area> source) => (source ?? Enumerable.Empty<Area>())
            .Where(area => area != null)
            .Select(area => new Area
            {
                Name = area.Name,
                Tags = area.Tags,
                ScalingBase = area.ScalingBase,
                To = area.To?.Where(side => side != null).Select(CloneSide).ToList(),
            })
            .ToList();

        private static List<Entrance> CloneEntrances(IEnumerable<Entrance> source) => (source ?? Enumerable.Empty<Entrance>())
            .Where(entrance => entrance != null)
            .Select(entrance => new Entrance
            {
                Name = entrance.Name,
                ID = entrance.ID,
                Area = entrance.Area,
                Tags = entrance.Tags,
                Text = entrance.Text,
                DoorCond = entrance.DoorCond,
                ASide = entrance.ASide == null ? null : CloneSide(entrance.ASide),
                BSide = entrance.BSide == null ? null : CloneSide(entrance.BSide),
            })
            .ToList();

        private static Side CloneSide(Side side) => new Side
        {
            Area = side.Area,
            Tags = side.Tags,
            Cond = side.Cond,
            ExcludeIfRandomized = side.ExcludeIfRandomized,
        };
    }

    internal static class CombinedTransitionGraphAuditSelfTests
    {
        public static void Run()
        {
            FogAnnotationCatalog projectDoor = Catalog(
                new[]
                {
                    Area("a", Array.Empty<FogAreaLink>()),
                    Area("b", Array.Empty<FogAreaLink>()),
                },
                new[]
                {
                    new FogEntrance("door", 1, new FogMapId("parish"), "door", null, FogEntranceTags.Door,
                        Side("a"), Side("b")),
                });
            Annotations reciprocalReference = Reference(
                new Area { Name = "a", To = new List<Side> { new Side { Area = "b" } } },
                new Area { Name = "b", To = new List<Side> { new Side { Area = "a" } } });
            CombinedTransitionGraphComparison equivalent = CombinedTransitionGraphAudit.Compare(reciprocalReference, projectDoor);
            if (equivalent.EquivalentProfiles != 2 || equivalent.BehaviorChangingProfiles != 0 || equivalent.UnresolvedProfiles != 0)
                throw new InvalidOperationException("A fixed bidirectional Entrance must compare behavior-equivalent to reciprocal static Area.To routes.");
            if (equivalent.Results.Any(result => result.RepresentationOnlyRoutes != 2))
                throw new InvalidOperationException("The combined graph audit must identify both equivalent route-category changes without treating them as behavior changes.");

            Annotations oneWayReference = Reference(
                new Area { Name = "a", To = new List<Side> { new Side { Area = "b" } } },
                new Area { Name = "b", To = new List<Side>() });
            CombinedTransitionGraphComparison changed = CombinedTransitionGraphAudit.Compare(oneWayReference, projectDoor);
            if (changed.BehaviorChangingProfiles != 2 || changed.EquivalentProfiles != 0 || changed.UnresolvedProfiles != 0)
                throw new InvalidOperationException("A bidirectional Entrance must not compare equivalent to a one-way static route.");

            FogAnnotationCatalog projectWarp = new FogAnnotationCatalog(
                new[] { Area("a", Array.Empty<FogAreaLink>()), Area("b", Array.Empty<FogAreaLink>()) },
                Array.Empty<FogKeyItem>(),
                new[]
                {
                    new FogWarp("warp", 2, new FogMapId("parish"), "warp", FogWarpTags.None,
                        WarpSide("a"), WarpSide("b")),
                },
                Array.Empty<FogEntrance>(), Array.Empty<FogCustomStart>(), Array.Empty<FogDefaultCost>(),
                Array.Empty<FogEnemyScalingRule>(), Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
            CombinedTransitionGraphComparison warpEquivalent = CombinedTransitionGraphAudit.Compare(oneWayReference, projectWarp);
            if (warpEquivalent.EquivalentProfiles != 2 || warpEquivalent.BehaviorChangingProfiles != 0
                || warpEquivalent.Results.Any(result => result.RepresentationOnlyRoutes != 1))
                throw new InvalidOperationException("A fixed Warp must participate in the combined graph as an equivalent directed route.");

            FogCondition sideA = Condition("a");
            FogCondition sideB = Condition("b");
            Annotations conditionalReference = Reference(
                new Area { Name = "a", To = new List<Side> { new Side { Area = "b", Cond = "AND b a" } } },
                new Area { Name = "b", To = new List<Side> { new Side { Area = "a", Cond = "AND b a" } } });
            FogAnnotationCatalog conditionalEntrance = Catalog(
                new[] { Area("a", Array.Empty<FogAreaLink>()), Area("b", Array.Empty<FogAreaLink>()) },
                new[]
                {
                    new FogEntrance("conditional-door", 3, new FogMapId("parish"), "conditional door", null,
                        FogEntranceTags.Boss, Side("a", sideA), Side("b", sideB)),
                });
            CombinedTransitionGraphComparison conditionalDoorResult = CombinedTransitionGraphAudit.Compare(conditionalReference, conditionalEntrance);
            if (conditionalDoorResult.EquivalentProfiles != 2 || conditionalDoorResult.UnresolvedProfiles != 0)
                throw new InvalidOperationException($"A fixed Entrance must compare using both endpoint conditions combined in LinkedExpr (E={conditionalDoorResult.EquivalentProfiles}, B={conditionalDoorResult.BehaviorChangingProfiles}, U={conditionalDoorResult.UnresolvedProfiles}, routes={string.Join(",", conditionalDoorResult.Results.Select(result => $"{result.ReferenceRoutes}/{result.ProjectRoutes}/diff{result.RouteDifferences}/unlinked{result.ProjectUnlinkedEndpoints}"))}).");

            FogAnnotationCatalog conditionalWarp = new FogAnnotationCatalog(
                new[] { Area("a", Array.Empty<FogAreaLink>()), Area("b", Array.Empty<FogAreaLink>()) },
                Array.Empty<FogKeyItem>(),
                new[]
                {
                    new FogWarp("conditional-warp", 4, new FogMapId("parish"), "conditional warp", FogWarpTags.None,
                        WarpSide("a", sideA), WarpSide("b", sideB)),
                },
                Array.Empty<FogEntrance>(), Array.Empty<FogCustomStart>(), Array.Empty<FogDefaultCost>(),
                Array.Empty<FogEnemyScalingRule>(), Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
            Annotations conditionalWarpReference = Reference(
                new Area { Name = "a", To = new List<Side> { new Side { Area = "b", Cond = "AND a b" } } },
                new Area { Name = "b", To = new List<Side>() });
            CombinedTransitionGraphComparison conditionalWarpResult = CombinedTransitionGraphAudit.Compare(conditionalWarpReference, conditionalWarp);
            if (conditionalWarpResult.EquivalentProfiles != 2 || conditionalWarpResult.UnresolvedProfiles != 0)
                throw new InvalidOperationException($"A fixed Warp must compare using the destination-side condition in LinkedExpr (E={conditionalWarpResult.EquivalentProfiles}, B={conditionalWarpResult.BehaviorChangingProfiles}, U={conditionalWarpResult.UnresolvedProfiles}, routes={string.Join(",", conditionalWarpResult.Results.Select(result => $"{result.ReferenceRoutes}/{result.ProjectRoutes}/diff{result.RouteDifferences}/unlinked{result.ProjectUnlinkedEndpoints}"))}).");

            FogAnnotationCatalog dnoftsDoor = Catalog(
                new[] { Area("a", Array.Empty<FogAreaLink>()), Area("b", Array.Empty<FogAreaLink>()) },
                new[]
                {
                    new FogEntrance("dnofts-door", 5, new FogMapId("parish"), "dnofts door", null,
                        FogEntranceTags.Door, Side("a", null, FogEntranceSideTags.Dnofts), Side("b")),
                });
            Annotations dnoftsReference = Reference(
                new Area { Name = "a", To = new List<Side> { new Side { Area = "b", Cond = "b" } } },
                new Area { Name = "b", To = new List<Side> { new Side { Area = "a", Cond = "b" } } });
            CombinedTransitionGraphComparison dnoftsResult = CombinedTransitionGraphAudit.Compare(dnoftsReference, dnoftsDoor);
            if (dnoftsResult.EquivalentProfiles != 2 || dnoftsResult.UnresolvedProfiles != 0)
                throw new InvalidOperationException("A dnofts Door route must compare using the paired LinkedExpr copied to both directions.");

            Annotations missingNodeReference = Reference(
                new Area { Name = "a", To = new List<Side>() },
                new Area { Name = "b", To = new List<Side>() },
                new Area { Name = "isolated", To = new List<Side>() });
            CombinedTransitionGraphComparison missingNode = CombinedTransitionGraphAudit.Compare(missingNodeReference, projectDoor);
            if (missingNode.UnresolvedProfiles != 2 || missingNode.Results.Any(result => result.NodeDifferences != 1))
                throw new InvalidOperationException("The fixed graph audit must detect a missing isolated node even when route sets match.");

            FogAnnotationCatalog unclassifiedEntrance = Catalog(
                new[] { Area("a", Array.Empty<FogAreaLink>()), Area("b", Array.Empty<FogAreaLink>()) },
                new[]
                {
                    new FogEntrance("unclassified", 6, new FogMapId("parish"), "unclassified", null,
                        FogEntranceTags.None, Side("a"), Side("b")),
                });
            CombinedTransitionGraphComparison unlinked = CombinedTransitionGraphAudit.Compare(
                Reference(new Area { Name = "a", To = new List<Side>() }, new Area { Name = "b", To = new List<Side>() }),
                unclassifiedEntrance);
            if (unlinked.UnresolvedProfiles != 2 || unlinked.Results.Any(result => result.ProjectUnlinkedEndpoints != 2))
                throw new InvalidOperationException("A fixed profile with unclassified unlinked entrance endpoints must be unresolved.");
        }

        private static FogAnnotationCatalog Catalog(IEnumerable<FogArea> areas, IEnumerable<FogEntrance> entrances) =>
            new FogAnnotationCatalog(areas, Array.Empty<FogKeyItem>(), Array.Empty<FogWarp>(), entrances,
                Array.Empty<FogCustomStart>(), Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(),
                Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());

        private static FogArea Area(string id, IReadOnlyList<FogAreaLink> links) => new FogArea(
            new FogAreaId(id), new FogModAreaName(id), new[] { new FogMapId("parish") }, FogAreaTags.None, null, links);

        private static FogEntranceSide Side(string area, FogCondition condition = null, FogEntranceSideTags tags = FogEntranceSideTags.None) => new FogEntranceSide(
            new FogAreaId(area), 0, 0, 0, 0, 0, null, 0, condition, null, 0, 0, null, null, tags);

        private static FogWarpSide WarpSide(string area, FogCondition condition = null) => new FogWarpSide(
            new FogAreaId(area), 0, condition, null, null, FogWarpSideTags.None);

        private static FogCondition Condition(string area) => new FogCondition(FogConditionKind.Atom,
            Array.AsReadOnly(new[] { FogConditionReference.Area(new FogAreaId(area)) }));

        private static Annotations Reference(params Area[] areas) => new Annotations
        {
            Areas = areas.ToList(),
            KeyItems = new List<Item>(),
            Entrances = new List<Entrance>(),
            Warps = new List<Entrance>(),
        };
    }
}
