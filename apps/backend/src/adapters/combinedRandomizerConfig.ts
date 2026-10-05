import type { DifficultyMode, DifficultyStrictness, TypeReplacementMode, EnemyPlacementAlgorithm, NormalEnemyReplacementMode, PlacementFitMode, EnemyScaling, GwynNerfMode, SecondGargoyleMode, SoulItemsMode, StartingItemsMode } from '@ds1tr/contracts';
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
  placementDiagnosticsLevel: 'summary' | 'detail';
  /** Items the world randomizer follows and the lots it can follow them in; null when it does not run. */
  fogTrackedItems: FogTrackedItems | null;
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
    placementDiagnosticsLevel: request.placementDiagnosticsLevel ?? 'summary',
    fogTrackedItems: request.fogTrackedItems ?? null,
    generateSpoilerLog: request.generateSpoilerLog,
  };

  const hasEnabledCategory = config.randomizeItems
    || config.randomizeStartingClass
    || config.randomizeStartingEquipment
    || config.randomizeGifts
    || config.randomizeEnemyDrops
    || config.randomizeShops
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
