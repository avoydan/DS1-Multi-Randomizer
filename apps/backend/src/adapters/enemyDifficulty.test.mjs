import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';

// sampling-v1 difficulty (Part B) and map-local retries, on synthetic catalogs.

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const load = (file) => import(pathToFileURL(resolveCombinedRandomizerCoreModulePath(`core/${file}`)).href);
const sampling = await load('sampling.js');
const placement = await load('placement.js');

// ---- synthetic catalogs -----------------------------------------------------------------------------------------------

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

/** Eight maps along the progression, three models each, HP growing with the tier: difficulty classes 0-7 all occur. */
function tieredSpec() {
  const maps = ['m18_01_00_00', 'm10_01_00_00', 'm10_00_00_00', 'm11_00_00_00', 'm14_00_00_00', 'm15_00_00_00', 'm12_01_00_00', 'm18_00_00_00'];
  const spec = {};
  maps.forEach((mapId, tier) => {
    spec[mapId] = [0, 1, 2].map((k) => [`c${tier}${k}00`, (tier + k) % 5, 6, 40 * (tier + 1) * (k + 1) * (k + 1)]);
  });
  return spec;
}

const config = (overrides = {}) => ({
  ...defaultConfig, seed: 'difficulty-seed', randomizeEnemies: true, randomizeBosses: false, randomizeItems: false,
  generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1', enemyPlacementFit: 'anywhere', ...overrides,
});
const run = (overrides, spec = tieredSpec()) => generate(config(overrides), { gameCatalog: catalogOf(spec) });
const slotsOf = (spec) => catalogOf(spec).enemySlots;

// ---- difficulty classes -----------------------------------------------------------------------------------------------------

test('difficulty classes are derived deterministically, independent of catalog order, and every type has a valid one', () => {
  const slots = slotsOf(tieredSpec());
  const table = sampling.deriveRegularClassTable(slots);
  assert.equal(table.version, 'regular-class-v2');
  assert.ok(table.types.every((type) => Number.isInteger(type.difficulty) && type.difficulty >= 0 && type.difficulty <= 7));
  assert.ok(new Set(table.types.map((type) => type.difficulty)).size >= 5, 'the synthetic progression spreads over the classes');
  assert.equal(sampling.deriveRegularClassTable(slots).hash, table.hash);
  const reversed = sampling.deriveRegularClassTable([...slots].reverse());
  assert.equal(reversed.hash, table.hash);
  assert.deepEqual(reversed.types.map((t) => [t.model, t.size, t.difficulty]), table.types.map((t) => [t.model, t.size, t.difficulty]));
  // Ordering of the models within the progression: a later map with more HP is never easier than an earlier one with less.
  const easy = table.byModel.get('c0000').difficulty;
  const hard = table.byModel.get('c7200').difficulty;
  assert.ok(hard > easy, `${hard} > ${easy}`);
});

test('the class table hash reacts to every input that changes a difficulty class', () => {
  const slots = slotsOf(tieredSpec());
  const base = sampling.deriveRegularClassTable(slots);
  // More HP for one model moves its HP rank, its class or both.
  const stronger = slots.map((s) => (s.modelName === 'c0000' ? { ...s, baseHp: 1e7 } : s));
  const strongerTable = sampling.deriveRegularClassTable(stronger);
  assert.notEqual(strongerTable.hash, base.hash);
  assert.ok(strongerTable.byModel.get('c0000').difficulty > base.byModel.get('c0000').difficulty);
  // The same model living in a later map moves its tier.
  const moved = slots.map((s) => (s.modelName === 'c0000' ? { ...s, mapId: 'm18_00_00_00', id: s.id.replace('m18_01_00_00', 'm18_00_00_00') } : s));
  assert.notEqual(sampling.deriveRegularClassTable(moved).hash, base.hash);
  // A change that alters neither size nor difficulty does not change the hash.
  const nudged = slots.map((s) => (s.modelName === 'c0000' ? { ...s, baseHp: s.baseHp + 1 } : s));
  assert.equal(sampling.deriveRegularClassTable(nudged).hash, base.hash);
});

test('a class that cannot be derived is a typed structural error, never an arbitrary class 0', () => {
  const slots = slotsOf(tieredSpec());
  assert.throws(
    () => sampling.deriveRegularClassTable(slots.map((s) => (s.modelName === 'c0000' ? { ...s, baseHp: 0 } : s))),
    (error) => error.code === 'ENEMY_PLACEMENT_CLASS_UNDERIVABLE' && /base HP/u.test(error.message),
  );
  assert.throws(
    () => sampling.deriveRegularClassTable(slots.map((s) => (s.modelName === 'c0000' ? { ...s, mapId: 'm99_00_00_00' } : s))),
    (error) => error.code === 'ENEMY_PLACEMENT_CLASS_UNDERIVABLE' && /progression tier/u.test(error.message),
  );
});

test('a slot without a regular type (a Pinwheel clone) gets a derived destination class from its map and HP', () => {
  const table = sampling.deriveRegularClassTable(slotsOf(tieredSpec()));
  const clone = slot('m13_00_00_00', 'c3320_0001', 'c3320', 332001, 9000, 3, { baseHp: 1 });
  const early = sampling.slotDifficultyClass(table, slot('m18_01_00_00', 'c3320_0002', 'c3320', 332001, 9001, 3, { baseHp: 500 }));
  const late = sampling.slotDifficultyClass(table, slot('m18_00_00_00', 'c3320_0003', 'c3320', 332001, 9002, 3, { baseHp: 500 }));
  assert.ok(Number.isInteger(sampling.slotDifficultyClass(table, clone)));
  assert.ok(late > early);
  assert.throws(() => sampling.slotDifficultyClass(table, { ...clone, baseHp: 0 }), (error) => error.code === 'ENEMY_PLACEMENT_CLASS_UNDERIVABLE');
});

// ---- eligibility matrix ----------------------------------------------------------------------------------------------------------

test('the strictness tables are complete distributions and follow the documented ordering', () => {
  for (const strictness of ['strict', 'loose', 'very-loose']) {
    const total = sampling.STRICTNESS_OFFSETS[strictness].reduce((sum, [, percent]) => sum + percent, 0);
    assert.ok(total < 100, 'some mass stays on the own class');
    for (let destination = 0; destination <= 7; destination += 1) {
      const distribution = sampling.difficultyDistribution(destination, strictness);
      assert.equal(distribution.length, 8);
      assert.equal(distribution.reduce((a, b) => a + b, 0), 100, `${strictness}/${destination}`);
      assert.ok(distribution[destination] >= 100 - total, 'the own class never loses its stay mass');
    }
  }
  // The own class keeps more mass the stricter the setting.
  for (let destination = 0; destination <= 7; destination += 1) {
    const stay = (strictness) => sampling.difficultyDistribution(destination, strictness)[destination];
    assert.ok(stay('strict') > stay('loose') && stay('loose') > stay('very-loose'), `destination ${destination}`);
  }
  assert.throws(() => sampling.difficultyDistribution(8, 'strict'), RangeError);
  assert.throws(() => sampling.difficultyDistribution(3, 'sloppy'), RangeError);
});

test('eligibility matrix: which candidate classes can replace which destination, per mode, strictness and map', () => {
  const REACH = { strict: { up: 2, down: 1 }, loose: { up: 5, down: 3 }, 'very-loose': { up: 7, down: 4 } };
  for (const mode of ['random', 'random-easy-asylum', 'curve', 'curve-easy-asylum']) {
    for (const strictness of ['strict', 'loose', 'very-loose']) {
      for (let destination = 0; destination <= 7; destination += 1) {
        for (const mapId of ['m10_01_00_00', 'm18_01_00_00']) {
          const eligible = sampling.eligibleDifficultyClasses({ mode, strictness, destination, mapId });
          const asylum = mapId === 'm18_01_00_00' && mode.endsWith('easy-asylum');
          const label = `${mode}/${strictness}/${destination}/${mapId}`;
          if (asylum) {
            assert.deepEqual(eligible, [0, 1], label);
          } else if (mode.startsWith('curve')) {
            const lowest = Math.max(0, destination - REACH[strictness].down);
            const highest = Math.min(7, destination + REACH[strictness].up);
            assert.deepEqual(eligible, Array.from({ length: highest - lowest + 1 }, (_, i) => lowest + i), label);
            assert.ok(eligible.includes(destination), `${label}: the own class is always eligible`);
          } else {
            assert.deepEqual(eligible, [0, 1, 2, 3, 4, 5, 6, 7], label);
          }
        }
      }
    }
  }
  // Boundaries spelled out: the lowest and highest destination classes cannot leave 0-7.
  assert.deepEqual(sampling.eligibleDifficultyClasses({ mode: 'curve', strictness: 'strict', destination: 0, mapId: 'm10_00_00_00' }), [0, 1, 2]);
  assert.deepEqual(sampling.eligibleDifficultyClasses({ mode: 'curve', strictness: 'strict', destination: 7, mapId: 'm10_00_00_00' }), [6, 7]);
  assert.deepEqual(sampling.eligibleDifficultyClasses({ mode: 'curve', strictness: 'very-loose', destination: 0, mapId: 'm10_00_00_00' }), [0, 1, 2, 3, 4, 5, 6, 7], 'very loose can put the hardest enemy in the easiest place');
  assert.deepEqual(sampling.eligibleDifficultyClasses({ mode: 'curve', strictness: 'loose', destination: 0, mapId: 'm10_00_00_00' }), [0, 1, 2, 3, 4, 5]);
});

test('the draw maps onto the distribution exactly: every draw in 0-99 selects an eligible class with the stated frequency', () => {
  for (const strictness of ['strict', 'loose', 'very-loose']) {
    for (let destination = 0; destination <= 7; destination += 1) {
      const distribution = sampling.difficultyDistribution(destination, strictness);
      const counts = new Array(8).fill(0);
      for (let draw = 0; draw < 100; draw += 1) counts[sampling.difficultyTargetClass(destination, strictness, draw)] += 1;
      assert.deepEqual(counts, distribution, `${strictness}/${destination}`);
    }
  }
});

// ---- planning with difficulty -----------------------------------------------------------------------------------------------------------

function detailRun(overrides, spec = tieredSpec()) {
  const result = run({ placementDiagnosticsLevel: 'detail', ...overrides }, spec);
  return { result, records: result.placementDiagnostics.slots };
}

test('curve mode: a candidate is drawn from the eligible classes, or from the own class when the drawn class is empty', () => {
  for (const strictness of ['strict', 'loose', 'very-loose']) {
    const { records, result } = detailRun({ enemyDifficultyMode: 'curve', enemyDifficultyStrictness: strictness, seed: `curve-${strictness}` });
    let checked = 0;
    for (const record of records.filter((r) => r.stage === 'sampled')) {
      const eligible = sampling.eligibleDifficultyClasses({ mode: 'curve', strictness, destination: record.destinationClass, mapId: record.slotId.split(':')[0] });
      assert.ok(eligible.includes(record.candidateClass) || record.candidateClass === record.destinationClass || result.placementDiagnostics.summary.counters.difficultyLapses > 0,
        `${record.slotId}: ${record.candidateClass} for destination ${record.destinationClass}`);
      assert.ok(eligible.includes(record.targetClass));
      checked += 1;
    }
    assert.ok(checked > 20);
  }
});

test('curve strictness orders the spread of the drawn classes: strict < a bit loose < very loose', () => {
  const spread = (strictness) => {
    let total = 0;
    let count = 0;
    for (let i = 0; i < 12; i += 1) {
      const { records } = detailRun({ enemyDifficultyMode: 'curve', enemyDifficultyStrictness: strictness, seed: `spread-${i}` });
      for (const record of records.filter((r) => r.stage === 'sampled' && r.candidateClass !== undefined)) {
        total += Math.abs(record.candidateClass - record.destinationClass);
        count += 1;
      }
    }
    return total / count;
  };
  const [strict, loose, veryLoose] = ['strict', 'loose', 'very-loose'].map(spread);
  assert.ok(strict < loose && loose < veryLoose, `${strict.toFixed(2)} < ${loose.toFixed(2)} < ${veryLoose.toFixed(2)}`);
});

test('fully random draws no class: the output equals the one without any difficulty setting', () => {
  const plain = run({});
  const random = run({ enemyDifficultyMode: 'random', enemyDifficultyStrictness: 'very-loose' });
  assert.equal(random.placementHash, plain.placementHash, 'strictness has no effect outside the curve modes');
  assert.equal(random.placementDiagnostics.summary.counters.candidatesOutOfClass ?? 0, 0);
});

test('easy Asylum: Asylum slots only receive the easiest classes, other maps are unrestricted', () => {
  const table = sampling.deriveRegularClassTable(slotsOf(tieredSpec()));
  for (const mode of ['random-easy-asylum', 'curve-easy-asylum']) {
    let outsideSeen = false;
    for (let i = 0; i < 8; i += 1) {
      const { records } = detailRun({ enemyDifficultyMode: mode, seed: `asylum-${i}` });
      for (const record of records.filter((r) => r.stage === 'sampled')) {
        const model = record.selected;
        if (record.slotId.startsWith('m18_01_00_00:')) assert.ok(table.byModel.get(model).difficulty <= sampling.EASY_ASYLUM_MAX_CLASS, `${record.slotId} -> ${model}`);
        else if (table.byModel.get(model).difficulty > sampling.EASY_ASYLUM_MAX_CLASS) outsideSeen = true;
      }
    }
    assert.ok(outsideSeen, `${mode}: other maps still receive harder classes`);
  }
  // Without easy Asylum the Asylum is not restricted.
  let harder = false;
  for (let i = 0; i < 8; i += 1) {
    const { records } = detailRun({ enemyDifficultyMode: 'random', seed: `asylum-${i}` });
    if (records.some((r) => r.slotId.startsWith('m18_01_00_00:') && r.stage === 'sampled' && table.byModel.get(r.selected).difficulty > 1)) harder = true;
  }
  assert.ok(harder);
});

test('difficulty never relaxes Placement fit and never breaks Prevent same', () => {
  const sizes = new Map(sampling.deriveRegularClassTable(slotsOf(tieredSpec())).types.map((t) => [t.model, t.size]));
  for (const mode of ['curve', 'curve-easy-asylum', 'random-easy-asylum']) {
    for (const strictness of ['strict', 'very-loose']) {
      for (let i = 0; i < 6; i += 1) {
        const { records, result } = detailRun({
          enemyDifficultyMode: mode, enemyDifficultyStrictness: strictness, enemyPlacementFit: 'fits', preventSameEnemy: true,
          enemyReplacementChance: 70, seed: `fit-${i}`,
        });
        for (const record of records.filter((r) => r.stage === 'sampled' || r.stage === 'in-set-fit')) {
          const own = record.slotId.split(':')[1].slice(0, 5);
          assert.ok(sizes.get(record.selected) <= sizes.get(own), `${record.slotId} -> ${record.selected}`);
          assert.notEqual(record.selected, own, 'Prevent same');
        }
        assert.equal(result.placementDiagnostics.summary.stages['fit-relaxed'], 0);
      }
    }
  }
});

test('difficulty rejections are counted and a difficulty fallback is visible in the summary', () => {
  const { result } = detailRun({ enemyDifficultyMode: 'curve', enemyDifficultyStrictness: 'strict', seed: 'counters' });
  const summary = result.placementDiagnostics.summary;
  assert.ok(summary.counters.candidatesOutOfClass > 0, 'candidates outside the wanted class are counted');
  assert.deepEqual(result.placementDiagnostics.options.difficultyMode, 'curve');
  assert.equal(result.placementDiagnostics.options.difficultyStrictness, 'strict');
  assert.match(result.placementDiagnostics.classTableHash, /^[0-9a-f]{64}$/u);
  assert.notEqual(result.placementDiagnostics.classTableHash, 'x');
});

// ---- Part A compatibility ---------------------------------------------------------------------------------------------------------------

const PART_A_SPEC = {
  m10_00_00_00: [['c1000', 0, 12], ['c1010', 1, 10], ['c1020', 2, 8], ['c1030', 3, 6], ['c1040', 4, 4], ['c1050', 5, 2]],
  m11_00_00_00: [['c1000', 0, 10], ['c1020', 2, 10], ['c1050', 5, 4], ['c1060', 1, 6]],
  m12_00_00_00: [['c1010', 1, 8], ['c1030', 3, 8], ['c1040', 4, 4]],
};
// Placement hashes produced by the Part A planner (commit e6d99f3) for these exact inputs; attempt 0 must not move.
const PART_A_PINS = JSON.parse(`{
 "pin-1/100/fits/false": "5e0926bc7b15594b",
 "pin-1/100/fits/true": "2fe08db67c86e0e7",
 "pin-1/100/anywhere/false": "a46bf074a3d9b724",
 "pin-1/100/anywhere/true": "f91f3a571e6ed734"
}`);

test('with neutral difficulty and a first attempt that succeeds, the output is the Part A output', () => {
  for (const [key, expected] of Object.entries(PART_A_PINS)) {
    const [seed, chance, fit, prevent] = key.split('/');
    const result = generate(config({
      seed, enemyReplacementChance: Number(chance), enemyPlacementFit: fit, preventSameEnemy: prevent === 'true',
    }), { gameCatalog: catalogOf(PART_A_SPEC) });
    assert.equal(result.placementHash.slice(0, 16), expected, key);
    assert.equal(result.placementDiagnostics.summary.counters.mapsRetried ?? 0, 0);
  }
});

// ---- map-local retries ----------------------------------------------------------------------------------------------------------------------

const MAP = 'm10_00_00_00';
const OTHER = 'm11_00_00_00';
/** Map M can only be solved when at least one of its 4 big slots keeps its model (zero headroom, Prevent same). */
const RETRY_SPEC = { [MAP]: [['cB', 3, 4]], [OTHER]: [['cA', 0, 6], ['cB', 3, 6], ['cC', 2, 6]] };
const fixed30 = new Map([[MAP, new Set(Array.from({ length: 30 }, (_, i) => `x${i + 1}`))]]);

function retryPlan(seed, overrides = {}) {
  const slots = catalogOf(RETRY_SPEC).enemySlots;
  const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  const plan = () => sampling.planSampledRegularEnemies({
    seed, options: { replacementChance: 90, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'random', difficultyStrictness: 'loose' },
    preventSame: true, slots, fixedModelsByMap: fixed30, diagnostics, ...overrides,
  });
  return { plan, diagnostics };
}

test('attempt streams: attempt 0 is the original slot stream, later attempts use their own namespace', () => {
  const id = `${MAP}:cB_0000`;
  assert.equal(placement.slotStreamName(id), `slot:${id}`);
  assert.equal(placement.slotStreamName(id, 0), `slot:${id}`);
  assert.equal(placement.slotStreamName(id, 2), `attempt:2:slot:${id}`);
  assert.equal(placement.deriveSlotStreamSeed('s', id), placement.deriveSlotStreamSeed('s', id, 0));
  assert.notEqual(placement.deriveSlotStreamSeed('s', id, 0), placement.deriveSlotStreamSeed('s', id, 1));
  assert.notEqual(placement.deriveSlotStreamSeed('s', id, 1), placement.deriveSlotStreamSeed('s', id, 2));
  assert.throws(() => placement.slotStreamName(id, -1), RangeError);
});

test('a map that fails is retried alone, deterministically and within the bound; other maps stay as they were', () => {
  let retried = null;
  let exhausted = null;
  for (let i = 0; i < 300 && (!retried || !exhausted); i += 1) {
    const { plan, diagnostics } = retryPlan(`retry-${i}`);
    try {
      const result = plan();
      const summary = diagnostics.toJSON().summary;
      if (!retried && summary.counters.mapsRetried) retried = { seed: `retry-${i}`, result, summary };
    } catch (error) {
      if (!exhausted && error.code === 'ENEMY_PLACEMENT_INFEASIBLE') exhausted = { seed: `retry-${i}`, error, summary: diagnostics.toJSON().summary };
    }
  }
  assert.ok(retried, 'a seed whose first attempt fails and a later attempt succeeds');
  assert.ok(exhausted, 'a seed whose attempts all fail');

  // Deterministic: the same seed and configuration choose the same attempt and the same placements.
  const again = retryPlan(retried.seed);
  const second = again.plan();
  assert.deepEqual([...second.assignment].map(([id, s]) => [id, s.id]), [...retried.result.assignment].map(([id, s]) => [id, s.id]));
  assert.equal(again.diagnostics.toJSON().summary.maps[MAP].attempts, retried.summary.maps[MAP].attempts);
  assert.ok(retried.summary.maps[MAP].attempts > 1 && retried.summary.maps[MAP].attempts <= sampling.MAP_ATTEMPTS);
  assert.equal(retried.summary.counters.mapsRetried, 1);
  assert.ok(retried.summary.counters.maxMapAttempt >= 1);
  assert.equal(retried.summary.retries.length, retried.summary.counters.mapAttemptsFailed);
  assert.ok(retried.summary.retries.every((entry) => entry.mapId === MAP && Number.isInteger(entry.attempt)));

  // Other maps: identical to a run where the failing map succeeds at once (same slots, hence the same class table).
  const easy = retryPlan(retried.seed, { fixedModelsByMap: new Map() });
  const easyResult = easy.plan();
  const inOther = (result) => [...result.assignment].filter(([id]) => id.startsWith(OTHER)).map(([id, s]) => [id, s.id]);
  assert.deepEqual(inOther(retried.result), inOther(easyResult), 'the map that was retried does not change another map');
  assert.equal(easy.diagnostics.toJSON().summary.counters.mapsRetried ?? 0, 0);

  // Bounded and typed: every attempt failed.
  assert.equal(exhausted.summary.counters.mapAttemptsFailed, sampling.MAP_ATTEMPTS);
  assert.equal(exhausted.error.attempts, sampling.MAP_ATTEMPTS);
  assert.equal(exhausted.error.mapId, MAP);
  assert.equal(exhausted.error.baseline, 30);
  assert.equal(exhausted.error.effectiveCap, 30);
  assert.equal(exhausted.error.difficultyMode, 'random');
  assert.equal(exhausted.error.fit, 'fits');
  assert.match(exhausted.error.message, /3 map attempts failed/u);
  assert.match(exhausted.error.message, /effective cap 30/u);
});

test('a retry rebuilds the map from scratch: the attempts draw the participation from different streams', () => {
  const first = (attempt) => placement.createSlotStream('retry-participation', `${MAP}:cB_0000`, attempt).int(100);
  const draws = [first(0), first(1), first(2)];
  assert.equal(new Set(draws).size, 3, `attempt streams are independent: ${draws}`);
  assert.equal(first(0), placement.createSlotStream('retry-participation', `${MAP}:cB_0000`).int(100), 'attempt 0 is the plain slot stream');
});

test('difficulty options are validated by the engine and refused under permutation-v1', () => {
  for (const overrides of [{ enemyDifficultyMode: 'hard' }, { enemyDifficultyStrictness: 'sloppy' }, { enemyDifficultyMode: 4 }]) {
    assert.throws(() => run(overrides), (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID', JSON.stringify(overrides));
  }
  for (const overrides of [{ enemyDifficultyMode: 'curve' }, { enemyDifficultyMode: 'random-easy-asylum' }, { enemyDifficultyStrictness: 'strict' }]) {
    assert.throws(
      () => run({ enemyPlacementAlgorithm: 'permutation-v1', enemyPlacementFit: 'anywhere', ...overrides }),
      (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID' && /only applies to sampling-v1/u.test(error.message),
      JSON.stringify(overrides),
    );
  }
  assert.ok(run({ enemyPlacementAlgorithm: 'permutation-v1', enemyDifficultyMode: 'random', enemyDifficultyStrictness: 'loose' }).placementHash);
});
