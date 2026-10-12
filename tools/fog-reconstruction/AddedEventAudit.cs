using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>
    /// Development-only inventory of added/removed Fog events and their direct initializer callers.
    /// It intentionally omits instruction operands and writes only outside the repository.
    /// </summary>
    internal static class AddedEventAudit
    {
        public static void Write(string pristineRoot, string referenceRoot, string outputPath)
        {
            string repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
            string output = Path.GetFullPath(outputPath);
            string repository = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (output.StartsWith(repository, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Added-event analysis must be written outside the repository.");

            string pristineEvents = Path.Combine(Path.GetFullPath(pristineRoot), "event");
            string referenceEvents = Path.Combine(Path.GetFullPath(referenceRoot), "dist", "DS1R", "event");
            if (!Directory.Exists(pristineEvents)) throw new DirectoryNotFoundException(pristineEvents);
            if (!Directory.Exists(referenceEvents)) throw new DirectoryNotFoundException(referenceEvents);

            var pristineByFile = Directory.GetFiles(pristineEvents, "*.emevd.dcx")
                .ToDictionary(Path.GetFileName, path => Read(path), StringComparer.Ordinal);
            var referenceByFile = Directory.GetFiles(referenceEvents, "*.emevd.dcx")
                .ToDictionary(Path.GetFileName, path => Read(path), StringComparer.Ordinal);

            var sourceCallers = IndexEventCallers(pristineByFile);
            var referenceCallers = IndexEventCallers(referenceByFile);
            var lines = new List<string>
            {
                "Added/removed Fog EMEVD event inventory and direct initializer references.",
                "Temporary analysis only; instruction operands and game-derived event bodies are intentionally omitted.",
                "Callers are scanned across every archive for InitializeEvent (2000[00]); local and cross-archive sites are listed separately.",
                "Caller format: archive:event-id@instruction-index. 'none' means no direct InitializeEvent caller was found.",
                "",
            };

            foreach ((string fileName, EMEVD target) in referenceByFile.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!pristineByFile.TryGetValue(fileName, out EMEVD source))
                    throw new FileNotFoundException("Pristine EMEVD archive is missing.", Path.Combine(pristineEvents, fileName));

                HashSet<long> sourceIds = source.Events.Select(evt => evt.ID).ToHashSet();
                HashSet<long> targetIds = target.Events.Select(evt => evt.ID).ToHashSet();
                foreach (EMEVD.Event evt in target.Events.Where(evt => !sourceIds.Contains(evt.ID)).OrderBy(evt => evt.ID))
                {
                    string localSites = FormatCallers(referenceCallers, fileName, evt.ID, includeMatchingArchive: true);
                    string externalSites = FormatCallers(referenceCallers, fileName, evt.ID, includeMatchingArchive: false);
                    lines.Add($"added {fileName} {evt.ID}; {evt.Instructions.Count} instructions; {evt.Parameters.Count} parameters; {evt.RestBehavior}; local-callers={localSites}; cross-archive-callers={externalSites}");
                }

                foreach (long eventId in sourceIds.Except(targetIds).OrderBy(value => value))
                {
                    string beforeLocal = FormatCallers(sourceCallers, fileName, eventId, includeMatchingArchive: true);
                    string beforeExternal = FormatCallers(sourceCallers, fileName, eventId, includeMatchingArchive: false);
                    string afterLocal = FormatCallers(referenceCallers, fileName, eventId, includeMatchingArchive: true);
                    string afterExternal = FormatCallers(referenceCallers, fileName, eventId, includeMatchingArchive: false);
                    lines.Add($"removed {fileName} {eventId}; vanilla-local-callers={beforeLocal}; vanilla-cross-archive-callers={beforeExternal}; reference-local-callers={afterLocal}; reference-cross-archive-callers={afterExternal}");
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllLines(output, lines, new UTF8Encoding(false));
            Console.WriteLine($"Wrote redacted added/removed event caller inventory outside the repository: {output}");
        }

        private static Dictionary<long, List<string>> IndexEventCallers(IReadOnlyDictionary<string, EMEVD> archives)
        {
            var callers = new Dictionary<long, List<string>>();
            foreach ((string fileName, EMEVD file) in archives.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                foreach (EMEVD.Event caller in file.Events)
                {
                    for (int instructionIndex = 0; instructionIndex < caller.Instructions.Count; instructionIndex++)
                    {
                        EMEVD.Instruction instruction = caller.Instructions[instructionIndex];
                        if (instruction.Bank != 2000 || instruction.ID != 0 || instruction.ArgData.Length < 8) continue;
                        long target = BitConverter.ToUInt32(instruction.ArgData, 4);
                        if (!callers.TryGetValue(target, out List<string> sites)) callers[target] = sites = new List<string>();
                        sites.Add($"{fileName}:{caller.ID}@{instructionIndex}");
                    }
                }
            }
            return callers;
        }

        private static string FormatCallers(Dictionary<long, List<string>> callers, string fileName, long eventId, bool includeMatchingArchive) =>
            callers.TryGetValue(eventId, out List<string> sites)
                ? string.Join(",", sites.Where(site => site.StartsWith(fileName + ":", StringComparison.Ordinal) == includeMatchingArchive)
                    .OrderBy(value => value, StringComparer.Ordinal).DefaultIfEmpty("none"))
                : "none";

        private static EMEVD Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            return EMEVD.Read(bytes);
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
