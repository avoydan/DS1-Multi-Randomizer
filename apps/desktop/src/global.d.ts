export {};

declare global {
  interface Window {
    desktop?: {
      selectGameDirectory(): Promise<string | null>;
    };
  }
}
