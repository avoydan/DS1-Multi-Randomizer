using System;
using System.Collections.Generic;
using FogMod;
using static FogMod.AnnotationData;

namespace FogHost.Tests
{
    internal static class BossProgressionTest
    {
        private const string Condition = "boss_defeated_11410410";
        private const string Entrance = "o4961_0000";

        public static void Verify()
        {
            GraphChecker.CheckRecord areaOnly = Check(triggerSide: false, centered: false);
            if (areaOnly.BossCompletions.Count != 0 || !areaOnly.Unvisited.Contains("lava"))
                throw new InvalidOperationException("Visiting the Ceaseless Area alone must not grant its completion state or open the lava route.");

            GraphChecker.CheckRecord wrongSpawn = Check(triggerSide: true, centered: false);
            if (wrongSpawn.BossCompletions.Count != 0 || !wrongSpawn.Unvisited.Contains("lava"))
                throw new InvalidOperationException("An arrival outside the verified boss trigger must not grant completion or open the route.");

            GraphChecker.CheckRecord completed = Check(triggerSide: true, centered: true);
            if (!completed.BossCompletions.Contains(Condition) || completed.Unvisited.Count != 0)
                throw new InvalidOperationException("A centered arrival through the verified Ceaseless trigger must make the boss completion transition available to the checker.");
        }

        private static GraphChecker.CheckRecord Check(bool triggerSide, bool centered)
        {
            var completion = new BossCompletionRule
            {
                Name = Condition,
                Area = "boss",
                EventId = 11410410,
                TriggerEntrance = Entrance,
                TriggerMap = "m14_01_00_00",
                TriggerRegionEntityId = 1412698,
            };
            var graph = new Graph
            {
                areas = new Dictionary<string, Area>(StringComparer.Ordinal)
                {
                    ["start"] = new Area { Name = "start" },
                    ["boss"] = new Area { Name = "boss" },
                    ["lava"] = new Area { Name = "lava" },
                },
                itemAreas = new Dictionary<string, string>(StringComparer.Ordinal),
                bossCompletions = new Dictionary<string, BossCompletionRule>(StringComparer.Ordinal) { [Condition] = completion },
                graph = new Dictionary<string, Graph.Node>(StringComparer.Ordinal)
                {
                    ["start"] = new Graph.Node { Area = "start" },
                    ["boss"] = new Graph.Node { Area = "boss" },
                    ["lava"] = new Graph.Node { Area = "lava" },
                },
            };
            Connect(graph, "start", "boss", null,
                triggerSide
                    ? new Side
                    {
                        Area = "boss",
                        BossTrigger = 11410410,
                        BossTriggerArea = centered ? "248.5 -284 69.7 -135 5 2 1.5" : "248.5 -284 69.7 -135 5 2 1.5",
                        CustomWarp = centered ? "m14_01_00_00 248.5 -284 69.7 -135" : "m14_01_00_00 249.69 -283.15 71.12 -135",
                    }
                    : new Side { Area = "boss" });
            Connect(graph, "boss", "lava", Expr.Named(Condition), new Side { Area = "lava" });
            var options = new RandomizerOptions();
            foreach (string flag in new[] { "explain", "dumpdist", "scalingbase" }) options[flag] = false;
            return new GraphChecker().Check(options, graph, "start");
        }

        private static void Connect(Graph graph, string from, string to, Expr condition, Side destinationSide)
        {
            var exit = new Graph.Edge { Type = Graph.EdgeType.Exit, From = from, Expr = condition, Side = new Side { Area = from } };
            var entrance = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = to, Side = destinationSide,
                Name = to == "boss" ? Entrance : null };
            exit.LinkedExpr = entrance.LinkedExpr = condition;
            graph.graph[from].To.Add(exit);
            graph.graph[to].From.Add(entrance);
            graph.connect(exit, entrance);
        }
    }
}
