using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace QaTelemetry;

public sealed record VerifiedProcessIdentity(int ProcessId, string ImagePath, string ImageSha256, DateTimeOffset CreatedUtc);
public sealed record CandidateTelemetrySample(DateTimeOffset TimestampUtc, int? CandidateAreaId, double? X, double? Y, double? Z, bool? CeaselessFlag11410410, string MapTrust, string PositionTrust, string FlagTrust, string? MapReadError = null, string? PositionReadError = null, string? FlagReadError = null);

public interface IReadOnlyMemory
{
    byte[] Read(ulong address, int length);
}

public static class ProcessIdentityPolicy
{
    public static VerifiedProcessIdentity SelectExactlyOne(IReadOnlyList<VerifiedProcessIdentity> eligible)
    {
        if (eligible.Count == 0) throw new QaTelemetryException("QA_PROCESS_NOT_FOUND", "No process passed the exact isolated QA process identity checks.");
        if (eligible.Count > 1) throw new QaTelemetryException("QA_PROCESS_AMBIGUOUS", "More than one exact QA process passed the process identity checks.");
        return eligible[0];
    }

    public static VerifiedProcessIdentity Verify(int pid, string observedPath, string expectedPath, string observedHash, string expectedHash, DateTimeOffset createdUtc)
    {
        string normalizedObserved = SafePaths.NormalizeFullPath(observedPath);
        string normalizedExpected = SafePaths.NormalizeFullPath(expectedPath);
        try { SafePaths.RejectSteamPath(normalizedObserved); }
        catch (InvalidDataException ex) { throw new QaTelemetryException("LIVE_STEAM_RUNTIME_REJECTED", "Refusing to inspect a process under a Steam runtime path.", ex); }
        if (!string.Equals(normalizedObserved, normalizedExpected, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_PROCESS_PATH_MISMATCH", "The process image path differs from the locked isolated QA executable.");
        if (!FixedEquals(observedHash, expectedHash))
            throw new QaTelemetryException("QA_EXECUTABLE_HASH_MISMATCH", "The process image hash differs from the exact-build lock.");
        if (pid <= 0 || createdUtc.Offset != TimeSpan.Zero) throw new QaTelemetryException("QA_PROCESS_IDENTITY_INVALID", "The process identity is incomplete or malformed.");
        return new(pid, normalizedObserved, observedHash.ToLowerInvariant(), createdUtc);
    }

    private static bool FixedEquals(string a, string b) => a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a.ToLowerInvariant()), Encoding.ASCII.GetBytes(b.ToLowerInvariant()));
}

public sealed class ReadOnlyGameProcess : IDisposable, IReadOnlyMemory
{
    public const uint ExactReadRights = ProcessAccessPolicy.LiveReaderRightsRequested;
    private const uint SnapshotModules = 0x00000008;
    private const uint SnapshotModules32 = 0x00000010;
    private const uint InvalidSnapshot = 0xffffffff;
    private const uint StillActive = 259;
    private SafeProcessHandle? _handle;

    public VerifiedProcessIdentity Identity { get; }
    public ulong ModuleBase { get; }
    public uint GrantedRights => ExactReadRights;
    public bool? IsRunning
    {
        get
        {
            if (_handle is null || _handle.IsClosed) return false;
            return GetExitCodeProcess(_handle, out uint exitCode) ? exitCode == StillActive : null;
        }
    }

    private ReadOnlyGameProcess(SafeProcessHandle handle, VerifiedProcessIdentity identity, ulong moduleBase)
    {
        _handle = handle;
        Identity = identity;
        ModuleBase = moduleBase;
    }

    public static ReadOnlyGameProcess Attach(CaseDescriptor descriptor)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Read-only DSR process telemetry is Windows-only.");
        ProcessAccessPolicy.RequireExactLiveRights(ExactReadRights);
        string expectedPath = SafePaths.NormalizeFullPath(descriptor.Runtime!.ExecutablePath!);
        SafePaths.RejectSteamPath(expectedPath);
        if (!string.Equals(expectedPath, ExactBuildProfile.ExpectedExecutablePath, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_PROCESS_PATH_MISMATCH", "Case descriptor does not name the exact isolated QA executable.");

        Process[] candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(expectedPath));
        if (candidates.Length == 0) throw new QaTelemetryException("QA_PROCESS_NOT_FOUND", "The isolated QA game is not running. Launch only the locked QA executable, then retry.");
        var eligible = new List<VerifiedProcessIdentity>();
        QaTelemetryException? rejection = null;
        foreach (Process process in candidates)
        {
            using (process)
            {
                using SafeProcessHandle query = OpenProcess(ProcessAccessPolicy.QueryLimitedInformation, false, process.Id);
                if (query.IsInvalid)
                {
                    rejection = NativeError("QA_PROCESS_INSUFFICIENT_RIGHTS", "Could not query the DSR process identity with limited-information rights.");
                    continue;
                }
                string observedPath = QueryImagePath(query);
                DateTimeOffset createdUtc = QueryCreatedUtc(query);
                if (!string.Equals(SafePaths.NormalizeFullPath(observedPath), expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsSteamPath(observedPath)) rejection = new QaTelemetryException("LIVE_STEAM_RUNTIME_REJECTED", "A DSR process under Steam was found and is not an eligible telemetry target.");
                    else rejection ??= new QaTelemetryException("QA_PROCESS_PATH_MISMATCH", "A process with the DSR image name has an unexpected path.");
                    continue;
                }

                SafePaths.EnsureNoReparseAncestors(observedPath, leafMustExist: true);
                SafePaths.EnsureUniqueFile(observedPath);
                string imageHash = SafePaths.HashFile(observedPath);
                try
                {
                    VerifiedProcessIdentity identity = ProcessIdentityPolicy.Verify(process.Id, observedPath, expectedPath, imageHash, descriptor.Runtime.ExecutableSha256!, createdUtc);
                    eligible.Add(identity);
                }
                catch (QaTelemetryException ex) { rejection = ex; }
            }
        }

        VerifiedProcessIdentity verified;
        try { verified = ProcessIdentityPolicy.SelectExactlyOne(eligible); }
        catch (QaTelemetryException) when (rejection is not null && eligible.Count == 0) { throw rejection; }
        SafeProcessHandle readHandle = OpenProcess(ExactReadRights, false, verified.ProcessId);
        try
        {
            if (readHandle.IsInvalid) throw NativeError("QA_PROCESS_INSUFFICIENT_RIGHTS", "Could not open the verified QA process with the exact query/read-only rights mask.");
            string recheckedPath = QueryImagePath(readHandle);
            DateTimeOffset recheckedCreated = QueryCreatedUtc(readHandle);
            if (!string.Equals(SafePaths.NormalizeFullPath(recheckedPath), verified.ImagePath, StringComparison.OrdinalIgnoreCase) || recheckedCreated != verified.CreatedUtc)
                throw new QaTelemetryException("QA_PROCESS_IDENTITY_CHANGED", "The PID was reused or its process identity changed during attachment.");
            string recheckedHash = SafePaths.HashFile(recheckedPath);
            _ = ProcessIdentityPolicy.Verify(verified.ProcessId, recheckedPath, expectedPath, recheckedHash, descriptor.Runtime.ExecutableSha256!, recheckedCreated);
            ulong moduleBase = FindModuleBase(verified.ProcessId, Path.GetFileName(expectedPath));
            return new(readHandle, verified, moduleBase);
        }
        catch { readHandle.Dispose(); throw; }
    }

    public byte[] Read(ulong address, int length)
    {
        if (_handle is null || _handle.IsClosed) throw new ObjectDisposedException(nameof(ReadOnlyGameProcess));
        if (length is <= 0 or > 4096 || !IsUserPointer(address) || (ulong)length > ulong.MaxValue - address)
            throw new InvalidDataException("Memory read bounds are invalid.");
        byte[] data = new byte[length];
        if (!ReadProcessMemory(_handle, unchecked((nint)address), data, (nuint)length, out nuint read) || read != (nuint)length)
            throw NativeError("QA_MEMORY_READ_FAILED", "A read-only memory read failed or returned a short buffer.");
        return data;
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }

    public static bool IsUserPointer(ulong pointer) => pointer >= 0x10000 && pointer <= 0x00007fffffffffff && (pointer & 7) == 0;

    private static ulong FindModuleBase(int pid, string moduleName)
    {
        using SafeSnapshotHandle snapshot = CreateToolhelp32Snapshot(SnapshotModules | SnapshotModules32, (uint)pid);
        if (snapshot.IsInvalid) throw NativeError("QA_MODULE_ENUMERATION_FAILED", "Could not enumerate modules for the verified process.");
        var entry = new ModuleEntry32 { Size = (uint)Marshal.SizeOf<ModuleEntry32>() };
        if (!Module32First(snapshot, ref entry)) throw NativeError("QA_MODULE_ENUMERATION_FAILED", "The verified process has no readable module list.");
        do
        {
            if (string.Equals(entry.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                return unchecked((ulong)entry.BaseAddress.ToInt64());
            entry.Size = (uint)Marshal.SizeOf<ModuleEntry32>();
        } while (Module32Next(snapshot, ref entry));
        throw new QaTelemetryException("QA_MODULE_NOT_FOUND", "The verified process does not contain the locked game executable module.");
    }

    private static string QueryImagePath(SafeProcessHandle handle)
    {
        var buffer = new StringBuilder(32768);
        uint size = (uint)buffer.Capacity;
        if (!QueryFullProcessImageName(handle, 0, buffer, ref size)) throw NativeError("QA_PROCESS_PATH_QUERY_FAILED", "Could not read the process image path.");
        return buffer.ToString(0, checked((int)size));
    }

    private static DateTimeOffset QueryCreatedUtc(SafeProcessHandle handle)
    {
        if (!GetProcessTimes(handle, out FileTime created, out _, out _, out _)) throw NativeError("QA_PROCESS_TIME_QUERY_FAILED", "Could not bind the process creation time.");
        long ticks = unchecked((long)(((ulong)created.High << 32) | created.Low));
        return DateTimeOffset.FromFileTime(ticks).ToUniversalTime();
    }

    private static bool IsSteamPath(string path)
    {
        try { SafePaths.RejectSteamPath(path); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static QaTelemetryException NativeError(string code, string message) =>
        new(code, message + " Win32=" + Marshal.GetLastWin32Error().ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static SafeProcessHandle OpenProcess(uint rights, bool inherit, int pid)
    {
        ProcessAccessPolicy.Validate(rights);
        return NativeOpenProcess(rights, inherit, (uint)pid);
    }

    private sealed class SafeSnapshotHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeSnapshotHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ModuleEntry32
    {
        public uint Size;
        public uint ModuleId;
        public uint ProcessId;
        public uint GlobalUsage;
        public uint ProcessUsage;
        public nint BaseAddress;
        public uint ModuleSize;
        public nint ModuleHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ModuleName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern SafeProcessHandle NativeOpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder imageName, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] buffer, nuint size, out nuint bytesRead);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeSnapshotHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Module32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32First(SafeSnapshotHandle snapshot, ref ModuleEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Module32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32Next(SafeSnapshotHandle snapshot, ref ModuleEntry32 entry);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

public static class CandidateTelemetryReader
{
    public static CandidateTelemetrySample Sample(IReadOnlyMemory memory, ulong moduleBase, ExactBuildAuditResult audit, uint bossFlagId = 11410410)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SignatureMatch world = NeedCandidate(audit, "worldChrMan");
        SignatureMatch map = NeedCandidate(audit, "worldChrMapCandidate");
        SignatureMatch flags = NeedCandidate(audit, "eventFlags");
        int? area = null;
        double? x = null, y = null, z = null;
        bool? bossFlag = null;
        string? mapError = null, positionError = null, flagError = null;

        try
        {
            ulong player = ResolvePointerChain(memory, Add(moduleBase, map.TargetRva!.Value), 0, 0x68);
            int candidate = BitConverter.ToInt32(memory.Read(Add(player, 0x354), 4));
            if (IsPlausibleCandidateAreaId(candidate)) area = candidate;
            else mapError = "Candidate map/area field contains a sentinel or implausible value.";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException) { mapError = ex.Message; }

        try
        {
            ulong position = ResolvePointerChain(memory, Add(moduleBase, world.TargetRva!.Value), 0, 0x68, 0x68, 0x28);
            byte[] bytes = memory.Read(Add(position, 0x10), 12);
            float fx = BitConverter.ToSingle(bytes, 0), fy = BitConverter.ToSingle(bytes, 4), fz = BitConverter.ToSingle(bytes, 8);
            if (IsPlausiblePosition(fx, fy, fz))
                (x, y, z) = (fx, fy, fz);
            else positionError = "Candidate XYZ contains a non-finite or implausible coordinate.";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException) { positionError = ex.Message; }

        try
        {
            (int offset, uint mask) = EventFlagAddressing.Resolve(bossFlagId);
            ulong flagManager = ResolvePointerChain(memory, Add(moduleBase, flags.TargetRva!.Value), 0, 0);
            uint word = BitConverter.ToUInt32(memory.Read(Add(flagManager, checked((uint)offset)), 4));
            bossFlag = (word & mask) != 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException) { flagError = ex.Message; }

        return new(now, area, x, y, z, bossFlag,
            area.HasValue ? "CANDIDATE_UNVERIFIED" : "UNAVAILABLE",
            x.HasValue ? "CANDIDATE_UNVERIFIED" : "UNAVAILABLE",
            bossFlag.HasValue ? "CANDIDATE_UNVERIFIED" : "UNAVAILABLE",
            mapError, positionError, flagError);
    }

    public static ulong ResolvePointerChain(IReadOnlyMemory memory, ulong baseAddress, params uint[] offsets)
    {
        ulong address = baseAddress;
        foreach (uint offset in offsets)
        {
            address = Add(address, offset);
            byte[] raw = memory.Read(address, 8);
            address = BitConverter.ToUInt64(raw);
            if (!ReadOnlyGameProcess.IsUserPointer(address)) throw new InvalidDataException("Pointer chain contains a null, misaligned, or out-of-user-range address.");
        }
        return address;
    }

    public static bool IsPlausibleCandidateAreaId(int areaId) => areaId is > 0 and <= 65535;

    public static bool IsPlausiblePosition(float x, float y, float z) =>
        float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) &&
        Math.Abs(x) <= 100000 && Math.Abs(y) <= 100000 && Math.Abs(z) <= 100000;

    public static bool ReadFlag(IReadOnlyMemory memory, ulong moduleBase, ExactBuildAuditResult audit, uint eventFlagId)
    {
        SignatureMatch flags = NeedCandidate(audit, "eventFlags");
        (int offset, uint mask) = EventFlagAddressing.Resolve(eventFlagId);
        ulong flagManager = ResolvePointerChain(memory, Add(moduleBase, flags.TargetRva!.Value), 0, 0);
        uint word = BitConverter.ToUInt32(memory.Read(Add(flagManager, checked((uint)offset)), 4));
        return (word & mask) != 0;
    }

    private static SignatureMatch NeedCandidate(ExactBuildAuditResult audit, string name)
    {
        SignatureMatch match = audit.Signatures.SingleOrDefault(x => x.Name == name)
            ?? throw new InvalidDataException("Exact-build profile lacks signature " + name + ".");
        if (match.MatchCount != 1 || match.Status != "CANDIDATE_UNVERIFIED" || match.TargetRva is null)
            throw new QaTelemetryException("QA_SIGNATURE_UNVERIFIED", "Signature is not unique and structurally valid: " + name);
        return match;
    }

    private static ulong Add(ulong a, uint b) => checked(a + b);
    private static ulong Add(ulong a, int b) => checked(a + (ulong)b);
}

public static class EventFlagAddressing
{
    private static readonly Dictionary<int, int> Groups = new() { [0] = 0x00000, [1] = 0x00500, [5] = 0x05F00, [6] = 0x0B900, [7] = 0x11300 };
    private static readonly Dictionary<int, int> Areas = new()
    {
        [0] = 0, [100] = 1, [101] = 2, [102] = 3, [110] = 4, [120] = 5, [121] = 6, [130] = 7,
        [131] = 8, [132] = 9, [140] = 10, [141] = 11, [150] = 12, [151] = 13, [160] = 14, [170] = 15,
        [180] = 16, [181] = 17
    };

    public static (int ByteOffset, uint Mask) Resolve(uint eventFlagId)
    {
        string digits = eventFlagId.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        if (digits.Length != 8) throw new InvalidDataException("Event flag ID must be an 8-digit DSR flag number.");
        int group = digits[0] - '0';
        int area = int.Parse(digits.AsSpan(1, 3), System.Globalization.CultureInfo.InvariantCulture);
        int section = digits[4] - '0';
        int number = int.Parse(digits.AsSpan(5, 3), System.Globalization.CultureInfo.InvariantCulture);
        if (!Groups.TryGetValue(group, out int groupBase) || !Areas.TryGetValue(area, out int areaIndex) || section is < 0 or > 9 || number is < 0 or > 999)
            throw new InvalidDataException("Event flag ID uses an unsupported DSR flag bank or area.");
        int offset = checked(groupBase + areaIndex * 0x500 + section * 128 + (number - number % 32) / 8);
        uint mask = 0x80000000u >> (number % 32);
        return (offset, mask);
    }
}
