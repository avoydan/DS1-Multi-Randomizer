using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FogHost;

namespace FogReconstruction
{
    /// <summary>Read-only spatial evidence from pristine MSB facts; it does not assign area policy.</summary>
    internal static class CustomStartSpatialAudit
    {
        public static void Write(string dataRoot, string pristineGameRoot, string selectedStartId = null)
        {
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            FogCustomStart[] starts = FogAnnotationPolicyResolver.ResolveCustomStarts(facts, FogProjectAnnotationPolicy.CustomStarts)
                .Where(start => selectedStartId == null || string.Equals(start.Id, selectedStartId, StringComparison.Ordinal))
                .ToArray();
            if (starts.Length == 0) throw new KeyNotFoundException($"No project custom start matches '{selectedStartId ?? "<all>"}'.");
            foreach (FogCustomStart start in starts)
            {
                FogCustomStartPolicy policy = FogProjectAnnotationPolicy.CustomStarts.Single(item => item.Id == start.Id);
                FogDsrFacts.Map map = facts.Maps[start.Respawn.Map];
                string spawnRegionName = map.SpawnPoints[start.Respawn.SpawnPoint.Value];
                FogDsrRegionFact spawn = map.RegionDetails.Single(region => string.Equals(region.Name, spawnRegionName, StringComparison.Ordinal));
                var nearbyBosses = map.BossDefeatHandlers
                    .Select(handler => new { Handler = handler, Enemy = map.EnemyDetails.SingleOrDefault(enemy => enemy.EntityId == handler.EnemyEntityId) })
                    .Where(pair => pair.Enemy != null)
                    .Select(pair => new
                    {
                        pair.Handler.EventId,
                        pair.Enemy.Name,
                        pair.Enemy.CollisionName,
                        Distance = Distance(spawn.Transform, pair.Enemy.Transform),
                    })
                    .OrderBy(pair => pair.Distance)
                    .ThenBy(pair => pair.EventId)
                    .Take(3)
                    .ToArray();

                Console.WriteLine($"{start.Id} | {start.Respawn.Map} | area={policy.Area} | region={spawn.Name} | spawn=({spawn.Transform.X:F1},{spawn.Transform.Y:F1},{spawn.Transform.Z:F1}) yaw={spawn.Transform.Yaw:F1} | boss facts on map={map.BossDefeatHandlers.Count}");
                foreach (var boss in nearbyBosses)
                    Console.WriteLine($"  boss event={boss.EventId} enemy={boss.Name} collision={boss.CollisionName} distance={boss.Distance:F1}");

                if (selectedStartId != null)
                {
                    foreach (FogDsrEnemyFact enemy in map.EnemyDetails
                        .Select(enemy => new { Fact = enemy, Distance = Distance(spawn.Transform, enemy.Transform) })
                        .Where(pair => pair.Distance <= 75)
                        .OrderBy(pair => pair.Distance)
                        .Take(20)
                        .Select(pair => pair.Fact))
                        Console.WriteLine($"  enemy {enemy.Name} npcParam={enemy.NpcParamId} collision={enemy.CollisionName} at=({enemy.Transform.X:F1},{enemy.Transform.Y:F1},{enemy.Transform.Z:F1})");
                    foreach (FogDsrObjectFact obj in map.ObjectDetails
                        .Select(obj => new { Fact = obj, Distance = Distance(spawn.Transform, obj.Transform) })
                        .Where(pair => pair.Distance <= 75)
                        .OrderBy(pair => pair.Distance)
                        .Take(12)
                        .Select(pair => pair.Fact))
                        Console.WriteLine($"  object {obj.Name} entity={obj.EntityId} collision={obj.CollisionName} at=({obj.Transform.X:F1},{obj.Transform.Y:F1},{obj.Transform.Z:F1}) yaw={obj.Transform.Yaw:F1}");
                    foreach (FogDsrRegionFact region in map.RegionDetails
                        .Where(region => !string.Equals(region.Name, spawnRegionName, StringComparison.Ordinal))
                        .Select(region => new { Fact = region, Distance = Distance(spawn.Transform, region.Transform) })
                        .Where(pair => pair.Distance <= 75)
                        .OrderBy(pair => pair.Distance)
                        .Take(12)
                        .Select(pair => pair.Fact))
                        Console.WriteLine($"  region {region.Name} entity={region.EntityId} shape={FormatShape(region.Shape)} at=({region.Transform.X:F1},{region.Transform.Y:F1},{region.Transform.Z:F1}) yaw={region.Transform.Yaw:F1}");
                }
            }
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
