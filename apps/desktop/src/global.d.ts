export {};

declare global {
  interface Window {
    desktop?: {
      selectGameDirectory(): Promise<string | null>;
      selectFogDataRoot(): Promise<{ path: string; error: string | null } | null>;
      connection(): Promise<{ apiBase: string | null; token: string; error: string | null; fogDataRoot: string }>;
    };
  }
}
