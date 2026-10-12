using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>
    /// Development-only event reader. It emits named operations and symbolic
    /// argument bindings to a caller-selected file outside the repository.
    /// It deliberately does not serialize an EMEVD body or byte-array dump.
    /// </summary>
    internal static class Event5700Inspector
    {
        private static readonly IReadOnlyDictionary<(int Bank, int Id), (string Name, string Category)> Operations =
            new Dictionary<(int, int), (string, string)>
            {
                [(0, 0)] = ("IfConditionGroup", "condition / control flow"),
                [(3, 0)] = ("IfEventFlag", "condition / flag"),
                [(3, 18)] = ("IfActionButtonLineSegment", "action button / interaction"),
                [(1000, 3)] = ("SkipUnconditionally", "branch / control flow"),
                [(1000, 4)] = ("EndUnconditionally", "branch / control flow"),
                [(1000, 5)] = ("SkipIfComparison", "condition / control flow"),
                [(1000, 7)] = ("SkipIfConditionGroupStateCompiled", "condition / control flow"),
                [(1000, 8)] = ("EndIfConditionGroupStateCompiled", "condition / control flow"),
                [(1001, 0)] = ("WaitFixedTimeSeconds", "wait / lifecycle"),
                [(1001, 1)] = ("WaitFixedTimeFrames", "wait / lifecycle"),
                [(1003, 1)] = ("SkipIfEventFlag", "condition / flag / control flow"),
                [(1003, 2)] = ("EndIfEventFlag", "condition / flag / lifecycle"),
                [(1003, 5)] = ("SkipIfMultiplayerState", "condition / multiplayer"),
                [(2000, 5)] = ("SaveRequest", "save / respawn"),
                [(2003, 2)] = ("SetEventFlag", "flag"),
                [(2003, 14)] = ("WarpPlayer", "map transition / player warp"),
                [(2003, 18)] = ("ForceAnimationPlayback", "animation"),
                [(2004, 41)] = ("IssueShortWarpRequest", "warp / player position"),
                [(2005, 3)] = ("DeactivateObject", "cleanup / interaction"),
                [(2006, 1)] = ("DeleteMapSFX", "cleanup / map effect"),
                [(2007, 1)] = ("DisplayGenericDialog", "dialog / interaction"),
            };

        public static void Write(string commonEmevdPath, string outputPath)
        {
            string root = FindRepositoryRoot(Directory.GetCurrentDirectory());
            string fullOutput = Path.GetFullPath(outputPath);
            if (IsInside(fullOutput, root))
                throw new InvalidOperationException("Detailed event analysis must be written outside the repository.");

            byte[] bytes = File.ReadAllBytes(commonEmevdPath);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD common = EMEVD.Read(bytes);
            EMEVD.Event evt = common.Events.SingleOrDefault(e => e.ID == 5700)
                ?? throw new InvalidDataException("Event 5700 was not found in the supplied EMEVD.");

            var text = new StringBuilder();
            text.AppendLine("Project analysis of common event 5700");
            text.AppendLine("Generated from the caller-selected reference file. This report is temporary analysis; do not commit or package it.");
            text.AppendLine($"Rest behavior: {evt.RestBehavior}; instructions: {evt.Instructions.Count}; parameter bindings: {evt.Parameters.Count}.");
            text.AppendLine("Event parameter source offsets are relative to the InitializeEvent payload (after slot and event id): mode @0; destination area @1; destination block @2; positive-before-warp @3; p0 @4; p1 @8; p2 @12; p3 @16; p4 @20; p5 @24; p6 magnitude @28.");
            text.AppendLine();

            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                EMEVD.Instruction instruction = evt.Instructions[i];
                (string name, string category) = Operations.TryGetValue((instruction.Bank, instruction.ID), out var operation)
                    ? operation
                    : ($"UnknownOperation({instruction.Bank},{instruction.ID})", "unknown");
                text.Append(i.ToString("D3", CultureInfo.InvariantCulture))
                    .Append("  ").Append(name).Append("  [").Append(category).Append(']')
                    .Append("  args=").Append(DecodeArguments(instruction));

                List<EMEVD.Parameter> bindings = evt.Parameters
                    .Where(parameter => parameter.InstructionIndex == i)
                    .OrderBy(parameter => parameter.TargetStartByte)
                    .ToList();
                if (bindings.Count != 0)
                {
                    text.Append("  bindings=");
                    text.Append(string.Join("; ", bindings.Select(binding =>
                        $"target+{binding.TargetStartByte}:{binding.ByteCount} <- {InitializerSource(binding.SourceStartByte)}")));
                }
                text.AppendLine();
            }

            text.AppendLine();
            text.AppendLine("Control-flow operand references (numeric labels are shown symbolically; target resolution depends on EMEVD label semantics):");
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                EMEVD.Instruction instruction = evt.Instructions[i];
                if (!IsBranch(instruction.Bank, instruction.ID)) continue;
                text.Append(i.ToString("D3", CultureInfo.InvariantCulture)).Append("  ")
                    .Append(Name(instruction.Bank, instruction.ID)).Append("  ")
                    .AppendLine(DecodeArguments(instruction));
            }

            string directory = Path.GetDirectoryName(fullOutput);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(fullOutput, text.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"Wrote named temporary event analysis outside the repository: {fullOutput}");
        }

        public static void ValidateInstructionSignatures(EMEVD.Event evt)
        {
            foreach (EMEVD.Instruction instruction in evt.Instructions)
            {
                (string[] names, EMEVD.Instruction.ArgType[] types) = Signature(instruction.Bank, instruction.ID);
                if (types == null)
                    throw new InvalidDataException($"Event {evt.ID} contains an operation without a reviewed argument signature: {instruction.Bank}[{instruction.ID}].");
                try
                {
                    object[] values = instruction.UnpackArgs(types).ToArray();
                    if (values.Length != names.Length)
                        throw new InvalidDataException($"Event {evt.ID} operation {instruction.Bank}[{instruction.ID}] signature argument count differs from its decoded values.");
                }
                catch (Exception exception) when (exception is not InvalidDataException)
                {
                    throw new InvalidDataException($"Event {evt.ID} operation {instruction.Bank}[{instruction.ID}] does not match its reviewed argument signature.", exception);
                }
            }
        }

        public static string DescribeInstruction(EMEVD.Instruction instruction)
        {
            (string name, string category) = Operations.TryGetValue((instruction.Bank, instruction.ID), out var operation)
                ? operation
                : ($"UnknownOperation({instruction.Bank},{instruction.ID})", "unknown");
            return name + " [" + category + "] " + DecodeArguments(instruction);
        }

        private static string DecodeArguments(EMEVD.Instruction instruction)
        {
            if (instruction.ArgData == null || instruction.ArgData.Length == 0) return "<none>";
            (string[] names, EMEVD.Instruction.ArgType[] types) = Signature(instruction.Bank, instruction.ID);
            if (types == null) return "<unknown layout; " + instruction.ArgData.Length.ToString(CultureInfo.InvariantCulture) + " bytes>";
            try
            {
                List<object> values = instruction.UnpackArgs(types);
                return "{" + string.Join(", ", values.Select((value, index) =>
                    (index < names.Length ? names[index] : "arg" + index.ToString(CultureInfo.InvariantCulture)) + "=" + FormatValue(value))) + "}";
            }
            catch (Exception exception)
            {
                return "<signature decode failed: " + exception.GetType().Name + "; " + instruction.ArgData.Length.ToString(CultureInfo.InvariantCulture) + " bytes>";
            }
        }

        private static (string[] Names, EMEVD.Instruction.ArgType[] Types) Signature(int bank, int id)
        {
            const int Byte = 0, SByte = 3, Int16 = 4, Int32 = 5, Single = 6;
            return (bank, id) switch
            {
                (0, 0) => (new[] { "resultGroup", "desiredState", "targetGroup" }, Types(SByte, Byte, SByte)),
                (3, 0) => (new[] { "resultGroup", "desiredFlagState", "targetFlagType", "targetFlagId" }, Types(SByte, Byte, Byte, Int32)),
                (3, 18) => (new[] { "resultGroup", "targetEntityType", "targetEntityId", "reactionAngle", "dummyPoly", "reactionDistance", "helpMessageId", "reactionType", "padId", "lineSegmentEntityId" }, Types(SByte, Int32, Int32, Single, Int16, Single, Int32, Byte, Int32, Int32)),
                (1000, 3) => (new[] { "numberOfSkippedLines" }, Types(Byte)),
                (1000, 4) => (new[] { "executionEndType" }, Types(Byte)),
                (1000, 5) => (new[] { "numberOfSkippedLines", "comparisonType", "left", "right" }, Types(Byte, SByte, Int32, Int32)),
                (1000, 7) => (new[] { "numberOfSkippedLines", "desiredState", "targetGroup" }, Types(Byte, Byte, SByte)),
                (1000, 8) => (new[] { "executionEndType", "desiredState", "targetGroup" }, Types(Byte, Byte, SByte)),
                (1001, 0) => (new[] { "seconds" }, Types(Single)),
                (1001, 1) => (new[] { "frames" }, Types(Int32)),
                (1003, 1) => (new[] { "numberOfSkippedLines", "desiredFlagState", "targetFlagType", "targetFlagId" }, Types(Byte, Byte, Byte, Int32)),
                (1003, 2) => (new[] { "executionEndType", "desiredFlagState", "targetFlagType", "targetFlagId" }, Types(Byte, Byte, Byte, Int32)),
                (1003, 5) => (new[] { "numberOfSkippedLines", "desiredState" }, Types(Byte, SByte)),
                (2000, 5) => (new[] { "dummy" }, Types(Byte)),
                (2003, 2) => (new[] { "eventFlagId", "state" }, Types(Int32, Byte)),
                (2003, 14) => (new[] { "areaId", "blockId", "initialAreaId" }, Types(Byte, Byte, Int32)),
                (2003, 18) => (new[] { "entityId", "animationId", "loop", "wait", "ignoreTransition" }, Types(Int32, Int32, Byte, Byte, Byte)),
                (2004, 41) => (new[] { "entityId", "warpType", "destinationEntityId", "dummyPoly" }, Types(Int32, Byte, Int32, Int32)),
                (2005, 3) => (new[] { "objectEntityId", "state" }, Types(Int32, Byte)),
                (2006, 1) => (new[] { "entityId", "onlyDeleteRoot" }, Types(Int32, Byte)),
                (2007, 1) => (new[] { "messageId", "dialogType", "numberOfOptions", "entityId", "displayDistance" }, Types(Int32, Int16, Int16, Int32, Single)),
                _ => (Array.Empty<string>(), null),
            };
        }

        private static EMEVD.Instruction.ArgType[] Types(params int[] codes) =>
            codes.Select(code => (EMEVD.Instruction.ArgType)code).ToArray();

        private static string FormatValue(object value)
        {
            return value switch
            {
                float number => number.ToString("R", CultureInfo.InvariantCulture),
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
                _ => value?.ToString() ?? "null",
            };
        }

        private static string InitializerSource(long offset)
        {
            return offset switch
            {
                0 => "mode",
                1 => "destination area",
                2 => "destination block",
                3 => "positive-before-warp boolean",
                4 => "p0",
                8 => "p1",
                12 => "p2",
                16 => "p3",
                20 => "p4",
                24 => "p5",
                28 => "p6 magnitude",
                _ => "initializer source offset " + offset.ToString(CultureInfo.InvariantCulture),
            };
        }

        private static bool IsBranch(int bank, int id) =>
            (bank == 1000 && id is 3 or 4 or 5 or 7 or 8) || (bank == 1003 && id is 1 or 2 or 5) || (bank == 0 && id == 0);

        private static string Name(int bank, int id) => Operations.TryGetValue((bank, id), out var operation)
            ? operation.Name
            : $"UnknownOperation({bank},{id})";

        private static bool IsInside(string path, string root)
        {
            string candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string repository = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(repository, StringComparison.OrdinalIgnoreCase);
        }

        private static string FindRepositoryRoot(string directory)
        {
            DirectoryInfo current = new DirectoryInfo(directory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repository root from the current directory.");
        }
    }
}
