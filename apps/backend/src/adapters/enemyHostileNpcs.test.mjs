import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';

// Part F1: hostile NPCs as sources of the normal enemy pool. The engine end to end on a synthetic catalog (the real catalog is not in the repository).

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const load = (file) => import(pathToFileURL(resolveCombinedRandomizerCoreModulePath(`core/${file}`)).href);
const generator = await load('generator.js');
const sampling = await load('sampling.js');

function slot(mapId, name, model, npc, extra = {}) {
  return {
    id: `${mapId}:${name}`, mapId, name, modelName: model, entityId: -1, npcParamId: npc, thinkParamId: npc, talkId: -1, charaInitId: -1, collisionName: 'h0000B0',
    position: { x: 0, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, movePoints: [], dummy: false, hasEntityId: false, eventModelLocked: false, riskFlags: [], safeCandidate: true,
    teamType: 0, npcType: 0, moveType: 3, hitHeight: 1.5, hitRadius: 0.5, hitYOffset: 0, baseHp: 100, soulReward: 10, battleStartDistance: 20, eyeDistance: 20, earDistance: 10,
    disablePathMove: false, battleGoalId: npc, ...extra,
  };
}
const npcSlot = (mapId, name, model, npc, extra = {}) => slot(mapId, name, model, npc, {
  entityId: 6000, hasEntityId: true, talkId: 101000, teamType: 2, npcType: 2, riskFlags: ['entity-id', 'talk', 'friendly-or-neutral'], baseHp: 500, hitHeight: 1.7, ...extra,
});

function catalogOf({ regularPerMap = 3, extraRegular = [] } = {}) {
  const enemySlots = [];
  for (const mapId of ['m10_00_00_00', 'm14_00_00_00', 'm18_01_00_00', 'm12_00_00_01']) {
    for (const [index, [model, npc]] of [['c2500', 250000], ['c2560', 256000], ['c2790', 279000], ['c2810', 281000]].entries()) {
      for (let copy = 0; copy < regularPerMap; copy += 1) enemySlots.push(slot(mapId, `${model}_00${index}${copy}`, model, npc, { baseHp: 100 + index * 50, hitHeight: 1.5 + index * 0.3 }));
    }
  }
  // The NPC slots of the real catalog that matter: the three authored sources, the ones left out, and the services that are never touched.
  enemySlots.push(
    npcSlot('m10_01_00_00', 'c2510_0000', 'c2510', 251001, { thinkParamId: 251000, battleGoalId: 251000, entityId: 6230 }),
    npcSlot('m10_02_00_00', 'c2510_0000', 'c2510', 251000, { thinkParamId: 251001, battleGoalId: 251000, entityId: 6240 }),
    npcSlot('m10_01_00_00', 'c2640_0000', 'c2640', 264000, { entityId: 6190, baseHp: 888, hitHeight: 1.9 }),
    npcSlot('m13_00_00_00', 'c2920_0000', 'c2920', 292000, { entityId: 6200, baseHp: 555 }),
    npcSlot('m12_01_00_00', 'c4110_0000', 'c4110', 411000, { thinkParamId: 0, entityId: 6720, baseHp: 1280, hitHeight: 8, hitRadius: 2 }),
    npcSlot('m14_00_00_00', 'c5340_0000', 'c5340', 534000, { entityId: 1400700 }),
    slot('m10_01_00_00', 'c1000_0000', 'c1000', 100000, { teamType: 2, talkId: 101001, entityId: 1011950, riskFlags: ['talk', 'friendly-or-neutral'] }),
    ...extraRegular,
  );
  return {
    schemaVersion: 17, maps: ['m10_00_00_00', 'm14_00_00_00', 'm18_01_00_00', 'm10_01_00_00', 'm12_00_00_01'].map((id) => ({ id, name: id })), enemySlots,
    enemyArchetypes: [{ id: 'x' }], bossSlots: [], worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

const base = {
  ...defaultConfig, randomizeEnemies: true, randomizeBosses: false, randomizeItems: false, generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1',
  enemyReplacementChance: 100, enemyNormalReplacement: 'normals', enemyPlacementFit: 'anywhere', secondGargoyle: 'keep',
};
const hostileOf = (result) => result.placements.enemies.filter((entry) => entry.hostileNpc === true);

test('hostile NPCs come only from the authored sources and stand only in regular enemy slots, with the slot-owned row, the tangible mark and the wake-up', () => {
  const sourceIds = new Set(['m10_01_00_00:c2510_0000', 'm10_01_00_00:c2640_0000', 'm13_00_00_00:c2920_0000']);
  const seen = new Set();
  let total = 0;
  for (let seed = 0; seed < 25; seed += 1) {
    const catalog = catalogOf();
    const result = generate({ ...base, seed: `hn-${seed}`, enemyHostileNpcs: true }, { gameCatalog: catalog });
    const byId = new Map(catalog.enemySlots.map((entry) => [entry.id, entry]));
    for (const entry of hostileOf(result)) {
      total += 1;
      assert.ok(sourceIds.has(entry.sourceSlot), `${entry.sourceSlot} is an authored hostile source`);
      assert.equal(byId.get(entry.slot).teamType, 0, 'the destination is a regular slot');
      assert.ok(Number.isInteger(entry.scaledNpcParamId), 'the slot owns the row: drops, team, respawn and rewards');
      assert.equal(entry.makeTangible, true);
      assert.notEqual(entry.targetModelName, 'c4110', 'Gough is not a source');
      assert.notEqual(entry.map, 'm12_00_00_01', 'the Darkroot Basin has no event file for the wake-up');
      if (entry.passiveUntilAttacked) {
        assert.equal(entry.compatibility, 'passive-until-attacked-sampled', 'a passive slot keeps its own wake-up rule');
        assert.equal(entry.forceCombatActivation, undefined);
      } else {
        assert.equal(entry.compatibility, 'sampled-hostile-npc');
        assert.equal(entry.forceCombatActivation, true);
      }
      assert.ok(entry.entityId >= 0, 'the wake-up needs an entity id (assigned when the slot has none)');
      seen.add(entry.targetModelName);
    }
    // No friendly NPC, bonfire or service slot is ever a destination.
    const npcSlots = catalog.enemySlots.filter((entry) => entry.teamType !== 0).map((entry) => entry.id);
    for (const placement of result.placements.enemies) assert.equal(npcSlots.includes(placement.slot), false, `${placement.slot} is not a destination`);
  }
  assert.ok(total > 50, 'hostile NPCs appear');
  assert.deepEqual([...seen].sort(), ['c2510', 'c2640', 'c2920']);
});

test('the option off, absent or on an unrelated catalog changes nothing: the NPC slots never influence a world without the option', () => {
  const hashOf = (config, catalog) => JSON.stringify(generate(config, { gameCatalog: catalog }).placements);
  const withNpcs = catalogOf();
  const withoutNpcs = { ...catalogOf(), enemySlots: catalogOf().enemySlots.filter((entry) => entry.teamType === 0) };
  const { enemyHostileNpcs: _omitted, ...absent } = { ...base, seed: 'same' };
  assert.equal(hashOf({ ...base, seed: 'same', enemyHostileNpcs: false }, withNpcs), hashOf(absent, withNpcs));
  assert.equal(hashOf({ ...base, seed: 'same', enemyHostileNpcs: false }, withNpcs), hashOf({ ...base, seed: 'same', enemyHostileNpcs: false }, withoutNpcs));
  assert.notEqual(hashOf({ ...base, seed: 'same', enemyHostileNpcs: true }, withNpcs), hashOf({ ...base, seed: 'same', enemyHostileNpcs: false }, withNpcs));
  assert.equal(hostileOf(generate({ ...base, seed: 'same', enemyHostileNpcs: false }, { gameCatalog: withNpcs })).length, 0);
});

test('the option is refused unless normal enemies are replaced, with a custom pool and under permutation-v1', () => {
  const catalog = catalogOf();
  for (const overrides of [
    { enemyNormalReplacement: 'none' },
    { enemyNormalReplacement: 'bosses' },
    { enemyCustomPool: ['c2500', 'c2560'] },
    { enemyHostileNpcs: 'yes' },
  ]) {
    assert.throws(() => generate({ ...base, enemyHostileNpcs: true, ...overrides }, { gameCatalog: catalog }), (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID', JSON.stringify(overrides));
  }
  assert.throws(() => generate({ ...base, enemyPlacementAlgorithm: 'permutation-v1', enemyHostileNpcs: true }, { gameCatalog: catalog }), (error) => error.code === 'ENEMY_PLACEMENT_OPTIONS_INVALID');
  for (const mode of ['normals', 'both']) assert.doesNotThrow(() => generate({ ...base, enemyNormalReplacement: mode, enemyHostileNpcs: true, seed: 'ok' }, { gameCatalog: catalog }), mode);
});

test('a map never holds more hostile NPCs than the limit, with or without Type replacement (a group counts all its slots)', () => {
  assert.equal(sampling.HOSTILE_NPC_MAP_CAP, 12);
  const many = Array.from({ length: 40 }, (_, index) => slot('m14_00_00_00', `c2500_01${String(index).padStart(2, '0')}`, 'c2500', 250000, { baseHp: 100 }));
  for (const typeReplacement of ['disabled', 'enabled', 'enabled-except-roaming-bosses']) {
    let withNpcs = 0;
    for (let seed = 0; seed < 25; seed += 1) {
      const result = generate({ ...base, seed: `cap-${typeReplacement}-${seed}`, enemyTypeReplacement: typeReplacement, enemyHostileNpcs: true }, { gameCatalog: catalogOf({ extraRegular: many }) });
      const perMap = new Map();
      for (const entry of hostileOf(result)) perMap.set(entry.map, (perMap.get(entry.map) ?? 0) + 1);
      for (const [mapId, count] of perMap) assert.ok(count <= 12, `${mapId} holds ${count} hostile NPCs (${typeReplacement})`);
      if (perMap.size > 0) withNpcs += 1;
    }
    assert.ok(withNpcs > 0, `hostile NPCs appear with Type replacement ${typeReplacement}`);
  }
});

test('every NPC slot has a reason: the sources are safe, every service, quest, bonfire and player-model slot is unsafe and an unknown slot defaults to unsafe', () => {
  const { NPC_ROLES, classifyNpcSlot } = generator;
  const role = (entry) => classifyNpcSlot(entry);
  const catalog = catalogOf();
  const byId = new Map(catalog.enemySlots.map((entry) => [entry.id, entry]));
  assert.equal(role(byId.get('m10_01_00_00:c2510_0000')), NPC_ROLES.SAFE_HOSTILE_SOURCE);
  assert.equal(role(byId.get('m10_01_00_00:c2640_0000')), NPC_ROLES.SAFE_HOSTILE_SOURCE);
  assert.equal(role(byId.get('m13_00_00_00:c2920_0000')), NPC_ROLES.SAFE_HOSTILE_SOURCE);
  assert.equal(role(byId.get('m10_02_00_00:c2510_0000')), NPC_ROLES.MERCHANT_UNSAFE, 'the Lower Burg Merchant names a logic script the data tool does not carry');
  assert.equal(role(byId.get('m12_01_00_00:c4110_0000')), NPC_ROLES.BLACKSMITH_UNSAFE);
  assert.equal(role(byId.get('m14_00_00_00:c5340_0000')), NPC_ROLES.FIREKEEPER_OR_BONFIRE);
  assert.equal(role(byId.get('m10_01_00_00:c1000_0000')), NPC_ROLES.FIREKEEPER_OR_BONFIRE);
  assert.equal(role(slot('m10_01_00_00', 'c0000_0001', 'c0000', 6130, { teamType: 2, talkId: 1 })), NPC_ROLES.PLAYER_MODEL_NPC);
  assert.equal(role(slot('m10_01_00_00', 'c9999_0000', 'c9999', 1, { teamType: 2 })), NPC_ROLES.UNKNOWN_UNSAFE);
  assert.equal(role(slot('m10_01_00_00', 'c9998_0000', 'c9998', 1, { teamType: 5 })), NPC_ROLES.UNKNOWN_UNSAFE);
  assert.equal(role(npcSlot('m10_01_00_00', 'c3210_0000', 'c3210', 321001)), NPC_ROLES.QUEST_NPC);
});
