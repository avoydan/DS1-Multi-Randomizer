using System;
using System.Linq;
using FogHost;
using FogMod;
using SoulsFormats;

namespace FogHost.Tests
{
    public static class FogCompactV2PvpPolicyTest
    {
        public static void VerifyWriterAnchorWhenPvpIsOff()
        {
            AnnotationData.Annotations MakeAnnotations() => new AnnotationData.Annotations
            {
                Areas = new System.Collections.Generic.List<AnnotationData.Area>(),
                Entrances = FogCompactV2PvpPolicy.GraphEntrances.Select(gate => new AnnotationData.Entrance
                {
                    Name = gate.Name,
                    ID = gate.EntityId,
                    Area = gate.Map.Value,
                    Tags = "pvp",
                    ASide = new AnnotationData.Side
                    {
                        Area = gate.ASideArea,
                        ExcludeIfRandomized = gate.Name == "o4954_0000" ? "o3970_0000" : null,
                    },
                    BSide = new AnnotationData.Side
                    {
                        Area = gate.BSideArea,
                        ExcludeIfRandomized = gate.Name == "o3970_0000" ? "o4954_0000" : null,
                    },
                }).ToList(),
            };

            AnnotationData.Annotations off = MakeAnnotations();
            FogCompactV2PvpPolicy.ApplyGraphOptions(off, majorPvpFogs: false, minorPvpFogs: false);
            string[] anchorNames = { "o3970_0000", "o4954_0000", "o5869_0000" };
            AnnotationData.Entrance[] anchors = off.Entrances.OrderBy(entrance => entrance.Name, StringComparer.Ordinal).ToArray();
            if (!anchors.Select(entrance => entrance.Name).SequenceEqual(anchorNames, StringComparer.Ordinal)
                || anchors.Any(entrance => !entrance.HasTag("unused") || !entrance.IsFixed
                    || entrance.ASide != null || entrance.BSide != null))
                throw new InvalidOperationException("PvP-off Compact V2 must retain only o3970_0000, o4954_0000 and o5869_0000 as fixed unused lookup anchors, with no graph sides or randomized PvP entrances.");

            EMEVD common = ArchiveWithEventZero();
            FogCompactV2PvpPolicy.AddUnprojectedSoloInitializers("common.emevd.dcx", common, off);
            int[] initializedEntities = ModeOneInitializers(common.Events.Single(evt => evt.ID == 0))
                .Select(call => ReadInt(call.ArgData, 12)).ToArray();
            if (!initializedEntities.Contains(1511754) || !initializedEntities.Contains(1321994)
                || !initializedEntities.Contains(1401998))
                throw new InvalidOperationException("Unused PvP lookup anchors must not suppress mode-1 solo initializers for their physical gates.");

            AnnotationData.Annotations minorOn = MakeAnnotations();
            FogCompactV2PvpPolicy.ApplyGraphOptions(minorOn, majorPvpFogs: false, minorPvpFogs: true);
            AnnotationData.Entrance active = minorOn.Entrances.Single(entrance => entrance.Name == "o5869_0000");
            AnnotationData.Entrance activeReference = minorOn.Entrances.Single(entrance => entrance.Name == "o3970_0000");
            AnnotationData.Entrance minorReferenceAnchor = minorOn.Entrances.Single(entrance => entrance.Name == "o4954_0000");
            if (active.HasTag("unused") || active.ASide == null || active.BSide == null
                || activeReference.HasTag("unused") || activeReference.ASide == null || activeReference.BSide == null
                || activeReference.BSide.ExcludeIfRandomized != "o4954_0000"
                || !minorReferenceAnchor.HasTag("unused") || !minorReferenceAnchor.IsFixed)
                throw new InvalidOperationException("When minor PvP fogs are enabled, o5869_0000 must remain the active two-sided gate rather than the writer anchor.");

            AnnotationData.Annotations majorOn = MakeAnnotations();
            FogCompactV2PvpPolicy.ApplyGraphOptions(majorOn, majorPvpFogs: true, minorPvpFogs: false);
            AnnotationData.Entrance referenceAnchor = majorOn.Entrances.Single(entrance => entrance.Name == "o3970_0000");
            AnnotationData.Entrance referringGate = majorOn.Entrances.Single(entrance => entrance.Name == "o4954_0000");
            if (!referenceAnchor.HasTag("unused") || !referenceAnchor.IsFixed
                || referringGate.HasTag("unused") || referringGate.ASide?.ExcludeIfRandomized != "o3970_0000")
                throw new InvalidOperationException("Major-only PvP must preserve o3970_0000 as a fixed unused target for o4954_0000's ExcludeIfRandomized lookup.");
        }

        public static void VerifySoloInitializers()
        {
            var common = ArchiveWithEventZero();
            common.Events.Single(evt => evt.ID == 0).Instructions.Add(
                new EMEVD.Instruction(2000, 0, BitConverter.GetBytes(0)));
            var annotations = new AnnotationData.Annotations
            {
                Entrances = new System.Collections.Generic.List<AnnotationData.Entrance>
                {
                    new AnnotationData.Entrance { Name = "o1408_0000" },
                },
            };

            FogCompactV2PvpPolicy.AddUnprojectedSoloInitializers("common.emevd.dcx", common, annotations);
            EMEVD.Event commonEventZero = common.Events.Single(evt => evt.ID == 0);
            EMEVD.Instruction[] calls = ModeOneInitializers(commonEventZero);
            string[] expectedNames = FogCompactV2PvpPolicy.Identities
                .Where(gate => !gate.Unused && gate.Name != "o1408_0000")
                .Select(gate => gate.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            string[] actualNames = calls.Select(call => FogCompactV2PvpPolicy.Identities
                    .Single(gate => gate.EntityId == ReadInt(call.ArgData, 12)).Name)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            int[] slots = calls.Select(call => ReadInt(call.ArgData, 0)).OrderBy(slot => slot).ToArray();
            bool wrongMap = calls.Any(call =>
            {
                FogCompactV2PvpPolicy.GateIdentity gate = FogCompactV2PvpPolicy.Identities.Single(item => item.EntityId == ReadInt(call.ArgData, 12));
                string map = AnnotationData.nameSpecs[gate.Map.Value].Map;
                return call.ArgData[9] != byte.Parse(map.Substring(1, 2), System.Globalization.CultureInfo.InvariantCulture)
                    || call.ArgData[10] != byte.Parse(map.Substring(4, 2), System.Globalization.CultureInfo.InvariantCulture);
            });
            if (!expectedNames.SequenceEqual(actualNames, StringComparer.Ordinal)
                || calls.Length != 44
                || slots[0] != 1
                || slots.Distinct().Count() != slots.Length
                || wrongMap
                || calls.Any(call => call.ArgData.Length != 40
                    || ReadInt(call.ArgData, 4) != 5700
                    || call.ArgData[8] != 1
                    || call.ArgData[11] != 0
                    || ReadInt(call.ArgData, 16) != ReadInt(call.ArgData, 12) + 1))
                throw new InvalidOperationException("Compact V2 must append exact FogMod-compatible Event 5700 mode-1 calls to common event 0 for all unprojected PvP gates, preserving each target map, active gate ownership, and occupied slots.");

            var mapArchive = ArchiveWithEventZero();
            FogCompactV2PvpPolicy.AddUnprojectedSoloInitializers("m10_01_00_00.emevd.dcx", mapArchive,
                new AnnotationData.Annotations { Entrances = new System.Collections.Generic.List<AnnotationData.Entrance>() });
            if (ModeOneInitializers(mapArchive.Events.Single(evt => evt.ID == 0)).Length != 0)
                throw new InvalidOperationException("Compact V2 PvP solo initializers belong only in common.emevd.dcx event 0, not individual map archives.");
        }

        private static EMEVD ArchiveWithEventZero()
        {
            var archive = new EMEVD { Format = EMEVD.Game.DarkSouls1 };
            archive.Events.Add(new EMEVD.Event(0, EMEVD.Event.RestBehaviorType.Default));
            return archive;
        }

        private static EMEVD.Instruction[] ModeOneInitializers(EMEVD.Event eventZero) => eventZero.Instructions
            .Where(instruction => instruction.Bank == 2000 && instruction.ID == 0
                && instruction.ArgData.Length == 40
                && ReadInt(instruction.ArgData, 4) == 5700
                && instruction.ArgData[8] == 1)
            .ToArray();

        private static int ReadInt(byte[] bytes, int offset) => BitConverter.ToInt32(bytes, offset);
    }
}
