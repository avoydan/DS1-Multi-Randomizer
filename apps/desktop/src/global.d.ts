export {};

declare global {
  interface Window {
    desktop?: {
      selectGameDirectory(): Promise<string | null>;
      rememberGamePath(gamePath: string): Promise<boolean>;
      launchGame(gamePath: string): Promise<{ ok: boolean; method: 'steam' | 'executable' | 'none'; message: string; technical?: string }>;
      language(): Promise<{ setting: 'auto' | 'en' | 'pl'; resolved: 'en' | 'pl' }>;
      setLanguage(setting: 'auto' | 'en' | 'pl'): Promise<{ setting: 'auto' | 'en' | 'pl'; resolved: 'en' | 'pl' }>;
      connection(): Promise<{ apiBase: string | null; token: string; error: string | null; gamePath: string }>;
    };
  }
}
