using System.Linq;
using SoulsFormats;

namespace FogHost
{
    /// <summary>
    /// Keeps DS1 "skip N lines" counts consistent when FogMod replaces an instruction in place with
    /// several instructions (its boss-trigger rewrite). Same principle as the combined engine's
    /// EventSkipRanges: every skip that covered the replaced instruction must cover all of its
    /// replacements, so the same logical instructions stay covered.
    /// </summary>
    public static class SkipRanges
    {
        /// <summary>
        /// Call before FogMod replaces the instruction at <paramref name="index"/> with
        /// <paramref name="inserted"/> instructions at that same position.
        /// </summary>
        public static void BeforeReplace(EMEVD.Event evt, int index, int inserted)
        {
            EMEVD.Instruction replaced = evt.Instructions[index];
            if (InstructionMerge.IsControlFlow(replaced))
                throw Refuse(evt, $"the replaced instruction {index} is control flow");
            int delta = inserted - 1;
            if (delta == 0) return;
            for (int position = 0; position < index; position++)
            {
                EMEVD.Instruction skip = evt.Instructions[position];
                if (!InstructionMerge.IsSkip(skip) || position + InstructionMerge.Lines(skip) < index) continue;
                if (evt.Parameters.Any(p => p.InstructionIndex == position && p.TargetStartByte <= 0 && p.TargetStartByte + p.ByteCount > 0))
                    throw Refuse(evt, $"skip {position} takes its line count from an event parameter");
                int lines = InstructionMerge.Lines(skip) + delta;
                if (lines < 0 || lines > byte.MaxValue) throw Refuse(evt, $"skip {position} would cover {lines} lines");
                skip.ArgData[0] = (byte)lines;
            }
        }

        /// <summary>
        /// Removes the instruction at <paramref name="index"/> the way FogMod's Asylum Demon edit does,
        /// shrinking every enclosing skip and re-indexing event parameters so the same logical
        /// instructions stay covered and parameterized.
        /// </summary>
        public static void Remove(EMEVD.Event evt, int index)
        {
            EMEVD.Instruction removed = evt.Instructions[index];
            if (InstructionMerge.IsControlFlow(removed))
                throw Refuse(evt, $"the removed instruction {index} is control flow");
            if (evt.Parameters.Any(p => p.InstructionIndex == index))
                throw Refuse(evt, $"the removed instruction {index} takes an event parameter");
            for (int position = 0; position < index; position++)
            {
                EMEVD.Instruction skip = evt.Instructions[position];
                if (!InstructionMerge.IsSkip(skip) || position + InstructionMerge.Lines(skip) < index) continue;
                if (evt.Parameters.Any(p => p.InstructionIndex == position && p.TargetStartByte <= 0 && p.TargetStartByte + p.ByteCount > 0))
                    throw Refuse(evt, $"skip {position} takes its line count from an event parameter");
                skip.ArgData[0] = (byte)(InstructionMerge.Lines(skip) - 1);
            }
            foreach (EMEVD.Parameter parameter in evt.Parameters.Where(p => p.InstructionIndex > index))
                parameter.InstructionIndex--;
            evt.Instructions.RemoveAt(index);
        }

        /// <summary>
        /// Inserts <paramref name="instruction"/> at <paramref name="index"/>, right after the anchor at
        /// index - 1: every skip covering the anchor grows by one so the new instruction shares its
        /// coverage, and event parameters at or after the index are re-indexed. Inserting right after a
        /// skip instruction is ambiguous and refused.
        /// </summary>
        public static void InsertAfter(EMEVD.Event evt, int index, EMEVD.Instruction instruction)
        {
            int anchor = index - 1;
            if (anchor < 0 || anchor >= evt.Instructions.Count) throw Refuse(evt, $"no anchor instruction before {index}");
            if (InstructionMerge.IsSkip(evt.Instructions[anchor]))
                throw Refuse(evt, $"inserting right after skip instruction {anchor} is ambiguous");
            for (int position = 0; position < anchor; position++)
            {
                EMEVD.Instruction skip = evt.Instructions[position];
                if (!InstructionMerge.IsSkip(skip) || position + InstructionMerge.Lines(skip) < anchor) continue;
                if (evt.Parameters.Any(p => p.InstructionIndex == position && p.TargetStartByte <= 0 && p.TargetStartByte + p.ByteCount > 0))
                    throw Refuse(evt, $"skip {position} takes its line count from an event parameter");
                int lines = InstructionMerge.Lines(skip) + 1;
                if (lines > byte.MaxValue) throw Refuse(evt, $"skip {position} would cover {lines} lines");
                skip.ArgData[0] = (byte)lines;
            }
            foreach (EMEVD.Parameter parameter in evt.Parameters.Where(p => p.InstructionIndex >= index))
                parameter.InstructionIndex++;
            evt.Instructions.Insert(index, instruction);
        }

        private static FogHostException Refuse(EMEVD.Event evt, string detail) =>
            new FogHostException(HostErrors.EngineFailed,
                $"Cannot keep skip ranges consistent in event {evt.ID} for a FogMod instruction edit: {detail}.")
            { Stage = "write-game-data" };
    }
}
