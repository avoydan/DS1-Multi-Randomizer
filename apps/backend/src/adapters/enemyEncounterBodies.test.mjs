import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';

// Part E2: a normal enemy as the body of a boss encounter. The planner on synthetic slots, and the engine end to end on a synthetic catalog.

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const load = (file) => import(pathToFileURL(resolveCombinedRandomizerCoreModulePath(`core/${file}`)).href);
const sampling = await load('sampling.js');
const placement = await load('placement.js');

const keyOf = (slot) => `${slot.modelName}:${slot.npcParamId}`;
const boss = (map, model, index = 0) => ({ id: `${map}:${model}_000${index}`, mapId: map, modelName: model, npcParamId: 100000 + Number(model.slice(1)), thinkParamId: 1, hitHeight: 3, hitRadius: 1, baseHp: 900 });
const MAPS = ['m18_01_00_00', 'm10_01_00_00'];
const SUPPORTED = new Set(['m18_01_00_00:c2232_0000', 'm10_01_00_00:c2250_0000']);
const slots = [boss(MAPS[0], 'c2232'), boss(MAPS[1], 'c2250'), boss(MAPS[1], 'c2240'), boss(MAPS[0], 'c2230')];
const archetypes = ['c5200', 'c5210', 'c5220', 'c2250', 'c2240', 'c2232', 'c2230'].map((model) => boss('m10_00_00_00', model));
const type = (model, size, difficulty) => ({ key: `${model}:${model.slice(1)}00`, model, size, difficulty, source: { id: `m14_00_00_00:${model}_0000`, mapId: 'm14_00_00_00', modelName: model, npcParamId: Number(model.slice(1)) * 100, thinkParamId: Number(model.slice(1)) * 100 } });
const TYPES = [type('c2500', 1, 0), type('c2560', 1, 1), type('c2790', 1, 5), type('c2810', 2, 3), type('c2910', 2, 5)];
const counts = (map) => new Map(Object.entries(map).map(([mapId, models]) => [mapId, new Map(Object.entries(models))]));
const bodies = (extra = {}) => ({
  supported: SUPPORTED, types: TYPES, destination: () => ({ size: 3, difficulty: 4 }),
  modelCounts: counts({ [MAPS[0]]: { c2232: 1, c2230: 1, c2500: 10 }, [MAPS[1]]: { c2250: 1, c2240: 1, c2500: 12 } }),
  vanillaSizes: new Map([[MAPS[0], 3], [MAPS[1], 3]]), ...extra,
});

function plan(seed, options, extra = {}) {
  const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  const result = sampling.planSampledBosses({
    seed, options: { replacementChance: 100, uniqueBosses: false, bossReplacement: 'normals', bossChanceInBossSlots: 90, fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', ...options },
    slots, pool: archetypes, keyOf, gwynRejection: 0, gwynModel: 'c5370', isUnsafePair: () => false, diagnostics, bodies: bodies(), ...extra,
  });
  return { ...result, counters: diagnostics.toJSON().summary.counters };
}

test('"normals" gives the supported boss slots a normal enemy as the body and leaves every other slot alone', () => {
  for (let seed = 0; seed < 30; seed += 1) {
    const { assignment, bodySlots, counters } = plan(`n-${seed}`, {});
    assert.deepEqual([...bodySlots].sort(), [...SUPPORTED].sort());
    assert.deepEqual([...assignment.keys()].sort(), [...SUPPORTED].sort(), 'nothing else is placed');
    for (const id of SUPPORTED) assert.ok(TYPES.some((entry) => entry.source === assignment.get(id)), 'a body comes from the pool');
    assert.equal(counters.bodiesPlaced, 2);
    assert.equal(counters.bossSlotsKeptUnsupported, 2);
  }
});

test('"both" gives a supported slot a boss with the chance and a normal enemy otherwise; a slot that is not supported only ever gets a boss', () => {
  const share = (chance) => {
    let bodiesPlaced = 0;
    for (let seed = 0; seed < 200; seed += 1) bodiesPlaced += plan(`b-${seed}`, { bossReplacement: 'both', bossChanceInBossSlots: chance }).bodySlots.size;
    return bodiesPlaced / (200 * SUPPORTED.size);
  };
  assert.equal(share(100), 0, 'chance 100: always a boss');
  assert.equal(share(0), 1, 'chance 0: always a normal enemy');
  const middle = share(60);
  assert.ok(middle > 0.34 && middle < 0.46, `about 40% normal enemies at a 60% boss chance (got ${middle.toFixed(3)})`);
  const { assignment, bodySlots } = plan('unsupported', { bossReplacement: 'both', bossChanceInBossSlots: 0 });
  for (const slot of slots.filter((entry) => !SUPPORTED.has(entry.id))) {
    assert.equal(bodySlots.has(slot.id), false);
    assert.ok(assignment.has(slot.id), 'an unsupported slot still draws a boss');
  }
});

test('the body is a pure function of the seed and the slot; "bosses" does not know the body options at all', () => {
  const flat = (result) => [...result.assignment].map(([id, source]) => `${id}>${source.modelName}`).sort().join(',');
  assert.equal(flat(plan('same', {})), flat(plan('same', {})));
  assert.ok(Array.from({ length: 20 }, (_, index) => flat(plan(`other-${index}`, {}))).some((value) => value !== flat(plan('same', {}))), 'the seed matters');
  const reversed = sampling.planSampledBosses({
    seed: 'same', options: { replacementChance: 100, uniqueBosses: false, bossReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random' }, slots: [...slots].reverse(), pool: archetypes, keyOf,
    gwynRejection: 0, gwynModel: 'c5370', isUnsafePair: () => false, diagnostics: placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' }), bodies: bodies(),
  });
  assert.equal(flat(reversed), flat(plan('same', {})), 'slot order of the input does not matter');
  // Part D: with Boss replacement "bosses" the extra description changes nothing (no extra draw, no extra stream).
  const withBodies = plan('d', { bossReplacement: 'bosses' });
  const without = plan('d', { bossReplacement: 'bosses' }, { bodies: null });
  assert.equal(flat(withBodies), flat(without));
  assert.equal(withBodies.bodySlots.size, 0);
});

test('Placement fit, the curve modes, the model limit and the Unique bosses cycle are honored', () => {
  // fits: a body never exceeds the slot's size class
  for (let seed = 0; seed < 60; seed += 1) {
    const { assignment } = plan(`fit-${seed}`, { fit: 'fits' }, { bodies: bodies({ destination: () => ({ size: 1, difficulty: 4 }) }) });
    for (const source of assignment.values()) assert.ok(TYPES.find((entry) => entry.source === source).size <= 1, source.modelName);
  }
  // curve (strict): mostly the class of the destination, never outside what the pool has
  let exact = 0;
  const classes = new Set(TYPES.map((entry) => entry.difficulty));
  for (let seed = 0; seed < 80; seed += 1) {
    const { assignment } = plan(`curve-${seed}`, { difficultyMode: 'curve', difficultyStrictness: 'strict' }, { bodies: bodies({ destination: () => ({ size: 3, difficulty: 5 }) }) });
    for (const source of assignment.values()) {
      const difficulty = TYPES.find((entry) => entry.source === source).difficulty;
      assert.ok(classes.has(difficulty));
      if (difficulty === 5) exact += 1;
    }
  }
  assert.ok(exact / (80 * SUPPORTED.size) > 0.5, `strict stays near the destination class (${exact})`);
  // model limit: the map holds max(30, its vanilla count) models. A body whose model the map already holds always fits; a new model
  // fits only if the boss model that leaves frees its place (here the boss model is shared by a second slot, so it does not).
  const full = Object.fromEntries(Array.from({ length: 29 }, (_, index) => [`c9${String(index).padStart(3, '0')}`, 1]));
  const tightCounts = counts({ [MAPS[0]]: { c2232: 2, c2500: 1, ...full, c9998: 1 }, [MAPS[1]]: { c2250: 2, c2500: 1, ...full, c9998: 1 } });
  let placed = 0;
  for (let seed = 0; seed < 60; seed += 1) {
    const tight = plan(`tight-${seed}`, {}, { bodies: bodies({ modelCounts: tightCounts, vanillaSizes: new Map([[MAPS[0], 32], [MAPS[1], 32]]) }) });
    for (const source of tight.assignment.values()) { assert.equal(source.modelName, 'c2500', 'only the model the map already holds fits'); placed += 1; }
    assert.equal(tight.counters.bodiesPlaced ?? 0, tight.bodySlots.size);
  }
  assert.ok(placed > 0, 'c2500 is admitted');
  // the same map with the boss model alone in it: the leaving model frees a place, so any body fits
  const freeing = plan('freeing', {}, { bodies: bodies({ modelCounts: counts({ [MAPS[0]]: { c2232: 1, c2500: 1, ...full, c9998: 1 }, [MAPS[1]]: { c2250: 1, c2500: 1, ...full, c9998: 1 } }), vanillaSizes: new Map([[MAPS[0], 32], [MAPS[1], 32]]) }) });
  assert.equal(freeing.bodySlots.size, 2);
  // Unique bosses: bodies never take part in the cycle
  const unique = plan('u', { bossReplacement: 'both', bossChanceInBossSlots: 0, uniqueBosses: true });
  assert.equal(unique.counters.bossesReplaced ?? 0, slots.length - SUPPORTED.size, 'only the unsupported slots drew bosses');
});

// ---- the engine end to end -------------------------------------------------------------------------------------------------------------

function engineSlot(mapId, name, model, npc, extra = {}) {
  return {
    id: `${mapId}:${name}`, mapId, name, modelName: model, entityId: -1, npcParamId: npc, thinkParamId: npc, talkId: -1, charaInitId: npc, collisionName: 'h0000B0',
    position: { x: 0, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, movePoints: [], dummy: false, hasEntityId: false, eventModelLocked: false, riskFlags: [], safeCandidate: true,
    teamType: 0, npcType: 0, moveType: 3, hitHeight: 1.5, hitRadius: 0.5, hitYOffset: 0, baseHp: 100, soulReward: 10, battleStartDistance: 20, eyeDistance: 20, earDistance: 10,
    disablePathMove: false, battleGoalId: npc, ...extra,
  };
}

function engineCatalog() {
  const enemySlots = [];
  for (const mapId of ['m10_00_00_00', 'm14_00_00_00', 'm18_01_00_00']) {
    for (const [index, [model, npc]] of [['c2500', 250000], ['c2560', 256000], ['c2790', 279000], ['c2810', 281000]].entries()) {
      for (let copy = 0; copy < 3; copy += 1) enemySlots.push(engineSlot(mapId, `${model}_00${index}${copy}`, model, npc, { baseHp: 100 + index * 50, hitHeight: 1.5 + index * 0.3 }));
    }
  }
  const bossRow = (mapId, model, npc, entityId) => engineSlot(mapId, `${model}_0000`, model, npc, { entityId, hasEntityId: true, teamType: 1, hitHeight: 4, hitRadius: 1.5, baseHp: 900, thinkParamId: npc + 1 });
  const bossSlots = [bossRow('m18_01_00_00', 'c2232', 223200, 1810800), bossRow('m10_01_00_00', 'c2250', 225000, 1010700), bossRow('m10_01_00_00', 'c2240', 224000, 1010750), bossRow('m14_01_00_00', 'c5200', 520000, 1410700)];
  return {
    schemaVersion: 17, maps: ['m10_00_00_00', 'm14_00_00_00', 'm18_01_00_00', 'm10_01_00_00', 'm14_01_00_00'].map((id) => ({ id, name: id })), enemySlots: [...enemySlots, ...bossSlots],
    enemyArchetypes: [{ id: 'x' }], bossSlots, worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

const base = { ...defaultConfig, randomizeEnemies: true, randomizeBosses: true, randomizeItems: false, generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1', enemyNormalReplacement: 'none', secondGargoyle: 'keep' };

test('the engine puts a normal enemy in a supported boss slot under the boss entity, with the encounter-owned row and a plain combat brain', () => {
  const seen = new Set();
  for (let seed = 0; seed < 20; seed += 1) {
    const result = generate({ ...base, seed: `eng-${seed}`, enemyBossReplacement: 'normals' }, { gameCatalog: engineCatalog() });
    const bodies = result.placements.bosses.filter((entry) => entry.compatibility === 'sampled-encounter-body');
    assert.ok(bodies.length >= 1 && bodies.length <= 3, 'the supported slots of this catalog');
    for (const entry of bodies) {
      assert.equal(entry.encounterBody, true);
      assert.ok([1810800, 1010700, 1010750].includes(entry.entityId), 'the entity id stays the boss entity');
      assert.ok(['c2500', 'c2560', 'c2790', 'c2810'].includes(entry.targetModelName), entry.targetModelName);
      assert.ok(Number.isInteger(entry.scaledNpcParamId), 'a scaled row: the encounter owns drops and rewards');
      assert.equal(entry.forceCombatActivation, true, 'the wake-up event of every boss placement');
      assert.equal(entry.portableBossSanitization, true);
      seen.add(entry.targetModelName);
    }
    // everything that is not supported is vanilla in "normals"
    assert.equal(result.placements.bosses.some((entry) => entry.slot.endsWith('c5200_0000')), false);
  }
  assert.ok(seen.size >= 2, 'different bodies appear');
});

test('without the body modes the engine output is the same as before: the new option changes nothing', () => {
  const hashOf = (config) => JSON.stringify(generate(config, { gameCatalog: engineCatalog() }).placements);
  assert.equal(hashOf({ ...base, seed: 'same', enemyBossReplacement: 'bosses' }), hashOf({ ...base, seed: 'same', enemyBossReplacement: 'bosses', enemyBossChanceBossSlots: 90 }));
  assert.equal(hashOf({ ...base, seed: 'same', enemyBossReplacement: 'classic' }), hashOf({ ...base, seed: 'same', enemyBossReplacement: 'classic', enemyBossChanceBossSlots: 90 }));
  const result = generate({ ...base, seed: 'same', enemyBossReplacement: 'bosses' }, { gameCatalog: engineCatalog() });
  assert.ok(result.placements.bosses.every((entry) => entry.compatibility !== 'sampled-encounter-body'));
});

test('the body options are refused unless the mode applies', () => {
  const catalog = { schemaVersion: 17, maps: [], enemySlots: [], enemyArchetypes: [{ id: 'x' }], bossSlots: [], worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [] };
  for (const overrides of [
    { enemyBossReplacement: 'bosses', enemyBossChanceBossSlots: 50 },
    { enemyBossReplacement: 'normals', enemyBossChanceBossSlots: 50 },
    { enemyBossReplacement: 'classic', enemyBossChanceBossSlots: 50 },
    { enemyBossReplacement: 'both', enemyBossChanceBossSlots: 101 },
    { enemyBossReplacement: 'normals', enemyUniqueBosses: true },
    { enemyBossReplacement: 'whatever' },
  ]) {
    assert.throws(() => generate({ ...base, ...overrides }, { gameCatalog: catalog }), (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID', JSON.stringify(overrides));
  }
  // Unique bosses is allowed with "both" (it concerns the boss draws); permutation-v1 refuses the new modes.
  assert.doesNotThrow(() => generate({ ...base, enemyBossReplacement: 'both', enemyUniqueBosses: true, enemyBossChanceBossSlots: 70 }, { gameCatalog: engineCatalog() }));
  for (const mode of ['normals', 'both']) {
    assert.throws(() => generate({ ...base, enemyPlacementAlgorithm: 'permutation-v1', enemyBossReplacement: mode }, { gameCatalog: catalog }), (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID', mode);
  }
});
