using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FogHost;
using FogMod;
using YamlDotNet.Serialization;

namespace FogReconstruction
{
    public sealed record KeyItemGateUse(string Map, long EventId, int RequiredState, IReadOnlyList<int> EventFlags, IReadOnlyList<string> ActionAnchors, int ShortWarpInstructions, int PlayerWarpInstructions);
    public sealed record KeyItemFactCandidate(
        string SemanticId,
        string EnglishName,
        int Category,
        int GameParamId,
        string Rationale,
        string DefaultLogicalArea,
        string FogTags,
        int ItemLotRowsContainingItem,
        int TreasureLotSources,
        int NpcDropSources,
        int EventAwardSources,
        string OracleIdentityStatus,
        IReadOnlyList<KeyItemGateUse> GateUses);
    public sealed record KeyItemIdentityComparison(int OracleItems, int FactCandidates, int IdentityMatches, int CandidateOnly, int OracleOnly);
    public sealed record KeyItemCandidateAuditReport(
        string Format,
        string Method,
        string Limitation,
        KeyItemIdentityComparison Comparison,
        IReadOnlyList<KeyItemFactCandidate> Candidates);

    /// <summary>Development-only identity inventory from pristine EMEVD possession checks.</summary>
    internal static class KeyItemCandidateAudit
    {
        public const string Format = "ds1tr-fog-key-item-candidates-1";

        public static void Write(string dataRoot, string pristineGameRoot, string areaGateReportPath, string outputPath)
        {
            string output = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot != null && IsWithin(repositoryRoot, output))
                throw new InvalidOperationException("Key-item candidate evidence must be written outside the repository.");
            if (repositoryRoot != null && IsWithin(repositoryRoot, Path.GetFullPath(areaGateReportPath)))
                throw new InvalidOperationException("The source area-gate report must be outside the repository.");

            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Key-item candidates require EMEVD and English FMG facts from the byte-verified clean DSR installation.");
            AreaGateEvidenceAuditReport gates = JsonSerializer.Deserialize<AreaGateEvidenceAuditReport>(File.ReadAllBytes(areaGateReportPath));
            if (gates == null || gates.Format != AreaGateEvidenceAudit.Format)
                throw new InvalidDataException("The supplied area-gate evidence report is missing or uses an unsupported format.");

            var usesByItem = gates.Events
                .SelectMany(evt => evt.ItemChecks
                    .Where(check => check.ItemType == (int)FogGameItemCategory.Goods)
                    .Select(check => (Item: new FogGameItemId(check.ItemType, check.ItemId), Use: new KeyItemGateUse(
                        evt.Map, evt.EventId, check.State, Array.AsReadOnly(evt.EventFlagConditions.ToArray()),
                        Array.AsReadOnly(evt.ActionTargets.Select(target => target.AnchorName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray()),
                        evt.ShortWarpInstructions, evt.PlayerWarpInstructions))))
                .GroupBy(entry => entry.Item)
                .ToDictionary(group => group.Key, group => group.Select(entry => entry.Use).Distinct().OrderBy(use => use.Map, StringComparer.Ordinal).ThenBy(use => use.EventId).ToArray());

            AnnotationData.Annotations oracle;
            using (var reader = File.OpenText(Path.Combine(Path.GetFullPath(dataRoot), "dist", "fog.txt")))
                oracle = new DeserializerBuilder().Build().Deserialize<AnnotationData.Annotations>(reader);
            HashSet<FogGameItemId> oracleIds = (oracle.KeyItems ?? new List<AnnotationData.Item>())
                .Select(item => ParseGameItemId(item.ID))
                .ToHashSet();

            var candidates = new List<KeyItemFactCandidate>();
            foreach (var entry in usesByItem.OrderBy(entry => entry.Key.Category).ThenBy(entry => entry.Key.ItemId))
            {
                FogGameItemId id = entry.Key;
                if (!facts.GameItemNames.TryGetValue(id, out string englishName))
                    throw new InvalidDataException($"Pristine English FMG does not name checked goods item {id}.");
                int[] lots = facts.ItemLots.Values
                    .Where(lot => lot.Items.Any(item => item.Item == id))
                    .Select(lot => lot.Id)
                    .Distinct()
                    .ToArray();
                int treasureSources = facts.ResolvedTreasureLots.Count(treasure => lots.Contains(treasure.ItemLotId));
                int npcSources = facts.NpcDropLots.Count(drop => lots.Contains(drop.ItemLotId));
                int eventAwardSources = facts.ItemLotAwards.Count(award => award.ItemLotId.HasValue && lots.Contains(award.ItemLotId.Value));
                string slug = Regex.Replace(englishName.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
                candidates.Add(new KeyItemFactCandidate(
                    $"{slug}_goods_{id.ItemId}",
                    englishName,
                    id.Category,
                    id.ItemId,
                    "Pristine DSR EMEVD contains an IfPlayerHasItem check for this exact Goods GameParam ID; English FMG supplies its stable displayed identity.",
                    null,
                    null,
                    lots.Length,
                    treasureSources,
                    npcSources,
                    eventAwardSources,
                    oracleIds.Contains(id) ? "identity-match" : "candidate-only-review",
                    Array.AsReadOnly(entry.Value)));
            }
            HashSet<FogGameItemId> candidateIds = candidates.Select(candidate => new FogGameItemId(candidate.Category, candidate.GameParamId)).ToHashSet();
            var comparison = new KeyItemIdentityComparison(
                oracleIds.Count,
                candidates.Count,
                candidateIds.Intersect(oracleIds).Count(),
                candidateIds.Except(oracleIds).Count(),
                oracleIds.Except(candidateIds).Count());
            var report = new KeyItemCandidateAuditReport(
                Format,
                "Indexes Goods-category IfPlayerHasItem checks from the pristine EMEVD area-gate inventory, resolves each exact (category, GameParam ID) to English FMG, and links candidate identities to ItemLotParam, treasure, NPC-drop, and event-award sources. Oracle identity comparison is performed only after the DSR candidate inventory is fixed.",
                "A possession check is evidence of item-dependent game behavior, not by itself evidence that the item belongs in Fog's randomized progression set. Default logical areas and Fog tags remain unresolved because physical item ownership and Fog policy are separate. This is an identity candidate inventory, not a complete KeyItems policy.",
                comparison,
                Array.AsReadOnly(candidates.ToArray()));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Key-item fact candidates: goods-identities={comparison.FactCandidates}, oracle identity matches={comparison.IdentityMatches}, candidate-only={comparison.CandidateOnly}, oracle-only={comparison.OracleOnly}. Report: {output}");
        }

        private static FogGameItemId ParseGameItemId(string value)
        {
            string[] parts = (value ?? string.Empty).Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out int category)
                || !int.TryParse(parts[1], out int itemId))
                throw new InvalidDataException($"Comparison KeyItems contains invalid game item ID '{value}'.");
            return new FogGameItemId(category, itemId);
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
