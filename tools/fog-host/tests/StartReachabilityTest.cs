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

            // Every other Area of the v3 catalog is exactly v2's: only the hallway, the two lava entries and the Sen's cage rooms changed.
            var changed = new HashSet<string>(StringComparer.Ordinal) { Hallway, "demonruins_izalith", "demonruins_izalithprelava", "sens_mid", "sens_cage", "sens_upper" };
            foreach (FogArea area in v3.Catalog.Areas.Where(area => !changed.Contains(area.Id.Value)))
            {
                FogArea before = v2.Catalog.Areas.Single(candidate => candidate.Id == area.Id);
                if (before.To.Count != area.To.Count) throw new InvalidOperationException($"v3 changed the links of {area.Id.Value}; only the Asylum hallway, the lava entries and the Sen's cage may change.");
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
        /// The lava crossing and the Sen's cage are physically gated in v3: ring on both lava entries (leaving the lava is free), cage key on the cage door, the
        /// cage a closed room. v2 keeps its frozen unconditional routes and the cage bypass; the two keys are found where the game puts them.
        /// </summary>
        public static void VerifyV3LavaAndCageCatalog()
        {
            string dataRoot = RequiredDirectory("DS1TR_COMPACT_DATA_ROOT");
            string pristineRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineRoot, includeLegacyAnnotations: false);
            FogCompactCatalogV2 v2 = FogCompactCatalogBuilder.BuildV2(facts);
            FogCompactCatalogV3 v3 = FogCompactCatalogBuilder.BuildV3(facts);
            FogAreaLink Link(FogCompactCatalogV2 catalog, string from, string to) => catalog.Catalog.Areas.Single(area => area.Id.Value == from).To.SingleOrDefault(link => link.Destination.Value == to);
            FogAreaLink LinkV3(string from, string to) => v3.Catalog.Areas.Single(area => area.Id.Value == from).To.SingleOrDefault(link => link.Destination.Value == to);

            // v2 (profile ds1tr-gen-v5) is frozen: free lava crossing, unlocked cage and the cage bypass between the middle and the roof.
            foreach ((string from, string to) in new[] { ("demonruins_izalith", "demonruins_izalithlava"), ("demonruins_izalithprelava", "demonruins_izalithlava"), ("sens_mid", "sens_cage"), ("sens_cage", "sens_upper"), ("sens_upper", "sens_cage") })
                if (Link(v2, from, to)?.Condition != null || Link(v2, from, to) == null)
                    throw new InvalidOperationException($"The frozen v2 route {from} > {to} must still exist and be unconditional.");

            // v3: both lava entries need the ring, both exits stay free.
            foreach (string entry in new[] { "demonruins_izalith", "demonruins_izalithprelava" })
            {
                FogAreaLink gate = LinkV3(entry, "demonruins_izalithlava");
                if (gate?.Condition is not { Kind: FogConditionKind.Atom, Terms.Count: 1 } || gate.Condition.Terms[0] != FogConditionReference.KeyItem(new FogItemName("orange_charred_ring")))
                    throw new InvalidOperationException($"v3 must gate {entry} > demonruins_izalithlava on the Orange Charred Ring.");
            }
            if (LinkV3("demonruins_izalithlava", "demonruins_izalith")?.Condition != null || LinkV3("demonruins_izalithlava", "demonruins_izalithprelava")?.Condition != null
                || LinkV3("demonruins_izalithlava", "demonruins_izalith") == null || LinkV3("demonruins_izalithlava", "demonruins_izalithprelava") == null)
                throw new InvalidOperationException("Leaving the lava segment must stay free in v3.");

            // v3: the cage door needs the Cage Key, the cage keeps its way back, and no link joins the cage to the roof.
            FogAreaLink door = LinkV3("sens_mid", "sens_cage");
            if (door?.Condition is not { Kind: FogConditionKind.Atom, Terms.Count: 1 } || door.Condition.Terms[0] != FogConditionReference.KeyItem(new FogItemName("cage_key_goods_2003")))
                throw new InvalidOperationException("v3 must lock sens_mid > sens_cage with the Cage Key.");
            if (LinkV3("sens_cage", "sens_mid") == null || LinkV3("sens_cage", "sens_upper") != null || LinkV3("sens_upper", "sens_cage") != null)
                throw new InvalidOperationException("v3's cage is a closed room: a way back to sens_mid and no link to sens_upper.");

            // The keys are found where the game puts them: the ring when the Centipede Demon encounter dies (event 11415386, collision h0062B1), the cage key on a roof collision.
            if (v3.Catalog.LotRows.Single(row => row.Lot.Value == 2670).Area.Value != "demonruins_centipede" || v3.Catalog.LotLocations.Single(lot => lot.FirstLot.Value == 2670).Area.Value != "demonruins_centipede"
                || v3.Catalog.LotRows.Single(row => row.Lot.Value == 1500150).Area.Value != "sens_upper" || v3.Catalog.LotLocations.Single(lot => lot.FirstLot.Value == 1500150).Area.Value != "sens_upper")
                throw new InvalidOperationException("v3 must find the Orange Charred Ring in demonruins_centipede and the Cage Key in sens_upper.");
            if (v2.Catalog.LotRows.Single(row => row.Lot.Value == 2670).Area.Value != "demonruins" || v2.Catalog.LotRows.Single(row => row.Lot.Value == 1500150).Area.Value != "sens")
                throw new InvalidOperationException("The frozen v2 lot areas must be unchanged.");
        }

        /// <summary>
        /// On real v3 layouts: without the ring the lava segment is unreachable, with the ring where the game puts it it is reachable; without the cage key the cage is
        /// unreachable and the cage never carries the player to the roof (v2 does: the bypass).
        /// </summary>
        public static void VerifyLavaAndCageOnRealLayouts()
        {
            int ringChecked = 0, cageChecked = 0, v2Bypass = 0;
            for (uint seed = 1; seed <= 12; seed++)
            {
                Connected v3 = Connect("v3", seed, OriginalOptions);
                string start = v3.Graph.start.Area;
                string ringArea = v3.Graph.itemAreas["orange_charred_ring"];
                string cageArea = v3.Graph.itemAreas["cage_key_goods_2003"];
                if (ringArea != "demonruins_centipede" || cageArea != "sens_upper")
                    throw new InvalidOperationException($"seed {seed}: the ring must sit in demonruins_centipede and the cage key in sens_upper (found {ringArea} and {cageArea}).");

                // 1 + 2: the ring.
                if (new GraphChecker().Check(v3.Options, v3.Graph, start).Unvisited.Count != 0)
                    throw new InvalidOperationException($"seed {seed}: the accepted v3 layout must be completely reachable with the ring.");
                var withoutRing = WithoutItem(v3, "orange_charred_ring", () => new GraphChecker().Check(v3.Options, v3.Graph, start).Unvisited.ToArray());
                if (!withoutRing.Contains("demonruins_izalithlava"))
                    throw new InvalidOperationException($"seed {seed}: the lava segment must not be reachable before the ring.");
                ringChecked++;

                // 3 + 4: the cage.
                var withoutKey = WithoutItem(v3, "cage_key_goods_2003", () => new GraphChecker().Check(v3.Options, v3.Graph, start).Unvisited.ToArray());
                if (!withoutKey.Contains("sens_cage"))
                    throw new InvalidOperationException($"seed {seed}: the cage must not be reachable without the Cage Key.");
                // The roof must not be reachable through the cage: forbid every randomized way into it and nothing else.
                Func<Graph.Edge, bool> noRandomizedWayIntoRoof = edge => edge.IsFixed || edge.To != "sens_upper";
                var viaCage = new GraphChecker().Check(v3.Options, v3.Graph, start, null, noRandomizedWayIntoRoof);
                if (viaCage.Records.ContainsKey("sens_upper"))
                    throw new InvalidOperationException($"seed {seed}: sens_upper must not be reachable through the cage.");
                cageChecked++;

                Connected v2 = Connect("v2", seed, OriginalOptions);
                var v2ViaCage = new GraphChecker().Check(v2.Options, v2.Graph, v2.Graph.start.Area, null, noRandomizedWayIntoRoof);
                if (v2ViaCage.Records.ContainsKey("sens_upper")) v2Bypass++;
            }
            if (ringChecked != 12 || cageChecked != 12) throw new InvalidOperationException("The lava and cage checks must run on every sampled layout.");
            if (v2Bypass == 0) throw new InvalidOperationException("The frozen v2 graph is expected to show the cage bypass to the roof in at least one sampled layout (that is the defect).");
        }

        private static T WithoutItem<T>(Connected connected, string item, Func<T> check)
        {
            var removed = new List<string>();
            foreach (var node in connected.Graph.graph.Values)
                if (node.Items.Remove(item)) removed.Add(node.Area);
            try { return check(); }
            finally { foreach (string area in removed) connected.Graph.graph[area].Items.Add(item); }
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

        internal sealed class Connected
        {
            public Graph Graph;
            public RandomizerOptions Options;
            public Annotations Annotations;
        }

        private static Annotations ConnectedAnnotations(Connected connected) => connected.Annotations;

        /// <param name="replayLayoutFrom">When set, the graph is not randomized: the connections of that already connected graph are copied onto this catalog's graph.</param>
        internal static Connected Connect(string catalog, uint seed, string[] enabledOptions, bool withoutBossArenaEntryRule = false, Connected replayLayoutFrom = null, bool withoutGapingWallCorrection = false)
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
                if (withoutBossArenaEntryRule) BossArenaEntryTest.StripBossArenaEntryRule(annotations);
                if (withoutGapingWallCorrection) BossArenaEntryTest.RestoreGapingWall(annotations);
                var graph = new Graph();
                graph.Construct(options, annotations);
                string staging = Path.Combine(scratch, "staging");
                CompactRealGenerationTest.CopyGameDataForAudit(Path.Combine(dataRoot, "dist", "DS1R"), staging, pristineRoot, "ENGLISH");
                Console.SetOut(TextWriter.Null);
                new ItemReader().FindItems(options, annotations, graph, staging, FromGame.DS1R);
                if (replayLayoutFrom != null) BossArenaEntryTest.ReplayLayout(replayLayoutFrom.Graph, graph);
                else new GraphConnector().Connect(options, graph, annotations);
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
