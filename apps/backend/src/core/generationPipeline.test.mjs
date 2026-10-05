import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { generateRun } from '../../dist/core/generationPipeline.js';

const sha = (value) => createHash('sha256').update(value).digest('hex');

function config(gamePath, fog = false) {
  return {
    gamePath, masterSeed: 'pipeline-test',
    items: { enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false,
      progressionLogic: true, guaranteeEarlyWeapon: true, balancedEarlyLoot: true,
      randomizeStartingClass: false, randomizeStartingEquipment: false,
      randomizeGifts: false, randomizeEnemyDrops: false, guaranteedEnemyDrops: false,
      randomizeShops: false, fashionSouls: true, soulItems: 'shuffled', startingItems: 'shield-2h' },
    enemies: { enabled: false, randomizeRegularEnemies: false, randomizeBosses: false, scaling: 'vanilla', replaceMimics: true, replaceRespawningMosquitoes: true, preventSameEnemy: false, pinwheelChaos: false, gwynNerf: 'medium', secondGargoyle: 'replace', placement: { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null } },
    fog: { enabled: fog, traversableFogs: true, bossFogs: false, areaWarps: false,
      majorPvpFogs: false, minorPvpFogs: false, lordvesselGates: false,
      randomStart: false, disconnectedFogs: false, noFallBedOfChaos: false,
      scaling: false, glitchedLogic: false, requireLordSouls: true,
      pacifistMode: false, language: 'ENGLISH' },
    output: { includeDlc: true, generateSpoilerLog: false },
  };
}

async function installation(directory) {
  for (const item of ['param/GameParam', 'map/MapStudio', 'script', 'event']) {
    await mkdir(path.join(directory, item), { recursive: true });
  }
  await writeFile(path.join(directory, 'DarkSoulsRemastered.exe'), 'exe');
  await writeFile(path.join(directory, 'param/GameParam/GameParam.parambnd.dcx'), 'param');
}

async function fixture(fn) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-pipeline-'));
  const game = path.join(root, 'game');
  const pristine = path.join(root, 'pristine');
  const workspace = path.join(root, 'workspace');
  await installation(game);
  await installation(pristine);
  try { await fn({ root, game, pristine, workspace }); }
  finally { await rm(root, { recursive: true, force: true }); }
}


const FOG_TXT = 'KeyItems:\n- Name: darkmoon_seance_ring\n  ID: 2:149\nWarps:\nLotLocations:\n  1300020: catacombs\nDefaultFlagCols:\n';
async function fogData(root) {
  const dataRoot = path.join(root, 'fog-data');
  await mkdir(path.join(dataRoot, 'dist'), { recursive: true });
  await writeFile(path.join(dataRoot, 'dist', 'fog.txt'), FOG_TXT);
  return dataRoot;
}

function combinedStub() {
  return async (request, onEvent) => {
    const seedDirectory = path.join(request.workspacePath, `seed_${request.combinedSeed}`);
    await mkdir(path.join(seedDirectory, 'mod'), { recursive: true });
    const placementsPath = path.join(seedDirectory, 'randomizer.json');
    const patchManifestPath = path.join(seedDirectory, 'patch-manifest.json');
    await writeFile(placementsPath, '{}');
    await writeFile(patchManifestPath, JSON.stringify({ schemaVersion: 2,
      patchedMaps: [], events: [], gameParam: null }));
    await writeFile(path.join(seedDirectory, 'mod', 'items.bin'), 'combined');
    onEvent({ type: 'progress', progress: 1, message: 'done' });
    return { ok: true, seedDirectory, workspaceDirectory: request.workspacePath,
      catalogPath: '', placementsPath, patchManifestPath, randomizerVersion: 'test-engine',
      placementHash: '', seed: request.combinedSeed, catalogScanned: false };
  };
}

test('requires a verified pristine source before invoking either engine', async () => fixture(async ({ game, workspace }) => {
  let called = false;
  const result = await generateRun({ config: config(game), workspacePath: workspace }, () => {}, {
    combined: async () => { called = true; throw new Error('unexpected'); },
  });
  assert.equal(result.ok, false);
  assert.equal(result.code, 'PRISTINE_IMPORT_REQUIRED');
  assert.equal(called, false);
}));

test('reports absent FogHost prerequisites before combined generation', async () => fixture(async ({ game, pristine, workspace }) => {
  let called = false;
  const result = await generateRun({ config: config(game, true), workspacePath: workspace }, () => {}, {
    getPristineSource: async () => ({ directory: pristine }),
    getFogPrerequisites: async () => null,
    combined: async () => { called = true; throw new Error('unexpected'); },
  });
  assert.equal(result.ok, false);
  assert.equal(result.code, 'FOG_PREREQUISITES_MISSING');
  assert.equal(called, false);
}));

test('a sampling placement that is infeasible for this seed is retried with a new combined seed and logged', async () => fixture(async ({ game, pristine, workspace }) => {
  const stub = combinedStub();
  let calls = 0;
  const result = await generateRun({ config: config(game), workspacePath: workspace }, () => {}, {
    getPristineSource: async () => ({ directory: pristine }),
    combined: async (request, onEvent) => {
      calls += 1;
      if (calls === 1) return { ok: false, code: 'ENEMY_PLACEMENT_INFEASIBLE', message: 'no placement', retryable: true };
      return stub(request, onEvent);
    },
  });
  assert.equal(result.ok, true);
  assert.equal(calls, 2);
  assert.deepEqual(result.manifest.attempts.map((a) => a.result), ['ENEMY_PLACEMENT_INFEASIBLE', 'success']);
}));

test('a structural placement failure is reported with its own code and is not retried', async () => fixture(async ({ game, pristine, workspace }) => {
  let calls = 0;
  const result = await generateRun({ config: config(game), workspacePath: workspace }, () => {}, {
    getPristineSource: async () => ({ directory: pristine }),
    combined: async () => { calls += 1; return { ok: false, code: 'ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE', message: 'structural', retryable: false }; },
  });
  assert.equal(result.ok, false);
  assert.equal(result.code, 'ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE');
  assert.equal(calls, 1);
}));

test('writes a validated combined package, manifest and progress', async () => fixture(async ({ game, pristine, workspace }) => {
  const events = [];
  const result = await generateRun({ config: config(game), workspacePath: workspace },
    (event) => events.push(event), {
      getPristineSource: async () => ({ directory: pristine, snapshotId: 'snapshot-1' }), combined: combinedStub(),
    });
  assert.equal(result.ok, true);
  assert.equal(result.manifest.engines.combined, 'test-engine');
  assert.equal(result.manifest.pristineSnapshotId, 'snapshot-1');
  assert.deepEqual(result.manifest.enemyPlacement, { algorithm: 'permutation-v1', rngVersion: null, classTableHash: null, summary: null });
  assert.equal(result.manifest.gameTreeRoot, 'mod');
  assert.equal(result.manifest.attempts[0].result, 'success');
  assert.equal(result.manifest.files.some((file) => file.path === 'mod/items.bin'), true);
  assert.equal(JSON.parse(await readFile(result.manifestPath, 'utf8')).masterSeed, 'pipeline-test');
  assert.equal(events.at(-1).progress, 1);
}));

test('retries a Fog seed and retains combined files outside the Fog read set', async () => fixture(async ({ root, game, pristine, workspace }) => {
  const executablePath = path.join(root, 'FogHost.exe');
  await writeFile(executablePath, 'host');
  const dataRoot = await fogData(root);
  let calls = 0;
  const fog = { execute: async (execution) => {
    calls++;
    if (calls === 1) return { schemaVersion: 2, ok: false,
      error: { code: 'FOG_SEED_UNSOLVABLE', retryable: true, retryScope: 'fog', message: 'bad graph' } };
    const relative = 'param/GameParam/GameParam.parambnd.dcx';
    const content = 'fogged';
    await writeFile(path.join(execution.request.stagingDirectory, relative), content);
    return { schemaVersion: 2, ok: true, hostVersion: 'host-test', fogModCommit: 'pin',
      annotationSha256: '', mode: 'write', seed: execution.request.seed, fogOptions: '',
      keyItems: { randomized: false, hash: '' },
      files: [{ path: relative, size: content.length, sha256: sha(content) }], error: null };
  } };
  const result = await generateRun({ config: config(game, true), workspacePath: workspace }, () => {}, {
    getPristineSource: async () => ({ directory: pristine }),
    getFogPrerequisites: async () => ({ executablePath, dataRoot,
      readSet: ['param/GameParam/GameParam.parambnd.dcx'] }),
    combined: combinedStub(), fog,
  });
  assert.equal(result.ok, true);
  assert.equal(calls, 2);
  assert.equal(result.manifest.attempts[0].result, 'FOG_SEED_UNSOLVABLE');
  assert.equal(result.manifest.attempts[1].result, 'success');
  assert.equal(result.manifest.gameTreeRoot, '');
  assert.equal(await readFile(path.join(result.packagePath, 'items.bin'), 'utf8'), 'combined');
  assert.equal(await readFile(path.join(result.packagePath, 'param/GameParam/GameParam.parambnd.dcx'), 'utf8'), 'fogged');
}));

test('boss randomization together with Fog is supported (no rejection; proceeds to Fog prerequisites)', async () => fixture(async ({ game, pristine, workspace }) => {
  const bossesAndFog = config(game, true);
  bossesAndFog.enemies = { enabled: true, randomizeRegularEnemies: false, randomizeBosses: true, scaling: 'vanilla', replaceMimics: true, replaceRespawningMosquitoes: true, preventSameEnemy: false, pinwheelChaos: false, gwynNerf: 'medium', secondGargoyle: 'replace', placement: { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null } };
  const result = await generateRun({ config: bossesAndFog, workspacePath: workspace }, () => {}, {
    getPristineSource: async () => ({ directory: pristine }),
    getFogPrerequisites: async () => null,
    combined: async () => { throw new Error('unexpected'); },
  });
  assert.equal(result.ok, false);
  assert.equal(result.code, 'FOG_PREREQUISITES_MISSING');
  assert.equal(result.manifest.options.enemies.randomizeBosses, true);
}));

test('passes the Fog message language to the prerequisite provider', async () => fixture(async ({ game, pristine, workspace }) => {
  const polish = config(game, true);
  polish.fog.language = 'POLISH';
  let requested = null;
  const result = await generateRun({ config: polish, workspacePath: workspace }, () => {}, {
    getPristineSource: async () => ({ directory: pristine }),
    getFogPrerequisites: async (language) => { requested = language; return null; },
    combined: async () => { throw new Error('unexpected'); },
  });
  assert.equal(result.code, 'FOG_PREREQUISITES_MISSING');
  assert.equal(requested, 'POLISH');
}));

test('a world randomizer preparation failure is shown in plain language; the exact reason goes to the log and the manifest', async () => fixture(async ({ game, pristine, workspace }) => {
  const events = [];
  const result = await generateRun({ config: config(game, true), workspacePath: workspace }, (event) => events.push(event), {
    getPristineSource: async () => ({ directory: pristine }),
    getFogPrerequisites: async () => ({ failure: { code: 'WORLD_DATA_OFFLINE', userMessage: 'The world randomizer files could not be prepared. Check your internet connection and click Retry.', technical: 'https://raw.githubusercontent.com/x: ENOTFOUND' } }),
    combined: async () => { throw new Error('unexpected'); },
  });
  assert.equal(result.ok, false);
  assert.equal(result.code, 'WORLD_DATA_OFFLINE');
  assert.match(result.message, /internet connection/u);
  assert.doesNotMatch(result.message, /ENOTFOUND|githubusercontent|dist|FogMod|FogHost/u);
  assert.ok(events.some((event) => event.type === 'log' && /ENOTFOUND/u.test(event.message)), 'details are logged');
  assert.ok(result.manifest.validation.some((line) => /WORLD_DATA_OFFLINE.*ENOTFOUND/u.test(line)), 'details are kept in the manifest');
}));

test('the preparation step receives the verified pristine snapshot and reports progress in plain language', async () => fixture(async ({ game, pristine, workspace }) => {
  const events = [];
  let context = null;
  await generateRun({ config: config(game, true), workspacePath: workspace }, (event) => events.push(event), {
    getPristineSource: async () => ({ directory: pristine }),
    getFogPrerequisites: async (_language, received) => { context = received; received.onProgress({ fraction: 0.5, message: 'Downloading required world randomizer data...' }); return null; },
    combined: async () => { throw new Error('unexpected'); },
  });
  assert.equal(context.pristineDirectory, pristine);
  assert.ok(events.some((event) => event.type === 'progress' && event.stage === 'fog' && /Downloading required world randomizer data/u.test(event.message)));
}));

test('technical failures are turned into plain messages while the code and detail stay available', async () => fixture(async ({ game, pristine, workspace }) => {
  const events = [];
  const result = await generateRun({ config: config(game, false), workspacePath: workspace }, (event) => events.push(event), {
    getPristineSource: async () => ({ directory: pristine }),
    combined: async () => ({ ok: false, code: 'COMBINED_PROCESS_CRASHED', message: 'DsrDataTool.exe exited with 0xC0000005 at C:/Users/x/runtime' }),
  });
  assert.equal(result.ok, false);
  assert.equal(result.code, 'COMBINED_PROCESS_CRASHED');
  assert.doesNotMatch(result.message, /DsrDataTool|0xC0000005|C:/u);
  assert.match(result.message, /Something went wrong/u);
  assert.ok(events.some((event) => /0xC0000005/u.test(event.message)));
}));

test('Fog runs hand the combined engine the items the world randomizer follows and the lots it can follow them in', async () => fixture(async ({ root, game, pristine, workspace }) => {
  const executablePath = path.join(root, 'FogHost.exe');
  await writeFile(executablePath, 'host');
  const dataRoot = await fogData(root);
  const requests = [];
  const stub = combinedStub();
  await generateRun({ config: config(game, true), workspacePath: workspace }, () => {}, {
    getPristineSource: async () => ({ directory: pristine }),
    getFogPrerequisites: async () => ({ executablePath, dataRoot, readSet: [] }),
    combined: async (request, onEvent) => { requests.push(request); return stub(request, onEvent); },
    fog: { execute: async () => ({ schemaVersion: 2, ok: false, error: { code: 'FOG_ENGINE_FAILED', retryable: false, retryScope: 'none', message: 'stop here' } }) },
  });
  assert.deepEqual(requests[0].fogTrackedItems, { items: [{ equipType: 2, itemId: 149, required: true }], lotStarts: [1300020] });
  // Without the world randomizer nothing is constrained.
  const plain = [];
  await generateRun({ config: config(game, false), workspacePath: path.join(root, 'plain') }, () => {}, {
    getPristineSource: async () => ({ directory: pristine }),
    combined: async (request, onEvent) => { plain.push(request); return stub(request, onEvent); },
  });
  assert.equal(plain[0].fogTrackedItems ?? null, null);
}));

test('a key item outside the world randomizer lots is structural: one attempt, no reseeding, plain message without internals', async () => fixture(async ({ root, game, pristine, workspace }) => {
  const executablePath = path.join(root, 'FogHost.exe');
  await writeFile(executablePath, 'host');
  const dataRoot = await fogData(root);
  let combinedCalls = 0;
  let fogCalls = 0;
  const stub = combinedStub();
  const events = [];
  const result = await generateRun({ config: config(game, true), workspacePath: workspace }, (event) => events.push(event), {
    getPristineSource: async () => ({ directory: pristine }),
    getFogPrerequisites: async () => ({ executablePath, dataRoot, readSet: [] }),
    combined: async (request, onEvent) => { combinedCalls += 1; return stub(request, onEvent); },
    fog: { execute: async () => {
      fogCalls += 1;
      return { schemaVersion: 2, ok: false, error: { code: 'FOG_UPSTREAM_INCOMPATIBLE', retryable: true, retryScope: 'combined', message: "Couldn't find darkmoon_seance_ring in item lots" } };
    } },
  });
  assert.equal(result.ok, false);
  assert.equal(result.code, 'FOG_UPSTREAM_INCOMPATIBLE');
  assert.equal(combinedCalls, 1, 'the combined engine is not run again');
  assert.equal(fogCalls, 1);
  assert.equal(result.manifest.attempts.length, 1);
  assert.match(result.message, /cannot be combined yet/u);
  assert.doesNotMatch(result.message, /darkmoon|ItemLot|item lots|FOG_UPSTREAM|try another seed/iu);
  assert.ok(events.some((event) => event.type === 'log' && /darkmoon_seance_ring/u.test(event.message)), 'the exact reason is in the log');
  assert.ok(result.manifest.validation.some((line) => /darkmoon_seance_ring/u.test(line)), 'and in the manifest');
}));
