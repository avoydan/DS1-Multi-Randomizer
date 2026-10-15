using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.Text.Json;
using FogMod;
using static FogMod.AnnotationData;

namespace FogHost
{
    /// <summary>
    /// Development identity for the active DS1 annotation projection. It deliberately ignores
    /// YAML formatting and fields the pinned runtime does not consume. This is an oracle-comparison
    /// format; the built-in catalog will use its own profile/version identity.
    /// </summary>
    public static class FogAnnotationFingerprint
    {
        public const string Format = "ds1tr-fog-consumed-annotations-2";

        private static readonly HashSet<string> AreaTags = new HashSet<string>(StringComparer.Ordinal)
            { "boss", "optional", "trivial" };
        private static readonly HashSet<string> ItemTags = new HashSet<string>(StringComparer.Ordinal)
            { "hard", "randomonly" };
        private static readonly HashSet<string> EntranceTags = new HashSet<string>(StringComparer.Ordinal)
            { "boss", "door", "lordvessel", "major", "pvp", "unused", "world" };
        private static readonly HashSet<string> AreaLinkTags = new HashSet<string>(StringComparer.Ordinal)
            { "hard", "shortcut", "temp" };
        private static readonly HashSet<string> EntranceSideTags = new HashSet<string>(StringComparer.Ordinal)
            { "higher" };
        private static readonly HashSet<string> DoorEntranceSideTags = new HashSet<string>(StringComparer.Ordinal)
            { "dnofts" };
        private static readonly HashSet<string> WarpTags = new HashSet<string>(StringComparer.Ordinal)
            { "norandom", "unique", "unused" };
        private static readonly HashSet<string> WarpA_SideTags = new HashSet<string>(StringComparer.Ordinal)
            { "temp" };
        private static readonly HashSet<string> WarpB_SideTags = new HashSet<string>(StringComparer.Ordinal)
            { "player" };

        public static string Compute(Annotations annotations)
        {
            if (annotations == null) throw new ArgumentNullException(nameof(annotations));
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteString("format", Format);

                json.WriteStartArray("areas");
                foreach (Area area in Required(annotations.Areas, nameof(annotations.Areas)))
                {
                    json.WriteStartObject();
                    json.WriteString("name", area.Name);
                    WriteTags(json, "tags", area.Tags, AreaTags);
                    WriteNullableString(json, "scalingBase", area.ScalingBase);
                    json.WriteStartArray("to");
                    foreach (Side side in area.To ?? new List<Side>())
                    {
                        json.WriteStartObject();
                        json.WriteString("area", side.Area);
                        // FogMod's condition parser splits on literal spaces; preserve the exact
                        // source string because whitespace and operand order can affect behavior.
                        WriteNullableString(json, "condition", side.Cond);
                        WriteTags(json, "tags", side.Tags, AreaLinkTags);
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                json.WriteEndArray();

                json.WriteStartArray("keyItems");
                foreach (Item item in Required(annotations.KeyItems, nameof(annotations.KeyItems)))
                {
                    json.WriteStartObject();
                    json.WriteString("name", item.Name);
                    json.WriteString("id", item.ID);
                    json.WriteString("area", item.Area);
                    WriteTags(json, "tags", item.Tags, ItemTags);
                    json.WriteEndObject();
                }
                json.WriteEndArray();

                WriteEntrances(json, "warps", Required(annotations.Warps, nameof(annotations.Warps)), isWarp: true);
                WriteEntrances(json, "entrances", Required(annotations.Entrances, nameof(annotations.Entrances)), isWarp: false);

                json.WriteStartArray("customStarts");
                foreach (CustomStart start in Required(annotations.CustomStarts, nameof(annotations.CustomStarts)))
                {
                    json.WriteStartObject();
                    json.WriteString("name", start.Name);
                    json.WriteString("area", start.Area);
                    json.WriteString("respawn", start.Respawn);
                    json.WriteEndObject();
                }
                json.WriteEndArray();

                json.WriteStartArray("defaultCost");
                foreach (KeyValuePair<string, float> cost in Required(annotations.DefaultCost, nameof(annotations.DefaultCost)).OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    json.WriteStartArray();
                    json.WriteStringValue(cost.Key);
                    json.WriteNumberValue(cost.Value);
                    json.WriteEndArray();
                }
                json.WriteEndArray();

                json.WriteStartArray("enemies");
                foreach (Enemies enemy in Required(annotations.Enemies, nameof(annotations.Enemies)))
                {
                    json.WriteStartObject();
                    json.WriteString("collision", enemy.Col);
                    json.WriteString("area", enemy.Area);
                    json.WriteStartArray("includes");
                    if (enemy.Includes == null) json.WriteNullValue();
                    else foreach (string include in enemy.Includes) json.WriteStringValue(include);
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                json.WriteEndArray();

                json.WriteStartArray("lotLocations");
                foreach (KeyValuePair<int, string> lot in Required(annotations.LotLocations, nameof(annotations.LotLocations)))
                {
                    json.WriteStartArray();
                    json.WriteNumberValue(lot.Key);
                    json.WriteStringValue(lot.Value);
                    json.WriteEndArray();
                }
                json.WriteEndArray();

                json.WriteStartArray("defaultFlagCols");
                foreach (KeyValuePair<string, int> entry in Required(annotations.DefaultFlagCols, nameof(annotations.DefaultFlagCols)).OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    json.WriteStartArray();
                    json.WriteStringValue(entry.Key);
                    json.WriteNumberValue(entry.Value);
                    json.WriteEndArray();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
        }

        /// <summary>Per-category semantic identities for development-only oracle comparison.</summary>
        public static IReadOnlyDictionary<string, string> ComputeCategories(Annotations annotations)
        {
            if (annotations == null) throw new ArgumentNullException(nameof(annotations));
            var categories = new Dictionary<string, string>(StringComparer.Ordinal);
            Add("Areas", value => value.Areas = Required(annotations.Areas, nameof(annotations.Areas)));
            Add("KeyItems", value => value.KeyItems = Required(annotations.KeyItems, nameof(annotations.KeyItems)));
            Add("Warps", value => value.Warps = Required(annotations.Warps, nameof(annotations.Warps)));
            Add("Entrances", value => value.Entrances = Required(annotations.Entrances, nameof(annotations.Entrances)));
            Add("CustomStarts", value => value.CustomStarts = Required(annotations.CustomStarts, nameof(annotations.CustomStarts)));
            Add("DefaultCost", value => value.DefaultCost = Required(annotations.DefaultCost, nameof(annotations.DefaultCost)));
            Add("Enemies", value => value.Enemies = Required(annotations.Enemies, nameof(annotations.Enemies)));
            Add("LotLocations", value => value.LotLocations = Required(annotations.LotLocations, nameof(annotations.LotLocations)));
            Add("DefaultFlagCols", value => value.DefaultFlagCols = Required(annotations.DefaultFlagCols, nameof(annotations.DefaultFlagCols)));
            return new ReadOnlyDictionary<string, string>(categories);

            void Add(string name, Action<Annotations> set)
            {
                Annotations isolated = new Annotations
                {
                    Areas = new List<Area>(), KeyItems = new List<Item>(), Warps = new List<Entrance>(),
                    Entrances = new List<Entrance>(), CustomStarts = new List<CustomStart>(),
                    DefaultCost = new Dictionary<string, float>(StringComparer.Ordinal), Enemies = new List<Enemies>(),
                    LotLocations = new Dictionary<int, string>(), DefaultFlagCols = new Dictionary<string, int>(StringComparer.Ordinal),
                };
                set(isolated);
                categories.Add(name, Compute(isolated));
            }
        }

        private static void WriteEntrances(Utf8JsonWriter json, string property, IEnumerable<Entrance> entrances, bool isWarp)
        {
            json.WriteStartArray(property);
            foreach (Entrance entrance in entrances)
            {
                json.WriteStartObject();
                WriteNullableString(json, "name", entrance.Name);
                json.WriteNumber("id", entrance.ID);
                WriteNullableString(json, "map", entrance.Area);
                WriteNullableString(json, "text", entrance.Text);
                if (isWarp) WriteTags(json, "tags", entrance.Tags, WarpTags);
                else
                {
                    bool canBeDoor = entrance.HasTag("door") || entrance.HasTag("lordvessel");
                    WriteNullableString(json, "doorCondition", canBeDoor ? entrance.DoorCond : null);
                    WriteTags(json, "tags", entrance.Tags, EntranceTags);
                }
                bool mayBeDoor = !isWarp && (entrance.HasTag("door") || entrance.HasTag("lordvessel"));
                bool canBeOrdinary = !isWarp && !entrance.HasTag("door");
                WriteSide(json, "a", entrance.ASide, isWarp, mayBeDoor, canBeOrdinary, isSideA: true);
                WriteSide(json, "b", entrance.BSide, isWarp, mayBeDoor, canBeOrdinary, isSideA: false);
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }

        private static void WriteSide(Utf8JsonWriter json, string property, Side side, bool isWarp, bool mayBeDoor, bool canBeOrdinary, bool isSideA)
        {
            json.WritePropertyName(property);
            if (side == null)
            {
                json.WriteNullValue();
                return;
            }
            json.WriteStartObject();
            json.WriteString("area", side.Area);
            WriteNullableString(json, "condition", side.Cond);
            WriteNullableString(json, "excludeIfRandomized", side.ExcludeIfRandomized);
            if (isWarp)
            {
                WriteNullableString(json, "destinationMap", side.DestinationMap);
                json.WriteNumber("cutscene", side.Cutscene);
                if (side.WarpFlag != 0) json.WriteNumber("warpFlag", side.WarpFlag);
                WriteTags(json, "tags", side.Tags, isSideA ? WarpA_SideTags : WarpB_SideTags);
            }
            else if (canBeOrdinary)
            {
                WriteNullableString(json, "destinationMap", side.DestinationMap);
                json.WriteNumber("flag", side.Flag);
                json.WriteNumber("trapFlag", side.TrapFlag);
                json.WriteNumber("entryFlag", side.EntryFlag);
                json.WriteNumber("beforeWarpFlag", side.BeforeWarpFlag);
                json.WriteNumber("bossTrigger", side.BossTrigger);
                WriteNullableString(json, "bossTriggerArea", side.BossTrigger == 0 ? null : CanonicalFloatVector(side.BossTriggerArea));
                json.WriteNumber("warpFlag", side.WarpFlag);
                WriteNullableString(json, "customWarp", CanonicalCustomWarp(side.CustomWarp));
                json.WriteNumber("customActionWidth", side.ActionRegion == 0 ? side.CustomActionWidth : 0);
                json.WriteNumber("actionRegion", side.ActionRegion);
                if (mayBeDoor) WriteTags(json, "doorTags", side.Tags, DoorEntranceSideTags);
                WriteTags(json, "ordinaryTags", side.Tags, EntranceSideTags);
            }
            else if (mayBeDoor) WriteTags(json, "doorTags", side.Tags, DoorEntranceSideTags);
            json.WriteEndObject();
        }

        private static string CanonicalCustomWarp(string value)
        {
            if (value == null) return null;
            string[] parts = value.Split(' ');
            if (parts.Length < 2) return value;
            string rawVector = string.Join(" ", parts.Skip(1));
            string canonicalVector = CanonicalFloatVector(rawVector);
            return canonicalVector == rawVector ? value : parts[0] + " " + canonicalVector;
        }

        private static string CanonicalFloatVector(string value)
        {
            if (value == null) return null;
            try
            {
                float[] values = value.Split(' ')
                    .Select(part => float.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                return string.Join(" ", values.Select(number => number.ToString("R", CultureInfo.InvariantCulture)));
            }
            catch (FormatException)
            {
                // Invalid spacing/values can change whether generation succeeds; retain them exactly.
                return value;
            }
        }

        private static void WriteTags(Utf8JsonWriter json, string property, string tags, HashSet<string> active)
        {
            json.WriteStartArray(property);
            if (tags != null)
            {
                foreach (string tag in tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(active.Contains).Distinct(StringComparer.Ordinal).OrderBy(tag => tag, StringComparer.Ordinal))
                    json.WriteStringValue(tag);
            }
            json.WriteEndArray();
        }

        private static void WriteNullableString(Utf8JsonWriter json, string property, string value)
        {
            if (value == null) json.WriteNull(property);
            else json.WriteString(property, value);
        }

        private static T Required<T>(T value, string name) where T : class =>
            value ?? throw new InvalidDataException($"The annotation field {name} is missing.");
    }
}
