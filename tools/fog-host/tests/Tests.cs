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
                Test("invariant: a model declared as a Player model (c0000) stays valid; losing it is a violation", ModelDeclarationInvariant);
                Test("merge appends custom events and event-0 inits", MergeCustom);
                Test("merge takes combined edit of an event FogMod left alone", MergeEdited);
                Test("merge conflict when both sides edit an event", MergeBothEdited);
                Test("merge conflict when combined deletes an event", MergeDeleted);
                Test("merge conflict on non-init event-0 addition", MergeEvent0NonInit);
                Test("11505392 pattern: unadjusted combined deletion inside SKIP IF Event Flag range is refused", Event11505392Refused);
                Test("derived skip: count reduction from a deletion inside the range merges", DerivedReductionMerges);
                Test("derived skip: count increase from an insertion inside the range merges", DerivedIncreaseMerges);
                Test("derived skip: other side editing inside the range is refused", DerivedOtherSideInsideRefused);
                Test("derived skip: incorrect count delta is refused", DerivedWrongDeltaRefused);
                Test("derived skip: changed skip opcode or condition is refused", DerivedChangedSkipRefused);
                Test("derived skip: ambiguous range mapping whose span the other side edits is refused", DerivedAmbiguousRefused);
                Test("derived skip: both sides adjusting the same skip is refused", DerivedBothSidesRefused);
                Test("real 11505392 case with the fixed combined engine merges", Event11505392DerivedMerges);
                Test("real 11600400 case with the fixed combined engine merges", Event11600400DerivedMerges);
                Test("FogMod boss-trigger rewrite inside a SKIP grows it to cover the replacements (x5393 pattern)", TriggerRewriteInsideSkip);
                Test("FogMod boss-trigger rewrite on the last covered line grows the SKIP", TriggerRewriteLastCoveredLine);
                Test("FogMod boss-trigger rewrite outside every SKIP leaves counts unchanged", TriggerRewriteOutsideSkip);
                Test("FogMod boss-trigger rewrite under nested SKIPs grows each", TriggerRewriteNestedSkips);
                Test("FogMod boss-trigger rewrite refuses parameterized, overflowing or control-flow cases", TriggerRewriteRefusals);
                Test("11505392 pattern: combined deletion outside the skip range merges with FogMod's trigger rewrite", Event11505392OutsideSkipMerges);
                Test("instruction merge: independent edits in one event succeed", IndependentEdits);
                Test("instruction merge: deletion plus unrelated edit succeeds", DeletionPlusUnrelatedEdit);
                Test("instruction merge: same-instruction edit is refused", SameInstructionRefused);
                Test("instruction merge: adjacent edits are refused", AdjacentEditsRefused);
                Test("instruction merge: skip-count change is refused", SkipCountChangeRefused);
                Test("instruction merge: inserted END/WAIT is refused", InsertedControlFlowRefused);
                Test("instruction merge: insertion inside a skip range is refused", InsertionInSkipRangeRefused);
                Test("instruction merge: insertion right after the covered lines (unchanged count) merges", SkipBoundaryMerges);
                Test("instruction merge: insertion right after the skip instruction is still refused", SkipFirstLineRefused);
                Test("ambiguous alignment with an identical result merges (boss health-bar companion pattern)", AmbiguousIdenticalResultMerges);
                Test("ambiguous alignment whose span the other side edits is refused", AmbiguousSpanTouchedRefused);
                Test("derived FogMod skip with a combined edit right after the range merges (11215003 pattern)", DerivedWithBoundaryEditMerges);
                Test("event 0: combined removal of an initialization FogMod keeps merges", Event0InitRemovalMerges);
                Test("event 0: combined removal of an initialization FogMod changed is refused", Event0InitRemovalChangedRefused);
                Test("FogMod Asylum Demon removal keeps skips and parameters consistent", AsylumRemovalKeepsSkips);
                Test("boss overlay: health-bar swap + AI companions on FogMod's gate-removed event (11005392 pattern)", OverlayHealthBarCompanions);
                Test("boss overlay: removals on an event FogMod disabled with END (11815390 pattern)", OverlayRemovalInDisabledEvent);
                Test("boss overlay: both sides delete the same instructions; anchor moves past a FogMod-deleted command (11810310 pattern)", OverlayBothDeleteSame);
                Test("boss overlay: FogMod already inserts AI enable; no duplicate (11815394 pattern)", OverlayNoDuplicateAiEnable);
                Test("boss overlay: all identical copies removed (11210001 pattern)", OverlayIdenticalCopiesRemoved);
                Test("boss overlay: removal inside a FogMod skip adjusts FogMod's count (11415342 pattern)", OverlayRemovalInsideFogSkip);
                Test("boss overlay refuses mutations outside the allowed operations", OverlayRefusesOtherMutations);
                Test("boss overlay refuses ambiguous anchors, missing health bars and dead code", OverlayRefusesUnsafe);
                Test("boss overlay applies only to whitelisted events, after the normal merge refuses", OverlayOnlyWhitelisted);
                Test("instruction merge: count-preserving replacement inside a skip range merges", ReplacementInSkipRangeMerges);
                Test("instruction merge: both sides changing conditions is refused", BothConditionsRefused);
                Test("instruction merge: ambiguous alignment whose span the other side edits is refused", AmbiguousAlignmentRefused);
                Test("instruction merge: parameters are re-indexed", ParametersReindexed);
                Test("instruction merge: deleting a parameterized instruction is refused", ParameterizedDeletionRefused);
                Test("second Gargoyle: events 11015396 and 11015382 carried over by FogMod merge unchanged, without the overlay", SecondGargoyleCarriedOver);
                Test("second Gargoyle: the replaced release event keeps the hold / wait for 60% / release structure", SecondGargoyleReleaseSemantics);
                Test("second Gargoyle: the 15-event boss overlay is unchanged and excludes both events", SecondGargoyleNotInOverlay);
                Test("second Gargoyle: a FogMod form that diverges (control flow, anchors, parameters, threshold) is refused", SecondGargoyleDivergenceRefused);
                Test("second Gargoyle: vanilla forms (option off) merge to nothing", SecondGargoyleUntouchedWhenOptionOff);
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
                DataTest("second Gargoyle: both legacy forms survive a real FogMod run", SecondGargoyleSurvivesFogMod);
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

        private static MSB1 MsbWithHuman(bool declareAsPlayer)
        {
            var msb = new MSB1();
            if (declareAsPlayer) msb.Models.Players.Add(new MSB1.Model.Player { Name = "c0000" });
            msb.Parts.Enemies.Add(new MSB1.Part.Enemy { Name = "c0000_0002", ModelName = "c0000", NPCParamID = 0, ThinkParamID = 0 });
            return msb;
        }

        private static void ModelDeclarationInvariant()
        {
            var npcs = new PARAM { Rows = new List<PARAM.Row>() };
            var ok = new List<string>();
            Invariants.CheckMsbs(new Dictionary<string, MSB1> { ["m"] = MsbWithHuman(true) }, new Dictionary<string, MSB1> { ["m"] = MsbWithHuman(true) },
                npcs, npcs, new HashSet<int>(), ok);
            Check(ok.Count == 0, "Player-declared c0000 flagged: " + string.Join("; ", ok));
            var lost = new List<string>();
            Invariants.CheckMsbs(new Dictionary<string, MSB1> { ["m"] = MsbWithHuman(true) }, new Dictionary<string, MSB1> { ["m"] = MsbWithHuman(false) },
                npcs, npcs, new HashSet<int>(), lost);
            Check(lost.Count == 1 && lost[0].Contains("is not declared"), "lost declaration not flagged");
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
            ExpectConflict(() => { var merge = new EmevdMerge(new() { ["m"] = (b, c) }); merge.Merge("m", t); merge.ThrowIfConflicts(); });
        }

        private static void MergeDeleted()
        {
            var (b, c, t) = Trio();
            c.Events.RemoveAt(2);
            ExpectConflict(() => { var merge = new EmevdMerge(new() { ["m"] = (b, c) }); merge.Merge("m", t); merge.ThrowIfConflicts(); });
        }

        private static void MergeEvent0NonInit()
        {
            var (b, c, t) = Trio();
            c.Events[0].Instructions.Add(Ins(2003, 66, 1));
            ExpectConflict(() => { var merge = new EmevdMerge(new() { ["m"] = (b, c) }); merge.Merge("m", t); merge.ThrowIfConflicts(); });
        }

        // ---------------------------------------------------------------- instruction-level merge tests

        // Opcode structure of m15_00_00_00 event 11505392 as found in the real-format runs, with
        // placeholder entity IDs (no game data): instruction 0 is SKIP 7 lines IF event flag.
        private const int E1 = 111, E2 = 222, Area = 333;

        private static List<EMEVD.Instruction> Event11505392Base() => new List<EMEVD.Instruction>
        {
            Ins(1003, 1, 7, 11),          // 0  SKIP 7 IF Event Flag 11 OFF
            Ins(2004, 5, E1, 0),          // 1  Change Character Enable State
            Ins(2004, 37, E1),            // 2  Force Character Treasure
            Ins(2004, 29, E1, 1),         // 3  Set Character Backread State
            Ins(4, 7, 0, E2, 1),          // 4  IF Character Backread Status
            Ins(2004, 17, E2, 10, 0),     // 5  Request Character AI Command (combined deletes this)
            Ins(2004, 20, E2),            // 6  Request Character AI Re-plan
            Ins(1000, 4, 0),              // 7  END Unconditionally
            Ins(2004, 1, E1, 0),          // 8  Set Character AI State
            Ins(2004, 9, E1, 9000),       // 9  Set Special Animation(s)
            Ins(2000, 0, 0, 5000, 0),     // 10 Initialize Event
            Ins(2000, 0, 0, 5001, 0),     // 11 Initialize Event
            Ins(3, 0, 1, 1, 0, 777),      // 12 IF Event Flag
            Ins(3, 2, 1, 1, 10000, Area), // 13 IF In/Outside Area (FogMod rewrites this)
            Ins(0, 0, 0, 1, 1),           // 14 IF Condition Group
            Ins(2004, 1, E1, 1),          // 15 Set Character AI State
            Ins(2003, 11, 1, E1, 0, 44),  // 16 Display Boss Health Bar
        };

        /// <summary>FogMod GameDataWriter.cs:1061-1071 boss-trigger rewrite of the IF In/Outside Area at 13.</summary>
        private static List<EMEVD.Instruction> FogModTriggerRewrite(List<EMEVD.Instruction> instructions)
        {
            var result = instructions.ToList();
            int index = result.FindIndex(i => i.Bank == 3 && i.ID == 2);
            result.RemoveAt(index);
            result.Insert(index, Ins(0, 0, 1, 1, -7));
            foreach (int alt in new[] { Area, Area + 1 }) result.Insert(index, Ins(3, 2, -7, 1, 10000, alt));
            return result;
        }

        private static EMEVD.Event EventOf(long id, IEnumerable<EMEVD.Instruction> instructions) => Evt(id, instructions.ToArray());

        private static EMEVD.Event MergeOrNull(List<EMEVD.Instruction> b, List<EMEVD.Instruction> c, List<EMEVD.Instruction> t, out string reason) =>
            InstructionMerge.Merge(EventOf(1, b), EventOf(1, c), EventOf(1, t), out reason);

        private static void ExpectRefused(List<EMEVD.Instruction> b, List<EMEVD.Instruction> c, List<EMEVD.Instruction> t, string contains)
        {
            EMEVD.Event merged = MergeOrNull(b, c, t, out string reason);
            Check(merged == null, "merge unexpectedly succeeded");
            Check(reason.Contains(contains, StringComparison.Ordinal), $"reason \"{reason}\" lacks \"{contains}\"");
        }

        private static void ExpectSequence(EMEVD.Event merged, List<EMEVD.Instruction> expected, string reason)
        {
            Check(merged != null, $"merge refused: {reason}");
            Check(merged.Instructions.Count == expected.Count && merged.Instructions.Zip(expected).All(p => EmevdMerge.SameInstruction(p.First, p.Second)),
                "merged instruction sequence differs from the expected one");
        }

        private static void Event11505392Refused()
        {
            List<EMEVD.Instruction> b = Event11505392Base();
            List<EMEVD.Instruction> c = b.Where((_, i) => i != 5).ToList();
            List<EMEVD.Instruction> t = FogModTriggerRewrite(b);
            // Through the file-level merge, exactly as FogHost runs it.
            EMEVD.Event e0 = Evt(0, Ins(2000, 0, 0, 11505392, 0));
            EMEVD baseEvd = Evd(e0, EventOf(11505392, b));
            EMEVD combinedEvd = Evd(Copy(e0), EventOf(11505392, c));
            EMEVD output = Evd(Copy(e0), EventOf(11505392, t));
            try
            {
                var merge = new EmevdMerge(new() { ["m15_00_00_00.emevd.dcx"] = (baseEvd, combinedEvd) });
                merge.Merge("m15_00_00_00.emevd.dcx", output);
                merge.ThrowIfConflicts();
            }
            catch (FogHostException e) when (e.Code == "FOG_MERGE_CONFLICT")
            {
                Check(e.Message.Contains("event 11505392") && e.Message.Contains("inside the 7-line range of 1003[01] at base 0"), e.Message);
                return;
            }
            throw new Exception("expected FOG_MERGE_CONFLICT");
        }

        /// <summary>Base with SKIP 3 at 2 (covers 3..5) and plain commands elsewhere.</summary>
        private static List<EMEVD.Instruction> DerivedBase() => WithSkip();

        private static void DerivedReductionMerges()
        {
            List<EMEVD.Instruction> b = DerivedBase();
            var c = b.ToList(); c.RemoveAt(4); c[2] = Ins(1000, 3, 2);
            var t = b.ToList(); t[10] = Ins(2004, 1, 1010, 1);
            var expected = c.ToList(); expected[9] = Ins(2004, 1, 1010, 1);
            ExpectSequence(MergeOrNull(b, c, t, out string reason), expected, reason);
        }

        private static void DerivedIncreaseMerges()
        {
            List<EMEVD.Instruction> b = DerivedBase();
            var c = b.ToList(); c.Insert(4, Ins(2004, 9, 1, 2)); c[2] = Ins(1000, 3, 4);
            var t = b.ToList(); t.Insert(9, Ins(2003, 66, 1, 2));
            var expected = c.ToList(); expected.Insert(10, Ins(2003, 66, 1, 2));
            ExpectSequence(MergeOrNull(b, c, t, out string reason), expected, reason);
        }

        private static void DerivedOtherSideInsideRefused()
        {
            List<EMEVD.Instruction> b = DerivedBase();
            var c = b.ToList(); c.RemoveAt(4); c[2] = Ins(1000, 3, 2);
            var t = b.ToList(); t[3] = Ins(2004, 1, 1003, 1); // count-preserving edit inside the same range
            ExpectRefused(b, c, t, "the other side edits the range");
        }

        private static void DerivedWrongDeltaRefused()
        {
            List<EMEVD.Instruction> b = DerivedBase();
            var c = b.ToList(); c.RemoveAt(4); c[2] = Ins(1000, 3, 1); // one deletion, count reduced by two
            var t = b.ToList(); t[10] = Ins(2004, 1, 1010, 1);
            ExpectRefused(b, c, t, "does not cover exactly");
            var c2 = b.ToList(); c2[2] = Ins(1000, 3, 2); // count changed with no edit inside at all
            ExpectRefused(b, c2, t, "changes control-flow instruction 1000[03]");
        }

        private static void DerivedChangedSkipRefused()
        {
            List<EMEVD.Instruction> b = DerivedBase();
            var t = b.ToList(); t[10] = Ins(2004, 1, 1010, 1);
            var c = b.ToList(); c.RemoveAt(4); c[2] = Ins(1000, 1, 2); // different skip opcode, adjusted count
            ExpectRefused(b, c, t, "changes control-flow instruction");
            b[2] = Ins(1003, 1, 3, 11);
            t = b.ToList(); t[10] = Ins(2004, 1, 1010, 1);
            var c2 = b.ToList(); c2.RemoveAt(4); c2[2] = Ins(1003, 1, 2, 12); // adjusted count and a different flag
            ExpectRefused(b, c2, t, "changes control-flow instruction 1003[01]");
        }

        private static void DerivedAmbiguousRefused()
        {
            var x = Ins(2004, 1, 5, 0);
            var b = new List<EMEVD.Instruction> { Ins(1000, 3, 2), x, x }.Concat(Commands(6)).ToList();
            var c = new List<EMEVD.Instruction> { Ins(1000, 3, 1), x }.Concat(Commands(6)).ToList();
            var t = b.ToList(); t[3] = Ins(2004, 1, 1000, 9); // right after the ambiguous span 1..3
            ExpectRefused(b, c, t, "ambiguous alignment");
        }

        private static void DerivedBothSidesRefused()
        {
            List<EMEVD.Instruction> b = DerivedBase();
            var c = b.ToList(); c.RemoveAt(4); c[2] = Ins(1000, 3, 2);
            var t = b.ToList(); t.Insert(5, Ins(2004, 9, 1, 2)); t[2] = Ins(1000, 3, 4);
            ExpectRefused(b, c, t, "the other side edits the range");
        }

        private static void Event11505392DerivedMerges()
        {
            List<EMEVD.Instruction> b = Event11505392Base();
            var c = b.Where((_, i) => i != 5).ToList();
            c[0] = Ins(1003, 1, 6, 11); // the fixed DsrDataTool shrinks SKIP 7 to 6
            List<EMEVD.Instruction> t = FogModTriggerRewrite(b);
            EMEVD.Event merged = MergeOrNull(b, c, t, out string reason);
            ExpectSequence(merged, FogModTriggerRewrite(c), reason);
            Check(merged.Instructions[0].ArgData[0] == 6, "adjusted skip not taken");
            // The SKIP 6 block is exactly the combined engine's block; the merged event keeps
            // Set Character AI State outside it.
            Check(EmevdMerge.SameInstruction(merged.Instructions[7], Ins(2004, 1, E1, 0)), "first unskipped line moved");
        }

        private static void Event11600400DerivedMerges()
        {
            const int e = 1600500;
            var b = new List<EMEVD.Instruction>
            {
                Ins(2004, 31, e, 1),                   // 0
                Ins(2004, 10, e, 0),                   // 1
                Ins(1003, 1, 0x00010002, 0),           // 2 SKIP 2 IF event flag (covers 3..4)
                Ins(2004, 9, e, -1, -1, 29060, -1, -1),// 3 (combined removes)
                Ins(1000, 4, 0),                       // 4 END
                Ins(3, 3, 0x1FF, e, 10000, 0),         // 5
                Ins(4, 1, 0xFF, e, 10000),             // 6
                Ins(0, 0, 0x00FF0100),                 // 7
                Ins(2004, 9, e, -1),                   // 8 (combined removes)
                Ins(2003, 18, e, 29060, 0),            // 9 (combined removes)
            };
            var c = b.Where((_, i) => i != 3 && i != 8 && i != 9).ToList();
            c[2] = Ins(1003, 1, 0x00010001, 0);        // the fixed DsrDataTool shrinks SKIP 2 to 1
            var t = b.ToList();                        // FogMod flips the two states before the skip
            t[0] = Ins(2004, 31, e, 0);
            t[1] = Ins(2004, 10, e, 1);
            var expected = c.ToList(); expected[0] = t[0]; expected[1] = t[1];
            ExpectSequence(MergeOrNull(b, c, t, out string reason), expected, reason);
        }

        /// <summary>Exactly FogMod GameDataWriter.cs boss-trigger rewrite, preceded by the FogHost patch 7 call.</summary>
        private static void ApplyTriggerRewrite(EMEVD.Event evt, int index, int[] adds)
        {
            int trigger = Area;
            SkipRanges.BeforeReplace(evt, index, 1 + new[] { trigger }.Concat(adds).Count());
            evt.Instructions.RemoveAt(index);
            evt.Instructions.Insert(index, Ins(0, 0, 1, 1, -7));
            foreach (int alt in new[] { trigger }.Concat(adds)) evt.Instructions.Insert(index, Ins(3, 2, -7, 1, 10000, alt));
        }

        private static List<EMEVD.Instruction> CoveredBy(EMEVD.Event evt, int position) =>
            evt.Instructions.Skip(position + 1).Take(InstructionMerge.Lines(evt.Instructions[position])).ToList();

        private static void TriggerRewriteInsideSkip()
        {
            // 0 SKIP 2 IF Event Flag (covers 1..2), 1 command, 2 IF In/Outside Area (boss trigger), 3 command
            var region = Ins(3, 2, 1, 1, 10000, Area);
            var tail = Ins(2004, 1, 7, 1);
            var evt = Evt(11505393, Ins(1003, 1, 2, 11), Ins(2004, 5, 7, 0), region, tail);
            var first = evt.Instructions[1];
            ApplyTriggerRewrite(evt, 2, new[] { Area + 1 });
            Check(InstructionMerge.Lines(evt.Instructions[0]) == 4, $"SKIP is {InstructionMerge.Lines(evt.Instructions[0])}, expected 4");
            List<EMEVD.Instruction> covered = CoveredBy(evt, 0);
            Check(covered[0] == first && covered.Skip(1).All(i => i.Bank == 3 && i.ID == 2 || i.Bank == 0 && i.ID == 0), "SKIP does not cover exactly the command and the replacements");
            Check(!covered.Contains(tail), "SKIP now covers the instruction after the block");
        }

        private static void TriggerRewriteLastCoveredLine()
        {
            var evt = Evt(1, Ins(1000, 3, 1), Ins(3, 2, 1, 1, 10000, Area), Ins(2004, 1, 7, 1));
            ApplyTriggerRewrite(evt, 1, Array.Empty<int>());
            Check(InstructionMerge.Lines(evt.Instructions[0]) == 2, "SKIP not grown to 2");
            Check(CoveredBy(evt, 0).All(i => i.Bank < 1000), "SKIP covers the following command");
        }

        private static void TriggerRewriteOutsideSkip()
        {
            var evt = Evt(1, Ins(1003, 1, 1, 11), Ins(2004, 5, 7, 0), Ins(3, 2, 1, 1, 10000, Area), Ins(2004, 1, 7, 1));
            ApplyTriggerRewrite(evt, 2, new[] { Area + 1 });
            Check(InstructionMerge.Lines(evt.Instructions[0]) == 1, "SKIP outside the rewrite changed");
        }

        private static void TriggerRewriteNestedSkips()
        {
            // 0 SKIP 4 (1..4), 1 command, 2 SKIP 1 IF cond (3), 3 IF In/Outside Area, 4 command
            var evt = Evt(1, Ins(1003, 1, 4, 11), Ins(2004, 5, 7, 0), Ins(1000, 1, 0x00000101), Ins(3, 2, 1, 1, 10000, Area), Ins(2004, 1, 7, 1));
            ApplyTriggerRewrite(evt, 3, Array.Empty<int>());
            Check(InstructionMerge.Lines(evt.Instructions[0]) == 5 && InstructionMerge.Lines(evt.Instructions[2]) == 2, "nested SKIPs not both grown by 1");
        }

        private static void TriggerRewriteRefusals()
        {
            void Expect(Action action, string contains)
            {
                try { action(); }
                catch (FogHostException e) when (e.Code == "FOG_ENGINE_FAILED" && e.Message.Contains(contains, StringComparison.Ordinal)) { return; }
                throw new Exception($"expected FOG_ENGINE_FAILED containing \"{contains}\"");
            }
            var parameterized = Evt(1, Ins(1003, 1, 2, 11), Ins(2004, 5, 7, 0), Ins(3, 2, 1, 1, 10000, Area));
            parameterized.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 1));
            Expect(() => SkipRanges.BeforeReplace(parameterized, 2, 3), "event parameter");
            var full = Evt(1, Ins(1000, 3, 255), Ins(3, 2, 1, 1, 10000, Area));
            Expect(() => SkipRanges.BeforeReplace(full, 1, 3), "257 lines");
            var flow = Evt(1, Ins(1000, 3, 1), Ins(1000, 4, 0));
            Expect(() => SkipRanges.BeforeReplace(flow, 1, 3), "control flow");
        }

        private const int Boss = 4242;

        private static EMEVD.Instruction Bar(int name) => new EMEVD.Instruction(2003, 11, new byte[] { 1, 0, 0, 0 }.Concat(BitConverter.GetBytes(Boss)).Concat(new byte[] { 0, 0 }).Concat(BitConverter.GetBytes((short)name)).ToArray());
        private static EMEVD.Instruction AiOn() => Ins(2004, 1, Boss, 1);
        private static EMEVD.Instruction AiOff() => Ins(2004, 1, Boss, 0);
        private static EMEVD.Instruction Replan() => Ins(2004, 20, Boss);
        private static EMEVD.Instruction HostGateA() => Ins(4, 3, 0xFF, 10000, 2);
        private static EMEVD.Instruction HostGateB() => Ins(4, 3, 0xFF, 10000, 12);
        private static EMEVD.Instruction EndIfGate() => Ins(1000, 2, 0x00FF0100);

        private static EMEVD.Event Overlay(long id, List<EMEVD.Instruction> b, List<EMEVD.Instruction> c, List<EMEVD.Instruction> t, out string reason) =>
            BossOverlay.Apply(EventOf(id, b), EventOf(id, c), EventOf(id, t), out reason);

        private static void ExpectOverlay(long id, List<EMEVD.Instruction> b, List<EMEVD.Instruction> c, List<EMEVD.Instruction> t, List<EMEVD.Instruction> expected)
        {
            EMEVD.Event result = Overlay(id, b, c, t, out string reason);
            Check(result != null, $"overlay refused: {reason}");
            string Show(IEnumerable<EMEVD.Instruction> list) => string.Join(" ", list.Select(i => $"{i.Bank}[{i.ID}]"));
            Check(result.Instructions.Count == expected.Count && result.Instructions.Zip(expected).All(p => EmevdMerge.SameInstruction(p.First, p.Second)),
                $"overlay result differs: {Show(result.Instructions)} vs {Show(expected)}");
        }

        private static void ExpectOverlayRefused(long id, List<EMEVD.Instruction> b, List<EMEVD.Instruction> c, List<EMEVD.Instruction> t, string contains)
        {
            EMEVD.Event result = Overlay(id, b, c, t, out string reason);
            Check(result == null, "overlay unexpectedly succeeded");
            Check(reason.Contains(contains, StringComparison.Ordinal), $"reason \"{reason}\" lacks \"{contains}\"");
        }

        /// <summary>11005392 shape: trigger, host gate, activation, AI on, health bar.</summary>
        private static List<EMEVD.Instruction> BossStartBase() => new List<EMEVD.Instruction>
        {
            Ins(1003, 1, 0x00010101, 11), Ins(2004, 5, Boss, 0), AiOff(), Ins(3, 0, 1, 1, 0, 777), Ins(3, 2, 1, 1, 10000, Area), Ins(0, 0, 0x00010100),
            HostGateA(), HostGateB(), EndIfGate(), Ins(2004, 5, Boss, 1), Ins(2003, 2, 777, 1), AiOn(), Bar(100),
        };

        private static void OverlayHealthBarCompanions()
        {
            var b = BossStartBase();
            var c = b.Take(12).Concat(new[] { Bar(200), AiOn(), Replan() }).ToList(); // combined: new name + companions
            var t = b.Where((_, i) => i < 6 || i > 8).ToList();                          // FogMod: host gate removed
            var expected = t.Take(t.Count - 1).Concat(new[] { Bar(200), AiOn(), Replan() }).ToList();
            ExpectOverlay(11005392, b, c, t, expected);
        }

        private static void OverlayRemovalInDisabledEvent()
        {
            var warp = Ins(2004, 41, Boss, 1, 5555, -1);
            var b = new List<EMEVD.Instruction> { Ins(3, 0, 1, 0, 0, 16), Ins(3, 18, 1, 1, 7, 0), Ins(0, 0, 0x00010100), warp, Ins(2004, 14, 10000, 7), Ins(1000, 4, 1) };
            var c = b.Where((_, i) => i != 3).ToList();
            var t = new[] { Ins(3, 2, 0x00000100, 10000, 7), Ins(1000, 4, 0) }.Concat(b).ToList(); // FogMod disables the event
            var expected = t.Where(i => !EmevdMerge.SameInstruction(i, warp)).ToList();
            ExpectOverlay(11815390, b, c, t, expected);
        }

        private static void OverlayBothDeleteSame()
        {
            // 11810310 shape: END IFs, objects, model-specific lines, trigger, flag, home, objects.
            var endIf = Ins(1003, 2, 0x00010000, 11);
            var obj0 = Ins(2005, 3, 7, 0); var sfx0 = Ins(2006, 1, 8, 0);
            var anim = Ins(2004, 9, Boss, 9000); var warp = Ins(2004, 41, Boss, 1, 5555, -1); var force = Ins(2003, 18, Boss, 9060, 0);
            var home = Ins(2004, 13, Boss, 5556);
            var obj1 = Ins(2005, 3, 7, 1); var sfx1 = Ins(2006, 2, 8);
            var b = new List<EMEVD.Instruction> { endIf, obj0, sfx0, AiOff(), anim, warp, Ins(3, 2, 0x00000100, 10000, 9), Ins(2003, 2, 11, 1), force, home, obj1, sfx1 };
            // Combined: removes anim, warp, force; re-enables AI right before the terminal instruction.
            var c = new List<EMEVD.Instruction> { endIf, obj0, sfx0, AiOff(), Ins(3, 2, 0x00000100, 10000, 9), Ins(2003, 2, 11, 1), home, obj1, AiOn(), sfx1 };
            // FogMod: removes the objects/SFX and (its own Asylum edit) warp + force animation.
            var t = new List<EMEVD.Instruction> { endIf, AiOff(), anim, Ins(3, 2, 0x00000100, 10000, 9), Ins(2003, 2, 11, 1), home };
            var expected = new List<EMEVD.Instruction> { endIf, AiOff(), Ins(3, 2, 0x00000100, 10000, 9), Ins(2003, 2, 11, 1), home, AiOn() };
            ExpectOverlay(11810310, b, c, t, expected);
            Check(BossOverlay.Notes.Any(n => n.Contains("already removed by FogMod")), "both-sides removal not reported as satisfied");
        }

        private static void OverlayNoDuplicateAiEnable()
        {
            var b = new List<EMEVD.Instruction> { Ins(3, 0, 0, 0, 0, 1), Ins(0, 0, 0x00010100), Ins(2003, 2, 12, 1), Bar(100), Ins(1000, 4, 1) };
            var c = new List<EMEVD.Instruction> { b[0], b[1], b[2], Bar(200), AiOn(), Replan(), b[4] };
            var t = new List<EMEVD.Instruction> { b[0], b[1], b[2], Bar(100), AiOn(), b[4] }; // FogMod adds AI enable itself
            var expected = new List<EMEVD.Instruction> { b[0], b[1], b[2], Bar(200), AiOn(), Replan(), b[4] };
            ExpectOverlay(11815394, b, c, t, expected);
            EMEVD.Event result = Overlay(11815394, b, c, t, out _);
            Check(result.Instructions.Count(i => EmevdMerge.SameInstruction(i, AiOn())) == 1, "AI enable duplicated");
        }

        private static void OverlayIdenticalCopiesRemoved()
        {
            var backread = Ins(2004, 29, Boss, 1);
            var b = new List<EMEVD.Instruction> { Ins(1003, 1, 0x00010003, 0), Ins(2004, 5, Boss, 0), backread, Ins(1000, 4, 0), Ins(4, 0, 0, Boss, 1), Ins(2003, 12, Boss), backread, Ins(2004, 5, 5, 1) };
            var c = new List<EMEVD.Instruction> { Ins(1003, 1, 0x00010002, 0), b[1], b[3], b[4], b[5], b[7] };
            var t = b.ToList(); t.Insert(4, Ins(3, 0, 0, 1, 0, 99)); // FogMod adds a condition after the END
            var expected = new List<EMEVD.Instruction> { Ins(1003, 1, 0x00010002, 0), b[1], b[3], Ins(3, 0, 0, 1, 0, 99), b[4], b[5], b[7] };
            ExpectOverlay(11210001, b, c, t, expected);
        }

        private static void OverlayRemovalInsideFogSkip()
        {
            // FogMod deletes a whole conditional block and shrinks SKIP 8 -> 3; combined removes a backread inside.
            var backread = Ins(2004, 29, Boss, 1);
            var b = new List<EMEVD.Instruction> { Ins(1003, 1, 8, 16), Ins(1003, 5, 0x00000104), Ins(2005, 3, 1, 0), Ins(2006, 1, 2, 0), Ins(2005, 3, 3, 0), Ins(2006, 1, 4, 0), Ins(2004, 5, Boss, 0), backread, Ins(1000, 4, 0), AiOff(), Bar(100) };
            var c = new List<EMEVD.Instruction> { Ins(1003, 1, 7, 16) }.Concat(b.Skip(1).Take(6)).Concat(new[] { b[8], b[9], Bar(200), AiOn(), Replan() }).ToList();
            var t = new List<EMEVD.Instruction> { Ins(1003, 1, 3, 16), b[6], backread, b[8], b[9], b[10] };
            var expected = new List<EMEVD.Instruction> { Ins(1003, 1, 2, 16), b[6], b[8], b[9], Bar(200), AiOn(), Replan() };
            ExpectOverlay(11415342, b, c, t, expected);
        }

        private static void OverlayRefusesOtherMutations()
        {
            var b = BossStartBase();
            var t = b.Where((_, i) => i < 6 || i > 8).ToList();
            var c1 = b.Where((_, i) => i != 10).ToList();                         // removes Set Event Flag
            ExpectOverlayRefused(11005392, b, c1, t, "not an allowed boss mutation");
            var c2 = b.ToList(); c2.Insert(12, Ins(2003, 2, 999, 1));               // inserts Set Event Flag
            ExpectOverlayRefused(11005392, b, c2, t, "not an allowed boss mutation");
            var c3 = b.ToList(); c3[3] = Ins(3, 0, 1, 1, 0, 778);                   // edits a condition
            ExpectOverlayRefused(11005392, b, c3, t, "not an allowed boss mutation");
        }

        private static void OverlayRefusesUnsafe()
        {
            var b = BossStartBase();
            var c = b.Take(12).Concat(new[] { Bar(200), AiOn(), Replan() }).ToList();
            var noBar = b.Take(12).ToList();                                        // FogMod no longer shows the bar
            ExpectOverlayRefused(11005392, b, c, noBar, "no longer exists");
            var twoBars = b.Concat(new[] { Bar(100) }).ToList();                    // ambiguous health bar
            ExpectOverlayRefused(11005392, b, c, twoBars, "ambiguous");
            var ended = b.Take(12).Concat(new[] { Ins(1000, 4, 0), Bar(100) }).ToList(); // bar is dead code under FogMod
            ExpectOverlayRefused(11005392, b, c, ended, "unreachable");
        }

        private static void OverlayOnlyWhitelisted()
        {
            var b = BossStartBase();
            var c = b.Take(12).Concat(new[] { Bar(200), AiOn(), Replan() }).ToList();
            var t = b.Where((_, i) => i < 6 || i > 8).ToList();
            ExpectOverlayRefused(12345678, b, c, t, "not a whitelisted boss event");
            // Through the file-level merge: the normal merge refuses (FogMod control-flow edit), the overlay applies.
            var e0 = Evt(0, Ins(2000, 0, 0, 11005392, 0));
            var merge = new EmevdMerge(new() { ["m10_00_00_00.emevd.dcx"] = (Evd(e0, EventOf(11005392, b)), Evd(Copy(e0), EventOf(11005392, c))) });
            EMEVD output = Evd(Copy(e0), EventOf(11005392, t));
            merge.Merge("m10_00_00_00.emevd.dcx", output);
            merge.ThrowIfConflicts();
            Check(merge.BossOverlays.Count == 1 && merge.InstructionMerges.Count == 0, "overlay not used as the fallback");
            // A non-whitelisted event with the same edits stays a conflict.
            var merge2 = new EmevdMerge(new() { ["m"] = (Evd(e0, EventOf(12345678, b)), Evd(Copy(e0), EventOf(12345678, c))) });
            merge2.Merge("m", Evd(Copy(e0), EventOf(12345678, t)));
            Check(merge2.Conflicts.Count == 1, "non-whitelisted event was overlaid");
        }

        private static void Event11505392OutsideSkipMerges()
        {
            List<EMEVD.Instruction> b = Event11505392Base();
            List<EMEVD.Instruction> c = b.Where((_, i) => i != 9).ToList(); // outside SKIP 7 (1..7) and its boundary (8)
            List<EMEVD.Instruction> t = FogModTriggerRewrite(b);
            EMEVD.Event merged = MergeOrNull(b, c, t, out string reason);
            ExpectSequence(merged, FogModTriggerRewrite(c), reason);
        }

        private static List<EMEVD.Instruction> Commands(int count) =>
            Enumerable.Range(0, count).Select(i => Ins(2004, 1, 1000 + i, 0)).ToList();

        private static void IndependentEdits()
        {
            List<EMEVD.Instruction> b = Commands(10);
            var c = b.ToList(); c.Insert(2, Ins(2004, 9, 1, 2));
            var t = b.ToList(); t[7] = Ins(2004, 1, 1007, 1);
            var expected = b.ToList(); expected[7] = Ins(2004, 1, 1007, 1); expected.Insert(2, Ins(2004, 9, 1, 2));
            ExpectSequence(MergeOrNull(b, c, t, out string reason), expected, reason);
        }

        private static void DeletionPlusUnrelatedEdit()
        {
            List<EMEVD.Instruction> b = Commands(10);
            var c = b.Where((_, i) => i != 3).ToList();
            var t = b.ToList(); t.Insert(8, Ins(2003, 66, 1, 2));
            var expected = b.ToList(); expected.Insert(8, Ins(2003, 66, 1, 2)); expected.RemoveAt(3);
            ExpectSequence(MergeOrNull(b, c, t, out string reason), expected, reason);
        }

        private static void SameInstructionRefused()
        {
            List<EMEVD.Instruction> b = Commands(6);
            var c = b.ToList(); c[3] = Ins(2004, 1, 1003, 1);
            var t = b.ToList(); t[3] = Ins(2004, 1, 1003, 2);
            ExpectRefused(b, c, t, "both sides change");
        }

        private static void AdjacentEditsRefused()
        {
            List<EMEVD.Instruction> b = Commands(6);
            var c = b.Where((_, i) => i != 3).ToList();
            var t = b.ToList(); t.Insert(4, Ins(2003, 66, 1, 2));
            ExpectRefused(b, c, t, "both sides change");
        }

        private static List<EMEVD.Instruction> WithSkip()
        {
            var b = Commands(12);
            b[2] = Ins(1000, 3, 3); // SKIP 3 lines unconditionally: covers 3..5
            return b;
        }

        private static void SkipCountChangeRefused()
        {
            List<EMEVD.Instruction> b = WithSkip();
            var c = b.ToList(); c[2] = Ins(1000, 3, 2);
            var t = b.ToList(); t[10] = Ins(2004, 1, 1010, 1);
            ExpectRefused(b, c, t, "changes control-flow instruction 1000[03]");
        }

        private static void InsertedControlFlowRefused()
        {
            List<EMEVD.Instruction> b = Commands(10);
            var c = b.ToList(); c.Insert(8, Ins(1000, 4, 0));
            var t = b.ToList(); t[1] = Ins(2004, 1, 1001, 1);
            ExpectRefused(b, c, t, "changes control-flow instruction 1000[04]");
            var t2 = b.ToList(); t2.Insert(5, Ins(1001, 0, 1));
            var c2 = b.ToList(); c2[9] = Ins(2004, 1, 1009, 1);
            ExpectRefused(b, c2, t2, "changes control-flow instruction 1001[00]");
        }

        private static void InsertionInSkipRangeRefused()
        {
            List<EMEVD.Instruction> b = WithSkip();
            var c = b.ToList(); c[10] = Ins(2004, 1, 1010, 1);
            var t = b.ToList(); t.Insert(4, Ins(2004, 9, 1, 2));
            ExpectRefused(b, c, t, "FogMod edit at base 4 is inside the 3-line range of 1000[03] at base 2");
        }

        private static void SkipBoundaryMerges()
        {
            // SKIP 3 at 2 covers 3..5. An insertion before base 6 is not covered in C (count unchanged)
            // and is not covered in the merged event either.
            List<EMEVD.Instruction> b = WithSkip();
            var c = b.ToList(); c.Insert(6, Ins(2004, 9, 1, 2));
            var t = b.ToList(); t[10] = Ins(2004, 1, 1010, 1);
            var expected = c.ToList(); expected[11] = Ins(2004, 1, 1010, 1);
            EMEVD.Event merged = MergeOrNull(b, c, t, out string reason);
            ExpectSequence(merged, expected, reason);
            Check(InstructionMerge.Lines(merged.Instructions[2]) == 3, "skip count changed");
        }

        private static void SkipFirstLineRefused()
        {
            List<EMEVD.Instruction> b = WithSkip();
            var c = b.ToList(); c.Insert(3, Ins(2004, 9, 1, 2)); // now covered, pushing line 5 out
            var t = b.ToList(); t[10] = Ins(2004, 1, 1010, 1);
            ExpectRefused(b, c, t, "inside the 3-line range");
        }

        private static void AmbiguousIdenticalResultMerges()
        {
            // Base tail: AI on (X), health bar. Combined changes the bar name and appends AI on (X) + re-plan,
            // an instruction identical to an existing one, so earliest and latest alignments differ.
            var aiOn = Ins(2004, 1, 111, 1);
            var b = Commands(6).Concat(new[] { aiOn, Ins(2003, 11, 1, 111, 10) }).ToList();
            var c = Commands(6).Concat(new[] { aiOn, Ins(2003, 11, 1, 111, 20), aiOn, Ins(2004, 20, 111) }).ToList();
            var t = b.ToList(); t[1] = Ins(2004, 1, 1001, 9); // FogMod edits far from the ambiguous span
            var expected = c.ToList(); expected[1] = Ins(2004, 1, 1001, 9);
            ExpectSequence(MergeOrNull(b, c, t, out string reason), expected, reason);
        }

        private static void AmbiguousSpanTouchedRefused()
        {
            var aiOn = Ins(2004, 1, 111, 1);
            var b = Commands(6).Concat(new[] { aiOn, Ins(2003, 11, 1, 111, 10) }).ToList();
            var c = Commands(6).Concat(new[] { aiOn, Ins(2003, 11, 1, 111, 20), aiOn, Ins(2004, 20, 111) }).ToList();
            var t = b.ToList(); t.Insert(6, Ins(2004, 9, 1, 2)); // FogMod inserts right before the ambiguous AI-on line
            ExpectRefused(b, c, t, "ambiguous alignment");
        }

        private static void DerivedWithBoundaryEditMerges()
        {
            // m12_01 11215003 shape: SKIP 5 at 0 covers 1..5; FogMod rewrites the region check at 3 and
            // grows the skip to 7; combined replaces the health bar at 6 (right after the covered lines).
            var b = new List<EMEVD.Instruction>
            {
                Ins(1003, 1, 0x00010105, 0), Ins(2004, 1, 111, 0), Ins(3, 0, 1, 1, 0, 777),
                Ins(3, 2, 1, 1, 10000, Area), Ins(0, 0, 0x00010100), Ins(2004, 1, 111, 1),
                Ins(2003, 11, 1, 111, 10), Ins(2003, 18, 111, 3017, 0),
            };
            var t = b.ToList();
            t[0] = Ins(1003, 1, 0x00010107, 0);
            t.RemoveAt(3);
            t.Insert(3, Ins(0, 0, 1, 1, -7));
            t.Insert(3, Ins(3, 2, -7, 1, 10000, Area));
            t.Insert(3, Ins(3, 2, -7, 1, 10000, Area + 1));
            var c = b.Take(6).Concat(new[] { Ins(2003, 11, 1, 111, 20), Ins(2004, 1, 111, 1), Ins(2004, 20, 111) }).ToList();
            var expected = t.Take(8).Concat(c.Skip(6)).ToList();
            ExpectSequence(MergeOrNull(b, c, t, out string reason), expected, reason);
        }

        private static (EMEVD B, EMEVD C, EMEVD T) Event0Trio(bool fogKeepsInit)
        {
            var floorA = Ins(2000, 0, 0, 11410200, 0);
            var floorB = Ins(2000, 0, 1, 11410201, 0);
            var other = Ins(2000, 0, 2, 11410300, 0);
            EMEVD b = Evd(Evt(0, other, floorA, floorB));
            EMEVD c = Evd(Evt(0, Copy(Evt(0, other)).Instructions[0], Ins(2000, 0, 3, 11419980, 0)));
            EMEVD t = fogKeepsInit
                ? Evd(Evt(0, Ins(2000, 0, 2, 11410300, 0), Ins(2000, 0, 0, 11410200, 0), Ins(2000, 0, 1, 11410201, 0), Ins(2000, 0, 9, 5000, 0)))
                : Evd(Evt(0, Ins(2000, 0, 2, 11410300, 0), Ins(2000, 0, 0, 11410220, 0), Ins(2000, 0, 1, 11410221, 0)));
            return (b, c, t);
        }

        private static void Event0InitRemovalMerges()
        {
            var (b, c, t) = Event0Trio(true);
            var merge = new EmevdMerge(new() { ["m14_01_00_00.emevd.dcx"] = (b, c) });
            merge.Merge("m14_01_00_00.emevd.dcx", t);
            merge.ThrowIfConflicts();
            var inits = t.Events[0].Instructions.Select(i => BitConverter.ToInt32(i.ArgData, 4)).ToList();
            Check(!inits.Contains(11410200) && !inits.Contains(11410201), "removed initializations still present");
            Check(inits.Contains(11410300) && inits.Contains(5000) && inits.Contains(11419980), "kept or added initializations missing");
        }

        private static void Event0InitRemovalChangedRefused()
        {
            var (b, c, t) = Event0Trio(false);
            ExpectConflict(() => { var merge = new EmevdMerge(new() { ["m"] = (b, c) }); merge.Merge("m", t); merge.ThrowIfConflicts(); });
        }

        private static void AsylumRemovalKeepsSkips()
        {
            // SKIP 3 at 0 covers 1..3 (2003[18] at 2); a parameter targets instruction 4.
            var evt = Evt(11810310, Ins(1000, 3, 3), Ins(2004, 1, 5, 0), Ins(2003, 18, 5, 9000, 0), Ins(2004, 1, 5, 1), Ins(2004, 9, 5, 1), Ins(2004, 41, 5, 1, 2, -1));
            evt.Parameters.Add(new EMEVD.Parameter(4, 4, 0, 4));
            var covered = new[] { evt.Instructions[1], evt.Instructions[3] };
            for (int i = evt.Instructions.Count - 1; i >= 0; i--)
            {
                var ins = evt.Instructions[i];
                if ((ins.Bank == 2003 && ins.ID == 18) || (ins.Bank == 2004 && ins.ID == 41)) SkipRanges.Remove(evt, i);
            }
            Check(InstructionMerge.Lines(evt.Instructions[0]) == 2 && CoveredBy(evt, 0).SequenceEqual(covered), "skip does not cover the same surviving lines");
            Check(evt.Parameters[0].InstructionIndex == 3 && evt.Instructions[3].ID == 9, "parameter not re-indexed to the same instruction");
            var parameterized = Evt(1, Ins(2003, 18, 5, 9000, 0));
            parameterized.Parameters.Add(new EMEVD.Parameter(0, 4, 0, 4));
            try { SkipRanges.Remove(parameterized, 0); }
            catch (FogHostException e) when (e.Code == "FOG_ENGINE_FAILED") { return; }
            throw new Exception("removing a parameterized instruction was not refused");
        }

        private static void ReplacementInSkipRangeMerges()
        {
            List<EMEVD.Instruction> b = WithSkip();
            var c = b.ToList(); c[4] = Ins(2004, 1, 1004, 1); // argument change inside SKIP 3 (3..5)
            var t = b.ToList(); t.Insert(10, Ins(2004, 9, 1, 2)); // insertion far outside the range
            var expected = b.ToList(); expected.Insert(10, Ins(2004, 9, 1, 2)); expected[4] = Ins(2004, 1, 1004, 1);
            ExpectSequence(MergeOrNull(b, c, t, out string reason), expected, reason);
        }

        private static void BothConditionsRefused()
        {
            List<EMEVD.Instruction> b = Commands(10);
            b[5] = Ins(3, 0, 1, 1, 0, 777);
            var c = b.ToList(); c.Insert(1, Ins(3, 0, 2, 1, 0, 778));
            var t = b.ToList(); t[5] = Ins(3, 0, 1, 0, 0, 777);
            ExpectRefused(b, c, t, "both sides change condition-group instructions");
        }

        private static void AmbiguousAlignmentRefused()
        {
            var x = Ins(2004, 1, 5, 0);
            var b = new List<EMEVD.Instruction> { x, x, Ins(2004, 2, 1), Ins(2004, 3, 1), Ins(2004, 4, 1) };
            var c = new List<EMEVD.Instruction> { x, Ins(2004, 2, 1), Ins(2004, 3, 1), Ins(2004, 4, 1) };
            var t = b.ToList(); t[2] = Ins(2004, 2, 7); // replaces the line right after the ambiguous x, x
            ExpectRefused(b, c, t, "ambiguous alignment");
        }

        private static EMEVD.Event WithParameter(List<EMEVD.Instruction> instructions, int index)
        {
            EMEVD.Event e = EventOf(1, instructions);
            e.Parameters.Add(new EMEVD.Parameter(index, 4, 0, 4));
            return e;
        }

        private static void ParametersReindexed()
        {
            List<EMEVD.Instruction> b = Commands(10);
            var c = b.Where((_, i) => i != 1).ToList();
            var t = b.ToList(); t[8] = Ins(2004, 1, 1008, 1);
            EMEVD.Event merged = InstructionMerge.Merge(WithParameter(b, 4), WithParameter(c, 3), WithParameter(t, 4), out string reason);
            Check(merged != null, $"merge refused: {reason}");
            Check(merged.Parameters.Count == 1 && merged.Parameters[0].InstructionIndex == 3, "parameter not re-indexed to 3");
        }

        private static void ParameterizedDeletionRefused()
        {
            List<EMEVD.Instruction> b = Commands(10);
            var c = b.Where((_, i) => i != 4).ToList();
            var t = b.ToList(); t[8] = Ins(2004, 1, 1008, 1);
            EMEVD.Event merged = InstructionMerge.Merge(WithParameter(b, 4), EventOf(1, c), WithParameter(t, 4), out string reason);
            Check(merged == null && reason != null && reason.Contains("parameterized", StringComparison.Ordinal), $"expected refusal, got {reason ?? "success"}");
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

        // ---------------------------------------------------------------- Replace second Gargoyle (events 11015396 / 11015382)
        //
        // FogMod makes no edit of its own to these two events: GameDataWriter copies the game directory's
        // instruction list into them ("11205382, 11015382, 11015396 have no modifications in fog rando, so they
        // can be copied over"). The combined engine's legacy "Replace second Gargoyle" forms therefore reach the
        // normal three-way merge as "FogMod output identical to the combined event", which rule 3 takes
        // unchanged. No boss-overlay entry is involved, and none is added (docs/FOGHOST_INTEGRATION.md).
        // Instruction data below is taken from real dumps of the DS1R events (vanilla, and the combined engine's
        // output for a replaced second Gargoyle).

        private const long GargoyleRelease = 11015396; // releases body 1010801 when 1010800 drops to 60%
        private const long TaurusIntro = 11015382;     // holds entity 1010700 until the player enters volume 1012701
        private const int SecondGargoyle = 1010801;
        private const int FirstGargoyle = 1010800;
        private const int Taurus = 1010700;

        private static EMEVD.Event Restart(long id, params EMEVD.Instruction[] instructions)
        {
            var evt = new EMEVD.Event(id, EMEVD.Event.RestBehaviorType.Restart);
            evt.Instructions.AddRange(instructions);
            return evt;
        }

        private static EMEVD.Event VanillaGargoyleRelease() => Restart(GargoyleRelease,
            Ins(1003, 2, 0x100, 3),
            Ins(2004, 1, SecondGargoyle, 0),
            Ins(2004, 9, SecondGargoyle, 7000, -1, -1, -1, -1),
            Ins(2004, 15, SecondGargoyle, 1),
            Ins(2004, 30, SecondGargoyle, 0),
            Ins(4, 2, 0, FirstGargoyle, 5, 0x3F19999A),
            Ins(2004, 9, SecondGargoyle, -1, -1, -1, -1, -1),
            Ins(2004, 41, SecondGargoyle, 1, 1012650, -1),
            Ins(2003, 18, SecondGargoyle, 7001, 0x100),
            Ins(2004, 15, SecondGargoyle, 0),
            Ins(2004, 1, SecondGargoyle, 1),
            Ins(2003, 11, 1, SecondGargoyle, 0x14E60001));

        // Combined engine output for a replaced second Gargoyle: the held-then-released structure of the legacy
        // template stays; the Gargoyle-specific instructions go; the boss bar carries the new boss's name.
        private static EMEVD.Event ReplacedGargoyleRelease() => Restart(GargoyleRelease,
            Ins(1003, 2, 0x100, 3),
            Ins(2004, 1, SecondGargoyle, 0),
            Ins(2004, 15, SecondGargoyle, 1),
            Ins(4, 2, 0, FirstGargoyle, 5, 0x3F19999A),
            Ins(2004, 15, SecondGargoyle, 0),
            Ins(2004, 1, SecondGargoyle, 1),
            Ins(2003, 11, 1, SecondGargoyle, 0x14640001),
            Ins(2004, 1, SecondGargoyle, 1),
            Ins(2004, 20, SecondGargoyle));

        private static EMEVD.Event VanillaTaurusIntro() => Restart(TaurusIntro,
            Ins(1003, 1, 0x00010002, 0),
            Ins(2004, 9, Taurus, -1, -1, -1, -1, -1),
            Ins(1000, 4, 0),
            Ins(2004, 1, Taurus, 0),
            Ins(2004, 9, Taurus, 9001, -1, -1, -1, -1),
            Ins(2004, 30, Taurus, 0),
            Ins(4, 1, 1, Taurus, 10000),
            Ins(3, 6, 2),
            Ins(3, 2, 0x102, 10000, 1012701),
            Ins(0, 0, 0x000101FF),
            Ins(0, 0, 0x000201FF),
            Ins(0, 0, 0x00FF0100),
            Ins(2003, 2, 11010005, 1),
            Ins(2004, 9, Taurus, -1, -1, -1, -1, -1),
            Ins(2003, 18, Taurus, 9061, 0x100),
            Ins(2004, 1, Taurus, 1));

        // The combined engine's standard replaced-boss edit of the same event (independent of the Gargoyle option).
        private static EMEVD.Event ReplacedTaurusIntro() => Restart(TaurusIntro,
            Ins(1003, 1, 0x00010001, 0),
            Ins(1000, 4, 0),
            Ins(2004, 1, Taurus, 0),
            Ins(4, 1, 1, Taurus, 10000),
            Ins(3, 6, 2),
            Ins(3, 2, 0x102, 10000, 1012701),
            Ins(0, 0, 0x000101FF),
            Ins(0, 0, 0x000201FF),
            Ins(0, 0, 0x00FF0100),
            Ins(2003, 2, 11010005, 1),
            Ins(2004, 1, Taurus, 1));

        private static EMEVD.Event Event0() => Restart(0,
            Ins(2000, 0, 0, (int)GargoyleRelease, 0),
            Ins(2000, 0, 0, (int)TaurusIntro, 0));

        /// <summary>Base = vanilla; combined = legacy forms; FogMod output = base with the game's instruction lists copied in.</summary>
        private static (EMEVD B, EMEVD C, EMEVD T) GargoyleTrio()
        {
            EMEVD b = Evd(Event0(), VanillaGargoyleRelease(), VanillaTaurusIntro());
            EMEVD c = Evd(Event0(), ReplacedGargoyleRelease(), ReplacedTaurusIntro());
            EMEVD t = Evd(Event0(), VanillaGargoyleRelease(), VanillaTaurusIntro());
            foreach (long id in new[] { GargoyleRelease, TaurusIntro })
            {
                EMEVD.Event fog = t.Events.Single(e => e.ID == id);
                EMEVD.Event game = c.Events.Single(e => e.ID == id);
                fog.Instructions = game.Instructions.Select(i => new EMEVD.Instruction(i.Bank, i.ID, i.ArgData.ToArray())).ToList();
                fog.Parameters = new List<EMEVD.Parameter>(game.Parameters);
            }
            return (b, c, t);
        }

        private static void SecondGargoyleCarriedOver()
        {
            var (b, c, t) = GargoyleTrio();
            var merge = new EmevdMerge(new() { ["m10_01_00_00.emevd.dcx"] = (b, c) });
            merge.Merge("m10_01_00_00.emevd.dcx", t);
            merge.ThrowIfConflicts();
            Check(merge.Conflicts.Count == 0, "unexpected conflicts");
            Check(merge.BossOverlays.Count == 0, "the boss overlay must not be involved");
            Check(merge.InstructionMerges.Count == 0, "no instruction-level merge is needed");
            foreach (long id in new[] { GargoyleRelease, TaurusIntro })
            {
                EMEVD.Event written = t.Events.Single(e => e.ID == id);
                Check(EmevdMerge.SameEvent(written, c.Events.Single(e => e.ID == id)), $"event {id} differs from the combined form");
                Check(!EmevdMerge.SameEvent(written, b.Events.Single(e => e.ID == id)), $"event {id} still has the vanilla form");
                Check(merge.ExpectedEvents["m10_01_00_00.emevd.dcx"].Any(e => e.ID == id), $"event {id} is not an invariant-4 expectation");
            }
            // Fog's own logic stays authoritative: event 0 keeps the initializations of both events.
            Check(t.Events.Single(e => e.ID == 0).Instructions.Count(i => i.Bank == 2000 && i.ID == 0) == 2, "event 0 initializations changed");
        }

        private static void SecondGargoyleReleaseSemantics()
        {
            // The held-then-released structure of the legacy template: the body is switched off and invincible,
            // a wait on the first body's 60% health follows, and only then is it released and its bar shown.
            EMEVD.Event e = ReplacedGargoyleRelease();
            int Index(int bank, int id, int a0, int a1 = int.MinValue)
            {
                for (int i = 0; i < e.Instructions.Count; i++)
                {
                    EMEVD.Instruction ins = e.Instructions[i];
                    if (ins.Bank == bank && ins.ID == id && BitConverter.ToInt32(ins.ArgData, 0) == a0
                        && (a1 == int.MinValue || BitConverter.ToInt32(ins.ArgData, 4) == a1)) return i;
                }
                return -1;
            }
            int aiOff = Index(2004, 1, SecondGargoyle, 0), invOn = Index(2004, 15, SecondGargoyle, 1);
            int wait = Index(4, 2, 0, FirstGargoyle), invOff = Index(2004, 15, SecondGargoyle, 0), bar = Index(2003, 11, 1, SecondGargoyle);
            Check(aiOff >= 0 && aiOff < invOn && invOn < wait && wait < invOff && invOff < bar, "hold / wait / release order is wrong");
            Check(BitConverter.ToSingle(e.Instructions[wait].ArgData, 12) == 0.6f, "release threshold is not 60%");
            Check(e.Instructions[0].Bank == 1003 && e.Instructions[0].ID == 2, "the multiplayer-client guard (flag 3) was lost");
            // Every Gargoyle-specific animation instruction of the vanilla event is gone.
            Check(!e.Instructions.Any(i => i.Bank == 2004 && (i.ID == 9 || i.ID == 41)) && !e.Instructions.Any(i => i.Bank == 2003 && i.ID == 18),
                "model-specific instructions remain");
            // The Taurus event differs from vanilla only by the combined engine's removals, with its SKIP count adjusted.
            EMEVD.Event taurus = ReplacedTaurusIntro();
            Check(BitConverter.ToInt32(VanillaTaurusIntro().Instructions[0].ArgData, 0) == 0x00010002 && BitConverter.ToInt32(taurus.Instructions[0].ArgData, 0) == 0x00010001,
                "the Taurus SKIP range was not shortened with the removal inside it");
        }

        private static void SecondGargoyleNotInOverlay()
        {
            var expected = new HashSet<long> { 11005392, 11010000, 11210001, 11405392, 11415342, 11415373, 11415382, 11515392,
                11600001, 11700000, 11810000, 11810310, 11815382, 11815390, 11815394 };
            Check(BossOverlay.BossEvents.SetEquals(expected), "the 15-event boss overlay set changed");
            Check(!BossOverlay.BossEvents.Contains(GargoyleRelease) && !BossOverlay.BossEvents.Contains(TaurusIntro),
                "the Gargoyle events must not be in the boss overlay");
        }

        private static void SecondGargoyleDivergenceRefused()
        {
            // If FogMod ever stopped copying these events unchanged, the normal merge (and no overlay) must decide.
            var variants = new Dictionary<string, Action<EMEVD.Event>>
            {
                ["control flow: an unconditional end is added"] = e => e.Instructions.Insert(1, Ins(1000, 4, 0)),
                ["missing anchor: the release instruction is gone"] = e => e.Instructions.RemoveAt(e.Instructions.FindIndex(i => i.Bank == 2004 && i.ID == 1 && BitConverter.ToInt32(i.ArgData, 4) == 1)),
                ["duplicate anchor: the health condition appears twice"] = e => e.Instructions.Insert(4, Ins(4, 2, 0, FirstGargoyle, 5, 0x3F19999A)),
                ["changed parameter layout: a parameter is added"] = e => e.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 4)),
                ["changed threshold: another health ratio"] = e => e.Instructions[e.Instructions.FindIndex(i => i.Bank == 4 && i.ID == 2)] = Ins(4, 2, 0, FirstGargoyle, 5, 0x3F000000),
            };
            foreach (var (name, mutate) in variants)
            {
                var (b, c, t) = GargoyleTrio();
                mutate(t.Events.Single(e => e.ID == GargoyleRelease));
                try
                {
                    var merge = new EmevdMerge(new() { ["m10_01_00_00.emevd.dcx"] = (b, c) });
                    merge.Merge("m10_01_00_00.emevd.dcx", t);
                    merge.ThrowIfConflicts();
                }
                catch (FogHostException e) when (e.Code == "FOG_MERGE_CONFLICT")
                {
                    continue;
                }
                throw new Exception($"silently merged a divergent FogMod event ({name})");
            }
        }

        private static void SecondGargoyleUntouchedWhenOptionOff()
        {
            // "Do not replace" / no boss randomization: the combined engine leaves both events vanilla and FogMod
            // copies them back unchanged. Nothing to merge, nothing written.
            EMEVD b = Evd(Event0(), VanillaGargoyleRelease(), VanillaTaurusIntro());
            EMEVD c = Evd(Event0(), VanillaGargoyleRelease(), VanillaTaurusIntro());
            EMEVD t = Evd(Event0(), VanillaGargoyleRelease(), VanillaTaurusIntro());
            var merge = new EmevdMerge(new() { ["m10_01_00_00.emevd.dcx"] = (b, c) });
            merge.Merge("m10_01_00_00.emevd.dcx", t);
            merge.ThrowIfConflicts();
            Check(merge.ExpectedEvents["m10_01_00_00.emevd.dcx"].Count == 0, "unexpected expectations for untouched events");
            Check(EmevdMerge.SameEvent(t.Events.Single(e => e.ID == GargoyleRelease), VanillaGargoyleRelease()), "vanilla form changed");
        }

        /// <summary>Real FogMod run: both legacy forms written by the combined engine survive FogMod's own game-data pass.</summary>
        private static void SecondGargoyleSurvivesFogMod()
        {
            string dir = MakeStaging("second-gargoyle");
            string game = Path.Combine(dir, "game");
            string baseDir = Path.Combine(dir, "base");
            string target = Path.Combine(baseDir, EventPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string eventFile = Path.Combine(game, EventPath.Replace('/', Path.DirectorySeparatorChar));
            File.Copy(eventFile, target);

            EMEVD evd = EMEVD.Read(eventFile);
            EMEVD.Event release = evd.Events.Single(e => e.ID == GargoyleRelease);
            EMEVD.Event taurus = evd.Events.Single(e => e.ID == TaurusIntro);
            // FogMod's own DS1R events are the vanilla forms for these two events.
            Check(EmevdMerge.SameEvent(release, VanillaGargoyleRelease()) && EmevdMerge.SameEvent(taurus, VanillaTaurusIntro()),
                "FogMod's template for these events is no longer the vanilla form");
            release.Instructions = ReplacedGargoyleRelease().Instructions;
            taurus.Instructions = ReplacedTaurusIntro().Instructions;
            evd.Write(eventFile);

            JsonObject request = Request(dir, dataRoot);
            request["compatibility"]!["combinedFiles"] = new JsonArray(new JsonObject
            {
                ["path"] = EventPath,
                ["baseSha256"] = Sha(target),
                ["combinedSha256"] = Sha(eventFile),
            });
            ExpectOk(Run(dir, request));

            EMEVD written = EMEVD.Read(eventFile);
            Check(EmevdMerge.SameEvent(written.Events.Single(e => e.ID == GargoyleRelease), ReplacedGargoyleRelease()), "11015396 was changed by FogMod");
            Check(EmevdMerge.SameEvent(written.Events.Single(e => e.ID == TaurusIntro), ReplacedTaurusIntro()), "11015382 was changed by FogMod");
            Check(written.Events.First(e => e.ID == 0).Instructions.Count(i => i.Bank == 2000 && i.ID == 0
                && (BitConverter.ToInt32(i.ArgData, 4) == GargoyleRelease || BitConverter.ToInt32(i.ArgData, 4) == TaurusIntro)) == 2,
                "event-0 initializations of the two events were lost");
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
            // A randomized placement on a collision FogMod has no area annotation for (seen on real
            // data): FogMod's scaling pass must not crash on it.
            var annotated = new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(
                File.ReadAllText(Path.Combine(dataRoot, "dist", "fog.txt")), @"Col:\s*\S+\s+(\S+)").Select(m => m.Groups[1].Value));
            string unannotated = msb.Parts.Collisions.Select(c => c.Name).First(name => !annotated.Contains(name));
            msb.Parts.Enemies.First(e => e != enemy && e.NPCParamID > 1000 && e.CollisionName != null && e.ModelName != "c1000").CollisionName = unannotated;
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
