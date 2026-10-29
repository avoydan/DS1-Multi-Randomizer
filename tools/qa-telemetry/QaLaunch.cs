using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace QaTelemetry;

public sealed record SteamProcessCandidate(string ImagePath, string UserSid);
public sealed record SteamClientStatus(bool Ready, string Code, string Message, string? ImagePath = null, string? UserSid = null, string? CurrentUserSid = null);
public sealed record QaSteamAppIdStatus(string Path, string Contents, string Sha256, bool Created);
public sealed record QaGameLaunchResult(string Status, string Code, string Message, string ExecutablePath, string WorkingDirectory, int ProcessId, string ExecutableSha256, string SteamAppIdPath, string SteamAppIdSha256, string SteamApi64Sha256, bool ProcessPathVerified, uint ProcessQueryRights);

public static class QaLaunchPolicy
{
    public static void ValidateLaunchPaths(string executablePath, string workingDirectory, string expectedExecutablePath, string expectedRuntimeRoot)
    {
        string executable = SafePaths.NormalizeFullPath(executablePath);
        string working = SafePaths.NormalizeFullPath(workingDirectory);
        string expectedExecutable = SafePaths.NormalizeFullPath(expectedExecutablePath);
        string expectedRoot = SafePaths.NormalizeFullPath(expectedRuntimeRoot);

        if (!string.Equals(executable, expectedExecutable, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(executable), expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_LAUNCH_EXECUTABLE_PATH_MISMATCH", "QA launch accepts only the exact locked isolated executable.");
        if (!string.Equals(working, expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_LAUNCH_WORKING_DIRECTORY_MISMATCH", "QA executable working directory must be the exact isolated runtime directory.");

        try { SafePaths.RejectSteamPath(executable); }
        catch (InvalidDataException ex) { throw new QaTelemetryException("LIVE_STEAM_EXECUTABLE_REJECTED", "Refusing to launch an executable from the normal Steam installation.", ex); }
    }

    public static string VerifyObservedProcessPath(string observedPath, string expectedExecutablePath)
    {
        string observed = SafePaths.NormalizeFullPath(observedPath);
        string expected = SafePaths.NormalizeFullPath(expectedExecutablePath);
        try { SafePaths.RejectSteamPath(observed); }
        catch (InvalidDataException ex) { throw new QaTelemetryException("LIVE_STEAM_EXECUTABLE_REJECTED", "Refusing a DSR process whose image is under a Steam library path.", ex); }
        if (!string.Equals(observed, expected, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_LAUNCH_PROCESS_PATH_MISMATCH", "The launched process image path is not the locked isolated QA executable.");
        return observed;
    }
}

public static class QaSteamAppIdSidecar
{
    public const string FileName = "steam_appid.txt";
    public const string AppId = "570940";
    private static readonly byte[] ExpectedBytes = Encoding.ASCII.GetBytes(AppId);
    public static string ExpectedSha256 => Convert.ToHexString(SHA256.HashData(ExpectedBytes)).ToLowerInvariant();

    public static QaSteamAppIdStatus ProvisionOrVerify(string runtimeRoot, string executablePath, string expectedRuntimeRoot)
    {
        string root = SafePaths.NormalizeFullPath(runtimeRoot);
        string expectedRoot = SafePaths.NormalizeFullPath(expectedRuntimeRoot);
        string executable = SafePaths.NormalizeFullPath(executablePath);
        if (!string.Equals(root, expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_STEAM_APPID_RUNTIME_MISMATCH", "steam_appid.txt may be provisioned only in the locked isolated QA runtime.");
        if (!string.Equals(Path.GetDirectoryName(executable), root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(executable), "DarkSoulsRemastered.exe", StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_STEAM_APPID_EXECUTABLE_MISMATCH", "The AppID sidecar can be associated only with DarkSoulsRemastered.exe in the same QA runtime directory.");
        SafePaths.EnsureNoReparseAncestors(root, leafMustExist: true);

        string sidecar = Path.Combine(root, FileName);
        if (Directory.Exists(sidecar))
            throw new QaTelemetryException("QA_STEAM_APPID_INVALID", "A directory occupies the required steam_appid.txt path.");

        bool created = false;
        if (!File.Exists(sidecar))
        {
            SafePaths.EnsureNoReparseAncestors(sidecar, leafMustExist: false);
            try
            {
                using var stream = new FileStream(sidecar, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 256, FileOptions.WriteThrough);
                stream.Write(ExpectedBytes);
                stream.Flush(flushToDisk: true);
                created = true;
            }
            catch (IOException ex) when (File.Exists(sidecar))
            {
                throw new QaTelemetryException("QA_STEAM_APPID_INVALID", "steam_appid.txt appeared concurrently; refusing to replace or trust it.", ex);
            }
            catch (Exception ex)
            {
                throw new QaTelemetryException("QA_STEAM_APPID_CREATE_FAILED", "Could not create the QA-only AppID sidecar at the locked isolated runtime path.", ex);
            }
        }

        SafePaths.EnsureNoReparseAncestors(sidecar, leafMustExist: true);
        SafePaths.EnsureUniqueFile(sidecar);
        byte[] actual = File.ReadAllBytes(sidecar);
        string hash = Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant();
        if (!actual.AsSpan().SequenceEqual(ExpectedBytes) || !FixedEquals(hash, ExpectedSha256))
            throw new QaTelemetryException("QA_STEAM_APPID_INVALID", "The QA launch sidecar must contain exactly the six ASCII bytes 570940, with no BOM, spaces, or newline. The file was preserved and not overwritten.");
        return new QaSteamAppIdStatus(sidecar, AppId, hash, created);
    }

    public static QaSteamAppIdStatus? VerifyIfPresent(string runtimeRoot, string expectedRuntimeRoot)
    {
        string root = SafePaths.NormalizeFullPath(runtimeRoot);
        string expectedRoot = SafePaths.NormalizeFullPath(expectedRuntimeRoot);
        if (!string.Equals(root, expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_STEAM_APPID_RUNTIME_MISMATCH", "The launch sidecar identity is bound to a different runtime root.");
        string path = Path.Combine(root, FileName);
        if (Directory.Exists(path)) throw new QaTelemetryException("QA_STEAM_APPID_INVALID", "A directory occupies the required steam_appid.txt path.");
        if (!File.Exists(path)) return null;
        SafePaths.EnsureNoReparseAncestors(path, leafMustExist: true);
        SafePaths.EnsureUniqueFile(path);
        byte[] actual = File.ReadAllBytes(path);
        string hash = Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant();
        if (!actual.AsSpan().SequenceEqual(ExpectedBytes) || !FixedEquals(hash, ExpectedSha256))
            throw new QaTelemetryException("QA_STEAM_APPID_INVALID", "The present QA steam_appid.txt is not the exact AppID sidecar; it was preserved and not overwritten.");
        return new QaSteamAppIdStatus(path, AppId, hash, false);
    }

    private static bool FixedEquals(string left, string right) => left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}

public static class SteamClientProbe
{
    private const uint TokenQuery = 0x0008;
    private const int TokenUser = 1;
    private const int ErrorInsufficientBuffer = 122;

    public static SteamClientStatus Evaluate(IEnumerable<SteamProcessCandidate> candidates, string expectedImagePath, string currentUserSid)
    {
        string expected = SafePaths.NormalizeFullPath(expectedImagePath);
        SteamProcessCandidate[] found = candidates.ToArray();
        if (found.Length == 0) return new(false, "STEAM_CLIENT_NOT_RUNNING", "Steam client is not running.", CurrentUserSid: currentUserSid);
        if (string.IsNullOrWhiteSpace(currentUserSid) || found.Length != 1)
            return new(false, "STEAM_CLIENT_IDENTITY_UNKNOWN", "Steam client process identity is unavailable or ambiguous.", CurrentUserSid: currentUserSid);

        SteamProcessCandidate candidate = found[0];
        string image;
        try { image = SafePaths.NormalizeFullPath(candidate.ImagePath); }
        catch { return new(false, "STEAM_CLIENT_IDENTITY_UNKNOWN", "Steam client image path could not be verified.", CurrentUserSid: currentUserSid); }
        if (!string.Equals(image, expected, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(candidate.UserSid))
            return new(false, "STEAM_CLIENT_IDENTITY_UNKNOWN", "The running steam.exe path or process owner could not be verified.", image, candidate.UserSid, currentUserSid);
        if (!string.Equals(candidate.UserSid, currentUserSid, StringComparison.OrdinalIgnoreCase))
            return new(false, "STEAM_CLIENT_USER_MISMATCH", "Steam is running under a different OS user. Start the QA launcher from the same user session as Steam.", image, candidate.UserSid, currentUserSid);
        return new(true, "STEAM_CLIENT_READY", "Steam client image and OS user match the current QA launcher session.", image, candidate.UserSid, currentUserSid);
    }

    public static SteamClientStatus Check(string expectedImagePath)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "STEAM_CLIENT_IDENTITY_UNKNOWN", "Steam process verification is Windows-only.");
        string currentSid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        Process[] processes = Process.GetProcessesByName("steam");
        if (processes.Length == 0) return Evaluate([], expectedImagePath, currentSid);
        var candidates = new List<SteamProcessCandidate>();
        try
        {
            foreach (Process process in processes)
            {
                using (process)
                {
                    string image = ProcessImageQuery.ReadImagePath(process.Id);
                    string sid = ReadOwnerSid(process.Id);
                    candidates.Add(new SteamProcessCandidate(image, sid));
                }
            }
            return Evaluate(candidates, expectedImagePath, currentSid);
        }
        catch (Exception ex)
        {
            return new(false, "STEAM_CLIENT_IDENTITY_UNKNOWN", "Steam is present but its executable path or OS user could not be verified: " + ex.Message, CurrentUserSid: currentSid);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string ReadOwnerSid(int processId)
    {
        using SafeProcessHandle process = ProcessImageQuery.OpenQueryHandle(processId);
        if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot query the Steam process token.");
        if (!OpenProcessToken(process, TokenQuery, out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open the Steam process token for read-only user identity verification.");
        using (token)
        {
            _ = GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out int required);
            int error = Marshal.GetLastWin32Error();
            if (required <= 0 || error != ErrorInsufficientBuffer)
                throw new Win32Exception(error, "Cannot size the Steam process user-token record.");
            IntPtr buffer = Marshal.AllocHGlobal(required);
            try
            {
                if (!GetTokenInformation(token, TokenUser, buffer, required, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read the Steam process user-token record.");
                IntPtr sidPointer = Marshal.ReadIntPtr(buffer);
                return new SecurityIdentifier(sidPointer).Value;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);
}

public static class ProcessImageQuery
{
    public static SafeProcessHandle OpenQueryHandle(int processId) => NativeOpenProcess(ProcessAccessPolicy.QueryLimitedInformation, false, (uint)processId);

    public static string ReadImagePath(int processId)
    {
        using SafeProcessHandle process = OpenQueryHandle(processId);
        if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot query the process image path with limited-information rights.");
        var buffer = new StringBuilder(32768);
        int length = buffer.Capacity;
        if (!QueryFullProcessImageName(process, 0, buffer, ref length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read the process image path with limited-information rights.");
        return buffer.ToString(0, length);
    }

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern SafeProcessHandle NativeOpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder imageName, ref int size);
}

public static class QaGameLauncher
{
    private static readonly string[] RequiredRuntimeLibraries = ["steam_api64.dll", "binkw64.dll", "fmodex64.dll", "fmod_event64.dll", "fmod_event_net64.dll", "xinput1_3.dll"];

    public static QaGameLaunchResult Launch(CaseDescriptor descriptor, RuntimePolicy? policy = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Isolated DSR launch is Windows-only.");
        RuntimePolicy selected = policy ?? QaRuntimePolicy.Default;

        SteamClientStatus steam = SteamClientProbe.Check(QaRuntimePolicy.SteamExecutablePath);
        if (!steam.Ready) throw new QaTelemetryException(steam.Code, steam.Message);

        PackageVerification package = PackageVerifier.Verify(descriptor);
        if (!package.Ok) throw new QaTelemetryException(package.Code, package.Message);
        RuntimePreflightResult preflight = RuntimePreflight.Run(descriptor, selected);
        if (!preflight.Ok || !string.Equals(preflight.ModifiedManifestSha256, descriptor.Runtime!.ModifiedManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_RUNTIME_PREFLIGHT_BLOCKED", string.Join("; ", preflight.Blockers));

        ExactBuildAuditResult audit = ExactBuildProfile.Audit(ExactBuildProfile.ExpectedExecutablePath);
        if (audit.Status != "EXACT_BUILD_SIGNATURES_UNIQUE_CANDIDATE")
            throw new QaTelemetryException("QA_SIGNATURE_UNVERIFIED", "Exact-build executable anchors are not verified; refusing to launch QA DSR.");

        string root = SafePaths.NormalizeFullPath(selected.ModifiedRuntimeRoot);
        string executable = SafePaths.NormalizeFullPath(descriptor.Runtime!.ExecutablePath!);
        QaLaunchPolicy.ValidateLaunchPaths(executable, root, ExactBuildProfile.ExpectedExecutablePath, selected.ModifiedRuntimeRoot);
        SafePaths.EnsureNoReparseAncestors(executable, leafMustExist: true);
        SafePaths.EnsureUniqueFile(executable);
        string exeHash = SafePaths.HashFile(executable);
        if (!FixedEquals(exeHash, ExactBuildProfile.ExpectedExecutableSha256))
            throw new QaTelemetryException("QA_EXECUTABLE_HASH_MISMATCH", "The isolated QA executable hash differs from the locked exact build.");

        var libraryHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string library in RequiredRuntimeLibraries)
        {
            string path = Path.Combine(root, library);
            SafePaths.EnsureNoReparseAncestors(path, leafMustExist: true);
            SafePaths.EnsureUniqueFile(path);
            libraryHashes.Add(library, SafePaths.HashFile(path));
        }

        Process? existing = FindExistingExactProcess(executable);
        if (existing is not null)
        {
            using (existing)
                throw new QaTelemetryException("QA_PROCESS_ALREADY_RUNNING", "An isolated QA DSR process is already running. Close that process normally, then run the launcher again; this launcher will not treat an existing process as a fresh successful startup.");
        }

        QaSteamAppIdStatus sidecar = QaSteamAppIdSidecar.ProvisionOrVerify(root, executable, selected.ModifiedRuntimeRoot);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = root,
            UseShellExecute = false
        };
        Process started;
        try { started = Process.Start(startInfo) ?? throw new QaTelemetryException("QA_LAUNCH_FAILED", "Windows did not return a process for the isolated QA executable."); }
        catch (QaTelemetryException) { throw; }
        catch (Exception ex) { throw new QaTelemetryException("QA_LAUNCH_FAILED", "Windows could not start the exact isolated QA executable.", ex); }

        using (started)
        {
            Thread.Sleep(2500);
            if (started.HasExited)
                throw new QaTelemetryException("QA_LAUNCH_EXITED_EARLY", $"The isolated QA executable exited during startup (exit code {started.ExitCode}). Check Steam initialization and the QA runtime.");

            string observedPath;
            try { observedPath = ProcessImageQuery.ReadImagePath(started.Id); }
            catch (Exception ex) { throw new QaTelemetryException("QA_LAUNCH_PROCESS_IDENTITY_UNKNOWN", "The isolated process started, but its image path could not be verified with query-limited rights.", ex); }
            string processPath = QaLaunchPolicy.VerifyObservedProcessPath(observedPath, executable);
            string processHash = SafePaths.HashFile(processPath);
            if (!FixedEquals(processHash, exeHash)) throw new QaTelemetryException("QA_EXECUTABLE_HASH_MISMATCH", "The launched process image hash differs from the isolated QA executable.");
            return new("ISOLATED_QA_GAME_LAUNCH_READY", "QA_GAME_PROCESS_VERIFIED", "The exact isolated QA executable is running with the locked working directory; no Steam-library DSR process was launched.", processPath, root, started.Id, processHash, sidecar.Path, sidecar.Sha256, libraryHashes["steam_api64.dll"], true, ProcessAccessPolicy.QueryLimitedInformation);
        }
    }

    private static Process? FindExistingExactProcess(string executable)
    {
        Process[] candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
        if (candidates.Length == 0) return null;
        var exact = new List<Process>();
        try
        {
            foreach (Process process in candidates)
            {
                string path;
                try { path = ProcessImageQuery.ReadImagePath(process.Id); }
                catch (Exception ex) { throw new QaTelemetryException("QA_PROCESS_IDENTITY_UNKNOWN", "A DSR process exists but its image path cannot be verified; refusing to start another game process.", ex); }
                _ = QaLaunchPolicy.VerifyObservedProcessPath(path, executable);
                exact.Add(process);
            }
            if (exact.Count > 1) throw new QaTelemetryException("QA_PROCESS_AMBIGUOUS", "More than one exact isolated QA DSR process is running.");
            Process? result = exact.Count == 1 ? exact[0] : null;
            foreach (Process process in candidates)
                if (!ReferenceEquals(process, result)) process.Dispose();
            return result;
        }
        catch
        {
            foreach (Process process in candidates) process.Dispose();
            throw;
        }
    }

    private static bool FixedEquals(string left, string right) => left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left.ToLowerInvariant()), Encoding.ASCII.GetBytes(right.ToLowerInvariant()));
}
