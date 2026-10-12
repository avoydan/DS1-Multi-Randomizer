using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FogHost;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>
    /// Bounded static contract verifier for the project-authored 5700 dispatcher.
    /// It extracts reviewed per-mode guards, parameter sources, observable effects,
    /// branch destinations and effect ordering. This is not an EMEVD VM and does not
    /// prove runtime gameplay behavior.
    /// </summary>
    internal static class Event5700BehaviorHarness
    {
        private const int PayloadMode = 0;
        private const int PayloadArea = 1;
        private const int PayloadBlock = 2;
        private const int PayloadBeforeSign = 3;
        private const int PayloadP0 = 4;
        private const int PayloadP1 = 8;
        private const int PayloadP2 = 12;
        private const int PayloadP3 = 16;
        private const int PayloadP4 = 20;
        private const int PayloadP5 = 24;
        private const int PayloadP6 = 28;
        private const int SystemWarpGuardFlag = 15;
        private const int WarpCompleteFlag = 6910;
        private const int PlayerWarpAnimation = 7410;
        private const int ActionMessage = 10010403;
        private const int BlockedEntryMessage = 15000281;
        private const int PlayerEntity = 10000;
        private const byte FlagOff = 0;
        private const byte FlagOn = 1;
        private const byte EndEvent = 0;
        private const byte RestartEvent = 1;

        private static readonly HashSet<(int Bank, int Id)> KnownOperations = new HashSet<(int, int)>
        {
            (0, 0), (3, 0), (3, 18), (1000, 3), (1000, 4), (1000, 5), (1000, 7), (1000, 8),
            (1001, 0), (1001, 1), (1003, 1), (1003, 2), (1003, 5), (2000, 5), (2003, 2),
            (2003, 14), (2003, 18), (2004, 41), (2005, 3), (2006, 1), (2007, 1),
        };

        public static Event5700Verification Verify(
            EMEVD.Event reference,
            EMEVD.Event candidate,
            IEnumerable<WarpEventCallMatrix.WarpEventCall> calls)
        {
            BehaviorProfile referenceContract = Describe(reference);
            BehaviorProfile candidateContract = Describe(candidate);
            if (!referenceContract.Terms.SetEquals(candidateContract.Terms))
            {
                string missing = string.Join(", ", referenceContract.Terms.Except(candidateContract.Terms).OrderBy(value => value, StringComparer.Ordinal));
                string added = string.Join(", ", candidateContract.Terms.Except(referenceContract.Terms).OrderBy(value => value, StringComparer.Ordinal));
                throw new InvalidDataException($"Event 5700 mode contracts differ. Missing from candidate: [{missing}]. Candidate-only: [{added}].");
            }

            WarpEventCallMatrix.WarpEventCall[] callRows = calls.ToArray();
            byte[] modes = callRows.Select(call => call.Mode).Distinct().OrderBy(mode => mode).ToArray();
            if (!new byte[] { 0, 1, 2, 3 }.SequenceEqual(modes))
                throw new InvalidDataException("The call matrix does not exercise all four Event5700 modes.");
            foreach (WarpEventCallMatrix.WarpEventCall call in callRows)
            {
                if (call.Mode > 3) throw new InvalidDataException($"Unsupported Event5700 mode {call.Mode} in the generated call matrix.");
                if (call.P6 < 0) throw new InvalidDataException("Event5700 initializer p6 must carry a non-negative magnitude; its sign is stored separately.");
            }

            return new Event5700Verification(
                callRows.Length,
                callRows.Select(Shape).Distinct(StringComparer.Ordinal).Count(),
                candidateContract.Terms.Count,
                modes);
        }

        public static void VerifyBuiltBehavior()
        {
            EMEVD.Event candidate = Event5700Builder.Build();
            BehaviorProfile profile = Describe(candidate);
            string[] required =
            {
                "dispatch:modes-0-1-2-3", "mode1:optional-lordvessel-cleanup-multiplayer-and-delay",
                "mode3:paired-action-warp-animation-cleanup-and-traversal-flag", "mode0:exit-trap-and-action-region-guards",
                "mode2:consume-trigger-and-system-warp-guard", "mode0-and-2:entry-flag-gate-and-blocked-entry-dialog",
                "mode0-and-2:signed-before-warp-flag-and-conditional-save",
                "mode0-and-2:save-before-map-transition-and-warp-complete-flag",
                "mode3:warp-animation-before-cleanup-and-flag", "route:conditional-termination-guards", "cutscene:none",
            };
            string[] missing = required.Where(term => !profile.Terms.Contains(term)).ToArray();
            if (missing.Length != 0)
                throw new InvalidDataException("Project-authored Event 5700 is missing behavior contracts: " + string.Join(", ", missing));

            VerifyMutationIsRejected("changed skip destination", evt => evt.Instructions[0].ArgData[0]--);
            VerifyMutationIsRejected("inverted event flag condition", evt => evt.Instructions[2].ArgData[1] = FlagOff);
            VerifyMutationIsRejected("moved save after map transition", MoveSaveAfterWarp);
            VerifyMutationIsRejected("changed restart behavior", evt => evt.Instructions[evt.Instructions.Count - 1].ArgData[0] = 0);
            VerifyMutationIsRejected("swapped destination area/block payload bindings", SwapDestinationBindings);

            var syntheticCalls = new[]
            {
                Call(0, true, 0x13, 0x21, 0, 0x31, 0x41, 0x61, 0x71, 0x81, 0x91),
                Call(0, false, 0x13, 0x21, 0x51, 0x31, 0x41, 0x61, 0x71, 0x81, 0x91),
                Call(1, false, 0x13, 0x21, 0x71, 0, 0x81, 0, 0, 0, 0),
                Call(2, true, 0x13, 0x21, 0x91, 0x31, 0, 0, 0x81, 0xA1, 0xB1),
                Call(3, false, 0x13, 0x21, 0xA1, 0x31, 0x41, 0x51, 0x61, 0, 0),
            };
            EMEVD.Event reference = Event5700Builder.Build();
            _ = Verify(reference, candidate, syntheticCalls);
        }

        private static void VerifyMutationIsRejected(string name, Action<EMEVD.Event> mutate)
        {
            EMEVD.Event reference = Event5700Builder.Build();
            EMEVD.Event candidate = Event5700Builder.Build();
            mutate(candidate);
            try
            {
                if (!Describe(reference).Terms.SetEquals(Describe(candidate).Terms)) return;
            }
            catch (InvalidDataException)
            {
                return;
            }
            throw new InvalidDataException($"Symbolic equivalence verifier accepted negative mutation: {name}.");
        }

        private static void MoveSaveAfterWarp(EMEVD.Event evt)
        {
            int save = evt.Instructions.FindIndex(instruction => instruction.Bank == 2000 && instruction.ID == 5);
            int warp = evt.Instructions.FindIndex(instruction => instruction.Bank == 2003 && instruction.ID == 14);
            EMEVD.Instruction saved = evt.Instructions[save];
            evt.Instructions[save] = evt.Instructions[warp];
            evt.Instructions[warp] = saved;
            foreach (EMEVD.Parameter parameter in evt.Parameters.Where(parameter => parameter.InstructionIndex == warp))
                parameter.InstructionIndex = save;
        }

        private static void SwapDestinationBindings(EMEVD.Event evt)
        {
            int warp = evt.Instructions.FindIndex(instruction => instruction.Bank == 2003 && instruction.ID == 14);
            EMEVD.Parameter area = evt.Parameters.Single(parameter => parameter.InstructionIndex == warp && parameter.TargetStartByte == 0);
            EMEVD.Parameter block = evt.Parameters.Single(parameter => parameter.InstructionIndex == warp && parameter.TargetStartByte == 1);
            long source = area.SourceStartByte;
            area.SourceStartByte = block.SourceStartByte;
            block.SourceStartByte = source;
        }

        public static void VerifyAndWriteMatrix(string referenceCommonPath, IEnumerable<string> generatedCommonPaths, string outputPath)
        {
            string repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
            string output = System.IO.Path.GetFullPath(outputPath);
            if (Inside(output, repositoryRoot))
                throw new InvalidOperationException("The value-level call matrix must be written outside the repository.");

            EMEVD referenceFile = ReadEmevd(referenceCommonPath);
            EMEVD.Event reference = referenceFile.Events.SingleOrDefault(evt => evt.ID == 5700)
                ?? throw new InvalidDataException("Reference common EMEVD has no event 5700.");
            EMEVD.Event candidate = Event5700Builder.Build();

            var allCalls = new List<WarpEventCallMatrix.WarpEventCall>();
            foreach (string path in generatedCommonPaths)
                allCalls.AddRange(WarpEventCallMatrix.Read(path));
            Event5700Verification result = Verify(reference, candidate, allCalls);

            var shapeGroups = allCalls.GroupBy(Shape).OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
            var lines = new List<string>
            {
                "Canonical Event5700 call-shape matrix (derived values stay in this temporary report).",
                $"Generated call rows: {result.CallCount}; unique mode/flag/optional-argument shapes: {result.ShapeCount}; matched mode-contract facts: {result.ContractFactCount}.",
                "Contract parity compares per-mode guards, parameter sources, observable effects, branch destinations, and required effect ordering. A bounded static analysis validates the dispatch graph; it does not execute the game EMEVD VM or prove in-game traversal.",
                "Shape key: mode, positive-before-warp bit, and p0..p6 presence mask; destination coverage is summarized as distinct map-pair count.",
                "",
            };
            foreach (IGrouping<string, WarpEventCallMatrix.WarpEventCall> group in shapeGroups)
            {
                int destinationCount = group.Select(call => (call.DestinationArea, call.DestinationBlock)).Distinct().Count();
                lines.Add($"{group.Key}: calls={group.Count()}, destinationMapPairs={destinationCount}");
            }
            string directory = System.IO.Path.GetDirectoryName(output);
            if (!System.IO.Directory.Exists(directory)) System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllLines(output, lines);
            Console.WriteLine($"Static Event5700 contract parity passed: {result.ContractFactCount} contract facts; call matrix covers {result.CallCount} rows and {result.ShapeCount} shapes.");
            Console.WriteLine($"Wrote aggregate call-shape matrix outside the repository: {output}");
        }

        private static BehaviorProfile Describe(EMEVD.Event evt)
        {
            if (evt == null || evt.ID != 5700) throw new InvalidDataException("Expected event 5700.");
            var terms = new HashSet<string>(StringComparer.Ordinal);
            EMEVD.Instruction unknown = evt.Instructions.FirstOrDefault(instruction => !KnownOperations.Contains((instruction.Bank, instruction.ID)));
            if (unknown != null)
                throw new InvalidDataException($"Event 5700 uses unreviewed operation {unknown.Bank}[{unknown.ID}].");
            Event5700Inspector.ValidateInstructionSignatures(evt);
            ValidateControlFlow(evt);
            ModeDispatch dispatch = ModeDispatch.Read(evt);
            if (evt.Instructions.Any(instruction => instruction.Bank == 2002)) terms.Add("cutscene:present");
            else terms.Add("cutscene:none");
            ValidateParameterBindings(evt);

            IReadOnlyList<int> modes = dispatch.Sections.Keys.OrderBy(mode => mode).ToArray();
            Add(terms, modes.SequenceEqual(new[] { 0, 1, 2, 3 }), "dispatch:modes-0-1-2-3");
            Add(terms, dispatch.Sections.TryGetValue(1, out InstructionRange mode1)
                && HasFlagCondition(evt, mode1, PayloadP2, FlagOn)
                && HasBoundOperation(evt, mode1, 2005, 3, 0, PayloadP0, 4)
                && HasBoundOperation(evt, mode1, 2006, 1, 0, PayloadP1, 4)
                && HasMultiplayerPolicy(evt, mode1, 1)
                && HasFloatWait(evt, mode1, 2f)
                && HasEndType(evt, mode1, RestartEvent), "mode1:optional-lordvessel-cleanup-multiplayer-and-delay");

            Add(terms, dispatch.Sections.TryGetValue(3, out InstructionRange mode3)
                && HasSkipFlagCondition(evt, mode3, PayloadP2, FlagOff)
                && HasAction(evt, mode3, PayloadP3, PayloadP0)
                && HasAction(evt, mode3, PayloadP4, PayloadP0)
                && HasShortWarp(evt, mode3, PayloadP3)
                && HasShortWarp(evt, mode3, PayloadP4)
                && HasLiteral(evt, mode3, 3, 18, 24, ActionMessage)
                && HasByteLiteral(evt, mode3, 3, 18, 28, 48)
                && HasOperationWithLiteral(evt, mode3, 2003, 18, 4, PlayerWarpAnimation)
                && HasBoundOperation(evt, mode3, 2005, 3, 0, PayloadP0, 4)
                && HasBoundOperation(evt, mode3, 2006, 1, 0, PayloadP1, 4)
                && HasSetFlagBinding(evt, mode3, PayloadP2, FlagOn)
                && HasEndType(evt, mode3, EndEvent), "mode3:paired-action-warp-animation-cleanup-and-traversal-flag");

            Add(terms, dispatch.Sections.TryGetValue(0, out InstructionRange mode0)
                && HasFlagCondition(evt, mode0, PayloadP4, FlagOn)
                && HasFlagCondition(evt, mode0, PayloadP2, FlagOff)
                && HasAction(evt, mode0, PayloadP3, PayloadP0), "mode0:exit-trap-and-action-region-guards");

            Add(terms, dispatch.Sections.TryGetValue(2, out InstructionRange mode2)
                && HasFlagCondition(evt, mode2, PayloadP0, FlagOn)
                && HasSetFlagBinding(evt, mode2, PayloadP0, FlagOff)
                && HasFlagConditionLiteral(evt, mode2, SystemWarpGuardFlag, FlagOn), "mode2:consume-trigger-and-system-warp-guard");

            Add(terms, HasEntryGate(evt, dispatch.CommonStart), "mode0-and-2:entry-flag-gate-and-blocked-entry-dialog");
            Add(terms, HasSignedBeforeWarpGate(evt, dispatch.CommonStart), "mode0-and-2:signed-before-warp-flag-and-conditional-save");
            Add(terms, HasMapTransition(evt, dispatch.CommonStart), "mode0-and-2:save-before-map-transition-and-warp-complete-flag");
            Add(terms, dispatch.Sections.TryGetValue(3, out InstructionRange mode3Order)
                && HasOrderedMode3Effects(evt, mode3Order), "mode3:warp-animation-before-cleanup-and-flag");
            Add(terms, HasOperation(evt, 1000, 8) && HasOperation(evt, 1003, 2), "route:conditional-termination-guards");

            return new BehaviorProfile(terms);
        }

        private static string Shape(WarpEventCallMatrix.WarpEventCall call)
        {
            int mask = 0;
            int[] values = { call.P0, call.P1, call.P2, call.P3, call.P4, call.P5, call.P6 };
            for (int i = 0; i < values.Length; i++) if (values[i] != 0) mask |= 1 << i;
            return $"mode={call.Mode};before={(call.BeforeWarpPositive ? 1 : 0)};optionalMask={mask:X2}";
        }

        private static void ValidateParameterBindings(EMEVD.Event evt)
        {
            for (int index = 0; index < evt.Parameters.Count; index++)
            {
                EMEVD.Parameter parameter = evt.Parameters[index];
                if (parameter.InstructionIndex < 0 || parameter.InstructionIndex >= evt.Instructions.Count)
                    throw new InvalidDataException($"Event 5700 parameter {index} references missing instruction {parameter.InstructionIndex}.");
                long targetEnd = parameter.TargetStartByte + parameter.ByteCount;
                if (parameter.TargetStartByte < 0 || parameter.ByteCount <= 0 || targetEnd > evt.Instructions[(int)parameter.InstructionIndex].ArgData.Length)
                    throw new InvalidDataException($"Event 5700 parameter {index} targets bytes outside instruction {parameter.InstructionIndex}.");
                long sourceEnd = parameter.SourceStartByte + parameter.ByteCount;
                if (parameter.SourceStartByte < 0 || sourceEnd > 32)
                    throw new InvalidDataException($"Event 5700 parameter {index} reads outside the 32-byte InitializeEvent payload.");
                for (int previous = 0; previous < index; previous++)
                {
                    EMEVD.Parameter other = evt.Parameters[previous];
                    if (other.InstructionIndex != parameter.InstructionIndex) continue;
                    bool overlaps = parameter.TargetStartByte < other.TargetStartByte + other.ByteCount
                        && other.TargetStartByte < targetEnd;
                    if (overlaps)
                        throw new InvalidDataException($"Event 5700 has overlapping parameter targets on instruction {parameter.InstructionIndex}.");
                }
            }
        }

        private static void ValidateControlFlow(EMEVD.Event evt)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                EMEVD.Instruction instruction = evt.Instructions[i];
                bool isSkip = (instruction.Bank == 1000 && instruction.ID is 3 or 5 or 7)
                    || (instruction.Bank == 1003 && instruction.ID is 1 or 5);
                if (!isSkip) continue;
                if (instruction.ArgData.Length == 0) throw new InvalidDataException($"Event 5700 skip at instruction {i} has no line count.");
                int destination = i + 1 + instruction.ArgData[0];
                if (destination > evt.Instructions.Count)
                    throw new InvalidDataException($"Event 5700 skip at instruction {i} targets line {destination} beyond its {evt.Instructions.Count}-instruction body.");
            }
        }

        private static bool HasOperation(EMEVD.Event evt, int bank, int id) => evt.Instructions.Any(instruction => instruction.Bank == bank && instruction.ID == id);

        private static bool HasOperation(EMEVD.Event evt, InstructionRange range, int bank, int id) =>
            Enumerable.Range(range.Start, range.Count).Any(index => evt.Instructions[index].Bank == bank && evt.Instructions[index].ID == id);

        private static bool HasAction(EMEVD.Event evt, InstructionRange range, int regionSource, int lineSource) =>
            HasBoundOperation(evt, range, 3, 18, 8, regionSource, 4)
            && HasBoundOperation(evt, range, 3, 18, 36, lineSource, 4);

        private static bool HasShortWarp(EMEVD.Event evt, InstructionRange range, int destinationSource) =>
            HasBoundOperation(evt, range, 2004, 41, 8, destinationSource, 4)
            && HasLiteral(evt, range, 2004, 41, 0, PlayerEntity)
            && HasByteLiteral(evt, range, 2004, 41, 4, 1);

        private static bool HasEntryGate(EMEVD.Event evt, int commonStart)
        {
            InstructionRange common = new InstructionRange(commonStart, evt.Instructions.Count);
            int noGate = FindSkipComparison(evt, common, PayloadP5, comparison: 0, left: 0, right: 0);
            int allowedFlag = FindSkipFlag(evt, common, PayloadP5, FlagOn);
            int dialog = FindLiteralOperation(evt, common, 2007, 1, 0, BlockedEntryMessage);
            int delay = FindFloatWait(evt, common, 1f);
            int retry = FindEnd(evt, common, RestartEvent);
            int beforeWarpCheck = FindSkipComparison(evt, common, PayloadP6, comparison: 0, left: 0, right: 0);
            if (noGate < 0 || allowedFlag < 0 || dialog < 0 || delay < 0 || retry < 0 || beforeWarpCheck < 0) return false;
            return noGate < allowedFlag && allowedFlag < dialog && dialog < delay && delay < retry && retry < beforeWarpCheck
                && SkipTarget(evt, noGate) == beforeWarpCheck
                && SkipTarget(evt, allowedFlag) == beforeWarpCheck;
        }

        private static bool HasSignedBeforeWarpGate(EMEVD.Event evt, int commonStart)
        {
            InstructionRange common = new InstructionRange(commonStart, evt.Instructions.Count);
            int skipWhenAbsent = FindSkipComparison(evt, common, PayloadP6, comparison: 0, left: 0, right: 0);
            int setSignedFlag = FindSetFlagBinding(evt, common, PayloadP6, stateSource: PayloadBeforeSign);
            int wait = FindFrameWait(evt, common, 1);
            int save = FindOperation(evt, common, 2000, 5);
            int warp = FindOperation(evt, common, 2003, 14);
            return skipWhenAbsent >= 0 && setSignedFlag >= 0 && wait >= 0 && save >= 0 && warp >= 0
                && skipWhenAbsent < setSignedFlag && setSignedFlag < wait && wait < save && save < warp
                && SkipTarget(evt, skipWhenAbsent) == warp;
        }

        private static bool HasMapTransition(EMEVD.Event evt, int commonStart)
        {
            InstructionRange common = new InstructionRange(commonStart, evt.Instructions.Count);
            int save = FindOperation(evt, common, 2000, 5);
            int warp = FindOperation(evt, common, 2003, 14);
            int complete = FindSetFlagLiteral(evt, common, WarpCompleteFlag, FlagOn);
            int restart = FindEndAfter(evt, common, RestartEvent, complete);
            return save >= 0 && warp >= 0 && complete >= 0 && restart >= 0 && save < warp && warp < complete && complete < restart
                && HasBoundOperation(evt, common, 2003, 14, 0, PayloadArea, 1)
                && HasBoundOperation(evt, common, 2003, 14, 1, PayloadBlock, 1)
                && HasBoundOperation(evt, common, 2003, 14, 4, PayloadP1, 4);
        }

        private static bool HasOrderedMode3Effects(EMEVD.Event evt, InstructionRange range)
        {
            int lastShortWarp = Math.Max(FindBoundOperation(evt, range, 2004, 41, 8, PayloadP3, 4),
                FindBoundOperation(evt, range, 2004, 41, 8, PayloadP4, 4));
            int animation = FindLiteralOperation(evt, range, 2003, 18, 4, PlayerWarpAnimation);
            int deactivate = FindBoundOperationAfter(evt, range, 2005, 3, 0, PayloadP0, 4, animation);
            int deleteSfx = FindBoundOperationAfter(evt, range, 2006, 1, 0, PayloadP1, 4, deactivate);
            int setTraversalFlag = FindSetFlagBinding(evt, range, PayloadP2, desiredState: FlagOn);
            int end = FindEndAfter(evt, range, EndEvent, setTraversalFlag);
            return lastShortWarp >= 0 && animation > lastShortWarp && animation < deactivate && deactivate < deleteSfx
                && deleteSfx < setTraversalFlag && setTraversalFlag < end;
        }

        private static bool HasBoundOperation(EMEVD.Event evt, InstructionRange range, int bank, int id, int targetOffset, int sourceOffset, int byteCount) =>
            FindBoundOperation(evt, range, bank, id, targetOffset, sourceOffset, byteCount) >= 0;

        private static int FindBoundOperation(EMEVD.Event evt, InstructionRange range, int bank, int id, int targetOffset, int sourceOffset, int byteCount) =>
            Enumerable.Range(range.Start, range.Count).FirstOrDefault(index => evt.Instructions[index].Bank == bank && evt.Instructions[index].ID == id
                && HasBinding(evt, index, targetOffset, sourceOffset, byteCount), -1);

        private static int FindBoundOperationAfter(EMEVD.Event evt, InstructionRange range, int bank, int id, int targetOffset, int sourceOffset, int byteCount, int afterIndex)
        {
            int start = Math.Max(range.Start, afterIndex + 1);
            return Enumerable.Range(start, Math.Max(0, range.End - start)).FirstOrDefault(index => evt.Instructions[index].Bank == bank && evt.Instructions[index].ID == id
                && HasBinding(evt, index, targetOffset, sourceOffset, byteCount), -1);
        }

        private static bool HasLiteral(EMEVD.Event evt, InstructionRange range, int bank, int id, int targetOffset, int value) =>
            FindLiteralOperation(evt, range, bank, id, targetOffset, value) >= 0;

        private static bool HasOperationWithLiteral(EMEVD.Event evt, InstructionRange range, int bank, int id, int targetOffset, int value) =>
            HasLiteral(evt, range, bank, id, targetOffset, value);

        private static int FindLiteralOperation(EMEVD.Event evt, InstructionRange range, int bank, int id, int targetOffset, int value) =>
            Enumerable.Range(range.Start, range.Count).FirstOrDefault(index =>
                evt.Instructions[index].Bank == bank && evt.Instructions[index].ID == id
                && !HasTargetBinding(evt, index, targetOffset)
                && targetOffset >= 0 && targetOffset + sizeof(int) <= evt.Instructions[index].ArgData.Length
                && BitConverter.ToInt32(evt.Instructions[index].ArgData, targetOffset) == value, -1);

        private static bool HasByteLiteral(EMEVD.Event evt, InstructionRange range, int bank, int id, int targetOffset, byte value) =>
            Enumerable.Range(range.Start, range.Count).Any(index =>
                evt.Instructions[index].Bank == bank && evt.Instructions[index].ID == id
                && !HasTargetBinding(evt, index, targetOffset)
                && targetOffset >= 0 && targetOffset < evt.Instructions[index].ArgData.Length
                && evt.Instructions[index].ArgData[targetOffset] == value);

        private static bool HasFlagCondition(EMEVD.Event evt, InstructionRange range, int sourceFlag, byte desiredState) =>
            Enumerable.Range(range.Start, range.Count).Any(index => evt.Instructions[index].Bank == 3 && evt.Instructions[index].ID == 0
                && HasBinding(evt, index, 4, sourceFlag, 4) && evt.Instructions[index].ArgData[1] == desiredState);

        private static bool HasFlagConditionLiteral(EMEVD.Event evt, InstructionRange range, int flag, byte desiredState) =>
            Enumerable.Range(range.Start, range.Count).Any(index => evt.Instructions[index].Bank == 3 && evt.Instructions[index].ID == 0
                && !HasTargetBinding(evt, index, 4) && BitConverter.ToInt32(evt.Instructions[index].ArgData, 4) == flag
                && evt.Instructions[index].ArgData[1] == desiredState);

        private static bool HasSkipFlagCondition(EMEVD.Event evt, InstructionRange range, int sourceFlag, byte desiredState) =>
            Enumerable.Range(range.Start, range.Count).Any(index => evt.Instructions[index].Bank == 1003 && evt.Instructions[index].ID == 1
                && evt.Instructions[index].ArgData[1] == desiredState && HasBinding(evt, index, 4, sourceFlag, 4));

        private static bool HasSetFlagBinding(EMEVD.Event evt, InstructionRange range, int sourceFlag, byte desiredState) =>
            FindSetFlagBinding(evt, range, sourceFlag, desiredState) >= 0;

        private static bool HasSetFlagBinding(EMEVD.Event evt, InstructionRange range, int sourceFlag, int stateSource) =>
            FindSetFlagBinding(evt, range, sourceFlag, stateSource) >= 0;

        private static int FindSetFlagBinding(EMEVD.Event evt, InstructionRange range, int sourceFlag, byte? desiredState = null, int? stateSource = null)
        {
            return Enumerable.Range(range.Start, range.Count).FirstOrDefault(index =>
            {
                EMEVD.Instruction instruction = evt.Instructions[index];
                if (instruction.Bank != 2003 || instruction.ID != 2) return false;
                bool flag = HasBinding(evt, index, 0, sourceFlag, 4)
                    || (!HasTargetBinding(evt, index, 0) && BitConverter.ToInt32(instruction.ArgData, 0) == sourceFlag);
                if (!flag) return false;
                if (stateSource.HasValue) return HasBinding(evt, index, 4, stateSource.Value, 1);
                return desiredState.HasValue && !HasTargetBinding(evt, index, 4) && instruction.ArgData[4] == desiredState.Value;
            }, -1);
        }

        private static int FindSetFlagLiteral(EMEVD.Event evt, InstructionRange range, int flag, byte state) =>
            Enumerable.Range(range.Start, range.Count).FirstOrDefault(index => evt.Instructions[index].Bank == 2003 && evt.Instructions[index].ID == 2
                && !HasTargetBinding(evt, index, 0) && BitConverter.ToInt32(evt.Instructions[index].ArgData, 0) == flag
                && evt.Instructions[index].ArgData[4] == state, -1);

        private static int FindOperation(EMEVD.Event evt, InstructionRange range, int bank, int id) =>
            Enumerable.Range(range.Start, range.Count).FirstOrDefault(index => evt.Instructions[index].Bank == bank && evt.Instructions[index].ID == id, -1);

        private static int FindEnd(EMEVD.Event evt, InstructionRange range, byte endType) =>
            Enumerable.Range(range.Start, range.Count).FirstOrDefault(index => evt.Instructions[index].Bank == 1000 && evt.Instructions[index].ID == 4
                && evt.Instructions[index].ArgData.Length > 0 && evt.Instructions[index].ArgData[0] == endType, -1);

        private static int FindEndAfter(EMEVD.Event evt, InstructionRange range, byte endType, int afterIndex)
        {
            int start = Math.Max(range.Start, afterIndex + 1);
            return Enumerable.Range(start, Math.Max(0, range.End - start)).FirstOrDefault(index => evt.Instructions[index].Bank == 1000 && evt.Instructions[index].ID == 4
                && evt.Instructions[index].ArgData.Length > 0 && evt.Instructions[index].ArgData[0] == endType, -1);
        }

        private static bool HasEndType(EMEVD.Event evt, InstructionRange range, byte endType) => FindEnd(evt, range, endType) >= 0;

        private static bool HasFloatWait(EMEVD.Event evt, InstructionRange range, float seconds) =>
            Enumerable.Range(range.Start, range.Count).Any(index => evt.Instructions[index].Bank == 1001 && evt.Instructions[index].ID == 0
                && evt.Instructions[index].ArgData.Length >= sizeof(float) && BitConverter.ToSingle(evt.Instructions[index].ArgData, 0) == seconds);

        private static bool HasMultiplayerPolicy(EMEVD.Event evt, InstructionRange range, sbyte desiredState) =>
            Enumerable.Range(range.Start, range.Count).Any(index => evt.Instructions[index].Bank == 1003 && evt.Instructions[index].ID == 5
                && evt.Instructions[index].ArgData.Length >= 2 && unchecked((sbyte)evt.Instructions[index].ArgData[1]) == desiredState);

        private static int FindFrameWait(EMEVD.Event evt, InstructionRange range, int frames) =>
            Enumerable.Range(range.Start, range.Count).FirstOrDefault(index => evt.Instructions[index].Bank == 1001 && evt.Instructions[index].ID == 1
                && evt.Instructions[index].ArgData.Length >= sizeof(int) && BitConverter.ToInt32(evt.Instructions[index].ArgData, 0) == frames, -1);

        private static int FindFloatWait(EMEVD.Event evt, InstructionRange range, float seconds) =>
            Enumerable.Range(range.Start, range.Count).FirstOrDefault(index => evt.Instructions[index].Bank == 1001 && evt.Instructions[index].ID == 0
                && evt.Instructions[index].ArgData.Length >= sizeof(float) && BitConverter.ToSingle(evt.Instructions[index].ArgData, 0) == seconds, -1);

        private static int FindSkipComparison(EMEVD.Event evt, InstructionRange range, int source, byte comparison, int left, int right) =>
            Enumerable.Range(range.Start, range.Count).FirstOrDefault(index =>
            {
                EMEVD.Instruction instruction = evt.Instructions[index];
                if (instruction.Bank != 1000 || instruction.ID != 5 || !HasBinding(evt, index, 4, source, source == PayloadMode ? 1 : 4)) return false;
                object[] args = instruction.UnpackArgs(new[] { Arg(0), Arg(3), Arg(5), Arg(5) }).ToArray();
                return (sbyte)args[1] == unchecked((sbyte)comparison) && (int)args[2] == left && (int)args[3] == right;
            }, -1);

        private static int FindSkipFlag(EMEVD.Event evt, InstructionRange range, int source, byte desiredState) =>
            Enumerable.Range(range.Start, range.Count).FirstOrDefault(index => evt.Instructions[index].Bank == 1003 && evt.Instructions[index].ID == 1
                && evt.Instructions[index].ArgData[1] == desiredState && HasBinding(evt, index, 4, source, 4), -1);

        private static int SkipTarget(EMEVD.Event evt, int instructionIndex) =>
            instructionIndex < 0 ? -1 : instructionIndex + 1 + evt.Instructions[instructionIndex].ArgData[0];

        private static int FindSetFlagBinding(EMEVD.Event evt, InstructionRange range, int sourceFlag, int stateSource) =>
            FindSetFlagBinding(evt, range, sourceFlag, desiredState: null, stateSource: stateSource);

        private static int FindSetFlagBinding(EMEVD.Event evt, InstructionRange range, int sourceFlag, byte desiredState) =>
            FindSetFlagBinding(evt, range, sourceFlag, desiredState: (byte?)desiredState, stateSource: null);

        private static bool HasOperationWithLiteral(EMEVD.Event evt, int bank, int id, int targetOffset, int value)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                EMEVD.Instruction instruction = evt.Instructions[i];
                if (instruction.Bank != bank || instruction.ID != id || HasTargetBinding(evt, i, targetOffset)) continue;
                if (targetOffset < 0 || targetOffset + sizeof(int) > instruction.ArgData.Length) continue;
                if (BitConverter.ToInt32(instruction.ArgData, targetOffset) == value) return true;
            }
            return false;
        }

        private static bool HasLiteral(EMEVD.Event evt, int bank, int id, int targetOffset, int value) =>
            HasOperationWithLiteral(evt, bank, id, targetOffset, value);

        private static bool HasByteLiteral(EMEVD.Event evt, int bank, int id, int targetOffset, byte value)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                EMEVD.Instruction instruction = evt.Instructions[i];
                if (instruction.Bank != bank || instruction.ID != id || HasTargetBinding(evt, i, targetOffset)) continue;
                if (targetOffset >= 0 && targetOffset < instruction.ArgData.Length && instruction.ArgData[targetOffset] == value) return true;
            }
            return false;
        }

        private static bool HasFloatWait(EMEVD.Event evt, float seconds)
        {
            foreach (EMEVD.Instruction instruction in evt.Instructions)
            {
                if (instruction.Bank != 1001 || instruction.ID != 0 || instruction.ArgData.Length < sizeof(float)) continue;
                if (BitConverter.ToSingle(instruction.ArgData, 0) == seconds) return true;
            }
            return false;
        }

        private static bool HasMultiplayerPolicy(EMEVD.Event evt, sbyte desiredState)
        {
            foreach (EMEVD.Instruction instruction in evt.Instructions)
                if (instruction.Bank == 1003 && instruction.ID == 5 && instruction.ArgData.Length >= 2 && unchecked((sbyte)instruction.ArgData[1]) == desiredState)
                    return true;
            return false;
        }

        private static bool HasBoundOperation(EMEVD.Event evt, int bank, int id, int targetOffset, int sourceOffset, int byteCount) =>
            Enumerable.Range(0, evt.Instructions.Count).Any(i => evt.Instructions[i].Bank == bank && evt.Instructions[i].ID == id && HasBinding(evt, i, targetOffset, sourceOffset, byteCount));

        private static bool HasSetFlagBinding(EMEVD.Event evt, int sourceFlag, byte? desiredState, int? stateSource = null)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                EMEVD.Instruction instruction = evt.Instructions[i];
                if (instruction.Bank != 2003 || instruction.ID != 2) continue;
                bool flagMatches = HasBinding(evt, i, 0, sourceFlag, 4)
                    || (!HasTargetBinding(evt, i, 0) && BitConverter.ToInt32(instruction.ArgData, 0) == sourceFlag);
                if (!flagMatches) continue;
                if (stateSource.HasValue && HasBinding(evt, i, 4, stateSource.Value, 1)) return true;
                if (desiredState.HasValue && !HasTargetBinding(evt, i, 4) && instruction.ArgData[4] == desiredState.Value) return true;
            }
            return false;
        }

        private static bool HasFlagCondition(EMEVD.Event evt, int sourceFlag, byte desiredState)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                EMEVD.Instruction instruction = evt.Instructions[i];
                if (instruction.Bank != 3 || instruction.ID != 0) continue;
                if (HasBinding(evt, i, 4, sourceFlag, 4) && instruction.ArgData[1] == desiredState) return true;
            }
            return false;
        }

        private static bool HasSkipFlagCondition(EMEVD.Event evt, int sourceFlag, byte desiredState)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                EMEVD.Instruction instruction = evt.Instructions[i];
                if (instruction.Bank != 1003 || instruction.ID != 1 || instruction.ArgData[1] != desiredState) continue;
                if (HasBinding(evt, i, 4, sourceFlag, 4)) return true;
            }
            return false;
        }

        private static bool HasBinding(EMEVD.Event evt, int instructionIndex, int target, int source, int count) =>
            evt.Parameters.Any(parameter => parameter.InstructionIndex == instructionIndex && parameter.TargetStartByte == target && parameter.SourceStartByte == source && parameter.ByteCount == count);

        private static bool HasTargetBinding(EMEVD.Event evt, int instructionIndex, int target) =>
            evt.Parameters.Any(parameter => parameter.InstructionIndex == instructionIndex && parameter.TargetStartByte == target);

        private static void Add(HashSet<string> terms, bool condition, string term)
        {
            if (condition) terms.Add(term);
        }

        private static EMEVD.Instruction.ArgType Arg(int code) => (EMEVD.Instruction.ArgType)code;

        private static EMEVD ReadEmevd(string path)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            return EMEVD.Read(bytes);
        }

        private static bool Inside(string candidate, string root) =>
            System.IO.Path.GetFullPath(candidate).TrimEnd(System.IO.Path.DirectorySeparatorChar).StartsWith(
                System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        private static string FindRepositoryRoot(string start)
        {
            var current = new System.IO.DirectoryInfo(start);
            while (current != null)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(current.FullName, ".git"))) return current.FullName;
                current = current.Parent;
            }
            throw new System.IO.DirectoryNotFoundException("Could not locate the repository root.");
        }

        private static WarpEventCallMatrix.WarpEventCall Call(byte mode, bool positive, int area, int block, int p0, int p1, int p2, int p3, int p4, int p5, int p6) =>
            new WarpEventCallMatrix.WarpEventCall(0, 0, mode, (byte)area, (byte)block, positive, p0, p1, p2, p3, p4, p5, p6);

        private sealed class BehaviorProfile
        {
            public BehaviorProfile(HashSet<string> terms) => Terms = terms;
            public HashSet<string> Terms { get; }
        }

        private readonly record struct InstructionRange(int Start, int End)
        {
            public int Count => Math.Max(0, End - Start);
        }

        private sealed class ModeDispatch
        {
            private ModeDispatch(Dictionary<int, InstructionRange> sections, int commonStart)
            {
                Sections = sections;
                CommonStart = commonStart;
            }

            public IReadOnlyDictionary<int, InstructionRange> Sections { get; }
            public int CommonStart { get; }

            public static ModeDispatch Read(EMEVD.Event evt)
            {
                var rows = new List<(int Index, int Mode, int Target)>();
                for (int index = 0; index < evt.Instructions.Count; index++)
                {
                    EMEVD.Instruction instruction = evt.Instructions[index];
                    if (instruction.Bank != 1000 || instruction.ID != 5 || !HasBinding(evt, index, 4, PayloadMode, 1)) continue;
                    object[] args = instruction.UnpackArgs(new[] { Arg(0), Arg(3), Arg(5), Arg(5) }).ToArray();
                    if ((sbyte)args[1] != 1 || (int)args[2] != 0)
                        throw new InvalidDataException($"Event 5700 mode dispatch at {index} must branch when mode differs from its selector.");
                    int target = index + 1 + (byte)args[0];
                    if ((int)args[3] is < 0 or > 3 || target <= index || target > evt.Instructions.Count)
                        throw new InvalidDataException($"Event 5700 mode dispatch at {index} has an invalid selector or skip destination.");
                    rows.Add((index, (int)args[3], target));
                }

                if (rows.Count != 4 || rows.Select(row => row.Mode).Distinct().Count() != 4
                    || !rows.Select(row => row.Mode).OrderBy(mode => mode).SequenceEqual(new[] { 0, 1, 2, 3 }))
                    throw new InvalidDataException("Event 5700 must have one mode dispatch for each mode 0 through 3.");

                rows.Sort((left, right) => left.Index.CompareTo(right.Index));
                for (int index = 0; index < rows.Count - 1; index++)
                {
                    if (rows[index].Target != rows[index + 1].Index)
                        throw new InvalidDataException($"Event 5700 mode {rows[index].Mode} bypasses the next dispatch; its false branch must target instruction {rows[index + 1].Index}.");
                }

                int commonStart = rows[^1].Target;
                if (commonStart <= rows[^1].Index || commonStart >= evt.Instructions.Count)
                    throw new InvalidDataException("Event 5700 final mode dispatch has no shared transition tail.");

                var sections = new Dictionary<int, InstructionRange>();
                for (int index = 0; index < rows.Count; index++)
                {
                    int end = index + 1 < rows.Count ? rows[index + 1].Index : commonStart;
                    if (end <= rows[index].Index + 1)
                        throw new InvalidDataException($"Event 5700 mode {rows[index].Mode} has an empty dispatch body.");
                    sections.Add(rows[index].Mode, new InstructionRange(rows[index].Index + 1, end));
                }

                return new ModeDispatch(sections, commonStart);
            }
        }

    }

    internal sealed record Event5700Verification(int CallCount, int ShapeCount, int ContractFactCount, IReadOnlyList<byte> Modes);
}
