// The full-playthrough worlds of docs/FINAL_GAMEPLAY_QA.md: fixed master seeds and settings, so the owner loads a Seed Code instead of setting a dozen controls by hand.
//
//   node apps/backend/scripts/qaWorlds.mjs          print each world's Seed Code
//   DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> node apps/backend/scripts/qaWorlds.mjs verify   generate each world twice through the real pipeline on a copy (identical package, no failure)
//
// The document is checked against these recipes by src/core/finalQaDoc.test.mjs (a code in the document that is not the code of its recipe fails).

import { cp, mkdir, mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { DEFAULT_GENERATION_SETTINGS } from '@ds1tr/contracts';
import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { packageDigest } from './goldenCompatibility.mjs';

const merge = (base, patch) => {
  const out = structuredClone(base);
  for (const [key, value] of Object.entries(patch ?? {})) out[key] = value && typeof value === 'object' && !Array.isArray(value) ? merge(base[key] ?? {}, value) : value;
  return out;
};
const allPools = { randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true };

/** `settings` is a patch over the product defaults (what a fresh installation shows). */
export const QA_WORLDS = [
  {
    id: 'recommended', title: 'Recommended Triple', masterSeed: 'qa-final-recommended-1',
    settings: { items: { soulItems: 'transposed' } },
  },
  {
    id: 'chaotic', title: 'Chaotic Triple', masterSeed: 'qa-final-chaotic-1',
    settings: {
      items: { ...allPools, soulItems: 'transposed' },
      enemies: { placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 70, normalReplacement: 'both', bossReplacement: 'both', bossChanceInNormalSlots: 10, bossChanceInBossSlots: 60, hostileNpcs: true } },
    },
  },
  {
    id: 'fog-keys', title: 'Fog with shuffled keys', masterSeed: 'qa-final-fog-keys-1',
    settings: { items: { keyPlacement: 'shuffled', randomizeLordvessel: true, randomizeLordSouls: true } },
  },
  {
    id: 'race', title: 'Race Mode', masterSeed: 'qa-final-race-1',
    settings: { fog: { enabled: false }, enemies: { enabled: false }, items: { keyPlacement: 'race', randomizeLordvessel: true, randomizeLordSouls: true, soulItems: 'transposed' } },
  },
  {
    id: 'race-plus', title: 'Race Mode +', masterSeed: 'qa-final-raceplus-1',
    settings: { fog: { enabled: false }, enemies: { enabled: false }, items: { keyPlacement: 'race-plus', randomizeLordvessel: true, randomizeLordSouls: true } },
  },
  {
    id: 'dlc-off', title: 'DLC content left out', masterSeed: 'qa-final-dlc-off-1',
    settings: { fog: { enabled: false }, output: { includeDlc: false }, items: { ...allPools, soulItems: 'transposed' } },
  },
];

export function qaWorldCodes() {
  return QA_WORLDS.map((world) => {
    const settings = merge(DEFAULT_GENERATION_SETTINGS, world.settings);
    const prepared = prepareRun({ gamePath: 'X:/qa', masterSeed: world.masterSeed, ...settings, output: { ...settings.output, generateSpoilerLog: false } });
    if (!prepared.ok) throw new Error(`${world.id}: ${prepared.code}: ${prepared.technical}`);
    return { ...world, seedCode: prepared.seedCode };
  });
}

async function verify() {
  const source = process.env.DS1TR_GOLDEN_GAME_SOURCE;
  if (!source) { console.error('DS1TR_GOLDEN_GAME_SOURCE is required.'); process.exit(2); }
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-qa-'));
  let failed = 0;
  try {
    const game = path.join(temp, 'game');
    await cp(path.join(source, executableName), path.join(game, executableName));
    for (const managed of managedRoots) await cp(path.join(source, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
    const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
    if (!(await authority.importPristine(game)).ok) throw new Error('pristine import failed');
    for (const world of qaWorldCodes()) {
      const digests = [];
      let detail = '';
      for (const attempt of [1, 2]) {
        const prepared = prepareRun({ gamePath: game, seedCode: world.seedCode });
        if (!prepared.ok) throw new Error(`${world.id}: ${prepared.code}`);
        const workspacePath = path.join(temp, `run-${world.id}-${attempt}`);
        await mkdir(workspacePath, { recursive: true });
        const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode }, () => {}, { getPristineSource: (gamePath) => authority.getPristineSource(gamePath) });
        if (!result.ok) { digests.push(`FAILED ${result.code}: ${result.message}`); break; }
        digests.push((await packageDigest(result.manifest.files, result.packagePath)).digest);
        detail = `${result.manifest.files.length} files, attempts ${result.manifest.attempts.map((entry) => entry.result).join(',')}`;
      }
      const good = digests.length === 2 && !digests[0].startsWith('FAILED') && digests[0] === digests[1];
      if (!good) failed += 1;
      console.log(`${good ? 'PASS' : 'FAIL'} ${world.id} (${world.title}): ${good ? detail : digests.join(' | ')}`);
    }
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
  console.log(failed === 0 ? 'RESULT: PASS' : `RESULT: FAIL (${failed})`);
  if (failed > 0) process.exitCode = 1;
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  if (process.argv[2] === 'verify') await verify();
  else for (const world of qaWorldCodes()) console.log(`${world.id}\n${world.seedCode}\n`);
}
