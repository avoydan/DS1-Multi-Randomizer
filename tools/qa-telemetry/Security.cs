using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace QaTelemetry;

public static class ProcessAccessPolicy
{
    public const uint QueryLimitedInformation = 0x1000;
    public const uint VirtualMemoryRead = 0x0010;
    public const uint AllowlistedReadOnlyRights = QueryLimitedInformation | VirtualMemoryRead;
    public const uint RightsRequestedByThisBuild = 0;

    public static void Validate(uint requested)
    {
        if ((requested & ~AllowlistedReadOnlyRights) != 0)
            throw new UnauthorizedAccessException("Process access mask exceeds the query/read-only allowlist.");
    }
}

public static class SafePaths
{
    public static void RequireSha256(string? value, string label)
    {
        if (value is null || value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid SHA-256 value for " + label + ".");
    }

    public static string NormalizeFullPath(string value)
    {
        if (!Path.IsPathFullyQualified(value)) throw new InvalidDataException("Path must be fully qualified.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    public static void RejectSteamPath(string value)
    {
        string full = NormalizeFullPath(value);
        string[] segments = full.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => string.Equals(s, "steamapps", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(s, "Steam", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(s, "SteamLibrary", StringComparison.OrdinalIgnoreCase)) ||
            full.Contains("\\Steam\\", StringComparison.OrdinalIgnoreCase) || full.Contains("/Steam/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Steam installation paths are explicitly rejected.");
    }

    public static void EnsureUnderTemp(string path)
    {
        string temp = NormalizeFullPath(Path.GetTempPath());
        string full = NormalizeFullPath(path);
        string prefix = Path.EndsInDirectorySeparator(temp) ? temp : temp + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Telemetry traces and summaries must remain under the current user's Temp directory.");
    }

    public static void EnsureNoReparseAncestors(string path, bool leafMustExist)
    {
        string full = Path.GetFullPath(path);
        string? cursor = full;
        while (!string.IsNullOrEmpty(cursor))
        {
            bool exists = Directory.Exists(cursor) || File.Exists(cursor);
            if (leafMustExist && string.Equals(cursor, full, StringComparison.OrdinalIgnoreCase) && !exists)
                throw new FileNotFoundException("Required path does not exist.", full);
            if (exists && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse points and symbolic links are not allowed: " + cursor);
            string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(cursor));
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, cursor, StringComparison.OrdinalIgnoreCase)) break;
            cursor = parent;
        }
    }

    public static IReadOnlyList<FileDigest> Inventory(string root, bool rejectHardLinks)
    {
        string fullRoot = NormalizeFullPath(root);
        EnsureNoReparseAncestors(fullRoot, leafMustExist: true);
        if (!Directory.Exists(fullRoot)) throw new DirectoryNotFoundException(fullRoot);
        var pending = new Stack<string>();
        var items = new List<FileDigest>();
        pending.Push(fullRoot);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                FileAttributes attrs = File.GetAttributes(entry);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse points and symbolic links are not allowed in certified input: " + entry);
                if ((attrs & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                if (rejectHardLinks) EnsureSingleLink(entry);
                string relative = Path.GetRelativePath(fullRoot, entry).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                    throw new InvalidDataException("Input file escaped its locked root: " + relative);
                var info = new FileInfo(entry);
                items.Add(new FileDigest(relative, info.Length, HashFile(entry)));
            }
        }
        return items;
    }

    public static string HashFile(string path)
    {
        EnsureNoReparseAncestors(path, leafMustExist: true);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static void EnsureUniqueFile(string path) => EnsureSingleLink(path);

    public static string InventoryDigest(IEnumerable<FileDigest> files)
    {
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (FileDigest file in files)
        {
            aggregate.AppendData(Encoding.UTF8.GetBytes(file.Path + "\0" + file.Size.ToString(CultureInfo.InvariantCulture) + "\0" + file.Sha256 + "\n"));
        }
        return Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant();
    }

    private static void EnsureSingleLink(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Certified QA input identity checks require Windows.");
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info))
            throw new IOException("Cannot verify file identity for locked input: " + path, Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        if (info.NumberOfLinks != 1)
            throw new InvalidDataException("Hard-linked files are not allowed in certified QA inputs: " + path);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);
}

public static class PackageVerifier
{
    public static PackageVerification Verify(CaseDescriptor descriptor)
    {
        try
        {
            descriptor.Validate();
            PackageLock package = descriptor.Package!;
            SafePaths.RejectSteamPath(package.RootPath!);
            SafePaths.RejectSteamPath(package.AttestationPath!);
            string packageRoot = SafePaths.NormalizeFullPath(package.RootPath!);
            string manifestPath = Path.Combine(packageRoot, "run-manifest.json");
            string manifestHash = SafePaths.HashFile(manifestPath);
            if (!FixedEquals(manifestHash, package.RunManifestSha256!))
                throw new InvalidDataException("Run manifest hash differs from the locked descriptor.");

            IReadOnlyList<FileDigest> inventory = SafePaths.Inventory(packageRoot, rejectHardLinks: true);
            if (inventory.Count < 2 || !inventory.Any(f => f.Path == "run-manifest.json"))
                throw new InvalidDataException("Package inventory is incomplete.");
            var actual = inventory.ToDictionary(f => f.Path, StringComparer.Ordinal);

            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            JsonElement manifestRoot = manifest.RootElement;
            ValidateRunManifest(descriptor, manifestRoot);
            var manifestFiles = new Dictionary<string, FileDigest>(StringComparer.Ordinal);
            foreach (JsonElement file in manifestRoot.GetProperty("files").EnumerateArray())
            {
                string relative = ValidateRelativePath(file.GetProperty("path").GetString());
                var digest = new FileDigest(relative, file.GetProperty("size").GetInt64(), file.GetProperty("sha256").GetString() ?? "");
                SafePaths.RequireSha256(digest.Sha256, "manifest file");
                if (!manifestFiles.TryAdd(relative, digest)) throw new InvalidDataException("Duplicate path in run manifest: " + relative);
                if (!actual.TryGetValue(relative, out FileDigest? found) || found.Size != digest.Size || !FixedEquals(found.Sha256, digest.Sha256))
                    throw new InvalidDataException("Run-manifest file mismatch: " + relative);
            }
            if (actual.Count != manifestFiles.Count + 1 || actual.Keys.Any(path => path != "run-manifest.json" && !manifestFiles.ContainsKey(path)))
                throw new InvalidDataException("Package files differ from the run-manifest inventory.");

            string attestationHash = SafePaths.HashFile(package.AttestationPath!);
            if (!FixedEquals(attestationHash, package.AttestationSha256!))
                throw new InvalidDataException("Contemporaneous attestation hash differs from the locked descriptor.");
            using JsonDocument attestation = JsonDocument.Parse(File.ReadAllBytes(package.AttestationPath!));
            JsonElement attRoot = attestation.RootElement;
            if (attRoot.GetProperty("schema").GetString() != "ds1tr-ceaseless-e2e-attestation-v2" ||
                attRoot.GetProperty("attestationClass").GetString() != "CONTEMPORANEOUS_HASH_ATTESTATION" ||
                attRoot.GetProperty("caseName").GetString() != package.AttestationCaseName ||
                attRoot.GetProperty("gitHead").GetString() != descriptor.SourceCommit ||
                attRoot.GetProperty("backendVersion").GetString() != descriptor.BackendVersion ||
                attRoot.GetProperty("fogHostProjectVersion").GetString() != descriptor.FogHostProjectVersion ||
                attRoot.GetProperty("fogHostHostVersion").GetString() != descriptor.FogHostHostVersion ||
                attRoot.GetProperty("worldDataId").GetString() != descriptor.WorldDataId ||
                attRoot.GetProperty("catalogFingerprint").GetString() != descriptor.CatalogFingerprint ||
                attRoot.GetProperty("semanticFingerprint").GetString() != descriptor.SemanticFingerprint ||
                attRoot.GetProperty("fogModCommit").GetString() != "6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f" ||
                attRoot.GetProperty("generation").GetProperty("masterSeed").GetString() != descriptor.MasterSeed)
                throw new InvalidDataException("Attestation type or case identity mismatch.");
            JsonElement attResult = attRoot.GetProperty("result");
            if (!attResult.GetProperty("ok").GetBoolean() ||
                !FixedEquals(attResult.GetProperty("packageTreeSha256").GetString(), package.TreeSha256!) ||
                !FixedEquals(attResult.GetProperty("packageManifestSha256").GetString(), manifestHash))
                throw new InvalidDataException("Attestation package identity does not match this descriptor.");

            var attestedInventory = new List<FileDigest>();
            var attestedSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement file in attResult.GetProperty("packageFiles").EnumerateArray())
            {
                string attestedPath = file.GetProperty("path").GetString() ?? "";
                string relative = ValidateRelativePath(attestedPath);
                var digest = new FileDigest(relative, file.GetProperty("size").GetInt64(), file.GetProperty("sha256").GetString() ?? "");
                SafePaths.RequireSha256(digest.Sha256, "attested package file");
                if (!attestedSet.Add(relative) || !actual.TryGetValue(relative, out FileDigest? found) || found.Size != digest.Size || !FixedEquals(found.Sha256, digest.Sha256))
                    throw new InvalidDataException("Contemporaneous per-file attestation mismatch: " + relative);
                attestedInventory.Add(digest);
            }
            if (attestedInventory.Count != actual.Count || attestedSet.Count != actual.Count)
                throw new InvalidDataException("Attestation file inventory count mismatch.");
            string treeHash = SafePaths.InventoryDigest(attestedInventory);
            if (!FixedEquals(treeHash, package.TreeSha256!)) throw new InvalidDataException("Package tree hash differs from the contemporaneous attestation.");

            long totalBytes = inventory.Sum(f => f.Size);
            return new PackageVerification(true, "PACKAGE_IDENTITY_VERIFIED", "The package, run manifest, per-file hashes, tree digest, and pinned local V2 attestation match.", treeHash, manifestHash, inventory.Count, totalBytes);
        }
        catch (Exception ex)
        {
            return new PackageVerification(false, "PACKAGE_IDENTITY_MISMATCH", ex.Message);
        }
    }

    private static void ValidateRunManifest(CaseDescriptor descriptor, JsonElement manifest)
    {
        if (manifest.GetProperty("appVersion").GetString() != descriptor.BackendVersion ||
            manifest.GetProperty("generationProfile").GetString() != "ds1tr-gen-v5" ||
            manifest.GetProperty("masterSeed").GetString() != descriptor.MasterSeed)
            throw new InvalidDataException("Run-manifest seed or generation profile mismatch.");
        if (!manifest.GetProperty("engines").GetProperty("fog").GetString()!.Contains(descriptor.FogHostHostVersion!, StringComparison.Ordinal))
            throw new InvalidDataException("FogHost runtime version mismatch in run manifest.");
        JsonElement fog = manifest.GetProperty("options").GetProperty("fog");
        if (!fog.GetProperty("enabled").GetBoolean() || fog.GetProperty("requireLordSouls").GetBoolean())
            throw new InvalidDataException("The selected package does not match the certified Lord-Souls-off case.");
        if (!fog.GetProperty("traversableFogs").GetBoolean() || !fog.GetProperty("bossFogs").GetBoolean() ||
            !fog.GetProperty("areaWarps").GetBoolean() || !fog.GetProperty("scaling").GetBoolean() ||
            fog.GetProperty("majorPvpFogs").GetBoolean() || fog.GetProperty("minorPvpFogs").GetBoolean() ||
            fog.GetProperty("lordvesselGates").GetBoolean() || fog.GetProperty("randomStart").GetBoolean() ||
            fog.GetProperty("disconnectedFogs").GetBoolean() || fog.GetProperty("glitchedLogic").GetBoolean())
            throw new InvalidDataException("Selected package Fog options differ from the certified profile.");
        JsonElement items = manifest.GetProperty("options").GetProperty("items");
        JsonElement enemies = manifest.GetProperty("options").GetProperty("enemies");
        if (!items.GetProperty("enabled").GetBoolean() || !enemies.GetProperty("enabled").GetBoolean() ||
            !enemies.GetProperty("randomizeRegularEnemies").GetBoolean() || enemies.GetProperty("randomizeBosses").GetBoolean())
            throw new InvalidDataException("Selected package combined-engine options differ from the certified case.");
        bool hasAttempt = manifest.GetProperty("attempts").EnumerateArray().Any(attempt =>
            attempt.GetProperty("fogAttempt").GetInt32() == descriptor.FogAttempt &&
            attempt.GetProperty("fogSeed").GetUInt32() == descriptor.RawFogSeed &&
            attempt.GetProperty("combinedSeed").GetString() == descriptor.CombinedSeed &&
            attempt.GetProperty("result").GetString() == "success");
        if (!hasAttempt) throw new InvalidDataException("The accepted Fog seed/attempt is not present in the run manifest.");
    }

    private static string ValidateRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("Unsafe relative package path in hash inventory.");
        return path;
    }

    private static bool FixedEquals(string? left, string right) =>
        left is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left.ToLowerInvariant()), Encoding.ASCII.GetBytes(right.ToLowerInvariant()));
}

public static class RuntimePreflight
{
    public static RuntimePreflightResult Run(CaseDescriptor descriptor, RuntimePolicy? policy = null)
    {
        RuntimePolicy selected = policy ?? QaRuntimePolicy.Default;
        var blockers = new List<string>();
        RuntimeLock runtime = descriptor.Runtime!;
        string exeHash = "";
        string? cleanManifestSha = null;
        string? modifiedManifestSha = null;
        bool requiredRuntimeAssets = false;
        try
        {
            if (runtime.CleanManifestPath is null || runtime.CleanManifestSha256 is null ||
                runtime.ModifiedManifestPath is null || runtime.ModifiedManifestSha256 is null || runtime.CleanRootPath is null)
                blockers.Add("QA_RUNTIME_MANIFEST_MISSING: the case is not bound to a clean and package-applied runtime attestation.");

            string? root = null;
            string? suppliedExe = null;
            try
            {
                root = SafePaths.NormalizeFullPath(runtime.RootPath!);
                suppliedExe = SafePaths.NormalizeFullPath(runtime.ExecutablePath!);
                RuntimeManifestService.RejectLiveRuntimeSelection(root, selected.SteamSourceRoot);
                if (!string.Equals(root, SafePaths.NormalizeFullPath(selected.ModifiedRuntimeRoot), StringComparison.OrdinalIgnoreCase))
                    blockers.Add("QA_RUNTIME_PATH_MISMATCH: runtime root differs from the approved isolated test path.");
                SafePaths.EnsureNoReparseAncestors(root, leafMustExist: true);
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Locked QA runtime root is missing.");
            }
            catch (Exception ex)
            {
                blockers.Add(ex.Message.StartsWith("LIVE_RUNTIME_REJECTED", StringComparison.Ordinal) ? ex.Message : "QA_RUNTIME_PATH_INVALID: " + ex.Message);
            }

            if (root is not null && suppliedExe is not null)
            {
                string expectedExe = Path.Combine(root, "DarkSoulsRemastered.exe");
                if (!string.Equals(expectedExe, suppliedExe, StringComparison.OrdinalIgnoreCase))
                    blockers.Add("QA_EXECUTABLE_PATH_MISMATCH");
                else if (!File.Exists(suppliedExe))
                    blockers.Add("QA_EXECUTABLE_MISSING");
                else
                {
                    SafePaths.EnsureNoReparseAncestors(suppliedExe, leafMustExist: true);
                    SafePaths.EnsureUniqueFile(suppliedExe);
                    exeHash = SafePaths.HashFile(suppliedExe);
                    if (!string.Equals(exeHash, runtime.ExecutableSha256, StringComparison.OrdinalIgnoreCase)) blockers.Add("QA_EXECUTABLE_HASH_MISMATCH");
                }
            }

            if (runtime.CleanManifestPath is not null && runtime.CleanManifestSha256 is not null &&
                runtime.ModifiedManifestPath is not null && runtime.ModifiedManifestSha256 is not null && runtime.CleanRootPath is not null)
            {
                try
                {
                    RuntimeManifestService.RejectLiveRuntimeSelection(runtime.CleanRootPath, selected.SteamSourceRoot);
                    if (!string.Equals(SafePaths.NormalizeFullPath(runtime.CleanRootPath), SafePaths.NormalizeFullPath(selected.CleanRuntimeRoot), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("QA_CLEAN_RUNTIME_PATH_MISMATCH: clean baseline root differs from the approved QA path.");
                    RuntimeManifestVerification attestation = RuntimeManifestService.VerifyPackageRuntime(
                        descriptor,
                        runtime.CleanManifestPath,
                        runtime.ModifiedManifestPath,
                        runtime.CleanManifestSha256,
                        runtime.ModifiedManifestSha256,
                        selected);
                    if (!attestation.Ok) blockers.Add(attestation.Code + ": " + attestation.Message);
                    else
                    {
                        requiredRuntimeAssets = true;
                        cleanManifestSha = runtime.CleanManifestSha256;
                        modifiedManifestSha = runtime.ModifiedManifestSha256;
                    }
                }
                catch (Exception ex)
                {
                    blockers.Add(ex.Message.StartsWith("LIVE_RUNTIME_REJECTED", StringComparison.Ordinal) ? ex.Message : "QA_RUNTIME_ATTESTATION_ERROR: " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            blockers.Add(ex.Message.StartsWith("LIVE_RUNTIME_REJECTED", StringComparison.Ordinal) ? ex.Message : "QA_RUNTIME_PREFLIGHT_ERROR: " + ex.Message);
        }

        bool ok = blockers.Count == 0;
        return new RuntimePreflightResult(ok, ok ? "QA_RUNTIME_COMPLETE" : blockers.Any(b => b.StartsWith("LIVE_RUNTIME_REJECTED", StringComparison.Ordinal)) ? "LIVE_RUNTIME_REJECTED" : "QA_RUNTIME_PREFLIGHT_BLOCKED", blockers.ToArray(), exeHash, requiredRuntimeAssets, cleanManifestSha, modifiedManifestSha);
    }
}
