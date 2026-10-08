import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';

// Unit participation (guard audit G-03 / F-01, docs/GENERATION_GUARD_AUDIT.md). The ordinary dragon-class units (Titanite Demons, Drakes), the hydras and the Mass of Souls
// groups are planned as whole units by their own planners. Under sampling-v1 a unit takes part only when "Replace regular enemies with" lets an ordinary enemy replace an
// ordinary enemy AND the first draw of its root slot is within the Replacement chance - the same draw the regular sampler and the roaming-boss pass make.
// Engine-level tests on a synthetic catalog (extracted game data is never committed); the real-catalog numbers are in the audit document and guardAuditProbe.mjs.

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const placementModule = await import(pathToFileURL(resolveCombinedRandomizerCoreModulePath('core/placement.js')).href);

let counter = 0;
function slot(mapId, name, model, npc, extra = {}) {
  counter += 1;
  return {
    id: `${mapId}:${name}`, mapId, name, modelName: model, entityId: -1, npcParamId: npc, thinkParamId: npc, talkId: -1, charaInitId: npc,
    collisionName: 'h0000B0', position: { x: counter, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, movePoints: [], dummy: false, hasEntityId: false,
    eventModelLocked: false, riskFlags: [], safeCandidate: true, teamType: 0, npcType: 0, moveType: 0, hitHeight: 1.2, hitRadius: 0.5, hitYOffset: 0,
    baseHp: 100 + (npc % 7) * 10, soulReward: 10, battleStartDistance: 20, eyeDistance: 20, earDistance: 10, disablePathMove: false, battleGoalId: npc, ...extra,
  };
}
const MODELS = [['c1000', 100000], ['c1010', 101000], ['c1020', 102000], ['c1030', 103000], ['c1040', 104000], ['c1050', 105000]];
const pad = (value) => String(value).padStart(4, '0');

/** `titanite` Titanite Demons, `drakes` Drakes, three hydras (7 heads each) and the two Mass of Souls groups, beside ordinary enemies and six boss rows. */
function catalog({ titanite = 3, drakes = 5 } = {}) {
  const enemySlots = [];
  for (const mapId of ['m10_00_00_00', 'm11_00_00_00', 'm14_00_00_00']) {
    for (let i = 0; i < 60; i += 1) {
      const [model, npc] = MODELS[i % MODELS.length];
      enemySlots.push(slot(mapId, `${model}_${pad(i)}`, model, npc));
    }
  }
  for (let i = 0; i < titanite; i += 1) enemySlots.push(slot('m15_00_00_00', `c2300_${pad(i)}`, 'c2300', 230000 + i * 10, { hitHeight: 4, hitRadius: 1.2, baseHp: 900 }));
  for (let i = 0; i < drakes; i += 1) enemySlots.push(slot('m16_00_00_00', `c3520_${pad(i)}`, 'c3520', 352000 + i * 10, { hitHeight: 5, hitRadius: 1.5, baseHp: 1200 }));
  for (const mapId of ['m12_00_00_00', 'm12_00_00_01', 'm13_02_00_00']) {
    enemySlots.push(slot(mapId, 'c3530_0000', 'c3530', 353000, { hitHeight: 6, hitRadius: 2, baseHp: 2000, entityId: 1200000 + counter }));
    for (let head = 0; head < 7; head += 1) enemySlots.push(slot(mapId, `c3531_${pad(head)}`, 'c3531', 353100 + head, { baseHp: 500 }));
  }
  for (let group = 0; group < 2; group += 1) {
    enemySlots.push(slot('m16_00_00_00', `c3500_${pad(group)}`, 'c3500', 350000, { hitHeight: 3, hitRadius: 1, baseHp: 300 }));
    for (let wisp = 0; wisp < 6; wisp += 1) enemySlots.push(slot('m16_00_00_00', `c3501_${pad(group * 6 + wisp)}`, 'c3501', 350100, { baseHp: 50 }));
  }
  const bossRow = (mapId, model, npc, size) => slot(mapId, `${model}_0000`, model, npc, { entityId: 1000000 + npc, hitHeight: size, hitRadius: size / 3, baseHp: 3000, thinkParamId: npc + 1 });
  const bossSlots = [
    bossRow('m18_00_00_00', 'c2230', 223000, 3), bossRow('m10_01_00_00', 'c2240', 224000, 2.8), bossRow('m10_01_00_00', 'c2250', 225000, 3.5),
    bossRow('m15_00_00_00', 'c2320', 232000, 6), bossRow('m14_00_00_00', 'c3230', 323000, 2), bossRow('m13_00_00_00', 'c3320', 332000, 2),
  ];
  const maps = [...new Set([...enemySlots, ...bossSlots].map((entry) => entry.mapId))];
  return { schemaVersion: 17, maps: maps.map((id) => ({ id, name: id })), enemySlots: [...enemySlots, ...bossSlots], enemyArchetypes: [{ id: 'x' }], bossSlots, worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [] };
}
const data = catalog();

const UNIT_KIND = /dragon|hydra|mass-of-souls/u;
const HYDRA_ROOTS = ['m12_00_00_00', 'm12_00_00_01', 'm13_02_00_00'].map((mapId) => `${mapId}:c3530_0000`);
const TITANITE_ROOTS = [0, 1, 2].map((i) => `m15_00_00_00:c2300_${pad(i)}`);
const DRAKE_ROOTS = [0, 1, 2, 3, 4].map((i) => `m16_00_00_00:c3520_${pad(i)}`);
const MASS_ROOTS = ['m16_00_00_00:c3500_0000', 'm16_00_00_00:c3500_0001'];
const DRAGON_ROOTS = [...TITANITE_ROOTS, ...DRAKE_ROOTS];
const ALL_ROOTS = [...DRAGON_ROOTS, ...HYDRA_ROOTS, ...MASS_ROOTS];

const run = (overrides, gameCatalog = data, seed = 'unit-participation') => generate({
  ...defaultConfig, seed, randomizeEnemies: true, randomizeBosses: false, randomizeItems: false, generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1',
  placementDiagnosticsLevel: 'summary', enemyNormalReplacement: 'normals', enemyReplacementChance: 100, enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'random', ...overrides,
}, { gameCatalog });
const unitRecords = (plan) => plan.placements.enemies.filter((entry) => UNIT_KIND.test(entry.compatibility));
const visible = (record) => record.changed === true && record.disableEntity !== true && record.syntheticEnemy !== true;
const roamingOf = (plan) => plan.placements.enemies.filter((entry) => entry.compatibility === 'sampled-roaming-boss');
const hasRecord = (plan, id) => plan.placements.enemies.some((entry) => entry.slot === id);
const drawn = (seed, root, chance) => placementModule.createSlotStream(seed, root).int(100) + 1 <= chance;

test('A. Replacement chance 0%: no unit planner emits a record, in any category mode', () => {
  for (const normalReplacement of ['normals', 'both', 'bosses', 'none']) {
    const plan = run({ enemyReplacementChance: 0, enemyNormalReplacement: normalReplacement });
    assert.deepEqual(unitRecords(plan), [], `${normalReplacement}: no unit record at 0%`);
    assert.equal(plan.placements.enemies.length, 0, `${normalReplacement}: a world at 0% changes no enemy`);
    for (const root of ALL_ROOTS) assert.equal(hasRecord(plan, root), false, `${normalReplacement}: ${root} stays as it is`);
  }
});

test('B. Replacement chance 100% takes every unit in, and the result equals the classic permutation (the unit planners are unchanged at 100%)', () => {
  const plan = run({});
  for (const root of [...DRAGON_ROOTS, ...HYDRA_ROOTS, ...MASS_ROOTS]) assert.equal(hasRecord(plan, root), true, `${root} takes part`);
  assert.ok(unitRecords(plan).some(visible), 'the exchange changes visible bodies');
  const classic = generate({ ...defaultConfig, seed: 'unit-participation', randomizeEnemies: true, randomizeBosses: false, randomizeItems: false, generateSpoilerLog: false }, { gameCatalog: data });
  assert.deepEqual(unitRecords(plan), unitRecords(classic), 'sampling at 100% plans the units exactly as the frozen permutation does');
});

test('C. 50%: the units that take part are exactly the ones whose root draw is within the chance; parts follow their root; the run is deterministic', () => {
  const seed = 'unit-participation-c';
  const plan = run({ enemyReplacementChance: 50 }, data, seed);
  const again = run({ enemyReplacementChance: 50 }, data, seed);
  assert.deepEqual(plan.placements.enemies, again.placements.enemies, 'same seed, same world');
  const expected = ALL_ROOTS.filter((root) => drawn(seed, root, 50));
  assert.ok(expected.length > 0 && expected.length < ALL_ROOTS.length, 'the seed gives a proper subset');
  const units = plan.placementDiagnostics.summary.units;
  const structural = Object.values(units).reduce((sum, family) => sum + family.rejectedStructural, 0);
  const emitted = ALL_ROOTS.filter((root) => hasRecord(plan, root));
  for (const root of emitted) assert.ok(expected.includes(root), `${root} was drawn`);
  if (structural === 0) {
    // A lone unit of a planner keeps itself; every other drawn unit has a record.
    const dragonDrawn = expected.filter((root) => DRAGON_ROOTS.includes(root) || HYDRA_ROOTS.includes(root));
    const massDrawn = expected.filter((root) => MASS_ROOTS.includes(root));
    if (dragonDrawn.length > 1) for (const root of dragonDrawn) assert.ok(hasRecord(plan, root), `${root} takes part`);
    assert.equal(massDrawn.length === 2, hasRecord(plan, MASS_ROOTS[0]) && hasRecord(plan, MASS_ROOTS[1]), 'both Mass of Souls groups exchange or none does');
  }
  // Parts follow their root: a hydra that does not take part has no record on any of its heads; one that does has a record on all seven.
  for (const root of HYDRA_ROOTS) {
    const mapId = root.split(':')[0];
    const heads = data.enemySlots.filter((entry) => entry.mapId === mapId && entry.modelName === 'c3531').map((entry) => entry.id);
    const withRecord = heads.filter((id) => hasRecord(plan, id)).length;
    assert.equal(withRecord, hasRecord(plan, root) ? 7 : 0, `${root}: the seven heads follow the body`);
  }
  // The exchanged world does not depend on anything but the seed: a different chance moves only the draws of the units.
  const other = run({ enemyReplacementChance: 50 }, data, 'unit-participation-c2');
  assert.notDeepEqual(unitRecords(other), unitRecords(plan));
});

test('D. "Nothing" and Bosses only never exchange ordinary units, and every unit is counted as not eligible by the mode', () => {
  for (const normalReplacement of ['none', 'bosses']) {
    const plan = run({ enemyNormalReplacement: normalReplacement });
    assert.deepEqual(unitRecords(plan), [], normalReplacement);
    const units = plan.placementDiagnostics.summary.units;
    assert.equal(units.dragon.logicalUnits, 8);
    assert.equal(units.dragon.notEligibleByMode, 8);
    assert.equal(units.hydra.notEligibleByMode, 3);
    assert.equal(units.massOfSouls.notEligibleByMode, 2);
    for (const family of Object.values(units)) assert.equal(family.replaced, 0);
  }
  assert.equal(run({ enemyNormalReplacement: 'none' }).placements.enemies.length, 0);
});

test('E/F. Bosses only: the dragon-class destinations without linked parts take bosses at 100%, nothing at 0%, and no ordinary enemy replaces an ordinary enemy', () => {
  const plan = run({ enemyNormalReplacement: 'bosses' });
  const roaming = roamingOf(plan);
  const bossSlots = new Set(roaming.map((entry) => entry.slot));
  for (const root of [...TITANITE_ROOTS, ...DRAKE_ROOTS]) assert.ok(bossSlots.has(root), `${root} takes a boss`);
  for (const root of [...HYDRA_ROOTS, ...MASS_ROOTS]) assert.equal(hasRecord(plan, root), false, `${root} (linked parts) stays as it is`);
  assert.equal(plan.placements.enemies.filter((entry) => /^sampled/u.test(entry.compatibility) && entry.compatibility !== 'sampled-roaming-boss').length, 0);
  assert.deepEqual(unitRecords(plan), []);
  const none = run({ enemyNormalReplacement: 'bosses', enemyReplacementChance: 0 });
  assert.equal(none.placements.enemies.length, 0);
  // 50%: a unit takes a boss exactly when its own draw says so.
  const seed = 'unit-participation-e';
  const half = run({ enemyNormalReplacement: 'bosses', enemyReplacementChance: 50 }, data, seed);
  const taken = new Set(roamingOf(half).map((entry) => entry.slot));
  for (const root of DRAGON_ROOTS) assert.equal(taken.has(root), drawn(seed, root, 50), `${root}: the roaming pass reads the same draw as the unit planner`);
});

test('G. Regular enemies only never injects a boss, at any chance', () => {
  for (const enemyReplacementChance of [100, 50]) {
    const plan = run({ enemyReplacementChance });
    assert.deepEqual(roamingOf(plan), []);
    assert.equal(plan.placements.enemies.some((entry) => entry.compatibility === 'sampled-roaming-boss'), false);
  }
});

test('H. Bosses or regular enemies: the boss chance decides the category of a unit that takes part', () => {
  const seed = 'unit-participation-h';
  const allBosses = run({ enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 100 }, data, seed);
  const bossSlots = new Set(roamingOf(allBosses).map((entry) => entry.slot));
  for (const root of [...TITANITE_ROOTS, ...DRAKE_ROOTS]) assert.ok(bossSlots.has(root), `${root} takes a boss at boss chance 100`);
  // At boss chance 0 the units are exchanged exactly as in "Regular enemies only".
  const noBosses = run({ enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 0 }, data, seed);
  assert.deepEqual(roamingOf(noBosses), []);
  assert.deepEqual(unitRecords(noBosses), unitRecords(run({}, data, seed)));
  // At 50% / 50% a unit that does not take part is never a boss and never exchanged.
  const mixed = run({ enemyNormalReplacement: 'both', enemyReplacementChance: 50, enemyBossChanceNormalSlots: 50 }, data, seed);
  const taken = new Set(roamingOf(mixed).map((entry) => entry.slot));
  for (const root of DRAGON_ROOTS) if (!drawn(seed, root, 50)) assert.ok(!taken.has(root) && !hasRecord(mixed, root), `${root} does not take part`);
});

test('I. The counters add up: units, eligible, selected, not selected, replaced, helper records', () => {
  for (const [label, overrides] of [['100', {}], ['50', { enemyReplacementChance: 50 }], ['0', { enemyReplacementChance: 0 }]]) {
    const plan = run(overrides);
    const units = plan.placementDiagnostics.summary.units;
    assert.deepEqual(Object.keys(units).sort(), ['dragon', 'hydra', 'massOfSouls'], label);
    for (const [name, family] of Object.entries(units)) {
      assert.equal(family.logicalUnits, family.eligible + family.notEligibleByMode, `${label}/${name}: eligible + not eligible by mode`);
      assert.equal(family.eligible, family.selectedByChance + family.notSelectedByChance, `${label}/${name}: selected + not selected`);
      assert.equal(family.logicalUnits, family.replaced + family.unchanged, `${label}/${name}: replaced + unchanged`);
    }
    const records = unitRecords(plan);
    const sum = (key) => Object.values(units).reduce((total, family) => total + family[key], 0);
    assert.equal(sum('visibleRecords'), records.filter(visible).length, `${label}: visible records`);
    assert.equal(sum('helperRecords'), records.filter((record) => record.disableEntity === true || record.syntheticEnemy === true).length, `${label}: helper records`);
  }
  assert.equal(run({ enemyReplacementChance: 0 }).placementDiagnostics.summary.units.dragon.replaced, 0);
  assert.equal(run({ enemyReplacementChance: 0 }).placementDiagnostics.summary.units.dragon.notSelectedByChance, 8);
});

test('J. The Titanite Demon rule holds for every subset and never throws; a Titanite Demon that cannot be placed stays and is counted', () => {
  const crowded = catalog({ titanite: 5, drakes: 2 });
  let structural = 0;
  for (let index = 0; index < 60; index += 1) {
    for (const gameCatalog of [data, crowded]) {
      const plan = run({ enemyReplacementChance: 60 }, gameCatalog, `titanite-${index}`);
      for (const record of unitRecords(plan)) {
        const native = gameCatalog.enemySlots.find((entry) => entry.id === record.slot);
        if (native?.modelName === 'c2300' && record.disableEntity !== true) assert.notEqual(record.targetModelName, 'c2300', `${record.slot}: no Titanite Demon takes a native Titanite Demon slot`);
      }
      structural += plan.placementDiagnostics.summary.units.dragon.rejectedStructural;
    }
  }
  assert.ok(structural > 0, 'the crowded catalog exercises the structural rejection');
});

test('K. The unit planners consume no stream of another system: the classic boss plan is identical at every chance', () => {
  const classic = (enemyReplacementChance) => run({ randomizeBosses: true, enemyBossReplacement: 'classic', enemyReplacementChance }).placements.bosses;
  const reference = classic(100);
  assert.ok(reference.length > 0);
  assert.deepEqual(classic(0), reference);
  assert.deepEqual(classic(50), reference);
});
