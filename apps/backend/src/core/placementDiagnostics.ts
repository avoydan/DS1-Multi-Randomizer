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
  maps: Record<string, { slots: number; replaced: number; finalModels: number; modelSet: number; baseline: number; effectiveCap: number; attempts?: number; typeGroups?: number }>;
  /** Map attempts that failed and were retried (map-local retries); empty when every map succeeded first time. */
  retries?: Array<{ mapId: string; attempt: number; slotId?: string; baseline?: number; effectiveCap?: number }>;
}

export interface PlacementDiagnosticsSummary {
  algorithm: EnemyPlacementAlgorithm;
  rngVersion: string | null;
  classTableHash: string | null;
  /** The effective sampling options of the run (sampling-v1 only). */
  options?: Record<string, unknown> | null;
  /** Custom pool state and coverage (sampling-v1). */
  customPool?: Record<string, unknown> | null;
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
    options: record(input?.options) ? structuredClone(record(input?.options)) as Record<string, unknown> : null,
    customPool: record(input?.customPool) ? structuredClone(record(input?.customPool)) as Record<string, unknown> : null,
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
  const options = diagnostics.options ?? {};
  const retried = summary.counters.mapsRetried ?? 0;
  const pool = diagnostics.customPool;
  const poolText = pool && pool.enabled ? `custom pool ${String(pool.selected)}/${String(pool.total)} (${String(pool.hash).slice(0, 12)}), ` : '';
  const typeMode = String(options.typeReplacement ?? 'disabled');
  const groups = typeMode === 'disabled' ? '' : `type replacement ${typeMode} (${summary.counters.typeGroupsReplaced ?? 0}/${summary.counters.typeGroups ?? 0} groups replaced), `;
  return `Enemy placement sampling-v1 (difficulty ${String(options.difficultyMode ?? 'random')}/${String(options.difficultyStrictness ?? 'loose')}): ${poolText}${groups}${summary.slots} regular slots, ${replaced} sampled, `
    + `${retried} map(s) retried, ${summary.counters.difficultyLapses ?? 0} difficulty lapses, `
    + `${summary.counters.keptByChance ?? 0} left by replacement chance, ${summary.rejections['model-budget'] ?? 0} model-budget rejections, `
    + `at most ${maxModels} models in a map, stages ${stages || 'none'}, class table ${diagnostics.classTableHash?.slice(0, 12) ?? 'n/a'}.`;
}
