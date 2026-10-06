// Proves that the spoiler preference (excluded from Seed Codes) does not change the world: the package digest of a golden case with
// the spoiler log ON equals the recorded digest once spoiler.txt itself is left out.
import { createHash } from 'node:crypto';
import { cp, mkdtemp, readFile, rm, mkdir } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { packageDigest } from './goldenCompatibility.mjs';

const source = process.env.DS1TR_GOLDEN_GAME_SOURCE;
const names = process.argv.slice(2);
const fixture = JSON.parse(await readFile(path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'fixtures', 'golden', 'ds1tr-gen-v1.json'), 'utf8'));
const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-spoiler-'));
let failed = 0;
try {
  const game = path.join(temp, 'game');
  await cp(path.join(source, executableName), path.join(game, executableName));
  for (const managed of managedRoots) await cp(path.join(source, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
  const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
  if (!(await authority.importPristine(game)).ok) throw new Error('import failed');
  for (const entry of fixture.cases.filter((c) => names.includes(c.name))) {
    const prepared = prepareRun({ gamePath: game, seedCode: entry.seedCode, output: { includeDlc: true, generateSpoilerLog: true } });
    if (!prepared.ok || prepared.config.output.generateSpoilerLog !== true) throw new Error('spoiler flag not carried');
    const workspacePath = path.join(temp, `run-${entry.name}`);
    await mkdir(workspacePath, { recursive: true });
    const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode }, () => {}, { getPristineSource: (p) => authority.getPristineSource(p) });
    if (!result.ok) { failed += 1; console.log(`FAIL ${entry.name}: ${result.code}`); continue; }
    const hasSpoiler = result.manifest.files.some((file) => file.path === 'spoiler.txt');
    const files = result.manifest.files.filter((file) => file.path !== 'spoiler.txt');
    const digest = await packageDigest(files, result.packagePath);
    const same = digest.digest === entry.expected.digest;
    if (!same) failed += 1;
    console.log(`${same ? 'SAME WORLD' : 'DIFFERENT'} ${entry.name}: spoiler.txt written=${hasSpoiler}, digest without it ${same ? 'equals' : 'differs from'} the recorded one`);
  }
  console.log(failed === 0 ? 'SPOILER INDEPENDENCE: PASS' : `SPOILER INDEPENDENCE: FAIL (${failed})`);
  if (failed) process.exitCode = 1;
} finally { await rm(temp, { recursive: true, force: true }); }
