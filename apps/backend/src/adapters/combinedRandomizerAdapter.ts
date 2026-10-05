import type { EnemyOptions, ItemOptions } from '@ds1tr/contracts';
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
  /** Whether upstream writes `spoiler.txt` into the generated package. */
  generateSpoilerLog: boolean;
  /** `detail` adds one record per slot to the sampling-v1 diagnostics (debugging); default `summary`. */
  placementDiagnosticsLevel?: 'summary' | 'detail';
}

export type CombinedRandomizerEvent =
  | { type: 'progress'; progress: number; message?: string }
  | { type: 'log'; level: 'info' | 'warning' | 'error'; message: string };

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
