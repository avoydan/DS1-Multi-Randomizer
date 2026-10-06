import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { resolveFogPrerequisites } from '../../dist/adapters/fogPrerequisites.js';

const tmp = () => mkdtemp(path.join(os.tmpdir(), 'ds1tr-fogprereq-'));

async function fakeHost() {
  const dir = await tmp();
  const host = path.join(dir, 'FogHost.exe');
  await writeFile(host, 'not really an executable');
  return host;
}

async function fakeDataRoot() {
  const root = await tmp();
  await mkdir(path.join(root, 'dist', 'DS1R', 'event'), { recursive: true });
  await mkdir(path.join(root, 'dist', 'DS1R', 'map', 'MapStudio'), { recursive: true });
  await writeFile(path.join(root, 'dist', 'DS1R', 'event', 'common.emevd.dcx'), 'x');
  await writeFile(path.join(root, 'dist', 'DS1R', 'event', 'm10_00_00_00.emevd.dcx'), 'x');
  await writeFile(path.join(root, 'dist', 'DS1R', 'map', 'MapStudio', 'm10_00_00_00.msb'), 'x');
  return root;
}

test('without an override or a way to prepare the data nothing is resolved', async () => {
  assert.equal(await resolveFogPrerequisites('ENGLISH', {}), null);
});

test('normal use: the data root comes from the application preparation, nothing is configured by the person', async () => {
  const host = await fakeHost();
  const root = await fakeDataRoot();
  const seen = [];
  const result = await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_HOST: host }, {
    pristineDirectory: 'C:\\pristine',
    cacheRoot: 'C:\\cache',
    prepare: async (options) => { seen.push(options); return { ok: true, root, reused: true, downloaded: 0, derived: 0 }; },
  });
  assert.equal('failure' in result, false);
  assert.equal(result.dataRoot, path.resolve(root));
  assert.equal(result.executablePath, path.resolve(host));
  assert.ok(result.readSet.some((file) => file.endsWith('m10_00_00_00.msb')));
  assert.deepEqual(seen.map((o) => [o.pristineDirectory, o.cacheRoot]), [['C:\\pristine', 'C:\\cache']]);
});

test('a developer override (DS1TR_FOG_DATA_ROOT) wins and the application does not prepare anything', async () => {
  const host = await fakeHost();
  const root = await fakeDataRoot();
  const result = await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_HOST: host, DS1TR_FOG_DATA_ROOT: root }, {
    pristineDirectory: 'C:\\pristine',
    prepare: async () => { throw new Error('must not prepare'); },
  });
  assert.equal(result.dataRoot, path.resolve(root));
});

test('preparation failures keep the plain message for the person and the exact reason for diagnostics', async () => {
  const host = await fakeHost();
  const result = await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_HOST: host }, {
    pristineDirectory: 'C:\\pristine',
    prepare: async () => ({ ok: false, code: 'WORLD_DATA_OFFLINE', userMessage: 'Check your internet connection and click Retry.', technical: 'https://raw.githubusercontent.com/...: ENOTFOUND' }),
  });
  assert.deepEqual(result.failure, { code: 'WORLD_DATA_OFFLINE', userMessage: 'Check your internet connection and click Retry.', technical: 'https://raw.githubusercontent.com/...: ENOTFOUND' });
});

test('a missing packaged fog randomizer component is reported without developer terms', async () => {
  const result = await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_HOST: path.join(os.tmpdir(), 'no-such-host', 'FogHost.exe') }, {
    pristineDirectory: 'C:\\pristine',
    prepare: async () => { throw new Error('must not prepare without the component'); },
  });
  assert.equal(result.failure.code, 'FOG_HOST_MISSING');
  assert.doesNotMatch(result.failure.userMessage, /FogHost|exe|path|dotnet|\.NET/iu);
  assert.match(result.failure.technical, /FogHost executable not found/u);
});

test('packaged layout: FogHost is found next to the runtime root, with no dotnet CLI or PATH involved', () => {
  const runtime = path.join(os.tmpdir(), 'runtime-root');
  const script = `import('${pathToFileURL(path.resolve('dist/adapters/fogPrerequisites.js')).href}').then((m) => console.log(m.defaultFogHostExecutable))`;
  const run = spawnSync(process.execPath, ['-e', script], { encoding: 'utf8', env: { ...process.env, DS1TR_RUNTIME_ROOT: runtime, PATH: '' } });
  assert.equal(run.stdout.trim(), path.join(runtime, 'tools', 'fog-host', 'FogHost.exe'));
});

test('the shared configuration has no Fog data folder setting; the old desktop setting is gone from the renderer API', async () => {
  const contracts = await readFile(new URL('../../../../packages/contracts/src/index.ts', import.meta.url), 'utf8');
  assert.doesNotMatch(contracts, /fogDataRoot|dataRoot|dist\/DS1R/iu);
  const app = await readFile(new URL('../../../desktop/src/App.tsx', import.meta.url), 'utf8');
  assert.doesNotMatch(app, /FogMod data root|dist\/DS1R|selectFogDataRoot|fogDataRoot/u);
});
