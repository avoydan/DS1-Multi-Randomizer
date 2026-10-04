import type { EnemyScaling } from '@ds1tr/contracts';
import type { CombinedRandomizerRequest } from './combinedRandomizerAdapter.js';

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
  generateSpoilerLog: boolean;
}

export type CombinedRandomizerConfigMappingResult =
  | { ok: true; config: UpstreamCombinedRandomizerConfig }
  | { ok: false; code: 'NO_RANDOMIZATION_FEATURES_ENABLED'; message: string };

export function mapCombinedRandomizerConfig(
  request: CombinedRandomizerRequest,
): CombinedRandomizerConfigMappingResult {
  const regularEnemies = request.enemies.enabled && request.enemies.randomizeRegularEnemies;
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
