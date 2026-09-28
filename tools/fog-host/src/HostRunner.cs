using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FogMod;
using SoulsFormats;
using SoulsIds;
using YamlDotNet.Serialization;
using static FogMod.AnnotationData;
using static SoulsIds.GameSpec;

namespace FogHost
{
    public sealed record OutputFile(string Path, long Size, string Sha256);

    public sealed class HostResult
    {
        public string AnnotationSha256 { get; init; }
        public string FogOptions { get; init; }
        public bool KeyItemsRandomized { get; init; }
        public string KeyItemsHash { get; init; }
        public IReadOnlyList<OutputFile> Files { get; init; }
    }

    /// <summary>
    /// DS1R-only reimplementation of FogMod's <c>Randomizer.Randomize</c> (commit 6e6dc78), which
    /// cannot be called directly: for DS1 it returns before the writer in dry runs.
    /// The working directory must already be the verified FogMod data root.
    /// </summary>
    public sealed class HostRunner
    {
        private const string GameParam = "param/GameParam/GameParam.parambnd.dcx";
        private const string DistGameParam = "dist/DS1R/param/GameParam/GameParam.parambnd.dcx";
        private static readonly string[] RequiredParams = { "NpcParam", "ItemLotParam", "SpEffectParam", "GameAreaParam" };

        private readonly Action<string, string> progress;

        public HostRunner(Action<string, string> progress)
        {
            this.progress = progress;
        }

        public string Stage { get; private set; } = "validate-request";

        /// <summary>Checks that need no FogMod data (run before the working directory changes).</summary>
        public void ValidateLayout(HostRequest request)
        {
            Enter("validate-request", "Validating request, staging directory and FogMod data root.");
            string staging = request.StagingDirectory;
            if (!Directory.Exists(staging)) throw Invalid($"Staging directory does not exist: {staging}");
            if (File.Exists(Path.Combine(staging, "DarkSoulsRemastered.exe")))
                throw new FogHostException(HostErrors.StagingUnsafe,
                    "The staging directory contains DarkSoulsRemastered.exe. FogHost only writes to a per-attempt staging copy, never to a game installation.")
                { Stage = Stage };
            foreach ((string name, string other) in new[] { ("FogMod data root", request.FogDataRoot), ("merge base directory", request.BaseDirectory) })
            {
                if (Overlaps(staging, other))
                    throw new FogHostException(HostErrors.StagingUnsafe, $"The staging directory overlaps the {name}: {other}") { Stage = Stage };
            }
            if (Contains(staging, request.LogPath)) throw Invalid("logPath must be outside the staging directory.");

            DataRoot.Verify(request.FogDataRoot);

            var missing = new List<string>();
            IEnumerable<string> readSet = new[] { GameParam, $"msg/{request.Language}/menu.msgbnd.dcx" }
                .Concat(DataRoot.EventFiles.Select(name => $"event/{name}"))
                .Concat(DataRoot.MapFiles.Select(name => $"map/MapStudio/{name}"));
            foreach (string relative in readSet)
            {
                if (!File.Exists(Resolve(staging, relative))) missing.Add(relative);
            }
            if (!Directory.Exists(Resolve(staging, "script/talk"))) missing.Add("script/talk/");
            if (missing.Count > 0) throw Invalid($"Staging directory is missing FogMod's read set: {string.Join(", ", missing)}");

            if (request.CombinedFiles.Count > 0 && !Directory.Exists(request.BaseDirectory))
                throw Invalid($"Merge base directory does not exist: {request.BaseDirectory}");
            foreach (CombinedFile file in request.CombinedFiles)
            {
                string combined = Resolve(staging, file.Path);
                string baseFile = Resolve(request.BaseDirectory, file.Path);
                if (!File.Exists(combined) || DataRoot.Hash(combined, false) != file.CombinedSha256)
                    throw Invalid($"Staged combined file does not match combinedSha256: {file.Path}");
                if (!File.Exists(baseFile) || DataRoot.Hash(baseFile, false) != file.BaseSha256)
                    throw Invalid($"Merge base file does not match baseSha256: {file.Path}");
            }
        }

        public HostResult Run(HostRequest request)
        {
            string staging = request.StagingDirectory;

            // Inputs for the merge and the invariants, read before FogMod runs.
            Dictionary<string, PARAM.Layout> layouts = new GameEditor(FromGame.DS1R).LoadLayouts();
            ParamSnapshot vanilla = ParamSnapshot.Read(Path.GetFullPath(DistGameParam), layouts);
            ParamSnapshot input = ParamSnapshot.Read(Resolve(staging, GameParam), layouts);
            var absent = RequiredParams.Where(name => !input.Params.ContainsKey(name)).ToList();
            if (absent.Count > 0) throw Invalid($"Staged GameParam does not match FogMod's DS1R layouts: {string.Join(", ", absent)}");
            var vanillaNpcs = new HashSet<int>(vanilla.Params["NpcParam"].Rows.Select(r => (int)r.ID));
            var inputNpcs = new HashSet<int>(input.Params["NpcParam"].Rows.Select(r => (int)r.ID));
            foreach (PreservedNpcParam row in request.PreservedNpcParams)
            {
                if (vanillaNpcs.Contains(row.Id)) throw Invalid($"Preserved NpcParam {row.Id} is a vanilla row; only rows added by the combined engine can be preserved.");
                if (!vanillaNpcs.Contains(row.BaseId)) throw Invalid($"Preserved NpcParam {row.Id} has baseId {row.BaseId}, which is not a vanilla row.");
                if (!inputNpcs.Contains(row.Id)) throw Invalid($"Preserved NpcParam {row.Id} is not present in the staged GameParam.");
            }
            var preserved = new HashSet<int>(request.PreservedNpcParams.Select(r => r.Id));

            var inputMsbs = new Dictionary<string, MSB1>(StringComparer.Ordinal);
            var mergeInputs = new Dictionary<string, (EMEVD, EMEVD)>(StringComparer.Ordinal);
            var fogEvents = new HashSet<string>(DataRoot.EventFiles, StringComparer.Ordinal);
            foreach (CombinedFile file in request.CombinedFiles)
            {
                if (file.Path.StartsWith("map/MapStudio/", StringComparison.Ordinal))
                    inputMsbs[file.Path] = MSB1.Read(Resolve(staging, file.Path));
                else if (file.Path.StartsWith("event/", StringComparison.Ordinal) && fogEvents.Contains(Path.GetFileName(file.Path)))
                    mergeInputs[Path.GetFileName(file.Path)] = (EMEVD.Read(Resolve(request.BaseDirectory, file.Path)), EMEVD.Read(Resolve(staging, file.Path)));
            }

            var opt = new RandomizerOptions { Seed = unchecked((int)request.Seed) };
            foreach ((string key, string flag) in HostRequest.OptionFlags) opt[flag] = request.Options[key];
            opt.Language = request.Language;

            Enter("load-annotations", "Loading FogMod annotations.");
            Annotations ann;
            using (StreamReader reader = File.OpenText("dist/fog.txt")) ann = new DeserializerBuilder().Build().Deserialize<Annotations>(reader);
            Console.WriteLine($"Seed: {opt.DisplaySeed}. Options: {string.Join(" ", opt.GetEnabled())}");
            var graph = new Graph();
            graph.Construct(opt, ann);

            Enter("read-key-items", "Locating key items.");
            ItemReader.Result items = new ItemReader().FindItems(opt, ann, graph, staging, FromGame.DS1R);
            Console.WriteLine(items.Randomized ? $"Key item hash: {items.ItemHash}" : "No key items randomized");

            Enter("solve-graph", "Connecting fog gates.");
            new GraphConnector().Connect(opt, graph, ann);

            Enter("write-game-data", request.DryRun ? "Computing game data (dry run)." : "Writing game data to staging.");
            var merge = new EmevdMerge(mergeInputs);
            var hooks = new FogHostHooks { EventMerger = merge.Merge };
            hooks.PreservedNpcParams.UnionWith(preserved);
            new GameDataWriter { Hooks = hooks }.Write(opt, ann, graph, staging, FromGame.DS1R);
            var unmerged = mergeInputs.Keys.Where(name => !merge.Merged.Contains(name)).ToList();
            if (unmerged.Count > 0) throw Invalid($"FogMod did not process combined event files: {string.Join(", ", unmerged)}");

            var problems = new List<string>();
            var outputMsbs = hooks.Msbs.ToDictionary(p => p.Key.Replace('\\', '/'), p => p.Value, StringComparer.Ordinal);
            Invariants.CheckMsbs(inputMsbs, outputMsbs, input.Params["NpcParam"], hooks.Params["NpcParam"], preserved, problems);
            Invariants.CheckParams(input.Params, hooks.Params, preserved, problems);
            if (problems.Count > 0) throw Invariants.Violation(problems);

            var result = new HostResult
            {
                AnnotationSha256 = DataRoot.AnnotationSha256(Directory.GetCurrentDirectory()),
                FogOptions = opt.ToString(),
                KeyItemsRandomized = items.Randomized,
                KeyItemsHash = items.ItemHash ?? string.Empty,
                Files = Array.Empty<OutputFile>(),
            };
            if (request.DryRun) return result;

            var backupsBefore = new HashSet<string>(Directory.GetFiles(staging, "*.bak", SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);
            foreach ((_, Action write) in hooks.Deferred) write();
            foreach (string backup in Directory.GetFiles(staging, "*.bak", SearchOption.AllDirectories))
            {
                if (!backupsBefore.Contains(backup)) File.Delete(backup);
            }

            // Re-read what was actually written (SoulsIds swallows BND writer exceptions).
            ParamSnapshot written = ParamSnapshot.Read(Resolve(staging, GameParam), layouts);
            var writtenMsbs = inputMsbs.Keys.Where(outputMsbs.ContainsKey)
                .ToDictionary(path => path, path => MSB1.Read(Resolve(staging, path)), StringComparer.Ordinal);
            Invariants.CheckMsbs(inputMsbs, writtenMsbs, input.Params["NpcParam"], written.Params["NpcParam"], preserved, problems);
            Invariants.CheckParams(input.Params, written.Params, preserved, problems);
            Invariants.CheckRaw(input.Raw, written.Raw, problems);
            Invariants.CheckEvents(staging, merge, problems);
            if (problems.Count > 0) throw Invariants.Violation(problems);

            Enter("hash-outputs", "Hashing written files.");
            var files = hooks.Deferred.Select(d => d.Path.Replace('\\', '/')).Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(path =>
                {
                    string full = Resolve(staging, path);
                    return new OutputFile(path, new FileInfo(full).Length, DataRoot.Hash(full, false));
                })
                .ToList();
            return new HostResult
            {
                AnnotationSha256 = result.AnnotationSha256,
                FogOptions = result.FogOptions,
                KeyItemsRandomized = result.KeyItemsRandomized,
                KeyItemsHash = result.KeyItemsHash,
                Files = files,
            };
        }

        private void Enter(string stage, string message)
        {
            Stage = stage;
            progress(stage, message);
        }

        private FogHostException Invalid(string message) =>
            new FogHostException(HostErrors.RequestInvalid, message) { Stage = Stage };

        private static string Resolve(string root, string relative) =>
            Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

        private static bool Overlaps(string a, string b) => Contains(a, b) || Contains(b, a);

        private static bool Contains(string parent, string child)
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(parent), Path.GetFullPath(child));
            return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
        }
    }
}
