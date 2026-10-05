import {
  DEFAULT_ENEMY_PLACEMENT_ALGORITHM,
  ENEMY_PLACEMENT_ALGORITHMS,
  IMPLEMENTED_ENEMY_PLACEMENT_ALGORITHMS,
} from '@ds1tr/contracts';
import type { EnemyPlacementAlgorithm } from '@ds1tr/contracts';

/**
 * A setting that only `sampling-v1` understands, with the value that makes it a no-op. Under `permutation-v1` the
 * backend rejects any other value instead of silently ignoring it (docs/ENEMY_PLACEMENT_MODEL.md, section 7).
 * Empty on purpose: Part 0 introduces the mechanism, the settings arrive with the sampler.
 */
export interface SamplingOnlyOption {
  key: string;
  neutral: unknown;
}

export const SAMPLING_ONLY_OPTIONS: readonly SamplingOnlyOption[] = [];

/** Stable code carried in the problem text and by the pipeline/engine failures for an algorithm that cannot run. */
export const ENEMY_PLACEMENT_UNAVAILABLE = 'ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE';

export function enemyPlacementUnavailableMessage(algorithm: string): string {
  return `${ENEMY_PLACEMENT_UNAVAILABLE}: enemy placement algorithm "${algorithm}" is not implemented yet. `
    + `Use "${DEFAULT_ENEMY_PLACEMENT_ALGORITHM}".`;
}

export function isEnemyPlacementImplemented(algorithm: EnemyPlacementAlgorithm): boolean {
  return IMPLEMENTED_ENEMY_PLACEMENT_ALGORITHMS.includes(algorithm);
}

export interface PlacementValidationContext {
  samplingOnly?: readonly SamplingOnlyOption[];
  implemented?: readonly EnemyPlacementAlgorithm[];
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

/**
 * Validates `config.enemies.placement`. Strict keys (`algorithm` plus the registered sampling-only options), a
 * known algorithm, an implemented algorithm, and neutral values for sampling-only options under `permutation-v1`.
 */
export function validateEnemyPlacement(value: unknown, problems: string[], context: PlacementValidationContext = {}): void {
  const samplingOnly = context.samplingOnly ?? SAMPLING_ONLY_OPTIONS;
  const implemented = context.implemented ?? IMPLEMENTED_ENEMY_PLACEMENT_ALGORITHMS;
  const path = 'config.enemies.placement';
  if (!isObject(value)) {
    problems.push(`${path} must be an object.`);
    return;
  }
  const allowed = new Set(['algorithm', ...samplingOnly.map((option) => option.key)]);
  if (!Object.hasOwn(value, 'algorithm')) problems.push(`${path}.algorithm is required.`);
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) problems.push(`${path}.${key} is not supported.`);
  }
  const algorithm = value.algorithm;
  if (algorithm === undefined) return;
  if (typeof algorithm !== 'string' || !(ENEMY_PLACEMENT_ALGORITHMS as readonly string[]).includes(algorithm)) {
    problems.push(`Enemy placement algorithm is invalid (expected ${ENEMY_PLACEMENT_ALGORITHMS.join(' or ')}).`);
    return;
  }
  if (!implemented.includes(algorithm as EnemyPlacementAlgorithm)) {
    problems.push(enemyPlacementUnavailableMessage(algorithm));
    return;
  }
  if (algorithm === 'permutation-v1') {
    for (const option of samplingOnly) {
      if (Object.hasOwn(value, option.key) && JSON.stringify(value[option.key]) !== JSON.stringify(option.neutral)) {
        problems.push(`${path}.${option.key} only applies to sampling-v1; it must be ${JSON.stringify(option.neutral)} with permutation-v1.`);
      }
    }
  }
}
