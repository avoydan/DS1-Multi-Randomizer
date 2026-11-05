using System;
using System.Collections.Generic;
using System.IO;
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
    /// Generation profile ds1tr-gen-v7: the ORIGINAL FogMod annotation file is the only source of the physical Fog/world model. These tests do not compare the host with itself:
    /// they derive the expected topology independently from the rows of <c>dist/fog.txt</c> and check the graph FogMod builds from it, then replay the four historical
    /// failure classes (docs/V7_UPSTREAM_PARITY.md). They need DS1TR_FOG_DATA_ROOT (the verified FogMod data root) and, for the item-reading parts, DS1TR_PRISTINE_DSR_ROOT.
    /// </summary>
    internal static class UpstreamV7Test
    {
        private static readonly string[] TripleFlags = { "world", "boss", "warp", "lords", "scale" };

        private static RandomizerOptions TripleOptions(uint seed, bool glitched = false)
        {
            var options = new RandomizerOptions { Seed = unchecked((int)seed) };
            foreach (string flag in TripleFlags) options[flag] = true;
            options["hard"] = glitched;
            return options;
        }

        private static Annotations LoadUpstream(string dataRoot)
        {
            using StreamReader reader = File.OpenText(Path.Combine(dataRoot, "dist", "fog.txt"));
            return new DeserializerBuilder().Build().Deserialize<Annotations>(reader);
        }

        private static string RequiredDirectory(string variable)
        {
            string value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value) || !Directory.Exists(value)) throw new InvalidOperationException($"{variable} must name an existing directory.");
            return Path.GetFullPath(value);
        }

        private static void InDirectory(string directory, Action body)
        {
            string original = Environment.CurrentDirectory;
            try { Environment.CurrentDirectory = directory; body(); }
            finally { Environment.CurrentDirectory = original; }
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        // ---- request contract ------------------------------------------------------------------------------------------------------------------------

        public static void VerifySourceContract()
        {
            Require(HostRequest.IsUpstream(HostRequest.UpstreamV7Annotations), "upstream-v7 must be recognized as the upstream source.");
            Require(!HostRequest.IsUpstream(HostRequest.LegacyFogModAnnotations) && !HostRequest.IsUpstream(HostRequest.CompactV3Annotations), "No other source may be treated as upstream-v7.");
            Require(HostRequest.UsesFogModAnnotationFile(HostRequest.UpstreamV7Annotations) && HostRequest.UsesFogModAnnotationFile(HostRequest.LegacyFogModAnnotations), "Both fog.txt sources need the verified data set.");
            Require(!HostRequest.UsesFogModAnnotationFile(HostRequest.CompactV3Annotations), "Compact sources never read fog.txt.");
            Require(!HostRequest.IsCompactV2Family(HostRequest.UpstreamV7Annotations), "upstream-v7 must not inherit any Compact v2 behavior.");
        }

        // ---- semantic parity with fog.txt ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Expected topology computed only from the rows of fog.txt, compared with the graph FogMod builds: every in-map link (minus the documented filters: temp, and hard unless
        /// glitched logic is on) is present in the graph, no in-map edge exists that fog.txt does not state, every non-unused Entrance has edges on exactly its sides' Areas, every
        /// non-unused Warp exits on its A side and enters on its B side, and unique warps are left unpaired exactly as upstream leaves them.
        /// </summary>
        public static void VerifyGraphIsExactlyTheUpstreamAnnotations()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            InDirectory(dataRoot, () =>
            {
                foreach (bool glitched in new[] { false, true })
                {
                    Annotations ann = LoadUpstream(dataRoot);
                    RandomizerOptions options = TripleOptions(7, glitched);
                    var graph = new Graph();
                    graph.Construct(options, ann);

                    // In-map links.
                    var forward = new HashSet<string>(StringComparer.Ordinal); var allowed = new HashSet<string>(StringComparer.Ordinal);
                    foreach (Area area in ann.Areas)
                        foreach (Side side in area.To ?? new List<Side>())
                        {
                            if (side.HasTag("temp")) continue;
                            if (side.HasTag("hard") && !glitched) continue;
                            forward.Add($"{area.Name}>{side.Area}"); allowed.Add($"{area.Name}>{side.Area}");
                            if (side.HasTag("shortcut")) allowed.Add($"{side.Area}>{area.Name}");
                        }
                    var actual = new HashSet<string>(graph.graph.Values.SelectMany(node => node.To).Where(edge => edge.Name == null && edge.Link != null).Select(edge => $"{edge.From}>{edge.To}"), StringComparer.Ordinal);
                    string[] missing = forward.Where(link => !actual.Contains(link)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    string[] invented = actual.Where(link => !allowed.Contains(link)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    Require(missing.Length == 0, $"glitched={glitched}: fog.txt links absent from the graph: {string.Join(", ", missing)}");
                    Require(invented.Length == 0, $"glitched={glitched}: in-map edges the graph has but fog.txt does not state: {string.Join(", ", invented)}");

                    // Entrances: edges named after the Entrance sit on exactly its sides' Areas.
                    // Upstream keeps one door per unordered Area pair (a second door between the same two Areas adds nothing to the graph).
                    var doorPairs = new HashSet<string>(StringComparer.Ordinal);
                    foreach (Entrance entrance in ann.Entrances.Where(e => !e.HasTag("unused")))
                    {
                        if (entrance.HasTag("door") && entrance.Sides().Count == 2
                            && !doorPairs.Add(string.CompareOrdinal(entrance.ASide.Area, entrance.BSide.Area) < 0 ? entrance.ASide.Area + "|" + entrance.BSide.Area : entrance.BSide.Area + "|" + entrance.ASide.Area)) continue;
                        var expected = new SortedSet<string>(entrance.Sides().Select(s => s.Area), StringComparer.Ordinal);
                        var found = new SortedSet<string>(graph.graph.Values.SelectMany(node => node.To.Concat(node.From)).Where(edge => edge.Name == entrance.EdgeName).Select(edge => edge.Side?.Area ?? edge.From ?? edge.To), StringComparer.Ordinal);
                        Require(found.Count > 0, $"Entrance {entrance.EdgeName} has no edge in the graph.");
                        Require(found.IsSubsetOf(expected), $"Entrance {entrance.EdgeName}: graph Areas [{string.Join(",", found)}] are not among its fog.txt sides [{string.Join(",", expected)}].");
                    }

                    // Warps: exit on A, entrance on B; unique warps are not paired (the connector joins them in its UNPAIRED silo).
                    foreach (Entrance warp in ann.Warps.Where(w => !w.HasTag("unused") && !w.ASide.HasTag("temp")))
                    {
                        Graph.Edge exit = graph.graph[warp.ASide.Area].To.FirstOrDefault(edge => edge.Name == warp.EdgeName && edge.Side == warp.ASide) ?? graph.graph[warp.ASide.Area].From.FirstOrDefault(edge => edge.Name == warp.EdgeName && edge.Side == warp.ASide);
                        Graph.Edge entry = graph.graph[warp.BSide.Area].To.FirstOrDefault(edge => edge.Name == warp.EdgeName && edge.Side == warp.BSide) ?? graph.graph[warp.BSide.Area].From.FirstOrDefault(edge => edge.Name == warp.EdgeName && edge.Side == warp.BSide);
                        Require(exit != null && entry != null, $"Warp {warp.EdgeName} must have its exit on {warp.ASide.Area} and its entrance on {warp.BSide.Area}.");
                        if (warp.HasTag("unique") && !warp.IsFixed) Require(exit.Pair == null && entry.Pair == null, $"Unique warp {warp.EdgeName} must stay unpaired before the connector runs.");
                    }
                }
            });
        }

        /// <summary>The identity of the annotation the host reads: the exact bytes (line endings normalized) of the pinned fog.txt, not a rebuilt model.</summary>
        public static void VerifyAnnotationFileIsTheVerifiedUpstreamFile()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            DataRoot.Verify(dataRoot, includeLegacyAnnotations: true);
            string hash = DataRoot.AnnotationSha256(dataRoot);
            Require(hash == DataRoot.Manifest.Single(entry => entry.Path == "dist/fog.txt").Sha256, "The fog.txt hash must be the manifest value.");
            InDirectory(dataRoot, () =>
            {
                Annotations ann = LoadUpstream(dataRoot);
                Require(ann.BossCompletions.Count == 0 && ann.CompactLotLocations == null, "Upstream annotations carry no FogHost-only project data (no boss-completion rules, no exact Compact lot ownership).");
                Require(ann.Areas.Count == 131 && ann.Entrances.Count == 109 && ann.Warps.Count == 15 && ann.KeyItems.Count == 27, $"Unexpected fog.txt shape: {ann.Areas.Count} Areas, {ann.Entrances.Count} Entrances, {ann.Warps.Count} Warps, {ann.KeyItems.Count} key items.");
            });
        }

        // ---- the V7 data root: bundled annotation + locally rebuilt inputs ---------------------------------------------------------------------------

        /// <summary>
        /// The V7 data root is the bundled upstream fog.txt plus the 58 inputs rebuilt on the player's machine. It verifies in V7 mode and only in V7 mode; its fog.txt is byte-identical
        /// to the bundled file and to the legacy cache's file; the projection of the upstream item model has every KeyItem (25 required, 2 random-only/hard) and every LotLocations entry.
        /// </summary>
        public static void VerifyV7DataRootAndItemProjection()
        {
            string v7Root = RequiredDirectory("DS1TR_V7_DATA_ROOT");
            string legacyRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            DataRoot.VerifyForSource(v7Root, HostRequest.UpstreamV7Annotations);
            bool legacyModeRejected = false;
            try { DataRoot.VerifyForSource(v7Root, HostRequest.LegacyFogModAnnotations); } catch (FogHostException) { legacyModeRejected = true; }
            Require(legacyModeRejected, "The locally rebuilt inputs must not pass as the legacy upstream files (the modes are separate).");
            Require(File.ReadAllBytes(Path.Combine(v7Root, "dist", "fog.txt")).SequenceEqual(File.ReadAllBytes(Path.Combine(legacyRoot, "dist", "fog.txt"))), "The V7 fog.txt must be byte-identical to the upstream file of the legacy cache.");
            string bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "upstream", "fog.txt"));
            if (File.Exists(bundled)) Require(File.ReadAllBytes(bundled).SequenceEqual(File.ReadAllBytes(Path.Combine(v7Root, "dist", "fog.txt"))), "The bundled fog.txt must be what the V7 data root holds.");

            using JsonDocument projection = JsonDocument.Parse(UpstreamProjectionCommands.Build(v7Root));
            JsonElement root = projection.RootElement;
            Require(root.GetProperty("format").GetString() == UpstreamProjectionCommands.ProjectionFormat, "projection format");
            Require(root.GetProperty("annotationSha256").GetString() == DataRoot.Manifest.Single(e => e.Path == "dist/fog.txt").Sha256, "The projection names the pinned fog.txt.");
            JsonElement[] items = root.GetProperty("items").EnumerateArray().ToArray();
            Require(items.Length == 27 && items.Count(i => i.GetProperty("required").GetBoolean()) == 25, $"Expected 27 upstream key items (25 required), found {items.Length}.");
            Require(root.GetProperty("lotStarts").GetArrayLength() == 440, "Expected the 440 upstream LotLocations entries.");
            Require(items.Any(i => i.GetProperty("name").GetString() == "lordvessel" && i.GetProperty("equipType").GetInt32() == 3 && i.GetProperty("itemId").GetInt32() == 2510), "The Lordvessel is an upstream key item.");
            Require(items.Any(i => i.GetProperty("name").GetString() == "peculiar_doll" && i.GetProperty("required").GetBoolean()), "The Peculiar Doll is a required upstream key item (the Compact required-9 set left it out).");
        }

        /// <summary>
        /// The locally rebuilt inputs of V7 (<c>reconstruct-compact-data --upstream-layout</c>) against FogMod's own dist files, measured on the real data: every event archive has the same
        /// events in the same order with the same instructions and the same parameter order, and every talk archive is byte-identical after the DCX layer (the compression encoder is the one
        /// thing that differs; FogMod copies the archive through unchanged). FogMod writes whatever it reads, so this is the input side of the runtime parity.
        /// </summary>
        public static void VerifyUpstreamLayoutRebuildMatchesFogMod()
        {
            string upstreamRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            string pristineRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            string output = Path.Combine(Path.GetTempPath(), "ds1tr-v7-layout-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            try
            {
                Require(CompactWorldDataCommands.Run(new[] { "reconstruct-compact-data", "--pristine-root", pristineRoot, "--output-root", output, "--upstream-layout" }) == 0, "The upstream-layout rebuild must succeed.");
                string up = Path.Combine(upstreamRoot, "dist", "DS1R"); string re = Path.Combine(output, "dist", "DS1R");
                SoulsFormats.EMEVD Load(string path) { byte[] bytes = File.ReadAllBytes(path); if (SoulsFormats.DCX.Is(bytes)) bytes = SoulsFormats.DCX.Decompress(bytes); return SoulsFormats.EMEVD.Read(bytes); }
                string Body(SoulsFormats.EMEVD.Event evt) => evt.RestBehavior + "|" + string.Join(";", evt.Instructions.Select(i => i.Bank + ":" + i.ID + ":" + Convert.ToHexString(i.ArgData)))
                    + "|" + string.Join(";", evt.Parameters.Select(p => p.InstructionIndex + ":" + p.TargetStartByte + ":" + p.SourceStartByte + ":" + p.ByteCount));
                int archives = 0;
                foreach (string file in Directory.GetFiles(Path.Combine(up, "event"), "*.emevd.dcx").OrderBy(f => f, StringComparer.Ordinal))
                {
                    var expected = Load(file); var actual = Load(Path.Combine(re, "event", Path.GetFileName(file)));
                    Require(expected.Events.Select(e => e.ID).SequenceEqual(actual.Events.Select(e => e.ID)), $"{Path.GetFileName(file)}: the events must be in FogMod's order.");
                    Require(expected.Events.Select(Body).SequenceEqual(actual.Events.Select(Body)), $"{Path.GetFileName(file)}: every event must have FogMod's instructions and parameter order.");
                    archives++;
                }
                Require(archives == 18, $"Expected 18 event archives, compared {archives}.");
                int scripts = 0;
                foreach (string file in Directory.GetFiles(Path.Combine(up, "script", "talk"), "*.talkesdbnd.dcx").OrderBy(f => f, StringComparer.Ordinal))
                {
                    byte[] expected = SoulsFormats.DCX.Decompress(File.ReadAllBytes(file)); byte[] actual = SoulsFormats.DCX.Decompress(File.ReadAllBytes(Path.Combine(re, "script", "talk", Path.GetFileName(file))));
                    Require(expected.SequenceEqual(actual), $"{Path.GetFileName(file)}: the decompressed talk archive must be byte-identical to FogMod's.");
                    scripts++;
                }
                Require(scripts == 17, $"Expected 17 talk archives, compared {scripts}.");
            }
            finally { try { Directory.Delete(output, true); } catch { /* best effort */ } }
        }

        // ---- the four historical failures ------------------------------------------------------------------------------------------------------------

        private static Graph ConstructGraph(string dataRoot, bool glitched = false)
        {
            Annotations ann = LoadUpstream(dataRoot);
            var graph = new Graph();
            graph.Construct(TripleOptions(7, glitched), ann);
            return graph;
        }

        /// <summary>Asylum / Ceaseless: the start hallway has no unconditional fixed exit in upstream, and the project-only unconditional hallway links do not exist.</summary>
        public static void VerifyAsylumHasNoFakeUnconditionalExit()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            InDirectory(dataRoot, () =>
            {
                Graph graph = ConstructGraph(dataRoot);
                Graph.Edge[] inMapExits = graph.graph["asylum"].To.Where(edge => edge.Name == null && edge.Link != null).ToArray();
                Require(inMapExits.All(edge => edge.LinkedExpr != null || edge.To == "asylum_stray"), "The Asylum hallway must not have an unconditional fixed exit into the world: " + string.Join(", ", inMapExits.Select(e => $"{e.From}>{e.To}")));
                Require(!graph.graph.Values.SelectMany(node => node.To).Any(edge => edge.Name == null && edge.Link != null && edge.From == "asylum" && edge.To == "firelink"), "No in-map Asylum -> Firelink link exists upstream (it was a ConnectCollision inference).");
                Require(!graph.graph.Values.SelectMany(node => node.To).Any(edge => edge.Name == null && edge.Link != null && edge.From == "firelink_crow" && edge.To == "asylum"), "No in-map Firelink crow -> Asylum link exists upstream.");
            });
        }

        /// <summary>Quelaag: the arena is entered by her own boss fog (flag/BossTrigger on the arena side); there is no in-map link from the bell room back into it.</summary>
        public static void VerifyQuelaagArenaHasNoBackLink()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            InDirectory(dataRoot, () =>
            {
                Graph graph = ConstructGraph(dataRoot);
                Require(!graph.graph.Values.SelectMany(node => node.To).Any(edge => edge.Name == null && edge.Link != null && edge.To == "blighttown_quelaag"), "No in-map link may enter Quelaag's arena (the bell-room back link was a project inference).");
                Require(!graph.graph.Values.SelectMany(node => node.To).Any(edge => edge.Name == null && edge.Link != null && edge.From == "blighttown_quelaag"), "No in-map link may leave Quelaag's arena either: its only way in or out is the fog gate.");
                Entrance gate = LoadUpstream(dataRoot).Entrances.Single(e => e.Name == "o4950_0000");
                Require(gate.ASide.Area == "blighttown_quelaag" && gate.ASide.BossTrigger != 0 && gate.BSide.Area == "blighttown", "o4950_0000: the arena side carries the boss trigger and the other side is Blighttown.");
            });
        }

        /// <summary>Gaping Dragon: o1420_0000 is the Depths wall (both sides Depths); the arena is entered by o1400_0000 only, or by the hard drop when glitched logic is on.</summary>
        public static void VerifyGapingAreaIsNotMislabelled()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            InDirectory(dataRoot, () =>
            {
                Annotations ann = LoadUpstream(dataRoot);
                Entrance wall = ann.Entrances.Single(e => e.Name == "o1420_0000");
                Require(wall.ASide.Area == "depths" && wall.BSide.Area == "depths", "o1420_0000 must be a Depths-to-Depths wall.");
                foreach (bool glitched in new[] { false, true })
                {
                    Graph graph = ConstructGraph(dataRoot, glitched);
                    string[] into = graph.graph.Values.SelectMany(node => node.To.Concat(node.From)).Where(edge => edge.To == "depths_gaping")
                        .Select(edge => edge.Name ?? "in-map").Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    string[] expected = glitched ? new[] { "in-map", "o1400_0000" } : new[] { "o1400_0000" };
                    Require(into.SequenceEqual(expected), $"glitched={glitched}: the Gaping arena must be entered by [{string.Join(",", expected)}], found [{string.Join(",", into)}].");
                }
            });
        }

        /// <summary>
        /// Priscilla: leaving the Painted World arena is the scripted unique warp 1512500 (WarpFlag 5302), not an in-map link. The arena has no in-map exit at all, and the graph never
        /// treats a ConnectCollision as a way out.
        /// </summary>
        public static void VerifyPriscillaExitIsTheUpstreamWarp()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            InDirectory(dataRoot, () =>
            {
                Annotations ann = LoadUpstream(dataRoot);
                Entrance warp = ann.Warps.Single(w => w.ID == 1512500);
                Require(warp.HasTag("unique") && warp.ASide.Area == "paintedworld_priscilla" && warp.ASide.WarpFlag == 5302 && warp.BSide.Area == "anorlondo_painting",
                    "Warp 1512500 is the Painted World exit: unique, leaving from the Priscilla arena on warp flag 5302 towards the Anor Londo painting.");
                foreach (bool glitched in new[] { false, true })
                {
                    Graph graph = ConstructGraph(dataRoot, glitched);
                    Graph.Edge[] leaving = graph.graph["paintedworld_priscilla"].To.Where(edge => edge.Name != "o1600_0000").ToArray();
                    Require(leaving.All(edge => edge.Name == "1512500"), $"glitched={glitched}: the only way out of the Priscilla arena must be warp 1512500, found [{string.Join(",", leaving.Select(e => e.Name ?? "in-map"))}].");
                    Require(!graph.graph.Values.SelectMany(node => node.To).Any(edge => edge.Name == null && edge.Link != null && edge.From == "paintedworld_priscilla"), "No in-map link leaves the Priscilla arena.");
                    Require(!graph.graph.Values.SelectMany(node => node.To).Any(edge => edge.Name == null && edge.Link != null && edge.From == "paintedworld_priscilla" && edge.To == "anorlondo_main"), "paintedworld_priscilla > anorlondo_main must not exist (the V6 ConnectCollision inference).");
                }
            });
        }

        // ---- the accepting search, witness provenance ------------------------------------------------------------------------------------------------

        private sealed record Connected(Graph Graph, Annotations Annotations, RandomizerOptions Options, GraphChecker.CheckRecord Check, WitnessObserver Observer);

        private static Connected Connect(string dataRoot, string gameRoot, uint seed)
        {
            Annotations ann = LoadUpstream(dataRoot);
            RandomizerOptions options = TripleOptions(seed);
            var graph = new Graph();
            graph.Construct(options, ann);
            new ItemReader().FindItems(options, ann, graph, gameRoot, FromGame.DS1R);
            new GraphConnector().Connect(options, graph, ann);
            var observer = new WitnessObserver();
            GraphChecker.CheckRecord check = StartReachabilityValidator.Validate(options, graph, observer);
            return new Connected(graph, ann, options, check, observer);
        }

        /// <summary>
        /// On real upstream layouts the witness is deterministic, every transition says it is upstream, no step is project-verified, and the route never leaves the Priscilla arena by a
        /// fixed route. The seeds are the historical regression seeds plus a small deterministic sample.
        /// </summary>
        public static void VerifyWitnessProvenanceAndDeterminism()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            string gameRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            InDirectory(dataRoot, () =>
            {
                int accepted = 0;
                foreach (uint seed in new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 4009337993, 4038375671, 2654435761 })
                {
                    Connected first;
                    try { first = Connect(dataRoot, gameRoot, seed); }
                    catch (Exception e) when (e.Message.StartsWith("Couldn't solve seed", StringComparison.Ordinal)) { continue; }
                    accepted++;
                    byte[] a = SolverWitness.Build(first.Options, first.Graph, first.Annotations, first.Check, first.Observer, HostRequest.UpstreamV7Annotations);
                    Connected second = Connect(dataRoot, gameRoot, seed);
                    byte[] b = SolverWitness.Build(second.Options, second.Graph, second.Annotations, second.Check, second.Observer, HostRequest.UpstreamV7Annotations);
                    Require(a.SequenceEqual(b), $"Fog seed {seed}: the V7 witness must be byte-identical on every connection.");
                    SolverWitness.WitnessDto document = SolverWitness.BuildDocument(first.Options, first.Graph, first.Annotations, first.Check, first.Observer, HostRequest.UpstreamV7Annotations);
                    Require(document.AnnotationSource == HostRequest.UpstreamV7Annotations, "The witness names its annotation source.");
                    SolverWitness.EdgeDto[] edges = document.ReachTree.Where(r => r.Edge != null).Select(r => r.Edge).Concat(document.MainRoute.Where(s => s.Edge != null).Select(s => s.Edge)).ToArray();
                    Require(edges.Length > 0 && edges.All(edge => edge.Source == SolverWitness.SourceUpstream), $"Fog seed {seed}: every transition of a V7 witness must be marked upstream.");
                    Require(document.KeyItems.All(item => item.Source == SolverWitness.SourceUpstream || item.Source == SolverWitness.SourceCombinedOverlay), "Key item sources are upstream or the combined overlay.");
                    Require(!edges.Any(edge => edge.Source == SolverWitness.SourceProjectVerified), "V7 has no project-specific verified routes.");
                    Require(!edges.Any(edge => edge.Kind == "fixed-route" && edge.Exit.Area == "paintedworld_priscilla"), $"Fog seed {seed}: no fixed route may leave the Priscilla arena.");
                    Require(document.Claim.AreasReached == document.Claim.AreasTotal - document.Claim.OptionalAreasNotRequired.Length || document.Claim.Unreached.Length == 0, "An accepted world reaches every required Area.");
                }
                Require(accepted >= 8, $"Expected most of the sample to be accepted, got {accepted}.");
            });
        }

        /// <summary>
        /// The V7 equivalent of the owner's hard-lock (V6: Asylum fog -> Priscilla boss side, then a fixed route out that killed the player): on real upstream layouts where the Asylum
        /// start exits through the boss side of Priscilla's fog, every Area the solver reaches from the arena is reached through the upstream warp 1512500 (WarpFlag 5302) and
        /// nothing else, the witness names it as upstream, and no in-map link leaves the arena.
        /// </summary>
        public static void VerifyPriscillaBossSideArrivalUsesTheUpstreamWarpOnly()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            string gameRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            InDirectory(dataRoot, () =>
            {
                int found = 0;
                foreach (uint seed in new uint[] { 140, 243, 265 })
                {
                    Connected world = Connect(dataRoot, gameRoot, seed);
                    bool fromAsylum = world.Graph.graph["paintedworld_priscilla"].From.Any(edge => edge.Name == "o1600_0000" && edge.From == "asylum");
                    Require(fromAsylum, $"Fog seed {seed} is a known layout with the Asylum hallway paired to the boss side of Priscilla's fog.");
                    found++;
                    SolverWitness.WitnessDto document = SolverWitness.BuildDocument(world.Options, world.Graph, world.Annotations, world.Check, world.Observer, HostRequest.UpstreamV7Annotations);
                    SolverWitness.ReachDto[] leaving = document.ReachTree.Where(reach => reach.FromArea == "paintedworld_priscilla").ToArray();
                    Require(leaving.All(reach => reach.Edge.Exit.Object == "1512500" && reach.Edge.Kind == "warp" && reach.Edge.Source == SolverWitness.SourceUpstream && reach.Edge.Exit.Tags.Contains("unique")),
                        $"Fog seed {seed}: the only way the solver leaves the Priscilla arena must be the upstream warp 1512500, found [{string.Join(",", leaving.Select(r => r.Edge.Exit.Object ?? "in-map"))}].");
                    foreach (Graph.Edge edge in world.Graph.graph["paintedworld_priscilla"].To.Where(e => e.Name == "1512500"))
                        Require(edge.Side.WarpFlag == 5302, "The warp keeps WarpFlag 5302.");
                    Require(!document.ReachTree.Any(reach => reach.Edge != null && reach.Edge.Kind == "fixed-route" && reach.Edge.Exit.Area == "paintedworld_priscilla"), "No fixed route leaves the arena.");
                }
                Require(found == 3, "All three known layouts were checked.");
            });
        }

        /// <summary>The transition inventory of V7 (default Triple options): everything is upstream; the overlay is the subset whose conditions name a key item the Items randomizer may move.</summary>
        public static void VerifyTransitionInventory()
        {
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            InDirectory(dataRoot, () =>
            {
                Annotations ann = LoadUpstream(dataRoot);
                RandomizerOptions options = TripleOptions(7);
                var keyItems = new HashSet<string>(ann.KeyItems.Select(i => i.Name), StringComparer.Ordinal);
                bool Overlay(params string[] conditions) => conditions.Where(c => !string.IsNullOrWhiteSpace(c)).Any(c => ParseExpr(c)?.FreeVars().Any(keyItems.Contains) == true);

                int links = 0, linkOverlay = 0, entrances = 0, entranceOverlay = 0, warps = 0, warpOverlay = 0;
                foreach (Area area in ann.Areas)
                    foreach (Side side in area.To ?? new List<Side>())
                    {
                        if (side.HasTag("temp") || (side.HasTag("hard") && !options["hard"])) continue;
                        links++; if (Overlay(side.Cond)) linkOverlay++;
                    }
                foreach (Entrance e in ann.Entrances.Where(e => !e.HasTag("unused"))) { entrances++; if (Overlay(e.DoorCond, e.ASide?.Cond, e.BSide?.Cond)) entranceOverlay++; }
                foreach (Entrance e in ann.Warps.Where(e => !e.HasTag("unused") && !e.ASide.HasTag("temp"))) { warps++; if (Overlay(e.ASide?.Cond, e.BSide?.Cond)) warpOverlay++; }
                int total = links + entrances + warps, overlay = linkOverlay + entranceOverlay + warpOverlay;
                Console.WriteLine($"      V7 transitions (default Triple options): total {total} = {links} in-map links + {entrances} entrances + {warps} warps; direct upstream {total - overlay}; upstream + combined-mode condition overlay {overlay}; project-specific verified 0; unsupported 0");
                Require(total > 0 && overlay < total, "The inventory must be non-trivial and mostly direct upstream.");
            });
        }

        /// <summary>
        /// Bounded raw-seed acceptance with FogMod's own connector (docs/V7_UPSTREAM_PARITY.md): a rejected impossible world is better than an accepted fake one, so nothing is relaxed.
        /// Controlled by DS1TR_V7_ACCEPTANCE_SEEDS (default 0 = skipped).
        /// </summary>
        public static void VerifyRawSeedAcceptance()
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("DS1TR_V7_ACCEPTANCE_SEEDS"), out int count) || count <= 0) { Console.WriteLine("      (skipped: set DS1TR_V7_ACCEPTANCE_SEEDS)"); return; }
            string dataRoot = RequiredDirectory("DS1TR_FOG_DATA_ROOT");
            string gameRoot = RequiredDirectory("DS1TR_PRISTINE_DSR_ROOT");
            InDirectory(dataRoot, () =>
            {
                int accepted = 0, exhausted = 0, other = 0; var failures = new List<string>();
                var original = Console.Out; Console.SetOut(TextWriter.Null);
                try
                {
                    for (uint seed = 1; seed <= count; seed++)
                    {
                        try { Connect(dataRoot, gameRoot, seed); accepted++; }
                        catch (Exception e) when (e.Message.StartsWith("Couldn't solve seed", StringComparison.Ordinal)) { exhausted++; failures.Add($"{seed}: {e.Message}"); }
                        catch (FogHostException e) { other++; failures.Add($"{seed}: {e.Message}"); }
                    }
                }
                finally { Console.SetOut(original); }
                Console.WriteLine($"      V7 raw-seed acceptance: {count} seeds, accepted {accepted}, connector exhausted {exhausted}, start-reachability rejected {other}; {string.Join(" | ", failures)}");
                Require(other == 0, "After FogMod's connector accepts a world, start-reachability must never disagree.");
            });
        }
    }
}
