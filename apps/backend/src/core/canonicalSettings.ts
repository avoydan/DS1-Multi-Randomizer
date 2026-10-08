import {
  CANONICAL_ENEMY_KEYS, CANONICAL_FOG_KEYS, CANONICAL_ITEM_EXTENSION_KEYS, CANONICAL_ITEM_KEYS, CANONICAL_OUTPUT_KEYS, CANONICAL_PLACEMENT_EXTENSION_KEYS,
  CANONICAL_PLACEMENT_KEYS,
} from '@ds1tr/contracts';
import type { GenerationSettings } from '@ds1tr/contracts';

/**
 * The canonical form of the settings that decide a world: every option present (defaults resolved by the caller, never
 * implied), keys in a fixed order, the custom enemy pool sorted by stable model id, nothing machine-specific. The same
 * semantic configuration always serializes to the same bytes, whatever order the UI or a preset built it in.
 */
type Json = Record<string, unknown>;

function pick(source: Json, keys: readonly string[], path: string, convert: Record<string, (value: unknown) => unknown> = {}): Json {
  const result: Json = {};
  for (const key of keys) {
    if (!Object.hasOwn(source, key)) throw new TypeError(`${path}.${key} is missing.`);
    result[key] = convert[key] ? convert[key](source[key]) : source[key];
  }
  const extra = Object.keys(source).filter((key) => !keys.includes(key));
  if (extra.length > 0) throw new TypeError(`${path} has unsupported ${extra.length > 1 ? 'options' : 'option'}: ${extra.join(', ')}.`);
  return result;
}

const object = (value: unknown, path: string): Json => {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new TypeError(`${path} must be an object.`);
  return value as Json;
};

/**
 * The complete canonical settings (every key present, fixed order). Placement options that were added after format 1 are filled with
 * their neutral value when the input predates them, so an old code, preset or settings file is complete again without changing
 * what it meant.
 */
export function canonicalizeSettings(input: unknown): GenerationSettings {
  const source = object(input, 'settings');
  const enemiesSource = object(source.enemies, 'settings.enemies');
  const placementSource = { ...object(enemiesSource.placement, 'settings.enemies.placement') };
  for (const { key, neutral } of CANONICAL_PLACEMENT_EXTENSION_KEYS) if (!Object.hasOwn(placementSource, key)) placementSource[key] = neutral;
  const placement = pick(placementSource, [...CANONICAL_PLACEMENT_KEYS, ...CANONICAL_PLACEMENT_EXTENSION_KEYS.map((entry) => entry.key)], 'settings.enemies.placement', {
    customPool: (pool) => (Array.isArray(pool) ? [...(pool as string[])].sort() : pool),
  });
  const enemies = pick(enemiesSource, CANONICAL_ENEMY_KEYS, 'settings.enemies', { placement: () => placement });
  const itemKeys = [...CANONICAL_ITEM_KEYS, ...CANONICAL_ITEM_EXTENSION_KEYS.map((entry) => entry.key)];
  const canonicalItems = (value: unknown): Json => {
    const itemsSource = { ...object(value, 'settings.items') };
    for (const { key, neutral } of CANONICAL_ITEM_EXTENSION_KEYS) if (!Object.hasOwn(itemsSource, key)) itemsSource[key] = neutral;
    return pick(itemsSource, itemKeys, 'settings.items');
  };
  // The DLC switch came after format 1: a code, preset or settings file without it was made with the DLC included.
  const outputSource = { ...object(source.output, 'settings.output') };
  if (!Object.hasOwn(outputSource, 'includeDlc')) outputSource.includeDlc = true;
  const top = pick({ ...source, output: outputSource }, ['items', 'enemies', 'fog', 'output'], 'settings', {
    items: canonicalItems,
    enemies: () => enemies,
    fog: (value) => pick(object(value, 'settings.fog'), CANONICAL_FOG_KEYS, 'settings.fog'),
    output: (value) => pick(object(value, 'settings.output'), CANONICAL_OUTPUT_KEYS, 'settings.output'),
  });
  return top as unknown as GenerationSettings;
}

/**
 * The settings as a Seed Code stores them: complete, except that a placement option added after format 1 is left out while it has its
 * neutral value. A code made before such an option existed therefore keeps its exact text.
 */
export function sparseCanonicalSettings(settings: GenerationSettings): Record<string, unknown> {
  const complete = canonicalizeSettings(settings) as unknown as { items: Record<string, unknown>; enemies: { placement: Record<string, unknown> } };
  const placement = { ...complete.enemies.placement };
  for (const { key, neutral } of CANONICAL_PLACEMENT_EXTENSION_KEYS) if (placement[key] === neutral) delete placement[key];
  const items = { ...complete.items };
  for (const { key, neutral } of CANONICAL_ITEM_EXTENSION_KEYS) if (items[key] === neutral) delete items[key];
  return { ...complete, items, enemies: { ...complete.enemies, placement } } as Record<string, unknown>;
}

/** Compact, order-stable JSON of the settings in their Seed Code form (see {@link sparseCanonicalSettings}). */
export function canonicalSettingsJson(settings: GenerationSettings): string {
  return JSON.stringify(sparseCanonicalSettings(settings));
}
