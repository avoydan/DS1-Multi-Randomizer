import assert from 'node:assert/strict';
import test from 'node:test';
import { loadCombinedRandomizerCore } from '../../dist/adapters/combinedRandomizerCore.js';

// Engine behavior of the legacy-parity Batch 1 options, on synthetic catalogs
// (extracted game data is never committed; docs/LICENSING.md).

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;

const MOSQUITO_IDS = ['0058', '0059', '0085', '0086', '0090', '0091'];

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

function enemyCatalog() {
  const slots = [];
  let index = 0;
  // Three maps; each model appears in more than one slot so same-model swaps are possible.
  const models = [['c1000', 100000], ['c1010', 101000], ['c1020', 102000], ['c1030', 103000]];
  for (const mapId of ['m10_00_00_00', 'm10_01_00_00', 'm11_00_00_00']) {
    for (let i = 0; i < 12; i += 1) {
      const [model, npc] = models[i % models.length];
      slots.push(slot(mapId, `${model}_${String(i).padStart(4, '0')}`, model, npc, index += 1));
    }
  }
  for (let i = 0; i < 3; i += 1) slots.push(slot('m15_01_00_00', `c2780_000${i}`, 'c2780', 278010 + i, index += 1));
  MOSQUITO_IDS.forEach((id) => slots.push(slot('m14_00_00_00', `c3090_${id}`, 'c3090', 309000, index += 1)));
  for (let i = 0; i < 4; i += 1) slots.push(slot('m14_00_00_00', `c3090_01${i}0`, 'c3090', 309000, index += 1));
  return {
    schemaVersion: 17,
    maps: ['m10_00_00_00', 'm10_01_00_00', 'm11_00_00_00', 'm14_00_00_00', 'm15_01_00_00'].map((id) => ({ id, name: id })),
    enemySlots: slots,
    enemyArchetypes: [{ id: 'x' }],
    bossSlots: [],
    worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

const base = (overrides = {}) => ({
  ...defaultConfig, seed: 'parity-seed', randomizeEnemies: true, randomizeBosses: false,
  randomizeItems: false, generateSpoilerLog: false, ...overrides,
});

const regular = (result) => result.placements.enemies.filter((p) => /^count-preserving/u.test(p.compatibility));
const placedSlots = (result) => new Set(result.placements.enemies.map((p) => p.slot));
const mimicIds = (catalog) => catalog.enemySlots.filter((s) => s.modelName === 'c2780').map((s) => s.id);
const mosquitoIds = () => MOSQUITO_IDS.map((id) => `m14_00_00_00:c3090_${id}`);

test('the legacy defaults are the engine defaults', () => {
  assert.equal(defaultConfig.fashionSouls, true);
  assert.equal(defaultConfig.replaceMimics, true);
  assert.equal(defaultConfig.replaceRespawningMosquitoes, true);
  assert.equal(defaultConfig.preventSameEnemy, false);
});

test('mimics: replaced by default, kept in place and out of the pool when off', () => {
  const catalog = enemyCatalog();
  const on = generate(base(), { gameCatalog: catalog });
  const off = generate(base({ replaceMimics: false }), { gameCatalog: catalog });
  for (const id of mimicIds(catalog)) {
    assert.equal(placedSlots(on).has(id), true, `${id} is randomized by default`);
    assert.equal(placedSlots(off).has(id), false, `${id} stays vanilla when off`);
  }
  // Excluded as targets means excluded as sources too: no mimic appears elsewhere.
  assert.equal(off.placements.enemies.some((p) => p.targetModelName === 'c2780'), false);
  assert.equal(generate(base({ replaceMimics: false }), { gameCatalog: catalog }).placementHash, off.placementHash);
  assert.notEqual(on.placementHash, off.placementHash);
});

test('mosquitoes: exactly the six respawning Blighttown swamp mosquitoes are controlled', () => {
  const catalog = enemyCatalog();
  const on = generate(base(), { gameCatalog: catalog });
  const off = generate(base({ replaceRespawningMosquitoes: false }), { gameCatalog: catalog });
  for (const id of mosquitoIds()) {
    assert.equal(placedSlots(on).has(id), true, `${id} is randomized by default`);
    assert.equal(placedSlots(off).has(id), false, `${id} stays vanilla when off`);
  }
  const otherMosquitoes = catalog.enemySlots.filter(
    (s) => s.modelName === 'c3090' && !mosquitoIds().includes(s.id),
  );
  assert.equal(otherMosquitoes.length, 4);
  for (const s of otherMosquitoes) assert.equal(placedSlots(off).has(s.id), true, `${s.id} is not respawning-controlled`);
});

test('prevent same enemy: no regular slot receives its own model, deterministically and within the map budget', () => {
  const catalog = enemyCatalog();
  const base0 = generate(base(), { gameCatalog: catalog });
  assert.ok(regular(base0).some((p) => p.modelName === p.targetModelName), 'the default allows same-model placements');
  for (let i = 0; i < 25; i += 1) {
    const config = base({ seed: `same-${i}`, preventSameEnemy: true });
    const result = generate(config, { gameCatalog: catalog });
    assert.equal(regular(result).filter((p) => p.modelName === p.targetModelName).length, 0, `seed ${i}`);
    assert.equal(result.placementHash, generate(config, { gameCatalog: catalog }).placementHash);
    // Count-preserving: the multiset of models is unchanged.
    const count = (r, key) => regular(r).reduce((m, p) => m.set(p[key], (m.get(p[key]) || 0) + 1), new Map());
    assert.deepEqual([...count(result, 'targetModelName')].sort(), [...count(result, 'modelName')].sort());
  }
});

test('prevent same enemy: an impossible pool fails with a typed message instead of looping', () => {
  const catalog = enemyCatalog();
  // Make one model fill more than half of the randomized slots.
  for (const s of catalog.enemySlots) {
    if (/^m1[01]_/u.test(s.mapId)) Object.assign(s, { modelName: 'c1000', npcParamId: 100000, thinkParamId: 100000 });
  }
  assert.throws(
    () => generate(base({ preventSameEnemy: true, replaceMimics: false, replaceRespawningMosquitoes: false }), { gameCatalog: catalog }),
    /PREVENT_SAME_ENEMY_INFEASIBLE/u,
  );
});

test('enemy options do not change item placements and vice versa', () => {
  const catalog = enemyCatalog();
  const a = generate(base(), { gameCatalog: catalog });
  const b = generate(base({ fashionSouls: false }), { gameCatalog: catalog });
  assert.deepEqual(a.placements.enemies, b.placements.enemies);
});

// --- Fashion Souls -------------------------------------------------------

function armorLot(mapId, rowId, itemId, name) {
  return { rowId, name, mapId, protectedProgression: false,
    entries: [{ slot: 1, itemId, category: 268435456, quantity: 1, equipType: 1, name }] };
}

function itemCatalog() {
  const lots = [];
  let row = 1_000_000;
  // Four four-piece sets and two three-piece sets in two maps, as consecutive rows (vanilla layout).
  const sets = [
    ['m10_00_00_00', 210000, 4], ['m10_00_00_00', 220000, 4], ['m10_01_00_00', 300000, 4],
    ['m10_01_00_00', 350000, 4], ['m11_00_00_00', 240000, 3], ['m11_00_00_00', 260000, 3],
  ];
  for (const [mapId, base, size] of sets) {
    row += 10;
    for (let piece = 0; piece < size; piece += 1) lots.push(armorLot(mapId, row + piece, base + piece * 1000, `Set${base}-${piece}`));
  }
  // An irregular set (non-consecutive rows) that must stay an ordinary pool of pieces.
  [1_001_000, 1_001_050, 1_001_090].forEach((rowId, i) => lots.push(armorLot('m11_00_00_00', rowId, 690000 + i * 1000, `Odd${i}`)));
  // Non-armor lots.
  for (let i = 0; i < 12; i += 1) {
    lots.push({ rowId: 1_002_000 + i, name: `Goods${i}`, mapId: 'm10_00_00_00', protectedProgression: false,
      entries: [{ slot: 1, itemId: 500 + i, category: 1073741824, quantity: 1, equipType: 3, name: `Goods${i}` }] });
  }
  return { schemaVersion: 17, maps: ['m10_00_00_00', 'm10_01_00_00', 'm11_00_00_00'].map((id) => ({ id, name: id })),
    enemySlots: [slot('m10_00_00_00', 'c1000_0000', 'c1000', 100000, 1)], enemyArchetypes: [{ id: 'x' }],
    bossSlots: [], worldItemLots: lots, gifts: [], enemyDropLots: [], shopEntries: [] };
}

const itemBase = (overrides = {}) => base({ randomizeEnemies: false, randomizeItems: true, ...overrides });
const itemByRow = (result) => new Map(result.placements.items.map((p) => [p.rowId, p]));

test('Fashion Souls on (default): armor pieces are shuffled individually and sets break up', () => {
  const catalog = itemCatalog();
  let broken = 0;
  for (let i = 0; i < 10; i += 1) {
    const rows = itemByRow(generate(itemBase({ seed: `fashion-on-${i}` }), { gameCatalog: catalog }));
    const first = [1_000_010, 1_000_011, 1_000_012, 1_000_013].map((r) => Math.floor(rows.get(r).itemId / 10000));
    if (new Set(first).size > 1) broken += 1;
  }
  assert.ok(broken > 0, 'at least one seed splits a set');
});

test('Fashion Souls off: complete sets are exchanged between same-size set locations', () => {
  const catalog = itemCatalog();
  const vanillaSets = new Set(['210000,211000,212000,213000', '220000,221000,222000,223000',
    '300000,301000,302000,303000', '350000,351000,352000,353000', '240000,241000,242000', '260000,261000,262000']);
  const homes = [[1_000_010, 4], [1_000_020, 4], [1_000_030, 4], [1_000_040, 4], [1_000_050, 3], [1_000_060, 3]];
  let moved = 0;
  for (let i = 0; i < 20; i += 1) {
    const config = itemBase({ seed: `fashion-off-${i}`, fashionSouls: false });
    const result = generate(config, { gameCatalog: catalog });
    assert.equal(result.placementHash, generate(config, { gameCatalog: catalog }).placementHash, 'deterministic');
    const rows = itemByRow(result);
    const seen = new Set();
    for (const [start, size] of homes) {
      const ids = Array.from({ length: size }, (_, k) => rows.get(start + k).itemId);
      const key = ids.join(',');
      assert.ok(vanillaSets.has(key), `home ${start} holds a complete set (${key})`);
      assert.equal(key.split(',').length, size, 'sizes match');
      assert.equal(seen.has(key), false, 'no set is duplicated');
      seen.add(key);
      const vanillaKey = Array.from({ length: size }, (_, k) =>
        catalog.worldItemLots.find((l) => l.rowId === start + k).entries[0].itemId).join(',');
      if (key !== vanillaKey) moved += 1;
    }
    // The irregular set stays in the ordinary pool: its pieces may be anywhere, but are conserved.
    const odd = result.placements.items.map((p) => p.itemId).filter((id) => id >= 690000 && id < 700000).sort();
    assert.deepEqual(odd, [690000, 691000, 692000]);
  }
  assert.ok(moved > 0, 'sets actually move');
});

test('Fashion Souls off: the multiset of items is unchanged', () => {
  const catalog = itemCatalog();
  const ids = (result) => result.placements.items.map((p) => p.itemId).sort((a, b) => a - b);
  const on = generate(itemBase(), { gameCatalog: catalog });
  const off = generate(itemBase({ fashionSouls: false }), { gameCatalog: catalog });
  assert.deepEqual(ids(on), ids(off));
  assert.notEqual(on.placementHash, off.placementHash);
});
