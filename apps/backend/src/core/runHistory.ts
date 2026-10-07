import { randomUUID } from 'node:crypto';
import { access, mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { GenerationSettings, RunHistoryEntry, RunStatus } from '@ds1tr/contracts';

/**
 * Persistent run history: what the person generated, with enough to recreate it from the Seed Code alone. It survives restarts,
 * does not depend on the run folder still existing, and holds only lightweight metadata (docs/SEED_FORMAT.md, "Run history").
 */
const HISTORY_VERSION = 1;
const MAX_ENTRIES = 200;

export interface NewRun {
  seedCode: string;
  profile: string;
  masterSeed: string;
  settings: GenerationSettings;
  packagePath: string;
  manifestPath: string;
  spoilerPath: string | null;
  hintsPath?: string | null;
  now?: Date;
}

const exists = async (target: string): Promise<boolean> => { try { await access(target); return true; } catch { return false; } };

export class RunHistory {
  readonly #file: string;
  #queue: Promise<unknown> = Promise.resolve();

  constructor(stateRoot: string) {
    this.#file = path.join(stateRoot, 'history', 'runs.json');
  }

  async #read(): Promise<RunHistoryEntry[]> {
    let text: string;
    try { text = await readFile(this.#file, 'utf8'); } catch { return []; }
    try {
      const parsed = JSON.parse(text) as { schemaVersion?: number; entries?: RunHistoryEntry[] };
      if (parsed.schemaVersion === HISTORY_VERSION && Array.isArray(parsed.entries)) return parsed.entries;
    } catch { /* fall through: a damaged file is set aside, not trusted */ }
    await rename(this.#file, `${this.#file}.damaged-${Date.now()}`).catch(() => undefined);
    return [];
  }

  async #write(entries: RunHistoryEntry[]): Promise<void> {
    await mkdir(path.dirname(this.#file), { recursive: true });
    const temporary = `${this.#file}.${process.pid}.tmp`;
    await writeFile(temporary, JSON.stringify({ schemaVersion: HISTORY_VERSION, entries: entries.slice(0, MAX_ENTRIES) }, null, 2));
    await rename(temporary, this.#file);
  }

  /** Serializes every change so two requests never overwrite each other. */
  #exclusive<T>(action: () => Promise<T>): Promise<T> {
    const result = this.#queue.then(action, action);
    this.#queue = result.catch(() => undefined);
    return result;
  }

  add(run: NewRun): Promise<RunHistoryEntry> {
    return this.#exclusive(async () => {
      const entry: RunHistoryEntry = {
        id: randomUUID(), createdAt: (run.now ?? new Date()).toISOString(), seedCode: run.seedCode, profile: run.profile,
        masterSeed: run.masterSeed, settings: run.settings, status: 'generated', packagePath: run.packagePath,
        manifestPath: run.manifestPath, spoilerPath: run.spoilerPath, hintsPath: run.hintsPath ?? null,
      };
      await this.#write([entry, ...(await this.#read())]);
      return entry;
    });
  }

  /**
   * The same world generated again (Load seed, Regenerate files) is not a new run: the existing entry keeps its identity (id, Seed
   * Code, first creation time) and only gets the new files. Anything else is a new entry.
   */
  addOrRefresh(run: NewRun): Promise<RunHistoryEntry> {
    return this.#exclusive(async () => {
      const entries = await this.#read();
      const index = entries.findIndex((entry) => entry.seedCode === run.seedCode);
      if (index < 0) {
        const entry: RunHistoryEntry = {
          id: randomUUID(), createdAt: (run.now ?? new Date()).toISOString(), seedCode: run.seedCode, profile: run.profile,
          masterSeed: run.masterSeed, settings: run.settings, status: 'generated', packagePath: run.packagePath,
          manifestPath: run.manifestPath, spoilerPath: run.spoilerPath, hintsPath: run.hintsPath ?? null,
        };
        await this.#write([entry, ...entries]);
        return entry;
      }
      const refreshed: RunHistoryEntry = {
        ...entries[index], packagePath: run.packagePath, manifestPath: run.manifestPath, spoilerPath: run.spoilerPath, hintsPath: run.hintsPath ?? null, status: 'generated',
      };
      await this.#write(entries.map((entry, position) => (position === index ? refreshed : entry)));
      return refreshed;
    });
  }

  remove(id: string): Promise<boolean> {
    return this.#exclusive(async () => {
      const entries = await this.#read();
      const kept = entries.filter((entry) => entry.id !== id);
      if (kept.length === entries.length) return false;
      await this.#write(kept);
      return true;
    });
  }

  async get(id: string): Promise<RunHistoryEntry | null> {
    return (await this.#exclusive(() => this.#read())).find((entry) => entry.id === id) ?? null;
  }

  /** Newest first, with `packageExists` computed now. */
  async list(): Promise<RunHistoryEntry[]> {
    const entries = await this.#exclusive(() => this.#read());
    return Promise.all(entries.map(async (entry) => ({ ...entry, packageExists: await exists(entry.packagePath) })));
  }

  /** The run whose package was applied becomes `applied`; any other run that was applied is no longer. */
  markApplied(packagePath: string): Promise<void> {
    return this.#exclusive(async () => {
      const entries = await this.#read();
      const target = path.resolve(packagePath).toLowerCase();
      await this.#write(entries.map((entry) => {
        if (path.resolve(entry.packagePath).toLowerCase() === target) return { ...entry, status: 'applied' as RunStatus };
        return entry.status === 'applied' ? { ...entry, status: 'restored' as RunStatus } : entry;
      }));
    });
  }

  markRestored(): Promise<void> {
    return this.#exclusive(async () => {
      const entries = await this.#read();
      await this.#write(entries.map((entry) => (entry.status === 'applied' ? { ...entry, status: 'restored' as RunStatus } : entry)));
    });
  }
}
