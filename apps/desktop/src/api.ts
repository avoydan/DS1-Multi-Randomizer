import type { GameStateReport, GenerationEvent, GenerationResult, InstallationValidation, OperationResult, PipelinePlan, RandomizerConfig } from '@ds1tr/contracts';

const API = 'http://127.0.0.1:43177/api';

async function post<T>(path: string, body: unknown, allowFailure = false): Promise<T> {
  const response = await fetch(`${API}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });

  if (!response.ok && !allowFailure) {
    const detail = await response.text();
    throw new Error(`${response.status}: ${detail}`);
  }
  return response.json() as Promise<T>;
}

export async function generate(config: RandomizerConfig, onEvent: (event: GenerationEvent) => void): Promise<GenerationResult> {
  const response = await fetch(`${API}/runs/generate`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
    body: JSON.stringify(config),
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
      return (await fetch(`${API}/health`)).ok;
    } catch {
      return false;
    }
  },
  validateInstallation: (gamePath: string) => post<InstallationValidation>('/installation/validate', { gamePath }),
  plan: (config: RandomizerConfig) => post<PipelinePlan>('/pipeline/plan', config),
  state: (gamePath: string) => post<GameStateReport>('/game/state', { gamePath }),
  importPristine: (gamePath: string) => post<OperationResult<{ snapshot: { snapshotId: string } }>>('/game/import', { gamePath }, true),
  activate: (gamePath: string, packagePath: string) => post<OperationResult<object>>('/game/activate', { gamePath, packagePath }, true),
  restore: (gamePath: string) => post<OperationResult<{ restored: number; state: GameStateReport }>>('/game/restore', { gamePath }, true),
  generate,
};
