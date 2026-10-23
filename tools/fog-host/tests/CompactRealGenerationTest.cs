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
                CopyGameData(Path.Combine(dataRoot, "dist", "DS1R"), staging, pristineRoot, "ENGLISH");
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

        public static int RunV2FromEnvironment()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            string pristineRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineRoot, includeLegacyAnnotations: false);
            FogCompactCatalogV1 v1 = FogCompactCatalogBuilder.Build(facts);
            FogCompactCatalogV2 v2 = FogCompactCatalogBuilder.BuildV2(facts);
            string originalDirectory = Environment.CurrentDirectory;
            string scratchRoot = Path.Combine(Path.GetTempPath(), "ds1tr-compact-fog-v2-realgen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratchRoot);
            try
            {
                Environment.CurrentDirectory = dataRoot;

                // Reproduce the owner's support-bundle Fog seed/options against both frozen v1 and
                // v2. v1 must retain its historical graph; v2 must gate the local Ceaseless/lava
                // route on verified boss completion while the writer still emits valid files.
                string[] reproducedOptions = { "traversableFogs", "bossFogs", "areaWarps", "scaling", "requireLordSouls" };
                string v1Landing = null;
                string v2Landing = null;
                string[] v1Output = RunCase(dataRoot, scratchRoot, v1, "support-repro-v1", reproducedOptions, 4009337993, "POLISH",
                    landing => v1Landing = landing);
                string[] v2Output = RunCaseV2(dataRoot, scratchRoot, v2, "support-repro-v2", reproducedOptions, 4009337993, "POLISH",
                    landing => v2Landing = landing);
                AssertCeaselessRoute(v1.Catalog, "v1", expectedCompletionEventId: null);
                AssertCeaselessRoute(v2.Catalog, "v2", expectedCompletionEventId: 11410410);
                if (string.IsNullOrWhiteSpace(v1Landing) || string.IsNullOrWhiteSpace(v2Landing)
                    || !string.Equals(PhysicalLanding(v1Landing), PhysicalLanding(v2Landing), StringComparison.Ordinal))
                    throw new InvalidDataException($"Compact Fog v2 Ceaseless warp output differs from the frozen v1/FogMod default landing: v1='{v1Landing}', v2='{v2Landing}'.");
                if (v1Output.SequenceEqual(v2Output, StringComparer.Ordinal))
                    throw new InvalidDataException("The exact reproduction produced identical v1/v2 output hashes despite the catalog correction.");
                Console.WriteLine($"PASS support reproduction: fogSeed=4009337993; v1Files={v1Output.Length}; v2Files={v2Output.Length}; identical Ceaseless physical landing; v1={v1Landing}; v2={v2Landing}; v1/v2 progression differs as expected.");

                var cases = HostRequest.OptionFlags
                    .Select(option => (Name: option.Key, Enabled: new[] { option.Key }, Seed: MatrixSeed))
                    .Prepend((Name: "fog-only", Enabled: Array.Empty<string>(), Seed: MatrixSeed))
                    .ToList();
                cases.Add(("high-risk-warp-hard-lord-start", new[] { "areaWarps", "glitchedLogic", "requireLordSouls", "randomStart" }, MatrixSeed));
                cases.Add(("all-fog-options", HostRequest.OptionFlags.Select(option => option.Key).ToArray(), MatrixSeed));
                cases.Add(("random-start-seed-2", new[] { "randomStart", "areaWarps", "requireLordSouls" }, 19088743));

                string[] deterministicOptions = { "areaWarps", "glitchedLogic", "requireLordSouls", "randomStart" };
                string[] deterministicFirst = null;
                var failures = new List<string>();
                foreach (var testCase in cases)
                {
                    try
                    {
                        string[] output = RunCaseV2(dataRoot, scratchRoot, v2, testCase.Name, testCase.Enabled, testCase.Seed);
                        if (testCase.Name == "high-risk-warp-hard-lord-start") deterministicFirst = output;
                        Console.WriteLine($"PASS Compact Fog v2 real generation: {testCase.Name}, seed={testCase.Seed}, files={output.Length}");
                    }
                    catch (Exception error)
                    {
                        string failure = $"{testCase.Name} (seed={testCase.Seed}): {error.Message}";
                        failures.Add(failure);
                        Console.Error.WriteLine($"FAIL Compact Fog v2 real generation: {failure}");
                    }
                }
                if (deterministicFirst != null)
                {
                    try
                    {
                        string[] repeated = RunCaseV2(dataRoot, scratchRoot, v2, "determinism-repeat", deterministicOptions, MatrixSeed);
                        if (!deterministicFirst.SequenceEqual(repeated, StringComparer.Ordinal))
                            failures.Add("determinism-repeat: output hashes changed for a repeated seed/options pair");
                        else
                            Console.WriteLine($"PASS Compact Fog v2 determinism: seed={MatrixSeed}, outputs={repeated.Length}");
                    }
                    catch (Exception error)
                    {
                        failures.Add($"determinism-repeat (seed={MatrixSeed}): {error.Message}");
                    }
                }
                else
                {
                    failures.Add("determinism-repeat: primary high-risk case failed, so deterministic comparison was unavailable");
                }

                Console.WriteLine($"Compact Fog v2 GameDataWriter matrix: {cases.Count - failures.Count} passed; {failures.Count} failed; {cases.Count} cases plus support reproduction; scratch={scratchRoot}");
                foreach (string failure in failures) Console.Error.WriteLine($"MATRIX FAILURE: {failure}");
                return failures.Count == 0 ? 0 : 1;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"FAIL Compact Fog v2 real generation: {error}");
                Console.Error.WriteLine($"Scratch retained for inspection: {scratchRoot}");
                return 1;
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
            }
        }

        private static void AssertCeaselessRoute(FogAnnotationCatalog catalog, string label, int? expectedCompletionEventId)
        {
            FogAreaLink mainToLava = catalog.Areas.Single(area => area.Id.Value == "demonruins").To
                .SingleOrDefault(link => link.Destination.Value == "demonruins_lava");
            FogAreaLink ceaselessToLava = catalog.Areas.Single(area => area.Id.Value == "demonruins_ceaseless").To
                .SingleOrDefault(link => link.Destination.Value == "demonruins_lava");
            bool lavaToCeaseless = catalog.Areas.Single(area => area.Id.Value == "demonruins_lava").To
                .Any(link => link.Destination.Value == "demonruins_ceaseless");
            bool matches = expectedCompletionEventId.HasValue
                ? mainToLava?.Condition is { Kind: FogConditionKind.Atom, Terms.Count: 1 }
                    && mainToLava.Condition.Terms[0] == FogConditionReference.BossCompletion(expectedCompletionEventId.Value)
                    && ceaselessToLava == null && !lavaToCeaseless
                : mainToLava == null && ceaselessToLava?.Condition == null && lavaToCeaseless;
            if (!matches)
                throw new InvalidDataException($"Compact Fog {label} Ceaseless/lower-Demon-Ruins route did not match its frozen condition and physical-owner policy.");
        }

        private static string[] RunCaseV2(
            string dataRoot,
            string scratchRoot,
            FogCompactCatalogV2 catalog,
            string name,
            IEnumerable<string> enabledOptions,
            uint seed,
            string language = "ENGLISH",
            Action<string> onCeaselessLanding = null)
        {
            string runRoot = Path.Combine(scratchRoot, name + "-" + seed + "-" + Guid.NewGuid().ToString("N"));
            string staging = Path.Combine(runRoot, "staging");
            Directory.CreateDirectory(runRoot);
            CopyGameData(Path.Combine(dataRoot, "dist", "DS1R"), staging, RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT"), language);

            var enabled = new HashSet<string>(enabledOptions, StringComparer.Ordinal);
            var options = new JsonObject();
            foreach ((string key, _) in HostRequest.OptionFlags) options[key] = enabled.Contains(key);
            HostRequest request = HostRequest.Parse(Request(dataRoot, staging, Path.Combine(runRoot, "foghost.log"), options,
                seed, RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT"), catalog.Fingerprint, HostRequest.CompactV2Annotations, language).ToJsonString());
            var runner = new HostRunner((_, _) => { });
            TextWriter originalOutput = Console.Out;
            HostResult result;
            try
            {
                Console.SetOut(TextWriter.Null);
                runner.ValidateLayout(request);
                result = runner.Run(request);
                DecodeEveryWrittenFile(staging, result);
                if (onCeaselessLanding != null)
                    onCeaselessLanding(VerifyCeaselessDefaultLanding(staging, RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT"), catalog.Catalog));
            }
            finally
            {
                Console.SetOut(originalOutput);
            }

            return result.Files.OrderBy(file => file.Path, StringComparer.Ordinal)
                .Select(file => $"{file.Path}:{file.Size}:{file.Sha256}")
                .ToArray();
        }

        private static string[] RunCase(
            string dataRoot,
            string scratchRoot,
            FogCompactCatalogV1 catalog,
            string name,
            IEnumerable<string> enabledOptions,
            uint seed,
            string language = "ENGLISH",
            Action<string> onCeaselessLanding = null)
        {
            string runRoot = Path.Combine(scratchRoot, name + "-" + seed + "-" + Guid.NewGuid().ToString("N"));
            string staging = Path.Combine(runRoot, "staging");
            Directory.CreateDirectory(runRoot);
            CopyGameData(Path.Combine(dataRoot, "dist", "DS1R"), staging, RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT"), language);

            var enabled = new HashSet<string>(enabledOptions, StringComparer.Ordinal);
            var options = new JsonObject();
            foreach ((string key, _) in HostRequest.OptionFlags) options[key] = enabled.Contains(key);
            HostRequest request = HostRequest.Parse(Request(dataRoot, staging, Path.Combine(runRoot, "foghost.log"), options,
                seed, RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT"), catalog.Fingerprint, HostRequest.CompactAnnotations, language).ToJsonString());
            var runner = new HostRunner((_, _) => { });
            TextWriter originalOutput = Console.Out;
            HostResult result;
            try
            {
                Console.SetOut(TextWriter.Null);
                runner.ValidateLayout(request);
                result = runner.Run(request);
                DecodeEveryWrittenFile(staging, result);
                if (onCeaselessLanding != null)
                    onCeaselessLanding(VerifyCeaselessDefaultLanding(staging, RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT"), catalog.Catalog));
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
            string annotationFingerprint,
            string annotationSource = HostRequest.CompactAnnotations,
            string language = "ENGLISH") => new JsonObject
        {
            ["schemaVersion"] = HostRequest.SchemaVersion,
            ["game"] = "DS1R",
            ["mode"] = "write",
            ["seed"] = seed,
            ["options"] = options,
            ["language"] = language,
            ["fogDataRoot"] = Path.GetFullPath(dataRoot),
            ["stagingDirectory"] = Path.GetFullPath(staging),
            ["logPath"] = Path.GetFullPath(logPath),
            ["annotationSource"] = annotationSource,
            ["pristineGameDirectory"] = Path.GetFullPath(pristineRoot),
            ["expectedAnnotationFingerprint"] = annotationFingerprint,
            ["compatibility"] = new JsonObject
            {
                ["baseDirectory"] = Path.Combine(Path.GetTempPath(), "ds1tr-compact-fog-base-" + Guid.NewGuid().ToString("N")),
                ["combinedFiles"] = new JsonArray(),
                ["preservedNpcParams"] = new JsonArray(),
            },
        };

        private static void CopyGameData(string source, string target, string pristineRoot, string language)
        {
            Directory.CreateDirectory(target);
            foreach (string subdirectory in new[] { "event", "map", "param", "msg", "script" })
                CopyTree(Path.Combine(source, subdirectory), Path.Combine(target, subdirectory));
            // The temporary compact data root intentionally contains only FogHost's
            // verified read set. The language menu binder remains in the read-only
            // pristine game source and is copied into test staging, never the runtime root.
            string menuSource = Path.Combine(pristineRoot, "msg", language, "menu.msgbnd.dcx");
            if (!File.Exists(menuSource)) throw new FileNotFoundException("Pristine QA source is missing the FogHost menu read-set file.", menuSource);
            string menuTarget = Path.Combine(target, "msg", language, "menu.msgbnd.dcx");
            Directory.CreateDirectory(Path.GetDirectoryName(menuTarget));
            File.Copy(menuSource, menuTarget, overwrite: true);
        }

        private static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            if (!Directory.Exists(source)) return;
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

        private static string VerifyCeaselessDefaultLanding(string staging, string pristineRoot, FogAnnotationCatalog catalog)
        {
            FogEntrance ceaselessEntrance = catalog.Entrances.Single(entrance => entrance.Name == "o4961_0000");
            FogEntranceSide destinationSide = ceaselessEntrance.BSide;
            if (destinationSide.Area.Value != "demonruins_ceaseless" || destinationSide.CustomWarp != null)
                throw new InvalidDataException("The Ceaseless B side must use FogMod's ordinary object-relative warp path.");

            string mapName = "m14_01_00_00";
            string mapPath = Path.Combine(pristineRoot, "map", "MapStudio", mapName + ".msb");
            MSB1 pristineMap = MSB1.Read(mapPath);
            MSB1.Part.Object fog = pristineMap.Parts.Objects.Single(part => part.Name == "o4961_0000" && part.EntityID == 1411790);
            MSB1.Region trigger = pristineMap.Regions.Regions.Single(region => region.EntityID == 1412698);

            // Pinned FogMod GameDataWriter: B-side warp is one metre along the opposite
            // fog-object yaw, with the fog object's yaw retained for player facing.
            float moveYaw = fog.Rotation.Y + 180f;
            if (moveYaw >= 180f) moveYaw -= 360f;
            double angle = moveYaw * Math.PI / 180d;
            float expectedX = fog.Position.X + (float)Math.Sin(angle);
            float expectedY = fog.Position.Y;
            float expectedZ = fog.Position.Z + (float)Math.Cos(angle);
            float expectedYaw = fog.Rotation.Y;

            MSB1 outputMap = MSB1.Read(Path.Combine(staging, "map", "MapStudio", mapName + ".msb"));
            float expectedActionY = fog.Position.Y - 1f;
            MSB1.Region actionRegion = outputMap.Regions.Regions.Single(region =>
                region.Name == "BR: demonruins-ceaseless-boundary");
            if (Math.Abs(actionRegion.Position.X - expectedX) >= 0.001f
                || Math.Abs(actionRegion.Position.Y - expectedActionY) >= 0.001f
                || Math.Abs(actionRegion.Position.Z - expectedZ) >= 0.001f
                || Math.Abs(actionRegion.Rotation.Y - moveYaw) >= 0.001f)
                throw new InvalidDataException($"FogMod default action region placement failed for {mapName}/o4961_0000: region {actionRegion.EntityID}, expected ({expectedX:R}, {expectedActionY:R}, {expectedZ:R}, {moveYaw:R}).");

            HashSet<string> sourcePlayerNames = pristineMap.Parts.Players.Select(player => player.Name).ToHashSet(StringComparer.Ordinal);
            MSB1.Part.Player[] generatedPlayers = outputMap.Parts.Players.Where(player => !sourcePlayerNames.Contains(player.Name)).ToArray();
            MSB1.Part.Player[] expectedLanding = generatedPlayers.Where(player =>
                Math.Abs(player.Position.X - expectedX) < 0.001f
                && Math.Abs(player.Position.Y - expectedY) < 0.001f
                && Math.Abs(player.Position.Z - expectedZ) < 0.001f
                && Math.Abs(player.Rotation.Y - expectedYaw) < 0.001f).ToArray();
            bool centeredLandingExists = generatedPlayers.Any(player =>
                Math.Abs(player.Position.X - trigger.Position.X) < 0.001f
                && Math.Abs(player.Position.Y - trigger.Position.Y) < 0.001f
                && Math.Abs(player.Position.Z - trigger.Position.Z) < 0.001f);
            if (expectedLanding.Length != 1 || centeredLandingExists)
                throw new InvalidDataException($"FogMod default landing parity failed for {mapName}/o4961_0000: expected ({expectedX:R}, {expectedY:R}, {expectedZ:R}, {expectedYaw:R}), matched={expectedLanding.Length}, trigger-center warp present={centeredLandingExists}.");

            byte[] eventBytes = File.ReadAllBytes(Path.Combine(staging, "event", mapName + ".emevd.dcx"));
            EMEVD outputEvents = EMEVD.Read(DCX.Decompress(eventBytes));
            EMEVD.Event startEvent = outputEvents.Events.Single(evt => evt.ID == 11415371);
            bool retainsBossStartRegion = startEvent.Instructions.Any(instruction => instruction.Bank == 3
                && instruction.ID == 18 && instruction.ArgData.Length >= 12
                && BitConverter.ToInt32(instruction.ArgData, 8) == 1412698);
            if (!retainsBossStartRegion)
                throw new InvalidDataException("The Fog output no longer preserves Ceaseless encounter-start event 11415371's check for region 1412698.");

            MSB1.Part.Player player = expectedLanding[0];
            string physicalLanding = $"{player.Name}:{player.Position.X:R},{player.Position.Y:R},{player.Position.Z:R},{player.Rotation.Y:R}";
            string generatedActionRegion = $"actionRegion={actionRegion.EntityID}:{actionRegion.Position.X:R},{actionRegion.Position.Y:R},{actionRegion.Position.Z:R},{actionRegion.Rotation.Y:R}";
            return $"{physicalLanding}|{generatedActionRegion}";
        }

        private static string PhysicalLanding(string landing) => landing.Split('|', 2)[0];

        private static string RequiredDirectory(string variable)
        {
            string path = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                throw new InvalidOperationException($"{variable} must point to an existing safe local data directory.");
            return Path.GetFullPath(path);
        }
    }
}
