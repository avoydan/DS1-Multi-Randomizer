import {
  BOSS_REPLACEMENT_MODES,
  CANONICAL_PLACEMENT_EXTENSION_KEYS,
  DEFAULT_ENEMY_PLACEMENT_ALGORITHM,
  ENEMY_PLACEMENT_ALGORITHMS,
  IMPLEMENTED_ENEMY_PLACEMENT_ALGORITHMS,
  DIFFICULTY_MODES,
  DIFFICULTY_STRICTNESS,
  NEUTRAL_ENEMY_PLACEMENT_OPTIONS,
  NORMAL_ENEMY_REPLACEMENT_MODES,
  PLACEMENT_FIT_MODES,
  ROAMING_BOSS_REPLACEMENT_MODES,
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
  { key: 'customPool', neutral: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.customPool },
];

/**
 * Options added after the first public Seed Codes (Part D). Optional on input - a configuration, preset or settings file that predates
 * them means the neutral value - and sampling-only like the others.
 */
const EXTENSION_OPTIONS: readonly SamplingOnlyOption[] = CANONICAL_PLACEMENT_EXTENSION_KEYS.map(({ key, neutral }) => ({ key, neutral }));

/** Model ids look like `c2500`; the engine checks them against the catalog of the run. */
export const MAX_CUSTOM_POOL_ENTRIES = 512;
const MODEL_ID = /^[A-Za-z][A-Za-z0-9_]{0,15}$/u;

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
  if (Object.hasOwn(value, 'bossReplacement') && !(BOSS_REPLACEMENT_MODES as readonly unknown[]).includes(value.bossReplacement)) {
    problems.push(`${path}.bossReplacement must be ${BOSS_REPLACEMENT_MODES.join(' or ')}.`);
  }
  if (Object.hasOwn(value, 'uniqueBosses')) {
    if (typeof value.uniqueBosses !== 'boolean') problems.push(`${path}.uniqueBosses must be true or false.`);
    else if (value.uniqueBosses && !['bosses', 'both'].includes(String(value.bossReplacement ?? 'classic'))) problems.push(`${path}.uniqueBosses needs ${path}.bossReplacement "bosses" or "both".`);
  }
  if (Object.hasOwn(value, 'bossChanceInBossSlots')) {
    const chance = value.bossChanceInBossSlots;
    if (typeof chance !== 'number' || !Number.isInteger(chance) || chance < 0 || chance > 100) problems.push(`${path}.bossChanceInBossSlots must be an integer from 0 to 100.`);
    else if (chance !== NEUTRAL_ENEMY_PLACEMENT_OPTIONS.bossChanceInBossSlots && value.bossReplacement !== 'both') problems.push(`${path}.bossChanceInBossSlots needs ${path}.bossReplacement "both".`);
  }
  if (Object.hasOwn(value, 'hostileNpcs')) {
    if (typeof value.hostileNpcs !== 'boolean') problems.push(`${path}.hostileNpcs must be true or false.`);
    else if (value.hostileNpcs) {
      if (value.normalReplacement !== 'normals' && value.normalReplacement !== 'both') problems.push(`${path}.hostileNpcs needs ${path}.normalReplacement "normals" or "both".`);
      if (value.customPool !== null && value.customPool !== undefined) problems.push(`${path}.hostileNpcs does not apply with a custom enemy pool.`);
    }
  }
  const roaming = (ROAMING_BOSS_REPLACEMENT_MODES as readonly unknown[]).includes(value.normalReplacement);
  if (Object.hasOwn(value, 'bossChanceInNormalSlots')) {
    const chance = value.bossChanceInNormalSlots;
    if (typeof chance !== 'number' || !Number.isInteger(chance) || chance < 0 || chance > 100) problems.push(`${path}.bossChanceInNormalSlots must be an integer from 0 to 100.`);
    else if (chance !== NEUTRAL_ENEMY_PLACEMENT_OPTIONS.bossChanceInNormalSlots && value.normalReplacement !== 'both') problems.push(`${path}.bossChanceInNormalSlots needs ${path}.normalReplacement "both".`);
  }
  if (Object.hasOwn(value, 'roamingBossSouls')) {
    const souls = value.roamingBossSouls;
    if (typeof souls !== 'number' || !Number.isInteger(souls) || souls < 0 || souls > 100) problems.push(`${path}.roamingBossSouls must be an integer from 0 to 100.`);
    else if (souls !== NEUTRAL_ENEMY_PLACEMENT_OPTIONS.roamingBossSouls && !roaming) problems.push(`${path}.roamingBossSouls needs ${path}.normalReplacement "bosses" or "both".`);
  }
  if (Object.hasOwn(value, 'roamingBossesStayDead')) {
    if (typeof value.roamingBossesStayDead !== 'boolean') problems.push(`${path}.roamingBossesStayDead must be true or false.`);
    else if (value.roamingBossesStayDead && !roaming) problems.push(`${path}.roamingBossesStayDead needs ${path}.normalReplacement "bosses" or "both".`);
  }
  if (Object.hasOwn(value, 'customPool')) {
    const pool = value.customPool;
    if (pool !== null) {
      if (!Array.isArray(pool)) {
        problems.push(`${path}.customPool must be null or a list of enemy model ids.`);
      } else if (pool.length === 0) {
        problems.push(`${path}.customPool is empty: select at least one enemy, or use the full pool (null).`);
      } else if (pool.length > MAX_CUSTOM_POOL_ENTRIES) {
        problems.push(`${path}.customPool lists more than ${MAX_CUSTOM_POOL_ENTRIES} enemies.`);
      } else {
        const bad = pool.filter((entry) => typeof entry !== 'string' || !MODEL_ID.test(entry));
        if (bad.length > 0) problems.push(`${path}.customPool entries must be model ids such as c2500 (got ${bad.slice(0, 3).map(String).join(', ')}).`);
        const strings = pool.filter((entry): entry is string => typeof entry === 'string');
        const duplicates = [...new Set(strings.filter((entry, index) => strings.indexOf(entry) !== index))];
        if (duplicates.length > 0) problems.push(`${path}.customPool lists ${duplicates.join(', ')} more than once.`);
      }
    }
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
  const required = new Set(['algorithm', ...samplingOnly.map((option) => option.key)]);
  const allowed = new Set([...required, ...(samplingOnly === SAMPLING_ONLY_OPTIONS ? EXTENSION_OPTIONS.map((option) => option.key) : [])]);
  for (const key of required) {
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
    for (const option of [...samplingOnly, ...(samplingOnly === SAMPLING_ONLY_OPTIONS ? EXTENSION_OPTIONS : [])]) {
      if (Object.hasOwn(value, option.key) && JSON.stringify(value[option.key]) !== JSON.stringify(option.neutral)) {
        problems.push(`${path}.${option.key} only applies to sampling-v1; it must be ${JSON.stringify(option.neutral)} with permutation-v1.`);
      }
    }
  }
}
