import type { EnemyOptions, ItemOptions } from '@ds1tr/contracts';
import type { GenerationProfile } from '../core/generationProfiles.js';
import type { FogTrackedItems } from './fogItemRules.js';
import {
  executeCombinedGeneration,
  type CombinedGenerationFailure,
} from './combinedRandomizerBridge.js';

export interface CombinedRandomizerRequest {
  gamePath: string;
  workspacePath: string;
  masterSeed: string;
  combinedSeed: string;
  items: ItemOptions;
  enemies: EnemyOptions;
  /** Frozen by the Seed Code generation profile; not a user setting. */
  roamingBossDestinationPolicy?: GenerationProfile['roamingBossDestinationPolicy'];
  /** Whether upstream writes `spoiler.txt` into the generated package. */
  generateSpoilerLog: boolean;
  /** False leaves the DLC out of the data the engine reads (docs/DLC_AUDIT.md); absent or true is the world as it has always been generated. */
  includeDlc?: boolean;
  /** `detail` adds one record per slot to the sampling-v1 diagnostics (debugging); default `summary`. */
  placementDiagnosticsLevel?: 'summary' | 'detail';
  /** Set when the world randomizer runs: items it follows may only be placed in lots it can follow (see fogItemRules.ts). */
  fogTrackedItems?: FogTrackedItems | null;
  /** Needed (and used) only with Key placement "shuffled": the exported progression model of the world data (adapters/progressionModel.ts). */
  progressionModel?: unknown | null;
}

export type CombinedRandomizerEvent =
  | { type: 'progress'; progress: number; message?: string }
  | { type: 'log'; level: 'info' | 'warning' | 'error'; message: string; /** The helper program the line came from, for the log's stage. */ component?: 'data-tool' };

export type CombinedRandomizerEventCallback = (event: CombinedRandomizerEvent) => void;

export type CombinedRandomizerResult =
  | { ok: true; workspacePath: string }
  | CombinedGenerationFailure;

export interface CombinedRandomizerEngine {
  generate(
    request: CombinedRandomizerRequest,
    onEvent: CombinedRandomizerEventCallback,
  ): Promise<CombinedRandomizerResult>;
}

export class CombinedRandomizerAdapter implements CombinedRandomizerEngine {
  constructor(private readonly execute: typeof executeCombinedGeneration = executeCombinedGeneration) {}

  async generate(
    request: CombinedRandomizerRequest,
    onEvent: CombinedRandomizerEventCallback,
  ): Promise<CombinedRandomizerResult> {
    const result = await this.execute(request, onEvent);
    return result.ok ? { ok: true, workspacePath: result.seedDirectory } : result;
  }
}
