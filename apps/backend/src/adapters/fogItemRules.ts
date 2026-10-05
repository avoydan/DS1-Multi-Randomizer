import { readFile } from 'node:fs/promises';
import path from 'node:path';

/**
 * Which items the world randomizer follows, and the item lots it can follow them in.
 *
 * FogMod (ItemReader.FindItems) reads the annotated item lots of the finished game data and needs every required key item
 * in one of them, because the area the item is found in is part of its logic. The combined engine must therefore keep these
 * items inside those lots: an unannotated world lot, an NPC gift, an enemy drop, a shop or the starting inventory cannot be
 * represented, and FogMod stops with "Couldn't find <item> in item lots". The rules come from the same `fog.txt` FogHost
 * reads, so they follow the pinned FogMod data instead of a second copy of it.
 */
export interface FogTrackedItems {
  /**
   * Every key item of fog.txt. `equipType` 0 weapon, 1 armor, 2 ring, 3 goods (the "type:id" form of fog.txt). `required` is
   * false for the `randomonly` and `hard` items FogMod does not insist on finding; FogMod needs exactly one copy of a
   * required item in the annotated lots and at most one of any item.
   */
  items: Array<{ equipType: number; itemId: number; required: boolean }>;
  /** First row of every annotated lot; a lot also covers the rows that follow it without a gap. */
  lotStarts: number[];
}

export class FogItemRulesError extends Error {
  readonly code = 'FOG_ITEM_RULES_UNREADABLE';
}

/** Parses the `KeyItems` and `LotLocations` sections of FogMod's `fog.txt` (YAML, flat enough for a line reader). */
export function parseFogItemRules(text: string): FogTrackedItems {
  const lines = text.replace(/\r\n/gu, '\n').split('\n');
  const section = (start: string, end: string): string[] => {
    const from = lines.findIndex((line) => line.startsWith(start));
    const to = lines.findIndex((line, index) => index > from && line.startsWith(end));
    if (from < 0 || to < 0) throw new FogItemRulesError(`fog.txt has no ${start} section.`);
    return lines.slice(from + 1, to);
  };

  const items: FogTrackedItems['items'] = [];
  let current: { id: string | null; tags: string } | null = null;
  const flush = (): void => {
    if (!current?.id) return;
    // FogMod does not require randomonly items, and skips `hard` ones unless the hard option is on (FogHost has no such option).
    // They still count: a second copy of any key item in another annotated area stops FogMod.
    const required = !/\b(randomonly|hard)\b/u.test(current.tags);
    const match = /^(\d+):(\d+)$/u.exec(current.id);
    if (match) items.push({ equipType: Number(match[1]), itemId: Number(match[2]), required });
  };
  for (const line of section('KeyItems:', 'Warps:')) {
    if (line.startsWith('- ')) {
      flush();
      current = { id: null, tags: '' };
    }
    const id = /^\s+ID:\s*(\S+)/u.exec(line);
    if (id && current) current.id = id[1];
    const tags = /^\s+Tags:\s*(.*)$/u.exec(line);
    if (tags && current) current.tags = tags[1];
  }
  flush();

  const lotStarts: number[] = [];
  for (const line of section('LotLocations:', 'DefaultFlagCols:')) {
    const match = /^\s+(\d+):\s*\S+/u.exec(line);
    if (match) lotStarts.push(Number(match[1]));
  }
  if (!items.some((item) => item.required) || lotStarts.length === 0) throw new FogItemRulesError('fog.txt lists no key items or no annotated lots.');
  return { items, lotStarts };
}

export async function readFogItemRules(dataRoot: string): Promise<FogTrackedItems> {
  try {
    return parseFogItemRules(await readFile(path.join(dataRoot, 'dist', 'fog.txt'), 'utf8'));
  } catch (error) {
    if (error instanceof FogItemRulesError) throw error;
    throw new FogItemRulesError(`Cannot read fog.txt in ${dataRoot}: ${error instanceof Error ? error.message : String(error)}`);
  }
}
