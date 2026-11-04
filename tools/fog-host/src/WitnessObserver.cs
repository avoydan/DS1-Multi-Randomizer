using System;
using System.Collections.Generic;
using System.Linq;
using static FogMod.AnnotationData;
using static FogMod.Graph;

namespace FogMod
{
    /// <summary>
    /// Read-only recorder for <see cref="GraphChecker.Check"/>: it captures WHY the solver first believed each Area reachable, at the exact moment of
    /// that belief (the edge it expanded, the condition on that edge and which of the condition's variables were already true), not afterwards from
    /// state the search rewrites later (<c>NodeRecord.Dist</c>, <c>Visited</c> and <c>InEdge</c> are replaced by cheaper paths).
    ///
    /// It never changes the search: <c>Check</c> only calls the observer, passes it values it already computed and reads nothing back. With a null
    /// observer (the default; every other caller, including the connector) behavior and output are identical. Project-owned, so it can live beside the
    /// vendored checker (docs/SOLVER_WITNESS.md).
    /// </summary>
    public sealed class WitnessObserver
    {
        public sealed class AreaReach
        {
            /// <summary>Position in the order the solver proved Areas reachable; the start Area is 0.</summary>
            public int Sequence { get; init; }
            /// <summary>Search pass (one sweep over the visited Areas) in which the Area was first reached; 0 for the start.</summary>
            public int Pass { get; init; }
            public string Area { get; init; }
            /// <summary>The expanded edge (null for the start Area). Its <c>Link</c> is the matching entrance on the destination side.</summary>
            public Edge Edge { get; init; }
            /// <summary>The Area the solver expanded the edge from (the solver treats every already-visited Area as available).</summary>
            public string FromArea { get; init; }
            /// <summary>The edge's combined condition as the solver evaluated it, or null when unconditional.</summary>
            public Expr Condition { get; init; }
            /// <summary>Condition variables that were already true when the edge was expanded, in ordinal order.</summary>
            public IReadOnlyList<string> Satisfied { get; init; }
            /// <summary>Key items that become available because the solver visited this Area (what <c>Node.Items</c> holds), in graph order.</summary>
            public IReadOnlyList<string> KeyItemsGained { get; init; }
        }

        public sealed class BossGrant
        {
            public int Sequence { get; init; }
            public string Name { get; init; }
            public BossCompletionRule Rule { get; init; }
            public Edge TriggerEdge { get; init; }
        }

        private int sequence;
        private int pass;

        public List<AreaReach> Reaches { get; } = new List<AreaReach>();
        public List<BossGrant> BossGrants { get; } = new List<BossGrant>();

        internal void OnStart(string start, IEnumerable<string> keyItems)
        {
            Reaches.Add(new AreaReach
            {
                Sequence = sequence++, Pass = 0, Area = start, KeyItemsGained = keyItems.ToList(),
                Satisfied = Array.Empty<string>(),
            });
        }

        internal void BeginPass() => pass++;

        internal void OnBossCompletion(string name, BossCompletionRule rule, Edge triggerEdge)
        {
            BossGrants.Add(new BossGrant { Sequence = sequence++, Name = name, Rule = rule, TriggerEdge = triggerEdge });
        }

        internal void OnFirstReach(string from, Edge edge, IReadOnlyDictionary<string, Expr> config, IEnumerable<string> keyItems)
        {
            Expr condition = edge.LinkedExpr;
            string[] satisfied = condition == null
                ? Array.Empty<string>()
                : condition.FreeVars().Where(name => config.TryGetValue(name, out Expr value) && value.IsTrue())
                    .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Reaches.Add(new AreaReach
            {
                Sequence = sequence++, Pass = pass, Area = edge.To, Edge = edge, FromArea = from, Condition = condition,
                Satisfied = satisfied, KeyItemsGained = keyItems.ToList(),
            });
        }
    }
}
