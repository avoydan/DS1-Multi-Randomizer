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
  // Part E1 (patches/0014): bosses in regular enemy slots ("roaming bosses"). enemyNormalReplacement may then be "bosses" or "both";
  // the three options below only apply with those values and are refused otherwise (never ignored). 10 / 50 / false are the legacy
  // defaults ("Boss chance: normal slots", "Roaming boss souls", "Respawning roaming bosses": respawn).
  enemyBossChanceNormalSlots: 10,
  enemyRoamingBossSouls: 50,
  enemyRoamingBossesStayDead: false,
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
  config.enemyScaling = ["vanilla", "area", "progressive"].includes(config.enemyScaling)
    ? config.enemyScaling
    : defaultConfig.enemyScaling;

  for (const [key, allowed] of [
    ["soulItems", ["shuffled", "replaced"]],
    ["startingItems", ["shield-1h", "shield-2h", "mixed-pool"]],
    ["gwynNerf", ["none", "medium", "strong"]],
    ["secondGargoyle", ["single-boss", "replace", "keep"]],
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
    !config.randomizeShops
  ) {
    errors.push("Enable at least one category to randomize.");
  }
  return errors;
}
