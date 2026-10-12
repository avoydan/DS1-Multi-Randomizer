using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FogHost;
using SoulsFormats;
using SoulsIds;

namespace FogReconstruction
{
    public sealed record TransitionAnchorReport(string Map, string Kind, string Name, int EntityId);
    public sealed record TransitionEventUseReport(
        string Map,
        string EventArchive,
        string AnchorKind,
        string AnchorName,
        int EntityId,
        long EventId,
        int InstructionIndex,
        int Bank,
        int InstructionId,
        int ArgumentOffset,
        bool Parameterized);
    public sealed record TransitionEventMapSummary(string Map, int CandidateRegions, int CandidateObjects, int EventUses);
    public sealed record TransitionEventAuditReport(
        string Format,
        string Method,
        string Limitation,
        IReadOnlyList<TransitionEventMapSummary> Maps,
        IReadOnlyList<TransitionAnchorReport> Anchors,
        IReadOnlyList<TransitionEventUseReport> EventUses);

    /// <summary>
    /// Development-only cross-reference of name-marked MSB transition candidates and aligned raw
    /// int32 values in pristine map EMEVD. A hit is an evidence lead, not a typed event reference.
    /// </summary>
    public static class TransitionEventAudit
    {
        public const string Format = "ds1tr-fog-transition-event-audit-1";
        private static readonly string[] RegionNameMarkers = { "光の壁", "アクション領域", "霧", "門", "扉" };

        private sealed record Candidate(string Map, string Kind, string Name, int EntityId);

        public static void Write(string dataRoot, string pristineGameRoot, string outputPath)
        {
            string output = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot != null && IsWithin(repositoryRoot, output))
                throw new InvalidOperationException("The transition event audit must be written outside the repository.");

            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            var anchors = new List<TransitionAnchorReport>();
            var uses = new List<TransitionEventUseReport>();
            var summaries = new List<TransitionEventMapSummary>();
            foreach (FogDsrFacts.Map map in facts.Maps.Values.OrderBy(map => map.Id.Value, StringComparer.Ordinal))
            {
                Candidate[] candidates = Discover(map).ToArray();
                anchors.AddRange(candidates.Select(candidate => new TransitionAnchorReport(candidate.Map, candidate.Kind, candidate.Name, candidate.EntityId)));
                var candidatesById = candidates.Where(candidate => candidate.EntityId > 0)
                    .GroupBy(candidate => candidate.EntityId)
                    .ToDictionary(group => group.Key, group => group.ToArray());
                string eventPath = Path.Combine(pristineGameRoot, "event", map.EventArchiveName);
                if (!File.Exists(eventPath)) throw new FileNotFoundException($"Pristine EMEVD for map '{map.Id}' is missing.", eventPath);
                byte[] eventBytes = File.ReadAllBytes(eventPath);
                if (DCX.Is(eventBytes)) eventBytes = DCX.Decompress(eventBytes);
                EMEVD archive = EMEVD.Read(eventBytes);
                int mapUses = 0;
                foreach (EMEVD.Event evt in archive.Events)
                {
                    for (int instructionIndex = 0; instructionIndex < evt.Instructions.Count; instructionIndex++)
                    {
                        EMEVD.Instruction instruction = evt.Instructions[instructionIndex];
                        foreach ((int offset, int value) in FindInt32Windows(instruction.ArgData))
                        {
                            if (!candidatesById.TryGetValue(value, out Candidate[] matches)) continue;
                            bool parameterized = evt.Parameters.Any(parameter => parameter.InstructionIndex == instructionIndex
                                && parameter.TargetStartByte < offset + sizeof(int)
                                && parameter.TargetStartByte + parameter.ByteCount > offset);
                            foreach (Candidate candidate in matches)
                            {
                                uses.Add(new TransitionEventUseReport(map.Id.Value, Path.GetFileName(eventPath), candidate.Kind,
                                    candidate.Name, value, evt.ID, instructionIndex, instruction.Bank, instruction.ID, offset, parameterized));
                                mapUses++;
                            }
                        }
                    }
                }
                summaries.Add(new TransitionEventMapSummary(
                    map.Id.Value,
                    candidates.Count(candidate => candidate.Kind == "region"),
                    candidates.Count(candidate => candidate.Kind == "object"),
                    mapUses));
            }

            var report = new TransitionEventAuditReport(
                Format,
                "Searches every byte offset in pristine map EMEVD instruction arguments for 4-byte windows matching candidate MSB entity IDs; every hit is tagged with its bank/instruction and parameter-binding overlap.",
                "Raw int32 matches are not schema-decoded and may be false positives. Confirm every candidate against the instruction definition and event flow before assigning an entrance, warp, area endpoint, or condition.",
                Array.AsReadOnly(summaries.ToArray()),
                Array.AsReadOnly(anchors.ToArray()),
                Array.AsReadOnly(uses.ToArray()));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Pristine transition-event audit: {summaries.Sum(map => map.CandidateRegions)} candidate regions, {summaries.Sum(map => map.CandidateObjects)} nearby objects, {uses.Count} raw event uses across {summaries.Count} maps. Report: {output}");
        }

        internal static IReadOnlyList<(int Offset, int Value)> FindInt32Windows(byte[] arguments)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            var values = new List<(int Offset, int Value)>();
            for (int offset = 0; offset + sizeof(int) <= arguments.Length; offset++)
                values.Add((offset, BitConverter.ToInt32(arguments, offset)));
            return Array.AsReadOnly(values.ToArray());
        }

        private static IEnumerable<Candidate> Discover(FogDsrFacts.Map map)
        {
            foreach (FogDsrRegionFact region in map.RegionDetails.Where(region => RegionNameMarkers.Any(marker => region.Name.Contains(marker, StringComparison.Ordinal))))
            {
                yield return new Candidate(map.Id.Value, "region", region.Name, region.EntityId);
                foreach (FogDsrObjectFact obj in map.ObjectDetails
                    .Select(candidate => new { Object = candidate, Distance = Distance(region.Transform, candidate.Transform) })
                    .Where(candidate => candidate.Distance <= 20)
                    .OrderBy(candidate => candidate.Distance)
                    .ThenBy(candidate => candidate.Object.Name, StringComparer.Ordinal)
                    .Take(5)
                    .Select(candidate => candidate.Object))
                    yield return new Candidate(map.Id.Value, "object", obj.Name, obj.EntityId);
            }
        }

        private static double Distance(FogDsrTransform left, FogDsrTransform right)
        {
            double x = left.X - right.X;
            double y = left.Y - right.Y;
            double z = left.Z - right.Z;
            return Math.Sqrt(x * x + y * y + z * z);
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

    internal static class TransitionEventAuditSelfTests
    {
        public static void Run()
        {
            byte[] arguments = new byte[12];
            BitConverter.GetBytes(123456).CopyTo(arguments, 0);
            BitConverter.GetBytes(789012).CopyTo(arguments, 4);
            BitConverter.GetBytes(-1).CopyTo(arguments, 8);
            var values = TransitionEventAudit.FindInt32Windows(arguments);
            if (!values.Contains((0, 123456)) || !values.Contains((4, 789012)) || !values.Contains((8, -1))
                || values.Any(value => value.Offset < 0 || value.Offset + sizeof(int) > arguments.Length))
                throw new InvalidOperationException("Transition event audit did not preserve bytewise int32 matches and offsets.");
            Console.WriteLine("Transition event audit self-tests passed (1 assertion).");
        }
    }
}
