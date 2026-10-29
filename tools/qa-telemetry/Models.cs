using System.Text.Json;
using System.Text.Json.Serialization;

namespace QaTelemetry;

public sealed class CaseDescriptor
{
    // Hash of the owner-specific external case.json, pinned after its generation.
    public const string PinnedDescriptorSha256 = "2b1ff04c7e8b8cef8b6fa61859128911a195dee0cf7ee31b5e9d3f480ddae9ab";
    public const string PinnedRuntimeDescriptorSha256 = "5b78fc8fca21cf950b784a8dd830818181c498275d4139a634a80be695c95547";

    public int SchemaVersion { get; set; }
    public string? CaseId { get; set; }
    public string? SeedCode { get; set; }
    public string? MasterSeed { get; set; }
    public uint RawFogSeed { get; set; }
    public int FogAttempt { get; set; }
    public string? CombinedSeed { get; set; }
    public string? SourceBranch { get; set; }
    public string? SourceCommit { get; set; }
    public string? CatalogFingerprint { get; set; }
    public string? SemanticFingerprint { get; set; }
    public string? WorldDataId { get; set; }
    public string? FogHostProjectVersion { get; set; }
    public string? FogHostHostVersion { get; set; }
    public string? BackendVersion { get; set; }
    public PackageLock? Package { get; set; }
    public RuntimeLock? Runtime { get; set; }
    public TelemetryProfile? TelemetryProfile { get; set; }
    public ExpectedRoute? ExpectedRoute { get; set; }
    public UpstreamComparison? UpstreamComparison { get; set; }

    public static CaseDescriptor Load(string path)
    {
        SafePaths.EnsureNoReparseAncestors(path, leafMustExist: true);
        SafePaths.EnsureUnderTemp(path);
        SafePaths.RejectSteamPath(path);
        var info = new FileInfo(path);
        if (info.Length > 256 * 1024) throw new InvalidDataException("Case descriptor exceeds 256 KiB.");
        string descriptorHash = SafePaths.HashFile(path);
        if (!string.Equals(descriptorHash, PinnedDescriptorSha256, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(descriptorHash, PinnedRuntimeDescriptorSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Case descriptor SHA-256 is not the pinned owner QA case.");
        var descriptor = JsonSerializer.Deserialize<CaseDescriptor>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Case descriptor is empty or invalid.");
        descriptor.Validate();
        return descriptor;
    }

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Unsupported case descriptor schema.");
        Require(CaseId, "caseId");
        Require(SeedCode, "seedCode");
        Require(MasterSeed, "masterSeed");
        Require(CombinedSeed, "combinedSeed");
        Require(SourceBranch, "sourceBranch");
        Require(SourceCommit, "sourceCommit");
        Require(CatalogFingerprint, "catalogFingerprint");
        Require(SemanticFingerprint, "semanticFingerprint");
        Require(WorldDataId, "worldDataId");
        Require(FogHostProjectVersion, "fogHostProjectVersion");
        Require(FogHostHostVersion, "fogHostHostVersion");
        if (Package is null || Runtime is null || TelemetryProfile is null || ExpectedRoute is null || UpstreamComparison is null)
            throw new InvalidDataException("Case descriptor is missing a required identity section.");
        Package.Validate();
        Runtime.Validate();
        if (FogAttempt < 0 || RawFogSeed == 0) throw new InvalidDataException("Fog attempt or seed is invalid.");
        if (TelemetryProfile.Status != "UNVERIFIED")
            throw new InvalidDataException("This case has no independently validated telemetry profile; status must remain UNVERIFIED.");
        string[] requiredFields = ["mapIdentity", "playerXyz", "eventFlag11410410", "mapTransitions", "barrierState", "interactionState", "markerKnownMap", "markerPlayerMovement", "markerVanillaTransition", "markerKnownFlag"];
        if (TelemetryProfile.Fields.Length != requiredFields.Length || requiredFields.Any(name =>
                !TelemetryProfile.Fields.Any(field => field.Name == name && field.Status == "UNVERIFIED" &&
                    field.OffsetSource is null && field.PointerChain is null && field.Validation is null)))
            throw new InvalidDataException("Every telemetry field must be explicitly unverified and without guessed offsets.");
        if (ExpectedRoute.EventFlag != 11410410 || ExpectedRoute.Entrance != "o4961_0000")
            throw new InvalidDataException("Unexpected Ceaseless route identity in case descriptor.");
        string[] expectedCheckpoints = ["CEASELESS_PRE_FIGHT", "CEASELESS_BOSS_ACTIVE", "CEASELESS_DEFEATED", "EXIT_APPROACH", "EXIT_PROMPT_PRESENT", "EXIT_CROSSED", "DESTINATION_LANDED", "MAIN_DEMON_RUINS_REACHED", "LOWER_LAVA_REACHED", "POST_RELOAD_EXIT_VERIFIED"];
        if (!ExpectedRoute.Checkpoints.SequenceEqual(expectedCheckpoints, StringComparer.Ordinal))
            throw new InvalidDataException("Ceaseless checkpoint sequence differs from the fixed QA procedure.");
        string[] expectedEdges = ["boss-completion:11410410", "post-boss-approach:o4961_0000", "post-boss-crossing:o4961_0000", "arrival:main-demon-ruins", "route:main-to-lower-lava"];
        if (!ExpectedRoute.CandidateEdgeIds.SequenceEqual(expectedEdges, StringComparer.Ordinal))
            throw new InvalidDataException("Ceaseless shadow-edge selectors differ from the fixed candidate route.");
        if (UpstreamComparison.Status != "PREPARED_NOT_RUN" || UpstreamComparison.RawFogSeed != RawFogSeed)
            throw new InvalidDataException("Upstream comparison must stay prepared, unrun, and bound to the accepted raw Fog seed.");
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Case descriptor is missing " + name + ".");
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class PackageLock
{
    public string? RootPath { get; set; }
    public string? TreeSha256 { get; set; }
    public string? RunManifestSha256 { get; set; }
    public string? AttestationPath { get; set; }
    public string? AttestationSha256 { get; set; }
    public string? AttestationCaseName { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath) || !Path.IsPathFullyQualified(RootPath)) throw new InvalidDataException("Package root must be an absolute path.");
        SafePaths.RequireSha256(TreeSha256, "package tree hash");
        SafePaths.RequireSha256(RunManifestSha256, "run-manifest hash");
        if (string.IsNullOrWhiteSpace(AttestationPath) || !Path.IsPathFullyQualified(AttestationPath)) throw new InvalidDataException("Contemporaneous attestation path is required.");
        SafePaths.RequireSha256(AttestationSha256, "attestation hash");
        if (string.IsNullOrWhiteSpace(AttestationCaseName)) throw new InvalidDataException("Attestation case name is required.");
    }
}

public sealed class RuntimeLock
{
    public string? RootPath { get; set; }
    public string? ExecutablePath { get; set; }
    public string? ExecutableSha256 { get; set; }
    public string? RuntimeProfileStatus { get; set; }
    public string? CleanRootPath { get; set; }
    public string? CleanManifestPath { get; set; }
    public string? CleanManifestSha256 { get; set; }
    public string? ModifiedManifestPath { get; set; }
    public string? ModifiedManifestSha256 { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath) || !Path.IsPathFullyQualified(RootPath)) throw new InvalidDataException("QA runtime root must be absolute.");
        if (string.IsNullOrWhiteSpace(ExecutablePath) || !Path.IsPathFullyQualified(ExecutablePath)) throw new InvalidDataException("QA executable path must be absolute.");
        SafePaths.RequireSha256(ExecutableSha256, "QA executable hash");
        if (RuntimeProfileStatus != "UNVERIFIED") throw new InvalidDataException("Telemetry marker profile must remain UNVERIFIED until owner marker validation.");
        bool hasBinding = CleanRootPath is not null || CleanManifestPath is not null || CleanManifestSha256 is not null || ModifiedManifestPath is not null || ModifiedManifestSha256 is not null;
        if (hasBinding)
        {
            if (string.IsNullOrWhiteSpace(CleanRootPath) || !Path.IsPathFullyQualified(CleanRootPath) ||
                string.IsNullOrWhiteSpace(CleanManifestPath) || !Path.IsPathFullyQualified(CleanManifestPath) ||
                string.IsNullOrWhiteSpace(ModifiedManifestPath) || !Path.IsPathFullyQualified(ModifiedManifestPath))
                throw new InvalidDataException("Runtime manifest binding paths must be absolute and complete.");
            SafePaths.RequireSha256(CleanManifestSha256, "clean runtime manifest hash");
            SafePaths.RequireSha256(ModifiedManifestSha256, "modified runtime manifest hash");
        }
    }
}

public sealed class TelemetryProfile
{
    public string? Status { get; set; }
    public string? ProfileId { get; set; }
    public string? ProfileSha256 { get; set; }
    public TelemetryField[] Fields { get; set; } = [];
}

public sealed class TelemetryField
{
    public string? Name { get; set; }
    public string? Status { get; set; }
    public string? OffsetSource { get; set; }
    public string? PointerChain { get; set; }
    public string? ValueType { get; set; }
    public string? Validation { get; set; }
}

public sealed class ExpectedRoute
{
    public int EventFlag { get; set; }
    public string? Entrance { get; set; }
    public string? GraphSideDestination { get; set; }
    public string[] Checkpoints { get; set; } = [];
    public string[] CandidateEdgeIds { get; set; } = [];
}

public sealed class UpstreamComparison
{
    public string? Status { get; set; }
    public uint RawFogSeed { get; set; }
    public string? ProfileMapping { get; set; }
    public Dictionary<string, bool> FogOptions { get; set; } = new(StringComparer.Ordinal);
}

public sealed record FileDigest(string Path, long Size, string Sha256);

public sealed record PackageVerification(bool Ok, string Code, string Message, string? TreeSha256 = null, string? RunManifestSha256 = null, int FileCount = 0, long TotalBytes = 0);

public sealed record RuntimePreflightResult(bool Ok, string Code, string[] Blockers, string ExecutableSha256, bool HasRequiredRuntimeAssets, string? CleanManifestSha256 = null, string? ModifiedManifestSha256 = null);

public sealed class TraceRecord
{
    public string Type { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string SessionId { get; set; } = "";
    public long Sequence { get; set; }
    public string TimestampUtc { get; set; } = "";
    public string ExecutableSha256 { get; set; } = "";
    public string PackageTreeSha256 { get; set; } = "";
    public string RunManifestSha256 { get; set; } = "";
    public string Provenance { get; set; } = "";
    public string TelemetryStatus { get; set; } = "UNVERIFIED";
    public string? ProfileSha256 { get; set; }
    public string? ObservedExecutablePath { get; set; }
    public int? ProcessId { get; set; }
    public string? ProcessCreatedUtc { get; set; }
    public string? Checkpoint { get; set; }
    public string ObservationStatus { get; set; } = "UNVERIFIED";
    public string? ObservedMapId { get; set; }
    public double? PlayerX { get; set; }
    public double? PlayerY { get; set; }
    public double? PlayerZ { get; set; }
    public bool? EventFlag11410410 { get; set; }
    public bool? ExitPromptPresent { get; set; }
    public bool? ExitActionable { get; set; }
    public bool? TransitionSucceeded { get; set; }
    public bool? ValidLanding { get; set; }
    public bool? LandingIsolated { get; set; }
    public bool? RouteMilestoneReached { get; set; }
    public string? TransitionType { get; set; }
    public string? Confidence { get; set; }
}

public sealed class TraceSummary
{
    public int SchemaVersion { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public int RecordCount { get; set; }
    public string TraceSha256 { get; set; } = "";
    public bool Completed { get; set; }
}

public sealed record ShadowEdgeResult(string EdgeId, string Certainty);

public sealed record TraceImportResult(string Status, bool GameplayConfirmed, string Explanation, ShadowEdgeResult[] ShadowEdges, int ObservationCount, string TraceSha256);

public sealed class QaTelemetryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
