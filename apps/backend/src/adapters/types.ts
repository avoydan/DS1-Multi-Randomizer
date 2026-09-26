import type { EngineName, EngineStatus, RandomizerConfig } from '@ds1tr/contracts';

export interface EngineRunContext {
  gamePath: string;
  workspacePath: string;
  seed: number;
  config: RandomizerConfig;
  log: (message: string) => void;
}

export interface RandomizerAdapter {
  readonly name: EngineName;
  status(): Promise<EngineStatus>;
  validate(ctx: EngineRunContext): Promise<void>;
  run(ctx: EngineRunContext): Promise<void>;
}
