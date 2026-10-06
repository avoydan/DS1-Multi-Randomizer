// Golden compatibility fixtures for generation profiles (docs/GENERATION_COMPATIBILITY.md).
//
// A fixture is a Seed Code created today together with the package it generates. A future application that still supports the
// profile must reproduce the same package from the Seed Code alone. This script drives the real pipeline (real engines, real
// FogHost) against a clean copy of Dark Souls Remastered:
//
//   verify   regenerate every fixture from its Seed Code and compare the package digests
//   record   create the fixtures of a NEW profile (refuses to touch an existing profile file)
//   probe    generate one fixture twice into different workspaces and list the files that differ (to pick the digest rules)
//
// RULE: never re-record a fixture file to make `verify` pass. If the output of an existing profile changed, the change is a
// compatibility break: restore the old behavior or add a NEW generation profile (docs/GENERATION_COMPATIBILITY.md).
//
// Usage (after `npm run build`; needs a clean game copy and the built helpers, like fogHostE2e.mjs):
//   DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> node apps/backend/scripts/goldenCompatibility.mjs verify [--app-version 9.9.9]
//   DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> node apps/backend/scripts/goldenCompatibility.mjs record ds1tr-gen-v1
//   (set DS1TR_STATE_DIR to an empty folder to prepare the world data there; DS1TR_FOG_HOST / DS1TR_DATA_TOOL_EXE as usual)

import { createHash } from 'node:crypto';
import { cp, mkdtemp, readFile, rm, writeFile, mkdir, access } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { canonicalSettingsJson } from '../dist/core/canonicalSettings.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const fixtureDirectory = path.join(here, '..', 'fixtures', 'golden');

/**
 * Files of a package that are not part of a world's identity: they embed the temporary workspace path or a per-run
 * attempt directory (the combined engine's own config and patch manifest). Everything else - including the placement plan
 * `randomizer.json` and every game file - is hashed.
 */
export const volatilePackageFiles = new Set(['config.json', 'patch-manifest.json', 'run-manifest.json']);

/**
 * The placement plan `randomizer.json` embeds the temporary workspace paths of its run, so it is hashed by what it decides:
 * its placement hash and the placements themselves.
 */
async function stablePlanHash(packagePath) {
  const plan = JSON.parse(await readFile(path.join(packagePath, 'randomizer.json'), 'utf8'));
  return createHash('sha256').update(JSON.stringify({ placementHash: plan.placementHash, placements: plan.placements })).digest('hex');
}

export async function packageDigest(files, packagePath) {
  const entries = files.filter((file) => !volatilePackageFiles.has(file.path)).map((file) => ({ path: file.path, sha256: file.sha256 }));
  for (const entry of entries) {
    if (entry.path === 'randomizer.json') entry.sha256 = await stablePlanHash(packagePath);
  }
  const lines = entries.map((entry) => `${entry.path}:${entry.sha256}`).sort();
  return { fileCount: lines.length, digest: createHash('sha256').update(lines.join(String.fromCharCode(10))).digest('hex'), files: Object.fromEntries(lines.map((line) => line.split(/:(?=[0-9a-f]{64}$)/u))) };
}

const defaults = {
  items: { enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true, guaranteeEarlyWeapon: true, balancedEarlyLoot: true,
    randomizeStartingClass: false, randomizeStartingEquipment: false, randomizeGifts: false, randomizeEnemyDrops: false, guaranteedEnemyDrops: false,
    randomizeShops: false, fashionSouls: true, soulItems: 'shuffled', startingItems: 'shield-2h' },
  enemies: { enabled: true, randomizeRegularEnemies: true, randomizeBosses: true, scaling: 'area', replaceMimics: true, replaceRespawningMosquitoes: true,
    preventSameEnemy: false, pinwheelChaos: false, gwynNerf: 'medium', secondGargoyle: 'replace',
    placement: { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random',
      difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false } },
  fog: { enabled: true, traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: false, minorPvpFogs: false, lordvesselGates: false,
    randomStart: false, disconnectedFogs: false, noFallBedOfChaos: false, scaling: true, glitchedLogic: false, requireLordSouls: true,
    pacifistMode: false, language: 'ENGLISH' },
  output: { includeDlc: true, generateSpoilerLog: false },
};

const merge = (base, patch) => {
  if (patch === undefined) return structuredClone(base);
  const out = structuredClone(base);
  for (const [key, value] of Object.entries(patch)) {
    out[key] = value && typeof value === 'object' && !Array.isArray(value) ? merge(base[key] ?? {}, value) : value;
  }
  return out;
};
const off = { items: { enabled: false }, enemies: { enabled: false }, fog: { enabled: false } };
const broadItems = { randomizeStartingClass: true, randomizeStartingEquipment: true, randomizeGifts: true, randomizeEnemyDrops: true, guaranteedEnemyDrops: true, randomizeShops: true };
const poolC = ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2500', 'c2711', 'c2860', 'c2940'];

/** The cases of generation profile ds1tr-gen-v1 (master seeds are fixed strings; recording derives the Seed Codes). */
export const v1Cases = [
  { name: 'items-only', masterSeed: 'golden-v1-items-only', patch: { enemies: { enabled: false }, fog: { enabled: false } } },
  { name: 'enemies-classic-permutation', masterSeed: 'golden-v1-enemies-classic', patch: { items: { enabled: false }, enemies: { randomizeBosses: false }, fog: { enabled: false } } },
  { name: 'enemies-sampling-v1', masterSeed: 'golden-v1-sampling', patch: { items: { enabled: false }, fog: { enabled: false },
    enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', replacementChance: 60, fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'strict' } } } },
  { name: 'items-enemies-bosses', masterSeed: 'golden-v1-ieb', patch: { fog: { enabled: false } } },
  { name: 'items-enemies-bosses-fog', masterSeed: 'golden-v1-iebf', patch: {} },
  { name: 'broad-items-shops-fog', masterSeed: 'golden-v1-broad', patch: { items: broadItems } },
  { name: 'custom-enemy-pool', masterSeed: 'golden-v1-pool', patch: { items: { enabled: false }, fog: { enabled: false },
    enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', replacementChance: 100, fit: 'fits', difficultyMode: 'random-easy-asylum', typeReplacement: 'enabled', customPool: poolC } } } },
  { name: 'second-gargoyle-keep', masterSeed: 'golden-v1-gargoyle', patch: { items: { enabled: false }, fog: { enabled: false }, enemies: { randomizeRegularEnemies: false, secondGargoyle: 'keep', gwynNerf: 'strong' } } },
  { name: 'custom-pool-model-grace', masterSeed: 'golden-v1-grace-75', patch: { items: { enabled: false }, fog: { enabled: false },
    enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', replacementChance: 25, fit: 'fits', difficultyMode: 'random-easy-asylum', customPool: poolC } } } },
  { name: 'world-options', masterSeed: 'golden-v1-world', patch: { items: { enabled: false }, enemies: { enabled: false },
    fog: { majorPvpFogs: true, minorPvpFogs: true, lordvesselGates: true, disconnectedFogs: true, glitchedLogic: true, requireLordSouls: false, language: 'POLISH' } } },
];

/**
 * Further permanent v1 cases, recorded in their own files (`<profile>.<set>.json`) and never mixed into the first ten. The
 * boss baseline was recorded BEFORE boss sampling existed: sampling-v1 with bosses on (the classic boss planner) must stay
 * byte-identical when boss sampling is added behind a non-neutral option.
 */
export const v1ExtraCaseSets = {
  /**
   * The configuration that used to fail with "Randomizer event ID is already used: 11419980": Type replacement enabled, Placement
   * anywhere, bosses on (the Bed of Chaos floor event exists) and enough event-requiring placements in Lost Izalith that the custom
   * event window had to move off the floor event. Recorded with the fixed data tool; it pins the relocated window.
   */
  'event-window': [
    { name: 'type-anywhere-relocated-window', masterSeed: 'golden-v1-boss-combined', patch: { items: { enabled: false }, fog: { enabled: false },
      enemies: { preventSameEnemy: true, gwynNerf: 'medium', secondGargoyle: 'keep', placement: { algorithm: 'sampling-v1', replacementChance: 100, fit: 'anywhere', typeReplacement: 'enabled' } } } },
  ],
  /** Boss sampling (Part D): worlds that use the non-neutral extension keys, frozen from the day they exist. */
  'boss-sampling': [
    { name: 'boss-sampling-fog', masterSeed: 'golden-v1-bosssample-fog', patch: { enemies: { gwynNerf: 'strong', secondGargoyle: 'replace',
      placement: { algorithm: 'sampling-v1', replacementChance: 100, fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'strict', bossReplacement: 'bosses' } } } },
    { name: 'boss-sampling-unique-combined', masterSeed: 'golden-v1-bosssample-unique', patch: { items: { enabled: false }, fog: { enabled: false },
      enemies: { gwynNerf: 'medium', secondGargoyle: 'keep', placement: { algorithm: 'sampling-v1', replacementChance: 70, fit: 'anywhere', bossReplacement: 'bosses', uniqueBosses: true } } } },
  ],
  'boss-baseline': [
    { name: 'sampling-bosses-fog', masterSeed: 'golden-v1-boss-fog', patch: { enemies: { gwynNerf: 'strong', secondGargoyle: 'replace',
      placement: { algorithm: 'sampling-v1', replacementChance: 70, fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'strict' } } } },
    { name: 'sampling-bosses-combined-only', masterSeed: 'golden-v1-boss-combined', patch: { items: { enabled: false }, fog: { enabled: false },
      enemies: { preventSameEnemy: true, gwynNerf: 'medium', secondGargoyle: 'keep', placement: { algorithm: 'sampling-v1', replacementChance: 100, fit: 'anywhere' } } } },
  ],
};

export function buildSettings(patch) {
  const merged = merge(defaults, patch);
  return JSON.parse(JSON.stringify(merged));
}

async function reconstruct(source, temp) {
  const game = path.join(temp, 'game');
  await cp(path.join(source, executableName), path.join(game, executableName));
  for (const managed of managedRoots) await cp(path.join(source, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
  const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
  const imported = await authority.importPristine(game);
  if (!imported.ok) throw new Error(`pristine import failed: ${imported.code}`);
  return { game, authority };
}

async function run({ authority, game, temp }, request, label, appVersion) {
  const prepared = prepareRun({ gamePath: game, ...request });
  if (!prepared.ok) throw new Error(`${label}: ${prepared.code}: ${prepared.technical}`);
  const workspacePath = path.join(temp, `run-${label}-${Math.random().toString(16).slice(2)}`);
  await mkdir(workspacePath, { recursive: true });
  const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode }, () => {}, {
    getPristineSource: (gamePath) => authority.getPristineSource(gamePath),
    ...(appVersion ? { appVersion } : {}),
  });
  return { prepared, result };
}

async function main() {
  const [mode, argument] = process.argv.slice(2);
  const source = process.env.DS1TR_GOLDEN_GAME_SOURCE;
  if (!['verify', 'record', 'probe', 'grace'].includes(mode) || !source) {
    console.error('Usage: DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> goldenCompatibility.mjs verify|record <profile>|probe <case>');
    process.exit(2);
  }
  const appVersionFlag = process.argv.indexOf('--app-version');
  const appVersion = appVersionFlag > 0 ? process.argv[appVersionFlag + 1] : undefined;
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-golden-'));
  try {
    const context = { ...(await reconstruct(source, temp)), temp };
    if (mode === 'probe') {
      const spec = v1Cases.find((entry) => entry.name === argument) ?? v1Cases[4];
      const settings = buildSettings(spec.patch);
      const request = { masterSeed: spec.masterSeed, ...settings };
      const first = await run(context, request, 'a', appVersion);
      const second = await run(context, request, 'b', appVersion);
      if (!first.result.ok || !second.result.ok) throw new Error(`probe failed: ${first.result.message ?? ''} ${second.result.message ?? ''}`);
      const a = new Map(Object.entries((await packageDigest(first.result.manifest.files, first.result.packagePath)).files));
      const b = new Map(Object.entries((await packageDigest(second.result.manifest.files, second.result.packagePath)).files));
      const differing = [...a.keys()].filter((key) => a.get(key) !== b.get(key));
      console.log(`files ${a.size}; differing between two workspaces: ${differing.join(', ') || 'none'}`);
      return;
    }
    if (mode === 'grace') {
      // Finds a master seed whose custom-pool run uses the one-model grace (docs/ENEMY_PLACEMENT_MODEL.md, 10.5).
      const spec = v1Cases.find((entry) => entry.name === 'custom-pool-model-grace');
      for (let n = 1; n <= 80; n += 1) {
        const request = { masterSeed: `golden-v1-grace-${n}`, ...buildSettings(spec.patch) };
        const { result } = await run(context, request, `grace${n}`, appVersion);
        const grace = result.ok ? Object.entries(result.manifest.enemyPlacement.summary?.maps ?? {}).filter(([, map]) => map.customPoolModelGrace).map(([id]) => id) : ['failed'];
        console.log(`golden-v1-grace-${n}: ${grace.join(', ') || '-'}`);
        if (grace.length > 0 && grace[0] !== 'failed') return;
      }
      return;
    }
    if (mode === 'record') {
      const profile = argument;
      const setName = process.argv[4] && !process.argv[4].startsWith('--') ? process.argv[4] : null;
      const caseList = setName ? v1ExtraCaseSets[setName] : v1Cases;
      if (!caseList) throw new Error(`Unknown extra case set ${setName}.`);
      const target = path.join(fixtureDirectory, setName ? `${profile}.${setName}.json` : `${profile}.json`);
      const exists = await access(target).then(() => true, () => false);
      if (exists) throw new Error(`${target} exists. Golden fixtures are never re-recorded; a changed output needs a NEW generation profile.`);
      const cases = [];
      for (const spec of caseList) {
        const settings = buildSettings(spec.patch);
        const { prepared, result } = await run(context, { masterSeed: spec.masterSeed, ...settings }, spec.name, appVersion);
        if (!result.ok) throw new Error(`${spec.name}: ${result.code}: ${result.message}`);
        const grace = Object.values(result.manifest.enemyPlacement.summary?.maps ?? {}).filter((map) => map.customPoolModelGrace).length;
        cases.push({
          name: spec.name, seedCode: prepared.seedCode, masterSeed: prepared.config.masterSeed, profile: prepared.profile.id,
          settings: JSON.parse(canonicalSettingsJson(prepared.envelope.settings)),
          expected: { ...(await packageDigest(result.manifest.files, result.packagePath)), modelGraceMaps: grace, attempts: result.manifest.attempts.map((attempt) => attempt.result) },
        });
        console.log(`${spec.name}: ${result.manifest.files.length} files, grace maps ${grace}, attempts ${result.manifest.attempts.map((a) => a.result).join(',')}`);
      }
      await mkdir(fixtureDirectory, { recursive: true });
      await writeFile(target, `${JSON.stringify({ profile, recordedAt: new Date().toISOString(), cases }, null, 2)}\n`);
      console.log(`recorded ${cases.length} cases in ${target}`);
      return;
    }
    // verify
    const { readdir } = await import('node:fs/promises');
    let failed = 0;
    for (const file of (await readdir(fixtureDirectory)).filter((name) => name.endsWith('.json')).sort()) {
      const fixture = JSON.parse(await readFile(path.join(fixtureDirectory, file), 'utf8'));
      for (const entry of fixture.cases) {
        const { prepared, result } = await run(context, { seedCode: entry.seedCode }, entry.name, appVersion);
        const actual = result.ok ? await packageDigest(result.manifest.files, result.packagePath) : null;
        const good = result.ok && actual.digest === entry.expected.digest && prepared.profile.id === entry.profile
          && prepared.config.masterSeed === entry.masterSeed && canonicalSettingsJson(prepared.envelope.settings) === JSON.stringify(entry.settings);
        if (!good) {
          failed += 1;
          const differing = actual ? Object.keys(entry.expected.files).filter((key) => entry.expected.files[key] !== actual.files[key]) : [];
          console.log(`FAIL ${fixture.profile}/${entry.name}: ${result.ok ? `digest ${actual.digest} != ${entry.expected.digest}; differing: ${differing.slice(0, 5).join(', ')}` : `${result.code}: ${result.message}`}`);
        } else {
          console.log(`PASS ${fixture.profile}/${entry.name} (${actual.fileCount} files${appVersion ? `, app version ${appVersion}` : ''})`);
        }
      }
    }
    console.log(failed === 0 ? 'RESULT: PASS' : `RESULT: FAIL (${failed})`);
    if (failed > 0) process.exitCode = 1;
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
