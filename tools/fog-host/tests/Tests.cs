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

        public static int Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            hostExe = Environment.GetEnvironmentVariable("FOGHOST_EXE") ?? Path.Combine(RepoRoot(), "tools", "fog-host", "bin", "Release", "net8.0", "FogHost.exe");
            dataRoot = Environment.GetEnvironmentVariable("DS1TR_FOG_DATA_ROOT");
            Directory.CreateDirectory(Scratch);
            Console.WriteLine($"FogHost: {hostExe}");
            Console.WriteLine($"Data root: {dataRoot ?? "(unset: data tests are skipped)"}");
            try
            {
                if (args.Contains("compact-candidate", StringComparer.Ordinal))
                {
                    Test("Compact Full Fog V1 candidate compiles the complete authored Warp pool and project rules", FogDsrFactsTest.VerifyCompactCatalogCandidateFromEnvironment);
                    Console.WriteLine($"Compact candidate checks: {passed} passed, {failed} failed.");
                    return failed == 0 ? 0 : 1;
                }
                if (args.Contains("compact-real-generation-smoke", StringComparer.Ordinal))
                {
                    try { return CompactRealGenerationTest.RunSmokeFromEnvironment(); }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine($"FAIL Compact real GameDataWriter generation: {error}");
                        return 1;
                    }
                }
                if (args.Contains("compact-real-generation-matrix", StringComparer.Ordinal))
                {
                    try { return CompactRealGenerationTest.RunMatrixFromEnvironment(); }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine($"FAIL Compact real GameDataWriter matrix: {error}");
                        return 1;
                    }
                }
                Test("usage error exits 2", UsageError);
                Test("unknown request key is FOG_REQUEST_INVALID", UnknownKey);
                Test("non-DS1R game is FOG_REQUEST_INVALID", WrongGame);
                Test("seed outside uint32 is FOG_REQUEST_INVALID", SeedRange);
                Test("missing option key is FOG_REQUEST_INVALID", MissingOption);
                Test("missing data root is FOG_DATA_MISSING", MissingDataRoot);
                Test("annotation fingerprint covers consumed semantics and preserves behavioral order", FogAnnotationFingerprintTest.Verify);
                Test("typed annotation catalog validates and projects the active FogMod DTO", FogAnnotationCatalogTest.VerifyProjection);
                Test("typed annotation catalog rejects unknown references", FogAnnotationCatalogTest.VerifyRejectsUnknownReferences);
                if (dataRoot != null) Test("verified DSR fact index reads map, spawn-point, ItemLotParam, NpcParam drop and object-act references", FogDsrFactsTest.Verify);
                Test("error classifier maps FogMod throw sites", Classifier);
                Test("invariant: a model declared as a Player model (c0000) stays valid; losing it is a violation", ModelDeclarationInvariant);
                Test("Event 5700 builder replaces exactly one body in place", Event5700Replacement);
                Test("Event 5700 builder refuses missing and duplicate source events", Event5700ReplacementRequiresUniqueSource);
                Test("Event 5700 replacement runs before merge conflict detection", Event5700MergeConflict);
                Test("Event 5690 replacement preserves its flag and map eligibility contract", Event5690Replacement);
                Test("Event 5690 replacement refuses missing and duplicate events", Event5690ReplacementRequiresUniqueSource);
                Test("common softlock, collision and random-start events preserve their call contracts", CommonUtilityEventsRebuilt);
                Test("modified common event 260 is reconstructed with exact parameter reindexing", CommonEvent260Reconstructed);
                Test("modified common event 840 is reconstructed with exact parameter reindexing", CommonEvent840Reconstructed);
                Test("Bed of Chaos helper events are rebuilt from semantic vanilla operations", BedOfChaosHelpersRebuilt);
                Test("Bed of Chaos helper builder refuses malformed source patterns", BedOfChaosHelpersRefuseMalformedSources);
                Test("known modified m11 events are reconstructed from named operations", KnownModifiedM11EventsReconstructed);
                Test("modified m11 event builder rejects unexpected source shapes", KnownModifiedM11EventsRejectUnexpectedSources);
                Test("M11 event 11100710 reconstructs its exact progression body", M11Event11100710BodyReconstructed);
                Test("known modified DLC, event 11210401 and Valley of Drakes events are reconstructed", KnownModifiedDlcAndDrakeEventsReconstructed);
                Test("known modified M13 DLC delay operation is removed idempotently", KnownModifiedM13DlcDelayRemoved);
                Test("M13 event 50 removes only its exact obsolete initializer calls", M13Event50InitializersRemoved);
                Test("M13 event 11305000 reconstructs its exact watcher and cleanup body", M13Event11305000BodyReconstructed);
                Test("known map event-50 initializer removals are exact and idempotent", KnownMapEventInitializersReconstructed);
                Test("M13 DLC transformations refuse duplicate or malformed source operations", KnownModifiedM13DlcDelayRejectsDuplicate);
                Test("known modified M15 DLC region and parameterized flag events are reconstructed", KnownModifiedKingFamilyEventReconstructed);
                Test("M15 event 11500210 reconstructs its exact progression watcher body", M15Event11500210BodyReconstructed);
                Test("known modified M15 DLC flag event refuses mismatched parameter metadata", KnownModifiedM15DlcFlagEventRejectsMalformedParameters);
                Test("M15 DLC event 11510001 removes only its exact three object/SFX pairs", M15DlcEvent11510001ObjectSfxSequenceRemoved);
                Test("M15 DLC event 11510543 removes only its exact sound-effect operations", M15DlcEvent11510543SfxSequenceRemoved);
                Test("M15 DLC event 11510900 reconstructs its exact timing and cleanup body", M15DlcEvent11510900BodyReconstructed);
                Test("known modified ending and DLC bonus events are reconstructed", KnownModifiedEndingEventsReconstructed);
                Test("known vanilla gate events are disabled with guarded unconditional ends", KnownVanillaGateEventsTerminated);
                Test("known vanilla object/SFX cleanup pairs are removed idempotently", KnownVanillaObjectSfxPairRemoved);
                Test("M17 event 11700000 replaces its exact first object/SFX pair and removes the second", M17Event11700000SfxOperationsRemoved);
                Test("M12 event 11200900 removes only its exact object/SFX sequence", M12Event11200900ObjectSfxSequenceRemoved);
                Test("M12 event 11200006 reconstructs its exact progression body", M12Event11200006BodyReconstructed);
                Test("known M10 gate condition sets are removed idempotently and malformed sets are refused", KnownM10GateConditionSetsRemoved);
                Test("M10 event 11005392 removes only its exact adjacent condition and skip block", M10Event11005392ConditionsRemoved);
                Test("M10 Parish event 11010000 removes only its exact adjacent condition and skip block", M10ParishEvent11010000ConditionsRemoved);
                Test("known M10 New Londo transition is reconstructed idempotently and malformed bodies are refused", KnownM10NewLondoTransitionReconstructed);
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
                DataTest("Event 5700 is integrated and deterministic across representative option profiles", Event5700ProfileMatrix);
                DataTest("small common event builders match reference operation semantics", CommonEventsMatchReferenceSemantics);
                DataTest("small added event builders match reference operation semantics", AddedEventsMatchReferenceSemantics);
                DataTest("No Fall Bed of Chaos emits semantically rebuilt helper events", BedOfChaosHelpersIntegrated);
                string pristineGameRoot = Environment.GetEnvironmentVariable("DS1TR_TEST_GAME_ROOT");
                if (!string.IsNullOrWhiteSpace(pristineGameRoot))
                    DataTest("No Fall Bed of Chaos derives helper events from a clean DSR archive", () => BedOfChaosHelpersUsePristineGame(pristineGameRoot));
                if (!string.IsNullOrWhiteSpace(pristineGameRoot) && !string.IsNullOrWhiteSpace(dataRoot))
                {
                    DataTest("common event 0 restores randomized-start initializers idempotently and rejects drift", () => CommonEvent0Reconstructed(pristineGameRoot));
                    DataTest("M10 Parish event 0 reconstructs its exact initializer, SFX and skip changes", () => M10ParishEvent0Reconstructed(pristineGameRoot));
                    DataTest("M10 event 11005150 reconstructs its exact skip and parameter semantics", () => M10Event11005150Reconstructed(pristineGameRoot));
                      DataTest("M15 DLC event 11515386 reconstructs its exact state and multiplayer changes", () => M15DlcEvent11515386Reconstructed(pristineGameRoot));
                      DataTest("M18 event 50 reconstructs its exact guard and initializer removals", () => M18Event50Reconstructed(pristineGameRoot));
                      DataTest("M10 New Londo event 50 reconstructs its exact flag and initializer changes", () => M10NewLondoEvent50Reconstructed(pristineGameRoot));
                      DataTest("M12 DLC event 11210839 rebuilds its multiplayer area flag watcher", () => M12DlcEvent11210839Reconstructed(pristineGameRoot));
                      DataTest("M12 DLC event 11210700 reconstructs its named PvP arena wiring", () => M12DlcEvent11210700Reconstructed(pristineGameRoot));
                      DataTest("M12 DLC event 11210875 reconstructs its exact multiplayer area watcher", () => M12DlcEvent11210875Reconstructed(pristineGameRoot));
                      DataTest("M12 DLC event 11210100 reconstructs its exact area state watcher", () => M12DlcEvent11210100Reconstructed(pristineGameRoot));
                      DataTest("M12 DLC event 11210830 reconstructs its exact area enemy watcher", () => M12DlcEvent11210830Reconstructed(pristineGameRoot));
                      DataTest("M12 DLC event 11210870 reconstructs its exact guidance body and parameters", () => M12DlcEvent11210870Reconstructed(pristineGameRoot));
                      DataTest("M12 DLC event 11210435 reconstructs its exact seven-state guidance body", () => M12DlcEvent11210435Reconstructed(pristineGameRoot));
                      DataTest("M12 event 0 reconstructs its exact object, SFX and skip edits", () => M12Event0Reconstructed(pristineGameRoot));
                      DataTest("M12 DLC event 0 reconstructs its exact SFX, skip and initializer edits", () => M12DlcEvent0Reconstructed(pristineGameRoot));
                      DataTest("M15 DLC event 0 reconstructs its exact object and initializer edits", () => M15DlcEvent0Reconstructed(pristineGameRoot));
                      DataTest("M14 DLC event 0 reconstructs its exact object, SFX and skip edits", () => M14DlcEvent0Reconstructed(pristineGameRoot));
                      DataTest("M13-2 event 0 removes only its exact omitted initializer sequence", () => M13PaintedWorldEvent0Reconstructed(pristineGameRoot));
                      DataTest("M13 event 0 reconstructs exact object and initializer edits", () => M13Event0Reconstructed(pristineGameRoot));
                      DataTest("M18 event 0 removes its exact multiplayer object and SFX setup", () => M18Event0Reconstructed(pristineGameRoot));
                      DataTest("M18 DLC event 0 reconstructs exact FogMod initializer edits", () => M18DlcEvent0Reconstructed(pristineGameRoot));
                      DataTest("M13 DLC event 0 reconstructs exact event and object/SFX edits", () => M13DlcEvent0Reconstructed(pristineGameRoot));
                      DataTest("M14 event 0 reconstructs exact object/SFX and initializer edits", () => M14Event0Reconstructed(pristineGameRoot));
                      DataTest("M16 event 0 reconstructs exact object/SFX and skip edits", () => M16Event0Reconstructed(pristineGameRoot));
                      DataTest("M17 event 0 reconstructs exact event, object/SFX and initializer edits", () => M17Event0Reconstructed(pristineGameRoot));
                      DataTest("M11 event 0 reconstructs its exact object and initializer changes", () => M11Event0Reconstructed(pristineGameRoot));
                      DataTest("M15 event 0 reconstructs exact object/SFX and initializer changes", () => M15Event0Reconstructed(pristineGameRoot));
                      DataTest("M10 event 0 reconstructs its exact multiplayer and initializer changes", () => M10Event0Reconstructed(pristineGameRoot));
                    DataTest("eight BossOverlay event bodies reconstruct idempotently and reject source drift", () => BossOverlayEventsReconstructed(pristineGameRoot));
                    DataTest("known modified event bodies reconstruct to pinned reference semantics from clean DSR", () => ModifiedEventsMatchReference(pristineGameRoot));
                }
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
            Check(response["schemaVersion"]!.GetValue<int>() == HostRequest.SchemaVersion, "schemaVersion");
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
                ["schemaVersion"] = HostRequest.SchemaVersion,
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
                ["annotationSource"] = HostRequest.LegacyFogModAnnotations,
                ["pristineGameDirectory"] = Path.GetTempPath(),
                ["expectedAnnotationFingerprint"] = "",
                ["compatibility"] = new JsonObject
                {
                    ["baseDirectory"] = Path.Combine(directory, "base"),
                    ["combinedFiles"] = new JsonArray(),
                    ["preservedNpcParams"] = new JsonArray(),
                },
            };
        }

        private static string NewDir(string name) => Path.Combine(Scratch, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        /// <summary>Builds staging from the configured clean game copy, falling back to FogMod's test fixture.</summary>
        private static string MakeStaging(string name)
        {
            string directory = NewDir(name);
            string game = Path.Combine(directory, "game");
            string pristineGameRoot = Environment.GetEnvironmentVariable("DS1TR_TEST_GAME_ROOT");
            string sourceRoot = string.IsNullOrWhiteSpace(pristineGameRoot)
                ? Path.Combine(dataRoot, "dist", "DS1R")
                : pristineGameRoot;
            foreach (string sub in new[] { "event", "map", "param", "msg", "script" })
                CopyTree(Path.Combine(sourceRoot, sub), Path.Combine(game, sub));
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

        private static EMEVD.Event TerminatedSourceEvent(long id, int instructionCount, EMEVD.Event.RestBehaviorType restBehavior)
        {
            var evt = new EMEVD.Event(id, restBehavior);
            for (int i = 0; i < instructionCount; i++)
                evt.Instructions.Add(new EMEVD.Instruction(2000, 0, new byte[] { (byte)i }));
            return evt;
        }

        private static EMEVD.Event ObjectSfxSourceEvent(long id, int instructionCount,
            EMEVD.Event.RestBehaviorType restBehavior, int objectEntity, byte objectState, int sfxEntity,
            bool spawnSfxInsteadOfDelete = false)
        {
            EMEVD.Event evt = TerminatedSourceEvent(id, instructionCount, restBehavior);
            int insertionIndex = instructionCount / 2;
            evt.Instructions[insertionIndex] = new EMEVD.Instruction(2005, 3, new object[] { objectEntity, objectState });
            evt.Instructions[insertionIndex + 1] = spawnSfxInsteadOfDelete
                ? new EMEVD.Instruction(2006, 2, new object[] { sfxEntity })
                : new EMEVD.Instruction(2006, 1, new object[] { sfxEntity, (byte)1 });
            return evt;
        }

        private static bool IsEndEvent(EMEVD.Instruction instruction) =>
            instruction.Bank == 1000 && instruction.ID == 4 && instruction.ArgData.SequenceEqual(new byte[4]);

        private static EMEVD.Instruction Ins(int bank, int id, params int[] args) =>
            new EMEVD.Instruction(bank, id, args.SelectMany(BitConverter.GetBytes).ToArray());

        private static EMEVD.Instruction InsHex(int bank, int id, string args) =>
            new EMEVD.Instruction(bank, id, Convert.FromHexString(args));

        private static bool ParametersMatch(IReadOnlyList<EMEVD.Parameter> actual, EMEVD.Parameter[] expected) =>
            actual.Count == expected.Length && actual.Zip(expected).All(pair =>
                pair.First.InstructionIndex == pair.Second.InstructionIndex
                && pair.First.TargetStartByte == pair.Second.TargetStartByte
                && pair.First.SourceStartByte == pair.Second.SourceStartByte
                && pair.First.ByteCount == pair.Second.ByteCount
                && pair.First.UnkID == pair.Second.UnkID);

        private static EMEVD Evd(params EMEVD.Event[] events)
        {
            var evd = new EMEVD { Format = EMEVD.Game.DarkSouls1 };
            evd.Events.AddRange(events);
            return evd;
        }

        private static EMEVD.Event Copy(EMEVD.Event e) =>
            Evt(e.ID, e.Instructions.Select(i => new EMEVD.Instruction(i.Bank, i.ID, i.ArgData.ToArray())).ToArray());

        private static void Event5700Replacement()
        {
            EMEVD.Event before = Evt(10, Ins(1000, 4, 0));
            EMEVD.Event original5700 = Evt(5700, Ins(1000, 4, 0));
            EMEVD.Event after = Evt(20, Ins(1000, 4, 1));
            EMEVD common = Evd(before, original5700, after);

            Event5700Builder.ReplaceExisting(common);

            Check(common.Events.Count == 3, "event count changed");
            Check(common.Events[0].ID == 10 && common.Events[1].ID == 5700 && common.Events[2].ID == 20, "event order or index changed");
            Check(EmevdMerge.SameEvent(before, common.Events[0]) && EmevdMerge.SameEvent(after, common.Events[2]), "a neighboring event changed");
            Check(EmevdMerge.SameEvent(Event5700Builder.Build(), common.Events[1]), "event 5700 is not the authored body");
        }

        private static void Event5700ReplacementRequiresUniqueSource()
        {
            bool missingRejected = false;
            try { Event5700Builder.ReplaceExisting(Evd(Evt(0, Ins(1000, 4, 0)))); }
            catch (InvalidDataException) { missingRejected = true; }
            Check(missingRejected, "missing event 5700 was accepted");

            bool duplicateRejected = false;
            try { Event5700Builder.ReplaceExisting(Evd(Evt(5700, Ins(1000, 4, 0)), Evt(5700, Ins(1000, 4, 1)))); }
            catch (InvalidDataException) { duplicateRejected = true; }
            Check(duplicateRejected, "duplicate event 5700 entries were accepted");
        }

        private static void Event5700MergeConflict()
        {
            EMEVD.Event baseZero = Evt(0, Ins(2000, 0, 0, 1, 0));
            EMEVD baseFile = Evd(baseZero);
            EMEVD combined = Evd(Copy(baseZero), Evt(5700, Ins(1000, 4, 99)));
            EMEVD fog = Evd(Copy(baseZero), Evt(5700, Ins(1000, 4, 0)));
            Event5700Builder.ReplaceExisting(fog);

            var merge = new EmevdMerge(new Dictionary<string, (EMEVD Base, EMEVD Combined)>
            {
                ["common.emevd.dcx"] = (baseFile, combined),
            });
            merge.Merge("common.emevd.dcx", fog);
            ExpectConflict(merge.ThrowIfConflicts);
        }

        private static void Event5690Replacement()
        {
            EMEVD.Event before = Evt(10, Ins(1000, 4, 0));
            EMEVD common = Evd(before, Evt(5690, Ins(1000, 4, 1)), Evt(5700, Ins(1000, 4, 0)));
            FogCommonEventBuilder.ReplaceEvent5690(common);
            EMEVD.Event evt = common.Events.Single(item => item.ID == 5690);
            Check(common.Events.Count == 3 && EmevdMerge.SameEvent(before, common.Events[0]), "replacing event 5690 changed unrelated event order or bodies");
            Check(evt.Instructions.Count == 5 && evt.Parameters.Count == 0, "event 5690 shape or parameter contract changed");
            Check(evt.Instructions[0].Bank == 1003 && evt.Instructions[0].ID == 2
                && evt.Instructions[1].Bank == 3 && evt.Instructions[1].ID == 0
                && evt.Instructions[2].Bank == 3 && evt.Instructions[2].ID == 8
                && evt.Instructions[3].Bank == 0 && evt.Instructions[3].ID == 0
                && evt.Instructions[4].Bank == 2003 && evt.Instructions[4].ID == 2, "event 5690 no longer guards and latches its target flag");
            Check(BitConverter.ToInt32(evt.Instructions[1].ArgData, 4) == 0x00B4353E
                && evt.Instructions[2].ArgData[1] == 0 && evt.Instructions[2].ArgData[2] == 18 && evt.Instructions[2].ArgData[3] == 1,
                "event 5690 source flag or outside-map condition changed");
        }

        private static void Event5690ReplacementRequiresUniqueSource()
        {
            bool missingRejected = false;
            try { FogCommonEventBuilder.ReplaceEvent5690(Evd(Evt(0, Ins(1000, 4, 0)))); }
            catch (InvalidDataException) { missingRejected = true; }
            Check(missingRejected, "missing event 5690 was accepted");

            bool duplicateRejected = false;
            try { FogCommonEventBuilder.ReplaceEvent5690(Evd(Evt(5690, Ins(1000, 4, 0)), Evt(5690, Ins(1000, 4, 1)))); }
            catch (InvalidDataException) { duplicateRejected = true; }
            Check(duplicateRejected, "duplicate event 5690 entries were accepted");
        }

        private static void CommonUtilityEventsRebuilt()
        {
            EMEVD common = Evd(Evt(5690), Evt(8900), Evt(8901), Evt(8950), Evt(5700));
            FogCommonEventBuilder.ReplaceAuthoredCommonEvents(common);

            Check(EmevdMerge.SameEvent(FogCommonEventBuilder.BuildEvent5690(), common.Events.Single(evt => evt.ID == 5690)), "event 5690 differs from its builder");
            EMEVD.Event softlockWarp = common.Events.Single(evt => evt.ID == 8900);
            Check(softlockWarp.Instructions.Count == 4 && softlockWarp.Parameters.Count == 3
                && softlockWarp.Instructions[0].Bank == 3 && softlockWarp.Instructions[0].ID == 0
                && BitConverter.ToInt32(softlockWarp.Instructions[0].ArgData, 4) == 5300,
                "event 8900 no longer consumes the system warp flag and transitions to the passed destination");
            Check(softlockWarp.Parameters.OrderBy(p => p.TargetStartByte).Select(p => p.SourceStartByte).SequenceEqual(new long[] { 0, 1, 4 }),
                "event 8900 payload bindings changed");

            EMEVD.Event collisionFlag = common.Events.Single(evt => evt.ID == 8901);
            Check(collisionFlag.Instructions.Count == 8 && collisionFlag.Parameters.Count == 5
                && BitConverter.ToInt32(collisionFlag.Instructions[2].ArgData, 0) == 10
                && collisionFlag.Instructions[6].Bank == 0 && collisionFlag.Instructions[6].ID == 0,
                "event 8901 no longer delays and waits for one of its completion flags");
            Check(collisionFlag.Parameters.Any(p => p.InstructionIndex == 0 && p.SourceStartByte == 0)
                && collisionFlag.Parameters.Any(p => p.InstructionIndex == 1 && p.SourceStartByte == 4)
                && collisionFlag.Parameters.Any(p => p.InstructionIndex == 3 && p.SourceStartByte == 0),
                "event 8901 does not bind collision and boss flags from its initializer");

            EMEVD.Event randomStart = common.Events.Single(evt => evt.ID == 8950);
            Check(randomStart.Instructions.Count == 8 && randomStart.Parameters.Count == 4
                && BitConverter.ToSingle(randomStart.Instructions[0].ArgData, 0) == 0.5f
                && randomStart.Instructions[3].Bank == 2003 && randomStart.Instructions[3].ID == 23
                && randomStart.Instructions[6].Bank == 2000 && randomStart.Instructions[6].ID == 5,
                "event 8950 no longer sets the spawn point and saves before the startup warp");
            Check(randomStart.Parameters.Any(p => p.InstructionIndex == 3 && p.SourceStartByte == 8)
                && randomStart.Parameters.Any(p => p.InstructionIndex == 7 && p.SourceStartByte == 0)
                && randomStart.Parameters.Any(p => p.InstructionIndex == 7 && p.SourceStartByte == 4),
                "event 8950 initializer payload bindings changed");
        }

        private static void CommonEvent260Reconstructed()
        {
            EMEVD.Event evt = CommonEvent260SourceEvent();
            EMEVD archive = Evd(evt, CommonEvent840SourceEvent());
            FogModifiedEventBuilder.ReconstructKnownCommonEvents(archive);
            Check(evt.Instructions.Count == 4
                && evt.Instructions[0].Bank == 1003 && evt.Instructions[0].ID == 2
                && evt.Instructions[1].Bank == 3 && evt.Instructions[1].ID == 0
                && evt.Instructions[2].Bank == 1001 && evt.Instructions[2].ID == 0
                && evt.Instructions[3].Bank == 2007 && evt.Instructions[3].ID == 3,
                "common event 260 did not remove its exact flag condition");
            Check(evt.Parameters.Count == 4
                && evt.Parameters[0].InstructionIndex == 0 && evt.Parameters[0].TargetStartByte == 4
                    && evt.Parameters[0].SourceStartByte == 0
                && evt.Parameters[1].InstructionIndex == 1 && evt.Parameters[1].TargetStartByte == 4
                    && evt.Parameters[1].SourceStartByte == 0
                && evt.Parameters[2].InstructionIndex == 3 && evt.Parameters[2].TargetStartByte == 0
                    && evt.Parameters[2].SourceStartByte == 4
                && evt.Parameters[3].InstructionIndex == 2 && evt.Parameters[3].TargetStartByte == 0
                    && evt.Parameters[3].SourceStartByte == 8,
                "common event 260 parameter bindings were not reindexed");
            EMEVD.Instruction[] body = evt.Instructions.ToArray();
            var parameters = evt.Parameters.Select(parameter => (parameter.InstructionIndex, parameter.TargetStartByte,
                parameter.SourceStartByte, parameter.ByteCount, parameter.UnkID)).ToArray();
            FogModifiedEventBuilder.ReconstructKnownCommonEvents(archive);
            Check(evt.Instructions.Count == body.Length
                && evt.Instructions.Zip(body).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && evt.Parameters.Select(parameter => (parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount, parameter.UnkID)).SequenceEqual(parameters),
                "common event 260 reconstruction is not idempotent");

            EMEVD.Event malformed = CommonEvent260SourceEvent();
            malformed.Parameters[3] = new EMEVD.Parameter(2, 0, 8, 4);
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownCommonEvents(Evd(malformed, CommonEvent840SourceEvent())); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == 5
                && malformed.Instructions[2].Bank == 1003 && malformed.Instructions[2].ID == 1
                && malformed.Parameters[3].InstructionIndex == 2,
                "common event 260 accepted or mutated a mismatched source parameter table");
        }

        private static EMEVD.Event CommonEvent260SourceEvent()
        {
            EMEVD.Event evt = Evt(260,
                InsHex(1003, 2, "0001000000000000"),
                InsHex(3, 0, "0001000000000000"),
                InsHex(1003, 1, "02010000A1230000"),
                InsHex(1001, 0, "00000000"),
                InsHex(2007, 3, "0000000001000000"));
            evt.Parameters.Add(new EMEVD.Parameter(0, 4, 0, 4));
            evt.Parameters.Add(new EMEVD.Parameter(1, 4, 0, 4));
            evt.Parameters.Add(new EMEVD.Parameter(4, 0, 4, 4));
            evt.Parameters.Add(new EMEVD.Parameter(3, 0, 8, 4));
            return evt;
        }

        private static void CommonEvent840Reconstructed()
        {
            EMEVD.Event evt = CommonEvent840SourceEvent();
            EMEVD archive = Evd(CommonEvent260SourceEvent(), evt);
            FogModifiedEventBuilder.ReconstructKnownCommonEvents(archive);
            EMEVD.Instruction[] expectedBody = CommonEvent840ExpectedBody();
            EMEVD.Parameter[] expectedParameters = CommonEvent840ExpectedParameters();
            Check(evt.Instructions.Count == expectedBody.Length
                && evt.Instructions.Zip(expectedBody).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(evt.Parameters, expectedParameters),
                "common event 840 did not match its exact reconstructed body and parameter table");

            EMEVD.Instruction[] body = evt.Instructions.ToArray();
            EMEVD.Parameter[] parameters = evt.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            FogModifiedEventBuilder.ReconstructKnownCommonEvents(archive);
            Check(evt.Instructions.Count == body.Length
                && evt.Instructions.Zip(body).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(evt.Parameters, parameters),
                "common event 840 reconstruction is not idempotent");

            EMEVD.Event malformed = CommonEvent840SourceEvent();
            malformed.Instructions[2] = InsHex(1003, 1, "010100004C030000");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            EMEVD.Parameter[] malformedParameters = malformed.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownCommonEvents(Evd(CommonEvent260SourceEvent(), malformed)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected
                && malformed.Instructions.Zip(malformedBody).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformed.Parameters, malformedParameters),
                "common event 840 accepted or mutated an unexpected source body");
        }

        private static EMEVD.Event CommonEvent840SourceEvent()
        {
            EMEVD.Event evt = Evt(840,
                InsHex(2003, 2, "0000000000000000"),
                InsHex(3, 0, "0001000000000000"),
                InsHex(1003, 1, "030100004C030000"),
                InsHex(1003, 1, "020100004F030000"),
                InsHex(2004, 14, "1027000000000000"),
                InsHex(2003, 18, "10270000FFFFFFFF00000000"),
                InsHex(1003, 1, "0901000048030000"),
                InsHex(1003, 1, "0801000049030000"),
                InsHex(1003, 1, "070100004A030000"),
                InsHex(1003, 1, "060100004B030000"),
                InsHex(1003, 1, "050100004D030000"),
                InsHex(1003, 1, "040100004E030000"),
                InsHex(1003, 1, "0301000050030000"),
                InsHex(1003, 1, "0201000051030000"),
                InsHex(1003, 1, "010100005C030000"),
                InsHex(2003, 18, "10270000FFFFFFFF00000100"),
                InsHex(1001, 0, "0000803F"),
                InsHex(2010, 2, "102700000500000015CD5B07"),
                InsHex(1001, 0, "00008040"),
                InsHex(1000, 5, "0100000000000000FFFFFFFF"),
                InsHex(2003, 18, "10270000FFFFFFFF01000000"),
                InsHex(1000, 4, "01000000"));
            evt.Parameters.AddRange(new[]
            {
                new EMEVD.Parameter(0, 0, 0, 4),
                new EMEVD.Parameter(1, 4, 0, 4),
                new EMEVD.Parameter(5, 4, 4, 4),
                new EMEVD.Parameter(15, 4, 4, 4),
                new EMEVD.Parameter(4, 4, 8, 4),
                new EMEVD.Parameter(19, 4, 12, 4),
                new EMEVD.Parameter(20, 4, 12, 4),
            });
            return evt;
        }

        private static EMEVD.Instruction[] CommonEvent840ExpectedBody() => new[]
        {
            InsHex(2003, 2, "0000000000000000"),
            InsHex(3, 0, "0001000000000000"),
            InsHex(1003, 1, "020100004C030000"),
            InsHex(1003, 1, "010100004F030000"),
            InsHex(2004, 14, "1027000000000000"),
            InsHex(2003, 18, "10270000FFFFFFFF00000000"),
            InsHex(1001, 0, "0000803F"),
            InsHex(2010, 2, "102700000500000015CD5B07"),
            InsHex(1001, 0, "00008040"),
            InsHex(1000, 5, "0100000000000000FFFFFFFF"),
            InsHex(2003, 18, "10270000FFFFFFFF01000000"),
            InsHex(1000, 4, "01000000"),
        };

        private static EMEVD.Parameter[] CommonEvent840ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(0, 0, 0, 4),
            new EMEVD.Parameter(1, 4, 0, 4),
            new EMEVD.Parameter(5, 4, 4, 4),
            new EMEVD.Parameter(4, 4, 8, 4),
            new EMEVD.Parameter(9, 4, 12, 4),
            new EMEVD.Parameter(10, 4, 12, 4),
        };

        private static void BedOfChaosHelpersRebuilt()
        {
            var floor = Evt(11410200,
                Ins(1000, 4, 0),
                Ins(2005, 2, 101), Ins(2005, 2, 102), Ins(2005, 2, 103), Ins(2005, 2, 104), Ins(2005, 2, 105),
                Ins(2005, 13, 101, 1), Ins(2005, 13, 102, 1), Ins(2005, 13, 103, 1), Ins(2005, 13, 104, 1), Ins(2005, 13, 105, 1),
                Ins(1000, 4, 0));
            var fall = Evt(11410201, Ins(2005, 2, 909), Ins(2005, 13, 909, 1), Ins(1000, 4, 0));
            fall.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 4) { UnkID = 7 });
            fall.Parameters.Add(new EMEVD.Parameter(1, 0, 4, 4) { UnkID = 8 });
            EMEVD pristine = Evd(floor, fall);
            EMEVD output = Evd(Evt(1, Ins(1000, 4, 0)), Evt(11410220, Ins(1000, 4, 1)));

            FogVanillaEventBuilder.ReplaceBedOfChaosHelpers(pristine, output);

            EMEVD.Event floorHelper = output.Events.Single(e => e.ID == 11410220);
            EMEVD.Event fallHelper = output.Events.Single(e => e.ID == 11410221);
            Check(output.Events.Count == 3 && output.Events[0].ID == 1, "helper replacement changed unrelated events or order");
            Check(floorHelper.Instructions.Count == 10 && floorHelper.Instructions.All(i => i.Bank == 2005), "floor helper contains instructions outside its semantic setup");
            Check(floorHelper.Instructions.Take(5).All(i => i.ID == 2) && floorHelper.Instructions.Skip(5).All(i => i.ID == 13), "floor helper order is incorrect");
            Check(fallHelper.Instructions.Count == 2 && fallHelper.Parameters.Count == 2, "fall helper instructions or parameter records were not copied");
            Check(fallHelper.Parameters[0].InstructionIndex == 0 && fallHelper.Parameters[1].InstructionIndex == 1
                && fallHelper.Parameters[0].UnkID == 7 && fallHelper.Parameters[1].UnkID == 8, "fall helper parameter indices or metadata were not preserved");
        }

        private static void BedOfChaosHelpersRefuseMalformedSources()
        {
            var duplicateFloor = Evt(11410200, Ins(2005, 2, 1), Ins(2005, 2, 1), Ins(2005, 2, 3), Ins(2005, 2, 4), Ins(2005, 2, 5),
                Ins(2005, 13, 1, 1), Ins(2005, 13, 2, 1), Ins(2005, 13, 3, 1), Ins(2005, 13, 4, 1), Ins(2005, 13, 5, 1));
            var fall = Evt(11410201, Ins(2005, 2, 9), Ins(2005, 13, 9, 1));
            bool missingRejected = false;
            try { FogVanillaEventBuilder.ReplaceBedOfChaosHelpers(Evd(duplicateFloor, fall), Evd()); }
            catch (InvalidDataException) { missingRejected = true; }
            Check(missingRejected, "duplicate floor object ids were accepted");

            var validFloor = Evt(11410200,
                Ins(2005, 2, 1), Ins(2005, 2, 2), Ins(2005, 2, 3), Ins(2005, 2, 4), Ins(2005, 2, 5),
                Ins(2005, 13, 1, 1), Ins(2005, 13, 2, 1), Ins(2005, 13, 3, 1), Ins(2005, 13, 4, 1), Ins(2005, 13, 5, 1));
            bool missingFallRejected = false;
            try { FogVanillaEventBuilder.ReplaceBedOfChaosHelpers(Evd(validFloor, Evt(11410201, Ins(1000, 4, 0))), Evd()); }
            catch (InvalidDataException) { missingFallRejected = true; }
            Check(missingFallRejected, "missing fall-object setup was accepted");
        }

        private static void KnownModifiedM11EventsReconstructed()
        {
            EMEVD.Event dragon = new EMEVD.Event(11100400, EMEVD.Event.RestBehaviorType.Restart);
            for (int i = 0; i < 10; i++) dragon.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            dragon.Instructions[7] = new EMEVD.Instruction(2004, 10, new object[] { 0x0010C98A, (byte)0 });

            EMEVD.Event arena = new EMEVD.Event(11105399, EMEVD.Event.RestBehaviorType.Default);
            for (int i = 0; i < 15; i++) arena.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            arena.Instructions[2] = new EMEVD.Instruction(3, 8, new object[] { (sbyte)0, (byte)1, (byte)15, (byte)1 });
            arena.Instructions[3] = new EMEVD.Instruction(3, 8, new object[] { (sbyte)0, (byte)1, (byte)11, (byte)0 });
            EMEVD.Event stoppedGate = TerminatedSourceEvent(11105390, 6, EMEVD.Event.RestBehaviorType.Default);
            EMEVD.Event objectSfxEvent = ObjectSfxSourceEvent(11100000, 10, EMEVD.Event.RestBehaviorType.Default,
                0x0010D0A6, 0, 0x0010D0A7);
            EMEVD.Event mapInitializer = M11MapInitializerSourceEvent();
            EMEVD.Event parishProgression = M11Event11100710SourceEvent();

            EMEVD archive = Evd(dragon, arena, stoppedGate, objectSfxEvent, mapInitializer, parishProgression);
            FogModifiedEventBuilder.ReconstructKnownM11Events(archive);

            Check(dragon.Instructions[7].Bank == 2004 && dragon.Instructions[7].ID == 10
                && BitConverter.ToInt32(dragon.Instructions[7].ArgData, 0) == 0x0010C98A
                && dragon.Instructions[7].ArgData[4] == 1, "undead dragon gravity was not enabled");
            Check(arena.Instructions[2].Bank == 3 && arena.Instructions[2].ID == 8
                && arena.Instructions[2].ArgData.SequenceEqual(new byte[] { 0, 0, 11, 0 }),
                "boss arena event reset map guard was not rewritten");
            Check(IsEndEvent(stoppedGate.Instructions[0]), "vanilla gate event was not stopped at its entry point");
            Check(objectSfxEvent.Instructions.Count == 8
                && !objectSfxEvent.Instructions.Any(instruction => instruction.Bank == 2005 && instruction.ID == 3
                    && instruction.ArgData.SequenceEqual(Convert.FromHexString("A6D0100000000000")))
                && !objectSfxEvent.Instructions.Any(instruction => instruction.Bank == 2006 && instruction.ID == 1
                    && instruction.ArgData.SequenceEqual(Convert.FromHexString("A7D0100001000000"))),
                "Undead Burg cleanup object/SFX pair was not removed");
            Check(mapInitializer.Instructions.Count == 18
                && !mapInitializer.Instructions.Any(instruction => instruction.Bank == 2000 && instruction.ID == 0
                    && instruction.ArgData.SequenceEqual(Convert.FromHexString("000000000673A90000000000"))),
                "M11 event 50 did not remove the exact reference-removed event initializer");
            Check(parishProgression.Instructions.Count == 14
                && parishProgression.Instructions[11].Bank == 2003 && parishProgression.Instructions[11].ID == 2
                && parishProgression.Instructions[11].ArgData.SequenceEqual(Convert.FromHexString("B614000001000000")),
                "M11 event 11100710 did not restore the reference progression flag operation");

            byte[] transformedDragon = dragon.Instructions[7].ArgData.ToArray();
            byte[] transformedArena = arena.Instructions[2].ArgData.ToArray();
            int transformedGateCount = stoppedGate.Instructions.Count;
            EMEVD.Instruction[] transformedObjectSfx = objectSfxEvent.Instructions.ToArray();
            EMEVD.Instruction[] transformedMapInitializer = mapInitializer.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM11Events(archive);
            Check(dragon.Instructions[7].ArgData.SequenceEqual(transformedDragon)
                && arena.Instructions[2].ArgData.SequenceEqual(transformedArena)
                && stoppedGate.Instructions.Count == transformedGateCount
                && objectSfxEvent.Instructions.Count == transformedObjectSfx.Length
                && objectSfxEvent.Instructions.Zip(transformedObjectSfx)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && mapInitializer.Instructions.Count == transformedMapInitializer.Length
                && mapInitializer.Instructions.Zip(transformedMapInitializer)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "reconstruction is not idempotent");
            Check(parishProgression.Instructions.Count == 14,
                "M11 event 11100710 reconstruction is not idempotent");
        }

        private static void M11Event11100710BodyReconstructed()
        {
            EMEVD.Event source = M11Event11100710SourceEvent();
            EMEVD.Instruction[] expectedBody = source.Instructions.Take(11)
                .Concat(new[] { InsHex(2003, 2, "B614000001000000"), source.Instructions[14], source.Instructions[19] }).ToArray();
            EMEVD archive = M11SourceArchive(source);

            FogModifiedEventBuilder.ReconstructKnownM11Events(archive);
            Check(source.Instructions.Count == expectedBody.Length && source.Instructions.Zip(expectedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M11 event 11100710 differs from its exact reference body");
            EMEVD.Instruction[] reconstructedBody = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM11Events(archive);
            Check(source.Instructions.Count == reconstructedBody.Length && source.Instructions.Zip(reconstructedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M11 event 11100710 reconstruction is not idempotent");

            EMEVD.Event malformed = M11Event11100710SourceEvent();
            malformed.Instructions[5] = InsHex(2004, 12, "80C9100000000000");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM11Events(M11SourceArchive(malformed)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M11 event 11100710 was accepted or partially mutated");
        }

        private static EMEVD M11SourceArchive(EMEVD.Event progressionEvent)
        {
            var dragon = new EMEVD.Event(11100400, EMEVD.Event.RestBehaviorType.Restart);
            for (int i = 0; i < 10; i++) dragon.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            dragon.Instructions[7] = new EMEVD.Instruction(2004, 10, new object[] { 0x0010C98A, (byte)0 });
            var arena = new EMEVD.Event(11105399, EMEVD.Event.RestBehaviorType.Default);
            for (int i = 0; i < 15; i++) arena.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            arena.Instructions[2] = new EMEVD.Instruction(3, 8, new object[] { (sbyte)0, (byte)1, (byte)15, (byte)1 });
            arena.Instructions[3] = new EMEVD.Instruction(3, 8, new object[] { (sbyte)0, (byte)1, (byte)11, (byte)0 });
            return Evd(dragon, arena,
                TerminatedSourceEvent(11105390, 6, EMEVD.Event.RestBehaviorType.Default),
                ObjectSfxSourceEvent(11100000, 10, EMEVD.Event.RestBehaviorType.Default, 0x0010D0A6, 0, 0x0010D0A7),
                M11MapInitializerSourceEvent(), progressionEvent);
        }

        private static EMEVD.Event M11Event11100710SourceEvent() => Evt(11100710,
            InsHex(2003, 2, "6474A90000000000"),
            InsHex(3, 6, "01000000"),
            InsHex(3, 0, "010000009B060000"),
            InsHex(3, 2, "0101000010270000A4D21000"),
            InsHex(0, 0, "00010100"),
            InsHex(2004, 12, "80C9100001000000"),
            InsHex(1003, 6, "00010000"),
            InsHex(2004, 12, "80C9100000000000"),
            InsHex(2003, 2, "6474A90001000000"),
            InsHex(1003, 1, "0101000004000000"),
            InsHex(3, 0, "000100007374A900"),
            InsHex(1003, 1, "0100000080A2AF00"),
            InsHex(2003, 19, "0F000200"),
            InsHex(2002, 4, "D3AD010000000000341417000F01000010270000"),
            InsHex(1001, 1, "01000000"),
            InsHex(1003, 1, "0101010000000000"),
            InsHex(2003, 36, "46230000"),
            InsHex(2003, 23, "00161700"),
            InsHex(2000, 5, "00000000"),
            InsHex(1000, 4, "01000000"));

        private static void KnownModifiedM11EventsRejectUnexpectedSources()
        {
            EMEVD.Event dragon = new EMEVD.Event(11100400, EMEVD.Event.RestBehaviorType.Restart);
            for (int i = 0; i < 10; i++) dragon.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            dragon.Instructions[7] = new EMEVD.Instruction(2004, 10, new object[] { 0x0010C98A, (byte)2 });
            EMEVD.Event arena = new EMEVD.Event(11105399, EMEVD.Event.RestBehaviorType.Default);
            for (int i = 0; i < 15; i++) arena.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            arena.Instructions[2] = new EMEVD.Instruction(3, 8, new object[] { (sbyte)0, (byte)1, (byte)15, (byte)1 });
            arena.Instructions[3] = new EMEVD.Instruction(3, 8, new object[] { (sbyte)0, (byte)1, (byte)11, (byte)0 });

            bool invalidGravityRejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM11Events(Evd(dragon, arena)); }
            catch (InvalidDataException) { invalidGravityRejected = true; }
            Check(invalidGravityRejected, "unsupported gravity value was accepted");

            bool missingEventRejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM11Events(Evd(dragon)); }
            catch (InvalidDataException) { missingEventRejected = true; }
            Check(missingEventRejected, "missing modified event was accepted");
        }

        private static void KnownModifiedDlcAndDrakeEventsReconstructed()
        {
            EMEVD.Event[] whiteCatEvents = new long[] { 11210340, 11210341, 11210345 }
                .Select(id =>
                {
                    var evt = new EMEVD.Event(id, EMEVD.Event.RestBehaviorType.Default);
                    for (int i = 0; i < 13; i++) evt.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
                    evt.Instructions[9] = new EMEVD.Instruction(2003, 46,
                        new object[] { 0x00001A68, 0x00001B5B, (byte)0, (byte)1, (byte)0, 5.0f });
                    return evt;
                })
                .ToArray();
            var drake = new EMEVD.Event(11600400, EMEVD.Event.RestBehaviorType.Restart);
            for (int i = 0; i < 10; i++) drake.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            drake.Instructions[0] = new EMEVD.Instruction(2004, 31, new object[] { 0x00186BF4, (byte)1 });
            drake.Instructions[1] = new EMEVD.Instruction(2004, 10, new object[] { 0x00186BF4, (byte)0 });

            EMEVD.Event[] dlcStoppedGates = new[]
            {
                TerminatedSourceEvent(11215000, 6, EMEVD.Event.RestBehaviorType.Restart),
                TerminatedSourceEvent(11215010, 6, EMEVD.Event.RestBehaviorType.Restart),
                TerminatedSourceEvent(11215060, 7, EMEVD.Event.RestBehaviorType.Restart),
                TerminatedSourceEvent(11215020, 6, EMEVD.Event.RestBehaviorType.Restart),
            };
            EMEVD.Event m12SharedObjectSfx = M12DlcSharedObjectSfxSourceEvent();
            EMEVD.Instruction[] m12SharedUnaffectedBody = m12SharedObjectSfx.Instructions.Take(12).ToArray();
            EMEVD.Event[] m12ObjectSfxEvents =
            {
                m12SharedObjectSfx,
                ObjectSfxSourceEvent(11210002, 21, EMEVD.Event.RestBehaviorType.Default, 0x00127E56, 0, 0x00127E57),
                ObjectSfxSourceEvent(11210005, 12, EMEVD.Event.RestBehaviorType.Default, 0x00127D2A, 0, 0x00127D2B),
                ObjectSfxSourceEvent(11210535, 18, EMEVD.Event.RestBehaviorType.Default, 0x00127D2A, 1, 0x00127D2B, true),
            };
            EMEVD.Event m12DlcEvent11210401 = M12DlcEvent11210401SourceEvent();
            EMEVD.Event m12DlcEvent11210404 = M12DlcEvent11210404SourceEvent();
            EMEVD.Event m12DlcEvent11210430 = M12DlcEvent11210430SourceEvent();
            EMEVD.Event m12DlcEvent11210701 = M12DlcEvent11210701SourceEvent();
            EMEVD.Parameter[] sourceM12DlcEvent11210404Parameters = m12DlcEvent11210404.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event m12DlcEvent11210434 = M12DlcEvent11210434SourceEvent();
            EMEVD.Event m12DlcEvent11210840 = M12DlcEvent11210840SourceEvent();
            EMEVD.Event m12DlcEvent11210850 = M12DlcEvent11210850SourceEvent();
            EMEVD.Event m12DlcEvent11210800 = M12DlcEvent11210800SourceEvent();
            EMEVD.Event m12DlcEvent11210890 = M12DlcEvent11210890SourceEvent();
            EMEVD.Event m12DlcEvent11210831 = M12DlcEvent11210831SourceEvent();
            EMEVD.Parameter[] sourceM12DlcEvent11210831Parameters = m12DlcEvent11210831.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event m12DlcEvent11210849 = M12DlcEvent11210849SourceEvent();
            EMEVD.Event m12DlcEvent11210835 = M12DlcEvent11210835SourceEvent();
            EMEVD.Parameter[] sourceM12DlcEvent11210835Parameters = m12DlcEvent11210835.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event m12DlcEvent11210900 = M12DlcEvent11210900SourceEvent();
            EMEVD.Event m12DlcEvent11210836 = M12DlcEvent11210836SourceEvent();
            EMEVD.Instruction[] sourceM12DlcEvent11210836 = m12DlcEvent11210836.Instructions.ToArray();
            EMEVD.Event m12DlcEvent11210837 = M12DlcEvent11210837SourceEvent();
            EMEVD.Instruction[] sourceM12DlcEvent11210837 = m12DlcEvent11210837.Instructions.ToArray();
            EMEVD.Event m12DlcEvent11210886 = M12DlcEvent11210886SourceEvent();
            EMEVD.Instruction[] sourceM12DlcEvent11210886 = m12DlcEvent11210886.Instructions.ToArray();
            EMEVD.Parameter[] sourceM12DlcEvent11210886Parameters = m12DlcEvent11210886.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            string[] stateWatcher11210110Anchors =
            {
                "0101000010270000CF7E1200", "0201000010270000CE7E1200",
                "0301000010270000D07E1200", "0401000010270000D17E1200",
            };
            string[] stateWatcher11210140Anchors =
            {
                "0101000010270000ED7E1200", "0201000010270000EC7E1200",
                "0301000010270000EE7E1200", "0401000010270000EF7E1200",
            };
            string[] stateWatcher11210120Anchors =
            {
                "0101000010270000D97E1200", "0201000010270000D87E1200",
                "0301000010270000DA7E1200", "0401000010270000DB7E1200",
            };
            string[] stateWatcher11210130Anchors =
            {
                "0101000010270000E37E1200", "0201000010270000E27E1200",
                "0301000010270000E47E1200", "0401000010270000E57E1200",
            };
            string[] stateWatcher11210110Removed = { "000000007521AB00" };
            string[] stateWatcher11210140Removed = { "000000007821AB00" };
            string[] stateWatcher11210120Removed = { "000100008B0DAB00", "000000007621AB00" };
            string[] stateWatcher11210130Removed = { "00010000950DAB00", "000000007721AB00" };
            string[][] stateWatcher11210110Conditions = M12DlcOneFlagStateConditionGroups("7521AB00");
            string[][] stateWatcher11210140Conditions = M12DlcOneFlagStateConditionGroups("7821AB00");
            string[][] stateWatcher11210120Conditions = M12DlcTwoFlagStateConditionGroups("8B0DAB00", "7621AB00");
            string[][] stateWatcher11210130Conditions = M12DlcTwoFlagStateConditionGroups("950DAB00", "7721AB00");
            EMEVD.Event m12DlcStateWatcher11210110 = M12DlcStateWatcherSourceEvent(
                11210110, 68, stateWatcher11210110Removed, stateWatcher11210110Anchors);
            EMEVD.Event m12DlcStateWatcher11210120 = M12DlcStateWatcherSourceEvent(
                11210120, 72, stateWatcher11210120Removed, stateWatcher11210120Anchors);
            EMEVD.Event m12DlcStateWatcher11210130 = M12DlcStateWatcherSourceEvent(
                11210130, 72, stateWatcher11210130Removed, stateWatcher11210130Anchors);
            EMEVD.Event m12DlcStateWatcher11210140 = M12DlcStateWatcherSourceEvent(
                11210140, 69, stateWatcher11210140Removed, stateWatcher11210140Anchors);
            EMEVD.Instruction[] sourceStateWatcher11210110 = m12DlcStateWatcher11210110.Instructions.ToArray();
            EMEVD.Instruction[] sourceStateWatcher11210120 = m12DlcStateWatcher11210120.Instructions.ToArray();
            EMEVD.Instruction[] sourceStateWatcher11210130 = m12DlcStateWatcher11210130.Instructions.ToArray();
            EMEVD.Instruction[] sourceStateWatcher11210140 = m12DlcStateWatcher11210140.Instructions.ToArray();
            EMEVD.Event[] dlcMapGuards =
            {
                M12DlcMapGuardSourceEvent(11215007),
                M12DlcMapGuardSourceEvent(11215009),
            };
            EMEVD.Event m12AreaConditionCleanup = M12DlcAreaConditionCleanupSourceEvent();
            EMEVD.Instruction[] expectedM12AreaConditions = m12AreaConditionCleanup.Instructions
                .Where((_, index) => index < 7 || index > 10).ToArray();
            EMEVD.Event m12AreaConditionCleanup2 = M12DlcAreaConditionCleanupSourceEvent2();
            EMEVD.Instruction[] expectedM12AreaConditions2 = m12AreaConditionCleanup2.Instructions
                .Where((_, index) => index < 7 || index > 10).ToArray();
            EMEVD.Event m12AreaConditionCleanup3 = M12DlcAreaConditionCleanupSourceEvent3();
            EMEVD.Instruction[] expectedM12AreaConditions3 = m12AreaConditionCleanup3.Instructions
                .Where((_, index) => index < 40 || index > 43).ToArray();
            EMEVD.Event m12AreaConditionCleanup4 = M12DlcAreaConditionCleanupSourceEvent4();
            EMEVD.Instruction[] expectedM12AreaConditions4 = m12AreaConditionCleanup4.Instructions
                .Where((_, index) => index < 5 || index > 8).ToArray();
            EMEVD.Event m12AreaConditionCleanup5 = M12DlcAreaConditionCleanupSourceEvent5();
            EMEVD.Instruction[] expectedM12AreaConditions5 = m12AreaConditionCleanup5.Instructions
                .Where((_, index) => index < 7 || index > 10).ToArray();
            EMEVD m12DlcArchive = Evd(whiteCatEvents.Concat(dlcStoppedGates)
                .Concat(m12ObjectSfxEvents).Concat(dlcMapGuards).Append(m12AreaConditionCleanup)
                .Append(m12AreaConditionCleanup2).Append(m12AreaConditionCleanup3)
                .Append(m12AreaConditionCleanup4).Append(m12AreaConditionCleanup5)
                .Append(m12DlcEvent11210401).Append(m12DlcEvent11210404)
                .Append(m12DlcEvent11210430).Append(m12DlcEvent11210701)
                .Append(m12DlcEvent11210434).Append(m12DlcEvent11210840)
                .Append(m12DlcEvent11210850).Append(m12DlcEvent11210800)
                .Append(m12DlcEvent11210890)
                .Append(m12DlcEvent11210831).Append(m12DlcEvent11210835)
                .Append(m12DlcEvent11210849).Append(m12DlcEvent11210900).Append(m12DlcEvent11210836)
                .Append(m12DlcEvent11210837).Append(m12DlcEvent11210886)
                .Append(m12DlcStateWatcher11210110).Append(m12DlcStateWatcher11210120)
                .Append(m12DlcStateWatcher11210130).Append(m12DlcStateWatcher11210140).ToArray());
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(m12DlcArchive);
            foreach (EMEVD.Event evt in whiteCatEvents)
            {
                EMEVD.Instruction animation = evt.Instructions.Single(instruction => instruction.Bank == 2003 && instruction.ID == 18);
                Check(animation.ArgData.Length == 12 && BitConverter.ToInt32(animation.ArgData, 0) == 0x00001A68
                    && BitConverter.ToInt32(animation.ArgData, 4) == 0x00001B5B,
                    "White Cat guidance did not use the ordinary ForceAnimationPlayback operation");
            }
            Check(dlcStoppedGates.Where(evt => evt.ID != 11215020).All(evt => IsEndEvent(evt.Instructions[0])),
                "DLC vanilla gate events were not stopped");
            CheckPrefix(dlcStoppedGates.Single(evt => evt.ID == 11215020),
                new[] { WaitForArea(0x00128244), EndInstruction() }, "Painted World gate-area watcher");
            foreach (EMEVD.Event mapGuard in dlcMapGuards)
            {
                Check(mapGuard.Instructions.Count > 1
                    && mapGuard.Instructions[0].Bank == 3 && mapGuard.Instructions[0].ID == 0
                    && mapGuard.Instructions[0].ArgData.SequenceEqual(Convert.FromHexString("00010000100DAB00"))
                    && mapGuard.Instructions[1].Bank == 3 && mapGuard.Instructions[1].ID == 0
                    && mapGuard.Instructions[1].ArgData.SequenceEqual(Convert.FromHexString("00010000110DAB00")),
                    "M12 DLC event " + mapGuard.ID + " did not prepend the additional map guard");
            }
            Check(m12SharedObjectSfx.Instructions.Count == 12
                && m12SharedObjectSfx.Instructions.Take(12).Zip(m12SharedUnaffectedBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210000 changed operations beyond its exact object/SFX sequence");
            Check(m12AreaConditionCleanup.Instructions.Count == 9
                && m12AreaConditionCleanup.Instructions.Zip(expectedM12AreaConditions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210838 changed operations beyond its exact condition removal");
            Check(m12AreaConditionCleanup2.Instructions.Count == 9
                && m12AreaConditionCleanup2.Instructions.Zip(expectedM12AreaConditions2)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210439 changed operations beyond its exact condition removal");
            Check(m12AreaConditionCleanup3.Instructions.Count == 115
                && m12AreaConditionCleanup3.Instructions.Zip(expectedM12AreaConditions3)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210407 changed operations beyond its exact condition removal");
            Check(m12AreaConditionCleanup4.Instructions.Count == 14
                && m12AreaConditionCleanup4.Instructions.Zip(expectedM12AreaConditions4)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210878 changed operations beyond its exact condition removal");
            Check(m12AreaConditionCleanup5.Instructions.Count == 15
                && m12AreaConditionCleanup5.Instructions.Zip(expectedM12AreaConditions5)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210402 changed operations beyond its exact condition removal");
            EMEVD.Instruction[] expectedM12DlcEvent11210401 = M12DlcEvent11210401ExpectedBody();
            Check(m12DlcEvent11210401.Instructions.Count == expectedM12DlcEvent11210401.Length
                && m12DlcEvent11210401.Instructions.Zip(expectedM12DlcEvent11210401)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210401 did not match its exact reconstructed body");
            EMEVD.Instruction[] expectedM12DlcEvent11210404 = M12DlcEvent11210404ExpectedBody();
            EMEVD.Parameter[] expectedM12DlcEvent11210404Parameters = M12DlcEvent11210404ExpectedParameters();
            Check(m12DlcEvent11210404.Instructions.Count == expectedM12DlcEvent11210404.Length
                && m12DlcEvent11210404.Instructions.Zip(expectedM12DlcEvent11210404)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210404.Parameters, expectedM12DlcEvent11210404Parameters),
                "M12 DLC event 11210404 did not match its exact reconstructed body and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210430 = M12DlcEvent11210430ExpectedBody();
            EMEVD.Parameter[] expectedM12DlcEvent11210430Parameters = M12DlcEvent11210430ExpectedParameters();
            Check(m12DlcEvent11210430.Instructions.Count == expectedM12DlcEvent11210430.Length
                && m12DlcEvent11210430.Instructions.Zip(expectedM12DlcEvent11210430)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210430.Parameters, expectedM12DlcEvent11210430Parameters),
                "M12 DLC event 11210430 did not match its exact reconstructed body and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210701 = M12DlcEvent11210701ExpectedBody();
            EMEVD.Parameter[] expectedM12DlcEvent11210701Parameters = M12DlcEvent11210701ExpectedParameters();
            Check(m12DlcEvent11210701.Instructions.Count == expectedM12DlcEvent11210701.Length
                && m12DlcEvent11210701.Instructions.Zip(expectedM12DlcEvent11210701)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210701.Parameters, expectedM12DlcEvent11210701Parameters),
                "M12 DLC event 11210701 did not match its exact reconstructed body and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210434 = M12DlcEvent11210434ExpectedBody();
            Check(m12DlcEvent11210434.Instructions.Count == expectedM12DlcEvent11210434.Length
                && m12DlcEvent11210434.Instructions.Zip(expectedM12DlcEvent11210434)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210434 did not match its exact reconstructed body");
            EMEVD.Instruction[] expectedM12DlcEvent11210840 = M12DlcEvent11210840ExpectedBody();
            EMEVD.Parameter[] expectedM12DlcEvent11210840Parameters = M12DlcEvent11210840ExpectedParameters();
            Check(m12DlcEvent11210840.Instructions.Count == expectedM12DlcEvent11210840.Length
                && m12DlcEvent11210840.Instructions.Zip(expectedM12DlcEvent11210840)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210840.Parameters, expectedM12DlcEvent11210840Parameters),
                "M12 DLC event 11210840 did not match its exact reconstructed body and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210850 = M12DlcEvent11210850ExpectedBody();
            EMEVD.Parameter[] expectedM12DlcEvent11210850Parameters = M12DlcEvent11210850ExpectedParameters();
            Check(m12DlcEvent11210850.Instructions.Count == expectedM12DlcEvent11210850.Length
                && m12DlcEvent11210850.Instructions.Zip(expectedM12DlcEvent11210850)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210850.Parameters, expectedM12DlcEvent11210850Parameters),
                "M12 DLC event 11210850 did not match its exact reconstructed body and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210800 = M12DlcEvent11210800ExpectedBody();
            EMEVD.Parameter[] expectedM12DlcEvent11210800Parameters = M12DlcEvent11210800ExpectedParameters();
            Check(m12DlcEvent11210800.Instructions.Count == expectedM12DlcEvent11210800.Length
                && m12DlcEvent11210800.Instructions.Zip(expectedM12DlcEvent11210800)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210800.Parameters, expectedM12DlcEvent11210800Parameters),
                "M12 DLC event 11210800 did not match its exact reconstructed body and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210890 = M12DlcEvent11210890ExpectedBody();
            EMEVD.Parameter[] expectedM12DlcEvent11210890Parameters = M12DlcEvent11210890ExpectedParameters();
            Check(m12DlcEvent11210890.Instructions.Count == expectedM12DlcEvent11210890.Length
                && m12DlcEvent11210890.Instructions.Zip(expectedM12DlcEvent11210890)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210890.Parameters, expectedM12DlcEvent11210890Parameters),
                "M12 DLC event 11210890 did not match its exact reconstructed body and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210831 = M12DlcEvent11210831ExpectedBody();
            EMEVD.Parameter[] expectedM12DlcEvent11210831Parameters = M12DlcEvent11210831ExpectedParameters();
            Check(m12DlcEvent11210831.Instructions.Count == expectedM12DlcEvent11210831.Length
                && m12DlcEvent11210831.Instructions.Zip(expectedM12DlcEvent11210831)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210831.Parameters, expectedM12DlcEvent11210831Parameters),
                "M12 DLC event 11210831 did not match its exact reconstructed body and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210849 = M12DlcEvent11210849ExpectedBody();
            Check(m12DlcEvent11210849.Instructions.Count == expectedM12DlcEvent11210849.Length
                && m12DlcEvent11210849.Instructions.Zip(expectedM12DlcEvent11210849)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210849 did not match its exact reconstructed body");
            EMEVD.Instruction[] expectedM12DlcEvent11210835 = M12DlcEvent11210835ExpectedBody();
            EMEVD.Parameter[] expectedM12DlcEvent11210835Parameters = M12DlcEvent11210835ExpectedParameters();
            Check(m12DlcEvent11210835.Instructions.Count == expectedM12DlcEvent11210835.Length
                && m12DlcEvent11210835.Instructions.Zip(expectedM12DlcEvent11210835)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210835.Parameters, expectedM12DlcEvent11210835Parameters),
                "M12 DLC event 11210835 did not match its exact reconstructed body and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210900 = M12DlcEvent11210900ExpectedBody();
            Check(m12DlcEvent11210900.Instructions.Count == expectedM12DlcEvent11210900.Length
                && m12DlcEvent11210900.Instructions.Zip(expectedM12DlcEvent11210900)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210900.Parameters, new[] { new EMEVD.Parameter(1, 4, 0, 4) }),
                "M12 DLC event 11210900 did not match its exact reconstruction and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210836 = new[]
            {
                sourceM12DlcEvent11210836[0], sourceM12DlcEvent11210836[1],
                InsHex(1003, 1, "030100002322AB00"), InsHex(1003, 1, "020100002022AB00"),
                InsHex(2007, 4, "634A020000000000"), InsHex(1000, 3, "01000000"),
                InsHex(2007, 4, "594A020000000000"), sourceM12DlcEvent11210836[3],
                sourceM12DlcEvent11210836[4], sourceM12DlcEvent11210836[5],
                sourceM12DlcEvent11210836[6], sourceM12DlcEvent11210836[7],
            };
            Check(m12DlcEvent11210836.Instructions.Count == expectedM12DlcEvent11210836.Length
                && m12DlcEvent11210836.Instructions.Zip(expectedM12DlcEvent11210836)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210836 did not match its exact reconstructed body");
            CheckM12DlcStateWatcher(m12DlcStateWatcher11210110, sourceStateWatcher11210110,
                stateWatcher11210110Removed, stateWatcher11210110Anchors, stateWatcher11210110Conditions);
            CheckM12DlcStateWatcher(m12DlcStateWatcher11210120, sourceStateWatcher11210120,
                stateWatcher11210120Removed, stateWatcher11210120Anchors, stateWatcher11210120Conditions);
            CheckM12DlcStateWatcher(m12DlcStateWatcher11210130, sourceStateWatcher11210130,
                stateWatcher11210130Removed, stateWatcher11210130Anchors, stateWatcher11210130Conditions);
            CheckM12DlcStateWatcher(m12DlcStateWatcher11210140, sourceStateWatcher11210140,
                stateWatcher11210140Removed, stateWatcher11210140Anchors, stateWatcher11210140Conditions);
            EMEVD.Instruction[] expectedM12DlcEvent11210886 = M12DlcEvent11210886ExpectedBody(sourceM12DlcEvent11210886);
            EMEVD.Parameter[] expectedM12DlcEvent11210886Parameters = M12DlcEvent11210886ExpectedParameters();
            Check(m12DlcEvent11210886.Instructions.Count == expectedM12DlcEvent11210886.Length
                && m12DlcEvent11210886.Instructions.Zip(expectedM12DlcEvent11210886)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210886.Parameters, expectedM12DlcEvent11210886Parameters),
                "M12 DLC event 11210886 did not match its exact reconstructed body and parameter table");
            EMEVD.Instruction[] expectedM12DlcEvent11210837 = M12DlcEvent11210837ExpectedBody();
            Check(m12DlcEvent11210837.Instructions.Count == expectedM12DlcEvent11210837.Length
                && m12DlcEvent11210837.Instructions.Zip(expectedM12DlcEvent11210837)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210837 did not match its exact reconstructed body");
            EMEVD.Instruction[] reconstructedM12DlcEvent11210886 = m12DlcEvent11210886.Instructions.ToArray();
            EMEVD.Parameter[] reconstructedM12DlcEvent11210886Parameters = m12DlcEvent11210886.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event malformedM12AreaCleanup = M12DlcAreaConditionCleanupSourceEvent();
            malformedM12AreaCleanup.Instructions[7] = InsHex(3, 2, "FF010000102700007B811200");
            EMEVD.Instruction[] malformedM12AreaCleanupBody = malformedM12AreaCleanup.Instructions.ToArray();
            bool malformedM12AreaCleanupRejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformedM12AreaCleanup)); }
            catch (InvalidDataException) { malformedM12AreaCleanupRejected = true; }
            Check(malformedM12AreaCleanupRejected && malformedM12AreaCleanup.Instructions.Zip(malformedM12AreaCleanupBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M12 DLC event 11210838 was accepted or mutated");
            EMEVD.Event malformedM12AreaCleanup5 = M12DlcAreaConditionCleanupSourceEvent5();
            malformedM12AreaCleanup5.Instructions[9] = InsHex(3, 2, "FF010000102700007B811200");
            EMEVD.Instruction[] malformedM12AreaCleanup5Body = malformedM12AreaCleanup5.Instructions.ToArray();
            bool malformedM12AreaCleanup5Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformedM12AreaCleanup5)); }
            catch (InvalidDataException) { malformedM12AreaCleanup5Rejected = true; }
            Check(malformedM12AreaCleanup5Rejected && malformedM12AreaCleanup5.Instructions.Zip(malformedM12AreaCleanup5Body)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M12 DLC event 11210402 was accepted or mutated");
            EMEVD.Instruction[][] reconstructedMapGuardBodies = dlcMapGuards
                .Select(evt => evt.Instructions.ToArray()).ToArray();
            EMEVD.Instruction[] reconstructedM12SharedBody = m12SharedObjectSfx.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12AreaConditions = m12AreaConditionCleanup.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12AreaConditions2 = m12AreaConditionCleanup2.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12AreaConditions3 = m12AreaConditionCleanup3.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12AreaConditions4 = m12AreaConditionCleanup4.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12AreaConditions5 = m12AreaConditionCleanup5.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210401 = m12DlcEvent11210401.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210404 = m12DlcEvent11210404.Instructions.ToArray();
            EMEVD.Parameter[] reconstructedM12DlcEvent11210404Parameters = m12DlcEvent11210404.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210430 = m12DlcEvent11210430.Instructions.ToArray();
            EMEVD.Parameter[] reconstructedM12DlcEvent11210430Parameters = m12DlcEvent11210430.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210701 = m12DlcEvent11210701.Instructions.ToArray();
            EMEVD.Parameter[] reconstructedM12DlcEvent11210701Parameters = m12DlcEvent11210701.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210434 = m12DlcEvent11210434.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210840 = m12DlcEvent11210840.Instructions.ToArray();
            EMEVD.Parameter[] reconstructedM12DlcEvent11210840Parameters = m12DlcEvent11210840.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210850 = m12DlcEvent11210850.Instructions.ToArray();
            EMEVD.Parameter[] reconstructedM12DlcEvent11210850Parameters = m12DlcEvent11210850.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210800 = m12DlcEvent11210800.Instructions.ToArray();
            EMEVD.Parameter[] reconstructedM12DlcEvent11210800Parameters = m12DlcEvent11210800.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210890 = m12DlcEvent11210890.Instructions.ToArray();
            EMEVD.Parameter[] reconstructedM12DlcEvent11210890Parameters = m12DlcEvent11210890.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210831 = m12DlcEvent11210831.Instructions.ToArray();
            EMEVD.Parameter[] reconstructedM12DlcEvent11210831Parameters = m12DlcEvent11210831.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210849 = m12DlcEvent11210849.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210835 = m12DlcEvent11210835.Instructions.ToArray();
            EMEVD.Parameter[] reconstructedM12DlcEvent11210835Parameters = m12DlcEvent11210835.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210900 = m12DlcEvent11210900.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210836 = m12DlcEvent11210836.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedM12DlcEvent11210837 = m12DlcEvent11210837.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedStateWatcher11210110 = m12DlcStateWatcher11210110.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedStateWatcher11210120 = m12DlcStateWatcher11210120.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedStateWatcher11210130 = m12DlcStateWatcher11210130.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedStateWatcher11210140 = m12DlcStateWatcher11210140.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(m12DlcArchive);
            Check(dlcMapGuards.Zip(reconstructedMapGuardBodies)
                .All(pair => pair.First.Instructions.Count == pair.Second.Length
                    && pair.First.Instructions.Zip(pair.Second)
                        .All(instructionPair => EmevdMerge.SameInstruction(instructionPair.First, instructionPair.Second))),
                "M12 DLC map-guard reconstruction is not idempotent");
            Check(m12SharedObjectSfx.Instructions.Count == reconstructedM12SharedBody.Length
                && m12SharedObjectSfx.Instructions.Zip(reconstructedM12SharedBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210000 reconstruction is not idempotent");
            Check(m12AreaConditionCleanup.Instructions.Count == reconstructedM12AreaConditions.Length
                && m12AreaConditionCleanup.Instructions.Zip(reconstructedM12AreaConditions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210838 reconstruction is not idempotent");
            Check(m12AreaConditionCleanup2.Instructions.Count == reconstructedM12AreaConditions2.Length
                && m12AreaConditionCleanup2.Instructions.Zip(reconstructedM12AreaConditions2)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210439 reconstruction is not idempotent");
            Check(m12AreaConditionCleanup3.Instructions.Count == reconstructedM12AreaConditions3.Length
                && m12AreaConditionCleanup3.Instructions.Zip(reconstructedM12AreaConditions3)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210407 reconstruction is not idempotent");
            Check(m12AreaConditionCleanup4.Instructions.Count == reconstructedM12AreaConditions4.Length
                && m12AreaConditionCleanup4.Instructions.Zip(reconstructedM12AreaConditions4)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210878 reconstruction is not idempotent");
            Check(m12AreaConditionCleanup5.Instructions.Count == reconstructedM12AreaConditions5.Length
                && m12AreaConditionCleanup5.Instructions.Zip(reconstructedM12AreaConditions5)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210402 reconstruction is not idempotent");
            Check(m12DlcEvent11210401.Instructions.Count == reconstructedM12DlcEvent11210401.Length
                && m12DlcEvent11210401.Instructions.Zip(reconstructedM12DlcEvent11210401)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210401 reconstruction is not idempotent");
            Check(m12DlcEvent11210404.Instructions.Count == reconstructedM12DlcEvent11210404.Length
                && m12DlcEvent11210404.Instructions.Zip(reconstructedM12DlcEvent11210404)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210404.Parameters, reconstructedM12DlcEvent11210404Parameters),
                "M12 DLC event 11210404 reconstruction is not idempotent");
            Check(m12DlcEvent11210430.Instructions.Count == reconstructedM12DlcEvent11210430.Length
                && m12DlcEvent11210430.Instructions.Zip(reconstructedM12DlcEvent11210430)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210430.Parameters, reconstructedM12DlcEvent11210430Parameters),
                "M12 DLC event 11210430 reconstruction is not idempotent");
            Check(m12DlcEvent11210701.Instructions.Count == reconstructedM12DlcEvent11210701.Length
                && m12DlcEvent11210701.Instructions.Zip(reconstructedM12DlcEvent11210701)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210701.Parameters, reconstructedM12DlcEvent11210701Parameters),
                "M12 DLC event 11210701 reconstruction is not idempotent");
            Check(m12DlcEvent11210434.Instructions.Count == reconstructedM12DlcEvent11210434.Length
                && m12DlcEvent11210434.Instructions.Zip(reconstructedM12DlcEvent11210434)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210434 reconstruction is not idempotent");
            Check(m12DlcEvent11210840.Instructions.Count == reconstructedM12DlcEvent11210840.Length
                && m12DlcEvent11210840.Instructions.Zip(reconstructedM12DlcEvent11210840)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210840.Parameters, reconstructedM12DlcEvent11210840Parameters),
                "M12 DLC event 11210840 reconstruction is not idempotent");
            Check(m12DlcEvent11210850.Instructions.Count == reconstructedM12DlcEvent11210850.Length
                && m12DlcEvent11210850.Instructions.Zip(reconstructedM12DlcEvent11210850)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210850.Parameters, reconstructedM12DlcEvent11210850Parameters),
                "M12 DLC event 11210850 reconstruction is not idempotent");
            Check(m12DlcEvent11210800.Instructions.Count == reconstructedM12DlcEvent11210800.Length
                && m12DlcEvent11210800.Instructions.Zip(reconstructedM12DlcEvent11210800)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210800.Parameters, reconstructedM12DlcEvent11210800Parameters),
                "M12 DLC event 11210800 reconstruction is not idempotent");
            Check(m12DlcEvent11210890.Instructions.Count == reconstructedM12DlcEvent11210890.Length
                && m12DlcEvent11210890.Instructions.Zip(reconstructedM12DlcEvent11210890)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210890.Parameters, reconstructedM12DlcEvent11210890Parameters),
                "M12 DLC event 11210890 reconstruction is not idempotent");
            Check(m12DlcEvent11210831.Instructions.Count == reconstructedM12DlcEvent11210831.Length
                && m12DlcEvent11210831.Instructions.Zip(reconstructedM12DlcEvent11210831)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210831.Parameters, reconstructedM12DlcEvent11210831Parameters),
                "M12 DLC event 11210831 reconstruction is not idempotent");
            Check(m12DlcEvent11210849.Instructions.Count == reconstructedM12DlcEvent11210849.Length
                && m12DlcEvent11210849.Instructions.Zip(reconstructedM12DlcEvent11210849)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210849 reconstruction is not idempotent");
            Check(m12DlcEvent11210835.Instructions.Count == reconstructedM12DlcEvent11210835.Length
                && m12DlcEvent11210835.Instructions.Zip(reconstructedM12DlcEvent11210835)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210835.Parameters, reconstructedM12DlcEvent11210835Parameters),
                "M12 DLC event 11210835 reconstruction is not idempotent");
            Check(m12DlcEvent11210900.Instructions.Count == reconstructedM12DlcEvent11210900.Length
                && m12DlcEvent11210900.Instructions.Zip(reconstructedM12DlcEvent11210900)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210900 reconstruction is not idempotent");
            Check(m12DlcEvent11210836.Instructions.Count == reconstructedM12DlcEvent11210836.Length
                && m12DlcEvent11210836.Instructions.Zip(reconstructedM12DlcEvent11210836)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210836 reconstruction is not idempotent");
            Check(m12DlcEvent11210837.Instructions.Count == reconstructedM12DlcEvent11210837.Length
                && m12DlcEvent11210837.Instructions.Zip(reconstructedM12DlcEvent11210837)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210837 reconstruction is not idempotent");
            Check(m12DlcEvent11210886.Instructions.Count == reconstructedM12DlcEvent11210886.Length
                && m12DlcEvent11210886.Instructions.Zip(reconstructedM12DlcEvent11210886)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(m12DlcEvent11210886.Parameters, reconstructedM12DlcEvent11210886Parameters),
                "M12 DLC event 11210886 reconstruction is not idempotent");
            Check(m12DlcStateWatcher11210110.Instructions.Count == reconstructedStateWatcher11210110.Length
                && m12DlcStateWatcher11210110.Instructions.Zip(reconstructedStateWatcher11210110)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210110 reconstruction is not idempotent");
            Check(m12DlcStateWatcher11210120.Instructions.Count == reconstructedStateWatcher11210120.Length
                && m12DlcStateWatcher11210120.Instructions.Zip(reconstructedStateWatcher11210120)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210120 reconstruction is not idempotent");
            Check(m12DlcStateWatcher11210130.Instructions.Count == reconstructedStateWatcher11210130.Length
                && m12DlcStateWatcher11210130.Instructions.Zip(reconstructedStateWatcher11210130)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210130 reconstruction is not idempotent");
            Check(m12DlcStateWatcher11210140.Instructions.Count == reconstructedStateWatcher11210140.Length
                && m12DlcStateWatcher11210140.Instructions.Zip(reconstructedStateWatcher11210140)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210140 reconstruction is not idempotent");
            AssertMalformedM12DlcStateWatcherIsRejected(m12DlcArchive, 11210110,
                68, stateWatcher11210110Removed, stateWatcher11210110Anchors);
            AssertMalformedM12DlcStateWatcherIsRejected(m12DlcArchive, 11210120,
                72, stateWatcher11210120Removed, stateWatcher11210120Anchors);
            AssertMalformedM12DlcStateWatcherIsRejected(m12DlcArchive, 11210130,
                72, stateWatcher11210130Removed, stateWatcher11210130Anchors);
            AssertMalformedM12DlcStateWatcherIsRejected(m12DlcArchive, 11210140,
                69, stateWatcher11210140Removed, stateWatcher11210140Anchors);
            EMEVD.Event malformedM12DlcEvent11210837 = M12DlcEvent11210837SourceEvent();
            malformedM12DlcEvent11210837.Instructions[0] = InsHex(3, 0, "02010000D11F0000");
            EMEVD.Instruction[] malformedM12DlcEvent11210837Body = malformedM12DlcEvent11210837.Instructions.ToArray();
            EMEVD.Event[] malformed11210837Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210837)
                .Append(malformedM12DlcEvent11210837).ToArray();
            bool malformedM12DlcEvent11210837Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210837Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210837Rejected = true; }
            Check(malformedM12DlcEvent11210837Rejected
                && malformedM12DlcEvent11210837.Instructions.Zip(malformedM12DlcEvent11210837Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M12 DLC event 11210837 was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210831 = M12DlcEvent11210831SourceEvent();
            malformedM12DlcEvent11210831.Parameters[0] = new EMEVD.Parameter(2, 0, 0, 4);
            EMEVD.Instruction[] malformedM12DlcEvent11210831Body = malformedM12DlcEvent11210831.Instructions.ToArray();
            EMEVD.Parameter[] malformedM12DlcEvent11210831Parameters = malformedM12DlcEvent11210831.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event[] malformed11210831Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210831)
                .Append(malformedM12DlcEvent11210831).ToArray();
            bool malformedM12DlcEvent11210831Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210831Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210831Rejected = true; }
            Check(malformedM12DlcEvent11210831Rejected
                && malformedM12DlcEvent11210831.Instructions.Zip(malformedM12DlcEvent11210831Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformedM12DlcEvent11210831.Parameters, malformedM12DlcEvent11210831Parameters),
                "malformed M12 DLC event 11210831 parameter metadata was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210404 = M12DlcEvent11210404SourceEvent();
            malformedM12DlcEvent11210404.Parameters[0] = new EMEVD.Parameter(5, 0, 0, 4);
            EMEVD.Instruction[] malformedM12DlcEvent11210404Body = malformedM12DlcEvent11210404.Instructions.ToArray();
            EMEVD.Parameter[] malformedM12DlcEvent11210404Parameters = malformedM12DlcEvent11210404.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event[] malformed11210404Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210404)
                .Append(malformedM12DlcEvent11210404).ToArray();
            bool malformedM12DlcEvent11210404Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210404Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210404Rejected = true; }
            Check(malformedM12DlcEvent11210404Rejected
                && malformedM12DlcEvent11210404.Instructions.Zip(malformedM12DlcEvent11210404Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformedM12DlcEvent11210404.Parameters, malformedM12DlcEvent11210404Parameters),
                "malformed M12 DLC event 11210404 parameter metadata was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210430 = M12DlcEvent11210430SourceEvent();
            malformedM12DlcEvent11210430.Parameters[0] = new EMEVD.Parameter(6, 0, 0, 4);
            EMEVD.Instruction[] malformedM12DlcEvent11210430Body = malformedM12DlcEvent11210430.Instructions.ToArray();
            EMEVD.Parameter[] malformedM12DlcEvent11210430Parameters = malformedM12DlcEvent11210430.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event[] malformed11210430Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210430)
                .Append(malformedM12DlcEvent11210430).ToArray();
            bool malformedM12DlcEvent11210430Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210430Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210430Rejected = true; }
            Check(malformedM12DlcEvent11210430Rejected
                && malformedM12DlcEvent11210430.Instructions.Zip(malformedM12DlcEvent11210430Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformedM12DlcEvent11210430.Parameters, malformedM12DlcEvent11210430Parameters),
                "malformed M12 DLC event 11210430 parameter metadata was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210701 = M12DlcEvent11210701SourceEvent();
            malformedM12DlcEvent11210701.Parameters[0] = new EMEVD.Parameter(6, 0, 0, 4);
            EMEVD.Instruction[] malformedM12DlcEvent11210701Body = malformedM12DlcEvent11210701.Instructions.ToArray();
            EMEVD.Parameter[] malformedM12DlcEvent11210701Parameters = malformedM12DlcEvent11210701.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event[] malformed11210701Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210701)
                .Append(malformedM12DlcEvent11210701).ToArray();
            bool malformedM12DlcEvent11210701Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210701Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210701Rejected = true; }
            Check(malformedM12DlcEvent11210701Rejected
                && malformedM12DlcEvent11210701.Instructions.Zip(malformedM12DlcEvent11210701Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformedM12DlcEvent11210701.Parameters, malformedM12DlcEvent11210701Parameters),
                "malformed M12 DLC event 11210701 parameter metadata was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210434 = M12DlcEvent11210434SourceEvent();
            malformedM12DlcEvent11210434.Instructions[16] = InsHex(3, 0, "030100001F22AB00");
            EMEVD.Instruction[] malformedM12DlcEvent11210434Body = malformedM12DlcEvent11210434.Instructions.ToArray();
            EMEVD.Event[] malformed11210434Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210434)
                .Append(malformedM12DlcEvent11210434).ToArray();
            bool malformedM12DlcEvent11210434Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210434Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210434Rejected = true; }
            Check(malformedM12DlcEvent11210434Rejected
                && malformedM12DlcEvent11210434.Instructions.Zip(malformedM12DlcEvent11210434Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M12 DLC event 11210434 was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210840 = M12DlcEvent11210840SourceEvent();
            malformedM12DlcEvent11210840.Parameters[0] = new EMEVD.Parameter(1, 4, 0, 4);
            EMEVD.Instruction[] malformedM12DlcEvent11210840Body = malformedM12DlcEvent11210840.Instructions.ToArray();
            EMEVD.Parameter[] malformedM12DlcEvent11210840Parameters = malformedM12DlcEvent11210840.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event[] malformed11210840Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210840)
                .Append(malformedM12DlcEvent11210840).ToArray();
            bool malformedM12DlcEvent11210840Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210840Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210840Rejected = true; }
            Check(malformedM12DlcEvent11210840Rejected
                && malformedM12DlcEvent11210840.Instructions.Zip(malformedM12DlcEvent11210840Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformedM12DlcEvent11210840.Parameters, malformedM12DlcEvent11210840Parameters),
                "malformed M12 DLC event 11210840 parameter metadata was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210850 = M12DlcEvent11210850SourceEvent();
            malformedM12DlcEvent11210850.Parameters[0] = new EMEVD.Parameter(1, 0, 0, 4);
            EMEVD.Instruction[] malformedM12DlcEvent11210850Body = malformedM12DlcEvent11210850.Instructions.ToArray();
            EMEVD.Parameter[] malformedM12DlcEvent11210850Parameters = malformedM12DlcEvent11210850.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event[] malformed11210850Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210850)
                .Append(malformedM12DlcEvent11210850).ToArray();
            bool malformedM12DlcEvent11210850Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210850Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210850Rejected = true; }
            Check(malformedM12DlcEvent11210850Rejected
                && malformedM12DlcEvent11210850.Instructions.Zip(malformedM12DlcEvent11210850Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformedM12DlcEvent11210850.Parameters, malformedM12DlcEvent11210850Parameters),
                "malformed M12 DLC event 11210850 parameter metadata was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210800 = M12DlcEvent11210800SourceEvent();
            malformedM12DlcEvent11210800.Parameters[0] = new EMEVD.Parameter(1, 0, 0, 4);
            EMEVD.Instruction[] malformedM12DlcEvent11210800Body = malformedM12DlcEvent11210800.Instructions.ToArray();
            EMEVD.Parameter[] malformedM12DlcEvent11210800Parameters = malformedM12DlcEvent11210800.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event[] malformed11210800Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210800)
                .Append(malformedM12DlcEvent11210800).ToArray();
            bool malformedM12DlcEvent11210800Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210800Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210800Rejected = true; }
            Check(malformedM12DlcEvent11210800Rejected
                && malformedM12DlcEvent11210800.Instructions.Zip(malformedM12DlcEvent11210800Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformedM12DlcEvent11210800.Parameters, malformedM12DlcEvent11210800Parameters),
                "malformed M12 DLC event 11210800 parameter metadata was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210890 = M12DlcEvent11210890SourceEvent();
            malformedM12DlcEvent11210890.Instructions[4] = InsHex(3, 6, "02030000");
            EMEVD.Instruction[] malformedM12DlcEvent11210890Body = malformedM12DlcEvent11210890.Instructions.ToArray();
            EMEVD.Event[] malformed11210890Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210890)
                .Append(malformedM12DlcEvent11210890).ToArray();
            bool malformedM12DlcEvent11210890Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210890Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210890Rejected = true; }
            Check(malformedM12DlcEvent11210890Rejected
                && malformedM12DlcEvent11210890.Instructions.Zip(malformedM12DlcEvent11210890Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && malformedM12DlcEvent11210890.Parameters.Count == 0,
                "malformed M12 DLC event 11210890 was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210849 = M12DlcEvent11210849SourceEvent();
            malformedM12DlcEvent11210849.Instructions[11] = InsHex(2003, 22, "591B0000451E000000000000");
            EMEVD.Instruction[] malformedM12DlcEvent11210849Body = malformedM12DlcEvent11210849.Instructions.ToArray();
            EMEVD.Event[] malformed11210849Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210849)
                .Append(malformedM12DlcEvent11210849).ToArray();
            bool malformedM12DlcEvent11210849Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210849Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210849Rejected = true; }
            Check(malformedM12DlcEvent11210849Rejected
                && malformedM12DlcEvent11210849.Instructions.Zip(malformedM12DlcEvent11210849Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M12 DLC event 11210849 was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210835 = M12DlcEvent11210835SourceEvent();
            malformedM12DlcEvent11210835.Parameters[0] = new EMEVD.Parameter(8, 0, 0, 4);
            EMEVD.Instruction[] malformedM12DlcEvent11210835Body = malformedM12DlcEvent11210835.Instructions.ToArray();
            EMEVD.Parameter[] malformedM12DlcEvent11210835Parameters = malformedM12DlcEvent11210835.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event[] malformed11210835Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210835)
                .Append(malformedM12DlcEvent11210835).ToArray();
            bool malformedM12DlcEvent11210835Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210835Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210835Rejected = true; }
            Check(malformedM12DlcEvent11210835Rejected
                && malformedM12DlcEvent11210835.Instructions.Zip(malformedM12DlcEvent11210835Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformedM12DlcEvent11210835.Parameters, malformedM12DlcEvent11210835Parameters),
                "malformed M12 DLC event 11210835 parameter metadata was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210900 = M12DlcEvent11210900SourceEvent();
            malformedM12DlcEvent11210900.Instructions[2] = InsHex(3, 2, "FF010000102700001F811200");
            EMEVD.Instruction[] malformedM12DlcEvent11210900Body = malformedM12DlcEvent11210900.Instructions.ToArray();
            EMEVD.Event[] malformed11210900Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210900)
                .Append(malformedM12DlcEvent11210900).ToArray();
            bool malformedM12DlcEvent11210900Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210900Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210900Rejected = true; }
            Check(malformedM12DlcEvent11210900Rejected
                && malformedM12DlcEvent11210900.Instructions.Zip(malformedM12DlcEvent11210900Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformedM12DlcEvent11210900.Parameters, new[] { new EMEVD.Parameter(1, 4, 0, 4) }),
                "malformed M12 DLC event 11210900 was accepted or mutated");
            EMEVD.Event malformedM12DlcEvent11210886 = M12DlcEvent11210886SourceEvent();
            malformedM12DlcEvent11210886.Instructions[3] = InsHex(2004, 48, "1027000000008040");
            EMEVD.Instruction[] malformedM12DlcEvent11210886Body = malformedM12DlcEvent11210886.Instructions.ToArray();
            EMEVD.Event[] malformed11210886Events = m12DlcArchive.Events.Where(evt => evt.ID != 11210886)
                .Append(malformedM12DlcEvent11210886).ToArray();
            bool malformedM12DlcEvent11210886Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(malformed11210886Events)); }
            catch (InvalidDataException) { malformedM12DlcEvent11210886Rejected = true; }
            Check(malformedM12DlcEvent11210886Rejected
                && malformedM12DlcEvent11210886.Instructions.Zip(malformedM12DlcEvent11210886Body)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformedM12DlcEvent11210886.Parameters, sourceM12DlcEvent11210886Parameters),
                "malformed M12 DLC event 11210886 was accepted or mutated");
            m12DlcEvent11210836.Instructions[4] = InsHex(2007, 4, "5E4A020000000000");
            EMEVD.Instruction[] malformedM12DlcEvent11210836 = m12DlcEvent11210836.Instructions.ToArray();
            bool malformedM12DlcEvent11210836Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(m12DlcArchive); }
            catch (InvalidDataException) { malformedM12DlcEvent11210836Rejected = true; }
            Check(malformedM12DlcEvent11210836Rejected
                && m12DlcEvent11210836.Instructions.Zip(malformedM12DlcEvent11210836)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M12 DLC event 11210836 was accepted or mutated");
            m12DlcEvent11210836.Instructions.Clear();
            m12DlcEvent11210836.Instructions.AddRange(reconstructedM12DlcEvent11210836);
            m12DlcEvent11210401.Instructions[6] = InsHex(1001, 0, "0000A040");
            EMEVD.Instruction[] malformedM12DlcEvent11210401 = m12DlcEvent11210401.Instructions.ToArray();
            bool malformedM12DlcEvent11210401Rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(m12DlcArchive); }
            catch (InvalidDataException) { malformedM12DlcEvent11210401Rejected = true; }
            Check(malformedM12DlcEvent11210401Rejected
                && m12DlcEvent11210401.Instructions.Zip(malformedM12DlcEvent11210401)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M12 DLC event 11210401 was accepted or mutated");

            EMEVD archive = Evd(drake,
                TerminatedSourceEvent(11605390, 6, EMEVD.Event.RestBehaviorType.Default));
            FogModifiedEventBuilder.ReconstructKnownM16Events(archive);
            Check(drake.Instructions[0].ArgData[4] == 0 && drake.Instructions[1].ArgData[4] == 1,
                "Valley of Drakes map-hit and gravity states were not reconstructed");
            FogModifiedEventBuilder.ReconstructKnownM16Events(archive);
            Check(drake.Instructions[0].ArgData[4] == 0 && drake.Instructions[1].ArgData[4] == 1,
                "Valley of Drakes transformation is not idempotent");
        }

        private static void M13Event50InitializersRemoved()
        {
            EMEVD.Event source = M13MapInitializerSourceEvent();
            EMEVD.Instruction[] expectedBody = source.Instructions.Where((_, index) => index != 2 && index != 4).ToArray();
            EMEVD archive = Evd(source, M13Event11305000SourceEvent(),
                ObjectSfxSourceEvent(11300001, 10, EMEVD.Event.RestBehaviorType.Default, 0x0013DDE6, 0, 0x0013DDE7),
                TerminatedSourceEvent(11305390, 7, EMEVD.Event.RestBehaviorType.Default));

            FogModifiedEventBuilder.ReconstructKnownM13Events(archive);
            Check(source.Instructions.Count == 16 && source.Instructions.Zip(expectedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13 event 50 changed operations beyond its exact initializer removals");

            EMEVD.Instruction[] reconstructedBody = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM13Events(archive);
            Check(source.Instructions.Count == reconstructedBody.Length && source.Instructions.Zip(reconstructedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13 event 50 initializer removal is not idempotent");

            EMEVD.Event malformed = M13MapInitializerSourceEvent();
            malformed.Instructions[4] = InsHex(2000, 0, "000000000784AC004280AC0096190000");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            bool rejected = false;
            try
            {
                FogModifiedEventBuilder.ReconstructKnownM13Events(Evd(malformed, M13Event11305000SourceEvent(),
                    ObjectSfxSourceEvent(11300001, 10, EMEVD.Event.RestBehaviorType.Default, 0x0013DDE6, 0, 0x0013DDE7),
                    TerminatedSourceEvent(11305390, 7, EMEVD.Event.RestBehaviorType.Default)));
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M13 event 50 initializer set was accepted or partially removed");
        }

        private static void M13Event11305000BodyReconstructed()
        {
            EMEVD.Event source = M13Event11305000SourceEvent();
            EMEVD.Instruction[] expectedBody = new[]
            {
                source.Instructions[0], source.Instructions[1], source.Instructions[2], source.Instructions[3],
                source.Instructions[6], InsHex(1001, 0, "00004040"), source.Instructions[8], source.Instructions[9],
                source.Instructions[10], source.Instructions[11], source.Instructions[12],
                InsHex(2003, 2, "BD14000001000000"), source.Instructions[16], source.Instructions[18], source.Instructions[19],
            };
            EMEVD archive = Evd(M13MapInitializerSourceEvent(), source,
                ObjectSfxSourceEvent(11300001, 10, EMEVD.Event.RestBehaviorType.Default, 0x0013DDE6, 0, 0x0013DDE7),
                TerminatedSourceEvent(11305390, 7, EMEVD.Event.RestBehaviorType.Default));

            FogModifiedEventBuilder.ReconstructKnownM13Events(archive);
            Check(source.Instructions.Count == expectedBody.Length && source.Instructions.Zip(expectedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13 event 11305000 differs from its exact reference body");
            EMEVD.Instruction[] reconstructedBody = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM13Events(archive);
            Check(source.Instructions.Count == reconstructedBody.Length && source.Instructions.Zip(reconstructedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13 event 11305000 reconstruction is not idempotent");

            EMEVD.Event malformed = M13Event11305000SourceEvent();
            malformed.Instructions[7] = InsHex(1001, 0, "0000A040");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            bool rejected = false;
            try
            {
                FogModifiedEventBuilder.ReconstructKnownM13Events(Evd(M13MapInitializerSourceEvent(), malformed, M13Event11305000SourceEvent(),
                    ObjectSfxSourceEvent(11300001, 10, EMEVD.Event.RestBehaviorType.Default, 0x0013DDE6, 0, 0x0013DDE7),
                    TerminatedSourceEvent(11305390, 7, EMEVD.Event.RestBehaviorType.Default)));
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M13 event 11305000 was accepted or partially mutated");
        }

        private static EMEVD.Event M13Event11305000SourceEvent() => Evt(11305000,
            InsHex(2000, 2, "00000000"),
            InsHex(3, 6, "01030000"),
            InsHex(3, 8, "01010D00"),
            InsHex(3, 2, "0101000010270000ACE01300"),
            InsHex(3, 0, "01000000B093AC00"),
            InsHex(3, 4, "010300006D00000001000000"),
            InsHex(0, 0, "00010100"),
            InsHex(1001, 0, "0000F041"),
            InsHex(1003, 6, "01020000"),
            InsHex(3, 2, "0601000010270000ACE01300"),
            InsHex(1000, 2, "01000600"),
            InsHex(4, 2, "07000000102700000500000000000000"),
            InsHex(1000, 2, "01010700"),
            InsHex(2003, 2, "E293AC0001000000"),
            InsHex(2002, 4, "E4FB0100000000006E0514000D01000010270000"),
            InsHex(2002, 3, "48FC01000000000010270000"),
            InsHex(1001, 1, "01000000"),
            InsHex(2005, 6, "44021400FFFFFFFF01000000"),
            InsHex(2004, 9, "10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            InsHex(1000, 4, "01000000"));

        private static void KnownMapEventInitializersReconstructed()
        {
            var cases = new (string ArchiveName, int SourceCount, string[] Initializers)[]
            {
                ("m10_00_00_00.emevd.dcx", 36, new[]
                {
                    "0000000065ECA70000000000", "000000006CECA70000000000", "000000006FECA70000000000",
                    "0000000095EDA70000000000", "0000000026F0A70068ECA7008D190000", "0100000026F0A7006BECA700BF190000",
                }),
                ("m10_01_00_00.emevd.dcx", 66, new[]
                {
                    "00000000DC16A80000000000", "00000000DD16A80000000000",
                    "000000003617A800BE13A8008C190000", "010000003617A800C113A800BE190000",
                    "000000002314A80000000000",
                }),
                ("m12_00_00_00.emevd.dcx", 78, new[]
                {
                    "00000000ABF9AA0000000000", "0000000066FDAA00A7F9AA0079190000",
                }),
                ("m14_00_00_00.emevd.dcx", 69, new[]
                {
                    "00000000E506AE0000000000", "00000000EB06AE0000000000",
                    "00000000E6B6D500E706AE0083190000",
                }),
                ("m14_01_00_00.emevd.dcx", 58, new[]
                {
                    "00000000F52DAE0000000000", "00000000FB2DAE0000000000", "00000000FE2DAE0000000000",
                    "00000000F6DDD500F72DAE008E190000",
                }),
                ("m15_00_00_00.emevd.dcx", 44, new[]
                {
                    "00000000858DAF0000000000", "000000004691AF00878DAF006E190000",
                }),
                ("m15_01_00_00.emevd.dcx", 52, new[]
                {
                    "0000000095B4AF0000000000", "0000000056B8AF0097B4AF008F190000",
                }),
                ("m16_00_00_00.emevd.dcx", 38, new[]
                {
                    "000000002514B10000000000", "00000000E617B1002714B10078190000",
                }),
            };

            foreach (var testCase in cases)
            {
                EMEVD.Event source = MapEventInitializerSourceEvent(testCase.SourceCount, testCase.Initializers);
                int[] indexes = Enumerable.Range(0, testCase.Initializers.Length).Select(index => index * 2).ToArray();
                EMEVD.Instruction[] expectedBody = source.Instructions.Where((_, index) => !indexes.Contains(index)).ToArray();
                EMEVD archive = Evd(source);

                FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(testCase.ArchiveName, archive);
                Check(source.Instructions.Count == expectedBody.Length && source.Instructions.Zip(expectedBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                    testCase.ArchiveName + " event 50 changed operations beyond its exact initializer set");
                EMEVD.Instruction[] reconstructedBody = source.Instructions.ToArray();
                FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(testCase.ArchiveName, archive);
                Check(source.Instructions.Count == reconstructedBody.Length && source.Instructions.Zip(reconstructedBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                    testCase.ArchiveName + " event 50 initializer removal is not idempotent");

                EMEVD.Event malformed = MapEventInitializerSourceEvent(testCase.SourceCount, testCase.Initializers);
                byte[] alteredArguments = Convert.FromHexString(testCase.Initializers[^1]);
                alteredArguments[4] ^= 1;
                malformed.Instructions[indexes[^1]] = new EMEVD.Instruction(2000, 0, alteredArguments);
                EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
                bool rejected = false;
                try
                {
                    FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(testCase.ArchiveName, Evd(malformed));
                }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected && malformed.Instructions.Zip(malformedBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                    testCase.ArchiveName + " malformed event 50 initializer set was accepted or partially removed");
            }
        }

        private static void KnownModifiedM13DlcDelayRemoved()
        {
            EMEVD.Event delay = M13DlcDelaySourceEvent();
            EMEVD.Event removedAreaOperations = M13DlcAreaOperationsSourceEvent();
            EMEVD archive = M13DlcSourceArchive(delay, removedAreaOperations);
            FogModifiedEventBuilder.ReconstructKnownM13DlcEvents(archive);
            EMEVD.Event mapInitializer = archive.Events.Single(evt => evt.ID == 50);
            Check(mapInitializer.Instructions.Count == 30
                && !mapInitializer.Instructions.Any(instruction => instruction.Bank == 2000 && instruction.ID == 0
                    && instruction.ArgData.SequenceEqual(Convert.FromHexString("0000000056A7AC0000000000"))),
                "Painted World event 50 did not remove the exact reference-removed event initializer");
            Check(delay.Instructions.Count == 7
                && !delay.Instructions.Any(instruction => instruction.Bank == 1003 && instruction.ID == 1
                    && instruction.ArgData.SequenceEqual(Convert.FromHexString("0301010000000000"))),
                "M13 DLC delay operation was not removed");
            Check(removedAreaOperations.Instructions.Count == 5
                && !removedAreaOperations.Instructions.Any(instruction =>
                    (instruction.Bank == 1003 && instruction.ID == 5
                        && instruction.ArgData.SequenceEqual(Convert.FromHexString("01000000")))
                    || (instruction.Bank == 3 && instruction.ID == 0
                        && instruction.ArgData.SequenceEqual(Convert.FromHexString("01010000BFA8AC00")))),
                "M13 DLC event 11315394 did not remove its exact instruction set");
            EMEVD.Event objectOperations = archive.Events.Single(evt => evt.ID == 11315398);
            Check(objectOperations.Instructions.Count == 9
                && objectOperations.Instructions[0].Bank == 3 && objectOperations.Instructions[0].ID == 0
                && objectOperations.Instructions[4].Bank == 2005 && objectOperations.Instructions[4].ID == 11
                && objectOperations.Instructions[6].Bank == 2005 && objectOperations.Instructions[6].ID == 9
                && objectOperations.Instructions[7].Bank == 2006 && objectOperations.Instructions[7].ID == 3,
                "M13 DLC event 11315398 did not reconstruct the exact object-operation body");
            EMEVD.Event areaStateEvent = archive.Events.Single(evt => evt.ID == 11310052);
            Check(areaStateEvent.Instructions.Count == 7
                && areaStateEvent.Instructions[3].Bank == 2003 && areaStateEvent.Instructions[3].ID == 2
                && areaStateEvent.Instructions[3].ArgData.SequenceEqual(Convert.FromHexString("BE14000001000000"))
                && areaStateEvent.Instructions[6].Bank == 1000 && areaStateEvent.Instructions[6].ID == 4,
                "M13 DLC event 11310052 did not reconstruct the expected flag state body");
            EMEVD.Instruction[] reconstructedBody = delay.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedAreaBody = removedAreaOperations.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedObjectBody = objectOperations.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedAreaStateBody = areaStateEvent.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedMapInitializer = mapInitializer.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM13DlcEvents(archive);
            Check(delay.Instructions.Count == reconstructedBody.Length
                && delay.Instructions.Zip(reconstructedBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13 DLC delay removal is not idempotent");
            Check(removedAreaOperations.Instructions.Count == reconstructedAreaBody.Length
                && removedAreaOperations.Instructions.Zip(reconstructedAreaBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13 DLC instruction-set removal is not idempotent");
            Check(objectOperations.Instructions.Count == reconstructedObjectBody.Length
                && objectOperations.Instructions.Zip(reconstructedObjectBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13 DLC full-body object reconstruction is not idempotent");
            Check(areaStateEvent.Instructions.Count == reconstructedAreaStateBody.Length
                && areaStateEvent.Instructions.Zip(reconstructedAreaStateBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13 DLC event 11310052 reconstruction is not idempotent");
            Check(mapInitializer.Instructions.Count == reconstructedMapInitializer.Length
                && mapInitializer.Instructions.Zip(reconstructedMapInitializer)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "Painted World event 50 initializer removal is not idempotent");
        }

        private static void KnownModifiedM13DlcDelayRejectsDuplicate()
        {
            EMEVD.Event malformed = TerminatedSourceEvent(11315393, 8, EMEVD.Event.RestBehaviorType.Default);
            malformed.Instructions[0] = InsHex(1003, 1, "0301010000000000");
            malformed.Instructions[1] = InsHex(1003, 1, "0301010000000000");
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM13DlcEvents(M13DlcSourceArchive(malformed, M13DlcAreaOperationsSourceEvent())); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "duplicate M13 DLC delay operations were accepted");
            Check(malformed.Instructions.Count == 8
                && malformed.Instructions[0].Bank == 1003 && malformed.Instructions[0].ID == 1
                && malformed.Instructions[1].Bank == 1003 && malformed.Instructions[1].ID == 1,
                "malformed M13 DLC event changed before duplicate operation validation");

            EMEVD.Event malformedAreaBody = M13DlcAreaOperationsSourceEvent();
            malformedAreaBody.Instructions[6] = InsHex(2010, 3, "0000000000000000");
            rejected = false;
            try
            {
                FogModifiedEventBuilder.ReconstructKnownM13DlcEvents(
                    M13DlcSourceArchive(M13DlcDelaySourceEvent(), malformedAreaBody));
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "malformed M13 DLC event 11315394 body was accepted");
            Check(malformedAreaBody.Instructions.Count == 7
                && malformedAreaBody.Instructions[6].ArgData.SequenceEqual(Convert.FromHexString("0000000000000000")),
                "malformed M13 DLC event 11315394 changed before full-body validation");

            EMEVD.Event malformedObjectBody = M13DlcObjectOperationsSourceEvent();
            malformedObjectBody.Instructions[2] = InsHex(4, 4, "00000000500014001027000001000000");
            rejected = false;
            try
            {
                FogModifiedEventBuilder.ReconstructKnownM13DlcEvents(
                    M13DlcSourceArchive(M13DlcDelaySourceEvent(), M13DlcAreaOperationsSourceEvent(), malformedObjectBody));
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "malformed M13 DLC event 11315398 body was accepted");
            Check(malformedObjectBody.Instructions.Count == 9
                && malformedObjectBody.Instructions[2].ArgData.SequenceEqual(Convert.FromHexString("00000000500014001027000001000000")),
                "malformed M13 DLC event 11315398 changed before full-body validation");

            EMEVD.Event malformedAreaStateBody = M13DlcAreaStateSourceEvent();
            malformedAreaStateBody.Instructions[1] = InsHex(2004, 9, "01000000");
            rejected = false;
            try
            {
                FogModifiedEventBuilder.ReconstructKnownM13DlcEvents(
                    M13DlcSourceArchive(M13DlcDelaySourceEvent(), malformedAreaStateBody));
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "malformed M13 DLC event 11310052 body was accepted");
            Check(malformedAreaStateBody.Instructions.Count == 9
                && malformedAreaStateBody.Instructions[1].ArgData.SequenceEqual(Convert.FromHexString("01000000")),
                "malformed M13 DLC event 11310052 changed before full-body validation");
        }

        private static EMEVD M13DlcSourceArchive(params EMEVD.Event[] modifiedEvents)
        {
            var defaultModifiedEvents = new List<EMEVD.Event>();
            if (!modifiedEvents.Any(evt => evt.ID == 11315398))
                defaultModifiedEvents.Add(M13DlcObjectOperationsSourceEvent());
            if (!modifiedEvents.Any(evt => evt.ID == 11310052))
                defaultModifiedEvents.Add(M13DlcAreaStateSourceEvent());
            return Evd(new[]
            {
                M13DlcMapInitializerSourceEvent(),
                TerminatedSourceEvent(11310095, 14, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11315390, 7, EMEVD.Event.RestBehaviorType.Default),
                ObjectSfxSourceEvent(11310001, 13, EMEVD.Event.RestBehaviorType.Default, 0x001404F6, 0, 0x001404F7),
            }.Concat(defaultModifiedEvents).Concat(modifiedEvents).ToArray());
        }

        private static EMEVD.Event M11MapInitializerSourceEvent()
        {
            EMEVD.Event evt = new EMEVD.Event(50, EMEVD.Event.RestBehaviorType.Default);
            for (int i = 0; i < 19; i++) evt.Instructions.Add(InsHex(1000, 4, "00000000"));
            evt.Instructions[2] = InsHex(2000, 0, "000000000673A90000000000");
            return evt;
        }

        private static EMEVD.Event M13DlcMapInitializerSourceEvent()
        {
            EMEVD.Event evt = new EMEVD.Event(50, EMEVD.Event.RestBehaviorType.Default);
            for (int i = 0; i < 31; i++) evt.Instructions.Add(InsHex(1000, 4, "00000000"));
            evt.Instructions[1] = InsHex(2000, 0, "0000000056A7AC0000000000");
            return evt;
        }

        private static EMEVD.Event M13DlcDelaySourceEvent()
        {
            EMEVD.Event delay = TerminatedSourceEvent(11315393, 8, EMEVD.Event.RestBehaviorType.Default);
            delay.Instructions[0] = InsHex(1003, 1, "0301010000000000");
            return delay;
        }

        private static EMEVD.Event M13DlcAreaOperationsSourceEvent() => Evt(11315394,
            InsHex(2000, 2, "00000000"),
            InsHex(3, 0, "0100000007000000"),
            InsHex(3, 0, "01010000C0A8AC00"),
            InsHex(1003, 5, "01000000"),
            InsHex(3, 0, "01010000BFA8AC00"),
            InsHex(0, 0, "00010100"),
            InsHex(2010, 3, "080C140001000000"));

        private static EMEVD.Event M13DlcObjectOperationsSourceEvent()
        {
            EMEVD.Event evt = new EMEVD.Event(11315398, EMEVD.Event.RestBehaviorType.Restart);
            evt.Instructions.AddRange(new[]
            {
                InsHex(2000, 2, "00000000"),
                InsHex(3, 0, "01010000BEA8AC00"),
                InsHex(4, 4, "01000000500014001027000001000000"),
                InsHex(4, 8, "0100000050001400BC02000001000000"),
                InsHex(0, 0, "00010100"),
                InsHex(2003, 2, "F6A0AC0001000000"),
                InsHex(4, 8, "0000000050001400C602000001000000"),
                InsHex(2003, 2, "F7A0AC0001000000"),
                InsHex(1000, 4, "01000000"),
            });
            return evt;
        }

        private static EMEVD.Event M13DlcAreaStateSourceEvent() => Evt(11310052,
            InsHex(5, 2, "000000008CA8AC00"),
            InsHex(2004, 9, "10270000EF1B0000FFFFFFFFFFFFFFFFC2170000FFFFFFFF"),
            InsHex(1001, 0, "00004040"),
            InsHex(2002, 4, "49FC010000000000FADD13000D00000010270000"),
            InsHex(2002, 3, "E5FB01000000000010270000"),
            InsHex(1001, 1, "01000000"),
            InsHex(2004, 9, "10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            InsHex(2005, 6, "44021400FFFFFFFF01000000"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Event M18DlcFinalFlagSourceEvent() => Evt(11815394,
            InsHex(2000, 2, "00000000"),
            InsHex(3, 0, "00000000E449B400"),
            InsHex(3, 0, "0100000010000000"),
            InsHex(3, 0, "010100000636B400"),
            InsHex(3, 0, "01010000E049B400"),
            InsHex(3, 2, "FF01000010270000FEA91B00"),
            InsHex(3, 2, "FF010000102700007EA71B00"),
            InsHex(0, 0, "0101FF00"),
            InsHex(0, 0, "00010100"),
            InsHex(2003, 2, "E449B40001000000"),
            InsHex(2010, 3, "28AD1B0001000000"),
            InsHex(2003, 11, "0100000070A11B000000B808"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Event M18DlcEvent11810150SourceEvent() => Evt(11810150,
            InsHex(1003, 1, "03010000D034B400"),
            InsHex(2005, 3, "64A31B0000000000"),
            InsHex(3, 0, "00010000D034B400"),
            InsHex(2005, 3, "64A31B0001000000"),
            InsHex(3, 5, "000000000100000021A61B0000000000FFFF0000000000008ABF9800FF00000000000000"),
            InsHex(2004, 9, "10270000881E0000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            InsHex(2003, 18, "10270000871E000000010000"),
            InsHex(1003, 5, "06020000"),
            InsHex(2004, 9, "10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            InsHex(2002, 4, "9EBF020000000000A9980F000A02000010270000"),
            InsHex(2002, 3, "828701000000000010270000"),
            InsHex(1001, 1, "01000000"),
            InsHex(2003, 28, "1C000000"),
            InsHex(1000, 4, "01000000"),
            InsHex(3, 5, "010000000100000021A61B0000000000FFFF0000000000008BBF9800FF00000000000000"),
            InsHex(3, 2, "020000001027000021A61B00"),
            InsHex(0, 0, "FF010100"),
            InsHex(0, 0, "FF010200"),
            InsHex(0, 0, "0001FF00"),
            InsHex(2004, 9, "10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            InsHex(1000, 7, "01010200"),
            InsHex(2003, 18, "10270000891E000000010000"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Instruction[] M18DlcEvent11810150ExpectedBody(EMEVD.Instruction[] source) =>
            source.Skip(4).Take(3)
                .Concat(new[] { InsHex(1003, 5, "04020000"), source[8], InsHex(2003, 2, "BB14000001000000") })
                .Concat(source.Skip(11).Take(1)).Concat(source.Skip(13)).ToArray();

        private static void ModifiedEventsMatchReference(string pristineGameRoot)
        {
            VerifyModifiedEvents(pristineGameRoot, "common.emevd.dcx", new long[] { 0, 260, 840 }, FogModifiedEventBuilder.ReconstructKnownCommonEvents);
            VerifyModifiedEvents(pristineGameRoot, "m11_00_00_00.emevd.dcx", new long[] { 0, 50, 11100000, 11100400, 11100710, 11105390, 11105399 }, FogModifiedEventBuilder.ReconstructKnownM11Events);
            VerifyModifiedEvents(pristineGameRoot, "m10_00_00_00.emevd.dcx", new long[] { 0, 50, 11000001, 11005030, 11005033, 11005150, 11005390, 11005391, 11005392 }, FogModifiedEventBuilder.ReconstructKnownM10Events);
            VerifyModifiedEvents(pristineGameRoot, "m10_01_00_00.emevd.dcx", new long[] { 0, 50, 11010000, 11010001, 11010901, 11010902, 11015100, 11015103, 11015370, 11015380, 11015390 }, FogModifiedEventBuilder.ReconstructKnownM10ParishEvents);
            VerifyModifiedEvents(pristineGameRoot, "m10_02_00_00.emevd.dcx", new long[] { 50, 11020001, 11020021, 11020899 }, FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents);
            VerifyModifiedEvents(pristineGameRoot, "m12_00_00_00.emevd.dcx", new long[] { 0, 50, 11200000, 11200001, 11200002, 11200005, 11200006, 11200900, 11205380, 11205390 }, FogModifiedEventBuilder.ReconstructKnownM12Events);
            VerifyModifiedEvents(pristineGameRoot, "m12_01_00_00.emevd.dcx", new long[] { 0, 11210000, 11210001, 11210002, 11210005, 11210100, 11210110, 11210120, 11210130, 11210140, 11210340, 11210341, 11210345, 11210401, 11210402, 11210404, 11210430, 11210434, 11210435, 11210407, 11210439, 11210535, 11210700, 11210701, 11210800, 11210830, 11210831, 11210835, 11210836, 11210837, 11210838, 11210839, 11210840, 11210849, 11210850, 11210870, 11210875, 11210878, 11210886, 11210890, 11210900, 11215000, 11215007, 11215009, 11215010, 11215020, 11215060 }, FogModifiedEventBuilder.ReconstructKnownM12DlcEvents);
            VerifyModifiedEvents(pristineGameRoot, "m13_00_00_00.emevd.dcx", new long[] { 0, 50, 11300001, 11305000, 11305390 }, FogModifiedEventBuilder.ReconstructKnownM13Events);
            VerifyModifiedEvents(pristineGameRoot, "m13_01_00_00.emevd.dcx", new long[] { 0, 50, 11310001, 11310052, 11310095, 11315390, 11315393, 11315394, 11315398 }, FogModifiedEventBuilder.ReconstructKnownM13DlcEvents);
            VerifyModifiedEvents(pristineGameRoot, "m13_02_00_00.emevd.dcx", new long[] { 0 }, archive => { });
            VerifyModifiedEvents(pristineGameRoot, "m14_00_00_00.emevd.dcx", new long[] { 0, 50, 11400001, 11400200, 11405390, 11405392 }, FogModifiedEventBuilder.ReconstructKnownM14Events);
            VerifyModifiedEvents(pristineGameRoot, "m14_01_00_00.emevd.dcx", new long[] { 0, 50, 11410001, 11410095, 11410410, 11410900, 11410901, 11415340, 11415342, 11415370, 11415373, 11415380, 11415382 }, FogModifiedEventBuilder.ReconstructKnownM14DlcEvents);
            VerifyModifiedEvents(pristineGameRoot, "m15_00_00_00.emevd.dcx", new long[] { 0, 50, 11500001, 11500210, 11505390 }, FogModifiedEventBuilder.ReconstructKnownM15Events);
            VerifyModifiedEvents(pristineGameRoot, "m15_01_00_00.emevd.dcx", new long[] { 0, 50, 11510001, 11510131, 11510230, 11510240, 11510460, 11510502, 11510543, 11510900, 11515381, 11515386, 11515390, 11515392 }, FogModifiedEventBuilder.ReconstructKnownM15DlcEvents);
            VerifyModifiedEvents(pristineGameRoot, "m16_00_00_00.emevd.dcx", new long[] { 0, 50, 11600001, 11600400, 11605390 }, FogModifiedEventBuilder.ReconstructKnownM16Events);
            VerifyModifiedEvents(pristineGameRoot, "m17_00_00_00.emevd.dcx", new long[] { 0, 11700000, 11700001, 11700085, 11705100, 11705380, 11705381, 11705382, 11705383, 11705385, 11705390 }, FogModifiedEventBuilder.ReconstructKnownM17Events);
            VerifyModifiedEvents(pristineGameRoot, "m18_00_00_00.emevd.dcx", new long[] { 0, 21, 50, 11805390 }, FogModifiedEventBuilder.ReconstructKnownM18Events);
            VerifyModifiedEvents(pristineGameRoot, "m18_01_00_00.emevd.dcx", new long[] { 0 }, archive => FogModifiedEventBuilder.ReconstructKnownMapEventInitializers("m18_01_00_00.emevd.dcx", archive));
            VerifyModifiedEvents(pristineGameRoot, "m18_01_00_00.emevd.dcx", new long[] { 11810000, 11810001, 11810150, 11810300, 11810310, 11810900, 11815382, 11815390, 11815394 }, FogModifiedEventBuilder.ReconstructKnownM18DlcEvents);
        }

        private static void BossOverlayEventsReconstructed(string pristineGameRoot)
        {
            var cases = new (string Archive, long EventId)[]
            {
                ("m12_01_00_00.emevd.dcx", 11210001),
                ("m14_00_00_00.emevd.dcx", 11405392),
                ("m14_01_00_00.emevd.dcx", 11415342),
                ("m14_01_00_00.emevd.dcx", 11415382),
                ("m15_01_00_00.emevd.dcx", 11515392),
                ("m16_00_00_00.emevd.dcx", 11600001),
                ("m18_01_00_00.emevd.dcx", 11810310),
                ("m18_01_00_00.emevd.dcx", 11815382),
            };

            foreach (var testCase in cases)
            {
                string pristinePath = Path.Combine(pristineGameRoot, "event", testCase.Archive);
                string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", testCase.Archive);
                EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(pristinePath)));
                EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
                FogModifiedEventBuilder.ReconstructBossEventsForOfflineRoot(testCase.Archive, source);
                EMEVD.Event actual = source.Events.Single(evt => evt.ID == testCase.EventId);
                EMEVD.Event expected = reference.Events.Single(evt => evt.ID == testCase.EventId);
                Check(EmevdMerge.SameEvent(actual, expected),
                    "boss event " + testCase.EventId + " does not match the pinned target body, rest behavior, layers, arguments or parameters");

                FogModifiedEventBuilder.ReconstructBossEventsForOfflineRoot(testCase.Archive, source);
                Check(EmevdMerge.SameEvent(actual, expected),
                    "boss event " + testCase.EventId + " reconstruction is not idempotent");

                EMEVD malformed = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(pristinePath)));
                EMEVD.Event malformedEvent = malformed.Events.Single(evt => evt.ID == testCase.EventId);
                EMEVD.Instruction first = malformedEvent.Instructions[0];
                byte[] changedArguments = first.ArgData.ToArray();
                changedArguments[0] ^= 1;
                malformedEvent.Instructions[0] = new EMEVD.Instruction(first.Bank, first.ID, changedArguments)
                {
                    Layer = first.Layer,
                };
                EMEVD.Event before = CopyCompleteEvent(malformedEvent);
                bool rejected = false;
                try { FogModifiedEventBuilder.ReconstructBossEventsForOfflineRoot(testCase.Archive, malformed); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected && EmevdMerge.SameEvent(malformedEvent, before),
                    "boss event " + testCase.EventId + " accepted or mutated a source with unrelated instruction drift");
            }
        }

        private static EMEVD.Event CopyCompleteEvent(EMEVD.Event source)
        {
            var copy = new EMEVD.Event(source.ID, source.RestBehavior);
            copy.Instructions.AddRange(source.Instructions.Select(instruction =>
                new EMEVD.Instruction(instruction.Bank, instruction.ID, instruction.ArgData.ToArray())
                {
                    Layer = instruction.Layer,
                }));
            copy.Parameters.AddRange(source.Parameters.Select(parameter =>
                new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }));
            return copy;
        }

        private static void M10ParishEvent0Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m10_01_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m10_01_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M10 Parish event 0 differs from pinned object/SFX removal or skip spans");
            FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M10 Parish event 0 reconstruction was not idempotent");
            EMEVD.Instruction dynamicInitializer = Ins(2000, 0, 0, 5700, 0);
            reconstructed.Instructions.Add(dynamicInitializer);
            FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(source);
            Check(reconstructed.Instructions.Count == expected.Instructions.Count + 1
                && reconstructed.Instructions.Take(expected.Instructions.Count)
                    .Zip(expected.Instructions).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && EmevdMerge.SameInstruction(reconstructed.Instructions[^1], dynamicInitializer),
                "M10 Parish event 0 changed the generated initializer suffix after its known Fog prefix");

            EMEVD malformed = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformedEvent = malformed.Events.Single(evt => evt.ID == 0);
            EMEVD.Instruction first = malformedEvent.Instructions[0];
            byte[] changedArguments = first.ArgData.ToArray();
            changedArguments[0] ^= 1;
            malformedEvent.Instructions[0] = new EMEVD.Instruction(first.Bank, first.ID, changedArguments)
                { Layer = first.Layer };
            EMEVD.Instruction[] before = malformedEvent.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(malformed); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformedEvent.Instructions.Zip(before)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M10 Parish event 0 accepted or mutated a source with unrelated instruction drift");
        }

        private static void CommonEvent0Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "common.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "common.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownCommonEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "common event 0 differs from pinned randomized-start initializers or startup instructions");
            FogModifiedEventBuilder.ReconstructKnownCommonEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "common event 0 reconstruction was not idempotent");
            EMEVD.Instruction dynamicInitializer = Ins(2000, 0, 0, 5700, 0);
            reconstructed.Instructions.Add(dynamicInitializer);
            FogModifiedEventBuilder.ReconstructKnownCommonEvents(source);
            Check(reconstructed.Instructions.Count == expected.Instructions.Count + 1
                && reconstructed.Instructions.Take(expected.Instructions.Count)
                    .Zip(expected.Instructions).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && EmevdMerge.SameInstruction(reconstructed.Instructions[^1], dynamicInitializer),
                "common event 0 changed the generated initializer suffix after its known Fog prefix");

            EMEVD malformed = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformedEvent = malformed.Events.Single(evt => evt.ID == 0);
            EMEVD.Instruction first = malformedEvent.Instructions[0];
            byte[] changedArguments = first.ArgData.ToArray();
            changedArguments[0] ^= 1;
            malformedEvent.Instructions[0] = new EMEVD.Instruction(first.Bank, first.ID, changedArguments)
                { Layer = first.Layer };
            EMEVD.Instruction[] before = malformedEvent.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownCommonEvents(malformed); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformedEvent.Instructions.Zip(before)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "common event 0 accepted or mutated a source with unrelated instruction drift");
        }

        private static void VerifyModifiedEvents(string pristineGameRoot, string archiveName, long[] eventIds, Action<EMEVD> reconstruct)
        {
            string pristinePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD pristine = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(pristinePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructBossEventsForOfflineRoot(archiveName, pristine);
            reconstruct(pristine);
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, pristine);
            foreach (long id in eventIds)
                Check(EmevdMerge.SameEvent(pristine.Events.Single(evt => evt.ID == id), reference.Events.Single(evt => evt.ID == id)),
                    "modified event " + id + " differs from pinned reference semantics");
        }

        private static void M10Event11005150Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m10_00_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m10_00_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM10Events(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 11005150);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 11005150);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11005150 differs from pinned instructions, skip spans or parameter mappings");
            FogModifiedEventBuilder.ReconstructKnownM10Events(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11005150 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 11005150);
            malformed.Instructions[24] = InsHex(1000, 7, "06000100");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            EMEVD.Parameter[] malformedParameters = malformed.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM10Events(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedInstructions)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformed.Parameters, malformedParameters),
                "event 11005150 accepted or mutated a source with a changed skip span");
        }

        private static void M15DlcEvent11515386Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m15_01_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m15_01_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 11515386);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 11515386);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11515386 differs from pinned operations or branch thresholds");
            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11515386 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 11515386);
            malformed.Instructions[17] = InsHex(1000, 7, "0B000100");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedInstructions)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11515386 accepted or mutated a source with an unknown conditional range");
        }

        private static void M18Event50Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m18_00_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m18_00_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM18Events(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 50);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 50);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M18 event 50 differs from pinned guard, initializer, or object/SFX semantics");
            FogModifiedEventBuilder.ReconstructKnownM18Events(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M18 event 50 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 50);
            malformed.Instructions[2] = InsHex(1003, 1, "030000000F000000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM18Events(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedInstructions)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M18 event 50 accepted or mutated a source with an unknown multiplayer guard");
        }

        private static void M10NewLondoEvent50Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m10_02_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m10_02_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 50);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 50);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M10 New Londo event 50 differs from pinned flag or initializer semantics");
            FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M10 New Londo event 50 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 50);
            malformed.Instructions[0] = InsHex(2003, 22, "2000FB026700FB0201000000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedInstructions)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M10 New Londo event 50 accepted or mutated a source with an unknown world flag");
        }

        private static void M12DlcEvent11210700Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m12_01_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m12_01_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));

            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 11210700);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 11210700);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M12 DLC event 11210700 differs from the named arena mode, door, prompt or participant wiring");

            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M12 DLC event 11210700 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 11210700);
            EMEVD.Instruction first = malformed.Instructions[0];
            byte[] changedArguments = first.ArgData.ToArray();
            changedArguments[0] ^= 1;
            malformed.Instructions[0] = new EMEVD.Instruction(first.Bank, first.ID, changedArguments)
                { Layer = first.Layer };
            EMEVD.Instruction[] before = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(before)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 11210700 accepted or mutated a source with unrelated instruction drift");
        }

        private static void M12DlcEvent11210839Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m12_01_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m12_01_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 11210839);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 11210839);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210839 differs from the pinned multiplayer area flag watcher");
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210839 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 11210839);
            malformed.Instructions[1] = InsHex(2003, 22, "CC1F0000D31F000000000000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedInstructions)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11210839 accepted or mutated a source with an unknown flag range");
        }

        private static void M12DlcEvent11210875Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m12_01_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m12_01_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 11210875);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 11210875);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210875 differs from pinned instructions or parameter semantics");
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210875 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 11210875);
            malformed.Instructions[0] = InsHex(3, 2, "020100001027000055991200");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            EMEVD.Parameter[] malformedParameters = malformed.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformed.Parameters, malformedParameters),
                "event 11210875 accepted or mutated a source with an unknown area condition");
        }

        private static void M12DlcEvent11210100Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m12_01_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m12_01_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 11210100);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 11210100);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210100 differs from the pinned area conditions or watcher operations");
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210100 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 11210100);
            malformed.Instructions[7] = InsHex(3, 2, "0101000010270000C57E1201");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11210100 accepted or mutated a source with an unknown area condition");
        }

        private static void M12DlcEvent11210830Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m12_01_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m12_01_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 11210830);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 11210830);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210830 differs from the pinned area enemy-state watcher");
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210830 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 11210830);
            malformed.Instructions[23] = InsHex(3, 2, "FE010000102700002B811201");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11210830 accepted or mutated a source with an unknown area condition");
        }

        private static void M12DlcEvent11210870Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m12_01_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m12_01_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 11210870);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 11210870);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210870 differs from pinned guidance instructions or parameter semantics");
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210870 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 11210870);
            malformed.Instructions[9] = InsHex(3, 20, "0200000000000000060300000000000006000001");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            EMEVD.Parameter[] malformedParameters = malformed.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformed.Parameters, malformedParameters),
                "event 11210870 accepted or mutated a source with unknown guidance conditions");
        }

        private static void M12DlcEvent11210435Reconstructed(string pristineGameRoot)
        {
            string archivePath = Path.Combine(pristineGameRoot, "event", "m12_01_00_00.emevd.dcx");
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m12_01_00_00.emevd.dcx");
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 11210435);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 11210435);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210435 differs from the pinned seven-state watcher or parameter semantics");
            FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "event 11210435 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 11210435);
            malformed.Instructions[0] = InsHex(3, 0, "010100001E22AB01");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            EMEVD.Parameter[] malformedParameters = malformed.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformed.Parameters, malformedParameters),
                "event 11210435 accepted or mutated an unknown seven-state condition");
        }

        private static void M13PaintedWorldEvent0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m13_02_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M13-2 event 0 differs from pinned initializer semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M13-2 event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[15] = InsHex(1003, 5, "03010000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedInstructions)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13-2 event 0 accepted or mutated a different initializer sequence");
        }

        private static void M12Event0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m12_00_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M12 event 0 differs from pinned object, SFX and skip semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M12 event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[35] = InsHex(1003, 1, "0300000005000000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 event 0 accepted or mutated a source with a different skip span");
        }

        private static void M12DlcEvent0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m12_01_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M12 DLC event 0 differs from pinned SFX, skip and initializer semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M12 DLC event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[1] = InsHex(1003, 5, "08010000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event 0 accepted or mutated a source with a different skip span");
        }

        private static void M15DlcEvent0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m15_01_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M15 DLC event 0 differs from pinned object and initializer semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M15 DLC event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[8] = InsHex(1003, 5, "03010000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M15 DLC event 0 accepted or mutated a source with a changed object skip");
        }

        private static void M14DlcEvent0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m14_01_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M14 DLC event 0 differs from pinned object, SFX and skip semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M14 DLC event 0 reconstruction was not idempotent");

            EMEVD knownSourceVariant = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            knownSourceVariant.Events.Single(evt => evt.ID == 0).Instructions[8] = InsHex(1003, 5, "07010000");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, knownSourceVariant);
            Check(EmevdMerge.SameEvent(knownSourceVariant.Events.Single(evt => evt.ID == 0), expected),
                "M14 DLC event 0 did not normalize the validated FogMod skip-count variant");

            EMEVD runtimeVariant = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            EMEVD runtimeVariantExpected = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            for (int index = 35; index <= 48; index++)
            {
                int entity = index == 35 ? 0x00AE1B2C : 0x00AE1B2D;
                foreach (EMEVD.Event evt in new[]
                    {
                        runtimeVariant.Events.Single(candidate => candidate.ID == 0),
                        runtimeVariantExpected.Events.Single(candidate => candidate.ID == 0),
                    })
                {
                    EMEVD.Instruction instruction = evt.Instructions[index];
                    byte[] arguments = instruction.ArgData.ToArray();
                    Buffer.BlockCopy(BitConverter.GetBytes(entity), 0, arguments, 4, 4);
                    evt.Instructions[index] = new EMEVD.Instruction(instruction.Bank, instruction.ID, arguments)
                    {
                        Layer = instruction.Layer,
                    };
                }
            }
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, runtimeVariant);
            Check(EmevdMerge.SameEvent(runtimeVariant.Events.Single(evt => evt.ID == 0),
                    runtimeVariantExpected.Events.Single(evt => evt.ID == 0)),
                "M14 DLC event 0 did not preserve the validated runtime entity remap");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[8] = InsHex(1003, 5, "05010000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M14 DLC event 0 accepted or mutated a source with a changed object skip");
        }

        private static void M13Event0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m13_00_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M13 event 0 differs from pinned object and initializer semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M13 event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[14] = InsHex(1003, 5, "02010000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13 event 0 accepted or mutated a source with a changed multiplayer condition");
        }

        private static void M18Event0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m18_00_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M18 event 0 differs from pinned multiplayer object/SFX semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M18 event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[4] = InsHex(1003, 5, "03010000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedInstructions)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M18 event 0 accepted or mutated a different multiplayer setup");
        }

        private static void M18DlcEvent0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m18_01_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M18 DLC event 0 differs from pinned initializer, flag and object/SFX semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M18 DLC event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[98] = InsHex(1003, 1, "010000000836B401");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M18 DLC event 0 accepted or mutated an unknown source variant");
        }

        private static void M13DlcEvent0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m13_01_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M13 DLC event 0 differs from the pinned event, flag and object/SFX semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M13 DLC event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[53] = InsHex(1003, 1, "0200000007000001");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M13 DLC event 0 accepted or mutated an unknown event condition");
        }

        private static void M14Event0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m14_00_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M14 event 0 differs from pinned object/SFX, initializer and flag semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M14 event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[61] = InsHex(1003, 1, "0200000009000001");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M14 event 0 accepted or mutated an unknown skip check");
        }

        private static void M16Event0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m16_00_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M16 event 0 differs from pinned object/SFX, initializer and skip semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M16 event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[6] = InsHex(1003, 5, "01010001");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M16 event 0 accepted or mutated an unknown skip condition");
        }

        private static void M17Event0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m17_00_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M17 event 0 differs from pinned event, object/SFX and initializer semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M17 event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[17] = InsHex(1003, 5, "01010001");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedInstructions.Length
                && malformed.Instructions.Zip(malformedInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M17 event 0 accepted or mutated an unknown skip condition");
        }

        private static void M11Event0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m11_00_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M11 event 0 differs from pinned object, flag and initializer semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M11 event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[70] = InsHex(1003, 1, "0500000004000000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedInstructions)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M11 event 0 accepted or mutated a source with a different skip span");
        }

        private static void M15Event0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m15_00_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M15 event 0 differs from pinned multiplayer, object/SFX or initializer semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M15 event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[127] = InsHex(1003, 1, "030000000B000000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedInstructions)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M15 event 0 accepted or mutated a source with an unknown skip span");
        }

        private static void M10Event0Reconstructed(string pristineGameRoot)
        {
            const string archiveName = "m10_00_00_00.emevd.dcx";
            string archivePath = Path.Combine(pristineGameRoot, "event", archiveName);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", archiveName);
            EMEVD source = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            EMEVD.Event reconstructed = source.Events.Single(evt => evt.ID == 0);
            EMEVD.Event expected = reference.Events.Single(evt => evt.ID == 0);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M10 event 0 differs from pinned multiplayer, object/SFX or initializer semantics");
            FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, source);
            Check(EmevdMerge.SameEvent(reconstructed, expected),
                "M10 event 0 reconstruction was not idempotent");

            EMEVD malformedArchive = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(archivePath)));
            EMEVD.Event malformed = malformedArchive.Events.Single(evt => evt.ID == 0);
            malformed.Instructions[10] = InsHex(1003, 5, "06010000");
            EMEVD.Instruction[] malformedInstructions = malformed.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer }).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownMapEventInitializers(archiveName, malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedInstructions)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M10 event 0 accepted or mutated a source with a different multiplayer skip");
        }

        private static void KnownModifiedKingFamilyEventReconstructed()
        {
            EMEVD.Event kingFamily = new EMEVD.Event(11510460, EMEVD.Event.RestBehaviorType.Default);
            for (int i = 0; i < 40; i++) kingFamily.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            kingFamily.Instructions[17] = new EMEVD.Instruction(3, 2,
                new object[] { (sbyte)3, (byte)0, 10000, 0x001713DB });
            EMEVD.Event[] stoppedGates = new[]
            {
                TerminatedSourceEvent(11510131, 8, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11515390, 6, EMEVD.Event.RestBehaviorType.Default),
            };
            EMEVD.Event fogFlagEvent = M15DlcFogFlagSourceEvent();
            EMEVD.Event gateFlagEvent = M15DlcGateFlagSourceEvent();
            EMEVD.Event gateAreaTransitionEvent = M15DlcGateAreaTransitionSourceEvent();
            EMEVD.Event fogWatcherEvent = M15DlcEvent11515381SourceEvent();
            EMEVD.Event objectSfxEvent = M15DlcEvent11510001SourceEvent();
            EMEVD.Event removedSfxEvent = M15DlcEvent11510543SourceEvent();
            EMEVD.Event gateTimingEvent = M15DlcEvent11510900SourceEvent();
            EMEVD.Instruction[] originalFogWatcherBody = fogWatcherEvent.Instructions.ToArray();
            EMEVD archive = Evd(new[] { kingFamily }.Concat(stoppedGates).Append(fogFlagEvent).Append(gateFlagEvent)
                .Append(gateAreaTransitionEvent).Append(fogWatcherEvent).Append(objectSfxEvent).Append(removedSfxEvent)
                .Append(gateTimingEvent).ToArray());

            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive);
            Check(BitConverter.ToInt32(kingFamily.Instructions[17].ArgData, 8) == 0x001713DA,
                "King family player-area check still targets the vanilla region");
            Check(fogFlagEvent.Instructions.Count == 11 && fogFlagEvent.Parameters.Count == 3
                && fogFlagEvent.Instructions[10].Bank == 2004 && fogFlagEvent.Instructions[10].ID == 2,
                "Fog's event-flag write was not removed from event 11510502");
            Check(fogFlagEvent.Parameters[0].InstructionIndex == 4
                && fogFlagEvent.Parameters[1].InstructionIndex == 10
                && fogFlagEvent.Parameters[2].InstructionIndex == 5
                && fogFlagEvent.Parameters[1].SourceStartByte == 0,
                "event 11510502 parameter records were not removed or re-indexed as expected");
            Check(gateFlagEvent.Instructions.Count == 11
                && gateFlagEvent.Instructions[8].Bank == 2003 && gateFlagEvent.Instructions[8].ID == 2
                && gateFlagEvent.Instructions[8].ArgData.SequenceEqual(Convert.FromHexString("B914000001000000"))
                && gateFlagEvent.Instructions[9].Bank == 1001 && gateFlagEvent.Instructions[10].Bank == 1000,
                "Fog transition event did not set the expected flag and retain wait/end order");
            Check(gateAreaTransitionEvent.Instructions.Count == 12
                && gateAreaTransitionEvent.Instructions[9].Bank == 2003 && gateAreaTransitionEvent.Instructions[9].ID == 2
                && gateAreaTransitionEvent.Instructions[9].ArgData.SequenceEqual(Convert.FromHexString("BA14000001000000"))
                && gateAreaTransitionEvent.Instructions[10].Bank == 1001
                && gateAreaTransitionEvent.Instructions[11].Bank == 1000,
                "DLC gate-area transition did not set the expected flag and retain wait/end order");
            Check(fogWatcherEvent.Instructions.Count == 15
                && fogWatcherEvent.Instructions[4].Bank == 3 && fogWatcherEvent.Instructions[4].ID == 18
                && fogWatcherEvent.Instructions[13].Bank == 2003 && fogWatcherEvent.Instructions[13].ID == 18
                && fogWatcherEvent.Instructions[13].ArgData.SequenceEqual(Convert.FromHexString(
                    "10270000F21C000000000000"))
                && fogWatcherEvent.Instructions.Select((instruction, index) => (instruction, index))
                    .Where(item => item.index != 4 && item.index != 13)
                    .All(item => EmevdMerge.SameInstruction(item.instruction, originalFogWatcherBody[item.index])),
                "event 11515381 operations were not reconstructed exactly");
            byte[] reconstructedFlagBody = fogFlagEvent.Instructions.SelectMany(instruction => instruction.ArgData).ToArray();
            EMEVD.Instruction[] reconstructedGateFlagBody = gateFlagEvent.Instructions.ToArray();
            EMEVD.Instruction[] reconstructedGateAreaTransitionBody = gateAreaTransitionEvent.Instructions.ToArray();
            var reconstructedFlagParameters = fogFlagEvent.Parameters
                .Select(parameter => (parameter.InstructionIndex, parameter.TargetStartByte, parameter.SourceStartByte,
                    parameter.ByteCount, parameter.UnkID)).ToArray();
            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive);
            Check(BitConverter.ToInt32(kingFamily.Instructions[17].ArgData, 8) == 0x001713DA,
                "King family event reconstruction is not idempotent");
            Check(fogFlagEvent.Instructions.Count == 11 && fogFlagEvent.Parameters.Count == 3
                && fogFlagEvent.Instructions.SelectMany(instruction => instruction.ArgData).SequenceEqual(reconstructedFlagBody)
                && fogFlagEvent.Parameters.Select(parameter => (parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount, parameter.UnkID)).SequenceEqual(reconstructedFlagParameters),
                "event 11510502 reconstruction is not idempotent");
            Check(gateFlagEvent.Instructions.Count == 11
                && gateFlagEvent.Instructions.Zip(reconstructedGateFlagBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11510230 reconstruction is not idempotent");
            Check(gateAreaTransitionEvent.Instructions.Count == 12
                && gateAreaTransitionEvent.Instructions.Zip(reconstructedGateAreaTransitionBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11510240 reconstruction is not idempotent");
            EMEVD.Instruction[] reconstructedFogWatcherBody = fogWatcherEvent.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive);
            Check(fogWatcherEvent.Instructions.Count == 15
                && fogWatcherEvent.Instructions.Zip(reconstructedFogWatcherBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11515381 reconstruction is not idempotent");
            fogWatcherEvent.Instructions[1] = new EMEVD.Instruction(3, 0,
                Convert.FromHexString("0100000075A4AF00"));
            EMEVD.Instruction[] malformedFogWatcherBody = fogWatcherEvent.Instructions.ToArray();
            bool malformedWatcherRejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive); }
            catch (InvalidDataException) { malformedWatcherRejected = true; }
            Check(malformedWatcherRejected && fogWatcherEvent.Instructions.Zip(malformedFogWatcherBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed event 11515381 body was accepted or mutated");
            Check(stoppedGates.All(evt => IsEndEvent(evt.Instructions[0])), "Painted World vanilla gate events were not stopped");
        }

        private static void KnownModifiedM15DlcFlagEventRejectsMalformedParameters()
        {
            EMEVD.Event kingFamily = new EMEVD.Event(11510460, EMEVD.Event.RestBehaviorType.Default);
            for (int i = 0; i < 40; i++) kingFamily.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            kingFamily.Instructions[17] = new EMEVD.Instruction(3, 2,
                new object[] { (sbyte)3, (byte)0, 10000, 0x001713DB });
            EMEVD.Event malformedFlagEvent = M15DlcFogFlagSourceEvent();
            malformedFlagEvent.Parameters[3] = new EMEVD.Parameter(10, 0, 0, 4);
            EMEVD.Event[] stoppedGates =
            {
                TerminatedSourceEvent(11510131, 8, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11515390, 6, EMEVD.Event.RestBehaviorType.Default),
            };
            EMEVD.Event objectSfxEvent = M15DlcEvent11510001SourceEvent();
            EMEVD.Event gateTimingEvent = M15DlcEvent11510900SourceEvent();

            bool rejected = false;
            try
            {
                FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(
                    Evd(new[] { kingFamily, malformedFlagEvent, M15DlcGateFlagSourceEvent(), M15DlcGateAreaTransitionSourceEvent(),
                        M15DlcEvent11515381SourceEvent(), objectSfxEvent, M15DlcEvent11510543SourceEvent(), gateTimingEvent }
                        .Concat(stoppedGates).ToArray()));
            }
            catch (InvalidDataException) { rejected = true; }

            Check(rejected, "unexpected event 11510502 parameter metadata was accepted");
            Check(malformedFlagEvent.Instructions.Count == 12 && malformedFlagEvent.Parameters.Count == 4,
                "event 11510502 was mutated before its parameter contract was validated");
        }

        private static void M15DlcEvent11510001ObjectSfxSequenceRemoved()
        {
            EMEVD.Event source = M15DlcEvent11510001SourceEvent();
            EMEVD.Instruction[] expectedBody = source.Instructions.Take(28).Concat(source.Instructions.Skip(34)).ToArray();
            EMEVD archive = M15DlcArchiveWithObjectSfxEvent(source);
            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive);
            Check(source.Instructions.Count == 34 && source.Instructions.Zip(expectedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11510001 changed operations beyond its exact object/SFX sequence");

            EMEVD.Instruction[] reconstructedBody = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive);
            Check(source.Instructions.Count == reconstructedBody.Length && source.Instructions.Zip(reconstructedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11510001 object/SFX reconstruction is not idempotent");

            EMEVD.Event malformed = M15DlcEvent11510001SourceEvent();
            malformed.Instructions[30] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x0017123A, (byte)0 });
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(M15DlcArchiveWithObjectSfxEvent(malformed)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed event 11510001 object/SFX sequence was accepted or mutated");
        }

        private static void M15DlcEvent11510543SfxSequenceRemoved()
        {
            EMEVD archive = M15DlcArchiveWithObjectSfxEvent(M15DlcEvent11510001SourceEvent());
            EMEVD.Event evt = archive.Events.Single(eventItem => eventItem.ID == 11510543);
            EMEVD.Instruction[] expectedBody = new[] { evt.Instructions[6] }.Concat(evt.Instructions.Skip(13)).ToArray();
            EMEVD.Parameter[] expectedParameters = M15DlcEvent11510543ExpectedParameters();
            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive);
            Check(evt.Instructions.Count == 14
                && evt.Instructions.Zip(expectedBody).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(evt.Parameters, expectedParameters),
                "M15 DLC event 11510543 did not remove only its exact sound-effect operations");

            EMEVD.Instruction[] body = evt.Instructions.ToArray();
            EMEVD.Parameter[] parameters = evt.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive);
            Check(evt.Instructions.Count == body.Length
                && evt.Instructions.Zip(body).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(evt.Parameters, parameters),
                "M15 DLC event 11510543 reconstruction is not idempotent");

            EMEVD.Event malformed = M15DlcEvent11510543SourceEvent();
            malformed.Parameters[0] = new EMEVD.Parameter(19, 1, 0, 4);
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            EMEVD.Parameter[] malformedParameters = malformed.Parameters
                .Select(parameter => new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }).ToArray();
            EMEVD.Event[] malformedEvents = archive.Events.Where(eventItem => eventItem.ID != 11510543)
                .Append(malformed).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(Evd(malformedEvents)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected
                && malformed.Instructions.Zip(malformedBody).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && ParametersMatch(malformed.Parameters, malformedParameters),
                "malformed M15 DLC event 11510543 parameter metadata was accepted or mutated");
        }

        private static void M15DlcEvent11510900BodyReconstructed()
        {
            EMEVD.Event source = M15DlcEvent11510900SourceEvent();
            EMEVD.Instruction[] expectedBody = source.Instructions.Take(9)
                .Append(InsHex(1001, 1, "01000000"))
                .Concat(source.Instructions.Skip(11).Take(4))
                .Concat(source.Instructions.Skip(17)).ToArray();
            EMEVD archive = M15DlcArchiveWithObjectSfxEvent(M15DlcEvent11510001SourceEvent(), source);

            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive);
            Check(source.Instructions.Count == 20 && source.Instructions.Zip(expectedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11510900 did not apply only its exact command, timing, and object/SFX edits");

            EMEVD.Instruction[] reconstructedBody = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(archive);
            Check(source.Instructions.Count == reconstructedBody.Length && source.Instructions.Zip(reconstructedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11510900 reconstruction is not idempotent");

            EMEVD.Event malformed = M15DlcEvent11510900SourceEvent();
            malformed.Instructions[10] = InsHex(1001, 1, "05000000");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            bool rejected = false;
            try
            {
                FogModifiedEventBuilder.ReconstructKnownM15DlcEvents(
                    M15DlcArchiveWithObjectSfxEvent(M15DlcEvent11510001SourceEvent(), malformed));
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed event 11510900 body was accepted or mutated");
        }

        private static void M15Event11500210BodyReconstructed()
        {
            EMEVD.Event source = M15Event11500210SourceEvent();
            EMEVD.Instruction[] expectedBody = source.Instructions.Take(7)
                .Concat(new[] { InsHex(2003, 2, "B814000001000000"), source.Instructions[12], source.Instructions[14] }).ToArray();
            EMEVD archive = M15MapSourceArchive(source);

            FogModifiedEventBuilder.ReconstructKnownM15Events(archive);
            Check(source.Instructions.Count == expectedBody.Length && source.Instructions.Zip(expectedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M15 event 11500210 differs from its exact reference body");
            EMEVD.Instruction[] reconstructedBody = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM15Events(archive);
            Check(source.Instructions.Count == reconstructedBody.Length && source.Instructions.Zip(reconstructedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M15 event 11500210 reconstruction is not idempotent");

            EMEVD.Event malformed = M15Event11500210SourceEvent();
            malformed.Instructions[2] = InsHex(1, 0, "0000000000008040");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM15Events(M15MapSourceArchive(malformed)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M15 event 11500210 was accepted or partially mutated");
        }

        private static EMEVD M15MapSourceArchive(EMEVD.Event progressionEvent)
            => Evd(progressionEvent,
                ObjectSfxSourceEvent(11500001, 17, EMEVD.Event.RestBehaviorType.Default, 0x0016EB26, 0, 0x0016EB27),
                TerminatedSourceEvent(11505390, 6, EMEVD.Event.RestBehaviorType.Default));

        private static EMEVD.Event M15Event11500210SourceEvent() => Evt(11500210,
            InsHex(1003, 6, "00010000"),
            InsHex(3, 8, "00010F00"),
            InsHex(1, 0, "000000000000A040"),
            InsHex(3, 0, "000100000B000000"),
            InsHex(3, 5, "000000000100000029ED160000000000FFFF00000000000008BE98003000000000000000"),
            InsHex(2004, 29, "29E4160001000000"),
            InsHex(1001, 1, "01000000"),
            InsHex(1003, 1, "0201000080A2AF00"),
            InsHex(2002, 4, "F04902000000000025ED16000F01000010270000"),
            InsHex(1000, 3, "02000000"),
            InsHex(2003, 19, "0F000200"),
            InsHex(2002, 4, "F24902000000000025ED16000F01000010270000"),
            InsHex(1001, 1, "01000000"),
            InsHex(2003, 28, "21000000"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD M15DlcArchiveWithObjectSfxEvent(EMEVD.Event objectSfxEvent)
            => M15DlcArchiveWithObjectSfxEvent(objectSfxEvent, M15DlcEvent11510900SourceEvent());

        private static EMEVD M15DlcArchiveWithObjectSfxEvent(EMEVD.Event objectSfxEvent, EMEVD.Event gateTimingEvent)
        {
            EMEVD.Event kingFamily = TerminatedSourceEvent(11510460, 40, EMEVD.Event.RestBehaviorType.Default);
            kingFamily.Instructions[17] = new EMEVD.Instruction(3, 2,
                new object[] { (sbyte)3, (byte)0, 10000, 0x001713DB });
            return Evd(kingFamily,
                TerminatedSourceEvent(11510131, 8, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11515390, 6, EMEVD.Event.RestBehaviorType.Default),
                M15DlcFogFlagSourceEvent(), M15DlcGateFlagSourceEvent(), M15DlcGateAreaTransitionSourceEvent(),
                M15DlcEvent11515381SourceEvent(), objectSfxEvent, M15DlcEvent11510543SourceEvent(), gateTimingEvent);
        }

        private static EMEVD.Event M15DlcEvent11510001SourceEvent()
        {
            EMEVD.Event evt = TerminatedSourceEvent(11510001, 40, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[28] = new EMEVD.Instruction(2005, 3, new object[] { 0x00171236, (byte)0 });
            evt.Instructions[29] = new EMEVD.Instruction(2006, 1, new object[] { 0x00171237, (byte)1 });
            evt.Instructions[30] = new EMEVD.Instruction(2005, 3, new object[] { 0x00171238, (byte)0 });
            evt.Instructions[31] = new EMEVD.Instruction(2006, 1, new object[] { 0x00171239, (byte)1 });
            evt.Instructions[32] = new EMEVD.Instruction(2005, 3, new object[] { 0x00171234, (byte)0 });
            evt.Instructions[33] = new EMEVD.Instruction(2006, 1, new object[] { 0x00171235, (byte)1 });
            return evt;
        }

        private static EMEVD.Event M15DlcEvent11510543SourceEvent()
        {
            EMEVD.Event evt = Evt(11510543,
                InsHex(2005, 3, "4611170000000000"),
                InsHex(2006, 1, "4711170000000000"),
                InsHex(2005, 3, "4811170000000000"),
                InsHex(2006, 1, "4911170000000000"),
                InsHex(2005, 3, "4A11170000000000"),
                InsHex(2006, 1, "4B11170000000000"),
                InsHex(3, 0, "00010000ACA3AF00"),
                InsHex(2005, 3, "4611170001000000"),
                InsHex(2006, 2, "47111700"),
                InsHex(2005, 3, "4811170001000000"),
                InsHex(2006, 2, "49111700"),
                InsHex(2005, 3, "4A11170001000000"),
                InsHex(2006, 2, "4B111700"),
                InsHex(2003, 2, "A81F000000000000"),
                InsHex(2003, 22, "000000000000000000000000"),
                InsHex(2003, 2, "0000000001000000"),
                InsHex(2004, 5, "0C0C170000000000"),
                InsHex(2004, 5, "0D0C170000000000"),
                InsHex(2004, 5, "640C170000000000"),
                InsHex(2004, 5, "0000000001000000"),
                InsHex(2004, 5, "5A19000001000000"),
                InsHex(2004, 5, "6419000001000000"),
                InsHex(1003, 1, "02010000A51F0000"),
                InsHex(2003, 2, "A51F000001000000"),
                InsHex(1000, 4, "00000000"),
                InsHex(2003, 2, "A41F000001000000"));
            evt.Parameters.AddRange(new[]
            {
                new EMEVD.Parameter(19, 0, 0, 4),
                new EMEVD.Parameter(14, 0, 4, 4),
                new EMEVD.Parameter(14, 4, 8, 4),
                new EMEVD.Parameter(15, 0, 12, 4),
            });
            return evt;
        }

        private static EMEVD.Parameter[] M15DlcEvent11510543ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(7, 0, 0, 4),
            new EMEVD.Parameter(2, 0, 4, 4),
            new EMEVD.Parameter(2, 4, 8, 4),
            new EMEVD.Parameter(3, 0, 12, 4),
        };

        private static EMEVD.Event M13MapInitializerSourceEvent()
        {
            EMEVD.Event evt = TerminatedSourceEvent(50, 18, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[2] = InsHex(2000, 0, "000000004580AC0000000000");
            evt.Instructions[4] = InsHex(2000, 0, "000000000684AC004280AC0096190000");
            return evt;
        }

        private static EMEVD.Event MapEventInitializerSourceEvent(int instructionCount, string[] initializers)
        {
            EMEVD.Event evt = TerminatedSourceEvent(50, instructionCount, EMEVD.Event.RestBehaviorType.Default);
            for (int index = 0; index < initializers.Length; index++)
                evt.Instructions[index * 2] = InsHex(2000, 0, initializers[index]);
            return evt;
        }

        private static EMEVD.Event M15DlcEvent11510900SourceEvent() => Evt(11510900,
            InsHex(4, 2, "00000000FA0C17000500000000000000"),
            InsHex(1001, 1, "01000000"),
            InsHex(1003, 5, "07010000"),
            InsHex(2003, 2, "FDB5AF0001000000"),
            InsHex(3, 0, "00010000FDB5AF00"),
            InsHex(1003, 1, "0201000080A2AF00"),
            InsHex(2002, 4, "884A020000000000C11517000F01000010270000"),
            InsHex(1000, 3, "01000000"),
            InsHex(2002, 4, "8D4A020000000000C11517000F01000010270000"),
            InsHex(2003, 2, "CF0F000001000000"),
            InsHex(1001, 1, "0A000000"),
            InsHex(1003, 1, "010000000C000000"),
            InsHex(2003, 2, "7800000001000000"),
            InsHex(2003, 2, "74A4AF0001000000"),
            InsHex(2003, 12, "FA0C1700"),
            InsHex(2005, 3, "D211170000000000"),
            InsHex(2006, 1, "D311170001000000"),
            InsHex(1003, 6, "00010000"),
            InsHex(2003, 2, "C229B40000000000"),
            InsHex(2003, 2, "CC29B40000000000"),
            InsHex(2003, 2, "D629B40000000000"),
            InsHex(2003, 2, "FE29B40000000000"),
            InsHex(2003, 28, "27000000"));

        private static EMEVD.Event M12DlcEvent11210401SourceEvent() => Evt(11210401,
            InsHex(2000, 2, "00000000"),
            InsHex(3, 6, "01020000"),
            InsHex(3, 10, "010000000022AB000522AB000400000002000000"),
            InsHex(4, 5, "01000000102700000512000001000000"),
            InsHex(3, 0, "01000000EC21AB00"),
            InsHex(0, 0, "00010100"),
            InsHex(1001, 0, "0000A040"),
            InsHex(1003, 2, "01010000EC21AB00"),
            InsHex(1003, 6, "01030000"),
            InsHex(2007, 4, "F049020001000000"),
            InsHex(1001, 0, "0000A040"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Event M12DlcEvent11210836SourceEvent() => Evt(11210836,
            InsHex(3, 0, "000100002422AB00"),
            InsHex(2003, 2, "2422AB0001000000"),
            InsHex(2007, 4, "5E4A020000000000"),
            InsHex(3, 0, "000100001F22AB00"),
            InsHex(2003, 2, "1F22AB0001000000"),
            InsHex(2007, 4, "1C4B020000000000"),
            InsHex(3, 0, "000000001F22AB00"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Event M12DlcEvent11210831SourceEvent()
        {
            EMEVD.Event evt = Evt(11210831,
                InsHex(3, 6, "01000000"),
                InsHex(3, 0, "010100001E22AB00"),
                InsHex(3, 0, "0100000000000000"),
                InsHex(3, 0, "0101000000000000"),
                InsHex(0, 0, "00010100"),
                InsHex(1003, 1, "020100002A22AB00"),
                InsHex(1003, 1, "010100002022AB00"),
                InsHex(2003, 2, "0000000000000000"),
                InsHex(1000, 4, "01000000"));
            evt.Parameters.AddRange(new[]
            {
                new EMEVD.Parameter(2, 4, 0, 4),
                new EMEVD.Parameter(3, 4, 4, 4),
                new EMEVD.Parameter(7, 0, 4, 4),
            });
            return evt;
        }

        private static EMEVD.Event M12DlcEvent11210434SourceEvent() => Evt(11210434,
            InsHex(2003, 2, "2722AB0001000000"),
            InsHex(1000, 4, "00000000"),
            InsHex(3, 0, "010100001E22AB00"),
            InsHex(3, 0, "010100003022AB00"),
            InsHex(3, 1, "FE020000C421AB00C921AB00"),
            InsHex(3, 1, "FE020000D021AB00D521AB00"),
            InsHex(0, 0, "0101FE00"),
            InsHex(3, 0, "020100001E22AB00"),
            InsHex(3, 0, "FF0100002022AB00"),
            InsHex(3, 0, "FF0100002322AB00"),
            InsHex(0, 0, "0201FF00"),
            InsHex(3, 1, "FD020000C421AB00C921AB00"),
            InsHex(3, 1, "FD020000CA21AB00CF21AB00"),
            InsHex(3, 1, "FD020000D021AB00D521AB00"),
            InsHex(3, 1, "FD020000D621AB00DB21AB00"),
            InsHex(0, 0, "0201FD00"),
            InsHex(3, 0, "030100001E22AB00"),
            InsHex(3, 0, "FB0100002A22AB00"),
            InsHex(3, 0, "FB0100002D22AB00"),
            InsHex(0, 0, "0301FF00"),
            InsHex(3, 1, "FB020000C421AB00C921AB00"),
            InsHex(3, 1, "FB020000CA21AB00CF21AB00"),
            InsHex(3, 1, "FB020000D021AB00D521AB00"),
            InsHex(3, 1, "FB020000D621AB00DB21AB00"),
            InsHex(3, 1, "FB020000DC21AB00E121AB00"),
            InsHex(3, 1, "FB020000E221AB00E721AB00"),
            InsHex(0, 0, "0301FB00"),
            InsHex(0, 0, "F9010100"),
            InsHex(0, 0, "F9010200"),
            InsHex(0, 0, "F9010300"),
            InsHex(0, 0, "0001F900"),
            InsHex(2003, 2, "2722AB0001000000"));

        private static EMEVD.Instruction[] M12DlcEvent11210434ExpectedBody()
        {
            EMEVD.Instruction[] source = M12DlcEvent11210434SourceEvent().Instructions.ToArray();
            return source.Take(3)
                .Concat(new[]
                {
                    InsHex(3, 0, "010000002022AB00"),
                    InsHex(3, 0, "010000002322AB00"),
                })
                .Concat(source.Skip(4).Take(12))
                .Concat(source.Skip(27).Take(2))
                .Concat(source.Skip(30)).ToArray();
        }

        private static EMEVD.Event M12DlcEvent11210840SourceEvent()
        {
            EMEVD.Event evt = Evt(11210840,
                InsHex(3, 0, "0101000000000000"),
                InsHex(3, 0, "010100001E22AB00"),
                InsHex(3, 0, "010000001F22AB00"),
                InsHex(4, 14, "01000000102700000000000001000000"),
                InsHex(0, 0, "00010100"),
                InsHex(2004, 15, "1027000001000000"),
                InsHex(2004, 8, "10270000915F0100"),
                InsHex(2004, 8, "1027000065000000"),
                InsHex(2004, 8, "1027000066000000"),
                InsHex(2004, 8, "1027000067000000"),
                InsHex(2004, 8, "1027000068000000"),
                InsHex(2004, 8, "1027000003120000"),
                InsHex(1003, 1, "0100000000000000"),
                InsHex(2007, 4, "FBB8070003000000"),
                InsHex(2003, 2, "0000000001000000"),
                InsHex(2003, 2, "0000000001000000"),
                InsHex(3, 0, "0000000000000000"),
                InsHex(2003, 2, "0000000000000000"),
                InsHex(1003, 1, "0101000000000000"),
                InsHex(2004, 15, "1027000000000000"),
                InsHex(2004, 21, "1027000003120000"),
                InsHex(2004, 43, "1027000001000000"),
                InsHex(1003, 1, "0201000000000000"),
                InsHex(2003, 44, "10270000271B000000010000"),
                InsHex(1000, 3, "01000000"),
                InsHex(2003, 44, "10270000271B000000010000"),
                InsHex(2004, 43, "1027000001000000"),
                InsHex(2003, 2, "0000000000000000"),
                InsHex(1000, 4, "01000000"));
            evt.Parameters.AddRange(new[]
            {
                new EMEVD.Parameter(0, 4, 0, 4),
                new EMEVD.Parameter(12, 4, 0, 4),
                new EMEVD.Parameter(18, 4, 0, 4),
                new EMEVD.Parameter(22, 4, 0, 4),
                new EMEVD.Parameter(14, 0, 4, 4),
                new EMEVD.Parameter(27, 0, 4, 4),
                new EMEVD.Parameter(15, 0, 8, 4),
                new EMEVD.Parameter(16, 4, 8, 4),
                new EMEVD.Parameter(17, 0, 8, 4),
            });
            return evt;
        }

        private static EMEVD.Instruction[] M12DlcEvent11210840ExpectedBody() => new[]
        {
            InsHex(3, 0, "0101000000000000"),
            InsHex(3, 0, "010100001E22AB00"),
            InsHex(3, 0, "010000001F22AB00"),
            InsHex(4, 14, "01000000102700000000000001000000"),
            InsHex(0, 0, "00010100"),
            InsHex(2004, 15, "1027000001000000"),
            InsHex(2004, 8, "1027000065000000"),
            InsHex(2004, 8, "1027000066000000"),
            InsHex(2004, 8, "1027000067000000"),
            InsHex(2004, 8, "1027000068000000"),
            InsHex(2004, 8, "1027000003120000"),
            InsHex(1003, 1, "0201000000000000"),
            InsHex(2007, 4, "0000000001000000"),
            InsHex(1000, 3, "01000000"),
            InsHex(2007, 4, "074A020001000000"),
            InsHex(2003, 31, "00000000060000003F000000"),
            InsHex(1003, 1, "010000002022AB00"),
            InsHex(2003, 31, "00000000060000003F000000"),
            InsHex(1001, 0, "00004040"),
            InsHex(2003, 2, "0000000001000000"),
            InsHex(2003, 2, "0000000001000000"),
            InsHex(3, 0, "0000000000000000"),
            InsHex(2003, 2, "0000000000000000"),
            InsHex(1003, 1, "0101000000000000"),
            InsHex(2004, 15, "1027000000000000"),
            InsHex(2004, 21, "1027000003120000"),
            InsHex(2004, 43, "1027000001000000"),
            InsHex(2003, 18, "10270000261B000000010000"),
            InsHex(2003, 2, "0000000000000000"),
            InsHex(1000, 4, "01000000"),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210840ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(0, 4, 0, 4),
            new EMEVD.Parameter(11, 4, 0, 4),
            new EMEVD.Parameter(23, 4, 0, 4),
            new EMEVD.Parameter(19, 0, 4, 4),
            new EMEVD.Parameter(28, 0, 4, 4),
            new EMEVD.Parameter(20, 0, 8, 4),
            new EMEVD.Parameter(21, 4, 8, 4),
            new EMEVD.Parameter(22, 0, 8, 4),
            new EMEVD.Parameter(15, 0, 12, 4),
            new EMEVD.Parameter(17, 0, 16, 4),
            new EMEVD.Parameter(12, 0, 20, 4),
        };

        private static EMEVD.Event M12DlcEvent11210850SourceEvent()
        {
            EMEVD.Event evt = Evt(11210850,
                InsHex(2000, 2, "00000000"),
                InsHex(3, 0, "0101000000000000"),
                InsHex(3, 0, "0101000000000000"),
                InsHex(3, 2, "010100001027000000000000"),
                InsHex(0, 0, "00010100"),
                InsHex(2004, 1, "1027000000000000"),
                InsHex(1001, 0, "00000040"),
                InsHex(1003, 1, "030100002022AB00"),
                InsHex(1003, 1, "020100002A22AB00"),
                InsHex(2002, 6, "42D501000200000000000000000000000C010000"),
                InsHex(1000, 3, "01000000"),
                InsHex(2002, 7, "42D501000200000000000000000000000C010000"),
                InsHex(1001, 1, "01000000"),
                InsHex(2004, 1, "1027000001000000"),
                InsHex(2004, 47, ""),
                InsHex(1003, 1, "010100003022AB00"),
                InsHex(2004, 51, "01000000"),
                InsHex(2003, 2, "0000000000000000"),
                InsHex(1001, 0, "0000C040"),
                InsHex(2004, 21, "10270000915F0100"),
                InsHex(2004, 15, "1027000000000000"),
                InsHex(1000, 4, "01000000"));
            evt.Parameters.AddRange(new[]
            {
                new EMEVD.Parameter(1, 4, 0, 4),
                new EMEVD.Parameter(2, 4, 4, 4),
                new EMEVD.Parameter(17, 0, 4, 4),
                new EMEVD.Parameter(3, 8, 8, 4),
                new EMEVD.Parameter(9, 8, 12, 4),
                new EMEVD.Parameter(11, 8, 12, 4),
                new EMEVD.Parameter(9, 12, 16, 4),
                new EMEVD.Parameter(11, 12, 16, 4),
            });
            return evt;
        }

        private static EMEVD.Instruction[] M12DlcEvent11210850ExpectedBody() => new[]
        {
            InsHex(2000, 2, "00000000"),
            InsHex(3, 0, "0101000000000000"),
            InsHex(3, 0, "0101000000000000"),
            InsHex(3, 2, "010100001027000000000000"),
            InsHex(0, 0, "00010100"),
            InsHex(2002, 2, "42D5010002000000000000000C010000"),
            InsHex(1001, 1, "01000000"),
            InsHex(2004, 47, ""),
            InsHex(2003, 2, "0000000000000000"),
            InsHex(1000, 4, "01000000"),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210850ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(1, 4, 0, 4),
            new EMEVD.Parameter(2, 4, 4, 4),
            new EMEVD.Parameter(8, 0, 4, 4),
            new EMEVD.Parameter(3, 8, 8, 4),
            new EMEVD.Parameter(5, 8, 12, 4),
        };

        private static EMEVD.Event M12DlcEvent11210800SourceEvent()
        {
            EMEVD.Event evt = Evt(11210800,
                InsHex(2000, 2, "00000000"),
                InsHex(3, 2, "010100001027000000000000"),
                InsHex(3, 0, "010000001E22AB00"),
                InsHex(0, 0, "00010100"),
                InsHex(2008, 4, ""),
                InsHex(2003, 22, "F621AB00FB21AB0000000000"),
                InsHex(2003, 2, "0000000001000000"),
                InsHex(2004, 1, "1027000000000000"),
                InsHex(2004, 50, ""),
                InsHex(2004, 47, ""),
                InsHex(2004, 51, "00000000"),
                InsHex(3, 0, "0000000000000000"),
                InsHex(1000, 4, "01000000"));
            evt.Parameters.AddRange(new[]
            {
                new EMEVD.Parameter(1, 8, 0, 4),
                new EMEVD.Parameter(6, 0, 4, 4),
                new EMEVD.Parameter(11, 4, 4, 4),
                new EMEVD.Parameter(10, 0, 8, 4),
            });
            return evt;
        }

        private static EMEVD.Instruction[] M12DlcEvent11210800ExpectedBody() => new[]
        {
            InsHex(2000, 2, "00000000"),
            InsHex(3, 2, "000100001027000000000000"),
            InsHex(2003, 22, "F621AB00F921AB0000000000"),
            InsHex(2003, 2, "0000000001000000"),
            InsHex(3, 0, "0000000000000000"),
            InsHex(1000, 4, "01000000"),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210800ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(1, 8, 0, 4),
            new EMEVD.Parameter(3, 0, 4, 4),
            new EMEVD.Parameter(4, 4, 4, 4),
        };

        private static EMEVD.Event M12DlcEvent11210430SourceEvent()
        {
            EMEVD.Event evt = Evt(11210430,
                InsHex(3, 6, "01020000"),
                InsHex(3, 0, "FF0000002422AB00"),
                InsHex(3, 0, "FF0100002522AB00"),
                InsHex(0, 0, "0101FF00"),
                InsHex(3, 0, "010100001E22AB00"),
                InsHex(3, 0, "010000001324AB00"),
                InsHex(3, 0, "0101000000000000"),
                InsHex(3, 0, "0100000000000000"),
                InsHex(3, 0, "020000002322AB00"),
                InsHex(3, 0, "020000002D22AB00"),
                InsHex(3, 0, "020100003022AB00"),
                InsHex(3, 0, "0201000000000000"),
                InsHex(3, 0, "0201000000000000"),
                InsHex(3, 0, "FC0100002022AB00"),
                InsHex(3, 0, "FC0100002A22AB00"),
                InsHex(0, 0, "0301FC00"),
                InsHex(3, 0, "0301000000000000"),
                InsHex(3, 0, "0301000000000000"),
                InsHex(0, 0, "FE010300"),
                InsHex(0, 0, "FE010200"),
                InsHex(0, 0, "0101FE00"),
                InsHex(3, 0, "FD0100002722AB00"),
                InsHex(3, 0, "FD0100002522AB00"),
                InsHex(0, 0, "0101FD00"),
                InsHex(0, 0, "00010100"),
                InsHex(2004, 8, "1027000006120000"),
                InsHex(1001, 0, "0000A040"),
                InsHex(1000, 4, "01000000"));
            evt.Parameters.AddRange(new[]
            {
                new EMEVD.Parameter(6, 4, 0, 4),
                new EMEVD.Parameter(11, 4, 4, 4),
                new EMEVD.Parameter(12, 4, 8, 4),
                new EMEVD.Parameter(16, 4, 12, 4),
                new EMEVD.Parameter(17, 4, 16, 4),
                new EMEVD.Parameter(7, 4, 20, 4),
            });
            return evt;
        }

        private static EMEVD.Instruction[] M12DlcEvent11210430ExpectedBody() => new[]
        {
            InsHex(3, 0, "FF0000002422AB00"),
            InsHex(3, 0, "FF0100002522AB00"),
            InsHex(0, 0, "0101FF00"),
            InsHex(3, 0, "010100001E22AB00"),
            InsHex(3, 0, "0101000000000000"),
            InsHex(3, 0, "020000002322AB00"),
            InsHex(3, 20, "0200000000000000060000000000000006000000"),
            InsHex(0, 0, "01010200"),
            InsHex(3, 0, "FD0100002722AB00"),
            InsHex(3, 0, "FD0100002522AB00"),
            InsHex(0, 0, "0101FD00"),
            InsHex(0, 0, "00010100"),
            InsHex(2004, 8, "1027000006120000"),
            InsHex(1001, 0, "0000A040"),
            InsHex(1000, 4, "01000000"),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210430ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(4, 4, 0, 4),
            new EMEVD.Parameter(6, 4, 4, 4),
            new EMEVD.Parameter(6, 12, 8, 4),
        };

        private static EMEVD.Event M12DlcEvent11210701SourceEvent()
        {
            EMEVD.Event evt = Evt(11210701,
                InsHex(3, 6, "01020000"),
                InsHex(3, 0, "FF0000002422AB00"),
                InsHex(3, 0, "FF0100002522AB00"),
                InsHex(0, 0, "0101FF00"),
                InsHex(3, 0, "010100001E22AB00"),
                InsHex(3, 0, "010000001324AB00"),
                InsHex(3, 0, "0101000000000000"),
                InsHex(3, 0, "0100000000000000"),
                InsHex(3, 0, "020000003022AB00"),
                InsHex(3, 0, "020000002322AB00"),
                InsHex(3, 0, "020000002D22AB00"),
                InsHex(3, 0, "0200000000000000"),
                InsHex(3, 0, "0201000000000000"),
                InsHex(3, 0, "FB0100002322AB00"),
                InsHex(3, 0, "FB0100002D22AB00"),
                InsHex(3, 0, "FB0100003022AB00"),
                InsHex(0, 0, "0301FB00"),
                InsHex(3, 0, "0301000000000000"),
                InsHex(1003, 1, "01010000F621AB00"),
                InsHex(3, 0, "03000000AC2DAB00"),
                InsHex(1003, 1, "01010000F821AB00"),
                InsHex(3, 0, "03000000AE2DAB00"),
                InsHex(1003, 1, "0A0100003022AB00"),
                InsHex(1003, 1, "01010000F721AB00"),
                InsHex(3, 0, "03000000AD2DAB00"),
                InsHex(1003, 1, "01010000F921AB00"),
                InsHex(3, 0, "03000000AF2DAB00"),
                InsHex(1003, 1, "050100003022AB00"),
                InsHex(1003, 1, "040100002322AB00"),
                InsHex(1003, 1, "01010000FA21AB00"),
                InsHex(3, 0, "03000000B02DAB00"),
                InsHex(1003, 1, "01010000FB21AB00"),
                InsHex(3, 0, "03000000B12DAB00"),
                InsHex(0, 0, "FE010200"),
                InsHex(0, 0, "FE010300"),
                InsHex(0, 0, "0101FE00"),
                InsHex(3, 0, "04010000EE21AB00"),
                InsHex(3, 0, "05010000EF21AB00"),
                InsHex(3, 0, "06010000F021AB00"),
                InsHex(3, 0, "07010000F121AB00"),
                InsHex(0, 0, "00010100"),
                InsHex(1000, 7, "01000400"),
                InsHex(2004, 8, "1027000007120000"),
                InsHex(1000, 7, "01000500"),
                InsHex(2004, 8, "1027000008120000"),
                InsHex(1000, 7, "01000600"),
                InsHex(2004, 8, "1027000009120000"),
                InsHex(1000, 7, "01000700"),
                InsHex(2004, 8, "102700000A120000"),
                InsHex(1000, 7, "04010400"),
                InsHex(1000, 7, "03010500"),
                InsHex(1000, 7, "02010600"),
                InsHex(1000, 7, "01010700"),
                InsHex(2004, 8, "1027000004120000"),
                InsHex(1001, 0, "0000A040"),
                InsHex(1000, 4, "01000000"));
            evt.Parameters.AddRange(new[]
            {
                new EMEVD.Parameter(6, 4, 0, 4),
                new EMEVD.Parameter(17, 4, 4, 4),
                new EMEVD.Parameter(12, 4, 8, 4),
                new EMEVD.Parameter(11, 4, 12, 4),
                new EMEVD.Parameter(7, 4, 16, 4),
            });
            return evt;
        }

        private static EMEVD.Instruction[] M12DlcEvent11210701ExpectedBody() => new[]
        {
            InsHex(3, 0, "FF0000002422AB00"),
            InsHex(3, 0, "FF0100002522AB00"),
            InsHex(0, 0, "0101FF00"),
            InsHex(3, 0, "010100001E22AB00"),
            InsHex(3, 0, "0101000000000000"),
            InsHex(3, 0, "020000002322AB00"),
            InsHex(3, 20, "0200000000000000060300000000000006000000"),
            InsHex(3, 0, "030100002322AB00"),
            InsHex(3, 20, "0300000000000000060300000000000006000000"),
            InsHex(3, 20, "0300000000000000060300000000000006000000"),
            InsHex(3, 20, "0300000000000000060300000000000006000000"),
            InsHex(0, 0, "FE010200"),
            InsHex(0, 0, "FE010300"),
            InsHex(0, 0, "0101FE00"),
            InsHex(3, 0, "04010000EE21AB00"),
            InsHex(3, 0, "05010000EF21AB00"),
            InsHex(3, 0, "06010000F021AB00"),
            InsHex(3, 0, "07010000F121AB00"),
            InsHex(0, 0, "00010100"),
            InsHex(1000, 7, "01000400"),
            InsHex(2004, 8, "1027000007120000"),
            InsHex(1000, 7, "01000500"),
            InsHex(2004, 8, "1027000008120000"),
            InsHex(1000, 7, "01000600"),
            InsHex(2004, 8, "1027000009120000"),
            InsHex(1000, 7, "01000700"),
            InsHex(2004, 8, "102700000A120000"),
            InsHex(1000, 7, "04010400"),
            InsHex(1000, 7, "03010500"),
            InsHex(1000, 7, "02010600"),
            InsHex(1000, 7, "01010700"),
            InsHex(2004, 8, "1027000004120000"),
            InsHex(1001, 0, "0000A040"),
            InsHex(1000, 4, "01000000"),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210701ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(4, 4, 0, 4),
            new EMEVD.Parameter(6, 4, 4, 4),
            new EMEVD.Parameter(8, 4, 4, 4),
            new EMEVD.Parameter(9, 4, 4, 4),
            new EMEVD.Parameter(10, 4, 4, 4),
            new EMEVD.Parameter(6, 12, 8, 4),
            new EMEVD.Parameter(8, 12, 8, 4),
            new EMEVD.Parameter(9, 12, 12, 4),
            new EMEVD.Parameter(10, 12, 16, 4),
        };

        private static EMEVD.Event M12DlcEvent11210890SourceEvent() => Evt(11210890,
            InsHex(2000, 2, "00000000"),
            InsHex(3, 0, "010100001E22AB00"),
            InsHex(3, 0, "010000001F22AB00"),
            InsHex(3, 0, "01000000D21F0000"),
            InsHex(3, 6, "01030000"),
            InsHex(0, 0, "00010100"),
            InsHex(2004, 21, "1027000004120000"),
            InsHex(2004, 21, "1027000006120000"),
            InsHex(2004, 21, "1027000007120000"),
            InsHex(2004, 21, "1027000008120000"),
            InsHex(2004, 21, "1027000009120000"),
            InsHex(2004, 21, "102700000A120000"),
            InsHex(1003, 1, "03010000E12CAB00"),
            InsHex(1003, 1, "02010000CC1F0000"),
            InsHex(1003, 1, "01010000F621AB00"),
            InsHex(2007, 4, "184A020001000000"),
            InsHex(1001, 0, "00004040"),
            InsHex(2003, 40, ""),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Instruction[] M12DlcEvent11210890ExpectedBody() => new[]
        {
            InsHex(2000, 2, "00000000"),
            InsHex(3, 0, "010100001E22AB00"),
            InsHex(3, 6, "01030000"),
            InsHex(3, 2, "010100001027000000000000"),
            InsHex(0, 0, "00010100"),
            InsHex(1001, 0, "00004040"),
            InsHex(2003, 14, "0C010000FFFFFFFF"),
            InsHex(1001, 0, "00004040"),
            InsHex(1000, 4, "01000000"),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210890ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(3, 8, 0, 4),
            new EMEVD.Parameter(6, 4, 4, 4),
        };

        private static EMEVD.Event M12DlcEvent11210404SourceEvent()
        {
            EMEVD.Event evt = Evt(11210404,
                InsHex(2000, 2, "00000000"),
                InsHex(3, 0, "06010000EC21AB00"),
                InsHex(3, 0, "0601000000000000"),
                InsHex(3, 0, "0601000000000000"),
                InsHex(0, 0, "00010600"),
                InsHex(3, 12, "01000000000000000A0300000A000000"),
                InsHex(3, 12, "02000000000000000A0400000A000000"),
                InsHex(3, 12, "02000000000000000A0300001E000000"),
                InsHex(3, 12, "03000000000000000A0400001E000000"),
                InsHex(3, 12, "03000000000000000A03000032000000"),
                InsHex(3, 12, "04000000000000000A04000032000000"),
                InsHex(3, 12, "04000000000000000A03000064000000"),
                InsHex(3, 12, "05000000000000000A04000064000000"),
                InsHex(1000, 1, "01000200"),
                InsHex(2003, 2, "EE21AB0001000000"),
                InsHex(1000, 1, "01000300"),
                InsHex(2003, 2, "EF21AB0001000000"),
                InsHex(1000, 1, "01000400"),
                InsHex(2003, 2, "F021AB0001000000"),
                InsHex(1000, 1, "01000500"),
                InsHex(2003, 2, "F121AB0001000000"));
            evt.Parameters.AddRange(Enumerable.Range(5, 8)
                .Select(index => new EMEVD.Parameter(index, 4, 0, 4))
                .Concat(new[]
                {
                    new EMEVD.Parameter(2, 4, 4, 4),
                    new EMEVD.Parameter(3, 4, 8, 4),
                }));
            return evt;
        }

        private static EMEVD.Instruction[] M12DlcEvent11210404ExpectedBody()
        {
            EMEVD.Event source = M12DlcEvent11210404SourceEvent();
            return new[] { source.Instructions[0], InsHex(3, 0, "00010000EC21AB00") }
                .Concat(source.Instructions.Skip(5)).ToArray();
        }

        private static EMEVD.Parameter[] M12DlcEvent11210404ExpectedParameters() => Enumerable.Range(2, 8)
            .Select(index => new EMEVD.Parameter(index, 4, 0, 4)).ToArray();

        private static EMEVD.Event M12DlcEvent11210849SourceEvent() => Evt(11210849,
            InsHex(2000, 2, "00000000"),
            InsHex(4, 5, "00000000102700001100000001000000"),
            InsHex(2003, 2, "1324AB0001000000"),
            InsHex(2003, 2, "D21F000001000000"),
            InsHex(3, 6, "01000000"),
            InsHex(1000, 1, "02010100"),
            InsHex(2003, 2, "D01F000001000000"),
            InsHex(1000, 3, "01000000"),
            InsHex(2003, 2, "D11F000001000000"),
            InsHex(2007, 4, "0F4A020001000000"),
            InsHex(1001, 0, "0000803F"),
            InsHex(2003, 22, "581B0000451E000000000000"),
            InsHex(2004, 52, ""),
            InsHex(2003, 40, ""),
            InsHex(2004, 47, ""),
            InsHex(1001, 0, "00004040"),
            InsHex(2003, 14, "0C01000062961200"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Instruction[] M12DlcEvent11210849ExpectedBody() => new[]
        {
            InsHex(2000, 2, "00000000"),
            InsHex(4, 5, "00000000102700001100000001000000"),
            InsHex(2003, 2, "D21F000001000000"),
            InsHex(1003, 5, "02010000"),
            InsHex(2007, 9, "0E4A0200"),
            InsHex(1000, 3, "01000000"),
            InsHex(2007, 9, "0F4A0200"),
            InsHex(1001, 0, "0000803F"),
            InsHex(1003, 1, "040100001E22AB00"),
            InsHex(2003, 40, ""),
            InsHex(1001, 0, "00004040"),
            InsHex(2003, 14, "0C01000062961200"),
            InsHex(1000, 4, "01000000"),
            InsHex(2003, 40, ""),
            InsHex(1001, 0, "00004040"),
            InsHex(1000, 4, "01000000"),
        };

        private static EMEVD.Event M12DlcEvent11210835SourceEvent()
        {
            EMEVD.Event evt = Evt(11210835,
                InsHex(2000, 2, "00000000"),
                InsHex(3, 6, "01000000"),
                InsHex(3, 0, "010000001F22AB00"),
                InsHex(3, 0, "010100001E22AB00"),
                InsHex(0, 0, "00010100"),
                InsHex(1003, 1, "050100002D22AB00"),
                InsHex(1003, 1, "040100002A22AB00"),
                InsHex(1003, 1, "030100002322AB00"),
                InsHex(1003, 1, "020100002022AB00"),
                InsHex(1001, 0, "00000000"),
                InsHex(1000, 3, "01000000"),
                InsHex(1001, 0, "00000000"),
                InsHex(1003, 1, "0101000088030000"),
                InsHex(2003, 2, "2422AB0001000000"),
                InsHex(1003, 1, "050100002D22AB00"),
                InsHex(1003, 1, "040100002A22AB00"),
                InsHex(1003, 1, "030100002322AB00"),
                InsHex(1003, 1, "020100002022AB00"),
                InsHex(1001, 0, "00000000"),
                InsHex(1000, 3, "01000000"),
                InsHex(1001, 0, "00000000"),
                InsHex(3, 0, "0000000088030000"),
                InsHex(2003, 2, "1F22AB0001000000"),
                InsHex(1000, 4, "01000000"));
            evt.Parameters.AddRange(new[]
            {
                new EMEVD.Parameter(9, 0, 0, 4),
                new EMEVD.Parameter(18, 0, 4, 4),
                new EMEVD.Parameter(11, 0, 8, 4),
                new EMEVD.Parameter(20, 0, 12, 4),
            });
            return evt;
        }

        private static EMEVD.Instruction[] M12DlcEvent11210835ExpectedBody()
        {
            EMEVD.Instruction[] source = M12DlcEvent11210835SourceEvent().Instructions.ToArray();
            return source.Take(5)
                .Concat(source.Skip(7).Take(5))
                .Concat(source.Skip(13).Take(1))
                .Concat(source.Skip(16)).ToArray();
        }

        private static EMEVD.Parameter[] M12DlcEvent11210835ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(7, 0, 0, 4),
            new EMEVD.Parameter(13, 0, 4, 4),
            new EMEVD.Parameter(9, 0, 8, 4),
            new EMEVD.Parameter(15, 0, 12, 4),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210831ExpectedBody() => new[]
        {
            InsHex(3, 6, "01000000"),
            InsHex(3, 0, "010100001E22AB00"),
            InsHex(3, 0, "0100000000000000"),
            InsHex(3, 1, "010300000000000000000000"),
            InsHex(0, 0, "00010100"),
            InsHex(1003, 1, "010100002022AB00"),
            InsHex(2003, 22, "000000000000000001000000"),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210831ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(2, 4, 0, 4),
            new EMEVD.Parameter(3, 4, 4, 4),
            new EMEVD.Parameter(6, 0, 4, 4),
            new EMEVD.Parameter(3, 8, 8, 4),
            new EMEVD.Parameter(6, 4, 8, 4),
        };

        private static EMEVD.Event M12DlcEvent11210900SourceEvent()
        {
            EMEVD.Event evt = Evt(11210900,
                InsHex(1003, 2, "0001020000000000"),
                InsHex(4, 0, "000000000000000001000000"),
                InsHex(3, 2, "FF010000102700001C811200"),
                InsHex(3, 2, "FF010000102700001D811200"),
                InsHex(3, 2, "FF010000102700001E811200"),
                InsHex(3, 2, "FF0100001027000029811200"),
                InsHex(3, 2, "FF010000102700002A811200"),
                InsHex(3, 2, "FF010000102700002B811200"),
                InsHex(3, 2, "FF0100001027000079811200"),
                InsHex(3, 2, "FF010000102700007A811200"),
                InsHex(3, 2, "FF010000102700007C811200"),
                InsHex(3, 2, "FF010000102700007D811200"),
                InsHex(1000, 2, "0001FF00"),
                InsHex(2007, 4, "5131310100000000"),
                InsHex(2004, 5, "4A1A000001000000"));
            evt.Parameters.Add(new EMEVD.Parameter(1, 4, 0, 4));
            return evt;
        }

        private static EMEVD.Instruction[] M12DlcEvent11210900ExpectedBody()
        {
            EMEVD.Instruction[] source = M12DlcEvent11210900SourceEvent().Instructions.ToArray();
            return source.Take(2).Concat(source.Skip(13)).ToArray();
        }

        private static EMEVD.Event M12DlcEvent11210886SourceEvent()
        {
            EMEVD.Event evt = Evt(11210886,
                InsHex(3, 0, "0101000000000000"),
                InsHex(3, 0, "0101000000000000"),
                InsHex(0, 0, "00010100"),
                InsHex(2004, 48, "1027000000000040"),
                InsHex(3, 0, "0201000000000000"),
                InsHex(3, 0, "0200000000000000"),
                InsHex(1, 0, "0200000000004040"),
                InsHex(0, 0, "00010200"),
                InsHex(2004, 49, "102700000000803F"),
                InsHex(1000, 4, "01000000"));
            evt.Parameters.AddRange(new[]
            {
                new EMEVD.Parameter(0, 4, 0, 4),
                new EMEVD.Parameter(4, 4, 0, 4),
                new EMEVD.Parameter(1, 4, 4, 4),
                new EMEVD.Parameter(5, 4, 4, 4),
            });
            return evt;
        }

        private static EMEVD.Event M12DlcEvent11210837SourceEvent() => Evt(11210837,
            InsHex(3, 0, "01010000D11F0000"),
            InsHex(3, 0, "010100001E22AB00"),
            InsHex(3, 0, "010000001F22AB00"),
            InsHex(0, 0, "00010100"),
            InsHex(2000, 6, "00000000"),
            InsHex(1003, 1, "04010000E12CAB00"),
            InsHex(1003, 1, "01000000D11F0000"),
            InsHex(1003, 1, "02010000F621AB00"),
            InsHex(1003, 1, "01010000D11F0000"),
            InsHex(2007, 4, "184A020001000000"),
            InsHex(1003, 1, "02000000D11F0000"),
            InsHex(1001, 0, "00004040"),
            InsHex(2003, 40, ""),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Instruction[] M12DlcEvent11210837ExpectedBody() => new[]
        {
            InsHex(3, 6, "01000000"),
            InsHex(3, 6, "01020000"),
            InsHex(3, 0, "01010000D21F0000"),
            InsHex(3, 6, "02010000"),
            InsHex(3, 6, "02020000"),
            InsHex(3, 0, "02010000D21F0000"),
            InsHex(0, 0, "FF010100"),
            InsHex(0, 0, "FF010200"),
            InsHex(0, 0, "0001FF00"),
            InsHex(1003, 1, "01000000D21F0000"),
            InsHex(3, 0, "00000000D21F0000"),
            InsHex(1000, 7, "02010200"),
            InsHex(2007, 9, "184A0200"),
            InsHex(1000, 4, "01000000"),
            InsHex(2007, 9, "B52E3101"),
            InsHex(1000, 4, "01000000"),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210886ExpectedBody(EMEVD.Instruction[] source) =>
            source.Take(3)
                .Concat(new[] { InsHex(1003, 1, "0101000000000000"), InsHex(2004, 5, "1027000000000000") })
                .Concat(source.Skip(4).Take(2))
                .Concat(new[]
                {
                    InsHex(1, 0, "0200000000000040"), source[7],
                    InsHex(1003, 1, "0101000000000000"), InsHex(2004, 5, "1027000001000000"),
                })
                .Concat(source.Skip(9)).ToArray();

        private static EMEVD.Parameter[] M12DlcEvent11210886ExpectedParameters() => new[]
        {
            new EMEVD.Parameter(0, 4, 0, 4),
            new EMEVD.Parameter(3, 4, 0, 4),
            new EMEVD.Parameter(5, 4, 0, 4),
            new EMEVD.Parameter(9, 4, 0, 4),
            new EMEVD.Parameter(1, 4, 4, 4),
            new EMEVD.Parameter(6, 4, 4, 4),
        };

        private static EMEVD.Event M12DlcStateWatcherSourceEvent(long eventId, int instructionCount,
            string[] removedConditionHex, string[] stateConditionHex)
        {
            var instructions = removedConditionHex.Select(hex => InsHex(3, 0, hex)).ToList();
            instructions.AddRange(stateConditionHex.Select(hex => InsHex(3, 2, hex)));
            instructions.Add(InsHex(1000, 9, "00004040"));
            while (instructions.Count < instructionCount) instructions.Add(InsHex(0, 0, "00000000"));
            return Evt(eventId, instructions.ToArray());
        }

        private static void CheckM12DlcStateWatcher(EMEVD.Event evt, EMEVD.Instruction[] sourceBody,
            string[] removedConditionHex, string[] stateConditionHex, string[][] stateConditionGroups)
        {
            var expected = new List<EMEVD.Instruction>();
            for (int i = 0; i < sourceBody.Length; i++)
            {
                EMEVD.Instruction instruction = sourceBody[i];
                if (removedConditionHex.Any(hex => EmevdMerge.SameInstruction(instruction, InsHex(3, 0, hex)))) continue;
                expected.Add(instruction);
                int anchorIndex = Array.FindIndex(stateConditionHex,
                    hex => EmevdMerge.SameInstruction(instruction, InsHex(3, 2, hex)));
                if (anchorIndex >= 0)
                    expected.AddRange(stateConditionGroups[anchorIndex].Select(hex => InsHex(3, 0, hex)));
                if (EmevdMerge.SameInstruction(instruction, InsHex(1000, 9, "00004040")))
                    expected.Add(InsHex(1001, 1, "00000000"));
            }
            Check(evt.Instructions.Count == expected.Count
                && evt.Instructions.Zip(expected).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 DLC event " + evt.ID + " did not match its exact reconstructed state-watcher body");
        }

        private static void AssertMalformedM12DlcStateWatcherIsRejected(EMEVD archive, long eventId,
            int instructionCount, string[] removedConditionHex, string[] stateConditionHex)
        {
            EMEVD.Event malformed = M12DlcStateWatcherSourceEvent(eventId, instructionCount,
                removedConditionHex, stateConditionHex);
            malformed.Instructions[removedConditionHex.Length + 1] = InsHex(3, 2, "FF01000010270000FFFFFF00");
            EMEVD.Instruction[] original = malformed.Instructions.ToArray();
            EMEVD.Event[] events = archive.Events.Where(evt => evt.ID != eventId).Append(malformed).ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12DlcEvents(Evd(events)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == original.Length
                && malformed.Instructions.Zip(original)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M12 DLC event " + eventId + " was accepted or mutated");
        }

        private static string[][] M12DlcOneFlagStateConditionGroups(string flagHex) =>
            Enumerable.Range(1, 4).Select(state => new[]
                { state.ToString("X2") + "000000" + flagHex }).ToArray();

        private static string[][] M12DlcTwoFlagStateConditionGroups(string firstFlagHex, string secondFlagHex) =>
            Enumerable.Range(1, 4).Select(state => new[]
            {
                state.ToString("X2") + "010000" + firstFlagHex,
                state.ToString("X2") + "000000" + secondFlagHex,
            }).ToArray();

        private static EMEVD.Instruction[] M12DlcEvent11210401ExpectedBody() => new[]
        {
            InsHex(2000, 2, "00000000"),
            InsHex(3, 6, "01020000"),
            InsHex(3, 10, "010000000022AB000322AB000400000002000000"),
            InsHex(4, 5, "01000000102700000512000001000000"),
            InsHex(3, 0, "01000000EC21AB00"),
            InsHex(0, 0, "00010100"),
            InsHex(1001, 0, "00007041"),
            InsHex(1003, 2, "01010000EC21AB00"),
            InsHex(1003, 6, "01030000"),
            InsHex(2007, 4, "F149020001000000"),
            InsHex(1000, 4, "01000000"),
        };

        private static EMEVD.Event M12DlcAreaConditionCleanupSourceEvent() => Evt(11210838,
            InsHex(2000, 2, "00000000"),
            InsHex(3, 2, "FF010000102700001C811200"),
            InsHex(3, 2, "FF010000102700001D811200"),
            InsHex(3, 2, "FF010000102700001E811200"),
            InsHex(3, 2, "FF0100001027000029811200"),
            InsHex(3, 2, "FF010000102700002A811200"),
            InsHex(3, 2, "FF010000102700002B811200"),
            InsHex(3, 2, "FF010000102700007C811200"),
            InsHex(3, 2, "FF010000102700007D811200"),
            InsHex(3, 2, "FF0100001027000079811200"),
            InsHex(3, 2, "FF010000102700007A811200"),
            InsHex(0, 0, "0001FF00"),
            InsHex(2004, 47, ""));

        private static EMEVD.Event M12DlcAreaConditionCleanupSourceEvent2() => Evt(11210439,
            InsHex(1003, 6, "00010000"),
            InsHex(3, 2, "FF010000102700001C811200"),
            InsHex(3, 2, "FF010000102700001D811200"),
            InsHex(3, 2, "FF010000102700001E811200"),
            InsHex(3, 2, "FF0100001027000029811200"),
            InsHex(3, 2, "FF010000102700002A811200"),
            InsHex(3, 2, "FF010000102700002B811200"),
            InsHex(3, 2, "FF0100001027000079811200"),
            InsHex(3, 2, "FF010000102700007A811200"),
            InsHex(3, 2, "FF010000102700007C811200"),
            InsHex(3, 2, "FF010000102700007D811200"),
            InsHex(1000, 2, "0000FF00"),
            InsHex(2004, 2, "1027000001000000"));

        private static EMEVD.Event M12DlcAreaConditionCleanupSourceEvent3()
        {
            EMEVD.Event evt = TerminatedSourceEvent(11210407, 119, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[40] = InsHex(3, 2, "FF010000102700007D811200");
            evt.Instructions[41] = InsHex(3, 2, "FF010000102700007C811200");
            evt.Instructions[42] = InsHex(3, 2, "FF010000102700007A811200");
            evt.Instructions[43] = InsHex(3, 2, "FF0100001027000079811200");
            return evt;
        }

        private static EMEVD.Event M12DlcAreaConditionCleanupSourceEvent4()
        {
            EMEVD.Event evt = TerminatedSourceEvent(11210878, 18, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[5] = InsHex(3, 2, "FF010000102700007C811200");
            evt.Instructions[6] = InsHex(3, 2, "FF010000102700007D811200");
            evt.Instructions[7] = InsHex(3, 2, "FF0100001027000079811200");
            evt.Instructions[8] = InsHex(3, 2, "FF010000102700007A811200");
            return evt;
        }

        private static EMEVD.Event M12DlcAreaConditionCleanupSourceEvent5()
        {
            EMEVD.Event evt = TerminatedSourceEvent(11210402, 19, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[7] = InsHex(3, 2, "FF010000102700007C811200");
            evt.Instructions[8] = InsHex(3, 2, "FF010000102700007D811200");
            evt.Instructions[9] = InsHex(3, 2, "FF0100001027000079811200");
            evt.Instructions[10] = InsHex(3, 2, "FF010000102700007A811200");
            return evt;
        }

        private static EMEVD.Event M12DlcMapGuardSourceEvent(long eventId)
        {
            EMEVD.Event evt = new EMEVD.Event(eventId, EMEVD.Event.RestBehaviorType.Restart);
            if (eventId == 11215007)
            {
                evt.Instructions.AddRange(new[]
                {
                    InsHex(3, 0, "00010000110DAB00"),
                    InsHex(2004, 1, "B179120000000000"),
                    InsHex(2004, 1, "B279120000000000"),
                    InsHex(3, 2, "0101000010270000647E1200"),
                    InsHex(3, 2, "0100000010270000617E1200"),
                    InsHex(3, 2, "0100000010270000627E1200"),
                    InsHex(0, 0, "FF010100"),
                    InsHex(4, 1, "FF000000B179120010270000"),
                    InsHex(4, 1, "FF000000B279120010270000"),
                    InsHex(0, 0, "0001FF00"),
                    InsHex(2004, 1, "B179120001000000"),
                    InsHex(2003, 18, "B1791200C90B000000000000"),
                    InsHex(1001, 1, "0F000000"),
                    InsHex(2004, 1, "B279120001000000"),
                    InsHex(2003, 18, "B2791200C90B000000000000"),
                    InsHex(1001, 1, "0F000000"),
                    InsHex(2004, 20, "B1791200"),
                    InsHex(2004, 20, "B2791200"),
                });
            }
            else if (eventId == 11215009)
            {
                evt.Instructions.AddRange(new[]
                {
                    InsHex(3, 0, "00010000110DAB00"),
                    InsHex(3, 2, "0001000010270000667E1200"),
                    InsHex(2004, 42, "B179120001000000677E1200FFFFFFFFB0791200"),
                    InsHex(2004, 42, "B279120001000000687E1200FFFFFFFFB0791200"),
                    InsHex(2004, 13, "B1791200677E1200"),
                    InsHex(2004, 13, "B2791200687E1200"),
                    InsHex(3, 2, "0001000010270000697E1200"),
                    InsHex(2004, 42, "B1791200010000006A7E1200FFFFFFFFB0791200"),
                    InsHex(2004, 42, "B2791200010000006B7E1200FFFFFFFFB0791200"),
                    InsHex(2004, 13, "B17912006A7E1200"),
                    InsHex(2004, 13, "B27912006B7E1200"),
                    InsHex(1000, 4, "01000000"),
                });
            }
            else throw new ArgumentOutOfRangeException(nameof(eventId));
            return evt;
        }

        private static EMEVD.Event M15DlcEvent11515381SourceEvent() => Evt(11515381,
            InsHex(2000, 2, "00000000"),
            InsHex(3, 0, "0100000074A4AF00"),
            InsHex(3, 0, "01010000F7B5AF00"),
            InsHex(4, 3, "010000001027000001000000"),
            InsHex(3, 19, "0100000001000000C215170000000000FFFF00000000000023BF9800FF00000000000000D2111700"),
            InsHex(0, 0, "00010100"),
            InsHex(2004, 14, "10270000C1151700"),
            InsHex(1003, 1, "0201000080A2AF00"),
            InsHex(2002, 4, "874A020002000000C51517000F01000010270000"),
            InsHex(1000, 3, "01000000"),
            InsHex(2002, 4, "8C4A020002000000C51517000F01000010270000"),
            InsHex(1001, 1, "01000000"),
            InsHex(2004, 5, "FA0C170001000000"),
            InsHex(2003, 2, "FCB5AF0001000000"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Event M15DlcFogFlagSourceEvent()
        {
            EMEVD.Event evt = Evt(11510502,
                InsHex(3, 0, "01010000D8040000"),
                InsHex(3, 0, "FE010000D0040000"),
                InsHex(3, 0, "FE010000F6B5AF00"),
                InsHex(0, 0, "0101FE00"),
                InsHex(4, 0, "010000000000000000000000"),
                InsHex(3, 0, "0201000000000000"),
                InsHex(3, 0, "0201010000000000"),
                InsHex(0, 0, "FF010100"),
                InsHex(0, 0, "FF010200"),
                InsHex(0, 0, "0001FF00"),
                InsHex(2003, 2, "0000000001000000"),
                InsHex(2004, 2, "0000000007000000"));
            evt.Parameters.Add(new EMEVD.Parameter(4, 4, 0, 4));
            evt.Parameters.Add(new EMEVD.Parameter(11, 0, 0, 4));
            evt.Parameters.Add(new EMEVD.Parameter(5, 4, 4, 4));
            evt.Parameters.Add(new EMEVD.Parameter(10, 0, 4, 4));
            return evt;
        }

        private static EMEVD.Event M15DlcGateFlagSourceEvent()
        {
            EMEVD.Event evt = TerminatedSourceEvent(11510230, 13, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[8] = InsHex(2002, 4, "774A020000000000AED210000B00000010270000");
            evt.Instructions[10] = InsHex(2003, 23, "AFD21000");
            evt.Instructions[11] = InsHex(2000, 5, "00000000");
            evt.Instructions[9] = InsHex(1001, 1, "01000000");
            evt.Instructions[12] = InsHex(1000, 4, "01000000");
            return evt;
        }

        private static EMEVD.Event M15DlcGateAreaTransitionSourceEvent() => Evt(11510240,
            InsHex(1003, 6, "00010000"),
            InsHex(3, 8, "00010F01"),
            InsHex(1, 0, "000000000000A040"),
            InsHex(3, 0, "02000000AAB4AF00"),
            InsHex(3, 5, "0200000002000000D40A170000003443FFFF00000000E04058BE98003000000000000000"),
            InsHex(0, 0, "00010200"),
            InsHex(1003, 5, "02030000"),
            InsHex(2007, 1, "3ABE980001000600FFFFFFFF00004040"),
            InsHex(1000, 4, "01000000"),
            InsHex(1003, 1, "0201000080A2AF00"),
            InsHex(2002, 4, "724A02000000000024ED16000F00000010270000"),
            InsHex(1000, 3, "01000000"),
            InsHex(2002, 4, "744A02000000000024ED16000F00000010270000"),
            InsHex(1001, 1, "01000000"),
            InsHex(2003, 19, "0F000000"),
            InsHex(1000, 4, "01000000"));

        private static void KnownModifiedEndingEventsReconstructed()
        {
            var ending = new EMEVD.Event(21, EMEVD.Event.RestBehaviorType.Default);
            for (int i = 0; i < 10; i++) ending.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            ending.Instructions[5] = new EMEVD.Instruction(2002, 3, new byte[12]);
            ending.Instructions[6] = new EMEVD.Instruction(1001, 1, new byte[4]);
            ending.Instructions[7] = new EMEVD.Instruction(2003, 21, new byte[] { 1, 0, 0, 0 });
            EMEVD.Event endingGate = TerminatedSourceEvent(11805390, 6, EMEVD.Event.RestBehaviorType.Default);
            EMEVD endingArchive = Evd(ending, endingGate);
            FogModifiedEventBuilder.ReconstructKnownM18Events(endingArchive);
            Check(ending.Instructions[5].Bank == 2003 && ending.Instructions[5].ID == 21
                && ending.Instructions[6].Bank == 2002 && ending.Instructions[6].ID == 3
                && ending.Instructions[7].Bank == 1001 && ending.Instructions[7].ID == 1,
                "game-cycle increment did not move ahead of ending cutscene playback");
            FogModifiedEventBuilder.ReconstructKnownM18Events(endingArchive);
            Check(ending.Instructions[5].Bank == 2003 && ending.Instructions[5].ID == 21,
                "ending-cycle event reconstruction is not idempotent");
            Check(IsEndEvent(endingGate.Instructions[0]), "Kiln vanilla gate event was not stopped");

            var bonus = new EMEVD.Event(11810300, EMEVD.Event.RestBehaviorType.Default);
            for (int i = 0; i < 14; i++) bonus.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            bonus.Instructions[8] = new EMEVD.Instruction(3, 0,
                new object[] { (sbyte)1, (byte)0, (byte)0, 0x00B4352A });
            EMEVD.Event transition = M18DlcEvent11810150SourceEvent();
            EMEVD.Instruction[] sourceTransition = transition.Instructions.ToArray();
            EMEVD bonusArchive = Evd(bonus,
                ObjectSfxSourceEvent(11810001, 11, EMEVD.Event.RestBehaviorType.Default, 0x001BA616, 0, 0x001BA617),
                ObjectSfxSourceEvent(11810900, 5, EMEVD.Event.RestBehaviorType.Default, 0x001BA5B2, 0, 0x001BA5B3),
                TerminatedSourceEvent(11810000, 14, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11815390, 11, EMEVD.Event.RestBehaviorType.Default),
                M18DlcFinalFlagSourceEvent(), transition);
            FogModifiedEventBuilder.ReconstructKnownM18DlcEvents(bonusArchive);
            EMEVD.Instruction[] expectedTransition = M18DlcEvent11810150ExpectedBody(sourceTransition);
            Check(transition.Instructions.Count == expectedTransition.Length
                && transition.Instructions.Zip(expectedTransition)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M18 DLC event 11810150 did not match its exact reconstructed body");
            Check(BitConverter.ToInt32(bonus.Instructions[8].ArgData, 4) == 0x02FAF0D0,
                "DLC demon bonus condition did not use the observed Fog flag");
            EMEVD.Event finalFlagEvent = bonusArchive.Events.Single(evt => evt.ID == 11815394);
            Check(finalFlagEvent.Instructions.Count == 14
                && finalFlagEvent.Instructions[12].Bank == 2004 && finalFlagEvent.Instructions[12].ID == 1
                && finalFlagEvent.Instructions[12].ArgData.SequenceEqual(Convert.FromHexString("70A11B0001000000"))
                && finalFlagEvent.Instructions[13].Bank == 1000 && finalFlagEvent.Instructions[13].ID == 4
                && finalFlagEvent.Instructions[13].ArgData.SequenceEqual(Convert.FromHexString("01000000")),
                "DLC final flag write was not inserted before the event end");
            EMEVD.Instruction[] reconstructedFinalFlagBody = finalFlagEvent.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM18DlcEvents(bonusArchive);
            Check(BitConverter.ToInt32(bonus.Instructions[8].ArgData, 4) == 0x02FAF0D0,
                "DLC demon bonus reconstruction is not idempotent");
            Check(finalFlagEvent.Instructions.Count == 14
                && finalFlagEvent.Instructions.Zip(reconstructedFinalFlagBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "DLC final flag write reconstruction is not idempotent");
            Check(transition.Instructions.Count == expectedTransition.Length
                && transition.Instructions.Zip(expectedTransition)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M18 DLC event 11810150 reconstruction is not idempotent");
            EMEVD.Event malformedTransition = M18DlcEvent11810150SourceEvent();
            malformedTransition.Instructions[7] = InsHex(1003, 5, "06030000");
            EMEVD.Instruction[] malformedTransitionBody = malformedTransition.Instructions.ToArray();
            EMEVD malformedTransitionArchive = Evd(bonusArchive.Events.Where(evt => evt.ID != 11810150)
                .Append(malformedTransition).ToArray());
            bool malformedTransitionRejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM18DlcEvents(malformedTransitionArchive); }
            catch (InvalidDataException) { malformedTransitionRejected = true; }
            Check(malformedTransitionRejected
                && malformedTransition.Instructions.Zip(malformedTransitionBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M18 DLC event 11810150 was accepted or mutated");
        }

        private static EMEVD.Event M17Event11700000SourceEvent()
        {
            EMEVD.Event evt = TerminatedSourceEvent(11700000, 31, EMEVD.Event.RestBehaviorType.Restart);
            evt.Instructions[1] = InsHex(2005, 3, "66F8190000000000");
            evt.Instructions[2] = InsHex(2006, 1, "67F8190000000000");
            evt.Instructions[27] = InsHex(2005, 3, "66F8190001000000");
            evt.Instructions[28] = InsHex(2006, 2, "67F81900");
            return evt;
        }

        private static EMEVD M17SourceEventArchive(EMEVD.Event additionalEvent)
        {
            var watcher = new EMEVD.Event(11705383, EMEVD.Event.RestBehaviorType.Restart);
            watcher.Instructions.Add(new EMEVD.Instruction(2004, 1, new object[] { 0x0019F35C, (byte)0 }));
            for (int i = 0; i < 8; i++) watcher.Instructions.Add(new EMEVD.Instruction(2000, 2, new byte[] { (byte)i }));
            return Evd(watcher,
                TerminatedSourceEvent(11700085, 13, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11705100, 7, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11705380, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11705381, 8, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11705382, 8, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11705390, 10, EMEVD.Event.RestBehaviorType.Default),
                ObjectSfxSourceEvent(11700001, 12, EMEVD.Event.RestBehaviorType.Default, 0x0019F866, 0, 0x0019F867),
                ObjectSfxSourceEvent(11705385, 9, EMEVD.Event.RestBehaviorType.Restart, 0x0019F802, 0, 0x0019F803),
                additionalEvent);
        }

        private static void M17Event11700000SfxOperationsRemoved()
        {
            EMEVD.Event source = M17Event11700000SourceEvent();
            var expected = source.Instructions.ToList();
            expected[1] = EndInstruction();
            expected.RemoveAt(28);
            expected.RemoveAt(27);
            expected.RemoveAt(2);
            EMEVD archive = M17SourceEventArchive(source);

            FogModifiedEventBuilder.ReconstructKnownM17Events(archive);
            Check(source.Instructions.Count == 28
                && source.Instructions.Zip(expected).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "11700000 changed beyond replacing its first exact object/SFX pair and removing the second");
            EMEVD.Instruction[] reconstructed = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM17Events(archive);
            Check(source.Instructions.Zip(reconstructed).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "11700000 operation removal was not idempotent");

            EMEVD.Event malformed = M17Event11700000SourceEvent();
            malformed.Instructions[28] = InsHex(2006, 2, "68F81900");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM17Events(M17SourceEventArchive(malformed)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed 11700000 operation set was accepted or mutated");
        }

        private static void KnownVanillaGateEventsTerminated()
        {
            EMEVD.Event parishLanding = M10ParishObjectSfxSourceEvent();
            EMEVD.Instruction[] parishLandingUnaffectedBody = parishLanding.Instructions.Take(14).ToArray();
            EMEVD.Event parishInteraction = M10ParishInteractionSourceEvent();
            EMEVD.Instruction[] parishInteractionUnaffectedBody = parishInteraction.Instructions.Take(10).ToArray();
            AssertPrefixedEvents(FogModifiedEventBuilder.ReconstructKnownM10Events,
                new[]
                {
                    ObjectSfxSourceEvent(11000001, 6, EMEVD.Event.RestBehaviorType.Default, 0x000F4A06, 0, 0x000F4A07),
                    M10GateConditionSourceEvent(11005030, 22, "68ECA700", "6DECA700"),
                    M10GateObjectStateSourceEvent(11005033, 21, 8, 12, 13, "6BECA700", "6EECA700"),
                    M10WatcherSourceEvent(),
                    M10Event11005392SourceEvent(),
                },
                PrefixExpectation(11005390, 6, EMEVD.Event.RestBehaviorType.Default, WaitForFlag(0x00A7EDD1), EndInstruction()));
            AssertPrefixedEvents(FogModifiedEventBuilder.ReconstructKnownM10ParishEvents,
                new[]
                {
                    parishLanding,
                    parishInteraction,
                    ObjectSfxSourceEvent(11010902, 20, EMEVD.Event.RestBehaviorType.Restart, 0x000F704E, 0, 0x000F704F),
                    M10GateConditionSourceEvent(11015100, 25, "BE13A800", "C213A800"),
                    M10GateObjectStateSourceEvent(11015103, 22, 8, 11, 12, "C113A800", "C313A800"),
                    M10ParishEvent11010000SourceEvent(),
                },
                PrefixExpectation(11015370, 6, EMEVD.Event.RestBehaviorType.Default, WaitForFlag(0x00A814CD), EndInstruction()),
                PrefixExpectation(11015380, 6, EMEVD.Event.RestBehaviorType.Default, WaitForFlag(0x00A814D7), EndInstruction()),
                PrefixExpectation(11015390, 6, EMEVD.Event.RestBehaviorType.Default, WaitForFlag(0x00A814E1), EndInstruction()));
            Check(parishLanding.Instructions.Count == 14
                && parishLanding.Instructions.Take(14).Zip(parishLandingUnaffectedBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "Parish landing reconstruction changed operations outside its exact object/SFX sequence");
            Check(parishInteraction.Instructions.Count == 10
                && parishInteraction.Instructions.Take(10).Zip(parishInteractionUnaffectedBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "Parish interaction reconstruction changed operations outside its exact object/SFX sequence");
            EMEVD.Event malformedParishSfx = M10ParishObjectSfxSourceEvent();
            malformedParishSfx.Instructions[17] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x000F7119, (byte)0 });
            AssertM10ParishLandingRefused(malformedParishSfx);

            EMEVD.Event parameterizedParishSfx = M10ParishObjectSfxSourceEvent();
            parameterizedParishSfx.Parameters.Add(new EMEVD.Parameter(14, 0, 0, 1));
            AssertM10ParishLandingRefused(parameterizedParishSfx);
            EMEVD.Event m12SharedObjectSfx = M12SharedObjectSfxSourceEvent();
            EMEVD.Instruction[] m12SharedUnaffected = m12SharedObjectSfx.Instructions
                .Where((_, index) => index != 3 && index != 4 && index != 31 && index != 32).ToArray();
            EMEVD.Event m12FogGate = M12Event11200005SourceEvent();
            EMEVD.Instruction[] m12FogGateUnaffected = m12FogGate.Instructions
                .Where((_, index) => index != 0 && index != 1 && index != 2 && index != 3 && index != 5).ToArray();
            AssertTerminatedEvents(FogModifiedEventBuilder.ReconstructKnownM12Events,
                new[]
                {
                    m12SharedObjectSfx,
                    m12FogGate,
                    M12Event11200006SourceEvent(),
                    M12Event11200900SourceEvent(),
                    ObjectSfxSourceEvent(11200001, 5, EMEVD.Event.RestBehaviorType.Default, 0x00125746, 0, 0x00125747),
                    ObjectSfxSourceEvent(11200002, 30, EMEVD.Event.RestBehaviorType.Default, 0x00125746, 1, 0x00125747, true),
                },
                (11205380, 6, EMEVD.Event.RestBehaviorType.Default),
                (11205390, 7, EMEVD.Event.RestBehaviorType.Default));
            Check(m12SharedObjectSfx.Instructions.Count == 30
                && m12SharedObjectSfx.Instructions.Zip(m12SharedUnaffected)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "Depths event object/SFX reconstruction changed operations beyond the two authored pairs");
            Check(m12FogGate.Instructions.Count == 8
                && m12FogGate.Instructions.Zip(m12FogGateUnaffected)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11200005 reconstruction changed operations beyond its authored body edit");
            EMEVD.Event malformedM12FogGate = M12Event11200005SourceEvent();
            malformedM12FogGate.Instructions[5] = InsHex(3, 0, "0100000012000000");
            EMEVD.Instruction[] malformedM12FogGateBody = malformedM12FogGate.Instructions.ToArray();
            bool malformedM12FogGateRejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12Events(Evd(malformedM12FogGate)); }
            catch (InvalidDataException) { malformedM12FogGateRejected = true; }
            Check(malformedM12FogGateRejected && malformedM12FogGate.Instructions.Zip(malformedM12FogGateBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed event 11200005 body was accepted or mutated");
            AssertTerminatedEvents(FogModifiedEventBuilder.ReconstructKnownM13Events,
                new[]
                {
                    M13MapInitializerSourceEvent(),
                    M13Event11305000SourceEvent(),
                    ObjectSfxSourceEvent(11300001, 10, EMEVD.Event.RestBehaviorType.Default, 0x0013DDE6, 0, 0x0013DDE7),
                },
                (11305390, 7, EMEVD.Event.RestBehaviorType.Default));
            AssertPrefixedEvents(FogModifiedEventBuilder.ReconstructKnownM13DlcEvents,
                new[]
                {
                    ObjectSfxSourceEvent(11310001, 13, EMEVD.Event.RestBehaviorType.Default, 0x001404F6, 0, 0x001404F7),
                    M13DlcMapInitializerSourceEvent(),
                    M13DlcAreaStateSourceEvent(),
                    M13DlcDelaySourceEvent(),
                    M13DlcAreaOperationsSourceEvent(),
                    M13DlcObjectOperationsSourceEvent(),
                },
                PrefixExpectation(11310095, 14, EMEVD.Event.RestBehaviorType.Default, EndInstruction()),
                PrefixExpectation(11315390, 7, EMEVD.Event.RestBehaviorType.Default, WaitForArea(0x001408E6), EndInstruction()));
            AssertM14Events();
            AssertM14DlcGateEvents();
            AssertTerminatedEvents(FogModifiedEventBuilder.ReconstructKnownM15Events,
                new[]
                {
                    ObjectSfxSourceEvent(11500001, 17, EMEVD.Event.RestBehaviorType.Default, 0x0016EB26, 0, 0x0016EB27),
                    M15Event11500210SourceEvent(),
                },
                (11505390, 6, EMEVD.Event.RestBehaviorType.Default));

            EMEVD.Event[] m16Events =
            {
                TerminatedSourceEvent(11605390, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11600400, 10, EMEVD.Event.RestBehaviorType.Restart),
            };
            m16Events[1].Instructions[0] = new EMEVD.Instruction(2004, 31, new object[] { 0x00186BF4, (byte)1 });
            m16Events[1].Instructions[1] = new EMEVD.Instruction(2004, 10, new object[] { 0x00186BF4, (byte)0 });
            var m16Archive = Evd(m16Events);
            EMEVD.Instruction[] m16Body = m16Events[0].Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM16Events(m16Archive);
            CheckPrefix(m16Events[0], new[] { WaitForArea(0x001875B4), EndInstruction() }, "Valley of Drakes gate watcher");
            Check(m16Events[0].Instructions.Skip(2).SequenceEqual(m16Body), "Valley of Drakes gate watcher body changed");
            FogModifiedEventBuilder.ReconstructKnownM16Events(m16Archive);
            Check(m16Events[0].Instructions.Count == m16Body.Length + 2, "Valley of Drakes gate watcher prefix is not idempotent");

            var watcher = new EMEVD.Event(11705383, EMEVD.Event.RestBehaviorType.Restart);
            watcher.Instructions.Add(new EMEVD.Instruction(2004, 1, new object[] { 0x0019F35C, (byte)0 }));
            for (int i = 0; i < 8; i++) watcher.Instructions.Add(new EMEVD.Instruction(2000, 2, new byte[] { (byte)i }));
            var m17Archive = Evd(watcher,
                TerminatedSourceEvent(11700085, 13, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11705100, 7, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11705380, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11705381, 8, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11705382, 8, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11705390, 10, EMEVD.Event.RestBehaviorType.Default),
                ObjectSfxSourceEvent(11700001, 12, EMEVD.Event.RestBehaviorType.Default, 0x0019F866, 0, 0x0019F867),
                ObjectSfxSourceEvent(11705385, 9, EMEVD.Event.RestBehaviorType.Restart, 0x0019F802, 0, 0x0019F803),
                M17Event11700000SourceEvent());
            EMEVD.Instruction[] watcherBody = watcher.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM17Events(m17Archive);
            Check(watcher.Instructions[0] == watcherBody[0] && IsEndEvent(watcher.Instructions[1])
                && watcher.Instructions.Skip(2).SequenceEqual(watcherBody.Skip(1)),
                "Seath crystal watcher did not stop after its initial AI-state change");
            Check(new long[] { 11700085, 11705100, 11705380, 11705381, 11705382, 11705390 }
                .All(id => IsEndEvent(m17Archive.Events.Single(evt => evt.ID == id).Instructions[0])),
                "Duke's Archives and Crystal Cave gate events were not stopped");
            EMEVD.Instruction insertedEnd = watcher.Instructions[1];
            FogModifiedEventBuilder.ReconstructKnownM17Events(m17Archive);
            Check(watcher.Instructions.Count == watcherBody.Length + 1 && watcher.Instructions[1] == insertedEnd,
                "Seath crystal watcher reconstruction is not idempotent");

            var dlcBonus = new EMEVD.Event(11810300, EMEVD.Event.RestBehaviorType.Default);
            for (int i = 0; i < 14; i++) dlcBonus.Instructions.Add(new EMEVD.Instruction(1000, 4, new byte[4]));
            dlcBonus.Instructions[8] = new EMEVD.Instruction(3, 0,
                new object[] { (sbyte)1, (byte)0, (byte)0, 0x00B4352A });
            EMEVD.Event dlcTransition = M18DlcEvent11810150SourceEvent();
            var m18DlcPrefixEvents = Evd(dlcBonus,
                ObjectSfxSourceEvent(11810001, 11, EMEVD.Event.RestBehaviorType.Default, 0x001BA616, 0, 0x001BA617),
                ObjectSfxSourceEvent(11810900, 5, EMEVD.Event.RestBehaviorType.Default, 0x001BA5B2, 0, 0x001BA5B3),
                TerminatedSourceEvent(11810000, 14, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11815390, 11, EMEVD.Event.RestBehaviorType.Default),
                M18DlcFinalFlagSourceEvent(), dlcTransition);
            FogModifiedEventBuilder.ReconstructKnownM18DlcEvents(m18DlcPrefixEvents);
            CheckPrefix(m18DlcPrefixEvents.Events.Single(evt => evt.ID == 11810000),
                new[] { EndIfFlag(0x00B4353E), WaitForFlag(0x04277DC8), EndInstruction() },
                "Kiln conditional gate watcher");
            CheckPrefix(m18DlcPrefixEvents.Events.Single(evt => evt.ID == 11815390),
                new[] { WaitForArea(0x001BAA06), EndInstruction() }, "DLC vanilla gate watcher");
            FogModifiedEventBuilder.ReconstructKnownM18DlcEvents(m18DlcPrefixEvents);
            Check(m18DlcPrefixEvents.Events.Single(evt => evt.ID == 11810000).Instructions.Count == 17,
                "Kiln conditional gate watcher prefix is not idempotent");

        }

        private static void AssertM14DlcGateEvents()
        {
            EMEVD.Event[] objectSfxCleanups =
            {
                M14DlcObjectSfxSourceEvent(11410901, 0x00158B32, 0x00158B33, 0x00158B34, 0x00158B35),
                M14DlcObjectSfxSourceEvent(11410410, 0x00158952, 0x00158953, 0x00158954, 0x00158955),
            };
            EMEVD.Instruction[][] expectedObjectSfxBodies = objectSfxCleanups
                .Select(evt => evt.Instructions.Where((_, index) => index < 4 || index > 7).ToArray())
                .ToArray();
            EMEVD.Event gateControls = M14DlcEvent11410001SourceEvent();
            EMEVD.Instruction[] sourceGateControls = gateControls.Instructions.ToArray();
            EMEVD.Event[] events =
            {
                gateControls,
                TerminatedSourceEvent(11410095, 14, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11415340, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11415370, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11415373, 10, EMEVD.Event.RestBehaviorType.Default),
                M14DlcGateVanillaEvent(),
                objectSfxCleanups[0],
                objectSfxCleanups[1],
                ObjectSfxSourceEvent(11410900, 12, EMEVD.Event.RestBehaviorType.Default, 0x00158ACE, 0, 0x00158ACF),
            };
            EMEVD.Event watcher = events.Single(evt => evt.ID == 11415380);
            EMEVD.Instruction[] watcherBody = watcher.Instructions.ToArray();
            EMEVD.Event delay = events.Single(evt => evt.ID == 11415373);
            EMEVD.Instruction[] delayBody = delay.Instructions.ToArray();
            EMEVD archive = Evd(events);

            FogModifiedEventBuilder.ReconstructKnownM14DlcEvents(archive);
            CheckPrefix(delay, new[] { new EMEVD.Instruction(1001, 1, new object[] { 1 }) }, "11415373 one-frame delay");
            Check(delay.Instructions.Count == delayBody.Length + 1
                && delay.Instructions.Skip(1).SequenceEqual(delayBody), "11415373 event body changed");
            CheckPrefix(watcher, new[] { WaitForArea(0x00158F20), EndInstruction() }, "11415380 vanilla watcher");
            Check(watcher.Instructions.Count == watcherBody.Length + 2
                && watcher.Instructions.Skip(2).SequenceEqual(watcherBody), "11415380 vanilla watcher body changed");
            Check(objectSfxCleanups.Zip(expectedObjectSfxBodies)
                .All(pair => pair.First.Instructions.Count == 8
                    && pair.First.Instructions.Zip(pair.Second)
                        .All(instructionPair => EmevdMerge.SameInstruction(instructionPair.First, instructionPair.Second))),
                "M14 DLC double object/SFX sequences were not removed exactly");
            EMEVD.Instruction[] expectedGateControls = sourceGateControls.Take(4).Concat(new[]
            {
                InsHex(2004, 15, "F086150000000000"), InsHex(2004, 15, "F186150000000000"),
                InsHex(2004, 4, "F086150000000000"), InsHex(2004, 4, "F186150000000000"),
                InsHex(2006, 1, "1891150001000000"), InsHex(2006, 1, "2291150001000000"),
                InsHex(2006, 1, "1991150001000000"), InsHex(2006, 1, "2391150001000000"),
            }).Concat(sourceGateControls.Skip(4).Take(3)).Concat(sourceGateControls.Skip(9)).ToArray();
            Check(gateControls.Instructions.Count == expectedGateControls.Length
                && gateControls.Instructions.Zip(expectedGateControls)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M14 DLC event 11410001 did not match its exact gate-control and wait reconstruction");
            FogModifiedEventBuilder.ReconstructKnownM14DlcEvents(archive);
            Check(delay.Instructions.Count == delayBody.Length + 1, "11415373 one-frame delay is not idempotent");
            Check(watcher.Instructions.Count == watcherBody.Length + 2, "11415380 vanilla watcher reconstruction is not idempotent");
            Check(objectSfxCleanups.Zip(expectedObjectSfxBodies)
                .All(pair => pair.First.Instructions.Count == 8
                    && pair.First.Instructions.Zip(pair.Second)
                        .All(instructionPair => EmevdMerge.SameInstruction(instructionPair.First, instructionPair.Second))),
                "M14 DLC double object/SFX removal is not idempotent");
            Check(gateControls.Instructions.Count == expectedGateControls.Length
                && gateControls.Instructions.Zip(expectedGateControls)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M14 DLC event 11410001 reconstruction is not idempotent");

            EMEVD.Event malformedGateControls = M14DlcEvent11410001SourceEvent();
            malformedGateControls.Instructions[1] = InsHex(4, 0, "00000000F386150001000000");
            EMEVD.Instruction[] malformedGateControlsBody = malformedGateControls.Instructions.ToArray();
            EMEVD malformedGateControlsArchive = Evd(events.Where(evt => evt.ID != 11410001)
                .Append(malformedGateControls).ToArray());
            bool malformedGateControlsRejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM14DlcEvents(malformedGateControlsArchive); }
            catch (InvalidDataException) { malformedGateControlsRejected = true; }
            Check(malformedGateControlsRejected
                && malformedGateControls.Instructions.Zip(malformedGateControlsBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M14 DLC event 11410001 was accepted or mutated");

            EMEVD.Event generatedWatcher = M14DlcGateGeneratedEvent();
            EMEVD.Instruction[] generatedBody = generatedWatcher.Instructions.ToArray();
            EMEVD generatedArchive = Evd(
                M14DlcEvent11410001SourceEvent(),
                TerminatedSourceEvent(11410095, 14, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11415340, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11415370, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11415373, 10, EMEVD.Event.RestBehaviorType.Default),
                generatedWatcher,
                M14DlcObjectSfxSourceEvent(11410901, 0x00158B32, 0x00158B33, 0x00158B34, 0x00158B35),
                M14DlcObjectSfxSourceEvent(11410410, 0x00158952, 0x00158953, 0x00158954, 0x00158955),
                ObjectSfxSourceEvent(11410900, 12, EMEVD.Event.RestBehaviorType.Default, 0x00158ACE, 0, 0x00158ACF));
            FogModifiedEventBuilder.ReconstructKnownM14DlcEvents(generatedArchive);
            Check(generatedWatcher.Instructions.SequenceEqual(generatedBody), "generated 11415380 watcher was changed");
            FogModifiedEventBuilder.ReconstructKnownM14DlcEvents(generatedArchive);
            Check(generatedWatcher.Instructions.SequenceEqual(generatedBody), "generated 11415380 watcher is not idempotent");

            EMEVD.Event malformedWatcher = M14DlcGateGeneratedEvent();
            malformedWatcher.Instructions[1] = WaitForArea(0x00158F21);
            EMEVD malformedArchive = Evd(
                M14DlcEvent11410001SourceEvent(),
                TerminatedSourceEvent(11410095, 14, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11415340, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11415370, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11415373, 10, EMEVD.Event.RestBehaviorType.Default),
                malformedWatcher,
                ObjectSfxSourceEvent(11410900, 12, EMEVD.Event.RestBehaviorType.Default, 0x00158ACE, 0, 0x00158ACF));
            bool malformedRejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM14DlcEvents(malformedArchive); }
            catch (InvalidDataException) { malformedRejected = true; }
            Check(malformedRejected, "malformed 11415380 generated watcher was accepted");
        }

        private static void AssertM14Events()
        {
            EMEVD.Event objectSfxCleanup = TerminatedSourceEvent(11400001, 10,
                EMEVD.Event.RestBehaviorType.Default);
            objectSfxCleanup.Instructions[3] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x00156486, (byte)0 });
            objectSfxCleanup.Instructions[4] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x00156487, (byte)1 });
            objectSfxCleanup.Instructions[5] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x00156488, (byte)0 });
            objectSfxCleanup.Instructions[6] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x00156489, (byte)1 });
            EMEVD.Instruction[] expectedCleanup = objectSfxCleanup.Instructions
                .Where((_, index) => index < 3 || index > 6).ToArray();
            EMEVD.Event removal = TerminatedSourceEvent(11400200, 21, EMEVD.Event.RestBehaviorType.Default);
            removal.Instructions[4] = new EMEVD.Instruction(2003, 16, Convert.FromHexString("1A270000"));
            EMEVD.Instruction[] originalBody = removal.Instructions.ToArray();
            EMEVD.Event gate = TerminatedSourceEvent(11405390, 6, EMEVD.Event.RestBehaviorType.Default);
            EMEVD archive = Evd(objectSfxCleanup, removal, gate);

            FogModifiedEventBuilder.ReconstructKnownM14Events(archive);
            Check(objectSfxCleanup.Instructions.Count == 6
                && objectSfxCleanup.Instructions.Zip(expectedCleanup)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "11400001 reconstruction changed operations beyond its exact object/SFX sequence");
            Check(removal.Instructions.Count == originalBody.Length - 1
                && !removal.Instructions.Any(instruction => instruction.Bank == 2003 && instruction.ID == 16
                    && instruction.ArgData.SequenceEqual(Convert.FromHexString("1A270000"))),
                "11400200 TriggerMultiplayerEvent was not removed");
            Check(removal.Instructions.SequenceEqual(originalBody.Where((_, index) => index != 4)),
                "11400200 body changed beyond the authored operation removal");
            CheckPrefix(gate, new[] { EndInstruction() }, "11405390 disabled gate");
            FogModifiedEventBuilder.ReconstructKnownM14Events(archive);
            Check(removal.Instructions.Count == originalBody.Length - 1
                && gate.Instructions.Count == 7 && objectSfxCleanup.Instructions.Count == 6
                && objectSfxCleanup.Instructions.Zip(expectedCleanup)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M14 event reconstruction is not idempotent");
        }

        private static EMEVD.Event M14DlcObjectSfxSourceEvent(long eventId, int firstObject, int firstSfx,
            int secondObject, int secondSfx)
        {
            EMEVD.Event evt = TerminatedSourceEvent(eventId, 12, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[4] = new EMEVD.Instruction(2005, 3,
                new object[] { firstObject, (byte)0 });
            evt.Instructions[5] = new EMEVD.Instruction(2006, 1,
                new object[] { firstSfx, (byte)1 });
            evt.Instructions[6] = new EMEVD.Instruction(2005, 3,
                new object[] { secondObject, (byte)0 });
            evt.Instructions[7] = new EMEVD.Instruction(2006, 1,
                new object[] { secondSfx, (byte)1 });
            return evt;
        }

        private static EMEVD.Event M14DlcEvent11410001SourceEvent() => Evt(11410001,
            InsHex(2005, 3, "6E8B150000000000"),
            InsHex(4, 0, "00000000F286150001000000"),
            InsHex(2003, 2, "0A00000001000000"),
            InsHex(2003, 12, "F0861500"),
            InsHex(1003, 5, "01010000"),
            InsHex(2003, 28, "24000000"),
            InsHex(2008, 3, "0E010000"),
            InsHex(2005, 3, "968B150000000000"),
            InsHex(2006, 1, "978B150001000000"),
            InsHex(2000, 2, "00000000"),
            InsHex(2006, 3, "000000006E8B1500FFFFFFFF9E5F0100"),
            InsHex(1001, 0, "00000040"),
            InsHex(2005, 3, "6E8B150001000000"),
            InsHex(2009, 3, "E81DAE006E8B1500000000400000344300000000"));

        private static EMEVD.Event M14DlcGateVanillaEvent()
        {
            var evt = new EMEVD.Event(11415380, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions.AddRange(M14DlcGateVanillaBodyForTest());
            return evt;
        }

        private static EMEVD.Event M14DlcGateGeneratedEvent()
        {
            var evt = new EMEVD.Event(11415380, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions.Add(new EMEVD.Instruction(3, 2, new object[] { (sbyte)-7, (byte)1, 10000, 0x001BB5D0 }));
            evt.Instructions.Add(new EMEVD.Instruction(3, 2, new object[] { (sbyte)-7, (byte)1, 10000, 0x00158F20 }));
            evt.Instructions.Add(new EMEVD.Instruction(0, 0, new object[] { (byte)0, (byte)1, (sbyte)-7, (byte)0 }));
            evt.Instructions.Add(EndInstruction());
            evt.Instructions.AddRange(M14DlcGateVanillaBodyForTest());
            return evt;
        }

        private static EMEVD.Instruction[] M14DlcGateVanillaBodyForTest() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000D51DAE00")),
            new EMEVD.Instruction(3, 19, Convert.FromHexString(
                "0100000001000000228F150000000000FFFF00000000000023BF98003000000000000000328B1500")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 14, Convert.FromHexString("10270000218F1500")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000F21C000000000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static void KnownVanillaObjectSfxPairRemoved()
        {
            EMEVD.Event objectSfxWatcher = TerminatedSourceEvent(11000001, 6, EMEVD.Event.RestBehaviorType.Default);
            objectSfxWatcher.Instructions[2] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x000F4A06, (byte)0 });
            objectSfxWatcher.Instructions[3] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x000F4A07, (byte)1 });
            EMEVD.Instruction[] original = objectSfxWatcher.Instructions.ToArray();
            EMEVD.Event gateWatcher = TerminatedSourceEvent(11005390, 6, EMEVD.Event.RestBehaviorType.Default);
            EMEVD archive = Evd(objectSfxWatcher, gateWatcher,
                M10GateConditionSourceEvent(11005030, 22, "68ECA700", "6DECA700"),
                M10GateObjectStateSourceEvent(11005033, 21, 8, 12, 13, "6BECA700", "6EECA700"),
                M10WatcherSourceEvent(),
                M10Event11005392SourceEvent());

            FogModifiedEventBuilder.ReconstructKnownM10Events(archive);
            EMEVD.Instruction[] expected = original.Take(2).Concat(original.Skip(4)).ToArray();
            Check(objectSfxWatcher.Instructions.Count == 4
                && objectSfxWatcher.Instructions.Zip(expected).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "the exact vanilla DeactivateObject/DeleteMapSFX pair was not removed");

            FogModifiedEventBuilder.ReconstructKnownM10Events(archive);
            Check(objectSfxWatcher.Instructions.Count == 4
                && objectSfxWatcher.Instructions.Zip(expected).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "object/SFX pair reconstruction was not idempotent");
        }

        private static void M12Event11200900ObjectSfxSequenceRemoved()
        {
            EMEVD.Event source = M12Event11200900SourceEvent();
            EMEVD.Instruction[] expectedBody = source.Instructions.Take(4).ToArray();
            EMEVD archive = M12ArchiveWithObjectSfxSourceEvent(source);
            FogModifiedEventBuilder.ReconstructKnownM12Events(archive);
            Check(source.Instructions.Count == 4 && source.Instructions.Zip(expectedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11200900 changed operations beyond its exact adjacent object/SFX pairs");

            EMEVD.Instruction[] reconstructedBody = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM12Events(archive);
            Check(source.Instructions.Count == reconstructedBody.Length
                && source.Instructions.Zip(reconstructedBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11200900 object/SFX reconstruction is not idempotent");

            EMEVD.Event malformed = M12Event11200900SourceEvent();
            malformed.Instructions[6] = InsHex(2005, 3, "E456120000000001");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12Events(M12ArchiveWithObjectSfxSourceEvent(malformed)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed event 11200900 object/SFX sequence was accepted or mutated");
        }

        private static void M12Event11200006BodyReconstructed()
        {
            EMEVD.Event source = M12Event11200006SourceEvent();
            EMEVD.Instruction[] expectedBody = new[]
            {
                source.Instructions[4], source.Instructions[6], source.Instructions[7], source.Instructions[8],
                source.Instructions[9], InsHex(2003, 2, "B714000001000000"), source.Instructions[13],
            };
            EMEVD archive = M12ArchiveWithObjectSfxSourceEvent(M12Event11200900SourceEvent(), source);

            FogModifiedEventBuilder.ReconstructKnownM12Events(archive);
            Check(source.Instructions.Count == expectedBody.Length && source.Instructions.Zip(expectedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 event 11200006 differs from its exact reference body");
            EMEVD.Instruction[] reconstructedBody = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM12Events(archive);
            Check(source.Instructions.Count == reconstructedBody.Length && source.Instructions.Zip(reconstructedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "M12 event 11200006 reconstruction is not idempotent");

            EMEVD.Event malformed = M12Event11200006SourceEvent();
            malformed.Instructions[7] = InsHex(3, 5, "01000000010000005857120000000000FFFF000000000000F4BD98003000000000000001");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            EMEVD malformedArchive = M12ArchiveWithObjectSfxSourceEvent(M12Event11200900SourceEvent(), malformed);
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM12Events(malformedArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Zip(malformedBody)
                .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed M12 event 11200006 was accepted or partially mutated");
        }

        private static void M10Event11005392ConditionsRemoved()
        {
            EMEVD.Event source = M10Event11005392SourceEvent();
            EMEVD.Instruction[] original = source.Instructions.ToArray();
            EMEVD.Instruction[] expected = original.Take(13).Concat(original.Skip(16)).ToArray();
            EMEVD archive = M10ArchiveWithGateConditionEvent(source);

            FogModifiedEventBuilder.ReconstructKnownM10Events(archive);
            Check(source.Instructions.Count == expected.Length
                && source.Instructions.Zip(expected).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11005392 did not remove only its exact adjacent condition and skip instructions");
            EMEVD.Instruction[] reconstructed = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM10Events(archive);
            Check(source.Instructions.Count == reconstructed.Length
                && source.Instructions.Zip(reconstructed).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11005392 condition removal was not idempotent");

            EMEVD.Event malformed = M10Event11005392SourceEvent();
            malformed.Instructions[14] = InsHex(4, 3, "FF0000001027000003000000");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM10Events(M10ArchiveWithGateConditionEvent(malformed)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedBody.Length
                && malformed.Instructions.Zip(malformedBody).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed event 11005392 condition sequence was accepted or mutated");
        }

        private static EMEVD.Event M10Event11005392SourceEvent()
        {
            var evt = new EMEVD.Event(11005392, EMEVD.Event.RestBehaviorType.Restart);
            for (int i = 0; i < 26; i++) evt.Instructions.Add(InsHex(1000, 4, "00000000"));
            evt.Instructions[13] = InsHex(4, 3, "FF000000102700000C000000");
            evt.Instructions[14] = InsHex(4, 3, "FF0000001027000002000000");
            evt.Instructions[15] = InsHex(1000, 2, "0001FF00");
            return evt;
        }

        private static EMEVD M10ArchiveWithGateConditionEvent(EMEVD.Event conditionEvent) => Evd(
            ObjectSfxSourceEvent(11000001, 6, EMEVD.Event.RestBehaviorType.Default, 0x000F4A06, 0, 0x000F4A07),
            TerminatedSourceEvent(11005390, 6, EMEVD.Event.RestBehaviorType.Default),
            M10GateConditionSourceEvent(11005030, 22, "68ECA700", "6DECA700"),
            M10GateObjectStateSourceEvent(11005033, 21, 8, 12, 13, "6BECA700", "6EECA700"),
            M10WatcherSourceEvent(), conditionEvent);

        private static void M10ParishEvent11010000ConditionsRemoved()
        {
            EMEVD.Event source = M10ParishEvent11010000SourceEvent();
            EMEVD.Instruction[] original = source.Instructions.ToArray();
            EMEVD.Instruction[] expected = original.Take(12).Concat(original.Skip(15)).ToArray();
            EMEVD archive = M10ParishArchiveWithConditionEvent(source);

            FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(archive);
            Check(source.Instructions.Count == expected.Length
                && source.Instructions.Zip(expected).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11010000 did not remove only its exact adjacent condition and skip instructions");
            EMEVD.Instruction[] reconstructed = source.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(archive);
            Check(source.Instructions.Count == reconstructed.Length
                && source.Instructions.Zip(reconstructed).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "event 11010000 condition removal was not idempotent");

            EMEVD.Event malformed = M10ParishEvent11010000SourceEvent();
            malformed.Instructions[14] = InsHex(1000, 2, "0002FF00");
            EMEVD.Instruction[] malformedBody = malformed.Instructions.ToArray();
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(M10ParishArchiveWithConditionEvent(malformed)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == malformedBody.Length
                && malformed.Instructions.Zip(malformedBody).All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed event 11010000 condition sequence was accepted or mutated");
        }

        private static EMEVD.Event M10ParishEvent11010000SourceEvent()
        {
            var evt = new EMEVD.Event(11010000, EMEVD.Event.RestBehaviorType.Restart);
            for (int i = 0; i < 22; i++) evt.Instructions.Add(InsHex(1000, 4, "00000000"));
            evt.Instructions[12] = InsHex(4, 3, "FF0000001027000002000000");
            evt.Instructions[13] = InsHex(4, 3, "FF000000102700000C000000");
            evt.Instructions[14] = InsHex(1000, 2, "0001FF00");
            return evt;
        }

        private static EMEVD M10ParishArchiveWithConditionEvent(EMEVD.Event conditionEvent) => Evd(
            M10ParishObjectSfxSourceEvent(),
            M10ParishInteractionSourceEvent(),
            ObjectSfxSourceEvent(11010902, 20, EMEVD.Event.RestBehaviorType.Restart, 0x000F704E, 0, 0x000F704F),
            M10GateConditionSourceEvent(11015100, 25, "BE13A800", "C213A800"),
            M10GateObjectStateSourceEvent(11015103, 22, 8, 11, 12, "C113A800", "C313A800"),
            TerminatedSourceEvent(11015370, 6, EMEVD.Event.RestBehaviorType.Default),
            TerminatedSourceEvent(11015380, 6, EMEVD.Event.RestBehaviorType.Default),
            TerminatedSourceEvent(11015390, 6, EMEVD.Event.RestBehaviorType.Default),
            conditionEvent);

        private static void KnownM10GateConditionSetsRemoved()
        {
            EMEVD.Event burgGateChecks = M10GateConditionSourceEvent(11005030, 22,
                "68ECA700", "6DECA700");
            EMEVD.Event burgObjectState = M10GateObjectStateSourceEvent(11005033, 21, 8, 12, 13,
                "6BECA700", "6EECA700");
            EMEVD.Event watcher = M10WatcherSourceEvent();
            EMEVD.Event burgArchiveGate = TerminatedSourceEvent(11005390, 6, EMEVD.Event.RestBehaviorType.Default);
            EMEVD m10Archive = Evd(
                ObjectSfxSourceEvent(11000001, 6, EMEVD.Event.RestBehaviorType.Default, 0x000F4A06, 0, 0x000F4A07),
                burgArchiveGate,
                burgGateChecks,
                burgObjectState,
                watcher,
                M10Event11005392SourceEvent());
            FogModifiedEventBuilder.ReconstructKnownM10Events(m10Archive);
            Check(burgGateChecks.Instructions.Count == 19 && !HasM10GateConditionRemovals(burgGateChecks),
                "Undead Burg condition operations were not removed");
            Check(burgObjectState.Instructions.Count == 20
                && EmevdMerge.SameInstruction(burgObjectState.Instructions[18], InsHex(3, 0, "000100006BECA700"))
                && EmevdMerge.SameInstruction(burgObjectState.Instructions[19], InsHex(2004, 8, "BF1900004A150000")),
                "Undead Burg gate object-state tail was not reconstructed");
            Check(watcher.Instructions.Count == 9 && watcher.Instructions[0].Bank == 2000
                && watcher.Instructions[0].ID == 2 && watcher.Instructions[4].Bank == 3
                && watcher.Instructions[4].ID == 18,
                "Undead Burg watcher operation conversion was not reconstructed");
            FogModifiedEventBuilder.ReconstructKnownM10Events(m10Archive);
            Check(burgGateChecks.Instructions.Count == 19 && !HasM10GateConditionRemovals(burgGateChecks)
                && burgObjectState.Instructions.Count == 20 && watcher.Instructions.Count == 9,
                "Undead Burg condition removal is not idempotent");

            EMEVD.Event parishGateChecks = M10GateConditionSourceEvent(11015100, 25,
                "BE13A800", "C213A800");
            EMEVD.Event parishObjectState = M10GateObjectStateSourceEvent(11015103, 22, 8, 11, 12,
                "C113A800", "C313A800");
            EMEVD.Event[] parishArchiveGates =
            {
                TerminatedSourceEvent(11015370, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11015380, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11015390, 6, EMEVD.Event.RestBehaviorType.Default),
            };
            EMEVD parishArchive = Evd(new[]
            {
                M10ParishObjectSfxSourceEvent(),
                M10ParishInteractionSourceEvent(),
                ObjectSfxSourceEvent(11010902, 20, EMEVD.Event.RestBehaviorType.Restart, 0x000F704E, 0, 0x000F704F),
                parishGateChecks,
                parishObjectState,
                M10ParishEvent11010000SourceEvent(),
            }.Concat(parishArchiveGates).ToArray());
            FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(parishArchive);
            Check(parishGateChecks.Instructions.Count == 22 && !HasM10GateConditionRemovals(parishGateChecks),
                "Parish condition operations were not removed");
            Check(parishObjectState.Instructions.Count == 21
                && EmevdMerge.SameInstruction(parishObjectState.Instructions[19], InsHex(3, 0, "00010000C113A800"))
                && EmevdMerge.SameInstruction(parishObjectState.Instructions[20], InsHex(2004, 8, "BE1900004A150000")),
                "Parish gate object-state tail was not reconstructed");
            FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(parishArchive);
            Check(parishGateChecks.Instructions.Count == 22 && !HasM10GateConditionRemovals(parishGateChecks)
                && parishObjectState.Instructions.Count == 21,
                "Parish condition removal is not idempotent");

            EMEVD.Event duplicate = M10GateConditionSourceEvent(11005030, 22,
                "68ECA700", "6DECA700");
            duplicate.Instructions[13] = InsHex(3, 24, "01050200");
            EMEVD duplicateArchive = Evd(
                ObjectSfxSourceEvent(11000001, 6, EMEVD.Event.RestBehaviorType.Default, 0x000F4A06, 0, 0x000F4A07),
                TerminatedSourceEvent(11005390, 6, EMEVD.Event.RestBehaviorType.Default),
                duplicate,
                M10GateObjectStateSourceEvent(11005033, 21, 8, 12, 13,
                    "6BECA700", "6EECA700"),
                M10WatcherSourceEvent(),
                M10Event11005392SourceEvent());
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM10Events(duplicateArchive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && duplicate.Instructions.Count == 22,
                "ambiguous duplicate condition operation was accepted or mutated");
        }

        private static void KnownM10NewLondoTransitionReconstructed()
        {
            EMEVD.Event transition = M10NewLondoTransitionSourceEvent();
            EMEVD.Event dlcArea = M10NewLondoDlcAreaSourceEvent();
            EMEVD.Event flagCleanup = M10NewLondoFlagCleanupSourceEvent();
            EMEVD.Instruction[] expectedFlagCleanup = flagCleanup.Instructions
                .Where((_, index) => index != 2 && index != 3 && index != 4 && index != 21).ToArray();
            EMEVD archive = Evd(transition, dlcArea, flagCleanup);
            FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents(archive);
            Check(transition.Instructions.Count == 10
                && transition.Instructions[3].Bank == 1001 && transition.Instructions[3].ID == 0
                && transition.Instructions[4].Bank == 2003 && transition.Instructions[4].ID == 2
                && transition.Instructions[4].ArgData.SequenceEqual(Convert.FromHexString("B514000001000000"))
                && transition.Instructions[8].Bank == 1001 && transition.Instructions[8].ID == 1,
                "New Londo gate transition order or completion flag is incorrect");
            Check(flagCleanup.Instructions.Count == 37
                && flagCleanup.Instructions.Zip(expectedFlagCleanup)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "New Londo event 11020001 changed operations beyond its exact flag-condition set");
            byte[] reconstructed = transition.Instructions.SelectMany(instruction => instruction.ArgData).ToArray();
            EMEVD.Instruction[] reconstructedFlagCleanup = flagCleanup.Instructions.ToArray();
            FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents(archive);
            Check(transition.Instructions.Count == 10
                && transition.Instructions.SelectMany(instruction => instruction.ArgData).SequenceEqual(reconstructed),
                "New Londo gate transition reconstruction is not idempotent");
            Check(flagCleanup.Instructions.Count == reconstructedFlagCleanup.Length
                && flagCleanup.Instructions.Zip(reconstructedFlagCleanup)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "New Londo event 11020001 cleanup is not idempotent");

            Check(dlcArea.Instructions.Count == 28
                && dlcArea.Instructions[0].Bank == 1003 && dlcArea.Instructions[0].ID == 6
                && dlcArea.Instructions[1].Bank == 2005 && dlcArea.Instructions[1].ID == 7
                && dlcArea.Instructions[2].Bank == 2005 && dlcArea.Instructions[2].ID == 6
                && dlcArea.Parameters.Count == 2
                && dlcArea.Parameters.All(parameter => parameter.InstructionIndex == 24)
                && dlcArea.Parameters[0].TargetStartByte == 0 && dlcArea.Parameters[0].SourceStartByte == 0
                && dlcArea.Parameters[1].TargetStartByte == 4 && dlcArea.Parameters[1].SourceStartByte == 4,
                "New Londo DLC event condition was not removed or parameter bindings were not reindexed");
            byte[] reconstructedDlcBody = dlcArea.Instructions.SelectMany(instruction => instruction.ArgData).ToArray();
            FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents(archive);
            Check(dlcArea.Instructions.Count == 28
                && dlcArea.Instructions.SelectMany(instruction => instruction.ArgData).SequenceEqual(reconstructedDlcBody)
                && dlcArea.Parameters.All(parameter => parameter.InstructionIndex == 24),
                "New Londo DLC event reconstruction is not idempotent");

            EMEVD.Event malformed = M10NewLondoTransitionSourceEvent();
            malformed.Instructions[3] = InsHex(1001, 0, "0000A040");
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents(Evd(malformed,
                M10NewLondoDlcAreaSourceEvent(), M10NewLondoFlagCleanupSourceEvent())); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformed.Instructions.Count == 11
                && malformed.Instructions[4].Bank == 2002 && malformed.Instructions[4].ID == 4,
                "unexpected New Londo transition body was accepted or mutated");

            EMEVD.Event malformedDlc = M10NewLondoDlcAreaSourceEvent();
            malformedDlc.Parameters[0] = new EMEVD.Parameter(25, 0, 0, 4);
            rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents(Evd(M10NewLondoTransitionSourceEvent(),
                malformedDlc, M10NewLondoFlagCleanupSourceEvent())); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && malformedDlc.Instructions.Count == 30
                && malformedDlc.Instructions[1].Bank == 1003 && malformedDlc.Parameters[0].InstructionIndex == 25,
                "unexpected New Londo DLC parameter table was accepted or mutated");

            EMEVD.Event changedSkipCount = M10NewLondoFlagCleanupSourceEvent();
            changedSkipCount.Instructions[2].ArgData[0] = 3;
            AssertM10NewLondoFlagCleanupRefused(changedSkipCount, "changed skip count");

            EMEVD.Event changedCoveredOperation = M10NewLondoFlagCleanupSourceEvent();
            changedCoveredOperation.Instructions[3] = InsHex(3, 8, "00010B02");
            AssertM10NewLondoFlagCleanupRefused(changedCoveredOperation, "changed covered operation");

            EMEVD.Event changedLayer = M10NewLondoFlagCleanupSourceEvent();
            changedLayer.Instructions[3].Layer = 1;
            AssertM10NewLondoFlagCleanupRefused(changedLayer, "changed instruction layer");

            EMEVD.Event unexpectedSkip = M10NewLondoFlagCleanupSourceEvent();
            unexpectedSkip.Instructions[6] = InsHex(1003, 1, "01010000D034B400");
            AssertM10NewLondoFlagCleanupRefused(unexpectedSkip, "an additional skip range");

            EMEVD.Event parameterized = M10NewLondoFlagCleanupSourceEvent();
            parameterized.Parameters.Add(new EMEVD.Parameter(2, 0, 0, 1));
            AssertM10NewLondoFlagCleanupRefused(parameterized, "unexpected event parameters");
        }

        private static void AssertM10NewLondoFlagCleanupRefused(EMEVD.Event candidate, string description)
        {
            EMEVD.Instruction[] originalInstructions = candidate.Instructions.ToArray();
            int originalParameterCount = candidate.Parameters.Count;
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM10NewLondoEvents(Evd(
                M10NewLondoTransitionSourceEvent(), M10NewLondoDlcAreaSourceEvent(), candidate)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && candidate.Instructions.Count == originalInstructions.Length
                && candidate.Instructions.Zip(originalInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && candidate.Parameters.Count == originalParameterCount,
                "New Londo event 11020001 accepted or mutated " + description);
        }

        private static EMEVD.Event M10NewLondoTransitionSourceEvent() => Evt(11020021,
            InsHex(2000, 2, "00000000"),
            InsHex(3, 0, "00010000A43AA800"),
            InsHex(1003, 1, "01010000E026A800"),
            InsHex(1001, 0, "0000A041"),
            InsHex(2002, 4, "86870100000000008EA61B001201000010270000"),
            InsHex(2002, 3, "A2BF02000000000010270000"),
            InsHex(1001, 1, "01000000"),
            InsHex(2003, 2, "E026A80001000000"),
            InsHex(2003, 2, "A43AA80000000000"),
            InsHex(2004, 9, "10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Event M10NewLondoFlagCleanupSourceEvent()
        {
            EMEVD.Event evt = TerminatedSourceEvent(11020001, 41, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[2] = InsHex(1003, 7, "02011201");
            evt.Instructions[3] = InsHex(3, 8, "00010A02");
            evt.Instructions[4] = InsHex(2003, 2, "D034B40001000000");
            evt.Instructions[21] = InsHex(3, 0, "00010000D034B400");
            return evt;
        }

        private static EMEVD.Event M10NewLondoDlcAreaSourceEvent()
        {
            EMEVD.Event evt = Evt(11020899,
                InsHex(1003, 6, "00010000"),
                InsHex(1003, 1, "04010000622AA800"),
                InsHex(2003, 2, "622AA80001000000"),
                InsHex(2005, 7, "FA960F0000000000"),
                InsHex(2005, 6, "FA960F00FFFFFFFF00000000"),
                InsHex(1000, 4, "00000000"),
                InsHex(1001, 1, "01000000"),
                InsHex(1003, 1, "14000000943BA800"),
                InsHex(5, 2, "00000000A029A800"),
                InsHex(1001, 1, "0A000000"),
                InsHex(2005, 4, "04970F0001000000"),
                InsHex(2005, 4, "05970F0001000000"),
                InsHex(2005, 4, "06970F0001000000"),
                InsHex(2005, 4, "07970F0001000000"),
                InsHex(2005, 4, "08970F0001000000"),
                InsHex(2005, 4, "09970F0001000000"),
                InsHex(2005, 4, "0A970F0001000000"),
                InsHex(2005, 4, "0B970F0001000000"),
                InsHex(2005, 15, "04970F00"),
                InsHex(2005, 15, "05970F00"),
                InsHex(2005, 15, "06970F00"),
                InsHex(2005, 15, "07970F00"),
                InsHex(2005, 15, "08970F00"),
                InsHex(2005, 15, "09970F00"),
                InsHex(2005, 15, "0A970F00"),
                InsHex(2005, 15, "0B970F00"),
                InsHex(2003, 22, "000000000000000001000000"),
                InsHex(1000, 4, "00000000"),
                InsHex(2005, 7, "FA960F0000000000"),
                InsHex(2005, 6, "FA960F00FFFFFFFF00000000"));
            evt.Parameters.Add(new EMEVD.Parameter(26, 0, 0, 4));
            evt.Parameters.Add(new EMEVD.Parameter(26, 4, 4, 4));
            return evt;
        }

        private static EMEVD.Event M10GateConditionSourceEvent(long eventId, int instructionCount,
            string firstFlagHex, string secondFlagHex)
        {
            EMEVD.Event evt = TerminatedSourceEvent(eventId, instructionCount, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[8] = InsHex(3, 24, "01050200");
            evt.Instructions[11] = InsHex(3, 0, "01000000" + firstFlagHex);
            evt.Instructions[12] = InsHex(3, 0, "01000000" + secondFlagHex);
            return evt;
        }

        private static EMEVD.Event M10ParishObjectSfxSourceEvent()
        {
            EMEVD.Event evt = TerminatedSourceEvent(11010001, 18, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[14] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x000F7116, (byte)0 });
            evt.Instructions[15] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x000F7117, (byte)1 });
            evt.Instructions[16] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x000F7118, (byte)0 });
            evt.Instructions[17] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x000F7119, (byte)1 });
            return evt;
        }

        private static EMEVD.Event M10ParishInteractionSourceEvent()
        {
            var evt = new EMEVD.Event(11010901, EMEVD.Event.RestBehaviorType.Restart);
            for (int i = 0; i < 14; i++)
                evt.Instructions.Add(new EMEVD.Instruction(2000, 0, new byte[] { (byte)i }));
            evt.Instructions[10] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x000F70B2, (byte)0 });
            evt.Instructions[11] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x000F70B3, (byte)1 });
            evt.Instructions[12] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x000F70B4, (byte)0 });
            evt.Instructions[13] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x000F70B5, (byte)1 });
            return evt;
        }

        private static EMEVD.Event M12Event11200005SourceEvent() => Evt(11200005,
            InsHex(3, 0, "FF01000065040000"),
            InsHex(3, 0, "FF01000066040000"),
            InsHex(3, 0, "FF01000067040000"),
            InsHex(0, 0, "0101FF00"),
            InsHex(3, 4, "01030000D809000001000000"),
            InsHex(3, 0, "0100000011000000"),
            InsHex(0, 0, "00010100"),
            InsHex(1003, 6, "00010000"),
            InsHex(3, 6, "00030000"),
            InsHex(2006, 2, "59571200"),
            InsHex(3, 6, "00020000"),
            InsHex(2006, 1, "5957120001000000"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Event M12Event11200006SourceEvent() => Evt(11200006,
            InsHex(3, 0, "FF01000065040000"),
            InsHex(3, 0, "FF01000066040000"),
            InsHex(3, 0, "FF01000067040000"),
            InsHex(0, 0, "0101FF00"),
            InsHex(3, 4, "01030000D809000001000000"),
            InsHex(3, 0, "0100000011000000"),
            InsHex(3, 6, "01030000"),
            InsHex(3, 5, "01000000010000005857120000000000FFFF000000000000F4BD98003000000000000000"),
            InsHex(0, 0, "00010100"),
            InsHex(1003, 6, "00010000"),
            InsHex(2002, 4, "C2D40100000000005E8012000C01000010270000"),
            InsHex(1001, 1, "01000000"),
            InsHex(2000, 5, "00000000"),
            InsHex(1000, 4, "01000000"));

        private static EMEVD.Event M12Event11200900SourceEvent() => Evt(11200900,
            InsHex(4, 0, "00000000A152120001000000"),
            InsHex(2004, 21, "102700007C150000"),
            InsHex(2003, 2, "84E9AA0001000000"),
            InsHex(2003, 12, "A1521200"),
            InsHex(2005, 3, "E256120000000000"),
            InsHex(2006, 1, "E356120001000000"),
            InsHex(2005, 3, "E456120000000000"),
            InsHex(2006, 1, "E556120001000000"));

        private static EMEVD M12ArchiveWithObjectSfxSourceEvent(EMEVD.Event source, EMEVD.Event progression = null) => Evd(
            M12SharedObjectSfxSourceEvent(),
            M12Event11200005SourceEvent(),
            progression ?? M12Event11200006SourceEvent(),
            source,
            ObjectSfxSourceEvent(11200001, 5, EMEVD.Event.RestBehaviorType.Default, 0x00125746, 0, 0x00125747),
            ObjectSfxSourceEvent(11200002, 30, EMEVD.Event.RestBehaviorType.Default, 0x00125746, 1, 0x00125747, true),
            TerminatedSourceEvent(11205380, 6, EMEVD.Event.RestBehaviorType.Default),
            TerminatedSourceEvent(11205390, 7, EMEVD.Event.RestBehaviorType.Default));

        private static EMEVD.Event M12DlcSharedObjectSfxSourceEvent()
        {
            var evt = new EMEVD.Event(11210000, EMEVD.Event.RestBehaviorType.Restart);
            for (int i = 0; i < 16; i++)
                evt.Instructions.Add(new EMEVD.Instruction(2000, 0, new byte[] { (byte)i }));
            evt.Instructions[12] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x00127D8E, (byte)0 });
            evt.Instructions[13] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x00127D8F, (byte)1 });
            evt.Instructions[14] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x00127D90, (byte)0 });
            evt.Instructions[15] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x00127D91, (byte)1 });
            return evt;
        }

        private static EMEVD.Event M12SharedObjectSfxSourceEvent()
        {
            EMEVD.Event evt = TerminatedSourceEvent(11200000, 34, EMEVD.Event.RestBehaviorType.Restart);
            evt.Instructions[3] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x00125746, (byte)0 });
            evt.Instructions[4] = new EMEVD.Instruction(2006, 1,
                new object[] { 0x00125747, (byte)0 });
            evt.Instructions[31] = new EMEVD.Instruction(2005, 3,
                new object[] { 0x00125746, (byte)1 });
            evt.Instructions[32] = new EMEVD.Instruction(2006, 2,
                new object[] { 0x00125747 });
            return evt;
        }

        private static void AssertM10ParishLandingRefused(EMEVD.Event landingEvent)
        {
            EMEVD.Instruction[] originalInstructions = landingEvent.Instructions
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID, instruction.ArgData.ToArray()))
                .ToArray();
            EMEVD archive = Evd(landingEvent,
                M10ParishEvent11010000SourceEvent(),
                TerminatedSourceEvent(11015370, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11015380, 6, EMEVD.Event.RestBehaviorType.Default),
                TerminatedSourceEvent(11015390, 6, EMEVD.Event.RestBehaviorType.Default));
            bool rejected = false;
            try { FogModifiedEventBuilder.ReconstructKnownM10ParishEvents(archive); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && landingEvent.Instructions.Count == originalInstructions.Length
                && landingEvent.Instructions.Zip(originalInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "malformed Parish landing object/SFX sequence was accepted or mutated");
        }

        private static EMEVD.Event M10GateObjectStateSourceEvent(long eventId, int instructionCount,
            int conditionIndex, int firstFlagIndex, int secondFlagIndex, string firstFlagHex, string secondFlagHex)
        {
            EMEVD.Event evt = TerminatedSourceEvent(eventId, instructionCount, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions[conditionIndex] = InsHex(3, 24, "01050200");
            evt.Instructions[firstFlagIndex] = InsHex(3, 0, "01000000" + firstFlagHex);
            evt.Instructions[secondFlagIndex] = InsHex(3, 0, "01000000" + secondFlagHex);
            return evt;
        }

        private static EMEVD.Event M10WatcherSourceEvent() => Evt(11005391,
            InsHex(3, 0, "0100000002000000"),
            InsHex(3, 0, "01010000D1EDA700"),
            InsHex(4, 3, "010000001027000001000000"),
            InsHex(3, 19, "0100000001000000F64D0F0000000000FFFF00000000000023BF9800FF00000000000000064A0F00"),
            InsHex(0, 0, "00010100"),
            InsHex(2004, 14, "10270000F54D0F00"),
            InsHex(2003, 18, "10270000F21C000000000000"),
            InsHex(1000, 4, "01000000"));

        private static bool HasM10GateConditionRemovals(EMEVD.Event evt) => evt.Instructions.Any(instruction =>
            (instruction.Bank == 3 && instruction.ID == 24
                && instruction.ArgData.SequenceEqual(Convert.FromHexString("01050200")))
            || (instruction.Bank == 3 && instruction.ID == 0
                && (instruction.ArgData.SequenceEqual(Convert.FromHexString("0100000068ECA700"))
                    || instruction.ArgData.SequenceEqual(Convert.FromHexString("010000006DECA700"))
                    || instruction.ArgData.SequenceEqual(Convert.FromHexString("010000006BECA700"))
                    || instruction.ArgData.SequenceEqual(Convert.FromHexString("010000006EECA700"))
                    || instruction.ArgData.SequenceEqual(Convert.FromHexString("01000000BE13A800"))
                    || instruction.ArgData.SequenceEqual(Convert.FromHexString("01000000C213A800"))
                    || instruction.ArgData.SequenceEqual(Convert.FromHexString("01000000C113A800"))
                    || instruction.ArgData.SequenceEqual(Convert.FromHexString("01000000C313A800")))));

        private sealed class ExpectedPrefix
        {
            public ExpectedPrefix(long id, int sourceInstructionCount, EMEVD.Event.RestBehaviorType restBehavior,
                EMEVD.Instruction[] prefix)
            {
                Id = id;
                SourceInstructionCount = sourceInstructionCount;
                RestBehavior = restBehavior;
                Prefix = prefix;
            }

            public long Id { get; private set; }
            public int SourceInstructionCount { get; private set; }
            public EMEVD.Event.RestBehaviorType RestBehavior { get; private set; }
            public EMEVD.Instruction[] Prefix { get; private set; }
        }

        private static ExpectedPrefix PrefixExpectation(long id, int sourceInstructionCount,
            EMEVD.Event.RestBehaviorType restBehavior, params EMEVD.Instruction[] prefix) =>
            new ExpectedPrefix(id, sourceInstructionCount, restBehavior, prefix);

        private static EMEVD.Instruction EndInstruction() => new EMEVD.Instruction(1000, 4, new byte[4]);

        private static EMEVD.Instruction WaitForFlag(int flag) => new EMEVD.Instruction(3, 0,
            new object[] { (sbyte)0, (byte)1, (byte)0, flag });

        private static EMEVD.Instruction WaitForArea(int region) => new EMEVD.Instruction(3, 2,
            new object[] { (sbyte)0, (byte)1, 10000, region });

        private static EMEVD.Instruction EndIfFlag(int flag) => new EMEVD.Instruction(1003, 2,
            new object[] { (byte)0, (byte)1, (byte)0, flag });

        private static void AssertPrefixedEvents(Action<EMEVD> reconstruct, params ExpectedPrefix[] specifications)
        {
            AssertPrefixedEvents(reconstruct, Array.Empty<EMEVD.Event>(), specifications);
        }

        private static void AssertPrefixedEvents(Action<EMEVD> reconstruct, EMEVD.Event[] additionalEvents,
            params ExpectedPrefix[] specifications)
        {
            EMEVD.Event[] events = specifications.Select(specification =>
                TerminatedSourceEvent(specification.Id, specification.SourceInstructionCount, specification.RestBehavior)).ToArray();
            EMEVD.Instruction[][] originalBodies = events.Select(evt => evt.Instructions.ToArray()).ToArray();
            EMEVD archive = Evd(events.Concat(additionalEvents).ToArray());
            reconstruct(archive);

            for (int i = 0; i < events.Length; i++)
            {
                CheckPrefix(events[i], specifications[i].Prefix, "event " + events[i].ID);
                Check(events[i].Instructions.Count == originalBodies[i].Length + specifications[i].Prefix.Length
                    && events[i].Instructions.Skip(specifications[i].Prefix.Length).SequenceEqual(originalBodies[i]),
                    "event " + events[i].ID + " body changed beyond its authored prefix");
            }

            reconstruct(archive);
            Check(events.Select(evt => evt.Instructions.Count).SequenceEqual(
                specifications.Select(specification => specification.SourceInstructionCount + specification.Prefix.Length)),
                "event prefix reconstruction is not idempotent");
            for (int i = 0; i < events.Length; i++)
                CheckPrefix(events[i], specifications[i].Prefix, "event " + events[i].ID + " after second application");
        }

        private static void AssertTerminatedEvents(Action<EMEVD> reconstruct,
            params (long Id, int SourceInstructionCount, EMEVD.Event.RestBehaviorType RestBehavior)[] specifications)
        {
            AssertTerminatedEvents(reconstruct, Array.Empty<EMEVD.Event>(), specifications);
        }

        private static void AssertTerminatedEvents(Action<EMEVD> reconstruct, EMEVD.Event[] additionalEvents,
            params (long Id, int SourceInstructionCount, EMEVD.Event.RestBehaviorType RestBehavior)[] specifications)
        {
            AssertPrefixedEvents(reconstruct, additionalEvents, specifications.Select(specification =>
                PrefixExpectation(specification.Id, specification.SourceInstructionCount, specification.RestBehavior,
                    EndInstruction())).ToArray());
        }

        private static void CheckPrefix(EMEVD.Event evt, EMEVD.Instruction[] expected, string description)
        {
            Check(evt.Instructions.Count >= expected.Length, description + " is shorter than its expected prefix");
            for (int i = 0; i < expected.Length; i++)
                Check(EmevdMerge.SameInstruction(evt.Instructions[i], expected[i]),
                    description + " has an unexpected instruction at prefix index " + i);
        }

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
                string commonPath = Path.Combine(game, "event", "common.emevd.dcx");
                EMEVD common = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(commonPath)));
                EMEVD.Event event5700 = common.Events.SingleOrDefault(evt => evt.ID == 5700);
                Check(event5700 != null && EmevdMerge.SameEvent(Event5700Builder.Build(), event5700), "FogHost output did not contain the project-authored Event 5700 body");
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

        private static void Event5700ProfileMatrix()
        {
            var profiles = new (string Name, bool Traversable, bool Boss, bool Warps, bool MajorPvp, bool MinorPvp, bool Lordvessel, bool Pacifist)[]
            {
                ("all-off", false, false, false, false, false, false, false),
                ("all-pacifist", true, true, true, true, true, true, true),
                ("all-randomized", true, true, true, true, true, true, false),
                ("boss-only", false, true, false, false, false, false, false),
                ("lordvessel-only", false, false, false, false, false, true, false),
                ("pvp-only", false, false, false, true, true, false, false),
                ("warps-only", false, false, true, false, false, false, false),
                ("world-only", true, false, false, false, false, false, false),
            };
            var observedModes = new HashSet<byte>();

            foreach (var profile in profiles)
            {
                string firstTree = null;
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    string dir = MakeStaging($"event5700-{profile.Name}-{repeat}");
                    JsonObject request = Request(dir, dataRoot, "write", seed: 246813579);
                    JsonObject options = request["options"]!.AsObject();
                    options["traversableFogs"] = profile.Traversable;
                    options["bossFogs"] = profile.Boss;
                    options["areaWarps"] = profile.Warps;
                    options["majorPvpFogs"] = profile.MajorPvp;
                    options["minorPvpFogs"] = profile.MinorPvp;
                    options["lordvesselGates"] = profile.Lordvessel;
                    options["pacifistMode"] = profile.Pacifist;

                    HostRun run = Run(dir, request);
                    ExpectOk(run);
                    string game = Path.Combine(dir, "game");
                    string commonPath = Path.Combine(game, "event", "common.emevd.dcx");
                    EMEVD common = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(commonPath)));
                    EMEVD.Event event5700 = common.Events.SingleOrDefault(evt => evt.ID == 5700);
                    Check(event5700 != null && EmevdMerge.SameEvent(Event5700Builder.Build(), event5700), $"{profile.Name}: generated body differs");
                    foreach (long commonEventId in new long[] { 5690, 8900, 8901, 8950 })
                    {
                        EMEVD.Event generated = common.Events.SingleOrDefault(evt => evt.ID == commonEventId);
                        EMEVD.Event expected = commonEventId switch
                        {
                            5690 => FogCommonEventBuilder.BuildEvent5690(),
                            8900 => FogCommonEventBuilder.BuildEvent8900(),
                            8901 => FogCommonEventBuilder.BuildEvent8901(),
                            _ => FogCommonEventBuilder.BuildEvent8950(),
                        };
                        Check(generated != null && EmevdMerge.SameEvent(expected, generated), $"{profile.Name}: common event {commonEventId} differs from its authored builder");
                    }

                    EMEVD.Event eventZero = common.Events.Single(evt => evt.ID == 0);
                    int callCount = 0;
                    foreach (EMEVD.Instruction instruction in eventZero.Instructions)
                    {
                        byte[] args = instruction.ArgData;
                        if (instruction.Bank != 2000 || instruction.ID != 0 || args.Length < 9 || BitConverter.ToUInt32(args, 4) != 5700) continue;
                        byte mode = args[8];
                        Check(mode <= 3, $"{profile.Name}: invalid initializer mode {mode}");
                        observedModes.Add(mode);
                        callCount++;
                    }
                    Check(callCount > 0, $"{profile.Name}: event 0 has no Event 5700 calls");

                    string tree = TreeHash(game);
                    if (repeat == 0) firstTree = tree;
                    else Check(tree == firstTree, $"{profile.Name}: repeated generation changed the staging tree");
                }
            }

            Check(observedModes.SetEquals(new byte[] { 0, 1, 2, 3 }), "profile matrix did not cover all Event 5700 modes");
        }

        private static void CommonEventsMatchReferenceSemantics()
        {
            string path = Path.Combine(dataRoot, "dist", "DS1R", "event", "common.emevd.dcx");
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(path)));
            EMEVD.Event[] authored =
            {
                FogCommonEventBuilder.BuildEvent5690(), FogCommonEventBuilder.BuildEvent8900(),
                FogCommonEventBuilder.BuildEvent8901(), FogCommonEventBuilder.BuildEvent8950(),
            };
            foreach (EMEVD.Event candidate in authored)
            {
                EMEVD.Event observed = reference.Events.Single(evt => evt.ID == candidate.ID);
                Check(SameEventIgnoringParameterOrder(candidate, observed), $"event {candidate.ID} differs from its observed operation/parameter contract");
            }
        }

        private static bool SameEventIgnoringParameterOrder(EMEVD.Event expected, EMEVD.Event actual)
        {
            if (expected.ID != actual.ID || expected.RestBehavior != actual.RestBehavior
                || expected.Instructions.Count != actual.Instructions.Count
                || expected.Parameters.Count != actual.Parameters.Count)
                return false;

            for (int i = 0; i < expected.Instructions.Count; i++)
                if (!EmevdMerge.SameInstruction(expected.Instructions[i], actual.Instructions[i]))
                    return false;

            var expectedParameters = expected.Parameters
                .Select(parameter => (parameter.InstructionIndex, parameter.TargetStartByte, parameter.SourceStartByte, parameter.ByteCount, parameter.UnkID))
                .OrderBy(parameter => parameter.InstructionIndex)
                .ThenBy(parameter => parameter.TargetStartByte)
                .ThenBy(parameter => parameter.SourceStartByte)
                .ThenBy(parameter => parameter.ByteCount)
                .ThenBy(parameter => parameter.UnkID)
                .ToArray();
            var actualParameters = actual.Parameters
                .Select(parameter => (parameter.InstructionIndex, parameter.TargetStartByte, parameter.SourceStartByte, parameter.ByteCount, parameter.UnkID))
                .OrderBy(parameter => parameter.InstructionIndex)
                .ThenBy(parameter => parameter.TargetStartByte)
                .ThenBy(parameter => parameter.SourceStartByte)
                .ThenBy(parameter => parameter.ByteCount)
                .ThenBy(parameter => parameter.UnkID)
                .ToArray();
            return expectedParameters.SequenceEqual(actualParameters);
        }

        private static void AddedEventsMatchReferenceSemantics()
        {
            string referenceRoot = Path.Combine(dataRoot, "dist", "DS1R", "event");
            CheckAddedEvent(referenceRoot, "common.emevd.dcx", FogAddedEventBuilder.BuildEvent8960());
            CheckAddedEvent(referenceRoot, "m10_02_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11020303());
            CheckAddedEvent(referenceRoot, "m12_01_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11210403());
            CheckAddedEvent(referenceRoot, "m12_01_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11210410());
            CheckAddedEvent(referenceRoot, "m12_01_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11210708());
            CheckAddedEvent(referenceRoot, "m12_01_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11210710());
            CheckAddedEvent(referenceRoot, "m12_01_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11210730());
            CheckAddedEvent(referenceRoot, "m12_01_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11210760());
            CheckAddedEvent(referenceRoot, "m12_01_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11210780());
            CheckAddedEvent(referenceRoot, "m12_01_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11210880());
            CheckAddedEvent(referenceRoot, "m15_01_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11510231());
            CheckAddedEvent(referenceRoot, "m17_00_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11700005());
            CheckAddedEvent(referenceRoot, "m17_00_00_00.emevd.dcx", FogAddedEventBuilder.BuildEvent11708500());
        }

        private static void CheckAddedEvent(string referenceRoot, string archiveName, EMEVD.Event candidate)
        {
            string path = Path.Combine(referenceRoot, archiveName);
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(path)));
            EMEVD.Event observed = reference.Events.Single(evt => evt.ID == candidate.ID);
            Check(SameEventIgnoringParameterOrder(candidate, observed), $"event {candidate.ID} differs from its authored operation contract");
        }

        private static void BedOfChaosHelpersIntegrated()
        {
            string dir = MakeStaging("bed-of-chaos-helpers");
            JsonObject request = Request(dir, dataRoot, "write", seed: 246813579);
            request["options"]!["noFallBedOfChaos"] = true;
            ExpectOk(Run(dir, request));

            EMEVD map = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(Path.Combine(dir, "game", "event", "m14_01_00_00.emevd.dcx"))));
            EMEVD.Event floor = map.Events.Single(e => e.ID == 11410220);
            EMEVD.Event fall = map.Events.Single(e => e.ID == 11410221);
            string referencePath = Path.Combine(dataRoot, "dist", "DS1R", "event", "m14_01_00_00.emevd.dcx");
            EMEVD reference = EMEVD.Read(DCX.Decompress(File.ReadAllBytes(referencePath)));
            Check(floor.Instructions.Count == 10 && floor.Instructions.Take(5).All(i => i.Bank == 2005 && i.ID == 2)
                && floor.Instructions.Skip(5).All(i => i.Bank == 2005 && i.ID == 13), "floor helper was not rebuilt from the five object setup pairs");
            Check(fall.Instructions.Count == 2 && fall.Instructions[0].Bank == 2005 && fall.Instructions[0].ID == 2
                && fall.Instructions[1].Bank == 2005 && fall.Instructions[1].ID == 13, "fall helper was not rebuilt from its object setup pair");
            Check(EmevdMerge.SameEvent(floor, reference.Events.Single(evt => evt.ID == 11410220))
                && EmevdMerge.SameEvent(fall, reference.Events.Single(evt => evt.ID == 11410221)),
                "Bed of Chaos helpers differ from the observed operation and parameter semantics");
            var initializations = map.Events.Single(e => e.ID == 0).Instructions
                .Where(i => i.Bank == 2000 && i.ID == 0 && i.ArgData.Length >= 8)
                .Select(i => BitConverter.ToInt32(i.ArgData, 4)).ToHashSet();
            Check(initializations.Contains(11410220) && initializations.Contains(11410221)
                && !initializations.Contains(11410200) && !initializations.Contains(11410201), "event 0 was not redirected to the rebuilt helper events");
        }

        private static void BedOfChaosHelpersUsePristineGame(string pristineGameRoot)
        {
            string pristineEvent = Path.Combine(pristineGameRoot, "event", "m14_01_00_00.emevd.dcx");
            Check(File.Exists(pristineEvent), "the configured clean game copy has no Bed of Chaos EMEVD archive");

            string dir = MakeStaging("bed-of-chaos-pristine-source");
            string gameEvent = Path.Combine(dir, "game", "event", "m14_01_00_00.emevd.dcx");
            string baseEvent = Path.Combine(dir, "base", "event", "m14_01_00_00.emevd.dcx");
            Directory.CreateDirectory(Path.GetDirectoryName(baseEvent));
            File.Copy(pristineEvent, gameEvent, overwrite: true);
            File.Copy(pristineEvent, baseEvent, overwrite: true);

            JsonObject request = Request(dir, dataRoot, "write", seed: 246813579);
            request["options"]!["noFallBedOfChaos"] = true;
            request["compatibility"]!["combinedFiles"] = new JsonArray(new JsonObject
            {
                ["path"] = "event/m14_01_00_00.emevd.dcx",
                ["baseSha256"] = Sha(baseEvent),
                ["combinedSha256"] = Sha(gameEvent),
            });
            ExpectOk(Run(dir, request));

            EMEVD written = EMEVD.Read(gameEvent);
            EMEVD pristine = EMEVD.Read(pristineEvent);
            EMEVD expected = Evd();
            FogVanillaEventBuilder.ReplaceBedOfChaosHelpers(pristine, expected);
            Check(EmevdMerge.SameEvent(written.Events.Single(evt => evt.ID == 11410220), expected.Events.Single(evt => evt.ID == 11410220)),
                "generated floor helper differs from the semantic operations selected from the clean game archive");
            Check(EmevdMerge.SameEvent(written.Events.Single(evt => evt.ID == 11410221), expected.Events.Single(evt => evt.ID == 11410221)),
                "generated fall helper differs from the semantic operations selected from the clean game archive");
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
