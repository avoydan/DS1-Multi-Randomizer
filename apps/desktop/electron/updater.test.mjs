import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  AUTO_CHECK_INTERVAL_MS, AUTO_CHECK_RETRY_MS, UPDATE_INVOKE_CHANNELS, UPDATE_STATE_EVENT, UpdateController, UNSET_OWNER,
  isNewerStable, isUpdateSourceConfigured, releaseUrlFor,
} from '../dist-electron/updater.js';
import { normalizeUpdateSettings, migrateSettings } from '../dist-electron/settings.js';

// The updater policy (docs/AUTO_UPDATE.md), tested without Electron, without a network and without GitHub: the real updater, the clock, the settings and the backend are
// replaced by small fakes. Nothing in this file contacts anything.

const SOURCE = { provider: 'github', owner: 'ds1-owner', repo: 'DS1-Multi-Randomizer' };
const NOW = Date.parse('2026-10-10T12:00:00.000Z');

class FakeUpdater {
  autoDownload = true;
  autoInstallOnAppQuit = true;
  allowPrerelease = true;
  allowDowngrade = true;
  feed = null;
  listeners = {};
  checks = 0;
  downloads = 0;
  installs = [];
  result = { isUpdateAvailable: true, updateInfo: { version: '0.37.0', releaseDate: '2026-10-09T10:00:00.000Z' } };
  checkError = null;
  downloadError = null;
  setFeedURL(options) { this.feed = options; }
  on(event, listener) { this.listeners[event] = listener; return this; }
  async checkForUpdates() { this.checks += 1; if (this.checkError) throw this.checkError; return this.result; }
  async downloadUpdate() {
    this.downloads += 1;
    this.listeners['download-progress']?.({ percent: 41.6 });
    if (this.downloadError) throw this.downloadError;
    return ['x.exe'];
  }
  quitAndInstall(isSilent, isForceRunAfter) { this.installs.push({ isSilent, isForceRunAfter }); }
}

function harness(overrides = {}) {
  const updater = new FakeUpdater();
  let stored = { autoCheck: true, lastCheckAt: null, lastCheckOk: false, ...(overrides.prefs ?? {}) };
  const opened = [];
  const logs = [];
  let clock = overrides.now ?? NOW;
  const controller = new UpdateController({
    createUpdater: () => updater,
    currentVersion: overrides.currentVersion ?? '0.36.0',
    mode: overrides.mode ?? 'installed',
    source: overrides.source ?? SOURCE,
    prefs: { get: () => ({ ...stored }), set: async (patch) => { stored = { ...stored, ...patch }; } },
    isOperationActive: overrides.isOperationActive ?? (async () => false),
    openExternal: async (url) => { opened.push(url); },
    now: () => clock,
    log: (level, message, code) => logs.push({ level, message, code }),
  });
  return { controller, updater, opened, logs, prefs: () => stored, advance: (ms) => { clock += ms; } };
}

test('1. a newer version is offered, with its date and the trusted release page', async () => {
  const { controller, updater } = harness();
  const state = await controller.check({ manual: true });
  assert.equal(state.phase, 'available');
  assert.equal(state.availableVersion, '0.37.0');
  assert.equal(state.releaseDate, '2026-10-09T10:00:00.000Z');
  assert.equal(state.releaseUrl, 'https://github.com/ds1-owner/DS1-Multi-Randomizer/releases/tag/v0.37.0');
  assert.equal(updater.downloads, 0, 'nothing is downloaded until the person asks');
});

test('2. the same version is up to date', async () => {
  const { controller, updater } = harness();
  updater.result = { isUpdateAvailable: false, updateInfo: { version: '0.36.0' } };
  assert.equal((await controller.check({ manual: true })).phase, 'upToDate');
});

test('3. an older or equal remote version is ignored, even if the updater claims it is available', async () => {
  const { controller, updater } = harness();
  for (const version of ['0.35.1', '0.36.0', '0.9.9', 'garbage']) {
    updater.result = { isUpdateAvailable: true, updateInfo: { version } };
    const state = await controller.check({ manual: true });
    assert.equal(state.phase, 'upToDate', version);
    assert.equal(state.availableVersion, null, version);
  }
});

test('13. a prerelease is never offered by a stable client (semver and the updater flags)', async () => {
  const { controller, updater } = harness();
  updater.result = { isUpdateAvailable: true, updateInfo: { version: '0.37.0-beta.1' } };
  assert.equal((await controller.check({ manual: true })).phase, 'upToDate');
  assert.equal(updater.allowPrerelease, false, 'allowPrerelease is forced off');
  assert.equal(updater.allowDowngrade, false);
  assert.equal(isNewerStable('0.37.0', '0.36.0'), true);
  assert.equal(isNewerStable('0.37.0-rc.1', '0.36.0'), false);
  assert.equal(isNewerStable('1.0.0', '0.99.99'), true);
  assert.equal(isNewerStable('0.36.0', '0.36.0-rc.1'), true, 'the stable release of a prerelease is newer');
  assert.equal(isNewerStable('0.36.1', '0.36.0'), true);
  assert.equal(isNewerStable('0.10.0', '0.9.0'), true, 'numeric, not alphabetic');
});

test('the updater is configured explicitly: the source, no automatic download, no install on quit', () => {
  const { updater } = harness();
  assert.deepEqual(updater.feed, { provider: 'github', owner: 'ds1-owner', repo: 'DS1-Multi-Randomizer' });
  assert.equal(updater.autoDownload, false);
  assert.equal(updater.autoInstallOnAppQuit, false, 'nothing installs silently when the application closes');
  assert.equal(updater.allowPrerelease, false);
  assert.equal(updater.allowDowngrade, false);
});

test('4. a failed check is non-fatal, is reported with a code, and can be repeated', async () => {
  const { controller, updater, logs } = harness();
  updater.checkError = new Error('getaddrinfo ENOTFOUND github.com');
  const failed = await controller.check({ manual: true });
  assert.equal(failed.phase, 'error');
  assert.equal(failed.error.code, 'CHECK_FAILED');
  assert.match(failed.error.detail, /ENOTFOUND/u);
  assert.ok(logs.some((entry) => entry.code === 'UPDATE_CHECK_FAILED'));
  updater.checkError = null;
  assert.equal((await controller.check({ manual: true })).phase, 'available');
});

test('a technical detail never carries a local path', async () => {
  const { controller, updater } = harness();
  updater.checkError = new Error('EPERM: cannot write C:\\Users\\Someone\\AppData\\Roaming\\x\\update.exe');
  const state = await controller.check({ manual: true });
  assert.doesNotMatch(state.error.detail, /Someone/u);
});

test('6. download progress is reported as a percentage, and 7. the downloaded state waits for the person', async () => {
  const { controller, updater } = harness();
  await controller.check({ manual: true });
  const seen = [];
  controller.subscribe((state) => seen.push([state.phase, state.progress]));
  const done = await controller.download();
  assert.equal(done.phase, 'downloaded');
  assert.equal(done.progress, 100);
  assert.ok(seen.some(([phase, progress]) => phase === 'downloading' && progress === 42), 'the progress event is shown as 42%');
  assert.equal(updater.installs.length, 0, 'a finished download does not restart the application');
});

test('5. a failed download is reported, keeps the version, and can be retried', async () => {
  const { controller, updater } = harness();
  await controller.check({ manual: true });
  updater.downloadError = new Error('sha512 checksum mismatch');
  const failed = await controller.download();
  assert.equal(failed.phase, 'error');
  assert.equal(failed.error.code, 'DOWNLOAD_FAILED');
  assert.equal(failed.availableVersion, '0.37.0');
  updater.downloadError = null;
  assert.equal((await controller.download()).phase, 'downloaded');
});

test('installing requires a finished download and the person: a silent, relaunching install', async () => {
  const { controller, updater } = harness();
  await controller.check({ manual: true });
  const early = await controller.install();
  assert.equal(early.ok, false);
  assert.equal(early.code, 'INVALID_STATE');
  assert.equal(updater.installs.length, 0);
  await controller.download();
  const result = await controller.install();
  assert.equal(result.ok, true);
  assert.equal(result.state.phase, 'installing');
  assert.deepEqual(updater.installs, [{ isSilent: true, isForceRunAfter: true }]);
});

test('8. installing is refused while a generation, Apply or Restore runs, and the application keeps working', async () => {
  let busy = true;
  const { controller, updater } = harness({ isOperationActive: async () => busy });
  await controller.check({ manual: true });
  await controller.download();
  const refused = await controller.install();
  assert.equal(refused.ok, false);
  assert.equal(refused.code, 'OPERATION_ACTIVE');
  assert.equal(refused.state.installBlocked, true);
  assert.equal(refused.state.phase, 'downloaded', 'the downloaded update waits');
  assert.equal(updater.installs.length, 0, 'quitAndInstall was never called');
  busy = false;
  assert.equal((await controller.install()).ok, true);
  assert.equal(controller.getState().installBlocked, false);
});

test('8b. if the backend cannot be asked, the answer is "busy", never "idle"', async () => {
  const { controller, updater } = harness({ isOperationActive: async () => { throw new Error('backend unreachable'); } });
  await controller.check({ manual: true });
  await controller.download();
  assert.equal((await controller.install()).code, 'OPERATION_ACTIVE');
  assert.equal(updater.installs.length, 0);
});

test('9. a manual check ignores the throttle', async () => {
  const stamp = new Date(NOW - 60 * 1000).toISOString();
  const { controller, updater } = harness({ prefs: { lastCheckAt: stamp, lastCheckOk: true } });
  await controller.check({ manual: true });
  assert.equal(updater.checks, 1);
});

test('10. an automatic check respects the throttle: a day after a success, an hour after a failure', async () => {
  const hours = (count) => count * 60 * 60 * 1000;
  // Checked 1 hour ago, successfully: nothing to do.
  let kit = harness({ prefs: { lastCheckAt: new Date(NOW - hours(1)).toISOString(), lastCheckOk: true } });
  await kit.controller.maybeAutoCheck();
  assert.equal(kit.updater.checks, 0);
  // Checked 23 hours ago, successfully: still nothing.
  kit = harness({ prefs: { lastCheckAt: new Date(NOW - hours(23)).toISOString(), lastCheckOk: true } });
  await kit.controller.maybeAutoCheck();
  assert.equal(kit.updater.checks, 0);
  // 25 hours ago: runs.
  kit = harness({ prefs: { lastCheckAt: new Date(NOW - hours(25)).toISOString(), lastCheckOk: true } });
  await kit.controller.maybeAutoCheck();
  assert.equal(kit.updater.checks, 1);
  assert.equal(kit.prefs().lastCheckOk, true);
  assert.equal(kit.prefs().lastCheckAt, new Date(NOW).toISOString());
  // Never checked: runs.
  kit = harness();
  await kit.controller.maybeAutoCheck();
  assert.equal(kit.updater.checks, 1);
  // A failed check is retried after an hour, not after a day.
  kit = harness({ prefs: { lastCheckAt: new Date(NOW - hours(2)).toISOString(), lastCheckOk: false } });
  await kit.controller.maybeAutoCheck();
  assert.equal(kit.updater.checks, 1);
  kit = harness({ prefs: { lastCheckAt: new Date(NOW - 30 * 60 * 1000).toISOString(), lastCheckOk: false } });
  await kit.controller.maybeAutoCheck();
  assert.equal(kit.updater.checks, 0);
  assert.equal(AUTO_CHECK_INTERVAL_MS, 24 * 3600 * 1000);
  assert.equal(AUTO_CHECK_RETRY_MS, 3600 * 1000);
});

test('the throttle is recorded after the check, so a restart does not repeat it', async () => {
  const kit = harness();
  await kit.controller.maybeAutoCheck();
  await kit.controller.maybeAutoCheck();
  assert.equal(kit.updater.checks, 1);
  kit.advance(25 * 3600 * 1000);
  await kit.controller.maybeAutoCheck();
  assert.equal(kit.updater.checks, 2);
});

test('11. with automatic checks off nothing runs by itself, and a manual check still works', async () => {
  const kit = harness({ prefs: { autoCheck: false } });
  assert.equal(kit.controller.getState().autoCheck, false);
  await kit.controller.maybeAutoCheck();
  assert.equal(kit.updater.checks, 0);
  await kit.controller.check({ manual: true });
  assert.equal(kit.updater.checks, 1);
  const on = await kit.controller.setAutoCheck(true);
  assert.equal(on.autoCheck, true);
  assert.equal(kit.prefs().autoCheck, true);
});

test('concurrent checks share one request', async () => {
  const kit = harness();
  await Promise.all([kit.controller.check({ manual: true }), kit.controller.check({ manual: true }), kit.controller.maybeAutoCheck()]);
  assert.equal(kit.updater.checks, 1);
});

test('12. a portable build learns that a version exists but never downloads or installs; it opens the trusted release page', async () => {
  const kit = harness({ mode: 'portable' });
  assert.equal(kit.controller.capability, 'manual');
  const state = await kit.controller.check({ manual: true });
  assert.equal(state.phase, 'available');
  assert.equal(state.capability, 'manual');
  const refused = await kit.controller.download();
  assert.equal(refused.error.code, 'NOT_SUPPORTED');
  assert.equal(kit.updater.downloads, 0);
  assert.deepEqual(await kit.controller.install().then((result) => [result.ok, result.code]), [false, 'NOT_SUPPORTED']);
  assert.equal(kit.updater.installs.length, 0, 'a running portable executable is never replaced');
  assert.equal(await kit.controller.openReleasePage(), true);
  assert.deepEqual(kit.opened, ['https://github.com/ds1-owner/DS1-Multi-Randomizer/releases/tag/v0.37.0']);
});

test('a development run and a build without an update source do not update and never touch the updater', async () => {
  let created = 0;
  for (const [mode, source, reason] of [['development', SOURCE, 'development'], ['installed', { ...SOURCE, owner: UNSET_OWNER }, 'no-update-source']]) {
    const controller = new UpdateController({
      createUpdater: () => { created += 1; return new FakeUpdater(); }, currentVersion: '0.36.0', mode, source,
      prefs: { get: () => ({ autoCheck: true, lastCheckAt: null, lastCheckOk: false }), set: async () => undefined },
      isOperationActive: async () => false, openExternal: async () => undefined,
    });
    assert.equal(controller.capability, 'none');
    assert.equal(controller.getState().phase, 'unsupported');
    assert.equal(controller.getState().unsupportedReason, reason);
    assert.equal((await controller.check({ manual: true })).phase, 'unsupported');
    if (reason === 'no-update-source') assert.equal(await controller.openReleasePage(), false, 'there is no page to open without a source');
  }
  assert.equal(created, 0, 'the real updater is never even created');
});

test('the update source must be a plain GitHub owner and repository, and the release page is built from it, never from release metadata', () => {
  assert.equal(isUpdateSourceConfigured(SOURCE), true);
  assert.equal(isUpdateSourceConfigured({ ...SOURCE, owner: UNSET_OWNER }), false);
  assert.equal(isUpdateSourceConfigured({ ...SOURCE, owner: 'a/b' }), false);
  assert.equal(isUpdateSourceConfigured({ ...SOURCE, repo: '../x' }), false);
  assert.equal(releaseUrlFor(SOURCE, null), 'https://github.com/ds1-owner/DS1-Multi-Randomizer/releases/latest');
  assert.equal(releaseUrlFor(SOURCE, '0.37.0'), 'https://github.com/ds1-owner/DS1-Multi-Randomizer/releases/tag/v0.37.0');
  assert.equal(releaseUrlFor(SOURCE, '0.37.0/../../evil'), 'https://github.com/ds1-owner/DS1-Multi-Randomizer/releases/latest');
  assert.equal(releaseUrlFor({ ...SOURCE, owner: UNSET_OWNER }, '0.37.0'), null);
});

test('a check does not replace a download in progress or a finished one', async () => {
  const kit = harness();
  await kit.controller.check({ manual: true });
  await kit.controller.download();
  const checksBefore = kit.updater.checks;
  assert.equal((await kit.controller.check({ manual: true })).phase, 'downloaded');
  assert.equal(kit.updater.checks, checksBefore);
});

test('an installer that fails to start is reported and the application stays usable', async () => {
  const kit = harness();
  await kit.controller.check({ manual: true });
  await kit.controller.download();
  await kit.controller.install();
  kit.updater.listeners.error(new Error('spawn UNKNOWN'));
  const state = kit.controller.getState();
  assert.equal(state.phase, 'error');
  assert.equal(state.error.code, 'INSTALL_FAILED');
});

test('14. the renderer gets six invoke channels and one event, and the preload exposes nothing else about updates', () => {
  assert.deepEqual([...UPDATE_INVOKE_CHANNELS], ['updates:get', 'updates:check', 'updates:download', 'updates:install', 'updates:open-release', 'updates:set-auto-check']);
  assert.equal(UPDATE_STATE_EVENT, 'updates:state');
  const preload = readFileSync(fileURLToPath(new URL('./preload.cts', import.meta.url)), 'utf8');
  const used = [...preload.matchAll(/'(updates:[a-z-]+)'/gu)].map((match) => match[1]);
  assert.deepEqual([...new Set(used)].sort(), [...UPDATE_INVOKE_CHANNELS, UPDATE_STATE_EVENT].sort());
  assert.doesNotMatch(preload, /electron-updater|autoUpdater|quitAndInstall|require\(/u, 'the updater object never reaches the page');
  const main = readFileSync(fileURLToPath(new URL('./main.ts', import.meta.url)), 'utf8');
  const handled = [...main.matchAll(/ipcMain\.handle\('(updates:[a-z-]+)'/gu)].map((match) => match[1]);
  assert.deepEqual(handled.sort(), [...UPDATE_INVOKE_CHANNELS].sort(), 'every channel has a handler and no other updates channel exists');
  for (const channel of UPDATE_INVOKE_CHANNELS) assert.match(main, new RegExp(`ipcMain\\.handle\\('${channel}', (async )?\\(event`, 'u'), `${channel} receives the event to check the sender`);
  assert.match(main, /fromMainWindow\(event\)/u);
});

test('the settings keep only valid update preferences', () => {
  assert.deepEqual(normalizeUpdateSettings({ autoCheck: false, lastCheckAt: '2026-10-10T10:00:00Z', lastCheckOk: true }), { autoCheck: false, lastCheckAt: '2026-10-10T10:00:00.000Z', lastCheckOk: true });
  assert.deepEqual(normalizeUpdateSettings({ autoCheck: true }), null, 'the default is not stored');
  assert.deepEqual(normalizeUpdateSettings({ lastCheckAt: 'yesterday', token: 'x' }), null);
  assert.deepEqual(normalizeUpdateSettings('x'), null);
  assert.deepEqual(migrateSettings({ gamePath: 'D:\\Games\\DSR', updates: { autoCheck: false, extra: 1 } }).settings, { gamePath: 'D:\\Games\\DSR', updates: { autoCheck: false } });
  assert.deepEqual(migrateSettings({ updates: 'broken' }).settings, {});
});

test('the explicit update source in the code and in electron-builder.yml are the same three values', () => {
  const code = readFileSync(fileURLToPath(new URL('./updateSource.ts', import.meta.url)), 'utf8');
  const yaml = readFileSync(fileURLToPath(new URL('../electron-builder.yml', import.meta.url)), 'utf8');
  const field = (text, name) => new RegExp(`${name}:\\s*'?([A-Za-z0-9._-]+)'?`, 'u').exec(text.slice(text.search(/provider:/u)))?.[1];
  for (const name of ['provider', 'owner', 'repo']) assert.equal(field(code, name), field(yaml, name), name);
  assert.equal(field(yaml, 'provider'), 'github');
});

test('15. the update texts exist in English and Polish for every state, every error code and every unsupported reason', () => {
  const read = (language) => JSON.parse(readFileSync(fileURLToPath(new URL(`../src/locales/${language}/app.json`, import.meta.url)), 'utf8')).update;
  const en = read('en');
  const pl = read('pl');
  const flat = (value, prefix = '') => Object.entries(value).flatMap(([key, child]) => (typeof child === 'object' ? flat(child, `${prefix}${key}.`) : [`${prefix}${key}`]));
  assert.deepEqual(flat(pl).sort(), flat(en).sort(), 'the same keys in both languages');
  for (const code of ['CHECK_FAILED', 'DOWNLOAD_FAILED', 'INSTALL_FAILED', 'OPERATION_ACTIVE', 'INVALID_STATE', 'NOT_SUPPORTED']) for (const text of [en.errors[code], pl.errors[code]]) assert.ok(text && text.length > 10, code);
  for (const reason of ['development', 'no-update-source']) for (const text of [en.unsupported[reason], pl.unsupported[reason]]) assert.ok(text && text.length > 10, reason);
  // The required wording, and the product name is not translated.
  for (const key of ['check', 'checking', 'upToDate', 'available', 'download', 'downloading', 'ready', 'restart', 'later', 'whatsNew', 'autoCheck', 'blocked', 'portableNote']) assert.ok(en[key] && pl[key], key);
  assert.match(en.available, /DS1 Multi Randomizer/u);
  assert.match(pl.available, /DS1 Multi Randomizer/u);
  assert.match(en.restart, /Restart and update/u);
  assert.match(en.blocked, /Finish the current operation/u);
  assert.notEqual(en.check, pl.check);
});
