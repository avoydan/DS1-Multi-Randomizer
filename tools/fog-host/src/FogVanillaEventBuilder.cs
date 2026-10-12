using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulsFormats;

namespace FogHost
{
    /// <summary>
    /// Rebuilds the Bed of Chaos fall-gate helper events from the user's pristine
    /// event archive. These helpers intentionally contain only object restoration
    /// and invulnerability setup, selected by semantic instruction shape.
    /// </summary>
    public static class FogVanillaEventBuilder
    {
        private const long OriginalFloorEvent = 11410200;
        private const long OriginalFallEvent = 11410201;
        private const long FloorHelperEvent = 11410220;
        private const long FallHelperEvent = 11410221;

        public static void ReplaceBedOfChaosHelpers(EMEVD pristine, EMEVD output)
        {
            if (pristine == null) throw new ArgumentNullException(nameof(pristine));
            if (output == null) throw new ArgumentNullException(nameof(output));

            ReplaceOrAppend(output, BuildFloorHelper(FindUnique(pristine, OriginalFloorEvent)));
            ReplaceOrAppend(output, BuildFallHelper(FindUnique(pristine, OriginalFallEvent)));
        }

        private static EMEVD.Event BuildFloorHelper(EMEVD.Event source)
        {
            // Bed of Chaos floor pieces must be restored, then made invulnerable,
            // for each of the five authored floor-piece entities.
            for (int start = 0; start <= source.Instructions.Count - 10; start++)
            {
                var restores = source.Instructions.Skip(start).Take(5).ToArray();
                var invulnerability = source.Instructions.Skip(start + 5).Take(5).ToArray();
                if (!restores.All(IsObjectRestore) || !invulnerability.All(IsInvulnerabilityOn)) continue;

                int[] restoreEntities = restores.Select(ObjectEntity).ToArray();
                int[] invulnerableEntities = invulnerability.Select(ObjectEntity).ToArray();
                if (restoreEntities.Distinct().Count() != 5 || !restoreEntities.SequenceEqual(invulnerableEntities)) continue;
                if (source.Parameters.Any(p => p.InstructionIndex >= start && p.InstructionIndex < start + 10))
                    throw Invalid($"Pristine Bed of Chaos event {OriginalFloorEvent} has unexpected parameters on the floor-piece setup.");

                return CreateHelper(FloorHelperEvent, source, Enumerable.Range(start, 10));
            }

            throw Invalid($"Pristine Bed of Chaos event {OriginalFloorEvent} has no unique five-piece restore/invulnerability setup.");
        }

        private static EMEVD.Event BuildFallHelper(EMEVD.Event source)
        {
            for (int i = 0; i < source.Instructions.Count - 1; i++)
            {
                EMEVD.Instruction restore = source.Instructions[i];
                EMEVD.Instruction invulnerability = source.Instructions[i + 1];
                if (!IsObjectRestore(restore) || !IsInvulnerabilityOn(invulnerability)) continue;
                if (ObjectEntity(restore) != ObjectEntity(invulnerability)) continue;

                return CreateHelper(FallHelperEvent, source, new[] { i, i + 1 });
            }

            throw Invalid($"Pristine Bed of Chaos event {OriginalFallEvent} has no restore/invulnerability setup for its fall object.");
        }

        private static EMEVD.Event CreateHelper(long id, EMEVD.Event source, IEnumerable<int> instructionIndices)
        {
            int[] indices = instructionIndices.ToArray();
            var reindex = indices.Select((sourceIndex, targetIndex) => (sourceIndex, targetIndex))
                .ToDictionary(pair => pair.sourceIndex, pair => pair.targetIndex);
            var helper = new EMEVD.Event(id, source.RestBehavior);
            foreach (int sourceIndex in indices) helper.Instructions.Add(Clone(source.Instructions[sourceIndex]));
            foreach (EMEVD.Parameter parameter in source.Parameters.Where(p => reindex.ContainsKey((int)p.InstructionIndex)))
            {
                helper.Parameters.Add(new EMEVD.Parameter(
                    reindex[(int)parameter.InstructionIndex],
                    parameter.TargetStartByte,
                    parameter.SourceStartByte,
                    parameter.ByteCount)
                { UnkID = parameter.UnkID });
            }
            return helper;
        }

        private static void ReplaceOrAppend(EMEVD output, EMEVD.Event replacement)
        {
            int[] matches = output.Events.Select((evt, index) => (evt, index))
                .Where(pair => pair.evt.ID == replacement.ID).Select(pair => pair.index).ToArray();
            if (matches.Length > 1) throw Invalid($"Fog output has duplicate helper event {replacement.ID}.");
            if (matches.Length == 0) output.Events.Add(replacement);
            else output.Events[matches[0]] = replacement;
        }

        private static EMEVD.Event FindUnique(EMEVD evd, long id)
        {
            EMEVD.Event[] matches = evd.Events.Where(evt => evt.ID == id).ToArray();
            if (matches.Length != 1) throw Invalid($"Pristine event archive must contain exactly one event {id}; found {matches.Length}.");
            return matches[0];
        }

        private static bool IsObjectRestore(EMEVD.Instruction instruction) =>
            instruction.Bank == 2005 && instruction.ID == 2 && instruction.ArgData?.Length == 4;

        private static bool IsInvulnerabilityOn(EMEVD.Instruction instruction) =>
            instruction.Bank == 2005 && instruction.ID == 13 && instruction.ArgData?.Length == 8
            && BitConverter.ToInt32(instruction.ArgData, 4) == 1;

        private static int ObjectEntity(EMEVD.Instruction instruction) => BitConverter.ToInt32(instruction.ArgData, 0);

        private static EMEVD.Instruction Clone(EMEVD.Instruction instruction) =>
            new EMEVD.Instruction(instruction.Bank, instruction.ID, instruction.ArgData.ToArray()) { Layer = instruction.Layer };

        private static InvalidDataException Invalid(string message) => new InvalidDataException(message);
    }
}
