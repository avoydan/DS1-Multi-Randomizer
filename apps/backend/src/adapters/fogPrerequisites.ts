import { access, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import type { FogLanguage } from '@ds1tr/contracts';
import { fogReadSet } from './fogCompatibility.js';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../');

/** Folder that contains FogMod's `dist` at commit 6e6dc78 (never bundled; supplied by the user). */
export const fogDataRootVariable = 'DS1TR_FOG_DATA_ROOT';
/** Optional override for the FogHost executable. */
export const fogHostVariable = 'DS1TR_FOG_HOST';
export const defaultFogHostExecutable = path.join(repoRoot, 'tools', 'fog-host', 'bin', 'Release', 'net8.0', 'FogHost.exe');

export interface FogPrerequisites {
  executablePath: string;
  dataRoot: string;
  readSet: string[];
}

async function names(directory: string): Promise<string[]> {
  try {
    return (await readdir(directory, { withFileTypes: true })).filter((entry) => entry.isFile()).map((entry) => entry.name);
  } catch {
    // FogHost verifies the data root file by file and reports FOG_DATA_MISSING with details.
    return [];
  }
}

/**
 * Resolves FogHost and the externally supplied FogMod data root. The data root is only
 * located here; FogHost verifies it against its pinned hash manifest on every run.
 * Returns null when no data root is configured or the host has not been built.
 */
export async function resolveFogPrerequisites(
  language: FogLanguage,
  env: NodeJS.ProcessEnv = process.env,
): Promise<FogPrerequisites | null> {
  const configured = env[fogDataRootVariable];
  if (!configured) return null;
  const executablePath = path.resolve(env[fogHostVariable] || defaultFogHostExecutable);
  try { await access(executablePath); } catch { return null; }
  const dataRoot = path.resolve(configured);
  const events = await names(path.join(dataRoot, 'dist', 'DS1R', 'event'));
  const maps = await names(path.join(dataRoot, 'dist', 'DS1R', 'map', 'MapStudio'));
  return { executablePath, dataRoot, readSet: fogReadSet(events, maps, language) };
}
