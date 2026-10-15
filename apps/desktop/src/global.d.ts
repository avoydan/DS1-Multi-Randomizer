import type { UpdateErrorCode, UpdateState } from '../electron/updater';

export {};

declare global {
  /** The version of the desktop app (package.json), put in by Vite at build time. */
  const __APP_VERSION__: string;

  interface Window {
    desktop?: {
      selectGameDirectory(): Promise<string | null>;
      rememberGamePath(gamePath: string): Promise<boolean>;
      launchGame(gamePath: string): Promise<{ ok: boolean; method: 'steam' | 'executable' | 'none'; message: string; technical?: string }>;
      language(): Promise<{ setting: 'auto' | 'en' | 'pl'; resolved: 'en' | 'pl' }>;
      setLanguage(setting: 'auto' | 'en' | 'pl'): Promise<{ setting: 'auto' | 'en' | 'pl'; resolved: 'en' | 'pl' }>;
      gameConveniences: {
        freeCursorInMenus(): Promise<boolean>;
        setFreeCursorInMenus(enabled: boolean): Promise<boolean>;
        freeCursorShortcut(): Promise<string>;
        setFreeCursorShortcut(shortcut: string): Promise<boolean>;
      };
      saveFile(request: { kind: 'html' | 'txt' | 'json' | 'zip'; defaultName: string; content: string }): Promise<{ ok: boolean; canceled: boolean; name?: string; code?: string }>;
      devTools(): Promise<boolean>;
      onDevTools(listener: (shown: boolean) => void): () => void;
      /** Application updates (docs/AUTO_UPDATE.md). A null state means no desktop shell answered. */
      updates: {
        state(): Promise<UpdateState | null>;
        check(): Promise<UpdateState | null>;
        download(): Promise<UpdateState | null>;
        install(): Promise<{ ok: boolean; code?: UpdateErrorCode; state?: UpdateState }>;
        openReleasePage(): Promise<boolean>;
        openNexusPage(): Promise<boolean>;
        setAutoCheck(enabled: boolean): Promise<UpdateState | null>;
        onState(listener: (state: UpdateState) => void): () => void;
      };
      connection(): Promise<{ apiBase: string | null; token: string; error: string | null; gamePath: string }>;
    };
  }
}
