// Shared helpers of the Seed Code tests: a complete configuration as the desktop app builds it (every option explicit).

export function fullSettings(overrides = {}) {
  const base = {
    items: {
      enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true, guaranteeEarlyWeapon: true,
      balancedEarlyLoot: true, randomizeStartingClass: false, randomizeStartingEquipment: false, randomizeGifts: false,
      randomizeEnemyDrops: false, guaranteedEnemyDrops: false, randomizeShops: false, fashionSouls: true, soulItems: 'shuffled',
      startingItems: 'shield-2h',
    },
    enemies: {
      enabled: true, randomizeRegularEnemies: true, randomizeBosses: true, scaling: 'area', replaceMimics: true,
      replaceRespawningMosquitoes: true, preventSameEnemy: false, pinwheelChaos: false, gwynNerf: 'medium', secondGargoyle: 'replace',
      placement: {
        algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random',
        difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false,
      },
    },
    fog: {
      enabled: true, traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: false, minorPvpFogs: false,
      lordvesselGates: false, randomStart: false, disconnectedFogs: false, noFallBedOfChaos: false, scaling: true, glitchedLogic: false,
      requireLordSouls: true, pacifistMode: false, language: 'ENGLISH',
    },
    output: { includeDlc: true },
  };
  const merged = structuredClone(base);
  for (const [section, patch] of Object.entries(overrides)) {
    if (section === 'enemies' && patch.placement) {
      const { placement, ...rest } = patch;
      Object.assign(merged.enemies, rest);
      Object.assign(merged.enemies.placement, placement);
    } else {
      Object.assign(merged[section], patch);
    }
  }
  return merged;
}

/** The full request body the app sends (a RandomizerConfig; the master seed is empty until Randomize is clicked). */
export function fullConfig(overrides = {}, { gamePath = 'C:\\Games\\DSR', masterSeed = '', spoiler = false } = {}) {
  return { gamePath, masterSeed, ...fullSettings(overrides), output: { includeDlc: true, generateSpoilerLog: spoiler } };
}

export const BROAD_ITEMS = {
  randomizeStartingClass: true, randomizeStartingEquipment: true, randomizeGifts: true, randomizeEnemyDrops: true,
  guaranteedEnemyDrops: true, randomizeShops: true,
};
