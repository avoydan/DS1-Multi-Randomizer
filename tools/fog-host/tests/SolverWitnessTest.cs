using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using FogMod;
using static FogMod.AnnotationData;

namespace FogHost.Tests
{
    /// <summary>
    /// The solver witness (docs/SOLVER_WITNESS.md): the proof the accepting search records of WHY it believes each Area reachable.
    /// Synthetic tests pin the mechanics (predecessor, both gate sides, conditions, key-item acquisition, glitched warning, determinism, JSON, and that the
    /// observer never changes the search). The data-backed tests replay three historical defects and check that the witness would have shown each one.
    /// </summary>
    internal static class SolverWitnessTest
    {
        private static readonly string[] OriginalOptions = { "traversableFogs", "bossFogs", "areaWarps", "scaling", "requireLordSouls" };

        // ---- synthetic world -------------------------------------------------------------------------------------------------------------------------

        private sealed class World
        {
            public Graph Graph;
            public Annotations Annotations;
            public RandomizerOptions Options;
            private readonly Dictionary<(Entrance, int), (Graph.Edge Exit, Graph.Edge Entrance)> edges = new Dictionary<(Entrance, int), (Graph.Edge, Graph.Edge)>();

            public (Graph.Edge Exit, Graph.Edge Entrance) Gate(Entrance entrance, int side)
            {
                if (edges.TryGetValue((entrance, side), out var cached)) return cached;
                Side s = side == 0 ? entrance.ASide : entrance.BSide;
                var exit = new Graph.Edge { Type = Graph.EdgeType.Exit, From = s.Area, Name = entrance.Name, Text = entrance.Text, Side = s };
                var arrival = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = s.Area, Name = entrance.Name, Text = entrance.Text, Side = s };
                exit.Pair = arrival;
                arrival.Pair = exit;
                Graph.graph[s.Area].To.Add(exit);
                Graph.graph[s.Area].From.Add(arrival);
                return edges[(entrance, side)] = (exit, arrival);
            }
        }

        private static Side SideOf(string area, int bossTrigger = 0) => new Side { Area = area, BossTrigger = bossTrigger };

        private static Entrance MakeEntrance(string name, Side a, Side b, string tags) => new Entrance { Name = name, Text = name + "-text", ASide = a, BSide = b, Tags = tags };

        /// <summary>
        /// start --fixed--> bell --fixed (needs arena)--> arena; start --(vanilla fog "gate")--> arena (boss-encounter side); arena --(vanilla fog "exit")--> world;
        /// start --(shuffled fog p->q)--> annex; world --fixed (needs boss_key AND arena)--> vault, boss_key lies in arena; start --hard in-map link--> secret.
        /// </summary>
        private static World BuildWorld(bool withHardLink = true, bool hardAllowed = true)
        {
            string[] names = { "start", "bell", "arena", "world", "vault", "annex", "secret", "hub", "ladder" };
            var areas = names.Select(name => new Area { Name = name, Tags = name == "arena" ? "boss" : null }).ToList();
            var graph = new Graph
            {
                areas = areas.ToDictionary(a => a.Name, a => a, StringComparer.Ordinal),
                itemAreas = new Dictionary<string, string>(StringComparer.Ordinal) { ["boss_key"] = "arena" },
                bossCompletions = new Dictionary<string, BossCompletionRule>(StringComparer.Ordinal),
                graph = names.ToDictionary(name => name, name => new Graph.Node { Area = name, Cost = name == "arena" ? 3 : 1 }, StringComparer.Ordinal),
                start = new CustomStart { Name = "Start", Area = "start" },
                entranceIds = new Dictionary<string, Entrance>(StringComparer.Ordinal),
            };
            graph.graph["arena"].Items.Add("boss_key");
            var annotations = new Annotations
            {
                Areas = areas, KeyItems = new List<Item> { new Item { Name = "boss_key", ID = "4:2003", Area = "arena" } },
                Warps = new List<Entrance>(), Entrances = new List<Entrance>(),
            };
            var world = new World { Graph = graph, Annotations = annotations, Options = new RandomizerOptions { Seed = 1 } };
            world.Options["hard"] = hardAllowed;

            void Fixed(string from, string to, string condition, string tags = null)
            {
                Expr expr = condition == null ? null : Expr.Named(condition);
                var exit = new Graph.Edge { Type = Graph.EdgeType.Exit, From = from, Side = new Side { Area = from, Tags = tags }, IsFixed = true, Expr = expr, Text = tags == "hard" ? "hard skip" : "in map" };
                var entrance = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = to, Side = new Side { Area = to, Tags = tags }, IsFixed = true, Expr = expr, Text = exit.Text };
                graph.graph[from].To.Add(exit);
                graph.graph[to].From.Add(entrance);
                graph.connect(exit, entrance);
            }
            Fixed("start", "bell", null);
            Fixed("bell", "arena", "arena");
            Fixed("arena", "bell", null);
            // Two conditions: a key item and an Area.
            {
                var both = new Expr(new List<Expr> { Expr.Named("boss_key"), Expr.Named("arena") }, true);
                var exit = new Graph.Edge { Type = Graph.EdgeType.Exit, From = "world", Side = new Side { Area = "world" }, IsFixed = true, Expr = both, Text = "in map" };
                var entrance = new Graph.Edge { Type = Graph.EdgeType.Entrance, To = "vault", Side = new Side { Area = "vault" }, IsFixed = true, Expr = both, Text = "in map" };
                graph.graph["world"].To.Add(exit);
                graph.graph["vault"].From.Add(entrance);
                graph.connect(exit, entrance);
            }
            if (withHardLink) Fixed("start", "secret", null, "hard");
            // Two opposite in-map links (a way back that is always there), and one whose way back needs the key.
            Fixed("start", "hub", null);
            Fixed("hub", "start", null);
            Fixed("start", "ladder", null);
            Fixed("ladder", "start", "boss_key");

            Entrance gate = MakeEntrance("gate", SideOf("start"), SideOf("arena", bossTrigger: 11), "boss");
            Entrance exitGate = MakeEntrance("exit", SideOf("arena"), SideOf("world"), "boss");
            Entrance p = MakeEntrance("p", SideOf("start"), SideOf("bell"), "world");
            Entrance q = MakeEntrance("q", SideOf("annex"), SideOf("world"), "world");
            foreach (Entrance entrance in new[] { gate, exitGate, p, q }) { graph.entranceIds[entrance.Name] = entrance; annotations.Entrances.Add(entrance); }
            // Vanilla pairing: the exit side of the gate on the start side leads to its own other side.
            graph.connect(world.Gate(gate, 0).Exit, world.Gate(gate, 1).Entrance);
            graph.connect(world.Gate(exitGate, 0).Exit, world.Gate(exitGate, 1).Entrance);
            // Shuffled pairing: gate p (start side) joins gate q (annex side).
            graph.connect(world.Gate(p, 0).Exit, world.Gate(q, 0).Entrance);
            return world;
        }

        private static string Summary(SolverWitness.WitnessDto document) =>
            $"target {document.Target.Area}; route [{string.Join(", ", document.MainRoute.Select(s => s.Type + "@" + s.Area))}]; tree [{string.Join(", ", document.ReachTree.Select(r => r.Area))}]";

        private static (SolverWitness.WitnessDto Document, GraphChecker.CheckRecord Check) Witness(World world)
        {
            var observer = new WitnessObserver();
            GraphChecker.CheckRecord check = new GraphChecker().Check(world.Options, world.Graph, "start", null, null, observer);
            return (SolverWitness.BuildDocument(world.Options, world.Graph, world.Annotations, check, observer, "compact-v3"), check);
        }

        /// <summary>The observer is read-only: with and without it the search returns the same records, in the same state.</summary>
        public static void VerifyObserverDoesNotChangeTheSearch()
        {
            World withObserver = BuildWorld();
            World without = BuildWorld();
            GraphChecker.CheckRecord plain = new GraphChecker().Check(without.Options, without.Graph, "start");
            (_, GraphChecker.CheckRecord observed) = Witness(withObserver);
            if (!plain.Records.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(observed.Records.Keys.OrderBy(k => k, StringComparer.Ordinal))
                || !plain.Unvisited.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(observed.Unvisited.OrderBy(k => k, StringComparer.Ordinal))
                || plain.Records.Any(entry => entry.Value.Dist != observed.Records[entry.Key].Dist))
                throw new InvalidOperationException("Observing the search must not change its result.");
        }

        public static void VerifyPredecessorsAreFirstReachInSolverOrder()
        {
            (SolverWitness.WitnessDto document, GraphChecker.CheckRecord check) = Witness(BuildWorld());
            if (check.Unvisited.Count != 0 || document.Claim.AreasReached != document.Claim.AreasTotal || document.Claim.Unreached.Length != 0)
                throw new InvalidOperationException("The synthetic world must be completely reachable.");
            var sequence = document.ReachTree.ToDictionary(r => r.Area, r => r.Sequence, StringComparer.Ordinal);
            if (document.ReachTree[0].Area != "start" || document.ReachTree[0].Edge != null || document.ReachTree[0].Sequence != 0)
                throw new InvalidOperationException("The start Area is the first, edge-less entry of the proof.");
            foreach (SolverWitness.ReachDto reach in document.ReachTree.Skip(1))
            {
                if (!sequence.TryGetValue(reach.FromArea, out int fromSequence) || fromSequence >= reach.Sequence)
                    throw new InvalidOperationException($"{reach.Area} must be proved from an Area proved earlier (from {reach.FromArea}).");
                if (reach.Edge.Exit.Area != reach.FromArea || reach.Edge.Arrival.Area != reach.Area)
                    throw new InvalidOperationException($"The edge that reached {reach.Area} must leave {reach.FromArea} and arrive at {reach.Area}.");
            }
            // The Area that was only reachable through the key condition got its predecessor from the condition-satisfied state, not from a shorter walk.
            SolverWitness.ReachDto vault = document.ReachTree.Single(r => r.Area == "vault");
            if (vault.FromArea != "world") throw new InvalidOperationException("The vault is reached from the world.");
        }

        public static void VerifyBothGateSidesAreKept()
        {
            (SolverWitness.WitnessDto document, _) = Witness(BuildWorld());
            // The arena is reached either by the gate or by the bell; the first proof is whichever the solver expanded first, so look at the gate edge directly.
            SolverWitness.EdgeDto viaGate = document.ReachTree.Select(r => r.Edge).Where(e => e != null).FirstOrDefault(e => e.Exit.Object == "gate");
            if (viaGate == null) throw new InvalidOperationException("The gate must be on the proof tree.");
            if (viaGate.Exit.Area != "start" || viaGate.Exit.OtherSideArea != "arena" || viaGate.Arrival.Area != "arena" || viaGate.Arrival.OtherSideArea != "start"
                || !viaGate.Arrival.BossTriggerSide || viaGate.Exit.BossTriggerSide || !viaGate.Arrival.AreaIsBossArena || viaGate.Pairing != "vanilla"
                || viaGate.Kind != "randomized-fog" || !viaGate.Qualifiers.Contains("boss") || viaGate.OneWay)
                throw new InvalidOperationException("A boss fog must show both sides, the encounter side, the boss arena and its pairing.");
            // The shuffled pair joins two different objects.
            SolverWitness.EdgeDto shuffled = document.ReachTree.Select(r => r.Edge).First(e => e != null && e.Exit.Object == "p");
            if (shuffled.Arrival.Object != "q" || shuffled.Pairing != "shuffled" || shuffled.Exit.Area != "start" || shuffled.Arrival.Area != "annex"
                || shuffled.Exit.Text != "p-text" || shuffled.Arrival.Text != "q-text")
                throw new InvalidOperationException("A shuffled fog must show the exit object and the DIFFERENT arrival object with their own descriptions.");
        }

        public static void VerifyConditionsAndItemAcquisitionAreShown()
        {
            (SolverWitness.WitnessDto document, _) = Witness(BuildWorld());
            SolverWitness.EdgeDto toVault = document.ReachTree.Single(r => r.Area == "vault").Edge;
            if (toVault.Requires != "(boss_key AND arena)" && toVault.Requires != "(arena AND boss_key)")
                throw new InvalidOperationException($"The vault link's condition must be shown as the solver evaluated it; got {toVault.Requires}.");
            SolverWitness.RequirementDto key = toVault.Requirements.Single(r => r.Name == "boss_key");
            SolverWitness.RequirementDto arena = toVault.Requirements.Single(r => r.Name == "arena");
            if (key.Kind != "key-item" || key.ReachedInArea != "arena" || arena.Kind != "area" || key.SatisfiedAtSequence is null || arena.SatisfiedAtSequence is null
                || !toVault.SatisfiedBy.Contains("boss_key") || !toVault.SatisfiedBy.Contains("arena"))
                throw new InvalidOperationException("Both terms must be reported with their kind, where they come from and that they were satisfied.");
            // The target is the Area with the largest solver distance (the synthetic graph has no Gwyn arena); the route must pass the key where the solver believes it lies.
            string target = document.Target.Area;
            if (target != document.Claim.StartArea && document.MainRoute.All(s => s.Area != target)) throw new InvalidOperationException("The route must end at its target.");
            SolverWitness.StepDto[] steps = document.MainRoute;
            int acquire = Array.FindIndex(steps, s => s.Type == "acquire" && s.Item.Name == "boss_key");
            int cross = Array.FindIndex(steps, s => s.Type == "cross" && s.Edge.Arrival.Area == "vault");
            if (cross < 0 && target == "vault") throw new InvalidOperationException("The vault is the target so the route must cross into it.");
            if (acquire < 0 && target == "vault") throw new InvalidOperationException("The route to the vault must show where the key is acquired: " + Summary(document));
            if (acquire >= 0 && (steps[acquire].Area != "arena" || steps[acquire].Item.SolverArea != "arena" || steps[acquire].Item.GameItemId != "4:2003"))
                throw new InvalidOperationException("The acquisition step must name the Area that holds the item.");
            if (cross >= 0 && acquire >= 0 && acquire > cross) throw new InvalidOperationException("The acquisition must come before the step that needs the key.");
        }

        /// <summary>
        /// "One-way" is a fact about the graph, not about a missing pair: an in-map link has no pair but its way back is a separate edge of the other Area. The witness
        /// reads that edge (and its condition) instead of calling every in-map link one-way.
        /// </summary>
        public static void VerifyWayBackIsReadFromTheGraph()
        {
            (SolverWitness.WitnessDto document, _) = Witness(BuildWorld());
            SolverWitness.EdgeDto To(string area) => document.ReachTree.Single(r => r.Area == area).Edge;
            SolverWitness.EdgeDto hub = To("hub");
            if (hub.OneWay || hub.Reverse != "unconditional" || hub.ReverseVia != "in map" || hub.ReverseCondition != null)
                throw new InvalidOperationException("Two opposite unconditional in-map links are TWO-WAY: " + hub.Reverse);
            SolverWitness.EdgeDto ladder = To("ladder");
            if (ladder.OneWay || ladder.Reverse != "conditional" || ladder.ReverseCondition != "boss_key")
                throw new InvalidOperationException("A way back that needs the key is two-way, conditional, with its condition shown.");
            SolverWitness.EdgeDto bell = To("bell");
            if (!bell.OneWay || bell.Reverse != "none" || bell.ReverseCondition != null || bell.ReverseVia != null)
                throw new InvalidOperationException("An in-map link with no edge back is one-way.");
            // A gate: the way back is the pair on the same object; a shuffled gate's way back is the arrival object's own exit.
            SolverWitness.EdgeDto vanilla = document.ReachTree.Select(r => r.Edge).First(e => e != null && e.Exit.Object == "gate");
            if (vanilla.OneWay || vanilla.Reverse != "unconditional" || vanilla.ReverseVia != "gate")
                throw new InvalidOperationException("A vanilla fog gate is crossed back through the same object.");
            SolverWitness.EdgeDto shuffled = document.ReachTree.Select(r => r.Edge).First(e => e != null && e.Exit.Object == "p");
            if (shuffled.OneWay || shuffled.ReverseVia != "q")
                throw new InvalidOperationException("A shuffled gate is crossed back through the arrival object.");
        }

        /// <summary>The route is walked greedily: steps that leave the Area the player stands in come first, so a return is only listed when nothing else is ready.</summary>
        public static void VerifyRouteDoesNotJumpBetweenBranchesNeedlessly()
        {
            (SolverWitness.WitnessDto document, _) = Witness(BuildWorld());
            string standing = null;
            foreach (SolverWitness.StepDto step in document.MainRoute.Where(s => s.Type == "start" || s.Type == "cross"))
            {
                if (step.Type == "cross" && step.ReturnTo != null)
                {
                    // A return is listed only when no later step of the route leaves the Area the player stands in AND is ready: here that means every later step that
                    // leaves `standing` needs something the route has not provided yet.
                    var provided = new HashSet<string>(document.MainRoute.TakeWhile(s => s != step).Where(s => s.Type == "start" || s.Type == "cross").Select(s => s.Area), StringComparer.Ordinal);
                    var held = new HashSet<string>(document.MainRoute.TakeWhile(s => s != step).Where(s => s.Type == "acquire").Select(s => s.Item.Name), StringComparer.Ordinal);
                    foreach (SolverWitness.StepDto later in document.MainRoute.SkipWhile(s => s != step).Skip(1).Where(s => s.Type == "cross" && s.Edge.Exit.Area == standing))
                    {
                        bool ready = provided.Contains(later.Edge.Exit.Area) && later.Edge.Requirements.All(r => r.Kind == "key-item" ? held.Contains(r.Name) : provided.Contains(r.Name));
                        if (ready) throw new InvalidOperationException($"Step {step.Number} returns to {step.ReturnTo} although step {later.Number} could leave {standing} first.");
                    }
                }
                standing = step.Type == "start" ? step.Area : step.Edge.Arrival.Area;
            }
        }

        public static void VerifyMainRouteIsSelfConsistent()
        {
            (SolverWitness.WitnessDto document, _) = Witness(BuildWorld());
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var held = new HashSet<string>(StringComparer.Ordinal);
            string where = null;
            foreach (SolverWitness.StepDto step in document.MainRoute)
            {
                if (step.Type == "start") { reached.Add(step.Area); where = step.Area; continue; }
                if (step.Type == "acquire") { if (!reached.Contains(step.Area)) throw new InvalidOperationException("An item is acquired only in an Area already reached."); held.Add(step.Item.Name); continue; }
                if (!reached.Contains(step.Edge.Exit.Area)) throw new InvalidOperationException($"Step {step.Number} leaves {step.Edge.Exit.Area}, which no earlier step reached.");
                if ((step.ReturnTo == null) == (where != step.Edge.Exit.Area)) throw new InvalidOperationException($"Step {step.Number} must say when the player first has to return.");
                foreach (SolverWitness.RequirementDto requirement in step.Edge.Requirements)
                {
                    bool met = requirement.Kind == "key-item" ? held.Contains(requirement.Name) : reached.Contains(requirement.Name);
                    if (!met) throw new InvalidOperationException($"Step {step.Number} needs {requirement.Name} before it is listed.");
                }
                reached.Add(step.Edge.Arrival.Area);
                where = step.Edge.Arrival.Area;
            }
            if (!reached.Contains(document.Target.Area)) throw new InvalidOperationException("The route must end at its target.");
        }

        public static void VerifyGlitchedRouteIsMarkedAndAbsentWhenOff()
        {
            (SolverWitness.WitnessDto on, _) = Witness(BuildWorld());
            SolverWitness.EdgeDto hard = on.ReachTree.Single(r => r.Area == "secret").Edge;
            if (!hard.Glitched || hard.Kind != "glitched-route" || !hard.Label.Contains("glitched", StringComparison.Ordinal))
                throw new InvalidOperationException("A route over a hard-tagged link must be marked as glitched logic.");
            if (!on.Claim.GlitchedLogicEnabled) throw new InvalidOperationException("The claim must state that glitched logic is on.");
            // With glitched logic off the graph has no hard link (Graph.Construct skips it), so a witness never contains one.
            (SolverWitness.WitnessDto off, _) = Witness(BuildWorld(withHardLink: false, hardAllowed: false));
            if (off.Claim.GlitchedLogicEnabled || off.ReachTree.Any(r => r.Edge != null && r.Edge.Glitched) || off.MainRoute.Any(s => s.Edge != null && s.Edge.Glitched))
                throw new InvalidOperationException("No glitched edge may appear when glitched logic is off.");
        }

        public static void VerifyDeterministicJsonRoundTrip()
        {
            byte[] first = SolverWitness.Serialize(Witness(BuildWorld()).Document);
            byte[] second = SolverWitness.Serialize(Witness(BuildWorld()).Document);
            if (!first.SequenceEqual(second)) throw new InvalidOperationException("The same graph must produce byte-identical witness JSON.");
            string text = Encoding.UTF8.GetString(first);
            if (text.Contains(":\\", StringComparison.Ordinal) || text.Contains("\"createdAt\"", StringComparison.Ordinal))
                throw new InvalidOperationException("The witness must carry no paths and no timestamps.");
            using JsonDocument parsed = JsonDocument.Parse(first);
            JsonElement root = parsed.RootElement;
            if (root.GetProperty("format").GetString() != SolverWitness.Format || root.GetProperty("schemaVersion").GetInt32() != SolverWitness.SchemaVersion
                || root.GetProperty("annotationSource").GetString() != "compact-v3" || root.GetProperty("mainRoute").GetArrayLength() < 3
                || root.GetProperty("reachTree").GetArrayLength() != 9 || root.GetProperty("claim").GetProperty("areasReached").GetInt32() != 9)
                throw new InvalidOperationException("The witness JSON must expose its format, schema, annotation source, the route and the full tree: " + Summary(Witness(BuildWorld()).Document));
            JsonElement firstFog = root.GetProperty("reachTree").EnumerateArray().First(r => r.GetProperty("edge").ValueKind == JsonValueKind.Object && r.GetProperty("edge").GetProperty("exit").GetProperty("object").ValueKind == JsonValueKind.String);
            if (!firstFog.GetProperty("edge").TryGetProperty("arrival", out JsonElement arrival) || !arrival.TryGetProperty("otherSideArea", out _) || !arrival.TryGetProperty("bossTriggerSide", out _))
                throw new InvalidOperationException("Both gate sides must serialize with their side details.");
        }

        // ---- data-backed historical defects ------------------------------------------------------------------------------------------------------------

        private static (SolverWitness.WitnessDto Document, GraphChecker.CheckRecord Check) Witness(StartReachabilityTest.Connected connected, string source)
        {
            var observer = new WitnessObserver();
            GraphChecker.CheckRecord check = new GraphChecker().Check(connected.Options, connected.Graph, connected.Graph.start.Area, null, null, observer);
            return (SolverWitness.BuildDocument(connected.Options, connected.Graph, connected.Annotations, check, observer, source), check);
        }

        /// <summary>
        /// Regression A (the original Asylum -> Ceaseless trap, frozen compact-v1, Fog seed 4009337993): the world is "reachable" only through unconditional fixed
        /// exits out of the Asylum hallway that the real game does not have. The witness proves every Area FROM the hallway with those links as the very first step.
        /// </summary>
        public static void VerifyWitnessExposesTheAsylumCeaselessTrap()
        {
            StartReachabilityTest.Connected v1 = StartReachabilityTest.Connect("v1", 4009337993, OriginalOptions);
            (SolverWitness.WitnessDto document, _) = Witness(v1, "compact-v1");
            SolverWitness.ReachDto[] leavingTheHallway = document.ReachTree.Where(r => r.Edge != null && r.Edge.Exit.Area == "asylum").ToArray();
            SolverWitness.ReachDto[] fake = leavingTheHallway.Where(r => r.Edge.Kind == "fixed-route" && r.Edge.Requires == null && !r.Edge.Randomized).ToArray();
            if (fake.Length == 0)
                throw new InvalidOperationException("The witness must show the unconditional fixed exit(s) out of the Asylum hallway that made the trap look solvable.");
            if (document.ReachTree.Count(r => r.Edge != null && r.Edge.Exit.Object == "o8952_0000" && r.Edge.Arrival.Area == "demonruins_ceaseless") > 1)
                throw new InvalidOperationException("The hallway wall into Ceaseless must appear at most once.");
            if (!fake.Any(r => r.Area == "firelink" || r.Area == "asylum_outside"))
                throw new InvalidOperationException("The fake exits of the frozen graph lead to Firelink and the Asylum exterior.");
            // The route to the deepest Area starts with such an exit: a human following it would be stopped at step 2 (there is no such exit in the game).
            SolverWitness.StepDto first = document.MainRoute.First(s => s.Type == "cross");
            if (first.Edge.Exit.Area != "asylum" || first.Edge.Kind != "fixed-route" || first.Edge.Requires != null)
                throw new InvalidOperationException("The first step of the old route must be the fake unconditional exit.");
        }

        /// <summary>
        /// Regression D (the owner's Quelaag layout, Fog seed 4038375671, before the arena rule): the route walks Demon Ruins -> bell room -> back into the arena
        /// by the fixed link and then leaves through the randomized boss-side fog o4950_0000. The witness shows the exit from the BOSS-ARENA side.
        /// </summary>
        public static void VerifyWitnessExposesTheQuelaagReverseFog()
        {
            StartReachabilityTest.Connected owner = StartReachabilityTest.Connect("v3", 4038375671, OriginalOptions, withoutBossArenaEntryRule: true, withoutGapingWallCorrection: true);
            (SolverWitness.WitnessDto document, _) = Witness(owner, "compact-v3");
            SolverWitness.ReachDto onward = document.ReachTree.Single(r => r.Area == "sens_mid");
            SolverWitness.EdgeDto edge = onward.Edge;
            if (edge.Exit.Object != "o4950_0000" || edge.Exit.Area != "blighttown_quelaag" || edge.Arrival.Area != "sens_mid" || !edge.Exit.AreaIsBossArena || edge.Kind != "randomized-fog")
                throw new InvalidOperationException("The witness must show the onward step as leaving the Quelaag ARENA by her randomized fog o4950_0000.");
            // And the arena itself was entered by the back link from the bell room (a fixed in-map link), not through a boss fog.
            SolverWitness.ReachDto arena = document.ReachTree.Single(r => r.Area == "blighttown_quelaag");
            if (arena.FromArea != "blighttown_bell" || arena.Edge.Kind != "fixed-route")
                throw new InvalidOperationException("The witness must show the arena reached from the bell room by the fixed link: the entry the game does not allow.");
            // The route lists both steps in order: bell -> arena, then arena -> sens_mid.
            SolverWitness.StepDto[] cross = document.MainRoute.Where(s => s.Type == "cross").ToArray();
            int back = Array.FindIndex(cross, s => s.Edge.Exit.Area == "blighttown_bell" && s.Edge.Arrival.Area == "blighttown_quelaag");
            int fog = Array.FindIndex(cross, s => s.Edge.Exit.Object == "o4950_0000");
            if (fog >= 0 && back >= 0 && back > fog) throw new InvalidOperationException("The arena must be entered before it is left.");
        }

        /// <summary>
        /// Regression E (Gaping Dragon, Fog seed 2654435761, before the wall correction): the Depths wall o1420_0000 is labelled as entering depths_gaping. The witness
        /// shows that wall arriving in the arena Area.
        /// </summary>
        public static void VerifyWitnessExposesTheGapingWall()
        {
            StartReachabilityTest.Connected before = StartReachabilityTest.Connect("v3", 2654435761, OriginalOptions, withoutGapingWallCorrection: true);
            (SolverWitness.WitnessDto document, _) = Witness(before, "compact-v3");
            SolverWitness.ReachDto arena = document.ReachTree.Single(r => r.Area == "depths_gaping");
            if (arena.Edge.Arrival.Object != "o1420_0000" || arena.Edge.Arrival.Area != "depths_gaping")
                throw new InvalidOperationException("The witness must show the arena entered through the Depths wall o1420_0000 (the mislabelled side).");
        }

        /// <summary>An accepted real v3 layout: the route is self-consistent and the witness is identical across two independent connections of the same Fog seed.</summary>
        public static void VerifyRealV3WitnessIsConsistentAndDeterministic()
        {
            StartReachabilityTest.Connected a = StartReachabilityTest.Connect("v3", 2873971204, OriginalOptions);
            StartReachabilityTest.Connected b = StartReachabilityTest.Connect("v3", 2873971204, OriginalOptions);
            (SolverWitness.WitnessDto first, GraphChecker.CheckRecord check) = Witness(a, "compact-v3");
            (SolverWitness.WitnessDto second, _) = Witness(b, "compact-v3");
            if (!SolverWitness.Serialize(first).SequenceEqual(SolverWitness.Serialize(second))) throw new InvalidOperationException("Two connections of the same Fog seed must give the same witness.");
            if (check.Unvisited.Count != 0 || first.Target.Area != SolverWitness.PreferredTarget) throw new InvalidOperationException("The accepted world's route targets the Gwyn arena.");
            var reached = new HashSet<string> { first.Claim.StartArea };
            var held = new HashSet<string>(StringComparer.Ordinal);
            foreach (SolverWitness.StepDto step in first.MainRoute.Skip(1))
            {
                if (step.Type == "acquire") { held.Add(step.Item.Name); continue; }
                if (step.Type == "boss-completion") continue;
                if (!reached.Contains(step.Edge.Exit.Area)) throw new InvalidOperationException($"Step {step.Number} leaves an Area no earlier step reached.");
                foreach (SolverWitness.RequirementDto requirement in step.Edge.Requirements.Where(r => r.Kind != "boss-completion"))
                    if (!(requirement.Kind == "key-item" ? held.Contains(requirement.Name) : reached.Contains(requirement.Name)))
                        throw new InvalidOperationException($"Step {step.Number} needs {requirement.Name}, which no earlier step provides.");
                reached.Add(step.Edge.Arrival.Area);
            }
            if (!reached.Contains(SolverWitness.PreferredTarget)) throw new InvalidOperationException("The route must reach the target.");
        }
    }
}
