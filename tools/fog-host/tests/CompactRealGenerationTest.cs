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

                // Reproduce the owner's accepted V5 QA Fog seed/options against frozen v1 and v2.
                // v1 must retain its historical graph; v2 gates the local Ceaseless/lava route on
                // verified boss completion while the writer still emits valid files.
                string[] reproducedOptions = { "traversableFogs", "bossFogs", "areaWarps", "scaling", "requireLordSouls" };
                string v1Landing = null;
                string v2Landing = null;
                string[] v1Output = RunCase(dataRoot, scratchRoot, v1, "pvp-off-repro-v1", reproducedOptions, 31341427, "POLISH",
                    landing => v1Landing = landing);
                string[] v2Output = RunCaseV2(dataRoot, scratchRoot, v2, "pvp-off-repro-v2", reproducedOptions, 31341427, "POLISH",
                    landing => v2Landing = landing,
                    (staging, result) => VerifyCompactPvpSoloInitializers(staging, result));
                AssertCeaselessRoute(v1.Catalog, "v1", expectedCompletionEventId: null);
                AssertCeaselessRoute(v2.Catalog, "v2", expectedCompletionEventId: 11410410);
                if (string.IsNullOrWhiteSpace(v1Landing) || string.IsNullOrWhiteSpace(v2Landing)
                    || !string.Equals(PhysicalLanding(v1Landing), PhysicalLanding(v2Landing), StringComparison.Ordinal))
                    throw new InvalidDataException($"Compact Fog v2 Ceaseless warp output differs from the frozen v1/FogMod default landing: v1='{v1Landing}', v2='{v2Landing}'.");
                if (v1Output.SequenceEqual(v2Output, StringComparer.Ordinal))
                    throw new InvalidDataException("The exact reproduction produced identical v1/v2 output hashes despite the catalog correction.");
                Console.WriteLine($"PASS PvP OFF real-format accepted-world regression: fogSeed=31341427; v1Files={v1Output.Length}; v2Files={v2Output.Length}; identical Ceaseless physical landing; v1={v1Landing}; v2={v2Landing}; v1/v2 progression differs as expected.");

                string capraLanding = null;
                string[] capraOutput = RunCaseV2(dataRoot, scratchRoot, v2, "owner-o5865-to-o1413-landing",
                    reproducedOptions, 2831875538, "POLISH",
                    onWrittenOutput: (staging, result) =>
                        capraLanding = VerifyCapraDefaultLanding(staging, pristineRoot, v2.Catalog),
                    onLogWritten: VerifyOwnerCapraRouteLog);
                if (string.IsNullOrWhiteSpace(capraLanding))
                    throw new InvalidDataException("The confirmed o5865 to o1413 real-format route did not validate its Capra landing.");
                Console.WriteLine($"PASS owner o5865 -> o1413 under-map landing regression: fogSeed=2831875538; outputFiles={capraOutput.Length}; {capraLanding}");

                var majorPvpRejections = new List<uint>();
                bool majorPvpWritten = false;
                IEnumerable<uint> majorPvpSeeds = new[] { MatrixSeed, 31341427U }
                    .Concat(Enumerable.Range(1, 127).Select(seed => checked((uint)seed)))
                    .Distinct();
                foreach (uint seed in majorPvpSeeds)
                {
                    try
                    {
                        RunCaseV2(dataRoot, scratchRoot, v2, "pvp-major-on-physical-" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            new[] { "traversableFogs", "majorPvpFogs" }, seed,
                            onWrittenOutput: (staging, result) => VerifyCompactPvpMajorRandomizedOutput(staging, result));
                        majorPvpWritten = true;
                        Console.WriteLine($"PASS Compact V2 PvP ON real writer output: seed={seed}, rejectedBeforeSuccess={majorPvpRejections.Count}, rejects=[{string.Join(",", majorPvpRejections)}].");
                        break;
                    }
                    catch (FogHostException error) when (error.Code == HostErrors.SeedUnsolvable && error.Retryable
                        && error.RetryScope == "fog" && error.Stage == "solve-graph")
                    {
                        majorPvpRejections.Add(seed);
                    }
                }
                if (!majorPvpWritten)
                    throw new InvalidDataException($"No valid major-PvP real-format seed was found in {majorPvpRejections.Count} deterministic candidates; first={majorPvpRejections.FirstOrDefault()}, last={majorPvpRejections.LastOrDefault()}.");

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
                int expectedRetryableRejections = 0;
                foreach (var testCase in cases)
                {
                    try
                    {
                        string[] output = RunCaseV2(dataRoot, scratchRoot, v2, testCase.Name, testCase.Enabled, testCase.Seed);
                        if (testCase.Name == "high-risk-warp-hard-lord-start") deterministicFirst = output;
                        Console.WriteLine($"PASS Compact Fog v2 real generation: {testCase.Name}, seed={testCase.Seed}, files={output.Length}");
                    }
                    catch (FogHostException error) when (IsExpectedPostBossRetry(testCase.Name, error))
                    {
                        expectedRetryableRejections++;
                        Console.WriteLine($"PASS Compact Fog v2 typed retry rejection: {testCase.Name}, seed={testCase.Seed}, code={error.Code}, stage={error.Stage}, retryable={error.Retryable}, scope={error.RetryScope}.");
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

                Console.WriteLine($"Compact Fog v2 GameDataWriter matrix: {cases.Count - failures.Count} passed, including {expectedRetryableRejections} expected typed post-boss retry rejections; {failures.Count} failed; {cases.Count} cases plus support reproduction; scratch={scratchRoot}");
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

        public static int RunPostBossSeedSweepFromEnvironment()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            string pristineRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            string seedList = Environment.GetEnvironmentVariable("DS1TR_COMPACT_V2_POST_BOSS_SEEDS");
            if (string.IsNullOrWhiteSpace(seedList))
                throw new InvalidOperationException("DS1TR_COMPACT_V2_POST_BOSS_SEEDS must contain a comma-separated list of uint32 Fog seeds.");

            uint[] seeds = seedList.Split(',').Select(value => uint.Parse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (seeds.Length == 0 || seeds.Distinct().Count() != seeds.Length)
                throw new InvalidOperationException("The post-boss seed sweep requires a non-empty list of distinct uint32 seeds.");

            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineRoot, includeLegacyAnnotations: false);
            FogCompactCatalogV2 catalog = FogCompactCatalogBuilder.BuildV2(facts);
            string originalDirectory = Environment.CurrentDirectory;
            string scratchRoot = Path.Combine(Path.GetTempPath(), "ds1tr-compact-v2-post-boss-sweep-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratchRoot);
            var successes = new List<uint>();
            var retryableRejections = new List<(uint Seed, string Message)>();
            var unexpectedFailures = new List<(uint Seed, string Message)>();
            string[] options = { "traversableFogs", "bossFogs", "areaWarps", "scaling", "requireLordSouls" };
            try
            {
                Environment.CurrentDirectory = dataRoot;
                foreach (uint seed in seeds)
                {
                    try
                    {
                        RunCaseV2(dataRoot, scratchRoot, catalog, "post-boss-" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            options, seed, "POLISH");
                        successes.Add(seed);
                        Console.WriteLine($"PASS Compact Fog v2 post-boss seed {seed}.");
                    }
                    catch (FogHostException error) when (error.Code == HostErrors.SeedUnsolvable && error.Retryable
                        && error.RetryScope == "fog" && error.Stage == "solve-graph")
                    {
                        retryableRejections.Add((seed, error.Message));
                        Console.WriteLine($"RETRYABLE Compact Fog v2 post-boss seed {seed}: {error.Message}");
                    }
                    catch (Exception error)
                    {
                        unexpectedFailures.Add((seed, error.ToString()));
                        Console.Error.WriteLine($"FAIL Compact Fog v2 post-boss seed {seed}: {error}");
                    }
                }

                Console.WriteLine($"Compact Fog v2 post-boss seed sweep: {successes.Count}/{seeds.Length} generated; {retryableRejections.Count} typed retryable post-boss rejections; {unexpectedFailures.Count} unexpected failures; firstSuccess={(successes.Count > 0 ? successes[0].ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}; scratch={scratchRoot}");
                return unexpectedFailures.Count == 0 && successes.Count > 0 ? 0 : 1;
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
            }
        }

        private static bool IsExpectedPostBossRetry(string caseName, FogHostException error) =>
            (caseName == "bossFogs" || caseName == "all-fog-options")
            && error.Code == HostErrors.SeedUnsolvable
            && error.Retryable
            && error.RetryScope == "fog"
            && error.Stage == "solve-graph";

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
            Action<string> onCeaselessLanding = null,
            Action<string, HostResult> onWrittenOutput = null,
            Action<string> onLogWritten = null)
        {
            string runRoot = Path.Combine(scratchRoot, name + "-" + seed + "-" + Guid.NewGuid().ToString("N"));
            string staging = Path.Combine(runRoot, "staging");
            Directory.CreateDirectory(runRoot);
            CopyGameData(Path.Combine(dataRoot, "dist", "DS1R"), staging, RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT"), language);

            var enabled = new HashSet<string>(enabledOptions, StringComparer.Ordinal);
            var options = new JsonObject();
            foreach ((string key, _) in HostRequest.OptionFlags) options[key] = enabled.Contains(key);
            string logPath = Path.Combine(runRoot, "foghost.log");
            HostRequest request = HostRequest.Parse(Request(dataRoot, staging, logPath, options,
                seed, RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT"), catalog.Fingerprint, HostRequest.CompactV2Annotations, language).ToJsonString());
            var runner = new HostRunner((_, _) => { });
            TextWriter originalOutput = Console.Out;
            StringWriter capturedOutput = onLogWritten == null ? null : new StringWriter();
            HostResult result;
            try
            {
                Console.SetOut(capturedOutput ?? TextWriter.Null);
                runner.ValidateLayout(request);
                result = runner.Run(request);
                DecodeEveryWrittenFile(staging, result);
                if (onCeaselessLanding != null)
                    onCeaselessLanding(VerifyCeaselessDefaultLanding(staging, RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT"), catalog.Catalog));
                onWrittenOutput?.Invoke(staging, result);
                onLogWritten?.Invoke(capturedOutput?.ToString() ?? string.Empty);
            }
            finally
            {
                Console.SetOut(originalOutput);
            }

            return result.Files.OrderBy(file => file.Path, StringComparer.Ordinal)
                .Select(file => $"{file.Path}:{file.Size}:{file.Sha256}")
                .ToArray();
        }

        private static void VerifyCompactPvpSoloInitializers(string staging, HostResult result)
        {
            OutputFile commonOutput = result.Files.SingleOrDefault(file => file.Path == "event/common.emevd.dcx")
                ?? throw new InvalidDataException("Compact Fog V2 did not report common.emevd.dcx, where FogMod owns all Event 5700 initializers.");
            string commonPath = Path.Combine(staging, commonOutput.Path.Replace('/', Path.DirectorySeparatorChar));
            EMEVD common = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(commonPath)));
            EMEVD.Event eventZero = common.Events.Single(evt => evt.ID == 0);
            EMEVD.Instruction[] modeOne = eventZero.Instructions.Where(instruction => instruction.Bank == 2000
                && instruction.ID == 0 && instruction.ArgData.Length == 40
                && BitConverter.ToUInt32(instruction.ArgData, 4) == 5700
                && instruction.ArgData[8] == 1).ToArray();

            FogCompactV2PvpPolicy.GateIdentity[] expected = FogCompactV2PvpPolicy.Identities
                .Where(gate => !gate.Unused).ToArray();
            foreach (FogCompactV2PvpPolicy.GateIdentity gate in expected)
            {
                EMEVD.Instruction[] matches = modeOne.Where(instruction => BitConverter.ToInt32(instruction.ArgData, 12) == gate.EntityId
                    && BitConverter.ToInt32(instruction.ArgData, 16) == gate.EntityId + 1).ToArray();
                string map = AnnotationData.nameSpecs[gate.Map.Value].Map;
                byte mapArea = byte.Parse(map.Substring(1, 2), System.Globalization.CultureInfo.InvariantCulture);
                byte mapBlock = byte.Parse(map.Substring(4, 2), System.Globalization.CultureInfo.InvariantCulture);
                bool hasModeZero = eventZero.Instructions.Any(instruction => instruction.Bank == 2000
                    && instruction.ID == 0 && instruction.ArgData.Length == 40
                    && BitConverter.ToUInt32(instruction.ArgData, 4) == 5700
                    && instruction.ArgData[8] == 0
                    && BitConverter.ToInt32(instruction.ArgData, 12) == gate.EntityId
                    && BitConverter.ToInt32(instruction.ArgData, 16) == gate.EntityId + 1);
                if (matches.Length != 1 || matches[0].ArgData[9] != mapArea || matches[0].ArgData[10] != mapBlock || hasModeZero)
                    throw new InvalidDataException($"PvP OFF must install exactly one Event 5700 mode-1 solo initializer and no mode-0 randomized dispatcher for '{gate.Name}' ({gate.Map}, entity {gate.EntityId}) in common event 0.");
            }
            FogCompactV2PvpPolicy.GateIdentity unused = FogCompactV2PvpPolicy.Identities.Single(gate => gate.Unused);
            if (modeOne.Any(instruction => BitConverter.ToInt32(instruction.ArgData, 12) == unused.EntityId))
                throw new InvalidDataException($"The upstream unused PvP record '{unused.Name}' must not receive an Event 5700 initializer.");
            Console.WriteLine($"PASS Compact V2 PvP OFF physical parity: {expected.Length} pinned active vanilla gates have one correctly mapped common Event 5700 mode-1 initializer; unused {unused.Name} omitted.");
        }

        private static void VerifyCompactPvpMajorRandomizedOutput(string staging, HostResult result)
        {
            OutputFile commonOutput = result.Files.SingleOrDefault(file => file.Path == "event/common.emevd.dcx")
                ?? throw new InvalidDataException("Compact Fog V2 did not report common.emevd.dcx for major PvP randomization.");
            string commonPath = Path.Combine(staging, commonOutput.Path.Replace('/', Path.DirectorySeparatorChar));
            EMEVD common = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(commonPath)));
            EMEVD.Event eventZero = common.Events.Single(evt => evt.ID == 0);
            EMEVD.Instruction[] initializers = eventZero.Instructions.Where(instruction => instruction.Bank == 2000
                && instruction.ID == 0 && instruction.ArgData.Length == 40
                && BitConverter.ToUInt32(instruction.ArgData, 4) == 5700).ToArray();
            foreach (FogCompactV2PvpPolicy.GateIdentity gate in FogCompactV2PvpPolicy.Identities.Where(identity => !identity.Unused))
            {
                EMEVD.Instruction[] calls = initializers
                    .Where(instruction => BitConverter.ToInt32(instruction.ArgData, 12) == gate.EntityId)
                    .ToArray();
                byte expectedMode = !gate.Door && gate.Major ? (byte)0 : (byte)1;
                bool hasExpectedMode = calls.Any(call => call.ArgData[8] == expectedMode);
                bool hasUnexpectedMode = calls.Any(call => call.ArgData[8] != expectedMode);
                if (!hasExpectedMode || hasUnexpectedMode)
                    throw new InvalidDataException($"Major PvP ON expected Event 5700 mode {expectedMode} for active gate '{gate.Name}' (entity {gate.EntityId}); found {calls.Length} initializer(s) with modes [{string.Join(",", calls.Select(call => call.ArgData[8]))}].");
            }
            Console.WriteLine("PASS Compact V2 PvP ON physical parity: all 45 active pinned FogMod gates use randomized mode 0 only for enabled major gates and solo mode 1 otherwise.");
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

        private static void VerifyOwnerCapraRouteLog(string log)
        {
            const string expectedRoute = "From anorlondo_main (anorlondo-main-building-wall) to burg_capra (burg-capra-boundary)";
            if (!log.Contains(expectedRoute, StringComparison.Ordinal))
                throw new InvalidDataException($"Fog seed 2831875538 no longer produces the confirmed o5865 to o1413 route. Route log did not contain '{expectedRoute}'.");
        }

        private static string VerifyCapraDefaultLanding(string staging, string pristineRoot, FogAnnotationCatalog catalog)
        {
            FogEntrance entrance = catalog.Entrances.Single(value => value.Name == "o1413_0000");
            FogEntranceSide side = entrance.BSide;
            if (entrance.Map.Value != "parish" || side.Area.Value != "burg_capra"
                || !entrance.Tags.HasFlag(FogEntranceTags.Boss)
                || entrance.Tags.HasFlag(FogEntranceTags.Door)
                || entrance.Tags.HasFlag(FogEntranceTags.World)
                || side.CustomWarp != null || side.DestinationMap != null
                || side.ActionRegion != 0 || side.CustomActionWidth != 0
                || side.Tags.HasFlag(FogEntranceSideTags.Higher)
                || side.BossTrigger <= 0 || side.BossTriggerArea == null)
                throw new InvalidDataException("The o1413 burg_capra BSide no longer has the verified default fog landing plus a separate boss trigger.");

            string mapName = AnnotationData.nameSpecs[entrance.Map.Value].Map;
            MSB1 pristineMap = MSB1.Read(Path.Combine(pristineRoot, "map", "MapStudio", mapName + ".msb"));
            MSB1.Part.Object fog = pristineMap.Parts.Objects.Single(part =>
                part.Name == entrance.Name && part.EntityID == entrance.Id);

            // FogMod's BSide default branch moves one metre opposite object yaw,
            // keeps the object yaw for player facing, and applies no "higher" offset.
            float oppositeYaw = fog.Rotation.Y + 180f;
            if (oppositeYaw >= 180f) oppositeYaw -= 360f;
            double angle = oppositeYaw * Math.PI / 180d;
            float expectedX = fog.Position.X + (float)Math.Sin(angle);
            float expectedY = fog.Position.Y;
            float expectedZ = fog.Position.Z + (float)Math.Cos(angle);
            float expectedYaw = fog.Rotation.Y;

            MSB1 outputMap = MSB1.Read(Path.Combine(staging, "map", "MapStudio", mapName + ".msb"));
            HashSet<string> sourcePlayerNames = pristineMap.Parts.Players
                .Select(player => player.Name).ToHashSet(StringComparer.Ordinal);
            MSB1.Part.Player[] generatedPlayers = outputMap.Parts.Players
                .Where(player => !sourcePlayerNames.Contains(player.Name)).ToArray();
            MSB1.Part.Player[] expectedLandings = generatedPlayers.Where(player =>
                Math.Abs(player.Position.X - expectedX) < 0.001f
                && Math.Abs(player.Position.Y - expectedY) < 0.001f
                && Math.Abs(player.Position.Z - expectedZ) < 0.001f
                && Math.Abs(player.Rotation.Y - expectedYaw) < 0.001f).ToArray();
            FogTriggerVolume trigger = side.BossTriggerArea;
            bool playerAtBossTriggerCenter = generatedPlayers.Any(player =>
                Math.Abs(player.Position.X - trigger.X) < 0.001f
                && Math.Abs(player.Position.Y - trigger.Y) < 0.001f
                && Math.Abs(player.Position.Z - trigger.Z) < 0.001f);
            if (expectedLandings.Length != 1 || playerAtBossTriggerCenter)
                throw new InvalidDataException($"o1413 BSide must use the default object-relative landing at ({expectedX:R},{expectedY:R},{expectedZ:R},{expectedYaw:R}), not the boss-trigger center ({trigger.X:R},{trigger.Y:R},{trigger.Z:R}); matches={expectedLandings.Length}, centeredPlayer={playerAtBossTriggerCenter}.");

            float expectedActionY = fog.Position.Y - 1f;
            MSB1.Region action = outputMap.Regions.Regions.Single(region => region.Name == $"BR: {entrance.Text}");
            MSB.Shape.Box actionBox = action.Shape as MSB.Shape.Box;
            if (Math.Abs(action.Position.X - expectedX) >= 0.001f
                || Math.Abs(action.Position.Y - expectedActionY) >= 0.001f
                || Math.Abs(action.Position.Z - expectedZ) >= 0.001f
                || Math.Abs(action.Rotation.Y - oppositeYaw) >= 0.001f
                || actionBox == null
                || Math.Abs(actionBox.Width - (side.CustomActionWidth == 0 ? 1.5f : side.CustomActionWidth)) >= 0.001f
                || Math.Abs(actionBox.Height - 3f) >= 0.001f
                || Math.Abs(actionBox.Depth - 1.5f) >= 0.001f)
                throw new InvalidDataException($"o1413 BSide action region does not match FogMod's default placement at ({expectedX:R},{expectedActionY:R},{expectedZ:R},{oppositeYaw:R}).");

            MSB1.Region bossStart = outputMap.Regions.Regions.Single(region => region.Name == $"Boss start for {side.Area.Value}");
            MSB.Shape.Box bossBox = bossStart.Shape as MSB.Shape.Box;
            if (Math.Abs(bossStart.Position.X - trigger.X) >= 0.001f
                || Math.Abs(bossStart.Position.Y - trigger.Y) >= 0.001f
                || Math.Abs(bossStart.Position.Z - trigger.Z) >= 0.001f
                || Math.Abs(bossStart.Rotation.Y - trigger.Yaw) >= 0.001f
                || bossBox == null
                || Math.Abs(bossBox.Width - trigger.Width) >= 0.001f
                || Math.Abs(bossBox.Height - trigger.Height) >= 0.001f
                || Math.Abs(bossBox.Depth - trigger.Depth) >= 0.001f)
                throw new InvalidDataException("The Capra boss-start region no longer matches its separate project-owned trigger volume.");

            MSB1.Part.Player player = expectedLandings[0];
            return $"{mapName}.msb:{player.Name} entity={player.EntityID} XYZ=({player.Position.X:R},{player.Position.Y:R},{player.Position.Z:R}) yaw={player.Rotation.Y:R}; actionRegion={action.EntityID}@({action.Position.X:R},{action.Position.Y:R},{action.Position.Z:R}); separateBossTrigger={bossStart.EntityID}@({bossStart.Position.X:R},{bossStart.Position.Y:R},{bossStart.Position.Z:R})";
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
