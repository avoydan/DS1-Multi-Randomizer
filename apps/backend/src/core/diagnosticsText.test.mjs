import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import i18next from 'i18next';
import { DEFAULT_GENERATION_SETTINGS, buildDiagnosticsText, buildSupportReadme, buildSupportSummary } from '@ds1tr/contracts';
import { buildDiagnostics } from '../../dist/core/diagnostics.js';

// The texts of the diagnostics view, "Copy diagnostics", summary.txt and README.txt (docs/DIAGNOSTICS.md): built in the app language from the real
// translation resources, with every key present in both languages and no raw key, [object Object] or undefined on the page.
const localeRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'desktop', 'src', 'locales');
const areas = ['common', 'settings', 'hints', 'diagnostics', 'support', 'errors'];
const load = (language) => Object.fromEntries(areas.map((name) => [name, JSON.parse(readFileSync(path.join(localeRoot, language, `${name}.json`), 'utf8'))]));
const translators = {};
for (const language of ['en', 'pl']) {
  const instance = i18next.createInstance();
  await instance.init({ resources: { en: { translation: load('en') }, pl: { translation: load('pl') } }, lng: language, fallbackLng: false, defaultNS: 'translation', interpolation: { escapeValue: false }, returnNull: false });
  translators[language] = (key, params) => { assert.ok(instance.exists(key), `${language}: missing translation ${key}`); return instance.t(key, params); };
}

const operation = (overrides = {}) => ({ correlationId: '3f2b8c1e-1111-4222-8333-444455556666', kind: 'generate', startedAt: '2026-10-07T10:00:00.000Z', finishedAt: '2026-10-07T10:00:09.000Z', ok: true, code: null, stage: null,
  seedCode: 'DS1TR1-06NJDCP6-ABCD-2G', profile: 'ds1tr-gen-v1', historyId: 'h1', timings: { ITEM_PLAN: 4200, FOG_PLAN: 4800 }, counts: { info: 5, warning: 1, error: 0, events: 12 }, windows: {}, warnings: [], errors: [], runId: 'run-a', ...overrides });
const entry = { id: 'h1', createdAt: '2026-10-07T10:00:00.000Z', seedCode: 'DS1TR1-06NJDCP6-ABCD-2G', profile: 'ds1tr-gen-v1', masterSeed: 'seed', settings: structuredClone(DEFAULT_GENERATION_SETTINGS), status: 'applied', packagePath: 'x', manifestPath: 'y', spoilerPath: null };
const diagnostics = (extra = {}) => buildDiagnostics({
  now: new Date('2026-10-07T11:00:00.000Z'), versions: { desktop: '0.31.0', backend: '0.43.0', contracts: '0.29.0' },
  profile: { id: 'ds1tr-gen-v1', combinedRandomizerVersions: ['0.5.66'], fogHostVersions: ['0.5.0'], fogModCommit: '6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f', worldDataId: 'f394fa536ba9b66d' },
  build: { schema: 'ds1tr-build-info-1', version: '0.31.0', commit: 'abc1234', mode: 'development', versions: { desktop: '0.31.0', backend: '0.43.0', contracts: '0.29.0', foghostProject: '0.6.0' }, builtAt: '2026-10-07T09:00:00.000Z' },
  installMode: 'installed', language: 'pl', platform: { os: 'win32 10.0', arch: 'x64', node: 'v20.19.0' }, game: { pathConfigured: true, state: 'active', pristineSnapshot: true },
  operations: { schemaVersion: 1, generate: { ...operation(), workspacePath: null, packagePath: null } },
  world: { entry, manifest: { appVersion: '0.43.0', attempts: [{ combinedAttempt: 0, fogAttempt: 0, result: 'success' }], files: [{ path: 'a', size: 5, sha256: 'b'.repeat(64) }], engines: { combined: '0.5.66', fog: '0.5.0 / 6e6dc78' } }, runRoot: 'C:\\x\\runs' },
  historyCount: 3, ...extra,
});
const failing = () => diagnostics({ operations: { schemaVersion: 1, generate: { ...operation({ ok: false, code: 'FOG_RETRIES_EXHAUSTED', stage: 'FOG_PLAN' }), workspacePath: null, packagePath: null },
  lastFailure: { ...operation({ ok: false, code: 'FOG_RETRIES_EXHAUSTED', stage: 'FOG_PLAN' }), workspacePath: null, packagePath: null } } });

const clean = (text) => { assert.ok(!/\[object|undefined|NaN|\b(?:diagnostics|hints|settings|support)\.(?:field|section|operation|installMode|signing|gameState|readme|levels|summary|where|items|enemies|fog)/u.test(text), text); };

test('the diagnostics text is complete and readable in English and Polish, with labels translated and codes, versions and ids untouched', () => {
  for (const language of ['en', 'pl']) {
    for (const model of [diagnostics(), failing(), diagnostics({ world: null, game: { pathConfigured: false, state: null, pristineSnapshot: false }, operations: { schemaVersion: 1 } })]) {
      const text = buildDiagnosticsText(model, translators[language]);
      clean(text);
      assert.match(text, /0\.31\.0/u);
      assert.ok(text.length > 400);
    }
  }
  const polish = buildDiagnosticsText(failing(), translators.pl);
  assert.match(polish, /Ostatnie losowanie: zakończone błędem/u);
  assert.match(polish, /Kod przyczyny: FOG_RETRIES_EXHAUSTED/u);
  assert.match(polish, /Etap: FOG_PLAN/u);
  assert.match(polish, /Kod seeda: DS1TR1-06NJDCP6-ABCD-2G/u);
  assert.match(polish, /Instalacja: zainstalowana/u);
  assert.match(polish, /Podpis: niepodpisana/u);
  const english = buildDiagnosticsText(failing(), translators.en);
  assert.match(english, /Last randomization: failed/u);
  assert.match(english, /Game: randomized game applied/u);
  assert.match(english, /ITEM_PLAN 4\.2 s, FOG_PLAN 4\.8 s/u);
});

test('the text contains no folder of the person, whatever the model holds', () => {
  const text = buildDiagnosticsText(diagnostics(), translators.en);
  assert.ok(!/[A-Za-z]:\\|\/Users\/|\/home\//u.test(text));
});

test('summary.txt is the diagnostics text, and README.txt explains the contents, the exclusions and the redaction in the chosen language', () => {
  assert.equal(buildSupportSummary({ diagnostics: diagnostics(), language: 'en' }, translators.en), buildDiagnosticsText(diagnostics(), translators.en));
  const english = buildSupportReadme(translators.en);
  const polish = buildSupportReadme(translators.pl);
  clean(english); clean(polish);
  for (const needle of ['summary.txt', 'diagnostics.json', 'generation/', 'logs/', 'release/', '<USER>', '<GAME_DIR>']) { assert.ok(english.includes(needle), needle); assert.ok(polish.includes(needle), needle); }
  assert.match(english, /What is never in it/u);
  assert.match(polish, /Czego nigdy nie zawiera/u);
  assert.match(polish, /Wczytaj seed/u);
});

test('the formatter hook is used for times, never for ids or hashes', () => {
  const text = buildDiagnosticsText(diagnostics(), translators.en, { formatTime: () => 'FORMATTED' });
  assert.match(text, /World created: FORMATTED/u);
  assert.ok(text.includes('3f2b8c1e-1111-4222-8333-444455556666') && text.includes('b'.repeat(64)) === false);
});
