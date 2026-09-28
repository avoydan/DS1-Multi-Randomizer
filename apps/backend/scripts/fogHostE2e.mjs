// FogHost end-to-end proof: combined Items + Enemies -> FogHost -> package, on real-format data.
//
// pristine import -> generateRun (combined + Fog through the built FogHost) -> deterministic rerun
// -> activate the Fog package -> restore, through the same GameStateAuthority and pipeline the
// server uses. Boss randomization stays off (FogHost v1 rejects it together with Fog).
//
// The live installation is only read. As in releaseAuditE2e.mjs, a reconstruction of its managed
// roots (legacy `X.bak` moved over `X`) in the OS temp directory is the "game". Game-derived
// output and FogMod data never enter the repository.
//
// Not part of `npm test`: needs a real installation, built DsrDataTool and FogHost, and
// DS1TR_FOG_DATA_ROOT (folder containing FogMod 6e6dc78 `dist`).
//
// Usage (after `npm run build -w @ds1tr/backend` and building tools/fog-host in Release):
//   DS1TR_FOG_DATA_ROOT=<fog data> node apps/backend/scripts/fogHostE2e.mjs "<live DSR directory>"
//   DS1TR_E2E_KEEP=1 keeps the temp directory.

import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { cp, mkdtemp, readdir, rename, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';

const livePath = process.argv[2] ?? process.env.DS1TR_E2E_GAME_PATH;
if (!livePath || !process.env.DS1TR_FOG_DATA_ROOT) {
  console.error('Usage: DS1TR_FOG_DATA_ROOT=<fog data> fogHostE2e.mjs <live DSR directory>');
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
  masterSeed: process.env.DS1TR_E2E_SEED ?? 'foghost-e2e-1',
  items: {
    enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true,
    guaranteeEarlyWeapon: true, balancedEarlyLoot: true, randomizeStartingClass: false,
    randomizeStartingEquipment: false, randomizeGifts: false, randomizeEnemyDrops: false,
    guaranteedEnemyDrops: false, randomizeShops: false,
  },
  // DS1TR_E2E_ENEMIES=0 runs Items + Fog only.
  enemies: { enabled: process.env.DS1TR_E2E_ENEMIES !== '0', randomizeRegularEnemies: true, randomizeBosses: false, scaling: 'area' },
  fog: {
    enabled: true, traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: false,
    minorPvpFogs: false, lordvesselGates: false, randomStart: false, disconnectedFogs: false,
    noFallBedOfChaos: false, scaling: true, glitchedLogic: false, requireLordSouls: true,
    pacifistMode: false, language: 'ENGLISH',
  },
  output: { includeDlc: true, generateSpoilerLog: false },
});

const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-fog-e2e-'));
const game = path.join(temp, 'game');
const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
const deps = { getPristineSource: (gamePath) => authority.getPristineSource(gamePath) };

try {
  const liveBefore = await hashRoots(livePath);
  console.log(`live read set: ${liveBefore.size} files`);

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

  const logs = [];
  const started = Date.now();
  const first = await generateRun({ config: config(game), workspacePath: path.join(temp, 'run-a') },
    (event) => { if (event.type === 'log' && event.stage === 'fog' && event.level !== 'info') logs.push(event.message); }, deps);
  check('combined + Fog generation succeeds', first.ok,
    first.ok ? `${first.packagePath} in ${Math.round((Date.now() - started) / 1000)} s` : `${first.code}: ${first.message}`);
  console.log(`attempts: ${JSON.stringify(first.manifest.attempts.map((a) => `${a.combinedAttempt}/${a.fogAttempt}:${a.result}`))}`);
  if (!first.ok) throw new Error('generation failed');
  const m = first.manifest;
  check('manifest records FogHost and pinned FogMod', /6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f/.test(m.engines.fog ?? ''), m.engines.fog);
  check('Fog output hashes verified', m.validation.includes('Fog output hashes verified.'));
  check('package has no .bak files', !m.files.some((file) => /\.bak$/i.test(file.path)));
  check('package includes Fog talk ESDs', m.files.some((file) => file.path.startsWith('script/talk/')));

  const second = await generateRun({ config: config(game), workspacePath: path.join(temp, 'run-b') }, () => {}, deps);
  check('rerun with same seed is byte-identical', second.ok && JSON.stringify(second.manifest.files) === JSON.stringify(m.files));
  check('no generation write into the game', sameMap(await hashRoots(game), reconstructed));

  const activated = await authority.activate(game, first.packagePath);
  check('Fog package activation (temp copy)', activated.ok, activated.ok ? `${activated.journal.files.length} files` : `${activated.code}: ${activated.message}`);
  const active = await authority.detectState(game);
  check('state active', active.state === 'active', active.problems.join('; '));
  const restored = await authority.restore(game);
  check('restore', restored.ok, restored.ok ? `${restored.restored} files` : `${restored.code}: ${restored.message}`);
  check('game matches reconstruction after restore', sameMap(await hashRoots(game), reconstructed));

  check('live installation unchanged', sameMap(await hashRoots(livePath), liveBefore));
} catch (error) {
  check('proof completed', false, String(error));
} finally {
  if (process.env.DS1TR_E2E_KEEP === '1') console.log(`kept ${temp}`);
  else await rm(temp, { recursive: true, force: true });
}
console.log(failed ? 'RESULT: FAIL' : 'RESULT: PASS');
process.exit(failed ? 1 : 0);
