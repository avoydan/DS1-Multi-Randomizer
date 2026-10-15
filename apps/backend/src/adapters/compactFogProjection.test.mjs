import assert from 'node:assert/strict';
import test from 'node:test';
import {
  COMPACT_FOG_CATALOG_VERSION,
  COMPACT_FOG_PROJECTION_FORMAT,
  CompactFogProjectionError,
  parseCompactFogProjection,
} from '../../dist/adapters/compactFogProjection.js';

const valid = () => ({
  format: COMPACT_FOG_PROJECTION_FORMAT,
  catalogVersion: COMPACT_FOG_CATALOG_VERSION,
  catalogFingerprint: 'a'.repeat(64),
  items: [{ equipType: 3, itemId: 2510, required: true }, { equipType: 3, itemId: 3500, required: false }],
  lotStarts: [1090, 2500],
});

test('parses a Compact projection without changing its tracked identities', () => {
  const source = valid();
  const projection = parseCompactFogProjection(source);
  assert.deepEqual(projection, source);
  projection.items.pop();
  projection.lotStarts.pop();
  assert.equal(source.items.length, 2);
  assert.equal(source.lotStarts.length, 2);
});

test('rejects an unknown fingerprint, duplicate identity, or empty coverage', () => {
  for (const source of [
    { ...valid(), catalogFingerprint: 'not-a-sha256' },
    { ...valid(), items: [valid().items[0], valid().items[0]] },
    { ...valid(), lotStarts: [] },
  ]) assert.throws(() => parseCompactFogProjection(source), CompactFogProjectionError);
});
