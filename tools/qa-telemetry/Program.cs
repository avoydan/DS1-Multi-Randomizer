using System.Text.Json;

namespace QaTelemetry;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h") return Help();
            string command = args[0];
            Dictionary<string, string> options = ParseOptions(args.Skip(1).ToArray());

            if (command == "audit-build")
            {
                string target = options.Count == 0 ? "ceaseless" : Need(options, "target");
                if (options.Keys.Any(key => key != "target") || target is not ("clean" or "ceaseless"))
                    throw new ArgumentException("audit-build accepts only optional --target clean|ceaseless.");
                string executable = target == "clean" ? ExactBuildProfile.CalibrationExecutablePath : ExactBuildProfile.ExpectedExecutablePath;
                ExactBuildAuditResult audit = ExactBuildProfile.Audit(executable);
                Console.WriteLine(JsonSerializer.Serialize(audit, CaseDescriptor.JsonOptions));
                return audit.Status == "EXACT_BUILD_SIGNATURES_UNVERIFIED" ? 3 : 0;
            }

            if (command == "verify-calibration-runtime")
            {
                if (options.Count != 0) throw new ArgumentException("verify-calibration-runtime accepts no options and checks only the locked clean runtime.");
                RuntimeManifestVerification runtime = RuntimeManifestService.VerifyCleanCalibrationRuntime();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = runtime.Code,
                    runtime.Message,
                    runtime.ManifestSha256,
                    runtime.Manifest?.CanonicalDigest,
                    runtime.Manifest?.ExecutableSha256,
                    runtime.Manifest?.FileCount,
                    runtime.Manifest?.TotalBytes,
                    runtimeRoot = QaRuntimePolicy.CleanRuntimeRoot,
                    packageApplied = false,
                    gameLaunched = false
                }, CaseDescriptor.JsonOptions));
                return runtime.Ok ? 0 : 3;
            }

            if (command == "launch-calibration")
            {
                if (options.Count != 0) throw new ArgumentException("launch-calibration accepts no options and launches only the locked clean QA runtime.");
                QaGameLaunchResult launch = QaGameLauncher.LaunchCalibration();
                Console.WriteLine(JsonSerializer.Serialize(launch, CaseDescriptor.JsonOptions));
                return launch.ProcessPathVerified ? 0 : 6;
            }

            if (command == "calibrate-markers")
            {
                if (options.Count != 0) throw new ArgumentException("calibrate-markers accepts no options; it is locked to the clean vanilla QA runtime.");
                return MarkerGuide.Run();
            }

            if (command == "verify-profile")
            {
                if (!options.TryGetValue("profile", out string? profilePath) || options.Keys.Any(key => key != "profile"))
                    throw new ArgumentException("verify-profile requires only --profile <executable-profile.json>.");
                MarkerProfileCheck profile = MarkerProfileStore.LoadVerified(profilePath, ExactBuildProfile.ExpectedExecutableSha256,
                    ExactBuildProfile.ToolVersion, ExactBuildProfile.DefinitionSha256);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = profile.Code,
                    profile.Message,
                    profile.ProfileSha256,
                    profileId = profile.Profile?.ProfileId,
                    executableProfileId = profile.Profile?.ExecutableProfileId,
                    executableSha256 = profile.Profile?.ExecutableSha256,
                    toolVersion = profile.Profile?.ToolVersion,
                    telemetryDefinitionSha256 = profile.Profile?.TelemetryDefinitionSha256
                }, CaseDescriptor.JsonOptions));
                return profile.Ok ? 0 : 4;
            }

            if (command == "launch-qa")
            {
                if (!options.TryGetValue("case", out string? launchCase) || options.Keys.Any(key => key != "case"))
                    throw new ArgumentException("launch-qa requires --case <descriptor.json> and accepts no other options.");
                CaseDescriptor launchDescriptor = CaseDescriptor.Load(launchCase);
                QaGameLaunchResult launch = QaGameLauncher.Launch(launchDescriptor);
                Console.WriteLine(JsonSerializer.Serialize(launch, CaseDescriptor.JsonOptions));
                return launch.ProcessPathVerified ? 0 : 6;
            }

            if (command == "attest-runtime")
            {
                if (options.Count != 0) throw new ArgumentException("attest-runtime uses repository-locked QA paths and takes no options.");
                RuntimeManifestWriteResult result = RuntimeManifestService.AttestCleanBaseline(QaRuntimePolicy.Default);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = "QA_RUNTIME_CLEAN_ATTESTED",
                    result.ManifestPath,
                    result.ManifestSha256,
                    result.Manifest.CanonicalDigest,
                    result.Manifest.FileCount,
                    result.Manifest.TotalBytes,
                    result.Manifest.ExecutableSha256,
                    gameLaunched = false
                }, CaseDescriptor.JsonOptions));
                return 0;
            }

            bool needsCase = command is "verify-case" or "preflight" or "trace-import" or "apply-package";
            if (!needsCase) throw new ArgumentException("Unknown command: " + command);
            if (!options.TryGetValue("case", out string? casePath)) throw new ArgumentException("Required option: --case <descriptor.json>");
            CaseDescriptor descriptor = CaseDescriptor.Load(casePath);

            if (command == "verify-case")
            {
                PackageVerification package = PackageVerifier.Verify(descriptor);
                Console.WriteLine(JsonSerializer.Serialize(package, CaseDescriptor.JsonOptions));
                return package.Ok ? 0 : 2;
            }

            if (command == "preflight")
            {
                PackageVerification package = PackageVerifier.Verify(descriptor);
                if (!package.Ok)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { status = package.Code, package.Message, processRightsRequested = ProcessAccessPolicy.PreflightRightsRequested }, CaseDescriptor.JsonOptions));
                    return 2;
                }
                RuntimePreflightResult runtime = RuntimePreflight.Run(descriptor);
                ProcessAccessPolicy.Validate(ProcessAccessPolicy.PreflightRightsRequested);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = runtime.Code,
                    package = new { package.TreeSha256, package.RunManifestSha256, package.FileCount, package.TotalBytes },
                    runtime.Blockers,
                    runtime.ExecutableSha256,
                    runtime.HasRequiredRuntimeAssets,
                    runtime.CleanManifestSha256,
                    runtime.ModifiedManifestSha256,
                    processRightsRequested = ProcessAccessPolicy.PreflightRightsRequested,
                    gameLaunched = false,
                    telemetryStatus = "UNVERIFIED"
                }, CaseDescriptor.JsonOptions));
                return runtime.Ok ? 0 : 3;
            }

            if (command == "apply-package")
            {
                if (options.Keys.Any(key => key != "case")) throw new ArgumentException("apply-package accepts only --case.");
                RuntimePolicy policy = QaRuntimePolicy.Default;
                RuntimeApplyResult result = RuntimeManifestService.ApplyCertifiedPackage(
                    descriptor,
                    policy.CleanManifestPath,
                    policy.ModifiedManifestPath,
                    policy);
                Console.WriteLine(JsonSerializer.Serialize(result, CaseDescriptor.JsonOptions));
                return result.Ok ? 0 : 5;
            }

            if (command == "record")
            {
                if (!options.TryGetValue("case", out string? recordCase) || !options.TryGetValue("profile", out string? profilePath) ||
                    options.Keys.Any(key => key is not ("case" or "profile" or "trace")))
                    throw new ArgumentException("record requires --case <descriptor.json> --profile <verified-profile.json> and optionally --trace <temp-path.jsonl>.");
                CaseDescriptor recordDescriptor = CaseDescriptor.Load(recordCase);
                return TraceRecorder.Run(recordDescriptor, profilePath, options.GetValueOrDefault("trace"));
            }

            if (command == "trace-import")
            {
                string trace = Need(options, "trace");
                string summary = Need(options, "summary");
                string summarySha = Need(options, "summary-sha");
                TraceImportResult result = TraceImporter.Import(descriptor, trace, summary, summarySha);
                Console.WriteLine(JsonSerializer.Serialize(result, CaseDescriptor.JsonOptions));
                return result.GameplayConfirmed ? 0 : result.Status == "SYNTHETIC_TEST_PASS" ? 0 : 4;
            }

            throw new ArgumentException("Unknown command: " + command);
        }
        catch (QaTelemetryException ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = ex.Code, error = ex.GetType().Name, message = ex.Message }, CaseDescriptor.JsonOptions));
            return ex.Code == "PACKAGE_IDENTITY_MISMATCH" ? 2 : 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "FAIL_CLOSED", error = ex.GetType().Name, message = ex.Message }, CaseDescriptor.JsonOptions));
            return 10;
        }
    }

    private static int Help()
    {
        Console.WriteLine("QaTelemetry " + ExactBuildProfile.ToolVersion + " (standalone development tool)");
        Console.WriteLine("  audit-build");
        Console.WriteLine("  verify-calibration-runtime");
        Console.WriteLine("  launch-calibration");
        Console.WriteLine("  calibrate-markers");
        Console.WriteLine("  verify-profile --profile <executable-profile.json>");
        Console.WriteLine("  audit-build [--target clean|ceaseless]");
        Console.WriteLine("  launch-qa --case <descriptor.json>");
        Console.WriteLine("  record --case <descriptor.json> --profile <verified-profile.json> [--trace <temp-path.jsonl>]");
        Console.WriteLine("  verify-case --case <descriptor.json>");
        Console.WriteLine("  attest-runtime");
        Console.WriteLine("  apply-package --case <descriptor.json>");
        Console.WriteLine("  preflight --case <descriptor.json>");
        Console.WriteLine("  trace-import --case <descriptor.json> --trace <trace.jsonl> --summary <summary.json> --summary-sha <summary.sha256>");
        Console.WriteLine("audit-build reads the locked executable statically. No game process is opened by that command.");
        Console.WriteLine("calibrate-markers reads only the clean vanilla QA process after its pinned manifest verification; it never opens the package-applied runtime.");
        Console.WriteLine("launch-qa starts only the exact isolated executable after same-user Steam and runtime verification, then verifies its process image path.");
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                throw new ArgumentException("Options must use --name value form.");
            if (!options.TryAdd(args[i][2..], args[++i])) throw new ArgumentException("Duplicate option: " + args[i - 1]);
        }
        return options;
    }

    private static string Need(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out string? value) ? value : throw new ArgumentException("Required option: --" + key);
}
