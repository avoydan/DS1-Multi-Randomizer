using System;
using System.Collections.Generic;
using System.Linq;
using SoulsFormats;

namespace FogHost
{
    /// <summary>
    /// Builds a project-authored warp dispatcher from named EMEVD operations.
    /// The input ABI matches FogMod's InitializeEvent(5700, ...) call payload;
    /// no serialized reference event body or raw argument blob is stored here.
    /// </summary>
    public static class Event5700Builder
    {
        private const long EventId = 5700;
        private const int SourceMode = 0;
        private const int SourceDestinationArea = 1;
        private const int SourceDestinationBlock = 2;
        private const int SourceBeforeWarpPositive = 3;
        private const int SourceP0 = 4;
        private const int SourceP1 = 8;
        private const int SourceP2 = 12;
        private const int SourceP3 = 16;
        private const int SourceP4 = 20;
        private const int SourceP5 = 24;
        private const int SourceP6Magnitude = 28;

        private const byte Equal = 0;
        private const byte NotEqual = 1;
        private const byte EndEvent = 0;
        private const byte RestartEvent = 1;
        private const byte ConditionPassed = 1;
        private const byte FlagOff = 0;
        private const byte FlagOn = 1;
        private const byte LocalFlag = 0;
        private const sbyte MainGroup = 0;
        private const sbyte OrGroup1 = -1;
        private const sbyte OrGroup2 = -2;
        private const int SystemWarpGuardFlag = 15;
        private const int WarpCompleteFlag = 6910;
        private const int ActionMessage = 10010403;
        private const int CannotEnterMessage = 15000281;
        private const int PlayerEntity = 10000;
        private const int PlayerWarpAnimation = 7410;

        public static EMEVD.Event Build()
        {
            var builder = new EventProgram();
            AddLordvesselDoorMode(builder);
            AddFixedWorldGateMode(builder);
            AddActionDrivenWarpMode(builder);
            AddFlagDrivenWarpMode(builder);
            return builder.ToEvent(EventId);
        }

        /// <summary>Replace the single event body while leaving event order and all other content intact.</summary>
        public static void ReplaceExisting(EMEVD common)
        {
            if (common == null) throw new System.ArgumentNullException(nameof(common));
            int[] eventIndices = Enumerable.Range(0, common.Events.Count)
                .Where(index => common.Events[index].ID == EventId).ToArray();
            if (eventIndices.Length != 1)
                throw new System.IO.InvalidDataException($"Expected exactly one existing event 5700 in common EMEVD; found {eventIndices.Length}.");

            common.Events[eventIndices[0]] = Build();
        }

        private static void AddLordvesselDoorMode(EventProgram builder)
        {
            // Mode 1 leaves the vanilla door transition in charge. This branch
            // checks its optional Lordvessel gate, then maintains the paired
            // door object/effect according to the multiplayer state.
            int modeDispatch = builder.SkipIfComparison(7, NotEqual, 0, 1);
            builder.Bind(modeDispatch, 4, SourceMode, 1);

            int optionalFlag = builder.SkipIfComparison(1, Equal, 0, 0);
            builder.Bind(optionalFlag, 4, SourceP2, 4);
            int requireLordvessel = builder.IfEventFlag(MainGroup, FlagOn, LocalFlag, 0);
            builder.Bind(requireLordvessel, 4, SourceP2, 4);

            builder.SkipIfMultiplayerState(2, 1);
            int deactivateDoor = builder.DeactivateObject(0, FlagOff);
            builder.Bind(deactivateDoor, 0, SourceP0, 4);
            int clearDoorEffect = builder.DeleteMapSfx(0, false);
            builder.Bind(clearDoorEffect, 0, SourceP1, 4);
            builder.WaitSeconds(2f);
            builder.End(RestartEvent);
        }

        private static void AddFixedWorldGateMode(EventProgram builder)
        {
            // Mode 3 uses the paired action regions and traversal flag for a
            // non-randomized world gate. Once open, its gate object/effect is
            // cleared; otherwise either side can trigger the paired short warp.
            int modeDispatch = builder.SkipIfComparison(18, NotEqual, 0, 3);
            builder.Bind(modeDispatch, 4, SourceMode, 1);
            int traversalFlagUnset = builder.SkipIfEventFlag(3, FlagOff, LocalFlag, 0);
            builder.Bind(traversalFlagUnset, 4, SourceP2, 4);
            int deactivateOpenGate = builder.DeactivateObject(0, FlagOff);
            builder.Bind(deactivateOpenGate, 0, SourceP0, 4);
            int deleteOpenGateEffect = builder.DeleteMapSfx(0, false);
            builder.Bind(deleteOpenGateEffect, 0, SourceP1, 4);
            builder.End(EndEvent);

            int frontAction = builder.IfActionButtonLineSegment(
                1, 1, 0, 0f, 0, 0f, ActionMessage, 48, 0, 0);
            builder.Bind(frontAction, 8, SourceP3, 4);
            builder.Bind(frontAction, 36, SourceP0, 4);
            int backAction = builder.IfActionButtonLineSegment(
                2, 1, 0, 0f, 0, 0f, ActionMessage, 48, 0, 0);
            builder.Bind(backAction, 8, SourceP4, 4);
            builder.Bind(backAction, 36, SourceP0, 4);
            builder.IfConditionGroup(OrGroup1, ConditionPassed, 1);
            builder.IfConditionGroup(OrGroup1, ConditionPassed, 2);
            builder.IfConditionGroup(MainGroup, ConditionPassed, OrGroup1);
            builder.SkipIfConditionGroup(2, ConditionPassed, 2);

            int warpFromFront = builder.IssueShortWarp(PlayerEntity, 1, 0, -1);
            builder.Bind(warpFromFront, 8, SourceP3, 4);
            builder.Skip(1);
            int warpFromBack = builder.IssueShortWarp(PlayerEntity, 1, 0, -1);
            builder.Bind(warpFromBack, 8, SourceP4, 4);
            builder.ForcePlayerWarpAnimation(PlayerEntity, PlayerWarpAnimation);

            int deactivateTraversedGate = builder.DeactivateObject(0, FlagOff);
            builder.Bind(deactivateTraversedGate, 0, SourceP0, 4);
            int deleteTraversedGateEffect = builder.DeleteMapSfx(0, true);
            builder.Bind(deleteTraversedGateEffect, 0, SourceP1, 4);
            int setTraversalFlag = builder.SetEventFlag(0, FlagOn);
            builder.Bind(setTraversalFlag, 0, SourceP2, 4);
            builder.End(EndEvent);
        }

        private static void AddActionDrivenWarpMode(EventProgram builder)
        {
            // Mode 0 waits for the source-side action, while preserving escape,
            // trap, and entry-flag conditions. The action may be a fog gate or
            // another annotated traversal point.
            int modeDispatch = builder.SkipIfComparison(13, NotEqual, 0, 0);
            builder.Bind(modeDispatch, 4, SourceMode, 1);
            int noExitFlag = builder.SkipIfComparison(4, Equal, 0, 0);
            builder.Bind(noExitFlag, 4, SourceP4, 4);
            int waitForExitFlag = builder.IfEventFlag(OrGroup1, FlagOn, LocalFlag, 0);
            builder.Bind(waitForExitFlag, 4, SourceP4, 4);
            int noTrapFlag = builder.SkipIfComparison(1, Equal, 0, 0);
            builder.Bind(noTrapFlag, 4, SourceP2, 4);
            int waitOutsideTrap = builder.IfEventFlag(OrGroup1, FlagOff, LocalFlag, 0);
            builder.Bind(waitOutsideTrap, 4, SourceP2, 4);
            builder.IfConditionGroup(MainGroup, ConditionPassed, OrGroup1);

            int action = builder.IfActionButtonLineSegment(
                OrGroup2, 1, 0, 0f, -1, 0f, ActionMessage, 48, 0, 0);
            builder.Bind(action, 8, SourceP3, 4);
            builder.Bind(action, 36, SourceP0, 4);

            int noTrapAfterAction = builder.SkipIfComparison(3, Equal, 0, 0);
            builder.Bind(noTrapAfterAction, 4, SourceP2, 4);
            int exitFlagAlreadySet = builder.SkipIfEventFlag(2, FlagOn, LocalFlag, 0);
            builder.Bind(exitFlagAlreadySet, 4, SourceP4, 4);
            int requireTrapForExit = builder.IfEventFlag(5, FlagOn, LocalFlag, 0);
            builder.Bind(requireTrapForExit, 4, SourceP2, 4);
            builder.IfConditionGroup(OrGroup2, ConditionPassed, 5);
            builder.IfEventFlag(OrGroup2, FlagOn, LocalFlag, SystemWarpGuardFlag);
            builder.IfConditionGroup(MainGroup, ConditionPassed, OrGroup2);
            builder.EndIfConditionGroup(RestartEvent, ConditionPassed, 5);
        }

        private static void AddFlagDrivenWarpMode(EventProgram builder)
        {
            // Mode 2 consumes a source warp flag, optionally checks the entry
            // flag, applies the signed before-warp flag, saves, and transitions
            // to the requested map and destination player entity.
            int modeDispatch = builder.SkipIfComparison(4, NotEqual, 0, 2);
            builder.Bind(modeDispatch, 4, SourceMode, 1);
            int warpFlag = builder.IfEventFlag(OrGroup1, FlagOn, LocalFlag, 0);
            builder.Bind(warpFlag, 4, SourceP0, 4);
            builder.IfEventFlag(OrGroup1, FlagOn, LocalFlag, SystemWarpGuardFlag);
            builder.IfConditionGroup(MainGroup, ConditionPassed, OrGroup1);
            int clearConsumedWarpFlag = builder.SetEventFlag(0, FlagOff);
            builder.Bind(clearConsumedWarpFlag, 0, SourceP0, 4);
            builder.EndIfEventFlag(EndEvent, FlagOn, LocalFlag, SystemWarpGuardFlag);

            int noEntryFlag = builder.SkipIfComparison(4, Equal, 0, 0);
            builder.Bind(noEntryFlag, 4, SourceP5, 4);
            int entryAllowed = builder.SkipIfEventFlag(3, FlagOn, LocalFlag, 0);
            builder.Bind(entryAllowed, 4, SourceP5, 4);
            builder.DisplayEntryBlocked(CannotEnterMessage);
            builder.WaitSeconds(1f);
            builder.End(RestartEvent);

            int noBeforeWarpFlag = builder.SkipIfComparison(3, Equal, 0, 0);
            builder.Bind(noBeforeWarpFlag, 4, SourceP6Magnitude, 4);
            int setSignedBeforeWarpFlag = builder.SetEventFlag(0, FlagOff);
            builder.Bind(setSignedBeforeWarpFlag, 0, SourceP6Magnitude, 4);
            builder.Bind(setSignedBeforeWarpFlag, 4, SourceBeforeWarpPositive, 1);

            builder.WaitFrames(1);
            builder.SaveRequest();
            int mapTransition = builder.WarpPlayer(0, 0, -1);
            builder.Bind(mapTransition, 0, SourceDestinationArea, 1);
            builder.Bind(mapTransition, 1, SourceDestinationBlock, 1);
            builder.Bind(mapTransition, 4, SourceP1, 4);
            builder.SetEventFlag(WarpCompleteFlag, FlagOn);
            builder.End(RestartEvent);
        }

        private sealed class EventProgram
        {
            private readonly List<EMEVD.Instruction> _instructions = new List<EMEVD.Instruction>();
            private readonly List<EMEVD.Parameter> _parameters = new List<EMEVD.Parameter>();

            public int SkipIfComparison(byte skip, byte comparison, int left, int right) => Add(1000, 5, skip, comparison, left, right);
            public int SkipIfEventFlag(byte skip, byte state, byte targetType, int flag) => Add(1003, 1, skip, state, targetType, flag);
            public int IfEventFlag(sbyte resultGroup, byte state, byte targetType, int flag) => Add(3, 0, resultGroup, state, targetType, flag);
            public int SkipIfMultiplayerState(byte skip, sbyte desiredState) => Add(1003, 5, skip, desiredState);
            public int DeactivateObject(int entity, byte state) => Add(2005, 3, entity, state);
            public int DeleteMapSfx(int entity, bool onlyDeleteRoot) => Add(2006, 1, entity, onlyDeleteRoot ? (byte)1 : (byte)0);
            public int WaitSeconds(float seconds) => Add(1001, 0, seconds);
            public int WaitFrames(int frames) => Add(1001, 1, frames);
            public int End(byte type) => Add(1000, 4, type);
            public int IfActionButtonLineSegment(sbyte group, int entityType, int entity, float angle, short dummyPoly, float distance, int help, byte reaction, int pad, int line) =>
                Add(3, 18, group, entityType, entity, angle, dummyPoly, distance, help, reaction, pad, line);
            public int IfConditionGroup(sbyte result, byte desired, sbyte target) => Add(0, 0, result, desired, target);
            public int SkipIfConditionGroup(byte skip, byte desired, sbyte target) => Add(1000, 7, skip, desired, target);
            public int IssueShortWarp(int entity, byte warpType, int destinationEntity, int dummyPoly) => Add(2004, 41, entity, warpType, destinationEntity, dummyPoly);
            public int Skip(byte count) => Add(1000, 3, count);
            public int ForcePlayerWarpAnimation(int entity, int animation) => Add(2003, 18, entity, animation, (byte)0, (byte)0, (byte)0);
            public int SetEventFlag(int flag, byte state) => Add(2003, 2, flag, state);
            public int EndIfConditionGroup(byte end, byte desired, sbyte group) => Add(1000, 8, end, desired, group);
            public int EndIfEventFlag(byte end, byte desired, byte targetType, int flag) => Add(1003, 2, end, desired, targetType, flag);
            public int DisplayEntryBlocked(int message) => Add(2007, 1, message, (short)1, (short)1, -1, 3f);
            public int SaveRequest() => Add(2000, 5, (byte)0);
            public int WarpPlayer(byte area, byte block, int initialArea) => Add(2003, 14, area, block, initialArea);

            public void Bind(int instructionIndex, int targetStartByte, int sourceStartByte, int byteCount)
            {
                if (instructionIndex < 0 || instructionIndex >= _instructions.Count) throw new ArgumentOutOfRangeException(nameof(instructionIndex));
                if (targetStartByte < 0 || byteCount <= 0 || targetStartByte + byteCount > _instructions[instructionIndex].ArgData.Length)
                    throw new ArgumentOutOfRangeException(nameof(targetStartByte), "Parameter target is outside the instruction argument buffer.");
                if (sourceStartByte < 0 || sourceStartByte + byteCount > 32)
                    throw new ArgumentOutOfRangeException(nameof(sourceStartByte), "Parameter source is outside the 32-byte Event5700 payload.");
                _parameters.Add(new EMEVD.Parameter(instructionIndex, targetStartByte, sourceStartByte, byteCount));
            }

            public EMEVD.Event ToEvent(long id)
            {
                Type restType = typeof(EMEVD.Event).GetProperty("RestBehavior").PropertyType;
                object restDefault = Enum.Parse(restType, "Default");
                var evt = (EMEVD.Event)Activator.CreateInstance(typeof(EMEVD.Event), id, restDefault);
                evt.Instructions = _instructions;
                evt.Parameters = _parameters;
                return evt;
            }

            private int Add(int bank, int id, params object[] args)
            {
                int index = _instructions.Count;
                _instructions.Add(new EMEVD.Instruction(bank, id, args));
                return index;
            }
        }
    }
}
