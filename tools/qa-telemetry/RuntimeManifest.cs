using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QaTelemetry;

public sealed class RuntimeTreeManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string AttestationClass { get; set; } = "LOCAL_RUNTIME_ATTESTATION";
    public string Kind { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string SourceRootPath { get; set; } = "";
    public string RuntimeRootPath { get; set; } = "";
    public string CreatedAtUtc { get; set; } = "";
    public string SourceAppId { get; set; } = "";
    public string SourceBuildId { get; set; } = "";
    public Dictionary<string, string> SourceDepotManifests { get; set; } = new(StringComparer.Ordinal);
    public string SourceAppManifestSha256 { get; set; } = "";
    public string ExecutableSha256 { get; set; } = "";
    public string CanonicalDigest { get; set; } = "";
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public string[] TopLevelArchivePairs { get; set; } = [];
    public string[] RequiredDirectories { get; set; } = [];
    public string[] RequiredSupportFiles { get; set; } = [];
    public Dictionary<string, int> AssetCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int QaDataSubsetFileCount { get; set; }
    public string QaDataSubsetDigest { get; set; } = "";
    public string? CleanManifestSha256 { get; set; }
    public string? PackageTreeSha256 { get; set; }
    public string? PackageManifestSha256 { get; set; }
    public FileDigest[] ChangedFiles { get; set; } = [];
    public FileDigest[] Files { get; set; } = [];
}

public sealed record RuntimeManifestWriteResult(string ManifestPath, string ManifestSha256, RuntimeTreeManifest Manifest);
public sealed record RuntimeManifestVerification(bool Ok, string Code, string Message, RuntimeTreeManifest? Manifest = null, string? ManifestSha256 = null);
public sealed record RuntimeApplyResult(bool Ok, string Code, string Message, string? RuntimeRoot = null, string? RuntimeDigest = null, string? ManifestSha256 = null, int ChangedFileCount = 0);

public sealed record RuntimePolicy(
    string SteamSourceRoot,
    string SteamAppManifestPath,
    string QaDataSnapshotRoot,
    string CleanRuntimeRoot,
    string ModifiedRuntimeRoot,
    string AttestationDirectory,
    string CleanManifestPath,
    string ModifiedManifestPath);

public static class QaRuntimePolicy
{
    public const string SteamSourceRoot = @"C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS REMASTERED";
    public const string SteamExecutablePath = @"C:\Program Files (x86)\Steam\steam.exe";
    public const string SteamAppManifestPath = @"C:\Program Files (x86)\Steam\steamapps\appmanifest_570940.acf";
    public const string QaDataSnapshotRoot = @"C:\Users\Adam\ds1tr-qa\game-inst\DARK SOULS REMASTERED";
    public const string CleanRuntimeRoot = @"C:\Users\Adam\ds1tr-qa\runtime\DARK SOULS REMASTERED";
    public const string ModifiedRuntimeRoot = @"C:\Users\Adam\ds1tr-qa\runtime\DARK SOULS REMASTERED - CEASELESS QA";
    public const string AttestationDirectory = @"C:\Users\Adam\ds1tr-qa\runtime\attestations";
    public const string CleanManifestPath = @"C:\Users\Adam\ds1tr-qa\runtime\attestations\ceaseless-clean-runtime.v1.json";
    public const string ModifiedManifestPath = @"C:\Users\Adam\ds1tr-qa\runtime\attestations\ceaseless-package-runtime.v1.json";

    public static RuntimePolicy Default { get; } = new(
        SteamSourceRoot,
        SteamAppManifestPath,
        QaDataSnapshotRoot,
        CleanRuntimeRoot,
        ModifiedRuntimeRoot,
        AttestationDirectory,
        CleanManifestPath,
        ModifiedManifestPath);
}

public static class RuntimeManifestService
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

    public static RuntimeManifestWriteResult AttestCleanBaseline(RuntimePolicy policy, string? outputPath = null)
    {
        string sourceRoot = SafePaths.NormalizeFullPath(policy.SteamSourceRoot);
        string runtimeRoot = SafePaths.NormalizeFullPath(policy.CleanRuntimeRoot);
        string manifestPath = SafePaths.NormalizeFullPath(outputPath ?? policy.CleanManifestPath);
        if (!PathEquals(sourceRoot, SafePaths.NormalizeFullPath(policy.SteamSourceRoot)))
            throw new InvalidDataException("Unexpected source runtime path.");
        if (!PathEquals(runtimeRoot, SafePaths.NormalizeFullPath(policy.CleanRuntimeRoot)))
            throw new InvalidDataException("Unexpected clean QA runtime path.");
        RejectLiveRuntimeSelection(runtimeRoot, sourceRoot);
        EnsureManifestPath(policy, manifestPath, policy.CleanRuntimeRoot, policy.ModifiedRuntimeRoot);

        var sourceInventory = SafePaths.Inventory(sourceRoot, rejectHardLinks: true).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var runtimeInventory = SafePaths.Inventory(runtimeRoot, rejectHardLinks: true).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        RequireSameInventory(sourceInventory, runtimeInventory, "The isolated runtime differs from the read-only installed source.");
        ValidateRuntimeShape(runtimeRoot, runtimeInventory);

        string appManifestPath = SafePaths.NormalizeFullPath(policy.SteamAppManifestPath);
        SafePaths.EnsureNoReparseAncestors(appManifestPath, leafMustExist: true);
        string appManifestText = File.ReadAllText(appManifestPath);
        string appId = ReadQuotedValue(appManifestText, "appid");
        string buildId = ReadQuotedValue(appManifestText, "buildid");
        if (appId != "570940") throw new InvalidDataException("Steam appmanifest does not identify DSR app 570940.");
        if (buildId != "10943698") throw new InvalidDataException("Steam DSR build ID differs from the approved QA runtime build.");
        var depots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["570941"] = ReadDepotManifest(appManifestText, "570941"),
            ["570943"] = ReadDepotManifest(appManifestText, "570943")
        };
        long appManifestBytes = long.Parse(ReadQuotedValue(appManifestText, "SizeOnDisk"), System.Globalization.CultureInfo.InvariantCulture);
        long runtimeBytes = runtimeInventory.Sum(x => x.Size);
        if (runtimeBytes != appManifestBytes)
            throw new InvalidDataException("Runtime byte total differs from the local Steam appmanifest SizeOnDisk.");

        FileDigest[] qaSubset = VerifyQaSubset(policy.QaDataSnapshotRoot, sourceInventory);
        string[] archivePairs = FindTopLevelArchivePairs(runtimeRoot);
        var manifest = new RuntimeTreeManifest
        {
            Kind = "QA_RUNTIME_CLEAN_BASELINE",
            SourceType = "LOCAL_STEAM_INSTALLED_RUNTIME_COPY",
            SourceRootPath = sourceRoot,
            RuntimeRootPath = runtimeRoot,
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            SourceAppId = appId,
            SourceBuildId = buildId,
            SourceDepotManifests = depots,
            SourceAppManifestSha256 = SafePaths.HashFile(appManifestPath),
            ExecutableSha256 = HashInventoryEntry(runtimeInventory, "DarkSoulsRemastered.exe"),
            CanonicalDigest = SafePaths.InventoryDigest(runtimeInventory),
            FileCount = runtimeInventory.Length,
            TotalBytes = runtimeBytes,
            TopLevelArchivePairs = archivePairs,
            RequiredDirectories = RequiredDirectories,
            RequiredSupportFiles = RequiredSupportFiles,
            AssetCounts = CountAssets(runtimeInventory),
            QaDataSubsetFileCount = qaSubset.Length,
            QaDataSubsetDigest = SafePaths.InventoryDigest(qaSubset),
            Files = runtimeInventory
        };
        return WriteNewManifest(policy, manifestPath, manifest);
    }

    public static RuntimeManifestVerification VerifyManifest(string manifestPath, string expectedRoot, string expectedKind, string? expectedManifestSha256 = null, RuntimePolicy? policy = null)
    {
        try
        {
            RuntimePolicy selectedPolicy = policy ?? QaRuntimePolicy.Default;
            string fullManifestPath = SafePaths.NormalizeFullPath(manifestPath);
            EnsureManifestPath(selectedPolicy, fullManifestPath, expectedRoot, expectedRoot);
            SafePaths.EnsureNoReparseAncestors(fullManifestPath, leafMustExist: true);
            string actualManifestSha = SafePaths.HashFile(fullManifestPath);
            if (expectedManifestSha256 is not null && !FixedEquals(actualManifestSha, expectedManifestSha256))
                throw new InvalidDataException("Runtime manifest SHA-256 differs from the case lock.");
            string sidecarPath = fullManifestPath + ".sha256";
            SafePaths.EnsureNoReparseAncestors(sidecarPath, leafMustExist: true);
            string sidecarSha = File.ReadAllText(sidecarPath).Trim();
            if (!FixedEquals(actualManifestSha, sidecarSha)) throw new InvalidDataException("Runtime manifest sidecar SHA-256 mismatch.");

            RuntimeTreeManifest manifest = JsonSerializer.Deserialize<RuntimeTreeManifest>(File.ReadAllText(fullManifestPath), CaseDescriptor.JsonOptions)
                ?? throw new InvalidDataException("Runtime manifest is empty or malformed.");
            if (manifest.SchemaVersion != 1 || manifest.AttestationClass != "LOCAL_RUNTIME_ATTESTATION" || manifest.Kind != expectedKind)
                throw new InvalidDataException("Runtime manifest class, kind, or schema is unsupported.");
            string runtimeRoot = SafePaths.NormalizeFullPath(manifest.RuntimeRootPath);
            if (!PathEquals(runtimeRoot, SafePaths.NormalizeFullPath(expectedRoot)))
                throw new InvalidDataException("Runtime manifest targets an unexpected runtime root.");
            RejectLiveRuntimeSelection(runtimeRoot, selectedPolicy.SteamSourceRoot);
            SafePaths.EnsureNoReparseAncestors(runtimeRoot, leafMustExist: true);
            FileDigest[] recorded = manifest.Files.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
            if (recorded.Length != manifest.FileCount || recorded.Any(f => f.Size < 0 || string.IsNullOrWhiteSpace(f.Path)))
                throw new InvalidDataException("Runtime manifest inventory is malformed.");
            if (!FixedEquals(SafePaths.InventoryDigest(recorded), manifest.CanonicalDigest))
                throw new InvalidDataException("Runtime manifest canonical digest is invalid.");
            FileDigest[] actual = InventoryForAttestedIdentity(runtimeRoot, expectedKind, selectedPolicy);
            RequireSameInventory(recorded, actual, "Runtime tree differs from its local attestation.");
            ValidateRuntimeShape(runtimeRoot, actual);
            if (!FixedEquals(HashInventoryEntry(actual, "DarkSoulsRemastered.exe"), manifest.ExecutableSha256))
                throw new InvalidDataException("Runtime executable hash differs from its local manifest.");
            if (actual.Sum(x => x.Size) != manifest.TotalBytes) throw new InvalidDataException("Runtime byte total differs from its local manifest.");
            return new RuntimeManifestVerification(true, "RUNTIME_MANIFEST_VERIFIED", "Runtime files match the local attestation.", manifest, actualManifestSha);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("Steam", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeManifestVerification(false, "LIVE_RUNTIME_REJECTED", ex.Message);
        }
        catch (Exception ex)
        {
            return new RuntimeManifestVerification(false, "QA_RUNTIME_MANIFEST_INVALID", ex.Message);
        }
    }

    public static RuntimeApplyResult ApplyCertifiedPackage(CaseDescriptor descriptor, string cleanManifestPath, string moddedManifestPath, RuntimePolicy policy)
    {
        string stagingRoot = policy.ModifiedRuntimeRoot + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            PackageVerification package = PackageVerifier.Verify(descriptor);
            if (!package.Ok) throw new InvalidDataException("Selected package verification failed: " + package.Message);
            RuntimeManifestVerification clean = VerifyManifest(cleanManifestPath, policy.CleanRuntimeRoot, "QA_RUNTIME_CLEAN_BASELINE", policy: policy);
            if (!clean.Ok || clean.Manifest is null || clean.ManifestSha256 is null)
                throw new InvalidDataException("Clean runtime baseline verification failed: " + clean.Message);
            string targetRoot = SafePaths.NormalizeFullPath(policy.ModifiedRuntimeRoot);
            RejectLiveRuntimeSelection(targetRoot, policy.SteamSourceRoot);
            if (Directory.Exists(targetRoot) || File.Exists(targetRoot)) throw new IOException("Refusing to overwrite the existing package test runtime.");
            EnsureManifestPath(policy, SafePaths.NormalizeFullPath(moddedManifestPath), policy.ModifiedRuntimeRoot, policy.ModifiedRuntimeRoot);
            SafePaths.EnsureNoReparseAncestors(stagingRoot, leafMustExist: false);

            CopyInventory(clean.Manifest.Files, policy.CleanRuntimeRoot, stagingRoot);
            FileDigest[] packageFiles = ReadPackageFiles(descriptor.Package!.RootPath!);
            var packageSet = packageFiles.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            foreach (FileDigest file in packageFiles)
            {
                string source = CombineRelative(descriptor.Package.RootPath!, file.Path);
                string target = CombineRelative(stagingRoot, file.Path);
                SafePaths.EnsureNoReparseAncestors(source, leafMustExist: true);
                if (!File.Exists(source) || SafePaths.HashFile(source) != file.Sha256)
                    throw new InvalidDataException("Package changed after verification: " + file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                SafePaths.EnsureNoReparseAncestors(target, leafMustExist: false);
                File.Copy(source, target, overwrite: true);
            }

            FileDigest[] finalInventory = SafePaths.Inventory(stagingRoot, rejectHardLinks: true).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
            FileDigest[] baselineInventory = clean.Manifest.Files.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
            FileDigest[] changed = Diff(baselineInventory, finalInventory);
            if (changed.Any(file => !packageSet.Contains(file.Path)))
                throw new InvalidDataException("Package application changed a path outside the certified package inventory.");
            foreach (FileDigest expected in packageFiles)
            {
                FileDigest? actual = finalInventory.FirstOrDefault(f => f.Path == expected.Path);
                if (actual is null || actual.Size != expected.Size || !FixedEquals(actual.Sha256, expected.Sha256))
                    throw new InvalidDataException("Applied package file differs from the certified package: " + expected.Path);
            }
            if (!FixedEquals(HashInventoryEntry(finalInventory, "DarkSoulsRemastered.exe"), clean.Manifest.ExecutableSha256))
                throw new InvalidDataException("Package application unexpectedly changed the game executable.");

            Directory.Move(stagingRoot, targetRoot);
            FileDigest[] movedInventory = SafePaths.Inventory(targetRoot, rejectHardLinks: true).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
            RequireSameInventory(finalInventory, movedInventory, "Package test runtime changed during final placement.");
            var moddedManifest = new RuntimeTreeManifest
            {
                Kind = "QA_RUNTIME_MODDED_DIGEST",
                SourceType = "COPY_OF_LOCAL_QA_RUNTIME_CLEAN_BASELINE",
                SourceRootPath = policy.CleanRuntimeRoot,
                RuntimeRootPath = targetRoot,
                CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                SourceAppId = clean.Manifest.SourceAppId,
                SourceBuildId = clean.Manifest.SourceBuildId,
                SourceDepotManifests = clean.Manifest.SourceDepotManifests,
                SourceAppManifestSha256 = clean.Manifest.SourceAppManifestSha256,
                ExecutableSha256 = clean.Manifest.ExecutableSha256,
                CanonicalDigest = SafePaths.InventoryDigest(movedInventory),
                FileCount = movedInventory.Length,
                TotalBytes = movedInventory.Sum(x => x.Size),
                TopLevelArchivePairs = FindTopLevelArchivePairs(targetRoot),
                RequiredDirectories = clean.Manifest.RequiredDirectories,
                RequiredSupportFiles = clean.Manifest.RequiredSupportFiles,
                AssetCounts = CountAssets(movedInventory),
                QaDataSubsetFileCount = clean.Manifest.QaDataSubsetFileCount,
                QaDataSubsetDigest = clean.Manifest.QaDataSubsetDigest,
                CleanManifestSha256 = clean.ManifestSha256,
                PackageTreeSha256 = package.TreeSha256,
                PackageManifestSha256 = package.RunManifestSha256,
                ChangedFiles = changed,
                Files = movedInventory
            };
            RuntimeManifestWriteResult written = WriteNewManifest(policy, moddedManifestPath, moddedManifest);
            return new RuntimeApplyResult(true, "QA_RUNTIME_PACKAGE_APPLIED", "Certified package was applied to a separate runtime copy; the clean baseline was preserved.", targetRoot, moddedManifest.CanonicalDigest, written.ManifestSha256, changed.Length);
        }
        catch (Exception ex)
        {
            return new RuntimeApplyResult(false, ex.Message.Contains("Steam", StringComparison.OrdinalIgnoreCase) ? "LIVE_RUNTIME_REJECTED" : "QA_RUNTIME_APPLY_FAILED", ex.Message,
                Directory.Exists(policy.ModifiedRuntimeRoot) ? policy.ModifiedRuntimeRoot : Directory.Exists(stagingRoot) ? stagingRoot : null);
        }
    }

    public static RuntimeManifestVerification VerifyPackageRuntime(
        CaseDescriptor descriptor,
        string cleanManifestPath,
        string moddedManifestPath,
        string? expectedCleanManifestSha256 = null,
        string? expectedModdedManifestSha256 = null,
        RuntimePolicy? policy = null)
    {
        RuntimePolicy selected = policy ?? QaRuntimePolicy.Default;
        RuntimeManifestVerification clean = VerifyManifest(cleanManifestPath, selected.CleanRuntimeRoot, "QA_RUNTIME_CLEAN_BASELINE", expectedCleanManifestSha256, selected);
        if (!clean.Ok || clean.Manifest is null || clean.ManifestSha256 is null) return clean;
        RuntimeManifestVerification modded = VerifyManifest(moddedManifestPath, selected.ModifiedRuntimeRoot, "QA_RUNTIME_MODDED_DIGEST", expectedModdedManifestSha256, selected);
        if (!modded.Ok || modded.Manifest is null) return modded;
        try
        {
            PackageVerification package = PackageVerifier.Verify(descriptor);
            if (!package.Ok) throw new InvalidDataException("Selected package verification failed: " + package.Message);
            RuntimeTreeManifest final = modded.Manifest;
            if (!FixedEquals(final.CleanManifestSha256, clean.ManifestSha256))
                throw new InvalidDataException("Modded runtime is not linked to the locked clean baseline manifest.");
            if (!FixedEquals(final.PackageTreeSha256, package.TreeSha256!) || !FixedEquals(final.PackageManifestSha256, package.RunManifestSha256!))
                throw new InvalidDataException("Modded runtime package identity differs from the selected certified package.");
            if (!FixedEquals(final.ExecutableSha256, clean.Manifest.ExecutableSha256))
                throw new InvalidDataException("Modded runtime executable differs from the clean baseline.");
            FileDigest[] expectedPackageFiles = ReadPackageFiles(descriptor.Package!.RootPath!);
            var expectedSet = expectedPackageFiles.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            FileDigest[] actualChanges = Diff(clean.Manifest.Files, final.Files);
            if (actualChanges.Any(f => !expectedSet.Contains(f.Path)))
                throw new InvalidDataException("Runtime differs from its baseline outside the selected package file set.");
            if (!SameInventory(actualChanges, final.ChangedFiles))
                throw new InvalidDataException("Recorded changed-file inventory differs from the baseline comparison.");
            foreach (FileDigest expected in expectedPackageFiles)
            {
                FileDigest? actual = final.Files.FirstOrDefault(f => f.Path == expected.Path);
                if (actual is null || actual.Size != expected.Size || !FixedEquals(actual.Sha256, expected.Sha256))
                    throw new InvalidDataException("Modded runtime does not contain the selected package file: " + expected.Path);
            }
            return new RuntimeManifestVerification(true, "QA_RUNTIME_COMPLETE", "Clean baseline and package-applied runtime match their local manifests and the selected package.", final, modded.ManifestSha256);
        }
        catch (Exception ex)
        {
            return new RuntimeManifestVerification(false, "QA_RUNTIME_PACKAGE_IDENTITY_MISMATCH", ex.Message);
        }
    }

    public static void RejectLiveRuntimeSelection(string runtimeRoot, string steamSourceRoot)
    {
        string full = SafePaths.NormalizeFullPath(runtimeRoot);
        string steam = SafePaths.NormalizeFullPath(steamSourceRoot);
        try { SafePaths.RejectSteamPath(full); }
        catch (InvalidDataException ex) { throw new InvalidDataException("LIVE_RUNTIME_REJECTED: " + ex.Message, ex); }
        if (PathEquals(full, steam) || IsUnder(full, steam))
            throw new InvalidDataException("LIVE_RUNTIME_REJECTED: runtime selection points into the live Steam installation.");
    }

    private static FileDigest[] InventoryForAttestedIdentity(string runtimeRoot, string expectedKind, RuntimePolicy policy)
    {
        FileDigest[] actual = SafePaths.Inventory(runtimeRoot, rejectHardLinks: true).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        if (expectedKind != "QA_RUNTIME_MODDED_DIGEST" || !PathEquals(runtimeRoot, policy.ModifiedRuntimeRoot)) return actual;

        FileDigest? sidecarEntry = actual.SingleOrDefault(file => string.Equals(file.Path, QaSteamAppIdSidecar.FileName, StringComparison.OrdinalIgnoreCase));
        _ = QaSteamAppIdSidecar.VerifyIfPresent(runtimeRoot, policy.ModifiedRuntimeRoot);
        if (sidecarEntry is null) return actual;
        if (!string.Equals(sidecarEntry.Path, QaSteamAppIdSidecar.FileName, StringComparison.Ordinal))
            throw new InvalidDataException("The QA Steam AppID sidecar must use the exact root-level filename steam_appid.txt.");
        return actual.Where(file => !string.Equals(file.Path, QaSteamAppIdSidecar.FileName, StringComparison.Ordinal)).ToArray();
    }

    public static FileDigest[] ReadPackageFiles(string packageRoot)
    {
        string manifestPath = Path.Combine(packageRoot, "run-manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        return manifest.RootElement.GetProperty("files").EnumerateArray().Select(file =>
        {
            string path = file.GetProperty("path").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(s => s is "" or "." or ".."))
                throw new InvalidDataException("Unsafe relative package path in runtime application.");
            return new FileDigest(path, file.GetProperty("size").GetInt64(), file.GetProperty("sha256").GetString() ?? "");
        }).ToArray();
    }

    private static FileDigest[] VerifyQaSubset(string qaRoot, FileDigest[] sourceInventory)
    {
        FileDigest[] qa = SafePaths.Inventory(qaRoot, rejectHardLinks: true).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var source = sourceInventory.ToDictionary(x => x.Path, StringComparer.Ordinal);
        foreach (FileDigest file in qa)
        {
            if (!source.TryGetValue(file.Path, out FileDigest? candidate) || candidate.Size != file.Size || !FixedEquals(candidate.Sha256, file.Sha256))
                throw new InvalidDataException("The installed source does not match the established QA data snapshot: " + file.Path);
        }
        return qa;
    }

    private static RuntimeManifestWriteResult WriteNewManifest(RuntimePolicy policy, string path, RuntimeTreeManifest manifest)
    {
        EnsureManifestPath(policy, SafePaths.NormalizeFullPath(path), manifest.RuntimeRootPath, policy.ModifiedRuntimeRoot);
        string parent = Path.GetDirectoryName(path)!;
        SafePaths.EnsureNoReparseAncestors(parent, leafMustExist: false);
        Directory.CreateDirectory(parent);
        SafePaths.EnsureNoReparseAncestors(parent, leafMustExist: true);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest, CaseDescriptor.JsonOptions);
        string sha = Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
        SafePaths.EnsureNoReparseAncestors(path, leafMustExist: false);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
        {
            stream.Write(json);
            stream.Flush(flushToDisk: true);
        }
        string sidecar = path + ".sha256";
        using (var stream = new FileStream(sidecar, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 256, FileOptions.WriteThrough))
        {
            byte[] hashLine = Encoding.ASCII.GetBytes(sha + "\n");
            stream.Write(hashLine);
            stream.Flush(flushToDisk: true);
        }
        return new RuntimeManifestWriteResult(path, sha, manifest);
    }

    private static void CopyInventory(FileDigest[] files, string sourceRoot, string targetRoot)
    {
        if (Directory.Exists(targetRoot)) throw new IOException("Copy staging path already exists.");
        SafePaths.EnsureNoReparseAncestors(targetRoot, leafMustExist: false);
        Directory.CreateDirectory(targetRoot);
        foreach (FileDigest file in files)
        {
            string source = CombineRelative(sourceRoot, file.Path);
            string target = CombineRelative(targetRoot, file.Path);
            if (!File.Exists(source)) throw new FileNotFoundException("Clean baseline file disappeared during copy.", source);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
        }
    }

    private static string CombineRelative(string root, string relative)
    {
        string fullRoot = SafePaths.NormalizeFullPath(root);
        string full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Relative path escaped runtime root.");
        return full;
    }

    private static void EnsureManifestPath(RuntimePolicy policy, string manifestPath, string runtimeA, string runtimeB)
    {
        string attestationRoot = SafePaths.NormalizeFullPath(policy.AttestationDirectory);
        string fullManifest = SafePaths.NormalizeFullPath(manifestPath);
        if (!IsUnder(fullManifest, attestationRoot)) throw new InvalidDataException("Runtime manifests must be outside the game tree in the QA attestation directory.");
        foreach (string root in new[] { runtimeA, runtimeB })
        {
            string fullRoot = SafePaths.NormalizeFullPath(root);
            if (PathEquals(fullManifest, fullRoot) || IsUnder(fullManifest, fullRoot))
                throw new InvalidDataException("Runtime manifest cannot be stored inside a game tree.");
        }
    }

    private static void ValidateRuntimeShape(string root, FileDigest[] files)
    {
        string[] missingDirectories = RequiredDirectories.Where(name => !Directory.Exists(Path.Combine(root, name))).ToArray();
        string[] missingFiles = RequiredSupportFiles.Where(name => !File.Exists(Path.Combine(root, name))).ToArray();
        if (missingDirectories.Length > 0 || missingFiles.Length > 0)
            throw new InvalidDataException("Required DSR runtime content is missing. Directories: " + string.Join(",", missingDirectories) + "; files: " + string.Join(",", missingFiles));
        if (files.Length < 1000) throw new InvalidDataException("Runtime file inventory is too small for the pinned DSR Steam build.");
    }

    private static string[] FindTopLevelArchivePairs(string root)
    {
        string[] bhd = Directory.GetFiles(root, "*.bhd", SearchOption.TopDirectoryOnly);
        string[] bdt = Directory.GetFiles(root, "*.bdt", SearchOption.TopDirectoryOnly);
        var names = new HashSet<string>(bdt.Select(Path.GetFileNameWithoutExtension).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        return bhd.Select(Path.GetFileNameWithoutExtension).OfType<string>().Where(names.Contains).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, int> CountAssets(FileDigest[] files) => files
        .GroupBy(file => Path.GetExtension(file.Path), StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => string.IsNullOrEmpty(group.Key) ? "[no-extension]" : group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    private static string HashInventoryEntry(FileDigest[] files, string path) =>
        files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))?.Sha256
        ?? throw new InvalidDataException("Runtime inventory lacks required file: " + path);

    private static string ReadQuotedValue(string text, string name)
    {
        Match match = Regex.Match(text, "\\\"" + Regex.Escape(name) + "\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : throw new InvalidDataException("Steam appmanifest lacks " + name + ".");
    }

    private static string ReadDepotManifest(string text, string depotId)
    {
        Match block = Regex.Match(text, "\\\"" + Regex.Escape(depotId) + "\\\"\\s*\\{([^}]*)\\}", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!block.Success) throw new InvalidDataException("Steam appmanifest lacks depot " + depotId + ".");
        return ReadQuotedValue(block.Groups[1].Value, "manifest");
    }

    private static FileDigest[] Diff(FileDigest[] baseline, FileDigest[] current)
    {
        var before = baseline.ToDictionary(x => x.Path, StringComparer.Ordinal);
        var after = current.ToDictionary(x => x.Path, StringComparer.Ordinal);
        var paths = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal);
        return paths.Where(path => !before.TryGetValue(path, out FileDigest? oldFile) || !after.TryGetValue(path, out FileDigest? newFile) ||
                                   oldFile.Size != newFile.Size || !FixedEquals(oldFile.Sha256, newFile.Sha256))
            .Select(path => after.TryGetValue(path, out FileDigest? file) ? file : before[path]).ToArray();
    }

    private static bool SameInventory(FileDigest[] left, FileDigest[] right) =>
        left.OrderBy(x => x.Path, StringComparer.Ordinal).SequenceEqual(right.OrderBy(x => x.Path, StringComparer.Ordinal));

    private static void RequireSameInventory(FileDigest[] expected, FileDigest[] actual, string message)
    {
        if (!SameInventory(expected, actual)) throw new InvalidDataException(message);
    }

    private static bool PathEquals(string left, string right) => string.Equals(SafePaths.NormalizeFullPath(left), SafePaths.NormalizeFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static bool IsUnder(string path, string root)
    {
        string fullPath = SafePaths.NormalizeFullPath(path);
        string fullRoot = SafePaths.NormalizeFullPath(root);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    private static bool FixedEquals(string? left, string? right) =>
        left is not null && right is not null && left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left.ToLowerInvariant()), Encoding.ASCII.GetBytes(right.ToLowerInvariant()));
}
