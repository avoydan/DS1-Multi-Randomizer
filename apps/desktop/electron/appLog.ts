import { appendFileSync, existsSync, mkdirSync, renameSync, rmSync, statSync } from 'node:fs';
import path from 'node:path';

/**
 * The desktop application's own log (docs/DIAGNOSTICS.md): the same line format as the backend log (JSON, stage, level), so the support bundle can show
 * both side by side. It records what the main process does (start, language, backend process, file export), never the content of a world, and never a token;
 * paths that may appear in an error are redacted when a support bundle is made. Writing never throws; the file rotates.
 */
export type DesktopLogLevel = 'info' | 'warning' | 'error';

export class DesktopLog {
  constructor(readonly file: string, private readonly maxBytes = 512 * 1024, private readonly keep = 2) {}

  write(level: DesktopLogLevel, message: string, code?: string): void {
    try {
      mkdirSync(path.dirname(this.file), { recursive: true });
      this.rotate();
      const line = { time: new Date().toISOString(), level, stage: 'APP', message: String(message).replace(/\r?\n/gu, ' \\n ').slice(0, 2000), source: 'desktop', ...(code ? { code } : {}) };
      appendFileSync(this.file, `${JSON.stringify(line)}\n`);
    } catch { /* logging must never break the application */ }
  }

  private rotate(): void {
    try {
      if (!existsSync(this.file) || statSync(this.file).size < this.maxBytes) return;
      rmSync(`${this.file}.${this.keep}`, { force: true });
      for (let index = this.keep - 1; index >= 1; index -= 1) if (existsSync(`${this.file}.${index}`)) renameSync(`${this.file}.${index}`, `${this.file}.${index + 1}`);
      renameSync(this.file, `${this.file}.1`);
    } catch { /* keep appending */ }
  }
}

/** installed | portable | development, from what the Electron runtime sets (the portable launcher sets PORTABLE_EXECUTABLE_DIR). */
export function installModeOf(packaged: boolean, env: NodeJS.ProcessEnv): 'installed' | 'portable' | 'development' {
  if (!packaged) return 'development';
  return env.PORTABLE_EXECUTABLE_DIR ? 'portable' : 'installed';
}
