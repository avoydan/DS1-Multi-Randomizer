import assert from 'node:assert/strict';
import test from 'node:test';
import { DLC_GIFT_LOT_IDS, DLC_MAP_ID, dlcContentRows, dlcShopRowIds, describeDlcExclusion, excludeDlcFromCatalog } from '../../dist/adapters/dlcExclusion.js';

const slot = (mapId, modelName, npcParamId) => ({ mapId, modelName, npcParamId, thinkParamId: 1, charaInitId: -1, safeCandidate: true });
const archetype = (model, npc, maps, slotCount) => ({ id: `${model}:${npc}:1:-1`, slotCount, safeSlotCount: slotCount, maps });
const GOODS = 0x40000000;

function catalog() {
  return {
    maps: [{ id: 'm10_00_00_00' }, { id: DLC_MAP_ID }],
    enemySlots: [slot('m10_00_00_00', 'c2500', 10), slot('m10_00_00_00', 'c2500', 10), slot(DLC_MAP_ID, 'c2500', 10), slot(DLC_MAP_ID, 'c5000', 50)],
    bossSlots: [slot('m10_00_00_00', 'c2010', 20), slot(DLC_MAP_ID, 'c5100', 51)],
    enemyArchetypes: [archetype('c2500', 10, ['m10_00_00_00', DLC_MAP_ID], 3), archetype('c5000', 50, [DLC_MAP_ID], 1)],
    worldItemLots: [{ rowId: 1, mapId: 'm10_00_00_00', entries: [{ category: 0, itemId: 100 }] }, { rowId: 2, mapId: DLC_MAP_ID, entries: [{ category: 0, itemId: 900 }, { category: 0, itemId: 100 }] }],
    gifts: [{ rowId: 3, entries: [{ category: 0, itemId: 101 }] }, { rowId: 1510, entries: [{ category: 0, itemId: 902 }] }, { rowId: 1520, entries: [{ category: GOODS, itemId: 230 }] }],
    enemyDropLots: [{ rowId: 4, entries: [{ category: 0, itemId: 102 }] }, { rowId: 5, dlcOnly: true, entries: [{ category: 0, itemId: 901 }] }, { rowId: 6, entries: [{ category: GOODS, itemId: 2900 }] }],
    shopEntries: [
      { rowId: 1580, equipId: 2001, equipType: 3, eventFlag: 11007020 }, // a base-game flag: the Master Key row next to the Artorias set is not the DLC's
      { rowId: 1581, equipId: 660000, equipType: 1, eventFlag: 11217060 }, // the DLC flag area anchors the Artorias set
      { rowId: 1582, equipId: 661000, equipType: 1, eventFlag: 11217070 },
      { rowId: 2200, equipId: 3400, equipType: 3, eventFlag: 11207000 }, // area 1120 is a base-game map
      { rowId: 6416, equipId: 290, equipType: 3, eventFlag: -1 }, // same merchant as the flagged 6420 (rows within four of each other)
      { rowId: 6420, equipId: 500, equipType: 3, eventFlag: 11217100 },
      { rowId: 6424, equipId: 2102000, equipType: 0, eventFlag: -1 },
      { rowId: 6500, equipId: 330, equipType: 3, eventFlag: -1 }, // another merchant: a gap of more than four rows from the first, flagged through 6501
      { rowId: 6501, equipId: 311, equipType: 3, eventFlag: 11217000 },
      { rowId: 10000, equipId: 3000, equipType: 4, eventFlag: -1 },
      { rowId: 5, equipId: 103, equipType: 0 }, // a record without a flag stays
    ],
    treasures: [{ mapId: 'm10_00_00_00' }, { mapId: DLC_MAP_ID }],
    bossRewards: [
      { id: 'kept', dlc: false, lots: [{ chain: [{ rowId: 7, entries: [{ category: GOODS, itemId: 2500 }] }] }] },
      { id: 'dlc', dlc: true, lots: [{ chain: [{ rowId: 6, entries: [{ category: GOODS, itemId: 2900 }] }] }] },
    ],
    transpositionRecipes: [{ soulItemId: 2500 }, { soulItemId: 2900 }],
    startingEquipmentPools: { weapons: [{ id: 100 }, { id: 900 }, { id: 901 }], helms: [{ id: 5 }] },
  };
}

test('the DLC map, its enemies, bosses, pickups, rewards and drops are left out; everything else is kept', () => {
  const original = catalog();
  const { catalog: filtered, summary, excludedDropLots } = excludeDlcFromCatalog(original);
  assert.deepEqual(excludedDropLots, [5, 6], 'the data tool is told which drop lots to leave alone (the DLC-only lot and the reward row of a DLC boss)');
  assert.deepEqual(filtered.maps.map((map) => map.id), ['m10_00_00_00']);
  assert.equal(filtered.enemySlots.length, 2);
  assert.equal(filtered.bossSlots.length, 1);
  assert.deepEqual(filtered.enemyArchetypes.map((entry) => [entry.id, entry.slotCount, entry.maps]), [['c2500:10:1:-1', 2, ['m10_00_00_00']]]);
  assert.deepEqual(filtered.worldItemLots.map((lot) => lot.rowId), [1]);
  assert.deepEqual(filtered.enemyDropLots.map((lot) => lot.rowId), [4], 'the DLC-only lot and the reward row of a DLC boss are gone');
  assert.equal(filtered.treasures.length, 1);
  assert.deepEqual(filtered.bossRewards.map((reward) => reward.id), ['kept']);
  assert.deepEqual(filtered.transpositionRecipes, [{ soulItemId: 2500 }]);
  assert.deepEqual(filtered.gifts.map((lot) => lot.rowId), [3], 'the gifts of the DLC NPCs (Hawkeye Gough, Elizabeth) are left out');
  assert.deepEqual(filtered.shopEntries.map((shop) => shop.rowId), [1580, 2200, 10000, 5], 'the shops of the DLC merchants are left out; the base-game shops stay');
  assert.deepEqual(filtered.startingEquipmentPools.weapons.map((item) => item.id), [100], 'equipment found only in the DLC is not offered; shared equipment stays');
  assert.deepEqual(filtered.startingEquipmentPools.helms, [{ id: 5 }]);
  assert.deepEqual(summary, { enemySlots: 2, bossSlots: 1, archetypes: 1, worldItemLots: 1, treasures: 1, bossRewards: 1, recipes: 1, enemyDropLots: 2, startingEquipment: 2, shopEntries: 7, gifts: 2, exclusiveItems: 12 });
  assert.match(describeDlcExclusion(summary), /DLC excluded from randomization: 2 enemy slots \(1 boss\)/u);
  assert.match(describeDlcExclusion(summary), /7 shop entries, 2 NPC gifts/u);
});

test('the DLC merchants are found by the DLC flag area and their neighbouring rows, never through a base-game flag, and the rows a verifier compares are the same', () => {
  const { shopEntries } = catalog();
  assert.deepEqual([...dlcShopRowIds(shopEntries)].sort((a, b) => a - b), [1581, 1582, 6416, 6420, 6424, 6500, 6501]);
  assert.deepEqual(dlcContentRows(catalog()), { shopRows: [1581, 1582, 6416, 6420, 6424, 6500, 6501], giftLotRows: [1510, 1520] });
  assert.deepEqual([...DLC_GIFT_LOT_IDS], [1510, 1520]);
  // The ranges the DLC talk script opens (6500, 6600) run on through spells that carry a base-game flag, and stop at the next merchant.
  const range = [6500, 6501, 6502, 6503, 6507, 6600, 6603, 6700].map((rowId) => ({ rowId, equipId: rowId, equipType: 3, eventFlag: rowId === 6502 ? 11207000 : -1 }));
  assert.deepEqual([...dlcShopRowIds(range)].sort((a, b) => a - b), [6500, 6501, 6502, 6503, 6507, 6600, 6603]);
  assert.deepEqual([...dlcShopRowIds([{ rowId: 1, equipId: 1, equipType: 3, eventFlag: -1 }, { rowId: 2, equipId: 2, equipType: 3 }])], [], 'no DLC flag, no DLC shop');
});

test('the input catalog is not changed, and a catalog without the DLC is returned equal', () => {
  const original = catalog();
  const before = JSON.stringify(original);
  const once = excludeDlcFromCatalog(original);
  assert.equal(JSON.stringify(original), before);
  assert.deepEqual(excludeDlcFromCatalog(once.catalog).catalog, once.catalog, 'applying it twice changes nothing more');
  assert.equal(excludeDlcFromCatalog(once.catalog).summary.enemySlots, 0);
});
