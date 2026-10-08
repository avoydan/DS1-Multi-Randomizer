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
 *   - the shops of the DLC merchants (ShopLineupParam rows, see {@link dlcShopRowIds}) and the gifts of the DLC NPCs (item lots, see {@link DLC_GIFT_LOT_IDS});
 *   - the equipment that is found only in those places (the starting-equipment pools must not offer it).
 */

export const DLC_MAP_ID = 'm12_01_00_00';

/**
 * The event flag area of the DLC map: the sell conditions of the DLC merchants' shop rows (and the progress flags of its NPCs) are flags 1121xxxx (the area number of m12_01), the
 * same way the base game's shops are flagged 1101xxxx ... 1180xxxx by their maps. The shop rows of the scan carry that flag; nothing else in the scan says whose shop a row is.
 */
const DLC_FLAG_FIRST = 11210000;
const DLC_FLAG_LAST = 11219999;
/**
 * The first rows of the shop ranges that the DLC's own talk script (script/talk/m12_01_00_00.talkesdbnd.dcx) opens and no other talk script does: 6500 and 6600. The shop rows
 * of the scan do not say who sells them, so this is read from the game files once and recorded here.
 */
const DLC_SHOP_RANGE_STARTS: readonly number[] = [6500, 6600];
/** Rows of one merchant lie next to each other; a larger gap starts another merchant. */
const SHOP_BLOCK_GAP = 4;

/**
 * The gift lots of the DLC NPCs: Hawkeye Gough (his model c4110 stands only in the DLC map) and Elizabeth. The scan does not say where a gift is given; these two are treated as DLC
 * gifts on the evidence of their NPCs, which is the safe side: a gift that is left out stays as the game has it.
 */
export const DLC_GIFT_LOT_IDS: readonly number[] = [1510, 1520];

interface EntryLike { category?: number; itemId?: number; equipType?: number }
interface LotLike { rowId: number; entries?: EntryLike[]; mapId?: string; dlcOnly?: boolean }
interface SlotLike { mapId: string; modelName: string; npcParamId: number; thinkParamId: number; charaInitId: number; safeCandidate?: boolean }
interface ShopLike { rowId?: number; equipId: number; equipType: number; eventFlag?: number }
interface RewardLike { dlc?: boolean; lots: Array<{ chain: Array<{ rowId: number; entries: EntryLike[] }> }> }

export interface CatalogLike {
  maps?: Array<{ id: string }>;
  enemySlots?: SlotLike[];
  bossSlots?: SlotLike[];
  enemyArchetypes?: Array<{ id: string; slotCount: number; safeSlotCount: number; maps: string[] }>;
  worldItemLots?: LotLike[];
  gifts?: LotLike[];
  enemyDropLots?: LotLike[];
  shopEntries?: ShopLike[];
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
  shopEntries: number;
  gifts: number;
  /** Items that exist only in the excluded content (they stay where the game puts them). */
  exclusiveItems: number;
}

const GOODS = 0x40000000;
const itemKey = (entry: EntryLike): string => `${entry.category}:${entry.itemId}`;
const categoryOfEquipType = (equipType: number): number => [0, 0x10000000, 0x20000000, GOODS, GOODS][equipType] ?? GOODS;
/** The pool name of the starting equipment decides the item category (weapons are category 0; helms, armor, gauntlets and legs are category 0x10000000). */
const POOL_CATEGORY: Record<string, number> = { weapons: 0, helms: 0x10000000, armors: 0x10000000, gauntlets: 0x10000000, legs: 0x10000000 };

const isDlcLot = (lot: LotLike): boolean => lot.mapId === DLC_MAP_ID;

/**
 * The shop rows of the DLC merchants. A row whose sell-condition flag is in the DLC flag area anchors its merchant; the neighbouring rows of the same merchant (no gap larger than
 * {@link SHOP_BLOCK_GAP}; a flagged anchor stops at a base-game flag, the ranges the DLC talk script opens do not) belong to it too. On the real scan this is the rows 1581-1584 (the Artorias set), 6410-6426, 6500-6507 and 6600-6611.
 * Leaving out too many rows is the safe side (those shops stay as the game has them); leaving out too few would write into a DLC merchant's shop.
 */
export function dlcShopRowIds(shops: ReadonlyArray<ShopLike>): Set<number> {
  const rows = shops.filter((shop) => typeof shop.rowId === 'number').sort((a, b) => (a.rowId as number) - (b.rowId as number));
  const inDlcFlagArea = (shop: ShopLike): boolean => typeof shop.eventFlag === 'number' && shop.eventFlag >= DLC_FLAG_FIRST && shop.eventFlag <= DLC_FLAG_LAST;
  const unflaggedOrDlc = (shop: ShopLike): boolean => typeof shop.eventFlag !== 'number' || shop.eventFlag < 0 || inDlcFlagArea(shop);
  const result = new Set<number>();
  const grow = (index: number, directions: number[], accepts: (shop: ShopLike) => boolean): void => {
    result.add(rows[index].rowId as number);
    for (const direction of directions) {
      let previous = rows[index];
      for (let next = index + direction; next >= 0 && next < rows.length; next += direction) {
        const candidate = rows[next];
        if (Math.abs((candidate.rowId as number) - (previous.rowId as number)) > SHOP_BLOCK_GAP || !accepts(candidate)) break;
        result.add(candidate.rowId as number);
        previous = candidate;
      }
    }
  };
  rows.forEach((row, index) => {
    // A flagged row anchors its merchant in both directions, stopping at a row of a base-game flag area.
    if (inDlcFlagArea(row)) grow(index, [-1, 1], unflaggedOrDlc);
    // A row range that the DLC's talk script opens runs forward from its first row whatever the flags of its rows are (the merchant also sells spells that carry a base-game flag).
    if (DLC_SHOP_RANGE_STARTS.includes(row.rowId as number)) grow(index, [1], () => true);
  });
  return result;
}

/** The rows a verifier compares with the clean game for the shops and gifts that are left out (the same sets the exclusion uses). */
export function dlcContentRows(catalog: CatalogLike): { shopRows: number[]; giftLotRows: number[] } {
  const gifts = new Set(DLC_GIFT_LOT_IDS);
  return {
    shopRows: [...dlcShopRowIds(catalog.shopEntries ?? [])].sort((a, b) => a - b),
    giftLotRows: (catalog.gifts ?? []).map((lot) => lot.rowId).filter((id) => gifts.has(id)).sort((a, b) => a - b),
  };
}

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
  const dlcShops = dlcShopRowIds(catalog.shopEntries ?? []);
  const dlcGifts = new Set(DLC_GIFT_LOT_IDS);
  for (const lot of catalog.gifts ?? []) addAll(dlcGifts.has(lot.rowId) ? excludedKeys : keptKeys, lot.entries);
  for (const reward of rewards) for (const lot of reward.lots) for (const row of lot.chain) addAll(reward.dlc ? excludedKeys : keptKeys, row.entries);
  for (const shop of catalog.shopEntries ?? []) (dlcShops.has(shop.rowId as number) ? excludedKeys : keptKeys).add(`${categoryOfEquipType(shop.equipType)}:${shop.equipId}`);
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
    ...(catalog.shopEntries ? { shopEntries: catalog.shopEntries.filter((shop) => !dlcShops.has(shop.rowId as number)) } : {}),
    ...(catalog.gifts ? { gifts: catalog.gifts.filter((lot) => !dlcGifts.has(lot.rowId)) } : {}),
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
      shopEntries: (catalog.shopEntries ?? []).length - (filtered.shopEntries ?? []).length,
      gifts: (catalog.gifts ?? []).length - (filtered.gifts ?? []).length,
      exclusiveItems: exclusive.size,
    },
  };
}

/** One line for the generation log. */
export function describeDlcExclusion(summary: DlcExclusionSummary): string {
  return `DLC excluded from randomization: ${summary.enemySlots} enemy slots (${summary.bossSlots} boss), ${summary.worldItemLots} item pickups, ${summary.bossRewards} boss rewards, `
    + `${summary.enemyDropLots} enemy drop lots, ${summary.shopEntries} shop entries, ${summary.gifts} NPC gifts and ${summary.startingEquipment} starting-equipment entries are left as the game has them.`;
}
