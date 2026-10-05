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

// --- Batch 2: Soul items --------------------------------------------------

function goodsLot(rowId, itemId, name, quantity = 1, extra = {}) {
  return { rowId, name, mapId: 'm10_00_00_00', protectedProgression: false, ...extra,
    entries: [{ slot: 1, itemId, category: 1073741824, quantity, equipType: 3, name }] };
}

function soulCatalog() {
  const lots = [];
  let row = 2_000_000;
  const souls = [[400, 'Soul of a Lost Undead'], [401, 'Large Soul of a Lost Undead'], [404, 'Soul of a Proud Knight'],
    [407, 'Large Soul of a Brave Warrior'], [408, 'Soul of a Hero'], [409, 'Soul of a Great Hero']];
  for (let rep = 0; rep < 3; rep += 1) for (const [id, name] of souls) lots.push(goodsLot(row += 1, id, name));
  // Not lesser souls: Fire Keeper Souls (390-396) stay where they are.
  lots.push(goodsLot(row += 1, 390, 'Fire Keeper Soul'), goodsLot(row += 1, 391, 'Fire Keeper Soul'));
  // Ordinary consumables that may replace the souls, with vanilla quantities.
  for (let copy = 0; copy < 2; copy += 1) {
    lots.push(goodsLot(row += 1, 500, 'Humanity', 1), goodsLot(row += 1, 230, "Elizabeth's Mushroom", 1),
      goodsLot(row += 1, 311, 'Gold Pine Resin', 3), goodsLot(row += 1, 1000, 'Titanite Shard', 2),
      goodsLot(row += 1, 292, 'Firebomb', 6));
  }
  // A unique, unprotected item that FogMod tracks by id (Peculiar Doll): never a replacement.
  lots.push(goodsLot(row += 1, 384, 'Peculiar Doll', 1));
  // Ammunition and a protected key must never become replacements.
  lots.push({ rowId: row += 1, name: 'Standard Arrow', mapId: 'm10_00_00_00', protectedProgression: false,
    entries: [{ slot: 1, itemId: 2000000, category: 0, quantity: 30, equipType: 0, name: 'Standard Arrow' }] });
  lots.push(goodsLot(row += 1, 2011, 'Master Key', 1, { protectedProgression: true }));
  return { schemaVersion: 17, maps: [{ id: 'm10_00_00_00', name: 'm10_00_00_00' }],
    enemySlots: [slot('m10_00_00_00', 'c1000_0000', 'c1000', 100000, 1)], enemyArchetypes: [{ id: 'x' }],
    bossSlots: [], worldItemLots: lots, gifts: [], enemyDropLots: [], shopEntries: [] };
}

const lesserSoul = (p) => p.equipType === 3 && p.itemId >= 400 && p.itemId <= 409;

test('Soul items shuffled (default): the lesser souls stay in the pool', () => {
  const catalog = soulCatalog();
  const result = generate(itemBase({ seed: 'souls-default' }), { gameCatalog: catalog });
  assert.equal(result.placements.items.filter(lesserSoul).length, 18);
  assert.equal(defaultConfig.soulItems, 'shuffled');
});

test('Soul items replaced: exactly the lesser souls become other consumables, deterministically', () => {
  const catalog = soulCatalog();
  const allowed = new Set([500, 230, 311, 1000, 292]);
  const vanillaQuantities = { 500: [1], 230: [1], 311: [3], 1000: [2], 292: [6] };
  for (let i = 0; i < 15; i += 1) {
    const config = itemBase({ seed: `souls-${i}`, soulItems: 'replaced' });
    const result = generate(config, { gameCatalog: catalog });
    assert.equal(result.placementHash, generate(config, { gameCatalog: catalog }).placementHash, 'deterministic');
    const items = result.placements.items;
    assert.equal(items.filter(lesserSoul).length, 0, 'no lesser soul remains');
    assert.equal(items.filter((p) => p.itemId === 390 || p.itemId === 391).length, 2, 'Fire Keeper Souls untouched');
    assert.equal(items.length, catalog.worldItemLots.length - 1, 'every randomizable location keeps a placement');
    // The six lesser-soul kinds sit at 18 locations; their "from" names survive for the spoiler.
    assert.equal(items.filter((p) => /Soul of/u.test(p.from)).length, 18);
    assert.equal(items.filter((p) => p.itemId === 384).length, 1, 'the unique Peculiar Doll is never duplicated');
    assert.equal(items.filter((p) => items.length && p.itemId === 2000000).length, 1, 'ammunition is not duplicated');
    assert.equal(items.some((p) => p.itemId === 2011), false, 'the protected key lot is outside the pool and stays vanilla');
    for (const p of items.filter((q) => allowed.has(q.itemId))) {
      assert.ok(vanillaQuantities[p.itemId].includes(p.itemQuantity), `${p.itemName} x${p.itemQuantity}`);
    }
  }
  const shuffled = generate(itemBase({ seed: 'souls-0' }), { gameCatalog: catalog });
  const replaced = generate(itemBase({ seed: 'souls-0', soulItems: 'replaced' }), { gameCatalog: catalog });
  assert.notEqual(shuffled.placementHash, replaced.placementHash);
});

test('Soul items replaced: an empty replacement pool fails loudly', () => {
  const catalog = soulCatalog();
  catalog.worldItemLots = catalog.worldItemLots.filter(
    (l) => (l.entries[0].itemId >= 390 && l.entries[0].itemId <= 409) || l.entries[0].itemId === 2011 || l.entries[0].itemId === 2000000,
  );
  assert.throws(() => generate(itemBase({ soulItems: 'replaced' }), { gameCatalog: catalog }), /SOUL_ITEMS_POOL_EMPTY/u);
});

// --- Batch 2: Starting items ------------------------------------------------

function weapon(id, name, strength, isPrimaryWeapon, dexterity = 0) {
  return { id, name, strength, dexterity, intelligence: 0, faith: 0, isPrimaryWeapon };
}

function startingCatalog() {
  const row = (extra = {}) => ({ equip_Wep_Right: -1, equip_Subwep_Right: -1, equip_Wep_Left: -1, equip_Subwep_Left: -1,
    equip_Helm: -1, equip_Armer: -1, equip_Gaunt: -1, equip_Leg: -1, baseStr: 12, baseDex: 12, baseMag: 8, baseFai: 8, ...extra });
  const classes = ['warrior', 'knight', 'thief'].map((id) => ({ id, name: id, displayRowId: 1, startRowId: 2, display: row(), start: row() }));
  const armor = (first) => Array.from({ length: 6 }, (_, i) => ({ id: first + i * 1000, name: `A${first + i}` }));
  const weapons = [
    weapon(100000, 'Dagger', 5, true), weapon(200000, 'Shortsword', 8, true), weapon(300000, 'Longsword', 12, true),
    weapon(400000, 'Battle Axe', 16, true), weapon(500000, 'Mace', 17, true), weapon(600000, 'Spear', 18, true),
    weapon(1_400_000, 'Buckler', 7, false), weapon(1_401_000, 'Wooden Shield', 8, false), weapon(1_402_000, 'Heater Shield', 9, false),
    weapon(1_403_000, 'Knight Shield', 10, false), weapon(1_404_000, 'Target Shield', 8, false), weapon(1_405_000, 'Plank Shield', 7, false),
    weapon(1_300_000, "Sorcerer's Catalyst", 4, false), weapon(1_360_000, 'Talisman', 4, false),
  ];
  return { schemaVersion: 17, maps: [{ id: 'm10_00_00_00', name: 'x' }],
    enemySlots: [slot('m10_00_00_00', 'c1000_0000', 'c1000', 100000, 1)], enemyArchetypes: [{ id: 'x' }], bossSlots: [],
    worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [], startingClasses: classes,
    startingEquipmentPools: { weapons, helms: armor(10_000), armors: armor(20_000), gauntlets: armor(30_000), legs: armor(40_000) } };
}

const startBase = (overrides = {}) => base({ randomizeEnemies: false, randomizeItems: false,
  randomizeStartingEquipment: true, ...overrides });
const isShieldId = (id) => id >= 1_400_000 && id < 1_600_000;

test('Starting items: the engine default keeps the mixed pool', () => {
  const catalog = startingCatalog();
  assert.equal(defaultConfig.startingItems, 'mixed-pool');
  for (let i = 0; i < 20; i += 1) {
    const a = generate(startBase({ seed: `start-${i}` }), { gameCatalog: catalog });
    const b = generate(startBase({ seed: `start-${i}`, startingItems: 'mixed-pool' }), { gameCatalog: catalog });
    assert.equal(a.placementHash, b.placementHash);
  }
});

test('Starting items: shield modes give every class a shield in the left hand', () => {
  const catalog = startingCatalog();
  let mixedHasNonShield = false;
  for (let i = 0; i < 30; i += 1) {
    const mixed = generate(startBase({ seed: `start-${i}`, startingItems: 'mixed-pool' }), { gameCatalog: catalog });
    if (mixed.placements.startingClasses.some((c) => !isShieldId(c.equipment.pickupOffhand.id))) mixedHasNonShield = true;
    for (const mode of ['shield-1h', 'shield-2h']) {
      const config = startBase({ seed: `start-${i}`, startingItems: mode });
      const result = generate(config, { gameCatalog: catalog });
      assert.equal(result.placementHash, generate(config, { gameCatalog: catalog }).placementHash, 'deterministic');
      const used = new Set();
      for (const cls of result.placements.startingClasses) {
        const eq = cls.equipment;
        assert.ok(isShieldId(eq.pickupOffhand.id), `${mode} ${cls.slot} offhand ${eq.pickupOffhand.name}`);
        assert.ok(eq.pickupOffhand.strength <= 12, 'shield is usable one-handed');
        assert.ok(eq.pickupWeapon.isPrimaryWeapon, 'right hand holds a primary weapon');
        const limit = mode === 'shield-1h' ? 12 : 18;
        assert.ok(eq.pickupWeapon.strength <= limit, `${mode} weapon strength ${eq.pickupWeapon.strength}`);
        for (const item of [eq.pickupWeapon, eq.pickupOffhand]) {
          assert.equal(used.has(item.id), false, 'pickups are unique');
          used.add(item.id);
        }
      }
    }
  }
  assert.ok(mixedHasNonShield, 'the mixed pool really can hand out non-shield offhands');
});

test('Starting items: shield-1h excludes weapons that need two hands, shield-2h allows them', () => {
  const catalog = startingCatalog();
  let twoHandedSeen = false;
  for (let i = 0; i < 60; i += 1) {
    const one = generate(startBase({ seed: `two-${i}`, startingItems: 'shield-1h' }), { gameCatalog: catalog });
    assert.ok(one.placements.startingClasses.every((c) => c.equipment.pickupWeapon.strength <= 12));
    const two = generate(startBase({ seed: `two-${i}`, startingItems: 'shield-2h' }), { gameCatalog: catalog });
    if (two.placements.startingClasses.some((c) => c.equipment.pickupWeapon.strength > 12)) twoHandedSeen = true;
  }
  assert.ok(twoHandedSeen);
});

test('Starting items has no effect without starting equipment', () => {
  const catalog = startingCatalog();
  const off = { randomizeStartingEquipment: false, randomizeStartingClass: true };
  const a = generate(startBase({ seed: 'off', ...off }), { gameCatalog: catalog });
  const b = generate(startBase({ seed: 'off', ...off, startingItems: 'shield-1h' }), { gameCatalog: catalog });
  assert.equal(a.placementHash, b.placementHash);
});

// --- Batch 2: Gwyn nerf and Pinwheel Chaos -----------------------------------

function bossSlot(mapId, name, modelName, npc, index) {
  return slot(mapId, name, modelName, npc, index, { entityId: 1_000_000 + index, hasEntityId: true, baseHp: 1000 });
}

function bossCatalog() {
  const bosses = [
    ['m10_00_00_00', 'c2240_0000', 'c2240', 224000], ['m11_00_00_00', 'c2250_0000', 'c2250', 225000],
    ['m12_00_00_00', 'c2260_0000', 'c2260', 226000], ['m13_00_00_00', 'c3320_0000', 'c3320', 332000],
    ['m14_00_00_00', 'c3330_0000', 'c3330', 333000], ['m15_00_00_00', 'c3340_0000', 'c3340', 334000],
    ['m16_00_00_00', 'c3350_0000', 'c3350', 335000], ['m18_00_00_00', 'c5370_0000', 'c5370', 537000],
  ].map(([mapId, name, model, npc], index) => bossSlot(mapId, name, model, npc, index + 1));
  const clones = Array.from({ length: 14 }, (_, i) =>
    slot('m13_00_00_00', `c3320_${String(i + 1).padStart(4, '0')}`, 'c3320', 332001, 50 + i,
      { baseHp: 1, entityId: 1_300_800 + i + 1 }));
  const regular = [];
  const models = [['c1000', 100000], ['c1010', 101000], ['c1020', 102000], ['c1030', 103000]];
  for (const mapId of ['m10_00_00_00', 'm11_00_00_00', 'm13_00_00_00', 'm14_00_00_00']) {
    for (let i = 0; i < 10; i += 1) {
      const [model, npc] = models[i % models.length];
      regular.push(slot(mapId, `${model}_${String(i).padStart(4, '0')}`, model, npc, 100 + regular.length));
    }
  }
  return { schemaVersion: 17, maps: bosses.map((b) => ({ id: b.mapId, name: b.mapId })),
    enemySlots: [...bosses, ...clones, ...regular], enemyArchetypes: [{ id: 'x' }], bossSlots: bosses,
    worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [] };
}

const bossBase = (overrides = {}) => base({ randomizeEnemies: true, randomizeBosses: true, randomizeItems: false, ...overrides });
const gwynPlacements = (result) => result.placements.bosses.filter((p) => p.targetModelName === 'c5370');

test('Gwyn nerf: none keeps Gwyn in the boss pool; strong and medium drop it with the legacy chance', () => {
  const catalog = bossCatalog();
  const counts = { none: 0, medium: 0, strong: 0 };
  const total = 120;
  for (const mode of Object.keys(counts)) {
    for (let i = 0; i < total; i += 1) {
      const config = bossBase({ seed: `gwyn-${i}`, gwynNerf: mode });
      const result = generate(config, { gameCatalog: catalog });
      if (i < 5) assert.equal(result.placementHash, generate(config, { gameCatalog: catalog }).placementHash);
      const placed = gwynPlacements(result).length;
      if (placed > 0) counts[mode] += 1;
      const note = result.validation.notes.find((n) => n.startsWith('Gwyn nerf'));
      if (mode === 'none') {
        assert.equal(note, undefined);
        assert.equal(placed, 1, 'Gwyn always goes somewhere in a permutation');
      } else {
        assert.equal(placed > 0, !note.includes('was excluded'), 'the note matches what happened');
      }
    }
  }
  assert.equal(counts.none, total);
  // Rejection chances are 60% (medium) and 85% (strong): Gwyn is placed in about 40% / 15% of seeds.
  assert.ok(counts.medium > total * 0.28 && counts.medium < total * 0.52, `medium ${counts.medium}`);
  assert.ok(counts.strong > total * 0.06 && counts.strong < total * 0.26, `strong ${counts.strong}`);
});

test('Gwyn nerf: the engine default changes nothing and adds no per-enemy property', () => {
  const catalog = bossCatalog();
  assert.equal(defaultConfig.gwynNerf, 'none');
  const a = generate(bossBase({ seed: 'gwyn-x' }), { gameCatalog: catalog });
  const b = generate(bossBase({ seed: 'gwyn-x', gwynNerf: 'none' }), { gameCatalog: catalog });
  assert.equal(a.placementHash, b.placementHash);
  for (const p of generate(bossBase({ seed: 'gwyn-x', gwynNerf: 'strong' }), { gameCatalog: catalog }).placements.bosses) {
    assert.equal('gwynNerf' in p, false);
  }
});

test('Gwyn nerf does nothing without boss randomization', () => {
  const catalog = bossCatalog();
  const a = generate(bossBase({ seed: 'gwyn-y', randomizeBosses: false }), { gameCatalog: catalog });
  const b = generate(bossBase({ seed: 'gwyn-y', randomizeBosses: false, gwynNerf: 'strong' }), { gameCatalog: catalog });
  assert.equal(a.placementHash, b.placementHash);
});

test('Pinwheel Chaos off (default): the main Pinwheel is replaced and the clones are untouched', () => {
  const catalog = bossCatalog();
  const result = generate(bossBase({ seed: 'pin-default' }), { gameCatalog: catalog });
  assert.ok(result.placements.bosses.some((p) => p.slot === 'm13_00_00_00:c3320_0000'));
  assert.equal(result.placements.enemies.some((p) => /c3320_00(0[1-9]|1[0-4])$/u.test(p.slot)), false);
  assert.equal(defaultConfig.pinwheelChaos, false);
});

test('Pinwheel Chaos on: the main Pinwheel stays, exactly the 14 clones are replaced with normal stats', () => {
  const catalog = bossCatalog();
  const cloneIds = catalog.enemySlots
    .filter((s) => s.id.startsWith('m13_00_00_00:c3320_') && !s.id.endsWith('_0000')).map((s) => s.id);
  assert.equal(cloneIds.length, 14);
  for (let i = 0; i < 20; i += 1) {
    const config = bossBase({ seed: `pin-${i}`, pinwheelChaos: true });
    const result = generate(config, { gameCatalog: catalog });
    assert.equal(result.placementHash, generate(config, { gameCatalog: catalog }).placementHash, 'deterministic');
    const all = [...result.placements.bosses, ...result.placements.enemies];
    assert.equal(all.some((p) => p.slot === 'm13_00_00_00:c3320_0000'), false, 'the main Pinwheel is not touched');
    assert.equal(result.placements.bosses.some((p) => p.targetModelName === 'c3320'), false, 'Pinwheel is out of the boss pool');
    const clones = result.placements.enemies.filter((p) => cloneIds.includes(p.slot));
    assert.deepEqual(clones.map((p) => p.slot).sort(), [...cloneIds].sort());
    for (const p of clones) {
      assert.equal(p.compatibility, 'pinwheel-chaos-clone');
      assert.equal(p.scaling, 'vanilla', 'replaced clones keep their own stats instead of the clone 1 HP');
      assert.notEqual(p.slot, p.sourceSlot);
    }
    for (const p of result.placements.enemies.filter((q) => !cloneIds.includes(q.slot) && /^count-preserving/u.test(q.compatibility))) {
      assert.equal(p.scaling, 'area');
    }
    const finalModels = new Map(catalog.enemySlots.map((s) => [s.id, [s.mapId, s.modelName]]));
    for (const p of all) finalModels.set(p.slot, [p.map, p.targetModelName]);
    const models = new Set([...finalModels.values()].filter(([map]) => map === 'm13_00_00_00').map(([, model]) => model));
    assert.ok(models.size <= 30);
  }
  const off = generate(bossBase({ seed: 'pin-0' }), { gameCatalog: catalog });
  const on = generate(bossBase({ seed: 'pin-0', pinwheelChaos: true }), { gameCatalog: catalog });
  assert.notEqual(off.placementHash, on.placementHash);
});

test('Pinwheel Chaos honors prevent-same', () => {
  const catalog = bossCatalog();
  const result = generate(bossBase({ seed: 'pin-same', pinwheelChaos: true, preventSameEnemy: true }), { gameCatalog: catalog });
  const clones = result.placements.enemies.filter((p) => p.compatibility === 'pinwheel-chaos-clone');
  assert.equal(clones.length, 14);
  assert.equal(clones.some((p) => p.targetModelName === 'c3320'), false);
});

test('Pinwheel Chaos needs regular enemies', () => {
  const catalog = bossCatalog();
  const a = generate(bossBase({ seed: 'pin-z', randomizeEnemies: false }), { gameCatalog: catalog });
  const b = generate(bossBase({ seed: 'pin-z', randomizeEnemies: false, pinwheelChaos: true }), { gameCatalog: catalog });
  assert.equal(a.placementHash, b.placementHash);
});

// --- Batch 3: Replace second Gargoyle -----------------------------------------

const G0 = 'm10_01_00_00:c5350_0000';
const G1 = 'm10_01_00_00:c5350_0001';
const G2 = 'm10_01_00_00:c5350_0002';
const T0 = 'm10_01_00_00:c5352_0000';
const T1 = 'm10_01_00_00:c5352_0001';

function gargoyleCatalog() {
  const catalog = bossCatalog();
  const bell = ['0000', '0001', '0002'].map((n, i) => bossSlot('m10_01_00_00', `c5350_${n}`, 'c5350', 535000 + (n === '0001' ? 1 : 0), 200 + i));
  const tails = ['0000', '0001'].map((n, i) => slot('m10_01_00_00', `c5352_${n}`, 'c5352', 535200, 210 + i, { entityId: 1010900 + i }));
  const guardian = [bossSlot('m12_01_00_00', 'c3471_0000', 'c3471', 347100, 220)];
  const guardianParts = [slot('m12_01_00_00', 'c3472_0000', 'c3472', 347200, 221, { entityId: 1210900 })];
  const bossOnly = [bell[0], bell[2], guardian[0]];
  catalog.enemySlots.push(...bell, ...tails, ...guardian, ...guardianParts);
  catalog.bossSlots.push(...bossOnly);
  catalog.maps.push({ id: 'm10_01_00_00', name: 'm10_01_00_00' }, { id: 'm12_01_00_00', name: 'm12_01_00_00' });
  return catalog;
}

const gargoyleBase = (overrides = {}) => bossBase({ randomizeItems: false, ...overrides });
const everyPlacement = (result) => [...result.placements.bosses, ...result.placements.enemies];
const placementsFor = (result, id) => everyPlacement(result).filter((p) => p.slot === id);

test('Second Gargoyle: the engine default keeps the earlier single-boss behavior', () => {
  const catalog = gargoyleCatalog();
  assert.equal(defaultConfig.secondGargoyle, 'single-boss');
  for (let i = 0; i < 10; i += 1) {
    const a = generate(gargoyleBase({ seed: `sg-${i}` }), { gameCatalog: catalog });
    const b = generate(gargoyleBase({ seed: `sg-${i}`, secondGargoyle: 'single-boss' }), { gameCatalog: catalog });
    assert.equal(a.placementHash, b.placementHash);
    const second = placementsFor(a, G1);
    assert.equal(second.length, 1);
    assert.equal(second[0].disableEntity, true, 'the extra body is disabled, not replaced');
    assert.equal(second[0].compatibility, 'disabled-extra-bell-gargoyle-body');
  }
});

test('Second Gargoyle replace: the second body is replaced by an independently drawn boss', () => {
  const catalog = gargoyleCatalog();
  const drawn = new Set();
  for (let i = 0; i < 40; i += 1) {
    const config = gargoyleBase({ seed: `sg-${i}`, secondGargoyle: 'replace' });
    const result = generate(config, { gameCatalog: catalog });
    assert.equal(result.placementHash, generate(config, { gameCatalog: catalog }).placementHash, 'deterministic');
    const second = placementsFor(result, G1);
    assert.equal(second.length, 1, 'exactly one placement for the second body');
    const [p] = second;
    assert.notEqual(p.disableEntity, true);
    assert.equal(p.compatibility, 'legacy-second-gargoyle-replacement');
    assert.equal(p.entityId, catalog.enemySlots.find((q) => q.id === G1).entityId, 'the vanilla entity is reused, so no duplicate entity id appears');
    assert.notEqual(p.targetModelName, 'c5350', 'a Bell Gargoyle is never drawn');
    assert.notEqual(p.targetModelName, 'c3471', 'linked units are not drawn as a lone body');
    assert.equal(p.portableBossSanitization, true);
    assert.equal(p.forceCombatActivation, false, 'the vanilla event releases the body; forcing combat would wake it at once');
    assert.deepEqual([p.groundX, p.groundY, p.groundZ], [10.69, 48.92, 124.35], 'legacy rooftop position');
    assert.ok(result.placements.enemies.includes(p), 'the placement lives in the regular list (DsrDataTool rejects it as a boss entry)');
    drawn.add(p.targetModelName);
    // The first body keeps its own, separate source; the third body and the second tail stay disabled.
    assert.equal(placementsFor(result, G0).length, 1);
    assert.equal(placementsFor(result, G2)[0].disableEntity, true);
    assert.equal(placementsFor(result, T1)[0].disableEntity, true);
    assert.equal(placementsFor(result, T0).length, 1);
    // No duplicate placements and no duplicate scaled NpcParam rows.
    const slots = everyPlacement(result).map((q) => q.slot);
    assert.equal(new Set(slots).size, slots.length);
    const scaled = everyPlacement(result).map((q) => q.scaledNpcParamId).filter(Boolean);
    assert.equal(new Set(scaled).size, scaled.length);
    assert.ok(result.validation.notes.some((n) => n.startsWith('Replace second Gargoyle:')));
  }
  assert.ok(drawn.size >= 3, `the draw varies across seeds (${drawn.size} distinct bosses)`);
});

test('Second Gargoyle replace: the draw honors the Gwyn nerf and Pinwheel Chaos exclusions', () => {
  const catalog = gargoyleCatalog();
  for (let i = 0; i < 60; i += 1) {
    const result = generate(gargoyleBase({ seed: `sgx-${i}`, secondGargoyle: 'replace', gwynNerf: 'strong', pinwheelChaos: true }), { gameCatalog: catalog });
    const [p] = placementsFor(result, G1);
    const note = result.validation.notes.find((n) => n.startsWith('Gwyn nerf'));
    if (note.includes('was excluded')) assert.notEqual(p.targetModelName, 'c5370');
    assert.notEqual(p.targetModelName, 'c3320');
  }
});

test('Second Gargoyle keep: the second body and its tail stay vanilla', () => {
  const catalog = gargoyleCatalog();
  for (let i = 0; i < 20; i += 1) {
    const config = gargoyleBase({ seed: `sg-${i}`, secondGargoyle: 'keep' });
    const result = generate(config, { gameCatalog: catalog });
    assert.equal(result.placementHash, generate(config, { gameCatalog: catalog }).placementHash, 'deterministic');
    assert.equal(placementsFor(result, G1).length, 0, 'no placement for the second body');
    assert.equal(placementsFor(result, T1).length, 0, 'no placement for its tail');
    assert.equal(placementsFor(result, G2)[0].disableEntity, true, 'the third body is unchanged');
    assert.equal(placementsFor(result, G0).length, 1, 'the first body is still replaced');
    assert.ok(result.validation.notes.some((n) => n.startsWith('Replace second Gargoyle off')));
  }
});

test('Second Gargoyle does nothing without boss randomization', () => {
  const catalog = gargoyleCatalog();
  const a = generate(gargoyleBase({ seed: 'sg-off', randomizeBosses: false }), { gameCatalog: catalog });
  for (const mode of ['replace', 'keep']) {
    const b = generate(gargoyleBase({ seed: 'sg-off', randomizeBosses: false, secondGargoyle: mode }), { gameCatalog: catalog });
    assert.equal(a.placementHash, b.placementHash, mode);
  }
});
