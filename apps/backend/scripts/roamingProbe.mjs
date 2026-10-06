// Generates ONE world with the real DsrDataTool and keeps its package, then prints the roaming bosses the plan contains.
//
//   DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> DS1TR_DATA_TOOL_EXE=... node apps/backend/scripts/roamingProbe.mjs <outDir> [seed] [both|bosses]
//
// The package folder (<outDir>/package) can then be read back with any NpcParam reader: each roaming boss has a scaled NpcParam row
// (`scaledNpcParamId` in the printed list) whose getSoul / disableRespawn must equal the printed values.

import { cp, mkdir, readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { buildSettings } from './goldenCompatibility.mjs';

const [outDir, seed = 'roaming-probe-1', mode = 'both'] = process.argv.slice(2);
const source = process.env.DS1TR_GOLDEN_GAME_SOURCE;
if (!outDir || !source) { console.error('Usage: DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> node roamingProbe.mjs <outDir> [seed] [both|bosses]'); process.exit(2); }

const game = path.join(outDir, 'game');
await mkdir(game, { recursive: true });
await cp(path.join(source, executableName), path.join(game, executableName));
for (const managed of managedRoots) await cp(path.join(source, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
const authority = new GameStateAuthority({ stateRoot: path.join(outDir, 'state'), isGameRunning: async () => false });
if (!(await authority.importPristine(game)).ok) throw new Error('pristine import failed');

const patch = {
  items: { enabled: false }, fog: { enabled: false },
  enemies: { placement: { algorithm: 'sampling-v1', normalReplacement: mode, fit: 'fits', replacementChance: 40, roamingBossSouls: 50, roamingBossesStayDead: true, ...(mode === 'both' ? { bossChanceInNormalSlots: 25 } : {}) } },
};
const prepared = prepareRun({ gamePath: game, masterSeed: seed, ...buildSettings(patch) });
if (!prepared.ok) throw new Error(`${prepared.code}: ${prepared.technical}`);
const workspacePath = path.join(outDir, 'run');
await mkdir(workspacePath, { recursive: true });
const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode }, () => {},
  { getPristineSource: (p) => authority.getPristineSource(p) });
if (!result.ok) throw new Error(`${result.code}: ${result.message}`);
console.log('package:', result.packagePath);
let plan = path.join(result.packagePath, 'randomizer.json');
try { await readFile(plan); } catch {
  const attempts = (await readdir(workspacePath)).filter((name) => /^combined-\d+$/u.test(name)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
  const seedDirectory = (await readdir(path.join(workspacePath, attempts[0]))).find((name) => name.startsWith('seed_'));
  plan = path.join(workspacePath, attempts[0], seedDirectory, 'randomizer.json');
}
const placements = JSON.parse(await readFile(plan, 'utf8')).placements.enemies.filter((p) => p.compatibility === 'sampled-roaming-boss');
console.log(`roaming bosses: ${placements.length}`);
for (const p of placements) console.log(JSON.stringify({ slot: p.slot, to: p.to, scaledNpcParamId: p.scaledNpcParamId, souls: p.roamingBossSouls, disableRespawn: p.disableRespawn === true, entityId: p.entityId, forceCombat: p.forceCombatActivation === true }));
