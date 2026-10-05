import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';

// sampling-v1 Type replacement (Part C1): legacy semantics on regular enemies, on synthetic catalogs.

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const load = (file) => import(pathToFileURL(resolveCombinedRandomizerCoreModulePath(`core/${file}`)).href);
const sampling = await load('sampling.js');
const placement = await load('placement.js');

// ---- synthetic catalog ------------------------------------------------------------------------------------------------------

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

/** spec: { map: [[model, size, count, hp], ...] }. */
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

const config = (overrides = {}) => ({
  ...defaultConfig, seed: 'type-seed', randomizeEnemies: true, randomizeBosses: false, randomizeItems: false,
  generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1', enemyPlacementFit: 'anywhere', enemyTypeReplacement: 'enabled', ...overrides,
});
const run = (overrides, spec = tieredSpec(), catalogOptions = {}) => generate(config(overrides), { gameCatalog: catalogOf(spec, catalogOptions) });
const detail = (overrides, spec) => {
  const result = run({ placementDiagnosticsLevel: 'detail', ...overrides }, spec);
  return { result, groups: result.placementDiagnostics.groups, records: result.placementDiagnostics.slots };
};
const modelOf = (slotId) => slotId.split(':')[1].split('_')[0];
const counters = (result) => result.placementDiagnostics.summary.counters;

// ---- grouping ---------------------------------------------------------------------------------------------------------------------

test('a group is the original model within one map; the order of the catalog does not matter', () => {
  const a = run({});
  const b = run({}, tieredSpec(), { reverse: true });
  const outcome = (result) => result.placements.enemies.map((p) => [p.slot, p.targetModelName]).sort((x, y) => (x[0] < y[0] ? -1 : 1));
  assert.deepEqual(outcome(a), outcome(b), 'the same slot gets the same model whatever the catalog order');
  assert.deepEqual(a.placementDiagnostics.summary.maps, b.placementDiagnostics.summary.maps);
  const { groups } = detail({});
  assert.equal(groups.length, 23, '8 maps x 3 models, minus the player model c0000 which is never a regular enemy');
  assert.ok(groups.every((g) => g.groupId === `map:${g.groupId.split(':')[1]}:model:${g.model}`));
  assert.ok(groups.every((g) => g.members === 6));
  assert.equal(new Set(groups.map((g) => g.groupId)).size, groups.length);
  assert.equal(counters(a).typeGroups, 23);
});

test('the same model in two maps forms two groups that decide separately', () => {
  const spec = { m10_00_00_00: [['c1000', 1, 4], ['c1010', 2, 4], ['c1020', 3, 4]], m11_00_00_00: [['c1000', 1, 4], ['c1010', 2, 4], ['c1030', 3, 4]] };
  let different = false;
  for (let i = 0; i < 30 && !different; i += 1) {
    const { groups } = detail({ seed: `maps-${i}` }, spec);
    const pick = (map) => groups.find((g) => g.groupId === `map:${map}:model:c1000`).selected;
    if (pick('m10_00_00_00') !== pick('m11_00_00_00')) different = true;
  }
  assert.ok(different, 'a global type key would force the same replacement in both maps');
});

test('all slots of a group receive the same replacement model and a group left alone stays vanilla as a whole', () => {
  for (let i = 0; i < 6; i += 1) {
    const { result, groups, records } = detail({ seed: `members-${i}`, enemyReplacementChance: 50 });
    for (const group of groups) {
      const members = records.filter((r) => r.groupId === group.groupId);
      assert.equal(members.length, group.members);
      const selected = new Set(members.map((r) => r.selected));
      assert.equal(selected.size, 1, group.groupId);
      if (!group.participates) assert.ok(members.every((r) => r.participates === false && r.selected === undefined));
    }
    const replacedModels = result.placements.enemies.filter((p) => p.changed).map((p) => p.targetModelName);
    assert.ok(replacedModels.length > 0);
  }
});

// ---- replacement chance ---------------------------------------------------------------------------------------------------------

test('the replacement chance is one roll per group, the first draw of the group stream: 0%, 100% and in between', () => {
  const none = run({ enemyReplacementChance: 0 });
  assert.deepEqual(none.placements.enemies, []);
  assert.equal(counters(none).typeGroupsKeptByChance, 23);
  assert.equal(counters(none).keptByChance, 138);

  const all = run({ enemyReplacementChance: 100 });
  assert.equal(counters(all).typeGroupsReplaced, 23);
  assert.equal(counters(all).typeGroupsKeptByChance ?? 0, 0);

  const { groups } = detail({ enemyReplacementChance: 40 });
  for (const group of groups) {
    const map = group.groupId.split(':')[1];
    const first = placement.createTypeStream('type-seed', map, group.model).int(100) + 1;
    assert.equal(group.participates, first <= 40, group.groupId);
  }
  const taken = groups.filter((g) => g.participates).length;
  assert.ok(taken > 0 && taken < 23, `${taken} of 23 groups participate`);
});

test('mode none leaves every group alone without drawing', () => {
  const result = run({ enemyNormalReplacement: 'none' });
  assert.deepEqual(result.placements.enemies, []);
  assert.equal(counters(result).keptByMode, 138);
});

test('type streams are disjoint from slot streams and attempt aware', () => {
  assert.equal(placement.typeStreamName('m10_01_00_00', 'c2500'), 'type:m10_01_00_00:c2500');
  assert.equal(placement.typeStreamName('m10_01_00_00', 'c2500', 2), 'attempt:2:type:m10_01_00_00:c2500');
  assert.notEqual(placement.slotStreamName('m10_01_00_00:c2500_0000'), placement.typeStreamName('m10_01_00_00', 'c2500'));
  for (const bad of [['x', 'c2500'], ['m10_01_00_00', 'c25 00']]) assert.throws(() => placement.typeStreamName(...bad), TypeError);
  assert.throws(() => placement.typeStreamName('m10_01_00_00', 'c2500', -1), RangeError);
});

// ---- fit across a group -------------------------------------------------------------------------------------------------------------

test('Placement fit: the replacement fits every member of the group', () => {
  const sizes = new Map(sampling.deriveRegularClassTable(catalogOf(tieredSpec()).enemySlots).types.map((t) => [t.model, t.size]));
  for (let i = 0; i < 8; i += 1) {
    const { groups, result } = detail({ seed: `fit-${i}`, enemyPlacementFit: 'fits' });
    for (const group of groups.filter((g) => g.stage === 'sampled' || g.stage === 'in-set-fit')) {
      assert.ok(sizes.get(group.selected) <= sizes.get(group.model), `${group.groupId} -> ${group.selected}`);
    }
    assert.ok(counters(result).typeFitRejections > 0);
  }
});

function plannerInput(spec, { extra = [], fixed = new Map(), options = {}, seed = 'planner', preventSame = false } = {}) {
  const catalog = catalogOf(spec);
  const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1', level: 'detail' });
  return {
    seed, slots: catalog.enemySlots, extraSlots: extra, fixedModelsByMap: fixed, diagnostics, preventSame,
    options: { replacementChance: 100, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'enabled', ...options },
  };
}

test('fit across members of different size: the smallest member decides; an empty intersection keeps the group or fails typed', () => {
  // The clones share a model that is not a regular type, so each clone is sized by its own capsule (1 and 3): the group fits size <= 1.
  const spec = { m11_00_00_00: [['cA', 0, 2], ['cB', 1, 2], ['cC', 2, 2], ['cD', 3, 2], ['cE', 4, 2]] };
  const clones = [slot('m10_00_00_00', 'cZ_0000', 'cZ', 900000, 7001, 1), slot('m10_00_00_00', 'cZ_0001', 'cZ', 900000, 7002, 3)];
  for (let i = 0; i < 20; i += 1) {
    const input = plannerInput(spec, { extra: clones, seed: `mixed-${i}` });
    const { assignment, table } = sampling.planSampledRegularEnemies(input);
    const first = assignment.get('m10_00_00_00:cZ_0000');
    const second = assignment.get('m10_00_00_00:cZ_0001');
    assert.equal(first.modelName, second.modelName, 'one replacement for the whole group');
    assert.ok(table.byModel.get(first.modelName).size <= 1, `${first.modelName} fits the smaller member`);
  }
  // No type fits a size-0 member: with room the group stays vanilla; with a full map there is nowhere to go.
  const tiny = [slot('m10_00_00_00', 'cZ_0000', 'cZ', 900000, 7001, 0)];
  const spec2 = { m10_00_00_00: [['cB', 1, 2], ['cC', 2, 2]] };
  const kept = plannerInput(spec2, { extra: tiny });
  const keptResult = sampling.planSampledRegularEnemies(kept);
  assert.equal(keptResult.assignment.has('m10_00_00_00:cZ_0000'), false);
  assert.equal(kept.diagnostics.toJSON().summary.counters.typeGroupsKeptVanilla, 1);
  const fullMap = new Map([['m10_00_00_00', new Set(Array.from({ length: 30 }, (_, i) => `x${i}`))]]);
  assert.throws(() => sampling.planSampledRegularEnemies(plannerInput(spec2, { extra: tiny, fixed: fullMap })), (error) => error.code === 'ENEMY_PLACEMENT_INFEASIBLE' && error.attempts === 3);
});

// ---- difficulty ----------------------------------------------------------------------------------------------------------------------

test('difficulty: every member of a real group shares the destination class; the group draws one class', () => {
  const table = sampling.deriveRegularClassTable(catalogOf(tieredSpec()).enemySlots);
  const members = catalogOf(tieredSpec()).enemySlots.filter((s) => s.modelName === 'c3100');
  assert.equal(new Set(members.map((s) => sampling.slotDifficultyClass(table, s))).size, 1);
  for (const strictness of ['strict', 'very-loose']) {
    const { groups } = detail({ enemyDifficultyMode: 'curve', enemyDifficultyStrictness: strictness, seed: `curve-${strictness}`, enemyPlacementFit: 'fits' });
    let checked = 0;
    for (const group of groups.filter((g) => g.stage === 'sampled' && g.targetClass !== undefined)) {
      const eligible = sampling.eligibleDifficultyClasses({ mode: 'curve', strictness, destination: group.destinationClass, mapId: group.groupId.split(':')[1] });
      assert.ok(eligible.includes(group.targetClass));
      checked += 1;
    }
    assert.ok(checked > 10);
  }
});

test('easy Asylum: Asylum groups only receive the easiest classes', () => {
  const table = sampling.deriveRegularClassTable(catalogOf(tieredSpec()).enemySlots);
  for (const mode of ['random-easy-asylum', 'curve-easy-asylum']) {
    for (let i = 0; i < 6; i += 1) {
      const { groups } = detail({ enemyDifficultyMode: mode, seed: `asylum-${i}` });
      for (const group of groups.filter((g) => g.groupId.startsWith('map:m18_01_00_00:') && g.selected && g.stage === 'sampled')) {
        assert.ok(table.byModel.get(group.selected).difficulty <= sampling.EASY_ASYLUM_MAX_CLASS, `${group.groupId} -> ${group.selected}`);
      }
    }
  }
});

// ---- Prevent same and injectivity -------------------------------------------------------------------------------------------------------

test('Prevent same applies to the group: off allows the same model, on never; an identity-only candidate keeps the group vanilla', () => {
  let identity = 0;
  for (let i = 0; i < 12; i += 1) {
    const off = detail({ seed: `same-${i}`, enemyPlacementFit: 'anywhere' }).groups.filter((g) => g.selected === g.model && g.stage === 'sampled');
    identity += off.length;
    const on = detail({ seed: `same-${i}`, enemyPlacementFit: 'anywhere', preventSameEnemy: true });
    assert.ok(on.groups.every((g) => !g.participates || g.stage !== 'sampled' || g.selected !== g.model));
    assert.ok(counters(on.result).typePreventSameRejections > 0);
  }
  assert.ok(identity > 0, 'identity replacement happens when the option is off');
  const lone = run({ preventSameEnemy: true }, { m10_00_00_00: [['cA', 2, 5]] });
  assert.deepEqual(lone.placements.enemies, []);
  assert.equal(counters(lone).typeGroupsKeptVanilla, 1);
});

test('two groups of one map never receive the same replacement', () => {
  for (let i = 0; i < 12; i += 1) {
    const { groups } = detail({ seed: `inj-${i}`, enemyPlacementFit: 'anywhere' });
    const byMap = new Map();
    for (const group of groups.filter((g) => g.participates && g.selected !== g.model)) {
      const map = group.groupId.split(':')[1];
      const seen = byMap.get(map) ?? new Set();
      assert.equal(seen.has(group.selected), false, `${map}: ${group.selected} twice`);
      seen.add(group.selected);
      byMap.set(map, seen);
    }
  }
  assert.ok((counters(run({ enemyPlacementFit: 'anywhere' })).typeInjectivityRejections ?? 0) >= 0);
});

// ---- model budget --------------------------------------------------------------------------------------------------------------------------

const MAP = 'm10_00_00_00';
const OTHER = 'm11_00_00_00';
const fixedSet = (count, models = []) => new Set([...models, ...Array.from({ length: count - models.length }, (_, i) => `x${i + 1}`)]);
const finalModels = (input, result, mapId) => {
  const models = new Set(input.fixedModelsByMap.get(mapId) ?? []);
  for (const s of [...input.slots, ...input.extraSlots].filter((candidate) => candidate.mapId === mapId)) models.add(result.assignment.get(s.id)?.modelName ?? s.modelName);
  return models;
};
const typeRun = (spec, fixed, overrides = {}) => {
  const input = plannerInput(spec, { fixed: new Map([[MAP, fixed]]), options: { fit: 'anywhere' }, ...overrides });
  const result = sampling.planSampledRegularEnemies(input);
  return { input, result, finals: finalModels(input, result, MAP), info: input.diagnostics.toJSON().summary.maps[MAP] };
};

test('the ledger counts references: a model leaves the set with its last owner', () => {
  const ledger = new placement.ModelLedger({ mapId: MAP, cap: 3 });
  ledger.hold('a'); ledger.hold('b'); ledger.hold('a');
  assert.equal(ledger.size, 2);
  ledger.release('a');
  assert.equal(ledger.has('a'), true, 'a second owner remains');
  ledger.release('a');
  assert.equal(ledger.has('a'), false);
  assert.deepEqual(ledger.models(), ['b']);
  assert.deepEqual(ledger.evaluate('b'), { admitted: true, alreadyPresent: true });
  ledger.hold('c'); ledger.hold('d');
  assert.equal(ledger.evaluate('e').admitted, false, 'a new model at the cap is rejected');
  assert.equal(ledger.evaluate('c').admitted, true, 'a present model is still fine');
  assert.throws(() => ledger.release('zzz'), RangeError);
});

test('a replaced group frees its original model and takes one new model; the effective cap is never exceeded', () => {
  const spec = { [MAP]: [['cA', 1, 4], ['cB', 1, 4], ['cC', 1, 4]], [OTHER]: [['cD', 1, 3], ['cE', 1, 3], ['cF', 1, 3], ['cG', 1, 3]] };
  for (let i = 0; i < 30; i += 1) {
    const { finals, info } = typeRun(spec, fixedSet(27), { seed: `swap-${i}` });
    assert.ok(finals.size <= info.effectiveCap, `seed ${i}: ${finals.size} > ${info.effectiveCap}`);
    assert.equal(info.baseline, 27);
    assert.equal(info.effectiveCap, 30);
  }
  // 27 fixed + 3 groups at the cap of 30: every group can swap, none can add.
  const { finals } = typeRun(spec, fixedSet(27), { seed: 'swap-0' });
  assert.equal(['cA', 'cB', 'cC'].filter((m) => finals.has(m)).length + [...finals].filter((m) => !m.startsWith('x') && !['cA', 'cB', 'cC'].includes(m)).length <= 3, true);
});

test('the original model still counts while something else owns it (a fixed plan keeps it)', () => {
  const spec = { [MAP]: [['cA', 1, 4]], [OTHER]: [['cB', 1, 3], ['cC', 1, 3], ['cD', 1, 3]] };
  for (let i = 0; i < 10; i += 1) {
    const { finals, result } = typeRun(spec, fixedSet(29, ['cA']), { seed: `pinned-${i}` });
    assert.equal(finals.has('cA'), true, 'cA is pinned by a fixed plan whatever happens to the group');
    assert.ok(finals.size <= 30);
    assert.ok(result.assignment.size >= 0);
  }
  // Without the pin the original model disappears from the map when every slot of its group is replaced.
  let gone = false;
  for (let i = 0; i < 20 && !gone; i += 1) {
    const { finals, result } = typeRun(spec, fixedSet(27), { seed: `free-${i}` });
    if (result.assignment.get(`${MAP}:cA_0000`)?.modelName !== 'cA') gone = !finals.has('cA');
  }
  assert.ok(gone, 'the original model leaves the final set with its last slot');
});

test('a replacement that is already present costs no capacity, and a new one at the cap is rejected', () => {
  // 30 fixed models including cB (a regular type): the group of cA can only join the models that are there.
  const spec = { [MAP]: [['cA', 1, 4]], [OTHER]: [['cB', 1, 3], ['cC', 1, 3]] };
  for (let i = 0; i < 10; i += 1) {
    const { finals, result, info } = typeRun(spec, fixedSet(30, ['cB']), { seed: `cap-${i}` });
    assert.equal(info.baseline, 30);
    assert.equal(info.effectiveCap, 30);
    assert.ok(finals.size <= 30);
    const picked = result.assignment.get(`${MAP}:cA_0000`)?.modelName;
    assert.ok(picked === 'cB' || picked === undefined, `only the present model is possible, got ${picked}`);
  }
});

test('an overflow that cannot be avoided becomes the cap and is not worsened', () => {
  const spec = { [MAP]: [['cA', 1, 4], ['cB', 1, 4]], [OTHER]: [['cC', 1, 3], ['cD', 1, 3]] };
  for (let i = 0; i < 10; i += 1) {
    const { finals, info } = typeRun(spec, fixedSet(31, ['cC', 'cD']), { seed: `over-${i}` });
    assert.equal(info.baseline, 31);
    assert.equal(info.effectiveCap, 31);
    assert.ok(finals.size <= 31);
  }
});

test('injectivity is a preference: with a full map a group may take a model another group already has, and only then', () => {
  // 17 groups, 15 fixed models that are not regular types: 32 models before the run, cap 30. Distinct replacements
  // cannot reduce the map, so sharing is the only way to meet the limit.
  const models = Array.from({ length: 17 }, (_, i) => `cG${String(i).padStart(2, '0')}`);
  const spec = { [MAP]: models.map((model) => [model, 1, 2]) };
  let shared = 0;
  for (let i = 0; i < 20; i += 1) {
    const { finals, info, input } = typeRun(spec, fixedSet(15), { seed: `shared-${i}` });
    assert.ok(finals.size <= info.effectiveCap, `seed ${i}: ${finals.size} > ${info.effectiveCap}`);
    assert.equal(info.baseline, 15);
    shared += input.diagnostics.toJSON().summary.counters.typeGroupsShared ?? 0;
  }
  assert.ok(shared > 0, 'sharing happens when the budget demands it');
  // With room for every group the rule holds without exception.
  for (let i = 0; i < 10; i += 1) {
    const { input } = typeRun(spec, fixedSet(5), { seed: `roomy-${i}` });
    assert.equal(input.diagnostics.toJSON().summary.counters.typeGroupsShared ?? 0, 0);
  }
});

test('the shared rung never relaxes fit, Prevent same or the budget', () => {
  const spec = { [MAP]: [['cA', 3, 2], ['cB', 3, 2]], [OTHER]: [['cC', 0, 2]] };
  const table = sampling.deriveRegularClassTable(catalogOf(spec).enemySlots);
  const pool = sampling.createCandidatePool(table);
  const ledger = new placement.ModelLedger({ mapId: MAP, cap: 30 });
  ledger.hold('cA'); ledger.hold('cC'); ledger.hold('cB', { pending: true });
  const group = { modelName: 'cB' };
  const rng = placement.createTypeStream('s', MAP, 'cB');
  const pick = (overrides) => sampling.sharedFallback({ group, budget: ledger, pool, table, maxSize: 3, preventSame: false, destination: 3, rng, ...overrides });
  assert.equal(pick({ maxSize: 0 })?.model, 'cC', 'only a model that fits qualifies');
  const nearest = ['cA', 'cC'].sort((x, y) => Math.abs(table.byModel.get(x).difficulty - 3) - Math.abs(table.byModel.get(y).difficulty - 3))[0];
  if (Math.abs(table.byModel.get('cA').difficulty - 3) !== Math.abs(table.byModel.get('cC').difficulty - 3)) {
    assert.equal(pick({ maxSize: 3 })?.model, nearest, 'closest difficulty class among the models that stay');
  }
  assert.ok(['cA', 'cC'].includes(pick({ maxSize: 3 })?.model), 'a model that stays is chosen, the pending cB is not');
  assert.equal(pick({ preventSame: true, group: { modelName: 'cA' }, maxSize: 3 })?.model, 'cC', 'the own model is excluded under Prevent same');
  assert.equal(pick({ maxSize: -1 }), null, 'nothing fits, nothing is invented');
});

// ---- map-local retries -----------------------------------------------------------------------------------------------------------------------

const RETRY_SPEC = { [MAP]: [['cB', 3, 4], ['cC', 3, 4]], [OTHER]: [['cA', 0, 6], ['cB', 3, 6], ['cD', 2, 6]] };
const fixed30 = new Map([[MAP, fixedSet(30)]]);
const retryPlan = (seed, overrides = {}) => {
  const input = plannerInput(RETRY_SPEC, { fixed: fixed30, seed, options: { replacementChance: 80 }, ...overrides });
  return { input, plan: () => sampling.planSampledRegularEnemies(input) };
};

test('a map whose attempt fails is rebuilt alone, deterministically, within three attempts; other maps do not change', () => {
  let retried = null;
  let exhausted = null;
  for (let i = 0; i < 300 && (!retried || !exhausted); i += 1) {
    const { input, plan } = retryPlan(`retry-${i}`);
    try {
      const result = plan();
      const summary = input.diagnostics.toJSON().summary;
      if (!retried && summary.counters.mapsRetried) retried = { seed: `retry-${i}`, result, summary };
    } catch (error) {
      if (!exhausted && error.code === 'ENEMY_PLACEMENT_INFEASIBLE') exhausted = { error, summary: input.diagnostics.toJSON().summary };
    }
  }
  assert.ok(retried, 'a seed whose first attempt fails and a later one succeeds');
  assert.ok(exhausted, 'a seed whose attempts all fail');

  const again = retryPlan(retried.seed);
  const second = again.plan();
  assert.deepEqual([...second.assignment].map(([id, s]) => [id, s.id]), [...retried.result.assignment].map(([id, s]) => [id, s.id]));
  assert.ok(retried.summary.maps[MAP].attempts > 1 && retried.summary.maps[MAP].attempts <= sampling.MAP_ATTEMPTS);
  assert.ok(retried.summary.counters.typeMapAttemptsFailed >= 1);
  assert.equal(retried.summary.counters.mapsRetried, 1);

  const easy = retryPlan(retried.seed, { fixed: new Map() });
  const easyResult = easy.plan();
  const inOther = (result) => [...result.assignment].filter(([id]) => id.startsWith(OTHER)).map(([id, s]) => [id, s.id]);
  assert.deepEqual(inOther(retried.result), inOther(easyResult), 'retrying one map does not change another');

  assert.equal(exhausted.summary.counters.mapAttemptsFailed, sampling.MAP_ATTEMPTS);
  assert.equal(exhausted.error.attempts, sampling.MAP_ATTEMPTS);
  assert.equal(exhausted.error.mapId, MAP);
  assert.equal(exhausted.error.typeReplacement, 'enabled');
  assert.match(exhausted.error.message, /type replacement enabled/u);
  assert.match(exhausted.error.message, /effective cap 30/u);
});

// ---- compatibility ---------------------------------------------------------------------------------------------------------------------------

// Placement hashes produced by the Part B planner (commit 2bdff73) for these exact inputs; with the option off nothing may move.
const PART_B_PINS = JSON.parse(`{
 "pin-3/100/curve/strict/false": "e7286abf014e8998",
 "pin-3/100/curve/strict/true": "19e6f9827cce36f3",
 "pin-3/100/curve/very-loose/false": "329aa4f83ecdd37c",
 "pin-3/100/curve/very-loose/true": "3b12b2102528a96a",
 "pin-3/100/curve-easy-asylum/loose/false": "b4e5b29ad9627041",
 "pin-3/100/curve-easy-asylum/loose/true": "3854c3528eaff87c",
 "pin-3/100/random-easy-asylum/loose/false": "4c9e9b2d0c273d78",
 "pin-3/100/random-easy-asylum/loose/true": "3f141245f2417fe1",
 "pin-3/50/curve/strict/false": "92a3404b8db15bc2",
 "pin-3/50/curve/strict/true": "aae9be86a29e6159",
 "pin-3/50/curve/very-loose/false": "576e48243717a665",
 "pin-3/50/curve/very-loose/true": "89bfc8ec35e0704e",
 "pin-3/50/curve-easy-asylum/loose/false": "4fd7835955296a07",
 "pin-3/50/curve-easy-asylum/loose/true": "f04daac7fc80b238",
 "pin-3/50/random-easy-asylum/loose/false": "e8a2a4b3aadf89c0",
 "pin-3/50/random-easy-asylum/loose/true": "98ae2a8e677db551",
 "pin-4/100/curve/strict/false": "0d7ad0508d6d79fc",
 "pin-4/100/curve/strict/true": "7948fc0a19868311",
 "pin-4/100/curve/very-loose/false": "4a7c73850d3860b3",
 "pin-4/100/curve/very-loose/true": "3b67c6f5a662fe78",
 "pin-4/100/curve-easy-asylum/loose/false": "b0e2947f15f6d685",
 "pin-4/100/curve-easy-asylum/loose/true": "8de755fadfdf07c3",
 "pin-4/100/random-easy-asylum/loose/false": "8db44df25badf8d0",
 "pin-4/100/random-easy-asylum/loose/true": "041d9f30252f84e4",
 "pin-4/50/curve/strict/false": "d416d61feabada36",
 "pin-4/50/curve/strict/true": "30aa2ce3e40b326d",
 "pin-4/50/curve/very-loose/false": "3e432b44431881b6",
 "pin-4/50/curve/very-loose/true": "604576eb5cf04696",
 "pin-4/50/curve-easy-asylum/loose/false": "76e378191e9d6eaf",
 "pin-4/50/curve-easy-asylum/loose/true": "12450fbd1854d2c0",
 "pin-4/50/random-easy-asylum/loose/false": "1493f1d7720893b1",
 "pin-4/50/random-easy-asylum/loose/true": "54df1febd85bf379"
}`);


test('with Type replacement disabled the output is the Part B output', () => {
  const catalog = () => catalogOf(tieredSpec());
  for (const [key, expected] of Object.entries(PART_B_PINS)) {
    const [seed, chance, mode, strictness, prevent] = key.split('/');
    const result = generate(config({
      seed, enemyReplacementChance: Number(chance), enemyPlacementFit: 'fits', preventSameEnemy: prevent === 'true',
      enemyDifficultyMode: mode, enemyDifficultyStrictness: strictness, enemyTypeReplacement: 'disabled',
    }), { gameCatalog: catalog() });
    assert.equal(result.placementHash.slice(0, 16), expected, key);
    assert.equal(counters(result).typeGroups ?? 0, 0, 'no group is built when the option is off');
  }
});

test('enabled and enabled-except-roaming-bosses behave the same for regular enemies', () => {
  assert.equal(run({}).placementHash, run({ enemyTypeReplacement: 'enabled-except-roaming-bosses' }).placementHash);
});

test('the candidate pool is an injectable boundary: a filter narrows every group and the in-set fallback', () => {
  const catalog = catalogOf(tieredSpec());
  const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  const allowed = new Set(['c0000', 'c1100', 'c2200', 'c3000', 'c4100', 'c5200']);
  const { assignment } = sampling.planSampledRegularEnemies({
    seed: 'pool', slots: catalog.enemySlots, fixedModelsByMap: new Map(), diagnostics, preventSame: false,
    options: { replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'enabled' },
    candidateFilter: (type) => allowed.has(type.model),
  });
  assert.ok(assignment.size > 0);
  for (const source of assignment.values()) assert.ok(allowed.has(source.modelName), source.modelName);
});

test('the engine validates Type replacement and refuses a non-neutral value under permutation-v1', () => {
  for (const value of ['sometimes', 1, undefined]) {
    assert.throws(() => run({ enemyTypeReplacement: value }), (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID', String(value));
  }
  for (const value of ['enabled', 'enabled-except-roaming-bosses']) {
    assert.throws(
      () => run({ enemyPlacementAlgorithm: 'permutation-v1', enemyTypeReplacement: value }),
      (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID' && /only applies to sampling-v1/u.test(error.message),
    );
  }
  assert.ok(run({ enemyPlacementAlgorithm: 'permutation-v1', enemyTypeReplacement: 'disabled' }).placementHash);
});
