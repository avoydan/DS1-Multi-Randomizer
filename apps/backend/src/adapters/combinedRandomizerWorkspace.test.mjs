import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import {
  planCombinedRandomizerWorkspace,
  sanitizeUpstreamSeed,
} from '../../dist/adapters/combinedRandomizerWorkspace.js';

const gamePath = 'C:\\games\\Dark Souls Remastered';
const workspacePath = 'C:\\workspaces\\run-1';
const combinedSeed = 'combined-seed';

test('sanitizeUpstreamSeed replaces characters outside [\\w.-]', () => {
  assert.equal(sanitizeUpstreamSeed('abc123._-'), 'abc123._-');
  assert.equal(sanitizeUpstreamSeed('a b/c:d'), 'a_b_c_d');
});

test('plans catalog, seed directory, and placements paths under the workspace', () => {
  const result = planCombinedRandomizerWorkspace({ gamePath, workspacePath, combinedSeed });

  assert.equal(result.ok, true);
  const { plan } = result;
  assert.equal(plan.gameDirectory, path.resolve(gamePath));
  assert.equal(plan.workspaceDirectory, path.resolve(workspacePath));
  assert.equal(plan.catalogPath, path.join(path.resolve(workspacePath), 'dsr-catalog.json'));
  assert.equal(plan.seedDirectory, path.join(path.resolve(workspacePath), 'seed_combined-seed'));
  assert.equal(
    plan.randomizerPlacementsPath,
    path.join(path.resolve(workspacePath), 'seed_combined-seed', 'randomizer.json'),
  );
});

test('sanitizes an unsafe seed before using it in the seed directory name', () => {
  const result = planCombinedRandomizerWorkspace({
    gamePath,
    workspacePath,
    combinedSeed: 'weird seed/name',
  });

  assert.equal(result.ok, true);
  assert.equal(
    result.plan.seedDirectory,
    path.join(path.resolve(workspacePath), 'seed_weird_seed_name'),
  );
});

test('builds scan args using only workspace/output paths, never install or restore', () => {
  const result = planCombinedRandomizerWorkspace({ gamePath, workspacePath, combinedSeed });

  assert.equal(result.ok, true);
  const { plan } = result;
  assert.deepEqual(plan.scanArgs, ['scan', '--game', plan.gameDirectory, '--output', plan.catalogPath]);
  assert.deepEqual(plan.patchEnemiesArgs, [
    'patch-enemies',
    '--game', plan.gameDirectory,
    '--catalog', plan.catalogPath,
    '--placements', plan.randomizerPlacementsPath,
    '--output', plan.seedDirectory,
  ]);
  for (const args of [plan.scanArgs, plan.patchEnemiesArgs]) {
    assert.ok(!args.includes('install'), 'command args must never include install');
    assert.ok(!args.includes('restore'), 'command args must never include restore');
  }
});

test('the shared event owner mode (legacy-style sampling) is asked of the scan only, and the classic permutation never gets it', () => {
  const classic = planCombinedRandomizerWorkspace({ gamePath, workspacePath, combinedSeed });
  const sampling = planCombinedRandomizerWorkspace({ gamePath, workspacePath, combinedSeed, sharedEventOwners: true });
  assert.equal(classic.ok && sampling.ok, true);
  assert.deepEqual(sampling.plan.scanArgs, [...classic.plan.scanArgs, '--event-owners', 'shared']);
  assert.deepEqual(sampling.plan.patchEnemiesArgs, classic.plan.patchEnemiesArgs, 'the patch step reads the mode from the catalog it was given');
  assert.ok(!classic.plan.scanArgs.includes('--event-owners'), 'a classic world scans exactly as before');
});

test('is deterministic for the same request', () => {
  const request = { gamePath, workspacePath, combinedSeed };
  const first = planCombinedRandomizerWorkspace(request);
  const second = planCombinedRandomizerWorkspace(request);

  assert.deepEqual(first, second);
});

test('rejects a workspace path equal to the game directory', () => {
  const result = planCombinedRandomizerWorkspace({
    gamePath,
    workspacePath: gamePath,
    combinedSeed,
  });

  assert.deepEqual(result, {
    ok: false,
    code: 'WORKSPACE_INSIDE_GAME_DIRECTORY',
    message:
      'The workspace directory must not be the live game directory or a path inside it. '
      + 'Generation must never write into the live Dark Souls Remastered installation.',
  });
});

test('rejects a workspace path nested inside the game directory', () => {
  const result = planCombinedRandomizerWorkspace({
    gamePath,
    workspacePath: path.join(gamePath, 'mod-output'),
    combinedSeed,
  });

  assert.equal(result.ok, false);
  assert.equal(result.code, 'WORKSPACE_INSIDE_GAME_DIRECTORY');
});
