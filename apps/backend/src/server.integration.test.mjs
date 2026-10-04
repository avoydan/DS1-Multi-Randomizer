import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

// Independent of the caller's cwd: `npm test` runs from apps/backend.
const backendRoot = fileURLToPath(new URL('..', import.meta.url));

test('desktop API exposes typed state and generation failure without touching a game', async (t) => {
  const stateRoot = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-api-'));
  const port = 45000 + Math.floor(Math.random() * 10000);
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
  const generation = await post('/runs/generate', { gamePath, masterSeed: '' });
  assert.equal(generation.status, 400);
  assert.equal((await generation.json()).error, 'Invalid configuration');
  const badPreset = await post('/preset/import', { text: '{}' });
  assert.equal(badPreset.status, 400);
  assert.equal((await badPreset.json()).ok, false);
});
