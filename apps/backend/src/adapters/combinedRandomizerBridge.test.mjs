import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, readdir, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  defaultDataToolDllPath,
  executeCombinedGeneration,
  supportedCatalogSchemaVersion,
} from '../../dist/adapters/combinedRandomizerBridge.js';
import { createPipelinePlan } from '../../dist/core/pipeline.js';

/**
 * These tests execute the real vendored upstream core (`generate()` and
 * `writeOutput()`) against a synthetic catalog, in a real temporary
 * workspace. Only the `DsrDataTool` process layer is faked: running it needs
 * a built .NET assembly and a real Dark Souls installation, neither of which
 * exists in a test environment. Everything the bridge itself decides — order
 * of operations, path planning, argument vectors, failure mapping — is
 * therefore exercised for real.
 */

const dataToolPaths = {
  dotnetExecutablePath: 'C:\\fixture\\dotnet.exe',
  dataToolDllPath: 'C:\\fixture\\DsrDataTool.dll',
};

function enemySlot(index, modelName, paramId) {
  return {
    id: `m10_01_00_00:${modelName}_000${index}`,
    mapId: 'm10_01_00_00',
    entityId: 1000 + index,
    modelName,
    npcParamId: paramId,
    thinkParamId: paramId,
    baseHp: 100 + index,
    teamType: 0,
    maxSize: 'medium',
    size: 'medium',
    dummy: false,
    battleGoalId: 1000,
    position: { x: 1 + index, y: 2, z: 3 },
    rotation: { x: 0, y: 0, z: 0 },
  };
}

/**
 * The minimum catalog shape that makes upstream report
 * `dataStatus: "extracted"`: schema 17 plus non-empty `enemySlots` and
 * `enemyArchetypes`. Item/boss/shop collections are absent, which upstream
 * handles by producing no placements for those categories.
 */
function extractedCatalog() {
  return {
    schemaVersion: supportedCatalogSchemaVersion,
    maps: [{ id: 'm10_01_00_00', name: 'Undead Burg' }],
    enemySlots: [
      enemySlot(0, 'c2240', 224000),
      enemySlot(1, 'c2250', 225000),
      enemySlot(2, 'c2260', 226000),
    ],
    enemyArchetypes: [
      { modelName: 'c2240', npcParamId: 224000, thinkParamId: 224000, name: 'Hollow', size: 'medium' },
      { modelName: 'c2250', npcParamId: 225000, thinkParamId: 225000, name: 'Skeleton', size: 'medium' },
    ],
  };
}

function request(overrides = {}) {
  return {
    gamePath: 'C:\\fixture\\DarkSoulsRemastered',
    workspacePath: 'C:\\fixture\\workspace',
    masterSeed: 'fixture-seed-1',
    combinedSeed: 'fixture-seed-1',
    items: {
      enabled: true,
      randomizeWorldItems: true,
      randomizeProtectedItems: false,
      progressionLogic: true,
      guaranteeEarlyWeapon: true,
      balancedEarlyLoot: true,
      randomizeStartingClass: false,
      randomizeStartingEquipment: false,
      randomizeGifts: false,
      randomizeEnemyDrops: false,
      guaranteedEnemyDrops: false,
      randomizeShops: false,
    },
    enemies: {
      enabled: true,
      randomizeRegularEnemies: true,
      randomizeBosses: true,
      scaling: 'area',
    },
    ...overrides,
  };
}

/**
 * Fake `DsrDataTool`. Records every invocation and, for `patch-enemies`,
 * runs an assertion hook from inside the call — the only place the bridge's
 * ordering can actually be observed.
 */
function fakeDataTool({ result, onInvoke } = {}) {
  const invocations = [];
  return {
    invocations,
    async execute(executionRequest, onEvent) {
      const command = executionRequest.plannedArguments[0];
      invocations.push({ command, ...executionRequest });
      if (onInvoke) await onInvoke(command, executionRequest);
      onEvent?.({ type: 'progress', command, status: 'started' });
      onEvent?.({ type: 'log', stream: 'stdout', message: `${command} completed\n` });
      const outcome = result?.(command) ?? { ok: true, stdout: '', stderr: '', exitCode: 0 };
      onEvent?.({
        type: 'progress',
        command,
        status: outcome.ok ? 'succeeded' : 'failed',
      });
      return outcome;
    },
  };
}

/** A temporary workspace plus a fixture game directory with contents. */
async function makeWorkspace(t, { catalog = extractedCatalog() } = {}) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-bridge-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const workspacePath = path.join(root, 'workspace');
  const gamePath = path.join(root, 'game');
  await mkdir(workspacePath, { recursive: true });
  await mkdir(path.join(gamePath, 'map', 'MapStudio'), { recursive: true });
  await writeFile(path.join(gamePath, 'DarkSoulsRemastered.exe'), 'fixture-executable', 'utf8');
  await writeFile(path.join(gamePath, 'map', 'MapStudio', 'm10_01_00_00.msb'), 'fixture-map', 'utf8');
  if (catalog) {
    await writeFile(
      path.join(workspacePath, 'dsr-catalog.json'),
      JSON.stringify(catalog),
      'utf8',
    );
  }
  return { root, workspacePath, gamePath };
}

async function snapshotDirectory(directory) {
  const entries = await readdir(directory, { withFileTypes: true, recursive: true });
  const snapshot = [];
  for (const entry of entries) {
    const absolute = path.join(entry.parentPath ?? entry.path, entry.name);
    if (!entry.isFile()) continue;
    snapshot.push([
      path.relative(directory, absolute),
      await readFile(absolute, 'utf8'),
    ]);
  }
  return snapshot.sort(([left], [right]) => left.localeCompare(right));
}

async function exists(target) {
  try {
    await stat(target);
    return true;
  } catch {
    return false;
  }
}

test('generates a workspace package end to end and drives only scan/patch-enemies', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t);
  const expectedSeedDirectory = path.join(workspacePath, 'seed_fixture-seed-1');
  let placementsExistedAtPatchTime = null;

  const dataTool = fakeDataTool({
    async onInvoke(command, executionRequest) {
      if (command !== 'patch-enemies') return;
      // Observed from inside the call: generation must already have written
      // the placements file the patch step consumes.
      placementsExistedAtPatchTime = await exists(
        path.join(expectedSeedDirectory, 'randomizer.json'),
      );
      const outputIndex = executionRequest.plannedArguments.indexOf('--output');
      await writeFile(
        path.join(executionRequest.plannedArguments[outputIndex + 1], 'patch-manifest.json'),
        JSON.stringify({ changedFiles: [] }),
        'utf8',
      );
    },
  });

  const events = [];
  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    (event) => events.push(event),
    { ...dataToolPaths, dependencies: { dataTool } },
  );

  assert.equal(result.ok, true, result.ok ? '' : `${result.code}: ${result.message}`);
  assert.equal(result.seedDirectory, expectedSeedDirectory);
  assert.equal(result.seed, 'fixture-seed-1');
  assert.equal(result.randomizerVersion, '0.5.66');
  assert.match(result.placementHash, /^[0-9a-f]{64}$/u);
  assert.equal(result.catalogScanned, false, 'an existing catalog must be reused');
  assert.equal(placementsExistedAtPatchTime, true);

  // A real package, written by upstream's own `writeOutput`.
  const packaged = (await readdir(expectedSeedDirectory)).sort();
  assert.deepEqual(packaged, ['README.txt', 'config.json', 'patch-manifest.json', 'randomizer.json']);
  assert.equal(result.patchManifestPath, path.join(expectedSeedDirectory, 'patch-manifest.json'));

  const placements = JSON.parse(await readFile(result.placementsPath, 'utf8'));
  assert.equal(placements.dataStatus, 'extracted');
  assert.equal(placements.placements.enemies.length, 3);

  // No spoiler file: no request field carries that option (Blocker 3), so the
  // bridge pins it off rather than inheriting upstream's `true` default.
  assert.equal(await exists(path.join(expectedSeedDirectory, 'spoiler.txt')), false);

  // Exactly one upstream command, with the arguments the planner produced.
  assert.deepEqual(dataTool.invocations.map((call) => call.command), ['patch-enemies']);
  assert.deepEqual(dataTool.invocations[0].plannedArguments, [
    'patch-enemies',
    '--game', gamePath,
    '--catalog', path.join(workspacePath, 'dsr-catalog.json'),
    '--placements', path.join(expectedSeedDirectory, 'randomizer.json'),
    '--output', expectedSeedDirectory,
  ]);
  assert.equal(dataTool.invocations[0].dotnetExecutablePath, dataToolPaths.dotnetExecutablePath);
  assert.equal(dataTool.invocations[0].dataToolDllPath, dataToolPaths.dataToolDllPath);

  // Progress is monotonic and ends at completion.
  const progress = events.filter((event) => event.type === 'progress').map((event) => event.progress);
  assert.deepEqual(progress, [...progress].sort((left, right) => left - right));
  assert.equal(progress.at(-1), 1);
  assert.ok(events.some((event) => event.type === 'log' && event.message.includes('patch-enemies')));
});

test('leaves the live game directory untouched', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t);
  const before = await snapshotDirectory(gamePath);

  // Guards the guard: a `readdir` that ignored `recursive` would make the
  // comparison below pass while comparing almost nothing.
  assert.deepEqual(
    before.map(([relative]) => relative.split(path.sep).join('/')),
    ['DarkSoulsRemastered.exe', 'map/MapStudio/m10_01_00_00.msb'],
  );

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool: fakeDataTool() } },
  );

  assert.equal(result.ok, true);
  assert.deepEqual(await snapshotDirectory(gamePath), before);
});

test('runs scan first when the workspace has no catalog yet', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t, { catalog: null });
  const catalogPath = path.join(workspacePath, 'dsr-catalog.json');

  const dataTool = fakeDataTool({
    async onInvoke(command) {
      if (command !== 'scan') return;
      await writeFile(catalogPath, JSON.stringify(extractedCatalog()), 'utf8');
    },
  });

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool } },
  );

  assert.equal(result.ok, true, result.ok ? '' : `${result.code}: ${result.message}`);
  assert.equal(result.catalogScanned, true);
  assert.deepEqual(dataTool.invocations.map((call) => call.command), ['scan', 'patch-enemies']);
  assert.deepEqual(dataTool.invocations[0].plannedArguments, [
    'scan', '--game', gamePath, '--output', catalogPath,
  ]);
});

test('reports CATALOG_UNAVAILABLE when scan succeeds without producing a catalog', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t, { catalog: null });

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool: fakeDataTool() } },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'CATALOG_UNAVAILABLE');
});

test('plans the package directory from the normalized seed, not the raw request seed', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t);
  const dataTool = fakeDataTool();

  // Upstream `normalizeConfig` trims the seed and derives its own
  // `seed_<safeSeed>` directory from the trimmed value. Planning from the raw
  // value would produce `seed__fixture-seed-1__` and point `--placements` at
  // a file upstream never wrote.
  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath, combinedSeed: '  fixture-seed-1  ' }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool } },
  );

  assert.equal(result.ok, true, result.ok ? '' : `${result.code}: ${result.message}`);
  assert.equal(result.seed, 'fixture-seed-1');
  assert.equal(result.seedDirectory, path.join(workspacePath, 'seed_fixture-seed-1'));
  assert.equal(await exists(result.placementsPath), true);
  assert.equal(
    dataTool.invocations[0].plannedArguments[dataTool.invocations[0].plannedArguments.indexOf('--placements') + 1],
    result.placementsPath,
  );
});

test('is deterministic for the same request', async (t) => {
  const first = await makeWorkspace(t);
  const second = await makeWorkspace(t);

  const runOne = await executeCombinedGeneration(
    request({ gamePath: first.gamePath, workspacePath: first.workspacePath }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool: fakeDataTool() } },
  );
  const runTwo = await executeCombinedGeneration(
    request({ gamePath: second.gamePath, workspacePath: second.workspacePath }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool: fakeDataTool() } },
  );

  assert.equal(runOne.ok && runTwo.ok, true);
  assert.equal(runOne.placementHash, runTwo.placementHash);
  assert.deepEqual(
    JSON.parse(await readFile(runOne.placementsPath, 'utf8')).placements,
    JSON.parse(await readFile(runTwo.placementsPath, 'utf8')).placements,
  );

  const firstConfig = JSON.parse(await readFile(path.join(runOne.seedDirectory, 'config.json'), 'utf8'));
  const secondConfig = JSON.parse(await readFile(path.join(runTwo.seedDirectory, 'config.json'), 'utf8'));
  assert.equal(firstConfig.seed, 'fixture-seed-1');
  assert.equal(firstConfig.randomizeItems, true);
  assert.equal(firstConfig.randomizeEnemies, true);
  assert.equal(firstConfig.generateSpoilerLog, false);
  assert.equal(firstConfig.outputDirectory, first.workspacePath);
  assert.deepEqual(
    { ...firstConfig, gameDirectory: '', outputDirectory: '' },
    { ...secondConfig, gameDirectory: '', outputDirectory: '' },
  );
});

test('rejects a prototype result without writing a package or spawning the tool', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t, {
    catalog: { schemaVersion: supportedCatalogSchemaVersion, enemySlots: [], enemyArchetypes: [] },
  });
  const dataTool = fakeDataTool();

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool } },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'PROTOTYPE_RESULT_REJECTED');
  assert.deepEqual(dataTool.invocations, [], 'patch-enemies must not run on prototype data');
  assert.equal(await exists(path.join(workspacePath, 'seed_fixture-seed-1')), false);
});

test('rejects an obsolete catalog schema before generating', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t, {
    catalog: { ...extractedCatalog(), schemaVersion: 16 },
  });
  const dataTool = fakeDataTool();

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool } },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'CATALOG_STALE');
  assert.deepEqual(dataTool.invocations, []);
});

test('reports CATALOG_UNREADABLE for a malformed catalog file', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t, { catalog: null });
  await writeFile(path.join(workspacePath, 'dsr-catalog.json'), '{ not json', 'utf8');

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool: fakeDataTool() } },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'CATALOG_UNREADABLE');
});

test('rejects a request with no randomization category enabled', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t);
  const dataTool = fakeDataTool();

  const result = await executeCombinedGeneration(
    request({
      gamePath,
      workspacePath,
      items: { ...request().items, enabled: false },
      enemies: { ...request().enemies, enabled: false },
    }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool } },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'NO_RANDOMIZATION_FEATURES_ENABLED');
  assert.deepEqual(dataTool.invocations, []);
});

test('reports INVALID_CONFIG for a seed upstream will not accept', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t);
  const dataTool = fakeDataTool();

  // Upstream requires /^[\w.-]{1,64}$/. The bridge surfaces the rejection
  // instead of silently rewriting the seed: deriving a compliant token is
  // integration Blocker 2, owned by the seed boundary, not by this bridge.
  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath, combinedSeed: 'seed with spaces!' }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool } },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'INVALID_CONFIG');
  assert.ok(result.validationErrors.length > 0);
  assert.deepEqual(dataTool.invocations, []);
  assert.equal(await exists(path.join(workspacePath, 'seed_seed with spaces!')), false);
});

test('refuses a workspace inside the live game directory before running anything', async (t) => {
  const { gamePath } = await makeWorkspace(t);
  const dataTool = fakeDataTool();

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath: path.join(gamePath, 'randomizer-workspace') }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool } },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'WORKSPACE_INSIDE_GAME_DIRECTORY');
  assert.deepEqual(dataTool.invocations, []);
  assert.equal(await exists(path.join(gamePath, 'randomizer-workspace')), false);
});

test('stops after scan process failure and preserves its diagnostics', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t, { catalog: null });
  const before = await snapshotDirectory(gamePath);
  const dataTool = fakeDataTool({
    result: (command) => {
      assert.equal(command, 'scan');
      return {
        ok: false,
        type: 'process-failure',
        stdout: 'examined 2 maps\n',
        stderr: 'unable to read map data\n',
        exitCode: 7,
      };
    },
  });

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    { ...dataToolPaths, dependencies: { dataTool } },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'DATA_TOOL_FAILED');
  assert.equal(result.retryable, false);
  assert.equal(result.command, 'scan');
  assert.equal(result.exitCode, 7);
  assert.equal(result.stdout, 'examined 2 maps\n');
  assert.equal(result.stderr, 'unable to read map data\n');
  assert.deepEqual(dataTool.invocations.map((call) => call.command), ['scan']);
  assert.equal(await exists(path.join(workspacePath, 'seed_fixture-seed-1')), false);
  assert.deepEqual(await snapshotDirectory(gamePath), before);
});

test('forwards DsrDataTool stdout and stderr with their log levels', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t);
  const events = [];
  const dataTool = {
    async execute(executionRequest, onEvent) {
      assert.equal(executionRequest.plannedArguments[0], 'patch-enemies');
      onEvent({ type: 'log', command: 'patch-enemies', stream: 'stdout', message: 'patched maps\n' });
      onEvent({ type: 'log', command: 'patch-enemies', stream: 'stderr', message: 'skipped slot\n' });
      return { ok: true, stdout: 'patched maps\n', stderr: 'skipped slot\n', exitCode: 0 };
    },
  };

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    (event) => events.push(event),
    { ...dataToolPaths, dependencies: { dataTool } },
  );

  assert.equal(result.ok, true);
  assert.ok(events.some((event) => event.type === 'log'
    && event.level === 'info' && event.message === 'patched maps'));
  assert.ok(events.some((event) => event.type === 'log'
    && event.level === 'warning' && event.message === 'skipped slot'));
});

test('maps a spawn failure to DATA_TOOL_MISSING', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t);

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    {
      ...dataToolPaths,
      dependencies: {
        dataTool: fakeDataTool({
          result: () => ({
            ok: false,
            type: 'spawn-failure',
            stdout: '',
            stderr: '',
            exitCode: null,
            message: 'spawn ENOENT',
          }),
        }),
      },
    },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'DATA_TOOL_MISSING');
  assert.equal(result.command, 'patch-enemies');
  assert.match(result.message, /spawn ENOENT/u);
});

test('maps a non-zero exit to DATA_TOOL_FAILED and keeps the process output', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t);

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    {
      ...dataToolPaths,
      dependencies: {
        dataTool: fakeDataTool({
          result: () => ({
            ok: false,
            type: 'process-failure',
            stdout: 'reading maps\n',
            stderr: 'Patch output cannot be placed inside the game installation.\n',
            exitCode: 1,
          }),
        }),
      },
    },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'DATA_TOOL_FAILED');
  assert.equal(result.exitCode, 1);
  assert.match(result.stderr, /Patch output cannot be placed/u);
  assert.match(result.stdout, /reading maps/u);
});

test('reports a missing vendored core without touching the filesystem', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t);
  const dataTool = fakeDataTool();

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    {
      ...dataToolPaths,
      dependencies: {
        dataTool,
        loadCore: async () => ({
          ok: false,
          code: 'JS_CORE_MISSING',
          message: 'Could not load the vendored combined-engine core module.',
          specifier: 'file:///absent/core/generator.js',
        }),
      },
    },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'JS_CORE_MISSING');
  assert.equal(result.specifier, 'file:///absent/core/generator.js');
  assert.deepEqual(dataTool.invocations, []);
});

test('reports GENERATION_FAILED as retryable when upstream gives up on a seed', async (t) => {
  const { workspacePath, gamePath } = await makeWorkspace(t);
  const dataTool = fakeDataTool();

  const result = await executeCombinedGeneration(
    request({ gamePath, workspacePath }),
    () => {},
    {
      ...dataToolPaths,
      dependencies: {
        dataTool,
        loadCore: async () => ({
          ok: true,
          coreRoot: 'fixture',
          core: {
            randomizerVersion: '0.5.66',
            defaultConfig: {},
            normalizeConfig: (input) => ({ ...input, seed: String(input.seed).trim() }),
            validateConfig: () => [],
            generate: () => {
              throw new Error('Could not create a safe boss permutation.');
            },
            writeOutput: async () => {
              throw new Error('writeOutput must not run after a generation failure.');
            },
          },
        }),
      },
    },
  );

  assert.equal(result.ok, false);
  assert.equal(result.code, 'GENERATION_FAILED');
  assert.equal(result.retryable, true);
  assert.match(result.message, /safe boss permutation/u);
  assert.deepEqual(dataTool.invocations, []);
});

test('the default DsrDataTool path points at the documented vendored build output', () => {
  const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../');
  assert.equal(
    path.relative(repositoryRoot, defaultDataToolDllPath).split(path.sep).join('/'),
    'engines/combined/vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll',
  );
});

test('the pipeline engine probe finds the vendored combined engine', async () => {
  const plan = await createPipelinePlan({
    gamePath: 'C:\\DSR',
    masterSeed: 'fixture-seed-1',
    items: request().items,
    enemies: request().enemies,
    fog: {
      enabled: false,
      traversableFogs: false,
      bossFogs: false,
      areaWarps: false,
      majorPvpFogs: false,
      minorPvpFogs: false,
      lordvesselGates: false,
      randomStart: false,
      disconnectedFogs: false,
      noFallBedOfChaos: false,
      scaling: false,
      glitchedLogic: false,
      requireLordSouls: false,
      pacifistMode: false,
      language: 'ENGLISH',
    },
    output: { includeDlc: true, generateSpoilerLog: false },
  });

  const combined = plan.engineStatus.find((status) => status.name === 'combined');
  // The probe previously looked for `vendor/src/cli.js`, which is
  // deliberately excluded from the vendor set, so it always reported
  // `missing`. `vendor/src/core/generator.js` is committed, so this is
  // deterministic; the built DLL is gitignored and intentionally not asserted.
  assert.equal(combined.mode, 'source');
  assert.notEqual(combined.mode, 'missing');
});
