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

    public TraceSessionWriter(CaseDescriptor descriptor, string tracePath, string sessionId, string provenance = "SYNTHETIC")
    {
        descriptor.Validate();
        if (provenance != "SYNTHETIC")
            throw new NotSupportedException("No verified read-only memory reader or offset profile is available; this build can write synthetic tests only.");
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
        _stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16 * 1024, FileOptions.WriteThrough);
        WriteLine(new TraceRecord
        {
            Type = "header",
            SchemaVersion = 1,
            SessionId = sessionId,
            Sequence = 0,
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            ExecutableSha256 = descriptor.Runtime!.ExecutableSha256!,
            PackageTreeSha256 = descriptor.Package!.TreeSha256!,
            RunManifestSha256 = descriptor.Package.RunManifestSha256!,
            Provenance = "SYNTHETIC",
            TelemetryStatus = "UNVERIFIED",
            ProfileSha256 = null,
            ObservationStatus = "UNVERIFIED",
            Confidence = "TEST_FIXTURE_ONLY"
        });
    }

    public void Append(TraceRecord observation)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        if (observation.Type != "observation") throw new InvalidDataException("Only observation records may follow the trace header.");
        if (!_descriptor.ExpectedRoute!.Checkpoints.Contains(observation.Checkpoint, StringComparer.Ordinal))
            throw new InvalidDataException("Unknown Ceaseless checkpoint: " + observation.Checkpoint);
        if (observation.Provenance is not ("" or "SYNTHETIC") || observation.TelemetryStatus != "UNVERIFIED")
            throw new InvalidDataException("This build only accepts synthetic, unverified observations.");
        observation.SchemaVersion = 1;
        observation.SessionId = _sessionId;
        observation.Sequence = ++_sequence;
        observation.TimestampUtc = DateTimeOffset.UtcNow.ToString("O");
        observation.ExecutableSha256 = _descriptor.Runtime!.ExecutableSha256!;
        observation.PackageTreeSha256 = _descriptor.Package!.TreeSha256!;
        observation.RunManifestSha256 = _descriptor.Package.RunManifestSha256!;
        observation.Provenance = "SYNTHETIC";
        observation.TelemetryStatus = "UNVERIFIED";
        observation.ObservationStatus = "UNVERIFIED";
        observation.ProfileSha256 = null;
        WriteLine(observation);
    }

    public (string SummaryPath, string SummarySha256Path, TraceSummary Summary) Complete()
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        string traceHash = SafePaths.HashFile(_tracePath);
        var summary = new TraceSummary { SessionId = _sessionId, RecordCount = checked((int)_sequence + 1), TraceSha256 = traceHash, Completed = true };
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
        if (summary.SchemaVersion != 1 || !summary.Completed || !FixedEquals(summary.TraceSha256, traceHash))
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
        if (header.Type != "header" || header.SchemaVersion != 1 || header.Sequence != 0 || string.IsNullOrWhiteSpace(header.SessionId))
            throw new InvalidDataException("Trace header is missing or unsupported.");
        if (summary.SessionId != header.SessionId || summary.RecordCount != records.Count)
            throw new InvalidDataException("Trace summary session identity or record count mismatch.");
        if (header.Provenance is not ("SYNTHETIC" or "READ_ONLY_GAME_TELEMETRY"))
            throw new InvalidDataException("Unsupported trace provenance.");
        if (header.TelemetryStatus is not ("UNVERIFIED" or "VERIFIED")) throw new InvalidDataException("Unknown telemetry verification status.");
        if (header.Provenance == "SYNTHETIC" && header.TelemetryStatus != "UNVERIFIED")
            throw new InvalidDataException("Synthetic records cannot claim verified telemetry.");
        if (header.Provenance == "SYNTHETIC" && header.ProfileSha256 is not null)
            throw new InvalidDataException("Synthetic traces cannot name a telemetry profile.");
        if (header.TelemetryStatus == "VERIFIED")
        {
            SafePaths.RequireSha256(header.ProfileSha256, "verified telemetry profile");
        }
        DateTimeOffset previousTimestamp = ParseUtc(header.TimestampUtc);
        if (header.Provenance == "READ_ONLY_GAME_TELEMETRY") ValidateProcessIdentity(descriptor, header);
        if (!MatchesCase(descriptor, header)) throw new InvalidDataException("Trace identity does not match the selected QA case.");

        for (int i = 1; i < records.Count; i++)
        {
            TraceRecord record = records[i];
            if (record.Type != "observation" || record.SchemaVersion != 1 || record.Sequence != i || record.SessionId != header.SessionId)
                throw new InvalidDataException("Trace sequence or session continuity check failed.");
            if (record.Provenance != header.Provenance || record.TelemetryStatus != header.TelemetryStatus || !MatchesCase(descriptor, record))
                throw new InvalidDataException("Trace contains mixed provenance, status, or case identities.");
            if (!string.Equals(record.ProfileSha256, header.ProfileSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Telemetry profile identity changed within the session.");
            DateTimeOffset timestamp = ParseUtc(record.TimestampUtc);
            if (timestamp < previousTimestamp) throw new InvalidDataException("Trace timestamps are not monotonic.");
            previousTimestamp = timestamp;
            if (record.Checkpoint is null || !descriptor.ExpectedRoute!.Checkpoints.Contains(record.Checkpoint, StringComparer.Ordinal))
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
            }
            bool anyPosition = record.PlayerX.HasValue || record.PlayerY.HasValue || record.PlayerZ.HasValue;
            bool fullPosition = record.PlayerX.HasValue && record.PlayerY.HasValue && record.PlayerZ.HasValue;
            if (anyPosition != fullPosition || (fullPosition && (!double.IsFinite(record.PlayerX!.Value) || !double.IsFinite(record.PlayerY!.Value) || !double.IsFinite(record.PlayerZ!.Value))))
                throw new InvalidDataException("Player position must contain three finite coordinates or be absent.");
        }

        if (header.Provenance == "SYNTHETIC")
            return new TraceImportResult("SYNTHETIC_TEST_PASS", false, "Trace structure and hashes are valid; synthetic data can never certify gameplay.", UnknownEdges(descriptor), records.Count - 1, traceHash);

        return new TraceImportResult("TELEMETRY_UNVERIFIED", false,
            "This build verifies trace structure and identity but has no live reader or independent marker-validation rules; gameplay promotion is disabled.",
            UnknownEdges(descriptor), records.Count - 1, traceHash);
    }

    private static bool MatchesCase(CaseDescriptor descriptor, TraceRecord record) =>
        FixedEquals(record.ExecutableSha256, descriptor.Runtime!.ExecutableSha256!) &&
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
        SafePaths.EnsureNoReparseAncestors(observed, leafMustExist: true);
        if (!string.Equals(observed, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Telemetry executable path does not match the locked QA executable.");
        _ = ParseUtc(record.ProcessCreatedUtc);
    }

    private static bool FixedEquals(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left.ToLowerInvariant()), Encoding.ASCII.GetBytes(right.ToLowerInvariant()));
}
