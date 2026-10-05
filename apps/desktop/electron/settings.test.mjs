import assert from 'node:assert/strict';
import test from 'node:test';
import { migrateSettings } from '../dist-electron/settings.js';

test('an old settings file with a Fog data folder is migrated: the folder is dropped, the file is marked for rewriting', () => {
  assert.deepEqual(migrateSettings({ fogDataRoot: 'C:\\Users\\x\\fog-data' }), { settings: {}, changed: true });
  assert.deepEqual(migrateSettings({ fogDataRoot: 'C:\\fog', gamePath: 'D:\\Games\\DSR' }), { settings: { gamePath: 'D:\\Games\\DSR' }, changed: true });
});

test('a current settings file is left alone; malformed content is discarded', () => {
  assert.deepEqual(migrateSettings({ gamePath: 'D:\\Games\\DSR' }), { settings: { gamePath: 'D:\\Games\\DSR' }, changed: false });
  assert.deepEqual(migrateSettings({}), { settings: {}, changed: false });
  assert.deepEqual(migrateSettings(null), { settings: {}, changed: false });
  assert.deepEqual(migrateSettings(['x']), { settings: {}, changed: false });
  assert.deepEqual(migrateSettings({ gamePath: 7, extra: true }), { settings: {}, changed: true });
});
