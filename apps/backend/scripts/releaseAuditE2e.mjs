// Release audit (Task 16): the production path on real-format game data.
//
// pristine import -> generateRun (combined Items + Enemies from the snapshot)
// -> deterministic rerun -> activate -> restore, all through the same
// GameStateAuthority and generation pipeline the server uses.
//
// The live installation is only read. A reconstruction of its managed roots
// (legacy `X.bak` moved over `X`, as in Task 8) in the OS temp directory is
// the "game" that gets imported, activated and restored. The backend state
// root is a temp directory too. Live read set hashes must be identical after
// the run. Game-derived output never enters the repository.
//
// Not part of `npm test`: needs a real installation, a built DsrDataTool and
// a .NET 8 runtime.
//
// Usage (after `npm run build -w @ds1tr/backend`):
//   node apps/backend/scripts/releaseAuditE2e.mjs "<live DSR directory>"
//   DS1TR_E2E_KEEP=1 keeps the temp directory.

import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { cp, mkdtemp, readdir, rename, rm, stat } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';

const livePath = process.argv[2] ?? process.env.DS1TR_E2E_GAME_PATH;
if (!livePath) {
  console.error('Usage: releaseAuditE2e.mjs <live DSR directory>');
  process.exit(2);
}

let failed = false;
function check(name, condition, detail = '') {
  if (!condition) failed = true;
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${name}${detail ? ` — ${detail}` : ''}`);
}

async function hashFile(file) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(file)) hash.update(chunk);
  return hash.digest('hex');
}

async function listTree(root, relative) {
  const files = [];
  let entries;
  try { entries = await readdir(path.join(root, relative), { withFileTypes: true }); } catch { return files; }
  for (const entry of entries) {
    const child = `${relative}/${entry.name}`;
    if (entry.isDirectory()) files.push(...await listTree(root, child));
    else files.push(child);
  }
  return files;
}

/** Relative path -> sha256 for the executable and every managed root. */
async function hashRoots(root) {
  const result = new Map([[executableName, await hashFile(path.join(root, executableName))]]);
  for (const managed of managedRoots) {
    for (const relative of await listTree(root, managed)) result.set(relative, await hashFile(path.join(root, relative)));
  }
  return result;
}

function sameMap(a, b) {
  if (a.size !== b.size) return false;
  for (const [k, v] of a) if (b.get(k) !== v) return false;
  return true;
}

const config = (gamePath) => ({
  gamePath,
  masterSeed: 'release-audit-16',
  items: {
    enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true,
    guaranteeEarlyWeapon: true, balancedEarlyLoot: true, randomizeStartingClass: true,
    randomizeStartingEquipment: true, randomizeGifts: true, randomizeEnemyDrops: true,
    guaranteedEnemyDrops: true, randomizeShops: true,
  },
  enemies: { enabled: true, randomizeRegularEnemies: true, randomizeBosses: true, scaling: 'area' },
  fog: {
    enabled: false, traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: false,
    minorPvpFogs: false, lordvesselGates: false, randomStart: false, disconnectedFogs: false,
    noFallBedOfChaos: false, scaling: true, glitchedLogic: false, requireLordSouls: false,
    pacifistMode: false, language: 'ENGLISH',
  },
  output: { includeDlc: true, generateSpoilerLog: true },
});

const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-audit-'));
const game = path.join(temp, 'game');
const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
const deps = { getPristineSource: (gamePath) => authority.getPristineSource(gamePath) };

try {
  const liveBefore = await hashRoots(livePath);
  console.log(`live read set: ${liveBefore.size} files`);

  // Reconstruct the managed roots with legacy backups restored.
  await cp(path.join(livePath, executableName), path.join(game, executableName));
  for (const managed of managedRoots) {
    await cp(path.join(livePath, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
  }
  for (const managed of managedRoots) {
    for (const relative of await listTree(game, managed)) {
      if (!/\.bak$/i.test(relative)) continue;
      await rename(path.join(game, relative), path.join(game, relative.slice(0, -4)));
    }
  }
  const reconstructed = await hashRoots(game);

  const imported = await authority.importPristine(game);
  check('pristine import', imported.ok, imported.ok ? `${imported.snapshot.files.length} files` : imported.code);
  check('snapshot includes paramdef', imported.ok
    && imported.snapshot.files.some((file) => file.path === 'paramdef/paramdef.paramdefbnd.dcx'));
  check('state vanilla after import', (await authority.detectState(game)).state === 'vanilla');

  const events = [];
  const first = await generateRun({ config: config(game), workspacePath: path.join(temp, 'run-a') },
    (event) => events.push(event), deps);
  check('generation succeeds from snapshot', first.ok, first.ok ? first.packagePath : `${first.code}: ${first.message}`);
  if (!first.ok) throw new Error('generation failed');
  const m = first.manifest;
  check('manifest binds snapshot', m.pristineSnapshotId === imported.snapshot.snapshotId);
  check('manifest records seeds, engine, retries, validation',
    m.attempts.length === 1 && m.attempts[0].result === 'success' && /^[0-9a-f]{32}$/.test(m.attempts[0].combinedSeed)
    && typeof m.engines.combined === 'string' && m.validation.length > 0 && m.appVersion === '0.15.1');
  const modFiles = m.files.filter((file) => file.path.startsWith('mod/'));
  check('package patches game files', modFiles.length > 0, `${modFiles.length} mod files`);
  check('GameParam patched (paramdef-dependent item data)',
    modFiles.some((file) => file.path === 'mod/param/GameParam/GameParam.parambnd.dcx'));
  check('progress reaches 1', events.some((event) => event.type === 'progress' && event.progress === 1));

  const second = await generateRun({ config: config(game), workspacePath: path.join(temp, 'run-b') }, () => {}, deps);
  const secondMod = second.ok ? second.manifest.files.filter((file) => file.path.startsWith('mod/')) : [];
  check('rerun with same seed is byte-identical in mod/',
    second.ok && JSON.stringify(secondMod) === JSON.stringify(modFiles));

  check('no generation write into the game', sameMap(await hashRoots(game), reconstructed));

  const activated = await authority.activate(game, first.packagePath);
  check('activation', activated.ok, activated.ok ? `${activated.journal.files.length} files` : `${activated.code}: ${activated.message}`);
  const active = await authority.detectState(game);
  check('state active', active.state === 'active', active.problems.join('; '));
  const again = await authority.activate(game, second.packagePath);
  check('second activation refused while active', !again.ok && again.code === 'GAME_STATE_NOT_VANILLA');

  const restored = await authority.restore(game);
  check('restore', restored.ok, restored.ok ? `${restored.restored} files` : `${restored.code}: ${restored.message}`);
  check('game matches reconstruction after restore', sameMap(await hashRoots(game), reconstructed));
  check('pristine source still verifies', (await authority.getPristineSource(game)) !== null);

  check('live installation unchanged', sameMap(await hashRoots(livePath), liveBefore));
} catch (error) {
  check('audit completed', false, String(error));
} finally {
  if (process.env.DS1TR_E2E_KEEP === '1') console.log(`kept ${temp}`);
  else await rm(temp, { recursive: true, force: true });
}
console.log(failed ? 'RESULT: FAIL' : 'RESULT: PASS');
process.exit(failed ? 1 : 0);
