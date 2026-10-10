import { contextBridge, ipcRenderer } from 'electron';
import type { UpdateErrorCode, UpdateState } from './updater.js';

contextBridge.exposeInMainWorld('desktop', {
  selectGameDirectory: (): Promise<string | null> => ipcRenderer.invoke('dialog:select-game-directory'),
  rememberGamePath: (gamePath: string): Promise<boolean> => ipcRenderer.invoke('game:remember-path', gamePath),
  launchGame: (gamePath: string): Promise<{ ok: boolean; method: 'steam' | 'executable' | 'none'; message: string; technical?: string }> => ipcRenderer.invoke('game:launch', gamePath),
  language: (): Promise<{ setting: 'auto' | 'en' | 'pl'; resolved: 'en' | 'pl' }> => ipcRenderer.invoke('language:get'),
  setLanguage: (setting: 'auto' | 'en' | 'pl'): Promise<{ setting: 'auto' | 'en' | 'pl'; resolved: 'en' | 'pl' }> => ipcRenderer.invoke('language:set', setting),
  saveFile: (request: { kind: 'html' | 'txt' | 'json' | 'zip'; defaultName: string; content: string }): Promise<{ ok: boolean; canceled: boolean; name?: string; code?: string }> => ipcRenderer.invoke('file:save', request),
  devTools: (): Promise<boolean> => ipcRenderer.invoke('app:dev-tools'),
  onDevTools: (listener: (shown: boolean) => void): (() => void) => {
    const handler = (_event: unknown, shown: boolean) => listener(shown === true);
    ipcRenderer.on('dev-tools:changed', handler);
    return () => { ipcRenderer.removeListener('dev-tools:changed', handler); };
  },
  /** Application updates (docs/AUTO_UPDATE.md): narrow calls and one event. The updater itself, GitHub and the installer stay in the main process. */
  updates: {
    state: (): Promise<UpdateState | null> => ipcRenderer.invoke('updates:get'),
    check: (): Promise<UpdateState | null> => ipcRenderer.invoke('updates:check'),
    download: (): Promise<UpdateState | null> => ipcRenderer.invoke('updates:download'),
    install: (): Promise<{ ok: boolean; code?: UpdateErrorCode; state?: UpdateState }> => ipcRenderer.invoke('updates:install'),
    openReleasePage: (): Promise<boolean> => ipcRenderer.invoke('updates:open-release'),
    openNexusPage: (): Promise<boolean> => ipcRenderer.invoke('updates:open-nexus-page'),
    setAutoCheck: (enabled: boolean): Promise<UpdateState | null> => ipcRenderer.invoke('updates:set-auto-check', enabled === true),
    onState: (listener: (state: UpdateState) => void): (() => void) => {
      const handler = (_event: unknown, state: UpdateState) => listener(state);
      ipcRenderer.on('updates:state', handler);
      return () => { ipcRenderer.removeListener('updates:state', handler); };
    },
  },
  connection: (): Promise<{ apiBase: string | null; token: string; error: string | null; gamePath: string }> => ipcRenderer.invoke('runtime:connection'),
});
