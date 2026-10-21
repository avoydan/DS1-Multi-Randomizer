using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FogHost;
using FogMod;
using YamlDotNet.Serialization;

namespace FogReconstruction
{
    public sealed record DefaultFlagCandidate(
        string Map,
        string Collision,
        int PlayRegionId,
        IReadOnlyList<int> EnemyEntityIds,
        IReadOnlyList<long> HandleBossDefeatEventIds,
        IReadOnlyList<int> Event970CompletionFlagCandidates,
        IReadOnlyList<string> EventFlowEvidence,
        IReadOnlyList<string> SameStemEncodedFlagAnchors,
        int? ReferenceFlag,
        string Resolution,
        string OracleClass);

    public sealed record DefaultFlagComparison(int ReferenceRows, int ReferenceActiveRows, int ProjectUniqueCandidates, int Exact, int BehaviorDifferent, int Unresolved, int Unused);

    public sealed record DefaultFlagCandidateReport(
        string Format,
        string Rule,
        string Limitation,
        int Maps,
        int EligibleLowPlayRegionCollisions,
        DefaultFlagComparison Comparison,
        IReadOnlyList<DefaultFlagCandidate> Candidates);

    /// <summary>Development-only discovery and post-derivation comparison for boss completion collision flags.</summary>
    internal static class DefaultFlagCandidateAudit
    {
        public const string Format = "ds1tr-fog-default-flag-candidate-audit-1";

        private sealed record ReferenceFlag(string Map, string Collision, int Flag);

        public static void Write(string dataRoot, string pristineGameRoot, string outputPath)
        {
            string output = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot != null && IsWithin(repositoryRoot, output))
                throw new InvalidOperationException("DefaultFlagCols evidence must be written outside the repository.");

            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("DefaultFlagCols discovery requires EMEVD from the byte-verified clean DSR installation.");

            var oracle = ReadReference(dataRoot);
            HashSet<int> bossEventIds = facts.Maps.Values
                .SelectMany(map => map.BossDefeatHandlers)
                .Where(handler => handler.EventId > 0 && handler.EventId <= int.MaxValue)
                .Select(handler => (int)handler.EventId)
                .ToHashSet();
            var candidates = new List<DefaultFlagCandidate>();
            int eligible = 0;
            foreach (FogDsrFacts.Map map in facts.Maps.Values.OrderBy(value => value.Id.Value, StringComparer.Ordinal))
            {
                foreach (FogDsrCollisionFact collision in map.CollisionDetails.Where(value => value.PlayRegionId < 10).OrderBy(value => value.Name, StringComparer.Ordinal))
                {
                    eligible++;
                    FogDsrEnemyFact[] enemies = map.EnemyDetails
                        .Where(enemy => enemy.EntityId > 0 && string.Equals(enemy.CollisionName, collision.Name, StringComparison.Ordinal))
                        .ToArray();
                    int[] enemyIds = enemies.Select(enemy => enemy.EntityId).Distinct().OrderBy(id => id).ToArray();
                    long[] bossEvents = map.BossDefeatHandlers
                        .Where(handler => enemyIds.Contains(handler.EnemyEntityId))
                        .Select(handler => handler.EventId)
                        .Where(eventId => eventId > 0)
                        .Distinct()
                        .OrderBy(eventId => eventId)
                        .ToArray();
                    int[] directFlags = bossEvents
                        .Where(eventId => eventId <= int.MaxValue && facts.BossRewardCompletionFlags.Contains((int)eventId))
                        .Select(eventId => (int)eventId)
                        .Distinct()
                        .OrderBy(flag => flag)
                        .ToArray();
                    long encodedEventId = -(long)collision.PlayRegionId - 10;
                    int[] playRegionFlags = encodedEventId > 0 && encodedEventId <= int.MaxValue
                        && facts.BossRewardCompletionFlags.Contains((int)encodedEventId)
                        && map.BossDefeatHandlers.Any(handler => handler.EventId == encodedEventId)
                            ? new[] { (int)encodedEventId }
                            : Array.Empty<int>();
                    int[] flags = directFlags.Concat(playRegionFlags).Distinct().OrderBy(flag => flag).ToArray();
                    string[] eventFlowEvidence = new[]
                    {
                        directFlags.Length == 1 ? "collision-enemy-to-HandleBossDefeat-to-common-970" : null,
                        playRegionFlags.Length == 1 ? "PlayRegionID-minus-10-matches-same-map-HandleBossDefeat-event-and-common-970" : null,
                    }.Where(value => value != null).ToArray();
                    string stem = CollisionStem(collision.Name);
                    string[] stemFlagAnchors = map.CollisionDetails
                        .Where(other => CollisionStem(other.Name) == stem && other.PlayRegionId <= -10)
                        .Select(other => (Collision: other, Flag: -(long)other.PlayRegionId - 10))
                        .Where(entry => entry.Flag > 0 && entry.Flag <= int.MaxValue
                            && facts.BossRewardCompletionFlags.Contains((int)entry.Flag)
                            && bossEventIds.Contains((int)entry.Flag))
                        .Select(entry => $"{entry.Collision.Name}->{entry.Flag}")
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray();
                    string resolution = flags.Length switch
                    {
                        0 => "no-unique-event970-completion-flag",
                        1 => "unique-event970-completion-flag",
                        _ => "ambiguous-multiple-event970-completion-flags",
                    };
                    string key = map.Id.Value + "_" + collision.Name;
                    bool hasReference = oracle.TryGetValue(key, out ReferenceFlag referenceFlag);
                    bool writerUnused = map.Id == new FogMapId("firelink") && collision.Name == "h0017B2_0000";
                    string classification = writerUnused && hasReference
                        ? "UNUSED"
                        : flags.Length != 1
                            ? hasReference ? "U" : "UNCLASSIFIED"
                            : hasReference
                                ? referenceFlag.Flag == flags[0] ? "E" : "B"
                                : "B";
                    candidates.Add(new DefaultFlagCandidate(
                        map.Id.Value,
                        collision.Name,
                        collision.PlayRegionId,
                        Array.AsReadOnly(enemyIds),
                        Array.AsReadOnly(bossEvents),
                        Array.AsReadOnly(flags),
                        Array.AsReadOnly(eventFlowEvidence),
                        Array.AsReadOnly(stemFlagAnchors),
                        hasReference ? referenceFlag.Flag : null,
                        resolution,
                        classification));
                }
            }

            var activeReference = oracle.Values
                .Where(row => !(row.Map == "firelink" && row.Collision == "h0017B2_0000"))
                .ToArray();
            var resolvedKeys = candidates.Where(row => row.Event970CompletionFlagCandidates.Count == 1)
                .Select(row => row.Map + "_" + row.Collision).ToHashSet(StringComparer.Ordinal);
            var comparison = new DefaultFlagComparison(
                oracle.Count,
                activeReference.Length,
                candidates.Count(row => row.Event970CompletionFlagCandidates.Count == 1),
                candidates.Count(row => row.OracleClass == "E"),
                candidates.Count(row => row.OracleClass == "B"),
                activeReference.Count(row => !resolvedKeys.Contains(row.Map + "_" + row.Collision)),
                oracle.Count - activeReference.Length);
            var report = new DefaultFlagCandidateReport(
                Format,
                "For each DSR collision with PlayRegionID < 10, derive candidates from either (1) same-collision enemy entity -> HandleBossDefeat event, or (2) an encoded negative PlayRegionID whose decoded value is a same-map HandleBossDefeat event ID; retain only IDs common event 0 passes to event 970 and event 970 consumes as an event flag. These facts are derived before the reference comparison.",
                "A unique event-flow candidate is not by itself proof that every collision should receive a runtime annotation; collision eligibility and active writer semantics still require review. Parameterized/alternate completion paths and conflicting flags remain unresolved. No production policy/provider is changed.",
                facts.Maps.Count,
                eligible,
                comparison,
                Array.AsReadOnly(candidates.ToArray()));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"DefaultFlagCols fact candidates: eligible-collisions={eligible}, unique-project-candidates={comparison.ProjectUniqueCandidates}, E={comparison.Exact}, B={comparison.BehaviorDifferent}, U={comparison.Unresolved}, unused={comparison.Unused}. Report: {output}");
        }

        private static Dictionary<string, ReferenceFlag> ReadReference(string dataRoot)
        {
            string path = Path.Combine(Path.GetFullPath(dataRoot), "dist", "fog.txt");
            AnnotationData.Annotations annotations;
            using (var reader = File.OpenText(path)) annotations = new DeserializerBuilder().Build().Deserialize<AnnotationData.Annotations>(reader);
            var output = new Dictionary<string, ReferenceFlag>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> entry in annotations.DefaultFlagCols ?? new Dictionary<string, int>())
            {
                string map = AnnotationData.nameSpecs.Keys
                    .Where(key => entry.Key.StartsWith(key + "_", StringComparison.Ordinal))
                    .OrderByDescending(key => key.Length)
                    .FirstOrDefault();
                if (map == null) throw new InvalidDataException($"Comparison reference key '{entry.Key}' has no supported map prefix.");
                string collision = entry.Key.Substring(map.Length + 1);
                if (!output.TryAdd(entry.Key, new ReferenceFlag(map, collision, entry.Value)))
                    throw new InvalidDataException($"Comparison reference repeats DefaultFlagCols key '{entry.Key}'.");
            }
            return output;
        }

        private static string CollisionStem(string name)
        {
            int separator = (name ?? string.Empty).LastIndexOf('_');
            if (separator < 0 || name.Length - separator != 5) return name ?? string.Empty;
            for (int i = separator + 1; i < name.Length; i++)
                if (!char.IsDigit(name[i])) return name;
            return name.Substring(0, separator);
        }

        private static string FindRepositoryRoot(string path)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }

        private static bool IsWithin(string root, string path)
        {
            string relative = Path.GetRelativePath(root, path);
            return !Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        }
    }
}
