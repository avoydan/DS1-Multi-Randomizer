using System;
using System.IO;
using System.Linq;
using SoulsFormats;

namespace FogHost
{
    /// <summary>
    /// Project-authored replacements for small common Fog events whose complete
    /// behavior can be expressed from named DS1 EMEVD operations.
    /// </summary>
    public static class FogCommonEventBuilder
    {
        private const long Event5690 = 5690;
        private const long Event8900 = 8900;
        private const long Event8901 = 8901;
        private const long Event8950 = 8950;
        private const int CompletionGuardFlag = 0x00B434D0;
        private const int ActivationSourceFlag = 0x00B4353E;
        private const int SoftlockWarpRequestFlag = 5300;
        private const int RandomStartRequestFlag = 0x03168ED0;
        private const int RandomStartCompletedFlag = 0x00B43608;
        private const int RandomStartProcessedFlag = 6908;
        private const sbyte MainConditionGroup = 0;
        private const byte ConditionPassed = 1;
        private const sbyte CombinedConditionGroup = 1;
        private const sbyte AnyCompletionConditionGroup = -1;
        private const byte FlagOn = 1;
        private const byte FlagOff = 0;
        private const byte PlayerOutsideMap = 0;
        private const byte LordranArea = 18;
        private const byte NorthernUndeadAsylumBlock = 1;

        public static EMEVD.Event BuildEvent5690()
        {
            var evt = new EMEVD.Event(Event5690, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions.Add(new EMEVD.Instruction(1003, 2, new object[] { (byte)0, FlagOn, (byte)0, CompletionGuardFlag }));
            evt.Instructions.Add(new EMEVD.Instruction(3, 0, new object[] { CombinedConditionGroup, FlagOn, (byte)0, ActivationSourceFlag }));
            evt.Instructions.Add(new EMEVD.Instruction(3, 8, new object[] { CombinedConditionGroup, PlayerOutsideMap, LordranArea, NorthernUndeadAsylumBlock }));
            evt.Instructions.Add(new EMEVD.Instruction(0, 0, new object[] { MainConditionGroup, ConditionPassed, CombinedConditionGroup }));
            evt.Instructions.Add(new EMEVD.Instruction(2003, 2, new object[] { CompletionGuardFlag, FlagOn }));
            return evt;
        }

        public static EMEVD.Event BuildEvent8900()
        {
            // One-shot softlock fallback: a system request resets its flag,
            // warps to the supplied start point, and restarts the watcher.
            var evt = new EMEVD.Event(Event8900, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions.Add(IfEventFlag(MainConditionGroup, FlagOn, SoftlockWarpRequestFlag));
            evt.Instructions.Add(SetEventFlag(SoftlockWarpRequestFlag, FlagOff));
            evt.Instructions.Add(WarpPlayer(0, 0, -1));
            evt.Instructions.Add(EndUnconditionally(1));
            Bind(evt, 2, 0, 0, 1);
            Bind(evt, 2, 1, 1, 1);
            Bind(evt, 2, 4, 4, 4);
            return evt;
        }

        public static EMEVD.Event BuildEvent8901()
        {
            // A collision play-region flag mirrors its associated boss flag.
            // When the boss is already defeated, leave the collision enabled;
            // otherwise clear it after a short settle delay and wait for either
            // completion condition before restarting.
            var evt = new EMEVD.Event(Event8901, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions.Add(SetEventFlag(0, FlagOn));
            evt.Instructions.Add(EndIfEventFlag(0, FlagOn, 0));
            evt.Instructions.Add(WaitFixedTimeFrames(10));
            evt.Instructions.Add(SetEventFlag(0, FlagOff));
            evt.Instructions.Add(IfEventFlag(AnyCompletionConditionGroup, FlagOn, 0));
            evt.Instructions.Add(IfEventFlag(AnyCompletionConditionGroup, FlagOn, 0));
            evt.Instructions.Add(IfConditionGroup(MainConditionGroup, ConditionPassed, AnyCompletionConditionGroup));
            evt.Instructions.Add(EndUnconditionally(1));
            Bind(evt, 0, 0, 0, 4);
            Bind(evt, 1, 4, 4, 4);
            Bind(evt, 3, 0, 0, 4);
            Bind(evt, 4, 4, 4, 4);
            Bind(evt, 5, 4, 0, 4);
            return evt;
        }

        public static EMEVD.Event BuildEvent8950()
        {
            // Random start is one-shot: wait for its request, set the matching
            // respawn point, save, then warp to the generated start position.
            var evt = new EMEVD.Event(Event8950, EMEVD.Event.RestBehaviorType.Default);
            evt.Instructions.Add(WaitFixedTimeSeconds(0.5f));
            evt.Instructions.Add(EndIfEventFlag(0, FlagOn, RandomStartProcessedFlag));
            evt.Instructions.Add(IfEventFlag(MainConditionGroup, FlagOn, RandomStartRequestFlag));
            evt.Instructions.Add(SetPlayerRespawnPoint(0));
            evt.Instructions.Add(SetEventFlag(RandomStartProcessedFlag, FlagOn));
            evt.Instructions.Add(SetEventFlag(RandomStartCompletedFlag, FlagOn));
            evt.Instructions.Add(SaveRequest());
            evt.Instructions.Add(WarpPlayer(0, 0, -1));
            Bind(evt, 3, 0, 8, 4);
            Bind(evt, 7, 0, 0, 1);
            Bind(evt, 7, 1, 1, 1);
            Bind(evt, 7, 4, 4, 4);
            return evt;
        }

        public static void ReplaceAuthoredCommonEvents(EMEVD common)
        {
            ReplaceEvent(common, BuildEvent5690());
            ReplaceEvent(common, BuildEvent8900());
            ReplaceEvent(common, BuildEvent8901());
            ReplaceEvent(common, BuildEvent8950());
        }

        public static void ReplaceEvent5690(EMEVD common)
        {
            ReplaceEvent(common, BuildEvent5690());
        }

        private static void ReplaceEvent(EMEVD common, EMEVD.Event replacement)
        {
            if (common == null) throw new ArgumentNullException(nameof(common));
            int[] matches = Enumerable.Range(0, common.Events.Count)
                .Where(index => common.Events[index].ID == replacement.ID).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Expected exactly one existing common event {replacement.ID}; found {matches.Length}.");
            common.Events[matches[0]] = replacement;
        }

        private static void Bind(EMEVD.Event evt, int instructionIndex, int targetStart, int sourceStart, int byteCount)
        {
            evt.Parameters.Add(new EMEVD.Parameter(instructionIndex, targetStart, sourceStart, byteCount));
        }

        private static EMEVD.Instruction IfEventFlag(sbyte group, byte state, int flag) =>
            new EMEVD.Instruction(3, 0, new object[] { group, state, (byte)0, flag });

        private static EMEVD.Instruction EndIfEventFlag(byte endType, byte state, int flag) =>
            new EMEVD.Instruction(1003, 2, new object[] { endType, state, (byte)0, flag });

        private static EMEVD.Instruction IfConditionGroup(sbyte group, byte state, sbyte target) =>
            new EMEVD.Instruction(0, 0, new object[] { group, state, target });

        private static EMEVD.Instruction SetEventFlag(int flag, byte state) =>
            new EMEVD.Instruction(2003, 2, new object[] { flag, state });

        private static EMEVD.Instruction WaitFixedTimeFrames(int frames) =>
            new EMEVD.Instruction(1001, 1, new object[] { frames });

        private static EMEVD.Instruction WaitFixedTimeSeconds(float seconds) =>
            new EMEVD.Instruction(1001, 0, new object[] { seconds });

        private static EMEVD.Instruction SetPlayerRespawnPoint(int entity) =>
            new EMEVD.Instruction(2003, 23, new object[] { entity });

        private static EMEVD.Instruction SaveRequest() =>
            new EMEVD.Instruction(2000, 5, new object[] { (byte)0 });

        private static EMEVD.Instruction WarpPlayer(byte area, byte block, int initialArea) =>
            new EMEVD.Instruction(2003, 14, new object[] { area, block, initialArea });

        private static EMEVD.Instruction EndUnconditionally(byte endType) =>
            new EMEVD.Instruction(1000, 4, new object[] { endType });
    }
}
