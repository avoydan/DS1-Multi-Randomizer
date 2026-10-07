import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('desktop', {
  selectGameDirectory: (): Promise<string | null> => ipcRenderer.invoke('dialog:select-game-directory'),
  rememberGamePath: (gamePath: string): Promise<boolean> => ipcRenderer.invoke('game:remember-path', gamePath),
  launchGame: (gamePath: string): Promise<{ ok: boolean; method: 'steam' | 'executable' | 'none'; message: string; technical?: string }> => ipcRenderer.invoke('game:launch', gamePath),
  language: (): Promise<{ setting: 'auto' | 'en' | 'pl'; resolved: 'en' | 'pl' }> => ipcRenderer.invoke('language:get'),
  setLanguage: (setting: 'auto' | 'en' | 'pl'): Promise<{ setting: 'auto' | 'en' | 'pl'; resolved: 'en' | 'pl' }> => ipcRenderer.invoke('language:set', setting),
  connection: (): Promise<{ apiBase: string | null; token: string; error: string | null; gamePath: string }> => ipcRenderer.invoke('runtime:connection'),
});
