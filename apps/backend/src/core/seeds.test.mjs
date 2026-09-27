import assert from 'node:assert/strict';
import test from 'node:test';
import { deriveSeeds } from '../../dist/core/seeds.js';

test('derives stable combined and Fog seeds from one master seed', () => {
  assert.deepEqual(deriveSeeds('abc'), {
    combined: 'abc',
    fog: 1_898_576_503,
  });
});

test('normalizes an empty master seed without collapsing engine streams', () => {
  const empty = deriveSeeds('   ');
  const zero = deriveSeeds('0');

  assert.deepEqual(empty, zero);
  assert.equal(empty.combined, '0');
});
