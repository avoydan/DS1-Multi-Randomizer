// Does a setting change the GAME FILES the real pipeline writes? (docs/SETTINGS_INVENTORY.md, "Settings with no effect")
//
//   DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> node apps/backend/scripts/settingEffectReal.mjs [name,name]
//
// Each case generates the same master seed twice through the real pipeline (real data tool, a clean copy of the game), once with the setting on and once off (or with the
// two values), and compares the hash of every file of the package except the files that only echo the configuration (randomizer.json, config.json, patch-manifest.json,
// run-manifest.json, spoiler.txt, cheat-locations.txt, README.txt). "SAME" means the game files are byte-identical: the setting changed nothing the game reads.

import { cp, mkdir, mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { buildSettings, packageDigest } from './goldenCompatibility.mjs';

const source = process.env.DS1TR_GOLDEN_GAME_SOURCE;
if (!source) { console.error('DS1TR_GOLDEN_GAME_SOURCE is required.'); process.exit(2); }
const echoes = new Set(['randomizer.json', 'config.json', 'patch-manifest.json', 'run-manifest.json', 'spoiler.txt', 'cheat-locations.txt', 'README.txt']);
const pools = { randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true, randomizeStartingClass: true, randomizeStartingEquipment: true };
const noFog = { fog: { enabled: false } };

export const CASES = [
  { name: 'early-weapon', a: { ...noFog, items: { ...pools, guaranteeEarlyWeapon: true } }, b: { ...noFog, items: { ...pools, guaranteeEarlyWeapon: false } } },
  { name: 'balanced-early-loot', a: { ...noFog, items: { ...pools, balancedEarlyLoot: true } }, b: { ...noFog, items: { ...pools, balancedEarlyLoot: false } } },
  { name: 'both-early-options', a: { ...noFog, items: { ...pools, guaranteeEarlyWeapon: true, balancedEarlyLoot: true } }, b: { ...noFog, items: { ...pools, guaranteeEarlyWeapon: false, balancedEarlyLoot: false } } },
  { name: 'scaling-progressive-vs-area', a: { ...noFog, items: { enabled: false }, enemies: { scaling: 'area' } }, b: { ...noFog, items: { enabled: false }, enemies: { scaling: 'progressive' } } },
  { name: 'control-scaling-vanilla-vs-area', a: { ...noFog, items: { enabled: false }, enemies: { scaling: 'area' } }, b: { ...noFog, items: { enabled: false }, enemies: { scaling: 'vanilla' } }, expectDifferent: true },
  { name: 'control-progression-logic', a: { ...noFog, enemies: { enabled: false }, items: { ...pools, progressionLogic: true } }, b: { ...noFog, enemies: { enabled: false }, items: { ...pools, progressionLogic: false } }, expectDifferent: true },
];

async function main() {
  const only = process.argv[2]?.split(',');
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-effect-'));
  let bad = 0;
  try {
    const game = path.join(temp, 'game');
    await cp(path.join(source, executableName), path.join(game, executableName));
    for (const managed of managedRoots) await cp(path.join(source, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
    const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
    if (!(await authority.importPristine(game)).ok) throw new Error('pristine import failed');
    const run = async (patch, seed, label) => {
      const prepared = prepareRun({ gamePath: game, masterSeed: seed, ...buildSettings(patch) });
      if (!prepared.ok) throw new Error(`${label}: ${prepared.technical}`);
      const workspacePath = path.join(temp, `run-${label}`);
      await mkdir(workspacePath, { recursive: true });
      const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode }, () => {}, { getPristineSource: (g) => authority.getPristineSource(g) });
      if (!result.ok) throw new Error(`${label}: ${result.code} ${result.message}`);
      return (await packageDigest(result.manifest.files, result.packagePath)).files;
    };
    for (const spec of CASES.filter((entry) => !only || only.includes(entry.name))) {
      let different = 0; const worlds = 3;
      for (let index = 0; index < worlds; index += 1) {
        const seed = `effect-${spec.name}-${index}`;
        const a = await run(spec.a, seed, `${spec.name}-${index}-a`);
        const b = await run(spec.b, seed, `${spec.name}-${index}-b`);
        const keys = [...new Set([...Object.keys(a), ...Object.keys(b)])].filter((key) => !echoes.has(key.split('/').at(-1)));
        const diff = keys.filter((key) => a[key] !== b[key]);
        if (diff.length > 0) { different += 1; if (process.env.DS1TR_EFFECT_VERBOSE) console.log('  differing:', diff.slice(0, 6).join(', ')); }
      }
      const verdict = different === 0 ? 'SAME (no effect on the game files)' : `DIFFERENT in ${different}/${worlds}`;
      const ok = spec.expectDifferent ? different > 0 : true;
      if (!ok) bad += 1;
      console.log(`${ok ? 'ok  ' : 'FAIL'} ${spec.name.padEnd(34)} ${verdict}${spec.expectDifferent ? ' (control: must differ)' : ''}`);
    }
  } finally { await rm(temp, { recursive: true, force: true }); }
  if (bad > 0) process.exitCode = 1;
}

await main();
