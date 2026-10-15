import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { PROGRESSION_MODEL_FORMAT, loadProgressionModel } from '../../dist/adapters/progressionModel.js';
import { worldDataManifest, worldDataSourceCommit } from '../../dist/adapters/worldData.js';

// The progression model of the world data (Part H): exported once by FogHost, cached next to the data, validated against the pinned annotation.

const annotationSha256 = worldDataManifest.find((entry) => entry.path === 'dist/fog.txt').sha256;
const goodModel = () => ({
  format: PROGRESSION_MODEL_FORMAT, fogModCommit: worldDataSourceCommit, annotationSha256, start: 'a', areas: ['a'], edges: [],
  keys: [], lotAreas: {}, optionalAreas: [], prison: { start: 'a', mustReach: 'a', areas: ['a'] },
});

async function fixture(fn) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-model-'));
  const dataRoot = path.join(root, 'world-data', 'abc123');
  await mkdir(dataRoot, { recursive: true });
  try { await fn({ root, dataRoot, prerequisites: { executablePath: path.join(root, 'FogHost.exe'), dataRoot } }); }
  finally { await rm(root, { recursive: true, force: true }); }
}

const exporting = (model, calls) => async (executable, args) => {
  calls.push({ executable, args });
  await writeFile(args[args.indexOf('--out') + 1], JSON.stringify(model));
  return { code: 0, stderr: '' };
};

test('the model is exported once with an argument array (no shell), cached beside the data, and read from the cache afterwards', async () => fixture(async ({ prerequisites, dataRoot, root }) => {
  const calls = [];
  const first = await loadProgressionModel(prerequisites, exporting(goodModel(), calls));
  assert.equal(first.ok, true);
  assert.equal(first.fromCache, false);
  assert.deepEqual(calls[0].args.slice(0, 3), ['progression-model', '--data-root', dataRoot]);
  assert.equal(calls[0].executable, prerequisites.executablePath);
  const second = await loadProgressionModel(prerequisites, async () => { throw new Error('the cache must be used'); });
  assert.equal(second.ok, true);
  assert.equal(second.fromCache, true);
  assert.deepEqual(second.model, first.model);
  const leftovers = (await readdir(path.join(root, 'world-data'))).filter((name) => name.endsWith('.tmp'));
  assert.deepEqual(leftovers, [], 'no temporary file remains');
}));

test('legacy profile progression graphs use the current read-only exporter while Fog generation keeps its pinned legacy host', async () => fixture(async ({ prerequisites, dataRoot, root }) => {
  const currentExporter = path.join(root, 'FogHost-0.6.0.exe');
  const calls = [];
  const result = await loadProgressionModel({ ...prerequisites, progressionModelExecutablePath: currentExporter }, exporting(goodModel(), calls));
  assert.equal(result.ok, true);
  assert.equal(calls[0].executable, currentExporter);
  assert.notEqual(calls[0].executable, prerequisites.executablePath);
  assert.equal(calls[0].args[2], dataRoot);
}));

test('a cached model of another annotation or commit is not used: it is exported again', async () => fixture(async ({ prerequisites, dataRoot }) => {
  const cacheFile = path.join(path.dirname(dataRoot), `${path.basename(dataRoot)}.progression-model.json`);
  await writeFile(cacheFile, JSON.stringify({ ...goodModel(), annotationSha256: 'f'.repeat(64) }));
  const calls = [];
  const result = await loadProgressionModel(prerequisites, exporting(goodModel(), calls));
  assert.equal(result.ok, true);
  assert.equal(result.fromCache, false);
  assert.equal(calls.length, 1);
  assert.equal(JSON.parse(await readFile(cacheFile, 'utf8')).annotationSha256, annotationSha256, 'the cache is replaced by the valid model');
}));

test('Compact profile builds and caches the progression model from the verified catalog fingerprint', async () => fixture(async ({ prerequisites, dataRoot, root }) => {
  const fingerprint = 'a'.repeat(64);
  const compact = {
    ...prerequisites,
    annotationSource: 'compact-v1',
    pristineGameDirectory: path.join(root, 'pristine'),
    compactProjection: { catalogFingerprint: fingerprint },
  };
  const calls = [];
  const model = { ...goodModel(), annotationSha256: fingerprint };
  const first = await loadProgressionModel(compact, exporting(model, calls));
  assert.equal(first.ok, true);
  assert.equal(first.fromCache, false);
  assert.deepEqual(calls[0].args, [
    'progression-model', '--data-root', dataRoot,
    '--annotation-source', 'compact-v1',
    '--pristine-root', compact.pristineGameDirectory,
    '--expected-annotation-fingerprint', fingerprint,
    '--out', calls[0].args.at(-1),
  ]);
  const cacheFile = path.join(path.dirname(dataRoot), `${path.basename(dataRoot)}.compact-v1.progression-model.json`);
  assert.equal(JSON.parse(await readFile(cacheFile, 'utf8')).annotationSha256, fingerprint);
  const second = await loadProgressionModel(compact, async () => { throw new Error('the Compact cache must be used'); });
  assert.equal(second.ok, true);
  assert.equal(second.fromCache, true);
}));

test('a failing host, an invalid export and unreadable output are typed failures with the reason kept for the log', async () => fixture(async ({ prerequisites }) => {
  const host = await loadProgressionModel(prerequisites, async () => ({ code: 3, stderr: 'annotation mismatch' }));
  assert.equal(host.ok, false);
  assert.equal(host.code, 'KEY_PLACEMENT_MODEL_UNAVAILABLE');
  assert.match(host.technical, /exited with 3: annotation mismatch/u);
  assert.doesNotMatch(host.message, /FogHost|annotation|exit/u, 'the user message is plain language');
  const foreign = await loadProgressionModel(prerequisites, exporting({ ...goodModel(), format: 'other' }, []));
  assert.equal(foreign.ok, false);
  assert.match(foreign.technical, /does not match the pinned world data/u);
  const wrongCommit = await loadProgressionModel(prerequisites, exporting({ ...goodModel(), fogModCommit: '0'.repeat(40) }, []));
  assert.equal(wrongCommit.ok, false);
  const garbage = await loadProgressionModel(prerequisites, async (_executable, args) => { await writeFile(args[args.indexOf('--out') + 1], '{'); return { code: 0, stderr: '' }; });
  assert.equal(garbage.ok, false);
}));
