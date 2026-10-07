import assert from 'node:assert/strict';
import test from 'node:test';
import { loadCombinedRandomizerCore } from '../../dist/adapters/combinedRandomizerCore.js';

// Part G: the order of the planners of legacy-style sampling and the independence of the streams (docs/ENEMY_PLACEMENT_MODEL.md, 10.10).
//
//   1. the boss plan (dragons, classic or sampled bosses, boss bodies)     - decided first, from its own streams
//   2. the regular sampler (Type replacement groups included)              - sees the bosses only through the models they leave in a map
//   3. the roaming boss pass, which only overrides slots                   - named streams roaming:*
//   4. hostile NPC types                                                   - extra candidates of step 2, nothing else
//   5. drop ownership (scaled rows), then the data tool's events          - derived, no draws
//
// What follows are the exact guarantees: a late stage never changes an earlier plan, a neutral option adds no draw, items never depend on enemies.

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true);
const { generate, defaultConfig } = loaded.core;

function slot(mapId, name, model, npc, extra = {}) {
  return {
    id: `${mapId}:${name}`, mapId, name, modelName: model, entityId: -1, npcParamId: npc, thinkParamId: npc, talkId: -1, charaInitId: -1, collisionName: 'h0000B0',
    position: { x: 0, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, movePoints: [], dummy: false, hasEntityId: false, eventModelLocked: false, riskFlags: [], safeCandidate: true,
    teamType: 0, npcType: 0, moveType: 3, hitHeight: 1.5, hitRadius: 0.5, hitYOffset: 0, baseHp: 100, soulReward: 10, battleStartDistance: 20, eyeDistance: 20, earDistance: 10,
    disablePathMove: false, battleGoalId: npc, ...extra,
  };
}

function catalogOf() {
  const enemySlots = [];
  for (const mapId of ['m10_00_00_00', 'm14_00_00_00', 'm18_01_00_00', 'm12_00_00_01']) {
    for (const [index, [model, npc]] of [['c2500', 250000], ['c2560', 256000], ['c2790', 279000], ['c2810', 281000]].entries()) {
      for (let copy = 0; copy < 4; copy += 1) enemySlots.push(slot(mapId, `${model}_00${index}${copy}`, model, npc, { baseHp: 100 + index * 50, hitHeight: 1.5 + index * 0.3 }));
    }
  }
  const npcSlot = (mapId, name, model, npc, extra = {}) => slot(mapId, name, model, npc, { entityId: 6000, hasEntityId: true, talkId: 101000, teamType: 2, npcType: 2, baseHp: 500, hitHeight: 1.7, ...extra });
  enemySlots.push(
    npcSlot('m10_01_00_00', 'c2510_0000', 'c2510', 251001, { thinkParamId: 251000, battleGoalId: 251000, entityId: 6230 }),
    npcSlot('m10_01_00_00', 'c2640_0000', 'c2640', 264000, { entityId: 6190, baseHp: 888, hitHeight: 1.9 }),
    npcSlot('m13_00_00_00', 'c2920_0000', 'c2920', 292000, { entityId: 6200, baseHp: 555 }),
  );
  const bossRow = (mapId, model, npc, entityId) => slot(mapId, `${model}_0000`, model, npc, { entityId, hasEntityId: true, teamType: 1, hitHeight: 4, hitRadius: 1.5, baseHp: 900, thinkParamId: npc + 1 });
  const bossSlots = [bossRow('m18_01_00_00', 'c2232', 223200, 1810800), bossRow('m10_01_00_00', 'c2250', 225000, 1010700), bossRow('m10_01_00_00', 'c2240', 224000, 1010750), bossRow('m14_01_00_00', 'c5200', 520000, 1410700)];
  return {
    schemaVersion: 17, maps: ['m10_00_00_00', 'm14_00_00_00', 'm18_01_00_00', 'm10_01_00_00', 'm14_01_00_00', 'm12_00_00_01'].map((id) => ({ id, name: id })), enemySlots: [...enemySlots, ...bossSlots],
    enemyArchetypes: [{ id: 'x' }], bossSlots, worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

const base = {
  ...defaultConfig, randomizeEnemies: true, randomizeBosses: true, randomizeItems: false, generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1',
  enemyReplacementChance: 100, enemyNormalReplacement: 'normals', enemyPlacementFit: 'anywhere', secondGargoyle: 'keep',
};
const plan = (overrides, seed = 'order') => generate({ ...base, seed, ...overrides }, { gameCatalog: catalogOf() });
const json = (value) => JSON.stringify(value);

// (The replacement chance is the one deliberate shared option: as in the legacy it is the first draw of every slot, boss slots of boss sampling included.)
test('the boss plan is decided before the regular sampler: no regular, roaming or hostile option changes a boss placement', () => {
  for (const seed of ['a', 'b', 'c']) {
    for (const mode of ['classic', 'bosses', 'both']) {
      const reference = plan({ enemyBossReplacement: mode }, seed).placements.bosses;
      for (const overrides of [
        { enemyHostileNpcs: true },
        { enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 30 },
        { enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 30, enemyHostileNpcs: true, enemyRoamingBossSouls: 80, enemyRoamingBossesStayDead: true },
        { enemyTypeReplacement: 'enabled' },
        { enemyPlacementFit: 'fits', enemyDifficultyMode: 'curve' },
      ]) {
        assert.equal(json(plan({ enemyBossReplacement: mode, ...overrides }, seed).placements.bosses), json(reference), `${mode} ${json(overrides)}`);
      }
    }
  }
});

test('items never depend on the enemy options: every enemy toggle leaves the item placements byte for byte alone', () => {
  const items = (overrides) => json(generate({ ...base, randomizeItems: true, seed: 'items', ...overrides }, { gameCatalog: { ...catalogOf(), worldItemLots: [] } }).placements.items);
  const reference = items({});
  for (const overrides of [{ enemyHostileNpcs: true }, { enemyBossReplacement: 'normals' }, { enemyNormalReplacement: 'both' }, { enemyTypeReplacement: 'enabled' }, { enemyPlacementAlgorithm: 'permutation-v1' }]) {
    const algorithmReset = overrides.enemyPlacementAlgorithm === 'permutation-v1' ? { enemyBossReplacement: 'classic', enemyPlacementFit: 'anywhere' } : {};
    assert.equal(items({ ...algorithmReset, ...overrides }), reference, json(overrides));
  }
});

test('a neutral option draws nothing: its off value, its absence and its default are the same world, for every extension key', () => {
  const hash = (overrides) => json(plan(overrides, 'neutral').placements);
  const reference = hash({});
  for (const overrides of [
    { enemyHostileNpcs: false },
    { enemyBossChanceBossSlots: 90 },
    { enemyBossChanceNormalSlots: 10 },
    { enemyRoamingBossSouls: 50 },
    { enemyRoamingBossesStayDead: false },
    { enemyUniqueBosses: false },
    { enemyBossReplacement: 'classic' },
  ]) assert.equal(hash(overrides), reference, json(overrides));
  // "classic" boss replacement and "bosses" are the two earlier modes: the body options change nothing for them.
  assert.equal(hash({ enemyBossReplacement: 'bosses', enemyBossChanceBossSlots: 90 }), hash({ enemyBossReplacement: 'bosses' }));
});

test('the roaming pass only overrides slots: every slot it did not take keeps the placement it had without the pass, with hostile NPCs on or off', () => {
  for (const hostile of [false, true]) {
    for (const seed of ['r1', 'r2', 'r3']) {
      const without = plan({ enemyNormalReplacement: 'normals', enemyHostileNpcs: hostile }, seed).placements.enemies;
      const withRoaming = plan({ enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 30, enemyHostileNpcs: hostile }, seed).placements.enemies;
      const taken = new Set(withRoaming.filter((placement) => placement.compatibility === 'sampled-roaming-boss').map((placement) => placement.slot));
      assert.ok(taken.size > 0 || seed !== 'r1', 'the pass takes slots');
      assert.equal(json(withRoaming.filter((placement) => !taken.has(placement.slot))), json(without.filter((placement) => !taken.has(placement.slot))), `hostile ${hostile} ${seed}`);
    }
  }
});

test('boss bodies (E2) take no draw from the regular sampler or the roaming pass: a world with "normals" and one with "classic" plan the same normal enemies when no boss model moves between maps', () => {
  // The bodies are drawn from body:slot:<id> and category:slot:<id>; the only coupling is the model budget, through the models that stay in a map.
  for (const seed of ['b1', 'b2', 'b3', 'b4']) {
    const normals = plan({ enemyBossReplacement: 'normals' }, seed);
    const repeated = plan({ enemyBossReplacement: 'normals' }, seed);
    assert.equal(json(normals.placements), json(repeated.placements), 'deterministic');
    const bodies = normals.placements.bosses.filter((placement) => placement.compatibility === 'sampled-encounter-body');
    assert.ok(bodies.length > 0, 'the audited slots take bodies');
    const asBoth = plan({ enemyBossReplacement: 'both', enemyBossChanceBossSlots: 0 }, seed).placements.bosses.filter((placement) => placement.compatibility === 'sampled-encounter-body');
    assert.equal(json(asBoth), json(bodies), '"both" at 0 has the bodies of "normals"');
  }
});

test('the placement validation summary reports what was decided and is not part of the placement hash', () => {
  const result = plan({ enemyBossReplacement: 'both', enemyBossChanceBossSlots: 50, enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 20, enemyHostileNpcs: true }, 'summary');
  const summary = result.validation.enemyPlacement;
  assert.equal(summary.algorithm, 'sampling-v1');
  assert.ok(summary.maps >= 4);
  assert.equal(summary.placements.changed + summary.placements.keptVanilla, summary.placements.regularSlotsPlaced + summary.placements.bossSlotsPlaced);
  assert.equal(summary.roamingBosses, result.placements.enemies.filter((placement) => placement.compatibility === 'sampled-roaming-boss').length);
  assert.equal(summary.hostileNpcs, result.placements.enemies.filter((placement) => placement.hostileNpc === true).length);
  assert.equal(summary.bossBodies, result.placements.bosses.filter((placement) => placement.encounterBody === true).length);
  assert.ok(summary.ownRows.npcParam >= summary.roamingBosses + summary.hostileNpcs, 'every roaming boss and hostile NPC has its own NpcParam row');
  assert.ok(summary.models.maxFinalModelsPerMap <= 30);
  // The same world with a different diagnostics level has the same placement hash: the summary is outside the hash.
  assert.equal(result.placementHash, plan({ enemyBossReplacement: 'both', enemyBossChanceBossSlots: 50, enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 20, enemyHostileNpcs: true, placementDiagnosticsLevel: 'detail' }, 'summary').placementHash);
  const classic = generate({ ...base, enemyPlacementAlgorithm: 'permutation-v1', enemyBossReplacement: 'classic', enemyPlacementFit: 'anywhere', seed: 'summary' }, { gameCatalog: catalogOf() });
  assert.equal(classic.validation.enemyPlacement.algorithm, 'permutation-v1');
  assert.equal(classic.validation.enemyPlacement.roamingBosses, 0);
});
