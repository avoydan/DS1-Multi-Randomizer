using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Numerics;

namespace QaTelemetry;

public sealed class MarkerProfileDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string ProfileId { get; set; } = "";
    public string Status { get; set; } = "UNVERIFIED";
    public string ExecutableSha256 { get; set; } = "";
    public string RuntimeDigest { get; set; } = "";
    public string ToolVersion { get; set; } = "";
    public string SignatureDefinitionSha256 { get; set; } = "";
    public string CreatedAtUtc { get; set; } = "";
    public string? ValidationSessionId { get; set; }
    public Dictionary<string, int> OwnerConfirmedMapAreaIds { get; set; } = new(StringComparer.Ordinal);
    public MarkerValidationEvidence? Validation { get; set; }
}

public sealed class MarkerValidationEvidence
{
    public bool OwnerConfirmedKnownMap { get; set; }
    public bool StationaryPositionStable { get; set; }
    public double StationarySpread { get; set; }
    public bool PlayerMovementObserved { get; set; }
    public double PlayerMovementDistance { get; set; }
    public bool KnownTransitionObserved { get; set; }
    public int? TransitionFromAreaId { get; set; }
    public int? TransitionToAreaId { get; set; }
    public bool KnownFalseFlagConfirmed { get; set; }
    public uint? KnownFalseFlagId { get; set; }
    public string? KnownFalseEvidence { get; set; }
    public bool? KnownFalseFlagRead { get; set; }
    public bool KnownTrueFlagConfirmed { get; set; }
    public uint? KnownTrueFlagId { get; set; }
    public string? KnownTrueEvidence { get; set; }
    public bool? KnownTrueFlagRead { get; set; }
    public string ProcessCreatedUtc { get; set; } = "";
    public int ProcessId { get; set; }
    public string ValidatedAtUtc { get; set; } = "";
}

public sealed record MarkerProfileCheck(bool Ok, string Code, string Message, string? ProfileSha256 = null, MarkerProfileDocument? Profile = null);

public static class MarkerProfileStore
{
    public static MarkerProfileCheck LoadVerified(string path, string expectedExeSha256, string expectedRuntimeDigest, string expectedToolVersion, string expectedSignatureDefinitionSha256)
    {
        try
        {
            SafePaths.RejectSteamPath(path);
            SafePaths.EnsureUnderTemp(path);
            SafePaths.EnsureNoReparseAncestors(path, leafMustExist: true);
            SafePaths.EnsureUniqueFile(path);
            string invalidationPath = path + ".invalid";
            if (File.Exists(invalidationPath))
            {
                SafePaths.EnsureNoReparseAncestors(invalidationPath, leafMustExist: true);
                throw new InvalidDataException("Marker profile was invalidated after an exact-build pointer/read validation failure.");
            }
            var info = new FileInfo(path);
            if (info.Length > 128 * 1024) throw new InvalidDataException("Marker profile exceeds its size limit.");
            string hash = SafePaths.HashFile(path);
            string sidecar = path + ".sha256";
            SafePaths.EnsureNoReparseAncestors(sidecar, leafMustExist: true);
            string expectedHash = File.ReadAllText(sidecar).Trim();
            SafePaths.RequireSha256(expectedHash, "marker profile sidecar");
            if (!FixedEquals(hash, expectedHash)) throw new InvalidDataException("Marker profile SHA-256 sidecar does not match the profile file.");
            MarkerProfileDocument profile = JsonSerializer.Deserialize<MarkerProfileDocument>(File.ReadAllText(path), CaseDescriptor.JsonOptions)
                ?? throw new InvalidDataException("Marker profile is malformed.");
            Validate(profile, expectedExeSha256, expectedRuntimeDigest, expectedToolVersion, expectedSignatureDefinitionSha256);
            return new(true, "MARKER_PROFILE_VERIFIED", "Owner marker profile matches the locked executable, runtime, tool, signatures, and validation evidence.", hash, profile);
        }
        catch (Exception ex)
        {
            return new(false, ex is QaTelemetryException qa ? qa.Code : "MARKER_PROFILE_INVALID", ex.Message);
        }
    }

    public static void Validate(MarkerProfileDocument profile, string expectedExeSha256, string expectedRuntimeDigest, string expectedToolVersion, string expectedSignatureDefinitionSha256)
    {
        if (profile.SchemaVersion != 1 || profile.Status != "VERIFIED" || string.IsNullOrWhiteSpace(profile.ProfileId))
            throw new InvalidDataException("Marker profile is not a verified schema-v1 profile.");
        if (!FixedEquals(profile.ExecutableSha256, expectedExeSha256)) throw new QaTelemetryException("MARKER_PROFILE_EXE_MISMATCH", "Marker profile targets a different executable hash.");
        if (!FixedEquals(profile.RuntimeDigest, expectedRuntimeDigest)) throw new QaTelemetryException("MARKER_PROFILE_RUNTIME_MISMATCH", "Marker profile targets a different runtime digest.");
        if (!string.Equals(profile.ToolVersion, expectedToolVersion, StringComparison.Ordinal)) throw new QaTelemetryException("MARKER_PROFILE_TOOL_MISMATCH", "Marker profile was validated with a different telemetry tool version.");
        if (!FixedEquals(profile.SignatureDefinitionSha256, expectedSignatureDefinitionSha256)) throw new QaTelemetryException("MARKER_PROFILE_SIGNATURE_MISMATCH", "Marker profile signature or pointer-chain definitions have changed.");
        if (!DateTimeOffset.TryParse(profile.CreatedAtUtc, out DateTimeOffset created) || created.Offset != TimeSpan.Zero ||
            profile.Validation is null || profile.Validation.ProcessId <= 0 ||
            !DateTimeOffset.TryParse(profile.Validation.ValidatedAtUtc, out DateTimeOffset validated) || validated.Offset != TimeSpan.Zero ||
            !DateTimeOffset.TryParse(profile.Validation.ProcessCreatedUtc, out DateTimeOffset processCreated) || processCreated.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Marker profile validation timestamps or process identity are invalid.");
        string[] expectedMapNames = ["Firelink Shrine", "Northern Undead Asylum", "Demon Ruins"];
        if (!expectedMapNames.All(name => profile.OwnerConfirmedMapAreaIds.ContainsKey(name)) ||
            profile.OwnerConfirmedMapAreaIds.Values.Distinct().Count() != profile.OwnerConfirmedMapAreaIds.Count ||
            profile.OwnerConfirmedMapAreaIds.Values.Any(areaId => !CandidateTelemetryReader.IsPlausibleCandidateAreaId(areaId)))
            throw new InvalidDataException("Owner has not verified distinct candidate area IDs for Firelink, Asylum, and Demon Ruins.");
        MarkerValidationEvidence validation = profile.Validation;
        if (!validation.OwnerConfirmedKnownMap || !validation.StationaryPositionStable || validation.StationarySpread > 0.05 ||
            !validation.PlayerMovementObserved || validation.PlayerMovementDistance < 1.5 ||
            !validation.KnownTransitionObserved || validation.TransitionFromAreaId is null || validation.TransitionToAreaId is null || validation.TransitionFromAreaId == validation.TransitionToAreaId ||
            !validation.KnownFalseFlagConfirmed || validation.KnownFalseFlagId is null || validation.KnownFalseFlagRead != false || string.IsNullOrWhiteSpace(validation.KnownFalseEvidence) ||
            !validation.KnownTrueFlagConfirmed || validation.KnownTrueFlagId is null || validation.KnownTrueFlagRead != true || string.IsNullOrWhiteSpace(validation.KnownTrueEvidence) || validation.KnownFalseFlagId == validation.KnownTrueFlagId)
            throw new InvalidDataException("One or more owner marker validation checks are missing or failed.");
        _ = EventFlagAddressing.Resolve(validation.KnownFalseFlagId.Value);
        _ = EventFlagAddressing.Resolve(validation.KnownTrueFlagId.Value);
        if (!FixedEquals(profile.ExecutableSha256, ExactBuildProfile.ExpectedExecutableSha256) || !FixedEquals(profile.RuntimeDigest, ExactBuildProfile.ExpectedRuntimeDigest))
            throw new InvalidDataException("Marker profile is not bound to the accepted Ceaseless QA case runtime.");
    }

    public static (string ProfilePath, string ProfileSha256, string SidecarPath) WriteNewVerified(MarkerProfileDocument profile)
    {
        Validate(profile, ExactBuildProfile.ExpectedExecutableSha256, ExactBuildProfile.ExpectedRuntimeDigest,
            ExactBuildProfile.ToolVersion, ExactBuildProfile.DefinitionSha256);
        string root = Path.Combine(Path.GetTempPath(), "ds1tr-qa-telemetry", "marker-profiles");
        SafePaths.EnsureNoReparseAncestors(root, leafMustExist: false);
        Directory.CreateDirectory(root);
        SafePaths.EnsureNoReparseAncestors(root, leafMustExist: true);
        string path = Path.Combine(root, profile.ProfileId + ".json");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(profile, CaseDescriptor.JsonOptions);
        string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        string sidecar = path + ".sha256";
        using (var stream = new FileStream(sidecar, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 128, FileOptions.WriteThrough))
        {
            stream.Write(Encoding.ASCII.GetBytes(sha + "\n"));
            stream.Flush(flushToDisk: true);
        }
        return (path, sha, sidecar);
    }

    public static void InvalidateAfterReadFailure(string path, string reason)
    {
        SafePaths.RejectSteamPath(path);
        SafePaths.EnsureUnderTemp(path);
        SafePaths.EnsureNoReparseAncestors(path, leafMustExist: true);
        SafePaths.EnsureUniqueFile(path);
        string invalidationPath = path + ".invalid";
        SafePaths.EnsureNoReparseAncestors(invalidationPath, leafMustExist: false);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            status = "INVALIDATED",
            timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            reasonCode = "POINTER_OR_FIELD_READ_VALIDATION_FAILED",
            reason = reason.Length > 512 ? reason[..512] : reason
        }, CaseDescriptor.JsonOptions);
        using var stream = new FileStream(invalidationPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static bool FixedEquals(string a, string b) => a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a.ToLowerInvariant()), Encoding.ASCII.GetBytes(b.ToLowerInvariant()));
}

public static class MarkerGuide
{
    public static int Run(CaseDescriptor descriptor)
    {
        PackageVerification package = PackageVerifier.Verify(descriptor);
        if (!package.Ok) throw new QaTelemetryException(package.Code, package.Message);
        RuntimePreflightResult runtime = RuntimePreflight.Run(descriptor);
        if (!runtime.Ok || !string.Equals(runtime.ModifiedManifestSha256, descriptor.Runtime!.ModifiedManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_RUNTIME_PREFLIGHT_BLOCKED", string.Join("; ", runtime.Blockers));
        ExactBuildAuditResult audit = ExactBuildProfile.Audit(ExactBuildProfile.ExpectedExecutablePath);
        if (audit.Status != "EXACT_BUILD_SIGNATURES_UNIQUE_CANDIDATE")
            throw new QaTelemetryException("QA_SIGNATURE_UNVERIFIED", "Exact-build signature anchors were not uniquely resolved; no process was opened.");

        Console.WriteLine("Marker validation is read-only. This command will not launch the game or send game input.");
        Console.WriteLine("Launch only the isolated QA executable, load the selected QA world, then press Enter here.");
        Console.WriteLine(ExactBuildProfile.ExpectedExecutablePath);
        _ = Console.ReadLine();
        using ReadOnlyGameProcess process = ReadOnlyGameProcess.Attach(descriptor);
        Console.WriteLine($"Attached read-only to PID {process.Identity.ProcessId}; granted rights 0x{process.GrantedRights:X4}.");
        Console.WriteLine("All readings are CANDIDATE_UNVERIFIED until this owner validation completes.");

        var areaAliases = new Dictionary<string, int>(StringComparer.Ordinal);
        string[] requiredMaps = ["Firelink Shrine", "Northern Undead Asylum", "Demon Ruins"];
        foreach (string mapName in requiredMaps)
        {
            Console.WriteLine($"Travel in-game to {mapName}. When you are physically there, press Enter.");
            _ = Console.ReadLine();
            CandidateTelemetrySample sample = CandidateTelemetryReader.Sample(process, process.ModuleBase, audit);
            if (sample.CandidateAreaId is null)
                throw new QaTelemetryException("MARKER_MAP_UNAVAILABLE", "Candidate map/area field is unavailable: " + sample.MapReadError);
            if (areaAliases.Values.Contains(sample.CandidateAreaId.Value))
                throw new QaTelemetryException("MARKER_MAP_AMBIGUOUS", "Candidate area ID did not distinguish the confirmed " + mapName + " location.");
            areaAliases.Add(mapName, sample.CandidateAreaId.Value);
            Console.WriteLine($"Owner-confirmed location: {mapName}; candidate area ID={sample.CandidateAreaId.Value}.");
        }

        Console.WriteLine("Return to a safe location, stand still, then press Enter for a 5-sample stability check.");
        _ = Console.ReadLine();
        CandidateTelemetrySample[] still = Enumerable.Range(0, 5).Select(_ => SampleAndDelay(process, audit, 1000)).ToArray();
        Vector3[] stillPoints = still.Select(RequirePosition).ToArray();
        double stationarySpread = MaxSpread(stillPoints);
        if (stationarySpread > 0.05) throw new QaTelemetryException("MARKER_XYZ_UNSTABLE", $"Stationary coordinate spread {stationarySpread:F4} exceeded 0.05 m.");

        Vector3 moveStart = RequirePosition(CandidateTelemetryReader.Sample(process, process.ModuleBase, audit));
        Console.WriteLine("Walk normally in-game at least 1.5 m. The helper only reads coordinates and never sends input.");
        Vector3 moveEnd = moveStart;
        DateTimeOffset moveDeadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (DateTimeOffset.UtcNow < moveDeadline && Distance(moveStart, moveEnd) < 1.5)
        {
            moveEnd = RequirePosition(SampleAndDelay(process, audit, 250));
        }
        double moveDistance = Distance(moveStart, moveEnd);
        if (moveDistance < 1.5) throw new QaTelemetryException("MARKER_XYZ_MOVEMENT_MISSING", "No coherent 1.5 m player movement was observed within 90 seconds.");

        Console.WriteLine("From a known location, perform an ordinary in-game transition. Press Enter immediately before crossing it.");
        _ = Console.ReadLine();
        CandidateTelemetrySample before = CandidateTelemetryReader.Sample(process, process.ModuleBase, audit);
        if (before.CandidateAreaId is null) throw new QaTelemetryException("MARKER_TRANSITION_SOURCE_UNAVAILABLE", "Candidate source map area is unavailable.");
        Console.WriteLine("Cross the transition normally. Waiting up to 120 seconds for an area ID change...");
        CandidateTelemetrySample after = before;
        DateTimeOffset transitionDeadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (DateTimeOffset.UtcNow < transitionDeadline && after.CandidateAreaId == before.CandidateAreaId)
            after = SampleAndDelay(process, audit, 500);
        if (after.CandidateAreaId is null || after.CandidateAreaId == before.CandidateAreaId)
            throw new QaTelemetryException("MARKER_TRANSITION_NOT_OBSERVED", "The candidate area ID did not change during the owner-confirmed transition.");

        Console.WriteLine("Flag validation: provide one event flag that you independently know is FALSE and one that you independently know is TRUE.");
        Console.WriteLine("Do not use Ceaseless as the true-state check. You may use 11410410 as FALSE only while you know Ceaseless is undefeated.");
        uint falseId = ReadFlagId("Known-false event flag ID");
        Console.WriteLine("Describe the in-game fact that makes this flag false:");
        string falseEvidence = ReadRequiredLine();
        bool falseObserved = CandidateTelemetryReader.ReadFlag(process, process.ModuleBase, audit, falseId);
        if (falseObserved) throw new QaTelemetryException("MARKER_FLAG_FALSE_MISMATCH", "The selected known-false event flag read TRUE.");
        uint trueId = ReadFlagId("Known-true event flag ID");
        if (trueId == falseId) throw new QaTelemetryException("MARKER_FLAG_IDS_NOT_DISTINCT", "Known-true and known-false checks must use different flag IDs.");
        Console.WriteLine("Describe the in-game fact that makes this flag true:");
        string trueEvidence = ReadRequiredLine();
        bool trueObserved = CandidateTelemetryReader.ReadFlag(process, process.ModuleBase, audit, trueId);
        if (!trueObserved) throw new QaTelemetryException("MARKER_FLAG_TRUE_MISMATCH", "The selected known-true event flag read FALSE.");

        string sessionId = "marker-" + Guid.NewGuid().ToString("N");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var profile = new MarkerProfileDocument
        {
            ProfileId = ExactBuildProfile.MarkerProfileId + "-" + sessionId,
            Status = "VERIFIED",
            ExecutableSha256 = audit.ExecutableSha256,
            RuntimeDigest = ExactBuildProfile.ExpectedRuntimeDigest,
            ToolVersion = audit.ToolVersion,
            SignatureDefinitionSha256 = audit.SignatureDefinitionSha256,
            CreatedAtUtc = now.ToString("O"),
            ValidationSessionId = sessionId,
            OwnerConfirmedMapAreaIds = areaAliases,
            Validation = new MarkerValidationEvidence
            {
                OwnerConfirmedKnownMap = true,
                StationaryPositionStable = true,
                StationarySpread = stationarySpread,
                PlayerMovementObserved = true,
                PlayerMovementDistance = moveDistance,
                KnownTransitionObserved = true,
                TransitionFromAreaId = before.CandidateAreaId,
                TransitionToAreaId = after.CandidateAreaId,
                KnownFalseFlagConfirmed = true,
                KnownFalseFlagId = falseId,
                KnownFalseEvidence = falseEvidence,
                KnownFalseFlagRead = falseObserved,
                KnownTrueFlagConfirmed = true,
                KnownTrueFlagId = trueId,
                KnownTrueEvidence = trueEvidence,
                KnownTrueFlagRead = trueObserved,
                ProcessCreatedUtc = process.Identity.CreatedUtc.ToString("O"),
                ProcessId = process.Identity.ProcessId,
                ValidatedAtUtc = now.ToString("O")
            }
        };
        (string path, string sha, string sidecar) = MarkerProfileStore.WriteNewVerified(profile);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "TELEMETRY_MARKERS_VERIFIED",
            profilePath = path,
            profileSha256 = sha,
            profileSidecarPath = sidecar,
            executableSha256 = audit.ExecutableSha256,
            runtimeDigest = profile.RuntimeDigest,
            toolVersion = profile.ToolVersion,
            mapAreaIds = areaAliases,
            stationarySpread,
            movementDistance = moveDistance,
            transition = new { from = before.CandidateAreaId, to = after.CandidateAreaId },
            falseFlag = new { id = falseId, observed = falseObserved },
            trueFlag = new { id = trueId, observed = trueObserved },
            bossFlag11410410 = CandidateTelemetryReader.Sample(process, process.ModuleBase, audit).CeaselessFlag11410410
        }, CaseDescriptor.JsonOptions));
        return 0;
    }

    private static CandidateTelemetrySample SampleAndDelay(ReadOnlyGameProcess process, ExactBuildAuditResult audit, int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return CandidateTelemetryReader.Sample(process, process.ModuleBase, audit);
    }

    private static Vector3 RequirePosition(CandidateTelemetrySample sample)
    {
        if (sample.X is null || sample.Y is null || sample.Z is null) throw new QaTelemetryException("MARKER_XYZ_UNAVAILABLE", "Player coordinate candidate is unavailable: " + sample.PositionReadError);
        return new((float)sample.X.Value, (float)sample.Y.Value, (float)sample.Z.Value);
    }

    private static double MaxSpread(Vector3[] points) => points.Max(a => points.Max(b => Distance(a, b)));
    private static double Distance(Vector3 a, Vector3 b) => Vector3.Distance(a, b);

    private static uint ReadFlagId(string prompt)
    {
        Console.WriteLine(prompt + ":");
        string input = ReadRequiredLine();
        if (!uint.TryParse(input, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out uint value))
            throw new InvalidDataException("Event flag ID must be an unsigned decimal integer.");
        _ = EventFlagAddressing.Resolve(value);
        return value;
    }

    private static string ReadRequiredLine() => Console.ReadLine() is { Length: > 0 } value ? value.Trim() : throw new InvalidDataException("Owner validation input was empty.");
}
