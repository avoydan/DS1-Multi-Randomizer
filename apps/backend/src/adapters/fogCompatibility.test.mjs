import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import {
  buildFogCompatibility, decideFogRetry, fogReadSet, planFogStaging, validateFogCompatibility,
} from '../../dist/adapters/fogCompatibility.js';

const hash = (character) => character.repeat(64);

const manifest = {
  schemaVersion: 2,
  generatedAt: '2026-09-28T00:00:00Z',
  outputDirectory: 'C:/volatile/seed_x',
  changedSlots: 3,
  patchedMaps: [{
    mapId: 'm18_01_00_00', source: 'map/MapStudio/m18_01_00_00.msb',
    output: 'mod/map/MapStudio/m18_01_00_00.msb', changedSlots: 3,
    sourceSha256: hash('A'), outputSha256: hash('b'),
  }],
  luaBundles: [{ source: 'script/m18.luabnd.dcx', output: 'mod/script/m18.luabnd.dcx', sourceSha256: hash('c'), outputSha256: hash('d') }],
  events: [{ source: 'event/m10_00_00_00.emevd.dcx', output: 'mod/event/m10_00_00_00.emevd.dcx', sourceSha256: hash('e'), outputSha256: hash('f') }],
  ffxBundle: null,
  gameParam: { source: 'param/GameParam/GameParam.parambnd.dcx', output: 'mod/param/GameParam/GameParam.parambnd.dcx', sourceSha256: hash('1'), outputSha256: hash('2') },
};

// The combined engine's randomizer.json: placement lists live under `placements`.
const placements = { schemaVersion: 1, placements: {
  enemies: [
    { slot: 'b', scaledNpcParamId: 910002, sourceNpcParamId: 200000 },
    { slot: 'a', scaledNpcParamId: null, sourceNpcParamId: 200001 },
    { slot: 'c', scaledNpcParamId: 910001, sourceNpcParamId: 200002 },
  ],
  bosses: [{ slot: 'd', scaledNpcParamId: 910000, sourceNpcParamId: 223000 }],
  items: [],
} };

test('builds a sorted, volatile-free compatibility object from shared files only', () => {
  const result = buildFogCompatibility(manifest, placements, 'attempt/base');
  assert.equal(result.ok, true);
  assert.deepEqual(result.value, {
    baseDirectory: 'attempt/base',
    combinedFiles: [
      { path: 'event/m10_00_00_00.emevd.dcx', baseSha256: hash('e'), combinedSha256: hash('f') },
      { path: 'map/MapStudio/m18_01_00_00.msb', baseSha256: hash('a'), combinedSha256: hash('b') },
      { path: 'param/GameParam/GameParam.parambnd.dcx', baseSha256: hash('1'), combinedSha256: hash('2') },
    ],
    preservedNpcParams: [
      { id: 910000, baseId: 223000 }, { id: 910001, baseId: 200002 }, { id: 910002, baseId: 200000 },
    ],
  });
  assert.deepEqual(validateFogCompatibility(result.value), []);
  const volatile = { ...manifest, generatedAt: 'later', outputDirectory: 'D:/x' };
  assert.deepEqual(buildFogCompatibility(volatile, placements, 'attempt/base'), result);
});

test('rejects malformed manifests and conflicting placements', () => {
  const cases = [
    [{ ...manifest, schemaVersion: 1 }, placements],
    [{ ...manifest, events: [{ ...manifest.events[0], output: 'mod/event/other.emevd.dcx' }] }, placements],
    [{ ...manifest, events: [{ ...manifest.events[0], source: '../event/m10_00_00_00.emevd.dcx' }] }, placements],
    [{ ...manifest, events: [manifest.events[0], manifest.events[0]] }, placements],
    [manifest, { placements: { enemies: [
      { slot: 'x', scaledNpcParamId: 1, sourceNpcParamId: 2 },
      { slot: 'y', scaledNpcParamId: 1, sourceNpcParamId: 3 },
    ] } }],
    [manifest, { placements: { bosses: [{ slot: 'z', scaledNpcParamId: 1.5, sourceNpcParamId: 2 }] } }],
    // Lists at the top level (the layout the bridge used to read) must not silently yield nothing.
    [manifest, { enemies: [{ slot: 'x', scaledNpcParamId: 910000, sourceNpcParamId: 2 }] }],
    [manifest, { placements: [] }],
  ];
  for (const [candidate, candidatePlacements] of cases) {
    const result = buildFogCompatibility(candidate, candidatePlacements, 'attempt/base');
    assert.equal(result.ok, false);
    assert.equal(result.code, 'FOG_BRIDGE_INPUT_INVALID');
  }
});

test('lists the FogMod read set deterministically', () => {
  assert.deepEqual(fogReadSet(['m10_00_00_00.emevd.dcx', 'common.emevd.dcx'], ['m10_00_00_00.msb'], 'POLISH'), [
    'event/common.emevd.dcx',
    'event/m10_00_00_00.emevd.dcx',
    'map/MapStudio/m10_00_00_00.msb',
    'msg/POLISH/menu.msgbnd.dcx',
    'param/GameParam/GameParam.parambnd.dcx',
  ]);
});

test('plans staging from pristine data overlaid with combined output plus merge bases', () => {
  const root = path.resolve('fixture-root');
  const combinedFiles = buildFogCompatibility(manifest, placements, 'unused').value.combinedFiles;
  const attempt = path.join(root, 'workspace', 'fog', 'c0-f0');
  const result = planFogStaging({
    pristineDirectory: path.join(root, 'pristine'),
    packageDirectory: path.join(root, 'workspace', 'seed_x'),
    attemptDirectory: attempt,
    readSet: ['event/common.emevd.dcx', 'event/m10_00_00_00.emevd.dcx'],
    combinedFiles,
  });
  assert.equal(result.ok, true);
  const rel = (value) => path.relative(root, value).split(path.sep).join('/');
  assert.equal(result.value.stagingDirectory, path.join(attempt, 'game'));
  assert.equal(result.value.baseDirectory, path.join(attempt, 'base'));
  assert.deepEqual(result.value.directories.map(rel), ['workspace/fog/c0-f0/game/script/talk']);
  assert.deepEqual(result.value.copies.map(({ from, to }) => [rel(from), rel(to)]), [
    ['pristine/event/common.emevd.dcx', 'workspace/fog/c0-f0/game/event/common.emevd.dcx'],
    ['workspace/seed_x/mod/event/m10_00_00_00.emevd.dcx', 'workspace/fog/c0-f0/game/event/m10_00_00_00.emevd.dcx'],
    ['workspace/seed_x/mod/map/MapStudio/m18_01_00_00.msb', 'workspace/fog/c0-f0/game/map/MapStudio/m18_01_00_00.msb'],
    ['workspace/seed_x/mod/param/GameParam/GameParam.parambnd.dcx', 'workspace/fog/c0-f0/game/param/GameParam/GameParam.parambnd.dcx'],
    ['pristine/event/m10_00_00_00.emevd.dcx', 'workspace/fog/c0-f0/base/event/m10_00_00_00.emevd.dcx'],
    ['pristine/map/MapStudio/m18_01_00_00.msb', 'workspace/fog/c0-f0/base/map/MapStudio/m18_01_00_00.msb'],
    ['pristine/param/GameParam/GameParam.parambnd.dcx', 'workspace/fog/c0-f0/base/param/GameParam/GameParam.parambnd.dcx'],
  ]);
});

test('refuses staging that overlaps pristine data or the combined package', () => {
  const root = path.resolve('fixture-root');
  const pristineDirectory = path.join(root, 'pristine');
  const packageDirectory = path.join(root, 'seed_x');
  for (const attemptDirectory of [path.join(pristineDirectory, 'fog'), path.join(packageDirectory, 'fog'), root]) {
    const result = planFogStaging({ pristineDirectory, packageDirectory, attemptDirectory, readSet: [], combinedFiles: [] });
    assert.equal(result.ok, false);
  }
  const unsafe = planFogStaging({
    pristineDirectory, packageDirectory, attemptDirectory: path.join(root, 'fog'),
    readSet: ['../escape.msb'], combinedFiles: [],
  });
  assert.equal(unsafe.ok, false);
});

test('retries deterministically within bounds and never retries merge conflicts', () => {
  const bounds = { maxCombinedAttempts: 2, maxFogAttempts: 2 };
  const unsolvable = { code: 'FOG_SEED_UNSOLVABLE', message: 'Ran out of eligible edges' };
  const incompatible = { code: 'FOG_UPSTREAM_INCOMPATIBLE', message: 'Lordvessel not in item lots' };

  assert.deepEqual(decideFogRetry(unsolvable, { combinedAttempt: 0, fogAttempt: 0 }, bounds), {
    action: 'retry-fog', next: { combinedAttempt: 0, fogAttempt: 1 },
    reason: 'FOG_SEED_UNSOLVABLE: Ran out of eligible edges',
  });
  assert.equal(decideFogRetry(unsolvable, { combinedAttempt: 0, fogAttempt: 1 }, bounds).action, 'fail');
  assert.deepEqual(decideFogRetry(incompatible, { combinedAttempt: 0, fogAttempt: 1 }, bounds).next,
    { combinedAttempt: 1, fogAttempt: 0 });
  assert.equal(decideFogRetry(incompatible, { combinedAttempt: 1, fogAttempt: 0 }, bounds).action, 'fail');
  for (const code of ['FOG_MERGE_CONFLICT', 'FOG_COMPATIBILITY_VIOLATED', 'FOG_ENGINE_FAILED', 'FOG_HOST_CRASHED']) {
    assert.equal(decideFogRetry({ code, message: 'x' }, { combinedAttempt: 0, fogAttempt: 0 }).action, 'fail');
  }
});

test('a document without placements (Fog-only) has no preserved rows', () => {
  const result = buildFogCompatibility(manifest, {}, 'attempt/base');
  assert.equal(result.ok, true);
  assert.deepEqual(result.value.preservedNpcParams, []);
});

