using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FogHost;

namespace FogReconstruction
{
    public sealed record TransitionFactCandidate(
        string SourceMap,
        string SourceGameMap,
        string SourcePart,
        string SourceCollision,
        string TargetGameMap,
        string TargetMap,
        IReadOnlyList<string> TargetMapCandidates,
        string TargetRegion,
        IReadOnlyList<long> EventIds,
        IReadOnlyList<int> Flags,
        int? ObjActId,
        string InstructionFamily,
        string EvidenceType,
        string Confidence,
        string InterpretationLimit);

    public sealed record TransitionFactAuditReport(
        string Format,
        string Method,
        string Limitation,
        int Maps,
        int ConnectCollisionParts,
        int DecodedTargets,
        int PatternsMatchingSupportedMaps,
        int SupportedMapCandidates,
        int AmbiguousPatterns,
        int TargetsOutsideSupportedMaps,
        int MalformedTargets,
        AreaGateEvidenceAuditReport InteractionEvidence,
        IReadOnlyList<TransitionFactCandidate> Candidates);

    /// <summary>Development-only index of pristine MSB cross-map load metadata; it creates no routes.</summary>
    internal static class TransitionFactAudit
    {
        public const string Format = "ds1tr-fog-transition-facts-1";

        public static void Write(string dataRoot, string pristineGameRoot, string outputPath)
        {
            string output = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot != null && IsWithin(repositoryRoot, output))
                throw new InvalidOperationException("Transition fact evidence must be written outside the repository.");

            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Transition fact evidence requires EMEVD from the byte-verified clean DSR installation.");

            var supportedMaps = facts.Maps.Values
                .Select(map => (Map: map.Id.Value, FileId: TryParseGameMapFileId(map.GameMapFileId)))
                .Where(entry => entry.FileId != null)
                .ToArray();
            var candidates = new List<TransitionFactCandidate>();
            int decoded = 0;
            int matchingPatterns = 0;
            int supportedCandidates = 0;
            int ambiguousPatterns = 0;
            int outside = 0;
            int malformed = 0;
            foreach (FogDsrFacts.Map map in facts.Maps.Values.OrderBy(value => value.Id.Value, StringComparer.Ordinal))
            {
                foreach (FogDsrConnectCollisionFact part in map.ConnectCollisionDetails.OrderBy(value => value.PartName, StringComparer.Ordinal))
                {
                    string targetGameMap = FormatTargetMapFileId(part.TargetMapIdBytes);
                    string targetMap = null;
                    string[] targetMaps = Array.Empty<string>();
                    string confidence;
                    if (targetGameMap == null)
                    {
                        malformed++;
                        confidence = "unresolved-malformed-map-id";
                    }
                    else
                    {
                        decoded++;
                        byte[] targetBytes = part.TargetMapIdBytes.ToArray();
                        targetMaps = supportedMaps
                            .Where(entry => MatchesMapPattern(targetBytes, entry.FileId))
                            .Select(entry => entry.Map)
                            .OrderBy(value => value, StringComparer.Ordinal)
                            .ToArray();
                        if (targetMaps.Length > 0)
                        {
                            matchingPatterns++;
                            supportedCandidates += targetMaps.Length;
                            if (targetMaps.Length == 1)
                            {
                                targetMap = targetMaps[0];
                                confidence = "high-for-map-load-metadata-only";
                            }
                            else
                            {
                                ambiguousPatterns++;
                                confidence = "high-for-map-load-metadata-only; wildcard-matches-multiple-supported-maps";
                            }
                        }
                        else
                        {
                            outside++;
                            confidence = "high-for-map-load-metadata-only; target-outside-index";
                        }
                    }

                    candidates.Add(new TransitionFactCandidate(
                        map.Id.Value,
                        map.GameMapFileId,
                        part.PartName,
                        part.CollisionName,
                        targetGameMap,
                        targetMap,
                        Array.AsReadOnly(targetMaps),
                        null,
                        Array.Empty<long>(),
                        Array.Empty<int>(),
                        null,
                        "MSB1.Part.ConnectCollision.MapID",
                        "pristine-msb-map-load-metadata",
                        confidence,
                        "Establishes only that this collision part declares another map to load. It does not establish player traversal, logical Area endpoints, directionality, or STATIC_AREA_LINK/ENTRANCE/WARP classification."));
                }
            }

            var report = new TransitionFactAuditReport(
                Format,
                "Reads ConnectCollision part name, referenced collision, and the four-byte target map ID from each byte-verified pristine DSR MSB. Target map keys are resolved only against the same supported-map inventory.",
                "ConnectCollision is load metadata, not proof of a traversable route. No proximity, event names, oracle endpoints, or inferred relation types are used.",
                facts.Maps.Count,
                candidates.Count,
                decoded,
                matchingPatterns,
                supportedCandidates,
                ambiguousPatterns,
                outside,
                malformed,
                AreaGateEvidenceAudit.Build(facts, pristineGameRoot),
                Array.AsReadOnly(candidates.ToArray()));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Pristine transition facts: maps={report.Maps}, ConnectCollision={report.ConnectCollisionParts}, decoded-targets={decoded}, patterns-matching-supported-maps={matchingPatterns}, supported-map-candidates={supportedCandidates}, ambiguous-patterns={ambiguousPatterns}, unmatched-patterns={outside}, malformed={malformed}. Report: {output}");
        }

        internal static string FormatTargetMapFileId(IReadOnlyList<byte> mapId)
        {
            if (mapId == null || mapId.Count != 4) return null;
            return $"m{mapId[0]:D2}_{mapId[1]:D2}_{mapId[2]:D2}_{mapId[3]:D2}";
        }

        private static byte[] TryParseGameMapFileId(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId)) return null;
            string[] components = mapId.Trim().Split('_');
            if (components.Length != 4 || components[0].Length < 2 || char.ToLowerInvariant(components[0][0]) != 'm') return null;
            var bytes = new byte[4];
            if (!byte.TryParse(components[0].Substring(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out bytes[0])) return null;
            for (int i = 1; i < components.Length; i++)
                if (!byte.TryParse(components[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out bytes[i])) return null;
            return bytes;
        }

        internal static bool MatchesMapPattern(IReadOnlyList<byte> pattern, IReadOnlyList<byte> mapId)
        {
            if (pattern == null || mapId == null || pattern.Count != 4 || mapId.Count != 4) return false;
            for (int i = 0; i < 4; i++)
                if (pattern[i] != byte.MaxValue && pattern[i] != mapId[i]) return false;
            return true;
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

    internal static class TransitionFactAuditSelfTests
    {
        public static void Run()
        {
            string decoded = TransitionFactAudit.FormatTargetMapFileId(new byte[] { 10, 0, 2, 1 });
            if (decoded != "m10_00_02_01") throw new InvalidOperationException($"ConnectCollision target ID formatted incorrectly: {decoded}.");
            if (!TransitionFactAudit.MatchesMapPattern(new byte[] { 10, 2, 255, 255 }, new byte[] { 10, 2, 0, 0 })
                || TransitionFactAudit.MatchesMapPattern(new byte[] { 10, 3, 255, 255 }, new byte[] { 10, 2, 0, 0 }))
                throw new InvalidOperationException("ConnectCollision wildcard map-ID matching failed.");
            if (TransitionFactAudit.FormatTargetMapFileId(new byte[] { 10, 0, 0 }) != null
                || TransitionFactAudit.FormatTargetMapFileId(null) != null)
                throw new InvalidOperationException("Malformed ConnectCollision target IDs must remain unresolved.");
            Console.WriteLine("Transition fact self-tests passed (2 assertions).");
        }
    }
}
