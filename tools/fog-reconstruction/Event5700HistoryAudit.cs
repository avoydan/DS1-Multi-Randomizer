using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>
    /// Redacted development-only semantic history comparison for event 5700.
    /// It emits operation/parameter counts and equality results, never instruction operands.
    /// </summary>
    internal static class Event5700HistoryAudit
    {
        public static void Write(IEnumerable<string> labeledFiles, string outputPath)
        {
            string repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
            string output = Path.GetFullPath(outputPath);
            string repository = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (output.StartsWith(repository, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Historical Event 5700 comparison must be written outside the repository.");

            var versions = new List<(string Label, EMEVD.Event Event)>();
            foreach (string input in labeledFiles)
            {
                int equals = input.IndexOf('=');
                if (equals <= 0 || equals == input.Length - 1)
                    throw new ArgumentException("Historical inputs must be labeled as <label>=<common.emevd.dcx-path>.");
                string label = input.Substring(0, equals);
                string path = input.Substring(equals + 1);
                byte[] bytes = File.ReadAllBytes(path);
                if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
                EMEVD file = EMEVD.Read(bytes);
                EMEVD.Event evt = file.Events.SingleOrDefault(value => value.ID == 5700);
                if (evt != null) versions.Add((label, evt));
            }
            if (versions.Count < 2) throw new InvalidDataException("At least two historical event 5700 bodies are required.");

            var lines = new List<string>
            {
                "Redacted historical Event 5700 semantic comparison.",
                "Labels identify FogMod commits. No instruction arguments or binary body are included.",
                "",
            };
            foreach ((string label, EMEVD.Event evt) in versions)
            {
                lines.Add($"{label}: present; instructions={evt.Instructions.Count}; parameters={evt.Parameters.Count}; rest={evt.RestBehavior}; body-sha256={Fingerprint(evt, includeArguments: true)}");
            }
            lines.Add("");
            lines.Add("Comparison is against the prior version in the supplied chronological input order:");
            for (int index = 1; index < versions.Count; index++)
            {
                (string previousLabel, EMEVD.Event previous) = versions[index - 1];
                (string currentLabel, EMEVD.Event current) = versions[index];
                lines.Add($"{previousLabel} -> {currentLabel}: same-operation-order={Fingerprint(previous, includeArguments: false) == Fingerprint(current, includeArguments: false)}; same-operands-and-parameters={Fingerprint(previous, includeArguments: true) == Fingerprint(current, includeArguments: true)}; changed-operation-slots={ChangedOperationSlots(previous, current)}; changed-parameter-count={Math.Abs(previous.Parameters.Count - current.Parameters.Count)}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllLines(output, lines, new UTF8Encoding(false));
            Console.WriteLine($"Wrote redacted history comparison for {versions.Count} Event 5700 versions outside the repository: {output}");
        }

        private static string Fingerprint(EMEVD.Event evt, bool includeArguments)
        {
            var text = new StringBuilder();
            text.Append(evt.RestBehavior).Append('|');
            foreach (EMEVD.Instruction instruction in evt.Instructions)
            {
                text.Append(instruction.Bank).Append(':').Append(instruction.ID).Append(':')
                    .Append(instruction.Layer?.ToString() ?? "-");
                if (includeArguments) text.Append(':').Append(Convert.ToHexString(instruction.ArgData ?? Array.Empty<byte>()));
                text.Append(';');
            }
            if (includeArguments)
            {
                foreach (EMEVD.Parameter parameter in evt.Parameters.OrderBy(value => value.InstructionIndex).ThenBy(value => value.TargetStartByte))
                    text.Append(parameter.InstructionIndex).Append(',').Append(parameter.TargetStartByte).Append(',')
                        .Append(parameter.SourceStartByte).Append(',').Append(parameter.ByteCount).Append(',').Append(parameter.UnkID).Append(';');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
        }

        private static int ChangedOperationSlots(EMEVD.Event left, EMEVD.Event right)
        {
            int changed = Math.Abs(left.Instructions.Count - right.Instructions.Count);
            for (int index = 0; index < Math.Min(left.Instructions.Count, right.Instructions.Count); index++)
            {
                EMEVD.Instruction a = left.Instructions[index];
                EMEVD.Instruction b = right.Instructions[index];
                if (a.Bank != b.Bank || a.ID != b.ID || a.Layer != b.Layer) changed++;
            }
            return changed;
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
