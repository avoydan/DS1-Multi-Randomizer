using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QaTelemetry;

namespace QaTelemetry.SelfTest;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run("package verifies against per-file manifest and pinned attestation", PackageVerifies);
        Run("external case descriptor must match its compiled hash lock", DescriptorHashLock);
        Run("changed package byte fails locked hashes", ChangedPackageFails);
        Run("package tree lock mismatch fails", TreeLockMismatchFails);
        Run("manifest lock mismatch fails", ManifestLockMismatchFails);
        Run("unsafe relative manifest path fails", UnsafeManifestPathFails);
        Run("process-rights allowlist contains query/read only", RightsAllowlist);
        Run("process write and operation rights are rejected", RightsRejectWrite);
        Run("Steam target path is rejected", SteamPathRejected);
        Run("trace output outside Temp is rejected", TraceOutsideTempRejected);
        Run("QA runtime missing archives fails closed", RuntimeMissingArchives);
        Run("unverified runtime profile blocks even with archive pair", RuntimeProfileUnverified);
        Run("unexpected QA executable hash fails closed", RuntimeExecutableHashMismatch);
        Run("unexpected QA executable path fails closed", RuntimeExecutablePathMismatch);
        Run("trace writer is create-new and summary is hash sealed", SyntheticTraceRoundTrip);
        Run("existing trace cannot be overwritten", TraceNoOverwrite);
        Run("trace mutation fails summary hash", TraceCorruptionRejected);
        Run("summary mutation fails summary hash file", SummaryCorruptionRejected);
        Run("case identity mismatch is rejected after valid reseal", TracePackageMismatchRejected);
        Run("unverified telemetry cannot promote gameplay certainty", TelemetryUnverifiedStaysUnknown);
        Run("synthetic data never promotes gameplay certainty", SyntheticNeverGameplayConfirmed);
        Run("live telemetry writer is disabled without verified profile", LiveWriterDisabled);
        Run("unknown checkpoint is rejected", UnknownCheckpointRejected);
        Run("missing session completion summary is rejected", MissingSummaryRejected);

        Console.WriteLine($"QaTelemetry self-tests: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void PackageVerifies()
    {
        using var f = new Fixture();
        PackageVerification result = PackageVerifier.Verify(f.Descriptor);
        Check(result.Ok, result.Message);
        Check(result.FileCount == 2, "Expected test package to have two files including run manifest.");
        Check(result.TotalBytes > 0, "Expected nonzero package size.");
    }

    private static void DescriptorHashLock()
    {
        using var f = new Fixture();
        string path = Path.Combine(f.Root, "case.json");
        File.WriteAllText(path, JsonSerializer.Serialize(f.Descriptor, CaseDescriptor.JsonOptions), new UTF8Encoding(false));
        Throws<InvalidDataException>(() => CaseDescriptor.Load(path));
    }

    private static void ChangedPackageFails()
    {
        using var f = new Fixture();
        File.AppendAllText(Path.Combine(f.PackageRoot, "event", "fixture.bin"), "changed");
        Check(!PackageVerifier.Verify(f.Descriptor).Ok, "Mutation unexpectedly verified.");
    }

    private static void TreeLockMismatchFails()
    {
        using var f = new Fixture();
        f.Descriptor.Package!.TreeSha256 = new string('0', 64);
        Check(!PackageVerifier.Verify(f.Descriptor).Ok, "Wrong tree hash unexpectedly verified.");
    }

    private static void ManifestLockMismatchFails()
    {
        using var f = new Fixture();
        f.Descriptor.Package!.RunManifestSha256 = new string('0', 64);
        Check(!PackageVerifier.Verify(f.Descriptor).Ok, "Wrong manifest hash unexpectedly verified.");
    }

    private static void UnsafeManifestPathFails()
    {
        using var f = new Fixture();
        string path = Path.Combine(f.PackageRoot, "run-manifest.json");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        manifest["files"]![0]!["path"] = "../outside";
        File.WriteAllText(path, manifest.ToJsonString(), new UTF8Encoding(false));
        f.Descriptor.Package!.RunManifestSha256 = SafePaths.HashFile(path);
        PackageVerification result = PackageVerifier.Verify(f.Descriptor);
        Check(!result.Ok && result.Message.Contains("Unsafe relative package path", StringComparison.Ordinal), "Unsafe path was rejected for an unrelated manifest failure.");
    }

    private static void RightsAllowlist()
    {
        ProcessAccessPolicy.Validate(ProcessAccessPolicy.AllowlistedReadOnlyRights);
        Check(ProcessAccessPolicy.RightsRequestedByThisBuild == 0, "This build must not open a process.");
        Check(ProcessAccessPolicy.AllowlistedReadOnlyRights == 0x1010, "Unexpected read-only rights mask.");
    }

    private static void RightsRejectWrite()
    {
        Throws<UnauthorizedAccessException>(() => ProcessAccessPolicy.Validate(0x1020));
        Throws<UnauthorizedAccessException>(() => ProcessAccessPolicy.Validate(0x1018));
    }

    private static void SteamPathRejected()
    {
        Throws<InvalidDataException>(() => SafePaths.RejectSteamPath("C:\\Program Files (x86)\\Steam\\steamapps\\common\\DARK SOULS REMASTERED"));
    }

    private static void TraceOutsideTempRejected()
    {
        string outside = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "..", "outside-trace.jsonl"));
        Throws<InvalidDataException>(() => SafePaths.EnsureUnderTemp(outside));
    }

    private static void RuntimeMissingArchives()
    {
        using var f = new Fixture();
        RuntimePreflightResult result = RuntimePreflight.Run(f.Descriptor);
        Check(!result.Ok && result.Code == "QA_RUNTIME_INCOMPLETE", "Missing archives did not fail closed.");
        Check(!result.HasArchivePair, "Fixture unexpectedly has game archives.");
    }

    private static void RuntimeProfileUnverified()
    {
        using var f = new Fixture();
        File.WriteAllBytes(Path.Combine(f.RuntimeRoot, "DataA.bhd"), [1, 2]);
        File.WriteAllBytes(Path.Combine(f.RuntimeRoot, "DataA.bdt"), [3, 4]);
        RuntimePreflightResult result = RuntimePreflight.Run(f.Descriptor);
        Check(!result.Ok && result.HasArchivePair, "Runtime profile gate did not remain closed.");
        Check(result.Blockers.Any(b => b.StartsWith("QA_RUNTIME_PROFILE_UNVERIFIED", StringComparison.Ordinal)), "Missing profile blocker not reported.");
    }

    private static void RuntimeExecutableHashMismatch()
    {
        using var f = new Fixture();
        f.Descriptor.Runtime!.ExecutableSha256 = new string('0', 64);
        RuntimePreflightResult result = RuntimePreflight.Run(f.Descriptor);
        Check(!result.Ok && result.Blockers.Contains("QA_EXECUTABLE_HASH_MISMATCH"), "Unexpected executable hash was accepted.");
    }

    private static void RuntimeExecutablePathMismatch()
    {
        using var f = new Fixture();
        f.Descriptor.Runtime!.ExecutablePath = Path.Combine(f.Root, "other.exe");
        File.WriteAllBytes(f.Descriptor.Runtime.ExecutablePath, [1, 2]);
        RuntimePreflightResult result = RuntimePreflight.Run(f.Descriptor);
        Check(!result.Ok && result.Blockers.Contains("QA_EXECUTABLE_PATH_MISMATCH"), "Unexpected executable path was accepted.");
    }

    private static void SyntheticTraceRoundTrip()
    {
        using var f = new Fixture();
        var paths = f.WriteSyntheticTrace("trace-round-trip");
        TraceImportResult result = TraceImporter.Import(f.Descriptor, paths.Trace, paths.Summary, paths.SummarySha);
        Check(result.Status == "SYNTHETIC_TEST_PASS", "Synthetic trace was not recognized.");
        Check(!result.GameplayConfirmed && result.ShadowEdges.All(e => e.Certainty == "UNKNOWN"), "Synthetic trace changed gameplay certainty.");
        var summary = JsonSerializer.Deserialize<TraceSummary>(File.ReadAllText(paths.Summary), CaseDescriptor.JsonOptions)!;
        Check(summary.Completed && summary.RecordCount == 2, "Session summary is incomplete.");
        Check(File.ReadAllText(paths.SummarySha).Trim() == SafePaths.HashFile(paths.Summary), "Summary SHA file mismatch.");
    }

    private static void TraceNoOverwrite()
    {
        using var f = new Fixture();
        string trace = Path.Combine(f.Root, "no-overwrite.jsonl");
        using (var writer = new TraceSessionWriter(f.Descriptor, trace, "no-overwrite"))
        {
            writer.Append(new TraceRecord { Type = "observation", Checkpoint = "CEASELESS_PRE_FIGHT" });
            writer.Complete();
        }
        Throws<IOException>(() => new TraceSessionWriter(f.Descriptor, trace, "no-overwrite"));
    }

    private static void TraceCorruptionRejected()
    {
        using var f = new Fixture();
        var paths = f.WriteSyntheticTrace("corrupt-trace");
        File.AppendAllText(paths.Trace, "corrupt\n");
        Throws<InvalidDataException>(() => TraceImporter.Import(f.Descriptor, paths.Trace, paths.Summary, paths.SummarySha));
    }

    private static void SummaryCorruptionRejected()
    {
        using var f = new Fixture();
        var paths = f.WriteSyntheticTrace("corrupt-summary");
        File.AppendAllText(paths.Summary, " ");
        Throws<InvalidDataException>(() => TraceImporter.Import(f.Descriptor, paths.Trace, paths.Summary, paths.SummarySha));
    }

    private static void TracePackageMismatchRejected()
    {
        using var f = new Fixture();
        var paths = f.WriteSyntheticTrace("mismatch-case");
        string[] lines = File.ReadAllLines(paths.Trace);
        TraceRecord header = JsonSerializer.Deserialize<TraceRecord>(lines[0], CaseDescriptor.JsonOptions)!;
        header.PackageTreeSha256 = new string('a', 64);
        lines[0] = JsonSerializer.Serialize(header, TraceFormat.JsonOptions);
        File.WriteAllLines(paths.Trace, lines, new UTF8Encoding(false));
        Reseal(paths.Trace, paths.Summary, paths.SummarySha, "mismatch-case");
        Throws<InvalidDataException>(() => TraceImporter.Import(f.Descriptor, paths.Trace, paths.Summary, paths.SummarySha));
    }

    private static void TelemetryUnverifiedStaysUnknown()
    {
        using var f = new Fixture();
        var paths = f.WriteHeaderOnlyTrace("unverified-live", "READ_ONLY_GAME_TELEMETRY", "VERIFIED", new string('b', 64));
        TraceImportResult result = TraceImporter.Import(f.Descriptor, paths.Trace, paths.Summary, paths.SummarySha);
        Check(result.Status == "TELEMETRY_UNVERIFIED" && !result.GameplayConfirmed, "Unpinned profile became trusted.");
        Check(result.ShadowEdges.All(e => e.Certainty == "UNKNOWN"), "Unverified trace promoted a shadow edge.");
    }

    private static void SyntheticNeverGameplayConfirmed()
    {
        using var f = new Fixture();
        var paths = f.WriteSyntheticTrace("synthetic-no-promotion");
        TraceImportResult result = TraceImporter.Import(f.Descriptor, paths.Trace, paths.Summary, paths.SummarySha);
        Check(result.Status == "SYNTHETIC_TEST_PASS" && !result.GameplayConfirmed, "Synthetic trace promoted to gameplay.");
    }

    private static void LiveWriterDisabled()
    {
        using var f = new Fixture();
        string trace = Path.Combine(f.Root, "live-disabled.jsonl");
        Throws<NotSupportedException>(() => new TraceSessionWriter(f.Descriptor, trace, "live-disabled", "READ_ONLY_GAME_TELEMETRY"));
        Check(!File.Exists(trace), "Disabled live writer created a trace.");
    }

    private static void UnknownCheckpointRejected()
    {
        using var f = new Fixture();
        string trace = Path.Combine(f.Root, "unknown-checkpoint.jsonl");
        using var writer = new TraceSessionWriter(f.Descriptor, trace, "unknown-checkpoint");
        Throws<InvalidDataException>(() => writer.Append(new TraceRecord { Type = "observation", Checkpoint = "INVENTED" }));
    }

    private static void MissingSummaryRejected()
    {
        using var f = new Fixture();
        string trace = Path.Combine(f.Root, "missing-summary.jsonl");
        using (var writer = new TraceSessionWriter(f.Descriptor, trace, "missing-summary"))
            writer.Append(new TraceRecord { Type = "observation", Checkpoint = "CEASELESS_PRE_FIGHT" });
        Throws<FileNotFoundException>(() => TraceImporter.Import(f.Descriptor, trace, trace + ".summary.json", trace + ".summary.sha256"));
    }

    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected exception " + typeof(T).Name + ".");
    }

    private static void Reseal(string tracePath, string summaryPath, string summaryShaPath, string session)
    {
        string hash = SafePaths.HashFile(tracePath);
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(new TraceSummary { SessionId = session, RecordCount = File.ReadAllLines(tracePath).Length, TraceSha256 = hash, Completed = true }, CaseDescriptor.JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(summaryShaPath, SafePaths.HashFile(summaryPath) + "\n", Encoding.ASCII);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ds1tr-qa-telemetry-test-" + Guid.NewGuid().ToString("N"));
        public string PackageRoot { get; }
        public string RuntimeRoot { get; }
        public CaseDescriptor Descriptor { get; }

        public Fixture()
        {
            PackageRoot = Path.Combine(Root, "package");
            RuntimeRoot = Path.Combine(Root, "qa-runtime");
            Directory.CreateDirectory(Path.Combine(PackageRoot, "event"));
            Directory.CreateDirectory(RuntimeRoot);
            File.WriteAllBytes(Path.Combine(PackageRoot, "event", "fixture.bin"), [1, 2, 3, 4]);
            string exe = Path.Combine(RuntimeRoot, "DarkSoulsRemastered.exe");
            File.WriteAllBytes(exe, [10, 11, 12]);

            string dataPath = "event/fixture.bin";
            string dataHash = SafePaths.HashFile(Path.Combine(PackageRoot, "event", "fixture.bin"));
            var manifestObject = new
            {
                appVersion = "0.46.4",
                generationProfile = "ds1tr-gen-v5",
                masterSeed = "fixture-master",
                options = new
                {
                    items = new { enabled = true },
                    enemies = new { enabled = true, randomizeRegularEnemies = true, randomizeBosses = false },
                    fog = new
                    {
                        enabled = true, requireLordSouls = false, traversableFogs = true, bossFogs = true, areaWarps = true,
                        scaling = true, majorPvpFogs = false, minorPvpFogs = false, lordvesselGates = false,
                        randomStart = false, disconnectedFogs = false, glitchedLogic = false
                    }
                },
                attempts = new[] { new { combinedAttempt = 0, fogAttempt = 4, combinedSeed = "0123456789abcdef0123456789abcdef", fogSeed = 3695053466U, result = "success" } },
                engines = new { combined = "0.5.66", fog = "0.7.3 / 6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f" },
                files = new[] { new { path = dataPath, size = 4L, sha256 = dataHash } }
            };
            string manifestPath = Path.Combine(PackageRoot, "run-manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestObject), new UTF8Encoding(false));
            string manifestHash = SafePaths.HashFile(manifestPath);
            var inventory = SafePaths.Inventory(PackageRoot, rejectHardLinks: true).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
            string treeHash = SafePaths.InventoryDigest(inventory);
            string caseName = "fixture-case";
            string attestationPath = Path.Combine(Root, "attestation.json");
            var attestation = new
            {
                schema = "ds1tr-ceaseless-e2e-attestation-v2",
                attestationClass = "CONTEMPORANEOUS_HASH_ATTESTATION",
                caseName,
                gitHead = "fixture-commit",
                backendVersion = "0.46.4",
                fogHostProjectVersion = "0.7.4",
                fogHostHostVersion = "0.7.3",
                fogModCommit = "6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f",
                worldDataId = "fixture-world",
                catalogFingerprint = "fixture-catalog",
                semanticFingerprint = "fixture-semantic",
                generation = new { masterSeed = "fixture-master" },
                result = new
                {
                    ok = true,
                    packageTreeSha256 = treeHash,
                    packageManifestSha256 = manifestHash,
                    packageFiles = inventory.Select(f => new { path = f.Path, size = f.Size, sha256 = f.Sha256 }).ToArray()
                }
            };
            File.WriteAllText(attestationPath, JsonSerializer.Serialize(attestation), new UTF8Encoding(false));
            Descriptor = new CaseDescriptor
            {
                SchemaVersion = 1,
                CaseId = "fixture-case",
                SeedCode = "fixture-seed-code",
                MasterSeed = "fixture-master",
                RawFogSeed = 3695053466U,
                FogAttempt = 4,
                CombinedSeed = "0123456789abcdef0123456789abcdef",
                SourceBranch = "fixture-branch",
                SourceCommit = "fixture-commit",
                CatalogFingerprint = "fixture-catalog",
                SemanticFingerprint = "fixture-semantic",
                WorldDataId = "fixture-world",
                FogHostProjectVersion = "0.7.4",
                FogHostHostVersion = "0.7.3",
                BackendVersion = "0.46.4",
                Package = new PackageLock
                {
                    RootPath = PackageRoot,
                    TreeSha256 = treeHash,
                    RunManifestSha256 = manifestHash,
                    AttestationPath = attestationPath,
                    AttestationSha256 = SafePaths.HashFile(attestationPath),
                    AttestationCaseName = caseName
                },
                Runtime = new RuntimeLock { RootPath = RuntimeRoot, ExecutablePath = exe, ExecutableSha256 = SafePaths.HashFile(exe), RuntimeProfileStatus = "UNVERIFIED" },
                TelemetryProfile = new TelemetryProfile
                {
                    Status = "UNVERIFIED",
                    Fields = new[] { "mapIdentity", "playerXyz", "eventFlag11410410", "mapTransitions", "barrierState", "interactionState", "markerKnownMap", "markerPlayerMovement", "markerVanillaTransition", "markerKnownFlag" }
                        .Select(name => new TelemetryField { Name = name, Status = "UNVERIFIED" }).ToArray()
                },
                ExpectedRoute = new ExpectedRoute
                {
                    EventFlag = 11410410,
                    Entrance = "o4961_0000",
                    GraphSideDestination = "Demon Ruins Lava",
                    Checkpoints = ["CEASELESS_PRE_FIGHT", "CEASELESS_BOSS_ACTIVE", "CEASELESS_DEFEATED", "EXIT_APPROACH", "EXIT_PROMPT_PRESENT", "EXIT_CROSSED", "DESTINATION_LANDED", "MAIN_DEMON_RUINS_REACHED", "LOWER_LAVA_REACHED", "POST_RELOAD_EXIT_VERIFIED"],
                    CandidateEdgeIds = ["boss-completion:11410410", "post-boss-approach:o4961_0000", "post-boss-crossing:o4961_0000", "arrival:main-demon-ruins", "route:main-to-lower-lava"]
                },
                UpstreamComparison = new UpstreamComparison { Status = "PREPARED_NOT_RUN", RawFogSeed = 3695053466U, ProfileMapping = "fixture", FogOptions = new Dictionary<string, bool>() }
            };
            Descriptor.Validate();
        }

        public (string Trace, string Summary, string SummarySha) WriteSyntheticTrace(string session)
        {
            string trace = Path.Combine(Root, session + ".jsonl");
            using var writer = new TraceSessionWriter(Descriptor, trace, session);
            writer.Append(new TraceRecord { Type = "observation", Checkpoint = "CEASELESS_PRE_FIGHT", Confidence = "TEST_FIXTURE_ONLY" });
            var completed = writer.Complete();
            return (trace, completed.SummaryPath, completed.SummarySha256Path);
        }

        public (string Trace, string Summary, string SummarySha) WriteHeaderOnlyTrace(string session, string provenance, string status, string profileSha)
        {
            string trace = Path.Combine(Root, session + ".jsonl");
            var header = new TraceRecord
            {
                Type = "header", SchemaVersion = 1, SessionId = session, Sequence = 0,
                TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                ExecutableSha256 = Descriptor.Runtime!.ExecutableSha256!,
                PackageTreeSha256 = Descriptor.Package!.TreeSha256!,
                RunManifestSha256 = Descriptor.Package.RunManifestSha256!,
                Provenance = provenance, TelemetryStatus = status, ProfileSha256 = profileSha,
                ObservedExecutablePath = Descriptor.Runtime.ExecutablePath,
                ProcessId = provenance == "READ_ONLY_GAME_TELEMETRY" ? 42 : null,
                ProcessCreatedUtc = provenance == "READ_ONLY_GAME_TELEMETRY" ? DateTimeOffset.UtcNow.ToString("O") : null
            };
            File.WriteAllText(trace, JsonSerializer.Serialize(header, TraceFormat.JsonOptions) + "\n", new UTF8Encoding(false));
            string traceHash = SafePaths.HashFile(trace);
            string summaryPath = trace + ".summary.json";
            string summaryShaPath = trace + ".summary.sha256";
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(new TraceSummary { SessionId = session, RecordCount = 1, TraceSha256 = traceHash, Completed = true }, CaseDescriptor.JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(summaryShaPath, SafePaths.HashFile(summaryPath) + "\n", Encoding.ASCII);
            return (trace, summaryPath, summaryShaPath);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
