import assert from 'node:assert/strict';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { DEFAULT_GENERATION_SETTINGS } from '@ds1tr/contracts';
import { hintsForEntry, persistHints } from '../../dist/core/hintStore.js';
import { RunHistory } from '../../dist/core/runHistory.js';
import { pickWorld } from '../../dist/core/diagnostics.js';

// "Restore and create world" (docs/HINT_SHEET.md, docs/DIAGNOSTICS.md): an applied world is restored and a new world is created with the same settings. The old
// history entry stays intact, the new one has its own hint data and diagnostics, and nothing of the old world is shown for the new one.
const placement = (name, area) => ({
  itemId: 1, itemName: name, equipType: 3, sourcePool: 'world', targetPool: 'world', progression: true, keyName: 'basement_key', from: 'Humanity',
  originalLocation: { area: 'Depths', map: 'm10_00_00_00', itemLot: 10 }, randomizedLocation: { area, map: 'm10_02_00_00', itemLot: 20 },
});

async function generated(root, history, runRoot, { seedCode, area, now }) {
  const workspace = path.join(runRoot, seedCode.replace(/[^A-Z0-9]/gu, ''));
  const seed = path.join(workspace, 'combined-0', 'seed_x');
  await mkdir(seed, { recursive: true });
  await writeFile(path.join(seed, 'randomizer.json'), JSON.stringify({ placements: { items: [placement('Basement Key', area)] } }));
  await writeFile(path.join(seed, 'run-manifest.json'), JSON.stringify({ appVersion: '0.44.1', attempts: [], files: [] }));
  const settings = structuredClone(DEFAULT_GENERATION_SETTINGS);
  const hintsPath = await persistHints(workspace, { seedCode, profile: 'ds1tr-gen-v1', createdAt: now.toISOString(), appVersion: '0.44.1', settings });
  return history.addOrRefresh({ seedCode, profile: 'ds1tr-gen-v1', masterSeed: seedCode, settings, packagePath: seed, manifestPath: path.join(seed, 'run-manifest.json'), spoilerPath: null, hintsPath, now });
}

test('restoring and creating a new world keeps the old entry intact and gives the new world its own hints, status and diagnostics world', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-restore-'));
  try {
    const history = new RunHistory(root);
    const runRoot = path.join(root, 'runs');
    const first = await generated(root, history, runRoot, { seedCode: 'DS1TR1-AAAA-1111', area: 'Firelink Shrine', now: new Date('2026-10-07T10:00:00Z') });
    await history.markApplied(first.packagePath);
    assert.equal(pickWorld(await history.list()).id, first.id, 'the applied world is the one described');

    // Restore, then create the new world (the settings the person had stay as they are).
    await history.markRestored();
    const second = await generated(root, history, runRoot, { seedCode: 'DS1TR1-BBBB-2222', area: 'Anor Londo', now: new Date('2026-10-07T11:00:00Z') });

    const entries = await history.list();
    assert.equal(entries.length, 2);
    const oldEntry = entries.find((entry) => entry.id === first.id);
    const newEntry = entries.find((entry) => entry.id === second.id);
    assert.equal(oldEntry.status, 'restored', 'the old world is restored, not deleted');
    assert.equal(oldEntry.seedCode, 'DS1TR1-AAAA-1111');
    assert.equal(oldEntry.createdAt, '2026-10-07T10:00:00.000Z', 'its identity is untouched');
    assert.equal(newEntry.status, 'generated');
    assert.notEqual(oldEntry.hintsPath, newEntry.hintsPath);
    assert.notEqual(oldEntry.packagePath, newEntry.packagePath);
    assert.equal(pickWorld(entries).id, second.id, 'with nothing applied the newest world is described');

    // No stale data: each world reads its own hint data.
    const oldHints = await hintsForEntry(oldEntry, runRoot, '0.44.1');
    const newHints = await hintsForEntry(newEntry, runRoot, '0.44.1');
    assert.equal(oldHints.seedCode, 'DS1TR1-AAAA-1111');
    assert.equal(newHints.seedCode, 'DS1TR1-BBBB-2222');
    assert.equal(oldHints.records[0].randomized.area, 'Firelink Shrine');
    assert.equal(newHints.records[0].randomized.area, 'Anor Londo');

    // Applying the new world makes it the applied one; the old stays restored.
    await history.markApplied(second.packagePath);
    const afterApply = await history.list();
    assert.equal(afterApply.find((entry) => entry.id === second.id).status, 'applied');
    assert.equal(afterApply.find((entry) => entry.id === first.id).status, 'restored');
    assert.equal(pickWorld(afterApply).id, second.id);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('generating the same world again refreshes its entry (and its hint data) instead of adding a second one', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-restore-'));
  try {
    const history = new RunHistory(root);
    const runRoot = path.join(root, 'runs');
    const once = await generated(root, history, runRoot, { seedCode: 'DS1TR1-CCCC-3333', area: 'Depths', now: new Date('2026-10-07T10:00:00Z') });
    const again = await generated(root, history, runRoot, { seedCode: 'DS1TR1-CCCC-3333', area: 'Depths', now: new Date('2026-10-07T12:00:00Z') });
    assert.equal(again.id, once.id);
    assert.equal((await history.list()).length, 1);
    assert.ok(again.hintsPath);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('an old entry without any new metadata still lists, with the actions that need it unavailable and nothing made up', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-restore-'));
  try {
    const history = new RunHistory(root);
    const old = await history.add({ seedCode: 'DS1TR1-OLD-0001', profile: 'ds1tr-gen-v1', masterSeed: 'old', settings: structuredClone(DEFAULT_GENERATION_SETTINGS), packagePath: path.join(root, 'gone', 'seed_z'), manifestPath: path.join(root, 'gone', 'run-manifest.json'), spoilerPath: null });
    const [listed] = await history.list();
    assert.equal(listed.id, old.id);
    assert.equal(listed.packageExists, false);
    assert.equal(listed.hintsPath ?? null, null);
    assert.equal(await hintsForEntry(listed, path.join(root, 'runs'), '0.44.1'), null);
  } finally { await rm(root, { recursive: true, force: true }); }
});
