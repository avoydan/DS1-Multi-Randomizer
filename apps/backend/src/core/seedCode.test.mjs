import assert from 'node:assert/strict';
import test from 'node:test';
import {
  CANONICAL_ENEMY_KEYS, CANONICAL_FOG_KEYS, CANONICAL_ITEM_KEYS, CANONICAL_OUTPUT_KEYS, CANONICAL_PLACEMENT_KEYS, GENERATION_FIELD_POLICY,
  isLoadedSeedModified, joinConfig, splitConfig,
} from '@ds1tr/contracts';
import { canonicalSettingsJson, canonicalizeSettings } from '../../dist/core/canonicalSettings.js';
import { validateRandomizerConfig } from '../../dist/core/configValidation.js';
import { base32Decode, base32Encode, decodeSeedCode, encodeSeedCode, serializeEnvelope } from '../../dist/core/seedCode.js';
import { prepareRun } from '../../dist/core/seededRun.js';
import { BROAD_ITEMS, fullConfig, fullSettings } from './seedTestSupport.mjs';

const PROFILE = 'ds1tr-gen-v1';
const envelopeOf = (settings, masterSeed = '4118795099') => ({ profile: PROFILE, masterSeed, settings });
const broad = () => fullSettings({
  items: BROAD_ITEMS,
  enemies: { placement: { algorithm: 'sampling-v1', replacementChance: 60, fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'strict', typeReplacement: 'enabled', customPool: ['c2500', 'c1200', 'c3200'] } },
});

test('a Seed Code round-trips: prefix, profile, master seed and every setting come back unchanged', () => {
  const settings = broad();
  const code = encodeSeedCode(envelopeOf(settings));
  assert.match(code, /^DS1TR1-[0-9A-HJKMNP-TV-Z]{8}(-[0-9A-HJKMNP-TV-Z]{1,8})+$/u);
  assert.ok(code.length < 400, `a broad configuration stays compact (${code.length} characters)`);
  const decoded = decodeSeedCode(code);
  assert.equal(decoded.ok, true);
  assert.equal(decoded.seedCode, code);
  assert.equal(decoded.envelope.profile, PROFILE);
  assert.equal(decoded.envelope.masterSeed, '4118795099');
  assert.equal(decoded.envelope.formatVersion, 1);
  assert.deepEqual(decoded.envelope.settings, canonicalizeSettings(settings));
  assert.deepEqual(decoded.envelope.settings.enemies.placement.customPool, ['c1200', 'c2500', 'c3200'], 'the custom pool is stored by stable model ids, sorted');
});

test('the transport tolerates how people copy codes: case, spaces, line breaks, missing dashes, look-alike letters', () => {
  const code = encodeSeedCode(envelopeOf(broad()));
  const mangled = `  ${code.toLowerCase().replaceAll('-', ' ')}\n`;
  assert.equal(decodeSeedCode(mangled).seedCode, code);
  assert.equal(decodeSeedCode(code.replaceAll('-', '')).seedCode, code);
  const lookalike = `DS1TR1-${code.slice(7).replace(/0/gu, 'O').replace(/1/gu, 'l')}`;
  assert.equal(decodeSeedCode(lookalike).seedCode, code);
  assert.deepEqual([...base32Decode(base32Encode(Uint8Array.from([0, 1, 2, 250, 255, 7])))], [0, 1, 2, 250, 255, 7]);
});

test('damage is detected: no single changed character decodes to another configuration', () => {
  const code = encodeSeedCode(envelopeOf(broad()));
  const alphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
  let checked = 0;
  for (let index = 0; index < code.length; index += 1) {
    if (code[index] === '-' || index < 7) continue;
    const replacement = alphabet[(alphabet.indexOf(code[index]) + 7) % alphabet.length];
    const damaged = decodeSeedCode(code.slice(0, index) + replacement + code.slice(index + 1));
    if (damaged.ok) {
      // Only the unused padding bits of the final character can change without changing a byte; the world is still the same one.
      assert.equal(index, code.length - 1);
      assert.equal(damaged.seedCode, code);
      continue;
    }
    assert.ok(['CHECKSUM', 'MALFORMED'].includes(damaged.code));
    assert.match(damaged.message, /invalid|damaged|not a valid/u);
    assert.doesNotMatch(damaged.message, /\bsha\b|checksum|crc/iu, 'the person sees no technical term');
    checked += 1;
  }
  assert.ok(checked > 100);
  const truncated = decodeSeedCode(code.slice(0, code.length - 6));
  assert.equal(truncated.ok, false);
  assert.equal(decodeSeedCode('hello world').code, 'MALFORMED');
  assert.equal(decodeSeedCode('').ok, false);
  assert.equal(decodeSeedCode('DS1TR1-').ok, false);
});

test('canonical serialization is stable: the same configuration always gives the same bytes and the same code', () => {
  const settings = broad();
  const reordered = {
    output: { includeDlc: true },
    fog: Object.fromEntries(Object.entries(settings.fog).reverse()),
    enemies: { ...Object.fromEntries(Object.entries(settings.enemies).reverse()), placement: { ...Object.fromEntries(Object.entries(settings.enemies.placement).reverse()), customPool: ['c3200', 'c2500', 'c1200'] } },
    items: Object.fromEntries(Object.entries(settings.items).reverse()),
  };
  assert.equal(canonicalSettingsJson(reordered), canonicalSettingsJson(settings));
  assert.equal(encodeSeedCode(envelopeOf(reordered)), encodeSeedCode(envelopeOf(settings)));
  assert.equal(serializeEnvelope(envelopeOf(settings)), serializeEnvelope(envelopeOf(structuredClone(settings))));
  // Fully resolved: a missing option is an error, never filled from today's defaults.
  const sparse = structuredClone(settings);
  delete sparse.fog.pacifistMode;
  assert.throws(() => canonicalizeSettings(sparse), /pacifistMode is missing/u);
  assert.throws(() => canonicalizeSettings({ ...settings, extra: 1 }), /unsupported option/u);
});

test('different worlds have different codes: one changed option or master seed changes the code', () => {
  const a = encodeSeedCode(envelopeOf(fullSettings()));
  assert.notEqual(a, encodeSeedCode(envelopeOf(fullSettings({ fog: { language: 'POLISH' } }))));
  assert.notEqual(a, encodeSeedCode(envelopeOf(fullSettings({ enemies: { secondGargoyle: 'keep' } }))));
  assert.notEqual(a, encodeSeedCode(envelopeOf(fullSettings(), '4118795100')));
});

test('every generation-affecting field is in the Seed Code or explicitly documented as not affecting the world', () => {
  const config = fullConfig();
  validateRandomizerConfig({ ...config, masterSeed: 'x' });
  const leaves = [];
  const walk = (value, prefix) => {
    for (const [key, child] of Object.entries(value)) {
      const pathName = prefix ? `${prefix}.${key}` : key;
      if (child !== null && typeof child === 'object' && !Array.isArray(child)) walk(child, pathName);
      else leaves.push(pathName);
    }
  };
  walk(config, '');
  assert.deepEqual([...leaves].sort(), Object.keys(GENERATION_FIELD_POLICY).sort(), 'the policy lists every leaf of the configuration and nothing else');
  const { settings } = splitConfig(config);
  const inSeed = [];
  const walkSeed = (value, prefix) => {
    for (const [key, child] of Object.entries(value)) {
      const pathName = prefix ? `${prefix}.${key}` : key;
      if (child !== null && typeof child === 'object' && !Array.isArray(child)) walkSeed(child, pathName);
      else inSeed.push(pathName);
    }
  };
  walkSeed(canonicalizeSettings(settings), '');
  const policySeed = Object.entries(GENERATION_FIELD_POLICY).filter(([, policy]) => policy === 'seed').map(([key]) => key);
  assert.deepEqual([...inSeed].sort(), [...policySeed].sort(), 'the canonical settings hold exactly the fields marked "seed"');
  for (const [key, policy] of Object.entries(GENERATION_FIELD_POLICY)) {
    if (typeof policy === 'object') assert.ok(policy.excluded.length > 10, `${key} documents why it is excluded`);
  }
  // The canonical key lists are the validator's keys.
  assert.deepEqual([...CANONICAL_ITEM_KEYS].sort(), Object.keys(config.items).sort());
  assert.deepEqual([...CANONICAL_ENEMY_KEYS].sort(), Object.keys(config.enemies).sort());
  assert.deepEqual([...CANONICAL_PLACEMENT_KEYS].sort(), Object.keys(config.enemies.placement).sort());
  assert.deepEqual([...CANONICAL_FOG_KEYS].sort(), Object.keys(config.fog).sort());
  assert.deepEqual([...CANONICAL_OUTPUT_KEYS], ['includeDlc']);
});

test('machine-specific state never enters a Seed Code; the master seed does', () => {
  const settings = broad();
  const json = serializeEnvelope(envelopeOf(settings, 'seed-1234'));
  assert.doesNotMatch(json, /gamePath|C:\\|steam|AppData|generateSpoilerLog|\.exe|runs[\\/]/iu);
  assert.match(json, /"masterSeed":"seed-1234"/u);
  const code = encodeSeedCode(envelopeOf(settings, 'seed-1234'));
  assert.equal(decodeSeedCode(code).envelope.masterSeed, 'seed-1234');
  // The spoiler preference and the game path of the sender make no difference to the code.
  const a = prepareRun(fullConfig({}, { gamePath: 'C:\\Games\\A', spoiler: false }), () => '777');
  const b = prepareRun(fullConfig({}, { gamePath: 'D:\\Other\\B', spoiler: true }), () => '777');
  assert.equal(a.seedCode, b.seedCode);
});

test('unknown or newer codes get explicit, plain errors: unsupported profile and unsupported format', () => {
  const futureProfile = encodeSeedCode({ profile: 'ds1tr-gen-v9', masterSeed: '1', settings: fullSettings() });
  const unsupported = decodeSeedCode(futureProfile);
  assert.equal(unsupported.ok, false);
  assert.equal(unsupported.code, 'UNSUPPORTED_PROFILE');
  assert.match(unsupported.message, /newer version of DS1 Triple Randomizer/u);
  assert.doesNotMatch(unsupported.message, /ds1tr-gen/u, 'the profile id is a diagnostic detail');
  assert.match(unsupported.technical, /ds1tr-gen-v9/u);
  const newerFormat = decodeSeedCode(encodeSeedCode(envelopeOf(fullSettings())).replace('DS1TR1', 'DS1TR2'));
  assert.equal(newerFormat.code, 'UNSUPPORTED_FORMAT');
  assert.match(newerFormat.message, /newer version/u);
  const invalid = decodeSeedCode(encodeSeedCode({ profile: PROFILE, masterSeed: '1', settings: fullSettings({ fog: { language: 'KLINGON' } }) }));
  assert.equal(invalid.code, 'INVALID_SETTINGS');
});

test('load seed restores exactly the encoded settings; any change afterwards ends the seed identity', () => {
  const settings = broad();
  const code = prepareRun({ ...fullConfig(broad()), masterSeed: 'shared-seed' }).seedCode;
  const loaded = decodeSeedCode(code);
  assert.equal(loaded.ok, true);
  // The app puts the decoded settings into its controls...
  const controls = joinConfig(loaded.envelope.settings, 'C:\\My\\Game', '', true);
  assert.deepEqual(splitConfig(controls).settings, canonicalizeSettings(settings));
  assert.equal(controls.gamePath, 'C:\\My\\Game', 'the machine path stays the receiver\'s own');
  assert.equal(isLoadedSeedModified(loaded.envelope.settings, splitConfig(controls).settings), false);
  // ... the spoiler preference is not part of the world ...
  assert.equal(isLoadedSeedModified(loaded.envelope.settings, splitConfig({ ...controls, output: { ...controls.output, generateSpoilerLog: false } }).settings), false);
  // ... but every generation setting is.
  for (const change of [
    (s) => { s.items.randomizeShops = false; },
    (s) => { s.enemies.placement.customPool = ['c1200']; },
    (s) => { s.fog.language = 'FRENCH'; },
    (s) => { s.enemies.placement.replacementChance = 59; },
  ]) {
    const edited = structuredClone(splitConfig(controls).settings);
    change(edited);
    assert.equal(isLoadedSeedModified(loaded.envelope.settings, edited), true);
  }
});

test('the master seed is made when a run starts, never earlier; a Seed Code decides the whole world', () => {
  let made = 0;
  const makeSeed = () => { made += 1; return `fresh-${made}`; };
  assert.equal(made, 0, 'nothing is made at startup or import');
  const first = prepareRun(fullConfig(), makeSeed);
  const second = prepareRun(fullConfig(), makeSeed);
  assert.equal(made, 2);
  assert.notEqual(first.seedCode, second.seedCode);
  assert.equal(first.config.masterSeed, 'fresh-1');
  assert.equal(first.profile.id, PROFILE);
  // A real seed is a number made at that moment.
  const real = prepareRun(fullConfig());
  assert.match(real.config.masterSeed, /^\d{1,10}$/u);
  // A Seed Code ignores the settings of the request and makes no new seed.
  made = 0;
  const reproduced = prepareRun({ ...fullConfig({ fog: { language: 'GERMAN' } }, { gamePath: 'E:\\Else' }), seedCode: first.seedCode }, makeSeed);
  assert.equal(made, 0);
  assert.equal(reproduced.config.masterSeed, 'fresh-1');
  assert.equal(reproduced.config.fog.language, 'ENGLISH');
  assert.equal(reproduced.config.gamePath, 'E:\\Else');
  assert.equal(reproduced.seedCode, first.seedCode);
  assert.equal(reproduced.fromSeedCode, true);
  // Older configurations that carry an explicit master seed still work.
  assert.equal(prepareRun({ ...fullConfig(), masterSeed: '4118795099' }, makeSeed).config.masterSeed, '4118795099');
  // A bad code or bad settings is a plain, typed refusal.
  assert.equal(prepareRun({ ...fullConfig(), seedCode: 'DS1TR1-NOPE' }).ok, false);
  assert.equal(prepareRun({ ...fullConfig(), items: undefined }).ok, false);
});
