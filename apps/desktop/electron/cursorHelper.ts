import { spawn, type ChildProcess } from 'node:child_process';

export interface CursorHelperDependencies {
  platform: string;
  ownerPid: number;
  helperPath: () => string;
  exists: (file: string) => Promise<boolean>;
  spawn: (file: string, args: string[], options: { detached: true; stdio: 'ignore'; windowsHide: true; shell: false }) => ChildProcess;
  log: (message: string) => void;
}

/** Owns the optional Windows shortcut helper. It has no connection to generation settings or the backend. */
export class CursorHelper {
  private child: ChildProcess | null = null;
  private request = 0;
  private shortcut = 'F10';
  private enabled = false;
  private activeShortcut: string | null = null;
  private transition: Promise<unknown> = Promise.resolve();

  constructor(private readonly dependencies: CursorHelperDependencies) {}

  setEnabled(enabled: boolean, shortcut = this.shortcut): Promise<boolean> {
    this.shortcut = shortcut;
    this.enabled = enabled;
    const request = ++this.request;
    const operation = this.transition.then(async () => {
      if (!enabled) { await this.stop(); return false; }
      if (this.child && this.child.exitCode === null && this.activeShortcut === this.shortcut) return true;
      await this.stop();
      return this.start(request);
    });
    this.transition = operation.then(() => undefined, () => undefined);
    return operation;
  }

  setShortcut(shortcut: string): Promise<boolean> {
    this.shortcut = shortcut;
    if (!this.enabled) return Promise.resolve(true);
    const request = ++this.request;
    const operation = this.transition.then(async () => {
      if (!this.enabled) return true;
      await this.stop();
      return this.start(request);
    });
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
      const child = this.dependencies.spawn(file, [this.shortcut, String(this.dependencies.ownerPid)], { detached: true, stdio: 'ignore', windowsHide: true, shell: false });
      child.unref();
      this.child = child;
      child.once('error', (error) => {
        this.dependencies.log(`free cursor helper failed to start: ${error.message}`);
        if (this.child === child) { this.child = null; this.activeShortcut = null; }
      });
      child.once('exit', (code) => {
        if (this.child === child) { this.child = null; this.activeShortcut = null; }
        if (code !== 0 && code !== null) this.dependencies.log(`free cursor helper exited with code ${code}`);
      });
      this.activeShortcut = this.shortcut;
      return true;
    } catch (error) {
      this.dependencies.log(`free cursor helper failed to start: ${error instanceof Error ? error.message : String(error)}`);
      return false;
    }
  }

  async stop(): Promise<void> {
    const child = this.child;
    this.child = null;
    this.activeShortcut = null;
    if (!child || child.exitCode !== null) return;
    await new Promise<void>((resolve) => {
      let settled = false;
      const finish = () => { if (settled) return; settled = true; clearTimeout(timeout); resolve(); };
      const timeout = setTimeout(() => { child.kill(); finish(); }, 1500);
      child.once('exit', finish);
      try { child.kill(); } catch { finish(); }
    });
  }
}

export function createCursorHelper(helperPath: () => string, exists: (file: string) => Promise<boolean>, log: (message: string) => void): CursorHelper {
  return new CursorHelper({ platform: process.platform, ownerPid: process.pid, helperPath, exists, spawn: (file, args, options) => spawn(file, args, options), log });
}
