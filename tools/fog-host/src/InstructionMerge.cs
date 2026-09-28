using System;
using System.Collections.Generic;
using System.Linq;
using SoulsFormats;

namespace FogHost
{
    /// <summary>
    /// Instruction-level three-way merge of one event edited by both the combined engine (C) and
    /// FogMod (T) relative to the pristine base (B). It succeeds only when the two edit sets are
    /// provably independent; otherwise it returns a reason and the caller reports FOG_MERGE_CONFLICT.
    ///
    /// Instruction classes follow the DS1 EMEDF (DarkScript3 ds1-common.emedf.json):
    /// banks 0-999 are conditions (they register into condition groups), banks 1000-1999 are
    /// control flow (WAIT / SKIP / END), banks 2000+ are commands. DS1 has no labels or GOTO;
    /// positional control flow is the "skip N lines" family listed in <see cref="SkipInstructions"/>.
    /// </summary>
    public static class InstructionMerge
    {
        /// <summary>DS1 instructions whose first argument byte is "Number Of Skipped Lines".</summary>
        public static readonly IReadOnlySet<(int Bank, int Id)> SkipInstructions = new HashSet<(int, int)>
        {
            (1000, 1), (1000, 3), (1000, 5), (1000, 7),
            (1003, 1), (1003, 3), (1003, 5), (1003, 7),
            (1005, 1),
        };

        /// <summary>
        /// A contiguous edit: base instructions [Start, End) are deleted and <see cref="Added"/> is
        /// inserted in their place (Start == End for a pure insertion before base index Start).
        /// </summary>
        public sealed record Hunk(int Start, int End, IReadOnlyList<EMEVD.Instruction> Added)
        {
            public IEnumerable<EMEVD.Instruction> Touched(IReadOnlyList<EMEVD.Instruction> baseInstructions) =>
                baseInstructions.Skip(Start).Take(End - Start).Concat(Added);
        }

        public static bool IsCondition(EMEVD.Instruction i) => i.Bank < 1000;

        public static bool IsControlFlow(EMEVD.Instruction i) => i.Bank >= 1000 && i.Bank < 2000;

        public static bool IsSkip(EMEVD.Instruction i) => SkipInstructions.Contains((i.Bank, i.ID));

        /// <summary>Returns the merged event, or null with a reason when the edits may interact.</summary>
        public static EMEVD.Event Merge(EMEVD.Event b, EMEVD.Event c, EMEVD.Event t, out string reason)
        {
            reason = null;
            if (b.RestBehavior != c.RestBehavior || b.RestBehavior != t.RestBehavior)
                return Refuse(out reason, "rest behavior differs between base, combined and FogMod");

            List<Hunk> combined = Diff(b.Instructions, c.Instructions, out reason, "combined");
            if (combined == null) return null;
            List<Hunk> fog = Diff(b.Instructions, t.Instructions, out reason, "FogMod");
            if (fog == null) return null;

            // Same instruction (or adjacent insertion point) changed by both sides.
            foreach (Hunk hc in combined)
            {
                foreach (Hunk ht in fog)
                {
                    if (Math.Max(hc.Start, ht.Start) <= Math.Min(hc.End, ht.End))
                        return Refuse(out reason, $"both sides change base instructions {Math.Min(hc.Start, ht.Start)}..{Math.Max(hc.End, ht.End)}");
                }
            }

            // Control flow (WAIT/SKIP/END) may not be inserted, deleted or modified by either side.
            foreach ((string side, List<Hunk> hunks) in new[] { ("combined", combined), ("FogMod", fog) })
            {
                foreach (Hunk h in hunks)
                {
                    EMEVD.Instruction flow = h.Touched(b.Instructions).FirstOrDefault(IsControlFlow);
                    if (flow != null) return Refuse(out reason, $"{side} changes control-flow instruction {Name(flow)} at base {h.Start}");
                }
            }

            // Condition groups: at most one side may change condition instructions.
            bool combinedConditions = combined.Any(h => h.Touched(b.Instructions).Any(IsCondition));
            bool fogConditions = fog.Any(h => h.Touched(b.Instructions).Any(IsCondition));
            if (combinedConditions && fogConditions) return Refuse(out reason, "both sides change condition-group instructions");

            // Skip ranges: no edit that changes the line count may touch the lines a skip covers or
            // the line right after them, because the unchanged skip count would then cover different
            // lines. A count-preserving replacement (as many instructions added as deleted) keeps
            // every position, so the skip covers the same lines; it still passes the control-flow,
            // condition and overlap rules above.
            for (int p = 0; p < b.Instructions.Count; p++)
            {
                EMEVD.Instruction skip = b.Instructions[p];
                if (!IsSkip(skip)) continue;
                int lines = skip.ArgData.Length > 0 ? skip.ArgData[0] : 0;
                int first = p + 1, last = p + lines + 1;
                foreach ((string side, List<Hunk> hunks) in new[] { ("combined", combined), ("FogMod", fog) })
                {
                    Hunk inside = hunks.FirstOrDefault(h => h.Added.Count != h.End - h.Start
                        && Math.Max(h.Start, first) <= Math.Min(h.End, last));
                    if (inside != null)
                        return Refuse(out reason, $"{side} edit at base {inside.Start} is inside the {lines}-line range of {Name(skip)} at base {p}");
                }
            }

            // Event parameters address instructions by index; keep them only if neither side changed them.
            var deleted = new HashSet<int>(combined.Concat(fog).SelectMany(h => Enumerable.Range(h.Start, h.End - h.Start)));
            if (b.Parameters.Any(prm => deleted.Contains((int)prm.InstructionIndex)))
                return Refuse(out reason, "an edit deletes a parameterized instruction");
            if (!SameParameters(b, c, combined) || !SameParameters(b, t, fog))
                return Refuse(out reason, "event parameters were changed");

            // Build the merged event from the base and both sets of hunks.
            var ordered = combined.Concat(fog).OrderBy(h => h.Start).ToList();
            var baseToMerged = new int[b.Instructions.Count];
            var merged = new EMEVD.Event(b.ID, b.RestBehavior);
            for (int i = 0; i <= b.Instructions.Count; i++)
            {
                foreach (Hunk h in ordered.Where(h => h.Start == i)) merged.Instructions.AddRange(h.Added);
                if (i == b.Instructions.Count) break;
                if (deleted.Contains(i)) { baseToMerged[i] = -1; continue; }
                baseToMerged[i] = merged.Instructions.Count;
                merged.Instructions.Add(b.Instructions[i]);
            }
            foreach (EMEVD.Parameter prm in b.Parameters)
            {
                merged.Parameters.Add(new EMEVD.Parameter(baseToMerged[(int)prm.InstructionIndex], prm.TargetStartByte, prm.SourceStartByte, prm.ByteCount)
                { UnkID = prm.UnkID });
            }
            return merged;
        }

        /// <summary>
        /// Minimal edit script from <paramref name="from"/> to <paramref name="to"/> (LCS). Refused as
        /// ambiguous when the earliest and latest alignments disagree (e.g. repeated instructions).
        /// </summary>
        public static List<Hunk> Diff(IReadOnlyList<EMEVD.Instruction> from, IReadOnlyList<EMEVD.Instruction> to, out string reason, string side = "side")
        {
            reason = null;
            List<Hunk> forward = Align(from, to);
            List<Hunk> backward = Mirror(Align(from.Reverse().ToList(), to.Reverse().ToList()), from.Count);
            if (!SameHunks(forward, backward))
            {
                reason = $"the {side} edit has an ambiguous alignment (repeated instructions)";
                return null;
            }
            return forward;
        }

        private static List<Hunk> Align(IReadOnlyList<EMEVD.Instruction> a, IReadOnlyList<EMEVD.Instruction> b)
        {
            int n = a.Count, m = b.Count;
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = EmevdMerge.SameInstruction(a[i], b[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            var hunks = new List<Hunk>();
            int x = 0, y = 0, start = -1;
            var added = new List<EMEVD.Instruction>();
            void Close(int end)
            {
                if (start >= 0) hunks.Add(new Hunk(start, end, added.ToList()));
                start = -1;
                added.Clear();
            }
            while (x < n || y < m)
            {
                if (x < n && y < m && EmevdMerge.SameInstruction(a[x], b[y]) && lcs[x, y] == lcs[x + 1, y + 1] + 1)
                {
                    Close(x);
                    x++; y++;
                }
                else if (x < n && (y == m || lcs[x + 1, y] >= lcs[x, y + 1]))
                {
                    if (start < 0) start = x;
                    x++;
                }
                else
                {
                    if (start < 0) start = x;
                    added.Add(b[y]);
                    y++;
                }
            }
            Close(x);
            return hunks;
        }

        private static List<Hunk> Mirror(List<Hunk> reversed, int count) =>
            reversed.Select(h => new Hunk(count - h.End, count - h.Start, h.Added.Reverse().ToList())).OrderBy(h => h.Start).ToList();

        private static bool SameHunks(List<Hunk> a, List<Hunk> b) =>
            a.Count == b.Count && a.Zip(b).All(p => p.First.Start == p.Second.Start && p.First.End == p.Second.End
                && p.First.Added.Count == p.Second.Added.Count
                && p.First.Added.Zip(p.Second.Added).All(q => EmevdMerge.SameInstruction(q.First, q.Second)));

        /// <summary>True when <paramref name="side"/>'s parameters are exactly the base parameters, re-indexed.</summary>
        private static bool SameParameters(EMEVD.Event b, EMEVD.Event side, List<Hunk> hunks)
        {
            if (b.Parameters.Count != side.Parameters.Count) return false;
            var deleted = new HashSet<int>(hunks.SelectMany(h => Enumerable.Range(h.Start, h.End - h.Start)));
            var map = new Dictionary<int, int>();
            int position = 0;
            for (int i = 0; i <= b.Instructions.Count; i++)
            {
                position += hunks.Where(h => h.Start == i).Sum(h => h.Added.Count);
                if (i == b.Instructions.Count) break;
                if (deleted.Contains(i)) continue;
                map[i] = position++;
            }
            return b.Parameters.Zip(side.Parameters).All(p =>
                map.TryGetValue((int)p.First.InstructionIndex, out int mapped) && mapped == p.Second.InstructionIndex
                && p.First.TargetStartByte == p.Second.TargetStartByte && p.First.SourceStartByte == p.Second.SourceStartByte
                && p.First.ByteCount == p.Second.ByteCount && p.First.UnkID == p.Second.UnkID);
        }

        private static string Name(EMEVD.Instruction i) => $"{i.Bank}[{i.ID:d2}]";

        private static EMEVD.Event Refuse(out string reason, string why)
        {
            reason = why;
            return null;
        }
    }
}
