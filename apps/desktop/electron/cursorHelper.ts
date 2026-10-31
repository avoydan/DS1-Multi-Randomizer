import { execFile, spawn, type ChildProcess } from 'node:child_process';
import path from 'node:path';

export interface CursorHelperDependencies {
  platform: string;
  ownerPid: number;
  helperPath: () => string;
  exists: (file: string) => Promise<boolean>;
  spawn: (file: string, args: string[], options: { detached: true; stdio: 'ignore'; windowsHide: true; shell: false }) => ChildProcess;
  log: (message: string) => void;
  /** True while a Dark Souls Remastered process exists. Never throws; an unanswerable check counts as "not running". */
  isGameRunning: () => Promise<boolean>;
  /** Timer seam; the default uses unref'd setTimeout so the watcher never keeps the application alive. */
  schedule?: (callback: () => void, delayMs: number) => unknown;
  cancel?: (handle: unknown) => void;
  /** Check interval while the game is not running (and the feature is on). */
  idlePollMs?: number;
  /** Safety interval while a helper runs; the helper itself also exits when the game does. */
  activePollMs?: number;
}

export const DEFAULT_IDLE_POLL_MS = 10_000;
export const DEFAULT_ACTIVE_POLL_MS = 15_000;

/**
 * Owns the optional Windows shortcut helper (docs/DESKTOP_PACKAGING.md, "Free cursor"). It has no connection to generation
 * settings or the backend.
 *
 * The helper installs a global low-level keyboard hook, so it must exist only while Dark Souls Remastered runs AND the person
 * enabled the feature. Enabling the feature (or starting the app with it saved on) therefore only ARMS a light presence check;
 * the helper is spawned when the game process is seen, at most once per game session, and stopped when the game is gone or the
 * feature is switched off. Nothing here treats Steam accepting a launch URL as proof that the game started.
 */
export class CursorHelper {
  private child: ChildProcess | null = null;
  private shortcut = 'F10';
  private enabled = false;
  private activeShortcut: string | null = null;
  /** A helper was already started for the game session being observed; cleared when the game is seen absent. */
  private startedThisSession = false;
  private startFailed = false;
  private timer: unknown = null;
  private transition: Promise<unknown> = Promise.resolve();
  private readonly idlePollMs: number;
  private readonly activePollMs: number;

  constructor(private readonly dependencies: CursorHelperDependencies) {
    this.idlePollMs = dependencies.idlePollMs ?? DEFAULT_IDLE_POLL_MS;
    this.activePollMs = dependencies.activePollMs ?? DEFAULT_ACTIVE_POLL_MS;
  }

  /** Arms or disarms the watcher. Resolves true when a helper is running afterwards (the game is up and the feature is on). */
  setEnabled(enabled: boolean, shortcut = this.shortcut): Promise<boolean> {
    this.shortcut = shortcut;
    this.enabled = enabled;
    if (!enabled) this.clearTimer();
    return this.enqueue(true);
  }

  /** Stores the shortcut; a running helper is restarted with it. Resolves false only when that restart fails. */
  setShortcut(shortcut: string): Promise<boolean> {
    this.shortcut = shortcut;
    if (!this.enabled) return Promise.resolve(true);
    return this.enqueue(true).then(() => !this.startFailed);
  }

  /** Resolves when every queued check has finished (tests). */
  whenIdle(): Promise<void> {
    return this.transition.then(() => undefined);
  }

  /** Stops the watcher and any helper this instance started (used by tests and on explicit shutdown). */
  async stop(): Promise<void> {
    this.enabled = false;
    this.clearTimer();
    await this.enqueue(false);
  }

  private clearTimer(): void {
    if (this.timer === null) return;
    (this.dependencies.cancel ?? ((handle) => clearTimeout(handle as NodeJS.Timeout)))(this.timer);
    this.timer = null;
  }

  private enqueue(userRequest: boolean): Promise<boolean> {
    const operation = this.transition.then(() => this.reconcile(userRequest));
    this.transition = operation.then(() => undefined, () => undefined);
    return operation;
  }

  /** The single place that decides whether a helper should exist right now. */
  private async reconcile(userRequest: boolean): Promise<boolean> {
    if (!this.enabled || this.dependencies.platform !== 'win32') {
      await this.stopChild();
      this.startedThisSession = false;
      return false;
    }
    const gameRunning = await this.dependencies.isGameRunning();
    if (!this.enabled) { await this.stopChild(); return false; }
    let running = false;
    if (!gameRunning) {
      this.startedThisSession = false;
      await this.stopChild();
    } else if (this.child && this.child.exitCode === null) {
      if (this.activeShortcut !== this.shortcut) {
        await this.stopChild();
        running = await this.start();
      } else running = true;
    } else if (!this.startedThisSession || userRequest) {
      // `userRequest` (enable / shortcut change) may retry once; the poll never respawns a helper that already ended in this session.
      running = await this.start();
    }
    this.armTimer(running || (this.child !== null && this.child.exitCode === null));
    return running;
  }

  private armTimer(helperRunning: boolean): void {
    this.clearTimer();
    if (!this.enabled) return;
    const schedule = this.dependencies.schedule ?? ((callback, delay) => {
      const handle = setTimeout(callback, delay);
      handle.unref();
      return handle;
    });
    this.timer = schedule(() => { void this.enqueue(false); }, helperRunning ? this.activePollMs : this.idlePollMs);
  }

  private async start(): Promise<boolean> {
    if (this.child && this.child.exitCode === null) return true;
    this.startFailed = false;
    try {
      const file = this.dependencies.helperPath();
      if (!await this.dependencies.exists(file)) {
        this.dependencies.log(`free cursor helper is missing: ${file}`);
        this.startedThisSession = true;
        this.startFailed = true;
        return false;
      }
      if (!this.enabled) return false;
      const child = this.dependencies.spawn(file, [this.shortcut, String(this.dependencies.ownerPid)], { detached: true, stdio: 'ignore', windowsHide: true, shell: false });
      child.unref();
      this.child = child;
      this.startedThisSession = true;
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
      this.startedThisSession = true;
      this.startFailed = true;
      return false;
    }
  }

  private async stopChild(): Promise<void> {
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

const GAME_IMAGE = 'DarkSoulsRemastered.exe';

/**
 * Asks Windows whether DarkSoulsRemastered.exe exists, with one short-lived `tasklist` call (CSV, no header, so the answer does not
 * depend on the display language: a match is a quoted image name, "no tasks" is a localized sentence).
 */
export function createGameProcessProbe(log: (message: string) => void, run: typeof execFile = execFile): () => Promise<boolean> {
  const tasklist = path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'tasklist.exe');
  let reportedFailure = false;
  return () => new Promise<boolean>((resolve) => {
    run(tasklist, ['/FI', `IMAGENAME eq ${GAME_IMAGE}`, '/FO', 'CSV', '/NH'], { windowsHide: true, timeout: 5000 }, (error, stdout) => {
      if (error) {
        if (!reportedFailure) { reportedFailure = true; log(`game process check failed: ${error.message}`); }
        resolve(false);
        return;
      }
      resolve(new RegExp(`^"${GAME_IMAGE.replace('.', '\\.')}"`, 'imu').test(String(stdout)));
    });
  });
}

export function createCursorHelper(helperPath: () => string, exists: (file: string) => Promise<boolean>, log: (message: string) => void): CursorHelper {
  return new CursorHelper({
    platform: process.platform,
    ownerPid: process.pid,
    helperPath,
    exists,
    spawn: (file, args, options) => spawn(file, args, options),
    log,
    isGameRunning: createGameProcessProbe(log),
  });
}
