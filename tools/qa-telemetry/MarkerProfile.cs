using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Numerics;

namespace QaTelemetry;

public sealed class MarkerProfileDocument
{
    public int SchemaVersion { get; set; } = 2;
    public string ProfileId { get; set; } = "";
    public string ExecutableProfileId { get; set; } = "";
    public string Status { get; set; } = "UNVERIFIED";
    public string ExecutableSha256 { get; set; } = "";
    public string ToolVersion { get; set; } = "";
    public string TelemetryDefinitionSha256 { get; set; } = "";
    public string CreatedAtUtc { get; set; } = "";
    public string? ValidationSessionId { get; set; }
    public Dictionary<string, int> OwnerConfirmedMapAreaIds { get; set; } = new(StringComparer.Ordinal);
    public MarkerValidationEvidence? Validation { get; set; }
}

public sealed class MarkerValidationEvidence
{
    public string CalibrationExecutablePath { get; set; } = "";
    public string CalibrationRuntimeManifestSha256 { get; set; } = "";
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
    public static MarkerProfileCheck LoadVerified(string path, string expectedExeSha256, string expectedToolVersion, string expectedTelemetryDefinitionSha256)
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
            Validate(profile, expectedExeSha256, expectedToolVersion, expectedTelemetryDefinitionSha256);
            return new(true, "TELEMETRY_EXECUTABLE_PROFILE_VERIFIED", "Owner calibration profile matches the locked executable, telemetry definitions, tool version, and vanilla calibration evidence.", hash, profile);
        }
        catch (Exception ex)
        {
            return new(false, ex is QaTelemetryException qa ? qa.Code : "MARKER_PROFILE_INVALID", ex.Message);
        }
    }

    public static void Validate(MarkerProfileDocument profile, string expectedExeSha256, string expectedToolVersion, string expectedTelemetryDefinitionSha256)
    {
        if (profile.SchemaVersion != 2 || profile.Status != "VERIFIED" ||
            !profile.ProfileId.StartsWith(ExactBuildProfile.MarkerProfileId + "-", StringComparison.Ordinal) ||
            profile.ProfileId.Length > 128 || profile.ProfileId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new InvalidDataException("Executable telemetry profile is not a verified schema-v2 profile.");
        if (!FixedEquals(profile.ExecutableSha256, expectedExeSha256)) throw new QaTelemetryException("MARKER_PROFILE_EXE_MISMATCH", "Marker profile targets a different executable hash.");
        if (!string.Equals(profile.ToolVersion, expectedToolVersion, StringComparison.Ordinal)) throw new QaTelemetryException("MARKER_PROFILE_TOOL_MISMATCH", "Marker profile was validated with a different telemetry tool version.");
        if (!FixedEquals(profile.TelemetryDefinitionSha256, expectedTelemetryDefinitionSha256)) throw new QaTelemetryException("MARKER_PROFILE_SIGNATURE_MISMATCH", "Marker profile signature, pointer-chain, candidate-structure, or flag-layout definitions have changed.");
        if (!string.Equals(profile.ExecutableProfileId, ExactBuildProfile.ExecutableProfileId, StringComparison.Ordinal))
            throw new QaTelemetryException("MARKER_PROFILE_IDENTITY_MISMATCH", "Marker profile identity does not match the exact executable and telemetry-definition profile.");
        if (!DateTimeOffset.TryParse(profile.CreatedAtUtc, out DateTimeOffset created) || created.Offset != TimeSpan.Zero ||
            profile.Validation is null || profile.Validation.ProcessId <= 0 ||
            !DateTimeOffset.TryParse(profile.Validation.ValidatedAtUtc, out DateTimeOffset validated) || validated.Offset != TimeSpan.Zero ||
            !DateTimeOffset.TryParse(profile.Validation.ProcessCreatedUtc, out DateTimeOffset processCreated) || processCreated.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Marker profile validation timestamps or process identity are invalid.");
        string[] expectedMapNames = ["Firelink Shrine", "Northern Undead Asylum"];
        if (profile.OwnerConfirmedMapAreaIds.Count != expectedMapNames.Length || !expectedMapNames.All(name => profile.OwnerConfirmedMapAreaIds.ContainsKey(name)) ||
            profile.OwnerConfirmedMapAreaIds.Values.Distinct().Count() != profile.OwnerConfirmedMapAreaIds.Count ||
            profile.OwnerConfirmedMapAreaIds.Values.Any(areaId => !CandidateTelemetryReader.IsPlausibleCandidateAreaId(areaId)))
            throw new InvalidDataException("Owner has not verified distinct candidate area IDs for vanilla Firelink Shrine and Northern Undead Asylum.");
        MarkerValidationEvidence validation = profile.Validation;
        if (!string.Equals(SafePaths.NormalizeFullPath(validation.CalibrationExecutablePath), ExactBuildProfile.CalibrationExecutablePath, StringComparison.OrdinalIgnoreCase) ||
            !FixedEquals(validation.CalibrationRuntimeManifestSha256, ExactBuildProfile.ExpectedCleanManifestSha256) ||
            !validation.OwnerConfirmedKnownMap || !validation.StationaryPositionStable || !double.IsFinite(validation.StationarySpread) || validation.StationarySpread < 0 || validation.StationarySpread > 0.05 ||
            !validation.PlayerMovementObserved || validation.PlayerMovementDistance < 1.5 ||
            !double.IsFinite(validation.PlayerMovementDistance) || !validation.KnownTransitionObserved ||
            validation.TransitionFromAreaId is null || validation.TransitionToAreaId is null || validation.TransitionFromAreaId == validation.TransitionToAreaId ||
            validation.TransitionFromAreaId != profile.OwnerConfirmedMapAreaIds["Northern Undead Asylum"] ||
            validation.TransitionToAreaId != profile.OwnerConfirmedMapAreaIds["Firelink Shrine"] ||
            !validation.KnownFalseFlagConfirmed || validation.KnownFalseFlagId is null || validation.KnownFalseFlagRead != false || string.IsNullOrWhiteSpace(validation.KnownFalseEvidence) ||
            !validation.KnownTrueFlagConfirmed || validation.KnownTrueFlagId is null || validation.KnownTrueFlagRead != true || string.IsNullOrWhiteSpace(validation.KnownTrueEvidence) || validation.KnownFalseFlagId == validation.KnownTrueFlagId)
            throw new InvalidDataException("One or more clean-runtime executable telemetry calibration checks are missing or failed.");
        _ = EventFlagAddressing.Resolve(validation.KnownFalseFlagId.Value);
        _ = EventFlagAddressing.Resolve(validation.KnownTrueFlagId.Value);
        ExactBuildProfile.RequireApprovedExecutable(ExactBuildProfile.CalibrationExecutablePath, profile.ExecutableSha256);
    }

    public static (string ProfilePath, string ProfileSha256, string SidecarPath) WriteNewVerified(MarkerProfileDocument profile)
    {
        Validate(profile, ExactBuildProfile.ExpectedExecutableSha256, ExactBuildProfile.ToolVersion, ExactBuildProfile.DefinitionSha256);
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
    public static int Run()
    {
        RuntimeManifestVerification runtime = RuntimeManifestService.VerifyCleanCalibrationRuntime();
        if (!runtime.Ok || runtime.Manifest is null || runtime.ManifestSha256 is null)
            throw new QaTelemetryException(runtime.Code, "Clean vanilla calibration runtime verification failed: " + runtime.Message);
        SteamClientStatus steam = SteamClientProbe.Check(QaRuntimePolicy.SteamExecutablePath);
        if (!steam.Ready) throw new QaTelemetryException(steam.Code, steam.Message);
        ExactBuildAuditResult audit = ExactBuildProfile.Audit(ExactBuildProfile.CalibrationExecutablePath);
        if (audit.Status != "EXACT_BUILD_SIGNATURES_UNIQUE_CANDIDATE")
            throw new QaTelemetryException("QA_SIGNATURE_UNVERIFIED", "Exact-build signature anchors were not uniquely resolved; no process was opened.");

        Console.WriteLine("Executable telemetry calibration is read-only and uses only the clean, manifest-verified QA runtime.");
        Console.WriteLine("No generated package, randomized Fog, randomized enemies, or Ceaseless route is used.");
        Console.WriteLine("The launcher verified that the isolated clean QA executable is already running.");
        Console.WriteLine("Load a vanilla DSR world/save, then press Enter here. Do not load a generated package world.");
        _ = Console.ReadLine();
        using ReadOnlyGameProcess process = ReadOnlyGameProcess.Attach(ExactBuildProfile.CalibrationExecutablePath, audit.ExecutableSha256);
        Console.WriteLine($"Attached read-only to PID {process.Identity.ProcessId}; granted rights 0x{process.GrantedRights:X4}.");
        Console.WriteLine("All readings are CANDIDATE_UNVERIFIED until this owner validation completes.");

        var areaAliases = new Dictionary<string, int>(StringComparer.Ordinal);
        Console.WriteLine("Reach Northern Undead Asylum using the vanilla world. When the location is independently confirmed in-game, press Enter.");
        _ = Console.ReadLine();
        CandidateTelemetrySample asylum = CandidateTelemetryReader.Sample(process, process.ModuleBase, audit);
        if (asylum.CandidateAreaId is null)
            throw new QaTelemetryException("MARKER_MAP_UNAVAILABLE", "Candidate Asylum map/area field is unavailable: " + asylum.MapReadError);
        areaAliases.Add("Northern Undead Asylum", asylum.CandidateAreaId.Value);
        Console.WriteLine($"Owner-confirmed Northern Undead Asylum; candidate area ID={asylum.CandidateAreaId.Value}.");

        Console.WriteLine("In the Asylum, stand still at a safe spot, then press Enter for a 5-sample stability check.");
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

        Console.WriteLine("Return to the Asylum's known vanilla departure point. Press Enter before using vanilla progression to reach Firelink Shrine.");
        _ = Console.ReadLine();
        CandidateTelemetrySample before = CandidateTelemetryReader.Sample(process, process.ModuleBase, audit);
        if (before.CandidateAreaId != asylum.CandidateAreaId)
            throw new QaTelemetryException("MARKER_TRANSITION_SOURCE_MISMATCH", "Player is not in the owner-confirmed Asylum source area before the vanilla transition.");
        Console.WriteLine("Use the normal Asylum-to-Firelink progression. Waiting up to 180 seconds for an area ID change...");
        CandidateTelemetrySample after = before;
        DateTimeOffset transitionDeadline = DateTimeOffset.UtcNow.AddSeconds(180);
        while (DateTimeOffset.UtcNow < transitionDeadline && after.CandidateAreaId == before.CandidateAreaId)
            after = SampleAndDelay(process, audit, 500);
        if (after.CandidateAreaId is null || after.CandidateAreaId == before.CandidateAreaId)
            throw new QaTelemetryException("MARKER_TRANSITION_NOT_OBSERVED", "The candidate area ID did not change during vanilla Asylum-to-Firelink progression.");
        Console.WriteLine("Confirm Firelink Shrine from the in-game location name or distinctive vanilla landmarks, then press Enter.");
        _ = Console.ReadLine();
        CandidateTelemetrySample firelink = CandidateTelemetryReader.Sample(process, process.ModuleBase, audit);
        if (firelink.CandidateAreaId != after.CandidateAreaId)
            throw new QaTelemetryException("MARKER_TRANSITION_DESTINATION_MISMATCH", "The candidate map changed after the observed transition; Firelink confirmation is ambiguous.");
        areaAliases.Add("Firelink Shrine", firelink.CandidateAreaId.Value);
        Console.WriteLine($"Owner-confirmed Firelink Shrine; candidate area ID={firelink.CandidateAreaId.Value}.");

        Console.WriteLine("Flag validation: provide one vanilla event flag independently known FALSE and one independently known TRUE.");
        Console.WriteLine("Do not use any randomized-boss state as the true-state check. The chosen in-game facts must be observable in the vanilla world.");
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
            ExecutableProfileId = ExactBuildProfile.ExecutableProfileId,
            Status = "VERIFIED",
            ExecutableSha256 = audit.ExecutableSha256,
            ToolVersion = audit.ToolVersion,
            TelemetryDefinitionSha256 = audit.TelemetryDefinitionSha256,
            CreatedAtUtc = now.ToString("O"),
            ValidationSessionId = sessionId,
            OwnerConfirmedMapAreaIds = areaAliases,
            Validation = new MarkerValidationEvidence
            {
                CalibrationExecutablePath = audit.ExecutablePath,
                CalibrationRuntimeManifestSha256 = runtime.ManifestSha256,
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
            status = "TELEMETRY_EXECUTABLE_PROFILE_VERIFIED",
            executableProfileId = profile.ExecutableProfileId,
            profilePath = path,
            profileSha256 = sha,
            profileSidecarPath = sidecar,
            executableSha256 = audit.ExecutableSha256,
            telemetryDefinitionSha256 = profile.TelemetryDefinitionSha256,
            calibrationRuntimeManifestSha256 = runtime.ManifestSha256,
            toolVersion = profile.ToolVersion,
            mapAreaIds = areaAliases,
            stationarySpread,
            movementDistance = moveDistance,
            transition = new { from = before.CandidateAreaId, to = after.CandidateAreaId },
            falseFlag = new { id = falseId, observed = falseObserved },
            trueFlag = new { id = trueId, observed = trueObserved }
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
