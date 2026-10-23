using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static FogMod.Util;
using static FogMod.AnnotationData;
using static FogMod.Graph;

// Modified from FogMod commit 6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f by DS1 Multi Randomizer contributors under Apache-2.0; see ../UPSTREAM_PIN.md.
namespace FogMod
{
    public class GraphChecker
    {
        public class CheckRecord
        {
            public Dictionary<string, NodeRecord> Records { get; set; }
            public List<string> Unvisited { get; set; }
            public List<string> BossCompletions { get; set; }
            public List<string> UnmetBossCompletionEdges { get; set; }
        }
        public class NodeRecord
        {
            public string Area { get; set; }
            public float Dist { get; set; }
            public List<string> Visited = new List<string>();
            public Dictionary<Edge, float> InEdge = new Dictionary<Edge, float>();
        }
        public CheckRecord Check(RandomizerOptions opt, Graph g, string start,
            IEnumerable<string> initiallyCompletedBossCompletions = null, Func<Edge, bool> edgeAllowed = null)
        {
            Dictionary<string, Node> graph = g.graph;
            foreach (Node node in graph.Values)
            {
                foreach (Edge edge in node.To)
                {
                    if (edge.Link == null) throw new Exception($"Unlinked {edge} leaving {node.Area}");
                }
                foreach (Edge edge in node.From)
                {
                    if (edge.Link == null) throw new Exception($"Unlinked {edge} entering {node.Area}");
                }
            }
            Dictionary<string, Expr> config = new Dictionary<string, Expr>();
            HashSet<string> visited = new HashSet<string>();
            HashSet<string> bossCompletions = new HashSet<string>(StringComparer.Ordinal);
            foreach (string completion in initiallyCompletedBossCompletions ?? Array.Empty<string>())
            {
                if (g.bossCompletions == null || !g.bossCompletions.ContainsKey(completion))
                    throw new ArgumentException($"Unknown initial boss-completion state '{completion}'.", nameof(initiallyCompletedBossCompletions));
                bossCompletions.Add(completion);
                config[completion] = Expr.TRUE;
            }
            void visit(string area)
            {
                visited.Add(area);
                config[area] = Expr.TRUE;
                foreach (string item in graph[area].Items)
                {
                    config[item] = Expr.TRUE;
                }
            }
            HashSet<Edge> seenEdge = new HashSet<Edge>();
            Dictionary<string, NodeRecord> recs = new Dictionary<string, NodeRecord>();
            visit(start);
            recs[start] = new NodeRecord { Area = start, Dist = 0 };
            bool lastLoop = false;
            Dictionary<Edge, float> duplicatePaths = new Dictionary<Edge, float>();
            Dictionary<string, int> areaCost = graph.Values.ToDictionary(n => n.Area, n => n.Cost);
            Dictionary<string, float> extraAreaCost = graph.Values.ToDictionary(n => n.Area, n => 0f);
            bool getDepRecord(string dep, out NodeRecord nr)
            {
                if (g.itemAreas.TryGetValue(dep, out string itemArea)) dep = itemArea;
                else if (g.bossCompletions.TryGetValue(dep, out BossCompletionRule boss))
                {
                    if (!bossCompletions.Contains(dep)) { nr = null; return false; }
                    dep = boss.Area;
                }
                return recs.TryGetValue(dep, out nr);
            }
            float addEdge(Edge e, string scalingBase)
            {
                if (!recs.TryGetValue(e.From, out NodeRecord from)) throw new Exception($"Internal error: {e} cannot calculate distance");
                if (!recs.TryGetValue(e.To, out NodeRecord to))
                {
                    to = recs[e.To] = new NodeRecord { Area = e.To };
                }
                List<string> visitedCand;
                if (e.LinkedExpr == null)
                {
                    visitedCand = from.Visited.ToList();
                    if (e.From != e.To) visitedCand.Add(e.From);
                }
                else
                {
                    HashSet<string> preVisit = new HashSet<string>(from.Visited);
                    List<string> depVisit = e.LinkedExpr.Cost(area => getDepRecord(area, out NodeRecord nr) ? nr.Dist : 10000000).Item1;
                    foreach (string dep in depVisit)
                    {
                        if (!getDepRecord(dep, out NodeRecord nr)) throw new Exception($"Dependency in edge {e} not visited: {dep}");
                        string depArea = g.itemAreas.TryGetValue(dep, out string itemArea)
                            ? itemArea
                            : g.bossCompletions.TryGetValue(dep, out BossCompletionRule boss) ? boss.Area : dep;
                        preVisit.Add(depArea);
                        preVisit.UnionWith(nr.Visited);
                    }
                    visitedCand = preVisit.ToList();
                }
                float candDist = visitedCand.Select(c => areaCost[c] + extraAreaCost[c]).Sum();
                if ((to.Dist == 0 && to.Area != start) || candDist < to.Dist)
                {
                    to.Dist = candDist;
                    to.Visited = visitedCand;
                    if (scalingBase != null)
                    {
                        if (!recs.TryGetValue(scalingBase, out NodeRecord scale)) throw new Exception($"Internal error: {e} can't find scaling base {scale}");
                        float scaleDist = scale.Dist;
                        // Allow this solely in the case of ToTG being given too high a ranking, given initial difficulty spike
                        // if (to.Dist > scaleDist) throw new Exception($"Calculated distance {to.Dist} of {e} greater than scaling base {scalingBase} at {scaleDist}; scaling base not needed");
                        extraAreaCost[to.Area] = scaleDist - to.Dist;
                        to.Dist = scaleDist;
                        Console.WriteLine($"TTT Setting scaling base of {to.Area} to {scalingBase} {scaleDist}");
                    }
                }
                return candDist;
            }
            while (true)
            {
                if (opt["explain"]) Console.WriteLine("---");
                int diff = visited.Count + bossCompletions.Count;
                foreach (string loc in visited.OrderBy(l => recs[l].Dist).ToList())
                {
                    Node node = graph[loc];
                    // Implicit condition on every edge
                    foreach (Edge edge in node.To)
                    {
                        // Ideally shouldn't happen
                        if (edge.To == null) continue;
                        if (edgeAllowed != null && !edgeAllowed(edge)) continue;
                        // This won't work if one-way is paired with two-way in the future
                        if (seenEdge.Contains(edge) || seenEdge.Contains(edge.Link.Pair)) continue;
                        if (edge.LinkedExpr != null)
                        {
                            Expr simp = edge.LinkedExpr.Substitute(config).Simplify();
                            if (!simp.IsTrue())
                            {
                                if (lastLoop && opt["explain"]) Console.WriteLine($"Missing: {loc} -> {edge.To}. Condition {edge.LinkedExpr} -> {simp}");
                                continue;
                            }
                        }
                        Node toNode = graph[edge.To];
                        if (toNode.ScalingBase != null && !visited.Contains(toNode.ScalingBase))
                        {
                            if (lastLoop && opt["explain"]) Console.WriteLine($"Missing: {loc} -> {edge.To}. Condition {toNode.ScalingBase} for scaling");
                            continue;
                        }
                        float dist = addEdge(edge, toNode.ScalingBase);
                        foreach (KeyValuePair<string, BossCompletionRule> completion in g.bossCompletions)
                        {
                            if (IsBossCompletionTriggerEdge(edge, completion.Value)
                                && bossCompletions.Add(completion.Key))
                            {
                                config[completion.Key] = Expr.TRUE;
                                if (opt["explain"]) Console.WriteLine($"Boss completion achievable: {completion.Value.EventId} via {completion.Value.TriggerEntrance}");
                            }
                        }
                        seenEdge.Add(edge);
                        recs[edge.To].InEdge[edge] = dist;
                        if (visited.Contains(edge.To))
                        {
                            if (edge.Name != null && opt["explain"]) Console.WriteLine($"    Additional path: {loc} -> {edge.To} ({dist}) - {edge.Text} -> {(edge.Link == null ? "" : edge.Link.Text)}");
                        }
                        else
                        {
                            visit(edge.To);
                            if (opt["explain"]) Console.WriteLine($"{loc} -> {edge.To} ({dist}) ({edge.Text} -> {(edge.Link == null ? "" : edge.Link.Text)}{(edge.LinkedExpr == null ? "" : $", if {edge.LinkedExpr}")})");
                        }
                    }
                }
                if (lastLoop)
                {
                    break;
                }
                else if (visited.Count + bossCompletions.Count == diff)
                {
                    lastLoop = true;
                }
            }
            List<string> unvisited = graph.Keys.Except(visited).Except(g.areas.Values.Where(a => a.HasTag("optional")).Select(a => a.Name)).ToList();
            List<string> unmetBossCompletionEdges = graph.Values.SelectMany(node => node.To)
                .Where(edge => edge.To != null)
                .Select(edge => (Edge: edge, Missing: edge.LinkedExpr?.FreeVars()
                    .Where(name => g.bossCompletions.ContainsKey(name) && !bossCompletions.Contains(name))
                    .OrderBy(name => name, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>()))
                .Where(entry => entry.Missing.Length > 0)
                .Select(entry => $"{entry.Edge.From}->{entry.Edge.To} requires {string.Join(" & ", entry.Missing)}")
                .OrderBy(value => value, StringComparer.Ordinal).ToList();

            if (opt["explain"]) Console.WriteLine($"Not visited: [{string.Join(", ", unvisited)}]");
            if (opt["explain"]) Console.WriteLine();

            return new CheckRecord
            {
                Records = recs,
                Unvisited = unvisited,
                BossCompletions = bossCompletions.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                UnmetBossCompletionEdges = unmetBossCompletionEdges,
            };
        }

        /// <summary>
        /// A broad Area visit is not a boss defeat. The completion state is derivable only after a
        /// directed arrival through the exact authored boss entrance with FogMod's default landing;
        /// the pristine event/region pair is validated when the host builds this rule. The game still
        /// sets the completion flag only when the boss dies.
        /// </summary>
        public static bool IsBossCompletionTriggerEdge(Edge edge, BossCompletionRule completion)
        {
            if (edge?.Link?.Side == null || completion == null || edge.To != completion.Area) return false;
            Side side = edge.Link.Side;
            if (side.Area != completion.Area || side.BossTrigger != 0
                || side.BossTriggerArea != null || side.CustomWarp != null
                || completion.TriggerEventId <= 0 || side.BossEncounterStartEventId != completion.TriggerEventId
                || completion.TriggerRegionEntityId <= 0
                || !string.Equals(edge.Link.Name, completion.TriggerEntrance, StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
