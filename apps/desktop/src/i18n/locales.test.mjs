import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import i18next from 'i18next';

// Localization completeness (docs/I18N.md): the Polish files cover exactly the English keys, every interpolation variable survives, plural forms are
// complete for each language, and English is the fallback. The English files are the canonical source.
const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'locales');
const load = (language) => Object.fromEntries(readdirSync(path.join(root, language)).filter((name) => name.endsWith('.json')).sort()
  .map((name) => [name.replace(/\.json$/u, ''), JSON.parse(readFileSync(path.join(root, language, name), 'utf8'))]));
const en = load('en');
const pl = load('pl');

const flatten = (value, prefix = '', out = {}) => {
  for (const [key, child] of Object.entries(value)) {
    const full = prefix ? `${prefix}.${key}` : key;
    if (child !== null && typeof child === 'object') flatten(child, full, out); else out[full] = child;
  }
  return out;
};
const PLURAL = /_(zero|one|two|few|many|other)$/u;
const base = (key) => key.replace(PLURAL, '');
const variables = (text) => [...String(text).matchAll(/\{\{\s*([\w.]+)\s*\}\}/gu)].map((match) => match[1]).sort();
const groupBases = (flat) => {
  const groups = new Map();
  for (const [key, text] of Object.entries(flat)) {
    const name = base(key);
    const entry = groups.get(name) ?? { forms: new Set(), variables: new Set(), plural: false };
    if (PLURAL.test(key)) { entry.plural = true; entry.forms.add(key.match(PLURAL)[1]); }
    for (const variable of variables(text)) entry.variables.add(variable);
    groups.set(name, entry);
  }
  return groups;
};
const enFlat = flatten(en);
const plFlat = flatten(pl);

test('both languages provide the same areas (files)', () => {
  assert.deepEqual(Object.keys(pl), Object.keys(en));
});

test('every English key exists in Polish and Polish has no extra keys (plural forms compared as one key)', () => {
  const enBases = new Set(Object.keys(enFlat).map(base));
  const plBases = new Set(Object.keys(plFlat).map(base));
  assert.deepEqual([...enBases].filter((key) => !plBases.has(key)), [], 'English keys missing in Polish');
  assert.deepEqual([...plBases].filter((key) => !enBases.has(key)), [], 'Polish keys that do not exist in English');
});

test('the no-stable-release update state is a clear informational message in both languages', () => {
  assert.equal(en.app.update.noStableRelease, 'No stable updates are available. Test versions are not installed automatically.');
  assert.equal(pl.app.update.noStableRelease, 'Brak dostępnych stabilnych aktualizacji. Wersje testowe nie są instalowane automatycznie.');
});

test('plural forms are complete: English one/other, Polish one/few/many/other', () => {
  for (const [name, entry] of groupBases(enFlat)) if (entry.plural) assert.deepEqual([...entry.forms].sort(), ['one', 'other'], `English ${name}`);
  for (const [name, entry] of groupBases(plFlat)) if (entry.plural) assert.deepEqual([...entry.forms].sort(), ['few', 'many', 'one', 'other'], `Polish ${name}`);
});

test('every interpolation variable of the English text is used in Polish and vice versa', () => {
  const enGroups = groupBases(enFlat);
  const plGroups = groupBases(plFlat);
  for (const [name, entry] of enGroups) {
    assert.deepEqual([...plGroups.get(name).variables].sort(), [...entry.variables].sort(), `variables of ${name}`);
  }
});

test('no value is empty, a placeholder like [PL], or an untranslated key path', () => {
  for (const [language, flat] of [['en', enFlat], ['pl', plFlat]]) {
    for (const [key, text] of Object.entries(flat)) {
      assert.ok(typeof text === 'string' && text.trim() !== '', `${language}:${key} is empty`);
      assert.ok(!/\[(PL|EN|TODO)\]/u.test(text), `${language}:${key} is a placeholder`);
    }
  }
});

test('Polish texts really differ from English where they are sentences (a copy of the English text is not a translation)', () => {
  const allowed = new Set(['common.language.english', 'common.language.polish', 'app.header.eyebrow', 'app.header.title']);
  const copies = Object.entries(plFlat).filter(([key, text]) => enFlat[key] === text && text.split(' ').length > 3 && !allowed.has(key));
  assert.deepEqual(copies.map(([key]) => key), []);
});

const make = async (language, resources) => {
  const instance = i18next.createInstance();
  await instance.init({ resources, lng: language, fallbackLng: 'en', defaultNS: 'translation', interpolation: { escapeValue: false }, returnNull: false });
  return instance;
};
const bundle = (english, polish) => ({ en: { translation: english }, pl: { translation: polish } });

test('English is the fallback: a key missing in Polish shows the English text, never the key', async () => {
  const broken = structuredClone(pl);
  delete broken.common.copied;
  const instance = await make('pl', bundle(en, broken));
  assert.equal(instance.t('common.copied'), en.common.copied);
  assert.equal(instance.t('common.cancel'), pl.common.cancel);
  assert.notEqual(instance.t('common.cancel'), instance.t('common.copied'));
});

test('plurals follow the language: Polish has one/few/many/other, English one/other', async () => {
  const polish = await make('pl', bundle(en, pl));
  const english = await make('en', bundle(en, pl));
  assert.equal(polish.t('pool.slots', { count: 1 }), '1 slot');
  assert.equal(polish.t('pool.slots', { count: 3 }), '3 sloty');
  assert.equal(polish.t('pool.slots', { count: 5 }), '5 slotów');
  assert.equal(polish.t('pool.slots', { count: 22 }), '22 sloty');
  assert.equal(english.t('pool.slots', { count: 1 }), '1 slot');
  assert.equal(english.t('pool.slots', { count: 3 }), '3 slots');
});

test('interpolation works in both languages and does not escape paths', async () => {
  const polish = await make('pl', bundle(en, pl));
  assert.equal(polish.t('game.useCopy', { path: 'D:\\Games\\DSR' }), 'Użyj kopii w D:\\Games\\DSR');
  const english = await make('en', bundle(en, pl));
  assert.equal(english.t('game.useCopy', { path: 'D:\\Games\\DSR' }), 'Use the copy in D:\\Games\\DSR');
});

test('every backend error code has a translation in both languages, so no code ever shows its English text in Polish', () => {
  const codes = Object.keys(en.errors.codes);
  assert.deepEqual(Object.keys(pl.errors.codes).sort(), [...codes].sort());
  for (const required of ['GAME_INVALID', 'FOG_KEY_CONFLICT', 'PACKAGE_TAMPERED', 'CHECKSUM', 'ACTIVATION_INTERRUPTED', 'WORLD_DATA_OFFLINE']) assert.ok(codes.includes(required), required);
});

// A rough but useful detector for text that was left in the source instead of the resources: visible JSX text and the text attributes of the screens.
const sourceDirectory = path.join(root, '..');
const screens = readdirSync(sourceDirectory).filter((name) => /\.tsx$/u.test(name) && name !== 'main.tsx');
test('the screens contain no untranslated literal text', () => {
  const problems = [];
  for (const name of screens) {
    const source = readFileSync(path.join(sourceDirectory, name), 'utf8');
    for (const match of source.matchAll(/>([^<>{}=;()\n][^<>{}\n]*[A-Za-z]{3,}[^<>{}\n]*)</gu)) {
      const text = match[1].trim();
      // Generic type arguments and member expressions that merely look like text (`Promise<string>`, `a.size < b`).
      const looksLikeCode = /^[\w$]+\.[\w.$]+$/u.test(text) || text === 'Promise' || /&&|\|\||===|!==|=>/u.test(text);
      if (text !== '' && !/^[\s\W]*$/u.test(text) && !looksLikeCode) problems.push(`${name}: text "${text}"`);
    }
    for (const match of source.matchAll(/\b(title|placeholder|aria-label|alt|label|hint)="([^"{]*[A-Za-z]{3,}[^"]*)"/gu)) {
      problems.push(`${name}: ${match[1]}="${match[2]}"`);
    }
  }
  assert.deepEqual(problems, []);
});

test('every code the backend turns into its own plain message is translated, and the backend wording is the English text', () => {
  const source = readFileSync(path.join(root, '..', '..', '..', 'backend', 'src', 'core', 'userMessages.ts'), 'utf8');
  const exact = [...source.matchAll(/^ {2}([A-Z][A-Z0-9_]+): '(.*)',$/gmu)].map((match) => [match[1], match[2].split(String.fromCharCode(92) + "'").join("'")]);
  assert.ok(exact.length > 40);
  for (const [code, text] of exact) {
    assert.ok(en.errors.codes[code], `${code} has an English text`);
    assert.ok(pl.errors.codes[code], `${code} has a Polish text`);
    assert.equal(en.errors.codes[code], text, `${code}: the English resource is the backend message`);
  }
});

test('the DLC switch has its words in both languages and does not claim to be unimplemented', () => {
  for (const [language, resources] of [['en', en], ['pl', pl]]) {
    const texts = { ...resources.help.settings.output.dlc, label: resources.settings.output.dlc.label, reason: resources.help.reasons.dlcWithFog };
    for (const key of ['label', 'short', 'what', 'affects', 'limits', 'reason']) assert.ok(texts[key] && texts[key].length > 10, `${language}: ${key}`);
    assert.doesNotMatch(Object.values(texts).join(' '), /not implemented|nie jest zaimplementowane/iu);
  }
  assert.equal(en.settings.output.dlc.label, 'Include DLC content');
  assert.equal(pl.settings.output.dlc.label, 'Uwzględnij zawartość DLC');
  assert.match(en.help.settings.output.dlc.short, /^Includes DLC areas, enemies, bosses and items in randomization/u);
  assert.match(pl.help.settings.output.dlc.short, /^Uwzględnia obszary, wrogów, bossów i przedmioty z DLC w losowaniu/u);
  assert.ok(en.common.chips.dlc && pl.common.chips.dlc && en.hints.summary.dlc && pl.hints.summary.dlc);
});

test('no text on the screens is smaller than 11 px', () => {
  const css = readFileSync(path.join(root, '..', 'styles.css'), 'utf8');
  const small = [...css.matchAll(/font-size:\s*([\d.]+)(px|rem|em)/gu)]
    .map((match) => (match[2] === 'px' ? Number(match[1]) : Number(match[1]) * 16))
    .filter((pixels) => pixels < 11);
  assert.deepEqual(small, []);
});

test('every validator sentence a person can meet has a translation in both languages, with the variables it carries', async () => {
  const { PROBLEM_KEYS, problemKey } = await import('@ds1tr/contracts');
  for (const key of PROBLEM_KEYS) {
    assert.ok(en.errors.problems[key], `English: ${key}`);
    assert.ok(pl.errors.problems[key], `Polish: ${key}`);
    assert.deepEqual(variables(pl.errors.problems[key]), variables(en.errors.problems[key]), key);
  }
  assert.deepEqual(Object.keys(en.errors.problems).sort(), [...PROBLEM_KEYS].sort());
  const samples = [
    ['Preset is not valid JSON.', 'notJson'], ['Preset must be a JSON object.', 'notObject'], ['Preset format must be "ds1tr-preset".', 'format'],
    ['Preset version must be 15 (versions 1 to 14 are migrated).', 'version'], ['preset.items.laundromat is not supported in version 12.', 'unsupportedOption'],
    ['config.items.enabled is required.', 'missingOption'], ['config.fog.enabled must be a boolean.', 'wrongType'], ['Master seed is required.', 'masterSeedRequired'],
    ['Key placement mode is invalid.', 'invalidChoice'], ['Excluding DLC content needs the fog randomizer to be off: the fog randomizer always includes the DLC areas.', 'dlcWithFog'],
    ['Enable at least one supported randomization capability.', 'nothingEnabled'], ['Race Mode + is only available with Fair item difficulty.', 'racePlusFair'],
  ];
  for (const [sentence, key] of samples) assert.equal(problemKey(sentence)?.key, key, sentence);
  assert.equal(problemKey('Something nobody wrote a translation for.'), null);
});
