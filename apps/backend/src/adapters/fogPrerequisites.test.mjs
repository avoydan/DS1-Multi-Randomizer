import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { defaultFogHostExecutable, resolveFogPrerequisites, worldDataOptionsForProfile } from '../../dist/adapters/fogPrerequisites.js';
import { compactRuntimeWorldDataId, compactRuntimeWorldDataManifest } from '../../dist/adapters/worldData.js';
import { resolveGenerationProfile } from '../../dist/core/generationProfiles.js';

const tmp = () => mkdtemp(path.join(os.tmpdir(), 'ds1tr-fogprereq-'));
const legacyRuntime = { hostVersion: '0.5.0', schemaVersion: 2, annotationSource: 'legacy-fogmod' };
const compactRuntime = { hostVersion: '0.6.0', schemaVersion: 3, annotationSource: 'compact-v1' };
const legacyContext = (context) => ({ ...context, fogRuntime: legacyRuntime });

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
  const result = await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_HOST_LEGACY: host }, {
    ...legacyContext({ pristineDirectory: 'C:\\pristine' }),
    cacheRoot: 'C:\\cache',
    prepare: async (options) => { seen.push(options); return { ok: true, root, reused: true, downloaded: 0, derived: 0 }; },
  });
  assert.equal('failure' in result, false);
  assert.equal(result.dataRoot, path.resolve(root));
  assert.equal(result.executablePath, path.resolve(host));
  assert.equal(result.progressionModelExecutablePath, path.resolve(defaultFogHostExecutable),
    'a v1/v2 generation uses the current host only for read-only progression-model export');
  assert.ok(result.readSet.some((file) => file.endsWith('m10_00_00_00.msb')));
  assert.deepEqual(seen.map((o) => [o.pristineDirectory, o.cacheRoot]), [['C:\\pristine', 'C:\\cache']]);
  assert.equal(seen[0].allowUpstreamDownloads, false, 'an absent distribution does not grant upstream access');
});

test('only the explicit GitHub distribution enables upstream legacy-data downloads', async () => {
  const host = await fakeHost();
  const root = await fakeDataRoot();
  for (const [distribution, expected] of [['github', true], ['nexus', false], ['invalid', false]]) {
    const seen = [];
    const result = await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_HOST_LEGACY: host, DS1TR_DISTRIBUTION: distribution }, {
      ...legacyContext({ pristineDirectory: 'C:\\pristine' }),
      prepare: async (options) => { seen.push(options); return { ok: true, root, reused: true, downloaded: 0, derived: 0 }; },
    });
    assert.equal('failure' in result, false);
    assert.equal(seen[0].allowUpstreamDownloads, expected, `${distribution} distribution policy`);
  }
});

test('world-data preflight selects the Seed Code profile and defaults new runs to the current compact profile', () => {
  const current = resolveGenerationProfile('ds1tr-gen-v3');
  const legacy = resolveGenerationProfile('ds1tr-gen-v1');
  assert.ok(current);
  assert.ok(legacy);
  const nexusEnv = { DS1TR_DISTRIBUTION: 'nexus', DS1TR_FOG_HOST: 'C:\\FogHost.exe', DS1TR_FOG_HOST_LEGACY: 'C:\\FogHost-0.5.0.exe' };
  const compact = worldDataOptionsForProfile(current, 'C:\\Pristine', nexusEnv, 'C:\\Cache');
  assert.equal(compact.id, compactRuntimeWorldDataId);
  assert.equal(compact.manifest, compactRuntimeWorldDataManifest);
  assert.equal(typeof compact.reconstructFiles, 'function');
  assert.equal(compact.allowUpstreamDownloads, false);
  const v1 = worldDataOptionsForProfile(legacy, 'C:\\Pristine', nexusEnv, 'C:\\Cache');
  assert.equal(v1.id, legacy.fogEngine.worldDataId);
  assert.equal(v1.manifest, undefined);
  assert.equal(v1.allowUpstreamDownloads, false);
  const github = worldDataOptionsForProfile(legacy, 'C:\\Pristine', { ...nexusEnv, DS1TR_DISTRIBUTION: 'github' }, 'C:\\Cache');
  assert.equal(github.allowUpstreamDownloads, true);
});

test('a developer override (DS1TR_FOG_DATA_ROOT) wins and the application does not prepare anything', async () => {
  const host = await fakeHost();
  const root = await fakeDataRoot();
  const result = await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_HOST_LEGACY: host, DS1TR_FOG_DATA_ROOT: root }, {
    ...legacyContext({ pristineDirectory: 'C:\\pristine' }),
    prepare: async () => { throw new Error('must not prepare'); },
  });
  assert.equal(result.dataRoot, path.resolve(root));
});

test('preparation failures keep the plain message for the person and the exact reason for diagnostics', async () => {
  const host = await fakeHost();
  const result = await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_HOST_LEGACY: host }, {
    ...legacyContext({ pristineDirectory: 'C:\\pristine' }),
    prepare: async () => ({ ok: false, code: 'WORLD_DATA_OFFLINE', userMessage: 'Check your internet connection and click Retry.', technical: 'https://raw.githubusercontent.com/...: ENOTFOUND' }),
  });
  assert.deepEqual(result.failure, { code: 'WORLD_DATA_OFFLINE', userMessage: 'Check your internet connection and click Retry.', technical: 'https://raw.githubusercontent.com/...: ENOTFOUND' });
});

test('missing packaged legacy and current hosts fail closed without cross-version fallback', async () => {
  const missing = path.join(os.tmpdir(), 'no-such-host', 'FogHost.exe');
  const legacy = await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_HOST_LEGACY: missing, DS1TR_FOG_HOST: await fakeHost() }, {
    ...legacyContext({ pristineDirectory: 'C:\\pristine' }),
    prepare: async () => { throw new Error('must not prepare without the component'); },
  });
  const compact = await resolveFogPrerequisites('ENGLISH', { DS1TR_FOG_HOST: missing, DS1TR_FOG_HOST_LEGACY: await fakeHost() }, {
    pristineDirectory: 'C:\\pristine', fogRuntime: compactRuntime,
    prepare: async () => { throw new Error('must not prepare without the component'); },
  });
  for (const result of [legacy, compact]) {
    assert.equal(result.failure.code, 'FOG_HOST_MISSING');
    assert.doesNotMatch(result.failure.userMessage, /FogHost|exe|path|dotnet|\.NET/iu);
    assert.match(result.failure.technical, /FogHost executable not found/u);
  }
});

test('packaged layout: both profile-selected hosts are found next to the runtime root', () => {
  const runtime = path.join(os.tmpdir(), 'runtime-root');
  const modulePath = fileURLToPath(new URL('../../dist/adapters/fogPrerequisites.js', import.meta.url));
  const script = `import('${pathToFileURL(modulePath).href}').then((m) => console.log(JSON.stringify([m.defaultFogHostExecutable,m.defaultLegacyFogHostExecutable])))`;
  const run = spawnSync(process.execPath, ['-e', script], { encoding: 'utf8', env: { ...process.env, DS1TR_RUNTIME_ROOT: runtime, PATH: '' } });
  assert.deepEqual(JSON.parse(run.stdout.trim()), [
    path.join(runtime, 'tools', 'fog-host', 'FogHost.exe'),
    path.join(runtime, 'tools', 'fog-host-legacy', 'FogHost.exe'),
  ]);
});

test('the shared configuration has no Fog data folder setting; the old desktop setting is gone from the renderer API', async () => {
  const contracts = await readFile(new URL('../../../../packages/contracts/src/index.ts', import.meta.url), 'utf8');
  assert.doesNotMatch(contracts, /fogDataRoot|dataRoot|dist\/DS1R/iu);
  const app = await readFile(new URL('../../../desktop/src/App.tsx', import.meta.url), 'utf8');
  assert.doesNotMatch(app, /FogMod data root|dist\/DS1R|selectFogDataRoot|fogDataRoot/u);
});
