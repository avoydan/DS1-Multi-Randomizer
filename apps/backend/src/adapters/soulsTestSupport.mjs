// Synthetic item and boss reward data for the Soul items tests (extracted game data is never committed; docs/LICENSING.md). The shapes are the ones
// DsrDataTool scan writes (bossRewards, transpositionRecipes: patches/0021), with the real soul and lot ids of the game.

const goods = 0x40000000;
const weapon = (itemId, name) => ({ itemId, category: 0, equipType: 0, name });

/** The twelve boss souls with the trigger map and lot row of each reward (Sif's soul sits in the chain row after the Covenant ring). */
export const BOSS_SOULS = [
  { soul: 700, name: 'Soul of Quelaag', row: 2570, slot: 9, map: 'm14_00_00_00' },
  { soul: 701, name: 'Soul of Sif', row: 2541, slot: 6, map: 'm12_00_00_00', chainStart: 2540 },
  { soul: 702, name: 'Soul of Gwyn, Lord of Cinder', row: 2650, slot: 20, map: 'm18_00_00_00' },
  { soul: 703, name: 'Core of an Iron Golem', row: 2590, slot: 14, map: 'm15_00_00_00' },
  { soul: 704, name: 'Soul of Ornstein', row: 2610, slot: 16, map: 'm15_01_00_00' },
  { soul: 705, name: 'Soul of the Moonlight Butterfly', row: 2530, slot: 5, map: 'm12_00_00_00' },
  { soul: 706, name: 'Soul of Smough', row: 2620, slot: 17, map: 'm15_01_00_00' },
  { soul: 707, name: 'Soul of Priscilla', row: 2520, slot: 4, map: 'm11_00_00_00' },
  { soul: 708, name: 'Soul of Gwyndolin', row: 2600, slot: 15, map: 'm15_01_00_00' },
  { soul: 709, name: 'Guardian Soul', row: 2680, slot: 23, map: 'm12_01_00_00', dlc: true },
  { soul: 710, name: 'Soul of Artorias', row: 2690, slot: 24, map: 'm12_01_00_00', dlc: true },
  { soul: 711, name: 'Soul of Manus', row: 2700, slot: 25, map: 'm12_01_00_00', dlc: true },
];

const row = (rowId, itemId, name, category = goods, equipType = 3) => ({
  rowId, name, entries: [{ slot: 1, itemId, category, quantity: 1, equipType, name }],
});

/** boss rewards, their chain rows and the key/ring that shares a chain with a soul (they must never join the pool) */
export function bossRewards() {
  return BOSS_SOULS.map((boss) => ({
    id: `boss-reward-${boss.slot}`, slot: boss.slot, triggerFlag: boss.slot, mapId: boss.map, dlc: boss.dlc === true,
    lots: [{
      rowId: boss.chainStart ?? boss.row, role: 'primary',
      chain: [
        ...(boss.chainStart ? [row(boss.chainStart, 138, 'Covenant of Artorias', 0x20000000, 2)] : []),
        row(boss.row, boss.soul, boss.name),
      ],
    }, { rowId: 9000, role: 'bonus', chain: [row(9000, 500, 'Humanity')] }],
  }));
}

/** two families (a weapon at its reinforcement levels) for every soul but the Guardian's; the families are runs of ids in steps of 100 */
export function transpositionRecipes() {
  return BOSS_SOULS.filter((boss) => boss.soul !== 709).map((boss) => ({
    soulItemId: boss.soul, soulName: boss.name,
    families: [
      [0, 1, 2, 3].map((level) => weapon(boss.soul * 1000 + level * 100, `Boss weapon ${boss.soul} +${level}`)),
      [weapon(boss.soul * 1000 + 900, `Boss weapon ${boss.soul} B`)],
    ],
  }));
}

/** ordinary world lots: weapons, armor, consumables, restricted items for the DLC rule, and one protected key */
export function worldLots(count = 60, firstRow = 1000100) {
  const lots = [];
  const lot = (rowId, itemId, name, category, equipType, mapId, extra = {}) => ({
    rowId, name, mapId, protectedProgression: false, entries: [{ slot: 1, itemId, category, quantity: 1, equipType, name }], ...extra,
  });
  for (let index = 0; index < count; index += 1) {
    const mapId = ['m10_00_00_00', 'm10_01_00_00', 'm12_01_00_00', 'm14_00_00_00'][index % 4];
    const rowId = firstRow + index * 10;
    if (index % 9 === 0) lots.push(lot(rowId, 1000 + index, 'Titanite Shard', goods, 3, mapId));
    else if (index % 11 === 0) lots.push(lot(rowId, 800 + index, 'Large Ember', goods, 3, mapId));
    else if (index % 5 === 0) lots.push(lot(rowId, 100000 + index * 1000, `Sword ${index}`, 0, 0, mapId));
    else lots.push(lot(rowId, 4000 + index, `Item ${index}`, goods, 3, mapId));
  }
  lots.push(lot(firstRow + 9990, 2018, 'Sewer Chamber Key', goods, 3, 'm10_00_00_00', { protectedProgression: true }));
  return lots;
}

// One ordinary enemy slot is enough to make the catalog "extracted" (the item lots are what these tests are about).
const oneSlot = () => ({
  id: 'm10_00_00_00:c1000_0000', mapId: 'm10_00_00_00', name: 'c1000_0000', modelName: 'c1000', entityId: -1,
  npcParamId: 100000, thinkParamId: 100000, talkId: -1, charaInitId: 100000, collisionName: 'h0000B0', position: { x: 1, y: 0, z: 0 },
  rotation: { x: 0, y: 0, z: 0 }, movePoints: [], dummy: false, hasEntityId: false, eventModelLocked: false, riskFlags: [], safeCandidate: true,
  teamType: 0, npcType: 0, moveType: 0, hitHeight: 1, hitRadius: 1, hitYOffset: 0, baseHp: 100, soulReward: 10, battleStartDistance: 20,
  eyeDistance: 20, earDistance: 10, disablePathMove: false, battleGoalId: 100000,
});

export function soulsCatalog(overrides = {}) {
  return {
    schemaVersion: 17,
    maps: ['m10_00_00_00', 'm10_01_00_00', 'm12_01_00_00', 'm14_00_00_00'].map((id) => ({ id, name: id })),
    enemySlots: [oneSlot()], enemyArchetypes: [{ id: 'x' }], bossSlots: [],
    worldItemLots: worldLots(), gifts: [], enemyDropLots: [], shopEntries: [],
    bossRewards: bossRewards(), transpositionRecipes: transpositionRecipes(),
    ...overrides,
  };
}
