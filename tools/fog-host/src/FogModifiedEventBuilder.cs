using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SoulsFormats;

namespace FogHost
{
    /// <summary>
    /// Project-authored semantic transforms for vanilla event bodies changed by FogMod.
    /// This incremental set intentionally fails closed if the source event shape drifts.
    /// </summary>
    public static class FogModifiedEventBuilder
    {
        private const long UndeadDragonDeathWatchEvent = 11100400;
        private const long BossArenaEventReset = 11105399;
        private const long WhiteCatGuidanceStartEvent = 11210340;
        private const long WhiteCatGuidanceMiddleEvent = 11210341;
        private const long WhiteCatGuidanceEndEvent = 11210345;
        private const long KingFamilyKneelingEvent = 11510460;
        private const long ValleyDrakeMovementEvent = 11600400;
        private const long EndingCycleEvent = 21;
        private const long DemonDestroyedBonusEvent = 11810300;
        private const long SeathCrystalWatcherEvent = 11705383;
        private const int UndeadDragonCharacter = 0x0010C98A;
        private const int WhiteCatEntity = 0x00001A68;
        private const int WhiteCatGuidanceAnimation = 0x00001B5B;
        private const int KingFamilyOriginalRegion = 0x001713DB;
        private const int KingFamilyAdjustedRegion = 0x001713DA;
        private const int ValleyDrakeCharacter = 0x00186BF4;
        private const int DemonBonusGateFlag = 0x02FAF0D0;
        private const int VanillaDemonBonusGateFlag = 0x00B4352A;
        private const int UndeadBurgGateCompletionFlag = 0x00A7EDD1;
        private const int ParishGateCompletionFlag = 0x00A814CD;
        private const int ParishTowerGateCompletionFlag = 0x00A814D7;
        private const int ParishLowerGateCompletionFlag = 0x00A814E1;
        private const int ArenaGateCompletionFlag = 0x04277DC8;
        private const int ArenaGateAlreadyCompleteFlag = 0x00B4353E;

        /// <summary>Reconstructs the exact vanilla common-event edit made by FogMod.</summary>
        public static void ReconstructKnownCommonEvents(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            EMEVD.Event commonEvent0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
            if (commonEvent0 != null) ReconstructCommonEvent0(commonEvent0);
            ReconstructCommonEvent260(FindUniqueEvent(archive, 260));
            ReconstructCommonEvent840(FindUniqueEvent(archive, 840));
        }

        /// <summary>Restores Fog's randomized-start initialization and one-shot startup marker in common event 0.</summary>
        private static void ReconstructCommonEvent0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "daaa7c89a058ed57a6ac1449f55fb1d9aa89bd57ab4421a0bdf8c9ae79091ed6";
            const string targetFingerprint = "a91c9536cb747944a3e6661a0a027ecb528a0afd15e693790b7557f28c1a02ec";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Common event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Common event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            if (evt.Instructions.Count > 250)
            {
                // GameDataWriter appends seed-specific warp initializers and combined-engine startup
                // calls after the pinned base body. Recognize that generated form by its exact
                // reconstructed prefix, then leave the dynamic suffix byte-for-byte untouched.
                var generatedPrefix = new EMEVD.Event(evt.ID, evt.RestBehavior);
                generatedPrefix.Instructions.AddRange(evt.Instructions.Take(250));
                if (EventSemanticFingerprint(generatedPrefix) == targetFingerprint) return;
            }
            Require(fingerprint == sourceFingerprint,
                "Common event 0 does not match its known pristine or reconstructed semantic form ("
                + evt.Instructions.Count.ToString(CultureInfo.InvariantCulture) + " instructions; fingerprint "
                + fingerprint + ").");

            // Fog replaces the vanilla common warp initializers with ten randomized-start region groups.
            // These initializers are for map 18-01 and use the already-authored event 8960 implementation.
            evt.Instructions.RemoveAt(250);
            evt.Instructions.RemoveRange(234, 9);
            evt.Instructions.RemoveRange(191, 9);
            evt.Instructions.RemoveAt(123);
            evt.Instructions.AddRange(new[]
            {
                InitializeEventCall(0, 8960, 0, 1810100, 1810110, 0),
                InitializeEventCall(1, 8960, 1, 1810120, 1810130, 0),
                InitializeEventCall(2, 8960, 2, 1810140, 1810150, 0),
                InitializeEventCall(3, 8960, 3, 1810160, 1810170, 0),
                InitializeEventCall(4, 8960, 4, 1810180, 1810190, 0),
                InitializeEventCall(5, 8960, 5, 1810200, 1810210, 1810220),
                InitializeEventCall(6, 8960, 6, 1810230, 1810240, 1810250),
                InitializeEventCall(7, 8960, 7, 1810260, 1810270, 1810280),
                InitializeEventCall(8, 8960, 8, 1810290, 1810300, 1810310),
                InitializeEventCall(9, 8960, 9, 1810320, 1810330, 0),
                InitializeEventCall(0, 5690, 0),
                new EMEVD.Instruction(3, 22, Convert.FromHexString("01010000")),
                new EMEVD.Instruction(1000, 1, Convert.FromHexString("01000100")),
                new EMEVD.Instruction(2007, 3, Convert.FromHexString("DAE2E40001000000")),
            });

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "Common event 0 reconstruction did not produce the pinned semantic form.");
        }

        /// <summary>Reconstructs the exact common gesture event and its parameter bindings.</summary>
        private static void ReconstructCommonEvent840(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = CommonEvent840SourceBody();
            EMEVD.Instruction[] referenceBody = CommonEvent840ReferenceBody();
            EMEVD.Parameter[] sourceParameters = CommonEvent840SourceParameters();
            EMEVD.Parameter[] referenceParameters = CommonEvent840ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Common event 840 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        private static EMEVD.Instruction[] CommonEvent840SourceBody() => new[]
        {
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0001000000000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("030100004C030000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("020100004F030000")),
            new EMEVD.Instruction(2004, 14, Convert.FromHexString("1027000000000000")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000FFFFFFFF00000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0901000048030000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0801000049030000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("070100004A030000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("060100004B030000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("050100004D030000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("040100004E030000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0301000050030000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0201000051030000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("010100005C030000")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000FFFFFFFF00000100")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000803F")),
            new EMEVD.Instruction(2010, 2, Convert.FromHexString("102700000500000015CD5B07")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00008040")),
            new EMEVD.Instruction(1000, 5, Convert.FromHexString("0100000000000000FFFFFFFF")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000FFFFFFFF01000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] CommonEvent840ReferenceBody() => new[]
        {
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0001000000000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("020100004C030000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("010100004F030000")),
            new EMEVD.Instruction(2004, 14, Convert.FromHexString("1027000000000000")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000FFFFFFFF00000000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000803F")),
            new EMEVD.Instruction(2010, 2, Convert.FromHexString("102700000500000015CD5B07")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00008040")),
            new EMEVD.Instruction(1000, 5, Convert.FromHexString("0100000000000000FFFFFFFF")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000FFFFFFFF01000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Parameter[] CommonEvent840SourceParameters() => new[]
        {
            new EMEVD.Parameter(0, 0, 0, 4),
            new EMEVD.Parameter(1, 4, 0, 4),
            new EMEVD.Parameter(5, 4, 4, 4),
            new EMEVD.Parameter(15, 4, 4, 4),
            new EMEVD.Parameter(4, 4, 8, 4),
            new EMEVD.Parameter(19, 4, 12, 4),
            new EMEVD.Parameter(20, 4, 12, 4),
        };

        private static EMEVD.Parameter[] CommonEvent840ReferenceParameters() => new[]
        {
            new EMEVD.Parameter(0, 0, 0, 4),
            new EMEVD.Parameter(1, 4, 0, 4),
            new EMEVD.Parameter(5, 4, 4, 4),
            new EMEVD.Parameter(4, 4, 8, 4),
            new EMEVD.Parameter(9, 4, 12, 4),
            new EMEVD.Parameter(10, 4, 12, 4),
        };

        /// <summary>Removes exact FogMod-omitted initializers from known map event-50 bodies.</summary>
        public static void ReconstructKnownMapEventInitializers(string archiveName, EMEVD archive)
        {
            if (archiveName == null) throw new ArgumentNullException("archiveName");
            if (archive == null) throw new ArgumentNullException("archive");

            switch (archiveName)
            {
                case "m10_00_00_00.emevd.dcx":
                    EMEVD.Event m10Event0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m10Event0 != null) ReconstructM10Event0(m10Event0);
                    ReconstructMapEventInitializers(archive, 36,
                        "0000000065ECA70000000000", "000000006CECA70000000000", "000000006FECA70000000000",
                        "0000000095EDA70000000000", "0000000026F0A70068ECA7008D190000",
                        "0100000026F0A7006BECA700BF190000");
                    break;
                case "m10_01_00_00.emevd.dcx":
                    ReconstructMapEventInitializers(archive, 66,
                        "00000000DC16A80000000000", "00000000DD16A80000000000",
                        "000000003617A800BE13A8008C190000", "010000003617A800C113A800BE190000",
                        "000000002314A80000000000");
                    break;
                case "m11_00_00_00.emevd.dcx":
                    ReconstructM11Event0(FindUniqueEvent(archive, 0));
                    break;
                case "m12_00_00_00.emevd.dcx":
                    EMEVD.Event m12Event0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m12Event0 != null) ReconstructM12Event0(m12Event0);
                    ReconstructMapEventInitializers(archive, 78,
                        "00000000ABF9AA0000000000", "0000000066FDAA00A7F9AA0079190000");
                    break;
                case "m12_01_00_00.emevd.dcx":
                    EMEVD.Event m12DlcEvent0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m12DlcEvent0 != null) ReconstructM12DlcEvent0(m12DlcEvent0);
                    break;
                case "m14_00_00_00.emevd.dcx":
                    EMEVD.Event m14Event0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m14Event0 != null) ReconstructM14Event0(m14Event0);
                    ReconstructMapEventInitializers(archive, 69,
                        "00000000E506AE0000000000", "00000000EB06AE0000000000",
                        "00000000E6B6D500E706AE0083190000");
                    break;
                case "m14_01_00_00.emevd.dcx":
                    EMEVD.Event m14DlcEvent0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m14DlcEvent0 != null) ReconstructM14DlcEvent0(m14DlcEvent0);
                    ReconstructMapEventInitializers(archive, 58,
                        "00000000F52DAE0000000000", "00000000FB2DAE0000000000", "00000000FE2DAE0000000000",
                        "00000000F6DDD500F72DAE008E190000");
                    break;
                case "m15_00_00_00.emevd.dcx":
                    EMEVD.Event event0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (event0 != null) ReconstructM15Event0(event0);
                    ReconstructMapEventInitializers(archive, 44,
                        "00000000858DAF0000000000", "000000004691AF00878DAF006E190000");
                    break;
                case "m15_01_00_00.emevd.dcx":
                    EMEVD.Event m15DlcEvent0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m15DlcEvent0 != null) ReconstructM15DlcEvent0(m15DlcEvent0);
                    ReconstructMapEventInitializers(archive, 52,
                        "0000000095B4AF0000000000", "0000000056B8AF0097B4AF008F190000");
                    break;
                case "m16_00_00_00.emevd.dcx":
                    EMEVD.Event m16Event0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m16Event0 != null) ReconstructM16Event0(m16Event0);
                    ReconstructMapEventInitializers(archive, 38,
                        "000000002514B10000000000", "00000000E617B1002714B10078190000");
                    break;
                case "m17_00_00_00.emevd.dcx":
                    EMEVD.Event m17Event0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m17Event0 != null) ReconstructM17Event0(m17Event0);
                    break;
                case "m13_02_00_00.emevd.dcx":
                    ReconstructRemovedVanillaInstructionSequences(archive,
                        RemoveInstructionSequence(0, 46, EMEVD.Event.RestBehaviorType.Default,
                            new EMEVD.Instruction(1003, 5, Convert.FromHexString("02010000")),
                            new EMEVD.Instruction(2005, 3, Convert.FromHexString("0A2C140000000000")),
                            new EMEVD.Instruction(2006, 1, Convert.FromHexString("0B2C140000000000")),
                            new EMEVD.Instruction(2000, 0,
                                Convert.FromHexString("000000001ABBAC00E42A1400E52A1400682E1400692E1400"))));
                    break;
                case "m13_01_00_00.emevd.dcx":
                    EMEVD.Event m13DlcEvent0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m13DlcEvent0 != null) ReconstructM13DlcEvent0(m13DlcEvent0);
                    break;
                case "m13_00_00_00.emevd.dcx":
                    EMEVD.Event m13Event0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m13Event0 != null) ReconstructM13Event0(m13Event0);
                    break;
                case "m18_00_00_00.emevd.dcx":
                    ReconstructRemovedVanillaInstructionSequences(archive,
                        RemoveInstructionSequence(0, 31, EMEVD.Event.RestBehaviorType.Default,
                            new EMEVD.Instruction(1003, 5, Convert.FromHexString("02010000")),
                            new EMEVD.Instruction(2005, 3, Convert.FromHexString("0A7F1B0000000000")),
                            new EMEVD.Instruction(2006, 1, Convert.FromHexString("0B7F1B0000000000"))));
                    break;
                case "m18_01_00_00.emevd.dcx":
                    EMEVD.Event m18DlcEvent0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
                    if (m18DlcEvent0 != null) ReconstructM18DlcEvent0(m18DlcEvent0);
                    break;
            }
        }

        /// <summary>Reconstructs the currently understood m11 event edits from pristine EMEVD input.</summary>
        public static void ReconstructKnownM11Events(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructUndeadDragonDeathWatch(FindUniqueEvent(archive, UndeadDragonDeathWatchEvent));
            ReconstructBossArenaEventReset(FindUniqueEvent(archive, BossArenaEventReset));
            ReconstructPrefixedEvents(archive,
                Disabled(11105390, 6, EMEVD.Event.RestBehaviorType.Default));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveObjectSfxPair(11100000, 10, EMEVD.Event.RestBehaviorType.Default, 0x0010D0A6, 0, 0x0010D0A7, false),
                RemoveSingleInstruction(50, 19, EMEVD.Event.RestBehaviorType.Default, 2000, 0,
                    "000000000673A90000000000"));
            ReconstructM11Event11100710(FindUniqueEvent(archive, 11100710));
        }

        /// <summary>Reconstructs the exact reference body for the Undead Parish progression event.</summary>
        private static void ReconstructM11Event11100710(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M11Event11100710SourceBody();
            EMEVD.Instruction[] reconstructedBody = M11Event11100710ReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11100710 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M11Event11100710SourceBody() => new[]
        {
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("6474A90000000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010000009B060000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("0101000010270000A4D21000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 12, Convert.FromHexString("80C9100001000000")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(2004, 12, Convert.FromHexString("80C9100000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("6474A90001000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0101000004000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("000100007374A900")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0100000080A2AF00")),
            new EMEVD.Instruction(2003, 19, Convert.FromHexString("0F000200")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("D3AD010000000000341417000F01000010270000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0101010000000000")),
            new EMEVD.Instruction(2003, 36, Convert.FromHexString("46230000")),
            new EMEVD.Instruction(2003, 23, Convert.FromHexString("00161700")),
            new EMEVD.Instruction(2000, 5, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M11Event11100710ReconstructedBody() => new[]
        {
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("6474A90000000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010000009B060000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("0101000010270000A4D21000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 12, Convert.FromHexString("80C9100001000000")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(2004, 12, Convert.FromHexString("80C9100000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("6474A90001000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0101000004000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("000100007374A900")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("B614000001000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        /// <summary>Reconstructs the Undead Burg event that waits for its gate completion flag and then ends.</summary>
        public static void ReconstructKnownM10Events(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            EMEVD.Event event11005150 = archive.Events.SingleOrDefault(evt => evt.ID == 11005150);
            if (event11005150 != null) ReconstructM10Event11005150(event11005150);
            ReconstructPrefixedEvents(archive,
                WaitForFlagThenEnd(11005390, 6, EMEVD.Event.RestBehaviorType.Default, UndeadBurgGateCompletionFlag));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveObjectSfxPair(11000001, 6, EMEVD.Event.RestBehaviorType.Default, 0x000F4A06, 0, 0x000F4A07, false),
                RemoveInstructionSequence(11005392, 26, EMEVD.Event.RestBehaviorType.Restart,
                    new EMEVD.Instruction(4, 3, Convert.FromHexString("FF000000102700000C000000")),
                    new EMEVD.Instruction(4, 3, Convert.FromHexString("FF0000001027000002000000")),
                    new EMEVD.Instruction(1000, 2, Convert.FromHexString("0001FF00"))));
            ReconstructRemovedVanillaInstructionSets(archive,
                RemoveInstructionSet(11005030, 22, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(3, 24, Convert.FromHexString("01050200")),
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000068ECA700")),
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("010000006DECA700"))));
            ReconstructM10GateObjectStateEvent(archive, 11005033, 21, new[] { 8, 12, 13 },
                new[]
                {
                    new EMEVD.Instruction(3, 24, Convert.FromHexString("01050200")),
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("010000006BECA700")),
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("010000006EECA700")),
                },
                new[]
                {
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("000100006BECA700")),
                    new EMEVD.Instruction(2004, 8, Convert.FromHexString("BF1900004A150000")),
                });
            ReconstructM10WatcherEvent(FindUniqueEvent(archive, 11005391));
        }

        private static void ReconstructM10Event11005150(EMEVD.Event evt)
        {
            const string sourceFingerprint = "10cfb524174ca0b4717f4b7af3f933be2a49f996e3922b561ff1c73d20b78213";
            const string targetFingerprint = "3d429a00397171d138a744ce1852140d4e56d946b66ee2dbb5acaf9c794dae19";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Restart,
                "Event 11005150 has an unexpected rest behavior.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "Event 11005150 does not match its known pristine or reconstructed semantic form.");

            // The reference intentionally retains the five-operation skip count after removing one
            // instruction from its source span; preserve every surviving skip operand exactly.
            evt.Instructions.RemoveRange(0, 15);
            evt.Instructions.Insert(0, new EMEVD.Instruction(1003, 1,
                Convert.FromHexString("0200020000000000")));
            evt.Instructions.RemoveAt(18); // source instruction 32
            evt.Instructions.RemoveAt(15); // source instruction 29

            var parameters = new List<EMEVD.Parameter>();
            foreach (EMEVD.Parameter parameter in evt.Parameters)
            {
                long sourceInstructionIndex = parameter.InstructionIndex;
                if (sourceInstructionIndex <= 14 || sourceInstructionIndex == 29 || sourceInstructionIndex == 32)
                    continue;

                EMEVD.Parameter copy = CopyParameter(parameter);
                copy.InstructionIndex = (int)(sourceInstructionIndex - 14 - (sourceInstructionIndex > 29 ? 1 : 0));
                parameters.Add(copy);
            }
            evt.Parameters.Clear();
            evt.Parameters.AddRange(parameters);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "Event 11005150 reconstruction did not produce the pinned semantic form.");
        }

        /// <summary>Reconstructs the Parish gate events that wait for their completion flags and then end.</summary>
        public static void ReconstructKnownM10ParishEvents(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            EMEVD.Event parishEvent0 = archive.Events.SingleOrDefault(evt => evt.ID == 0);
            if (parishEvent0 != null) ReconstructM10ParishEvent0(parishEvent0);
            ReconstructPrefixedEvents(archive,
                WaitForFlagThenEnd(11015370, 6, EMEVD.Event.RestBehaviorType.Default, ParishGateCompletionFlag),
                WaitForFlagThenEnd(11015380, 6, EMEVD.Event.RestBehaviorType.Default, ParishTowerGateCompletionFlag),
                WaitForFlagThenEnd(11015390, 6, EMEVD.Event.RestBehaviorType.Default, ParishLowerGateCompletionFlag));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveInstructionSequence(11010001, 18, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(2005, 3, new object[] { 0x000F7116, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x000F7117, (byte)1 }),
                    new EMEVD.Instruction(2005, 3, new object[] { 0x000F7118, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x000F7119, (byte)1 })),
                RemoveInstructionSequence(11010901, 14, EMEVD.Event.RestBehaviorType.Restart,
                    new EMEVD.Instruction(2005, 3, new object[] { 0x000F70B2, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x000F70B3, (byte)1 }),
                    new EMEVD.Instruction(2005, 3, new object[] { 0x000F70B4, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x000F70B5, (byte)1 })),
                RemoveObjectSfxPair(11010902, 20, EMEVD.Event.RestBehaviorType.Restart, 0x000F704E, 0, 0x000F704F, false),
                RemoveInstructionSequence(11010000, 22, EMEVD.Event.RestBehaviorType.Restart,
                    new EMEVD.Instruction(4, 3, Convert.FromHexString("FF0000001027000002000000")),
                    new EMEVD.Instruction(4, 3, Convert.FromHexString("FF000000102700000C000000")),
                    new EMEVD.Instruction(1000, 2, Convert.FromHexString("0001FF00"))));
            ReconstructRemovedVanillaInstructionSets(archive,
                RemoveInstructionSet(11015100, 25, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(3, 24, Convert.FromHexString("01050200")),
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000BE13A800")),
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000C213A800"))));
            ReconstructM10GateObjectStateEvent(archive, 11015103, 22, new[] { 8, 11, 12 },
                new[]
                {
                    new EMEVD.Instruction(3, 24, Convert.FromHexString("01050200")),
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000C113A800")),
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000C313A800")),
                },
                new[]
                {
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("00010000C113A800")),
                    new EMEVD.Instruction(2004, 8, Convert.FromHexString("BE1900004A150000")),
                });
        }

        /// <summary>Reconstructs the New Londo gate transition event from its exact pristine shape.</summary>
        public static void ReconstructKnownM10NewLondoEvents(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            EMEVD.Event startEvent = archive.Events.SingleOrDefault(evt => evt.ID == 50);
            if (startEvent != null) ReconstructM10NewLondoEvent50(startEvent);
            ReconstructM10NewLondoTransition(FindUniqueEvent(archive, 11020021));
            ReconstructM10NewLondoDlcAreaEvent(FindUniqueEvent(archive, 11020899));
            ReconstructM10NewLondoFlagCleanup(FindUniqueEvent(archive, 11020001));
        }

        /// <summary>Reconstructs the exact New Londo starting-event flag and initializer changes.</summary>
        private static void ReconstructM10NewLondoEvent50(EMEVD.Event evt)
        {
            const string sourceFingerprint = "863f4b679335784d1754d9fd258048f7eaa23ef223c02544424619e2a30a0306";
            const string targetFingerprint = "d9ee0964ecad5041774501bff322fc50430effdf0bbe1c861c083f6853b8b681";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M10 New Londo event 50 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M10 New Londo event 50 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M10 New Londo event 50 does not match its known pristine or reconstructed semantic form.");

            evt.Instructions[0] = new EMEVD.Instruction(2003, 22,
                Convert.FromHexString("2000FB026500FB0201000000"));
            evt.Instructions.RemoveAt(72);
            evt.Instructions.RemoveAt(71);
            evt.Instructions.Insert(71, new EMEVD.Instruction(2000, 0,
                Convert.FromHexString("00000000632AA8002000FB026500FB02")));

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M10 New Londo event 50 reconstruction did not produce the pinned semantic form.");
        }

        /// <summary>Reconstructs vanilla gate interaction events disabled by FogMod in the Depths.</summary>
        public static void ReconstructKnownM12Events(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructM12Event11200005(FindUniqueEvent(archive, 11200005));
            ReconstructPrefixedEvents(archive,
                Disabled(11205380, 6, EMEVD.Event.RestBehaviorType.Default),
                Disabled(11205390, 7, EMEVD.Event.RestBehaviorType.Default));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveObjectSfxPair(11200001, 5, EMEVD.Event.RestBehaviorType.Default, 0x00125746, 0, 0x00125747, false),
                RemoveObjectSfxPair(11200002, 30, EMEVD.Event.RestBehaviorType.Default, 0x00125746, 1, 0x00125747, true));
            ReconstructRemovedVanillaInstructionSets(archive,
                RemoveInstructionSet(11200000, 34, EMEVD.Event.RestBehaviorType.Restart,
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00125746, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00125747, (byte)0 }),
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00125746, (byte)1 }),
                    new EMEVD.Instruction(2006, 2, new object[] { 0x00125747 })));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveInstructionSequence(11200900, 8, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(2005, 3, new object[] { 0x001256E2, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x001256E3, (byte)1 }),
                    new EMEVD.Instruction(2005, 3, new object[] { 0x001256E4, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x001256E5, (byte)1 })));
            ReconstructM12Event11200006(FindUniqueEvent(archive, 11200006));
        }

        /// <summary>Reconstructs the exact reference body for the Depths progression event.</summary>
        private static void ReconstructM12Event11200006(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M12Event11200006SourceBody();
            EMEVD.Instruction[] reconstructedBody = M12Event11200006ReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11200006 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M12Event11200006SourceBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF01000065040000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF01000066040000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF01000067040000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FF00")),
            new EMEVD.Instruction(3, 4, Convert.FromHexString("01030000D809000001000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000011000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01030000")),
            new EMEVD.Instruction(3, 5, Convert.FromHexString("01000000010000005857120000000000FFFF000000000000F4BD98003000000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("C2D40100000000005E8012000C01000010270000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2000, 5, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12Event11200006ReconstructedBody() => new[]
        {
            new EMEVD.Instruction(3, 4, Convert.FromHexString("01030000D809000001000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01030000")),
            new EMEVD.Instruction(3, 5, Convert.FromHexString("01000000010000005857120000000000FFFF000000000000F4BD98003000000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("B714000001000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        /// <summary>Reconstructs the three repeated White Cat guidance animation edits.</summary>
        public static void ReconstructKnownM12DlcEvents(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            EMEVD.Event anorLondoArenaSetup = archive.Events.SingleOrDefault(evt => evt.ID == 11210700);
            if (anorLondoArenaSetup != null) ReconstructM12DlcEvent11210700(anorLondoArenaSetup);
            EMEVD.Event areaStateWatcher = archive.Events.SingleOrDefault(evt => evt.ID == 11210100);
            if (areaStateWatcher != null) ReconstructM12DlcEvent11210100(areaStateWatcher);
            EMEVD.Event areaEnemyWatcher = archive.Events.SingleOrDefault(evt => evt.ID == 11210830);
            if (areaEnemyWatcher != null) ReconstructM12DlcEvent11210830(areaEnemyWatcher);
            EMEVD.Event catGuidanceWatcher = archive.Events.SingleOrDefault(evt => evt.ID == 11210870);
            if (catGuidanceWatcher != null) ReconstructM12DlcEvent11210870(catGuidanceWatcher);
            EMEVD.Event stateGuidanceWatcher = archive.Events.SingleOrDefault(evt => evt.ID == 11210435);
            if (stateGuidanceWatcher != null) ReconstructM12DlcEvent11210435(stateGuidanceWatcher);
            EMEVD.Event partyAreaWatcher = archive.Events.SingleOrDefault(evt => evt.ID == 11210839);
            if (partyAreaWatcher != null) ReconstructM12DlcEvent11210839(partyAreaWatcher);
            EMEVD.Event multiplayerAreaWatcher = archive.Events.SingleOrDefault(evt => evt.ID == 11210875);
            if (multiplayerAreaWatcher != null) ReconstructM12DlcEvent11210875(multiplayerAreaWatcher);
            ReconstructRemovedVanillaInstructionSets(archive,
                RemoveInstructionSet(11210838, 13, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007C811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007D811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000079811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007A811200"))),
                RemoveInstructionSet(11210439, 13, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000079811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007A811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007C811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007D811200"))),
                RemoveInstructionSet(11210407, 119, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007D811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007C811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007A811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000079811200"))),
                RemoveInstructionSet(11210878, 18, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007C811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007D811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000079811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007A811200"))),
                RemoveInstructionSet(11210402, 19, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007C811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007D811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000079811200")),
                    new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007A811200"))));
            ReconstructWhiteCatGuidance(FindUniqueEvent(archive, WhiteCatGuidanceStartEvent));
            ReconstructWhiteCatGuidance(FindUniqueEvent(archive, WhiteCatGuidanceMiddleEvent));
            ReconstructWhiteCatGuidance(FindUniqueEvent(archive, WhiteCatGuidanceEndEvent));
            ReconstructM12DlcMapGuard(FindUniqueEvent(archive, 11215007));
            ReconstructM12DlcMapGuard(FindUniqueEvent(archive, 11215009));
            ReconstructPrefixedEvents(archive,
                Disabled(11215000, 6, EMEVD.Event.RestBehaviorType.Restart),
                Disabled(11215010, 6, EMEVD.Event.RestBehaviorType.Restart),
                Disabled(11215060, 7, EMEVD.Event.RestBehaviorType.Restart),
                WaitForAreaThenEnd(11215020, 6, EMEVD.Event.RestBehaviorType.Restart, 0x00128244));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveInstructionSequence(11210000, 16, EMEVD.Event.RestBehaviorType.Restart,
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00127D8E, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00127D8F, (byte)1 }),
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00127D90, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00127D91, (byte)1 })),
                RemoveObjectSfxPair(11210002, 21, EMEVD.Event.RestBehaviorType.Default, 0x00127E56, 0, 0x00127E57, false),
                RemoveObjectSfxPair(11210005, 12, EMEVD.Event.RestBehaviorType.Default, 0x00127D2A, 0, 0x00127D2B, false),
                RemoveObjectSfxPair(11210535, 18, EMEVD.Event.RestBehaviorType.Default, 0x00127D2A, 1, 0x00127D2B, true));
            ReconstructM12DlcEvent11210401(FindUniqueEvent(archive, 11210401));
            ReconstructM12DlcEvent11210404(FindUniqueEvent(archive, 11210404));
            ReconstructM12DlcEvent11210430(FindUniqueEvent(archive, 11210430));
            ReconstructM12DlcEvent11210701(FindUniqueEvent(archive, 11210701));
            ReconstructM12DlcEvent11210434(FindUniqueEvent(archive, 11210434));
            ReconstructM12DlcEvent11210840(FindUniqueEvent(archive, 11210840));
            ReconstructM12DlcEvent11210835(FindUniqueEvent(archive, 11210835));
            ReconstructM12DlcEvent11210831(FindUniqueEvent(archive, 11210831));
            ReconstructM12DlcEvent11210900(FindUniqueEvent(archive, 11210900));
            ReconstructM12DlcEvent11210849(FindUniqueEvent(archive, 11210849));
            ReconstructM12DlcEvent11210850(FindUniqueEvent(archive, 11210850));
            ReconstructM12DlcEvent11210800(FindUniqueEvent(archive, 11210800));
            ReconstructM12DlcEvent11210890(FindUniqueEvent(archive, 11210890));
            ReconstructM12DlcEvent11210836(FindUniqueEvent(archive, 11210836));
            ReconstructM12DlcStateWatcher(FindUniqueEvent(archive, 11210110), 68, "7521AB00", new[]
            {
                "0101000010270000CF7E1200", "0201000010270000CE7E1200",
                "0301000010270000D07E1200", "0401000010270000D17E1200",
            });
            ReconstructM12DlcStateWatcher(FindUniqueEvent(archive, 11210120), 72,
                new[] { "000100008B0DAB00", "000000007621AB00" }, new[]
                {
                    "0101000010270000D97E1200", "0201000010270000D87E1200",
                    "0301000010270000DA7E1200", "0401000010270000DB7E1200",
                }, M12DlcTwoFlagStateConditionGroups("8B0DAB00", "7621AB00"));
            ReconstructM12DlcStateWatcher(FindUniqueEvent(archive, 11210130), 72,
                new[] { "00010000950DAB00", "000000007721AB00" }, new[]
                {
                    "0101000010270000E37E1200", "0201000010270000E27E1200",
                    "0301000010270000E47E1200", "0401000010270000E57E1200",
                }, M12DlcTwoFlagStateConditionGroups("950DAB00", "7721AB00"));
            ReconstructM12DlcStateWatcher(FindUniqueEvent(archive, 11210140), 69, "7821AB00", new[]
            {
                "0101000010270000ED7E1200", "0201000010270000EC7E1200",
                "0301000010270000EE7E1200", "0401000010270000EF7E1200",
            });
            ReconstructM12DlcEvent11210837(FindUniqueEvent(archive, 11210837));
            ReconstructM12DlcEvent11210886(FindUniqueEvent(archive, 11210886));
        }

        /// <summary>
        /// Rebuilds Anor Londo's PvP arena wiring from named region, gate, prompt and mode contracts.
        /// The source event is a dispatcher; its replacement initializes project-authored watcher
        /// events with the regions and entities declared by the map.
        /// </summary>
        private static void ReconstructM12DlcEvent11210700(EMEVD.Event evt)
        {
            const string sourceFingerprint = "396d8202f3f6eb24a4ca7076894274cc3b24ca1e1ab4e8c264d0fe3ea80af843";
            const string targetFingerprint = "5d2cd1ace2721c90709e5440416a0752c2a4a0f910fac8653db40a47e26600d2";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11210700 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event 11210700 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "Event 11210700 does not match its known pristine or reconstructed semantic form.");

            ReplaceM12DlcEventInstructionRange(evt, 158, 111, M12DlcEvent11210700ArenaWatchers());
            ReplaceM12DlcEventInstructionRange(evt, 137, 20, M12DlcEvent11210700ArenaStateCalls());
            ReplaceM12DlcEventInstructionRange(evt, 76, 57, M12DlcEvent11210700ModeWatchers());
            ReplaceM12DlcEventInstructionRange(evt, 73, 1, Array.Empty<EMEVD.Instruction>());
            ReplaceM12DlcEventInstructionRange(evt, 71, 1, Array.Empty<EMEVD.Instruction>());
            ReplaceM12DlcEventInstructionRange(evt, 68, 2, Array.Empty<EMEVD.Instruction>());
            ReplaceM12DlcEventInstructionRange(evt, 52, 12, new[]
            {
                InitializeEventCall(0, 11210705, 0),
                InitializeEventCall(0, 11210706, 0),
                InitializeEventCall(0, 11210707, 0),
            });
            ReplaceM12DlcEventInstructionRange(evt, 37, 11,
                new[] { InitializeEventCall(0, 11210835, FloatArgument(120f), FloatArgument(60f), FloatArgument(240f), FloatArgument(120f)) });
            ReplaceM12DlcEventInstructionRange(evt, 35, 1, Array.Empty<EMEVD.Instruction>());
            ReplaceM12DlcEventInstructionRange(evt, 34, 0,
                new[] { InitializeEventCall(0, 11210875, 0) });
            ReplaceM12DlcEventInstructionRange(evt, 32, 0,
                new[] { InitializeEventCall(0, 11210708, 0) });
            ReplaceM12DlcEventInstructionRange(evt, 9, 3, Array.Empty<EMEVD.Instruction>());
            ReplaceM12DlcEventInstructionRange(evt, 4, 3, new[]
            {
                new EMEVD.Instruction(2003, 22, new object[] { 11215350, 11215360, (byte)0 }),
            });
            ReplaceM12DlcEventInstructionRange(evt, 2, 1, Array.Empty<EMEVD.Instruction>());

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "Event 11210700 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReplaceM12DlcEventInstructionRange(EMEVD.Event evt, int start, int count,
            IReadOnlyList<EMEVD.Instruction> replacement)
        {
            Require(start >= 0 && start <= evt.Instructions.Count && count >= 0
                && start + count <= evt.Instructions.Count,
                "Event " + evt.ID + " has an invalid reconstruction range.");
            if (count == 0)
            {
                for (int index = 0; index < replacement.Count; index++)
                    SkipRanges.InsertAfter(evt, start + index, replacement[index]);
                return;
            }

            for (int index = start + count - 1; index > start; index--)
                SkipRanges.Remove(evt, index);
            if (replacement.Count == 0)
            {
                SkipRanges.Remove(evt, start);
                return;
            }

            evt.Instructions[start] = replacement[0];
            for (int index = 1; index < replacement.Count; index++)
                SkipRanges.InsertAfter(evt, start + index, replacement[index]);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210700ModeWatchers()
        {
            AnorLondoPvpMode[] modes = AnorLondoPvpModes();
            int[] modeOrder = { 5, 3, 1, 4, 2, 0 };
            var calls = new List<EMEVD.Instruction>
            {
                InitializeEventCall(0, 11210403, 0),
                InitializeEventCall(0, 11210404, 7200),
                InitializeEventCall(1, 11210404, 7450),
                InitializeEventCall(2, 11210404, 7700),
                InitializeEventCall(0, 11210407, 0),
            };

            calls.Add(InitializeEventCall(0, 11210439, 0));

            int promptSlot = 0;
            foreach (AnorLondoPvpMode mode in modes)
                calls.Add(InitializeEventCall(promptSlot++, 11210710,
                    mode.EntryActionEntity, mode.ReturnPlayerEntity, mode.EntryPromptMessage, 1));
            for (int index = 0; index < modes.Length; index++)
                calls.Add(InitializeEventCall(promptSlot++, 11210710,
                    1211510 + index, 1218200, 10010137, 0));

            for (int index = 0; index < 26; index++)
                calls.Add(InitializeEventCall(index, 11210730, 1212700 + index, 4510 + index));

            int slot = 0;
            foreach (int modeIndex in modeOrder)
            {
                AnorLondoPvpMode mode = modes[modeIndex];
                for (int participantIndex = 0; participantIndex < mode.ParticipantRegions.Length; participantIndex++)
                {
                    int region = mode.ParticipantRegions[participantIndex];
                    int state = mode.ParticipantStates[participantIndex];
                    calls.Add(InitializeEventCall(slot++, 11210410,
                        region, 11215350 + state, region + 700, 11215355 + state, 11215360 + state));
                }
            }

            slot = 0;
            foreach (int modeIndex in modeOrder)
            {
                AnorLondoPvpMode mode = modes[modeIndex];
                for (int participantIndex = 0; participantIndex < mode.ParticipantRegions.Length; participantIndex++)
                {
                    int region = mode.ParticipantRegions[participantIndex];
                    int state = mode.ParticipantStates[participantIndex];
                    calls.Add(InitializeEventCall(slot++, 11210800, region, 11215350 + state));
                }
            }
            return calls.ToArray();
        }

        private static EMEVD.Instruction[] M12DlcEvent11210700ArenaStateCalls()
        {
            int[] sharedMatchFlags = { 11215300, 11215306, 11215312, 11215318 };
            int[][] participantStateOrder =
            {
                new[] { 0, 2, 3, 1 },
                new[] { 1, 2, 3, 0 },
                new[] { 2, 0, 1, 3 },
                new[] { 3, 0, 1, 2 },
            };
            var calls = new List<EMEVD.Instruction>();
            for (int state = 0; state < 4; state++)
                calls.Add(InitializeEventCall(state, 11210825, 11215360 + state, 11215370 + state));
            for (int state = 0; state < participantStateOrder.Length; state++)
            {
                int[] order = participantStateOrder[state];
                calls.Add(InitializeEventCall(state, 11210701, 11215350 + state,
                    sharedMatchFlags[order[0]], sharedMatchFlags[order[1]],
                    sharedMatchFlags[order[2]], sharedMatchFlags[order[3]]));
            }
            return calls.ToArray();
        }

        private static EMEVD.Instruction[] M12DlcEvent11210700ArenaWatchers()
        {
            var calls = new List<EMEVD.Instruction>();
            int[] modeFlags = { 11215350, 11215351, 11215352, 11215353 };
            int[] winnerFlags = { 11215300, 11215306, 11215312, 11215318 };
            int[] pairedFlags = { 11215312, 11215312, 11215300, 11215300 };
            for (int state = 0; state < modeFlags.Length; state++)
                calls.Add(InitializeEventCall(state, 11210430,
                    modeFlags[state], winnerFlags[state], pairedFlags[state]));

            for (int state = 0; state < 4; state++)
            {
                int[] commonFlags = { 11215300, 11215306, 11215312, 11215318 };
                int[][] order =
                {
                    new[] { 0, 2, 3, 1 },
                    new[] { 1, 2, 3, 0 },
                    new[] { 2, 0, 1, 3 },
                    new[] { 3, 0, 1, 2 },
                };
                calls.Add(InitializeEventCall(state, 11210435, 11215350 + state,
                    commonFlags[order[state][0]], commonFlags[order[state][1]],
                    commonFlags[order[state][2]], commonFlags[order[state][3]]));
            }

            int[] guidanceFlags = { 11215300, 11215306, 11215312, 11215318 };
            int[][] guidanceOrder =
            {
                new[] { 0, 2, 3, 1 },
                new[] { 1, 2, 3, 0 },
                new[] { 2, 0, 1, 3 },
                new[] { 3, 0, 1, 2 },
            };
            for (int state = 0; state < 4; state++)
                calls.Add(InitializeEventCall(state, 11210870, 11215350 + state,
                    guidanceFlags[guidanceOrder[state][0]], guidanceFlags[guidanceOrder[state][1]],
                    guidanceFlags[guidanceOrder[state][2]], guidanceFlags[guidanceOrder[state][3]]));
            for (int state = 0; state < 4; state++)
                calls.Add(InitializeEventCall(state, 11210831,
                    11215360 + state, 11215300 + state * 6, 11215305 + state * 6));

            int slot = 0;
            foreach (AnorLondoArenaGate gate in AnorLondoPvpGates())
                calls.Add(InitializeEventCall(slot++, 11210760,
                    gate.AreaEntity, gate.DoorObject, gate.HitCollision, gate.StateFlag));
            slot = 0;
            foreach (AnorLondoArenaGate gate in AnorLondoPvpGates())
                calls.Add(InitializeEventCall(slot++, 11210780,
                    gate.AreaEntity, gate.SwitchObject, gate.SwitchCollision, gate.PresenceFlag));

            for (int state = 0; state < 4; state++)
            {
                int firstPair = state == 1 ? 11215306 : state == 2 ? 11215312 : state == 3 ? 11215318 : 11215300;
                int secondPair = state == 1 ? 11215300 : state == 2 ? 11215318 : state == 3 ? 11215312 : 11215306;
                calls.Add(InitializeEventCall(state, 11210840,
                    11215350 + state, 11215370 + state, 11215375 + state,
                    firstPair, secondPair, 150070 + state));
            }

            int[] participantModeOrder = { 0, 2, 4, 1, 3, 5 };
            slot = 0;
            foreach (int modeIndex in participantModeOrder)
            {
                AnorLondoPvpMode mode = AnorLondoPvpModes()[modeIndex];
                for (int participantIndex = 0; participantIndex < mode.ParticipantRegions.Length; participantIndex++)
                {
                    int state = mode.ParticipantStates[participantIndex];
                    int participantRegion = mode.ParticipantRegions[participantIndex];
                    calls.Add(InitializeEventCall(slot++, 11210850,
                        11215350 + state, 11215375 + state, mode.TerritoryRegion, participantRegion));
                }
            }

            int[] modeOrder = { 0, 2, 4, 1, 3, 5 };
            for (int state = 0; state < 4; state++)
                calls.Add(InitializeEventCall(state, 11210886, 11215350 + state, 11215375 + state));
            for (int index = 0; index < modeOrder.Length; index++)
            {
                AnorLondoPvpMode mode = AnorLondoPvpModes()[modeOrder[index]];
                calls.Add(InitializeEventCall(index, 11210880,
                    mode.TerritoryRegion, mode.ParticipantRegions[0]));
            }

            for (int index = 0; index < AnorLondoPvpModes().Length; index++)
            {
                AnorLondoPvpMode mode = AnorLondoPvpModes()[index];
                calls.Add(InitializeEventCall(index, 11210890, mode.BattleRoomRegion, mode.ReturnPlayerEntity));
            }

            return calls.ToArray();
        }

        private static int FloatArgument(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        private static AnorLondoPvpMode[] AnorLondoPvpModes() => new[]
        {
            new AnorLondoPvpMode(1212700, 1212730, 1218215, 1211500, 10010132,
                new[] { 1212703, 1212704 }, new[] { 0, 2 }),
            new AnorLondoPvpMode(1212713, 1212731, 1218213, 1211501, 10010134,
                new[] { 1212716, 1212717 }, new[] { 0, 2 }),
            new AnorLondoPvpMode(1212701, 1212732, 1218211, 1211502, 10010136,
                new[] { 1212705, 1212706, 1212707, 1212708 }, new[] { 0, 1, 2, 3 }),
            new AnorLondoPvpMode(1212714, 1212733, 1218214, 1211503, 10010131,
                new[] { 1212718, 1212719, 1212720, 1212721 }, new[] { 0, 1, 2, 3 }),
            new AnorLondoPvpMode(1212702, 1212734, 1218212, 1211504, 10010133,
                new[] { 1212709, 1212710, 1212711, 1212712 }, new[] { 0, 1, 2, 3 }),
            new AnorLondoPvpMode(1212715, 1212735, 1218210, 1211505, 10010135,
                new[] { 1212722, 1212723, 1212724, 1212725 }, new[] { 0, 1, 2, 3 }),
        };

        private static AnorLondoArenaGate[] AnorLondoPvpGates() => new[]
        {
            new AnorLondoArenaGate(1212711, 1211520, 1213202, 11215372, 1211420, 1213302, 11215362),
            new AnorLondoArenaGate(1212712, 1211521, 1213203, 11215373, 1211421, 1213303, 11215363),
            new AnorLondoArenaGate(1212709, 1211522, 1213200, 11215370, 1211422, 1213300, 11215360),
            new AnorLondoArenaGate(1212710, 1211523, 1213201, 11215371, 1211423, 1213301, 11215361),
            new AnorLondoArenaGate(1212707, 1211530, 1213212, 11215372, 1211430, 1213302, 11215362),
            new AnorLondoArenaGate(1212708, 1211531, 1213213, 11215373, 1211431, 1213303, 11215363),
            new AnorLondoArenaGate(1212705, 1211532, 1213210, 11215370, 1211432, 1213300, 11215360),
            new AnorLondoArenaGate(1212706, 1211533, 1213211, 11215371, 1211433, 1213301, 11215361),
            new AnorLondoArenaGate(1212704, 1211540, 1213222, 11215372, 1211440, 1213302, 11215362),
            new AnorLondoArenaGate(1212703, 1211542, 1213220, 11215370, 1211442, 1213300, 11215360),
            new AnorLondoArenaGate(1212724, 1211550, 1213232, 11215372, 1211450, 1213302, 11215362),
            new AnorLondoArenaGate(1212725, 1211551, 1213233, 11215373, 1211451, 1213303, 11215363),
            new AnorLondoArenaGate(1212722, 1211552, 1213230, 11215370, 1211452, 1213300, 11215360),
            new AnorLondoArenaGate(1212723, 1211553, 1213231, 11215371, 1211453, 1213301, 11215361),
            new AnorLondoArenaGate(1212720, 1211560, 1213242, 11215372, 1211460, 1213302, 11215362),
            new AnorLondoArenaGate(1212721, 1211561, 1213243, 11215373, 1211461, 1213303, 11215363),
            new AnorLondoArenaGate(1212718, 1211562, 1213240, 11215370, 1211462, 1213300, 11215360),
            new AnorLondoArenaGate(1212719, 1211563, 1213241, 11215371, 1211463, 1213301, 11215361),
            new AnorLondoArenaGate(1212717, 1211570, 1213252, 11215372, 1211470, 1213302, 11215362),
            new AnorLondoArenaGate(1212716, 1211572, 1213250, 11215370, 1211472, 1213300, 11215360),
        };

        /// <summary>
        /// Compact catalog projection of the already authored PvP gate contract. Four-player
        /// arena regions are project-major PvP entrances; two-player regions are project-minor.
        /// Every numeric physical anchor remains sourced from the same event-reconstruction policy.
        /// </summary>
        internal static IReadOnlyList<(int AreaEntity, int DoorObject, int HitCollision, int StateFlag,
            int SwitchObject, int SwitchCollision, int PresenceFlag, bool Major)> CompactPvpGatePolicies()
        {
            var participantCounts = AnorLondoPvpModes()
                .SelectMany(mode => mode.ParticipantRegions.Select(region => (Region: region, Count: mode.ParticipantRegions.Length)))
                .ToDictionary(entry => entry.Region, entry => entry.Count);
            var gates = AnorLondoPvpGates().Select(gate =>
            {
                if (!participantCounts.TryGetValue(gate.AreaEntity, out int participantCount))
                    throw new InvalidDataException($"PvP gate region {gate.AreaEntity} is not owned by a project arena mode.");
                return (gate.AreaEntity, gate.DoorObject, gate.HitCollision, gate.StateFlag,
                    gate.SwitchObject, gate.SwitchCollision, gate.PresenceFlag, Major: participantCount > 2);
            }).ToArray();
            return Array.AsReadOnly(gates);
        }

        private sealed class AnorLondoPvpMode
        {
            public AnorLondoPvpMode(int territoryRegion, int battleRoomRegion, int returnPlayerEntity,
                int entryActionEntity, int entryPromptMessage, int[] participantRegions,
                int[] participantStates)
            {
                TerritoryRegion = territoryRegion;
                BattleRoomRegion = battleRoomRegion;
                ReturnPlayerEntity = returnPlayerEntity;
                EntryActionEntity = entryActionEntity;
                EntryPromptMessage = entryPromptMessage;
                ParticipantRegions = participantRegions;
                ParticipantStates = participantStates;
            }

            public int TerritoryRegion { get; }
            public int BattleRoomRegion { get; }
            public int ReturnPlayerEntity { get; }
            public int EntryActionEntity { get; }
            public int EntryPromptMessage { get; }
            public int[] ParticipantRegions { get; }
            public int[] ParticipantStates { get; }
        }

        private sealed class AnorLondoArenaGate
        {
            public AnorLondoArenaGate(int areaEntity, int doorObject, int hitCollision, int stateFlag,
                int switchObject, int switchCollision, int presenceFlag)
            {
                AreaEntity = areaEntity;
                DoorObject = doorObject;
                HitCollision = hitCollision;
                StateFlag = stateFlag;
                SwitchObject = switchObject;
                SwitchCollision = switchCollision;
                PresenceFlag = presenceFlag;
            }

            public int AreaEntity { get; }
            public int DoorObject { get; }
            public int HitCollision { get; }
            public int StateFlag { get; }
            public int SwitchObject { get; }
            public int SwitchCollision { get; }
            public int PresenceFlag { get; }
        }

        /// <summary>Rebuilds the DLC multiplayer area watcher using named flag and condition operations.</summary>
        private static void ReconstructM12DlcEvent11210839(EMEVD.Event evt)
        {
            const string sourceFingerprint = "a3caea2fea1367b523371f8e9671b223955261bc82cbb63108e601cbe878ea1b";
            const string targetFingerprint = "353a95701a9cdd364adbfe1d9401afdc124c239a83e162d26b54c43dcabeb7ed";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11210839 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event 11210839 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "Event 11210839 does not match its known pristine or reconstructed semantic form.");

            int[] areas = { 0x0012811C, 0x0012811D, 0x0012811E, 0x00128129, 0x0012812A, 0x0012812B };
            int[] resultFlags = { 0x001FD1, 0x001FCF, 0x001FCD, 0x001FD0, 0x001FCE, 0x001FCC };
            var instructions = new List<EMEVD.Instruction>
            {
                evt.Instructions[0],
                M12DlcBatchSetEventFlags(0x001FCC, 0x001FD1, 0),
            };

            for (int index = 0; index < areas.Length; index++)
            {
                sbyte group = (sbyte)(index + 1);
                instructions.Add(M12DlcIfMultiplayerState(group, 2));
                instructions.Add(M12DlcIfInoutsideArea(group, 1, 0x2710, areas[index]));
            }
            for (int index = 0; index < areas.Length; index++)
                instructions.Add(M12DlcIfConditionGroup(-1, 1, (sbyte)(index + 1)));
            instructions.Add(M12DlcIfConditionGroup(0, 1, -1));

            for (int index = 0; index < resultFlags.Length; index++)
            {
                instructions.Add(M12DlcSkipIfConditionGroup(1, 0, (sbyte)(index + 1)));
                instructions.Add(new EMEVD.Instruction(2003, 2,
                    new object[] { resultFlags[index], (byte)1 }));
            }

            evt.Instructions.Clear();
            evt.Instructions.AddRange(instructions);
            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "Event 11210839 reconstruction did not produce the pinned semantic form.");
        }

        /// <summary>Reconstructs the exact M12 DLC multiplayer area watcher and its parameter table.</summary>
        private static void ReconstructM12DlcEvent11210875(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11210875 has an unexpected rest behavior.");

            EMEVD.Instruction[] sourceInstructions =
            {
                new EMEVD.Instruction(3, 2, Convert.FromHexString("020100001027000054991200")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("0201000000000000")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("00010200")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000000000000")),
                new EMEVD.Instruction(2004, 47, Array.Empty<byte>()),
                new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000065000000")),
                new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000066000000")),
                new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000067000000")),
                new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000068000000")),
                new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000000")),
                new EMEVD.Instruction(1000, 5, Convert.FromHexString("01000000000000004E1B0000")),
                new EMEVD.Instruction(2007, 12, Convert.FromHexString("0000000001000000000000000A000000")),
                new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
            };
            EMEVD.Parameter[] sourceParameters =
            {
                new EMEVD.Parameter(1, 4, 0, 4),
                new EMEVD.Parameter(3, 0, 0, 4),
                new EMEVD.Parameter(10, 4, 0, 4),
                new EMEVD.Parameter(11, 0, 4, 4),
                new EMEVD.Parameter(11, 8, 8, 4),
            };
            EMEVD.Instruction[] targetInstructions =
            {
                new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
                new EMEVD.Instruction(3, 6, Convert.FromHexString("01020000")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700001F811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000020811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000021811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000022811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000023811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000024811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000025811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000026811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000027811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000028811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700002C811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700002D811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700002E811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700002F811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000030811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000031811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000032811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000033811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000034811200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000035811200")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FF00")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("2222AB0001000000")),
                new EMEVD.Instruction(2004, 15, Convert.FromHexString("1027000001000000")),
                new EMEVD.Instruction(2004, 8, Convert.FromHexString("10270000905F0100")),
                new EMEVD.Instruction(1001, 0, Convert.FromHexString("00008040")),
                new EMEVD.Instruction(2003, 8, Convert.FromHexString("7C10AB000000000001000000")),
                new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
            };
            EMEVD.Parameter[] targetParameters = Array.Empty<EMEVD.Parameter>();

            if (evt.Instructions.Count == targetInstructions.Length
                && evt.Instructions.Zip(targetInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && EventParametersMatch(evt, targetParameters)) return;

            Require(evt.Instructions.Count == sourceInstructions.Length
                && evt.Instructions.Zip(sourceInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210875 does not match its known pristine or reconstructed semantic form.");

            evt.Instructions.Clear();
            evt.Instructions.AddRange(targetInstructions);
            evt.Parameters.Clear();
            Require(evt.Instructions.Zip(targetInstructions)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))
                && EventParametersMatch(evt, targetParameters),
                "Event 11210875 reconstruction did not produce the pinned semantic form.");
        }

        /// <summary>Reconstructs the exact DLC multiplayer-area state watcher.</summary>
        private static void ReconstructM12DlcEvent11210100(EMEVD.Event evt)
        {
            const string sourceFingerprint = "a5a5e371722429ce7b72e784d8553b3e880ef2edb84fe8fcdfc977dcee738946";
            const string targetFingerprint = "05036f98a5c64b21fa06cef26dcae0c07b92051638aa5aaa8e140a6db38b45e8";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11210100 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event 11210100 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "Event 11210100 does not match its known pristine or reconstructed semantic form.");

            EMEVD.Instruction[] targetInstructions =
            {
                new EMEVD.Instruction(1003, 1, Convert.FromHexString("01000000750DAB00")),
                new EMEVD.Instruction(2005, 7, Convert.FromHexString("787A120000000000")),
                new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000750DAB00")),
                new EMEVD.Instruction(2005, 7, Convert.FromHexString("787A12000A000000")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000750DAB00")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("0101000010270000C57E1200")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000770DAB00")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("010000007421AB00")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("02010000760DAB00")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("02010000750DAB00")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("0201000010270000C47E1200")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("02010000770DAB00")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("020000007421AB00")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("03010000760DAB00")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("03000000750DAB00")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("0301000010270000C67E1200")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("03010000770DAB00")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("030000007421AB00")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("04010000750DAB00")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("0401000010270000C77E1200")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("04010000770DAB00")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("040000007421AB00")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010100")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010200")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010300")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010400")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("0001FF00")),
                new EMEVD.Instruction(1000, 9, Convert.FromHexString("00004040")),
                new EMEVD.Instruction(1001, 1, Convert.FromHexString("00000000")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("7421AB0001000000")),
                new EMEVD.Instruction(1000, 7, Convert.FromHexString("19010200")),
                new EMEVD.Instruction(1000, 7, Convert.FromHexString("18010400")),
                new EMEVD.Instruction(2006, 3, Convert.FromHexString("00000000797A120065000000DED40100")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("750DAB0001000000")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("760DAB0001000000")),
                new EMEVD.Instruction(2006, 4, Convert.FromHexString("787A1200BF000000DDD40100")),
                new EMEVD.Instruction(2003, 18, Convert.FromHexString("787A12000000000000000000")),
                new EMEVD.Instruction(1001, 1, Convert.FromHexString("B4000000")),
                new EMEVD.Instruction(2006, 5, Convert.FromHexString("787A120000000000")),
                new EMEVD.Instruction(1000, 7, Convert.FromHexString("08010100")),
                new EMEVD.Instruction(3, 7, Convert.FromHexString("FE000000C67E1200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("0501000010270000C77E1200")),
                new EMEVD.Instruction(3, 6, Convert.FromHexString("05000000")),
                new EMEVD.Instruction(1, 0, Convert.FromHexString("050000000000803F")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010500")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("0001FE00")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("7421AB0000000000")),
                new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
                new EMEVD.Instruction(3, 7, Convert.FromHexString("FE000000C47E1200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("0501000010270000C77E1200")),
                new EMEVD.Instruction(3, 6, Convert.FromHexString("05000000")),
                new EMEVD.Instruction(1, 0, Convert.FromHexString("050000000000803F")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010500")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("0001FE00")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("7421AB0000000000")),
                new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
                new EMEVD.Instruction(2006, 3, Convert.FromHexString("000000007A7A120065000000DED40100")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("750DAB0000000000")),
                new EMEVD.Instruction(2006, 4, Convert.FromHexString("787A1200BF000000DDD40100")),
                new EMEVD.Instruction(2003, 18, Convert.FromHexString("787A12000A00000000000000")),
                new EMEVD.Instruction(1001, 1, Convert.FromHexString("B4000000")),
                new EMEVD.Instruction(2006, 5, Convert.FromHexString("787A120000000000")),
                new EMEVD.Instruction(1000, 7, Convert.FromHexString("08010200")),
                new EMEVD.Instruction(3, 7, Convert.FromHexString("FD000000C77E1200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("0601000010270000C67E1200")),
                new EMEVD.Instruction(3, 6, Convert.FromHexString("06000000")),
                new EMEVD.Instruction(1, 0, Convert.FromHexString("060000000000803F")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FD010600")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("0001FD00")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("7421AB0000000000")),
                new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
                new EMEVD.Instruction(3, 7, Convert.FromHexString("FD000000C57E1200")),
                new EMEVD.Instruction(3, 2, Convert.FromHexString("0601000010270000C67E1200")),
                new EMEVD.Instruction(3, 6, Convert.FromHexString("06000000")),
                new EMEVD.Instruction(1, 0, Convert.FromHexString("060000000000803F")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FD010600")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("0001FD00")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("7421AB0000000000")),
                new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
            };
            evt.Instructions.Clear();
            evt.Instructions.AddRange(targetInstructions);
            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "Event 11210100 reconstruction did not produce the pinned semantic form.");
        }

        private static EMEVD.Instruction M12DlcBatchSetEventFlags(int startFlag, int endFlag, byte state) =>
            new EMEVD.Instruction(2003, 22, new object[] { startFlag, endFlag, state });

        /// <summary>Reconstructs the DLC area enemy-state watcher from its pinned reference body.</summary>
        private static void ReconstructM12DlcEvent11210830(EMEVD.Event evt)
        {
            const string sourceFingerprint = "ec2d871317fe3468f5ca932bf8b9e9908efe281cbdb23d765d2d74ee86f8a8e3";
            const string targetFingerprint = "e8e23af52cd498317d1ad76fc056e8c170fd67fccb3c09a81c50378dd4664c65";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11210830 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event 11210830 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "Event 11210830 does not match its known pristine or reconstructed semantic form.");

            EMEVD.Instruction[] targetInstructions =
            {
                InsHex(3, 2, "01010000102700002B811200"),
                InsHex(3, 1, "010000000022AB000322AB00"),
                InsHex(3, 2, "02010000102700001E811200"),
                InsHex(3, 1, "020000000022AB000322AB00"),
                InsHex(3, 2, "03010000102700002A811200"),
                InsHex(3, 1, "030000000022AB000322AB00"),
                InsHex(3, 2, "04010000102700001D811200"),
                InsHex(3, 1, "040000000022AB000322AB00"),
                InsHex(3, 2, "050100001027000029811200"),
                InsHex(3, 0, "050100000022AB00"),
                InsHex(3, 0, "050100000222AB00"),
                InsHex(3, 2, "06010000102700001C811200"),
                InsHex(3, 0, "060100000022AB00"),
                InsHex(3, 0, "060100000222AB00"),
                InsHex(0, 0, "FF010100"),
                InsHex(0, 0, "FF010200"),
                InsHex(0, 0, "FF010300"),
                InsHex(0, 0, "FF010400"),
                InsHex(0, 0, "FF010500"),
                InsHex(0, 0, "FF010600"),
                InsHex(0, 0, "0001FF00"),
                InsHex(2003, 2, "EC21AB0001000000"),
                InsHex(1000, 7, "02010300"),
                InsHex(1000, 7, "01010400"),
                InsHex(1000, 3, "01000000"),
                InsHex(2003, 2, "2022AB0001000000"),
                InsHex(1000, 7, "02010100"),
                InsHex(1000, 7, "01010200"),
                InsHex(1000, 3, "01000000"),
                InsHex(2003, 2, "2322AB0001000000"),
                InsHex(1001, 0, "00000041"),
                InsHex(1003, 1, "030100002322AB00"),
                InsHex(1003, 1, "020100002022AB00"),
                InsHex(2007, 4, "C74A020001000000"),
                InsHex(1000, 3, "01000000"),
                InsHex(2007, 4, "BD4A020001000000"),
                InsHex(1001, 0, "00000040"),
                InsHex(2003, 2, "1E22AB0001000000"),
                InsHex(3, 0, "000000001E22AB00"),
                InsHex(1000, 4, "01000000"),
            };
            evt.Instructions.Clear();
            evt.Instructions.AddRange(targetInstructions);
            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "Event 11210830 reconstruction did not produce the pinned semantic form.");
        }

        /// <summary>Reconstructs the DLC cat-guidance event and its exact parameter bindings.</summary>
        private static void ReconstructM12DlcEvent11210870(EMEVD.Event evt)
        {
            const string sourceFingerprint = "3cf72efe0b01a54c86c33118988c5d817a71eada50137611782b8d0bb88d506d";
            const string targetFingerprint = "2e6d5c20cf3241a29ebb9a8ada371ba40e7398413a2b843a33d0b2f19cf30f8b";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11210870 has an unexpected rest behavior.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "Event 11210870 does not match its known pristine or reconstructed semantic form.");

            EMEVD.Instruction[] targetInstructions =
            {
                new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
                new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001F22AB00")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
                new EMEVD.Instruction(2004, 15, Convert.FromHexString("1027000001000000")),
                new EMEVD.Instruction(2004, 8, Convert.FromHexString("10270000905F0100")),
                new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000C040")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("2522AB0001000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0200000000000000060300000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0200000000000000060300000000000006000000")),
                new EMEVD.Instruction(1003, 1, Convert.FromHexString("010100002022AB00")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0200000000000000060300000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("FF00000000000000060200000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("FF00000000000000060200000000000006000000")),
                new EMEVD.Instruction(1003, 1, Convert.FromHexString("010100002022AB00")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("FF00000000000000060200000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0300000000000000060000000000000006000000")),
                new EMEVD.Instruction(1003, 1, Convert.FromHexString("080100002022AB00")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0300000000000000060300000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0300000000000000060300000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0400000000000000060300000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0400000000000000060000000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0400000000000000060300000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0500000000000000060300000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0500000000000000060300000000000006000000")),
                new EMEVD.Instruction(3, 20, Convert.FromHexString("0500000000000000060000000000000006000000")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010300")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010400")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010500")),
                new EMEVD.Instruction(1000, 1, Convert.FromHexString("03010200")),
                new EMEVD.Instruction(1000, 1, Convert.FromHexString("0B01FF00")),
                new EMEVD.Instruction(1000, 1, Convert.FromHexString("0601FE00")),
                new EMEVD.Instruction(1000, 3, Convert.FromHexString("05000000")),
                new EMEVD.Instruction(2003, 2, Convert.FromHexString("2122AB0001000000")),
                new EMEVD.Instruction(2007, 2, Convert.FromHexString("0F000000")),
                new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000040")),
                new EMEVD.Instruction(2007, 4, Convert.FromHexString("224A020001000000")),
                new EMEVD.Instruction(1000, 3, Convert.FromHexString("1D000000")),
                new EMEVD.Instruction(2007, 2, Convert.FromHexString("11000000")),
                new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000040")),
                new EMEVD.Instruction(2007, 4, Convert.FromHexString("274A020001000000")),
                new EMEVD.Instruction(1000, 3, Convert.FromHexString("12000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("581B00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("8A1B00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("BC1B00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("EE1B00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("201C00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("521C00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("841C00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("B61C00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("E81C00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("1A1D00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("4C1D00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("7E1D00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("B01D00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("E21D00000A000000")),
                new EMEVD.Instruction(2003, 32, Convert.FromHexString("141E00000A000000")),
                new EMEVD.Instruction(2007, 2, Convert.FromHexString("10000000")),
                new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000040")),
                new EMEVD.Instruction(2007, 4, Convert.FromHexString("2C4A020001000000")),
                new EMEVD.Instruction(1003, 1, Convert.FromHexString("020000002022AB00")),
                new EMEVD.Instruction(2003, 38, Array.Empty<byte>()),
                new EMEVD.Instruction(1000, 3, Convert.FromHexString("04000000")),
                new EMEVD.Instruction(1003, 1, Convert.FromHexString("020000002322AB00")),
                new EMEVD.Instruction(2003, 39, Array.Empty<byte>()),
                new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
                new EMEVD.Instruction(2003, 37, Array.Empty<byte>()),
                new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000041")),
                new EMEVD.Instruction(2004, 15, Convert.FromHexString("1027000000000000")),
                new EMEVD.Instruction(2004, 21, Convert.FromHexString("10270000905F0100")),
                new EMEVD.Instruction(2003, 40, Array.Empty<byte>()),
            };
            EMEVD.Parameter[] targetParameters =
            {
                new EMEVD.Parameter(1, 4, 0, 4) { UnkID = 0 },
                new EMEVD.Parameter(9, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(10, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(12, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(13, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(14, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(16, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(17, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(19, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(20, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(21, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(22, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(23, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(24, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(25, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(26, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(9, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(13, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(17, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(21, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(24, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(10, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(14, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(19, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(22, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(25, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(12, 12, 16, 4) { UnkID = 0 },
                new EMEVD.Parameter(16, 12, 16, 4) { UnkID = 0 },
                new EMEVD.Parameter(20, 12, 16, 4) { UnkID = 0 },
                new EMEVD.Parameter(23, 12, 16, 4) { UnkID = 0 },
                new EMEVD.Parameter(26, 12, 16, 4) { UnkID = 0 },
            };

            evt.Instructions.Clear();
            evt.Instructions.AddRange(targetInstructions);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(targetParameters);
            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "Event 11210870 reconstruction did not produce the pinned semantic form.");
        }

        /// <summary>Reconstructs the DLC seven-state guidance watcher and its parameter table.</summary>
        private static void ReconstructM12DlcEvent11210435(EMEVD.Event evt)
        {
            const string sourceFingerprint = "4f70fb5e0397efefa386d28e3e9bf6f1429f9f08ffc175382f8e52a247232041";
            const string targetFingerprint = "2593e804e39e8a9f3694587b6379dbc75706bc9ff8833abc362206f9441f72f1";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11210435 has an unexpected rest behavior.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "Event 11210435 does not match its known pristine or reconstructed semantic form.");

            EMEVD.Instruction[] targetInstructions =
            {
                InsHex(3, 0, "010100001E22AB00"), InsHex(3, 0, "020100001E22AB00"),
                InsHex(3, 0, "030100001E22AB00"), InsHex(3, 0, "040100001E22AB00"),
                InsHex(3, 0, "050100001E22AB00"), InsHex(3, 0, "060100001E22AB00"),
                InsHex(3, 0, "070100001E22AB00"), InsHex(3, 0, "0101000000000000"),
                InsHex(3, 0, "0201000000000000"), InsHex(3, 0, "0301000000000000"),
                InsHex(3, 0, "0401000000000000"), InsHex(3, 0, "0501000000000000"),
                InsHex(3, 0, "0601000000000000"), InsHex(3, 0, "0701000000000000"),
                InsHex(3, 0, "010100002322AB00"), InsHex(3, 0, "020100002322AB00"),
                InsHex(3, 0, "030100002322AB00"), InsHex(3, 0, "040100002322AB00"),
                InsHex(3, 0, "050100002322AB00"), InsHex(3, 0, "060100002322AB00"),
                InsHex(3, 0, "070100002322AB00"), InsHex(3, 0, "FF0100002722AB00"),
                InsHex(3, 0, "FF0100002522AB00"), InsHex(0, 0, "0101FF00"),
                InsHex(0, 0, "0201FF00"), InsHex(0, 0, "0301FF00"),
                InsHex(0, 0, "0401FF00"), InsHex(0, 0, "0501FF00"),
                InsHex(0, 0, "0601FF00"), InsHex(0, 0, "0701FF00"),
                InsHex(3, 0, "FE0000002422AB00"), InsHex(3, 0, "FE0100002522AB00"),
                InsHex(0, 0, "0101FE00"), InsHex(0, 0, "0201FE00"),
                InsHex(0, 0, "0301FE00"), InsHex(0, 0, "0401FE00"),
                InsHex(0, 0, "0501FE00"), InsHex(0, 0, "0601FE00"),
                InsHex(0, 0, "0701FE00"),
                InsHex(3, 20, "0100000000000000060000000000000006000000"),
                InsHex(3, 20, "0100000000000000060000000000000006000000"),
                InsHex(3, 20, "0100000000000000060000000000000006000000"),
                InsHex(3, 20, "0200000000000000060000000000000006000000"),
                InsHex(3, 20, "0200000000000000060300000000000006000000"),
                InsHex(3, 20, "0200000000000000060300000000000006000000"),
                InsHex(3, 20, "0300000000000000060300000000000006000000"),
                InsHex(3, 20, "0300000000000000060000000000000006000000"),
                InsHex(3, 20, "0300000000000000060300000000000006000000"),
                InsHex(3, 20, "0400000000000000060300000000000006000000"),
                InsHex(3, 20, "0400000000000000060300000000000006000000"),
                InsHex(3, 20, "0400000000000000060000000000000006000000"),
                InsHex(3, 20, "0500000000000000060000000000000006000000"),
                InsHex(3, 20, "0500000000000000060000000000000006000000"),
                InsHex(3, 20, "0500000000000000060300000000000006000000"),
                InsHex(3, 20, "0600000000000000060300000000000006000000"),
                InsHex(3, 20, "0600000000000000060000000000000006000000"),
                InsHex(3, 20, "0600000000000000060000000000000006000000"),
                InsHex(3, 20, "0700000000000000060000000000000006000000"),
                InsHex(3, 20, "0700000000000000060300000000000006000000"),
                InsHex(3, 20, "0700000000000000060000000000000006000000"),
                InsHex(0, 0, "F9010100"), InsHex(0, 0, "F9010200"),
                InsHex(0, 0, "F9010300"), InsHex(0, 0, "F9010400"),
                InsHex(0, 0, "F9010400"), InsHex(0, 0, "F9010500"),
                InsHex(0, 0, "F9010600"), InsHex(0, 0, "F9010700"),
                InsHex(0, 0, "0001F900"), InsHex(2004, 8, "1027000006120000"),
                InsHex(1001, 0, "0000A040"), InsHex(1000, 4, "01000000"),
            };
            EMEVD.Parameter[] targetParameters =
            {
                new EMEVD.Parameter(7, 4, 0, 4) { UnkID = 0 },
                new EMEVD.Parameter(8, 4, 0, 4) { UnkID = 0 },
                new EMEVD.Parameter(9, 4, 0, 4) { UnkID = 0 },
                new EMEVD.Parameter(10, 4, 0, 4) { UnkID = 0 },
                new EMEVD.Parameter(11, 4, 0, 4) { UnkID = 0 },
                new EMEVD.Parameter(12, 4, 0, 4) { UnkID = 0 },
                new EMEVD.Parameter(13, 4, 0, 4) { UnkID = 0 },
                new EMEVD.Parameter(39, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(40, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(41, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(42, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(43, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(44, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(45, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(46, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(47, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(48, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(49, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(50, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(51, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(52, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(53, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(54, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(55, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(56, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(57, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(58, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(59, 4, 4, 4) { UnkID = 0 },
                new EMEVD.Parameter(39, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(42, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(45, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(48, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(51, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(54, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(57, 12, 8, 4) { UnkID = 0 },
                new EMEVD.Parameter(40, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(43, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(46, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(49, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(52, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(55, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(58, 12, 12, 4) { UnkID = 0 },
                new EMEVD.Parameter(41, 12, 16, 4) { UnkID = 0 },
                new EMEVD.Parameter(44, 12, 16, 4) { UnkID = 0 },
                new EMEVD.Parameter(47, 12, 16, 4) { UnkID = 0 },
                new EMEVD.Parameter(50, 12, 16, 4) { UnkID = 0 },
                new EMEVD.Parameter(53, 12, 16, 4) { UnkID = 0 },
                new EMEVD.Parameter(56, 12, 16, 4) { UnkID = 0 },
                new EMEVD.Parameter(59, 12, 16, 4) { UnkID = 0 },
            };
            evt.Instructions.Clear();
            evt.Instructions.AddRange(targetInstructions);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(targetParameters);
            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "Event 11210435 reconstruction did not produce the pinned semantic form.");
        }

        private static EMEVD.Instruction M12DlcIfMultiplayerState(sbyte group, sbyte state) =>
            new EMEVD.Instruction(3, 6, new object[] { group, state });

        private static EMEVD.Instruction M12DlcIfInoutsideArea(sbyte group, byte state, int targetEntity,
            int areaEntity) =>
            new EMEVD.Instruction(3, 2, new object[] { group, state, targetEntity, areaEntity });

        private static EMEVD.Instruction M12DlcIfConditionGroup(sbyte group, byte state, sbyte targetGroup) =>
            new EMEVD.Instruction(0, 0, new object[] { group, state, targetGroup });

        private static EMEVD.Instruction M12DlcSkipIfConditionGroup(byte skip, byte state, sbyte group) =>
            new EMEVD.Instruction(1000, 7, new object[] { skip, state, group });

        private static string[][] M12DlcTwoFlagStateConditionGroups(string firstFlagHex, string secondFlagHex) =>
            Enumerable.Range(1, 4).Select(state => new[]
            {
                state.ToString("X2") + "010000" + firstFlagHex,
                state.ToString("X2") + "000000" + secondFlagHex,
            }).ToArray();

        /// <summary>Reconstructs the per-state flag guards and zero-delay setup in one DLC state watcher.</summary>
        private static void ReconstructM12DlcStateWatcher(EMEVD.Event evt, int sourceInstructionCount,
            string stateFlagHex, string[] stateConditionHex)
        {
            string[] removedConditions = { "00000000" + stateFlagHex };
            string[][] stateConditions = Enumerable.Range(1, 4)
                .Select(state => new[] { state.ToString("X2") + "000000" + stateFlagHex }).ToArray();
            ReconstructM12DlcStateWatcher(evt, sourceInstructionCount, removedConditions,
                stateConditionHex, stateConditions);
        }

        private static void ReconstructM12DlcStateWatcher(EMEVD.Event evt, int sourceInstructionCount,
            string[] removedConditionHex, string[] stateConditionHex, string[][] stateConditionGroups)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] removedConditions = removedConditionHex
                .Select(hex => new EMEVD.Instruction(3, 0, Convert.FromHexString(hex))).ToArray();
            var anchors = stateConditionHex.Select(hex => new EMEVD.Instruction(3, 2, Convert.FromHexString(hex))).ToArray();
            EMEVD.Instruction[][] stateConditions = stateConditionGroups
                .Select(group => group.Select(hex => new EMEVD.Instruction(3, 0, Convert.FromHexString(hex))).ToArray())
                .ToArray();
            Require(anchors.Length == stateConditions.Length,
                "Event " + evt.ID + " has a mismatched number of state anchors and condition groups.");
            var zeroDelay = new EMEVD.Instruction(1001, 1, Convert.FromHexString("00000000"));
            var delayAnchor = new EMEVD.Instruction(1000, 9, Convert.FromHexString("00004040"));

            int targetInstructionCount = sourceInstructionCount - removedConditions.Length
                + stateConditions.Sum(group => group.Length) + 1;
            if (IsM12DlcStateWatcherReconstructed(evt, targetInstructionCount,
                removedConditions, stateConditions, anchors, delayAnchor, zeroDelay)) return;

            Require(evt.Instructions.Count == sourceInstructionCount
                && removedConditions.All(removed => evt.Instructions.Count(instruction => EmevdMerge.SameInstruction(instruction, removed)) == 1)
                && evt.Instructions.Count(instruction => EmevdMerge.SameInstruction(instruction, delayAnchor)) == 1,
                "Event " + evt.ID + " does not match its known pristine state-watcher shape.");
            foreach (EMEVD.Instruction anchor in anchors)
                Require(evt.Instructions.Count(instruction => EmevdMerge.SameInstruction(instruction, anchor)) == 1,
                    "Event " + evt.ID + " has a missing or duplicate state-condition anchor.");
            foreach (EMEVD.Instruction condition in stateConditions.SelectMany(group => group))
                Require(evt.Instructions.All(instruction => !EmevdMerge.SameInstruction(instruction, condition)),
                    "Event " + evt.ID + " already contains a reconstructed state condition in an unexpected position.");

            var reconstructed = new List<EMEVD.Instruction>(sourceInstructionCount + 4);
            foreach (EMEVD.Instruction instruction in evt.Instructions)
            {
                if (removedConditions.Any(removed => EmevdMerge.SameInstruction(instruction, removed))) continue;
                reconstructed.Add(instruction);
                for (int i = 0; i < anchors.Length; i++)
                    if (EmevdMerge.SameInstruction(instruction, anchors[i])) reconstructed.AddRange(stateConditions[i]);
                if (EmevdMerge.SameInstruction(instruction, delayAnchor)) reconstructed.Add(zeroDelay);
            }

            Require(IsM12DlcStateWatcherReconstructed(evt, targetInstructionCount,
                removedConditions, stateConditions, anchors, delayAnchor, zeroDelay, reconstructed),
                "Event " + evt.ID + " could not be reconstructed to its known state-watcher shape.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructed);
        }

        private static bool IsM12DlcStateWatcherReconstructed(EMEVD.Event evt, int targetInstructionCount,
            EMEVD.Instruction[] removedConditions, EMEVD.Instruction[][] stateConditions,
            EMEVD.Instruction[] anchors, EMEVD.Instruction delayAnchor, EMEVD.Instruction zeroDelay,
            IReadOnlyList<EMEVD.Instruction> candidate = null)
        {
            IReadOnlyList<EMEVD.Instruction> instructions = candidate ?? evt.Instructions;
            if (instructions.Count != targetInstructionCount
                || instructions.Any(instruction => removedConditions.Any(removed => EmevdMerge.SameInstruction(instruction, removed)))) return false;
            for (int i = 0; i < anchors.Length; i++)
            {
                int anchorIndex = instructions.ToList().FindIndex(instruction => EmevdMerge.SameInstruction(instruction, anchors[i]));
                if (anchorIndex < 0 || anchorIndex + stateConditions[i].Length >= instructions.Count) return false;
                for (int conditionIndex = 0; conditionIndex < stateConditions[i].Length; conditionIndex++)
                    if (!EmevdMerge.SameInstruction(instructions[anchorIndex + conditionIndex + 1], stateConditions[i][conditionIndex])
                        || instructions.Count(instruction => EmevdMerge.SameInstruction(instruction, stateConditions[i][conditionIndex])) != 1)
                        return false;
            }
            int delayIndex = instructions.ToList().FindIndex(instruction => EmevdMerge.SameInstruction(instruction, delayAnchor));
            return delayIndex >= 0 && delayIndex + 1 < instructions.Count
                && EmevdMerge.SameInstruction(instructions[delayIndex + 1], zeroDelay)
                && instructions.Count(instruction => EmevdMerge.SameInstruction(instruction, zeroDelay)) == 1;
        }

        /// <summary>Reconstructs the exact reference conditions and sound operations for the DLC area event.</summary>
        private static void ReconstructM12DlcEvent11210836(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M12DlcEvent11210836SourceBody();
            EMEVD.Instruction[] reconstructedBody = M12DlcEvent11210836ReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11210836 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210836SourceBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("000100002422AB00")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("2422AB0001000000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("5E4A020000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("000100001F22AB00")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("1F22AB0001000000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("1C4B020000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("000000001F22AB00")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210836ReconstructedBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("000100002422AB00")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("2422AB0001000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("030100002322AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("020100002022AB00")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("634A020000000000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("594A020000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("000100001F22AB00")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("1F22AB0001000000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("1C4B020000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("000000001F22AB00")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        /// <summary>Reconstructs the two DLC message checks and their parameter bindings.</summary>
        private static void ReconstructM12DlcEvent11210886(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210886SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210886ReferenceBody();
            EMEVD.Parameter[] sourceParameters = M12DlcEvent11210886SourceParameters();
            EMEVD.Parameter[] referenceParameters = M12DlcEvent11210886ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210886 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        /// <summary>Reconstructs the exact DLC event check and its event-parameter bindings.</summary>
        private static void ReconstructM12DlcEvent11210831(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210831SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210831ReferenceBody();
            EMEVD.Parameter[] sourceParameters = M12DlcEvent11210831SourceParameters();
            EMEVD.Parameter[] referenceParameters = M12DlcEvent11210831ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210831 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        /// <summary>Reconstructs the DLC event's exact flag checks and parameter reindexing.</summary>
        private static void ReconstructM12DlcEvent11210404(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210404SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210404ReferenceBody();
            EMEVD.Parameter[] sourceParameters = M12DlcEvent11210404SourceParameters();
            EMEVD.Parameter[] referenceParameters = M12DlcEvent11210404ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210404 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210404SourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("06010000EC21AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0601000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0601000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010600")),
            new EMEVD.Instruction(3, 12, Convert.FromHexString("01000000000000000A0300000A000000")),
            new EMEVD.Instruction(3, 12, Convert.FromHexString("02000000000000000A0400000A000000")),
            new EMEVD.Instruction(3, 12, Convert.FromHexString("02000000000000000A0300001E000000")),
            new EMEVD.Instruction(3, 12, Convert.FromHexString("03000000000000000A0400001E000000")),
            new EMEVD.Instruction(3, 12, Convert.FromHexString("03000000000000000A03000032000000")),
            new EMEVD.Instruction(3, 12, Convert.FromHexString("04000000000000000A04000032000000")),
            new EMEVD.Instruction(3, 12, Convert.FromHexString("04000000000000000A03000064000000")),
            new EMEVD.Instruction(3, 12, Convert.FromHexString("05000000000000000A04000064000000")),
            new EMEVD.Instruction(1000, 1, Convert.FromHexString("01000200")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("EE21AB0001000000")),
            new EMEVD.Instruction(1000, 1, Convert.FromHexString("01000300")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("EF21AB0001000000")),
            new EMEVD.Instruction(1000, 1, Convert.FromHexString("01000400")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("F021AB0001000000")),
            new EMEVD.Instruction(1000, 1, Convert.FromHexString("01000500")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("F121AB0001000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210404ReferenceBody()
        {
            EMEVD.Instruction[] source = M12DlcEvent11210404SourceBody();
            return new[]
            {
                source[0],
                new EMEVD.Instruction(3, 0, Convert.FromHexString("00010000EC21AB00")),
            }.Concat(source.Skip(5)).ToArray();
        }

        private static EMEVD.Parameter[] M12DlcEvent11210404SourceParameters() => new[]
        {
            new EMEVD.Parameter(5, 4, 0, 4),
            new EMEVD.Parameter(6, 4, 0, 4),
            new EMEVD.Parameter(7, 4, 0, 4),
            new EMEVD.Parameter(8, 4, 0, 4),
            new EMEVD.Parameter(9, 4, 0, 4),
            new EMEVD.Parameter(10, 4, 0, 4),
            new EMEVD.Parameter(11, 4, 0, 4),
            new EMEVD.Parameter(12, 4, 0, 4),
            new EMEVD.Parameter(2, 4, 4, 4),
            new EMEVD.Parameter(3, 4, 8, 4),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210404ReferenceParameters() => new[]
        {
            new EMEVD.Parameter(2, 4, 0, 4),
            new EMEVD.Parameter(3, 4, 0, 4),
            new EMEVD.Parameter(4, 4, 0, 4),
            new EMEVD.Parameter(5, 4, 0, 4),
            new EMEVD.Parameter(6, 4, 0, 4),
            new EMEVD.Parameter(7, 4, 0, 4),
            new EMEVD.Parameter(8, 4, 0, 4),
            new EMEVD.Parameter(9, 4, 0, 4),
        };

        /// <summary>Reconstructs the exact DLC region checks and merged condition parameter bindings.</summary>
        private static void ReconstructM12DlcEvent11210430(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210430SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210430ReferenceBody();
            EMEVD.Parameter[] sourceParameters = M12DlcEvent11210430SourceParameters();
            EMEVD.Parameter[] referenceParameters = M12DlcEvent11210430ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210430 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210430SourceBody() => new[]
        {
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01020000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF0000002422AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF0100002522AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FF00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010000001324AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("020000002322AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("020000002D22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("020100003022AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0201000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0201000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FC0100002022AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FC0100002A22AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0301FC00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0301000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0301000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010300")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010200")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FE00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FD0100002722AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FD0100002522AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FD00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000006120000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000A040")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210430ReferenceBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF0000002422AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF0100002522AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FF00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("020000002322AB00")),
            new EMEVD.Instruction(3, 20, Convert.FromHexString("0200000000000000060000000000000006000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("01010200")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FD0100002722AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FD0100002522AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FD00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000006120000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000A040")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210430SourceParameters() => new[]
        {
            new EMEVD.Parameter(6, 4, 0, 4),
            new EMEVD.Parameter(11, 4, 4, 4),
            new EMEVD.Parameter(12, 4, 8, 4),
            new EMEVD.Parameter(16, 4, 12, 4),
            new EMEVD.Parameter(17, 4, 16, 4),
            new EMEVD.Parameter(7, 4, 20, 4),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210430ReferenceParameters() => new[]
        {
            new EMEVD.Parameter(4, 4, 0, 4),
            new EMEVD.Parameter(6, 4, 4, 4),
            new EMEVD.Parameter(6, 12, 8, 4),
        };

        /// <summary>Reconstructs the exact DLC progression event and its parameterized condition checks.</summary>
        private static void ReconstructM12DlcEvent11210701(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210701SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210701ReferenceBody();
            EMEVD.Parameter[] sourceParameters = M12DlcEvent11210701SourceParameters();
            EMEVD.Parameter[] referenceParameters = M12DlcEvent11210701ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210701 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210701SourceBody() => new[]
        {
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01020000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF0000002422AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF0100002522AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FF00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010000001324AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("020000003022AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("020000002322AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("020000002D22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0200000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0201000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FB0100002322AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FB0100002D22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FB0100003022AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0301FB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0301000000000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000F621AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("03000000AC2DAB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000F821AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("03000000AE2DAB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0A0100003022AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000F721AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("03000000AD2DAB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000F921AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("03000000AF2DAB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("050100003022AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("040100002322AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000FA21AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("03000000B02DAB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000FB21AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("03000000B12DAB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010200")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010300")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FE00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("04010000EE21AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("05010000EF21AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("06010000F021AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("07010000F121AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01000400")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000007120000")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01000500")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000008120000")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01000600")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000009120000")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01000700")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("102700000A120000")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("04010400")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("03010500")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("02010600")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01010700")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000004120000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000A040")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210701ReferenceBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF0000002422AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF0100002522AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FF00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("020000002322AB00")),
            new EMEVD.Instruction(3, 20, Convert.FromHexString("0200000000000000060300000000000006000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("030100002322AB00")),
            new EMEVD.Instruction(3, 20, Convert.FromHexString("0300000000000000060300000000000006000000")),
            new EMEVD.Instruction(3, 20, Convert.FromHexString("0300000000000000060300000000000006000000")),
            new EMEVD.Instruction(3, 20, Convert.FromHexString("0300000000000000060300000000000006000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010200")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FE010300")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FE00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("04010000EE21AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("05010000EF21AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("06010000F021AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("07010000F121AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01000400")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000007120000")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01000500")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000008120000")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01000600")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000009120000")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01000700")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("102700000A120000")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("04010400")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("03010500")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("02010600")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01010700")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000004120000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000A040")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210701SourceParameters() => new[]
        {
            new EMEVD.Parameter(6, 4, 0, 4),
            new EMEVD.Parameter(17, 4, 4, 4),
            new EMEVD.Parameter(12, 4, 8, 4),
            new EMEVD.Parameter(11, 4, 12, 4),
            new EMEVD.Parameter(7, 4, 16, 4),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210701ReferenceParameters() => new[]
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

        /// <summary>Reconstructs the exact DLC watcher branch and its two parameter bindings.</summary>
        private static void ReconstructM12DlcEvent11210890(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210890SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210890ReferenceBody();
            EMEVD.Parameter[] sourceParameters = M12DlcEvent11210890SourceParameters();
            EMEVD.Parameter[] referenceParameters = M12DlcEvent11210890ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210890 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210890SourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010000001F22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000D21F0000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01030000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 21, Convert.FromHexString("1027000004120000")),
            new EMEVD.Instruction(2004, 21, Convert.FromHexString("1027000006120000")),
            new EMEVD.Instruction(2004, 21, Convert.FromHexString("1027000007120000")),
            new EMEVD.Instruction(2004, 21, Convert.FromHexString("1027000008120000")),
            new EMEVD.Instruction(2004, 21, Convert.FromHexString("1027000009120000")),
            new EMEVD.Instruction(2004, 21, Convert.FromHexString("102700000A120000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("03010000E12CAB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("02010000CC1F0000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000F621AB00")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("184A020001000000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(2003, 40, Array.Empty<byte>()),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210890ReferenceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01030000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("010100001027000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(2003, 14, Convert.FromHexString("0C010000FFFFFFFF")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210890SourceParameters() => Array.Empty<EMEVD.Parameter>();

        private static EMEVD.Parameter[] M12DlcEvent11210890ReferenceParameters() => new[]
        {
            new EMEVD.Parameter(3, 8, 0, 4),
            new EMEVD.Parameter(6, 4, 4, 4),
        };

        /// <summary>Reconstructs the exact DLC event activation and cleanup sequence.</summary>
        private static void ReconstructM12DlcEvent11210849(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210849SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210849ReferenceBody();
            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11210849 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210849SourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(4, 5, Convert.FromHexString("00000000102700001100000001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("1324AB0001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("D21F000001000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1000, 1, Convert.FromHexString("02010100")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("D01F000001000000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("D11F000001000000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("0F4A020001000000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000803F")),
            new EMEVD.Instruction(2003, 22, Convert.FromHexString("581B0000451E000000000000")),
            new EMEVD.Instruction(2004, 52, Array.Empty<byte>()),
            new EMEVD.Instruction(2003, 40, Array.Empty<byte>()),
            new EMEVD.Instruction(2004, 47, Array.Empty<byte>()),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(2003, 14, Convert.FromHexString("0C01000062961200")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210849ReferenceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(4, 5, Convert.FromHexString("00000000102700001100000001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("D21F000001000000")),
            new EMEVD.Instruction(1003, 5, Convert.FromHexString("02010000")),
            new EMEVD.Instruction(2007, 9, Convert.FromHexString("0E4A0200")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2007, 9, Convert.FromHexString("0F4A0200")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000803F")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("040100001E22AB00")),
            new EMEVD.Instruction(2003, 40, Array.Empty<byte>()),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(2003, 14, Convert.FromHexString("0C01000062961200")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2003, 40, Array.Empty<byte>()),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        /// <summary>Removes the exact duplicate DLC checks and reindexes their event parameters.</summary>
        /// <summary>Reconstructs the exact DLC item-event body and its parameter bindings.</summary>
        private static void ReconstructM12DlcEvent11210840(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210840SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210840ReferenceBody();
            EMEVD.Parameter[] sourceParameters = M12DlcEvent11210840SourceParameters();
            EMEVD.Parameter[] referenceParameters = M12DlcEvent11210840ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210840 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210840SourceBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010000001F22AB00")),
            new EMEVD.Instruction(4, 14, Convert.FromHexString("01000000102700000000000001000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 15, Convert.FromHexString("1027000001000000")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("10270000915F0100")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000065000000")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000066000000")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000067000000")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000068000000")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000003120000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0100000000000000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("FBB8070003000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000001000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(2004, 15, Convert.FromHexString("1027000000000000")),
            new EMEVD.Instruction(2004, 21, Convert.FromHexString("1027000003120000")),
            new EMEVD.Instruction(2004, 43, Convert.FromHexString("1027000001000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0201000000000000")),
            new EMEVD.Instruction(2003, 44, Convert.FromHexString("10270000271B000000010000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2003, 44, Convert.FromHexString("10270000271B000000010000")),
            new EMEVD.Instruction(2004, 43, Convert.FromHexString("1027000001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210840ReferenceBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010000001F22AB00")),
            new EMEVD.Instruction(4, 14, Convert.FromHexString("01000000102700000000000001000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 15, Convert.FromHexString("1027000001000000")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000065000000")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000066000000")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000067000000")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000068000000")),
            new EMEVD.Instruction(2004, 8, Convert.FromHexString("1027000003120000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0201000000000000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("0000000001000000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("074A020001000000")),
            new EMEVD.Instruction(2003, 31, Convert.FromHexString("00000000060000003F000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("010000002022AB00")),
            new EMEVD.Instruction(2003, 31, Convert.FromHexString("00000000060000003F000000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000001000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(2004, 15, Convert.FromHexString("1027000000000000")),
            new EMEVD.Instruction(2004, 21, Convert.FromHexString("1027000003120000")),
            new EMEVD.Instruction(2004, 43, Convert.FromHexString("1027000001000000")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000261B000000010000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210840SourceParameters() => new[]
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
        };

        private static EMEVD.Parameter[] M12DlcEvent11210840ReferenceParameters() => new[]
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

        /// <summary>Reconstructs the exact DLC area-condition groups from the pinned reference.</summary>
        private static void ReconstructM12DlcEvent11210434(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210434SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210434ReferenceBody();
            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11210434 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210434SourceBody() => new[]
        {
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("2722AB0001000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100003022AB00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FE020000C421AB00C921AB00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FE020000D021AB00D521AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FE00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("020100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF0100002022AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF0100002322AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0201FF00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FD020000C421AB00C921AB00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FD020000CA21AB00CF21AB00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FD020000D021AB00D521AB00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FD020000D621AB00DB21AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0201FD00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("030100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FB0100002A22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FB0100002D22AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0301FF00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FB020000C421AB00C921AB00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FB020000CA21AB00CF21AB00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FB020000D021AB00D521AB00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FB020000D621AB00DB21AB00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FB020000DC21AB00E121AB00")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("FB020000E221AB00E721AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0301FB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("F9010100")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("F9010200")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("F9010300")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0001F900")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("2722AB0001000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210434ReferenceBody()
        {
            EMEVD.Instruction[] source = M12DlcEvent11210434SourceBody();
            return source.Take(3)
                .Concat(new[]
                {
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("010000002022AB00")),
                    new EMEVD.Instruction(3, 0, Convert.FromHexString("010000002322AB00")),
                })
                .Concat(source.Skip(4).Take(12))
                .Concat(source.Skip(27).Take(2))
                .Concat(source.Skip(30)).ToArray();
        }

        /// <summary>Reconstructs the exact DLC flag watcher and its event parameter bindings.</summary>
        private static void ReconstructM12DlcEvent11210800(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210800SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210800ReferenceBody();
            EMEVD.Parameter[] sourceParameters = M12DlcEvent11210800SourceParameters();
            EMEVD.Parameter[] referenceParameters = M12DlcEvent11210800ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210800 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210800SourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("010100001027000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010000001E22AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2008, 4, Array.Empty<byte>()),
            new EMEVD.Instruction(2003, 22, Convert.FromHexString("F621AB00FB21AB0000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000001000000")),
            new EMEVD.Instruction(2004, 1, Convert.FromHexString("1027000000000000")),
            new EMEVD.Instruction(2004, 50, Array.Empty<byte>()),
            new EMEVD.Instruction(2004, 47, Array.Empty<byte>()),
            new EMEVD.Instruction(2004, 51, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210800ReferenceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("000100001027000000000000")),
            new EMEVD.Instruction(2003, 22, Convert.FromHexString("F621AB00F921AB0000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000001000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210800SourceParameters() => new[]
        {
            new EMEVD.Parameter(1, 8, 0, 4),
            new EMEVD.Parameter(6, 0, 4, 4),
            new EMEVD.Parameter(11, 4, 4, 4),
            new EMEVD.Parameter(10, 0, 8, 4),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210800ReferenceParameters() => new[]
        {
            new EMEVD.Parameter(1, 8, 0, 4),
            new EMEVD.Parameter(3, 0, 4, 4),
            new EMEVD.Parameter(4, 4, 4, 4),
        };

        /// <summary>Reconstructs the exact DLC message event and its merged parameter bindings.</summary>
        private static void ReconstructM12DlcEvent11210850(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210850SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210850ReferenceBody();
            EMEVD.Parameter[] sourceParameters = M12DlcEvent11210850SourceParameters();
            EMEVD.Parameter[] referenceParameters = M12DlcEvent11210850ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210850 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210850SourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("010100001027000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 1, Convert.FromHexString("1027000000000000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000040")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("030100002022AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("020100002A22AB00")),
            new EMEVD.Instruction(2002, 6, Convert.FromHexString("42D501000200000000000000000000000C010000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2002, 7, Convert.FromHexString("42D501000200000000000000000000000C010000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2004, 1, Convert.FromHexString("1027000001000000")),
            new EMEVD.Instruction(2004, 47, Array.Empty<byte>()),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("010100003022AB00")),
            new EMEVD.Instruction(2004, 51, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000C040")),
            new EMEVD.Instruction(2004, 21, Convert.FromHexString("10270000915F0100")),
            new EMEVD.Instruction(2004, 15, Convert.FromHexString("1027000000000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210850ReferenceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("010100001027000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2002, 2, Convert.FromHexString("42D5010002000000000000000C010000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2004, 47, Array.Empty<byte>()),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210850SourceParameters() => new[]
        {
            new EMEVD.Parameter(1, 4, 0, 4),
            new EMEVD.Parameter(2, 4, 4, 4),
            new EMEVD.Parameter(17, 0, 4, 4),
            new EMEVD.Parameter(3, 8, 8, 4),
            new EMEVD.Parameter(9, 8, 12, 4),
            new EMEVD.Parameter(11, 8, 12, 4),
            new EMEVD.Parameter(9, 12, 16, 4),
            new EMEVD.Parameter(11, 12, 16, 4),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210850ReferenceParameters() => new[]
        {
            new EMEVD.Parameter(1, 4, 0, 4),
            new EMEVD.Parameter(2, 4, 4, 4),
            new EMEVD.Parameter(8, 0, 4, 4),
            new EMEVD.Parameter(3, 8, 8, 4),
            new EMEVD.Parameter(5, 8, 12, 4),
        };

        private static void ReconstructM12DlcEvent11210835(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210835SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210835ReferenceBody();
            EMEVD.Parameter[] sourceParameters = M12DlcEvent11210835SourceParameters();
            EMEVD.Parameter[] referenceParameters = M12DlcEvent11210835ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11210835 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210835SourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010000001F22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("050100002D22AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("040100002A22AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("030100002322AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("020100002022AB00")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0101000088030000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("2422AB0001000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("050100002D22AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("040100002A22AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("030100002322AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("020100002022AB00")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0000000088030000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("1F22AB0001000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210835ReferenceBody()
        {
            EMEVD.Instruction[] source = M12DlcEvent11210835SourceBody();
            return source.Take(5)
                .Concat(source.Skip(7).Take(5))
                .Concat(source.Skip(13).Take(1))
                .Concat(source.Skip(16)).ToArray();
        }

        private static EMEVD.Parameter[] M12DlcEvent11210835SourceParameters() => new[]
        {
            new EMEVD.Parameter(9, 0, 0, 4),
            new EMEVD.Parameter(18, 0, 4, 4),
            new EMEVD.Parameter(11, 0, 8, 4),
            new EMEVD.Parameter(20, 0, 12, 4),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210835ReferenceParameters() => new[]
        {
            new EMEVD.Parameter(7, 0, 0, 4),
            new EMEVD.Parameter(13, 0, 4, 4),
            new EMEVD.Parameter(9, 0, 8, 4),
            new EMEVD.Parameter(15, 0, 12, 4),
        };

        /// <summary>Removes only the exact obsolete DLC condition block.</summary>
        private static void ReconstructM12DlcEvent11210900(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210900SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210900ReferenceBody();
            EMEVD.Parameter[] parameters = M12DlcEvent11210900Parameters();
            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, parameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, parameters),
                "Event 11210900 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210900SourceBody() => new[]
        {
            new EMEVD.Instruction(1003, 2, Convert.FromHexString("0001020000000000")),
            new EMEVD.Instruction(4, 0, Convert.FromHexString("000000000000000001000000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700001C811200")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700001D811200")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700001E811200")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000029811200")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700002A811200")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700002B811200")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF0100001027000079811200")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007A811200")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007C811200")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007D811200")),
            new EMEVD.Instruction(1000, 2, Convert.FromHexString("0001FF00")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("5131310100000000")),
            new EMEVD.Instruction(2004, 5, Convert.FromHexString("4A1A000001000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210900ReferenceBody()
        {
            EMEVD.Instruction[] source = M12DlcEvent11210900SourceBody();
            return source.Take(2).Concat(source.Skip(13)).ToArray();
        }

        private static EMEVD.Parameter[] M12DlcEvent11210900Parameters() => new[]
        {
            new EMEVD.Parameter(1, 4, 0, 4),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210831SourceBody() => new[]
        {
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("020100002A22AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("010100002022AB00")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000000000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210831ReferenceBody() => new[]
        {
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000000000000")),
            new EMEVD.Instruction(3, 1, Convert.FromHexString("010300000000000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("010100002022AB00")),
            new EMEVD.Instruction(2003, 22, Convert.FromHexString("000000000000000001000000")),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210831SourceParameters() => new[]
        {
            new EMEVD.Parameter(2, 4, 0, 4),
            new EMEVD.Parameter(3, 4, 4, 4),
            new EMEVD.Parameter(7, 0, 4, 4),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210831ReferenceParameters() => new[]
        {
            new EMEVD.Parameter(2, 4, 0, 4),
            new EMEVD.Parameter(3, 4, 4, 4),
            new EMEVD.Parameter(6, 0, 4, 4),
            new EMEVD.Parameter(3, 8, 8, 4),
            new EMEVD.Parameter(6, 4, 8, 4),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210886SourceBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0101000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 48, Convert.FromHexString("1027000000000040")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0201000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0200000000000000")),
            new EMEVD.Instruction(1, 0, Convert.FromHexString("0200000000004040")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010200")),
            new EMEVD.Instruction(2004, 49, Convert.FromHexString("102700000000803F")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210886ReferenceBody()
        {
            EMEVD.Instruction[] source = M12DlcEvent11210886SourceBody();
            return source.Take(3)
                .Concat(new[]
                {
                    new EMEVD.Instruction(1003, 1, Convert.FromHexString("0101000000000000")),
                    new EMEVD.Instruction(2004, 5, Convert.FromHexString("1027000000000000")),
                })
                .Concat(source.Skip(4).Take(2))
                .Concat(new[]
                {
                    new EMEVD.Instruction(1, 0, Convert.FromHexString("0200000000000040")),
                    source[7],
                    new EMEVD.Instruction(1003, 1, Convert.FromHexString("0101000000000000")),
                    new EMEVD.Instruction(2004, 5, Convert.FromHexString("1027000001000000")),
                })
                .Concat(source.Skip(9)).ToArray();
        }

        private static EMEVD.Parameter[] M12DlcEvent11210886SourceParameters() => new[]
        {
            new EMEVD.Parameter(0, 4, 0, 4),
            new EMEVD.Parameter(4, 4, 0, 4),
            new EMEVD.Parameter(1, 4, 4, 4),
            new EMEVD.Parameter(5, 4, 4, 4),
        };

        private static EMEVD.Parameter[] M12DlcEvent11210886ReferenceParameters() => new[]
        {
            new EMEVD.Parameter(0, 4, 0, 4),
            new EMEVD.Parameter(3, 4, 0, 4),
            new EMEVD.Parameter(5, 4, 0, 4),
            new EMEVD.Parameter(9, 4, 0, 4),
            new EMEVD.Parameter(1, 4, 4, 4),
            new EMEVD.Parameter(6, 4, 4, 4),
        };

        /// <summary>Reconstructs the exact DLC event-state watcher and its message event.</summary>
        private static void ReconstructM12DlcEvent11210837(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");
            EMEVD.Instruction[] sourceBody = M12DlcEvent11210837SourceBody();
            EMEVD.Instruction[] referenceBody = M12DlcEvent11210837ReferenceBody();
            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11210837 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210837SourceBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000D11F0000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100001E22AB00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010000001F22AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2000, 6, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("04010000E12CAB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01000000D11F0000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("02010000F621AB00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000D11F0000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("184A020001000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("02000000D11F0000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(2003, 40, Array.Empty<byte>()),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210837ReferenceBody() => new[]
        {
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01020000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000D21F0000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("02010000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("02020000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("02010000D21F0000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010100")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010200")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0001FF00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01000000D21F0000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("00000000D21F0000")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("02010200")),
            new EMEVD.Instruction(2007, 9, Convert.FromHexString("184A0200")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2007, 9, Convert.FromHexString("B52E3101")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        /// <summary>Reconstructs the disabled vanilla gate event in Darkroot Garden.</summary>
        public static void ReconstructKnownM13Events(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructRemovedVanillaInstructionSets(archive,
                RemoveInstructionSet(50, 18, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(2000, 0, Convert.FromHexString("000000004580AC0000000000")),
                    new EMEVD.Instruction(2000, 0, Convert.FromHexString("000000000684AC004280AC0096190000"))));
            ReconstructPrefixedEvents(archive,
                Disabled(11305390, 7, EMEVD.Event.RestBehaviorType.Default));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveObjectSfxPair(11300001, 10, EMEVD.Event.RestBehaviorType.Default, 0x0013DDE6, 0, 0x0013DDE7, false));
            ReconstructM13Event11305000(FindUniqueEvent(archive, 11305000));
        }

        /// <summary>Reconstructs the observed FogMod timing and cleanup changes in the Painted World watcher.</summary>
        private static void ReconstructM13Event11305000(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M13Event11305000SourceBody();
            EMEVD.Instruction[] reconstructedBody = M13Event11305000ReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11305000 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M13Event11305000SourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01030000")),
            new EMEVD.Instruction(3, 8, Convert.FromHexString("01010D00")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("0101000010270000ACE01300")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000B093AC00")),
            new EMEVD.Instruction(3, 4, Convert.FromHexString("010300006D00000001000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000F041")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("01020000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("0601000010270000ACE01300")),
            new EMEVD.Instruction(1000, 2, Convert.FromHexString("01000600")),
            new EMEVD.Instruction(4, 2, Convert.FromHexString("07000000102700000500000000000000")),
            new EMEVD.Instruction(1000, 2, Convert.FromHexString("01010700")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("E293AC0001000000")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("E4FB0100000000006E0514000D01000010270000")),
            new EMEVD.Instruction(2002, 3, Convert.FromHexString("48FC01000000000010270000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2005, 6, Convert.FromHexString("44021400FFFFFFFF01000000")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M13Event11305000ReconstructedBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01030000")),
            new EMEVD.Instruction(3, 8, Convert.FromHexString("01010D00")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("0101000010270000ACE01300")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("01020000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("0601000010270000ACE01300")),
            new EMEVD.Instruction(1000, 2, Convert.FromHexString("01000600")),
            new EMEVD.Instruction(4, 2, Convert.FromHexString("07000000102700000500000000000000")),
            new EMEVD.Instruction(1000, 2, Convert.FromHexString("01010700")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("BD14000001000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        /// <summary>Reconstructs the disabled vanilla gate event in the Painted World.</summary>
        public static void ReconstructKnownM13DlcEvents(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructPrefixedEvents(archive,
                Disabled(11310095, 14, EMEVD.Event.RestBehaviorType.Default),
                WaitForAreaThenEnd(11315390, 7, EMEVD.Event.RestBehaviorType.Default, 0x001408E6));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveObjectSfxPair(11310001, 13, EMEVD.Event.RestBehaviorType.Default, 0x001404F6, 0, 0x001404F7, false),
                RemoveSingleInstruction(11315393, 8, EMEVD.Event.RestBehaviorType.Default, 1003, 1, "0301010000000000"),
                RemoveSingleInstruction(50, 31, EMEVD.Event.RestBehaviorType.Default, 2000, 0,
                    "0000000056A7AC0000000000"));
            ReconstructM13DlcAreaOperations(FindUniqueEvent(archive, 11315394));
            ReconstructM13DlcObjectOperations(FindUniqueEvent(archive, 11315398));
            ReconstructM13DlcAreaStateEvent(FindUniqueEvent(archive, 11310052));
        }

        /// <summary>Reconstructs the disabled vanilla gate event in Anor Londo.</summary>
        public static void ReconstructKnownM14Events(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructPrefixedEvents(archive,
                Disabled(11405390, 6, EMEVD.Event.RestBehaviorType.Default));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveInstructionSequence(11400001, 10, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00156486, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00156487, (byte)1 }),
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00156488, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00156489, (byte)1 })),
                RemoveSingleInstruction(11400200, 21, EMEVD.Event.RestBehaviorType.Default, 2003, 16, "1A270000"));
        }

        /// <summary>Reconstructs vanilla gate interaction events disabled by FogMod in the Painted World.</summary>
        public static void ReconstructKnownM14DlcEvents(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructM14DlcEvent11410001(FindUniqueEvent(archive, 11410001));
            ReconstructPrefixedEvents(archive,
                Disabled(11410095, 14, EMEVD.Event.RestBehaviorType.Default),
                Disabled(11415340, 6, EMEVD.Event.RestBehaviorType.Default),
                Disabled(11415370, 6, EMEVD.Event.RestBehaviorType.Default),
                WaitOneFrame(11415373, 10, EMEVD.Event.RestBehaviorType.Default));
            ReconstructM14DlcGateWatcher(FindUniqueEvent(archive, 11415380));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveInstructionSequence(11410901, 12, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00158B32, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00158B33, (byte)1 }),
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00158B34, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00158B35, (byte)1 })),
                RemoveInstructionSequence(11410410, 12, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00158952, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00158953, (byte)1 }),
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00158954, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00158955, (byte)1 })),
                RemoveObjectSfxPair(11410900, 12, EMEVD.Event.RestBehaviorType.Default, 0x00158ACE, 0, 0x00158ACF, false));
        }

        /// <summary>Reconstructs the exact painted-world gate controls and removes its obsolete wait operations.</summary>
        private static void ReconstructM14DlcEvent11410001(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M14DlcEvent11410001SourceBody();
            EMEVD.Instruction[] referenceBody = M14DlcEvent11410001ReferenceBody();
            if (evt.Instructions.Count == referenceBody.Length
                && evt.Instructions.Zip(referenceBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second))) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && evt.Instructions.Zip(sourceBody)
                    .All(pair => EmevdMerge.SameInstruction(pair.First, pair.Second)),
                "Event 11410001 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
        }

        private static EMEVD.Instruction[] M14DlcEvent11410001SourceBody() => new[]
        {
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("6E8B150000000000")),
            new EMEVD.Instruction(4, 0, Convert.FromHexString("00000000F286150001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0A00000001000000")),
            new EMEVD.Instruction(2003, 12, Convert.FromHexString("F0861500")),
            new EMEVD.Instruction(1003, 5, Convert.FromHexString("01010000")),
            new EMEVD.Instruction(2003, 28, Convert.FromHexString("24000000")),
            new EMEVD.Instruction(2008, 3, Convert.FromHexString("0E010000")),
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("968B150000000000")),
            new EMEVD.Instruction(2006, 1, Convert.FromHexString("978B150001000000")),
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(2006, 3, Convert.FromHexString("000000006E8B1500FFFFFFFF9E5F0100")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000040")),
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("6E8B150001000000")),
            new EMEVD.Instruction(2009, 3, Convert.FromHexString("E81DAE006E8B1500000000400000344300000000")),
        };

        private static EMEVD.Instruction[] M14DlcEvent11410001ReferenceBody()
        {
            EMEVD.Instruction[] source = M14DlcEvent11410001SourceBody();
            EMEVD.Instruction[] gateControls =
            {
                new EMEVD.Instruction(2004, 15, Convert.FromHexString("F086150000000000")),
                new EMEVD.Instruction(2004, 15, Convert.FromHexString("F186150000000000")),
                new EMEVD.Instruction(2004, 4, Convert.FromHexString("F086150000000000")),
                new EMEVD.Instruction(2004, 4, Convert.FromHexString("F186150000000000")),
                new EMEVD.Instruction(2006, 1, Convert.FromHexString("1891150001000000")),
                new EMEVD.Instruction(2006, 1, Convert.FromHexString("2291150001000000")),
                new EMEVD.Instruction(2006, 1, Convert.FromHexString("1991150001000000")),
                new EMEVD.Instruction(2006, 1, Convert.FromHexString("2391150001000000")),
            };
            return source.Take(4).Concat(gateControls).Concat(source.Skip(4).Take(3))
                .Concat(source.Skip(9)).ToArray();
        }

        /// <summary>Reconstructs the Valley of Drakes movement-state event.</summary>
        public static void ReconstructKnownM16Events(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructValleyDrakeMovement(FindUniqueEvent(archive, ValleyDrakeMovementEvent));
            ReconstructPrefixedEvents(archive,
                WaitForAreaThenEnd(11605390, 6, EMEVD.Event.RestBehaviorType.Default, 0x001875B4));
        }

        /// <summary>Reconstructs the adjusted region check in the King family event.</summary>
        public static void ReconstructKnownM15DlcEvents(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructM15DlcEvent11510900(FindUniqueEvent(archive, 11510900));
            ReconstructM15DlcEvent11510543(FindUniqueEvent(archive, 11510543));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveInstructionSequence(11510001, 40, EMEVD.Event.RestBehaviorType.Default,
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00171236, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00171237, (byte)1 }),
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00171238, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00171239, (byte)1 }),
                    new EMEVD.Instruction(2005, 3, new object[] { 0x00171234, (byte)0 }),
                    new EMEVD.Instruction(2006, 1, new object[] { 0x00171235, (byte)1 })));
            ReconstructM15DlcEvent11515381(FindUniqueEvent(archive, 11515381));
            EMEVD.Event stateEvent = archive.Events.SingleOrDefault(evt => evt.ID == 11515386);
            if (stateEvent != null) ReconstructM15DlcEvent11515386(stateEvent);
            ReconstructKingFamilyKneelingEvent(FindUniqueEvent(archive, KingFamilyKneelingEvent));
            ReconstructM15DlcFogFlagEvent(FindUniqueEvent(archive, 11510502));
            ReconstructM15DlcGateFlagEvent(FindUniqueEvent(archive, 11510230));
            ReconstructM15DlcGateAreaTransition(FindUniqueEvent(archive, 11510240));
            ReconstructPrefixedEvents(archive,
                Disabled(11510131, 8, EMEVD.Event.RestBehaviorType.Default),
                Disabled(11515390, 6, EMEVD.Event.RestBehaviorType.Default));
        }

        /// <summary>Removes the exact FogMod-omitted M15 DLC sound-effect operations and reindexes event parameters.</summary>
        private static void ReconstructM15DlcEvent11510543(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            EMEVD.Instruction[] sourceBody = M15DlcEvent11510543SourceBody();
            EMEVD.Instruction[] referenceBody = M15DlcEvent11510543ReferenceBody(sourceBody);
            EMEVD.Parameter[] sourceParameters = M15DlcEvent11510543SourceParameters();
            EMEVD.Parameter[] referenceParameters = M15DlcEvent11510543ReferenceParameters();

            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)
                && EventParametersMatch(evt, referenceParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11510543 does not match its known pristine body and parameter table.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(referenceParameters);
        }

        private static EMEVD.Instruction[] M15DlcEvent11510543SourceBody() => new[]
        {
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("4611170000000000")),
            new EMEVD.Instruction(2006, 1, Convert.FromHexString("4711170000000000")),
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("4811170000000000")),
            new EMEVD.Instruction(2006, 1, Convert.FromHexString("4911170000000000")),
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("4A11170000000000")),
            new EMEVD.Instruction(2006, 1, Convert.FromHexString("4B11170000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("00010000ACA3AF00")),
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("4611170001000000")),
            new EMEVD.Instruction(2006, 2, Convert.FromHexString("47111700")),
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("4811170001000000")),
            new EMEVD.Instruction(2006, 2, Convert.FromHexString("49111700")),
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("4A11170001000000")),
            new EMEVD.Instruction(2006, 2, Convert.FromHexString("4B111700")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("A81F000000000000")),
            new EMEVD.Instruction(2003, 22, Convert.FromHexString("000000000000000000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000001000000")),
            new EMEVD.Instruction(2004, 5, Convert.FromHexString("0C0C170000000000")),
            new EMEVD.Instruction(2004, 5, Convert.FromHexString("0D0C170000000000")),
            new EMEVD.Instruction(2004, 5, Convert.FromHexString("640C170000000000")),
            new EMEVD.Instruction(2004, 5, Convert.FromHexString("0000000001000000")),
            new EMEVD.Instruction(2004, 5, Convert.FromHexString("5A19000001000000")),
            new EMEVD.Instruction(2004, 5, Convert.FromHexString("6419000001000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("02010000A51F0000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("A51F000001000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("A41F000001000000")),
        };

        private static EMEVD.Instruction[] M15DlcEvent11510543ReferenceBody(EMEVD.Instruction[] sourceBody) =>
            new[] { sourceBody[6] }.Concat(sourceBody.Skip(13)).ToArray();

        private static EMEVD.Parameter[] M15DlcEvent11510543SourceParameters() => new[]
        {
            new EMEVD.Parameter(19, 0, 0, 4),
            new EMEVD.Parameter(14, 0, 4, 4),
            new EMEVD.Parameter(14, 4, 8, 4),
            new EMEVD.Parameter(15, 0, 12, 4),
        };

        private static EMEVD.Parameter[] M15DlcEvent11510543ReferenceParameters() => new[]
        {
            new EMEVD.Parameter(7, 0, 0, 4),
            new EMEVD.Parameter(2, 0, 4, 4),
            new EMEVD.Parameter(2, 4, 8, 4),
            new EMEVD.Parameter(3, 0, 12, 4),
        };

        private static void ReconstructM15DlcEvent11510900(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M15DlcEvent11510900SourceBody();
            EMEVD.Instruction[] reconstructedBody = M15DlcEvent11510900ReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11510900 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M15DlcEvent11510900SourceBody() => new[]
        {
            new EMEVD.Instruction(4, 2, Convert.FromHexString("00000000FA0C17000500000000000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1003, 5, Convert.FromHexString("07010000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("FDB5AF0001000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("00010000FDB5AF00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0201000080A2AF00")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("884A020000000000C11517000F01000010270000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("8D4A020000000000C11517000F01000010270000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("CF0F000001000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("0A000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("010000000C000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("7800000001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("74A4AF0001000000")),
            new EMEVD.Instruction(2003, 12, Convert.FromHexString("FA0C1700")),
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("D211170000000000")),
            new EMEVD.Instruction(2006, 1, Convert.FromHexString("D311170001000000")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("C229B40000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("CC29B40000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("D629B40000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("FE29B40000000000")),
            new EMEVD.Instruction(2003, 28, Convert.FromHexString("27000000")),
        };

        private static EMEVD.Instruction[] M15DlcEvent11510900ReconstructedBody() => new[]
        {
            new EMEVD.Instruction(4, 2, Convert.FromHexString("00000000FA0C17000500000000000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1003, 5, Convert.FromHexString("07010000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("FDB5AF0001000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("00010000FDB5AF00")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0201000080A2AF00")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("884A020000000000C11517000F01000010270000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("8D4A020000000000C11517000F01000010270000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("010000000C000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("7800000001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("74A4AF0001000000")),
            new EMEVD.Instruction(2003, 12, Convert.FromHexString("FA0C1700")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("C229B40000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("CC29B40000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("D629B40000000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("FE29B40000000000")),
            new EMEVD.Instruction(2003, 28, Convert.FromHexString("27000000")),
        };

        /// <summary>Reconstructs the disabled vanilla gate event in the Catacombs.</summary>
        public static void ReconstructKnownM15Events(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructM15Event11500210(FindUniqueEvent(archive, 11500210));
            ReconstructPrefixedEvents(archive,
                Disabled(11505390, 6, EMEVD.Event.RestBehaviorType.Default));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveObjectSfxPair(11500001, 17, EMEVD.Event.RestBehaviorType.Default, 0x0016EB26, 0, 0x0016EB27, false));
        }

        /// <summary>Reconstructs the exact reference body for the Demon Ruins progression watcher.</summary>
        private static void ReconstructM15Event11500210(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M15Event11500210SourceBody();
            EMEVD.Instruction[] reconstructedBody = M15Event11500210ReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11500210 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M15Event11500210SourceBody() => new[]
        {
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(3, 8, Convert.FromHexString("00010F00")),
            new EMEVD.Instruction(1, 0, Convert.FromHexString("000000000000A040")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("000100000B000000")),
            new EMEVD.Instruction(3, 5, Convert.FromHexString("000000000100000029ED160000000000FFFF00000000000008BE98003000000000000000")),
            new EMEVD.Instruction(2004, 29, Convert.FromHexString("29E4160001000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0201000080A2AF00")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("F04902000000000025ED16000F01000010270000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("02000000")),
            new EMEVD.Instruction(2003, 19, Convert.FromHexString("0F000200")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("F24902000000000025ED16000F01000010270000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2003, 28, Convert.FromHexString("21000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M15Event11500210ReconstructedBody() => new[]
        {
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(3, 8, Convert.FromHexString("00010F00")),
            new EMEVD.Instruction(1, 0, Convert.FromHexString("000000000000A040")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("000100000B000000")),
            new EMEVD.Instruction(3, 5, Convert.FromHexString("000000000100000029ED160000000000FFFF00000000000008BE98003000000000000000")),
            new EMEVD.Instruction(2004, 29, Convert.FromHexString("29E4160001000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("B814000001000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        /// <summary>Reconstructs the known vanilla event stops in the Duke's Archives and Crystal Cave.</summary>
        public static void ReconstructKnownM17Events(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructPrefixedEvents(archive,
                Disabled(11700085, 13, EMEVD.Event.RestBehaviorType.Default),
                Disabled(11705100, 7, EMEVD.Event.RestBehaviorType.Default),
                Disabled(11705380, 6, EMEVD.Event.RestBehaviorType.Default),
                Disabled(11705381, 8, EMEVD.Event.RestBehaviorType.Default),
                Disabled(11705382, 8, EMEVD.Event.RestBehaviorType.Default),
                Disabled(11705390, 10, EMEVD.Event.RestBehaviorType.Default));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveObjectSfxPair(11700001, 12, EMEVD.Event.RestBehaviorType.Default, 0x0019F866, 0, 0x0019F867, false),
                RemoveObjectSfxPair(11705385, 9, EMEVD.Event.RestBehaviorType.Restart, 0x0019F802, 0, 0x0019F803, false));
            ReconstructM17Event11700000(FindUniqueEvent(archive, 11700000));
            ReconstructSeathCrystalWatcher(FindUniqueEvent(archive, SeathCrystalWatcherEvent));
        }

        private static void ReconstructM17Event11700000(EMEVD.Event evt)
        {
            const int sourceCount = 31;
            const int targetCount = 28;
            EMEVD.Instruction[] removedOperations =
            {
                new EMEVD.Instruction(2005, 3, Convert.FromHexString("66F8190000000000")),
                new EMEVD.Instruction(2006, 1, Convert.FromHexString("67F8190000000000")),
                new EMEVD.Instruction(2005, 3, Convert.FromHexString("66F8190001000000")),
                new EMEVD.Instruction(2006, 2, Convert.FromHexString("67F81900")),
            };
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Restart,
                "Event 11700000 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event 11700000 unexpectedly has event parameters.");

            if (evt.Instructions.Count == sourceCount)
            {
                int[] sourceIndexes = { 1, 2, 27, 28 };
                for (int i = 0; i < sourceIndexes.Length; i++)
                {
                    int index = sourceIndexes[i];
                    EMEVD.Instruction expected = removedOperations[i];
                    int[] matches = Enumerable.Range(0, evt.Instructions.Count)
                        .Where(candidate => InstructionSequenceMatches(evt, candidate, new[] { expected }))
                        .ToArray();
                    Require(EmevdMerge.SameInstruction(evt.Instructions[index], expected) && matches.Length == 1,
                        "Event 11700000 does not contain its exact authored object/SFX operations.");
                }

                evt.Instructions[1] = EndEventInstruction();
                foreach (int index in new[] { 28, 27, 2 })
                    SkipRanges.Remove(evt, index);
                Require(evt.Instructions.Count == targetCount,
                    "Event 11700000 did not reach its expected reconstructed instruction count.");
            }
            else
            {
                Require(evt.Instructions.Count == targetCount
                    && EmevdMerge.SameInstruction(evt.Instructions[1], EndEventInstruction()),
                    "Event 11700000 has an unexpected instruction count or terminal operation.");
                foreach (EMEVD.Instruction removed in removedOperations)
                    Require(!evt.Instructions.Any(instruction => EmevdMerge.SameInstruction(instruction, removed)),
                        "Event 11700000 still contains an operation removed by FogMod.");
            }
        }

        /// <summary>Reconstructs the end-of-cycle event ordering in the ending archive.</summary>
        public static void ReconstructKnownM18Events(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            EMEVD.Event startEvent = archive.Events.SingleOrDefault(evt => evt.ID == 50);
            if (startEvent != null) ReconstructM18Event50(startEvent);
            ReconstructEndingCycleEvent(FindUniqueEvent(archive, EndingCycleEvent));
            ReconstructPrefixedEvents(archive,
                Disabled(11805390, 6, EMEVD.Event.RestBehaviorType.Default));
        }

        /// <summary>Reconstructs the exact M18 starting-event guard and obsolete initializer removals.</summary>
        private static void ReconstructM18Event50(EMEVD.Event evt)
        {
            const string sourceFingerprint = "8852885128d9c426f687348bbf875ae3eb8c565eea16ad777d5be7bc23c1cda6";
            const string targetFingerprint = "3fe9056614bffca9033023d088b25b9c45c95ba1a440129394cf85c1c6ec0899";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 50 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event 50 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M18 event 50 does not match its known pristine or reconstructed semantic form.");

            evt.Instructions[2] = new EMEVD.Instruction(1003, 1,
                Convert.FromHexString("020000000F000000"));
            evt.Instructions.RemoveAt(18);
            evt.Instructions.RemoveAt(16);
            evt.Instructions.RemoveRange(4, 2);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M18 event 50 reconstruction did not produce the pinned semantic form.");
        }

        /// <summary>Reconstructs the Fog event flag used by the DLC demon bonus watcher.</summary>
        public static void ReconstructKnownM18DlcEvents(EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            ReconstructM18DlcEvent11810150(FindUniqueEvent(archive, 11810150));
            ReconstructDemonDestroyedBonusEvent(FindUniqueEvent(archive, DemonDestroyedBonusEvent));
            ReconstructPrefixedEvents(archive,
                EndWhenFlagSetOrWaitForFlagThenEnd(11810000, 14, EMEVD.Event.RestBehaviorType.Default,
                    ArenaGateAlreadyCompleteFlag, ArenaGateCompletionFlag),
                WaitForAreaThenEnd(11815390, 11, EMEVD.Event.RestBehaviorType.Default, 0x001BAA06));
            ReconstructM18DlcFinalFlagWrite(FindUniqueEvent(archive, 11815394));
            ReconstructRemovedVanillaInstructionSequences(archive,
                RemoveObjectSfxPair(11810001, 11, EMEVD.Event.RestBehaviorType.Default, 0x001BA616, 0, 0x001BA617, false),
                RemoveObjectSfxPair(11810900, 5, EMEVD.Event.RestBehaviorType.Default, 0x001BA5B2, 0, 0x001BA5B3, false));
        }

        /// <summary>
        /// Applies the eight strict static boss-event reconstructions while assembling the offline
        /// Fog data root. Call this before FogMod reads the archives; runtime-generated event
        /// changes must be preserved by the runtime merge path.
        /// </summary>
        public static void ReconstructBossEventsForOfflineRoot(string archiveName, EMEVD archive)
        {
            if (archiveName == null) throw new ArgumentNullException("archiveName");
            if (archive == null) throw new ArgumentNullException("archive");

            var staged = new List<KeyValuePair<int, EMEVD.Event>>();
            switch (archiveName)
            {
                case "m12_01_00_00.emevd.dcx":
                    StageBossEvent(archive, staged, 11210001, ReconstructM12DlcBossEvent11210001);
                    break;
                case "m14_00_00_00.emevd.dcx":
                    StageBossEvent(archive, staged, 11405392, ReconstructM14BossEvent11405392);
                    break;
                case "m14_01_00_00.emevd.dcx":
                    StageBossEvent(archive, staged, 11415342, ReconstructM14DlcBossEvent11415342);
                    StageBossEvent(archive, staged, 11415382, ReconstructM14DlcBossEvent11415382);
                    break;
                case "m15_01_00_00.emevd.dcx":
                    StageBossEvent(archive, staged, 11515392, ReconstructM15DlcBossEvent11515392);
                    break;
                case "m16_00_00_00.emevd.dcx":
                    StageBossEvent(archive, staged, 11600001, ReconstructM16BossEvent11600001);
                    break;
                case "m18_01_00_00.emevd.dcx":
                    StageBossEvent(archive, staged, 11810310, ReconstructM18DlcBossEvent11810310);
                    StageBossEvent(archive, staged, 11815382, ReconstructM18DlcBossEvent11815382);
                    break;
            }

            foreach (KeyValuePair<int, EMEVD.Event> replacement in staged)
                archive.Events[replacement.Key] = replacement.Value;
        }

        private static void StageBossEvent(EMEVD archive, List<KeyValuePair<int, EMEVD.Event>> staged,
            long eventId, Action<EMEVD.Event> reconstruct)
        {
            int[] matches = archive.Events.Select((evt, index) => new { evt, index })
                .Where(pair => pair.evt.ID == eventId).Select(pair => pair.index).ToArray();
            Require(matches.Length <= 1,
                "Expected at most one source boss event " + eventId + "; found " + matches.Length + ".");
            if (matches.Length == 0) return;

            EMEVD.Event rebuilt = CloneEvent(archive.Events[matches[0]]);
            reconstruct(rebuilt);
            staged.Add(new KeyValuePair<int, EMEVD.Event>(matches[0], rebuilt));
        }

        /// <summary>Reconstructs eight exact vanilla boss-event edits used by the pinned FogMod data.</summary>
        private static void ReconstructM12DlcBossEvent11210001(EMEVD.Event evt)
        {
            ReconstructBossEvent(evt,
                "43f1333b6e3ced926f6977c458a6de7088fc7e1497385627d00958e64c40aef8",
                "d1bd4c98c627873b62b8123afbc33cb158f8741db3273deea32c70480d518621",
                rebuilt =>
                {
                    byte[] skipArgs = rebuilt.Instructions[0].ArgData.ToArray();
                    skipArgs[0] = 9;
                    rebuilt.Instructions[0] = new EMEVD.Instruction(1003, 1, skipArgs)
                    {
                        Layer = rebuilt.Instructions[0].Layer,
                    };

                    RemoveBossInstructionSequence(rebuilt, new[]
                    {
                        InsHex(2005, 3, "F27D120000000000"),
                        InsHex(2006, 1, "F37D120001000000"),
                    });
                    RemoveBossInstructionSequence(rebuilt, new[]
                    {
                        InsHex(2005, 3, "F47D120000000000"),
                        InsHex(2006, 1, "F57D120001000000"),
                    });

                    EMEVD.Instruction[] activePair =
                    {
                        InsHex(2004, 5, "B179120001000000"),
                        InsHex(2004, 5, "B279120001000000"),
                    };
                    int[] activeStarts = FindBossInstructionSequenceStarts(rebuilt, activePair);
                    Require(activeStarts.Length == 2,
                        "Event 11210001 does not have its two exact active-state anchors.");

                    int disabledStart = FindUniqueBossInstructionSequence(rebuilt, new[]
                    {
                        InsHex(2004, 5, "B179120000000000"),
                        InsHex(2004, 5, "B279120000000000"),
                    });
                    EMEVD.Instruction[] disabledPair = rebuilt.Instructions
                        .Skip(disabledStart).Take(2).Select(CloneBossInstruction).ToArray();
                    EMEVD.Instruction aiInitialize = InsHex(3, 0, "00010000100DAB00");

                    // FogMod adds the same character activation before each of two state checks.
                    // The first branch also receives a second copy of the existing paired state operations.
                    rebuilt.Instructions.Insert(activeStarts[1], CloneBossInstruction(aiInitialize));
                    rebuilt.Instructions.InsertRange(activeStarts[0], disabledPair.Concat(new[] { aiInitialize }));
                });
        }

        private static void ReconstructM14BossEvent11405392(EMEVD.Event evt) =>
            ReconstructBossEvent(evt,
                "45ffe65b0cf6386c268dab4a2225f656507e7d75f40882cf34216560fe83835c",
                "a5a0961b90d8429abcd683a8e5e3e1fb481e6961d61530cf0367f5bdf2b0a672",
                RemoveM14BossArenaConditionBlock);

        private static void ReconstructM14DlcBossEvent11415342(EMEVD.Event evt) =>
            ReconstructBossEvent(evt,
                "383cf5392ee0ec250b4efc6197b7b4e0bed12eaad4af602fe87f21c6f0a81b79",
                "45747d793578ce4e644eff47b4e7a0447bb463372fa89aa13b0e25bef532c9af",
                rebuilt =>
                {
                    byte[] startArgs = rebuilt.Instructions[0].ArgData.ToArray();
                    Buffer.BlockCopy(BitConverter.GetBytes(3), 0, startArgs, 0, sizeof(int));
                    rebuilt.Instructions[0] = new EMEVD.Instruction(1003, 1, startArgs)
                    {
                        Layer = rebuilt.Instructions[0].Layer,
                    };
                    RemoveBossInstructionSequenceAt(rebuilt, 1, new[]
                    {
                        InsHex(1003, 5, "04010000"),
                        InsHex(2005, 3, "5289150000000000"),
                        InsHex(2006, 1, "5389150000000000"),
                        InsHex(2005, 3, "5489150000000000"),
                        InsHex(2006, 1, "5589150000000000"),
                    });
                });

        private static void ReconstructM14DlcBossEvent11415382(EMEVD.Event evt) =>
            ReconstructBossEvent(evt,
                "02d0f5802f7def72490ae1d08683d5cb8be0bc027809d2b6e8c3f2b28249e75c",
                "614ef5ad155f0221e102b3a54e3223f36f750d30b39b45227cf249091d1704d1",
                RemoveM14BossArenaConditionBlock);

        private static void ReconstructM15DlcBossEvent11515392(EMEVD.Event evt) =>
            ReconstructBossEvent(evt,
                "bdd0ac08949f24b11593c37a150f9eb1bc8d6b3e1926bf4dfe6c30ee581298fe",
                "7c893dbd38635197bd9297c75f4216e760b019a39395f18ced49fd9957b8f215",
                RemoveM14BossArenaConditionBlock);

        private static void RemoveM14BossArenaConditionBlock(EMEVD.Event evt) =>
            RemoveBossInstructionSequence(evt, new[]
            {
                InsHex(4, 3, "FF0000001027000002000000"),
                InsHex(4, 3, "FF000000102700000C000000"),
                InsHex(1000, 2, "0001FF00"),
            });

        private static void ReconstructM16BossEvent11600001(EMEVD.Event evt) =>
            ReconstructBossEvent(evt,
                "9c6970af1ee90c43986af9dfdb94a2c33e811dda382546cbd16f81d976abdc00",
                "0e9b7c82fede7bbbc48283b47594faf36c353f7d4b61a9f1ad0b4a4bad763256",
                rebuilt =>
                {
                    RemoveBossInstructionSequenceAt(rebuilt, 6, new[]
                    {
                        InsHex(2004, 48, "206D180000004040"),
                        InsHex(2004, 48, "216D180000004040"),
                        InsHex(2004, 48, "226D180000004040"),
                        InsHex(2004, 48, "236D180000004040"),
                        InsHex(2004, 48, "246D180000004040"),
                    });
                    RemoveBossInstructionSequence(rebuilt, new[]
                    {
                        InsHex(2003, 2, "CF0F000001000000"),
                    });
                    int difficultyCheck = FindUniqueBossInstructionSequence(rebuilt, new[]
                    {
                        InsHex(1001, 1, "0A000000"),
                    });
                    rebuilt.Instructions[difficultyCheck] = InsHex(1001, 1, "01000000");
                    // FogMod also removes the pristine warp's exact transition flag pair.
                    RemoveBossInstructionSequence(rebuilt, new[]
                    {
                        InsHex(2005, 3, "C671180000000000"),
                        InsHex(2006, 1, "C771180001000000"),
                    });
                });

        private static void ReconstructM18DlcBossEvent11810310(EMEVD.Event evt) =>
            ReconstructBossEvent(evt,
                "40a1138729715b675f3d8b772b91ac3649b2450c0410dede007c6ebc8bbcd136",
                "f56027d9227764e6b135b548f13565cb26b251c04bf5db9ecc4ff6636966871f",
                rebuilt =>
                {
                    RemoveBossInstructionSequenceAt(rebuilt, 3, new[]
                    {
                        InsHex(2005, 3, "16A61B0000000000"),
                        InsHex(2006, 1, "17A61B0000000000"),
                    });
                    RemoveBossInstructionSequence(rebuilt, new[]
                    {
                        InsHex(2005, 3, "16A61B0001000000"),
                        InsHex(2006, 2, "17A61B00"),
                    });
                });

        private static void ReconstructM18DlcBossEvent11815382(EMEVD.Event evt) =>
            ReconstructBossEvent(evt,
                "8f004a28d49fea5d9ff680eaf1b45cfa75224b7c55e8dfa4458e70a88b539e3d",
                "0b7ff610f5d5f16011fb5a4d93c40c7c8f4e0504038b13311cebe123e7eaad2a",
                rebuilt =>
                {
                    RemoveBossInstructionSequence(rebuilt, new[]
                    {
                        InsHex(2005, 3, "B2A51B0001000000"),
                        InsHex(2006, 2, "B3A51B00"),
                    });
                    int areaCheck = FindUniqueBossInstructionSequence(rebuilt, new[]
                    {
                        InsHex(3, 2, "0001000010270000A0A91B00"),
                    });
                    rebuilt.Instructions.Insert(areaCheck,
                        InsHex(3, 0, "00010000D034B400"));
                });

        private static void ReconstructBossEvent(EMEVD.Event evt, string sourceFingerprint,
            string targetFingerprint, Action<EMEVD.Event> transform)
        {
            Require(evt.Parameters.Count == 0,
                "Boss event " + evt.ID + " unexpectedly has event parameters.");
            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "Boss event " + evt.ID + " does not match its known pristine or reconstructed semantic form (fingerprint "
                + fingerprint + ").");

            EMEVD.Event rebuilt = CloneEvent(evt);
            transform(rebuilt);
            Require(EventSemanticFingerprint(rebuilt) == targetFingerprint,
                "Boss event " + evt.ID + " reconstruction did not produce the pinned semantic form (fingerprint "
                + EventSemanticFingerprint(rebuilt) + ").");

            evt.Instructions.Clear();
            evt.Instructions.AddRange(rebuilt.Instructions.Select(CloneBossInstruction));
        }

        private static void RemoveBossInstructionSequence(EMEVD.Event evt,
            EMEVD.Instruction[] expected)
        {
            int start = FindUniqueBossInstructionSequence(evt, expected);
            evt.Instructions.RemoveRange(start, expected.Length);
        }

        private static void RemoveBossInstructionSequenceAt(EMEVD.Event evt, int start,
            EMEVD.Instruction[] expected)
        {
            Require(InstructionSequenceMatches(evt, start, expected),
                "Boss event " + evt.ID + " does not contain its expected instruction sequence at the authored anchor.");
            evt.Instructions.RemoveRange(start, expected.Length);
        }

        private static int FindUniqueBossInstructionSequence(EMEVD.Event evt,
            EMEVD.Instruction[] expected)
        {
            int[] matches = FindBossInstructionSequenceStarts(evt, expected);
            Require(matches.Length == 1,
                "Boss event " + evt.ID + " expected one exact instruction sequence; found " + matches.Length + ".");
            return matches[0];
        }

        private static int[] FindBossInstructionSequenceStarts(EMEVD.Event evt,
            EMEVD.Instruction[] expected)
        {
            if (expected.Length == 0 || expected.Length > evt.Instructions.Count) return Array.Empty<int>();
            return Enumerable.Range(0, evt.Instructions.Count - expected.Length + 1)
                .Where(start => InstructionSequenceMatches(evt, start, expected)).ToArray();
        }

        private static EMEVD.Instruction CloneBossInstruction(EMEVD.Instruction instruction) =>
            new EMEVD.Instruction(instruction.Bank, instruction.ID, instruction.ArgData.ToArray())
            {
                Layer = instruction.Layer,
            };

        /// <summary>Reconstructs the exact Oolacile transition checks and removes the obsolete local draw operations.</summary>
        private static void ReconstructM18DlcEvent11810150(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M18DlcEvent11810150SourceBody();
            EMEVD.Instruction[] referenceBody = M18DlcEvent11810150ReferenceBody();
            if (evt.Instructions.Count == referenceBody.Length
                && InstructionSequenceMatches(evt, 0, referenceBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11810150 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(referenceBody);
        }

        private static EMEVD.Instruction[] M18DlcEvent11810150SourceBody() => new[]
        {
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("03010000D034B400")),
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("64A31B0000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("00010000D034B400")),
            new EMEVD.Instruction(2005, 3, Convert.FromHexString("64A31B0001000000")),
            new EMEVD.Instruction(3, 5, Convert.FromHexString("000000000100000021A61B0000000000FFFF0000000000008ABF9800FF00000000000000")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000881E0000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000871E000000010000")),
            new EMEVD.Instruction(1003, 5, Convert.FromHexString("06020000")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("9EBF020000000000A9980F000A02000010270000")),
            new EMEVD.Instruction(2002, 3, Convert.FromHexString("828701000000000010270000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2003, 28, Convert.FromHexString("1C000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(3, 5, Convert.FromHexString("010000000100000021A61B0000000000FFFF0000000000008BBF9800FF00000000000000")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("020000001027000021A61B00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010100")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010200")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0001FF00")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")),
            new EMEVD.Instruction(1000, 7, Convert.FromHexString("01010200")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000891E000000010000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M18DlcEvent11810150ReferenceBody()
        {
            EMEVD.Instruction[] source = M18DlcEvent11810150SourceBody();
            return source.Skip(4).Take(3)
                .Concat(new[] { new EMEVD.Instruction(1003, 5, Convert.FromHexString("04020000")), source[8] })
                .Concat(new[] { new EMEVD.Instruction(2003, 2, Convert.FromHexString("BB14000001000000")) })
                .Concat(source.Skip(11).Take(1)).Concat(source.Skip(13)).ToArray();
        }

        private static void ReconstructUndeadDragonDeathWatch(EMEVD.Event evt)
        {
            RequireEventShape(evt, EMEVD.Event.RestBehaviorType.Restart, 10);
            EMEVD.Instruction gravity = evt.Instructions[7];
            RequireInstruction(gravity, 2004, 10, 8, "SetCharacterGravity");
            Require(BitConverter.ToInt32(gravity.ArgData, 0) == UndeadDragonCharacter,
                "Event 11100400 SetCharacterGravity target is not the expected undead dragon.");
            Require(gravity.ArgData[4] == 0 || gravity.ArgData[4] == 1,
                "Event 11100400 SetCharacterGravity state is not a supported DisabledEnabled value.");

            if (gravity.ArgData[4] == 0)
                evt.Instructions[7] = Rewrite(gravity, 2004, 10, UndeadDragonCharacter, (byte)1);
        }

        private static void ReconstructBossArenaEventReset(EMEVD.Event evt)
        {
            RequireEventShape(evt, EMEVD.Event.RestBehaviorType.Default, 15);
            EMEVD.Instruction mapGuard = evt.Instructions[2];
            EMEVD.Instruction pairedMapGuard = evt.Instructions[3];
            RequireInstruction(mapGuard, 3, 8, 4, "IfPlayerInoutMap");
            RequireInstruction(pairedMapGuard, 3, 8, 4, "IfPlayerInoutMap");
            Require(IsMapGuard(pairedMapGuard, 0, 1, 11, 0),
                "Event 11105399 paired map guard has an unexpected shape.");

            bool pristineGuard = IsMapGuard(mapGuard, 0, 1, 15, 1);
            bool fogGuard = IsMapGuard(mapGuard, 0, 0, 11, 0);
            Require(pristineGuard || fogGuard, "Event 11105399 primary map guard has an unexpected shape.");
            if (pristineGuard)
                evt.Instructions[2] = Rewrite(mapGuard, 3, 8, (sbyte)0, (byte)0, (byte)11, (byte)0);
        }

        private static void ReconstructWhiteCatGuidance(EMEVD.Event evt)
        {
            RequireEventShape(evt, EMEVD.Event.RestBehaviorType.Default, 13);
            int[] matches = Enumerable.Range(0, evt.Instructions.Count)
                .Where(instructionIndex => IsWhiteCatGuidanceAnimation(evt.Instructions[instructionIndex]))
                .ToArray();
            Require(matches.Length == 1, "Event " + evt.ID + " must contain one White Cat guidance animation operation.");

            int index = matches[0];
            EMEVD.Instruction animation = evt.Instructions[index];
            if (animation.Bank == 2003 && animation.ID == 46)
            {
                Require(animation.ArgData.Length == 16 && animation.ArgData[11] == 0
                    && BitConverter.ToSingle(animation.ArgData, 12) == 5.0f,
                    "Event " + evt.ID + " has an unexpected DS1R animation extension.");
                byte[] knownArguments = new byte[12];
                Array.Copy(animation.ArgData, knownArguments, knownArguments.Length);
                evt.Instructions[index] = new EMEVD.Instruction(2003, 18, knownArguments) { Layer = animation.Layer };
            }
            else
            {
                Require(animation.Bank == 2003 && animation.ID == 18 && animation.ArgData.Length == 12,
                    "Event " + evt.ID + " has an unexpected ForceAnimationPlayback operation.");
            }
        }

        private static void ReconstructM12DlcMapGuard(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Restart,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M12DlcMapGuardSourceBody(evt.ID);
            EMEVD.Instruction addedGuard = new EMEVD.Instruction(3, 0,
                Convert.FromHexString("00010000100DAB00"));
            EMEVD.Instruction[] reconstructedBody = new[] { addedGuard }.Concat(sourceBody).ToArray();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event " + evt.ID + " does not match its known pristine or reconstructed DLC map-guard body.");
            evt.Instructions.Insert(0, addedGuard);
        }

        private static void ReconstructM12DlcEvent11210401(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M12DlcEvent11210401SourceBody();
            EMEVD.Instruction[] reconstructedBody = M12DlcEvent11210401ReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11210401 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M12DlcEvent11210401SourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01020000")),
            new EMEVD.Instruction(3, 10, Convert.FromHexString("010000000022AB000522AB000400000002000000")),
            new EMEVD.Instruction(4, 5, Convert.FromHexString("01000000102700000512000001000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000EC21AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000A040")),
            new EMEVD.Instruction(1003, 2, Convert.FromHexString("01010000EC21AB00")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("01030000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("F049020001000000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000A040")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcEvent11210401ReconstructedBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("01020000")),
            new EMEVD.Instruction(3, 10, Convert.FromHexString("010000000022AB000322AB000400000002000000")),
            new EMEVD.Instruction(4, 5, Convert.FromHexString("01000000102700000512000001000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000EC21AB00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00007041")),
            new EMEVD.Instruction(1003, 2, Convert.FromHexString("01010000EC21AB00")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("01030000")),
            new EMEVD.Instruction(2007, 4, Convert.FromHexString("F149020001000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M12DlcMapGuardSourceBody(long eventId)
        {
            switch (eventId)
            {
                case 11215007:
                    return new[]
                    {
                        new EMEVD.Instruction(3, 0, Convert.FromHexString("00010000110DAB00")),
                        new EMEVD.Instruction(2004, 1, Convert.FromHexString("B179120000000000")),
                        new EMEVD.Instruction(2004, 1, Convert.FromHexString("B279120000000000")),
                        new EMEVD.Instruction(3, 2, Convert.FromHexString("0101000010270000647E1200")),
                        new EMEVD.Instruction(3, 2, Convert.FromHexString("0100000010270000617E1200")),
                        new EMEVD.Instruction(3, 2, Convert.FromHexString("0100000010270000627E1200")),
                        new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010100")),
                        new EMEVD.Instruction(4, 1, Convert.FromHexString("FF000000B179120010270000")),
                        new EMEVD.Instruction(4, 1, Convert.FromHexString("FF000000B279120010270000")),
                        new EMEVD.Instruction(0, 0, Convert.FromHexString("0001FF00")),
                        new EMEVD.Instruction(2004, 1, Convert.FromHexString("B179120001000000")),
                        new EMEVD.Instruction(2003, 18, Convert.FromHexString("B1791200C90B000000000000")),
                        new EMEVD.Instruction(1001, 1, Convert.FromHexString("0F000000")),
                        new EMEVD.Instruction(2004, 1, Convert.FromHexString("B279120001000000")),
                        new EMEVD.Instruction(2003, 18, Convert.FromHexString("B2791200C90B000000000000")),
                        new EMEVD.Instruction(1001, 1, Convert.FromHexString("0F000000")),
                        new EMEVD.Instruction(2004, 20, Convert.FromHexString("B1791200")),
                        new EMEVD.Instruction(2004, 20, Convert.FromHexString("B2791200")),
                    };
                case 11215009:
                    return new[]
                    {
                        new EMEVD.Instruction(3, 0, Convert.FromHexString("00010000110DAB00")),
                        new EMEVD.Instruction(3, 2, Convert.FromHexString("0001000010270000667E1200")),
                        new EMEVD.Instruction(2004, 42, Convert.FromHexString("B179120001000000677E1200FFFFFFFFB0791200")),
                        new EMEVD.Instruction(2004, 42, Convert.FromHexString("B279120001000000687E1200FFFFFFFFB0791200")),
                        new EMEVD.Instruction(2004, 13, Convert.FromHexString("B1791200677E1200")),
                        new EMEVD.Instruction(2004, 13, Convert.FromHexString("B2791200687E1200")),
                        new EMEVD.Instruction(3, 2, Convert.FromHexString("0001000010270000697E1200")),
                        new EMEVD.Instruction(2004, 42, Convert.FromHexString("B1791200010000006A7E1200FFFFFFFFB0791200")),
                        new EMEVD.Instruction(2004, 42, Convert.FromHexString("B2791200010000006B7E1200FFFFFFFFB0791200")),
                        new EMEVD.Instruction(2004, 13, Convert.FromHexString("B17912006A7E1200")),
                        new EMEVD.Instruction(2004, 13, Convert.FromHexString("B27912006B7E1200")),
                        new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
                    };
                default:
                    throw new InvalidDataException("Unexpected M12 DLC map-guard event " + eventId + ".");
            }
        }

        private static void ReconstructM13DlcAreaOperations(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M13DlcAreaOperationsSourceBody();
            EMEVD.Instruction[] reconstructedBody = M13DlcAreaOperationsReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11315394 does not match the known pristine or reconstructed area-operation body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M13DlcAreaOperationsSourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000007000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000C0A8AC00")),
            new EMEVD.Instruction(1003, 5, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000BFA8AC00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2010, 3, Convert.FromHexString("080C140001000000")),
        };

        private static EMEVD.Instruction[] M13DlcAreaOperationsReconstructedBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000007000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000C0A8AC00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2010, 3, Convert.FromHexString("080C140001000000")),
        };

        private static void ReconstructM13DlcObjectOperations(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Restart,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M13DlcObjectOperationsSourceBody();
            EMEVD.Instruction[] reconstructedBody = M13DlcObjectOperationsReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11315398 does not match its known pristine or reconstructed object-operation body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M13DlcObjectOperationsSourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000BEA8AC00")),
            new EMEVD.Instruction(4, 4, Convert.FromHexString("01000000500014001027000001000000")),
            new EMEVD.Instruction(4, 8, Convert.FromHexString("0100000050001400BC02000001000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("F6A0AC0001000000")),
            new EMEVD.Instruction(4, 8, Convert.FromHexString("0000000050001400C602000001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("F7A0AC0001000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M13DlcObjectOperationsReconstructedBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000BEA8AC00")),
            new EMEVD.Instruction(4, 4, Convert.FromHexString("01000000500014001027000001000000")),
            new EMEVD.Instruction(4, 8, Convert.FromHexString("0100000050001400BC02000001000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2005, 11, Convert.FromHexString("1202140010270000FFFF0000")),
            new EMEVD.Instruction(4, 8, Convert.FromHexString("0000000050001400C602000001000000")),
            new EMEVD.Instruction(2005, 9, Convert.FromHexString("C7A8AC001202140001000000242C0000010000009A99993E9A99993E00000000")),
            new EMEVD.Instruction(2006, 3, Convert.FromHexString("0000000012021400FFFFFFFF783B0000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static void ReconstructM13DlcAreaStateEvent(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M13DlcAreaStateSourceBody();
            EMEVD.Instruction[] reconstructedBody = M13DlcAreaStateReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11310052 does not match its known pristine or reconstructed area-state body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M13DlcAreaStateSourceBody() => new[]
        {
            new EMEVD.Instruction(5, 2, Convert.FromHexString("000000008CA8AC00")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000EF1B0000FFFFFFFFFFFFFFFFC2170000FFFFFFFF")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("49FC010000000000FADD13000D00000010270000")),
            new EMEVD.Instruction(2002, 3, Convert.FromHexString("E5FB01000000000010270000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")),
            new EMEVD.Instruction(2005, 6, Convert.FromHexString("44021400FFFFFFFF01000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M13DlcAreaStateReconstructedBody() => new[]
        {
            new EMEVD.Instruction(5, 2, Convert.FromHexString("000000008CA8AC00")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000EF1B0000FFFFFFFFFFFFFFFFC2170000FFFFFFFF")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00004040")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("BE14000001000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static void ReconstructM18DlcFinalFlagWrite(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M18DlcFinalFlagWriteSourceBody();
            EMEVD.Instruction[] reconstructedBody = M18DlcFinalFlagWriteReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11815394 does not match its known pristine or reconstructed final-flag body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M18DlcFinalFlagWriteSourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("00000000E449B400")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000010000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100000636B400")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000E049B400")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF01000010270000FEA91B00")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007EA71B00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FF00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("E449B40001000000")),
            new EMEVD.Instruction(2010, 3, Convert.FromHexString("28AD1B0001000000")),
            new EMEVD.Instruction(2003, 11, Convert.FromHexString("0100000070A11B000000B808")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M18DlcFinalFlagWriteReconstructedBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("00000000E449B400")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000010000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("010100000636B400")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000E049B400")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF01000010270000FEA91B00")),
            new EMEVD.Instruction(3, 2, Convert.FromHexString("FF010000102700007EA71B00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FF00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("E449B40001000000")),
            new EMEVD.Instruction(2010, 3, Convert.FromHexString("28AD1B0001000000")),
            new EMEVD.Instruction(2003, 11, Convert.FromHexString("0100000070A11B000000B808")),
            new EMEVD.Instruction(2004, 1, Convert.FromHexString("70A11B0001000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static bool IsWhiteCatGuidanceAnimation(EMEVD.Instruction instruction)
        {
            if (instruction.Bank != 2003 || (instruction.ID != 46 && instruction.ID != 18)) return false;
            byte[] args = instruction.ArgData;
            return args.Length >= 12
                && BitConverter.ToInt32(args, 0) == WhiteCatEntity
                && BitConverter.ToInt32(args, 4) == WhiteCatGuidanceAnimation
                && args[8] == 0 && args[9] == 1 && args[10] == 0;
        }

        private static void ReconstructValleyDrakeMovement(EMEVD.Event evt)
        {
            RequireEventShape(evt, EMEVD.Event.RestBehaviorType.Restart, 10);
            EMEVD.Instruction mapHits = evt.Instructions[0];
            EMEVD.Instruction gravity = evt.Instructions[1];
            RequireInstruction(mapHits, 2004, 31, 8, "SetCharacterMapHits");
            RequireInstruction(gravity, 2004, 10, 8, "SetCharacterGravity");
            Require(BitConverter.ToInt32(mapHits.ArgData, 0) == ValleyDrakeCharacter
                && BitConverter.ToInt32(gravity.ArgData, 0) == ValleyDrakeCharacter,
                "Event 11600400 state operations do not target the expected drake.");
            bool pristineState = mapHits.ArgData[4] == 1 && gravity.ArgData[4] == 0;
            bool fogState = mapHits.ArgData[4] == 0 && gravity.ArgData[4] == 1;
            Require(pristineState || fogState, "Event 11600400 has an unexpected drake movement state.");
            if (pristineState)
            {
                evt.Instructions[0] = Rewrite(mapHits, 2004, 31, ValleyDrakeCharacter, (byte)0);
                evt.Instructions[1] = Rewrite(gravity, 2004, 10, ValleyDrakeCharacter, (byte)1);
            }
        }

        private static void ReconstructKingFamilyKneelingEvent(EMEVD.Event evt)
        {
            RequireEventShape(evt, EMEVD.Event.RestBehaviorType.Default, 40);
            int[] matches = Enumerable.Range(0, evt.Instructions.Count)
                .Where(instructionIndex => IsKingFamilyAreaCheck(evt.Instructions[instructionIndex], KingFamilyOriginalRegion)
                    || IsKingFamilyAreaCheck(evt.Instructions[instructionIndex], KingFamilyAdjustedRegion))
                .ToArray();
            Require(matches.Length == 1, "Event 11510460 must contain one authored King family area check.");

            int index = matches[0];
            EMEVD.Instruction areaCheck = evt.Instructions[index];
            if (IsKingFamilyAreaCheck(areaCheck, KingFamilyOriginalRegion))
                evt.Instructions[index] = Rewrite(areaCheck, 3, 2, (sbyte)3, (byte)0, 10000, KingFamilyAdjustedRegion);
        }

        private static void ReconstructM15DlcFogFlagEvent(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");

            EMEVD.Instruction[] vanillaBody = M15DlcFogFlagVanillaBody();
            EMEVD.Instruction[] reconstructedBody = vanillaBody.Where((_, index) => index != 10).ToArray();
            EMEVD.Parameter[] vanillaParameters = M15DlcFogFlagVanillaParameters();
            EMEVD.Parameter[] reconstructedParameters = vanillaParameters
                .Where(parameter => parameter.InstructionIndex != 10)
                .Select(parameter =>
                {
                    EMEVD.Parameter copy = CopyParameter(parameter);
                    if (copy.InstructionIndex > 10) copy.InstructionIndex--;
                    return copy;
                })
                .ToArray();

            if (evt.Instructions.Count == vanillaBody.Length)
            {
                Require(InstructionSequenceMatches(evt, 0, vanillaBody),
                    "Event 11510502 has an unexpected pristine instruction sequence.");
                Require(EventParametersMatch(evt, vanillaParameters),
                    "Event 11510502 has an unexpected pristine parameter table.");

                int parameterIndex = evt.Parameters.FindIndex(parameter => parameter.InstructionIndex == 10);
                Require(parameterIndex >= 0,
                    "Event 11510502 is missing the parameter bound to its removed flag operation.");
                evt.Parameters.RemoveAt(parameterIndex);
                SkipRanges.Remove(evt, 10);
                return;
            }

            Require(evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody),
                "Event 11510502 does not match the known pristine or reconstructed instruction sequence.");
            Require(EventParametersMatch(evt, reconstructedParameters),
                "Event 11510502 has an unexpected reconstructed parameter table.");
        }

        private static void ReconstructM15DlcEvent11515381(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11515381 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event 11515381 unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M15DlcEvent11515381SourceBody();
            EMEVD.Instruction[] targetBody = sourceBody
                .Select(instruction => new EMEVD.Instruction(instruction.Bank, instruction.ID,
                    instruction.ArgData.ToArray()) { Layer = instruction.Layer })
                .ToArray();
            targetBody[4] = new EMEVD.Instruction(3, 18, targetBody[4].ArgData.ToArray())
            {
                Layer = targetBody[4].Layer,
            };
            targetBody[13] = new EMEVD.Instruction(2003, 18,
                Convert.FromHexString("10270000F21C000000000000"))
            {
                Layer = targetBody[13].Layer,
            };

            if (evt.Instructions.Count == targetBody.Length
                && InstructionSequenceMatches(evt, 0, targetBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11515381 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(targetBody);
        }

        /// <summary>Reconstructs the M15 DLC pressure-plate state checks and multiplayer thresholds.</summary>
        private static void ReconstructM15DlcEvent11515386(EMEVD.Event evt)
        {
            const string sourceFingerprint = "9208324c048a876709d3f73404ecb79d8b07200e334b4b513c53f5dc857f6e8f";
            const string targetFingerprint = "09c8efde0c272dff3446414e870266bfe5e450245cdd3ff8e22a8f86e9c700be";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Restart,
                "Event 11515386 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event 11515386 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "Event 11515386 does not match its known pristine or reconstructed semantic form.");

            evt.Instructions[39] = new EMEVD.Instruction(1003, 5, Convert.FromHexString("02020000"));
            evt.Instructions[29] = new EMEVD.Instruction(1003, 5, Convert.FromHexString("02020000"));
            evt.Instructions[18] = new EMEVD.Instruction(1003, 5, Convert.FromHexString("02020000"));
            evt.Instructions.RemoveRange(14, 3);
            evt.Instructions.RemoveAt(7);
            evt.Instructions.RemoveAt(2);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "Event 11515386 reconstruction did not produce the pinned semantic form.");
        }

        private static EMEVD.Instruction[] M15DlcEvent11515381SourceBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000074A4AF00")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000F7B5AF00")),
            new EMEVD.Instruction(4, 3, Convert.FromHexString("010000001027000001000000")),
            new EMEVD.Instruction(3, 19, Convert.FromHexString(
                "0100000001000000C215170000000000FFFF00000000000023BF9800FF00000000000000D2111700")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 14, Convert.FromHexString("10270000C1151700")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0201000080A2AF00")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("874A020002000000C51517000F01000010270000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("8C4A020002000000C51517000F01000010270000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2004, 5, Convert.FromHexString("FA0C170001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("FCB5AF0001000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static void ReconstructM10NewLondoDlcAreaEvent(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11020899 has an unexpected rest behavior.");

            EMEVD.Instruction[] sourceBody = M10NewLondoDlcAreaSourceBody();
            EMEVD.Instruction[] reconstructedBody = sourceBody
                .Where((_, index) => index != 1 && index != 2)
                .ToArray();
            EMEVD.Parameter[] sourceParameters = M10NewLondoDlcAreaSourceParameters();
            EMEVD.Parameter[] reconstructedParameters = sourceParameters
                .Select(parameter => new EMEVD.Parameter(24, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID })
                .ToArray();

            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)
                && EventParametersMatch(evt, reconstructedParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 11020899 does not match its known pristine body and parameter table.");

            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(reconstructedParameters);
        }

        private static void ReconstructCommonEvent260(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 260 has an unexpected rest behavior.");

            EMEVD.Instruction[] sourceBody = CommonEvent260SourceBody();
            EMEVD.Instruction[] reconstructedBody = sourceBody
                .Where((_, index) => index != 2)
                .ToArray();
            EMEVD.Parameter[] sourceParameters = CommonEvent260SourceParameters();
            EMEVD.Parameter[] reconstructedParameters = sourceParameters
                .Select(parameter =>
                {
                    EMEVD.Parameter copy = CopyParameter(parameter);
                    if (copy.InstructionIndex > 2) copy.InstructionIndex--;
                    return copy;
                })
                .ToArray();

            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)
                && EventParametersMatch(evt, reconstructedParameters)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody)
                && EventParametersMatch(evt, sourceParameters),
                "Event 260 does not match its known pristine body and parameter table.");

            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
            evt.Parameters.Clear();
            evt.Parameters.AddRange(reconstructedParameters);
        }

        private static EMEVD.Instruction[] CommonEvent260SourceBody() => new[]
        {
            new EMEVD.Instruction(1003, 2, Convert.FromHexString("0001000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0001000000000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("02010000A1230000")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(2007, 3, Convert.FromHexString("0000000001000000")),
        };

        private static void ReconstructM12Event11200005(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11200005 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event 11200005 unexpectedly has event parameters.");

            EMEVD.Instruction[] sourceBody = M12Event11200005SourceBody();
            EMEVD.Instruction[] targetBody = sourceBody
                .Where((_, index) => index != 0 && index != 1 && index != 2 && index != 3 && index != 5)
                .ToArray();
            if (evt.Instructions.Count == targetBody.Length
                && InstructionSequenceMatches(evt, 0, targetBody)) return;

            Require(evt.Instructions.Count == sourceBody.Length
                && InstructionSequenceMatches(evt, 0, sourceBody),
                "Event 11200005 does not match its known pristine or reconstructed body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(targetBody);
        }

        private static EMEVD.Instruction[] M12Event11200005SourceBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF01000065040000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF01000066040000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FF01000067040000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FF00")),
            new EMEVD.Instruction(3, 4, Convert.FromHexString("01030000D809000001000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000011000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("00030000")),
            new EMEVD.Instruction(2006, 2, Convert.FromHexString("59571200")),
            new EMEVD.Instruction(3, 6, Convert.FromHexString("00020000")),
            new EMEVD.Instruction(2006, 1, Convert.FromHexString("5957120001000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Parameter[] CommonEvent260SourceParameters() => new[]
        {
            new EMEVD.Parameter(0, 4, 0, 4),
            new EMEVD.Parameter(1, 4, 0, 4),
            new EMEVD.Parameter(4, 0, 4, 4),
            new EMEVD.Parameter(3, 0, 8, 4),
        };

        private static EMEVD.Instruction[] M10NewLondoDlcAreaSourceBody() => new[]
        {
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("04010000622AA800")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("622AA80001000000")),
            new EMEVD.Instruction(2005, 7, Convert.FromHexString("FA960F0000000000")),
            new EMEVD.Instruction(2005, 6, Convert.FromHexString("FA960F00FFFFFFFF00000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("14000000943BA800")),
            new EMEVD.Instruction(5, 2, Convert.FromHexString("00000000A029A800")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("0A000000")),
            new EMEVD.Instruction(2005, 4, Convert.FromHexString("04970F0001000000")),
            new EMEVD.Instruction(2005, 4, Convert.FromHexString("05970F0001000000")),
            new EMEVD.Instruction(2005, 4, Convert.FromHexString("06970F0001000000")),
            new EMEVD.Instruction(2005, 4, Convert.FromHexString("07970F0001000000")),
            new EMEVD.Instruction(2005, 4, Convert.FromHexString("08970F0001000000")),
            new EMEVD.Instruction(2005, 4, Convert.FromHexString("09970F0001000000")),
            new EMEVD.Instruction(2005, 4, Convert.FromHexString("0A970F0001000000")),
            new EMEVD.Instruction(2005, 4, Convert.FromHexString("0B970F0001000000")),
            new EMEVD.Instruction(2005, 15, Convert.FromHexString("04970F00")),
            new EMEVD.Instruction(2005, 15, Convert.FromHexString("05970F00")),
            new EMEVD.Instruction(2005, 15, Convert.FromHexString("06970F00")),
            new EMEVD.Instruction(2005, 15, Convert.FromHexString("07970F00")),
            new EMEVD.Instruction(2005, 15, Convert.FromHexString("08970F00")),
            new EMEVD.Instruction(2005, 15, Convert.FromHexString("09970F00")),
            new EMEVD.Instruction(2005, 15, Convert.FromHexString("0A970F00")),
            new EMEVD.Instruction(2005, 15, Convert.FromHexString("0B970F00")),
            new EMEVD.Instruction(2003, 22, Convert.FromHexString("000000000000000001000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(2005, 7, Convert.FromHexString("FA960F0000000000")),
            new EMEVD.Instruction(2005, 6, Convert.FromHexString("FA960F00FFFFFFFF00000000")),
        };

        private static EMEVD.Parameter[] M10NewLondoDlcAreaSourceParameters() => new[]
        {
            new EMEVD.Parameter(26, 0, 0, 4),
            new EMEVD.Parameter(26, 4, 4, 4),
        };

        private static void ReconstructM15DlcGateFlagEvent(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction summon = new EMEVD.Instruction(2002, 4,
                Convert.FromHexString("774A020000000000AED210000B00000010270000"));
            EMEVD.Instruction fogFlag = new EMEVD.Instruction(2003, 2,
                Convert.FromHexString("B914000001000000"));
            EMEVD.Instruction temporaryFlagOff = new EMEVD.Instruction(2003, 23,
                Convert.FromHexString("AFD21000"));
            EMEVD.Instruction commonWait = new EMEVD.Instruction(2000, 5,
                Convert.FromHexString("00000000"));
            EMEVD.Instruction wait = new EMEVD.Instruction(1001, 1,
                Convert.FromHexString("01000000"));
            EMEVD.Instruction end = new EMEVD.Instruction(1000, 4,
                Convert.FromHexString("01000000"));

            if (evt.Instructions.Count == 11)
            {
                Require(InstructionSequenceMatches(evt, 8, new[] { fogFlag })
                    && InstructionSequenceMatches(evt, 9, new[] { wait })
                    && InstructionSequenceMatches(evt, 10, new[] { end })
                    && CountInstruction(evt, summon) == 0
                    && CountInstruction(evt, temporaryFlagOff) == 0
                    && CountInstruction(evt, commonWait) == 0,
                    "Event 11510230 does not match the known reconstructed gate/summon shape.");
                return;
            }

            Require(evt.Instructions.Count == 13
                && InstructionSequenceMatches(evt, 8, new[] { summon })
                && InstructionSequenceMatches(evt, 10, new[] { temporaryFlagOff })
                && InstructionSequenceMatches(evt, 11, new[] { commonWait })
                && InstructionSequenceMatches(evt, 9, new[] { wait })
                && InstructionSequenceMatches(evt, 12, new[] { end }),
                "Event 11510230 does not match the known pristine gate/summon shape.");

            evt.Instructions[8] = Rewrite(evt.Instructions[8], 2003, 2, 0x000014B9, 1);
            SkipRanges.Remove(evt, 11);
            SkipRanges.Remove(evt, 10);
            Require(evt.Instructions.Count == 11 && InstructionSequenceMatches(evt, 8, new[] { fogFlag }),
                "Event 11510230 reconstruction did not reach the expected gate/summon shape.");
        }

        private static void ReconstructM15DlcGateAreaTransition(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] vanillaBody = M15DlcGateAreaTransitionVanillaBody();
            EMEVD.Instruction[] reconstructedBody = M15DlcGateAreaTransitionReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == vanillaBody.Length
                && InstructionSequenceMatches(evt, 0, vanillaBody),
                "Event 11510240 does not match the known pristine or reconstructed gate-area transition body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static EMEVD.Instruction[] M15DlcGateAreaTransitionVanillaBody() => new[]
        {
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(3, 8, Convert.FromHexString("00010F01")),
            new EMEVD.Instruction(1, 0, Convert.FromHexString("000000000000A040")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("02000000AAB4AF00")),
            new EMEVD.Instruction(3, 5, Convert.FromHexString("0200000002000000D40A170000003443FFFF00000000E04058BE98003000000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010200")),
            new EMEVD.Instruction(1003, 5, Convert.FromHexString("02030000")),
            new EMEVD.Instruction(2007, 1, Convert.FromHexString("3ABE980001000600FFFFFFFF00004040")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("0201000080A2AF00")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("724A02000000000024ED16000F00000010270000")),
            new EMEVD.Instruction(1000, 3, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("744A02000000000024ED16000F00000010270000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2003, 19, Convert.FromHexString("0F000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M15DlcGateAreaTransitionReconstructedBody() => new[]
        {
            new EMEVD.Instruction(1003, 6, Convert.FromHexString("00010000")),
            new EMEVD.Instruction(3, 8, Convert.FromHexString("00010F01")),
            new EMEVD.Instruction(1, 0, Convert.FromHexString("000000000000A040")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("02000000AAB4AF00")),
            new EMEVD.Instruction(3, 5, Convert.FromHexString("0200000002000000D40A170000003443FFFF00000000E04058BE98003000000000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010200")),
            new EMEVD.Instruction(1003, 5, Convert.FromHexString("02030000")),
            new EMEVD.Instruction(2007, 1, Convert.FromHexString("3ABE980001000600FFFFFFFF00004040")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("BA14000001000000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M15DlcFogFlagVanillaBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000D8040000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FE010000D0040000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("FE010000F6B5AF00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0101FE00")),
            new EMEVD.Instruction(4, 0, Convert.FromHexString("010000000000000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0201000000000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0201010000000000")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010100")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("FF010200")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("0001FF00")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("0000000001000000")),
            new EMEVD.Instruction(2004, 2, Convert.FromHexString("0000000007000000")),
        };

        private static EMEVD.Parameter[] M15DlcFogFlagVanillaParameters() => new[]
        {
            new EMEVD.Parameter(4, 4, 0, 4),
            new EMEVD.Parameter(11, 0, 0, 4),
            new EMEVD.Parameter(5, 4, 4, 4),
            new EMEVD.Parameter(10, 0, 4, 4),
        };

        private static bool EventParametersMatch(EMEVD.Event evt, EMEVD.Parameter[] expected)
        {
            return evt.Parameters.Count == expected.Length && evt.Parameters.Zip(expected).All(pair =>
                pair.First.InstructionIndex == pair.Second.InstructionIndex
                && pair.First.TargetStartByte == pair.Second.TargetStartByte
                && pair.First.SourceStartByte == pair.Second.SourceStartByte
                && pair.First.ByteCount == pair.Second.ByteCount
                && pair.First.UnkID == pair.Second.UnkID);
        }

        private static string EventSemanticFingerprint(EMEVD.Event evt)
        {
            var canonical = new StringBuilder();
            canonical.Append("E|").Append(evt.ID.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(evt.RestBehavior).Append('\n');
            foreach (EMEVD.Instruction instruction in evt.Instructions)
            {
                canonical.Append("I|")
                    .Append(instruction.Bank.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(instruction.ID.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(instruction.Layer.HasValue
                        ? instruction.Layer.Value.ToString(CultureInfo.InvariantCulture)
                        : "null").Append('|')
                    .Append(Convert.ToHexString(instruction.ArgData)).Append('\n');
            }
            foreach (EMEVD.Parameter parameter in evt.Parameters
                .OrderBy(parameter => parameter.InstructionIndex)
                .ThenBy(parameter => parameter.TargetStartByte))
            {
                canonical.Append("P|")
                    .Append(parameter.InstructionIndex.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(parameter.TargetStartByte.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(parameter.SourceStartByte.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(parameter.ByteCount.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(parameter.UnkID.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()))
                    .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static EMEVD.Instruction InitializeEventCall(params int[] arguments)
        {
            byte[] data = new byte[arguments.Length * sizeof(int)];
            for (int index = 0; index < arguments.Length; index++)
                Buffer.BlockCopy(BitConverter.GetBytes(arguments[index]), 0, data, index * sizeof(int), sizeof(int));
            return new EMEVD.Instruction(2000, 0, data);
        }

        private static EMEVD.Parameter CopyParameter(EMEVD.Parameter parameter) =>
            new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID };

        private static bool IsKingFamilyAreaCheck(EMEVD.Instruction instruction, int areaEntity)
        {
            byte[] args = instruction.ArgData;
            return instruction.Bank == 3 && instruction.ID == 2 && args.Length == 12
                && unchecked((sbyte)args[0]) == 3 && args[1] == 0
                && BitConverter.ToInt32(args, 4) == 10000
                && BitConverter.ToInt32(args, 8) == areaEntity;
        }

        private static void ReconstructEndingCycleEvent(EMEVD.Event evt)
        {
            RequireEventShape(evt, EMEVD.Event.RestBehaviorType.Default, 10);
            EMEVD.Instruction incrementCycle = evt.Instructions.SingleOrDefault(instruction =>
                instruction.Bank == 2003 && instruction.ID == 21 && instruction.ArgData.Length == 4
                && instruction.ArgData[0] == 1);
            Require(incrementCycle != null, "Event 21 must contain its one IncrementGameCycle operation.");

            bool pristineOrder = evt.Instructions[5].Bank == 2002 && evt.Instructions[5].ID == 3
                && evt.Instructions[6].Bank == 1001 && evt.Instructions[6].ID == 1
                && evt.Instructions[7] == incrementCycle;
            bool fogOrder = evt.Instructions[5] == incrementCycle
                && evt.Instructions[6].Bank == 2002 && evt.Instructions[6].ID == 3
                && evt.Instructions[7].Bank == 1001 && evt.Instructions[7].ID == 1;
            Require(pristineOrder || fogOrder, "Event 21 ending-cycle operation order is unexpected.");
            if (pristineOrder)
            {
                evt.Instructions.RemoveAt(7);
                evt.Instructions.Insert(5, incrementCycle);
            }
        }

        private static void ReconstructDemonDestroyedBonusEvent(EMEVD.Event evt)
        {
            RequireEventShape(evt, EMEVD.Event.RestBehaviorType.Default, 14);
            EMEVD.Instruction gate = evt.Instructions[8];
            RequireInstruction(gate, 3, 0, 8, "IfEventFlag");
            Require(gate.ArgData[0] == 1 && gate.ArgData[1] == 0 && gate.ArgData[2] == 0,
                "Event 11810300 bonus gate has an unexpected condition signature.");
            int currentFlag = BitConverter.ToInt32(gate.ArgData, 4);
            Require(currentFlag == VanillaDemonBonusGateFlag || currentFlag == DemonBonusGateFlag,
                "Event 11810300 bonus gate uses an unexpected flag.");
            if (currentFlag == VanillaDemonBonusGateFlag)
                evt.Instructions[8] = Rewrite(gate, 3, 0, (sbyte)1, (byte)0, (byte)0, DemonBonusGateFlag);
        }

        private sealed class RemovedInstructionSequenceSpec
        {
            public RemovedInstructionSequenceSpec(long eventId, int sourceInstructionCount,
                EMEVD.Event.RestBehaviorType restBehavior, EMEVD.Instruction[] sequence)
            {
                EventId = eventId;
                SourceInstructionCount = sourceInstructionCount;
                RestBehavior = restBehavior;
                Sequence = sequence;
            }

            public long EventId { get; private set; }
            public int SourceInstructionCount { get; private set; }
            public EMEVD.Event.RestBehaviorType RestBehavior { get; private set; }
            public EMEVD.Instruction[] Sequence { get; private set; }
        }

        private sealed class RemovedInstructionSetSpec
        {
            public RemovedInstructionSetSpec(long eventId, int sourceInstructionCount,
                EMEVD.Event.RestBehaviorType restBehavior, EMEVD.Instruction[] instructions)
            {
                EventId = eventId;
                SourceInstructionCount = sourceInstructionCount;
                RestBehavior = restBehavior;
                Instructions = instructions;
            }

            public long EventId { get; private set; }
            public int SourceInstructionCount { get; private set; }
            public EMEVD.Event.RestBehaviorType RestBehavior { get; private set; }
            public EMEVD.Instruction[] Instructions { get; private set; }
        }

        private static RemovedInstructionSequenceSpec RemoveObjectSfxPair(long eventId, int sourceInstructionCount,
            EMEVD.Event.RestBehaviorType restBehavior, int objectEntity, byte objectState, int sfxEntity,
            bool spawnSfxInsteadOfDelete)
        {
            EMEVD.Instruction sfxOperation = spawnSfxInsteadOfDelete
                ? new EMEVD.Instruction(2006, 2, new object[] { sfxEntity })
                : new EMEVD.Instruction(2006, 1, new object[] { sfxEntity, (byte)1 });
            return RemoveInstructionSequence(eventId, sourceInstructionCount, restBehavior,
                new EMEVD.Instruction[]
            {
                new EMEVD.Instruction(2005, 3, new object[] { objectEntity, objectState }),
                sfxOperation,
            });
        }

        private static RemovedInstructionSequenceSpec RemoveInstructionSequence(long eventId,
            int sourceInstructionCount, EMEVD.Event.RestBehaviorType restBehavior,
            params EMEVD.Instruction[] sequence)
        {
            return new RemovedInstructionSequenceSpec(eventId, sourceInstructionCount, restBehavior, sequence);
        }

        private static RemovedInstructionSequenceSpec RemoveSingleInstruction(long eventId, int sourceInstructionCount,
            EMEVD.Event.RestBehaviorType restBehavior, int bank, int instructionId, string argumentHex)
        {
            return new RemovedInstructionSequenceSpec(eventId, sourceInstructionCount, restBehavior,
                new[] { new EMEVD.Instruction(bank, instructionId, Convert.FromHexString(argumentHex)) });
        }

        private static RemovedInstructionSetSpec RemoveInstructionSet(long eventId, int sourceInstructionCount,
            EMEVD.Event.RestBehaviorType restBehavior, params EMEVD.Instruction[] instructions)
        {
            return new RemovedInstructionSetSpec(eventId, sourceInstructionCount, restBehavior, instructions);
        }

        private static void ReconstructMapEventInitializers(EMEVD archive, int sourceInstructionCount,
            params string[] initializerArguments)
        {
            ReconstructRemovedVanillaInstructionSets(archive,
                RemoveInstructionSet(50, sourceInstructionCount, EMEVD.Event.RestBehaviorType.Default,
                    initializerArguments.Select(arguments => new EMEVD.Instruction(2000, 0,
                        Convert.FromHexString(arguments))).ToArray()));
        }

        private static void ReconstructM11Event0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "2cacfc0f20f58e91dd54132597afbd4262dab52d2d7fa09e5d70e46191004363";
            const string targetFingerprint = "f2f61aa8712051baf7c11bec7522198ca180a4c29a7f2b523933750c1a767922";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M11 event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M11 event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M11 event 0 does not match its known pristine or reconstructed semantic form.");

            evt.Instructions.RemoveAt(86);
            evt.Instructions.RemoveAt(85);
            evt.Instructions.RemoveRange(72, 2);
            evt.Instructions[70] = new EMEVD.Instruction(1003, 1,
                Convert.FromHexString("0200000004000000"));
            evt.Instructions.RemoveAt(11);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M11 event 0 reconstruction did not produce the pinned semantic form.");
        }

        /// <summary>Reconstructs the Parish startup event's gate skips and omitted object/SFX initializers.</summary>
        private static void ReconstructM10ParishEvent0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "d2f9c9ab15e9fb513708b64f5f3442c7307be1658609209e1a5a91b70f9f63b6";
            const string targetFingerprint = "7ff2295e8fd4880aecdc6bc264fb919a34631d33fd86ec41ddffc2d30d619d65";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M10 Parish event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M10 Parish event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            if (evt.Instructions.Count > 140)
            {
                // GameDataWriter and the combined engine may append event initializers after the
                // static map body. Keep that generated suffix intact when the known prefix matches.
                var generatedPrefix = new EMEVD.Event(evt.ID, evt.RestBehavior);
                generatedPrefix.Instructions.AddRange(evt.Instructions.Take(140));
                if (EventSemanticFingerprint(generatedPrefix) == targetFingerprint) return;
            }
            Require(fingerprint == sourceFingerprint,
                "M10 Parish event 0 does not match its known pristine or reconstructed semantic form.");

            // Fog removes the Parish's obsolete object/SFX setup and six event initializers.
            // The three affected skip spans are reduced to the pinned two-instruction form.
            evt.Instructions[72] = new EMEVD.Instruction(1003, 1,
                Convert.FromHexString("0200000003000000"));
            evt.Instructions[93] = new EMEVD.Instruction(1003, 1,
                Convert.FromHexString("020000005503A800"));
            evt.Instructions[109] = new EMEVD.Instruction(1003, 1,
                Convert.FromHexString("020000005603A800"));
            evt.Instructions.RemoveRange(177, 6);
            evt.Instructions.RemoveRange(111, 2);
            evt.Instructions.RemoveRange(95, 4);
            evt.Instructions.RemoveRange(74, 4);
            evt.Instructions.RemoveRange(5, 27);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M10 Parish event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM10Event0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "a76cbc1b8e5f3fb41bf32f1ebf75cbd9eedcb9e11d3c41f972cc4088b51bbc2a";
            const string targetFingerprint = "9ad406aec4d12d5a32dfcd8bbab86c7a9ab47b09e150d4b92ead166eba4864b1";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M10 event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M10 event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M10 event 0 does not match its known pristine or reconstructed semantic form.");

            evt.Instructions.RemoveRange(78, 2);
            evt.Instructions.RemoveRange(71, 3);
            evt.Instructions.InsertRange(71, new[]
            {
                new EMEVD.Instruction(2000, 0, Convert.FromHexString("00000000DEECA700D6420F008C460F00")),
                new EMEVD.Instruction(2000, 0, Convert.FromHexString("01000000DEECA700D7420F008D460F00")),
                new EMEVD.Instruction(2000, 0, Convert.FromHexString("02000000DEECA700D8420F008E460F00")),
            });
            evt.Instructions.RemoveRange(36, 2);
            evt.Instructions[34] = new EMEVD.Instruction(1003, 1,
                Convert.FromHexString("0200000002000000"));
            evt.Instructions.RemoveAt(18);
            evt.Instructions.RemoveRange(12, 4);
            evt.Instructions[10] = new EMEVD.Instruction(1003, 5,
                Convert.FromHexString("01010000"));

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M10 event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM15Event0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "85d756da112d5afff81aba179f375cd603a15df5ed5af3ee051e6761a7f02abd";
            const string targetFingerprint = "a4cf0868b91a8c0bf40db5fcd19674e773f4c6a71ee359123c532db7e24fc9b4";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M15 event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M15 event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M15 event 0 does not match its known pristine or reconstructed semantic form.");

            evt.Instructions.RemoveRange(167, 2);
            evt.Instructions.RemoveRange(129, 2);
            evt.Instructions[127] = new EMEVD.Instruction(1003, 1,
                Convert.FromHexString("020000000B000000"));
            evt.Instructions.RemoveRange(71, 2);
            evt.Instructions.RemoveRange(10, 3);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M15 event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM18DlcEvent0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "b25f33d2b6ed0da547aab1c803b11ae738bde8d2940ab114435b16e92d08378b";
            const string targetFingerprint = "4a52231102cde3df8a70b64462e10fd5917fa9085b0668076392dfda6b50da56";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M18 DLC event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M18 DLC event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M18 DLC event 0 does not match its known pristine or reconstructed semantic form.");

            RequireInstruction(evt, 14, 2000, 0, "000000002A35B400F4A41B00F5A41B0078A81B0079A81B00");
            RequireInstruction(evt, 15, 2000, 0, "00000000D034B40000000000");
            RequireInstruction(evt, 98, 1003, 1, "01000000B925AF03");
            RequireInstruction(evt, 100, 1003, 1, "0700000010000000");
            RequireInstruction(evt, 102, 2005, 3, "16A61B0000000000");
            RequireInstruction(evt, 103, 2006, 1, "17A61B0000000000");
            RequireInstruction(evt, 114, 2005, 3, "B2A51B0000000000");
            RequireInstruction(evt, 115, 2006, 1, "B3A51B0000000000");

            evt.Instructions[98] = InsHex(1003, 1, "010000000836B400");
            evt.Instructions[100] = InsHex(1003, 1, "0500000010000000");
            evt.Instructions.RemoveRange(114, 2);
            evt.Instructions.RemoveRange(102, 2);
            evt.Instructions.RemoveRange(14, 2);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M18 DLC event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM13DlcEvent0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "875fd91181512c14f373d196be44a6a6294a1693c6a98e7b20cabcc06e273017";
            const string targetFingerprint = "4ce9fc121121eb6c523f9138cfe666dba0dc2d763d53ec772fdd28d4bbd570e7";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M13 DLC event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M13 DLC event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M13 DLC event 0 does not match its known pristine or reconstructed semantic form.");

            RequireInstruction(evt, 16, 3, 6, "01010000");
            RequireInstruction(evt, 17, 3, 8, "01010D01");
            RequireInstruction(evt, 18, 1000, 1, "02010100");
            RequireInstruction(evt, 19, 2005, 3, "FA04140000000000");
            RequireInstruction(evt, 20, 2006, 1, "FB04140000000000");
            RequireInstruction(evt, 26, 2000, 0, "000000000A94AC00D4031400D50314005807140059071400");
            RequireInstruction(evt, 53, 1003, 1, "0400000007000000");
            RequireInstruction(evt, 55, 2005, 3, "F604140000000000");
            RequireInstruction(evt, 56, 2006, 1, "F704140000000000");
            RequireInstruction(evt, 59, 2000, 0, "00000000BFA8AC0000000000");
            RequireInstruction(evt, 67, 2000, 0, "00000000DEA4AC0000000000");
            RequireInstruction(evt, 74, 2000, 0, "0000000083AAAC0007000000F6041400E6081400E5081400");
            RequireInstruction(evt, 75, 2000, 0, "0000000086AAAC0007000000F6041400F7041400");

            evt.Instructions[53] = InsHex(1003, 1, "0200000007000000");
            evt.Instructions.RemoveRange(74, 2);
            evt.Instructions.RemoveAt(67);
            evt.Instructions.RemoveAt(59);
            evt.Instructions.RemoveRange(55, 2);
            evt.Instructions.RemoveAt(26);
            evt.Instructions.RemoveRange(16, 5);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M13 DLC event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM14Event0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "97fedb018f5f91e94cc39a0c60c6ae27b230c941c0248198e0e79eb70e2e1ba6";
            const string targetFingerprint = "372cc4327cca65aca9f73212bfdac4f4977b5f0f74ae25c14e05c49da98a99c9";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M14 event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M14 event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M14 event 0 does not match its known pristine or reconstructed semantic form.");

            RequireInstruction(evt, 34, 1003, 5, "06010000");
            RequireInstruction(evt, 35, 2005, 3, "8A64150000000000");
            RequireInstruction(evt, 36, 2006, 1, "8B64150000000000");
            RequireInstruction(evt, 37, 2005, 3, "8C64150000000000");
            RequireInstruction(evt, 38, 2006, 1, "8D64150000000000");
            RequireInstruction(evt, 39, 2005, 3, "8E64150000000000");
            RequireInstruction(evt, 40, 2006, 1, "8F64150000000000");
            RequireInstruction(evt, 48, 2000, 0, "010000009AF3AD006663150067631500EA661500EB661500");
            RequireInstruction(evt, 61, 1003, 1, "0600000009000000");
            RequireInstruction(evt, 63, 2005, 3, "8664150000000000");
            RequireInstruction(evt, 64, 2006, 1, "8764150000000000");
            RequireInstruction(evt, 65, 2005, 3, "8864150000000000");
            RequireInstruction(evt, 66, 2006, 1, "8964150000000000");
            RequireInstruction(evt, 101, 2000, 0, "00000000822EAE0000000000");
            RequireInstruction(evt, 102, 2000, 0, "00000000130AAE0009000000866415007668150075681500");
            RequireInstruction(evt, 103, 2000, 0, "00000000160AAE00090000008664150087641500");

            evt.Instructions[61] = InsHex(1003, 1, "0200000009000000");
            evt.Instructions.RemoveRange(101, 3);
            evt.Instructions.RemoveRange(63, 4);
            evt.Instructions.RemoveAt(48);
            evt.Instructions.RemoveRange(34, 7);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M14 event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM16Event0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "0184c18211be05fd7d1834690ee32fee20cc0a5f0776f161cc269dfba81cd62c";
            const string targetFingerprint = "500e2b36704146f834e0c9beb8bb3f83af1003e2684513c5b97debfe67082716";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M16 event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M16 event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M16 event 0 does not match its known pristine or reconstructed semantic form.");

            RequireInstruction(evt, 6, 1003, 5, "07010000");
            RequireInstruction(evt, 8, 2005, 3, "CA71180000000000");
            RequireInstruction(evt, 9, 2006, 1, "CB71180000000000");
            RequireInstruction(evt, 10, 2005, 3, "CC71180000000000");
            RequireInstruction(evt, 11, 2006, 1, "CD71180000000000");
            RequireInstruction(evt, 12, 2005, 3, "CE71180000000000");
            RequireInstruction(evt, 13, 2006, 1, "CF71180000000000");
            RequireInstruction(evt, 15, 2000, 0, "00000000DA00B100A4701800A57018002874180029741800");
            RequireInstruction(evt, 16, 2000, 0, "01000000DA00B100A6701800A77018002A7418002B741800");
            RequireInstruction(evt, 47, 1003, 1, "040000000D000000");
            RequireInstruction(evt, 49, 2005, 3, "C671180000000000");
            RequireInstruction(evt, 50, 2006, 1, "C771180000000000");
            RequireInstruction(evt, 149, 2000, 0, "000000005317B1000D000000C6711800B6751800B5751800");
            RequireInstruction(evt, 150, 2000, 0, "000000005617B1000D000000C6711800C7711800");

            evt.Instructions[6] = InsHex(1003, 5, "01010000");
            evt.Instructions[47] = InsHex(1003, 1, "020000000D000000");
            evt.Instructions.RemoveRange(149, 2);
            evt.Instructions.RemoveRange(49, 2);
            evt.Instructions.RemoveRange(15, 2);
            evt.Instructions.RemoveRange(8, 6);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M16 event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM17Event0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "bcc9abcc5cce63ddc8457af56d152b1c060557686f2abc3efa4724449c2885da";
            const string targetFingerprint = "16721e471b7265f35206bdf240f19e52f808083f6bcaf3cf573a334117151995";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M17 event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M17 event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M17 event 0 does not match its known pristine or reconstructed semantic form.");

            RequireInstruction(evt, 17, 1003, 5, "05010000");
            RequireInstruction(evt, 19, 2005, 3, "6AF8190000000000");
            RequireInstruction(evt, 20, 2006, 1, "6BF8190000000000");
            RequireInstruction(evt, 21, 2005, 3, "6CF8190000000000");
            RequireInstruction(evt, 22, 2006, 1, "6DF8190000000000");
            RequireInstruction(evt, 39, 2000, 0, "000000007387B2004AF719004BF71900CEFA1900CFFA1900");
            RequireInstruction(evt, 86, 1003, 1, "050000000E000000");
            RequireInstruction(evt, 89, 2005, 3, "66F8190000000000");
            RequireInstruction(evt, 90, 2006, 1, "67F8190000000000");
            RequireInstruction(evt, 173, 2000, 0, "00000000F39DB2000E00000066F8190056FC190055FC1900");
            RequireInstruction(evt, 174, 2000, 0, "00000000F69DB2000E00000066F8190067F81900");

            evt.Instructions.RemoveRange(173, 2);
            evt.Instructions.RemoveRange(89, 2);
            evt.Instructions.RemoveAt(86);
            evt.Instructions.InsertRange(86, new[]
            {
                InsHex(2000, 0, "000000002587B20000000000"),
                InsHex(1003, 1, "030000000E000000"),
            });
            evt.Instructions.RemoveAt(39);
            evt.Instructions.RemoveRange(19, 4);
            evt.Instructions[17] = InsHex(1003, 5, "01010000");

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M17 event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM12Event0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "b206d7aa56fd458d1c830c0aa0365ca49d5908a89454ef9d6cf5b41fe2b9ce6a";
            const string targetFingerprint = "698a193bbea77de7e916bf4b384f948c9d3edb34fa01b475cc93e5b96f32c53b";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M12 event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M12 event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M12 event 0 does not match its known pristine or reconstructed semantic form.");

            RequireInstruction(evt, 8, 1003, 5, "0A010000");
            RequireInstruction(evt, 9, 2005, 3, "4A57120000000000");
            RequireInstruction(evt, 10, 2006, 1, "4B57120000000000");
            RequireInstruction(evt, 11, 2005, 3, "4C57120000000000");
            RequireInstruction(evt, 12, 2006, 1, "4D57120000000000");
            RequireInstruction(evt, 13, 2005, 3, "4E57120000000000");
            RequireInstruction(evt, 14, 2006, 1, "4F57120000000000");
            RequireInstruction(evt, 15, 2005, 3, "4457120000000000");
            RequireInstruction(evt, 16, 2006, 1, "4557120000000000");
            RequireInstruction(evt, 17, 2005, 3, "4257120000000000");
            RequireInstruction(evt, 18, 2006, 1, "4357120000000000");
            RequireInstruction(evt, 19, 2000, 0, "000000005AE6AA002456120025561200A8591200A9591200");
            RequireInstruction(evt, 25, 2000, 0, "0100000064E6AA006FE6AA0072531200D5D40100455912000100000075D8A503");
            RequireInstruction(evt, 26, 2000, 0, "010000006EE6AA0065E6AA00725312004559120001000000");
            RequireInstruction(evt, 35, 1003, 1, "0400000005000000");
            RequireInstruction(evt, 37, 2005, 3, "4657120000000000");
            RequireInstruction(evt, 38, 2006, 1, "4757120000000000");
            RequireInstruction(evt, 51, 1003, 1, "0600000084E9AA00");
            RequireInstruction(evt, 53, 2005, 3, "E256120000000000");
            RequireInstruction(evt, 54, 2006, 1, "E356120000000000");
            RequireInstruction(evt, 55, 2005, 3, "E456120000000000");
            RequireInstruction(evt, 56, 2006, 1, "E556120000000000");
            RequireInstruction(evt, 133, 2000, 0, "00000000D3FCAA0084E9AA00E2561200D25A1200CE5A1200");
            RequireInstruction(evt, 134, 2000, 0, "00000000D6FCAA0084E9AA00E2561200E3561200");
            RequireInstruction(evt, 135, 2000, 0, "01000000D3FCAA000500000046571200365B1200355B1200");
            RequireInstruction(evt, 136, 2000, 0, "01000000D6FCAA00050000004657120047571200");

            evt.Instructions.RemoveRange(133, 4);
            evt.Instructions.RemoveRange(53, 4);
            evt.Instructions[51] = InsHex(1003, 1, "0200000084E9AA00");
            evt.Instructions.RemoveRange(37, 2);
            evt.Instructions[35] = InsHex(1003, 1, "0200000005000000");
            evt.Instructions.RemoveRange(25, 2);
            evt.Instructions.Insert(25, InsHex(2005, 7, "7253120001000000"));
            evt.Instructions.RemoveRange(8, 12);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M12 event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM12DlcEvent0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "c2de75b8cb861e16d429e0129388e7f9f2a064012e0041aabbbc3b98a8184e94";
            const string targetFingerprint = "9de0222f8fcf516b1610cd06171c7a1cafc48934aba34631d5a3cc404cdeb2bb";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M12 DLC event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M12 DLC event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M12 DLC event 0 does not match its known pristine or reconstructed semantic form.");

            RequireInstruction(evt, 1, 1003, 5, "0A010000");
            RequireInstruction(evt, 2, 2005, 3, "547E120000000000");
            RequireInstruction(evt, 3, 2006, 1, "557E120000000000");
            RequireInstruction(evt, 4, 2005, 3, "5A7E120000000000");
            RequireInstruction(evt, 5, 2006, 1, "5B7E120000000000");
            RequireInstruction(evt, 6, 2005, 3, "5C7E120000000000");
            RequireInstruction(evt, 7, 2006, 1, "5D7E120000000000");
            RequireInstruction(evt, 8, 2005, 3, "5E7E120000000000");
            RequireInstruction(evt, 9, 2006, 1, "5F7E120000000000");
            RequireInstruction(evt, 10, 2005, 3, "527E120000000000");
            RequireInstruction(evt, 11, 2006, 1, "537E120000000000");
            RequireInstruction(evt, 20, 1003, 1, "06000000100DAB00");
            RequireInstruction(evt, 22, 2005, 3, "8E7D120000000000");
            RequireInstruction(evt, 23, 2006, 1, "8F7D120000000000");
            RequireInstruction(evt, 24, 2005, 3, "907D120000000000");
            RequireInstruction(evt, 25, 2006, 1, "917D120000000000");
            RequireInstruction(evt, 41, 1003, 1, "07000000110DAB00");
            RequireInstruction(evt, 43, 2005, 3, "F27D120000000000");
            RequireInstruction(evt, 44, 2006, 1, "F37D120000000000");
            RequireInstruction(evt, 45, 2005, 3, "F47D120000000000");
            RequireInstruction(evt, 46, 2006, 1, "F57D120000000000");
            RequireInstruction(evt, 57, 1003, 1, "04000000120DAB00");
            RequireInstruction(evt, 59, 2005, 3, "567E120000000000");
            RequireInstruction(evt, 60, 2006, 1, "577E120000000000");
            RequireInstruction(evt, 74, 1000, 1, "03010100");
            RequireInstruction(evt, 75, 2005, 3, "2A7D120000000000");
            RequireInstruction(evt, 76, 2006, 1, "2B7D120000000000");
            RequireInstruction(evt, 157, 2000, 0, "000000009C30AB00B62CAB00787A12000000000001000000797A1200");
            RequireInstruction(evt, 158, 2000, 0, "010000009C30AB00B72CAB00787A12000A000000000000007A7A1200");
            RequireInstruction(evt, 210, 2000, 0, "00000000A930AB0000000000");
            RequireInstruction(evt, 211, 2000, 0, "00000000AA30AB0000000000");
            RequireInstruction(evt, 212, 2000, 0, "00000000FFFFFFFF00000000");
            RequireInstruction(evt, 213, 2000, 0, "00000000401CAB0000000000");
            RequireInstruction(evt, 214, 2000, 0, "00000000E623AB00100DAB008E7D12008F7D1200");
            RequireInstruction(evt, 215, 2000, 0, "00000000E323AB00100DAB008E7D1200D8811200D7811200");
            RequireInstruction(evt, 216, 2000, 0, "01000000E623AB00110DAB00F27D1200F37D1200");
            RequireInstruction(evt, 217, 2000, 0, "01000000E323AB00110DAB00F27D1200E2811200E1811200");
            RequireInstruction(evt, 218, 2000, 0, "02000000E623AB00140DAB002A7D12002B7D1200");
            RequireInstruction(evt, 219, 2000, 0, "02000000E323AB00140DAB002A7D1200EC811200EB811200");
            RequireInstruction(evt, 220, 2000, 0, "03000000E623AB00120DAB00567E1200577E1200");
            RequireInstruction(evt, 221, 2000, 0, "03000000E323AB00120DAB00567E12004682120045821200");

            evt.Instructions.RemoveRange(210, 12);
            evt.Instructions.RemoveRange(157, 2);
            evt.Instructions[74] = InsHex(1000, 1, "01010100");
            evt.Instructions.RemoveRange(75, 2);
            evt.Instructions.RemoveRange(59, 2);
            evt.Instructions[57] = InsHex(1003, 1, "02000000120DAB00");
            evt.Instructions.RemoveRange(43, 4);
            evt.Instructions[41] = InsHex(1003, 1, "03000000110DAB00");
            evt.Instructions.RemoveRange(22, 4);
            evt.Instructions[20] = InsHex(1003, 1, "02000000100DAB00");
            evt.Instructions.RemoveRange(1, 11);
            evt.Instructions.InsertRange(1, new[]
            {
                InsHex(2005, 3, "FC7D120000000000"),
                InsHex(2006, 1, "FD7D120000000000"),
            });

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M12 DLC event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM15DlcEvent0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "2c754bb366bf0c535724a21b2f029c57f7d99184849f465a41e88b013bba9d9d";
            const string targetFingerprint = "799b4ad6de4df6c07a5b744fe62d8bc8b98266a721cdb9058a632edf57f93325";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M15 DLC event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M15 DLC event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M15 DLC event 0 does not match its known pristine or reconstructed semantic form.");

            RequireInstruction(evt, 8, 1003, 5, "02010000");
            RequireInstruction(evt, 9, 2005, 3, "3A12170000000000");
            RequireInstruction(evt, 10, 2006, 1, "3B12170000000000");
            RequireInstruction(evt, 38, 2000, 0, "000000004AA1AF0014111700151117009814170099141700");
            RequireInstruction(evt, 39, 2000, 0, "010000004AA1AF0016111700171117009A1417009B141700");
            RequireInstruction(evt, 92, 1003, 1, "0A0000000C000000");
            RequireInstruction(evt, 96, 2005, 3, "3612170000000000");
            RequireInstruction(evt, 97, 2006, 1, "3712170000000000");
            RequireInstruction(evt, 98, 2005, 3, "3812170000000000");
            RequireInstruction(evt, 99, 2006, 1, "3912170000000000");
            RequireInstruction(evt, 100, 2005, 3, "3412170000000000");
            RequireInstruction(evt, 101, 2006, 1, "3512170000000000");
            RequireInstruction(evt, 115, 1003, 1, "0600000074A4AF00");
            RequireInstruction(evt, 117, 2005, 3, "D211170000000000");
            RequireInstruction(evt, 118, 2006, 1, "D311170000000000");
            RequireInstruction(evt, 119, 2005, 3, "D411170000000000");
            RequireInstruction(evt, 120, 2006, 1, "D511170000000000");
            RequireInstruction(evt, 124, 2000, 0, "00000000E4B9AF0000000000");
            RequireInstruction(evt, 223, 2000, 0, "00000000C3B7AF0076A4AF00361217002616170025161700");
            RequireInstruction(evt, 224, 2000, 0, "00000000C6B7AF0076A4AF003612170037121700");
            RequireInstruction(evt, 225, 2000, 0, "01000000C3B7AF0077A4AF00361217002616170025161700");
            RequireInstruction(evt, 226, 2000, 0, "01000000C6B7AF0077A4AF003612170037121700");
            RequireInstruction(evt, 227, 2000, 0, "02000000C3B7AF0074A4AF00D2111700C2151700C1151700");
            RequireInstruction(evt, 228, 2000, 0, "02000000C6B7AF0074A4AF00D2111700D3111700");

            evt.Instructions.RemoveRange(223, 6);
            evt.Instructions.RemoveAt(124);
            evt.Instructions.RemoveRange(117, 4);
            evt.Instructions[115] = InsHex(1003, 1, "0400000074A4AF00");
            evt.Instructions.RemoveRange(96, 6);
            evt.Instructions[92] = InsHex(1003, 1, "040000000C000000");
            evt.Instructions.Insert(60, InsHex(2000, 0, "00000000D7A1AF0000000000"));
            evt.Instructions.RemoveRange(38, 2);
            evt.Instructions.RemoveRange(8, 3);

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M15 DLC event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void ReconstructM14DlcEvent0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "a42816d1a4b3ab9d6042b4d7b4a09a5309f5af74ccb8130e08241d1317305937";
            const string targetFingerprint = "008d05faff01ecaf80347e6075346c2ff9772639df0056af8ffd8581e40b86d5";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M14 DLC event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M14 DLC event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            if (M14DlcEvent0RuntimeEntityVariantMatches(evt, targetFingerprint)) return;
            bool sourceSkipVariant = M14DlcEvent0SourceSkipVariantMatches(evt, sourceFingerprint);
            Require(fingerprint == sourceFingerprint || sourceSkipVariant,
                "M14 DLC event 0 does not match its known pristine or reconstructed semantic form.");

            RequireInstruction(evt, 8, 1003, 5, sourceSkipVariant ? "07010000" : "0E010000");
            RequireInstruction(evt, 9, 2005, 3, "9A8B150000000000");
            RequireInstruction(evt, 10, 2006, 1, "9B8B150000000000");
            RequireInstruction(evt, 11, 2005, 3, "9C8B150000000000");
            RequireInstruction(evt, 12, 2006, 1, "9D8B150000000000");
            RequireInstruction(evt, 13, 2005, 3, "9E8B150000000000");
            RequireInstruction(evt, 14, 2006, 1, "9F8B150000000000");
            RequireInstruction(evt, 19, 2005, 3, "908B150000000000");
            RequireInstruction(evt, 20, 2006, 1, "918B150000000000");
            RequireInstruction(evt, 59, 1003, 1, "04000000D41DAE00");
            RequireInstruction(evt, 61, 2005, 3, "CE8A150000000000");
            RequireInstruction(evt, 62, 2006, 1, "CF8A150000000000");
            RequireInstruction(evt, 80, 1003, 1, "06000000EA1BAE00");
            RequireInstruction(evt, 82, 2005, 3, "5289150000000000");
            RequireInstruction(evt, 83, 2006, 1, "5389150000000000");
            RequireInstruction(evt, 84, 2005, 3, "5489150000000000");
            RequireInstruction(evt, 85, 2006, 1, "5589150000000000");
            RequireInstruction(evt, 95, 1003, 1, "06000000D51DAE00");
            RequireInstruction(evt, 97, 2005, 3, "328B150000000000");
            RequireInstruction(evt, 98, 2006, 1, "338B150000000000");
            RequireInstruction(evt, 99, 2005, 3, "348B150000000000");
            RequireInstruction(evt, 100, 2006, 1, "358B150000000000");
            RequireInstruction(evt, 110, 1003, 1, "040000000A000000");
            RequireInstruction(evt, 112, 2005, 3, "968B150000000000");
            RequireInstruction(evt, 113, 2006, 1, "978B150000000000");
            RequireInstruction(evt, 199, 2000, 0, "000000002331AE000A000000968B1500868F1500858F1500");
            RequireInstruction(evt, 200, 2000, 0, "000000002631AE000A000000968B1500978B1500");
            RequireInstruction(evt, 201, 2000, 0, "010000002331AE00D41DAE00CE8A15005A8E1500598E1500");
            RequireInstruction(evt, 202, 2000, 0, "010000002631AE00D41DAE00CE8A1500CF8A1500");
            RequireInstruction(evt, 203, 2000, 0, "020000002331AE00D51DAE00328B1500228F1500218F1500");
            RequireInstruction(evt, 204, 2000, 0, "020000002631AE00D51DAE00328B1500338B1500");
            RequireInstruction(evt, 205, 2000, 0, "030000002331AE00EA1BAE00528915003B8D15003C8D1500");
            RequireInstruction(evt, 206, 2000, 0, "030000002631AE00EA1BAE005289150053891500");

            evt.Instructions.RemoveRange(199, 8);
            evt.Instructions.RemoveRange(112, 2);
            evt.Instructions[110] = InsHex(1003, 1, "020000000A000000");
            evt.Instructions.RemoveRange(97, 4);
            evt.Instructions[95] = InsHex(1003, 1, "02000000D51DAE00");
            evt.Instructions.RemoveRange(82, 4);
            evt.Instructions[80] = InsHex(1003, 1, "02000000EA1BAE00");
            evt.Instructions.RemoveRange(61, 2);
            evt.Instructions[59] = InsHex(1003, 1, "02000000D41DAE00");
            evt.Instructions.RemoveRange(19, 2);
            evt.Instructions.RemoveRange(8, 7);
            evt.Instructions.Insert(8, InsHex(1003, 5, "06010000"));

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M14 DLC event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static bool M14DlcEvent0SourceSkipVariantMatches(EMEVD.Event evt, string expectedFingerprint)
        {
            if (evt.Instructions.Count != 207
                || evt.Instructions[8].Bank != 1003 || evt.Instructions[8].ID != 5
                || !evt.Instructions[8].ArgData.SequenceEqual(Convert.FromHexString("07010000"))) return false;

            EMEVD.Event normalized = CloneEvent(evt);
            normalized.Instructions[8] = InsHex(1003, 5, "0E010000");
            return EventSemanticFingerprint(normalized) == expectedFingerprint;
        }

        private static bool M14DlcEvent0RuntimeEntityVariantMatches(EMEVD.Event evt, string expectedFingerprint)
        {
            if (evt.Instructions.Count != 179) return false;
            EMEVD.Event normalized = CloneEvent(evt);
            for (int index = 35; index <= 48; index++)
            {
                EMEVD.Instruction instruction = normalized.Instructions[index];
                int expectedEntity = index == 35 ? 0x00AE1B18 : 0x00AE1B19;
                int observedEntity = index == 35 ? 0x00AE1B2C : 0x00AE1B2D;
                if (instruction.Bank != 2000 || instruction.ID != 0 || instruction.ArgData.Length < 8
                    || BitConverter.ToInt32(instruction.ArgData, 4) != observedEntity) return false;

                byte[] arguments = instruction.ArgData.ToArray();
                Buffer.BlockCopy(BitConverter.GetBytes(expectedEntity), 0, arguments, 4, 4);
                normalized.Instructions[index] = new EMEVD.Instruction(instruction.Bank, instruction.ID, arguments)
                {
                    Layer = instruction.Layer,
                };
            }
            return EventSemanticFingerprint(normalized) == expectedFingerprint;
        }

        private static EMEVD.Event CloneEvent(EMEVD.Event source)
        {
            var clone = new EMEVD.Event(source.ID, source.RestBehavior);
            clone.Instructions.AddRange(source.Instructions.Select(instruction =>
                new EMEVD.Instruction(instruction.Bank, instruction.ID, instruction.ArgData.ToArray())
                {
                    Layer = instruction.Layer,
                }));
            clone.Parameters.AddRange(source.Parameters.Select(parameter =>
                new EMEVD.Parameter(parameter.InstructionIndex, parameter.TargetStartByte,
                    parameter.SourceStartByte, parameter.ByteCount) { UnkID = parameter.UnkID }));
            return clone;
        }

        private static void ReconstructM13Event0(EMEVD.Event evt)
        {
            const string sourceFingerprint = "2b18b6cfdd24fa5d247bf034a0715b0a1b915b1f1dafb4745a87f6583645eba5";
            const string targetFingerprint = "ee297230b19882d508c879fa2c8d4d51d1288da7b1d97a17340d5bbfab6942ab";
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "M13 event 0 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "M13 event 0 unexpectedly has event parameters.");

            string fingerprint = EventSemanticFingerprint(evt);
            if (fingerprint == targetFingerprint) return;
            Require(fingerprint == sourceFingerprint,
                "M13 event 0 does not match its known pristine or reconstructed semantic form.");

            RequireInstruction(evt, 14, 1003, 5, "03010000");
            RequireInstruction(evt, 16, 2005, 3, "EADD130000000000");
            RequireInstruction(evt, 17, 2006, 1, "EBDD130000000000");
            RequireInstruction(evt, 18, 2000, 0, "00000000FA6CAC00C4DC1300C5DC13007AE013007BE01300");
            RequireInstruction(evt, 43, 2000, 0, "06000000446EAC0016270000");
            RequireInstruction(evt, 54, 2000, 0, "11000000446EAC0021270000");
            RequireInstruction(evt, 88, 1003, 1, "0400000006000000");
            RequireInstruction(evt, 89, 2005, 3, "E6DD130000000000");
            RequireInstruction(evt, 90, 2006, 1, "E7DD130000000000");
            RequireInstruction(evt, 136, 2000, 0, "000000007683AC0006000000E6DD1300E7DD1300");
            RequireInstruction(evt, 137, 2000, 0, "000000007383AC0006000000E6DD1300D6E11300D5E11300");

            evt.Instructions.RemoveRange(136, 2);
            evt.Instructions[88] = InsHex(1003, 1, "0200000006000000");
            evt.Instructions.RemoveRange(89, 2);
            evt.Instructions.RemoveRange(43, 12);
            evt.Instructions.RemoveRange(16, 3);
            evt.Instructions[14] = InsHex(1003, 5, "01010000");

            Require(EventSemanticFingerprint(evt) == targetFingerprint,
                "M13 event 0 reconstruction did not produce the pinned semantic form.");
        }

        private static void RequireInstruction(EMEVD.Event evt, int index, int bank, int instructionId,
            string argumentHex)
        {
            Require(index >= 0 && index < evt.Instructions.Count,
                "Event " + evt.ID + " is missing expected instruction " + index + ".");
            EMEVD.Instruction actual = evt.Instructions[index];
            Require(actual.Bank == bank && actual.ID == instructionId
                && actual.ArgData.SequenceEqual(Convert.FromHexString(argumentHex)),
                "Event " + evt.ID + " has an unexpected instruction at index " + index + ".");
        }

        private static void ReconstructRemovedVanillaInstructionSequences(EMEVD archive,
            params RemovedInstructionSequenceSpec[] specifications)
        {
            foreach (RemovedInstructionSequenceSpec specification in specifications)
            {
                EMEVD.Event evt = FindUniqueEvent(archive, specification.EventId);
                Require(evt.RestBehavior == specification.RestBehavior,
                    "Event " + evt.ID + " has an unexpected rest behavior.");
                Require(evt.Parameters.Count == 0, "Event " + evt.ID + " unexpectedly has event parameters.");

                int targetInstructionCount = specification.SourceInstructionCount - specification.Sequence.Length;
                Require(evt.Instructions.Count == specification.SourceInstructionCount
                    || evt.Instructions.Count == targetInstructionCount,
                    "Event " + evt.ID + " has instruction count " + evt.Instructions.Count
                    + " (expected " + specification.SourceInstructionCount + " or " + targetInstructionCount + ").");

                int[] matches = Enumerable.Range(0, evt.Instructions.Count - specification.Sequence.Length + 1)
                    .Where(index => InstructionSequenceMatches(evt, index, specification.Sequence))
                    .ToArray();
                if (evt.Instructions.Count == specification.SourceInstructionCount)
                {
                    Require(matches.Length == 1,
                        "Event " + evt.ID + " must contain exactly one authored operation sequence.");
                    evt.Instructions.RemoveRange(matches[0], specification.Sequence.Length);
                }
                else
                {
                    Require(matches.Length == 0,
                        "Event " + evt.ID + " still contains its removed operation sequence.");
                }
            }
        }

        private static void ReconstructRemovedVanillaInstructionSets(EMEVD archive,
            params RemovedInstructionSetSpec[] specifications)
        {
            foreach (RemovedInstructionSetSpec specification in specifications)
            {
                EMEVD.Event evt = FindUniqueEvent(archive, specification.EventId);
                Require(evt.RestBehavior == specification.RestBehavior,
                    "Event " + evt.ID + " has an unexpected rest behavior.");
                Require(evt.Parameters.Count == 0,
                    "Event " + evt.ID + " unexpectedly has event parameters.");

                int targetInstructionCount = specification.SourceInstructionCount - specification.Instructions.Length;
                Require(evt.Instructions.Count == specification.SourceInstructionCount
                    || evt.Instructions.Count == targetInstructionCount,
                    "Event " + evt.ID + " has an unexpected instruction count.");

                var matches = new List<int>();
                foreach (EMEVD.Instruction expected in specification.Instructions)
                {
                    int[] found = Enumerable.Range(0, evt.Instructions.Count)
                        .Where(index => InstructionSequenceMatches(evt, index, new[] { expected }))
                        .ToArray();
                    if (evt.Instructions.Count == specification.SourceInstructionCount)
                        Require(found.Length == 1,
                            "Event " + evt.ID + " must contain each expected removed operation exactly once.");
                    else
                        Require(found.Length == 0,
                            "Event " + evt.ID + " still contains an operation removed by FogMod.");
                    matches.AddRange(found);
                }

                if (evt.Instructions.Count == targetInstructionCount)
                    continue;

                Require(matches.Distinct().Count() == specification.Instructions.Length,
                    "Event " + evt.ID + " has overlapping removed-operation matches.");
                foreach (int index in matches.OrderByDescending(index => index))
                    SkipRanges.Remove(evt, index);
                Require(evt.Instructions.Count == targetInstructionCount,
                    "Event " + evt.ID + " did not reach the expected reconstructed instruction count.");
            }
        }

        private static void ReconstructM10GateObjectStateEvent(EMEVD archive, long eventId,
            int sourceInstructionCount, int[] expectedRemovalIndexes,
            EMEVD.Instruction[] removed, EMEVD.Instruction[] appended)
        {
            EMEVD.Event evt = FindUniqueEvent(archive, eventId);
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            int targetInstructionCount = sourceInstructionCount - removed.Length + appended.Length;
            Require(evt.Instructions.Count == sourceInstructionCount || evt.Instructions.Count == targetInstructionCount,
                "Event " + evt.ID + " has an unexpected instruction count.");

            if (evt.Instructions.Count == sourceInstructionCount)
            {
                Require(expectedRemovalIndexes.Length == removed.Length,
                    "Event " + evt.ID + " has an invalid authored removal contract.");
                var removalIndexes = new List<int>();
                for (int i = 0; i < removed.Length; i++)
                {
                    EMEVD.Instruction instruction = removed[i];
                    int[] matches = Enumerable.Range(0, evt.Instructions.Count)
                        .Where(index => InstructionSequenceMatches(evt, index, new[] { instruction }))
                        .ToArray();
                    Require(matches.Length == 1 && matches[0] == expectedRemovalIndexes[i],
                        "Event " + evt.ID + " must contain each expected vanilla condition once at its authored position.");
                    removalIndexes.Add(matches[0]);
                }
                Require(removalIndexes.Distinct().Count() == removed.Length,
                    "Event " + evt.ID + " has overlapping vanilla-condition matches.");
                foreach (EMEVD.Instruction instruction in appended)
                    Require(CountInstruction(evt, instruction) == 0,
                        "Event " + evt.ID + " already contains a FogMod-appended operation.");

                foreach (int index in removalIndexes.OrderByDescending(index => index))
                    SkipRanges.Remove(evt, index);
                foreach (EMEVD.Instruction instruction in appended)
                    SkipRanges.InsertAfter(evt, evt.Instructions.Count, instruction);
                return;
            }

            Require(removed.All(instruction => CountInstruction(evt, instruction) == 0)
                && appended.All(instruction => CountInstruction(evt, instruction) == 1)
                && InstructionSequenceMatches(evt, evt.Instructions.Count - appended.Length, appended),
                "Event " + evt.ID + " does not match the known pristine or reconstructed gate/object-state shape.");
        }

        private static void ReconstructM10WatcherEvent(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] vanillaBody = M10WatcherVanillaBody();
            EMEVD.Instruction[] reconstructedBody = M10WatcherReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == vanillaBody.Length
                && InstructionSequenceMatches(evt, 0, vanillaBody),
                "Event 11005391 does not match the known pristine or reconstructed watcher body.");
            evt.Instructions.Insert(0, reconstructedBody[0]);
            evt.Instructions[4] = reconstructedBody[4];
            Require(InstructionSequenceMatches(evt, 0, reconstructedBody),
                "Event 11005391 reconstruction did not reach the expected watcher body.");
        }

        private static void ReconstructM10NewLondoTransition(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] vanillaBody = M10NewLondoTransitionVanillaBody();
            EMEVD.Instruction[] reconstructedBody = M10NewLondoTransitionReconstructedBody();
            if (evt.Instructions.Count == reconstructedBody.Length
                && InstructionSequenceMatches(evt, 0, reconstructedBody)) return;

            Require(evt.Instructions.Count == vanillaBody.Length
                && InstructionSequenceMatches(evt, 0, vanillaBody),
                "Event 11020021 does not match the known pristine or reconstructed transition body.");
            evt.Instructions.Clear();
            evt.Instructions.AddRange(reconstructedBody);
        }

        private static void ReconstructM10NewLondoFlagCleanup(EMEVD.Event evt)
        {
            const int sourceInstructionCount = 41;
            const int reconstructedInstructionCount = 37;
            EMEVD.Instruction skipAndCoveredOperations = new EMEVD.Instruction(1003, 7,
                Convert.FromHexString("02011201"));
            EMEVD.Instruction playerMapCondition = new EMEVD.Instruction(3, 8,
                Convert.FromHexString("00010A02"));
            EMEVD.Instruction setFlag = new EMEVD.Instruction(2003, 2,
                Convert.FromHexString("D034B40001000000"));
            EMEVD.Instruction oldFlagCondition = new EMEVD.Instruction(3, 0,
                Convert.FromHexString("00010000D034B400"));

            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event 11020001 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0,
                "Event 11020001 unexpectedly has event parameters.");

            if (evt.Instructions.Count == reconstructedInstructionCount)
            {
                Require(CountInstruction(evt, skipAndCoveredOperations) == 0
                    && CountInstruction(evt, playerMapCondition) == 0
                    && CountInstruction(evt, setFlag) == 0
                    && CountInstruction(evt, oldFlagCondition) == 0
                    && !evt.Instructions.Any(InstructionMerge.IsSkip),
                    "Event 11020001 does not match the known reconstructed flag-cleanup shape.");
                return;
            }

            Require(evt.Instructions.Count == sourceInstructionCount
                && InstructionSequenceMatches(evt, 2,
                    new[] { skipAndCoveredOperations, playerMapCondition, setFlag })
                && InstructionSequenceMatches(evt, 21, new[] { oldFlagCondition }),
                "Event 11020001 does not match the known pristine flag-cleanup shape.");

            int[] skipIndexes = evt.Instructions.Select((instruction, index) => (instruction, index))
                .Where(pair => InstructionMerge.IsSkip(pair.instruction))
                .Select(pair => pair.index)
                .ToArray();
            Require(skipIndexes.SequenceEqual(new[] { 2 })
                && InstructionMerge.Lines(evt.Instructions[2]) == 2,
                "Event 11020001 has an unexpected enclosing or nested skip range.");
            Require(CountInstruction(evt, skipAndCoveredOperations) == 1
                && CountInstruction(evt, playerMapCondition) == 1
                && CountInstruction(evt, setFlag) == 1
                && CountInstruction(evt, oldFlagCondition) == 1,
                "Event 11020001 has ambiguous flag-cleanup operations.");

            // The exact SKIP covers only the two operations removed with it; no other skip exists.
            SkipRanges.Remove(evt, 21);
            evt.Instructions.RemoveRange(2, 3);
            Require(evt.Instructions.Count == reconstructedInstructionCount,
                "Event 11020001 did not reach its reconstructed instruction count.");
        }

        private static EMEVD.Instruction[] M10NewLondoTransitionVanillaBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("00010000A43AA800")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000E026A800")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("0000A041")),
            new EMEVD.Instruction(2002, 4, Convert.FromHexString("86870100000000008EA61B001201000010270000")),
            new EMEVD.Instruction(2002, 3, Convert.FromHexString("A2BF02000000000010270000")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("E026A80001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("A43AA80000000000")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M10NewLondoTransitionReconstructedBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("00010000A43AA800")),
            new EMEVD.Instruction(1003, 1, Convert.FromHexString("01010000E026A800")),
            new EMEVD.Instruction(1001, 0, Convert.FromHexString("00002041")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("B514000001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("E026A80001000000")),
            new EMEVD.Instruction(2003, 2, Convert.FromHexString("A43AA80000000000")),
            new EMEVD.Instruction(2004, 9, Convert.FromHexString("10270000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")),
            new EMEVD.Instruction(1001, 1, Convert.FromHexString("01000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M10WatcherVanillaBody() => new[]
        {
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000002000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000D1EDA700")),
            new EMEVD.Instruction(4, 3, Convert.FromHexString("010000001027000001000000")),
            new EMEVD.Instruction(3, 19, Convert.FromHexString("0100000001000000F64D0F0000000000FFFF00000000000023BF9800FF00000000000000064A0F00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 14, Convert.FromHexString("10270000F54D0F00")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000F21C000000000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static EMEVD.Instruction[] M10WatcherReconstructedBody() => new[]
        {
            new EMEVD.Instruction(2000, 2, Convert.FromHexString("00000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("0100000002000000")),
            new EMEVD.Instruction(3, 0, Convert.FromHexString("01010000D1EDA700")),
            new EMEVD.Instruction(4, 3, Convert.FromHexString("010000001027000001000000")),
            new EMEVD.Instruction(3, 18, Convert.FromHexString("0100000001000000F64D0F0000000000FFFF00000000000023BF9800FF00000000000000064A0F00")),
            new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
            new EMEVD.Instruction(2004, 14, Convert.FromHexString("10270000F54D0F00")),
            new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000F21C000000000000")),
            new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
        };

        private static int CountInstruction(EMEVD.Event evt, EMEVD.Instruction expected) =>
            Enumerable.Range(0, evt.Instructions.Count)
                .Count(index => InstructionSequenceMatches(evt, index, new[] { expected }));

        private static bool InstructionSequenceMatches(EMEVD.Event evt, int start, EMEVD.Instruction[] sequence)
        {
            for (int i = 0; i < sequence.Length; i++)
            {
                EMEVD.Instruction actual = evt.Instructions[start + i];
                EMEVD.Instruction expected = sequence[i];
                if (actual.Bank != expected.Bank || actual.ID != expected.ID || actual.Layer != expected.Layer
                    || !actual.ArgData.SequenceEqual(expected.ArgData)) return false;
            }
            return true;
        }

        private sealed class EventPrefixSpec
        {
            public EventPrefixSpec(long eventId, int sourceInstructionCount, EMEVD.Event.RestBehaviorType restBehavior, EMEVD.Instruction[] prefix)
            {
                EventId = eventId;
                SourceInstructionCount = sourceInstructionCount;
                RestBehavior = restBehavior;
                Prefix = prefix;
            }

            public long EventId { get; private set; }
            public int SourceInstructionCount { get; private set; }
            public EMEVD.Event.RestBehaviorType RestBehavior { get; private set; }
            public EMEVD.Instruction[] Prefix { get; private set; }
        }

        private static EventPrefixSpec Disabled(long eventId, int sourceInstructionCount, EMEVD.Event.RestBehaviorType restBehavior)
        {
            return new EventPrefixSpec(eventId, sourceInstructionCount, restBehavior, new[] { EndEventInstruction() });
        }

        private static EventPrefixSpec WaitOneFrame(long eventId, int sourceInstructionCount,
            EMEVD.Event.RestBehaviorType restBehavior)
        {
            return new EventPrefixSpec(eventId, sourceInstructionCount, restBehavior,
                new[] { new EMEVD.Instruction(1001, 1, new object[] { 1 }) });
        }

        private static EventPrefixSpec WaitForFlagThenEnd(long eventId, int sourceInstructionCount,
            EMEVD.Event.RestBehaviorType restBehavior, int flag)
        {
            return new EventPrefixSpec(eventId, sourceInstructionCount, restBehavior, new[]
            {
                new EMEVD.Instruction(3, 0, new object[] { (sbyte)0, (byte)1, (byte)0, flag }),
                EndEventInstruction(),
            });
        }

        private static EventPrefixSpec WaitForAreaThenEnd(long eventId, int sourceInstructionCount,
            EMEVD.Event.RestBehaviorType restBehavior, int region)
        {
            return new EventPrefixSpec(eventId, sourceInstructionCount, restBehavior, new[]
            {
                new EMEVD.Instruction(3, 2, new object[] { (sbyte)0, (byte)1, 10000, region }),
                EndEventInstruction(),
            });
        }

        private static void ReconstructM14DlcGateWatcher(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Default,
                "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0, "Event " + evt.ID + " unexpectedly has event parameters.");

            EMEVD.Instruction[] vanillaBody = M14DlcGateVanillaBody();
            EMEVD.Instruction[] reconstructedPrefix =
            {
                new EMEVD.Instruction(3, 2, new object[] { (sbyte)0, (byte)1, 10000, 0x00158F20 }),
                EndEventInstruction(),
            };

            if (evt.Instructions.Count == vanillaBody.Length
                && InstructionSequenceMatches(evt, 0, vanillaBody))
            {
                evt.Instructions.InsertRange(0, reconstructedPrefix);
                return;
            }

            if (evt.Instructions.Count == vanillaBody.Length + reconstructedPrefix.Length
                && PrefixMatches(evt, reconstructedPrefix)
                && InstructionSequenceMatches(evt, reconstructedPrefix.Length, vanillaBody))
                return;

            // A real FogMod run may place the randomized gate-area check ahead of the
            // reconstructed vanilla watcher and move the unconditional end behind its
            // generated condition group. Preserve that runtime form only when the
            // generated prefix and the untouched vanilla body both match exactly.
            if (evt.Instructions.Count == vanillaBody.Length + 4
                && IsWaitForAreaInGroup(evt.Instructions[0], -7, null)
                && IsWaitForAreaInGroup(evt.Instructions[1], -7, 0x00158F20)
                && InstructionSequenceMatches(evt, 2, new[]
                {
                    new EMEVD.Instruction(0, 0, new object[] { (byte)0, (byte)1, (sbyte)-7, (byte)0 }),
                    EndEventInstruction(),
                })
                && InstructionSequenceMatches(evt, 4, vanillaBody))
                return;

            throw new InvalidDataException("Event " + evt.ID
                + " does not match the known pristine, reconstructed, or generated gate-watcher shape.");
        }

        private static bool IsWaitForAreaInGroup(EMEVD.Instruction instruction, sbyte conditionGroup, int? region)
        {
            if (instruction.Bank != 3 || instruction.ID != 2 || instruction.ArgData.Length != 12
                || unchecked((sbyte)instruction.ArgData[0]) != conditionGroup || instruction.ArgData[1] != 1
                || BitConverter.ToInt32(instruction.ArgData, 4) != 10000)
                return false;
            int actualRegion = BitConverter.ToInt32(instruction.ArgData, 8);
            return region.HasValue ? actualRegion == region.Value : actualRegion != 0;
        }

        private static EMEVD.Instruction[] M14DlcGateVanillaBody()
        {
            return new[]
            {
                new EMEVD.Instruction(3, 0, Convert.FromHexString("01000000D51DAE00")),
                new EMEVD.Instruction(3, 19, Convert.FromHexString(
                    "0100000001000000228F150000000000FFFF00000000000023BF98003000000000000000328B1500")),
                new EMEVD.Instruction(0, 0, Convert.FromHexString("00010100")),
                new EMEVD.Instruction(2004, 14, Convert.FromHexString("10270000218F1500")),
                new EMEVD.Instruction(2003, 18, Convert.FromHexString("10270000F21C000000000000")),
                new EMEVD.Instruction(1000, 4, Convert.FromHexString("01000000")),
            };
        }

        private static EventPrefixSpec EndWhenFlagSetOrWaitForFlagThenEnd(long eventId, int sourceInstructionCount,
            EMEVD.Event.RestBehaviorType restBehavior, int alreadyCompleteFlag, int completionFlag)
        {
            return new EventPrefixSpec(eventId, sourceInstructionCount, restBehavior, new[]
            {
                new EMEVD.Instruction(1003, 2, new object[] { (byte)0, (byte)1, (byte)0, alreadyCompleteFlag }),
                new EMEVD.Instruction(3, 0, new object[] { (sbyte)0, (byte)1, (byte)0, completionFlag }),
                EndEventInstruction(),
            });
        }

        private static EMEVD.Instruction EndEventInstruction()
        {
            return new EMEVD.Instruction(1000, 4, new byte[] { 0, 0, 0, 0 });
        }

        private static void ReconstructPrefixedEvents(EMEVD archive, params EventPrefixSpec[] specifications)
        {
            foreach (var specification in specifications)
            {
                EMEVD.Event evt = FindUniqueEvent(archive, specification.EventId);
                Require(evt.RestBehavior == specification.RestBehavior,
                    "Event " + evt.ID + " has an unexpected rest behavior.");
                Require(evt.Parameters.Count == 0, "Event " + evt.ID + " unexpectedly has event parameters.");
                Require(evt.Instructions.Count == specification.SourceInstructionCount
                    || evt.Instructions.Count == specification.SourceInstructionCount + specification.Prefix.Length,
                    "Event " + evt.ID + " has " + evt.Instructions.Count + " instructions; expected "
                    + specification.SourceInstructionCount + " before reconstruction or "
                    + (specification.SourceInstructionCount + specification.Prefix.Length) + " after reconstruction. Actual operations: "
                    + string.Join(", ", evt.Instructions.Select(instruction => instruction.Bank + "[" + instruction.ID + "]:" + BitConverter.ToString(instruction.ArgData))) + ".");

                if (evt.Instructions.Count == specification.SourceInstructionCount + specification.Prefix.Length)
                {
                    Require(PrefixMatches(evt, specification.Prefix),
                        "Event " + evt.ID + " has an unexpected inserted prefix.");
                    continue;
                }

                Require(evt.Instructions.Count > 0 && !PrefixMatches(evt, specification.Prefix),
                    "Event " + evt.ID + " source already begins with its reconstructed prefix.");
                evt.Instructions.InsertRange(0, specification.Prefix);
            }
        }

        private static bool PrefixMatches(EMEVD.Event evt, EMEVD.Instruction[] prefix)
        {
            if (evt.Instructions.Count < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                EMEVD.Instruction actual = evt.Instructions[i];
                EMEVD.Instruction expected = prefix[i];
                if (actual.Bank != expected.Bank || actual.ID != expected.ID || actual.Layer != expected.Layer
                    || !actual.ArgData.SequenceEqual(expected.ArgData)) return false;
            }
            return true;
        }

        private static bool IsEndEvent(EMEVD.Instruction instruction)
        {
            return instruction.Bank == 1000 && instruction.ID == 4 && instruction.ArgData.Length == 4
                && instruction.ArgData[0] == 0 && instruction.ArgData[1] == 0
                && instruction.ArgData[2] == 0 && instruction.ArgData[3] == 0;
        }

        private static void ReconstructSeathCrystalWatcher(EMEVD.Event evt)
        {
            Require(evt.RestBehavior == EMEVD.Event.RestBehaviorType.Restart,
                "Event 11705383 has an unexpected rest behavior.");
            Require(evt.Parameters.Count == 0, "Event 11705383 unexpectedly has event parameters.");
            Require(evt.Instructions.Count == 9 || evt.Instructions.Count == 10,
                "Event 11705383 has an unexpected instruction count.");
            RequireInstruction(evt.Instructions[0], 2004, 1, 8, "SetCharacterAIState");
            Require(BitConverter.ToInt32(evt.Instructions[0].ArgData, 0) == 0x0019F35C
                && evt.Instructions[0].ArgData[4] == 0,
                "Event 11705383 initial AI-state operation has an unexpected target or state.");

            if (evt.Instructions.Count == 10)
            {
                Require(IsEndEvent(evt.Instructions[1]), "Event 11705383 has an unexpected inserted operation.");
                return;
            }

            Require(!IsEndEvent(evt.Instructions[1]), "Event 11705383 source already contains the observed terminator.");
            evt.Instructions.Insert(1, new EMEVD.Instruction(1000, 4, new byte[] { 0, 0, 0, 0 }));
        }

        private static EMEVD.Event FindUniqueEvent(EMEVD archive, long id)
        {
            EMEVD.Event[] matches = archive.Events.Where(evt => evt.ID == id).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("Expected exactly one source event " + id + "; found " + matches.Length + ".");
            return matches[0];
        }

        private static void RequireEventShape(EMEVD.Event evt, EMEVD.Event.RestBehaviorType restBehavior, int instructionCount)
        {
            Require(evt.RestBehavior == restBehavior, "Event " + evt.ID + " has an unexpected rest behavior.");
            Require(evt.Instructions.Count == instructionCount, "Event " + evt.ID + " has an unexpected instruction count.");
            Require(evt.Parameters.Count == 0, "Event " + evt.ID + " unexpectedly has event parameters.");
        }

        private static bool IsMapGuard(EMEVD.Instruction instruction, sbyte conditionGroup, byte playerInside, byte area, byte block)
        {
            return instruction.ArgData.Length == 4
                && unchecked((sbyte)instruction.ArgData[0]) == conditionGroup
                && instruction.ArgData[1] == playerInside
                && instruction.ArgData[2] == area
                && instruction.ArgData[3] == block;
        }

        private static EMEVD.Instruction InsHex(int bank, int id, string arguments) =>
            new EMEVD.Instruction(bank, id, Convert.FromHexString(arguments));

        private static EMEVD.Instruction Rewrite(EMEVD.Instruction original, int bank, int id, params object[] arguments)
        {
            return new EMEVD.Instruction(bank, id, arguments) { Layer = original.Layer };
        }

        private static void RequireInstruction(EMEVD.Instruction instruction, int bank, int id, int argumentLength, string semanticName)
        {
            Require(instruction.Bank == bank && instruction.ID == id && instruction.ArgData.Length == argumentLength,
                "Expected " + semanticName + " at its authored event position.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
