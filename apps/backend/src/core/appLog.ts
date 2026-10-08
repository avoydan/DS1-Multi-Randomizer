import { appendFileSync, existsSync, mkdirSync, readFileSync, renameSync, rmSync, statSync } from 'node:fs';
import path from 'node:path';

/**
 * The application log (docs/DIAGNOSTICS.md): one JSON line per event, with a stage, a level and the correlation id of the operation it belongs to.
 * It is for support only and has no effect on a world (no timestamp or id here ever reaches a Seed Code, a package or a hash). Writing never throws, rotation keeps
 * the file small, and secrets are removed as the line is written.
 */
import { LOG_STAGES, type LogStage } from '@ds1tr/contracts';
export { LOG_STAGES, type LogStage };
export type LogLevel = 'debug' | 'info' | 'warning' | 'error';
export interface LogLine {
  time: string;
  level: LogLevel;
  stage: LogStage;
  message: string;
  correlationId?: string;
  code?: string;
  source?: string;
}

export interface LoggerOptions {
  maxBytes?: number;
  /** Rotated files kept next to the current one. */
  keep?: number;
  redact?: (text: string) => string;
  debug?: boolean;
  now?: () => Date;
}

const MAX_MESSAGE = 4000;

export class AppLogger {
  readonly file: string;
  readonly #maxBytes: number;
  readonly #keep: number;
  readonly #redact: (text: string) => string;
  readonly #debug: boolean;
  readonly #now: () => Date;

  constructor(file: string, options: LoggerOptions = {}) {
    this.file = file;
    this.#maxBytes = options.maxBytes ?? 1024 * 1024;
    this.#keep = options.keep ?? 2;
    this.#redact = options.redact ?? ((text) => text);
    this.#debug = options.debug ?? process.env.DS1TR_DEBUG === '1';
    this.#now = options.now ?? (() => new Date());
  }

  log(level: LogLevel, stage: LogStage, message: string, extra: { correlationId?: string; code?: string; source?: string } = {}): void {
    if (level === 'debug' && !this.#debug) return;
    try {
      const text = this.#redact(String(message)).replace(/\r?\n/gu, ' \\n ').slice(0, MAX_MESSAGE);
      const line: LogLine = { time: this.#now().toISOString(), level, stage, message: text, ...extra };
      mkdirSync(path.dirname(this.file), { recursive: true });
      this.#rotate();
      appendFileSync(this.file, `${JSON.stringify(line)}\n`);
    } catch { /* logging must never break the application */ }
  }

  info(stage: LogStage, message: string, extra?: { correlationId?: string; code?: string; source?: string }): void { this.log('info', stage, message, extra); }
  warning(stage: LogStage, message: string, extra?: { correlationId?: string; code?: string; source?: string }): void { this.log('warning', stage, message, extra); }
  error(stage: LogStage, message: string, extra?: { correlationId?: string; code?: string; source?: string }): void { this.log('error', stage, message, extra); }
  debug(stage: LogStage, message: string, extra?: { correlationId?: string; code?: string; source?: string }): void { this.log('debug', stage, message, extra); }

  #rotate(): void {
    try {
      if (!existsSync(this.file) || statSync(this.file).size < this.#maxBytes) return;
      rmSync(`${this.file}.${this.#keep}`, { force: true });
      for (let index = this.#keep - 1; index >= 1; index -= 1) {
        if (existsSync(`${this.file}.${index}`)) renameSync(`${this.file}.${index}`, `${this.file}.${index + 1}`);
      }
      renameSync(this.file, `${this.file}.1`);
    } catch { /* keep appending to the current file */ }
  }
}

/** The last `maxBytes` of a log file, cut at a line start; '' when it does not exist. */
export function readLogTail(file: string, maxBytes: number): string {
  try {
    const buffer = readFileSync(file);
    if (buffer.length <= maxBytes) return buffer.toString('utf8');
    const text = buffer.subarray(buffer.length - maxBytes).toString('utf8');
    return text.slice(text.indexOf('\n') + 1);
  } catch { return ''; }
}

export function parseLogLines(text: string): LogLine[] {
  const lines: LogLine[] = [];
  for (const raw of text.split('\n')) {
    if (!raw.trim()) continue;
    try { const parsed = JSON.parse(raw) as LogLine; if (parsed && typeof parsed.message === 'string') lines.push(parsed); } catch { /* a damaged line is skipped */ }
  }
  return lines;
}

/**
 * Where a generation event belongs among the stages. The combined engine runs the item and enemy plans; the data tool and the world randomizer are helper
 * programs whose output arrives tagged by their component.
 */
export function logStageFor(stage: string, component: string | undefined, plan: { items: boolean; enemies: boolean }): LogStage {
  if (component === 'data-tool') return 'DATA_TOOL';
  if (component === 'fog-host') return 'FOG_PLAN';
  switch (stage) {
    case 'verify': return 'GAME_DETECTION';
    case 'import': return 'SNAPSHOT';
    case 'combined': return plan.items ? 'ITEM_PLAN' : plan.enemies ? 'ENEMY_PLAN' : 'ITEM_PLAN';
    case 'fog': return 'FOG_PLAN';
    case 'validation': return 'MERGE';
    case 'package': return 'PACKAGE';
    case 'seed': return 'SEED';
    default: return 'SERVER';
  }
}
