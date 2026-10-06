import {
  CANONICAL_ENEMY_KEYS, CANONICAL_FOG_KEYS, CANONICAL_ITEM_KEYS, CANONICAL_OUTPUT_KEYS, CANONICAL_PLACEMENT_KEYS,
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

export function canonicalizeSettings(input: unknown): GenerationSettings {
  const source = object(input, 'settings');
  const enemiesSource = object(source.enemies, 'settings.enemies');
  const placement = pick(object(enemiesSource.placement, 'settings.enemies.placement'), CANONICAL_PLACEMENT_KEYS, 'settings.enemies.placement', {
    customPool: (pool) => (Array.isArray(pool) ? [...(pool as string[])].sort() : pool),
  });
  const enemies = pick(enemiesSource, CANONICAL_ENEMY_KEYS, 'settings.enemies', { placement: () => placement });
  const top = pick(source, ['items', 'enemies', 'fog', 'output'], 'settings', {
    items: (value) => pick(object(value, 'settings.items'), CANONICAL_ITEM_KEYS, 'settings.items'),
    enemies: () => enemies,
    fog: (value) => pick(object(value, 'settings.fog'), CANONICAL_FOG_KEYS, 'settings.fog'),
    output: (value) => pick(object(value, 'settings.output'), CANONICAL_OUTPUT_KEYS, 'settings.output'),
  });
  return top as unknown as GenerationSettings;
}

/** Compact, order-stable JSON of the canonical settings. */
export function canonicalSettingsJson(settings: GenerationSettings): string {
  return JSON.stringify(canonicalizeSettings(settings));
}
