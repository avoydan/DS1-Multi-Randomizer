import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('desktop', {
  selectGameDirectory: (): Promise<string | null> => ipcRenderer.invoke('dialog:select-game-directory'),
  connection: (): Promise<{ apiBase: string | null; token: string; error: string | null; gamePath: string }> => ipcRenderer.invoke('runtime:connection'),
});
