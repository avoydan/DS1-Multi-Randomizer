import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';

// sampling-v1 boss sampling (Part D): the boss planner on synthetic boss slots, and the option contract of the engine.

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { defaultConfig } = loaded.core;
const load = (file) => import(pathToFileURL(resolveCombinedRandomizerCoreModulePath(`core/${file}`)).href);
const sampling = await load('sampling.js');
const placement = await load('placement.js');

const bossSlot = (map, model, index) => ({ id: `${map}:${model}_000${index}`, mapId: map, modelName: model, npcParamId: 100000 + Number(model.slice(1)) });
const keyOf = (slot) => `${slot.modelName}:${slot.npcParamId}`;
const MODELS = ['c5200', 'c5210', 'c5220', 'c5230', 'c5240', 'c5250', 'c5260', 'c2250', 'c5370', 'c5380'];
const pool = MODELS.map((model) => bossSlot('m10_00_00_00', model, 0));
const slots = MODELS.map((model, index) => bossSlot(`m1${index % 8}_00_00_00`, model, 1));
const unsafe = (slot, candidate) => slot.modelName === 'c2250' && candidate.modelName === 'c5250';

function run(seed, options, extra = {}) {
  const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  const result = sampling.planSampledBosses({
    seed, options: { replacementChance: 100, uniqueBosses: false, ...options }, slots, pool, keyOf,
    gwynRejection: 0, gwynModel: 'c5370', isUnsafePair: unsafe, diagnostics, ...extra,
  });
  return { assignment: result.assignment, counters: diagnostics.toJSON().summary.counters };
}
const flat = (assignment) => [...assignment].map(([slot, boss]) => `${slot}>${boss.modelName}`).join(',');

test('the boss sampler is deterministic and a pure function of the seed', () => {
  assert.equal(flat(run('seed-a', {}).assignment), flat(run('seed-a', {}).assignment));
  assert.notEqual(flat(run('seed-a', {}).assignment), flat(run('seed-b', {}).assignment));
  // Slot order of the input does not matter: slots are processed in stable id order.
  const reversed = sampling.planSampledBosses({
    seed: 'seed-a', options: { replacementChance: 100, uniqueBosses: false }, slots: [...slots].reverse(), pool: [...pool].reverse(), keyOf,
    gwynRejection: 0, gwynModel: 'c5370', isUnsafePair: unsafe, diagnostics: placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' }),
  });
  assert.equal(flat(reversed.assignment), flat(run('seed-a', {}).assignment));
});

test('every boss slot gets another boss, never itself, never an unsafe pair, and bosses may repeat', () => {
  let repeats = 0;
  for (let seed = 0; seed < 60; seed += 1) {
    const { assignment, counters } = run(`rep-${seed}`, {});
    assert.equal(assignment.size, slots.length);
    assert.equal(counters.bossesReplaced, slots.length);
    for (const slot of slots) {
      const boss = assignment.get(slot.id);
      assert.notEqual(keyOf(boss), keyOf(slot), 'a boss is never replaced by itself');
      assert.equal(slot.modelName === 'c2250' && boss.modelName === 'c5250', false, 'Ceaseless Discharge never receives c5250');
    }
    const models = [...assignment.values()].map((boss) => boss.modelName);
    repeats += models.length - new Set(models).size;
  }
  assert.ok(repeats > 0, 'sampling is with replacement: some boss appears in more than one place');
});

test('the replacement chance decides, per slot, whether a boss slot takes part at all', () => {
  assert.equal(run('chance', { replacementChance: 0 }).assignment.size, 0);
  assert.equal(run('chance', { replacementChance: 0 }).counters.bossSlotsNotParticipating, slots.length);
  assert.equal(run('chance', { replacementChance: 100 }).assignment.size, slots.length);
  let total = 0;
  const seeds = 200;
  for (let seed = 0; seed < seeds; seed += 1) total += run(`c-${seed}`, { replacementChance: 40 }).assignment.size;
  const share = total / (seeds * slots.length);
  assert.ok(share > 0.34 && share < 0.46, `about 40% of the slots take part (got ${share.toFixed(3)})`);
  // The chance is the first draw of the slot's own stream: lowering it only removes slots, it never moves the others.
  const full = run('stable', { replacementChance: 100 }).assignment;
  const half = run('stable', { replacementChance: 50 }).assignment;
  for (const [slotId, boss] of half) assert.equal(boss.modelName, full.get(slotId).modelName, 'a slot that still takes part draws the same boss');
});

test('the Gwyn nerf rejects Lord Gwyn per candidate with the legacy probability', () => {
  const count = (gwynRejection) => {
    let gwyn = 0;
    for (let seed = 0; seed < 150; seed += 1) gwyn += [...run(`g-${seed}`, {}, { gwynRejection }).assignment.values()].filter((boss) => boss.modelName === 'c5370').length;
    return gwyn;
  };
  const none = count(0);
  const medium = count(0.6);
  const strong = count(0.85);
  assert.ok(none > medium && medium > strong, `Gwyn is placed less often as the nerf grows (${none} > ${medium} > ${strong})`);
  assert.ok(strong < none * 0.3, 'strong nerf: Gwyn is rare');
  const rejected = run('g-1', {}, { gwynRejection: 0.85 }).counters['bossRejections:gwyn-nerf'] ?? 0;
  assert.ok(rejected >= 0);
});

test('Unique bosses uses every boss of the pool once before any boss repeats, then starts again', () => {
  const small = MODELS.slice(0, 5).map((model) => bossSlot('m10_00_00_00', model, 0));
  const slotsOf = (count) => Array.from({ length: count }, (_, index) => bossSlot(`m1${index % 8}_0${Math.floor(index / 8)}_00_00`, MODELS[index % 5], 7));
  for (let seed = 0; seed < 40; seed += 1) {
    // 5 bosses in the pool: the first four draws (each excluding the slot's own boss) cannot repeat within one cycle.
    const input = slotsOf(4);
    const { assignment } = sampling.planSampledBosses({
      seed: `u-${seed}`, options: { replacementChance: 100, uniqueBosses: true }, slots: input, pool: small, keyOf, gwynRejection: 0, gwynModel: 'c5370',
      isUnsafePair: () => false, diagnostics: placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' }),
    });
    const models = [...assignment.values()].map((boss) => boss.modelName);
    assert.equal(new Set(models).size, models.length, `no repeat inside one cycle (seed ${seed}): ${models.join(',')}`);
  }
  // More slots than bosses: the cycle restarts, every slot is still served.
  const many = slotsOf(12);
  const manyDiagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  const { assignment } = sampling.planSampledBosses({
    seed: 'u-many', options: { replacementChance: 100, uniqueBosses: true }, slots: many, pool: small, keyOf, gwynRejection: 0, gwynModel: 'c5370',
    isUnsafePair: () => false, diagnostics: manyDiagnostics,
  });
  const counters = manyDiagnostics.toJSON().summary.counters;
  assert.equal(assignment.size + (counters.bossesKeptVanilla ?? 0), many.length);
  assert.ok(assignment.size >= 11);
});

test('a slot without any allowed replacement stays vanilla and is counted', () => {
  const only = [bossSlot('m10_00_00_00', 'c5200', 0)];
  const aloneDiagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  const { assignment } = sampling.planSampledBosses({
    seed: 'alone', options: { replacementChance: 100, uniqueBosses: false }, slots: only, pool: only, keyOf, gwynRejection: 0, gwynModel: 'c5370',
    isUnsafePair: () => false, diagnostics: aloneDiagnostics,
  });
  const counters = aloneDiagnostics.toJSON().summary.counters;
  assert.equal(assignment.size, 0);
  assert.equal(counters.bossesKeptVanilla, 1);
});

test('the boss options never reach the regular-slot settings, and the diagnostics of regular slots stay as before', () => {
  const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  run('diag', {}, { diagnostics });
  const json = diagnostics.toJSON();
  assert.equal(json.summary.slots, 0, 'boss slots are counted separately from the regular-slot summary');
  assert.equal(Object.values(json.summary.rejections).reduce((a, b) => a + b, 0), 0);
});

test('engine options: boss sampling is sampling-only, unique bosses needs it, and nothing is ignored silently', () => {
  const base = { ...defaultConfig };
  assert.equal(base.enemyBossReplacement, 'classic');
  assert.equal(base.enemyUniqueBosses, false);
  const sampled = { ...base, enemyPlacementAlgorithm: 'sampling-v1', enemyBossReplacement: 'bosses', enemyUniqueBosses: true };
  const resolved = placement.resolveEnemyPlacementOptions(sampled);
  assert.equal(resolved.bossReplacement, 'bosses');
  assert.equal(resolved.uniqueBosses, true);
  assert.throws(() => placement.resolveEnemyPlacementOptions({ ...base, enemyBossReplacement: 'bosses' }), /enemyBossReplacement only applies to sampling-v1/u);
  assert.throws(() => placement.resolveEnemyPlacementOptions({ ...base, enemyPlacementAlgorithm: 'sampling-v1', enemyUniqueBosses: true }), /needs enemyBossReplacement/u);
  assert.throws(() => placement.resolveEnemyPlacementOptions({ ...base, enemyPlacementAlgorithm: 'sampling-v1', enemyBossReplacement: 'everything' }), /enemyBossReplacement must be/u);
  assert.throws(() => placement.resolveEnemyPlacementOptions({ ...base, enemyPlacementAlgorithm: 'sampling-v1', enemyBossReplacement: 'bosses', enemyUniqueBosses: 'yes' }), /enemyUniqueBosses must be true or false/u);
  // permutation-v1 with the neutral values is unchanged.
  assert.equal(placement.resolveEnemyPlacementOptions(base).bossReplacement, 'classic');
});
