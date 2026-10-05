import { ENEMY_PLACEMENT_ALGORITHMS } from '@ds1tr/contracts';
import type { EnemyPlacementAlgorithm } from '@ds1tr/contracts';

/** Counters of the sampling-v1 planner (engine `placementDiagnostics.summary`); `null` for permutation-v1. */
export interface PlacementSummary {
  slots: number;
  participating: number;
  notParticipating: number;
  stages: Record<string, number>;
  rejections: Record<string, number>;
  categoryDowngrades: number;
  terminalFailures: string[];
  counters: Record<string, number>;
  maps: Record<string, { slots: number; replaced: number; finalModels: number; modelSet: number; baseline: number; effectiveCap: number }>;
}

export interface PlacementDiagnosticsSummary {
  algorithm: EnemyPlacementAlgorithm;
  rngVersion: string | null;
  classTableHash: string | null;
  summary: PlacementSummary | null;
}

function record(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null;
}

/** Reads the engine's diagnostics defensively and drops per-slot detail, which belongs in `randomizer.json` only. */
export function summarizePlacementDiagnostics(value: unknown): PlacementDiagnosticsSummary {
  const input = record(value);
  const algorithm = input?.algorithm;
  if (typeof algorithm !== 'string' || !(ENEMY_PLACEMENT_ALGORITHMS as readonly string[]).includes(algorithm)) {
    return { algorithm: 'permutation-v1', rngVersion: null, classTableHash: null, summary: null };
  }
  const summary = record(input?.summary);
  return {
    algorithm: algorithm as EnemyPlacementAlgorithm,
    rngVersion: typeof input?.rngVersion === 'string' ? input.rngVersion : null,
    classTableHash: typeof input?.classTableHash === 'string' ? input.classTableHash : null,
    summary: summary ? structuredClone(summary) as unknown as PlacementSummary : null,
  };
}

/** One log line for a run; contains only counters and ids, never a path. */
export function formatPlacementDiagnostics(diagnostics: PlacementDiagnosticsSummary): string | null {
  const summary = diagnostics.summary;
  if (diagnostics.algorithm !== 'sampling-v1' || !summary) return null;
  const replaced = Object.values(summary.maps).reduce((total, map) => total + map.replaced, 0);
  const maxModels = Math.max(0, ...Object.values(summary.maps).map((map) => map.finalModels));
  const stages = Object.entries(summary.stages).filter(([, count]) => count > 0).map(([name, count]) => `${name} ${count}`).join(', ');
  return `Enemy placement sampling-v1: ${summary.slots} regular slots, ${replaced} sampled, `
    + `${summary.counters.keptByChance ?? 0} left by replacement chance, ${summary.rejections['model-budget'] ?? 0} model-budget rejections, `
    + `at most ${maxModels} models in a map, stages ${stages || 'none'}, class table ${diagnostics.classTableHash?.slice(0, 12) ?? 'n/a'}.`;
}
