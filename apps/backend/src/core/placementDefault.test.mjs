import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import {
  DEFAULT_GENERATION_SETTINGS, LEGACY_ENEMY_PLACEMENT_OPTIONS, NEUTRAL_ENEMY_PLACEMENT_OPTIONS, SMOKE_SCENARIOS, SMOKE_TEST_BASE_SETTINGS, joinConfig, resolveScenarioSettings,
} from '@ds1tr/contracts';
import { canonicalizeSettings, canonicalSettingsJson } from '../../dist/core/canonicalSettings.js';
import { validateRandomizerConfig } from '../../dist/core/configValidation.js';
import { exportPreset, importPreset } from '../../dist/core/preset.js';
import { decodeSeedCode, encodeSeedCode } from '../../dist/core/seedCode.js';
import { loadPersistedSettings, savePersistedSettings } from '../../dist/core/settingsStore.js';
import { fullSettings } from './seedTestSupport.mjs';

// Part G: legacy-style sampling is the default of NEW configurations only. Everything that already carries an algorithm keeps it, and what an older
// record does not say is still read as the classic permutation (docs/SEED_FORMAT.md, "Defaults and old records").

const envelopeOf = (settings, masterSeed = 'default-test') => ({ format: 'ds1tr-seed', formatVersion: 1, profile: 'ds1tr-gen-v1', masterSeed, settings });

test('a fresh configuration is sampling with the legacy defaults and is valid; the value an older record is read with stays the classic permutation', () => {
  const placement = DEFAULT_GENERATION_SETTINGS.enemies.placement;
  assert.equal(placement.algorithm, 'sampling-v1');
  assert.deepEqual({ ...placement, algorithm: undefined }, { ...LEGACY_ENEMY_PLACEMENT_OPTIONS, algorithm: undefined }, 'the options are the legacy defaults the controls offer for sampling');
  validateRandomizerConfig(joinConfig(DEFAULT_GENERATION_SETTINGS, 'validation-only', 'validation-only', false));
  // What a missing key means never changes: that is how every old Seed Code, preset and settings file is read.
  assert.equal(NEUTRAL_ENEMY_PLACEMENT_OPTIONS.bossReplacement, 'classic');
  assert.equal(canonicalizeSettings(fullSettings()).enemies.placement.algorithm, 'permutation-v1');
  // A record from before an option existed lacks its key and is read with the neutral value, never with the new default.
  const older = fullSettings({ enemies: { placement: { algorithm: 'permutation-v1' } } });
  delete older.enemies.placement.hostileNpcs;
  delete older.enemies.placement.bossChanceInBossSlots;
  const filled = canonicalizeSettings(older).enemies.placement;
  assert.equal(filled.hostileNpcs, false);
  assert.equal(filled.bossChanceInBossSlots, 90);
  assert.equal(filled.algorithm, 'permutation-v1');
});

test('the test worlds are built from their own frozen base: the product default does not move them', () => {
  assert.equal(SMOKE_TEST_BASE_SETTINGS.enemies.placement.algorithm, 'permutation-v1');
  const second = SMOKE_SCENARIOS.find((scenario) => scenario.id === 'second-gargoyle');
  assert.equal(resolveScenarioSettings(second).enemies.placement.algorithm, 'permutation-v1', 'a scenario that does not name an algorithm keeps the classic one');
  const quick = SMOKE_SCENARIOS.find((scenario) => scenario.id === 'quick-roaming-basic');
  assert.equal(resolveScenarioSettings(quick).enemies.placement.algorithm, 'sampling-v1');
});

test('a saved setting keeps its algorithm: classic stays classic, sampling stays sampling, a first start gets the new default', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-default-'));
  try {
    assert.deepEqual(await loadPersistedSettings(root), { found: false, persisted: null, warning: null }, 'a first start has nothing saved: the app starts from the default');
    await savePersistedSettings(root, { settings: fullSettings(), generateSpoilerLog: false });
    const classic = await loadPersistedSettings(root);
    assert.equal(classic.persisted.settings.enemies.placement.algorithm, 'permutation-v1', 'an existing classic choice stays');
    await savePersistedSettings(root, { settings: fullSettings({ enemies: { placement: { algorithm: 'sampling-v1', fit: 'fits' } } }), generateSpoilerLog: false });
    assert.equal((await loadPersistedSettings(root)).persisted.settings.enemies.placement.algorithm, 'sampling-v1');
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('presets always state the algorithm: the new default exports as sampling, an old preset without a placement section imports as the classic permutation', () => {
  const exported = exportPreset(joinConfig(DEFAULT_GENERATION_SETTINGS, 'C:\\Game', 'preset-test-seed', false));
  assert.equal(exported.enemies.placement.algorithm, 'sampling-v1');
  const roundTrip = importPreset(JSON.stringify(exported));
  assert.equal(roundTrip.ok, true);
  assert.equal(roundTrip.preset.enemies.placement.algorithm, 'sampling-v1');
  // A version-4 preset (before the placement section existed) says nothing about it: it is the classic permutation, not the new default.
  const classicPreset = exportPreset(joinConfig(fullSettings(), 'C:\\Game', 'preset-test-seed', false));
  const old = JSON.parse(JSON.stringify(classicPreset));
  old.presetVersion = 4;
  for (const key of ['keyPlacement', 'randomizeLordvessel', 'randomizeLordSouls', 'itemDifficulty', 'laundromat']) delete old.items[key];
  delete old.enemies.placement;
  const imported = importPreset(JSON.stringify(old));
  assert.equal(imported.ok, true, (imported.problems ?? []).join(' '));
  assert.equal(imported.preset.enemies.placement.algorithm, 'permutation-v1');
});

test('Seed Codes carry the algorithm explicitly: the new default is a sampling code, an old classic code decodes to the classic permutation, neither is reinterpreted', () => {
  const sampling = encodeSeedCode(envelopeOf(canonicalizeSettings(DEFAULT_GENERATION_SETTINGS)));
  assert.match(canonicalSettingsJson(canonicalizeSettings(DEFAULT_GENERATION_SETTINGS)), /"algorithm":"sampling-v1"/u);
  const decoded = decodeSeedCode(sampling);
  assert.equal(decoded.ok, true);
  assert.equal(decoded.envelope.settings.enemies.placement.algorithm, 'sampling-v1');
  const classicSettings = canonicalizeSettings(fullSettings());
  const classic = decodeSeedCode(encodeSeedCode(envelopeOf(classicSettings)));
  assert.equal(classic.envelope.settings.enemies.placement.algorithm, 'permutation-v1');
  assert.equal(decodeSeedCode(sampling).seedCode, sampling, 're-encoding a sampling code is the identity');
});
