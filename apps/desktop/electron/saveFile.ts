import path from 'node:path';

/** What the renderer may ask the main process to save: a generated text document or a support bundle, never an arbitrary path or content type. */
export const SAVE_KINDS = {
  html: { extension: 'html', filter: 'HTML' },
  txt: { extension: 'txt', filter: 'Text' },
  json: { extension: 'json', filter: 'JSON' },
  zip: { extension: 'zip', filter: 'ZIP' },
} as const;
export type SaveKind = keyof typeof SAVE_KINDS;

export const MAX_SAVE_BYTES = 32 * 1024 * 1024;

export interface SaveRequest {
  kind: SaveKind;
  defaultName: string;
  /** Text for html / txt / json; base64 for zip. */
  content: string;
}

/** A file name with no folder, no reserved characters and the extension of its kind. */
export function safeFileName(name: unknown, kind: SaveKind): string {
  const extension = SAVE_KINDS[kind].extension;
  const base = path.win32.basename(typeof name === 'string' ? name : '').replace(/[<>:"/\\|?*\u0000-\u001f]/gu, '_').replace(/[. ]+$/u, '');
  const stem = base.replace(/\.[A-Za-z0-9]{1,5}$/u, '').trim() || 'DS1TR-export';
  return `${stem.slice(0, 120)}.${extension}`;
}

export function parseSaveRequest(value: unknown): SaveRequest | null {
  if (typeof value !== 'object' || value === null) return null;
  const request = value as Record<string, unknown>;
  const kind = request.kind;
  if (typeof kind !== 'string' || !(kind in SAVE_KINDS) || typeof request.content !== 'string') return null;
  if (request.content.length > MAX_SAVE_BYTES * 1.4) return null;
  return { kind: kind as SaveKind, defaultName: safeFileName(request.defaultName, kind as SaveKind), content: request.content };
}

export function saveBytes(request: SaveRequest): Buffer {
  return request.kind === 'zip' ? Buffer.from(request.content, 'base64') : Buffer.from(request.content, 'utf8');
}
