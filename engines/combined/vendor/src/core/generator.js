// DS1 Triple Randomizer local modification (GPL-3.0-only section 5(a)):
// legacy-parity options fashionSouls, replaceMimics,
// replaceRespawningMosquitoes and preventSameEnemy (Batch 1, 2026-10-05, diff
// in patches/0002-legacy-parity-batch-1.patch) and soulItems, startingItems,
// pinwheelChaos and gwynNerf (Batch 2, diff in
// patches/0003-legacy-parity-batch-2.patch) and secondGargoyle (Batch 3, diff
// in patches/0004-legacy-parity-second-gargoyle.patch). With every option at
// its engine default the output is identical to the unmodified upstream core.
import { createHash } from "node:crypto";
import { createStream } from "./rng.js";
import { normalizeConfig, validateConfig } from "./config.js";
import {
  EnemyPlacementUnavailableError,
  BOSS_SAMPLING_MODES,
  ROAMING_BOSS_MODES,
  assertSlotOwnedDrops,
  createPlacementDiagnostics,
  permutationDiagnostics,
  resolveEnemyPlacementAlgorithm,
  resolveEnemyPlacementOptions,
} from "./placement.js";
import { bossDestinationClass, deriveBodyTypes, deriveRegularClassTable, deriveRoamingBossTypes, planRoamingBosses, planSampledBosses, planSampledRegularEnemies } from "./sampling.js";
import {
  enemies,
  enemySlots,
  bosses,
  bossSlots,
  items,
  itemLocations,
} from "../data/prototype-data.js";

const byId = (values) => new Map(values.map((value) => [value.id, value]));
const dragonBodyModels = new Set([
  "c2300", // Titanite Demon
  "c2730", // Crossbreed Priscilla
  "c3420", // Undead Dragon
  "c3421", // Bounding Demon / Undead Dragon legs
  "c3430", // Hellkite Drake
  "c3520", // Drake
  "c5351", // Anor Londo Gargoyle (ordinary enemy)
  "c4510", // Black Dragon Kalameet
  "c5260", // Gaping Dragon
  "c5290", // Seath the Scaleless
]);
const linkedPartModels = new Set([
  "c2731", // Priscilla's tail
  "c3422", // Undead Dragon wing
  "c3431", // Hellkite tail
  "c3451", // Everlasting Dragon tail; its friendly parent stays protected
  "c3472", // Sanctuary Guardian tail
  "c3531", // Hydra heads
  "c4511", // Kalameet tail
  "c5201", "c5202", // Centipede Demon limbs
  "c5261", // Gaping Dragon tail
  "c5291", // Seath's tail
  "c5352", "c5353", // Gargoyle tails
  "c5400", "c5401", // Bed of Chaos linked encounter parts
]);
const internalHelperModels = new Set([
  "c3510", // Asylum transport crow
  "c3511", // Alternate transport-crow dummy
  // Translucent Black Knight memories in the Kiln are scenery-only actors.
  // Their NPC/AI parameter IDs look valid, but they have no combat lifecycle.
  "c2791",
  // Invisible Mass of Souls wisps are moved only by their explicit linked
  // group; standalone variants must never occupy an ordinary enemy slot.
  "c3501",
]);
const MAX_ENEMY_MODELS_PER_MAP = 30;
// Legacy Enemy Randomizer "Replace mimics": every Mimic slot (model c2780).
const mimicModel = "c2780";
// Legacy Enemy Randomizer "Respawning mosquito replacement": the six
// respawning Blighttown swamp mosquitoes (c3090_0058/0059/0085/0086/0090/0091).
const respawningMosquitoSlots = new Set(
  ["0058", "0059", "0085", "0086", "0090", "0091"].map(
    (index) => `m14_00_00_00:c3090_${index}`,
  ),
);
// Legacy "Pinwheel Chaos": the fight's main Pinwheel keeps its place and the
// fourteen clone bodies join the regular enemy pool.
const pinwheelMainSlot = "m13_00_00_00:c3320_0000";
const pinwheelCloneMap = "m13_00_00_00";
const pinwheelModel = "c3320";
// Legacy "Gwyn nerf": the chance that Lord Gwyn is rejected as a replacement.
const gwynNerfRejection = Object.freeze({ none: 0, medium: 0.6, strong: 0.85 });
const gwynModel = "c5370";
// Legacy "Replace second Gargoyle": the second Bell Gargoyle body. The
// legacy tool replaces exactly this part (`c5350_0001`, entity 1010801) as an
// independent boss; its release is scripted by event 11015396.
const secondGargoyleSlotId = "m10_01_00_00:c5350_0001";
const staticBridgeDragonSpawn = Object.freeze({
  x: 9.144,
  y: 9.55,
  z: -48.007,
  collisionName: "h1113B1",
});
const staticBridgeCombatSourceByModel = new Map([
  ["c2300", "m13_00_00_00:c2300_0000"],
  ["c3420", "m16_00_00_00:c3420_0000"],
  ["c3520", "m16_00_00_00:c3520_0007"],
]);
const dragonNames = new Map([
  ["c2300", "Titanite Demon"],
  ["c2730", "Crossbreed Priscilla"],
  ["c3420", "Undead Dragon"],
  ["c3421", "Bounding Demon"],
  ["c3430", "Hellkite Drake"],
  ["c3520", "Drake"],
  ["c5351", "Anor Londo Gargoyle"],
  ["c4510", "Black Dragon Kalameet"],
  ["c5260", "Gaping Dragon"],
  ["c5290", "Seath the Scaleless"],
]);
const additionalBossNames = new Map([
  ["c3230", "Moonlight Butterfly"],
  ["c5230", "Bed of Chaos"],
  // The visible c5230 tree is not damageable outside its scripted arena. The
  // c5401 chaos bug is the encounter's real vulnerable body and is therefore
  // the portable representation used by the randomizer.
  ["c5401", "Bed of Chaos"],
  ["c5250", "Ceaseless Discharge"],
  ["c5320", "Dark Sun Gwyndolin"],
]);
const bossArchetypeNames = new Map([
  ["c2360:236001", "Super Smough"],
  ["c5271:527100", "Super Ornstein"],
]);
const additionalBossModels = new Set(additionalBossNames.keys());
const nonBossEncounterSlots = new Set([
  // The Anor Londo gargoyles reuse a boss model/parameter family but are
  // ordinary enemies without an encounter health bar.
  "m15_01_00_00:c5351_0000",
  "m15_01_00_00:c5351_0001",
  // Crystal Cave butterflies share the Moonlight Butterfly model family, but
  // they are ordinary patrol enemies and should remain in the enemy pool.
  "m17_00_00_00:c3230_0000",
  "m17_00_00_00:c3230_0001",
  "m17_00_00_00:c3230_0002",
]);
const additionalBossSlots = new Set([
  // Super Smough uses the phase-one model with distinct second-phase params,
  // so model-based boss detection alone cannot discover it.
  "m15_01_00_00:c2360_0001",
]);
const activeReplacementThinkParams = new Map([
  // Ambush/defensive variants depend on their vanilla area setup. Use each
  // model's ordinary combat brain when it is moved to an unrelated spawn.
  ["c2670", 267000], // Male Ghost
  ["c2680", 268000], // Female Ghost
  ["c3330", 333000], // Pisaca
  ["c2280", 228000], // Mushroom Child (non-ambush combat brain)
  ["c2300", 230000], // Titanite Demon (portable combat brain)
  ["c2780", 278000], // Mimic (awake combat brain; chest variants are event-bound)
]);
const forceCombatActivationModels = new Set([
  "c2280", "c2300", "c2670", "c2680", "c2780",
]);
const tangibleGhostModels = new Set(["c2670", "c2680"]);
const asylumPassiveSlots = new Set([
  "m18_01_00_00:c2500_0000",
  "m18_01_00_00:c2500_0001",
  "m18_01_00_00:c2500_0002",
]);
const newLondoPassiveSlots = new Set(
  Array.from(
    { length: 15 },
    (_, index) =>
      `m16_00_00_00:c2500_${String(index).padStart(4, "0")}`,
  ),
);
const additionalPassiveSlots = new Set([
  // The second Black Knight in the Kiln should observe the destination's
  // non-hostile opening behavior regardless of which enemy replaces it.
  "m18_00_00_00:c2790_0002",
]);
const parishBonfirePassiveSlots = new Set([
  // The Titanite Demon chamber is directly below the Undead Parish bonfire.
  // Replacements must not acquire the player through the floor or stairwell.
  "m10_01_00_00:c2300_0000",
]);
const deferredBossActivationRegions = new Map([
  // The regions used by the vanilla battle events are beyond the fog gate and
  // below the collapsing Asylum floor respectively. Waiting on the event ID
  // itself is too late and can also expose the character for one load frame.
  ["m10_01_00_00:c2250_0000", { enter: 1012701 }],
  // The Butterfly is staged outside its bridge and the vanilla encounter
  // event does not reliably wake grounded replacement bosses. Activate them
  // only after the player crosses into the arena.
  ["m12_00_00_00:c3230_0000", { enter: 1202896 }],
  ["m12_00_00_01:c3230_0000", { enter: 1202896 }],
  [
    "m18_01_00_00:c2230_0000",
    { enter: 1812896, warp: 1812302 },
  ],
]);
const containedBossCombatRegions = new Map([
  // Keep the first Asylum boss visible in its encounter. Pause it when the
  // vanilla side gate closes behind the escaping player, and wake it only
  // after the upper boss fog has actually been traversed.
  [
    "m18_01_00_00:c2232_0000",
    { enter: 1812996, pauseFlag: 11810315, reentryFlag: 11815390 },
  ],
]);
const safeBossSpawnPositions = new Map([
  // The Asylum Demon encounter floor. The vanilla entity begins above this
  // point and enters through a model-specific drop event.
  ["m18_01_00_00:c2232_0000", { x: 3.41, y: 197.61, z: -23.1 }],
  // Keep the second Asylum boss far below both playable floors. Its activation
  // event warps it to region 1812302 only after the player reaches the lower
  // arena, preventing tall replacements from clipping through the upper floor.
  ["m18_01_00_00:c2230_0000", { x: 3.31, y: 100, z: -19 }],
  // Taurus starts inside the tower and normally jumps to the bridge through
  // a model-specific event. Replacements must begin directly on the bridge.
  [
    "m10_01_00_00:c2250_0000",
    { x: 1.16, y: 15.82, z: -114.34, rotationY: -73.54 },
  ],
  // The second Bell Gargoyle has a rooftop staging position.
  ["m10_01_00_00:c5350_0001", { x: 10.69, y: 48.92, z: 124.35 }],
  ["m10_01_00_00:c5350_0002", { x: 6.14, y: 48.92, z: 124.35 }],
  // The Butterfly and Ceaseless encounters also enter from event-controlled
  // off-arena positions in vanilla.
  [
    "m12_00_00_00:c3230_0000",
    { x: 196.12, y: 8.09, z: 62.25, rotationY: 27.37 },
  ],
  [
    "m12_00_00_01:c3230_0000",
    { x: 196.12, y: 8.09, z: 62.25, rotationY: 27.37 },
  ],
  [
    "m14_01_00_00:c5250_0000",
    { x: 396.14, y: -278.14, z: 74.56, rotationY: 130.84 },
  ],
  // The primary Four Kings body begins far below the actual combat plane and
  // rises through a model-specific animation. Portable replacements do not
  // use that animation, so place them directly on the plane used by the
  // remaining king bodies.
  [
    "m16_00_00_00:c5390_0000",
    { x: 12.22, y: -311, z: 8.09, rotationY: -88.32 },
  ],
]);

const bedOfChaosSafeSpawn = Object.freeze({
  x: 105.3,
  y: -51.6,
  z: 570.2,
  rotationY: 180,
});

const bossArchetypeKey = (slot) => {
  // The Bed destination uses the core entity watched by the vanilla victory
  // event, while its portable archetype remains the visible c5230 boss body.
  if (slot.id === "m14_01_00_00:c5401_0000") return "c5230:523000";
  return `${slot.modelName}:${slot.npcParamId}`;
};

// ---- roaming bosses (Part E1, patches/0014) -------------------------------------------------------------------------------
// A boss in a regular enemy slot. Legacy: Normal enemy replacement "bosses"/"both"; the boss then drops a share of its souls
// (legacy `BossSouls`, authored per NpcParam row) because the boss-kill reward of an arena is an event a regular slot does not have.

/**
 * Souls of the vanilla boss fight by NpcParam row (the game's own values; the table is DS1TR's, keyed like the boss archetypes).
 * Only a boss with an entry can roam: without a known reward there is no souls figure to scale.
 */
const bossKillSouls = new Map([
  [223000, 20000], [223100, 20000], [223200, 2000], [224000, 6000], [225000, 3000], [232000, 40000], [236000, 10000], [236001, 25000],
  [273000, 30000], [323000, 10000], [332000, 15000], [410000, 50000], [450000, 60000], [451000, 60000], [521000, 40000], [522000, 60000],
  [525000, 20000], [526000, 25000], [527000, 10000], [527100, 25000], [528000, 20000], [532000, 40000], [537000, 70000],
]);

/**
 * DS1TR safety boundary: bosses that never roam, because the fight needs scripted terrain, the body cannot move, or it is not hostile
 * on its own. Everything else with a souls entry is a roaming candidate (single-body, mobile ground or air fighters).
 */
const roamingExcludedModels = new Set([
  "c5230", // Bed of Chaos: immobile, floor script
  "c5401", // Bed of Chaos core
  "c5250", // Ceaseless Discharge: immobile, 10 m tall lava body
  "c5320", // Dark Sun Gwyndolin: neutral team, stationary, scripted
  "c5370", // Lord Gwyn: scripted entrance, arena-bound
  // Multi-body bosses stay with their own planners (tails, linked parts, scripted release): they never stand in a regular slot,
  // with or without boss randomization (the dragon plan only reserves them when bosses are randomized).
  "c2730", // Crossbreed Priscilla (tail)
  "c3430", // Hellkite Drake (bridge dragon bodies)
  "c3471", // Sanctuary Guardian (tail)
  "c4510", // Black Dragon Kalameet (tail, two bodies)
  "c5200", // Centipede Demon (linked)
  "c5260", // Gaping Dragon (tail)
  "c5290", // Seath the Scaleless (tail, arena script)
  "c5350", // Bell Gargoyles
  "c5351", // Anor Londo Gargoyles
  "c5390", // Four Kings (linked bodies)
]);

/**
 * Bosses whose hit capsule does not describe their body, with the size class (0-5, `SIZE_EDGES` in sampling.js) Placement fit uses instead:
 * the Moonlight Butterfly is a flier with a 0.4 x 0.2 capsule and a body about three meters wide.
 */
const roamingSizeClassOverrides = new Map([["c3230", 3]]);

// ---- a regular enemy as the body of a boss encounter (Part E2, patches/0016) ------------------------------------------------
// The encounter stays the boss's: the slot keeps its entity id, so every vanilla event that waits for that entity (the health bar, the
// death, the boss-defeated flag, the fog, the exit) keeps working, and only the body that stands in the arena changes. DsrDataTool
// adds nothing to the events (the same portable-boss edits as boss sampling); it writes the body's NpcParam row so that the
// encounter owns what a boss row owns: item lots, drop type, souls, "do not initialize as dead" and "do not respawn".

/**
 * The boss slots whose encounter was audited as safe for a normal enemy as the body (docs/ENEMY_PLACEMENT_MODEL.md, 10.8): one
 * entity, completion that depends only on that entity's death, no dependent entities of its own, in a map that the legacy tool
 * needed no event surgery for. Every other boss slot keeps its boss in "normals" and can only be given a boss in "both".
 */
const encounterBodySupportedSlots = new Set([
  "m18_01_00_00:c2232_0000", // Asylum Demon
  "m10_01_00_00:c2250_0000", // Taurus Demon
  "m10_01_00_00:c2240_0000", // Capra Demon
]);

/**
 * The normal enemies that may be the body: ordinary awake ground melee enemies with a plain combat brain. Everything that is
 * passive, an ambush, tangible-ghost, a mimic, stationary, a flier, a statue, a plant, an NPC or tied to a scripted map event is left
 * out, and so is every boss-family model. Names are for the spoiler only.
 */
const encounterBodyModels = new Map([
  ["c2500", "Hollow"], ["c2540", "Armored Hollow"], ["c2550", "Undead Soldier"], ["c2560", "Balder Knight"], ["c2520", "Undead Assassin"],
  ["c2790", "Black Knight"], ["c2410", "Silver Knight"], ["c2390", "Darkwraith"], ["c2810", "Infested Barbarian"],
  ["c2690", "Serpent Soldier"], ["c2800", "Undead Crystal Soldier"],
]);

/**
 * model -> the catalog slot that stands for the model's body: among the regular slots of the model that are not protected, the variant
 * with the model's base brain (think row model*100), otherwise the most common one; ties by the lowest key, then the lowest slot id.
 */
function encounterBodySources(catalog) {
  const protectedIds = roamingProtectedSlotIds(catalog.enemySlots);
  const sources = new Map();
  for (const model of encounterBodyModels.keys()) {
    const base = Number(model.slice(1)) * 100;
    const slots = catalog.enemySlots.filter((slot) =>
      slot.modelName === model && !slot.dummy && slot.teamType === 0 && slot.npcParamId > 0 && slot.thinkParamId > 0 && slot.baseHp > 0 &&
      !protectedIds.has(slot.id));
    const groups = new Map();
    for (const slot of slots) {
      const key = `${slot.npcParamId}:${slot.thinkParamId}`;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(slot);
    }
    const ranked = [...groups].sort(([a, left], [b, right]) => {
      const baseLeft = left[0].thinkParamId === base ? 0 : 1;
      const baseRight = right[0].thinkParamId === base ? 0 : 1;
      return baseLeft - baseRight || right.length - left.length || (a < b ? -1 : a > b ? 1 : 0);
    });
    if (ranked.length > 0) sources.set(model, ranked[0][1].sort((a, b) => (a.id < b.id ? -1 : 1))[0]);
  }
  return sources;
}

/** What the boss planner needs to give a boss slot a normal enemy as its body, or null when the mode does not allow it. */
function encounterBodySetup(config, catalog, dragonPlan, bossOptions) {
  if (!["normals", "both"].includes(bossOptions.bossReplacement)) return null;
  const { slots } = regularEnemyContext(config, catalog, dragonPlan, []);
  const table = deriveRegularClassTable(slots);
  const customPool = bossOptions.customPool === null || bossOptions.customPool === undefined ? null : new Set(bossOptions.customPool);
  const sources = new Map([...encounterBodySources(catalog)].filter(([model]) => customPool === null || customPool.has(model)));
  const modelCounts = new Map();
  for (const slot of catalog.enemySlots) {
    if (slot.dummy || !/^m1[0-8]_/u.test(slot.mapId) || !slot.modelName.startsWith("c")) continue;
    if (!modelCounts.has(slot.mapId)) modelCounts.set(slot.mapId, new Map());
    const models = modelCounts.get(slot.mapId);
    models.set(slot.modelName, (models.get(slot.modelName) ?? 0) + 1);
  }
  return {
    supported: encounterBodySupportedSlots,
    types: deriveBodyTypes(sources, table),
    destination: (slot) => bossDestinationClass(table, slot),
    modelCounts,
    vanillaSizes: new Map([...modelCounts].map(([mapId, models]) => [mapId, models.size])),
  };
}

// ---- hostile NPCs (Part F1, patches/0017) ---------------------------------------------------------------------------------------
// Legacy "Hostile NPCs": four NPC models join the normal enemy pool (the legacy tool also sets their allegiance with an event). DS1TR
// reconstructs that as a source pool only: the friendly NPCs themselves (their slots, talk, shops, quests, services) are never touched,
// a hostile NPC is a body that stands in a regular enemy slot and is owned by that slot (team, drops, respawn, reward).

/**
 * The NPC slots of the real catalog by role. The classification is semantic (talk script, team, model family), never by model id alone, and
 * anything not recognized is UNKNOWN_UNSAFE. Only SAFE_HOSTILE_SOURCE slots are ever used, and only as a source of a body.
 */
export const NPC_ROLES = Object.freeze({
  SAFE_HOSTILE_SOURCE: "SAFE_HOSTILE_SOURCE",
  MERCHANT_UNSAFE: "MERCHANT_UNSAFE",
  BLACKSMITH_UNSAFE: "BLACKSMITH_UNSAFE",
  FIREKEEPER_OR_BONFIRE: "FIREKEEPER_OR_BONFIRE",
  QUEST_NPC: "QUEST_NPC",
  PLAYER_MODEL_NPC: "PLAYER_MODEL_NPC",
  BOSS_COMPANION: "BOSS_COMPANION",
  DUMMY_OR_PASSIVE: "DUMMY_OR_PASSIVE",
  UNKNOWN_UNSAFE: "UNKNOWN_UNSAFE",
});

/** The authored hostile sources (legacy: the Merchant, Andre and Vamos; Gough and the second Merchant are left out, see docs/ENEMY_PLACEMENT_MODEL.md 10.9). */
const hostileNpcSourceSlots = new Set([
  // The Undead Merchant of the Lower Undead Burg (m10_02, NPC 251000 / think 251001) is left out: its think row names a logic script (251000_logic.lua)
  // that the data tool does not carry to another map (it copies battle goals), so a placed copy would name a missing logic goal.
  "m10_01_00_00:c2510_0000", // Undead Merchant (Undead Burg), NPC 251001 / think 251000, common logic
  "m10_01_00_00:c2640_0000", // Andre of Astora (Undead Parish), NPC 264000 / think 264000
  "m13_00_00_00:c2920_0000", // Vamos (Catacombs), NPC 292000 / think 292000
]);

/** Role of a catalog slot that is not a regular enemy (team 0): why it may or may not be used. Unknown is unsafe. */
export function classifyNpcSlot(slot) {
  if (hostileNpcSourceSlots.has(slot.id)) return NPC_ROLES.SAFE_HOSTILE_SOURCE;
  if (slot.dummy) return NPC_ROLES.DUMMY_OR_PASSIVE;
  if (slot.modelName === "c1000") return NPC_ROLES.FIREKEEPER_OR_BONFIRE;
  if (slot.modelName === "c5330" || slot.modelName === "c5340") return NPC_ROLES.FIREKEEPER_OR_BONFIRE;
  if (slot.modelName === "c0000") return NPC_ROLES.PLAYER_MODEL_NPC;
  if (slot.modelName === "c2510") return NPC_ROLES.MERCHANT_UNSAFE;
  if (["c2640", "c2920", "c4110"].includes(slot.modelName)) return NPC_ROLES.BLACKSMITH_UNSAFE;
  if (slot.teamType === 1) return NPC_ROLES.BOSS_COMPANION;
  if (slot.talkId > 0 && slot.teamType === 2) return NPC_ROLES.QUEST_NPC;
  return NPC_ROLES.UNKNOWN_UNSAFE;
}

/** model -> catalog source slots of the hostile NPC candidates (empty when the catalog lacks them). */
function hostileNpcSources(catalog) {
  const sources = new Map();
  for (const slot of catalog.enemySlots) {
    if (!hostileNpcSourceSlots.has(slot.id) || slot.dummy || slot.npcParamId <= 0 || slot.thinkParamId <= 0 || !(slot.baseHp > 0)) continue;
    if (classifyNpcSlot(slot) !== NPC_ROLES.SAFE_HOSTILE_SOURCE) continue;
    if (!sources.has(slot.modelName)) sources.set(slot.modelName, []);
    sources.get(slot.modelName).push(slot);
  }
  return sources.size === 0 ? null : sources;
}

const hostileNpcSourceIds = (catalog) => new Set([...(hostileNpcSources(catalog)?.values() ?? [])].flat().map((slot) => slot.id));

/** Regular slots that keep their own enemy even when bosses may roam (they have special handling that a boss would break). */
function roamingProtectedSlotIds(slots) {
  return new Set(slots.filter((slot) =>
    asylumPassiveSlots.has(slot.id) ||
    newLondoPassiveSlots.has(slot.id) ||
    additionalPassiveSlots.has(slot.id) ||
    parishBonfirePassiveSlots.has(slot.id) ||
    slot.eventModelLocked === true ||
    tangibleGhostModels.has(slot.modelName) ||
    ((slot.mapId === "m12_00_00_00" || slot.mapId === "m12_00_00_01") && slot.modelName === "c2330")).map((slot) => slot.id));
}

function effectiveBossSlots(catalog) {
  const result = new Map(
    (catalog.bossSlots || [])
      .filter((slot) => !nonBossEncounterSlots.has(slot.id))
      .map((slot) => [slot.id, slot]),
  );
  for (const slot of catalog.enemySlots) {
    const modelId = Number(slot.modelName.slice(1));
    if (
      (additionalBossModels.has(slot.modelName) ||
        additionalBossSlots.has(slot.id)) &&
      !nonBossEncounterSlots.has(slot.id) &&
      !slot.dummy &&
      (slot.npcParamId === modelId * 100 || additionalBossSlots.has(slot.id)) &&
      slot.thinkParamId > 1000
    ) {
      result.set(slot.id, slot);
    }
  }
  const bedCore = catalog.enemySlots.find(
    (slot) => slot.id === "m14_01_00_00:c5401_0000",
  );
  if (bedCore) result.set(bedCore.id, bedCore);
  return [...result.values()];
}

function pickCompatibleEnemy(rng, slot, pool) {
  const sizeOrder = { small: 0, medium: 1, large: 2 };
  let candidates = pool.filter(
    (enemy) => sizeOrder[enemy.size] <= sizeOrder[slot.maxSize],
  );
  if (candidates.length === 0) candidates = pool;
  return candidates[rng.int(candidates.length)];
}

function randomizePrototypeEnemies(config) {
  if (!config.randomizeEnemies) return [];
  const rng = createStream(config.seed, "enemies", config.version);
  const enemyIndex = byId(enemies);
  return enemySlots.map((slot) => {
    const replacement = pickCompatibleEnemy(rng, slot, enemies);
    return {
      slot: slot.id,
      map: slot.map,
      from: enemyIndex.get(slot.original).name,
      to: replacement.name,
      scaling: config.enemyScaling,
      areaTier: slot.areaTier,
    };
  });
}

function bossGrounding(slot, catalog) {
  const safePosition = safeBossSpawnPositions.get(slot.id) || slot.position;
  return {
    groundX: safePosition.x,
    groundY: safePosition.y,
    groundZ: safePosition.z,
    groundRotationY: safePosition.rotationY ?? slot.rotation.y,
  };
}

function enemyPlacement(config, target, source, scaledNpcParamId, extra = {}) {
  // forceScaledNpcParam is an instruction to this function (sampling-v1 drop ownership), not a placement field.
  const { forceScaledNpcParam: _forceScaledNpcParam, ...placementExtra } = extra;
  const effectiveTargetThinkParamId =
    extra.targetThinkParamId ?? source.thinkParamId;
  const changed =
    source.modelName !== target.modelName ||
    source.npcParamId !== target.npcParamId ||
    effectiveTargetThinkParamId !== target.thinkParamId;
  const desiredInitialTeamType =
    extra.initialTeamType ?? target.teamType;
  const requiresTeamOverride =
    Number.isInteger(desiredInitialTeamType) &&
    source.teamType !== desiredInitialTeamType;
  return {
    slot: target.id,
    sourceSlot: source.id,
    sourceMap: source.mapId,
    map: target.mapId,
    entityId: target.entityId,
    from: `${target.modelName} [NPC ${target.npcParamId} / AI ${target.thinkParamId}]`,
    to: `${source.modelName} [NPC ${source.npcParamId} / AI ${source.thinkParamId}]`,
    modelName: target.modelName,
    targetModelName: source.modelName,
    sourceNpcParamId: target.npcParamId,
    sourceThinkParamId: target.thinkParamId,
    targetNpcParamId: source.npcParamId,
    targetThinkParamId: source.thinkParamId,
    targetBattleGoalId: source.battleGoalId,
    scaledNpcParamId:
      (config.enemyScaling === "vanilla" || !changed) &&
      extra.makeTangible !== true &&
      !requiresTeamOverride &&
      !(config.randomizeEnemyDrops && changed) &&
      !(extra.forceScaledNpcParam === true && changed)
        ? null
        : scaledNpcParamId,
    changed,
    scaling: config.enemyScaling,
    ...(requiresTeamOverride
      ? { initialTeamType: desiredInitialTeamType }
      : {}),
    ...placementExtra,
  };
}

function buildDragonPlan(config, catalog) {
  const bossSlotIds = new Set(effectiveBossSlots(catalog).map((slot) => slot.id));
  const slots = catalog.enemySlots;
  const byModel = (modelName, mapId = null) =>
    slots.filter(
      (slot) =>
        slot.modelName === modelName &&
        (!mapId || slot.mapId === mapId),
    );
  const unit = (id, bodies, parts = []) => ({ id, bodies, parts });
  const hellkiteBodies = byModel("c3430", "m10_01_00_00");
  const hellkitePrimary = hellkiteBodies.find(
    (body) => body.id === "m10_01_00_00:c3430_0000",
  );
  const hellkiteAuxiliary = hellkiteBodies.filter(
    (body) => body !== hellkitePrimary,
  );
  const hellkiteParts = byModel("c3431", "m10_01_00_00");
  const anorGargoyleBodies = byModel("c5351", "m15_01_00_00")
    .sort((left, right) => left.id.localeCompare(right.id));
  const anorGargoyleParts = byModel("c5353", "m15_01_00_00")
    .sort((left, right) => left.id.localeCompare(right.id));
  const hydras = ["m12_00_00_00", "m12_00_00_01", "m13_02_00_00"]
    .map((mapId) =>
      unit(
        `hydra-${mapId}`,
        byModel("c3530", mapId),
        byModel("c3531", mapId),
      ))
    .filter((entry) => entry.bodies.length && entry.parts.length === 7);
  const hydraHeadTemplate = byModel("c3531", "m12_00_00_00")
    .sort((left, right) => left.id.localeCompare(right.id));
  const isHydra = (entry) => entry.id.startsWith("hydra-");
  const simpleRegularDragons = [
    ...(hellkitePrimary
      ? [unit("hellkite-bridge", [hellkitePrimary], hellkiteParts)]
      : []),
    ...anorGargoyleBodies.map((body, index) =>
      unit(
        `anor-londo-gargoyle-${index}`,
        [body],
        anorGargoyleParts[index] ? [anorGargoyleParts[index]] : [],
      )),
    ...byModel("c2300").map((body) =>
      unit(`titanite-demon-${body.id}`, [body])),
    unit(
      "valley-undead-dragon",
      byModel("c3420", "m16_00_00_00"),
    ),
    ...byModel("c3520").map((body) => unit(`drake-${body.id}`, [body])),
    ...hydras,
    ...byModel("c3421", "m14_01_00_00").map((body) =>
      unit(`bounding-${body.id}`, [body])),
  ].filter((entry) => entry.bodies.length > 0);
  const units = [
    unit(
      "priscilla",
      byModel("c2730", "m11_00_00_00"),
      byModel("c2731", "m11_00_00_00"),
    ),
    unit(
      "painted-undead-dragon",
      byModel("c3420", "m11_00_00_00"),
      [
        ...byModel("c3421", "m11_00_00_00"),
        ...byModel("c3422", "m11_00_00_00"),
      ],
    ),
    unit(
      "kalameet",
      byModel("c4510", "m12_01_00_00"),
      byModel("c4511", "m12_01_00_00"),
    ),
    unit(
      "gaping-dragon",
      byModel("c5260", "m10_00_00_00"),
      byModel("c5261", "m10_00_00_00"),
    ),
    unit(
      "seath",
      byModel("c5290", "m17_00_00_00").filter(
        (body) => body.id === "m17_00_00_00:c5290_0001",
      ),
      byModel("c5291", "m17_00_00_00"),
    ),
  ].filter((entry) => entry.bodies.length > 0);

  const enabled = units.filter((entry) => {
    const boss = entry.bodies.some((body) => bossSlotIds.has(body.id));
    return boss ? config.randomizeBosses : config.randomizeEnemies;
  });
  const assignments = new Map();
  const rng = createStream(config.seed, "dragons", config.version);
  const isBossUnit = (entry) =>
    entry.bodies.some((body) => bossSlotIds.has(body.id));
  const poolKeys = new Set(enabled.map(
    (entry) => `${isBossUnit(entry) ? "boss" : "regular"}:${entry.parts.length}`,
  ));
  for (const poolKey of poolKeys) {
    const targets = enabled.filter(
      (entry) =>
        `${isBossUnit(entry) ? "boss" : "regular"}:${entry.parts.length}` ===
        poolKey,
    );
    const sources = shuffledWithoutFixedPoints(rng, targets);
    targets.forEach((target, index) => assignments.set(target.id, sources[index]));
  }

  const areaNames = new Map(
    (catalog.maps || []).map((map) => [map.id, map.name]),
  );
  const bossNames = catalog.bossNames || {};
  const result = { enemies: [], bosses: [], reservedSlotIds: new Set() };
  let enemyIndex = 0;
  let bossIndex = 0;
  if (config.randomizeEnemies && simpleRegularDragons.length > 1) {
    const simpleSources = shuffledWithoutNativeTitaniteDemon(
      rng,
      simpleRegularDragons,
    );
    simpleRegularDragons.forEach((target, index) => {
      const source = simpleSources[index];
      const targetBody = target.bodies[0];
      const shuffledSourceBody = source.bodies[0];
      const portableCombatSourceId =
        staticBridgeCombatSourceByModel.get(shuffledSourceBody.modelName);
      const sourceBody = portableCombatSourceId
        ? slots.find((slot) => slot.id === portableCombatSourceId) ??
          shuffledSourceBody
        : shuffledSourceBody;
      const targetIsHydra = isHydra(target);
      const sourceIsHydra = isHydra(source);
      const targetPosition = target.id === "hellkite-bridge"
        ? staticBridgeDragonSpawn
        : targetBody.position;
      const portableCollisionName = target.id === "hellkite-bridge"
        ? staticBridgeDragonSpawn.collisionName
        : targetBody.collisionName;
      const portableHydraEntityBase = 9_650_000 + index * 10;
      const placementExtra = sourceIsHydra && !targetIsHydra
        ? {
            compatibility: "portable-hydra-dragon-permutation",
            linkedDragonGroup: target.id,
            portableHydraGroup: true,
            entityId: portableHydraEntityBase,
            groundX: targetPosition.x,
            groundY: targetPosition.y,
            groundZ: targetPosition.z,
            targetCollisionName: portableCollisionName,
            forceCombatActivation: true,
            ...(target.id === "hellkite-bridge"
              ? {
                  staticBridgeDragon: true,
                  ...(config.randomizeEnemyDrops
                    ? { awardItemLotId: 34_310_000 }
                    : {}),
                }
              : {}),
          }
        : sourceIsHydra && targetIsHydra
          ? {
              compatibility: "linked-hydra-dragon-permutation",
              linkedDragonGroup: target.id,
              linkedEnemyGroup: target.id,
              // Each lake has its own authored combat distances and goals.
              // Keep those destination parameters when Hydras swap lakes.
              targetNpcParamId: targetBody.npcParamId,
              targetThinkParamId: targetBody.thinkParamId,
              targetBattleGoalId: targetBody.battleGoalId,
              forceCombatActivation: targetBody.entityId >= 0,
            }
          : target.id === "hellkite-bridge"
            ? {
                compatibility: "static-bridge-dragon-permutation",
                linkedDragonGroup: target.id,
                staticBridgeDragon: true,
                // The vanilla Hellkite actor is neutral until its fly-in
                // lifecycle changes allegiance. Static replacements skip that
                // lifecycle, so make the bridge occupant hostile immediately.
                initialTeamType: 0,
                forceCombatActivation: true,
                groundX: staticBridgeDragonSpawn.x,
                groundY: staticBridgeDragonSpawn.y,
                groundZ: staticBridgeDragonSpawn.z,
                targetCollisionName: staticBridgeDragonSpawn.collisionName,
                ...(config.randomizeEnemyDrops
                  ? { awardItemLotId: 34_310_000 }
                  : {}),
              }
            : {
                compatibility: "dragon-only-group-permutation",
                linkedDragonGroup: target.id,
                ...(forceCombatActivationModels.has(sourceBody.modelName) &&
                targetBody.entityId >= 0
                  ? { forceCombatActivation: true }
                  : {}),
              };
      if (
        sourceBody.modelName === "c2730" &&
        targetBody.mapId !== "m11_00_00_00"
      ) {
        placementExtra.preventInvisibility = true;
      }
      if (parishBonfirePassiveSlots.has(targetBody.id)) {
        Object.assign(placementExtra, {
          compatibility: "passive-parish-bonfire-permutation",
          passiveUntilAttacked: true,
          holdAiUntilAttacked: true,
          initialTeamType: 2,
          baseThinkParamId: sourceBody.thinkParamId,
          destinationThinkParamId: targetBody.thinkParamId,
          targetThinkParamId: 9_700_000 + enemyIndex,
          preserveDestinationPerception: true,
        });
        delete placementExtra.forceCombatActivation;
      }
      result.reservedSlotIds.add(targetBody.id);
      result.enemies.push(enemyPlacement(
        config,
        targetBody,
        sourceBody,
        9_150_000 + enemyIndex++,
        placementExtra,
      ));

      if (targetIsHydra) {
        target.parts.forEach((targetPart, partIndex) => {
          result.reservedSlotIds.add(targetPart.id);
          if (sourceIsHydra) {
            const sourcePart = source.parts[partIndex];
            result.enemies.push(enemyPlacement(
              config,
              targetPart,
              sourcePart,
              9_150_000 + enemyIndex++,
              {
                compatibility: "linked-hydra-dragon-permutation",
                linkedDragonGroup: target.id,
                linkedEnemyGroup: target.id,
                targetNpcParamId: targetPart.npcParamId,
                targetThinkParamId: targetPart.thinkParamId,
                targetBattleGoalId: targetPart.battleGoalId,
                forceCombatActivation: targetPart.entityId >= 0,
              },
            ));
          } else {
            result.enemies.push(enemyPlacement(
              config,
              targetPart,
              targetPart,
              null,
              {
                compatibility: "disabled-native-hydra-head",
                linkedDragonGroup: target.id,
                linkedEnemyGroup: target.id,
                disableEntity: true,
                groundX: targetPart.position.x,
                groundY: -1000,
                groundZ: targetPart.position.z,
              },
            ));
          }
        });
      } else if (sourceIsHydra) {
        const templateBody = hydras[0].bodies[0];
        source.parts.forEach((sourcePart, partIndex) => {
          const templatePart = hydraHeadTemplate[partIndex];
          const offset = {
            x: templatePart.position.x - templateBody.position.x,
            y: templatePart.position.y - templateBody.position.y,
            z: templatePart.position.z - templateBody.position.z,
          };
          const yawDelta =
            (targetBody.rotation.y - templateBody.rotation.y) * Math.PI / 180;
          const rotatedX =
            offset.x * Math.cos(yawDelta) - offset.z * Math.sin(yawDelta);
          const rotatedZ =
            offset.x * Math.sin(yawDelta) + offset.z * Math.cos(yawDelta);
          const syntheticName = `dsr_hydra_${index}_${partIndex}`;
          result.enemies.push({
            ...enemyPlacement(
              config,
              {
                ...templatePart,
                id: `${targetBody.mapId}:${syntheticName}`,
                mapId: targetBody.mapId,
                name: syntheticName,
                entityId: portableHydraEntityBase + partIndex + 1,
                collisionName: portableCollisionName,
                position: {
                  x: targetPosition.x + rotatedX,
                  y: targetPosition.y + offset.y,
                  z: targetPosition.z + rotatedZ,
                },
                rotation: {
                  ...templatePart.rotation,
                  y: targetBody.rotation.y +
                    (templatePart.rotation.y - templateBody.rotation.y),
                },
              },
              sourcePart,
              9_150_000 + enemyIndex++,
              {
                compatibility: "synthetic-linked-hydra-head",
                linkedDragonGroup: target.id,
                linkedEnemyGroup: target.id,
                portableHydraGroup: true,
                syntheticEnemy: true,
                entityId: portableHydraEntityBase + partIndex + 1,
                groundX: targetPosition.x + rotatedX,
                groundY: targetPosition.y + offset.y,
                groundZ: targetPosition.z + rotatedZ,
                groundRotationY: targetBody.rotation.y +
                  (templatePart.rotation.y - templateBody.rotation.y),
                targetCollisionName: portableCollisionName,
                forceCombatActivation: true,
              },
            ),
            sourceSlot: hydraHeadTemplate[partIndex].id,
            sourceMap: hydraHeadTemplate[partIndex].mapId,
          });
        });
      } else {
        target.parts.forEach((targetPart, partIndex) => {
          result.reservedSlotIds.add(targetPart.id);
          const sourcePart = source.parts[partIndex];
          if (sourcePart) {
            result.enemies.push(enemyPlacement(
              config,
              targetPart,
              sourcePart,
              9_150_000 + enemyIndex++,
              {
                compatibility: "linked-dragon-part-permutation",
                linkedDragonGroup: target.id,
              },
            ));
          } else {
            result.enemies.push(enemyPlacement(
              config,
              targetPart,
              targetPart,
              null,
              {
                compatibility: "disabled-unmatched-dragon-part",
                linkedDragonGroup: target.id,
                disableEntity: true,
                groundX: targetPart.position.x,
                groundY: -1000,
                groundZ: targetPart.position.z,
              },
            ));
          }
        });
        if (source.parts.length > target.parts.length) {
          const sourceBodyTemplate = source.bodies[0];
          source.parts.slice(target.parts.length).forEach(
            (sourcePart, sourcePartIndex) => {
              const partIndex = target.parts.length + sourcePartIndex;
              const offsetX = sourcePart.position.x - sourceBodyTemplate.position.x;
              const offsetY = sourcePart.position.y - sourceBodyTemplate.position.y;
              const offsetZ = sourcePart.position.z - sourceBodyTemplate.position.z;
              const yawDelta =
                (targetBody.rotation.y - sourceBodyTemplate.rotation.y) * Math.PI / 180;
              const rotatedX =
                offsetX * Math.cos(yawDelta) - offsetZ * Math.sin(yawDelta);
              const rotatedZ =
                offsetX * Math.sin(yawDelta) + offsetZ * Math.cos(yawDelta);
              const syntheticEntityId = 9_660_000 + index * 10 + partIndex;
              const syntheticName = `dsr_dragon_part_${index}_${partIndex}`;
              result.enemies.push({
                ...enemyPlacement(
                  config,
                  {
                    ...sourcePart,
                    id: `${targetBody.mapId}:${syntheticName}`,
                    mapId: targetBody.mapId,
                    name: syntheticName,
                    entityId: syntheticEntityId,
                    collisionName: targetBody.collisionName,
                    position: {
                      x: targetPosition.x + rotatedX,
                      y: targetPosition.y + offsetY,
                      z: targetPosition.z + rotatedZ,
                    },
                    rotation: {
                      ...sourcePart.rotation,
                      y: targetBody.rotation.y +
                        (sourcePart.rotation.y - sourceBodyTemplate.rotation.y),
                    },
                  },
                  sourcePart,
                  9_150_000 + enemyIndex++,
                  {
                    compatibility: "synthetic-linked-dragon-part",
                    linkedDragonGroup: target.id,
                    syntheticEnemy: true,
                    entityId: syntheticEntityId,
                    groundX: targetPosition.x + rotatedX,
                    groundY: targetPosition.y + offsetY,
                    groundZ: targetPosition.z + rotatedZ,
                    groundRotationY: targetBody.rotation.y +
                      (sourcePart.rotation.y - sourceBodyTemplate.rotation.y),
                    targetCollisionName: targetBody.collisionName,
                  },
                ),
                sourceSlot: sourcePart.id,
                sourceMap: sourcePart.mapId,
              });
            },
          );
        }
      }
    });
    for (const auxiliary of hellkiteAuxiliary) {
      result.reservedSlotIds.add(auxiliary.id);
      result.enemies.push(enemyPlacement(
        config,
        auxiliary,
        auxiliary,
        null,
        {
          compatibility: "disabled-hellkite-flight-auxiliary",
          linkedDragonGroup: "hellkite-bridge",
          disableEntity: true,
          groundX: auxiliary.position.x,
          groundY: -1000,
          groundZ: auxiliary.position.z,
        },
      ));
    }
  }
  for (const target of enabled) {
    const source = assignments.get(target.id);
    [...target.bodies, ...target.parts].forEach((slot) =>
      result.reservedSlotIds.add(slot.id),
    );
    target.bodies.forEach((targetBody, index) => {
      const sourceBody = source.bodies[index % source.bodies.length];
      const isBoss = bossSlotIds.has(targetBody.id);
      const extra = isBoss
        ? {
            originalBossName:
              bossNames[targetBody.modelName] ||
              dragonNames.get(targetBody.modelName) ||
              targetBody.modelName,
            randomizedBossName:
              bossNames[sourceBody.modelName] ||
              dragonNames.get(sourceBody.modelName) ||
              sourceBody.modelName,
            encounterLocation: {
              area: areaNames.get(targetBody.mapId) || targetBody.mapId,
              map: targetBody.mapId,
              slot: targetBody.name,
            },
            compatibility: "grounded-dragon-group-permutation",
            ...bossGrounding(targetBody, catalog),
            linkedDragonGroup: target.id,
            forceCombatActivation: targetBody.entityId >= 0,
          }
        : target.id === "hellkite-bridge"
        ? {
            compatibility: "static-bridge-dragon-permutation",
            linkedDragonGroup: target.id,
            staticBridgeDragon: true,
            initialTeamType: 0,
            groundX: staticBridgeDragonSpawn.x,
            groundY: staticBridgeDragonSpawn.y,
            groundZ: staticBridgeDragonSpawn.z,
            targetCollisionName: staticBridgeDragonSpawn.collisionName,
            forceCombatActivation: targetBody.entityId >= 0,
            ...(config.randomizeEnemyDrops
              ? { awardItemLotId: 34_310_000 }
              : {}),
          }
        : {
            compatibility: "dragon-only-group-permutation",
            linkedDragonGroup: target.id,
            forceCombatActivation: targetBody.entityId >= 0,
          };
      if (
        sourceBody.modelName === "c2730" &&
        targetBody.mapId !== "m11_00_00_00"
      ) {
        extra.preventInvisibility = true;
      }
      const placement = enemyPlacement(
        config,
        targetBody,
        sourceBody,
        isBoss ? 9_250_000 + bossIndex++ : 9_150_000 + enemyIndex++,
        extra,
      );
      (isBoss ? result.bosses : result.enemies).push(placement);
    });
    target.parts.forEach((targetPart, index) => {
      const sourcePart = source.parts[index];
      result.enemies.push(enemyPlacement(
        config,
        targetPart,
        sourcePart,
        9_150_000 + enemyIndex++,
        {
          compatibility: "linked-dragon-part-permutation",
          linkedDragonGroup: target.id,
        },
      ));
    });
  }

  if (config.randomizeBosses) {
    const linkedBossRng = createStream(
      config.seed,
      "linked-bosses",
      config.version,
    );
    const linkedBosses = [
      unit(
        "sanctuary-guardian",
        byModel("c3471", "m12_01_00_00"),
        byModel("c3472", "m12_01_00_00"),
      ),
      unit(
        "bell-gargoyles",
        byModel("c5350", "m10_01_00_00"),
        byModel("c5352", "m10_01_00_00"),
      ),
      unit(
        "centipede-demon",
        byModel("c5200", "m14_01_00_00"),
        [
          ...byModel("c5201", "m14_01_00_00"),
          ...byModel("c5202", "m14_01_00_00"),
        ],
      ),
    ].filter((entry) => entry.bodies.length && entry.parts.length);
    const linkedBossSources = shuffledWithoutFixedPoints(
      linkedBossRng,
      linkedBosses,
    );
    linkedBosses.forEach((target, unitIndex) => {
      const source = linkedBossSources[unitIndex];
      const singleBellReplacement =
        target.id === "bell-gargoyles" && source.id !== "bell-gargoyles";
      [...target.bodies, ...target.parts].forEach((slot) =>
        result.reservedSlotIds.add(slot.id),
      );
      target.bodies.forEach((targetBody, index) => {
        if (
          singleBellReplacement &&
          index > 0 &&
          targetBody.id === secondGargoyleSlotId &&
          config.secondGargoyle !== "single-boss"
        ) {
          // Legacy "Replace second Gargoyle". "keep" leaves the body (and its
          // tail) alone. "replace" lets the boss plan draw an independent boss
          // for it, so no disabled placement is written here.
          if (config.secondGargoyle === "replace") result.secondGargoyle = targetBody;
          return;
        }
        if (singleBellReplacement && index > 0) {
          result.enemies.push(enemyPlacement(
            config,
            targetBody,
            targetBody,
            null,
            {
              compatibility: "disabled-extra-bell-gargoyle-body",
              linkedEnemyGroup: target.id,
              disableEntity: true,
              killDisabledEntity: true,
            },
          ));
          return;
        }
        const sourceBody = source.bodies[index % source.bodies.length];
        const isBossSlot = bossSlotIds.has(targetBody.id);
        const placement = enemyPlacement(
          config,
          targetBody,
          sourceBody,
          isBossSlot
            ? 9_300_000 + bossIndex++
            : 9_350_000 + enemyIndex++,
          isBossSlot
            ? {
                originalBossName:
                  bossNames[targetBody.modelName] || targetBody.modelName,
                randomizedBossName:
                  bossNames[sourceBody.modelName] || sourceBody.modelName,
                encounterLocation: {
                  area: areaNames.get(targetBody.mapId) || targetBody.mapId,
                  map: targetBody.mapId,
                  slot: targetBody.name,
                },
                compatibility: "grounded-linked-boss-group-permutation",
                ...bossGrounding(targetBody, catalog),
                linkedEnemyGroup: target.id,
                forceCombatActivation: targetBody.entityId >= 0,
              }
            : {
                compatibility: "grounded-linked-boss-auxiliary",
                ...bossGrounding(targetBody, catalog),
                linkedEnemyGroup: target.id,
              },
        );
        (isBossSlot ? result.bosses : result.enemies).push(placement);
      });
      target.parts.forEach((targetPart, index) => {
        if (
          singleBellReplacement &&
          index === 1 &&
          config.secondGargoyle === "keep"
        ) {
          return;
        }
        if (singleBellReplacement && index > 0) {
          result.enemies.push(enemyPlacement(
            config,
            targetPart,
            targetPart,
            null,
            {
              compatibility: "disabled-extra-bell-gargoyle-part",
              linkedEnemyGroup: target.id,
              disableEntity: true,
            },
          ));
          return;
        }
        const sourcePart = source.parts[index % source.parts.length];
        result.enemies.push(enemyPlacement(
          config,
          targetPart,
          sourcePart,
          9_350_000 + enemyIndex++,
          {
            compatibility: "linked-boss-part-permutation",
            linkedEnemyGroup: target.id,
          },
        ));
      });
    });

    const bedVisualBodies = byModel("c5230", "m14_01_00_00");
    const bedArmBodies = byModel("c5400", "m14_01_00_00");
    const bedCoreBodies = byModel("c5401", "m14_01_00_00");
    [...bedVisualBodies, ...bedArmBodies].forEach((slot) =>
      result.reservedSlotIds.add(slot.id),
    );
    for (const part of [...bedVisualBodies, ...bedArmBodies]) {
      result.enemies.push(enemyPlacement(config, part, part, null, {
        compatibility: "disabled-bed-of-chaos-script-part",
        linkedEnemyGroup: "bed-of-chaos",
        disableEntity: true,
        killDisabledEntity: true,
      }));
    }

    const fourKingBodies = byModel("c5390", "m16_00_00_00")
      .sort((left, right) => left.id.localeCompare(right.id));
    for (const part of fourKingBodies.slice(1)) {
      result.reservedSlotIds.add(part.id);
      result.enemies.push(enemyPlacement(config, part, part, null, {
        compatibility: "disabled-extra-four-kings-body",
        linkedEnemyGroup: "four-kings",
        disableEntity: true,
        killDisabledEntity: true,
      }));
    }
    // The core entity (1410802) is retained as the Bed destination because the
    // vanilla victory event watches it. The general boss planner replaces its
    // model and parameters and places it on the intact arena floor.
  }
  return result;
}

function buildMassOfSoulsPlan(config, catalog) {
  if (!config.randomizeEnemies) {
    return { placements: [], reservedSlotIds: new Set() };
  }
  const slotsById = new Map(catalog.enemySlots.map((slot) => [slot.id, slot]));
  const group = (index, wispStart) => {
    const ids = [
      `m16_00_00_00:c3500_000${index}`,
      ...Array.from(
        { length: 6 },
        (_, offset) =>
          `m16_00_00_00:c3501_${String(wispStart + offset).padStart(4, "0")}`,
      ),
    ];
    return ids.map((id) => slotsById.get(id)).filter(Boolean);
  };
  const groups = [group(0, 0), group(1, 6)];
  if (groups.some((entry) => entry.length !== 7)) {
    return { placements: [], reservedSlotIds: new Set() };
  }
  const reservedSlotIds = new Set(groups.flat().map((slot) => slot.id));
  const placements = [];
  groups.forEach((targetGroup, groupIndex) => {
    const sourceGroup = groups[1 - groupIndex];
    targetGroup.forEach((target, index) => {
      placements.push(enemyPlacement(
        config,
        target,
        sourceGroup[index],
        null,
        {
          compatibility: "linked-mass-of-souls-group-permutation",
          linkedEnemyGroup: `mass-of-souls-${groupIndex}`,
        },
      ));
    });
  });
  return { placements, reservedSlotIds };
}

// Narrow dispatch between the enemy placement algorithms (patches/0005, 0006). permutation-v1 is the unchanged
// function below. sampling-v1 plans regular enemy slots only (planSampledRegularEnemies); bosses, units and every
// placement decoration are shared with permutation-v1.
function planExtractedEnemies(algorithm, config, catalog, dragonPlan, bossPlan, diagnostics) {
  if (algorithm === "permutation-v1") {
    return randomizeExtractedEnemies(config, catalog, dragonPlan, bossPlan);
  }
  if (algorithm === "sampling-v1") {
    return sampleExtractedEnemies(config, catalog, dragonPlan, bossPlan, diagnostics);
  }
  throw new EnemyPlacementUnavailableError(algorithm, true);
}

// The slots, special plans and fixed models both algorithms start from.
function regularEnemyContext(config, catalog, dragonPlan, bossPlan) {
  const massOfSoulsPlan = buildMassOfSoulsPlan(config, catalog);
  const bossSlotIds = new Set(effectiveBossSlots(catalog).map((slot) => slot.id));
  const bossFamilyModels = new Set(
    effectiveBossSlots(catalog).flatMap((slot) => [
      slot.modelName,
      canonicalBossModel.get(slot.modelName) || slot.modelName,
    ]),
  );
  const slots = catalog.enemySlots.filter(
    (slot) =>
      /^m1[0-8]_/u.test(slot.mapId) &&
      !bossSlotIds.has(slot.id) &&
      !dragonPlan.reservedSlotIds.has(slot.id) &&
      !massOfSoulsPlan.reservedSlotIds.has(slot.id) &&
      slot.teamType === 0 &&
      (config.replaceMimics || slot.modelName !== mimicModel) &&
      (config.replaceRespawningMosquitoes ||
        !respawningMosquitoSlots.has(slot.id)) &&
      slot.modelName !== "c0000" &&
      slot.modelName.startsWith("c") &&
      slot.npcParamId > 0 &&
      slot.thinkParamId > 0 &&
      slot.baseHp > 0 &&
      (!bossFamilyModels.has(slot.modelName) ||
        nonBossEncounterSlots.has(slot.id)) &&
      !dragonBodyModels.has(slot.modelName) &&
      !linkedPartModels.has(slot.modelName) &&
      !internalHelperModels.has(slot.modelName),
  );

  const pinwheelClones = config.pinwheelChaos
    ? catalog.enemySlots.filter(
        (slot) =>
          slot.mapId === pinwheelCloneMap &&
          slot.modelName === pinwheelModel &&
          slot.id !== pinwheelMainSlot &&
          !bossSlotIds.has(slot.id) &&
          !slot.dummy &&
          slot.teamType === 0 &&
          slot.npcParamId > 0 &&
          slot.thinkParamId > 0 &&
          slot.baseHp > 0,
      )
    : [];
  const pinwheelCloneIds = new Set(pinwheelClones.map((slot) => slot.id));
  const regularSlotIds = new Set(
    [...slots, ...pinwheelClones].map((slot) => slot.id),
  );
  const specialPlacements = [
    ...massOfSoulsPlan.placements,
    ...dragonPlan.enemies,
    ...dragonPlan.bosses,
    ...bossPlan,
  ];
  const specialModelBySlot = new Map(
    specialPlacements.map((placement) => [
      placement.slot,
      placement.targetModelName,
    ]),
  );
  const fixedModelsByMap = new Map();
  const addFixedModel = (mapId, modelName) => {
    if (!fixedModelsByMap.has(mapId)) fixedModelsByMap.set(mapId, new Set());
    fixedModelsByMap.get(mapId).add(modelName);
  };
  for (const slot of catalog.enemySlots) {
    if (regularSlotIds.has(slot.id)) continue;
    addFixedModel(
      slot.mapId,
      specialModelBySlot.get(slot.id) ?? slot.modelName,
    );
  }
  for (const placement of specialPlacements) {
    addFixedModel(placement.map, placement.targetModelName);
  }
  return { massOfSoulsPlan, slots, pinwheelClones, pinwheelCloneIds, fixedModelsByMap };
}

// Placement decoration shared by both algorithms: special entities (passive, ghosts, butterflies, combat
// activation), think-param clones and the placement record itself. Behavior for permutation-v1 is unchanged;
// sampling-v1 only differs in the compatibility label and in always forcing a scaled NpcParam row, so the
// destination slot's drops and team follow the slot (model document 6.9).
function decorateEnemyPlacements(config, catalog, allSlots, assignment, pinwheelCloneIds, algorithm, roamingSlotIds = new Set(), roamingOptions = null, hostileNpcIds = new Set()) {
  const sampling = algorithm === "sampling-v1";
  const newLondoGroundAnchors = catalog.enemySlots
    .filter((slot) =>
      slot.mapId === "m16_00_00_00" &&
      slot.modelName === "c2390" &&
      !slot.dummy &&
      slot.position &&
      slot.collisionName)
    .sort((left, right) => left.id.localeCompare(right.id));

  function groundedNewLondoGhostSpawn(slot, index) {
    if (newLondoGroundAnchors.length === 0) return null;
    const anchor = newLondoGroundAnchors.reduce((best, candidate) => {
      const distance = Math.hypot(
        candidate.position.x - slot.position.x,
        candidate.position.y - slot.position.y,
        candidate.position.z - slot.position.z,
      );
      return !best || distance < best.distance
        ? { slot: candidate, distance }
        : best;
    }, null).slot;
    let directionX = slot.position.x - anchor.position.x;
    let directionZ = slot.position.z - anchor.position.z;
    const length = Math.hypot(directionX, directionZ);
    if (length < 0.01) {
      const angle = index * 2.399963229728653;
      directionX = Math.cos(angle);
      directionZ = Math.sin(angle);
    } else {
      directionX /= length;
      directionZ /= length;
    }
    return {
      x: anchor.position.x + directionX * 1.25,
      y: anchor.position.y + 0.25,
      z: anchor.position.z + directionZ * 1.25,
      collisionName: anchor.collisionName,
    };
  }

  const randomized = allSlots.map((slot, index) => {
    const replacement = assignment.get(slot.id) ?? slot;
    const isChaosPinwheelClone = pinwheelCloneIds.has(slot.id);
    const passiveUntilAttacked =
      asylumPassiveSlots.has(slot.id) ||
      newLondoPassiveSlots.has(slot.id) ||
      additionalPassiveSlots.has(slot.id);
    const replacementThinkParamId =
      activeReplacementThinkParams.get(replacement.modelName) ??
      replacement.thinkParamId;
    const changed =
      replacement.modelName !== slot.modelName ||
      replacement.npcParamId !== slot.npcParamId ||
      replacementThinkParamId !== slot.thinkParamId;
    // Part F1: a hostile NPC is a body in a regular slot. Its NPC row's ghost transition is switched off (like every tangible replacement) and
    // the slot owns its team, respawn, reward and drops (the data tool takes those cells from the slot's row).
    const isHostileNpc = hostileNpcIds.has(replacement.id);
    const makeTangible = tangibleGhostModels.has(replacement.modelName) || isHostileNpc;
    const replacesWallPhasingGhost =
      slot.mapId === "m16_00_00_00" &&
      tangibleGhostModels.has(slot.modelName) &&
      !tangibleGhostModels.has(replacement.modelName);
    const groundedGhostSpawn = replacesWallPhasingGhost
      ? groundedNewLondoGhostSpawn(slot, index)
      : null;
    const cloneThinkParam = changed || passiveUntilAttacked;
    const darkrootButterflyPath =
      (slot.mapId === "m12_00_00_00" || slot.mapId === "m12_00_00_01") &&
      slot.modelName === "c2330";
    const isRoamingBoss = roamingSlotIds.has(slot.id);
    // A boss brain moved into a regular slot may wait for its arena event: the same wake-up as every portable boss gets.
    const requiresCombatEvent =
      forceCombatActivationModels.has(replacement.modelName) ||
      darkrootButterflyPath ||
      isRoamingBoss ||
      // A dummy part is not spawned in the game: it gets the row and the mark, never a wake-up event.
      (isHostileNpc && !slot.dummy);
    const assignedEntityId =
      (passiveUntilAttacked || requiresCombatEvent) && slot.entityId < 0
        ? 16_099_000 + index
        : slot.entityId;
    return enemyPlacement(config, slot, replacement, 9_100_000 + index, {
      entityId: assignedEntityId,
      ...(cloneThinkParam
        ? {
            baseThinkParamId: replacementThinkParamId,
            destinationThinkParamId: slot.thinkParamId,
            targetThinkParamId: 9_600_000 + index,
            preserveDestinationPerception: !darkrootButterflyPath,
          }
        : {
            targetThinkParamId: replacementThinkParamId,
          }),
      ...(makeTangible ? { makeTangible: true } : {}),
      ...(isHostileNpc ? { hostileNpc: true } : {}),
      // Replaced clones keep their own combat stats (normal HP) instead of
      // inheriting the clone's 1 HP; drops and team still follow the slot.
      ...(isChaosPinwheelClone ? { scaling: "vanilla" } : {}),
      // A roaming boss keeps the boss's own combat stats and the reward is set explicitly (legacy: the "with souls" row).
      ...(isRoamingBoss
        ? {
            scaling: "vanilla",
            roamingBossSouls: Math.trunc(bossKillSouls.get(replacement.npcParamId) * roamingOptions.roamingBossSouls / 100),
            ...(roamingOptions.roamingBossesStayDead ? { disableRespawn: true } : {}),
          }
        : {}),
      ...(sampling ? { forceScaledNpcParam: true } : {}),
      ...(groundedGhostSpawn
        ? {
            groundX: groundedGhostSpawn.x,
            groundY: groundedGhostSpawn.y,
            groundZ: groundedGhostSpawn.z,
            targetCollisionName: groundedGhostSpawn.collisionName,
          }
        : {}),
      ...(requiresCombatEvent &&
      !passiveUntilAttacked && assignedEntityId >= 0
        ? { forceCombatActivation: true }
        : {}),
      ...(passiveUntilAttacked
        ? {
            passiveUntilAttacked: true,
            // The event still changes allegiance after the first hit, but the
            // initial PARAM team prevents AI from targeting the player before
            // the map constructor has started that event.
            initialTeamType: 2,
          }
        : {}),
      compatibility: sampling
        ? isRoamingBoss
          ? "sampled-roaming-boss"
          : isHostileNpc && !passiveUntilAttacked && !isChaosPinwheelClone
          ? "sampled-hostile-npc"
          : isChaosPinwheelClone
          ? "pinwheel-chaos-clone"
          : passiveUntilAttacked
            ? "passive-until-attacked-sampled"
            : groundedGhostSpawn
              ? "grounded-new-londo-ghost-slot-sampled"
              : darkrootButterflyPath
                ? "active-darkroot-butterfly-path-sampled"
                : changed
                  ? "sampled-regular-replacement"
                  : "sampled-kept-vanilla"
        : isChaosPinwheelClone
        ? "pinwheel-chaos-clone"
        : passiveUntilAttacked
        ? asylumPassiveSlots.has(slot.id)
          ? "passive-asylum-source-ai-permutation"
          : newLondoPassiveSlots.has(slot.id)
            ? "passive-new-londo-entrance-permutation"
            : "passive-until-attacked-permutation"
        : groundedGhostSpawn
          ? "grounded-new-londo-ghost-slot-permutation"
          : darkrootButterflyPath
            ? "active-darkroot-butterfly-path-permutation"
            : changed
              ? "count-preserving-unrestricted-permutation"
              : replacement.id !== slot.id
                ? "count-preserving-identical-source-permutation"
                : "count-preserving-fixed-point",
    });
  });
  return randomized;
}

function randomizeExtractedEnemies(config, catalog, dragonPlan, bossPlan) {
  if (!config.randomizeEnemies) return dragonPlan.enemies;
  const rng = createStream(config.seed, "enemies", config.version);
  const { massOfSoulsPlan, slots, pinwheelClones, pinwheelCloneIds, fixedModelsByMap } =
    regularEnemyContext(config, catalog, dragonPlan, bossPlan);

  const budgetedSources = limitUniqueEnemyModelsByMap(
    slots,
    shuffledWithoutFixedPoints(rng, slots),
    MAX_ENEMY_MODELS_PER_MAP,
    fixedModelsByMap,
  );
  const shuffledSources = config.preventSameEnemy
    ? separateSameModelSources(
        createStream(config.seed, "prevent-same-enemy", config.version),
        slots,
        budgetedSources,
        MAX_ENEMY_MODELS_PER_MAP,
        fixedModelsByMap,
      )
    : budgetedSources;
  const cloneSources = chooseChaosPinwheelSources(
    createStream(config.seed, "pinwheel-chaos", config.version),
    config,
    pinwheelClones,
    slots,
    shuffledSources,
    fixedModelsByMap,
  );
  const allSlots = [...slots, ...pinwheelClones];
  const assignment = new Map(
    allSlots.map((target, index) => [
      target.id,
      index < slots.length ? shuffledSources[index] : cloneSources[index - slots.length],
    ]),
  );
  const randomized = decorateEnemyPlacements(config, catalog, allSlots, assignment, pinwheelCloneIds, "permutation-v1");
  return randomized
    .concat(massOfSoulsPlan.placements)
    .concat(dragonPlan.enemies);
}

// sampling-v1 for regular enemy slots (Part A). Everything that is not a regular slot - bosses, linked units,
// Mass of Souls, special entities - is planned exactly as in permutation-v1; their models are the fixed models
// of each map. The sampler's assignment goes through the same placement decoration.
function sampleExtractedEnemies(config, catalog, dragonPlan, bossPlan, diagnostics) {
  if (!config.randomizeEnemies) return dragonPlan.enemies;
  const options = resolveEnemyPlacementOptions(config, "sampling-v1");
  const { massOfSoulsPlan, slots, pinwheelClones, pinwheelCloneIds, fixedModelsByMap } =
    regularEnemyContext(config, catalog, dragonPlan, bossPlan);
  // Part E1: with bosses allowed in regular slots the regular sampler plans the normal enemies only ("bosses" leaves every
  // slot to the boss pass, "both" keeps the normal draw for the slots that do not get a boss) and the roaming pass overrides slots.
  const roamingMode = ROAMING_BOSS_MODES.includes(options.normalReplacement);
  const { assignment: regularAssignment, table } = planSampledRegularEnemies({
    seed: config.seed,
    options: roamingMode ? { ...options, normalReplacement: options.normalReplacement === "both" ? "normals" : "none" } : options,
    preventSame: config.preventSameEnemy,
    slots,
    extraSlots: pinwheelClones,
    fixedModelsByMap,
    diagnostics,
    hostileNpcSources: options.hostileNpcs ? hostileNpcSources(catalog) : null,
  });
  const allSlots = [...slots, ...pinwheelClones];
  let assignment = regularAssignment;
  let roaming = new Map();
  if (roamingMode) {
    // The regular sampler saw "normals"/"none"; the diagnostics state what was asked for.
    diagnostics.options = {
      ...diagnostics.options, normalReplacement: options.normalReplacement, bossChanceInNormalSlots: options.bossChanceInNormalSlots,
      roamingBossSouls: options.roamingBossSouls, roamingBossesStayDead: options.roamingBossesStayDead,
    };
    const candidates = roamingBossCandidates(config, catalog, dragonPlan);
    roaming = planRoamingBosses({
      seed: config.seed,
      options,
      slots,
      allSlots,
      assignment: regularAssignment,
      fixedModelsByMap,
      table,
      bossTypes: deriveRoamingBossTypes(candidates, table, roamingSizeClassOverrides),
      preventSame: config.preventSameEnemy,
      excludedSlotIds: roamingProtectedSlotIds(slots),
      diagnostics,
    });
    diagnostics.count("roamingCandidates", candidates.length);
    assignment = new Map([...regularAssignment, ...roaming]);
  }
  // Decoration (and its index-based ids) is computed over every slot so ids stay stable, but only slots the
  // sampler assigned get a placement: the others stay exactly vanilla (model document 5, decision 2).
  const randomized = decorateEnemyPlacements(config, catalog, allSlots, assignment, pinwheelCloneIds, "sampling-v1", new Set(roaming.keys()), options,
    options.hostileNpcs ? hostileNpcSourceIds(catalog) : new Set())
    .filter((placement) => assignment.has(placement.slot));
  if (options.hostileNpcs) {
    const placed = randomized.filter((placement) => placement.hostileNpc === true);
    diagnostics.count("hostileNpcPlacements", placed.length);
    for (const model of new Set(placed.map((placement) => placement.targetModelName))) diagnostics.count(`hostileNpc:${model}`, placed.filter((placement) => placement.targetModelName === model).length);
  }
  assertSlotOwnedDrops(randomized);
  return randomized
    .concat(massOfSoulsPlan.placements)
    .concat(dragonPlan.enemies);
}

const canonicalBossModel = new Map([
  ["c5351", "c5350"],
]);
const nonPortableBossReplacementModels = new Set();

function shuffledPortableBosses(rng, archetypeKeys, archetypes, excludedModels = new Set()) {
  const portable = archetypes.filter(
    (archetype) =>
      !nonPortableBossReplacementModels.has(archetype.modelName) &&
      !excludedModels.has(archetype.modelName),
  );
  if (portable.length === 0) return [...archetypes];
  const candidates = [...portable];
  while (candidates.length < archetypes.length) {
    candidates.push(portable[rng.int(portable.length)]);
  }
  for (let attempt = 0; attempt < 128; attempt += 1) {
    const shuffled = rng.shuffle(candidates);
    if (shuffled.every((replacement, index) => {
      const replacementModel =
        canonicalBossModel.get(replacement.modelName) || replacement.modelName;
      const replacementKey = bossArchetypeKey(replacement);
      const targetKey = archetypeKeys[index];
      const targetModel = targetKey.split(":")[0];
      return (
        replacementKey !== targetKey &&
        // Ceaseless Discharge's retreat/navigation logic walks into the
        // Taurus tower walls on the narrow Undead Burg bridge.
        !(targetModel === "c2250" && replacementModel === "c5250")
      );
    })) {
      return shuffled;
    }
  }
  throw new Error("Could not create a safe boss permutation.");
}

// The single-body boss slots that are both the destinations and the archetype sources of the boss planners.
function bossSourceSlots(config, catalog, dragonPlan) {
  return effectiveBossSlots(catalog).filter(
    (slot) =>
      !dragonPlan.reservedSlotIds.has(slot.id) &&
      !(config.pinwheelChaos && config.randomizeEnemies && slot.id === pinwheelMainSlot) &&
      slot.id !== "m14_01_00_00:c5230_0000" &&
      slot.npcParamId >= 0 &&
      slot.thinkParamId >= 0,
  );
}

// One boss slot per archetype that may stand in a regular slot (a souls entry, not on the exclusion list, not Gwyn-nerf-rejected by a
// "none" setting: the nerf applies to Lord Gwyn, who never roams).
function roamingBossCandidates(config, catalog, dragonPlan) {
  const byKey = new Map();
  for (const slot of bossSourceSlots(config, catalog, dragonPlan)) {
    const key = bossArchetypeKey(slot);
    if (byKey.has(key) || nonPortableBossReplacementModels.has(slot.modelName) || roamingExcludedModels.has(slot.modelName)) continue;
    if (!bossKillSouls.has(slot.npcParamId)) continue;
    byKey.set(key, slot);
  }
  return [...byKey.entries()].sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)).map(([, slot]) => slot);
}

function randomizeExtractedBosses(config, catalog, dragonPlan, placementDiagnostics = null) {
  if (!config.randomizeBosses || !catalog?.bossSlots?.length) return [];
  const rng = createStream(config.seed, "bosses", config.version);
  const areaNames = new Map(
    (catalog.maps || []).map((map) => [map.id, map.name]),
  );
  const bossNames = catalog.bossNames || {};
  const sources = bossSourceSlots(config, catalog, dragonPlan);
  const archetypeKeys = [
    ...new Set(sources.map((slot) => bossArchetypeKey(slot))),
  ];
  const archetypes = archetypeKeys.map((archetypeKey) =>
    sources.find((slot) => bossArchetypeKey(slot) === archetypeKey),
  );
  // Legacy "Gwyn nerf": with the legacy rejection chance Lord Gwyn is not a
  // candidate for any boss slot, the Kiln included. Another boss fills the gap
  // (a boss may then appear twice). Drawn from its own stream, and only when
  // the nerf is on, so the default stream usage is unchanged.
  const gwynRejected =
    (gwynNerfRejection[config.gwynNerf] ?? 0) > 0 &&
    createStream(config.seed, "gwyn-nerf", config.version).next() <
      gwynNerfRejection[config.gwynNerf];
  // Part D: with sampling-v1 and Boss replacement "bosses" every single-body boss slot draws its own replacement
  // (planSampledBosses). Every other configuration keeps the archetype permutation below, byte for byte.
  const bossOptions = config.enemyPlacementAlgorithm === "sampling-v1"
    ? resolveEnemyPlacementOptions(config, "sampling-v1")
    : null;
  const sampledBosses = BOSS_SAMPLING_MODES.includes(bossOptions?.bossReplacement) && placementDiagnostics !== null;
  const shuffled = sampledBosses
    ? []
    : shuffledPortableBosses(
      rng,
      archetypeKeys,
      archetypes,
      gwynRejected ? new Set([gwynModel]) : new Set(),
    );
  const replacementsByModel = new Map();
  archetypeKeys.forEach((archetypeKey, index) =>
    replacementsByModel.set(archetypeKey, shuffled[index]),
  );
  const buildBossPlacement = (slot, index, replacement, overrides = {}) => {
    const sourceArchetypeKey = bossArchetypeKey(slot);
    const sourceArchetypeIndex = archetypeKeys.indexOf(sourceArchetypeKey);
    const sourceModelName = sourceArchetypeKey.split(":")[0];
    const originalBossName =
      bossArchetypeNames.get(sourceArchetypeKey) ||
      bossNames[sourceModelName] ||
      additionalBossNames.get(sourceModelName) ||
      `${sourceModelName} [NPC ${slot.npcParamId}]`;
    const randomizedBossName =
      bossArchetypeNames.get(bossArchetypeKey(replacement)) ||
      bossNames[replacement.modelName] ||
      additionalBossNames.get(replacement.modelName) ||
      `${replacement.modelName} [NPC ${replacement.npcParamId}]`;
    const deferredActivation = deferredBossActivationRegions.get(slot.id);
    const portableThinkParamId = deferredActivation
      // The permutation gives every slot of a boss archetype the same replacement, so one think-param definition per source
      // archetype is consistent. Sampled bosses differ per slot, so each slot gets its own definition (a disjoint id range).
      ? (sampledBosses ? 9_750_000 + index : 9_700_000 + sourceArchetypeIndex)
      : replacement.thinkParamId;
    return enemyPlacement(config, slot, replacement, 9_200_000 + index, {
      ...(deferredActivation
        ? {
            // Preserve the replacement's battle goal while borrowing the
            // destination arena's detection distances. Several bosses (Nito
            // in particular) otherwise remain idle outside their native map.
            baseThinkParamId: replacement.thinkParamId,
            destinationThinkParamId: slot.thinkParamId,
            targetThinkParamId: portableThinkParamId,
            preserveDestinationPerception: true,
          }
        : {}),
      originalBossName,
      randomizedBossName,
      encounterLocation: {
        area: areaNames.get(slot.mapId) || slot.mapId,
        map: slot.mapId,
        slot: slot.name,
      },
      compatibility: slot.id === "m14_01_00_00:c5401_0000"
        ? "grounded-flat-floor-bed-of-chaos"
        : canonicalBossModel.has(slot.modelName)
        ? "linked-boss-form"
        : "grounded-unrestricted-boss-permutation",
      ...(deferredActivation
        ? {
            activationRegionId:
              deferredActivation.enter,
            ...(deferredActivation.warp
              ? {
                  activationWarpRegionId:
                    deferredActivation.warp,
                }
              : {}),
          }
        : {}),
      ...(slot.id === "m15_00_00_00:c2320_0000"
        ? { activationRegionId: 1502996 }
        : {}),
      ...(containedBossCombatRegions.has(slot.id)
        ? {
            combatRegionId: containedBossCombatRegions.get(slot.id).enter,
            combatPauseFlagId:
              containedBossCombatRegions.get(slot.id).pauseFlag,
            combatReentryFlagId:
              containedBossCombatRegions.get(slot.id).reentryFlag,
          }
        : {}),
      ...(replacement.modelName === "c2730" &&
      slot.mapId !== "m11_00_00_00"
        ? { preventInvisibility: true }
        : {}),
      // Some portable boss brains start in a neutral/waiting state when they
      // are transplanted into another encounter. If the destination does not
      // already use a deferred or contained activation event, add a small
      // combat event that enables AI and replans goals as soon as the map
      // constructor runs.
      ...(slot.entityId >= 0 ? { forceCombatActivation: true } : {}),
      portableBossSanitization: slot.modelName !== replacement.modelName ||
        slot.npcParamId !== replacement.npcParamId,
      ...(slot.id === "m14_01_00_00:c5401_0000"
        ? {
            groundX: bedOfChaosSafeSpawn.x,
            groundY: bedOfChaosSafeSpawn.y,
            groundZ: bedOfChaosSafeSpawn.z,
            groundRotationY: bedOfChaosSafeSpawn.rotationY,
            preserveBedOfChaosFloor: true,
          }
        : bossGrounding(slot, catalog)),
      ...overrides,
    });
  };
  let placements;
  if (sampledBosses) {
    const { assignment, bodySlots } = planSampledBosses({
      seed: config.seed,
      options: bossOptions,
      bodies: encounterBodySetup(config, catalog, dragonPlan, bossOptions),
      slots: sources,
      pool: archetypes.filter((archetype) => !nonPortableBossReplacementModels.has(archetype.modelName)),
      keyOf: bossArchetypeKey,
      gwynRejection: gwynNerfRejection[config.gwynNerf] ?? 0,
      gwynModel,
      // Same authored safety rule as the permutation: Ceaseless Discharge's navigation walks into the Taurus tower walls.
      isUnsafePair: (slot, candidate) =>
        bossArchetypeKey(slot).split(":")[0] === "c2250" &&
        (canonicalBossModel.get(candidate.modelName) || candidate.modelName) === "c5250",
      diagnostics: placementDiagnostics,
    });
    placements = sources.flatMap((slot, index) => {
      if (!assignment.has(slot.id)) return [];
      if (bodySlots.has(slot.id)) {
        // A normal enemy as the body of the encounter (Part E2): the plain combat brain of its model, the encounter's own rewards.
        const body = assignment.get(slot.id);
        return [buildBossPlacement(slot, index, { ...body, thinkParamId: activeReplacementThinkParams.get(body.modelName) ?? body.thinkParamId }, {
          forceScaledNpcParam: true,
          encounterBody: true,
          randomizedBossName: encounterBodyModels.get(body.modelName) ?? body.modelName,
          compatibility: "sampled-encounter-body",
        })];
      }
      return [buildBossPlacement(slot, index, assignment.get(slot.id), {
        // Drops and team follow the slot (model document 6.9); the compatibility tag names the planner.
        forceScaledNpcParam: true,
        compatibility: "sampled-boss-replacement",
      })];
    });
  } else {
    placements = sources.map((slot, index) =>
      buildBossPlacement(
        slot,
        index,
        replacementsByModel.get(bossArchetypeKey(slot)),
      ));
  }
  // Legacy "Replace second Gargoyle" = replace: the second Bell Gargoyle is
  // replaced by a boss drawn independently of the first one (repeats allowed),
  // from the same portable pool, honoring the Gwyn nerf and Pinwheel Chaos
  // exclusions already applied to it. It is released by the vanilla event
  // 11015396 once the first body drops to 60% health, so no forced combat
  // activation is added (it would wake the body immediately).
  // The body is not a catalogued boss slot, so (like the other secondary
  // bodies) its placement lives in the regular list: it is appended to the
  // linked-boss plan's enemy placements, which the enemy planner counts for the
  // per-map model budget and appends to the enemy output.
  if (dragonPlan.secondGargoyle) {
    const pool = archetypes.filter(
      (archetype) =>
        !nonPortableBossReplacementModels.has(archetype.modelName) &&
        !(gwynRejected && archetype.modelName === gwynModel),
    );
    if (pool.length === 0) {
      throw new Error("SECOND_GARGOYLE_POOL_EMPTY: no portable boss can replace the second Bell Gargoyle.");
    }
    const replacement = pool[
      createStream(config.seed, "second-gargoyle", config.version).int(pool.length)
    ];
    dragonPlan.enemies.push(buildBossPlacement(
      dragonPlan.secondGargoyle,
      sources.length,
      replacement,
      {
        forceCombatActivation: false,
        compatibility: "legacy-second-gargoyle-replacement",
        linkedEnemyGroup: "bell-gargoyles",
      },
    ));
  }
  return placements.concat(dragonPlan.bosses);
}

function randomizableWorldItemLots(config, catalog) {
  if (!catalog?.worldItemLots?.length) return [];
  const permanentlyFixedGoods = new Set([2010, 2015]);
  const permanentlyFixedNames = new Set([
    "Archive Prison Extra Key",
    "Archive Tower Giant Cell Key",
    "Archive Tower Giant Door Key",
  ]);
  const isPermanentlyFixed = (lot) =>
    lot.entries?.some(
      (entry) =>
        entry.category === 0x40000000 &&
        permanentlyFixedGoods.has(entry.itemId),
    ) ||
    lot.name === "Dungeon Cell Key" ||
    lot.name === "Undead Asylum F2 East Key" ||
    permanentlyFixedNames.has(lot.name);
  return catalog.worldItemLots.filter(
    (lot) =>
      !isPermanentlyFixed(lot) &&
      (lot.protectedProgression
        ? config.randomizeProtectedItems
        : config.randomizeItems),
  );
}

const bulkShopGoodNames = new Set([
  "Throwing Knife",
  "Poison Throwing Knife",
  "Firebomb",
  "Black Firebomb",
  "Dung Pie",
  "Lloyd's Talisman",
  "Alluring Skull",
  "Prism Stone",
]);

const randomizableProtectedShopItems = new Set([
  "Armor Smithbox",
  "Bottomless Box",
  "Crest of Artorias",
  "Master Key",
  "Repairbox",
  "Weapon Smithbox",
  "Book of the Guilty",
  "Dried Finger",
  "Eye of Death",
  "Orange Guidance Soapstone",
  "Divine Blessing",
  "Gold Pine Resin",
  "Humanity",
  "Purging Stone",
  "Transient Curse",
  "Twin Humanities",
  "Cast Light",
  "Fire Orb",
  "Great Combustion",
  "Great Heal Excerpt",
  "Homeward",
  "Iron Flesh",
  "Magic Weapon",
  "Seek Guidance",
  "Soul Spear",
  "Force",
  "Great Heal",
  "Heal",
  "Karmic Justice",
  "Magic Barrier",
  "Wrath of the Gods",
  "Combustion",
  "Fire Whip",
  "Fireball",
  "Firestorm",
  "Flash Sweat",
  "Great Fireball",
  "Poison Mist",
  "Toxic Mist",
  "Undead Rapport",
  "Aural Decoy",
  "Chameleon",
  "Crystal Magic Weapon",
  "Crystal Soul Spear",
  "Fall Control",
  "Great Heavy Soul Arrow",
  "Great Soul Arrow",
  "Heavy Soul Arrow",
  "Hidden Body",
  "Hidden Weapon",
  "Homing Crystal Soulmass",
  "Homing Soulmass",
  "Magic Shield",
  "Repair",
  "Resist Curse",
  "Soul Arrow",
]);

function canonicalShopItemName(name) {
  return String(name).replace(/^(?:Sorcery|Pyromancy|Miracle):\s*/u, "");
}

function isBulkShopConsumable(entry) {
  return (
    (entry.equipType === 0 && /(?:Arrow|Bolt)$/u.test(entry.name)) ||
    (entry.equipType === 3 && bulkShopGoodNames.has(entry.name))
  );
}

function isConsumableSoul(entry) {
  return (
    /^(?:Large )?Soul of /u.test(entry.name) ||
    entry.name === "Fire Keeper Soul"
  );
}

function isUnsupportedCutItem(entry) {
  // Escape Death is an unfinished miracle. It remains in ShopLineupParam and
  // has residual English metadata, but retail localized Magic_name tables do
  // not provide a usable player-facing name for ID 5200.
  return entry.equipType === 4 && entry.itemId === 5200;
}

function shopPurchaseLimit(entry) {
  if (isBulkShopConsumable(entry)) return 99;
  if (isConsumableSoul(entry)) return 1;
  if (
    entry.equipType === 0 ||
    entry.equipType === 1 ||
    entry.equipType === 4 ||
    /^(?:Sorcery|Pyromancy|Miracle):/u.test(entry.name)
  ) {
    return 1;
  }
  return 10;
}

function randomizeExtractedItemLots(config, catalog) {
  const worldLots = randomizableWorldItemLots(config, catalog);
  const protectedGiftRows = new Set([1090, 1100]);
  const giftLots = config.randomizeGifts
    ? (catalog?.gifts || []).filter(
        (gift) =>
          !config.progressionLogic || !protectedGiftRows.has(gift.rowId),
      )
    : [];
  const enemyLots = config.randomizeEnemyDrops
    ? catalog?.enemyDropLots || []
    : [];
  const shopRows = config.randomizeShops
    ? (catalog?.shopEntries || []).filter(
        (entry) =>
          !(entry.equipType === 4 && entry.equipId === 5200) &&
          (!config.progressionLogic ||
            entry.equipType !== 3 ||
            entry.eventFlag < 0 ||
            randomizableProtectedShopItems.has(canonicalShopItemName(entry.name))),
      )
    : [];
  const allLots = [
    ...worldLots.map((lot) => ({ ...lot, pool: "world" })),
    ...giftLots.map((lot) => ({ ...lot, pool: "gift" })),
    ...enemyLots.map((lot) => ({ ...lot, pool: "enemy" })),
  ];
  if (allLots.some((lot) => !Array.isArray(lot.entries))) {
    throw new Error(
      "The item catalog has no per-slot data. Verify the clean game and import its data again.",
    );
  }
  const lotTargets = allLots.flatMap((lot) =>
    lot.entries.map((entry) => ({
      ...entry,
      rowId: lot.rowId,
      mapId: lot.mapId,
      pool: lot.pool,
      kind: "lot",
      progression: Boolean(lot.protectedProgression),
      name: entry.name || lot.name,
    })).filter((entry) => !isUnsupportedCutItem(entry)),
  );
  const shopTargets = shopRows.map((entry) => ({
    rowId: entry.rowId,
    pool: "shop",
    kind: "shop",
    slot: null,
    itemId: entry.equipId,
    category:
      entry.equipType === 0
        ? 0
        : entry.equipType === 1
          ? 0x10000000
          : entry.equipType === 2
            ? 0x20000000
            : 0x40000000,
    equipType: entry.equipType,
    quantity: 1,
    name: entry.name,
    eventFlag: entry.eventFlag,
    progression: false,
  }));
  const shopOnlyTargets = shopTargets.filter(isBulkShopConsumable);
  // Legacy "Soul items" = Replaced: lesser souls become other consumables.
  if (config.soulItems === "replaced") {
    replaceLesserSouls(
      createStream(config.seed, "soul-items", config.version),
      lotTargets,
    );
  }
  // Legacy "Fashion Souls" off: world armor sets move as whole sets.
  const armorSetHomes = config.fashionSouls ? [] : findArmorSetHomes(lotTargets);
  const armorSetTargets = new Set(armorSetHomes.flat());
  const globalTargets = [
    ...lotTargets.filter((entry) => !armorSetTargets.has(entry)),
    ...shopTargets.filter((entry) => !isBulkShopConsumable(entry)),
  ];
  const result = { items: [], gifts: [], enemyDrops: [], shops: [] };
  if (globalTargets.length + shopOnlyTargets.length < 2 && armorSetHomes.length === 0) {
    return result;
  }

  const areaNames = new Map(
    (catalog.maps || []).map((map) => [map.id, map.name]),
  );
  const describeLocation = (entry) =>
    entry.pool === "world"
      ? {
          area: areaNames.get(entry.mapId) || entry.mapId,
          map: entry.mapId,
          itemLot: entry.rowId,
        }
      : entry.pool === "gift"
        ? {
            area: `NPC gift: ${entry.name}`,
            map: "ItemLotParam",
            itemLot: entry.rowId,
          }
        : entry.pool === "shop"
          ? {
              area: `Shop inventory: ${entry.name}`,
              map: "ShopLineupParam",
              itemLot: entry.rowId,
            }
          : {
              area: `Enemy drop: ${entry.name}`,
              map: "ItemLotParam",
              itemLot: entry.rowId,
            };
  const addPlacement = (target, source) => {
    const singleQuantity =
      [0, 1, 2, 4].includes(source.equipType) ||
      /^(Sorcery|Pyromancy|Miracle):/.test(source.name);
    const placement = {
      rowId: target.rowId,
      sourceRowId: source.rowId,
      sourceSlot: source.slot,
      targetKind: target.kind,
      targetSlot: target.slot,
      itemId: source.itemId,
      itemCategory: source.category,
      equipType: source.equipType,
      itemQuantity: singleQuantity
        ? 1
        : Math.max(1, Number(source.quantity) || 1),
      ...(target.kind === "shop"
        ? { shopQuantity: shopPurchaseLimit(source) }
        : {}),
      from: target.originalName ?? target.name,
      to: source.name,
      itemName: source.name,
      sourcePool: source.pool,
      targetPool: target.pool,
      originalLocation: describeLocation(source),
      randomizedLocation: describeLocation(target),
      progression: source.progression,
      preserved:
        target.kind === source.kind &&
        target.rowId === source.rowId &&
        target.slot === source.slot &&
        !source.transmuted,
    };
    if (target.pool === "world") {
      placement.location = `itemlot:${target.rowId}`;
      placement.map = target.mapId;
      result.items.push(placement);
    } else if (target.pool === "gift") {
      result.gifts.push(placement);
    } else if (target.pool === "enemy") {
      result.enemyDrops.push(placement);
    } else {
      result.shops.push(placement);
    }
  };
  for (const [stream, targets] of [
    ["all-item-sources", globalTargets],
    ["shop-consumables", shopOnlyTargets],
  ]) {
    if (targets.length === 0) continue;
    const rng = createStream(config.seed, stream, config.version);
    let sources = stream === "all-item-sources"
      ? shuffledWithoutDlcRestrictedItems(rng, targets)
      : shuffledWithoutFixedPoints(rng, targets);
    if (stream === "all-item-sources") {
      sources = keepConsumableSoulsInFiniteShops(targets, sources);
      const fogRules = fogPlacementRules(config, catalog, targets);
      if (fogRules) sources = repairFogTrackedItems(rng, targets, sources, fogRules);
    }
    targets.forEach((target, index) => addPlacement(target, sources[index]));
  }
  if (armorSetHomes.length > 0) {
    const rng = createStream(config.seed, "armor-sets", config.version);
    for (const [home, sourceSet] of permutedArmorSets(rng, armorSetHomes)) {
      home.forEach((target, index) => addPlacement(target, sourceSet[index]));
    }
  }
  return result;
}

// Legacy "Soul items" Replaced: the lesser souls (goods 400-409, the Soul of
// a Lost Undead line up to the Soul of a Great Hero) are replaced in place by
// other consumables before the shuffle. Boss souls are not part of the item
// catalog and Fire Keeper Souls (390-396) are not lesser souls, so neither is
// touched. The replacement pool is derived from the catalog's own unprotected
// world goods (never legacy tables) with their vanilla quantities, in item-id
// order. Only goods that already occur in at least two distinct lots qualify:
// unique items (keys, the Peculiar Doll, the Lordvessel) must never be
// duplicated because FogMod tracks them by id and needs exactly one copy.
// Ammunition is excluded because it is not a goods item.
const isLesserSoul = (entry) =>
  entry.equipType === 3 && entry.itemId >= 400 && entry.itemId <= 409;

function replaceLesserSouls(rng, lotTargets) {
  const lotsByItem = new Map();
  for (const entry of lotTargets) {
    if (entry.equipType !== 3) continue;
    if (!lotsByItem.has(entry.itemId)) lotsByItem.set(entry.itemId, new Set());
    lotsByItem.get(entry.itemId).add(`${entry.pool}:${entry.rowId}`);
  }
  const pool = new Map();
  for (const entry of lotTargets) {
    if (
      (lotsByItem.get(entry.itemId)?.size ?? 0) < 2 ||
      entry.pool !== "world" ||
      entry.progression ||
      entry.equipType !== 3 ||
      (entry.itemId >= 390 && entry.itemId <= 409)
    ) {
      continue;
    }
    if (!pool.has(entry.itemId)) {
      pool.set(entry.itemId, { entry, quantities: new Set() });
    }
    pool.get(entry.itemId).quantities.add(Math.max(1, Number(entry.quantity) || 1));
  }
  const candidates = [...pool.entries()]
    .sort((left, right) => left[0] - right[0])
    .map(([, value]) => ({
      entry: value.entry,
      quantities: [...value.quantities].sort((left, right) => left - right),
    }));
  lotTargets.forEach((entry, index) => {
    if (!isLesserSoul(entry)) return;
    if (candidates.length === 0) {
      throw new Error(
        "SOUL_ITEMS_POOL_EMPTY: the catalog has no consumables to replace lesser souls with.",
      );
    }
    const pick = candidates[rng.int(candidates.length)];
    const quantity = pick.quantities[rng.int(pick.quantities.length)];
    lotTargets[index] = {
      ...entry,
      itemId: pick.entry.itemId,
      category: pick.entry.category,
      equipType: pick.entry.equipType,
      quantity,
      name: pick.entry.name,
      originalName: entry.originalName ?? entry.name,
      transmuted: true,
    };
  });
}

// Vanilla world armor sets are runs of consecutive item-lot rows in one map,
// one single-entry lot per piece, all pieces sharing the set number
// floor(itemId / 10000). Only complete runs qualify; irregular sets (for
// example the Guardian set, scattered over unrelated rows) stay ordinary
// pieces in the global pool.
function findArmorSetHomes(lotTargets) {
  const groups = new Map();
  for (const target of lotTargets) {
    if (
      target.pool !== "world" ||
      target.equipType !== 1 ||
      target.progression ||
      target.slot !== 1
    ) {
      continue;
    }
    const key = `${target.mapId}:${Math.floor(target.itemId / 10000)}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(target);
  }
  const homes = [];
  for (const group of groups.values()) {
    const pieces = [...group].sort((left, right) => left.rowId - right.rowId);
    const consecutive = pieces.every(
      (piece, index) => index === 0 || piece.rowId === pieces[index - 1].rowId + 1,
    );
    const ordered = [...pieces].sort((left, right) => left.itemId - right.itemId);
    const inSlotOrder = pieces.every((piece, index) => piece === ordered[index]);
    if (pieces.length >= 2 && consecutive && inSlotOrder) homes.push(pieces);
  }
  return homes.sort((left, right) => left[0].rowId - right[0].rowId);
}

// Whole sets are exchanged between set homes with the same piece count, never
// leaving a home with its own set when an exchange is possible. A size class
// with a single home keeps its vanilla set.
function permutedArmorSets(rng, homes) {
  const bySize = new Map();
  for (const home of homes) {
    if (!bySize.has(home.length)) bySize.set(home.length, []);
    bySize.get(home.length).push(home);
  }
  const pairs = [];
  for (const size of [...bySize.keys()].sort((left, right) => left - right)) {
    const sizeHomes = bySize.get(size);
    const sets = shuffledWithoutFixedPoints(rng, sizeHomes);
    sizeHomes.forEach((home, index) => pairs.push([home, sets[index]]));
  }
  return pairs;
}

function keepConsumableSoulsInFiniteShops(targets, initialSources) {
  const sources = [...initialSources];
  const invalid = () => targets
    .map((target, index) => ({ target, index }))
    .filter(({ target, index }) =>
      target.kind === "shop" &&
      target.eventFlag < 0 &&
      isConsumableSoul(sources[index]));
  for (const { index: soulIndex } of invalid()) {
    const donorIndex = targets.findIndex((target, index) =>
      target.kind === "shop" &&
      target.eventFlag >= 0 &&
      !isConsumableSoul(sources[index]) &&
      sources[index] !== targets[soulIndex] &&
      sources[soulIndex] !== target);
    if (donorIndex < 0) {
      throw new Error(
        "Could not assign consumable souls to finite shop stock.",
      );
    }
    [sources[soulIndex], sources[donorIndex]] =
      [sources[donorIndex], sources[soulIndex]];
  }
  return sources;
}

function isDlcRestrictedItem(entry) {
  return (
    entry.progression === true ||
    /(?:Titanite|\bEmber\b)/iu.test(entry.name) ||
    entry.name === "Havel's Ring" ||
    entry.name === "Fire Keeper Soul" ||
    entry.name === "Rusted Iron Ring"
  );
}

function shuffledWithoutNativeTitaniteDemon(rng, values) {
  if (values.length < 2) return [...values];
  for (let attempt = 0; attempt < 256; attempt += 1) {
    const shuffled = shuffledWithoutFixedPoints(rng, values);
    if (shuffled.every((value, index) => {
      const targetModel = values[index].bodies[0]?.modelName;
      const sourceModel = value.bodies[0]?.modelName;
      return targetModel !== "c2300" || sourceModel !== "c2300";
    })) {
      return shuffled;
    }
  }
  throw new Error("Could not keep Titanite Demons out of native Titanite Demon slots.");
}

// World randomizer (FogMod) item tracking. FogMod reads the annotated item lots of the finished game data
// (ItemReader.FindItems) and needs every required key item in exactly one of them: an item that left them (an unannotated
// world lot, a gift or drop lot it does not annotate, a shop, the starting inventory) stops generation with "Couldn't find
// <item> in item lots", and a second copy in another annotated area stops it with "Item <item> found in both ...". The
// shuffle therefore gives each source of a tracked item a role:
//   "annotated" - the one copy FogMod must find: it may only be placed in a lot FogMod annotates (a start row or a row that
//                 follows one without a gap in the catalog's lots - a subset of what FogMod walks, so never too permissive);
//   "shop"      - an extra copy (the shops sell a second Crest of Artorias and Residence Key, and Cast Light): it may only go
//                 into a shop, where FogMod does not look, so it can never become a second copy in an annotated lot.
// The first lot copy of a required item is the annotated one; an item with no copy in the catalog's lots (FogMod finds it
// in a protected lot outside the shuffle) has only extra copies. Items FogMod does not require are always extras.
function fogCoveredLotRows(config, catalog) {
  const starts = config.fogTrackedItems?.lotStarts ?? [];
  const known = new Set([
    ...(catalog?.worldItemLots || []).map((lot) => lot.rowId),
    ...(catalog?.gifts || []).map((lot) => lot.rowId),
    ...(catalog?.enemyDropLots || []).map((lot) => lot.rowId),
  ]);
  const covered = new Set();
  for (const start of starts) {
    for (let row = start; known.has(row); row += 1) covered.add(row);
  }
  return covered;
}

function fogPlacementRules(config, catalog, targets) {
  const tracked = config.fogTrackedItems;
  if (!tracked?.items?.length) return null;
  const trackedItems = new Map(tracked.items.map((item) => [`${item.equipType}:${item.itemId}`, item]));
  // Vanilla copies of each tracked item in the catalog's lots (not shops), whether or not the lot joins this shuffle.
  const vanillaLotCopies = new Map();
  for (const lot of [...(catalog?.worldItemLots || []), ...(catalog?.gifts || []), ...(catalog?.enemyDropLots || [])]) {
    for (const entry of lot.entries || []) {
      const key = `${entry.equipType}:${entry.itemId}`;
      if (trackedItems.has(key)) vanillaLotCopies.set(key, (vanillaLotCopies.get(key) ?? 0) + 1);
    }
  }
  const roles = new Map();
  const annotatedGiven = new Set();
  for (const source of targets) {
    const key = `${source.equipType}:${source.itemId}`;
    const item = trackedItems.get(key);
    if (!item) continue;
    const isAnnotatedCopy = item.required !== false && source.kind === "lot" && (vanillaLotCopies.get(key) ?? 0) > 0 && !annotatedGiven.has(key);
    if (isAnnotatedCopy) annotatedGiven.add(key);
    roles.set(source, isAnnotatedCopy ? "annotated" : "shop");
  }
  if (roles.size === 0) return null;
  const covered = fogCoveredLotRows(config, catalog);
  return {
    roleOf: (source) => roles.get(source) ?? null,
    isBad: (target, source) => {
      const role = roles.get(source);
      if (role === "annotated") return !(target.kind === "lot" && covered.has(target.rowId));
      if (role === "shop") return target.kind !== "shop";
      return false;
    },
  };
}

// Repairs a finished shuffle so that every tracked item respects its role. Only swaps, so the permutation (and every other
// item) stays intact; the other rules of the shuffle (no fixed points, DLC-restricted items, consumable souls in finite
// shops) are kept. The RNG is used only when a swap is needed, so a shuffle that already satisfies the rule is returned
// untouched and seeds that worked before keep their exact placement.
function repairFogTrackedItems(rng, values, shuffled, rules) {
  const dlcBad = (target, source) => target.mapId === "m12_01_00_00" && isDlcRestrictedItem(source);
  const soulBad = (target, source) =>
    target.kind === "shop" && target.eventFlag < 0 && isConsumableSoul(source);
  const pairBad = (target, source) => rules.isBad(target, source) || dlcBad(target, source) || soulBad(target, source);
  for (let index = 0; index < values.length; index += 1) {
    if (!rules.isBad(values[index], shuffled[index])) continue;
    const donors = [];
    for (let other = 0; other < values.length; other += 1) {
      if (other === index) continue;
      if (
        !pairBad(values[index], shuffled[other]) &&
        !pairBad(values[other], shuffled[index]) &&
        shuffled[other] !== values[index] &&
        shuffled[index] !== values[other]
      ) {
        donors.push(other);
      }
    }
    if (donors.length === 0) {
      throw new Error(
        `FOG_TRACKED_ITEM_UNPLACEABLE: ${shuffled[index].name} has no place the world randomizer can follow.`,
      );
    }
    const donor = donors[rng.int(donors.length)];
    [shuffled[index], shuffled[donor]] = [shuffled[donor], shuffled[index]];
  }
  return shuffled;
}

function shuffledWithoutDlcRestrictedItems(rng, values) {
  if (values.length < 2) return [...values];
  for (let attempt = 0; attempt < 32; attempt += 1) {
    const shuffled = shuffledWithoutFixedPoints(rng, values);
    const badTargets = values
      .map((target, index) => ({ target, index }))
      .filter(
        ({ target, index }) =>
          target.mapId === "m12_01_00_00" &&
          isDlcRestrictedItem(shuffled[index]),
      );
    let repaired = true;
    for (const { index: dlcIndex } of badTargets) {
      const restrictedSource = shuffled[dlcIndex];
      const donorIndex = values.findIndex(
        (target, index) =>
          target.mapId !== "m12_01_00_00" &&
          !isDlcRestrictedItem(shuffled[index]) &&
          shuffled[index] !== values[dlcIndex] &&
          restrictedSource !== target,
      );
      if (donorIndex < 0) {
        repaired = false;
        break;
      }
      [shuffled[dlcIndex], shuffled[donorIndex]] =
        [shuffled[donorIndex], shuffled[dlcIndex]];
    }
    if (repaired) return shuffled;
  }
  throw new Error(
    "Could not keep DLC-restricted items out of DLC locations.",
  );
}

function shuffledWithoutFixedPoints(rng, values) {
  if (values.length < 2) return [...values];
  for (let attempt = 0; attempt < 32; attempt += 1) {
    const shuffled = rng.shuffle(values);
    if (shuffled.every((value, index) => value !== values[index])) return shuffled;
  }
  return [...values.slice(1), values[0]];
}

// Legacy "Pinwheel Chaos": the clone bodies are replaced independently, so a
// source may be used by more than one clone. Sources come from the regular
// permutation, models are limited to the per-map budget, and the draw is
// bounded and deterministic.
function chooseChaosPinwheelSources(
  rng,
  config,
  clones,
  regularSlots,
  regularSources,
  fixedModelsByMap,
) {
  if (clones.length === 0) return [];
  const models = new Set(fixedModelsByMap.get(pinwheelCloneMap) ?? []);
  regularSlots.forEach((slot, index) => {
    if (slot.mapId === pinwheelCloneMap) models.add(regularSources[index].modelName);
  });
  const chosen = [];
  for (const clone of clones) {
    const candidates = regularSources.filter(
      (source) =>
        (models.has(source.modelName) || models.size < MAX_ENEMY_MODELS_PER_MAP) &&
        source.id !== clone.id &&
        !(config.preventSameEnemy && source.modelName === clone.modelName),
    );
    if (candidates.length === 0) {
      throw new Error(
        "PINWHEEL_CHAOS_NO_CANDIDATE: no enemy fits the Catacombs model budget.",
      );
    }
    const source = candidates[rng.int(candidates.length)];
    models.add(source.modelName);
    chosen.push(source);
  }
  return chosen;
}

// Legacy "Prevent same enemy": no slot may receive a source of its own
// model. Runs after the per-map model budget has been satisfied and only
// swaps sources, so the count-preserving property holds. Swaps keep every
// map within the budget. The search is bounded (a fixed number of passes, one
// swap per offending slot per pass) and deterministic for a given stream.
function separateSameModelSources(
  rng,
  targets,
  initialSources,
  maximumUnique,
  fixedModelsByMap,
) {
  const sources = [...initialSources];
  const total = targets.length;
  const modelTotals = new Map();
  for (const source of sources) {
    modelTotals.set(source.modelName, (modelTotals.get(source.modelName) || 0) + 1);
  }
  for (const [modelName, count] of modelTotals) {
    if (count * 2 > total) {
      throw new Error(
        `PREVENT_SAME_ENEMY_INFEASIBLE: model ${modelName} fills ${count} of ` +
          `${total} randomized enemy slots, so it cannot avoid replacing itself.`,
      );
    }
  }

  const sourceCounts = new Map();
  const sourceCount = (mapId) => {
    if (!sourceCounts.has(mapId)) sourceCounts.set(mapId, new Map());
    return sourceCounts.get(mapId);
  };
  targets.forEach((target, index) => {
    const counts = sourceCount(target.mapId);
    counts.set(sources[index].modelName, (counts.get(sources[index].modelName) || 0) + 1);
  });
  const uniqueAfter = (mapId, lost, gained) => {
    const counts = sourceCount(mapId);
    const fixed = fixedModelsByMap.get(mapId);
    const models = new Set(counts.keys());
    if (counts.get(lost) === 1) models.delete(lost);
    models.add(gained);
    for (const modelName of fixed ?? []) models.add(modelName);
    return models.size;
  };
  const move = (mapId, lost, gained) => {
    const counts = sourceCount(mapId);
    counts.set(lost, counts.get(lost) - 1);
    if (counts.get(lost) === 0) counts.delete(lost);
    counts.set(gained, (counts.get(gained) || 0) + 1);
  };
  const isSame = (index) => sources[index].modelName === targets[index].modelName;

  for (let pass = 0; pass < 8; pass += 1) {
    const offending = [];
    for (let index = 0; index < total; index += 1) {
      if (isSame(index)) offending.push(index);
    }
    if (offending.length === 0) return sources;
    for (const index of offending) {
      if (!isSame(index)) continue;
      const start = rng.int(total);
      for (let step = 0; step < total; step += 1) {
        const partner = (start + step) % total;
        if (partner === index) continue;
        const own = sources[index];
        const other = sources[partner];
        if (
          other.modelName === targets[index].modelName ||
          own.modelName === targets[partner].modelName ||
          other.id === targets[index].id ||
          own.id === targets[partner].id
        ) {
          continue;
        }
        const mapId = targets[index].mapId;
        const partnerMapId = targets[partner].mapId;
        if (
          mapId !== partnerMapId &&
          (uniqueAfter(mapId, own.modelName, other.modelName) > maximumUnique ||
            uniqueAfter(partnerMapId, other.modelName, own.modelName) > maximumUnique)
        ) {
          continue;
        }
        if (mapId !== partnerMapId) {
          move(mapId, own.modelName, other.modelName);
          move(partnerMapId, other.modelName, own.modelName);
        }
        [sources[index], sources[partner]] = [sources[partner], sources[index]];
        break;
      }
    }
  }
  if (targets.some((_, index) => isSame(index))) {
    throw new Error(
      "PREVENT_SAME_ENEMY_UNRESOLVED: the bounded repair could not separate every " +
        "enemy from its own model within the per-map model budget.",
    );
  }
  return sources;
}

function limitUniqueEnemyModelsByMap(
  targets,
  initialSources,
  maximumUnique,
  fixedModelsByMap = new Map(),
) {
  const sources = [...initialSources];
  const indicesByMap = new Map();
  targets.forEach((target, index) => {
    if (!indicesByMap.has(target.mapId)) indicesByMap.set(target.mapId, []);
    indicesByMap.get(target.mapId).push(index);
  });

  const modelCounts = () => {
    const result = new Map();
    for (const [mapId, indices] of indicesByMap) {
      const counts = new Map();
      for (const modelName of fixedModelsByMap.get(mapId) ?? []) {
        counts.set(modelName, 1);
      }
      for (const index of indices) {
        const modelName = sources[index].modelName;
        counts.set(modelName, (counts.get(modelName) || 0) + 1);
      }
      result.set(mapId, counts);
    }
    return result;
  };

  for (let pass = 0; pass < 200; pass += 1) {
    let countsByMap = modelCounts();
    const overBudget = [...countsByMap]
      .filter(([, counts]) => counts.size > maximumUnique)
      .sort((left, right) =>
        right[1].size - left[1].size ||
        left[0].localeCompare(right[0]));
    if (overBudget.length === 0) return sources;

    let madeProgress = false;
    for (const [mapId, initialCounts] of overBudget) {
      const mapIndices = indicesByMap.get(mapId);
      const models = [...initialCounts]
        .sort((left, right) =>
          left[1] - right[1] ||
          left[0].localeCompare(right[0]));
      for (const [modelName] of models) {
        const sourceIndices = mapIndices.filter(
          (index) => sources[index].modelName === modelName,
        );
        const before = [...sources];
        let completed = true;

        for (const sourceIndex of sourceIndices) {
          countsByMap = modelCounts();
          const destinationCounts = countsByMap.get(mapId);
          const candidates = [];
          for (
            let partnerIndex = 0;
            partnerIndex < sources.length;
            partnerIndex += 1
          ) {
            const partnerMap = targets[partnerIndex].mapId;
            const partnerModel = sources[partnerIndex].modelName;
            if (
              partnerMap === mapId ||
              partnerModel === modelName ||
              !destinationCounts.has(partnerModel) ||
              sources[sourceIndex].id === targets[partnerIndex].id ||
              sources[partnerIndex].id === targets[sourceIndex].id
            ) {
              continue;
            }

            const partnerCounts = countsByMap.get(partnerMap);
            const partnerUniqueAfter =
              partnerCounts.size +
              (partnerCounts.has(modelName) ? 0 : 1) -
              (partnerCounts.get(partnerModel) === 1 ? 1 : 0);
            if (partnerUniqueAfter > maximumUnique) continue;
            candidates.push({
              index: partnerIndex,
              introducesModel: partnerCounts.has(modelName) ? 0 : 1,
              uniqueAfter: partnerUniqueAfter,
              currentUnique: partnerCounts.size,
            });
          }

          candidates.sort((left, right) =>
            left.introducesModel - right.introducesModel ||
            left.uniqueAfter - right.uniqueAfter ||
            left.currentUnique - right.currentUnique ||
            left.index - right.index);
          if (candidates.length === 0) {
            completed = false;
            break;
          }
          const partnerIndex = candidates[0].index;
          [sources[sourceIndex], sources[partnerIndex]] =
            [sources[partnerIndex], sources[sourceIndex]];
        }

        const uniqueAfter = modelCounts().get(mapId).size;
        if (completed && uniqueAfter < initialCounts.size) {
          madeProgress = true;
          break;
        }
        sources.splice(0, sources.length, ...before);
      }
    }

    if (!madeProgress) {
      const unresolved = [...modelCounts()]
        .filter(([, counts]) => counts.size > maximumUnique)
        .map(([mapId, counts]) => `${mapId}=${counts.size}`)
        .join(", ");
      throw new Error(
        `Could not satisfy the per-map enemy model budget: ${unresolved}`,
      );
    }
  }
  throw new Error("Enemy model-budget balancing did not converge.");
}

// Shields occupy EquipParamWeapon ids 1,400,000-1,599,999 (Buckler through the
// Greatshield of Artorias); whips start at 1,600,000.
const isShieldWeapon = (weapon) =>
  weapon.id >= 1_400_000 && weapon.id < 1_600_000;

function randomizeStartingClasses(config, catalog) {
  if (
    (!config.randomizeStartingClass && !config.randomizeStartingEquipment) ||
    !catalog?.startingClasses?.length
  ) {
    return [];
  }
  const classes = catalog.startingClasses;
  const statsRng = createStream(config.seed, "starting-classes", config.version);
  const equipmentRng = createStream(
    config.seed,
    "starting-equipment",
    config.version,
  );
  const statsSources = config.randomizeStartingClass
    ? shuffledWithoutFixedPoints(statsRng, classes)
    : classes;
  const equipmentPools = catalog.startingEquipmentPools;
  const hasGeneralEquipmentPools = Boolean(
    equipmentPools?.weapons?.length &&
      equipmentPools?.helms?.length &&
      equipmentPools?.armors?.length &&
      equipmentPools?.gauntlets?.length &&
      equipmentPools?.legs?.length,
  );
  const specialIds = new Set(["hunter", "sorcerer", "pyromancer", "cleric"]);
  const vanillaStartingWeaponIds = new Set();
  const vanillaStartingArmorIds = new Set();
  for (const itemLot of catalog.startingItemLots || []) {
    for (const [name, value] of Object.entries(itemLot.cells || {})) {
      if (name.startsWith("lotItemId") && Number(value) > 0) {
        vanillaStartingWeaponIds.add(Number(value));
      }
    }
  }
  for (const startingClass of classes) {
    for (const row of [startingClass.display, startingClass.start]) {
      for (const field of ["equip_Wep_Right", "equip_Subwep_Right", "equip_Wep_Left", "equip_Subwep_Left"]) {
        if (Number(row[field]) >= 0) vanillaStartingWeaponIds.add(Number(row[field]));
      }
      for (const field of ["equip_Helm", "equip_Armer", "equip_Gaunt", "equip_Leg"]) {
        if (Number(row[field]) >= 0) vanillaStartingArmorIds.add(Number(row[field]));
      }
    }
  }
  const randomArmorPools = {
    helms: (equipmentPools?.helms || []).filter(
      (item) => !vanillaStartingArmorIds.has(item.id),
    ),
    armors: (equipmentPools?.armors || []).filter(
      (item) => !vanillaStartingArmorIds.has(item.id),
    ),
    gauntlets: (equipmentPools?.gauntlets || []).filter(
      (item) => !vanillaStartingArmorIds.has(item.id),
    ),
    legs: (equipmentPools?.legs || []).filter(
      (item) => !vanillaStartingArmorIds.has(item.id),
    ),
  };
  // One shared set makes the no-duplicate rule explicit across every class
  // and every armor slot, even if future game data contains overlapping IDs.
  const usedArmorIds = new Set();
  const pickUnique = (pool, used, label) => {
    const available = pool.filter((item) => !used.has(item.id));
    if (available.length === 0) {
      throw new Error(`No unique options remain in the ${label} pool.`);
    }
    const selected = available[equipmentRng.int(available.length)];
    used.add(selected.id);
    return selected;
  };
  const weaponAssignments = new Map();
  if (config.randomizeStartingEquipment && hasGeneralEquipmentPools) {
    const requests = [];
    classes.forEach((target, index) => {
      const stats = statsSources[index].start;
      const baseStrength = Number(stats.baseStr);
      const otherwiseUsableWeapons = equipmentPools.weapons.filter(
        (weapon) =>
          weapon.dexterity <= Number(stats.baseDex) &&
          weapon.intelligence <= Number(stats.baseMag) &&
          weapon.faith <= Number(stats.baseFai) &&
          !vanillaStartingWeaponIds.has(weapon.id) &&
          weapon.id < 2_000_000 &&
          weapon.id % 1_000 === 0 &&
          weapon.name !== "Fists" &&
          !(weapon.id >= 1_200_000 && weapon.id < 1_300_000),
      );
      const oneHandedWeapons = otherwiseUsableWeapons.filter(
        (weapon) => weapon.strength <= baseStrength,
      );
      const twoHandCompatibleWeapons = otherwiseUsableWeapons.filter(
        (weapon) => weapon.strength <= Math.floor(baseStrength * 1.5),
      );
      // Legacy "Starting items": the right-hand weapon is one-handed usable in
      // "shield-1h" and may need two-handing otherwise; the left hand is a
      // shield in both shield modes and any usable weapon in "mixed-pool".
      const primaryPool =
        config.startingItems === "shield-1h"
          ? oneHandedWeapons
          : twoHandCompatibleWeapons;
      const offhandPool =
        config.startingItems === "mixed-pool"
          ? oneHandedWeapons
          : oneHandedWeapons.filter(isShieldWeapon);
      const roles = specialIds.has(target.id)
        ? ["primary", "offhand", "special"]
        : ["primary", "offhand"];
      for (const role of roles) {
        const candidates =
          role === "primary"
            ? primaryPool.filter((weapon) => weapon.isPrimaryWeapon === true)
            : role === "offhand"
              ? offhandPool
              : oneHandedWeapons;
        if (candidates.length === 0) {
          throw new Error(
            `Not enough compatible equipment for ${target.name}:${role}.`,
          );
        }
        requests.push({
          key: `${target.id}:${role}`,
          candidates: equipmentRng.shuffle(candidates),
        });
      }
    });

    // Bipartite matching gives every slot a unique weapon and can reassign an
    // earlier choice when a class has fewer compatible alternatives.
    requests.sort(
      (left, right) =>
        left.candidates.length - right.candidates.length ||
        left.key.localeCompare(right.key),
    );
    const ownerByItemId = new Map();
    const requestByKey = new Map(requests.map((request) => [request.key, request]));
    const assign = (request, seenItems, seenRequests) => {
      if (seenRequests.has(request.key)) return false;
      seenRequests.add(request.key);
      for (const candidate of request.candidates) {
        if (seenItems.has(candidate.id)) continue;
        seenItems.add(candidate.id);
        const currentOwnerKey = ownerByItemId.get(candidate.id);
        if (
          !currentOwnerKey ||
          assign(
            requestByKey.get(currentOwnerKey),
            seenItems,
            seenRequests,
          )
        ) {
          ownerByItemId.set(candidate.id, request.key);
          weaponAssignments.set(request.key, candidate);
          return true;
        }
      }
      return false;
    };
    for (const request of requests) {
      if (!assign(request, new Set(), new Set())) {
        throw new Error(
          `Could not assign a unique weapon to ${request.key}.`,
        );
      }
    }
  }

  return classes.map((target, index) => {
    const statsSource = statsSources[index];
    let equipment = null;
    if (config.randomizeStartingEquipment && hasGeneralEquipmentPools) {
      const preserveVanillaArmor = target.id === "deprived";
      const vanillaArmor = (field, name) => ({
        id: Number(target.start[field]),
        name,
      });
      equipment = {
        pickupWeapon: weaponAssignments.get(`${target.id}:primary`),
        pickupOffhand: weaponAssignments.get(`${target.id}:offhand`),
        pickupSpecial: specialIds.has(target.id)
          ? weaponAssignments.get(`${target.id}:special`)
          : null,
        helm: preserveVanillaArmor
          ? vanillaArmor("equip_Helm", "No helmet")
          : pickUnique(randomArmorPools.helms, usedArmorIds, "helm"),
        armor: preserveVanillaArmor
          ? vanillaArmor("equip_Armer", "No chest armor")
          : pickUnique(randomArmorPools.armors, usedArmorIds, "chest armor"),
        gauntlets: preserveVanillaArmor
          ? vanillaArmor("equip_Gaunt", "No gauntlets")
          : pickUnique(randomArmorPools.gauntlets, usedArmorIds, "gauntlets"),
        legs: preserveVanillaArmor
          ? vanillaArmor("equip_Leg", "No leg armor")
          : pickUnique(randomArmorPools.legs, usedArmorIds, "leg armor"),
        preserveVanillaArmor,
      };
    }
    return {
      slot: target.id,
      name: target.name,
      from: target.name,
      to:
        statsSource.id === target.id
          ? target.name
          : `${target.name} with ${statsSource.name} stats`,
      statsFrom: statsSource.id,
      equipmentFrom: target.id,
      equipment,
      randomizeStats: config.randomizeStartingClass,
      randomizeEquipment:
        config.randomizeStartingEquipment && hasGeneralEquipmentPools,
    };
  });
}

function randomizeBosses(config) {
  if (!config.randomizeBosses) return [];
  const rng = createStream(config.seed, "bosses", config.version);
  const bossIndex = byId(bosses);
  return bossSlots.map((slot) => {
    const candidates = bosses.filter(
      (boss) =>
        boss.safe &&
        boss.arena.every((tag) => slot.capabilities.includes(tag)),
    );
    const pool = candidates.length > 0 ? candidates : [bossIndex.get(slot.original)];
    const replacement = pool[rng.int(pool.length)];
    return {
      slot: slot.id,
      map: slot.map,
      from: bossIndex.get(slot.original).name,
      to: replacement.name,
      compatibility: candidates.length > 0 ? "safe" : "vanilla-fallback",
    };
  });
}

function randomizeItems(config) {
  const itemIndex = byId(items);
  const normalItems = items.filter((item) => !item.progression);
  const keyItems = items.filter((item) => item.progression);
  const keyLocations = itemLocations.filter(
    (location) => itemIndex.get(location.original).progression,
  );
  const assignments = new Map(
    itemLocations.map((location) => [location.id, itemIndex.get(location.original)]),
  );
  const keyDestinationIds = new Set();

  if (config.randomizeProtectedItems) {
    const rng = createStream(config.seed, "progression", config.version);
    const safeLocations = itemLocations
      .filter((location) => location.acceptsProgression)
      .sort((left, right) => left.sphere - right.sphere);
    const available = rng.shuffle(safeLocations.filter((location) => location.sphere <= 1));
    keyItems.forEach((item, index) => {
      const destination = available[index % available.length];
      const originalLocation = keyLocations.find(
        (location) => location.original === item.id,
      );
      const displacedItem = assignments.get(destination.id);
      assignments.set(destination.id, item);
      assignments.set(originalLocation.id, displacedItem);
      keyDestinationIds.add(destination.id);
    });
  }

  if (config.randomizeItems) {
    const rng = createStream(config.seed, "items", config.version);
    const targetLocations = itemLocations.filter(
      (location) => !keyDestinationIds.has(location.id),
    );
    const fixedKeyIds = new Set(
      config.randomizeProtectedItems
        ? []
        : keyLocations.map((location) => location.id),
    );
    const randomizableLocations = targetLocations.filter(
      (location) => !fixedKeyIds.has(location.id),
    );
    let shuffled = rng.shuffle(normalItems);

    const promoteCategory = (category, sphere, startIndex = 0) => {
      const targetIndex = randomizableLocations.findIndex(
        (location, index) => index >= startIndex && location.sphere <= sphere,
      );
      const itemIndexByCategory = shuffled.findIndex(
        (item, index) => index >= startIndex && item.category === category,
      );
      if (targetIndex >= 0 && itemIndexByCategory >= 0) {
        [randomizableLocations[startIndex], randomizableLocations[targetIndex]] = [
          randomizableLocations[targetIndex],
          randomizableLocations[startIndex],
        ];
        [shuffled[startIndex], shuffled[itemIndexByCategory]] = [
          shuffled[itemIndexByCategory],
          shuffled[startIndex],
        ];
      }
    };

    let guaranteed = 0;
    if (config.guaranteeEarlyWeapon) {
      promoteCategory("weapon", 0, guaranteed);
      guaranteed += 1;
    }
    if (config.balancedEarlyLoot) {
      promoteCategory("shield", 1, guaranteed);
      guaranteed += 1;
      promoteCategory("upgrade", 1, guaranteed);
    }
    randomizableLocations.forEach((location, index) => {
      assignments.set(location.id, shuffled[index % shuffled.length]);
    });
  }

  if (!config.randomizeItems && !config.randomizeProtectedItems) return [];

  return itemLocations.map((location) => {
    const original = itemIndex.get(location.original);
    const replacement = assignments.get(location.id);
    return {
      location: location.id,
      map: location.map,
      sphere: location.sphere,
      from: original.name,
      to: replacement.name,
      progression: replacement.progression,
      preserved: original.id === replacement.id,
    };
  });
}

function gwynNerfApplied(config) {
  return (
    (gwynNerfRejection[config.gwynNerf] ?? 0) > 0 &&
    createStream(config.seed, "gwyn-nerf", config.version).next() <
      gwynNerfRejection[config.gwynNerf]
  );
}

/**
 * The replacement types sampling-v1 can draw from for this catalog, for a custom-pool editor: one entry per regular
 * model with its derived size and difficulty class, the number of regular slots it has and the maps it occurs in.
 * Read-only and deterministic; it uses the same slot filter and class table as the planner (default options: Mimics
 * and the respawning mosquitoes count as regular enemies).
 */
export function describeRegularEnemyPool(inputConfig, gameCatalog) {
  if (!gameCatalog || gameCatalog.schemaVersion !== 17) {
    throw new Error("A catalog of schema 17 is required to describe the enemy pool.");
  }
  const config = normalizeConfig({ ...inputConfig, randomizeEnemies: true });
  const dragonPlan = buildDragonPlan(config, gameCatalog);
  const { slots } = regularEnemyContext(config, gameCatalog, dragonPlan, []);
  const table = deriveRegularClassTable(slots);
  const bySlots = new Map();
  for (const slot of slots) {
    const entry = bySlots.get(slot.modelName) ?? { slots: 0, maps: new Set() };
    entry.slots += 1;
    entry.maps.add(slot.mapId);
    bySlots.set(slot.modelName, entry);
  }
  return {
    classTableHash: table.hash,
    regularSlots: slots.length,
    entries: table.types.map((type) => ({
      model: type.model,
      size: type.size,
      difficulty: type.difficulty,
      slots: bySlots.get(type.model).slots,
      maps: [...bySlots.get(type.model).maps].sort(),
    })),
  };
}

/**
 * The one validation summary of a generated enemy placement (Part G): what was decided, what stayed vanilla, what the data tool will have to
 * write. Read-only and deterministic, derived from the placements and the planner's counters; it is not part of `placementHash`, not part of
 * any package digest, and never read back by the generator. The normal user does not see it: it goes to the manifest, the logs and the spoiler
 * metadata. `events` counts the custom events the placements ask the data tool for (the event window itself is measured when the tool writes the
 * files: `DS1TR_AUDIT_EVENTS`).
 */
export function summarizeEnemyPlacement(placements, diagnostics) {
  const enemies = placements.enemies ?? [];
  const bosses = placements.bosses ?? [];
  const all = [...enemies, ...bosses];
  const changed = all.filter((placement) => placement.changed);
  const maps = new Set(all.map((placement) => placement.map));
  const summary = diagnostics?.summary ?? null;
  const perMap = Object.values(summary?.maps ?? {});
  const wakeUps = all.filter((placement) => placement.forceCombatActivation === true).length;
  return {
    algorithm: diagnostics?.algorithm ?? "permutation-v1",
    maps: maps.size,
    placements: { regularSlotsPlaced: enemies.length, bossSlotsPlaced: bosses.length, changed: changed.length, keptVanilla: all.length - changed.length },
    models: { maxFinalModelsPerMap: perMap.length > 0 ? Math.max(...perMap.map((map) => map.finalModels ?? 0)) : null, mapsAboveTheLimit: summary?.counters?.mapsAboveCap ?? 0 },
    events: {
      wakeUp: wakeUps,
      passiveUntilAttacked: all.filter((placement) => placement.passiveUntilAttacked === true).length,
      disabledEntities: all.filter((placement) => placement.disableEntity === true).length,
      deferredOrContained: all.filter((placement) => placement.activationRegionId !== undefined || placement.combatRegionId !== undefined).length,
    },
    ownRows: {
      npcParam: all.filter((placement) => Number.isInteger(placement.scaledNpcParamId)).length,
      npcThinkParam: all.filter((placement) => Number.isInteger(placement.targetThinkParamId) && placement.targetThinkParamId >= 9_600_000).length,
    },
    roamingBosses: enemies.filter((placement) => placement.compatibility === "sampled-roaming-boss").length,
    hostileNpcs: enemies.filter((placement) => placement.hostileNpc === true).length,
    bossBodies: bosses.filter((placement) => placement.encounterBody === true).length,
    retries: summary?.retries?.length ?? 0,
    difficultyLapses: summary?.counters?.difficultyLapses ?? 0,
    classFallbacks: summary?.counters?.classFallbacks ?? 0,
    safetyExclusions: {
      bossSlotsKeptUnsupported: summary?.counters?.bossSlotsKeptUnsupported ?? 0,
      slotsKeptByChance: summary?.counters?.keptByChance ?? 0,
      slotsKeptByPool: summary?.counters?.keptByPool ?? 0,
    },
  };
}

export function generate(inputConfig, { gameCatalog = null } = {}) {
  const config = normalizeConfig(inputConfig);
  const errors = validateConfig(config);
  if (errors.length > 0) {
    throw new Error(errors.join("\n"));
  }
  const enemyPlacementAlgorithm = resolveEnemyPlacementAlgorithm(config);
  const placementDiagnostics = enemyPlacementAlgorithm === "sampling-v1"
    ? createPlacementDiagnostics({ algorithm: "sampling-v1", level: config.placementDiagnosticsLevel })
    : null;
  if (gameCatalog && gameCatalog.schemaVersion !== 17) {
    throw new Error(
      `Catalog schema ${gameCatalog.schemaVersion} is obsolete. ` +
        "Verify the clean game and import its data again.",
    );
  }

  const extractedData = Boolean(
    gameCatalog?.enemySlots?.length && gameCatalog?.enemyArchetypes?.length,
  );
  const dragonPlan = extractedData
    ? buildDragonPlan(config, gameCatalog)
    : null;
  const bossPlan = extractedData
    ? randomizeExtractedBosses(config, gameCatalog, dragonPlan, placementDiagnostics)
    : null;
  const itemLotPlan = extractedData
    ? randomizeExtractedItemLots(config, gameCatalog)
    : null;
  const result = {
    schemaVersion: 1,
    randomizerVersion: config.version,
    dataStatus: extractedData ? "extracted" : "prototype",
    warning: extractedData
      ? "Real game data detected. The output uses conservative slots and validated MSB/PARAM edits."
      : "This result uses development data and does not contain real IDs for a game patch.",
    seed: config.seed,
    config,
    placements: {
      enemies: extractedData
        ? planExtractedEnemies(
            enemyPlacementAlgorithm,
            config,
            gameCatalog,
            dragonPlan,
            bossPlan,
            placementDiagnostics,
          )
        : randomizePrototypeEnemies(config),
      bosses: extractedData
        ? bossPlan
        : randomizeBosses(config),
      items: extractedData
        ? itemLotPlan.items
        : randomizeItems(config),
      startingClasses: extractedData
        ? randomizeStartingClasses(config, gameCatalog)
        : [],
      gifts: extractedData ? itemLotPlan.gifts : [],
      enemyDrops: extractedData
        ? itemLotPlan.enemyDrops
        : [],
      shops: extractedData ? itemLotPlan.shops : [],
    },
    validation: {
      valid: true,
      finalBossReachable: !config.randomizeProtectedItems,
      notes: extractedData
        ? [
            "All hostile regular-enemy slots use a count-preserving global permutation within a 30-character-model total map budget; friendly NPCs and invisible technical helpers stay vanilla.",
            "Area scaling inherits destination combat stats and replaces hidden level multipliers from the selected enemy.",
            "Bosses use an unrestricted permutation and are grounded at their destination encounter; dragons only exchange complete linked dragon groups.",
            config.randomizeProtectedItems
              ? "Protected world items joined the full item pool; the two Asylum escape keys stayed vanilla."
              : "World items were randomized while protected progression lots stayed vanilla.",
            config.randomizeStartingClass
              ? "Starting-class base stats were redistributed."
              : "Starting-class base stats were preserved.",
            config.randomizeStartingEquipment
              ? "Asylum pickups were selected for each class's final stats."
              : "Starting loadouts were preserved.",
            config.randomizeGifts
              ? "Items granted by NPCs joined the enabled global item pool."
              : "NPC gifts were preserved.",
            config.randomizeEnemyDrops
              ? "Enemy drops joined the enabled global item pool."
              : "Enemy drops were preserved.",
            config.guaranteedEnemyDrops
              ? "Every catalogued enemy drop lot was normalized to a 100% total item chance."
              : "Enemy drop rates were preserved.",
            config.randomizeShops
              ? config.progressionLogic
                ? "Shop inventory joined the enabled global item pool; arrows, bolts, and throwable stock stayed shop-only, selected event-bound goods were randomized, and the remaining progression stock stayed protected."
                : "Shop inventory joined the enabled global item pool; arrows, bolts, and throwable stock stayed shop-only."
              : "Shops were preserved.",
            ...(config.fashionSouls
              ? []
              : ["Fashion Souls off: complete world armor sets were exchanged between set locations instead of splitting into single pieces."]),
            ...(config.replaceMimics
              ? []
              : ["Mimics were left in place and kept out of the enemy pool."]),
            ...(config.replaceRespawningMosquitoes
              ? []
              : ["The six respawning Blighttown mosquitoes were left in place and kept out of the enemy pool."]),
            ...(config.soulItems === "replaced"
              ? ["Soul items replaced: lesser souls (goods 400-409) were swapped for other consumables before shuffling."]
              : []),
            ...(config.randomizeStartingEquipment && config.startingItems !== "mixed-pool"
              ? [`Starting items (${config.startingItems}): left-hand pickups are shields${config.startingItems === "shield-1h" ? " and right-hand weapons are one-handed usable" : ""}.`]
              : []),
            ...(config.pinwheelChaos
              ? ["Pinwheel Chaos: the main Pinwheel stays; its clones join the enemy pool with their own stats."]
              : []),
            ...(config.gwynNerf !== "none" && config.randomizeBosses
              ? [`Gwyn nerf (${config.gwynNerf}): Lord Gwyn ${gwynNerfApplied(config) ? "was" : "was not"} excluded from the boss candidates for this seed.`]
              : []),
            ...(config.secondGargoyle === "replace" && config.randomizeBosses
              ? ["Replace second Gargoyle: the second Bell Gargoyle was replaced by an independently drawn boss."]
              : []),
            ...(config.secondGargoyle === "keep" && config.randomizeBosses
              ? ["Replace second Gargoyle off: the second Bell Gargoyle stays a Bell Gargoyle."]
              : []),
            ...(config.preventSameEnemy
              ? ["Prevent same enemy: no regular enemy slot received its own model (bosses never receive the same boss)."]
              : []),
          ]
        : config.randomizeProtectedItems
          ? ["Prototype progression was validated through the opening sphere; full graph validation remains pending."]
          : ["Key items were preserved."],
    },
  };

  result.placementHash = createHash("sha256")
    .update(JSON.stringify({
      placements: result.placements,
      guaranteedEnemyDrops: config.guaranteedEnemyDrops,
    }))
    .digest("hex");
  // Identity (and, for sampling-v1, the summary) of the planner; not part of placementHash.
  result.placementDiagnostics = placementDiagnostics ? placementDiagnostics.toJSON() : permutationDiagnostics();
  result.validation.enemyPlacement = summarizeEnemyPlacement(result.placements, result.placementDiagnostics);
  return result;
}
