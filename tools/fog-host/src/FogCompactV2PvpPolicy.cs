using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FogMod;
using SoulsFormats;
using static FogMod.AnnotationData;

namespace FogHost
{
    /// <summary>
    /// Project-owned PvP gate identities and Compact V2 projection policy. The identity census was
    /// checked against pristine DSR MSBs and the pinned FogMod annotation oracle; no oracle data is
    /// loaded at runtime. The 33 non-door gates are modeled in the V2 graph. PvP door records stay
    /// fixed in the project graph and receive the same solo-mode initializer as the upstream writer.
    /// </summary>
    public static class FogCompactV2PvpPolicy
    {
        public sealed record GateIdentity(
            string Name,
            int EntityId,
            FogMapId Map,
            string ASideArea,
            string BSideArea,
            bool Major,
            bool Door = false,
            bool Unused = false);

        private static readonly IReadOnlyList<GateIdentity> identities = Array.AsReadOnly(new[]
        {
            Gate("o1401_0000", 1001994, "depths", "burg_lower", "depths", false, door: true),
            Gate("o1402_0000", 1001996, "depths", "blighttown_upper", "depths", false, door: true),
            Gate("o1407_0000", 1011994, "parish", "firelink", "burg_start", true),
            Gate("o1408_0000", 1011996, "parish", "parish_andre", "parish_church", true),
            Gate("o1409_0000", 1011998, "parish", "parish_hellkite", "burg_posttaurus", false),
            Gate("o1410_0000", 1011988, "parish", "firelink_elevator", "parish_church", true),
            Gate("o1411_0000", 1011986, "parish", "burg_upper", "darkroot", false, door: true),
            Gate("o1412_0000", 1011984, "parish", "parish_hellkite", "burg_ladder", false, door: true),
            Gate("o1414_0000", 1011976, "parish", "burg_lower", "firelink_gate", true),
            Gate("o1415_0000", 1011982, "parish", "burg_lower", "burg_ladder", true),
            Gate("o1417_0000", 1011980, "parish", "burg_upper", "burg_lower", false, door: true),
            Gate("o1418_0000", 1011978, "parish", "burg_bonfire", "burg_upper", false),
            Gate("o1421_0000", 1011974, "parish", "parish_hellkite", "burg_above_bonfire", true),
            Gate("o1421_0001", 1011972, "parish", "burg_lower", "depths", false, door: true),
            Gate("o2901_0000", 1201994, "darkroot", "parish_andre", "darkroot", true),
            Gate("o2902_0000", 1201996, "darkroot", "darkroot", "burg_upper", false, door: true),
            Gate("o2903_0000", 1201998, "darkroot", "darkroot", "newlondo_drakes", true),
            Gate("o2907_0000", 1201988, "darkroot", "darkroot", "darkroot_forest", false),
            Gate("o2908_0000", 1201986, "darkroot", "darkroot_forest", "darkroot", false, door: true),
            Gate("o2912_0001", 1211988, "dlc", "dlc_township", "dlc_bottom", false),
            Gate("o2913", 1211994, "dlc", "dlc_forest", "dlc_sanctuary", false),
            Gate("o2914", 1211996, "dlc", "dlc_forest", "dlc_top", false),
            Gate("o2917_0000", 1211998, "dlc", "dlc_hallway", "dlc_township", false),
            Gate("o2920_0000", 1211986, "dlc", "dlc_postartorias", "dlc_hallway", false),
            Gate("o3951_0000", 1301994, "catacombs", "firelink", "catacombs_start", true),
            Gate("o3961_0000", 1311994, "totg", "totg", "catacombs_pinwheel", true),
            Gate("o3970_0000", 1321994, "greathollow", "greathollow", "greathollow_start", false),
            Gate("o4952_0000", 1401994, "blighttown", "blighttown_upper", "depths", false, door: true),
            Gate("o4953_0000", 1401996, "blighttown", "newlondo_drakes", "blighttown", true),
            Gate("o4954_0000", 1401998, "blighttown", "greathollow_start", "blighttown", true),
            Gate("o4966_0000", 1411994, "demonruins", "blighttown_bell", "demonruins", true),
            Gate("o4967_0000", 1411996, "demonruins", "demonruins_elevator", "demonruins_stairs", false),
            Gate("o4968_0000", 1411998, "demonruins", "demonruins_lower", "demonruins", false),
            Gate("o4971_0000", 1411984, "demonruins", "demonruins_lordvessel", "demonruins_izalith", false, door: true),
            Gate("o4973_0000", 1411982, "demonruins", "", "", false, unused: true),
            Gate("o5151_0000", 1501994, "sens", "parish_andre", "sens", false),
            Gate("o5863_0000", 1511994, "anorlondo", "anorlondo_prerafters", "anorlondo_start", false),
            Gate("o5869_0000", 1511754, "anorlondo", "anorlondo_cathedral", "anorlondo_front", false),
            Gate("o5870_0000", 1511750, "anorlondo", "anorlondo_cathedral", "anorlondo_building", false),
            Gate("o5871_0000", 1511752, "anorlondo", "anorlondo_blacksmith", "anorlondo_cathedral", false),
            Gate("o6601_0000", 1601994, "newlondo", "newlondo", "firelink", true),
            Gate("o6602_0000", 1601996, "newlondo", "newlondo", "newlondo_drakes", false, door: true),
            Gate("o6603_0000", 1601998, "newlondo", "newlondo_drakes", "newlondo_lower", false, door: true),
            Gate("o7902_0000", 1701994, "dukes", "dukes_library", "dukes_start", false),
            Gate("o7908_0000", 1701996, "dukes", "dukes_library", "dukes_hallway", false),
            Gate("o8051_0000", 1801994, "kiln", "kiln_stairs", "kiln", true),
        });

        public static IReadOnlyList<GateIdentity> Identities => identities;
        public static IReadOnlyList<GateIdentity> GraphEntrances => Array.AsReadOnly(identities.Where(gate => !gate.Door && !gate.Unused).ToArray());
        public static IReadOnlyList<GateIdentity> FixedDoors => Array.AsReadOnly(identities.Where(gate => gate.Door).ToArray());

        /// <summary>
        /// Keep the legacy Compact V2 topology unchanged when PvP gate randomization is off.
        /// Solo opening is provided by Event 5700 initializers in that mode. Only a gate category
        /// explicitly enabled by the user becomes a graph entrance and replaces its parallel
        /// static Area.To route.
        /// </summary>
        public static void ApplyGraphOptions(Annotations annotations, bool majorPvpFogs, bool minorPvpFogs)
        {
            if (annotations == null) throw new ArgumentNullException(nameof(annotations));

            GateIdentity[] active = GraphEntrances
                .Where(gate => gate.Major ? majorPvpFogs : minorPvpFogs)
                .ToArray();
            HashSet<string> activeNames = active.Select(gate => gate.Name).ToHashSet(StringComparer.Ordinal);
            HashSet<string> graphGateNames = GraphEntrances.Select(gate => gate.Name).ToHashSet(StringComparer.Ordinal);
            annotations.Entrances.RemoveAll(entrance => graphGateNames.Contains(entrance.Name)
                && !activeNames.Contains(entrance.Name));

            // The pinned GameDataWriter unconditionally indexes o5869_0000 to choose the
            // vanilla Anor Londo broken-window transform. The o4954_0000 and o3970_0000 sides
            // reference each other through ExcludeIfRandomized, so an option-scoped graph may
            // remove one while the other remains active. Retain inactive referenced identities
            // only as fixed, unused lookup anchors: they add no graph edge or shuffle input.
            foreach (string anchorName in new[] { "o5869_0000", "o3970_0000", "o4954_0000" })
            {
                if (activeNames.Contains(anchorName)) continue;
                GateIdentity anchor = identities.Single(gate => gate.Name == anchorName);
                annotations.Entrances.Add(new Entrance
                {
                    Name = anchor.Name,
                    ID = anchor.EntityId,
                    Area = anchor.Map.Value,
                    Text = "Compact writer support anchor",
                    Tags = "unused",
                    IsFixed = true,
                });
            }

            HashSet<string> activeAreaPairs = active.Select(gate => CanonicalAreaPair(gate.ASideArea, gate.BSideArea))
                .ToHashSet(StringComparer.Ordinal);
            foreach (Area area in annotations.Areas)
            {
                area.To?.RemoveAll(link => activeAreaPairs.Contains(CanonicalAreaPair(area.Name, link.Area)));
            }
        }

        private static string CanonicalAreaPair(string first, string second) =>
            string.CompareOrdinal(first, second) < 0 ? first + "|" + second : second + "|" + first;

        public static IReadOnlyList<FogEntrance> BuildGraphEntrances(FogDsrFacts facts, FogAnnotationCatalog projectCatalog)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (projectCatalog == null) throw new ArgumentNullException(nameof(projectCatalog));
            VerifyPhysicalIdentities(facts);

            HashSet<FogAreaId> areas = projectCatalog.Areas.Select(area => area.Id).ToHashSet();
            foreach (GateIdentity gate in GraphEntrances)
            {
                if (!areas.Contains(new FogAreaId(gate.ASideArea)) || !areas.Contains(new FogAreaId(gate.BSideArea)))
                    throw new InvalidDataException($"Compact PvP gate '{gate.Name}' names an Area outside the project-owned Area catalog.");
            }

            return Array.AsReadOnly(GraphEntrances.Select(gate => CreateGraphEntrance(facts, gate)).ToArray());
        }

        public static void VerifyPhysicalIdentities(FogDsrFacts facts)
        {
            foreach (GateIdentity gate in identities)
            {
                if (!facts.Maps.TryGetValue(gate.Map, out FogDsrFacts.Map map))
                    throw new InvalidDataException($"Compact PvP policy map '{gate.Map}' for '{gate.Name}' is absent from pristine DSR.");
                FogDsrObjectFact[] matches = map.ObjectDetails.Where(part => part.Name == gate.Name && part.EntityId == gate.EntityId).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException($"Compact PvP gate '{gate.Name}' must resolve exactly once as pristine DSR entity {gate.EntityId} on map '{gate.Map}'; found {matches.Length}.");
            }
        }

        /// <summary>
        /// Restore solo-open behavior for pinned PvP gates that are not represented as an active
    /// Compact Entrance. FogMod's GameDataWriter places all Event 5700 initializers in
    /// common.emevd.dcx event 0, so the fallback must share that archive and its slot namespace.
    /// Active Compact PvP and keyed-door Entrances are initialized by the FogMod writer itself.
    /// The unused upstream record is intentionally not initialized.
        /// </summary>
        public static void AddUnprojectedSoloInitializers(string archiveName, EMEVD archive, Annotations annotations)
        {
            if (archiveName == null) throw new ArgumentNullException(nameof(archiveName));
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            if (annotations == null) throw new ArgumentNullException(nameof(annotations));
            if (!string.Equals(archiveName, "common.emevd.dcx", StringComparison.Ordinal)) return;

            GateIdentity[] candidates = identities.Where(gate => !gate.Unused
                && !annotations.Entrances.Any(entrance => string.Equals(entrance.Name, gate.Name, StringComparison.Ordinal)
                    && !entrance.HasTag("unused")))
                .ToArray();
            if (candidates.Length == 0) return;

            EMEVD.Event event0 = archive.Events.SingleOrDefault(evt => evt.ID == 0)
                ?? throw new InvalidDataException($"Map event archive '{archiveName}' has no event 0 for the Compact PvP solo initializers.");
            HashSet<int> occupiedSlots = event0.Instructions
                .Where(instruction => instruction.Bank == 2000 && instruction.ID == 0 && instruction.ArgData.Length >= sizeof(int))
                .Select(instruction => BitConverter.ToInt32(instruction.ArgData, 0))
                .ToHashSet();
            int slot = 0;
            foreach (GateIdentity gate in candidates)
            {
                while (occupiedSlots.Contains(slot)) slot++;
                MapSpec mapSpec = AnnotationData.nameSpecs[gate.Map.Value];
                int mapArea = int.Parse(mapSpec.Map.Substring(1, 2), CultureInfo.InvariantCulture);
                int mapBlock = int.Parse(mapSpec.Map.Substring(4, 2), CultureInfo.InvariantCulture);
                event0.Instructions.Add(InitializeEventCall(slot, gate.EntityId, mapArea, mapBlock));
                occupiedSlots.Add(slot++);
            }
        }

        private static FogEntrance CreateGraphEntrance(FogDsrFacts facts, GateIdentity gate)
        {
            FogAreaId areaA = new FogAreaId(gate.ASideArea);
            FogAreaId areaB = new FogAreaId(gate.BSideArea);
            FogEntranceSide Side(FogAreaId area, bool aSide)
            {
                bool higher = IsHigher(gate.Name, aSide);
                int beforeWarpFlag = gate.Name == "o1410_0000" && aSide ? -11020302
                    : gate.Name == "o6601_0000" && !aSide ? -11600201 : 0;
                FogCustomWarp customWarp = gate.Name == "o1410_0000" && aSide
                    ? new FogCustomWarp(new FogMapId("firelink"), new FogPosition(-5.132f, 28.46f, 109.28f, 90f))
                    : gate.Name == "o6601_0000" && !aSide
                        ? new FogCustomWarp(new FogMapId("firelink"), new FogPosition(-52.759f, -74.018f, 50.86f, -110f))
                        : null;
                FogCondition condition = SideCondition(gate.Name, aSide);
                FogEntranceKey? excludeIfRandomized = gate.Name == "o2920_0000" && aSide
                    ? new FogEntranceKey("o2916_0000")
                    : gate.Name == "o3970_0000" && !aSide
                    ? new FogEntranceKey("o4954_0000")
                    : gate.Name == "o4954_0000" && aSide
                        ? new FogEntranceKey("o3970_0000")
                        : gate.Name == "o5869_0000" && !aSide
                            ? new FogEntranceKey("o5869_0000")
                            : null;
                // Keep the upstream writer's entrance-map default. Only the three pinned
                // PvP records with explicit upstream DestinationMap values override it.
                FogMapId? destinationMap = gate.Name switch
                {
                    "o2901_0000" when aSide => new FogMapId("parish"),
                    "o2903_0000" when !aSide => new FogMapId("newlondo"),
                    "o5151_0000" when aSide => new FogMapId("parish"),
                    _ => null,
                };
                FogEntranceSideTags sideTags = higher ? FogEntranceSideTags.Higher : FogEntranceSideTags.None;
                if (gate.Name == "o2920_0000" && !aSide) sideTags |= FogEntranceSideTags.Main;
                return new FogEntranceSide(
                    area, 0, 0, 0, beforeWarpFlag, 0, null, 0, condition, customWarp,
                    gate.Name == "o1410_0000" ? 4 : 0, 0, excludeIfRandomized,
                    destinationMap, sideTags);
            }

            FogEntranceTags tags = FogEntranceTags.Pvp | (gate.Major ? FogEntranceTags.Major : FogEntranceTags.None);
            return new FogEntrance(gate.Name, gate.EntityId, gate.Map,
                $"Compact PvP transition {gate.Name}", null, tags, Side(areaA, true), Side(areaB, false));
        }

        private static FogCondition SideCondition(string entrance, bool aSide)
        {
            if (entrance == "o4968_0000" && !aSide)
                return new FogCondition(FogConditionKind.Any, Array.AsReadOnly(new[]
                {
                    FogConditionReference.KeyItem(new FogItemName("orange_charred_ring")),
                    FogConditionReference.Area(new FogAreaId("demonruins_ceaseless")),
                }));
            if (entrance == "o5151_0000" && !aSide)
                return new FogCondition(FogConditionKind.All, Array.AsReadOnly(new[]
                {
                    FogConditionReference.Area(new FogAreaId("blighttown_bell")),
                    FogConditionReference.Area(new FogAreaId("parish_bell")),
                }));
            if (entrance == "o5863_0000" && !aSide)
                return new FogCondition(FogConditionKind.Atom, Array.AsReadOnly(new[]
                {
                    FogConditionReference.Area(new FogAreaId("anorlondo_start")),
                }));
            return null;
        }

        private static bool IsHigher(string name, bool aSide) =>
            (name == "o2901_0000" && aSide)
            || (name == "o2903_0000" && !aSide)
            || name == "o2907_0000"
            || (name == "o3951_0000" && aSide)
            || (name == "o3970_0000" && !aSide)
            || (name == "o4954_0000" && aSide)
            || (name == "o5151_0000" && aSide)
            || (name == "o8051_0000" && aSide);

        private static EMEVD.Instruction InitializeEventCall(int slot, int entranceEntityId, int mapArea, int mapBlock)
        {
            // FogMod's AddWarpEvent(mode: 1) ABI: slot, event 5700, mode, map, block,
            // before-warp flag sign, then the paired object entities.
            return new EMEVD.Instruction(2000, 0, new object[]
            {
                slot, (uint)5700, (byte)1, checked((byte)mapArea), checked((byte)mapBlock), (byte)0,
                entranceEntityId, entranceEntityId + 1, 0, 0, 0, 0, 0,
            });
        }

        private static GateIdentity Gate(string name, int id, string map, string sideA, string sideB, bool major, bool door = false, bool unused = false) =>
            new GateIdentity(name, id, new FogMapId(map), sideA, sideB, major, door, unused);
    }
}
