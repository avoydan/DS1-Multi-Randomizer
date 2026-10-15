import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { copyFile, mkdir, mkdtemp, readFile, readdir, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { GameStateAuthority } from '../../dist/core/gameState.js';

const sha = (value) => createHash('sha256').update(value).digest('hex');

const vanilla = {
  'DarkSoulsRemastered.exe': 'exe-v1',
  'param/GameParam/GameParam.parambnd.dcx': 'param',
  'paramdef/paramdef.paramdefbnd.dcx': 'paramdef',
  'map/MapStudio/m10_00_00_00.msb': 'msb-a',
  'map/MapStudio/m12_00_00_00.msb': 'msb-b',
  'event/m10_00_00_00.emevd.dcx': 'event-a',
  'script/aiCommon.luabnd.dcx': 'lua',
  'sfx/FRPG_SfxBnd_CommonEffects.ffxbnd.dcx': 'ffx',
  'msg/ENGLISH/menu.msgbnd.dcx': 'menu',
};

async function writeTree(root, files) {
  for (const [relative, content] of Object.entries(files)) {
    const target = path.join(root, ...relative.split('/'));
    await mkdir(path.dirname(target), { recursive: true });
    await writeFile(target, content);
  }
}

async function snapshotTree(root, directory = root) {
  const result = {};
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const target = path.join(directory, entry.name);
    if (entry.isDirectory()) Object.assign(result, await snapshotTree(root, target));
    else result[path.relative(root, target).replaceAll('\\', '/')] = (await readFile(target)).toString('utf8');
  }
  return result;
}

async function fixture(fn, options = {}) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-state-'));
  const game = path.join(root, 'game');
  const state = path.join(root, 'state');
  await writeTree(game, vanilla);
  let running = false;
  const authority = new GameStateAuthority({
    stateRoot: state, isGameRunning: async () => running, ...options,
  });
  try {
    await fn({ root, game, state, authority, setRunning: (value) => { running = value; } });
  } finally { await rm(root, { recursive: true, force: true }); }
}

/** Writes a Fog-layout package (game tree at the package root). */
async function fogPackage(root, snapshotId, files, name = 'package') {
  const directory = path.join(root, name);
  await writeTree(directory, files);
  const manifest = {
    schemaVersion: 1, appVersion: 'test', game: 'DSR', masterSeed: 'seed',
    pristineSnapshotId: snapshotId, gameTreeRoot: '', options: {}, attempts: [],
    engines: {}, validation: [],
    files: Object.entries(files).map(([file, content]) => ({ path: file, size: content.length, sha256: sha(content) })),
  };
  await writeFile(path.join(directory, 'run-manifest.json'), JSON.stringify(manifest));
  return directory;
}

/** Writes a combined-only package (mod/ mirrors the game root). */
async function combinedPackage(root, snapshotId, changes, patchOverrides = {}) {
  const directory = path.join(root, 'combined');
  const files = { 'randomizer.json': '{}' };
  const entries = [];
  for (const [relative, content] of Object.entries(changes)) {
    files[`mod/${relative}`] = content;
    entries.push({ source: relative, output: `mod/${relative}`,
      sourceSha256: sha(vanilla[relative]), outputSha256: sha(content) });
  }
  const patch = { schemaVersion: 2, patchedMaps: entries.filter((e) => e.source.startsWith('map/')),
    luaBundles: entries.filter((e) => e.source.startsWith('script/')),
    events: entries.filter((e) => e.source.startsWith('event/')),
    ffxBundle: null, gameParam: entries.find((e) => e.source.startsWith('param/')) ?? null, ...patchOverrides };
  files['patch-manifest.json'] = JSON.stringify(patch);
  await writeTree(directory, files);
  const manifest = {
    masterSeed: 'seed', pristineSnapshotId: snapshotId, gameTreeRoot: 'mod',
    files: Object.entries(files).map(([file, content]) => ({ path: file, size: content.length, sha256: sha(content) })),
  };
  await writeFile(path.join(directory, 'run-manifest.json'), JSON.stringify(manifest));
  return directory;
}

async function imported(authority, game) {
  const result = await authority.importPristine(game);
  assert.equal(result.ok, true, JSON.stringify(result));
  return result.snapshot;
}

test('import snapshots managed data without touching the game and becomes the pristine source', async () => fixture(async ({ game, authority }) => {
  assert.equal((await authority.detectState(game)).state, 'no-snapshot');
  assert.equal(await authority.getPristineSource(game), null);
  const snapshot = await imported(authority, game);
  assert.deepEqual(await snapshotTree(game), vanilla);
  assert.equal(snapshot.files.length, Object.keys(vanilla).length - 1);
  // The snapshot is DsrDataTool's generation source and must cover its read set.
  for (const root of ['event', 'map/MapStudio', 'msg', 'param/GameParam', 'paramdef', 'script', 'sfx']) {
    assert.ok(snapshot.roots.includes(root), `snapshot roots must include ${root}`);
  }
  assert.ok(snapshot.files.some((file) => file.path === 'paramdef/paramdef.paramdefbnd.dcx'));
  assert.equal(snapshot.provenance.referenceHashesVerified, false);
  assert.equal((await authority.detectState(game)).state, 'vanilla');
  const source = await authority.getPristineSource(game);
  assert.equal(source.snapshotId, snapshot.snapshotId);
  assert.deepEqual(await snapshotTree(source.directory), vanilla);
  const again = await authority.importPristine(game);
  assert.equal(again.code, 'SNAPSHOT_EXISTS');
}));

test('import refuses installations with legacy backup chains', async () => fixture(async ({ game, authority }) => {
  await writeFile(path.join(game, 'map/MapStudio/m10_00_00_00.msb.bak'), 'legacy');
  const result = await authority.importPristine(game);
  assert.equal(result.code, 'LEGACY_ARTIFACTS_DETECTED');
  assert.deepEqual(result.problems, ['map/MapStudio/m10_00_00_00.msb.bak']);
  assert.equal((await authority.detectState(game)).state, 'no-snapshot');
}));

test('reference verification failure leaves no snapshot', async () => fixture(async ({ game, authority }) => {
  const result = await authority.importPristine(game);
  assert.equal(result.code, 'NOT_VANILLA');
  assert.equal(await authority.getPristineSource(game), null);
}, { verifyReferenceHashes: async () => ['m10 differs from vanilla'] }));

test('activates a Fog-layout package, detects it as active, and restores vanilla exactly', async () => fixture(async ({ root, game, authority }) => {
  const snapshot = await imported(authority, game);
  const pkg = await fogPackage(root, snapshot.snapshotId, {
    'map/MapStudio/m10_00_00_00.msb': 'fog-msb',
    'map/MapStudio/m12_00_00_00.msb': 'msb-b', // unchanged read-set copy is skipped
    'event/m10_00_00_00.emevd.dcx': 'fog-event',
  });
  const activation = await authority.activate(game, pkg);
  assert.equal(activation.ok, true, JSON.stringify(activation));
  assert.deepEqual(activation.journal.files.map((file) => file.path),
    ['event/m10_00_00_00.emevd.dcx', 'map/MapStudio/m10_00_00_00.msb']);
  const tree = await snapshotTree(game);
  assert.equal(tree['map/MapStudio/m10_00_00_00.msb'], 'fog-msb');
  assert.equal(tree['event/m10_00_00_00.emevd.dcx'], 'fog-event');
  const active = await authority.detectState(game);
  assert.equal(active.state, 'active');
  assert.equal(active.activePackage, path.resolve(pkg));

  const second = await authority.activate(game, pkg);
  assert.equal(second.code, 'GAME_STATE_NOT_VANILLA');

  const restored = await authority.restore(game);
  assert.equal(restored.ok, true, JSON.stringify(restored));
  assert.equal(restored.restored, 2);
  assert.deepEqual(await snapshotTree(game), vanilla);
  assert.equal((await authority.detectState(game)).state, 'vanilla');
  const noop = await authority.restore(game);
  assert.equal(noop.ok, true);
  assert.equal(noop.restored, 0);
}));

test('activates a combined package only when it agrees with patch-manifest.json', async () => fixture(async ({ root, game, authority }) => {
  const snapshot = await imported(authority, game);
  const pkg = await combinedPackage(root, snapshot.snapshotId, {
    'map/MapStudio/m10_00_00_00.msb': 'enemy-msb',
    'script/aiCommon.luabnd.dcx': 'enemy-lua',
    'param/GameParam/GameParam.parambnd.dcx': 'enemy-param',
  });
  const result = await authority.activate(game, pkg);
  assert.equal(result.ok, true, JSON.stringify(result));
  const tree = await snapshotTree(game);
  assert.equal(tree['script/aiCommon.luabnd.dcx'], 'enemy-lua');
  assert.equal(tree['randomizer.json'], undefined);
  assert.equal(tree['patch-manifest.json'], undefined);
  assert.equal((await authority.restore(game)).ok, true);
  assert.deepEqual(await snapshotTree(game), vanilla);
}));

test('refuses a combined package whose mod file is missing from patch-manifest.json', async () => fixture(async ({ root, game, authority }) => {
  const snapshot = await imported(authority, game);
  const pkg = await combinedPackage(root, snapshot.snapshotId, {
    'map/MapStudio/m10_00_00_00.msb': 'enemy-msb',
  }, { patchedMaps: [] });
  const result = await authority.activate(game, pkg);
  assert.equal(result.code, 'PACKAGE_TAMPERED');
  assert.deepEqual(await snapshotTree(game), vanilla);
}));

test('refuses tampered, foreign, unmanaged and unsafe packages without writing', async () => fixture(async ({ root, game, authority }) => {
  const snapshot = await imported(authority, game);
  const tampered = await fogPackage(root, snapshot.snapshotId, { 'map/MapStudio/m10_00_00_00.msb': 'fog' }, 'tampered');
  await writeFile(path.join(tampered, 'map/MapStudio/m10_00_00_00.msb'), 'changed after generation');
  assert.equal((await authority.activate(game, tampered)).code, 'PACKAGE_TAMPERED');

  const foreign = await fogPackage(root, 'other-snapshot', { 'map/MapStudio/m10_00_00_00.msb': 'fog' }, 'foreign');
  assert.equal((await authority.activate(game, foreign)).code, 'PACKAGE_SNAPSHOT_MISMATCH');

  const unmanaged = await fogPackage(root, snapshot.snapshotId, { 'dvdbnd0.bdt': 'archive' }, 'unmanaged');
  const unmanagedResult = await authority.activate(game, unmanaged);
  assert.equal(unmanagedResult.code, 'PACKAGE_TAMPERED');
  assert.match(unmanagedResult.problems[0], /outside the pristine snapshot/);

  const backup = await fogPackage(root, snapshot.snapshotId, { 'map/MapStudio/m10_00_00_00.msb.bak': 'x' }, 'backup');
  assert.equal((await authority.activate(game, backup)).code, 'PACKAGE_TAMPERED');

  const escaping = await fogPackage(root, snapshot.snapshotId, { 'map/MapStudio/m10_00_00_00.msb': 'fog' }, 'escaping');
  const manifest = JSON.parse(await readFile(path.join(escaping, 'run-manifest.json'), 'utf8'));
  manifest.files[0].path = '../game/DarkSoulsRemastered.exe';
  await writeFile(path.join(escaping, 'run-manifest.json'), JSON.stringify(manifest));
  assert.equal((await authority.activate(game, escaping)).code, 'PACKAGE_TAMPERED');

  const unchanged = await fogPackage(root, snapshot.snapshotId, { 'map/MapStudio/m10_00_00_00.msb': 'msb-a' }, 'unchanged');
  assert.equal((await authority.activate(game, unchanged)).code, 'PACKAGE_EMPTY');

  assert.deepEqual(await snapshotTree(game), vanilla);
  assert.equal((await authority.detectState(game)).state, 'vanilla');
}));

test('activates and restores a launcher-only package without changing any game file', async () => fixture(async ({ root, game, authority }) => {
  const snapshot = await imported(authority, game);
  const pkg = path.join(root, 'launcher-only-package');
  await mkdir(pkg, { recursive: true });
  await writeFile(path.join(pkg, 'run-manifest.json'), JSON.stringify({
    masterSeed: 'launcher-only', pristineSnapshotId: snapshot.snapshotId, gameTreeRoot: '', noRandomization: true, files: [],
  }));

  const activation = await authority.activate(game, pkg);
  assert.equal(activation.ok, true, JSON.stringify(activation));
  assert.deepEqual(activation.journal.files, []);
  assert.deepEqual(await snapshotTree(game), vanilla);
  assert.equal((await authority.detectState(game)).state, 'active');

  const restored = await authority.restore(game);
  assert.equal(restored.ok, true, JSON.stringify(restored));
  assert.equal(restored.restored, 0);
  assert.deepEqual(await snapshotTree(game), vanilla);
  assert.equal((await authority.detectState(game)).state, 'vanilla');
}));

test('refuses activation and restore while the game is running', async () => fixture(async ({ root, game, authority, setRunning }) => {
  const snapshot = await imported(authority, game);
  const pkg = await fogPackage(root, snapshot.snapshotId, { 'map/MapStudio/m10_00_00_00.msb': 'fog' });
  setRunning(true);
  assert.equal((await authority.activate(game, pkg)).code, 'GAME_RUNNING');
  assert.deepEqual(await snapshotTree(game), vanilla);
  setRunning(false);
  assert.equal((await authority.activate(game, pkg)).ok, true);
  setRunning(true);
  assert.equal((await authority.restore(game)).code, 'GAME_RUNNING');
  assert.equal((await authority.detectState(game)).state, 'active');
}));

test('a failed activation rolls back to vanilla', async () => {
  let copies = 0;
  let failAt = Infinity;
  await fixture(async ({ root, game, authority }) => {
    const snapshot = await imported(authority, game);
    const pkg = await fogPackage(root, snapshot.snapshotId, {
      'event/m10_00_00_00.emevd.dcx': 'fog-event',
      'map/MapStudio/m10_00_00_00.msb': 'fog-msb',
    });
    copies = 0;
    failAt = 2;
    const result = await authority.activate(game, pkg);
    failAt = Infinity;
    assert.equal(result.code, 'ACTIVATION_FAILED');
    assert.deepEqual(await snapshotTree(game), vanilla);
    assert.equal((await authority.detectState(game)).state, 'vanilla');
  }, { copyFile: async (from, to) => { if (++copies === failAt) throw new Error('disk full'); await copyFile(from, to); } });
});

test('an interrupted activation is detected and restore returns to vanilla', async () => fixture(async ({ root, game, authority }) => {
  const snapshot = await imported(authority, game);
  const pkg = await fogPackage(root, snapshot.snapshotId, {
    'event/m10_00_00_00.emevd.dcx': 'fog-event',
    'map/MapStudio/m10_00_00_00.msb': 'fog-msb',
  });
  assert.equal((await authority.activate(game, pkg)).ok, true);
  // Simulate a crash after the first write: journal still 'activating', a temp file left behind.
  const journalPath = path.join(authority.installDirectory(game), 'activation.json');
  const journal = JSON.parse(await readFile(journalPath, 'utf8'));
  journal.status = 'activating';
  await writeFile(journalPath, JSON.stringify(journal));
  await writeFile(path.join(game, 'map/MapStudio/m10_00_00_00.msb'), 'msb-a');
  await writeFile(path.join(game, 'map/MapStudio/m10_00_00_00.msb.ds1tr-tmp'), 'fog-m');
  const report = await authority.detectState(game);
  assert.equal(report.state, 'interrupted');
  assert.equal((await authority.activate(game, pkg)).code, 'GAME_STATE_NOT_VANILLA');
  const restored = await authority.restore(game);
  assert.equal(restored.ok, true, JSON.stringify(restored));
  assert.deepEqual(await snapshotTree(game), vanilla);
}));

test('unrecorded changes block restore unless overwriting is confirmed', async () => fixture(async ({ game, authority }) => {
  await imported(authority, game);
  await writeFile(path.join(game, 'event/m10_00_00_00.emevd.dcx'), 'other-mod');
  await writeFile(path.join(game, 'event/m10_00_00_00.emevd.dcx.bak'), 'event-a');
  const report = await authority.detectState(game);
  assert.equal(report.state, 'modified');
  assert.equal((await authority.restore(game)).code, 'GAME_STATE_UNKNOWN');
  assert.equal((await readFile(path.join(game, 'event/m10_00_00_00.emevd.dcx'), 'utf8')), 'other-mod');
  const forced = await authority.restore(game, { overwriteUnrecordedChanges: true });
  assert.equal(forced.ok, true, JSON.stringify(forced));
  assert.deepEqual(await snapshotTree(game), vanilla);
}));

test('a changed executable makes the snapshot stale and blocks every write', async () => fixture(async ({ root, game, authority }) => {
  const snapshot = await imported(authority, game);
  const pkg = await fogPackage(root, snapshot.snapshotId, { 'map/MapStudio/m10_00_00_00.msb': 'fog' });
  await writeFile(path.join(game, 'DarkSoulsRemastered.exe'), 'exe-v2');
  assert.equal((await authority.detectState(game)).state, 'version-changed');
  assert.equal((await authority.activate(game, pkg)).code, 'GAME_STATE_NOT_VANILLA');
  assert.equal((await authority.restore(game, { overwriteUnrecordedChanges: true })).code, 'SNAPSHOT_STALE');
  assert.equal((await readFile(path.join(game, 'map/MapStudio/m10_00_00_00.msb'), 'utf8')), 'msb-a');
}));

test('a damaged pristine copy is never used as a source or restore origin', async () => fixture(async ({ root, game, authority }) => {
  const snapshot = await imported(authority, game);
  const pristine = path.join(authority.installDirectory(game), 'pristine');
  await writeFile(path.join(pristine, 'map/MapStudio/m10_00_00_00.msb'), 'rot');
  assert.equal(await authority.getPristineSource(game), null);
  const pkg = await fogPackage(root, snapshot.snapshotId, { 'map/MapStudio/m10_00_00_00.msb': 'fog' });
  assert.equal((await authority.activate(game, pkg)).code, 'PRISTINE_COPY_DAMAGED');
  assert.deepEqual(await snapshotTree(game), vanilla);
}));

test('a live lock from another process blocks mutations; a stale lock is recovered', async () => fixture(async ({ game, authority }) => {
  const lock = path.join(authority.installDirectory(game), 'lock');
  await mkdir(path.dirname(lock), { recursive: true });
  await writeFile(lock, String(process.ppid));
  assert.equal((await authority.importPristine(game)).code, 'STATE_LOCKED');
  await writeFile(lock, '999999999');
  assert.equal((await authority.importPristine(game)).ok, true);
  await assert.rejects(stat(lock));
}));

test('the state root may not be inside the game installation', async () => fixture(async ({ game }) => {
  const authority = new GameStateAuthority({ stateRoot: path.join(game, 'state'), isGameRunning: async () => false });
  assert.equal((await authority.importPristine(game)).code, 'STATE_ROOT_UNSAFE');
}));
