using System.Text;
using SoulsFormats;

// Tests for EventSkipRanges (the DsrDataTool skip-count fix) and a verifier for real combined output.
// Unit fixtures use real DS1 opcodes with placeholder entity IDs (no game data).

System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if ((args.Length == 3 || args.Length == 4 && args[3] == "--list-edited") && args[0] == "verify-skips")
    return SkipVerifier.Run(args[1], args[2], args.Length == 4);
if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: DsrDataTool.Tests [verify-skips <baseEventDir> <combinedEventDir> [--list-edited]]");
    return 2;
}
return UnitTests.Run();

internal static class Fixtures
{
    public static EMEVD.Instruction Ins(int bank, int id, params int[] args) =>
        new(bank, id, args.SelectMany(BitConverter.GetBytes).ToArray());

    public static EMEVD.Event Event(long id, params EMEVD.Instruction[] instructions)
    {
        var entry = new EMEVD.Event(id, EMEVD.Event.RestBehaviorType.Default);
        entry.Instructions.AddRange(instructions);
        return entry;
    }

    public static EMEVD.Instruction Cmd(int n) => Ins(2004, 1, 1000 + n, 0);

    /// <summary>Removal exactly as DsrDataTool.RemoveEventInstruction performs it.</summary>
    public static void Remove(EMEVD.Event entry, int index)
    {
        EventSkipRanges.BeforeRemove(entry, index);
        entry.Instructions.RemoveAt(index);
    }

    /// <summary>Insertion exactly as DsrDataTool.InsertEventInstruction performs it.</summary>
    public static void Insert(EMEVD.Event entry, int index, EMEVD.Instruction instruction, InsertAnchor anchor)
    {
        EventSkipRanges.BeforeInsert(entry, index, anchor);
        entry.Instructions.Insert(index, instruction);
    }

    /// <summary>The instructions (by reference) a skip at <paramref name="position"/> covers.</summary>
    public static List<EMEVD.Instruction> Covered(EMEVD.Event entry, int position) =>
        entry.Instructions.Skip(position + 1).Take(EventSkipRanges.Lines(entry.Instructions[position])).ToList();
}

internal static class UnitTests
{
    private static int passed, failed;

    public static int Run()
    {
        Test("deletion inside a SKIP range shrinks it", DeletionInside);
        Test("insertion inside a SKIP range grows it", InsertionInside);
        Test("insertion after the last covered line joins the range when anchored to it", InsertionAfterLastCovered);
        Test("edit immediately after the SKIP range leaves it unchanged", EditImmediatelyAfter);
        Test("edit before the SKIP leaves its count unchanged", EditBefore);
        Test("same-length replacement leaves the SKIP unchanged", SameLengthReplacement);
        Test("multiple SKIPs covering the edited position are all adjusted", MultipleSkips);
        Test("real 11505392 case: removing the AI command keeps SKIP 7 on its block", Real11505392);
        Test("real 11600400 case: removing the animation keeps SKIP 2 on END only", Real11600400);
        Test("inserting right after a SKIP instruction is refused", InsertAfterSkipRefused);
        Test("removing a SKIP instruction is refused", RemoveSkipRefused);
        Test("parameterized SKIP count is refused", ParameterizedSkipRefused);
        Test("SKIP count overflow is refused", OverflowRefused);
        Test("every DS1 skip opcode is recognized; END/WAIT are not", SkipOpcodes);
        Console.WriteLine($"\n{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Throws(Action action, string contains)
    {
        try { action(); }
        catch (InvalidDataException e) when (e.Message.Contains(contains, StringComparison.Ordinal)) { return; }
        throw new Exception($"expected InvalidDataException containing \"{contains}\"");
    }

    private static EMEVD.Event SkipEvent()
    {
        // 0 cmd, 1 SKIP 3 (covers 2..4), 2..4 cmds, 5..7 cmds
        var entry = Fixtures.Event(1, Fixtures.Cmd(0), Fixtures.Ins(1000, 3, 3));
        for (var i = 2; i < 8; i++) entry.Instructions.Add(Fixtures.Cmd(i));
        return entry;
    }

    private static void DeletionInside()
    {
        var entry = SkipEvent();
        var expected = Fixtures.Covered(entry, 1).Where((_, i) => i != 1).ToList();
        Fixtures.Remove(entry, 3);
        Check(EventSkipRanges.Lines(entry.Instructions[1]) == 2, "count not 2");
        Check(Fixtures.Covered(entry, 1).SequenceEqual(expected), "covered lines changed");
    }

    private static void InsertionInside()
    {
        var entry = SkipEvent();
        var added = Fixtures.Ins(2004, 9, 1);
        Fixtures.Insert(entry, 3, added, InsertAnchor.After); // after line 2 (covered)
        Check(EventSkipRanges.Lines(entry.Instructions[1]) == 4, "count not 4");
        Check(Fixtures.Covered(entry, 1).Contains(added) && Fixtures.Covered(entry, 1).Last() == entry.Instructions[5], "coverage wrong");
    }

    private static void InsertionAfterLastCovered()
    {
        // The DsrDataTool health-bar case: companions inserted after a covered anchor stay covered.
        var entry = SkipEvent();
        var added = Fixtures.Ins(2004, 20, 1);
        Fixtures.Insert(entry, 5, added, InsertAnchor.After); // anchor 4 = last covered line
        Check(EventSkipRanges.Lines(entry.Instructions[1]) == 4 && Fixtures.Covered(entry, 1).Last() == added, "companion not covered");
    }

    private static void EditImmediatelyAfter()
    {
        var entry = SkipEvent();
        var covered = Fixtures.Covered(entry, 1);
        Fixtures.Insert(entry, 5, Fixtures.Ins(2004, 9, 1), InsertAnchor.Before); // before line 5, first uncovered
        Check(EventSkipRanges.Lines(entry.Instructions[1]) == 3 && Fixtures.Covered(entry, 1).SequenceEqual(covered), "insert changed range");
        Fixtures.Remove(entry, 5); // remove the line right after the range
        Check(EventSkipRanges.Lines(entry.Instructions[1]) == 3 && Fixtures.Covered(entry, 1).SequenceEqual(covered), "remove changed range");
    }

    private static void EditBefore()
    {
        var entry = SkipEvent();
        var covered = Fixtures.Covered(entry, 1);
        Fixtures.Remove(entry, 0);
        Check(EventSkipRanges.Lines(entry.Instructions[0]) == 3 && Fixtures.Covered(entry, 0).SequenceEqual(covered), "range changed");
    }

    private static void SameLengthReplacement()
    {
        var entry = SkipEvent();
        var skipBytes = entry.Instructions[1].ArgData.ToArray();
        entry.Instructions[3].ArgData[4] = 1; // in-place argument change (DsrDataTool's name-ID patch)
        var replacement = Fixtures.Ins(2004, 9, 7);
        Fixtures.Remove(entry, 3);
        Fixtures.Insert(entry, 3, replacement, InsertAnchor.Before);
        Check(entry.Instructions[1].ArgData.SequenceEqual(skipBytes), "skip bytes changed");
        Check(Fixtures.Covered(entry, 1)[1] == replacement, "replacement not covered");
    }

    private static void MultipleSkips()
    {
        // 0 SKIP 5 IF flag (1..5), 1 cmd, 2 SKIP 2 IF cond (3..4), 3 cmd, 4 cmd, 5 cmd, 6 cmd
        var entry = Fixtures.Event(1, Fixtures.Ins(1003, 1, 5, 11), Fixtures.Cmd(1), Fixtures.Ins(1000, 1, 0x00000102),
            Fixtures.Cmd(3), Fixtures.Cmd(4), Fixtures.Cmd(5), Fixtures.Cmd(6));
        Fixtures.Remove(entry, 3);
        Check(EventSkipRanges.Lines(entry.Instructions[0]) == 4, "outer not 4");
        Check(EventSkipRanges.Lines(entry.Instructions[2]) == 1, "inner not 1");
        Fixtures.Insert(entry, 4, Fixtures.Ins(2004, 9, 1), InsertAnchor.After); // after inner-covered line 3
        Check(EventSkipRanges.Lines(entry.Instructions[0]) == 5 && EventSkipRanges.Lines(entry.Instructions[2]) == 2, "insert not applied to both");
    }

    private static void Real11505392()
    {
        const int e1 = 111, e2 = 222;
        var entry = Fixtures.Event(11505392,
            Fixtures.Ins(1003, 1, 7, 11),          // 0 SKIP 7 IF Event Flag 11 OFF
            Fixtures.Ins(2004, 5, e1, 0),          // 1 Change Character Enable State
            Fixtures.Ins(2004, 37, e1),            // 2 Force Character Treasure
            Fixtures.Ins(2004, 29, e1, 1),         // 3 Set Character Backread State
            Fixtures.Ins(4, 7, 0, e2, 1),          // 4 IF Character Backread Status
            Fixtures.Ins(2004, 17, e2, 10, 0),     // 5 Request Character AI Command (model-specific)
            Fixtures.Ins(2004, 20, e2),            // 6 Request Character AI Re-plan
            Fixtures.Ins(1000, 4, 0),              // 7 END Unconditionally
            Fixtures.Ins(2004, 1, e1, 0),          // 8 Set Character AI State: must still run when flag 11 is OFF
            Fixtures.Ins(2004, 9, e1, 9000));      // 9
        var runsWhenSkipped = entry.Instructions[8];
        var block = Fixtures.Covered(entry, 0).Where(i => i.ID != 17).ToList();
        Fixtures.Remove(entry, 5);
        Check(EventSkipRanges.Lines(entry.Instructions[0]) == 6, "SKIP not 6");
        Check(Fixtures.Covered(entry, 0).SequenceEqual(block), "block changed");
        Check(!Fixtures.Covered(entry, 0).Contains(runsWhenSkipped), "Set Character AI State is now skipped");
    }

    private static void Real11600400()
    {
        const int e = 1600500;
        var entry = Fixtures.Event(11600400,
            Fixtures.Ins(2004, 31, e, 1),          // 0
            Fixtures.Ins(2004, 10, e, 0),          // 1
            Fixtures.Ins(1003, 1, 0x00010002, 0),  // 2 SKIP 2 IF event flag (covers 3..4)
            Fixtures.Ins(2004, 9, e, -1, -1, 29060, -1, -1), // 3 Set Special Animation (model-specific)
            Fixtures.Ins(1000, 4, 0),              // 4 END
            Fixtures.Ins(3, 3, 0x1FF, e, 10000, 0),// 5 IF condition (must not be skipped)
            Fixtures.Ins(4, 1, 0xFF, e, 10000),    // 6
            Fixtures.Ins(0, 0, 0x00FF0100),        // 7
            Fixtures.Ins(2004, 9, e, -1),          // 8 (model-specific)
            Fixtures.Ins(2003, 18, e, 29060, 0));  // 9 (model-specific)
        var end = entry.Instructions[4];
        var condition = entry.Instructions[5];
        foreach (var index in new[] { 9, 8, 3 }) Fixtures.Remove(entry, index); // backwards, as DsrDataTool removes
        Check(EventSkipRanges.Lines(entry.Instructions[2]) == 1, "SKIP not 1");
        Check(Fixtures.Covered(entry, 2).SequenceEqual(new[] { end }), "SKIP no longer covers exactly END");
        Check(!Fixtures.Covered(entry, 2).Contains(condition), "condition is now skipped");
    }

    private static void InsertAfterSkipRefused()
    {
        var entry = SkipEvent();
        Throws(() => Fixtures.Insert(entry, 2, Fixtures.Cmd(9), InsertAnchor.After), "ambiguous");
    }

    private static void RemoveSkipRefused()
    {
        var entry = SkipEvent();
        Throws(() => Fixtures.Remove(entry, 1), "Cannot remove skip instruction");
    }

    private static void ParameterizedSkipRefused()
    {
        var entry = SkipEvent();
        entry.Parameters.Add(new EMEVD.Parameter(1, 0, 0, 1));
        Throws(() => Fixtures.Remove(entry, 3), "event parameter");
    }

    private static void OverflowRefused()
    {
        var entry = Fixtures.Event(1, Fixtures.Ins(1000, 3, 255));
        for (var i = 0; i < 256; i++) entry.Instructions.Add(Fixtures.Cmd(i));
        Throws(() => Fixtures.Insert(entry, 2, Fixtures.Cmd(999), InsertAnchor.After), "256 lines");
    }

    private static void SkipOpcodes()
    {
        foreach (var (bank, id) in new[] { (1000, 1), (1000, 3), (1000, 5), (1000, 7), (1003, 1), (1003, 3), (1003, 5), (1003, 7), (1005, 1) })
            Check(EventSkipRanges.IsSkip(Fixtures.Ins(bank, id, 1)), $"{bank}[{id}] not a skip");
        foreach (var (bank, id) in new[] { (1000, 0), (1000, 2), (1000, 4), (1003, 0), (1003, 2), (1001, 0), (2004, 1) })
            Check(!EventSkipRanges.IsSkip(Fixtures.Ins(bank, id, 1)), $"{bank}[{id}] wrongly a skip");
    }
}

/// <summary>
/// Checks real combined output against the pristine base: for every base skip that survives, the
/// skip must cover exactly its original instructions minus the ones the combined engine deleted
/// (plus instructions the combined engine inserted into the block).
/// </summary>
internal static class SkipVerifier
{
    public static int Run(string baseDirectory, string combinedDirectory, bool listEdited = false)
    {
        int events = 0, skips = 0, adjusted = 0;
        var problems = new List<string>();
        foreach (var basePath in Directory.GetFiles(baseDirectory, "*.emevd.dcx").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(basePath);
            var combinedPath = Path.Combine(combinedDirectory, name);
            if (!File.Exists(combinedPath)) continue;
            var baseEvents = EMEVD.Read(basePath).Events.ToDictionary(e => e.ID);
            foreach (var combined in EMEVD.Read(combinedPath).Events)
            {
                if (!baseEvents.TryGetValue(combined.ID, out var original)) continue;
                var map = Align(original.Instructions, combined.Instructions);
                if (map == null) continue; // identical event
                events++;
                if (listEdited) Console.WriteLine($"EDITED {name}:{combined.ID}");
                for (var p = 0; p < original.Instructions.Count; p++)
                {
                    if (!EventSkipRanges.IsSkip(original.Instructions[p])) continue;
                    var n = EventSkipRanges.Lines(original.Instructions[p]);
                    if (map[p] < 0) { problems.Add($"{name}:{combined.ID} skip {p} was removed"); continue; }
                    skips++;
                    var q = map[p];
                    var m = EventSkipRanges.Lines(combined.Instructions[q]);
                    if (m != n) adjusted++;
                    var expected = Enumerable.Range(p + 1, n).Where(b => b < map.Length && map[b] >= 0).Select(b => map[b]).ToHashSet();
                    var inverse = map.Select((c, b) => (c, b)).Where(t => t.c >= 0).ToDictionary(t => t.c, t => t.b);
                    var actual = Enumerable.Range(q + 1, m).Where(c => inverse.ContainsKey(c)).ToHashSet();
                    if (!expected.SetEquals(actual))
                        problems.Add($"{name}:{combined.ID} {original.Instructions[p].Bank}[{original.Instructions[p].ID:d2}] at {p}: covers {Describe(actual, inverse)} instead of base {string.Join(",", expected.Select(c => inverse[c]))}");
                }
            }
        }
        foreach (var problem in problems) Console.WriteLine($"MISMATCH {problem}");
        Console.WriteLine($"verify-skips: {events} edited events, {skips} surviving skips, {adjusted} adjusted, {problems.Count} mismatches");
        return problems.Count == 0 ? 0 : 1;
    }

    private static string Describe(HashSet<int> combined, Dictionary<int, int> inverse) =>
        string.Join(",", combined.Select(c => inverse[c]));

    private static bool Same(EMEVD.Instruction a, EMEVD.Instruction b)
    {
        if (a.Bank != b.Bank || a.ID != b.ID || !Equals(a.Layer, b.Layer) || a.ArgData.Length != b.ArgData.Length) return false;
        // A skip's line count may legitimately change; compare everything else.
        var from = EventSkipRanges.IsSkip(a) ? 1 : 0;
        return a.ArgData.AsSpan(from).SequenceEqual(b.ArgData.AsSpan(from));
    }

    /// <summary>LCS map base index -> combined index (-1 when deleted); null when identical.</summary>
    private static int[] Align(List<EMEVD.Instruction> a, List<EMEVD.Instruction> b)
    {
        if (a.Count == b.Count && a.Zip(b).All(p => Same(p.First, p.Second) && p.First.ArgData.SequenceEqual(p.Second.ArgData))) return null;
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                lcs[i, j] = Same(a[i], b[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var map = Enumerable.Repeat(-1, a.Count).ToArray();
        int x = 0, y = 0;
        while (x < a.Count && y < b.Count)
        {
            if (Same(a[x], b[y]) && lcs[x, y] == lcs[x + 1, y + 1] + 1) { map[x++] = y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) x++;
            else y++;
        }
        return map;
    }
}
