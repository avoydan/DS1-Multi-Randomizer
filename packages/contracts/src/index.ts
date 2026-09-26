export type EngineName = 'items' | 'enemies' | 'fog';

export interface ItemOptions {
  enabled: boolean;
  difficulty: 'easy' | 'medium' | 'hard';
  keyPlacement: 'vanilla' | 'shuffled' | 'race' | 'race-plus';
  fashionSouls: boolean;
  laundromatMixup: boolean;
}

export interface EnemyOptions {
  enabled: boolean;
  randomizeBosses: boolean;
  difficultyCurve: boolean;
  uniqueBosses: boolean;
  hostileNpcs: boolean;
}

export interface FogOptions {
  enabled: boolean;
  traversableFogs: boolean;
  bossFogs: boolean;
  areaWarps: boolean;
  majorPvpFogs: boolean;
  minorPvpFogs: boolean;
  lordvesselGates: boolean;
  disconnected: boolean;
  glitchedLogic: boolean;
  scaling: boolean;
}

export interface RandomizerConfig {
  gamePath: string;
  masterSeed: string;
  items: ItemOptions;
  enemies: EnemyOptions;
  fog: FogOptions;
}

export interface DerivedSeeds {
  items: number;
  enemies: number;
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
    seed: number;
  }>;
  engineStatus: EngineStatus[];
}
