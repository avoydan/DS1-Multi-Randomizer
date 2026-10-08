import type { GenerationSettings } from '@ds1tr/contracts';

export interface WorldSummary { items: boolean; enemies: boolean; bosses: boolean; fog: boolean; dlc: boolean }

/** The four things a person recognizes about a world, from its settings. */
export function summarize(settings: GenerationSettings): WorldSummary {
  return {
    items: settings.items.enabled,
    enemies: settings.enemies.enabled && settings.enemies.randomizeRegularEnemies,
    bosses: settings.enemies.enabled && settings.enemies.randomizeBosses,
    fog: settings.fog.enabled,
    dlc: settings.output.includeDlc,
  };
}
