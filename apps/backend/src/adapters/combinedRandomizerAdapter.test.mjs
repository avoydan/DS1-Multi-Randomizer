import assert from 'node:assert/strict';
import test from 'node:test';
import { CombinedRandomizerAdapter } from '../../dist/adapters/combinedRandomizerAdapter.js';

const request = {
  gamePath: 'C:\\fixture\\Dark Souls Remastered',
  workspacePath: 'C:\\fixture\\workspace',
  masterSeed: 'master-seed',
  combinedSeed: 'derived-seed',
  items: { enabled: true, randomizeWorldItems: true },
  enemies: { enabled: false, randomizeRegularEnemies: false, randomizeBosses: false },
  generateSpoilerLog: false,
};

test('forwards the request and progress/log events, then returns the package directory', async () => {
  const progress = { type: 'progress', progress: 0.5, message: 'Generating.' };
  const log = { type: 'log', level: 'info', message: 'Scanning.' };
  const events = [];
  let receivedRequest;
  const adapter = new CombinedRandomizerAdapter(async (input, onEvent) => {
    receivedRequest = input;
    onEvent(progress);
    onEvent(log);
    return {
      ok: true,
      seedDirectory: 'C:\\fixture\\workspace\\seed_derived-seed',
      workspaceDirectory: input.workspacePath,
      catalogPath: 'catalog',
      placementsPath: 'placements',
      patchManifestPath: 'manifest',
      randomizerVersion: '0.5.66',
      placementHash: 'hash',
      seed: input.combinedSeed,
      catalogScanned: true,
    };
  });

  const result = await adapter.generate(request, (event) => events.push(event));

  assert.strictEqual(receivedRequest, request);
  assert.equal(receivedRequest.combinedSeed, 'derived-seed');
  assert.deepEqual(events, [progress, log]);
  assert.deepEqual(result, {
    ok: true,
    workspacePath: 'C:\\fixture\\workspace\\seed_derived-seed',
  });
});

test('preserves typed bridge failures and emitted events', async () => {
  const failure = {
    ok: false,
    code: 'GENERATION_FAILED',
    message: 'No safe boss permutation.',
    retryable: true,
    validationErrors: ['fixture detail'],
  };
  const events = [];
  const adapter = new CombinedRandomizerAdapter(async (_input, onEvent) => {
    onEvent({ type: 'log', level: 'error', message: failure.message });
    return failure;
  });

  const result = await adapter.generate(request, (event) => events.push(event));

  assert.strictEqual(result, failure);
  assert.deepEqual(events, [{ type: 'log', level: 'error', message: failure.message }]);
});
