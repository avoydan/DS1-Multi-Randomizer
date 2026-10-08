import { randomUUID } from 'node:crypto';
import { appendFileSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { type AppLogger, type LogLevel, logStageFor } from './appLog.js';
import type { LogStage, OperationKind, OperationRecord } from '@ds1tr/contracts';
import type { GenerationEvent } from './generationPipeline.js';

/**
 * What the application was doing and how it ended (docs/DIAGNOSTICS.md): one record per kind of operation (generate, apply, restore) plus the last failure,
 * kept in `diagnostics/last-run.json`. This is what makes a support bundle useful after a FAILED generation or a failed Apply, and when no world exists yet.
 * The correlation id ties one operation together across the interface, the backend, the helper programs and the bundle. It is for diagnostics only: it
 * is never part of a Seed Code, a package or a hash, so it cannot make two runs of the same world differ.
 */
export interface OperationStore {
  schemaVersion: 1;
  generate?: OperationRecord;
  apply?: OperationRecord;
  restore?: OperationRecord;
  /** The most recent operation that failed, of any kind. */
  lastFailure?: OperationRecord;
}

const MAX_NOTES = 20;
const clip = (text: string): string => (text.length > 600 ? `${text.slice(0, 600)}...` : text);

export function readOperationStore(file: string): OperationStore {
  try {
    const parsed = JSON.parse(readFileSync(file, 'utf8')) as OperationStore;
    if (parsed.schemaVersion === 1) return parsed;
  } catch { /* none yet, or damaged: start again */ }
  return { schemaVersion: 1 };
}

export class OperationTracker {
  readonly file: string;
  readonly #logger: AppLogger;
  readonly #now: () => Date;

  constructor(stateRoot: string, logger: AppLogger, now: () => Date = () => new Date()) {
    this.file = path.join(stateRoot, 'diagnostics', 'last-run.json');
    this.#logger = logger;
    this.#now = now;
  }

  begin(kind: OperationKind, meta: { seedCode?: string; profile?: string; workspacePath?: string; plan?: { items: boolean; enemies: boolean } } = {}): Operation {
    return new Operation(this, kind, meta, this.#logger, this.#now);
  }

  /** @internal */
  save(record: OperationRecord): void {
    try {
      const store = readOperationStore(this.file);
      store[record.kind] = record;
      if (record.ok === false) store.lastFailure = record;
      mkdirSync(path.dirname(this.file), { recursive: true });
      const temporary = `${this.file}.${process.pid}.tmp`;
      writeFileSync(temporary, JSON.stringify(store, null, 2));
      renameSync(temporary, this.file);
    } catch { /* diagnostics must never break an operation */ }
  }
}

export class Operation {
  readonly correlationId = randomUUID();
  readonly record: OperationRecord;
  readonly #tracker: OperationTracker;
  readonly #logger: AppLogger;
  readonly #now: () => Date;
  readonly #plan: { items: boolean; enemies: boolean };
  #current: { stage: LogStage; since: number } | null = null;

  constructor(tracker: OperationTracker, kind: OperationKind, meta: { seedCode?: string; profile?: string; workspacePath?: string; plan?: { items: boolean; enemies: boolean } }, logger: AppLogger, now: () => Date) {
    this.#tracker = tracker;
    this.#logger = logger;
    this.#now = now;
    this.#plan = meta.plan ?? { items: true, enemies: true };
    this.record = {
      correlationId: this.correlationId, kind, startedAt: now().toISOString(), finishedAt: null, ok: null, code: null, stage: null,
      seedCode: meta.seedCode ?? null, profile: meta.profile ?? null, historyId: null, workspacePath: meta.workspacePath ?? null, packagePath: null,
      timings: {}, counts: { info: 0, warning: 0, error: 0, events: 0 }, windows: {}, warnings: [], errors: [],
    };
    this.#logger.info(kind === 'apply' ? 'APPLY' : kind === 'restore' ? 'RESTORE' : 'SEED', `${kind} started`, { correlationId: this.correlationId });
    this.#tracker.save(this.record);
  }

  /** One event of the generation pipeline: logged with its stage, counted, timed, and appended to the run's own log. */
  event(event: GenerationEvent): void {
    const stage = logStageFor(event.stage, event.type === 'log' ? event.component : undefined, this.#plan);
    const when = this.#now();
    this.#enter(stage, when.getTime());
    const window = this.record.windows[stage];
    this.record.windows[stage] = { first: window?.first ?? when.toISOString(), last: when.toISOString(), events: (window?.events ?? 0) + 1 };
    this.record.counts.events += 1;
    if (event.type === 'progress') {
      this.#logger.debug(stage, `${Math.round(event.progress * 100)}% ${event.message}`, { correlationId: this.correlationId });
      return;
    }
    this.record.counts[event.level] += 1;
    this.#logger.log(event.level as LogLevel, stage, event.message, { correlationId: this.correlationId, ...(event.component ? { source: event.component } : {}) });
    if (event.level === 'warning' && this.record.warnings.length < MAX_NOTES) this.record.warnings.push(clip(event.message));
    if (event.level === 'error' && this.record.errors.length < MAX_NOTES) this.record.errors.push(clip(event.message));
    this.#appendRunLog(when, event.level, stage, event.message);
  }

  /** A line that is not a pipeline event (the interface reporting an error, a Seed Code problem). */
  note(level: LogLevel, stage: LogStage, message: string, code?: string): void {
    this.#logger.log(level, stage, message, { correlationId: this.correlationId, ...(code ? { code } : {}) });
    this.#appendRunLog(this.#now(), level, stage, message);
  }

  set(fields: Partial<Pick<OperationRecord, 'historyId' | 'packagePath' | 'workspacePath' | 'seedCode' | 'profile'>>): void { Object.assign(this.record, fields); }

  finish(result: { ok: true } | { ok: false; code: string; stage?: string }): OperationRecord {
    const finished = this.#now();
    this.#leave(finished.getTime());
    this.record.finishedAt = finished.toISOString();
    this.record.ok = result.ok;
    if (!result.ok) {
      this.record.code = result.code;
      this.record.stage = logStageFor(result.stage ?? '', undefined, this.#plan);
      if (this.record.kind === 'apply') this.record.stage = 'APPLY';
      if (this.record.kind === 'restore') this.record.stage = 'RESTORE';
    }
    this.#logger.log(result.ok ? 'info' : 'error', this.record.kind === 'apply' ? 'APPLY' : this.record.kind === 'restore' ? 'RESTORE' : (this.record.stage ?? 'PACKAGE'),
      result.ok ? `${this.record.kind} finished` : `${this.record.kind} failed: ${result.code}`, { correlationId: this.correlationId, ...(result.ok ? {} : { code: result.code }) });
    this.#tracker.save(this.record);
    return this.record;
  }

  #enter(stage: LogStage, at: number): void {
    if (this.#current?.stage === stage) return;
    this.#leave(at);
    this.#current = { stage, since: at };
  }

  #leave(at: number): void {
    if (!this.#current) return;
    this.record.timings[this.#current.stage] = (this.record.timings[this.#current.stage] ?? 0) + Math.max(0, at - this.#current.since);
    this.#current = null;
  }

  #appendRunLog(when: Date, level: string, stage: LogStage, message: string): void {
    if (!this.record.workspacePath) return;
    try {
      appendFileSync(path.join(this.record.workspacePath, 'generation.log'), `${when.toISOString()} ${level.toUpperCase().padEnd(7)} [${stage}] ${message.replace(/\r?\n/gu, ' | ')}\n`);
    } catch { /* the run folder may be gone */ }
  }
}
