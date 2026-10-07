import assert from 'node:assert/strict';
import test from 'node:test';
import { languageFromSystem, mainMessage, MAIN_MESSAGE_TABLES, normalizeLanguageSetting, resolveLanguage } from '../dist-electron/language.js';
import { migrateSettings } from '../dist-electron/settings.js';

test('Auto follows the operating system: Polish locales give Polish, English and unsupported ones give English', () => {
  for (const locale of ['pl', 'pl-PL', 'pl_PL', 'PL', 'pl-Latn-PL']) assert.equal(resolveLanguage('auto', [locale]), 'pl', locale);
  for (const locale of ['en', 'en-US', 'en-GB', 'EN']) assert.equal(resolveLanguage('auto', [locale]), 'en', locale);
  for (const locale of ['de-DE', 'ja-JP', 'zh-Hans-CN', '', 'x', null, undefined, 7]) assert.equal(resolveLanguage('auto', [locale]), 'en', String(locale));
  assert.equal(resolveLanguage('auto', []), 'en');
  assert.equal(resolveLanguage(undefined, ['pl-PL']), 'pl', 'no saved choice means Auto');
});

test('the first supported system language wins; unsupported ones before it are skipped', () => {
  assert.equal(languageFromSystem(['de-DE', 'pl-PL', 'en-US']), 'pl');
  assert.equal(languageFromSystem(['en-US', 'pl-PL']), 'en');
});

test('a saved explicit choice always wins over the system language', () => {
  assert.equal(resolveLanguage('en', ['pl-PL']), 'en');
  assert.equal(resolveLanguage('pl', ['en-US']), 'pl');
  assert.equal(resolveLanguage('pl', []), 'pl');
});

test('the language setting is validated and persisted without touching other settings', () => {
  assert.equal(normalizeLanguageSetting('pl'), 'pl');
  assert.equal(normalizeLanguageSetting('auto'), 'auto');
  assert.equal(normalizeLanguageSetting('de'), null);
  assert.equal(normalizeLanguageSetting(5), null);
  assert.deepEqual(migrateSettings({ gamePath: 'D:\Games\DSR', language: 'pl' }), { settings: { gamePath: 'D:\Games\DSR', language: 'pl' }, changed: false });
  assert.deepEqual(migrateSettings({ language: 'en' }), { settings: { language: 'en' }, changed: false });
  assert.deepEqual(migrateSettings({ language: 'xx' }), { settings: {}, changed: true }, 'an unknown language is dropped');
  assert.deepEqual(migrateSettings({ language: 'auto' }), { settings: {}, changed: true }, 'Auto is stored as no value');
});

test('the main process messages exist in both languages with the same variables', () => {
  const english = MAIN_MESSAGE_TABLES.en;
  const polish = MAIN_MESSAGE_TABLES.pl;
  assert.deepEqual(Object.keys(polish).sort(), Object.keys(english).sort());
  const variables = (text) => [...text.matchAll(/\{\{(\w+)\}\}/gu)].map((match) => match[1]).sort();
  for (const key of Object.keys(english)) assert.deepEqual(variables(polish[key]), variables(english[key]), key);
  assert.equal(mainMessage('pl', 'backendExited', { code: 3 }), 'Silnik zakończył pracę z kodem 3.');
  assert.equal(mainMessage('en', 'backendExited', { code: 3 }), 'Backend exited with code 3.');
});
