using System;
using System.Collections.Generic;
using System.Globalization;
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

    public sealed record FogKeyItemInteractionAnchor(FogMapId Map, long EventId, string Mechanic);

    /// <summary>Identity-only item policy, kept separate until its logical Area and Fog tags are authored.</summary>
    public sealed record FogKeyItemIdentityPolicy(
        FogItemName Name,
        FogGameItemCategory Category,
        int GameParamId,
        string EnglishGameName,
        IReadOnlyList<FogKeyItemInteractionAnchor> InteractionAnchors,
        string Rationale);

    public sealed record FogKeyItemIdentity(
        FogItemName Name,
        FogGameItemId GameItemId,
        string EnglishGameName,
        IReadOnlyList<FogKeyItemInteractionAnchor> InteractionAnchors,
        string Rationale);

    /// <summary>Project-owned progression intent for one verified key-item identity.</summary>
    public sealed record FogKeyItemProjectPolicyV1(
        FogItemName Name,
        FogAreaId DefaultArea,
        FogKeyItemTags Tags,
        string ControlledMechanic,
        string Rationale);

    /// <summary>Authored start policy uses the named spawn region, not a copied entity ID.</summary>
    public sealed record FogCustomStartPolicy(
        string Id,
        string DisplayName,
        FogAreaId Area,
        FogMapId Map,
        string SpawnPointRegionName,
        FogCustomStartSemantics Semantics = FogCustomStartSemantics.None);

    public sealed record FogAreaTagPolicy(FogAreaId Area, FogAreaTags Tags, string Rationale);

    public enum FogProjectAreaAnchorKind { MapMembership, BossEncounter, CustomStart, EntranceBoundary, Warp, StaticLink }
    public enum FogProjectAreaRouteDisposition
    {
        StaticLinksAuthored,
        EntranceOwned,
        WarpOwned,
        ExplicitNoStaticLink,
    }

    public sealed record FogProjectAreaEvidenceAnchor(FogProjectAreaAnchorKind Kind, FogMapId Map, string Reference);

    /// <summary>One authored V1 logical Area record. Empty StaticLinks means no Area.To edge has been authored for this node yet.</summary>
    public sealed record FogProjectAreaPolicyV1(
        FogAreaId Id,
        FogMapId OwnerMap,
        FogAreaTags Tags,
        IReadOnlyList<FogAreaLinkPolicy> StaticLinks,
        FogProjectAreaRouteDisposition RouteDisposition,
        IReadOnlyList<FogProjectAreaEvidenceAnchor> Anchors,
        string Rationale);

    /// <summary>Project rule that resolves an area ID namespace to its owning DSR map.</summary>
    public sealed record FogAreaMapMembershipPolicy(string Prefix, FogMapId Map, string Rationale);

    /// <summary>Classification used while validating project-owned static graph links.</summary>
    public enum FogAreaLinkKind { Static, CrossMapGlitch }

    /// <summary>Project-owned static graph route, separate from Entrance and Warp transitions.</summary>
    public sealed record FogAreaLinkPolicy(
        FogAreaId Source,
        FogAreaId Destination,
        FogCondition Condition,
        FogAreaLinkTags Tags,
        string Rationale,
        long? DestinationBossEventId = null,
        FogAreaLinkKind Kind = FogAreaLinkKind.Static,
        string ConnectCollisionPartName = null);

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

    public enum FogProjectEntranceKind { FixedKeyDoor, BossBoundary, TraversableFog, LordvesselGate }

    /// <summary>Logical transition semantics authored by DS1TR and anchored to pristine DSR data.</summary>
    public sealed record FogProjectEntrancePolicyV1(
        string Id,
        FogProjectEntranceKind Kind,
        FogMapId Map,
        FogAreaId SourceArea,
        FogAreaId DestinationArea,
        string ObjectPartName,
        int ObjectEntityId,
        int? ActionRegionEntityId,
        long? InteractionEventId,
        FogItemName? RequiredItemName,
        FogGameItemId? RequiredItem,
        FogEntranceTags Tags,
        string Rationale,
        int? ASideActionRegionEntityId = null,
        int? BSideActionRegionEntityId = null,
        int? BossTriggerRegionEntityId = null,
        long? RequiredItemCheckEventId = null,
        bool BossAreaOnASide = false,
        int? ASideEntryFlag = null,
        int? BSideEntryFlag = null,
        FogCondition ASideCondition = null,
        FogCondition BSideCondition = null)
    {
        public FogEntrance ToFogEntrance(
            string resolvedObjectPartName,
            FogTriggerVolume bossTriggerArea = null,
            int bossTriggerEventId = 0)
        {
            if (string.IsNullOrWhiteSpace(resolvedObjectPartName))
                throw new ArgumentException("A resolved pristine object part name is required.", nameof(resolvedObjectPartName));

            FogCondition doorCondition = RequiredItemName.HasValue
                ? new FogCondition(FogConditionKind.Atom, Array.AsReadOnly(new[]
                {
                    FogConditionReference.KeyItem(RequiredItemName.Value),
                }))
                : null;
            FogAreaId aSideArea = BossAreaOnASide ? DestinationArea : SourceArea;
            FogAreaId bSideArea = BossAreaOnASide ? SourceArea : DestinationArea;
            FogMapId aSideMap = FogProjectAnnotationPolicy.ResolveAreaMap(aSideArea);
            FogMapId bSideMap = FogProjectAnnotationPolicy.ResolveAreaMap(bSideArea);
            bool aSideIsBoss = BossTriggerRegionEntityId.HasValue && BossAreaOnASide;
            bool bSideIsBoss = BossTriggerRegionEntityId.HasValue && !BossAreaOnASide;
            int aSideActionRegion = ASideActionRegionEntityId
                ?? (aSideMap == Map ? ActionRegionEntityId : null)
                ?? 0;
            int bSideActionRegion = BSideActionRegionEntityId
                ?? (bSideMap == Map ? ActionRegionEntityId : null)
                ?? 0;
            FogEntranceSide Side(
                FogAreaId area,
                FogMapId sideMap,
                int actionRegion,
                bool bossSide,
                int? entryFlag,
                FogCondition condition) => new FogEntranceSide(
                    area, 0, 0, entryFlag ?? 0, 0,
                    bossSide ? bossTriggerEventId : 0,
                    bossSide ? bossTriggerArea : null,
                    0, condition, null, 0, actionRegion, null,
                    sideMap == Map ? null : sideMap,
                    FogEntranceSideTags.None);

            return new FogEntrance(
                resolvedObjectPartName,
                ObjectEntityId,
                Map,
                Id,
                doorCondition,
                Tags,
                Side(aSideArea, aSideMap, aSideActionRegion, aSideIsBoss, ASideEntryFlag, ASideCondition),
                Side(bSideArea, bSideMap, bSideActionRegion, bSideIsBoss, BSideEntryFlag, BSideCondition));
        }
    }

    public sealed record FogProjectEntranceResolution(
        FogProjectEntrancePolicyV1 Policy,
        string ResolvedObjectPartName,
        bool ObjectAnchorResolved,
        bool RegionAnchorResolved,
        bool EventAnchorResolved,
        bool ConditionAnchorResolved,
        FogTriggerVolume BossTriggerArea = null,
        int BossTriggerEventId = 0);

    /// <summary>One directed player world relocation, anchored to its pristine source event and destination region.</summary>
    public sealed record FogProjectWarpPolicyV1(
        string Id,
        FogMapId SourceMap,
        FogAreaId SourceArea,
        long SourceEventId,
        int CutsceneId,
        int PlaybackType,
        FogMapId DestinationMap,
        FogAreaId DestinationArea,
        int DestinationRegionEntityId,
        string Rationale);

    /// <summary>A project-added one-way Warp activated by a reconstructed event flag and a writer-remapped region.</summary>
    public sealed record FogProjectFlagWarpPolicyV1(
        string Id,
        FogMapId Map,
        FogAreaId SourceArea,
        FogAreaId DestinationArea,
        long ActivationEventId,
        int ActivationObjectEntityId,
        int ActivationFlagId,
        string DestinationRegionName,
        int PristineDestinationRegionEntityId,
        int RuntimeDestinationRegionEntityId,
        FogDsrTransform DestinationPosition,
        string Rationale);

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
    public enum FogBossFlagAnchorKind
    {
        CollisionBossEnemy,
        EncodedNegativePlayRegion,
        Both,
    }

    /// <summary>
    /// Project-owned association between a DSR boss collision and its completion event. The
    /// expected event ID and anchor kind are authored explicitly, then checked against pristine
    /// MSB/EMEVD and common event 970 facts.
    /// </summary>
    public sealed record FogBossDefeatFlagCollisionPolicy(
        FogMapId Map,
        string Collision,
        int BossCompletionEventId,
        FogBossFlagAnchorKind AnchorKind,
        string Rationale);

    /// <summary>Logical placement for a common character-death award anchored to a pristine MSB enemy.</summary>
    public sealed record FogCharacterDeathRewardAreaPolicy(
        FogMapId Map,
        long EventId,
        int RequiredEventFlagId,
        int CharacterEntityId,
        int ItemLotId,
        FogAreaId Area,
        string Rationale);

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
        public const int SeathPrisonWarpRuntimeId = 1702901;
        public const string AreaScalingBasePolicyRationale =
            "Project policy leaves ScalingBase unset for production. The shipped Fog option contract exposes neither scalingbase nor dumpdist, and FogMod only reads ScalingBase when one of those debug flags is enabled.";

        /// <summary>
        /// Identities observed in resolved pristine DSR IfPlayerHasItem instructions. This list
        /// deliberately records item identity and interaction anchors only; it does not claim
        /// that every interaction is required progression or assign an Area/RandomOnly/Hard tag.
        /// </summary>
        public static IReadOnlyList<FogKeyItemIdentityPolicy> KeyItemIdentityPolicies { get; } = Array.AsReadOnly(new[]
        {
            KeyIdentity("eye_of_death_goods_109", "Eye of Death", 109, ("catacombs", 11305000)),
            KeyIdentity("black_eye_orb_goods_115", "Black Eye Orb", 115, ("anorlondo", 11510150)),
            KeyIdentity("purple_cowards_crystal_goods_118", "Purple Coward's Crystal", 118, ("dlc", 11219113), ("dlc", 11219114)),
            KeyIdentity("peculiar_doll_goods_384", "Peculiar Doll", 384, ("anorlondo", 11510230)),
            KeyIdentity("soul_of_artorias_goods_710", "Soul of Artorias", 710, ("dlc", 11210531)),
            KeyIdentity("crest_of_artorias_goods_2002", "Crest of Artorias", 2002, ("darkroot", 11200100), ("darkroot", 11200110)),
            KeyIdentity("cage_key_goods_2003", "Cage Key", 2003, ("sens", 11500102), ("sens", 11500103), ("sens", 11500110)),
            KeyIdentity("archive_tower_giant_door_key_goods_2005", "Archive Tower Giant Door Key", 2005, ("dukes", 11700140), ("dukes", 11700141)),
            KeyIdentity("blighttown_key_goods_2007", "Blighttown Key", 2007, ("depths", 11000110), ("depths", 11000111)),
            KeyIdentity("key_to_new_londo_ruins_goods_2008", "Key to New Londo Ruins", 2008, ("newlondo", 11600120)),
            KeyIdentity("key_to_the_seal_goods_2013", "Key to the Seal", 2013, ("newlondo", 11600900)),
            KeyIdentity("lift_chamber_key_goods_2015", "Lift Chamber Key", 2015, ("firelink", 11020120)),
            KeyIdentity("mystery_key_goods_2017", "Mystery Key", 2017, ("parish", 11010190)),
            KeyIdentity("sewer_chamber_key_goods_2018", "Sewer Chamber Key", 2018, ("depths", 11000120)),
            KeyIdentity("watchtower_basement_key_goods_2019", "Watchtower Basement Key", 2019, ("parish", 11010190)),
            KeyIdentity("residence_key_goods_2021", "Residence Key", 2021, ("parish", 11010140)),
            KeyIdentity("lord_soul_goods_2500", "Lord Soul", 2500, ("kiln", 11800200)),
            KeyIdentity("lord_soul_goods_2501", "Lord Soul", 2501, ("kiln", 11800200)),
            KeyIdentity("bequeathed_lord_soul_shard_goods_2502", "Bequeathed Lord Soul Shard", 2502, ("kiln", 11800200)),
            KeyIdentity("bequeathed_lord_soul_shard_goods_2503", "Bequeathed Lord Soul Shard", 2503, ("kiln", 11800200)),
            KeyIdentity("lordvessel_goods_2510", "Lordvessel", 2510, ("anorlondo", 11510400), ("kiln", 11800100), ("kiln", 11800101)),
            KeyIdentity("broken_pendant_goods_2520", "Broken Pendant", 2520, ("darkroot", 11200005), ("darkroot", 11200006), ("darkroot", 11200529), ("darkroot", 11200533)),
        });

        /// <summary>
        /// Explicit Compact V1 progression policy for every verified key-item identity. Required
        /// means the item participates in completing the project progression graph; RandomOnly
        /// means its optional mechanic can use randomized placement without becoming a reachability
        /// requirement. No item is Hard-only in V1.
        /// </summary>
        public static IReadOnlyList<FogKeyItemProjectPolicyV1> KeyItemProjectPoliciesV1 { get; } = Array.AsReadOnly(new[]
        {
            KeyProject("eye_of_death_goods_109", "catacombs", FogKeyItemTags.RandomOnly, "Optional Gravelord covenant use.", "The Eye of Death supports an optional covenant mechanic and is not needed to finish the main route."),
            KeyProject("black_eye_orb_goods_115", "anorlondo_main", FogKeyItemTags.RandomOnly, "Optional invasion of Lautrec.", "The Black Eye Orb enables an optional invasion encounter; it is not a completion gate."),
            KeyProject("purple_cowards_crystal_goods_118", "dlc_township", FogKeyItemTags.RandomOnly, "Optional multiplayer return item.", "The Purple Coward's Crystal is an optional multiplayer utility item."),
            KeyProject("peculiar_doll_goods_384", "anorlondo_start", FogKeyItemTags.RandomOnly, "Optional Painted World access.", "The Peculiar Doll opens an optional area and is not required for the main completion route."),
            KeyProject("soul_of_artorias_goods_710", "dlc_artorias", FogKeyItemTags.RandomOnly, "Optional DLC reward interaction.", "Soul of Artorias is an optional DLC reward interaction, not a main progression requirement."),
            KeyProject("crest_of_artorias_goods_2002", "darkroot", FogKeyItemTags.None, "Crest-gated Darkroot forest passage.", "The validated Darkroot gate policy tracks this item as required for project completion closure and binds it to the Crest Entrance."),
            KeyProject("cage_key_goods_2003", "sens_cage", FogKeyItemTags.RandomOnly, "Optional Sen's Fortress cage shortcut.", "The Cage Key opens an optional shortcut and is not a completion requirement."),
            KeyProject("archive_tower_giant_door_key_goods_2005", "dukes_prison", FogKeyItemTags.None, "Archives prison escape door.", "The Archives Tower Giant Door Key controls the required prison escape route after the first Seath encounter."),
            KeyProject("blighttown_key_goods_2007", "depths", FogKeyItemTags.None, "Fixed Depths-to-Blighttown key door.", "The project models the validated Blighttown key door directly; the item is retained in the required progression pool."),
            KeyProject("key_to_new_londo_ruins_goods_2008", "newlondo", FogKeyItemTags.RandomOnly, "Optional New Londo gate.", "The Key to New Londo Ruins opens an optional early passage; the required Seal gate is tracked separately."),
            KeyProject("key_to_the_seal_goods_2013", "newlondo_building", FogKeyItemTags.None, "New Londo Seal gate.", "The key is checked by pristine event 11600900 and is placed on the accessible building side before the validated lower-New-Londo fog transition."),
            KeyProject("lift_chamber_key_goods_2015", "firelink_elevator", FogKeyItemTags.RandomOnly, "Optional Firelink lift chamber.", "The Lift Chamber Key opens an optional access chamber and is not required for completion."),
            KeyProject("mystery_key_goods_2017", "parish_church", FogKeyItemTags.RandomOnly, "Optional Parish cell door.", "The Mystery Key supports an optional rescue/door interaction, not main progression."),
            KeyProject("sewer_chamber_key_goods_2018", "depths", FogKeyItemTags.RandomOnly, "Optional Depths sewer door.", "The Sewer Chamber Key opens an optional Depths door and is not a completion requirement."),
            KeyProject("watchtower_basement_key_goods_2019", "burg_upper", FogKeyItemTags.RandomOnly, "Optional Undead Burg watchtower access.", "The Watchtower Basement Key opens an optional route and is not a completion requirement."),
            KeyProject("residence_key_goods_2021", "burg_lower", FogKeyItemTags.RandomOnly, "Optional residence doors.", "The Residence Key opens optional doors and is not a completion requirement."),
            KeyProject("lord_soul_goods_2500", "totg_nito", FogKeyItemTags.None, "Lord Soul requirement for the Kiln seal.", "Project V1 assigns this generic-named verified Lord Soul identity to Nito's completion branch; all four Lord Soul identities are required by the Kiln progression rule."),
            KeyProject("lord_soul_goods_2501", "dukes_seath2", FogKeyItemTags.None, "Lord Soul requirement for the Kiln seal.", "Project V1 assigns this generic-named verified Lord Soul identity to Seath's completion branch; all four Lord Soul identities are required by the Kiln progression rule."),
            KeyProject("bequeathed_lord_soul_shard_goods_2502", "demonruins_bedofchaos", FogKeyItemTags.None, "Lord Soul requirement for the Kiln seal.", "Project V1 assigns this verified Lord Soul Shard identity to the Bed of Chaos branch; all four Lord Soul identities are required by the Kiln progression rule."),
            KeyProject("bequeathed_lord_soul_shard_goods_2503", "newlondo_fourkings", FogKeyItemTags.None, "Lord Soul requirement for the Kiln seal.", "Project V1 assigns this verified Lord Soul Shard identity to the Four Kings branch; all four Lord Soul identities are required by the Kiln progression rule."),
            KeyProject("lordvessel_goods_2510", "anorlondo_start", FogKeyItemTags.None, "Lordvessel golden gates and Kiln access.", "The Lordvessel is a required milestone for the three golden seal gates and for entering the Kiln progression route."),
            KeyProject("broken_pendant_goods_2520", "darkroot_forest", FogKeyItemTags.RandomOnly, "Optional DLC access.", "The Broken Pendant opens the optional DLC route; the base-game completion graph does not require it."),
        });

        private static FogKeyItemProjectPolicyV1 KeyProject(
            string name,
            string area,
            FogKeyItemTags tags,
            string controlledMechanic,
            string rationale) => new FogKeyItemProjectPolicyV1(
                new FogItemName(name), new FogAreaId(area), tags, controlledMechanic, rationale);

        private static FogKeyItemIdentityPolicy KeyIdentity(
            string name,
            string englishName,
            int gameParamId,
            params (string Map, long EventId)[] eventAnchors)
        {
            FogKeyItemInteractionAnchor[] anchors = eventAnchors.Select(anchor => new FogKeyItemInteractionAnchor(
                new FogMapId(anchor.Map), anchor.EventId,
                $"Pristine map EMEVD event {anchor.EventId} contains a resolved IfPlayerHasItem instruction for Goods 3:{gameParamId}."))
                .ToArray();
            return new FogKeyItemIdentityPolicy(
                new FogItemName(name), FogGameItemCategory.Goods, gameParamId, englishName,
                Array.AsReadOnly(anchors),
                $"The DSR GameParam item ID and English FMG name identify this Goods item; the listed pristine events independently check its possession. This establishes item identity, not required progression or a Fog placement/tag decision.");
        }

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
            // FogMod's fixed golden-gate rule refers to `kiln_start` as the Lordvessel
            // progression milestone. Compact V1 assigns this project-owned marker to the
            // accessible Firelink altar; the physical Kiln remains `kiln` behind the four souls.
            if (area.Value == "kiln_start") return new FogMapId("firelink");
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

        /// <summary>
        /// Project rule: encode a completion event into low-ID boss arena collisions when pristine
        /// DSR independently ties the collision to that event. Some maps encode the event as
        /// -PlayRegionID - 10; others expose a boss entity in the collision; a few have both.
        /// Membership and event IDs are authored here, independently of FogMod annotation data.
        /// </summary>
        public static IReadOnlyList<FogBossDefeatFlagCollisionPolicy> DefaultFlagCollisionPolicies { get; } = Array.AsReadOnly(new[]
        {
            BossFlagFamily("anorlondo", 11510900, FogBossFlagAnchorKind.EncodedNegativePlayRegion, "h0031B1_0000", "h0032B1_0000"),
            BossFlagFamily("anorlondo", 11510900, FogBossFlagAnchorKind.CollisionBossEnemy, "h0031B1_0001"),
            BossFlagFamily("asylum", 11810900, FogBossFlagAnchorKind.Both, "h0016B1"),
            BossFlagFamily("darkroot", 11200900, FogBossFlagAnchorKind.EncodedNegativePlayRegion, "h0060B0"),
            BossFlagFamily("darkroot", 11200900, FogBossFlagAnchorKind.CollisionBossEnemy, "h0062B0"),
            BossFlagFamily("demonruins", 11410900, FogBossFlagAnchorKind.EncodedNegativePlayRegion, "h0005B1", "h0006B1", "h0600B1"),
            BossFlagFamily("demonruins", 11410900, FogBossFlagAnchorKind.CollisionBossEnemy, "h0000B1"),
            BossFlagFamily("demonruins", 11410410, FogBossFlagAnchorKind.Both, "h0018B1"),
            BossFlagFamily("demonruins", 11410901, FogBossFlagAnchorKind.Both, "h0062B1"),
            BossFlagFamily("demonruins", 11410901, FogBossFlagAnchorKind.EncodedNegativePlayRegion, "h6200B1", "h6201B1"),
            BossFlagFamily("dlc", 11210000, FogBossFlagAnchorKind.Both, "h0036B1"),
            BossFlagFamily("dlc", 11210000, FogBossFlagAnchorKind.EncodedNegativePlayRegion, "h3600B1", "h3601B1", "h3602B1"),
            BossFlagFamily("dlc", 11210001, FogBossFlagAnchorKind.Both, "h0095B1"),
            BossFlagFamily("parish", 11010901, FogBossFlagAnchorKind.Both, "h1117B1"),
            BossFlagFamily("parish", 11010901, FogBossFlagAnchorKind.EncodedNegativePlayRegion, "h1119B1"),
            BossFlagFamily("parish", 11010902, FogBossFlagAnchorKind.Both, "h1122B1"),
        }.SelectMany(entries => entries).ToArray());

        private static FogAreaTagPolicy AreaTag(string area, FogAreaTags tags, string rationale) =>
            new FogAreaTagPolicy(new FogAreaId(area), tags, rationale);

        private static IReadOnlyList<FogBossDefeatFlagCollisionPolicy> BossFlagFamily(
            string map,
            int completionEventId,
            FogBossFlagAnchorKind anchorKind,
            params string[] collisions)
        {
            string anchorRationale = anchorKind switch
            {
                FogBossFlagAnchorKind.CollisionBossEnemy => "a boss enemy entity belongs to the named collision and its pristine HandleBossDefeat handler uses the event ID",
                FogBossFlagAnchorKind.EncodedNegativePlayRegion => "the pristine negative PlayRegionID decodes to the same-map HandleBossDefeat event ID",
                FogBossFlagAnchorKind.Both => "both the collision's boss enemy handler and its encoded negative PlayRegionID resolve to the same event ID",
                _ => throw new ArgumentOutOfRangeException(nameof(anchorKind)),
            };
            string rationale = $"Project boss-collision rule: {anchorRationale}; common event 0 routes this completion event through event 970, which consumes it as a completion flag. This collision receives the project default flag so generated boss-collision handling tracks that encounter.";
            return Array.AsReadOnly(collisions.Select(collision => new FogBossDefeatFlagCollisionPolicy(
                new FogMapId(map), collision, completionEventId, anchorKind, rationale)).ToArray());
        }

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
        /// Builds the project-owned logical Area inventory. The 131 stable IDs are grouped by
        /// explicit map-family rules; ordinary is the default class, while boss/optional/trivial
        /// exceptions, start regions, gates, and static links carry separate DSR anchors. Fixed
        /// project topology is explicit, while randomizable transitions are owned by validated
        /// Entrance or Warp records; unresolved relocation candidates are not fabricated.
        /// </summary>
        public static IReadOnlyList<FogProjectAreaPolicyV1> ResolveAreaPolicyV1(
            FogDsrFacts facts,
            IEnumerable<FogKeyItem> resolvedKeyItems = null)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("FogProjectAreaPolicyV1 requires map, interaction, and EMEVD facts from a clean DSR game directory.");

            IReadOnlyList<FogAreaTagPolicy> tags = ResolveCompleteAreaTagPolicies(facts);
            var tagsByArea = tags.ToDictionary(policy => policy.Area);
            FogKeyItem[] policyKeyItems = resolvedKeyItems?.ToArray() ?? FogAnnotationPolicyResolver.ResolveKeyItemIdentities(
                facts, KeyItemIdentityPolicies).Select(identity =>
                {
                    FogKeyItemProjectPolicyV1 policy = KeyItemProjectPoliciesV1.Single(candidate => candidate.Name == identity.Name);
                    return new FogKeyItem(identity.Name, identity.GameItemId, policy.DefaultArea, policy.Tags);
                }).ToArray();
            IReadOnlyList<FogAreaLink> resolvedLinks = FogAnnotationPolicyResolver.ResolveAreaLinkPolicies(
                facts, AreaLinkPolicies, policyKeyItems);
            if (resolvedLinks.Count != AreaLinkPolicies.Count)
                throw new InvalidDataException("FogProjectAreaPolicyV1 did not resolve every authored static Area.To link.");
            FogCustomStart[] starts = FogAnnotationPolicyResolver.ResolveCustomStarts(facts, CustomStarts).ToArray();
            FogProjectEntranceBoundaryPolicy[] gates = FogAnnotationPolicyResolver.ResolveEntranceBoundaryPolicies(facts).ToArray();
            IReadOnlyList<FogAreaNoStaticLinksPolicy> noStatic = FogAnnotationPolicyResolver.ResolveAreaNoStaticLinksPolicies(facts, AreaNoStaticLinksPolicies);

            var result = new List<FogProjectAreaPolicyV1>(AreaIdentityOrder.Count);
            foreach (FogAreaId area in AreaIdentityOrder)
            {
                FogMapId ownerMap = ResolveAreaMap(area);
                if (!facts.Maps.TryGetValue(ownerMap, out FogDsrFacts.Map map))
                    throw new KeyNotFoundException($"Area '{area}' is assigned to missing pristine DSR map '{ownerMap}'.");
                FogAreaTagPolicy tag = tagsByArea[area];
                FogAreaLinkPolicy[] staticLinks = AreaLinkPolicies.Where(policy => policy.Source == area).ToArray();
                bool participatesInStaticLink = AreaLinkPolicies.Any(policy => policy.Source == area || policy.Destination == area);
                var anchors = new List<FogProjectAreaEvidenceAnchor>
                {
                    new FogProjectAreaEvidenceAnchor(FogProjectAreaAnchorKind.MapMembership, ownerMap,
                        area.Value == "kiln_start"
                            ? $"Compact V1 progression marker assigned to Firelink map {map.GameMapFileId}; it represents accessible Lordvessel altar access, not a physical Kiln segment."
                            : $"MapStudio/{map.GameMapFileId}.msb membership under the authored '{area.Value.Split('_')[0]}' area family."),
                };
                FogBossAreaTagPolicy boss = BossAreaTagPolicies.SingleOrDefault(policy => policy.Area == area);
                if (boss != null)
                    anchors.Add(new FogProjectAreaEvidenceAnchor(FogProjectAreaAnchorKind.BossEncounter, boss.Map,
                        $"HandleBossDefeat event {boss.BossEventId}; MSB enemy part(s): {string.Join(", ", boss.EnemyPartNames)}."));
                foreach (FogCustomStart start in starts.Where(policy => policy.Area == area))
                    anchors.Add(new FogProjectAreaEvidenceAnchor(FogProjectAreaAnchorKind.CustomStart, start.Respawn.Map,
                        $"Named respawn region '{facts.Maps[start.Respawn.Map].SpawnPoints[start.Respawn.SpawnPoint.Value]}', entity {start.Respawn.SpawnPoint.Value}."));
                foreach (FogProjectEntranceBoundaryPolicy gate in gates.Where(policy => policy.ASideArea == area || policy.BSideArea == area))
                    anchors.Add(new FogProjectAreaEvidenceAnchor(FogProjectAreaAnchorKind.EntranceBoundary, gate.Map,
                        $"{gate.ProjectTransitionId}; object {gate.InteractionObjectName}/{gate.InteractionObjectEntityId}; event {gate.InteractionEventId}."));
                foreach (FogProjectWarpPolicyV1 warp in WarpPoliciesV1.Where(policy => policy.SourceArea == area || policy.DestinationArea == area))
                    anchors.Add(new FogProjectAreaEvidenceAnchor(FogProjectAreaAnchorKind.Warp,
                        warp.SourceArea == area ? warp.SourceMap : warp.DestinationMap,
                        $"{warp.Id}; source event {warp.SourceEventId}; cutscene {warp.CutsceneId}; destination region {warp.DestinationRegionEntityId} on {warp.DestinationMap}."));
                foreach (FogProjectFlagWarpPolicyV1 warp in FlagWarpPoliciesV1.Where(policy => policy.SourceArea == area || policy.DestinationArea == area))
                    anchors.Add(new FogProjectAreaEvidenceAnchor(FogProjectAreaAnchorKind.Warp, warp.Map,
                        $"{warp.Id}; generated event {warp.ActivationEventId} sets flag {warp.ActivationFlagId} on object {warp.ActivationObjectEntityId}; destination region '{warp.DestinationRegionName}' is remapped to {warp.RuntimeDestinationRegionEntityId}."));
                foreach (FogAreaLinkPolicy link in staticLinks)
                    anchors.Add(new FogProjectAreaEvidenceAnchor(FogProjectAreaAnchorKind.StaticLink, ownerMap,
                        $"Area.To {link.Source.Value} -> {link.Destination.Value}; {link.Rationale}"));
                string rationale = boss != null
                    ? $"Project segmentation assigns '{area}' to the boss encounter node in {ownerMap}; {boss.Rationale}"
                    : !string.IsNullOrWhiteSpace(tag.Rationale) && tag.Tags != FogAreaTags.None
                        ? $"Project segmentation assigns '{area}' to {ownerMap}; {tag.Rationale}"
                        : $"Project segmentation assigns '{area}' as a named logical segment in the {ownerMap} map family. Ordinary progression is the default; only explicit boss, optional, or trivial policies override it. Map membership validates ownership and does not claim graph reachability.";
                FogProjectAreaRouteDisposition disposition;
                if (participatesInStaticLink)
                {
                    disposition = FogProjectAreaRouteDisposition.StaticLinksAuthored;
                }
                else if (EntrancePoliciesV1.Any(policy => policy.SourceArea == area || policy.DestinationArea == area))
                {
                    disposition = FogProjectAreaRouteDisposition.EntranceOwned;
                }
                else if (WarpPoliciesV1.Any(policy => policy.SourceArea == area || policy.DestinationArea == area)
                    || FlagWarpPoliciesV1.Any(policy => policy.SourceArea == area || policy.DestinationArea == area))
                {
                    disposition = FogProjectAreaRouteDisposition.WarpOwned;
                }
                else if (AreaNoStaticLinksPolicies.Any(policy => policy.Area == area))
                {
                    disposition = FogProjectAreaRouteDisposition.ExplicitNoStaticLink;
                }
                else
                {
                    // The project owns every Area identity. An Area without an authored static
                    // route is explicitly left to the curated physical Entrance/Warp policy;
                    // this never creates a synthetic edge. Category coverage separately gates
                    // production until every required physical transition family is represented.
                    disposition = FogProjectAreaRouteDisposition.ExplicitNoStaticLink;
                    rationale += " Project V1 explicitly authors no static Area.To link for this segment; physical transitions, when present, must be represented by a validated Entrance or Warp.";
                }
                if (noStatic.Any(policy => policy.Area == area))
                    rationale += " A separate policy assigns this encounter/approach boundary outside Area.To; remaining physical edge families are tracked in the Entrance/Warp pass.";
                result.Add(new FogProjectAreaPolicyV1(area, ownerMap, tag.Tags,
                    Array.AsReadOnly(staticLinks), disposition, Array.AsReadOnly(anchors.ToArray()), rationale));
            }
            return Array.AsReadOnly(result.ToArray());
        }

        private static readonly HashSet<FogAreaId> ProjectBossAreaIds = new HashSet<FogAreaId>(new[]
        {
            "depths_gaping", "burg_taurus", "burg_capra", "parish_gargoyles", "paintedworld_priscilla",
            "darkroot_butterfly", "darkroot_sif", "dlc_guardian", "dlc_artorias", "dlc_kalameetdeath",
            "dlc_manus", "catacombs_pinwheel", "totg_nito", "blighttown_quelaag", "demonruins_ceaseless",
            "demonruins_firesage", "demonruins_centipede", "demonruins_bedofchaos", "sens_irongolem",
            "anorlondo_os", "anorlondo_gwyndolin", "newlondo_fourkings", "dukes_seath2", "kiln_gwyn",
        }.Select(value => new FogAreaId(value)));

        private static FogAreaId Area(string value) => new FogAreaId(value);
        private static FogCondition AreaReached(string value) => new FogCondition(FogConditionKind.Atom,
            Array.AsReadOnly(new[] { FogConditionReference.Area(Area(value)) }));
        private static FogCondition ItemOwned(string value) => new FogCondition(FogConditionKind.Atom,
            Array.AsReadOnly(new[] { FogConditionReference.KeyItem(new FogItemName(value)) }));
        private static FogCondition AllReached(params FogConditionReference[] terms) => new FogCondition(
            FogConditionKind.All, Array.AsReadOnly(terms));

        private static FogAreaLinkPolicy LocalRoute(string source, string destination, string rationale,
            FogCondition condition = null, long? destinationBossEventId = null) => new FogAreaLinkPolicy(
                Area(source), Area(destination), condition, FogAreaLinkTags.None, rationale,
                destinationBossEventId);

        private static FogAreaLinkPolicy MapRoute(string source, string destination, string sourcePart,
            string rationale, FogCondition condition = null) => new FogAreaLinkPolicy(
                Area(source), Area(destination), condition, FogAreaLinkTags.None, rationale,
                ConnectCollisionPartName: sourcePart);

        private static void AddLocalRoute(List<FogAreaLinkPolicy> output, string source, string destination,
            FogCondition condition = null, long? destinationBossEventId = null)
        {
            string rationale = $"Compact V1 project topology joins the adjacent logical segments '{source}' and '{destination}' inside their shared DSR map. It is a fixed Area.To route, not a randomized Entrance.";
            output.Add(LocalRoute(source, destination, rationale, condition, destinationBossEventId));
            output.Add(LocalRoute(destination, source, rationale, condition));
        }

        private static bool IsPolicyGatePair(string first, string second)
        {
            static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
            string key = Pair(first, second);
            return new HashSet<string>(StringComparer.Ordinal)
            {
                Pair("anorlondo_rafters", "anorlondo_postrafters"), Pair("anorlondo_main", "anorlondo_building"),
                Pair("asylum", "asylum_oscar"), Pair("blighttown_upper", "blighttown"),
                Pair("catacombs_start", "catacombs"), Pair("darkroot", "darkroot_stone"),
                Pair("depths", "depths_gaping"), Pair("dukes_machines", "dukes_caves"),
                Pair("greathollow", "greathollow_ashlake"), Pair("newlondo", "newlondo_building"),
                Pair("newlondo_lower", "newlondo_building"), Pair("paintedworld", "paintedworld_annex"),
                Pair("burg_start", "burg_upper"), Pair("parish_hellkite", "parish_church"),
                Pair("sens", "sens_mid"), Pair("sens_upper", "sens_mid"), Pair("totg", "totg_lower"),
                Pair("demonruins_lower", "demonruins_lordvessel"),
                Pair("totg_lower", "totg_lordvessel"),
            }.Contains(key);
        }

        private static void AddLocalSequence(List<FogAreaLinkPolicy> output, params string[] areas)
        {
            for (int index = 0; index + 1 < areas.Length; index++)
            {
                string source = areas[index];
                string destination = areas[index + 1];
                if (IsPolicyGatePair(source, destination)) continue;
                FogAreaId destinationId = Area(destination);
                // Boss gates are separate graph edges. A boss area can still own its authored
                // post-encounter route, but an ordinary Area.To edge cannot bypass its entrance.
                if (ProjectBossAreaIds.Contains(destinationId)) continue;
                if (source == "darkroot_postbutterfly" && destination == "darkroot_forest") continue;

                if (source == "dukes_prison" && destination == "dukes_extra")
                {
                    AddLocalRoute(output, source, destination,
                        ItemOwned("archive_tower_giant_door_key_goods_2005"));
                    continue;
                }
                AddLocalRoute(output, source, destination);
            }
        }

        /// <summary>
        /// Project V1's fixed Area topology. Physical cross-map links name their verified
        /// ConnectCollision part; within-map logical segmentation is explicitly authored from the
        /// stable project Area families. Randomizable walls remain Entrances and hard shortcuts
        /// retain their existing tags.
        /// </summary>
        private static IReadOnlyList<FogAreaLinkPolicy> BuildProjectAreaLinkPolicies()
        {
            var links = new List<FogAreaLinkPolicy>
            {
                new FogAreaLinkPolicy(Area("depths"), Area("depths_gaping"), null, FogAreaLinkTags.Hard,
                    "A deliberate hazardous drop from the Depths reaches the Gaping Dragon arena; this hard-only project route is separate from the validated fog-wall Entrance.", 11000001),
                new FogAreaLinkPolicy(Area("totg"), Area("totg_lower"), null, FogAreaLinkTags.Hard,
                    "Pristine ToTG EMEVD event 11310002 checks Patches-kick region 1313400 and the post-kick landing region 1312121; project V1 exposes this one-way descent only in hard logic."),
            };

            AddLocalSequence(links, "asylum", "asylum_outside", "asylum_oscar", "asylum_prebalcony", "asylum_f2west", "asylum_balcony", "asylum_doll", "asylum_stray", "asylum_loop");
            AddLocalSequence(links, "burg_start", "burg_upper", "burg_upperresidence", "burg_bonfire", "burg_above_bonfire", "burg_ladder", "burg_lower", "burg_lowerresidence", "burg_taurus", "burg_posttaurus", "parish_hellkite", "parish_church", "parish_andre", "parish_gargoyles", "parish_bell");
            AddLocalSequence(links, "firelink", "firelink_crow", "firelink_elevator", "firelink_gate");
            AddLocalSequence(links, "paintedworld", "paintedworld_annex", "paintedworld_priscilla");
            AddLocalSequence(links, "darkroot", "darkroot_stone", "darkroot_butterfly", "darkroot_postbutterfly", "darkroot_forest", "darkroot_sif");
            AddLocalSequence(links, "dlc_start", "dlc_guardian", "dlc_sanctuary", "dlc_forest", "dlc_artorias", "dlc_hallway", "dlc_gough", "dlc_kalameet", "dlc_kalameetdeath", "dlc_township", "dlc_townshiphidden", "dlc_manus", "dlc_top", "dlc_bottom", "dlc_postartorias");
            AddLocalSequence(links, "catacombs_start", "catacombs", "catacombs_prepinwheel", "catacombs_pinwheel");
            AddLocalSequence(links, "totg", "totg_lower", "totg_lordvessel", "totg_coffin", "totg_nito");
            AddLocalSequence(links, "greathollow_start", "greathollow", "greathollow_ashlake");
            AddLocalSequence(links, "blighttown_upper", "blighttown", "blighttown_quelaag", "blighttown_bell");
            AddLocalSequence(links, "demonruins", "demonruins_ceaseless", "demonruins_lava", "demonruins_ceaselesslava", "demonruins_lower", "demonruins_lordvessel", "demonruins_firesage", "demonruins_stairs", "demonruins_elevator", "demonruins_centipede", "demonruins_izalithprelava", "demonruins_izalithlava", "demonruins_izalith", "demonruins_bedofchaos");
            AddLocalSequence(links, "sens", "sens_mid", "sens_cage", "sens_upper", "sens_irongolem");
            AddLocalSequence(links, "anorlondo_start", "anorlondo_prerafters", "anorlondo_rafters", "anorlondo_postrafters", "anorlondo_main", "anorlondo_building", "anorlondo_elevator1", "anorlondo_elevator2", "anorlondo_front", "anorlondo_blacksmith", "anorlondo_cathedral", "anorlondo_painting", "anorlondo_chandelier", "anorlondo_gwynevere", "anorlondo_darkmoon");
            AddLocalSequence(links, "newlondo", "newlondo_building", "newlondo_lower", "newlondo_lowerskip", "newlondo_stairs", "newlondo_fourkings", "newlondo_drakes", "newlondo_drakesbeatrice");
            AddLocalSequence(links, "dukes_start", "dukes_library", "dukes_crystalgolem", "dukes_hallway", "dukes_prison", "dukes_extra", "dukes_giantcell", "dukes_library2", "dukes_machines", "dukes_caves", "dukes_seath1", "dukes_seath2");
            AddLocalRoute(links, "firelink", "kiln_start");
            AddLocalSequence(links, "kiln", "kiln_stairs");

            links.Add(LocalRoute("anorlondo_darkmoon", "anorlondo_gwyndolin",
                "Project V1 keeps the hidden Gwyndolin encounter fixed and reachable from the Darkmoon branch; the boss event identity is validated against pristine event 11510900.",
                destinationBossEventId: 11510900));

            links.Add(MapRoute("firelink_gate", "burg_start", "h0008B2_0001",
                "The verified Firelink ConnectCollision part h0008B2_0001 targets the Parish map; project V1 assigns the paired logical route to the Burg entry."));
            links.Add(MapRoute("parish_andre", "firelink_elevator", "h1021B1_0000",
                "The verified Parish ConnectCollision part h1021B1_0000 targets Firelink; project V1 assigns this fixed elevator route between the blacksmith side and Firelink."));
            links.Add(MapRoute("parish_andre", "darkroot", "h1049B1_0000",
                "The verified Parish ConnectCollision part h1049B1_0000 targets Darkroot; project V1 assigns the valley passage to the Andre-side Parish segment."));
            links.Add(MapRoute("darkroot", "parish_andre", "h0014B0_0000",
                "The verified Darkroot ConnectCollision part h0014B0_0000 targets Parish; project V1 assigns the return route to the Andre-side segment."));
            links.Add(MapRoute("parish_bell", "sens", "h1048B1_0001",
                "The verified Parish ConnectCollision part h1048B1_0001 targets Sen's Fortress; project V1 requires both bell Areas before this mandatory route is available.",
                AllReached(FogConditionReference.Area(Area("parish_bell")), FogConditionReference.Area(Area("blighttown_bell")))));
            links.Add(MapRoute("sens_mid", "parish_andre", "h0001B0_0001",
                "The verified Sen's ConnectCollision part h0001B0_0001 targets Parish; project V1 assigns the fixed return lift to the Andre-side segment."));
            links.Add(MapRoute("burg_lower", "depths", "h1124B1_0000",
                "The verified Parish ConnectCollision part h1124B1_0000 targets Depths; project V1 requires the Capra branch to be reached before entering the sewer route.", AreaReached("burg_capra")));
            links.Add(MapRoute("depths", "burg_lower", "h0007B0_0000",
                "The verified Depths ConnectCollision part h0007B0_0000 targets Parish; project V1 assigns the return route to Lower Burg."));
            links.Add(MapRoute("blighttown_bell", "demonruins", "h0053B0_0000",
                "The verified Blighttown ConnectCollision part h0053B0_0000 targets Demon Ruins; project V1 makes the route available from the post-Quelaag bell branch.", AreaReached("blighttown_quelaag")));
            links.Add(MapRoute("demonruins", "blighttown_bell", "h0000B1_0000",
                "The verified Demon Ruins ConnectCollision part h0000B1_0000 targets Blighttown; project V1 assigns the fixed return route to the bell branch."));
            links.Add(MapRoute("demonruins_izalithprelava", "blighttown_bell", "h0020B1_0000",
                "The verified Demon Ruins ConnectCollision part h0020B1_0000 targets Blighttown; project V1 assigns this fixed backtrack from the Lost Izalith approach to the bell-side hub route."));
            links.Add(MapRoute("blighttown", "greathollow_start", "h0033B0_0000",
                "The verified Blighttown ConnectCollision part h0033B0_0000 targets Great Hollow; project V1 assigns the hollow entrance as an optional fixed branch."));
            links.Add(MapRoute("greathollow_start", "blighttown", "h0000B2_0000",
                "The verified Great Hollow ConnectCollision part h0000B2_0000 targets Blighttown; project V1 assigns the return route to the swamp."));
            links.Add(MapRoute("blighttown_upper", "parish_andre", "h9700B0_0000",
                "The verified Blighttown ConnectCollision part h9700B0_0000 loads the Parish map; project V1 treats this fixed upper-route exit as a one-way return to the Andre-side hub."));
            links.Add(MapRoute("blighttown_upper", "newlondo_drakesbeatrice", "h0062B0_0000",
                "The verified Blighttown ConnectCollision part h0062B0_0000 loads New Londo; project V1 assigns the fixed valley connector to its Drakes-side segment."));
            links.Add(MapRoute("blighttown_upper", "newlondo_drakes", "h0062B0_0001",
                "The verified Blighttown ConnectCollision part h0062B0_0001 loads New Londo; project V1 assigns the paired platform endpoint to the Drakes approach."));
            links.Add(MapRoute("newlondo_drakesbeatrice", "blighttown_upper", "h0003B0_0002",
                "The verified New Londo ConnectCollision part h0003B0_0002 loads Blighttown; project V1 assigns this fixed valley return to the upper pit route."));
            links.Add(MapRoute("newlondo_drakes", "blighttown_upper", "h0050B0_0001",
                "The verified New Londo ConnectCollision part h0050B0_0001 loads Blighttown; project V1 assigns this fixed valley return to the upper pit route."));
            links.Add(MapRoute("newlondo", "blighttown_upper", "h0053B0_0001",
                "The verified New Londo ConnectCollision part h0053B0_0001 loads Blighttown; project V1 assigns this fixed valley return to the upper pit route."));
            links.Add(MapRoute("dukes_start", "anorlondo_start", "h0100B0_0000",
                "The verified Duke's Archives ConnectCollision part h0100B0_0000 loads Anor Londo; project V1 assigns this fixed return to the Anor Londo arrival segment."));
            links.Add(MapRoute("dukes_library", "anorlondo_start", "h0101B0_0000",
                "The verified Duke's Archives ConnectCollision part h0101B0_0000 loads Anor Londo; project V1 assigns this fixed return to the Anor Londo arrival segment."));
            links.Add(MapRoute("dukes_hallway", "anorlondo_start", "h0102B0_0000",
                "The verified Duke's Archives ConnectCollision part h0102B0_0000 loads Anor Londo; project V1 assigns this fixed return to the Anor Londo arrival segment."));
            links.Add(MapRoute("dukes_library2", "anorlondo_start", "h0103B0_0000",
                "The verified Duke's Archives ConnectCollision part h0103B0_0000 loads Anor Londo; project V1 assigns this fixed return to the Archives bonfire segment."));
            links.Add(MapRoute("catacombs_pinwheel", "totg", "h0036B0_0000",
                "The verified Catacombs ConnectCollision part h0036B0_0000 targets Tomb of the Giants; project V1 connects the post-Pinwheel branch to the Tomb.", AreaReached("catacombs_pinwheel")));
            links.Add(MapRoute("totg", "catacombs", "h0000B1_0000",
                "The verified Tomb of the Giants ConnectCollision part h0000B1_0000 targets Catacombs; project V1 assigns the fixed return descent."));
            links.Add(MapRoute("darkroot_forest", "dlc_start", "h0017B0_0000",
                "The verified Darkroot ConnectCollision part h0017B0_0000 targets the DLC map; project V1 makes optional DLC access require the Broken Pendant.", ItemOwned("broken_pendant_goods_2520")));
            links.Add(MapRoute("dlc_postartorias", "darkroot", "h0037B1_0000",
                "The verified DLC ConnectCollision part h0037B1_0000 targets the Darkroot map; project V1 assigns the return landing to the Darkroot hub Area so a DLC custom start can rejoin the main progression graph.", AreaReached("dlc_postartorias")));
            links.Add(MapRoute("anorlondo_painting", "paintedworld", "h0039B1_0001",
                "The verified Anor Londo ConnectCollision part h0039B1_0001 targets the Painted World; project V1 gates entry on the Peculiar Doll.", ItemOwned("peculiar_doll_goods_384")));
            links.Add(MapRoute("paintedworld_priscilla", "anorlondo_main", "h0041B0_0000",
                "The verified Painted World ConnectCollision part h0041B0_0000 targets Anor Londo; project V1 assigns the fixed exit to the main-building side."));
            links.Add(MapRoute("firelink", "catacombs_start", "h0021B2_0001",
                "The verified Firelink ConnectCollision part h0021B2_0001 targets Catacombs; project V1 assigns the graveyard descent to the Catacombs start."));
            links.Add(MapRoute("catacombs_start", "firelink", "h0000B0_0000",
                "The verified Catacombs ConnectCollision part h0000B0_0000 targets Firelink; project V1 assigns the fixed ascent to Firelink."));
            links.Add(MapRoute("firelink", "newlondo", "h0019B2_0001",
                "The verified Firelink ConnectCollision part h0019B2_0001 targets New Londo; project V1 assigns the fixed elevator descent."));
            links.Add(MapRoute("newlondo", "firelink", "h0000B0_0001",
                "The verified New Londo ConnectCollision part h0000B0_0001 targets Firelink; project V1 assigns the fixed elevator return."));
            links.Add(MapRoute("sens_irongolem", "anorlondo_start", "h0029B0_0001",
                "The verified Sen's ConnectCollision part h0029B0_0001 targets Anor Londo; project V1 places the route beyond the Iron Golem branch."));
            links.Add(MapRoute("anorlondo_start", "sens_irongolem", "h0000B1_0001",
                "The verified Anor Londo ConnectCollision part h0000B1_0001 targets Sen's Fortress; project V1 assigns the fixed lift return."));
            links.Add(MapRoute("asylum", "firelink", "h0054B1_0000",
                "The verified Asylum ConnectCollision part h0054B1_0000 targets Firelink; project V1 treats the tutorial departure as a fixed one-way route."));
            links.Add(MapRoute("firelink_crow", "asylum", "h0039B2_0001",
                "The verified Firelink ConnectCollision part h0039B2_0001 targets the Asylum; project V1 represents the fixed crow return as a one-way static route."));
            links.Add(MapRoute("firelink", "kiln", "h0018B2_0001",
                "The verified Firelink ConnectCollision part h0018B2_0001 targets the physical Kiln; project V1 keeps that final-area route behind the Lordvessel and all four Lord Souls. The separate kiln_start marker satisfies the legacy fixed golden-gate milestone at the accessible Firelink altar.",
                AllReached(
                    FogConditionReference.KeyItem(new FogItemName("lordvessel")),
                    FogConditionReference.KeyItem(new FogItemName("lord_soul_goods_2500")),
                    FogConditionReference.KeyItem(new FogItemName("lord_soul_goods_2501")),
                    FogConditionReference.KeyItem(new FogItemName("bequeathed_lord_soul_shard_goods_2502")),
                    FogConditionReference.KeyItem(new FogItemName("bequeathed_lord_soul_shard_goods_2503")))));

            return Array.AsReadOnly(links.ToArray());
        }

        public static IReadOnlyList<FogAreaLinkPolicy> AreaLinkPolicies { get; } = BuildProjectAreaLinkPolicies();

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

        private static FogProjectEntrancePolicyV1 TraversableFog(
            string id,
            string map,
            string sideA,
            string sideB,
            string objectName,
            int objectId,
            long eventId,
            int regionA,
            int regionB,
            string rationale,
            FogItemName? requiredItemName = null,
            FogGameItemId? requiredItem = null,
            long? requiredItemCheckEventId = null,
            int? entryFlag = null,
            FogCondition sideCondition = null) => new FogProjectEntrancePolicyV1(
                id,
                FogProjectEntranceKind.TraversableFog,
                new FogMapId(map),
                new FogAreaId(sideA),
                new FogAreaId(sideB),
                objectName,
                objectId,
                null,
                eventId,
                requiredItemName,
                requiredItem,
                FogEntranceTags.World,
                rationale,
                ASideActionRegionEntityId: regionA,
                BSideActionRegionEntityId: regionB,
                RequiredItemCheckEventId: requiredItemCheckEventId,
                ASideEntryFlag: entryFlag,
                BSideEntryFlag: entryFlag,
                ASideCondition: sideCondition,
                BSideCondition: sideCondition);

        private static FogProjectEntrancePolicyV1 BossFog(
            string id,
            string map,
            string approachArea,
            string bossArea,
            string objectName,
            int objectId,
            long interactionEventId,
            int triggerRegionId,
            bool bossAreaOnASide,
            string rationale) => new FogProjectEntrancePolicyV1(
                id,
                FogProjectEntranceKind.BossBoundary,
                new FogMapId(map),
                new FogAreaId(approachArea),
                new FogAreaId(bossArea),
                objectName,
                objectId,
                null,
                interactionEventId,
                null,
                null,
                FogEntranceTags.Boss,
                rationale,
                BossTriggerRegionEntityId: triggerRegionId,
                BossAreaOnASide: bossAreaOnASide);

        private static FogProjectEntrancePolicyV1 LordvesselGate(
            string id,
            string map,
            string areaA,
            string areaB,
            string objectName,
            int objectId,
            long eventId,
            int regionId,
            string rationale) => new FogProjectEntrancePolicyV1(
                id,
                FogProjectEntranceKind.LordvesselGate,
                new FogMapId(map),
                new FogAreaId(areaA),
                new FogAreaId(areaB),
                objectName,
                objectId,
                regionId,
                eventId,
                null,
                null,
                FogEntranceTags.Door | FogEntranceTags.Lordvessel,
                rationale);

        private static FogCondition All(FogConditionReference first, FogConditionReference second) =>
            new FogCondition(FogConditionKind.All, Array.AsReadOnly(new[] { first, second }));

        /// <summary>Validated ordinary fog walls with explicit physical side regions and project Areas.</summary>
        public static IReadOnlyList<FogProjectEntrancePolicyV1> TraversableFogPoliciesV1 { get; } = Array.AsReadOnly(new[]
        {
            TraversableFog("anorlondo-rafters-elevator-wall", "anorlondo", "anorlondo_rafters", "anorlondo_postrafters", "o5864_0000", 1511700, 11510090, 1512600, 1512601,
                "Project segmentation links the rafters to the elevator landing; pristine event 11510090 pairs action regions 1512600/1512601 at object o5864_0000/entity 1511700."),
            TraversableFog("anorlondo-main-building-wall", "anorlondo", "anorlondo_main", "anorlondo_building", "o5865_0000", 1511702, 11510090, 1512602, 1512603,
                "Project segmentation links the Silver Knight approach to the main building; pristine event 11510090 pairs action regions 1512602/1512603 at object o5865_0000/entity 1511702."),
            TraversableFog("asylum-oscar-cell-wall", "asylum", "asylum", "asylum_oscar", "o8952_0000", 1811700, 11810090, 1812600, 1812601,
                "Project segmentation separates the starting hallway from Oscar's cell; pristine event 11810090 pairs action regions 1812600/1812601 at object o8952_0000/entity 1811700."),
            TraversableFog("blighttown-upper-pit-wall", "blighttown", "blighttown_upper", "blighttown", "o4956_0000", 1401702, 11400090, 1402602, 1402603,
                "Project segmentation connects the upper wood-plank route to the main pit; pristine event 11400090 pairs action regions 1402602/1402603 at object o4956_0000/entity 1401702."),
            TraversableFog("catacombs-patches-bridge-wall", "catacombs", "catacombs_start", "catacombs", "o3952_0000", 1301700, 11300090, 1302650, 1302651,
                "Project segmentation connects the Patches bridge landing to the Catacombs interior; pristine event 11300090 pairs action regions 1302650/1302651 at object o3952_0000/entity 1301700."),
            TraversableFog("darkroot-stone-knights-wall", "darkroot", "darkroot", "darkroot_stone", "o2906_0000", 1201700, 11200090, 1202600, 1202601,
                "Project segmentation connects the Garden approach to the stone-knight section; pristine event 11200090 pairs action regions 1202600/1202601 at object o2906_0000/entity 1201700."),
            TraversableFog("depths-gaping-approach-wall", "depths", "depths", "depths_gaping", "o1420_0000", 1001700, 11000090, 1002600, 1002601,
                "Project segmentation assigns the Channeler-side fog boundary to the Gaping Dragon encounter approach; pristine event 11000090 pairs action regions 1002600/1002601 at object o1420_0000/entity 1001700."),
            TraversableFog("dukes-crystal-caves-wall", "dukes", "dukes_machines", "dukes_caves", "o7906_0000", 1701706, 11700083, 1702606, 1702607,
                "Project segmentation connects the Archives machinery to the Crystal Caves; pristine event 11700083 pairs action regions 1702606/1702607 at object o7906_0000/entity 1701706."),
            TraversableFog("great-hollow-ash-lake-wall", "greathollow", "greathollow", "greathollow_ashlake", "o3971_0000", 1321700, 11320090, 1322600, 1322601,
                "Project segmentation connects Great Hollow to Ash Lake; pristine event 11320090 pairs action regions 1322600/1322601 at object o3971_0000/entity 1321700."),
            TraversableFog("new-londo-shortcut-building-wall", "newlondo", "newlondo", "newlondo_building", "o6604_0000", 1601700, 11600090, 1602600, 1602601,
                "Project segmentation connects the New Londo approach to the shortcut building; pristine event 11600090 pairs action regions 1602600/1602601 at object o6604_0000/entity 1601700."),
            TraversableFog("new-londo-seal-building-wall", "newlondo", "newlondo_lower", "newlondo_building", "o6605_0000", 1601702, 11600090, 1602602, 1602603,
                "Project policy keeps the Key to the Seal condition and opened-gate event on both sides; pristine event 11600090 pairs action regions 1602602/1602603 at object o6605_0000/entity 1601702, and event 11600900 checks Goods 2013.",
                new FogItemName("key_to_the_seal_goods_2013"), new FogGameItemId((int)FogGameItemCategory.Goods, 2013), 11600900, 11600100,
                All(FogConditionReference.Area(new FogAreaId("newlondo_building")), FogConditionReference.KeyItem(new FogItemName("key_to_the_seal_goods_2013")))),
            TraversableFog("painted-world-courtyard-wall", "paintedworld", "paintedworld", "paintedworld_annex", "o1603_0000", 1101702, 11100090, 1102602, 1102603,
                "Project segmentation links the statue-tower route and courtyard approach; pristine event 11100090 pairs action regions 1102602/1102603 at object o1603_0000/entity 1101702."),
            TraversableFog("parish-upper-burg-wall", "parish", "burg_start", "burg_upper", "o1419_0000", 1011700, 11010090, 1012600, 1012601,
                "Project segmentation connects the drake landing to Upper Burg; pristine event 11010090 pairs action regions 1012600/1012601 at object o1419_0000/entity 1011700."),
            TraversableFog("parish-church-bridge-wall", "parish", "parish_hellkite", "parish_church", "o1416_0000", 1011702, 11010090, 1012602, 1012603,
                "Project segmentation connects the Hellkite bridge to the Parish church; pristine event 11010090 pairs action regions 1012602/1012603 at object o1416_0000/entity 1011702."),
            TraversableFog("sens-midpoint-stair-wall", "sens", "sens", "sens_mid", "o5152_0000", 1501700, 11500090, 1502600, 1502601,
                "Project segmentation connects the boulder stairway to the midpoint route; pristine event 11500090 pairs action regions 1502600/1502601 at object o5152_0000/entity 1501700."),
            TraversableFog("sens-roof-wall", "sens", "sens_upper", "sens_mid", "o5153_0000", 1501702, 11500090, 1502602, 1502603,
                "Project segmentation connects the upper swinging-axes route to the roof approach; pristine event 11500090 pairs action regions 1502602/1502603 at object o5153_0000/entity 1501702."),
            TraversableFog("tomb-upper-lower-wall", "totg", "totg", "totg_lower", "o3962_0000", 1311700, 11310090, 1312600, 1312601,
                "Project segmentation connects the Tomb of the Giants upper route to the lower route; pristine event 11310090 pairs action regions 1312600/1312601 at object o3962_0000/entity 1311700."),
        });

        /// <summary>Twenty-two boss fogs anchored to their exact entrance and completion events.</summary>
        public static IReadOnlyList<FogProjectEntrancePolicyV1> BossEntrancePoliciesV1 { get; } = Array.AsReadOnly(new[]
        {
            BossFog("blighttown-quelaag-boundary", "blighttown", "blighttown", "blighttown_quelaag", "o4950_0000", 1401990, 11405391, 1402998, true,
                "Project policy links the Blighttown approach to Quelaag; pristine event 11405391 region 1402998 and object o4950_0000/entity 1401990 anchor the gate."),
            BossFog("darkroot-sif-boundary", "darkroot", "darkroot_forest", "darkroot_sif", "o2900_0000", 1201990, 11205391, 1202998, true,
                "Project policy links the forest to Sif; pristine event 11205391 region 1202998 and object o2900_0000/entity 1201990 anchor the gate."),
            BossFog("darkroot-butterfly-boundary", "darkroot", "darkroot_stone", "darkroot_butterfly", "o2904_0000", 1201890, 11205381, 1202898, false,
                "Project policy links the stone-knight route to Moonlight Butterfly; pristine event 11205381 region 1202898 and object o2904_0000/entity 1201890 anchor the gate."),
            BossFog("demonruins-ceaseless-boundary", "demonruins", "demonruins", "demonruins_ceaseless", "o4961_0000", 1411790, 11415371, 1412698, false,
                "Project policy links Demon Ruins to Ceaseless Discharge; pristine event 11415371 region 1412698 and object o4961_0000/entity 1411790 anchor the gate."),
            BossFog("demonruins-firesage-boundary", "demonruins", "demonruins_lordvessel", "demonruins_firesage", "o4962_0000", 1411410, 11415341, 1412411, false,
                "Project policy links the Lordvessel route to Demon Firesage; pristine event 11415341 region 1412411 and object o4962_0000/entity 1411410 anchor the gate."),
            BossFog("demonruins-centipede-boundary", "demonruins", "demonruins_stairs", "demonruins_centipede", "o4964_0000", 1411890, 11415381, 1412898, false,
                "Project policy links the stairs to Centipede Demon; pristine event 11415381 region 1412898 and object o4964_0000/entity 1411890 anchor the gate."),
            BossFog("demonruins-bed-of-chaos-boundary", "demonruins", "demonruins_izalith", "demonruins_bedofchaos", "o4972_0000", 1411990, 11415391, 1412998, true,
                "Project policy links Lost Izalith to Bed of Chaos; pristine event 11415391 region 1412998 and object o4972_0000/entity 1411990 anchor the gate."),
            BossFog("dlc-guardian-boundary", "dlc", "dlc_start", "dlc_guardian", "o2909_0001", 1211790, 11215001, 1212888, false,
                "Project policy links the DLC entry to Sanctuary Guardian; pristine event 11215001 region 1212888 and object o2909_0001/entity 1211790 anchor the gate."),
            BossFog("dlc-artorias-boundary", "dlc", "dlc_forest", "dlc_artorias", "o2915_0000", 1211890, 11215011, 1212898, false,
                "Project policy links the forest approach to Artorias; pristine event 11215011 region 1212898 and object o2915_0000/entity 1211890 anchor the gate."),
            BossFog("dlc-manus-boundary", "dlc", "dlc_township", "dlc_manus", "o2911_0001", 1211990, 11215021, 1212998, false,
                "Project policy links the Township to Manus; pristine event 11215021 region 1212998 and object o2911_0001/entity 1211990 anchor the gate."),
            BossFog("dlc-kalameet-boundary", "dlc", "dlc_forest", "dlc_kalameetdeath", "o2919_0000", 1211690, 11215061, 1212908, false,
                "Project policy links the forest approach to Kalameet; pristine event 11215061 region 1212908 and object o2919_0000/entity 1211690 anchor the gate."),
            BossFog("dukes-seath-boundary", "dukes", "dukes_caves", "dukes_seath2", "o7900_0000", 1701990, 11705391, 1702998, false,
                "Project policy links the Crystal Caves to Seath; pristine event 11705391 region 1702998 and object o7900_0000/entity 1701990 anchor the gate."),
            BossFog("kiln-gwyn-boundary", "kiln", "kiln_stairs", "kiln_gwyn", "o8050_0000", 1801990, 11805391, 1802998, false,
                "Project policy links the Kiln stair approach to Gwyn; pristine event 11805391 region 1802998 and object o8050_0000/entity 1801990 anchor the gate."),
            BossFog("newlondo-four-kings-boundary", "newlondo", "newlondo_lower", "newlondo_fourkings", "o6606_0000", 1601990, 11605391, 1602998, false,
                "Project policy links lower New Londo to the Four Kings encounter; pristine event 11605391 region 1602998 and object o6606_0000/entity 1601990 anchor the gate."),
            BossFog("painted-world-priscilla-boundary", "paintedworld", "paintedworld", "paintedworld_priscilla", "o1600_0000", 1101990, 11105391, 1102998, false,
                "Project policy links the Painted World to Priscilla; pristine event 11105391 region 1102998 and object o1600_0000/entity 1101990 anchor the gate."),
            BossFog("parish-gargoyles-boundary", "parish", "parish_church", "parish_gargoyles", "o1403_0000", 1011990, 11015391, 1012998, true,
                "Project policy links the Parish church to the Bell Gargoyles; pristine event 11015391 region 1012998 and object o1403_0000/entity 1011990 anchor the gate."),
            BossFog("burg-taurus-boundary", "parish", "burg_upper", "burg_taurus", "o1405_0000", 1011890, 11015381, 1012898, true,
                "Project policy links Upper Burg to Taurus Demon; pristine event 11015381 region 1012898 and object o1405_0000/entity 1011890 anchor the gate."),
            BossFog("burg-capra-boundary", "parish", "burg_lower", "burg_capra", "o1413_0000", 1011790, 11015371, 1012888, false,
                "Project policy links Lower Burg to Capra Demon; pristine event 11015371 region 1012888 and object o1413_0000/entity 1011790 anchor the gate."),
            BossFog("sens-irongolem-boundary", "sens", "sens_upper", "sens_irongolem", "o5150_0000", 1501990, 11505391, 1502998, false,
                "Project policy links the Sen's Fortress roof to Iron Golem; pristine event 11505391 region 1502998 and object o5150_0000/entity 1501990 anchor the gate."),
            BossFog("anor-londo-os-boundary", "anorlondo", "anorlondo_cathedral", "anorlondo_os", "o5860_0000", 1511990, 11515391, 1512998, true,
                "Project policy links the cathedral to Ornstein and Smough; pristine event 11515391 region 1512998 and object o5860_0000/entity 1511990 anchor the gate."),
            BossFog("totg-nito-boundary", "totg", "totg_lordvessel", "totg_nito", "o3964_0000", 1311990, 11315391, 1312998, false,
                "Project policy links the Lordvessel route to Gravelord Nito; pristine event 11315391 region 1312998 and object o3964_0000/entity 1311990 anchor the gate."),
        });

        /// <summary>Lordvessel-gated world transitions from their verified game-side anchors.</summary>
        public static IReadOnlyList<FogProjectEntrancePolicyV1> LordvesselEntrancePoliciesV1 { get; } = Array.AsReadOnly(new[]
        {
            LordvesselGate("demonruins-lordvessel-gate", "demonruins", "demonruins_lower", "demonruins_lordvessel", "o4960_0000", 1411710, 11410095, 1412660,
                "The Demon Ruins golden seal links the lower route and the Lordvessel segment; pristine event 11410095 region 1412660 and object o4960_0000/entity 1411710 anchor the gate."),
            LordvesselGate("dukes-lordvessel-gate", "dukes", "anorlondo_start", "dukes_start", "o7907_0000", 1701710, 11700085, 1702610,
                "The Archives golden seal links Anor Londo to the Duke's Archives; pristine event 11700085 region 1702610 and object o7907_0000/entity 1701710 anchor the cross-map gate."),
            LordvesselGate("totg-lordvessel-gate", "totg", "totg_lower", "totg_lordvessel", "o3963_0000", 1311710, 11310095, 1312610,
                "The Tomb of the Giants golden seal links the lower route to the Lordvessel segment; pristine event 11310095 region 1312610 and object o3963_0000/entity 1311710 anchor the gate."),
        });

        /// <summary>Complete compact Entrance candidate pool, with independently authored project endpoint semantics.</summary>
        public static IReadOnlyList<FogProjectEntrancePolicyV1> EntrancePoliciesV1 { get; } = Array.AsReadOnly(
            EntranceBoundaryPolicies.Select(policy => new FogProjectEntrancePolicyV1(
                policy.ProjectTransitionId,
                FogProjectEntranceKind.FixedKeyDoor,
                policy.Map,
                policy.ASideArea,
                policy.BSideArea,
                policy.InteractionObjectName,
                policy.InteractionObjectEntityId,
                policy.ProjectTransitionId == "darkroot-crest-gate" ? 1202500 : null,
                policy.InteractionEventId,
                policy.RequiredItemName,
                policy.RequiredItem,
                FogEntranceTags.Door,
                policy.Rationale))
            .Append(new FogProjectEntrancePolicyV1(
                "catacombs-pinwheel-boss-boundary",
                FogProjectEntranceKind.BossBoundary,
                new FogMapId("catacombs"),
                new FogAreaId("catacombs_prepinwheel"),
                new FogAreaId("catacombs_pinwheel"),
                null,
                1301990,
                null,
                11305391,
                null,
                null,
                FogEntranceTags.Boss,
                "Project policy links the Catacombs approach to Pinwheel; pristine object entity 1301990, trigger region 1302998, interaction event 11305391, and boss completion event 11300001 anchor the boundary.",
                BossTriggerRegionEntityId: 1302998))
            .Concat(TraversableFogPoliciesV1)
            .Concat(BossEntrancePoliciesV1)
            .Concat(LordvesselEntrancePoliciesV1)
            .ToArray());

        /// <summary>
        /// Three independently authored reciprocal world-warp pairs. Each direction is backed by
        /// a pristine PlayCutsceneAndWarpPlayer instruction and a real region in its destination MSB.
        /// </summary>
        public static IReadOnlyList<FogProjectWarpPolicyV1> WarpPoliciesV1 { get; } = Array.AsReadOnly(new[]
        {
            new FogProjectWarpPolicyV1(
                "firelink-crow-asylum",
                new FogMapId("firelink"), new FogAreaId("firelink_crow"), 11020021, 100230, 0,
                new FogMapId("asylum"), new FogAreaId("asylum_loop"), 1812110,
                "The Firelink crow transport event relocates the player to pristine Asylum region 1812110; this optional return route is paired with the later crow return event."),
            new FogProjectWarpPolicyV1(
                "asylum-crow-firelink",
                new FogMapId("asylum"), new FogAreaId("asylum_loop"), 11810150, 180126, 0,
                new FogMapId("firelink"), new FogAreaId("firelink_crow"), 1022121,
                "The later Asylum return event relocates the player to pristine Firelink region 1022121; the initial tutorial departure remains excluded."),
            new FogProjectWarpPolicyV1(
                "anorlondo-painting-entry",
                new FogMapId("anorlondo"), new FogAreaId("anorlondo_painting"), 11510230, 150135, 0,
                new FogMapId("paintedworld"), new FogAreaId("paintedworld"), 1102510,
                "The painting interaction relocates the player to pristine Painted World region 1102510; the Doll gate stays at its original source interaction."),
            new FogProjectWarpPolicyV1(
                "paintedworld-anorlondo-return",
                new FogMapId("paintedworld"), new FogAreaId("paintedworld"), 11100710, 110035, 0,
                new FogMapId("anorlondo"), new FogAreaId("anorlondo_painting"), 1512500,
                "The Painted World return event relocates the player to pristine Anor Londo region 1512500."),
            new FogProjectWarpPolicyV1(
                "catacombs-coffin-totg",
                new FogMapId("catacombs"), new FogAreaId("catacombs"), 11305000, 130020, 0,
                new FogMapId("totg"), new FogAreaId("totg_coffin"), 1312110,
                "The Catacombs coffin transport relocates the player to pristine Tomb of the Giants region 1312110."),
            new FogProjectWarpPolicyV1(
                "totg-coffin-catacombs-return",
                new FogMapId("totg"), new FogAreaId("totg_coffin"), 11310052, 130121, 0,
                new FogMapId("catacombs"), new FogAreaId("catacombs"), 1302010,
                "The Tomb of the Giants return event relocates the player to pristine Catacombs region 1302010."),
        });

        /// <summary>
        /// The Seath crystal route is not a pristine cutscene relocation: generated event 11700005
        /// sets flag 5308, and the pinned writer remaps the named Dukes temporary respawn point to 1702901.
        /// Keep it separate from WarpPoliciesV1, whose entries require pristine cutscene evidence
        /// and reciprocal directions.
        /// </summary>
        public static IReadOnlyList<FogProjectFlagWarpPolicyV1> FlagWarpPoliciesV1 { get; } = Array.AsReadOnly(new[]
        {
            new FogProjectFlagWarpPolicyV1(
                "dukes-seath-prison",
                new FogMapId("dukes"), new FogAreaId("dukes_seath1"), new FogAreaId("dukes_prison"),
                FogAddedEventBuilder.SeathCrystalInteractionEventId,
                FogAddedEventBuilder.SeathCrystalObjectEntityId,
                FogAddedEventBuilder.SeathCrystalActivatedFlag,
                "復活ポイント（一時用）", -1, SeathPrisonWarpRuntimeId,
                new FogDsrTransform(377.37f, 269.78f, 553.62f, 0f),
                "The project-added Seath crystal interaction sets flag 5308 and sends the player to the uniquely named Dukes prison respawn region; the pinned writer remaps that region to entity 1702901."),
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
        /// The common character-death handler awards the Crest of Artorias lot after the Parish
        /// character dies. Entity/event/flag/lot identities are verified against pristine DSR;
        /// this assigns the source to the project's Andre sub-area.
        /// </summary>
        public static IReadOnlyList<FogCharacterDeathRewardAreaPolicy> CharacterDeathRewardAreaPolicies { get; } = Array.AsReadOnly(new[]
        {
            new FogCharacterDeathRewardAreaPolicy(new FogMapId("parish"), 960, 1322, 6190, 6190,
                new FogAreaId("parish_andre"),
                "Common event 960 awards ItemLot 6190 when pristine Parish enemy entity 6190 is dead and event flag 1322 is on; project policy places that source in the authored Andre sub-area."),
        });

        /// <summary>
        /// Boss encounter areas without a separately authored outgoing route rely on their
        /// Entrance boundary. The Pinwheel gate is anchored to its pristine object, trigger region,
        /// and event on both logical sides; boss areas with a project post-encounter route are
        /// omitted from this no-static list.
        /// </summary>
        public static IReadOnlyList<FogAreaNoStaticLinksPolicy> AreaNoStaticLinksPolicies { get; } = Array.AsReadOnly(
            BossAreaTagPolicies
                .Where(policy => !AreaLinkPolicies.Any(link => link.Source == policy.Area))
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
                .ToArray());

        private static FogBossAreaTagPolicy BossArea(string area, string map, long eventId, params string[] enemyParts) =>
            new FogBossAreaTagPolicy(
                new FogAreaId(area),
                new FogMapId(map),
                eventId,
                Array.AsReadOnly(enemyParts),
                $"Project policy assigns boss progression cost to this encounter area; pristine event {eventId} targets MSB enemy part(s) {string.Join(", ", enemyParts)}.");

        /// <summary>
        /// Compact records used by the custom-start projection. They intentionally carry only
        /// selected reviewed routes; the full generated project topology is compiled separately
        /// from AreaLinkPolicies and EntrancePoliciesV1.
        /// </summary>
        public static IReadOnlyList<FogArea> ReviewedStartAreaRecords { get; } = Array.AsReadOnly(new[]
        {
            StartArea("firelink", "firelink"),
            StartArea("parish_andre", "parish"),
            StartArea("blighttown", "blighttown"),
            StartArea("anorlondo_start", "anorlondo"),
            StartArea("anorlondo_building", "anorlondo"),
            StartArea("catacombs", "catacombs"),
            StartArea("totg", "totg", ReviewedHardLinksFrom("totg")),
            StartArea("depths", "depths", ReviewedHardLinksFrom("depths")),
        });

        private static IReadOnlyList<FogAreaLink> ReviewedHardLinksFrom(string source) => Array.AsReadOnly(AreaLinkPolicies
            .Where(policy => string.Equals(policy.Source.Value, source, StringComparison.Ordinal)
                && policy.Tags.HasFlag(FogAreaLinkTags.Hard))
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
        /// <summary>Resolves authored entrance identity, logical sides, conditions, and DSR anchors.</summary>
        public static IReadOnlyList<FogProjectEntranceResolution> ResolveEntrancePoliciesV1(
            FogDsrFacts facts,
            IEnumerable<FogProjectEntrancePolicyV1> policies = null)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            FogProjectEntrancePolicyV1[] entries = (policies ?? FogProjectAnnotationPolicy.EntrancePoliciesV1).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var resolved = new List<FogProjectEntranceResolution>(entries.Length);
            const FogEntranceTags allowedEntranceTags = FogEntranceTags.Boss | FogEntranceTags.Door
                | FogEntranceTags.Lordvessel | FogEntranceTags.Major | FogEntranceTags.Pvp
                | FogEntranceTags.Unused | FogEntranceTags.World;
            foreach (FogProjectEntrancePolicyV1 policy in entries)
            {
                if (policy == null || string.IsNullOrWhiteSpace(policy.Id) || !ids.Add(policy.Id))
                    throw new InvalidDataException("Project Entrance IDs must be nonempty and unique.");
                if (!Enum.IsDefined(typeof(FogProjectEntranceKind), policy.Kind)
                    || (policy.Tags & ~allowedEntranceTags) != 0
                    || policy.SourceArea == policy.DestinationArea
                    || string.IsNullOrWhiteSpace(policy.Rationale))
                    throw new InvalidDataException($"Project Entrance '{policy.Id}' has invalid kind, sides, tags, or rationale.");
                if (!FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.SourceArea)
                    || !FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.DestinationArea))
                    throw new KeyNotFoundException($"Project Entrance '{policy.Id}' refers to an unknown logical Area.");
                FogMapId sourceMap = FogProjectAnnotationPolicy.ResolveAreaMap(policy.SourceArea);
                FogMapId destinationMap = FogProjectAnnotationPolicy.ResolveAreaMap(policy.DestinationArea);
                if (sourceMap != policy.Map && destinationMap != policy.Map)
                    throw new InvalidDataException($"Project Entrance '{policy.Id}' does not touch its physical anchor map.");
                if (!facts.Maps.TryGetValue(policy.Map, out FogDsrFacts.Map map)
                    || !facts.Maps.ContainsKey(destinationMap))
                    throw new KeyNotFoundException($"Project Entrance '{policy.Id}' refers to a missing pristine DSR map.");
                if (policy.ObjectEntityId <= 0)
                    throw new InvalidDataException($"Project Entrance '{policy.Id}' must identify a positive DSR object entity.");
                FogDsrObjectFact[] objects = map.ObjectDetails.Where(value => value.EntityId == policy.ObjectEntityId
                    && (string.IsNullOrWhiteSpace(policy.ObjectPartName)
                        || string.Equals(value.Name, policy.ObjectPartName, StringComparison.Ordinal))).ToArray();
                if (objects.Length != 1)
                    throw new InvalidDataException($"Project Entrance '{policy.Id}' object anchor does not resolve uniquely in pristine DSR.");
                int[] regionIds = new[]
                {
                    policy.ActionRegionEntityId,
                    policy.ASideActionRegionEntityId,
                    policy.BSideActionRegionEntityId,
                    policy.BossTriggerRegionEntityId,
                }.Where(value => value.HasValue).Select(value => value.Value).ToArray();
                bool regionResolved = regionIds.Distinct().Count() == regionIds.Length
                    && regionIds.All(regionId => map.RegionDetails.Count(region => region.EntityId == regionId) == 1);
                bool eventResolved = !policy.InteractionEventId.HasValue
                    || map.EventIds.Contains(policy.InteractionEventId.Value);
                if (!regionResolved || !eventResolved)
                    throw new InvalidDataException($"Project Entrance '{policy.Id}' region/event anchor does not resolve in pristine DSR.");

                bool hasItemName = policy.RequiredItemName.HasValue;
                bool hasItemId = policy.RequiredItem.HasValue;
                if (hasItemName != hasItemId)
                    throw new InvalidDataException($"Project Entrance '{policy.Id}' must author both required-item identity fields or neither.");
                long? itemCheckEventId = policy.RequiredItemCheckEventId ?? policy.InteractionEventId;
                bool conditionResolved = !hasItemId;
                if (hasItemId)
                {
                    conditionResolved = facts.GameItemNames.TryGetValue(policy.RequiredItem.Value, out string itemName)
                        && facts.GameItemIds.Contains(policy.RequiredItem.Value)
                        && itemCheckEventId.HasValue
                        && map.EventIds.Contains(itemCheckEventId.Value)
                        && facts.ItemPossessionChecks.Any(check => check.Map == policy.Map
                            && check.EventId == itemCheckEventId && check.Item == policy.RequiredItem.Value)
                        && (policy.RequiredItemName.HasValue
                            && string.Equals(policy.RequiredItemName.Value.Value, itemName.Replace(" ", "_", StringComparison.Ordinal).ToLowerInvariant(), StringComparison.Ordinal));
                    // Stable project item IDs may use a curated semantic slug; the exact GameParam
                    // identity and event check above are authoritative when the display name differs.
                    if (!conditionResolved && policy.RequiredItemName.HasValue)
                        conditionResolved = facts.GameItemNames.ContainsKey(policy.RequiredItem.Value)
                            && itemCheckEventId.HasValue
                            && map.EventIds.Contains(itemCheckEventId.Value)
                            && facts.ItemPossessionChecks.Any(check => check.Map == policy.Map
                                && check.EventId == itemCheckEventId && check.Item == policy.RequiredItem.Value);
                    if (!conditionResolved)
                        throw new InvalidDataException($"Project Entrance '{policy.Id}' required-item condition is not checked by its anchored pristine DSR event.");
                }
                int[] entryFlags = new[] { policy.ASideEntryFlag, policy.BSideEntryFlag }.Where(value => value.HasValue).Select(value => value.Value).ToArray();
                if (entryFlags.Any(flag => flag <= 0 || !map.EventFlagWrites.Any(write => write.FlagId == flag)))
                    throw new InvalidDataException($"Project Entrance '{policy.Id}' has an entry flag that is not written by its pristine map event facts.");
                if (policy.Kind == FogProjectEntranceKind.FixedKeyDoor
                    && (!policy.Tags.HasFlag(FogEntranceTags.Door) || !hasItemId))
                    throw new InvalidDataException($"Fixed key-door Entrance '{policy.Id}' requires the Door tag and an anchored item condition.");
                FogTriggerVolume bossTriggerArea = null;
                int bossTriggerEventId = 0;
                if (policy.Kind == FogProjectEntranceKind.BossBoundary)
                {
                    FogBossAreaTagPolicy boss = FogProjectAnnotationPolicy.BossAreaTagPolicies.SingleOrDefault(value => value.Area == policy.DestinationArea);
                    if (!policy.Tags.HasFlag(FogEntranceTags.Boss) || boss == null || !policy.BossTriggerRegionEntityId.HasValue)
                        throw new InvalidDataException($"Boss boundary '{policy.Id}' must target a project-owned boss Area and physical trigger region.");
                    FogDsrFacts.Map bossMap = facts.Maps[boss.Map];
                    if (!bossMap.EventIds.Contains(boss.BossEventId) || boss.BossEventId > int.MaxValue)
                        throw new InvalidDataException($"Boss boundary '{policy.Id}' completion event {boss.BossEventId} is absent from its pristine boss map.");
                    FogDsrRegionFact trigger = map.RegionDetails.Single(region => region.EntityId == policy.BossTriggerRegionEntityId.Value);
                    FogDsrRegionShape shape = trigger.Shape;
                    if (shape.Kind != FogDsrRegionShapeKind.Box
                        || !shape.Width.HasValue || !shape.Height.HasValue || !shape.Depth.HasValue
                        || shape.Width <= 0 || shape.Height <= 0 || shape.Depth <= 0
                        || !float.IsFinite(trigger.Transform.X) || !float.IsFinite(trigger.Transform.Y)
                        || !float.IsFinite(trigger.Transform.Z) || !float.IsFinite(trigger.Transform.Yaw))
                        throw new InvalidDataException($"Boss boundary '{policy.Id}' trigger region {trigger.EntityId} is not a finite positive box.");
                    bossTriggerArea = new FogTriggerVolume(trigger.Transform.X, trigger.Transform.Y, trigger.Transform.Z,
                        trigger.Transform.Yaw, shape.Width.Value, shape.Height.Value, shape.Depth.Value);
                    bossTriggerEventId = checked((int)boss.BossEventId);
                }
                if (policy.Kind == FogProjectEntranceKind.TraversableFog
                    && (!policy.Tags.HasFlag(FogEntranceTags.World)
                        || !policy.ASideActionRegionEntityId.HasValue
                        || !policy.BSideActionRegionEntityId.HasValue))
                    throw new InvalidDataException($"Traversable fog Entrance '{policy.Id}' requires the World tag and both physical action regions.");
                if (policy.Kind == FogProjectEntranceKind.LordvesselGate
                    && (!policy.Tags.HasFlag(FogEntranceTags.Door) || !policy.Tags.HasFlag(FogEntranceTags.Lordvessel)))
                    throw new InvalidDataException($"Lordvessel gate '{policy.Id}' requires both Door and Lordvessel tags.");

                resolved.Add(new FogProjectEntranceResolution(policy, objects[0].Name, true, regionResolved,
                    eventResolved, conditionResolved, bossTriggerArea, bossTriggerEventId));
            }
            return Array.AsReadOnly(resolved.ToArray());
        }

        /// <summary>Resolves project world-warps against literal pristine EMEVD relocations and destination MSB regions.</summary>
        public static IReadOnlyList<FogWarp> ResolveWarpPoliciesV1(
            FogDsrFacts facts,
            IEnumerable<FogProjectWarpPolicyV1> policies = null)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Project Warp policies require cutscene and map facts from a clean DSR game directory.");

            FogProjectWarpPolicyV1[] entries = (policies ?? FogProjectAnnotationPolicy.WarpPoliciesV1).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var destinationIds = new HashSet<(FogMapId Map, int EntityId)>();
            var resolved = new List<FogWarp>(entries.Length);
            foreach (FogProjectWarpPolicyV1 policy in entries)
            {
                if (policy == null || string.IsNullOrWhiteSpace(policy.Id) || !ids.Add(policy.Id))
                    throw new InvalidDataException("Project Warp IDs must be nonempty and unique.");
                if (policy.SourceArea == policy.DestinationArea || policy.SourceEventId <= 0
                    || policy.CutsceneId <= 0 || policy.PlaybackType < 0
                    || policy.DestinationRegionEntityId <= 0 || string.IsNullOrWhiteSpace(policy.Rationale))
                    throw new InvalidDataException($"Project Warp '{policy.Id}' has invalid sides, source event, destination facts, or rationale.");
                if (!FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.SourceArea)
                    || !FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.DestinationArea))
                    throw new KeyNotFoundException($"Project Warp '{policy.Id}' refers to an unknown logical Area.");
                if (FogProjectAnnotationPolicy.ResolveAreaMap(policy.SourceArea) != policy.SourceMap
                    || FogProjectAnnotationPolicy.ResolveAreaMap(policy.DestinationArea) != policy.DestinationMap)
                    throw new InvalidDataException($"Project Warp '{policy.Id}' source/destination Areas do not belong to their pristine maps.");
                if (!facts.Maps.TryGetValue(policy.SourceMap, out FogDsrFacts.Map sourceMap)
                    || !facts.Maps.TryGetValue(policy.DestinationMap, out FogDsrFacts.Map destinationMap))
                    throw new KeyNotFoundException($"Project Warp '{policy.Id}' refers to a missing pristine DSR map.");
                if (!sourceMap.EventIds.Contains(policy.SourceEventId))
                    throw new InvalidDataException($"Project Warp '{policy.Id}' source event {policy.SourceEventId} is absent from pristine map {policy.SourceMap}.");
                FogDsrCutsceneWarpFact[] matchingRelocations = sourceMap.CutsceneWarps.Where(fact =>
                    fact.EventId == policy.SourceEventId
                    && fact.CutsceneId == policy.CutsceneId
                    && fact.PlaybackType == policy.PlaybackType
                    && fact.DestinationMap == policy.DestinationMap
                    && fact.DestinationEntityId == policy.DestinationRegionEntityId).ToArray();
                if (matchingRelocations.Length != 1 || matchingRelocations[0].HasEventParameterBindings)
                    throw new InvalidDataException($"Project Warp '{policy.Id}' does not resolve to one unparameterized pristine cutscene relocation with the authored destination.");
                if (!destinationMap.RegionEntityIds.Contains(policy.DestinationRegionEntityId)
                    || destinationMap.RegionDetails.Count(region => region.EntityId == policy.DestinationRegionEntityId) != 1)
                    throw new InvalidDataException($"Project Warp '{policy.Id}' destination region {policy.DestinationRegionEntityId} does not resolve uniquely in pristine map {policy.DestinationMap}.");
                if (!destinationIds.Add((policy.DestinationMap, policy.DestinationRegionEntityId)))
                    throw new InvalidDataException($"Project Warp destination {policy.DestinationMap}/{policy.DestinationRegionEntityId} is assigned more than once.");

                resolved.Add(new FogWarp(
                    policy.Id,
                    policy.DestinationRegionEntityId,
                    policy.SourceMap,
                    policy.Rationale,
                    FogWarpTags.None,
                    new FogWarpSide(policy.SourceArea, 0, null, null, policy.SourceMap, FogWarpSideTags.None),
                    new FogWarpSide(policy.DestinationArea, policy.CutsceneId, null, null, policy.DestinationMap, FogWarpSideTags.None)));
            }

            var pairs = entries.GroupBy(policy => string.CompareOrdinal(policy.SourceArea.Value, policy.DestinationArea.Value) < 0
                ? (policy.SourceArea, policy.DestinationArea)
                : (policy.DestinationArea, policy.SourceArea));
            foreach (IGrouping<(FogAreaId, FogAreaId), FogProjectWarpPolicyV1> pair in pairs)
            {
                FogProjectWarpPolicyV1[] directions = pair.ToArray();
                if (directions.Length != 2
                    || directions.Select(policy => policy.SourceArea).Distinct().Count() != 2
                    || !directions.Select(policy => policy.SourceArea).ToHashSet().SetEquals(new[] { pair.Key.Item1, pair.Key.Item2 }))
                    throw new InvalidDataException($"Project Warp connection '{pair.Key.Item1}' / '{pair.Key.Item2}' must have exactly two reciprocal physical directions.");
            }
            return Array.AsReadOnly(resolved.ToArray());
        }

        /// <summary>Resolves the Seath crystal's project-added, flag-triggered one-way warp.</summary>
        public static IReadOnlyList<FogWarp> ResolveFlagTriggeredWarpPoliciesV1(
            FogDsrFacts facts,
            IEnumerable<FogProjectFlagWarpPolicyV1> policies = null)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Flag-triggered Warp policies require map and region facts from a clean DSR game directory.");

            FogProjectFlagWarpPolicyV1[] entries = (policies ?? FogProjectAnnotationPolicy.FlagWarpPoliciesV1).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var runtimeIds = new HashSet<int>();
            var resolved = new List<FogWarp>(entries.Length);
            foreach (FogProjectFlagWarpPolicyV1 policy in entries)
            {
                if (policy == null || string.IsNullOrWhiteSpace(policy.Id) || !ids.Add(policy.Id))
                    throw new InvalidDataException("Flag-triggered project Warp IDs must be nonempty and unique.");
                if (policy.SourceArea == policy.DestinationArea || policy.ActivationEventId <= 0
                    || policy.ActivationObjectEntityId <= 0 || policy.ActivationFlagId <= 0
                    || string.IsNullOrWhiteSpace(policy.DestinationRegionName)
                    || policy.PristineDestinationRegionEntityId != -1 || policy.RuntimeDestinationRegionEntityId <= 0
                    || policy.DestinationPosition == null || string.IsNullOrWhiteSpace(policy.Rationale))
                    throw new InvalidDataException($"Flag-triggered project Warp '{policy.Id}' has invalid activation, region, or rationale data.");
                if (!FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.SourceArea)
                    || !FogProjectAnnotationPolicy.AreaIdentityOrder.Contains(policy.DestinationArea))
                    throw new KeyNotFoundException($"Flag-triggered project Warp '{policy.Id}' refers to an unknown logical Area.");
                if (FogProjectAnnotationPolicy.ResolveAreaMap(policy.SourceArea) != policy.Map
                    || FogProjectAnnotationPolicy.ResolveAreaMap(policy.DestinationArea) != policy.Map)
                    throw new InvalidDataException($"Flag-triggered project Warp '{policy.Id}' source and destination Areas must both belong to its pristine map.");
                if (policy.ActivationEventId != FogAddedEventBuilder.SeathCrystalInteractionEventId
                    || policy.ActivationObjectEntityId != FogAddedEventBuilder.SeathCrystalObjectEntityId
                    || policy.ActivationFlagId != FogAddedEventBuilder.SeathCrystalActivatedFlag
                    || policy.RuntimeDestinationRegionEntityId != FogProjectAnnotationPolicy.SeathPrisonWarpRuntimeId)
                    throw new InvalidDataException($"Flag-triggered project Warp '{policy.Id}' is not tied to the generated Seath crystal activation and pinned writer destination.");
                if (!runtimeIds.Add(policy.RuntimeDestinationRegionEntityId))
                    throw new InvalidDataException($"Flag-triggered project Warp runtime destination {policy.RuntimeDestinationRegionEntityId} is assigned more than once.");
                if (!facts.Maps.TryGetValue(policy.Map, out FogDsrFacts.Map map))
                    throw new KeyNotFoundException($"Flag-triggered project Warp '{policy.Id}' refers to missing pristine DSR map '{policy.Map}'.");

                FogDsrRegionFact[] destinationRegions = map.RegionDetails.Where(region =>
                    string.Equals(region.Name, policy.DestinationRegionName, StringComparison.Ordinal)).ToArray();
                if (destinationRegions.Length != 1
                    || destinationRegions[0].EntityId != policy.PristineDestinationRegionEntityId
                    || destinationRegions[0].Shape.Kind != FogDsrRegionShapeKind.Point
                    || !PositionNear(destinationRegions[0].Transform, policy.DestinationPosition, 0.01f)
                    || map.RegionDetails.Any(region => region.EntityId == policy.RuntimeDestinationRegionEntityId
                        && !string.Equals(region.Name, policy.DestinationRegionName, StringComparison.Ordinal)))
                    throw new InvalidDataException($"Flag-triggered project Warp '{policy.Id}' destination region '{policy.DestinationRegionName}' does not match the unique pristine DSR remap anchor.");

                var activationEvent = FogAddedEventBuilder.BuildEvent11700005();
                bool setsActivationFlag = activationEvent.ID == policy.ActivationEventId
                    && activationEvent.Instructions.Count(instruction => instruction.Bank == 2003 && instruction.ID == 2
                        && instruction.ArgData.Length >= 5
                        && BitConverter.ToInt32(instruction.ArgData, 0) == policy.ActivationFlagId
                        && instruction.ArgData[4] == 1) == 1;
                bool interactsWithSourceObject = activationEvent.Instructions.Any(instruction =>
                    instruction.Bank == 3 && instruction.ID == 5
                    && ContainsInt32(instruction.ArgData, policy.ActivationObjectEntityId));
                if (!setsActivationFlag || !interactsWithSourceObject)
                    throw new InvalidDataException($"Flag-triggered project Warp '{policy.Id}' is not backed by the generated crystal interaction event, object, and activation flag.");

                resolved.Add(new FogWarp(
                    null,
                    policy.RuntimeDestinationRegionEntityId,
                    policy.Map,
                    "To Duke's Archives prison",
                    FogWarpTags.Unique,
                    new FogWarpSide(policy.SourceArea, 0, null, null, policy.Map, FogWarpSideTags.None, policy.ActivationFlagId),
                    new FogWarpSide(policy.DestinationArea, 0, null, null, policy.Map, FogWarpSideTags.None)));
            }
            return Array.AsReadOnly(resolved.ToArray());
        }

        // The point's original yaw remains in the pristine MSB and is consumed by the writer.
        // The compact policy anchors the stable 3D destination position without replacing that rotation.
        private static bool PositionNear(FogDsrTransform actual, FogDsrTransform expected, float tolerance) =>
            actual != null && expected != null
            && Math.Abs(actual.X - expected.X) <= tolerance
            && Math.Abs(actual.Y - expected.Y) <= tolerance
            && Math.Abs(actual.Z - expected.Z) <= tolerance;

        private static bool ContainsInt32(byte[] bytes, int value)
        {
            if (bytes == null || bytes.Length < sizeof(int)) return false;
            byte[] expected = BitConverter.GetBytes(value);
            for (int offset = 0; offset <= bytes.Length - expected.Length; offset++)
            {
                if (bytes[offset] == expected[0] && bytes[offset + 1] == expected[1]
                    && bytes[offset + 2] == expected[2] && bytes[offset + 3] == expected[3]) return true;
            }
            return false;
        }

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
                if (!facts.Maps.ContainsKey(sourceMap) || !facts.Maps.ContainsKey(destinationMap))
                    throw new KeyNotFoundException($"Static area route '{policy.Source}' -> '{policy.Destination}' refers to a missing pristine endpoint map ('{sourceMap}' -> '{destinationMap}').");
                if (sourceMap != destinationMap && policy.Kind == FogAreaLinkKind.Static)
                {
                    if (string.IsNullOrWhiteSpace(policy.ConnectCollisionPartName))
                        throw new InvalidDataException($"Cross-map static route '{policy.Source}' -> '{policy.Destination}' must name its verified source ConnectCollision part.");
                    FogDsrConnectCollisionFact[] physicalParts = facts.Maps[sourceMap].ConnectCollisionDetails
                        .Where(part => string.Equals(part.PartName, policy.ConnectCollisionPartName, StringComparison.Ordinal)).ToArray();
                    if (physicalParts.Length != 1 || !ConnectCollisionTargetsMap(physicalParts[0], facts.Maps[destinationMap]))
                        throw new InvalidDataException($"Cross-map static route '{policy.Source}' -> '{policy.Destination}' does not resolve ConnectCollision '{policy.ConnectCollisionPartName}' to pristine target map '{facts.Maps[destinationMap].GameMapFileId}'.");
                }
                if (sourceMap == destinationMap && policy.Kind == FogAreaLinkKind.CrossMapGlitch)
                    throw new InvalidDataException($"Cross-map glitch route '{policy.Source}' -> '{policy.Destination}' does not cross map boundaries.");
                if (sourceMap == destinationMap && !string.IsNullOrWhiteSpace(policy.ConnectCollisionPartName))
                    throw new InvalidDataException($"Same-map static route '{policy.Source}' -> '{policy.Destination}' cannot use cross-map ConnectCollision anchor '{policy.ConnectCollisionPartName}'.");
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

        private static bool ConnectCollisionTargetsMap(FogDsrConnectCollisionFact part, FogDsrFacts.Map targetMap)
        {
            if (part == null || targetMap == null || part.TargetMapIdBytes == null || part.TargetMapIdBytes.Count != 4
                || string.IsNullOrWhiteSpace(targetMap.GameMapFileId)) return false;
            string fileId = targetMap.GameMapFileId;
            if (fileId.Length < 2 || fileId[0] != 'm') return false;
            string[] components = fileId.Substring(1).Split('_');
            if (components.Length != 4) return false;
            byte[] expected = new byte[4];
            for (int index = 0; index < components.Length; index++)
            {
                if (!byte.TryParse(components[index], NumberStyles.None, CultureInfo.InvariantCulture, out expected[index])) return false;
                byte actual = part.TargetMapIdBytes[index];
                // ConnectCollision uses 255 in block slots as a wildcard for any block in a map.
                if (actual == byte.MaxValue && index >= 2) continue;
                if (actual != expected[index]) return false;
            }
            return true;
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
                if (policy.BossCompletionEventId <= 0 || string.IsNullOrWhiteSpace(policy.Rationale))
                    throw new InvalidDataException($"Boss-defeat flag policy for '{policy.Map}/{policy.Collision}' must name a positive event anchor and rationale.");

                FogDsrCollisionFact collision = map.CollisionDetails.Single(value =>
                    string.Equals(value.Name, policy.Collision, StringComparison.Ordinal));

                HashSet<int> enemyEntityIds = map.EnemyDetails
                    .Where(enemy => string.Equals(enemy.CollisionName, policy.Collision, StringComparison.Ordinal) && enemy.EntityId > 0)
                    .Select(enemy => enemy.EntityId)
                    .ToHashSet();
                bool hasCollisionBossEnemyAnchor = map.BossDefeatHandlers
                    .Where(handler => enemyEntityIds.Contains(handler.EnemyEntityId)
                        && handler.EventId > 0 && handler.EventId <= int.MaxValue
                        && facts.BossRewardCompletionFlags.Contains((int)handler.EventId))
                    .Any(handler => handler.EventId == policy.BossCompletionEventId);
                long encodedEventId = -(long)collision.PlayRegionId - 10;
                bool hasEncodedPlayRegionAnchor = encodedEventId == policy.BossCompletionEventId
                    && facts.BossRewardCompletionFlags.Contains(policy.BossCompletionEventId)
                    && map.BossDefeatHandlers.Any(handler => handler.EventId == policy.BossCompletionEventId);
                bool anchorsSatisfied = policy.AnchorKind switch
                {
                    FogBossFlagAnchorKind.CollisionBossEnemy => hasCollisionBossEnemyAnchor,
                    FogBossFlagAnchorKind.EncodedNegativePlayRegion => hasEncodedPlayRegionAnchor,
                    FogBossFlagAnchorKind.Both => hasCollisionBossEnemyAnchor && hasEncodedPlayRegionAnchor,
                    _ => false,
                };
                if (!anchorsSatisfied)
                    throw new InvalidDataException($"The authored {policy.AnchorKind} anchor for '{policy.Map}/{policy.Collision}' does not resolve to completion event {policy.BossCompletionEventId} in pristine DSR facts.");

                int[] matchingFlags = map.BossDefeatHandlers
                    .Where(handler => enemyEntityIds.Contains(handler.EnemyEntityId)
                        && handler.EventId > 0 && handler.EventId <= int.MaxValue
                        && facts.BossRewardCompletionFlags.Contains((int)handler.EventId))
                    .Select(handler => (int)handler.EventId)
                    .Concat(hasEncodedPlayRegionAnchor ? new[] { policy.BossCompletionEventId } : Array.Empty<int>())
                    .Distinct()
                    .ToArray();
                if (matchingFlags.Length != 1 || matchingFlags[0] != policy.BossCompletionEventId)
                    throw new InvalidDataException($"Expected only authored boss completion event {policy.BossCompletionEventId} for '{policy.Map}/{policy.Collision}' from pristine EMEVD/MSB; found [{string.Join(", ", matchingFlags)}].");
                output.Add(new FogDefaultFlagCollision(policy.Map, policy.Collision, policy.BossCompletionEventId));
            }
            return Array.AsReadOnly(output.ToArray());
        }

        /// <summary>Resolves exact item identity and interaction anchors without assigning placement or randomization tags.</summary>
        public static IReadOnlyList<FogKeyItemIdentity> ResolveKeyItemIdentities(
            FogDsrFacts facts,
            IEnumerable<FogKeyItemIdentityPolicy> policies)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (policies == null) throw new ArgumentNullException(nameof(policies));
            if (facts.EventFactSource != FogDsrEventFactSource.PristineGameInstall)
                throw new InvalidOperationException("Key-item identity anchors require EMEVD facts from a clean DSR game directory.");

            var resolved = new List<FogKeyItemIdentity>();
            var names = new HashSet<FogItemName>();
            var gameItems = new HashSet<FogGameItemId>();
            foreach (FogKeyItemIdentityPolicy policy in policies)
            {
                if (policy == null) throw new InvalidDataException("A key-item identity policy entry cannot be null.");
                if (string.IsNullOrWhiteSpace(policy.Name.Value) || string.IsNullOrWhiteSpace(policy.EnglishGameName)
                    || policy.GameParamId <= 0 || !Enum.IsDefined(typeof(FogGameItemCategory), policy.Category)
                    || string.IsNullOrWhiteSpace(policy.Rationale))
                    throw new InvalidDataException("A key-item identity policy must have a stable name, valid category/ID, English name, and rationale.");
                if (!names.Add(policy.Name)) throw new InvalidDataException($"Duplicate key-item identity name '{policy.Name}'.");
                FogGameItemId gameItem = new FogGameItemId((int)policy.Category, policy.GameParamId);
                if (!gameItems.Add(gameItem)) throw new InvalidDataException($"Duplicate key-item identity GameParam ID '{gameItem}'.");
                if (!facts.GameItemIds.Contains(gameItem)
                    || !facts.GameItemNames.TryGetValue(gameItem, out string englishName)
                    || !string.Equals(englishName, policy.EnglishGameName, StringComparison.Ordinal))
                    throw new InvalidDataException($"Authored key-item identity '{policy.Name}' does not match pristine DSR GameParam/FMG item {gameItem}.");
                if (policy.InteractionAnchors == null || policy.InteractionAnchors.Count == 0)
                    throw new InvalidDataException($"Key-item identity '{policy.Name}' has no pristine interaction anchors.");
                var anchorKeys = new HashSet<(FogMapId Map, long EventId)>();
                foreach (FogKeyItemInteractionAnchor anchor in policy.InteractionAnchors)
                {
                    if (anchor == null || string.IsNullOrWhiteSpace(anchor.Mechanic) || !anchorKeys.Add((anchor.Map, anchor.EventId)))
                        throw new InvalidDataException($"Key-item identity '{policy.Name}' has an empty or duplicate interaction anchor.");
                    if (!facts.Maps.ContainsKey(anchor.Map)
                        || !facts.ItemPossessionChecks.Any(check => check.Map == anchor.Map
                            && check.EventId == anchor.EventId && check.Item == gameItem))
                        throw new InvalidDataException($"Key-item identity '{policy.Name}' is not checked as {gameItem} by pristine event {anchor.Map}/{anchor.EventId}.");
                }
                resolved.Add(new FogKeyItemIdentity(policy.Name, gameItem, englishName,
                    Array.AsReadOnly(policy.InteractionAnchors.ToArray()), policy.Rationale));
            }
            return Array.AsReadOnly(resolved.ToArray());
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
