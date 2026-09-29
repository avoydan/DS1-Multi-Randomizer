using System;
using System.Collections.Generic;
using System.Linq;
using SoulsFormats;

namespace FogHost
{
    /// <summary>
    /// Boss-event compatibility layer (FogHost 0.5.0). For whitelisted boss events edited by both
    /// engines, when the normal instruction-level merge refuses, FogMod's event is the control-flow and
    /// world-logic base and only the combined engine's known boss mutations are re-applied, by content:
    /// <list type="number">
    /// <item>replace a boss health bar (2003[11]) whose original instruction is uniquely identifiable;</item>
    /// <item>remove an instruction kind the combined engine removes for replaced enemies, when the exact
    /// original instruction is identifiable;</item>
    /// <item>insert boss-support instructions (AI state, AI re-plan, force animation) right after a
    /// unique content anchor, never by raw index;</item>
    /// <item>treat a mutation FogMod already performed as satisfied, never applying it twice.</item>
    /// </list>
    /// Every insertion/deletion adjusts enclosing skips (<see cref="SkipRanges"/>). Anything else,
    /// any ambiguity, dead code, a duplicate, or a change to FogMod's control flow fails the overlay.
    /// </summary>
    public static class BossOverlay
    {
        /// <summary>Boss events measured on real-format data (docs/FOGHOST_INTEGRATION.md).</summary>
        public static readonly IReadOnlySet<long> BossEvents = new HashSet<long>
        {
            11005392, 11010000, 11210001, 11405392, 11415342, 11415373, 11415382, 11515392,
            11600001, 11700000, 11810000, 11810310, 11815382, 11815390, 11815394,
        };

        /// <summary>
        /// Instruction kinds the combined engine removes for a replaced enemy (DsrDataTool
        /// IsModelSpecificEnemyInstruction, IsPortableBossSpecificInstruction and
        /// IsStaticEnemyLifecycleInstruction). All are commands (banks 2003/2004).
        /// </summary>
        private static readonly HashSet<(int Bank, int Id)> Removable = new HashSet<(int, int)>(
            new[] { (2003, 18) }.Concat(new[]
            {
                1, 5, 8, 9, 10, 12, 13, 15, 17, 21, 22, 26, 27, 28, 29, 30, 31, 32, 35, 36, 40, 41, 42,
            }.Select(id => (2004, id))));

        /// <summary>Boss-support instructions the combined engine inserts (AI state, AI re-plan, force animation).</summary>
        private static readonly HashSet<(int Bank, int Id)> Insertable = new HashSet<(int, int)> { (2004, 1), (2004, 20), (2003, 18) };

        private sealed record Replacement(EMEVD.Instruction Old, EMEVD.Instruction New);

        /// <summary>A block the combined engine inserted; CIndex is its first instruction's index in the combined event.</summary>
        private sealed record Insertion(int CIndex, List<EMEVD.Instruction> Block);

        private sealed class Operations
        {
            public List<Replacement> Replacements { get; } = new List<Replacement>();
            public List<EMEVD.Instruction> Removals { get; } = new List<EMEVD.Instruction>();
            public List<Insertion> Insertions { get; } = new List<Insertion>();

            public string Signature() =>
                string.Join("|", Replacements.Select(r => $"R{Key(r.Old)}>{Key(r.New)}"))
                + "#" + string.Join("|", Removals.Select(Key).OrderBy(k => k, StringComparer.Ordinal))
                + "#" + string.Join("|", Insertions.Select(i => $"{i.CIndex}:{string.Join(",", i.Block.Select(Key))}"));
        }

        /// <summary>Messages describing what the last successful overlay satisfied (for the log).</summary>
        public static List<string> Notes { get; } = new List<string>();

        public static EMEVD.Event Apply(EMEVD.Event b, EMEVD.Event c, EMEVD.Event t, out string reason)
        {
            reason = null;
            Notes.Clear();
            if (!BossEvents.Contains(b.ID)) return Refuse(out reason, "not a whitelisted boss event");
            if (b.RestBehavior != c.RestBehavior) return Refuse(out reason, "the combined engine changed the rest behavior");
            if (b.Parameters.Count > 0 || c.Parameters.Count > 0) return Refuse(out reason, "parameterized boss events are not overlaid");

            // An interpretation of the combined edit is valid only if its operations, applied to the base
            // event, reproduce the combined event exactly (skip counts included). Every valid
            // interpretation that can be applied safely to FogMod's event must give the identical result.
            EMEVD.Event result = null;
            List<string> resultNotes = null;
            string firstRefusal = null;
            foreach (List<InstructionMerge.Hunk> hunks in InstructionMerge.Alignments(b.Instructions, c.Instructions))
            {
                Operations operations = Derive(b, c, hunks, out string why);
                if (operations == null) { firstRefusal ??= why; continue; }
                EMEVD.Event replay = Transform(operations, b, Clone(b), c, new List<string>(), out why, out _, out _);
                if (replay == null || !EmevdMerge.SameEvent(replay, c))
                {
                    firstRefusal ??= why ?? "the derived boss operations do not reproduce the combined event";
                    continue;
                }
                var notes = new List<string>();
                EMEVD.Event candidate = Transform(operations, b, Clone(t), c, notes, out why, out List<EMEVD.Instruction> inserted, out List<EMEVD.Instruction> touched);
                if (candidate == null) { firstRefusal ??= why; continue; }
                why = Check(c, t, candidate, inserted, touched);
                if (why != null) { firstRefusal ??= why; continue; }
                if (result == null) { result = candidate; resultNotes = notes; }
                else if (!EmevdMerge.SameEvent(result, candidate))
                    return Refuse(out reason, "valid interpretations of the combined edit give different results");
            }
            if (result == null) return Refuse(out reason, firstRefusal ?? "no valid interpretation of the combined edit");
            Notes.AddRange(resultNotes);
            return result;
        }

        /// <summary>Applies the operations to <paramref name="result"/> (a copy of the target event).</summary>
        private static EMEVD.Event Transform(Operations operations, EMEVD.Event b, EMEVD.Event result, EMEVD.Event c, List<string> notes,
            out string reason, out List<EMEVD.Instruction> inserted, out List<EMEVD.Instruction> touched)
        {
            reason = null;
            inserted = new List<EMEVD.Instruction>();
            touched = new List<EMEVD.Instruction>();

            // 1. Health bar replacements (in place: no position change).
            foreach (Replacement r in operations.Replacements)
            {
                var old = result.Instructions.Where(i => EmevdMerge.SameInstruction(i, r.Old)).ToList();
                if (old.Count == 1)
                {
                    old[0].ArgData = r.New.ArgData.ToArray();
                    touched.Add(old[0]);
                    continue;
                }
                var already = result.Instructions.Where(i => EmevdMerge.SameInstruction(i, r.New)).ToList();
                if (old.Count == 0 && already.Count == 1) { notes.Add($"health bar {Key(r.New)} already present"); touched.Add(already[0]); continue; }
                return Refuse(out reason, old.Count == 0
                    ? $"the health bar {Key(r.Old)} to replace no longer exists in the target event"
                    : $"the health bar {Key(r.Old)} to replace is ambiguous ({old.Count} copies)");
            }

            // 2. Removals, per distinct instruction.
            foreach (var group in operations.Removals.GroupBy(Key))
            {
                EMEVD.Instruction removed = group.First();
                int removedCount = group.Count();
                int inBase = b.Instructions.Count(i => EmevdMerge.SameInstruction(i, removed));
                var inResult = result.Instructions.Select((i, index) => (i, index)).Where(p => EmevdMerge.SameInstruction(p.i, removed)).Select(p => p.index).ToList();
                if (inResult.Count > inBase)
                    return Refuse(out reason, $"the target event has more copies of {Name(removed)} {Key(removed)} than the base; which one to remove is ambiguous");
                if (inResult.Count == 0) { notes.Add($"{Name(removed)} {Key(removed)} already removed by FogMod"); continue; }
                if (removedCount != inBase)
                    return Refuse(out reason, $"the combined engine removes {removedCount} of {inBase} identical {Name(removed)} instructions; which ones is ambiguous");
                if (inResult.Count < inBase) notes.Add($"FogMod already removed {inBase - inResult.Count} of {inBase} {Name(removed)} {Key(removed)}");
                try { for (int k = inResult.Count - 1; k >= 0; k--) SkipRanges.Remove(result, inResult[k]); }
                catch (FogHostException e) { return Refuse(out reason, e.Message); }
            }

            // 3. Insertions after a unique content anchor; instructions already present there are satisfied.
            foreach (Insertion insertion in operations.Insertions)
            {
                // Anchor: the nearest preceding combined-side instruction present exactly once in the target.
                // Instructions skipped on the way must be absent there and must be commands, so the insertion
                // keeps its order relative to every surviving instruction and its control context.
                int anchor = -1;
                for (int j = insertion.CIndex - 1; j >= 0 && anchor < 0; j--)
                {
                    EMEVD.Instruction candidate = c.Instructions[j];
                    var found = result.Instructions.Select((i, index) => (i, index)).Where(p => EmevdMerge.SameInstruction(p.i, candidate)).Select(p => p.index).ToList();
                    if (found.Count > 1) return Refuse(out reason, $"the anchor {Name(candidate)} {Key(candidate)} is ambiguous ({found.Count} copies)");
                    if (found.Count == 1) { anchor = found[0]; break; }
                    if (candidate.Bank < 2000)
                        return Refuse(out reason, $"the anchor would skip over {Name(candidate)}, a control-flow or condition instruction absent from the target event");
                    notes.Add($"anchor moved past {Name(candidate)} {Key(candidate)}, absent from FogMod's event");
                }
                if (anchor < 0) return Refuse(out reason, "no anchor instruction for a combined insertion exists in the target event");
                int at = anchor + 1;
                foreach (EMEVD.Instruction instruction in insertion.Block)
                {
                    if (at < result.Instructions.Count && EmevdMerge.SameInstruction(result.Instructions[at], instruction))
                    {
                        notes.Add($"{Name(instruction)} {Key(instruction)} already provided by FogMod");
                        at++;
                        continue;
                    }
                    EMEVD.Instruction copy = CloneInstruction(instruction);
                    try { SkipRanges.InsertAfter(result, at, copy); }
                    catch (FogHostException e) { return Refuse(out reason, e.Message); }
                    inserted.Add(copy);
                    at++;
                }
            }
            return result;
        }

        /// <summary>
        /// Safety checks on the overlaid FogMod event: nothing added or replaced is dead code, no
        /// boss-support instruction is duplicated, and FogMod's control flow and conditions are unchanged
        /// apart from skip counts. Returns null when all pass.
        /// </summary>
        private static string Check(EMEVD.Event c, EMEVD.Event t, EMEVD.Event result, List<EMEVD.Instruction> inserted, List<EMEVD.Instruction> touched)
        {
            bool[] reachable = Reachable(result);
            foreach (EMEVD.Instruction instruction in inserted.Concat(touched))
            {
                int index = result.Instructions.IndexOf(instruction);
                if (index < 0 || !reachable[index]) return $"{Name(instruction)} {Key(instruction)} would be unreachable under FogMod's control flow";
            }
            foreach (EMEVD.Instruction instruction in inserted)
            {
                int now = result.Instructions.Count(i => EmevdMerge.SameInstruction(i, instruction));
                int allowed = Math.Max(c.Instructions.Count(i => EmevdMerge.SameInstruction(i, instruction)), t.Instructions.Count(i => EmevdMerge.SameInstruction(i, instruction)));
                if (now > allowed) return $"{Name(instruction)} {Key(instruction)} would be duplicated";
            }
            return SameSkeleton(t, result) ? null : "FogMod's control flow or conditions would change";
        }

        private static Operations Derive(EMEVD.Event b, EMEVD.Event c, List<InstructionMerge.Hunk> hunks, out string why)
        {
            why = null;
            var operations = new Operations();
            // Side index of each hunk's first added instruction.
            var addedAt = new Dictionary<InstructionMerge.Hunk, int>();
            var deletedLines = new HashSet<int>(hunks.SelectMany(h => Enumerable.Range(h.Start, h.End - h.Start)));
            int position = 0;
            for (int i = 0; i <= b.Instructions.Count; i++)
            {
                foreach (InstructionMerge.Hunk h in hunks.Where(h => h.Start == i)) { addedAt[h] = position; position += h.Added.Count; }
                if (i == b.Instructions.Count) break;
                if (!deletedLines.Contains(i)) position++;
            }

            foreach (InstructionMerge.Hunk h in hunks)
            {
                var deleted = b.Instructions.Skip(h.Start).Take(h.End - h.Start).ToList();
                var pairedDeleted = new HashSet<EMEVD.Instruction>();
                var replacementNew = new HashSet<int>();
                for (int k = 0; k < h.Added.Count; k++)
                {
                    EMEVD.Instruction added = h.Added[k];
                    if (!IsHealthBar(added)) continue;
                    EMEVD.Instruction old = deleted.FirstOrDefault(d => IsHealthBar(d) && !pairedDeleted.Contains(d) && SameBarSlot(d, added));
                    if (old == null) { why = $"the combined engine inserts a health bar {Key(added)} without replacing one"; return null; }
                    pairedDeleted.Add(old);
                    replacementNew.Add(k);
                    operations.Replacements.Add(new Replacement(old, added));
                }
                foreach (EMEVD.Instruction d in deleted.Where(d => !pairedDeleted.Contains(d)))
                {
                    if (!Removable.Contains((d.Bank, d.ID))) { why = $"the combined engine removes {Name(d)} {Key(d)}, which is not an allowed boss mutation"; return null; }
                    operations.Removals.Add(d);
                }
                for (int k = 0; k < h.Added.Count; k++)
                {
                    if (replacementNew.Contains(k)) continue;
                    int start = k;
                    while (k < h.Added.Count && !replacementNew.Contains(k)) k++;
                    var block = h.Added.Skip(start).Take(k - start).ToList();
                    k--;
                    EMEVD.Instruction outside = block.FirstOrDefault(a => !Insertable.Contains((a.Bank, a.ID)));
                    if (outside != null) { why = $"the combined engine inserts {Name(outside)} {Key(outside)}, which is not an allowed boss mutation"; return null; }
                    int sideIndex = addedAt[h] + start;
                    if (sideIndex == 0) { why = "a combined insertion has no preceding anchor instruction"; return null; }
                    operations.Insertions.Add(new Insertion(sideIndex, block));
                }
            }
            return operations;
        }

        /// <summary>Static reachability: skips jump forward; END Unconditionally (1000[04]) stops a path.</summary>
        public static bool[] Reachable(EMEVD.Event e)
        {
            var reach = new bool[e.Instructions.Count + 1];
            if (e.Instructions.Count == 0) return reach;
            reach[0] = true;
            for (int i = 0; i < e.Instructions.Count; i++)
            {
                if (!reach[i]) continue;
                EMEVD.Instruction ins = e.Instructions[i];
                if (ins.Bank == 1000 && ins.ID == 4) continue;
                if (InstructionMerge.IsSkip(ins))
                {
                    int target = Math.Min(i + 1 + InstructionMerge.Lines(ins), e.Instructions.Count);
                    reach[target] = true;
                    if (!(ins.Bank == 1000 && ins.ID == 3)) reach[i + 1] = true;
                    continue;
                }
                reach[i + 1] = true;
            }
            return reach;
        }

        /// <summary>Control-flow and condition instructions, in order, ignoring skip line counts.</summary>
        private static bool SameSkeleton(EMEVD.Event a, EMEVD.Event b)
        {
            List<EMEVD.Instruction> Skeleton(EMEVD.Event e) => e.Instructions.Where(i => i.Bank < 2000).ToList();
            var x = Skeleton(a);
            var y = Skeleton(b);
            return x.Count == y.Count && x.Zip(y).All(p => p.First.Bank == p.Second.Bank && p.First.ID == p.Second.ID
                && p.First.ArgData.AsSpan(InstructionMerge.IsSkip(p.First) ? 1 : 0).SequenceEqual(p.Second.ArgData.AsSpan(InstructionMerge.IsSkip(p.Second) ? 1 : 0)));
        }

        private static bool IsHealthBar(EMEVD.Instruction i) => i.Bank == 2003 && i.ID == 11;

        /// <summary>Same enable state, entity and slot (bytes 0-9); only the name (bytes 10-11) may differ.</summary>
        private static bool SameBarSlot(EMEVD.Instruction a, EMEVD.Instruction b) =>
            a.ArgData.Length == 12 && b.ArgData.Length == 12 && a.ArgData.AsSpan(0, 10).SequenceEqual(b.ArgData.AsSpan(0, 10));

        private static EMEVD.Event Clone(EMEVD.Event e)
        {
            var copy = new EMEVD.Event(e.ID, e.RestBehavior);
            copy.Instructions.AddRange(e.Instructions.Select(CloneInstruction));
            foreach (EMEVD.Parameter p in e.Parameters)
                copy.Parameters.Add(new EMEVD.Parameter(p.InstructionIndex, p.TargetStartByte, p.SourceStartByte, p.ByteCount) { UnkID = p.UnkID });
            return copy;
        }

        private static EMEVD.Instruction CloneInstruction(EMEVD.Instruction i) =>
            new EMEVD.Instruction(i.Bank, i.ID, i.ArgData.ToArray()) { Layer = i.Layer };

        private static string Key(EMEVD.Instruction i) => $"{i.Bank}[{i.ID:d2}]{Convert.ToHexString(i.ArgData)}";

        private static string Name(EMEVD.Instruction i) => $"{i.Bank}[{i.ID:d2}]";

        private static EMEVD.Event Refuse(out string reason, string why)
        {
            reason = why;
            return null;
        }
    }
}
