// Repository-authored file (ds1-triple-randomizer), not part of upstream kellernz1/ds-randomizer.
// Licensed GPL-3.0-only like the vendored tool it extends. See engines/combined/vendor/UPSTREAM_PIN.md.
//
// DS1 EMEVD "skip N lines" instructions cover the N instructions that follow them. Removing or
// inserting an instruction inside such a range without changing N makes the skip cover different
// instructions. These helpers keep every enclosing skip count consistent with the edit, and throw
// when the edit cannot be adjusted unambiguously.

using SoulsFormats;

/// <summary>Where an inserted instruction belongs relative to an existing anchor instruction.</summary>
public enum InsertAnchor
{
    /// <summary>The new instruction is inserted right before the instruction at the index and shares its skip coverage.</summary>
    Before,

    /// <summary>The new instruction is inserted right after the instruction at index - 1 and shares its skip coverage.</summary>
    After,
}

public static class EventSkipRanges
{
    /// <summary>DS1 instructions whose first argument byte is "Number Of Skipped Lines" (DS1 EMEDF).</summary>
    private static readonly HashSet<(int Bank, int Id)> SkipInstructions = new()
    {
        (1000, 1), (1000, 3), (1000, 5), (1000, 7),
        (1003, 1), (1003, 3), (1003, 5), (1003, 7),
        (1005, 1),
    };

    public static bool IsSkip(EMEVD.Instruction instruction) =>
        SkipInstructions.Contains((instruction.Bank, instruction.ID));

    public static int Lines(EMEVD.Instruction skip) => skip.ArgData.Length > 0 ? skip.ArgData[0] : 0;

    /// <summary>Indices of the skips whose covered lines include the instruction at <paramref name="index"/>.</summary>
    public static List<int> Covering(EMEVD.Event entry, int index)
    {
        var result = new List<int>();
        for (var position = 0; position < index; position++)
        {
            var instruction = entry.Instructions[position];
            if (IsSkip(instruction) && position + Lines(instruction) >= index)
                result.Add(position);
        }
        return result;
    }

    /// <summary>Call before removing the instruction at <paramref name="index"/>: shrinks every enclosing skip.</summary>
    public static void BeforeRemove(EMEVD.Event entry, int index)
    {
        var removed = entry.Instructions[index];
        if (IsSkip(removed) && Lines(removed) > 0)
            throw new InvalidDataException(
                $"Cannot remove skip instruction {index} of event {entry.ID}: the lines it covers would change meaning.");
        foreach (var position in Covering(entry, index))
            SetLines(entry, position, Lines(entry.Instructions[position]) - 1);
    }

    /// <summary>
    /// Call before inserting a new instruction at <paramref name="index"/>: grows every skip that
    /// covers the anchor, so the new instruction shares the anchor's coverage.
    /// </summary>
    public static void BeforeInsert(EMEVD.Event entry, int index, InsertAnchor anchor)
    {
        var anchorIndex = anchor == InsertAnchor.Before ? index : index - 1;
        if (anchorIndex < 0 || anchorIndex >= entry.Instructions.Count)
            throw new InvalidDataException(
                $"Cannot insert at {index} of event {entry.ID}: no anchor instruction {anchor.ToString().ToLowerInvariant()} it.");
        if (anchor == InsertAnchor.After && IsSkip(entry.Instructions[anchorIndex]))
            throw new InvalidDataException(
                $"Cannot insert right after skip instruction {anchorIndex} of event {entry.ID}: it is ambiguous whether the new instruction is skipped.");
        foreach (var position in Covering(entry, anchorIndex))
            SetLines(entry, position, Lines(entry.Instructions[position]) + 1);
    }

    private static void SetLines(EMEVD.Event entry, int position, int lines)
    {
        var skip = entry.Instructions[position];
        if (lines < 0 || lines > byte.MaxValue)
            throw new InvalidDataException($"Skip instruction {position} of event {entry.ID} would cover {lines} lines.");
        if (entry.Parameters.Any(parameter =>
                parameter.InstructionIndex == position &&
                parameter.TargetStartByte <= 0 &&
                parameter.TargetStartByte + parameter.ByteCount > 0))
            throw new InvalidDataException(
                $"Skip instruction {position} of event {entry.ID} takes its line count from an event parameter; it cannot be adjusted.");
        skip.ArgData[0] = (byte)lines;
    }
}
