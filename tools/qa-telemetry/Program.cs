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
                    Console.WriteLine(JsonSerializer.Serialize(new { status = package.Code, package.Message, processRightsRequested = ProcessAccessPolicy.RightsRequestedByThisBuild }, CaseDescriptor.JsonOptions));
                    return 2;
                }
                RuntimePreflightResult runtime = RuntimePreflight.Run(descriptor);
                ProcessAccessPolicy.Validate(ProcessAccessPolicy.RightsRequestedByThisBuild);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = runtime.Code,
                    package = new { package.TreeSha256, package.RunManifestSha256, package.FileCount, package.TotalBytes },
                    runtime.Blockers,
                    runtime.ExecutableSha256,
                    runtime.HasRequiredRuntimeAssets,
                    runtime.CleanManifestSha256,
                    runtime.ModifiedManifestSha256,
                    processRightsRequested = ProcessAccessPolicy.RightsRequestedByThisBuild,
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
        Console.WriteLine("QaTelemetry 0.2.0 (standalone development tool)");
        Console.WriteLine("  verify-case --case <descriptor.json>");
        Console.WriteLine("  attest-runtime");
        Console.WriteLine("  apply-package --case <descriptor.json>");
        Console.WriteLine("  preflight --case <descriptor.json>");
        Console.WriteLine("  trace-import --case <descriptor.json> --trace <trace.jsonl> --summary <summary.json> --summary-sha <summary.sha256>");
        Console.WriteLine("This build never launches or attaches to a game process. Live telemetry is disabled until runtime and offsets are independently verified.");
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
