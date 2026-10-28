using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FogHost;
using FogMod;
using SoulsFormats;
using MapSpec = FogMod.AnnotationData.MapSpec;

namespace FogReconstruction.PhysicalWitness
{
    /// <summary>Re-reads the current staging bytes for the bounded 331-world shadow audit.</summary>
    internal sealed class StagedTransitionEvidenceReader
    {
        private sealed record ParsedTransition(string SourceObject, string SourceArea, string DestinationArea, string TargetObject, string DispatchBytes, string ExpectedMap, int ExpectedPlayer);
        private sealed record LandingCheck(bool Verified, string Description, string Failure);
        private sealed record CustomWarpPolicy(string Id, string GateMap, string GateName, string DestinationArea, string DestinationMap, float X, float Y, float Z, float Yaw);
        private sealed record BossPolicyMatch(FogProjectEntrancePolicyV1 Policy, string SideName);

        private static readonly Regex TransitionPattern = new(
            @"^(?<object>\S+)\s+(?<sourceArea>[^>]+)>(?<destinationArea>\S+)\s+target=(?<target>\S+)\s+dispatch=(?<dispatch>[0-9A-Fa-f]+|none)\s+expected=(?<map>[^/]+)/(?<player>\d+)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private const int SeathEventId = 11700005;
        private const int SeathObjectEntityId = 1701801;
        private const int SeathFlag = 5308;
        private const string TestOnlyUnknownHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private readonly string root;
        private readonly Dictionary<string, MSB1> mapsByCode = new(StringComparer.OrdinalIgnoreCase);
        private EMEVD common;
        private IReadOnlyList<WarpEventCallMatrix.WarpEventCall> calls;
        private EMEVD.Event commonEventZero;
        private EMEVD.Event warpDispatcher;
        private bool dispatcherContractChecked;
        private bool dispatcherContractVerified;
        private string dispatcherContractFailure;

        public StagedTransitionEvidenceReader(string stagingGameDirectory)
        {
            root = Path.GetFullPath(stagingGameDirectory ?? throw new ArgumentNullException(nameof(stagingGameDirectory)));
        }

        public static void SelfTest()
        {
            CustomWarpPolicy parishElevator = ResolveCustomWarp("o1410_0000", "firelink_elevator");
            if (parishElevator == null || parishElevator.DestinationMap != "firelink"
                || !VerifyCustomWarpLanding(parishElevator, new MSB1.Part.Player
                {
                    Position = new Vector3(-5.132f, 28.46f, 109.28f),
                    Rotation = new Vector3(0f, 90f, 0f),
                }).Verified
                || VerifyCustomWarpLanding(parishElevator, new MSB1.Part.Player
                {
                    Position = new Vector3(-5.132f, 28.46f, 109.28f),
                    Rotation = new Vector3(0f, 0f, 0f),
                }).Verified)
                throw new InvalidOperationException("The explicit o1410 Firelink elevator landing accepted an incorrect position or yaw.");

            CustomWarpPolicy newLondoElevator = ResolveCustomWarp("o6601_0000", "firelink");
            if (newLondoElevator == null || newLondoElevator.DestinationMap != "firelink"
                || !VerifyCustomWarpLanding(newLondoElevator, new MSB1.Part.Player
                {
                    Position = new Vector3(-52.759f, -74.018f, 50.86f),
                    Rotation = new Vector3(0f, -110f, 0f),
                }).Verified
                || VerifyCustomWarpLanding(newLondoElevator, new MSB1.Part.Player
                {
                    Position = new Vector3(-52.757f, -74.018f, 50.86f),
                    Rotation = new Vector3(0f, -110f, 0f),
                }).Verified)
                throw new InvalidOperationException("The explicit o6601 Firelink elevator landing accepted an incorrect position or yaw.");

            if (ResolveCustomWarp("o1410_0000", "firelink") != null
                || ResolveCustomWarp("o6601_0000", "dukes") != null)
                throw new InvalidOperationException("An unowned Firelink CustomWarp side was accepted.");
            Console.WriteLine("Staged CustomWarp landing self-tests passed (two exact Firelink anchors and four rejection cases).");
        }

        public PhysicalEdge ReadRandomTransition(
            string identity,
            int rowIndex,
            string transitionText,
            string sourceMapClaim,
            string sourceAction,
            string destinationMapClaim,
            string destinationPlayerClaim,
            string pairingCondition,
            string pairingLocator,
            string datasetHash,
            string packageHash)
        {
            string id = StableId(identity + "|" + (transitionText ?? "missing-transition-" + rowIndex.ToString(CultureInfo.InvariantCulture)));
            ParsedTransition parsed = Parse(transitionText);
            string sourceMap = sourceMapClaim ?? "unknown-map";
            string sourceArea = parsed?.SourceArea ?? "unknown-side";
            string sourceAnchor = "fog-action:" + (parsed?.SourceObject ?? "unresolved") + ":side:" + sourceArea + ":action:unresolved";
            string destinationMap = destinationMapClaim ?? "unknown-map";
            string destinationPlayer = destinationPlayerClaim ?? "unresolved";
            var evidence = new List<PhysicalEvidence>
            {
                new("CertifiedDatasetLocator", $"{identity}.randomTransitions[{rowIndex}]", datasetHash),
                new("ObservedStagingTreeUnanchored", identity + " current staging tree", packageHash),
            };

            try
            {
                if (parsed == null)
                    return UnknownTransition(id, sourceMap, sourceAnchor, destinationMap, "player:" + destinationPlayer,
                        "unclassified", "unknown", "transition row is not in the recognized generated-edge format", evidence, packageHash);

                MapSpec sourceSideMap = MapSpecForArea(parsed.SourceArea);
                sourceMap = sourceSideMap.Name;
                bool sourceMapClaimMatches = string.Equals(sourceMap, sourceMapClaim, StringComparison.Ordinal);
                string sourceMapCode = sourceSideMap.Map;
                evidence.Add(new PhysicalEvidence("VerifiedProjectAreaMap", parsed.SourceArea + " -> " + sourceMap, packageHash));
                MSB1 sourceMsb = ReadMap(sourceMapCode);
                MSB1.Part.Object[] sourceObjects = sourceMsb.Parts.Objects.Where(part => part.Name == parsed.SourceObject).ToArray();
                bool sourceObjectVerified = sourceObjects.Length == 1 && sourceObjects[0].EntityID > 0;
                int sourceEntity = sourceObjectVerified ? sourceObjects[0].EntityID : 0;

                string actionName = sourceAction ?? string.Empty;
                int? projectActionId = ResolveProjectActionRegion(parsed.SourceObject, sourceMap, sourceArea);
                MSB1.Region[] actionRegions = projectActionId.HasValue
                    ? sourceMsb.Regions.Regions.Where(region => region.EntityID == projectActionId.Value).ToArray()
                    : sourceMsb.Regions.Regions.Where(region => region.Name == actionName).ToArray();
                bool actionRegionVerified = actionRegions.Length == 1 && actionRegions[0].EntityID > 0;
                int actionRegionId = actionRegionVerified ? actionRegions[0].EntityID : 0;
                sourceAnchor = "fog-action:" + parsed.SourceObject + ":side:" + parsed.SourceArea + ":region:" + (actionRegionVerified ? actionRegionId.ToString(CultureInfo.InvariantCulture) : "unresolved");

                EnsureCommon();
                bool contractVerified = VerifyDispatcherContract();
                WarpEventCallMatrix.WarpEventCall[] matchingCalls = sourceObjectVerified && actionRegionVerified
                    ? calls.Where(call => call.P0 == sourceEntity && call.P3 == actionRegionId).ToArray()
                    : Array.Empty<WarpEventCallMatrix.WarpEventCall>();
                bool dispatcherUnique = matchingCalls.Length == 1;
                WarpEventCallMatrix.WarpEventCall call = dispatcherUnique ? matchingCalls[0] : null;
                string dispatcherLocator = call == null ? "common.emevd.dcx/event0: no unique object/action-region binding"
                    : $"common.emevd.dcx/event0/instruction[{call.InstructionIndex}]/slot[{call.Slot}]";
                bool transitionBytesMatch = false;
                if (call != null && parsed.DispatchBytes != "none")
                {
                    EMEVD.Instruction instruction = commonEventZero.Instructions[call.InstructionIndex];
                    transitionBytesMatch = string.Equals(Convert.ToHexString(instruction.ArgData), parsed.DispatchBytes, StringComparison.OrdinalIgnoreCase);
                }
                bool bindingsMatch = dispatcherUnique && call.Mode == 0 && transitionBytesMatch
                    && call.P0 == sourceEntity && call.P3 == actionRegionId
                    && call.P1 == parsed.ExpectedPlayer
                    && int.TryParse(destinationPlayerClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out int claimedPlayer)
                    && call.P1 == claimedPlayer;

                MapSpec targetSideMap = MapSpecForArea(parsed.DestinationArea);
                evidence.Add(new PhysicalEvidence("VerifiedProjectAreaMap", parsed.DestinationArea + " -> " + targetSideMap.Name, packageHash));
                MSB1 targetSideMsb = ReadMap(targetSideMap.Map);
                MSB1.Part.Object[] targetObjects = targetSideMsb.Parts.Objects.Where(part => part.Name == parsed.TargetObject).ToArray();
                bool targetObjectUnique = targetObjects.Length == 1;

                MapSpec actualDestinationSpec = call == null ? null : MapSpecForPayload(call.DestinationArea, call.DestinationBlock);
                string actualDestinationMap = actualDestinationSpec?.Name ?? "unresolved-map";
                destinationMap = actualDestinationMap;
                destinationPlayer = call?.P1.ToString(CultureInfo.InvariantCulture) ?? "unresolved";
                MSB1.Part.Player[] destinationPlayers = call == null || actualDestinationSpec == null
                    ? Array.Empty<MSB1.Part.Player>()
                    : ReadMap(actualDestinationSpec.Map).Parts.Players.Where(player => player.EntityID == call.P1).ToArray();
                bool destinationPlayerVerified = destinationPlayers.Length == 1;
                MSB1.Part.Player destinationPlayerPart = destinationPlayerVerified ? destinationPlayers[0] : null;

                CustomWarpPolicy customWarp = ResolveCustomWarp(parsed.TargetObject, parsed.DestinationArea);
                bool expectedMapClaimMatches = sourceMapClaimMatches
                    && destinationMapClaim == actualDestinationMap
                    && parsed.ExpectedMap == actualDestinationMap;
                bool destinationMapVerified = call != null && actualDestinationSpec != null
                    && expectedMapClaimMatches
                    && (customWarp == null
                        ? actualDestinationSpec.Map == targetSideMap.Map
                        : actualDestinationSpec.Map == MapSpecForLogical(customWarp.DestinationMap).Map);

                LandingCheck defaultLanding = targetObjectUnique && destinationPlayerPart != null && call != null
                    ? VerifyDefaultLanding(targetObjects[0], destinationPlayerPart, actualDestinationSpec, targetSideMap)
                    : new LandingCheck(false, "not checked", "target object or destination player is missing/ambiguous");
                LandingCheck customLanding = customWarp != null && destinationPlayerPart != null && call != null
                    ? VerifyCustomWarpLanding(customWarp, destinationPlayerPart)
                    : new LandingCheck(false, "not applicable", "no exact project CustomWarp policy match");
                bool customWarpVerified = customWarp != null && destinationMapVerified && customLanding.Verified;
                bool landingVerified = customWarp == null ? defaultLanding.Verified : customLanding.Verified;
                string landingDescription = customWarp == null ? defaultLanding.Description : customLanding.Description;

                bool pvpIdentityVerified = TryResolvePvpSide(parsed.SourceObject, sourceMap, parsed.SourceArea, out FogCompactV2PvpPolicy.GateIdentity pvpGate, out bool pvpASide);
                bool pvpConditionUnconditional = (!pvpIdentityVerified || PvpSideIsUnconditional(pvpGate.Name, pvpASide));
                bool targetPvp = TryResolvePvpSide(parsed.TargetObject, targetSideMap.Name, parsed.DestinationArea,
                    out FogCompactV2PvpPolicy.GateIdentity targetGate, out bool targetASide);
                if (targetPvp && !PvpSideIsUnconditional(targetGate.Name, targetASide)) pvpConditionUnconditional = false;

                BossPolicyMatch sourceBoss = ResolveBossPolicy(parsed.SourceObject, sourceMap, parsed.SourceArea, sourceAction);
                BossPolicyMatch targetBoss = ResolveBossPolicy(parsed.TargetObject, targetSideMap.Name, parsed.DestinationArea, null);
                bool bossBoundary = sourceBoss != null || targetBoss != null;
                bool customClass = customWarp != null;
                bool pvpClass = pvpIdentityVerified || targetPvp;
                string mechanismClass = customClass ? GeneratedTransitionCertifier.ProjectCustomWarpMode0
                    : bossBoundary ? GeneratedTransitionCertifier.BossBoundaryMode0
                    : pvpClass ? GeneratedTransitionCertifier.PvpMode0
                    : GeneratedTransitionCertifier.OrdinaryMode0;
                string ruleId = mechanismClass + "-v1";
                bool allStructural = sourceMapClaimMatches && sourceObjectVerified && actionRegionVerified && dispatcherUnique && contractVerified
                    && bindingsMatch && destinationPlayerVerified && destinationMapVerified && landingVerified;
                string conditionForRule = pairingCondition ?? "unresolved";
                if (ProjectSideHasCondition(parsed.SourceObject, sourceMap, sourceArea)
                    || ProjectSideHasCondition(parsed.TargetObject, targetSideMap.Name, parsed.DestinationArea))
                    conditionForRule = "unsupported project side condition";

                AddVerifiedEvidence(evidence, packageHash, identity, sourceMapCode, parsed, sourceEntity,
                    sourceMap, actionRegions, actionRegionVerified, dispatcherLocator, actualDestinationSpec,
                    call, destinationPlayerVerified, landingDescription, pairingLocator, customWarp, sourceBoss, targetBoss);
                var facts = new GeneratedTransitionFacts(
                    ruleId, mechanismClass, call?.Mode ?? 0,
                    sourceMap + "/" + sourceAnchor,
                    destinationMap + "/player:" + destinationPlayer,
                    sourceMap,
                    sourceAnchor,
                    destinationMap,
                    "player:" + destinationPlayer,
                    actionRegionVerified ? actionRegionId : null,
                    dispatcherLocator,
                    destinationPlayerVerified ? call.P1 : null,
                    landingDescription,
                    "current staging bytes; package tree SHA-256 is an unanchored observation; dataset used only as row locator",
                    conditionForRule,
                    sourceObjectVerified,
                    actionRegionVerified,
                    dispatcherUnique,
                    contractVerified,
                    bindingsMatch,
                    destinationPlayerVerified,
                    destinationMapVerified,
                    landingVerified,
                    pvpClass,
                    pvpClass,
                    pvpConditionUnconditional,
                    customWarpVerified,
                    false,
                    false,
                    call?.P0 ?? 0,
                    call?.P1 ?? 0,
                    call?.P2 ?? 0,
                    call?.P3 ?? 0,
                    call?.P4 ?? 0,
                    call?.P5 ?? 0,
                    call?.P6 ?? 0,
                    call?.BeforeWarpPositive ?? false,
                    evidence);
                GeneratedTransitionDecision decision = GeneratedTransitionCertifier.Certify(facts);
                string reason = decision.Certification.Reason;
                if (!allStructural && decision.Certainty == PhysicalEdgeCertainty.UNKNOWN)
                {
                    string[] missing = new[]
                    {
                        sourceMapClaimMatches ? null : "source map disagrees with project-owned Area-to-Map policy",
                        sourceObjectVerified ? null : "source object",
                        actionRegionVerified ? null : "ActionRegion",
                        dispatcherUnique ? null : "dispatcher binding",
                        contractVerified ? null : "dispatcher contract",
                        bindingsMatch ? null : "initializer bytes/parameters",
                        destinationPlayerVerified ? null : "destination player",
                        destinationMapVerified ? null : "destination map",
                        landingVerified ? null : "landing",
                    }.Where(value => value != null).ToArray();
                    reason = "staged-byte checks missing/failed: " + string.Join(", ", missing) + (string.IsNullOrEmpty(reason) ? "" : "; " + reason);
                    if (!contractVerified && !string.IsNullOrWhiteSpace(dispatcherContractFailure))
                        reason += "; Event5700 contract detail: " + dispatcherContractFailure;
                    decision = decision with { Certification = decision.Certification with { Reason = reason } };
                }
                PhysicalNode from = new(sourceMap, sourceAnchor);
                PhysicalNode to = new(destinationMap, "player:" + destinationPlayer);
                return new PhysicalEdge(id, from, to, decision.Certainty, mechanismClass,
                    decision.Certification.RequiredState,
                    decision.RequiredEventFlags,
                    decision.ForbiddenEventFlags,
                    Array.Empty<int>(),
                    decision.SetEventFlags,
                    decision.ClearEventFlags,
                    decision.Evidence)
                {
                    Certification = decision.Certification,
                };
            }
            catch (Exception exception) when (IsExpectedReadFailure(exception))
            {
                return UnknownTransition(id, sourceMap, sourceAnchor, destinationMap, "player:" + destinationPlayer,
                    "unclassified", "unknown", "staged byte verification failed closed: " + exception.Message, evidence, packageHash);
            }
        }

        public IReadOnlyList<PhysicalEdge> ReadMode2Edges(string identity, string datasetHash, string packageHash)
        {
            EnsureCommon();
            bool contract = VerifyDispatcherContract();
            WarpEventCallMatrix.WarpEventCall[] mode2 = calls.Where(call => call.Mode == 2).ToArray();
            var result = new List<PhysicalEdge>(mode2.Length);
            foreach (WarpEventCallMatrix.WarpEventCall call in mode2)
            {
                string id = StableId(identity + "|mode2|" + call.InstructionIndex.ToString(CultureInfo.InvariantCulture));
                MapSpec destinationSpec = MapSpecForPayload(call.DestinationArea, call.DestinationBlock);
                MSB1 destinationMsb = ReadMap(destinationSpec.Map);
                MSB1.Part.Player[] players = destinationMsb.Parts.Players.Where(player => player.EntityID == call.P1).ToArray();
                bool playerVerified = players.Length == 1;
                string ruleClass = call.P0 == SeathFlag ? GeneratedTransitionCertifier.SeathMode2P05308 : "mode2-unsupported";
                string source = call.P0 == SeathFlag ? "dukes/seath-crystal:1701801/event:11700005" : "unresolved-mode2-trigger-flag:" + call.P0.ToString(CultureInfo.InvariantCulture);
                bool sourceVerified = call.P0 == SeathFlag && VerifySeathInteractionChain();
                FogProjectFlagWarpPolicyV1 seathPolicy = FogProjectAnnotationPolicy.FlagWarpPoliciesV1.Single(policy => policy.Id == "dukes-seath-prison");
                bool mapVerified = call.P0 == SeathFlag && destinationSpec.Name == seathPolicy.Map.Value;
                bool landingVerified = call.P0 == SeathFlag && playerVerified && VerifySeathLanding(players[0], destinationMsb, seathPolicy);
                int sameTriggerCalls = mode2.Count(candidate => candidate.P0 == call.P0);
                bool unique = sameTriggerCalls == 1;
                string dispatcher = $"common.emevd.dcx/event0/instruction[{call.InstructionIndex}]/slot[{call.Slot}]";
                bool binding = playerVerified && call.P1 == players[0].EntityID
                    && (call.P0 != SeathFlag || (mapVerified && call.P1 > 0));
                var evidence = new List<PhysicalEvidence>
                {
                    new("ObservedStagingTreeUnanchored", identity + " current staging tree", packageHash),
                    new("GeneratedDispatcher", dispatcher + "; mode=2; p0=" + call.P0.ToString(CultureInfo.InvariantCulture), packageHash),
                    new("VerifiedDestinationPlayer", destinationSpec.Map + "/player:" + call.P1.ToString(CultureInfo.InvariantCulture), packageHash),
                    new("VerifiedDestinationMap", "common.emevd.dcx destination map=" + destinationSpec.Map, packageHash),
                };
                if (sourceVerified)
                {
                    evidence.Add(new PhysicalEvidence("VerifiedSourceObject", "map/MapStudio/" + MapSpecForLogical("dukes").Map + ".msb/object entity 1701801", packageHash));
                    evidence.Add(new PhysicalEvidence("SeathInteractionChain", "m17 event0 initializes event 11700005; event targets crystal 1701801 and sets flag 5308", packageHash));
                }
                if (landingVerified)
                    evidence.Add(new PhysicalEvidence("VerifiedLanding", "m17 MSB region 1702901 and generated player transform match", packageHash));
                var facts = new GeneratedTransitionFacts(
                    "seath-mode2-p0-5308-v1", ruleClass, 2, source,
                    destinationSpec.Name + "/player:" + call.P1.ToString(CultureInfo.InvariantCulture),
                    "dukes", source, destinationSpec.Name, "player:" + call.P1.ToString(CultureInfo.InvariantCulture),
                    null, dispatcher, playerVerified ? call.P1 : null,
                    landingVerified ? "generated player exactly matches runtime prison region 1702901" : "Seath prison landing unresolved",
                    "current staging bytes; package tree SHA-256 is an unanchored observation",
                    "true", sourceVerified, false, unique, contract, binding, playerVerified,
                    mapVerified, landingVerified, false, false, false, false, sourceVerified, false,
                    call.P0, call.P1, call.P2, call.P3, call.P4, call.P5, call.P6, call.BeforeWarpPositive, evidence);
                GeneratedTransitionDecision decision = GeneratedTransitionCertifier.Certify(facts);
                if (call.P0 == SeathFlag && !sourceVerified)
                {
                    decision = decision with
                    {
                        Certification = decision.Certification with
                        {
                            Reason = decision.Certification.Reason + "; Seath producer detail: " + DescribeSeathInteractionChain(),
                        },
                    };
                }
                if (!contract && !string.IsNullOrWhiteSpace(dispatcherContractFailure))
                {
                    decision = decision with
                    {
                        Certification = decision.Certification with
                        {
                            Reason = decision.Certification.Reason + "; Event5700 contract detail: " + dispatcherContractFailure,
                        },
                    };
                }
                if (call.P0 != SeathFlag)
                {
                    string reason = call.P0 == 0
                        ? "Mode-2 p0=0 is explicitly out of scope and remains UNKNOWN."
                        : "Mode-2 trigger flag " + call.P0.ToString(CultureInfo.InvariantCulture) + " is outside the sole certified Seath p0=5308 class.";
                    decision = decision with
                    {
                        Certainty = PhysicalEdgeCertainty.UNKNOWN,
                        Certification = decision.Certification with { Status = "UNKNOWN", Reason = reason },
                    };
                }
                result.Add(new PhysicalEdge(id,
                    new PhysicalNode("dukes", "mode2-trigger:" + call.P0.ToString(CultureInfo.InvariantCulture)),
                    new PhysicalNode(destinationSpec.Name, "player:" + call.P1.ToString(CultureInfo.InvariantCulture)),
                    decision.Certainty, ruleClass, decision.Certification.RequiredState,
                    decision.RequiredEventFlags, decision.ForbiddenEventFlags, Array.Empty<int>(),
                    decision.SetEventFlags, decision.ClearEventFlags, decision.Evidence)
                {
                    Certification = decision.Certification,
                });
            }
            return result;
        }

        private static void AddVerifiedEvidence(
            List<PhysicalEvidence> evidence,
            string packageHash,
            string identity,
            string sourceMapCode,
            ParsedTransition parsed,
            int sourceEntity,
            string sourceMap,
            MSB1.Region[] actionRegions,
            bool actionRegionVerified,
            string dispatcherLocator,
            MapSpec destinationSpec,
            WarpEventCallMatrix.WarpEventCall call,
            bool destinationPlayerVerified,
            string landing,
            string pairingLocator,
            CustomWarpPolicy customWarp,
            BossPolicyMatch sourceBoss,
            BossPolicyMatch targetBoss)
        {
            if (sourceEntity > 0)
                evidence.Add(new PhysicalEvidence("VerifiedSourceObject", $"map/MapStudio/{sourceMapCode}.msb/{parsed.SourceObject}/entity:{sourceEntity}", packageHash));
            if (actionRegionVerified)
                evidence.Add(new PhysicalEvidence("SourceActionRegion", $"map/MapStudio/{sourceMapCode}.msb/region:{actionRegions[0].EntityID}; name={actionRegions[0].Name}", packageHash));
            evidence.Add(new PhysicalEvidence("GeneratedDispatcher", dispatcherLocator + (call == null ? "" : "; mode=" + call.Mode), packageHash));
            if (destinationPlayerVerified && call != null)
                evidence.Add(new PhysicalEvidence("VerifiedDestinationPlayer", destinationSpec.Map + "/player:" + call.P1.ToString(CultureInfo.InvariantCulture), packageHash));
            if (destinationSpec != null)
                evidence.Add(new PhysicalEvidence("VerifiedDestinationMap", "Event5700 destination map " + destinationSpec.Map, packageHash));
            evidence.Add(new PhysicalEvidence("VerifiedLanding", landing, packageHash));
            evidence.Add(new PhysicalEvidence("GeneratedPairingLocator", pairingLocator ?? "no unique pairing", packageHash));
            if (customWarp != null)
                evidence.Add(new PhysicalEvidence("ProjectCustomWarpPolicy", customWarp.Id + "; exact configured position/yaw checked", packageHash));
            if (sourceBoss != null)
                evidence.Add(new PhysicalEvidence("BossLifecyclePolicy",
                    "source boss-policy " + sourceBoss.Policy.Id + " side " + sourceBoss.SideName + "; lifecycle evidence incomplete", packageHash));
            if (targetBoss != null)
                evidence.Add(new PhysicalEvidence("BossLifecyclePolicy",
                    "destination boss-policy " + targetBoss.Policy.Id + " side " + targetBoss.SideName + "; lifecycle evidence incomplete", packageHash));
            if (TryResolvePvpSide(parsed.SourceObject, sourceMap, parsed.SourceArea,
                out FogCompactV2PvpPolicy.GateIdentity sourceGate, out bool sourceASide))
                evidence.Add(new PhysicalEvidence("ProjectPolicyIdentity",
                    $"source side {sourceGate.Name}/{sourceGate.EntityId} {sourceGate.Map.Value}/{(sourceASide ? "A" : "B")}/{parsed.SourceArea}", packageHash));
            MapSpec destinationSide = MapSpecForArea(parsed.DestinationArea);
            if (TryResolvePvpSide(parsed.TargetObject, destinationSide.Name, parsed.DestinationArea,
                out FogCompactV2PvpPolicy.GateIdentity targetGate, out bool targetASide))
                evidence.Add(new PhysicalEvidence("ProjectPolicyIdentity",
                    $"destination side {targetGate.Name}/{targetGate.EntityId} {targetGate.Map.Value}/{(targetASide ? "A" : "B")}/{parsed.DestinationArea}", packageHash));
        }

        private bool VerifyDispatcherContract()
        {
            if (dispatcherContractChecked) return dispatcherContractVerified;
            dispatcherContractChecked = true;
            try
            {
                EMEVD.Event expected = Event5700Builder.Build();
                Event5700BehaviorHarness.VerifyBodyContract(expected, warpDispatcher);
                dispatcherContractVerified = true;
            }
            catch (Exception exception) when (IsExpectedReadFailure(exception))
            {
                dispatcherContractFailure = exception.Message;
            }
            return dispatcherContractVerified;
        }

        private void EnsureCommon()
        {
            if (common != null) return;
            string path = Path.Combine(root, "event", "common.emevd.dcx");
            common = ReadEmevd(path);
            commonEventZero = common.Events.SingleOrDefault(evt => evt.ID == 0)
                ?? throw new InvalidDataException("Staged common EMEVD has no event 0.");
            warpDispatcher = common.Events.SingleOrDefault(evt => evt.ID == 5700)
                ?? throw new InvalidDataException("Staged common EMEVD has no Event 5700 dispatcher.");
            calls = WarpEventCallMatrix.Read(path);
        }

        private bool VerifySeathInteractionChain()
        {
            string mapCode = MapSpecForLogical("dukes").Map;
            MSB1 msb = ReadMap(mapCode);
            MSB1.Part.Object[] objects = msb.Parts.Objects.Where(part => part.EntityID == SeathObjectEntityId).ToArray();
            EMEVD archive = ReadEmevd(Path.Combine(root, "event", mapCode + ".emevd.dcx"));
            EMEVD.Event[] events = archive.Events.Where(evt => evt.ID == SeathEventId).ToArray();
            EMEVD.Event[] eventZeroRows = archive.Events.Where(evt => evt.ID == 0).ToArray();
            int initializerMatches = eventZeroRows.Length == 1
                ? eventZeroRows[0].Instructions.Count(instruction => instruction.Bank == 2000 && instruction.ID == 0
                    && instruction.ArgData.Length >= 12 && BitConverter.ToInt64(instruction.ArgData, 4) == SeathEventId)
                : 0;
            EMEVD.Event expected = FogAddedEventBuilder.BuildEvent11700005();
            return objects.Length == 1 && initializerMatches == 1 && events.Length == 1
                && SameInstructions(events[0], expected)
                && events[0].Instructions.Any(instruction => instruction.Bank == 3 && instruction.ID == 5
                    && instruction.ArgData.Length >= 8 && ContainsInt32(instruction.ArgData, SeathObjectEntityId))
                && events[0].Instructions.Any(instruction => instruction.Bank == 2003 && instruction.ID == 2
                    && instruction.ArgData.Length >= 5 && BitConverter.ToInt32(instruction.ArgData, 0) == SeathFlag
                    && instruction.ArgData[4] == 1);
        }

        private string DescribeSeathInteractionChain()
        {
            string mapCode = MapSpecForLogical("dukes").Map;
            MSB1 msb = ReadMap(mapCode);
            MSB1.Part.Object[] objects = msb.Parts.Objects.Where(part => part.EntityID == SeathObjectEntityId).ToArray();
            EMEVD archive = ReadEmevd(Path.Combine(root, "event", mapCode + ".emevd.dcx"));
            EMEVD.Event[] events = archive.Events.Where(evt => evt.ID == SeathEventId).ToArray();
            EMEVD.Event[] eventZeroRows = archive.Events.Where(evt => evt.ID == 0).ToArray();
            int initializerMatches = eventZeroRows.Length == 1
                ? eventZeroRows[0].Instructions.Count(instruction => instruction.Bank == 2000 && instruction.ID == 0
                    && instruction.ArgData.Length >= 12 && BitConverter.ToInt64(instruction.ArgData, 4) == SeathEventId)
                : 0;
            EMEVD.Event expected = FogAddedEventBuilder.BuildEvent11700005();
            bool instructionsMatch = events.Length == 1 && SameInstructions(events[0], expected);
            return "objectEntityMatches=" + objects.Length.ToString(CultureInfo.InvariantCulture)
                + "; event0InitializerMatches=" + initializerMatches.ToString(CultureInfo.InvariantCulture)
                + "; event11700005Matches=" + events.Length.ToString(CultureInfo.InvariantCulture)
                + "; instructionBodyMatches=" + instructionsMatch.ToString(CultureInfo.InvariantCulture);
        }

        private static bool SameInstructions(EMEVD.Event actual, EMEVD.Event expected) =>
            actual.RestBehavior == expected.RestBehavior
            && actual.Instructions.Count == expected.Instructions.Count
            && actual.Instructions.Zip(expected.Instructions).All(pair => pair.First.Bank == pair.Second.Bank
                && pair.First.ID == pair.Second.ID
                && pair.First.ArgData.SequenceEqual(pair.Second.ArgData))
            && actual.Parameters.Count == expected.Parameters.Count;

        private static bool ContainsInt32(byte[] data, int expected)
        {
            if (data == null || data.Length < sizeof(int)) return false;
            byte[] value = BitConverter.GetBytes(expected);
            for (int offset = 0; offset <= data.Length - value.Length; offset++)
                if (data[offset] == value[0] && data[offset + 1] == value[1]
                    && data[offset + 2] == value[2] && data[offset + 3] == value[3]) return true;
            return false;
        }

        private static bool VerifySeathLanding(MSB1.Part.Player player, MSB1 msb, FogProjectFlagWarpPolicyV1 policy)
        {
            MSB1.Region[] regions = msb.Regions.Regions.Where(region => region.EntityID == policy.RuntimeDestinationRegionEntityId).ToArray();
            if (regions.Length != 1) return false;
            MSB1.Region region = regions[0];
            return Near(region.Position.X, policy.DestinationPosition.X)
                && Near(region.Position.Y, policy.DestinationPosition.Y)
                && Near(region.Position.Z, policy.DestinationPosition.Z)
                && Near(player.Position.X, region.Position.X)
                && Near(player.Position.Y, region.Position.Y)
                && Near(player.Position.Z, region.Position.Z)
                && Near(player.Rotation.X, region.Rotation.X)
                && Near(player.Rotation.Y, region.Rotation.Y)
                && Near(player.Rotation.Z, region.Rotation.Z);
        }

        private static LandingCheck VerifyDefaultLanding(MSB1.Part.Object target, MSB1.Part.Player player, MapSpec actualDestination, MapSpec targetSideMap)
        {
            if (!string.Equals(actualDestination.Map, targetSideMap.Map, StringComparison.Ordinal))
                return new LandingCheck(false, "default landing target map differs from destination map", "the target object and player are on different map files");

            float yaw = target.Rotation.Y;
            float oppositeYaw = yaw + 180f;
            if (oppositeYaw >= 180f) oppositeYaw -= 360f;
            var matches = new List<string>();
            foreach (bool sideA in new[] { true, false })
            foreach (float height in new[] { 0f, 1f })
            {
                float moveYaw = sideA ? yaw : oppositeYaw;
                double radians = moveYaw * Math.PI / 180d;
                float x = target.Position.X + (float)Math.Sin(radians);
                float y = target.Position.Y + height;
                float z = target.Position.Z + (float)Math.Cos(radians);
                float faceYaw = sideA ? oppositeYaw : yaw;
                if (Near(player.Position.X, x) && Near(player.Position.Y, y)
                    && Near(player.Position.Z, z) && Near(player.Rotation.Y, faceYaw))
                    matches.Add("side=" + (sideA ? "A" : "B") + "; higherOffset=" + height.ToString("R", CultureInfo.InvariantCulture));
            }
            return matches.Count == 1
                ? new LandingCheck(true, "pinned FogMod object-relative landing verified at " + matches[0], null)
                : new LandingCheck(false, "object-relative landing match count=" + matches.Count.ToString(CultureInfo.InvariantCulture), "landing did not match exactly one upstream transform");
        }

        private static LandingCheck VerifyCustomWarpLanding(CustomWarpPolicy policy, MSB1.Part.Player player)
        {
            bool verified = Near(player.Position.X, policy.X) && Near(player.Position.Y, policy.Y)
                && Near(player.Position.Z, policy.Z) && Near(player.Rotation.Y, policy.Yaw);
            string expected = string.Format(CultureInfo.InvariantCulture, "{0} {1:R},{2:R},{3:R},yaw={4:R}",
                policy.DestinationMap, policy.X, policy.Y, policy.Z, policy.Yaw);
            return new LandingCheck(verified, verified ? policy.Id + " exact project CustomWarp " + expected : "CustomWarp expected " + expected,
                verified ? null : "generated player differs from exact project-owned CustomWarp coordinates");
        }

        private static bool Near(float left, float right) => Math.Abs(left - right) <= 0.001f;

        private static CustomWarpPolicy ResolveCustomWarp(string targetName, string destinationArea)
        {
            FogCompactV2PvpPolicy.GateIdentity gate = FogCompactV2PvpPolicy.GraphEntrances.SingleOrDefault(item => item.Name == targetName);
            if (gate == null) return null;
            if (targetName == "o1410_0000" && destinationArea == "firelink_elevator"
                && gate.Map.Value == "parish" && gate.ASideArea == destinationArea)
                return new CustomWarpPolicy("compact-pvp-o1410-a-firelink-v1", gate.Map.Value, gate.Name, destinationArea, "firelink",
                    -5.132f, 28.46f, 109.28f, 90f);
            if (targetName == "o6601_0000" && destinationArea == "firelink"
                && gate.Map.Value == "newlondo" && gate.BSideArea == destinationArea)
                return new CustomWarpPolicy("compact-pvp-o6601-b-firelink-v1", gate.Map.Value, gate.Name, destinationArea, "firelink",
                    -52.759f, -74.018f, 50.86f, -110f);
            return null;
        }

        private static bool TryResolvePvpSide(string name, string map, string area,
            out FogCompactV2PvpPolicy.GateIdentity gate, out bool aSide)
        {
            gate = FogCompactV2PvpPolicy.GraphEntrances.SingleOrDefault(item => item.Name == name);
            aSide = false;
            if (gate == null || gate.Map.Value != map) return false;
            if (gate.ASideArea == area) { aSide = true; return true; }
            if (gate.BSideArea == area) return true;
            return false;
        }

        private static bool PvpSideIsUnconditional(string gateName, bool aSide) =>
            !((gateName == "o4968_0000" && !aSide)
                || (gateName == "o5151_0000" && !aSide)
                || (gateName == "o5863_0000" && !aSide));

        private static BossPolicyMatch ResolveBossPolicy(string objectName, string map, string area, string sourceAction)
        {
            FogProjectEntrancePolicyV1[] candidates = FogProjectAnnotationPolicy.BossEntrancePoliciesV1
                .Where(policy => policy.ObjectPartName == objectName && policy.Map.Value == map).ToArray();
            if (sourceAction != null)
            {
                string id = sourceAction.StartsWith("FR: ", StringComparison.Ordinal) || sourceAction.StartsWith("BR: ", StringComparison.Ordinal)
                    ? sourceAction.Substring(4) : sourceAction;
                FogProjectEntrancePolicyV1[] named = candidates.Where(policy => policy.Id == id).ToArray();
                if (named.Length == 1) candidates = named;
            }
            if (candidates.Length != 1) return null;
            FogProjectEntrancePolicyV1 candidate = candidates[0];
            bool aSide = candidate.BossAreaOnASide ? area == candidate.DestinationArea.Value : area == candidate.SourceArea.Value;
            bool bSide = candidate.BossAreaOnASide ? area == candidate.SourceArea.Value : area == candidate.DestinationArea.Value;
            return aSide ? new BossPolicyMatch(candidate, "A") : bSide ? new BossPolicyMatch(candidate, "B") : null;
        }

        private static bool ProjectSideHasCondition(string objectName, string map, string area)
        {
            FogProjectEntrancePolicyV1[] policies = FogProjectAnnotationPolicy.EntrancePoliciesV1
                .Where(policy => policy.ObjectPartName == objectName && policy.Map.Value == map).ToArray();
            if (policies.Length != 1) return false;
            FogProjectEntrancePolicyV1 policy = policies[0];
            bool aSide = policy.BossAreaOnASide ? area == policy.DestinationArea.Value : area == policy.SourceArea.Value;
            bool bSide = policy.BossAreaOnASide ? area == policy.SourceArea.Value : area == policy.DestinationArea.Value;
            return aSide ? policy.ASideCondition != null : bSide && policy.BSideCondition != null;
        }

        private static int? ResolveProjectActionRegion(string objectName, string map, string area)
        {
            FogProjectEntrancePolicyV1[] policies = FogProjectAnnotationPolicy.EntrancePoliciesV1
                .Where(policy => policy.ObjectPartName == objectName && policy.Map.Value == map).ToArray();
            if (policies.Length != 1) return null;
            FogProjectEntrancePolicyV1 policy = policies[0];
            bool aSide = policy.BossAreaOnASide ? area == policy.DestinationArea.Value : area == policy.SourceArea.Value;
            bool bSide = policy.BossAreaOnASide ? area == policy.SourceArea.Value : area == policy.DestinationArea.Value;
            if (!aSide && !bSide) return null;
            int? explicitRegion = aSide ? policy.ASideActionRegionEntityId : policy.BSideActionRegionEntityId;
            if (explicitRegion.HasValue) return explicitRegion.Value;
            FogAreaId sideArea = new FogAreaId(area);
            return FogProjectAnnotationPolicy.ResolveAreaMap(sideArea) == policy.Map ? policy.ActionRegionEntityId : null;
        }

        private static bool IsExpectedReadFailure(Exception exception) =>
            exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException
            or KeyNotFoundException or FormatException or OverflowException;

        private ParsedTransition Parse(string text)
        {
            Match match = TransitionPattern.Match(text ?? string.Empty);
            if (!match.Success || !int.TryParse(match.Groups["player"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int player))
                return null;
            return new ParsedTransition(match.Groups["object"].Value, match.Groups["sourceArea"].Value,
                match.Groups["destinationArea"].Value, match.Groups["target"].Value, match.Groups["dispatch"].Value,
                match.Groups["map"].Value, player);
        }

        private MSB1 ReadMap(string mapCode)
        {
            if (!mapsByCode.TryGetValue(mapCode, out MSB1 map))
            {
                string path = Path.Combine(root, "map", "MapStudio", mapCode + ".msb");
                map = MSB1.Read(path);
                mapsByCode.Add(mapCode, map);
            }
            return map;
        }

        private static EMEVD ReadEmevd(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            return EMEVD.Read(bytes);
        }

        private static MapSpec MapSpecForLogical(string name) =>
            AnnotationData.nameSpecs.TryGetValue(name ?? string.Empty, out MapSpec spec)
                ? spec
                : throw new InvalidDataException("Fog map identity does not resolve: " + (name ?? "<null>"));

        private static MapSpec MapSpecForArea(string area)
        {
            FogMapId map = FogProjectAnnotationPolicy.ResolveAreaMap(new FogAreaId(area));
            return MapSpecForLogical(map.Value);
        }

        private static MapSpec MapSpecForPayload(byte area, byte block)
        {
            MapSpec[] specs = AnnotationData.specs.Values.Where(spec =>
            {
                Match map = Regex.Match(spec.Map, @"^m(?<area>\d{2})_(?<block>\d{2})_", RegexOptions.CultureInvariant);
                return map.Success
                    && byte.Parse(map.Groups["area"].Value, CultureInfo.InvariantCulture) == area
                    && byte.Parse(map.Groups["block"].Value, CultureInfo.InvariantCulture) == block;
            }).ToArray();
            if (specs.Length != 1)
                throw new InvalidDataException($"Event5700 map payload {area:D2}_{block:D2} resolves to {specs.Length} Fog map specs.");
            return specs[0];
        }

        private static PhysicalEdge UnknownTransition(string id, string sourceMap, string sourceAnchor,
            string destinationMap, string destinationAnchor, string mechanism, string ruleId, string reason,
            IReadOnlyList<PhysicalEvidence> evidence, string packageHash)
        {
            var allEvidence = evidence.ToList();
            allEvidence.Add(new PhysicalEvidence("CertificationFailure", reason, packageHash));
            var certification = new PhysicalEdgeCertification(ruleId + "-v1", mechanism, 0,
                sourceMap + "/" + sourceAnchor, destinationMap + "/" + destinationAnchor, "unresolved",
                "unresolved", "unresolved", "unresolved", destinationMap, "unresolved",
                "current staging bytes; package hash is unanchored", "UNKNOWN", reason);
            return new PhysicalEdge(id, new PhysicalNode(sourceMap, sourceAnchor),
                new PhysicalNode(destinationMap, destinationAnchor), PhysicalEdgeCertainty.UNKNOWN,
                mechanism, reason, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
                Array.Empty<int>(), Array.Empty<int>(), allEvidence)
            {
                Certification = certification,
            };
        }

        private static string StableId(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
