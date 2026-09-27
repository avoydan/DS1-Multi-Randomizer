import assert from 'node:assert/strict';
import fs from 'node:fs';
import fsPromises from 'node:fs/promises';
import { syncBuiltinESMExports } from 'node:module';
import test from 'node:test';
import { CombinedRandomizerAdapter } from '../../dist/adapters/combinedRandomizerAdapter.js';

const request = {
  gamePath: 'C:\\nonexistent\\Dark Souls Remastered',
  workspacePath: 'C:\\nonexistent\\workspace',
  masterSeed: 'master-seed',
  combinedSeed: 'combined-seed',
  items: {
    enabled: true,
    randomizeWorldItems: true,
    randomizeProtectedItems: false,
    progressionLogic: true,
    guaranteeEarlyWeapon: true,
    balancedEarlyLoot: true,
    randomizeStartingClass: true,
    randomizeStartingEquipment: true,
    randomizeGifts: true,
    randomizeEnemyDrops: true,
    guaranteedEnemyDrops: false,
    randomizeShops: true,
  },
  enemies: {
    enabled: true,
    randomizeRegularEnemies: true,
    randomizeBosses: true,
    scaling: 'progressive',
  },
};

test('returns NOT_IMPLEMENTED without events or filesystem I/O', async (t) => {
  const adapter = new CombinedRandomizerAdapter();
  const events = [];

  for (const api of [fs, fsPromises]) {
    for (const key of Object.keys(api)) {
      if (/^[a-z]/.test(key) && typeof api[key] === 'function') {
        t.mock.method(api, key, () => {
          assert.fail(`Unexpected filesystem call: ${key}`);
        });
      }
    }
  }
  syncBuiltinESMExports();

  let result;
  try {
    result = await adapter.generate(request, (event) => events.push(event));
  } finally {
    t.mock.restoreAll();
    syncBuiltinESMExports();
  }

  assert.deepEqual(result, {
    ok: false,
    code: 'NOT_IMPLEMENTED',
    message: 'Combined items and enemies randomizer integration is not implemented.',
  });
  assert.deepEqual(events, []);
});
