import type { RandomizerConfig } from '@ds1tr/contracts';
import { validateEnemyPlacement } from './enemyPlacement.js';

const topLevelKeys = ['gamePath', 'masterSeed', 'items', 'enemies', 'fog', 'output'] as const;
const itemKeys = [
  'enabled', 'randomizeWorldItems', 'randomizeProtectedItems', 'progressionLogic',
  'guaranteeEarlyWeapon', 'balancedEarlyLoot', 'randomizeStartingClass',
  'randomizeStartingEquipment', 'randomizeGifts', 'randomizeEnemyDrops',
  'guaranteedEnemyDrops', 'randomizeShops', 'fashionSouls', 'soulItems', 'startingItems',
] as const;
/** Item options added after format 1 (Part H): optional on input (an older configuration means their neutral value), validated when present. */
const itemExtensionKeys = ['keyPlacement', 'randomizeLordvessel', 'randomizeLordSouls', 'itemDifficulty', 'laundromat'] as const;
const keyPlacementModes = new Set(['vanilla', 'shuffled', 'race', 'race-plus']);
const itemDifficultyModes = new Set(['fair', 'unfair', 'very-unfair']);
const enemyKeys = [
  'enabled', 'randomizeRegularEnemies', 'randomizeBosses', 'scaling',
  'replaceMimics', 'replaceRespawningMosquitoes', 'preventSameEnemy', 'pinwheelChaos', 'gwynNerf', 'secondGargoyle', 'placement',
] as const;
const fogKeys = [
  'enabled', 'traversableFogs', 'bossFogs', 'areaWarps', 'majorPvpFogs',
  'minorPvpFogs', 'lordvesselGates', 'randomStart', 'disconnectedFogs',
  'noFallBedOfChaos', 'scaling', 'glitchedLogic', 'requireLordSouls',
  'pacifistMode', 'language',
] as const;
const outputKeys = ['includeDlc', 'generateSpoilerLog'] as const;
const soulItemModes = new Set(['shuffled', 'replaced', 'transposed']);
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
    config.items.randomizeShops ||
    config.items.laundromat
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
    validateKeys(section, key === 'items' ? [...expected, ...itemExtensionKeys.filter((optional) => Object.hasOwn(section, optional))] : expected, `config.${key}`, problems);
  }

  if (isObject(input.items)) {
    validateBooleans(input.items, itemKeys.filter((key) => key !== 'soulItems' && key !== 'startingItems'), 'config.items', problems);
    if (!soulItemModes.has(String(input.items.soulItems))) problems.push('Soul items mode is invalid.');
    if (!startingItemModes.has(String(input.items.startingItems))) problems.push('Starting items mode is invalid.');
    const keyMode = input.items.keyPlacement ?? 'vanilla';
    if (!keyPlacementModes.has(String(keyMode))) problems.push('Key placement mode is invalid.');
    const difficulty = input.items.itemDifficulty ?? 'fair';
    if (!itemDifficultyModes.has(String(difficulty))) problems.push('Item difficulty mode is invalid.');
    for (const optional of ['randomizeLordvessel', 'randomizeLordSouls', 'laundromat'] as const) {
      if (Object.hasOwn(input.items, optional) && typeof input.items[optional] !== 'boolean') problems.push(`config.items.${optional} must be a boolean.`);
    }
    if (keyMode === 'race-plus' && difficulty !== 'fair') problems.push('Race Mode + is only available with Fair item difficulty.');
    if (keyMode !== 'vanilla') {
      if (!input.items.enabled || !input.items.randomizeWorldItems) problems.push('Key placement needs the world items: turn on Items and World items.');
      if (input.items.randomizeProtectedItems) problems.push('Key placement and protected-item randomization cannot be combined.');
    } else if (input.items.randomizeLordvessel === true || input.items.randomizeLordSouls === true) {
      problems.push('The Lordvessel and Lord Souls switches need key placement other than Vanilla.');
    }
  }
  if (isObject(input.enemies)) {
    validateBooleans(input.enemies, enemyKeys.filter((key) => key !== 'scaling' && key !== 'gwynNerf' && key !== 'secondGargoyle' && key !== 'placement'), 'config.enemies', problems);
    if (Object.hasOwn(input.enemies, 'placement')) validateEnemyPlacement(input.enemies.placement, problems);
    if (!gwynNerfModes.has(String(input.enemies.gwynNerf))) problems.push('Gwyn nerf mode is invalid.');
    if (!secondGargoyleModes.has(String(input.enemies.secondGargoyle))) problems.push('Second Gargoyle mode is invalid.');
    if (!['vanilla', 'area', 'progressive'].includes(String(input.enemies.scaling))) {
      problems.push('Enemy scaling mode is invalid.');
    }
  }
  if (isObject(input.fog)) {
    validateBooleans(input.fog, fogKeys.filter((key) => key !== 'language'), 'config.fog', problems);
    if (typeof input.fog.language !== 'string' || !fogLanguages.has(input.fog.language)) {
      problems.push('Game language is invalid.');
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

