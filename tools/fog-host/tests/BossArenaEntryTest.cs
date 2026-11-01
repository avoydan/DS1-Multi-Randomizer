using System;
using System.Collections.Generic;
using System.Linq;
using FogMod;
using static FogMod.AnnotationData;

namespace FogHost.Tests
{
    /// <summary>
    /// Regression coverage for the boss-arena entry rule of generation profile ds1tr-gen-v6 (Compact Fog v3).
    ///
    /// The owner's V6 world (Fog seed 4038375671) was accepted although the only route onward went through the back of Quelaag's boss fog while she was alive:
    /// Demon Ruins reaches Quelaag's bell room by a fixed route, the bell room had an unconditional fixed link back into the arena (AddLocalRoute is two-way, so
    /// every boss Area with a post-encounter route got one), and the arena's randomized boss-side fog led to the rest of the world. The game does not open that
    /// fog from inside while the boss lives. v3 asks for the arena to have been reached already before such a link can be used; the only other way in is the
    /// boss's own fog. The link out of the arena, forward-only encounter routes and every other Area are untouched.
    /// </summary>
    internal static class BossArenaEntryTest
    {
        private static readonly string[] OriginalOptions = { "traversableFogs", "bossFogs", "areaWarps", "scaling", "requireLordSouls" };
        private const uint OwnerFogSeed = 4038375671;

        /// <summary>The twelve post-encounter neighbours that own a link back into a boss arena, with that arena.</summary>
        private static readonly (string Neighbour, string Boss)[] BackEntries =
        {
            ("burg_posttaurus", "burg_taurus"), ("parish_bell", "parish_gargoyles"), ("darkroot_postbutterfly", "darkroot_butterfly"),
            ("dlc_sanctuary", "dlc_guardian"), ("dlc_hallway", "dlc_artorias"), ("dlc_township", "dlc_kalameetdeath"), ("dlc_top", "dlc_manus"),
            ("blighttown_bell", "blighttown_quelaag"), ("demonruins_stairs", "demonruins_firesage"), ("demonruins_izalithprelava", "demonruins_centipede"),
            ("anorlondo_start", "sens_irongolem"), ("newlondo_drakes", "newlondo_fourkings"),
        };

        // ---- test helpers used by StartReachabilityTest.Connect --------------------------------------------------------------------------------------------

        /// <summary>Removes the v3 boss-arena entry condition from annotations, giving the graph v3 had before the rule (the owner's build).</summary>
        public static void StripBossArenaEntryRule(Annotations annotations)
        {
            var bosses = new HashSet<string>(BackEntries.Select(entry => entry.Boss), StringComparer.Ordinal);
            foreach (Area area in annotations.Areas)
                foreach (Side side in area.To ?? new List<Side>())
                    if (bosses.Contains(side.Area) && side.Cond == side.Area) side.Cond = null;
        }

        /// <summary>Copies every randomized connection of a connected graph onto an unconnected graph built from the same Area and Entrance lists.</summary>
        public static void ReplayLayout(Graph source, Graph fresh)
        {
            foreach (string area in source.graph.Keys)
            {
                Graph.Node from = source.graph[area], to = fresh.graph[area];
                if (from.To.Count != to.To.Count || from.From.Count != to.From.Count)
                    throw new InvalidOperationException($"The replayed graph has a different edge count at {area}.");
                for (int index = 0; index < from.To.Count; index++)
                {
                    Graph.Edge oldExit = from.To[index], newExit = to.To[index];
                    if (oldExit.Name != newExit.Name || oldExit.IsFixed != newExit.IsFixed || oldExit.Side.Area != newExit.Side.Area)
                        throw new InvalidOperationException($"The replayed graph has a different exit at {area}[{index}].");
                    if (oldExit.IsFixed || newExit.Link != null) continue;
                    Graph.Node target = source.graph[oldExit.To];
                    int entranceIndex = target.From.IndexOf(oldExit.Link);
                    if (entranceIndex < 0) throw new InvalidOperationException($"The entrance of {area}[{index}] was not found at {oldExit.To}.");
                    Graph.Edge newEntrance = fresh.graph[oldExit.To].From[entranceIndex];
                    if (newEntrance.Name != oldExit.Link.Name || newEntrance.Link != null)
                        throw new InvalidOperationException($"The replayed entrance of {area}[{index}] does not match.");
                    fresh.connect(newExit, newEntrance);
                }
            }
            fresh.start = source.start;
        }

        // ---- catalog (real data) -------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// v3 asks for the boss arena to have been reached before a post-encounter neighbour can walk back into it; v2 keeps its frozen unconditional links; the way out of
        /// the arena stays free; forward-only encounter routes (Gwyndolin, the hard Gaping Dragon drop) are not back-entries and are untouched.
        /// </summary>
        public static void VerifyV3BossArenaEntryCatalog()
        {
            string dataRoot = RequiredDirectory("DS1TR_COMPACT_DATA_ROOT");
            string pristineRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineRoot, includeLegacyAnnotations: false);
            FogCompactCatalogV2 v2 = FogCompactCatalogBuilder.BuildV2(facts);
            FogCompactCatalogV3 v3 = FogCompactCatalogBuilder.BuildV3(facts);
            FogAreaLink Link(FogAnnotationCatalog catalog, string from, string to) => catalog.Areas.Single(area => area.Id.Value == from).To.SingleOrDefault(link => link.Destination.Value == to);

            foreach ((string neighbour, string boss) in BackEntries)
            {
                if (Link(v2.Catalog, neighbour, boss) is not { Condition: null })
                    throw new InvalidOperationException($"The frozen v2 back-entry {neighbour} > {boss} must still exist and be unconditional (profile ds1tr-gen-v5 stays byte-compatible).");
                FogAreaLink gate = Link(v3.Catalog, neighbour, boss);
                if (gate?.Condition is not { Kind: FogConditionKind.Atom, Terms.Count: 1 } || gate.Condition.Terms[0] != FogConditionReference.Area(new FogAreaId(boss)))
                    throw new InvalidOperationException($"v3 must require {boss} to be reached before {neighbour} can walk back into it.");
                if (Link(v3.Catalog, boss, neighbour) is not { Condition: null })
                    throw new InvalidOperationException($"v3 must keep the way out of {boss} to {neighbour} free.");
            }

            // Nothing else changed: exactly these twelve links carry an Area condition on their own destination boss, and the two forward-only routes are as in v2.
            int bossConditioned = v3.Catalog.Areas.SelectMany(area => area.To).Count(link => link.Condition is { Kind: FogConditionKind.Atom, Terms.Count: 1 }
                && link.Condition.Terms[0] == FogConditionReference.Area(link.Destination)
                && FogProjectAnnotationPolicy.BossAreaTagPolicies.Any(policy => policy.Area == link.Destination));
            if (bossConditioned != BackEntries.Length) throw new InvalidOperationException($"v3 must condition exactly {BackEntries.Length} boss-arena links, found {bossConditioned}.");
            foreach ((string from, string to) in new[] { ("anorlondo_darkmoon", "anorlondo_gwyndolin"), ("depths", "depths_gaping") })
                if (Link(v3.Catalog, from, to) is not { Condition: null } || Link(v2.Catalog, from, to) is not { Condition: null })
                    throw new InvalidOperationException($"The forward-only encounter route {from} > {to} must stay unconditional in v2 and v3.");
            if (v3.Fingerprint == v2.Fingerprint) throw new InvalidOperationException("The v3 catalog must have its own fingerprint.");
        }

        // ---- the owner's layout --------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Replays the owner's V6 world on the corrected graph. The owner's build accepted it (every Area reachable); with Quelaag's arena exit unusable from inside only
        /// four Areas are reachable. The corrected graph rejects the same layout with a retryable Fog failure, and the same Fog seed on the corrected catalog yields a
        /// valid layout that does not depend on walking back into a boss arena.
        /// </summary>
        public static void VerifyOwnerQuelaagLayoutIsRejected()
        {
            // The owner's build: v3 before the rule. Its connector decisions are deterministic, so Fog seed 4038375671 reproduces the world the owner played.
            StartReachabilityTest.Connected owner = StartReachabilityTest.Connect("v3", OwnerFogSeed, OriginalOptions, withoutBossArenaEntryRule: true);
            string[] hallwayExits = owner.Graph.graph["asylum"].To.Where(edge => !edge.IsFixed).Select(edge => edge.To).ToArray();
            Graph.Edge quelaagFog = owner.Graph.graph["blighttown_quelaag"].To.Single(edge => !edge.IsFixed);
            if (hallwayExits.Length != 1 || hallwayExits[0] != "demonruins" || quelaagFog.To != "sens_mid" || quelaagFog.Name != "o4950_0000")
                throw new InvalidOperationException("The owner's layout must pair the Asylum wall with Demon Ruins and Quelaag's boss-side fog with sens_mid.");
            if (new GraphChecker().Check(owner.Options, owner.Graph, "asylum").Unvisited.Count != 0)
                throw new InvalidOperationException("The owner's build accepted this layout (every Area reachable); that is the defect.");
            GraphChecker.CheckRecord trapped = new GraphChecker().Check(owner.Options, owner.Graph, "asylum", null, edge => !(edge.From == "blighttown_quelaag" && !edge.IsFixed));
            string[] reached = trapped.Records.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (!reached.SequenceEqual(new[] { "asylum", "blighttown_bell", "blighttown_quelaag", "demonruins" }) || trapped.Unvisited.Count < 100 || !trapped.Unvisited.Contains("sens_mid"))
                throw new InvalidOperationException($"Without crossing Quelaag's fog from inside the owner's start reaches only its four-Area pocket; reached [{string.Join(", ", reached)}].");

            // The same layout on the corrected graph is rejected, naming the unreachable Areas.
            StartReachabilityTest.Connected replayed = StartReachabilityTest.Connect("v3", OwnerFogSeed, OriginalOptions, replayLayoutFrom: owner);
            FogHostException rejected = Capture(() => StartReachabilityValidator.Validate(replayed.Options, replayed.Graph));
            if (rejected?.Code != HostErrors.SeedUnsolvable || !rejected.Retryable || rejected.RetryScope != "fog" || rejected.Stage != "solve-graph"
                || !rejected.Message.Contains("sens_mid", StringComparison.Ordinal))
                throw new InvalidOperationException("The owner's layout must be rejected as a retryable Fog failure on the corrected graph.");

            // The same Fog seed on the corrected catalog is a different, valid layout: the connector repairs toward it.
            StartReachabilityTest.Connected fixedLayout = StartReachabilityTest.Connect("v3", OwnerFogSeed, OriginalOptions);
            AssertNoBackEntryDependence(fixedLayout, "the owner's Fog seed on the corrected catalog");
            StartReachabilityValidator.Validate(fixedLayout.Options, fixedLayout.Graph);
        }

        /// <summary>
        /// On real corrected layouts every Area stays reachable when no link back into a boss arena may be used at all: after the arena has been reached such a link
        /// adds nothing, so a layout that needs one before that has been rejected or repaired. The frozen graph without the rule fails the same property on a real layout.
        /// </summary>
        public static void VerifyNoBackEntryDependenceOnRealLayouts()
        {
            for (uint seed = 1; seed <= 10; seed++)
                AssertNoBackEntryDependence(StartReachabilityTest.Connect("v3", seed, OriginalOptions), $"Fog seed {seed}");
        }

        private static void AssertNoBackEntryDependence(StartReachabilityTest.Connected connected, string what)
        {
            string start = connected.Graph.start.Area;
            GraphChecker.CheckRecord all = new GraphChecker().Check(connected.Options, connected.Graph, start);
            GraphChecker.CheckRecord withoutBackEntries = new GraphChecker().Check(connected.Options, connected.Graph, start, null, IsNotBackEntry);
            if (all.Unvisited.Count != 0 || withoutBackEntries.Unvisited.Count != 0 || withoutBackEntries.Records.Count != all.Records.Count)
                throw new InvalidOperationException($"{what}: the layout must not depend on walking back into a boss arena (reachable {all.Records.Count} versus {withoutBackEntries.Records.Count} without those links).");
        }

        /// <summary>A fixed link whose condition asks for its own destination Area to be reached: the v3 boss-arena back-entry.</summary>
        private static bool IsNotBackEntry(Graph.Edge edge) =>
            !(edge.IsFixed && edge.LinkedExpr != null && edge.To != null && edge.LinkedExpr.FreeVars().Contains(edge.To));

        // ---- synthetic graph (no game data) --------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The Quelaag shape: start -> bell by a fixed route, bell <-> arena fixed (the way back in conditioned on the arena), arena <-> world by a randomized fog.
        /// Without a fog into the arena the world is unreachable and rejected; with the fog the arena is reached through its gate, the way back in becomes usable, and
        /// the world is reachable. Without the condition the same trapped layout looks fine.
        /// </summary>
        public static void VerifySyntheticBossArenaEntry()
        {
            // The defect: no condition on the way back in.
            Graph unconditioned = BuildSynthetic(withGate: false, conditioned: false);
            if (new GraphChecker().Check(Options(), unconditioned, "start").Unvisited.Count != 0)
                throw new InvalidOperationException("Without the condition the trapped layout looks reachable (the defect).");

            // The rule: the same layout is rejected, retryably, naming the arena side of the world.
            Graph trapped = BuildSynthetic(withGate: false, conditioned: true);
            FogHostException rejected = Capture(() => StartReachabilityValidator.Validate(Options(), trapped));
            if (rejected?.Code != HostErrors.SeedUnsolvable || !rejected.Retryable || rejected.RetryScope != "fog"
                || !rejected.Message.Contains("arena", StringComparison.Ordinal) || !rejected.Message.Contains("world", StringComparison.Ordinal))
                throw new InvalidOperationException("A layout whose only way into the boss arena is the link back from its bell room must be rejected as a retryable Fog failure.");

            // The legitimate traversal: the arena is entered through its own fog first; then the link back in is allowed and the world is reachable.
            Graph legitimate = BuildSynthetic(withGate: true, conditioned: true);
            StartReachabilityValidator.Validate(Options(), legitimate);
            GraphChecker.CheckRecord record = new GraphChecker().Check(Options(), legitimate, "start");
            if (!record.Records["arena"].InEdge.Keys.Any(edge => edge.From == "bell" && edge.IsFixed))
                throw new InvalidOperationException("After the arena was reached through its gate the way back in from the bell room must be usable.");
            if (new GraphChecker().Check(Options(), legitimate, "start", null, IsNotBackEntry).Unvisited.Count != 0)
                throw new InvalidOperationException("The legitimate layout must not need the way back in.");
        }

        private static Graph BuildSynthetic(bool withGate, bool conditioned)
        {
            string[] names = { "start", "bell", "arena", "world" };
            var graph = new Graph
            {
                areas = names.ToDictionary(name => name, name => new Area { Name = name }, StringComparer.Ordinal),
                itemAreas = new Dictionary<string, string>(StringComparer.Ordinal),
                bossCompletions = new Dictionary<string, BossCompletionRule>(StringComparer.Ordinal),
                graph = names.ToDictionary(name => name, name => new Graph.Node { Area = name }, StringComparer.Ordinal),
                start = new CustomStart { Name = "Start", Area = "start" },
            };
            ConnectFixed(graph, "start", "bell", null);
            ConnectFixed(graph, "arena", "bell", null);
            ConnectFixed(graph, "bell", "arena", conditioned ? "arena" : null);
            if (withGate) ConnectFog(graph, "start", "arena", "gate");
            ConnectFog(graph, "arena", "world", "exit");
            return graph;
        }

        private static void ConnectFixed(Graph graph, string from, string to, string condition)
        {
            Expr expr = condition == null ? null : Expr.Named(condition);
            var exit = new Graph.Edge { Type = Graph.EdgeType.Exit, From = from, Side = new Side { Area = from }, IsFixed = true, Expr = expr };
            var entrance = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = to, Side = new Side { Area = to }, IsFixed = true, Expr = expr };
            graph.graph[from].To.Add(exit);
            graph.graph[to].From.Add(entrance);
            graph.connect(exit, entrance);
        }

        private static void ConnectFog(Graph graph, string first, string second, string name)
        {
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
            if (string.IsNullOrWhiteSpace(value) || !System.IO.Directory.Exists(value)) throw new InvalidOperationException($"{variable} must name an existing directory.");
            return System.IO.Path.GetFullPath(value);
        }
    }
}
