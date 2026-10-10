import assert from 'node:assert/strict';
import test from 'node:test';
import { createGameDirectoryPicker } from '../dist-electron/gameDirectoryDialog.js';

test('the game directory picker remembers the last visited parent folder', async () => {
  const defaults = [];
  const picker = createGameDirectoryPicker(async (defaultPath) => {
    defaults.push(defaultPath);
    if (defaults.length === 1) return { canceled: false, filePaths: ['C:\\Games\\Dark Souls Remastered'] };
    return { canceled: true, filePaths: [] };
  }, () => 'C:\\Users\\Player\\Documents');

  assert.equal(await picker(), 'C:\\Games\\Dark Souls Remastered');
  assert.equal(await picker(), null);
  assert.deepEqual(defaults, ['C:\\Users\\Player\\Documents', 'C:\\Games']);
});

test('a canceled game directory selection does not replace the saved initial directory', async () => {
  const defaults = [];
  const picker = createGameDirectoryPicker(async (defaultPath) => {
    defaults.push(defaultPath);
    return { canceled: true, filePaths: [] };
  }, () => 'C:\\Games\\Dark Souls Remastered');

  assert.equal(await picker(), null);
  assert.equal(await picker(), null);
  assert.deepEqual(defaults, ['C:\\Games\\Dark Souls Remastered', 'C:\\Games\\Dark Souls Remastered']);
});
