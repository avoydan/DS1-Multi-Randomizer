import assert from 'node:assert/strict';
import test from 'node:test';
import { mapCombinedRandomizerConfig } from '../../dist/adapters/combinedRandomizerConfig.js';

const itemOptions = {
  enabled: true,
  randomizeWorldItems: true,
  randomizeProtectedItems: true,
  progressionLogic: true,
  guaranteeEarlyWeapon: true,
  balancedEarlyLoot: true,
  randomizeStartingClass: true,
  randomizeStartingEquipment: true,
  randomizeGifts: true,
  randomizeEnemyDrops: true,
  guaranteedEnemyDrops: true,
  randomizeShops: true,
  fashionSouls: false,
  soulItems: 'replaced',
  startingItems: 'shield-1h',
};

const enemyOptions = {
  enabled: true,
  randomizeRegularEnemies: true,
  randomizeBosses: true,
  scaling: 'progressive',
  replaceMimics: false,
  replaceRespawningMosquitoes: false,
  preventSameEnemy: true,
  pinwheelChaos: true,
  gwynNerf: 'strong',
  secondGargoyle: 'keep',
  placement: { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null },
};

function createRequest(overrides = {}) {
  return {
    gamePath: 'C:\\games\\Dark Souls Remastered',
    workspacePath: 'C:\\workspaces\\run-1',
    masterSeed: 'master-seed',
    combinedSeed: 'combined-seed',
    items: { ...itemOptions },
    enemies: { ...enemyOptions },
    generateSpoilerLog: true,
    ...overrides,
  };
}

test('maps enabled items and disables every enemy feature', () => {
  const result = mapCombinedRandomizerConfig(createRequest({
    enemies: { ...enemyOptions, enabled: false },
  }));

  assert.equal(result.ok, true);
  assert.equal(result.config.randomizeItems, true);
  assert.equal(result.config.randomizeShops, true);
  assert.equal(result.config.randomizeEnemies, false);
  assert.equal(result.config.randomizeBosses, false);
  assert.equal(result.config.enemyScaling, 'progressive');
});

test('maps enabled enemies and disables every item feature', () => {
  const result = mapCombinedRandomizerConfig(createRequest({
    items: { ...itemOptions, enabled: false },
  }));

  assert.equal(result.ok, true);
  assert.equal(result.config.randomizeItems, false);
  assert.equal(result.config.progressionLogic, false);
  assert.equal(result.config.randomizeShops, false);
  assert.equal(result.config.randomizeEnemies, true);
  assert.equal(result.config.randomizeBosses, true);
});

test('maps both enabled capabilities deterministically', () => {
  const request = createRequest();
  const first = mapCombinedRandomizerConfig(request);
  const second = mapCombinedRandomizerConfig(request);

  assert.deepEqual(first, second);
  assert.deepEqual(first, {
    ok: true,
    config: {
      seed: 'combined-seed',
      gameDirectory: 'C:\\games\\Dark Souls Remastered',
      outputDirectory: 'C:\\workspaces\\run-1',
      randomizeItems: true,
      randomizeProtectedItems: true,
      progressionLogic: true,
      guaranteeEarlyWeapon: true,
      balancedEarlyLoot: true,
      randomizeStartingClass: true,
      randomizeStartingEquipment: true,
      randomizeGifts: true,
      randomizeEnemyDrops: true,
      guaranteedEnemyDrops: true,
      randomizeShops: true,
      randomizeEnemies: true,
      randomizeBosses: true,
      enemyScaling: 'progressive',
      fashionSouls: false,
      replaceMimics: false,
      replaceRespawningMosquitoes: false,
      preventSameEnemy: true,
      soulItems: 'replaced',
      startingItems: 'shield-1h',
      pinwheelChaos: true,
      gwynNerf: 'strong',
      secondGargoyle: 'keep',
      enemyPlacementAlgorithm: 'permutation-v1',
      enemyReplacementChance: 100,
      enemyNormalReplacement: 'normals',
      enemyPlacementFit: 'anywhere',
      enemyDifficultyMode: 'random',
      enemyDifficultyStrictness: 'loose',
      enemyTypeReplacement: 'disabled',
      enemyCustomPool: null,
      placementDiagnosticsLevel: 'summary',
      fogTrackedItems: null,
      generateSpoilerLog: true,
    },
  });
});

test('ANDs nested options with a disabled parent', () => {
  const result = mapCombinedRandomizerConfig(createRequest({
    items: { ...itemOptions, enabled: false },
  }));

  assert.equal(result.ok, true);
  for (const key of [
    'randomizeItems',
    'randomizeProtectedItems',
    'progressionLogic',
    'guaranteeEarlyWeapon',
    'balancedEarlyLoot',
    'randomizeStartingClass',
    'randomizeStartingEquipment',
    'randomizeGifts',
    'randomizeEnemyDrops',
    'guaranteedEnemyDrops',
    'randomizeShops',
  ]) {
    assert.equal(result.config[key], false, `${key} should be disabled`);
  }
});

test('rejects a request with every randomization category disabled', () => {
  const result = mapCombinedRandomizerConfig(createRequest({
    items: { ...itemOptions, enabled: false },
    enemies: { ...enemyOptions, enabled: false },
  }));

  assert.deepEqual(result, {
    ok: false,
    code: 'NO_RANDOMIZATION_FEATURES_ENABLED',
    message: 'Enable at least one item or enemy randomization feature.',
  });
});

test('rejects enabled parent options when only non-category safeguards are selected', () => {
  const result = mapCombinedRandomizerConfig(createRequest({
    items: {
      ...itemOptions,
      randomizeWorldItems: false,
      randomizeStartingClass: false,
      randomizeStartingEquipment: false,
      randomizeGifts: false,
      randomizeEnemyDrops: false,
      randomizeShops: false,
      fashionSouls: true,
      soulItems: 'shuffled',
      startingItems: 'shield-2h',
    },
    enemies: {
      ...enemyOptions,
      randomizeRegularEnemies: false,
      randomizeBosses: false,
    },
  }));

  assert.equal(result.ok, false);
  assert.equal(result.code, 'NO_RANDOMIZATION_FEATURES_ENABLED');
});

test('passes the spoiler-log flag through independently of the categories', () => {
  for (const generateSpoilerLog of [true, false]) {
    const result = mapCombinedRandomizerConfig(createRequest({ generateSpoilerLog }));

    assert.equal(result.ok, true);
    assert.equal(result.config.generateSpoilerLog, generateSpoilerLog);
  }
});

test('legacy-parity options fall back to the legacy defaults when their parent is off', () => {
  const noWorldItems = mapCombinedRandomizerConfig(createRequest({
    items: { ...itemOptions, randomizeWorldItems: false },
  }));
  assert.equal(noWorldItems.ok, true);
  assert.equal(noWorldItems.config.fashionSouls, true);

  const noItems = mapCombinedRandomizerConfig(createRequest({ items: { ...itemOptions, enabled: false } }));
  assert.equal(noItems.config.fashionSouls, true);

  const noRegular = mapCombinedRandomizerConfig(createRequest({
    enemies: { ...enemyOptions, randomizeRegularEnemies: false },
  }));
  assert.equal(noRegular.ok, true);
  assert.equal(noRegular.config.replaceMimics, true);
  assert.equal(noRegular.config.replaceRespawningMosquitoes, true);
  assert.equal(noRegular.config.preventSameEnemy, false);

  const noEnemies = mapCombinedRandomizerConfig(createRequest({ enemies: { ...enemyOptions, enabled: false } }));
  assert.equal(noEnemies.config.preventSameEnemy, false);
  assert.equal(noEnemies.config.replaceMimics, true);
});

test('legacy-parity options are forwarded when their parents are on', () => {
  const result = mapCombinedRandomizerConfig(createRequest({
    items: { ...itemOptions, fashionSouls: true, soulItems: 'shuffled', startingItems: 'shield-2h' },
    enemies: { ...enemyOptions, replaceMimics: true, replaceRespawningMosquitoes: true, preventSameEnemy: false, pinwheelChaos: false, gwynNerf: 'medium', secondGargoyle: 'replace', placement: { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null } },
  }));
  assert.equal(result.config.fashionSouls, true);
  assert.equal(result.config.replaceMimics, true);
  assert.equal(result.config.replaceRespawningMosquitoes, true);
  assert.equal(result.config.preventSameEnemy, false);
});

test('Batch 2 options map through when their parents are on', () => {
  const result = mapCombinedRandomizerConfig(createRequest());
  assert.equal(result.config.soulItems, 'replaced');
  assert.equal(result.config.startingItems, 'shield-1h');
  assert.equal(result.config.pinwheelChaos, true);
  assert.equal(result.config.gwynNerf, 'strong');
});

test('Batch 2 options fall back to the behavior-preserving values when their parent is off', () => {
  // Starting items need starting equipment; soul items need a pool that holds souls.
  const noEquipment = mapCombinedRandomizerConfig(createRequest({
    items: { ...itemOptions, randomizeStartingEquipment: false },
  }));
  assert.equal(noEquipment.config.startingItems, 'mixed-pool');
  assert.equal(noEquipment.config.soulItems, 'replaced');

  const shopsOnly = mapCombinedRandomizerConfig(createRequest({
    items: { ...itemOptions, randomizeWorldItems: false, randomizeGifts: false, randomizeEnemyDrops: false },
  }));
  assert.equal(shopsOnly.config.soulItems, 'shuffled');

  const noItems = mapCombinedRandomizerConfig(createRequest({ items: { ...itemOptions, enabled: false } }));
  assert.equal(noItems.config.soulItems, 'shuffled');
  assert.equal(noItems.config.startingItems, 'mixed-pool');

  // Pinwheel Chaos needs regular enemies; the Gwyn nerf needs boss randomization.
  const noRegular = mapCombinedRandomizerConfig(createRequest({
    enemies: { ...enemyOptions, randomizeRegularEnemies: false },
  }));
  assert.equal(noRegular.config.pinwheelChaos, false);
  assert.equal(noRegular.config.gwynNerf, 'strong');

  const noBosses = mapCombinedRandomizerConfig(createRequest({
    enemies: { ...enemyOptions, randomizeBosses: false },
  }));
  assert.equal(noBosses.config.gwynNerf, 'none');
  assert.equal(noBosses.config.pinwheelChaos, true);

  const noEnemies = mapCombinedRandomizerConfig(createRequest({ enemies: { ...enemyOptions, enabled: false } }));
  assert.equal(noEnemies.config.pinwheelChaos, false);
  assert.equal(noEnemies.config.gwynNerf, 'none');
});

test('the second Gargoyle mode maps through with boss randomization and falls back without it', () => {
  const on = mapCombinedRandomizerConfig(createRequest());
  assert.equal(on.config.secondGargoyle, 'keep');
  for (const mode of ['replace', 'keep', 'single-boss']) {
    const result = mapCombinedRandomizerConfig(createRequest({ enemies: { ...enemyOptions, secondGargoyle: mode } }));
    assert.equal(result.config.secondGargoyle, mode);
  }
  const noBosses = mapCombinedRandomizerConfig(createRequest({ enemies: { ...enemyOptions, randomizeBosses: false } }));
  assert.equal(noBosses.config.secondGargoyle, 'single-boss');
  const noEnemies = mapCombinedRandomizerConfig(createRequest({ enemies: { ...enemyOptions, enabled: false } }));
  assert.equal(noEnemies.config.secondGargoyle, 'single-boss');
});

test('the mapper passes both algorithms and the sampling settings, and refuses an algorithm that cannot run', () => {
  const ok = mapCombinedRandomizerConfig(createRequest({ enemies: { ...enemyOptions, placement: { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null } } }));
  assert.equal(ok.ok, true);
  assert.equal(ok.config.enemyPlacementAlgorithm, 'permutation-v1');
  const sampling = mapCombinedRandomizerConfig(createRequest({
    enemies: { ...enemyOptions, placement: { algorithm: 'sampling-v1', replacementChance: 35, normalReplacement: 'none', fit: 'fits', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null } },
    placementDiagnosticsLevel: 'detail',
  }));
  assert.equal(sampling.ok, true);
  assert.equal(sampling.config.enemyPlacementAlgorithm, 'sampling-v1');
  assert.equal(sampling.config.enemyReplacementChance, 35);
  assert.equal(sampling.config.enemyNormalReplacement, 'none');
  assert.equal(sampling.config.enemyPlacementFit, 'fits');
  assert.equal(sampling.config.enemyDifficultyMode, 'random');
  assert.equal(sampling.config.placementDiagnosticsLevel, 'detail');
  const refused = mapCombinedRandomizerConfig(createRequest({ enemies: { ...enemyOptions, placement: { algorithm: 'sampling-v2', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null } } }));
  assert.equal(refused.ok, false);
  assert.equal(refused.code, 'ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE');
  assert.match(refused.message, /not implemented yet/);
});
