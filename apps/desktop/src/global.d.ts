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
      saveFile(request: { kind: 'html' | 'txt' | 'json' | 'zip'; defaultName: string; content: string }): Promise<{ ok: boolean; canceled: boolean; name?: string; code?: string }>;
      devTools(): Promise<boolean>;
      onDevTools(listener: (shown: boolean) => void): () => void;
      connection(): Promise<{ apiBase: string | null; token: string; error: string | null; gamePath: string }>;
    };
  }
}
