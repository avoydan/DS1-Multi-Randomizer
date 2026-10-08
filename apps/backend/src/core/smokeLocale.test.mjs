import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import i18next from 'i18next';
import { describeSmokeExpected, describeSmokeExpectedWith, describeSmokeTarget, describeSmokeTargetWith } from '@ds1tr/contracts';
import { smokeTexts } from '../../scripts/smokeLocaleSync.mjs';
import { smokeTestWorlds } from '../../dist/core/smokeTests.js';

// The test worlds in the interface (docs/I18N.md): the English resource follows the contracts, the Polish resource has a text for every one, and the target and
// expectation sentences built from templates say exactly what the contracts' English sentences say.
const resource = (language) => JSON.parse(readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'desktop', 'src', 'locales', language, 'smoke.json'), 'utf8'));
const english = resource('en');
const polish = resource('pl');
const leaves = (node, prefix = '') => Object.entries(node).flatMap(([key, value]) => (value !== null && typeof value === 'object' ? leaves(value, `${prefix}${key}.`) : [`${prefix}${key}`]));

test('the English texts of the test worlds are in step with the scenarios in the contracts', () => {
  const texts = smokeTexts();
  assert.deepEqual(english.worlds, texts.worlds);
  assert.deepEqual(english.checklist, texts.checklist);
  assert.equal(english.start, texts.start);
});

test('every English text of a test world has a Polish text, and Polish has nothing else', () => {
  assert.deepEqual(leaves(polish.worlds).sort(), leaves(english.worlds).sort());
  assert.deepEqual(leaves(polish.checklist).sort(), leaves(english.checklist).sort());
  const worlds = smokeTestWorlds().worlds;
  assert.equal(Object.keys(polish.worlds).length, worlds.length);
  for (const world of worlds) assert.ok(polish.worlds[world.id].title && polish.worlds[world.id].purpose, world.id);
});

test('a Polish text is a translation: it is not the English text, and it keeps the numbers, slots and game names of the English one', () => {
  const same = leaves(english.worlds).filter((key) => key.endsWith('.title') === false).filter((key) => {
    const get = (tree) => key.split('.').reduce((node, part) => node[part], tree);
    return get(english.worlds) === get(polish.worlds);
  });
  assert.deepEqual(same, []);
  const numbers = (text) => (text.match(/\bc\d{4}(?:_\d{4})?\b|\b\d+(?:[.,]\d+)?\b/gu) ?? []).map((value) => value.replace(',', '.')).sort();
  for (const key of leaves(english.worlds)) {
    const get = (tree) => key.split('.').reduce((node, part) => node[part], tree);
    assert.deepEqual(numbers(get(polish.worlds)), numbers(get(english.worlds)), key);
  }
});

test('the templated target and expectation sentences equal the contracts\' English ones, and read without leftovers in Polish', async () => {
  const make = async (language) => {
    const instance = i18next.createInstance();
    await instance.init({ resources: { en: { translation: { smoke: english } }, pl: { translation: { smoke: polish } } }, lng: language, fallbackLng: false, defaultNS: 'translation', interpolation: { escapeValue: false }, returnNull: false });
    return (key, params) => { assert.ok(instance.exists(key), `${language}: ${key}`); return instance.t(key, params); };
  };
  const t = { en: await make('en'), pl: await make('pl') };
  let count = 0;
  for (const world of smokeTestWorlds().worlds) {
    for (const expectation of world.expectations) {
      count += 1;
      assert.equal(describeSmokeTargetWith(expectation, t.en, 'en-GB'), describeSmokeTarget(expectation));
      assert.equal(describeSmokeExpectedWith(expectation, t.en, 'en-GB'), describeSmokeExpected(expectation));
      for (const text of [describeSmokeTargetWith(expectation, t.pl, 'pl-PL'), describeSmokeExpectedWith(expectation, t.pl, 'pl-PL')]) assert.ok(!/\{\{|undefined|NaN/u.test(text), text);
    }
  }
  assert.ok(count >= 20, `${count} expectations checked`);
  const decimal = smokeTestWorlds().worlds.flatMap((world) => world.expectations).find((expectation) => !Number.isInteger(expectation.meters));
  assert.match(describeSmokeTargetWith(decimal, t.pl, 'pl-PL'), /\d,\d m od miejsca/u, 'Polish uses the decimal comma');
});
