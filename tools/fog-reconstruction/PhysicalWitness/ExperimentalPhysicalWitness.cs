using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FogReconstruction.PhysicalWitness
{
    public enum PhysicalEdgeCertainty
    {
        PROVEN_ACTIONABLE,
        PROVEN_NON_ACTIONABLE,
        UNKNOWN
    }

    public enum PhysicalWitnessVerdict
    {
        PHYSICALLY_SOLVABLE_PROVEN,
        PHYSICALLY_UNSOLVABLE_PROVEN,
        UNRESOLVED_MODEL_GAP
    }

    public sealed record PhysicalNode(string Map, string Anchor);
    public sealed record PhysicalEvidence(string Kind, string Locator, string Sha256);

    public sealed record PhysicalEdge(
        string Id,
        PhysicalNode From,
        PhysicalNode To,
        PhysicalEdgeCertainty Certainty,
        string Mechanism,
        string Condition,
        IReadOnlyList<int> RequiredEventFlags,
        IReadOnlyList<int> ForbiddenEventFlags,
        IReadOnlyList<int> RequiredItems,
        IReadOnlyList<int> SetEventFlags,
        IReadOnlyList<int> ClearEventFlags,
        IReadOnlyList<PhysicalEvidence> Evidence)
    {
        public PhysicalEdgeCertification Certification { get; init; }
    }

    public sealed record PhysicalCoverage(
        string Scope,
        bool Complete,
        IReadOnlyList<PhysicalEvidence> Evidence);

    public sealed record PhysicalQuery(
        PhysicalNode Start,
        PhysicalNode Goal,
        IReadOnlyList<int> InitialEventFlags,
        IReadOnlyList<int> InitialItems,
        IReadOnlyList<int> RequiredGoalEventFlags,
        IReadOnlyList<PhysicalNode> RequiredWaypoints,
        int MaxStates);

    public sealed record PhysicalWitnessStep(
        string EdgeId,
        string Mechanism,
        PhysicalNode From,
        PhysicalNode To,
        int[] EventFlagsBefore,
        int[] EventFlagsAfter,
        int[] ItemsBefore,
        int[] ItemsAfter,
        PhysicalEvidence[] Evidence)
    {
        public PhysicalEdgeCertification Certification { get; init; }
    }

    public sealed record PhysicalWitnessResult(
        PhysicalWitnessVerdict Verdict,
        PhysicalWitnessStep[] Steps,
        string RouteClassFingerprint,
        int VisitedStates,
        bool ReachableUnknownEscape,
        string Explanation);

    /// <summary>
    /// Pure, development-only search over independently evidenced physical edges.
    /// It has no production validator or generation dependency.
    /// </summary>
    public static class ExperimentalPhysicalWitness
    {
        private static readonly string[] RequiredActionProofKinds =
        {
            "SourceActionRegion", "GeneratedDispatcher", "VerifiedDestinationPlayer"
        };
        private static readonly string[] RequiredSeathProofKinds =
        {
            "VerifiedSourceObject", "SeathInteractionChain", "GeneratedDispatcher", "VerifiedDestinationPlayer",
            "VerifiedDestinationMap", "VerifiedLanding"
        };

        private static readonly HashSet<string> NonActionProofKinds = new(StringComparer.Ordinal)
        {
            "VerifiedActionDisabled", "VerifiedGateBlock", "GameplayTelemetry"
        };

        private sealed record SearchState(
            PhysicalNode Node,
            HashSet<int> Flags,
            HashSet<int> Items,
            ulong Waypoints,
            IReadOnlyList<PhysicalWitnessStep> Steps);

        public static PhysicalWitnessResult Evaluate(
            PhysicalQuery query,
            IReadOnlyList<PhysicalEdge> edges,
            PhysicalCoverage coverage)
        {
            ValidateQuery(query);
            ValidateCoverage(coverage);
            ValidateEdges(edges);

            var bySource = edges
                .GroupBy(edge => edge.From)
                .ToDictionary(group => group.Key, group => group.OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray());

            var initialFlags = new HashSet<int>(query.InitialEventFlags);
            var initialItems = new HashSet<int>(query.InitialItems);
            ulong initialWaypoints = UpdateWaypoints(query.Start, query.RequiredWaypoints, 0);
            var queue = new Queue<SearchState>();
            queue.Enqueue(new SearchState(query.Start, initialFlags, initialItems, initialWaypoints, Array.Empty<PhysicalWitnessStep>()));
            var visited = new HashSet<string>(StringComparer.Ordinal) { StateKey(query.Start, initialFlags, initialItems, initialWaypoints) };
            var stateCount = 0;
            var unknownEscape = false;
            var budgetExceeded = false;

            while (queue.Count > 0)
            {
                if (stateCount >= query.MaxStates)
                {
                    budgetExceeded = true;
                    break;
                }

                SearchState state = queue.Dequeue();
                stateCount++;
                if (IsGoal(state, query))
                {
                    PhysicalWitnessStep[] steps = state.Steps.ToArray();
                    return new PhysicalWitnessResult(
                        PhysicalWitnessVerdict.PHYSICALLY_SOLVABLE_PROVEN,
                        steps,
                        Fingerprint(steps),
                        stateCount,
                        unknownEscape,
                        "A directed path was replayed using only PROVEN_ACTIONABLE edges with satisfied conditions.");
                }

                if (!bySource.TryGetValue(state.Node, out PhysicalEdge[] outgoing))
                    continue;

                foreach (PhysicalEdge edge in outgoing)
                {
                    if (edge.Certainty == PhysicalEdgeCertainty.UNKNOWN)
                    {
                        unknownEscape = true;
                        continue;
                    }

                    if (edge.Certainty == PhysicalEdgeCertainty.PROVEN_NON_ACTIONABLE)
                    {
                        if (!ConditionsHold(edge, state.Flags, state.Items)) unknownEscape = true;
                        continue;
                    }

                    if (!ConditionsHold(edge, state.Flags, state.Items))
                        continue;

                    var nextFlags = new HashSet<int>(state.Flags);
                    foreach (int flag in edge.ClearEventFlags) nextFlags.Remove(flag);
                    foreach (int flag in edge.SetEventFlags) nextFlags.Add(flag);
                    var nextItems = new HashSet<int>(state.Items);
                    ulong nextWaypoints = UpdateWaypoints(edge.To, query.RequiredWaypoints, state.Waypoints);
                    string key = StateKey(edge.To, nextFlags, nextItems, nextWaypoints);
                    if (!visited.Add(key)) continue;

                    var step = new PhysicalWitnessStep(
                        edge.Id,
                        edge.Mechanism,
                        edge.From,
                        edge.To,
                        Sorted(state.Flags),
                        Sorted(nextFlags),
                        Sorted(state.Items),
                        Sorted(nextItems),
                        edge.Evidence.ToArray())
                    {
                        Certification = edge.Certification,
                    };
                    queue.Enqueue(new SearchState(edge.To, nextFlags, nextItems, nextWaypoints, state.Steps.Append(step).ToArray()));
                }
            }

            PhysicalWitnessVerdict verdict = coverage.Complete && !unknownEscape && !budgetExceeded
                ? PhysicalWitnessVerdict.PHYSICALLY_UNSOLVABLE_PROVEN
                : PhysicalWitnessVerdict.UNRESOLVED_MODEL_GAP;
            string explanation = verdict switch
            {
                PhysicalWitnessVerdict.PHYSICALLY_UNSOLVABLE_PROVEN =>
                    "The complete declared physical inventory was exhausted without a plausible UNKNOWN escape.",
                _ when budgetExceeded =>
                    "The search-state budget was exhausted; absence of a witness is not a negative proof.",
                _ when unknownEscape =>
                    "A reachable UNKNOWN edge could plausibly escape the explored proven graph.",
                _ =>
                    "The physical inventory is incomplete; absence of a witness is not a negative proof."
            };

            return new PhysicalWitnessResult(verdict, Array.Empty<PhysicalWitnessStep>(), null, stateCount, unknownEscape, explanation);
        }

        public static bool VerifyWitness(
            PhysicalQuery query,
            IReadOnlyList<PhysicalEdge> edges,
            PhysicalCoverage coverage,
            PhysicalWitnessResult witness)
        {
            if (witness == null || witness.Verdict != PhysicalWitnessVerdict.PHYSICALLY_SOLVABLE_PROVEN)
                return false;

            if (Fingerprint(witness.Steps) != witness.RouteClassFingerprint) return false;
            PhysicalWitnessResult replayed = Evaluate(query, edges, coverage);
            if (replayed.Verdict != PhysicalWitnessVerdict.PHYSICALLY_SOLVABLE_PROVEN
                || replayed.RouteClassFingerprint != witness.RouteClassFingerprint
                || replayed.Steps.Length != witness.Steps.Length)
                return false;

            for (int index = 0; index < replayed.Steps.Length; index++)
            {
                PhysicalWitnessStep expected = replayed.Steps[index];
                PhysicalWitnessStep supplied = witness.Steps[index];
                if (expected.EdgeId != supplied.EdgeId
                    || expected.From != supplied.From
                    || expected.To != supplied.To
                    || expected.Mechanism != supplied.Mechanism
                    || expected.Certification != supplied.Certification
                    || !expected.Evidence.SequenceEqual(supplied.Evidence)
                    || !expected.EventFlagsBefore.SequenceEqual(supplied.EventFlagsBefore)
                    || !expected.EventFlagsAfter.SequenceEqual(supplied.EventFlagsAfter)
                    || !expected.ItemsBefore.SequenceEqual(supplied.ItemsBefore)
                    || !expected.ItemsAfter.SequenceEqual(supplied.ItemsAfter))
                    return false;
            }

            return true;
        }

        public static string Fingerprint(PhysicalWitnessStep[] steps)
        {
            var maps = new SortedSet<string>(StringComparer.Ordinal);
            var mechanisms = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var directions = new List<string>();
            int bossGates = 0;
            int itemGates = 0;
            int pvp = 0;
            int randomizedFog = 0;
            int fixedTransitions = 0;
            foreach (PhysicalWitnessStep step in steps)
            {
                maps.Add(step.From.Map);
                maps.Add(step.To.Map);
                string mechanism = step.Mechanism ?? "unknown";
                mechanisms[mechanism] = mechanisms.TryGetValue(mechanism, out int count) ? count + 1 : 1;
                if (mechanism.Contains("boss", StringComparison.OrdinalIgnoreCase)) bossGates++;
                if (mechanism.Contains("item", StringComparison.OrdinalIgnoreCase)) itemGates++;
                if (mechanism.Contains("pvp", StringComparison.OrdinalIgnoreCase)) pvp++;
                if (mechanism.Contains("fog", StringComparison.OrdinalIgnoreCase)) randomizedFog++;
                if (mechanism.Contains("fixed", StringComparison.OrdinalIgnoreCase)) fixedTransitions++;
                directions.Add(step.From.Map == step.To.Map ? "same-map" : "cross-map");
                if (step.Certification != null)
                    directions.Add("cert=" + string.Join("/", step.Certification.RuleId, step.Certification.Mode,
                        step.Certification.RequiredState, step.Certification.ActionRegion, step.Certification.Dispatcher,
                        step.Certification.DestinationPlayer, step.Certification.DestinationMap,
                        step.Certification.Landing, step.Certification.Provenance, step.Certification.Status));
            }

            string canonical = string.Join("|", new[]
            {
                "physical-route-class-v1",
                "maps=" + maps.Count,
                "randomizedFog=" + randomizedFog,
                "fixed=" + fixedTransitions,
                "bossGates=" + bossGates,
                "itemGates=" + itemGates,
                "pvp=" + pvp,
                "directions=" + string.Join(",", directions),
                "mechanisms=" + string.Join(",", mechanisms.Select(pair => pair.Key + ":" + pair.Value))
            });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }

        private static void ValidateQuery(PhysicalQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            ValidateNode(query.Start, nameof(query.Start));
            ValidateNode(query.Goal, nameof(query.Goal));
            if (query.MaxStates < 1) throw new ArgumentOutOfRangeException(nameof(query.MaxStates));
            if (query.RequiredWaypoints == null || query.RequiredWaypoints.Count > 63)
                throw new ArgumentException("A route query must provide at most 63 required waypoints.", nameof(query));
            foreach (PhysicalNode waypoint in query.RequiredWaypoints) ValidateNode(waypoint, nameof(query.RequiredWaypoints));
        }

        private static void ValidateCoverage(PhysicalCoverage coverage)
        {
            if (coverage == null) throw new ArgumentNullException(nameof(coverage));
            if (string.IsNullOrWhiteSpace(coverage.Scope)) throw new ArgumentException("Coverage scope is required.", nameof(coverage));
            ValidateEvidence(coverage.Evidence, "coverage");
            if (coverage.Complete && coverage.Evidence.Count == 0)
                throw new ArgumentException("Complete physical coverage requires auditable evidence.", nameof(coverage));
        }

        private static void ValidateEdges(IReadOnlyList<PhysicalEdge> edges)
        {
            if (edges == null) throw new ArgumentNullException(nameof(edges));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (PhysicalEdge edge in edges)
            {
                if (edge == null) throw new ArgumentException("Null physical edges are invalid.", nameof(edges));
                if (string.IsNullOrWhiteSpace(edge.Id) || !ids.Add(edge.Id))
                    throw new ArgumentException("Physical edge IDs must be non-empty and unique.", nameof(edges));
                ValidateNode(edge.From, edge.Id + ".from");
                ValidateNode(edge.To, edge.Id + ".to");
                if (!Enum.IsDefined(edge.Certainty))
                    throw new ArgumentException($"Edge '{edge.Id}' has an unsupported certainty value.", nameof(edges));
                if (string.IsNullOrWhiteSpace(edge.Mechanism) || edge.RequiredEventFlags == null
                    || edge.ForbiddenEventFlags == null || edge.RequiredItems == null
                    || edge.SetEventFlags == null || edge.ClearEventFlags == null)
                    throw new ArgumentException($"Edge '{edge.Id}' has incomplete typed fields.", nameof(edges));
                ValidateEvidence(edge.Evidence, edge.Id);
                if (edge.Certainty == PhysicalEdgeCertainty.PROVEN_ACTIONABLE)
                {
                    string[] required = edge.Certification?.MechanismClass == GeneratedTransitionCertifier.SeathMode2P05308
                        ? RequiredSeathProofKinds
                        : RequiredActionProofKinds;
                    if (required.Any(kind => !edge.Evidence.Any(item => item.Kind == kind)))
                        throw new ArgumentException($"Actionable edge '{edge.Id}' lacks source, dispatcher, or destination proof.", nameof(edges));
                    if (edge.Certification == null || edge.Certification.Status != "PROVEN_ACTIONABLE"
                        || string.IsNullOrWhiteSpace(edge.Certification.RuleId)
                        || string.IsNullOrWhiteSpace(edge.Certification.MechanismClass)
                        || string.IsNullOrWhiteSpace(edge.Certification.Source)
                        || string.IsNullOrWhiteSpace(edge.Certification.Destination)
                        || string.IsNullOrWhiteSpace(edge.Certification.RequiredState)
                        || string.IsNullOrWhiteSpace(edge.Certification.ActionRegion)
                        || string.IsNullOrWhiteSpace(edge.Certification.Dispatcher)
                        || string.IsNullOrWhiteSpace(edge.Certification.DestinationPlayer)
                        || string.IsNullOrWhiteSpace(edge.Certification.DestinationMap)
                        || string.IsNullOrWhiteSpace(edge.Certification.Landing)
                        || string.IsNullOrWhiteSpace(edge.Certification.Provenance))
                        throw new ArgumentException($"Actionable edge '{edge.Id}' lacks complete rule-bound certification metadata.", nameof(edges));
                    if (!edge.Evidence.Any(item => item.Kind == "VerifiedSourceObject")
                        || !edge.Evidence.Any(item => item.Kind == "VerifiedDestinationMap")
                        || !edge.Evidence.Any(item => item.Kind == "VerifiedLanding"))
                        throw new ArgumentException($"Actionable edge '{edge.Id}' lacks independently verified source, map, or landing evidence.", nameof(edges));
                    if (edge.Certification.MechanismClass == GeneratedTransitionCertifier.PvpMode0
                        && !edge.Evidence.Any(item => item.Kind == "ProjectPolicyIdentity"))
                        throw new ArgumentException($"PvP edge '{edge.Id}' lacks project-owned gate identity evidence.", nameof(edges));
                    if (edge.Certification.MechanismClass == GeneratedTransitionCertifier.ProjectCustomWarpMode0
                        && !edge.Evidence.Any(item => item.Kind == "ProjectCustomWarpPolicy"))
                        throw new ArgumentException($"CustomWarp edge '{edge.Id}' lacks exact project policy evidence.", nameof(edges));
                    if (edge.Certification.MechanismClass == GeneratedTransitionCertifier.SeathMode2P05308
                        && !edge.Evidence.Any(item => item.Kind == "SeathInteractionChain"))
                        throw new ArgumentException($"Seath edge '{edge.Id}' lacks the crystal/event/flag producer chain.", nameof(edges));
                    if (edge.Certification.MechanismClass == GeneratedTransitionCertifier.BossBoundaryMode0
                        && !edge.Evidence.Any(item => item.Kind == "BossLifecyclePolicy"))
                        throw new ArgumentException($"Boss edge '{edge.Id}' lacks complete boss lifecycle policy evidence.", nameof(edges));
                    if (edge.Evidence.All(item => item.Kind is "Area.To" or "ConnectCollision"))
                        throw new ArgumentException($"Area.To or ConnectCollision alone cannot prove edge '{edge.Id}'.", nameof(edges));
                }
                if (edge.Certainty == PhysicalEdgeCertainty.PROVEN_NON_ACTIONABLE)
                {
                    bool scoped = edge.RequiredEventFlags.Count + edge.ForbiddenEventFlags.Count + edge.RequiredItems.Count > 0;
                    bool globalProof = edge.Evidence.Any(item => item.Kind is "VerifiedActionDisabled" or "GameplayTelemetry");
                    if (!edge.Evidence.Any(item => NonActionProofKinds.Contains(item.Kind)) || (!scoped && !globalProof))
                        throw new ArgumentException($"Non-actionable edge {edge.Id} lacks direct state-scoped refusal evidence.", nameof(edges));
                }
            }
        }

        private static void ValidateEvidence(IReadOnlyList<PhysicalEvidence> evidence, string owner)
        {
            if (evidence == null || evidence.Count == 0)
                throw new ArgumentException($"Physical evidence is required for '{owner}'.");
            foreach (PhysicalEvidence item in evidence)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Kind) || string.IsNullOrWhiteSpace(item.Locator)
                    || !IsSha256(item.Sha256))
                    throw new ArgumentException($"Evidence for '{owner}' requires a kind, locator, and SHA-256.");
            }
        }

        private static bool IsSha256(string value) =>
            value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

        private static void ValidateNode(PhysicalNode node, string name)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Map) || string.IsNullOrWhiteSpace(node.Anchor))
                throw new ArgumentException($"A physical map and anchor are required for '{name}'.");
        }

        private static bool ConditionsHold(PhysicalEdge edge, HashSet<int> flags, HashSet<int> items) =>
            edge.RequiredEventFlags.All(flags.Contains)
            && !edge.ForbiddenEventFlags.Any(flags.Contains)
            && edge.RequiredItems.All(items.Contains);

        private static bool IsGoal(SearchState state, PhysicalQuery query)
        {
            ulong completeWaypoints = query.RequiredWaypoints.Count == 0
                ? 0
                : (1UL << query.RequiredWaypoints.Count) - 1;
            return state.Node == query.Goal
                && query.RequiredGoalEventFlags.All(state.Flags.Contains)
                && state.Waypoints == completeWaypoints;
        }

        private static ulong UpdateWaypoints(PhysicalNode node, IReadOnlyList<PhysicalNode> waypoints, ulong mask)
        {
            for (int index = 0; index < waypoints.Count; index++)
                if (node == waypoints[index]) mask |= 1UL << index;
            return mask;
        }

        private static int[] Sorted(IEnumerable<int> values) => values.OrderBy(value => value).ToArray();

        private static string StateKey(PhysicalNode node, HashSet<int> flags, HashSet<int> items, ulong waypoints) =>
            node.Map + "\u001f" + node.Anchor + "\u001f" + string.Join(",", Sorted(flags))
            + "\u001f" + string.Join(",", Sorted(items)) + "\u001f" + waypoints.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static class ExperimentalPhysicalWitnessSelfTests
    {
        private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        public static void Run()
        {
            ProvenDirectedRouteProducesReplayableWitness();
            UnknownEdgesNeverProveTraversalOrImpossibility();
            CompleteClosedInventoryCanProveNonReachability();
            BossFlagAndSequentialStateArePreserved();
            AreaAndConnectCollisionAloneCannotProveTraversal();
            DirectionAndFingerprintAreDeterministic();
            SeathMode2RequiresItsProducerChainInsteadOfActionRegion();
            Console.WriteLine("Experimental physical witness self-tests passed (7 groups).");
        }

        private static void ProvenDirectedRouteProducesReplayableWitness()
        {
            PhysicalNode start = new("demonruins", "ceaseless-trigger");
            PhysicalNode main = new("demonruins", "main-side");
            PhysicalNode lava = new("demonruins", "lower-lava-side");
            var edges = new[]
            {
                ActionEdge("boss-exit", start, main, "randomized-fog-boss", new[] { 11410410 }),
                ActionEdge("lower-route", main, lava, "fixed-boss-gated-route", new[] { 11410410 })
            };
            PhysicalQuery query = Query(start, lava, new[] { 11410410 }, new[] { main });
            PhysicalWitnessResult result = ExperimentalPhysicalWitness.Evaluate(query, edges, IncompleteCoverage());
            if (result.Verdict != PhysicalWitnessVerdict.PHYSICALLY_SOLVABLE_PROVEN || result.Steps.Length != 2
                || result.Steps[0].EventFlagsBefore.Contains(11410410) == false
                || !ExperimentalPhysicalWitness.VerifyWitness(query, edges, IncompleteCoverage(), result))
                throw new InvalidOperationException("A fully evidenced directed post-boss witness did not replay.");

            PhysicalWitnessStep changed = result.Steps[0] with { To = new PhysicalNode("demonruins", "wrong-side") };
            if (ExperimentalPhysicalWitness.VerifyWitness(query, edges, IncompleteCoverage(), result with { Steps = new[] { changed, result.Steps[1] } }))
                throw new InvalidOperationException("Witness replay accepted a changed destination.");
            PhysicalEvidence[] changedEvidence = result.Steps[0].Evidence.ToArray();
            changedEvidence[0] = changedEvidence[0] with { Sha256 = new string('b', 64) };
            PhysicalWitnessStep evidenceChanged = result.Steps[0] with { Evidence = changedEvidence };
            if (ExperimentalPhysicalWitness.VerifyWitness(query, edges, IncompleteCoverage(), result with { Steps = new[] { evidenceChanged, result.Steps[1] } }))
                throw new InvalidOperationException("Witness replay accepted altered provenance.");
            PhysicalEdgeCertification changedCertification = result.Steps[0].Certification with { RuleId = "unreviewed-rule" };
            PhysicalWitnessStep certificationChanged = result.Steps[0] with { Certification = changedCertification };
            if (ExperimentalPhysicalWitness.VerifyWitness(query, edges, IncompleteCoverage(), result with { Steps = new[] { certificationChanged, result.Steps[1] } }))
                throw new InvalidOperationException("Witness replay accepted altered edge certification metadata.");
            if (ExperimentalPhysicalWitness.VerifyWitness(query, edges, IncompleteCoverage(), result with { RouteClassFingerprint = new string('0', 64) }))
                throw new InvalidOperationException("Witness replay accepted an altered route fingerprint.");
        }

        private static void UnknownEdgesNeverProveTraversalOrImpossibility()
        {
            PhysicalNode start = new("demonruins", "arena");
            PhysicalNode goal = new("demonruins", "main");
            PhysicalEdge unknown = UnknownEdge("unknown-exit", start, goal);
            PhysicalWitnessResult noPositive = ExperimentalPhysicalWitness.Evaluate(Query(start, goal, new[] { 11410410 }), new[] { unknown }, IncompleteCoverage());
            if (noPositive.Verdict != PhysicalWitnessVerdict.UNRESOLVED_MODEL_GAP || noPositive.Steps.Length != 0)
                throw new InvalidOperationException("UNKNOWN was treated as traversable.");

            PhysicalWitnessResult noNegative = ExperimentalPhysicalWitness.Evaluate(
                Query(start, goal, new[] { 11410410 }),
                new[] { unknown },
                CompleteCoverage());
            if (noNegative.Verdict != PhysicalWitnessVerdict.UNRESOLVED_MODEL_GAP || !noNegative.ReachableUnknownEscape)
                throw new InvalidOperationException("A reachable UNKNOWN escape was treated as impossible.");
        }

        private static void CompleteClosedInventoryCanProveNonReachability()
        {
            PhysicalNode start = new("demonruins", "arena");
            PhysicalNode blocked = new("demonruins", "blocked-gate");
            PhysicalNode goal = new("demonruins", "lava");
            PhysicalEdge edge = NonActionableEdge("closed-gate", start, blocked);
            PhysicalWitnessResult result = ExperimentalPhysicalWitness.Evaluate(Query(start, goal, new[] { 11410410 }), new[] { edge }, CompleteCoverage());
            if (result.Verdict != PhysicalWitnessVerdict.PHYSICALLY_UNSOLVABLE_PROVEN)
                throw new InvalidOperationException("A complete, closed inventory did not support a negative proof.");
            PhysicalWitnessResult outOfScope = ExperimentalPhysicalWitness.Evaluate(
                Query(start, goal, Array.Empty<int>()),
                new[] { edge },
                CompleteCoverage());
            if (outOfScope.Verdict != PhysicalWitnessVerdict.UNRESOLVED_MODEL_GAP)
                throw new InvalidOperationException("A state-scoped non-actionability proof was applied outside its observed state.");
        }

        private static void BossFlagAndSequentialStateArePreserved()
        {
            PhysicalNode start = new("demonruins", "arena");
            PhysicalNode main = new("demonruins", "main");
            PhysicalNode lava = new("demonruins", "lava");
            PhysicalEdge edge1 = ActionEdge("boss-gated-exit", start, main, "boss-gated-fog", new[] { 11410410 });
            PhysicalEdge edge2 = ActionEdge("progression-route", main, lava, "fixed-route", new[] { 11410410 });
            PhysicalQuery withoutFlag = Query(start, lava, Array.Empty<int>(), new[] { main });
            if (ExperimentalPhysicalWitness.Evaluate(withoutFlag, new[] { edge1, edge2 }, CompleteCoverage()).Verdict
                != PhysicalWitnessVerdict.PHYSICALLY_UNSOLVABLE_PROVEN)
                throw new InvalidOperationException("An unset boss flag was ignored.");

            PhysicalEdge setFlag = ActionEdge("defeat-boss", start, main, "boss-state-change", Array.Empty<int>())
                with { SetEventFlags = new[] { 11410410 } };
            PhysicalQuery sequential = Query(start, lava, Array.Empty<int>(), new[] { main });
            if (ExperimentalPhysicalWitness.Evaluate(sequential, new[] { setFlag, edge2 }, IncompleteCoverage()).Verdict
                != PhysicalWitnessVerdict.PHYSICALLY_SOLVABLE_PROVEN)
                throw new InvalidOperationException("A flag set on one path step was not preserved for the next step.");
        }

        private static void AreaAndConnectCollisionAloneCannotProveTraversal()
        {
            PhysicalNode start = new("map", "a");
            PhysicalNode goal = new("map", "b");
            var edge = ActionEdge("bad-shortcut", start, goal, "randomized-fog", Array.Empty<int>()) with
            {
                Evidence = new[]
                {
                    new PhysicalEvidence("Area.To", "area A -> B", Hash),
                    new PhysicalEvidence("ConnectCollision", "MSB connect-collision", Hash)
                }
            };
            bool rejected = false;
            try
            {
                ExperimentalPhysicalWitness.Evaluate(Query(start, goal, Array.Empty<int>()), new[] { edge }, IncompleteCoverage());
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            if (!rejected) throw new InvalidOperationException("Area.To and ConnectCollision alone created a proven traversal.");
        }

        private static void DirectionAndFingerprintAreDeterministic()
        {
            PhysicalNode a = new("map-a", "spawn-a");
            PhysicalNode b = new("map-b", "spawn-b");
            PhysicalEdge edge = ActionEdge("a-to-b", a, b, "randomized-fog-pvp", Array.Empty<int>());
            PhysicalWitnessResult forward = ExperimentalPhysicalWitness.Evaluate(Query(a, b, Array.Empty<int>()), new[] { edge }, IncompleteCoverage());
            if (forward.Verdict != PhysicalWitnessVerdict.PHYSICALLY_SOLVABLE_PROVEN
                || forward.RouteClassFingerprint != ExperimentalPhysicalWitness.Fingerprint(forward.Steps))
                throw new InvalidOperationException("A valid directed route did not produce a stable route-class fingerprint.");

            PhysicalWitnessResult reverse = ExperimentalPhysicalWitness.Evaluate(Query(b, a, Array.Empty<int>()), new[] { edge }, IncompleteCoverage());
            if (reverse.Verdict != PhysicalWitnessVerdict.UNRESOLVED_MODEL_GAP)
                throw new InvalidOperationException("A one-way edge was traversed in reverse.");
        }

        private static void SeathMode2RequiresItsProducerChainInsteadOfActionRegion()
        {
            PhysicalNode crystal = new("dukes", "seath-crystal:1701801");
            PhysicalNode prison = new("dukes", "player:1815800");
            PhysicalEvidence[] evidence =
            {
                new("VerifiedSourceObject", "MSB crystal entity 1701801", Hash),
                new("SeathInteractionChain", "event0 initializes 11700005; event sets 5308", Hash),
                new("GeneratedDispatcher", "common/event0 mode 2 p0=5308", Hash),
                new("VerifiedDestinationPlayer", "dukes/player 1815800", Hash),
                new("VerifiedDestinationMap", "dukes map", Hash),
                new("VerifiedLanding", "runtime prison region", Hash),
            };
            var edge = new PhysicalEdge("seath-mode2", crystal, prison,
                PhysicalEdgeCertainty.PROVEN_ACTIONABLE, GeneratedTransitionCertifier.SeathMode2P05308,
                "flag 5308 ON; system warp flag 15 OFF", new[] { 5308 }, new[] { 15 },
                Array.Empty<int>(), Array.Empty<int>(), new[] { 5308 }, evidence)
            {
                Certification = new PhysicalEdgeCertification("seath-mode2-p0-5308-v1",
                    GeneratedTransitionCertifier.SeathMode2P05308, 2, "dukes/crystal:1701801", "dukes/player:1815800",
                    "flag 5308 ON; system warp flag 15 OFF", "N/A: crystal interaction event 11700005",
                    "common/event0/init[81]/slot[0]", "1815800", "dukes", "runtime prison region 1702901",
                    "current synthetic evidence", "PROVEN_ACTIONABLE", "test"),
            };
            var query = new PhysicalQuery(crystal, prison, new[] { 5308 }, Array.Empty<int>(),
                Array.Empty<int>(), Array.Empty<PhysicalNode>(), 10);
            PhysicalWitnessResult result = ExperimentalPhysicalWitness.Evaluate(query, new[] { edge }, IncompleteCoverage());
            if (result.Verdict != PhysicalWitnessVerdict.PHYSICALLY_SOLVABLE_PROVEN)
                throw new InvalidOperationException("The exact Seath Mode 2 producer chain did not satisfy its proof-specific edge requirements.");

            PhysicalEdge missingChain = edge with { Evidence = evidence.Where(item => item.Kind != "SeathInteractionChain").ToArray() };
            bool rejected = false;
            try
            {
                ExperimentalPhysicalWitness.Evaluate(query, new[] { missingChain }, IncompleteCoverage());
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            if (!rejected) throw new InvalidOperationException("A Seath Mode 2 edge without its producer chain was accepted.");
        }

        private static PhysicalQuery Query(PhysicalNode start, PhysicalNode goal, int[] initialFlags, PhysicalNode[] waypoints = null) =>
            new(start, goal, initialFlags, Array.Empty<int>(), initialFlags, waypoints ?? Array.Empty<PhysicalNode>(), 100);

        private static PhysicalCoverage IncompleteCoverage() =>
            new("synthetic incomplete test inventory", false, new[] { new PhysicalEvidence("TestFixture", "synthetic", Hash) });

        private static PhysicalCoverage CompleteCoverage() =>
            new("synthetic complete test inventory", true, new[] { new PhysicalEvidence("TestFixture", "complete synthetic inventory", Hash) });

        private static PhysicalEdge ActionEdge(string id, PhysicalNode from, PhysicalNode to, string mechanism, int[] requiredFlags) =>
            new PhysicalEdge(id, from, to, PhysicalEdgeCertainty.PROVEN_ACTIONABLE, mechanism, "verified",
                requiredFlags, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
                new[]
                {
                    new PhysicalEvidence("VerifiedSourceObject", "verified source fog object", Hash),
                    new PhysicalEvidence("SourceActionRegion", "verified trigger region", Hash),
                    new PhysicalEvidence("GeneratedDispatcher", "verified Event 5700 dispatch", Hash),
                    new PhysicalEvidence("VerifiedDestinationPlayer", "verified player landing", Hash),
                    new PhysicalEvidence("VerifiedDestinationMap", "verified destination map", Hash),
                    new PhysicalEvidence("VerifiedLanding", "verified object-relative landing", Hash)
                })
            {
                Certification = new PhysicalEdgeCertification("synthetic-reviewed-rule-v1", mechanism, 0,
                    from.Map + "/" + from.Anchor, to.Map + "/" + to.Anchor, "verified test state",
                    "1234", "common/event0/init[0]", "5678", to.Map, "synthetic exact landing",
                    "synthetic unit test evidence", "PROVEN_ACTIONABLE", "Synthetic test-only certificate."),
            };

        private static PhysicalEdge UnknownEdge(string id, PhysicalNode from, PhysicalNode to) =>
            new(id, from, to, PhysicalEdgeCertainty.UNKNOWN, "unknown", "unresolved",
                Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
                new[] { new PhysicalEvidence("UnknownCandidate", "candidate only", Hash) });

        private static PhysicalEdge NonActionableEdge(string id, PhysicalNode from, PhysicalNode to) =>
            new(id, from, to, PhysicalEdgeCertainty.PROVEN_NON_ACTIONABLE, "verified-blocked-gate", "closed when 11410410 is set",
                new[] { 11410410 }, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
                new[] { new PhysicalEvidence("VerifiedGateBlock", "exact blocked gate state when 11410410 is set", Hash) });
    }
}
