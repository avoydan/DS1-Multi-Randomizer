import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';

// Roaming bosses (Part E1): bosses standing in regular enemy slots. The planner on synthetic slots, and the engine's option rules.

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const load = (file) => import(pathToFileURL(resolveCombinedRandomizerCoreModulePath(`core/${file}`)).href);
const sampling = await load('sampling.js');
const placement = await load('placement.js');

const MAP = 'm10_00_00_00';
const regular = (mapId, model, index, size = 1) => ({
  id: `${mapId}:${model}_${String(index).padStart(4, '0')}`, mapId, modelName: model, npcParamId: 100000 + Number(model.slice(1)), thinkParamId: 1,
  hitHeight: size, hitRadius: size / 2, baseHp: 100 + Number(model.slice(1)) % 50,
});
const boss = (model, size, mapId = 'm15_01_00_00') => ({
  id: `${mapId}:${model}_0000`, mapId, modelName: model, npcParamId: 200000 + Number(model.slice(1)), thinkParamId: 1,
  hitHeight: size, hitRadius: size / 2, baseHp: 5000,
});

const MODELS = ['c1000', 'c1010', 'c1020', 'c1030'];
const slots = (count = 12, mapId = MAP) => Array.from({ length: count }, (_, index) => regular(mapId, MODELS[index % MODELS.length], index));
const BOSSES = [boss('c2240', 2.8), boss('c2250', 3.5), boss('c2320', 6), boss('c3320', 2), boss('c4100', 2.6), boss('c4500', 4)];

function plan(seed, options, input = {}) {
  const regularSlots = input.slots ?? slots();
  const table = sampling.deriveRegularClassTable(regularSlots);
  const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  const roaming = sampling.planRoamingBosses({
    seed,
    options: { replacementChance: 100, normalReplacement: 'bosses', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', bossChanceInNormalSlots: 10, ...options },
    slots: regularSlots,
    allSlots: regularSlots,
    assignment: input.assignment ?? new Map(),
    fixedModelsByMap: input.fixed ?? new Map(),
    table,
    bossTypes: sampling.deriveRoamingBossTypes(input.bosses ?? BOSSES, table),
    preventSame: input.preventSame ?? false,
    excludedSlotIds: input.excluded ?? new Set(),
    diagnostics,
  });
  return { roaming, counters: diagnostics.toJSON().summary.counters };
}
const flat = (roaming) => [...roaming].map(([slot, source]) => `${slot}>${source.modelName}`).sort().join(',');

test('the roaming planner is deterministic, a pure function of the seed, and independent of the input order', () => {
  assert.equal(flat(plan('a', {}).roaming), flat(plan('a', {}).roaming));
  assert.notEqual(flat(plan('a', {}).roaming), flat(plan('b', {}).roaming));
  const reversed = slots().reverse();
  assert.equal(flat(plan('a', {}, { slots: reversed }).roaming), flat(plan('a', {}).roaming));
});

test('only the modes "bosses" and "both" place bosses; the rest of the options decide how many', () => {
  for (const normalReplacement of ['none', 'normals']) assert.equal(plan('m', { normalReplacement }).roaming.size, 0, normalReplacement);
  assert.equal(plan('m', { normalReplacement: 'bosses', replacementChance: 0 }).roaming.size, 0);
  assert.equal(plan('m', { normalReplacement: 'both', bossChanceInNormalSlots: 0 }).roaming.size, 0);
  // "bosses" at 100% takes every slot of the map (there is no limit on the number of bosses of a map; backend 0.45.5).
  assert.equal(plan('m', { normalReplacement: 'bosses' }).roaming.size, 12);
  assert.equal(plan('m', { normalReplacement: 'bosses' }, { slots: slots(6) }).roaming.size, 6);
  assert.equal(plan('m', { normalReplacement: 'both', bossChanceInNormalSlots: 100 }, { slots: slots(6) }).roaming.size, 6);
  // "both" takes about the chance of the slots that take part.
  let total = 0;
  for (let seed = 0; seed < 200; seed += 1) total += plan(`share-${seed}`, { normalReplacement: 'both', bossChanceInNormalSlots: 30 }, { slots: slots(8) }).roaming.size;
  const share = total / (200 * 8);
  assert.ok(share > 0.24 && share < 0.36, `about 30% of the slots get a boss (got ${share.toFixed(3)})`);
});

test('lowering the boss chance or the replacement chance only removes slots; it never moves the others', () => {
  for (const key of ['bossChanceInNormalSlots', 'replacementChance']) {
    const options = { normalReplacement: 'both', ...(key === 'replacementChance' ? { bossChanceInNormalSlots: 100 } : {}) };
    const full = plan('stable', { ...options, [key]: 100 }, { slots: slots(8) }).roaming;
    const half = plan('stable', { ...options, [key]: 50 }, { slots: slots(8) }).roaming;
    assert.ok(half.size < full.size, key);
    for (const [slotId, source] of half) assert.equal(source.modelName, full.get(slotId)?.modelName, `${key}: ${slotId}`);
  }
});

test('there is no limit on the number of bosses of a map: every slot that takes part gets one, in every map, and the model budget counts distinct models only', () => {
  const two = [...slots(60, 'm10_00_00_00'), ...slots(40, 'm10_01_00_00')];
  const { roaming, counters } = plan('nocap', { normalReplacement: 'bosses' }, { slots: two });
  assert.equal(roaming.size, 100);
  assert.equal([...roaming.keys()].filter((id) => id.startsWith('m10_00')).length, 60);
  assert.equal(counters.roamingMapLimitReached, undefined, 'the counter of the removed limit no longer exists');
  assert.equal(counters.roamingBosses, 100);
  // Many instances, few distinct models: the 100 bosses use at most the 6 test archetypes.
  assert.ok(new Set([...roaming.values()].map((source) => source.modelName)).size <= BOSSES.length);
});

test('the diagnostics explain every ordinary destination of a map: protected by reason, selected, boss, normal draw, unchanged by reason', () => {
  const regularSlots = slots(20);
  const excluded = new Set(regularSlots.slice(0, 4).map((slot) => slot.id));
  const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  const table = sampling.deriveRegularClassTable(regularSlots);
  sampling.planRoamingBosses({
    seed: 'diag', options: { replacementChance: 50, normalReplacement: 'both', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', bossChanceInNormalSlots: 50 },
    slots: regularSlots, allSlots: regularSlots, assignment: new Map(), fixedModelsByMap: new Map(), table, bossTypes: sampling.deriveRoamingBossTypes(BOSSES, table), preventSame: false,
    excludedSlotIds: excluded, protectedReasons: new Map([...excluded].map((id) => [id, 'passiveSlot'])), diagnostics,
  });
  const map = diagnostics.toJSON().summary.roaming[MAP];
  assert.equal(map.destinations, 20);
  assert.deepEqual(map.protectedByReason, { passiveSlot: 4 });
  assert.equal(map.eligible, 16);
  assert.equal(map.notParticipating + map.selected, 16);
  assert.equal(map.boss + map.normalDraw + Object.values(map.unchanged).reduce((sum, value) => sum + value, 0), map.selected);
  assert.ok(map.boss > 0 && map.normalDraw > 0 && map.notParticipating > 0);
});

test('fit availability diagnostics distinguish no candidate from one or multiple fitting bosses', () => {
  const mapId = MAP;
  const destinations = [
    regular(mapId, 'c1000', 0, 0.8),
    regular(mapId, 'c1010', 1, 1.0),
    regular(mapId, 'c1020', 2, 1.8),
  ];
  const diagnostics = placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' });
  const table = sampling.deriveRegularClassTable(destinations);
  const roaming = sampling.planRoamingBosses({
    seed: 'fit-contract',
    options: { replacementChance: 100, normalReplacement: 'bosses', fit: 'fits', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', bossChanceInNormalSlots: 10 },
    slots: destinations,
    allSlots: destinations,
    assignment: new Map(),
    fixedModelsByMap: new Map(),
    table,
    bossTypes: sampling.deriveRoamingBossTypes([boss('c2230', 1.0), boss('c2240', 2.0)], table),
    preventSame: false,
    diagnostics,
  });

  assert.equal(roaming.size, 2, 'every selected destination with a fitting candidate receives a boss');
  const map = diagnostics.toJSON().summary.roaming[mapId];
  assert.equal(map.unchanged.fit, 1, 'the only vanilla survivor has no fitting boss');
  assert.deepEqual(map.fitCandidateAnalysis, {
    0: { destinations: 1, fittingBossArchetypes: 0, slotsByCandidateCount: { zero: 1, one: 0, two: 0, threePlus: 0 }, unchangedFit: 1 },
    1: { destinations: 1, fittingBossArchetypes: 1, slotsByCandidateCount: { zero: 0, one: 1, two: 0, threePlus: 0 }, unchangedFit: 0 },
    2: { destinations: 1, fittingBossArchetypes: 2, slotsByCandidateCount: { zero: 0, one: 0, two: 1, threePlus: 0 }, unchangedFit: 0 },
  });
});

test('Prevent same enemy, Placement fit and the excluded slots are honored', () => {
  for (let seed = 0; seed < 40; seed += 1) {
    const withBosses = [...slots(), regular(MAP, 'c2240', 90, 2.8)];
    const { roaming } = plan(`ps-${seed}`, { normalReplacement: 'bosses' }, { slots: withBosses, preventSame: true });
    const own = new Map(withBosses.map((slot) => [slot.id, slot.modelName]));
    for (const [slotId, source] of roaming) assert.notEqual(source.modelName, own.get(slotId));
  }
  for (let seed = 0; seed < 40; seed += 1) {
    const small = slots(12).map((slot) => ({ ...slot, hitHeight: 1, hitRadius: 0.4 }));
    const { roaming } = plan(`fit-${seed}`, { normalReplacement: 'bosses', fit: 'fits' }, { slots: small });
    for (const source of roaming.values()) assert.ok(sampling.slotFootprint(source) <= 1.7 + 1e-9 || sampling.sizeClassOfFootprint(sampling.slotFootprint(source)) <= 1, `${source.modelName} fits`);
  }
  const excluded = new Set(slots().slice(0, 6).map((slot) => slot.id));
  const { roaming } = plan('ex', { normalReplacement: 'bosses' }, { excluded });
  for (const id of excluded) assert.equal(roaming.has(id), false);
  assert.equal(roaming.size, 6);
});

test('the model budget never gets worse: a map at the limit takes only bosses that replace a whole model', () => {
  const fixed = new Map([[MAP, new Set(Array.from({ length: 26 }, (_, index) => `c9${String(index).padStart(3, '0')}`))]]);
  for (let seed = 0; seed < 60; seed += 1) {
    const { roaming } = plan(`budget-${seed}`, { normalReplacement: 'bosses', replacementChance: 60 }, { fixed });
    const final = new Set(fixed.get(MAP));
    for (const slot of slots()) final.add((roaming.get(slot.id) ?? slot).modelName);
    assert.ok(final.size <= 30, `seed ${seed}: ${final.size} models`);
  }
  // A map that already exceeds the limit keeps its count at most.
  const over = new Map([[MAP, new Set(Array.from({ length: 33 }, (_, index) => `c9${String(index).padStart(3, '0')}`))]]);
  for (let seed = 0; seed < 30; seed += 1) {
    const { roaming } = plan(`over-${seed}`, { normalReplacement: 'bosses' }, { fixed: over });
    const final = new Set(over.get(MAP));
    for (const slot of slots()) final.add((roaming.get(slot.id) ?? slot).modelName);
    assert.ok(final.size <= 37, `seed ${seed}: ${final.size} models (baseline 37)`);
    const baseline = new Set(over.get(MAP));
    for (const slot of slots()) baseline.add(slot.modelName);
    assert.ok(final.size <= Math.max(30, baseline.size), `seed ${seed}: ${final.size} > ${baseline.size}`);
  }
});

test('Type replacement: "enabled" gives a whole kind one boss, "except roaming bosses" decides every slot on its own', () => {
  const grouped = [...slots(8)];
  let differing = 0;
  for (let seed = 0; seed < 40; seed += 1) {
    const { roaming } = plan(`tg-${seed}`, { normalReplacement: 'bosses', typeReplacement: 'enabled' }, { slots: grouped });
    for (const model of MODELS) {
      const bosses = new Set(grouped.filter((slot) => slot.modelName === model).map((slot) => roaming.get(slot.id)?.modelName));
      assert.equal(bosses.size, 1, `${model} gets one boss for the whole kind`);
    }
    const free = plan(`tg-${seed}`, { normalReplacement: 'bosses', typeReplacement: 'enabled-except-roaming-bosses' }, { slots: grouped }).roaming;
    for (const model of MODELS) if (new Set(grouped.filter((slot) => slot.modelName === model).map((slot) => free.get(slot.id)?.modelName)).size > 1) differing += 1;
  }
  assert.ok(differing > 0, 'without grouping the slots of one kind draw different bosses');
  // A kind with a slot that cannot take a boss stays whole.
  const blocked = new Set([grouped[0].id]);
  const { roaming } = plan('blocked', { normalReplacement: 'bosses', typeReplacement: 'enabled' }, { slots: grouped, excluded: blocked });
  for (const slot of grouped.filter((entry) => entry.modelName === grouped[0].modelName)) assert.equal(roaming.has(slot.id), false);
});

test('a size-class override replaces the capsule class of a boss whose capsule does not describe its body', () => {
  const table = sampling.deriveRegularClassTable(slots());
  const butterfly = boss('c3230', 0.4);
  const plain = sampling.deriveRoamingBossTypes([butterfly], table);
  const overridden = sampling.deriveRoamingBossTypes([butterfly], table, new Map([['c3230', 3]]));
  assert.equal(plain[0].size, 0);
  assert.equal(overridden[0].size, 3);
  // Under "fits" the overridden boss no longer fits the smallest slots.
  const small = slots(12).map((slot) => ({ ...slot, hitHeight: 1, hitRadius: 0.4 }));
  const run = (types) => sampling.planRoamingBosses({
    seed: 'ov', options: { replacementChance: 100, normalReplacement: 'bosses', fit: 'fits', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', bossChanceInNormalSlots: 10 },
    slots: small, allSlots: small, assignment: new Map(), fixedModelsByMap: new Map(), table: sampling.deriveRegularClassTable(small), bossTypes: types, preventSame: false,
    diagnostics: placement.createPlacementDiagnostics({ algorithm: 'sampling-v1' }),
  });
  assert.equal(run(plain).size, 12);
  assert.equal(run(overridden).size, 0);
});

test('difficulty modes: the curve limits where a boss may stand, Easy Asylum keeps bosses out of the Asylum', () => {
  const asylum = slots(10, 'm18_01_00_00');
  for (let seed = 0; seed < 30; seed += 1) {
    assert.equal(plan(`ea-${seed}`, { normalReplacement: 'bosses', difficultyMode: 'random-easy-asylum' }, { slots: asylum }).roaming.size, 0);
    assert.equal(plan(`cea-${seed}`, { normalReplacement: 'bosses', difficultyMode: 'curve-easy-asylum' }, { slots: asylum }).roaming.size, 0);
  }
  assert.ok(plan('ra', { normalReplacement: 'bosses', difficultyMode: 'random' }, { slots: asylum }).roaming.size > 0);
});

test('the roaming options are refused unless the mode applies, and the neutral values equal the legacy defaults', () => {
  assert.equal(defaultConfig.enemyBossChanceNormalSlots, 10);
  assert.equal(defaultConfig.enemyRoamingBossSouls, 50);
  assert.equal(defaultConfig.enemyRoamingBossesStayDead, false);
  const base = { ...defaultConfig, seed: 'e1', randomizeEnemies: true, randomizeBosses: false, randomizeItems: false, generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1' };
  const catalog = { schemaVersion: 17, maps: [], enemySlots: [], enemyArchetypes: [{ id: 'x' }], bossSlots: [], worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [] };
  for (const overrides of [
    { enemyNormalReplacement: 'normals', enemyBossChanceNormalSlots: 30 },
    { enemyNormalReplacement: 'bosses', enemyBossChanceNormalSlots: 30 },
    { enemyNormalReplacement: 'normals', enemyRoamingBossSouls: 20 },
    { enemyNormalReplacement: 'none', enemyRoamingBossesStayDead: true },
    { enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 101 },
    { enemyNormalReplacement: 'both', enemyRoamingBossSouls: -1 },
    { enemyNormalReplacement: 'both', enemyRoamingBossesStayDead: 'yes' },
  ]) {
    assert.throws(() => generate({ ...base, ...overrides }, { gameCatalog: catalog }), (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID', JSON.stringify(overrides));
  }
  // permutation-v1 refuses the cross-class modes and their options (they need sampling-v1).
  for (const overrides of [{ enemyNormalReplacement: 'bosses' }, { enemyNormalReplacement: 'both' }]) {
    assert.throws(() => generate({ ...base, enemyPlacementAlgorithm: 'permutation-v1', ...overrides }, { gameCatalog: catalog }), (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID', JSON.stringify(overrides));
  }
});

// ---- the engine end to end on a synthetic catalog -----------------------------------------------------------------------

function engineSlot(mapId, name, model, npc, index, extra = {}) {
  return {
    id: `${mapId}:${name}`, mapId, name, modelName: model, entityId: -1, npcParamId: npc, thinkParamId: npc, talkId: -1, charaInitId: npc,
    collisionName: 'h0000B0', position: { x: index, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, movePoints: [], dummy: false, hasEntityId: false,
    eventModelLocked: false, riskFlags: [], safeCandidate: true, teamType: 0, npcType: 0, moveType: 0, hitHeight: 1.2, hitRadius: 0.5, hitYOffset: 0,
    baseHp: 100, soulReward: 10, battleStartDistance: 20, eyeDistance: 20, earDistance: 10, disablePathMove: false, battleGoalId: npc, ...extra,
  };
}

function engineCatalog() {
  const regularModels = [['c1000', 100000], ['c1010', 101000], ['c1020', 102000], ['c1030', 103000]];
  const enemySlots = [];
  let index = 0;
  for (const mapId of ['m10_00_00_00', 'm11_00_00_00', 'm12_00_00_00']) {
    for (let i = 0; i < 16; i += 1) {
      const [model, npc] = regularModels[i % regularModels.length];
      enemySlots.push(engineSlot(mapId, `${model}_${String(i).padStart(4, '0')}`, model, npc, index += 1));
    }
  }
  const bossRow = (mapId, model, npc, size) => engineSlot(mapId, `${model}_0000`, model, npc, index += 1, { entityId: 1000000 + index, hitHeight: size, hitRadius: size / 3, baseHp: 3000, thinkParamId: npc + 1 });
  const bossSlots = [
    bossRow('m10_01_00_00', 'c2240', 224000, 2.8), bossRow('m10_01_00_00', 'c2250', 225000, 3.5), bossRow('m15_00_00_00', 'c2320', 232000, 6),
    bossRow('m14_01_00_00', 'c5250', 525000, 10), bossRow('m12_01_00_00', 'c4510', 451000, 10.9), bossRow('m18_00_00_00', 'c5370', 537000, 2.4),
  ];
  return {
    schemaVersion: 17, maps: ['m10_00_00_00', 'm11_00_00_00', 'm12_00_00_00'].map((id) => ({ id, name: id })), enemySlots: [...enemySlots, ...bossSlots],
    enemyArchetypes: [{ id: 'x' }], bossSlots, worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

test('the engine places bosses in regular slots with the boss stats, a share of its souls, the respawn decision and a wake-up', () => {
  const base = { ...defaultConfig, randomizeEnemies: true, randomizeBosses: false, randomizeItems: false, generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1', enemyPlacementFit: 'anywhere' };
  const SOULS = new Map([[224000, 6000], [225000, 3000], [232000, 40000]]);
  const seen = new Set();
  for (let seed = 0; seed < 30; seed += 1) {
    for (const [souls, stayDead] of [[50, false], [80, true], [0, false]]) {
      const result = generate({ ...base, seed: `eng-${seed}`, enemyNormalReplacement: 'bosses', enemyRoamingBossSouls: souls, enemyRoamingBossesStayDead: stayDead }, { gameCatalog: engineCatalog() });
      const roaming = result.placements.enemies.filter((entry) => entry.compatibility === 'sampled-roaming-boss');
      assert.ok(roaming.length > 0);
      for (const entry of roaming) {
        const npc = Number(entry.to.match(/NPC (\d+)/u)[1]);
        assert.ok(SOULS.has(npc), `${entry.targetModelName} is a roaming boss (never the arena-bound or multi-body ones)`);
        assert.equal(entry.scaling, 'vanilla', 'the boss keeps its own stats');
        assert.equal(entry.roamingBossSouls, Math.trunc(SOULS.get(npc) * souls / 100));
        assert.equal(entry.disableRespawn === true, stayDead);
        assert.ok(Number.isInteger(entry.scaledNpcParamId), 'a scaled row: drops and team follow the slot');
        assert.ok(entry.forceCombatActivation === true || entry.entityId < 0, 'the wake-up event is requested');
        seen.add(entry.targetModelName);
      }
      assert.ok(roaming.every((entry) => entry.slot.split(':')[0] !== 'm10_01_00_00'), 'boss slots are not regular slots');
    }
  }
  assert.deepEqual([...seen].sort(), ['c2240', 'c2250', 'c2320']);
});

test('without roaming modes the engine output is the same as before: the new options change nothing', () => {
  const base = { ...defaultConfig, seed: 'same', randomizeEnemies: true, randomizeBosses: true, randomizeItems: false, generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1' };
  const hashOf = (config) => JSON.stringify(generate(config, { gameCatalog: engineCatalog() }).placements);
  assert.equal(hashOf({ ...base, enemyNormalReplacement: 'normals' }), hashOf({ ...base, enemyNormalReplacement: 'normals', enemyBossChanceNormalSlots: 10, enemyRoamingBossSouls: 50, enemyRoamingBossesStayDead: false }));
  const none = generate({ ...base, enemyNormalReplacement: 'none' }, { gameCatalog: engineCatalog() }).placements.enemies;
  assert.ok(none.every((entry) => entry.compatibility !== 'sampled-roaming-boss'));
});
