import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, rm } from 'node:fs/promises';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

// Independent of the caller's cwd: `npm test` runs from apps/backend.
const backendRoot = fileURLToPath(new URL('..', import.meta.url));

test('desktop API exposes typed state and generation failure without touching a game', async (t) => {
  const stateRoot = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-api-'));
  // A port the operating system hands out: a random number can fall into a range Windows reserves (Hyper-V / WinNAT) and the server cannot bind it.
  const port = await new Promise((resolve, reject) => {
    const probe = net.createServer();
    probe.once('error', reject);
    probe.listen(0, '127.0.0.1', () => { const { port: free } = probe.address(); probe.close(() => resolve(free)); });
  });
  const child = spawn(process.execPath, ['dist/server.js'], {
    cwd: backendRoot,
    env: { ...process.env, DS1TR_PORT: String(port), DS1TR_STATE_DIR: stateRoot },
    stdio: 'ignore',
  });
  t.after(async () => { child.kill(); await rm(stateRoot, { recursive: true, force: true }); });
  const url = `http://127.0.0.1:${port}/api`;
  let ready = false;
  for (let attempt = 0; attempt < 200; attempt++) {
    try { ready = (await fetch(`${url}/health`)).ok; } catch { /* Server starting. */ }
    if (ready) break;
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
  assert.equal(ready, true, 'backend starts');
  // Nothing is running: the desktop shell may restart to install an update (docs/AUTO_UPDATE.md).
  const idle = await (await fetch(`${url}/activity`)).json();
  assert.deepEqual(idle, { ok: true, busy: false, active: [] });
  const denied = await fetch(`${url}/game/restore`, {
    method: 'POST', headers: { Origin: 'https://example.org', 'Content-Type': 'application/json' },
    body: JSON.stringify({ gamePath: 'irrelevant' }),
  });
  assert.equal(denied.status, 403);
  const post = (route, body) => fetch(`${url}${route}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
  });
  const gamePath = path.join(os.tmpdir(), `missing-ds1tr-game-${port}`);
  const state = await (await post('/game/state', { gamePath })).json();
  assert.equal(state.state, 'invalid');
  const imported = await (await post('/game/import', { gamePath })).json();
  assert.equal(imported.ok, false);
  assert.equal(imported.code, 'GAME_INVALID');
  // Generation is an event stream; settings that cannot be used end it with a typed result before any seed is made.
  const generation = await post('/runs/generate', { gamePath, masterSeed: '' });
  const streamed = await generation.text();
  assert.match(streamed, /event: result/u);
  assert.match(streamed, /"code":"INVALID_SETTINGS"/u);
  assert.match(streamed, /"stage":"seed"/u);
  const badSeed = await post('/seed/decode', { seedCode: 'DS1TR1-NOT-A-REAL-CODE' });
  assert.equal(badSeed.status, 400);
  assert.equal((await badSeed.json()).ok, false);
  const noSettings = await (await post('/settings/load', {})).json();
  assert.equal(noSettings.found, false);
  assert.equal((await post('/settings/save', { settings: {}, generateSpoilerLog: false })).status, 400);
  const history = await (await post('/history/list', {})).json();
  assert.deepEqual(history.entries, []);
  assert.deepEqual(history.supportedProfiles, ['ds1tr-gen-v1']);
  // A world that is not in the history has no hint sheet; nothing is made up for an id (or a missing id).
  assert.deepEqual(await (await post('/hints/get', { id: 'not-a-world' })).json(), { ok: false, code: 'HINTS_UNAVAILABLE' });
  assert.deepEqual(await (await post('/hints/get', {})).json(), { ok: false, code: 'HINTS_UNAVAILABLE' });
  // The requests above (an import, a generation stream) have ended, so nothing counts as running any more: the activity ends with the response.
  let after = await (await fetch(`${url}/activity`)).json();
  for (let attempt = 0; attempt < 20 && after.busy; attempt += 1) { await new Promise((resolve) => setTimeout(resolve, 50)); after = await (await fetch(`${url}/activity`)).json(); }
  assert.deepEqual(after, { ok: true, busy: false, active: [] });
  const badPreset = await post('/preset/import', { text: '{}' });
  assert.equal(badPreset.status, 400);
  assert.equal((await badPreset.json()).ok, false);
});
