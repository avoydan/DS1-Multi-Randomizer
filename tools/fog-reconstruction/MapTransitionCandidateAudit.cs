using System;
using System.Linq;
using FogHost;

namespace FogReconstruction
{
    /// <summary>Lists pristine MSB region/object evidence that may identify physical transitions.</summary>
    internal static class MapTransitionCandidateAudit
    {
        private static readonly string[] RegionNameMarkers = { "光の壁", "アクション領域", "霧", "門", "扉" };

        public static void Write(string dataRoot, string pristineGameRoot, string selectedMapId = null)
        {
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            FogDsrFacts.Map[] maps = facts.Maps.Values
                .Where(map => selectedMapId == null || string.Equals(map.Id.Value, selectedMapId, StringComparison.Ordinal))
                .OrderBy(map => map.Id.Value, StringComparer.Ordinal)
                .ToArray();
            if (maps.Length == 0) throw new System.Collections.Generic.KeyNotFoundException($"No verified DSR map matches '{selectedMapId ?? "<all>"}'.");
            int regionCount = 0;
            foreach (FogDsrFacts.Map map in maps)
            {
                foreach (FogDsrRegionFact region in map.RegionDetails
                    .Where(region => RegionNameMarkers.Any(marker => region.Name.Contains(marker, StringComparison.Ordinal)))
                    .OrderBy(region => region.EntityId).ThenBy(region => region.Name, StringComparer.Ordinal))
                {
                    regionCount++;
                    Console.WriteLine($"{map.Id.Value} region={region.Name} entity={region.EntityId} shape={FormatShape(region.Shape)} at=({region.Transform.X:F1},{region.Transform.Y:F1},{region.Transform.Z:F1}) yaw={region.Transform.Yaw:F1}");
                    foreach (var candidate in map.ObjectDetails
                        .Select(obj => new { Object = obj, Distance = Distance(region.Transform, obj.Transform) })
                        .Where(candidate => candidate.Distance <= 20)
                        .OrderBy(candidate => candidate.Distance)
                        .ThenBy(candidate => candidate.Object.Name, StringComparer.Ordinal)
                        .Take(5))
                    {
                        FogDsrObjectFact obj = candidate.Object;
                        Console.WriteLine($"  object={obj.Name} entity={obj.EntityId} collision={obj.CollisionName} yaw={obj.Transform.Yaw:F1} distance={candidate.Distance:F1}");
                    }
                }
            }
            Console.WriteLine($"Pristine transition-name candidates: {regionCount} region records across {maps.Length} maps. Names and proximity are evidence only; no area or entrance policy was assigned.");
        }

        private static double Distance(FogDsrTransform left, FogDsrTransform right)
        {
            double x = left.X - right.X;
            double y = left.Y - right.Y;
            double z = left.Z - right.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static string FormatShape(FogDsrRegionShape shape)
        {
            if (shape == null) return "unknown";
            if (shape.Radius.HasValue) return $"{shape.Kind}(r={shape.Radius.Value:F1})";
            if (shape.Width.HasValue || shape.Height.HasValue || shape.Depth.HasValue)
                return $"{shape.Kind}(w={shape.Width?.ToString("F1") ?? "?"},h={shape.Height?.ToString("F1") ?? "?"},d={shape.Depth?.ToString("F1") ?? "?"})";
            return shape.Kind.ToString();
        }
    }
}
