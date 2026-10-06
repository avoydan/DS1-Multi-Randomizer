import assert from 'node:assert/strict';
import test from 'node:test';
import { launchGame } from '../dist-electron/launchGame.js';

const deps = (overrides = {}) => {
  const calls = [];
  return {
    calls,
    openExternal: async (url) => { calls.push(['openExternal', url]); if (overrides.steam === false) throw new Error('no protocol handler'); },
    startDetached: async (exe, dir) => { calls.push(['start', exe, dir]); if (overrides.start === false) throw new Error('spawn failed'); },
    exists: async () => overrides.exists !== false,
  };
};

test('the game is launched through Steam with its app id, without browsing for anything', async () => {
  const d = deps();
  const result = await launchGame('D:\Games\DSR', d);
  assert.deepEqual(d.calls, [['openExternal', 'steam://rungameid/570940']]);
  assert.equal(result.ok, true);
  assert.equal(result.method, 'steam');
});

test('without a Steam handler the executable of the installation is the fallback, and the method says so', async () => {
  const d = deps({ steam: false });
  const result = await launchGame('D:\Games\DSR', d);
  assert.equal(result.ok, true);
  assert.equal(result.method, 'executable');
  assert.equal(d.calls[1][0], 'start');
  assert.match(d.calls[1][1], /DarkSoulsRemastered\.exe$/u);
  assert.match(result.technical, /no protocol handler/u);
});

test('when nothing works the message is simple and the reason stays technical', async () => {
  for (const options of [{ steam: false, exists: false }, { steam: false, start: false }]) {
    const result = await launchGame('D:\Games\DSR', deps(options));
    assert.equal(result.ok, false);
    assert.equal(result.method, 'none');
    assert.match(result.message, /start it from Steam/u);
    assert.doesNotMatch(result.message, /exe|ENOENT|protocol/u);
    assert.ok(result.technical.length > 0);
  }
});
