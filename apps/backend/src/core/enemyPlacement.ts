import {
  DEFAULT_ENEMY_PLACEMENT_ALGORITHM,
  ENEMY_PLACEMENT_ALGORITHMS,
  IMPLEMENTED_ENEMY_PLACEMENT_ALGORITHMS,
  DIFFICULTY_MODES,
  DIFFICULTY_STRICTNESS,
  NEUTRAL_ENEMY_PLACEMENT_OPTIONS,
  NORMAL_ENEMY_REPLACEMENT_MODES,
  PLACEMENT_FIT_MODES,
  TYPE_REPLACEMENT_MODES,
} from '@ds1tr/contracts';
import type { EnemyPlacementAlgorithm } from '@ds1tr/contracts';

/**
 * A setting that only `sampling-v1` understands, with the value that makes it a no-op. Under `permutation-v1` the
 * backend rejects any other value instead of silently ignoring it (docs/ENEMY_PLACEMENT_MODEL.md, section 7).
 */
export interface SamplingOnlyOption {
  key: string;
  neutral: unknown;
}

export const SAMPLING_ONLY_OPTIONS: readonly SamplingOnlyOption[] = [
  { key: 'replacementChance', neutral: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.replacementChance },
  { key: 'normalReplacement', neutral: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.normalReplacement },
  { key: 'fit', neutral: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.fit },
  { key: 'difficultyMode', neutral: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.difficultyMode },
  { key: 'difficultyStrictness', neutral: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.difficultyStrictness },
  { key: 'typeReplacement', neutral: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.typeReplacement },
];

/** Value-domain checks of the sampling-only options (they apply to both algorithms: a bad value is never ignored). */
function validateSamplingOnlyValues(value: Record<string, unknown>, path: string, problems: string[]): void {
  const chance = value.replacementChance;
  if (Object.hasOwn(value, 'replacementChance') && (typeof chance !== 'number' || !Number.isInteger(chance) || chance < 0 || chance > 100)) {
    problems.push(`${path}.replacementChance must be an integer from 0 to 100.`);
  }
  if (Object.hasOwn(value, 'normalReplacement') && !(NORMAL_ENEMY_REPLACEMENT_MODES as readonly unknown[]).includes(value.normalReplacement)) {
    problems.push(`${path}.normalReplacement must be ${NORMAL_ENEMY_REPLACEMENT_MODES.join(' or ')}.`);
  }
  if (Object.hasOwn(value, 'fit') && !(PLACEMENT_FIT_MODES as readonly unknown[]).includes(value.fit)) {
    problems.push(`${path}.fit must be ${PLACEMENT_FIT_MODES.join(', ')}.`);
  }
  if (Object.hasOwn(value, 'difficultyMode') && !(DIFFICULTY_MODES as readonly unknown[]).includes(value.difficultyMode)) {
    problems.push(`${path}.difficultyMode must be ${DIFFICULTY_MODES.join(', ')}.`);
  }
  if (Object.hasOwn(value, 'difficultyStrictness') && !(DIFFICULTY_STRICTNESS as readonly unknown[]).includes(value.difficultyStrictness)) {
    problems.push(`${path}.difficultyStrictness must be ${DIFFICULTY_STRICTNESS.join(', ')}.`);
  }
  if (Object.hasOwn(value, 'typeReplacement') && !(TYPE_REPLACEMENT_MODES as readonly unknown[]).includes(value.typeReplacement)) {
    problems.push(`${path}.typeReplacement must be ${TYPE_REPLACEMENT_MODES.join(', ')}.`);
  }
}

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
  for (const key of allowed) {
    if (!Object.hasOwn(value, key)) problems.push(`${path}.${key} is required.`);
  }
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) problems.push(`${path}.${key} is not supported.`);
  }
  if (samplingOnly === SAMPLING_ONLY_OPTIONS) validateSamplingOnlyValues(value, path, problems);
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
