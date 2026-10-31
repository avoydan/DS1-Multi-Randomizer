import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { CursorHelper, createGameProcessProbe } from '../dist-electron/cursorHelper.js';

const watcherSource = await readFile(new URL('../../../tools/game-cursor-helper/Program.cs', import.meta.url), 'utf8');
const mainSource = await readFile(new URL('./main.ts', import.meta.url), 'utf8');

function childProcess() {
  const child = new EventEmitter();
  child.exitCode = null;
  child.unref = () => { child.unreferenced = true; };
  child.kill = () => { child.killed = true; child.exitCode = 1; child.emit('exit', 1); };
  child.endByItself = (code = 0) => { child.exitCode = code; child.emit('exit', code); };
  return child;
}

/** A helper with a fake game, fake timers and a spawn log. */
function harness({ platform = 'win32', gameRunning = false, exists = async () => true } = {}) {
  const state = { game: gameRunning, spawns: [], children: [], checks: 0, logs: [], timers: [] };
  const helper = new CursorHelper({
    platform,
    ownerPid: 420,
    helperPath: () => 'GameCursorHelper.exe',
    exists,
    spawn: (...args) => { const child = childProcess(); state.spawns.push(args); state.children.push(child); return child; },
    log: (message) => state.logs.push(message),
    isGameRunning: async () => { state.checks++; return state.game; },
    schedule: (callback, delayMs) => { const timer = { callback, delayMs, cancelled: false, fired: false }; state.timers.push(timer); return timer; },
    cancel: (timer) => { timer.cancelled = true; },
    idlePollMs: 10_000,
    activePollMs: 15_000,
  });
  /** Fires the one live (armed, not cancelled, not fired) timer and waits for the resulting check to finish. */
  state.tick = async () => {
    const live = state.timers.filter((timer) => !timer.cancelled && !timer.fired);
    assert.equal(live.length, 1, 'exactly one poll timer is armed');
    live[0].fired = true;
    live[0].callback();
    await helper.whenIdle();
  };
  state.live = () => state.timers.filter((timer) => !timer.cancelled && !timer.fired);
  return { helper, state };
}

test('does not start a Windows helper, or look for the game, on other platforms', async () => {
  const { helper, state } = harness({ platform: 'linux', gameRunning: true });
  assert.equal(await helper.setEnabled(true), false);
  assert.equal(state.spawns.length, 0);
  assert.equal(state.checks, 0);
  assert.equal(state.live().length, 0);
});

test('enabling the feature while the game is not running starts no helper and installs no hook; it only arms a slow check', async () => {
  const { helper, state } = harness({ gameRunning: false });
  assert.equal(await helper.setEnabled(true, 'F10'), false);
  assert.equal(state.spawns.length, 0);
  assert.equal(state.live().length, 1);
  assert.equal(state.live()[0].delayMs, 10_000);
});

test('starting the application with the saved preference on behaves like enabling: the helper stays stopped until the game runs', async () => {
  const { helper, state } = harness({ gameRunning: false });
  await helper.setEnabled(true, 'Ctrl+Shift+F8'); // what app.whenReady does for a saved freeCursorInMenus
  assert.equal(state.spawns.length, 0);
  assert.match(mainSource, /if \(settings\.freeCursorInMenus === true\) await cursorHelper\.setEnabled\(true, settings\.freeCursorShortcut \?\? 'F10'\);/u);
  assert.doesNotMatch(mainSource, /spawn\([^)]*GameCursorHelper/u, 'main.ts never spawns the helper itself');
  // The game appears: exactly one helper with the saved shortcut and the owner pid.
  state.game = true;
  await state.tick();
  assert.equal(state.spawns.length, 1);
  assert.deepEqual(state.spawns[0].slice(0, 2), ['GameCursorHelper.exe', ['Ctrl+Shift+F8', '420']]);
  assert.equal(state.spawns[0][2].detached, true);
  assert.equal(state.spawns[0][2].windowsHide, true);
  assert.equal(state.children[0].unreferenced, true);
});

test('at most one helper per game session, however many checks happen while it runs', async () => {
  const { helper, state } = harness({ gameRunning: true });
  assert.equal(await helper.setEnabled(true), true);
  for (let i = 0; i < 4; i++) await state.tick();
  assert.equal(state.spawns.length, 1);
  assert.equal(state.live()[0].delayMs, 15_000, 'a running helper is only re-checked on the safety interval');
});

test('enabling while the game is already running starts the helper for that session', async () => {
  const { helper, state } = harness({ gameRunning: true });
  assert.equal(await helper.setEnabled(true), true);
  assert.equal(state.spawns.length, 1);
});

test('the helper is stopped after the game exits, and a later game session gets a new one', async () => {
  const { helper, state } = harness({ gameRunning: true });
  await helper.setEnabled(true);
  assert.equal(state.spawns.length, 1);
  // The game closes; the helper ends by itself (it follows the game) and the next check sees no game.
  state.game = false;
  state.children[0].endByItself(0);
  await state.tick();
  assert.equal(state.spawns.length, 1);
  assert.equal(state.live()[0].delayMs, 10_000, 'back to the slow idle check');
  // The next game session gets a fresh helper.
  state.game = true;
  await state.tick();
  assert.equal(state.spawns.length, 2, 'a new game session starts one new helper');
  state.game = false;
  await state.tick();
  assert.equal(state.children[1].killed, true, 'a helper that outlived the game is stopped');
  assert.equal(state.live().length, 1);
});

test('a helper that ends while the game is still running is not respawned by polling', async () => {
  const { helper, state } = harness({ gameRunning: true });
  await helper.setEnabled(true);
  state.children[0].endByItself(1); // crashed or a duplicate instance exited at once
  for (let i = 0; i < 3; i++) await state.tick();
  assert.equal(state.spawns.length, 1, 'no spawn loop');
  assert.match(state.logs.join('\n'), /exited with code 1/u);
  state.game = false; await state.tick();
  state.game = true; await state.tick();
  assert.equal(state.spawns.length, 2, 'the next game session may start a helper again');
});

test('disabling stops the helper promptly and stops all checks', async () => {
  const { helper, state } = harness({ gameRunning: true });
  await helper.setEnabled(true);
  const checksBefore = state.checks;
  assert.equal(await helper.setEnabled(false), false);
  assert.equal(state.children[0].killed, true);
  assert.equal(state.live().length, 0);
  assert.equal(state.checks, checksBefore, 'disabling does not even look for the game');
});

test('disabling while the game check is still in flight never spawns', async () => {
  let release;
  const state = { spawns: 0 };
  const helper = new CursorHelper({
    platform: 'win32', ownerPid: 1, helperPath: () => 'helper.exe', exists: async () => true,
    spawn: () => { state.spawns++; throw new Error('unexpected'); }, log: () => {},
    isGameRunning: () => new Promise((resolve) => { release = resolve; }),
    schedule: () => ({}), cancel: () => {},
  });
  const enabling = helper.setEnabled(true);
  await new Promise((resolve) => setImmediate(resolve));
  const disabling = helper.setEnabled(false);
  release(true);
  assert.equal(await enabling, false);
  assert.equal(await disabling, false);
  assert.equal(state.spawns, 0);
});

test('repeated enable / disable never leaves more than one poll timer armed', async () => {
  const { helper, state } = harness({ gameRunning: false });
  for (let i = 0; i < 5; i++) { await helper.setEnabled(true); await helper.setEnabled(false); }
  await helper.setEnabled(true);
  await helper.setEnabled(true);
  assert.equal(state.live().length, 1);
});

test('a missing helper file is reported once and not retried every check', async () => {
  const { helper, state } = harness({ gameRunning: true, exists: async () => false });
  assert.equal(await helper.setEnabled(true), false);
  for (let i = 0; i < 3; i++) await state.tick();
  assert.equal(state.spawns.length, 0);
  assert.equal(state.logs.filter((message) => /GameCursorHelper\.exe/u.test(message)).length, 1);
});

test('a changed shortcut restarts a running helper and only stores the value when the game is not running', async () => {
  const running = harness({ gameRunning: true });
  assert.equal(await running.helper.setEnabled(true, 'F10'), true);
  assert.equal(await running.helper.setShortcut('Ctrl+Shift+F8'), true);
  assert.equal(running.state.spawns.length, 2);
  assert.equal(running.state.children[0].killed, true);
  assert.deepEqual(running.state.spawns[1][1], ['Ctrl+Shift+F8', '420']);

  const idle = harness({ gameRunning: false });
  await idle.helper.setEnabled(true, 'F10');
  assert.equal(await idle.helper.setShortcut('F9'), true);
  assert.equal(idle.state.spawns.length, 0);
  idle.state.game = true;
  await idle.state.tick();
  assert.deepEqual(idle.state.spawns[0][1], ['F9', '420']);
});

test('the process probe answers from one tasklist call without depending on the display language', async () => {
  const answer = (error, stdout) => (file, args, options, callback) => { answer.last = { file, args, options }; callback(error, stdout); };
  const logs = [];
  assert.equal(await createGameProcessProbe((m) => logs.push(m), answer(null, '"DarkSoulsRemastered.exe","4242","Console","1","1,234,567 K"\r\n'))(), true);
  assert.deepEqual(answer.last.args, ['/FI', 'IMAGENAME eq DarkSoulsRemastered.exe', '/FO', 'CSV', '/NH']);
  assert.equal(answer.last.options.windowsHide, true);
  assert.match(answer.last.file, /tasklist\.exe$/iu);
  assert.equal(await createGameProcessProbe(() => {}, answer(null, 'INFO: No tasks are running which match the specified criteria.\r\n'))(), false);
  assert.equal(await createGameProcessProbe(() => {}, answer(null, 'INFORMACJA: Brak uruchomionych zadan spelniajacych podane kryteria.\r\n'))(), false);
  assert.equal(await createGameProcessProbe(() => {}, answer(null, '"DarkSoulsRemasteredLauncher.exe","1"\r\n'))(), false);
  const failing = createGameProcessProbe((m) => logs.push(m), answer(new Error('spawn failed'), ''));
  assert.equal(await failing(), false);
  assert.equal(await failing(), false);
  assert.equal(logs.filter((m) => /game process check failed/u.test(m)).length, 1, 'a persistent failure is logged once');
});

test('the helper checks for the game before it installs the keyboard hook, runs one instance, and follows the game', () => {
  assert.match(watcherSource, /WH_KEYBOARD_LL/u);
  const guard = watcherSource.indexOf('if (!GameProcessIsRunning()) return 0;');
  const mutex = watcherSource.indexOf('new Mutex(true, SingleInstanceName');
  const hookStart = watcherSource.indexOf('shortcut.Start();');
  assert.ok(mutex > 0 && guard > mutex && hookStart > guard, 'single-instance check, then game check, then the hook');
  assert.match(watcherSource, /MonitorGameLifetimeAsync\(shortcut, ownerPid\)/u);
});

test('the configured hotkey selects a recent external window and helper lifetime follows the game', () => {
  assert.match(watcherSource, /ShouldTrigger\(shortcut, key\.VirtualKey, message, Program\.IsDsrWindow\(foreground, requireVisible: true\)/u);
  assert.match(watcherSource, /TrackForeground\(GetForegroundWindow\(\)\)/u);
  assert.match(watcherSource, /ChooseTargetWindow\(IntPtr gameWindow\)/u);
  assert.match(watcherSource, /_ownerPid > 0 && processId == \(uint\)_ownerPid/u);
  assert.match(watcherSource, /SetForegroundWindow\(target\)/u);
  assert.doesNotMatch(watcherSource, /SendAltTab|SendInput|VK_ESCAPE|EscapeFocusHandoff/u);
});
