import path from 'node:path';

type DirectoryDialogResult = { canceled: boolean; filePaths: string[] };

/** Keep a predictable starting location now that Electron explicitly defaults native dialogs to Downloads. */
export function createGameDirectoryPicker(
  showOpenDialog: (defaultPath: string) => Promise<DirectoryDialogResult>,
  initialDirectory: () => string,
): () => Promise<string | null> {
  let lastVisitedDirectory: string | null = null;

  return async () => {
    const result = await showOpenDialog(lastVisitedDirectory ?? initialDirectory());
    const selectedDirectory = result.filePaths[0];
    if (result.canceled || !selectedDirectory) return null;

    lastVisitedDirectory = path.dirname(selectedDirectory);
    return selectedDirectory;
  };
}
