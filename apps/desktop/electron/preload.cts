import { contextBridge, ipcRenderer } from 'electron';

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
  connection: (): Promise<{ apiBase: string | null; token: string; error: string | null; gamePath: string }> => ipcRenderer.invoke('runtime:connection'),
});
