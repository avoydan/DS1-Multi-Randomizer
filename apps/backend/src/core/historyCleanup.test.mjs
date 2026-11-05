import assert from 'node:assert/strict';
import { access, mkdir, mkdtemp, readFile, readdir, rm, utimes, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { RunHistory } from '../../dist/core/runHistory.js';
import { ORPHAN_MINIMUM_AGE_MS, activePackagePaths, clearHistory, deleteRun, previewCleanup, runFolderOf } from '../../dist/core/historyCleanup.js';

const exists = async (target) => { try { await access(target); return true; } catch { return false; } };

/** A state folder like the application's: runs, history, one installation with a pristine snapshot and an activation journal, world data, settings and a support bundle. */
async function makeState() {
  const stateRoot = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-history-'));
  const runRoot = path.join(stateRoot, 'runs');
  const history = new RunHistory(stateRoot);
  const shared = {
    pristine: path.join(stateRoot, 'installs', 'abc', 'pristine', 'param', 'GameParam.parambnd.dcx'),
    worldData: path.join(stateRoot, 'world-data', 'abcd', 'dist', 'fog.txt'),
    settings: path.join(stateRoot, 'settings.json'),
    support: path.join(stateRoot, 'support', 'bundle-1.zip'),
    snapshot: path.join(stateRoot, 'installs', 'abc', 'snapshot.json'),
  };
  for (const file of Object.values(shared)) { await mkdir(path.dirname(file), { recursive: true }); await writeFile(file, 'shared'); }
  return { stateRoot, runRoot, history, shared };
}

let counter = 0;
/** One run: a folder with a package, a manifest, hints and a witness, plus its history record. */
async function addRun(state, { profile = 'ds1tr-gen-v6', appVersion = '0.41.0', seedCode } = {}) {
  counter += 1;
  const folder = path.join(state.runRoot, `run-${counter}`);
  const packagePath = path.join(folder, 'fog-0-0', 'game');
  await mkdir(path.join(packagePath, 'event'), { recursive: true });
  await writeFile(path.join(packagePath, 'event', 'common.emevd.dcx'), 'x');
  await writeFile(path.join(folder, 'hints.json'), '{}');
  await writeFile(path.join(folder, 'solver-witness.json'), '{}');
  const manifestPath = path.join(packagePath, 'run-manifest.json');
  await writeFile(manifestPath, '{}');
  const entry = await state.history.add({
    seedCode: seedCode ?? `DS1TR1-TEST-${counter}`, profile, masterSeed: String(counter), settings: {}, packagePath, manifestPath, spoilerPath: null, appVersion,
  });
  return { entry, folder, packagePath };
}

const writeJournal = (state, packagePath) => writeFile(path.join(state.stateRoot, 'installs', 'abc', 'activation.json'), JSON.stringify({ packagePath }));
const cleanup = (state) => rm(state.stateRoot, { recursive: true, force: true });

test('clearing an empty history changes nothing and does not fail', async () => {
  const state = await makeState();
  try {
    const preview = await previewCleanup(await state.history.list(), state.runRoot, state.stateRoot, 'ds1tr-gen-v7');
    assert.deepEqual(preview, { total: 0, removable: 0, protected: 0, removableOld: 0, orphanFolders: 0 });
    const result = await clearHistory(state.history, state.runRoot, state.stateRoot, { includeOrphans: true });
    assert.deepEqual(result, { ok: true, removedEntries: 0, keptEntries: 0, removedFolders: 0, failedFolders: 0 });
    for (const file of Object.values(state.shared)) assert.equal(await exists(file), true);
  } finally { await cleanup(state); }
});

test('clearing several runs removes the records and the run folders they own, and nothing shared', async () => {
  const state = await makeState();
  try {
    const runs = [await addRun(state), await addRun(state), await addRun(state, { profile: 'ds1tr-gen-v7', appVersion: '0.42.0' })];
    const result = await clearHistory(state.history, state.runRoot, state.stateRoot, { includeOrphans: true });
    assert.deepEqual(result, { ok: true, removedEntries: 3, keptEntries: 0, removedFolders: 3, failedFolders: 0 });
    assert.deepEqual(await state.history.list(), []);
    for (const run of runs) assert.equal(await exists(run.folder), false, 'the run folder is gone');
    for (const file of Object.values(state.shared)) assert.equal(await exists(file), true, `${path.basename(file)} is shared data and stays`);
    assert.equal(await readFile(state.shared.settings, 'utf8'), 'shared');
  } finally { await cleanup(state); }
});

test('the applied run is protected: by its status, and by the activation journal even when its status says otherwise', async () => {
  const state = await makeState();
  try {
    const old = await addRun(state);
    const applied = await addRun(state);
    const journalled = await addRun(state);
    await state.history.markApplied(applied.packagePath);
    // The record of the journalled run claims it was restored, but the journal still names its package: it must stay.
    await writeJournal(state, journalled.packagePath);
    assert.equal((await activePackagePaths(state.stateRoot)).size, 1);
    const preview = await previewCleanup(await state.history.list(), state.runRoot, state.stateRoot, 'ds1tr-gen-v7');
    assert.equal(preview.total, 3);
    assert.equal(preview.protected, 2);
    assert.equal(preview.removable, 1);
    const result = await clearHistory(state.history, state.runRoot, state.stateRoot, { includeOrphans: true });
    assert.equal(result.removedEntries, 1);
    assert.equal(result.keptEntries, 2);
    assert.equal(await exists(old.folder), false);
    assert.equal(await exists(applied.folder), true, 'the applied run keeps its folder');
    assert.equal(await exists(journalled.folder), true, 'the journal names this package: its folder stays');
    assert.deepEqual((await state.history.list()).map((entry) => entry.id).sort(), [applied.entry.id, journalled.entry.id].sort());
    const refused = await deleteRun(state.history, applied.entry.id, state.runRoot, state.stateRoot);
    assert.equal(refused.ok, false);
    assert.equal(refused.code, 'RUN_PROTECTED');
    assert.equal(await exists(applied.folder), true);
  } finally { await cleanup(state); }
});

test('deleting one run removes its record and its folder only', async () => {
  const state = await makeState();
  try {
    const first = await addRun(state);
    const second = await addRun(state);
    const result = await deleteRun(state.history, first.entry.id, state.runRoot, state.stateRoot);
    assert.deepEqual(result, { ok: true, removedFolder: true });
    assert.equal(await exists(first.folder), false);
    assert.equal(await exists(path.join(first.folder, 'hints.json')), false, 'hints and witness are run-owned and go with the run');
    assert.equal(await exists(second.folder), true);
    assert.equal(await exists(path.join(second.folder, 'solver-witness.json')), true);
    assert.deepEqual((await state.history.list()).map((entry) => entry.id), [second.entry.id]);
    for (const file of Object.values(state.shared)) assert.equal(await exists(file), true);
    const missing = await deleteRun(state.history, first.entry.id, state.runRoot, state.stateRoot);
    assert.equal(missing.ok, false);
    assert.equal(missing.code, 'RUN_NOT_FOUND');
  } finally { await cleanup(state); }
});

test('a folder that another record still points into is kept when one record is deleted', async () => {
  const state = await makeState();
  try {
    const first = await addRun(state);
    // A second record that points into the same run folder (a refreshed world can leave two records on one folder).
    const second = await state.history.add({ seedCode: 'DS1TR1-SHARED', profile: 'ds1tr-gen-v6', masterSeed: '9', settings: {}, packagePath: first.packagePath, manifestPath: path.join(first.packagePath, 'run-manifest.json'), spoilerPath: null });
    const result = await deleteRun(state.history, second.id, state.runRoot, state.stateRoot);
    assert.deepEqual(result, { ok: true, removedFolder: false });
    assert.equal(await exists(first.folder), true);
    assert.equal((await state.history.list()).length, 1);
  } finally { await cleanup(state); }
});

test('run folders nothing points at are removed by a full clear, but not while they are young (a generation may still be writing)', async () => {
  const state = await makeState();
  try {
    const old = path.join(state.runRoot, 'failed-old'); const young = path.join(state.runRoot, 'running-now');
    await mkdir(old, { recursive: true }); await mkdir(young, { recursive: true });
    await writeFile(path.join(old, 'failed-manifest.json'), '{}'); await writeFile(path.join(young, 'work.tmp'), 'x');
    const longAgo = new Date(Date.now() - ORPHAN_MINIMUM_AGE_MS - 60_000);
    await utimes(old, longAgo, longAgo);
    const preview = await previewCleanup([], state.runRoot, state.stateRoot, 'ds1tr-gen-v7');
    assert.equal(preview.orphanFolders, 1);
    const result = await clearHistory(state.history, state.runRoot, state.stateRoot, { includeOrphans: true });
    assert.equal(result.removedFolders, 1);
    assert.equal(await exists(old), false);
    assert.equal(await exists(young), true, 'a folder younger than the minimum age is never removed as an orphan');
  } finally { await cleanup(state); }
});

test('removing old runs keeps the current profile, the applied run and the orphans', async () => {
  const state = await makeState();
  try {
    const legacy = await addRun(state, { profile: 'ds1tr-gen-v6' });
    const legacyApplied = await addRun(state, { profile: 'ds1tr-gen-v5' });
    const current = await addRun(state, { profile: 'ds1tr-gen-v7', appVersion: '0.42.0' });
    await state.history.markApplied(legacyApplied.packagePath);
    const orphan = path.join(state.runRoot, 'failed-old'); await mkdir(orphan, { recursive: true });
    const longAgo = new Date(Date.now() - ORPHAN_MINIMUM_AGE_MS - 60_000); await utimes(orphan, longAgo, longAgo);
    const preview = await previewCleanup(await state.history.list(), state.runRoot, state.stateRoot, 'ds1tr-gen-v7');
    assert.equal(preview.removableOld, 1);
    const result = await clearHistory(state.history, state.runRoot, state.stateRoot, { includeOrphans: false, select: (entry) => entry.profile !== 'ds1tr-gen-v7' });
    assert.equal(result.removedEntries, 1);
    assert.equal(await exists(legacy.folder), false);
    assert.equal(await exists(legacyApplied.folder), true, 'the applied run is never removed, old profile or not');
    assert.equal(await exists(current.folder), true);
    assert.equal(await exists(orphan), true, 'old-run cleanup does not touch folders without a record');
  } finally { await cleanup(state); }
});

test('a record that points outside the run root loses its record but never an outside folder', async () => {
  const state = await makeState();
  const outside = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-outside-'));
  try {
    await writeFile(path.join(outside, 'precious.txt'), 'keep');
    await state.history.add({ seedCode: 'DS1TR1-OUTSIDE', profile: 'ds1tr-gen-v6', masterSeed: '1', settings: {}, packagePath: path.join(outside, 'game'), manifestPath: path.join(outside, 'manifest.json'), spoilerPath: null });
    assert.equal(runFolderOf({ packagePath: path.join(outside, 'game'), manifestPath: path.join(outside, 'm.json') }, state.runRoot), null);
    const result = await clearHistory(state.history, state.runRoot, state.stateRoot, { includeOrphans: true });
    assert.equal(result.removedEntries, 1);
    assert.equal(await exists(path.join(outside, 'precious.txt')), true);
  } finally { await cleanup(state); await rm(outside, { recursive: true, force: true }); }
});

test('asking for a preview and then not confirming changes nothing at all', async () => {
  const state = await makeState();
  try {
    await addRun(state); await addRun(state);
    const before = JSON.stringify((await state.history.list()).map((entry) => entry.id));
    const folders = (await readdir(state.runRoot)).sort();
    await previewCleanup(await state.history.list(), state.runRoot, state.stateRoot, 'ds1tr-gen-v7');
    assert.equal(JSON.stringify((await state.history.list()).map((entry) => entry.id)), before);
    assert.deepEqual((await readdir(state.runRoot)).sort(), folders);
  } finally { await cleanup(state); }
});

test('the application version recorded with a run is kept in its history entry', async () => {
  const state = await makeState();
  try {
    const { entry } = await addRun(state, { appVersion: '0.42.0', profile: 'ds1tr-gen-v7' });
    const [listed] = await state.history.list();
    assert.equal(listed.id, entry.id);
    assert.equal(listed.appVersion, '0.42.0');
    assert.equal(listed.profile, 'ds1tr-gen-v7');
  } finally { await cleanup(state); }
});
