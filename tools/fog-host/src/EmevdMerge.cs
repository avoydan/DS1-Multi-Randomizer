using System;
using System.Collections.Generic;
using System.Linq;
using SoulsFormats;

namespace FogHost
{
    /// <summary>
    /// Three-way merge of combined-engine EMEVD edits into FogMod's output
    /// (docs/FOGHOST_INTEGRATION.md, "EMEVD: three-way merge"). B is the pristine base,
    /// C the combined file in staging, T FogMod's output after its own carry-over.
    /// </summary>
    public sealed class EmevdMerge
    {
        private readonly Dictionary<string, (EMEVD Base, EMEVD Combined)> inputs;

        public EmevdMerge(Dictionary<string, (EMEVD Base, EMEVD Combined)> inputs)
        {
            this.inputs = inputs;
        }

        /// <summary>Events that must be present, unchanged, in the written file (invariant 4).</summary>
        public Dictionary<string, List<EMEVD.Event>> ExpectedEvents { get; } = new Dictionary<string, List<EMEVD.Event>>(StringComparer.Ordinal);

        /// <summary>Event-0 initializations that must be present in the written file (invariant 4).</summary>
        public Dictionary<string, List<EMEVD.Instruction>> ExpectedInits { get; } = new Dictionary<string, List<EMEVD.Instruction>>(StringComparer.Ordinal);

        /// <summary>Event files FogMod processed (so every combined event file is accounted for).</summary>
        public HashSet<string> Merged { get; } = new HashSet<string>(StringComparer.Ordinal);

        public void Merge(string fileName, EMEVD output)
        {
            if (!inputs.TryGetValue(fileName, out var input)) return;
            Merged.Add(fileName);
            var expectedEvents = new List<EMEVD.Event>();
            var expectedInits = new List<EMEVD.Instruction>();
            ExpectedEvents[fileName] = expectedEvents;
            ExpectedInits[fileName] = expectedInits;

            Dictionary<long, EMEVD.Event> baseById = ById(input.Base, fileName, "base");
            Dictionary<long, EMEVD.Event> combinedById = ById(input.Combined, fileName, "combined");
            Dictionary<long, EMEVD.Event> outputById = ById(output, fileName, "FogMod output");

            // Rule 2: the combined engine never deletes events.
            foreach (EMEVD.Event baseEvent in input.Base.Events)
            {
                if (!combinedById.ContainsKey(baseEvent.ID)) throw Conflict(fileName, $"combined output deleted event {baseEvent.ID}");
            }

            foreach (EMEVD.Event combined in input.Combined.Events)
            {
                if (combined.ID == 0) continue;
                if (!baseById.TryGetValue(combined.ID, out EMEVD.Event baseEvent))
                {
                    // Rule 1: custom combined event.
                    if (outputById.TryGetValue(combined.ID, out EMEVD.Event existing))
                    {
                        if (!SameEvent(existing, combined)) throw Conflict(fileName, $"custom event {combined.ID} collides with a different FogMod event");
                    }
                    else
                    {
                        output.Events.Add(combined);
                        outputById[combined.ID] = combined;
                    }
                    expectedEvents.Add(combined);
                }
                else if (!SameEvent(baseEvent, combined))
                {
                    // Rule 3: vanilla event edited by the combined engine.
                    if (!outputById.TryGetValue(combined.ID, out EMEVD.Event fog))
                        throw Conflict(fileName, $"event {combined.ID} was edited by the combined engine but is absent from FogMod's output");
                    if (SameEvent(fog, combined))
                    {
                        // Already identical.
                    }
                    else if (SameEvent(fog, baseEvent))
                    {
                        output.Events[output.Events.IndexOf(fog)] = combined;
                        outputById[combined.ID] = combined;
                    }
                    else
                    {
                        throw Conflict(fileName, $"event {combined.ID} was edited by both the combined engine and FogMod");
                    }
                    expectedEvents.Add(combined);
                }
            }

            // Rule 4: event 0 may only gain 2000[00] initializations.
            baseById.TryGetValue(0, out EMEVD.Event base0);
            combinedById.TryGetValue(0, out EMEVD.Event combined0);
            if (combined0 == null) return;
            if (base0 == null) throw Conflict(fileName, "combined output added event 0");
            var remaining = base0.Instructions.ToList();
            var added = new List<EMEVD.Instruction>();
            foreach (EMEVD.Instruction instruction in combined0.Instructions)
            {
                int match = remaining.FindIndex(b => SameInstruction(b, instruction));
                if (match >= 0) remaining.RemoveAt(match);
                else added.Add(instruction);
            }
            if (remaining.Count > 0) throw Conflict(fileName, $"combined output removed or changed {remaining.Count} event-0 instruction(s)");
            if (!SameParameters(base0.Parameters, combined0.Parameters) || base0.RestBehavior != combined0.RestBehavior)
                throw Conflict(fileName, "combined output changed event 0 parameters");
            EMEVD.Event output0 = outputById.TryGetValue(0, out EMEVD.Event o) ? o : throw Conflict(fileName, "FogMod output has no event 0");
            foreach (EMEVD.Instruction instruction in added)
            {
                if (instruction.Bank != 2000 || instruction.ID != 0)
                    throw Conflict(fileName, $"combined output added a non-initialization instruction {instruction.Bank}[{instruction.ID:d2}] to event 0");
                if (!output0.Instructions.Any(t => SameInstruction(t, instruction))) output0.Instructions.Add(instruction);
                expectedInits.Add(instruction);
            }
        }

        public static bool SameEvent(EMEVD.Event a, EMEVD.Event b) =>
            a.ID == b.ID && a.RestBehavior == b.RestBehavior
            && a.Instructions.Count == b.Instructions.Count
            && a.Instructions.Zip(b.Instructions).All(p => SameInstruction(p.First, p.Second))
            && SameParameters(a.Parameters, b.Parameters);

        public static bool SameInstruction(EMEVD.Instruction a, EMEVD.Instruction b) =>
            a.Bank == b.Bank && a.ID == b.ID && Equals(a.Layer, b.Layer)
            && (a.ArgData ?? Array.Empty<byte>()).AsSpan().SequenceEqual(b.ArgData ?? Array.Empty<byte>());

        private static bool SameParameters(List<EMEVD.Parameter> a, List<EMEVD.Parameter> b) =>
            a.Count == b.Count && a.Zip(b).All(p =>
                p.First.InstructionIndex == p.Second.InstructionIndex
                && p.First.TargetStartByte == p.Second.TargetStartByte
                && p.First.SourceStartByte == p.Second.SourceStartByte
                && p.First.ByteCount == p.Second.ByteCount
                && p.First.UnkID == p.Second.UnkID);

        private static Dictionary<long, EMEVD.Event> ById(EMEVD evd, string fileName, string which)
        {
            var result = new Dictionary<long, EMEVD.Event>();
            foreach (EMEVD.Event evt in evd.Events)
            {
                if (!result.TryAdd(evt.ID, evt)) throw Conflict(fileName, $"{which} has duplicate event {evt.ID}");
            }
            return result;
        }

        private static FogHostException Conflict(string fileName, string detail) =>
            new FogHostException(HostErrors.MergeConflict, $"EMEVD merge conflict in event/{fileName}: {detail}.") { Stage = "write-game-data" };
    }
}
