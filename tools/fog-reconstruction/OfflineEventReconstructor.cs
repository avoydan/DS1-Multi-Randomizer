using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FogHost;
using SoulsFormats;

namespace FogReconstruction
{
    /// <summary>Builds project-authored EMEVD files from a verified clean DSR snapshot.</summary>
    internal static class OfflineEventReconstructor
    {
        private const string EventPrefix = "dist/DS1R/event/";

        // Binary source hashes pin the clean DSR event archives used to author these transforms.
        private static readonly IReadOnlyDictionary<string, string> SourceHashes =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["common.emevd.dcx"] = "84758a6888d5ece12abed648b7390ff5427f9766ae66337c52df81d43347faa0",
                ["m10_00_00_00.emevd.dcx"] = "119df77057b9d56a3f13bf03bd7e4fd2c4bbf990e236573561da82d3d2b615e3",
                ["m10_01_00_00.emevd.dcx"] = "29472f6360e1c8be472b92dd2c5e553d8cb1dcf3eaf3ef17dc29df5517d55f6f",
                ["m10_02_00_00.emevd.dcx"] = "b519c70b1d3bdbaf7deb0472b47355b827cbae5f61b91c9edd915e41cec0d346",
                ["m11_00_00_00.emevd.dcx"] = "f0edaad3713f2548b43928ece91451eb8c00415eccf56cd0292e42e9c49a76a0",
                ["m12_00_00_00.emevd.dcx"] = "2e13def5aa25a2d4b500906c2bd08d329da58a3aa9521325dbd38943adb00b6e",
                ["m12_01_00_00.emevd.dcx"] = "4ec4aff38d973916bc39da2427b0596f697b629fef6efde09ad0229094670616",
                ["m13_00_00_00.emevd.dcx"] = "691516e02235c8770629d032b6512e2ce1e14844487b198a1ee88756207431ba",
                ["m13_01_00_00.emevd.dcx"] = "4c51cd8828862bb16cd2e3544a03f0885f77411908201948407691eca96f9925",
                ["m13_02_00_00.emevd.dcx"] = "6fcc2aaacc89a019ac0e8e7e9d2b876ce1b9595f154fbab09754f80ffc44dd26",
                ["m14_00_00_00.emevd.dcx"] = "9239b8aa5e25b626aab9b086491da034f9bb9b8f172ddce02ed76426581b517c",
                ["m14_01_00_00.emevd.dcx"] = "dc0e348fc53082dd8dfbefa6573c774d0157c2880afaa640daa4e59a819b1deb",
                ["m15_00_00_00.emevd.dcx"] = "a3ef09ffdfca8bb98af8ed0804888f43c7ece94f80f52c9c917796466cd2ee08",
                ["m15_01_00_00.emevd.dcx"] = "ee6349fcf667b64a1e038db2f67c886548e3aabc768599ff86375a176b3b04f7",
                ["m16_00_00_00.emevd.dcx"] = "42c4244f869332b5b483412f833dccf0c2b073126c92d17313a8e80837bdafe0",
                ["m17_00_00_00.emevd.dcx"] = "4d00d0abd8d6e3334e56a2c3c2da539df8b026781b87239da322ed7f96349066",
                ["m18_00_00_00.emevd.dcx"] = "e49eb2fdfb9748f804d1d3ddc3acfe2310465ab5419487cb3c49260595b11256",
                ["m18_01_00_00.emevd.dcx"] = "9ca12188f5b6d5bd38fbfd116a4d9b0f0ca8063fcc8d042252b403d22e319665",
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
                throw new IOException("Offline event output already exists; select a new output path: " + finalRoot);

            PristineFogInputs.Verify(sourceRoot);
#if FOG_RECONSTRUCTION_DEV
            if (oracleRoot != null) DataRoot.Verify(oracleRoot);
#endif
            List<DataRoot.Entry> eventEntries = DataRoot.Manifest
                .Where(entry => entry.Path.StartsWith(EventPrefix, StringComparison.Ordinal))
                .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                .ToList();
            if (eventEntries.Count != 18 || SourceHashes.Count != 18
                || !eventEntries.Select(entry => Path.GetFileName(entry.Path)).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(SourceHashes.Keys))
                throw new InvalidDataException("The pinned event reconstruction inventory must contain the same 18 archives as the source hash manifest.");

            string stagingRoot = finalRoot + ".staging-" + Guid.NewGuid().ToString("N");
            string stagingEvents = Path.Combine(stagingRoot, "event");
            Directory.CreateDirectory(stagingEvents);
            int removedEvents = 0;
            foreach (DataRoot.Entry entry in eventEntries)
            {
                string name = Path.GetFileName(entry.Path);
                string sourcePath = Path.Combine(sourceRoot, "event", name);
                byte[] sourceBytes = File.ReadAllBytes(sourcePath);
                string actualSourceHash = DataRoot.Hash(sourcePath, text: false);
                if (!string.Equals(actualSourceHash, SourceHashes[name], StringComparison.Ordinal))
                    throw new InvalidDataException($"Pristine event input hash mismatch for {name}: expected {SourceHashes[name]}, got {actualSourceHash}.");

                DCX.Type compression = DCX.Type.None;
                byte[] uncompressed = DCX.Is(sourceBytes) ? DCX.Decompress(sourceBytes, out compression) : sourceBytes;
                EMEVD archive = EMEVD.Read(uncompressed);
                ApplyProjectOwnedTransforms(name, archive);
                removedEvents += EventRemovalPlan.Apply(name, archive);

                byte[] reconstructed = archive.Write();
                byte[] outputBytes = compression == DCX.Type.None ? reconstructed : DCX.Compress(reconstructed, compression);
                string outputPath = Path.Combine(stagingEvents, name);
                File.WriteAllBytes(outputPath, outputBytes);
#if FOG_RECONSTRUCTION_DEV
                if (oracleRoot != null)
                    VerifyArchiveParity(name, outputPath, Path.Combine(oracleRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
                else
                    throw new InvalidOperationException("The development reconstruction command requires its comparison oracle.");
#else
                VerifyReadableArchive(outputPath);
#endif
            }

            if (removedEvents != EventRemovalPlan.TotalEventCount)
                throw new InvalidDataException($"Expected to remove {EventRemovalPlan.TotalEventCount} event IDs, removed {removedEvents}.");
            Directory.Move(stagingRoot, finalRoot);
#if FOG_RECONSTRUCTION_DEV
            if (oracleRoot != null)
                Console.WriteLine($"Offline EMEVD reconstruction passed semantic parity for {eventEntries.Count}/18 archives; removed {removedEvents}/96 declared events.");
            else
#endif
                Console.WriteLine($"Reconstructed and decoded {eventEntries.Count}/18 EMEVD archives; removed {removedEvents}/96 declared events.");
        }

        private static void ApplyProjectOwnedTransforms(string archiveName, EMEVD archive)
        {
            FogAddedEventBuilder.ReplaceReconstructedAddedEvents(archiveName, archive);
            if (string.Equals(archiveName, "common.emevd.dcx", StringComparison.Ordinal))
            {
                Upsert(archive, FogCommonEventBuilder.BuildEvent5690());
                Upsert(archive, FogCommonEventBuilder.BuildEvent8900());
                Upsert(archive, FogCommonEventBuilder.BuildEvent8901());
                Upsert(archive, FogCommonEventBuilder.BuildEvent8950());
                Upsert(archive, Event5700Builder.Build());
                FogModifiedEventBuilder.ReconstructKnownCommonEvents(archive);
            }

            FogModifiedEventBuilder.ReconstructBossEventsForOfflineRoot(archiveName, archive);
            switch (archiveName)
            {
                case "m10_00_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM10Events(archive); break;
                case "m10_01_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(archive); break;
                case "m10_02_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents(archive); break;
                case "m11_00_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM11Events(archive); break;
                case "m12_00_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM12Events(archive); break;
                case "m12_01_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(archive); break;
                case "m13_00_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM13Events(archive); break;
                case "m13_01_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM13DlcEvents(archive); break;
                case "m14_00_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM14Events(archive); break;
                case "m14_01_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM14DlcEvents(archive); break;
                case "m15_00_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM15Events(archive); break;
                case "m15_01_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive); break;
                case "m16_00_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM16Events(archive); break;
                case "m17_00_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM17Events(archive); break;
                case "m18_00_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM18Events(archive); break;
                case "m18_01_00_00.emevd.dcx": FogModifiedEventBuilder.ReconstructKnownM18DlcEvents(archive); break;
            }
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, archive);
            if (string.Equals(archiveName, "m14_01_00_00.emevd.dcx", StringComparison.Ordinal))
                FogVanillaEventBuilder.ReplaceBedOfChaosHelpers(archive, archive);
        }

        private static void Upsert(EMEVD archive, EMEVD.Event replacement)
        {
            int[] matches = Enumerable.Range(0, archive.Events.Count)
                .Where(index => archive.Events[index].ID == replacement.ID)
                .ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException($"Expected at most one source event {replacement.ID}; found {matches.Length}.");
            if (matches.Length == 0) archive.Events.Add(replacement);
            else archive.Events[matches[0]] = replacement;
        }

#if FOG_RECONSTRUCTION_DEV
        private static void VerifyArchiveParity(string name, string actualPath, string expectedPath)
        {
            EMEVD actual = ReadEmevd(actualPath);
            EMEVD expected = ReadEmevd(expectedPath);
            SemanticComparison comparison = SemanticComparer.Compare(actual, expected);
            if (!comparison.Equal)
            {
                long[] actualIds = actual.Events.Select(evt => evt.ID).OrderBy(id => id).ToArray();
                long[] expectedIds = expected.Events.Select(evt => evt.ID).OrderBy(id => id).ToArray();
                string actualOnly = string.Join(",", actualIds.Except(expectedIds));
                string expectedOnly = string.Join(",", expectedIds.Except(actualIds));
                throw new InvalidDataException($"Reconstructed event/{name} differs from the development oracle at {comparison.FirstDifference}; actual-only IDs=[{actualOnly}], expected-only IDs=[{expectedOnly}]; actual={comparison.LeftFingerprint}, expected={comparison.RightFingerprint}.");
            }
            Console.WriteLine($"PASS semantic parity event/{name}");
        }

        private static EMEVD ReadEmevd(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            return EMEVD.Read(bytes);
        }
#else
        private static void VerifyReadableArchive(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            _ = EMEVD.Read(bytes);
        }
#endif

        private static void EnsureDistinctRoots(string source, string oracle, string output)
        {
            string[] roots = oracle == null ? new[] { source, output } : new[] { source, oracle, output };
            for (int i = 0; i < roots.Length; i++)
            {
                for (int j = i + 1; j < roots.Length; j++)
                    if (IsSameOrChild(roots[i], roots[j]) || IsSameOrChild(roots[j], roots[i]))
                        throw new InvalidOperationException("Pristine source, reference oracle and output paths must be separate trees.");
            }
        }

        private static void EnsureOutsideRepository(string path, string repositoryRoot)
        {
            if (IsSameOrChild(repositoryRoot, path))
                throw new InvalidOperationException("Reconstructed EMEVD output must be outside the repository.");
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
