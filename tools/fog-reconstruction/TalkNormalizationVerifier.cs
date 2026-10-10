using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulsFormats;

namespace FogReconstruction
{
    internal static class TalkNormalizationVerifier
    {
        internal static void Verify(string pristineRoot, string referenceRoot)
        {
            string sourceDirectory = Path.Combine(pristineRoot, "script", "talk");
            string targetDirectory = Path.Combine(referenceRoot, "dist", "DS1R", "script", "talk");
            if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
            if (!Directory.Exists(targetDirectory)) throw new DirectoryNotFoundException(targetDirectory);

            string[] targets = Directory.GetFiles(targetDirectory, "*.talkesdbnd.dcx").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
            int scripts = 0;
            int changedScriptsBefore = 0;
            int normalizedExpressions = 0;
            int semanticMismatchCount = 0;
            int rawMismatchCount = 0;
            var examples = new List<string>();
            foreach (string targetPath in targets)
            {
                string sourcePath = Path.Combine(sourceDirectory, Path.GetFileName(targetPath));
                if (!File.Exists(sourcePath)) throw new FileNotFoundException("Pristine Talk archive is missing.", sourcePath);
                Dictionary<string, byte[]> sourceEntries = ReadEsdEntries(File.ReadAllBytes(sourcePath));
                Dictionary<string, byte[]> targetEntries = ReadEsdEntries(File.ReadAllBytes(targetPath));
                foreach (string name in sourceEntries.Keys.Union(targetEntries.Keys).OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
                {
                    if (!sourceEntries.TryGetValue(name, out byte[] sourceBytes) || !targetEntries.TryGetValue(name, out byte[] targetBytes))
                    {
                        semanticMismatchCount++;
                        if (examples.Count < 20) examples.Add($"{Path.GetFileName(targetPath)}:{name}: ESD entry membership differs");
                        continue;
                    }

                    ESD source = ESD.Read(Decompress(sourceBytes));
                    ESD target = ESD.Read(Decompress(targetBytes));
                    string targetFingerprint = CanonicalSemantics.EsdFingerprint(CanonicalSemantics.Esd(target, name, normalizeBytecode: false));
                    string originalFingerprint = CanonicalSemantics.EsdFingerprint(CanonicalSemantics.Esd(source, name, normalizeBytecode: false));
                    bool changedByFog = originalFingerprint != targetFingerprint;
                    if (changedByFog)
                    {
                        changedScriptsBefore++;
                        normalizedExpressions += TalkBytecodeNormalizer.Normalize(source);
                    }
                    string normalizedFingerprint = CanonicalSemantics.EsdFingerprint(CanonicalSemantics.Esd(source, name, normalizeBytecode: false));
                    scripts++;
                    if (normalizedFingerprint != targetFingerprint) rawMismatchCount++;
                    SemanticComparison semantic = SemanticComparer.Compare(source, target);
                    if (!semantic.Equal)
                    {
                        semanticMismatchCount++;
                        if (examples.Count < 20)
                        {
                            string difference = semantic.FirstDifference ?? "semantic fingerprint differs";
                            examples.Add($"{Path.GetFileName(targetPath)}:{name}: {difference}");
                        }
                    }
                }
            }

            Console.WriteLine($"Talk normalization: {scripts} ESD entries across {targets.Length} archives; {changedScriptsBefore} entries differ before normalization; {normalizedExpressions} expressions/compatibility edits changed; {scripts - semanticMismatchCount}/{scripts} semantic parity; {scripts - rawMismatchCount}/{scripts} raw-model parity.");
            foreach (string example in examples) Console.WriteLine($"Mismatch: {example}");
            if (semanticMismatchCount != 0) throw new InvalidDataException($"Talk semantic canonicalization did not match the reference for {semanticMismatchCount} ESD entries.");
        }

        private static Dictionary<string, byte[]> ReadEsdEntries(byte[] bytes)
        {
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            IBinder binder;
            if (BND4.Is(bytes)) binder = BND4.Read(bytes);
            else if (BND3.Is(bytes)) binder = BND3.Read(bytes);
            else throw new InvalidDataException("Talk archive is neither BND3 nor BND4 after DCX decompression.");

            var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (BinderFile entry in binder.Files)
            {
                string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (extension != ".esd" && extension != ".dcx") continue;
                if (!entries.TryAdd(entry.Name, entry.Bytes)) throw new InvalidDataException($"Duplicate Talk entry: {entry.Name}");
            }
            if (entries.Count == 0) throw new InvalidDataException("Talk archive has no readable ESD entries.");
            return entries;
        }

        private static byte[] Decompress(byte[] bytes) => DCX.Is(bytes) ? DCX.Decompress(bytes) : bytes;
    }
}
