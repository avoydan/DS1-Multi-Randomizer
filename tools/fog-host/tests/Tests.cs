using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FogHost;
using SoulsFormats;

namespace FogHost.Tests
{
    /// <summary>
    /// FogHost self-tests. Process tests spawn the real Release FogHost.exe.
    /// Data tests need DS1TR_FOG_DATA_ROOT (a folder containing FogMod 6e6dc78 `dist`) and never
    /// touch a game installation: staging is built from the data root's own DS1R files in %TEMP%.
    /// </summary>
    public static class Tests
    {
        private static int passed, failed, skipped;
        private static readonly string Scratch = Path.Combine(Path.GetTempPath(), "foghost-tests", Guid.NewGuid().ToString("N"));
        private static string hostExe;
        private static string dataRoot;

        public static int Main()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            hostExe = Environment.GetEnvironmentVariable("FOGHOST_EXE") ?? Path.Combine(RepoRoot(), "tools", "fog-host", "bin", "Release", "net8.0", "FogHost.exe");
            dataRoot = Environment.GetEnvironmentVariable("DS1TR_FOG_DATA_ROOT");
            Directory.CreateDirectory(Scratch);
            Console.WriteLine($"FogHost: {hostExe}");
            Console.WriteLine($"Data root: {dataRoot ?? "(unset: data tests are skipped)"}");
            try
            {
                Test("usage error exits 2", UsageError);
                Test("unknown request key is FOG_REQUEST_INVALID", UnknownKey);
                Test("non-DS1R game is FOG_REQUEST_INVALID", WrongGame);
                Test("seed outside uint32 is FOG_REQUEST_INVALID", SeedRange);
                Test("missing option key is FOG_REQUEST_INVALID", MissingOption);
                Test("missing data root is FOG_DATA_MISSING", MissingDataRoot);
                Test("error classifier maps FogMod throw sites", Classifier);
                Test("merge appends custom events and event-0 inits", MergeCustom);
                Test("merge takes combined edit of an event FogMod left alone", MergeEdited);
                Test("merge conflict when both sides edit an event", MergeBothEdited);
                Test("merge conflict when combined deletes an event", MergeDeleted);
                Test("merge conflict on non-init event-0 addition", MergeEvent0NonInit);
                DataTest("altered or extended data root is FOG_DATA_MISSING", AlteredDataRoot);
                DataTest("CRLF text files in data root still verify", CrlfDataRoot);
                DataTest("staging with DarkSoulsRemastered.exe is FOG_STAGING_UNSAFE", UnsafeStaging);
                DataTest("dry run does not mutate staging", DryRunUnchanged);
                DataTest("boundary seeds solve", BoundarySeeds);
                DataTest("write is deterministic across processes, hashed, without .bak", WriteDeterministic);
                DataTest("combined NpcParam rows, MSB parts and events survive (scale on)", () => CombinedSurvives(true));
                DataTest("combined NpcParam rows, MSB parts and events survive (scale off)", () => CombinedSurvives(false));
                DataTest("preserving a vanilla NpcParam row is FOG_REQUEST_INVALID", PreservedVanilla);
                DataTest("combined hash mismatch is FOG_REQUEST_INVALID", CombinedHashMismatch);
            }
            finally
            {
                try { Directory.Delete(Scratch, true); } catch (Exception) { }
            }
            Console.WriteLine($"\n{passed} passed, {failed} failed, {skipped} skipped");
            return failed == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- harness

        private static void Test(string name, Action body)
        {
            try
            {
                body();
                passed++;
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"FAIL {name}: {e.Message}");
            }
        }

        private static void DataTest(string name, Action body)
        {
            if (string.IsNullOrEmpty(dataRoot))
            {
                skipped++;
                Console.WriteLine($"SKIP {name} (DS1TR_FOG_DATA_ROOT not set)");
                return;
            }
            Test(name, body);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "tools", "fog-host", "FogHost.csproj"))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new Exception("Cannot locate repository root.");
        }

        private sealed record HostRun(int Exit, string Stdout, byte[] ResponseBytes)
        {
            public JsonNode Response => JsonNode.Parse(ResponseBytes);
        }

        private static HostRun Run(string directory, JsonObject request, params string[] rawArgs)
        {
            Directory.CreateDirectory(directory);
            string requestPath = Path.Combine(directory, "request.json");
            string responsePath = Path.Combine(directory, "response.json");
            if (File.Exists(responsePath)) File.Delete(responsePath);
            if (request != null) File.WriteAllText(requestPath, request.ToJsonString());
            var start = new ProcessStartInfo(hostExe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string arg in rawArgs.Length > 0 ? rawArgs : new[] { "generate", "--request", requestPath, "--response", responsePath }) start.ArgumentList.Add(arg);
            using Process process = Process.Start(start);
            string stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new HostRun(process.ExitCode, stdout, File.Exists(responsePath) ? File.ReadAllBytes(responsePath) : null);
        }

        private static void ExpectError(HostRun run, string code, string contains = null)
        {
            Check(run.Exit == 0, $"exit code {run.Exit}, expected 0");
            Check(run.ResponseBytes != null, "no response file");
            JsonNode response = run.Response;
            Check(response["schemaVersion"]!.GetValue<int>() == 2, "schemaVersion");
            Check(response["ok"]!.GetValue<bool>() == false, $"expected failure, got {Encoding.UTF8.GetString(run.ResponseBytes)}");
            string actual = response["error"]!["code"]!.GetValue<string>();
            string message = response["error"]!["message"]!.GetValue<string>();
            Check(actual == code, $"code {actual}, expected {code}: {message}");
            if (contains != null) Check(message.Contains(contains, StringComparison.Ordinal), $"message lacks \"{contains}\": {message}");
        }

        private static JsonNode ExpectOk(HostRun run)
        {
            Check(run.Exit == 0, $"exit code {run.Exit}");
            Check(run.ResponseBytes != null, "no response file");
            JsonNode response = run.Response;
            Check(response["ok"]!.GetValue<bool>(), $"expected success, got {Encoding.UTF8.GetString(run.ResponseBytes)}");
            return response;
        }

        private static JsonObject Request(string directory, string root, string mode = "write", uint seed = 3141592653, bool scaling = true)
        {
            return new JsonObject
            {
                ["schemaVersion"] = 2,
                ["game"] = "DS1R",
                ["mode"] = mode,
                ["seed"] = seed,
                ["options"] = new JsonObject
                {
                    ["traversableFogs"] = true, ["bossFogs"] = true, ["areaWarps"] = true,
                    ["majorPvpFogs"] = false, ["minorPvpFogs"] = false, ["lordvesselGates"] = false,
                    ["randomStart"] = false, ["disconnectedFogs"] = false, ["noFallBedOfChaos"] = false,
                    ["scaling"] = scaling, ["glitchedLogic"] = false, ["requireLordSouls"] = true, ["pacifistMode"] = false,
                },
                ["language"] = "ENGLISH",
                ["fogDataRoot"] = root,
                ["stagingDirectory"] = Path.Combine(directory, "game"),
                ["logPath"] = Path.Combine(directory, "foghost.log"),
                ["compatibility"] = new JsonObject
                {
                    ["baseDirectory"] = Path.Combine(directory, "base"),
                    ["combinedFiles"] = new JsonArray(),
                    ["preservedNpcParams"] = new JsonArray(),
                },
            };
        }

        private static string NewDir(string name) => Path.Combine(Scratch, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        /// <summary>Stand-in "pristine" staging built from the data root's own DS1R files.</summary>
        private static string MakeStaging(string name)
        {
            string directory = NewDir(name);
            string game = Path.Combine(directory, "game");
            foreach (string sub in new[] { "event", "map", "param", "msg", "script" })
                CopyTree(Path.Combine(dataRoot, "dist", "DS1R", sub), Path.Combine(game, sub));
            Directory.CreateDirectory(Path.Combine(directory, "base"));
            return directory;
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(from)) CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        private static string TreeHash(string root)
        {
            var sb = new StringBuilder();
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                sb.Append(Path.GetRelativePath(root, file)).Append(' ').Append(Sha(file)).Append('\n');
            return sb.ToString();
        }

        private static string Sha(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

        // ---------------------------------------------------------------- process / request tests

        private static void UsageError()
        {
            HostRun run = Run(NewDir("usage"), null, "generate");
            Check(run.Exit == 2, $"exit code {run.Exit}");
        }

        private static void UnknownKey()
        {
            string dir = NewDir("unknown");
            JsonObject request = Request(dir, dir);
            request["extra"] = true;
            ExpectError(Run(dir, request), "FOG_REQUEST_INVALID", "unknown keys: extra");
        }

        private static void WrongGame()
        {
            string dir = NewDir("game");
            JsonObject request = Request(dir, dir);
            request["game"] = "DS1";
            ExpectError(Run(dir, request), "FOG_REQUEST_INVALID", "DS1R");
        }

        private static void SeedRange()
        {
            string dir = NewDir("seed");
            JsonObject request = Request(dir, dir);
            request["seed"] = 4294967296L;
            ExpectError(Run(dir, request), "FOG_REQUEST_INVALID", "uint32");
        }

        private static void MissingOption()
        {
            string dir = NewDir("option");
            JsonObject request = Request(dir, dir);
            request["options"]!.AsObject().Remove("scaling");
            ExpectError(Run(dir, request), "FOG_REQUEST_INVALID", "missing keys: scaling");
        }

        private static void MissingDataRoot()
        {
            string dir = NewDir("noroot");
            Directory.CreateDirectory(Path.Combine(dir, "game"));
            string emptyRoot = Path.Combine(dir, "fog-data");
            Directory.CreateDirectory(emptyRoot);
            ExpectError(Run(dir, Request(dir, emptyRoot)), "FOG_DATA_MISSING", DataRoot.FogModCommit);
        }

        private static void Classifier()
        {
            (string Message, string Code, string Scope)[] cases =
            {
                ("Couldn't find Lordvessel in item lots", "FOG_UPSTREAM_INCOMPATIBLE", "combined"),
                ("Item Lordvessel found in both a and b", "FOG_UPSTREAM_INCOMPATIBLE", "combined"),
                ("Couldn't solve seed 5 - try a different one", "FOG_SEED_UNSOLVABLE", "fog"),
                ("Ran out of eligible edges", "FOG_SEED_UNSOLVABLE", "fog"),
                ("Could not find edge into unreachable areas [x] starting from asylum", "FOG_SEED_UNSOLVABLE", "fog"),
                ("Bad seed: Can't find edge to self-link to reach x", "FOG_SEED_UNSOLVABLE", "fog"),
                ("Internal error: something", "FOG_ENGINE_FAILED", null),
            };
            foreach (var c in cases)
            {
                FogHostException e = HostErrors.Classify(new Exception(c.Message), "solve-graph");
                Check(e.Code == c.Code && e.RetryScope == c.Scope && e.Retryable == (c.Scope != null), $"{c.Message} -> {e.Code}/{e.RetryScope}");
            }
            Check(HostErrors.Classify(new IOException("disk"), "x").Code == "FOG_IO_FAILED", "IOException");
        }

        // ---------------------------------------------------------------- merge unit tests

        private static EMEVD.Event Evt(long id, params EMEVD.Instruction[] instructions)
        {
            var evt = new EMEVD.Event(id, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions.AddRange(instructions);
            return evt;
        }

        private static EMEVD.Instruction Ins(int bank, int id, params int[] args) =>
            new EMEVD.Instruction(bank, id, args.SelectMany(BitConverter.GetBytes).ToArray());

        private static EMEVD Evd(params EMEVD.Event[] events)
        {
            var evd = new EMEVD { Format = EMEVD.Game.DarkSouls1 };
            evd.Events.AddRange(events);
            return evd;
        }

        private static EMEVD.Event Copy(EMEVD.Event e) =>
            Evt(e.ID, e.Instructions.Select(i => new EMEVD.Instruction(i.Bank, i.ID, i.ArgData.ToArray())).ToArray());

        private static (EMEVD B, EMEVD C, EMEVD T) Trio()
        {
            EMEVD.Event e0 = Evt(0, Ins(2000, 0, 0, 11010000, 0));
            EMEVD.Event e1 = Evt(11010000, Ins(1000, 1, 1));
            EMEVD.Event e2 = Evt(11010001, Ins(1000, 2, 2));
            return (Evd(e0, e1, e2), Evd(Copy(e0), Copy(e1), Copy(e2)), Evd(Copy(e0), Copy(e1), Copy(e2)));
        }

        private static void ExpectConflict(Action action)
        {
            try { action(); }
            catch (FogHostException e) when (e.Code == "FOG_MERGE_CONFLICT") { return; }
            throw new Exception("expected FOG_MERGE_CONFLICT");
        }

        private static void MergeCustom()
        {
            var (b, c, t) = Trio();
            c.Events.Add(Evt(11019900, Ins(1000, 3, 3)));
            c.Events[0].Instructions.Add(Ins(2000, 0, 0, 11019900, 0));
            t.Events[0].Instructions.Add(Ins(2000, 0, 0, 5000, 0)); // FogMod's own init
            var merge = new EmevdMerge(new() { ["m.emevd.dcx"] = (b, c) });
            merge.Merge("m.emevd.dcx", t);
            Check(t.Events.Any(e => e.ID == 11019900), "custom event not appended");
            Check(t.Events[0].Instructions.Count == 3, $"event 0 has {t.Events[0].Instructions.Count} instructions");
            Check(merge.ExpectedEvents["m.emevd.dcx"].Count == 1 && merge.ExpectedInits["m.emevd.dcx"].Count == 1, "expectations");
        }

        private static void MergeEdited()
        {
            var (b, c, t) = Trio();
            c.Events[1].Instructions.Add(Ins(1000, 9, 9));
            new EmevdMerge(new() { ["m"] = (b, c) }).Merge("m", t);
            Check(EmevdMerge.SameEvent(t.Events[1], c.Events[1]), "combined edit not taken");
        }

        private static void MergeBothEdited()
        {
            var (b, c, t) = Trio();
            c.Events[1].Instructions.Add(Ins(1000, 9, 9));
            t.Events[1].Instructions.Add(Ins(1000, 8, 8));
            ExpectConflict(() => new EmevdMerge(new() { ["m"] = (b, c) }).Merge("m", t));
        }

        private static void MergeDeleted()
        {
            var (b, c, t) = Trio();
            c.Events.RemoveAt(2);
            ExpectConflict(() => new EmevdMerge(new() { ["m"] = (b, c) }).Merge("m", t));
        }

        private static void MergeEvent0NonInit()
        {
            var (b, c, t) = Trio();
            c.Events[0].Instructions.Add(Ins(2003, 66, 1));
            ExpectConflict(() => new EmevdMerge(new() { ["m"] = (b, c) }).Merge("m", t));
        }

        // ---------------------------------------------------------------- data tests

        private static void AlteredDataRoot()
        {
            string dir = MakeStaging("altered");
            string root = Path.Combine(dir, "fog-data");
            CopyTree(Path.Combine(dataRoot, "dist"), Path.Combine(root, "dist"));
            File.AppendAllText(Path.Combine(root, "dist", "fog.txt"), "\n# edited\n");
            File.WriteAllText(Path.Combine(root, "dist", "DS1R", "event", "extra.emevd.dcx"), "x");
            File.Delete(Path.Combine(root, "dist", "DS1R", "script", "talk", Directory.GetFiles(Path.Combine(root, "dist", "DS1R", "script", "talk")).Select(Path.GetFileName).First()));
            HostRun run = Run(dir, Request(dir, root));
            ExpectError(run, "FOG_DATA_MISSING", "altered dist/fog.txt");
            string message = run.Response["error"]!["message"]!.GetValue<string>();
            Check(message.Contains("unexpected dist/DS1R/event/extra.emevd.dcx") && message.Contains("missing dist/DS1R/script/talk/"), message);
        }

        private static void CrlfDataRoot()
        {
            string dir = MakeStaging("crlf");
            string root = Path.Combine(dir, "fog-data");
            CopyTree(Path.Combine(dataRoot, "dist"), Path.Combine(root, "dist"));
            foreach (string file in new[] { Path.Combine(root, "dist", "fog.txt") }.Concat(Directory.GetFiles(Path.Combine(root, "dist", "DS1R", "Layouts"))))
            {
                // Byte-level LF -> CRLF, as core.autocrlf would do (keeps any BOM).
                var bytes = new List<byte>();
                foreach (byte b in File.ReadAllBytes(file))
                {
                    if (b == (byte)'\n' && (bytes.Count == 0 || bytes[^1] != (byte)'\r')) bytes.Add((byte)'\r');
                    bytes.Add(b);
                }
                File.WriteAllBytes(file, bytes.ToArray());
            }
            ExpectOk(Run(dir, Request(dir, root, "dryRun")));
        }

        private static void UnsafeStaging()
        {
            string dir = MakeStaging("unsafe");
            File.WriteAllText(Path.Combine(dir, "game", "DarkSoulsRemastered.exe"), "not a game");
            ExpectError(Run(dir, Request(dir, dataRoot)), "FOG_STAGING_UNSAFE");
        }

        private static void DryRunUnchanged()
        {
            string dir = MakeStaging("dry");
            string before = TreeHash(Path.Combine(dir, "game"));
            JsonNode response = ExpectOk(Run(dir, Request(dir, dataRoot, "dryRun")));
            Check(response["files"]!.AsArray().Count == 0, "dry run listed files");
            Check(response["mode"]!.GetValue<string>() == "dryRun", "mode");
            Check(TreeHash(Path.Combine(dir, "game")) == before, "staging changed during dry run");
        }

        private static void BoundarySeeds()
        {
            string dir = MakeStaging("seeds");
            foreach (uint seed in new uint[] { 0, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF })
            {
                JsonNode response = ExpectOk(Run(Path.Combine(dir, seed.ToString()), RequestIn(dir, seed)));
                Check(response["seed"]!.GetValue<uint>() == seed, $"seed echo {seed}");
            }
        }

        private static JsonObject RequestIn(string staging, uint seed)
        {
            JsonObject request = Request(staging, dataRoot, "dryRun", seed);
            request["logPath"] = Path.Combine(staging, $"foghost-{seed}.log");
            return request;
        }

        private static void WriteDeterministic()
        {
            var runs = new List<(string Dir, HostRun Run)>();
            for (int i = 0; i < 2; i++)
            {
                string dir = MakeStaging("write" + i);
                runs.Add((dir, Run(dir, Request(dir, dataRoot))));
            }
            foreach ((string dir, HostRun run) in runs)
            {
                JsonNode response = ExpectOk(run);
                JsonArray files = response["files"]!.AsArray();
                // 18 event + 17 MSB (FogMod reads but never writes the PTDE-era m12_00_00_00) + GameParam + msg + 17 talk.
                Check(files.Count == 54, $"expected 54 written files, got {files.Count}");
                string game = Path.Combine(dir, "game");
                Check(Directory.GetFiles(game, "*.bak", SearchOption.AllDirectories).Length == 0, ".bak left in staging");
                var paths = files.Select(f => f!["path"]!.GetValue<string>()).ToList();
                Check(paths.SequenceEqual(paths.OrderBy(p => p, StringComparer.Ordinal)), "files not ordinally sorted");
                foreach (JsonNode file in files)
                {
                    string full = Path.Combine(game, file!["path"]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar));
                    Check(Sha(full) == file["sha256"]!.GetValue<string>() && new FileInfo(full).Length == file["size"]!.GetValue<long>(), $"hash mismatch {file["path"]}");
                }
                Check(!Encoding.UTF8.GetString(run.ResponseBytes).Contains(dir.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase), "response contains an absolute path");
            }
            Check(runs[0].Run.ResponseBytes.AsSpan().SequenceEqual(runs[1].Run.ResponseBytes), "responses differ between processes");
            Check(TreeHash(Path.Combine(runs[0].Dir, "game")) == TreeHash(Path.Combine(runs[1].Dir, "game")), "staging trees differ between processes");
        }

        // ---------------------------------------------------------------- combined fixture

        private const int ScaledId = 9_100_000;
        private const long CustomEvent = 11019900;
        private const string MsbPath = "map/MapStudio/m10_01_00_00.msb";
        private const string EventPath = "event/m10_01_00_00.emevd.dcx";
        private const string ParamPath = "param/GameParam/GameParam.parambnd.dcx";

        private sealed record Fixture(string Dir, JsonObject Request, string EnemyName, int BaseId);

        /// <summary>
        /// Builds staging that looks like combined output: a scaled NpcParam row, an MSB enemy pointing
        /// at it, and a custom event registered in event 0. The base directory keeps the originals.
        /// </summary>
        private static Fixture CombinedFixture(string name, bool scaling, bool preserveVanilla = false)
        {
            string dir = MakeStaging(name);
            string game = Path.Combine(dir, "game");
            string baseDir = Path.Combine(dir, "base");
            foreach (string path in new[] { MsbPath, EventPath, ParamPath })
            {
                string target = Path.Combine(baseDir, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(game, path.Replace('/', Path.DirectorySeparatorChar)), target);
            }

            string msbFile = Path.Combine(game, MsbPath.Replace('/', Path.DirectorySeparatorChar));
            MSB1 msb = MSB1.Read(msbFile);
            MSB1.Part.Enemy enemy = msb.Parts.Enemies.First(e => e.NPCParamID > 1000 && e.CollisionName != null && e.ModelName != "c1000");
            int baseId = enemy.NPCParamID;
            enemy.NPCParamID = ScaledId;
            msb.Write(msbFile);

            string paramFile = Path.Combine(game, ParamPath.Replace('/', Path.DirectorySeparatorChar));
            BND3 bnd = BND3.Read(paramFile);
            BinderFile npcFile = bnd.Files.First(f => f.Name.EndsWith("NpcParam.param", StringComparison.Ordinal));
            PARAM npc = PARAM.Read(npcFile.Bytes);
            PARAM.Layout layout = PARAM.Layout.ReadXMLFile(Path.Combine(dataRoot, "dist", "DS1R", "Layouts", "NPC_PARAM_ST.xml"));
            npc.ApplyParamdef(layout.ToParamdef(npc.ParamType, out _));
            PARAM.Row source = npc.Rows.First(r => r.ID == baseId);
            var scaled = new PARAM.Row(ScaledId, "DSR Randomizer test", npc.AppliedParamdef);
            for (int i = 0; i < scaled.Cells.Count; i++) scaled.Cells[i].Value = source.Cells[i].Value;
            scaled["hp"].Value = (uint)12345;
            npc.Rows.Add(scaled);
            npcFile.Bytes = npc.Write();
            bnd.Write(paramFile);

            string eventFile = Path.Combine(game, EventPath.Replace('/', Path.DirectorySeparatorChar));
            EMEVD evd = EMEVD.Read(eventFile);
            evd.Events.Add(Evt(CustomEvent, Ins(1000, 3, 3)));
            evd.Events.First(e => e.ID == 0).Instructions.Add(Ins(2000, 0, 0, (int)CustomEvent, 0));
            evd.Write(eventFile);

            JsonObject request = Request(dir, dataRoot, scaling: scaling);
            var files = new JsonArray();
            foreach (string path in new[] { MsbPath, EventPath, ParamPath }.OrderBy(p => p, StringComparer.Ordinal))
            {
                files.Add(new JsonObject
                {
                    ["path"] = path,
                    ["baseSha256"] = Sha(Path.Combine(baseDir, path.Replace('/', Path.DirectorySeparatorChar))),
                    ["combinedSha256"] = Sha(Path.Combine(game, path.Replace('/', Path.DirectorySeparatorChar))),
                });
            }
            request["compatibility"]!["combinedFiles"] = files;
            request["compatibility"]!["preservedNpcParams"] = new JsonArray(new JsonObject
            {
                ["id"] = preserveVanilla ? baseId : ScaledId,
                ["baseId"] = baseId,
            });
            return new Fixture(dir, request, enemy.Name, baseId);
        }

        private static void CombinedSurvives(bool scaling)
        {
            Fixture fixture = CombinedFixture("combined", scaling);
            string game = Path.Combine(fixture.Dir, "game");
            string paramFile = Path.Combine(game, ParamPath.Replace('/', Path.DirectorySeparatorChar));
            PARAM.Row expected = NpcRow(paramFile, ScaledId);

            ExpectOk(Run(fixture.Dir, fixture.Request));

            PARAM.Row written = NpcRow(paramFile, ScaledId);
            Check(written != null, "scaled NpcParam row was deleted");
            Check(Invariants.SameRow(expected, written), "scaled NpcParam row was changed");
            MSB1 msb = MSB1.Read(Path.Combine(game, MsbPath.Replace('/', Path.DirectorySeparatorChar)));
            Check(msb.Parts.Enemies.Single(e => e.Name == fixture.EnemyName).NPCParamID == ScaledId, "MSB enemy lost its scaled row");
            EMEVD evd = EMEVD.Read(Path.Combine(game, EventPath.Replace('/', Path.DirectorySeparatorChar)));
            Check(evd.Events.Count(e => e.ID == CustomEvent) == 1, "custom event missing");
            Check(evd.Events.First(e => e.ID == 0).Instructions.Any(i => i.Bank == 2000 && i.ID == 0 && BitConverter.ToInt32(i.ArgData, 4) == CustomEvent), "event-0 registration missing");
            Check(Directory.GetFiles(game, "*.bak", SearchOption.AllDirectories).Length == 0, ".bak left in staging");
        }

        private static PARAM.Row NpcRow(string paramFile, int id)
        {
            BND3 bnd = BND3.Read(paramFile);
            PARAM npc = PARAM.Read(bnd.Files.First(f => f.Name.EndsWith("NpcParam.param", StringComparison.Ordinal)).Bytes);
            PARAM.Layout layout = PARAM.Layout.ReadXMLFile(Path.Combine(dataRoot, "dist", "DS1R", "Layouts", "NPC_PARAM_ST.xml"));
            npc.ApplyParamdef(layout.ToParamdef(npc.ParamType, out _));
            return npc.Rows.FirstOrDefault(r => r.ID == id);
        }

        private static void PreservedVanilla()
        {
            Fixture fixture = CombinedFixture("vanilla", true, preserveVanilla: true);
            ExpectError(Run(fixture.Dir, fixture.Request), "FOG_REQUEST_INVALID", "is a vanilla row");
        }

        private static void CombinedHashMismatch()
        {
            Fixture fixture = CombinedFixture("mismatch", true);
            fixture.Request["compatibility"]!["combinedFiles"]![0]!["combinedSha256"] = new string('0', 64);
            ExpectError(Run(fixture.Dir, fixture.Request), "FOG_REQUEST_INVALID", "combinedSha256");
        }
    }
}
