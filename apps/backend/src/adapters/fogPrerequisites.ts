import { access, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import type { FogLanguage } from '@ds1tr/contracts';
import { fogReadSet } from './fogCompatibility.js';
import { prepareWorldData, type WorldDataProgress, type WorldDataResult } from './worldData.js';
import { defaultStateRoot } from '../core/gameState.js';

const repoRoot = process.env.DS1TR_RUNTIME_ROOT ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../');

/**
 * Developer/test override: a folder that contains FogMod's `dist` at commit 6e6dc78. Normal users never set it; the
 * application prepares the data itself (worldData.ts).
 */
export const fogDataRootVariable = 'DS1TR_FOG_DATA_ROOT';
/** Optional override for the FogHost executable. */
export const fogHostVariable = 'DS1TR_FOG_HOST';
export const defaultFogHostExecutable = process.env.DS1TR_RUNTIME_ROOT
  ? path.join(repoRoot, 'tools', 'fog-host', 'FogHost.exe')
  : path.join(repoRoot, 'tools', 'fog-host', 'bin', 'Release', 'net8.0', 'FogHost.exe');

export interface FogPrerequisites {
  executablePath: string;
  dataRoot: string;
  readSet: string[];
  /** Id of the application-prepared world data set (its folder name); absent when a developer override supplies the data. */
  worldDataId?: string;
}

/** Why the world randomizer cannot run: a plain-language message for the person and the exact reason for diagnostics. */
export interface FogPrerequisiteFailure {
  failure: { code: string; userMessage: string; technical: string };
}

export interface FogPreparationContext {
  /** Verified clean snapshot of the installation (source of the game-derived files). */
  pristineDirectory: string;
  cacheRoot?: string;
  onProgress?: (progress: WorldDataProgress) => void;
  prepare?: (options: Parameters<typeof prepareWorldData>[0]) => Promise<WorldDataResult>;
}

export const defaultWorldDataCacheRoot = (): string => path.join(defaultStateRoot(), 'world-data');

async function names(directory: string): Promise<string[]> {
  try {
    return (await readdir(directory, { withFileTypes: true })).filter((entry) => entry.isFile()).map((entry) => entry.name);
  } catch {
    // FogHost verifies the data root file by file and reports FOG_DATA_MISSING with details.
    return [];
  }
}

/**
 * Resolves FogHost and its data root. Without an override the data root is the application's own prepared copy
 * (derived from the pristine snapshot plus a verified one-time download, see worldData.ts); the user chooses nothing.
 * FogHost still verifies the data against its pinned hash manifest on every run.
 * Returns null only when there is neither an override nor a preparation context (callers that cannot prepare).
 */
export async function resolveFogPrerequisites(
  language: FogLanguage,
  env: NodeJS.ProcessEnv = process.env,
  context?: FogPreparationContext,
): Promise<FogPrerequisites | FogPrerequisiteFailure | null> {
  const configured = env[fogDataRootVariable];
  if (!configured && !context) return null;
  const executablePath = path.resolve(env[fogHostVariable] || defaultFogHostExecutable);
  try { await access(executablePath); } catch {
    return { failure: {
      code: 'FOG_HOST_MISSING',
      userMessage: 'A part of the world randomizer is missing from this installation. Please download DS1 Triple Randomizer again.',
      technical: `FogHost executable not found: ${executablePath}`,
    } };
  }
  let dataRoot: string;
  let worldDataId: string | undefined;
  if (configured) {
    dataRoot = path.resolve(configured);
  } else {
    const prepared = await (context!.prepare ?? prepareWorldData)({
      cacheRoot: context!.cacheRoot ?? defaultWorldDataCacheRoot(),
      pristineDirectory: context!.pristineDirectory,
      onProgress: context!.onProgress,
    });
    if (!prepared.ok) return { failure: { code: prepared.code, userMessage: prepared.userMessage, technical: prepared.technical } };
    dataRoot = prepared.root;
    worldDataId = path.basename(prepared.root);
  }
  const events = await names(path.join(dataRoot, 'dist', 'DS1R', 'event'));
  const maps = await names(path.join(dataRoot, 'dist', 'DS1R', 'map', 'MapStudio'));
  return { executablePath, dataRoot, readSet: fogReadSet(events, maps, language), ...(worldDataId ? { worldDataId } : {}) };
}
