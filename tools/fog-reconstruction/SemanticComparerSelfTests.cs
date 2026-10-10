using System;
using System.Collections.Generic;

namespace FogReconstruction
{
    internal static class SemanticComparerSelfTests
    {
        public static void Run()
        {
            EmevdRetainsParametersAndInstructionArguments();
            EsdIgnoresContainerEntryNameButRetainsMachineSemantics();
            Console.WriteLine("Semantic comparer self-tests passed (4 assertions).");
        }

        private static void EmevdRetainsParametersAndInstructionArguments()
        {
            EmevdFileModel left = EventModel("00112233", 12, 4);
            EmevdFileModel same = EventModel("00112233", 12, 4);
            Require(SemanticComparer.Compare(left, same).Equal, "identical EMEVD semantics compare equal");

            EmevdFileModel changed = EventModel("00112234", 12, 4);
            SemanticComparison comparison = SemanticComparer.Compare(left, changed);
            Require(!comparison.Equal && comparison.FirstDifference.Contains("rawArgsHex", StringComparison.Ordinal),
                "raw instruction arguments are compared");

            changed = EventModel("00112233", 13, 4);
            comparison = SemanticComparer.Compare(left, changed);
            Require(!comparison.Equal && comparison.FirstDifference.Contains("targetStartByte", StringComparison.Ordinal),
                "event parameter target offsets are compared");
        }

        private static void EsdIgnoresContainerEntryNameButRetainsMachineSemantics()
        {
            EsdFileModel left = EsdModel("m10_00/t100000.esd", 25);
            EsdFileModel sameProgramDifferentContainerName = EsdModel("m10_01/t100000.esd", 25);
            Require(SemanticComparer.Compare(left, sameProgramDifferentContainerName).Equal,
                "container entry name does not affect ESD program equality");

            EsdFileModel changed = EsdModel("m10_00/t100000.esd", 26);
            SemanticComparison comparison = SemanticComparer.Compare(left, changed);
            Require(!comparison.Equal && comparison.FirstDifference.Contains("targetState", StringComparison.Ordinal),
                "ESD transition targets are compared");
        }

        private static EmevdFileModel EventModel(string rawArgs, long targetOffset, long sourceOffset) => new EmevdFileModel
        {
            Format = "DarkSouls1",
            LinkedFileOffsets = new List<long>(),
            StringDataHex = "",
            Events = new List<EmevdEventModel>
            {
                new EmevdEventModel
                {
                    Id = 100,
                    Name = "",
                    RestBehavior = "Restart",
                    Instructions = new List<EmevdInstructionModel>
                    {
                        new EmevdInstructionModel { Bank = 2000, Id = 0, Layer = null, RawArgsHex = rawArgs, Anchor = "synthetic" },
                    },
                    Parameters = new List<EmevdParameterModel>
                    {
                        new EmevdParameterModel { InstructionIndex = 0, TargetStartByte = targetOffset, SourceStartByte = sourceOffset, ByteCount = 4, UnkId = 1 },
                    },
                },
            },
        };

        private static EsdFileModel EsdModel(string entryName, long targetState) => new EsdFileModel
        {
            EntryName = entryName,
            LongFormat = true,
            DarkSoulsCount = 0,
            Name = "",
            StateGroups = new List<EsdStateGroupModel>
            {
                new EsdStateGroupModel
                {
                    Id = 1,
                    States = new List<EsdStateModel>
                    {
                        new EsdStateModel
                        {
                            Id = 10,
                            Conditions = new List<EsdConditionModel>
                            {
                                new EsdConditionModel { TargetState = targetState, EvaluatorHex = "41A1", PassCommands = new List<EsdCommandModel>(), Subconditions = new List<EsdConditionModel>() },
                            },
                            EntryCommands = new List<EsdCommandModel>(),
                            ExitCommands = new List<EsdCommandModel>(),
                            WhileCommands = new List<EsdCommandModel>(),
                        },
                    },
                },
            },
        };

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Semantic comparer self-test failed: " + description);
        }
    }
}
