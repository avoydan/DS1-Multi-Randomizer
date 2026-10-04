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
};

const enemyOptions = {
  enabled: true,
  randomizeRegularEnemies: true,
  randomizeBosses: true,
  scaling: 'progressive',
  replaceMimics: false,
  replaceRespawningMosquitoes: false,
  preventSameEnemy: true,
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
    items: { ...itemOptions, fashionSouls: true },
    enemies: { ...enemyOptions, replaceMimics: true, replaceRespawningMosquitoes: true, preventSameEnemy: false },
  }));
  assert.equal(result.config.fashionSouls, true);
  assert.equal(result.config.replaceMimics, true);
  assert.equal(result.config.replaceRespawningMosquitoes, true);
  assert.equal(result.config.preventSameEnemy, false);
});
