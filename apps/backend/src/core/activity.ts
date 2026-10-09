/**
 * What the backend is doing to files right now (docs/AUTO_UPDATE.md, "An update never interrupts a game-file operation").
 *
 * The desktop shell asks `GET /api/activity` before it restarts the application to install an update: a generation, an Apply, a Restore, the import of the
 * pristine copy, the preparation of the world data, a support bundle or the deletion of a run must not be cut off half-way. The tracker only counts the
 * requests that change or read files for a long time; it holds no secret and no path, and it is never part of a world, a Seed Code or a package.
 */
export type ActivityKind = 'generate' | 'apply' | 'restore' | 'import' | 'prepare' | 'support' | 'history';

/** The routes that count as activity, and what each one is called. Everything else (state queries, decoding a Seed Code, the log) is not file work. */
export const ACTIVITY_ROUTES: ReadonlyMap<string, ActivityKind> = new Map<string, ActivityKind>([
  ['/api/runs/generate', 'generate'],
  ['/api/randomize', 'generate'],
  ['/api/game/activate', 'apply'],
  ['/api/game/restore', 'restore'],
  ['/api/game/import', 'import'],
  ['/api/world-data/prepare', 'prepare'],
  ['/api/support/bundle', 'support'],
  ['/api/history/delete', 'history'],
]);

export interface ActivitySnapshot {
  busy: boolean;
  active: Array<{ kind: ActivityKind; since: string }>;
}

export class ActivityTracker {
  readonly #active = new Map<number, { kind: ActivityKind; since: string }>();
  #next = 1;
  readonly #now: () => Date;

  constructor(now: () => Date = () => new Date()) {
    this.#now = now;
  }

  /** Starts one activity; the returned function ends it (calling it twice is harmless). */
  begin(kind: ActivityKind): () => void {
    const id = this.#next++;
    this.#active.set(id, { kind, since: this.#now().toISOString() });
    return () => { this.#active.delete(id); };
  }

  snapshot(): ActivitySnapshot {
    return { busy: this.#active.size > 0, active: [...this.#active.values()].map((entry) => ({ ...entry })) };
  }
}
