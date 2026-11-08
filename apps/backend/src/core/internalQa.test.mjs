import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { DEFAULT_GENERATION_SETTINGS } from '@ds1tr/contracts';
import { INTERNAL_QA_VARIABLE, buildChannelOf, hostEnvironment, internalQaEnabled } from '../../dist/core/internalQa.js';
import { assembleSupportBundle } from '../../dist/core/support.js';

// Internal QA mode (docs/INTERNAL_QA.md): the solver route exists only for internal builds started with DS1TR_INTERNAL_QA=1.

const buildInfo = (channel) => ({ schema: 'ds1tr-build-info-1', version: '0.42.0', commit: null, mode: 'development', channel, versions: { desktop: '0.42.0', backend: 'test', contracts: 'test', foghostProject: null }, builtAt: '2026-01-01T00:00:00Z' });

async function runtimeRoot(channel) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-qa-runtime-'));
  if (channel !== null) await writeFile(path.join(root, 'build-info.json'), typeof channel === 'string' && channel.startsWith('{') ? channel : JSON.stringify(buildInfo(channel)));
  return root;
}

test('internal QA needs the variable AND a non-public build; a public build ignores the variable whatever it says', async () => {
  const internal = await runtimeRoot('internal');
  const publicRoot = await runtimeRoot('public');
  const legacy = await runtimeRoot(JSON.stringify({ schema: 'ds1tr-build-info-1' }));
  const missing = await runtimeRoot(null);
  const malformed = await runtimeRoot('{ not json');
  try {
    const on = { [INTERNAL_QA_VARIABLE]: '1' };
    assert.equal(internalQaEnabled(on, internal), true);
    assert.equal(internalQaEnabled({}, internal), false, 'an internal build without the variable is a normal build');
    for (const value of ['0', 'true', 'yes', '', ' 1', '1 ']) assert.equal(internalQaEnabled({ [INTERNAL_QA_VARIABLE]: value }, internal), false, `'${value}'`);
    for (const root of [publicRoot, legacy, missing, malformed]) assert.equal(internalQaEnabled(on, root), false, 'every doubt is public');
    assert.equal(buildChannelOf(publicRoot), 'public');
    assert.equal(buildChannelOf(internal), 'internal');
    // From source (no packaged runtime) the variable alone decides: development and the test suites.
    assert.equal(internalQaEnabled(on, null), true);
    assert.equal(internalQaEnabled({}, null), false);
    assert.equal(internalQaEnabled(on, undefined), true);
  } finally {
    for (const root of [internal, publicRoot, legacy, missing, malformed]) await rm(root, { recursive: true, force: true });
  }
});

test('FogHost is told by the backend: a player-set variable never reaches the host of a public build, and the host never inherits a stale one', () => {
  assert.equal(hostEnvironment({ PATH: 'p', [INTERNAL_QA_VARIABLE]: '1' }, false)[INTERNAL_QA_VARIABLE], undefined);
  assert.equal(hostEnvironment({ PATH: 'p' }, true)[INTERNAL_QA_VARIABLE], '1');
  assert.equal(hostEnvironment({ PATH: 'p', [INTERNAL_QA_VARIABLE]: 'garbage' }, true)[INTERNAL_QA_VARIABLE], '1');
  assert.equal(hostEnvironment({ PATH: 'p' }, false).PATH, 'p', 'everything else is passed through');
});

function dependencies(root, runtime, env, packagePath) {
  const entry = { id: 'run-1', seedCode: 'DS1TR1-X', profile: 'ds1tr-gen-v7', masterSeed: 'seed', settings: structuredClone(DEFAULT_GENERATION_SETTINGS), packagePath, manifestPath: path.join(path.dirname(packagePath), 'run-manifest.json'), spoilerPath: null, hintsPath: null, createdAt: new Date().toISOString(), status: 'generated' };
  return {
    stateRoot: path.join(root, 'state'), runRoot: path.join(root, 'runs'), userDataDir: null, appLogPath: null, backendLogPath: path.join(root, 'backend.log'),
    runtimeRoot: runtime, appVersion: 'test', env, listHistory: async () => [entry], detectState: async () => ({ state: 'unknown' }), now: () => new Date('2026-01-01T00:00:00Z'),
  };
}

async function bundleNames(env, runtime) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-qa-support-'));
  try {
    const run = path.join(root, 'runs', 'abc');
    const packagePath = path.join(run, 'fog-0-0', 'game');
    await mkdir(packagePath, { recursive: true });
    await writeFile(path.join(run, 'solver-witness.json'), '{"secret":"route to gwyn"}');
    await writeFile(path.join(run, 'solver-route.md'), 'Step 1: ...');
    await mkdir(path.join(root, 'state'), { recursive: true });
    const result = await assembleSupportBundle(dependencies(root, runtime, env, packagePath), { summaryText: 's', readmeText: 'r' });
    assert.equal(result.ok, true, result.ok ? '' : `${result.code}: ${result.detail}`);
    return result.report.entries.map((entry) => entry.name);
  } finally { await rm(root, { recursive: true, force: true }); }
}

test('support bundle: a public build never carries the route even when the files exist and the variable is set; an internal QA build does', async () => {
  const publicRoot = await runtimeRoot('public');
  const internal = await runtimeRoot('internal');
  try {
    for (const env of [{}, { [INTERNAL_QA_VARIABLE]: '1' }]) {
      const names = await bundleNames(env, publicRoot);
      assert.ok(!names.some((name) => name.includes('solver-')), `public build, env ${JSON.stringify(env)}: ${names.join(', ')}`);
    }
    assert.ok(!(await bundleNames({}, internal)).some((name) => name.includes('solver-')), 'an internal build without the variable is public');
    const internalNames = await bundleNames({ [INTERNAL_QA_VARIABLE]: '1' }, internal);
    assert.ok(internalNames.includes('generation/solver-route.md') && internalNames.includes('generation/solver-witness.json'));
  } finally {
    await rm(publicRoot, { recursive: true, force: true });
    await rm(internal, { recursive: true, force: true });
  }
});

test('the server wires the route only behind the internal QA decision', async () => {
  const source = await readFile(new URL('../../src/server.ts', import.meta.url), 'utf8');
  assert.match(source, /const INTERNAL_QA = internalQaEnabled\(process\.env, process\.env\.DS1TR_RUNTIME_ROOT\)/u);
  assert.match(source, /if \(INTERNAL_QA && req\.method === 'POST' && url\.pathname === '\/api\/solver-route\/get'\)/u);
  assert.match(source, /if \(INTERNAL_QA\) \{\s+await persistSolverWitness\(/u);
  assert.match(source, /\.\.\.\(INTERNAL_QA \? \{ solverRouteAvailable:/u);
  assert.equal((source.match(/persistSolverWitness\(/gu) ?? []).length, 1);
});
