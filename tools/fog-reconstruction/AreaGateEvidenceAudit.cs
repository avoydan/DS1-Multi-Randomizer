using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FogHost;
using SoulsFormats;

namespace FogReconstruction
{
    public sealed record AreaGateItemCheck(int Group, int ItemType, int ItemId, string EnglishGameName, int State);
    public sealed record AreaGateActionTarget(
        string Operation,
        int Group,
        int EntityType,
        int EntityId,
        string AnchorKind,
        string AnchorName,
        string CollisionName,
        FogDsrTransform Transform);
    public sealed record AreaGateWarpTarget(
        string Operation,
        int EntityId,
        string EntityAnchorKind,
        string EntityAnchorName,
        int WarpType,
        int DestinationEntityId,
        string DestinationAnchorKind,
        string DestinationAnchorName,
        int? DestinationArea,
        int? DestinationBlock,
        int? InitialAreaId);
    public sealed record AreaGateObjectActReport(
        string Map,
        string ObjectPartName,
        int ObjectActEntityId,
        int? ObjectEntityId,
        int ObjectActParamId,
        int EventFlagId,
        int State,
        string CollisionName,
        FogDsrTransform Transform);
    public sealed record AreaGateMapFlagWriteReport(string Map, long EventId, int FlagId);
    public sealed record AreaGateEventReport(
        string Map,
        string EventArchive,
        long EventId,
        long? CallerEventId,
        int? CallerInstructionIndex,
        bool ParameterValuesResolved,
        IReadOnlyList<AreaGateItemCheck> ItemChecks,
        IReadOnlyList<AreaGateActionTarget> ActionTargets,
        IReadOnlyList<int> EventFlagConditions,
        IReadOnlyList<int> EventFlagWrites,
        int ShortWarpInstructions,
        int PlayerWarpInstructions,
        IReadOnlyList<AreaGateWarpTarget> WarpTargets);
    public sealed record AreaGateEvidenceAuditReport(
        string Format,
        string Method,
        string Limitation,
        int Maps,
        int EventsWithEvidence,
        IReadOnlyList<AreaGateObjectActReport> ObjectActs,
        IReadOnlyList<AreaGateMapFlagWriteReport> MapEventFlagWrites,
        IReadOnlyList<AreaGateEventReport> Events);

    /// <summary>
    /// Development-only inventory of pristine event item checks and action targets. It resolves
    /// event parameters from InitializeEvent call sites and validates action entity IDs against
    /// the same map's pristine MSB; it does not assign logical Areas or annotation categories.
    /// </summary>
    public static class AreaGateEvidenceAudit
    {
        public const string Format = "ds1tr-fog-area-gate-evidence-4";

        private sealed record EventCallSite(EMEVD.Event Caller, EMEVD.Instruction Instruction);

        public static void Write(string dataRoot, string pristineGameRoot, string outputPath)
        {
            string output = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot != null && IsWithin(repositoryRoot, output))
                throw new InvalidOperationException("Area gate evidence must be written outside the repository.");

            FogDsrFacts facts = FogDsrFacts.Read(dataRoot, pristineGameRoot);
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Area gate evidence requires EMEVD from the byte-verified clean DSR installation.");

            string eventRoot = Path.Combine(Path.GetFullPath(pristineGameRoot), "event");
            var reports = new List<AreaGateEventReport>();
            var objectActs = new List<AreaGateObjectActReport>();
            var mapFlagWrites = new List<AreaGateMapFlagWriteReport>();
            foreach (FogDsrFacts.Map map in facts.Maps.Values.OrderBy(value => value.Id.Value, StringComparer.Ordinal))
            {
                mapFlagWrites.AddRange(map.EventFlagWrites
                    .Select(write => new AreaGateMapFlagWriteReport(map.Id.Value, write.EventId, write.FlagId)));
                foreach (FogDsrObjectActFact act in map.ObjectActDetails)
                {
                    FogDsrObjectFact part = map.ObjectDetails.SingleOrDefault(item =>
                        string.Equals(item.Name, act.PartName, StringComparison.Ordinal));
                    objectActs.Add(new AreaGateObjectActReport(map.Id.Value, act.PartName, act.ObjectActEntityId,
                        act.ObjectEntityId, act.ParamId, act.EventFlagId, act.State, part?.CollisionName, part?.Transform));
                }
                string eventPath = Path.Combine(eventRoot, map.EventArchiveName);
                EMEVD archive = ReadEmevd(eventPath);
                Dictionary<long, List<EventCallSite>> callers = BuildCallSites(archive);
                foreach (EMEVD.Event evt in archive.Events)
                {
                    EventCallSite[] eventCallSites = callers.TryGetValue(evt.ID, out List<EventCallSite> mappedCallSites)
                        ? mappedCallSites.ToArray()
                        : new EventCallSite[] { null };
                    foreach (EventCallSite callSite in eventCallSites)
                    {
                        AreaGateEventReport report = BuildEventReport(facts, map, Path.GetFileName(eventPath), evt, callSite);
                        if (report.ItemChecks.Count > 0 || report.ActionTargets.Count > 0 || report.WarpTargets.Count > 0
                            || report.EventFlagConditions.Count > 0 && report.ShortWarpInstructions > 0
                            || report.EventFlagWrites.Count > 0 && report.ActionTargets.Count > 0)
                            reports.Add(report);
                    }
                }
            }

            var result = new AreaGateEvidenceAuditReport(
                Format,
                "Inventories pristine MSB ObjAct part/entity/parameter/flag/state bindings and all map-local SetEventFlag event/flag pairs; recognizes IfPlayerHasItem (3[4]), IfActionButton (3[5]), IfActionButtonLineSegment (3[18]), IfEventFlag (3[0]), IssueShortWarpRequest (2004[41]), and WarpPlayer (2003[14]). InitializeEvent parameter bindings are resolved from pristine call sites; action IDs are matched by exact MSB entity ID.",
                "These are event/object interaction facts only. Shared condition groups and action targets do not establish logical Area endpoints or whether a transition belongs to Area.To, Entrances, or Warps; no area edge or gate classification is inferred.",
                facts.Maps.Count,
                reports.Count,
                Array.AsReadOnly(objectActs.ToArray()),
                Array.AsReadOnly(mapFlagWrites.ToArray()),
                Array.AsReadOnly(reports.ToArray()));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Pristine area-gate evidence: {facts.Maps.Count} maps, {objectActs.Count} MSB ObjActs, {reports.Count} events with typed item/action/flag/warp evidence. Report: {output}");
        }

        private static AreaGateEventReport BuildEventReport(
            FogDsrFacts facts,
            FogDsrFacts.Map map,
            string eventArchive,
            EMEVD.Event evt,
            EventCallSite callSite)
        {
            var itemChecks = new List<AreaGateItemCheck>();
            var actionTargets = new List<AreaGateActionTarget>();
            var flags = new List<int>();
            var writes = new List<int>();
            var warpTargets = new List<AreaGateWarpTarget>();
            int shortWarps = 0;
            int playerWarps = 0;
            bool parameterValuesResolved = true;
            int Resolve(int instructionIndex, int offset)
            {
                (int Value, bool Resolved) resolved = ResolveInt32(evt, instructionIndex, offset, callSite);
                parameterValuesResolved &= resolved.Resolved;
                return resolved.Value;
            }

            for (int instructionIndex = 0; instructionIndex < evt.Instructions.Count; instructionIndex++)
            {
                EMEVD.Instruction instruction = evt.Instructions[instructionIndex];
                byte[] args = instruction.ArgData;
                if (instruction.Bank == 3 && instruction.ID == 4 && args.Length >= 12)
                {
                    int group = unchecked((sbyte)ResolveByte(evt, instructionIndex, 0, callSite, ref parameterValuesResolved));
                    int itemType = ResolveByte(evt, instructionIndex, 1, callSite, ref parameterValuesResolved);
                    int itemId = Resolve(instructionIndex, 4);
                    int state = ResolveByte(evt, instructionIndex, 8, callSite, ref parameterValuesResolved);
                    facts.GameItemNames.TryGetValue(new FogGameItemId(itemType, itemId), out string englishName);
                    itemChecks.Add(DecodeItemCheck(args, englishName, group, itemType, itemId, state));
                }
                else if (instruction.Bank == 3 && (instruction.ID == 5 || instruction.ID == 18) && args.Length >= 12)
                {
                    int group = unchecked((sbyte)ReadInt32(args, 0));
                    int entityType = Resolve(instructionIndex, 4);
                    int entityId = Resolve(instructionIndex, 8);
                    string kind = null;
                    string name = null;
                    string collisionName = null;
                    FogDsrTransform transform = null;
                    if (map.RegionDetails.SingleOrDefault(region => region.EntityId == entityId) is FogDsrRegionFact region)
                    {
                        kind = "region";
                        name = region.Name;
                        transform = region.Transform;
                    }
                    else if (map.ObjectDetails.SingleOrDefault(obj => obj.EntityId == entityId) is FogDsrObjectFact obj)
                    {
                        kind = "object";
                        name = obj.Name;
                        collisionName = obj.CollisionName;
                        transform = obj.Transform;
                    }
                    string operation = instruction.ID == 5 ? "IfActionButton" : "IfActionButtonLineSegment";
                    actionTargets.Add(new AreaGateActionTarget(operation, group, entityType, entityId, kind, name, collisionName, transform));
                }
                else if (instruction.Bank == 3 && instruction.ID == 0 && args.Length >= 8)
                    flags.Add(Resolve(instructionIndex, 4));
                else if (instruction.Bank == 2003 && instruction.ID == 2 && args.Length >= 4)
                    writes.Add(Resolve(instructionIndex, 0));
                else if (instruction.Bank == 2004 && instruction.ID == 41)
                {
                    shortWarps++;
                    if (args.Length >= 12)
                    {
                        (int EntityId, int WarpType, int DestinationEntityId) decoded = ResolveShortWarpArguments(
                            args, offset => Resolve(instructionIndex, offset));
                        int entityId = decoded.EntityId;
                        int warpType = decoded.WarpType;
                        int destinationEntityId = decoded.DestinationEntityId;
                        (string entityKind, string entityName) = ResolveEntityAnchor(map, entityId);
                        (string destinationKind, string destinationName) = ResolveEntityAnchor(map, destinationEntityId);
                        warpTargets.Add(new AreaGateWarpTarget("IssueShortWarpRequest", entityId, entityKind, entityName,
                            warpType, destinationEntityId, destinationKind, destinationName, null, null, null));
                    }
                }
                else if (instruction.Bank == 2003 && instruction.ID == 14)
                {
                    playerWarps++;
                    if (args.Length >= 6)
                    {
                        int destinationArea = ResolveByte(evt, instructionIndex, 0, callSite, ref parameterValuesResolved);
                        int destinationBlock = ResolveByte(evt, instructionIndex, 1, callSite, ref parameterValuesResolved);
                        int initialAreaId = Resolve(instructionIndex, 2);
                        warpTargets.Add(new AreaGateWarpTarget("WarpPlayer", 0, null, null, 0, 0, null, null,
                            destinationArea, destinationBlock, initialAreaId));
                    }
                }
            }

            return new AreaGateEventReport(
                map.Id.Value,
                eventArchive,
                evt.ID,
                callSite?.Caller.ID,
                callSite == null ? null : Array.IndexOf(callSite.Caller.Instructions.ToArray(), callSite.Instruction),
                parameterValuesResolved,
                Array.AsReadOnly(itemChecks.ToArray()),
                Array.AsReadOnly(actionTargets.ToArray()),
                Array.AsReadOnly(flags.Distinct().OrderBy(value => value).ToArray()),
                Array.AsReadOnly(writes.Distinct().OrderBy(value => value).ToArray()),
                shortWarps,
                playerWarps,
                Array.AsReadOnly(warpTargets.ToArray()));
        }

        private static (string Kind, string Name) ResolveEntityAnchor(FogDsrFacts.Map map, int entityId)
        {
            if (entityId == 0) return (null, null);
            var anchors = map.RegionDetails.Where(region => region.EntityId == entityId)
                .Select(region => (Kind: "region", region.Name))
                .Concat(map.ObjectDetails.Where(obj => obj.EntityId == entityId)
                    .Select(obj => (Kind: "object", obj.Name)))
                .Concat(map.EnemyDetails.Where(enemy => enemy.EntityId == entityId)
                    .Select(enemy => (Kind: "enemy", enemy.Name)))
                .ToArray();
            if (anchors.Length == 0) return (null, null);
            if (anchors.Length == 1) return (anchors[0].Kind, anchors[0].Name);
            return ("ambiguous", string.Join(" | ", anchors.Select(anchor => anchor.Kind + ":" + anchor.Name)));
        }

        private static Dictionary<long, List<EventCallSite>> BuildCallSites(EMEVD archive)
        {
            var callers = new Dictionary<long, List<EventCallSite>>();
            foreach (EMEVD.Event evt in archive.Events)
            {
                foreach (EMEVD.Instruction instruction in evt.Instructions.Where(value => value.Bank == 2000 && value.ID == 0 && value.ArgData.Length >= 8))
                {
                    long targetEventId = (uint)ReadInt32(instruction.ArgData, 4);
                    if (!callers.TryGetValue(targetEventId, out List<EventCallSite> list))
                        callers[targetEventId] = list = new List<EventCallSite>();
                    list.Add(new EventCallSite(evt, instruction));
                }
            }
            return callers;
        }

        private static (int Value, bool Resolved) ResolveInt32(
            EMEVD.Event evt,
            int instructionIndex,
            int targetOffset,
            EventCallSite callSite)
        {
            int literal = ReadInt32(evt.Instructions[instructionIndex].ArgData, targetOffset);
            EMEVD.Parameter[] bindings = evt.Parameters.Where(parameter => parameter.InstructionIndex == instructionIndex
                && parameter.TargetStartByte == targetOffset && parameter.ByteCount == sizeof(int)).ToArray();
            if (bindings.Length == 0) return (literal, true);
            if (bindings.Length != 1 || callSite == null) return (literal, false);

            int sourceOffset = checked(8 + checked((int)bindings[0].SourceStartByte));
            if (sourceOffset < 8 || sourceOffset + sizeof(int) > callSite.Instruction.ArgData.Length)
                throw new InvalidDataException($"InitializeEvent for {evt.ID} has an out-of-range parameter source at {sourceOffset}.");
            return (ReadInt32(callSite.Instruction.ArgData, sourceOffset), true);
        }

        private static byte ResolveByte(
            EMEVD.Event evt,
            int instructionIndex,
            int targetOffset,
            EventCallSite callSite,
            ref bool allResolved)
        {
            byte literal = evt.Instructions[instructionIndex].ArgData[targetOffset];
            EMEVD.Parameter[] bindings = evt.Parameters.Where(parameter => parameter.InstructionIndex == instructionIndex
                && parameter.TargetStartByte == targetOffset && parameter.ByteCount == sizeof(byte)).ToArray();
            if (bindings.Length == 0) return literal;
            if (bindings.Length != 1 || callSite == null)
            {
                allResolved = false;
                return literal;
            }
            int sourceOffset = checked(8 + checked((int)bindings[0].SourceStartByte));
            if (sourceOffset < 8 || sourceOffset >= callSite.Instruction.ArgData.Length)
                throw new InvalidDataException($"InitializeEvent for {evt.ID} has an out-of-range byte parameter source at {sourceOffset}.");
            return callSite.Instruction.ArgData[sourceOffset];
        }

        internal static AreaGateItemCheck DecodeItemCheck(
            byte[] args,
            string englishName,
            int? resolvedGroup = null,
            int? resolvedItemType = null,
            int? resolvedItemId = null,
            int? resolvedState = null)
        {
            if (args == null || args.Length < 12) throw new InvalidDataException("IfPlayerHasItem requires at least 12 argument bytes.");
            return new AreaGateItemCheck(
                resolvedGroup ?? unchecked((sbyte)args[0]),
                resolvedItemType ?? args[1],
                resolvedItemId ?? ReadInt32(args, 4),
                englishName,
                resolvedState ?? args[8]);
        }

        internal static (int EntityId, int WarpType, int DestinationEntityId) ResolveShortWarpArguments(
            byte[] args,
            Func<int, int> resolveInt32)
        {
            if (args == null || args.Length < 12)
                throw new InvalidDataException("IssueShortWarpRequest requires 12 argument bytes for its entity, type, and destination entity IDs.");
            if (resolveInt32 == null) throw new ArgumentNullException(nameof(resolveInt32));
            return (resolveInt32(0), resolveInt32(4), resolveInt32(8));
        }

        private static int ReadInt32(byte[] bytes, int offset)
        {
            if (bytes == null || offset < 0 || offset + sizeof(int) > bytes.Length)
                throw new InvalidDataException($"Expected a 4-byte EMEVD argument at offset {offset}.");
            return BitConverter.ToInt32(bytes, offset);
        }

        public static void SelfTest()
        {
            AreaGateItemCheck crest = DecodeItemCheck(
                Convert.FromHexString("01030000D207000001000000"), englishName: "Crest of Artorias");
            if (crest.Group != 1 || crest.ItemType != 3 || crest.ItemId != 2002
                || crest.EnglishGameName != "Crest of Artorias" || crest.State != 1)
                throw new InvalidOperationException("The pristine IfPlayerHasItem argument layout did not decode the known Crest-of-Artorias fixture.");
            byte[] shortWarpArgs = Convert.FromHexString("102700000000000053E7160078000000");
            (int EntityId, int WarpType, int DestinationEntityId) shortWarp = ResolveShortWarpArguments(
                shortWarpArgs, offset => ReadInt32(shortWarpArgs, offset));
            if (shortWarp.EntityId != 10000 || shortWarp.WarpType != 0 || shortWarp.DestinationEntityId != 1501011)
                throw new InvalidOperationException("The IssueShortWarpRequest destination entity must be decoded from aligned argument offset 8.");
            Console.WriteLine("Area gate evidence self-test passed (Crest item layout and aligned short-warp destination argument).");
        }

        private static EMEVD ReadEmevd(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Pristine map EMEVD archive is missing.", path);
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            return EMEVD.Read(bytes);
        }

        private static string FindRepositoryRoot(string start)
        {
            DirectoryInfo current = new DirectoryInfo(Path.GetFullPath(start));
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
                current = current.Parent;
            }
            return null;
        }

        private static bool IsWithin(string parent, string candidate)
        {
            string relative = Path.GetRelativePath(parent, candidate);
            return relative == "." || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        }
    }
}
