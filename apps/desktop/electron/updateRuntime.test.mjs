import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  createInertUpdateOperations,
  initializeGithubUpdates,
  shouldScheduleUpdateChecks,
  unavailableUpdateState,
} from '../dist-electron/updateRuntime.js';

test('only a positively identified GitHub distribution loads the updater bootstrap', async () => {
  let initialized = 0;
  const initialize = async () => { initialized += 1; return 'github-updater'; };
  assert.equal(await initializeGithubUpdates('nexus', initialize), null);
  assert.equal(await initializeGithubUpdates('unavailable', initialize), null);
  assert.equal(initialized, 0);
  assert.equal(await initializeGithubUpdates('github', initialize), 'github-updater');
  assert.equal(initialized, 1);
  assert.equal(shouldScheduleUpdateChecks('nexus', true), false);
  assert.equal(shouldScheduleUpdateChecks('unavailable', true), false);
  assert.equal(shouldScheduleUpdateChecks('github', false), false);
  assert.equal(shouldScheduleUpdateChecks('github', true), true);
});

test('Nexus update state and every update IPC fallback remain local and preserve autoCheck', async () => {
  const state = unavailableUpdateState({ distribution: 'nexus', currentVersion: '0.39.0', autoCheck: false, nexusPageUrl: null });
  const inert = createInertUpdateOperations(state);
  const attemptedRequests = [];
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async (...args) => { attemptedRequests.push(args); throw new Error('network must not be used'); };
  try {
    assert.equal((await inert.getState()).phase, 'unsupported');
    assert.equal((await inert.check()).distribution, 'nexus');
    assert.equal((await inert.download()).distribution, 'nexus');
    assert.deepEqual(await inert.install().then(({ ok, code }) => [ok, code]), [false, 'NOT_SUPPORTED']);
    assert.equal((await inert.setAutoCheck(true)).autoCheck, false, 'the stored preference is ignored and not rewritten');
    assert.equal(await inert.openReleasePage(), false);
    assert.deepEqual(attemptedRequests, []);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test('the Electron main process has no eager updater or GitHub API imports and gates startup timers', () => {
  const read = (path) => readFileSync(fileURLToPath(new URL(path, import.meta.url)), 'utf8');
  const main = read('./main.ts');
  const ui = read('../src/ui/UpdatePanel.tsx');
  assert.doesNotMatch(main, /^\s*import\s+(?!type\b).*?(?:electron-updater|githubReleaseApi|updateSource|updater\.js)/mu);
  assert.match(main, /initializeGithubUpdates\(buildDistribution, async \(\) =>/u);
  assert.match(main, /shouldScheduleUpdateChecks\(buildDistribution, updates !== null\)/u);
  assert.match(main, /updates:\s*'nexus'|buildDistribution !== 'nexus'/u);
  assert.match(ui, /state\?\.distribution === 'nexus'/u);
  assert.match(ui, /state\.distribution === 'github' && state\.phase !== 'unsupported'/u);
  for (const operation of ['check', 'download', 'install', 'setAutoCheck']) assert.match(main, new RegExp(`inertUpdates\\?\\.${operation}|inertUpdates\\?\\.${operation}\\(`, 'u'));
});
