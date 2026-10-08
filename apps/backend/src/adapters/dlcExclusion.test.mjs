import assert from 'node:assert/strict';
import test from 'node:test';
import { DLC_MAP_ID, describeDlcExclusion, excludeDlcFromCatalog } from '../../dist/adapters/dlcExclusion.js';

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
    gifts: [{ rowId: 3, entries: [{ category: 0, itemId: 101 }] }],
    enemyDropLots: [{ rowId: 4, entries: [{ category: 0, itemId: 102 }] }, { rowId: 5, dlcOnly: true, entries: [{ category: 0, itemId: 901 }] }, { rowId: 6, entries: [{ category: GOODS, itemId: 2900 }] }],
    shopEntries: [{ equipId: 103, equipType: 0 }],
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
  const { catalog: filtered, summary } = excludeDlcFromCatalog(original);
  assert.deepEqual(filtered.maps.map((map) => map.id), ['m10_00_00_00']);
  assert.equal(filtered.enemySlots.length, 2);
  assert.equal(filtered.bossSlots.length, 1);
  assert.deepEqual(filtered.enemyArchetypes.map((entry) => [entry.id, entry.slotCount, entry.maps]), [['c2500:10:1:-1', 2, ['m10_00_00_00']]]);
  assert.deepEqual(filtered.worldItemLots.map((lot) => lot.rowId), [1]);
  assert.deepEqual(filtered.enemyDropLots.map((lot) => lot.rowId), [4], 'the DLC-only lot and the reward row of a DLC boss are gone');
  assert.equal(filtered.treasures.length, 1);
  assert.deepEqual(filtered.bossRewards.map((reward) => reward.id), ['kept']);
  assert.deepEqual(filtered.transpositionRecipes, [{ soulItemId: 2500 }]);
  assert.deepEqual(filtered.gifts, original.gifts);
  assert.deepEqual(filtered.shopEntries, original.shopEntries);
  assert.deepEqual(filtered.startingEquipmentPools.weapons.map((item) => item.id), [100], 'equipment found only in the DLC is not offered; shared equipment stays');
  assert.deepEqual(filtered.startingEquipmentPools.helms, [{ id: 5 }]);
  assert.deepEqual(summary, { enemySlots: 2, bossSlots: 1, archetypes: 1, worldItemLots: 1, treasures: 1, bossRewards: 1, recipes: 1, enemyDropLots: 2, startingEquipment: 2, exclusiveItems: 3 });
  assert.match(describeDlcExclusion(summary), /DLC excluded from randomization: 2 enemy slots \(1 boss\)/u);
});

test('the input catalog is not changed, and a catalog without the DLC is returned equal', () => {
  const original = catalog();
  const before = JSON.stringify(original);
  const once = excludeDlcFromCatalog(original);
  assert.equal(JSON.stringify(original), before);
  assert.deepEqual(excludeDlcFromCatalog(once.catalog).catalog, once.catalog, 'applying it twice changes nothing more');
  assert.equal(excludeDlcFromCatalog(once.catalog).summary.enemySlots, 0);
});
