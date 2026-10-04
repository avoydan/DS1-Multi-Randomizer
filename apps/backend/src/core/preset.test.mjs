import assert from 'node:assert/strict';
import test from 'node:test';
import { exportPreset, importPreset } from '../../dist/core/preset.js';

function config() {
  return {
    gamePath: 'C:/DSR', masterSeed: ' seed-1 ',
    items: { enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true,
      guaranteeEarlyWeapon: true, balancedEarlyLoot: true, randomizeStartingClass: false,
      randomizeStartingEquipment: false, randomizeGifts: false, randomizeEnemyDrops: false,
      guaranteedEnemyDrops: false, randomizeShops: false },
    enemies: { enabled: true, randomizeRegularEnemies: true, randomizeBosses: true, scaling: 'progressive' },
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
  assert.match(importPreset(JSON.stringify({ ...preset, presetVersion: 2 })).problems.join(' '), /version/);
  assert.match(importPreset(JSON.stringify({ ...preset, gamePath: 'C:/x' })).problems.join(' '), /gamePath is not supported/);
  const unknown = { ...preset, fog: { ...preset.fog, bogus: true } };
  assert.match(importPreset(JSON.stringify(unknown)).problems.join(' '), /bogus is not supported/);
  const unsafe = { ...preset, items: { ...preset.items, randomizeProtectedItems: true } };
  assert.match(importPreset(JSON.stringify(unsafe)).problems.join(' '), /Protected-item/);
  const badLanguage = { ...preset, fog: { ...preset.fog, language: 'KLINGON' } };
  assert.equal(importPreset(JSON.stringify(badLanguage)).ok, false);
});
