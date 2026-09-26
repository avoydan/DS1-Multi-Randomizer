import { access } from 'node:fs/promises';
import path from 'node:path';
import type { EngineName, EngineStatus } from '@ds1tr/contracts';
import type { EngineRunContext, RandomizerAdapter } from './types.js';

export interface ExternalAdapterOptions {
  name: EngineName;
  engineRoot: string;
  expectedSource?: string;
  expectedExecutable?: string;
}

async function exists(target?: string): Promise<boolean> {
  if (!target) return false;
  try {
    await access(target);
    return true;
  } catch {
    return false;
  }
}

export class ExternalAdapter implements RandomizerAdapter {
  readonly name: EngineName;

  constructor(private readonly options: ExternalAdapterOptions) {
    this.name = options.name;
  }

  async status(): Promise<EngineStatus> {
    const source = this.options.expectedSource
      ? path.join(this.options.engineRoot, this.options.expectedSource)
      : undefined;
    const executable = this.options.expectedExecutable
      ? path.join(this.options.engineRoot, this.options.expectedExecutable)
      : undefined;

    if (await exists(source)) {
      return { name: this.name, ready: false, mode: 'source', detail: 'Source detected; adapter bridge still needs implementation.' };
    }
    if (await exists(executable)) {
      return { name: this.name, ready: false, mode: 'executable', detail: 'Executable detected; CLI bridge still needs implementation.' };
    }
    return { name: this.name, ready: false, mode: 'missing', detail: 'Engine files are not installed in this workspace yet.' };
  }

  async validate(_ctx: EngineRunContext): Promise<void> {
    const status = await this.status();
    if (status.mode === 'missing') throw new Error(`${this.name} engine is missing.`);
  }

  async run(_ctx: EngineRunContext): Promise<void> {
    throw new Error(`${this.name} adapter is scaffolded but not wired to the upstream randomizer yet.`);
  }
}
