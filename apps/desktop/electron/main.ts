import { app, BrowserWindow, dialog, ipcMain, Menu, shell, type IpcMainInvokeEvent } from 'electron';
import electronUpdater from 'electron-updater';
import { spawn, execFile, type ChildProcess } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { readFile, writeFile, mkdir, access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { migrateSettings, normalizeGamePath, type StoredSettings } from './settings.js';
import { launchGame } from './launchGame.js';
import { DesktopLog, installModeOf } from './appLog.js';
import { MAX_SAVE_BYTES, SAVE_KINDS, parseSaveRequest, saveBytes } from './saveFile.js';
import { isDevToolsShortcut, parseBuildChannel, type BuildChannel } from './channel.js';
import { UPDATE_SOURCE } from './updateSource.js';
import { PERIODIC_CHECK_MS, STARTUP_CHECK_DELAY_MS, UPDATE_STATE_EVENT, UpdateController, type UpdaterLike } from './updater.js';
import { mainMessage, normalizeLanguageSetting, resolveLanguage, type AppLanguage, type LanguageSetting, type MainMessageKey } from './language.js';

const currentDirectory = path.dirname(fileURLToPath(import.meta.url));
const runtimeRoot = path.join(process.resourcesPath, 'runtime');
const settingsPath = () => path.join(app.getPath('userData'), 'settings.json');
let backend: ChildProcess | null = null;
let backendUrl: string | null = null;
let backendError: string | null = null;
let sessionToken = '';
let settings: StoredSettings = {};
let quitting = false;
let mainWindow: BrowserWindow | null = null;
/** Application updates (docs/AUTO_UPDATE.md): the policy lives in updater.ts; this file only wires the real parts. */
let updates: UpdateController | null = null;
/** Whether the internal test tools are shown: always in an internal or development build, and in a public build only after the optional shortcut (this session only). */
let buildChannel: BuildChannel = 'public';
let devToolsShown = false;
/** The application's own log (userData/logs/app.log); the backend and the support bundle read it. */
const desktopLog = new DesktopLog(path.join(app.getPath('userData'), 'logs', 'app.log'));

/** The language the person chose, or the operating system's when it is Auto (docs/I18N.md). Resolved before the window exists. */
const systemLocales = (): string[] => [...app.getPreferredSystemLanguages(), app.getLocale()];
const languageSetting = (): LanguageSetting => settings.language ?? 'auto';
const currentLanguage = (): AppLanguage => resolveLanguage(settings.language, systemLocales());
const say = (key: MainMessageKey, params?: Record<string, string | number>): string => mainMessage(currentLanguage(), key, params);

async function saveSettings(): Promise<void> {
  try {
    await mkdir(app.getPath('userData'), { recursive: true });
    await writeFile(settingsPath(), JSON.stringify(settings, null, 2));
  } catch { /* settings are a convenience; the app works without them */ }
}

async function loadBuildChannel(): Promise<void> {
  if (!app.isPackaged) { buildChannel = 'internal'; devToolsShown = true; return; }
  try { buildChannel = parseBuildChannel(await readFile(path.join(runtimeRoot, 'build-info.json'), 'utf8')); } catch { buildChannel = parseBuildChannel(null); }
  devToolsShown = buildChannel === 'internal';
}

async function loadSettings(): Promise<void> {
  try {
    const migrated = migrateSettings(JSON.parse(await readFile(settingsPath(), 'utf8')));
    settings = migrated.settings;
    if (migrated.changed) await saveSettings();
  } catch { settings = {}; }
}

async function stopBackend(): Promise<void> {
  const child = backend;
  backend = null;
  backendUrl = null;
  if (!child?.pid || child.exitCode !== null) return;
  const graceful = new Promise<boolean>((resolve) => {
    const timeout = setTimeout(() => resolve(false), 3000);
    child.once('exit', () => { clearTimeout(timeout); resolve(true); });
    try { child.send('shutdown'); } catch { clearTimeout(timeout); resolve(false); }
  });
  if (await graceful) return;
  if (process.platform === 'win32') {
    await new Promise<void>((resolve) => {
      execFile('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true }, () => resolve());
    });
  } else {
    child.kill();
  }
}

async function startBackend(): Promise<void> {
  if (!app.isPackaged) return;
  await stopBackend();
  backendError = null;
  sessionToken = randomUUID();
  const entry = path.join(runtimeRoot, 'apps', 'backend', 'dist', 'server.js');
  const dataTool = path.join(runtimeRoot, 'engines', 'combined', 'vendor', 'tools', 'DsrDataTool', 'DsrDataTool.exe');
  const child = spawn(process.execPath, [entry], {
    cwd: app.getPath('userData'),
    env: {
      ...process.env,
      ELECTRON_RUN_AS_NODE: '1',
      DS1TR_HOST: '127.0.0.1',
      DS1TR_PORT: '0',
      DS1TR_API_TOKEN: sessionToken,
      DS1TR_RUNTIME_ROOT: runtimeRoot,
      DS1TR_DOTNET_PATH: dataTool,
      DS1TR_DATA_TOOL_EXE: dataTool,
      DS1TR_STATE_DIR: path.join(app.getPath('userData'), 'state'),
      DS1TR_USER_DATA: app.getPath('userData'),
      DS1TR_APP_LOG: desktopLog.file,
      DS1TR_DESKTOP_VERSION: app.getVersion(),
      DS1TR_INSTALL_MODE: installModeOf(app.isPackaged, process.env),
    },
    stdio: ['ignore', 'pipe', 'pipe', 'ipc'],
    windowsHide: true,
  });
  backend = child;
  desktopLog.write('info', 'backend process started');
  let output = '';
  let errors = '';
  await new Promise<void>((resolve) => {
    const timeout = setTimeout(() => finish(say('backendTimeout')), 15000);
    let finished = false;
    function finish(error?: string): void {
      if (finished) return;
      finished = true;
      clearTimeout(timeout);
      if (error) { backendError = `${error}${errors ? ` ${errors.slice(-500)}` : ''}`; desktopLog.write('error', `backend startup problem: ${backendError}`, 'BACKEND_UNAVAILABLE'); }
      resolve();
    }
    child.stdout?.on('data', (chunk: Buffer) => {
      output += chunk.toString();
      const match = output.match(/\[backend\] http:\/\/127\.0\.0\.1:(\d+)/);
      if (match) { backendUrl = `http://127.0.0.1:${match[1]}/api`; desktopLog.write('info', 'backend is listening'); finish(); }
    });
    child.stderr?.on('data', (chunk: Buffer) => { errors += chunk.toString(); });
    child.on('error', (error) => finish(say('backendCannotStart', { reason: error.message })));
    child.on('exit', (code) => {
      backendUrl = null;
      finish(say('backendExited', { code: String(code) }));
      if (finished && !backendError) backendError = `${say('backendExited', { code: String(code) })}${errors ? ` ${errors.slice(-500)}` : ''}`;
    });
  });
}

/**
 * Is the backend working on files right now (a generation, an Apply, a Restore, ...)? Asked before the application restarts to install an update. No running backend
 * means nothing is running; a running backend that cannot answer counts as busy, because "cannot confirm it is idle" must never restart over a half-written game folder.
 */
async function backendHasActivity(): Promise<boolean> {
  const child = backend;
  if (!app.isPackaged || !child || child.exitCode !== null || !backendUrl) return false;
  try {
    const response = await fetch(`${backendUrl}/activity`, { headers: { 'X-DS1TR-Token': sessionToken }, signal: AbortSignal.timeout(2500) });
    if (!response.ok) return true;
    return ((await response.json()) as { busy?: unknown }).busy !== false;
  } catch { return true; }
}

function createUpdates(): UpdateController {
  const controller = new UpdateController({
    createUpdater: () => electronUpdater.autoUpdater as unknown as UpdaterLike,
    currentVersion: app.getVersion(),
    mode: installModeOf(app.isPackaged, process.env),
    buildChannel,
    source: UPDATE_SOURCE,
    prefs: {
      get: () => ({ autoCheck: settings.updates?.autoCheck !== false, lastCheckAt: settings.updates?.lastCheckAt ?? null, lastCheckOk: settings.updates?.lastCheckOk === true }),
      set: async (patch) => {
        const next = { ...settings.updates };
        if ('autoCheck' in patch) { if (patch.autoCheck === false) next.autoCheck = false; else delete next.autoCheck; }
        if (patch.lastCheckAt !== undefined) next.lastCheckAt = patch.lastCheckAt ?? undefined;
        if (patch.lastCheckOk !== undefined) next.lastCheckOk = patch.lastCheckOk;
        const { updates: _previous, ...rest } = settings;
        settings = Object.keys(next).length > 0 ? { ...rest, updates: next } : rest;
        await saveSettings();
      },
    },
    isOperationActive: backendHasActivity,
    openExternal: (url) => shell.openExternal(url),
    log: (level, message, code) => desktopLog.write(level, message, code),
  });
  controller.subscribe((state) => { mainWindow?.webContents.send(UPDATE_STATE_EVENT, state); });
  return controller;
}

/** The first automatic check waits until the window has been up for a while; a long-running session checks again every few hours (the 24-hour throttle decides whether it really runs). */
function scheduleUpdateChecks(): void {
  setTimeout(() => { void updates?.maybeAutoCheck(); }, STARTUP_CHECK_DELAY_MS).unref();
  setInterval(() => { void updates?.maybeAutoCheck(); }, PERIODIC_CHECK_MS).unref();
}

/** The DS1TR icon: shipped next to the runtime when packaged, in build/ during development. */
const iconPath = () => (app.isPackaged ? path.join(process.resourcesPath, 'icon.ico') : path.join(currentDirectory, '..', 'build', 'icon.ico'));

async function createWindow(): Promise<void> {
  // The packaged app has no File / Edit / View / Window / Help menu bar; development builds keep Electron's default menu.
  if (app.isPackaged) Menu.setApplicationMenu(null);
  const win = new BrowserWindow({
    icon: iconPath(),
    autoHideMenuBar: true,
    width: 1280,
    height: 820,
    minWidth: 980,
    minHeight: 680,
    backgroundColor: '#0d1016',
    title: 'DS1 Multi Randomizer',
    webPreferences: {
      preload: path.join(currentDirectory, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });
  mainWindow = win;
  win.on('closed', () => { if (mainWindow === win) mainWindow = null; });
  win.webContents.on('before-input-event', (event, input) => {
    if (!isDevToolsShortcut(input)) return;
    event.preventDefault();
    devToolsShown = !devToolsShown;
    win.webContents.send('dev-tools:changed', devToolsShown);
  });
  if (!app.isPackaged) await win.loadURL(process.env.VITE_DEV_SERVER_URL ?? 'http://127.0.0.1:5173');
  else await win.loadFile(path.join(currentDirectory, '..', 'dist', 'index.html'));
}

ipcMain.handle('dialog:select-game-directory', async () => {
  const result = await dialog.showOpenDialog({ title: say('selectGameFolder'), properties: ['openDirectory'] });
  if (result.canceled) return null;
  // Only the choice is returned: the folder is remembered once the installation check accepted it (game:remember-path).
  return result.filePaths[0];
});
ipcMain.handle('language:get', () => ({ setting: languageSetting(), resolved: currentLanguage() }));
ipcMain.handle('language:set', async (_event, value: unknown) => {
  const next = normalizeLanguageSetting(value);
  if (!next) return { setting: languageSetting(), resolved: currentLanguage() };
  const { language: _previous, ...rest } = settings;
  settings = next === 'auto' ? rest : { ...rest, language: next };
  await saveSettings();
  desktopLog.write('info', `language set to ${next} (resolved ${currentLanguage()})`);
  return { setting: languageSetting(), resolved: currentLanguage() };
});
// Export a document the renderer made (hint sheet, support bundle): the person chooses where; the renderer never supplies a path.
ipcMain.handle('file:save', async (event, value: unknown) => {
  const request = parseSaveRequest(value);
  if (!request) return { ok: false, canceled: false, code: 'INVALID_REQUEST' };
  const bytes = saveBytes(request);
  if (bytes.length > MAX_SAVE_BYTES) return { ok: false, canceled: false, code: 'TOO_LARGE' };
  const parent = BrowserWindow.fromWebContents(event.sender) ?? undefined;
  const options = { title: say('saveFileTitle'), defaultPath: path.join(app.getPath('documents'), request.defaultName), filters: [{ name: SAVE_KINDS[request.kind].filter, extensions: [SAVE_KINDS[request.kind].extension] }] };
  const chosen = parent ? await dialog.showSaveDialog(parent, options) : await dialog.showSaveDialog(options);
  if (chosen.canceled || !chosen.filePath) return { ok: false, canceled: true };
  try {
    await writeFile(chosen.filePath, bytes);
    desktopLog.write('info', `exported ${request.kind} (${bytes.length} bytes)`);
    return { ok: true, name: path.basename(chosen.filePath) };
  } catch (error) {
    desktopLog.write('error', `export failed: ${error instanceof Error ? error.message : String(error)}`, 'WRITE_FAILED');
    return { ok: false, canceled: false, code: 'WRITE_FAILED' };
  }
});
ipcMain.handle('game:remember-path', async (_event, gamePath: unknown) => {
  const normalized = normalizeGamePath(gamePath);
  if (!normalized) return false;
  settings = { ...settings, gamePath: normalized };
  await saveSettings();
  return true;
});
ipcMain.handle('app:dev-tools', () => devToolsShown);
ipcMain.handle('runtime:connection', () => ({
  apiBase: app.isPackaged ? backendUrl : 'http://127.0.0.1:43177/api',
  token: app.isPackaged ? sessionToken : '',
  error: backendError,
  gamePath: settings.gamePath ?? '',
}));
// Updates: a narrow surface. Only the main window may call it, nothing takes a path or an address, and the renderer never touches the updater object.
const fromMainWindow = (event: IpcMainInvokeEvent): boolean => mainWindow !== null && event.sender === mainWindow.webContents;
ipcMain.handle('updates:get', (event) => (fromMainWindow(event) ? updates?.getState() ?? null : null));
ipcMain.handle('updates:check', (event) => (fromMainWindow(event) ? updates?.check({ manual: true }) ?? null : null));
ipcMain.handle('updates:download', (event) => (fromMainWindow(event) ? updates?.download() ?? null : null));
ipcMain.handle('updates:install', async (event) => (fromMainWindow(event) && updates ? updates.install() : { ok: false, code: 'NOT_SUPPORTED' }));
ipcMain.handle('updates:open-release', async (event) => (fromMainWindow(event) && updates ? updates.openReleasePage() : false));
ipcMain.handle('updates:set-auto-check', async (event, enabled: unknown) => (fromMainWindow(event) && updates && typeof enabled === 'boolean' ? updates.setAutoCheck(enabled) : null));
ipcMain.handle('game:launch', (_event, gamePath: string) => launchGame(typeof gamePath === 'string' ? gamePath : '', {
  openExternal: (url) => shell.openExternal(url),
  startDetached: async (executable, directory) => {
    const child = spawn(executable, [], { cwd: directory, detached: true, stdio: 'ignore' });
    child.unref();
  },
  exists: (target) => access(target).then(() => true, () => false),
}));

app.whenReady().then(async () => {
  await loadBuildChannel();
  await loadSettings();
  desktopLog.write('info', `DS1 Multi Randomizer ${app.getVersion()} started (${installModeOf(app.isPackaged, process.env)}, ${buildChannel} channel), language ${currentLanguage()}`);
  await startBackend();
  updates = createUpdates();
  await createWindow();
  scheduleUpdateChecks();
}).catch((error) => { dialog.showErrorBox(say('startupFailedTitle'), String(error)); app.quit(); });
app.on('before-quit', (event) => {
  if (quitting) return;
  event.preventDefault();
  quitting = true;
  void stopBackend().finally(() => app.quit());
});
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
