import type { RandomizerConfig } from '@ds1tr/contracts';

const topLevelKeys = ['gamePath', 'masterSeed', 'items', 'enemies', 'fog', 'output'] as const;
const itemKeys = [
  'enabled', 'randomizeWorldItems', 'randomizeProtectedItems', 'progressionLogic',
  'guaranteeEarlyWeapon', 'balancedEarlyLoot', 'randomizeStartingClass',
  'randomizeStartingEquipment', 'randomizeGifts', 'randomizeEnemyDrops',
  'guaranteedEnemyDrops', 'randomizeShops', 'fashionSouls', 'soulItems', 'startingItems',
] as const;
const enemyKeys = [
  'enabled', 'randomizeRegularEnemies', 'randomizeBosses', 'scaling',
  'replaceMimics', 'replaceRespawningMosquitoes', 'preventSameEnemy', 'pinwheelChaos', 'gwynNerf', 'secondGargoyle',
] as const;
const fogKeys = [
  'enabled', 'traversableFogs', 'bossFogs', 'areaWarps', 'majorPvpFogs',
  'minorPvpFogs', 'lordvesselGates', 'randomStart', 'disconnectedFogs',
  'noFallBedOfChaos', 'scaling', 'glitchedLogic', 'requireLordSouls',
  'pacifistMode', 'language',
] as const;
const outputKeys = ['includeDlc', 'generateSpoilerLog'] as const;
const soulItemModes = new Set(['shuffled', 'replaced']);
const startingItemModes = new Set(['shield-1h', 'shield-2h', 'mixed-pool']);
const gwynNerfModes = new Set(['none', 'medium', 'strong']);
const secondGargoyleModes = new Set(['replace', 'keep', 'single-boss']);
const fogLanguages = new Set([
  'ENGLISH', 'FRENCH', 'GERMAN', 'ITALIAN', 'JAPANESE', 'KOREAN',
  'NSPANISH', 'POLISH', 'PORTUGUESE', 'RUSSIAN', 'SCHINESE', 'SPANISH',
  'TCHINESE',
]);

type JsonObject = Record<string, unknown>;

function isObject(value: unknown): value is JsonObject {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

function validateKeys(value: JsonObject, expected: readonly string[], path: string, problems: string[]): void {
  const expectedSet = new Set(expected);
  for (const key of expected) {
    if (!Object.hasOwn(value, key)) problems.push(`${path}.${key} is required.`);
  }
  for (const key of Object.keys(value)) {
    if (!expectedSet.has(key)) problems.push(`${path}.${key} is not supported.`);
  }
}

function validateBooleans(value: JsonObject, keys: readonly string[], path: string, problems: string[]): void {
  for (const key of keys) {
    if (typeof value[key] !== 'boolean') problems.push(`${path}.${key} must be a boolean.`);
  }
}

export class InvalidConfigurationError extends Error {
  readonly problems: string[];

  constructor(problems: string[]) {
    super(problems.join(' '));
    this.name = 'InvalidConfigurationError';
    this.problems = problems;
  }
}

export function effectiveCombinedEnabled(config: RandomizerConfig): boolean {
  const itemEnabled = config.items.enabled && (
    config.items.randomizeWorldItems ||
    config.items.randomizeStartingClass ||
    config.items.randomizeStartingEquipment ||
    config.items.randomizeGifts ||
    config.items.randomizeEnemyDrops ||
    config.items.guaranteedEnemyDrops ||
    config.items.randomizeShops
  );
  const enemyEnabled = config.enemies.enabled && (
    config.enemies.randomizeRegularEnemies || config.enemies.randomizeBosses
  );
  return itemEnabled || enemyEnabled;
}

export function validateRandomizerConfig(input: unknown): asserts input is RandomizerConfig {
  const problems: string[] = [];
  if (!isObject(input)) throw new InvalidConfigurationError(['Configuration must be an object.']);

  validateKeys(input, topLevelKeys, 'config', problems);
  if (typeof input.gamePath !== 'string') problems.push('config.gamePath must be a string.');
  if (typeof input.masterSeed !== 'string') {
    problems.push('config.masterSeed must be a string.');
  } else if (input.masterSeed.trim().length === 0) {
    problems.push('Master seed is required.');
  }

  for (const [key, expected] of [
    ['items', itemKeys],
    ['enemies', enemyKeys],
    ['fog', fogKeys],
    ['output', outputKeys],
  ] as const) {
    const section = input[key];
    if (!isObject(section)) {
      problems.push(`config.${key} must be an object.`);
      continue;
    }
    validateKeys(section, expected, `config.${key}`, problems);
  }

  if (isObject(input.items)) {
    validateBooleans(input.items, itemKeys.filter((key) => key !== 'soulItems' && key !== 'startingItems'), 'config.items', problems);
    if (!soulItemModes.has(String(input.items.soulItems))) problems.push('Soul items mode is invalid.');
    if (!startingItemModes.has(String(input.items.startingItems))) problems.push('Starting items mode is invalid.');
  }
  if (isObject(input.enemies)) {
    validateBooleans(input.enemies, enemyKeys.filter((key) => key !== 'scaling' && key !== 'gwynNerf' && key !== 'secondGargoyle'), 'config.enemies', problems);
    if (!gwynNerfModes.has(String(input.enemies.gwynNerf))) problems.push('Gwyn nerf mode is invalid.');
    if (!secondGargoyleModes.has(String(input.enemies.secondGargoyle))) problems.push('Second Gargoyle mode is invalid.');
    if (!['vanilla', 'area', 'progressive'].includes(String(input.enemies.scaling))) {
      problems.push('Enemy scaling mode is invalid.');
    }
  }
  if (isObject(input.fog)) {
    validateBooleans(input.fog, fogKeys.filter((key) => key !== 'language'), 'config.fog', problems);
    if (typeof input.fog.language !== 'string' || !fogLanguages.has(input.fog.language)) {
      problems.push('FogMod language is invalid.');
    }
  }
  if (isObject(input.output)) validateBooleans(input.output, outputKeys, 'config.output', problems);

  if (problems.length > 0) throw new InvalidConfigurationError(problems);

  const config = input as unknown as RandomizerConfig;
  if (config.items.randomizeProtectedItems) {
    problems.push('Protected-item randomization is blocked until progression safety is proven.');
  }
  if (!config.output.includeDlc) {
    // The engines have no DLC switch: nothing reads this flag, so accepting `false`
    // would silently ignore it.
    problems.push('Excluding DLC content is not supported yet; Include DLC must stay on.');
  }
  if (config.fog.randomStart && !config.fog.bossFogs && !config.fog.traversableFogs) {
    problems.push('A random start requires boss or traversable fog gates.');
  }
  if (!effectiveCombinedEnabled(config) && !config.fog.enabled) {
    problems.push('Enable at least one supported randomization capability.');
  }

  if (problems.length > 0) throw new InvalidConfigurationError(problems);
}

