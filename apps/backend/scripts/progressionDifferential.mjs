// Differential test of the progression model (docs/ITEM_PROGRESSION_MODEL.md): the engine's evaluation of the exported graph (engines/combined/vendor/src/core/keys.js)
// against FogMod's own GraphChecker (FogHost progression-check), on thousands of key assignments, valid and invalid. There is one model of the world;
// this script is the hard gate that its two implementations agree on every assignment: completable or not, the exact set of unvisited areas, and the prison escape.
//
//   node apps/backend/scripts/progressionDifferential.mjs <FogHost.exe> <fog data root> [--trials 3000] [--seed 1]
//
// Needs the built FogHost and a prepared world data root (the one the application caches); reads nothing else, writes only to the temp folder.

import { execFileSync } from 'node:child_process';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const [hostExe, dataRoot, ...rest] = process.argv.slice(2);
if (!hostExe || !dataRoot) { console.error('Usage: node progressionDifferential.mjs <FogHost.exe> <fog data root> [--trials N] [--seed S]'); process.exit(2); }
const option = (name, fallback) => (rest.includes(name) ? rest[rest.indexOf(name) + 1] : fallback);
const trials = Number(option('--trials', 3000));
let state = Number(option('--seed', 1)) >>> 0;
const random = () => { state = (state + 0x6d2b79f5) >>> 0; let value = state; value = Math.imul(value ^ (value >>> 15), value | 1); value ^= value + Math.imul(value ^ (value >>> 7), value | 61); return ((value ^ (value >>> 14)) >>> 0) / 4294967296; };
const pick = (list) => list[Math.floor(random() * list.length)];

const keys = await import(pathToFileURL(fileURLToPath(new URL('../../../engines/combined/vendor/src/core/keys.js', import.meta.url))).href);
const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-progression-'));
try {
  const modelFile = path.join(temp, 'model.json');
  execFileSync(hostExe, ['progression-model', '--data-root', dataRoot, '--out', modelFile], { stdio: 'inherit' });
  const model = JSON.parse(await readFile(modelFile, 'utf8'));
  const prepared = keys.prepareModel(model);
  const movable = model.keys.filter((key) => !(key.tags ?? []).includes('randomonly'));
  const lotAreas = Object.values(model.lotAreas);
  const vanilla = Object.fromEntries(model.keys.map((key) => [key.name, key.area]));

  const assignments = [];
  for (let index = 0; index < trials; index += 1) {
    const mode = index % 4;
    const assignment = { ...vanilla };
    if (mode === 0) for (const key of movable) assignment[key.name] = pick(lotAreas);                       // everything random
    else if (mode === 1) for (const key of movable) if (random() < 0.3) assignment[key.name] = pick(lotAreas); // a few moved
    else if (mode === 2) for (const key of movable) if (random() < 0.12) assignment[key.name] = pick(model.areas); // any area, even without a pickup
    else { const some = [...movable].sort(() => random() - 0.5).slice(0, 2); for (const key of some) assignment[key.name] = pick(lotAreas); } // one or two moved
    assignments.push(assignment);
  }
  const file = path.join(temp, 'assignments.json');
  await writeFile(file, JSON.stringify(assignments));
  const resultFile = path.join(temp, 'result.json');
  execFileSync(hostExe, ['progression-check', '--data-root', dataRoot, '--assign-many', file, '--out', resultFile], { stdio: 'inherit' });
  const { results } = JSON.parse(await readFile(resultFile, 'utf8'));
  let agree = 0; let completable = 0; let blocked = 0; let prisonStuck = 0;
  const mismatches = [];
  for (let index = 0; index < assignments.length; index += 1) {
    const mine = keys.checkCompletable(prepared, new Map(Object.entries(assignments[index])));
    const theirs = results[index];
    const same = mine.completable === theirs.worldCompletable && mine.prisonEscapable === theirs.prisonEscapable && JSON.stringify(mine.unvisited) === JSON.stringify(theirs.unvisited.filter((area) => !prepared.optional.has(area)));
    if (same) agree += 1; else if (mismatches.length < 10) mismatches.push({ index, mine, theirs });
    if (theirs.worldCompletable) completable += 1; else blocked += 1;
    if (!theirs.prisonEscapable) prisonStuck += 1;
  }
  console.log(`${trials} assignments: ${agree} agree, ${mismatches.length === 0 ? 0 : trials - agree} differ; FogMod says completable ${completable}, blocked ${blocked}, prison not escapable ${prisonStuck}`);
  for (const mismatch of mismatches) console.log(JSON.stringify(mismatch));
  process.exitCode = agree === trials ? 0 : 1;
} finally {
  await rm(temp, { recursive: true, force: true });
}
