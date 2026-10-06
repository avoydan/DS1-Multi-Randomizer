// Configuration x seed matrix for the enemy placement work (real catalog, real DsrDataTool, optionally the real FogHost).
//
// For every case and seed it runs the production pipeline (prepareRun + generateRun) against a clean copy of Dark Souls
// Remastered and records the final result, the attempts, the DsrDataTool event-window audit (DS1TR_AUDIT_EVENTS: IDs reserved
// vs IDs used per map, floor event) and, for the first seeds of each case, a rerun that must give an identical package.
// Fog packages are checked with `verify-skips` by the sweeps of fogHostE2e.mjs; this runner is about failures, retries and event windows.
//
// Not part of `npm test` (needs a clean game copy and the built helpers). Usage:
//   DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> DS1TR_DATA_TOOL_EXE=... DS1TR_FOG_HOST=... DS1TR_STATE_DIR=<state with world data> \
//     node apps/backend/scripts/placementMatrix.mjs <set> [--seeds N] [--cases a,b] [--reruns K]
//   node apps/backend/scripts/placementMatrix.mjs --list

import { cp, mkdtemp, mkdir, readdir, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { buildSettings, packageDigest } from './goldenCompatibility.mjs';

const sampling = (placement, enemies = {}) => ({ enemies: { ...enemies, placement: { algorithm: 'sampling-v1', ...placement } } });
const noItems = { items: { enabled: false } };
const merge2 = (...parts) => parts.reduce((all, part) => ({
  ...all, ...part, enemies: { ...(all.enemies ?? {}), ...(part.enemies ?? {}), placement: { ...(all.enemies?.placement ?? {}), ...(part.enemies?.placement ?? {}) } },
  fog: { ...(all.fog ?? {}), ...(part.fog ?? {}) }, items: { ...(all.items ?? {}), ...(part.items ?? {}) },
}), {});
const noFog = { fog: { enabled: false } };

/** The configuration that used to fail: Type replacement + Placement anywhere with bosses on (the Bed of Chaos floor event exists). */
const formerlyFailing = merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled', replacementChance: 100 }, { gwynNerf: 'medium', secondGargoyle: 'keep' }));

export const sets = {
  /** 100 seeds of the historically failing configuration, without and with the world randomizer. */
  'type-event-former': [
    { name: 'former-failing', patch: formerlyFailing, seeds: 100 },
    { name: 'former-failing-fog', patch: merge2(noItems, sampling({ fit: 'anywhere', typeReplacement: 'enabled', replacementChance: 100 }, { gwynNerf: 'medium', secondGargoyle: 'replace' })), seeds: 30 },
  ],
  /** The wider matrix of the event-ID fix: chance x fit x difficulty x Type replacement x Prevent same x pool x Fog. */
  'type-event-matrix': [
    ...[25, 60, 100].flatMap((chance) => [
      { name: `type-enabled-anywhere-random-c${chance}`, patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled', replacementChance: chance, difficultyMode: 'random' })) },
      { name: `type-enabled-fits-curve-c${chance}`, patch: merge2(noItems, noFog, sampling({ fit: 'fits', typeReplacement: 'enabled', replacementChance: chance, difficultyMode: 'curve', difficultyStrictness: 'strict' })) },
      { name: `type-except-roaming-anywhere-c${chance}`, patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled-except-roaming-bosses', replacementChance: chance })) },
    ]),
    { name: 'type-enabled-anywhere-prevent-same', patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled' }, { preventSameEnemy: true })) },
    { name: 'type-enabled-fits-prevent-same-curve', patch: merge2(noItems, noFog, sampling({ fit: 'fits', typeReplacement: 'enabled', difficultyMode: 'curve', difficultyStrictness: 'loose' }, { preventSameEnemy: true })) },
    { name: 'type-enabled-restrictive-pool', patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled', customPool: ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2500', 'c2711', 'c2860', 'c2940', 'c3490', 'c4171'] })) },
    { name: 'type-enabled-boss-sampling', patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled', bossReplacement: 'bosses' })) },
    { name: 'type-enabled-anywhere-fog', patch: merge2(noItems, sampling({ fit: 'anywhere', typeReplacement: 'enabled', replacementChance: 60 }, { gwynNerf: 'strong' })) },
    { name: 'type-enabled-fits-curve-fog', patch: merge2(noItems, sampling({ fit: 'fits', typeReplacement: 'enabled', difficultyMode: 'curve', difficultyStrictness: 'loose' }, { preventSameEnemy: true })) },
    { name: 'type-enabled-pool-fog', patch: merge2(noItems, sampling({ fit: 'anywhere', typeReplacement: 'enabled', customPool: ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2500', 'c2711', 'c2860', 'c2940', 'c3490', 'c4171'] })) },
    { name: 'scaling-vanilla-type', patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled' }, { scaling: 'vanilla' })) },
  ],
  /** Part E1: bosses in regular enemy slots (roaming bosses) through the real data tool; the "-fog" cases also run the world randomizer. */
  'roaming-e1': [
    { name: 'roaming-bosses-fits', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', fit: 'fits' })) },
    { name: 'roaming-both-legacy-default', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', fit: 'fits', difficultyMode: 'random-easy-asylum' })) },
    { name: 'roaming-both-25-anywhere', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'anywhere' })) },
    { name: 'roaming-bosses-c60-curve', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', replacementChance: 60, fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'loose' })) },
    { name: 'roaming-bosses-c25', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', replacementChance: 25, fit: 'anywhere' })) },
    { name: 'roaming-type-enabled', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 30, typeReplacement: 'enabled', fit: 'anywhere' })) },
    { name: 'roaming-type-except-roaming', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 30, typeReplacement: 'enabled-except-roaming-bosses', fit: 'anywhere' })) },
    { name: 'roaming-prevent-same', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', fit: 'fits' }, { preventSameEnemy: true })) },
    { name: 'roaming-custom-pool', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 50, fit: 'anywhere', customPool: ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2500', 'c2711', 'c2860', 'c2940', 'c3490', 'c4171'] })) },
    { name: 'roaming-boss-sampling', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'anywhere', bossReplacement: 'bosses' })) },
    { name: 'roaming-unique-bosses', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', replacementChance: 50, fit: 'anywhere', bossReplacement: 'bosses', uniqueBosses: true })) },
    { name: 'roaming-stay-dead-souls0', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', replacementChance: 40, fit: 'fits', roamingBossSouls: 0, roamingBossesStayDead: true })) },
    { name: 'roaming-second-gargoyle', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'anywhere' }, { secondGargoyle: 'replace' })) },
    { name: 'roaming-vanilla-scaling', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', replacementChance: 50, fit: 'anywhere' }, { scaling: 'vanilla' })) },
    { name: 'roaming-both-fog', patch: merge2(noItems, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'fits' })), seeds: 20 },
    { name: 'roaming-bosses-fog', patch: merge2(noItems, sampling({ normalReplacement: 'bosses', replacementChance: 40, fit: 'anywhere', typeReplacement: 'enabled-except-roaming-bosses' })), seeds: 20 },
  ],
  /** Part E2: a normal enemy as the body of a boss encounter through the real data tool (Fog is covered by the fogHostE2e sweeps). */
  'encounter-e2': [
    { name: 'body-normals-anywhere', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random' })) },
    { name: 'body-normals-fits-curve', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'strict' })) },
    { name: 'body-normals-c50', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', replacementChance: 50, fit: 'fits' })) },
    { name: 'body-both-40', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'both', bossChanceInBossSlots: 40, fit: 'anywhere' })) },
    { name: 'body-both-unique', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'both', bossChanceInBossSlots: 50, uniqueBosses: true, fit: 'anywhere' })) },
    { name: 'body-plus-roaming', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'fits' })) },
    { name: 'body-type-enabled', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', typeReplacement: 'enabled', fit: 'anywhere' })) },
    { name: 'body-custom-pool', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', fit: 'anywhere', customPool: ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2500', 'c2560', 'c2390', 'c2711', 'c2860', 'c2940'] })) },
    { name: 'body-vanilla-scaling', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', fit: 'anywhere' }, { scaling: 'vanilla' })) },
    { name: 'body-second-gargoyle-gwyn', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', fit: 'fits' }, { secondGargoyle: 'replace', gwynNerf: 'strong' })) },
  ],
};

const deterministicSeed = (set, name, index) => `matrix-${set}-${name}-${index}`;

function parseArguments(argv) {
  const out = { set: null, seeds: null, cases: null, reruns: 2, list: false, only: null };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === '--list') out.list = true;
    else if (arg === '--seeds') out.seeds = Number(argv[++index]);
    else if (arg === '--cases') out.cases = argv[++index].split(',');
    else if (arg === '--reruns') out.reruns = Number(argv[++index]);
    else if (arg === '--only') out.only = argv[++index].split(',').map(Number);
    else if (!arg.startsWith('--') && out.set === null) out.set = arg;
  }
  return out;
}

async function main() {
  const args = parseArguments(process.argv.slice(2));
  if (args.list) {
    for (const [name, cases] of Object.entries(sets)) console.log(`${name}: ${cases.map((entry) => `${entry.name}${entry.seeds ? `(${entry.seeds})` : ''}`).join(', ')}`);
    return;
  }
  const source = process.env.DS1TR_GOLDEN_GAME_SOURCE ?? process.env.DS1TR_E2E_GAME_SOURCE;
  const caseList = args.set ? sets[args.set] : null;
  if (!caseList || !source) {
    console.error('Usage: DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> node placementMatrix.mjs <set> [--seeds N] [--cases a,b] [--reruns K]  (--list shows the sets)');
    process.exit(2);
  }
  process.env.DS1TR_AUDIT_EVENTS = '1';
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-matrix-'));
    const summary = [];
  try {
    const game = path.join(temp, 'game');
    await cp(path.join(source, executableName), path.join(game, executableName));
    for (const managed of managedRoots) await cp(path.join(source, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
    const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
    if (!(await authority.importPristine(game)).ok) throw new Error('pristine import failed');

    const generate = async (spec, seed, label) => {
      const prepared = prepareRun({ gamePath: game, masterSeed: seed, ...buildSettings(spec.patch) });
      if (!prepared.ok) throw new Error(`${label}: ${prepared.code}: ${prepared.technical}`);
      const workspacePath = path.join(temp, `run-${label}`);
      await mkdir(workspacePath, { recursive: true });
      const audit = [];
      const details = [];
      const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode },
        (event) => {
          if (event.type !== 'log') return;
          if (event.message.includes('audit-events')) audit.push(...event.message.split(/\r?\n/u).filter((line) => line.includes('audit-events')));
          if (/Details:|failed|already used|Conflicting|Duplicate|exception|Exception/u.test(event.message)) details.push(event.message.slice(0, 500));
        },
        { getPristineSource: (p) => authority.getPristineSource(p) });
      return { prepared, workspacePath, audit, details, result };
    };

    for (const spec of caseList.filter((entry) => !args.cases || args.cases.includes(entry.name))) {
      const seeds = args.seeds ?? spec.seeds ?? 12;
      const stats = { case: spec.name, seeds, ok: 0, failed: [], retries: 0, windowOverruns: 0, windowInexact: 0, floorReserved: 0, windowMoved: 0, movedSeeds: [], maxEvents: 0, maxEventsMap: '', nonDeterministic: 0, maxModels: 0, started: Date.now() };
      const indices = args.only ?? Array.from({ length: seeds }, (_, offset) => offset + 1);
      stats.seeds = indices.length;
      for (const index of indices) {
        const seed = deterministicSeed(args.set, spec.name, index);
        const run = await generate(spec, seed, `${spec.name}-${index}`);
        if (!run.result.ok) {
          stats.failed.push(`#${index} ${seed}: ${run.result.code}: ${run.details.slice(-3).join(' || ') || String(run.result.technical ?? run.result.message).slice(0, 200)}`);
        } else {
          stats.ok += 1;
          stats.retries += run.result.manifest.attempts.length - 1;
          const maps = Object.values(run.result.manifest.enemyPlacement.summary?.maps ?? {});
          stats.maxModels = Math.max(stats.maxModels, ...maps.map((map) => map.finalModels ?? 0), 0);
          for (const line of run.audit) {
            const match = /audit-events (\S+) count=(\d+) consumed=(\d+) base=(\d+) floor=(\S+)/u.exec(line);
            if (!match) continue;
            const [, mapId, count, consumed, base, floor] = match;
            if (Number(count) > stats.maxEvents) { stats.maxEvents = Number(count); stats.maxEventsMap = mapId; }
            // The window was moved off the preferred base because the Bed of Chaos floor event sits inside it (the former failure).
            if (floor !== '-' && Number(base) !== 11419900) { stats.windowMoved += 1; stats.movedSeeds.push(index); }
            if (Number(consumed) > Number(count)) stats.windowOverruns += 1;
            if (Number(consumed) !== Number(count)) stats.windowInexact += 1;
            if (floor !== '-') stats.floorReserved += 1;
          }
          if (index <= args.reruns) {
            const first = await packageDigest(run.result.manifest.files, run.result.packagePath);
            const again = await generate(spec, seed, `${spec.name}-${index}-rerun`);
            const second = again.result.ok ? await packageDigest(again.result.manifest.files, again.result.packagePath) : null;
            if (!second || second.digest !== first.digest) stats.nonDeterministic += 1;
            await rm(again.workspacePath, { recursive: true, force: true });
          }
        }
        await rm(run.workspacePath, { recursive: true, force: true });
      }
      stats.seconds = Math.round((Date.now() - stats.started) / 1000);
      delete stats.started;
      summary.push(stats);
      console.log(JSON.stringify(stats));
    }
    const failed = summary.reduce((total, entry) => total + entry.failed.length, 0);
    const bad = summary.reduce((total, entry) => total + entry.windowOverruns + entry.nonDeterministic, 0);
    console.log(`MATRIX ${args.set}: ${summary.reduce((total, entry) => total + entry.ok, 0)} ok, ${failed} failed, ${bad} invariant violations (window overruns / non-deterministic reruns)`);
    if (failed > 0 || bad > 0) process.exitCode = 1;
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
