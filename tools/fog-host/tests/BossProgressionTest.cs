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
            GraphChecker.CheckRecord areaOnly = Check(entranceName: "unrelated-boss-area-entry", triggerEventId: 11415371, defaultLanding: true);
            if (areaOnly.BossCompletions.Count != 0 || !areaOnly.Unvisited.Contains("lava"))
                throw new InvalidOperationException("Visiting the Ceaseless Area alone must not grant its completion state or open the lava route.");

            GraphChecker.CheckRecord wrongEntrance = Check(entranceName: "different-fog", triggerEventId: 11415371, defaultLanding: true);
            if (wrongEntrance.BossCompletions.Count != 0 || !wrongEntrance.Unvisited.Contains("lava"))
                throw new InvalidOperationException("A default landing through a different entrance must not grant the Ceaseless completion state.");

            GraphChecker.CheckRecord customWarp = Check(entranceName: Entrance, triggerEventId: 11415371, defaultLanding: false);
            if (customWarp.BossCompletions.Count != 0 || !customWarp.Unvisited.Contains("lava"))
                throw new InvalidOperationException("A custom warp must not stand in for the verified FogMod-default landing.");

            GraphChecker.CheckRecord wrongStartEvent = Check(entranceName: Entrance, triggerEventId: 11410410, defaultLanding: true);
            if (wrongStartEvent.BossCompletions.Count != 0 || !wrongStartEvent.Unvisited.Contains("lava"))
                throw new InvalidOperationException("The boss-death event must not be confused with its encounter-start event.");

            GraphChecker.CheckRecord completed = Check(entranceName: Entrance, triggerEventId: 11415371, defaultLanding: true);
            if (!completed.BossCompletions.Contains(Condition) || completed.Unvisited.Count != 0)
                throw new InvalidOperationException("The exact Ceaseless entrance with its verified default landing must make the boss completion transition available to the checker.");
        }

        private static GraphChecker.CheckRecord Check(string entranceName, int triggerEventId, bool defaultLanding)
        {
            var completion = new BossCompletionRule
            {
                Name = Condition,
                Area = "boss",
                EventId = 11410410,
                TriggerEventId = 11415371,
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
            Connect(graph, "start", "boss", null, defaultLanding
                ? new Side { Area = "boss", BossEncounterStartEventId = triggerEventId }
                : new Side { Area = "boss", CustomWarp = "m14_01_00_00 248.5 -284 69.7 -135" }, entranceName);
            Connect(graph, "boss", "lava", Expr.Named(Condition), new Side { Area = "lava" });
            var options = new RandomizerOptions();
            foreach (string flag in new[] { "explain", "dumpdist", "scalingbase" }) options[flag] = false;
            return new GraphChecker().Check(options, graph, "start");
        }

        private static void Connect(Graph graph, string from, string to, Expr condition, Side destinationSide, string entranceName = null)
        {
            var exit = new Graph.Edge { Type = Graph.EdgeType.Exit, From = from, Expr = condition, Side = new Side { Area = from } };
            var entrance = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = to, Side = destinationSide,
                Name = to == "boss" ? entranceName : null };
            exit.LinkedExpr = entrance.LinkedExpr = condition;
            graph.graph[from].To.Add(exit);
            graph.graph[to].From.Add(entrance);
            graph.connect(exit, entrance);
        }
    }
}
