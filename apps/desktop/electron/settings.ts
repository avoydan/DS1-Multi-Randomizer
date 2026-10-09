import { normalizeLanguageSetting, type LanguageSetting } from './language.js';

/** Application settings kept in userData/settings.json. */
export interface StoredSettings {
  /** The Dark Souls Remastered folder the person chose by hand and that passed the installation check; it wins over detection. */
  gamePath?: string;
  /** The language the person chose (Auto follows the operating system). Absent means Auto. Never part of a world. */
  language?: LanguageSetting;
  /** Application updates (docs/AUTO_UPDATE.md). Absent means the defaults: automatic checks ON, nothing checked yet. Never part of a world. */
  updates?: StoredUpdateSettings;
}

export interface StoredUpdateSettings {
  /** Only ever stored as `false`: automatic checks are ON unless the person turned them off. */
  autoCheck?: false;
  /** When the last check ended, ISO 8601, and whether it succeeded (a failed one is retried after an hour, a successful one after a day). */
  lastCheckAt?: string;
  lastCheckOk?: boolean;
}

/** The update settings of any older or damaged file: only a valid date and booleans survive. */
export function normalizeUpdateSettings(value: unknown): StoredUpdateSettings | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null;
  const source = value as Record<string, unknown>;
  const result: StoredUpdateSettings = {};
  if (source.autoCheck === false) result.autoCheck = false;
  if (typeof source.lastCheckAt === 'string' && Number.isFinite(Date.parse(source.lastCheckAt))) {
    result.lastCheckAt = new Date(Date.parse(source.lastCheckAt)).toISOString();
    result.lastCheckOk = source.lastCheckOk === true;
  }
  return Object.keys(result).length > 0 ? result : null;
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
  const language = normalizeLanguageSetting(source.language);
  if (language && language !== 'auto') settings.language = language;
  const updates = normalizeUpdateSettings(source.updates);
  if (updates) settings.updates = updates;
  const keptKeys = Object.keys(settings);
  const changed = Object.keys(source).length !== keptKeys.length || Object.keys(source).some((key) => !keptKeys.includes(key));
  return { settings, changed };
}
