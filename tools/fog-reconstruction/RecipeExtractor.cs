using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SoulsFormats;
using SoulsIds;

namespace FogReconstruction
{
    /// <summary>
    /// Development-only semantic diff. Its report may contain game-derived instruction and evaluator data;
    /// write it outside the repository and never use it as a production patch or fixture.
    /// </summary>
    internal static class RecipeExtractor
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        public static void VerifyInputs(string pristineRoot, string referenceRoot)
        {
            RequireDirectory(pristineRoot, "pristine game root");
            RequireDirectory(referenceRoot, "verified Fog reference root");
            VerifyPristineBaseFiles(pristineRoot);
            FogHost.DataRoot.Verify(referenceRoot);
            string eventRoot = Path.Combine(referenceRoot, "dist", "DS1R", "event");
            string talkRoot = Path.Combine(referenceRoot, "dist", "DS1R", "script", "talk");
            int events = Directory.Exists(eventRoot) ? Directory.GetFiles(eventRoot, "*.emevd.dcx").Length : 0;
            int talks = Directory.Exists(talkRoot) ? Directory.GetFiles(talkRoot, "*.talkesdbnd.dcx").Length : 0;
            int sourceEvents = Directory.Exists(Path.Combine(pristineRoot, "event")) ? Directory.GetFiles(Path.Combine(pristineRoot, "event"), "*.emevd.dcx").Length : 0;
            int sourceTalks = Directory.Exists(Path.Combine(pristineRoot, "script", "talk")) ? Directory.GetFiles(Path.Combine(pristineRoot, "script", "talk"), "*.talkesdbnd.dcx").Length : 0;
            string paramdef = Path.Combine(pristineRoot, "paramdef", "paramdef.paramdefbnd.dcx");

            if (events != 18 || talks != 17 || sourceEvents < events || sourceTalks < talks || !File.Exists(paramdef))
                throw new InvalidOperationException($"Input inventory is incomplete: pristine EMEVD={sourceEvents}, pristine Talk={sourceTalks}, ParamDef={File.Exists(paramdef)}; reference EMEVD={events}, Talk={talks}. Expected at least 18, 17, ParamDef and exactly 18, 17.");

            Console.WriteLine($"Verified inputs: 19 pristine Fog base files match the pinned SHA-256 inventory; ParamDef is present; reference Fog root passes all {FogHost.DataRoot.Manifest.Count} pinned hashes and inventories; pristine EMEVD={sourceEvents}, Talk={sourceTalks}; reference EMEVD={events}, Talk={talks}.");
        }

        private static void VerifyPristineBaseFiles(string pristineRoot)
        {
            const string prefix = "dist/DS1R/";
            int verified = 0;
            foreach (FogHost.DataRoot.Entry entry in FogHost.DataRoot.Manifest.Where(e =>
                         e.Path.StartsWith(prefix + "map/MapStudio/", StringComparison.Ordinal)
                         || e.Path.StartsWith(prefix + "param/GameParam/", StringComparison.Ordinal)))
            {
                string gameRelative = entry.Path.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
                string file = Path.Combine(pristineRoot, gameRelative);
                if (!File.Exists(file)) throw new FileNotFoundException("Pristine game snapshot is missing a pinned Fog input.", file);
                string actual = FogHost.DataRoot.Hash(file, entry.Text);
                if (!string.Equals(actual, entry.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException($"Pristine game snapshot does not match pinned Fog input {gameRelative}: expected {entry.Sha256}, got {actual}.");
                verified++;
            }
            if (verified != 19) throw new InvalidOperationException($"Expected to verify 19 pristine Fog base files, verified {verified}.");
            string paramdef = Path.Combine(pristineRoot, "paramdef", "paramdef.paramdefbnd.dcx");
            if (!File.Exists(paramdef)) throw new FileNotFoundException("Pristine game snapshot is missing the DSR ParamDef binder.", paramdef);
        }

        public static void Extract(string pristineRoot, string referenceRoot, string outputPath, string emedfPath)
        {
            VerifyInputs(pristineRoot, referenceRoot);
            EMEDF emedf = emedfPath == null ? null : EMEDF.ReadFile(Path.GetFullPath(emedfPath));
            var report = new ReconstructionDiffReport
            {
                Source = "verified pristine DSR snapshot",
                Reference = "FogMod cache, pinned commit 6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f",
                TypedArgumentSchema = emedf == null ? "not supplied; raw arguments retained" : Path.GetFileName(emedfPath),
            };
            List<SourceEventCandidate> sourceEventCorpus = ReadSourceEventCorpus(pristineRoot);

            string referenceEvents = Path.Combine(referenceRoot, "dist", "DS1R", "event");
            foreach (string targetPath in Directory.GetFiles(referenceEvents, "*.emevd.dcx").OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(targetPath);
                string sourcePath = Path.Combine(pristineRoot, "event", name);
                if (!File.Exists(sourcePath)) throw new FileNotFoundException("Pristine EMEVD archive is missing.", sourcePath);
                report.EventArchives.Add(DiffEmevd(name, EMEVD.Read(sourcePath), EMEVD.Read(targetPath), emedf, sourceEventCorpus));
            }

            string referenceTalk = Path.Combine(referenceRoot, "dist", "DS1R", "script", "talk");
            foreach (string targetPath in Directory.GetFiles(referenceTalk, "*.talkesdbnd.dcx").OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(targetPath);
                string sourcePath = Path.Combine(pristineRoot, "script", "talk", name);
                if (!File.Exists(sourcePath)) throw new FileNotFoundException("Pristine Talk archive is missing.", sourcePath);
                report.TalkArchives.Add(DiffTalk(name, File.ReadAllBytes(sourcePath), File.ReadAllBytes(targetPath)));
            }

            report.Summary = Summarize(report);
            string fullOutput = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput));
            File.WriteAllText(fullOutput, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
            Console.WriteLine($"Semantic diff written to {fullOutput}");
            Console.WriteLine(JsonSerializer.Serialize(report.Summary, JsonOptions));
        }

        private static EmevdArchiveDiff DiffEmevd(string name, EMEVD source, EMEVD target, EMEDF emedf,
            List<SourceEventCandidate> sourceEventCorpus)
        {
            var diff = new EmevdArchiveDiff { File = name };
            EmevdFileModel sourceModel = CanonicalSemantics.Emevd(source, emedf);
            EmevdFileModel targetModel = CanonicalSemantics.Emevd(target, emedf);
            diff.SourceFingerprint = CanonicalSemantics.Fingerprint(sourceModel);
            diff.TargetFingerprint = CanonicalSemantics.Fingerprint(targetModel);

            var from = source.Events.ToDictionary(e => e.ID);
            var to = target.Events.ToDictionary(e => e.ID);
            foreach (long id in from.Keys.Except(to.Keys).OrderBy(v => v)) diff.RemovedEvents.Add(id);
            foreach (long id in to.Keys.Except(from.Keys).OrderBy(v => v))
            {
                EMEVD.Event evt = to[id];
                diff.AddedEvents.Add(new AddedEventRecipe
                {
                    EventId = id,
                    RestBehavior = evt.RestBehavior.ToString(),
                    BodyFingerprint = EventBodyFingerprint(evt, emedf),
                    Provenance = AddedEventProvenance(evt, emedf, sourceEventCorpus),
                    Instructions = evt.Instructions.Select(i => CanonicalSemantics.Instruction(i, emedf)).ToList(),
                    SourceCandidates = FindSourceCandidates(evt, emedf, sourceEventCorpus),
                    Parameters = evt.Parameters.Select(p => new EmevdParameterModel
                    {
                        InstructionIndex = p.InstructionIndex,
                        TargetStartByte = p.TargetStartByte,
                        SourceStartByte = p.SourceStartByte,
                        ByteCount = p.ByteCount,
                        UnkId = p.UnkID,
                    }).ToList(),
                });
            }

            foreach (long id in from.Keys.Intersect(to.Keys).OrderBy(v => v))
            {
                EMEVD.Event a = from[id];
                EMEVD.Event b = to[id];
                if (CanonicalSemantics.Fingerprint(sourceModel.Events.Single(e => e.Id == id))
                    == CanonicalSemantics.Fingerprint(targetModel.Events.Single(e => e.Id == id))) continue;

                diff.ChangedEvents.Add(DiffEvent(a, b, emedf));
            }
            return diff;
        }

        private static ChangedEventRecipe DiffEvent(EMEVD.Event source, EMEVD.Event target, EMEDF emedf)
        {
            var recipe = new ChangedEventRecipe
            {
                EventId = source.ID,
                SourceRestBehavior = source.RestBehavior.ToString(),
                TargetRestBehavior = target.RestBehavior.ToString(),
                SourceParameters = Parameters(source.Parameters),
                TargetParameters = Parameters(target.Parameters),
            };
            List<InstructionHunk> hunks = DiffSequence(source.Instructions, target.Instructions, CanonicalSemantics.SameInstruction);
            int[] anchorCounts = source.Instructions.GroupBy(CanonicalSemantics.InstructionAnchor).ToDictionary(g => g.Key, g => g.Count())
                .Values.ToArray();
            var sourceAnchors = source.Instructions.Select(CanonicalSemantics.InstructionAnchor).ToList();
            var targetAnchors = target.Instructions.Select(CanonicalSemantics.InstructionAnchor).ToList();
            foreach (InstructionHunk hunk in hunks)
            {
                int beforeAt = hunk.SourceStart - 1;
                int afterAt = hunk.SourceEnd;
                string before = beforeAt >= 0 ? sourceAnchors[beforeAt] : null;
                string after = afterAt < sourceAnchors.Count ? sourceAnchors[afterAt] : null;
                recipe.Operations.Add(new InstructionEditRecipe
                {
                    SourceRange = new[] { hunk.SourceStart, hunk.SourceEnd },
                    TargetInsertAt = hunk.TargetStart,
                    BeforeAnchor = before,
                    BeforeAnchorUnique = before == null || sourceAnchors.Count(a => a == before) == 1,
                    AfterAnchor = after,
                    AfterAnchorUnique = after == null || sourceAnchors.Count(a => a == after) == 1,
                    Remove = source.Instructions.Skip(hunk.SourceStart).Take(hunk.SourceEnd - hunk.SourceStart)
                        .Select(i => CanonicalSemantics.Instruction(i, emedf)).ToList(),
                    Insert = target.Instructions.Skip(hunk.TargetStart).Take(hunk.TargetEnd - hunk.TargetStart)
                        .Select(i => CanonicalSemantics.Instruction(i, emedf)).ToList(),
                });
            }
            recipe.ParameterMappingChanged = CanonicalSemantics.Fingerprint(recipe.SourceParameters)
                != CanonicalSemantics.Fingerprint(recipe.TargetParameters);
            recipe.SourceInstructions = source.Instructions.Count;
            recipe.TargetInstructions = target.Instructions.Count;
            recipe.UniqueInstructionAnchorsInSource = anchorCounts.Length;
            return recipe;
        }

        private static List<EmevdParameterModel> Parameters(List<EMEVD.Parameter> values) => values.Select(p => new EmevdParameterModel
        {
            InstructionIndex = p.InstructionIndex,
            TargetStartByte = p.TargetStartByte,
            SourceStartByte = p.SourceStartByte,
            ByteCount = p.ByteCount,
            UnkId = p.UnkID,
        }).OrderBy(p => p.InstructionIndex).ThenBy(p => p.TargetStartByte).ThenBy(p => p.SourceStartByte).ThenBy(p => p.ByteCount).ThenBy(p => p.UnkId).ToList();

        private static List<SourceEventCandidate> ReadSourceEventCorpus(string pristineRoot)
        {
            var candidates = new List<SourceEventCandidate>();
            foreach (string path in Directory.GetFiles(Path.Combine(pristineRoot, "event"), "*.emevd.dcx").OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                EMEVD file = EMEVD.Read(path);
                foreach (EMEVD.Event evt in file.Events)
                    candidates.Add(new SourceEventCandidate { File = Path.GetFileName(path), Event = evt });
            }
            return candidates;
        }

        private static string EventBodyFingerprint(EMEVD.Event evt, EMEDF emedf) => CanonicalSemantics.Fingerprint(new
        {
            RestBehavior = evt.RestBehavior.ToString(),
            Instructions = evt.Instructions.Select(i => CanonicalSemantics.Instruction(i, emedf)).ToList(),
            Parameters = Parameters(evt.Parameters),
        });

        private static List<SourceEventCandidateModel> FindSourceCandidates(EMEVD.Event target, EMEDF emedf,
            List<SourceEventCandidate> corpus)
        {
            string fingerprint = EventBodyFingerprint(target, emedf);
            return corpus.Select(candidate =>
            {
                int common = LcsLength(candidate.Event.Instructions, target.Instructions);
                bool exact = fingerprint == EventBodyFingerprint(candidate.Event, emedf);
                return new SourceEventCandidateModel
                {
                    File = candidate.File,
                    EventId = candidate.Event.ID,
                    ExactBody = exact,
                    SameRestBehavior = candidate.Event.RestBehavior == target.RestBehavior,
                    SameParameters = CanonicalSemantics.Fingerprint(Parameters(candidate.Event.Parameters))
                        == CanonicalSemantics.Fingerprint(Parameters(target.Parameters)),
                    CommonInstructions = common,
                    SourceInstructions = candidate.Event.Instructions.Count,
                    TargetInstructions = target.Instructions.Count,
                };
            }).OrderByDescending(c => c.ExactBody).ThenByDescending(c => c.CommonInstructions)
              .ThenBy(c => c.File, StringComparer.Ordinal).ThenBy(c => c.EventId)
              .Take(5).ToList();
        }

        private static string AddedEventProvenance(EMEVD.Event target, EMEDF emedf, List<SourceEventCandidate> corpus)
        {
            string body = EventBodyFingerprint(target, emedf);
            if (corpus.Any(candidate => body == EventBodyFingerprint(candidate.Event, emedf)))
                return "exact-vanilla-event-body";
            if (corpus.Any(candidate => candidate.Event.Instructions.Count == target.Instructions.Count
                    && candidate.Event.Instructions.Zip(target.Instructions).All(pair => CanonicalSemantics.SameInstruction(pair.First, pair.Second))))
                return "vanilla-instruction-stream-with-metadata-or-parameter-change";
            return "no-vanilla-event-body-match; pinned source-code provenance required";
        }

        private static int LcsLength(IReadOnlyList<EMEVD.Instruction> a, IReadOnlyList<EMEVD.Instruction> b)
        {
            int[,] lcs = new int[a.Count + 1, b.Count + 1];
            for (int i = a.Count - 1; i >= 0; i--)
                for (int j = b.Count - 1; j >= 0; j--)
                    lcs[i, j] = CanonicalSemantics.SameInstruction(a[i], b[j]) ? 1 + lcs[i + 1, j + 1]
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            return lcs[0, 0];
        }

        private static List<InstructionHunk> DiffSequence(IReadOnlyList<EMEVD.Instruction> source, IReadOnlyList<EMEVD.Instruction> target,
            Func<EMEVD.Instruction, EMEVD.Instruction, bool> same)
        {
            int[,] lcs = new int[source.Count + 1, target.Count + 1];
            for (int i = source.Count - 1; i >= 0; i--)
                for (int j = target.Count - 1; j >= 0; j--)
                    lcs[i, j] = same(source[i], target[j]) ? 1 + lcs[i + 1, j + 1] : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            var result = new List<InstructionHunk>();
            int sourceAt = 0, targetAt = 0;
            while (sourceAt < source.Count || targetAt < target.Count)
            {
                if (sourceAt < source.Count && targetAt < target.Count && same(source[sourceAt], target[targetAt]))
                {
                    sourceAt++; targetAt++; continue;
                }
                int sourceStart = sourceAt, targetStart = targetAt;
                while (sourceAt < source.Count || targetAt < target.Count)
                {
                    if (sourceAt < source.Count && targetAt < target.Count && same(source[sourceAt], target[targetAt])) break;
                    if (targetAt >= target.Count || (sourceAt < source.Count && lcs[sourceAt + 1, targetAt] >= lcs[sourceAt, targetAt + 1])) sourceAt++;
                    else targetAt++;
                }
                result.Add(new InstructionHunk
                {
                    SourceStart = sourceStart,
                    SourceEnd = sourceAt,
                    TargetStart = targetStart,
                    TargetEnd = targetAt,
                });
            }
            return result;
        }

        private static TalkArchiveDiff DiffTalk(string name, byte[] sourceBytes, byte[] targetBytes)
        {
            IBinder sourceBinder = ReadBinder(sourceBytes);
            IBinder targetBinder = ReadBinder(targetBytes);
            var result = new TalkArchiveDiff
            {
                File = name,
                SourceContainer = Container(sourceBinder),
                TargetContainer = Container(targetBinder),
            };
            Dictionary<string, EsdFileModel> sourceEsd = ReadEsdFiles(sourceBinder);
            Dictionary<string, EsdFileModel> targetEsd = ReadEsdFiles(targetBinder);
            foreach (string entry in sourceEsd.Keys.Union(targetEsd.Keys).OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
            {
                sourceEsd.TryGetValue(entry, out EsdFileModel a);
                targetEsd.TryGetValue(entry, out EsdFileModel b);
                if (a == null || b == null)
                {
                    result.EntryMembershipChanges.Add(new EntryMembershipChange { Name = entry, SourcePresent = a != null, TargetPresent = b != null });
                    continue;
                }
                string sourceFingerprint = CanonicalSemantics.Fingerprint(a);
                string targetFingerprint = CanonicalSemantics.Fingerprint(b);
                result.Scripts.Add(new TalkScriptDiff
                {
                    Entry = entry,
                    SourceFingerprint = sourceFingerprint,
                    TargetFingerprint = targetFingerprint,
                    Changed = sourceFingerprint != targetFingerprint,
                    SourceStateCount = a.StateGroups.Sum(g => g.States.Count),
                    TargetStateCount = b.StateGroups.Sum(g => g.States.Count),
                    ChangedStates = DiffStates(a, b),
                });
            }
            return result;
        }

        private static List<EsdStateChange> DiffStates(EsdFileModel source, EsdFileModel target)
        {
            var before = source.StateGroups.SelectMany(g => g.States.Select(s => new { Group = g.Id, State = s })).ToDictionary(x => (x.Group, x.State.Id), x => x.State);
            var after = target.StateGroups.SelectMany(g => g.States.Select(s => new { Group = g.Id, State = s })).ToDictionary(x => (x.Group, x.State.Id), x => x.State);
            var changes = new List<EsdStateChange>();
            foreach (var key in before.Keys.Union(after.Keys).OrderBy(k => k.Group).ThenBy(k => k.Id))
            {
                before.TryGetValue(key, out EsdStateModel a);
                after.TryGetValue(key, out EsdStateModel b);
                if (a == null || b == null)
                {
                    changes.Add(new EsdStateChange { GroupId = key.Group, StateId = key.Id, MembershipChanged = true, Source = a, Target = b });
                    continue;
                }
                if (CanonicalSemantics.Fingerprint(a) == CanonicalSemantics.Fingerprint(b)) continue;
                changes.Add(StateChange(key.Group, key.Id, a, b));
            }
            return changes;
        }

        private static EsdStateChange StateChange(long groupId, long stateId, EsdStateModel a, EsdStateModel b)
        {
            var change = new EsdStateChange { GroupId = groupId, StateId = stateId, PatternFingerprint = StatePattern(a, b), Source = a, Target = b };
            change.EntryCommandsChanged = CanonicalSemantics.Fingerprint(a.EntryCommands) != CanonicalSemantics.Fingerprint(b.EntryCommands);
            change.ExitCommandsChanged = CanonicalSemantics.Fingerprint(a.ExitCommands) != CanonicalSemantics.Fingerprint(b.ExitCommands);
            change.WhileCommandsChanged = CanonicalSemantics.Fingerprint(a.WhileCommands) != CanonicalSemantics.Fingerprint(b.WhileCommands);
            change.ConditionCountChanged = a.Conditions.Count != b.Conditions.Count;
            int common = Math.Min(a.Conditions.Count, b.Conditions.Count);
            for (int i = 0; i < common; i++) CountConditionChanges(a.Conditions[i], b.Conditions[i], change);
            change.AddedConditions = Math.Max(0, b.Conditions.Count - common);
            change.RemovedConditions = Math.Max(0, a.Conditions.Count - common);
            return change;
        }

        private static void CountConditionChanges(EsdConditionModel a, EsdConditionModel b, EsdStateChange change)
        {
            if (a.TargetState != b.TargetState) change.TargetStateChanges++;
            if (a.EvaluatorHex != b.EvaluatorHex) change.EvaluatorChanges++;
            if (CanonicalSemantics.Fingerprint(a.PassCommands) != CanonicalSemantics.Fingerprint(b.PassCommands)) change.PassCommandChanges++;
            int common = Math.Min(a.Subconditions.Count, b.Subconditions.Count);
            change.AddedConditions += Math.Max(0, b.Subconditions.Count - common);
            change.RemovedConditions += Math.Max(0, a.Subconditions.Count - common);
            for (int i = 0; i < common; i++) CountConditionChanges(a.Subconditions[i], b.Subconditions[i], change);
        }

        private static string StatePattern(EsdStateModel a, EsdStateModel b)
        {
            // Exclude state and script IDs so repeated edit shapes cluster across archives.
            var shape = new
            {
                EntryChanged = CanonicalSemantics.Fingerprint(a.EntryCommands) != CanonicalSemantics.Fingerprint(b.EntryCommands),
                ExitChanged = CanonicalSemantics.Fingerprint(a.ExitCommands) != CanonicalSemantics.Fingerprint(b.ExitCommands),
                WhileChanged = CanonicalSemantics.Fingerprint(a.WhileCommands) != CanonicalSemantics.Fingerprint(b.WhileCommands),
                ConditionsBefore = a.Conditions.Count,
                ConditionsAfter = b.Conditions.Count,
                ConditionShape = ConditionShape(a.Conditions, b.Conditions),
            };
            return CanonicalSemantics.Fingerprint(shape);
        }

        private static List<object> ConditionShape(List<EsdConditionModel> a, List<EsdConditionModel> b)
        {
            var shape = new List<object>();
            int common = Math.Min(a.Count, b.Count);
            for (int i = 0; i < common; i++)
            {
                shape.Add(new
                {
                    Target = a[i].TargetState != b[i].TargetState,
                    Evaluator = a[i].EvaluatorHex != b[i].EvaluatorHex,
                    PassCommands = CanonicalSemantics.Fingerprint(a[i].PassCommands) != CanonicalSemantics.Fingerprint(b[i].PassCommands),
                    BeforeChildren = a[i].Subconditions.Count,
                    AfterChildren = b[i].Subconditions.Count,
                    Children = ConditionShape(a[i].Subconditions, b[i].Subconditions),
                });
            }
            if (b.Count > common) shape.Add(new { AddedConditions = b.Count - common });
            if (a.Count > common) shape.Add(new { RemovedConditions = a.Count - common });
            return shape;
        }

        private static IBinder ReadBinder(byte[] bytes)
        {
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            if (BND4.Is(bytes)) return BND4.Read(bytes);
            if (BND3.Is(bytes)) return BND3.Read(bytes);
            throw new InvalidDataException("Talk archive is neither BND3 nor BND4 after DCX decompression.");
        }

        private static Dictionary<string, EsdFileModel> ReadEsdFiles(IBinder binder)
        {
            var result = new Dictionary<string, EsdFileModel>(StringComparer.OrdinalIgnoreCase);
            foreach (BinderFile entry in binder.Files)
            {
                string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (ext != ".esd" && ext != ".dcx") continue;
                byte[] bytes = entry.Bytes;
                if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
                ESD esd = ESD.Read(bytes);
                result.Add(entry.Name, CanonicalSemantics.Esd(esd, entry.Name));
            }
            if (result.Count == 0) throw new InvalidDataException("Talk archive has no readable ESD entries.");
            return result;
        }

        private static object Container(IBinder binder)
        {
            BND4 bnd4 = binder as BND4;
            if (bnd4 != null) return new { Type = "BND4", bnd4.BigEndian, bnd4.BitBigEndian, bnd4.Unicode, bnd4.Unk04, bnd4.Unk05, bnd4.Extended, bnd4.Version, FileCount = bnd4.Files.Count, Files = BinderFiles(bnd4.Files) };
            BND3 bnd3 = binder as BND3;
            if (bnd3 != null) return new { Type = "BND3", bnd3.BigEndian, bnd3.BitBigEndian, bnd3.Unk18, bnd3.Version, FileCount = bnd3.Files.Count, Files = BinderFiles(bnd3.Files) };
            return new { Type = binder.GetType().FullName, FileCount = binder.Files.Count };
        }

        private static List<object> BinderFiles(List<BinderFile> files) => files.Select((file, index) => (object)new
        {
            Index = index,
            file.Name,
            file.ID,
            Flags = file.Flags.ToString(),
            Compression = file.CompressionType.ToString(),
            Length = file.Bytes.Length,
            Sha256 = CanonicalSemantics.Sha256(file.Bytes),
        }).ToList();

        private static object Summarize(ReconstructionDiffReport report)
        {
            var changedEvents = report.EventArchives.SelectMany(f => f.ChangedEvents).ToList();
            var addedEvents = report.EventArchives.SelectMany(f => f.AddedEvents).ToList();
            var removedEvents = report.EventArchives.SelectMany(f => f.RemovedEvents).ToList();
            var changedScripts = report.TalkArchives.SelectMany(f => f.Scripts).Where(s => s.Changed).ToList();
            var changedStates = changedScripts.SelectMany(s => s.ChangedStates).ToList();
            return new
            {
                EmevdArchives = report.EventArchives.Count,
                ModifiedEvents = changedEvents.Count,
                AddedEvents = addedEvents.Count,
                RemovedEvents = removedEvents.Count,
                SourceInstructionsInChangedEvents = changedEvents.Sum(e => e.SourceInstructions),
                TargetInstructionsInChangedEvents = changedEvents.Sum(e => e.TargetInstructions),
                SourceEventParameterRecords = changedEvents.Sum(e => e.SourceParameters.Count),
                TargetEventParameterRecords = changedEvents.Sum(e => e.TargetParameters.Count),
                EventsWithChangedParameterMapping = changedEvents.Count(e => e.ParameterMappingChanged),
                InstructionEditHunks = changedEvents.Sum(e => e.Operations.Count),
                InstructionEditHunkShapes = changedEvents.SelectMany(e => e.Operations).GroupBy(OperationShape).Count(),
                AddedEventInstructionCount = addedEvents.Sum(e => e.Instructions.Count),
                AddedEventsWithExactVanillaBody = addedEvents.Count(e => e.Provenance == "exact-vanilla-event-body"),
                AddedEventsWithVanillaInstructionStream = addedEvents.Count(e => e.Provenance == "vanilla-instruction-stream-with-metadata-or-parameter-change"),
                AddedEventsWithoutVanillaBodyMatch = addedEvents.Count(e => e.Provenance.StartsWith("no-vanilla", StringComparison.Ordinal)),
                RemovedEventIds = removedEvents.Count,
                TalkArchives = report.TalkArchives.Count,
                TalkEntries = report.TalkArchives.Sum(a => a.Scripts.Count),
                ModifiedTalkScripts = changedScripts.Count,
                ModifiedTalkStates = changedStates.Count,
                TalkStateEditPatterns = changedStates.GroupBy(s => s.PatternFingerprint).Count(),
                EmevdInstructionArgumentsDecoded = changedEvents.SelectMany(e => e.Operations).SelectMany(o => o.Insert).Count(i => i.TypedArgs != null),
                EmevdInstructionArgumentsRawOnly = changedEvents.SelectMany(e => e.Operations).SelectMany(o => o.Insert).Count(i => i.TypedArgs == null),
            };
        }

        private static string OperationShape(InstructionEditRecipe operation)
        {
            string removed = string.Join(",", operation.Remove.Select(i => i.Bank + ":" + i.Id));
            string added = string.Join(",", operation.Insert.Select(i => i.Bank + ":" + i.Id));
            return removed + "=>" + added;
        }

        private static void RequireDirectory(string path, string description)
        {
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"The {description} does not exist: {Path.GetFullPath(path)}");
        }
    }

    internal sealed class ReconstructionDiffReport
    {
        public string Source { get; set; }
        public string Reference { get; set; }
        public string TypedArgumentSchema { get; set; }
        public object Summary { get; set; }
        public List<EmevdArchiveDiff> EventArchives { get; set; } = new List<EmevdArchiveDiff>();
        public List<TalkArchiveDiff> TalkArchives { get; set; } = new List<TalkArchiveDiff>();
    }

    internal sealed class EmevdArchiveDiff
    {
        public string File { get; set; }
        public string SourceFingerprint { get; set; }
        public string TargetFingerprint { get; set; }
        public List<long> RemovedEvents { get; set; } = new List<long>();
        public List<AddedEventRecipe> AddedEvents { get; set; } = new List<AddedEventRecipe>();
        public List<ChangedEventRecipe> ChangedEvents { get; set; } = new List<ChangedEventRecipe>();
    }

    internal sealed class AddedEventRecipe
    {
        public long EventId { get; set; }
        public string RestBehavior { get; set; }
        public string BodyFingerprint { get; set; }
        public string Provenance { get; set; }
        public List<SourceEventCandidateModel> SourceCandidates { get; set; } = new List<SourceEventCandidateModel>();
        public List<EmevdInstructionModel> Instructions { get; set; } = new List<EmevdInstructionModel>();
        public List<EmevdParameterModel> Parameters { get; set; } = new List<EmevdParameterModel>();
    }

    internal sealed class SourceEventCandidate
    {
        public string File { get; set; }
        public EMEVD.Event Event { get; set; }
    }

    internal sealed class SourceEventCandidateModel
    {
        public string File { get; set; }
        public long EventId { get; set; }
        public bool ExactBody { get; set; }
        public bool SameRestBehavior { get; set; }
        public bool SameParameters { get; set; }
        public int CommonInstructions { get; set; }
        public int SourceInstructions { get; set; }
        public int TargetInstructions { get; set; }
    }

    internal sealed class ChangedEventRecipe
    {
        public long EventId { get; set; }
        public string SourceRestBehavior { get; set; }
        public string TargetRestBehavior { get; set; }
        public int SourceInstructions { get; set; }
        public int TargetInstructions { get; set; }
        public int UniqueInstructionAnchorsInSource { get; set; }
        public bool ParameterMappingChanged { get; set; }
        public List<EmevdParameterModel> SourceParameters { get; set; }
        public List<EmevdParameterModel> TargetParameters { get; set; }
        public List<InstructionEditRecipe> Operations { get; set; } = new List<InstructionEditRecipe>();
    }

    internal sealed class InstructionEditRecipe
    {
        public int[] SourceRange { get; set; }
        public int TargetInsertAt { get; set; }
        public string BeforeAnchor { get; set; }
        public bool BeforeAnchorUnique { get; set; }
        public string AfterAnchor { get; set; }
        public bool AfterAnchorUnique { get; set; }
        public List<EmevdInstructionModel> Remove { get; set; }
        public List<EmevdInstructionModel> Insert { get; set; }
    }

    internal sealed class InstructionHunk
    {
        public int SourceStart { get; set; }
        public int SourceEnd { get; set; }
        public int TargetStart { get; set; }
        public int TargetEnd { get; set; }
    }

    internal sealed class TalkArchiveDiff
    {
        public string File { get; set; }
        public object SourceContainer { get; set; }
        public object TargetContainer { get; set; }
        public List<EntryMembershipChange> EntryMembershipChanges { get; set; } = new List<EntryMembershipChange>();
        public List<TalkScriptDiff> Scripts { get; set; } = new List<TalkScriptDiff>();
    }

    internal sealed class EntryMembershipChange
    {
        public string Name { get; set; }
        public bool SourcePresent { get; set; }
        public bool TargetPresent { get; set; }
    }

    internal sealed class TalkScriptDiff
    {
        public string Entry { get; set; }
        public string SourceFingerprint { get; set; }
        public string TargetFingerprint { get; set; }
        public bool Changed { get; set; }
        public int SourceStateCount { get; set; }
        public int TargetStateCount { get; set; }
        public List<EsdStateChange> ChangedStates { get; set; } = new List<EsdStateChange>();
    }

    internal sealed class EsdStateChange
    {
        public long GroupId { get; set; }
        public long StateId { get; set; }
        public bool MembershipChanged { get; set; }
        public EsdStateModel Source { get; set; }
        public EsdStateModel Target { get; set; }
        public string PatternFingerprint { get; set; }
        public bool EntryCommandsChanged { get; set; }
        public bool ExitCommandsChanged { get; set; }
        public bool WhileCommandsChanged { get; set; }
        public bool ConditionCountChanged { get; set; }
        public int TargetStateChanges { get; set; }
        public int EvaluatorChanges { get; set; }
        public int PassCommandChanges { get; set; }
        public int AddedConditions { get; set; }
        public int RemovedConditions { get; set; }
    }
}
