using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FogHost;

namespace FogReconstruction
{
    public sealed record BossDefeatNearbyAnchor(
        string Kind,
        string Name,
        int EntityId,
        string CollisionName,
        float Distance,
        FogDsrTransform Transform);

    public sealed record BossDefeatEvidenceRow(
        string Map,
        long EventId,
        int EnemyEntityId,
        int EnemyMatches,
        string Resolution,
        IReadOnlyList<string> OtherAnchorMatches,
        string EnemyPart,
        string Collision,
        float? X,
        float? Y,
        float? Z,
        bool CommonBossRewardFlagRead,
        IReadOnlyList<BossDefeatNearbyAnchor> NearbyAnchors);

    public sealed record BossDefeatEvidenceReport(
        string Format,
        string Method,
        int TotalHandlers,
        int UniqueEnemyAnchors,
        int UnresolvedEnemyAnchors,
        IReadOnlyList<BossDefeatEvidenceRow> Handlers);

    /// <summary>Development-only inventory of pristine boss event/enemy/collision anchors; it assigns no area policy.</summary>
    public static class BossDefeatEvidenceAudit
    {
        public const string Format = "ds1tr-fog-boss-defeat-evidence-2";

        public static void Write(string dataRoot, string pristineGameRoot, string outputPath)
        {
            string output = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot != null && IsWithin(repositoryRoot, output))
                throw new InvalidOperationException("Boss evidence report must be written outside the repository.");

            DataRoot.Verify(dataRoot);
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Boss area evidence requires EMEVD facts from the byte-verified clean DSR installation.");

            var rows = new List<BossDefeatEvidenceRow>();
            foreach (FogDsrFacts.Map map in facts.Maps.Values.OrderBy(value => value.Id.Value, StringComparer.Ordinal))
            {
                foreach (FogDsrBossDefeatFact handler in map.BossDefeatHandlers.OrderBy(value => value.EventId))
                {
                    FogDsrEnemyFact[] enemies = map.EnemyDetails.Where(value => value.EntityId == handler.EnemyEntityId).ToArray();
                    FogDsrEnemyFact enemy = enemies.Length == 1 ? enemies[0] : null;
                    string resolution = enemies.Length == 1
                        ? "MATCHED_UNIQUE_ENEMY"
                        : enemies.Length == 0 ? "NO_MSB_ENEMY_MATCH" : "AMBIGUOUS_MSB_ENEMY_MATCH";
                    string[] otherAnchors = map.ObjectDetails
                        .Where(value => value.EntityId == handler.EnemyEntityId)
                        .Select(value => "object:" + value.Name)
                        .Concat(map.RegionDetails.Where(value => value.EntityId == handler.EnemyEntityId)
                            .Select(value => "region:" + value.Name))
                        .ToArray();
                    rows.Add(new BossDefeatEvidenceRow(map.Id.Value, handler.EventId, handler.EnemyEntityId,
                        enemies.Length, resolution, Array.AsReadOnly(otherAnchors), enemy?.Name, enemy?.CollisionName,
                        enemy?.Transform.X, enemy?.Transform.Y, enemy?.Transform.Z,
                        handler.EventId <= int.MaxValue && facts.BossRewardCompletionFlags.Contains((int)handler.EventId),
                        enemy == null ? Array.Empty<BossDefeatNearbyAnchor>() : BuildNearbyAnchors(map, enemy.Transform)));
                }
            }

            var report = new BossDefeatEvidenceReport(
                Format,
                "Reads only byte-verified pristine map/MSB and EMEVD facts; boss event rows are evidence candidates, not area tags or graph edges.",
                rows.Count,
                rows.Count(row => row.Resolution == "MATCHED_UNIQUE_ENEMY"),
                rows.Count(row => row.Resolution != "MATCHED_UNIQUE_ENEMY"),
                Array.AsReadOnly(rows.ToArray()));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static IReadOnlyList<BossDefeatNearbyAnchor> BuildNearbyAnchors(FogDsrFacts.Map map, FogDsrTransform center)
        {
            const float radius = 75f;
            var objects = map.ObjectDetails.Where(value => value.EntityId > 0).Select(value => new
            {
                Anchor = new BossDefeatNearbyAnchor("object", value.Name, value.EntityId, value.CollisionName,
                    Distance(center, value.Transform), value.Transform),
            });
            var regions = map.RegionDetails.Where(value => value.EntityId > 0).Select(value => new
            {
                Anchor = new BossDefeatNearbyAnchor("region", value.Name, value.EntityId, null,
                    Distance(center, value.Transform), value.Transform),
            });
            return Array.AsReadOnly(objects.Concat(regions)
                .Where(value => value.Anchor.Distance <= radius)
                .OrderBy(value => value.Anchor.Distance)
                .ThenBy(value => value.Anchor.Kind, StringComparer.Ordinal)
                .ThenBy(value => value.Anchor.Name, StringComparer.Ordinal)
                .Take(40)
                .Select(value => value.Anchor)
                .ToArray());
        }

        private static float Distance(FogDsrTransform left, FogDsrTransform right)
        {
            float x = left.X - right.X;
            float y = left.Y - right.Y;
            float z = left.Z - right.Z;
            return MathF.Sqrt(x * x + y * y + z * z);
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
}
