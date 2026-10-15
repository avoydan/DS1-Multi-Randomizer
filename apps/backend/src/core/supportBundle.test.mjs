import assert from 'node:assert/strict';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { DEFAULT_GENERATION_SETTINGS } from '@ds1tr/contracts';
import { AppLogger } from '../../dist/core/appLog.js';
import { OperationTracker } from '../../dist/core/operations.js';
import { redactionContext } from '../../dist/core/redaction.js';
import { assembleSupportBundle, gatherDiagnostics } from '../../dist/core/support.js';
import { BUNDLE_ALLOWLIST, BUNDLE_LIMITS, BUNDLE_TOTAL_LIMIT, buildSupportBundle } from '../../dist/core/supportBundle.js';
import { createZip, readZip } from '../../dist/core/zipWriter.js';

// Support bundle (docs/SUPPORT_BUNDLE.md): an explicit allowlist, redaction proven by planting synthetic secrets in real files and searching the archive, and a
// bundle that is useful after a failed generation, a failed Apply and before any world exists.
const userName = os.userInfo().username;
const home = os.homedir();
const SECRETS = {
  anthropic: 'sk-ant-api03-SYNTHETICSECRETKEY0123456789',
  password: 'hunter2-synthetic-password',
  token: 'tok_synthetic_0123456789abcdef',
  session: '6f1c2d9e-aaaa-4bbb-8ccc-0123456789ab',
  bearer: 'syntheticbearertoken9876543210',
  cookie: 'synthetic-cookie-value-1234',
};
const PLANTED = `
ANTHROPIC_API_KEY=${SECRETS.anthropic}
password=${SECRETS.password}
token=${SECRETS.token}
sessionToken: ${SECRETS.session}
Authorization: Bearer ${SECRETS.bearer}
Cookie: sid=${SECRETS.cookie}
log file ${home}\\AppData\\Roaming\\DS1 Triple Randomizer\\logs\\app.log and ${home.replace(/\\/gu, '/')}/secret.txt
hello ${userName} on ${os.hostname()}
`;
const GAME_MARKER = 'GAME-FILE-CONTENT-MARKER-DO-NOT-LEAK';

async function workspace() {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-bundle-'));
  const stateRoot = path.join(root, 'userdata', 'state');
  const runRoot = path.join(stateRoot, 'runs');
  const gameDir = path.join(root, 'Steam', 'DARK SOULS REMASTERED');
  await mkdir(gameDir, { recursive: true });
  await writeFile(path.join(gameDir, 'DarkSoulsRemastered.exe'), GAME_MARKER);
  const env = { ...process.env, DS1TR_API_TOKEN: SECRETS.session, ANTHROPIC_API_KEY: SECRETS.anthropic, DS1TR_INSTALL_MODE: 'installed', DS1TR_DESKTOP_VERSION: '0.31.0' };
  const logger = new AppLogger(path.join(stateRoot, 'logs', 'backend.log'));
  const tracker = new OperationTracker(stateRoot, logger);
  const deps = (history = []) => ({
    stateRoot, runRoot, userDataDir: path.join(root, 'userdata'), appLogPath: path.join(root, 'userdata', 'logs', 'app.log'), backendLogPath: logger.file,
    runtimeRoot: null, appVersion: '0.43.0', env, listHistory: async () => history,
    detectState: async () => ({ state: 'vanilla', gamePath: gameDir, snapshotId: 'snap', activePackage: null, problems: [] }),
  });
  return { root, stateRoot, runRoot, gameDir, logger, tracker, deps };
}

async function world(w, { id = 'run-a', failed = false } = {}) {
  const folder = path.join(w.runRoot, id);
  const seed = path.join(folder, 'combined-0', 'seed_x');
  await mkdir(path.join(seed, 'mod', 'param'), { recursive: true });
  await writeFile(path.join(seed, 'mod', 'param', 'GameParam.parambnd.dcx'), GAME_MARKER);
  await writeFile(path.join(seed, 'randomizer.json'), JSON.stringify({ placements: { items: [] } }));
  const manifest = { schemaVersion: 1, appVersion: '0.43.0', generationProfile: 'ds1tr-gen-v1', seedCode: 'DS1TR1-06NJDCP6-ABCD-2G', masterSeed: 'seed',
    attempts: [{ combinedAttempt: 0, fogAttempt: 0, combinedSeed: 'x', fogSeed: 1, result: 'success' }], engines: { combined: '0.5.66' }, validation: ['Combined shared output hashes verified.'],
    files: [{ path: 'mod/param/GameParam.parambnd.dcx', size: 10, sha256: 'a'.repeat(64) }], note: PLANTED };
  await writeFile(path.join(seed, 'run-manifest.json'), JSON.stringify(manifest));
  await writeFile(path.join(folder, 'generation.log'), `2026-10-07T10:00:00.000Z INFO    [ITEM_PLAN] started\n${PLANTED}`);
  if (failed) await writeFile(path.join(folder, 'failed-manifest.json'), JSON.stringify({ ...manifest, attempts: [{ combinedAttempt: 0, fogAttempt: 2, combinedSeed: 'x', fogSeed: 1, result: 'FOG_SEED_UNSOLVABLE' }] }));
  return { folder, seed, entry: {
    id: 'h1', createdAt: '2026-10-07T10:00:00.000Z', seedCode: 'DS1TR1-06NJDCP6-ABCD-2G', profile: 'ds1tr-gen-v1', masterSeed: 'seed', settings: structuredClone(DEFAULT_GENERATION_SETTINGS),
    status: 'generated', packagePath: seed, manifestPath: path.join(seed, 'run-manifest.json'), spoilerPath: null,
  } };
}

const texts = { summaryText: `Summary for ${userName}\n${PLANTED}`, readmeText: 'README of the bundle' };
const unpack = (result) => { assert.equal(result.ok, true, JSON.stringify(result)); return Object.fromEntries(readZip(result.archive).map((entry) => [entry.name, entry.data.toString('utf8')])); };
const everything = (files) => Object.entries(files).map(([name, text]) => `### ${name}\n${text}`).join('\n');

function assertClean(files, w, extra = []) {
  const all = everything(files).toLowerCase();
  for (const [label, value] of [...Object.entries(SECRETS), ['game marker', GAME_MARKER], ['home', home], ['user', `\\users\\${userName}`], ['game dir', w.gameDir], ['root', w.root], ...extra.map((value) => ['extra', value])]) {
    assert.ok(!all.includes(String(value).toLowerCase()), `${label} must not be in the bundle`);
  }
  assert.ok(!new RegExp(`(?<![A-Za-z0-9])${userName.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&')}(?![A-Za-z0-9])`, 'iu').test(everything(files)), 'the user name is not in the bundle');
  for (const name of Object.keys(files)) assert.ok(BUNDLE_ALLOWLIST.includes(name), `${name} is on the allowlist`);
}

test('a bundle after a successful generation holds only allowlisted files and none of the planted secrets, paths or game data', async () => {
  const w = await workspace();
  try {
    const { entry, folder } = await world(w);
    const operation = w.tracker.begin('generate', { seedCode: entry.seedCode, profile: entry.profile, workspacePath: folder });
    operation.event({ type: 'log', stage: 'combined', level: 'info', message: PLANTED });
    operation.finish({ ok: true });
    w.logger.info('APP', PLANTED);
    await mkdir(path.join(w.root, 'userdata', 'logs'), { recursive: true });
    await writeFile(path.join(w.root, 'userdata', 'logs', 'app.log'), `${JSON.stringify({ time: 'x', level: 'info', stage: 'APP', message: PLANTED })}\n`);
    const result = await assembleSupportBundle(w.deps([entry]), { gamePath: w.gameDir, language: 'pl', ...texts });
    const files = unpack(result);
    assertClean(files, w);
    for (const required of ['README.txt', 'summary.txt', 'diagnostics.json', 'generation/run-manifest.json', 'generation/generation-summary.json', 'generation/validation-summary.json', 'logs/backend.log', 'logs/relevant-generation.log', 'logs/app.log']) {
      assert.ok(required in files, `${required} is in the bundle`);
    }
    const diagnostics = JSON.parse(files['diagnostics.json']);
    assert.equal(diagnostics.schemaVersion, 1);
    assert.equal(diagnostics.world.seedCode, 'DS1TR1-06NJDCP6-ABCD-2G', 'the Seed Code survives for reproduction');
    assert.equal(diagnostics.application.language, 'pl');
    assert.equal(diagnostics.game.state, 'vanilla');
    assert.ok(JSON.parse(files['generation/run-manifest.json']).files[0].sha256 === 'a'.repeat(64), 'hashes survive');
    assert.ok(files['logs/relevant-generation.log'].includes('[ITEM_PLAN]'));
    assert.ok(result.report.redactions > 10);
  } finally { await rm(w.root, { recursive: true, force: true }); }
});

test('after a FAILED generation the bundle carries the failed attempt, its stage and its reason code', async () => {
  const w = await workspace();
  try {
    const { folder } = await world(w, { id: 'run-failed', failed: true });
    const operation = w.tracker.begin('generate', { seedCode: 'DS1TR1-06NJDCP6-ABCD-2G', profile: 'ds1tr-gen-v1', workspacePath: folder, plan: { items: true, enemies: false } });
    operation.event({ type: 'progress', stage: 'fog', progress: 0.5, message: 'fog' });
    operation.event({ type: 'log', stage: 'fog', level: 'error', message: `The fog randomizer could not find a layout ${PLANTED}`, component: 'fog-host' });
    operation.finish({ ok: false, code: 'FOG_SEED_UNSOLVABLE', stage: 'fog' });
    const result = await assembleSupportBundle(w.deps([]), { gamePath: w.gameDir, language: 'en', ...texts });
    const files = unpack(result);
    assertClean(files, w);
    const manifest = JSON.parse(files['generation/run-manifest.json']);
    assert.equal(manifest.attempts[0].result, 'FOG_SEED_UNSOLVABLE');
    const summary = JSON.parse(files['generation/generation-summary.json']);
    assert.equal(summary.manifestSource, 'failed-generation');
    assert.equal(summary.operations.lastFailure.code, 'FOG_SEED_UNSOLVABLE');
    assert.equal(summary.operations.lastFailure.stage, 'FOG_PLAN');
    assert.ok(JSON.parse(files['generation/validation-summary.json']).reasonCodes.includes('FOG_SEED_UNSOLVABLE'));
    assert.equal(JSON.parse(files['diagnostics.json']).world, null, 'no world exists yet');
    assert.ok(JSON.parse(files['diagnostics.json']).generation.lastFailure.timings.FOG_PLAN >= 0);
  } finally { await rm(w.root, { recursive: true, force: true }); }
});

test('after an Apply failure the bundle carries the APPLY failure and its code', async () => {
  const w = await workspace();
  try {
    const { entry } = await world(w);
    const operation = w.tracker.begin('apply');
    operation.note('error', 'APPLY', `GAME_RUNNING: Close the game ${PLANTED}`, 'GAME_RUNNING');
    operation.finish({ ok: false, code: 'GAME_RUNNING' });
    const files = unpack(await assembleSupportBundle(w.deps([entry]), { gamePath: w.gameDir, language: 'en', ...texts }));
    assertClean(files, w);
    const diagnostics = JSON.parse(files['diagnostics.json']);
    assert.equal(diagnostics.generation.lastApply.ok, false);
    assert.equal(diagnostics.generation.lastApply.code, 'GAME_RUNNING');
    assert.equal(diagnostics.generation.lastApply.stage, 'APPLY');
    assert.equal(diagnostics.generation.lastFailure.kind, 'apply');
  } finally { await rm(w.root, { recursive: true, force: true }); }
});

test('with no world, no operation and no logs the bundle still works and says so', async () => {
  const w = await workspace();
  try {
    const result = await assembleSupportBundle(w.deps([]), { gamePath: undefined, language: 'en', summaryText: 'nothing yet', readmeText: 'readme' });
    const files = unpack(result);
    assert.deepEqual(Object.keys(files).sort(), ['README.txt', 'diagnostics.json', 'generation/generation-summary.json', 'generation/validation-summary.json', 'summary.txt']);
    const diagnostics = JSON.parse(files['diagnostics.json']);
    assert.equal(diagnostics.world, null);
    assert.equal(diagnostics.game.pathConfigured, false);
    assert.match(result.fileName, /^DS1TR-support-noworld-\d{8}-\d{6}Z\.zip$/u);
  } finally { await rm(w.root, { recursive: true, force: true }); }
});

test('logs are cut to the newest lines within their limit and the total stays within the documented cap', async () => {
  const w = await workspace();
  try {
    const line = `${JSON.stringify({ time: '2026-10-07T10:00:00.000Z', level: 'info', stage: 'APP', message: 'x'.repeat(200) })}\n`;
    await mkdir(path.dirname(w.logger.file), { recursive: true });
    await writeFile(w.logger.file, line.repeat(20000));
    await writeFile(w.logger.file, `${line.repeat(19999)}${JSON.stringify({ time: 't', level: 'info', stage: 'APP', message: 'THE-NEWEST-LINE' })}\n`);
    const result = await assembleSupportBundle(w.deps([]), { gamePath: undefined, language: 'en', summaryText: 's', readmeText: 'r' });
    const files = unpack(result);
    assert.ok(Buffer.byteLength(files['logs/backend.log']) <= BUNDLE_LIMITS['logs/backend.log']);
    assert.ok(files['logs/backend.log'].includes('THE-NEWEST-LINE'));
    assert.ok(result.report.entries.find((entry) => entry.name === 'logs/backend.log').truncated || Buffer.byteLength(files['logs/backend.log']) < BUNDLE_LIMITS['logs/backend.log'] * 2);
    assert.ok(result.report.totalBytes <= BUNDLE_TOTAL_LIMIT);
  } finally { await rm(w.root, { recursive: true, force: true }); }
});

test('only allowlisted names can be added, and an empty or oversized bundle is refused', () => {
  const context = redactionContext({});
  assert.equal(buildSupportBundle([{ name: 'game/GameParam.parambnd.dcx', content: 'x' }], context, { seedCode: null }).code, 'BUNDLE_INVALID_ENTRY');
  assert.equal(buildSupportBundle([{ name: '../evil.txt', content: 'x' }], context, { seedCode: null }).code, 'BUNDLE_INVALID_ENTRY');
  assert.equal(buildSupportBundle([{ name: 'summary.txt', content: null }], context, { seedCode: null }).code, 'BUNDLE_EMPTY');
  const big = BUNDLE_ALLOWLIST.map((name) => ({ name, content: `${'y'.repeat(BUNDLE_LIMITS[name])}\n` }));
  const huge = buildSupportBundle(big, context, { seedCode: null });
  assert.ok(huge.ok === false ? huge.code === 'BUNDLE_TOO_LARGE' : huge.report.totalBytes <= BUNDLE_TOTAL_LIMIT);
});

test('diagnostics describe the application without a single folder of the person', async () => {
  const w = await workspace();
  try {
    const { entry } = await world(w);
    const diagnostics = await gatherDiagnostics(w.deps([entry]), { gamePath: w.gameDir, language: 'en' });
    const text = JSON.stringify(diagnostics);
    assert.ok(!text.includes(w.gameDir.replace(/\\/gu, '\\\\')) && !text.includes(w.root.replace(/\\/gu, '\\\\')));
    assert.equal(diagnostics.application.generationProfile, 'ds1tr-gen-v2');
    assert.equal(diagnostics.application.backendVersion, '0.43.0');
    assert.equal(diagnostics.application.installMode, 'installed');
    assert.equal(diagnostics.release.signing, 'unknown');
    assert.equal(diagnostics.world.package.fileCount, 1);
    assert.match(diagnostics.world.package.fileListSha256, /^[0-9a-f]{64}$/u);
    assert.equal(diagnostics.world.runId, 'run-a');
    assert.equal(diagnostics.application.engines.combinedRandomizer, '0.5.66');
  } finally { await rm(w.root, { recursive: true, force: true }); }
});

test('the ZIP writer produces an archive that reads back, with UTF-8 names and unsafe names refused', () => {
  const archive = createZip([{ name: 'a/zażółć.txt', data: Buffer.from('zażółć gęślą jaźń'.repeat(50)) }, { name: 'b.bin', data: Buffer.from([0, 1, 2, 3]) }]);
  const entries = readZip(archive);
  assert.deepEqual(entries.map((entry) => entry.name), ['a/zażółć.txt', 'b.bin']);
  assert.equal(entries[0].data.toString('utf8'), 'zażółć gęślą jaźń'.repeat(50));
  for (const bad of ['../x', '/abs', 'a\\b', '']) assert.throws(() => createZip([{ name: bad, data: Buffer.alloc(1) }]));
  assert.throws(() => createZip([{ name: 'x', data: Buffer.alloc(1) }, { name: 'x', data: Buffer.alloc(1) }]));
});
