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
const data = catalog();

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

test('F. Normal enemies only at 100%: no boss anywhere', () => {
  const plan = run({ enemyNormalReplacement: 'normals' });
  assert.equal(roamingOf(plan).length, 0);
  assert.ok(sampledNormals(plan).length > 400);
});

test('G. Bosses only with a restrictive size fit: destinations a boss cannot fit stay as they are, with the reason, and never get an ordinary enemy', () => {
  const small = catalog({ m10_00_00_00: 80 });
  for (const entry of small.enemySlots) if (entry.mapId === 'm10_00_00_00') Object.assign(entry, { hitHeight: 0.4, hitRadius: 0.15 });
  const plan = run({ enemyPlacementFit: 'fits' }, small);
  const stats = roamingStats(plan);
  assert.equal(sampledNormals(plan).length, 0);
  assert.equal(stats.m10_00_00_00.boss + stats.m10_00_00_00.unchanged.fit, stats.m10_00_00_00.selected);
  assert.ok(stats.m10_00_00_00.unchanged.fit > 0, 'the smallest slots take no boss');
  const placed = new Set(plan.placements.enemies.map((entry) => entry.slot));
  assert.equal(small.enemySlots.filter((entry) => entry.mapId === 'm10_00_00_00' && !placed.has(entry.id)).length, stats.m10_00_00_00.unchanged.fit, 'those destinations have no placement at all: they keep their own enemy');
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
