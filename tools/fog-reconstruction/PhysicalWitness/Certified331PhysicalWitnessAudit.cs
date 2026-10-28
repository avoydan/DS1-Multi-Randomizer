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
    public sealed record PhysicalWitnessMechanismCoverage(
        string Mechanism,
        string Status,
        int EvidenceCount,
        string ProvenFact,
        string RemainingUnknown,
        int CertifiedEdges = 0,
        int AffectedWorlds = 0,
        int UnknownEdges = 0,
        int UnknownAffectedWorlds = 0);
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
        PhysicalEdge[] CertifiedEdges,
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
                var stagedReader = new StagedTransitionEvidenceReader(caseRoot);
                var edges = BuildGeneratedTransitionEdges(record, profile, seed, datasetHash, tree.Sha256, stagedReader);
                edges.AddRange(stagedReader.ReadMode2Edges(identity, datasetHash, tree.Sha256));
                edges.AddRange(BuildCeaselessUnknownCandidates(record, profile, seed, datasetHash, tree.Sha256, edges));

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
                3,
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
                allEdges.Where(edge => edge.Certainty == PhysicalEdgeCertainty.PROVEN_ACTIONABLE).ToArray(),
                evaluated.Select(item => item.Result).ToArray());
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            WriteNewFile(output, JsonSerializer.Serialize(report, options) + Environment.NewLine);
            WriteNewFile(markdown, RenderMarkdown(report));
            Console.WriteLine($"Physical witness shadow audit: cases={records.Length}, actionable={edgeCounts.PROVEN_ACTIONABLE}, non-actionable={edgeCounts.PROVEN_NON_ACTIONABLE}, unknown={edgeCounts.UNKNOWN}, results={results.SafeRejectExperimentalSolvable}/{results.SafeRejectExperimentalUnsolvable}/{results.SafeRejectExperimentalUnresolved} (solvable/unsolvable/unresolved).");
            Console.WriteLine("JSON: " + output);
            Console.WriteLine("Markdown: " + markdown);
        }

        private static List<PhysicalEdge> BuildGeneratedTransitionEdges(
            JsonElement record,
            string profile,
            int seed,
            string datasetHash,
            string packageHash,
            StagedTransitionEvidenceReader reader)
        {
            var result = new List<PhysicalEdge>();
            JsonElement[] transitions = RequiredArray(record, "randomTransitions").EnumerateArray().ToArray();
            JsonElement[] pairings = RequiredArray(record, "generatedPairings").EnumerateArray().ToArray();
            var pairingIndex = BuildPairingIndex(pairings);
            string identity = profile + "/" + seed.ToString(CultureInfo.InvariantCulture);

            for (int index = 0; index < transitions.Length; index++)
            {
                JsonElement transition = transitions[index];
                string text = OptionalString(transition, "transition") ?? "";
                Match match = Regex.Match(text, @"^(?<object>\S+)\s+(?<sourceArea>[^>]+)>(?<destinationArea>\S+)\s+target=");
                string sourceMap = OptionalString(transition, "sourceMap") ?? "unknown-map";
                string destinationMap = OptionalString(transition, "destinationMap") ?? "unknown-map";
                string sourceObject = match.Success ? match.Groups["object"].Value : "unresolved-source-" + index.ToString(CultureInfo.InvariantCulture);
                string sourceAction = OptionalString(transition, "sourceAction") ?? "unclassified";
                string destinationPlayer = OptionalString(transition, "destinationPlayer") ?? "unresolved";
                string condition = "unresolved";
                string pairingLocator = "no unique generated pairing";

                if (match.Success)
                {
                    var key = new TransitionKey(sourceObject, match.Groups["sourceArea"].Value, match.Groups["destinationArea"].Value);
                    if (pairingIndex.TryGetValue(key, out Pairing[] matches) && matches.Length == 1)
                    {
                        condition = matches[0].Condition;
                        pairingLocator = matches[0].Locator;
                    }
                }

                result.Add(reader.ReadRandomTransition(identity, index, text, sourceMap, sourceAction,
                    destinationMap, destinationPlayer, condition, pairingLocator, datasetHash, packageHash));
            }

            return result;
        }

        private static List<PhysicalEdge> BuildCeaselessUnknownCandidates(
            JsonElement record,
            string profile,
            int seed,
            string datasetHash,
            string packageHash,
            IReadOnlyList<PhysicalEdge> generatedTransitions)
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
                    return match.Success
                        && match.Groups["object"].Value == sourceObject
                        && match.Groups["sourceArea"].Value == sourceArea
                        && match.Groups["destinationArea"].Value == destinationArea;
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
                string transitionText = OptionalString(transition, "transition") ?? "";
                string transitionId = StableId(identity + "|" + transitionText);
                PhysicalEdge generated = generatedTransitions.SingleOrDefault(edge => edge.Id == transitionId);
                if (generated == null)
                {
                    PhysicalNode unresolved = new("unresolved-map", "post-boss-trigger:" + sourceObject + ":side:" + sourceArea);
                    result.Add(new PhysicalEdge(
                        StableId(identity + "|unmatched-physical-approach|" + matched.ToString(CultureInfo.InvariantCulture)),
                        current,
                        unresolved,
                        PhysicalEdgeCertainty.UNKNOWN,
                        "unresolved-post-boss-trigger-identity",
                        "No independently parsed staging edge maps to this exact source object and side.",
                        new[] { BossCompletionFlag }, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
                        new[] { new PhysicalEvidence("CertifiedPostBossPathRow", pathLocator, datasetHash), evidence[0], evidence[1] }));
                    current = unresolved;
                    continue;
                }
                PhysicalNode trigger = generated.From;
                PhysicalNode destination = generated.To;
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
            PhysicalWitnessMechanismCoverage ByRule(string name, string mechanismClass, string provenFact, string remaining)
            {
                PhysicalEdge[] members = edges.Where(edge =>
                    edge.Certification?.MechanismClass == mechanismClass
                    || edge.Mechanism == mechanismClass).ToArray();
                PhysicalEdge[] proven = members.Where(edge => edge.Certainty == PhysicalEdgeCertainty.PROVEN_ACTIONABLE).ToArray();
                PhysicalEdge[] unknown = members.Where(edge => edge.Certainty == PhysicalEdgeCertainty.UNKNOWN).ToArray();
                string status = proven.Length > 0 ? "STAGED_BYTES_CERTIFIED" : members.Length > 0 ? "UNKNOWN" : "NO_CANDIDATE_ROWS";
                string reasonSummary = string.Join("; ", members.Where(edge => edge.Certainty == PhysicalEdgeCertainty.UNKNOWN)
                    .Select(edge => edge.Certification?.Reason ?? edge.Condition)
                    .Where(reason => !string.IsNullOrWhiteSpace(reason))
                    .GroupBy(reason => reason, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Take(3)
                    .Select(group => group.Key + " (" + group.Count().ToString(CultureInfo.InvariantCulture) + ")"));
                string bossPolicySummary = mechanismClass == GeneratedTransitionCertifier.BossBoundaryMode0
                    ? " Project-owned boss entrance policies observed: " + members.SelectMany(edge => edge.Evidence)
                        .Where(item => item.Kind == "BossLifecyclePolicy")
                        .Select(item => Regex.Match(item.Locator, @"boss-policy (?<id>\S+) side", RegexOptions.CultureInvariant))
                        .Where(match => match.Success).Select(match => match.Groups["id"].Value)
                        .Distinct(StringComparer.Ordinal).Count().ToString(CultureInfo.InvariantCulture) + "."
                    : string.Empty;
                return new PhysicalWitnessMechanismCoverage(name, status, members.Length,
                    provenFact + " Direct certified edges: " + proven.Length.ToString(CultureInfo.InvariantCulture) + "." + bossPolicySummary,
                    string.IsNullOrEmpty(reasonSummary) ? remaining : reasonSummary,
                    proven.Length,
                    members.Select(CaseIdentity).Where(value => value != null).Distinct(StringComparer.Ordinal).Count(),
                    unknown.Length,
                    unknown.Select(CaseIdentity).Where(value => value != null).Distinct(StringComparer.Ordinal).Count());
            }

            int seathTraces = cases.Sum(item => CountTrace(item.Source, "mode2Trace", "p0Flag=5308"));
            int bossFlag = cases.Count(item => item.Source.TryGetProperty("bossEvent11410410", out JsonElement value)
                && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0);
            string[] ceaselessMechanisms =
            {
                "unverified-physical-approach-to-post-boss-fog",
                "unresolved-post-boss-trigger-identity",
                "unverified-arrival-to-main-demonruins-side",
                "main-demonruins-to-lower-lava",
            };
            PhysicalEdge[] ceaselessEdges = edges.Where(edge => ceaselessMechanisms.Contains(edge.Mechanism, StringComparer.Ordinal)).ToArray();
            int ceaselessCandidates = ceaselessEdges.Length;
            int ceaselessWorlds = ceaselessEdges.Select(CaseIdentity).Where(value => value != null).Distinct(StringComparer.Ordinal).Count();
            int ceaselessApproachEdges = ceaselessEdges.Count(edge => edge.Mechanism == "unverified-physical-approach-to-post-boss-fog"
                || edge.Mechanism == "unresolved-post-boss-trigger-identity");
            int ceaselessArrivalEdges = ceaselessEdges.Count(edge => edge.Mechanism == "unverified-arrival-to-main-demonruins-side");
            int ceaselessLowerRouteEdges = ceaselessEdges.Count(edge => edge.Mechanism == "main-demonruins-to-lower-lava");
            int p0Zero = edges.Count(edge => edge.Certification?.MechanismClass == "mode2-unsupported"
                && edge.Certification.Dispatcher.Contains("p0=0", StringComparison.Ordinal));

            return new[]
            {
                ByRule("ordinary Event5700 Mode 0", GeneratedTransitionCertifier.OrdinaryMode0,
                    "Current staged object, ActionRegion, initializer, dispatcher contract, destination player/map, and landing bytes were reparsed.",
                    "Physical approach to each trigger remains outside the certified edge."),
                ByRule("PvP randomized Mode 0", GeneratedTransitionCertifier.PvpMode0,
                    "Current staging was checked against exact project PvP gate identity/side policy; Mode 1 is excluded.",
                    "Conditional PvP sides and physical approach remain UNKNOWN."),
                ByRule("project CustomWarp", GeneratedTransitionCertifier.ProjectCustomWarpMode0,
                    "Only the two explicit Firelink CustomWarp policy sides are eligible; generated coordinates and yaw are checked exactly.",
                    "Any other map override or unmatched CustomWarp remains UNKNOWN."),
                ByRule("Seath Mode 2 p0=5308", GeneratedTransitionCertifier.SeathMode2P05308,
                    "The staged crystal object, map event-0 initializer, event 11700005 flag producer, Mode 2 dispatcher, map/player, and runtime prison region are independently checked.",
                    seathTraces.ToString(CultureInfo.InvariantCulture) + " dataset traces are locators only; current package hashes are not historically anchored."),
                ByRule("boss-boundary Mode 0", GeneratedTransitionCertifier.BossBoundaryMode0,
                    "Staged boundary bindings are checked against project-owned boss entrance identities.",
                    "Encounter activation, defeat-state ownership, and post-boss exit lifecycle are not fully proven."),
                Coverage("Mode-2 p0=0", p0Zero, "UNKNOWN",
                    "Mode-2 p0=0 call sites are retained as UNKNOWN candidates; they are never generalized from Seath.",
                    "Each source producer and destination semantic still needs a separate exact rule."),
                Coverage("boss completion state", bossFlag, bossFlag > 0 ? "TRACE_ROWS_ONLY" : "UNKNOWN",
                    bossFlag > 0 ? "Event 11410410 trace rows are present; the query starts after the boss." : "The certified event 11410410 record is missing.",
                    "Flag presence does not establish an exit or a physical route.", 0, bossFlag, 0, 0),
                Coverage("Ceaseless post-boss physical approach/route", ceaselessCandidates, "CANDIDATES_ONLY",
                    "Case-specific trigger sides retain exact object/area identity from staged transition parsing; breakdown: "
                    + ceaselessApproachEdges.ToString(CultureInfo.InvariantCulture) + " trigger-approach/identity, "
                    + ceaselessArrivalEdges.ToString(CultureInfo.InvariantCulture) + " arrival-to-main-side, "
                    + ceaselessLowerRouteEdges.ToString(CultureInfo.InvariantCulture) + " main-to-lower-lava.",
                    "All listed edges remain UNKNOWN; fixed physical walking and approach evidence is absent.", 0, ceaselessWorlds,
                    ceaselessCandidates, ceaselessWorlds),
                Coverage("fixed walkable links", 0, "UNKNOWN",
                    "No fixed traversal edge is promoted from logical Area.To metadata.",
                    "Needs exact directed source/destination anchors plus physical traversal proof across the affected worlds."),
                Coverage("doors", 0, "UNKNOWN", "No door edge is promoted.",
                    "Needs interaction, lock/state, and destination-side evidence."),
                Coverage("elevators", 0, "UNKNOWN", "No elevator edge is promoted.",
                    "Needs platform positions, call/ride state, and destination evidence."),
                Coverage("collision and walkability", 0, "UNKNOWN",
                    "No ConnectCollision or geometry-only edge is promoted.",
                    "Needs a complete physical path/walkability proof or verified gameplay observation.")
            };
        }

        private static string CaseIdentity(PhysicalEdge edge)
        {
            PhysicalEvidence locator = edge.Evidence.FirstOrDefault(item => item.Kind == "CertifiedDatasetLocator");
            if (locator != null)
            {
                int marker = locator.Locator.IndexOf(".randomTransitions[", StringComparison.Ordinal);
                if (marker > 0) return locator.Locator.Substring(0, marker);
            }
            PhysicalEvidence caseLocator = edge.Evidence.FirstOrDefault(item => item.Kind is "CertifiedPostBossPathRow" or "UnresolvedEventTrace");
            if (caseLocator != null)
            {
                Match match = Regex.Match(caseLocator.Locator, @"cases\[(?<identity>[^\]]+)\]", RegexOptions.CultureInvariant);
                if (match.Success) return match.Groups["identity"].Value;
            }
            PhysicalEvidence staging = edge.Evidence.FirstOrDefault(item => item.Kind == "ObservedStagingTreeUnanchored");
            if (staging == null) return null;
            const string packagePrefix = "current bytes for staging package ";
            return staging.Locator.StartsWith(packagePrefix, StringComparison.Ordinal)
                ? staging.Locator.Substring(packagePrefix.Length)
                : staging.Locator.Split(' ')[0];
        }
        private static PhysicalWitnessMechanismCoverage Coverage(string name, int count, string status,
            string proven, string unknown, int certifiedEdges = 0, int affectedWorlds = 0,
            int unknownEdges = -1, int unknownAffectedWorlds = -1) =>
            new(name, status, count, proven, unknown, certifiedEdges, affectedWorlds,
                unknownEdges < 0 ? Math.Max(0, count - certifiedEdges) : unknownEdges,
                unknownAffectedWorlds < 0 ? affectedWorlds : unknownAffectedWorlds);

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
            builder.AppendLine("| Mechanism | Status | Candidate rows | Certified edges | UNKNOWN edges | Worlds with candidates | Worlds with UNKNOWN edges | What is proved / remains unknown |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
            foreach (PhysicalWitnessMechanismCoverage item in report.MechanismCoverage)
                builder.AppendLine("| " + item.Mechanism + " | " + item.Status + " | " + item.EvidenceCount + " | " + item.CertifiedEdges
                    + " | " + item.UnknownEdges + " | " + item.AffectedWorlds + " | " + item.UnknownAffectedWorlds
                    + " | " + item.ProvenFact + " " + item.RemainingUnknown + " |");
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
