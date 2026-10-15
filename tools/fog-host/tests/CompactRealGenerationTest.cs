using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using FogHost;
using FogMod;
using SoulsFormats;

namespace FogHost.Tests
{
    /// <summary>Focused real GameDataWriter checks for the Compact annotation candidate.</summary>
    public static class CompactRealGenerationTest
    {
        private const uint MatrixSeed = 3141592653;

        public static int RunSmokeFromEnvironment()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            string pristineRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineRoot, includeLegacyAnnotations: false);
            FogCompactCatalogV1 catalog = FogCompactCatalogBuilder.Build(facts);
            FogWarp seathPrisonWarp = catalog.Catalog.Warps.Single(warp => warp.Id == FogProjectAnnotationPolicy.SeathPrisonWarpRuntimeId);
            if (seathPrisonWarp.ASide.WarpFlag != FogAddedEventBuilder.SeathCrystalActivatedFlag
                || seathPrisonWarp.BSide.Area != new FogAreaId("dukes_prison"))
                throw new InvalidDataException("The Seath crystal route is not projected to the validated prison destination.");
            FogDsrItemLotAwardFact crestAward = facts.ItemLotAwards.Single(award => award.EventArchiveName == "common.emevd.dcx"
                && award.EventId == 960 && award.ItemLotId == 6190);
            FogDsrEnemyFact crestSource = facts.Maps.Values.SelectMany(map => map.EnemyDetails)
                .Single(enemy => enemy.EntityId == 6190);
            FogLotRowLocation crestLot = catalog.Catalog.LotRows.SingleOrDefault(row => row.Lot.Value == 6190);
            if (crestAward.CharacterEntityId != 6190 || crestAward.RequiredEventFlagId != 1322
                || crestSource.Name != "c2640_0000" || crestLot == null || crestLot.Area.Value != "parish_andre"
                || catalog.Catalog.LotRows.Any(row => row.Lot.Value == 4001))
                throw new InvalidDataException("Crest of Artorias must resolve only from the common character-death reward anchored to pristine Parish enemy 6190; the unreferenced 4001 row must stay excluded.");
            FogDsrItemLotAwardFact blighttownKeyAward = facts.ItemLotAwards.Single(award => award.EventArchiveName == "common.emevd.dcx"
                && award.EventId == 970 && award.ItemLotId == 2500);
            FogLotRowLocation blighttownKeyLot = catalog.Catalog.LotRows.SingleOrDefault(row => row.Lot.Value == 2500);
            if (blighttownKeyAward.RequiredEventFlagId != 2 || blighttownKeyLot == null
                || blighttownKeyLot.Area.Value != "depths_gaping")
                throw new InvalidDataException("Blighttown Key must resolve from common boss-reward event 970 flag 2 to the pristine Gaping Dragon completion event and its Compact Area.");
            AnnotationData.Annotations annotations = catalog.CreateFreshCandidateAnnotations();
            if (annotations.CompactLotLocations == null
                || !annotations.CompactLotLocations.TryGetValue(1100, out string sealArea) || sealArea != "newlondo"
                || annotations.CompactLotLocations.ContainsKey(4013))
                throw new InvalidDataException("Compact exact lot ownership must include the source-backed New Londo seal row 1100 and exclude the unreferenced Firelink row 4013.");
            string originalDirectory = Environment.CurrentDirectory;
            string scratchRoot = Path.Combine(Path.GetTempPath(), "ds1tr-compact-fog-realgen-" + Guid.NewGuid().ToString("N"));
            string staging = Path.Combine(scratchRoot, "staging");
            Directory.CreateDirectory(scratchRoot);
            try
            {
                CopyGameData(Path.Combine(dataRoot, "dist", "DS1R"), staging);
                var options = new JsonObject();
                foreach ((string key, _) in HostRequest.OptionFlags) options[key] = false;
                JsonObject requestJson = Request(dataRoot, staging, Path.Combine(scratchRoot, "foghost.log"), options,
                    seed: 3141592653, pristineRoot: pristineRoot, annotationFingerprint: catalog.Fingerprint);
                HostRequest request = HostRequest.Parse(requestJson.ToJsonString());

                Environment.CurrentDirectory = dataRoot;
                var runner = new HostRunner((_, _) => { }, catalog.CreateFreshCandidateAnnotations);
                TextWriter originalOutput = Console.Out;
                HostResult result;
                try
                {
                    Console.SetOut(TextWriter.Null);
                    runner.ValidateLayout(request);
                    result = runner.Run(request);
                    DecodeEveryWrittenFile(staging, result);
                }
                finally
                {
                    Console.SetOut(originalOutput);
                }
                Console.WriteLine($"PASS Compact real GameDataWriter generation: Fog-only, seed={request.Seed}, files={result.Files.Count}, scratch={scratchRoot}");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"FAIL Compact real GameDataWriter generation: {error}");
                Console.Error.WriteLine($"Scratch retained for inspection: {scratchRoot}");
                return 1;
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
            }
        }

        public static int RunMatrixFromEnvironment()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            string pristineRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineRoot, includeLegacyAnnotations: false);
            FogCompactCatalogV1 catalog = FogCompactCatalogBuilder.Build(facts);
            var cases = HostRequest.OptionFlags
                .Select(option => (Name: option.Key, Enabled: new[] { option.Key }, Seed: MatrixSeed))
                .Prepend((Name: "fog-only", Enabled: Array.Empty<string>(), Seed: MatrixSeed))
                .ToList();
            cases.Add(("high-risk-warp-hard-lord-start", new[] { "areaWarps", "glitchedLogic", "requireLordSouls", "randomStart" }, MatrixSeed));
            cases.Add(("all-fog-options", HostRequest.OptionFlags.Select(option => option.Key).ToArray(), MatrixSeed));
            cases.Add(("random-start-seed-2", new[] { "randomStart", "areaWarps", "requireLordSouls" }, 19088743));

            var deterministicOptions = new[] { "areaWarps", "glitchedLogic", "requireLordSouls", "randomStart" };
            IReadOnlyList<string> firstDeterministicOutput = null;
            string originalDirectory = Environment.CurrentDirectory;
            string scratchRoot = Path.Combine(Path.GetTempPath(), "ds1tr-compact-fog-matrix-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratchRoot);
            try
            {
                Environment.CurrentDirectory = dataRoot;
                foreach (var testCase in cases)
                {
                    string[] output = RunCase(dataRoot, scratchRoot, catalog, testCase.Name, testCase.Enabled, testCase.Seed);
                    if (testCase.Name == "high-risk-warp-hard-lord-start") firstDeterministicOutput = output;
                    Console.WriteLine($"PASS Compact real generation: {testCase.Name}, seed={testCase.Seed}, files={output.Length}");
                }

                string[] repeated = RunCase(dataRoot, scratchRoot, catalog, "determinism-repeat", deterministicOptions, MatrixSeed);
                if (firstDeterministicOutput == null || !firstDeterministicOutput.SequenceEqual(repeated, StringComparer.Ordinal))
                    throw new InvalidDataException("Compact real generation output hashes changed for a repeated seed/options pair.");
                Console.WriteLine($"PASS Compact real generation determinism: seed={MatrixSeed}, outputs={repeated.Length}");
                Console.WriteLine($"PASS Compact real GameDataWriter matrix: {cases.Count + 1} runs; scratch={scratchRoot}");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"FAIL Compact real GameDataWriter matrix: {error}");
                Console.Error.WriteLine($"Scratch retained for inspection: {scratchRoot}");
                return 1;
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
            }
        }

        private static string[] RunCase(
            string dataRoot,
            string scratchRoot,
            FogCompactCatalogV1 catalog,
            string name,
            IEnumerable<string> enabledOptions,
            uint seed)
        {
            string runRoot = Path.Combine(scratchRoot, name + "-" + seed + "-" + Guid.NewGuid().ToString("N"));
            string staging = Path.Combine(runRoot, "staging");
            Directory.CreateDirectory(runRoot);
            CopyGameData(Path.Combine(dataRoot, "dist", "DS1R"), staging);

            var enabled = new HashSet<string>(enabledOptions, StringComparer.Ordinal);
            var options = new JsonObject();
            foreach ((string key, _) in HostRequest.OptionFlags) options[key] = enabled.Contains(key);
            HostRequest request = HostRequest.Parse(Request(dataRoot, staging, Path.Combine(runRoot, "foghost.log"), options,
                seed, RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT"), catalog.Fingerprint).ToJsonString());
            var runner = new HostRunner((_, _) => { });
            TextWriter originalOutput = Console.Out;
            HostResult result;
            try
            {
                Console.SetOut(TextWriter.Null);
                runner.ValidateLayout(request);
                result = runner.Run(request);
                DecodeEveryWrittenFile(staging, result);
            }
            finally
            {
                Console.SetOut(originalOutput);
            }

            return result.Files.OrderBy(file => file.Path, StringComparer.Ordinal)
                .Select(file => $"{file.Path}:{file.Size}:{file.Sha256}")
                .ToArray();
        }

        private static JsonObject Request(
            string dataRoot,
            string staging,
            string logPath,
            JsonObject options,
            uint seed,
            string pristineRoot,
            string annotationFingerprint) => new JsonObject
        {
            ["schemaVersion"] = HostRequest.SchemaVersion,
            ["game"] = "DS1R",
            ["mode"] = "write",
            ["seed"] = seed,
            ["options"] = options,
            ["language"] = "ENGLISH",
            ["fogDataRoot"] = Path.GetFullPath(dataRoot),
            ["stagingDirectory"] = Path.GetFullPath(staging),
            ["logPath"] = Path.GetFullPath(logPath),
            ["annotationSource"] = HostRequest.CompactAnnotations,
            ["pristineGameDirectory"] = Path.GetFullPath(pristineRoot),
            ["expectedAnnotationFingerprint"] = annotationFingerprint,
            ["compatibility"] = new JsonObject
            {
                ["baseDirectory"] = Path.Combine(Path.GetTempPath(), "ds1tr-compact-fog-base-" + Guid.NewGuid().ToString("N")),
                ["combinedFiles"] = new JsonArray(),
                ["preservedNpcParams"] = new JsonArray(),
            },
        };

        private static void CopyGameData(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string subdirectory in new[] { "event", "map", "param", "msg", "script" })
                CopyTree(Path.Combine(source, subdirectory), Path.Combine(target, subdirectory));
        }

        private static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            foreach (string directory in Directory.GetDirectories(source))
                CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
        }

        private static void DecodeEveryWrittenFile(string staging, HostResult result)
        {
            if (result.Files == null || result.Files.Count == 0)
                throw new InvalidDataException("Compact GameDataWriter did not report any written files.");
            foreach (OutputFile output in result.Files)
            {
                string path = Path.Combine(staging, output.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) throw new FileNotFoundException($"FogHost reported missing output '{output.Path}'.", path);
                if (path.EndsWith(".msb", StringComparison.OrdinalIgnoreCase))
                {
                    _ = MSB1.Read(path);
                }
                else if (path.EndsWith(".emevd.dcx", StringComparison.OrdinalIgnoreCase))
                {
                    _ = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(path)));
                }
                else if (path.EndsWith(".dcx", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    byte[] binder = DCX.Is(bytes) ? DCX.Decompress(bytes) : bytes;
                    if (binder.Length < 4) throw new InvalidDataException($"FogHost output '{output.Path}' is too short to be a binder.");
                    if (binder[0] == 'B' && binder[1] == 'N' && binder[2] == 'D' && binder[3] == '3') _ = BND3.Read(binder);
                    else if (binder[0] == 'B' && binder[1] == 'N' && binder[2] == 'D' && binder[3] == '4') _ = BND4.Read(binder);
                    else throw new InvalidDataException($"FogHost output '{output.Path}' has an unsupported binder header.");
                }
                else
                {
                    throw new InvalidDataException($"Unexpected Compact GameDataWriter output path '{output.Path}'.");
                }
            }
        }

        private static string RequiredDirectory(string variable)
        {
            string path = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                throw new InvalidOperationException($"{variable} must point to an existing safe local data directory.");
            return Path.GetFullPath(path);
        }
    }
}
