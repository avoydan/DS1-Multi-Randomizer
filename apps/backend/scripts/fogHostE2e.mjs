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
//   DS1TR_E2E_ENEMIES=0 runs Items + Fog only; DS1TR_E2E_SEED sets the master seed.
//   DS1TR_E2E_SWEEP=<n> runs <n> seeds (`<DS1TR_E2E_SEED>-<i>`) generation-only and prints
//   per-attempt statistics, merge conflicts and instruction-level merges instead of the full proof.
//   DS1TR_E2E_FOG=0 disables Fog (combined-only regression sweep).
//   DS1TR_E2E_BOSSES=1 enables boss randomization.
//   DS1TR_E2E_VERIFY_SKIPS=<DsrDataTool.Tests.exe> checks every combined package's EMEVD skip ranges
//   against the pristine snapshot (`verify-skips`, engines/combined/tests/DsrDataTool.Tests).

import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { cp, mkdtemp, readFile, readdir, rename, rm } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
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

const config = (gamePath, masterSeed = process.env.DS1TR_E2E_SEED ?? 'foghost-e2e-1') => ({
  gamePath,
  masterSeed,
  items: {
    enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true,
    guaranteeEarlyWeapon: true, balancedEarlyLoot: true, randomizeStartingClass: false,
    randomizeStartingEquipment: false, randomizeGifts: false, randomizeEnemyDrops: false,
    guaranteedEnemyDrops: false, randomizeShops: false,
    fashionSouls: process.env.DS1TR_E2E_FASHION !== '0',
    soulItems: process.env.DS1TR_E2E_SOULS === 'replaced' ? 'replaced' : 'shuffled',
    startingItems: process.env.DS1TR_E2E_STARTING ?? 'shield-2h',
  },
  // DS1TR_E2E_ENEMIES=0 runs Items + Fog only.
  enemies: { enabled: process.env.DS1TR_E2E_ENEMIES !== '0', randomizeRegularEnemies: true, randomizeBosses: process.env.DS1TR_E2E_BOSSES === '1', scaling: 'area',
    replaceMimics: process.env.DS1TR_E2E_MIMICS !== '0',
    replaceRespawningMosquitoes: process.env.DS1TR_E2E_MOSQUITOES !== '0',
    preventSameEnemy: process.env.DS1TR_E2E_PREVENT_SAME === '1',
    pinwheelChaos: process.env.DS1TR_E2E_PINWHEEL === '1',
    gwynNerf: process.env.DS1TR_E2E_GWYN ?? 'medium',
    secondGargoyle: process.env.DS1TR_E2E_GARGOYLE ?? 'replace',
    // DS1TR_E2E_PLACEMENT='{"algorithm":"sampling-v1","replacementChance":100,"normalReplacement":"normals","fit":"fits"}'
    placement: process.env.DS1TR_E2E_PLACEMENT ? JSON.parse(process.env.DS1TR_E2E_PLACEMENT)
      : { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere' } },
  fog: {
    enabled: process.env.DS1TR_E2E_FOG !== '0', traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: false,
    minorPvpFogs: false, lordvesselGates: false, randomStart: false, disconnectedFogs: false,
    noFallBedOfChaos: false, scaling: true, glitchedLogic: false, requireLordSouls: true,
    pacifistMode: false, language: 'ENGLISH',
  },
  output: { includeDlc: true, generateSpoilerLog: false },
});

class SweepDone extends Error {}

/** Generation-only statistics over many seeds (one reconstruction and snapshot). */
async function sweep(count, prefix) {
  const results = [];
  for (let i = 1; i <= count; i++) {
    const seed = `${prefix}-${i}`;
    const workspace = path.join(temp, `sweep-${i}`);
    const result = await generateRun({ config: config(game, seed), workspacePath: workspace }, () => {}, deps);
    const merges = [];
    const overlays = [];
    for (const directory of await readdir(workspace).catch(() => [])) {
      const log = await readFile(path.join(workspace, directory, 'foghost.log'), 'utf8').catch(() => '');
      for (const match of log.matchAll(/merged (event\/\S+) event (\d+) at instruction level/g)) merges.push(`${match[1]}:${match[2]}`);
      for (const match of log.matchAll(/boss overlay applied to (event\/\S+) event (\d+)/g)) overlays.push(`${match[1]}:${match[2]}`);
    }
    const conflicts = result.ok || result.code !== 'FOG_MERGE_CONFLICT' ? []
      : [...result.message.matchAll(/event\/(\S+?): event (\d+)[^(;]*\(([^)]*)\)/g)].map((m) => `${m[1]}:${m[2]} (${m[3]})`);
    const entry = {
      seed, ok: result.ok, code: result.ok ? 'success' : result.code,
      attempts: result.manifest.attempts.map((a) => `${a.combinedAttempt}/${a.fogAttempt}:${a.result}`),
      conflicts,
      merges: [...new Set(merges)],
      overlays: [...new Set(overlays)],
      ...(result.ok ? {} : { message: result.message.slice(0, 6000) }),
    };
    // sampling-v1: budget and drop-ownership checks on the plan of the last combined attempt.
    const placementInfo = result.manifest.enemyPlacement;
    if (placementInfo.summary) {
      const maps = Object.values(placementInfo.summary.maps);
      entry.placement = {
        classTableHash: placementInfo.classTableHash?.slice(0, 12),
        replaced: maps.reduce((total, map) => total + map.replaced, 0),
        slots: placementInfo.summary.slots,
        maxFinalModels: Math.max(0, ...maps.map((map) => map.finalModels)),
        mapsAboveCap: maps.filter((map) => map.baseline > 30).length,
        samplerOverflow: maps.filter((map) => map.finalModels > map.effectiveCap).length,
        stages: placementInfo.summary.stages,
      };
      const last = (await readdir(workspace).catch(() => [])).filter((d) => d.startsWith('combined-')).sort().pop();
      const seedDirs = last ? (await readdir(path.join(workspace, last)).catch(() => [])).filter((d) => d.startsWith('seed_')) : [];
      if (seedDirs.length > 0) {
        const plan = JSON.parse(await readFile(path.join(workspace, last, seedDirs[0], 'randomizer.json'), 'utf8'));
        const sampled = plan.placements.enemies.filter((p) => /sampled|^pinwheel|passive-until|grounded-new-londo|active-darkroot/u.test(p.compatibility) && p.changed);
        entry.placement.sampledChanged = sampled.length;
        entry.placement.changedWithoutScaledRow = sampled.filter((p) => !Number.isInteger(p.scaledNpcParamId)).length;
      }
    }
    let combinedEdited = new Set();
    if (process.env.DS1TR_E2E_VERIFY_SKIPS) {
      const pristine = (await authority.getPristineSource(game)).directory;
      entry.skipChecks = [];
      for (const attempt of (await readdir(workspace).catch(() => [])).filter((d) => d.startsWith('combined-')).sort()) {
        for (const seedDir of (await readdir(path.join(workspace, attempt)).catch(() => [])).filter((d) => d.startsWith('seed_'))) {
          const run = spawnSync(process.env.DS1TR_E2E_VERIFY_SKIPS,
            ['verify-skips', path.join(pristine, 'event'), path.join(workspace, attempt, seedDir, 'mod', 'event'), '--list-edited'],
            { encoding: 'utf8' });
          entry.skipChecks.push(`${attempt}: exit ${run.status} ${(run.stdout.match(/verify-skips: .*/) ?? ['no output'])[0]}`);
          if (run.status !== 0) console.log(run.stdout);
          // The last combined attempt is the one the Fog run consumed.
          combinedEdited = new Set(run.stdout.split(/\r?\n/).filter((l) => l.startsWith('EDITED ')).map((l) => l.slice(7).trim()));
        }
      }
    }
    if (process.env.DS1TR_E2E_VERIFY_SKIPS && result.ok && process.env.DS1TR_E2E_FOG !== '0') {
      // Final Fog package against FogMod's own event templates: every skip the combined engine or the
      // merge touched must be intact. Mismatches in events the combined engine never edited are
      // FogMod's own runtime edits (stock FogMod behavior) and are reported separately.
      const run = spawnSync(process.env.DS1TR_E2E_VERIFY_SKIPS,
        ['verify-skips', path.join(process.env.DS1TR_FOG_DATA_ROOT, 'dist', 'DS1R', 'event'), path.join(result.packagePath, 'event')],
        { encoding: 'utf8' });
      const mismatched = [...new Set(run.stdout.split(/\r?\n/).filter((l) => l.startsWith('MISMATCH '))
        .map((l) => l.slice(9).split(' ')[0]))];
      entry.fogCombinedEventMismatches = mismatched.filter((id) => combinedEdited.has(id));
      entry.fogModOnlyEventMismatches = mismatched.filter((id) => !combinedEdited.has(id));
    }
    results.push(entry);
    console.log(JSON.stringify(entry));
    await rm(workspace, { recursive: true, force: true });
  }
  const attempts = results.flatMap((r) => r.attempts);
  const tally = (list) => list.reduce((acc, key) => ({ ...acc, [key]: (acc[key] ?? 0) + 1 }), {});
  console.log(JSON.stringify({
    seeds: results.length,
    success: results.filter((r) => r.ok).length,
    finalCodes: tally(results.map((r) => r.code)),
    attemptResults: tally(attempts.map((a) => a.split(':')[1])),
    seedsNeedingCombinedRetry: results.filter((r) => r.attempts.some((a) => a.endsWith('FOG_UPSTREAM_INCOMPATIBLE'))).length,
    conflictEvents: tally(results.flatMap((r) => r.conflicts)),
    instructionMerges: tally(results.flatMap((r) => r.merges)),
    bossOverlays: tally(results.flatMap((r) => r.overlays)),
    samplerOverflow: results.reduce((total, r) => total + (r.placement?.samplerOverflow ?? 0), 0),
    changedWithoutScaledRow: results.reduce((total, r) => total + (r.placement?.changedWithoutScaledRow ?? 0), 0),
    maxFinalModels: Math.max(0, ...results.map((r) => r.placement?.maxFinalModels ?? 0)),
    mapsAboveCap: results.reduce((total, r) => total + (r.placement?.mapsAboveCap ?? 0), 0),
    skipChecksFailed: results.flatMap((r) => r.skipChecks ?? []).filter((c) => !c.includes('exit 0')).length,
    skipChecks: results.flatMap((r) => r.skipChecks ?? []).length,
    fogCombinedEventMismatches: tally(results.flatMap((r) => r.fogCombinedEventMismatches ?? [])),
    fogModOnlyEventMismatches: tally(results.flatMap((r) => r.fogModOnlyEventMismatches ?? [])),
  }, null, 2));
}

const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-fog-e2e-'));
const game = path.join(temp, 'game');
const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
const deps = {
  getPristineSource: (gamePath) => authority.getPristineSource(gamePath),
};

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

  if (process.env.DS1TR_E2E_SWEEP) {
    await sweep(Number(process.env.DS1TR_E2E_SWEEP), process.env.DS1TR_E2E_SEED ?? 'sweep');
    check('live installation unchanged', sameMap(await hashRoots(livePath), liveBefore));
    throw new SweepDone();
  }

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
  if (!(error instanceof SweepDone)) check('proof completed', false, String(error));
} finally {
  if (process.env.DS1TR_E2E_KEEP === '1') console.log(`kept ${temp}`);
  else await rm(temp, { recursive: true, force: true });
}
console.log(failed ? 'RESULT: FAIL' : 'RESULT: PASS');
process.exit(failed ? 1 : 0);
