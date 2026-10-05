import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';
import { validateEnemyPlacement } from '../../dist/core/enemyPlacement.js';

// Part 0 of the sampling-v1 enemy placement work (docs/ENEMY_PLACEMENT_MODEL.md): infrastructure only. The
// sampler itself is not implemented; these tests pin the vocabulary, the RNG substreams, the budget abstraction,
// the gating and the unchanged behavior of permutation-v1.

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const RANDOMIZER_VERSION = loaded.core.normalizeConfig({}).version;
const placement = await import(pathToFileURL(resolveCombinedRandomizerCoreModulePath('core/placement.js')).href);
const rng = await import(pathToFileURL(resolveCombinedRandomizerCoreModulePath('core/rng.js')).href);

// ---- synthetic catalog (extracted game data is never committed) ----------------------------------------------------

function slot(mapId, name, modelName, npcParamId, index, extra = {}) {
  return {
    id: `${mapId}:${name}`, mapId, name, modelName, entityId: -1,
    npcParamId, thinkParamId: npcParamId, talkId: -1, charaInitId: npcParamId,
    collisionName: 'h0000B0', position: { x: index, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0 },
    movePoints: [], dummy: false, hasEntityId: false, eventModelLocked: false, riskFlags: [],
    safeCandidate: true, teamType: 0, npcType: 0, moveType: 0, hitHeight: 1, hitRadius: 1, hitYOffset: 0,
    baseHp: 100, soulReward: 10, battleStartDistance: 20, eyeDistance: 20, earDistance: 10, disablePathMove: false,
    battleGoalId: npcParamId, ...extra,
  };
}

function catalog() {
  const slots = [];
  let index = 0;
  const models = [['c1000', 100000], ['c1010', 101000], ['c1020', 102000], ['c1030', 103000]];
  for (const mapId of ['m10_00_00_00', 'm10_01_00_00', 'm11_00_00_00']) {
    for (let i = 0; i < 12; i += 1) {
      const [model, npc] = models[i % models.length];
      slots.push(slot(mapId, `${model}_${String(i).padStart(4, '0')}`, model, npc, index += 1));
    }
  }
  return {
    schemaVersion: 17,
    maps: ['m10_00_00_00', 'm10_01_00_00', 'm11_00_00_00'].map((id) => ({ id, name: id })),
    enemySlots: slots, enemyArchetypes: [{ id: 'x' }], bossSlots: [],
    worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

const config = (overrides = {}) => ({
  ...defaultConfig, seed: 'part0-seed', randomizeEnemies: true, randomizeBosses: false,
  randomizeItems: false, generateSpoilerLog: false, ...overrides,
});

// ---- algorithm identity and gating ---------------------------------------------------------------------------------

test('permutation-v1 is the engine default; sampling-v1 is implemented but never implied', () => {
  assert.equal(defaultConfig.enemyPlacementAlgorithm, 'permutation-v1');
  assert.equal(placement.DEFAULT_ENEMY_PLACEMENT_ALGORITHM, 'permutation-v1');
  assert.deepEqual([...placement.ENEMY_PLACEMENT_ALGORITHMS], ['permutation-v1', 'sampling-v1']);
  assert.deepEqual([...placement.IMPLEMENTED_ENEMY_PLACEMENT_ALGORITHMS], ['permutation-v1', 'sampling-v1']);
  assert.equal(RANDOMIZER_VERSION, '0.5.66', 'the shared RNG salt stays frozen');
});

test('the engine refuses unknown algorithms with a typed error and never falls back', () => {
  for (const algorithm of ['permutation-v2', 'sampling-v2', '', undefined]) {
    assert.throws(
      () => generate(config({ enemyPlacementAlgorithm: algorithm }), { gameCatalog: catalog() }),
      (error) => error.code === 'ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE'
        && error instanceof placement.EnemyPlacementUnavailableError
        && /unknown/u.test(error.message),
      String(algorithm),
    );
  }
  // The refusal does not depend on whether the enemy categories are enabled: a request is never ignored.
  assert.throws(
    () => generate(config({ enemyPlacementAlgorithm: 'sampling-v2', randomizeEnemies: false, randomizeItems: true }), { gameCatalog: catalog() }),
    (error) => error.code === 'ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE',
  );
  // normalizeConfig must not turn the value into the default.
  assert.equal(loaded.core.normalizeConfig({ enemyPlacementAlgorithm: 'sampling-v2' }).enemyPlacementAlgorithm, 'sampling-v2');
});

test('sampling-only options are validated in the engine and refused under permutation-v1', () => {
  for (const overrides of [
    { enemyReplacementChance: 50 }, { enemyNormalReplacement: 'none' }, { enemyPlacementFit: 'fits' },
  ]) {
    assert.throws(
      () => generate(config(overrides), { gameCatalog: catalog() }),
      (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID' && /only applies to sampling-v1/u.test(error.message),
      JSON.stringify(overrides),
    );
  }
  for (const overrides of [
    { enemyReplacementChance: 101 }, { enemyReplacementChance: -1 }, { enemyReplacementChance: 1.5 }, { enemyReplacementChance: '50' },
    { enemyNormalReplacement: 'bosses' }, { enemyNormalReplacement: 'both' }, { enemyPlacementFit: 'everywhere' },
  ]) {
    assert.throws(
      () => generate(config({ enemyPlacementAlgorithm: 'sampling-v1', ...overrides }), { gameCatalog: catalog() }),
      (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID',
      JSON.stringify(overrides),
    );
  }
});

test('validation rejects an unimplemented algorithm, unknown algorithms and non-neutral sampling-only options', () => {
  const problemsOf = (value, context) => { const problems = []; validateEnemyPlacement(value, problems, context); return problems; };
  const neutral = { replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null };
  assert.deepEqual(problemsOf({ algorithm: 'permutation-v1', ...neutral }), []);
  assert.deepEqual(problemsOf({ algorithm: 'sampling-v1', ...neutral, fit: 'fits' }), []);
  assert.match(problemsOf({ algorithm: 'sampling-v1', ...neutral }, { implemented: ['permutation-v1'] }).join(' '), /ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE/);
  assert.match(problemsOf({ algorithm: 'bogus', ...neutral }).join(' '), /invalid/);
  assert.match(problemsOf({}).join(' '), /algorithm is required/);
  assert.match(problemsOf({ algorithm: 'permutation-v1', ...neutral, extra: 1 }).join(' '), /extra is not supported/);
  assert.match(problemsOf('permutation-v1').join(' '), /must be an object/);

  // The gating mechanism, exercised with a registered sampling-only option and an implemented sampler.
  const samplingOnly = [{ key: 'sizeBias', neutral: 0 }];
  assert.deepEqual(problemsOf({ algorithm: 'permutation-v1', sizeBias: 0 }, { samplingOnly }), []);
  assert.match(problemsOf({ algorithm: 'permutation-v1', sizeBias: 2 }, { samplingOnly }).join(' '), /only applies to sampling-v1/);
  assert.deepEqual(problemsOf({ algorithm: 'sampling-v1', sizeBias: 2 }, { samplingOnly, implemented: ['permutation-v1', 'sampling-v1'] }), []);
});

// ---- permutation-v1 stays the same ---------------------------------------------------------------------------------

test('permutation-v1 output does not depend on the algorithm key and reports only its identity', () => {
  const explicit = generate(config({ enemyPlacementAlgorithm: 'permutation-v1' }), { gameCatalog: catalog() });
  const legacy = generate(config(), { gameCatalog: catalog() });
  assert.equal(explicit.placementHash, legacy.placementHash);
  assert.deepEqual(explicit.placements, legacy.placements);
  assert.deepEqual(explicit.placementDiagnostics, { algorithm: 'permutation-v1', rngVersion: null, level: 'summary', classTableHash: null, options: null, customPool: null, summary: null });
  assert.equal(explicit.randomizerVersion, '0.5.66');
});

test('permutation-v1 derives no sampling stream', () => {
  const before = placement.slotStreamDerivationCount();
  for (const scaling of ['vanilla', 'area', 'progressive']) {
    generate(config({ enemyScaling: scaling, randomizeBosses: true, preventSameEnemy: true }), { gameCatalog: catalog() });
  }
  assert.equal(placement.slotStreamDerivationCount(), before);
});

// ---- slot identity and RNG substreams ------------------------------------------------------------------------------

test('stable slot identity accepts catalog ids and rejects anything else', () => {
  assert.equal(placement.stableSlotId('m10_01_00_00:c5350_0001'), 'm10_01_00_00:c5350_0001');
  assert.equal(placement.stableSlotId({ id: 'm10_01_00_00:c5350_0001' }), 'm10_01_00_00:c5350_0001');
  assert.equal(placement.slotStreamName('m10_01_00_00:c5350_0001'), 'slot:m10_01_00_00:c5350_0001');
  for (const bad of ['', 'c5350_0001', 'm10_01:c5350', 42, null, { id: 7 }, 'C:\\x\\m10_01_00_00:c5350']) {
    assert.throws(() => placement.stableSlotId(bad), TypeError, String(bad));
  }
});

test('slot substreams are stable, per-slot, order independent and version independent', () => {
  const a = 'm10_00_00_00:c1000_0000';
  const b = 'm10_00_00_00:c1000_0004';
  assert.equal(placement.SAMPLING_RNG_VERSION, 'enemy-placement-v1');

  // Same input, same stream; the derivation is the documented one.
  const expected = createHash('sha256').update(`enemy-placement-v1\0seed\0slot:${a}`).digest().readUInt32LE(0);
  assert.equal(placement.deriveSlotStreamSeed('seed', a), expected);
  assert.equal(placement.deriveSlotStreamSeed('seed', a), placement.deriveSlotStreamSeed('seed', a));
  assert.deepEqual(
    Array.from({ length: 8 }, () => 0).map((_, i) => placement.createSlotStream('seed', a).int(1000 + i)),
    Array.from({ length: 8 }, () => 0).map((_, i) => placement.createSlotStream('seed', a).int(1000 + i)),
  );

  // Different slots, different seeds; different seeds, different streams.
  assert.notEqual(placement.deriveSlotStreamSeed('seed', a), placement.deriveSlotStreamSeed('seed', b));
  assert.notEqual(placement.deriveSlotStreamSeed('seed', a), placement.deriveSlotStreamSeed('other', a));

  // Other slots' draws (and the order they are made in) never change a slot's own sequence.
  const alone = placement.createSlotStream('seed', a);
  const aloneDraws = [alone.int(100), alone.int(100), alone.int(100)];
  const busy = placement.createSlotStream('seed', b);
  for (let i = 0; i < 50; i += 1) busy.int(100);
  const afterOthers = placement.createSlotStream('seed', a);
  assert.deepEqual([afterOthers.int(100), afterOthers.int(100), afterOthers.int(100)], aloneDraws);

  // The sampling version is independent of RANDOMIZER_VERSION and distinct from every permutation stream.
  assert.notEqual(placement.deriveSlotStreamSeed('seed', a), rng.deriveSeed('seed', `slot:${a}`, RANDOMIZER_VERSION));
  assert.notEqual(placement.SAMPLING_RNG_VERSION, RANDOMIZER_VERSION);
});

// ---- model budget --------------------------------------------------------------------------------------------------

test('the model budget counts fixed models, admits deterministically and never exceeds the cap', () => {
  assert.equal(placement.MODEL_BUDGET_CAP, 30);
  const budget = new placement.ModelBudget({ mapId: 'm10_00_00_00', cap: 3, fixedModels: ['c1030', 'c1000', 'c1030'] });
  assert.deepEqual(budget.admissionOrder(), ['c1000', 'c1030'], 'fixed models are deduplicated and sorted');
  assert.equal(budget.size, 2);
  assert.deepEqual(budget.evaluate('c1000'), { admitted: true, alreadyPresent: true });
  assert.deepEqual(budget.evaluate('c1010'), { admitted: true, alreadyPresent: false });
  budget.admit('c1010');
  assert.deepEqual(budget.evaluate('c1020'), { admitted: false, reason: 'model-budget', size: 3, cap: 3, mapId: 'm10_00_00_00' });
  assert.throws(() => budget.admit('c1020'), (error) => error.code === 'ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE');
  assert.equal(budget.admit('c1010').alreadyPresent, true);
  assert.deepEqual(budget.models(), ['c1000', 'c1010', 'c1030']);

  // A map that is over the cap through fixed models alone cannot be repaired.
  assert.throws(
    () => new placement.ModelBudget({ mapId: 'm10_00_00_00', cap: 2, fixedModels: ['a', 'b', 'c'] }),
    (error) => error.code === 'ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE' && error.mapId === 'm10_00_00_00',
  );
  // There is no per-map exception, including for the largest map.
  assert.equal(new placement.ModelBudget({ mapId: 'm12_00_00_01' }).cap, 30);
  assert.deepEqual(placement.checkCoverageReserve({ fixedModels: ['a'], nonParticipantModels: ['a', 'b'], reserveModels: ['c', 'b'], cap: 3 }), { ok: true, size: 3, cap: 3, over: 0 });
  assert.equal(placement.checkCoverageReserve({ fixedModels: ['a', 'b'], reserveModels: ['c', 'd'], cap: 3 }).over, 1);
});

test('the budget is not applied to permutation-v1', () => {
  // A map holding more than three distinct models is fine for permutation-v1; its own 30-model limiter is the only cap.
  const result = generate(config(), { gameCatalog: catalog() });
  assert.equal(result.dataStatus, 'extracted');
  assert.equal(result.placementDiagnostics.summary, null);
});

// ---- diagnostics ---------------------------------------------------------------------------------------------------

test('diagnostics keep a summary by default, add per-slot detail only on request and refuse paths', () => {
  const record = {
    slotId: 'm10_00_00_00:c1000_0000', slotClass: 'normal', participates: true, category: 'normal', selected: 'c1010',
    stage: 'in-set-fit', categoryDowngraded: true, rejected: [{ model: 'c1020', reason: 'model-budget' }, { model: 'c1030', reason: 'size' }],
  };
  const summaryOnly = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  summaryOnly.record(record);
  summaryOnly.record({ slotId: 'm10_00_00_00:c1000_0004', slotClass: 'normal', participates: false });
  summaryOnly.record({ slotId: 'm10_00_00_00:c1000_0008', slotClass: 'boss', participates: true, stage: 'infeasible' });
  const json = summaryOnly.toJSON();
  assert.equal(json.rngVersion, 'enemy-placement-v1');
  assert.equal(json.slots, undefined);
  assert.equal(json.summary.slots, 3);
  assert.equal(json.summary.participating, 2);
  assert.equal(json.summary.notParticipating, 1);
  assert.equal(json.summary.stages['in-set-fit'], 1);
  assert.equal(json.summary.rejections['model-budget'], 1);
  assert.equal(json.summary.categoryDowngrades, 1);
  assert.deepEqual(json.summary.terminalFailures, ['m10_00_00_00:c1000_0008']);

  const detail = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1', level: 'detail' });
  detail.record(record);
  assert.equal(detail.toJSON().slots.length, 1);
  assert.equal(detail.toJSON().slots[0].selected, 'c1010');

  assert.throws(() => detail.record({ ...record, selected: 'C:\\Users\\someone\\x' }), TypeError);
  assert.throws(() => detail.record({ ...record, slotId: 'C:\\Users\\someone' }), TypeError);
  assert.throws(() => detail.record({ ...record, stage: 'nope' }), TypeError);
  assert.throws(() => detail.record({ ...record, rejected: [{ model: 'c1', reason: 'nope' }] }), TypeError);
  assert.throws(() => placement.createPlacementDiagnostics({ algorithm: 'nope' }), TypeError);
  assert.throws(() => placement.createPlacementDiagnostics({ algorithm: 'sampling-v1', level: 'verbose' }), TypeError);
});

// ---- drop ownership ------------------------------------------------------------------------------------------------

test('permutation-v1 keeps its legacy drop ownership; the sampling infrastructure can express slot ownership', () => {
  const changed = (scaling) => generate(config({ enemyScaling: scaling, seed: 'drops' }), { gameCatalog: catalog() })
    .placements.enemies.filter((p) => p.changed);
  const vanilla = changed('vanilla');
  assert.ok(vanilla.length > 0);
  // Documented legacy behavior (not fixed in Part 0): without a scaled row the replacement keeps its own drops.
  assert.ok(vanilla.every((p) => placement.describeDropOwnership(p).owner === placement.DROP_OWNERSHIP.REPLACEMENT));
  assert.throws(() => placement.assertSlotOwnedDrops(vanilla), /ENEMY_PLACEMENT_DROP_OWNERSHIP/);
  // Area scaling already creates a scaled row for every changed placement.
  const area = changed('area');
  assert.ok(area.every((p) => placement.describeDropOwnership(p).owner === placement.DROP_OWNERSHIP.SLOT));
  placement.assertSlotOwnedDrops(area);
  // The helper a sampling placement uses to force slot ownership whatever the global scaling mode is.
  const forced = vanilla.map((p) => ({ ...p, ...placement.slotOwnedDrops(777001) }));
  placement.assertSlotOwnedDrops(forced);
  assert.deepEqual(placement.describeDropOwnership(forced[0]), { owner: 'slot', scaledNpcParamId: 777001 });
  assert.throws(() => placement.slotOwnedDrops(0), RangeError);
  assert.throws(() => placement.slotOwnedDrops(null), RangeError);
});
