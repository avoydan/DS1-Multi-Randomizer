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
};

const enemyOptions = {
  enabled: true,
  randomizeRegularEnemies: true,
  randomizeBosses: true,
  scaling: 'progressive',
};

function createRequest(overrides = {}) {
  return {
    gamePath: 'C:\\games\\Dark Souls Remastered',
    workspacePath: 'C:\\workspaces\\run-1',
    masterSeed: 'master-seed',
    combinedSeed: 'combined-seed',
    items: { ...itemOptions },
    enemies: { ...enemyOptions },
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
