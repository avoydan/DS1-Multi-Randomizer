import { app, BrowserWindow, dialog, ipcMain } from 'electron';
import { spawn, execFile, type ChildProcess } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { migrateSettings, type StoredSettings } from './settings.js';

const currentDirectory = path.dirname(fileURLToPath(import.meta.url));
const runtimeRoot = path.join(process.resourcesPath, 'runtime');
const settingsPath = () => path.join(app.getPath('userData'), 'settings.json');
let backend: ChildProcess | null = null;
let backendUrl: string | null = null;
let backendError: string | null = null;
let sessionToken = '';
let settings: StoredSettings = {};
let quitting = false;

async function saveSettings(): Promise<void> {
  try {
    await mkdir(app.getPath('userData'), { recursive: true });
    await writeFile(settingsPath(), JSON.stringify(settings, null, 2));
  } catch { /* settings are a convenience; the app works without them */ }
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
    },
    stdio: ['ignore', 'pipe', 'pipe', 'ipc'],
    windowsHide: true,
  });
  backend = child;
  let output = '';
  let errors = '';
  await new Promise<void>((resolve) => {
    const timeout = setTimeout(() => finish('Backend startup timed out. Check the packaged runtime files.'), 15000);
    let finished = false;
    function finish(error?: string): void {
      if (finished) return;
      finished = true;
      clearTimeout(timeout);
      if (error) backendError = `${error}${errors ? ` ${errors.slice(-500)}` : ''}`;
      resolve();
    }
    child.stdout?.on('data', (chunk: Buffer) => {
      output += chunk.toString();
      const match = output.match(/\[backend\] http:\/\/127\.0\.0\.1:(\d+)/);
      if (match) { backendUrl = `http://127.0.0.1:${match[1]}/api`; finish(); }
    });
    child.stderr?.on('data', (chunk: Buffer) => { errors += chunk.toString(); });
    child.on('error', (error) => finish(`Cannot start backend: ${error.message}`));
    child.on('exit', (code) => {
      backendUrl = null;
      finish(`Backend exited with code ${code}.`);
      if (finished && !backendError) backendError = `Backend exited with code ${code}.${errors ? ` ${errors.slice(-500)}` : ''}`;
    });
  });
}

async function createWindow(): Promise<void> {
  const win = new BrowserWindow({
    width: 1280,
    height: 820,
    minWidth: 980,
    minHeight: 680,
    backgroundColor: '#0d1016',
    title: 'DS1 Triple Randomizer',
    webPreferences: {
      preload: path.join(currentDirectory, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });
  if (!app.isPackaged) await win.loadURL(process.env.VITE_DEV_SERVER_URL ?? 'http://127.0.0.1:5173');
  else await win.loadFile(path.join(currentDirectory, '..', 'dist', 'index.html'));
}

ipcMain.handle('dialog:select-game-directory', async () => {
  const result = await dialog.showOpenDialog({ title: 'Select the Dark Souls Remastered folder', properties: ['openDirectory'] });
  if (result.canceled) return null;
  settings = { ...settings, gamePath: result.filePaths[0] };
  await saveSettings();
  return result.filePaths[0];
});
ipcMain.handle('runtime:connection', () => ({
  apiBase: app.isPackaged ? backendUrl : 'http://127.0.0.1:43177/api',
  token: app.isPackaged ? sessionToken : '',
  error: backendError,
  gamePath: settings.gamePath ?? '',
}));
app.whenReady().then(async () => {
  await loadSettings();
  await startBackend();
  await createWindow();
}).catch((error) => { dialog.showErrorBox('Desktop startup failed', String(error)); app.quit(); });
app.on('before-quit', (event) => {
  if (quitting) return;
  event.preventDefault();
  quitting = true;
  void stopBackend().finally(() => app.quit());
});
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
