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

        /// <summary>
        /// Returns the merged event, or null with a reason when the edits may interact.
        /// When a side's edit script is ambiguous (repeated instructions), the earliest and latest
        /// alignments are both tried. The merge is accepted only if the other side does not edit the
        /// span where they disagree (so every alignment in between yields the same interleaving) and
        /// every combination passes all rules and produces the identical event.
        /// </summary>
        public static EMEVD.Event Merge(EMEVD.Event b, EMEVD.Event c, EMEVD.Event t, out string reason)
        {
            reason = null;
            if (b.RestBehavior != c.RestBehavior || b.RestBehavior != t.RestBehavior)
                return Refuse(out reason, "rest behavior differs between base, combined and FogMod");

            // Skips are aligned ignoring only their line count, so a count change is not an edit
            // hunk; every changed count must then be validated as a derived adjustment.
            List<List<Hunk>> combinedAlignments = Alignments(b.Instructions, c.Instructions);
            List<List<Hunk>> fogAlignments = Alignments(b.Instructions, t.Instructions);
            foreach ((string side, List<List<Hunk>> own, List<List<Hunk>> other) in new[]
                     { ("combined", combinedAlignments, fogAlignments), ("FogMod", fogAlignments, combinedAlignments) })
            {
                if (own.Count < 2) continue;
                (int spanStart, int spanEnd) = DisagreementSpan(own[0], own[1]);
                Hunk touching = other.SelectMany(h => h).FirstOrDefault(h => Math.Max(h.Start, spanStart) <= Math.Min(h.End, spanEnd));
                if (touching != null)
                    return Refuse(out reason, $"the {side} edit has an ambiguous alignment (repeated instructions) over base {spanStart}..{spanEnd}, which the other side edits");
            }

            // Alignment only attributes identity among identical instructions; semantics depend on
            // content. It is enough that one interpretation passes every rule, provided every passing
            // interpretation yields the identical merged event.
            EMEVD.Event result = null;
            string firstRefusal = null;
            foreach (List<Hunk> combined in combinedAlignments)
            {
                foreach (List<Hunk> fog in fogAlignments)
                {
                    EMEVD.Event merged = MergeWith(b, c, t, combined, fog, out string why);
                    if (merged == null) { firstRefusal ??= why; continue; }
                    if (result == null) result = merged;
                    else if (!EmevdMerge.SameEvent(result, merged))
                        return Refuse(out reason, "alternative alignments of repeated instructions give different merged events");
                }
            }
            if (result == null) return Refuse(out reason, firstRefusal);
            return result;
        }

        /// <summary>Earliest alignment, plus the latest one when it differs (ambiguous edit script).</summary>
        internal static List<List<Hunk>> Alignments(IReadOnlyList<EMEVD.Instruction> from, IReadOnlyList<EMEVD.Instruction> to)
        {
            Func<EMEVD.Instruction, EMEVD.Instruction, bool> same = SameIgnoringSkipCount;
            List<Hunk> forward = Align(from, to, same);
            List<Hunk> backward = Mirror(Align(from.Reverse().ToList(), to.Reverse().ToList(), same), from.Count);
            return SameHunks(forward, backward) ? new List<List<Hunk>> { forward } : new List<List<Hunk>> { forward, backward };
        }

        /// <summary>Base span [start, end] covered by the hunks on which two alignments disagree.</summary>
        private static (int Start, int End) DisagreementSpan(List<Hunk> a, List<Hunk> b)
        {
            bool Shared(Hunk h, List<Hunk> list) => list.Any(o => o.Start == h.Start && o.End == h.End
                && o.Added.Count == h.Added.Count && o.Added.Zip(h.Added).All(p => EmevdMerge.SameInstruction(p.First, p.Second)));
            var differing = a.Where(h => !Shared(h, b)).Concat(b.Where(h => !Shared(h, a))).ToList();
            return (differing.Min(h => h.Start), differing.Max(h => h.End));
        }

        /// <summary>Merges one fixed pair of edit scripts (see <see cref="Merge"/>).</summary>
        private static EMEVD.Event MergeWith(EMEVD.Event b, EMEVD.Event c, EMEVD.Event t, List<Hunk> combined, List<Hunk> fog, out string reason)
        {
            reason = null;

            // Derived skip-count adjustments: base skip index -> (side, adjusted instruction, the side's hunks inside).
            var derived = new Dictionary<int, (string Side, EMEVD.Instruction Skip, HashSet<Hunk> Inside)>();
            foreach ((string side, EMEVD.Event x, List<Hunk> hunks, List<Hunk> other) in new[] { ("combined", c, combined, fog), ("FogMod", t, fog, combined) })
            {
                int[] baseToSide = BaseToSide(b.Instructions.Count, hunks, out Dictionary<Hunk, int> addedAt);
                for (int p = 0; p < b.Instructions.Count; p++)
                {
                    if (!IsSkip(b.Instructions[p]) || baseToSide[p] < 0) continue;
                    EMEVD.Instruction adjusted = x.Instructions[baseToSide[p]];
                    if (Lines(adjusted) == Lines(b.Instructions[p])) continue;
                    HashSet<Hunk> inside = ValidateDerived(b, x, hunks, other, baseToSide, addedAt, p, out string why);
                    if (inside == null)
                        return Refuse(out reason, $"{side} changes control-flow instruction {Name(b.Instructions[p])} at base {p} (skip count {Lines(b.Instructions[p])} -> {Lines(adjusted)}: {why})");
                    if (derived.ContainsKey(p))
                        return Refuse(out reason, $"both sides change the count of {Name(b.Instructions[p])} at base {p}");
                    derived[p] = (side, adjusted, inside);
                }
            }

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

            // Skip ranges: no edit that changes the line count may touch the lines a skip covers
            // ([p+1, p+n]), because the unchanged skip count would then cover different lines. An
            // edit starting right after the covered lines (p+n+1) leaves the covered lines in place.
            // A count-preserving replacement keeps every position; it still passes the control-flow,
            // condition and overlap rules above.
            for (int p = 0; p < b.Instructions.Count; p++)
            {
                EMEVD.Instruction skip = b.Instructions[p];
                if (!IsSkip(skip)) continue;
                int lines = skip.ArgData.Length > 0 ? skip.ArgData[0] : 0;
                int first = p + 1, lastCovered = p + lines;
                derived.TryGetValue(p, out var adjustment);
                foreach ((string side, List<Hunk> hunks) in new[] { ("combined", combined), ("FogMod", fog) })
                {
                    bool adjustingSide = adjustment.Side == side;
                    bool otherSide = adjustment.Side != null && !adjustingSide;
                    Hunk inside = hunks.FirstOrDefault(h => TouchesCovered(h, first, lastCovered)
                        && (otherSide || (h.Added.Count != h.End - h.Start && !(adjustingSide && adjustment.Inside.Contains(h)))));
                    if (inside != null)
                        return Refuse(out reason, otherSide
                            ? $"{side} edit at base {inside.Start} is inside the {lines}-line range of {Name(skip)} at base {p}, whose count the other side adjusts"
                            : $"{side} edit at base {inside.Start} is inside the {lines}-line range of {Name(skip)} at base {p}");
                }
            }

            if (b.Parameters.Any(prm => derived.ContainsKey((int)prm.InstructionIndex) && prm.TargetStartByte <= 0 && prm.TargetStartByte + prm.ByteCount > 0))
                return Refuse(out reason, "an adjusted skip count comes from an event parameter");

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
                merged.Instructions.Add(derived.TryGetValue(i, out var adjusted) ? adjusted.Skip : b.Instructions[i]);
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
        public static List<Hunk> Diff(IReadOnlyList<EMEVD.Instruction> from, IReadOnlyList<EMEVD.Instruction> to, out string reason,
            string side = "side", bool ignoreSkipCounts = false)
        {
            reason = null;
            Func<EMEVD.Instruction, EMEVD.Instruction, bool> same = ignoreSkipCounts ? SameIgnoringSkipCount : EmevdMerge.SameInstruction;
            List<Hunk> forward = Align(from, to, same);
            List<Hunk> backward = Mirror(Align(from.Reverse().ToList(), to.Reverse().ToList(), same), from.Count);
            if (!SameHunks(forward, backward))
            {
                reason = $"the {side} edit has an ambiguous alignment (repeated instructions)";
                return null;
            }
            return forward;
        }

        private static List<Hunk> Align(IReadOnlyList<EMEVD.Instruction> a, IReadOnlyList<EMEVD.Instruction> b,
            Func<EMEVD.Instruction, EMEVD.Instruction, bool> same)
        {
            int n = a.Count, m = b.Count;
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = same(a[i], b[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

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
                if (x < n && y < m && same(a[x], b[y]) && lcs[x, y] == lcs[x + 1, y + 1] + 1)
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

        public static int Lines(EMEVD.Instruction skip) => skip.ArgData.Length > 0 ? skip.ArgData[0] : 0;

        /// <summary>
        /// True when a hunk deletes a covered base line in [first, lastCovered], or inserts at a point
        /// inside the covered block (right after the skip up to before its last covered line).
        /// </summary>
        private static bool TouchesCovered(Hunk h, int first, int lastCovered) =>
            h.End > h.Start
                ? h.Start <= lastCovered && h.End - 1 >= first
                : h.Start >= first && h.Start <= lastCovered;

        /// <summary>Equal instructions, except that two skips may differ in their line count (byte 0) only.</summary>
        private static bool SameIgnoringSkipCount(EMEVD.Instruction a, EMEVD.Instruction b)
        {
            if (!IsSkip(a) || !IsSkip(b)) return EmevdMerge.SameInstruction(a, b);
            return a.Bank == b.Bank && a.ID == b.ID && Equals(a.Layer, b.Layer)
                && a.ArgData.Length == b.ArgData.Length && a.ArgData.Length > 0
                && a.ArgData.AsSpan(1).SequenceEqual(b.ArgData.AsSpan(1));
        }

        /// <summary>Side index of each base instruction (-1 when deleted) and side index of each hunk's first added instruction.</summary>
        private static int[] BaseToSide(int baseCount, List<Hunk> hunks, out Dictionary<Hunk, int> addedAt)
        {
            var map = new int[baseCount];
            addedAt = new Dictionary<Hunk, int>();
            var deleted = new HashSet<int>(hunks.SelectMany(h => Enumerable.Range(h.Start, h.End - h.Start)));
            int position = 0;
            for (int i = 0; i <= baseCount; i++)
            {
                foreach (Hunk h in hunks.Where(h => h.Start == i))
                {
                    addedAt[h] = position;
                    position += h.Added.Count;
                }
                if (i == baseCount) break;
                map[i] = deleted.Contains(i) ? -1 : position++;
            }
            return map;
        }

        /// <summary>
        /// Validates that side <paramref name="x"/>'s change of the count of the base skip at
        /// <paramref name="p"/> is a mechanical consequence of its own insertions/deletions inside that
        /// skip's range: every such hunk lies wholly inside the range (or is a pure insertion at the end
        /// of it), the other side does not edit the range, and the adjusted skip covers exactly the
        /// surviving base lines of the range plus the side's own inserted lines. Returns the side's
        /// hunks that are accounted for, or null with a reason.
        /// </summary>
        private static HashSet<Hunk> ValidateDerived(EMEVD.Event b, EMEVD.Event x, List<Hunk> hunks, List<Hunk> other,
            int[] baseToSide, Dictionary<Hunk, int> addedAt, int p, out string why)
        {
            why = null;
            int n = Lines(b.Instructions[p]);
            int first = p + 1, lastCovered = p + n;
            if (lastCovered >= b.Instructions.Count) { why = "the base range runs past the end of the event"; return null; }
            Hunk otherInside = other.FirstOrDefault(h => TouchesCovered(h, first, lastCovered));
            if (otherInside != null) { why = $"the other side edits the range at base {otherInside.Start}"; return null; }

            var inside = new HashSet<Hunk>();
            Hunk boundary = null;
            foreach (Hunk h in hunks)
            {
                bool deletes = h.End > h.Start;
                if (deletes && h.Start >= first && h.End - 1 <= lastCovered) inside.Add(h);
                else if (deletes && Math.Max(h.Start, first) <= Math.Min(h.End - 1, lastCovered)) { why = $"the edit at base {h.Start} straddles the range boundary"; return null; }
                else if (!deletes && h.Start >= first && h.Start <= lastCovered) inside.Add(h);
                else if (!deletes && h.Start == lastCovered + 1) boundary = h;
            }

            int q = baseToSide[p];
            var covered = Enumerable.Range(q + 1, Lines(x.Instructions[q])).ToHashSet();
            var expected = Enumerable.Range(first, n).Where(i => baseToSide[i] >= 0).Select(i => baseToSide[i]).ToHashSet();
            foreach (Hunk h in inside) expected.UnionWith(Enumerable.Range(addedAt[h], h.Added.Count));
            if (covered.SetEquals(expected)) return inside;
            if (boundary != null)
            {
                // A pure insertion right after the last covered line is inside only if the side's count includes it.
                expected.UnionWith(Enumerable.Range(addedAt[boundary], boundary.Added.Count));
                if (covered.SetEquals(expected))
                {
                    inside.Add(boundary);
                    return inside;
                }
            }
            why = "the adjusted count does not cover exactly the surviving lines plus the side's own insertions";
            return null;
        }

        private static string Name(EMEVD.Instruction i) => $"{i.Bank}[{i.ID:d2}]";

        private static EMEVD.Event Refuse(out string reason, string why)
        {
            reason = why;
            return null;
        }
    }
}
