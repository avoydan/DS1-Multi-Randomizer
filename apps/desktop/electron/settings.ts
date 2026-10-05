/** Application settings kept in userData/settings.json. */
export interface StoredSettings {
  /** The Dark Souls Remastered folder the person last chose (only when automatic detection did not apply). */
  gamePath?: string;
}

/**
 * Reads a settings file of any older version. Earlier builds stored a `fogDataRoot` folder the person had to pick; the
 * application now prepares that data itself, so the key is dropped (`changed` tells the caller to rewrite the file).
 * Unknown keys and malformed values are discarded; the result never carries a machine-specific tool path.
 */
export function migrateSettings(raw: unknown): { settings: StoredSettings; changed: boolean } {
  const source = typeof raw === 'object' && raw !== null && !Array.isArray(raw) ? raw as Record<string, unknown> : {};
  const settings: StoredSettings = {};
  if (typeof source.gamePath === 'string' && source.gamePath.trim() !== '') settings.gamePath = source.gamePath;
  const keptKeys = Object.keys(settings);
  const changed = Object.keys(source).length !== keptKeys.length || Object.keys(source).some((key) => !keptKeys.includes(key));
  return { settings, changed };
}
