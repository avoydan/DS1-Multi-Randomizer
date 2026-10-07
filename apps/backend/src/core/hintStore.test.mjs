import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { DEFAULT_GENERATION_SETTINGS } from '@ds1tr/contracts';
import { buildHintDocument, findPlacementsFile, hintsAvailableFor, hintsForEntry, locateRunFolder, persistHints, readHintDocument } from '../../dist/core/hintStore.js';

// Hint data persistence (docs/HINT_SHEET.md): written once at generation next to the run, read by the viewer; an old entry is rebuilt only from placements
// that really exist; nothing is invented.
const placement = (name, overrides = {}) => ({
  itemId: 1, itemName: name, equipType: 3, sourcePool: 'world', targetPool: 'world', progression: false, from: 'Estus Flask',
  originalLocation: { area: 'Depths', map: 'm10_00_00_00', itemLot: 10 }, randomizedLocation: { area: 'Firelink Shrine', map: 'm10_02_00_00', itemLot: 20 }, ...overrides,
});
const placementsDocument = { placements: { items: [placement('Basement Key', { progression: true, keyName: 'basement_key' }), placement('Humanity')], gifts: [], shops: [], enemyDrops: [], bossRewards: [] } };
const source = { seedCode: 'DS1TR1-AAAA-BBBB', profile: 'ds1tr-gen-v1', createdAt: '2026-10-07T10:00:00.000Z', appVersion: '0.43.0', settings: structuredClone(DEFAULT_GENERATION_SETTINGS) };

async function runFolder(root, { withPlacements = true } = {}) {
  const folder = await mkdtemp(path.join(root, 'run-'));
  const seed = path.join(folder, 'combined-0', 'seed_x');
  await mkdir(seed, { recursive: true });
  if (withPlacements) await writeFile(path.join(seed, 'randomizer.json'), JSON.stringify(placementsDocument));
  return { folder, seed };
}

test('the hint data is built from the placements document, with the clean-game item at the destination', () => {
  const document = buildHintDocument(source, placementsDocument);
  assert.equal(document.format, 'ds1tr-hint-data');
  assert.equal(document.schemaVersion, 1);
  assert.equal(document.seedCode, source.seedCode);
  assert.equal(document.records.length, 2);
  assert.equal(document.records[0].placedAt, 'Estus Flask');
  assert.equal(document.records[0].importance, 'key');
  assert.equal(buildHintDocument(source, {}), null);
  assert.equal(buildHintDocument(source, null), null);
});

test('persistHints writes hints.json next to the run (not into the package) and it reads back identically', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-hints-'));
  try {
    const { folder, seed } = await runFolder(root);
    const written = await persistHints(folder, source);
    assert.equal(written, path.join(folder, 'hints.json'));
    assert.ok(!(await readFile(path.join(seed, 'randomizer.json'), 'utf8')).includes('ds1tr-hint-data'), 'the package side is untouched');
    const read = await readHintDocument(written);
    assert.deepEqual(read, buildHintDocument(source, placementsDocument));
    assert.equal(await persistHints(path.join(root, 'nowhere'), source), null, 'a run without a placements document writes nothing');
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('the newest combined attempt wins, and a damaged or foreign hints file is not trusted', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-hints-'));
  try {
    const { folder } = await runFolder(root);
    const second = path.join(folder, 'combined-3', 'seed_y');
    await mkdir(second, { recursive: true });
    await writeFile(path.join(second, 'randomizer.json'), JSON.stringify(placementsDocument));
    assert.equal(await findPlacementsFile(folder), path.join(second, 'randomizer.json'));
    await writeFile(path.join(folder, 'hints.json'), '{ not json');
    assert.equal(await readHintDocument(path.join(folder, 'hints.json')), null);
    await writeFile(path.join(folder, 'hints.json'), JSON.stringify({ format: 'something-else', schemaVersion: 1, records: [], seedCode: 'x' }));
    assert.equal(await readHintDocument(path.join(folder, 'hints.json')), null);
    await writeFile(path.join(folder, 'hints.json'), JSON.stringify({ ...buildHintDocument(source, placementsDocument), schemaVersion: 99 }));
    assert.equal(await readHintDocument(path.join(folder, 'hints.json')), null, 'a newer schema is not guessed at');
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('an old history entry (no hintsPath) is rebuilt from the placements the run folder still holds; without them the hint sheet is unavailable', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-hints-'));
  try {
    const { seed } = await runFolder(root);
    const entry = { id: 'a', createdAt: '2026-01-01T00:00:00.000Z', seedCode: 'DS1TR1-OLD', profile: 'ds1tr-gen-v1', masterSeed: 'm', settings: source.settings, status: 'generated', packagePath: seed, manifestPath: path.join(seed, 'run-manifest.json'), spoilerPath: null };
    assert.equal(await hintsAvailableFor(entry, root), true);
    const document = await hintsForEntry(entry, root, '0.43.0');
    assert.equal(document.seedCode, 'DS1TR1-OLD');
    assert.equal(document.createdAt, '2026-01-01T00:00:00.000Z', 'the entry\'s own time, not now');
    assert.equal(document.records.length, 2);

    const gone = { ...entry, packagePath: path.join(root, 'cleaned-up', 'seed_z') };
    assert.equal(await hintsAvailableFor(gone, root), false);
    assert.equal(await hintsForEntry(gone, root, '0.43.0'), null, 'nothing is made up');

    const { seed: noPlacements } = await runFolder(root, { withPlacements: false });
    assert.equal(await hintsForEntry({ ...entry, packagePath: noPlacements }, root, '0.43.0'), null, 'a run without placements has no hint sheet');
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('the run folder is only searched inside the run root', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-hints-'));
  try {
    const { seed } = await runFolder(root);
    const elsewhere = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-elsewhere-'));
    await mkdir(path.join(elsewhere, 'combined-0', 'seed_x'), { recursive: true });
    assert.equal(await locateRunFolder(path.join(elsewhere, 'combined-0', 'seed_x'), root), null);
    assert.equal(await locateRunFolder(path.join(root, '..', path.basename(elsewhere)), root), null);
    assert.ok(await locateRunFolder(seed, root));
    await rm(elsewhere, { recursive: true, force: true });
  } finally { await rm(root, { recursive: true, force: true }); }
});
