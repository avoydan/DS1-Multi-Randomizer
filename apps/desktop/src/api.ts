import type { DecodedSeedPayload, EnemyPoolDescription, GenerationSettings, PersistedSettings, RunHistoryEntry, GameStateReport, GenerationEvent, GenerationResult, InstallationValidation, OperationResult, PipelinePlan, PresetDocument, PresetImportResult, RandomizerConfig } from '@ds1tr/contracts';

export interface DetectedInstallation { gamePath: string; label: string; valid: boolean; problems: string[] }
export type WorldDataPreparation = { ok: true } | { ok: false; code: string; userMessage: string };

async function connection(): Promise<{ apiBase: string; token: string }> {
  const result = await window.desktop?.connection();
  if (!result?.apiBase) throw new Error(result?.error ?? 'Backend is unavailable. Restart the desktop app.');
  return { apiBase: result.apiBase, token: result.token };
}

async function post<T>(path: string, body: unknown, allowFailure = false): Promise<T> {
  const { apiBase, token } = await connection();
  const response = await fetch(`${apiBase}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-DS1TR-Token': token },
    body: JSON.stringify(body),
  });

  if (!response.ok && !allowFailure) {
    const detail = await response.text();
    throw new Error(`${response.status}: ${detail}`);
  }
  return response.json() as Promise<T>;
}

export async function generate(config: RandomizerConfig, onEvent: (event: GenerationEvent) => void, seedCode?: string): Promise<GenerationResult> {
  const { apiBase, token } = await connection();
  const response = await fetch(`${apiBase}/runs/generate`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream', 'X-DS1TR-Token': token },
    // The master seed is made by the backend when Randomize is clicked; a Seed Code, when given, decides the whole world.
    body: JSON.stringify({ ...config, masterSeed: '', ...(seedCode ? { seedCode } : {}) }),
  });
  if (!response.ok || !response.body) throw new Error(`${response.status}: ${await response.text()}`);
  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = '';
  let result: GenerationResult | null = null;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer = (buffer + value).replaceAll('\r\n', '\n');
    let boundary = buffer.indexOf('\n\n');
    while (boundary !== -1) {
      const frame = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      const event = frame.match(/^event: (.+)$/m)?.[1];
      const data = frame.match(/^data: (.+)$/m)?.[1];
      if (data && event === 'result') result = JSON.parse(data) as GenerationResult;
      else if (data && (event === 'progress' || event === 'log')) onEvent(JSON.parse(data) as GenerationEvent);
      boundary = buffer.indexOf('\n\n');
    }
  }
  if (!result) throw new Error('Generation stream ended without a result.');
  return result;
}

export const api = {
  health: async (): Promise<boolean> => {
    try {
      const { apiBase, token } = await connection();
      return (await fetch(`${apiBase}/health`, { headers: { 'X-DS1TR-Token': token } })).ok;
    } catch {
      return false;
    }
  },
  decodeSeed: (seedCode: string) => post<DecodedSeedPayload>('/seed/decode', { seedCode }, true),
  history: () => post<{ entries: RunHistoryEntry[] }>('/history/list', {}),
  loadSettings: () => post<{ found: boolean; persisted: PersistedSettings | null; warning: string | null }>('/settings/load', {}),
  saveSettings: (settings: GenerationSettings, generateSpoilerLog: boolean) => post<{ ok: boolean }>('/settings/save', { settings, generateSpoilerLog }, true),
  detectGame: () => post<{ installations: DetectedInstallation[] }>('/game/detect', {}),
  prepareWorldData: (gamePath: string) => post<WorldDataPreparation>('/world-data/prepare', { gamePath }, true),
  validateInstallation: (gamePath: string) => post<InstallationValidation>('/installation/validate', { gamePath }),
  plan: (config: RandomizerConfig) => post<PipelinePlan>('/pipeline/plan', config),
  state: (gamePath: string) => post<GameStateReport>('/game/state', { gamePath }),
  importPristine: (gamePath: string) => post<OperationResult<{ snapshot: { snapshotId: string } }>>('/game/import', { gamePath }, true),
  activate: (gamePath: string, packagePath: string) => post<OperationResult<object>>('/game/activate', { gamePath, packagePath }, true),
  restore: (gamePath: string) => post<OperationResult<{ restored: number; state: GameStateReport }>>('/game/restore', { gamePath }, true),
  enemyPool: (gamePath: string) => post<{ pool: EnemyPoolDescription } | { ok: false; code: string; message: string }>('/enemy-pool', { gamePath }, true),
  exportPreset: (config: RandomizerConfig) => post<PresetDocument>('/preset/export', config),
  importPreset: (text: string) => post<PresetImportResult>('/preset/import', { text }, true),
  generate,
};
