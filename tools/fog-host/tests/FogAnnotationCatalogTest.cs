using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FogHost;

namespace FogHost.Tests
{
    internal static class FogAnnotationCatalogTest
    {
        public static void VerifyProjection()
        {
            FogAnnotationCatalog catalog = Fixture();
            var projection = catalog.ToFogModAnnotations();
            Assert(projection.Options == null, "Unused Options must not be recreated.");
            Assert(projection.Areas.Select(area => area.Name).SequenceEqual(new[] { "start", "gate" }), "Area order was not preserved.");
            Assert(projection.Areas[0].To.Single().Area == "gate" && projection.Areas[1].ScalingBase == "start", "Project area IDs were not translated to FogMod graph keys.");
            Assert(projection.Areas[0].To.Single().Cond == "AND gate key_token", "Typed condition order was not projected intact.");
            Assert(projection.Areas[0].To.Single().Tags == "hard shortcut", "Only active area-link tags should be projected.");
            Assert(projection.KeyItems.Single().ID == "3:123", "Game item IDs must use FogMod's lot ID form.");
            Assert(projection.KeyItems.Single().Area == "gate", "Key item area references must use the FogMod area key.");
            Assert(projection.Warps.Single().ASide.Cutscene == 101 && projection.Warps.Single().ASide.WarpFlag == 5308
                && projection.Warps.Single().ASide.Tags == "temp", "Warp-only cutscene, activation flag and A-side tags must project from the typed warp model.");
            Assert(projection.Warps.Single().BSide.Tags == "player" && projection.Warps.Single().BSide.Flag == 0, "Warp destinations must not expose ordinary entrance flags.");
            Assert(projection.Warps.Single().ASide.Area == "start" && projection.Warps.Single().BSide.Area == "gate", "Warp-side areas must use FogMod graph keys.");
            Assert(projection.Entrances.Single().DoorCond == "gate"
                && projection.Entrances.Single().ASide.Area == "start"
                && projection.Entrances.Single().ASide.Cond == "start"
                && projection.Entrances.Single().BSide.Area == "gate", "Entrance and condition area references must use FogMod graph keys.");
            Assert(projection.Enemies.Single().Area == "gate", "Enemy area references must use the FogMod area key.");
            Assert(projection.CustomStarts.Single().Name == "Depths Bonfire", "The project-owned custom-start display name must reach FogMod's in-game return message.");
            Assert(projection.CustomStarts.Single().Area == "start", "Custom-start area references must use the FogMod area key.");
            Assert(projection.CustomStarts.Single().Respawn == "depths 1000", "Typed respawn references must be projected consistently.");
            Assert(projection.DefaultCost.Keys.Single() == "start" && projection.LotLocations[1300020] == "gate", "Cost and lot area references must use the FogMod area key.");
            Assert(projection.CompactLotLocations == null, "Legacy catalogs must retain consecutive-lot behavior unless Compact exact rows are explicitly supplied.");
            Assert(catalog.Fingerprint.Length == 64, "Catalog identity must include a SHA-256 fingerprint.");
            var exactLots = new FogAnnotationCatalog(catalog.Areas, catalog.KeyItems, catalog.Warps, catalog.Entrances,
                catalog.CustomStarts, catalog.DefaultCosts, catalog.Enemies, catalog.LotLocations, catalog.DefaultFlagCols,
                catalog.WriterSupportEntrances,
                new[] { new FogLotRowLocation(new FogItemLotId(1300021), new FogAreaId("project-gate")) });
            Assert(exactLots.ToFogModAnnotations().CompactLotLocations[1300021] == "gate"
                && exactLots.Fingerprint != catalog.Fingerprint,
                "Explicit Compact row ownership must be projected exactly and included in catalog identity.");
            FogArea[] remappedAreas = catalog.Areas.Select(area => area.Id.Value == "project-start"
                ? area with { Maps = new[] { new FogMapId("parish") } }
                : area).ToArray();
            var remapped = new FogAnnotationCatalog(remappedAreas, catalog.KeyItems, catalog.Warps, catalog.Entrances,
                catalog.CustomStarts, catalog.DefaultCosts, catalog.Enemies, catalog.LotLocations, catalog.DefaultFlagCols);
            Assert(catalog.Fingerprint != remapped.Fingerprint, "The built-in profile identity must include project-owned area/map membership.");

            FogArea[] renamedProjection = catalog.Areas.Select(area => area.Id.Value == "project-start"
                ? area with { FogModName = new FogModAreaName("other-start") }
                : area).ToArray();
            var changedProjection = new FogAnnotationCatalog(renamedProjection, catalog.KeyItems, catalog.Warps, catalog.Entrances,
                catalog.CustomStarts, catalog.DefaultCosts, catalog.Enemies, catalog.LotLocations, catalog.DefaultFlagCols);
            Assert(catalog.Fingerprint != changedProjection.Fingerprint, "The profile identity must include project-to-FogMod area name mapping.");

            FogCustomStart[] renamedStart = catalog.CustomStarts.Select(start => start with { DisplayName = "Different project label" }).ToArray();
            var changedStartLabel = new FogAnnotationCatalog(catalog.Areas, catalog.KeyItems, catalog.Warps, catalog.Entrances,
                renamedStart, catalog.DefaultCosts, catalog.Enemies, catalog.LotLocations, catalog.DefaultFlagCols);
            Assert(catalog.Fingerprint != changedStartLabel.Fingerprint
                && FogAnnotationFingerprint.Compute(catalog.ToFogModAnnotations()) != FogAnnotationFingerprint.Compute(changedStartLabel.ToFogModAnnotations()),
                "Custom-start display labels must be included in both project and active-consumed fingerprints.");

            FogAnnotationCatalog withoutStartException = StartSemanticsFixture(FogCustomStartSemantics.None);
            FogAnnotationCatalog withStartException = StartSemanticsFixture(FogCustomStartSemantics.BypassDepthsEntryKeyDoor);
            Assert(withoutStartException.Fingerprint != withStartException.Fingerprint,
                "Project-owned custom-start progression semantics must be covered by the catalog fingerprint.");
            Assert(FogAnnotationFingerprint.Compute(withoutStartException.ToFogModAnnotations())
                    == FogAnnotationFingerprint.Compute(withStartException.ToFogModAnnotations()),
                "The Depths start exception is project policy and must not alter the FogMod DTO projection.");

            var invalidStartRuleRejected = false;
            try
            {
                _ = new FogAnnotationCatalog(
                    new[] { new FogArea(new FogAreaId("project-start"), new FogModAreaName("start"), new[] { new FogMapId("depths") }, FogAreaTags.None, null, Array.Empty<FogAreaLink>()) },
                    Array.Empty<FogKeyItem>(), Array.Empty<FogWarp>(), Array.Empty<FogEntrance>(),
                    new[] { new FogCustomStart("invalid-depths-start", "Depths Bonfire", new FogAreaId("project-start"), new FogRespawn(new FogMapId("depths"), new FogSpawnPointId(1000)), FogCustomStartSemantics.BypassDepthsEntryKeyDoor) },
                    Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(), Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());
            }
            catch (InvalidDataException) { invalidStartRuleRejected = true; }
            Assert(invalidStartRuleRejected, "The Depths key-door exception must not be attached to a different logical area.");
        }

        public static void VerifyRejectsUnknownReferences()
        {
            var exception = false;
            try
            {
                new FogAnnotationCatalog(
                    new[] { new FogArea(new FogAreaId("start"), new FogModAreaName("start"), new[] { new FogMapId("depths") }, FogAreaTags.None, null, new[]
                    {
                        new FogAreaLink(new FogAreaId("missing"), null, FogAreaLinkTags.None),
                    }) },
                    new FogKeyItem[0], new FogWarp[0], new FogEntrance[0], new FogCustomStart[0],
                    new FogDefaultCost[0], new FogEnemyScalingRule[0], new FogLotLocation[0], new FogDefaultFlagCollision[0]);
            }
            catch (InvalidDataException) { exception = true; }
            Assert(exception, "An unknown graph destination must fail catalog validation.");
        }

        private static FogAnnotationCatalog Fixture() => new FogAnnotationCatalog(
            new[]
            {
                new FogArea(new FogAreaId("project-start"), new FogModAreaName("start"), new[] { new FogMapId("depths") }, FogAreaTags.Trivial, null, new[]
                {
                    new FogAreaLink(new FogAreaId("project-gate"), new FogCondition(FogConditionKind.All, new[]
                    {
                        FogConditionReference.Area(new FogAreaId("project-gate")),
                        FogConditionReference.KeyItem(new FogItemName("key_token")),
                    }), FogAreaLinkTags.Hard | FogAreaLinkTags.Shortcut),
                }),
                new FogArea(new FogAreaId("project-gate"), new FogModAreaName("gate"), new[] { new FogMapId("depths") }, FogAreaTags.Boss, new FogAreaId("project-start"), new FogAreaLink[0]),
            },
            new[] { new FogKeyItem(new FogItemName("key_token"), new FogGameItemId(3, 123), new FogAreaId("project-gate"), FogKeyItemTags.RandomOnly) },
            new[]
            {
                new FogWarp("home-warp", 2000, new FogMapId("depths"), "Test warp", FogWarpTags.Unique,
                    new FogWarpSide(new FogAreaId("project-start"), 101, null, null, null, FogWarpSideTags.Temporary, WarpFlag: 5308),
                    new FogWarpSide(new FogAreaId("project-gate"), 0, null, null, null, FogWarpSideTags.Player)),
            }, new[]
            {
                new FogEntrance("projected-door", 3000, new FogMapId("depths"), "Test door",
                    new FogCondition(FogConditionKind.Atom, new[] { FogConditionReference.Area(new FogAreaId("project-gate")) }),
                    FogEntranceTags.Door,
                    new FogEntranceSide(new FogAreaId("project-start"), 0, 0, 0, 0, 0, null, 0,
                        new FogCondition(FogConditionKind.Atom, new[] { FogConditionReference.Area(new FogAreaId("project-start")) }),
                        null, 0, 0, null, null, FogEntranceSideTags.None),
                    new FogEntranceSide(new FogAreaId("project-gate"), 0, 0, 0, 0, 0, null, 0, null,
                        null, 0, 0, null, null, FogEntranceSideTags.None)),
            },
            new[] { new FogCustomStart("start-test", "Depths Bonfire", new FogAreaId("project-start"), new FogRespawn(new FogMapId("depths"), new FogSpawnPointId(1000))) },
            new[] { new FogDefaultCost(new FogAreaId("project-start"), 0.25f) },
            new[] { new FogEnemyScalingRule(new FogMapId("depths"), "", new FogAreaId("project-gate"), new string[0]) },
            new[] { new FogLotLocation(new FogItemLotId(1300020), new FogAreaId("project-gate")) },
            new FogDefaultFlagCollision[0]);

        private static FogAnnotationCatalog StartSemanticsFixture(FogCustomStartSemantics semantics) => new FogAnnotationCatalog(
            new[] { new FogArea(new FogAreaId("depths"), new FogModAreaName("depths"), new[] { new FogMapId("depths") }, FogAreaTags.None, null, Array.Empty<FogAreaLink>()) },
            Array.Empty<FogKeyItem>(), Array.Empty<FogWarp>(), Array.Empty<FogEntrance>(),
            new[] { new FogCustomStart("depths-start", "The Depths", new FogAreaId("depths"), new FogRespawn(new FogMapId("depths"), new FogSpawnPointId(1000)), semantics) },
            Array.Empty<FogDefaultCost>(), Array.Empty<FogEnemyScalingRule>(), Array.Empty<FogLotLocation>(), Array.Empty<FogDefaultFlagCollision>());

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new System.InvalidOperationException(message);
        }
    }
}
