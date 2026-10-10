using System;
using System.Collections.Generic;
using System.Linq;
using SoulsFormats;

namespace FogReconstruction
{
    internal static class TalkBytecodeNormalizerSelfTests
    {
        internal static void Run()
        {
            AssertBytes(
                new byte[] { 0x4A, 0xA1 },
                new byte[] { 0x82, 0x0A, 0x00, 0x00, 0x00, 0xA1 },
                "small integer literal is widened to Int32");

            AssertBytes(
                new byte[] { 0x66, 0x40, 0x40, 0x86, 0xA6, 0x41, 0x95, 0xA1 },
                new byte[] { 0x66, 0x82, 0, 0, 0, 0, 0x82, 0, 0, 0, 0, 0x86, 0x82, 1, 0, 0, 0, 0x95, 0xA1 },
                "compact function ID remains compact while literals widen and ContinueIfFalse is omitted");

            AssertBytes(
                new byte[] { 0x40, 0xB7, 0xA1 },
                new byte[] { 0x82, 0, 0, 0, 0, 0xA1 },
                "AbortIfFalse is represented by ESD condition flow and omitted from evaluator bytecode");

            AssertRepeatedNpcConversationPatch();
        }

        private static void AssertRepeatedNpcConversationPatch()
        {
            var esd = new ESD();
            var branchState = new ESD.State
            {
                Conditions = new List<ESD.Condition>(),
                EntryCommands = new List<ESD.CommandCall>(),
            };
            for (int i = 0; i < 17; i++) branchState.Conditions.Add(new ESD.Condition(1, new byte[] { 0x41, 0xA1 }));
            branchState.Conditions[3] = new ESD.Condition(22, new byte[] { 0x57, 0x84, 0xA7, 0x40, 0x95, 0xA1 });
            for (int i = 0; i < 15; i++)
            {
                int id = i < 3 ? 10 : 19;
                branchState.EntryCommands.Add(new ESD.CommandCall
                {
                    CommandBank = 1,
                    CommandID = id,
                    Arguments = new List<byte[]> { Int32Expression(i), Int32Expression(i), Int32Expression(i) },
                });
            }
            branchState.EntryCommands[4].Arguments[2] = Int32Expression(unchecked((int)0x00B434D0));
            branchState.EntryCommands[11].Arguments[0] = Int32Expression(12);
            branchState.EntryCommands[11].Arguments[1] = Int32Expression(unchecked((int)0x00E4E256));
            branchState.EntryCommands[11].Arguments[2] = Int32Expression(0x02CD);
            branchState.EntryCommands[13].Arguments[2] = Int32Expression(unchecked((int)0x00B434D0));
            branchState.EntryCommands[14].Arguments[2] = Int32Expression(unchecked((int)0x00B434D0));

            var resultState = new ESD.State
            {
                EntryCommands = new List<ESD.CommandCall>
                {
                    new ESD.CommandCall
                    {
                        CommandBank = 1,
                        CommandID = 11,
                        Arguments = new List<byte[]> { Int32Expression(unchecked((int)0x00A80222)), Int32Expression(1) },
                    },
                },
                Conditions = new List<ESD.Condition>
                {
                    new ESD.Condition(21, new byte[] { 0x41, 0xA1 }),
                    new ESD.Condition(18, new byte[] { 0x40, 0xA1 }),
                },
            };
            esd.StateGroups.Add(1, new Dictionary<long, ESD.State> { [4] = branchState, [17] = resultState });

            TalkBytecodeNormalizer.Normalize(esd);
            ESD.Condition inserted = branchState.Conditions[4];
            ESD.Condition replacement = resultState.Conditions.Single();
            ESD.CommandCall addedTalkList = branchState.EntryCommands[11];
            ESD.CommandCall resultEntry = resultState.EntryCommands.Single();
            if (branchState.Conditions.Count != 18 || inserted.TargetState != 17
                || !inserted.Evaluator.SequenceEqual(new byte[] { 0x57, 0x84, 0x82, 0x0F, 0, 0, 0, 0x95, 0xA1 })
                || replacement.TargetState != 22 || !replacement.Evaluator.SequenceEqual(new byte[] { 0x41, 0xA1 })
                || ReadInt(addedTalkList.Arguments[0]) != 15
                || ReadInt(addedTalkList.Arguments[1]) != unchecked((int)0x00E4E2D8)
                || ReadInt(addedTalkList.Arguments[2]) != 0x001AFE
                || ReadInt(resultEntry.Arguments[0]) != 0x14B4
                || ReadInt(resultEntry.Arguments[1]) != 1)
                throw new InvalidOperationException("Talk compatibility transform self-test failed for the repeated NPC conversation patch.");
            Console.WriteLine("PASS: repeated NPC conversation compatibility patch is parameterized and exact");
        }

        private static byte[] Int32Expression(int value)
        {
            byte[] result = new byte[6];
            result[0] = 0x82;
            BitConverter.GetBytes(value).CopyTo(result, 1);
            result[5] = 0xA1;
            return result;
        }

        private static int ReadInt(byte[] value) => BitConverter.ToInt32(value, 1);

        private static void AssertBytes(byte[] source, byte[] expected, string description)
        {
            byte[] actual = TalkBytecodeNormalizer.NormalizeExpression(source);
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException($"Talk bytecode self-test failed: {description}. Expected {BitConverter.ToString(expected)}, got {BitConverter.ToString(actual)}.");
            Console.WriteLine($"PASS: {description}");
        }
    }
}
