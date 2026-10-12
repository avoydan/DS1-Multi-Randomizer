using System.Collections.Generic;
using System.Linq;
using FogHost;
using FogMod;

namespace FogHost.Tests
{
    internal static class FogAnnotationFingerprintTest
    {
        public static void Verify()
        {
            var original = Fixture(false);
            var equivalent = Fixture(true);
            Assert(FogAnnotationFingerprint.Compute(original) == FogAnnotationFingerprint.Compute(equivalent),
                "Unused fields, unconsumed tags, tag order, and equivalent numeric formatting must not change the consumed-model fingerprint.");
            AnnotationData.Annotations renamedStart = Fixture(false);
            renamedStart.CustomStarts[0].Name = "different-return-label";
            Assert(FogAnnotationFingerprint.Compute(original) != FogAnnotationFingerprint.Compute(renamedStart),
                "The custom-start name must affect the consumed-model fingerprint because it is written into the return message.");

            equivalent.Warps[0].ASide.Flag++;
            Assert(FogAnnotationFingerprint.Compute(original) == FogAnnotationFingerprint.Compute(equivalent),
                "Fields consumed only for ordinary entrances must be excluded from warp semantics.");
            equivalent = Fixture(false);
            equivalent.Warps[0].BSide.Cutscene++;
            Assert(FogAnnotationFingerprint.Compute(original) != FogAnnotationFingerprint.Compute(equivalent),
                "A warp cutscene target must change the consumed-model fingerprint.");

            equivalent = Fixture(false);
            equivalent.Entrances[0].ASide.Flag++;
            Assert(FogAnnotationFingerprint.Compute(original) != FogAnnotationFingerprint.Compute(equivalent),
                "A consumed ordinary-entrance flag must change the fingerprint.");

            equivalent = Fixture(false);
            equivalent.Entrances[0].DoorCond = "unused-condition";
            Assert(FogAnnotationFingerprint.Compute(original) == FogAnnotationFingerprint.Compute(equivalent),
                "DoorCond must not affect an ordinary entrance that is never a door.");
            equivalent = Fixture(false);
            equivalent.Entrances[0].Tags = "door";
            equivalent.Entrances[0].DoorCond = "AND start key-area";
            Assert(FogAnnotationFingerprint.Compute(original) != FogAnnotationFingerprint.Compute(equivalent),
                "A door's condition must affect the consumed-model fingerprint.");

            equivalent = Fixture(false);
            equivalent.Areas.Reverse();
            Assert(FogAnnotationFingerprint.Compute(original) != FogAnnotationFingerprint.Compute(equivalent),
                "Area order must be retained because it feeds graph construction and seeded edge ordering.");

            equivalent = Fixture(false);
            equivalent.Areas[0].To[0].Cond = "OR key_token key-area";
            Assert(FogAnnotationFingerprint.Compute(original) != FogAnnotationFingerprint.Compute(equivalent),
                "Condition operand order must be retained because equal-cost OR dependencies use the first branch.");

            var before = FogAnnotationFingerprint.ComputeCategories(original);
            equivalent = Fixture(false);
            equivalent.Entrances[0].ASide.Flag++;
            var after = FogAnnotationFingerprint.ComputeCategories(equivalent);
            Assert(before.Count == 9 && before["Entrances"] != after["Entrances"], "The semantic comparer must expose category-level entrance identity.");
            Assert(before.Where(entry => entry.Key != "Entrances").All(entry => entry.Value == after[entry.Key]),
                "A change in one annotation category must not alter unrelated category identities.");
        }

        private static AnnotationData.Annotations Fixture(bool equivalentNoise)
        {
            return new AnnotationData.Annotations
            {
                Options = new List<AnnotationData.ConfigAnnotation>
                {
                    new AnnotationData.ConfigAnnotation { Opt = equivalentNoise ? "different-unused-option" : "unused-option" },
                },
                Areas = new List<AnnotationData.Area>
                {
                    new AnnotationData.Area
                    {
                        Name = "start",
                        Tags = equivalentNoise ? "not-consumed optional boss" : "boss optional",
                        ScalingBase = null,
                        To = new List<AnnotationData.Side>
                        {
                            new AnnotationData.Side
                            {
                                Area = "key-area",
                                Cond = "OR key-area key_token",
                                Tags = equivalentNoise ? "drop shortcut hard" : "hard shortcut",
                            },
                        },
                    },
                    new AnnotationData.Area { Name = "key-area", Tags = "optional", To = new List<AnnotationData.Side>() },
                },
                KeyItems = new List<AnnotationData.Item>
                {
                    new AnnotationData.Item { Name = "key_token", ID = "3:123", Area = "key-area", Tags = "randomonly ignored" },
                },
                Warps = new List<AnnotationData.Entrance>
                {
                    new AnnotationData.Entrance
                    {
                        Name = "warp_gate", ID = 10, Area = "m10_00_00_00", Text = "Warp",
                        Comment = equivalentNoise ? "changed unused comment" : "unused comment", Tags = equivalentNoise ? "unique boss" : "unique",
                        ASide = new AnnotationData.Side { Area = "start", CustomWarp = equivalentNoise ? "start 1.0 2.00 3 4.000" : "start 1 2 3.0 4", Col = equivalentNoise ? "unused-col-b" : "unused-col-a" },
                        BSide = new AnnotationData.Side { Area = "key-area", Tags = equivalentNoise ? "player higher hard" : "player" },
                    },
                },
                Entrances = new List<AnnotationData.Entrance>
                {
                    new AnnotationData.Entrance
                    {
                        Name = "fog_gate", ID = 20, Area = "m10_00_00_00", Text = "Fog gate",
                        DoorCond = null, Tags = equivalentNoise ? "world unique norandom" : "world",
                        ASide = new AnnotationData.Side { Area = "start", Flag = 100, Tags = equivalentNoise ? "higher temp hard player" : "higher", BossTriggerArea = "1.0 2 3 4 5 6 7", CustomWarp = equivalentNoise ? "depths 1.0 2.00 3 4.000" : "depths 1 2 3.0 4" },
                        BSide = new AnnotationData.Side { Area = "key-area", DestinationMap = "m10_01_00_00" },
                    },
                },
                CustomStarts = new List<AnnotationData.CustomStart>
                {
                    new AnnotationData.CustomStart { Name = "return-to-start", Area = "start", Respawn = "m10_00_00_00 1000" },
                },
                DefaultCost = new Dictionary<string, float> { ["start"] = 0.25f },
                Enemies = new List<AnnotationData.Enemies>
                {
                    new AnnotationData.Enemies { Col = "start collision", Area = "start", Includes = new List<string> { "enemy_a" } },
                },
                LotLocations = new Dictionary<int, string> { [1300020] = "key-area" },
                DefaultFlagCols = new Dictionary<string, int> { ["start_h0001B1"] = 123456 },
            };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new System.InvalidOperationException(message);
        }
    }
}
