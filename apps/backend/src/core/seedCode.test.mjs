import assert from 'node:assert/strict';
import test from 'node:test';
import {
  CANONICAL_ENEMY_KEYS, CANONICAL_FOG_KEYS, CANONICAL_ITEM_EXTENSION_KEYS, CANONICAL_ITEM_KEYS, CANONICAL_OUTPUT_KEYS, CANONICAL_PLACEMENT_EXTENSION_KEYS, CANONICAL_PLACEMENT_KEYS, GENERATION_FIELD_POLICY,
  isLoadedSeedModified, joinConfig, splitConfig,
} from '@ds1tr/contracts';
import { canonicalSettingsJson, canonicalizeSettings, sparseCanonicalSettings } from '../../dist/core/canonicalSettings.js';
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
  assert.deepEqual([...CANONICAL_ITEM_KEYS, ...CANONICAL_ITEM_EXTENSION_KEYS.map((entry) => entry.key)].sort(), Object.keys(config.items).sort());
  assert.deepEqual([...CANONICAL_ENEMY_KEYS].sort(), Object.keys(config.enemies).sort());
  assert.deepEqual([...CANONICAL_PLACEMENT_KEYS, ...CANONICAL_PLACEMENT_EXTENSION_KEYS.map((entry) => entry.key)].sort(), Object.keys(config.enemies.placement).sort());
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
  assert.match(unsupported.message, /newer version of DS1 Multi Randomizer/u);
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
  assert.equal(first.profile.id, 'ds1tr-gen-v3');
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

test('placement options added after format 1 are stored only when they differ from their neutral value, so old codes keep their text', () => {
  const complete = fullSettings();
  const withoutExtensions = structuredClone(complete);
  delete withoutExtensions.enemies.placement.bossReplacement;
  delete withoutExtensions.enemies.placement.uniqueBosses;
  for (const key of ['bossChanceInNormalSlots', 'roamingBossSouls', 'roamingBossesStayDead']) delete withoutExtensions.enemies.placement[key];
  // A settings object that predates the options is completed with their neutral value ...
  assert.equal(canonicalizeSettings(withoutExtensions).enemies.placement.bossReplacement, 'classic');
  assert.equal(canonicalizeSettings(withoutExtensions).enemies.placement.uniqueBosses, false);
  // ... and has exactly the same Seed Code as one that states the neutral values.
  assert.equal(encodeSeedCode(envelopeOf(withoutExtensions)), encodeSeedCode(envelopeOf(complete)));
  assert.doesNotMatch(canonicalSettingsJson(complete), /bossReplacement|uniqueBosses|bossChanceInNormalSlots|roamingBossSouls|roamingBossesStayDead/u, 'the stored form omits neutral extension keys');
  // A non-neutral value is stored, changes the code and round-trips; the order of the keys is fixed (after customPool).
  const sampled = fullSettings({ enemies: { placement: { algorithm: 'sampling-v1', fit: 'fits', bossReplacement: 'bosses', uniqueBosses: true } } });
  assert.match(canonicalSettingsJson(sampled), /"customPool":null,"bossReplacement":"bosses","uniqueBosses":true\}/u);
  assert.notEqual(encodeSeedCode(envelopeOf(sampled)), encodeSeedCode(envelopeOf(complete)));
  const decoded = decodeSeedCode(encodeSeedCode(envelopeOf(sampled)));
  assert.equal(decoded.ok, true);
  assert.equal(decoded.envelope.settings.enemies.placement.bossReplacement, 'bosses');
  assert.equal(decoded.envelope.settings.enemies.placement.uniqueBosses, true);
  assert.equal(decoded.seedCode, encodeSeedCode(envelopeOf(sampled)));
  // The decoded settings of an old code are complete again.
  const old = decodeSeedCode(encodeSeedCode(envelopeOf(withoutExtensions)));
  assert.equal(old.envelope.settings.enemies.placement.bossReplacement, 'classic');
  assert.deepEqual(sparseCanonicalSettings(old.envelope.settings), sparseCanonicalSettings(complete));
  // Unique bosses without boss sampling is refused when a code is loaded.
  const invalid = decodeSeedCode(encodeSeedCode(envelopeOf(fullSettings({ enemies: { placement: { uniqueBosses: true } } }))));
  assert.equal(invalid.ok, false);
  assert.equal(invalid.code, 'INVALID_SETTINGS');
});

test('Part E1 options (bosses in regular slots) extend the format without changing any earlier code', () => {
  const part = { algorithm: 'sampling-v1', fit: 'fits', bossReplacement: 'bosses', uniqueBosses: true };
  const before = fullSettings({ enemies: { placement: part } });
  // The Part D world is stored exactly as before: the three new keys are neutral and therefore absent.
  assert.match(canonicalSettingsJson(before), /"customPool":null,"bossReplacement":"bosses","uniqueBosses":true\}/u);
  const roaming = fullSettings({ enemies: { placement: { ...part, normalReplacement: 'both', bossChanceInNormalSlots: 25, roamingBossSouls: 80, roamingBossesStayDead: true } } });
  assert.match(canonicalSettingsJson(roaming), /"bossReplacement":"bosses","uniqueBosses":true,"bossChanceInNormalSlots":25,"roamingBossSouls":80,"roamingBossesStayDead":true\}/u);
  assert.notEqual(encodeSeedCode(envelopeOf(roaming)), encodeSeedCode(envelopeOf(before)));
  const decoded = decodeSeedCode(encodeSeedCode(envelopeOf(roaming)));
  assert.equal(decoded.ok, true);
  assert.deepEqual(decoded.envelope.settings.enemies.placement, canonicalizeSettings(roaming).enemies.placement);
  assert.equal(decoded.seedCode, encodeSeedCode(envelopeOf(roaming)));
  // Only the options that apply are accepted: a setting of a mode that is not selected is refused, never ignored.
  for (const placement of [
    { normalReplacement: 'normals', bossChanceInNormalSlots: 30 },
    { normalReplacement: 'bosses', bossChanceInNormalSlots: 30 },
    { normalReplacement: 'normals', roamingBossSouls: 10 },
    { normalReplacement: 'none', roamingBossesStayDead: true },
  ]) {
    const invalid = decodeSeedCode(encodeSeedCode(envelopeOf(fullSettings({ enemies: { placement: { algorithm: 'sampling-v1', ...placement } } }))));
    assert.equal(invalid.ok, false, JSON.stringify(placement));
    assert.equal(invalid.code, 'INVALID_SETTINGS');
  }
});

test('Part E2 (a normal enemy in a boss slot) extends the format without changing any earlier code', () => {
  const part = { algorithm: 'sampling-v1', fit: 'fits', bossReplacement: 'bosses', uniqueBosses: true };
  const before = fullSettings({ enemies: { placement: part } });
  // A Part D world and a Part E1 world are stored exactly as before: the new key is neutral and therefore absent.
  assert.doesNotMatch(canonicalSettingsJson(before), /bossChanceInBossSlots/u);
  const e1 = fullSettings({ enemies: { placement: { ...part, normalReplacement: 'both', bossChanceInNormalSlots: 25, roamingBossSouls: 80, roamingBossesStayDead: true } } });
  assert.match(canonicalSettingsJson(e1), /"roamingBossesStayDead":true\}/u, 'the E1 text is unchanged: it still ends with its last key');
  const both = fullSettings({ enemies: { placement: { algorithm: 'sampling-v1', bossReplacement: 'both', bossChanceInBossSlots: 60, uniqueBosses: true } } });
  assert.match(canonicalSettingsJson(both), /"bossReplacement":"both","uniqueBosses":true,"bossChanceInBossSlots":60\}/u);
  const decoded = decodeSeedCode(encodeSeedCode(envelopeOf(both)));
  assert.equal(decoded.ok, true);
  assert.equal(decoded.envelope.settings.enemies.placement.bossChanceInBossSlots, 60);
  assert.equal(decoded.seedCode, encodeSeedCode(envelopeOf(both)));
  const normals = decodeSeedCode(encodeSeedCode(envelopeOf(fullSettings({ enemies: { placement: { algorithm: 'sampling-v1', bossReplacement: 'normals' } } }))));
  assert.equal(normals.ok, true);
  assert.equal(normals.envelope.settings.enemies.placement.bossChanceInBossSlots, 90, 'the neutral value is filled in');
  for (const placement of [
    { bossReplacement: 'normals', bossChanceInBossSlots: 60 },
    { bossReplacement: 'bosses', bossChanceInBossSlots: 60 },
    { bossReplacement: 'normals', uniqueBosses: true },
    { bossReplacement: 'both', bossChanceInBossSlots: 101 },
  ]) {
    const invalid = decodeSeedCode(encodeSeedCode(envelopeOf(fullSettings({ enemies: { placement: { algorithm: 'sampling-v1', ...placement } } }))));
    assert.equal(invalid.ok, false, JSON.stringify(placement));
    assert.equal(invalid.code, 'INVALID_SETTINGS');
  }
});

test('Part F1 (hostile NPCs) extends the format without changing any earlier code', () => {
  const part = { algorithm: 'sampling-v1', fit: 'fits', normalReplacement: 'both', bossChanceInNormalSlots: 25, bossReplacement: 'both', bossChanceInBossSlots: 60 };
  const before = fullSettings({ enemies: { placement: part } });
  // Every earlier world is stored exactly as before: the new key is neutral (off) and therefore absent, and it comes after every earlier key.
  assert.doesNotMatch(canonicalSettingsJson(before), /hostileNpcs/u);
  const on = fullSettings({ enemies: { placement: { ...part, hostileNpcs: true } } });
  assert.equal(canonicalSettingsJson(on).replace(',"hostileNpcs":true', ''), canonicalSettingsJson(before), 'the only difference is the appended key');
  assert.match(canonicalSettingsJson(on), /"bossChanceInBossSlots":60,"hostileNpcs":true\}/u);
  const decoded = decodeSeedCode(encodeSeedCode(envelopeOf(on)));
  assert.equal(decoded.ok, true);
  assert.equal(decoded.envelope.settings.enemies.placement.hostileNpcs, true);
  assert.equal(decoded.seedCode, encodeSeedCode(envelopeOf(on)));
  const plain = decodeSeedCode(encodeSeedCode(envelopeOf(before)));
  assert.equal(plain.envelope.settings.enemies.placement.hostileNpcs, false, 'the neutral value is filled in');
  for (const placement of [
    { normalReplacement: 'none', hostileNpcs: true },
    { normalReplacement: 'bosses', hostileNpcs: true },
    { hostileNpcs: true, customPool: ['c2500'] },
  ]) {
    const invalid = decodeSeedCode(encodeSeedCode(envelopeOf(fullSettings({ enemies: { placement: { algorithm: 'sampling-v1', ...placement } } }))));
    assert.equal(invalid.ok, false, JSON.stringify(placement));
    assert.equal(invalid.code, 'INVALID_SETTINGS');
  }
});

test('key placement keys are extension keys: neutral values never enter a Seed Code, so every earlier code keeps its exact text', () => {
  const plain = broad();
  const withoutOptions = JSON.parse(JSON.stringify(plain));
  for (const { key } of CANONICAL_ITEM_EXTENSION_KEYS) delete withoutOptions.items[key];
  assert.equal(encodeSeedCode(envelopeOf(plain)), encodeSeedCode(envelopeOf(withoutOptions)), 'stating the neutral values or omitting them is the same code');
  assert.doesNotMatch(serializeEnvelope(envelopeOf(plain)), /keyPlacement|randomizeLordvessel|randomizeLordSouls/u);
  const keyed = JSON.parse(JSON.stringify(plain));
  keyed.items.keyPlacement = 'shuffled';
  keyed.items.randomizeLordvessel = true;
  const code = encodeSeedCode(envelopeOf(keyed));
  assert.notEqual(code, encodeSeedCode(envelopeOf(plain)));
  assert.match(serializeEnvelope(envelopeOf(keyed)), /"keyPlacement":"shuffled"/u);
  assert.doesNotMatch(serializeEnvelope(envelopeOf(keyed)), /randomizeLordSouls/u, 'a neutral companion option stays out');
  assert.deepEqual(decodeSeedCode(code).envelope.settings, canonicalizeSettings(keyed));
});

test('the DLC switch is part of the world: ON keeps every earlier code, OFF is a different code that round-trips, a code without it means ON', () => {
  const on = broad();
  on.fog.enabled = false;
  const off = JSON.parse(JSON.stringify(on));
  off.output.includeDlc = false;
  assert.match(serializeEnvelope(envelopeOf(on)), /"output":\{"includeDlc":true\}/u, 'ON is stated exactly as every earlier code states it');
  const onCode = encodeSeedCode(envelopeOf(on));
  const offCode = encodeSeedCode(envelopeOf(off));
  assert.notEqual(onCode, offCode);
  const decoded = decodeSeedCode(offCode);
  assert.equal(decoded.ok, true);
  assert.equal(decoded.envelope.settings.output.includeDlc, false);
  assert.equal(decoded.seedCode, offCode);
  assert.equal(decodeSeedCode(onCode).envelope.settings.output.includeDlc, true);
  // A settings document that predates the switch is complete again with the DLC included, and keeps the code of the DLC-included world.
  const legacy = JSON.parse(JSON.stringify(on));
  legacy.output = {};
  assert.equal(encodeSeedCode(envelopeOf(legacy)), onCode);
  assert.equal(canonicalizeSettings(legacy).output.includeDlc, true);
  // The DLC cannot be left out while the world randomizer runs.
  const withFog = JSON.parse(JSON.stringify(off));
  withFog.fog.enabled = true;
  const refused = decodeSeedCode(encodeSeedCode(envelopeOf(withFog)));
  assert.equal(refused.ok, false);
  assert.equal(refused.code, 'INVALID_SETTINGS');
  assert.match(refused.technical, /fog randomizer to be off/u);
  // Turning it off after loading a seed ends the seed's identity.
  assert.equal(isLoadedSeedModified(decoded.envelope.settings, canonicalizeSettings(off)), false);
  assert.equal(isLoadedSeedModified(decoded.envelope.settings, canonicalizeSettings(on)), true);
});

test('whether a loaded seed was changed is decided by the settings, never by the order the interface builds them in', async () => {
  const { DEFAULT_GENERATION_SETTINGS } = await import('@ds1tr/contracts');
  // The interface builds the product defaults in its own key order, a Seed Code and a generated world hold the canonical order: the same settings.
  const asBuilt = structuredClone(DEFAULT_GENERATION_SETTINGS);
  const canonical = canonicalizeSettings(asBuilt);
  assert.notEqual(JSON.stringify(asBuilt), JSON.stringify(canonical), 'the two orders really differ (this is what showed a changed-settings notice after every untouched world)');
  assert.equal(isLoadedSeedModified(canonical, asBuilt), false);
  assert.equal(isLoadedSeedModified(canonicalizeSettings(decodeSeedCode(encodeSeedCode(envelopeOf(asBuilt))).envelope.settings), asBuilt), false);
  // A custom pool is a set.
  const pooled = structuredClone(asBuilt);
  pooled.enemies.placement.customPool = ['c3200', 'c1200'];
  const reordered = structuredClone(pooled);
  reordered.enemies.placement.customPool = ['c1200', 'c3200'];
  assert.equal(isLoadedSeedModified(pooled, reordered), false);
  // A real change is still a change.
  const changed = structuredClone(asBuilt);
  changed.items.laundromat = true;
  assert.equal(isLoadedSeedModified(canonical, changed), true);
  const dlcOff = structuredClone(asBuilt);
  dlcOff.output.includeDlc = false;
  assert.equal(isLoadedSeedModified(canonical, dlcOff), true);
  const pool2 = structuredClone(pooled);
  pool2.enemies.placement.customPool = ['c1200'];
  assert.equal(isLoadedSeedModified(pooled, pool2), true);
});
