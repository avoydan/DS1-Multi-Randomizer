import type { InstallationValidation, PipelinePlan, RandomizerConfig } from '@ds1tr/contracts';

const API = 'http://127.0.0.1:43177/api';

async function post<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`${API}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const detail = await response.text();
    throw new Error(`${response.status}: ${detail}`);
  }
  return response.json() as Promise<T>;
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
};
