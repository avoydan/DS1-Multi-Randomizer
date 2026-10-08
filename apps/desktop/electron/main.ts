import { app, BrowserWindow, dialog, ipcMain, Menu, shell } from 'electron';
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
ipcMain.handle('game:launch', (_event, gamePath: string) => launchGame(typeof gamePath === 'string' ? gamePath : '', {
  openExternal: (url) => shell.openExternal(url),
  startDetached: async (executable, directory) => {
    const child = spawn(executable, [], { cwd: directory, detached: true, stdio: 'ignore' });
    child.unref();
  },
  exists: (target) => access(target).then(() => true, () => false),
}));

app.whenReady().then(async () => {
  await loadSettings();
  desktopLog.write('info', `DS1 Multi Randomizer ${app.getVersion()} started (${installModeOf(app.isPackaged, process.env)}, ${buildChannel} channel), language ${currentLanguage()}`);
  await startBackend();
  await createWindow();
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
