import { randomBytes } from "node:crypto";

export const RANDOMIZER_VERSION = "0.5.66";

export const defaultConfig = Object.freeze({
  version: RANDOMIZER_VERSION,
  seed: "817293615",
  gameDirectory: "",
  outputDirectory: "output",
  lastPackageDirectory: "",
  randomizeEnemies: true,
  randomizeBosses: true,
  randomizeItems: true,
  randomizeProtectedItems: false,
  progressionLogic: true,
  enemyScaling: "area",
  guaranteeEarlyWeapon: true,
  balancedEarlyLoot: true,
  randomizeStartingClass: false,
  randomizeStartingEquipment: false,
  randomizeGifts: false,
  randomizeEnemyDrops: false,
  guaranteedEnemyDrops: false,
  randomizeShops: false,
  includeDlc: true,
  // Part H (patches/0023): key placement. "vanilla" keeps every key where the game puts it (the behavior before the option existed); "shuffled" places the
  // keys of FogMod's annotation with a progression-safe assumed fill over the exported progression model (keys.js). Lordvessel and Lord Souls join only
  // with their own switches. Needs the exported model (progressionModel) and the world items.
  // Part H (patches/0024): "race" and "race-plus" are shuffled-family modes (docs/RACE_MODE.md): the same fill, restricted to the race destinations;
  // "race-plus" also uses the skip-extended model. itemDifficulty ("fair" = neutral: no draw, current behavior; "unfair"; "very-unfair") orders the
  // candidate locations of every item by tier (docs/ITEM_DIFFICULTY_MODEL.md). laundromat randomizes the armor of NPC characters (docs/LAUNDROMAT_MIXUP.md).
  keyPlacement: "vanilla",
  itemDifficulty: "fair",
  laundromat: false,
  // Analysis only (the tier measurements of docs/ITEM_DIFFICULTY_MODEL.md): report the tier matrix of the layout without changing it. Never set by the product.
  measureTiers: false,
  keyRandomizeLordvessel: false,
  keyRandomizeLordSouls: false,
  progressionModel: null,
  generateSpoilerLog: true,
  // DS1TR legacy-parity options (patches/0002). Defaults reproduce the
  // unmodified upstream behavior exactly.
  fashionSouls: true,
  replaceMimics: true,
  replaceRespawningMosquitoes: true,
  preventSameEnemy: false,
  // Batch 2 (patches/0003). Engine defaults reproduce the unmodified behavior.
  soulItems: "shuffled",
  startingItems: "mixed-pool",
  pinwheelChaos: false,
  gwynNerf: "none",
  // Batch 3 (patches/0004). "single-boss" is the engine's earlier behavior
  // (the extra Bell Gargoyle bodies are disabled); "replace" and "keep" are the
  // legacy Replace second Gargoyle values 1 and 0.
  secondGargoyle: "single-boss",
  // Part 0 of the sampling work (patches/0005). Not an enum fallback: an unknown or unimplemented
  // value is refused by resolveEnemyPlacementAlgorithm() instead of being replaced by the default.
  enemyPlacementAlgorithm: "permutation-v1",
  // Part A (patches/0006): sampling-only options for regular enemy slots. The defaults are the values under which
  // permutation-v1 behaves as it always did; any other value is refused there (resolveEnemyPlacementOptions).
  enemyReplacementChance: 100,
  enemyNormalReplacement: "normals",
  enemyPlacementFit: "anywhere",
  // Part B (patches/0008): difficulty. "random" (legacy mode 2) and "loose" are neutral: permutation-v1 requires them.
  enemyDifficultyMode: "random",
  enemyDifficultyStrictness: "loose",
  // Part C1 (patches/0009): legacy Type replacement; "disabled" (legacy default 1) is neutral for permutation-v1.
  enemyTypeReplacement: "disabled",
  // Part C2 (patches/0010): custom enemy pool for the replacement types. null = the full regular pool (neutral for
  // permutation-v1); otherwise a non-empty list of unique model ids.
  enemyCustomPool: null,
  // Part D (patches/0012): boss sampling. "classic" (the existing boss planner, neutral for permutation-v1) or "bosses": every
  // single-body boss slot draws its own replacement from the portable boss pool. Unique bosses cycles through that pool.
  enemyBossReplacement: "classic",
  enemyUniqueBosses: false,
  // Part E2 (patches/0016): a regular enemy in a boss encounter slot. enemyBossReplacement may then also be "normals" or "both"; the
  // chance only applies with "both" (legacy "Boss chance: boss slots", default 90) and is refused otherwise (never ignored).
  enemyBossChanceBossSlots: 90,
  // Part F1 (patches/0017): legacy "Hostile NPCs". The Undead Merchant, Andre of Astora and Vamos (the authored hostile sources) join the
  // normal enemy pool as hostile combatants; the friendly NPCs themselves are never touched. Needs sampling-v1 with normal enemies replaced and
  // no custom pool; refused otherwise (never ignored).
  enemyHostileNpcs: false,
  // Part E1 (patches/0014): bosses in regular enemy slots ("roaming bosses"). enemyNormalReplacement may then be "bosses" or "both";
  // the three options below only apply with those values and are refused otherwise (never ignored). 10 / 50 / false are the legacy
  // defaults ("Boss chance: normal slots", "Roaming boss souls", "Respawning roaming bosses": respawn).
  enemyBossChanceNormalSlots: 10,
  enemyRoamingBossSouls: 50,
  enemyRoamingBossesStayDead: false,
  // Profile-controlled routing: old Seed Codes retain the v1 destination guards; new codes use dedicated safe handlers.
  enemyRoamingBossDestinationPolicy: "safe-handlers-v2",
  // Profile-controlled candidate and fit behavior. Historical Seed Codes pass legacy-v1 explicitly.
  enemyRoamingBossFitPolicy: "owner-approved-v1",
  // Items that the world randomizer (FogMod) follows by lot: null (no world randomizer) or
  // { items: [{ equipType, itemId, required }], lotStarts: [itemLotRowId] }. FogMod needs exactly one copy of each required
  // item in the lots it annotates (a start row or the rows that follow it without a gap) and at most one of any other;
  // see randomizeExtractedItemLots and docs/FOG_ITEM_TRACKING.md.
  fogTrackedItems: null,
  // "summary" keeps counters only; "detail" adds one record per slot (debugging).
  placementDiagnosticsLevel: "summary",
  useExtractedData: true,
  dryRun: true,
});

const booleanKeys = [
  "randomizeEnemies",
  "randomizeBosses",
  "randomizeItems",
  "randomizeProtectedItems",
  "progressionLogic",
  "guaranteeEarlyWeapon",
  "balancedEarlyLoot",
  "randomizeStartingClass",
  "randomizeStartingEquipment",
  "randomizeGifts",
  "randomizeEnemyDrops",
  "guaranteedEnemyDrops",
  "randomizeShops",
  "includeDlc",
  "generateSpoilerLog",
  "fashionSouls",
  "replaceMimics",
  "replaceRespawningMosquitoes",
  "preventSameEnemy",
  "pinwheelChaos",
  "keyRandomizeLordvessel",
  "keyRandomizeLordSouls",
  "laundromat",
  "measureTiers",
  "useExtractedData",
  "dryRun",
];

export function generateSeed() {
  return randomBytes(8).readBigUInt64LE().toString();
}

export function normalizeConfig(input = {}) {
  const config = { ...defaultConfig };
  for (const key of Object.keys(defaultConfig)) {
    if (Object.hasOwn(input, key)) config[key] = input[key];
  }

  config.seed = String(config.seed || generateSeed()).trim();
  config.version = RANDOMIZER_VERSION;
  config.gameDirectory = String(config.gameDirectory || "").trim();
  config.outputDirectory = String(config.outputDirectory || "output").trim();
  config.lastPackageDirectory = String(config.lastPackageDirectory || "").trim();
  config.progressionModel = config.progressionModel && typeof config.progressionModel === "object" ? config.progressionModel : null;
  if (!["protected-v1", "safe-handlers-v2"].includes(config.enemyRoamingBossDestinationPolicy)) {
    config.enemyRoamingBossDestinationPolicy = defaultConfig.enemyRoamingBossDestinationPolicy;
  }
  if (!["legacy-v1", "owner-approved-v1"].includes(config.enemyRoamingBossFitPolicy)) {
    config.enemyRoamingBossFitPolicy = defaultConfig.enemyRoamingBossFitPolicy;
  }
  config.enemyScaling = ["vanilla", "area", "progressive"].includes(config.enemyScaling)
    ? config.enemyScaling
    : defaultConfig.enemyScaling;

  for (const [key, allowed] of [
    ["soulItems", ["shuffled", "replaced", "transposed"]],
    ["startingItems", ["shield-1h", "shield-2h", "mixed-pool"]],
    ["gwynNerf", ["none", "medium", "strong"]],
    ["secondGargoyle", ["single-boss", "replace", "keep"]],
    ["keyPlacement", ["vanilla", "shuffled", "race", "race-plus"]],
    ["itemDifficulty", ["fair", "unfair", "very-unfair"]],
  ]) {
    if (!allowed.includes(config[key])) config[key] = defaultConfig[key];
  }

  for (const key of booleanKeys) {
    config[key] = Boolean(config[key]);
  }

  return config;
}

export function validateConfig(config, { requireGame = false } = {}) {
  const errors = [];
  if (!/^[\w.-]{1,64}$/u.test(config.seed)) {
    errors.push("The seed must contain 1-64 characters (letters, numbers, dot, hyphen, or underscore).");
  }
  if (requireGame && !config.gameDirectory) {
    errors.push("Select the Dark Souls Remastered game directory.");
  }
  if (
    !config.randomizeEnemies &&
    !config.randomizeBosses &&
    !config.randomizeItems &&
    !config.randomizeProtectedItems &&
    !config.randomizeStartingClass &&
    !config.randomizeStartingEquipment &&
    !config.randomizeGifts &&
    !config.randomizeEnemyDrops &&
    !config.guaranteedEnemyDrops &&
    !config.randomizeShops &&
    !config.laundromat
  ) {
    errors.push("Enable at least one category to randomize.");
  }
  if (config.itemDifficulty !== "fair" && !config.progressionModel) errors.push("ITEM_DIFFICULTY_MODEL_MISSING: item difficulty needs the progression model of the world data.");
  if (config.keyPlacement !== "vanilla") {
    if (!config.progressionModel) errors.push("KEY_PLACEMENT_MODEL_MISSING: key placement needs the progression model of the world data.");
    // The legacy GUI forces Fair under Race Mode + (the skips make the placement a puzzle of its own).
    if (config.keyPlacement === "race-plus" && config.itemDifficulty !== "fair") errors.push("RACE_PLUS_DIFFICULTY: Race Mode + is only available with Fair item difficulty.");
    if (!config.randomizeItems) errors.push("KEY_PLACEMENT_NEEDS_WORLD_ITEMS: key placement needs the world items.");
    if (config.randomizeProtectedItems) errors.push("KEY_PLACEMENT_CONFLICT: key placement and protected-item randomization cannot be combined.");
  } else if (config.keyRandomizeLordvessel || config.keyRandomizeLordSouls) {
    errors.push("KEY_PLACEMENT_OFF: the Lordvessel and Lord Souls switches need key placement.");
  }
  return errors;
}
