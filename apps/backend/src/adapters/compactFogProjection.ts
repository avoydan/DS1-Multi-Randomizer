import { execFile } from 'node:child_process';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { promisify } from 'node:util';

export const COMPACT_FOG_PROJECTION_FORMAT = 'ds1tr-compact-fog-projection-v1';
export const COMPACT_FOG_CATALOG_VERSION = 'ds1tr-compact-full-fog-v1';
export const COMPACT_FOG_CATALOG_FINGERPRINT = '8c03fd3de1cd7c772d5c933753ae54eab35f9e8b70a1aef2954322330d0002d4';

export interface CompactFogProjection {
  format: typeof COMPACT_FOG_PROJECTION_FORMAT;
  catalogVersion: typeof COMPACT_FOG_CATALOG_VERSION;
  catalogFingerprint: string;
  items: Array<{ equipType: number; itemId: number; required: boolean }>;
  lotStarts: number[];
}

export class CompactFogProjectionError extends Error {
  readonly code = 'FOG_COMPACT_PROJECTION_UNAVAILABLE';
}

const sha256Pattern = /^[a-f0-9]{64}$/u;
const execFileAsync = promisify(execFile);

function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function parseCompactFogProjection(value: unknown): CompactFogProjection {
  if (!record(value) || value.format !== COMPACT_FOG_PROJECTION_FORMAT
    || value.catalogVersion !== COMPACT_FOG_CATALOG_VERSION
    || typeof value.catalogFingerprint !== 'string' || !sha256Pattern.test(value.catalogFingerprint)
    || !Array.isArray(value.items) || !Array.isArray(value.lotStarts)) {
    throw new CompactFogProjectionError('The Compact catalog projection has an unsupported format or identity.');
  }

  const items: CompactFogProjection['items'] = [];
  const itemIds = new Set<string>();
  for (const candidate of value.items) {
    if (!record(candidate) || typeof candidate.equipType !== 'number' || !Number.isInteger(candidate.equipType) || candidate.equipType < 0 || candidate.equipType > 3
      || typeof candidate.itemId !== 'number' || !Number.isInteger(candidate.itemId) || candidate.itemId <= 0 || typeof candidate.required !== 'boolean') {
      throw new CompactFogProjectionError('The Compact catalog projection contains an invalid tracked item.');
    }
    const identity = `${candidate.equipType}:${candidate.itemId}`;
    if (itemIds.has(identity)) throw new CompactFogProjectionError(`The Compact catalog projection repeats tracked item ${identity}.`);
    itemIds.add(identity);
    items.push({ equipType: candidate.equipType as number, itemId: candidate.itemId as number, required: candidate.required });
  }

  const lotStarts: number[] = [];
  const lotIds = new Set<number>();
  for (const candidate of value.lotStarts) {
    if (!Number.isInteger(candidate) || (candidate as number) <= 0)
      throw new CompactFogProjectionError('The Compact catalog projection contains an invalid lot start.');
    if (lotIds.has(candidate as number)) throw new CompactFogProjectionError(`The Compact catalog projection repeats lot start ${candidate}.`);
    lotIds.add(candidate as number);
    lotStarts.push(candidate as number);
  }
  if (!items.some((item) => item.required) || lotStarts.length === 0)
    throw new CompactFogProjectionError('The Compact catalog projection has no required items or no item lots.');

  return {
    format: COMPACT_FOG_PROJECTION_FORMAT,
    catalogVersion: COMPACT_FOG_CATALOG_VERSION,
    catalogFingerprint: value.catalogFingerprint,
    items,
    lotStarts,
  };
}

/** Loads the C# source-of-truth projection; it does not read or download fog.txt. */
export async function loadCompactFogProjection(
  executablePath: string,
  dataRoot: string,
  pristineGameDirectory: string,
): Promise<CompactFogProjection> {
  const scratch = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-compact-projection-'));
  const outputPath = path.join(scratch, 'projection.json');
  try {
    try {
      await execFileAsync(executablePath, [
        'compact-projection', '--data-root', dataRoot,
        '--pristine-root', pristineGameDirectory,
        '--out', outputPath,
      ], { windowsHide: true, timeout: 120_000, maxBuffer: 1024 * 1024 });
    } catch (error) {
      const detail = record(error) && typeof error.stderr === 'string' ? error.stderr.trim() : '';
      throw new CompactFogProjectionError(detail || (error instanceof Error ? error.message : String(error)));
    }
    return parseCompactFogProjection(JSON.parse(await readFile(outputPath, 'utf8')) as unknown);
  } catch (error) {
    if (error instanceof CompactFogProjectionError) throw error;
    throw new CompactFogProjectionError(error instanceof Error ? error.message : String(error));
  } finally {
    await rm(scratch, { recursive: true, force: true }).catch(() => undefined);
  }
}
