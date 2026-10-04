export type EngineName = 'combined' | 'fog';

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
}

export type EnemyScaling = 'vanilla' | 'area' | 'progressive';

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

export interface PipelinePlan {
  seeds: DerivedSeeds;
  stages: Array<{
    engine: EngineName;
    enabled: boolean;
    seed: string | number;
  }>;
  engineStatus: EngineStatus[];
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
  | { ok: true; packagePath: string; manifestPath: string; manifest: { masterSeed: string; files: Array<{ path: string }> } }
  | { ok: false; code: string; message: string; stage: string };

export const PRESET_FORMAT = 'ds1tr-preset';
export const PRESET_VERSION = 2;

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
