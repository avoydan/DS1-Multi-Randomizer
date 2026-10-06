import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('desktop', {
  selectGameDirectory: (): Promise<string | null> => ipcRenderer.invoke('dialog:select-game-directory'),
  launchGame: (gamePath: string): Promise<{ ok: boolean; method: 'steam' | 'executable' | 'none'; message: string; technical?: string }> => ipcRenderer.invoke('game:launch', gamePath),
  connection: (): Promise<{ apiBase: string | null; token: string; error: string | null; gamePath: string }> => ipcRenderer.invoke('runtime:connection'),
});
