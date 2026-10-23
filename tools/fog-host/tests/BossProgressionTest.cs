using System;
using System.Collections.Generic;
using System.Linq;
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
            if (areaOnly.BossCompletions.Count != 0 || !areaOnly.Unvisited.Contains("main") || !areaOnly.Unvisited.Contains("lava"))
                throw new InvalidOperationException("Visiting the Ceaseless Area alone must not grant its completion state or open the main-side lava route.");

            GraphChecker.CheckRecord wrongEntrance = Check(entranceName: "different-fog", triggerEventId: 11415371, defaultLanding: true);
            if (wrongEntrance.BossCompletions.Count != 0 || !wrongEntrance.Unvisited.Contains("lava"))
                throw new InvalidOperationException("A default landing through a different entrance must not grant the Ceaseless completion state.");

            GraphChecker.CheckRecord customWarp = Check(entranceName: Entrance, triggerEventId: 11415371, defaultLanding: false);
            if (customWarp.BossCompletions.Count != 0 || !customWarp.Unvisited.Contains("lava"))
                throw new InvalidOperationException("A custom warp must not stand in for the verified FogMod-default landing.");

            GraphChecker.CheckRecord wrongStartEvent = Check(entranceName: Entrance, triggerEventId: 11410410, defaultLanding: true);
            if (wrongStartEvent.BossCompletions.Count != 0 || !wrongStartEvent.Unvisited.Contains("lava"))
                throw new InvalidOperationException("The boss-death event must not be confused with its encounter-start event.");

            GraphChecker.CheckRecord mainWithoutBoss = Check(entranceName: "unrelated-boss-area-entry", triggerEventId: 11415371, defaultLanding: true, reachMain: true);
            if (mainWithoutBoss.BossCompletions.Count != 0 || !mainWithoutBoss.Unvisited.Contains("lava"))
                throw new InvalidOperationException("The lower route must remain locked when the main Demon Ruins Area is reachable but Ceaseless has not been defeated.");

            GraphChecker.CheckRecord completedWithoutMain = Check(entranceName: Entrance, triggerEventId: 11415371, defaultLanding: true, reachMain: false);
            if (!completedWithoutMain.BossCompletions.Contains(Condition)
                || !completedWithoutMain.Unvisited.Contains("main") || !completedWithoutMain.Unvisited.Contains("lava"))
                throw new InvalidOperationException("Ceaseless completion inside the arena must not make the main-side lower route reachable when the main Demon Ruins Area is inaccessible.");

            GraphChecker.CheckRecord completedWithMain = Check(entranceName: Entrance, triggerEventId: 11415371, defaultLanding: true, reachMain: true);
            if (!completedWithMain.BossCompletions.Contains(Condition) || completedWithMain.Unvisited.Count != 0)
                throw new InvalidOperationException("Ceaseless completion plus reachability to the physical main-side route must open the lower Demon Ruins Area.");

            GraphChecker.CheckRecord sealedPocket = Check(entranceName: Entrance, triggerEventId: 11415371, defaultLanding: true, reachMain: false);
            if (!sealedPocket.BossCompletions.Contains(Condition)
                || !sealedPocket.Records.ContainsKey("asylum") || !sealedPocket.Records.ContainsKey("boss")
                || sealedPocket.Records.ContainsKey("main") || sealedPocket.Records.ContainsKey("lava"))
                throw new InvalidOperationException("The general graph checker must reject a randomized Asylum-to-Ceaseless pocket whose only exit returns to Asylum, without a seed-specific rule.");

            Graph postBossPocket = BuildGraph(Entrance, 11415371, defaultLanding: true, reachMain: false);
            FogHostException trapped = null;
            try { BossProgressionValidator.ValidatePostBossRoutes(Options(), postBossPocket, CreateAnnotations()); }
            catch (FogHostException error) { trapped = error; }
            if (trapped?.Code != HostErrors.SeedUnsolvable || !trapped.Retryable || trapped.RetryScope != "fog"
                || trapped.Stage != "solve-graph" || !trapped.Message.Contains("11410410", StringComparison.Ordinal)
                || !trapped.Message.Contains("lava", StringComparison.Ordinal))
                throw new InvalidOperationException("The v5 post-boss validator must reject an encounter pocket whose only exit returns to Asylum and report a retryable Fog-seed failure.");

            Graph postBossRoute = BuildGraph(Entrance, 11415371, defaultLanding: true, reachMain: true);
            BossProgressionValidator.ValidatePostBossRoutes(Options(), postBossRoute, CreateAnnotations());

            Graph routeThroughAnotherFog = BuildGraph(Entrance, 11415371, defaultLanding: true,
                reachMain: true, mainViaRandomGate: true);
            FogHostException indirect = null;
            try { BossProgressionValidator.ValidatePostBossRoutes(Options(), routeThroughAnotherFog, CreateAnnotations()); }
            catch (FogHostException error) { indirect = error; }
            if (indirect?.Code != HostErrors.SeedUnsolvable || !indirect.Message.Contains("another randomized entrance", StringComparison.Ordinal))
                throw new InvalidOperationException("Post-boss reachability through an unrelated randomized Fog gate must not substitute for the boss area's physical return route.");

            Graph connectorGraph = BuildGraph(Entrance, 11415371, defaultLanding: true, reachMain: false);
            var connectorOptions = new RandomizerOptions();
            foreach (string flag in new[] { "explain", "dumpdist", "scalingbase", "unconnected", "warp", "start" }) connectorOptions[flag] = false;
            bool rejectedByGeneralConnector = false;
            try
            {
                new GraphConnector().Connect(connectorOptions, connectorGraph, new Annotations());
            }
            catch (Exception error) when (error.Message.Contains("Could not find edge into unreachable areas", StringComparison.Ordinal)
                && error.Message.Contains("main", StringComparison.Ordinal) && error.Message.Contains("lava", StringComparison.Ordinal))
            {
                rejectedByGeneralConnector = true;
            }
            if (!rejectedByGeneralConnector)
                throw new InvalidOperationException("The general GraphConnector must reject the sealed Ceaseless pocket because its only return is to the already visited Asylum.");
        }

        private static GraphChecker.CheckRecord Check(string entranceName, int triggerEventId, bool defaultLanding, bool reachMain = false)
        {
            Graph graph = BuildGraph(entranceName, triggerEventId, defaultLanding, reachMain);
            var options = new RandomizerOptions();
            foreach (string flag in new[] { "explain", "dumpdist", "scalingbase" }) options[flag] = false;
            return new GraphChecker().Check(options, graph, "asylum");
        }

        private static Graph BuildGraph(string entranceName, int triggerEventId, bool defaultLanding, bool reachMain,
            bool mainViaRandomGate = false)
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
                    ["asylum"] = new Area { Name = "asylum" },
                    ["boss"] = new Area { Name = "boss" },
                    ["main"] = new Area { Name = "main" },
                    ["lava"] = new Area { Name = "lava" },
                },
                itemAreas = new Dictionary<string, string>(StringComparer.Ordinal),
                bossCompletions = new Dictionary<string, BossCompletionRule>(StringComparer.Ordinal) { [Condition] = completion },
                graph = new Dictionary<string, Graph.Node>(StringComparer.Ordinal)
                {
                    ["asylum"] = new Graph.Node { Area = "asylum" },
                    ["boss"] = new Graph.Node { Area = "boss" },
                    ["main"] = new Graph.Node { Area = "main" },
                    ["lava"] = new Graph.Node { Area = "lava" },
                },
            };
            ConnectBidirectionalFog(graph, "asylum", "boss", entranceName, defaultLanding
                ? new Side { Area = "boss", BossEncounterStartEventId = triggerEventId }
                : new Side { Area = "boss", CustomWarp = "m14_01_00_00 248.5 -284 69.7 -135" });
            if (!graph.graph["boss"].To.Any(edge => edge.To == "asylum"))
                throw new InvalidOperationException("The synthetic Ceaseless fog boundary must return from the encounter area to Asylum.");
            if (reachMain) Connect(graph, "asylum", "main", null, new Side { Area = "main" },
                mainViaRandomGate ? "unrelated-random-fog" : null);
            Connect(graph, "main", "lava", Expr.Named(Condition), new Side { Area = "lava" });
            return graph;
        }

        private static RandomizerOptions Options()
        {
            var options = new RandomizerOptions();
            foreach (string flag in new[] { "explain", "dumpdist", "scalingbase", "unconnected", "warp", "start" }) options[flag] = false;
            return options;
        }

        private static Annotations CreateAnnotations() => new Annotations
        {
            BossCompletions = new List<BossCompletionRule>
            {
                new BossCompletionRule
                {
                    Name = Condition,
                    Area = "boss",
                    EventId = 11410410,
                    TriggerEventId = 11415371,
                    TriggerEntrance = Entrance,
                    TriggerMap = "m14_01_00_00",
                    TriggerRegionEntityId = 1412698,
                },
            },
        };

        private static void Connect(Graph graph, string from, string to, Expr condition, Side destinationSide, string entranceName = null)
        {
            var exit = new Graph.Edge { Type = Graph.EdgeType.Exit, From = from, Expr = condition, Side = new Side { Area = from }, IsFixed = entranceName == null };
            var entrance = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = to, Side = destinationSide,
                Name = to == "boss" ? entranceName : null, IsFixed = entranceName == null };
            exit.LinkedExpr = entrance.LinkedExpr = condition;
            graph.graph[from].To.Add(exit);
            graph.graph[to].From.Add(entrance);
            graph.connect(exit, entrance);
        }

        private static void ConnectBidirectionalFog(Graph graph, string asylumArea, string bossArea, string entranceName, Side bossArrival)
        {
            var exitFromBoss = new Graph.Edge { Type = Graph.EdgeType.Exit, From = bossArea, Side = new Side { Area = bossArea }, Name = entranceName };
            var entranceIntoAsylum = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = asylumArea, Side = new Side { Area = asylumArea }, Name = entranceName };
            var exitFromAsylum = new Graph.Edge { Type = Graph.EdgeType.Exit, From = asylumArea, Side = new Side { Area = asylumArea }, Name = entranceName };
            var entranceIntoBoss = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = bossArea, Side = bossArrival, Name = entranceName };

            exitFromBoss.Pair = entranceIntoBoss;
            entranceIntoBoss.Pair = exitFromBoss;
            exitFromAsylum.Pair = entranceIntoAsylum;
            entranceIntoAsylum.Pair = exitFromAsylum;

            graph.graph[bossArea].To.Add(exitFromBoss);
            graph.graph[asylumArea].From.Add(entranceIntoAsylum);
            graph.graph[asylumArea].To.Add(exitFromAsylum);
            graph.graph[bossArea].From.Add(entranceIntoBoss);
            graph.connect(exitFromBoss, entranceIntoAsylum);
        }
    }
}
