import type { EnemyOptions, FogOptions, ItemOptions, RandomizerConfig } from './index.js';

/**
 * Seed Codes, generation profiles, run history and persisted settings (docs/SEED_FORMAT.md,
 * docs/GENERATION_COMPATIBILITY.md).
 *
 * A generation profile is an immutable compatibility contract: it names the exact deterministic behavior (seed derivation,
 * combined engine, Fog engine, retry rules) that turns a master seed and a canonical configuration into a world. The
 * application version is not part of it. A Seed Code carries the profile id, so any later application that still supports the
 * profile reproduces the same world.
 */

/** The first profile: the deterministic behavior of the application at desktop 0.16.1 / backend 0.29.1. Never edit its meaning. */
export const GENERATION_PROFILE_V1 = 'ds1tr-gen-v1';
/** The profile new runs use. A later profile may replace it here; older profiles stay runnable. */
export const CURRENT_GENERATION_PROFILE = GENERATION_PROFILE_V1;

export const SEED_CODE_PREFIX = 'DS1TR';
/** Version of the Seed Code container itself (encoding, checksum, envelope layout). */
export const SEED_FORMAT_VERSION = 1;
export const SEED_ENVELOPE_FORMAT = 'ds1tr-seed';

/**
 * Everything about a run that changes the generated world, with all defaults resolved. There is no "difference from the
 * defaults": an old Seed Code restores every option explicitly, whatever the current defaults are.
 */
export interface GenerationSettings {
  items: ItemOptions;
  enemies: EnemyOptions;
  fog: FogOptions;
  output: { includeDlc: boolean };
}

/** What a Seed Code decodes to (before it is validated for the running application). */
export interface SeedEnvelope {
  format: typeof SEED_ENVELOPE_FORMAT;
  formatVersion: number;
  profile: string;
  /** The internal master seed; the existing seed derivation (named streams) runs from it unchanged. */
  masterSeed: string;
  settings: GenerationSettings;
}

export type SeedCodeProblem =
  | 'MALFORMED'
  | 'CHECKSUM'
  | 'UNSUPPORTED_FORMAT'
  | 'UNSUPPORTED_PROFILE'
  | 'INVALID_SETTINGS';

/** The answer of the decode endpoint (what the app needs to load a Seed Code). */
export type DecodedSeedPayload =
  | { ok: true; seedCode: string; profile: string; masterSeed: string; settings: GenerationSettings }
  | { ok: false; code: SeedCodeProblem; message: string; technical: string };

export type DecodedSeed =
  | { ok: true; envelope: SeedEnvelope; seedCode: string }
  | { ok: false; code: SeedCodeProblem; message: string; technical: string };

/** Key order of the canonical settings. The serialization is stable because it never depends on object insertion order. */
export const CANONICAL_ITEM_KEYS = [
  'enabled', 'randomizeWorldItems', 'randomizeProtectedItems', 'progressionLogic', 'guaranteeEarlyWeapon', 'balancedEarlyLoot',
  'randomizeStartingClass', 'randomizeStartingEquipment', 'randomizeGifts', 'randomizeEnemyDrops', 'guaranteedEnemyDrops',
  'randomizeShops', 'fashionSouls', 'soulItems', 'startingItems',
] as const;
/**
 * Item options added after format 1 (Part H): complete in memory, stored in a Seed Code only when they differ from their neutral value, so every earlier
 * code keeps its exact text (the same rule as the placement extension keys below). Keys are appended in this order and a neutral value never changes.
 */
export const CANONICAL_ITEM_EXTENSION_KEYS = [
  { key: 'keyPlacement', neutral: 'vanilla' },
  { key: 'randomizeLordvessel', neutral: false },
  { key: 'randomizeLordSouls', neutral: false },
] as const;
export const CANONICAL_ENEMY_KEYS = [
  'enabled', 'randomizeRegularEnemies', 'randomizeBosses', 'scaling', 'replaceMimics', 'replaceRespawningMosquitoes',
  'preventSameEnemy', 'pinwheelChaos', 'gwynNerf', 'secondGargoyle', 'placement',
] as const;
export const CANONICAL_PLACEMENT_KEYS = [
  'algorithm', 'replacementChance', 'normalReplacement', 'fit', 'difficultyMode', 'difficultyStrictness', 'typeReplacement', 'customPool',
] as const;
/**
 * Placement options added after format 1 existed (Part D). They are complete in memory (UI, history, settings file) but a Seed Code
 * stores one only when it differs from its neutral value, so every code that was made before the option existed keeps its exact
 * text, and decoding fills the neutral value back in. The neutral value of a key is frozen forever: it is "what the world did before
 * this option existed". Keys are appended in this order.
 */
export const CANONICAL_PLACEMENT_EXTENSION_KEYS = [
  { key: 'bossReplacement', neutral: 'classic' },
  { key: 'uniqueBosses', neutral: false },
  // Part E1: bosses in regular slots. The neutral values are the legacy defaults; they matter only with Normal enemy replacement
  // "bosses" / "both", which an older code cannot contain, so no existing code changes.
  { key: 'bossChanceInNormalSlots', neutral: 10 },
  { key: 'roamingBossSouls', neutral: 50 },
  { key: 'roamingBossesStayDead', neutral: false },
  // Part E2: a normal enemy in a boss slot. The neutral value is the legacy default; it matters only with Boss replacement "normals" /
  // "both", which an earlier code cannot contain, so no existing code changes.
  { key: 'bossChanceInBossSlots', neutral: 90 },
  // Part F1: hostile NPCs as normal enemy sources. Off is the world before the option existed; an earlier code cannot contain it.
  { key: 'hostileNpcs', neutral: false },
] as const;
export const CANONICAL_FOG_KEYS = [
  'enabled', 'traversableFogs', 'bossFogs', 'areaWarps', 'majorPvpFogs', 'minorPvpFogs', 'lordvesselGates', 'randomStart',
  'disconnectedFogs', 'noFallBedOfChaos', 'scaling', 'glitchedLogic', 'requireLordSouls', 'pacifistMode', 'language',
] as const;
export const CANONICAL_OUTPUT_KEYS = ['includeDlc'] as const;

/**
 * Every leaf of RandomizerConfig and what the Seed Code does with it. A new option that is not listed here fails the
 * coverage test, so a setting that changes the world can never be forgotten in the Seed Code.
 *   'seed'     - part of the canonical settings inside the Seed Code
 *   'envelope' - stored next to the settings in the Seed Code envelope
 *   {excluded} - deliberately not part of a world's identity, with the reason
 */
export type GenerationFieldPolicy = 'seed' | 'envelope' | { excluded: string };

export const GENERATION_FIELD_POLICY: Readonly<Record<string, GenerationFieldPolicy>> = {
  gamePath: { excluded: 'machine-specific installation path; the world does not depend on where the game is installed' },
  masterSeed: 'envelope',
  ...Object.fromEntries(CANONICAL_ITEM_KEYS.map((key) => [`items.${key}`, 'seed' as const])),
  ...Object.fromEntries(CANONICAL_ITEM_EXTENSION_KEYS.map(({ key }) => [`items.${key}`, 'seed' as const])),
  ...Object.fromEntries(CANONICAL_ENEMY_KEYS.filter((key) => key !== 'placement').map((key) => [`enemies.${key}`, 'seed' as const])),
  ...Object.fromEntries(CANONICAL_PLACEMENT_KEYS.map((key) => [`enemies.placement.${key}`, 'seed' as const])),
  ...Object.fromEntries(CANONICAL_PLACEMENT_EXTENSION_KEYS.map(({ key }) => [`enemies.placement.${key}`, 'seed' as const])),
  ...Object.fromEntries(CANONICAL_FOG_KEYS.map((key) => [`fog.${key}`, 'seed' as const])),
  'output.includeDlc': 'seed',
  'output.generateSpoilerLog': { excluded: 'only decides whether spoiler.txt is written; every placement is identical with or without it' },
};

export function splitConfig(config: RandomizerConfig): { settings: GenerationSettings; generateSpoilerLog: boolean } {
  return {
    settings: { items: config.items, enemies: config.enemies, fog: config.fog, output: { includeDlc: config.output.includeDlc } },
    generateSpoilerLog: config.output.generateSpoilerLog,
  };
}

/** Joins loaded or stored settings with the machine and output preferences of this installation. */
export function joinConfig(settings: GenerationSettings, gamePath: string, masterSeed: string, generateSpoilerLog: boolean): RandomizerConfig {
  return {
    gamePath, masterSeed, items: settings.items, enemies: settings.enemies, fog: settings.fog,
    output: { includeDlc: settings.output.includeDlc, generateSpoilerLog },
  };
}

/**
 * A loaded Seed Code is authoritative for the world's settings. Once any of them is changed the original identity no
 * longer applies: the comparison is on the generation settings only, never on the machine or output preferences.
 */
export function isLoadedSeedModified(loaded: GenerationSettings, current: GenerationSettings): boolean {
  return JSON.stringify(loaded) !== JSON.stringify(current);
}

export type RunStatus = 'generated' | 'applied' | 'restored';

export interface RunHistoryEntry {
  id: string;
  createdAt: string;
  seedCode: string;
  profile: string;
  masterSeed: string;
  settings: GenerationSettings;
  status: RunStatus;
  /** Where the generated package was written; may be gone (the app cleans old runs). */
  packagePath: string;
  manifestPath: string;
  spoilerPath: string | null;
  /** Computed when the history is read. */
  packageExists?: boolean;
}

/** Settings kept between sessions (not machine state: the game path is managed separately). */
export interface PersistedSettings {
  schemaVersion: number;
  settings: GenerationSettings;
  generateSpoilerLog: boolean;
}

export const PERSISTED_SETTINGS_VERSION = 1;

export interface LaunchGameResult {
  ok: boolean;
  /** How the game was started: through Steam (`steam://rungameid`), or the game executable as a fallback. */
  method: 'steam' | 'executable' | 'none';
  /** Short text for the person. */
  message: string;
  /** Exact reason for the log. */
  technical?: string;
}

/** Steam's id for Dark Souls: Remastered. */
export const DSR_STEAM_APP_ID = '570940';

/** The explicit outcomes the person must see after the game files change (asserted by the desktop tests). */
export const OUTCOME_MESSAGES = {
  applied: { text: 'Randomized game applied successfully.', sub: 'You can now launch Dark Souls Remastered.' },
  restored: { text: 'Original game restored successfully.', sub: 'Dark Souls Remastered is back to its original files.' },
} as const;

/**
 * What the run area of the app shows. The action buttons follow this state, so only what applies right now is offered:
 *   ready      original game: Randomize, Load seed
 *   generated  a world is ready: Apply to game (Copy seed secondary)
 *   applied    a randomized game is in place: Launch game, Restore original game
 *   repair     an interrupted change: Restore original game
 *   blocked    the game cannot be used yet (not found, modified, updated): a message and nothing to click
 *   working    a long operation is running: progress only
 */
export type RunPhase = 'ready' | 'generated' | 'applied' | 'repair' | 'blocked' | 'working';

export function deriveRunPhase(input: { busy: boolean; gameState: string | null; hasGeneratedWorld: boolean }): RunPhase {
  if (input.busy) return 'working';
  switch (input.gameState) {
    case 'active': return 'applied';
    case 'interrupted': return 'repair';
    case 'vanilla': return input.hasGeneratedWorld ? 'generated' : 'ready';
    default: return 'blocked';
  }
}
