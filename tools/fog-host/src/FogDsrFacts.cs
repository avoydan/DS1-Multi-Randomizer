using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Linq;
using FogMod;
using SoulsFormats;
using SoulsIds;

namespace FogHost
{
    public enum FogGameItemCategory { Weapon = 0, Armor = 1, Accessory = 2, Goods = 3 }
    public enum FogDsrRegionShapeKind { Point, Box, Cylinder, Sphere }
    public enum FogDsrEventFactSource { VerifiedFogReference, PristineGameInstall }

    public sealed record FogDsrTransform(float X, float Y, float Z, float Yaw);
    public sealed record FogDsrRegionShape(FogDsrRegionShapeKind Kind, float? Width, float? Height, float? Depth, float? Radius);
    public sealed record FogDsrObjectFact(string Name, int EntityId, string CollisionName, FogDsrTransform Transform);
    public sealed record FogDsrConnectCollisionFact(string PartName, string CollisionName, IReadOnlyList<byte> TargetMapIdBytes);
    public sealed record FogDsrObjectActFact(string PartName, int ObjectActEntityId, int? ObjectEntityId, int ParamId, int EventFlagId, int State);
    public sealed record FogDsrCollisionFact(string Name, int EntityId, int PlayRegionId, int DisableBonfireEntityId);
    public sealed record FogDsrEnemyFact(string Name, int EntityId, int NpcParamId, int ThinkParamId, string CollisionName, FogDsrTransform Transform);
    public sealed record FogDsrRegionFact(string Name, int EntityId, FogDsrTransform Transform, FogDsrRegionShape Shape);
    public sealed record FogDsrEventFlagWrite(long EventId, int FlagId);
    public sealed record FogDsrEventFact(long EventId);
    public sealed record FogDsrCutsceneWarpFact(
        FogMapId SourceMap,
        long EventId,
        int InstructionIndex,
        int CutsceneId,
        int PlaybackType,
        int DestinationEntityId,
        string DestinationMapFileId,
        FogMapId? DestinationMap,
        bool HasEventParameterBindings);
    public sealed record FogDsrBossDefeatFact(long EventId, int EnemyEntityId);
    public sealed record FogDsrItemPossessionFact(
        FogMapId Map,
        long EventId,
        int InstructionIndex,
        int Group,
        FogGameItemId Item,
        int RequiredState,
        long? CallerEventId,
        int? CallerInstructionIndex);
    public sealed record FogDsrTreasureFact(int EntityId, string PartName, string PartCollisionName, bool StartDisabled, bool InChest, FogDsrTransform? PartTransform, IReadOnlyList<int> ItemLots);
    public sealed record FogDsrTreasureLotFact(FogMapId Map, int TreasureEntityId, string PartName, string PartCollisionName, bool StartDisabled, bool InChest, int Slot, int ItemLotId, FogDsrTransform? PartTransform);
    public sealed record FogDsrLotItemFact(int Slot, FogGameItemId Item);
    public sealed record FogDsrItemLotFact(int Id, IReadOnlyList<FogDsrLotItemFact> Items);
    public sealed record FogDsrNpcDropLotFact(int NpcParamId, int Slot, int ItemLotId);
    public sealed record FogDsrItemLotAwardFact(
        string EventArchiveName,
        long EventId,
        int InstructionIndex,
        int? ItemLotId,
        long? InitializerEventId,
        int? InitializerInstructionIndex,
        int? CharacterEntityId = null,
        int? RequiredEventFlagId = null);

    /// <summary>
    /// Facts read from the verified pristine DSR data root. This is deliberately separate from
    /// project policy: it records stable game identifiers and references that policy may use.
    /// </summary>
    public sealed class FogDsrFacts
    {
        public sealed class Map
        {
            public FogMapId Id { get; init; }
            public string GameMapFileId { get; init; }
            public IReadOnlySet<string> Collisions { get; init; }
            public IReadOnlySet<string> Objects { get; init; }
            public IReadOnlySet<int> ObjectEntityIds { get; init; }
            public IReadOnlySet<int> PlayerEntityIds { get; init; }
            public IReadOnlySet<string> Enemies { get; init; }
            public IReadOnlySet<string> Regions { get; init; }
            public IReadOnlySet<int> RegionEntityIds { get; init; }
            public IReadOnlyDictionary<int, string> SpawnPoints { get; init; }
            public IReadOnlySet<int> ObjectActFlags { get; init; }
            public IReadOnlyList<FogDsrObjectActFact> ObjectActDetails { get; init; }
            public string EventArchiveName { get; init; }
            public IReadOnlySet<long> EventIds { get; init; }
            public IReadOnlyList<FogDsrCutsceneWarpFact> CutsceneWarps { get; init; }
            public IReadOnlyList<FogDsrEventFlagWrite> EventFlagWrites { get; init; }
            public IReadOnlyList<FogDsrObjectFact> ObjectDetails { get; init; }
            public IReadOnlyList<FogDsrConnectCollisionFact> ConnectCollisionDetails { get; init; }
            public IReadOnlyList<FogDsrCollisionFact> CollisionDetails { get; init; }
            public IReadOnlyList<FogDsrEnemyFact> EnemyDetails { get; init; }
            public IReadOnlyList<FogDsrBossDefeatFact> BossDefeatHandlers { get; init; }
            public IReadOnlyList<FogDsrRegionFact> RegionDetails { get; init; }
            public IReadOnlyList<FogDsrTreasureFact> Treasures { get; init; }
        }

        public IReadOnlyDictionary<FogMapId, Map> Maps { get; }
        public IReadOnlySet<int> ItemLotIds { get; }
        public IReadOnlyDictionary<int, FogDsrItemLotFact> ItemLots { get; }
        public IReadOnlyList<FogDsrNpcDropLotFact> NpcDropLots { get; }
        /// <summary>Schema-confirmed 2003[4]/[36] item-lot award calls; InitializeEvent arguments are resolved per caller when possible.</summary>
        public IReadOnlyList<FogDsrItemLotAwardFact> ItemLotAwards { get; }
        /// <summary>Resolved pristine EMEVD IfPlayerHasItem checks with their exact map-event and caller anchors.</summary>
        public IReadOnlyList<FogDsrItemPossessionFact> ItemPossessionChecks { get; }
        public IReadOnlyDictionary<string, IReadOnlySet<int>> ItemLotParamTableIds { get; }
        public IReadOnlyList<FogDsrEventFlagWrite> CommonEventFlagWrites { get; }
        /// <summary>Completion flags passed to common event 970 and consumed by its event-flag conditions.</summary>
        public IReadOnlySet<int> BossRewardCompletionFlags { get; }
        /// <summary>All positive ItemLot values stored in the five MSB treasure slots, including inactive extras.</summary>
        public IReadOnlyList<FogDsrTreasureLotFact> TreasureLots { get; }
        /// <summary>The first MSB treasure slot, which the format reader identifies as the functional item-lot reference.</summary>
        public IReadOnlyList<FogDsrTreasureLotFact> FunctionalTreasureLots { get; }
        public IReadOnlyList<FogDsrTreasureLotFact> ResolvedTreasureLots { get; }
        public IReadOnlyList<FogDsrTreasureLotFact> UnresolvedTreasureLots { get; }
        /// <summary>Positive values in treasure slots 2-5, retained for audit but not used to derive item locations.</summary>
        public IReadOnlyList<FogDsrTreasureLotFact> AdditionalTreasureSlotLots { get; }
        public IReadOnlySet<FogGameItemId> ItemsInLots { get; }
        public IReadOnlySet<FogGameItemId> GameItemIds { get; }
        public IReadOnlyDictionary<FogGameItemId, string> GameItemNames { get; }
        public FogDsrEventFactSource EventFactSource { get; }

        /// <summary>Resolves one English game item name to its unique category/ID, refusing missing or ambiguous names.</summary>
        public FogGameItemId RequireUniqueGameItem(string englishName)
        {
            FogGameItemId[] matches = FindGameItems(englishName, null);
            if (matches.Length == 0) throw new KeyNotFoundException($"The pristine DSR English item tables have no item named '{englishName}'.");
            if (matches.Length > 1) throw new InvalidDataException($"The pristine DSR English item tables have {matches.Length} entries named '{englishName}'; resolve it with an explicit game-item category.");
            return matches[0];
        }

        /// <summary>Resolves an English item name within the category named by the DSR item-table format.</summary>
        public FogGameItemId RequireUniqueGameItem(FogGameItemCategory category, string englishName)
        {
            if (!Enum.IsDefined(typeof(FogGameItemCategory), category)) throw new ArgumentOutOfRangeException(nameof(category));
            FogGameItemId[] matches = FindGameItems(englishName, category);
            if (matches.Length == 0) throw new KeyNotFoundException($"The pristine DSR {category} item table has no item named '{englishName}'.");
            if (matches.Length > 1) throw new InvalidDataException($"The pristine DSR {category} item table has {matches.Length} entries named '{englishName}'; the catalog must disambiguate them by item ID.");
            return matches[0];
        }

        private FogGameItemId[] FindGameItems(string englishName, FogGameItemCategory? category)
        {
            if (string.IsNullOrWhiteSpace(englishName)) throw new ArgumentException("An English game item name is required.", nameof(englishName));
            return GameItemNames
                .Where(entry => (!category.HasValue || entry.Key.Category == (int)category.Value)
                    && string.Equals(entry.Value, englishName, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Key)
                .OrderBy(item => item.Category)
                .ThenBy(item => item.ItemId)
                .ToArray();
        }

        private FogDsrFacts(
            IReadOnlyDictionary<FogMapId, Map> maps,
            IReadOnlySet<int> itemLotIds,
            IReadOnlyDictionary<int, FogDsrItemLotFact> itemLots,
            IReadOnlyList<FogDsrNpcDropLotFact> npcDropLots,
            IReadOnlyList<FogDsrItemLotAwardFact> itemLotAwards,
            IReadOnlyList<FogDsrItemPossessionFact> itemPossessionChecks,
            IReadOnlyDictionary<string, IReadOnlySet<int>> itemLotParamTableIds,
            IReadOnlyList<FogDsrEventFlagWrite> commonEventFlagWrites,
            IReadOnlySet<int> bossRewardCompletionFlags,
            IReadOnlyList<FogDsrTreasureLotFact> treasureLots,
            IReadOnlySet<FogGameItemId> itemsInLots,
            IReadOnlySet<FogGameItemId> gameItemIds,
            IReadOnlyDictionary<FogGameItemId, string> gameItemNames,
            FogDsrEventFactSource eventFactSource)
        {
            Maps = maps;
            ItemLotIds = itemLotIds;
            ItemLots = itemLots;
            NpcDropLots = Snapshot(npcDropLots, "NPC drop ItemLot references");
            ItemLotAwards = Snapshot(itemLotAwards, "EMEVD AwardItemLot calls");
            ItemPossessionChecks = Snapshot(itemPossessionChecks, "EMEVD item-possession checks");
            ItemLotParamTableIds = itemLotParamTableIds;
            CommonEventFlagWrites = Snapshot(commonEventFlagWrites, "common EMEVD event-flag writes");
            BossRewardCompletionFlags = bossRewardCompletionFlags;
            TreasureLots = treasureLots;
            FunctionalTreasureLots = Array.AsReadOnly(treasureLots.Where(treasure => treasure.Slot == 1).ToArray());
            ResolvedTreasureLots = Array.AsReadOnly(FunctionalTreasureLots.Where(treasure => itemLotIds.Contains(treasure.ItemLotId)).ToArray());
            UnresolvedTreasureLots = Array.AsReadOnly(FunctionalTreasureLots.Where(treasure => !itemLotIds.Contains(treasure.ItemLotId)).ToArray());
            AdditionalTreasureSlotLots = Array.AsReadOnly(treasureLots.Where(treasure => treasure.Slot > 1).ToArray());
            ItemsInLots = itemsInLots;
            GameItemIds = gameItemIds;
            GameItemNames = gameItemNames;
            EventFactSource = eventFactSource;
        }

        /// <summary>
        /// Verifies the pinned fact source. When a clean game directory is supplied, its maps and
        /// GameParam must match the pinned bytes and its EMEVD archives supply event facts; the
        /// FogMod reference EMEVD is never presented as pristine in that mode.
        /// </summary>
        public static FogDsrFacts Read(string dataRoot, string pristineGameDirectory = null, bool includeLegacyAnnotations = true)
        {
            if (string.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("A verified data root is required.", nameof(dataRoot));
            string root = Path.GetFullPath(dataRoot);
            DataRoot.Verify(root, includeLegacyAnnotations);
            string gameData = Path.Combine(root, "dist", "DS1R");
            if (!Directory.Exists(gameData)) throw new DirectoryNotFoundException($"Verified DSR fact data was not found under {gameData}.");
            string pristineDirectory = string.IsNullOrWhiteSpace(pristineGameDirectory) ? null : Path.GetFullPath(pristineGameDirectory);
            string eventData = pristineDirectory == null ? Path.Combine(gameData, "event") : Path.Combine(pristineDirectory, "event");
            if (!Directory.Exists(eventData)) throw new DirectoryNotFoundException($"DSR event archives were not found under {eventData}.");
            if (pristineDirectory != null) VerifyPristineMapAndParamParity(gameData, pristineDirectory);

            var maps = new Dictionary<FogMapId, Map>();
            var treasureLots = new List<FogDsrTreasureLotFact>();
            string commonEventPath = Path.Combine(eventData, "common.emevd.dcx");
            IReadOnlyList<FogDsrEventFlagWrite> commonEventFlagWrites = ReadEventFlagWrites(commonEventPath);
            var itemLotAwards = new List<FogDsrItemLotAwardFact>();
            var itemPossessionChecks = new List<FogDsrItemPossessionFact>();
            var indexedEventArchives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddItemLotAwards(commonEventPath, itemLotAwards, indexedEventArchives);
            IReadOnlySet<int> bossRewardCompletionFlags = ReadBossRewardCompletionFlags(commonEventPath);
            foreach (AnnotationData.MapSpec spec in AnnotationData.nameSpecs.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                string path = Path.Combine(gameData, "map", "MapStudio", spec.Map + ".msb");
                if (!File.Exists(path)) throw new FileNotFoundException($"Pristine map {spec.Map} is missing.", path);
                MSB1 msb = MSB1.Read(path);
                string eventArchivePath = Path.Combine(eventData, spec.Map + ".emevd.dcx");
                if (!File.Exists(eventArchivePath) && spec.Map.EndsWith("_01", StringComparison.Ordinal))
                    eventArchivePath = Path.Combine(eventData, spec.Map.Substring(0, spec.Map.Length - 2) + "00.emevd.dcx");
                if (!File.Exists(eventArchivePath)) throw new FileNotFoundException($"Pristine map event archive for {spec.Name} is missing.", eventArchivePath);
                AddItemLotAwards(eventArchivePath, itemLotAwards, indexedEventArchives);
                itemPossessionChecks.AddRange(ReadItemPossessionChecks(eventArchivePath, new FogMapId(spec.Name)));
                IReadOnlyList<FogDsrEventFlagWrite> eventFlagWrites = ReadEventFlagWrites(eventArchivePath);
                IReadOnlySet<long> eventIds = ReadEventIds(eventArchivePath);
                IReadOnlyList<FogDsrCutsceneWarpFact> cutsceneWarps = ReadCutsceneWarps(eventArchivePath, new FogMapId(spec.Name));
                IReadOnlyList<FogDsrBossDefeatFact> bossDefeatHandlers = ReadBossDefeatHandlers(eventArchivePath);
                var spawnPoints = new Dictionary<int, string>();
                foreach (MSB1.Event.SpawnPoint spawn in msb.Events.SpawnPoints)
                {
                    if (spawn.EntityID > 0 && !spawnPoints.TryAdd(spawn.EntityID, spawn.SpawnPointName ?? spawn.RegionName ?? string.Empty))
                        throw new InvalidDataException($"Map {spec.Map} has duplicate spawn-point ID {spawn.EntityID}.");
                }

                IReadOnlyList<FogDsrObjectFact> objectDetails = Snapshot(msb.Parts.Objects.Select(part => new FogDsrObjectFact(
                    part.Name, part.EntityID, part.CollisionName, new FogDsrTransform(part.Position.X, part.Position.Y, part.Position.Z, part.Rotation.Y))), "map objects");
                IReadOnlyList<FogDsrObjectActFact> objectActDetails = Snapshot(msb.Events.ObjActs.Select(evt =>
                {
                    FogDsrObjectFact[] matchingObjects = objectDetails.Where(part =>
                        string.Equals(part.Name, evt.ObjActPartName, StringComparison.Ordinal)).ToArray();
                    if (matchingObjects.Length > 1)
                        throw new InvalidDataException($"Map {spec.Map} ObjAct entity {evt.ObjActEntityID} refers to ambiguous object part '{evt.ObjActPartName}'.");
                    return new FogDsrObjectActFact(evt.ObjActPartName, evt.ObjActEntityID,
                        matchingObjects.SingleOrDefault()?.EntityId, evt.ObjActParamID, evt.EventFlagID,
                        Convert.ToInt32(evt.ObjActState, CultureInfo.InvariantCulture));
                }), "map object-act records");
                IReadOnlyList<FogDsrRegionFact> regionDetails = Snapshot(msb.Regions.Regions.Select(region => new FogDsrRegionFact(
                    region.Name, region.EntityID, new FogDsrTransform(region.Position.X, region.Position.Y, region.Position.Z, region.Rotation.Y), ReadRegionShape(region.Shape))), "map regions");
                IReadOnlyList<FogDsrTreasureFact> treasureDetails = Snapshot(msb.Events.Treasures.Select(evt =>
                {
                    FogDsrObjectFact[] parts = objectDetails.Where(part => string.Equals(part.Name, evt.TreasurePartName, StringComparison.Ordinal)).ToArray();
                    if (parts.Length > 1) throw new InvalidDataException($"Map {spec.Map} treasure entity {evt.EntityID} refers to ambiguous part '{evt.TreasurePartName}'.");
                    FogDsrObjectFact part = parts.SingleOrDefault();
                    return new FogDsrTreasureFact(evt.EntityID, evt.TreasurePartName, part?.CollisionName,
                        evt.StartDisabled, evt.InChest, part?.Transform, Snapshot(evt.ItemLots ?? Array.Empty<int>(), "treasure ItemLots"));
                }), "map treasure events");

                var mapFacts = new Map
                {
                    Id = new FogMapId(spec.Name),
                    GameMapFileId = spec.Map,
                    Collisions = Set(msb.Parts.Collisions.Select(part => part.Name)),
                    Objects = Set(msb.Parts.Objects.Select(part => part.Name)),
                    ObjectEntityIds = Set(msb.Parts.Objects.Select(part => part.EntityID).Where(id => id > 0)),
                    PlayerEntityIds = Set(msb.Parts.Players.Select(part => part.EntityID).Where(id => id > 0)),
                    Enemies = Set(msb.Parts.Enemies.Select(part => part.Name)),
                    Regions = Set(msb.Regions.Regions.Select(region => region.Name)),
                    RegionEntityIds = Set(msb.Regions.Regions.Select(region => region.EntityID).Where(id => id > 0)),
                    SpawnPoints = new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(spawnPoints),
                    ObjectActFlags = Set(objectActDetails.Select(evt => evt.EventFlagId).Where(id => id > 0)),
                    ObjectActDetails = objectActDetails,
                    EventArchiveName = Path.GetFileName(eventArchivePath),
                    EventIds = eventIds,
                    CutsceneWarps = cutsceneWarps,
                    EventFlagWrites = eventFlagWrites,
                    ObjectDetails = objectDetails,
                    ConnectCollisionDetails = Snapshot(msb.Parts.ConnectCollisions.Select(part => new FogDsrConnectCollisionFact(
                        part.Name, part.CollisionName,
                        Array.AsReadOnly((part.MapID ?? Array.Empty<byte>()).ToArray()))), "map ConnectCollision parts"),
                    CollisionDetails = Snapshot(msb.Parts.Collisions.Select(part => new FogDsrCollisionFact(
                        part.Name, part.EntityID, part.PlayRegionID, part.DisableBonfireEntityID)), "map collisions"),
                    EnemyDetails = Snapshot(msb.Parts.Enemies.Select(part => new FogDsrEnemyFact(
                        part.Name, part.EntityID, part.NPCParamID, part.ThinkParamID, part.CollisionName,
                        new FogDsrTransform(part.Position.X, part.Position.Y, part.Position.Z, part.Rotation.Y))), "map enemies"),
                    BossDefeatHandlers = bossDefeatHandlers,
                    RegionDetails = regionDetails,
                    Treasures = treasureDetails,
                };
                maps.Add(mapFacts.Id, mapFacts);
                foreach (FogDsrTreasureFact treasure in mapFacts.Treasures)
                    foreach ((int lotId, int index) in treasure.ItemLots.Select((value, index) => (value, index)))
                        if (lotId > 0) treasureLots.Add(new FogDsrTreasureLotFact(mapFacts.Id, treasure.EntityId, treasure.PartName, treasure.PartCollisionName, treasure.StartDisabled, treasure.InChest, index + 1, lotId, treasure.PartTransform));
            }

            string layoutsPath = Path.Combine(gameData, "Layouts");
            if (!Directory.Exists(layoutsPath)) throw new DirectoryNotFoundException($"DSR parameter layouts were not found under {layoutsPath}.");
            var layouts = new Dictionary<string, PARAM.Layout>(StringComparer.Ordinal);
            foreach (string layoutPath in Directory.GetFiles(layoutsPath, "*.xml"))
                layouts.Add(Path.GetFileNameWithoutExtension(layoutPath), PARAM.Layout.ReadXMLFile(layoutPath));

            string gameParam = Path.Combine(gameData, "param", "GameParam", "GameParam.parambnd.dcx");
            if (!File.Exists(gameParam)) throw new FileNotFoundException("Pristine DSR GameParam is missing.", gameParam);
            ParamSnapshot snapshot = ParamSnapshot.Read(gameParam, layouts);
            if (!snapshot.Params.TryGetValue("ItemLotParam", out PARAM itemLots))
                throw new InvalidDataException("Pristine DSR GameParam does not contain a readable ItemLotParam.");

            var lotIds = new HashSet<int>();
            var itemLotsById = new Dictionary<int, FogDsrItemLotFact>();
            var itemsInLots = new HashSet<FogGameItemId>();
            var gameItemIds = new HashSet<FogGameItemId>();
            string[] itemParamNames = { "EquipParamWeapon", "EquipParamProtector", "EquipParamAccessory", "EquipParamGoods" };
            for (int category = 0; category < itemParamNames.Length; category++)
            {
                if (!snapshot.Raw.TryGetValue(itemParamNames[category], out byte[] itemParamBytes))
                    throw new InvalidDataException($"Pristine DSR GameParam does not contain {itemParamNames[category]}.");
                PARAM itemParam = PARAM.Read(itemParamBytes);
                foreach (PARAM.Row row in itemParam.Rows) gameItemIds.Add(new FogGameItemId(category, checked((int)row.ID)));
            }
            foreach (PARAM.Row row in itemLots.Rows)
            {
                int lotId = checked((int)row.ID);
                lotIds.Add(lotId);
                var rowItems = new List<FogDsrLotItemFact>();
                for (int slot = 1; slot <= 8; slot++)
                {
                    int itemId = Convert.ToInt32(row[$"lotItemId0{slot}"].Value, CultureInfo.InvariantCulture);
                    if (itemId <= 0) continue;
                    uint rawCategory = Convert.ToUInt32(row[$"lotItemCategory0{slot}"].Value, CultureInfo.InvariantCulture);
                    if (Universe.LotTypes.TryGetValue(rawCategory, out int category))
                    {
                        var item = new FogGameItemId(category, itemId);
                        itemsInLots.Add(item);
                        rowItems.Add(new FogDsrLotItemFact(slot, item));
                    }
                }
                itemLotsById.Add(lotId, new FogDsrItemLotFact(lotId, Array.AsReadOnly(rowItems.ToArray())));
            }

            if (!snapshot.Params.TryGetValue("NpcParam", out PARAM npcParams))
                throw new InvalidDataException("Pristine DSR GameParam does not contain a readable NpcParam.");
            var npcDropLots = new List<FogDsrNpcDropLotFact>();
            foreach (PARAM.Row row in npcParams.Rows)
            {
                int npcParamId = checked((int)row.ID);
                for (int slot = 1; slot <= 6; slot++)
                {
                    int itemLotId = Convert.ToInt32(row[$"itemLotId_{slot}"].Value, CultureInfo.InvariantCulture);
                    if (itemLotId > 0) npcDropLots.Add(new FogDsrNpcDropLotFact(npcParamId, slot, itemLotId));
                }
            }

            var itemLotTableIds = new Dictionary<string, IReadOnlySet<int>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, byte[]> entry in snapshot.Raw.Where(entry => entry.Key.Contains("ItemLotParam", StringComparison.OrdinalIgnoreCase)))
            {
                PARAM table = PARAM.Read(entry.Value);
                itemLotTableIds.Add(entry.Key, table.Rows.Select(row => checked((int)row.ID)).ToFrozenSet());
            }
            IReadOnlyDictionary<FogGameItemId, string> gameItemNames = string.IsNullOrWhiteSpace(pristineGameDirectory)
                ? new System.Collections.ObjectModel.ReadOnlyDictionary<FogGameItemId, string>(new Dictionary<FogGameItemId, string>())
                : ReadGameItemNames(pristineDirectory);

            return new FogDsrFacts(
                new System.Collections.ObjectModel.ReadOnlyDictionary<FogMapId, Map>(maps),
                lotIds,
                new System.Collections.ObjectModel.ReadOnlyDictionary<int, FogDsrItemLotFact>(itemLotsById),
                Array.AsReadOnly(npcDropLots.ToArray()),
                Array.AsReadOnly(itemLotAwards.ToArray()),
                Array.AsReadOnly(itemPossessionChecks.ToArray()),
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlySet<int>>(itemLotTableIds),
                commonEventFlagWrites,
                bossRewardCompletionFlags,
                Array.AsReadOnly(treasureLots.ToArray()),
                itemsInLots,
                gameItemIds,
                gameItemNames,
                pristineDirectory == null ? FogDsrEventFactSource.VerifiedFogReference : FogDsrEventFactSource.PristineGameInstall);
        }

        private static void VerifyPristineMapAndParamParity(string verifiedGameData, string pristineDirectory)
        {
            foreach (AnnotationData.MapSpec spec in AnnotationData.nameSpecs.Values)
            {
                string relative = Path.Combine("map", "MapStudio", spec.Map + ".msb");
                VerifySameBytes(Path.Combine(verifiedGameData, relative), Path.Combine(pristineDirectory, relative), relative);
            }

            const string gameParamRelative = "param\\GameParam\\GameParam.parambnd.dcx";
            VerifySameBytes(Path.Combine(verifiedGameData, gameParamRelative), Path.Combine(pristineDirectory, gameParamRelative), gameParamRelative);
        }

        private static void VerifySameBytes(string verifiedPath, string pristinePath, string relativePath)
        {
            if (!File.Exists(pristinePath)) throw new FileNotFoundException($"The clean DSR copy is missing {relativePath}.", pristinePath);
            byte[] verified = File.ReadAllBytes(verifiedPath);
            byte[] pristine = File.ReadAllBytes(pristinePath);
            if (!verified.AsSpan().SequenceEqual(pristine))
                throw new InvalidDataException($"The clean DSR copy does not match the pinned fact source for {relativePath}.");
        }

        private static IReadOnlyDictionary<FogGameItemId, string> ReadGameItemNames(string gameDirectory)
        {
            string executable = Path.Combine(gameDirectory, "DarkSoulsRemastered.exe");
            string messages = Path.Combine(gameDirectory, "msg", "ENGLISH", "item.msgbnd.dcx");
            if (!File.Exists(executable)) throw new FileNotFoundException("A pristine DSR directory must contain DarkSoulsRemastered.exe.", executable);
            if (!File.Exists(messages)) throw new FileNotFoundException("The pristine DSR English item message bundle is missing.", messages);

            BND3 binder = BND3.Read(messages);
            var tableSpecs = new[]
            {
                (FogGameItemCategory.Weapon, "Weapon_name_.fmg"),
                (FogGameItemCategory.Armor, "Armor_name_.fmg"),
                (FogGameItemCategory.Accessory, "Accessory_name_.fmg"),
                (FogGameItemCategory.Goods, "Item_name_.fmg"),
            };
            var names = new Dictionary<FogGameItemId, string>();
            foreach ((FogGameItemCategory category, string fileName) in tableSpecs)
            {
                BinderFile file = binder.Files.FirstOrDefault(entry =>
                    Path.GetFileName(entry.Name).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                if (file == null) throw new InvalidDataException($"The pristine DSR English item message bundle is missing {fileName}.");

                var table = new Dictionary<int, string>();
                foreach (var entry in FMG.Read(file.Bytes).Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Text)))
                {
                    if (!table.TryAdd(entry.ID, entry.Text.Trim()))
                        throw new InvalidDataException($"The pristine DSR {fileName} has duplicate English item names for ID {entry.ID}.");
                }
                foreach (KeyValuePair<int, string> entry in table)
                    names.Add(new FogGameItemId((int)category, entry.Key), entry.Value);
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<FogGameItemId, string>(names);
        }

        private static IReadOnlyList<FogDsrEventFlagWrite> ReadEventFlagWrites(string eventArchivePath)
        {
            if (!File.Exists(eventArchivePath)) throw new FileNotFoundException("A pristine DSR EMEVD archive is missing.", eventArchivePath);
            byte[] bytes = File.ReadAllBytes(eventArchivePath);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD archive = EMEVD.Read(bytes);
            var writes = new List<FogDsrEventFlagWrite>();
            foreach (EMEVD.Event evt in archive.Events)
            {
                foreach (EMEVD.Instruction instruction in evt.Instructions)
                {
                    if (instruction.Bank != 2003 || instruction.ID != 2) continue;
                    object[] args = instruction.UnpackArgs(new[]
                    {
                        (EMEVD.Instruction.ArgType)5,
                        (EMEVD.Instruction.ArgType)1,
                    }).ToArray();
                    int flagId = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
                    if (flagId > 0) writes.Add(new FogDsrEventFlagWrite(evt.ID, flagId));
                }
            }
            return Array.AsReadOnly(writes.ToArray());
        }

        private static void AddItemLotAwards(string eventArchivePath, List<FogDsrItemLotAwardFact> destination, HashSet<string> indexedArchives)
        {
            string fullPath = Path.GetFullPath(eventArchivePath);
            if (!indexedArchives.Add(fullPath)) return;
            byte[] bytes = File.ReadAllBytes(fullPath);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD archive = EMEVD.Read(bytes);
            foreach (EMEVD.Event evt in archive.Events)
            {
                for (int index = 0; index < evt.Instructions.Count; index++)
                {
                    EMEVD.Instruction instruction = evt.Instructions[index];
                    if (instruction.Bank != 2003 || instruction.ID is not (4 or 36)) continue;
                    if (instruction.ArgData.Length != sizeof(int))
                        throw new InvalidDataException($"Item-lot award in {Path.GetFileName(fullPath)} event {evt.ID} instruction {index} has {instruction.ArgData.Length} argument bytes; expected one ItemLot ID.");

                    EMEVD.Parameter[] bindings = evt.Parameters.Where(parameter => parameter.InstructionIndex == index
                        && parameter.TargetStartByte == 0 && parameter.ByteCount == sizeof(int)).ToArray();
                    if (bindings.Length == 0)
                    {
                        int itemLotId = Convert.ToInt32(instruction.UnpackArgs(new[] { (EMEVD.Instruction.ArgType)5 }).Single(), CultureInfo.InvariantCulture);
                        destination.Add(new FogDsrItemLotAwardFact(Path.GetFileName(fullPath), evt.ID, index, itemLotId, null, null));
                        continue;
                    }
                    if (bindings.Length != 1)
                        throw new InvalidDataException($"Item-lot award in {Path.GetFileName(fullPath)} event {evt.ID} instruction {index} has ambiguous event-parameter bindings.");

                    EMEVD.Parameter binding = bindings[0];
                    int sourceOffset = checked(8 + checked((int)binding.SourceStartByte));
                    EMEVD.Event[] callers = archive.Events.Where(caller => caller.Instructions.Any(call => call.Bank == 2000 && call.ID == 0
                        && call.ArgData.Length >= 8 && BitConverter.ToInt32(call.ArgData, 4) == evt.ID)).ToArray();
                    int resolvedCallers = 0;
                    foreach (EMEVD.Event caller in callers)
                    {
                        for (int callerIndex = 0; callerIndex < caller.Instructions.Count; callerIndex++)
                        {
                            EMEVD.Instruction call = caller.Instructions[callerIndex];
                            if (call.Bank != 2000 || call.ID != 0 || call.ArgData.Length < 8
                                || BitConverter.ToInt32(call.ArgData, 4) != evt.ID) continue;
                            if (sourceOffset < 8 || sourceOffset + sizeof(int) > call.ArgData.Length)
                                throw new InvalidDataException($"InitializeEvent in {Path.GetFileName(fullPath)} event {caller.ID} instruction {callerIndex} does not supply the ItemLot argument for event {evt.ID}.");
                            destination.Add(new FogDsrItemLotAwardFact(
                                Path.GetFileName(fullPath), evt.ID, index,
                                BitConverter.ToInt32(call.ArgData, sourceOffset), caller.ID, callerIndex,
                                string.Equals(Path.GetFileName(fullPath), "common.emevd.dcx", StringComparison.OrdinalIgnoreCase) && evt.ID == 960
                                    ? ReadEventParameterArgument(evt, call, 4, 0)
                                    : null,
                                string.Equals(Path.GetFileName(fullPath), "common.emevd.dcx", StringComparison.OrdinalIgnoreCase) && (evt.ID is 960 or 970)
                                    ? ReadEventParameterArgument(evt, call, 3, 0)
                                    : null));
                            resolvedCallers++;
                        }
                    }
                    if (resolvedCallers == 0)
                        destination.Add(new FogDsrItemLotAwardFact(Path.GetFileName(fullPath), evt.ID, index, null, null, null));
                }
            }
        }

        private static int? ReadEventParameterArgument(EMEVD.Event evt, EMEVD.Instruction initializer, int bank, int instructionId)
        {
            int[] conditionIndexes = evt.Instructions.Select((instruction, index) => (instruction, index))
                .Where(entry => entry.instruction.Bank == bank && entry.instruction.ID == instructionId)
                .Select(entry => entry.index).ToArray();
            if (conditionIndexes.Length != 1) return null;

            EMEVD.Parameter[] bindings = evt.Parameters.Where(parameter => parameter.InstructionIndex == conditionIndexes[0]
                && parameter.TargetStartByte == sizeof(int) && parameter.ByteCount == sizeof(int)).ToArray();
            if (bindings.Length != 1) return null;

            int sourceOffset = checked(8 + checked((int)bindings[0].SourceStartByte));
            if (sourceOffset < 8 || sourceOffset + sizeof(int) > initializer.ArgData.Length)
                throw new InvalidDataException($"InitializeEvent does not supply parameter data for common event {evt.ID} condition {bank}[{instructionId}].");
            return BitConverter.ToInt32(initializer.ArgData, sourceOffset);
        }

        private static IReadOnlySet<long> ReadEventIds(string eventArchivePath)
        {
            if (!File.Exists(eventArchivePath)) throw new FileNotFoundException("A pristine DSR EMEVD archive is missing.", eventArchivePath);
            byte[] bytes = File.ReadAllBytes(eventArchivePath);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD archive = EMEVD.Read(bytes);
            return archive.Events.Select(evt => evt.ID).ToFrozenSet();
        }

        private static IReadOnlyList<FogDsrCutsceneWarpFact> ReadCutsceneWarps(string eventArchivePath, FogMapId sourceMap)
        {
            byte[] bytes = File.ReadAllBytes(eventArchivePath);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD archive = EMEVD.Read(bytes);
            var warps = new List<FogDsrCutsceneWarpFact>();
            foreach (EMEVD.Event evt in archive.Events)
            {
                for (int index = 0; index < evt.Instructions.Count; index++)
                {
                    EMEVD.Instruction instruction = evt.Instructions[index];
                    if (instruction.Bank != 2002 || instruction.ID != 4) continue;
                    if (instruction.ArgData.Length < 14)
                        throw new InvalidDataException($"PlayCutsceneAndWarpPlayer in {Path.GetFileName(eventArchivePath)} event {evt.ID} instruction {index} has fewer than 14 argument bytes.");

                    int cutsceneId = BitConverter.ToInt32(instruction.ArgData, 0);
                    int playbackType = BitConverter.ToInt32(instruction.ArgData, 4);
                    int destinationEntityId = BitConverter.ToInt32(instruction.ArgData, 8);
                    byte destinationArea = instruction.ArgData[12];
                    byte destinationBlock = instruction.ArgData[13];
                    string destinationMapPrefix = $"m{destinationArea:D2}_{destinationBlock:D2}_";
                    AnnotationData.MapSpec[] mapSpecs = AnnotationData.specs.Values.Where(mapSpec =>
                        mapSpec.Map.StartsWith(destinationMapPrefix, StringComparison.Ordinal)).ToArray();
                    string destinationMapFileId = mapSpecs.Length == 1 ? mapSpecs[0].Map : destinationMapPrefix;
                    FogMapId? destinationMap = mapSpecs.Length == 1 ? new FogMapId(mapSpecs[0].Name) : null;
                    bool hasBindings = evt.Parameters.Any(parameter => parameter.InstructionIndex == index
                        && parameter.TargetStartByte < 14
                        && parameter.TargetStartByte + parameter.ByteCount > 0);
                    warps.Add(new FogDsrCutsceneWarpFact(sourceMap, evt.ID, index, cutsceneId, playbackType,
                        destinationEntityId, destinationMapFileId, destinationMap, hasBindings));
                }
            }
            return Array.AsReadOnly(warps.ToArray());
        }

        private static IReadOnlyList<FogDsrBossDefeatFact> ReadBossDefeatHandlers(string eventArchivePath)
        {
            byte[] bytes = File.ReadAllBytes(eventArchivePath);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD archive = EMEVD.Read(bytes);
            var handlers = new List<FogDsrBossDefeatFact>();
            foreach (EMEVD.Event evt in archive.Events)
            {
                foreach (EMEVD.Instruction instruction in evt.Instructions)
                {
                    if (instruction.Bank != 2003 || instruction.ID != 12) continue;
                    int enemyEntityId = Convert.ToInt32(instruction.UnpackArgs(new[] { (EMEVD.Instruction.ArgType)5 }).Single(), CultureInfo.InvariantCulture);
                    if (enemyEntityId > 0) handlers.Add(new FogDsrBossDefeatFact(evt.ID, enemyEntityId));
                }
            }
            return Array.AsReadOnly(handlers.ToArray());
        }

        private sealed record EmevdCallSite(EMEVD.Event Caller, int InstructionIndex, EMEVD.Instruction Instruction);

        private static IReadOnlyList<FogDsrItemPossessionFact> ReadItemPossessionChecks(string eventArchivePath, FogMapId map)
        {
            byte[] bytes = File.ReadAllBytes(eventArchivePath);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD archive = EMEVD.Read(bytes);
            var callers = new Dictionary<long, List<EmevdCallSite>>();
            foreach (EMEVD.Event caller in archive.Events)
            {
                for (int instructionIndex = 0; instructionIndex < caller.Instructions.Count; instructionIndex++)
                {
                    EMEVD.Instruction instruction = caller.Instructions[instructionIndex];
                    if (instruction.Bank != 2000 || instruction.ID != 0 || instruction.ArgData.Length < 8) continue;
                    long targetEventId = (uint)BitConverter.ToInt32(instruction.ArgData, 4);
                    if (!callers.TryGetValue(targetEventId, out List<EmevdCallSite> callSites))
                        callers[targetEventId] = callSites = new List<EmevdCallSite>();
                    callSites.Add(new EmevdCallSite(caller, instructionIndex, instruction));
                }
            }

            var result = new List<FogDsrItemPossessionFact>();
            foreach (EMEVD.Event evt in archive.Events)
            {
                EmevdCallSite[] eventCallers = callers.TryGetValue(evt.ID, out List<EmevdCallSite> mapped)
                    ? mapped.ToArray()
                    : new EmevdCallSite[] { null };
                for (int instructionIndex = 0; instructionIndex < evt.Instructions.Count; instructionIndex++)
                {
                    EMEVD.Instruction instruction = evt.Instructions[instructionIndex];
                    if (instruction.Bank != 3 || instruction.ID != 4 || instruction.ArgData.Length < 12) continue;
                    foreach (EmevdCallSite caller in eventCallers)
                    {
                        (int Group, bool GroupResolved) group = ResolvePossessionByte(evt, instructionIndex, 0, caller, signed: true);
                        (int Category, bool CategoryResolved) category = ResolvePossessionByte(evt, instructionIndex, 1, caller);
                        (int ItemId, bool ItemIdResolved) itemId = ResolvePossessionInt(evt, instructionIndex, 4, caller);
                        (int State, bool StateResolved) state = ResolvePossessionByte(evt, instructionIndex, 8, caller);
                        if (!group.GroupResolved || !category.CategoryResolved || !itemId.ItemIdResolved || !state.StateResolved)
                            continue;
                        result.Add(new FogDsrItemPossessionFact(
                            map, evt.ID, instructionIndex, group.Group,
                            new FogGameItemId(category.Category, itemId.ItemId), state.State,
                            caller?.Caller.ID,
                            caller == null ? null : caller.InstructionIndex));
                    }
                }
            }
            return Array.AsReadOnly(result.ToArray());
        }

        private static (int Value, bool Resolved) ResolvePossessionInt(
            EMEVD.Event evt,
            int instructionIndex,
            int targetOffset,
            EmevdCallSite caller)
        {
            int literal = BitConverter.ToInt32(evt.Instructions[instructionIndex].ArgData, targetOffset);
            EMEVD.Parameter[] bindings = evt.Parameters.Where(parameter => parameter.InstructionIndex == instructionIndex
                && parameter.TargetStartByte == targetOffset && parameter.ByteCount == sizeof(int)).ToArray();
            if (bindings.Length == 0) return (literal, true);
            if (bindings.Length != 1 || caller == null) return (literal, false);
            int sourceOffset = checked(8 + checked((int)bindings[0].SourceStartByte));
            if (sourceOffset < 8 || sourceOffset + sizeof(int) > caller.Instruction.ArgData.Length)
                throw new InvalidDataException($"InitializeEvent for {evt.ID} has an out-of-range item-check parameter source at {sourceOffset}.");
            return (BitConverter.ToInt32(caller.Instruction.ArgData, sourceOffset), true);
        }

        private static (int Value, bool Resolved) ResolvePossessionByte(
            EMEVD.Event evt,
            int instructionIndex,
            int targetOffset,
            EmevdCallSite caller,
            bool signed = false)
        {
            byte literal = evt.Instructions[instructionIndex].ArgData[targetOffset];
            EMEVD.Parameter[] bindings = evt.Parameters.Where(parameter => parameter.InstructionIndex == instructionIndex
                && parameter.TargetStartByte == targetOffset && parameter.ByteCount == sizeof(byte)).ToArray();
            if (bindings.Length == 0) return (signed ? unchecked((sbyte)literal) : literal, true);
            if (bindings.Length != 1 || caller == null) return (signed ? unchecked((sbyte)literal) : literal, false);
            int sourceOffset = checked(8 + checked((int)bindings[0].SourceStartByte));
            if (sourceOffset < 8 || sourceOffset >= caller.Instruction.ArgData.Length)
                throw new InvalidDataException($"InitializeEvent for {evt.ID} has an out-of-range item-check byte parameter source at {sourceOffset}.");
            byte value = caller.Instruction.ArgData[sourceOffset];
            return (signed ? unchecked((sbyte)value) : value, true);
        }

        private static IReadOnlySet<int> ReadBossRewardCompletionFlags(string commonEventPath)
        {
            byte[] bytes = File.ReadAllBytes(commonEventPath);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            EMEVD archive = EMEVD.Read(bytes);
            EMEVD.Event[] startupEvents = archive.Events.Where(evt => evt.ID == 0).ToArray();
            EMEVD.Event[] rewardEvents = archive.Events.Where(evt => evt.ID == 970).ToArray();
            if (startupEvents.Length != 1 || rewardEvents.Length != 1)
                throw new InvalidDataException("The common EMEVD must contain unique events 0 and 970 to resolve boss reward completion flags.");

            EMEVD.Event startup = startupEvents[0];
            EMEVD.Event reward = rewardEvents[0];
            bool rewardReadsParameterZeroAsFlag = reward.Instructions.Select((instruction, index) => (instruction, index))
                .Any(entry => ((entry.instruction.Bank == 3 && entry.instruction.ID == 0)
                        || (entry.instruction.Bank == 1003 && entry.instruction.ID == 2))
                    && reward.Parameters.Any(parameter => parameter.InstructionIndex == entry.index
                        && parameter.SourceStartByte == 0 && parameter.TargetStartByte == 4 && parameter.ByteCount == 4));
            if (!rewardReadsParameterZeroAsFlag) return Array.Empty<int>().ToFrozenSet();

            var flags = new HashSet<int>();
            foreach ((EMEVD.Instruction instruction, int index) in startup.Instructions.Select((instruction, index) => (instruction, index)))
            {
                if (instruction.Bank != 2000 || (instruction.ID != 0 && instruction.ID != 6) || instruction.ArgData.Length < 12) continue;
                if (BitConverter.ToInt32(instruction.ArgData, 4) != 970) continue;
                bool parameterOverridden = startup.Parameters.Any(parameter => parameter.InstructionIndex == index
                    && parameter.TargetStartByte < instruction.ArgData.Length - 8
                    && parameter.TargetStartByte + parameter.ByteCount > 8);
                if (parameterOverridden) continue;
                int flagId = BitConverter.ToInt32(instruction.ArgData, 8);
                if (flagId > 0) flags.Add(flagId);
            }
            return flags.ToFrozenSet();
        }

        /// <summary>Checks game-derived references in the authored annotation model against pristine data.</summary>
        public IReadOnlyList<string> Validate(FogAnnotationCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var problems = new List<string>();
            Map GetMap(FogMapId id, string context)
            {
                if (Maps.TryGetValue(id, out Map map)) return map;
                problems.Add($"{context} refers to map '{id}', which is not in the verified DSR map set.");
                return null;
            }

            void RequireAreaMap(FogAreaId areaId, FogMapId mapId, string context)
            {
                FogArea area = catalog.Areas.FirstOrDefault(candidate => candidate.Id == areaId);
                if (area != null && !(area.Maps ?? Array.Empty<FogMapId>()).Contains(mapId))
                    problems.Add($"{context} maps to area {areaId}, but that area does not declare endpoint map {mapId}.");
            }

            foreach (FogArea area in catalog.Areas)
                foreach (FogMapId map in area.Maps ?? Array.Empty<FogMapId>())
                    GetMap(map, $"Area {area.Id}");

            foreach (FogEntrance entrance in catalog.Entrances)
            {
                Map entranceMap = GetMap(entrance.Map, $"Entrance {entrance.Identity}");
                if (entranceMap != null && !entrance.Tags.HasFlag(FogEntranceTags.Unused)
                    && !entrance.Tags.HasFlag(FogEntranceTags.Door) && entrance.Name != null
                    && !entranceMap.Objects.Contains(entrance.Name))
                    problems.Add($"Entrance object '{entrance.Name}' is absent from map {entrance.Map}.");

                foreach (FogEntranceSide side in new[] { entrance.ASide, entrance.BSide }.Where(value => value != null))
                {
                    FogMapId mapId = side.DestinationMap ?? entrance.Map;
                    Map map = GetMap(mapId, $"Entrance {entrance.Identity} side");
                    if (!entrance.Tags.HasFlag(FogEntranceTags.Unused) && !entrance.Tags.HasFlag(FogEntranceTags.Door))
                        RequireAreaMap(side.Area, mapId, $"Entrance {entrance.Identity} side");
                    if (map != null && side.ActionRegion > 0 && !map.RegionEntityIds.Contains(side.ActionRegion))
                        problems.Add($"Entrance {entrance.Identity} action region {side.ActionRegion} is absent from map {mapId}.");
                }
            }

            var validatedRuntimeWarpRegions = new HashSet<(FogMapId Map, int EntityId)>();
            if (EventFactSource == FogDsrEventFactSource.PristineGameInstall)
            {
                foreach (FogProjectFlagWarpPolicyV1 policy in FogProjectAnnotationPolicy.FlagWarpPoliciesV1)
                {
                    bool candidatePresent = catalog.Warps.Any(warp => warp.Id == policy.RuntimeDestinationRegionEntityId
                        && warp.Map == policy.Map);
                    if (!candidatePresent) continue;
                    try
                    {
                        FogWarp expected = FogAnnotationPolicyResolver.ResolveFlagTriggeredWarpPoliciesV1(this, new[] { policy }).Single();
                        if (catalog.Warps.Contains(expected))
                            validatedRuntimeWarpRegions.Add((policy.Map, policy.RuntimeDestinationRegionEntityId));
                        else
                            problems.Add($"Flag-triggered Warp {policy.Id} does not match its resolved project policy.");
                    }
                    catch (Exception error)
                    {
                        problems.Add($"Flag-triggered Warp {policy.Id} failed pristine region validation: {error.Message}");
                    }
                }
            }

            foreach (FogWarp warp in catalog.Warps)
            {
                if (warp.Tags.HasFlag(FogWarpTags.Unused)) continue;
                foreach (FogWarpSide side in new[] { warp.ASide, warp.BSide }.Where(value => value != null && !value.Tags.HasFlag(FogWarpSideTags.Temporary)))
                {
                    FogMapId mapId = side.DestinationMap ?? warp.Map;
                    GetMap(mapId, $"Warp {warp.Identity} side");
                    RequireAreaMap(side.Area, mapId, $"Warp {warp.Identity} side");
                }
                if (warp.Tags.HasFlag(FogWarpTags.NoRandom) || warp.BSide == null) continue;
                FogMapId destinationId = warp.BSide.DestinationMap ?? warp.Map;
                Map destination = GetMap(destinationId, $"Warp {warp.Identity} destination");
                if (destination == null) continue;
                if (warp.BSide.Tags.HasFlag(FogWarpSideTags.Player)
                    && !destination.PlayerEntityIds.Contains(warp.Id))
                    problems.Add($"Warp {warp.Identity} player entity {warp.Id} is absent from map {destinationId}.");
                else if (!warp.BSide.Tags.HasFlag(FogWarpSideTags.Player)
                    && !destination.RegionEntityIds.Contains(warp.Id)
                    && !validatedRuntimeWarpRegions.Contains((destinationId, warp.Id)))
                    problems.Add($"Warp {warp.Identity} destination region {warp.Id} is absent from map {destinationId}.");
            }

            foreach (FogCustomStart start in catalog.CustomStarts)
            {
                Map map = GetMap(start.Respawn.Map, $"Custom start {start.Id}");
                FogArea startArea = catalog.Areas.FirstOrDefault(area => area.Id == start.Area);
                if (startArea != null && !(startArea.Maps ?? Array.Empty<FogMapId>()).Contains(start.Respawn.Map))
                    problems.Add($"Custom start {start.Id} respawns on {start.Respawn.Map}, which is not declared as a map for area {start.Area}.");
                if (map != null)
                {
                    if (!map.SpawnPoints.TryGetValue(start.Respawn.SpawnPoint.Value, out string regionName))
                        problems.Add($"Custom start {start.Id} spawn-point ID {start.Respawn.SpawnPoint.Value} is absent from map {start.Respawn.Map}.");
                    else if (!map.Regions.Contains(regionName))
                        problems.Add($"Custom start {start.Id} spawn-point region '{regionName}' is absent from map {start.Respawn.Map}.");
                }
            }

            foreach (FogEnemyScalingRule enemy in catalog.Enemies)
            {
                Map map = GetMap(enemy.Map, "Enemy scaling rule");
                if (map == null) continue;
                if (!string.IsNullOrEmpty(enemy.Collision) && !map.Collisions.Contains(enemy.Collision))
                    problems.Add($"Enemy scaling collision '{enemy.Collision}' is absent from map {enemy.Map}.");
                foreach (string include in enemy.Includes ?? Array.Empty<string>())
                    if (!map.Enemies.Contains(include)) problems.Add($"Enemy include '{include}' is absent from map {enemy.Map}.");
            }

            foreach (FogDefaultFlagCollision flag in catalog.DefaultFlagCols)
            {
                Map map = GetMap(flag.Map, "Default boss-flag collision");
                if (map != null && !map.Collisions.Contains(flag.Collision))
                    problems.Add($"Default boss-flag collision '{flag.Collision}' is absent from map {flag.Map}.");
            }

            foreach (FogKeyItem item in catalog.KeyItems)
            {
                if (!GameItemIds.Contains(item.GameItemId))
                    problems.Add($"Key item {item.Name} ({item.GameItemId}) is absent from the matching pristine GameParam item table.");
                if (!ItemsInLots.Contains(item.GameItemId) && !item.Tags.HasFlag(FogKeyItemTags.RandomOnly) && !item.Tags.HasFlag(FogKeyItemTags.Hard))
                    problems.Add($"Key item {item.Name} ({item.GameItemId}) does not occur in any pristine ItemLotParam slot.");
            }

            foreach (FogLotLocation lot in catalog.LotLocations)
            {
                if (!ItemLotIds.Contains(lot.FirstLot.Value))
                {
                    problems.Add($"ItemLot start {lot.FirstLot.Value} is absent from pristine ItemLotParam.");
                    continue;
                }

                FogArea owner = catalog.Areas.First(area => area.Id == lot.Area);
                int rowId = lot.FirstLot.Value;
                while (ItemLotIds.Contains(rowId))
                {
                    foreach (FogDsrTreasureLotFact source in FunctionalTreasureLots.Where(source => source.ItemLotId == rowId))
                        if (!(owner.Maps ?? Array.Empty<FogMapId>()).Contains(source.Map))
                            problems.Add($"Lot {rowId} is assigned to area {lot.Area}, but its MSB treasure source is on {source.Map}, outside that area's declared maps.");
                    if (rowId == int.MaxValue) break;
                    rowId++;
                }
            }

            return problems;
        }

        private static IReadOnlySet<T> Set<T>(IEnumerable<T> values) => values.ToFrozenSet(EqualityComparer<T>.Default);

        private static FogDsrRegionShape ReadRegionShape(MSB.Shape shape) => shape switch
        {
            MSB.Shape.Point => new FogDsrRegionShape(FogDsrRegionShapeKind.Point, null, null, null, null),
            MSB.Shape.Box box => new FogDsrRegionShape(FogDsrRegionShapeKind.Box, box.Width, box.Height, box.Depth, null),
            MSB.Shape.Cylinder cylinder => new FogDsrRegionShape(FogDsrRegionShapeKind.Cylinder, null, cylinder.Height, null, cylinder.Radius),
            MSB.Shape.Sphere sphere => new FogDsrRegionShape(FogDsrRegionShapeKind.Sphere, null, null, null, sphere.Radius),
            null => throw new InvalidDataException("A pristine DSR MSB region is missing its shape."),
            _ => throw new InvalidDataException($"Unsupported pristine DSR MSB region shape type '{shape.GetType().Name}'."),
        };

        private static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> values, string description) =>
            Array.AsReadOnly((values ?? throw new ArgumentNullException(description)).ToArray());

    }
}
