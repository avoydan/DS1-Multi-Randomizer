export {};

declare global {
  interface Window {
    desktop?: {
      selectGameDirectory(): Promise<string | null>;
      launchGame(gamePath: string): Promise<{ ok: boolean; method: 'steam' | 'executable' | 'none'; message: string; technical?: string }>;
      connection(): Promise<{ apiBase: string | null; token: string; error: string | null; gamePath: string }>;
    };
  }
}
