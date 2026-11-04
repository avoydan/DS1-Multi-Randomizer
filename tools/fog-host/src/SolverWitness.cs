using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FogMod;
using static FogMod.AnnotationData;
using static FogMod.Graph;

namespace FogHost
{
    /// <summary>
    /// Human-followable proof of the solver's claim for ONE accepted Fog graph (docs/SOLVER_WITNESS.md).
    ///
    /// The source of truth is the search that accepted the world: <see cref="StartReachabilityValidator"/> runs <see cref="GraphChecker.Check"/> with a
    /// <see cref="WitnessObserver"/>, and everything here is derived from what that observer recorded at the moment the solver first reached each Area.
    /// Nothing is re-solved and no semantics are interpreted: an edge is described only by the metadata it carries (its Entrance/Warp record, its tags,
    /// its linked condition). Where the data has no ladder/drop/elevator tag, the edge is a fixed in-map route and says so.
    ///
    /// Two products, one document:
    /// <list type="bullet">
    /// <item><c>reachTree</c>: for every Area, the edge that first reached it, in the order the solver proved it (the full machine-readable proof).</item>
    /// <item><c>mainRoute</c>: the dependency closure of one target (the Gwyn arena when the solver reached it): the predecessor chain plus, recursively, every
    /// area / key item / boss completion the edges on it needed, ordered by the solver's own sequence so each step comes after what it depends on.</item>
    /// </list>
    /// The output is deterministic (fixed key order, no paths, no timestamps); the same graph yields the same bytes.
    /// </summary>
    public static class SolverWitness
    {
        public const string Format = "ds1tr-solver-witness";
        public const int SchemaVersion = 1;
        /// <summary>The Gwyn arena: the deepest progression Area of the graph, and the natural completion target when the solver reached it.</summary>
        public const string PreferredTarget = "kiln_gwyn";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public static string[] Assumptions { get; } =
        {
            "Every Area the solver has already reached can be entered again at any time (the search expands every visited Area; it does not check the way back).",
            "A key item is obtained as soon as the Area that holds it has been reached; nothing else is needed to take it.",
            "A boss completion is derived only from a directed arrival through the boss encounter entrance; the game sets the real flag only when the boss dies.",
            "A fog gate or route marked two-way can be crossed in both directions; one-way is a link the graph only has in one direction.",
            "The solver only knows the graph. The first step that cannot be done in the game identifies the graph/model defect.",
        };

        // ---- DTOs (serialized in declaration order) --------------------------------------------------------------------------------------------------

        /// <summary>One side of a transition. <c>OtherSideArea</c> is the Area on the opposite side of the same physical gate; <c>BossTriggerSide</c> marks the side that starts the boss encounter.</summary>
        public sealed record GateDto(string Area, string Object, string Text, string[] Tags, string SideCondition, string OtherSideArea, bool AreaIsBossArena, bool BossTriggerSide);

        public sealed record RequirementDto(string Name, string Kind, string ReachedInArea, int? SatisfiedAtSequence, string Item);

        public sealed record EdgeDto(
            string Kind, string Label, bool Randomized, string Pairing, bool Glitched, bool OneWay, string Reverse, string ReverseCondition, string ReverseVia,
            string[] Qualifiers, GateDto Exit, GateDto Arrival, string Requires, RequirementDto[] Requirements, string[] SatisfiedBy);

        /// <summary><c>Lots</c> are the staged ItemLotParam rows (inside the solver's own lot-to-Area table) that hold the item; the solver only uses their Area.</summary>
        public sealed record ItemDto(string Name, string GameName, string GameItemId, string DefaultArea, string SolverArea, bool Moved, int[] Lots, string[] Tags);

        public sealed record ReachDto(int Sequence, int Pass, string Area, string[] AreaTags, string FromArea, EdgeDto Edge, string[] KeyItemsGained);

        public sealed record CompletionDto(int Sequence, string Name, string EncounterArea, int EventId, string TriggerEntrance, string TriggerEdgeFrom);

        public sealed record StepDto(
            int Number, string Type, string Area, int Sequence, string ReturnTo, EdgeDto Edge, ItemDto Item, CompletionDto Completion,
            string[] Notes);

        public sealed record ClaimDto(
            string Statement, string StartArea, string StartName, int AreasTotal, int AreasReached, string[] OptionalAreasNotRequired, string[] Unreached,
            string[] FogOptions, bool GlitchedLogicEnabled);

        public sealed record TargetDto(string Area, string Reason);

        public sealed record WitnessDto(
            string Format, int SchemaVersion, string HostVersion, string AnnotationSource, ClaimDto Claim, string[] Assumptions, TargetDto Target,
            ItemDto[] KeyItems, ReachDto[] ReachTree, CompletionDto[] BossCompletions, StepDto[] MainRoute);

        // ---- build -----------------------------------------------------------------------------------------------------------------------------------

        /// <summary>Display names and lot rows of the key items, read from data the host already loaded; absent entries simply leave the fields empty.</summary>
        public sealed class KeyItemSources
        {
            public Dictionary<string, string> GameNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public Dictionary<string, List<int>> Lots { get; } = new Dictionary<string, List<int>>(StringComparer.Ordinal);

            /// <summary>The same lot walk as <c>ItemReader</c> (exact compact lot ownership, the category:id key), recording lot IDs instead of deciding Areas.</summary>
            public static KeyItemSources Find(Annotations annotations, SoulsFormats.PARAM itemLots)
            {
                var result = new KeyItemSources();
                // The graph names some items differently from the policy (aliases such as "lordvessel"), so the English name is matched by game item id.
                foreach (Item key in annotations.KeyItems)
                {
                    FogKeyItemIdentityPolicy policy = FogProjectAnnotationPolicy.KeyItemIdentityPolicies.FirstOrDefault(p => key.ID == new FogGameItemId((int)p.Category, p.GameParamId).ToString());
                    if (policy != null) result.GameNames[key.Name] = policy.EnglishGameName;
                }
                if (itemLots == null || annotations.CompactLotLocations == null) return result;
                Dictionary<string, Item> byId = annotations.KeyItems.ToDictionary(i => i.ID, i => i, StringComparer.Ordinal);
                Dictionary<int, SoulsFormats.PARAM.Row> rows = itemLots.Rows.ToDictionary(r => (int)r.ID, r => r);
                foreach (int lot in annotations.CompactLotLocations.Keys.OrderBy(k => k))
                {
                    if (!rows.TryGetValue(lot, out SoulsFormats.PARAM.Row row)) continue;
                    for (int i = 1; i <= 8; i++)
                    {
                        int item = (int)row[$"lotItemId0{i}"].Value;
                        if (item == 0) continue;
                        int category = (int)row[$"lotItemCategory0{i}"].Value;
                        if (!SoulsIds.Universe.LotTypes.TryGetValue((uint)category, out int mapped)) continue;
                        if (!byId.TryGetValue($"{mapped}:{item}", out Item key)) continue;
                        if (!result.Lots.TryGetValue(key.Name, out List<int> list)) result.Lots[key.Name] = list = new List<int>();
                        if (!list.Contains(lot)) list.Add(lot);
                    }
                }
                return result;
            }
        }

        public static byte[] Serialize(WitnessDto witness) => System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(witness, JsonOptions) + "\n");

        public static byte[] Build(RandomizerOptions options, Graph graph, Annotations annotations, GraphChecker.CheckRecord check, WitnessObserver observer, string annotationSource, KeyItemSources sources = null)
            => Serialize(BuildDocument(options, graph, annotations, check, observer, annotationSource, sources));

        public static WitnessDto BuildDocument(RandomizerOptions options, Graph graph, Annotations annotations, GraphChecker.CheckRecord check, WitnessObserver observer, string annotationSource, KeyItemSources sources = null)
        {
            sources ??= new KeyItemSources();
            if (observer.Reaches.Count == 0) throw new InvalidOperationException("The solver recorded no reachable Area; there is no witness.");
            var warps = new HashSet<Entrance>(annotations.Warps ?? new List<Entrance>());
            Dictionary<string, AreaReachInfo> reachBy = observer.Reaches.ToDictionary(r => r.Area, r => new AreaReachInfo(r), StringComparer.Ordinal);
            Dictionary<string, WitnessObserver.BossGrant> grantBy = observer.BossGrants.ToDictionary(g => g.Name, g => g, StringComparer.Ordinal);
            Dictionary<string, Area> areas = annotations.Areas.ToDictionary(a => a.Name, a => a, StringComparer.Ordinal);
            Dictionary<string, Item> keyItems = annotations.KeyItems.ToDictionary(i => i.Name, i => i, StringComparer.Ordinal);

            ItemDto ItemOf(string name) => ItemInfo(name, keyItems, graph, sources);
            EdgeDto Describe(AreaReachInfo reach) => DescribeEdge(reach.Edge, reach.Condition, reach.Satisfied, graph, annotations, warps, reachBy, grantBy, keyItems, areas);

            ReachDto[] tree = observer.Reaches.Select(r => new ReachDto(
                r.Sequence, r.Pass, r.Area, AreaTags(areas, r.Area), r.FromArea, r.Edge == null ? null : Describe(new AreaReachInfo(r)),
                r.KeyItemsGained.OrderBy(n => n, StringComparer.Ordinal).ToArray())).ToArray();

            CompletionDto[] completions = observer.BossGrants.Select(g => new CompletionDto(
                g.Sequence, g.Name, g.Rule.Area, g.Rule.EventId, g.Rule.TriggerEntrance, g.TriggerEdge.From)).ToArray();

            (string targetArea, string targetReason) = ChooseTarget(check, observer);
            StepDto[] route = BuildRoute(targetArea, observer, graph, reachBy, grantBy, keyItems, areas, Describe, ItemOf);

            string[] optional = areas.Values.Where(a => a.HasTag("optional")).Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var claim = new ClaimDto(
                "Every non-optional Area is reachable from the start Area through the connected Fog graph (generation profile ds1tr-gen-v6 start-reachability rule).",
                observer.Reaches[0].Area, graph.start?.Name, graph.graph.Count, observer.Reaches.Count,
                optional, (check.Unvisited ?? new List<string>()).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                options.GetEnabled().ToArray(), options["hard"]);
            return new WitnessDto(
                Format, SchemaVersion, Program.HostVersion, annotationSource, claim, Assumptions,
                new TargetDto(targetArea, targetReason),
                annotations.KeyItems.Select(i => ItemOf(i.Name)).OrderBy(i => i.Name, StringComparer.Ordinal).ToArray(),
                tree, completions, route);
        }

        private sealed class AreaReachInfo
        {
            public AreaReachInfo(WitnessObserver.AreaReach reach)
            {
                Edge = reach.Edge; Condition = reach.Condition; Satisfied = reach.Satisfied; Sequence = reach.Sequence;
            }
            public Edge Edge { get; }
            public Expr Condition { get; }
            public IReadOnlyList<string> Satisfied { get; }
            public int Sequence { get; }
        }

        private static (string, string) ChooseTarget(GraphChecker.CheckRecord check, WitnessObserver observer)
        {
            if (observer.Reaches.Any(r => r.Area == PreferredTarget))
                return (PreferredTarget, "The Gwyn arena is the deepest progression Area of the graph; reaching it is the completion the solver proves.");
            // Largest solver distance; a tie goes to the Area the solver proved last (deterministic, and the deeper one in discovery order).
            var order = observer.Reaches.ToDictionary(r => r.Area, r => r.Sequence, StringComparer.Ordinal);
            string deepest = check.Records.Values.OrderByDescending(r => r.Dist).ThenByDescending(r => order.TryGetValue(r.Area, out int sequence) ? sequence : -1)
                .ThenBy(r => r.Area, StringComparer.Ordinal).First().Area;
            return (deepest, $"The graph has no {PreferredTarget} Area; the Area with the largest solver distance is used.");
        }

        private static string[] AreaTags(Dictionary<string, Area> areas, string name)
            => areas.TryGetValue(name, out Area area) ? area.TagList.OrderBy(t => t, StringComparer.Ordinal).ToArray() : Array.Empty<string>();

        private static ItemDto ItemInfo(string name, Dictionary<string, Item> keyItems, Graph graph, KeyItemSources sources)
        {
            keyItems.TryGetValue(name, out Item item);
            graph.itemAreas.TryGetValue(name, out string solverArea);
            sources.GameNames.TryGetValue(name, out string gameName);
            int[] lots = sources.Lots.TryGetValue(name, out List<int> found) ? found.OrderBy(l => l).ToArray() : Array.Empty<int>();
            return new ItemDto(name, gameName, item?.ID, item?.Area, solverArea, item != null && solverArea != null && item.Area != solverArea, lots,
                item?.TagList.OrderBy(t => t, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>());
        }

        // ---- edge description (metadata only) --------------------------------------------------------------------------------------------------------

        private static EdgeDto DescribeEdge(
            Edge edge, Expr condition, IReadOnlyList<string> satisfied, Graph graph, Annotations annotations, HashSet<Entrance> warps,
            Dictionary<string, AreaReachInfo> reachBy, Dictionary<string, WitnessObserver.BossGrant> grantBy, Dictionary<string, Item> keyItems,
            Dictionary<string, Area> areas)
        {
            Edge arrival = edge.Link;
            Entrance exitEntrance = Lookup(graph, edge.Name);
            Entrance arrivalEntrance = Lookup(graph, arrival?.Name);
            bool exitWarp = exitEntrance != null && warps.Contains(exitEntrance);
            bool arrivalWarp = arrivalEntrance != null && warps.Contains(arrivalEntrance);
            string[] exitTags = GateTags(exitEntrance, edge.Side);
            string[] arrivalTags = GateTags(arrivalEntrance, arrival?.Side);
            bool glitched = HasTag(edge.Side, "hard") || HasTag(arrival?.Side, "hard") || edge.Text == "hard skip";
            bool inMap = edge.Name == null;
            bool shortcut = HasTag(edge.Side, "shortcut") || HasTag(arrival?.Side, "shortcut");

            string kind;
            if (glitched) kind = "glitched-route";
            else if (inMap) kind = "fixed-route";
            else if (exitWarp || arrivalWarp) kind = "warp";
            else if (HasEntranceTag(exitEntrance, "door") || HasEntranceTag(arrivalEntrance, "door")) kind = "door";
            else kind = edge.IsFixed ? "fixed-fog" : "randomized-fog";

            var qualifiers = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Entrance entrance in new[] { exitEntrance, arrivalEntrance })
            {
                if (entrance == null) continue;
                foreach (string tag in new[] { "boss", "pvp", "major", "world", "lordvessel", "unique", "norandom" })
                    if (entrance.HasTag(tag)) qualifiers.Add(tag);
            }
            if (shortcut) qualifiers.Add("shortcut");
            if (inMap) qualifiers.Add("in-map link");
            bool randomized = !edge.IsFixed;
            // Way back: the graph builds an in-map link without a pair, and a walkable route back is a SEPARATE edge of the other Area. So "one-way" means no
            // edge from the arrival Area back to the exit Area exists in the graph at all, not that this edge has no pair.
            Edge reverse = ReverseEdge(edge, graph);
            bool oneWay = reverse == null;
            string reverseState = reverse == null ? "none" : reverse.LinkedExpr == null ? "unconditional" : "conditional";

            RequirementDto[] requirements = condition == null
                ? Array.Empty<RequirementDto>()
                : condition.FreeVars().Select(name => Requirement(name, graph, reachBy, grantBy, keyItems, satisfied.Contains(name, StringComparer.Ordinal))).ToArray();

string label = Label(kind, randomized, qualifiers);
            // A shuffled gate pair joins two different objects; "vanilla" is an object whose exit side leads to its own other side.
            string pairing = edge.Name == null ? null : (edge.Name == arrival?.Name ? "vanilla" : "shuffled");
            return new EdgeDto(
                kind, label, randomized, pairing, glitched, oneWay, reverseState, reverse?.LinkedExpr?.ToString(), reverse == null ? null : (reverse.Name ?? "in map"), qualifiers.ToArray(),
                Gate(edge.From, edge, exitEntrance, edge.Side, areas),
                Gate(edge.To, arrival, arrivalEntrance, arrival?.Side, areas),
                condition?.ToString(), requirements, satisfied.ToArray());
        }

        /// <summary>The edge that leaves the arrival Area for the exit Area again: the pair of a gate, else the best (unconditional first, then by condition text) edge back.</summary>
        private static Edge ReverseEdge(Edge edge, Graph graph)
        {
            Edge pair = edge.Link?.Pair;
            if (pair != null && pair.To == edge.From && pair.From == edge.To) return pair;
            if (edge.To == null || !graph.graph.TryGetValue(edge.To, out Graph.Node node)) return null;
            return node.To.Where(candidate => candidate.To == edge.From && candidate.Link != null)
                .OrderBy(candidate => candidate.LinkedExpr == null ? 0 : 1).ThenBy(candidate => candidate.LinkedExpr?.ToString() ?? "", StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Name ?? "", StringComparer.Ordinal).FirstOrDefault();
        }

        private static GateDto Gate(string area, Edge edge, Entrance entrance, Side side, Dictionary<string, Area> areas)
        {
            string other = entrance?.Sides().FirstOrDefault(s => !ReferenceEquals(s, side))?.Area;
            bool bossTrigger = side != null && (side.BossTrigger != 0 || side.BossTriggerArea != null || side.BossEncounterStartEventId != 0);
            return new GateDto(area, edge?.Name, edge?.Text, GateTags(entrance, side), side?.Cond, other,
                areas.TryGetValue(area, out Area a) && a.HasTag("boss"), bossTrigger);
        }

        private static string Label(string kind, bool randomized, SortedSet<string> qualifiers)
        {
            string boss = qualifiers.Contains("boss") ? "boss " : "";
            string pvp = qualifiers.Contains("pvp") ? (qualifiers.Contains("major") ? "major PvP " : "PvP ") : "";
            return kind switch
            {
                "randomized-fog" => $"randomized {boss}{pvp}fog".Replace("  ", " ").Trim(),
                "fixed-fog" => $"fixed {boss}{pvp}fog".Replace("  ", " ").Trim(),
                "warp" => randomized ? "randomized warp" : "fixed warp",
                "door" => qualifiers.Contains("lordvessel") ? "lordvessel gate" : "door/gate",
                "glitched-route" => "glitched-logic route",
                _ => qualifiers.Contains("shortcut") ? "fixed route (in-map, shortcut)" : "fixed route (in-map)",
            };
        }

        private static RequirementDto Requirement(
            string name, Graph graph, Dictionary<string, AreaReachInfo> reachBy, Dictionary<string, WitnessObserver.BossGrant> grantBy,
            Dictionary<string, Item> keyItems, bool satisfiedNow)
        {
            if (graph.itemAreas.TryGetValue(name, out string itemArea))
                return new RequirementDto(name, "key-item", itemArea, itemArea != null && reachBy.TryGetValue(itemArea, out AreaReachInfo holder) ? holder.Sequence : null, name);
            if (grantBy.TryGetValue(name, out WitnessObserver.BossGrant grant))
                return new RequirementDto(name, "boss-completion", grant.Rule.Area, grant.Sequence, null);
            return new RequirementDto(name, "area", name, satisfiedNow && reachBy.TryGetValue(name, out AreaReachInfo area) ? area.Sequence : null, null);
        }

        private static Entrance Lookup(Graph graph, string name)
            => name != null && graph.entranceIds.TryGetValue(name, out Entrance entrance) ? entrance : null;

        private static bool HasTag(Side side, string tag) => side != null && side.HasTag(tag);

        private static bool HasEntranceTag(Entrance entrance, string tag) => entrance != null && entrance.HasTag(tag);

        private static string[] GateTags(Entrance entrance, Side side)
        {
            var tags = new SortedSet<string>(StringComparer.Ordinal);
            if (entrance != null) foreach (string tag in entrance.TagList) tags.Add(tag);
            if (side != null) foreach (string tag in side.TagList) tags.Add("side:" + tag);
            return tags.ToArray();
        }

        // ---- main route: dependency closure ordered by the solver's own sequence -----------------------------------------------------------------------

        private static StepDto[] BuildRoute(
            string target, WitnessObserver observer, Graph graph, Dictionary<string, AreaReachInfo> reachBy,
            Dictionary<string, WitnessObserver.BossGrant> grantBy, Dictionary<string, Item> keyItems, Dictionary<string, Area> areas,
            Func<AreaReachInfo, EdgeDto> describe, Func<string, ItemDto> itemOf)
        {
            var routeAreas = new SortedSet<int>();
            var dependenciesOf = new Dictionary<int, string[]>();
            var acquired = new SortedSet<string>(StringComparer.Ordinal);
            var granted = new SortedSet<string>(StringComparer.Ordinal);
            var bySequence = observer.Reaches.ToDictionary(r => r.Sequence, r => r);

            float SequenceOf(string name)
            {
                if (graph.itemAreas.TryGetValue(name, out string itemArea))
                    return itemArea != null && reachBy.TryGetValue(itemArea, out AreaReachInfo holder) ? holder.Sequence : 10000000;
                if (grantBy.TryGetValue(name, out WitnessObserver.BossGrant grant)) return grant.Sequence;
                return reachBy.TryGetValue(name, out AreaReachInfo area) ? area.Sequence : 10000000;
            }

            void AddArea(string area)
            {
                if (!reachBy.TryGetValue(area, out AreaReachInfo reach)) throw new InvalidOperationException($"Area {area} was never reached by the solver.");
                if (!routeAreas.Add(reach.Sequence)) return;
                WitnessObserver.AreaReach record = bySequence[reach.Sequence];
                if (record.Edge == null) return;
                AddArea(record.FromArea);
                if (record.Condition == null) return;
                // The same selection the checker uses for an edge's dependencies: every AND term, the cheapest satisfied OR term (here "cheapest" is "proved first").
                string[] dependencies = record.Condition.Cost(SequenceOf).Item1.Distinct(StringComparer.Ordinal).ToArray();
                dependenciesOf[reach.Sequence] = dependencies;
                foreach (string dependency in dependencies)
                {
                    if (graph.itemAreas.TryGetValue(dependency, out string itemArea))
                    {
                        acquired.Add(dependency);
                        AddArea(itemArea);
                    }
                    else if (grantBy.TryGetValue(dependency, out WitnessObserver.BossGrant grant))
                    {
                        granted.Add(dependency);
                        AddArea(grant.Rule.Area);
                    }
                    else AddArea(dependency);
                }
            }
            AddArea(target);

            // Order: the closure ordered by the solver's own sequence is always valid, but it interleaves separate branches. Walk it greedily instead: among the
            // steps whose prerequisites are already met, prefer one that leaves the Area the player is standing in, then the lowest sequence. Deterministic, and every
            // remaining "first be in X" is then a real return the solver assumes rather than a leftover of its search order.
            var pending = new SortedSet<int>(routeAreas);
            var reachedAreas = new HashSet<string>(StringComparer.Ordinal);
            int startSequence = pending.First();
            var ordered = new List<int>();
            string standing = null;
            bool Ready(int sequence)
            {
                WitnessObserver.AreaReach reach = bySequence[sequence];
                if (reach.Edge == null) return true;
                if (!reachedAreas.Contains(reach.FromArea)) return false;
                foreach (string dependency in dependenciesOf.TryGetValue(sequence, out string[] list) ? list : Array.Empty<string>())
                {
                    string needed = graph.itemAreas.TryGetValue(dependency, out string itemArea) ? itemArea
                        : grantBy.TryGetValue(dependency, out WitnessObserver.BossGrant grant) ? grant.Rule.Area : dependency;
                    if (needed == null || !reachedAreas.Contains(needed)) return false;
                }
                return true;
            }
            while (pending.Count > 0)
            {
                int next = pending.Where(Ready).Where(sequence => bySequence[sequence].FromArea == standing).DefaultIfEmpty(-1).First();
                if (next < 0) next = pending.First(Ready);
                pending.Remove(next);
                ordered.Add(next);
                reachedAreas.Add(bySequence[next].Area);
                standing = bySequence[next].Area;
            }
            if (ordered[0] != startSequence) throw new InvalidOperationException("The route must begin at the start Area.");

            var steps = new List<StepDto>();
            string where = null;
            foreach (int sequence in ordered)
            {
                WitnessObserver.AreaReach reach = bySequence[sequence];
                var notes = new List<string>();
                if (reach.Edge == null)
                {
                    steps.Add(new StepDto(steps.Count + 1, "start", reach.Area, sequence, null, null, null, null, new[] { "The player starts here." }));
                }
                else
                {
                    EdgeDto edge = describe(new AreaReachInfo(reach));
                    string returnTo = where != reach.FromArea ? reach.FromArea : null;
                    if (edge.Glitched) notes.Add("GLITCHED LOGIC REQUIRED");
                    steps.Add(new StepDto(steps.Count + 1, "cross", reach.Area, sequence, returnTo, edge, null, null, notes.ToArray()));
                }
                where = reach.Area;
                foreach (string item in reach.KeyItemsGained.OrderBy(n => n, StringComparer.Ordinal).Where(acquired.Contains))
                    steps.Add(new StepDto(steps.Count + 1, "acquire", reach.Area, sequence, null, null, itemOf(item), null,
                        new[] { "The solver considers this item obtained once this Area has been reached." }));
                foreach (WitnessObserver.BossGrant grant in observer.BossGrants.Where(g => g.Rule.Area == reach.Area && granted.Contains(g.Name)))
                    steps.Add(new StepDto(steps.Count + 1, "boss-completion", reach.Area, sequence, null, null, null,
                        new CompletionDto(grant.Sequence, grant.Name, grant.Rule.Area, grant.Rule.EventId, grant.Rule.TriggerEntrance, grant.TriggerEdge.From),
                        new[] { "Assumed: the boss of this encounter is defeated after arriving through its entrance." }));
            }
            return steps.ToArray();
        }
    }
}
