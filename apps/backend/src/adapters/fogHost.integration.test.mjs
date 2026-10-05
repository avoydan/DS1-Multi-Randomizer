import assert from 'node:assert/strict';
import { access, cp, mkdir, mkdtemp, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { FogHostProcess } from '../../dist/adapters/fogHostProcess.js';
import { defaultFogHostExecutable, resolveFogPrerequisites } from '../../dist/adapters/fogPrerequisites.js';

// These tests drive the real FogHost build (tools/fog-host). They are skipped when it is not built.
// The success case also needs DS1TR_FOG_DATA_ROOT; it never touches a game installation.
const hostBuilt = await access(defaultFogHostExecutable).then(() => true, () => false);
const dataRoot = process.env.DS1TR_FOG_DATA_ROOT;

const options = {
  traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: false, minorPvpFogs: false,
  lordvesselGates: false, randomStart: false, disconnectedFogs: false, noFallBedOfChaos: false,
  scaling: true, glitchedLogic: false, requireLordSouls: true, pacifistMode: false,
};

async function temp(fn) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-foghost-'));
  try { await fn(root); } finally { await rm(root, { recursive: true, force: true }); }
}

function execution(root, fogDataRoot, mode) {
  return {
    executablePath: defaultFogHostExecutable,
    requestPath: path.join(root, 'request.json'),
    responsePath: path.join(root, 'response.json'),
    timeoutMs: 120_000,
    request: {
      schemaVersion: 2, game: 'DS1R', mode, seed: 3141592653, options, language: 'ENGLISH',
      fogDataRoot, stagingDirectory: path.join(root, 'game'), logPath: path.join(root, 'foghost.log'),
      compatibility: { baseDirectory: path.join(root, 'base'), combinedFiles: [], preservedNpcParams: [] },
    },
  };
}

test('prerequisites (developer override) pass the message language into the read set', async () => temp(async (root) => {
  assert.equal(await resolveFogPrerequisites('ENGLISH', {}), null);
  assert.equal((await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_DATA_ROOT: root, DS1TR_FOG_HOST: path.join(root, 'missing.exe') })).failure.code, 'FOG_HOST_MISSING');
  const host = path.join(root, 'FogHost.exe');
  await writeFile(host, 'host');
  await mkdir(path.join(root, 'dist', 'DS1R', 'event'), { recursive: true });
  await mkdir(path.join(root, 'dist', 'DS1R', 'map', 'MapStudio'), { recursive: true });
  await writeFile(path.join(root, 'dist', 'DS1R', 'event', 'common.emevd.dcx'), 'x');
  await writeFile(path.join(root, 'dist', 'DS1R', 'map', 'MapStudio', 'm10_00_00_00.msb'), 'x');
  const ready = await resolveFogPrerequisites('POLISH', { DS1TR_FOG_DATA_ROOT: root, DS1TR_FOG_HOST: host });
  assert.deepEqual(ready.readSet, ['event/common.emevd.dcx', 'map/MapStudio/m10_00_00_00.msb',
    'msg/POLISH/menu.msgbnd.dcx', 'param/GameParam/GameParam.parambnd.dcx']);
}));

test('backend adapter invokes the built FogHost and parses a typed data-root failure', { skip: !hostBuilt && 'FogHost is not built' }, async () => temp(async (root) => {
  await mkdir(path.join(root, 'game'));
  await mkdir(path.join(root, 'empty-fog-data'));
  const response = await new FogHostProcess().execute(execution(root, path.join(root, 'empty-fog-data'), 'dryRun'));
  assert.equal(response.ok, false);
  assert.equal(response.error.code, 'FOG_DATA_MISSING');
  assert.match(response.error.message, /6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f/);
}));

test('backend adapter runs a FogHost dry run with streamed progress', { skip: (!hostBuilt && 'FogHost is not built') || (!dataRoot && 'DS1TR_FOG_DATA_ROOT not set') }, async () => temp(async (root) => {
  // Stand-in staging from the data root's own DS1R files (not a game installation).
  for (const directory of ['event', 'map', 'param', 'msg', 'script']) {
    await cp(path.join(dataRoot, 'dist', 'DS1R', directory), path.join(root, 'game', directory), { recursive: true });
  }
  await mkdir(path.join(root, 'base'));
  const before = await readdir(path.join(root, 'game'), { recursive: true });
  const stages = [];
  const response = await new FogHostProcess().execute(execution(root, dataRoot, 'dryRun'),
    (event) => { if (event.type === 'progress') stages.push(event.stage); });
  assert.equal(response.ok, true, JSON.stringify(response));
  assert.equal(response.fogModCommit, '6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f');
  assert.deepEqual(response.files, []);
  assert.deepEqual(stages, ['validate-request', 'load-annotations', 'read-key-items', 'solve-graph', 'write-game-data']);
  assert.deepEqual(await readdir(path.join(root, 'game'), { recursive: true }), before);
}));
