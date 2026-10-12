using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FogHost
{
    /// <summary>
    /// Authored semantic identity and placement policy for one key item. The numeric game ID is
    /// resolved from the verified DSR English item tables instead of being copied into policy.
    /// </summary>
    public sealed record FogKeyItemPolicy(
        FogItemName Name,
        string EnglishGameName,
        FogGameItemCategory Category,
        FogAreaId DefaultArea,
        FogKeyItemTags Tags);

    /// <summary>Authored start policy uses the named spawn region, not a copied entity ID.</summary>
    public sealed record FogCustomStartPolicy(
        string Id,
        string DisplayName,
        FogAreaId Area,
        FogMapId Map,
        string SpawnPointRegionName,
        FogCustomStartSemantics Semantics = FogCustomStartSemantics.None);

    public sealed record FogAreaTagPolicy(FogAreaId Area, FogAreaTags Tags, string Rationale);

    /// <summary>Project rule that resolves an area ID namespace to its owning DSR map.</summary>
    public sealed record FogAreaMapMembershipPolicy(string Prefix, FogMapId Map, string Rationale);

    /// <summary>Classification used only while validating project-owned static graph links.</summary>
    public enum FogAreaLinkKind { Static, CrossMapGlitch }

    /// <summary>Project-owned static graph route, separate from Entrance and Warp transitions.</summary>
    public sealed record FogAreaLinkPolicy(
        FogAreaId Source,
        FogAreaId Destination,
        FogCondition Condition,
        FogAreaLinkTags Tags,
        string Rationale,
        long? DestinationBossEventId = null,
        FogAreaLinkKind Kind = FogAreaLinkKind.Static);

    /// <summary>
    /// Project-owned fixed entrance boundary backed by a pristine interaction event. It is kept
    /// separate from the runtime DTO until its complete Entrance/Side fields have been reviewed.
    /// </summary>
    public sealed record FogProjectEntranceBoundaryPolicy(
        string ProjectTransitionId,
        FogMapId Map,
        int RuntimeId,
        string InteractionObjectName,
        int InteractionObjectEntityId,
        long InteractionEventId,
        FogAreaId ASideArea,
        FogAreaId BSideArea,
        FogItemName RequiredItemName,
        FogGameItemId RequiredItem,
        string RequiredItemEnglishName,
        string Rationale)
    {
        public FogEntrance ToFogEntrance()
        {
            FogCondition doorCondition = new FogCondition(FogConditionKind.Atom, Array.AsReadOnly(new[]
            {
                FogConditionReference.KeyItem(RequiredItemName),
            }));
            static FogEntranceSide Side(FogAreaId area) => new FogEntranceSide(
                area, 0, 0, 0, 0, 0, null, 0, null, null, 0, 0, null, null, FogEntranceSideTags.None);

            return new FogEntrance(
                InteractionObjectName,
                RuntimeId,
                Map,
                $"{ASideArea.Value} to {BSideArea.Value}",
                doorCondition,
                FogEntranceTags.Door,
                Side(ASideArea),
                Side(BSideArea));
        }
    }

    /// <summary>Project policy for a boss encounter node with no independent static Area.To route.</summary>
    public sealed record FogAreaNoStaticLinksPolicy(
        FogAreaId Area,
        long? BossEventId,
        string Rationale,
        FogMapId? EntranceMap = null,
        int? EntranceObjectEntityId = null,
        int? EntranceTriggerRegionEntityId = null,
        long? EntranceEventId = null);

    /// <summary>Project-owned boss-area classification anchored to a pristine boss event and its MSB enemy part(s).</summary>
    public sealed record FogBossAreaTagPolicy(
        FogAreaId Area,
        FogMapId Map,
        long BossEventId,
        IReadOnlyList<string> EnemyPartNames,
        string Rationale);

    /// <summary>Authored lot-location policy groups physical DSR treasure parts by their map collision.</summary>
    public sealed record FogTreasureCollisionAreaPolicy(FogMapId Map, string Collision, FogAreaId Area);

    /// <summary>Authored enemy-scaling assignment for one map default or one collision.</summary>
    public sealed record FogEnemyCollisionAreaPolicy(
        FogMapId Map,
        string Collision,
        FogAreaId Area,
        IReadOnlyList<string> IncludedEnemyNames);

    /// <summary>
    /// Project-owned rule for a collision containing a boss whose HandleBossDefeat event ID is
    /// also consumed as an event-flag parameter by common event 970.
    /// </summary>
    public sealed record FogBossDefeatFlagCollisionPolicy(FogMapId Map, string Collision);

    public sealed record FogEnemyCollisionGroup(FogMapId Map, string Collision, IReadOnlyList<FogDsrEnemyFact> Enemies);

    public sealed record FogEnemyScalingResolution(
        IReadOnlyList<FogEnemyScalingRule> Rules,
        IReadOnlyList<FogEnemyCollisionGroup> UnresolvedCollisionGroups)
    {
        public bool IsComplete => UnresolvedCollisionGroups.Count == 0;
    }

    public sealed record FogTreasureLotResolution(
        IReadOnlyList<FogLotLocation> Locations,
        IReadOnlyList<FogDsrTreasureLotFact> UnresolvedReferences)
    {
        public bool IsComplete => UnresolvedReferences.Count == 0;
    }

    /// <summary>
    /// Initial project-owned random-start candidates. Each region name is resolved against the
    /// pristine MSB; the numeric spawn entity IDs are never authored here. The final Kiln is
    /// intentionally excluded so random start cannot place a fresh run at the final encounter.
    /// </summary>
    public static class FogProjectAnnotationPolicy
    {
        public const string AreaScalingBasePolicyRationale =
            "Project policy leaves ScalingBase unset for production. The shipped Fog option contract exposes neither scalingbase nor dumpdist, and FogMod only reads ScalingBase when one of those debug flags is enabled.";

        /// <summary>
        /// Stable compatibility identity/order inventory for the 131 logical Areas. These are only
        /// graph keys; tags, scaling, topology, conditions, and map ownership are resolved separately.
        /// The order is preserved because the pinned engine consumes ordered annotation lists.
        /// </summary>
        public static IReadOnlyList<FogAreaId> AreaIdentityOrder { get; } = Array.AsReadOnly(new[]
        {
            "depths", "depths_gaping",
            "burg_start", "burg_upper", "burg_upperresidence", "burg_bonfire", "burg_above_bonfire", "burg_ladder", "burg_lower", "burg_lowerresidence", "burg_taurus", "burg_posttaurus", "burg_capra",
            "parish_hellkite", "parish_church", "parish_andre", "parish_gargoyles", "parish_bell",
            "firelink", "firelink_crow", "firelink_elevator", "firelink_gate",
            "paintedworld", "paintedworld_annex", "paintedworld_priscilla",
            "darkroot", "darkroot_stone", "darkroot_butterfly", "darkroot_postbutterfly", "darkroot_forest", "darkroot_sif",
            "dlc_start", "dlc_guardian", "dlc_sanctuary", "dlc_forest", "dlc_artorias", "dlc_hallway", "dlc_gough", "dlc_kalameet", "dlc_kalameetdeath", "dlc_township", "dlc_townshiphidden", "dlc_manus", "dlc_top", "dlc_bottom", "dlc_postartorias",
            "catacombs_start", "catacombs", "catacombs_prepinwheel", "catacombs_pinwheel",
            "totg", "totg_lower", "totg_lordvessel", "totg_coffin", "totg_nito",
            "greathollow", "greathollow_ashlake", "greathollow_start",
            "blighttown", "blighttown_upper", "blighttown_quelaag", "blighttown_bell",
            "demonruins", "demonruins_ceaseless", "demonruins_lava", "demonruins_ceaselesslava", "demonruins_lower", "demonruins_lordvessel", "demonruins_firesage", "demonruins_stairs", "demonruins_elevator", "demonruins_centipede", "demonruins_izalithprelava", "demonruins_izalithlava", "demonruins_izalith", "demonruins_bedofchaos",
            "sens", "sens_mid", "sens_cage", "sens_upper", "sens_irongolem",
            "anorlondo_start", "anorlondo_os", "anorlondo_prerafters", "anorlondo_rafters", "anorlondo_main", "anorlondo_darkmoon", "anorlondo_postrafters", "anorlondo_painting", "anorlondo_chandelier", "anorlondo_building", "anorlondo_cathedral", "anorlondo_front", "anorlondo_blacksmith", "anorlondo_elevator1", "anorlondo_elevator2", "anorlondo_gwynevere", "anorlondo_gwyndolin",
            "newlondo", "newlondo_building", "newlondo_lower", "newlondo_lowerskip", "newlondo_stairs", "newlondo_fourkings", "newlondo_drakes", "newlondo_drakesbeatrice",
            "dukes_start", "dukes_library", "dukes_crystalgolem", "dukes_hallway", "dukes_prison", "dukes_extra", "dukes_giantcell", "dukes_library2", "dukes_machines", "dukes_caves", "dukes_seath1", "dukes_seath2",
            "kiln", "kiln_start", "kiln_stairs", "kiln_gwyn",
            "asylum", "asylum_outside", "asylum_oscar", "asylum_prebalcony", "asylum_f2west", "asylum_balcony", "asylum_doll", "asylum_stray", "asylum_loop",
        }.Select(value => new FogAreaId(value)).ToArray());

        public static IReadOnlyList<FogAreaMapMembershipPolicy> AreaMapMembershipPolicies { get; } = Array.AsReadOnly(new[]
        {
            MapPrefix("asylum", "asylum"),
            MapPrefix("depths", "depths"),
            MapPrefix("burg", "parish"),
            MapPrefix("parish", "parish"),
            MapPrefix("firelink", "firelink"),
            MapPrefix("paintedworld", "paintedworld"),
            MapPrefix("darkroot", "darkroot"),
            MapPrefix("dlc", "dlc"),
            MapPrefix("catacombs", "catacombs"),
            MapPrefix("totg", "totg"),
            MapPrefix("greathollow", "greathollow"),
            MapPrefix("blighttown", "blighttown"),
            MapPrefix("demonruins", "demonruins"),
            MapPrefix("sens", "sens"),
            MapPrefix("anorlondo", "anorlondo"),
            MapPrefix("newlondo", "newlondo"),
            MapPrefix("dukes", "dukes"),
            MapPrefix("kiln", "kiln"),
        });

        private static FogAreaMapMembershipPolicy MapPrefix(string prefix, string map) => new FogAreaMapMembershipPolicy(
            prefix,
            new FogMapId(map),
            prefix == "burg"
                ? "The Burg is a named progression subregion in the Parish DSR map."
                : $"The area ID namespace '{prefix}' follows the DSR map key '{map}'.");

        /// <summary>Resolves a project area ID by the authored map namespace; callers still validate the map against pristine facts.</summary>
        public static FogMapId ResolveAreaMap(FogAreaId area)
        {
            if (string.IsNullOrWhiteSpace(area.Value)) throw new ArgumentException("A project area ID is required.", nameof(area));
            FogAreaMapMembershipPolicy[] matches = AreaMapMembershipPolicies
                .Where(policy => string.Equals(area.Value, policy.Prefix, StringComparison.Ordinal)
                    || area.Value.StartsWith(policy.Prefix + "_", StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Project area '{area}' matches {matches.Length} DSR map-prefix policies; exactly one is required.");
            return matches[0].Map;
        }

        /// <summary>Resolves the legacy graph key using an explicit alias when one is required, otherwise preserving the project ID.</summary>
        public static FogModAreaName ResolveAreaProjection(FogAreaId area)
        {
            if (string.IsNullOrWhiteSpace(area.Value)) throw new ArgumentException("A project area ID is required.", nameof(area));
            return CustomStartAreaProjection.TryGetValue(area, out FogModAreaName projection)
                ? projection
                : new FogModAreaName(area.Value);
        }

        private static readonly IReadOnlyDictionary<FogAreaId, FogModAreaName> CustomStartAreaProjection =
            new System.Collections.ObjectModel.ReadOnlyDictionary<FogAreaId, FogModAreaName>(new Dictionary<FogAreaId, FogModAreaName>
            {
                [new FogAreaId("firelink")] = new FogModAreaName("firelink"),
                [new FogAreaId("parish_andre")] = new FogModAreaName("parish_andre"),
                [new FogAreaId("darkroot")] = new FogModAreaName("darkroot"),
                [new FogAreaId("blighttown")] = new FogModAreaName("blighttown"),
                [new FogAreaId("depths")] = new FogModAreaName("depths"),
                [new FogAreaId("demonruins")] = new FogModAreaName("demonruins"),
                [new FogAreaId("sens_mid")] = new FogModAreaName("sens_mid"),
                [new FogAreaId("anorlondo_start")] = new FogModAreaName("anorlondo_start"),
                [new FogAreaId("anorlondo_building")] = new FogModAreaName("anorlondo_building"),
                [new FogAreaId("paintedworld")] = new FogModAreaName("paintedworld"),
                [new FogAreaId("totg")] = new FogModAreaName("totg"),
                [new FogAreaId("catacombs")] = new FogModAreaName("catacombs"),
                [new FogAreaId("dukes_library2")] = new FogModAreaName("dukes_library2"),
                [new FogAreaId("demonruins_izalithprelava")] = new FogModAreaName("demonruins_izalithprelava"),
                [new FogAreaId("dlc_sanctuary")] = new FogModAreaName("dlc_sanctuary"),
                [new FogAreaId("dlc_township")] = new FogModAreaName("dlc_township"),
            });

        public static IReadOnlyList<FogCustomStartPolicy> CustomStarts { get; } = Array.AsReadOnly(new[]
        {
            new FogCustomStartPolicy("firelink-bonfire", "Firelink Shrine", new FogAreaId("firelink"), new FogMapId("firelink"), "復活ポイント00"),
            new FogCustomStartPolicy("parish-andre-bonfire", "Undead Parish", new FogAreaId("parish_andre"), new FogMapId("parish"), "復活ポイント04"),
            new FogCustomStartPolicy("darkroot-bonfire", "Darkroot Garden", new FogAreaId("darkroot"), new FogMapId("darkroot"), "復活ポイント00"),
            new FogCustomStartPolicy("blighttown-bonfire", "Blighttown", new FogAreaId("blighttown"), new FogMapId("blighttown"), "復活ポイント01"),
            new FogCustomStartPolicy("depths-bonfire", "Depths", new FogAreaId("depths"), new FogMapId("depths"), "復活ポイント00", FogCustomStartSemantics.BypassDepthsEntryKeyDoor),
            new FogCustomStartPolicy("demonruins-bonfire", "Demon Ruins", new FogAreaId("demonruins"), new FogMapId("demonruins"), "復活ポイント01"),
            new FogCustomStartPolicy("sens-midpoint-bonfire", "Sen's Fortress", new FogAreaId("sens_mid"), new FogMapId("sens"), "復活ポイント01"),
            new FogCustomStartPolicy("anorlondo-approach-bonfire", "Anor Londo", new FogAreaId("anorlondo_start"), new FogMapId("anorlondo"), "復活ポイント00"),
            new FogCustomStartPolicy("anorlondo-cathedral-bonfire", "Anor Londo Cathedral", new FogAreaId("anorlondo_building"), new FogMapId("anorlondo"), "復活ポイント01"),
            new FogCustomStartPolicy("paintedworld-bonfire", "Painted World of Ariamis", new FogAreaId("paintedworld"), new FogMapId("paintedworld"), "復活ポイント00"),
            new FogCustomStartPolicy("tomb-of-giants-bonfire", "Tomb of the Giants", new FogAreaId("totg"), new FogMapId("totg"), "復活ポイント01"),
            new FogCustomStartPolicy("catacombs-bonfire", "Catacombs", new FogAreaId("catacombs"), new FogMapId("catacombs"), "復活ポイント01"),
            new FogCustomStartPolicy("dukes-archives-bonfire", "Duke's Archives", new FogAreaId("dukes_library2"), new FogMapId("dukes"), "復活ポイント00"),
            new FogCustomStartPolicy("izalith-approach-bonfire", "Lost Izalith", new FogAreaId("demonruins_izalithprelava"), new FogMapId("demonruins"), "復活ポイント06"),
            new FogCustomStartPolicy("sanctuary-garden-bonfire", "Oolacile Sanctuary", new FogAreaId("dlc_sanctuary"), new FogMapId("dlc"), "復活ポイント01"),
            new FogCustomStartPolicy("township-bonfire", "Oolacile Township", new FogAreaId("dlc_township"), new FogMapId("dlc"), "復活ポイント02"),
        });

        /// <summary>
        /// Project-owned progression-cost and optionality classifications. These name the logical
        /// role of each project Area; map ownership is independently checked against pristine DSR.
        /// Optional areas are not mandatory reachability objectives. Trivial areas are connectors
        /// or transition segments that add no independent progression-distance tier.
        /// </summary>
        public static IReadOnlyList<FogAreaTagPolicy> AreaTagPolicies { get; } = Array.AsReadOnly(new[]
        {
            AreaTag("burg_start", FogAreaTags.Trivial, "The Burg entry landing is a transition segment, not an independent progression milestone."),
            AreaTag("burg_bonfire", FogAreaTags.Trivial, "The lower Burg bonfire pocket is a rest-point connector and adds no progression-distance tier."),
            AreaTag("burg_above_bonfire", FogAreaTags.Trivial, "The short Burg passage above the bonfire is a connector between progression spaces."),
            AreaTag("burg_ladder", FogAreaTags.Trivial, "The Burg ladder segment is a shortcut connector, not a separate progression objective."),
            AreaTag("burg_posttaurus", FogAreaTags.Trivial, "The route immediately beyond Taurus is a transition segment; the encounter itself remains represented by its boss area."),
            AreaTag("parish_bell", FogAreaTags.Trivial, "The Parish bell interaction space is a completion connector after the Gargoyle encounter."),
            AreaTag("firelink_elevator", FogAreaTags.Trivial, "The Firelink elevator landing is a vertical connector, not an independent progression milestone."),
            AreaTag("firelink_gate", FogAreaTags.Trivial, "The Firelink gate approach is a connector between the hub and its fixed passage."),
            AreaTag("dlc_start", FogAreaTags.Trivial, "The DLC arrival segment is an entry connector; progression cost begins in its destination spaces."),
            AreaTag("dlc_kalameet", FogAreaTags.Trivial, "The Kalameet approach segment is a connector; the encounter node owns the boss progression cost."),
            AreaTag("dlc_townshiphidden", FogAreaTags.Trivial, "The hidden Township passage is a connector within the same optional branch."),
            AreaTag("dlc_top", FogAreaTags.Trivial, "The upper DLC transition segment does not form a separate progression milestone."),
            AreaTag("dlc_bottom", FogAreaTags.Trivial, "The lower DLC transition segment does not form a separate progression milestone."),
            AreaTag("dlc_postartorias", FogAreaTags.Optional | FogAreaTags.Trivial, "This is an optional DLC continuation after Artorias; it is not required for the main progression objective and its connector segment adds no cost."),
            AreaTag("catacombs_prepinwheel", FogAreaTags.Trivial, "The approach to Pinwheel is a transition segment; the encounter area carries the boss progression cost."),
            AreaTag("greathollow_start", FogAreaTags.Optional | FogAreaTags.Trivial, "Great Hollow is an optional side branch from the main route; its entry segment adds no progression-distance tier."),
            AreaTag("blighttown_bell", FogAreaTags.Trivial, "The Blighttown bell interaction space is a completion connector after Quelaag."),
            AreaTag("demonruins_lordvessel", FogAreaTags.Trivial, "The Lordvessel access segment is a transition node; the required Lord Soul encounters carry their own progression cost."),
            AreaTag("demonruins_izalithprelava", FogAreaTags.Trivial, "The post-Centipede, pre-lava passage is a connector beyond the boss-arena exit wall; it contributes no separate progression-distance cost."),
            AreaTag("demonruins_izalithlava", FogAreaTags.Trivial, "The lava crossing segment is a connector on the route to Lost Izalith rather than a separate progression milestone."),
            AreaTag("sens_cage", FogAreaTags.Trivial, "The Sen's cage-lift segment is a transport connector, not an independent progression objective."),
            AreaTag("anorlondo_darkmoon", FogAreaTags.Trivial, "The Darkmoon access segment is a connector to an optional encounter branch."),
            AreaTag("anorlondo_chandelier", FogAreaTags.Trivial, "The chandelier access segment is a traversal connector, not an independent progression milestone."),
            AreaTag("anorlondo_front", FogAreaTags.Optional | FogAreaTags.Trivial, "The front-side Anor Londo branch is optional to the required route; its connector space adds no progression-distance tier."),
            AreaTag("anorlondo_elevator1", FogAreaTags.Trivial, "The first Anor Londo elevator landing is a vertical connector between progression spaces."),
            AreaTag("anorlondo_elevator2", FogAreaTags.Trivial, "The second Anor Londo elevator landing is a vertical connector between progression spaces."),
            AreaTag("newlondo_lowerskip", FogAreaTags.Trivial, "The lower New Londo shortcut segment is a connector and not a separate progression milestone."),
            AreaTag("newlondo_drakesbeatrice", FogAreaTags.Trivial, "The New Londo approach segment is a connector; its route is classified by the surrounding progression areas."),
            AreaTag("dukes_hallway", FogAreaTags.Trivial, "The Archives hallway is a traversal connector between progression spaces."),
            AreaTag("dukes_machines", FogAreaTags.Trivial, "The Archives mechanism space is a transition connector, not an independent progression objective."),
            AreaTag("asylum_oscar", FogAreaTags.Trivial, "The Oscar encounter's opening segment is a tutorial connector, not a separate progression tier."),
            AreaTag("asylum_prebalcony", FogAreaTags.Trivial, "The Asylum upper approach is a transition segment before the balcony encounter space."),
            AreaTag("asylum_f2west", FogAreaTags.Trivial, "The west upper-floor segment is a traversal connector, not an independent progression milestone."),
            AreaTag("asylum_doll", FogAreaTags.Trivial, "The Asylum cell access is a pickup connector; it does not create a separate progression-distance tier."),
            AreaTag("asylum_loop", FogAreaTags.Optional | FogAreaTags.Trivial, "The return loop to the Asylum is optional after the tutorial and its connector segment adds no progression cost."),
        });

        private static FogAreaTagPolicy AreaTag(string area, FogAreaTags tags, string rationale) =>
            new FogAreaTagPolicy(new FogAreaId(area), tags, rationale);

        /// <summary>
        /// Resolves the full Area tag state. Ordinary progression nodes use the project's default
        /// unit-cost classification; boss, optional, and connector exceptions are explicitly listed
        /// above and independently validated against pristine DSR facts where applicable.
        /// </summary>
        public static IReadOnlyList<FogAreaTagPolicy> ResolveCompleteAreaTagPolicies(FogDsrFacts facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            IReadOnlyList<FogAreaTagPolicy> explicitPolicies = FogAnnotationPolicyResolver.ResolveAreaTagPolicies(facts, AreaTagPolicies)
                .Concat(FogAnnotationPolicyResolver.ResolveBossAreaTagPolicies(facts, BossAreaTagPolicies))
                .ToArray();
            var byArea = explicitPolicies.ToDictionary(policy => policy.Area);
            const string defaultRationale = "Project default: this node represents an ordinary progression unit; boss cost, optionality, and zero-cost connector behavior are applied only by their explicit project rules.";
            var complete = new List<FogAreaTagPolicy>(AreaIdentityOrder.Count);
            foreach (FogAreaId area in AreaIdentityOrder)
            {
                FogMapId map = ResolveAreaMap(area);
                if (!facts.Maps.ContainsKey(map))
                    throw new KeyNotFoundException($"Area tag assignment for '{area}' resolves to missing pristine DSR map '{map}'.");
                complete.Add(byArea.TryGetValue(area, out FogAreaTagPolicy policy)
                    ? policy
                    : new FogAreaTagPolicy(area, FogAreaTags.None, defaultRationale));
            }
            return Array.AsReadOnly(complete.ToArray());
        }

        /// <summary>
        /// Static movement policies include the hazardous Depths drop into Gaping Dragon's arena
        /// and the hard-only upper-to-lower Tomb of the Giants descent. Boss/fog-gate transitions
        /// remain separate Entrance/Warp records.
        /// </summary>
        public static IReadOnlyList<FogAreaLinkPolicy> AreaLinkPolicies { get; } = Array.AsReadOnly(new[]
        {
            new FogAreaLinkPolicy(
                new FogAreaId("depths"),
                new FogAreaId("depths_gaping"),
                null,
                FogAreaLinkTags.Hard,
                "A deliberate hazardous drop from the Depths reaches the Gaping Dragon arena; the project models this static route only in hard mode, separately from the arena fog-gate Entrance.",
                11000001),
            new FogAreaLinkPolicy(
                new FogAreaId("totg"),
                new FogAreaId("totg_lower"),
                null,
                FogAreaLinkTags.Hard,
                "Pristine ToTG EMEVD event 11310002 checks the Patches-kick trigger region 1313400 and references the post-kick landing region 1312121. Project policy models that one-way upper-to-lower descent as a hard-only static edge; door and fog-gate transitions remain separate Entrance/Warp records."),
        });

        /// <summary>
        /// Evidence-backed fixed boundary rules. These name project semantics and pristine game
        /// anchors; they do not import FogMod endpoint or condition values.
        /// </summary>
        public static IReadOnlyList<FogProjectEntranceBoundaryPolicy> EntranceBoundaryPolicies { get; } = Array.AsReadOnly(new[]
        {
            new FogProjectEntranceBoundaryPolicy(
                "depths-blighttown-key-door",
                new FogMapId("depths"),
                1001200,
                "o1309",
                1001200,
                11000110,
                new FogAreaId("depths"),
                new FogAreaId("blighttown"),
                new FogItemName("blighttown_key"),
                new FogGameItemId((int)FogGameItemCategory.Goods, 2007),
                "Blighttown Key",
                "Pristine Depths event 11000110 checks the DSR Blighttown Key and the action on object o1309/entity 1001200. Project policy models the fixed keyed passage from Depths into Blighttown as an Entrance, not an Area.To edge."),
            new FogProjectEntranceBoundaryPolicy(
                "darkroot-crest-gate",
                new FogMapId("darkroot"),
                1201986,
                "o2908_0000",
                1201986,
                11200100,
                new FogAreaId("darkroot"),
                new FogAreaId("darkroot_forest"),
                new FogItemName("crest_of_artorias"),
                new FogGameItemId((int)FogGameItemCategory.Goods, 2002),
                "Crest of Artorias",
                "Pristine Darkroot event 11200100 checks the Crest of Artorias at gate object o2908_0000/entity 1201986; the paired gate action is anchored to region 1202500. Project policy assigns the fixed passage from Darkroot Garden into the sealed forest to one key-gated Entrance."),
        });

        /// <summary>
        /// Boss-cost areas are explicit project progression classifications. Each is anchored to
        /// the matching pristine HandleBossDefeat event and exact MSB enemy part set.
        /// </summary>
        public static IReadOnlyList<FogBossAreaTagPolicy> BossAreaTagPolicies { get; } = Array.AsReadOnly(new[]
        {
            BossArea("depths_gaping", "depths", 11000001, "c5260_0000"),
            BossArea("burg_taurus", "parish", 11010902, "c2240_0000"),
            BossArea("burg_capra", "parish", 11010901, "c2250_0000"),
            BossArea("parish_gargoyles", "parish", 11010001, "c5350_0000"),
            BossArea("paintedworld_priscilla", "paintedworld", 11100000, "c2730_0000"),
            BossArea("darkroot_butterfly", "darkroot", 11200900, "c3230_0000"),
            BossArea("darkroot_sif", "darkroot", 11200001, "c5210_0000"),
            BossArea("dlc_guardian", "dlc", 11210000, "c3471_0000"),
            BossArea("dlc_artorias", "dlc", 11210001, "c4100_0000"),
            BossArea("dlc_kalameetdeath", "dlc", 11210005, "c4510_0001"),
            BossArea("dlc_manus", "dlc", 11210002, "c4500_0000"),
            BossArea("catacombs_pinwheel", "catacombs", 11300001, "c3320_0000"),
            BossArea("totg_nito", "totg", 11310001, "c5220_0000"),
            BossArea("blighttown_quelaag", "blighttown", 11400001, "c5280_0000"),
            BossArea("demonruins_ceaseless", "demonruins", 11410410, "c2231_0000"),
            BossArea("demonruins_firesage", "demonruins", 11410900, "c5250_0000"),
            BossArea("demonruins_centipede", "demonruins", 11410901, "c5200_0000"),
            BossArea("demonruins_bedofchaos", "demonruins", 11410001, "c5400_0000"),
            BossArea("sens_irongolem", "sens", 11500001, "c2320_0000"),
            BossArea("anorlondo_os", "anorlondo", 11510001, "c5271_0000", "c2360_0001"),
            BossArea("anorlondo_gwyndolin", "anorlondo", 11510900, "c5320_0000"),
            BossArea("newlondo_fourkings", "newlondo", 11600001, "c5390_0000"),
            BossArea("dukes_seath2", "dukes", 11700001, "c5290_0001"),
            BossArea("kiln_gwyn", "kiln", 11800001, "c5370_0000"),
        });

        /// <summary>
        /// Boss encounter areas and the Pinwheel approach have no separate static route at their
        /// fog-gate boundary; those transitions belong to the Entrance model. The Catacombs gate
        /// is anchored to its pristine object, trigger region, and event on both logical sides.
        /// </summary>
        public static IReadOnlyList<FogAreaNoStaticLinksPolicy> AreaNoStaticLinksPolicies { get; } = Array.AsReadOnly(
            BossAreaTagPolicies
                .Select(policy => new FogAreaNoStaticLinksPolicy(
                    policy.Area,
                    policy.BossEventId,
                    policy.Area == new FogAreaId("catacombs_pinwheel")
                        ? "Project policy assigns the Pinwheel arena's return boundary to its boss Entrance and does not duplicate that physical gate as Area.To. Pristine gate object entity 1301990 and trigger region entity 1302998 anchor the Catacombs-side boundary; event 11305350 short-warps skeleton enemies, not the player. HandleBossDefeat event 11300001 targets c3320_0000."
                        : $"Project policy treats this event-anchored boss arena as an encounter node with no independent static Area.To route; its physical transitions belong to the Entrance/Warp model. Pristine HandleBossDefeat event {policy.BossEventId} targets {string.Join(", ", policy.EnemyPartNames)}.",
                    policy.Area == new FogAreaId("catacombs_pinwheel") ? new FogMapId("catacombs") : null,
                    policy.Area == new FogAreaId("catacombs_pinwheel") ? 1301990 : null,
                    policy.Area == new FogAreaId("catacombs_pinwheel") ? 1302998 : null,
                    policy.Area == new FogAreaId("catacombs_pinwheel") ? 11305391 : null))
                .Append(new FogAreaNoStaticLinksPolicy(
                    new FogAreaId("catacombs_prepinwheel"),
                    null,
                    "Project policy assigns the Pinwheel fog-gate transition to the Entrance model on the approach side too; pristine Catacombs gate object entity 1301990, trigger region entity 1302998, and interaction event 11305391 anchor the boundary. This prevents duplicating the same gate as a static Area.To route.",
                    new FogMapId("catacombs"),
                    1301990,
                    1302998,
                    11305391))
                .ToArray());

        private static FogBossAreaTagPolicy BossArea(string area, string map, long eventId, params string[] enemyParts) =>
            new FogBossAreaTagPolicy(
                new FogAreaId(area),
                new FogMapId(map),
                eventId,
                Array.AsReadOnly(enemyParts),
                $"Project policy assigns boss progression cost to this encounter area; pristine event {eventId} targets MSB enemy part(s) {string.Join(", ", enemyParts)}.");

        /// <summary>
        /// Area records whose graph semantics have been independently reviewed. Static links are
        /// emitted from the project-owned route policy; other travel is represented by the
        /// entrance/warp model. These records have no special scaling base. Other start nodes
        /// remain map-membership placeholders pending graph reconstruction.
        /// </summary>
        public static IReadOnlyList<FogArea> ReviewedStartAreaRecords { get; } = Array.AsReadOnly(new[]
        {
            StartArea("firelink", "firelink"),
            StartArea("parish_andre", "parish"),
            StartArea("blighttown", "blighttown"),
            StartArea("anorlondo_start", "anorlondo"),
            StartArea("anorlondo_building", "anorlondo"),
            StartArea("catacombs", "catacombs"),
            StartArea("totg", "totg", StaticLinksFrom("totg")),
            StartArea("depths", "depths", StaticLinksFrom("depths")),
        });

        private static IReadOnlyList<FogAreaLink> StaticLinksFrom(string source) => Array.AsReadOnly(AreaLinkPolicies
            .Where(policy => string.Equals(policy.Source.Value, source, StringComparison.Ordinal))
            .Select(policy => new FogAreaLink(policy.Destination, policy.Condition, policy.Tags))
            .ToArray());

        private static FogArea StartArea(string id, string map, IReadOnlyList<FogAreaLink> links = null) => new FogArea(
            new FogAreaId(id),
            new FogModAreaName(id),
            Array.AsReadOnly(new[] { new FogMapId(map) }),
            FogAreaTags.None,
            null,
            links ?? Array.Empty<FogAreaLink>());

        /// <summary>Area-node slice that guarantees each authored random start belongs to a declared DSR map.</summary>
        public static IReadOnlyList<FogArea> CustomStartAreaNodes { get; } = Array.AsReadOnly(CustomStarts
            .GroupBy(start => start.Area)
            .Select(BuildCustomStartAreaNode)
            .ToArray());

        private static FogArea BuildCustomStartAreaNode(IGrouping<FogAreaId, FogCustomStartPolicy> group)
        {
            FogMapId[] maps = group.Select(start => start.Map).Distinct().ToArray();
            FogArea reviewed = ReviewedStartAreaRecords.SingleOrDefault(area => area.Id == group.Key);
            if (reviewed != null)
            {
                if (!new HashSet<FogMapId>(reviewed.Maps).SetEquals(maps))
                    throw new InvalidDataException($"Reviewed area '{reviewed.Id}' map membership disagrees with its custom-start spawn maps.");
                return reviewed;
            }

            if (!CustomStartAreaProjection.TryGetValue(group.Key, out FogModAreaName fogModName))
                throw new InvalidDataException($"Custom-start area '{group.Key}' has no explicit FogMod projection key.");
            FogAreaTagPolicy tagPolicy = AreaTagPolicies.SingleOrDefault(policy => policy.Area == group.Key);
            return new FogArea(group.Key, fogModName, Array.AsReadOnly(maps), tagPolicy?.Tags ?? FogAreaTags.None, null, Array.Empty<FogAreaLink>());
        }
    }

    public static class FogAnnotationPolicyResolver
    {
        /// <summary>Validates authored fixed-boundary rules against exact pristine DSR anchors.</summary>
        public static IReadOnlyList<FogProjectEntranceBoundaryPolicy> ResolveEntranceBoundaryPolicies(
            FogDsrFacts facts,
            IEnumerable<FogProjectEntranceBoundaryPolicy> policies = null)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            FogProjectEntranceBoundaryPolicy[] entries = (policies ?? FogProjectAnnotationPolicy.EntranceBoundaryPolicies).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (FogProjectEntranceBoundaryPolicy policy in entries)
            {
                if (policy == null) throw new InvalidDataException("An Entrance boundary policy cannot be null.");
                if (string.IsNullOrWhiteSpace(policy.ProjectTransitionId) || !ids.Add(policy.ProjectTransitionId))
                    throw new InvalidDataException("Entrance boundary project transition IDs must be nonempty and unique.");
                if (string.IsNullOrWhiteSpace(policy.Rationale))
                    throw new InvalidDataException($"Entrance boundary '{policy.ProjectTransitionId}' has no project rationale.");
                if (policy.RuntimeId <= 0 || policy.InteractionObjectEntityId <= 0 || policy.InteractionEventId <= 0)
                    throw new InvalidDataException($"Entrance boundary '{policy.ProjectTransitionId}' must use positive runtime and pristine anchor IDs.");
                if (!FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.ASideArea)
                    || !FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.BSideArea))
                    throw new KeyNotFoundException($"Entrance boundary '{policy.ProjectTransitionId}' refers to an unknown project Area.");
                FogDsrFacts.Map map = facts.Maps.TryGetValue(policy.Map, out FogDsrFacts.Map foundMap)
                    ? foundMap
                    : throw new KeyNotFoundException($"Entrance boundary '{policy.ProjectTransitionId}' refers to missing pristine map '{policy.Map}'.");
                if (FogProjectAnnotationPolicy.ResolveAreaMap(policy.ASideArea) != policy.Map)
                    throw new InvalidDataException($"Entrance boundary '{policy.ProjectTransitionId}' A-side Area is not owned by its pristine interaction map.");
                FogMapId destinationMap = FogProjectAnnotationPolicy.ResolveAreaMap(policy.BSideArea);
                if (!facts.Maps.ContainsKey(destinationMap))
                    throw new KeyNotFoundException($"Entrance boundary '{policy.ProjectTransitionId}' B-side Area refers to missing pristine map '{destinationMap}'.");
                FogDsrObjectFact[] objects = map.ObjectDetails.Where(value =>
                    value.EntityId == policy.InteractionObjectEntityId
                    && string.Equals(value.Name, policy.InteractionObjectName, StringComparison.Ordinal)).ToArray();
                if (objects.Length != 1 || map.EventIds.Count(value => value == policy.InteractionEventId) != 1)
                    throw new InvalidDataException($"Entrance boundary '{policy.ProjectTransitionId}' interaction object/event does not resolve uniquely in pristine DSR.");
                if (!facts.GameItemNames.TryGetValue(policy.RequiredItem, out string itemName)
                    || !string.Equals(itemName, policy.RequiredItemEnglishName, StringComparison.Ordinal))
                    throw new InvalidDataException($"Entrance boundary '{policy.ProjectTransitionId}' required item does not resolve to its authored English DSR name.");
            }
            return Array.AsReadOnly(entries);
        }

        /// <summary>Resolves explicit empty-static-route policy against pristine boss or Entrance anchors.</summary>
        public static IReadOnlyList<FogAreaNoStaticLinksPolicy> ResolveAreaNoStaticLinksPolicies(
            FogDsrFacts facts,
            IEnumerable<FogAreaNoStaticLinksPolicy> policies = null)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            FogAreaNoStaticLinksPolicy[] entries = (policies ?? FogProjectAnnotationPolicy.AreaNoStaticLinksPolicies).ToArray();
            var seen = new HashSet<FogAreaId>();
            foreach (FogAreaNoStaticLinksPolicy policy in entries)
            {
                if (policy == null) throw new InvalidDataException("A no-static-links Area policy entry cannot be null.");
                if (!FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.Area))
                    throw new KeyNotFoundException($"No-static-links policy refers to unknown project Area '{policy.Area}'.");
                if (!seen.Add(policy.Area))
                    throw new InvalidDataException($"Duplicate no-static-links policy for Area '{policy.Area}'.");
                if (string.IsNullOrWhiteSpace(policy.Rationale))
                    throw new InvalidDataException($"No-static-links policy for Area '{policy.Area}' has no project-owned rationale.");
                if (policy.BossEventId.HasValue)
                {
                    FogBossAreaTagPolicy boss = FogProjectAnnotationPolicy.BossAreaTagPolicies.SingleOrDefault(candidate =>
                        candidate.Area == policy.Area && candidate.BossEventId == policy.BossEventId.Value);
                    if (boss == null)
                        throw new InvalidDataException($"No-static-links Area '{policy.Area}' has no matching authored boss-event anchor {policy.BossEventId.Value}.");
                    FogAnnotationPolicyResolver.ResolveBossAreaTagPolicies(facts, new[] { boss });
                }
                bool hasEntranceAnchor = policy.EntranceMap.HasValue
                    || policy.EntranceObjectEntityId.HasValue
                    || policy.EntranceTriggerRegionEntityId.HasValue
                    || policy.EntranceEventId.HasValue;
                if (hasEntranceAnchor)
                {
                    if (!policy.EntranceMap.HasValue || !policy.EntranceObjectEntityId.HasValue
                        || !policy.EntranceTriggerRegionEntityId.HasValue || !policy.EntranceEventId.HasValue)
                        throw new InvalidDataException($"No-static-links Area '{policy.Area}' has an incomplete Entrance boundary anchor.");
                    FogMapId areaMap = FogProjectAnnotationPolicy.ResolveAreaMap(policy.Area);
                    if (policy.EntranceMap.Value != areaMap || !facts.Maps.TryGetValue(policy.EntranceMap.Value, out FogDsrFacts.Map map))
                        throw new InvalidDataException($"Entrance boundary anchor for Area '{policy.Area}' is assigned to a different or unknown DSR map.");
                    if (map.ObjectDetails.Count(obj => obj.EntityId == policy.EntranceObjectEntityId.Value) != 1
                        || map.RegionDetails.Count(region => region.EntityId == policy.EntranceTriggerRegionEntityId.Value) != 1
                        || !map.EventIds.Contains(policy.EntranceEventId.Value))
                        throw new InvalidDataException($"Entrance boundary object/trigger region/event for Area '{policy.Area}' does not resolve uniquely in pristine DSR.");
                }
                else if (!policy.BossEventId.HasValue)
                    throw new InvalidDataException($"No-static-links Area '{policy.Area}' requires a pristine boss or Entrance boundary anchor.");
                if (FogProjectAnnotationPolicy.AreaLinkPolicies.Any(route => route.Source == policy.Area))
                    throw new InvalidDataException($"Area '{policy.Area}' cannot have both a no-static-links policy and a static route policy.");
            }
            return Array.AsReadOnly(entries);
        }

        /// <summary>Resolves static area-link policy against map and optional boss-event anchors.</summary>
        public static IReadOnlyList<FogAreaLink> ResolveAreaLinkPolicies(
            FogDsrFacts facts,
            IEnumerable<FogAreaLinkPolicy> policies,
            IEnumerable<FogKeyItem> resolvedKeyItems = null)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (policies == null) throw new ArgumentNullException(nameof(policies));
            const FogAreaLinkTags allowedTags = FogAreaLinkTags.Hard | FogAreaLinkTags.Shortcut | FogAreaLinkTags.Temporary;
            HashSet<FogItemName> keyItems = ValidateResolvedKeyItems(facts, resolvedKeyItems);
            var output = new List<FogAreaLink>();
            var seen = new HashSet<(FogAreaId Source, FogAreaId Destination)>();
            HashSet<FogAreaId> reviewedNoStaticLinks = FogProjectAnnotationPolicy.AreaNoStaticLinksPolicies
                .Select(policy => policy.Area).ToHashSet();
            foreach (FogAreaLinkPolicy policy in policies)
            {
                if (policy == null) throw new InvalidDataException("An Area.To policy entry cannot be null.");
                if (!FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.Source)
                    || !FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.Destination))
                    throw new KeyNotFoundException($"Static area route '{policy.Source}' -> '{policy.Destination}' refers to an unknown project area.");
                if (!seen.Add((policy.Source, policy.Destination)))
                    throw new InvalidDataException($"Duplicate static area route '{policy.Source}' -> '{policy.Destination}'.");
                if (reviewedNoStaticLinks.Contains(policy.Source))
                    throw new InvalidDataException($"Area '{policy.Source}' is reviewed as having no static Area.To links and cannot also own route '{policy.Source}' -> '{policy.Destination}'.");
                if (policy.Tags != FogAreaLinkTags.None && (policy.Tags & ~allowedTags) != 0)
                    throw new InvalidDataException($"Static area route '{policy.Source}' -> '{policy.Destination}' contains an unconsumed tag.");
                if (policy.Kind != FogAreaLinkKind.Static && policy.Kind != FogAreaLinkKind.CrossMapGlitch)
                    throw new InvalidDataException($"Static area route '{policy.Source}' -> '{policy.Destination}' has unknown route kind '{policy.Kind}'.");
                if (policy.Kind == FogAreaLinkKind.CrossMapGlitch && !policy.Tags.HasFlag(FogAreaLinkTags.Hard))
                    throw new InvalidDataException($"Cross-map glitch route '{policy.Source}' -> '{policy.Destination}' must be tagged hard.");
                FogMapId sourceMap = FogProjectAnnotationPolicy.ResolveAreaMap(policy.Source);
                FogMapId destinationMap = FogProjectAnnotationPolicy.ResolveAreaMap(policy.Destination);
                if (sourceMap != destinationMap && policy.Kind != FogAreaLinkKind.CrossMapGlitch)
                    throw new InvalidDataException($"Static area route '{policy.Source}' -> '{policy.Destination}' crosses maps; declare a project-owned CrossMapGlitch route only when gameplay evidence supports it.");
                if (sourceMap == destinationMap && policy.Kind == FogAreaLinkKind.CrossMapGlitch)
                    throw new InvalidDataException($"Cross-map glitch route '{policy.Source}' -> '{policy.Destination}' does not cross map boundaries.");
                if (!facts.Maps.ContainsKey(sourceMap) || !facts.Maps.ContainsKey(destinationMap))
                    throw new KeyNotFoundException($"Static area route '{policy.Source}' -> '{policy.Destination}' refers to a missing pristine endpoint map ('{sourceMap}' -> '{destinationMap}').");
                if (string.IsNullOrWhiteSpace(policy.Rationale))
                    throw new InvalidDataException($"Static area route '{policy.Source}' -> '{policy.Destination}' has no project-owned rationale.");
                ValidateAreaLinkCondition(policy.Source, policy.Destination, policy.Condition, facts, keyItems);
                if (policy.DestinationBossEventId.HasValue)
                {
                    FogBossAreaTagPolicy boss = FogProjectAnnotationPolicy.BossAreaTagPolicies.SingleOrDefault(candidate =>
                        candidate.Area == policy.Destination && candidate.BossEventId == policy.DestinationBossEventId.Value);
                    if (boss == null)
                        throw new InvalidDataException($"Static area route destination '{policy.Destination}' has no matching authored boss-event anchor {policy.DestinationBossEventId.Value}.");
                    FogAnnotationPolicyResolver.ResolveBossAreaTagPolicies(facts, new[] { boss });
                }
                output.Add(new FogAreaLink(policy.Destination, policy.Condition, policy.Tags));
            }
            return Array.AsReadOnly(output.ToArray());
        }

        private static HashSet<FogItemName> ValidateResolvedKeyItems(FogDsrFacts facts, IEnumerable<FogKeyItem> resolvedKeyItems)
        {
            var names = new HashSet<FogItemName>();
            foreach (FogKeyItem item in resolvedKeyItems ?? Array.Empty<FogKeyItem>())
            {
                if (item == null) throw new InvalidDataException("A resolved key-item condition inventory cannot contain null.");
                if (string.IsNullOrWhiteSpace(item.Name.Value)) throw new InvalidDataException("A resolved key-item condition inventory cannot contain an empty name.");
                if (!facts.GameItemIds.Contains(item.GameItemId))
                    throw new InvalidDataException($"Resolved key item '{item.Name}' refers to game item '{item.GameItemId}' absent from pristine GameParam.");
                if (!FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(item.DefaultArea))
                    throw new KeyNotFoundException($"Resolved key item '{item.Name}' refers to unknown default area '{item.DefaultArea}'.");
                FogMapId defaultMap = FogProjectAnnotationPolicy.ResolveAreaMap(item.DefaultArea);
                if (!facts.Maps.ContainsKey(defaultMap))
                    throw new KeyNotFoundException($"Resolved key item '{item.Name}' refers to default area '{item.DefaultArea}' on missing pristine map '{defaultMap}'.");
                if (!names.Add(item.Name))
                    throw new InvalidDataException($"Resolved key-item condition inventory contains duplicate name '{item.Name}'.");
            }
            return names;
        }

        private static void ValidateAreaLinkCondition(
            FogAreaId source,
            FogAreaId destination,
            FogCondition condition,
            FogDsrFacts facts,
            HashSet<FogItemName> keyItems)
        {
            if (condition == null) return;
            if (condition.Terms == null || condition.Terms.Count == 0)
                throw new InvalidDataException($"Static area route '{source}' -> '{destination}' has an empty condition.");
            if (condition.Kind == FogConditionKind.Atom && condition.Terms.Count != 1)
                throw new InvalidDataException($"Static area route '{source}' -> '{destination}' has an atomic condition with {condition.Terms.Count} terms.");
            if (condition.Kind != FogConditionKind.Atom && condition.Kind != FogConditionKind.All && condition.Kind != FogConditionKind.Any)
                throw new InvalidDataException($"Static area route '{source}' -> '{destination}' has an unknown condition operator '{condition.Kind}'.");

            foreach (FogConditionReference term in condition.Terms)
            {
                if (string.IsNullOrWhiteSpace(term.Value))
                    throw new InvalidDataException($"Static area route '{source}' -> '{destination}' has an empty condition reference.");
                if (term.Kind == FogConditionReferenceKind.Area)
                {
                    FogAreaId area = new FogAreaId(term.Value);
                    if (!FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(area))
                        throw new KeyNotFoundException($"Static area route '{source}' -> '{destination}' condition refers to unknown project area '{area}'.");
                    FogMapId map = FogProjectAnnotationPolicy.ResolveAreaMap(area);
                    if (!facts.Maps.ContainsKey(map))
                        throw new KeyNotFoundException($"Static area route '{source}' -> '{destination}' condition area '{area}' refers to missing pristine map '{map}'.");
                }
                else if (term.Kind == FogConditionReferenceKind.KeyItem)
                {
                    if (!keyItems.Contains(new FogItemName(term.Value)))
                        throw new KeyNotFoundException($"Static area route '{source}' -> '{destination}' condition refers to key item '{term.Value}' that is not in the resolved project key-item inventory.");
                }
                else
                {
                    throw new InvalidDataException($"Static area route '{source}' -> '{destination}' has an unknown condition reference kind '{term.Kind}'.");
                }
            }
        }

        /// <summary>Resolves project-owned optionality/cost tags against the declared pristine map owner.</summary>
        public static IReadOnlyList<FogAreaTagPolicy> ResolveAreaTagPolicies(
            FogDsrFacts facts,
            IEnumerable<FogAreaTagPolicy> policies)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (policies == null) throw new ArgumentNullException(nameof(policies));
            const FogAreaTags allowedTags = FogAreaTags.Optional | FogAreaTags.Trivial;
            var output = new List<FogAreaTagPolicy>();
            var seenAreas = new HashSet<FogAreaId>();
            foreach (FogAreaTagPolicy policy in policies)
            {
                if (policy == null) throw new InvalidDataException("An area tag policy entry cannot be null.");
                if (!seenAreas.Add(policy.Area)) throw new InvalidDataException($"Duplicate area tag policy for '{policy.Area}'.");
                if (!FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.Area))
                    throw new KeyNotFoundException($"Area tag policy refers to unknown project area '{policy.Area}'.");
                if (policy.Tags == FogAreaTags.None || (policy.Tags & ~allowedTags) != 0)
                    throw new InvalidDataException($"Area '{policy.Area}' must declare only active project optional/trivial tags.");
                FogMapId map = FogProjectAnnotationPolicy.ResolveAreaMap(policy.Area);
                if (!facts.Maps.ContainsKey(map))
                    throw new KeyNotFoundException($"Area tag policy for '{policy.Area}' resolves to missing pristine DSR map '{map}'.");
                if (string.IsNullOrWhiteSpace(policy.Rationale))
                    throw new InvalidDataException($"Area '{policy.Area}' has no project-owned classification rationale.");
                output.Add(policy);
            }
            return Array.AsReadOnly(output.ToArray());
        }

        /// <summary>Resolves project boss-cost tags against byte-verified pristine EMEVD and MSB encounter anchors.</summary>
        public static IReadOnlyList<FogAreaTagPolicy> ResolveBossAreaTagPolicies(
            FogDsrFacts facts,
            IEnumerable<FogBossAreaTagPolicy> policies)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (policies == null) throw new ArgumentNullException(nameof(policies));
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Boss area tags require EMEVD facts from a byte-verified clean DSR installation.");

            var output = new List<FogAreaTagPolicy>();
            var seenAreas = new HashSet<FogAreaId>();
            var seenEvents = new HashSet<(FogMapId Map, long EventId)>();
            foreach (FogBossAreaTagPolicy policy in policies)
            {
                if (policy == null) throw new InvalidDataException("A boss area tag policy entry cannot be null.");
                if (!seenAreas.Add(policy.Area)) throw new InvalidDataException($"Duplicate boss area tag policy for '{policy.Area}'.");
                if (!seenEvents.Add((policy.Map, policy.BossEventId)))
                    throw new InvalidDataException($"Boss event {policy.BossEventId} on '{policy.Map}' is assigned to multiple boss areas.");
                if (!FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.Area))
                    throw new KeyNotFoundException($"Boss area policy refers to unknown project area '{policy.Area}'.");
                FogMapId idMap = FogProjectAnnotationPolicy.ResolveAreaMap(policy.Area);
                if (idMap != policy.Map)
                    throw new InvalidDataException($"Boss area '{policy.Area}' resolves to map '{idMap}', not authored map '{policy.Map}'.");
                if (!facts.Maps.TryGetValue(policy.Map, out FogDsrFacts.Map map))
                    throw new KeyNotFoundException($"Boss area policy refers to unknown pristine DSR map '{policy.Map}'.");
                if (policy.EnemyPartNames == null || policy.EnemyPartNames.Count == 0
                    || policy.EnemyPartNames.Any(string.IsNullOrWhiteSpace)
                    || policy.EnemyPartNames.Distinct(StringComparer.Ordinal).Count() != policy.EnemyPartNames.Count)
                    throw new InvalidDataException($"Boss area '{policy.Area}' must declare unique pristine enemy-part anchors.");

                FogDsrBossDefeatFact[] handlers = map.BossDefeatHandlers.Where(handler => handler.EventId == policy.BossEventId).ToArray();
                if (handlers.Length != policy.EnemyPartNames.Count)
                    throw new InvalidDataException($"Boss event {policy.BossEventId} on '{policy.Map}' has {handlers.Length} pristine handlers; area '{policy.Area}' expects {policy.EnemyPartNames.Count}.");
                string[] actualEnemyParts = handlers.Select(handler =>
                {
                    FogDsrEnemyFact[] matches = map.EnemyDetails.Where(enemy => enemy.EntityId == handler.EnemyEntityId).ToArray();
                    if (matches.Length != 1)
                        throw new InvalidDataException($"Boss event {policy.BossEventId} on '{policy.Map}' resolves to {matches.Length} MSB enemy parts for entity {handler.EnemyEntityId}.");
                    return matches[0].Name;
                }).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                string[] expectedEnemyParts = policy.EnemyPartNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();
                if (!actualEnemyParts.SequenceEqual(expectedEnemyParts, StringComparer.Ordinal))
                    throw new InvalidDataException($"Boss event {policy.BossEventId} on '{policy.Map}' targets [{string.Join(", ", actualEnemyParts)}], not the authored anchors [{string.Join(", ", expectedEnemyParts)}].");

                if (string.IsNullOrWhiteSpace(policy.Rationale))
                    throw new InvalidDataException($"Boss area '{policy.Area}' has no project-owned balancing rationale.");
                output.Add(new FogAreaTagPolicy(policy.Area, FogAreaTags.Boss, policy.Rationale));
            }
            return Array.AsReadOnly(output.ToArray());
        }

        /// <summary>
        /// Resolves boss flags from pristine EMEVD and MSB anchors. The flag is the event ID of the
        /// unique HandleBossDefeat call targeting an enemy on the authored collision, provided
        /// common event 0 passes that ID to event 970 and event 970 reads it as an event flag.
        /// </summary>
        public static IReadOnlyList<FogDefaultFlagCollision> ResolveBossDefeatFlagCollisions(
            FogDsrFacts facts,
            IEnumerable<FogBossDefeatFlagCollisionPolicy> policies)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (policies == null) throw new ArgumentNullException(nameof(policies));
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Boss-defeat flag associations require EMEVD facts from a clean DSR game directory.");

            var output = new List<FogDefaultFlagCollision>();
            var seen = new HashSet<(FogMapId Map, string Collision)>();
            foreach (FogBossDefeatFlagCollisionPolicy policy in policies)
            {
                if (policy == null) throw new InvalidDataException("A boss-defeat flag policy entry cannot be null.");
                if (!seen.Add((policy.Map, policy.Collision)))
                    throw new InvalidDataException($"Duplicate boss-defeat flag policy for '{policy.Map}/{policy.Collision}'.");
                if (!facts.Maps.TryGetValue(policy.Map, out FogDsrFacts.Map map))
                    throw new KeyNotFoundException($"Boss-defeat flag policy refers to unknown DSR map '{policy.Map}'.");
                if (!map.Collisions.Contains(policy.Collision))
                    throw new KeyNotFoundException($"Boss-defeat flag collision '{policy.Collision}' is absent from DSR map '{policy.Map}'.");

                HashSet<int> enemyEntityIds = map.EnemyDetails
                    .Where(enemy => string.Equals(enemy.CollisionName, policy.Collision, StringComparison.Ordinal) && enemy.EntityId > 0)
                    .Select(enemy => enemy.EntityId)
                    .ToHashSet();
                int[] matchingFlags = map.BossDefeatHandlers
                    .Where(handler => enemyEntityIds.Contains(handler.EnemyEntityId)
                        && handler.EventId > 0 && handler.EventId <= int.MaxValue
                        && facts.BossRewardCompletionFlags.Contains((int)handler.EventId))
                    .Select(handler => (int)handler.EventId)
                    .Distinct()
                    .ToArray();
                if (matchingFlags.Length != 1)
                    throw new InvalidDataException($"Expected one boss completion flag for '{policy.Map}/{policy.Collision}' from pristine EMEVD; found {matchingFlags.Length}.");
                output.Add(new FogDefaultFlagCollision(policy.Map, policy.Collision, matchingFlags[0]));
            }
            return Array.AsReadOnly(output.ToArray());
        }

        /// <summary>Resolves authored key-item policy in input order against pristine DSR facts.</summary>
        public static IReadOnlyList<FogKeyItem> ResolveKeyItems(FogDsrFacts facts, IEnumerable<FogKeyItemPolicy> policies)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (policies == null) throw new ArgumentNullException(nameof(policies));

            var resolved = new List<FogKeyItem>();
            foreach (FogKeyItemPolicy policy in policies)
            {
                if (policy == null) throw new InvalidDataException("A key-item policy entry cannot be null.");
                if (string.IsNullOrWhiteSpace(policy.Name.Value)) throw new InvalidDataException("A key-item policy name cannot be empty.");
                if (string.IsNullOrWhiteSpace(policy.EnglishGameName)) throw new InvalidDataException($"Key item '{policy.Name}' has no English DSR game name.");

                FogGameItemId item = facts.RequireUniqueGameItem(policy.Category, policy.EnglishGameName);
                if (!facts.GameItemIds.Contains(item))
                    throw new InvalidDataException($"Key item '{policy.Name}' resolves to {item}, which is absent from the pristine DSR GameParam item table.");
                resolved.Add(new FogKeyItem(policy.Name, item, policy.DefaultArea, policy.Tags));
            }

            return Array.AsReadOnly(resolved.ToArray());
        }

        /// <summary>Resolves custom starts by the named DSR spawn-point region, preserving policy order.</summary>
        public static IReadOnlyList<FogCustomStart> ResolveCustomStarts(FogDsrFacts facts, IEnumerable<FogCustomStartPolicy> policies)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (policies == null) throw new ArgumentNullException(nameof(policies));

            var resolved = new List<FogCustomStart>();
            foreach (FogCustomStartPolicy policy in policies)
            {
                if (policy == null) throw new InvalidDataException("A custom-start policy entry cannot be null.");
                if (string.IsNullOrWhiteSpace(policy.Id)) throw new InvalidDataException("A custom-start policy ID cannot be empty.");
                if (string.IsNullOrWhiteSpace(policy.DisplayName)) throw new InvalidDataException($"Custom start '{policy.Id}' has no display name.");
                if (string.IsNullOrWhiteSpace(policy.SpawnPointRegionName))
                    throw new InvalidDataException($"Custom start '{policy.Id}' has no DSR spawn-point region name.");
                if (!facts.Maps.TryGetValue(policy.Map, out FogDsrFacts.Map map))
                    throw new KeyNotFoundException($"Custom start '{policy.Id}' refers to unknown DSR map '{policy.Map}'.");

                int[] matches = map.SpawnPoints
                    .Where(entry => string.Equals(entry.Value, policy.SpawnPointRegionName, StringComparison.Ordinal))
                    .Select(entry => entry.Key)
                    .OrderBy(id => id)
                    .ToArray();
                if (matches.Length == 0)
                    throw new KeyNotFoundException($"Custom start '{policy.Id}' has no spawn point named '{policy.SpawnPointRegionName}' on map '{policy.Map}'.");
                if (matches.Length > 1)
                    throw new InvalidDataException($"Custom start '{policy.Id}' spawn point '{policy.SpawnPointRegionName}' is ambiguous on map '{policy.Map}' ({matches.Length} entity IDs).");

                resolved.Add(new FogCustomStart(policy.Id, policy.DisplayName, policy.Area, new FogRespawn(policy.Map, new FogSpawnPointId(matches[0])), policy.Semantics));
            }

            return Array.AsReadOnly(resolved.ToArray());
        }

        /// <summary>
        /// Resolves authored enemy-scaling area policy against pristine MSB enemy/collision names.
        /// Missing collision assignments are reported explicitly; a map-wide default covers the
        /// map's otherwise-unassigned collision groups just as it does in the pinned writer.
        /// </summary>
        public static FogEnemyScalingResolution ResolveEnemyScalingRules(
            FogDsrFacts facts,
            IEnumerable<FogEnemyCollisionAreaPolicy> policies)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (policies == null) throw new ArgumentNullException(nameof(policies));

            var resolved = new List<FogEnemyScalingRule>();
            var rulesByCollision = new Dictionary<(FogMapId Map, string Collision), List<FogEnemyScalingRule>>();
            var defaults = new HashSet<FogMapId>();
            var includedNames = new HashSet<(FogMapId Map, string Collision, string Enemy)>();
            foreach (FogEnemyCollisionAreaPolicy policy in policies)
            {
                if (policy == null) throw new InvalidDataException("An enemy-scaling policy entry cannot be null.");
                if (!facts.Maps.TryGetValue(policy.Map, out FogDsrFacts.Map map))
                    throw new KeyNotFoundException($"Enemy-scaling policy refers to unknown DSR map '{policy.Map}'.");

                string collision = policy.Collision ?? string.Empty;
                if (collision.Length > 0 && !map.Collisions.Contains(collision))
                    throw new KeyNotFoundException($"Enemy-scaling collision '{collision}' is absent from DSR map '{policy.Map}'.");
                IReadOnlyList<string> authoredIncludes = policy.IncludedEnemyNames ?? Array.Empty<string>();
                if (collision.Length == 0 && authoredIncludes.Count > 0)
                    throw new InvalidDataException($"A map-wide enemy-scaling default for '{policy.Map}' cannot target individual enemies.");
                if (collision.Length == 0 && !defaults.Add(policy.Map))
                    throw new InvalidDataException($"Duplicate map-wide enemy-scaling default for '{policy.Map}'.");

                var includes = new List<string>();
                foreach (string enemyName in authoredIncludes)
                {
                    if (string.IsNullOrWhiteSpace(enemyName))
                        throw new InvalidDataException($"An enemy-scaling include for '{policy.Map} {collision}' cannot be empty.");
                    string partName = enemyName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (partName == null || !map.Enemies.Contains(partName))
                        throw new KeyNotFoundException($"Enemy-scaling include '{enemyName}' does not name an enemy part in DSR map '{policy.Map}'.");
                    if (!map.EnemyDetails.Any(enemy => string.Equals(enemy.Name, partName, StringComparison.Ordinal)
                            && string.Equals(enemy.CollisionName, collision, StringComparison.Ordinal)))
                        throw new InvalidDataException($"Enemy-scaling include '{enemyName}' is not in collision '{collision}' on DSR map '{policy.Map}'.");
                    if (!includedNames.Add((policy.Map, collision, partName)))
                        throw new InvalidDataException($"Enemy part '{partName}' is included more than once for DSR map '{policy.Map}', collision '{collision}'.");
                    includes.Add(enemyName);
                }

                var rule = new FogEnemyScalingRule(policy.Map, collision, policy.Area,
                    includes.Count == 0 ? null : Array.AsReadOnly(includes.ToArray()));
                resolved.Add(rule);
                if (!rulesByCollision.TryGetValue((policy.Map, collision), out List<FogEnemyScalingRule> group))
                {
                    group = new List<FogEnemyScalingRule>();
                    rulesByCollision.Add((policy.Map, collision), group);
                }
                group.Add(rule);
            }

            for (int i = 0; i < resolved.Count; i++)
            {
                FogEnemyScalingRule rule = resolved[i];
                if (rulesByCollision[(rule.Map, rule.Collision)].Count <= 1 || rule.Includes != null) continue;
                FogEnemyScalingRule normalized = rule with { Includes = Array.Empty<string>() };
                resolved[i] = normalized;
                List<FogEnemyScalingRule> group = rulesByCollision[(rule.Map, rule.Collision)];
                int groupIndex = group.FindIndex(candidate => ReferenceEquals(candidate, rule));
                if (groupIndex < 0) throw new InvalidDataException("An enemy-scaling policy group lost its authored rule order.");
                group[groupIndex] = normalized;
            }

            foreach (KeyValuePair<(FogMapId Map, string Collision), List<FogEnemyScalingRule>> entry in rulesByCollision)
                if (entry.Value.Count == 1 && entry.Value[0].Includes != null)
                    throw new InvalidDataException($"Enemy includes for DSR map '{entry.Key.Map}', collision '{entry.Key.Collision}' have no effect unless the collision has multiple area rules.");

            var unresolved = new List<FogEnemyCollisionGroup>();
            foreach (KeyValuePair<FogMapId, FogDsrFacts.Map> mapEntry in facts.Maps.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal))
            {
                foreach (IGrouping<string, FogDsrEnemyFact> collisionGroup in mapEntry.Value.EnemyDetails
                    .Where(enemy => !string.IsNullOrWhiteSpace(enemy.CollisionName))
                    .GroupBy(enemy => enemy.CollisionName, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    if (!rulesByCollision.ContainsKey((mapEntry.Key, collisionGroup.Key)) && !defaults.Contains(mapEntry.Key))
                        unresolved.Add(new FogEnemyCollisionGroup(mapEntry.Key, collisionGroup.Key,
                            Array.AsReadOnly(collisionGroup.ToArray())));
                }
            }

            return new FogEnemyScalingResolution(Array.AsReadOnly(resolved.ToArray()), Array.AsReadOnly(unresolved.ToArray()));
        }

        /// <summary>
        /// Resolves located slot-1 ItemLot references from authored map/collision area rules. Missing
        /// rules are returned explicitly so an incomplete policy cannot be mistaken for a full catalog.
        /// </summary>
        public static FogTreasureLotResolution ResolveTreasureLotLocations(
            FogDsrFacts facts,
            IEnumerable<FogTreasureCollisionAreaPolicy> policies)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (policies == null) throw new ArgumentNullException(nameof(policies));

            var areaByCollision = new Dictionary<(FogMapId Map, string Collision), FogAreaId>();
            var orderedRules = new List<(FogMapId Map, string Collision)>();
            foreach (FogTreasureCollisionAreaPolicy policy in policies)
            {
                if (policy == null) throw new InvalidDataException("A treasure collision policy entry cannot be null.");
                if (string.IsNullOrWhiteSpace(policy.Collision)) throw new InvalidDataException("A treasure collision policy must name a DSR collision.");
                var key = (policy.Map, policy.Collision);
                if (!areaByCollision.TryAdd(key, policy.Area))
                    throw new InvalidDataException($"Duplicate treasure collision policy for map '{policy.Map}', collision '{policy.Collision}'.");
                orderedRules.Add(key);
            }

            var areasByLot = new Dictionary<int, FogAreaId>();
            var unresolved = new List<FogDsrTreasureLotFact>();
            foreach (FogDsrTreasureLotFact source in facts.FunctionalTreasureLots)
            {
                if (!facts.ItemLotIds.Contains(source.ItemLotId)) continue;
                if (string.IsNullOrWhiteSpace(source.PartCollisionName)
                    || !areaByCollision.TryGetValue((source.Map, source.PartCollisionName), out FogAreaId area))
                {
                    unresolved.Add(source);
                    continue;
                }

                if (areasByLot.TryGetValue(source.ItemLotId, out FogAreaId previousArea) && previousArea != area)
                    throw new InvalidDataException($"Pristine ItemLot {source.ItemLotId} is referenced from multiple map/collision groups assigned to different areas ('{previousArea}' and '{area}').");
                areasByLot[source.ItemLotId] = area;
            }

            var locations = new List<FogLotLocation>();
            var emitted = new HashSet<int>();
            foreach ((FogMapId map, string collision) in orderedRules)
                foreach (FogDsrTreasureLotFact source in facts.FunctionalTreasureLots)
                    if (source.Map == map && string.Equals(source.PartCollisionName, collision, StringComparison.Ordinal)
                        && areasByLot.TryGetValue(source.ItemLotId, out FogAreaId area) && emitted.Add(source.ItemLotId))
                        locations.Add(new FogLotLocation(new FogItemLotId(source.ItemLotId), area));

            return new FogTreasureLotResolution(Array.AsReadOnly(locations.ToArray()), Array.AsReadOnly(unresolved.ToArray()));
        }
    }
}
