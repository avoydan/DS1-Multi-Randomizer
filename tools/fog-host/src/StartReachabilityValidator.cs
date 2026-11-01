using System;
using System.Linq;
using FogMod;

namespace FogHost
{
    /// <summary>
    /// The general safety postcondition of generation profile ds1tr-gen-v6 (Compact Fog v3): after FogMod's connector has paired the fog gates, every logical Area
    /// must be reachable from the start Area through the graph. This is the rule that rules out start traps (the start region's only exit paired with a
    /// dead-end pocket), terminal pockets and dead-end cycles in one statement, for any boss or region, instead of checking one encounter.
    ///
    /// The rule is only as true as the graph. The v3 catalog is what makes it physical at the Asylum start: the hallway has no unconditional exit, so the
    /// connector repairs (up to 100 tries) toward layouts that reach the world through the randomized wall instead of through a fake edge. The check is
    /// deliberately not stricter than the graph: a stricter runtime predicate would be invisible to the connector and could only reject.
    ///
    /// A failure is retryable (<c>FOG_SEED_UNSOLVABLE</c>, scope <c>fog</c>) like the connector's own failures: the next deterministic Fog seed is drawn.
    /// </summary>
    public static class StartReachabilityValidator
    {
        public static void Validate(RandomizerOptions options, Graph graph)
        {
            string start = graph.start?.Area ?? throw new InvalidOperationException("The graph has no start Area to check reachability from.");
            GraphChecker.CheckRecord check = new GraphChecker().Check(options, graph, start);
            string[] unvisited = (check.Unvisited ?? new System.Collections.Generic.List<string>()).OrderBy(area => area, StringComparer.Ordinal).ToArray();
            if (unvisited.Length > 0)
            {
                throw new FogHostException(
                    HostErrors.SeedUnsolvable,
                    $"Start region '{start}' cannot reach [{string.Join(", ", unvisited)}] after the fog gates were connected.",
                    retryable: true,
                    retryScope: "fog")
                { Stage = "solve-graph" };
            }
        }
    }
}
