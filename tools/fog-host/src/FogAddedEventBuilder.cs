using System;
using System.IO;
using System.Linq;
using SoulsFormats;

namespace FogHost
{
    /// <summary>
    /// Project-authored reconstructions for small added events whose behavior
    /// is described by named DS1 EMEVD operations.
    /// </summary>
    public static class FogAddedEventBuilder
    {
        private const int KeyItemRewardReadyFlag = 6909;
        private const int KeyItemRewardLot = 9030;
        private const int AnorLondoRetryFlag = 5310;
        public const int SeathCrystalObjectEntityId = 1701801;
        private const int SeathCrystalSfx = 170004;
        public const int SeathCrystalActivatedFlag = 5308;
        public const long SeathCrystalInteractionEventId = 11700005;
        private const int ClassStartProcessedFlag = 6908;
        private const int AnorLondoPlayerEntity = 10000;

        public static EMEVD.Event BuildEvent11210403()
        {
            var evt = NewEvent(11210403);
            evt.Instructions.Add(SetNetworkSyncState(0));
            evt.Instructions.Add(IfMultiplayerState(1, 3));
            evt.Instructions.Add(IfEventFlag(1, FlagOn, 765));
            int[] arenaRegions =
            {
                0x0012811F, 0x00128120, 0x00128121, 0x00128122, 0x00128123,
                0x00128124, 0x00128125, 0x00128126, 0x00128127, 0x00128128,
                0x0012812C, 0x0012812D, 0x0012812E, 0x0012812F, 0x00128130,
                0x00128131, 0x00128132, 0x00128133, 0x00128134, 0x00128135,
            };
            foreach (int region in arenaRegions)
                evt.Instructions.Add(IfInoutsideArea(-1, FlagOn, AnorLondoPlayerEntity, region));
            evt.Instructions.Add(IfConditionGroup(1, FlagOn, -1));
            evt.Instructions.Add(IfConditionGroup(0, FlagOn, 1));
            evt.Instructions.Add(DisplayGenericDialog(150400, 1, 1, -1, 5f));
            evt.Instructions.Add(WaitSeconds(10f));
            evt.Instructions.Add(EndUnconditionally(RestartEvent));
            return evt;
        }

        public static EMEVD.Event BuildEvent11210410()
        {
            var evt = NewEvent(11210410);
            evt.Instructions.Add(SkipIfEventFlag(10, FlagOn, 0));
            evt.Instructions.Add(IfEventFlag(1, FlagOff, 765));
            evt.Instructions.Add(IfEventFlag(1, FlagOff, 0x00AB221E));
            evt.Instructions.Add(IfEventFlag(1, FlagOn, 0));
            evt.Instructions.Add(IfInoutsideArea(1, FlagOn, AnorLondoPlayerEntity, 0));
            evt.Instructions.Add(IfEventFlag(5, FlagOn, 0));
            evt.Instructions.Add(IfEventFlag(5, FlagOff, 0x00AB221E));
            evt.Instructions.Add(IfElapsedSeconds(5, 15f));
            evt.Instructions.Add(IfConditionGroup(-2, FlagOn, 1));
            evt.Instructions.Add(IfConditionGroup(-2, FlagOn, 5));
            evt.Instructions.Add(IfConditionGroup(0, FlagOn, -2));
            evt.Instructions.Add(SetEventFlag(0, FlagOn));
            evt.Instructions.Add(SkipIfMultiplayerState(1, 2));
            evt.Instructions.Add(DisplayMessage(150000, 1));
            evt.Instructions.Add(SpawnMapSfx(0));
            evt.Instructions.Add(SkipIfEventFlag(11, FlagOff, 0));
            evt.Instructions.Add(IfEventFlag(2, FlagOn, 0));
            evt.Instructions.Add(IfInoutsideArea(2, FlagOff, AnorLondoPlayerEntity, 0));
            evt.Instructions.Add(IfMultiplayerState(3, 2));
            evt.Instructions.Add(IfEventFlag(3, FlagOff, 0));
            evt.Instructions.Add(IfElapsedSeconds(3, 5f));
            evt.Instructions.Add(IfMultiplayerState(4, 3));
            evt.Instructions.Add(IfEventFlag(4, FlagOff, 0));
            evt.Instructions.Add(IfConditionGroup(-1, FlagOn, 2));
            evt.Instructions.Add(IfConditionGroup(-1, FlagOn, 3));
            evt.Instructions.Add(IfConditionGroup(-1, FlagOn, 4));
            evt.Instructions.Add(IfConditionGroup(0, FlagOn, -1));
            evt.Instructions.Add(SetEventFlag(0, FlagOff));
            evt.Instructions.Add(DeleteMapSfx(0, FlagOn));
            evt.Instructions.Add(EndUnconditionally(RestartEvent));
            Bind(evt, 4, 8, 0, 4);
            Bind(evt, 17, 8, 0, 4);
            Bind(evt, 3, 4, 4, 4);
            Bind(evt, 16, 4, 4, 4);
            Bind(evt, 22, 4, 4, 4);
            Bind(evt, 14, 0, 8, 4);
            Bind(evt, 28, 0, 8, 4);
            Bind(evt, 0, 4, 12, 4);
            Bind(evt, 11, 0, 12, 4);
            Bind(evt, 15, 4, 12, 4);
            Bind(evt, 27, 0, 12, 4);
            Bind(evt, 5, 4, 16, 4);
            Bind(evt, 19, 4, 16, 4);
            return evt;
        }

        public static EMEVD.Event BuildEvent11210710()
        {
            var evt = NewEvent(11210710);
            evt.Instructions.Add(IfActionButton(0, 0, 0, 180f, 100, 2f, 0, 48, 0));
            evt.Instructions.Add(SetEventFlag(0x00AB21ED, FlagOn));
            evt.Instructions.Add(SetEventFlag(0x00AB0FD5, FlagOn));
            evt.Instructions.Add(RotateCharacter(AnorLondoPlayerEntity, 0));
            evt.Instructions.Add(ForceAnimationPlayback(AnorLondoPlayerEntity, 7114, FlagOff, FlagOff, FlagOff));
            evt.Instructions.Add(WaitSeconds(0.7f));
            evt.Instructions.Add(SpawnOneshotSfx(2, AnorLondoPlayerEntity, 17, 0x00015FA5));
            evt.Instructions.Add(WaitSeconds(1.5f));
            evt.Instructions.Add(IfHPRatio(1, AnorLondoPlayerEntity, 0, 0f));
            evt.Instructions.Add(EndIfConditionGroup(EndEvent, FlagOn, 1));
            evt.Instructions.Add(SkipIfComparison(1, 0, 0, 0));
            evt.Instructions.Add(BatchSetEventFlags(7000, 7799, FlagOff));
            evt.Instructions.Add(SkipIfComparison(1, 0, 1, 0));
            evt.Instructions.Add(BattleOfStoicismExitRequest());
            evt.Instructions.Add(BatchSetEventFlags(8140, 8146, FlagOff));
            evt.Instructions.Add(WarpPlayer(12, 1, -1));
            Bind(evt, 0, 8, 0, 4);
            Bind(evt, 3, 4, 0, 4);
            Bind(evt, 15, 4, 4, 4);
            Bind(evt, 0, 24, 8, 4);
            Bind(evt, 10, 8, 12, 4);
            Bind(evt, 12, 8, 12, 4);
            return evt;
        }

        public static EMEVD.Event BuildEvent11210780()
        {
            var evt = NewEvent(11210780);
            evt.Instructions.Add(SkipIfMultiplayerState(1, 0));
            evt.Instructions.Add(SkipIfEventFlag(9, FlagOn, 0));
            evt.Instructions.Add(ReproduceObjectAnimation(0, 1));
            evt.Instructions.Add(ActivateHit(0, FlagOff));
            evt.Instructions.Add(IfMultiplayerState(1, 2));
            evt.Instructions.Add(IfInoutsideArea(-1, FlagOn, AnorLondoPlayerEntity, 0));
            evt.Instructions.Add(IfEventFlag(-1, FlagOn, 0));
            evt.Instructions.Add(IfConditionGroup(1, FlagOn, -1));
            evt.Instructions.Add(IfConditionGroup(0, FlagOn, 1));
            evt.Instructions.Add(ActivateHit(0, FlagOn));
            evt.Instructions.Add(ForceAnimationPlayback(0, 0, FlagOff, FlagOn, FlagOff));
            evt.Instructions.Add(IfEventFlag(2, FlagOff, 0x00AB21EC));
            evt.Instructions.Add(IfMultiplayerState(-2, 3));
            evt.Instructions.Add(IfEventFlag(3, FlagOff, 0));
            evt.Instructions.Add(IfAllPlayersInoutsideArea(3, FlagOff, 0));
            evt.Instructions.Add(IfConditionGroup(-2, FlagOn, 3));
            evt.Instructions.Add(IfConditionGroup(2, FlagOn, -2));
            evt.Instructions.Add(IfConditionGroup(0, FlagOn, 2));
            evt.Instructions.Add(ForceAnimationPlayback(0, 1, FlagOff, FlagOn, FlagOff));
            evt.Instructions.Add(EndUnconditionally(RestartEvent));
            Bind(evt, 5, 8, 0, 4);
            Bind(evt, 14, 4, 0, 4);
            Bind(evt, 2, 0, 4, 4);
            Bind(evt, 10, 0, 4, 4);
            Bind(evt, 18, 0, 4, 4);
            Bind(evt, 3, 0, 8, 4);
            Bind(evt, 9, 0, 8, 4);
            Bind(evt, 1, 4, 12, 4);
            Bind(evt, 6, 4, 12, 4);
            Bind(evt, 13, 4, 12, 4);
            return evt;
        }

        public static EMEVD.Event BuildEvent11210708()
        {
            var evt = NewEvent(11210708);
            int[] arenaRegions = { 0x0012811C, 0x0012811D, 0x0012811E, 0x00128129, 0x0012812A, 0x0012812B };
            foreach (int region in arenaRegions)
                evt.Instructions.Add(IfInoutsideArea(-1, FlagOn, AnorLondoPlayerEntity, region));
            evt.Instructions.Add(IfConditionGroup(1, FlagOn, -1));
            evt.Instructions.Add(IfConditionGroup(0, FlagOn, 1));
            evt.Instructions.Add(IfPlayerHasItem(2, 3, 118, FlagOn));
            evt.Instructions.Add(EndIfConditionGroup(EndEvent, FlagOn, 2));
            evt.Instructions.Add(AwardItemLot(2200));
            return evt;
        }

        public static EMEVD.Event BuildEvent11210730()
        {
            var evt = NewEvent(11210730);
            evt.Instructions.Add(SetNetworkSyncState(0));
            evt.Instructions.Add(IfEventFlag(1, FlagOff, 0x00AB221E));
            evt.Instructions.Add(IfEventFlag(1, FlagOff, 765));
            evt.Instructions.Add(IfInoutsideArea(1, FlagOn, AnorLondoPlayerEntity, 0));
            evt.Instructions.Add(IfConditionGroup(0, FlagOn, 1));
            evt.Instructions.Add(SetSpEffect(AnorLondoPlayerEntity, 0));
            evt.Instructions.Add(WaitFrames(10));
            evt.Instructions.Add(EndUnconditionally(RestartEvent));
            Bind(evt, 3, 8, 0, 4);
            Bind(evt, 5, 4, 4, 4);
            return evt;
        }

        public static EMEVD.Event BuildEvent11210760()
        {
            var evt = NewEvent(11210760);
            evt.Instructions.Add(IfEventFlag(1, FlagOn, 0x00AB221E));
            evt.Instructions.Add(IfEventFlag(1, FlagOff, 0));
            evt.Instructions.Add(IfInoutsideArea(1, FlagOn, AnorLondoPlayerEntity, 0));
            evt.Instructions.Add(IfConditionGroup(0, FlagOn, 1));
            evt.Instructions.Add(WaitSeconds(1f));
            evt.Instructions.Add(ActivateHit(0, FlagOff));
            evt.Instructions.Add(ForceAnimationPlayback(0, 1, FlagOff, FlagOn, FlagOff));
            evt.Instructions.Add(ActivateHit(0, FlagOn));
            evt.Instructions.Add(EndUnconditionally(RestartEvent));
            Bind(evt, 2, 8, 0, 4);
            Bind(evt, 5, 0, 8, 4);
            Bind(evt, 6, 0, 4, 4);
            Bind(evt, 7, 0, 8, 4);
            Bind(evt, 1, 4, 12, 4);
            return evt;
        }

        public static EMEVD.Event BuildEvent11210880()
        {
            var evt = NewEvent(11210880);
            evt.Instructions.Add(SetNetworkSyncState(0));
            evt.Instructions.Add(EndIfMultiplayerState(EndEvent, 1));
            evt.Instructions.Add(IfMultiplayerState(1, 0));
            evt.Instructions.Add(IfMultiplayerState(1, 2));
            evt.Instructions.Add(IfEventFlag(1, FlagOff, 0x00AB21ED));
            evt.Instructions.Add(IfEventFlag(1, FlagOff, 0x00AB221E));
            evt.Instructions.Add(IfInoutsideArea(1, FlagOn, AnorLondoPlayerEntity, 0));
            evt.Instructions.Add(IfInoutsideArea(1, FlagOff, AnorLondoPlayerEntity, 0));
            evt.Instructions.Add(IfConditionGroup(0, FlagOn, 1));
            evt.Instructions.Add(PlayCutsceneAndWarpAllPlayers(120130, 2, 0, 12, 1));
            evt.Instructions.Add(WaitFrames(1));
            evt.Instructions.Add(EndUnconditionally(RestartEvent));
            Bind(evt, 6, 8, 0, 4);
            Bind(evt, 7, 8, 4, 4);
            Bind(evt, 9, 8, 4, 4);
            return evt;
        }

        public static EMEVD.Event BuildEvent8960()
        {
            // Award the selected class's optional starting lots after a short
            // delay, then always grant the fixed Estus lot.  Event parameters
            // keep the class and three class-specific lots in the event-0 calls.
            var evt = NewEvent(8960);
            evt.Instructions.Add(IfPlayersClass(1, 0));
            evt.Instructions.Add(EndIfConditionGroup(EndEvent, FlagOff, 1));
            evt.Instructions.Add(EndIfEventFlag(EndEvent, FlagOff, ClassStartProcessedFlag));
            evt.Instructions.Add(WaitSeconds(0.5f));
            evt.Instructions.Add(SkipIfComparison(1, 0, 0, 0));
            evt.Instructions.Add(AwardItemsIncludingClients(0));
            evt.Instructions.Add(AwardItemsIncludingClients(0));
            evt.Instructions.Add(AwardItemsIncludingClients(0));
            evt.Instructions.Add(AwardItemsIncludingClients(1082));
            Bind(evt, 0, 1, 0, 1);
            Bind(evt, 4, 4, 12, 4);
            Bind(evt, 5, 0, 12, 4);
            Bind(evt, 6, 0, 8, 4);
            Bind(evt, 7, 0, 4, 4);
            return evt;
        }

        public static EMEVD.Event BuildEvent11020303()
        {
            // Consume the one-shot retry request, then restart until the paired
            // map progression flags reach their stable state.
            var evt = NewEvent(11020303);
            evt.Instructions.Add(IfEventFlag(0, FlagOn, AnorLondoRetryFlag));
            evt.Instructions.Add(SetEventFlag(AnorLondoRetryFlag, FlagOff));
            evt.Instructions.Add(EndIfEventFlag(RestartEvent, FlagOff, 0x00A8280C));
            evt.Instructions.Add(EndIfEventFlag(RestartEvent, FlagOn, 0x00A8280E));
            return evt;
        }

        public static EMEVD.Event BuildEvent11510231()
        {
            // Award the delayed key-item lot exactly once after its request flag.
            var evt = NewEvent(11510231);
            evt.Instructions.Add(IfEventFlag(0, FlagOn, KeyItemRewardReadyFlag));
            evt.Instructions.Add(AwardItemsIncludingClients(KeyItemRewardLot));
            evt.Instructions.Add(SetEventFlag(KeyItemRewardReadyFlag, FlagOff));
            return evt;
        }

        public static EMEVD.Event BuildEvent11700005()
        {
            // Activate the Seath crystal interaction, attach its SFX, and set
            // the progression flag after the player uses its action prompt.
            var evt = NewEvent(SeathCrystalInteractionEventId);
            evt.Instructions.Add(DeactivateObject(SeathCrystalObjectEntityId, FlagOn));
            evt.Instructions.Add(SetObjectInvulnerability(SeathCrystalObjectEntityId, FlagOn));
            evt.Instructions.Add(CreateObjectFollowingSfx(SeathCrystalObjectEntityId, 100, SeathCrystalSfx));
            evt.Instructions.Add(IfActionButton(
                0, 0, SeathCrystalObjectEntityId, 180f, -1, 3f, 15000283, 48, 0));
            evt.Instructions.Add(SetEventFlag(SeathCrystalActivatedFlag, FlagOn));
            evt.Instructions.Add(WaitSeconds(1f));
            evt.Instructions.Add(EndUnconditionally(RestartEvent));
            return evt;
        }

        public static EMEVD.Event BuildEvent11708500()
        {
            // This MSB-linked event is an intentional no-op in the reference.
            var evt = NewEvent(11708500);
            evt.Instructions.Add(EndUnconditionally(EndEvent));
            return evt;
        }

        public static void ReplaceReconstructedAddedEvents(string fileName, EMEVD archive)
        {
            if (archive == null) throw new ArgumentNullException("archive");
            switch (fileName)
            {
                case "common.emevd.dcx":
                    ReplaceOrAppend(archive, BuildEvent8960());
                    break;
                case "m10_02_00_00.emevd.dcx":
                    ReplaceOrAppend(archive, BuildEvent11020303());
                    break;
                case "m12_01_00_00.emevd.dcx":
                    ReplaceOrAppend(archive, BuildEvent11210403());
                    ReplaceOrAppend(archive, BuildEvent11210410());
                    ReplaceOrAppend(archive, BuildEvent11210708());
                    ReplaceOrAppend(archive, BuildEvent11210710());
                    ReplaceOrAppend(archive, BuildEvent11210730());
                    ReplaceOrAppend(archive, BuildEvent11210760());
                    ReplaceOrAppend(archive, BuildEvent11210780());
                    ReplaceOrAppend(archive, BuildEvent11210880());
                    break;
                case "m15_01_00_00.emevd.dcx":
                    ReplaceOrAppend(archive, BuildEvent11510231());
                    break;
                case "m17_00_00_00.emevd.dcx":
                    ReplaceOrAppend(archive, BuildEvent11700005());
                    ReplaceOrAppend(archive, BuildEvent11708500());
                    break;
            }
        }

        private static EMEVD.Event NewEvent(long id)
        {
            return new EMEVD.Event(id, EMEVD.Event.RestBehaviorType.Default);
        }

        private static void ReplaceOrAppend(EMEVD archive, EMEVD.Event replacement)
        {
            int[] matches = Enumerable.Range(0, archive.Events.Count)
                .Where(index => archive.Events[index].ID == replacement.ID)
                .ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException(String.Format(
                    "Expected at most one existing event {0}; found {1}.", replacement.ID, matches.Length));
            if (matches.Length == 0) archive.Events.Add(replacement);
            else archive.Events[matches[0]] = replacement;
        }

        private static EMEVD.Instruction IfEventFlag(sbyte group, byte state, int flag)
        {
            return new EMEVD.Instruction(3, 0, new object[] { group, state, (byte)0, flag });
        }

        private static EMEVD.Instruction IfElapsedSeconds(sbyte group, float seconds)
        {
            return new EMEVD.Instruction(1, 0, new object[] { group, seconds });
        }

        private static EMEVD.Instruction IfAllPlayersInoutsideArea(sbyte group, byte state, int areaEntity)
        {
            return new EMEVD.Instruction(3, 7, new object[] { group, state, areaEntity });
        }

        private static EMEVD.Instruction IfPlayersClass(sbyte group, byte classType)
        {
            return new EMEVD.Instruction(4, 11, new object[] { group, classType });
        }

        private static EMEVD.Instruction IfMultiplayerState(sbyte group, sbyte state)
        {
            return new EMEVD.Instruction(3, 6, new object[] { group, state });
        }

        private static EMEVD.Instruction IfInoutsideArea(sbyte group, byte state, int targetEntity, int areaEntity)
        {
            return new EMEVD.Instruction(3, 2, new object[] { group, state, targetEntity, areaEntity });
        }

        private static EMEVD.Instruction IfConditionGroup(sbyte group, byte state, sbyte target)
        {
            return new EMEVD.Instruction(0, 0, new object[] { group, state, target });
        }

        private static EMEVD.Instruction IfPlayerHasItem(sbyte group, byte itemType, int itemId, byte state)
        {
            return new EMEVD.Instruction(3, 4, new object[] { group, itemType, itemId, state });
        }

        private static EMEVD.Instruction EndIfConditionGroup(byte endType, byte state, sbyte group)
        {
            return new EMEVD.Instruction(1000, 2, new object[] { endType, state, group });
        }

        private static EMEVD.Instruction SkipIfComparison(byte skipLines, sbyte comparison, int left, int right)
        {
            return new EMEVD.Instruction(1000, 5, new object[] { skipLines, comparison, left, right });
        }

        private static EMEVD.Instruction SkipIfEventFlag(byte skipLines, byte state, int flag)
        {
            return new EMEVD.Instruction(1003, 1, new object[] { skipLines, state, (byte)0, flag });
        }

        private static EMEVD.Instruction SkipIfMultiplayerState(byte skipLines, sbyte state)
        {
            return new EMEVD.Instruction(1003, 5, new object[] { skipLines, state });
        }

        private static EMEVD.Instruction SetNetworkSyncState(byte state)
        {
            return new EMEVD.Instruction(2000, 2, new object[] { state });
        }

        private static EMEVD.Instruction AwardItemLot(int itemLot)
        {
            return new EMEVD.Instruction(2003, 4, new object[] { itemLot });
        }

        private static EMEVD.Instruction SetSpEffect(int entityId, int spEffectId)
        {
            return new EMEVD.Instruction(2004, 8, new object[] { entityId, spEffectId });
        }

        private static EMEVD.Instruction RotateCharacter(int entityId, int relativeEntityId)
        {
            return new EMEVD.Instruction(2004, 14, new object[] { entityId, relativeEntityId });
        }

        private static EMEVD.Instruction ReproduceObjectAnimation(int entityId, int animationId)
        {
            return new EMEVD.Instruction(2005, 7, new object[] { entityId, animationId });
        }

        private static EMEVD.Instruction IfHPRatio(sbyte group, int entityId, sbyte comparison, float ratio)
        {
            return new EMEVD.Instruction(4, 2, new object[] { group, entityId, comparison, ratio });
        }

        private static EMEVD.Instruction BatchSetEventFlags(int startFlag, int endFlag, byte state)
        {
            return new EMEVD.Instruction(2003, 22, new object[] { startFlag, endFlag, state });
        }

        private static EMEVD.Instruction BattleOfStoicismExitRequest()
        {
            return new EMEVD.Instruction(2003, 40, new object[0]);
        }

        private static EMEVD.Instruction WarpPlayer(byte area, byte block, int initialArea)
        {
            return new EMEVD.Instruction(2003, 14, new object[] { area, block, initialArea });
        }

        private static EMEVD.Instruction SpawnOneshotSfx(int entityType, int entityId, int dummyPoly, int sfxId)
        {
            return new EMEVD.Instruction(2006, 3, new object[] { entityType, entityId, dummyPoly, sfxId });
        }

        private static EMEVD.Instruction SpawnMapSfx(int entityId)
        {
            return new EMEVD.Instruction(2006, 2, new object[] { entityId });
        }

        private static EMEVD.Instruction DeleteMapSfx(int entityId, byte onlyDeleteRoot)
        {
            return new EMEVD.Instruction(2006, 1, new object[] { entityId, onlyDeleteRoot });
        }

        private static EMEVD.Instruction DisplayMessage(int messageId, byte screenLocation)
        {
            return new EMEVD.Instruction(2007, 4, new object[] { messageId, screenLocation });
        }

        private static EMEVD.Instruction ActivateHit(int hitEntityId, byte state)
        {
            return new EMEVD.Instruction(2011, 1, new object[] { hitEntityId, state });
        }

        private static EMEVD.Instruction ForceAnimationPlayback(int entityId, int animationId,
            byte loop, byte waitForCompletion, byte ignoreTransitionWait)
        {
            return new EMEVD.Instruction(2003, 18,
                new object[] { entityId, animationId, loop, waitForCompletion, ignoreTransitionWait });
        }

        private static EMEVD.Instruction EndIfMultiplayerState(byte endType, sbyte state)
        {
            return new EMEVD.Instruction(1003, 6, new object[] { endType, state });
        }

        private static EMEVD.Instruction PlayCutsceneAndWarpAllPlayers(int cutsceneId, uint playbackMethod,
            int pointEntityId, byte area, byte block)
        {
            return new EMEVD.Instruction(2002, 2,
                new object[] { cutsceneId, playbackMethod, pointEntityId, area, block });
        }

        private static EMEVD.Instruction WaitFrames(int frames)
        {
            return new EMEVD.Instruction(1001, 1, new object[] { frames });
        }

        private static EMEVD.Instruction DisplayGenericDialog(int messageId, short dialogType,
            short numberOfOptions, int entityId, float displayDistance)
        {
            return new EMEVD.Instruction(2007, 1,
                new object[] { messageId, dialogType, numberOfOptions, entityId, displayDistance });
        }

        private static EMEVD.Instruction SetEventFlag(int flag, byte state)
        {
            return new EMEVD.Instruction(2003, 2, new object[] { flag, state });
        }

        private static EMEVD.Instruction EndIfEventFlag(byte endType, byte state, int flag)
        {
            return new EMEVD.Instruction(1003, 2, new object[] { endType, state, (byte)0, flag });
        }

        private static EMEVD.Instruction AwardItemsIncludingClients(int itemLot)
        {
            return new EMEVD.Instruction(2003, 36, new object[] { itemLot });
        }

        private static EMEVD.Instruction DeactivateObject(int entityId, byte state)
        {
            return new EMEVD.Instruction(2005, 3, new object[] { entityId, state });
        }

        private static EMEVD.Instruction SetObjectInvulnerability(int entityId, byte state)
        {
            return new EMEVD.Instruction(2005, 13, new object[] { entityId, state });
        }

        private static EMEVD.Instruction CreateObjectFollowingSfx(int entityId, int dummyPoly, int sfxId)
        {
            return new EMEVD.Instruction(2006, 4, new object[] { entityId, dummyPoly, sfxId });
        }

        private static EMEVD.Instruction IfActionButton(sbyte group, int entityType, int entityId, float angle,
            short dummyPoly, float distance, int message, byte reaction, int pad)
        {
            return new EMEVD.Instruction(3, 5, new object[] { group, entityType, entityId, angle, dummyPoly, distance, message, reaction, pad });
        }

        private static EMEVD.Instruction WaitSeconds(float seconds)
        {
            return new EMEVD.Instruction(1001, 0, new object[] { seconds });
        }

        private static EMEVD.Instruction EndUnconditionally(byte endType)
        {
            return new EMEVD.Instruction(1000, 4, new object[] { endType });
        }

        private static void Bind(EMEVD.Event evt, int instructionIndex, int targetStart, int sourceStart, int byteCount)
        {
            evt.Parameters.Add(new EMEVD.Parameter(instructionIndex, targetStart, sourceStart, byteCount));
        }

        private const byte FlagOff = 0;
        private const byte FlagOn = 1;
        private const byte EndEvent = 0;
        private const byte RestartEvent = 1;
    }
}
