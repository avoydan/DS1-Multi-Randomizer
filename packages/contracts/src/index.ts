import type { GenerationSettings } from './generation.js';
export type EngineName = 'combined' | 'fog';

/**
 * Legacy "Soul items": shuffled with the pool (default), replaced by other consumables, or transposed (the boss souls join the pool and
 * each has a 75% chance to become a boss weapon of its boss; docs/ITEM_PROGRESSION_MODEL.md).
 */
export type SoulItemsMode = 'shuffled' | 'replaced' | 'transposed';

/**
 * Legacy "Key placement" (Part H, docs/ITEM_PROGRESSION_MODEL.md). `vanilla`: every key stays where the game puts it (the behavior before the option existed).
 * `shuffled`: the keys of FogMod's annotation are placed by a progression-safe fill over the world graph. The legacy's Race Mode and Race Mode + are not built.
 */
export type KeyPlacementMode = 'vanilla' | 'shuffled' | 'race' | 'race-plus';

/**
 * Legacy item "Difficulty" (Part I, docs/ITEM_DIFFICULTY_MODEL.md): `fair` (the legacy default, DS1TR's neutral value: items have an equal chance anywhere),
 * `unfair` (desirable items are not placed in plain view), `very-unfair` (desirable items are hidden and rarely in early areas).
 */
export type ItemDifficultyMode = 'fair' | 'unfair' | 'very-unfair';

/** The values under which key placement does nothing: what an older Seed Code, preset or settings file means. They never change. */
export const NEUTRAL_KEY_OPTIONS: { keyPlacement: KeyPlacementMode; randomizeLordvessel: boolean; randomizeLordSouls: boolean; itemDifficulty: ItemDifficultyMode; laundromat: boolean } = {
  keyPlacement: 'vanilla',
  itemDifficulty: 'fair',
  laundromat: false,
  randomizeLordvessel: false,
  randomizeLordSouls: false,
};

/** Legacy "Starting items": right-hand weapon rule and left-hand pool. 'shield-2h' is the legacy default. */
export type StartingItemsMode = 'shield-1h' | 'shield-2h' | 'mixed-pool';

/** Legacy "Gwyn nerf": the chance that Lord Gwyn is rejected as a boss replacement (strong 85%, medium 60%). */
export type GwynNerfMode = 'none' | 'medium' | 'strong';

export interface ItemOptions {
  enabled: boolean;
  randomizeWorldItems: boolean;
  randomizeProtectedItems: boolean;
  progressionLogic: boolean;
  guaranteeEarlyWeapon: boolean;
  balancedEarlyLoot: boolean;
  randomizeStartingClass: boolean;
  randomizeStartingEquipment: boolean;
  randomizeGifts: boolean;
  randomizeEnemyDrops: boolean;
  guaranteedEnemyDrops: boolean;
  randomizeShops: boolean;
  /**
   * Legacy "Fashion Souls". On (legacy default) shuffles armor pieces
   * individually; off exchanges complete world armor sets between set locations.
   */
  fashionSouls: boolean;
  /** Legacy default: shuffled. */
  soulItems: SoulItemsMode;
  /** Legacy default: shield-2h. Acts only with starting equipment on. */
  startingItems: StartingItemsMode;
  /** Part H: key placement. Needs world items. Neutral: vanilla. */
  keyPlacement: KeyPlacementMode;
  /** Part I: item difficulty. Neutral: fair. Race Mode + is only available with fair. */
  itemDifficulty: ItemDifficultyMode;
  /** Legacy "Laundromat Mixup": NPC characters wear armor pieces other NPCs wear (docs/LAUNDROMAT_MIXUP.md). Neutral: off. */
  laundromat: boolean;
  /** Legacy "Senile Gwynevere": the Lordvessel joins the key shuffle (needs key placement "shuffled"). Neutral: off. */
  randomizeLordvessel: boolean;
  /** Legacy "Senile Primordial Serpents": the four Lord Soul items join the key shuffle (needs key placement "shuffled"). Neutral: off. */
  randomizeLordSouls: boolean;
}

export type EnemyScaling = 'vanilla' | 'area' | 'progressive';

/**
 * Legacy "Replace second Gargoyle". `replace` (legacy default) replaces the second Bell Gargoyle with an
 * independently drawn boss; `keep` leaves it a Bell Gargoyle. `single-boss` is the earlier DS1TR behavior
 * (the extra Bell Gargoyle bodies are disabled); it is kept so older presets reproduce their output.
 */
export type SecondGargoyleMode = 'replace' | 'keep' | 'single-boss';

/**
 * Enemy placement algorithm (docs/ENEMY_PLACEMENT_MODEL.md).
 *
 * - `permutation-v1`: the count-preserving global permutation. Frozen: every existing preset and seed depends on it.
 * - `sampling-v1`: the per-slot sampler of the legacy-parity group. Opt-in. Part A samples regular enemy slots
 *   only; bosses, linked units and NPCs are planned exactly as under `permutation-v1`.
 */
export type EnemyPlacementAlgorithm = 'permutation-v1' | 'sampling-v1';

export const ENEMY_PLACEMENT_ALGORITHMS: readonly EnemyPlacementAlgorithm[] = ['permutation-v1', 'sampling-v1'];

/** Algorithms that can currently run. */
export const IMPLEMENTED_ENEMY_PLACEMENT_ALGORITHMS: readonly EnemyPlacementAlgorithm[] = ['permutation-v1', 'sampling-v1'];

/** The algorithm of every new configuration and of every migrated older preset. */
export const DEFAULT_ENEMY_PLACEMENT_ALGORITHM: EnemyPlacementAlgorithm = 'permutation-v1';

/**
 * Legacy "Normal enemy replacement" (0 none, 1 only with bosses, 2 only with normal enemies, 3 either). `bosses` and `both` (Part E1)
 * let bosses stand in regular enemy slots ("roaming bosses"); they need `sampling-v1`.
 */
export type NormalEnemyReplacementMode = 'none' | 'normals' | 'bosses' | 'both';

export const NORMAL_ENEMY_REPLACEMENT_MODES: readonly NormalEnemyReplacementMode[] = ['none', 'normals', 'bosses', 'both'];

/** The Normal enemy replacement values in which a boss can stand in a regular enemy slot. */
export const ROAMING_BOSS_REPLACEMENT_MODES: readonly NormalEnemyReplacementMode[] = ['bosses', 'both'];

/** Legacy "Placement" (0 only where they fit, 1 anywhere, 2 anywhere except when replacing NPCs). */
export type PlacementFitMode = 'fits' | 'anywhere' | 'anywhere-except-npcs';

export const PLACEMENT_FIT_MODES: readonly PlacementFitMode[] = ['fits', 'anywhere', 'anywhere-except-npcs'];

/**
 * Legacy "Difficulty mode": curve = replacements of comparable difficulty (strictness applies), random = fully
 * random, random-easy-asylum = fully random with an easy Asylum, curve-easy-asylum = curve with an easy Asylum.
 * Legacy default: random-easy-asylum. Regular enemies only in Part B.
 */
export type DifficultyMode = 'curve' | 'random' | 'random-easy-asylum' | 'curve-easy-asylum';

export const DIFFICULTY_MODES: readonly DifficultyMode[] = ['curve', 'random', 'random-easy-asylum', 'curve-easy-asylum'];

/** Legacy "Difficulty strictness" (strict, a bit loose = default, very loose); only effective in the curve modes. */
export type DifficultyStrictness = 'strict' | 'loose' | 'very-loose';

export const DIFFICULTY_STRICTNESS: readonly DifficultyStrictness[] = ['strict', 'loose', 'very-loose'];

/**
 * Legacy "Type replacement": disabled (default) draws every slot on its own; enabled replaces all slots of one
 * original model within a map by the same replacement model; enabled-except-roaming-bosses differs only for boss
 * sources, which regular-enemy sampling never draws, so it behaves like enabled today.
 */
export type TypeReplacementMode = 'disabled' | 'enabled' | 'enabled-except-roaming-bosses';

export const TYPE_REPLACEMENT_MODES: readonly TypeReplacementMode[] = ['disabled', 'enabled', 'enabled-except-roaming-bosses'];

/**
 * Boss replacement (Part D). `classic` keeps the existing boss planner (the archetype permutation); `bosses` draws a replacement
 * for every single-body boss slot from the portable boss pool, with repeats. Linked units (dragons, Gargoyles, Four Kings, ...)
 * stay classic in both modes. Sampling only: neutral under permutation-v1.
 */
export type BossReplacementMode = 'classic' | 'bosses' | 'normals' | 'both';

export const BOSS_REPLACEMENT_MODES: readonly BossReplacementMode[] = ['classic', 'bosses', 'normals', 'both'];

/** The values in which a normal enemy can be the body of a boss encounter (Part E2). */
export const ENCOUNTER_BODY_REPLACEMENT_MODES: readonly BossReplacementMode[] = ['normals', 'both'];

/**
 * Placement settings. Everything except `algorithm` applies to `sampling-v1` only; under `permutation-v1` the
 * backend requires the neutral values below and rejects any other (it never ignores a setting silently).
 */
export interface EnemyPlacementOptions {
  algorithm: EnemyPlacementAlgorithm;
  /** Per-slot chance (percent, 0-100) that a regular enemy slot is replaced at all. */
  replacementChance: number;
  normalReplacement: NormalEnemyReplacementMode;
  fit: PlacementFitMode;
  difficultyMode: DifficultyMode;
  difficultyStrictness: DifficultyStrictness;
  typeReplacement: TypeReplacementMode;
  /**
   * Custom enemy pool: the regular enemy models (stable ids such as `c2500`) the sampler may place. `null` is the full
   * pool. The pool restricts replacement candidates only; the original enemies of a map are never removed because they
   * are not selected. Canonical form: unique ids in ascending order (the backend sorts; duplicates are rejected).
   */
  customPool: string[] | null;
  /** Part D. Optional on input (older configurations and presets lack it: `classic`); always present once canonicalized. */
  bossReplacement: BossReplacementMode;
  /** Part D. Draws every boss of the pool once before any boss repeats. Needs `bossReplacement: 'bosses'`. */
  uniqueBosses: boolean;
  /** Part E2. Chance (percent, 0-100) that a boss slot that takes part gets a boss rather than a normal enemy. Needs `bossReplacement: 'both'`. */
  bossChanceInBossSlots: number;
  /** Part F1. Legacy "Hostile NPCs": the Undead Merchant, Andre and Vamos join the normal enemy pool as hostile combatants. Needs `normalReplacement` `normals` or `both` and no custom pool. */
  hostileNpcs: boolean;
  /** Part E1. Chance (percent, 0-100) that a regular enemy slot that takes part gets a boss rather than a normal enemy. Needs `normalReplacement: 'both'`. */
  bossChanceInNormalSlots: number;
  /** Part E1. Share (percent, 0-100) of a boss's fight souls that the boss drops when it stands in a regular slot. Needs roaming bosses. */
  roamingBossSouls: number;
  /** Part E1. A roaming boss stays dead once killed instead of coming back when the player rests. Needs roaming bosses. */
  roamingBossesStayDead: boolean;
}

/** One enemy a custom pool can contain, described from the game data of the installation. */
export interface EnemyPoolEntry {
  model: string;
  /** Size class 0-5 derived from the hit capsule. */
  size: number;
  /** Difficulty class 0-7 derived from the maps the enemy lives in and its HP. */
  difficulty: number;
  /** Regular slots of this enemy in the game. */
  slots: number;
  maps: string[];
}

export interface EnemyPoolDescription {
  /** Where the data came from: the pristine snapshot or the selected game directory. */
  source: 'pristine' | 'game';
  classTableHash: string;
  regularSlots: number;
  entries: EnemyPoolEntry[];
}

/** The values under which `permutation-v1` behaves as it always did. Migrated presets and new configurations use them. */
export const NEUTRAL_ENEMY_PLACEMENT_OPTIONS: Omit<EnemyPlacementOptions, 'algorithm'> = {
  replacementChance: 100,
  normalReplacement: 'normals',
  fit: 'anywhere',
  difficultyMode: 'random',
  difficultyStrictness: 'loose',
  typeReplacement: 'disabled',
  customPool: null,
  bossReplacement: 'classic',
  uniqueBosses: false,
  bossChanceInBossSlots: 90,
  hostileNpcs: false,
  bossChanceInNormalSlots: 10,
  roamingBossSouls: 50,
  roamingBossesStayDead: false,
};

/** The legacy defaults offered when `sampling-v1` is chosen: replace always, only normal enemies, only where they fit. */
export const LEGACY_ENEMY_PLACEMENT_OPTIONS: Omit<EnemyPlacementOptions, 'algorithm'> = {
  replacementChance: 100,
  normalReplacement: 'normals',
  fit: 'fits',
  difficultyMode: 'random-easy-asylum',
  difficultyStrictness: 'loose',
  typeReplacement: 'disabled',
  customPool: null,
  bossReplacement: 'classic',
  uniqueBosses: false,
  bossChanceInBossSlots: 90,
  hostileNpcs: false,
  bossChanceInNormalSlots: 10,
  roamingBossSouls: 50,
  roamingBossesStayDead: false,
};

export interface EnemyOptions {
  enabled: boolean;
  randomizeRegularEnemies: boolean;
  randomizeBosses: boolean;
  scaling: EnemyScaling;
  /** Legacy "Replace mimics" (default on). Off leaves every Mimic in place. */
  replaceMimics: boolean;
  /** Legacy "Respawning mosquito replacement" (default on). Off leaves the six Blighttown swamp mosquitoes. */
  replaceRespawningMosquitoes: boolean;
  /** Legacy "Prevent same enemy" (default off). On: no regular enemy slot receives its own model. */
  preventSameEnemy: boolean;
  /** Legacy "Pinwheel Chaos" (default off): the main Pinwheel stays and its clones are replaced. */
  pinwheelChaos: boolean;
  /** Legacy default: medium. Acts only with boss randomization on. */
  gwynNerf: GwynNerfMode;
  /** Legacy default: replace. Acts only with boss randomization on. */
  secondGargoyle: SecondGargoyleMode;
  placement: EnemyPlacementOptions;
}

export type FogLanguage =
  | 'ENGLISH' | 'FRENCH' | 'GERMAN' | 'ITALIAN' | 'JAPANESE'
  | 'KOREAN' | 'NSPANISH' | 'POLISH' | 'PORTUGUESE' | 'RUSSIAN'
  | 'SCHINESE' | 'SPANISH' | 'TCHINESE';

export interface FogOptions {
  enabled: boolean;
  traversableFogs: boolean;
  bossFogs: boolean;
  areaWarps: boolean;
  majorPvpFogs: boolean;
  minorPvpFogs: boolean;
  lordvesselGates: boolean;
  randomStart: boolean;
  disconnectedFogs: boolean;
  noFallBedOfChaos: boolean;
  scaling: boolean;
  glitchedLogic: boolean;
  requireLordSouls: boolean;
  pacifistMode: boolean;
  language: FogLanguage;
}

export interface OutputOptions {
  includeDlc: boolean;
  generateSpoilerLog: boolean;
}

export interface RandomizerConfig {
  gamePath: string;
  masterSeed: string;
  items: ItemOptions;
  enemies: EnemyOptions;
  fog: FogOptions;
  output: OutputOptions;
}

export interface DerivedSeeds {
  combined: string;
  fog: number;
}

export interface InstallationValidation {
  valid: boolean;
  gamePath: string;
  executableFound: boolean;
  gameParamFound: boolean;
  mapStudioFound: boolean;
  scriptFound: boolean;
  eventFound: boolean;
  problems: string[];
}

export interface EngineStatus {
  name: EngineName;
  ready: boolean;
  mode: 'source' | 'executable' | 'missing';
  detail: string;
}

/** One line of the plan preview: what an active setting does, in plain words. Never a location (spoilers stay in the spoiler log). */
export interface PlanBehavior {
  label: string;
  value: string;
  /** Stable ids of the line and of its value, and the variables of the value, so a translated interface never reads the English sentence. */
  labelKey?: string;
  valueKey?: string;
  params?: Record<string, string>;
}

export interface PipelinePlan {
  seeds: DerivedSeeds;
  stages: Array<{
    engine: EngineName;
    enabled: boolean;
    seed: string | number;
  }>;
  engineStatus: EngineStatus[];
  /** The behaviors the selected settings switch on, for the preview (optional: older backends do not send it). */
  behaviors?: PlanBehavior[];
}

export interface GameStateReport {
  state: 'invalid' | 'no-snapshot' | 'version-changed' | 'vanilla' | 'active' | 'interrupted' | 'modified';
  gamePath: string;
  snapshotId: string | null;
  activePackage: string | null;
  problems: string[];
}

export type OperationResult<T> = ({ ok: true } & T) | {
  ok: false;
  code: string;
  message: string;
  problems: string[];
};

export type GenerationEvent =
  | { type: 'progress'; stage: string; progress: number; message: string }
  | { type: 'log'; stage: string; level: 'info' | 'warning' | 'error'; message: string };

export type GenerationResult =
  | {
    ok: true; packagePath: string; manifestPath: string; manifest: { masterSeed: string; files: Array<{ path: string }> };
    /** The shareable Seed Code of this world, and the generation profile it was made with. */
    seedCode: string; profile: string;
    /** The run-history entry of this world (an existing one when the same world was generated again). */
    historyId?: string | null;
    /** The settings of the generated world (the Seed Code's), which the controls may no longer show. */
    settings?: GenerationSettings;
  }
  | { ok: false; code: string; message: string; stage: string };

export const PRESET_FORMAT = 'ds1tr-preset';
export const PRESET_VERSION = 15;

/** Shareable settings: everything in RandomizerConfig except the machine-specific game path. */
export interface PresetDocument {
  format: typeof PRESET_FORMAT;
  presetVersion: typeof PRESET_VERSION;
  masterSeed: string;
  items: ItemOptions;
  enemies: EnemyOptions;
  fog: FogOptions;
  output: OutputOptions;
}

export type PresetImportResult =
  | { ok: true; preset: PresetDocument }
  | { ok: false; problems: string[] };

export * from './generation.js';
export * from './installation.js';
export * from './smokeTests.js';
