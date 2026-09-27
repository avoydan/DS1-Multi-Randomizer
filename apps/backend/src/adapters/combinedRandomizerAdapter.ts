import type { EnemyOptions, ItemOptions } from '@ds1tr/contracts';

export interface CombinedRandomizerRequest {
  gamePath: string;
  workspacePath: string;
  masterSeed: string;
  combinedSeed: string;
  items: ItemOptions;
  enemies: EnemyOptions;
}

export type CombinedRandomizerEvent =
  | { type: 'progress'; progress: number; message?: string }
  | { type: 'log'; level: 'info' | 'warning' | 'error'; message: string };

export type CombinedRandomizerEventCallback = (event: CombinedRandomizerEvent) => void;

export type CombinedRandomizerResult =
  | { ok: true; workspacePath: string }
  | { ok: false; code: string; message: string };

export interface CombinedRandomizerEngine {
  generate(
    request: CombinedRandomizerRequest,
    onEvent: CombinedRandomizerEventCallback,
  ): Promise<CombinedRandomizerResult>;
}

export class CombinedRandomizerAdapter implements CombinedRandomizerEngine {
  async generate(
    _request: CombinedRandomizerRequest,
    _onEvent: CombinedRandomizerEventCallback,
  ): Promise<CombinedRandomizerResult> {
    return {
      ok: false,
      code: 'NOT_IMPLEMENTED',
      message: 'Combined items and enemies randomizer integration is not implemented.',
    };
  }
}
