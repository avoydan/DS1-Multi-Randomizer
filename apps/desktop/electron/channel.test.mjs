import assert from 'node:assert/strict';
import test from 'node:test';
import { isDevToolsShortcut, parseBuildChannel } from '../dist-electron/channel.js';

test('a packaged app is public unless its build information says it is an internal build (the safe side is to show less)', () => {
  assert.equal(parseBuildChannel(null), 'public');
  assert.equal(parseBuildChannel('not json'), 'public');
  assert.equal(parseBuildChannel('{"schema":"ds1tr-build-info-1","mode":"development"}'), 'public', 'a build made before the channel existed is public');
  assert.equal(parseBuildChannel('{"channel":"public"}'), 'public');
  assert.equal(parseBuildChannel('{"channel":"internal"}'), 'internal');
  assert.equal(parseBuildChannel('{"channel":"INTERNAL"}'), 'public');
  assert.equal(parseBuildChannel('{"channel":true}'), 'public');
});

test('the optional shortcut is exactly Ctrl+Shift+Alt+F12', () => {
  const base = { type: 'keyDown', key: 'F12', control: true, shift: true, alt: true };
  assert.equal(isDevToolsShortcut(base), true);
  for (const change of [{ control: false }, { shift: false }, { alt: false }, { key: 'F11' }, { type: 'keyUp' }, { meta: true }]) assert.equal(isDevToolsShortcut({ ...base, ...change }), false, JSON.stringify(change));
});
