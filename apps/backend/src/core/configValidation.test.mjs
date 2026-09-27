import assert from 'node:assert/strict';
import test from 'node:test';
import {
  effectiveCombinedEnabled,
  InvalidConfigurationError,
  validateRandomizerConfig,
} from './configValidation.ts';

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
    },
    enemies: {
      enabled: true,
      randomizeRegularEnemies: true,
      randomizeBosses: true,
      scaling: 'area',
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
