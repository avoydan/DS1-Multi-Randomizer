import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import test from 'node:test';
import { loadCombinedRandomizerCore } from '../../dist/adapters/combinedRandomizerCore.js';

// "Replace regular enemies with: Bosses only" (docs/ENEMY_PLACEMENT_MODEL.md, 10.7 and "Bosses only audit"). Every ordinary destination selected for replacement receives a boss,
// there is no limit on the number of bosses of a map, no ordinary enemy takes the place of a boss, and every destination that stays as it is has a counted reason.
// Engine-level tests on synthetic catalogs (extracted game data is never committed; the real-catalog numbers are in the audit document and in roamingSweep.mjs).

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;

const MODELS = [['c1000', 100000], ['c1010', 101000], ['c1020', 102000], ['c1030', 103000], ['c1040', 104000], ['c1050', 105000]];
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

/** `sizes`: the number of ordinary slots per map. The Asylum (m18_01) and a Titanite Demon group (a dragon-class ordinary enemy) are part of the catalog. */
function catalog(sizes = { m10_00_00_00: 160, m11_00_00_00: 40, m14_00_00_00: 220 }) {
  const enemySlots = [];
  for (const [mapId, count] of Object.entries(sizes)) {
    for (let i = 0; i < count; i += 1) {
      const [model, npc] = MODELS[i % MODELS.length];
      enemySlots.push(slot(mapId, `${model}_${String(i).padStart(4, '0')}`, model, npc));
    }
  }
  for (let i = 0; i < 30; i += 1) {
    const [model, npc] = MODELS[i % 3];
    enemySlots.push(slot('m18_01_00_00', `${model}_${String(i).padStart(4, '0')}`, model, npc));
  }
  // Two Titanite Demons (a dragon-class ordinary enemy the dragon plan permutes among themselves) in a map of their own.
  for (let i = 0; i < 2; i += 1) enemySlots.push(slot('m15_00_00_00', `c2300_${String(i).padStart(4, '0')}`, 'c2300', 230000 + i * 10, { hitHeight: 4, hitRadius: 1.2, baseHp: 900 }));
  // Three Drakes (another dragon-class ordinary enemy) so that the dragon plan has something to exchange the Titanite Demons with.
  for (let i = 0; i < 4; i += 1) enemySlots.push(slot('m16_00_00_00', `c3520_${String(i).padStart(4, '0')}`, 'c3520', 352000 + i * 10, { hitHeight: 5, hitRadius: 1.5, baseHp: 1200 }));
  for (let i = 0; i < 8; i += 1) enemySlots.push(slot('m15_00_00_00', `c1000_${String(i).padStart(4, '0')}`, 'c1000', 100000));
  const bossRow = (mapId, model, npc, size) => slot(mapId, `${model}_0000`, model, npc, { entityId: 1000000 + npc, hitHeight: size, hitRadius: size / 3, baseHp: 3000, thinkParamId: npc + 1 });
  const bossSlots = [
    bossRow('m18_00_00_00', 'c2230', 223000, 3), bossRow('m10_01_00_00', 'c2240', 224000, 2.8), bossRow('m10_01_00_00', 'c2250', 225000, 3.5),
    bossRow('m15_00_00_00', 'c2320', 232000, 6), bossRow('m14_00_00_00', 'c3230', 323000, 2), bossRow('m13_00_00_00', 'c3320', 332000, 2),
  ];
  const maps = [...new Set([...enemySlots, ...bossSlots].map((entry) => entry.mapId))];
  return { schemaVersion: 17, maps: maps.map((id) => ({ id, name: id })), enemySlots: [...enemySlots, ...bossSlots], enemyArchetypes: [{ id: 'x' }], bossSlots, worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [] };
}

/** A crowded map with many ordinary archetypes, shaped to exercise the real 30-distinct-model budget. */
function crowdedCatalog() {
  const enemySlots = [];
  const targetModels = Array.from({ length: 20 }, (_, index) => [`c${String(1000 + index * 10).padStart(4, '0')}`, 100000 + index * 100]);
  const donorModels = Array.from({ length: 20 }, (_, index) => [`c${String(3000 + index * 10).padStart(4, '0')}`, 300000 + index * 100]);
  for (const [model, npc] of targetModels) {
    for (let index = 0; index < 4; index += 1) enemySlots.push(slot('m10_00_00_00', `${model}_${index}`, model, npc));
  }
  // These catalog archetypes are available as replacements but do not inflate the target map's vanilla model set.
  for (const [model, npc] of donorModels) enemySlots.push(slot('m11_00_00_00', `${model}_0000`, model, npc));
  const bossRow = (mapId, model, npc, size) => slot(mapId, `${model}_0000`, model, npc, { entityId: 1000000 + npc, hitHeight: size, hitRadius: size / 3, baseHp: 3000, thinkParamId: npc + 1 });
  const bossSlots = [
    bossRow('m18_00_00_00', 'c2230', 223000, 3), bossRow('m10_01_00_00', 'c2240', 224000, 2.8), bossRow('m10_01_00_00', 'c2250', 225000, 3.5),
    bossRow('m15_00_00_00', 'c2320', 232000, 6), bossRow('m14_00_00_00', 'c3230', 323000, 2), bossRow('m13_00_00_00', 'c3320', 332000, 2),
  ];
  const maps = [...new Set([...enemySlots, ...bossSlots].map((entry) => entry.mapId))];
  return { schemaVersion: 17, maps: maps.map((id) => ({ id, name: id })), enemySlots: [...enemySlots, ...bossSlots], enemyArchetypes: [{ id: 'x' }], bossSlots, worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [] };
}
const data = catalog();

function ownerApprovedFitCatalog() {
  const destinations = [
    ['m10_00_00_00', 0, 0.8, 0.3],
    ['m10_01_00_00', 1, 1.2, 0.5],
    ['m11_00_00_00', 2, 1.8, 0.5],
    ['m12_00_00_00', 3, 2.8, 0.6],
    ['m13_00_00_00', 4, 4.4, 1],
    ['m14_00_00_00', 5, 7.5, 1],
  ].map(([mapId, sizeClass, hitHeight, hitRadius]) => ({
    ...slot(mapId, `c${1200 + sizeClass * 10}_0000`, `c${1200 + sizeClass * 10}`, 120000 + sizeClass * 100),
    hitHeight,
    hitRadius,
    expectedSizeClass: sizeClass,
  }));
  const sourceRows = [
    ['m18_00_00_00', 'c2230_0000', 'c2230', 223000, 7.5, 1, 0],
    ['m14_01_00_00', 'c2231_0000', 'c2231', 223100, 4.4, 1, 0],
    ['m18_00_00_00', 'c2232_0000', 'c2232', 223200, 7.5, 1, 0],
    ['m10_01_00_00', 'c2240_0000', 'c2240', 224000, 2.8, 0.6, 0],
    ['m10_01_00_00', 'c2250_0000', 'c2250', 225000, 3.5, 0.6, 0],
    ['m15_00_00_00', 'c2320_0000', 'c2320', 232000, 6, 2, 0],
    ['m15_01_00_00', 'c2360_0000', 'c2360', 236000, 2.8, 0.6, 0],
    ['m15_01_00_00', 'c2360_0001', 'c2360', 236001, 2.8, 0.6, 0],
    ['m14_00_00_00', 'c3230_0000', 'c3230', 323000, 0.4, 0.19, 0],
    ['m13_00_00_00', 'c3320_0000', 'c3320', 332000, 2, 0.6, 0],
    ['m15_01_00_00', 'c4100_0000', 'c4100', 410000, 2.6, 0.6, 0],
    ['m15_01_00_00', 'c4500_0000', 'c4500', 450000, 4, 0.8, 0],
    ['m15_01_00_00', 'c5210_0000', 'c5210', 521000, 4.4, 1, 0],
    ['m15_01_00_00', 'c5220_0000', 'c5220', 522000, 4.4, 1, 0],
    ['m15_01_00_00', 'c5270_0000', 'c5270', 527000, 2.8, 0.6, 0],
    ['m15_01_00_00', 'c5271_0000', 'c5271', 527100, 4.4, 1, 0],
    ['m15_01_00_00', 'c5280_0000', 'c5280', 528000, 2.8, 0.6, 0],
    ['m15_01_00_00', 'c5320_0000', 'c5320', 532000, 2.5, 0.5, 2],
    // These remain explicitly unsupported; they are present to guard the pool boundary.
    ['m14_01_00_00', 'c5230_0000', 'c5230', 523000, 1, 0.5, 0],
    ['m14_01_00_00', 'c5250_0000', 'c5250', 525000, 8, 2, 0],
    ['m18_00_00_00', 'c5370_0000', 'c5370', 537000, 2.6, 0.6, 0],
    ['m10_01_00_00', 'c5350_0000', 'c5350', 535000, 2.8, 0.6, 0],
  ].map(([mapId, name, model, npc, hitHeight, hitRadius, teamType]) => ({
    ...slot(mapId, name, model, npc, { hitHeight, hitRadius, teamType, entityId: 1_500_000 + npc, hasEntityId: true }),
  }));
  const enemySlots = [...destinations, ...sourceRows];
  const maps = [...new Set(enemySlots.map((entry) => entry.mapId))].map((id) => ({ id, name: id }));
  return {
    schemaVersion: 17,
    maps,
    enemySlots,
    enemyArchetypes: [{ id: 'synthetic' }],
    bossSlots: sourceRows,
    worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

const base = (overrides = {}) => ({
  ...defaultConfig, seed: 'bosses-only', randomizeEnemies: true, randomizeBosses: false, randomizeItems: false, generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1',
  placementDiagnosticsLevel: 'summary', enemyNormalReplacement: 'bosses', enemyReplacementChance: 100, enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'random', ...overrides,
});
const run = (overrides, gameCatalog = data) => generate(base(overrides), { gameCatalog });
const roamingOf = (plan) => plan.placements.enemies.filter((entry) => entry.compatibility === 'sampled-roaming-boss');
const sampledNormals = (plan) => plan.placements.enemies.filter((entry) => /^sampled/u.test(entry.compatibility) && entry.compatibility !== 'sampled-roaming-boss');
const roamingStats = (plan) => plan.placementDiagnostics.summary?.roaming ?? plan.placementDiagnostics.toJSON().summary.roaming;
const sum = (stats, key) => Object.values(stats).reduce((total, entry) => total + entry[key], 0);
const hash = (value) => createHash('sha256').update(JSON.stringify(value)).digest('hex');

test('owner-approved roaming fit minima apply to the real candidate pipeline without changing other classes', () => {
  const gameCatalog = ownerApprovedFitCatalog();
  const destinationClasses = [0, 1, 2, 3, 4, 5];
  const expectedFittingCounts = [4, 4, 5, 11, 16, 18];
  const destinations = gameCatalog.enemySlots.filter((entry) => entry.expectedSizeClass !== undefined);
  const outputs = new Map([false, true].map((pinwheelChaos) => [
    pinwheelChaos,
    new Map(destinationClasses.map((sizeClass) => [sizeClass, new Set()])),
  ]));
  const allPlaced = new Set();
  let gwyndolinPlacement;

  for (const pinwheelChaos of [false, true]) {
    for (let index = 0; index < 80; index += 1) {
      const config = base({
        seed: `owner-fit-${pinwheelChaos ? 'chaos' : 'plain'}-${index}`,
        randomizeEnemies: true,
        randomizeBosses: false,
        randomizeItems: false,
        enemyPlacementAlgorithm: 'sampling-v1',
        placementDiagnosticsLevel: 'summary',
        enemyNormalReplacement: 'bosses',
        enemyReplacementChance: 100,
        enemyPlacementFit: 'fits',
        enemyDifficultyMode: 'random',
        enemyTypeReplacement: 'disabled',
        preventSameEnemy: false,
        pinwheelChaos,
      });
      const result = generate(config, { gameCatalog });
      const stats = result.placementDiagnostics.summary?.roaming ?? result.placementDiagnostics.toJSON().summary.roaming;
      for (const destination of destinations) {
        const sizeClass = destination.expectedSizeClass;
        const fitting = stats[destination.mapId].fitCandidateAnalysis[sizeClass].fittingBossArchetypes;
        assert.equal(fitting, expectedFittingCounts[sizeClass], `class ${sizeClass}, Pinwheel Chaos ${pinwheelChaos}`);
        const placement = result.placements.enemies.find((entry) => entry.slot === destination.id && entry.compatibility === 'sampled-roaming-boss');
        assert.ok(placement, `${destination.id} receives a fitting boss`);
        const identity = `${placement.targetModelName}:${placement.targetNpcParamId}`;
        outputs.get(pinwheelChaos).get(sizeClass).add(identity);
        allPlaced.add(identity);
        if (identity === 'c5320:532000') gwyndolinPlacement = placement;
      }
    }
  }

  const classZeroExpected = new Set(['c2240:224000', 'c3320:332000', 'c4100:410000', 'c5320:532000']);
  for (const pinwheelChaos of [false, true]) {
    assert.deepEqual(outputs.get(pinwheelChaos).get(0), classZeroExpected, `class 0 fit pool with Pinwheel Chaos ${pinwheelChaos}`);
    assert.deepEqual(outputs.get(pinwheelChaos).get(1), classZeroExpected, `class 1 fit pool with Pinwheel Chaos ${pinwheelChaos}`);
    assert.ok(outputs.get(pinwheelChaos).get(2).has('c2250:225000'), `Taurus Demon is accepted from class 2 with Pinwheel Chaos ${pinwheelChaos}`);
    assert.equal(outputs.get(pinwheelChaos).get(0).has('c2250:225000'), false, 'Taurus Demon is rejected in class 0');
    assert.equal(outputs.get(pinwheelChaos).get(1).has('c2250:225000'), false, 'Taurus Demon is rejected in class 1');
    assert.ok(outputs.get(pinwheelChaos).get(2).has('c3320:332000'), `Pinwheel remains available with Pinwheel Chaos ${pinwheelChaos}`);
  }
  for (const unsupported of ['c5230:523000', 'c5250:525000', 'c5350:535000', 'c5370:537000']) {
    assert.equal(allPlaced.has(unsupported), false, `${unsupported} remains outside the roaming pool`);
  }
  assert.ok(allPlaced.has('c3230:323000'), 'Moonlight Butterfly remains available only in its authored class-3+ range');
  assert.ok(gwyndolinPlacement, 'Gwyndolin is selected as a roaming candidate');
  assert.equal(gwyndolinPlacement.initialTeamType, 0, 'the neutral source team is converted to the destination hostile team');
  assert.equal(gwyndolinPlacement.forceCombatActivation, true, 'the generic roaming wake-up path is used');
  assert.equal(Number.isInteger(gwyndolinPlacement.scaledNpcParamId), true, 'a slot-owned NpcParam row is requested for the team override');
  assert.equal(gwyndolinPlacement.roamingBossSouls, 20_000, 'the existing boss-souls scaling path is used');
});

test('A. 100% + Bosses only + Anywhere + Easy Asylum OFF: every eligible destination gets a boss, nothing is replaced by an ordinary enemy, nothing is capped', () => {
  const plan = run({});
  const stats = roamingStats(plan);
  assert.equal(sampledNormals(plan).length, 0, 'zero ordinary-enemy replacements');
  for (const [mapId, entry] of Object.entries(stats)) {
    assert.equal(entry.notParticipating, 0, mapId);
    assert.equal(entry.selected, entry.eligible, `${mapId}: every eligible destination is selected at 100%`);
    assert.equal(entry.boss, entry.selected, `${mapId}: every selected destination gets a boss`);
    assert.deepEqual(Object.values(entry.unchanged).filter(Boolean), [], `${mapId}: nothing stays unchanged for a reason`);
    assert.equal(roamingOf(plan).filter((placement) => placement.map === mapId).length, entry.boss);
  }
  assert.ok(stats.m14_00_00_00.boss >= 200, 'a map with 220 destinations holds more than 200 roaming bosses');
  assert.ok(stats.m18_01_00_00.boss === 30, 'with Easy Asylum off the Asylum takes bosses like any other map');
  assert.ok(roamingOf(plan).length >= 450);
});

test('B. Easy Asylum ON: only the Asylum differs, its destinations stay as they are and say why, and no ordinary enemy takes their place', () => {
  const off = run({});
  for (const mode of ['random-easy-asylum', 'curve-easy-asylum']) {
    const on = run({ enemyDifficultyMode: mode, ...(mode.startsWith('curve') ? { enemyDifficultyStrictness: 'very-loose' } : {}) });
    const stats = roamingStats(on);
    assert.equal(sampledNormals(on).length, 0, mode);
    assert.equal(stats.m18_01_00_00.boss, 0, `${mode}: no boss in the Asylum`);
    assert.equal(stats.m18_01_00_00.unchanged.easyAsylum, 30, `${mode}: the reason is counted`);
    assert.equal(on.placements.enemies.filter((entry) => entry.map === 'm18_01_00_00').length, 0, `${mode}: the Asylum keeps every vanilla enemy`);
    if (mode === 'random-easy-asylum') {
      // The restriction does not leak: every other map is the same world as with Easy Asylum off.
      const other = (plan) => plan.placements.enemies.filter((entry) => entry.map !== 'm18_01_00_00');
      assert.deepEqual(other(on), other(off));
    }
  }
});

test('C. 50% + Bosses only: the replacement chance decides which destinations take part; every one that does gets a boss', () => {
  const plan = run({ enemyReplacementChance: 50 });
  const stats = roamingStats(plan);
  const eligible = sum(stats, 'eligible');
  const selected = sum(stats, 'selected');
  assert.ok(selected > eligible * 0.42 && selected < eligible * 0.58, `about half take part (${selected} of ${eligible})`);
  assert.equal(sum(stats, 'boss'), selected);
  assert.equal(sum(stats, 'notParticipating'), eligible - selected);
  assert.equal(sampledNormals(plan).length, 0);
  assert.equal(roamingOf(plan).length, selected);
  // Independent of the other options: the same participation with another boss pool size.
  const again = run({ enemyReplacementChance: 50, enemyRoamingBossSouls: 10, enemyRoamingBossesStayDead: true });
  assert.equal(sum(roamingStats(again), 'selected'), selected);
});

test('D. Bosses or normal enemies at 0% boss chance: selected destinations get ordinary enemies only', () => {
  const plan = run({ enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 0 });
  assert.equal(roamingOf(plan).length, 0);
  assert.ok(sampledNormals(plan).length > 400);
  // Same ordinary replacements as "Normal enemies only": the roaming pass added nothing.
  const normals = run({ enemyNormalReplacement: 'normals' });
  assert.deepEqual(plan.placements.enemies, normals.placements.enemies);
});

test('E. Bosses or normal enemies at 100% boss chance: the same destinations get bosses as with Bosses only, and no ordinary replacement', () => {
  const both = run({ enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 100 });
  const only = run({});
  const slotsOf = (plan) => roamingOf(plan).map((entry) => entry.slot).sort();
  assert.deepEqual(slotsOf(both), slotsOf(only));
  assert.equal(sampledNormals(both).length, 0);
});

test('E2. Both + 100% boss chance preserves boss coverage when the normal sampler would fill the 30-model budget', () => {
  const crowded = crowdedCatalog();
  const seed = 'bosses-only-model-budget-regression';
  const both = run({ seed, enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 100 }, crowded);
  const legacyBoth = run({ seed, enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 100, enemyRoamingBossDestinationPolicy: 'protected-v1' }, crowded);
  const only = run({ seed }, crowded);
  const bothStats = roamingStats(both).m10_00_00_00;
  const legacyStats = roamingStats(legacyBoth).m10_00_00_00;
  const onlyStats = roamingStats(only).m10_00_00_00;
  const slotsOf = (plan) => roamingOf(plan).filter((entry) => entry.map === 'm10_00_00_00').map((entry) => entry.slot).sort();
  assert.ok(legacyStats.boss < legacyStats.eligible, 'the frozen v1 normal prepass reproduces the model-budget regression');
  assert.ok(legacyStats.unchanged.modelBudget > 0);
  assert.equal(onlyStats.boss, onlyStats.eligible, 'Bosses only has room for the boss pool on the vanilla model set');
  assert.equal(bothStats.boss, bothStats.eligible, 'a discarded normal draw must not consume boss model headroom');
  assert.equal(bothStats.unchanged.modelBudget, 0);
  assert.deepEqual(slotsOf(both), slotsOf(only));
  assert.equal(sampledNormals(both).filter((entry) => entry.map === 'm10_00_00_00').length, 0);
});

test('F. Normal enemies only at 100%: no boss anywhere', () => {
  const plan = run({ enemyNormalReplacement: 'normals' });
  assert.equal(roamingOf(plan).length, 0);
  assert.ok(sampledNormals(plan).length > 400);
});

test('G. Bosses only with a restrictive size fit: owner-approved class-0 bosses fill the smallest slots', () => {
  const small = catalog({ m10_00_00_00: 80 });
  for (const entry of small.enemySlots) if (entry.mapId === 'm10_00_00_00') Object.assign(entry, { hitHeight: 0.4, hitRadius: 0.15 });
  const plan = run({ enemyPlacementFit: 'fits' }, small);
  const stats = roamingStats(plan);
  assert.equal(sampledNormals(plan).length, 0);
  assert.equal(stats.m10_00_00_00.boss + stats.m10_00_00_00.unchanged.fit, stats.m10_00_00_00.selected);
  assert.equal(stats.m10_00_00_00.unchanged.fit, 0, 'the owner-approved class-0 candidates fit every smallest slot');
  assert.equal(stats.m10_00_00_00.boss, stats.m10_00_00_00.eligible);
  const placed = new Set(plan.placements.enemies.map((entry) => entry.slot));
  assert.equal(small.enemySlots.filter((entry) => entry.mapId === 'm10_00_00_00' && !placed.has(entry.id)).length, stats.m10_00_00_00.unchanged.fit, 'those destinations have no placement at all: they keep their own enemy');
});

test('roaming bosses use the existing event, ghost-grounding and Darkroot-path handling', () => {
  const game = catalog();
  const eventLocked = game.enemySlots.find((entry) => entry.mapId === 'm10_00_00_00');
  eventLocked.eventModelLocked = true;
  const ghost = slot('m16_00_00_00', 'c2670_9000', 'c2670', 267000);
  const groundAnchor = slot('m16_00_00_00', 'c2390_9000', 'c2390', 239000);
  const butterflyPath = slot('m12_00_00_00', 'c2330_9000', 'c2330', 233000);
  const asylumPassive = slot('m18_01_00_00', 'c2500_0000', 'c2500', 250000);
  const newLondoPassive = Array.from({ length: 15 }, (_, index) => slot('m16_00_00_00', `c2500_${String(index).padStart(4, '0')}`, 'c2500', 250000));
  const kilnPassive = slot('m18_00_00_00', 'c2790_0002', 'c2790', 279000);
  const parishPassive = slot('m10_01_00_00', 'c2300_0000', 'c2300', 230000, { hitHeight: 4, hitRadius: 1.2, baseHp: 900 });
  game.enemySlots.push(ghost, groundAnchor, butterflyPath, asylumPassive, ...newLondoPassive, kilnPassive, parishPassive);
  game.maps = [...new Set([...game.maps.map((entry) => entry.id), ghost.mapId, butterflyPath.mapId])].map((id) => ({ id, name: id }));

  const plan = run({ seed: 'bosses-only-safe-handlers' }, game);
  const placementBySlot = new Map(plan.placements.enemies.map((entry) => [entry.slot, entry]));
  for (const target of [eventLocked, ghost, butterflyPath, asylumPassive, ...newLondoPassive, kilnPassive, parishPassive]) {
    assert.equal(placementBySlot.get(target.id)?.compatibility, 'sampled-roaming-boss', target.id);
  }
  const ghostPlacement = placementBySlot.get(ghost.id);
  assert.equal(ghostPlacement.forceCombatActivation, true);
  assert.equal(ghostPlacement.targetCollisionName, groundAnchor.collisionName);
  assert.ok(Number.isFinite(ghostPlacement.groundY), 'the displaced ghost is grounded from a nearby wall-passable spawn point');
  const butterflyPlacement = placementBySlot.get(butterflyPath.id);
  assert.equal(butterflyPlacement.forceCombatActivation, true);
  assert.equal(butterflyPlacement.preserveDestinationPerception, false);
  const protectedReasons = Object.values(roamingStats(plan)).flatMap((entry) => Object.keys(entry.protectedByReason ?? {}));
  assert.deepEqual(protectedReasons, [], 'all four former guard categories now use their dedicated safe handlers');
  for (const target of [asylumPassive, ...newLondoPassive, kilnPassive, parishPassive]) {
    assert.equal(placementBySlot.get(target.id).passiveUntilAttacked, true, target.id);
  }
  assert.equal(placementBySlot.get(parishPassive.id).holdAiUntilAttacked, true, 'the Parish bonfire Titanite Demon keeps AI held until the player attacks');
});

test('stress: 20, 50 and 100+ boss instances in one map, repeated archetypes, deterministic, the model count within the budget, ids unique', () => {
  for (const [count, minimum] of [[24, 20], [60, 50], [150, 100], [260, 200]]) {
    const world = catalog({ m10_00_00_00: count });
    const plan = run({ enemyDifficultyMode: 'random' }, world);
    const stats = roamingStats(plan).m10_00_00_00;
    assert.ok(stats.boss >= minimum, `${count} destinations: ${stats.boss} roaming bosses`);
    assert.equal(stats.boss, stats.eligible, 'every eligible destination');
    const models = new Set(roamingOf(plan).filter((entry) => entry.map === 'm10_00_00_00').map((entry) => entry.targetModelName));
    assert.ok(models.size <= 6, `at most the six boss archetypes (${models.size})`);
    assert.ok(stats.distinctBossModels < stats.boss / 3, 'many instances, few distinct models');
    assert.ok(stats.finalModels <= stats.modelLimit, 'the model budget counts distinct models');
    const entityIds = plan.placements.enemies.filter((entry) => Number.isInteger(entry.entityId) && entry.entityId >= 0).map((entry) => `${entry.map}:${entry.entityId}`);
    // A slot that already had an entity id keeps it, so the same id may name the same vanilla entity; the generated ones (16099000 + index) must be unique.
    const generated = roamingOf(plan).filter((entry) => entry.entityId >= 16_099_000 && entry.entityId < 16_100_000).map((entry) => `${entry.map}:${entry.entityId}`);
    assert.equal(new Set(generated).size, generated.length, 'generated entity ids are unique');
    const rows = roamingOf(plan).map((entry) => entry.scaledNpcParamId);
    assert.equal(new Set(rows).size, rows.length, 'scaled NpcParam rows are unique');
    assert.ok(entityIds.length >= 0);
    assert.equal(hash(plan.placements), hash(run({ enemyDifficultyMode: 'random' }, world).placements), 'deterministic');
  }
});

test('stay dead ON and OFF, souls percent: the same bosses in the same destinations, only the per-placement values differ', () => {
  const dead = run({ enemyRoamingBossesStayDead: true, enemyRoamingBossSouls: 50 });
  const alive = run({ enemyRoamingBossesStayDead: false, enemyRoamingBossSouls: 50 });
  assert.deepEqual(roamingOf(dead).map((entry) => [entry.slot, entry.targetModelName]), roamingOf(alive).map((entry) => [entry.slot, entry.targetModelName]));
  assert.ok(roamingOf(dead).every((entry) => entry.disableRespawn === true));
  assert.ok(roamingOf(alive).every((entry) => entry.disableRespawn !== true));
  const SOULS = new Map([[223000, 20000], [224000, 6000], [225000, 3000], [232000, 40000], [323000, 10000], [332000, 15000]]);
  for (const entry of roamingOf(dead)) {
    const npc = Number(entry.to.match(/NPC (\d+)/u)[1]);
    assert.equal(entry.roamingBossSouls, Math.trunc(SOULS.get(npc) / 2), 'the souls of a roaming boss are the percent of its own fight, whatever the density');
    assert.equal(entry.scaling, 'vanilla');
    assert.ok(!('awardItemLotId' in entry) && !entry.bossBar, 'a roaming boss carries no encounter reward, boss bar or arena logic');
  }
});

test('dragon-class ordinary enemies (Titanite Demons) take a boss like any ordinary enemy, and are not exchanged for another ordinary enemy by the dragon plan', () => {
  const only = run({ randomizeBosses: true, enemyBossReplacement: 'classic' });
  const titanite = data.enemySlots.filter((entry) => entry.modelName === 'c2300' || entry.modelName === 'c3520').map((entry) => entry.id);
  for (const id of titanite) {
    const placements = only.placements.enemies.filter((entry) => entry.slot === id);
    assert.equal(placements.length, 1, id);
    assert.equal(placements[0].compatibility, 'sampled-roaming-boss', `${id} receives a boss`);
  }
  assert.equal(only.placements.enemies.filter((entry) => entry.compatibility === 'dragon-only-group-permutation' && titanite.includes(entry.slot)).length, 0);
  // With ordinary enemies the dragon plan keeps doing what it did (and with 0% boss chance nothing changes).
  const normals = run({ enemyNormalReplacement: 'normals', randomizeBosses: true, enemyBossReplacement: 'classic' });
  assert.ok(normals.placements.enemies.some((entry) => entry.compatibility === 'dragon-only-group-permutation' && titanite.includes(entry.slot)));
  const zero = run({ enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 0, randomizeBosses: true, enemyBossReplacement: 'classic' });
  assert.deepEqual(zero.placements.enemies, normals.placements.enemies);
});

test('every destination that stays as it is has a counted reason: the diagnostics add up in every map, for several configurations', () => {
  for (const overrides of [{}, { enemyReplacementChance: 70 }, { enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 40 }, { enemyPlacementFit: 'fits' }, { enemyDifficultyMode: 'curve-easy-asylum' }, { preventSameEnemy: true }, { enemyTypeReplacement: 'enabled' }]) {
    const plan = run(overrides);
    for (const [mapId, entry] of Object.entries(roamingStats(plan))) {
      const unchanged = Object.values(entry.unchanged).reduce((total, value) => total + value, 0);
      assert.equal(entry.boss + entry.normalDraw + unchanged, entry.selected, `${mapId} ${JSON.stringify(overrides)}`);
      if (overrides.enemyTypeReplacement !== 'enabled') assert.equal(entry.notParticipating + entry.selected, entry.eligible, `${mapId}: every eligible destination is either not selected or selected`);
      assert.equal(entry.unchanged.eventLimit, 0, 'no event limit exists');
    }
    if (overrides.enemyNormalReplacement !== 'both') assert.equal(sampledNormals(plan).length, 0, JSON.stringify(overrides));
  }
});
