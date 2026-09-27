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
}

export type EnemyScaling = 'vanilla' | 'area' | 'progressive';

export interface EnemyOptions {
  enabled: boolean;
  randomizeRegularEnemies: boolean;
  randomizeBosses: boolean;
  scaling: EnemyScaling;
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
