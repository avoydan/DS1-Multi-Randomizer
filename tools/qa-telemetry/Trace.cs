using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QaTelemetry;

public static class TraceFormat
{
    public static readonly JsonSerializerOptions JsonOptions = new(CaseDescriptor.JsonOptions) { WriteIndented = false };
}

public sealed class TraceSessionWriter : IDisposable
{
    private readonly CaseDescriptor _descriptor;
    private readonly FileStream _stream;
    private readonly string _tracePath;
    private readonly string _sessionId;
    private long _sequence;
    private bool _completed;
    private readonly TraceRecord _header;
    private readonly VerifiedProcessIdentity? _liveIdentity;
    private readonly string? _profilePath;
    private readonly string? _profileSha256;
    private readonly MarkerProfileDocument? _profile;
    private readonly string _runtimeDigest;

    public TraceSessionWriter(CaseDescriptor descriptor, string tracePath, string sessionId, string provenance = "SYNTHETIC")
        : this(descriptor, tracePath, sessionId, RequireSynthetic(provenance)) { }

    internal static TraceSessionWriter CreateLive(CaseDescriptor descriptor, string tracePath, string sessionId,
        string markerProfilePath, VerifiedProcessIdentity identity, string runtimeDigest)
    {
        descriptor.Validate();
        if (!string.Equals(SafePaths.NormalizeFullPath(descriptor.Runtime!.ExecutablePath!), ExactBuildProfile.ExpectedExecutablePath, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_PROCESS_PATH_MISMATCH", "Live trace requires the exact locked isolated QA executable path.");
        if (!string.Equals(runtimeDigest, ExactBuildProfile.ExpectedRuntimeDigest, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_RUNTIME_IDENTITY_MISMATCH", "Live trace requires the exact locked package-applied runtime digest.");
        SafePaths.RequireSha256(descriptor.Runtime.ModifiedManifestSha256, "package-applied runtime manifest hash");
        VerifiedProcessIdentity verifiedIdentity = ProcessIdentityPolicy.Verify(identity.ProcessId, identity.ImagePath,
            descriptor.Runtime!.ExecutablePath!, identity.ImageSha256, descriptor.Runtime.ExecutableSha256!, identity.CreatedUtc);
        MarkerProfileCheck profile = MarkerProfileStore.LoadVerified(markerProfilePath,
            descriptor.Runtime!.ExecutableSha256!, ExactBuildProfile.ToolVersion, ExactBuildProfile.DefinitionSha256);
        if (!profile.Ok || profile.Profile is null || profile.ProfileSha256 is null)
            throw new QaTelemetryException(profile.Code, profile.Message);
        return new TraceSessionWriter(descriptor, tracePath, sessionId,
            new LiveTraceBinding(markerProfilePath, profile.ProfileSha256, profile.Profile, verifiedIdentity, runtimeDigest));
    }

    private TraceSessionWriter(CaseDescriptor descriptor, string tracePath, string sessionId, LiveTraceBinding? live)
    {
        descriptor.Validate();
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 80 || sessionId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Session id must be a short ASCII identifier.", nameof(sessionId));
        SafePaths.RejectSteamPath(tracePath);
        string full = Path.GetFullPath(tracePath);
        SafePaths.EnsureUnderTemp(full);
        string parent = Path.GetDirectoryName(full)!;
        SafePaths.EnsureNoReparseAncestors(parent, leafMustExist: false);
        Directory.CreateDirectory(parent);
        SafePaths.EnsureNoReparseAncestors(parent, leafMustExist: true);
        _descriptor = descriptor;
        _tracePath = full;
        _sessionId = sessionId;
        _liveIdentity = live?.Identity;
        _profilePath = live?.ProfilePath;
        _profileSha256 = live?.ProfileSha256;
        _profile = live?.Profile;
        _runtimeDigest = live?.RuntimeDigest ?? "";
        _stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16 * 1024, FileOptions.WriteThrough);
        _header = new TraceRecord
        {
            Type = "header",
            SchemaVersion = 2,
            SessionId = sessionId,
            Sequence = 0,
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            CaseId = descriptor.CaseId!,
            SeedCode = descriptor.SeedCode!,
            ExecutableSha256 = descriptor.Runtime!.ExecutableSha256!,
            ExecutableProfileId = live?.Profile.ExecutableProfileId,
            RuntimeManifestSha256 = descriptor.Runtime.ModifiedManifestSha256 ?? "",
            PackageTreeSha256 = descriptor.Package!.TreeSha256!,
            RunManifestSha256 = descriptor.Package.RunManifestSha256!,
            Provenance = live is null ? "SYNTHETIC" : "READ_ONLY_GAME_TELEMETRY",
            TelemetryStatus = live is null ? "UNVERIFIED" : "VERIFIED",
            ProfileSha256 = live?.ProfileSha256,
            ProfilePath = live?.ProfilePath,
            RuntimeDigest = live?.RuntimeDigest ?? "",
            ToolVersion = ExactBuildProfile.ToolVersion,
            ObservedExecutablePath = live?.Identity.ImagePath,
            ProcessId = live?.Identity.ProcessId,
            ProcessCreatedUtc = live?.Identity.CreatedUtc.ToString("O"),
            ProcessRightsRequested = live is null ? ProcessAccessPolicy.PreflightRightsRequested : ReadOnlyGameProcess.ExactReadRights,
            ObservationStatus = "UNVERIFIED",
            Confidence = "TEST_FIXTURE_ONLY"
        };
        if (live is not null)
        {
            _header.Confidence = "OWNER_MARKER_PROFILE_VERIFIED";
            _header.ObservationStatus = "VERIFIED";
        }
        WriteLine(_header);
    }

    public void Append(TraceRecord observation)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        if (observation.Type != "observation") throw new InvalidDataException("Only observation records may follow the trace header.");
        if (observation.Checkpoint is not null && !_descriptor.ExpectedRoute!.Checkpoints.Contains(observation.Checkpoint, StringComparer.Ordinal))
            throw new InvalidDataException("Unknown Ceaseless checkpoint: " + observation.Checkpoint);
        if (_liveIdentity is null && (observation.Provenance is not ("" or "SYNTHETIC") || observation.TelemetryStatus != "UNVERIFIED"))
            throw new InvalidDataException("Synthetic writer cannot accept live or verified observations.");
        if (_liveIdentity is not null && (observation.Provenance is not ("" or "READ_ONLY_GAME_TELEMETRY") || observation.TelemetryStatus is not ("" or "VERIFIED")))
            throw new InvalidDataException("Live writer accepts only verified observations from its bound profile.");
        observation.SchemaVersion = 2;
        observation.SessionId = _sessionId;
        observation.Sequence = ++_sequence;
        observation.TimestampUtc = DateTimeOffset.UtcNow.ToString("O");
        observation.CaseId = _descriptor.CaseId!;
        observation.SeedCode = _descriptor.SeedCode!;
        observation.ExecutableSha256 = _descriptor.Runtime!.ExecutableSha256!;
        observation.ExecutableProfileId = _profile?.ExecutableProfileId;
        observation.RuntimeManifestSha256 = _descriptor.Runtime.ModifiedManifestSha256 ?? "";
        observation.PackageTreeSha256 = _descriptor.Package!.TreeSha256!;
        observation.RunManifestSha256 = _descriptor.Package.RunManifestSha256!;
        observation.Provenance = _liveIdentity is null ? "SYNTHETIC" : "READ_ONLY_GAME_TELEMETRY";
        observation.TelemetryStatus = _liveIdentity is null ? "UNVERIFIED" : "VERIFIED";
        observation.ObservationStatus = _liveIdentity is null ? "UNVERIFIED" : "VERIFIED";
        observation.ProfileSha256 = _profileSha256;
        observation.ProfilePath = _profilePath;
        observation.RuntimeDigest = _runtimeDigest;
        observation.ToolVersion = ExactBuildProfile.ToolVersion;
        observation.ObservedExecutablePath = _liveIdentity?.ImagePath;
        observation.ProcessId = _liveIdentity?.ProcessId;
        observation.ProcessCreatedUtc = _liveIdentity?.CreatedUtc.ToString("O");
        observation.ProcessRightsRequested = _liveIdentity is null ? ProcessAccessPolicy.PreflightRightsRequested : ReadOnlyGameProcess.ExactReadRights;
        if (_liveIdentity is null)
        {
            observation.MapTrust = "UNAVAILABLE";
            observation.PositionTrust = "UNAVAILABLE";
            observation.EventFlagTrust = "UNAVAILABLE";
            observation.TransitionTrust = "UNAVAILABLE";
        }
        else
        {
            if (observation.CandidateMapAreaId is int areaId && _profile!.OwnerConfirmedMapAreaIds.FirstOrDefault(entry => entry.Value == areaId) is { Key.Length: > 0 } mapAlias)
            {
                observation.MapTrust = "SESSION_VERIFIED";
                observation.ObservedMapId = mapAlias.Key;
            }
            else observation.MapTrust = observation.CandidateMapAreaId.HasValue ? "CANDIDATE_UNVERIFIED" : "UNAVAILABLE";
            observation.PositionTrust = observation.PlayerX.HasValue && observation.PlayerY.HasValue && observation.PlayerZ.HasValue ? "SESSION_VERIFIED" : "UNAVAILABLE";
            observation.EventFlagTrust = observation.EventFlag11410410.HasValue ? "SESSION_VERIFIED" : "UNAVAILABLE";
            int[] verifiedAreas = _profile!.OwnerConfirmedMapAreaIds.Values
                .Concat([_profile.Validation!.TransitionFromAreaId!.Value, _profile.Validation.TransitionToAreaId!.Value])
                .Distinct().ToArray();
            bool transitionKnown = observation.TransitionFromAreaId.HasValue && observation.TransitionToAreaId.HasValue &&
                verifiedAreas.Contains(observation.TransitionFromAreaId.Value) && verifiedAreas.Contains(observation.TransitionToAreaId.Value);
            observation.TransitionTrust = transitionKnown ? "SESSION_VERIFIED" : observation.TransitionFromAreaId.HasValue ? "CANDIDATE_UNVERIFIED" : "UNAVAILABLE";
        }
        WriteLine(observation);
    }

    public (string SummaryPath, string SummarySha256Path, TraceSummary Summary) Complete()
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        string traceHash = SafePaths.HashFile(_tracePath);
        var summary = new TraceSummary
        {
            SchemaVersion = 2,
            SessionId = _sessionId,
            RecordCount = checked((int)_sequence + 1),
            TraceSha256 = traceHash,
            CaseId = _header.CaseId,
            SeedCode = _header.SeedCode,
            ExecutableSha256 = _header.ExecutableSha256,
            ExecutableProfileId = _header.ExecutableProfileId,
            RuntimeManifestSha256 = _header.RuntimeManifestSha256,
            RuntimeDigest = _runtimeDigest,
            PackageTreeSha256 = _header.PackageTreeSha256,
            RunManifestSha256 = _header.RunManifestSha256,
            ProfileSha256 = _profileSha256,
            ObservedExecutablePath = _header.ObservedExecutablePath,
            ProcessId = _header.ProcessId,
            ProcessCreatedUtc = _header.ProcessCreatedUtc,
            StartUtc = _header.TimestampUtc,
            EndUtc = DateTimeOffset.UtcNow.ToString("O"),
            Completed = true
        };
        string summaryPath = _tracePath + ".summary.json";
        string summaryShaPath = _tracePath + ".summary.sha256";
        WriteNew(summaryPath, JsonSerializer.SerializeToUtf8Bytes(summary, CaseDescriptor.JsonOptions));
        string summaryHash = SafePaths.HashFile(summaryPath);
        WriteNew(summaryShaPath, Encoding.ASCII.GetBytes(summaryHash + "\n"));
        _completed = true;
        return (summaryPath, summaryShaPath, summary);
    }

    public void Dispose()
    {
        if (!_completed) _stream.Dispose();
        _completed = true;
    }

    private void WriteLine(TraceRecord record)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(record, TraceFormat.JsonOptions);
        _stream.Write(json);
        _stream.WriteByte((byte)'\n');
        _stream.Flush(flushToDisk: true);
    }

    private static void WriteNew(string path, byte[] data)
    {
        SafePaths.EnsureNoReparseAncestors(Path.GetDirectoryName(path)!, leafMustExist: true);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }

    private sealed record LiveTraceBinding(string ProfilePath, string ProfileSha256, MarkerProfileDocument Profile, VerifiedProcessIdentity Identity, string RuntimeDigest);

    private static LiveTraceBinding? RequireSynthetic(string provenance)
    {
        if (provenance != "SYNTHETIC")
            throw new NotSupportedException("Live recording requires a verified owner marker profile and verified process identity.");
        return null;
    }
}

public static class TraceImporter
{
    private const int MaxTraceBytes = 16 * 1024 * 1024;
    private const int MaxRecords = 10000;

    public static TraceImportResult Import(CaseDescriptor descriptor, string tracePath, string summaryPath, string summaryShaPath)
    {
        PackageVerification package = PackageVerifier.Verify(descriptor);
        if (!package.Ok) throw new QaTelemetryException(package.Code, package.Message);
        SafePaths.RejectSteamPath(tracePath);
        SafePaths.RejectSteamPath(summaryPath);
        SafePaths.RejectSteamPath(summaryShaPath);
        SafePaths.EnsureUnderTemp(tracePath);
        SafePaths.EnsureUnderTemp(summaryPath);
        SafePaths.EnsureUnderTemp(summaryShaPath);
        SafePaths.EnsureNoReparseAncestors(tracePath, leafMustExist: true);
        SafePaths.EnsureNoReparseAncestors(summaryPath, leafMustExist: true);
        SafePaths.EnsureNoReparseAncestors(summaryShaPath, leafMustExist: true);
        var traceInfo = new FileInfo(tracePath);
        if (traceInfo.Length > MaxTraceBytes) throw new InvalidDataException("Trace exceeds 16 MiB.");
        if (new FileInfo(summaryPath).Length > 64 * 1024 || new FileInfo(summaryShaPath).Length > 80)
            throw new InvalidDataException("Trace summary or summary-hash file exceeds its size limit.");
        string traceHash = SafePaths.HashFile(tracePath);
        string summaryHash = SafePaths.HashFile(summaryPath);
        string recordedSummaryHash = File.ReadAllText(summaryShaPath).TrimEnd('\r', '\n');
        SafePaths.RequireSha256(recordedSummaryHash, "trace summary hash");
        if (!FixedEquals(summaryHash, recordedSummaryHash)) throw new InvalidDataException("Trace summary SHA-256 mismatch.");

        TraceSummary summary = JsonSerializer.Deserialize<TraceSummary>(File.ReadAllText(summaryPath), CaseDescriptor.JsonOptions)
            ?? throw new InvalidDataException("Trace summary is malformed.");
        if (summary.SchemaVersion != 2 || !summary.Completed || !FixedEquals(summary.TraceSha256, traceHash))
            throw new InvalidDataException("Trace is truncated, incomplete, or differs from its session summary.");

        string[] lines = File.ReadAllLines(tracePath, Encoding.UTF8);
        if (lines.Length < 1 || lines.Length > MaxRecords + 1 || lines.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Trace record count or line structure is invalid.");
        var records = new List<TraceRecord>(lines.Length);
        foreach (string line in lines)
        {
            if (Encoding.UTF8.GetByteCount(line) > 64 * 1024) throw new InvalidDataException("Trace line exceeds 64 KiB.");
            records.Add(JsonSerializer.Deserialize<TraceRecord>(line, TraceFormat.JsonOptions) ?? throw new InvalidDataException("Trace contains malformed JSON."));
        }
        TraceRecord header = records[0];
        if (header.Type != "header" || header.SchemaVersion != 2 || summary.SchemaVersion != 2 || header.Sequence != 0 || string.IsNullOrWhiteSpace(header.SessionId))
            throw new InvalidDataException("Trace header is missing or unsupported.");
        if (summary.SessionId != header.SessionId || summary.RecordCount != records.Count)
            throw new InvalidDataException("Trace summary session identity or record count mismatch.");
        if (summary.CaseId != header.CaseId || summary.SeedCode != header.SeedCode ||
            !FixedEquals(summary.ExecutableSha256, header.ExecutableSha256) ||
            summary.ExecutableProfileId != header.ExecutableProfileId ||
            !FixedEquals(summary.RuntimeManifestSha256, header.RuntimeManifestSha256) ||
            !FixedEquals(summary.RuntimeDigest, header.RuntimeDigest) ||
            !FixedEquals(summary.PackageTreeSha256, header.PackageTreeSha256) ||
            !FixedEquals(summary.RunManifestSha256, header.RunManifestSha256) ||
            !string.Equals(summary.ObservedExecutablePath, header.ObservedExecutablePath, StringComparison.OrdinalIgnoreCase) ||
            summary.ProcessId != header.ProcessId || summary.ProcessCreatedUtc != header.ProcessCreatedUtc ||
            !string.Equals(summary.ProfileSha256, header.ProfileSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Trace summary identity does not match its session header.");
        if (header.Provenance is not ("SYNTHETIC" or "READ_ONLY_GAME_TELEMETRY"))
            throw new InvalidDataException("Unsupported trace provenance.");
        if (header.TelemetryStatus is not ("UNVERIFIED" or "VERIFIED")) throw new InvalidDataException("Unknown telemetry verification status.");
        if (header.Provenance == "SYNTHETIC" && header.TelemetryStatus != "UNVERIFIED")
            throw new InvalidDataException("Synthetic records cannot claim verified telemetry.");
        if (header.Provenance == "SYNTHETIC" && header.ProfileSha256 is not null)
            throw new InvalidDataException("Synthetic traces cannot name a telemetry profile.");
        if (header.Provenance == "READ_ONLY_GAME_TELEMETRY" && header.TelemetryStatus == "UNVERIFIED" &&
            (header.ProfileSha256 is not null || header.ProfilePath is not null))
            throw new InvalidDataException("Unverified live telemetry cannot claim a marker profile.");
        if (header.Provenance == "READ_ONLY_GAME_TELEMETRY")
        {
            if (header.RuntimeDigest != ExactBuildProfile.ExpectedRuntimeDigest ||
                !string.Equals(header.RuntimeManifestSha256, descriptor.Runtime!.ModifiedManifestSha256 ?? "", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Live telemetry header does not match the locked package-applied runtime identity.");
            if (header.ProcessRightsRequested != ReadOnlyGameProcess.ExactReadRights)
                throw new InvalidDataException("Live telemetry header does not declare the exact query/read-only rights mask.");
            ProcessAccessPolicy.Validate(header.ProcessRightsRequested);
        }
        if (header.TelemetryStatus == "VERIFIED")
        {
            SafePaths.RequireSha256(header.ProfileSha256, "verified telemetry profile");
            if (header.Provenance != "READ_ONLY_GAME_TELEMETRY" || string.IsNullOrWhiteSpace(header.ProfilePath) ||
                header.RuntimeDigest != ExactBuildProfile.ExpectedRuntimeDigest || header.ToolVersion != ExactBuildProfile.ToolVersion ||
                header.ProcessRightsRequested != ReadOnlyGameProcess.ExactReadRights ||
                string.IsNullOrWhiteSpace(header.ExecutableProfileId))
                throw new InvalidDataException("Verified telemetry header lacks the exact case, runtime, tool, executable-profile, or process-rights binding.");
            MarkerProfileCheck profile = MarkerProfileStore.LoadVerified(header.ProfilePath,
                descriptor.Runtime!.ExecutableSha256!, header.ToolVersion, ExactBuildProfile.DefinitionSha256);
            if (!profile.Ok || profile.Profile is null || !FixedEquals(profile.ProfileSha256!, header.ProfileSha256!) ||
                !string.Equals(profile.Profile.ExecutableProfileId, header.ExecutableProfileId, StringComparison.Ordinal))
                throw new InvalidDataException("Trace marker profile is missing, corrupt, or does not match its recorded hash: " + profile.Message);
            if (!FixedEquals(summary.ExecutableSha256, header.ExecutableSha256) || !FixedEquals(summary.RuntimeDigest, header.RuntimeDigest) ||
                !FixedEquals(summary.RuntimeManifestSha256, header.RuntimeManifestSha256) ||
                !FixedEquals(summary.PackageTreeSha256, header.PackageTreeSha256) || !FixedEquals(summary.RunManifestSha256, header.RunManifestSha256) ||
                !FixedEquals(summary.ProfileSha256!, header.ProfileSha256!) || summary.StartUtc != header.TimestampUtc ||
                summary.CaseId != header.CaseId || summary.SeedCode != header.SeedCode ||
                summary.ExecutableProfileId != header.ExecutableProfileId ||
                !string.Equals(summary.ObservedExecutablePath, header.ObservedExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                summary.ProcessId != header.ProcessId || summary.ProcessCreatedUtc != header.ProcessCreatedUtc)
                throw new InvalidDataException("Trace summary linkage does not match its session header.");
        }
        DateTimeOffset previousTimestamp = ParseUtc(header.TimestampUtc);
        DateTimeOffset? previousSampleTimestamp = null;
        if (header.Provenance == "READ_ONLY_GAME_TELEMETRY") ValidateProcessIdentity(descriptor, header);
        if (!MatchesCase(descriptor, header)) throw new InvalidDataException("Trace identity does not match the selected QA case.");

        for (int i = 1; i < records.Count; i++)
        {
            TraceRecord record = records[i];
            if (record.Type != "observation" || record.SchemaVersion != 2 || record.Sequence != i || record.SessionId != header.SessionId)
                throw new InvalidDataException("Trace sequence or session continuity check failed.");
            if (record.Provenance != header.Provenance || record.TelemetryStatus != header.TelemetryStatus || !MatchesCase(descriptor, record))
                throw new InvalidDataException("Trace contains mixed provenance, status, or case identities.");
            if (record.RuntimeDigest != header.RuntimeDigest || record.ToolVersion != header.ToolVersion ||
                record.ExecutableProfileId != header.ExecutableProfileId ||
                record.RuntimeManifestSha256 != header.RuntimeManifestSha256 ||
                !string.Equals(record.ProfilePath, header.ProfilePath, StringComparison.OrdinalIgnoreCase) ||
                record.ProcessRightsRequested != header.ProcessRightsRequested)
                throw new InvalidDataException("Trace runtime, tool, or marker-profile linkage changed within the session.");
            if (!string.Equals(record.ProfileSha256, header.ProfileSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Telemetry profile identity changed within the session.");
            DateTimeOffset timestamp = ParseUtc(record.TimestampUtc);
            if (timestamp < previousTimestamp) throw new InvalidDataException("Trace timestamps are not monotonic.");
            previousTimestamp = timestamp;
            bool anyPosition = record.PlayerX.HasValue || record.PlayerY.HasValue || record.PlayerZ.HasValue;
            bool fullPosition = record.PlayerX.HasValue && record.PlayerY.HasValue && record.PlayerZ.HasValue;
            if (record.SampleTimestampUtc is not null)
            {
                DateTimeOffset sampleTimestamp = ParseUtc(record.SampleTimestampUtc);
                if (sampleTimestamp > timestamp)
                    throw new InvalidDataException("Captured sample timestamp is later than its trace record.");
                if (previousSampleTimestamp.HasValue && sampleTimestamp < previousSampleTimestamp.Value)
                    throw new InvalidDataException("Captured sample timestamps are not monotonic.");
                previousSampleTimestamp = sampleTimestamp;
            }
            if (record.OwnerAnnotationTimestampUtc is not null)
            {
                DateTimeOffset ownerTimestamp = ParseUtc(record.OwnerAnnotationTimestampUtc);
                if (ownerTimestamp < ParseUtc(header.TimestampUtc) || ownerTimestamp > timestamp || ownerTimestamp > ParseUtc(summary.EndUtc))
                    throw new InvalidDataException("Owner annotation timestamp falls outside the trace session.");
            }
            if (record.OwnerConfirmedCheckpoint == true && record.OwnerAnnotationTimestampUtc is null)
                throw new InvalidDataException("Owner-confirmed checkpoint lacks its explicit annotation timestamp.");
            if (record.Checkpoint is not null && !descriptor.ExpectedRoute!.Checkpoints.Contains(record.Checkpoint, StringComparer.Ordinal))
                throw new InvalidDataException("Trace contains an unknown checkpoint.");
            if (record.ObservationStatus is not ("UNVERIFIED" or "VERIFIED")) throw new InvalidDataException("Unknown observation verification status.");
            if ((header.Provenance == "SYNTHETIC" || header.TelemetryStatus != "VERIFIED") && record.ObservationStatus != "UNVERIFIED")
                throw new InvalidDataException("An observation claims verification without a verified telemetry profile.");
            if (record.Provenance == "READ_ONLY_GAME_TELEMETRY")
            {
                ValidateProcessIdentity(descriptor, record);
                if (record.ProcessId != header.ProcessId || record.ProcessCreatedUtc != header.ProcessCreatedUtc ||
                    !string.Equals(record.ObservedExecutablePath, header.ObservedExecutablePath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Process identity changed within a telemetry session.");
                if (record.MapTrust is not ("UNAVAILABLE" or "CANDIDATE_UNVERIFIED" or "MARKER_VERIFIED" or "SESSION_VERIFIED") ||
                    record.PositionTrust is not ("UNAVAILABLE" or "CANDIDATE_UNVERIFIED" or "MARKER_VERIFIED" or "SESSION_VERIFIED") ||
                    record.EventFlagTrust is not ("UNAVAILABLE" or "CANDIDATE_UNVERIFIED" or "MARKER_VERIFIED" or "SESSION_VERIFIED") ||
                    record.TransitionTrust is not ("UNAVAILABLE" or "CANDIDATE_UNVERIFIED" or "MARKER_VERIFIED" or "SESSION_VERIFIED"))
                    throw new InvalidDataException("Trace contains an unknown field trust state.");
                if ((record.CandidateMapAreaId.HasValue && record.MapTrust == "UNAVAILABLE") ||
                    (fullPosition && record.PositionTrust == "UNAVAILABLE") ||
                    (record.EventFlag11410410.HasValue && record.EventFlagTrust == "UNAVAILABLE"))
                    throw new InvalidDataException("Trace has a value whose field trust state is unavailable.");
            }
            if (anyPosition != fullPosition || (fullPosition && (!double.IsFinite(record.PlayerX!.Value) || !double.IsFinite(record.PlayerY!.Value) || !double.IsFinite(record.PlayerZ!.Value))))
                throw new InvalidDataException("Player position must contain three finite coordinates or be absent.");
        }

        if (header.Provenance == "SYNTHETIC")
            return new TraceImportResult("SYNTHETIC_TEST_PASS", false, "Trace structure and hashes are valid; synthetic data can never certify gameplay.", UnknownEdges(descriptor), records.Count - 1, traceHash);

        if (header.TelemetryStatus != "VERIFIED")
            return new TraceImportResult("TELEMETRY_UNVERIFIED", false, "Live observations without an exact-build verified marker profile remain untrusted.", UnknownEdges(descriptor), records.Count - 1, traceHash);

        return ShadowRouteClassifier.Classify(descriptor, records, traceHash);
    }

    private static bool MatchesCase(CaseDescriptor descriptor, TraceRecord record) =>
        string.Equals(record.CaseId, descriptor.CaseId, StringComparison.Ordinal) &&
        string.Equals(record.SeedCode, descriptor.SeedCode, StringComparison.Ordinal) &&
        FixedEquals(record.ExecutableSha256, descriptor.Runtime!.ExecutableSha256!) &&
        string.Equals(record.RuntimeManifestSha256, descriptor.Runtime.ModifiedManifestSha256 ?? "", StringComparison.OrdinalIgnoreCase) &&
        FixedEquals(record.PackageTreeSha256, descriptor.Package!.TreeSha256!) &&
        FixedEquals(record.RunManifestSha256, descriptor.Package.RunManifestSha256!);

    private static ShadowEdgeResult[] UnknownEdges(CaseDescriptor descriptor) =>
        descriptor.ExpectedRoute!.CandidateEdgeIds.Select(id => new ShadowEdgeResult(id, "UNKNOWN")).ToArray();

    private static DateTimeOffset ParseUtc(string value)
    {
        if (!DateTimeOffset.TryParse(value, out DateTimeOffset timestamp) || timestamp.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Trace timestamp is not a valid UTC timestamp.");
        return timestamp;
    }

    private static void ValidateProcessIdentity(CaseDescriptor descriptor, TraceRecord record)
    {
        if (record.ProcessId is null or <= 0 || record.ProcessCreatedUtc is null || record.ObservedExecutablePath is null)
            throw new InvalidDataException("Live telemetry lacks a bound process identity.");
        string observed = SafePaths.NormalizeFullPath(record.ObservedExecutablePath);
        string expected = SafePaths.NormalizeFullPath(descriptor.Runtime!.ExecutablePath!);
        SafePaths.RejectSteamPath(observed);
        if (!string.Equals(observed, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Telemetry executable path does not match the locked QA executable.");
        SafePaths.EnsureNoReparseAncestors(observed, leafMustExist: true);
        if (!FixedEquals(SafePaths.HashFile(observed), descriptor.Runtime.ExecutableSha256!))
            throw new InvalidDataException("Telemetry executable file hash no longer matches the locked exact build.");
        _ = ParseUtc(record.ProcessCreatedUtc);
    }

    private static bool FixedEquals(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left.ToLowerInvariant()), Encoding.ASCII.GetBytes(right.ToLowerInvariant()));
}
