import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import i18next from 'i18next';
import {
  DEFAULT_GENERATION_SETTINGS, buildHintExport, buildHintHtml, buildHintJson, buildHintText, describePlace, escapeHtml, filterHints, hintFileName, hintSections, searchHints,
  shortSeed, summarizeWorld,
} from '@ds1tr/contracts';

// Hint sheet (docs/HINT_SHEET.md): the three spoiler levels, the sections, the exports and their safety. The texts come from the application's real
// translation resources, so these tests also prove that the keys the exports use exist in both languages.
const localeRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'desktop', 'src', 'locales');
const load = (language) => Object.fromEntries(['common', 'settings', 'hints'].map((name) => [name, JSON.parse(readFileSync(path.join(localeRoot, language, `${name}.json`), 'utf8'))]));
const translators = {};
for (const language of ['en', 'pl']) {
  const instance = i18next.createInstance();
  await instance.init({ resources: { en: { translation: load('en') }, pl: { translation: load('pl') } }, lng: language, fallbackLng: false, defaultNS: 'translation', interpolation: { escapeValue: false }, returnNull: false, saveMissing: false });
  translators[language] = (key, params) => {
    assert.ok(instance.exists(key), `${language}: missing translation ${key}`);
    return instance.t(key, params);
  };
}

const place = (acquisition, area, owner = area, map = 'm10_00_00_00') => ({ area, map, acquisition, owner, lot: 1234 });
const record = (itemName, importance, randomized, original, extra = {}) => ({ itemId: 1, itemName, itemKind: 'goods', importance, original, randomized, keyName: null, transposedFrom: null, placedAt: null, ...extra });
const records = [
  record('Basement Key', 'key', place('world pickup', 'Depths'), place('world pickup', 'Undead Burg / Undead Parish', undefined, 'm10_01_00_00'), { keyName: 'basement_key', placedAt: 'Humanity' }),
  record('Lordvessel', 'lord', place('boss reward', 'Boss reward: Gaping Dragon', 'Gaping Dragon', 'm10_00_00_00'), place('NPC gift', 'NPC gift: Gwynevere', 'Gwynevere', 'ItemLotParam'), { keyName: 'lordvessel' }),
  record('Rite of Kindling', 'race item', place('shop', "Shop inventory: Andre of Astora", 'Andre of Astora', 'ShopLineupParam'), place('world pickup', 'Firelink Shrine')),
  record('Annex Key', 'progression', place('enemy drop', 'Enemy drop: Black Knight', 'Black Knight', 'ItemLotParam'), place('world pickup', 'Anor Londo')),
  record('Soul of Smough', 'boss soul', place('world pickup', 'Blighttown / Quelaag\'s Domain'), place('boss reward', 'Boss reward: Smough', 'Smough')),
  record('Smough\'s Hammer', 'boss soul weapon', place('world pickup', 'The Catacombs'), place('boss reward', 'Boss reward: Smough', 'Smough'), { transposedFrom: 'Soul of Smough', itemKind: 'weapon' }),
  record('Estus Shard', 'normal', place('NPC gift', 'NPC gift: Petrus', 'Petrus', 'ItemLotParam'), place('world pickup', 'Firelink Shrine'), { placedAt: 'Humanity' }),
  record('Humanity', 'normal', place('world pickup', 'Darkroot Garden / Darkroot Basin'), place('enemy drop', 'Enemy drop: Hollow', 'Hollow')),
];
const document = (overrides = {}) => ({
  format: 'ds1tr-hint-data', schemaVersion: 1, seedCode: 'DS1TR1-06NJDCP6-ABCD-2G', generationProfile: 'ds1tr-gen-v1', createdAt: '2026-10-07T18:30:00.000Z', appVersion: '0.43.0',
  settings: structuredClone(DEFAULT_GENERATION_SETTINGS), records, unhintable: 0, ...overrides,
});

test('Low spoilers lists only the important progression items, with the broad area or the kind of place and nothing more', () => {
  const entries = filterHints(records, 'low');
  assert.deepEqual(entries.map((entry) => entry.itemName).sort(), ['Basement Key', 'Lordvessel', 'Rite of Kindling']);
  const text = JSON.stringify(entries);
  for (const leaked of ['Gaping Dragon', 'Andre of Astora', 'Humanity', 'm10_', 'basement_key', 'lot', 'source', 'Gwynevere', 'ShopLineupParam']) assert.ok(!text.includes(leaked), `Low spoilers must not mention ${leaked}`);
  const key = entries.find((entry) => entry.itemName === 'Basement Key');
  assert.deepEqual(key.destination, { acquisition: 'world-pickup', area: 'Depths' });
  assert.deepEqual(entries.find((entry) => entry.itemName === 'Lordvessel').destination, { acquisition: 'boss-reward' });
  assert.ok(entries.every((entry) => entry.source === undefined && entry.keyName === undefined));
});

test('Progression lists every progression item with the exact named place, but not the clean-game source or the map ids', () => {
  const entries = filterHints(records, 'progression');
  assert.deepEqual(entries.map((entry) => entry.itemName).sort(), ['Annex Key', 'Basement Key', 'Lordvessel', 'Rite of Kindling']);
  assert.deepEqual(entries.find((entry) => entry.itemName === 'Lordvessel').destination, { acquisition: 'boss-reward', owner: 'Gaping Dragon' });
  assert.deepEqual(entries.find((entry) => entry.itemName === 'Basement Key').destination, { acquisition: 'world-pickup', area: 'Depths', formerItem: 'Humanity' });
  const text = JSON.stringify(entries);
  assert.ok(!text.includes('m10_') && !text.includes('"source"') && !text.includes('Gwynevere'));
});

test('Full spoilers contains every hintable placement with source, destination, acquisition, category and progression marker', () => {
  const entries = filterHints(records, 'full');
  assert.equal(entries.length, records.length);
  const weapon = entries.find((entry) => entry.itemName === "Smough's Hammer");
  assert.equal(weapon.importance, 'boss-soul-weapon');
  assert.equal(weapon.itemKind, 'weapon');
  assert.equal(weapon.transposedFrom, 'Soul of Smough');
  assert.deepEqual(weapon.source, { acquisition: 'boss-reward', owner: 'Smough' });
  assert.deepEqual(weapon.destination, { acquisition: 'world-pickup', area: 'The Catacombs', map: 'm10_00_00_00' });
  assert.equal(entries.find((entry) => entry.itemName === 'Basement Key').progression, true);
  assert.equal(entries.find((entry) => entry.itemName === 'Humanity').progression, false);
  assert.ok(entries.every((entry) => entry.source), 'every entry has its source');
});

test('the levels nest: what Low shows, Progression shows, and what Progression shows, Full shows', () => {
  const names = (level) => new Set(filterHints(records, level).map((entry) => entry.itemName));
  for (const name of names('low')) assert.ok(names('progression').has(name));
  for (const name of names('progression')) assert.ok(names('full').has(name));
  assert.ok(names('full').size > names('progression').size && names('progression').size > names('low').size);
});

test('switching levels only filters: the records are never changed and the same level always gives the same result', () => {
  const before = JSON.stringify(records);
  const first = JSON.stringify(filterHints(records, 'full'));
  filterHints(records, 'low');
  filterHints(records, 'progression');
  assert.equal(JSON.stringify(records), before);
  assert.equal(JSON.stringify(filterHints(records, 'full')), first);
});

test('sections group the entries (keys, Lordvessel and Lord Souls, progression, boss souls, other) and search matches names and places', () => {
  const sections = hintSections(filterHints(records, 'full'));
  assert.deepEqual(sections.map((section) => section.id), ['keys', 'lord', 'progression', 'bossSouls', 'other']);
  const full = filterHints(records, 'full');
  assert.deepEqual(searchHints(full, 'smough').map((entry) => entry.itemName).sort(), ["Smough's Hammer", 'Soul of Smough']);
  assert.deepEqual(searchHints(full, 'catacombs').map((entry) => entry.itemName), ["Smough's Hammer"]);
  assert.equal(searchHints(full, '  ').length, full.length);
  assert.equal(searchHints(full, 'zzzz').length, 0);
});

test('the world summary names the major settings as data', () => {
  const lines = summarizeWorld(document().settings);
  assert.deepEqual(lines.slice(0, 4).map((line) => line.id), ['items', 'enemies', 'bosses', 'fog']);
  assert.ok(lines.some((line) => line.id === 'keyPlacement' && line.value === 'vanilla' && line.optionPath === 'items.keyPlacement'));
});

test('file names are safe and recognizable, whatever the Seed Code looks like', () => {
  assert.equal(shortSeed('DS1TR1-06NJDCP6-AAAA-2G'), '06NJDCP6');
  assert.equal(hintFileName('DS1TR1-06NJDCP6-AAAA-2G', 'full', 'html'), 'DS1TR-06NJDCP6-hints-full.html');
  assert.equal(hintFileName('DS1TR1-06NJDCP6-AAAA-2G', 'low', 'json'), 'DS1TR-06NJDCP6-hints-low.json');
  for (const hostile of ['../../evil', 'DS1TR1-..\\..\\x:y', 'DS1TR1-a b<>|"*?', '', '---']) assert.match(hintFileName(hostile, 'progression', 'txt'), /^DS1TR-[A-Z0-9]+-hints-progression\.txt$/u, hostile);
});

test('every translation key used by the exports exists in English and in Polish', () => {
  for (const language of ['en', 'pl']) {
    for (const level of ['low', 'progression', 'full']) {
      for (const format of ['html', 'txt']) assert.ok(buildHintExport(format, { document: document(), level, language, translate: translators[language] }).length > 200);
    }
    const emptyHtml = buildHintHtml({ document: document({ records: [] }), level: 'full', language, translate: translators[language] });
    assert.ok(emptyHtml.includes(translators[language]('hints.empty')));
  }
});

test('the HTML export is self-contained: no script, no remote resource, no link, a Content-Security-Policy that allows nothing but inline style', () => {
  const html = buildHintHtml({ document: document(), level: 'full', language: 'en', translate: translators.en });
  assert.ok(html.startsWith('<!doctype html>'));
  assert.ok(!/<script/iu.test(html) && !/\son[a-z]+\s*=/iu.test(html), 'no script');
  assert.ok(!/(https?:)?\/\/[a-z0-9.-]+\.[a-z]{2,}/iu.test(html.replace(/<meta http-equiv="Content-Security-Policy"[^>]*>/u, '')), 'no remote address');
  assert.ok(!/<(link|img|iframe|object|embed|form|a)\b/iu.test(html), 'no resource or link element');
  assert.ok(html.includes("default-src 'none'") && html.includes('<meta name="viewport"'));
  assert.ok(html.includes('@media print'), 'printable');
  assert.ok(html.includes('DS1TR1-06NJDCP6-ABCD-2G') && html.includes('Full spoilers') && html.includes('lang="en"'));
});

test('every name and every piece of metadata is HTML-escaped', () => {
  const hostile = '<script>alert(1)</script> & "q" \'s\' `t` <img src=x onerror=alert(1)>';
  const evil = [record(hostile, 'key', place('world pickup', hostile), place('boss reward', `Boss reward: ${hostile}`, hostile), { placedAt: hostile }), record('Normal', 'normal', place('shop', hostile, hostile), place('world pickup', hostile))];
  const html = buildHintHtml({ document: document({ records: evil, seedCode: `DS1TR1-${hostile}`, generationProfile: hostile, appVersion: hostile }), level: 'full', language: 'en', translate: translators.en });
  assert.ok(!html.includes('<script>alert') && !html.includes('<img src=x'), 'raw markup never reaches the page');
  assert.ok(html.includes('&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;q&quot; &#39;s&#39;'), 'it is escaped, not dropped');
  assert.equal(escapeHtml('<a href="x">&\'</a>'), '&lt;a href=&quot;x&quot;&gt;&amp;&#39;&lt;/a&gt;');
  const text = buildHintText({ document: document({ records: evil }), level: 'full', language: 'en', translate: translators.en });
  assert.ok(text.includes(hostile), 'plain text keeps the characters as they are');
});

test('the text export is human-readable, not a JSON dump', () => {
  const text = buildHintText({ document: document(), level: 'progression', language: 'en', translate: translators.en });
  assert.ok(!text.trimStart().startsWith('{') && !text.includes('"importance"'));
  assert.match(text, /Seed Code: DS1TR1-06NJDCP6-ABCD-2G/u);
  assert.match(text, /Spoiler level: Progression/u);
  assert.match(text, /\* Basement Key: Depths, at the pickup where the Humanity lies in the clean game/u);
  assert.match(text, /\* Lordvessel: as the reward of Gaping Dragon/u);
  const polish = buildHintText({ document: document(), level: 'low', language: 'pl', translate: translators.pl });
  assert.match(polish, /Kod seeda: DS1TR1-06NJDCP6-ABCD-2G/u);
  assert.match(polish, /Poziom spoilerów: Mało spoilerów/u);
  assert.match(polish, /\* Lordvessel: jako nagroda za bossa/u);
});

test('the JSON export has a schema version, the Seed Code, the generation profile, the level, the language, the settings and the placements, with stable ids in both languages', () => {
  const english = JSON.parse(buildHintJson({ document: document(), level: 'full', language: 'en', translate: translators.en }));
  const polish = JSON.parse(buildHintJson({ document: document(), level: 'full', language: 'pl', translate: translators.pl }));
  assert.equal(english.schemaVersion, 1);
  assert.equal(english.format, 'ds1tr-hint-sheet');
  assert.equal(english.seedCode, 'DS1TR1-06NJDCP6-ABCD-2G');
  assert.equal(english.generationProfile, 'ds1tr-gen-v1');
  assert.equal(english.spoilerLevel, 'full');
  assert.equal(english.language, 'en');
  assert.equal(polish.language, 'pl');
  assert.deepEqual(english.settings, document().settings);
  assert.equal(english.placements.length, records.length);
  assert.deepEqual(english.placements, polish.placements, 'the placements never depend on the language');
  assert.ok(english.placements.every((entry) => /^[a-z-]+$/u.test(entry.importance) && /^[a-z-]+$/u.test(entry.destination.acquisition)));
  const low = JSON.parse(buildHintJson({ document: document(), level: 'low', language: 'en', translate: translators.en }));
  assert.equal(low.placements.length, 3);
  assert.ok(!JSON.stringify(low).includes('Gaping Dragon'));
});

test('a place reads naturally at both precisions', () => {
  assert.equal(describePlace({ acquisition: 'shop', owner: 'Andre of Astora' }, translators.en, true), 'sold by Andre of Astora');
  assert.equal(describePlace({ acquisition: 'shop' }, translators.en, false), 'sold in a shop');
  assert.equal(describePlace({ acquisition: 'world-pickup', area: 'Depths' }, translators.pl, false), 'w obszarze Depths');
});
