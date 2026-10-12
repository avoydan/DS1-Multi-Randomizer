import assert from 'node:assert/strict';
import test from 'node:test';
import { migrateSettings, normalizeGamePath } from '../dist-electron/settings.js';

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

test('the local free-cursor convenience defaults off and a saved boolean survives migration', () => {
  assert.deepEqual(migrateSettings({}), { settings: {}, changed: false });
  assert.deepEqual(migrateSettings({ freeCursorInMenus: true }), { settings: { freeCursorInMenus: true }, changed: false });
  assert.deepEqual(migrateSettings({ freeCursorInMenus: false }), { settings: { freeCursorInMenus: false }, changed: false });
  assert.deepEqual(migrateSettings({ freeCursorInMenus: 'yes' }), { settings: {}, changed: true });
});

test('a chosen game folder is stored in one canonical form; anything that is not an absolute folder is not remembered', () => {
  assert.equal(normalizeGamePath('D:/Games/Dark Souls Remastered/'), String.raw`D:\Games\Dark Souls Remastered`);
  assert.equal(normalizeGamePath(String.raw`  C:\Games\DSR\\ `), String.raw`C:\Games\DSR`);
  assert.equal(normalizeGamePath(String.raw`\\nas\games\DSR`), String.raw`\\nas\games\DSR`);
  for (const bad of ['', '   ', 'games/DSR', '.', 'C:', 'C:\\', 7, null, undefined, {}]) assert.equal(normalizeGamePath(bad), null, String(bad));
  // The remembered value survives a settings round trip untouched.
  assert.deepEqual(migrateSettings({ gamePath: normalizeGamePath('D:/Games/DSR') }), { settings: { gamePath: String.raw`D:\Games\DSR` }, changed: false });
});
