import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import test from 'node:test';
import { CursorHelper } from '../dist-electron/cursorHelper.js';

function childProcess() {
  const child = new EventEmitter();
  child.exitCode = null;
  child.stdin = { end: (text) => { child.stopText = text; queueMicrotask(() => { child.exitCode = 0; child.emit('exit', 0); }); } };
  child.stderr = new EventEmitter();
  child.kill = () => { child.killed = true; child.exitCode = 1; child.emit('exit', 1); };
  return child;
}

test('does not start a Windows helper on other platforms', async () => {
  let starts = 0;
  const helper = new CursorHelper({ platform: 'linux', helperPath: () => 'helper.exe', exists: async () => true, spawn: () => { starts++; throw new Error('unexpected'); }, log: () => {} });
  assert.equal(await helper.setEnabled(true), false);
  assert.equal(starts, 0);
});

test('starts only when enabled and stops the helper through its private stdin channel', async () => {
  const child = childProcess();
  const calls = [];
  const helper = new CursorHelper({ platform: 'win32', helperPath: () => 'GameCursorHelper.exe', exists: async () => true, spawn: (...args) => { calls.push(args); return child; }, log: () => {} });
  assert.equal(await helper.setEnabled(false), false);
  assert.equal(calls.length, 0);
  assert.equal(await helper.setEnabled(true), true);
  assert.equal(calls.length, 1);
  assert.deepEqual(calls[0].slice(0, 2), ['GameCursorHelper.exe', []]);
  assert.equal(calls[0][2].windowsHide, true);
  await helper.setEnabled(false);
  assert.equal(child.stopText, 'stop\n');
});

test('missing helper is reported and does not spawn', async () => {
  const messages = [];
  const helper = new CursorHelper({ platform: 'win32', helperPath: () => 'missing.exe', exists: async () => false, spawn: () => { throw new Error('unexpected'); }, log: (message) => messages.push(message) });
  assert.equal(await helper.setEnabled(true), false);
  assert.match(messages[0], /missing\.exe/u);
});

test('a later disable cancels an in-flight enable before it can spawn', async () => {
  let release;
  let starts = 0;
  const helper = new CursorHelper({ platform: 'win32', helperPath: () => 'helper.exe', exists: () => new Promise((resolve) => { release = resolve; }), spawn: () => { starts++; throw new Error('unexpected'); }, log: () => {} });
  const enabling = helper.setEnabled(true);
  await Promise.resolve();
  const disabling = helper.setEnabled(false);
  release(true);
  assert.equal(await disabling, false);
  assert.equal(await enabling, false);
  assert.equal(starts, 0);
});
