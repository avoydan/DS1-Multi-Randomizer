using System;
using System.Linq;
using FogMod;
using static FogMod.AnnotationData;

namespace FogHost
{
    /// <summary>
    /// Validates that each V5 boss-gated physical route is reachable from its encounter after
    /// defeating that boss, by taking that encounter's actual exit and then using only fixed
    /// physical transitions. Ordinary FogMod reachability can otherwise escape through unrelated
    /// randomized Fog gates and mistake global world connectivity for a physical post-boss route.
    /// </summary>
    public static class BossProgressionValidator
    {
        public static void ValidatePostBossRoutes(RandomizerOptions options, Graph graph, Annotations annotations)
        {
            foreach (BossCompletionRule completion in annotations.BossCompletions?.AsEnumerable() ?? Enumerable.Empty<BossCompletionRule>())
            {
                if (!completion.RequireFixedPostBossRoute) continue;
                string[] destinations = graph.graph.Values
                    .SelectMany(node => node.To)
                    .Where(edge => edge.LinkedExpr != null && edge.LinkedExpr.FreeVars().Contains(completion.Name, StringComparer.Ordinal))
                    .Select(edge => edge.To)
                    .Where(destination => destination != null)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(destination => destination, StringComparer.Ordinal)
                    .ToArray();
                if (destinations.Length == 0)
                    throw new InvalidOperationException($"Boss completion {completion.EventId} has no boss-gated route in the generated graph.");

                var checker = new GraphChecker();
                GraphChecker.CheckRecord globalAfterDefeat = checker.Check(
                    options, graph, completion.Area, new[] { completion.Name });
                GraphChecker.CheckRecord physicalAfterDefeat = checker.Check(
                    options,
                    graph,
                    completion.Area,
                    new[] { completion.Name },
                    edge => edge.IsFixed || (edge.From == completion.Area
                        && string.Equals(edge.Name, completion.TriggerEntrance, StringComparison.Ordinal)));
                string[] unreachable = destinations.Where(destination => !physicalAfterDefeat.Records.ContainsKey(destination)).ToArray();
                string[] triggerDestinations = graph.graph.Values
                    .SelectMany(node => node.To)
                    .Where(edge => edge.Name == completion.TriggerEntrance && edge.From == completion.Area)
                    .Select(edge => edge.To)
                    .Where(destination => destination != null)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(destination => destination, StringComparer.Ordinal)
                    .ToArray();
                Console.WriteLine($"V5 post-boss route event {completion.EventId}: exit '{completion.TriggerEntrance}' from '{completion.Area}'; global destinations=[{string.Join(", ", destinations.Where(globalAfterDefeat.Records.ContainsKey))}]; fixed-route destinations=[{string.Join(", ", destinations.Where(physicalAfterDefeat.Records.ContainsKey))}].");
                if (unreachable.Length > 0)
                {
                    throw new FogHostException(
                        HostErrors.SeedUnsolvable,
                        $"Post-boss route for event {completion.EventId} cannot reach [{string.Join(", ", unreachable)}] from encounter area '{completion.Area}' by taking its exit '{completion.TriggerEntrance}' (currently leads to [{string.Join(", ", triggerDestinations)}]) and following fixed physical routes; it would require another randomized entrance.",
                        retryable: true,
                        retryScope: "fog")
                    { Stage = "solve-graph" };
                }
            }
        }
    }
}
