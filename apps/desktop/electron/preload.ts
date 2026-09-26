import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('desktop', {
  selectGameDirectory: (): Promise<string | null> => ipcRenderer.invoke('dialog:select-game-directory'),
});
