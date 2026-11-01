using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FogMod;
using static SoulsIds.GameSpec;
using static FogMod.AnnotationData;

namespace FogHost.Tests
{
    /// <summary>
    /// Regression coverage for generation profile ds1tr-gen-v6 (Compact Fog v3): the start-reachability rule that replaced the Ceaseless-specific post-boss check.
    ///
    /// The original hard-lock was not a Ceaseless problem. The Asylum starting hallway has one way out, the randomized wall o8952_0000, and the compact
    /// catalog (v1, v2) also gave it two unconditional fixed exits that upstream FogMod does not have (to the Asylum exterior and to Firelink). The solver
    /// therefore accepted layouts whose only hallway exit pairs with a dead-end pocket (Ceaseless in the confirmed case): the player walks through the wall,
    /// can only walk back, and nothing else is reachable. These tests measure that with a "sealed hallway" reading of a connected graph (no unconditional
    /// fixed exit leaves the hallway) and prove that v3 has nothing left for the reading to remove.
    /// </summary>
    internal static class StartReachabilityTest
    {
        private const string Hallway = "asylum";
        private static readonly string[] OriginalOptions = { "traversableFogs", "bossFogs", "areaWarps", "scaling", "requireLordSouls" };

        // ---- synthetic graph (no game data) --------------------------------------------------------------------------------------------------------------

        public static void VerifySyntheticStartTrapIsRejected()
        {
            // start --(randomized fog, two-way)-- pocket; the world is behind a different entrance that no longer touches the start.
            Graph trapped = BuildSyntheticGraph(startToWorld: false);
            FogHostException error = Capture(() => StartReachabilityValidator.Validate(Options(), trapped));
            if (error?.Code != HostErrors.SeedUnsolvable || !error.Retryable || error.RetryScope != "fog" || error.Stage != "solve-graph"
                || !error.Message.Contains("world", StringComparison.Ordinal) || !error.Message.Contains("start", StringComparison.Ordinal))
                throw new InvalidOperationException("A start region whose only exit pairs with a dead-end pocket must be rejected as a retryable Fog-seed failure naming the unreachable Area.");

            // The same graph with a real route from the start passes: the rule is general reachability, not a ban on pockets.
            Graph open = BuildSyntheticGraph(startToWorld: true);
            StartReachabilityValidator.Validate(Options(), open);
        }

        public static void VerifySyntheticFakeUnconditionalExitHidesTheTrap()
        {
            // This is the defect itself: with an unconditional fixed exit out of the start (v1/v2 Asylum), the very same pocket layout looks reachable.
            Graph fake = BuildSyntheticGraph(startToWorld: true);
            GraphChecker.CheckRecord global = new GraphChecker().Check(Options(), fake, "start");
            GraphChecker.CheckRecord sealedStart = new GraphChecker().Check(Options(), fake, "start", null, edge => !(edge.From == "start" && edge.IsFixed && edge.LinkedExpr == null));
            if (global.Unvisited.Count != 0 || !sealedStart.Unvisited.Contains("world") || sealedStart.Unvisited.Contains("pocket"))
                throw new InvalidOperationException("The sealed-start reading must expose the trap that an unconditional fixed exit hides: the pocket stays reachable, the world does not.");
        }

        // ---- real data -----------------------------------------------------------------------------------------------------------------------------------

        /// <summary>v3 gives the Asylum hallway no unconditional exit; v1 and v2 keep their frozen fake exits.</summary>
        public static void VerifyV3CatalogSealsTheAsylumStart()
        {
            string dataRoot = RequiredDirectory("DS1TR_COMPACT_DATA_ROOT");
            string pristineRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineRoot, includeLegacyAnnotations: false);
            FogCompactCatalogV2 v2 = FogCompactCatalogBuilder.BuildV2(facts);
            FogCompactCatalogV3 v3 = FogCompactCatalogBuilder.BuildV3(facts);
            if (!v3.IsComplete || v3.Fingerprint == v2.Fingerprint)
                throw new InvalidOperationException("The v3 catalog must be complete and have its own fingerprint.");

            FogArea v2Hallway = v2.Catalog.Areas.Single(area => area.Id.Value == Hallway);
            FogArea v3Hallway = v3.Catalog.Areas.Single(area => area.Id.Value == Hallway);
            if (!v2Hallway.To.Any(link => link.Condition == null && link.Destination.Value == "firelink")
                || !v2Hallway.To.Any(link => link.Condition == null && link.Destination.Value == "asylum_outside"))
                throw new InvalidOperationException("The frozen v2 catalog must still carry the hallway's two unconditional exits (profile ds1tr-gen-v5 stays byte-compatible).");
            if (v3Hallway.To.Any(link => link.Condition == null) || v3Hallway.To.Any(link => link.Destination.Value == "firelink")
                || !v3Hallway.To.Any(link => link.Destination.Value == "asylum_outside" && link.Condition != null))
                throw new InvalidOperationException("The v3 hallway must have no unconditional exit, no hallway-owned route to Firelink, and a conditioned exterior route.");

            // Every other Area of the v3 catalog is exactly v2's: only the hallway changed.
            foreach (FogArea area in v3.Catalog.Areas.Where(area => area.Id.Value != Hallway))
            {
                FogArea before = v2.Catalog.Areas.Single(candidate => candidate.Id == area.Id);
                if (before.To.Count != area.To.Count) throw new InvalidOperationException($"v3 changed the links of {area.Id.Value}; only the Asylum hallway may change.");
            }

            // The Ceaseless post-boss route proof is retired for v3: no completion rule asks for it, and the lower route stays an ordinary boss-gated edge.
            Annotations annotations = v3.CreateFreshCandidateAnnotations();
            if (annotations.BossCompletions.Any(rule => rule.RequireFixedPostBossRoute))
                throw new InvalidOperationException("No v3 boss completion may require the retired fixed post-boss route.");
            if (!annotations.BossCompletions.Any(rule => rule.EventId == 11410410))
                throw new InvalidOperationException("v3 must keep the Ceaseless completion as an ordinary boss-completion condition.");
            if (!annotations.Areas.Single(area => area.Name == "demonruins").To.Any(link => link.Area == "demonruins_lava" && link.Cond != null && link.Cond.Contains("boss_defeated_11410410", StringComparison.Ordinal)))
                throw new InvalidOperationException("v3 must keep the main Demon Ruins to lava route gated by Ceaseless completion.");
        }

        /// <summary>
        /// Regression A: the original confirmed V4 hard-lock. Frozen compact-v1, Fog seed 4009337993, the original options: the solver accepts it, the sealed
        /// hallway reaches only {Asylum, Ceaseless}. The same seed on v3 reaches the whole world.
        /// </summary>
        public static void VerifyOriginalHardLockSeed()
        {
            const uint seed = 4009337993;
            Connected v1 = Connect("v1", seed, OriginalOptions);
            string[] hallwayExits = RandomizedExits(v1.Graph, Hallway);
            if (hallwayExits.Length != 1 || hallwayExits[0] != "demonruins_ceaseless")
                throw new InvalidOperationException("The original seed must pair the Asylum hallway's only randomized exit (o8952_0000) with the Ceaseless pocket.");
            string[] pocketExits = RandomizedExits(v1.Graph, "demonruins_ceaseless");
            if (pocketExits.Length != 1 || pocketExits[0] != Hallway)
                throw new InvalidOperationException("The Ceaseless pocket's only randomized exit (o4961_0000) must return to the Asylum hallway.");
            if (new GraphChecker().Check(v1.Options, v1.Graph, Hallway).Unvisited.Count != 0)
                throw new InvalidOperationException("The frozen v1 graph is expected to accept this layout (that is the defect).");
            GraphChecker.CheckRecord trapped = SealedHallway(v1);
            if (trapped.Unvisited.Count < 100 || !trapped.Records.ContainsKey("demonruins_ceaseless") || trapped.Records.Count > 5)
                throw new InvalidOperationException($"The sealed hallway must be a closed region around the Ceaseless pocket; reached {trapped.Records.Count} Areas.");

            Connected v3 = Connect("v3", seed, OriginalOptions);
            AssertWorldReachable(v3, "the original hard-lock seed on v3");
            StartReachabilityValidator.Validate(v3.Options, v3.Graph);
        }

        /// <summary>
        /// Regression B: the V5 owner smoke seed (Fog seed 6843). Its accepted V5 world traps the start in a four-Area region (hallway, Tomb of the Giants
        /// Lordvessel gate, coffin, Burg start); the same Fog seed on v3 reaches everything.
        /// </summary>
        public static void VerifySmokeSeedStartTrap()
        {
            const uint seed = 6843;
            Connected v2 = Connect("v2", seed, OriginalOptions);
            GraphChecker.CheckRecord trapped = SealedHallway(v2);
            if (trapped.Unvisited.Count < 100 || trapped.Records.Count > 8)
                throw new InvalidOperationException($"The V5 smoke Fog seed must trap the sealed hallway in a small region; reached {trapped.Records.Count} Areas.");

            Connected v3 = Connect("v3", seed, OriginalOptions);
            AssertWorldReachable(v3, "the V5 smoke seed on v3");
            StartReachabilityValidator.Validate(v3.Options, v3.Graph);
        }

        /// <summary>
        /// Regression C: a valid world that the V5 Ceaseless check rejects. Fog seed 31341427 is rejected by the V5 post-boss rule although its start reaches
        /// the whole world; v3 does not reject it.
        /// </summary>
        public static void VerifyWorldRejectedByCeaselessRuleIsAcceptedByV6()
        {
            const uint seed = 31341427;
            Connected v2 = Connect("v2", seed, OriginalOptions);
            if (SealedHallway(v2).Unvisited.Count != 0)
                throw new InvalidOperationException("The chosen seed must be a start-reachable world on v2 (it is the case of a valid world rejected only by the Ceaseless rule).");
            Annotations v2Annotations = ConnectedAnnotations(v2);
            FogHostException rejected = Capture(() => BossProgressionValidator.ValidatePostBossRoutes(v2.Options, v2.Graph, v2Annotations));
            if (rejected?.Code != HostErrors.SeedUnsolvable || !rejected.Message.Contains("11410410", StringComparison.Ordinal))
                throw new InvalidOperationException("The V5 Ceaseless post-boss rule must keep rejecting this seed (profile ds1tr-gen-v5 is frozen).");

            Connected v3 = Connect("v3", seed, OriginalOptions);
            AssertWorldReachable(v3, "the Ceaseless-rejected world on v3");
            StartReachabilityValidator.Validate(v3.Options, v3.Graph);
        }

        // ---- helpers -------------------------------------------------------------------------------------------------------------------------------------

        private sealed class Connected
        {
            public Graph Graph;
            public RandomizerOptions Options;
            public Annotations Annotations;
        }

        private static Annotations ConnectedAnnotations(Connected connected) => connected.Annotations;

        private static Connected Connect(string catalog, uint seed, string[] enabledOptions)
        {
            string dataRoot = RequiredDirectory("DS1TR_COMPACT_DATA_ROOT");
            string pristineRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            string original = Environment.CurrentDirectory;
            string scratch = Path.Combine(Path.GetTempPath(), "ds1tr-start-reachability-" + Guid.NewGuid().ToString("N"));
            TextWriter originalOutput = Console.Out;
            try
            {
                Environment.CurrentDirectory = dataRoot;
                FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineRoot, includeLegacyAnnotations: false);
                Annotations annotations = catalog == "v1" ? FogCompactCatalogBuilder.Build(facts).CreateFreshCandidateAnnotations()
                    : catalog == "v2" ? FogCompactCatalogBuilder.BuildV2(facts).CreateFreshCandidateAnnotations()
                    : FogCompactCatalogBuilder.BuildV3(facts).CreateFreshCandidateAnnotations();
                var enabled = new HashSet<string>(enabledOptions, StringComparer.Ordinal);
                var options = new RandomizerOptions { Seed = unchecked((int)seed) };
                foreach ((string key, string flag) in HostRequest.OptionFlags) options[flag] = enabled.Contains(key);
                options.Language = "ENGLISH";
                if (catalog != "v1") FogCompactV2PvpPolicy.ApplyGraphOptions(annotations, options["major"], options["minor"]);
                var graph = new Graph();
                graph.Construct(options, annotations);
                string staging = Path.Combine(scratch, "staging");
                CompactRealGenerationTest.CopyGameDataForAudit(Path.Combine(dataRoot, "dist", "DS1R"), staging, pristineRoot, "ENGLISH");
                Console.SetOut(TextWriter.Null);
                new ItemReader().FindItems(options, annotations, graph, staging, FromGame.DS1R);
                new GraphConnector().Connect(options, graph, annotations);
                return new Connected { Graph = graph, Options = options, Annotations = annotations };
            }
            finally
            {
                Console.SetOut(originalOutput);
                Environment.CurrentDirectory = original;
                try { Directory.Delete(scratch, true); } catch (IOException) { }
            }
        }

        /// <summary>Reachability from the start with every unconditional fixed edge leaving the Asylum hallway removed (what the hallway physically is).</summary>
        private static GraphChecker.CheckRecord SealedHallway(Connected connected) =>
            new GraphChecker().Check(connected.Options, connected.Graph, connected.Graph.start.Area, null,
                edge => !(edge.From == Hallway && edge.IsFixed && edge.LinkedExpr == null));

        private static void AssertWorldReachable(Connected connected, string what)
        {
            if (connected.Graph.start.Area != Hallway) throw new InvalidOperationException("These regressions start at the Asylum hallway.");
            GraphChecker.CheckRecord sealedReach = SealedHallway(connected);
            if (sealedReach.Unvisited.Count != 0)
                throw new InvalidOperationException($"{what}: the start must reach every Area without any unconditional hallway exit; unreachable: {string.Join(", ", sealedReach.Unvisited.Take(8))}.");
            if (RandomizedExits(connected.Graph, Hallway).Length != 1)
                throw new InvalidOperationException($"{what}: the hallway must keep exactly one randomized exit.");
            foreach (Graph.Edge edge in connected.Graph.graph[Hallway].To.Where(edge => edge.IsFixed))
                if (edge.LinkedExpr == null) throw new InvalidOperationException($"{what}: the hallway has an unconditional fixed exit to {edge.To}.");
        }

        private static string[] RandomizedExits(Graph graph, string area) =>
            graph.graph[area].To.Where(edge => !edge.IsFixed).Select(edge => edge.To).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        private static FogHostException Capture(Action action)
        {
            try { action(); return null; }
            catch (FogHostException error) { return error; }
        }

        private static RandomizerOptions Options()
        {
            var options = new RandomizerOptions();
            foreach (string flag in new[] { "explain", "dumpdist", "scalingbase", "unconnected", "warp", "start" }) options[flag] = false;
            return options;
        }

        private static string RequiredDirectory(string variable)
        {
            string value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value) || !Directory.Exists(value)) throw new InvalidOperationException($"{variable} must name an existing directory.");
            return Path.GetFullPath(value);
        }

        private static Graph BuildSyntheticGraph(bool startToWorld)
        {
            var graph = new Graph
            {
                areas = new Dictionary<string, Area>(StringComparer.Ordinal)
                {
                    ["start"] = new Area { Name = "start" },
                    ["pocket"] = new Area { Name = "pocket" },
                    ["world"] = new Area { Name = "world" },
                },
                itemAreas = new Dictionary<string, string>(StringComparer.Ordinal),
                bossCompletions = new Dictionary<string, BossCompletionRule>(StringComparer.Ordinal),
                graph = new Dictionary<string, Graph.Node>(StringComparer.Ordinal)
                {
                    ["start"] = new Graph.Node { Area = "start" },
                    ["pocket"] = new Graph.Node { Area = "pocket" },
                    ["world"] = new Graph.Node { Area = "world" },
                },
                start = new CustomStart { Name = "Start", Area = "start" },
            };
            ConnectFog(graph, "start", "pocket", "wall");
            if (startToWorld) ConnectFixed(graph, "start", "world");
            return graph;
        }

        private static void ConnectFixed(Graph graph, string from, string to)
        {
            var exit = new Graph.Edge { Type = Graph.EdgeType.Exit, From = from, Side = new Side { Area = from }, IsFixed = true };
            var entrance = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = to, Side = new Side { Area = to }, IsFixed = true };
            graph.graph[from].To.Add(exit);
            graph.graph[to].From.Add(entrance);
            graph.connect(exit, entrance);
        }

        private static void ConnectFog(Graph graph, string first, string second, string name)
        {
            // Same wiring as BossProgressionTest: pairs are the two edges of one Area's side; one connect call links both directions.
            var exitFirst = new Graph.Edge { Type = Graph.EdgeType.Exit, From = first, Side = new Side { Area = first }, Name = name };
            var entranceFirst = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = first, Side = new Side { Area = first }, Name = name };
            var exitSecond = new Graph.Edge { Type = Graph.EdgeType.Exit, From = second, Side = new Side { Area = second }, Name = name };
            var entranceSecond = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = second, Side = new Side { Area = second }, Name = name };
            exitFirst.Pair = entranceFirst;
            entranceFirst.Pair = exitFirst;
            exitSecond.Pair = entranceSecond;
            entranceSecond.Pair = exitSecond;
            graph.graph[first].To.Add(exitFirst);
            graph.graph[first].From.Add(entranceFirst);
            graph.graph[second].To.Add(exitSecond);
            graph.graph[second].From.Add(entranceSecond);
            graph.connect(exitSecond, entranceFirst);
        }
    }
}
