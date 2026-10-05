export {};

declare global {
  interface Window {
    desktop?: {
      selectGameDirectory(): Promise<string | null>;
      connection(): Promise<{ apiBase: string | null; token: string; error: string | null; gamePath: string }>;
    };
  }
}
