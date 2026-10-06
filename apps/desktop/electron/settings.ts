/** Application settings kept in userData/settings.json. */
export interface StoredSettings {
  /** The Dark Souls Remastered folder the person chose by hand and that passed the installation check; it wins over detection. */
  gamePath?: string;
}

/**
 * The canonical form of a folder the person chose: absolute, normalized separators, no trailing separator. Anything else is not a
 * usable installation folder and is not remembered.
 */
export function normalizeGamePath(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  if (!/^[A-Za-z]:[\\/]/u.test(trimmed) && !trimmed.startsWith('\\\\')) return null;
  const normalized = trimmed.replace(/\//gu, '\\').replace(/\\+$/u, '');
  return normalized.length > 3 ? normalized : null;
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
