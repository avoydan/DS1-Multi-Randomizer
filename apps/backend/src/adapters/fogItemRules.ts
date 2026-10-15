/** Items that the world randomizer requires and the lot rows it can follow. */
export interface FogTrackedItems {
  items: Array<{ equipType: number; itemId: number; required: boolean }>;
  lotStarts: number[];
}

/** The projection produced from the same Compact catalog instance used by FogHost. */
export interface CompactFogTrackingProjection {
  items: readonly { equipType: number; itemId: number; required: boolean }[];
  lotStarts: readonly number[];
}

export function trackedItemsFromCompactProjection(projection: CompactFogTrackingProjection): FogTrackedItems {
  return {
    items: projection.items.map((item) => ({ ...item })),
    lotStarts: [...projection.lotStarts],
  };
}
