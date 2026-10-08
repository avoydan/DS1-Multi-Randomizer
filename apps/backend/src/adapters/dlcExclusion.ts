/**
 * DLC exclusion (docs/DLC_AUDIT.md). "DLC off" means: the DLC stays in the installed game exactly as it is, and the randomizer neither uses DLC content as a
 * candidate nor writes anything into it. It is done once, on the data catalog the combined engine reads (the vendored engine is not changed): every record that belongs
 * to the DLC is left out, so the engine cannot place it, cannot place anything there, and the data tool, which writes only what the engine placed, leaves it untouched.
 * With the DLC on nothing here runs, so every earlier world is generated exactly as before.
 *
 * What belongs to the DLC (only the Artorias of the Abyss map, `m12_01_00_00`, exists as DLC in Dark Souls Remastered):
 *   - the map, its enemy slots, its boss slots (Sanctuary Guardian, Knight Artorias, Manus, Black Dragon Kalameet) and the enemy archetypes that stand nowhere else;
 *   - its world item lots and treasure chests;
 *   - the boss rewards of its four bosses and the soul-weapon recipes of their souls;
 *   - the enemy drop lots that only DLC enemies award (tagged `dlcOnly` by the data tool, patch 0025) and the reward rows of the DLC bosses;
 *   - the equipment that is found only in those places (the starting-equipment pools must not offer it).
 */

export const DLC_MAP_ID = 'm12_01_00_00';

interface EntryLike { category?: number; itemId?: number; equipType?: number }
interface LotLike { rowId: number; entries?: EntryLike[]; mapId?: string; dlcOnly?: boolean }
interface SlotLike { mapId: string; modelName: string; npcParamId: number; thinkParamId: number; charaInitId: number; safeCandidate?: boolean }
interface RewardLike { dlc?: boolean; lots: Array<{ chain: Array<{ rowId: number; entries: EntryLike[] }> }> }

export interface CatalogLike {
  maps?: Array<{ id: string }>;
  enemySlots?: SlotLike[];
  bossSlots?: SlotLike[];
  enemyArchetypes?: Array<{ id: string; slotCount: number; safeSlotCount: number; maps: string[] }>;
  worldItemLots?: LotLike[];
  gifts?: LotLike[];
  enemyDropLots?: LotLike[];
  shopEntries?: Array<{ equipId: number; equipType: number }>;
  treasures?: Array<{ mapId: string }>;
  bossRewards?: RewardLike[];
  transpositionRecipes?: Array<{ soulItemId: number }>;
  startingEquipmentPools?: Record<string, Array<{ id: number }> | undefined>;
  [key: string]: unknown;
}

export interface DlcExclusionSummary {
  enemySlots: number;
  bossSlots: number;
  archetypes: number;
  worldItemLots: number;
  treasures: number;
  bossRewards: number;
  recipes: number;
  enemyDropLots: number;
  startingEquipment: number;
  /** Items that exist only in the excluded content (they stay where the game puts them). */
  exclusiveItems: number;
}

const GOODS = 0x40000000;
const itemKey = (entry: EntryLike): string => `${entry.category}:${entry.itemId}`;
const categoryOfEquipType = (equipType: number): number => [0, 0x10000000, 0x20000000, GOODS, GOODS][equipType] ?? GOODS;
/** The pool name of the starting equipment decides the item category (weapons are category 0; helms, armor, gauntlets and legs are category 0x10000000). */
const POOL_CATEGORY: Record<string, number> = { weapons: 0, helms: 0x10000000, armors: 0x10000000, gauntlets: 0x10000000, legs: 0x10000000 };

const isDlcLot = (lot: LotLike): boolean => lot.mapId === DLC_MAP_ID;

/** Returns a copy of the catalog without any DLC content, and what was left out. The input is not changed. */
export function excludeDlcFromCatalog<T extends CatalogLike>(catalog: T): { catalog: T; summary: DlcExclusionSummary } {
  const rewards = catalog.bossRewards ?? [];
  const dlcRewardRows = new Set<number>();
  const dlcRewardSouls = new Set<number>();
  for (const reward of rewards.filter((entry) => entry.dlc)) {
    for (const lot of reward.lots) {
      for (const row of lot.chain) {
        dlcRewardRows.add(row.rowId);
        for (const entry of row.entries) if (entry.category === GOODS && typeof entry.itemId === 'number') dlcRewardSouls.add(entry.itemId);
      }
    }
  }
  const worldLots = catalog.worldItemLots ?? [];
  const dropLots = catalog.enemyDropLots ?? [];
  const keptWorld = worldLots.filter((lot) => !isDlcLot(lot));
  const keptDrops = dropLots.filter((lot) => !lot.dlcOnly && !dlcRewardRows.has(lot.rowId));
  const keptRewards = rewards.filter((entry) => !entry.dlc);

  // Items that are found only in the excluded content: equipment the starting-equipment pools must not offer.
  const keptKeys = new Set<string>();
  const excludedKeys = new Set<string>();
  const addAll = (target: Set<string>, entries: EntryLike[] | undefined): void => { for (const entry of entries ?? []) target.add(itemKey(entry)); };
  for (const lot of worldLots) addAll(isDlcLot(lot) ? excludedKeys : keptKeys, lot.entries);
  for (const lot of dropLots) addAll(lot.dlcOnly || dlcRewardRows.has(lot.rowId) ? excludedKeys : keptKeys, lot.entries);
  for (const lot of catalog.gifts ?? []) addAll(keptKeys, lot.entries);
  for (const reward of rewards) for (const lot of reward.lots) for (const row of lot.chain) addAll(reward.dlc ? excludedKeys : keptKeys, row.entries);
  for (const shop of catalog.shopEntries ?? []) keptKeys.add(`${categoryOfEquipType(shop.equipType)}:${shop.equipId}`);
  const exclusive = new Set([...excludedKeys].filter((key) => !keptKeys.has(key)));

  const keptEnemySlots = (catalog.enemySlots ?? []).filter((slot) => slot.mapId !== DLC_MAP_ID);
  const archetypeKey = (slot: SlotLike): string => `${slot.modelName}:${slot.npcParamId}:${slot.thinkParamId}:${slot.charaInitId}`;
  const counts = new Map<string, { slotCount: number; safeSlotCount: number; maps: Set<string> }>();
  for (const slot of keptEnemySlots) {
    const entry = counts.get(archetypeKey(slot)) ?? { slotCount: 0, safeSlotCount: 0, maps: new Set<string>() };
    entry.slotCount += 1;
    if (slot.safeCandidate) entry.safeSlotCount += 1;
    entry.maps.add(slot.mapId);
    counts.set(archetypeKey(slot), entry);
  }
  const archetypes = (catalog.enemyArchetypes ?? []).flatMap((archetype) => {
    const entry = counts.get(archetype.id);
    return entry ? [{ ...archetype, slotCount: entry.slotCount, safeSlotCount: entry.safeSlotCount, maps: [...entry.maps].sort() }] : [];
  });

  let startingEquipment = 0;
  const pools: Record<string, Array<{ id: number }> | undefined> = {};
  for (const [name, pool] of Object.entries(catalog.startingEquipmentPools ?? {})) {
    const category = POOL_CATEGORY[name];
    const kept = (pool ?? []).filter((item) => category === undefined || !exclusive.has(`${category}:${item.id}`));
    startingEquipment += (pool ?? []).length - kept.length;
    pools[name] = kept;
  }

  const recipes = (catalog.transpositionRecipes ?? []).filter((recipe) => !dlcRewardSouls.has(recipe.soulItemId));
  const filtered = {
    ...catalog,
    maps: (catalog.maps ?? []).filter((map) => map.id !== DLC_MAP_ID),
    enemySlots: keptEnemySlots,
    bossSlots: (catalog.bossSlots ?? []).filter((slot) => slot.mapId !== DLC_MAP_ID),
    enemyArchetypes: archetypes,
    worldItemLots: keptWorld,
    enemyDropLots: keptDrops,
    treasures: (catalog.treasures ?? []).filter((treasure) => treasure.mapId !== DLC_MAP_ID),
    bossRewards: keptRewards,
    transpositionRecipes: recipes,
    ...(catalog.startingEquipmentPools ? { startingEquipmentPools: pools } : {}),
  } as T;
  return {
    catalog: filtered,
    summary: {
      enemySlots: (catalog.enemySlots ?? []).length - keptEnemySlots.length,
      bossSlots: (catalog.bossSlots ?? []).length - (filtered.bossSlots ?? []).length,
      archetypes: (catalog.enemyArchetypes ?? []).length - archetypes.length,
      worldItemLots: worldLots.length - keptWorld.length,
      treasures: (catalog.treasures ?? []).length - (filtered.treasures ?? []).length,
      bossRewards: rewards.length - keptRewards.length,
      recipes: (catalog.transpositionRecipes ?? []).length - recipes.length,
      enemyDropLots: dropLots.length - keptDrops.length,
      startingEquipment,
      exclusiveItems: exclusive.size,
    },
  };
}

/** One line for the generation log. */
export function describeDlcExclusion(summary: DlcExclusionSummary): string {
  return `DLC excluded from randomization: ${summary.enemySlots} enemy slots (${summary.bossSlots} boss), ${summary.worldItemLots} item pickups, ${summary.bossRewards} boss rewards, `
    + `${summary.enemyDropLots} enemy drop lots and ${summary.startingEquipment} starting-equipment entries are left as the game has them.`;
}
