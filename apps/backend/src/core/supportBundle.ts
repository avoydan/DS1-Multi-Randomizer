import { readFileSync, statSync } from 'node:fs';
import { type RedactionContext, createRedactor, findLeaks, redactJson } from './redaction.js';
import { createZip, readZip } from './zipWriter.js';

/**
 * The support bundle (docs/SUPPORT_BUNDLE.md): one ZIP a person can hand to whoever helps them. It is assembled from an explicit ALLOWLIST of named
 * entries, never by walking a folder: a file that is not on the list cannot get in. Every text passes through the redactor, a finished bundle is
 * scanned once more for the person's known identifiers and secrets (and refused if any is left), and the archive is read back before it is returned.
 * It never contains game files, generated game data, executables, certificates or keys, the environment, or the whole run folder.
 */
export const BUNDLE_ALLOWLIST = [
  'README.txt',
  'summary.txt',
  'diagnostics.json',
  'generation/run-manifest.json',
  'generation/generation-summary.json',
  'generation/validation-summary.json',
  'logs/app.log',
  'logs/backend.log',
  'logs/relevant-generation.log',
  'release/release-manifest.json',
] as const;
export type BundleEntryName = (typeof BUNDLE_ALLOWLIST)[number];

/** Per-entry and total limits (uncompressed bytes). A log longer than its limit is cut to its END (the newest lines) and says so. */
export const BUNDLE_LIMITS: Record<BundleEntryName, number> = {
  'README.txt': 32 * 1024, 'summary.txt': 64 * 1024, 'diagnostics.json': 256 * 1024,
  'generation/run-manifest.json': 256 * 1024, 'generation/generation-summary.json': 128 * 1024, 'generation/validation-summary.json': 128 * 1024,
  'logs/app.log': 256 * 1024, 'logs/backend.log': 512 * 1024, 'logs/relevant-generation.log': 256 * 1024, 'release/release-manifest.json': 64 * 1024,
};
export const BUNDLE_TOTAL_LIMIT = 2 * 1024 * 1024;

export interface BundleSource {
  name: string;
  /** Text content, or an object serialized as JSON. A missing source (null) leaves the entry out. */
  content: string | object | null;
}

export type BundleResult =
  | { ok: true; archive: Buffer; fileName: string; report: BundleReport }
  | { ok: false; code: 'BUNDLE_SANITIZATION_FAILED' | 'BUNDLE_TOO_LARGE' | 'BUNDLE_INVALID_ENTRY' | 'BUNDLE_EMPTY'; detail: string };

export interface BundleReport {
  entries: Array<{ name: string; bytes: number; truncated: boolean }>;
  totalBytes: number;
  redactions: number;
}

const isAllowed = (name: string): name is BundleEntryName => (BUNDLE_ALLOWLIST as readonly string[]).includes(name);

/** The end of a text within a byte limit, cut at a line start, with a note on top. */
function limitText(text: string, limit: number): { text: string; truncated: boolean } {
  if (Buffer.byteLength(text, 'utf8') <= limit) return { text, truncated: false };
  const tail = Buffer.from(text, 'utf8').subarray(Buffer.byteLength(text, 'utf8') - (limit - 200)).toString('utf8');
  return { text: `[truncated: only the newest part is included]\n${tail.slice(tail.indexOf('\n') + 1)}`, truncated: true };
}

export function bundleFileName(seedCode: string | null, now: Date): string {
  const stamp = now.toISOString().replace(/[-:]/gu, '').replace(/\.\d+Z$/u, 'Z').replace('T', '-');
  const short = seedCode ? (seedCode.split('-')[1] ?? '').replace(/[^A-Za-z0-9]/gu, '').toUpperCase().slice(0, 12) : '';
  return `DS1TR-support-${short || 'noworld'}-${stamp}.zip`;
}

/** A line longer than this is a blob, not a message: it is cut before redaction so no pattern has to chew through it. */
const MAX_LINE = 8000;
const capLines = (text: string): string => (/[^\n]{8001}/u.test(text) ? text.split('\n').map((line) => (line.length > MAX_LINE ? `${line.slice(0, MAX_LINE)} [line cut]` : line)).join('\n') : text);

export function buildSupportBundle(sources: readonly BundleSource[], context: RedactionContext, options: { seedCode: string | null; now?: Date }): BundleResult {
  const now = options.now ?? new Date();
  const redact = createRedactor(context);
  const entries: Array<{ name: BundleEntryName; data: Buffer; truncated: boolean }> = [];
  for (const source of sources) {
    if (source.content === null || source.content === undefined) continue;
    if (!isAllowed(source.name)) return { ok: false, code: 'BUNDLE_INVALID_ENTRY', detail: source.name };
    let text: string;
    if (typeof source.content === 'object') text = `${JSON.stringify(redactJson(source.content, redact), null, 2)}\n`;
    else if (source.name.endsWith('.json')) {
      try { text = `${JSON.stringify(redactJson(JSON.parse(source.content), redact), null, 2)}\n`; } catch { text = redact(source.content); }
    } else text = redact(capLines(source.content));
    const limited = limitText(text, BUNDLE_LIMITS[source.name]);
    entries.push({ name: source.name, data: Buffer.from(limited.text, 'utf8'), truncated: limited.truncated });
  }
  if (entries.length === 0) return { ok: false, code: 'BUNDLE_EMPTY', detail: 'nothing to include' };
  const totalBytes = entries.reduce((sum, entry) => sum + entry.data.length, 0);
  if (totalBytes > BUNDLE_TOTAL_LIMIT) return { ok: false, code: 'BUNDLE_TOO_LARGE', detail: `${totalBytes} bytes` };

  // Independent check of the finished content: any identifier or secret we know about that is still there stops the bundle.
  const leaks = entries.flatMap((entry) => findLeaks(entry.data.toString('utf8'), context).map((leak) => `${entry.name}: ${leak}`));
  if (leaks.length > 0) return { ok: false, code: 'BUNDLE_SANITIZATION_FAILED', detail: leaks.join('; ') };

  const archive = createZip(entries.map((entry) => ({ name: entry.name, data: entry.data, modified: now })), now);
  // Read it back: the names are exactly the allowlisted ones and every checksum holds.
  const readBack = readZip(archive);
  if (readBack.length !== entries.length || readBack.some((entry) => !isAllowed(entry.name))) return { ok: false, code: 'BUNDLE_INVALID_ENTRY', detail: 'the archive does not match its contents' };
  return {
    ok: true, archive, fileName: bundleFileName(options.seedCode, now),
    report: { entries: entries.map((entry) => ({ name: entry.name, bytes: entry.data.length, truncated: entry.truncated })), totalBytes, redactions: redact.count() },
  };
}

/** Reads a file for the bundle: its text, or null when it does not exist or is not a regular file. Never follows the size past the cap. */
export function readBundleFile(file: string | null | undefined, capBytes: number): string | null {
  if (!file) return null;
  try {
    const stats = statSync(file);
    if (!stats.isFile()) return null;
    const buffer = readFileSync(file);
    return (stats.size > capBytes * 2 ? buffer.subarray(buffer.length - capBytes * 2) : buffer).toString('utf8');
  } catch { return null; }
}
