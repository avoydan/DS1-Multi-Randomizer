import type { GenerationSettings } from '@ds1tr/contracts';

export interface WorldSummary { items: boolean; enemies: boolean; bosses: boolean; world: boolean }

/** The four things a person recognizes about a world, from its settings. */
export function summarize(settings: GenerationSettings): WorldSummary {
  return {
    items: settings.items.enabled,
    enemies: settings.enemies.enabled && settings.enemies.randomizeRegularEnemies,
    bosses: settings.enemies.enabled && settings.enemies.randomizeBosses,
    world: settings.fog.enabled,
  };
}
