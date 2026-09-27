import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { PassThrough } from 'node:stream';
import test from 'node:test';
import { DsrDataToolExecutor } from '../../dist/adapters/combinedRandomizerDataToolExecutor.js';

class FakeChildProcess extends EventEmitter {
  stdout = new PassThrough();
  stderr = new PassThrough();
}

function createSpawn({ stdout = '', stderr = '', exitCode = 0, spawnError } = {}) {
  const calls = [];
  const spawnProcess = (command, args, options) => {
    calls.push({ command, args, options });
    const child = new FakeChildProcess();
    queueMicrotask(() => {
      if (spawnError) {
        child.emit('error', spawnError);
        return;
      }
      if (stdout) child.stdout.write(stdout);
      if (stderr) child.stderr.write(stderr);
      child.stdout.end();
      child.stderr.end();
      child.emit('close', exitCode);
    });
    return child;
  };
  return { calls, spawnProcess };
}

const scanRequest = {
  dotnetExecutablePath: 'C:\\tools\\dotnet.exe',
  dataToolDllPath: 'C:\\vendor\\DsrDataTool.dll',
  plannedArguments: [
    'scan',
    '--game', 'C:\\games\\Dark Souls Remastered',
    '--output', 'C:\\workspace\\dsr-catalog.json',
  ],
};

test('returns success and forwards the complete planned argument vector', async () => {
  const fake = createSpawn();
  const executor = new DsrDataToolExecutor(fake.spawnProcess);

  const result = await executor.execute(scanRequest);

  assert.deepEqual(result, { ok: true, stdout: '', stderr: '', exitCode: 0 });
  assert.deepEqual(fake.calls, [{
    command: scanRequest.dotnetExecutablePath,
    args: [scanRequest.dataToolDllPath, ...scanRequest.plannedArguments],
    options: { stdio: ['ignore', 'pipe', 'pipe'] },
  }]);
});

test('returns a typed process failure for a non-zero exit code', async () => {
  const fake = createSpawn({ stderr: 'patch failed\n', exitCode: 17 });
  const executor = new DsrDataToolExecutor(fake.spawnProcess);
  const request = {
    ...scanRequest,
    plannedArguments: ['patch-enemies', '--game', 'game', '--output', 'workspace'],
  };

  const result = await executor.execute(request);

  assert.deepEqual(result, {
    ok: false,
    type: 'process-failure',
    stdout: '',
    stderr: 'patch failed\n',
    exitCode: 17,
  });
});

test('returns a typed spawn failure without throwing', async () => {
  const fake = createSpawn({ spawnError: new Error('dotnet missing') });
  const executor = new DsrDataToolExecutor(fake.spawnProcess);

  const result = await executor.execute(scanRequest);

  assert.deepEqual(result, {
    ok: false,
    type: 'spawn-failure',
    stdout: '',
    stderr: '',
    exitCode: null,
    message: 'dotnet missing',
  });
});

test('captures and forwards stdout as log events', async () => {
  const fake = createSpawn({ stdout: 'Scanned 12 maps.\n' });
  const executor = new DsrDataToolExecutor(fake.spawnProcess);
  const events = [];

  const result = await executor.execute(scanRequest, (event) => events.push(event));

  assert.equal(result.stdout, 'Scanned 12 maps.\n');
  assert.deepEqual(events.filter((event) => event.type === 'log'), [{
    type: 'log', stream: 'stdout', message: 'Scanned 12 maps.\n',
  }]);
});

test('captures and forwards stderr as log events', async () => {
  const fake = createSpawn({ stderr: 'warning from tool\n' });
  const executor = new DsrDataToolExecutor(fake.spawnProcess);
  const events = [];

  const result = await executor.execute(scanRequest, (event) => events.push(event));

  assert.equal(result.stderr, 'warning from tool\n');
  assert.deepEqual(events.filter((event) => event.type === 'log'), [{
    type: 'log', stream: 'stderr', message: 'warning from tool\n',
  }]);
});

test('emits stage-level progress events', async () => {
  const fake = createSpawn();
  const executor = new DsrDataToolExecutor(fake.spawnProcess);
  const events = [];

  await executor.execute(scanRequest, (event) => events.push(event));

  assert.deepEqual(events, [
    { type: 'progress', command: 'scan', status: 'started' },
    { type: 'progress', command: 'scan', status: 'succeeded' },
  ]);
});

test('refuses commands outside scan and patch-enemies without spawning', async () => {
  const fake = createSpawn();
  const executor = new DsrDataToolExecutor(fake.spawnProcess);

  const result = await executor.execute({
    ...scanRequest,
    plannedArguments: ['install', '--game', 'C:\\games\\Dark Souls Remastered'],
  });

  assert.equal(result.ok, false);
  assert.equal(result.type, 'spawn-failure');
  assert.match(result.message, /Unsupported DsrDataTool command/);
  assert.deepEqual(fake.calls, []);
});
