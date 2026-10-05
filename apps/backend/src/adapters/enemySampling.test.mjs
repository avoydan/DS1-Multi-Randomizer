import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';
import { formatPlacementDiagnostics, summarizePlacementDiagnostics } from '../../dist/core/placementDiagnostics.js';

// sampling-v1 for regular enemy slots (Part A), on synthetic catalogs (extracted game data is never committed).

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const load = (file) => import(pathToFileURL(resolveCombinedRandomizerCoreModulePath(`core/${file}`)).href);
const sampling = await load('sampling.js');
const placement = await load('placement.js');

// ---- synthetic catalog --------------------------------------------------------------------------------------------

/** Footprint (height with a thin capsule) that lands in each size class 0-5. */
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

/** spec: { map: [[model, size, count], ...] }; every model gets its own NpcParam id. */
function catalogOf(spec, { shuffle = false } = {}) {
  const slots = [];
  const npc = new Map();
  let index = 0;
  for (const [mapId, entries] of Object.entries(spec)) {
    for (const [model, size, count] of entries) {
      if (!npc.has(model)) npc.set(model, 100000 + npc.size * 10);
      for (let i = 0; i < count; i += 1) slots.push(slot(mapId, `${model}_${String(i).padStart(4, '0')}`, model, npc.get(model), index += 1, size));
    }
  }
  if (shuffle) slots.reverse();
  return {
    schemaVersion: 17,
    maps: Object.keys(spec).map((id) => ({ id, name: id })),
    enemySlots: slots, enemyArchetypes: [{ id: 'x' }], bossSlots: [],
    worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

const SPEC = {
  m10_00_00_00: [['c1000', 0, 12], ['c1010', 1, 10], ['c1020', 2, 8], ['c1030', 3, 6], ['c1040', 4, 4], ['c1050', 5, 2]],
  m11_00_00_00: [['c1000', 0, 10], ['c1020', 2, 10], ['c1050', 5, 4], ['c1060', 1, 6]],
  m12_00_00_00: [['c1010', 1, 8], ['c1030', 3, 8], ['c1040', 4, 4]],
};
const sizeOfModel = new Map([['c1000', 0], ['c1010', 1], ['c1020', 2], ['c1030', 3], ['c1040', 4], ['c1050', 5], ['c1060', 1]]);

const config = (overrides = {}) => ({
  ...defaultConfig, seed: 'sampling-seed', randomizeEnemies: true, randomizeBosses: false, randomizeItems: false,
  generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1', enemyPlacementFit: 'fits', ...overrides,
});
const run = (overrides, catalog = catalogOf(SPEC)) => generate(config(overrides), { gameCatalog: catalog });
const sampled = (result) => result.placements.enemies.filter((p) => /^sampled-|^passive-|^grounded-|^active-|^pinwheel/u.test(p.compatibility));
const changed = (result) => result.placements.enemies.filter((p) => p.changed);
const slotModel = (placementRecord) => placementRecord.slot.split(':')[1].slice(0, 5);

// ---- classification ---------------------------------------------------------------------------------------------------

test('size classes come from the capsule footprint and the class table hash tracks the derived classes', () => {
  assert.deepEqual([...sampling.SIZE_EDGES], [0.9, 1.7, 2.6, 4.2, 7]);
  assert.deepEqual([0.1, 0.89, 0.9, 1.69, 1.7, 2.59, 2.6, 4.19, 4.2, 6.99, 7, 30].map(sampling.sizeClassOfFootprint), [0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5]);
  assert.equal(sampling.slotFootprint({ hitHeight: 1, hitRadius: 0.8 }), 1.6, 'the diameter wins over the height');
  assert.equal(sampling.slotFootprint({ hitHeight: 2, hitRadius: 0.4 }), 2);
  assert.equal(sampling.slotFootprint({ hitHeight: -1, hitRadius: -1 }), null);
  assert.equal(sampling.slotFootprint({ hitHeight: 0, hitRadius: 0.4 }), null);

  const slots = catalogOf(SPEC).enemySlots;
  const table = sampling.deriveRegularClassTable(slots);
  assert.deepEqual(table.types.map((type) => [type.model, type.size]), [...sizeOfModel].sort((a, b) => (a[0] < b[0] ? -1 : 1)));
  assert.match(table.hash, /^[0-9a-f]{64}$/u);
  assert.equal(sampling.deriveRegularClassTable(slots).hash, table.hash, 'same catalog, same hash');

  // Input order alone changes nothing.
  const reversed = sampling.deriveRegularClassTable([...slots].reverse());
  assert.equal(reversed.hash, table.hash);
  assert.deepEqual(reversed.types.map((type) => type.variants.map((variant) => variant.source.id)), table.types.map((type) => type.variants.map((variant) => variant.source.id)));

  // A change inside the same class does not move the hash; crossing an edge does.
  const within = slots.map((s) => (s.modelName === 'c1020' ? { ...s, hitHeight: 2.2 } : s));
  assert.equal(sampling.deriveRegularClassTable(within).hash, table.hash);
  const crossing = slots.map((s) => (s.modelName === 'c1020' ? { ...s, hitHeight: 3.0 } : s));
  assert.notEqual(sampling.deriveRegularClassTable(crossing).hash, table.hash);
  const extraModel = [...slots, slot('m10_00_00_00', 'c1999_0000', 'c1999', 199990, 9999, 2)];
  assert.notEqual(sampling.deriveRegularClassTable(extraModel).hash, table.hash);

  // A model's footprint is its largest usable one, so a stray bad row neither breaks nor shrinks the class.
  const withBadRow = [...slots, { ...slot('m10_00_00_00', 'c1020_0099', 'c1020', 100020, 9998, 2), hitHeight: -1, hitRadius: -1 }];
  assert.equal(sampling.deriveRegularClassTable(withBadRow).hash, table.hash);
  assert.throws(
    () => sampling.deriveRegularClassTable([{ ...slot('m10_00_00_00', 'c1999_0000', 'c1999', 199990, 1, 2), hitHeight: 0, hitRadius: 0 }]),
    (error) => error.code === 'ENEMY_PLACEMENT_CLASS_UNDERIVABLE' && error.model === 'c1999',
  );
});

test('the class table hash is reported by generation, independent of catalog order', () => {
  const a = run({});
  const b = run({}, catalogOf(SPEC, { shuffle: true }));
  assert.match(a.placementDiagnostics.classTableHash, /^[0-9a-f]{64}$/u);
  assert.equal(a.placementDiagnostics.classTableHash, b.placementDiagnostics.classTableHash);
});

// ---- replacement chance and mode ------------------------------------------------------------------------------------------

test('replacement chance: 0 keeps every slot vanilla, 100 offers every slot, a middle value is a per-slot draw', () => {
  const total = catalogOf(SPEC).enemySlots.length;
  const none = run({ enemyReplacementChance: 0 });
  assert.equal(changed(none).length, 0);
  assert.equal(none.placementDiagnostics.summary.counters.keptByChance, total);
  assert.equal(none.placementDiagnostics.summary.participating, 0);

  const all = run({ enemyReplacementChance: 100 });
  assert.equal(all.placementDiagnostics.summary.counters.keptByChance ?? 0, 0);
  assert.equal(all.placementDiagnostics.summary.participating, total);
  assert.ok(changed(all).length > total / 2);

  const half = run({ enemyReplacementChance: 50 });
  const left = half.placementDiagnostics.summary.counters.keptByChance;
  assert.ok(left > total * 0.3 && left < total * 0.7, `left by chance ${left} of ${total}`);
  assert.equal(left + half.placementDiagnostics.summary.participating, total);
  assert.deepEqual(run({ enemyReplacementChance: 50 }).placements, half.placements, 'deterministic');
});

test('the replacement chance is the first draw of the slot stream: 1..100 <= chance', () => {
  const detail = run({ enemyReplacementChance: 40, placementDiagnosticsLevel: 'detail' });
  const records = detail.placementDiagnostics.slots;
  assert.equal(records.length, catalogOf(SPEC).enemySlots.length);
  for (const record of records) {
    const first = placement.createSlotStream('sampling-seed', record.slotId).int(100) + 1;
    assert.equal(record.participates, first <= 40, record.slotId);
  }
});

test('a slot that is not replaced is exactly vanilla: no placement, even for passive and active-think slots', () => {
  const catalog = catalogOf({ ...SPEC, m18_01_00_00: [['c2500', 1, 3]], m12_00_00_00: [...SPEC.m12_00_00_00, ['c2280', 1, 4]] });
  const special = ['m18_01_00_00:c2500_0000', 'm12_00_00_00:c2280_0000'];
  const total = catalog.enemySlots.length;
  for (const overrides of [{ enemyReplacementChance: 0 }, { enemyNormalReplacement: 'none' }]) {
    const result = generate(config(overrides), { gameCatalog: catalog });
    assert.deepEqual(result.placements.enemies, [], JSON.stringify(overrides));
    assert.equal(result.placementDiagnostics.summary.slots, total);
  }
  // With a partial chance every emitted placement belongs to a slot the sampler assigned, and ids stay stable.
  const half = generate(config({ enemyReplacementChance: 50, placementDiagnosticsLevel: 'detail' }), { gameCatalog: catalog });
  const assigned = new Set(half.placementDiagnostics.slots.filter((r) => r.stage === 'sampled').map((r) => r.slotId));
  assert.deepEqual(half.placements.enemies.map((p) => p.slot).sort(), [...assigned].sort());
  const full = generate(config({ enemyReplacementChance: 100 }), { gameCatalog: catalog });
  for (const id of special) {
    assert.ok(full.placements.enemies.some((p) => p.slot === id), `${id} is placed at 100%`);
    const fullRow = full.placements.enemies.find((p) => p.slot === id);
    const halfRow = half.placements.enemies.find((p) => p.slot === id);
    if (halfRow) assert.equal(halfRow.scaledNpcParamId, fullRow.scaledNpcParamId, 'index-based ids do not depend on the chance');
  }
});

test('normal replacement "none" leaves every regular slot vanilla and draws nothing', () => {
  const result = run({ enemyNormalReplacement: 'none' });
  assert.equal(changed(result).length, 0);
  assert.equal(result.placementDiagnostics.summary.counters.keptByMode, catalogOf(SPEC).enemySlots.length);
  assert.equal(result.placementDiagnostics.summary.participating, 0);
});

// ---- sampling behavior -----------------------------------------------------------------------------------------------------

test('sampling is deterministic, seed dependent and does not preserve model counts', () => {
  const a = run({});
  assert.equal(run({}).placementHash, a.placementHash);
  assert.notEqual(run({ seed: 'other-seed' }).placementHash, a.placementHash);
  const count = (result) => {
    const counts = new Map();
    for (const p of sampled(result)) counts.set(p.targetModelName, (counts.get(p.targetModelName) ?? 0) + 1);
    return counts;
  };
  const vanilla = new Map();
  for (const s of catalogOf(SPEC).enemySlots) vanilla.set(s.modelName, (vanilla.get(s.modelName) ?? 0) + 1);
  assert.notDeepEqual([...count(a)].sort(), [...vanilla].sort(), 'repeated types are allowed');
  assert.equal(sampled(a).length, catalogOf(SPEC).enemySlots.length, 'one placement per regular slot');
});

test('placement fit "fits" never puts a larger type into a slot; "anywhere" may', () => {
  const sizeOf = (placementRecord) => sizeOfModel.get(placementRecord.targetModelName);
  const slotSize = (placementRecord) => sizeOfModel.get(slotModel(placementRecord));
  let oversized = 0;
  for (let i = 0; i < 10; i += 1) {
    const fits = run({ seed: `fit-${i}`, enemyPlacementFit: 'fits' });
    for (const p of sampled(fits)) assert.ok(sizeOf(p) <= slotSize(p), `${p.slot} -> ${p.targetModelName}`);
    assert.ok(fits.placementDiagnostics.summary.counters.candidatesOverSize > 0, 'oversized candidates are excluded');
    for (const mode of ['anywhere', 'anywhere-except-npcs']) {
      const free = run({ seed: `fit-${i}`, enemyPlacementFit: mode });
      oversized += sampled(free).filter((p) => sizeOf(p) > slotSize(p)).length;
      assert.equal(free.placementDiagnostics.summary.counters.candidatesOverSize ?? 0, 0);
    }
  }
  assert.ok(oversized > 0, 'anywhere places larger types');
  // The two "anywhere" values are identical for regular slots (they differ only for NPC targets).
  assert.deepEqual(run({ enemyPlacementFit: 'anywhere' }).placements, run({ enemyPlacementFit: 'anywhere-except-npcs' }).placements);
});

test('Prevent same enemy: no sampled slot keeps its model; off allows it; only-same candidates keep the slot vanilla', () => {
  let sameWhenOff = 0;
  for (let i = 0; i < 10; i += 1) {
    const on = run({ seed: `same-${i}`, preventSameEnemy: true });
    for (const p of sampled(on)) {
      if (p.changed) assert.notEqual(p.targetModelName, slotModel(p), p.slot);
    }
    sameWhenOff += sampled(run({ seed: `same-${i}`, preventSameEnemy: false })).filter((p) => p.targetModelName === slotModel(p)).length;
  }
  assert.ok(sameWhenOff > 0, 'same-model replacements occur when the option is off');

  // One model only: nothing else exists, so the slots stay vanilla (the legacy outcome), without an error.
  const lone = catalogOf({ m10_00_00_00: [['c1000', 2, 9]] });
  const result = run({ preventSameEnemy: true }, lone);
  assert.equal(changed(result).length, 0);
  assert.equal(result.placementDiagnostics.summary.stages['kept-vanilla'], 9);
  assert.equal(result.placementDiagnostics.summary.rejections['prevent-same'], 9);
});

test('a Prevent same slot is never counted as replaced by its own model', () => {
  const result = run({ preventSameEnemy: true });
  const detail = run({ preventSameEnemy: true, placementDiagnosticsLevel: 'detail' }).placementDiagnostics.slots;
  for (const record of detail) {
    if (record.stage === 'sampled') assert.notEqual(record.selected, slotModel({ slot: record.slotId }));
  }
  assert.ok(result.placementDiagnostics.summary.rejections['prevent-same'] > 0);
});

// ---- model budget ------------------------------------------------------------------------------------------------------------

function plannerInput(spec, overrides = {}) {
  const catalog = catalogOf(spec);
  const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  return {
    seed: 'planner', options: { replacementChance: 100, normalReplacement: 'normals', fit: 'fits' }, preventSame: false,
    slots: catalog.enemySlots, fixedModelsByMap: new Map(), diagnostics, ...overrides,
  };
}
const finalModelsOf = (input, result, mapId) => {
  const models = new Set(input.fixedModelsByMap.get(mapId) ?? []);
  for (const s of input.slots.filter((candidate) => candidate.mapId === mapId)) models.add(result.assignment.get(s.id)?.modelName ?? s.modelName);
  return models;
};

test('the model budget is enforced on every map, including models that stay vanilla', () => {
  const spec = { m10_00_00_00: ['c1000', 'c1010', 'c1020', 'c1030', 'c1040', 'c1050', 'c1060', 'c1070', 'c1080'].map((model, i) => [model, i % 6, 5]) };
  for (const cap of [3, 4, 6, 9]) {
    for (let i = 0; i < 20; i += 1) {
      const input = plannerInput(spec, { seed: `budget-${i}`, cap });
      const result = sampling.planSampledRegularEnemies(input);
      assert.ok(finalModelsOf(input, result, 'm10_00_00_00').size <= cap, `cap ${cap} seed ${i}`);
    }
  }
});

test('a model already in the set does not consume a new slot, a new one is admitted up to the cap and rejected beyond it', () => {
  const budget = new placement.ModelBudget({ mapId: 'm10_00_00_00', cap: 3, fixedModels: ['a', 'b'] });
  assert.deepEqual(budget.evaluate('a'), { admitted: true, alreadyPresent: true });
  assert.equal(budget.admit('c').alreadyPresent, false, 'the 3rd model fits (cap 3)');
  assert.equal(budget.evaluate('d').admitted, false, 'the 4th is rejected');
  assert.equal(budget.evaluate('d').reason, 'model-budget');
  assert.equal(budget.evaluate('a').admitted, true, 'present models are still fine at capacity');

  // At the real cap: 29 present + one new = 30 is fine, a 31st is not.
  const models = Array.from({ length: 29 }, (_, i) => `c${2000 + i}`);
  const real = new placement.ModelBudget({ mapId: 'm10_00_00_00', fixedModels: models });
  assert.equal(real.evaluate('c3000').admitted, true);
  real.admit('c3000');
  assert.equal(real.size, 30);
  assert.equal(real.evaluate('c3001').admitted, false);
});

test('the coverage reserve keeps a fitting type in the set and can reject an otherwise valid candidate', () => {
  // Map: 1 small slot (A, size 0) and big slots of three other models. With cap 3 an early big slot could fill
  // the set with big types and leave nothing for the small slot; the reserve admits A first.
  const spec = { m10_00_00_00: [['cA', 0, 1], ['cB', 3, 3], ['cC', 4, 3], ['cD', 5, 3]] };
  const input = plannerInput(spec, { cap: 3 });
  const table = sampling.deriveRegularClassTable(input.slots);
  const model = sampling.buildMapModelSet({
    mapId: 'm10_00_00_00', cap: 3, fixedModels: new Set(), participants: input.slots,
    sizeOf: (s) => sampling.slotSizeClass(table, s), fitSize: (s) => sampling.slotSizeClass(table, s), preventSame: false, table,
  });
  assert.deepEqual(model.reserve, ['cA']);
  assert.deepEqual(model.budget.models(), ['cA']);
  assert.equal(model.budget.evaluate('cB').admitted, true);
  model.budget.admit('cB');
  model.budget.admit('cC');
  assert.equal(model.budget.evaluate('cD').admitted, false, 'cD would be individually valid for the big slots but the set is full');

  for (let i = 0; i < 40; i += 1) {
    const run40 = sampling.planSampledRegularEnemies(plannerInput(spec, { seed: `reserve-${i}`, cap: 3 }));
    const small = run40.assignment.get('m10_00_00_00:cA_0000');
    assert.ok(!small || small.modelName === 'cA', 'the small slot only ever receives a type that fits it');
  }
});

test('the reserve is skipped when the set already holds a fitting type and trimmed when it cannot fit', () => {
  const spec = { m10_00_00_00: [['cA', 0, 2], ['cB', 1, 2], ['cC', 2, 2]] };
  const input = plannerInput(spec);
  const table = sampling.deriveRegularClassTable(input.slots);
  const parameters = (fixedModels, cap, preventSame = false) => ({
    mapId: 'm10_00_00_00', cap, fixedModels: new Set(fixedModels), participants: input.slots,
    sizeOf: (s) => sampling.slotSizeClass(table, s), fitSize: (s) => sampling.slotSizeClass(table, s), preventSame, table,
  });
  // cA is fixed already: it is the fitting type, so no reserve is needed.
  assert.deepEqual(sampling.buildMapModelSet(parameters(['cA'], 30)).reserve, []);
  // Prevent same needs two distinct fitting types.
  assert.deepEqual(sampling.buildMapModelSet(parameters(['cA'], 30, true)).reserve, ['cB']);
  // A reserve that would exceed the cap is trimmed (and counted); the set never exceeds the cap.
  const tight = sampling.buildMapModelSet(parameters(['x1'], 2, true));
  assert.deepEqual(tight.reserve, ['cA']);
  assert.equal(tight.trimmed, 1);
  assert.equal(tight.budget.size, 2);
  // Fixed models alone above the cap cannot be repaired.
  assert.throws(
    () => sampling.buildMapModelSet(parameters(['x1', 'x2', 'x3', 'x4'], 3)),
    (error) => error.code === 'ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE' && error.mapId === 'm10_00_00_00',
  );
  assert.throws(
    () => sampling.planSampledRegularEnemies(plannerInput(spec, { fixedModelsByMap: new Map([['m10_00_00_00', new Set(['x1', 'x2', 'x3', 'x4'])]]), cap: 3 })),
    (error) => error.code === 'ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE',
  );
  // Non-participating slots count: their vanilla models plus the fixed ones above the cap is also impossible.
  assert.throws(
    () => sampling.planSampledRegularEnemies(plannerInput(spec, { options: { replacementChance: 0, normalReplacement: 'normals', fit: 'fits' }, cap: 2 })),
    (error) => error.code === 'ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE',
  );
});

test('admission is deterministic: the same input gives the same admission order', () => {
  const orderOf = () => {
    const input = plannerInput(SPEC, { seed: 'order', cap: 5 });
    const result = sampling.planSampledRegularEnemies(input);
    return JSON.stringify([...result.assignment].map(([id, source]) => [id, source.id]));
  };
  assert.equal(orderOf(), orderOf());
});

test('budget rejections are recorded with the reason model-budget and the set size per map is reported', () => {
  const input = plannerInput(SPEC, { seed: 'record', cap: 4 });
  sampling.planSampledRegularEnemies(input);
  const summary = input.diagnostics.toJSON().summary;
  assert.ok(summary.rejections['model-budget'] > 0);
  for (const info of Object.values(summary.maps)) assert.ok(info.finalModels <= 4);
});

// ---- fallback ladder --------------------------------------------------------------------------------------------------------

test('the fallback ladder is ordered and bounded: keep vanilla, in-set fit, relaxed fit, infeasible', () => {
  const slots = catalogOf({ m10_00_00_00: [['cA', 0, 1], ['cB', 2, 1], ['cC', 4, 1]] }).enemySlots;
  const table = sampling.deriveRegularClassTable(slots);
  const target = slots.find((s) => s.modelName === 'cB');
  const ladder = (members, overrides = {}) => sampling.fallbackLadder({
    slot: target, budget: new placement.ModelBudget({ mapId: 'm10_00_00_00', cap: members.length || 1, fixedModels: members }),
    table, maxSize: 2, fit: 'fits', preventSame: false, ...overrides,
  });
  // 1. the slot's own model is in the set -> keep it (also with Prevent same: an unreplaced slot is unchanged)
  assert.deepEqual(ladder(['cB']), { stage: 'kept-vanilla', chosen: null });
  assert.equal(ladder(['cB'], { preventSame: true }).stage, 'kept-vanilla');
  // 2. the set is full without it: the smallest in-set type that fits
  const inSet = ladder(['cC', 'cA']);
  assert.equal(inSet.stage, 'in-set-fit');
  assert.equal(inSet.chosen.model, 'cA');
  // 3. nothing in the set fits: relax the fit (recorded)
  const relaxed = ladder(['cC']);
  assert.equal(relaxed.stage, 'fit-relaxed');
  assert.equal(relaxed.chosen.model, 'cC');
  // 4. relaxation is not available with "anywhere" (everything fits already) and Prevent same can empty the set
  assert.equal(ladder([], { fit: 'anywhere' }).stage === 'kept-vanilla', true, 'an empty set has room for the slot model');
  const stuck = sampling.fallbackLadder({
    slot: target, budget: new placement.ModelBudget({ mapId: 'm10_00_00_00', cap: 1, fixedModels: ['cA'] }),
    table: { byModel: new Map([['cA', table.byModel.get('cA')]]) }, maxSize: 2, fit: 'fits', preventSame: true,
  });
  assert.equal(stuck.stage, 'in-set-fit');
  const impossible = sampling.fallbackLadder({
    slot: target, budget: new placement.ModelBudget({ mapId: 'm10_00_00_00', cap: 1, fixedModels: ['cA'] }),
    table: { byModel: new Map() }, maxSize: 2, fit: 'fits', preventSame: true,
  });
  assert.deepEqual(impossible, { stage: 'infeasible', chosen: null });
});

test('an infeasible slot raises a typed, retryable error with map and slot context', () => {
  // A catalog the sampler cannot place cannot be built through the reserve; the error type is what callers rely on.
  const error = new placement.EnemyPlacementInfeasibleError('m10_00_00_00', 'm10_00_00_00:c1000_0000', 'example');
  assert.equal(error.code, 'ENEMY_PLACEMENT_INFEASIBLE');
  assert.match(error.message, /m10_00_00_00:c1000_0000/u);
  assert.match(error.message, /m10_00_00_00/u);
});

// ---- RNG isolation --------------------------------------------------------------------------------------------------------------

test('another map, and the draws other slots consume, never change a slot\'s own result', () => {
  const small = plannerInput({ m10_00_00_00: SPEC.m10_00_00_00 }, { seed: 'iso' });
  const smallResult = sampling.planSampledRegularEnemies(small);
  // The extra map only uses models that exist in the first one, so the type pool (and class table) is identical.
  const large = plannerInput({ ...SPEC, m11_00_00_00: [['c1000', 0, 10], ['c1020', 2, 10], ['c1050', 5, 4]] }, { seed: 'iso' });
  const largeResult = sampling.planSampledRegularEnemies(large);
  for (const s of small.slots) {
    assert.equal(largeResult.assignment.get(s.id)?.id, smallResult.assignment.get(s.id)?.id, s.id);
  }
  // The participation draw of a slot depends on that slot's stream only.
  const first = (id) => placement.createSlotStream('iso', id).int(100);
  const before = first('m10_00_00_00:c1000_0003');
  for (let i = 0; i < 100; i += 1) placement.createSlotStream('iso', 'm10_00_00_00:c1000_0004').int(50);
  assert.equal(first('m10_00_00_00:c1000_0003'), before);
});

test('slot streams use enemy-placement-v1 and sampling consumes no permutation stream', () => {
  const before = placement.slotStreamDerivationCount();
  run({});
  const used = placement.slotStreamDerivationCount() - before;
  assert.equal(used, catalogOf(SPEC).enemySlots.length, 'exactly one stream per regular slot');
  assert.equal(placement.SAMPLING_RNG_VERSION, 'enemy-placement-v1');
});

// ---- drop ownership -----------------------------------------------------------------------------------------------------------------

test('every changed sampling-v1 placement carries its own scaled NpcParam row in every scaling mode', () => {
  for (const scaling of ['vanilla', 'area', 'progressive']) {
    const result = run({ enemyScaling: scaling, preventSameEnemy: true });
    const rows = changed(result);
    assert.ok(rows.length > 0);
    assert.ok(rows.every((p) => Number.isInteger(p.scaledNpcParamId)), scaling);
    assert.equal(new Set(rows.map((p) => p.scaledNpcParamId)).size, rows.length, 'one row per placement: repeated sources do not share a row');
    placement.assertSlotOwnedDrops(result.placements.enemies);
  }
});

test('repeated sampled sources take the drops of their destination slot, not their own', () => {
  const result = run({ enemyScaling: 'vanilla' });
  const bySource = new Map();
  for (const p of changed(result)) {
    // The tool copies the item-lot fields from `sourceNpcParamId` (the destination slot's own row) onto the scaled row.
    const destinationNpc = catalogOf(SPEC).enemySlots.find((s) => s.id === p.slot).npcParamId;
    assert.equal(p.sourceNpcParamId, destinationNpc);
    assert.notEqual(p.scaledNpcParamId, p.targetNpcParamId, 'the replacement\'s own row is never used directly');
    bySource.set(p.targetNpcParamId, (bySource.get(p.targetNpcParamId) ?? 0) + 1);
  }
  assert.ok([...bySource.values()].some((n) => n > 1), 'at least one source type was placed more than once');
  // permutation-v1 keeps its documented legacy behavior (unchanged): no scaled row under vanilla scaling.
  const legacy = generate(config({ enemyPlacementAlgorithm: 'permutation-v1', enemyPlacementFit: 'anywhere', enemyScaling: 'vanilla' }), { gameCatalog: catalogOf(SPEC) });
  assert.ok(legacy.placements.enemies.filter((p) => p.changed).every((p) => p.scaledNpcParamId === null));
});

// ---- out-of-scope categories and options --------------------------------------------------------------------------------------------------

test('only regular enemy slots are sampled; every other placement is exactly what permutation-v1 plans', () => {
  const catalog = catalogOf(SPEC);
  // Friendly NPCs and an excluded Mimic stay out of the sampler.
  catalog.enemySlots.push(slot('m10_00_00_00', 'c1070_0000', 'c1070', 100900, 9001, 1, { teamType: 2 }));
  catalog.enemySlots.push(slot('m10_00_00_00', 'c2780_0000', 'c2780', 278000, 9002, 1));
  const sample = generate(config({ replaceMimics: false }), { gameCatalog: catalog });
  const ids = new Set(sample.placements.enemies.map((p) => p.slot));
  assert.equal(ids.has('m10_00_00_00:c1070_0000'), false, 'friendly NPC is not a regular slot');
  assert.equal(ids.has('m10_00_00_00:c2780_0000'), false, 'Mimic with Replace mimics off is not sampled');
  const permutation = generate(config({ enemyPlacementAlgorithm: 'permutation-v1', enemyPlacementFit: 'anywhere', replaceMimics: false }), { gameCatalog: catalog });
  assert.deepEqual(sample.placements.bosses, permutation.placements.bosses);
  assert.deepEqual(sample.placements.items, permutation.placements.items);
  // The mimic's model is a fixed model of its map and counts toward the budget.
  assert.ok(sample.placementDiagnostics.summary.maps.m10_00_00_00.finalModels >= 1);
});

test('the summary is compact, free of paths and detail appears only at the detail level', () => {
  const summary = run({});
  assert.equal(summary.placementDiagnostics.level, 'summary');
  assert.equal(summary.placementDiagnostics.slots, undefined);
  assert.equal(summary.placementDiagnostics.algorithm, 'sampling-v1');
  assert.equal(summary.placementDiagnostics.rngVersion, 'enemy-placement-v1');
  const text = JSON.stringify(summary.placementDiagnostics);
  assert.ok(text.length < 6000, `summary is ${text.length} bytes`);
  assert.doesNotMatch(text, /[A-Za-z]:\\|\/Users\//u);
  const detail = run({ placementDiagnosticsLevel: 'detail' });
  assert.equal(detail.placementDiagnostics.slots.length, catalogOf(SPEC).enemySlots.length);
  assert.throws(() => run({ placementDiagnosticsLevel: 'verbose' }), TypeError);
  const s = summary.placementDiagnostics.summary;
  assert.equal(s.slots, catalogOf(SPEC).enemySlots.length);
  assert.deepEqual(s.terminalFailures, []);
  assert.equal(s.stages['fit-relaxed'], 0);
});

function bossCatalog() {
  const bossSlot = (mapId, name, model, npc, index) => slot(mapId, name, model, npc, index, 3, { entityId: 1_000_000 + index, hasEntityId: true, baseHp: 1000 });
  const bosses = [
    ['m10_00_00_00', 'c2240_0000', 'c2240', 224000], ['m11_00_00_00', 'c2250_0000', 'c2250', 225000],
    ['m12_00_00_00', 'c2260_0000', 'c2260', 226000], ['m13_00_00_00', 'c3320_0000', 'c3320', 332000],
    ['m14_00_00_00', 'c3330_0000', 'c3330', 333000], ['m15_00_00_00', 'c3340_0000', 'c3340', 334000],
    ['m16_00_00_00', 'c3350_0000', 'c3350', 335000], ['m18_00_00_00', 'c5370_0000', 'c5370', 537000],
  ].map(([mapId, name, model, npc], index) => bossSlot(mapId, name, model, npc, index + 1));
  const clones = Array.from({ length: 14 }, (_, i) =>
    slot('m13_00_00_00', `c3320_${String(i + 1).padStart(4, '0')}`, 'c3320', 332001, 50 + i, 3, { baseHp: 1, entityId: 1_300_800 + i + 1 }));
  const regular = catalogOf({
    m10_00_00_00: [['c1000', 0, 6], ['c1010', 1, 6], ['c1020', 2, 6]], m11_00_00_00: [['c1000', 0, 6], ['c1030', 3, 6]],
    m13_00_00_00: [['c1010', 1, 6], ['c1020', 2, 6]], m14_00_00_00: [['c1000', 0, 6], ['c1030', 3, 6]],
  }).enemySlots;
  return {
    schemaVersion: 17, maps: bosses.map((b) => ({ id: b.mapId, name: b.mapId })),
    enemySlots: [...bosses, ...clones, ...regular], enemyArchetypes: [{ id: 'x' }], bossSlots: bosses,
    worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

test('Pinwheel Chaos clones are ordinary sampled slots with their own stats, the main Pinwheel is untouched', () => {
  const catalog = bossCatalog();
  const cloneIds = catalog.enemySlots.filter((s) => s.id.startsWith('m13_00_00_00:c3320_') && !s.id.endsWith('_0000')).map((s) => s.id);
  for (let i = 0; i < 10; i += 1) {
    const result = generate(config({ seed: `pin-${i}`, randomizeBosses: true, pinwheelChaos: true, enemyScaling: 'vanilla' }), { gameCatalog: catalog });
    const clones = result.placements.enemies.filter((p) => cloneIds.includes(p.slot));
    assert.deepEqual(clones.map((p) => p.slot).sort(), [...cloneIds].sort());
    for (const p of clones) {
      assert.equal(p.compatibility, 'pinwheel-chaos-clone');
      assert.equal(p.scaling, 'vanilla');
      if (p.changed) assert.ok(Number.isInteger(p.scaledNpcParamId), 'drops follow the slot even with vanilla scaling');
    }
    assert.equal([...result.placements.bosses, ...result.placements.enemies].some((p) => p.slot === 'm13_00_00_00:c3320_0000'), false);
  }
});

test('bosses and units are planned exactly as under permutation-v1', () => {
  const catalog = bossCatalog();
  for (let i = 0; i < 5; i += 1) {
    const overrides = { seed: `boss-${i}`, randomizeBosses: true, gwynNerf: 'strong', secondGargoyle: 'replace' };
    const sample = generate(config(overrides), { gameCatalog: catalog });
    const permutation = generate(config({ ...overrides, enemyPlacementAlgorithm: 'permutation-v1', enemyPlacementFit: 'anywhere' }), { gameCatalog: catalog });
    assert.deepEqual(sample.placements.bosses, permutation.placements.bosses);
    assert.ok(sample.placements.bosses.length > 0);
    // Boss models are fixed models of their maps: they count toward the budget the sampler respects.
    for (const info of Object.values(sample.placementDiagnostics.summary.maps)) assert.ok(info.finalModels <= 30);
  }
});

test('the backend summary keeps counters and the class table hash, drops per-slot detail and never logs paths', () => {
  const detail = run({ placementDiagnosticsLevel: 'detail' });
  const summary = summarizePlacementDiagnostics(detail.placementDiagnostics);
  assert.equal(summary.algorithm, 'sampling-v1');
  assert.equal(summary.rngVersion, 'enemy-placement-v1');
  assert.equal(summary.classTableHash, detail.placementDiagnostics.classTableHash);
  assert.equal('slots' in summary, false);
  assert.equal(summary.summary.slots, catalogOf(SPEC).enemySlots.length);
  const line = formatPlacementDiagnostics(summary);
  assert.match(line, /^Enemy placement sampling-v1: \d+ regular slots, \d+ sampled/u);
  assert.doesNotMatch(line, /[A-Za-z]:\|\/Users\//u);
  // permutation-v1 and anything unreadable reduce to the frozen algorithm's identity.
  assert.deepEqual(summarizePlacementDiagnostics(undefined), { algorithm: 'permutation-v1', rngVersion: null, classTableHash: null, summary: null });
  assert.equal(formatPlacementDiagnostics(summarizePlacementDiagnostics(placement.permutationDiagnostics())), null);
});
