import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { stat } from 'node:fs/promises';
import path from 'node:path';

import type { UpstreamGameCatalog } from './combinedRandomizerCore.js';

/**
 * Orchestration-level check that a workspace catalog still describes the game
 * directory it is about to be used against (integration Blocker 6).
 *
 * `DsrDataTool scan` records every game file it read in `sourceFiles` as
 * `{ path, size, lastWriteTimeUtc, sha256 }`, with `path` relative to the
 * scanned directory. `patch-enemies` resolves those relative paths under
 * `--game`, and asserts the hash of each file it touches — but only after the
 * JS generator has already produced a layout from the catalog. Checking all
 * recorded sources up front turns "the game directory is not the one this
 * catalog came from" into an early, explicit failure.
 *
 * `lastWriteTimeUtc` is deliberately ignored: copying or restoring a clean
 * installation changes it without changing content.
 *
 * This does NOT prove the catalog was scanned from vanilla data. A catalog
 * scanned from an already-modified installation verifies cleanly against that
 * same installation; pristine provenance belongs to the backend's pristine
 * import/backup authority.
 */

export type CatalogSourceVerificationResult =
  | { ok: true; verifiedFiles: number }
  | { ok: false; problems: string[] };

interface SourceFileRecord {
  path: string;
  size: number;
  sha256: string;
}

function isSourceFileRecord(value: unknown): value is SourceFileRecord {
  if (!value || typeof value !== 'object') return false;
  const record = value as Record<string, unknown>;
  return typeof record.path === 'string'
    && record.path.length > 0
    && typeof record.size === 'number'
    && typeof record.sha256 === 'string';
}

async function hashFile(filePath: string): Promise<string> {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(filePath)) hash.update(chunk as Buffer);
  return hash.digest('hex');
}

/** Resolves a catalog-relative path under the game root, or null if it escapes it. */
function resolveInsideGame(gameRoot: string, relativePath: string): string | null {
  if (path.isAbsolute(relativePath) || path.win32.isAbsolute(relativePath)) return null;
  const resolved = path.resolve(gameRoot, relativePath);
  const relative = path.relative(gameRoot, resolved);
  if (relative === '' || relative.startsWith('..') || path.isAbsolute(relative)) return null;
  return resolved;
}

export async function verifyCatalogSources(
  gamePath: string,
  catalog: UpstreamGameCatalog,
): Promise<CatalogSourceVerificationResult> {
  const sourceFiles = catalog.sourceFiles;
  if (!Array.isArray(sourceFiles) || sourceFiles.length === 0) {
    return {
      ok: false,
      problems: ['The catalog records no source files, so it cannot be verified against the game directory.'],
    };
  }

  const gameRoot = path.resolve(gamePath);
  const problems: string[] = [];
  for (const [index, record] of sourceFiles.entries()) {
    if (!isSourceFileRecord(record)) {
      problems.push(`sourceFiles[${index}] is malformed.`);
      continue;
    }

    const filePath = resolveInsideGame(gameRoot, record.path);
    if (!filePath) {
      problems.push(`${record.path}: path is outside the game directory.`);
      continue;
    }

    let size: number;
    try {
      const info = await stat(filePath);
      if (!info.isFile()) {
        problems.push(`${record.path}: not a file.`);
        continue;
      }
      size = info.size;
    } catch {
      problems.push(`${record.path}: missing.`);
      continue;
    }

    if (size !== record.size) {
      problems.push(`${record.path}: size ${size} does not match catalog size ${record.size}.`);
      continue;
    }

    if ((await hashFile(filePath)) !== record.sha256.toLowerCase()) {
      problems.push(`${record.path}: SHA-256 does not match the catalog.`);
    }
  }

  return problems.length > 0 ? { ok: false, problems } : { ok: true, verifiedFiles: sourceFiles.length };
}
