using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>
    /// Reads the InitializeEvent ABI used by the pinned FogMod caller. This utility is for
    /// development analysis only; callers should direct any detailed output outside the repo.
    /// </summary>
    internal static class WarpEventCallMatrix
    {
        private const long WarpEventId = 5700;
        private const int InitializerPayloadSize = 32;
        private const int InitializeEventHeaderSize = 8;

        public static IReadOnlyList<WarpEventCall> Read(string commonEmevdPath)
        {
            byte[] bytes = File.ReadAllBytes(commonEmevdPath);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD common = EMEVD.Read(bytes);
            EMEVD.Event eventZero = common.Events.SingleOrDefault(e => e.ID == 0)
                ?? throw new InvalidDataException("The common EMEVD has no event 0.");

            var calls = new List<WarpEventCall>();
            for (int instructionIndex = 0; instructionIndex < eventZero.Instructions.Count; instructionIndex++)
            {
                EMEVD.Instruction instruction = eventZero.Instructions[instructionIndex];
                if (instruction.Bank != 2000 || instruction.ID != 0) continue;
                byte[] args = instruction.ArgData;
                if (args.Length < InitializeEventHeaderSize) continue;
                if (BitConverter.ToUInt32(args, 4) != WarpEventId) continue;
                if (args.Length != InitializeEventHeaderSize + InitializerPayloadSize)
                    throw new InvalidDataException($"Event 5700 initializer at instruction {instructionIndex} has {args.Length - InitializeEventHeaderSize} payload bytes; expected {InitializerPayloadSize}.");

                int payload = InitializeEventHeaderSize;
                calls.Add(new WarpEventCall(
                    instructionIndex,
                    BitConverter.ToInt32(args, 0),
                    args[payload],
                    args[payload + 1],
                    args[payload + 2],
                    args[payload + 3] != 0,
                    ReadInt32(args, payload + 4),
                    ReadInt32(args, payload + 8),
                    ReadInt32(args, payload + 12),
                    ReadInt32(args, payload + 16),
                    ReadInt32(args, payload + 20),
                    ReadInt32(args, payload + 24),
                    ReadInt32(args, payload + 28)));
            }

            return calls;
        }

        public static void Inspect(string commonEmevdPath)
        {
            IReadOnlyList<WarpEventCall> calls = Read(commonEmevdPath);
            Console.WriteLine($"event=5700 calls={calls.Count}");
            foreach (IGrouping<byte, WarpEventCall> group in calls.GroupBy(c => c.Mode).OrderBy(g => g.Key))
            {
                Console.WriteLine($"mode={group.Key} count={group.Count()} destinationMaps={string.Join(",", group.Select(c => $"{c.DestinationArea:D2}_{c.DestinationBlock:D2}").Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal))}");
                Console.WriteLine($"  nonzero: p0={CountNonzero(group, c => c.P0)}, p1={CountNonzero(group, c => c.P1)}, p2={CountNonzero(group, c => c.P2)}, p3={CountNonzero(group, c => c.P3)}, p4={CountNonzero(group, c => c.P4)}, p5={CountNonzero(group, c => c.P5)}, p6={CountNonzero(group, c => c.P6)}; beforeWarpSet={group.Count(c => c.BeforeWarpPositive)}");
            }

            Console.WriteLine("Calls (temporary analysis only; do not commit this output):");
            foreach (WarpEventCall call in calls)
                Console.WriteLine($"slot={call.Slot} mode={call.Mode} dest={call.DestinationArea:D2}_{call.DestinationBlock:D2} before={call.BeforeWarpPositive} p0={call.P0} p1={call.P1} p2={call.P2} p3={call.P3} p4={call.P4} p5={call.P5} p6={call.P6}");
        }

        public static void WriteArgumentAudit(IEnumerable<string> commonEmevdPaths, string outputPath)
        {
            string repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
            string output = Path.GetFullPath(outputPath);
            if (output.StartsWith(Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Observed Event 5700 values must be written outside the repository.");

            string[] paths = commonEmevdPaths.ToArray();
            var calls = paths.SelectMany(Read).ToArray();
            if (calls.Length == 0) throw new InvalidDataException("No event 5700 initializer calls were found.");
            var arguments = new[]
            {
                new AuditArgument("mode", "byte", "0", "GameDataWriter.AddWarpEvent mode", "0=action-driven, 1=vanilla door/PvP, 2=flag-driven, 3=fixed world gate", "high", c => c.Mode.ToString()),
                new AuditArgument("destinationArea", "byte", "1", "AddWarpEvent destination area", "DSR destination map area", "high", c => c.DestinationArea.ToString()),
                new AuditArgument("destinationBlock", "byte", "2", "AddWarpEvent destination block", "DSR destination map block", "high", c => c.DestinationBlock.ToString()),
                new AuditArgument("beforeWarpPositive", "byte boolean", "3", "p[6] > 0", "Whether the before-warp flag is set rather than cleared", "high", c => c.BeforeWarpPositive ? "1" : "0"),
                new AuditArgument("p0", "int32", "4", "GameDataWriter warpArgs[0]", "Mode 0/3 action target or cleanup object; mode 1 cleanup object; mode 2 trigger flag", "high", c => c.P0.ToString()),
                new AuditArgument("p1", "int32", "8", "GameDataWriter warpArgs[1]", "Mode 0/2 destination player entity; mode 1/3 map SFX cleanup entity", "high", c => c.P1.ToString()),
                new AuditArgument("p2", "int32", "12", "GameDataWriter warpArgs[2]", "Mode 0 trap flag; mode 1 Lordvessel gate flag; mode 3 traversal flag", "high", c => c.P2.ToString()),
                new AuditArgument("p3", "int32", "16", "GameDataWriter warpArgs[3]", "Mode 0 action region; mode 3 first action/paired-warp region", "high", c => c.P3.ToString()),
                new AuditArgument("p4", "int32", "20", "GameDataWriter warpArgs[4]", "Mode 0 exit gate flag; mode 3 second action/paired-warp region", "high", c => c.P4.ToString()),
                new AuditArgument("p5", "int32", "24", "GameDataWriter warpArgs[5]", "Destination entry gate flag", "high", c => c.P5.ToString()),
                new AuditArgument("p6Magnitude", "int32", "28", "Math.Abs(p[6])", "Magnitude of before-warp flag; sign is transported separately at payload byte 3", "high", c => c.P6.ToString()),
            };
            var lines = new List<string>
            {
                "Event 5700 caller ABI audit; generated from real FogHost outputs and pinned FogMod AddWarpEvent source.",
                $"Calls={calls.Length}; profiles={paths.Length}; modes={string.Join(",", calls.Select(call => call.Mode).Distinct().OrderBy(mode => mode))}.",
                "Values are unique wire values observed across every supplied profile. Detailed game-derived values remain outside the repository.",
                "",
                "argument | wire type | payload offset | FogMod source | observed values | meaning | confidence",
            };
            foreach (AuditArgument argument in arguments)
            {
                string values = string.Join(",", calls.Select(argument.Select).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));
                lines.Add($"{argument.Name} | {argument.Type} | {argument.Offset} | {argument.Source} | {values} | {argument.Meaning} | {argument.Confidence}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllLines(output, lines);
            Console.WriteLine($"Wrote observed-value audit for {calls.Length} calls and {arguments.Length} ABI fields outside the repository: {output}");
        }

        private static int ReadInt32(byte[] bytes, int offset) => BitConverter.ToInt32(bytes, offset);

        private static int CountNonzero(IEnumerable<WarpEventCall> calls, Func<WarpEventCall, int> select) =>
            calls.Count(call => select(call) != 0);

        private static string FindRepositoryRoot(string start)
        {
            var current = new DirectoryInfo(start);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }

        private sealed record AuditArgument(string Name, string Type, string Offset, string Source, string Meaning, string Confidence, Func<WarpEventCall, string> Select);

        internal sealed record WarpEventCall(
            int InstructionIndex,
            int Slot,
            byte Mode,
            byte DestinationArea,
            byte DestinationBlock,
            bool BeforeWarpPositive,
            int P0,
            int P1,
            int P2,
            int P3,
            int P4,
            int P5,
            int P6);
    }
}
