import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { DEFAULT_GENERATION_SETTINGS, joinConfig } from '@ds1tr/contracts';
import { canonicalizeSettings } from '../../dist/core/canonicalSettings.js';
import { validateRandomizerConfig } from '../../dist/core/configValidation.js';
import { exportPreset, importPreset } from '../../dist/core/preset.js';
import { decodeSeedCode, encodeSeedCode } from '../../dist/core/seedCode.js';
import { loadPersistedSettings, savePersistedSettings } from '../../dist/core/settingsStore.js';
import { fullSettings } from './seedTestSupport.mjs';

// Soul items "Transposed" (Part H) travels through every record that keeps settings: Seed Code, preset, saved settings. Nothing that exists changes.

const envelopeOf = (settings, masterSeed = 'souls-test') => ({ format: 'ds1tr-seed', formatVersion: 1, profile: 'ds1tr-gen-v1', masterSeed, settings });
const withSouls = (soulItems) => fullSettings({ items: { soulItems } });

test('Transposed is a valid setting and round-trips through a Seed Code; the codes of the two older modes are unchanged by its existence', () => {
  const settings = canonicalizeSettings(withSouls('transposed'));
  validateRandomizerConfig(joinConfig(settings, 'validation-only', 'validation-only', false));
  const code = encodeSeedCode(envelopeOf(settings));
  const decoded = decodeSeedCode(code);
  assert.equal(decoded.ok, true);
  assert.equal(decoded.envelope.settings.items.soulItems, 'transposed');
  assert.equal(decodeSeedCode(code).seedCode, code, 're-encoding is the identity');
  // The older modes keep the text they always had: pinned by the golden fixtures; here the decode must still read them as before.
  for (const soulItems of ['shuffled', 'replaced']) {
    const older = encodeSeedCode(envelopeOf(canonicalizeSettings(withSouls(soulItems))));
    assert.equal(decodeSeedCode(older).envelope.settings.items.soulItems, soulItems);
    assert.notEqual(older, code);
  }
});

test('the three modes are the only values: anything else is refused, never read as shuffled', () => {
  assert.throws(() => validateRandomizerConfig(joinConfig(canonicalizeSettings(withSouls('sublimated')), 'validation-only', 'validation-only', false)));
});

test('presets carry Transposed', () => {
  const exported = exportPreset(joinConfig(canonicalizeSettings(withSouls('transposed')), 'C:\\Game', 'preset-seed', false));
  assert.equal(exported.items.soulItems, 'transposed');
  const imported = importPreset(JSON.stringify(exported));
  assert.equal(imported.ok, true, (imported.problems ?? []).join(' '));
  assert.equal(imported.preset.items.soulItems, 'transposed');
  // An older preset without the key keeps meaning Shuffled: pinned by preset.test.mjs (version 1 and 2 migrations).
});

test('saved settings keep Transposed, and the product default stays Shuffled', async () => {
  assert.equal(DEFAULT_GENERATION_SETTINGS.items.soulItems, 'shuffled');
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-souls-'));
  try {
    await savePersistedSettings(root, { settings: withSouls('transposed'), generateSpoilerLog: false });
    assert.equal((await loadPersistedSettings(root)).persisted.settings.items.soulItems, 'transposed');
  } finally { await rm(root, { recursive: true, force: true }); }
});
