import { DEFAULT_ENEMY_PLACEMENT_ALGORITHM, NEUTRAL_ENEMY_PLACEMENT_OPTIONS, PRESET_FORMAT, PRESET_VERSION } from '@ds1tr/contracts';
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
/**
 * Version 6: the sampling-only placement settings, nested in `enemies.placement`. Every older preset migrates to
 * the neutral values, under which permutation-v1 behaves exactly as before.
 */
const placementBatch5Defaults: Record<string, unknown> = {
  replacementChance: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.replacementChance,
  normalReplacement: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.normalReplacement,
  fit: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.fit,
};
/** Version 7: the difficulty settings, again nested in `enemies.placement`; older presets migrate to the neutral values. */
const placementBatch6Defaults: Record<string, unknown> = {
  difficultyMode: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.difficultyMode,
  difficultyStrictness: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.difficultyStrictness,
};
/** Version 8: Type replacement; older presets migrate to `disabled`. */
const placementBatch7Defaults: Record<string, unknown> = { typeReplacement: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.typeReplacement };
/** Version 9: the custom enemy pool; older presets migrate to the full pool (null). */
const placementBatch8Defaults: Record<string, unknown> = { customPool: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.customPool };
/** Version 10: boss sampling (Part D); older presets migrate to the classic boss planner. */
const placementBatch9Defaults: Record<string, unknown> = {
  bossReplacement: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.bossReplacement,
  uniqueBosses: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.uniqueBosses,
};
/** Version 11: bosses in regular enemy slots (Part E1); older presets migrate to the legacy defaults, which do nothing without roaming bosses. */
const placementBatch10Defaults: Record<string, unknown> = {
  bossChanceInNormalSlots: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.bossChanceInNormalSlots,
  roamingBossSouls: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.roamingBossSouls,
  roamingBossesStayDead: NEUTRAL_ENEMY_PLACEMENT_OPTIONS.roamingBossesStayDead,
};
/** Nested `enemies.placement` keys added by each preset version, applied to every older preset. */
const placementKeysAddedIn: Array<{ version: number; defaults: Record<string, unknown> }> = [
  { version: 6, defaults: placementBatch5Defaults },
  { version: 7, defaults: placementBatch6Defaults },
  { version: 8, defaults: placementBatch7Defaults },
  { version: 9, defaults: placementBatch8Defaults },
  { version: 10, defaults: placementBatch9Defaults },
  { version: 11, defaults: placementBatch10Defaults },
];
/** What each older preset version lacks, newest additions last (the nested v6 placement keys are handled apart). */
const missingByVersion: Record<number, SectionDefaults[]> = {
  1: [batch1Defaults, batch2Defaults, batch3Defaults, batch4Defaults],
  2: [batch2Defaults, batch3Defaults, batch4Defaults],
  3: [batch3Defaults, batch4Defaults],
  4: [batch4Defaults],
  5: [],
  6: [],
  7: [],
  8: [],
  9: [],
  10: [],
};

const presetKeys = ['format', 'presetVersion', 'masterSeed', 'items', 'enemies', 'fog', 'output'];

/** Validates through the same authority as a run request, so a preset can never carry unsupported options. */
/** The custom pool is stored sorted, so equal selections export identically whatever order the UI built them in. */
function canonicalEnemies(enemies: RandomizerConfig['enemies']): RandomizerConfig['enemies'] {
  const pool = enemies.placement.customPool;
  // Options added later are always stated in an exported preset (an older configuration means their neutral value).
  const placement = {
    ...enemies.placement,
    bossReplacement: enemies.placement.bossReplacement ?? NEUTRAL_ENEMY_PLACEMENT_OPTIONS.bossReplacement,
    uniqueBosses: enemies.placement.uniqueBosses ?? NEUTRAL_ENEMY_PLACEMENT_OPTIONS.uniqueBosses,
    bossChanceInNormalSlots: enemies.placement.bossChanceInNormalSlots ?? NEUTRAL_ENEMY_PLACEMENT_OPTIONS.bossChanceInNormalSlots,
    roamingBossSouls: enemies.placement.roamingBossSouls ?? NEUTRAL_ENEMY_PLACEMENT_OPTIONS.roamingBossSouls,
    roamingBossesStayDead: enemies.placement.roamingBossesStayDead ?? NEUTRAL_ENEMY_PLACEMENT_OPTIONS.roamingBossesStayDead,
  };
  return { ...enemies, placement: pool === null ? placement : { ...placement, customPool: [...pool].sort() } };
}

export function exportPreset(config: RandomizerConfig): PresetDocument {
  validateRandomizerConfig(config);
  return {
    format: PRESET_FORMAT, presetVersion: PRESET_VERSION, masterSeed: config.masterSeed.trim(),
    items: config.items, enemies: canonicalEnemies(config.enemies), fog: config.fog, output: config.output,
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
    problems.push(`Preset version must be ${PRESET_VERSION} (versions 1 to 10 are migrated).`);
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
    // Versions 6 and 7 nest the sampling-only settings inside `enemies.placement`; fill in the ones an older preset
    // lacks without touching the algorithm, and reject a document that already carries a key from a newer version.
    const enemies = settings.enemies;
    for (const added of placementKeysAddedIn) {
      if (presetVersion >= added.version) continue;
      const placement = enemies && typeof enemies === 'object' && !Array.isArray(enemies) ? (enemies as Record<string, unknown>).placement : undefined;
      if (!placement || typeof placement !== 'object' || Array.isArray(placement)) continue;
      for (const key of Object.keys(added.defaults)) {
        if (Object.hasOwn(placement, key)) problems.push(`preset.enemies.placement.${key} is not supported in version ${presetVersion}.`);
      }
      (enemies as Record<string, unknown>).placement = { ...(placement as Record<string, unknown>), ...structuredClone(added.defaults) };
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
