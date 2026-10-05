import assert from 'node:assert/strict';
import test from 'node:test';
import { exportPreset, importPreset } from '../../dist/core/preset.js';

function config() {
  return {
    gamePath: 'C:/DSR', masterSeed: ' seed-1 ',
    items: { enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true,
      guaranteeEarlyWeapon: true, balancedEarlyLoot: true, randomizeStartingClass: false,
      randomizeStartingEquipment: false, randomizeGifts: false, randomizeEnemyDrops: false,
      guaranteedEnemyDrops: false, randomizeShops: false, fashionSouls: false, soulItems: 'replaced', startingItems: 'shield-1h' },
    enemies: { enabled: true, randomizeRegularEnemies: true, randomizeBosses: true, scaling: 'progressive', replaceMimics: false, replaceRespawningMosquitoes: true, preventSameEnemy: true, pinwheelChaos: true, gwynNerf: 'strong', secondGargoyle: 'keep', placement: { algorithm: 'permutation-v1' } },
    fog: { enabled: true, traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: true,
      minorPvpFogs: false, lordvesselGates: false, randomStart: false, disconnectedFogs: false,
      noFallBedOfChaos: false, scaling: true, glitchedLogic: false, requireLordSouls: true,
      pacifistMode: false, language: 'POLISH' },
    output: { includeDlc: true, generateSpoilerLog: false },
  };
}

test('export omits the game path and round-trips through import deterministically', () => {
  const preset = exportPreset(config());
  assert.equal('gamePath' in preset, false);
  assert.equal(preset.masterSeed, 'seed-1');
  const text = JSON.stringify(preset);
  const imported = importPreset(text);
  assert.equal(imported.ok, true);
  assert.deepEqual(imported.preset, preset);
  assert.equal(JSON.stringify(importPreset(text).preset), text);
});

test('import rejects malformed, foreign and unsupported presets', () => {
  assert.equal(importPreset('{').ok, false);
  assert.equal(importPreset('[]').ok, false);
  const preset = exportPreset(config());
  assert.match(importPreset(JSON.stringify({ ...preset, format: 'other' })).problems.join(' '), /format/);
  assert.match(importPreset(JSON.stringify({ ...preset, presetVersion: 6 })).problems.join(' '), /version/);
  assert.match(importPreset(JSON.stringify({ ...preset, gamePath: 'C:/x' })).problems.join(' '), /gamePath is not supported/);
  const unknown = { ...preset, fog: { ...preset.fog, bogus: true } };
  assert.match(importPreset(JSON.stringify(unknown)).problems.join(' '), /bogus is not supported/);
  const unsafe = { ...preset, items: { ...preset.items, randomizeProtectedItems: true } };
  assert.match(importPreset(JSON.stringify(unsafe)).problems.join(' '), /Protected-item/);
  const badLanguage = { ...preset, fog: { ...preset.fog, language: 'KLINGON' } };
  assert.equal(importPreset(JSON.stringify(badLanguage)).ok, false);
});

test('a version-1 preset is migrated with the legacy defaults', () => {
  const v2 = exportPreset(config());
  const v1 = JSON.parse(JSON.stringify(v2));
  v1.presetVersion = 1;
  delete v1.items.fashionSouls;
  for (const key of ['replaceMimics', 'replaceRespawningMosquitoes', 'preventSameEnemy']) delete v1.enemies[key];
  for (const key of ['soulItems', 'startingItems']) delete v1.items[key];
  for (const key of ['pinwheelChaos', 'gwynNerf', 'secondGargoyle', 'placement']) delete v1.enemies[key];
  const imported = importPreset(JSON.stringify(v1));
  assert.equal(imported.ok, true);
  assert.equal(imported.preset.presetVersion, 5);
  assert.equal(imported.preset.items.fashionSouls, true);
  assert.equal(imported.preset.enemies.replaceMimics, true);
  assert.equal(imported.preset.enemies.replaceRespawningMosquitoes, true);
  assert.equal(imported.preset.enemies.preventSameEnemy, false);
  // Batch 2 options migrate to the values that keep the old behavior, not the new legacy defaults.
  assert.equal(imported.preset.items.soulItems, 'shuffled');
  assert.equal(imported.preset.items.startingItems, 'mixed-pool');
  assert.equal(imported.preset.enemies.pinwheelChaos, false);
  assert.equal(imported.preset.enemies.gwynNerf, 'none');
  assert.deepEqual(imported.preset.enemies.placement, { algorithm: 'permutation-v1' });
  // A version-1 document must not already carry version-2 keys.
  const smuggled = JSON.parse(JSON.stringify(v1));
  smuggled.items.fashionSouls = false;
  assert.match(importPreset(JSON.stringify(smuggled)).problems.join(' '), /not supported in version 1/);
});

test('a version-2 preset round-trips the Batch 1 options', () => {
  const preset = exportPreset(config());
  assert.equal(preset.presetVersion, 5);
  assert.equal(preset.items.fashionSouls, false);
  assert.equal(preset.enemies.replaceMimics, false);
  assert.equal(preset.enemies.preventSameEnemy, true);
  const missing = JSON.parse(JSON.stringify(preset));
  delete missing.enemies.preventSameEnemy;
  assert.equal(importPreset(JSON.stringify(missing)).ok, false);
});

test('a version-2 preset is migrated with the behavior-preserving Batch 2 values', () => {
  const v3 = exportPreset(config());
  const v2 = JSON.parse(JSON.stringify(v3));
  v2.presetVersion = 2;
  for (const key of ['soulItems', 'startingItems']) delete v2.items[key];
  for (const key of ['pinwheelChaos', 'gwynNerf', 'secondGargoyle', 'placement']) delete v2.enemies[key];
  const imported = importPreset(JSON.stringify(v2));
  assert.equal(imported.ok, true);
  assert.equal(imported.preset.presetVersion, 5);
  assert.equal(imported.preset.items.fashionSouls, false, 'version-2 keys are kept');
  assert.equal(imported.preset.items.soulItems, 'shuffled');
  assert.equal(imported.preset.items.startingItems, 'mixed-pool');
  assert.equal(imported.preset.enemies.pinwheelChaos, false);
  assert.equal(imported.preset.enemies.gwynNerf, 'none');
  const smuggled = JSON.parse(JSON.stringify(v2));
  smuggled.enemies.gwynNerf = 'strong';
  assert.match(importPreset(JSON.stringify(smuggled)).problems.join(' '), /not supported in version 2/);
});

test('a preset that turns Include DLC off is rejected', () => {
  const preset = exportPreset(config());
  preset.output.includeDlc = false;
  assert.match(importPreset(JSON.stringify(preset)).problems.join(' '), /Excluding DLC content/);
});

test('presets of versions 1 to 3 migrate the second Gargoyle to the earlier single-boss behavior', () => {
  const v4 = exportPreset(config());
  assert.equal(v4.enemies.secondGargoyle, 'keep');
  for (const version of [1, 2, 3]) {
    const old = JSON.parse(JSON.stringify(v4));
    old.presetVersion = version;
    delete old.enemies.secondGargoyle;
    delete old.enemies.placement;
    if (version <= 2) {
      for (const key of ['soulItems', 'startingItems']) delete old.items[key];
      for (const key of ['pinwheelChaos', 'gwynNerf']) delete old.enemies[key];
    }
    if (version === 1) {
      delete old.items.fashionSouls;
      for (const key of ['replaceMimics', 'replaceRespawningMosquitoes', 'preventSameEnemy']) delete old.enemies[key];
    }
    const imported = importPreset(JSON.stringify(old));
    assert.equal(imported.ok, true, `version ${version}`);
    assert.equal(imported.preset.presetVersion, 5);
    // Not the new legacy default: the old preset keeps reproducing the old Bell Gargoyle handling.
    assert.equal(imported.preset.enemies.secondGargoyle, 'single-boss');
    const smuggled = JSON.parse(JSON.stringify(old));
    smuggled.enemies.secondGargoyle = 'replace';
    assert.match(importPreset(JSON.stringify(smuggled)).problems.join(' '), new RegExp(`not supported in version ${version}`));
  }
});

test('presets of versions 1 to 4 pin the permutation-v1 placement algorithm; version 5 states it explicitly', () => {
  const v5 = exportPreset(config());
  assert.equal(v5.presetVersion, 5);
  assert.deepEqual(v5.enemies.placement, { algorithm: 'permutation-v1' });
  for (const version of [1, 2, 3, 4]) {
    const old = JSON.parse(JSON.stringify(v5));
    old.presetVersion = version;
    delete old.enemies.placement;
    if (version <= 3) delete old.enemies.secondGargoyle;
    if (version <= 2) {
      for (const key of ['soulItems', 'startingItems']) delete old.items[key];
      for (const key of ['pinwheelChaos', 'gwynNerf']) delete old.enemies[key];
    }
    if (version === 1) {
      delete old.items.fashionSouls;
      for (const key of ['replaceMimics', 'replaceRespawningMosquitoes', 'preventSameEnemy']) delete old.enemies[key];
    }
    const imported = importPreset(JSON.stringify(old));
    assert.equal(imported.ok, true, `version ${version}`);
    assert.deepEqual(imported.preset.enemies.placement, { algorithm: 'permutation-v1' }, `version ${version}`);
    // An old document must not already carry the version-5 key.
    const smuggled = JSON.parse(JSON.stringify(old));
    smuggled.enemies.placement = { algorithm: 'sampling-v1' };
    assert.match(importPreset(JSON.stringify(smuggled)).problems.join(' '), new RegExp(`not supported in version ${version}`));
  }
});

test('a version-5 preset requires the placement algorithm and never turns into sampling-v1 silently', () => {
  const preset = exportPreset(config());
  const missing = JSON.parse(JSON.stringify(preset));
  delete missing.enemies.placement;
  assert.equal(importPreset(JSON.stringify(missing)).ok, false);
  const sampling = JSON.parse(JSON.stringify(preset));
  sampling.enemies.placement = { algorithm: 'sampling-v1' };
  const refused = importPreset(JSON.stringify(sampling));
  assert.equal(refused.ok, false);
  assert.match(refused.problems.join(' '), /ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE/);
  const unknown = JSON.parse(JSON.stringify(preset));
  unknown.enemies.placement = { algorithm: 'bogus' };
  assert.equal(importPreset(JSON.stringify(unknown)).ok, false);
});
