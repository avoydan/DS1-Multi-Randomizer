using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FogMod;
using static FogMod.AnnotationData;

namespace FogHost
{
    public readonly record struct FogAreaId(string Value)
    {
        public override string ToString() => Value;
    }

    /// <summary>The legacy FogMod graph key emitted for a project-owned area ID.</summary>
    public readonly record struct FogModAreaName(string Value)
    {
        public override string ToString() => Value;
    }

    public readonly record struct FogItemName(string Value)
    {
        public override string ToString() => Value;
    }

    /// <summary>A FogMod map key, such as <c>depths</c> or <c>anorlondo</c>.</summary>
    public readonly record struct FogMapId(string Value)
    {
        public override string ToString() => Value;
    }

    public readonly record struct FogItemLotId(int Value);
    public readonly record struct FogSpawnPointId(int Value);
    public readonly record struct FogEntranceKey(string Value)
    {
        public override string ToString() => Value;
    }

    public readonly record struct FogGameItemId(int Category, int ItemId)
    {
        public override string ToString() => $"{Category}:{ItemId}";
    }

    public enum FogConditionKind { Atom, All, Any }
    public enum FogConditionReferenceKind { Area, KeyItem }

    public readonly record struct FogConditionReference(FogConditionReferenceKind Kind, string Value)
    {
        public static FogConditionReference Area(FogAreaId id) => new FogConditionReference(FogConditionReferenceKind.Area, id.Value);
        public static FogConditionReference KeyItem(FogItemName id) => new FogConditionReference(FogConditionReferenceKind.KeyItem, id.Value);
    }

    public sealed record FogCondition(FogConditionKind Kind, IReadOnlyList<FogConditionReference> Terms)
    {
        public string ToFogModCondition(Func<FogAreaId, FogModAreaName> resolveAreaName)
        {
            if (Terms == null || Terms.Count == 0) throw new InvalidDataException("A Fog condition must have at least one term.");
            if (resolveAreaName == null) throw new ArgumentNullException(nameof(resolveAreaName));
            string Project(FogConditionReference term) => term.Kind switch
            {
                FogConditionReferenceKind.Area => resolveAreaName(new FogAreaId(term.Value)).Value,
                FogConditionReferenceKind.KeyItem => term.Value,
                _ => throw new InvalidDataException($"Unknown Fog condition reference kind {term.Kind}.")
            };
            if (Kind == FogConditionKind.Atom)
            {
                if (Terms.Count != 1) throw new InvalidDataException("An atomic Fog condition must have exactly one term.");
                return Project(Terms[0]);
            }
            string operation = Kind == FogConditionKind.All ? "AND" : Kind == FogConditionKind.Any ? "OR" : null;
            if (operation == null) throw new InvalidDataException($"Unknown Fog condition kind {Kind}.");
            return operation + " " + string.Join(" ", Terms.Select(Project));
        }
    }

    [Flags]
    public enum FogAreaTags { None = 0, Boss = 1, Optional = 2, Trivial = 4 }

    [Flags]
    public enum FogAreaLinkTags { None = 0, Hard = 1, Shortcut = 2, Temporary = 4 }

    [Flags]
    public enum FogKeyItemTags { None = 0, Hard = 1, RandomOnly = 2 }

    [Flags]
    public enum FogEntranceTags
    {
        None = 0, Boss = 1, Door = 2, Lordvessel = 4, Major = 8,
        Pvp = 32, Unused = 128, World = 256,
    }

    [Flags]
    public enum FogWarpTags { None = 0, NoRandom = 1, Unique = 2, Unused = 4 }

    [Flags]
    public enum FogEntranceSideTags { None = 0, Dnofts = 1, Higher = 4 }

    [Flags]
    public enum FogWarpSideTags { None = 0, Player = 1, Temporary = 2 }

    /// <summary>Project runtime rules attached to a custom start but absent from FogMod's DTO.</summary>
    [Flags]
    public enum FogCustomStartSemantics { None = 0, BypassDepthsEntryKeyDoor = 1 }

    public sealed record FogAreaLink(FogAreaId Destination, FogCondition Condition, FogAreaLinkTags Tags);
    public sealed record FogArea(FogAreaId Id, FogModAreaName FogModName, IReadOnlyList<FogMapId> Maps, FogAreaTags Tags, FogAreaId? ScalingBase, IReadOnlyList<FogAreaLink> To);
    public sealed record FogKeyItem(FogItemName Name, FogGameItemId GameItemId, FogAreaId DefaultArea, FogKeyItemTags Tags);
    public sealed record FogPosition(float X, float Y, float Z, float Yaw);
    public sealed record FogTriggerVolume(float X, float Y, float Z, float Yaw, float Width, float Height, float Depth);
    public sealed record FogCustomWarp(FogMapId Map, FogPosition Position);
    public sealed record FogRespawn(FogMapId Map, FogSpawnPointId SpawnPoint);

    public sealed record FogEntranceSide(
        FogAreaId Area,
        int Flag,
        int TrapFlag,
        int EntryFlag,
        int BeforeWarpFlag,
        int BossTrigger,
        FogTriggerVolume BossTriggerArea,
        int WarpFlag,
        FogCondition Condition,
        FogCustomWarp CustomWarp,
        int CustomActionWidth,
        int ActionRegion,
        FogEntranceKey? ExcludeIfRandomized,
        FogMapId? DestinationMap,
        FogEntranceSideTags Tags);

    public sealed record FogWarpSide(
        FogAreaId Area,
        int Cutscene,
        FogCondition Condition,
        FogEntranceKey? ExcludeIfRandomized,
        FogMapId? DestinationMap,
        FogWarpSideTags Tags,
        int WarpFlag = 0);

    public sealed record FogEntrance(
        string Name,
        int Id,
        FogMapId Map,
        string Text,
        FogCondition DoorCondition,
        FogEntranceTags Tags,
        FogEntranceSide ASide,
        FogEntranceSide BSide)
    {
        public FogEntranceKey Identity => new FogEntranceKey(Name == null ? Id.ToString(CultureInfo.InvariantCulture) : Name);
    }

    public sealed record FogWarp(
        string Name,
        int Id,
        FogMapId Map,
        string Text,
        FogWarpTags Tags,
        FogWarpSide ASide,
        FogWarpSide BSide)
    {
        public FogEntranceKey Identity => new FogEntranceKey(Name == null ? Id.ToString(CultureInfo.InvariantCulture) : Name);
    }

    public sealed record FogCustomStart(
        string Id,
        string DisplayName,
        FogAreaId Area,
        FogRespawn Respawn,
        FogCustomStartSemantics Semantics = FogCustomStartSemantics.None);
    public sealed record FogDefaultCost(FogAreaId Area, float Cost);
    public sealed record FogEnemyScalingRule(FogMapId Map, string Collision, FogAreaId Area, IReadOnlyList<string> Includes);
    public sealed record FogLotLocation(FogItemLotId FirstLot, FogAreaId Area);
    public sealed record FogLotRowLocation(FogItemLotId Lot, FogAreaId Area);
    public sealed record FogDefaultFlagCollision(FogMapId Map, string Collision, int BossFlag);
    /// <summary>A pristine game object retained only for a pinned FogMod writer branch, never a graph edge.</summary>
    public sealed record FogWriterSupportEntrance(string Name, int Id, FogMapId Map, string Rationale);

    /// <summary>
    /// Project-owned semantic annotations for the pinned DS1 Fog path. Values derived from DSR are
    /// resolved by the catalog builder; this immutable projection contains only fields consumed by
    /// the active host path. It intentionally has no YAML shape, comments, ConfigAnnotation, or
    /// experimental Side.Col field.
    /// </summary>
    public sealed class FogAnnotationCatalog
    {
        public const string ProfileId = "ds1r-fog-annotations-v2";

        private readonly IReadOnlyDictionary<FogAreaId, FogModAreaName> fogModNamesByArea;

        public IReadOnlyList<FogArea> Areas { get; }
        public IReadOnlyList<FogKeyItem> KeyItems { get; }
        public IReadOnlyList<FogWarp> Warps { get; }
        public IReadOnlyList<FogEntrance> Entrances { get; }
        public IReadOnlyList<FogCustomStart> CustomStarts { get; }
        public IReadOnlyList<FogDefaultCost> DefaultCosts { get; }
        public IReadOnlyList<FogEnemyScalingRule> Enemies { get; }
        public IReadOnlyList<FogLotLocation> LotLocations { get; }
        public IReadOnlyList<FogLotRowLocation> LotRows { get; }
        public IReadOnlyList<FogDefaultFlagCollision> DefaultFlagCols { get; }
        public IReadOnlyList<FogWriterSupportEntrance> WriterSupportEntrances { get; }

        public FogAnnotationCatalog(
            IEnumerable<FogArea> areas,
            IEnumerable<FogKeyItem> keyItems,
            IEnumerable<FogWarp> warps,
            IEnumerable<FogEntrance> entrances,
            IEnumerable<FogCustomStart> customStarts,
            IEnumerable<FogDefaultCost> defaultCosts,
            IEnumerable<FogEnemyScalingRule> enemies,
            IEnumerable<FogLotLocation> lotLocations,
            IEnumerable<FogDefaultFlagCollision> defaultFlagCols,
            IEnumerable<FogWriterSupportEntrance> writerSupportEntrances = null,
            IEnumerable<FogLotRowLocation> lotRows = null)
        {
            Areas = Snapshot((areas ?? throw new ArgumentNullException(nameof(areas))).Select(area => area with
            {
                Maps = area.Maps == null ? null : Snapshot(area.Maps, "area map memberships"),
                To = area.To == null ? null : Snapshot(area.To.Select(link => link with { Condition = Snapshot(link.Condition) }), "area links"),
            }), nameof(areas));
            KeyItems = Snapshot(keyItems, nameof(keyItems));
            Warps = Snapshot((warps ?? throw new ArgumentNullException(nameof(warps))).Select(warp => Snapshot(warp)), nameof(warps));
            Entrances = Snapshot((entrances ?? throw new ArgumentNullException(nameof(entrances))).Select(entrance => Snapshot(entrance)), nameof(entrances));
            CustomStarts = Snapshot(customStarts, nameof(customStarts));
            DefaultCosts = Snapshot(defaultCosts, nameof(defaultCosts));
            Enemies = Snapshot((enemies ?? throw new ArgumentNullException(nameof(enemies))).Select(enemy => enemy with
            {
                Includes = enemy.Includes == null ? null : Snapshot(enemy.Includes, "enemy includes"),
            }), nameof(enemies));
            LotLocations = Snapshot(lotLocations, nameof(lotLocations));
            DefaultFlagCols = Snapshot(defaultFlagCols, nameof(defaultFlagCols));
            WriterSupportEntrances = Snapshot(writerSupportEntrances ?? Array.Empty<FogWriterSupportEntrance>(), nameof(writerSupportEntrances));
            LotRows = Snapshot(lotRows ?? Array.Empty<FogLotRowLocation>(), nameof(lotRows));
            _ = Unique(Areas.Select(area => area.Id.Value), "area ID");
            _ = Unique(Areas.Select(area => area.FogModName.Value), "FogMod area name");
            fogModNamesByArea = new System.Collections.ObjectModel.ReadOnlyDictionary<FogAreaId, FogModAreaName>(
                Areas.ToDictionary(area => area.Id, area => area.FogModName));
            Validate();
        }

        public Annotations ToFogModAnnotations()
        {
            return new Annotations
            {
                Areas = Areas.Select(area => new Area
                {
                    Name = ResolveFogModAreaName(area.Id).Value,
                    Tags = Tags(area.Tags),
                    ScalingBase = area.ScalingBase.HasValue ? ResolveFogModAreaName(area.ScalingBase.Value).Value : null,
                    To = area.To?.Select(link => new Side
                    {
                        Area = ResolveFogModAreaName(link.Destination).Value,
                        Cond = link.Condition?.ToFogModCondition(ResolveFogModAreaName),
                        Tags = Tags(link.Tags),
                    }).ToList(),
                }).ToList(),
                KeyItems = KeyItems.Select(item => new Item
                {
                    Name = item.Name.Value,
                    ID = item.GameItemId.ToString(),
                    Area = ResolveFogModAreaName(item.DefaultArea).Value,
                    Tags = Tags(item.Tags),
                }).ToList(),
                Warps = Warps.Select(ToFogModWarp).ToList(),
                Entrances = Entrances.Select(ToFogModEntrance)
                    .Concat(WriterSupportEntrances.Select(ToFogModSupportEntrance)).ToList(),
                CustomStarts = CustomStarts.Select(start => new CustomStart
                {
                    // FogMod uses this text in its in-game "Return to ..." message.
                    Name = start.DisplayName,
                    Area = ResolveFogModAreaName(start.Area).Value,
                    Respawn = $"{start.Respawn.Map.Value} {start.Respawn.SpawnPoint.Value.ToString(CultureInfo.InvariantCulture)}",
                }).ToList(),
                DefaultCost = DefaultCosts.ToDictionary(cost => ResolveFogModAreaName(cost.Area).Value, cost => cost.Cost, StringComparer.Ordinal),
                Enemies = Enemies.Select(enemy => new Enemies
                {
                    Col = enemy.Map.Value + (string.IsNullOrEmpty(enemy.Collision) ? "" : " " + enemy.Collision),
                    Area = ResolveFogModAreaName(enemy.Area).Value,
                    Includes = enemy.Includes?.ToList(),
                }).ToList(),
                LotLocations = LotLocations.ToDictionary(lot => lot.FirstLot.Value, lot => ResolveFogModAreaName(lot.Area).Value),
                CompactLotLocations = LotRows.Count == 0
                    ? null
                    : LotRows.ToDictionary(lot => lot.Lot.Value, lot => ResolveFogModAreaName(lot.Area).Value),
                DefaultFlagCols = DefaultFlagCols.ToDictionary(flag => flag.Map.Value + "_" + flag.Collision, flag => flag.BossFlag, StringComparer.Ordinal),
            };
        }

        /// <summary>Versioned identity for this catalog, distinct from the legacy fog.txt SHA-256.</summary>
        public string Fingerprint
        {
            get
            {
                string semantic = FogAnnotationFingerprint.Compute(ToFogModAnnotations());
                using var stream = new MemoryStream();
                using (var json = new System.Text.Json.Utf8JsonWriter(stream))
                {
                    json.WriteStartObject();
                    json.WriteString("profile", ProfileId);
                    json.WriteString("activeFogModProjection", semantic);
                    json.WriteStartArray("areas");
                    foreach (FogArea area in Areas)
                    {
                        json.WriteStartObject();
                        json.WriteString("id", area.Id.Value);
                        json.WriteStartArray("maps");
                        foreach (FogMapId map in area.Maps ?? Array.Empty<FogMapId>()) json.WriteStringValue(map.Value);
                        json.WriteEndArray();
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                    json.WriteStartArray("writerSupportEntrances");
                    foreach (FogWriterSupportEntrance entrance in WriterSupportEntrances)
                    {
                        json.WriteStartObject();
                        json.WriteString("name", entrance.Name);
                        json.WriteNumber("id", entrance.Id);
                        json.WriteString("map", entrance.Map.Value);
                        json.WriteBoolean("fixed", true);
                        json.WriteBoolean("unused", true);
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                    json.WriteStartArray("compactLotRows");
                    foreach (FogLotRowLocation lot in LotRows.OrderBy(lot => lot.Lot.Value))
                    {
                        json.WriteStartObject();
                        json.WriteNumber("id", lot.Lot.Value);
                        json.WriteString("area", ResolveFogModAreaName(lot.Area).Value);
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                    json.WriteStartArray("customStarts");
                    foreach (FogCustomStart start in CustomStarts)
                    {
                        json.WriteStartObject();
                        json.WriteString("id", start.Id);
                        json.WriteString("displayName", start.DisplayName);
                        json.WriteString("area", start.Area.Value);
                        json.WriteString("map", start.Respawn.Map.Value);
                        json.WriteNumber("spawnPoint", start.Respawn.SpawnPoint.Value);
                        json.WriteNumber("semantics", (int)start.Semantics);
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                byte[] input = stream.ToArray();
                return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            }
        }

        private void Validate()
        {
            var areaIds = Unique(Areas.Select(area => area.Id.Value), "area ID");
            var itemNames = Unique(KeyItems.Select(item => item.Name.Value), "key item name");
            Unique(KeyItems.Select(item => item.GameItemId.ToString()), "key item GameParam ID");
            Unique(CustomStarts.Select(start => start.Id), "custom start ID");
            if (CustomStarts.Any(start => string.IsNullOrWhiteSpace(start.DisplayName)))
                throw new InvalidDataException("Every custom start must have a display name for the in-game return message.");
            Unique(DefaultCosts.Select(cost => cost.Area.Value), "default scaling cost area");
            Unique(LotLocations.Select(lot => lot.FirstLot.Value.ToString(CultureInfo.InvariantCulture)), "ItemLot start ID");
            Unique(LotRows.Select(lot => lot.Lot.Value.ToString(CultureInfo.InvariantCulture)), "exact ItemLot row ID");
            Unique(DefaultFlagCols.Select(flag => flag.Map.Value + "_" + flag.Collision), "default flag collision");
            if (KeyItems.Any(item => item.GameItemId.ItemId <= 0)) throw new InvalidDataException("A key item GameParam ID must be positive.");
            if (LotLocations.Any(lot => lot.FirstLot.Value <= 0)) throw new InvalidDataException("An ItemLot start ID must be positive.");
            if (LotRows.Any(lot => lot.Lot.Value <= 0)) throw new InvalidDataException("An exact ItemLot row ID must be positive.");
            if (LotRows.Any(lot => !areaIds.Contains(lot.Area.Value))) throw new InvalidDataException("An exact ItemLot row references an unknown project Area.");
            if (DefaultCosts.Any(cost => !float.IsFinite(cost.Cost))) throw new InvalidDataException("Default scaling costs must be finite numbers.");

            var entranceIds = new HashSet<FogEntranceKey>();
            foreach (FogWarp warp in Warps)
            {
                if (warp.Name != null && string.IsNullOrWhiteSpace(warp.Name)) throw new InvalidDataException("A warp name cannot be empty.");
                if (!entranceIds.Add(warp.Identity)) throw new InvalidDataException($"Duplicate Fog entrance identity '{warp.Identity}'.");
                ValidateMap(warp.Map);
                bool unused = warp.Tags.HasFlag(FogWarpTags.Unused);
                if (!unused && (warp.ASide == null || warp.BSide == null)) throw new InvalidDataException($"Warp '{warp.Identity}' must have two sides.");
                ValidateWarpSide(warp.ASide, areaIds, itemNames);
                ValidateWarpSide(warp.BSide, areaIds, itemNames);
            }
            foreach (FogEntrance entrance in Entrances)
            {
                if (entrance.Name != null && string.IsNullOrWhiteSpace(entrance.Name)) throw new InvalidDataException("An entrance name cannot be empty.");
                if (!entranceIds.Add(entrance.Identity)) throw new InvalidDataException($"Duplicate Fog entrance identity '{entrance.Identity}'.");
                ValidateMap(entrance.Map);
                ValidateCondition(entrance.DoorCondition, areaIds, itemNames);
                ValidateSide(entrance.ASide, areaIds, itemNames);
                ValidateSide(entrance.BSide, areaIds, itemNames);
                int sideCount = (entrance.ASide == null ? 0 : 1) + (entrance.BSide == null ? 0 : 1);
                bool unused = entrance.Tags.HasFlag(FogEntranceTags.Unused);
                if (!unused && sideCount < 2) throw new InvalidDataException($"Entrance '{entrance.Identity}' has fewer than two sides.");
            }
            foreach (FogWriterSupportEntrance entrance in WriterSupportEntrances)
            {
                if (string.IsNullOrWhiteSpace(entrance.Name)) throw new InvalidDataException("A writer-support Entrance requires a pristine object name.");
                if (!entranceIds.Add(new FogEntranceKey(entrance.Name))) throw new InvalidDataException($"Duplicate Fog writer-support entrance identity '{entrance.Name}'.");
                ValidateMap(entrance.Map);
                if (entrance.Id <= 0) throw new InvalidDataException($"Writer-support Entrance '{entrance.Name}' must have a positive pristine entity ID.");
                if (string.IsNullOrWhiteSpace(entrance.Rationale)) throw new InvalidDataException($"Writer-support Entrance '{entrance.Name}' must state its project rationale.");
            }

            foreach (FogArea area in Areas)
            {
                if (area.Maps == null || area.Maps.Count == 0) throw new InvalidDataException($"Area '{area.Id}' must declare at least one supported DSR map.");
                foreach (FogMapId map in area.Maps) ValidateMap(map);
                if (area.ScalingBase.HasValue) RequireArea(area.ScalingBase.Value, areaIds, $"scaling base of {area.Id}");
                foreach (FogAreaLink link in area.To ?? Array.Empty<FogAreaLink>())
                {
                    RequireArea(link.Destination, areaIds, $"destination from {area.Id}");
                    ValidateCondition(link.Condition, areaIds, itemNames);
                }
            }
            foreach (FogKeyItem item in KeyItems) RequireArea(item.DefaultArea, areaIds, $"default area of {item.Name}");
            foreach (FogCustomStart start in CustomStarts)
            {
                RequireArea(start.Area, areaIds, $"start {start.Id}");
                ValidateMap(start.Respawn.Map);
                if (start.Respawn.SpawnPoint.Value <= 0) throw new InvalidDataException($"Start '{start.Id}' has an invalid spawn-point ID.");
                if ((start.Semantics & ~FogCustomStartSemantics.BypassDepthsEntryKeyDoor) != FogCustomStartSemantics.None)
                    throw new InvalidDataException($"Start '{start.Id}' has unknown custom-start semantics.");
                if (start.Semantics.HasFlag(FogCustomStartSemantics.BypassDepthsEntryKeyDoor)
                    && (start.Area.Value != "depths" || ResolveFogModAreaName(start.Area).Value != "depths" || start.Respawn.Map.Value != "depths"))
                    throw new InvalidDataException($"Start '{start.Id}' can bypass the Depths entry key only when its project area, FogMod area key, and spawn map are all 'depths'.");
            }
            foreach (FogDefaultCost cost in DefaultCosts) RequireArea(cost.Area, areaIds, "default scaling cost");
            foreach (FogEnemyScalingRule enemy in Enemies)
            {
                ValidateMap(enemy.Map);
                RequireArea(enemy.Area, areaIds, $"enemy scaling rule for {enemy.Map}");
            }
            foreach (FogLotLocation lot in LotLocations) RequireArea(lot.Area, areaIds, $"lot {lot.FirstLot.Value}");
            foreach (FogDefaultFlagCollision flag in DefaultFlagCols) ValidateMap(flag.Map);

            var allNames = new HashSet<FogEntranceKey>(Warps.Select(warp => warp.Identity).Concat(Entrances.Select(entrance => entrance.Identity)));
            foreach (FogEntranceSide side in Entrances.SelectMany(entrance => new[] { entrance.ASide, entrance.BSide }).Where(side => side != null))
            {
                if (side.ExcludeIfRandomized.HasValue && !allNames.Contains(side.ExcludeIfRandomized.Value))
                    throw new InvalidDataException($"An entrance side excludes unknown entrance '{side.ExcludeIfRandomized}'.");
            }
            foreach (FogWarpSide side in Warps.SelectMany(warp => new[] { warp.ASide, warp.BSide }).Where(side => side != null))
            {
                if (side.ExcludeIfRandomized.HasValue && !allNames.Contains(side.ExcludeIfRandomized.Value))
                    throw new InvalidDataException($"A warp side excludes unknown entrance '{side.ExcludeIfRandomized}'.");
            }
        }

        private static void ValidateWarpSide(FogWarpSide side, HashSet<string> areaIds, HashSet<string> itemNames)
        {
            if (side == null) return;
            RequireArea(side.Area, areaIds, "warp side");
            if (side.WarpFlag < 0) throw new InvalidDataException($"Warp side '{side.Area}' has a negative activation flag.");
            ValidateCondition(side.Condition, areaIds, itemNames);
            if (side.DestinationMap.HasValue) ValidateMap(side.DestinationMap.Value);
        }

        private static void ValidateSide(FogEntranceSide side, HashSet<string> areaIds, HashSet<string> itemNames)
        {
            if (side == null) return;
            RequireArea(side.Area, areaIds, "entrance side");
            ValidateCondition(side.Condition, areaIds, itemNames);
            if (side.DestinationMap.HasValue) ValidateMap(side.DestinationMap.Value);
            if (side.CustomWarp != null) ValidateMap(side.CustomWarp.Map);
        }

        private static void ValidateCondition(FogCondition condition, HashSet<string> areaIds, HashSet<string> itemNames)
        {
            if (condition == null) return;
            if (condition.Terms == null || condition.Terms.Count == 0)
                throw new InvalidDataException("A Fog condition must have at least one term.");
            if (condition.Kind == FogConditionKind.Atom && condition.Terms.Count != 1)
                throw new InvalidDataException("An atomic Fog condition must have exactly one term.");
            if (condition.Kind != FogConditionKind.Atom && condition.Kind != FogConditionKind.All && condition.Kind != FogConditionKind.Any)
                throw new InvalidDataException($"Unknown Fog condition kind {condition.Kind}.");
            foreach (FogConditionReference term in condition.Terms)
            {
                if (term.Kind == FogConditionReferenceKind.Area) RequireArea(new FogAreaId(term.Value), areaIds, "condition");
                else if (term.Kind == FogConditionReferenceKind.KeyItem && !itemNames.Contains(term.Value))
                    throw new InvalidDataException($"Condition references unknown key item '{term.Value}'.");
                else if (term.Kind != FogConditionReferenceKind.KeyItem)
                    throw new InvalidDataException($"Unknown Fog condition reference kind {term.Kind}.");
            }
        }

        private static void ValidateMap(FogMapId map)
        {
            if (string.IsNullOrWhiteSpace(map.Value) || !AnnotationData.nameSpecs.ContainsKey(map.Value))
                throw new InvalidDataException($"Unknown Fog map key '{map.Value}'.");
        }

        private static void RequireArea(FogAreaId area, HashSet<string> areaIds, string context)
        {
            if (!areaIds.Contains(area.Value)) throw new InvalidDataException($"Unknown area '{area.Value}' in {context}.");
        }

        private static HashSet<string> Unique(IEnumerable<string> values, string description)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"A {description} is empty.");
                if (!result.Add(value)) throw new InvalidDataException($"Duplicate {description} '{value}'.");
            }
            return result;
        }

        private static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> source, string name)
        {
            if (source == null) throw new ArgumentNullException(name);
            return Array.AsReadOnly(source.ToArray());
        }

        private static FogCondition Snapshot(FogCondition condition) => condition == null
            ? null
            : condition with { Terms = condition.Terms == null ? null : Snapshot(condition.Terms, "condition terms") };

        private static FogEntrance Snapshot(FogEntrance entrance) => entrance with
        {
            DoorCondition = Snapshot(entrance.DoorCondition),
            ASide = Snapshot(entrance.ASide),
            BSide = Snapshot(entrance.BSide),
        };

        private static FogEntranceSide Snapshot(FogEntranceSide side) => side == null
            ? null
            : side with { Condition = Snapshot(side.Condition) };

        private static FogWarp Snapshot(FogWarp warp) => warp with
        {
            ASide = Snapshot(warp.ASide),
            BSide = Snapshot(warp.BSide),
        };

        private static FogWarpSide Snapshot(FogWarpSide side) => side == null
            ? null
            : side with { Condition = Snapshot(side.Condition) };

        private FogModAreaName ResolveFogModAreaName(FogAreaId area)
        {
            if (fogModNamesByArea.TryGetValue(area, out FogModAreaName name)) return name;
            throw new InvalidDataException($"Unknown project area ID '{area}' in FogMod projection.");
        }

        private Entrance ToFogModEntrance(FogEntrance entrance) => new Entrance
        {
            Name = entrance.Name,
            ID = entrance.Id,
            Area = entrance.Map.Value,
            Text = entrance.Text,
            DoorCond = entrance.DoorCondition?.ToFogModCondition(ResolveFogModAreaName),
            Tags = Tags(entrance.Tags),
            ASide = ToFogModSide(entrance.ASide),
            BSide = ToFogModSide(entrance.BSide),
        };

        private static Entrance ToFogModSupportEntrance(FogWriterSupportEntrance entrance) => new Entrance
        {
            Name = entrance.Name,
            ID = entrance.Id,
            Area = entrance.Map.Value,
            Text = "Compact writer support anchor",
            Tags = "unused",
            IsFixed = true,
        };

        private Entrance ToFogModWarp(FogWarp warp) => new Entrance
        {
            Name = warp.Name,
            ID = warp.Id,
            Area = warp.Map.Value,
            Text = warp.Text,
            Tags = Tags(warp.Tags),
            ASide = ToFogModWarpSide(warp.ASide),
            BSide = ToFogModWarpSide(warp.BSide),
        };

        private Side ToFogModWarpSide(FogWarpSide side)
        {
            if (side == null) return null;
            return new Side
            {
                Area = ResolveFogModAreaName(side.Area).Value,
                WarpFlag = side.WarpFlag,
                Cutscene = side.Cutscene,
                Cond = side.Condition?.ToFogModCondition(ResolveFogModAreaName),
                ExcludeIfRandomized = side.ExcludeIfRandomized?.Value,
                DestinationMap = side.DestinationMap?.Value,
                Tags = Tags(side.Tags),
            };
        }

        private Side ToFogModSide(FogEntranceSide side)
        {
            if (side == null) return null;
            return new Side
            {
                Area = ResolveFogModAreaName(side.Area).Value,
                Flag = side.Flag,
                TrapFlag = side.TrapFlag,
                EntryFlag = side.EntryFlag,
                BeforeWarpFlag = side.BeforeWarpFlag,
                BossTrigger = side.BossTrigger,
                BossTriggerArea = side.BossTriggerArea == null ? null : string.Join(" ", new[]
                {
                    side.BossTriggerArea.X, side.BossTriggerArea.Y, side.BossTriggerArea.Z,
                    side.BossTriggerArea.Yaw, side.BossTriggerArea.Width, side.BossTriggerArea.Height, side.BossTriggerArea.Depth,
                }.Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
                WarpFlag = side.WarpFlag,
                Cond = side.Condition?.ToFogModCondition(ResolveFogModAreaName),
                CustomWarp = side.CustomWarp == null ? null : side.CustomWarp.Map.Value + " " + string.Join(" ", new[]
                {
                    side.CustomWarp.Position.X, side.CustomWarp.Position.Y, side.CustomWarp.Position.Z, side.CustomWarp.Position.Yaw,
                }.Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
                CustomActionWidth = side.CustomActionWidth,
                ActionRegion = side.ActionRegion,
                ExcludeIfRandomized = side.ExcludeIfRandomized?.Value,
                DestinationMap = side.DestinationMap?.Value,
                Tags = Tags(side.Tags),
            };
        }

        private static string Tags(FogAreaTags tags) => JoinTags(tags,
            (FogAreaTags.Boss, "boss"), (FogAreaTags.Optional, "optional"), (FogAreaTags.Trivial, "trivial"));

        private static string Tags(FogAreaLinkTags tags) => JoinTags(tags,
            (FogAreaLinkTags.Hard, "hard"), (FogAreaLinkTags.Shortcut, "shortcut"), (FogAreaLinkTags.Temporary, "temp"));

        private static string Tags(FogKeyItemTags tags) => JoinTags(tags,
            (FogKeyItemTags.Hard, "hard"), (FogKeyItemTags.RandomOnly, "randomonly"));

        private static string Tags(FogEntranceTags tags) => JoinTags(tags,
            (FogEntranceTags.Boss, "boss"), (FogEntranceTags.Door, "door"), (FogEntranceTags.Lordvessel, "lordvessel"),
            (FogEntranceTags.Major, "major"), (FogEntranceTags.Pvp, "pvp"),
            (FogEntranceTags.Unused, "unused"), (FogEntranceTags.World, "world"));

        private static string Tags(FogWarpTags tags) => JoinTags(tags,
            (FogWarpTags.NoRandom, "norandom"), (FogWarpTags.Unique, "unique"), (FogWarpTags.Unused, "unused"));

        private static string Tags(FogEntranceSideTags tags) => JoinTags(tags,
            (FogEntranceSideTags.Dnofts, "dnofts"), (FogEntranceSideTags.Higher, "higher"));

        private static string Tags(FogWarpSideTags tags) => JoinTags(tags,
            (FogWarpSideTags.Player, "player"), (FogWarpSideTags.Temporary, "temp"));

        private static string JoinTags<T>(T flags, params (T Flag, string Name)[] known) where T : struct, Enum
        {
            Enum actual = (Enum)(object)flags;
            string[] values = known.Where(tag => actual.HasFlag((Enum)(object)tag.Flag))
                .Select(tag => tag.Name).ToArray();
            return string.Join(" ", values);
        }
    }
}
