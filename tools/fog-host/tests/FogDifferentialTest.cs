using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using FogHost;
using FogMod;
using YamlDotNet.Serialization;
using static FogMod.AnnotationData;
using static SoulsIds.GameSpec;

namespace FogHost.Tests
{
    /// <summary>
    /// In-process raw-seed differential between the project-owned Compact V2 graph and the exact
    /// pinned FogMod annotation oracle. This deliberately does not launch FogHost per seed.
    /// </summary>
    internal static class FogDifferentialTest
    {
        private sealed record Profile(string Name, bool Disconnected, bool RequireLords,
            bool MajorPvp = false, bool MinorPvp = false, bool Lordvessel = false,
            bool RandomStart = false, bool Glitched = false);
        private sealed record GraphInputs(string[] RandomizedPool, string[] RandomizedFromOrder,
            string[] RandomizedToOrder, string[] StaticAreaRoutes, string[] KeyItemAreas, string[] FixedNonBossGates);
        private sealed record Topology(GraphInputs Inputs, string[] Edges, string[] RandomizedPairings,
            string[] FixedRoutes, string[] BossConditionalRoutes, string[] GateState, string[] BossRules);
        private sealed record RunResult(bool Accepted, bool GraphConnected, string Stage, string Failure, Topology Topology);
        private sealed record CaseRecord(string Kind, string Profile, uint Seed, string Outcome,
            string CompactStage, string CompactFailure, string UpstreamStage, string UpstreamFailure,
            string SolvabilityParity, string TopologyParity, Topology CompactTopology, Topology UpstreamTopology);
        private sealed record CountSnapshot(int MatchAccept, int MatchReject, int CompactOnlyAccept,
            int UpstreamOnlyAccept, int ExpectedV5SafetyDivergence, int UnexplainedSolvability,
            int TopologyExact, int ExpectedV5TopologyDivergence, int AuthoredInputDivergence,
            int AuthoredInputOrderDivergence, int UnexplainedTopology);

        private const string PinnedFogModCommit = "6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = false };

        public static int RunFromEnvironment(string[] args)
        {
            string upstreamDataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            string compactDataRoot = OptionalDirectory("DS1TR_COMPACT_FOG_DATA_ROOT") ?? upstreamDataRoot;
            string pristineRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            int coreLimit = ReadLimit(args, "--core-limit", 10000);
            int extendedLimit = ReadLimit(args, "--extended-limit", 2000);
            uint[] selectedSeeds = ReadSeeds(args);
            string selectedProfile = ReadValue(args, "--profile");
            string outputPath = ReadValue(args, "--output") ?? Path.Combine(Path.GetTempPath(),
                "ds1tr-fog-differential-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".jsonl.gz");
            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            Console.WriteLine($"Fog differential: Compact V2 vs FogMod {PinnedFogModCommit}");
            Console.WriteLine($"Read-only inputs: pinned upstream={upstreamDataRoot}; compact projection={compactDataRoot}; pristine DSR={pristineRoot}");
            Console.WriteLine($"Limits: core={coreLimit}/profile, extended={extendedLimit}/profile");
            Console.WriteLine($"Compressed case record: {outputPath}");

            DataRoot.Verify(upstreamDataRoot, includeLegacyAnnotations: true);
            DataRoot.Verify(compactDataRoot, includeLegacyAnnotations: false);
            FogDsrFacts facts = FogDsrFacts.Read(compactDataRoot, pristineRoot, includeLegacyAnnotations: false);
            FogCompactCatalogV2 compactCatalog = FogCompactCatalogBuilder.BuildV2(facts);
            if (!compactCatalog.IsComplete) throw new InvalidDataException("Compact V2 catalog is incomplete; differential comparison is not trustworthy.");
            string upstreamPath = Path.Combine(upstreamDataRoot, "dist", "fog.txt");
            if (!File.Exists(upstreamPath)) throw new FileNotFoundException("Pinned FogMod annotation oracle dist/fog.txt is missing.", upstreamPath);
            Annotations upstreamTemplate;
            using (var reader = File.OpenText(upstreamPath)) upstreamTemplate = new DeserializerBuilder().Build().Deserialize<Annotations>(reader);
            if (upstreamTemplate?.Areas == null || upstreamTemplate.Entrances == null || upstreamTemplate.KeyItems == null)
                throw new InvalidDataException("Pinned FogMod dist/fog.txt did not deserialize to a complete annotation graph.");

            var profiles = new List<Profile>
            {
                new Profile("A-disconnected-off-lords-on", false, true),
                new Profile("B-disconnected-on-lords-on", true, true),
                new Profile("C-disconnected-off-lords-off", false, false),
                new Profile("D-disconnected-on-lords-off", true, false),
            };
            var extended = new List<Profile>
            {
                new Profile("E-major-pvp", false, true, MajorPvp: true),
                new Profile("F-minor-pvp", false, true, MinorPvp: true),
                new Profile("G-major-and-minor-pvp", false, true, MajorPvp: true, MinorPvp: true),
                new Profile("H-lordvessel", false, true, Lordvessel: true),
                new Profile("I-random-start", false, true, RandomStart: true),
                new Profile("J-glitched-logic", false, true, Glitched: true),
            };

            if (selectedSeeds != null)
            {
                string profileName = selectedProfile ?? profiles[0].Name;
                Profile selected = profiles.SingleOrDefault(profile => profile.Name == profileName)
                    ?? throw new ArgumentException($"Unknown core profile '{profileName}'.");
                profiles = new List<Profile> { selected };
                extended.Clear();
                coreLimit = selectedSeeds.Length;
            }
            else if (selectedProfile != null)
            {
                throw new ArgumentException("--profile requires --raw-seeds.");
            }

            using var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var gzip = new GZipStream(file, CompressionLevel.Optimal);
            using var writer = new StreamWriter(gzip, new UTF8Encoding(false));
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "header", pinnedFogModCommit = PinnedFogModCommit, compactVersion = FogCompactCatalogV2.Version,
                rawSeedStartInclusive = 0, selectedRawSeeds = selectedSeeds, selectedProfile, coreLimit, extendedLimit, upstreamDataRoot, compactDataRoot, pristineRoot,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                notes = "Each case uses the same uint32 Fog seed and HostRequest option mapping. The pristine DSR root is read-only."
            }, JsonOptions));

            var all = new Totals();
            var special = new SpecialSeeds();
            foreach (Profile profile in profiles)
                RunProfile(profile, coreLimit, "core", compactCatalog, upstreamTemplate, compactDataRoot, upstreamDataRoot, pristineRoot, writer, all, special, selectedSeeds);
            foreach (Profile profile in extended)
                RunProfile(profile, extendedLimit, "extended", compactCatalog, upstreamTemplate, compactDataRoot, upstreamDataRoot, pristineRoot, writer, all, special);

            var summary = new
            {
                kind = "summary", totalCases = all.Total,
                profiles = all.PerProfile.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value.Snapshot()),
                totals = all.Snapshot(), topologyInterpretation = "Expected V5 safety rejection requires an upstream accept and a direct Compact post-boss validator or unmet boss-completion-edge failure. Topology differences are categorized as boss-policy-only, authored graph-input/pool differences, authored input-order differences, or unexplained output divergence under identical graph inputs.",
                specialRawSeeds = special.Snapshot()
            };
            writer.WriteLine(JsonSerializer.Serialize(summary, JsonOptions));
            writer.Flush();
            Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Wrote {all.Total} differential cases to {outputPath}");
                return all.UnexplainedSolvability + all.UnexplainedTopology == 0 ? 0 : 3;
        }

        private static void RunProfile(Profile profile, int limit, string kind, FogCompactCatalogV2 compactCatalog,
            Annotations upstreamTemplate, string compactDataRoot, string upstreamDataRoot, string pristineRoot,
            StreamWriter writer, Totals all, SpecialSeeds special, IReadOnlyList<uint> selectedSeeds = null)
        {
            var counts = new Totals();
            all.PerProfile[profile.Name] = counts;
            var placementCache = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            // ItemReader is run against the verified, read-only DSR once for each annotation flavor
            // and hard-logic setting. Its graph placements are then applied identically per seed.
            foreach (bool compact in new[] { true, false })
            foreach (bool hard in new[] { false, true })
                placementCache[CacheKey(compact, hard)] = ResolveItemPlacements(profile, compact, hard,
                    compactCatalog, upstreamTemplate, compactDataRoot, upstreamDataRoot, pristineRoot);

            var timer = Stopwatch.StartNew();
            IReadOnlyList<uint> seeds = selectedSeeds ?? Enumerable.Range(0, limit).Select(index => (uint)index).ToArray();
            for (int seedIndex = 0; seedIndex < seeds.Count; seedIndex++)
            {
                uint seed = seeds[seedIndex];
                Profile seedProfile = profile;
                TextWriter originalOutput = Console.Out;
                RunResult compact;
                RunResult upstream;
                try
                {
                    Console.SetOut(TextWriter.Null);
                    compact = RunOne(seed, seedProfile, true, compactCatalog, upstreamTemplate,
                        placementCache[CacheKey(true, profile.Glitched)]);
                    upstream = RunOne(seed, seedProfile, false, compactCatalog, upstreamTemplate,
                        placementCache[CacheKey(false, profile.Glitched)]);
                }
                finally { Console.SetOut(originalOutput); }

                string outcome = !compact.Accepted && !upstream.Accepted ? "MATCH_REJECT"
                    : compact.Accepted && upstream.Accepted ? "MATCH_ACCEPT"
                    : compact.Accepted ? "COMPACT_ONLY_ACCEPT" : "UPSTREAM_ONLY_ACCEPT";
                string solvabilityParity = outcome == "UPSTREAM_ONLY_ACCEPT"
                    && (compact.Stage == "v5-post-boss-validation"
                        || (compact.Stage == "graph-connect" && compact.Failure.Contains("unmet boss-completion edges", StringComparison.Ordinal)))
                    ? "expected-v5-post-boss-safety"
                    : outcome.Contains("ONLY_ACCEPT", StringComparison.Ordinal) ? "unexplained" : "same-solvability";
                string parity = "not-applicable";
                if (compact.GraphConnected && upstream.GraphConnected)
                {
                    if (SameTopology(compact.Topology, upstream.Topology)) parity = "exact";
                    else if (SameCatalogInputs(compact.Topology.Inputs, upstream.Topology.Inputs)
                        && SameInputOrder(compact.Topology.Inputs, upstream.Topology.Inputs)
                        && Same(compact.Topology.RandomizedPairings, upstream.Topology.RandomizedPairings)
                        && Same(RemoveBossConditional(compact.Topology.Edges), RemoveBossConditional(upstream.Topology.Edges))
                        && Same(RemoveBossGateState(compact.Topology.GateState), RemoveBossGateState(upstream.Topology.GateState))) parity = "expected-v5-boss-policy-only";
                    else if (!SameCatalogInputs(compact.Topology.Inputs, upstream.Topology.Inputs)) parity = "authored-input-pool-or-area-policy";
                    else if (!SameInputOrder(compact.Topology.Inputs, upstream.Topology.Inputs)) parity = "authored-input-order";
                    else parity = "unexplained-same-input-output";
                }

                counts.Add(outcome, solvabilityParity, parity);
                all.Add(outcome, solvabilityParity, parity);
                special.Observe(profile.Name, seed, compact.Topology, upstream.Topology);
                writer.WriteLine(JsonSerializer.Serialize(new CaseRecord(kind, profile.Name, seed, outcome,
                    compact.Stage, compact.Failure, upstream.Stage, upstream.Failure, solvabilityParity, parity,
                    compact.Topology, upstream.Topology), JsonOptions));

                if ((seedIndex + 1) % 1000 == 0 || seedIndex + 1 == seeds.Count)
                {
                    writer.Flush();
                    Console.WriteLine($"{kind} {profile.Name}: {seedIndex + 1}/{seeds.Count} (raw seed {seed}); elapsed={timer.Elapsed}; matchAccept={counts.MatchAccept}, matchReject={counts.MatchReject}, compactOnly={counts.CompactOnlyAccept}, upstreamOnly={counts.UpstreamOnlyAccept}, inputDiff={counts.AuthoredInputDivergence}, inputOrderDiff={counts.AuthoredInputOrderDivergence}, topologyUnexplained={counts.UnexplainedTopology}");
                }
            }
            Console.WriteLine($"PROFILE {kind} {profile.Name}: {JsonSerializer.Serialize(counts.Snapshot())}");
        }

        private static Dictionary<string, string> ResolveItemPlacements(Profile profile, bool compact, bool hard,
            FogCompactCatalogV2 compactCatalog, Annotations upstreamTemplate, string compactDataRoot,
            string upstreamDataRoot, string pristineRoot)
        {
            Annotations annotations = MakeAnnotations(profile, compact, compactCatalog, upstreamTemplate);
            RandomizerOptions options = MakeOptions(0, profile);
            options["hard"] = hard;
            var graph = new Graph();
            graph.Construct(options, annotations);
            string originalDirectory = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = compact ? compactDataRoot : upstreamDataRoot;
                new ItemReader().FindItems(options, annotations, graph, pristineRoot, FromGame.DS1R);
            }
            finally { Environment.CurrentDirectory = originalDirectory; }
            return graph.itemAreas.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        private static RunResult RunOne(uint seed, Profile profile, bool compact, FogCompactCatalogV2 compactCatalog,
            Annotations upstreamTemplate, IReadOnlyDictionary<string, string> placements)
        {
            string stage = "construct";
            Graph graph = null;
            Annotations annotations = null;
            GraphInputs inputs = null;
            bool graphConnected = false;
            try
            {
                annotations = MakeAnnotations(profile, compact, compactCatalog, upstreamTemplate);
                RandomizerOptions options = MakeOptions(seed, profile);
                graph = new Graph();
                graph.Construct(options, annotations);
                stage = "item-placement";
                foreach (KeyValuePair<string, string> item in placements)
                {
                    if (item.Value == null || !graph.graph.TryGetValue(item.Value, out Graph.Node node))
                        throw new InvalidDataException($"Resolved key item '{item.Key}' has missing graph area '{item.Value}'.");
                    graph.itemAreas[item.Key] = item.Value;
                    node.Items.Add(item.Key);
                }
                inputs = SnapshotInputs(graph, annotations);
                stage = "graph-connect";
                new GraphConnector().Connect(options, graph, annotations);
                graphConnected = true;
                if (compact)
                {
                    stage = "v5-post-boss-validation";
                    BossProgressionValidator.ValidatePostBossRoutes(options, graph, annotations);
                }
                stage = "topology-snapshot";
                return new RunResult(true, true, "accepted", null, SnapshotTopology(graph, annotations, inputs));
            }
            catch (Exception error)
            {
                string failure = error.GetType().Name + ": " + error.Message;
                Topology partial = graph == null || annotations == null ? null
                    : SnapshotTopology(graph, annotations, inputs ?? SnapshotInputs(graph, annotations));
                return new RunResult(false, graphConnected, stage, failure, partial);
            }
        }

        private static Annotations MakeAnnotations(Profile profile, bool compact, FogCompactCatalogV2 compactCatalog,
            Annotations upstreamTemplate)
        {
            if (!compact) return CloneAnnotations(upstreamTemplate);
            Annotations annotations = compactCatalog.CreateFreshCandidateAnnotations();
            FogCompactV2PvpPolicy.ApplyGraphOptions(annotations, profile.MajorPvp, profile.MinorPvp);
            return annotations;
        }

        private static RandomizerOptions MakeOptions(uint seed, Profile profile)
        {
            var options = new RandomizerOptions { Seed = unchecked((int)seed) };
            foreach ((string _, string flag) in HostRequest.OptionFlags) options[flag] = false;
            options["world"] = true;
            options["boss"] = true;
            options["warp"] = true;
            options["scale"] = true;
            options["major"] = profile.MajorPvp;
            options["minor"] = profile.MinorPvp;
            options["lordvessel"] = profile.Lordvessel;
            options["start"] = profile.RandomStart;
            options["unconnected"] = profile.Disconnected;
            options["hard"] = profile.Glitched;
            options["lords"] = profile.RequireLords;
            options["pacifist"] = false;
            options["bboc"] = false;
            return options;
        }

        private static GraphInputs SnapshotInputs(Graph graph, Annotations annotations)
        {
            string SideIdentity(AnnotationData.Side side) => side == null ? "-" : $"{side.Area}|{side.Cond ?? "true"}|exclude={side.ExcludeIfRandomized ?? "-"}";
            string EntranceIdentity(Entrance entrance) => $"{entrance.Name ?? entrance.ID.ToString(CultureInfo.InvariantCulture)}|tags={entrance.Tags ?? ""}|A={SideIdentity(entrance.ASide)}|B={SideIdentity(entrance.BSide)}";
            string InputEdge(Graph.Edge edge) => $"{edge.Name ?? "<area>"}|{edge.From ?? "?"}>{edge.To ?? "?"}|{edge.Expr?.ToString() ?? edge.LinkedExpr?.ToString() ?? "true"}";
            string[] pool = annotations.Entrances.Concat(annotations.Warps)
                .Where(entrance => !entrance.IsFixed && !entrance.HasTag("unused"))
                .Select(EntranceIdentity).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            string[] fromOrder = graph.graph.Values.SelectMany(node => node.From.Where(edge => edge.From == null))
                .Select(InputEdge).ToArray();
            string[] toOrder = graph.graph.Values.SelectMany(node => node.To.Where(edge => edge.To == null))
                .Select(InputEdge).ToArray();
            string[] areaRoutes = graph.graph.Values.SelectMany(node => node.To.Where(edge => edge.Name == null))
                .Select(InputEdge).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            string[] itemAreas = graph.itemAreas.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value ?? "<unresolved>"}").ToArray();
            string[] fixedGates = annotations.Entrances.Concat(annotations.Warps)
                .Where(entrance => entrance.IsFixed && !entrance.HasTag("unused") && !entrance.HasTag("boss"))
                .Select(EntranceIdentity).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            return new GraphInputs(pool, fromOrder, toOrder, areaRoutes, itemAreas, fixedGates);
        }

        private static Topology SnapshotTopology(Graph graph, Annotations annotations, GraphInputs inputs)
        {
            string Describe(Graph.Edge edge) => $"{(edge.IsFixed ? "fixed" : "random")}|{edge.Name ?? "<area>"}|{edge.From ?? "?"}>{edge.To ?? "?"}|{edge.LinkedExpr?.ToString() ?? edge.Expr?.ToString() ?? "true"}";
            string[] edges = graph.graph.Values.SelectMany(node => node.To).Select(Describe).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            string[] randomized = edges.Where(value => value.StartsWith("random|", StringComparison.Ordinal)).ToArray();
            string[] fixedRoutes = edges.Where(value => value.StartsWith("fixed|", StringComparison.Ordinal)).ToArray();
            string[] bossRoutes = edges.Where(value => value.Contains("boss_defeated_", StringComparison.Ordinal)).ToArray();
            string Side(AnnotationData.Side side) => side == null ? "-" : $"{side.Area}|{side.Cond ?? "true"}|exclude={side.ExcludeIfRandomized ?? "-"}|boss={side.BossTrigger}:{side.BossTriggerArea ?? "-"}:{side.BossEncounterStartEventId}";
            string[] gateState = annotations.Entrances.Concat(annotations.Warps).Select(entrance =>
                $"{entrance.Name ?? entrance.ID.ToString(CultureInfo.InvariantCulture)}|fixed={entrance.IsFixed}|tags={entrance.Tags ?? ""}|A={Side(entrance.ASide)}|B={Side(entrance.BSide)}")
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
            string[] bossRules = (annotations.BossCompletions ?? new List<BossCompletionRule>())
                .Select(rule => $"{rule.EventId}|{rule.Name}|{rule.Area}|{rule.TriggerEntrance}|{rule.RequireFixedPostBossRoute}")
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
            return new Topology(inputs, edges, randomized, fixedRoutes, bossRoutes, gateState, bossRules);
        }

        private static bool SameTopology(Topology a, Topology b) => SameCatalogInputs(a.Inputs, b.Inputs)
            && SameInputOrder(a.Inputs, b.Inputs) && Same(a.Edges, b.Edges)
            && Same(a.GateState, b.GateState) && Same(a.BossRules, b.BossRules);

        private static bool SameCatalogInputs(GraphInputs a, GraphInputs b) => Same(a.RandomizedPool, b.RandomizedPool)
            && Same(a.StaticAreaRoutes, b.StaticAreaRoutes) && Same(a.KeyItemAreas, b.KeyItemAreas)
            && Same(a.FixedNonBossGates, b.FixedNonBossGates);

        private static bool SameInputOrder(GraphInputs a, GraphInputs b) => Same(a.RandomizedFromOrder, b.RandomizedFromOrder)
            && Same(a.RandomizedToOrder, b.RandomizedToOrder);

        private static string[] RemoveBossConditional(IEnumerable<string> values) => values
            .Where(value => !value.Contains("boss_defeated_", StringComparison.Ordinal)).ToArray();

        private static string[] RemoveBossGateState(IEnumerable<string> values) => values
            .Where(value =>
            {
                int tagsStart = value.IndexOf("|tags=", StringComparison.Ordinal);
                int sideStart = value.IndexOf("|A=", StringComparison.Ordinal);
                string tags = tagsStart < 0 ? string.Empty : value.Substring(tagsStart + 6,
                    (sideStart < 0 ? value.Length : sideStart) - tagsStart - 6);
                return !tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("boss", StringComparer.Ordinal);
            }).ToArray();

        private static bool Same(IEnumerable<string> a, IEnumerable<string> b) => a.SequenceEqual(b, StringComparer.Ordinal);

        private static string CacheKey(bool compact, bool hard) => (compact ? "compact" : "upstream") + (hard ? "-hard" : "-normal");

        private static Annotations CloneAnnotations(Annotations source)
        {
            Side CloneSide(Side side) => side == null ? null : new Side
            {
                Area = side.Area, Tags = side.Tags, Flag = side.Flag, TrapFlag = side.TrapFlag, EntryFlag = side.EntryFlag,
                BeforeWarpFlag = side.BeforeWarpFlag, BossTrigger = side.BossTrigger, BossTriggerArea = side.BossTriggerArea,
                WarpFlag = side.WarpFlag, Cutscene = side.Cutscene, Cond = side.Cond, CustomWarp = side.CustomWarp,
                CustomActionWidth = side.CustomActionWidth, Col = side.Col, ActionRegion = side.ActionRegion,
                ExcludeIfRandomized = side.ExcludeIfRandomized, DestinationMap = side.DestinationMap,
            };
            Entrance CloneEntrance(Entrance entrance) => new Entrance
            {
                Name = entrance.Name, ID = entrance.ID, Area = entrance.Area, Tags = entrance.Tags, Text = entrance.Text,
                Comment = entrance.Comment, DoorCond = entrance.DoorCond, ASide = CloneSide(entrance.ASide), BSide = CloneSide(entrance.BSide),
            };
            return new Annotations
            {
                Options = source.Options?.Select(option => new ConfigAnnotation { Opt = option.Opt, TrueOpt = option.TrueOpt }).ToList(),
                Areas = source.Areas?.Select(area => new Area { Name = area.Name, Tags = area.Tags, ScalingBase = area.ScalingBase, To = area.To?.Select(CloneSide).ToList() }).ToList(),
                KeyItems = source.KeyItems?.Select(item => new Item { Name = item.Name, ID = item.ID, Area = item.Area, Tags = item.Tags }).ToList(),
                Warps = source.Warps?.Select(CloneEntrance).ToList(), Entrances = source.Entrances?.Select(CloneEntrance).ToList(),
                CustomStarts = source.CustomStarts?.Select(start => new CustomStart { Name = start.Name, Area = start.Area, Respawn = start.Respawn }).ToList(),
                DefaultCost = source.DefaultCost == null ? null : new Dictionary<string, float>(source.DefaultCost, StringComparer.Ordinal),
                Enemies = source.Enemies?.Select(enemies => new Enemies { Col = enemies.Col, Area = enemies.Area, Includes = enemies.Includes?.ToList() }).ToList(),
                LotLocations = source.LotLocations == null ? null : new Dictionary<int, string>(source.LotLocations),
                DefaultFlagCols = source.DefaultFlagCols == null ? null : new Dictionary<string, int>(source.DefaultFlagCols, StringComparer.Ordinal),
            };
        }

        private static uint[] ReadSeeds(string[] args)
        {
            string value = ReadValue(args, "--raw-seeds");
            if (value == null) return null;
            uint[] seeds = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(token => uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out uint seed)
                    ? seed
                    : throw new ArgumentException($"--raw-seeds contains an invalid uint32 value: '{token}'."))
                .Distinct()
                .ToArray();
            if (seeds.Length == 0) throw new ArgumentException("--raw-seeds must contain at least one uint32 seed.");
            return seeds;
        }

        private static int ReadLimit(string[] args, string key, int fallback)
        {
            string value = ReadValue(args, key);
            if (value == null) return fallback;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) || result < 0)
                throw new ArgumentException($"{key} must be a non-negative integer.");
            return result;
        }

        private static string ReadValue(string[] args, string key)
        {
            string prefix = key + "=";
            string match = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal));
            return match == null ? null : match.Substring(prefix.Length);
        }

        private static string RequiredDirectory(string environmentName)
        {
            string value = Environment.GetEnvironmentVariable(environmentName);
            if (string.IsNullOrWhiteSpace(value) || !Directory.Exists(value))
                throw new DirectoryNotFoundException($"{environmentName} must point to an existing verified directory.");
            return Path.GetFullPath(value);
        }

        private static string OptionalDirectory(string environmentName)
        {
            string value = Environment.GetEnvironmentVariable(environmentName);
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!Directory.Exists(value)) throw new DirectoryNotFoundException($"{environmentName} points to a missing directory.");
            return Path.GetFullPath(value);
        }

        private static string FindRepoRoot()
        {
            for (DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
                if (Directory.Exists(Path.Combine(current.FullName, "tools", "fog-host")) && Directory.Exists(Path.Combine(current.FullName, "docs")))
                    return current.FullName;
            throw new DirectoryNotFoundException("Cannot locate the repository root from the FogHost test runner.");
        }

        private sealed class Totals
        {
            public int Total, MatchAccept, MatchReject, CompactOnlyAccept, UpstreamOnlyAccept;
            public int ExpectedV5SafetyDivergence, TopologyExact, ExpectedV5TopologyDivergence;
            public int AuthoredInputDivergence, AuthoredInputOrderDivergence, UnexplainedTopology, UnexplainedSolvability;
            public Dictionary<string, Totals> PerProfile { get; } = new Dictionary<string, Totals>(StringComparer.Ordinal);
            public void Add(string outcome, string solvabilityParity, string parity)
            {
                Total++;
                switch (outcome)
                {
                    case "MATCH_ACCEPT": MatchAccept++; break;
                    case "MATCH_REJECT": MatchReject++; break;
                    case "COMPACT_ONLY_ACCEPT": CompactOnlyAccept++; UnexplainedSolvability++; break;
                    case "UPSTREAM_ONLY_ACCEPT":
                        UpstreamOnlyAccept++;
                        if (solvabilityParity == "expected-v5-post-boss-safety") ExpectedV5SafetyDivergence++;
                        else UnexplainedSolvability++;
                        break;
                }
                if (parity == "exact") TopologyExact++;
                else if (parity == "expected-v5-boss-policy-only") ExpectedV5TopologyDivergence++;
                else if (parity == "authored-input-pool-or-area-policy") AuthoredInputDivergence++;
                else if (parity == "authored-input-order") AuthoredInputOrderDivergence++;
                else if (parity == "unexplained-same-input-output") UnexplainedTopology++;
            }
            public CountSnapshot Snapshot() => new CountSnapshot(MatchAccept, MatchReject, CompactOnlyAccept,
                UpstreamOnlyAccept, ExpectedV5SafetyDivergence, UnexplainedSolvability, TopologyExact,
                ExpectedV5TopologyDivergence, AuthoredInputDivergence, AuthoredInputOrderDivergence, UnexplainedTopology);
        }

        private sealed class SpecialSeeds
        {
            private readonly Dictionary<string, List<string>> cases = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            public void Observe(string profile, uint seed, Topology compact, Topology upstream)
            {
                if (compact == null && upstream == null) return;
                string[] edges = (compact?.Edges ?? Array.Empty<string>()).Concat(upstream?.Edges ?? Array.Empty<string>()).ToArray();
                AddIf("gaping-dragon", edges.Any(edge => edge.Contains("o1400_0000", StringComparison.Ordinal)), profile, seed, edges);
                AddIf("artorias", edges.Any(edge => edge.Contains("o2915_0000", StringComparison.Ordinal) || edge.Contains("o2916_0000", StringComparison.Ordinal)), profile, seed, edges);
                AddIf("ceaseless", edges.Any(edge => edge.Contains("o4961_0000", StringComparison.Ordinal)) || (compact?.BossRules ?? Array.Empty<string>()).Any(rule => rule.Contains("11410410", StringComparison.Ordinal)), profile, seed, edges);
                AddIf("capra-o1413", edges.Any(edge => edge.Contains("o1413_0000", StringComparison.Ordinal)), profile, seed, edges);
                AddIf("parish-pvp-off", !profile.Contains("pvp", StringComparison.Ordinal)
                    && edges.Any(edge => edge.Contains("fixed|o1408_0000|parish_andre>parish_church", StringComparison.Ordinal))
                    && edges.Any(edge => edge.Contains("fixed|o1410_0000|firelink_elevator>parish_church", StringComparison.Ordinal)), profile, seed, edges);
            }
            public object Snapshot() => cases.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
            private void AddIf(string name, bool condition, string profile, uint seed, string[] edges)
            {
                if (!condition) return;
                if (!cases.TryGetValue(name, out List<string> list)) cases[name] = list = new List<string>();
                if (list.Count >= 5) return;
                string[] relevant = edges.Where(edge => name switch
                {
                    "gaping-dragon" => edge.Contains("o1400_0000", StringComparison.Ordinal),
                    "artorias" => edge.Contains("o2915_0000", StringComparison.Ordinal) || edge.Contains("o2916_0000", StringComparison.Ordinal),
                    "ceaseless" => edge.Contains("o4961_0000", StringComparison.Ordinal),
                    "capra-o1413" => edge.Contains("o1413_0000", StringComparison.Ordinal),
                    _ => edge.Contains("o1408_0000", StringComparison.Ordinal) || edge.Contains("o1410_0000", StringComparison.Ordinal),
                }).Take(6).ToArray();
                list.Add($"{profile}:{seed} [{string.Join(";", relevant)}]");
            }
        }
    }
}
