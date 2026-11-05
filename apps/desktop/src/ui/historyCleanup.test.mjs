import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

// Run history cleanup (docs/RUN_HISTORY_CLEANUP.md): wiring checks over the sources. The deletion rules themselves (applied-run protection, run-owned files only, shared data kept)
// are tested against real folders in apps/backend/src/core/historyCleanup.test.mjs.
const desktop = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const read = (relative) => readFileSync(path.join(desktop, relative), 'utf8');
const locale = (language) => JSON.parse(read(`src/locales/${language}/history.json`));

test('nothing is deleted without a confirmation dialog, and cancelling calls no deletion', () => {
  const app = read('src/App.tsx');
  // The three actions only open a dialog; the API calls exist in exactly one place, behind the confirm handler.
  assert.match(app, /const requestDelete = \(entry: RunHistoryEntry\) => \{[^}]*setHistoryAction\(\{ kind: 'delete', entry \}\)/u);
  assert.match(app, /const requestClear = async \(kind: 'clear' \| 'old'\) => \{[\s\S]{0,200}api\.historyCleanupPreview\(\)/u);
  assert.equal((app.match(/api\.deleteHistoryEntry\(/gu) ?? []).length, 1);
  assert.equal((app.match(/api\.clearHistory\(/gu) ?? []).length, 1);
  const confirm = app.slice(app.indexOf('const confirmHistoryAction'), app.indexOf('const confirmHistoryAction') + 900);
  assert.match(confirm, /api\.deleteHistoryEntry[\s\S]*api\.clearHistory/u);
  // Cancelling just closes the dialog.
  assert.equal((app.match(/onCancel=\{\(\) => setHistoryAction\(null\)\}/gu) ?? []).length, 2);
  const dialog = read('src/ConfirmDialog.tsx');
  assert.match(dialog, /role="alertdialog"/u);
  assert.match(dialog, /cancelRef\.current\?\.focus\(\)/u, 'the safe choice has the focus');
  assert.match(dialog, /event\.key === 'Escape'[^\n]*onCancel\(\)/u);
});

test('the confirmation states how many entries go, what it removes and what it does not touch', () => {
  const app = read('src/App.tsx');
  assert.match(app, /history\.dialog\.clearBody', 'history\.dialog\.oldBody'|history\.dialog\.clearBody' : 'history\.dialog\.oldBody'/u);
  assert.match(app, /count: historyAction\.kind === 'clear' \? historyAction\.preview\.removable : historyAction\.preview\.removableOld/u);
  assert.match(app, /history\.dialog\.protected/u);
  assert.match(app, /history\.dialog\.safety/u);
  for (const language of ['pl', 'en']) {
    const { dialog } = locale(language);
    assert.match(dialog.clearBody, /\{\{count\}\}/u);
    assert.match(dialog.oldBody, /\{\{count\}\}/u);
    assert.match(dialog.clearConfirm, /\{\{count\}\}/u);
    assert.ok(dialog.safety.length > 80, 'the safety text is a real sentence');
  }
  const pl = locale('pl').dialog;
  assert.match(pl.safety, /nie odinstalowuje aplikacji/u);
  assert.match(pl.safety, /nie zmienia zapisów gry Dark Souls/u);
  assert.match(pl.safety, /nie zmienia zainstalowanych plików gry/u);
  assert.match(pl.clearBody, /pliki diagnostyczne/u);
  assert.match(pl.protected, /Zastosowane losowanie zostaje zachowane/u);
});

test('the visible actions use the Polish labels and the individual delete is disabled for the applied world', () => {
  const panel = read('src/RunHistoryPanel.tsx');
  assert.match(panel, /onClick=\{onClear\}>\{t\('history\.clear'\)\}/u);
  assert.match(panel, /onClick=\{onRemoveOld\}>\{t\('history\.removeOld'\)\}/u);
  assert.match(panel, /disabled=\{entry\.status === 'applied'\}[^\n]*history\.deleteApplied[^\n]*onDelete\(entry\)/u);
  const pl = locale('pl');
  assert.equal(pl.clear, 'Wyczyść historię');
  assert.equal(pl.removeOld, 'Usuń stare runy');
  assert.equal(pl.delete, 'Usuń z historii');
  assert.equal(pl.legacy, 'Starszy profil');
  assert.equal(locale('en').clear, 'Clear history');
});

test('every row shows the application version and the generation profile, and an older profile is labelled without calling its Seed Code invalid', () => {
  const panel = read('src/RunHistoryPanel.tsx');
  assert.match(panel, /entry\.appVersion \? t\('history\.appVersion', \{ version: entry\.appVersion \}\) : t\('history\.appVersionUnknown'\)/u);
  assert.match(panel, /<code className="history-profile">\{entry\.profile\}<\/code>/u);
  assert.match(panel, /entry\.profile !== CURRENT_GENERATION_PROFILE && <span className="history-legacy" title=\{t\('history\.legacyHint'\)\}>\{t\('history\.legacy'\)\}/u);
  assert.match(panel, /import \{ CURRENT_GENERATION_PROFILE,/u);
  for (const language of ['pl', 'en']) {
    const texts = locale(language);
    assert.match(texts.appVersion, /\{\{version\}\}/u);
    assert.doesNotMatch(texts.legacyHint, /(invalid|nieprawid|błęd|zepsut)/iu, 'the label never suggests the Seed Code is broken');
    assert.match(texts.legacyHint, /(still valid|nadal jest poprawny)/u);
  }
});

test('the backend routes exist, only the cleanup ones change data, and both languages carry every key the dialog uses', () => {
  const api = read('src/api.ts');
  assert.match(api, /historyCleanupPreview: \(\) => post<HistoryCleanupPreview>\('\/history\/cleanup-preview', \{\}\)/u);
  assert.match(api, /clearHistory: \(onlyOldProfiles: boolean\) => post<HistoryCleanupResult>\('\/history\/clear', \{ onlyOldProfiles \}\)/u);
  const used = [...read('src/App.tsx').matchAll(/t\('(history\.dialog\.[A-Za-z]+)'/gu)].map((match) => match[1]);
  for (const key of new Set(used)) {
    const leaf = key.split('.').at(-1);
    for (const language of ['pl', 'en']) assert.ok(locale(language).dialog[leaf], `${language} has ${key}`);
  }
  assert.deepEqual(Object.keys(locale('pl').dialog).sort(), Object.keys(locale('en').dialog).sort());
});
