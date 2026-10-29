using System.Security.Cryptography;
using System.Diagnostics;
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
        Run("only query and read rights are the live reader mask", LiveRightsMask);
        Run("Steam target path is rejected", SteamPathRejected);
        Run("Steam client absence is an actionable launch blocker", SteamClientAbsent);
        Run("Steam client must run under the same OS user", SteamClientUserIdentity);
        Run("QA launch rejects an incorrect working directory", QaLaunchWrongWorkingDirectory);
        Run("missing QA AppID sidecar is provisioned with exact bytes", QaAppIdSidecarProvisioned);
        Run("incorrect QA AppID sidecar is rejected without replacement", QaAppIdSidecarWrongContent);
        Run("live Steam DSR executable cannot be selected for QA launch", QaLiveSteamExecutableRejected);
        Run("exact isolated QA executable is selected", QaIsolatedExecutableSelected);
        Run("wrong process image path is rejected", ProcessImagePathMismatch);
        Run("wrong process image hash is rejected", ProcessImageHashMismatch);
        Run("Steam process identity is rejected", ProcessSteamPathRejected);
        Run("missing exact process fails with actionable code", MissingProcessIdentityRejected);
        Run("signature scanner rejects zero and multiple matches", SignatureMatchCountsFailClosed);
        Run("flag 11410410 resolves to the documented candidate bit", CeaselessFlagCandidateBit);
        Run("invalid pointer chain fails closed", InvalidPointerRejected);
        Run("NaN and implausible coordinates fail sanity checks", CoordinateSanityRejected);
        Run("invalid candidate map identity fails closed", CandidateMapIdentityRejected);
        Run("invalid event flag bank is rejected", InvalidEventFlagIndexRejected);
        Run("corrupt marker profile fails closed", CorruptMarkerProfileRejected);
        Run("marker profile for another executable is rejected", MarkerProfileExecutableMismatch);
        Run("marker profile for another runtime is rejected", MarkerProfileRuntimeMismatch);
        Run("marker profile for changed signatures is rejected", MarkerProfileSignatureMismatch);
        Run("pointer failure invalidates the marker profile", MarkerProfileInvalidation);
        Run("production telemetry source contains no process-write or injection API", ProcessWriteApiAudit);
        Run("trace output outside Temp is rejected", TraceOutsideTempRejected);
        Run("QA runtime without manifest binding fails closed", RuntimeMissingManifestBinding);
        Run("clean runtime cannot be used as the package-applied process target", CleanRuntimeRejectedForLiveTarget);
        Run("clean runtime manifest attests exact copied tree and QA subset", CleanRuntimeManifestRoundTrip);
        Run("QA launch sidecar is separately hashed and excluded from package runtime identity", PackageAppliedRuntimePreflight);
        Run("runtime tree mutation fails its manifest", RuntimeTreeMutationRejected);
        Run("reparse point in runtime inventory fails closed", RuntimeReparsePointRejected);
        Run("Steam runtime path is rejected without reading it", RuntimeSteamPathRejected);
        Run("non-approved isolated runtime path is rejected", RuntimePathMismatchRejected);
        Run("package identity change after application is rejected", RuntimePackageMismatchRejected);
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
        Check(ProcessAccessPolicy.PreflightRightsRequested == 0, "Static preflight unexpectedly requests process rights.");
        Check(ProcessAccessPolicy.AllowlistedReadOnlyRights == 0x1010, "Unexpected read-only rights mask.");
    }

    private static void RightsRejectWrite()
    {
        Throws<UnauthorizedAccessException>(() => ProcessAccessPolicy.Validate(0x1020));
        Throws<UnauthorizedAccessException>(() => ProcessAccessPolicy.Validate(0x1018));
    }

    private static void LiveRightsMask()
    {
        ProcessAccessPolicy.Validate(ProcessAccessPolicy.LiveReaderRightsRequested);
        Check(ProcessAccessPolicy.PreflightRightsRequested == 0, "Static preflight unexpectedly requests process rights.");
        Check(ProcessAccessPolicy.LiveReaderRightsRequested == 0x1010, "Live telemetry rights must be query-limited plus read-only memory.");
        Throws<UnauthorizedAccessException>(() => ProcessAccessPolicy.RequireExactLiveRights(ProcessAccessPolicy.QueryLimitedInformation));
        Throws<UnauthorizedAccessException>(() => ProcessAccessPolicy.RequireExactLiveRights(ProcessAccessPolicy.VirtualMemoryRead));
    }

    private static void SteamPathRejected()
    {
        Throws<InvalidDataException>(() => SafePaths.RejectSteamPath("C:\\Program Files (x86)\\Steam\\steamapps\\common\\DARK SOULS REMASTERED"));
    }

    private static void ProcessImagePathMismatch()
    {
        string expected = Path.Combine(Path.GetTempPath(), "isolated-qa", "DarkSoulsRemastered.exe");
        QaTelemetryException error = ThrowsAndReturn<QaTelemetryException>(() => ProcessIdentityPolicy.Verify(42,
            Path.Combine(Path.GetTempPath(), "other", "DarkSoulsRemastered.exe"), expected, new string('a', 64), new string('a', 64), DateTimeOffset.UtcNow));
        Check(error.Code == "QA_PROCESS_PATH_MISMATCH", "Wrong image path did not use the typed mismatch code.");
    }

    private static void ProcessImageHashMismatch()
    {
        string expected = Path.Combine(Path.GetTempPath(), "isolated-qa", "DarkSoulsRemastered.exe");
        QaTelemetryException error = ThrowsAndReturn<QaTelemetryException>(() => ProcessIdentityPolicy.Verify(42,
            expected, expected, new string('a', 64), new string('b', 64), DateTimeOffset.UtcNow));
        Check(error.Code == "QA_EXECUTABLE_HASH_MISMATCH", "Wrong image hash did not use the typed mismatch code.");
    }

    private static void ProcessSteamPathRejected()
    {
        string steam = @"C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS REMASTERED\DarkSoulsRemastered.exe";
        QaTelemetryException error = ThrowsAndReturn<QaTelemetryException>(() => ProcessIdentityPolicy.Verify(42,
            steam, steam, new string('a', 64), new string('a', 64), DateTimeOffset.UtcNow));
        Check(error.Code == "LIVE_STEAM_RUNTIME_REJECTED", "Steam image path was not explicitly rejected.");
    }

    private static void SteamClientAbsent()
    {
        SteamClientStatus result = SteamClientProbe.Evaluate([], QaRuntimePolicy.SteamExecutablePath, "S-1-5-21-qa-user");
        Check(!result.Ready && result.Code == "STEAM_CLIENT_NOT_RUNNING", "Absent Steam client did not block launch clearly.");
    }

    private static void SteamClientUserIdentity()
    {
        SteamProcessCandidate[] matching = [new(QaRuntimePolicy.SteamExecutablePath, "S-1-5-21-owner")];
        SteamClientStatus ready = SteamClientProbe.Evaluate(matching, QaRuntimePolicy.SteamExecutablePath, "S-1-5-21-owner");
        Check(ready.Ready && ready.Code == "STEAM_CLIENT_READY", "Same-user Steam client was not accepted.");
        SteamClientStatus mismatch = SteamClientProbe.Evaluate(matching, QaRuntimePolicy.SteamExecutablePath, "S-1-5-21-other");
        Check(!mismatch.Ready && mismatch.Code == "STEAM_CLIENT_USER_MISMATCH", "Different-user Steam client was not rejected.");
    }

    private static void QaLaunchWrongWorkingDirectory()
    {
        string executable = ExactBuildProfile.ExpectedExecutablePath;
        string runtimeRoot = Path.GetDirectoryName(executable)!;
        QaTelemetryException error = ThrowsAndReturn<QaTelemetryException>(() => QaLaunchPolicy.ValidateLaunchPaths(
            executable, Path.GetTempPath(), executable, runtimeRoot));
        Check(error.Code == "QA_LAUNCH_WORKING_DIRECTORY_MISMATCH", "Incorrect working directory did not fail with the typed code.");
    }

    private static void QaAppIdSidecarProvisioned()
    {
        string root = Path.Combine(Path.GetTempPath(), "ds1tr-qa-appid-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string executable = Path.Combine(root, "DarkSoulsRemastered.exe");
        File.WriteAllBytes(executable, [0x4d, 0x5a]);
        try
        {
            QaSteamAppIdStatus created = QaSteamAppIdSidecar.ProvisionOrVerify(root, executable, root);
            Check(created.Created && created.Contents == "570940", "Missing sidecar was not created as the DSR AppID.");
            Check(File.ReadAllBytes(created.Path).SequenceEqual(Encoding.ASCII.GetBytes("570940")), "Sidecar bytes include unexpected encoding or newline data.");
            Check(created.Sha256 == QaSteamAppIdSidecar.ExpectedSha256, "Sidecar SHA-256 does not match the pinned exact AppID bytes.");
            QaSteamAppIdStatus verified = QaSteamAppIdSidecar.ProvisionOrVerify(root, executable, root);
            Check(!verified.Created && verified.Sha256 == created.Sha256, "Existing correct sidecar was not independently verified.");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    private static void QaAppIdSidecarWrongContent()
    {
        string root = Path.Combine(Path.GetTempPath(), "ds1tr-qa-appid-wrong-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string executable = Path.Combine(root, "DarkSoulsRemastered.exe");
        string sidecar = Path.Combine(root, QaSteamAppIdSidecar.FileName);
        File.WriteAllBytes(executable, [0x4d, 0x5a]);
        byte[] wrongBytes = Encoding.ASCII.GetBytes("570940\r\n");
        File.WriteAllBytes(sidecar, wrongBytes);
        try
        {
            QaTelemetryException error = ThrowsAndReturn<QaTelemetryException>(() => QaSteamAppIdSidecar.ProvisionOrVerify(root, executable, root));
            Check(error.Code == "QA_STEAM_APPID_INVALID", "Wrong sidecar did not use an actionable typed failure.");
            Check(File.ReadAllBytes(sidecar).SequenceEqual(wrongBytes), "Incorrect existing sidecar was overwritten instead of preserved.");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    private static void QaLiveSteamExecutableRejected()
    {
        string live = @"C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS REMASTERED\DarkSoulsRemastered.exe";
        QaTelemetryException error = ThrowsAndReturn<QaTelemetryException>(() => QaLaunchPolicy.VerifyObservedProcessPath(live, ExactBuildProfile.ExpectedExecutablePath));
        Check(error.Code == "LIVE_STEAM_EXECUTABLE_REJECTED", "Live Steam DSR process path was not explicitly rejected.");
    }

    private static void QaIsolatedExecutableSelected()
    {
        string accepted = QaLaunchPolicy.VerifyObservedProcessPath(ExactBuildProfile.ExpectedExecutablePath, ExactBuildProfile.ExpectedExecutablePath);
        Check(accepted == ExactBuildProfile.ExpectedExecutablePath, "The exact QA executable path was not selected.");
        QaLaunchPolicy.ValidateLaunchPaths(ExactBuildProfile.ExpectedExecutablePath,
            Path.GetDirectoryName(ExactBuildProfile.ExpectedExecutablePath)!, ExactBuildProfile.ExpectedExecutablePath,
            Path.GetDirectoryName(ExactBuildProfile.ExpectedExecutablePath)!);
    }

    private static void MissingProcessIdentityRejected()
    {
        QaTelemetryException error = ThrowsAndReturn<QaTelemetryException>(() => ProcessIdentityPolicy.SelectExactlyOne([]));
        Check(error.Code == "QA_PROCESS_NOT_FOUND", "Missing process did not use an actionable status.");
        Throws<UnauthorizedAccessException>(() => ProcessAccessPolicy.Validate(0x1020));
    }

    private static void SignatureMatchCountsFailClosed()
    {
        Check(ExactBuildProfile.FindMatches([1, 2, 3], [8]).Length == 0, "Zero-match test fixture was incorrect.");
        Check(ExactBuildProfile.FindMatches([1, 2, 1, 2], [1, 2]).Length == 2, "Multi-match test fixture was incorrect.");
    }

    private static void CeaselessFlagCandidateBit()
    {
        (int offset, uint mask) = EventFlagAddressing.Resolve(11410410);
        Check(offset == 0x3c30 && mask == 0x20, $"Candidate flag address changed: offset=0x{offset:X}, mask=0x{mask:X}.");
    }

    private static void InvalidPointerRejected()
    {
        Throws<InvalidDataException>(() => CandidateTelemetryReader.ResolvePointerChain(new ZeroMemory(), 0x100000, 0));
    }

    private static void CoordinateSanityRejected()
    {
        Check(!CandidateTelemetryReader.IsPlausiblePosition(float.NaN, 1, 1), "NaN coordinate was accepted.");
        Check(!CandidateTelemetryReader.IsPlausiblePosition(100001, 1, 1), "Implausible coordinate was accepted.");
        Check(CandidateTelemetryReader.IsPlausiblePosition(1, -2, 3), "Plausible finite coordinates were rejected.");
    }

    private static void CandidateMapIdentityRejected()
    {
        Check(!CandidateTelemetryReader.IsPlausibleCandidateAreaId(0), "Unknown/sentinel map identity zero was accepted.");
        Check(!CandidateTelemetryReader.IsPlausibleCandidateAreaId(-1), "Negative map identity was accepted.");
        Check(CandidateTelemetryReader.IsPlausibleCandidateAreaId(101), "A plausible candidate area ID was rejected.");
    }

    private static void InvalidEventFlagIndexRejected()
    {
        Throws<InvalidDataException>(() => EventFlagAddressing.Resolve(91410410));
    }

    private static void CorruptMarkerProfileRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), "ds1tr-corrupt-profile-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{}", new UTF8Encoding(false));
            File.WriteAllText(path + ".sha256", SafePaths.HashFile(path) + "\n", Encoding.ASCII);
            MarkerProfileCheck result = MarkerProfileStore.LoadVerified(path, ExactBuildProfile.ExpectedExecutableSha256,
                ExactBuildProfile.ExpectedRuntimeDigest, ExactBuildProfile.ToolVersion, ExactBuildProfile.DefinitionSha256);
            Check(!result.Ok, "Corrupt marker profile unexpectedly verified.");
        }
        finally { TryDelete(path); TryDelete(path + ".sha256"); }
    }

    private static void MarkerProfileExecutableMismatch()
    {
        MarkerProfileDocument profile = ValidMarkerProfile();
        QaTelemetryException error = ThrowsAndReturn<QaTelemetryException>(() => MarkerProfileStore.Validate(profile,
            new string('0', 64), ExactBuildProfile.ExpectedRuntimeDigest, ExactBuildProfile.ToolVersion, ExactBuildProfile.DefinitionSha256));
        Check(error.Code == "MARKER_PROFILE_EXE_MISMATCH", "Wrong executable-bound profile used an unexpected status.");
    }

    private static void MarkerProfileRuntimeMismatch()
    {
        MarkerProfileDocument profile = ValidMarkerProfile();
        QaTelemetryException error = ThrowsAndReturn<QaTelemetryException>(() => MarkerProfileStore.Validate(profile,
            ExactBuildProfile.ExpectedExecutableSha256, new string('0', 64), ExactBuildProfile.ToolVersion, ExactBuildProfile.DefinitionSha256));
        Check(error.Code == "MARKER_PROFILE_RUNTIME_MISMATCH", "Wrong runtime-bound profile used an unexpected status.");
    }

    private static void MarkerProfileSignatureMismatch()
    {
        MarkerProfileDocument profile = ValidMarkerProfile();
        QaTelemetryException error = ThrowsAndReturn<QaTelemetryException>(() => MarkerProfileStore.Validate(profile,
            ExactBuildProfile.ExpectedExecutableSha256, ExactBuildProfile.ExpectedRuntimeDigest, ExactBuildProfile.ToolVersion, new string('0', 64)));
        Check(error.Code == "MARKER_PROFILE_SIGNATURE_MISMATCH", "Changed signature binding used an unexpected status.");
    }

    private static void MarkerProfileInvalidation()
    {
        string path = Path.Combine(Path.GetTempPath(), "ds1tr-invalidated-profile-" + Guid.NewGuid().ToString("N") + ".json");
        string invalid = path + ".invalid";
        try
        {
            File.WriteAllText(path, "{}", new UTF8Encoding(false));
            MarkerProfileStore.InvalidateAfterReadFailure(path, "synthetic invalid-pointer test");
            MarkerProfileCheck result = MarkerProfileStore.LoadVerified(path, ExactBuildProfile.ExpectedExecutableSha256,
                ExactBuildProfile.ExpectedRuntimeDigest, ExactBuildProfile.ToolVersion, ExactBuildProfile.DefinitionSha256);
            Check(!result.Ok && result.Message.Contains("invalidated", StringComparison.OrdinalIgnoreCase), "Invalidated profile remained loadable.");
        }
        finally { TryDelete(path); TryDelete(invalid); }
    }

    private static void ProcessWriteApiAudit()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        string[] forbidden =
        [
            string.Concat("WriteProcess", "Memory"), string.Concat("VirtualAlloc", "Ex"),
            string.Concat("VirtualProtect", "Ex"), string.Concat("CreateRemote", "Thread"),
            string.Concat("PROCESS_VM_", "WRITE"), string.Concat("PROCESS_VM_", "OPERATION"),
            string.Concat("DebugActive", "Process"), string.Concat("SetThread", "Context"),
            string.Concat("NtWriteVirtual", "Memory")
        ];
        IEnumerable<string> sources = Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "tests" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        foreach (string source in sources)
        {
            string text = File.ReadAllText(source);
            foreach (string api in forbidden)
                Check(!text.Contains(api, StringComparison.OrdinalIgnoreCase), "Forbidden process-write API text found in " + Path.GetFileName(source) + ".");
        }
    }

    private static void TraceOutsideTempRejected()
    {
        string outside = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "..", "outside-trace.jsonl"));
        Throws<InvalidDataException>(() => SafePaths.EnsureUnderTemp(outside));
    }

    private static void RuntimeMissingManifestBinding()
    {
        using var f = new Fixture();
        RuntimePreflightResult result = RuntimePreflight.Run(f.Descriptor);
        Check(!result.Ok && result.Code == "QA_RUNTIME_PREFLIGHT_BLOCKED", "Missing runtime attestations did not fail closed.");
        Check(result.Blockers.Any(b => b.StartsWith("QA_RUNTIME_MANIFEST_MISSING", StringComparison.Ordinal)), "Missing runtime attestation was not reported.");
        Check(!result.HasRequiredRuntimeAssets, "Unattested runtime was accepted.");
    }

    private static void CleanRuntimeRejectedForLiveTarget()
    {
        using var f = new Fixture();
        using var runtime = new RuntimeFixture();
        _ = runtime.AttestClean();
        f.Descriptor.Runtime!.RootPath = runtime.Policy.CleanRuntimeRoot;
        f.Descriptor.Runtime.ExecutablePath = Path.Combine(runtime.Policy.CleanRuntimeRoot, "DarkSoulsRemastered.exe");
        f.Descriptor.Runtime.ExecutableSha256 = SafePaths.HashFile(f.Descriptor.Runtime.ExecutablePath);
        RuntimePreflightResult result = RuntimePreflight.Run(f.Descriptor, runtime.Policy);
        Check(!result.Ok && result.Blockers.Any(blocker => blocker.StartsWith("QA_RUNTIME_PATH_MISMATCH", StringComparison.Ordinal)),
            "The clean baseline was accepted as the package-applied process target.");
    }

    private static void CleanRuntimeManifestRoundTrip()
    {
        using var f = new RuntimeFixture();
        RuntimeManifestWriteResult written = f.AttestClean();
        RuntimeManifestVerification verified = RuntimeManifestService.VerifyManifest(
            written.ManifestPath, f.Policy.CleanRuntimeRoot, "QA_RUNTIME_CLEAN_BASELINE", written.ManifestSha256, f.Policy);
        Check(verified.Ok, verified.Message);
        Check(written.Manifest.FileCount >= 1000, "Runtime inventory was unexpectedly small.");
        Check(written.Manifest.QaDataSubsetFileCount == 1, "Established QA snapshot subset was not bound.");
        Check(written.Manifest.CanonicalDigest == SafePaths.InventoryDigest(written.Manifest.Files), "Canonical inventory digest is inconsistent.");
        Check(written.Manifest.TopLevelArchivePairs.Length == 0, "Synthetic loose-tree fixture unexpectedly has archive pairs.");
    }

    private static void PackageAppliedRuntimePreflight()
    {
        using var package = new Fixture();
        using var runtime = new RuntimeFixture();
        RuntimeManifestWriteResult clean = runtime.AttestClean();
        RuntimeApplyResult applied = RuntimeManifestService.ApplyCertifiedPackage(
            package.Descriptor, runtime.Policy.CleanManifestPath, runtime.Policy.ModifiedManifestPath, runtime.Policy);
        Check(applied.Ok, applied.Message);
        Check(applied.ChangedFileCount == 1, "Expected exactly one package-owned changed path.");

        RuntimeManifestVerification verified = RuntimeManifestService.VerifyPackageRuntime(
            package.Descriptor,
            runtime.Policy.CleanManifestPath,
            runtime.Policy.ModifiedManifestPath,
            clean.ManifestSha256,
            applied.ManifestSha256,
            runtime.Policy);
        Check(verified.Ok, verified.Message);
        Check(verified.Manifest!.ChangedFiles.Select(file => file.Path).SequenceEqual(["event/fixture.bin"], StringComparer.Ordinal), "Changed-file list includes a path outside the selected package.");
        Check(SafePaths.HashFile(Path.Combine(runtime.Policy.CleanRuntimeRoot, "event", "fixture.bin")) != SafePaths.HashFile(Path.Combine(runtime.Policy.ModifiedRuntimeRoot, "event", "fixture.bin")), "Clean baseline was modified or package was not applied.");
        Check(SafePaths.HashFile(Path.Combine(runtime.SourceRoot, "event", "fixture.bin")) == SafePaths.HashFile(Path.Combine(runtime.Policy.CleanRuntimeRoot, "event", "fixture.bin")), "Package application modified the read-only source runtime.");

        string exe = Path.Combine(runtime.Policy.ModifiedRuntimeRoot, "DarkSoulsRemastered.exe");
        package.Descriptor.Runtime = new RuntimeLock
        {
            RootPath = runtime.Policy.ModifiedRuntimeRoot,
            ExecutablePath = exe,
            ExecutableSha256 = SafePaths.HashFile(exe),
            RuntimeProfileStatus = "UNVERIFIED",
            CleanRootPath = runtime.Policy.CleanRuntimeRoot,
            CleanManifestPath = runtime.Policy.CleanManifestPath,
            CleanManifestSha256 = clean.ManifestSha256,
            ModifiedManifestPath = runtime.Policy.ModifiedManifestPath,
            ModifiedManifestSha256 = applied.ManifestSha256
        };
        RuntimePreflightResult preflight = RuntimePreflight.Run(package.Descriptor, runtime.Policy);
        Check(preflight.Ok && preflight.Code == "QA_RUNTIME_COMPLETE", string.Join("; ", preflight.Blockers));
        Check(preflight.HasRequiredRuntimeAssets, "Complete runtime assets were not reported.");
        Check(preflight.CleanManifestSha256 == clean.ManifestSha256 && preflight.ModifiedManifestSha256 == applied.ManifestSha256, "Preflight did not return both locked manifest hashes.");
        Check(package.Descriptor.TelemetryProfile!.Status == "UNVERIFIED", "Runtime preflight promoted the telemetry marker profile.");

        string sidecarPath = Path.Combine(runtime.Policy.ModifiedRuntimeRoot, QaSteamAppIdSidecar.FileName);
        QaSteamAppIdStatus sidecar = QaSteamAppIdSidecar.ProvisionOrVerify(runtime.Policy.ModifiedRuntimeRoot, exe, runtime.Policy.ModifiedRuntimeRoot);
        Check(sidecar.Created && sidecar.Sha256 == QaSteamAppIdSidecar.ExpectedSha256, "QA launch sidecar was not created and separately hashed.");
        RuntimeManifestVerification withSidecar = RuntimeManifestService.VerifyPackageRuntime(
            package.Descriptor, runtime.Policy.CleanManifestPath, runtime.Policy.ModifiedManifestPath,
            clean.ManifestSha256, applied.ManifestSha256, runtime.Policy);
        Check(withSidecar.Ok, withSidecar.Message);
        Check(withSidecar.Manifest!.CanonicalDigest == applied.RuntimeDigest, "QA launch sidecar changed the immutable runtime digest.");
        Check(!withSidecar.Manifest.Files.Any(file => file.Path == QaSteamAppIdSidecar.FileName), "QA launch sidecar leaked into the immutable runtime inventory.");

        File.WriteAllBytes(sidecarPath, Encoding.ASCII.GetBytes("570940\n"));
        RuntimeManifestVerification badSidecar = RuntimeManifestService.VerifyManifest(
            runtime.Policy.ModifiedManifestPath, runtime.Policy.ModifiedRuntimeRoot, "QA_RUNTIME_MODDED_DIGEST",
            applied.ManifestSha256, runtime.Policy);
        Check(!badSidecar.Ok && badSidecar.Code == "QA_RUNTIME_MANIFEST_INVALID" && badSidecar.Message.Contains("AppID", StringComparison.OrdinalIgnoreCase),
            "Changed launch sidecar was not rejected independently of the package runtime identity.");
    }

    private static void RuntimeTreeMutationRejected()
    {
        using var f = new RuntimeFixture();
        RuntimeManifestWriteResult written = f.AttestClean();
        File.AppendAllText(Path.Combine(f.Policy.CleanRuntimeRoot, "event", "fixture.bin"), "changed");
        RuntimeManifestVerification result = RuntimeManifestService.VerifyManifest(
            written.ManifestPath, f.Policy.CleanRuntimeRoot, "QA_RUNTIME_CLEAN_BASELINE", written.ManifestSha256, f.Policy);
        Check(!result.Ok && result.Code == "QA_RUNTIME_MANIFEST_INVALID", "Mutated runtime tree unexpectedly verified.");
    }

    private static void RuntimeReparsePointRejected()
    {
        using var f = new RuntimeFixture();
        string link = Path.Combine(f.SourceRoot, "map", "linked-assets");
        string target = Path.Combine(f.Root, "external-link-target");
        Directory.CreateDirectory(target);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /c mklink /J \"" + link + "\" \"" + target + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        }) ?? throw new IOException("Could not start the Windows junction test helper.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not create an isolated test junction: " + process.StandardError.ReadToEnd());
        Check((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "Test link was not created as a reparse point.");
        Throws<InvalidDataException>(() => SafePaths.Inventory(f.SourceRoot, rejectHardLinks: true));
    }

    private static void RuntimeSteamPathRejected()
    {
        using var f = new Fixture();
        f.Descriptor.Runtime!.RootPath = @"C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS REMASTERED";
        f.Descriptor.Runtime.ExecutablePath = Path.Combine(f.Descriptor.Runtime.RootPath, "DarkSoulsRemastered.exe");
        RuntimePreflightResult result = RuntimePreflight.Run(f.Descriptor);
        Check(!result.Ok && result.Code == "LIVE_RUNTIME_REJECTED", "Live Steam runtime path was not rejected before filesystem inspection.");
    }

    private static void RuntimePathMismatchRejected()
    {
        using var f = new Fixture();
        RuntimePreflightResult result = RuntimePreflight.Run(f.Descriptor);
        Check(!result.Ok && result.Blockers.Any(b => b.StartsWith("QA_RUNTIME_PATH_MISMATCH", StringComparison.Ordinal)), "An unapproved isolated runtime path was not rejected.");
    }

    private static void RuntimePackageMismatchRejected()
    {
        using var package = new Fixture();
        using var runtime = new RuntimeFixture();
        RuntimeManifestWriteResult clean = runtime.AttestClean();
        RuntimeApplyResult applied = RuntimeManifestService.ApplyCertifiedPackage(
            package.Descriptor, runtime.Policy.CleanManifestPath, runtime.Policy.ModifiedManifestPath, runtime.Policy);
        Check(applied.Ok, applied.Message);
        package.Descriptor.Package!.TreeSha256 = new string('0', 64);
        RuntimeManifestVerification result = RuntimeManifestService.VerifyPackageRuntime(
            package.Descriptor,
            runtime.Policy.CleanManifestPath,
            runtime.Policy.ModifiedManifestPath,
            clean.ManifestSha256,
            applied.ManifestSha256,
            runtime.Policy);
        Check(!result.Ok && result.Code == "QA_RUNTIME_PACKAGE_IDENTITY_MISMATCH", "Changed package identity was accepted by runtime verification.");
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
        var paths = f.WriteHeaderOnlyTrace("unverified-live", "READ_ONLY_GAME_TELEMETRY", "UNVERIFIED", new string('b', 64));
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

    private static T ThrowsAndReturn<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException("Expected exception " + typeof(T).Name + ".");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static MarkerProfileDocument ValidMarkerProfile()
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        return new MarkerProfileDocument
        {
            ProfileId = "fixture-profile",
            Status = "VERIFIED",
            ExecutableSha256 = ExactBuildProfile.ExpectedExecutableSha256,
            RuntimeDigest = ExactBuildProfile.ExpectedRuntimeDigest,
            ToolVersion = ExactBuildProfile.ToolVersion,
            SignatureDefinitionSha256 = ExactBuildProfile.DefinitionSha256,
            CreatedAtUtc = now,
            OwnerConfirmedMapAreaIds = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Firelink Shrine"] = 101,
                ["Northern Undead Asylum"] = 102,
                ["Demon Ruins"] = 150
            },
            Validation = new MarkerValidationEvidence
            {
                OwnerConfirmedKnownMap = true,
                StationaryPositionStable = true,
                StationarySpread = 0.01,
                PlayerMovementObserved = true,
                PlayerMovementDistance = 2,
                KnownTransitionObserved = true,
                TransitionFromAreaId = 101,
                TransitionToAreaId = 102,
                KnownFalseFlagConfirmed = true,
                KnownFalseFlagId = 11410410,
                KnownFalseEvidence = "fixture known false",
                KnownFalseFlagRead = false,
                KnownTrueFlagConfirmed = true,
                KnownTrueFlagId = 11000100,
                KnownTrueEvidence = "fixture known true",
                KnownTrueFlagRead = true,
                ProcessId = 1,
                ProcessCreatedUtc = now,
                ValidatedAtUtc = now
            }
        };
    }

    private sealed class ZeroMemory : IReadOnlyMemory
    {
        public byte[] Read(ulong address, int length) => new byte[length];
    }

    private static void Reseal(string tracePath, string summaryPath, string summaryShaPath, string session)
    {
        string hash = SafePaths.HashFile(tracePath);
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(new TraceSummary { SessionId = session, RecordCount = File.ReadAllLines(tracePath).Length, TraceSha256 = hash, Completed = true }, CaseDescriptor.JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(summaryShaPath, SafePaths.HashFile(summaryPath) + "\n", Encoding.ASCII);
    }

    private sealed class RuntimeFixture : IDisposable
    {
        private static readonly string[] RequiredDirectories =
        [
            "chr", "event", "facegen", "font", "map", "menu", "movww", "msg", "mtd", "obj", "other",
            "param", "paramdef", "parts", "remo", "script", "sfx", "shader", "sound"
        ];

        private static readonly string[] RequiredSupportFiles =
        [
            "DarkSoulsRemastered.exe", "binkw64.dll", "fmodex64.dll", "fmod_event64.dll",
            "fmod_event_net64.dll", "steam_api64.dll", "xinput1_3.dll"
        ];

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ds1tr-runtime-manifest-test-" + Guid.NewGuid().ToString("N"));
        public string SourceRoot => Path.Combine(Root, "source-runtime");
        public string QaSubsetRoot => Path.Combine(Root, "qa-data-subset");
        public string AppManifestPath => Path.Combine(Root, "appmanifest_570940.acf");
        public RuntimePolicy Policy { get; }

        public RuntimeFixture()
        {
            string cleanRoot = Path.Combine(Root, "clean-runtime");
            string modifiedRoot = Path.Combine(Root, "package-runtime");
            string attestationRoot = Path.Combine(Root, "attestations");
            Policy = new RuntimePolicy(
                SourceRoot,
                AppManifestPath,
                QaSubsetRoot,
                cleanRoot,
                modifiedRoot,
                attestationRoot,
                Path.Combine(attestationRoot, "clean.json"),
                Path.Combine(attestationRoot, "modified.json"));

            foreach (string directory in RequiredDirectories)
            {
                string path = Path.Combine(SourceRoot, directory);
                Directory.CreateDirectory(path);
                File.WriteAllBytes(Path.Combine(path, "fixture.dat"), [(byte)directory.Length, 0x5a]);
            }
            File.WriteAllBytes(Path.Combine(SourceRoot, "event", "fixture.bin"), [9, 8, 7]);
            for (int i = 0; i < 1000; i++)
                File.WriteAllBytes(Path.Combine(SourceRoot, "map", $"fixture-{i:D4}.bin"), [(byte)(i % 251), (byte)(i % 127)]);
            for (int i = 0; i < RequiredSupportFiles.Length; i++)
                File.WriteAllBytes(Path.Combine(SourceRoot, RequiredSupportFiles[i]), [(byte)(40 + i), 0x11]);

            Directory.CreateDirectory(QaSubsetRoot);
            Directory.CreateDirectory(Path.Combine(QaSubsetRoot, "event"));
            File.Copy(Path.Combine(SourceRoot, "event", "fixture.bin"), Path.Combine(QaSubsetRoot, "event", "fixture.bin"));
            CopyTree(SourceRoot, cleanRoot);

            long bytes = SafePaths.Inventory(SourceRoot, rejectHardLinks: true).Sum(file => file.Size);
            File.WriteAllText(AppManifestPath,
                "\"appid\" \"570940\"\n" +
                "\"buildid\" \"10943698\"\n" +
                "\"SizeOnDisk\" \"" + bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"\n" +
                "\"InstalledDepots\"\n{\n" +
                "  \"570941\" { \"manifest\" \"2900566705832487094\" }\n" +
                "  \"570943\" { \"manifest\" \"2385633508013979570\" }\n}\n",
                new UTF8Encoding(false));
        }

        public RuntimeManifestWriteResult AttestClean() => RuntimeManifestService.AttestCleanBaseline(Policy);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }

        private static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (FileDigest file in SafePaths.Inventory(source, rejectHardLinks: true))
            {
                string destination = Path.Combine(target, file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(source, file.Path.Replace('/', Path.DirectorySeparatorChar)), destination);
            }
        }
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
                Provenance = provenance, TelemetryStatus = status,
                ProfileSha256 = status == "VERIFIED" ? profileSha : null,
                ObservedExecutablePath = Descriptor.Runtime.ExecutablePath,
                ProcessId = provenance == "READ_ONLY_GAME_TELEMETRY" ? 42 : null,
                ProcessCreatedUtc = provenance == "READ_ONLY_GAME_TELEMETRY" ? DateTimeOffset.UtcNow.ToString("O") : null,
                ProcessRightsRequested = provenance == "READ_ONLY_GAME_TELEMETRY" ? ReadOnlyGameProcess.ExactReadRights : ProcessAccessPolicy.PreflightRightsRequested,
                RuntimeDigest = provenance == "READ_ONLY_GAME_TELEMETRY" ? ExactBuildProfile.ExpectedRuntimeDigest : "",
                ToolVersion = ExactBuildProfile.ToolVersion
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
