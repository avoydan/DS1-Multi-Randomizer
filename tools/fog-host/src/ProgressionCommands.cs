using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FogMod;
using YamlDotNet.Serialization;
using static FogMod.AnnotationData;
using static FogMod.GraphChecker;

namespace FogHost
{
    /// <summary>
    /// The progression model of the item randomizer (docs/ITEM_PROGRESSION_MODEL.md): FogMod's own world graph, built with every
    /// randomization option off (the vanilla doors, gates and warps) and the rule "the Kiln needs the Lord Souls" on. There is ONE model:
    /// this one. The item engine evaluates the exported edges (a closure over areas, with a key available once the area that holds it is
    /// visited); <c>progression-check</c> asks FogMod's own <c>ItemReader</c> and <c>GraphChecker</c> the same question on a real staged
    /// game tree, which is how the engine's evaluation is cross-checked and how every generated package is validated.
    ///
    /// <c>FogHost progression-model --data-root &lt;dir&gt; --annotation-source &lt;legacy-fogmod|compact-v1&gt; --out &lt;model.json&gt;</c>
    /// <c>FogHost progression-check --data-root &lt;dir&gt; (--game &lt;staged tree&gt; | --assign &lt;key areas.json&gt;) --out &lt;result.json&gt;</c>
    ///
    /// These commands never write game data and never change what <c>generate</c> does: the generation behaviour version
    /// (<see cref="Program.HostVersion"/>) is unchanged, so every existing generation profile still matches.
    /// </summary>
    public static class ProgressionCommands
    {
        public const string ModelFormat = "ds1tr-progression-model-1";

        /// <summary>The vanilla rules: nothing randomized, the Kiln needs the four Lord Soul items. Anything else is a different world.</summary>
        private static readonly string[] VanillaFlags = { "lords" };

        /// <summary>
        /// Race Mode + (docs/RACE_MODE.md): the vanilla world plus the skips of the legacy list that FogMod's own glitched logic models. FogMod has more
        /// "hard" edges than the legacy lists skips (Gaping Dragon, Tomb of the Giants and Duke's Library drops), and those are NOT used; the skips the
        /// legacy lists that FogMod does not model (Sen's Gate, the Master Key routes, killing Gwynevere, the Duke skip) are not assumed either, so a
        /// world made with this variant never needs a skip outside the list below. Key: "from>to", value: the name of the skip.
        /// </summary>
        private static readonly Dictionary<string, string> RacePlusSkips = new Dictionary<string, string>
        {
            { "burg_upper>depths", "capra" },
            { "burg_upper>burg_lower", "lower-burg" },
            { "burg_start>burg_lower", "lower-burg" },
            { "paintedworld>paintedworld_annex", "annex-jump" },
            { "newlondo>newlondo_lowerskip", "seal-skip" },
            { "newlondo>newlondo_fourkings", "four-kings-oob" },
            { "darkroot>dlc_hallway", "purple-crystal-wrong-warp" },
            { "dlc_sanctuary>kiln", "purple-crystal-kiln" },
            { "dlc_forest>kiln", "purple-crystal-kiln" },
            { "dlc_hallway>kiln", "purple-crystal-kiln" },
            { "dlc_township>kiln", "purple-crystal-kiln" },
        };

        private static bool IsHard(Graph.Edge edge) => edge.Side != null && edge.Side.HasTag("hard");
        private static string SkipOf(Graph.Edge edge) => IsHard(edge) && RacePlusSkips.TryGetValue($"{edge.From}>{edge.To}", out string name) ? name : null;

        /// <summary>The Duke's Archives prison (docs/ITEM_PROGRESSION_MODEL.md): where the player starts after Seath's capture, with no key.</summary>
        private static readonly string[] PrisonAreas = { "dukes_prison", "dukes_extra", "dukes_giantcell" };
        private const string PrisonStart = "dukes_prison";
        private const string PrisonExit = "dukes_library2";
        private const string WorldStart = "asylum";

        public static bool Handles(string[] args) => args.Length > 0 && (args[0] == "progression-model" || args[0] == "progression-check");

        public static int Run(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string root = Option(args, "--data-root");
            string output = Option(args, "--out");
            string variant = Option(args, "--variant") ?? "vanilla";
            string annotationSource = Option(args, "--annotation-source") ?? HostRequest.LegacyFogModAnnotations;
            string pristineRoot = Option(args, "--pristine-root");
            string expectedFingerprint = Option(args, "--expected-annotation-fingerprint");
            if (variant != "vanilla" && variant != "race-plus")
            {
                Console.Error.WriteLine("--variant must be vanilla or race-plus.");
                return 2;
            }
            if (root == null || output == null)
            {
                Console.Error.WriteLine("Usage: FogHost progression-model --data-root <dir> --out <model.json>");
                Console.Error.WriteLine("       FogHost progression-check --data-root <dir> (--game <staged tree> | --assign <areas.json>) --out <result.json>");
                return 2;
            }
            string original = Environment.CurrentDirectory;
            try
            {
                root = Path.GetFullPath(root);
                output = Path.GetFullPath(output);
            if (annotationSource != HostRequest.LegacyFogModAnnotations && annotationSource != HostRequest.CompactAnnotations)
                throw new FogHostException(HostErrors.RequestInvalid, "--annotation-source must be legacy-fogmod or compact-v1.");
            if (annotationSource == HostRequest.CompactAnnotations
                && (string.IsNullOrWhiteSpace(pristineRoot) || !Regex.IsMatch(expectedFingerprint ?? string.Empty, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)))
                throw new FogHostException(HostErrors.RequestInvalid, "Compact progression models require --pristine-root and a lowercase --expected-annotation-fingerprint.");
            DataRoot.Verify(root, includeLegacyAnnotations: annotationSource == HostRequest.LegacyFogModAnnotations);
                Environment.CurrentDirectory = root;
            (Annotations ann, Graph graph, RandomizerOptions opt, string annotationHash) = Build(
                root, variant, annotationSource, pristineRoot, expectedFingerprint);
                byte[] json = args[0] == "progression-model" ? Model(ann, graph, annotationHash, variant) : Check(args, ann, graph, opt, annotationHash);
                Environment.CurrentDirectory = original;
                File.WriteAllBytes(output + ".tmp", json);
                File.Move(output + ".tmp", output, true);
                return 0;
            }
            catch (FogHostException e)
            {
                Environment.CurrentDirectory = original;
                Console.Error.WriteLine($"{e.Code}: {e.Message}");
                return 4;
            }
            catch (Exception e)
            {
                Environment.CurrentDirectory = original;
                Console.Error.WriteLine($"PROGRESSION_FAILED: {e.Message}");
                return 5;
            }
        }

        private static string Option(string[] args, string name)
        {
            for (int index = 1; index + 1 < args.Length; index += 1) if (args[index] == name) return args[index + 1];
            return null;
        }

        private static (Annotations, Graph, RandomizerOptions, string) Build(
            string root, string variant, string annotationSource, string pristineRoot, string expectedFingerprint)
        {
            var opt = new RandomizerOptions { Seed = 1 };
            foreach (string flag in VanillaFlags) opt[flag] = true;
            if (variant == "race-plus") opt["hard"] = true;
            Annotations ann;
            string annotationHash;
            if (annotationSource == HostRequest.CompactAnnotations)
            {
                FogCompactCatalogV1 compact = FogCompactCatalogBuilder.Build(
                    FogDsrFacts.Read(root, Path.GetFullPath(pristineRoot), includeLegacyAnnotations: false));
                if (!compact.IsComplete) throw new FogHostException(HostErrors.EngineFailed, "Compact annotation coverage is incomplete.");
                if (!string.Equals(compact.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
                    throw new FogHostException(HostErrors.RequestInvalid, "The Compact annotation fingerprint does not match the generation profile.");
                ann = compact.CreateFreshCandidateAnnotations();
                annotationHash = compact.Fingerprint;
            }
            else
            {
                using (StreamReader reader = File.OpenText("dist/fog.txt")) ann = new DeserializerBuilder().Build().Deserialize<Annotations>(reader);
                annotationHash = DataRoot.Hash("dist/fog.txt", true);
            }
            var graph = new Graph();
            graph.Construct(opt, ann);
            if (variant == "race-plus")
            {
                // FogMod's glitched logic is wider than the legacy skip list: keep only the skips of the list (RacePlusSkips).
                foreach (Graph.Node node in graph.graph.Values)
                {
                    node.To.RemoveAll(edge => IsHard(edge) && SkipOf(edge) == null);
                    node.From.RemoveAll(edge => IsHard(edge) && RacePlusSkips.ContainsKey($"{edge.From}>{edge.To}") == false);
                }
            }
            return (ann, graph, opt, annotationHash);
        }

        // ---- the model -------------------------------------------------------------------------------------------------------------

        private static byte[] Model(Annotations ann, Graph graph, string annotationHash, string variant)
        {
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                json.WriteStartObject();
                json.WriteString("format", ModelFormat);
                json.WriteString("fogModCommit", DataRoot.FogModCommit);
                json.WriteString("annotationSha256", annotationHash);
                json.WriteString("variant", variant);
                json.WriteStartArray("options");
                foreach (string flag in VanillaFlags) json.WriteStringValue(flag);
                if (variant == "race-plus") json.WriteStringValue("hard");
                json.WriteEndArray();
                json.WriteString("start", WorldStart);
                json.WriteStartArray("areas");
                foreach (string area in graph.graph.Keys.OrderBy(a => a, StringComparer.Ordinal)) json.WriteStringValue(area);
                json.WriteEndArray();
                json.WriteStartArray("optionalAreas");
                foreach (string area in graph.areas.Values.Where(a => a.HasTag("optional")).Select(a => a.Name).OrderBy(a => a, StringComparer.Ordinal)) json.WriteStringValue(area);
                json.WriteEndArray();
                // Every traversable edge once, with the condition FogMod's checker evaluates (LinkedExpr).
                var seen = new HashSet<string>();
                var edges = new List<(string From, string To, string Expr, string Skip)>();
                foreach (Graph.Node node in graph.graph.Values)
                {
                    foreach (Graph.Edge edge in node.To)
                    {
                        if (edge.To == null) throw new FogHostException(HostErrors.EngineFailed, $"The vanilla graph has an unconnected edge leaving {node.Area}.");
                        string expr = edge.LinkedExpr == null ? "true" : edge.LinkedExpr.ToString();
                        if (seen.Add($"{node.Area}>{edge.To}>{expr}")) edges.Add((node.Area, edge.To, expr, SkipOf(edge)));
                    }
                }
                json.WriteStartArray("edges");
                foreach ((string from, string to, string expr, string skip) in edges.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal).ThenBy(e => e.Expr, StringComparer.Ordinal))
                {
                    json.WriteStartObject();
                    json.WriteString("from", from);
                    json.WriteString("to", to);
                    if (skip != null) json.WriteString("skip", skip);
                    json.WritePropertyName("when");
                    ExprJson.Write(json, expr);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartArray("keys");
                foreach (Item key in ann.KeyItems)
                {
                    json.WriteStartObject();
                    json.WriteString("name", key.Name);
                    json.WriteString("id", key.ID);
                    json.WriteString("area", key.Area);
                    json.WriteStartArray("tags");
                    foreach (string tag in (key.Tags ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(t => t, StringComparer.Ordinal)) json.WriteStringValue(tag);
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartObject("lotAreas");
                foreach (KeyValuePair<int, string> lot in ann.LotLocations.OrderBy(l => l.Key))
                    json.WriteString(lot.Key.ToString(CultureInfo.InvariantCulture), lot.Value);
                json.WriteEndObject();
                json.WriteStartObject("prison");
                json.WriteString("start", PrisonStart);
                json.WriteString("mustReach", PrisonExit);
                json.WriteStartArray("areas");
                foreach (string area in PrisonAreas) json.WriteStringValue(area);
                json.WriteEndArray();
                json.WriteEndObject();
                json.WriteEndObject();
            }
            stream.WriteByte((byte)'\n');
            return stream.ToArray();
        }

        // ---- the check -------------------------------------------------------------------------------------------------------------

        private static byte[] CheckMany(string file, Annotations ann, Graph graph, RandomizerOptions opt, string annotationHash)
        {
            // Many assignments in one run (the differential test of the engine's evaluation): { key name: area } each; unnamed keys stay in their vanilla area.
            var assignments = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(File.ReadAllText(Path.GetFullPath(file)));
            var checker = new GraphChecker();
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteString("format", "ds1tr-progression-check-many-1");
                json.WriteString("annotationSha256", annotationHash);
                json.WriteStartArray("results");
                foreach (Dictionary<string, string> assignment in assignments)
                {
                    foreach (Graph.Node node in graph.graph.Values) node.Items.Clear();
                    foreach (Item key in ann.KeyItems)
                    {
                        string area = assignment.TryGetValue(key.Name, out string given) ? given : key.Area;
                        graph.itemAreas[key.Name] = area;
                        graph.graph[area].Items.Add(key.Name);
                    }
                    CheckRecord world = checker.Check(opt, graph, WorldStart);
                    var kept = graph.graph.ToDictionary(e => e.Key, e => e.Value.Items.ToList());
                    foreach (Graph.Node node in graph.graph.Values) if (!PrisonAreas.Contains(node.Area)) node.Items.Clear();
                    bool prisonEscape = checker.Check(opt, graph, PrisonStart).Records.ContainsKey(PrisonExit);
                    foreach (KeyValuePair<string, List<string>> entry in kept) { graph.graph[entry.Key].Items.Clear(); graph.graph[entry.Key].Items.AddRange(entry.Value); }
                    json.WriteStartObject();
                    json.WriteBoolean("worldCompletable", world.Unvisited.Count == 0);
                    json.WriteBoolean("prisonEscapable", prisonEscape);
                    json.WriteStartArray("unvisited");
                    foreach (string area in world.Unvisited.OrderBy(a => a, StringComparer.Ordinal)) json.WriteStringValue(area);
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            stream.WriteByte((byte)'\n');
            return stream.ToArray();
        }

        private static byte[] Check(string[] args, Annotations ann, Graph graph, RandomizerOptions opt, string annotationHash)
        {
            string many = Option(args, "--assign-many");
            if (many != null) return CheckMany(many, ann, graph, opt, annotationHash);
            string game = Option(args, "--game");
            string assign = Option(args, "--assign");
            if ((game == null) == (assign == null)) throw new FogHostException(HostErrors.RequestInvalid, "Give exactly one of --game and --assign.");
            string source;
            if (game != null)
            {
                // FogMod's own reader: where do the lots of the staged GameParam put every key item?
                ItemReader.Result found = new ItemReader().FindItems(opt, ann, graph, Path.GetFullPath(game), SoulsIds.GameSpec.FromGame.DS1R);
                source = "game";
                _ = found;
            }
            else
            {
                var assignment = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.GetFullPath(assign)));
                foreach (Graph.Node node in graph.graph.Values) node.Items.Clear();
                foreach (Item key in ann.KeyItems)
                {
                    string area = assignment.TryGetValue(key.Name, out string given) ? given : key.Area;
                    if (!graph.graph.ContainsKey(area)) throw new FogHostException(HostErrors.RequestInvalid, $"Unknown area {area} for {key.Name}.");
                    graph.itemAreas[key.Name] = area;
                    graph.graph[area].Items.Add(key.Name);
                }
                source = "assignment";
            }
            var checker = new GraphChecker();
            CheckRecord world = checker.Check(opt, graph, WorldStart);
            // The capture check: start in the prison with only what the prison itself holds.
            var kept = graph.graph.ToDictionary(e => e.Key, e => e.Value.Items.ToList());
            foreach (Graph.Node node in graph.graph.Values) if (!PrisonAreas.Contains(node.Area)) node.Items.Clear();
            bool prisonEscape;
            try { prisonEscape = checker.Check(opt, graph, PrisonStart).Records.ContainsKey(PrisonExit); }
            finally { foreach (KeyValuePair<string, List<string>> entry in kept) { graph.graph[entry.Key].Items.Clear(); graph.graph[entry.Key].Items.AddRange(entry.Value); } }
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteString("format", "ds1tr-progression-check-1");
                json.WriteString("annotationSha256", annotationHash);
                json.WriteString("source", source);
                json.WriteBoolean("worldCompletable", world.Unvisited.Count == 0);
                json.WriteBoolean("prisonEscapable", prisonEscape);
                json.WriteStartArray("unvisited");
                foreach (string area in world.Unvisited.OrderBy(a => a, StringComparer.Ordinal)) json.WriteStringValue(area);
                json.WriteEndArray();
                json.WriteStartArray("visited");
                foreach (string area in world.Records.Keys.OrderBy(a => a, StringComparer.Ordinal)) json.WriteStringValue(area);
                json.WriteEndArray();
                json.WriteStartObject("keyAreas");
                foreach (Item key in ann.KeyItems) json.WriteString(key.Name, graph.itemAreas[key.Name]);
                json.WriteEndObject();
                json.WriteEndObject();
            }
            stream.WriteByte((byte)'\n');
            return stream.ToArray();
        }
    }

    /// <summary>FogMod prints a condition as <c>(a AND (b OR c))</c>, <c>name</c>, <c>true</c> or <c>false</c>; this writes the same tree as JSON.</summary>
    internal static class ExprJson
    {
        public static void Write(Utf8JsonWriter json, string text)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            foreach (char c in text)
            {
                if (c == '(' || c == ')' || c == ' ')
                {
                    if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                    if (c != ' ') tokens.Add(c.ToString());
                }
                else current.Append(c);
            }
            if (current.Length > 0) tokens.Add(current.ToString());
            int position = 0;
            WriteExpr(json, tokens, ref position);
            if (position != tokens.Count) throw new FogHostException(HostErrors.EngineFailed, $"Unparsed condition: {text}");
        }

        private static void WriteExpr(Utf8JsonWriter json, List<string> tokens, ref int position)
        {
            string token = tokens[position++];
            if (token == "true") { json.WriteBooleanValue(true); return; }
            if (token == "false") { json.WriteBooleanValue(false); return; }
            if (token != "(") { json.WriteStringValue(token); return; }
            var items = new List<Action<Utf8JsonWriter>>();
            string op = null;
            while (tokens[position] != ")")
            {
                if (tokens[position] == "AND" || tokens[position] == "OR")
                {
                    string next = tokens[position++];
                    if (op != null && op != next) throw new FogHostException(HostErrors.EngineFailed, "Mixed operators in one group.");
                    op = next;
                    continue;
                }
                int start = position;
                var captured = new List<string>();
                SkipExpr(tokens, ref position);
                for (int index = start; index < position; index += 1) captured.Add(tokens[index]);
                items.Add(writer => { int local = 0; WriteExpr(writer, captured, ref local); });
            }
            position++;
            if (items.Count == 0) { json.WriteBooleanValue(op != "OR"); return; }
            if (items.Count == 1) { items[0](json); return; }
            json.WriteStartObject();
            json.WritePropertyName(op == "OR" ? "any" : "all");
            json.WriteStartArray();
            foreach (Action<Utf8JsonWriter> item in items) item(json);
            json.WriteEndArray();
            json.WriteEndObject();
        }

        private static void SkipExpr(List<string> tokens, ref int position)
        {
            if (tokens[position] != "(") { position++; return; }
            int depth = 0;
            do
            {
                if (tokens[position] == "(") depth++;
                else if (tokens[position] == ")") depth--;
                position++;
            } while (depth > 0);
        }
    }
}
