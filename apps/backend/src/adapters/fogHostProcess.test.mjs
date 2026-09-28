import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { PassThrough } from 'node:stream';
import test from 'node:test';
import { FogHostProcess } from '../../dist/adapters/fogHostProcess.js';

const options = Object.fromEntries([
  'traversableFogs', 'bossFogs', 'areaWarps', 'majorPvpFogs', 'minorPvpFogs',
  'lordvesselGates', 'randomStart', 'disconnectedFogs', 'noFallBedOfChaos',
  'scaling', 'glitchedLogic', 'requireLordSouls', 'pacifistMode',
].map((key) => [key, false]));

const request = {
  schemaVersion: 1, game: 'DS1R', mode: 'dryRun', seed: 0xffffffff,
  options, language: 'ENGLISH', fogDataRoot: 'fog-data',
  stagingDirectory: 'attempt/game', logPath: 'attempt/foghost.log', compatibility: {},
};
const success = {
  schemaVersion: 1, ok: true, hostVersion: '0.1.0', fogModCommit: 'abc',
  annotationSha256: 'f'.repeat(64), mode: 'dryRun', seed: request.seed,
  fogOptions: 'v5', keyItems: { randomized: true, hash: '01234' },
  files: [], error: null,
};

class FakeChild extends EventEmitter {
  stdout = new PassThrough();
  stderr = new PassThrough();
  killed = false;
  kill() { this.killed = true; return true; }
}

async function fixture(t) {
  const dir = await mkdtemp(path.join(tmpdir(), 'fog-host-test-'));
  t.after(() => rm(dir, { recursive: true, force: true }));
  return {
    executablePath: 'FogHost.exe', requestPath: path.join(dir, 'request.json'),
    responsePath: path.join(dir, 'response.json'), timeoutMs: 1000, request,
  };
}

test('writes request, forwards exact command, parses progress and authoritative response', async (t) => {
  const execution = await fixture(t);
  const calls = [];
  const events = [];
  const process = new FogHostProcess((command, args, spawnOptions) => {
    calls.push({ command, args, spawnOptions });
    const child = new FakeChild();
    queueMicrotask(async () => {
      assert.deepEqual(JSON.parse(await readFile(execution.requestPath, 'utf8')), request);
      child.stdout.write('{"type":"progress","stage":"solve-graph","message":"Solving"}\nother output\n');
      child.stderr.write('warning\n');
      await writeFile(execution.responsePath, JSON.stringify(success));
      child.emit('close', 0);
    });
    return child;
  });
  assert.deepEqual(await process.execute(execution, (event) => events.push(event)), success);
  assert.deepEqual(calls, [{ command: 'FogHost.exe', args: [
    'generate', '--request', execution.requestPath, '--response', execution.responsePath,
  ], spawnOptions: { stdio: ['ignore', 'pipe', 'pipe'] } }]);
  assert.deepEqual(events, [
    { type: 'progress', stage: 'solve-graph', message: 'Solving' },
    { type: 'log', stream: 'stdout', message: 'other output\n' },
    { type: 'log', stream: 'stderr', message: 'warning\n' },
  ]);
});

test('preserves typed host failure and retry scope', async (t) => {
  const execution = await fixture(t);
  const response = { schemaVersion: 1, ok: false, error: {
    code: 'FOG_SEED_UNSOLVABLE', stage: 'solve-graph', retryable: true,
    retryScope: 'fog', message: 'Could not solve seed',
  } };
  const process = new FogHostProcess(() => {
    const child = new FakeChild();
    queueMicrotask(async () => {
      await writeFile(execution.responsePath, JSON.stringify(response));
      child.emit('close', 0);
    });
    return child;
  });
  assert.deepEqual(await process.execute(execution), response);
});

test('maps nonzero exit, missing response, spawn failure and invalid JSON to crashed', async (t) => {
  for (const scenario of ['exit', 'missing', 'spawn', 'invalid']) {
    const execution = await fixture(t);
    const process = new FogHostProcess(() => {
      if (scenario === 'spawn') throw new Error('missing executable');
      const child = new FakeChild();
      queueMicrotask(async () => {
        if (scenario === 'invalid') await writeFile(execution.responsePath, '{broken');
        child.emit('close', scenario === 'exit' ? 3 : 0);
      });
      return child;
    });
    const result = await process.execute(execution);
    assert.equal(result.ok, false);
    assert.equal(result.error.code, 'FOG_HOST_CRASHED');
  }
});

test('kills a timed-out process and returns a non-retryable timeout', async (t) => {
  const execution = { ...await fixture(t), timeoutMs: 10 };
  const child = new FakeChild();
  const process = new FogHostProcess(() => child);
  const result = await process.execute(execution);
  assert.equal(child.killed, true);
  assert.equal(result.error.code, 'FOG_HOST_TIMEOUT');
  assert.equal(result.error.retryable, false);
});

test('rejects invalid schema before process launch', async (t) => {
  const execution = { ...await fixture(t), request: { ...request, seed: -1 } };
  const process = new FogHostProcess(() => { throw new Error('must not spawn'); });
  const result = await process.execute(execution);
  assert.equal(result.error.code, 'FOG_REQUEST_INVALID');
});

test('rejects a stale response file before process launch', async (t) => {
  const execution = await fixture(t);
  await writeFile(execution.responsePath, JSON.stringify(success));
  const process = new FogHostProcess(() => { throw new Error('must not spawn'); });
  const result = await process.execute(execution);
  assert.equal(result.error.code, 'FOG_REQUEST_INVALID');
});

test('rejects omitted option keys before process launch', async (t) => {
  const execution = await fixture(t);
  const { pacifistMode, ...incomplete } = request.options;
  execution.request = { ...request, options: incomplete };
  const process = new FogHostProcess(() => { throw new Error('must not spawn'); });
  const result = await process.execute(execution);
  assert.equal(result.error.code, 'FOG_REQUEST_INVALID');
});
