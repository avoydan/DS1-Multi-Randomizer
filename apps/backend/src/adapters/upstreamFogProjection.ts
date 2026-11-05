import { execFile } from 'node:child_process';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { promisify } from 'node:util';
import type { FogTrackedItems } from './fogItemRules.js';

/**
 * The UPSTREAM item model of generation profile ds1tr-gen-v7 (docs/V7_UPSTREAM_PARITY.md): the key items and item lots that the original fog.txt declares
 * (`KeyItems`, `LotLocations`), exported by `FogHost upstream-projection` from the verified data root. The Items randomizer places progression items by this list;
 * FogMod's own ItemReader then finds where each one ended up. It is not a catalog projection, not a frozen snapshot and not a reduced "required" set.
 */
export const UPSTREAM_FOG_PROJECTION_FORMAT = 'ds1tr-upstream-fog-projection-v1';

export interface UpstreamFogProjection {
  format: typeof UPSTREAM_FOG_PROJECTION_FORMAT;
  /** SHA-256 of the upstream fog.txt the projection was read from (LF-normalized, as FogHost pins it). */
  annotationSha256: string;
  items: Array<{ equipType: number; itemId: number; required: boolean; name: string }>;
  lotStarts: number[];
}

export class UpstreamFogProjectionError extends Error {
  readonly code = 'FOG_UPSTREAM_PROJECTION_UNAVAILABLE';
}

const sha256Pattern = /^[a-f0-9]{64}$/u;
const execFileAsync = promisify(execFile);

function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function parseUpstreamFogProjection(value: unknown, expectedAnnotationSha256: string): UpstreamFogProjection {
  if (!record(value) || value.format !== UPSTREAM_FOG_PROJECTION_FORMAT || value.annotationSha256 !== expectedAnnotationSha256
    || !sha256Pattern.test(expectedAnnotationSha256) || !Array.isArray(value.items) || !Array.isArray(value.lotStarts)) {
    throw new UpstreamFogProjectionError('The upstream fog projection has an unsupported format or does not match the pinned fog.txt.');
  }
  const items: UpstreamFogProjection['items'] = [];
  const identities = new Set<string>();
  for (const candidate of value.items) {
    if (!record(candidate) || typeof candidate.equipType !== 'number' || !Number.isInteger(candidate.equipType) || candidate.equipType < 0 || candidate.equipType > 3
      || typeof candidate.itemId !== 'number' || !Number.isInteger(candidate.itemId) || candidate.itemId <= 0
      || typeof candidate.required !== 'boolean' || typeof candidate.name !== 'string') {
      throw new UpstreamFogProjectionError('The upstream fog projection contains an invalid key item.');
    }
    const identity = `${candidate.equipType}:${candidate.itemId}`;
    if (identities.has(identity)) throw new UpstreamFogProjectionError(`The upstream fog projection repeats key item ${identity}.`);
    identities.add(identity);
    items.push({ equipType: candidate.equipType, itemId: candidate.itemId, required: candidate.required, name: candidate.name });
  }
  const lotStarts: number[] = [];
  const lots = new Set<number>();
  for (const candidate of value.lotStarts) {
    if (!Number.isInteger(candidate) || (candidate as number) <= 0) throw new UpstreamFogProjectionError('The upstream fog projection contains an invalid lot.');
    if (lots.has(candidate as number)) throw new UpstreamFogProjectionError(`The upstream fog projection repeats lot ${candidate}.`);
    lots.add(candidate as number);
    lotStarts.push(candidate as number);
  }
  if (!items.some((item) => item.required) || lotStarts.length === 0) throw new UpstreamFogProjectionError('The upstream fog projection has no required key items or no item lots.');
  return { format: UPSTREAM_FOG_PROJECTION_FORMAT, annotationSha256: value.annotationSha256, items, lotStarts };
}

/** Runs the host command on a verified V7 data root. */
export async function loadUpstreamFogProjection(executablePath: string, dataRoot: string, expectedAnnotationSha256: string): Promise<UpstreamFogProjection> {
  const scratch = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-upstream-projection-'));
  const outputPath = path.join(scratch, 'projection.json');
  try {
    try {
      await execFileAsync(executablePath, ['upstream-projection', '--data-root', dataRoot, '--out', outputPath], { windowsHide: true, timeout: 120_000, maxBuffer: 1024 * 1024 });
    } catch (error) {
      const detail = record(error) && typeof error.stderr === 'string' ? error.stderr.trim() : '';
      throw new UpstreamFogProjectionError(detail || (error instanceof Error ? error.message : String(error)));
    }
    return parseUpstreamFogProjection(JSON.parse(await readFile(outputPath, 'utf8')) as unknown, expectedAnnotationSha256);
  } catch (error) {
    if (error instanceof UpstreamFogProjectionError) throw error;
    throw new UpstreamFogProjectionError(error instanceof Error ? error.message : String(error));
  } finally {
    await rm(scratch, { recursive: true, force: true }).catch(() => undefined);
  }
}

/** The tracking the combined engine consumes: every upstream key item (required or not) and every upstream lot, in the order the host wrote them. */
export function trackedItemsFromUpstreamProjection(projection: UpstreamFogProjection): FogTrackedItems {
  return {
    items: projection.items.map(({ equipType, itemId, required }) => ({ equipType, itemId, required })),
    lotStarts: [...projection.lotStarts],
  };
}
