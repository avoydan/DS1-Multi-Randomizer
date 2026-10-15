import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const app = await readFile(new URL('../App.tsx', import.meta.url), 'utf8');
const appSettings = await readFile(new URL('./OtherViews.tsx', import.meta.url), 'utf8');

test('game conveniences are a Your game card and unlock launcher-only runs when enabled', () => {
  assert.match(app, /view === 'game'[\s\S]*?<GameConvenienceSettings onEnabledChange=\{setFreeCursorEnabled\} \/>/u);
  assert.match(app, /enabledCount === 0 && !freeCursorEnabled/u);
  assert.doesNotMatch(appSettings, /GameConvenienceSettings/u);
});

test('the free-cursor shortcut is captured in the game card and saved through the local settings bridge', async () => {
  const settings = await readFile(new URL('./GameConvenienceSettings.tsx', import.meta.url), 'utf8');
  assert.match(settings, /freeCursorShortcut\(\)/u);
  assert.match(settings, /setFreeCursorShortcut\(next\)/u);
  assert.match(settings, /onKeyDown=\{captureShortcut\}/u);
  assert.match(settings, /event\.ctrlKey && 'Ctrl'/u);
  assert.match(settings, /event\.altKey && 'Alt'/u);
  assert.match(settings, /event\.shiftKey && 'Shift'/u);
});
