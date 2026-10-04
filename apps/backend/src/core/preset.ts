import { PRESET_FORMAT, PRESET_VERSION } from '@ds1tr/contracts';
import type { PresetDocument, PresetImportResult, RandomizerConfig } from '@ds1tr/contracts';
import { InvalidConfigurationError, validateRandomizerConfig } from './configValidation.js';

/** Version-1 presets predate the Batch 1 options; these are the legacy defaults that reproduce their output. */
const batch1Defaults = {
  items: { fashionSouls: true },
  enemies: { replaceMimics: true, replaceRespawningMosquitoes: true, preventSameEnemy: false },
} as const;

const presetKeys = ['format', 'presetVersion', 'masterSeed', 'items', 'enemies', 'fog', 'output'];

/** Validates through the same authority as a run request, so a preset can never carry unsupported options. */
export function exportPreset(config: RandomizerConfig): PresetDocument {
  validateRandomizerConfig(config);
  return {
    format: PRESET_FORMAT, presetVersion: PRESET_VERSION, masterSeed: config.masterSeed.trim(),
    items: config.items, enemies: config.enemies, fog: config.fog, output: config.output,
  };
}

export function importPreset(text: string): PresetImportResult {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { ok: false, problems: ['Preset is not valid JSON.'] };
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return { ok: false, problems: ['Preset must be a JSON object.'] };
  }
  const record = parsed as Record<string, unknown>;
  const problems: string[] = [];
  if (record.format !== PRESET_FORMAT) problems.push(`Preset format must be "${PRESET_FORMAT}".`);
  if (record.presetVersion !== PRESET_VERSION && record.presetVersion !== 1) {
    problems.push(`Preset version must be ${PRESET_VERSION} (version 1 is migrated).`);
  }
  for (const key of Object.keys(record)) {
    if (!presetKeys.includes(key)) problems.push(`preset.${key} is not supported.`);
  }
  if (problems.length > 0) return { ok: false, problems };

  const { format: _format, presetVersion, ...settings } = record;
  if (presetVersion === 1) {
    for (const [section, defaults] of Object.entries(batch1Defaults)) {
      const current = settings[section];
      if (!current || typeof current !== 'object' || Array.isArray(current)) continue;
      for (const key of Object.keys(defaults)) {
        if (Object.hasOwn(current, key)) problems.push(`preset.${section}.${key} is not supported in version 1.`);
      }
      settings[section] = { ...defaults, ...current };
    }
    if (problems.length > 0) return { ok: false, problems };
  }
  const candidate = { gamePath: '', ...settings };
  try {
    validateRandomizerConfig(candidate);
  } catch (error) {
    if (error instanceof InvalidConfigurationError) return { ok: false, problems: error.problems };
    throw error;
  }
  const config = candidate as unknown as RandomizerConfig;
  return { ok: true, preset: exportPreset(config) };
}
