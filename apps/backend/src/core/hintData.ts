/**
 * Hint-sheet data contract (Part I, docs/ITEM_PROGRESSION_MODEL.md section 8). The hint sheet itself is a later phase; this is the one place that turns the
 * engine's placement metadata into a stable record, so the next phase does not have to reverse-engineer item lots again. Pure and deterministic; it reads the
 * placements document (`randomizer.json`, `placements`) and nothing else. It reveals nothing a spoiler log does not, and exposes no internal row ids by default
 * (`lot` is kept for the diagnostics, never shown in a hint).
 *
 * Spoiler severity is applied on top of these records by `filterHints` in the contracts (docs/HINT_SHEET.md); a record itself is the full detail.
 */

import type { HintLocation, HintRecord, HintLocationAcquisition as HintAcquisition, HintRecordImportance as HintImportance } from '@ds1tr/contracts';

export type { HintAcquisition, HintImportance, HintLocation, HintRecord };

interface PlacementLocation { area?: unknown; map?: unknown; itemLot?: unknown }
export interface PlacementLike {
  itemId?: unknown;
  itemName?: unknown;
  equipType?: unknown;
  sourcePool?: unknown;
  targetPool?: unknown;
  progression?: unknown;
  keyName?: unknown;
  raceItem?: unknown;
  transposedFrom?: unknown;
  /** The name of the item that lay at the destination in the clean game (`from` in the engine's placement). */
  from?: unknown;
  originalLocation?: PlacementLocation;
  randomizedLocation?: PlacementLocation;
}

const kinds = ['weapon', 'armor', 'ring', 'goods'] as const;
const acquisitionOf = (pool: unknown): HintAcquisition | null => ({
  world: 'world pickup', gift: 'NPC gift', boss: 'boss reward', enemy: 'enemy drop', shop: 'shop',
} as Record<string, HintAcquisition>)[String(pool)] ?? null;
const ownerOf = (acquisition: HintAcquisition, area: string): string => {
  const colon = area.indexOf(': ');
  return acquisition === 'world pickup' || colon < 0 ? area : area.slice(colon + 2);
};

function location(pool: unknown, value: PlacementLocation | undefined): HintLocation | null {
  const acquisition = acquisitionOf(pool);
  if (!acquisition || !value || typeof value.area !== 'string' || value.area === '' || typeof value.map !== 'string') return null;
  return { area: value.area, map: value.map, acquisition, owner: ownerOf(acquisition, value.area), lot: typeof value.itemLot === 'number' ? value.itemLot : null };
}

function importanceOf(placement: PlacementLike): HintImportance {
  const key = typeof placement.keyName === 'string' ? placement.keyName : null;
  if (key && (key === 'lordvessel' || key.startsWith('lord_soul'))) return 'lord';
  if (key) return 'key';
  if (placement.raceItem === true) return 'race item';
  if (typeof placement.transposedFrom === 'string') return 'boss soul weapon';
  if (placement.equipType === 3 && typeof placement.itemId === 'number' && placement.itemId >= 700 && placement.itemId <= 711) return 'boss soul';
  if (placement.progression === true) return 'progression';
  return 'normal';
}

/** One hint record per placement, or null when the placement lacks what a hint needs (the readiness test asserts that never happens). */
export function toHintRecord(placement: PlacementLike): HintRecord | null {
  const original = location(placement.sourcePool, placement.originalLocation);
  const randomized = location(placement.targetPool, placement.randomizedLocation);
  const kind = typeof placement.equipType === 'number' ? kinds[placement.equipType] : undefined;
  if (!original || !randomized || !kind || typeof placement.itemName !== 'string' || placement.itemName === '' || typeof placement.itemId !== 'number') return null;
  return {
    itemId: placement.itemId, itemName: placement.itemName, itemKind: kind, importance: importanceOf(placement), original, randomized,
    keyName: typeof placement.keyName === 'string' ? placement.keyName : null,
    transposedFrom: typeof placement.transposedFrom === 'string' ? placement.transposedFrom : null,
    placedAt: typeof placement.from === 'string' && placement.from !== '' ? placement.from : null,
  };
}

const LISTS = ['items', 'gifts', 'enemyDrops', 'shops', 'bossRewards'] as const;

/** Every placement of a placements document as a hint record, in a stable order (list, then the document's own order). */
export function hintRecords(placements: Partial<Record<(typeof LISTS)[number], PlacementLike[]>>): { records: HintRecord[]; unhintable: number } {
  const records: HintRecord[] = [];
  let unhintable = 0;
  for (const list of LISTS) {
    for (const placement of placements[list] ?? []) {
      const record = toHintRecord(placement);
      if (record) records.push(record); else unhintable += 1;
    }
  }
  return { records, unhintable };
}

/** A plain sentence for one record ("Basement Key (a key) now lies in Depths, a world pickup; in the clean game it lay in Undead Parish, a world pickup."). */
export function describeHint(record: HintRecord): string {
  const where = (place: HintLocation): string => `${place.owner}, ${place.acquisition === 'world pickup' ? 'a world pickup' : place.acquisition === 'NPC gift' ? 'a gift' : place.acquisition === 'boss reward' ? 'a boss reward' : place.acquisition === 'enemy drop' ? 'an enemy drop' : 'a shop'}`;
  const what = record.importance === 'normal' ? '' : ` (${record.importance})`;
  return `${record.itemName}${what} now lies in ${where(record.randomized)}; in the clean game it lay in ${where(record.original)}.`;
}
