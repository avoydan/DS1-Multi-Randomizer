using System;
using System.Collections.Generic;
using System.Linq;

namespace FogReconstruction.PhysicalWitness
{
    public sealed record PhysicalEdgeCertification(
        string RuleId,
        string MechanismClass,
        int Mode,
        string Source,
        string Destination,
        string RequiredState,
        string ActionRegion,
        string Dispatcher,
        string DestinationPlayer,
        string DestinationMap,
        string Landing,
        string Provenance,
        string Status,
        string Reason);

    public sealed record GeneratedTransitionFacts(
        string RuleId,
        string MechanismClass,
        int Mode,
        string Source,
        string Destination,
        string SourceMap,
        string SourceAnchor,
        string DestinationMap,
        string DestinationAnchor,
        int? ActionRegionEntityId,
        string Dispatcher,
        int? DestinationPlayerEntityId,
        string Landing,
        string Provenance,
        string PairingCondition,
        bool SourceObjectVerified,
        bool ActionRegionVerified,
        bool DispatcherUnique,
        bool DispatcherContractVerified,
        bool BindingsMatch,
        bool DestinationPlayerVerified,
        bool DestinationMapVerified,
        bool LandingVerified,
        bool PvpPolicyVerified,
        bool PvpSideVerified,
        bool PvpConditionUnconditional,
        bool CustomWarpPolicyVerified,
        bool SeathInteractionVerified,
        bool BossLifecycleVerified,
        int P0,
        int P1,
        int P2,
        int P3,
        int P4,
        int P5,
        int P6,
        bool BeforeWarpPositive,
        IReadOnlyList<PhysicalEvidence> Evidence);

    public sealed record GeneratedTransitionDecision(
        PhysicalEdgeCertainty Certainty,
        PhysicalEdgeCertification Certification,
        int[] RequiredEventFlags,
        int[] ForbiddenEventFlags,
        int[] SetEventFlags,
        int[] ClearEventFlags,
        PhysicalEvidence[] Evidence);

    /// <summary>Fail-closed, development-only certification rules for the bounded Fog edge classes.</summary>
    public static class GeneratedTransitionCertifier
    {
        public const string OrdinaryMode0 = "ordinary-mode0";
        public const string PvpMode0 = "pvp-mode0";
        public const string ProjectCustomWarpMode0 = "project-customwarp-mode0";
        public const string SeathMode2P05308 = "seath-mode2-p0-5308";
        public const string BossBoundaryMode0 = "boss-boundary-mode0";
        private const int WarpInProgressFlag = 15;
        private const int SeathActivationFlag = 5308;
        private const string TestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        public static GeneratedTransitionDecision Certify(GeneratedTransitionFacts facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (facts.Evidence == null || facts.Evidence.Count == 0)
                throw new ArgumentException("Certification requires auditable evidence, including for UNKNOWN results.", nameof(facts));

            string failure = CommonFailure(facts);
            if (failure == null)
            {
                failure = facts.MechanismClass switch
                {
                    OrdinaryMode0 => facts.Mode == 0 ? null : "ordinary randomized fog requires Event5700 Mode 0; other modes are not crossing proof",
                    PvpMode0 => facts.Mode != 0
                        ? "PvP Mode 1 is cleanup/solo-opening behavior, not a randomized map crossing"
                        : !facts.PvpPolicyVerified || !facts.PvpSideVerified
                            ? "PvP source identity or side does not match current project policy"
                            : !facts.PvpConditionUnconditional
                                ? "the PvP side has a policy condition the shadow state cannot evaluate completely"
                                : null,
                    ProjectCustomWarpMode0 => facts.Mode != 0
                        ? "project CustomWarp transition requires an observed Mode 0 dispatcher"
                        : !facts.CustomWarpPolicyVerified
                            ? "destination is not an exact project-owned CustomWarp policy side"
                            : null,
                    SeathMode2P05308 => facts.Mode != 2
                        ? "Seath crystal route requires Event5700 Mode 2"
                        : facts.P0 != SeathActivationFlag
                            ? "only Seath trigger flag 5308 is in scope; other Mode 2 flags remain UNKNOWN"
                            : !facts.SeathInteractionVerified
                                ? "the crystal interaction event/object/flag producer chain is incomplete"
                                : null,
                    BossBoundaryMode0 => facts.Mode != 0
                        ? "boss-boundary crossing requires an observed Mode 0 dispatcher"
                        : !facts.BossLifecycleVerified
                            ? "boss entry/completion/exit semantics are not fully verified for this staged edge"
                            : null,
                    _ => "mechanism class has no reviewed certification rule",
                };
            }

            int[] required = Array.Empty<int>();
            int[] forbidden = Array.Empty<int>();
            int[] set = Array.Empty<int>();
            int[] clear = Array.Empty<int>();
            string requiredState = "unresolved";
            if (failure == null)
            {
                var requiredFlags = new HashSet<int>();
                var forbiddenFlags = new HashSet<int>();
                if (facts.P2 > 0) forbiddenFlags.Add(facts.P2);
                if (facts.P4 > 0) requiredFlags.Add(facts.P4);
                if (facts.P5 > 0) requiredFlags.Add(facts.P5);
                if (facts.Mode == 2)
                {
                    requiredFlags.Add(SeathActivationFlag);
                    forbiddenFlags.Add(WarpInProgressFlag);
                    clear = new[] { SeathActivationFlag };
                }
                if (facts.P6 > 0)
                {
                    if (facts.BeforeWarpPositive) set = new[] { facts.P6 };
                    else clear = clear.Append(facts.P6).Distinct().OrderBy(value => value).ToArray();
                }
                required = requiredFlags.OrderBy(value => value).ToArray();
                forbidden = forbiddenFlags.OrderBy(value => value).ToArray();
                requiredState = string.Join("; ", new[]
                {
                    facts.P2 > 0 ? $"flag {facts.P2}=OFF (trap guard)" : null,
                    facts.P4 > 0 ? $"flag {facts.P4}=ON (source-side gate)" : null,
                    facts.P5 > 0 ? $"flag {facts.P5}=ON (destination entry gate)" : null,
                    facts.Mode == 2 ? $"flag {SeathActivationFlag}=ON; flag {WarpInProgressFlag}=OFF" : null,
                    facts.P6 > 0 ? $"after crossing set/clear flag {facts.P6}={(facts.BeforeWarpPositive ? "ON" : "OFF")}" : null,
                }.Where(value => value != null));
                if (string.IsNullOrEmpty(requiredState)) requiredState = "no additional event-flag gate";
            }

            bool proven = failure == null;
            string actionRegion = facts.ActionRegionEntityId.HasValue
                ? facts.ActionRegionEntityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : facts.MechanismClass == SeathMode2P05308 ? "N/A: crystal interaction event 11700005" : "unresolved";
            var certification = new PhysicalEdgeCertification(
                facts.RuleId ?? "unresolved",
                facts.MechanismClass ?? "unclassified",
                facts.Mode,
                facts.Source ?? "unresolved",
                facts.Destination ?? "unresolved",
                requiredState,
                actionRegion,
                facts.Dispatcher ?? "unresolved",
                facts.DestinationPlayerEntityId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unresolved",
                facts.DestinationMap ?? "unresolved",
                facts.Landing ?? "unresolved",
                facts.Provenance ?? "unresolved",
                proven ? "PROVEN_ACTIONABLE" : "UNKNOWN",
                proven ? "All rule-specific staged-byte and policy checks passed." : failure);
            return new GeneratedTransitionDecision(
                proven ? PhysicalEdgeCertainty.PROVEN_ACTIONABLE : PhysicalEdgeCertainty.UNKNOWN,
                certification,
                required,
                forbidden,
                set,
                clear,
                facts.Evidence.ToArray());
        }

        private static string CommonFailure(GeneratedTransitionFacts facts)
        {
            if (facts.MechanismClass == SeathMode2P05308)
            {
                if (!facts.SourceObjectVerified) return "the staged crystal object/event binding is not unique and verified";
                if (!facts.DispatcherUnique) return "the Mode 2 trigger dispatcher is missing or ambiguous";
                if (!facts.DispatcherContractVerified) return "the staged Event5700 body does not match the reviewed dispatcher contract";
                if (!facts.BindingsMatch) return "Mode 2 parameters do not bind flag 5308 to the expected player/map";
                if (!facts.DestinationPlayerVerified) return "the destination player entity is missing or ambiguous";
                if (!facts.DestinationMapVerified) return "the destination map does not match the project route";
                if (!facts.LandingVerified) return "the generated player does not match the verified Seath prison landing anchor";
                return null;
            }

            if (facts.Mode == 1) return "Mode 1 only changes PvP/door state; it does not prove a randomized crossing";
            if (!string.Equals(facts.PairingCondition, "true", StringComparison.Ordinal)) return "generated pairing condition is not resolved to unconditional";
            if (!facts.SourceObjectVerified) return "the staged source fog object is missing or ambiguous";
            if (!facts.ActionRegionVerified) return "the staged source ActionRegion is missing, ambiguous, or mismatched";
            if (!facts.DispatcherUnique) return "the matching Event5700 initializer is missing or ambiguous";
            if (!facts.DispatcherContractVerified) return "the staged Event5700 body does not match the reviewed dispatcher contract";
            if (!facts.BindingsMatch) return "Event5700 source/action/destination bindings disagree with the observed transition";
            if (!facts.DestinationPlayerVerified) return "the destination player entity is missing or ambiguous";
            if (!facts.DestinationMapVerified) return "the dispatcher destination map does not match the selected side/policy";
            if (!facts.LandingVerified) return "the destination landing does not match the default or exact project CustomWarp rule";
            return null;
        }

        public static void SelfTest()
        {
            ValidOrdinaryMode0Passes();
            MissingActionRegionAndDispatcherFailClosed();
            DestinationAndLandingMismatchesFailClosed();
            ProjectCustomWarpRequiresExactPolicy();
            PvpRequiresIdentitySideAndUnconditionalPolicy();
            SeathMode2RequiresTheExactProducerChain();
            BossBoundariesRequireCompleteLifecycleSemantics();
            Console.WriteLine("Generated transition certification self-tests passed (7 groups, including negative cases).");
        }

        private static void ValidOrdinaryMode0Passes()
        {
            GeneratedTransitionDecision decision = Certify(Facts(OrdinaryMode0));
            Assert(decision.Certainty == PhysicalEdgeCertainty.PROVEN_ACTIONABLE
                && decision.Certification.RuleId == "ordinary-mode0-v1"
                && decision.Certification.ActionRegion == "1234"
                && decision.Certification.Dispatcher == "common/event0/init[7]/slot[2]"
                && decision.Certification.DestinationPlayer == "5678"
                && decision.RequiredEventFlags.SequenceEqual(new[] { 100, 200 })
                && decision.ForbiddenEventFlags.SequenceEqual(new[] { 300 })
                && decision.SetEventFlags.SequenceEqual(new[] { 400 }),
                "A fully verified Mode 0 edge or its state/effect metadata was not certified.");
        }

        private static void MissingActionRegionAndDispatcherFailClosed()
        {
            AssertUnknown(Facts(OrdinaryMode0) with { ActionRegionVerified = false }, "missing ActionRegion");
            AssertUnknown(Facts(OrdinaryMode0) with { DispatcherUnique = false }, "missing dispatcher");
            AssertUnknown(Facts(OrdinaryMode0) with { DispatcherContractVerified = false }, "changed dispatcher body");
        }

        private static void DestinationAndLandingMismatchesFailClosed()
        {
            AssertUnknown(Facts(OrdinaryMode0) with { BindingsMatch = false }, "wrong destination entity binding");
            AssertUnknown(Facts(OrdinaryMode0) with { DestinationMapVerified = false }, "wrong destination map");
            AssertUnknown(Facts(OrdinaryMode0) with { LandingVerified = false }, "bad landing");
            AssertUnknown(Facts(OrdinaryMode0) with { PairingCondition = "Any item X OR area Y" }, "unsupported condition");
        }

        private static void ProjectCustomWarpRequiresExactPolicy()
        {
            Assert(Certify(Facts(ProjectCustomWarpMode0)).Certainty == PhysicalEdgeCertainty.PROVEN_ACTIONABLE,
                "An exact project-owned CustomWarp failed certification.");
            AssertUnknown(Facts(ProjectCustomWarpMode0) with { CustomWarpPolicyVerified = false }, "unowned CustomWarp");
            AssertUnknown(Facts(ProjectCustomWarpMode0) with { LandingVerified = false }, "Firelink CustomWarp landing mismatch");
        }

        private static void PvpRequiresIdentitySideAndUnconditionalPolicy()
        {
            GeneratedTransitionFacts pvp = Facts(PvpMode0) with
            {
                PvpPolicyVerified = true,
                PvpSideVerified = true,
                PvpConditionUnconditional = true,
            };
            Assert(Certify(pvp).Certainty == PhysicalEdgeCertainty.PROVEN_ACTIONABLE,
                "A verified, unconditional PvP Mode 0 edge failed certification.");
            AssertUnknown(pvp with { PvpPolicyVerified = false }, "wrong PvP identity");
            AssertUnknown(pvp with { PvpSideVerified = false }, "wrong PvP side");
            AssertUnknown(pvp with { PvpConditionUnconditional = false }, "conditional PvP side");
            AssertUnknown(pvp with { Mode = 1 }, "PvP Mode 1 cleanup");
        }

        private static void SeathMode2RequiresTheExactProducerChain()
        {
            GeneratedTransitionFacts seath = Facts(SeathMode2P05308) with
            {
                Mode = 2,
                P0 = 5308,
                ActionRegionEntityId = null,
                SourceObjectVerified = true,
                ActionRegionVerified = false,
                SeathInteractionVerified = true,
            };
            GeneratedTransitionDecision decision = Certify(seath);
            Assert(decision.Certainty == PhysicalEdgeCertainty.PROVEN_ACTIONABLE
                && decision.RequiredEventFlags.Contains(5308)
                && decision.ForbiddenEventFlags.Contains(15)
                && decision.ClearEventFlags.Contains(5308)
                && decision.Certification.ActionRegion.StartsWith("N/A:", StringComparison.Ordinal),
                "The fully verified Seath crystal route was not certified with its trigger/system state.");
            AssertUnknown(seath with { P0 = 0 }, "Mode-2 p0=0");
            AssertUnknown(seath with { SeathInteractionVerified = false }, "missing Seath producer");
            AssertUnknown(seath with { BindingsMatch = false }, "Seath wrong destination");
        }

        private static void BossBoundariesRequireCompleteLifecycleSemantics()
        {
            GeneratedTransitionFacts boss = Facts(BossBoundaryMode0) with
            {
                BossLifecycleVerified = false,
            };
            AssertUnknown(boss, "incomplete boss boundary");
            Assert(Certify(boss with { BossLifecycleVerified = true }).Certainty == PhysicalEdgeCertainty.PROVEN_ACTIONABLE,
                "A fully evidenced boss boundary failed certification.");
        }

        private static GeneratedTransitionFacts Facts(string mechanism) => new(
            mechanism + "-v1", mechanism, 0, "m14_00/o4961:sideA", "m11_00/player:5678",
            "demonruins", "fog-action:o4961:side:demonruins:region:1234", "paintedworld", "player:5678",
            1234, "common/event0/init[7]/slot[2]", 5678, "target fog object-relative landing verified",
            "current staging bytes; unanchored historical package", "true",
            true, true, true, true, true, true, true, true,
            false, false, false, mechanism == ProjectCustomWarpMode0,
            false, false,
            1411790, 5678, 300, 0, 100, 200, 400, true,
            new[] { new PhysicalEvidence("SyntheticFixture", "certifier test", TestHash) });

        private static void AssertUnknown(GeneratedTransitionFacts facts, string name)
        {
            GeneratedTransitionDecision decision = Certify(facts);
            Assert(decision.Certainty == PhysicalEdgeCertainty.UNKNOWN
                && decision.Certification.Status == "UNKNOWN"
                && !string.IsNullOrWhiteSpace(decision.Certification.Reason),
                "The certification rule did not fail closed for " + name + ".");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
