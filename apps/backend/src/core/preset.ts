import { DEFAULT_ENEMY_PLACEMENT_ALGORITHM, PRESET_FORMAT, PRESET_VERSION } from '@ds1tr/contracts';
import type { PresetDocument, PresetImportResult, RandomizerConfig } from '@ds1tr/contracts';
import { InvalidConfigurationError, validateRandomizerConfig } from './configValidation.js';

type SectionDefaults = Record<string, Record<string, unknown>>;

/** Older presets predate these options; the values reproduce the output the old preset had. */
const batch1Defaults: SectionDefaults = {
  items: { fashionSouls: true },
  enemies: { replaceMimics: true, replaceRespawningMosquitoes: true, preventSameEnemy: false },
};
/** Batch 2 values that keep the pre-Batch-2 engine behavior (not the new legacy defaults). */
const batch2Defaults: SectionDefaults = {
  items: { soulItems: 'shuffled', startingItems: 'mixed-pool' },
  enemies: { pinwheelChaos: false, gwynNerf: 'none' },
};
/** Batch 3: the earlier DS1TR Bell Gargoyle behavior (extra bodies disabled), not the new legacy default. */
const batch3Defaults: SectionDefaults = {
  enemies: { secondGargoyle: 'single-boss' },
};
/**
 * Version 5: the enemy placement algorithm. Every older preset was produced by the count-preserving permutation,
 * so it migrates to `permutation-v1` explicitly; it is never opted into `sampling-v1`.
 */
const batch4Defaults: SectionDefaults = {
  enemies: { placement: { algorithm: DEFAULT_ENEMY_PLACEMENT_ALGORITHM } },
};
/** What each older preset version lacks, newest additions last. */
const missingByVersion: Record<number, SectionDefaults[]> = {
  1: [batch1Defaults, batch2Defaults, batch3Defaults, batch4Defaults],
  2: [batch2Defaults, batch3Defaults, batch4Defaults],
  3: [batch3Defaults, batch4Defaults],
  4: [batch4Defaults],
};

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
  if (record.presetVersion !== PRESET_VERSION && !(typeof record.presetVersion === 'number' && missingByVersion[record.presetVersion])) {
    problems.push(`Preset version must be ${PRESET_VERSION} (versions 1 to 4 are migrated).`);
  }
  for (const key of Object.keys(record)) {
    if (!presetKeys.includes(key)) problems.push(`preset.${key} is not supported.`);
  }
  if (problems.length > 0) return { ok: false, problems };

  const { format: _format, presetVersion, ...settings } = record;
  if (typeof presetVersion === 'number' && missingByVersion[presetVersion]) {
    for (const defaultsSet of missingByVersion[presetVersion]) {
      for (const [section, defaults] of Object.entries(defaultsSet)) {
        const current = settings[section];
        if (!current || typeof current !== 'object' || Array.isArray(current)) continue;
        for (const key of Object.keys(defaults)) {
          if (Object.hasOwn(current, key)) problems.push(`preset.${section}.${key} is not supported in version ${presetVersion}.`);
        }
        settings[section] = { ...structuredClone(defaults), ...(current as Record<string, unknown>) };
      }
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
