using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FogHost;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>Rewrites pinned Talk archives by applying project-owned ESD normalization to clean DSR scripts.</summary>
    internal static class OfflineTalkReconstructor
    {
        private const string TalkPrefix = "dist/DS1R/script/talk/";

        private static readonly IReadOnlyDictionary<string, string> SourceHashes =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["m10_00_00_00.talkesdbnd.dcx"] = "afef6c73272edae08a98670d469b68266d656d0d8fae0ee71f248f9f7a08b591",
                ["m10_01_00_00.talkesdbnd.dcx"] = "db97dd40e40f152c418441abd0dfabfcf5312c8a85539ae89da939bf8a2da6eb",
                ["m10_02_00_00.talkesdbnd.dcx"] = "68936061967d55942f5e1a65defb2590d558bc22779ddefd12f88d2d508319bb",
                ["m11_00_00_00.talkesdbnd.dcx"] = "372c1dfa75d465e48a24ce0fbdbfdd53214caa9b010ab0855910d077c2b9a282",
                ["m12_00_00_00.talkesdbnd.dcx"] = "2265bdb4bdc54edf45e83637c5133f45ac04ae92d0d60dd64d76e9cd30edbe76",
                ["m12_01_00_00.talkesdbnd.dcx"] = "b1387e4d88bf89f5d25c45f3341e06c88c066a875f2da700883246607e2f914a",
                ["m13_00_00_00.talkesdbnd.dcx"] = "ffa020e8a0e8ebf27976e8653d40ee848446557e1f02961bff6285a928fd65de",
                ["m13_01_00_00.talkesdbnd.dcx"] = "2a200cf6353de517bf9e744fdbedd16a6868427d2c47cb141668f9f860bda4a2",
                ["m13_02_00_00.talkesdbnd.dcx"] = "1f9bbcba5bd6862c2acec5d1fd9edd91a245fbb0d4027f022d9d3c5d351dac77",
                ["m14_00_00_00.talkesdbnd.dcx"] = "2a32513f22db2de1f129261efac2fe8f3a1bb15b892051152608be5fb1d0048f",
                ["m14_01_00_00.talkesdbnd.dcx"] = "4cbcf60927e548aa7b545229a1b193dcc90cc1d2f6d96982c91eb6679138938c",
                ["m15_00_00_00.talkesdbnd.dcx"] = "051041daea0709fa1f1201f099de030392239d8192e53851f6206f4b82b8bad5",
                ["m15_01_00_00.talkesdbnd.dcx"] = "40c2baac4981aef295ad01152002fa720213649cdeaa339aec91cfe00720ff61",
                ["m16_00_00_00.talkesdbnd.dcx"] = "df53540ee729b92849012366a15ad8969b8e81e8758b75a5ebbd38e9528bca29",
                ["m17_00_00_00.talkesdbnd.dcx"] = "c7965e05ae1af091310ac460e0350e82618bb018233c887eadbac34472a31f72",
                ["m18_00_00_00.talkesdbnd.dcx"] = "90bcecd264afd83a2aabc11d932ef295c0ed71f6a36703f8aae1c8afcee7d6d1",
                ["m18_01_00_00.talkesdbnd.dcx"] = "500a6719a5a60df07397c8e955996716c2a0d172a174640f06b2db0b0e806094",
            };

        internal static void Build(string pristineRoot, string referenceRoot, string outputRoot)
        {
            BuildCore(pristineRoot, referenceRoot, outputRoot);
        }

        internal static void BuildFromPristine(string pristineRoot, string outputRoot)
        {
            BuildCore(pristineRoot, null, outputRoot);
        }

        private static void BuildCore(string pristineRoot, string referenceRoot, string outputRoot)
        {
            string sourceRoot = Path.GetFullPath(pristineRoot);
            string oracleRoot = referenceRoot == null ? null : Path.GetFullPath(referenceRoot);
            string finalRoot = Path.GetFullPath(outputRoot);
#if FOG_RECONSTRUCTION_DEV
            string repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
            EnsureOutsideRepository(finalRoot, repositoryRoot);
#endif
            EnsureDistinctRoots(sourceRoot, oracleRoot, finalRoot);
            if (Directory.Exists(finalRoot) || File.Exists(finalRoot))
                throw new IOException("Offline Talk output already exists; select a new output path: " + finalRoot);

            PristineFogInputs.Verify(sourceRoot);
#if FOG_RECONSTRUCTION_DEV
            if (oracleRoot != null) DataRoot.Verify(oracleRoot);
#endif
            List<DataRoot.Entry> entries = DataRoot.Manifest
                .Where(entry => entry.Path.StartsWith(TalkPrefix, StringComparison.Ordinal))
                .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                .ToList();
            if (entries.Count != 17 || SourceHashes.Count != 17
                || !entries.Select(entry => Path.GetFileName(entry.Path)).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(SourceHashes.Keys))
                throw new InvalidDataException("The pinned Talk reconstruction inventory must contain the same 17 archives as the source hash manifest.");

            string stagingRoot = finalRoot + ".staging-" + Guid.NewGuid().ToString("N");
            string stagingTalk = Path.Combine(stagingRoot, "script", "talk");
            Directory.CreateDirectory(stagingTalk);
            int scripts = 0;
            int normalizedScripts = 0;
            int normalizedExpressions = 0;
            var appliedPlan = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataRoot.Entry entry in entries)
            {
                string name = Path.GetFileName(entry.Path);
                string sourcePath = Path.Combine(sourceRoot, "script", "talk", name);
                byte[] sourceBytes = File.ReadAllBytes(sourcePath);
                string actualSourceHash = DataRoot.Hash(sourcePath, text: false);
                if (!string.Equals(actualSourceHash, SourceHashes[name], StringComparison.Ordinal))
                    throw new InvalidDataException($"Pristine Talk input hash mismatch for {name}: expected {SourceHashes[name]}, got {actualSourceHash}.");

                DCX.Type outerCompression = DCX.Type.None;
                byte[] binderBytes = DCX.Is(sourceBytes) ? DCX.Decompress(sourceBytes, out outerCompression) : sourceBytes;
                IBinder sourceBinder = ReadBinder(binderBytes);
#if FOG_RECONSTRUCTION_DEV
                IBinder oracleBinder = null;
                Dictionary<string, ESD> oracleScripts = null;
                if (oracleRoot != null)
                {
                    oracleBinder = ReadBinder(ReadOuter(Path.Combine(oracleRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar)), out _));
                    RequireSameContainerMetadata(name, sourceBinder, oracleBinder, "source", "oracle");
                    oracleScripts = ReadEsdScripts(oracleBinder);
                }
#endif
                var presentScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (BinderFile binderFile in sourceBinder.Files)
                {
                    if (!IsEsdEntry(binderFile.Name)) continue;
                    if (!presentScripts.Add(binderFile.Name)) throw new InvalidDataException($"Duplicate Talk script entry {binderFile.Name} in {name}.");
#if FOG_RECONSTRUCTION_DEV
                    if (oracleScripts != null && !oracleScripts.TryGetValue(binderFile.Name, out _))
                        throw new InvalidDataException($"Oracle is missing Talk script {name}:{binderFile.Name}.");
#endif

                    DCX.Type innerCompression = DCX.Type.None;
                    byte[] esdBytes = DCX.Is(binderFile.Bytes) ? DCX.Decompress(binderFile.Bytes, out innerCompression) : binderFile.Bytes;
                    ESD script = ESD.Read(esdBytes);
                    if (TalkNormalizationPlan.ShouldNormalize(name, binderFile.Name))
                    {
                        normalizedScripts++;
                        appliedPlan.Add(name + "|" + Path.GetFileName(binderFile.Name.Replace('\\', '/')));
                        normalizedExpressions += TalkBytecodeNormalizer.Normalize(script);
                    }
#if FOG_RECONSTRUCTION_DEV
                    if (oracleScripts != null)
                    {
                        SemanticComparison comparison = SemanticComparer.Compare(script, oracleScripts[binderFile.Name]);
                        if (!comparison.Equal)
                            throw new InvalidDataException($"Reconstructed Talk script {name}:{binderFile.Name} differs from the development oracle at {comparison.FirstDifference}.");
                    }
#endif
                    if (TalkNormalizationPlan.ShouldNormalize(name, binderFile.Name))
                    {
                        byte[] serialized = script.Write();
                        binderFile.Bytes = innerCompression == DCX.Type.None ? serialized : DCX.Compress(serialized, innerCompression);
                    }
                    scripts++;
                }
#if FOG_RECONSTRUCTION_DEV
                if (oracleScripts != null && presentScripts.Count != oracleScripts.Count)
                    throw new InvalidDataException($"Talk script entry membership differs in {name}: source={presentScripts.Count}, oracle={oracleScripts.Count}.");
#endif

                byte[] serializedBinder = WriteBinder(sourceBinder);
                byte[] outputBytes = outerCompression == DCX.Type.None ? serializedBinder : DCX.Compress(serializedBinder, outerCompression);
                string outputPath = Path.Combine(stagingTalk, name);
                File.WriteAllBytes(outputPath, outputBytes);
#if FOG_RECONSTRUCTION_DEV
                if (oracleBinder != null) VerifyOutput(name, outputPath, sourceBinder, oracleBinder);
                else
#endif
                    VerifyOutputFromSource(name, outputPath, sourceBinder);
            }

            if (scripts != 178 || normalizedScripts != TalkNormalizationPlan.EntryCount
                || appliedPlan.Count != TalkNormalizationPlan.EntryCount)
                throw new InvalidDataException($"Expected 178 ESD scripts and {TalkNormalizationPlan.EntryCount} planned normalizations; found scripts={scripts}, normalized={normalizedScripts}, matched-plan={appliedPlan.Count}.");
            Directory.Move(stagingRoot, finalRoot);
#if FOG_RECONSTRUCTION_DEV
            if (oracleRoot != null)
                Console.WriteLine($"Offline Talk reconstruction passed semantic parity for {entries.Count}/17 archives and {scripts}/178 ESD scripts; applied {normalizedScripts}/73 planned ESD normalizations.");
            else
#endif
                Console.WriteLine($"Reconstructed and decoded {entries.Count}/17 Talk archives and {scripts}/178 ESD scripts; applied {normalizedScripts}/73 planned normalizations.");
            Console.WriteLine($"Project-owned ESD normalization changed {normalizedExpressions} bytecode expressions/compatibility edits.");
        }

#if FOG_RECONSTRUCTION_DEV
        private static void VerifyOutput(string name, string outputPath, IBinder sourceBinder, IBinder oracleBinder)
        {
            byte[] outputBytes = ReadOuter(outputPath, out _);
            IBinder outputBinder = ReadBinder(outputBytes);
            RequireSameContainerMetadata(name, sourceBinder, outputBinder, "source", "output");
            RequireSameContainerMetadata(name, oracleBinder, outputBinder, "oracle", "output");
            Dictionary<string, ESD> outputScripts = ReadEsdScripts(outputBinder);
            Dictionary<string, ESD> oracleScripts = ReadEsdScripts(oracleBinder);
            foreach (string scriptName in oracleScripts.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (!outputScripts.TryGetValue(scriptName, out ESD output))
                    throw new InvalidDataException($"Serialized Talk archive {name} lost entry {scriptName}.");
                SemanticComparison comparison = SemanticComparer.Compare(output, oracleScripts[scriptName]);
                if (!comparison.Equal)
                    throw new InvalidDataException($"Serialized Talk script {name}:{scriptName} differs from the development oracle at {comparison.FirstDifference}.");
            }
            Console.WriteLine($"PASS Talk archive and script parity talk/{name} ({outputScripts.Count} ESD entries)");
        }
#endif

        private static void VerifyOutputFromSource(string name, string outputPath, IBinder sourceBinder)
        {
            byte[] outputBytes = ReadOuter(outputPath, out _);
            IBinder outputBinder = ReadBinder(outputBytes);
            RequireSameContainerMetadata(name, sourceBinder, outputBinder, "source", "output");
            Dictionary<string, ESD> sourceScripts = ReadEsdScripts(sourceBinder);
            Dictionary<string, ESD> outputScripts = ReadEsdScripts(outputBinder);
            if (!sourceScripts.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(outputScripts.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Reconstructed Talk archive {name} changed ESD entry membership.");
        }

        private static Dictionary<string, ESD> ReadEsdScripts(IBinder binder)
        {
            var scripts = new Dictionary<string, ESD>(StringComparer.OrdinalIgnoreCase);
            foreach (BinderFile entry in binder.Files)
            {
                if (!IsEsdEntry(entry.Name)) continue;
                byte[] bytes = DCX.Is(entry.Bytes) ? DCX.Decompress(entry.Bytes) : entry.Bytes;
                if (!scripts.TryAdd(entry.Name, ESD.Read(bytes)))
                    throw new InvalidDataException("Duplicate Talk script entry: " + entry.Name);
            }
            return scripts;
        }

        private static bool IsEsdEntry(string name)
        {
            string extension = Path.GetExtension(name).ToLowerInvariant();
            return extension == ".esd" || extension == ".dcx";
        }

        private static byte[] ReadOuter(string path, out DCX.Type compression)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) return DCX.Decompress(bytes, out compression);
            compression = DCX.Type.None;
            return bytes;
        }

        private static IBinder ReadBinder(byte[] bytes)
        {
            if (BND4.Is(bytes)) return BND4.Read(bytes);
            if (BND3.Is(bytes)) return BND3.Read(bytes);
            throw new InvalidDataException("Talk archive is neither BND3 nor BND4.");
        }

        private static byte[] WriteBinder(IBinder binder)
        {
            if (binder is BND4 bnd4) return bnd4.Write();
            if (binder is BND3 bnd3) return bnd3.Write();
            throw new InvalidDataException("Unsupported Talk binder type: " + binder.GetType().FullName);
        }

        private static void RequireSameContainerMetadata(string name, IBinder expected, IBinder actual,
            string expectedLabel, string actualLabel)
        {
            string expectedMetadata = ContainerMetadata(expected);
            string actualMetadata = ContainerMetadata(actual);
            if (!string.Equals(expectedMetadata, actualMetadata, StringComparison.Ordinal))
                throw new InvalidDataException($"Talk container metadata changed for {name} between {expectedLabel} and {actualLabel}.");
        }

        private static string ContainerMetadata(IBinder binder)
        {
            string container;
            if (binder is BND4 bnd4)
                container = string.Join("|", "BND4", bnd4.Format, bnd4.BigEndian, bnd4.BitBigEndian,
                    bnd4.Unicode, bnd4.Unk04, bnd4.Unk05, bnd4.Extended, bnd4.Version);
            else if (binder is BND3 bnd3)
                container = string.Join("|", "BND3", bnd3.Format, bnd3.BigEndian, bnd3.BitBigEndian,
                    bnd3.Unk18, bnd3.Version);
            else throw new InvalidDataException("Unsupported Talk binder type: " + binder.GetType().FullName);

            string[] files = binder.Files.Select(file => string.Join("|", file.ID, file.Name, file.Flags, file.CompressionType)).ToArray();
            return container + "\n" + string.Join("\n", files);
        }

        private static void EnsureDistinctRoots(string source, string oracle, string output)
        {
            string[] roots = oracle == null ? new[] { source, output } : new[] { source, oracle, output };
            for (int i = 0; i < roots.Length; i++)
                for (int j = i + 1; j < roots.Length; j++)
                    if (IsSameOrChild(roots[i], roots[j]) || IsSameOrChild(roots[j], roots[i]))
                        throw new InvalidOperationException("Pristine source, reference oracle and output paths must be separate trees.");
        }

        private static void EnsureOutsideRepository(string path, string repositoryRoot)
        {
            if (IsSameOrChild(repositoryRoot, path))
                throw new InvalidOperationException("Reconstructed Talk output must be outside the repository.");
        }

        private static bool IsSameOrChild(string parent, string candidate)
        {
            string normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedParent, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
                || normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
