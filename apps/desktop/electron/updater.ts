/**
 * Application updates (docs/AUTO_UPDATE.md). This module is the whole policy of the updater and nothing else: it holds the update state machine, the check
 * throttle, the rules for a portable build and for a build without an update source, and the refusal to restart while a game-file operation runs. It never imports
 * `electron` or `electron-updater`: the real updater, the clock, the settings and the backend probe are handed in, so every rule is tested without a network,
 * without Electron and without GitHub (updater.test.mjs). main.ts wires the real parts and exposes the narrow IPC surface below to the renderer.
 *
 * What the updater is NOT allowed to do, by construction: install without a click (autoInstallOnAppQuit is off, nothing here restarts the application unless the
 * person asked and the backend reported no running operation), follow a prerelease, downgrade, or touch the game (an update replaces this application only;
 * it never restores, re-applies or edits game files).
 */

export const UPDATE_PROVIDER = 'github' as const;

/** Where updates come from. Explicit on purpose (docs/AUTO_UPDATE.md, "The update source"): a packaged client must always know its one controlled repository. */
export interface UpdateSource {
  readonly provider: typeof UPDATE_PROVIDER;
  readonly owner: string;
  readonly repo: string;
}

/** The value of an update source that has not been chosen yet; a build with it cannot update (and a public release cannot be made with it). */
export const UNSET_OWNER = 'REPLACE_WITH_GITHUB_OWNER';

export const isUpdateSourceConfigured = (source: UpdateSource): boolean =>
  source.provider === UPDATE_PROVIDER && /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$/u.test(source.owner) && source.owner !== UNSET_OWNER && /^[A-Za-z0-9._-]{1,100}$/u.test(source.repo);

/** The IPC channels the renderer may use, and the one event the main process pushes. Nothing else about the updater is reachable from the page. */
export const UPDATE_INVOKE_CHANNELS = [
  'updates:get', 'updates:check', 'updates:download', 'updates:install', 'updates:open-release', 'updates:set-auto-check',
] as const;
export const UPDATE_STATE_EVENT = 'updates:state' as const;

export type UpdatePhase = 'idle' | 'checking' | 'upToDate' | 'available' | 'downloading' | 'downloaded' | 'installing' | 'error' | 'unsupported';
/** auto: an installed build downloads and installs by itself on request; manual: a portable build only learns that a version exists; none: no update at all. */
export type UpdateCapability = 'auto' | 'manual' | 'none';
export type UpdateUnsupportedReason = 'development' | 'no-update-source';
export type UpdateErrorCode = 'CHECK_FAILED' | 'DOWNLOAD_FAILED' | 'INSTALL_FAILED' | 'OPERATION_ACTIVE' | 'INVALID_STATE' | 'NOT_SUPPORTED';
export type InstallMode = 'installed' | 'portable' | 'development';

export interface UpdateErrorInfo {
  code: UpdateErrorCode;
  /** A short technical text for the details line (no path, clipped); the interface shows its own translated sentence for the code. */
  detail: string;
}

export interface UpdateState {
  phase: UpdatePhase;
  currentVersion: string;
  availableVersion: string | null;
  releaseDate: string | null;
  /** Percent while a download runs. */
  progress: number | null;
  error: UpdateErrorInfo | null;
  capability: UpdateCapability;
  unsupportedReason: UpdateUnsupportedReason | null;
  autoCheck: boolean;
  /** When the last check ended (success or failure), ISO 8601. */
  lastCheckAt: string | null;
  /** The trusted release page of the available version (built from the explicit update source, never taken from the release metadata). */
  releaseUrl: string | null;
  /** The last attempt to install was refused because a game-file operation was running. */
  installBlocked: boolean;
}

export interface UpdatePrefs {
  autoCheck: boolean;
  lastCheckAt: string | null;
  lastCheckOk: boolean;
}

/** The part of electron-updater's AppUpdater that the controller uses (a structural type, so a test can stand in for it). */
export interface UpdaterLike {
  autoDownload: boolean;
  autoInstallOnAppQuit: boolean;
  allowPrerelease: boolean;
  allowDowngrade: boolean;
  setFeedURL(options: { provider: 'github'; owner: string; repo: string }): void;
  checkForUpdates(): Promise<{ isUpdateAvailable?: boolean; updateInfo: { version: string; releaseDate?: string } } | null>;
  downloadUpdate(): Promise<unknown>;
  quitAndInstall(isSilent?: boolean, isForceRunAfter?: boolean): void;
  on(event: 'download-progress', listener: (info: { percent: number }) => void): unknown;
  on(event: 'error', listener: (error: Error) => void): unknown;
}

export interface UpdateControllerOptions {
  /** Called only when the build can update at all; never in development or without a configured source. */
  createUpdater: () => UpdaterLike;
  currentVersion: string;
  mode: InstallMode;
  source: UpdateSource;
  prefs: { get(): UpdatePrefs; set(patch: Partial<UpdatePrefs>): Promise<void> };
  /** True while the backend is generating, applying, restoring or otherwise working on files. */
  isOperationActive: () => Promise<boolean>;
  openExternal: (url: string) => Promise<void> | void;
  now?: () => number;
  log?: (level: 'info' | 'warning' | 'error', message: string, code?: string) => void;
}

/** After a successful check the next automatic one waits a day; after a failed one an hour (a laptop that was offline at start should try again soon, not once a day). */
export const AUTO_CHECK_INTERVAL_MS = 24 * 60 * 60 * 1000;
export const AUTO_CHECK_RETRY_MS = 60 * 60 * 1000;
/** The first automatic check runs well after the window is up, so it never competes with the first paint or the backend start. */
export const STARTUP_CHECK_DELAY_MS = 20_000;
/** An application left open for days checks again at most this often (the throttle above still decides whether a check really runs). */
export const PERIODIC_CHECK_MS = 6 * 60 * 60 * 1000;

const clip = (text: string): string => String(text).replace(/[A-Za-z]:\\[^\s"']*/gu, '<path>').replace(/\r?\n/gu, ' ').slice(0, 300);

const SEMVER = /^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$/u;

/** True when `remote` is a stable (no prerelease part) version strictly newer than `current`. Anything that is not a version is not newer. */
export function isNewerStable(remote: string, current: string): boolean {
  const a = SEMVER.exec(remote);
  const b = SEMVER.exec(current);
  if (!a || !b || a[4]) return false;
  for (let index = 1; index <= 3; index += 1) {
    const difference = Number(a[index]) - Number(b[index]);
    if (difference !== 0) return difference > 0;
  }
  // Equal numbers: a stable remote is newer than a prerelease current of the same numbers.
  return b[4] !== undefined;
}

/** The release page of a version, from the explicit source: https://github.com/<owner>/<repo>/releases/tag/v<version>; null for anything that is not a plain version. */
export function releaseUrlFor(source: UpdateSource, version: string | null): string | null {
  if (!isUpdateSourceConfigured(source)) return null;
  const base = `https://github.com/${source.owner}/${source.repo}/releases`;
  if (version === null) return `${base}/latest`;
  return /^\d+\.\d+\.\d+$/u.test(version) ? `${base}/tag/v${version}` : `${base}/latest`;
}

export type UpdateListener = (state: UpdateState) => void;

export class UpdateController {
  readonly #options: UpdateControllerOptions;
  readonly #now: () => number;
  readonly #listeners = new Set<UpdateListener>();
  readonly capability: UpdateCapability;
  #updater: UpdaterLike | null = null;
  #state: UpdateState;
  #pending: Promise<UpdateState> | null = null;

  constructor(options: UpdateControllerOptions) {
    this.#options = options;
    this.#now = options.now ?? Date.now;
    const unsupportedReason: UpdateUnsupportedReason | null = options.mode === 'development' ? 'development' : !isUpdateSourceConfigured(options.source) ? 'no-update-source' : null;
    this.capability = unsupportedReason !== null ? 'none' : options.mode === 'portable' ? 'manual' : 'auto';
    const prefs = options.prefs.get();
    this.#state = {
      phase: this.capability === 'none' ? 'unsupported' : 'idle',
      currentVersion: options.currentVersion,
      availableVersion: null,
      releaseDate: null,
      progress: null,
      error: null,
      capability: this.capability,
      unsupportedReason,
      autoCheck: prefs.autoCheck,
      lastCheckAt: prefs.lastCheckAt,
      releaseUrl: null,
      installBlocked: false,
    };
    if (this.capability !== 'none') this.#configure();
  }

  /** The one place the real updater is configured: stable releases only, nothing downloaded or installed unless asked, the source set explicitly. */
  #configure(): void {
    const updater = this.#options.createUpdater();
    this.#updater = updater;
    updater.autoDownload = false;
    updater.autoInstallOnAppQuit = false;
    updater.allowPrerelease = false;
    updater.allowDowngrade = false;
    const { owner, repo } = this.#options.source;
    updater.setFeedURL({ provider: UPDATE_PROVIDER, owner, repo });
    updater.on('download-progress', (info) => {
      if (this.#state.phase !== 'downloading') return;
      this.#set({ progress: Math.max(0, Math.min(100, Math.round(info.percent))) });
    });
    // An 'error' event without a listener would throw inside the main process. The failing call reports its own error; this one only covers the installer failing to start.
    updater.on('error', (error) => {
      this.#options.log?.('warning', `updater error: ${clip(error?.message ?? String(error))}`);
      if (this.#state.phase === 'installing') this.#set({ phase: 'error', error: { code: 'INSTALL_FAILED', detail: clip(error?.message ?? 'install failed') } });
    });
  }

  getState(): UpdateState {
    return { ...this.#state, error: this.#state.error ? { ...this.#state.error } : null };
  }

  subscribe(listener: UpdateListener): () => void {
    this.#listeners.add(listener);
    return () => { this.#listeners.delete(listener); };
  }

  #set(patch: Partial<UpdateState>): void {
    this.#state = { ...this.#state, ...patch };
    const snapshot = this.getState();
    for (const listener of this.#listeners) {
      try { listener(snapshot); } catch { /* a listener must never break the updater */ }
    }
  }

  /**
   * Looks for a newer version. A manual check always runs; an automatic one (`manual: false`) runs only when the person left automatic checks on and the
   * throttle allows it. A check never replaces a download that is running or finished, and a failure is reported, never thrown: the application stays usable.
   */
  check(options: { manual: boolean }): Promise<UpdateState> {
    if (this.capability === 'none') return Promise.resolve(this.getState());
    if (['downloading', 'downloaded', 'installing'].includes(this.#state.phase)) return Promise.resolve(this.getState());
    if (!options.manual) {
      const prefs = this.#options.prefs.get();
      if (!prefs.autoCheck) return Promise.resolve(this.getState());
      if (prefs.lastCheckAt !== null) {
        const last = Date.parse(prefs.lastCheckAt);
        const wait = prefs.lastCheckOk ? AUTO_CHECK_INTERVAL_MS : AUTO_CHECK_RETRY_MS;
        if (Number.isFinite(last) && this.#now() - last >= 0 && this.#now() - last < wait) return Promise.resolve(this.getState());
      }
    }
    if (this.#pending) return this.#pending;
    this.#pending = this.#runCheck().finally(() => { this.#pending = null; });
    return this.#pending;
  }

  async #runCheck(): Promise<UpdateState> {
    const updater = this.#updater;
    if (!updater) return this.getState();
    this.#set({ phase: 'checking', error: null, progress: null, installBlocked: false });
    const stamp = new Date(this.#now()).toISOString();
    try {
      const result = await updater.checkForUpdates();
      await this.#options.prefs.set({ lastCheckAt: stamp, lastCheckOk: true });
      this.#set({ lastCheckAt: stamp });
      if (result === null) {
        this.#set({ phase: 'unsupported', unsupportedReason: 'development' });
        return this.getState();
      }
      const version = result.updateInfo.version;
      // The updater's own comparison and ours must both say "newer": a prerelease or an older version published by mistake is never offered.
      if (result.isUpdateAvailable !== false && isNewerStable(version, this.#options.currentVersion)) {
        this.#set({ phase: 'available', availableVersion: version, releaseDate: result.updateInfo.releaseDate ?? null, releaseUrl: releaseUrlFor(this.#options.source, version) });
        this.#options.log?.('info', `update ${version} is available (current ${this.#options.currentVersion})`);
      } else {
        this.#set({ phase: 'upToDate', availableVersion: null, releaseDate: null, releaseUrl: null });
      }
    } catch (error) {
      const detail = clip(error instanceof Error ? error.message : String(error));
      await this.#options.prefs.set({ lastCheckAt: stamp, lastCheckOk: false }).catch(() => undefined);
      this.#options.log?.('warning', `update check failed: ${detail}`, 'UPDATE_CHECK_FAILED');
      this.#set({ phase: 'error', error: { code: 'CHECK_FAILED', detail }, lastCheckAt: stamp });
    }
    return this.getState();
  }

  /** The automatic check: the same check, but it obeys the setting and the throttle. */
  maybeAutoCheck(): Promise<UpdateState> {
    return this.check({ manual: false });
  }

  /** Downloads the version that was offered. Only an installed build downloads; a portable build is told to update by hand. */
  async download(): Promise<UpdateState> {
    const updater = this.#updater;
    if (this.capability !== 'auto' || !updater) {
      this.#set({ error: { code: 'NOT_SUPPORTED', detail: 'this build updates by hand' } });
      return this.getState();
    }
    const retry = this.#state.phase === 'error' && this.#state.availableVersion !== null;
    if (this.#state.phase !== 'available' && !retry) {
      this.#set({ error: { code: 'INVALID_STATE', detail: `cannot download while ${this.#state.phase}` } });
      return this.getState();
    }
    this.#set({ phase: 'downloading', progress: 0, error: null, installBlocked: false });
    try {
      await updater.downloadUpdate();
      this.#set({ phase: 'downloaded', progress: 100 });
      this.#options.log?.('info', `update ${this.#state.availableVersion} downloaded`);
    } catch (error) {
      const detail = clip(error instanceof Error ? error.message : String(error));
      this.#options.log?.('warning', `update download failed: ${detail}`, 'UPDATE_DOWNLOAD_FAILED');
      this.#set({ phase: 'error', progress: null, error: { code: 'DOWNLOAD_FAILED', detail } });
    }
    return this.getState();
  }

  /**
   * Restarts into the new version. Refused unless the update is downloaded, the person asked, and no game-file operation is running: a generation, an Apply or a
   * Restore cut off by a restart could leave files half-written. If the backend cannot be asked while it is running, the answer is "busy", never "idle".
   */
  async install(): Promise<{ ok: boolean; code?: UpdateErrorCode; state: UpdateState }> {
    const updater = this.#updater;
    if (this.capability !== 'auto' || !updater) return { ok: false, code: 'NOT_SUPPORTED', state: this.getState() };
    if (this.#state.phase !== 'downloaded') {
      this.#set({ error: { code: 'INVALID_STATE', detail: `cannot install while ${this.#state.phase}` } });
      return { ok: false, code: 'INVALID_STATE', state: this.getState() };
    }
    let busy = true;
    try { busy = await this.#options.isOperationActive(); } catch { busy = true; }
    if (busy) {
      this.#options.log?.('info', 'update install refused: a game-file operation is running');
      this.#set({ installBlocked: true });
      return { ok: false, code: 'OPERATION_ACTIVE', state: this.getState() };
    }
    this.#set({ phase: 'installing', installBlocked: false, error: null });
    this.#options.log?.('info', `installing update ${this.#state.availableVersion}`);
    try {
      // Silent (the wizard is not shown again), and the new version starts by itself afterwards.
      updater.quitAndInstall(true, true);
    } catch (error) {
      const detail = clip(error instanceof Error ? error.message : String(error));
      this.#set({ phase: 'error', error: { code: 'INSTALL_FAILED', detail } });
      return { ok: false, code: 'INSTALL_FAILED', state: this.getState() };
    }
    return { ok: true, state: this.getState() };
  }

  async setAutoCheck(enabled: boolean): Promise<UpdateState> {
    await this.#options.prefs.set({ autoCheck: enabled });
    this.#set({ autoCheck: enabled });
    return this.getState();
  }

  /** Opens the trusted release page: of the available version when there is one, otherwise the latest release. The address is built here, never read from the metadata. */
  async openReleasePage(): Promise<boolean> {
    const url = releaseUrlFor(this.#options.source, this.#state.availableVersion);
    if (url === null) return false;
    await this.#options.openExternal(url);
    return true;
  }
}
