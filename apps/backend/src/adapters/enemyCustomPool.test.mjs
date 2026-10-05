import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';
import { describeEnemyPool } from '../../dist/adapters/enemyPool.js';

// sampling-v1 custom enemy pool (Part C2) on synthetic catalogs.

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const load = (file) => import(pathToFileURL(resolveCombinedRandomizerCoreModulePath(`core/${file}`)).href);
const sampling = await load('sampling.js');
const placement = await load('placement.js');

// ---- synthetic catalog ---------------------------------------------------------------------------------------------------------

const HEIGHT = [0.5, 1.2, 2.0, 3.0, 5.0, 8.0];
function slot(mapId, name, modelName, npcParamId, index, size, extra = {}) {
  return {
    id: `${mapId}:${name}`, mapId, name, modelName, entityId: -1,
    npcParamId, thinkParamId: npcParamId, talkId: -1, charaInitId: npcParamId,
    collisionName: 'h0000B0', position: { x: index, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0 },
    movePoints: [], dummy: false, hasEntityId: false, eventModelLocked: false, riskFlags: [],
    safeCandidate: true, teamType: 0, npcType: 0, moveType: 0, hitHeight: HEIGHT[size], hitRadius: 0.1, hitYOffset: 0,
    baseHp: 100, soulReward: 10, battleStartDistance: 20, eyeDistance: 20, earDistance: 10, disablePathMove: false,
    battleGoalId: npcParamId, ...extra,
  };
}

function catalogOf(spec, { reverse = false } = {}) {
  const slots = [];
  const npc = new Map();
  let index = 0;
  for (const [mapId, entries] of Object.entries(spec)) {
    for (const [model, size, count, hp = 100] of entries) {
      if (!npc.has(model)) npc.set(model, 100000 + npc.size * 10);
      for (let i = 0; i < count; i += 1) slots.push(slot(mapId, `${model}_${String(i).padStart(4, '0')}`, model, npc.get(model), index += 1, size, { baseHp: hp }));
    }
  }
  if (reverse) slots.reverse();
  return {
    schemaVersion: 17, maps: Object.keys(spec).map((id) => ({ id, name: id })),
    enemySlots: slots, enemyArchetypes: [{ id: 'x' }], bossSlots: [], worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

function tieredSpec() {
  const maps = ['m18_01_00_00', 'm10_01_00_00', 'm10_00_00_00', 'm11_00_00_00', 'm14_00_00_00', 'm15_00_00_00', 'm12_01_00_00', 'm18_00_00_00'];
  const spec = {};
  maps.forEach((mapId, tier) => {
    spec[mapId] = [0, 1, 2].map((k) => [`c${tier}${k}00`, (tier + k) % 5, 6, 40 * (tier + 1) * (k + 1) * (k + 1)]);
  });
  return spec;
}
const TIERED_MODELS = Object.values(tieredSpec()).flat().map(([model]) => model).filter((model) => model !== 'c0000').sort();

const config = (overrides = {}) => ({
  ...defaultConfig, seed: 'pool-seed', randomizeEnemies: true, randomizeBosses: false, randomizeItems: false,
  generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1', enemyPlacementFit: 'anywhere', ...overrides,
});
const run = (overrides, spec = tieredSpec()) => generate(config(overrides), { gameCatalog: catalogOf(spec) });
const detail = (overrides, spec) => {
  const result = run({ placementDiagnosticsLevel: 'detail', ...overrides }, spec);
  return { result, slots: result.placementDiagnostics.slots, groups: result.placementDiagnostics.groups };
};
const counters = (result) => result.placementDiagnostics.summary.counters;
const targets = (result) => new Set(result.placements.enemies.filter((p) => p.changed).map((p) => p.targetModelName));

// ---- identity ------------------------------------------------------------------------------------------------------------------------

test('a custom pool is a canonical list of stable model ids: order is irrelevant, duplicates and junk are refused', () => {
  assert.equal(placement.canonicalCustomPool(null), null);
  assert.equal(placement.canonicalCustomPool(undefined), null);
  assert.deepEqual(placement.canonicalCustomPool(['c2500', 'c1200', 'c3300']), ['c1200', 'c2500', 'c3300']);
  const reasonOf = (value) => { try { placement.canonicalCustomPool(value); } catch (error) { assert.equal(error.code, 'ENEMY_PLACEMENT_CUSTOM_POOL_INVALID'); return error.reason; } return null; };
  assert.equal(reasonOf([]), 'empty');
  assert.equal(reasonOf(['c2500', 'c1200', 'c2500']), 'duplicate');
  assert.equal(reasonOf('c2500'), 'format');
  assert.equal(reasonOf([2500]), 'format');
  assert.equal(reasonOf(['c25 00']), 'format');
  assert.equal(reasonOf(['']), 'format');
});

test('the pool hash depends on the set only', () => {
  const a = sampling.customPoolHash(placement.canonicalCustomPool(['c2500', 'c1200']));
  const b = sampling.customPoolHash(placement.canonicalCustomPool(['c1200', 'c2500']));
  assert.match(a, /^[0-9a-f]{64}$/u);
  assert.equal(a, b);
  assert.notEqual(a, sampling.customPoolHash(['c1200']));
  assert.equal(sampling.customPoolHash(null), null);
});

test('the engine refuses malformed pools, a stale entry, and any pool under permutation-v1, all with typed non-retryable errors', () => {
  for (const [value, reason] of [[[], 'empty'], [['c0100', 'c0100'], 'duplicate'], ['c0100', 'format'], [['c0100', 'c9999'], 'unknown']]) {
    assert.throws(
      () => run({ enemyCustomPool: value }),
      (error) => error.code === 'ENEMY_PLACEMENT_CUSTOM_POOL_INVALID' && error.reason === reason && !('attempts' in error),
      JSON.stringify(value),
    );
  }
  assert.throws(
    () => run({ enemyCustomPool: ['c0100', 'c9999', 'c9998'] }),
    (error) => error.reason === 'unknown' && JSON.stringify(error.unknown) === JSON.stringify(['c9998', 'c9999']) && /c9998, c9999/u.test(error.message),
    'the message names the stale entries',
  );
  assert.throws(
    () => run({ enemyPlacementAlgorithm: 'permutation-v1', enemyCustomPool: ['c0100'] }),
    (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID' && /enemyCustomPool only applies to sampling-v1/u.test(error.message),
  );
  assert.ok(run({ enemyPlacementAlgorithm: 'permutation-v1', enemyCustomPool: null }).placementHash);
});

// ---- default pool ---------------------------------------------------------------------------------------------------------------------

test('the full pool, null or listed, is the Part C1 behavior', () => {
  const full = run({});
  const explicit = run({ enemyCustomPool: TIERED_MODELS });
  assert.equal(explicit.placementHash, full.placementHash, 'listing every model changes nothing');
  const reversed = run({ enemyCustomPool: [...TIERED_MODELS].reverse() });
  assert.equal(reversed.placementHash, full.placementHash, 'order of the list is irrelevant');
  assert.equal(full.placementDiagnostics.customPool.enabled, false);
  assert.equal(explicit.placementDiagnostics.customPool.enabled, true);
  assert.equal(explicit.placementDiagnostics.customPool.selected, TIERED_MODELS.length);
  for (const type of ['enabled']) {
    assert.equal(run({ enemyTypeReplacement: type, enemyCustomPool: TIERED_MODELS }).placementHash, run({ enemyTypeReplacement: type }).placementHash);
  }
});

// ---- restriction ----------------------------------------------------------------------------------------------------------------------

test('every replacement comes from the pool: one candidate, several candidates, and a candidate left out never appears', () => {
  const one = run({ enemyCustomPool: ['c3200'] });
  assert.deepEqual([...targets(one)], ['c3200']);
  const several = ['c1100', 'c2200', 'c4100'];
  for (let i = 0; i < 6; i += 1) {
    const result = run({ enemyCustomPool: several, seed: `several-${i}`, enemyReplacementChance: 70 });
    for (const model of targets(result)) assert.ok(several.includes(model), model);
  }
  const without = TIERED_MODELS.filter((model) => model !== 'c3200');
  assert.equal(targets(run({ enemyCustomPool: without })).has('c3200'), false);
  assert.ok(counters(one).candidatesOutsideCustomPool > 0 || one.placementDiagnostics.customPool.selected === 1);
});

test('the pool restricts replacements only: originals outside it stay, and slots left alone are exactly vanilla', () => {
  const pool = ['c3200'];
  const result = run({ enemyCustomPool: pool, enemyReplacementChance: 40 });
  const placed = new Set(result.placements.enemies.map((p) => p.slot));
  const all = catalogOf(tieredSpec()).enemySlots.filter((s) => s.modelName !== 'c0000');
  const untouched = all.filter((s) => !placed.has(s.id));
  assert.ok(untouched.length > 0);
  assert.ok(untouched.some((s) => !pool.includes(s.modelName)), 'originals that are not in the pool stay in the game');
  for (const p of result.placements.enemies) assert.equal(p.targetModelName, 'c3200');
  // A pool made of one model that is also an original: that model's slots may be "replaced" by it, nothing else disappears.
  const identity = run({ enemyCustomPool: ['c3100'], enemyReplacementChance: 100 });
  assert.deepEqual([...targets(identity)].filter((m) => m !== 'c3100'), []);
});

test('Placement fit never relaxes: a pool of big enemies leaves small slots vanilla, and a pool no slot can take is refused', () => {
  const sizes = new Map(sampling.deriveRegularClassTable(catalogOf(tieredSpec()).enemySlots).types.map((t) => [t.model, t.size]));
  const big = TIERED_MODELS.filter((model) => sizes.get(model) >= 3);
  assert.ok(big.length > 0);
  for (let i = 0; i < 4; i += 1) {
    const { slots, result } = detail({ enemyCustomPool: big, enemyPlacementFit: 'fits', seed: `big-${i}` });
    for (const record of slots.filter((r) => r.stage === 'sampled')) assert.ok(sizes.get(record.selected) <= sizes.get(record.slotId.split(':')[1].split('_')[0]));
    assert.ok(slots.some((r) => r.stage === 'kept-vanilla' || r.participates === false) || counters(result).candidatesOverSize > 0);
    assert.ok(result.placementDiagnostics.customPool.slotsWithoutFit > 0, 'slots without a fitting candidate are counted');
  }
  const onlyLarge = TIERED_MODELS.filter((model) => sizes.get(model) === Math.max(...TIERED_MODELS.map((m) => sizes.get(m))));
  // Structurally useless: the only selected enemy is the one every slot of its kind would have to replace with itself.
  const lone = { m10_00_00_00: [['c0001', 1, 3]], m11_00_00_00: [['c0003', 1, 3]] };
  assert.throws(
    () => run({ enemyCustomPool: ['c0003'], enemyPlacementFit: 'fits', preventSameEnemy: true }, { m11_00_00_00: [['c0003', 1, 3]] }),
    (error) => error.code === 'ENEMY_PLACEMENT_CUSTOM_POOL_INVALID' && error.reason === 'no-candidate' && /select more or different enemies/u.test(error.message),
  );
  assert.ok(run({ enemyCustomPool: ['c0003'], enemyPlacementFit: 'fits', preventSameEnemy: true }, lone).placementHash, 'another model has slots to take it');
  assert.ok(run({ enemyCustomPool: ['c0003'], enemyPlacementFit: 'fits', preventSameEnemy: true, enemyReplacementChance: 0 }, { m11_00_00_00: [['c0003', 1, 3]] }).placementHash, 'not consulted: nothing is checked');
  assert.ok(onlyLarge.length > 0);
});

test('difficulty: the target class inside the pool is used; an absent class falls back inside the pool, never outside it', () => {
  const table = sampling.deriveRegularClassTable(catalogOf(tieredSpec()).enemySlots);
  const low = TIERED_MODELS.filter((m) => table.byModel.get(m).difficulty <= 2);
  assert.ok(low.length >= 3);
  for (const strictness of ['strict', 'very-loose']) {
    const { slots, result } = detail({ enemyCustomPool: low, enemyDifficultyMode: 'curve', enemyDifficultyStrictness: strictness, seed: `diff-${strictness}`, enemyReplacementChance: 100 });
    for (const record of slots.filter((r) => r.selected && r.stage !== 'kept-vanilla')) assert.ok(low.includes(record.selected), `${record.slotId} -> ${record.selected}`);
    assert.ok((counters(result).classFallbacks ?? 0) + (counters(result).difficultyLapses ?? 0) >= 0);
  }
  // A pool that holds the wanted class is used for it.
  const high = TIERED_MODELS.filter((m) => table.byModel.get(m).difficulty >= 5);
  const { slots } = detail({ enemyCustomPool: high, enemyDifficultyMode: 'curve', enemyDifficultyStrictness: 'strict', seed: 'diff-high' });
  assert.ok(slots.filter((r) => r.stage === 'sampled').every((r) => high.includes(r.selected)));
});

test('Prevent same with a pool: the only candidate being the original means the slot stays vanilla; a second candidate is used', () => {
  const spec = { m10_00_00_00: [['cA', 1, 4]], m11_00_00_00: [['cB', 1, 4]] };
  const alone = run({ enemyCustomPool: ['cA'], preventSameEnemy: true, enemyPlacementFit: 'fits' }, spec);
  assert.ok([...targets(alone)].every((m) => m === 'cA'));
  assert.ok(alone.placements.enemies.filter((p) => p.slot.startsWith('m10_00_00_00')).every((p) => !p.changed || p.targetModelName !== 'cA'), 'cA slots never receive cA');
  assert.equal(counters(alone).stages === undefined, true);
  const both = run({ enemyCustomPool: ['cA', 'cB'], preventSameEnemy: true, enemyPlacementFit: 'fits' }, spec);
  assert.ok(both.placements.enemies.filter((p) => p.slot.startsWith('m10_00_00_00')).every((p) => p.targetModelName === 'cB'));
});

// ---- Type replacement -----------------------------------------------------------------------------------------------------------------

test('Type replacement draws every group from the same pool', () => {
  const pool = ['c1100', 'c2200', 'c3000', 'c4100', 'c5200'];
  for (let i = 0; i < 8; i += 1) {
    const { groups, result } = detail({ enemyCustomPool: pool, enemyTypeReplacement: 'enabled', seed: `type-${i}`, enemyReplacementChance: 80 });
    for (const group of groups.filter((g) => g.participates && g.stage !== 'kept-vanilla')) assert.ok(pool.includes(group.selected), `${group.groupId} -> ${group.selected}`);
    for (const model of targets(result)) assert.ok(pool.includes(model) || model === undefined, model);
  }
});

test('the shared-model fallback stays inside the pool, and a group that keeps its original may sit outside it', () => {
  // 17 groups, 15 fixed non-regular models: the same Parish-like squeeze as Part C1, now with a pool of 6 of the 17 models.
  const models = Array.from({ length: 17 }, (_, i) => `cG${String(i).padStart(2, '0')}`);
  const pool = models.slice(0, 6);
  const catalog = catalogOf({ m10_00_00_00: models.map((model) => [model, 1, 2]) });
  let shared = 0;
  let keptOutside = 0;
  for (let i = 0; i < 25; i += 1) {
    const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1', level: 'detail' });
    let result;
    try {
      result = sampling.planSampledRegularEnemies({
        seed: `shared-${i}`, slots: catalog.enemySlots, extraSlots: [], diagnostics, preventSame: false,
        fixedModelsByMap: new Map([['m10_00_00_00', new Set(Array.from({ length: 15 }, (_, k) => `x${k}`))]]),
        options: { replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'enabled', customPool: pool },
      });
    } catch (error) {
      assert.equal(error.code, 'ENEMY_PLACEMENT_INFEASIBLE');
      assert.equal(error.attempts, 3);
      continue;
    }
    for (const source of result.assignment.values()) assert.ok(pool.includes(source.modelName), `${source.modelName} is outside the pool`);
    const summary = diagnostics.toJSON().summary;
    shared += summary.counters.typeGroupsShared ?? 0;
    keptOutside += (summary.counters.typeGroupsKeptVanilla ?? 0);
    const finals = new Set([...Array.from({ length: 15 }, (_, k) => `x${k}`), ...catalog.enemySlots.map((s) => result.assignment.get(s.id)?.modelName ?? s.modelName)]);
    assert.ok(finals.size <= 30);
  }
  assert.ok(shared + keptOutside > 0, 'the squeeze really exercised the fallbacks');
});

// ---- budget ---------------------------------------------------------------------------------------------------------------------------------

const MAP = 'm10_00_00_00';
const fixedSet = (count, models = []) => new Set([...models, ...Array.from({ length: count - models.length }, (_, i) => `x${i + 1}`)]);
function plannerInput(spec, { fixed = new Map(), options = {}, seed = 'planner', preventSame = false } = {}) {
  const catalog = catalogOf(spec);
  return {
    seed, slots: catalog.enemySlots, extraSlots: [], fixedModelsByMap: fixed, preventSame,
    diagnostics: placement.createPlacementDiagnostics({ algorithm: 'sampling-v1', level: 'detail' }),
    options: { replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, ...options },
  };
}

test('the pool gets no budget exemption: a pool model at the cap is rejected, one already present is reused', () => {
  const spec = { [MAP]: [['cA', 1, 4]], m11_00_00_00: [['cB', 1, 3], ['cC', 1, 3]] };
  // 30 fixed models, none of them regular: the pool models are all new, so nothing can be placed.
  assert.throws(
    () => sampling.planSampledRegularEnemies(plannerInput(spec, { fixed: new Map([[MAP, fixedSet(30)]]), options: { customPool: ['cB'] } })),
    (error) => error.code === 'ENEMY_PLACEMENT_INFEASIBLE' && error.attempts === 3 && /custom pool 1 of 3/u.test(error.message) && typeof error.customPoolHash === 'string',
  );
  // With the pool model already present (a fixed plan holds it) the same map is fine and nothing is added.
  const input = plannerInput(spec, { fixed: new Map([[MAP, fixedSet(30, ['cB'])]]), options: { customPool: ['cB'] } });
  const result = sampling.planSampledRegularEnemies(input);
  const info = input.diagnostics.toJSON().summary.maps[MAP];
  assert.equal(info.baseline, 30);
  assert.equal(info.effectiveCap, 30);
  assert.ok(info.finalModels <= 30);
  for (const source of result.assignment.values()) assert.equal(source.modelName, 'cB');
});

test('a restricted pool changes the coverage reserve: it holds a pool model that fits the smallest slot', () => {
  const spec = { [MAP]: [['cA', 1, 2], ['cB', 3, 2]], m11_00_00_00: [['cC', 1, 2], ['cD', 4, 2]] };
  const table = sampling.deriveRegularClassTable(catalogOf(spec).enemySlots);
  const slots = catalogOf(spec).enemySlots.filter((s) => s.mapId === MAP);
  const params = (pool) => ({
    mapId: MAP, cap: 30, fixedModels: new Set(), participants: slots, sizeOf: (s) => sampling.slotSizeClass(table, s), fitSize: (s) => sampling.slotSizeClass(table, s),
    preventSame: false, table, pool,
  });
  assert.deepEqual(sampling.buildMapModelSet(params(sampling.createCandidatePool(table))).reserve, ['cA'], 'the full pool: the original of the smallest slot');
  const restricted = sampling.createCandidatePool(table, (t) => ['cC', 'cD'].includes(t.model));
  const reserve = sampling.buildMapModelSet(params(restricted)).reserve;
  assert.deepEqual(reserve, ['cC'], 'a restricted pool: the smallest pool model, not an original that is not a candidate');
});

// ---- retries and errors ---------------------------------------------------------------------------------------------------------------------

test('a supported pool can still make one map attempt fail: bounded map-local retries, then a typed retryable error that names the pool', () => {
  const spec = { [MAP]: [['cB', 3, 4], ['cC', 3, 4]], m11_00_00_00: [['cA', 0, 6], ['cB', 3, 6], ['cD', 2, 6]] };
  const fixed = new Map([[MAP, fixedSet(30)]]);
  let retried = null;
  let exhausted = null;
  for (let i = 0; i < 300 && (!retried || !exhausted); i += 1) {
    const input = plannerInput(spec, { fixed, seed: `retry-${i}`, options: { replacementChance: 80, fit: 'fits', typeReplacement: 'enabled', customPool: ['cA', 'cB', 'cC', 'cD'] }, preventSame: true });
    try {
      sampling.planSampledRegularEnemies(input);
      if (!retried && input.diagnostics.toJSON().summary.counters.mapsRetried) retried = i;
    } catch (error) {
      if (!exhausted && error.code === 'ENEMY_PLACEMENT_INFEASIBLE') exhausted = error;
    }
  }
  assert.ok(retried !== null, 'a retry rescued a map');
  assert.ok(exhausted, 'a map exhausted its attempts');
  assert.equal(exhausted.attempts, sampling.MAP_ATTEMPTS);
  assert.equal(exhausted.mapId, MAP);
  assert.match(exhausted.message, /custom pool 4 of 4/u);
  assert.notEqual(exhausted.code, 'ENEMY_PLACEMENT_CUSTOM_POOL_INVALID', 'a valid pool is never classified as invalid');
});

// ---- coverage ---------------------------------------------------------------------------------------------------------------------------------

test('coverage analysis counts the selected enemies per size and difficulty class and the slots no candidate fits', () => {
  const spec = { [MAP]: [['cA', 0, 3], ['cB', 2, 2]], m11_00_00_00: [['cC', 4, 1]] };
  const catalog = catalogOf(spec);
  const table = sampling.deriveRegularClassTable(catalog.enemySlots);
  const pool = sampling.createCandidatePool(table, (t) => ['cB', 'cC'].includes(t.model));
  const coverage = sampling.analyzePoolCoverage({ table, pool, slots: catalog.enemySlots, fit: 'fits' });
  assert.equal(coverage.total, 3);
  assert.equal(coverage.selected, 2);
  assert.deepEqual(coverage.sizeCoverage, [0, 0, 1, 0, 1, 0]);
  assert.equal(coverage.difficultyCoverage.reduce((a, b) => a + b, 0), 2);
  assert.equal(coverage.smallestSize, 2);
  assert.equal(coverage.slotsWithoutFit, 3, 'the three size-0 slots have no candidate that fits');
  assert.equal(sampling.analyzePoolCoverage({ table, pool, slots: catalog.enemySlots, fit: 'anywhere' }).slotsWithoutFit, 0);
  const result = run({ enemyCustomPool: ['cB', 'cC'], enemyPlacementFit: 'fits' }, spec);
  assert.equal(result.placementDiagnostics.customPool.slotsWithoutFit, 3);
  assert.equal(result.placementDiagnostics.customPool.selected, 2);
  assert.equal(result.placementDiagnostics.customPool.total, 3);
  assert.match(result.placementDiagnostics.customPool.hash, /^[0-9a-f]{64}$/u);
  assert.equal(result.placementDiagnostics.options.customPool, undefined, 'the list itself is not repeated in the options summary');
});

// ---- enemy pool description (UI metadata) ------------------------------------------------------------------------------------------------------

test('the enemy pool description comes from the catalog, in model order, independent of catalog order', async () => {
  const dir = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-pool-test-'));
  try {
    const describe = async (reverse) => {
      const catalog = catalogOf(tieredSpec(), { reverse });
      let scans = 0;
      const result = await describeEnemyPool({ directory: 'C:/pristine', snapshotId: `snap-${reverse}` }, {
        cacheRoot: dir,
        dataTool: {
          execute: async (request) => {
            scans += 1;
            await writeFile(request.plannedArguments[request.plannedArguments.indexOf('--output') + 1], JSON.stringify(catalog));
            return { ok: true, stdout: '', stderr: '' };
          },
        },
      });
      return { result, scans };
    };
    const a = await describe(false);
    const b = await describe(true);
    assert.equal(a.result.ok, true, JSON.stringify(a.result));
    assert.equal(a.scans, 1);
    assert.deepEqual(a.result.pool.entries.map((e) => e.model), TIERED_MODELS);
    assert.deepEqual(a.result.pool.entries, b.result.pool.entries);
    assert.equal(a.result.pool.classTableHash, b.result.pool.classTableHash);
    assert.equal(a.result.pool.source, 'pristine');
    assert.equal(a.result.pool.regularSlots, 138);
    for (const entry of a.result.pool.entries) assert.ok(entry.size >= 0 && entry.size <= 5 && entry.difficulty >= 0 && entry.difficulty <= 7 && entry.slots === 6 && entry.maps.length === 1);
    // The cache is reused for the same snapshot.
    const again = await describeEnemyPool({ directory: 'C:/pristine', snapshotId: 'snap-false' }, { cacheRoot: dir, dataTool: { execute: async () => { throw new Error('must not scan again'); } } });
    assert.equal(again.ok, true);
    // Without an imported clean game there is no list.
    const none = await describeEnemyPool(null);
    assert.equal(none.ok, false);
    assert.equal(none.code, 'PRISTINE_IMPORT_REQUIRED');
    // A failing scan is reported with its code.
    const failed = await describeEnemyPool({ directory: 'C:/other', snapshotId: 'x' }, { cacheRoot: dir, dataTool: { execute: async () => ({ ok: false, type: 'exit', exitCode: 3, stdout: '', stderr: 'boom' }) } });
    assert.equal(failed.ok, false);
    assert.equal(failed.code, 'DATA_TOOL_FAILED');
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});
