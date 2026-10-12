import { spawn, type ChildProcess } from 'node:child_process';

export interface CursorHelperDependencies {
  platform: string;
  helperPath: () => string;
  exists: (file: string) => Promise<boolean>;
  spawn: (file: string, args: string[], options: { stdio: ['pipe', 'ignore', 'pipe']; windowsHide: true; shell: false }) => ChildProcess;
  log: (message: string) => void;
}

/** Owns the optional Windows watcher. It has no connection to generation settings or the backend. */
export class CursorHelper {
  private child: ChildProcess | null = null;
  private request = 0;
  private transition: Promise<unknown> = Promise.resolve();

  constructor(private readonly dependencies: CursorHelperDependencies) {}

  setEnabled(enabled: boolean): Promise<boolean> {
    const request = ++this.request;
    const operation = this.transition.then(() => enabled ? this.start(request) : this.stop().then(() => false));
    this.transition = operation.then(() => undefined, () => undefined);
    return operation;
  }

  private async start(request: number): Promise<boolean> {
    if (this.dependencies.platform !== 'win32') return false;
    if (this.child && this.child.exitCode === null) return true;
    try {
      const file = this.dependencies.helperPath();
      if (!await this.dependencies.exists(file)) {
        this.dependencies.log(`free cursor helper is missing: ${file}`);
        return false;
      }
      if (request !== this.request) return false;
      const child = this.dependencies.spawn(file, [], { stdio: ['pipe', 'ignore', 'pipe'], windowsHide: true, shell: false });
      this.child = child;
      child.stderr?.on('data', (chunk: Buffer) => this.dependencies.log(`free cursor helper: ${chunk.toString().trim()}`));
      child.once('error', (error) => {
        this.dependencies.log(`free cursor helper failed to start: ${error.message}`);
        if (this.child === child) this.child = null;
      });
      child.once('exit', (code) => {
        if (this.child === child) this.child = null;
        if (code !== 0 && code !== null) this.dependencies.log(`free cursor helper exited with code ${code}`);
      });
      return true;
    } catch (error) {
      this.dependencies.log(`free cursor helper failed to start: ${error instanceof Error ? error.message : String(error)}`);
      return false;
    }
  }

  async stop(): Promise<void> {
    const child = this.child;
    this.child = null;
    if (!child || child.exitCode !== null) return;
    await new Promise<void>((resolve) => {
      let settled = false;
      const finish = () => { if (settled) return; settled = true; clearTimeout(timeout); resolve(); };
      const timeout = setTimeout(() => { child.kill(); finish(); }, 1500);
      child.once('exit', finish);
      try { child.stdin?.end('stop\n'); } catch { child.kill(); finish(); }
    });
  }
}

export function createCursorHelper(helperPath: () => string, exists: (file: string) => Promise<boolean>, log: (message: string) => void): CursorHelper {
  return new CursorHelper({ platform: process.platform, helperPath, exists, spawn: (file, args, options) => spawn(file, args, options), log });
}
