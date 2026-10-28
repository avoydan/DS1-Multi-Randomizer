using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FogReconstruction.PhysicalWitness
{
    public sealed record PhysicalWitnessEdgeCounts(int PROVEN_ACTIONABLE, int PROVEN_NON_ACTIONABLE, int UNKNOWN);
    public sealed record PhysicalWitnessResultCounts(int SafeRejectExperimentalSolvable, int SafeRejectExperimentalUnsolvable, int SafeRejectExperimentalUnresolved);
    public sealed record PhysicalWitnessDatasetIdentity(string Path, string Sha256, int Cases, string StagingRoot, string RelativePathInventorySha256, string PackageByteAuthentication);
    public sealed record PhysicalWitnessBaseline(string Branch, string Commit, string CatalogFingerprint, string SemanticFingerprint, string WorldDataId, string FogHostVersion, int FogHostProtocol, string BackendVersion, string LocalTag);
    public sealed record PhysicalWitnessMechanismCoverage(string Mechanism, string Status, int EvidenceCount, string ProvenFact, string RemainingUnknown);
    public sealed record PhysicalWitnessCaseResult(string Profile, int Seed, string SafeValidatorDisposition, string ExperimentalWitness, string PackageTreeSha256ObservedUnanchored, int PackageTreeFileCount, int CertifiedOutputFileClaim, string RouteClassFingerprint, bool ReachableUnknownEscape, string Explanation, PhysicalWitnessStep[] WitnessSteps, string[] InputErrors);
    public sealed record PhysicalWitnessAuditReport(
        int SchemaVersion,
        PhysicalWitnessBaseline Baseline,
        PhysicalWitnessDatasetIdentity Dataset,
        PhysicalWitnessEdgeCounts PhysicalEdges,
        PhysicalWitnessResultCounts ShadowResults,
        int WitnessClassCount,
        int ManualQaCandidateCount,
        PhysicalWitnessMechanismCoverage[] MechanismCoverage,
        PhysicalWitnessCaseResult[] Cases);

    /// <summary>Read-only comparison of the canonical rejected 331 worlds against the experimental model.</summary>
    public static class Certified331PhysicalWitnessAudit
    {
        public const string CertifiedDatasetSha256 = "c59ac51177ceaed2c9ee8668f535ff0f60dcba6273bcbf743405de2fabbc4340";
        public const string BaselineCommit = "abc6a5286353618a1087b8363bf9438236b8fb10";
        private const int ExpectedCases = 331;
        private const int ExpectedStagingFileCount = 59;
        private const int BossCompletionFlag = 11410410;

        private sealed record TransitionKey(string Object, string SourceArea, string DestinationArea);
        private sealed record Pairing(string Condition, string Locator);
        private sealed record PackageTree(string Sha256, int FileCount, string RelativePathInventorySha256);
        private sealed record EvaluatedCase(PhysicalWitnessCaseResult Result, PhysicalEdge[] Edges, JsonElement Source);

        public static void SelfTestPathSafety()
        {
            string root = Path.Combine(Path.GetTempPath(), "ds1tr-physical-witness-path-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string dataset = Path.Combine(root, "canonical.json");
                string stage = Path.Combine(root, "staging");
                Directory.CreateDirectory(stage);
                File.WriteAllText(dataset, "read-only fixture");
                AssertPathRejected(() => ValidateOutputPaths(dataset, Path.ChangeExtension(dataset, ".md"), dataset, stage),
                    "The report writer accepted the canonical dataset as its output.");
                AssertPathRejected(() => ValidateOutputPaths(Path.Combine(stage, "report.json"), Path.Combine(stage, "report.md"), dataset, stage),
                    "The report writer accepted an output inside staging.");
                string existing = Path.Combine(root, "existing.json");
                File.WriteAllText(existing, "keep");
                AssertPathRejected(() => ValidateOutputPaths(existing, Path.ChangeExtension(existing, ".md"), dataset, stage),
                    "The report writer accepted an existing output path.");
                string colliding = Path.Combine(root, "collision.md");
                AssertPathRejected(() => ValidateOutputPaths(colliding, Path.ChangeExtension(colliding, ".md"), dataset, stage),
                    "The report writer accepted colliding JSON and Markdown paths.");
                Console.WriteLine("Physical witness report path-safety self-tests passed (4 cases).");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static void AssertPathRejected(Action action, string failure)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException) { return; }
            catch (IOException) { return; }
            throw new InvalidOperationException(failure);
        }
        public static void Write(string datasetPath, string stagingRoot, string reportJsonPath)
        {
            string dataset = Path.GetFullPath(datasetPath);
            string stageRoot = Path.GetFullPath(stagingRoot);
            string output = Path.GetFullPath(reportJsonPath);
            string markdown = Path.ChangeExtension(output, ".md");
            ValidateInputPaths(dataset, stageRoot);
            ValidateOutputPaths(output, markdown, dataset, stageRoot);

            string datasetHash = HashFile(dataset);
            if (!string.Equals(datasetHash, CertifiedDatasetSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Certified dataset SHA-256 mismatch: expected {CertifiedDatasetSha256}, got {datasetHash}.");

            using StreamReader reader = new(dataset, Encoding.UTF8, true);
            using JsonDocument document = JsonDocument.Parse(reader.ReadToEnd());
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The certified dataset must be a JSON array.");
            JsonElement[] records = document.RootElement.EnumerateArray().ToArray();
            if (records.Length != ExpectedCases)
                throw new InvalidDataException($"The certified dataset must contain exactly {ExpectedCases} rows; found {records.Length}.");

            var identitySet = new HashSet<string>(StringComparer.Ordinal);
            var evaluated = new List<EvaluatedCase>(records.Length);
            string rootPrefix = WithTrailingSeparator(stageRoot);
            string expectedInventoryHash = null;

            foreach (JsonElement record in records)
            {
                string profile = RequiredString(record, "profile");
                int seed = RequiredInt(record, "seed");
                string identity = profile + "/" + seed.ToString(CultureInfo.InvariantCulture);
                if (!identitySet.Add(identity)) throw new InvalidDataException("Duplicate certified case identity: " + identity + ".");
                if (!string.Equals(RequiredString(record, "graphClass"), "STRICT", StringComparison.Ordinal))
                    throw new InvalidDataException("Non-STRICT case found in canonical dataset: " + identity + ".");
                if (!RequiredBool(record, "generationSucceeded") || !RequiredBool(record, "allOutputHashesValid") || !RequiredBool(record, "allOutputFilesDecode"))
                    throw new InvalidDataException("Certified generation/hash/decode proof is false for " + identity + ".");

                string caseRoot = Path.GetFullPath(RequiredString(record, "staging"));
                if (!caseRoot.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Case staging path escapes the supplied read-only staging root: " + caseRoot);
                PackageTree tree = HashTree(caseRoot);
                if (tree.FileCount != ExpectedStagingFileCount)
                    throw new InvalidDataException($"Expected {ExpectedStagingFileCount} files in {identity}; found {tree.FileCount}.");
                if (expectedInventoryHash == null) expectedInventoryHash = tree.RelativePathInventorySha256;
                else if (!string.Equals(expectedInventoryHash, tree.RelativePathInventorySha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Staging relative-path inventory changed across cases at " + identity + ".");
                int certifiedOutputFiles = RequiredInt(record, "outputFiles");
                var edges = BuildGeneratedTransitionEdges(record, profile, seed, datasetHash, tree.Sha256);
                edges.AddRange(BuildCeaselessUnknownCandidates(record, profile, seed, datasetHash, tree.Sha256));

                PhysicalNode start = new("demonruins", "post-ceaseless-arena-state");
                PhysicalNode mainSide = new("demonruins", "main-demonruins-physical-side-unverified");
                PhysicalNode lavaSide = new("demonruins", "lower-demonruins-lava-physical-side-unverified");
                var query = new PhysicalQuery(
                    start,
                    lavaSide,
                    new[] { BossCompletionFlag },
                    Array.Empty<int>(),
                    new[] { BossCompletionFlag },
                    new[] { mainSide },
                    100000);
                var coverage = new PhysicalCoverage(
                    "complete post-Ceaseless physical route inventory",
                    false,
                    new[] { new PhysicalEvidence("CertifiedDataset", identity + " source record", datasetHash) });
                PhysicalWitnessResult witness = ExperimentalPhysicalWitness.Evaluate(query, edges, coverage);

                var caseResult = new PhysicalWitnessCaseResult(
                    profile,
                    seed,
                    "REJECT (certified dataset selection; validator not rerun)",
                    ToReportName(witness.Verdict),
                    tree.Sha256,
                    tree.FileCount,
                    certifiedOutputFiles,
                    witness.RouteClassFingerprint,
                    witness.ReachableUnknownEscape,
                    witness.Explanation,
                    witness.Steps,
                    Array.Empty<string>());
                evaluated.Add(new EvaluatedCase(caseResult, edges.ToArray(), record));
            }

            if (identitySet.Count != ExpectedCases) throw new InvalidDataException("Canonical case identity count did not match the certified row count.");

            PhysicalEdge[] allEdges = evaluated.SelectMany(item => item.Edges).ToArray();
            var edgeCounts = new PhysicalWitnessEdgeCounts(
                allEdges.Count(edge => edge.Certainty == PhysicalEdgeCertainty.PROVEN_ACTIONABLE),
                allEdges.Count(edge => edge.Certainty == PhysicalEdgeCertainty.PROVEN_NON_ACTIONABLE),
                allEdges.Count(edge => edge.Certainty == PhysicalEdgeCertainty.UNKNOWN));
            var results = new PhysicalWitnessResultCounts(
                evaluated.Count(item => item.Result.SafeValidatorDisposition.StartsWith("REJECT (certified dataset selection; validator not rerun)", StringComparison.Ordinal) && item.Result.ExperimentalWitness == "PHYSICALLY_SOLVABLE_PROVEN"),
                evaluated.Count(item => item.Result.SafeValidatorDisposition.StartsWith("REJECT (certified dataset selection; validator not rerun)", StringComparison.Ordinal) && item.Result.ExperimentalWitness == "PHYSICALLY_UNSOLVABLE_PROVEN"),
                evaluated.Count(item => item.Result.SafeValidatorDisposition.StartsWith("REJECT (certified dataset selection; validator not rerun)", StringComparison.Ordinal) && item.Result.ExperimentalWitness == "UNRESOLVED_MODEL_GAP"));
            string[] positiveFingerprints = evaluated.Select(item => item.Result.RouteClassFingerprint)
                .Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var report = new PhysicalWitnessAuditReport(
                2,
                new PhysicalWitnessBaseline(
                    "fix/compact-fog-v5-progression",
                    BaselineCommit,
                    "73f3c842d51490d45a836a489105b37f57d702eaa2ffda084f42781e5aaef00f",
                    "6c73ee8777629c43759403d0c156cd928107cfcc8d21439ca5f1eff09686e5f0",
                    "ef625777d6aa5abf",
                    "0.7.4 (FogHost.HostVersion 0.7.3)",
                    3,
                    "0.46.4",
                    "v5-safe-ceaseless-baseline"),
                new PhysicalWitnessDatasetIdentity(dataset, datasetHash, records.Length, stageRoot, expectedInventoryHash ?? "", "NOT AUTHENTICATED: canonical dataset contains no expected per-case package hashes; package tree hashes are current staging observations only."),
                edgeCounts,
                results,
                positiveFingerprints.Length,
                0,
                BuildMechanismCoverage(allEdges, evaluated),
                evaluated.Select(item => item.Result).ToArray());
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            WriteNewFile(output, JsonSerializer.Serialize(report, options) + Environment.NewLine);
            WriteNewFile(markdown, RenderMarkdown(report));
            Console.WriteLine($"Physical witness shadow audit: cases={records.Length}, actionable={edgeCounts.PROVEN_ACTIONABLE}, non-actionable={edgeCounts.PROVEN_NON_ACTIONABLE}, unknown={edgeCounts.UNKNOWN}, results={results.SafeRejectExperimentalSolvable}/{results.SafeRejectExperimentalUnsolvable}/{results.SafeRejectExperimentalUnresolved} (solvable/unsolvable/unresolved).");
            Console.WriteLine("JSON: " + output);
            Console.WriteLine("Markdown: " + markdown);
        }

        private static List<PhysicalEdge> BuildGeneratedTransitionEdges(JsonElement record, string profile, int seed, string datasetHash, string packageHash)
        {
            var result = new List<PhysicalEdge>();
            JsonElement[] transitions = RequiredArray(record, "randomTransitions").EnumerateArray().ToArray();
            JsonElement[] pairings = RequiredArray(record, "generatedPairings").EnumerateArray().ToArray();
            var pairingIndex = BuildPairingIndex(pairings);

            for (int index = 0; index < transitions.Length; index++)
            {
                JsonElement transition = transitions[index];
                string text = OptionalString(transition, "transition") ?? "";
                Match match = Regex.Match(text, @"^(?<object>\S+)\s+(?<sourceArea>[^>]+)>(?<destinationArea>\S+)\s+target=");
                string sourceMap = OptionalString(transition, "sourceMap") ?? "unknown-map";
                string destinationMap = OptionalString(transition, "destinationMap") ?? "unknown-map";
                string sourceObject = match.Success ? match.Groups["object"].Value : "unresolved-source-" + index.ToString(CultureInfo.InvariantCulture);
                string destinationPlayer = OptionalString(transition, "destinationPlayer") ?? "unresolved";
                string condition = "unresolved";
                string pairingLocator = "no unique generated pairing";
                bool pairingVerified = false;

                if (match.Success)
                {
                    var key = new TransitionKey(sourceObject, match.Groups["sourceArea"].Value, match.Groups["destinationArea"].Value);
                    if (pairingIndex.TryGetValue(key, out Pairing[] matches) && matches.Length == 1)
                    {
                        condition = matches[0].Condition;
                        pairingLocator = matches[0].Locator;
                        pairingVerified = true;
                    }
                }

                bool structureVerified =
                    BoolIsTrue(transition, "sourceObjectFound")
                    && BoolIsTrue(transition, "sourceActionRegionFound")
                    && BoolIsTrue(transition, "event5700DispatcherFound")
                    && BoolIsTrue(transition, "destinationPlayerFound")
                    && BoolIsTrue(transition, "destinationMapMatches")
                    && BoolIsTrue(transition, "destinationTransformMatches")
                    && match.Success
                    && pairingVerified
                    && string.Equals(condition, "true", StringComparison.Ordinal)
                    && int.TryParse(destinationPlayer, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

                string identity = profile + "/" + seed.ToString(CultureInfo.InvariantCulture);
                string rowLocator = $"cases[{identity}].randomTransitions[{index}]";
                var evidence = new List<PhysicalEvidence>
                {
                    new("CertifiedTransitionRecord", rowLocator, datasetHash),
                    new("ObservedStagingTreeUnanchored", "current bytes for staging package " + identity, packageHash)
                };
                if (BoolIsTrue(transition, "sourceActionRegionFound"))
                    evidence.Add(new PhysicalEvidence("DatasetClaim.SourceActionRegion", rowLocator + ".sourceActionRegionFound", datasetHash));
                if (BoolIsTrue(transition, "event5700DispatcherFound"))
                    evidence.Add(new PhysicalEvidence("DatasetClaim.GeneratedDispatcher", rowLocator + ".event5700DispatcherFound", datasetHash));
                if (BoolIsTrue(transition, "destinationPlayerFound") && BoolIsTrue(transition, "destinationMapMatches") && BoolIsTrue(transition, "destinationTransformMatches"))
                    evidence.Add(new PhysicalEvidence("DatasetClaim.DestinationPlayerMapTransform", rowLocator + ".destinationPlayer/map/transform", datasetHash));
                evidence.Add(new PhysicalEvidence("DatasetClaim.GeneratedPairing", pairingLocator + "; condition=" + condition, datasetHash));
                if (structureVerified)
                    evidence.Add(new PhysicalEvidence("DatasetClaim.StructurallyComplete", rowLocator, datasetHash));

                PhysicalEdgeCertainty certainty = PhysicalEdgeCertainty.UNKNOWN;
                string action = OptionalString(transition, "sourceAction") ?? "unclassified";
                string mechanism = action.Contains("pvp", StringComparison.OrdinalIgnoreCase)
                    ? "randomized-fog-pvp"
                    : action.Contains("boss", StringComparison.OrdinalIgnoreCase)
                        ? "randomized-fog-boss"
                        : "randomized-fog";

                result.Add(new PhysicalEdge(
                    StableId(identity + "|" + text),
                    new PhysicalNode(sourceMap, "fog-action:" + sourceObject),
                    new PhysicalNode(destinationMap, "player:" + destinationPlayer),
                    certainty,
                    mechanism,
                    condition,
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    evidence.ToArray()));
            }

            return result;
        }

        private static List<PhysicalEdge> BuildCeaselessUnknownCandidates(JsonElement record, string profile, int seed, string datasetHash, string packageHash)
        {
            string identity = profile + "/" + seed.ToString(CultureInfo.InvariantCulture);
            string eventDigest = StableId(record.TryGetProperty("event11410090", out JsonElement eventBody) ? eventBody.GetRawText() : "missing-event11410090");
            var evidence = new[]
            {
                new PhysicalEvidence("UnresolvedEventTrace", "cases[" + identity + "].event11410090; digest=" + eventDigest, datasetHash),
                new PhysicalEvidence("ObservedStagingTreeUnanchored", "current bytes for staging package " + identity, packageHash)
            };
            var result = new List<PhysicalEdge>();
            PhysicalNode current = new("demonruins", "post-ceaseless-arena-state");
            JsonElement[] transitions = RequiredArray(record, "randomTransitions").EnumerateArray().ToArray();
            JsonElement[] path = RequiredArray(record, "postBossPath").EnumerateArray().ToArray();
            int matched = 0;

            foreach (JsonElement pathEntry in path)
            {
                string pathText = pathEntry.GetString() ?? "";
                Match pathMatch = Regex.Match(pathText, @"^random:(?<object>[^:]+):(?<sourceArea>[^>]+)>(?<destinationArea>[^:]+):");
                if (!pathMatch.Success) continue;
                string sourceObject = pathMatch.Groups["object"].Value;
                string sourceArea = pathMatch.Groups["sourceArea"].Value;
                string destinationArea = pathMatch.Groups["destinationArea"].Value;
                JsonElement[] sourceMatches = transitions.Where(item =>
                {
                    string text = OptionalString(item, "transition") ?? "";
                    Match match = Regex.Match(text, @"^(?<object>\S+)\s+(?<sourceArea>[^>]+)>(?<destinationArea>\S+)\s+target=");
                    return match.Success && match.Groups["object"].Value == sourceObject;
                }).ToArray();

                matched++;
                string pathLocator = "cases[" + identity + "].postBossPath: " + pathText;
                if (sourceMatches.Length != 1)
                {
                    PhysicalNode unresolvedTrigger = new("unresolved-map", "post-boss-trigger:" + sourceObject);
                    result.Add(new PhysicalEdge(
                        StableId(identity + "|ambiguous-physical-approach|" + matched.ToString(CultureInfo.InvariantCulture)),
                        current,
                        unresolvedTrigger,
                        PhysicalEdgeCertainty.UNKNOWN,
                        "unresolved-post-boss-trigger-identity",
                        "The topology path names a trigger object but no unique physical transition row identifies its map and player landing.",
                        new[] { BossCompletionFlag },
                        Array.Empty<int>(),
                        Array.Empty<int>(),
                        Array.Empty<int>(),
                        Array.Empty<int>(),
                        new[]
                        {
                            new PhysicalEvidence("CertifiedPostBossPathRow", pathLocator, datasetHash),
                            evidence[0],
                            evidence[1]
                        }));
                    current = unresolvedTrigger;
                    continue;
                }

                JsonElement transition = sourceMatches[0];
                string sourceMap = OptionalString(transition, "sourceMap") ?? "unknown-map";
                string destinationMap = OptionalString(transition, "destinationMap") ?? "unknown-map";
                string destinationPlayer = OptionalString(transition, "destinationPlayer") ?? "unresolved";
                PhysicalNode trigger = new(sourceMap, "fog-action:" + sourceObject);
                PhysicalNode destination = new(destinationMap, "player:" + destinationPlayer);
                matched++;
                result.Add(new PhysicalEdge(
                    StableId(identity + "|physical-approach|" + matched.ToString(CultureInfo.InvariantCulture)),
                    current,
                    trigger,
                    PhysicalEdgeCertainty.UNKNOWN,
                    "unverified-physical-approach-to-post-boss-fog",
                    "postBossPath identifies a candidate trigger, but does not prove physical approach or actionability",
                    new[] { BossCompletionFlag },
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    new[]
                    {
                        new PhysicalEvidence("CertifiedPostBossPathRow", pathLocator, datasetHash),
                        evidence[0],
                        evidence[1]
                    }));
                current = destination;
            }

            if (matched > 0 && string.Equals(current.Map, "demonruins", StringComparison.Ordinal))
            {
                PhysicalNode mainSide = new("demonruins", "main-demonruins-physical-side-unverified");
                PhysicalNode lowerSide = new("demonruins", "lower-demonruins-lava-physical-side-unverified");
                result.Add(new PhysicalEdge(
                    StableId(identity + "|candidate-arrival-to-main-side"),
                    current,
                    mainSide,
                    PhysicalEdgeCertainty.UNKNOWN,
                    "unverified-arrival-to-main-demonruins-side",
                    "Destination player placement is present as a dataset claim; walking to the main route is unverified",
                    new[] { BossCompletionFlag },
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    evidence));
                result.Add(new PhysicalEdge(
                    StableId(identity + "|candidate-main-to-lower-lava"),
                    mainSide,
                    lowerSide,
                    PhysicalEdgeCertainty.UNKNOWN,
                    "main-demonruins-to-lower-lava",
                    "The physical route is unresolved; Area.To and ConnectCollision are not traversal proof",
                    new[] { BossCompletionFlag },
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    evidence));
            }
            return result;
        }
        private static Dictionary<TransitionKey, Pairing[]> BuildPairingIndex(JsonElement[] pairings)
        {
            var groups = new Dictionary<TransitionKey, List<Pairing>>();
            for (int index = 0; index < pairings.Length; index++)
            {
                string text = pairings[index].GetString() ?? "";
                Match match = Regex.Match(text, @"^(?<object>[^:]+):(?<sourceArea>[^>]+)>(?<destinationArea>[^ ]+)\s+->\s+.*;\s*condition=(?<condition>.*)$");
                if (!match.Success) continue;
                var key = new TransitionKey(match.Groups["object"].Value, match.Groups["sourceArea"].Value, match.Groups["destinationArea"].Value);
                if (!groups.TryGetValue(key, out List<Pairing> list)) groups.Add(key, list = new List<Pairing>());
                list.Add(new Pairing(match.Groups["condition"].Value, "generatedPairings[" + index.ToString(CultureInfo.InvariantCulture) + "]"));
            }
            return groups.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        }

        private static PhysicalWitnessMechanismCoverage[] BuildMechanismCoverage(PhysicalEdge[] edges, IReadOnlyList<EvaluatedCase> cases)
        {
            int structural = edges.Count(edge => edge.Evidence.Any(item => item.Kind == "DatasetClaim.StructurallyComplete"));
            int ordinary = edges.Count(edge => edge.Mechanism == "randomized-fog"
                && edge.Evidence.Any(item => item.Kind == "DatasetClaim.StructurallyComplete"));
            int pvp = edges.Count(edge => edge.Mechanism == "randomized-fog-pvp"
                && edge.Evidence.Any(item => item.Kind == "DatasetClaim.StructurallyComplete"));
            int bossTransitions = edges.Count(edge => edge.Mechanism == "randomized-fog-boss"
                && edge.Evidence.Any(item => item.Kind == "DatasetClaim.StructurallyComplete"));
            int seath = cases.Sum(item => CountTrace(item.Source, "mode2Trace", "p0Flag=5308"));
            int bossFlag = cases.Count(item => item.Source.TryGetProperty("bossEvent11410410", out JsonElement value)
                && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0);
            int ceaselessCandidates = edges.Count(edge => edge.Mechanism == "unverified-physical-approach-to-post-boss-fog");

            return new[]
            {
                Coverage("all structurally complete transition rows", structural, "STRUCTURAL_CLAIMS_ONLY",
                    "Rows satisfy the dataset structural predicates, including unique pairing condition=true and destination transform claims.",
                    "This is a dataset assertion, not authenticated package evidence or proof of runtime actionability."),
                Coverage("ordinary fog", ordinary, "STRUCTURAL_CLAIMS_ONLY",
                    structural + " rows claim source ActionRegion, Event 5700 dispatch, condition=true pairing, and destination player/map/transform.",
                    "Current package bytes are unanchored and trigger runtime state/physical approach are unknown; no edge is PROVEN_ACTIONABLE."),
                Coverage("PvP", pvp, "STRUCTURAL_CLAIMS_ONLY",
                    "Dataset rows label these transitions as PvP and carry structural source/dispatcher/landing claims.",
                    "Interaction actionability and physical approach remain unknown; no edge is PROVEN_ACTIONABLE."),
                Coverage("Seath p0=5308", seath, seath > 0 ? "TRACE_ROWS_ONLY" : "UNKNOWN",
                    seath > 0 ? "Certified trace text includes p0=5308 rows." : "No p0=5308 trace row was found.",
                    "Trace text is not a physical route witness; source and destination traversal remain unknown."),
                Coverage("fixed links", 0, "UNKNOWN",
                    "No fixed traversal edge is admitted from Area.To or ConnectCollision alone.",
                    "Requires exact directed anchors and traversal evidence."),
                Coverage("doors", 0, "UNKNOWN", "No door edge is currently proven.",
                    "Requires exact interaction, state, and destination-side evidence."),
                Coverage("elevators", 0, "UNKNOWN", "No elevator edge is currently proven.",
                    "Requires verified platform states, call/ride behavior, and destination state."),
                Coverage("boss-boundary randomized fog", bossTransitions, "STRUCTURAL_CLAIMS_ONLY",
                    "Dataset rows claim boss-boundary transition and generated pairing structure.",
                    "Current package bytes and runtime action state are unverified; no edge is PROVEN_ACTIONABLE."),
                Coverage("boss state", bossFlag, bossFlag > 0 ? "TRACE_ROWS_ONLY" : "UNKNOWN",
                    bossFlag > 0 ? "Event 11410410 trace rows are present; the query begins post-boss." : "The certified event 11410410 record is missing.",
                    "Flag presence does not prove an exit or physical route."),
                Coverage("Mode-2 p0=0", 0, "UNKNOWN", "No p0=0 Area Warp is promoted to traversal proof.",
                    "The identified p0=0 families still need exact semantics."),
                Coverage("Ceaseless post-boss path", ceaselessCandidates, "CANDIDATES_ONLY",
                    "Case-specific postBossPath trigger candidates are connected in order from the arena state; the target query requires the main Demon Ruins waypoint before lower lava.",
                    "All approach, randomized edge, arrival-to-main-side, and main-to-lava edges remain UNKNOWN."),
                Coverage("collision and walkability", 0, "UNKNOWN",
                    "No collision or map-load metadata is used as traversal proof.",
                    "Requires complete physical geometry/path evidence or gameplay telemetry.")
            };
        }
        private static PhysicalWitnessMechanismCoverage Coverage(string name, int count, string status, string proven, string unknown) =>
            new(name, status, count, proven, unknown);

        private static int CountTrace(JsonElement record, string property, string marker)
        {
            if (!record.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array) return 0;
            return array.EnumerateArray().Count(item => (item.GetString() ?? "").Contains(marker, StringComparison.Ordinal));
        }

        private static string ToReportName(PhysicalWitnessVerdict verdict) => verdict switch
        {
            PhysicalWitnessVerdict.PHYSICALLY_SOLVABLE_PROVEN => "PHYSICALLY_SOLVABLE_PROVEN",
            PhysicalWitnessVerdict.PHYSICALLY_UNSOLVABLE_PROVEN => "PHYSICALLY_UNSOLVABLE_PROVEN",
            _ => "UNRESOLVED_MODEL_GAP"
        };

        private static string RenderMarkdown(PhysicalWitnessAuditReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Experimental Physical Witness Shadow Audit");
            builder.AppendLine();
            builder.AppendLine("- Safe baseline: " + report.Baseline.Commit + " (" + report.Baseline.LocalTag + ").");
            builder.AppendLine("- Certified dataset SHA-256: " + report.Dataset.Sha256 + "; cases: " + report.Dataset.Cases + ".");
            builder.AppendLine("- Production validator: not invoked or changed by this shadow audit.");
            builder.AppendLine("- Safe disposition is inherited from the certified dataset selection and was not rerun.");
            builder.AppendLine("- Package bytes are NOT authenticated: " + report.Dataset.PackageByteAuthentication);

            builder.AppendLine("- Staging relative-path inventory SHA-256 (shape only): " + report.Dataset.RelativePathInventorySha256 + ".");
            builder.AppendLine();
            builder.AppendLine("## Edge certainty counts");
            builder.AppendLine();
            builder.AppendLine("| Certainty | Count |");
            builder.AppendLine("|---|---:|");
            builder.AppendLine("| PROVEN_ACTIONABLE | " + report.PhysicalEdges.PROVEN_ACTIONABLE + " |");
            builder.AppendLine("| PROVEN_NON_ACTIONABLE | " + report.PhysicalEdges.PROVEN_NON_ACTIONABLE + " |");
            builder.AppendLine("| UNKNOWN | " + report.PhysicalEdges.UNKNOWN + " |");
            builder.AppendLine();
            builder.AppendLine("## Shadow outcomes");
            builder.AppendLine();
            builder.AppendLine("| SAFE REJECT / experimental result | Count |");
            builder.AppendLine("|---|---:|");
            builder.AppendLine("| PHYSICALLY_SOLVABLE_PROVEN | " + report.ShadowResults.SafeRejectExperimentalSolvable + " |");
            builder.AppendLine("| PHYSICALLY_UNSOLVABLE_PROVEN | " + report.ShadowResults.SafeRejectExperimentalUnsolvable + " |");
            builder.AppendLine("| UNRESOLVED_MODEL_GAP | " + report.ShadowResults.SafeRejectExperimentalUnresolved + " |");
            builder.AppendLine();
            builder.AppendLine("## Mechanism coverage");
            builder.AppendLine();
            builder.AppendLine("| Mechanism | Status | Evidence rows | What is proved / remains unknown |");
            builder.AppendLine("|---|---|---:|---|");
            foreach (PhysicalWitnessMechanismCoverage item in report.MechanismCoverage)
                builder.AppendLine("| " + item.Mechanism + " | " + item.Status + " | " + item.EvidenceCount + " | " + item.ProvenFact + " " + item.RemainingUnknown + " |");
            builder.AppendLine();
            builder.AppendLine("Positive witness classes: " + report.WitnessClassCount + ". Manual QA candidates: " + report.ManualQaCandidateCount + ".");
            foreach (PhysicalWitnessCaseResult item in report.Cases.Where(value => value.ExperimentalWitness == "PHYSICALLY_SOLVABLE_PROVEN"))
            {
                builder.AppendLine();
                builder.AppendLine("### Positive witness: " + item.Profile + "/" + item.Seed.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("Safe validator: " + item.SafeValidatorDisposition + ".");
                builder.AppendLine("Route-class fingerprint: " + item.RouteClassFingerprint + ".");
                builder.AppendLine("Start: post-Ceaseless arena; event flag 11410410 is set.");
                foreach (PhysicalWitnessStep step in item.WitnessSteps)
                {
                    builder.AppendLine("- " + step.EdgeId + " (" + step.Mechanism + "): "
                        + step.From.Map + "/" + step.From.Anchor + " -> "
                        + step.To.Map + "/" + step.To.Anchor + "; flags "
                        + string.Join(",", step.EventFlagsBefore) + " -> " + string.Join(",", step.EventFlagsAfter)
                        + "; items " + string.Join(",", step.ItemsBefore) + " -> " + string.Join(",", step.ItemsAfter) + ".");
                    foreach (PhysicalEvidence evidence in step.Evidence)
                        builder.AppendLine("  Evidence: " + evidence.Kind + " at " + evidence.Locator + " (SHA-256 " + evidence.Sha256 + ").");
                }
                builder.AppendLine("Reached: main Demon Ruins waypoint, then lower Demon Ruins/lava goal.");
            }
            builder.AppendLine("No positive class was generated; therefore no manual candidate was selected.");
            builder.AppendLine();
            builder.AppendLine("Generated fog edges are anchored at the exact trigger state and destination player state. This does not assert that the player can physically reach the trigger.");
            builder.AppendLine("The Ceaseless-to-main and main-to-lower-lava candidate edges remain UNKNOWN. Area.To and ConnectCollision are not used as traversal proof.");
            return builder.ToString();
        }

        private static PackageTree HashTree(string directory)
        {
            EnsureNoReparseAncestors(directory, requireLeaf: true);
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(directory));
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Reparse points are not allowed in certified staging: " + entry);
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                    else files.Add(entry);
                }
            }

            string root = Path.GetFullPath(directory);
            string prefix = WithTrailingSeparator(root);
            string[] paths = files
                .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal)
                .ToArray();
            using IncrementalHash aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string path in paths)
            {
                string full = Path.GetFullPath(path);
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A staging file escapes its case directory: " + path);
                string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
                aggregate.AppendData(Encoding.UTF8.GetBytes(relative + "\n"));
                using FileStream stream = new(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                aggregate.AppendData(SHA256.HashData(stream));
                aggregate.AppendData(new byte[] { 0 });
            }
            string inventoryCanonical = string.Join("\n", paths.Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))) + "\n";
            string inventoryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inventoryCanonical))).ToLowerInvariant();
            return new PackageTree(Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant(), paths.Length, inventoryHash);
        }

        private static void ValidateInputPaths(string dataset, string stageRoot)
        {
            EnsureNoReparseAncestors(dataset, requireLeaf: true);
            EnsureNoReparseAncestors(stageRoot, requireLeaf: true);
            if (!File.Exists(dataset)) throw new FileNotFoundException("The certified 331-case dataset is missing.", dataset);
            if (!Directory.Exists(stageRoot)) throw new DirectoryNotFoundException("The certified staging root is missing: " + stageRoot);
            if (IsPathWithin(dataset, stageRoot) || IsPathWithin(stageRoot, dataset))
                throw new InvalidOperationException("The dataset and staging root must be distinct, non-overlapping inputs.");
        }

        private static void ValidateOutputPaths(string output, string markdown, string dataset, string stageRoot)
        {
            if (!string.Equals(Path.GetExtension(output), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The report path must end in .json so its Markdown companion is separate.");
            if (string.Equals(output, markdown, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The JSON and Markdown report paths collide.");

            foreach (string path in new[] { output, markdown })
            {
                EnsureOutsideRepository(path);
                string parent = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                    throw new DirectoryNotFoundException("The report parent directory must already exist: " + parent);
                EnsureNoReparseAncestors(parent, requireLeaf: true);
                if (string.Equals(path, dataset, StringComparison.OrdinalIgnoreCase)
                    || IsPathWithin(path, stageRoot)
                    || IsPathWithin(stageRoot, path))
                    throw new InvalidOperationException("A report path overlaps an input or staging tree: " + path);
                if (File.Exists(path) || Directory.Exists(path))
                    throw new IOException("Refusing to overwrite an existing report or directory: " + path);
            }
        }

        private static void EnsureNoReparseAncestors(string path, bool requireLeaf)
        {
            string full = Path.GetFullPath(path);
            string current = full;
            bool first = true;
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Reparse-point paths are not allowed for physical-witness inputs/outputs: " + current);
                }
                catch (FileNotFoundException) when (!(first && requireLeaf)) { }
                catch (DirectoryNotFoundException) when (!(first && requireLeaf)) { }
                first = false;
                DirectoryInfo parent = Directory.GetParent(current);
                if (parent == null || string.Equals(parent.FullName, current, StringComparison.OrdinalIgnoreCase)) break;
                current = parent.FullName;
            }
            if (requireLeaf && !(File.Exists(full) || Directory.Exists(full)))
                throw new FileNotFoundException("Required physical-witness path does not exist: " + full, full);
        }

        private static bool IsPathWithin(string candidate, string root)
        {
            string fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return fullCandidate.StartsWith(WithTrailingSeparator(fullRoot), StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureOutsideRepository(string path)
        {
            string full = Path.GetFullPath(path);
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo directory = new(Path.GetFullPath(start));
                while (directory != null)
                {
                    string marker = Path.Combine(directory.FullName, ".git");
                    if (File.Exists(marker) || Directory.Exists(marker))
                    {
                        if (IsPathWithin(full, directory.FullName)
                            || string.Equals(full, directory.FullName, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Shadow reports must be written outside the repository: " + full);
                        break;
                    }
                    directory = directory.Parent;
                }
            }
        }

        private static void WriteNewFile(string path, string content)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        private static string WithTrailingSeparator(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;

        private static string HashFile(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static string StableId(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private static bool BoolIsTrue(JsonElement record, string name)
        {
            if (!record.TryGetProperty(name, out JsonElement value)) return false;
            if (value.ValueKind == JsonValueKind.True) return true;
            return value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string RequiredString(JsonElement record, string name) =>
            OptionalString(record, name) is { Length: > 0 } value ? value : throw new InvalidDataException("Required dataset field is missing: " + name);

        private static string OptionalString(JsonElement record, string name)
        {
            if (!record.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        private static int RequiredInt(JsonElement record, string name)
        {
            if (record.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)) return result;
            throw new InvalidDataException("Required integer dataset field is missing or invalid: " + name);
        }

        private static bool RequiredBool(JsonElement record, string name) =>
            record.TryGetProperty(name, out JsonElement value)
            && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase)));

        private static JsonElement RequiredArray(JsonElement record, string name)
        {
            if (!record.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Required array dataset field is missing or invalid: " + name);
            return value;
        }
    }
}