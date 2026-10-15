import type { BossReplacementMode, DifficultyMode, DifficultyStrictness, TypeReplacementMode, EnemyPlacementAlgorithm, NormalEnemyReplacementMode, PlacementFitMode, EnemyScaling, GwynNerfMode, SecondGargoyleMode, SoulItemsMode, StartingItemsMode } from '@ds1tr/contracts';
import { enemyPlacementUnavailableMessage, ENEMY_PLACEMENT_UNAVAILABLE, isEnemyPlacementImplemented } from '../core/enemyPlacement.js';
import type { CombinedRandomizerRequest } from './combinedRandomizerAdapter.js';
import type { FogTrackedItems } from './fogItemRules.js';

export interface UpstreamCombinedRandomizerConfig {
  seed: string;
  gameDirectory: string;
  outputDirectory: string;
  randomizeItems: boolean;
  randomizeProtectedItems: boolean;
  progressionLogic: boolean;
  guaranteeEarlyWeapon: boolean;
  balancedEarlyLoot: boolean;
  randomizeStartingClass: boolean;
  randomizeStartingEquipment: boolean;
  randomizeGifts: boolean;
  randomizeEnemyDrops: boolean;
  guaranteedEnemyDrops: boolean;
  randomizeShops: boolean;
  randomizeEnemies: boolean;
  randomizeBosses: boolean;
  enemyScaling: EnemyScaling;
  fashionSouls: boolean;
  replaceMimics: boolean;
  replaceRespawningMosquitoes: boolean;
  preventSameEnemy: boolean;
  soulItems: SoulItemsMode;
  startingItems: StartingItemsMode;
  pinwheelChaos: boolean;
  gwynNerf: GwynNerfMode;
  secondGargoyle: SecondGargoyleMode;
  enemyPlacementAlgorithm: EnemyPlacementAlgorithm;
  enemyReplacementChance: number;
  enemyNormalReplacement: NormalEnemyReplacementMode;
  enemyPlacementFit: PlacementFitMode;
  enemyDifficultyMode: DifficultyMode;
  enemyDifficultyStrictness: DifficultyStrictness;
  enemyTypeReplacement: TypeReplacementMode;
  /** Canonical custom pool (ascending unique model ids) or null for the full regular pool. */
  enemyCustomPool: string[] | null;
  enemyBossReplacement: BossReplacementMode;
  enemyUniqueBosses: boolean;
  enemyBossChanceBossSlots: number;
  enemyHostileNpcs: boolean;
  enemyBossChanceNormalSlots: number;
  enemyRoamingBossSouls: number;
  enemyRoamingBossesStayDead: boolean;
  enemyRoamingBossDestinationPolicy: 'protected-v1' | 'safe-handlers-v2';
  placementDiagnosticsLevel: 'summary' | 'detail';
  /** Items the world randomizer follows and the lots it can follow them in; null when it does not run. */
  fogTrackedItems: FogTrackedItems | null;
  /** Part H: key placement. "vanilla" is the engine's behavior without the option. */
  keyPlacement: 'vanilla' | 'shuffled' | 'race' | 'race-plus';
  /** Part I: item difficulty (fair = the engine's neutral behavior) and Laundromat Mixup. */
  itemDifficulty: 'fair' | 'unfair' | 'very-unfair';
  laundromat: boolean;
  keyRandomizeLordvessel: boolean;
  keyRandomizeLordSouls: boolean;
  /** The exported progression model; null unless key placement or an item difficulty needs it. */
  progressionModel: unknown | null;
  generateSpoilerLog: boolean;
}

export type CombinedRandomizerConfigMappingResult =
  | { ok: true; config: UpstreamCombinedRandomizerConfig }
  | { ok: false; code: 'NO_RANDOMIZATION_FEATURES_ENABLED' | typeof ENEMY_PLACEMENT_UNAVAILABLE; message: string };

export function mapCombinedRandomizerConfig(
  request: CombinedRandomizerRequest,
): CombinedRandomizerConfigMappingResult {
  // An algorithm that cannot run never reaches the engine, whatever else is selected (no silent fallback).
  const algorithm = request.enemies.placement.algorithm;
  if (!isEnemyPlacementImplemented(algorithm)) {
    return { ok: false, code: ENEMY_PLACEMENT_UNAVAILABLE, message: enemyPlacementUnavailableMessage(algorithm) };
  }
  const regularEnemies = request.enemies.enabled && request.enemies.randomizeRegularEnemies;
  // Lesser souls sit in world lots, NPC gifts and enemy drops; shops hold none.
  const itemPoolsWithSouls = request.items.enabled && (
    request.items.randomizeWorldItems || request.items.randomizeGifts || request.items.randomizeEnemyDrops
  );
  const keysOn = request.items.enabled && request.items.randomizeWorldItems && (request.items.keyPlacement ?? 'vanilla') !== 'vanilla';
  const config: UpstreamCombinedRandomizerConfig = {
    seed: request.combinedSeed,
    gameDirectory: request.gamePath,
    outputDirectory: request.workspacePath,
    randomizeItems: request.items.enabled && request.items.randomizeWorldItems,
    randomizeProtectedItems: request.items.enabled && request.items.randomizeProtectedItems,
    progressionLogic: request.items.enabled && request.items.progressionLogic,
    guaranteeEarlyWeapon: request.items.enabled && request.items.guaranteeEarlyWeapon,
    balancedEarlyLoot: request.items.enabled && request.items.balancedEarlyLoot,
    randomizeStartingClass: request.items.enabled && request.items.randomizeStartingClass,
    randomizeStartingEquipment: request.items.enabled && request.items.randomizeStartingEquipment,
    randomizeGifts: request.items.enabled && request.items.randomizeGifts,
    randomizeEnemyDrops: request.items.enabled && request.items.randomizeEnemyDrops,
    guaranteedEnemyDrops: request.items.enabled && request.items.guaranteedEnemyDrops,
    randomizeShops: request.items.enabled && request.items.randomizeShops,
    randomizeEnemies: request.enemies.enabled && request.enemies.randomizeRegularEnemies,
    randomizeBosses: request.enemies.enabled && request.enemies.randomizeBosses,
    enemyScaling: request.enemies.scaling,
    // Legacy-parity options only act when their parent category is on; otherwise they
    // carry the legacy defaults, which are the unmodified engine behavior.
    fashionSouls: !(request.items.enabled && request.items.randomizeWorldItems) || request.items.fashionSouls,
    replaceMimics: !regularEnemies || request.enemies.replaceMimics,
    replaceRespawningMosquitoes: !regularEnemies || request.enemies.replaceRespawningMosquitoes,
    preventSameEnemy: regularEnemies && request.enemies.preventSameEnemy,
    soulItems: itemPoolsWithSouls ? request.items.soulItems : 'shuffled',
    // 'mixed-pool' is the engine's behavior without the option; it is also what the option
    // falls back to while starting equipment is not randomized.
    startingItems: request.items.enabled && request.items.randomizeStartingEquipment
      ? request.items.startingItems
      : 'mixed-pool',
    pinwheelChaos: regularEnemies && request.enemies.pinwheelChaos,
    gwynNerf: request.enemies.enabled && request.enemies.randomizeBosses ? request.enemies.gwynNerf : 'none',
    // 'single-boss' is the engine's behavior without the option and the fallback while bosses are off.
    secondGargoyle: request.enemies.enabled && request.enemies.randomizeBosses ? request.enemies.secondGargoyle : 'single-boss',
    // Passed through unconditionally: the algorithm is part of the run's identity, not an enemy feature toggle.
    enemyPlacementAlgorithm: algorithm,
    enemyReplacementChance: request.enemies.placement.replacementChance,
    enemyNormalReplacement: request.enemies.placement.normalReplacement,
    enemyPlacementFit: request.enemies.placement.fit,
    enemyDifficultyMode: request.enemies.placement.difficultyMode,
    enemyDifficultyStrictness: request.enemies.placement.difficultyStrictness,
    enemyTypeReplacement: request.enemies.placement.typeReplacement,
    enemyCustomPool: request.enemies.placement.customPool === null ? null : [...request.enemies.placement.customPool].sort(),
    // Part D options are optional on input (a configuration that predates them means the classic boss planner).
    enemyBossReplacement: request.enemies.placement.bossReplacement ?? 'classic',
    enemyUniqueBosses: request.enemies.placement.uniqueBosses ?? false,
    // Part E2 option: optional on input too (the legacy default, which does nothing without Boss replacement "both").
    enemyBossChanceBossSlots: request.enemies.placement.bossChanceInBossSlots ?? 90,
    // Part F1 option: optional on input too (off is the world before the option existed).
    enemyHostileNpcs: request.enemies.placement.hostileNpcs ?? false,
    // Part E1 options are optional on input too (the legacy defaults, which do nothing without roaming bosses).
    enemyBossChanceNormalSlots: request.enemies.placement.bossChanceInNormalSlots ?? 10,
    enemyRoamingBossSouls: request.enemies.placement.roamingBossSouls ?? 50,
    enemyRoamingBossesStayDead: request.enemies.placement.roamingBossesStayDead ?? false,
    enemyRoamingBossDestinationPolicy: request.roamingBossDestinationPolicy ?? 'safe-handlers-v2',
    placementDiagnosticsLevel: request.placementDiagnosticsLevel ?? 'summary',
    fogTrackedItems: request.fogTrackedItems ?? null,
    // Part H options are optional on input (an older configuration means the keys stay where the game puts them).
    keyPlacement: keysOn ? (request.items.keyPlacement as UpstreamCombinedRandomizerConfig['keyPlacement']) : 'vanilla',
    itemDifficulty: request.items.enabled ? (request.items.itemDifficulty ?? 'fair') : 'fair',
    laundromat: request.items.enabled && (request.items.laundromat ?? false),
    keyRandomizeLordvessel: keysOn && (request.items.randomizeLordvessel ?? false),
    keyRandomizeLordSouls: keysOn && (request.items.randomizeLordSouls ?? false),
    progressionModel: keysOn || (request.items.enabled && (request.items.itemDifficulty ?? 'fair') !== 'fair') ? (request.progressionModel ?? null) : null,
    generateSpoilerLog: request.generateSpoilerLog,
  };

  const hasEnabledCategory = config.randomizeItems
    || config.randomizeStartingClass
    || config.randomizeStartingEquipment
    || config.randomizeGifts
    || config.randomizeEnemyDrops
    || config.randomizeShops
    || config.laundromat
    || config.randomizeEnemies
    || config.randomizeBosses;

  if (!hasEnabledCategory) {
    return {
      ok: false,
      code: 'NO_RANDOMIZATION_FEATURES_ENABLED',
      message: 'Enable at least one item or enemy randomization feature.',
    };
  }

  return { ok: true, config };
}
