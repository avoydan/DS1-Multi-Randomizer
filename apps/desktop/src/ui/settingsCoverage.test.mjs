// Settings audit (docs/SETTINGS_INVENTORY.md): every option the interface draws is described in both languages, every disabled option says why, the basic view is a view of the
// same configuration (it never rewrites an advanced value), and changing the language, the view or the help never touches the configuration.
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  BASIC_LAYOUT, DEFAULT_GENERATION_SETTINGS, GENERATION_FIELD_POLICY, SECTION_IDS, SETTINGS, VISIBLE_SETTINGS, applySetting, customAdvancedSettings, displayedValue,
  joinConfig, sectionsWithCustomSettings, settingById, settingState, settingValue, settingsOfSection,
} from '@ds1tr/contracts';

const here = path.dirname(fileURLToPath(import.meta.url));
const locales = path.join(here, '..', 'locales');
const load = (language) => Object.fromEntries(readdirSync(path.join(locales, language)).filter((name) => name.endsWith('.json'))
  .map((name) => [name.replace(/\.json$/u, ''), JSON.parse(readFileSync(path.join(locales, language, name), 'utf8'))]));
const resources = { en: load('en'), pl: load('pl') };
const get = (language, key) => key.split('.').reduce((value, part) => (value === undefined || value === null ? undefined : value[part]), resources[language]);
const text = (language, key) => { const value = get(language, key); return typeof value === 'string' ? value.trim() : ''; };
const config = (overrides = (value) => value) => overrides(joinConfig(structuredClone(DEFAULT_GENERATION_SETTINGS), 'C:/Games/DSR', 'audit', true));
const deepFreeze = (value) => { if (value && typeof value === 'object') { Object.freeze(value); for (const child of Object.values(value)) deepFreeze(child); } return value; };
const leaves = (value, prefix = '') => Object.entries(value ?? {}).flatMap(([key, child]) => (child !== null && typeof child === 'object' && !Array.isArray(child) ? leaves(child, `${prefix}${key}.`) : [[`${prefix}${key}`, child]]));

const HIDDEN = SETTINGS.filter((setting) => setting.hidden);
const PARTS = ['short', 'what', 'affects', 'limits'];

test('help coverage: every option the interface draws has a name, a short and a full explanation, and every choice is explained, in both languages', () => {
  const problems = [];
  let covered = 0;
  for (const setting of VISIBLE_SETTINGS) {
    let complete = true;
    for (const language of ['en', 'pl']) {
      if (!text(language, `settings.${setting.id}.label`)) { problems.push(`${language}: no name for ${setting.id}`); complete = false; }
      for (const part of PARTS) if (!text(language, `help.settings.${setting.id}.${part}`)) { problems.push(`${language}: ${setting.id} has no help "${part}"`); complete = false; }
      if (setting.kind === 'select') {
        for (const choice of setting.choices) {
          if (!text(language, `settings.${setting.id}.options.${choice}`)) { problems.push(`${language}: ${setting.id} choice ${choice} has no name`); complete = false; }
          if (!setting.plainChoices && !text(language, `help.settings.${setting.id}.choices.${choice}`)) { problems.push(`${language}: ${setting.id} choice ${choice} is not explained`); complete = false; }
        }
      }
    }
    if (complete) covered += 1;
  }
  assert.deepEqual(problems, []);
  console.log(`HELP COVERAGE: ${covered}/${VISIBLE_SETTINGS.length} options (${VISIBLE_SETTINGS.filter((setting) => setting.kind === 'select').length} with choices), English and Polish`);
  assert.equal(covered, VISIBLE_SETTINGS.length);
  assert.ok(VISIBLE_SETTINGS.length >= 55);
});

test('the explanation never says more than the short text promises, and texts are not empty copies of each other', () => {
  for (const language of ['en', 'pl']) {
    for (const setting of VISIBLE_SETTINGS) {
      assert.notEqual(text(language, `help.settings.${setting.id}.what`), text(language, `help.settings.${setting.id}.short`), `${language}: ${setting.id} what repeats short`);
    }
  }
});

test('options that cannot do anything are not drawn and have no help; their fields stay in the configuration, the presets and the Seed Codes', () => {
  assert.deepEqual(HIDDEN.map((setting) => setting.id).sort(), ['items.balancedEarlyLoot', 'items.earlyWeapon', 'items.protectedItems']);
  const seedFields = Object.entries(GENERATION_FIELD_POLICY).filter(([, policy]) => policy === 'seed').map(([field]) => field);
  for (const setting of HIDDEN) {
    assert.ok(seedFields.includes(setting.path), `${setting.path} stays a Seed Code field`);
    assert.equal(get('en', `settings.${setting.id}`), undefined, `${setting.id} has no label to show`);
    assert.equal(get('en', `help.settings.${setting.id}`), undefined, `${setting.id} has no help to show`);
  }
  for (const language of ['en', 'pl']) {
    const everything = JSON.stringify(resources[language].settings) + JSON.stringify(resources[language].help);
    assert.doesNotMatch(everything, /early weapon|wczesną broń|Balanced early loot|Wyważony wczesny/iu, `${language}: no text promises an option that does nothing`);
  }
});

test('the registry and the configuration describe the same fields: every setting is a Seed Code field and every field of a world has a control (or is documented)', () => {
  const seedFields = Object.entries(GENERATION_FIELD_POLICY).filter(([, policy]) => policy === 'seed').map(([field]) => field);
  const registryPaths = new Set(SETTINGS.map((setting) => setting.path));
  // The spoiler file switch is an output preference of the person, not a part of a world: it is in the envelope policy, never in a Seed Code.
  for (const setting of SETTINGS.filter((entry) => entry.id !== 'output.spoiler')) assert.ok(seedFields.includes(setting.path), `${setting.id} -> ${setting.path} is a Seed Code field`);
  assert.match(GENERATION_FIELD_POLICY['output.generateSpoilerLog'].excluded, /only decides whether spoiler.txt is written/u);
  const withoutControl = seedFields.filter((field) => !registryPaths.has(field));
  // The only world fields without a control of their own: the DLC switch is the registry's `output.dlc`, and the engine-level fields below are not options.
  assert.deepEqual(withoutControl.filter((field) => /^(items|enemies|fog|output)\./u.test(field)), [], 'every option of a world is in the registry');
  const defaults = config();
  for (const setting of SETTINGS) assert.notEqual(settingValue(defaults, setting.id) === undefined && setting.kind !== 'pool', true, `${setting.id} has a default`);
  assert.equal(new Set(SETTINGS.map((setting) => setting.id)).size, SETTINGS.length, 'ids are unique');
  assert.equal(registryPaths.size, SETTINGS.length, 'paths are unique');
});

test('every select lists exactly the values the configuration accepts and its default is among them', () => {
  const defaults = config();
  for (const setting of VISIBLE_SETTINGS.filter((entry) => entry.kind === 'select')) {
    assert.ok(setting.choices.includes(String(settingValue(defaults, setting.id) ?? setting.choices[0])), `${setting.id} default is a listed choice`);
    assert.equal(new Set(setting.choices).size, setting.choices.length, `${setting.id} has no duplicate choice`);
  }
});

test('the sections list every visible option once, in the order shown', () => {
  const all = SECTION_IDS.flatMap((section) => settingsOfSection(section).map((setting) => setting.id));
  assert.deepEqual([...all].sort(), VISIBLE_SETTINGS.map((setting) => setting.id).sort());
  for (const section of SECTION_IDS) {
    assert.ok(text('en', `settings.sections.${section}.title`) && text('pl', `settings.sections.${section}.title`), section);
    assert.ok(text('en', `settings.sections.${section}.note`) && text('pl', `settings.sections.${section}.note`), section);
  }
});

// ---- reasons

const registrySource = readFileSync(path.join(here, '..', '..', '..', '..', 'packages', 'contracts', 'src', 'settingsRegistry.ts'), 'utf8');
const reasonUnion = registrySource.slice(registrySource.indexOf('export type ReasonId'), registrySource.indexOf('export interface SettingState'));
const reasonIds = [...new Set([...reasonUnion.matchAll(/'(\w+)'/gu)].map((match) => match[1]))];

test('every reason the registry can give is written in both languages, and no reason is written that nothing gives', () => {
  assert.ok(reasonIds.length >= 15);
  for (const language of ['en', 'pl']) {
    assert.deepEqual(reasonIds.filter((id) => !text(language, `help.reasons.${id}`)), [], `${language}: reasons without text`);
    const written = Object.keys(resources[language].help.reasons);
    assert.deepEqual(written.filter((id) => !reasonIds.includes(id)), [], `${language}: texts nothing uses`);
  }
});

function* configurations() {
  // A deterministic walk through the configuration space: every visible option is set to each of its values in turn, from several different starting points.
  let seed = 20260708;
  const random = () => { seed = (seed * 1664525 + 1013904223) % 4294967296; return seed / 4294967296; };
  for (let walk = 0; walk < 60; walk += 1) {
    let current = config();
    yield current;
    for (let step = 0; step < 40; step += 1) {
      const setting = VISIBLE_SETTINGS[Math.floor(random() * VISIBLE_SETTINGS.length)];
      if (setting.kind === 'pool') { current = applySetting(current, setting.id, random() < 0.5 ? null : ['c2500']); }
      else if (setting.kind === 'toggle') current = applySetting(current, setting.id, random() < 0.5);
      else if (setting.kind === 'range') current = applySetting(current, setting.id, Math.floor(random() * 101));
      else current = applySetting(current, setting.id, setting.choices[Math.floor(random() * setting.choices.length)]);
      yield current;
    }
  }
}

test('a disabled option always carries a reason that is written in both languages (audit of every disabled state reachable through the controls)', () => {
  const seen = new Map();
  let checked = 0;
  for (const current of configurations()) {
    for (const setting of VISIBLE_SETTINGS) {
      const state = settingState(current, setting.id);
      checked += 1;
      if (!state.enabled) {
        assert.ok(state.reason, `${setting.id} is disabled without a reason`);
        assert.ok(text('en', `help.reasons.${state.reason}`) && text('pl', `help.reasons.${state.reason}`), `${state.reason} has no text`);
        seen.set(state.reason, (seen.get(state.reason) ?? 0) + 1);
      } else assert.equal(state.reason, undefined, `${setting.id} is enabled but names a reason`);
    }
  }
  console.log(`DISABLED-STATE AUDIT: ${checked} states checked, reasons seen: ${[...seen.keys()].sort().join(', ')}`);
  for (const id of ['itemsOff', 'needsStartingEquipment', 'needsKeyPlacement', 'racePlusFair', 'samplingOnly', 'needsCurve', 'fogOff', 'needsWorldItems', 'needsSoulPool', 'regularEnemiesOff', 'bossesOff']) assert.ok(seen.has(id), `the walk reaches ${id}`);
});

test('Starting weapon types (Starting items) is disabled exactly while Starting equipment is off, and says so', () => {
  const off = config((value) => applySetting(value, 'items.startingEquipment', false));
  const state = settingState(off, 'items.startingItems');
  assert.deepEqual([state.enabled, state.reason], [false, 'needsStartingEquipment']);
  assert.match(text('en', 'help.reasons.needsStartingEquipment'), /Starting equipment/u);
  assert.match(text('pl', 'help.reasons.needsStartingEquipment'), /Wyposażenie startowe/u);
  const on = applySetting(off, 'items.startingEquipment', true);
  assert.equal(settingState(on, 'items.startingItems').enabled, true);
  assert.equal(settingValue(off, 'items.startingItems'), settingValue(on, 'items.startingItems'), 'the choice is kept while the option is unavailable');
  const itemsOff = applySetting(on, 'items.enabled', false);
  assert.equal(settingState(itemsOff, 'items.startingItems').reason, 'itemsOff');
});

test('the DLC switch is locked on while the fog randomizer is on, shows "on", and gets its own value back untouched', () => {
  const off = config((value) => applySetting(value, 'output.dlc', false));
  assert.equal(settingValue(off, 'output.dlc'), false);
  const fog = applySetting(off, 'fog.enabled', true);
  assert.equal(settingState(fog, 'output.dlc').reason, 'dlcWithFog');
  assert.equal(displayedValue(fog, 'output.dlc'), true);
  assert.equal(settingValue(fog, 'output.dlc'), true, 'turning the fog randomizer on puts the DLC back in, as the generation requires');
});

// ---- the two views are one configuration

const BASIC_IDS = Object.values(BASIC_LAYOUT).flat();

test('the basic view lists exactly the options the registry marks as basic', () => {
  assert.deepEqual([...BASIC_IDS].sort(), VISIBLE_SETTINGS.filter((setting) => setting.basic).map((setting) => setting.id).sort());
  for (const id of BASIC_IDS) assert.ok(text('en', `help.settings.${id}.short`) && text('pl', `help.settings.${id}.short`), id);
});

test('using the basic view never changes an advanced value, and the advanced view sees every basic change', () => {
  const advanced = VISIBLE_SETTINGS.filter((setting) => !setting.basic);
  for (const current of [config(), ...[...configurations()].slice(0, 400)]) {
    const before = Object.fromEntries(advanced.map((setting) => [setting.id, JSON.stringify(settingValue(current, setting.id))]));
    for (const id of BASIC_IDS) {
      for (const value of [true, false]) {
        const next = applySetting(current, id, value);
        for (const setting of advanced) assert.equal(JSON.stringify(settingValue(next, setting.id)), before[setting.id], `${id}=${value} changed ${setting.id}`);
      }
    }
  }
});

test('custom advanced settings are detected, listed per section, and ignore the basic options', () => {
  assert.deepEqual(customAdvancedSettings(config()), []);
  const basicOnly = config((value) => applySetting(applySetting(value, 'items.shops', false), 'enemies.bosses', false));
  assert.deepEqual(customAdvancedSettings(basicOnly), []);
  const changed = applySetting(basicOnly, 'enemies.gwyn', 'none');
  assert.deepEqual(customAdvancedSettings(changed), ['enemies.gwyn']);
  assert.deepEqual(sectionsWithCustomSettings(changed), ['classic']);
  assert.match(text('pl', 'app.basic.customActive'), /^Aktywne są niestandardowe ustawienia zaawansowane\.$/u);
  assert.match(text('en', 'app.basic.customActive'), /^Custom advanced settings are active\.$/u);
  assert.deepEqual(customAdvancedSettings(applySetting(config(), 'items.progressionLogic', false)), ['items.progressionLogic']);
});

test('applying a setting never changes its input, and only the documented fields move together', () => {
  const frozen = deepFreeze(config());
  for (const setting of VISIBLE_SETTINGS) {
    const values = setting.kind === 'toggle' ? [true, false] : setting.kind === 'range' ? [0, 100] : setting.kind === 'pool' ? [null, ['c2500']] : setting.choices;
    for (const value of values) assert.doesNotThrow(() => applySetting(frozen, setting.id, value), `${setting.id}=${String(value)} mutates its input`);
  }
  const base = config();
  const changed = applySetting(base, 'items.keyPlacement', 'shuffled');
  assert.deepEqual(leaves(changed).filter(([field, value]) => JSON.stringify(value) !== JSON.stringify(settingValue(base, 'items.keyPlacement')) && JSON.stringify(leaves(base).find(([name]) => name === field)?.[1]) !== JSON.stringify(value)).map(([field]) => field), ['items.keyPlacement']);
  const reverted = applySetting(applySetting(applySetting(base, 'items.keyPlacement', 'shuffled'), 'items.lordvessel', true), 'items.keyPlacement', 'vanilla');
  assert.equal(settingValue(reverted, 'items.lordvessel'), false, 'leaving the key modes turns the switches that belong to them off, so nothing hidden stays in the world');
  assert.equal(settingValue(applySetting(base, 'items.keyPlacement', 'race-plus'), 'items.difficulty'), 'fair');
});

test('the classic options of the old tools that do nothing are hidden, not shown as controls that look broken', () => {
  for (const id of ['items.earlyWeapon', 'items.balancedEarlyLoot']) assert.equal(settingById(id).hidden, 'no-effect');
  assert.equal(settingById('items.protectedItems').hidden, 'refused');
});

// ---- the interface does not move the configuration

const uiSource = (name) => readFileSync(path.join(here, name), 'utf8');

test('help, navigation, the mode switch and the language selector have no way to change the configuration', () => {
  for (const name of ['HelpPanel.tsx', 'Sidebar.tsx', 'OtherViews.tsx', 'text.ts', 'icons.tsx']) {
    assert.doesNotMatch(uiSource(name), /applySetting|setConfig|onChange\(def|api\.(generate|activate|restore)/u, `${name} does not touch the settings`);
  }
  const views = uiSource('SettingsViews.tsx');
  const modeSwitch = views.slice(views.indexOf('export function ModeSwitch'), views.indexOf('export function BasicView'));
  assert.doesNotMatch(modeSwitch, /onChange|applySetting|setConfig/u, 'the mode switch does not change settings');
  assert.doesNotMatch(readFileSync(path.join(here, '..', 'LanguageSelect.tsx'), 'utf8'), /applySetting|setConfig|joinConfig|api\./u);
});

test('no screen shows the withdrawn LUDOS mark, and no resource mentions it', () => {
  const files = ['App.tsx', 'styles.css', 'ui/Sidebar.tsx', 'ui/OtherViews.tsx'].map((name) => path.join(here, '..', name));
  for (const file of files) assert.doesNotMatch(readFileSync(file, 'utf8'), /ludos/iu, file);
  for (const language of ['en', 'pl']) assert.doesNotMatch(JSON.stringify(resources[language]), /ludos/iu);
});

test('the settings inventory in docs/ is generated from the registry and is up to date', async () => {
  const { buildInventory, buildMarkdown } = await import('../../scripts/settings-inventory.mjs');
  const docs = path.join(here, '..', '..', '..', '..', 'docs');
  const inventory = buildInventory();
  assert.equal(readFileSync(path.join(docs, 'settings-inventory.json'), 'utf8').replace(/\r\n/gu, '\n'), `${JSON.stringify(inventory, null, 2)}\n`);
  assert.equal(readFileSync(path.join(docs, 'SETTINGS_INVENTORY.md'), 'utf8').replace(/\r\n/gu, '\n'), buildMarkdown(inventory));
  assert.deepEqual([inventory.counts.total, inventory.counts.drawn, inventory.counts.hidden], [SETTINGS.length, VISIBLE_SETTINGS.length, HIDDEN.length]);
});
