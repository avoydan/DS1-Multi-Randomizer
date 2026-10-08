import type {
  BossReplacementMode, EnemyPlacementAlgorithm, FogLanguage, GwynNerfMode, ItemDifficultyMode, KeyPlacementMode, NormalEnemyReplacementMode, RandomizerConfig, SecondGargoyleMode,
  SoulItemsMode, StartingItemsMode,
} from './index.js';
import { DEFAULT_GENERATION_SETTINGS } from './smokeTests.js';

/**
 * The registry of every player-facing setting (docs/SETTINGS_INVENTORY.md): which config field it is, where the interface shows it, when it is available and why not,
 * and what happens to other fields when it changes. The labels and the help texts live in the translation resources under the same id (`settings.<id>` and
 * `help.settings.<id>`); this file holds only structure and rules, so the interface, the tests and the documentation read one source and cannot drift apart.
 *
 * Nothing here decides what a world is. A rule here only says what the interface offers; the backend validates the same combinations independently
 * (`configValidation.ts`), and the generation never reads this file. Changing a label or a help text can never change a Seed Code.
 */

export type SettingKind = 'toggle' | 'select' | 'range' | 'pool';

/** The sections of the advanced view, in the order they are shown. */
export const SECTION_IDS = ['items', 'enemies', 'bosses', 'transitions', 'keys', 'classic', 'other'] as const;
export type SectionId = (typeof SECTION_IDS)[number];

/** The three cards of the basic view. */
export type BasicGroup = 'main' | 'common' | 'world';

export interface SettingDef {
  /** The translation key stem (`settings.<id>`, `help.settings.<id>`). Never changes once shipped: the help of a setting is found by it. */
  id: string;
  /** The field of the configuration (`items.randomizeWorldItems`). */
  path: string;
  kind: SettingKind;
  section: SectionId;
  /** The serialized values of a select, in the order shown. */
  choices?: readonly string[];
  /** A switch that turns a whole category on or off. */
  master?: boolean;
  /** Where the basic view shows it; absent means advanced only. */
  basic?: BasicGroup;
  /** Not drawn at all, and why. The field stays in the configuration, in presets and in Seed Codes; only the control is gone. */
  hidden?: 'no-effect' | 'refused';
  /** The choices are names (languages) that need no explanation. */
  plainChoices?: boolean;
  /** How sure the help text is: `verified` is read from the code that does it, `partial` is read from the code but the exact in-game effect was not observed. */
  evidence: 'verified' | 'partial';
}

const toggle = (id: string, path: string, section: SectionId, extra: Partial<SettingDef> = {}): SettingDef => ({ id, path, kind: 'toggle', section, evidence: 'verified', ...extra });
const select = (id: string, path: string, section: SectionId, choices: readonly string[], extra: Partial<SettingDef> = {}): SettingDef => ({ id, path, kind: 'select', section, choices, evidence: 'verified', ...extra });
const range = (id: string, path: string, section: SectionId, extra: Partial<SettingDef> = {}): SettingDef => ({ id, path, kind: 'range', section, evidence: 'verified', ...extra });

export const FOG_LANGUAGES: readonly FogLanguage[] = ['ENGLISH', 'FRENCH', 'GERMAN', 'ITALIAN', 'JAPANESE', 'KOREAN', 'NSPANISH', 'POLISH', 'PORTUGUESE', 'RUSSIAN', 'SCHINESE', 'SPANISH', 'TCHINESE'];

export const SETTINGS: readonly SettingDef[] = [
  // --- Items
  toggle('items.enabled', 'items.enabled', 'items', { master: true, basic: 'main' }),
  toggle('items.worldItems', 'items.randomizeWorldItems', 'items'),
  toggle('items.progressionLogic', 'items.progressionLogic', 'items'),
  toggle('items.npcGifts', 'items.randomizeGifts', 'items', { basic: 'common' }),
  toggle('items.enemyDrops', 'items.randomizeEnemyDrops', 'items'),
  toggle('items.guaranteedDrops', 'items.guaranteedEnemyDrops', 'items'),
  toggle('items.shops', 'items.randomizeShops', 'items', { basic: 'common' }),
  toggle('items.startingClass', 'items.randomizeStartingClass', 'items'),
  toggle('items.startingEquipment', 'items.randomizeStartingEquipment', 'items'),
  select('items.startingItems', 'items.startingItems', 'items', ['shield-1h', 'shield-2h', 'mixed-pool'] satisfies StartingItemsMode[]),
  // Controls that cannot do anything in this version: the field stays (Seed Codes and presets carry it), the control is not drawn.
  toggle('items.earlyWeapon', 'items.guaranteeEarlyWeapon', 'items', { hidden: 'no-effect' }),
  toggle('items.balancedEarlyLoot', 'items.balancedEarlyLoot', 'items', { hidden: 'no-effect' }),
  toggle('items.protectedItems', 'items.randomizeProtectedItems', 'items', { hidden: 'refused' }),
  // --- Keys and progression
  select('items.keyPlacement', 'items.keyPlacement', 'keys', ['vanilla', 'shuffled', 'race', 'race-plus'] satisfies KeyPlacementMode[]),
  toggle('items.lordvessel', 'items.randomizeLordvessel', 'keys'),
  toggle('items.lordSouls', 'items.randomizeLordSouls', 'keys'),
  select('items.difficulty', 'items.itemDifficulty', 'keys', ['fair', 'unfair', 'very-unfair'] satisfies ItemDifficultyMode[]),
  // --- Classic options
  select('items.soulItems', 'items.soulItems', 'classic', ['shuffled', 'replaced', 'transposed'] satisfies SoulItemsMode[]),
  toggle('items.fashionSouls', 'items.fashionSouls', 'classic'),
  toggle('items.laundromat', 'items.laundromat', 'classic'),
  // --- Enemies
  toggle('enemies.enabled', 'enemies.enabled', 'enemies', { master: true, basic: 'main' }),
  toggle('enemies.regular', 'enemies.randomizeRegularEnemies', 'enemies'),
  select('enemies.scaling', 'enemies.scaling', 'enemies', ['vanilla', 'area', 'progressive']),
  select('enemies.algorithm', 'enemies.placement.algorithm', 'enemies', ['sampling-v1', 'permutation-v1'] satisfies EnemyPlacementAlgorithm[]),
  range('enemies.replacementChance', 'enemies.placement.replacementChance', 'enemies'),
  select('enemies.normalReplacement', 'enemies.placement.normalReplacement', 'enemies', ['none', 'normals', 'bosses', 'both'] satisfies NormalEnemyReplacementMode[]),
  select('enemies.fit', 'enemies.placement.fit', 'enemies', ['fits', 'anywhere', 'anywhere-except-npcs']),
  select('enemies.difficultyMode', 'enemies.placement.difficultyMode', 'enemies', ['curve-easy-asylum', 'curve', 'random-easy-asylum', 'random']),
  select('enemies.strictness', 'enemies.placement.difficultyStrictness', 'enemies', ['strict', 'loose', 'very-loose']),
  select('enemies.typeReplacement', 'enemies.placement.typeReplacement', 'enemies', ['enabled', 'enabled-except-roaming-bosses', 'disabled']),
  { id: 'enemies.customPool', path: 'enemies.placement.customPool', kind: 'pool', section: 'enemies', evidence: 'verified' },
  // --- Bosses
  toggle('enemies.bosses', 'enemies.randomizeBosses', 'bosses', { basic: 'main' }),
  select('enemies.bossReplacement', 'enemies.placement.bossReplacement', 'bosses', ['classic', 'bosses', 'normals', 'both'] satisfies BossReplacementMode[]),
  range('enemies.bossChanceBoss', 'enemies.placement.bossChanceInBossSlots', 'bosses'),
  toggle('enemies.uniqueBosses', 'enemies.placement.uniqueBosses', 'bosses'),
  range('enemies.bossChanceNormal', 'enemies.placement.bossChanceInNormalSlots', 'bosses'),
  range('enemies.roamingSouls', 'enemies.placement.roamingBossSouls', 'bosses'),
  toggle('enemies.roamingDead', 'enemies.placement.roamingBossesStayDead', 'bosses'),
  // --- Classic enemy options
  toggle('enemies.mimics', 'enemies.replaceMimics', 'classic'),
  toggle('enemies.hostileNpcs', 'enemies.placement.hostileNpcs', 'classic'),
  toggle('enemies.mosquitoes', 'enemies.replaceRespawningMosquitoes', 'classic'),
  toggle('enemies.sameEnemy', 'enemies.preventSameEnemy', 'classic'),
  toggle('enemies.pinwheel', 'enemies.pinwheelChaos', 'classic'),
  select('enemies.gargoyle', 'enemies.secondGargoyle', 'classic', ['replace', 'keep', 'single-boss'] satisfies SecondGargoyleMode[]),
  select('enemies.gwyn', 'enemies.gwynNerf', 'classic', ['strong', 'medium', 'none'] satisfies GwynNerfMode[]),
  // --- Transitions and fog gates
  toggle('fog.enabled', 'fog.enabled', 'transitions', { master: true, basic: 'main' }),
  toggle('fog.traversable', 'fog.traversableFogs', 'transitions'),
  toggle('fog.bossFogs', 'fog.bossFogs', 'transitions'),
  toggle('fog.warps', 'fog.areaWarps', 'transitions'),
  toggle('fog.majorPvp', 'fog.majorPvpFogs', 'transitions', { evidence: 'partial' }),
  toggle('fog.minorPvp', 'fog.minorPvpFogs', 'transitions', { evidence: 'partial' }),
  toggle('fog.lordvesselGates', 'fog.lordvesselGates', 'transitions'),
  toggle('fog.randomStart', 'fog.randomStart', 'transitions'),
  toggle('fog.disconnected', 'fog.disconnectedFogs', 'transitions'),
  toggle('fog.noFall', 'fog.noFallBedOfChaos', 'transitions', { evidence: 'partial' }),
  toggle('fog.scaling', 'fog.scaling', 'transitions'),
  toggle('fog.glitched', 'fog.glitchedLogic', 'transitions'),
  toggle('fog.requireLordSouls', 'fog.requireLordSouls', 'transitions'),
  toggle('fog.pacifist', 'fog.pacifistMode', 'transitions', { evidence: 'partial' }),
  select('fog.gameLanguage', 'fog.language', 'transitions', FOG_LANGUAGES, { plainChoices: true }),
  // --- World and output
  toggle('output.dlc', 'output.includeDlc', 'other', { basic: 'world' }),
  toggle('output.spoiler', 'output.generateSpoilerLog', 'other', { basic: 'world' }),
];

const byId = new Map(SETTINGS.map((setting) => [setting.id, setting]));
export const settingById = (id: string): SettingDef => {
  const found = byId.get(id);
  if (!found) throw new Error(`Unknown setting ${id}.`);
  return found;
};

/** The settings the interface draws (the hidden ones are not). */
export const VISIBLE_SETTINGS: readonly SettingDef[] = SETTINGS.filter((setting) => !setting.hidden);

export const settingsOfSection = (section: SectionId): SettingDef[] => VISIBLE_SETTINGS.filter((setting) => setting.section === section);

// --- reading and writing the configuration by path (immutable) -------------------------------------------------------------------------------------------------

export function getByPath(config: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>((value, key) => (value !== null && typeof value === 'object' ? (value as Record<string, unknown>)[key] : undefined), config);
}

export function setByPath<T>(config: T, path: string, value: unknown): T {
  const [head, ...rest] = path.split('.');
  const source = config as unknown as Record<string, unknown>;
  return { ...source, [head]: rest.length === 0 ? value : setByPath(source[head], rest.join('.'), value) } as unknown as T;
}

export const settingValue = (config: RandomizerConfig, id: string): unknown => getByPath(config, settingById(id).path);

// --- availability -------------------------------------------------------------------------------------------------------------------------------------------------

/**
 * Why a control is not available (`help.reasons.<id>`), or why a combination will not work although the control is available (a warning). A disabled control always
 * carries a reason: nothing in the interface is disabled without saying why.
 */
export type ReasonId =
  | 'itemsOff' | 'enemiesOff' | 'regularEnemiesOff' | 'bossesOff' | 'fogOff'
  | 'needsWorldItems' | 'needsSoulPool' | 'needsStartingEquipment' | 'needsKeyPlacement' | 'racePlusFair'
  | 'samplingOnly' | 'needsCurve' | 'needsRoamingMode' | 'needsBothNormalMode' | 'needsBossDraw' | 'needsBossBothMode' | 'needsNormalEnemiesMode' | 'customPoolActive'
  | 'dlcWithFog'
  | 'warnKeysNeedWorldItems' | 'warnRandomStart';

export interface SettingState {
  enabled: boolean;
  /** Set when `enabled` is false. */
  reason?: ReasonId;
  /** An advice about the current combination (the control stays usable). */
  warning?: ReasonId;
}

const ok: SettingState = { enabled: true };
const off = (reason: ReasonId): SettingState => ({ enabled: false, reason });

export function settingState(config: RandomizerConfig, id: string): SettingState {
  const items = config.items;
  const enemies = config.enemies;
  const placement = enemies.placement;
  const itemsOn = items.enabled;
  const enemiesOn = enemies.enabled;
  const regularOn = enemiesOn && enemies.randomizeRegularEnemies;
  const bossesOn = enemiesOn && enemies.randomizeBosses;
  const sampling = placement.algorithm === 'sampling-v1';
  const keyMode = items.keyPlacement ?? 'vanilla';

  switch (id) {
    // items
    case 'items.worldItems': case 'items.progressionLogic': case 'items.npcGifts': case 'items.enemyDrops': case 'items.guaranteedDrops': case 'items.shops':
    case 'items.startingClass': case 'items.startingEquipment': case 'items.laundromat': case 'items.keyPlacement':
      if (!itemsOn) return off('itemsOff');
      if (id === 'items.keyPlacement' && keyMode !== 'vanilla' && !items.randomizeWorldItems) return { enabled: true, warning: 'warnKeysNeedWorldItems' };
      return ok;
    case 'items.startingItems': return !itemsOn ? off('itemsOff') : !items.randomizeStartingEquipment ? off('needsStartingEquipment') : ok;
    case 'items.fashionSouls': return !itemsOn ? off('itemsOff') : !items.randomizeWorldItems ? off('needsWorldItems') : ok;
    case 'items.soulItems': return !itemsOn ? off('itemsOff') : !(items.randomizeWorldItems || items.randomizeGifts || items.randomizeEnemyDrops) ? off('needsSoulPool') : ok;
    case 'items.lordvessel': case 'items.lordSouls': return !itemsOn ? off('itemsOff') : keyMode === 'vanilla' ? off('needsKeyPlacement') : ok;
    case 'items.difficulty': return !itemsOn ? off('itemsOff') : keyMode === 'race-plus' ? off('racePlusFair') : ok;
    // enemies
    case 'enemies.regular': case 'enemies.bosses': case 'enemies.scaling': return !enemiesOn ? off('enemiesOff') : ok;
    case 'enemies.algorithm': return !regularOn ? off(enemiesOn ? 'regularEnemiesOff' : 'enemiesOff') : ok;
    case 'enemies.replacementChance': case 'enemies.normalReplacement': case 'enemies.fit': case 'enemies.difficultyMode': case 'enemies.typeReplacement': case 'enemies.customPool':
      return !regularOn ? off(enemiesOn ? 'regularEnemiesOff' : 'enemiesOff') : !sampling ? off('samplingOnly') : ok;
    case 'enemies.strictness':
      return !regularOn ? off(enemiesOn ? 'regularEnemiesOff' : 'enemiesOff') : !sampling ? off('samplingOnly') : !placement.difficultyMode.startsWith('curve') ? off('needsCurve') : ok;
    case 'enemies.bossReplacement': return !bossesOn ? off(enemiesOn ? 'bossesOff' : 'enemiesOff') : !sampling ? off('samplingOnly') : ok;
    case 'enemies.bossChanceBoss':
      return !bossesOn ? off(enemiesOn ? 'bossesOff' : 'enemiesOff') : !sampling ? off('samplingOnly') : placement.bossReplacement !== 'both' ? off('needsBossBothMode') : ok;
    case 'enemies.uniqueBosses':
      return !bossesOn ? off(enemiesOn ? 'bossesOff' : 'enemiesOff') : !sampling ? off('samplingOnly') : placement.bossReplacement !== 'bosses' && placement.bossReplacement !== 'both' ? off('needsBossDraw') : ok;
    case 'enemies.bossChanceNormal':
      return !regularOn ? off(enemiesOn ? 'regularEnemiesOff' : 'enemiesOff') : !sampling ? off('samplingOnly') : placement.normalReplacement !== 'both' ? off('needsBothNormalMode') : ok;
    case 'enemies.roamingSouls': case 'enemies.roamingDead':
      return !regularOn ? off(enemiesOn ? 'regularEnemiesOff' : 'enemiesOff') : !sampling ? off('samplingOnly') : placement.normalReplacement !== 'bosses' && placement.normalReplacement !== 'both' ? off('needsRoamingMode') : ok;
    case 'enemies.hostileNpcs':
      return !regularOn ? off(enemiesOn ? 'regularEnemiesOff' : 'enemiesOff') : !sampling ? off('samplingOnly')
        : placement.normalReplacement !== 'normals' && placement.normalReplacement !== 'both' ? off('needsNormalEnemiesMode') : placement.customPool !== null ? off('customPoolActive') : ok;
    case 'enemies.mimics': case 'enemies.mosquitoes': case 'enemies.sameEnemy': case 'enemies.pinwheel': return !regularOn ? off(enemiesOn ? 'regularEnemiesOff' : 'enemiesOff') : ok;
    case 'enemies.gargoyle': case 'enemies.gwyn': return !bossesOn ? off(enemiesOn ? 'bossesOff' : 'enemiesOff') : ok;
    // fog
    case 'fog.traversable': case 'fog.bossFogs': case 'fog.warps': case 'fog.majorPvp': case 'fog.minorPvp': case 'fog.lordvesselGates': case 'fog.disconnected':
    case 'fog.noFall': case 'fog.scaling': case 'fog.glitched': case 'fog.requireLordSouls': case 'fog.pacifist': case 'fog.gameLanguage':
      return !config.fog.enabled ? off('fogOff') : ok;
    case 'fog.randomStart':
      return !config.fog.enabled ? off('fogOff') : !config.fog.bossFogs && !config.fog.traversableFogs ? { enabled: true, warning: 'warnRandomStart' } : ok;
    case 'output.dlc': return config.fog.enabled ? off('dlcWithFog') : ok;
    default: return ok;
  }
}

/** What the control shows as its value: the DLC switch shows "on" while the fog randomizer forces it. */
export function displayedValue(config: RandomizerConfig, id: string): unknown {
  if (id === 'output.dlc') return config.output.includeDlc || config.fog.enabled;
  return settingValue(config, id);
}

// --- changing a setting -------------------------------------------------------------------------------------------------------------------------------------------

const SAMPLING_DEFAULTS = { replacementChance: 100, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'random-easy-asylum', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInBossSlots: 90, hostileNpcs: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false } as const;
const PERMUTATION_DEFAULTS = { ...SAMPLING_DEFAULTS, fit: 'anywhere', difficultyMode: 'random' } as const;

/**
 * Changes one setting and applies the rules the product has always applied to the settings that belong to it, so a hidden setting never stays in the world
 * (each rule is stated in the help of the setting that causes it):
 * - choosing the enemy placement algorithm puts the sampling-only options to their defaults for that algorithm;
 * - leaving the key modes puts Lordvessel and Lord Souls placement back to off, and Race Mode + puts the item difficulty to Fair;
 * - leaving the modes that allow bosses in regular slots (or the one that gives them a chance) puts their options back to the defaults;
 * - a custom enemy pool turns the hostile NPCs off;
 * - turning the fog randomizer on puts the DLC back on.
 * Nothing else is ever changed, and a switch between views or languages never calls this.
 */
export function applySetting(config: RandomizerConfig, id: string, value: unknown): RandomizerConfig {
  const def = settingById(id);
  let next = setByPath(config, def.path, value);
  const set = (path: string, to: unknown) => { next = setByPath(next, path, to); };
  switch (id) {
    case 'items.keyPlacement':
      if (value === 'vanilla') { set('items.randomizeLordvessel', false); set('items.randomizeLordSouls', false); }
      if (value === 'race-plus') set('items.itemDifficulty', 'fair');
      break;
    case 'enemies.algorithm':
      next = { ...next, enemies: { ...next.enemies, placement: { ...(value === 'sampling-v1' ? SAMPLING_DEFAULTS : PERMUTATION_DEFAULTS), algorithm: value as EnemyPlacementAlgorithm } } };
      break;
    case 'enemies.normalReplacement': {
      const roaming = value === 'bosses' || value === 'both';
      if (value !== 'both') set('enemies.placement.bossChanceInNormalSlots', 10);
      if (!roaming) { set('enemies.placement.roamingBossSouls', 50); set('enemies.placement.roamingBossesStayDead', false); }
      if (value !== 'normals' && value !== 'both') set('enemies.placement.hostileNpcs', false);
      break;
    }
    case 'enemies.bossReplacement':
      if (value !== 'both') set('enemies.placement.bossChanceInBossSlots', 90);
      if (value !== 'bosses' && value !== 'both') set('enemies.placement.uniqueBosses', false);
      break;
    case 'enemies.customPool':
      if (value !== null) set('enemies.placement.hostileNpcs', false);
      break;
    case 'fog.enabled':
      if (value === true) set('output.includeDlc', true);
      break;
    default: break;
  }
  return next;
}

// --- the basic view -----------------------------------------------------------------------------------------------------------------------------------------------

export const BASIC_SETTINGS: readonly SettingDef[] = VISIBLE_SETTINGS.filter((setting) => setting.basic);

const sameValue = (left: unknown, right: unknown): boolean => JSON.stringify(left) === JSON.stringify(right);

/**
 * The advanced settings (the ones the basic view does not show) that differ from the recommended configuration. The basic view says so instead of hiding it; it never resets them.
 * The hidden controls (no effect, refused) are not counted: there is nothing to review.
 */
export function customAdvancedSettings(config: RandomizerConfig): string[] {
  const defaults = { ...DEFAULT_GENERATION_SETTINGS, output: { ...DEFAULT_GENERATION_SETTINGS.output, generateSpoilerLog: true } } as unknown as RandomizerConfig;
  return VISIBLE_SETTINGS
    .filter((setting) => !setting.basic)
    .filter((setting) => !sameValue(getByPath(config, setting.path), getByPath(defaults, setting.path)))
    .map((setting) => setting.id);
}

export const customAdvancedActive = (config: RandomizerConfig): boolean => customAdvancedSettings(config).length > 0;

/** The order of the tiles of the basic view, per card. The set is the settings that carry `basic`; a test keeps the two in step. */
export const BASIC_LAYOUT: Record<BasicGroup, readonly string[]> = {
  main: ['items.enabled', 'enemies.enabled', 'fog.enabled', 'enemies.bosses'],
  common: ['items.npcGifts', 'items.shops'],
  world: ['output.dlc', 'output.spoiler'],
};

/** The advanced settings that differ from the recommended ones, grouped by section (the advanced view opens these sections when it is opened from the basic notice). */
export function sectionsWithCustomSettings(config: RandomizerConfig): SectionId[] {
  const ids = new Set(customAdvancedSettings(config));
  return SECTION_IDS.filter((section) => settingsOfSection(section).some((setting) => ids.has(setting.id)));
}
