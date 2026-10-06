import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { PERSISTED_SETTINGS_VERSION, joinConfig } from '@ds1tr/contracts';
import type { PersistedSettings } from '@ds1tr/contracts';
import { canonicalizeSettings } from './canonicalSettings.js';
import { validateRandomizerConfig } from './configValidation.js';

/**
 * The settings the person left in the app, kept between sessions (docs/SEED_FORMAT.md, "Settings persistence"). Only
 * generation settings and the spoiler preference: the game path is machine state and lives elsewhere. A file that is
 * missing, damaged or from a newer app falls back to the defaults with a short plain-language note, never an error.
 */
export type LoadedSettings =
  | { found: true; persisted: PersistedSettings; warning: null }
  | { found: false; persisted: null; warning: string | null };

const fileOf = (stateRoot: string): string => path.join(stateRoot, 'settings', 'ui-settings.json');

export async function loadPersistedSettings(stateRoot: string): Promise<LoadedSettings> {
  let text: string;
  try {
    text = await readFile(fileOf(stateRoot), 'utf8');
  } catch {
    return { found: false, persisted: null, warning: null };
  }
  const reset = (reason: string): LoadedSettings => ({
    found: false, persisted: null, warning: `Your saved settings could not be read (${reason}) and were reset to the defaults.`,
  });
  let parsed: unknown;
  try { parsed = JSON.parse(text); } catch { return reset('the file is damaged'); }
  if (typeof parsed !== 'object' || parsed === null) return reset('the file is damaged');
  const record = parsed as Record<string, unknown>;
  if (typeof record.schemaVersion !== 'number') return reset('the file is damaged');
  if (record.schemaVersion > PERSISTED_SETTINGS_VERSION) return reset('they were saved by a newer version of the app');
  // Version 1 is the only layout so far; a later version adds its migration here, before validation.
  try {
    const settings = canonicalizeSettings(record.settings);
    const spoiler = typeof record.generateSpoilerLog === 'boolean' ? record.generateSpoilerLog : false;
    validateRandomizerConfig(joinConfig(settings, 'validation-only', 'validation-only', spoiler));
    return { found: true, persisted: { schemaVersion: PERSISTED_SETTINGS_VERSION, settings, generateSpoilerLog: spoiler }, warning: null };
  } catch {
    return reset('some options are no longer valid');
  }
}

export async function savePersistedSettings(stateRoot: string, value: Pick<PersistedSettings, 'settings' | 'generateSpoilerLog'>): Promise<void> {
  const settings = canonicalizeSettings(value.settings);
  validateRandomizerConfig(joinConfig(settings, 'validation-only', 'validation-only', value.generateSpoilerLog));
  const document: PersistedSettings = { schemaVersion: PERSISTED_SETTINGS_VERSION, settings, generateSpoilerLog: value.generateSpoilerLog };
  const file = fileOf(stateRoot);
  await mkdir(path.dirname(file), { recursive: true });
  const temporary = `${file}.${process.pid}.tmp`;
  await writeFile(temporary, JSON.stringify(document, null, 2));
  await rename(temporary, file);
}
