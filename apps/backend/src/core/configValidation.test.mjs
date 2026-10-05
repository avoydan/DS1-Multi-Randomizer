import assert from 'node:assert/strict';
import test from 'node:test';
import {
  effectiveCombinedEnabled,
  InvalidConfigurationError,
  validateRandomizerConfig,
} from '../../dist/core/configValidation.js';

function validConfig() {
  return {
    gamePath: 'C:\\DSR',
    masterSeed: 'test',
    items: {
      enabled: true,
      randomizeWorldItems: true,
      randomizeProtectedItems: false,
      progressionLogic: true,
      guaranteeEarlyWeapon: true,
      balancedEarlyLoot: true,
      randomizeStartingClass: false,
      randomizeStartingEquipment: false,
      randomizeGifts: false,
      randomizeEnemyDrops: false,
      guaranteedEnemyDrops: false,
      randomizeShops: false,
      fashionSouls: true,
      soulItems: 'shuffled',
      startingItems: 'shield-2h',
    },
    enemies: {
      enabled: true,
      randomizeRegularEnemies: true,
      randomizeBosses: true,
      scaling: 'area',
      replaceMimics: true,
      replaceRespawningMosquitoes: true,
      preventSameEnemy: false,
      pinwheelChaos: false,
      gwynNerf: 'medium',
      secondGargoyle: 'replace',
      placement: { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere' },
    },
    fog: {
      enabled: true,
      traversableFogs: true,
      bossFogs: true,
      areaWarps: true,
      majorPvpFogs: false,
      minorPvpFogs: false,
      lordvesselGates: false,
      randomStart: false,
      disconnectedFogs: false,
      noFallBedOfChaos: false,
      scaling: true,
      glitchedLogic: false,
      requireLordSouls: true,
      pacifistMode: false,
      language: 'ENGLISH',
    },
    output: { includeDlc: true, generateSpoilerLog: true },
  };
}

test('accepts a supported native configuration', () => {
  assert.doesNotThrow(() => validateRandomizerConfig(validConfig()));
});

test('blocks unsafe protected-item randomization', () => {
  const config = validConfig();
  config.items.randomizeProtectedItems = true;
  assert.throws(() => validateRandomizerConfig(config), /progression safety/);
});

test('rejects unsupported Fog language and random-start combination', () => {
  const config = validConfig();
  config.fog.language = 'KLINGON';
  assert.throws(() => validateRandomizerConfig(config), /language/i);

  config.fog.language = 'ENGLISH';
  config.fog.randomStart = true;
  config.fog.bossFogs = false;
  config.fog.traversableFogs = false;
  assert.throws(() => validateRandomizerConfig(config), /random start/i);
});

test('uses effective native categories instead of section toggles', () => {
  const config = validConfig();
  config.items.randomizeWorldItems = false;
  config.enemies.randomizeRegularEnemies = false;
  config.enemies.randomizeBosses = false;
  assert.equal(effectiveCombinedEnabled(config), false);
  config.fog.enabled = false;
  assert.throws(() => validateRandomizerConfig(config), /at least one/);
});

test('rejects wrong primitive types, missing fields, and unknown options', () => {
  const config = validConfig();
  config.items.enabled = 'yes';
  config.items.unsupportedOption = true;
  delete config.output.generateSpoilerLog;

  assert.throws(() => validateRandomizerConfig(config), (error) => {
    assert.ok(error instanceof InvalidConfigurationError);
    assert.ok(error.problems.some((problem) => problem.includes('unsupportedOption')));
    assert.ok(error.problems.some((problem) => problem.includes('enabled must be a boolean')));
    assert.ok(error.problems.some((problem) => problem.includes('generateSpoilerLog is required')));
    return true;
  });
});

test('requires the Batch 1 legacy-parity options as booleans', () => {
  for (const [section, key] of [
    ['items', 'fashionSouls'], ['enemies', 'replaceMimics'],
    ['enemies', 'replaceRespawningMosquitoes'], ['enemies', 'preventSameEnemy'],
  ]) {
    const missing = validConfig();
    delete missing[section][key];
    assert.throws(() => validateRandomizerConfig(missing), new RegExp(`config\.${section}\.${key} is required`));
    const wrongType = validConfig();
    wrongType[section][key] = 'yes';
    assert.throws(() => validateRandomizerConfig(wrongType), new RegExp(`config\.${section}\.${key} must be a boolean`));
  }
  const nonDefault = validConfig();
  nonDefault.items.fashionSouls = false;
  nonDefault.enemies.replaceMimics = false;
  nonDefault.enemies.replaceRespawningMosquitoes = false;
  nonDefault.enemies.preventSameEnemy = true;
  assert.doesNotThrow(() => validateRandomizerConfig(nonDefault));
});

test('requires the Batch 2 options and validates their values', () => {
  for (const [section, key] of [
    ['items', 'soulItems'], ['items', 'startingItems'], ['enemies', 'pinwheelChaos'], ['enemies', 'gwynNerf'],
  ]) {
    const missing = validConfig();
    delete missing[section][key];
    assert.throws(() => validateRandomizerConfig(missing), new RegExp(`config\\.${section}\\.${key} is required`));
  }
  const wrongBoolean = validConfig();
  wrongBoolean.enemies.pinwheelChaos = 'yes';
  assert.throws(() => validateRandomizerConfig(wrongBoolean), /config\.enemies\.pinwheelChaos must be a boolean/);
  for (const [section, key, bad] of [
    ['items', 'soulItems', 'transposed'], ['items', 'startingItems', 'shield-1h-2h'], ['enemies', 'gwynNerf', 'weak'],
  ]) {
    const invalid = validConfig();
    invalid[section][key] = bad;
    assert.throws(() => validateRandomizerConfig(invalid), /mode is invalid/, `${key}=${bad}`);
  }
  const valid = validConfig();
  valid.items.soulItems = 'replaced';
  valid.items.startingItems = 'mixed-pool';
  valid.enemies.pinwheelChaos = true;
  valid.enemies.gwynNerf = 'none';
  assert.doesNotThrow(() => validateRandomizerConfig(valid));
});

test('refuses to silently ignore Include DLC being turned off', () => {
  const config = validConfig();
  config.output.includeDlc = false;
  assert.throws(() => validateRandomizerConfig(config), /Excluding DLC content is not supported yet/);
});

test('requires the second Gargoyle mode and validates its value', () => {
  const missing = validConfig();
  delete missing.enemies.secondGargoyle;
  assert.throws(() => validateRandomizerConfig(missing), /config\.enemies\.secondGargoyle is required/);
  for (const bad of ['replaced', 'true', 'disabled', true]) {
    const invalid = validConfig();
    invalid.enemies.secondGargoyle = bad;
    assert.throws(() => validateRandomizerConfig(invalid), /Second Gargoyle mode is invalid/, String(bad));
  }
  for (const mode of ['replace', 'keep', 'single-boss']) {
    const valid = validConfig();
    valid.enemies.secondGargoyle = mode;
    assert.doesNotThrow(() => validateRandomizerConfig(valid), mode);
  }
});
