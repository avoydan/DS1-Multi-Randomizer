import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import {
  compactRuntimeWorldDataId, compactRuntimeWorldDataManifest, hashWorldDataContent, prepareWorldData, verifyWorldDataRoot,
  worldDataBaseUrl, worldDataId, worldDataManifest, worldDataSourceCommit,
} from '../../dist/adapters/worldData.js';

const sha = (bytes) => createHash('sha256').update(bytes).digest('hex');
const dir = () => mkdtemp(path.join(os.tmpdir(), 'ds1tr-worlddata-'));

// A small synthetic set: two files derived from the game, three fetched from upstream (one of them text with CRLF).
const GAME_FILE_A = Buffer.from('map-a');
const GAME_FILE_B = Buffer.from('param-b');
const UPSTREAM = {
  'dist/DS1R/event/common.emevd.dcx': Buffer.from('event-common'),
  'dist/DS1R/script/talk/m10_00_00_00.talkesdbnd.dcx': Buffer.from('talk-10'),
  'dist/fog.txt': Buffer.from('line1\nline2\n'),
};
const manifest = [
  { path: 'dist/DS1R/map/MapStudio/m10_00_00_00.msb', sha256: sha(GAME_FILE_A), text: false, size: 5, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m10_00_00_00.msb' },
  { path: 'dist/DS1R/param/GameParam/GameParam.parambnd.dcx', sha256: sha(GAME_FILE_B), text: false, size: 7, kind: 'param', origin: 'game', gamePath: 'param/GameParam/GameParam.parambnd.dcx' },
  { path: 'dist/DS1R/event/common.emevd.dcx', sha256: sha(UPSTREAM['dist/DS1R/event/common.emevd.dcx']), text: false, size: 12, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m10_00_00_00.talkesdbnd.dcx', sha256: sha(UPSTREAM['dist/DS1R/script/talk/m10_00_00_00.talkesdbnd.dcx']), text: false, size: 7, kind: 'talk', origin: 'upstream' },
  { path: 'dist/fog.txt', sha256: sha(UPSTREAM['dist/fog.txt']), text: true, size: 12, kind: 'fog-annotations', origin: 'upstream' },
];

async function pristineWith(overrides = {}) {
  const root = await dir();
  const files = { 'map/MapStudio/m10_00_00_00.msb': GAME_FILE_A, 'param/GameParam/GameParam.parambnd.dcx': GAME_FILE_B, ...overrides };
  for (const [relative, content] of Object.entries(files)) {
    if (content === null) continue;
    await mkdir(path.dirname(path.join(root, relative)), { recursive: true });
    await writeFile(path.join(root, relative), content);
  }
  return root;
}

function server(overrides = {}) {
  const calls = [];
  const fetchBytes = async (url) => {
    calls.push(url);
    const relative = url.slice('https://example.test/fogmod/'.length);
    if (overrides[relative] instanceof Error) throw overrides[relative];
    return overrides[relative] ?? UPSTREAM[relative];
  };
  return { calls, fetchBytes };
}

const options = (cacheRoot, pristineDirectory, extra = {}) => ({
  cacheRoot, pristineDirectory, manifest, baseUrl: 'https://example.test/fogmod/', id: 'test-set', attempts: 2, allowUpstreamDownloads: true, ...extra,
});

test('the real manifest is the 59 files FogHost reads: 19 derived from the game, 40 from the pinned upstream commit', async () => {
  assert.equal(worldDataManifest.length, 59);
  assert.equal(new Set(worldDataManifest.map((entry) => entry.path)).size, 59);
  assert.equal(worldDataManifest.filter((entry) => entry.origin === 'game').length, 19);
  assert.equal(worldDataManifest.filter((entry) => entry.origin === 'upstream').length, 40);
  for (const entry of worldDataManifest.filter((e) => e.origin === 'game')) assert.ok(entry.kind === 'map' || entry.kind === 'param');
  for (const entry of worldDataManifest.filter((e) => e.origin === 'upstream')) assert.ok(['event', 'talk', 'layout', 'fog-annotations'].includes(entry.kind));
  assert.match(worldDataId, /^[0-9a-f]{16}$/u);
  assert.equal(worldDataBaseUrl, `https://raw.githubusercontent.com/thefifthmatt/FogMod/${worldDataSourceCommit}/`);
  assert.ok(worldDataBaseUrl.startsWith('https://'));
});

test('the manifest equals the hash manifest compiled into FogHost (tools/fog-host/src/DataRoot.cs)', async () => {
  const source = await readFile(new URL('../../../../tools/fog-host/src/DataRoot.cs', import.meta.url), 'utf8');
  const hostEntries = [...source.matchAll(/new\("([^"]+)", "([0-9a-f]{64})", (true|false)\)/gu)].map((m) => `${m[1]}|${m[2]}|${m[3]}`);
  assert.equal(hostEntries.length, 59);
  assert.deepEqual(worldDataManifest.map((entry) => `${entry.path}|${entry.sha256}|${entry.text}`).sort(), [...hostEntries].sort());
});

test('the Compact runtime has 58 offline inputs while v1/v2 retain the pinned 59-file manifest', () => {
  assert.equal(worldDataManifest.length, 59);
  assert.equal(worldDataManifest.filter((entry) => entry.origin === 'upstream').length, 40);
  assert.equal(compactRuntimeWorldDataManifest.length, 58);
  assert.equal(compactRuntimeWorldDataManifest.filter((entry) => entry.origin === 'game').length, 19);
  assert.equal(compactRuntimeWorldDataManifest.filter((entry) => entry.origin === 'reconstructed').length, 39);
  assert.equal(compactRuntimeWorldDataManifest.filter((entry) => entry.origin === 'upstream').length, 0);
  assert.ok(!compactRuntimeWorldDataManifest.some((entry) => entry.path === 'dist/fog.txt'));
  assert.match(compactRuntimeWorldDataId, /^[0-9a-f]{16}$/u);
});

test('text content is hashed after CRLF normalization, binary content as is', () => {
  assert.equal(hashWorldDataContent(Buffer.from('a\r\nb\r\n'), true), hashWorldDataContent(Buffer.from('a\nb\n'), true));
  assert.notEqual(hashWorldDataContent(Buffer.from('a\r\nb\r\n'), false), hashWorldDataContent(Buffer.from('a\nb\n'), false));
});

test('cache miss: game files are copied from the pristine snapshot, the rest downloaded over HTTPS and verified', async () => {
  const cache = await dir();
  const pristine = await pristineWith();
  const remote = server();
  const events = [];
  const result = await prepareWorldData(options(cache, pristine, { fetchBytes: remote.fetchBytes, onProgress: (p) => events.push(p) }));
  assert.equal(result.ok, true);
  assert.equal(result.reused, false);
  assert.equal(result.derived, 2);
  assert.equal(result.downloaded, 3);
  assert.equal(result.root, path.join(cache, 'test-set'));
  assert.deepEqual(remote.calls.sort(), Object.keys(UPSTREAM).map((p) => `https://example.test/fogmod/${p}`).sort(), 'only upstream files are fetched, never a game file');
  assert.deepEqual(await verifyWorldDataRoot(result.root, manifest), { ok: true, missing: [], altered: [], unexpected: [] });
  assert.ok(events.length > 2 && events.at(-1).fraction === 1 && events.every((e) => !/FogMod|dist|sha|\.NET/iu.test(e.message)), 'progress text is plain language');
  assert.deepEqual((await readdir(cache)).sort(), ['test-set'], 'no temporary folder is left behind');
});

test('offline distribution reuses only a complete legacy cache and refuses a cold or partial cache without repairing it', async () => {
  const cache = await dir();
  const pristine = await pristineWith();
  const remote = server();

  const missing = await prepareWorldData(options(cache, pristine, { allowUpstreamDownloads: false, fetchBytes: remote.fetchBytes }));
  assert.equal(missing.ok, false);
  assert.equal(missing.code, 'WORLD_DATA_LEGACY_UNAVAILABLE');
  assert.deepEqual(remote.calls, []);
  assert.deepEqual(await readdir(cache), [], 'a cold offline request does not create or repair a cache');

  const complete = await prepareWorldData(options(cache, pristine, { allowUpstreamDownloads: true, fetchBytes: remote.fetchBytes }));
  assert.equal(complete.ok, true);
  const reused = await prepareWorldData(options(cache, pristine, { allowUpstreamDownloads: false, fetchBytes: remote.fetchBytes }));
  assert.equal(reused.ok, true);
  assert.equal(reused.reused, true);
  assert.equal(reused.downloaded, 0);

  await writeFile(path.join(complete.root, 'dist', 'fog.txt'), 'corrupt');
  const callsBefore = remote.calls.length;
  const corrupted = await prepareWorldData(options(cache, pristine, { allowUpstreamDownloads: false, fetchBytes: remote.fetchBytes }));
  assert.equal(corrupted.ok, false);
  assert.equal(corrupted.code, 'WORLD_DATA_LEGACY_UNAVAILABLE');
  assert.equal(remote.calls.length, callsBefore);
  assert.equal(await readFile(path.join(complete.root, 'dist', 'fog.txt'), 'utf8'), 'corrupt', 'a Nexus request leaves the invalid legacy cache untouched');
});

test('reconstructed inputs are built locally and never sent through the downloader', async () => {
  const cache = await dir();
  const pristine = await dir();
  const content = Buffer.from('locally reconstructed input');
  const reconstructedManifest = [{
    path: 'dist/DS1R/event/common.emevd.dcx', sha256: sha(content), text: false, size: content.length,
    kind: 'event', origin: 'reconstructed',
  }];
  let reconstructionArgs;
  let fetchCalls = 0;
  const result = await prepareWorldData({
    cacheRoot: cache,
    pristineDirectory: pristine,
    manifest: reconstructedManifest,
    id: 'compact-local-test',
    fetchBytes: async () => { fetchCalls += 1; throw new Error('network access is forbidden in this test'); },
    reconstructFiles: async (inputRoot, outputRoot) => {
      reconstructionArgs = [inputRoot, outputRoot];
      const target = path.join(outputRoot, 'dist', 'DS1R', 'event', 'common.emevd.dcx');
      await mkdir(path.dirname(target), { recursive: true });
      await writeFile(target, content);
    },
  });
  assert.equal(result.ok, true);
  assert.equal(result.downloaded, 0);
  assert.equal(result.derived, 1);
  assert.equal(fetchCalls, 0);
  assert.equal(reconstructionArgs[0], pristine);
  assert.equal(path.dirname(reconstructionArgs[1]), cache);
  assert.match(path.basename(reconstructionArgs[1]), /^\.preparing-/u);
  assert.deepEqual(await verifyWorldDataRoot(result.root, reconstructedManifest), { ok: true, missing: [], altered: [], unexpected: [] });
});

test('cache hit: a verified cache is used without any network access or game read', async () => {
  const cache = await dir();
  const pristine = await pristineWith();
  assert.equal((await prepareWorldData(options(cache, pristine, { fetchBytes: server().fetchBytes }))).ok, true);
  const offline = server({ 'dist/fog.txt': new Error('offline') });
  const again = await prepareWorldData(options(cache, '/does/not/exist', { fetchBytes: offline.fetchBytes }));
  assert.equal(again.ok, true);
  assert.equal(again.reused, true);
  assert.equal(offline.calls.length, 0);
});

test('a corrupted cache is rebuilt: only the damaged or missing files are fetched again', async () => {
  const cache = await dir();
  const pristine = await pristineWith();
  const first = await prepareWorldData(options(cache, pristine, { fetchBytes: server().fetchBytes }));
  await writeFile(path.join(first.root, 'dist', 'DS1R', 'event', 'common.emevd.dcx'), 'tampered');
  await rm(path.join(first.root, 'dist', 'DS1R', 'param', 'GameParam', 'GameParam.parambnd.dcx'));
  await writeFile(path.join(first.root, 'dist', 'DS1R', 'event', 'extra.emevd.dcx'), 'unexpected');
  const remote = server();
  const rebuilt = await prepareWorldData(options(cache, pristine, { fetchBytes: remote.fetchBytes }));
  assert.equal(rebuilt.ok, true);
  assert.equal(rebuilt.reused, false);
  assert.deepEqual(remote.calls, ['https://example.test/fogmod/dist/DS1R/event/common.emevd.dcx']);
  assert.equal(rebuilt.derived, 1);
  assert.deepEqual(await verifyWorldDataRoot(rebuilt.root, manifest), { ok: true, missing: [], altered: [], unexpected: [] });
  assert.deepEqual((await readdir(cache)).sort(), ['test-set']);
});

test('a download whose hash does not match is retried, then refused; nothing is installed', async () => {
  const cache = await dir();
  const remote = server({ 'dist/DS1R/event/common.emevd.dcx': Buffer.from('not the expected content') });
  const result = await prepareWorldData(options(cache, await pristineWith(), { fetchBytes: remote.fetchBytes }));
  assert.equal(result.ok, false);
  assert.equal(result.code, 'WORLD_DATA_DOWNLOAD_CORRUPT');
  assert.match(result.technical, /dist\/DS1R\/event\/common\.emevd\.dcx.*does not match the expected/u);
  assert.equal(remote.calls.filter((url) => url.endsWith('common.emevd.dcx')).length, 2, 'bounded retries');
  assert.deepEqual(await readdir(cache), [], 'no cache folder and no temporary leftovers');
});

test('offline: the failure is plain language, the technical reason is kept for diagnostics, and a later online run succeeds', async () => {
  const cache = await dir();
  const pristine = await pristineWith();
  const failing = server({ 'dist/fog.txt': new Error('getaddrinfo ENOTFOUND raw.githubusercontent.com') });
  const failed = await prepareWorldData(options(cache, pristine, { fetchBytes: failing.fetchBytes }));
  assert.equal(failed.ok, false);
  assert.equal(failed.code, 'WORLD_DATA_OFFLINE');
  assert.match(failed.userMessage, /internet connection/u);
  assert.doesNotMatch(failed.userMessage, /ENOTFOUND|githubusercontent|sha256|dist\//u);
  assert.match(failed.technical, /ENOTFOUND/u);
  assert.deepEqual(await readdir(cache), []);
  assert.equal((await prepareWorldData(options(cache, pristine, { fetchBytes: server().fetchBytes }))).ok, true);
});

test('only HTTPS sources are accepted by the default downloader', async () => {
  const result = await prepareWorldData({ cacheRoot: await dir(), pristineDirectory: await pristineWith(), manifest, baseUrl: 'http://example.test/', id: 'http', attempts: 1, allowUpstreamDownloads: true });
  assert.equal(result.ok, false);
  assert.match(result.technical, /Only HTTPS/u);
});

test('a missing source game file and an unsupported game build are distinct, plain-language failures', async () => {
  const missing = await prepareWorldData(options(await dir(), await pristineWith({ 'param/GameParam/GameParam.parambnd.dcx': null }), { fetchBytes: server().fetchBytes }));
  assert.equal(missing.ok, false);
  assert.equal(missing.code, 'WORLD_DATA_GAME_FILES');
  assert.match(missing.userMessage, /Verify the game files in Steam/u);
  const other = await prepareWorldData(options(await dir(), await pristineWith({ 'map/MapStudio/m10_00_00_00.msb': Buffer.from('another build') }), { fetchBytes: server().fetchBytes }));
  assert.equal(other.ok, false);
  assert.equal(other.code, 'WORLD_DATA_GAME_VERSION');
  assert.match(other.userMessage, /not supported/u);
  assert.match(other.technical, /map\/MapStudio\/m10_00_00_00\.msb does not match/u);
});

test('concurrent requests for the same cache share one preparation', async () => {
  const cache = await dir();
  const pristine = await pristineWith();
  const remote = server();
  const [a, b] = await Promise.all([
    prepareWorldData(options(cache, pristine, { fetchBytes: remote.fetchBytes })),
    prepareWorldData(options(cache, pristine, { fetchBytes: remote.fetchBytes })),
  ]);
  assert.equal(a.ok && b.ok, true);
  assert.equal(remote.calls.length, 3);
});

test('the live installation is only read: preparation writes nothing outside the cache', async () => {
  const cache = await dir();
  const pristine = await pristineWith();
  const before = (await readdir(pristine, { recursive: true })).sort();
  assert.equal((await prepareWorldData(options(cache, pristine, { fetchBytes: server().fetchBytes }))).ok, true);
  assert.deepEqual((await readdir(pristine, { recursive: true })).sort(), before);
});
