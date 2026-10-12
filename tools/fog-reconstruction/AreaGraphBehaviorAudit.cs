using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FogHost;
using FogMod;
using static SoulsIds.GameSpec;
using YamlDotNet.Serialization;
using static FogMod.AnnotationData;

namespace FogReconstruction
{
    public sealed record AreaGraphCaseReport(
        string Profile,
        int Seed,
        string ReferenceConnector,
        string ProjectConnector,
        int ReferenceUnvisitedAreas,
        int ProjectUnvisitedAreas,
        int ReachabilityDifferences,
        int DistanceDifferences,
        int ProgressionPathDifferences,
        int EntrancePairingDifferences,
        string Observation);

    public sealed record AreaGraphBehaviorSummary(
        int Cases,
        int ConnectorFailures,
        int CasesWithReachabilityDifferences,
        int CasesWithDistanceDifferences,
        int CasesWithProgressionPathDifferences,
        int CasesWithEntrancePairingDifferences,
        int ObservationallyEqualCases,
        IReadOnlyList<string> ProjectUnreachableAreaIds);

    public sealed record AreaGraphBehaviorReport(
        string Format,
        string Method,
        AreaGraphBehaviorSummary Summary,
        IReadOnlyList<AreaGraphCaseReport> Cases);

    /// <summary>
    /// Compares runtime graph outcomes while holding all non-Area annotations equal to the
    /// development reference. The output contains aggregate differences only; reference route
    /// endpoints and conditions are never serialized.
    /// </summary>
    public static class AreaGraphBehaviorAudit
    {
        public const string Format = "ds1tr-fog-area-graph-behavior-audit-2";
        private static readonly int[] Seeds = { 1, 17, 42, 1337, 65535 };

        private sealed record Profile(string Name, bool Hard, bool World, bool Boss, bool Major, bool Minor, bool Lordvessel, bool Warp, bool RandomStart);
        private sealed record RunResult(string Error, int ConnectorUnreachableAreas, HashSet<string> Unvisited, Dictionary<string, float> Distances,
            Dictionary<string, HashSet<string>> Paths, string[] EntrancePairings);

        private static readonly Profile[] Profiles =
        {
            new Profile("fixed-default", false, false, false, false, false, false, false, false),
            new Profile("random-world", false, true, true, true, true, true, true, true),
            new Profile("random-hard", true, true, true, true, true, true, true, true),
        };

        public static void Write(string fogDataRoot, string pristineGameDirectory, string outputPath)
        {
            string output = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot != null && IsWithin(repositoryRoot, output))
                throw new InvalidOperationException("The area graph behavior report must be written outside the repository.");

            DataRoot.Verify(fogDataRoot);
            FogDsrFacts facts = FogDsrFacts.Read(fogDataRoot, pristineGameDirectory);
            FogAnnotationCatalog projectAreaCatalog = BuildProjectAreaCatalog(facts);
            var cases = new List<AreaGraphCaseReport>();
            var projectUnreachableAreaIds = new HashSet<string>(StringComparer.Ordinal);
            string originalDirectory = Environment.CurrentDirectory;
            try
            {
                // SoulsIds resolves pinned layout files relative to the application working directory.
                Environment.CurrentDirectory = Path.GetFullPath(fogDataRoot);
                foreach (Profile profile in Profiles)
                {
                    foreach (int seed in Seeds)
                    {
                        Annotations reference = LoadReference(fogDataRoot);
                        Annotations project = LoadReference(fogDataRoot);
                        project.Areas = projectAreaCatalog.ToFogModAnnotations().Areas;
                        RunResult referenceRun = Run(reference, profile, seed, pristineGameDirectory);
                        RunResult projectRun = Run(project, profile, seed, pristineGameDirectory);
                        projectUnreachableAreaIds.UnionWith(projectRun.Unvisited);
                        cases.Add(Compare(profile.Name, seed, referenceRun, projectRun));
                    }
                }
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
            }

            AreaGraphBehaviorSummary summary = new AreaGraphBehaviorSummary(
                cases.Count,
                cases.Count(entry => entry.ReferenceConnector != "PASS" || entry.ProjectConnector != "PASS"),
                cases.Count(entry => entry.ReachabilityDifferences > 0),
                cases.Count(entry => entry.DistanceDifferences > 0),
                cases.Count(entry => entry.ProgressionPathDifferences > 0),
                cases.Count(entry => entry.EntrancePairingDifferences > 0),
                cases.Count(entry => entry.Observation == "NO_DIFFERENCE_OBSERVED"),
                Array.AsReadOnly(projectUnreachableAreaIds.OrderBy(value => value, StringComparer.Ordinal).ToArray()));
            var report = new AreaGraphBehaviorReport(
                Format,
                "For each fixed seed/profile, FogMod Graph and GraphConnector run once with reference Areas and once with the project Area projection; all other annotation categories come from the same pinned development reference. Reachability, GraphChecker distances/path sets, and entrance pairings are compared. This is a bounded observation, not a proof of equivalence and not a source for project route values.",
                summary,
                Array.AsReadOnly(cases.ToArray()));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Area graph behavior audit: cases={summary.Cases}, connectorFailures={summary.ConnectorFailures}, reachabilityDiffCases={summary.CasesWithReachabilityDifferences}, distanceDiffCases={summary.CasesWithDistanceDifferences}, progressionPathDiffCases={summary.CasesWithProgressionPathDifferences}, entrancePairingDiffCases={summary.CasesWithEntrancePairingDifferences}, noDiffCases={summary.ObservationallyEqualCases}. Aggregate report: {output}");
        }

        private static FogAnnotationCatalog BuildProjectAreaCatalog(FogDsrFacts facts)
        {
            IReadOnlyList<FogAreaTagPolicy> tags = FogProjectAnnotationPolicy.ResolveCompleteAreaTagPolicies(facts);
            IReadOnlyList<FogAreaLink> links = FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(
                facts, FogProjectAnnotationPolicy.AreaLinkPolicies, Array.Empty<FogKeyItem>());
            var bySource = FogProjectAnnotationPolicy.AreaLinkPolicies.Select((policy, index) => (policy.Source, Link: links[index]))
                .GroupBy(entry => entry.Source)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<FogAreaLink>)Array.AsReadOnly(group.Select(entry => entry.Link).ToArray()));
            FogArea[] areas = FogProjectAnnotationPolicy.AreaIdentityOrder.Select(id => new FogArea(
                id,
                FogProjectAnnotationPolicy.ResolveAreaProjection(id),
                Array.AsReadOnly(new[] { FogProjectAnnotationPolicy.ResolveAreaMap(id) }),
                tags.Single(policy => policy.Area == id).Tags,
                null,
                bySource.TryGetValue(id, out IReadOnlyList<FogAreaLink> areaLinks) ? areaLinks : Array.Empty<FogAreaLink>())).ToArray();
            return new FogAnnotationCatalog(areas, Array.Empty<FogKeyItem>(), Array.Empty<FogWarp>(), Array.Empty<FogEntrance>(),
                Array.Empty<FogCustomStart>(), Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(),
                Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
        }

        private static Annotations LoadReference(string fogDataRoot)
        {
            using StreamReader reader = File.OpenText(Path.Combine(fogDataRoot, "dist", "fog.txt"));
            Annotations reference = new DeserializerBuilder().Build().Deserialize<Annotations>(reader);
            if (reference == null || reference.Areas == null) throw new InvalidDataException("The development comparison reference has no Area inventory.");
            return reference;
        }

        private static RunResult Run(Annotations annotations, Profile profile, int seed, string pristineGameDirectory)
        {
            var options = new RandomizerOptions { Seed = seed };
            options["hard"] = profile.Hard;
            options["world"] = profile.World;
            options["boss"] = profile.Boss;
            options["major"] = profile.Major;
            options["minor"] = profile.Minor;
            options["lordvessel"] = profile.Lordvessel;
            options["warp"] = profile.Warp;
            options["start"] = profile.RandomStart;

            try
            {
                TextWriter originalOutput = Console.Out;
                Console.SetOut(TextWriter.Null);
                try
                {
                var graph = new Graph();
                graph.Construct(options, annotations);
                new ItemReader().FindItems(options, annotations, graph, pristineGameDirectory, FromGame.DS1R);
                new GraphConnector().Connect(options, graph, annotations);
                GraphChecker.CheckRecord check = new GraphChecker().Check(options, graph, graph.start.Area);
                var paths = check.Records.ToDictionary(entry => entry.Key,
                    entry => entry.Value.Visited.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
                var distances = check.Records.ToDictionary(entry => entry.Key, entry => entry.Value.Dist, StringComparer.Ordinal);
                string[] pairings = graph.graph.Values.SelectMany(node => node.To)
                    .Where(edge => edge.Type == Graph.EdgeType.Exit && edge.Name != null)
                    .Select(edge => $"{edge.Name}>{edge.Link?.Name ?? "<unlinked>"}:{edge.From}>{edge.To ?? "<null>"}")
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                return new RunResult(null, check.Unvisited.Count, check.Unvisited.ToHashSet(StringComparer.Ordinal), distances, paths, pairings);
                }
                finally
                {
                    Console.SetOut(originalOutput);
                }
            }
            catch (Exception exception)
            {
                string[] unreachable = ParseConnectorUnreachableAreas(exception.Message).ToArray();
                return new RunResult(exception.GetType().Name, unreachable.Length, unreachable.ToHashSet(StringComparer.Ordinal),
                    new Dictionary<string, float>(StringComparer.Ordinal), new Dictionary<string, HashSet<string>>(StringComparer.Ordinal), Array.Empty<string>());
            }
        }

        internal static IReadOnlyList<string> ParseConnectorUnreachableAreas(string message)
        {
            const string marker = "Could not find edge into unreachable areas [";
            int start = message?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
            if (start < 0) return Array.Empty<string>();
            start += marker.Length;
            int end = message.IndexOf(']', start);
            if (end < start) return Array.Empty<string>();
            return Array.AsReadOnly(message.Substring(start, end - start)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
        }

        internal static int CountConnectorUnreachableAreas(string message) => ParseConnectorUnreachableAreas(message).Count;

        private static AreaGraphCaseReport Compare(string profile, int seed, RunResult reference, RunResult project)
        {
            int reachability = reference.Error == null && project.Error == null
                ? SymmetricDifference(reference.Unvisited, project.Unvisited)
                : 0;
            var areaIds = reference.Distances.Keys.Union(project.Distances.Keys, StringComparer.Ordinal).ToArray();
            int distance = 0;
            int path = 0;
            foreach (string area in areaIds)
            {
                bool hasReference = reference.Distances.TryGetValue(area, out float referenceDistance);
                bool hasProject = project.Distances.TryGetValue(area, out float projectDistance);
                if (hasReference != hasProject || hasReference && referenceDistance != projectDistance) distance++;
                bool hasReferencePath = reference.Paths.TryGetValue(area, out HashSet<string> referencePath);
                bool hasProjectPath = project.Paths.TryGetValue(area, out HashSet<string> projectPath);
                if (hasReferencePath != hasProjectPath || hasReferencePath && !referencePath.SetEquals(projectPath)) path++;
            }
            int pairing = reference.Error == null && project.Error == null
                ? SymmetricDifference(reference.EntrancePairings, project.EntrancePairings)
                : 0;
            bool connectorPass = reference.Error == null && project.Error == null;
            string observation = !connectorPass ? "CONNECTOR_FAILED"
                : reachability + distance + path + pairing == 0 ? "NO_DIFFERENCE_OBSERVED"
                : "BEHAVIOR_DIFFERENCE_OBSERVED";
            return new AreaGraphCaseReport(profile, seed,
                reference.Error == null ? "PASS" : reference.Error,
                project.Error == null ? "PASS" : project.Error,
                reference.Error == null ? reference.Unvisited.Count : reference.ConnectorUnreachableAreas,
                project.Error == null ? project.Unvisited.Count : project.ConnectorUnreachableAreas,
                reachability,
                distance,
                path,
                pairing,
                observation);
        }

        private static int SymmetricDifference(IEnumerable<string> left, IEnumerable<string> right) =>
            left.Except(right, StringComparer.Ordinal).Count() + right.Except(left, StringComparer.Ordinal).Count();

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

    internal static class AreaGraphBehaviorAuditSelfTests
    {
        public static void Run()
        {
            IReadOnlyList<string> parsed = AreaGraphBehaviorAudit.ParseConnectorUnreachableAreas(
                "Could not find edge into unreachable areas [three, one, two, one] starting from home");
            if (!parsed.SequenceEqual(new[] { "one", "three", "two" }, StringComparer.Ordinal)
                || AreaGraphBehaviorAudit.CountConnectorUnreachableAreas("Could not find edge into unreachable areas [one, two, three] starting from home") != 3
                || AreaGraphBehaviorAudit.CountConnectorUnreachableAreas("Could not find edge into unreachable areas [] starting from home") != 0
                || AreaGraphBehaviorAudit.CountConnectorUnreachableAreas("Could not connect graph") != 0)
                throw new InvalidOperationException("Area graph behavior audit did not count connector-unreachable areas safely.");
            Console.WriteLine("Area graph behavior audit self-tests passed (1 assertion).");
        }
    }
}
