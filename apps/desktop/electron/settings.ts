import { normalizeLanguageSetting, type LanguageSetting } from './language.js';

/** Application settings kept in userData/settings.json. */
export interface StoredSettings {
  /** The Dark Souls Remastered folder the person chose by hand and that passed the installation check; it wins over detection. */
  gamePath?: string;
  /** The language the person chose (Auto follows the operating system). Absent means Auto. Never part of a world. */
  language?: LanguageSetting;
  /** Application updates (docs/AUTO_UPDATE.md). Absent means the defaults: automatic checks ON, nothing checked yet. Never part of a world. */
  updates?: StoredUpdateSettings;
  /** Local launcher convenience; never used by generation, presets, or Seed Codes. Absent means off. */
  freeCursorInMenus?: boolean;
  /** Local launcher hotkey for moving focus away from DSR; absent means F10. */
  freeCursorShortcut?: string;
}

const FREE_CURSOR_SHORTCUT_KEYS = new Set([
  'Space', 'Tab', 'Insert', 'Delete', 'Home', 'End', 'PageUp', 'PageDown', 'Up', 'Down', 'Left', 'Right', 'Enter', 'Backspace',
]);

export function normalizeFreeCursorShortcut(value: unknown): string | null {
  if (typeof value !== 'string' || value.trim() === '') return null;
  const parts = value.trim().split('+');
  if (parts.length < 1 || parts.length > 4 || parts.some((part) => part.trim() === '')) return null;
  const modifiers = new Set<string>();
  for (const raw of parts.slice(0, -1)) {
    const modifier = raw.trim().toLowerCase();
    if (modifier === 'ctrl' || modifier === 'control') modifiers.add('Ctrl');
    else if (modifier === 'alt') modifiers.add('Alt');
    else if (modifier === 'shift') modifiers.add('Shift');
    else return null;
  }
  if (modifiers.size !== parts.length - 1) return null;

  const rawKey = parts.at(-1)!.trim();
  const upperKey = rawKey.toUpperCase();
  const namedKey = [...FREE_CURSOR_SHORTCUT_KEYS].find((key) => key.toLowerCase() === rawKey.toLowerCase());
  const key = /^F(?:[1-9]|1[0-9]|2[0-4])$/u.test(upperKey) || /^[A-Z0-9]$/u.test(upperKey)
    ? upperKey
    : namedKey ?? null;
  if (!key) return null;
  return [...['Ctrl', 'Alt', 'Shift'].filter((modifier) => modifiers.has(modifier)), key].join('+');
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
  if (typeof source.freeCursorInMenus === 'boolean') settings.freeCursorInMenus = source.freeCursorInMenus;
  const freeCursorShortcut = normalizeFreeCursorShortcut(source.freeCursorShortcut);
  if (freeCursorShortcut) settings.freeCursorShortcut = freeCursorShortcut;
  const keptKeys = Object.keys(settings);
  const changed = Object.keys(source).length !== keptKeys.length || Object.keys(source).some((key) => !keptKeys.includes(key));
  return { settings, changed };
}
