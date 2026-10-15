import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { loadPersistedSettings, savePersistedSettings } from '../../dist/core/settingsStore.js';
import { RunHistory } from '../../dist/core/runHistory.js';
import { prepareRun } from '../../dist/core/seededRun.js';
import { fullConfig, fullSettings, BROAD_ITEMS } from './seedTestSupport.mjs';

async function stateRoot(fn) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-state-'));
  try { await fn(root); } finally { await rm(root, { recursive: true, force: true }); }
}

test('settings persist between sessions: what was saved is what the next start loads', async () => stateRoot(async (root) => {
  assert.deepEqual(await loadPersistedSettings(root), { found: false, persisted: null, warning: null }, 'a first start is not an error');
  const settings = fullSettings({ items: BROAD_ITEMS, fog: { language: 'POLISH' }, enemies: { placement: { algorithm: 'sampling-v1', fit: 'fits', customPool: ['c2500', 'c1200'] } } });
  await savePersistedSettings(root, { settings, generateSpoilerLog: true });
  const loaded = await loadPersistedSettings(root);
  assert.equal(loaded.found, true);
  assert.equal(loaded.persisted.schemaVersion, 1);
  assert.equal(loaded.persisted.generateSpoilerLog, true);
  assert.equal(loaded.persisted.settings.fog.language, 'POLISH');
  assert.deepEqual(loaded.persisted.settings.enemies.placement.customPool, ['c1200', 'c2500']);
  assert.equal(loaded.persisted.settings.items.randomizeShops, true);
  assert.equal(JSON.stringify(loaded).includes('gamePath'), false, 'the game path is machine state, kept elsewhere');
  assert.equal((await readdir(path.join(root, 'settings'))).filter((name) => name.endsWith('.tmp')).length, 0, 'no temporary file is left behind');
}));

test('unreadable, invalid or newer settings fall back to defaults with a plain warning, never an error', async () => stateRoot(async (root) => {
  const file = path.join(root, 'settings', 'ui-settings.json');
  await mkdir(path.dirname(file), { recursive: true });
  for (const [text, reason] of [
    ['{not json', /damaged/u],
    ['[]', /damaged/u],
    [JSON.stringify({ schemaVersion: 99, settings: fullSettings() }), /newer version/u],
    [JSON.stringify({ schemaVersion: 1, settings: { items: {} } }), /no longer valid/u],
    [JSON.stringify({ schemaVersion: 1, settings: fullSettings({ fog: { language: 'KLINGON' } }) }), /no longer valid/u],
  ]) {
    await writeFile(file, text);
    const loaded = await loadPersistedSettings(root);
    assert.equal(loaded.found, false);
    assert.match(loaded.warning, /could not be read/u);
    assert.match(loaded.warning, reason);
    assert.match(loaded.warning, /reset to the defaults/u);
  }
  await assert.rejects(savePersistedSettings(root, { settings: { items: {} }, generateSpoilerLog: false }), 'garbage is never written');
}));

function entryFor(seedCodeSeed, packagePath) {
  const prepared = prepareRun(fullConfig(), () => seedCodeSeed);
  return {
    seedCode: prepared.seedCode, profile: prepared.profile.id, masterSeed: prepared.config.masterSeed, settings: prepared.envelope.settings,
    packagePath, manifestPath: path.join(packagePath, 'run-manifest.json'), spoilerPath: null,
  };
}

test('run history persists across restarts, newest first, with the package flag computed from disk', async () => stateRoot(async (root) => {
  const kept = path.join(root, 'runs', 'kept');
  const gone = path.join(root, 'runs', 'gone');
  await mkdir(kept, { recursive: true });
  const first = new RunHistory(root);
  const older = await first.add({ ...entryFor('1111', gone), now: new Date('2026-10-01T10:00:00Z') });
  const newer = await first.add({ ...entryFor('2222', kept), now: new Date('2026-10-02T10:00:00Z') });
  assert.equal(older.status, 'generated');
  const second = new RunHistory(root);
  const list = await second.list();
  assert.deepEqual(list.map((entry) => entry.masterSeed), ['2222', '1111']);
  assert.equal(list[0].packageExists, true);
  assert.equal(list[1].packageExists, false, 'a cleaned-up run folder is reported, not an error');
  assert.equal(list[0].createdAt, '2026-10-02T10:00:00.000Z');
  assert.match(list[0].seedCode, /^DS1TR1-/u);
  assert.equal(list[0].profile, 'ds1tr-gen-v3');
  assert.equal(list[0].settings.fog.language, 'ENGLISH');
  assert.equal(newer.id === older.id, false);
}));

test('applying and restoring are recorded: one applied run at most, restoring marks it restored', async () => stateRoot(async (root) => {
  const history = new RunHistory(root);
  const a = path.join(root, 'a');
  const b = path.join(root, 'b');
  await history.add(entryFor('1', a));
  await history.add(entryFor('2', b));
  await history.markApplied(a);
  assert.deepEqual((await history.list()).map((entry) => [entry.masterSeed, entry.status]), [['2', 'generated'], ['1', 'applied']]);
  await history.markApplied(b);
  assert.deepEqual((await history.list()).map((entry) => [entry.masterSeed, entry.status]), [['2', 'applied'], ['1', 'restored']]);
  await history.markRestored();
  assert.deepEqual((await history.list()).map((entry) => entry.status), ['restored', 'restored']);
}));

test('concurrent additions are all kept and a damaged history file is set aside instead of crashing', async () => stateRoot(async (root) => {
  const history = new RunHistory(root);
  await Promise.all(['1', '2', '3', '4', '5'].map((seed) => history.add(entryFor(seed, path.join(root, seed)))));
  assert.equal((await history.list()).length, 5);
  await writeFile(path.join(root, 'history', 'runs.json'), '{broken');
  assert.deepEqual(await new RunHistory(root).list(), []);
  assert.equal((await readdir(path.join(root, 'history'))).some((name) => name.startsWith('runs.json.damaged-')), true);
  await history.add(entryFor('6', path.join(root, '6')));
  assert.equal((await history.list()).length, 1, 'a new run works again after a damaged file');
}));

test('Recreate needs only the history entry: it works without the old run folder and reproduces the same seed', async () => stateRoot(async (root) => {
  const history = new RunHistory(root);
  const original = prepareRun({ ...fullConfig({ items: BROAD_ITEMS }), masterSeed: '987654321' });
  await history.add({ seedCode: original.seedCode, profile: original.profile.id, masterSeed: original.config.masterSeed, settings: original.envelope.settings,
    packagePath: path.join(root, 'deleted-run-folder'), manifestPath: path.join(root, 'deleted-run-folder', 'run-manifest.json'), spoilerPath: null });
  const [entry] = await new RunHistory(root).list();
  assert.equal(entry.packageExists, false);
  // The app sends only the stored Seed Code (and this machine's game path).
  const recreated = prepareRun({ gamePath: 'D:\\Games\\Mine', seedCode: entry.seedCode });
  assert.equal(recreated.ok, true);
  assert.equal(recreated.seedCode, original.seedCode);
  assert.equal(recreated.config.masterSeed, '987654321');
  assert.deepEqual(recreated.envelope.settings, entry.settings);
  assert.equal(recreated.config.gamePath, 'D:\\Games\\Mine');
}));

test('generating the same world again refreshes its entry instead of adding another one (Load seed, Regenerate files)', async () => stateRoot(async (root) => {
  const history = new RunHistory(root);
  const first = await history.addOrRefresh({ ...entryFor('same-world', path.join(root, 'run-1')), now: new Date('2026-10-01T10:00:00Z') });
  const other = await history.addOrRefresh({ ...entryFor('another-world', path.join(root, 'other')), now: new Date('2026-10-02T10:00:00Z') });
  await history.markApplied(path.join(root, 'run-1'));
  await history.markRestored();
  const again = await history.addOrRefresh({ ...entryFor('same-world', path.join(root, 'run-2')), now: new Date('2026-10-05T10:00:00Z') });
  assert.equal(again.id, first.id, 'the identity of the entry is kept');
  assert.equal(again.seedCode, first.seedCode);
  assert.equal(again.createdAt, '2026-10-01T10:00:00.000Z', 'the original creation time is kept');
  assert.equal(again.packagePath, path.join(root, 'run-2'), 'the files are the new ones');
  assert.equal(again.status, 'generated');
  const list = await history.list();
  assert.equal(list.length, 2, 'no duplicate entry');
  assert.deepEqual(list.map((entry) => entry.id), [other.id, first.id], 'the order of the list is not disturbed');
}));

test('an entry can be deleted from the history and is then gone for good', async () => stateRoot(async (root) => {
  const history = new RunHistory(root);
  const a = await history.add(entryFor('1', path.join(root, '1')));
  await history.add(entryFor('2', path.join(root, '2')));
  assert.equal(await history.remove(a.id), true);
  assert.equal(await history.remove(a.id), false);
  assert.equal(await history.get(a.id), null);
  assert.equal((await new RunHistory(root).list()).length, 1);
}));

test('a world without the DLC is remembered, listed apart from the same world with the DLC, and recreated exactly (Load seed, Regenerate files, Recreate)', async () => stateRoot(async (root) => {
  const history = new RunHistory(root);
  const withDlc = prepareRun({ ...fullConfig({ fog: { enabled: false } }), masterSeed: '55555' });
  const body = fullConfig({ fog: { enabled: false } });
  body.output.includeDlc = false;
  const without = prepareRun({ ...body, masterSeed: '55555' });
  assert.equal(without.ok, true);
  assert.notEqual(without.seedCode, withDlc.seedCode);
  const record = (run, folder) => ({ seedCode: run.seedCode, profile: run.profile.id, masterSeed: run.config.masterSeed, settings: run.envelope.settings,
    packagePath: path.join(root, folder), manifestPath: path.join(root, folder, 'run-manifest.json'), spoilerPath: null });
  const a = await history.addOrRefresh(record(withDlc, 'on'));
  const b = await history.addOrRefresh(record(without, 'off'));
  assert.notEqual(a.id, b.id, 'the same master seed with and without the DLC are two worlds');
  const again = await history.addOrRefresh(record(without, 'off-2'));
  assert.equal(again.id, b.id, 'Regenerate files refreshes the world without the DLC');
  const [newest] = await new RunHistory(root).list();
  assert.equal(newest.settings.output.includeDlc, false);
  // The app sends only the stored Seed Code: the DLC choice is inside it.
  const recreated = prepareRun({ gamePath: 'D:\Games\Mine', seedCode: newest.seedCode });
  assert.equal(recreated.ok, true);
  assert.equal(recreated.config.output.includeDlc, false);
  assert.equal(recreated.seedCode, without.seedCode);
  // The persisted UI settings keep the choice between sessions.
  const settings = fullSettings({ fog: { enabled: false } });
  settings.output.includeDlc = false;
  await savePersistedSettings(root, { settings, generateSpoilerLog: false });
  assert.equal((await loadPersistedSettings(root)).persisted.settings.output.includeDlc, false);
}));
