import assert from 'node:assert/strict';
import test from 'node:test';
import { genericFailureMessage, userMessageFor } from '../../dist/core/userMessages.js';

test('known technical codes have a plain message without developer terms', () => {
  for (const code of ['GAME_INVALID', 'PRISTINE_SOURCE_INVALID', 'COMBINED_OUTPUT_MISMATCH', 'FOG_RETRIES_EXHAUSTED', 'MANIFEST_WRITE_FAILED', 'FOG_STAGING_FAILED']) {
    const message = userMessageFor(code, 'technical detail with C:/path and sha256 abc');
    assert.doesNotMatch(message, /C:|sha256|FogHost|FogMod|DsrDataTool|dotnet|\.NET|EMEVD|MSB|NpcParam|dist\/DS1R/u, code);
  }
});

test('messages already written for people pass through without their code prefix', () => {
  assert.equal(userMessageFor('WORLD_DATA_OFFLINE', 'Check your internet connection and click Retry.'), 'Check your internet connection and click Retry.');
  assert.equal(userMessageFor('ENEMY_PLACEMENT_CUSTOM_POOL_INVALID', 'ENEMY_PLACEMENT_CUSTOM_POOL_INVALID: no regular slot can take any selected enemy.'), 'no regular slot can take any selected enemy.');
});

test('unknown failures and unknown fog randomizer codes get a safe, generic message', () => {
  assert.equal(userMessageFor('SOMETHING_NEW', 'boom'), genericFailureMessage);
  assert.match(userMessageFor('FOG_MERGE_CONFLICT', 'event/m15: event 11505392'), /fog randomizer/u);
});
