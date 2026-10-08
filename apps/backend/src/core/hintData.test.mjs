import assert from 'node:assert/strict';
import test from 'node:test';
import { describeHint, hintRecords, toHintRecord } from '../../dist/core/hintData.js';
import { loadCombinedRandomizerCore } from '../../dist/adapters/combinedRandomizerCore.js';
import { soulsCatalog } from '../adapters/soulsTestSupport.mjs';

// Hint-sheet readiness (Part I): every randomized item that a hint sheet will list carries enough structured data to become a plain sentence later, without
// reading item lots again. The hint sheet itself is a later phase; nothing here builds a document.

const placement = (overrides = {}) => ({
  itemId: 2001, itemName: 'Basement Key', equipType: 3, sourcePool: 'world', targetPool: 'world', progression: true, keyName: 'basement_key',
  originalLocation: { area: 'Undead Parish', map: 'm10_01_00_00', itemLot: 1010140 }, randomizedLocation: { area: 'Depths', map: 'm10_00_00_00', itemLot: 1000240 }, ...overrides,
});

test('a placement becomes a record with the item, its kind, where it was, where it lies, the acquisition and the semantic owner of both', () => {
  const record = toHintRecord(placement());
  assert.deepEqual(record, {
    itemId: 2001, itemName: 'Basement Key', itemKind: 'goods', importance: 'key',
    original: { area: 'Undead Parish', map: 'm10_01_00_00', acquisition: 'world pickup', owner: 'Undead Parish', lot: 1010140 },
    randomized: { area: 'Depths', map: 'm10_00_00_00', acquisition: 'world pickup', owner: 'Depths', lot: 1000240 },
    keyName: 'basement_key', transposedFrom: null, placedAt: null,
  });
  assert.equal(describeHint(record), 'Basement Key (key) now lies in Depths, a world pickup; in the clean game it lay in Undead Parish, a world pickup.');
});

test('the item that lay at the destination in the clean game is carried (the engine from field)', () => {
  assert.equal(toHintRecord(placement({ from: 'Estus Flask' })).placedAt, 'Estus Flask');
  assert.equal(toHintRecord(placement({ from: '' })).placedAt, null);
});

test('the semantic owner of a boss reward, a gift, a drop and a shop is the boss, the NPC, the enemy and the shop, never a row id', () => {
  const boss = toHintRecord(placement({ sourcePool: 'boss', targetPool: 'gift', originalLocation: { area: 'Boss reward: Gaping Dragon', map: 'm10_00_00_00', itemLot: 2500 }, randomizedLocation: { area: 'NPC gift: Key to the Seal', map: 'ItemLotParam', itemLot: 1100 } }));
  assert.equal(boss.original.owner, 'Gaping Dragon');
  assert.equal(boss.original.acquisition, 'boss reward');
  assert.equal(boss.randomized.owner, 'Key to the Seal');
  assert.equal(boss.randomized.acquisition, 'NPC gift');
  const drop = toHintRecord(placement({ targetPool: 'enemy', randomizedLocation: { area: 'Enemy drop: Black Knight', map: 'ItemLotParam', itemLot: 27900000 } }));
  assert.equal(drop.randomized.owner, 'Black Knight');
  const shop = toHintRecord(placement({ targetPool: 'shop', randomizedLocation: { area: "Shop inventory: Andre of Astora's Soul", map: 'ShopLineupParam', itemLot: 5000 } }));
  assert.equal(shop.randomized.acquisition, 'shop');
});

test('the importance separates keys, the Lordvessel and Lord Souls, race items, boss souls and transposed weapons from ordinary items', () => {
  const importance = (overrides) => toHintRecord(placement({ keyName: undefined, progression: false, ...overrides })).importance;
  assert.equal(importance({ keyName: 'basement_key' }), 'key');
  assert.equal(importance({ keyName: 'lordvessel' }), 'lord');
  assert.equal(importance({ keyName: 'lord_soul_nito' }), 'lord');
  assert.equal(importance({ raceItem: true }), 'race item');
  assert.equal(importance({ transposedFrom: 'Soul of Smough', equipType: 0, itemId: 856400 }), 'boss soul weapon');
  assert.equal(importance({ itemId: 700, equipType: 3 }), 'boss soul');
  assert.equal(importance({ progression: true }), 'progression');
  assert.equal(importance({}), 'normal');
});

test('a spell (equip type 4) is a hintable item of its own kind', () => {
  const record = toHintRecord(placement({ itemName: 'Sorcery: Remedy', equipType: 4, keyName: undefined, progression: false }));
  assert.equal(record.itemKind, 'spell');
  assert.equal(record.importance, 'normal');
});

test('a placement that lacks what a hint needs is counted, never half-described', () => {
  assert.equal(toHintRecord(placement({ originalLocation: undefined })), null);
  assert.equal(toHintRecord(placement({ targetPool: 'unknown' })), null);
  assert.equal(toHintRecord(placement({ itemName: '' })), null);
  const { records, unhintable } = hintRecords({ items: [placement(), placement({ randomizedLocation: { area: '' } })], gifts: [] });
  assert.equal(records.length, 1);
  assert.equal(unhintable, 1);
});

test('every placement of a generated world (Transposed, all pools) can be turned into a hint', async () => {
  const loaded = await loadCombinedRandomizerCore();
  assert.equal(loaded.ok, true);
  const { generate, defaultConfig } = loaded.core;
  for (let index = 0; index < 5; index += 1) {
    const plan = generate({ ...defaultConfig, seed: `hint-${index}`, randomizeEnemies: false, randomizeBosses: false, randomizeItems: true, randomizeGifts: true,
      randomizeEnemyDrops: true, randomizeShops: true, soulItems: 'transposed', generateSpoilerLog: false }, { gameCatalog: soulsCatalog() });
    const { records, unhintable } = hintRecords(plan.placements);
    assert.ok(records.length > 20, 'the world has placements');
    assert.equal(unhintable, 0, `world ${index}: every placement is hintable`);
    for (const record of records) assert.match(describeHint(record), /^.+ now lies in .+, an? .+; in the clean game it lay in .+, an? .+\.$/u);
    assert.ok(records.some((record) => record.importance === 'boss soul weapon' || record.importance === 'boss soul'), 'the boss souls are classified');
  }
});
