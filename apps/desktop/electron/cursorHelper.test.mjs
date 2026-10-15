import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { CursorHelper } from '../dist-electron/cursorHelper.js';

const watcherSource = await readFile(new URL('../../../tools/game-cursor-helper/Program.cs', import.meta.url), 'utf8');

function childProcess() {
  const child = new EventEmitter();
  child.exitCode = null;
  child.unref = () => { child.unreferenced = true; };
  child.kill = () => { child.killed = true; child.exitCode = 1; child.emit('exit', 1); };
  return child;
}

test('does not start a Windows helper on other platforms', async () => {
  let starts = 0;
  const helper = new CursorHelper({ platform: 'linux', helperPath: () => 'helper.exe', exists: async () => true, spawn: () => { starts++; throw new Error('unexpected'); }, log: () => {} });
  assert.equal(await helper.setEnabled(true), false);
  assert.equal(starts, 0);
});

test('starts a detached helper only when enabled and stops it when disabled', async () => {
  const child = childProcess();
  const calls = [];
  const helper = new CursorHelper({ platform: 'win32', ownerPid: 420, helperPath: () => 'GameCursorHelper.exe', exists: async () => true, spawn: (...args) => { calls.push(args); return child; }, log: () => {} });
  assert.equal(await helper.setEnabled(false), false);
  assert.equal(calls.length, 0);
  assert.equal(await helper.setEnabled(true), true);
  assert.equal(calls.length, 1);
  assert.deepEqual(calls[0].slice(0, 2), ['GameCursorHelper.exe', ['F10', '420']]);
  assert.equal(calls[0][2].detached, true);
  assert.equal(calls[0][2].stdio, 'ignore');
  assert.equal(calls[0][2].windowsHide, true);
  assert.equal(child.unreferenced, true);
  await helper.setEnabled(false);
  assert.equal(child.killed, true);
});

test('missing helper is reported and does not spawn', async () => {
  const messages = [];
  const helper = new CursorHelper({ platform: 'win32', ownerPid: 1, helperPath: () => 'missing.exe', exists: async () => false, spawn: () => { throw new Error('unexpected'); }, log: (message) => messages.push(message) });
  assert.equal(await helper.setEnabled(true), false);
  assert.match(messages[0], /missing\.exe/u);
});

test('a later disable cancels an in-flight enable before it can spawn', async () => {
  let release;
  let signalExistsStarted;
  const existsStarted = new Promise((resolve) => { signalExistsStarted = resolve; });
  let starts = 0;
  const helper = new CursorHelper({ platform: 'win32', ownerPid: 1, helperPath: () => 'helper.exe', exists: () => new Promise((resolve) => { release = resolve; signalExistsStarted(); }), spawn: () => { starts++; throw new Error('unexpected'); }, log: () => {} });
  const enabling = helper.setEnabled(true);
  await existsStarted;
  const disabling = helper.setEnabled(false);
  release(true);
  assert.equal(await disabling, false);
  assert.equal(await enabling, false);
  assert.equal(starts, 0);
});

test('the configured hotkey selects a recent external window and helper lifetime follows the game', () => {
  assert.match(watcherSource, /WH_KEYBOARD_LL/u);
  assert.match(watcherSource, /ShouldTrigger\(shortcut, key\.VirtualKey, message, Program\.IsDsrWindow\(foreground, requireVisible: true\)/u);
  assert.match(watcherSource, /TrackForeground\(GetForegroundWindow\(\)\)/u);
  assert.match(watcherSource, /ChooseTargetWindow\(IntPtr gameWindow\)/u);
  assert.match(watcherSource, /_ownerPid > 0 && processId == \(uint\)_ownerPid/u);
  assert.match(watcherSource, /MonitorGameLifetimeAsync\(shortcut, ownerPid\)/u);
  assert.match(watcherSource, /SetForegroundWindow\(target\)/u);
  assert.doesNotMatch(watcherSource, /SendAltTab|SendInput|VK_ESCAPE|EscapeFocusHandoff/u);
});

test('a changed shortcut restarts the detached helper with the new setting', async () => {
  const children = [childProcess(), childProcess()];
  let starts = 0;
  const calls = [];
  const helper = new CursorHelper({ platform: 'win32', ownerPid: 420, helperPath: () => 'GameCursorHelper.exe', exists: async () => true, spawn: (...args) => { calls.push(args); return children[starts++]; }, log: () => {} });
  assert.equal(await helper.setEnabled(true, 'F10'), true);
  assert.equal(await helper.setShortcut('Ctrl+Shift+F8'), true);
  assert.equal(starts, 2);
  assert.equal(children[0].killed, true);
  assert.deepEqual(calls[1][1], ['Ctrl+Shift+F8', '420']);
  await helper.setEnabled(false);
});
