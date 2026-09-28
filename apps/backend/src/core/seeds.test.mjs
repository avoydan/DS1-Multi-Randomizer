import assert from 'node:assert/strict';
import test from 'node:test';
import { deriveSeeds } from '../../dist/core/seeds.js';

/** Seed charset the pinned upstream `validateConfig` accepts. */
const upstreamSeedPattern = /^[\w.-]{1,64}$/;

test('derives stable combined and Fog seeds from one master seed', () => {
  assert.deepEqual(deriveSeeds('abc'), {
    combined: '834cd1e1947b0c3ef50cb4ac36fa99ad',
    fog: 1_898_576_503,
  });
});

test('normalizes an empty master seed without collapsing engine streams', () => {
  const empty = deriveSeeds('   ');
  const zero = deriveSeeds('0');

  assert.deepEqual(empty, zero);
  assert.notEqual(empty.combined, '0');
});

test('trims the master seed before deriving', () => {
  assert.deepEqual(deriveSeeds('  abc  '), deriveSeeds('abc'));
});

test('always derives a combined seed the upstream engine accepts', () => {
  const masterSeeds = [
    'abc',
    'zażółć gęślą jaźń',
    'seed with internal spaces',
    '🔥 praise the sun 🔥',
    'a/b\c:d*e?f"g<h>i|j',
    'x'.repeat(1000),
    '0',
  ];

  for (const masterSeed of masterSeeds) {
    const { combined } = deriveSeeds(masterSeed);
    assert.match(combined, upstreamSeedPattern, `master seed ${JSON.stringify(masterSeed)}`);
    assert.match(combined, /^[0-9a-f]{32}$/);
  }
});

test('distinct master seeds derive distinct combined seeds', () => {
  assert.notEqual(deriveSeeds('abc').combined, deriveSeeds('abd').combined);
  assert.notEqual(deriveSeeds('abc').combined, deriveSeeds('ABC').combined);
});
