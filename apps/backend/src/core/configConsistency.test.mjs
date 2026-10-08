// Configuration consistency audit (docs/FINAL_CONFIG_AUDIT.md): every setting that decides a world is the same thing in every place that knows about it, and the settings
// that are not part of a world never reach the generation. A setting that is added to one place and forgotten in another fails here.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import {
  CANONICAL_ITEM_EXTENSION_KEYS, CANONICAL_PLACEMENT_EXTENSION_KEYS, DEFAULT_GENERATION_SETTINGS, GENERATION_FIELD_POLICY, NEUTRAL_ENEMY_PLACEMENT_OPTIONS, NEUTRAL_KEY_OPTIONS,
  joinConfig,
} from '@ds1tr/contracts';
import { canonicalSettingsJson } from '../../dist/core/canonicalSettings.js';
import { exportPreset } from '../../dist/core/preset.js';
import { decodeSeedCode, encodeSeedCode } from '../../dist/core/seedCode.js';
import { prepareRun } from '../../dist/core/seededRun.js';

const desktop = new URL('../../../desktop/src/', import.meta.url);
const read = (relative) => readFileSync(new URL(relative, desktop), 'utf8');
const uiSource = ['App.tsx', 'EnemyPoolEditor.tsx'].map(read).join('\n');
const englishSettings = JSON.parse(read('locales/en/settings.json'));

const resolve = (object, path) => path.split('.').reduce((value, key) => (value === undefined || value === null ? undefined : value[key]), object);
const seedFields = Object.entries(GENERATION_FIELD_POLICY).filter(([, policy]) => policy === 'seed').map(([path]) => path);
const defaults = DEFAULT_GENERATION_SETTINGS;
const defaultConfig = joinConfig(defaults, 'C:/Games/DSR', 'audit-seed', false);

/** Settings that the interface offers through a control that is not a plain `config.<path>` read (a group of buttons, a pool editor, a derived switch). */
const UI_NOTES = {
  'enemies.placement.customPool': 'EnemyPoolEditor',
  'enemies.placement.algorithm': 'the algorithm choice sets the legacy and the classic option groups',
  'items.randomizeProtectedItems': 'blocked until progression safety is proven: validation refuses it, so the interface offers no control (a disabled placeholder was removed in 0.34.1)',
  'output.includeDlc': 'the DLC switch (locked on while the fog randomizer is on)',
};

test('every setting of a world has a default, is exported in a preset and survives a Seed Code', () => {
  const preset = exportPreset(defaultConfig);
  const decoded = decodeSeedCode(encodeSeedCode({ profile: 'ds1tr-gen-v1', masterSeed: 'audit-seed', settings: defaults }));
  assert.equal(decoded.ok, true);
  const problems = [];
  for (const path of seedFields) {
    if (resolve(defaults, path) === undefined) problems.push(`${path}: no default`);
    if (resolve(preset, path) === undefined) problems.push(`${path}: missing from an exported preset`);
    if (resolve(decoded.envelope.settings, path) === undefined) problems.push(`${path}: lost by the Seed Code`);
    if (JSON.stringify(resolve(decoded.envelope.settings, path)) !== JSON.stringify(resolve(defaults, path))) problems.push(`${path}: the Seed Code returns another value`);
  }
  assert.deepEqual(problems, []);
  assert.ok(seedFields.length > 50, 'the audit covers the whole configuration');
});

test('every setting of a world has a control in the interface, and the control has a name in both languages', () => {
  const problems = [];
  for (const path of seedFields) {
    const leaf = path.split('.').at(-1);
    const [section] = path.split('.');
    const patterns = [`config.${path}`, `config.${section}.${leaf}`, `placement.${leaf}`, `'${leaf}'`, `${leaf}:`];
    if (!UI_NOTES[path] && !patterns.some((pattern) => uiSource.includes(pattern))) problems.push(`${path}: no control found in the interface`);
  }
  assert.deepEqual(problems, []);
  // The names of the controls exist in the English and Polish resources with the same keys (the key-by-key check is src/i18n/locales.test.mjs).
  const polish = JSON.parse(read('locales/pl/settings.json'));
  const keys = (object, prefix = '') => Object.entries(object).flatMap(([key, value]) => (value && typeof value === 'object' ? keys(value, `${prefix}${key}.`) : [`${prefix}${key}`]));
  assert.deepEqual(keys(polish).sort(), keys(englishSettings).sort());
});

test('the policy of every setting is explicit: a setting is in the Seed Code, in the envelope, or documented as not deciding a world', () => {
  for (const [path, policy] of Object.entries(GENERATION_FIELD_POLICY)) {
    assert.ok(policy === 'seed' || policy === 'envelope' || (typeof policy === 'object' && policy.excluded.length > 20), `${path} has a policy`);
  }
  const leaves = (object, prefix = '') => Object.entries(object).flatMap(([key, value]) => (value && typeof value === 'object' && !Array.isArray(value) ? leaves(value, `${prefix}${key}.`) : [`${prefix}${key}`]));
  const configPaths = leaves(defaultConfig).filter((path) => !path.startsWith('masterSeed'));
  for (const path of configPaths) assert.ok(Object.hasOwn(GENERATION_FIELD_POLICY, path), `${path} is classified`);
});

test('the neutral value of every extension setting is what an old world meant, and a neutral value never reaches the Seed Code text', () => {
  for (const { key, neutral } of CANONICAL_ITEM_EXTENSION_KEYS) {
    assert.deepEqual(NEUTRAL_KEY_OPTIONS[key], neutral, `items.${key}`);
    assert.deepEqual(defaults.items[key], neutral, `a fresh installation has items.${key} at its neutral value`);
  }
  for (const { key, neutral } of CANONICAL_PLACEMENT_EXTENSION_KEYS) {
    assert.deepEqual(NEUTRAL_ENEMY_PLACEMENT_OPTIONS[key], neutral, `enemies.placement.${key}`);
    assert.deepEqual(defaults.enemies.placement[key], neutral, `a fresh installation has enemies.placement.${key} at its neutral value`);
  }
  const text = canonicalSettingsJson(defaults);
  for (const { key } of CANONICAL_ITEM_EXTENSION_KEYS) assert.doesNotMatch(text, new RegExp(`"${key}"`, 'u'));
  for (const { key } of CANONICAL_PLACEMENT_EXTENSION_KEYS) assert.doesNotMatch(text, new RegExp(`"${key}"`, 'u'));
});

test('a fresh installation: the DLC included, no Laundromat, no hostile NPCs, no bosses in regular slots, no Race, Fair difficulty', () => {
  assert.equal(defaults.output.includeDlc, true);
  assert.equal(defaults.items.laundromat, false);
  assert.equal(defaults.enemies.placement.hostileNpcs, false);
  assert.equal(defaults.enemies.placement.bossReplacement, 'classic');
  assert.equal(defaults.items.keyPlacement, 'vanilla');
  assert.equal(defaults.items.itemDifficulty, 'fair');
  assert.equal(defaults.items.randomizeLordvessel, false);
  assert.equal(defaults.items.randomizeLordSouls, false);
});

test('what is not part of a world never reaches the generation: the spoiler preference, the machine and the interface language change nothing', () => {
  const body = (extra) => ({ gamePath: 'C:/Games/DSR', masterSeed: 'neutral-1', ...defaults, output: { includeDlc: true, generateSpoilerLog: false, ...extra } });
  const plain = prepareRun(body({}));
  const spoiler = prepareRun({ ...body({ generateSpoilerLog: true }), gamePath: 'D:/Other/Steam/DSR' });
  assert.equal(plain.ok && spoiler.ok, true);
  assert.deepEqual(plain.envelope, spoiler.envelope);
  assert.equal(plain.seedCode, spoiler.seedCode, 'the same world has the same Seed Code whatever the spoiler preference or the install path');
  const text = canonicalSettingsJson(defaults);
  assert.doesNotMatch(text, /hint|diagnostic|appLanguage|interfaceLanguage|spoiler|gamePath/iu, 'no interface preference is in the identity of a world');
});

test('turning the DLC off changes the Seed Code and nothing else about the settings', () => {
  const off = structuredClone(defaults);
  off.fog.enabled = false;
  off.output.includeDlc = false;
  const on = structuredClone(off);
  on.output.includeDlc = true;
  const textOn = canonicalSettingsJson(on);
  const textOff = canonicalSettingsJson(off);
  assert.equal(textOn.replace('"includeDlc":true', '"includeDlc":false'), textOff);
});
