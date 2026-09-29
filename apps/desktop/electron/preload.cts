import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('desktop', {
  selectGameDirectory: (): Promise<string | null> => ipcRenderer.invoke('dialog:select-game-directory'),
  selectFogDataRoot: (): Promise<{ path: string; error: string | null } | null> => ipcRenderer.invoke('dialog:select-fog-data-root'),
  connection: (): Promise<{ apiBase: string | null; token: string; error: string | null; fogDataRoot: string }> => ipcRenderer.invoke('runtime:connection'),
});
