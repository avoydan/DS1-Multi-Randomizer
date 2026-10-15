import { access, readdir } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import type { FogLanguage } from '@ds1tr/contracts';
import { fogReadSet } from './fogCompatibility.js';
import {
  compactRuntimeWorldDataId, compactRuntimeWorldDataManifest, prepareWorldData,
  type WorldDataOptions, type WorldDataProgress, type WorldDataResult,
} from './worldData.js';
import { loadCompactFogProjection, type CompactFogProjection } from './compactFogProjection.js';
import { defaultStateRoot } from '../core/gameState.js';
import { fogHostRuntimeForProfile, type FogHostRuntimeContract, type GenerationProfile } from '../core/generationProfiles.js';

const repoRoot = process.env.DS1TR_RUNTIME_ROOT ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../');

/**
 * Developer/test override: a folder that contains FogMod's `dist` at commit 6e6dc78. Normal users never set it; the
 * application prepares the data itself (worldData.ts).
 */
export const fogDataRootVariable = 'DS1TR_FOG_DATA_ROOT';
/** Optional override for the FogHost executable. */
export const fogHostVariable = 'DS1TR_FOG_HOST';
/** Optional developer override for the frozen v0.5.0 generation host. */
export const legacyFogHostVariable = 'DS1TR_FOG_HOST_LEGACY';
export const defaultFogHostExecutable = process.env.DS1TR_RUNTIME_ROOT
  ? path.join(repoRoot, 'tools', 'fog-host', 'FogHost.exe')
  : path.join(repoRoot, 'tools', 'fog-host', 'bin', 'Release', 'net8.0', 'FogHost.exe');
export const defaultLegacyFogHostExecutable = process.env.DS1TR_RUNTIME_ROOT
  ? path.join(repoRoot, 'tools', 'fog-host-legacy', 'FogHost.exe')
  : path.join(repoRoot, 'apps', 'desktop', 'release', 'runtime', 'tools', 'fog-host-legacy', 'FogHost.exe');

export interface FogPrerequisites {
  executablePath: string;
  dataRoot: string;
  readSet: string[];
  annotationSource: 'legacy-fogmod' | 'compact-v1';
  /** v0.6.0 exports progression graphs for legacy profiles; generation itself still uses executablePath (v0.5.0). */
  progressionModelExecutablePath: string;
  pristineGameDirectory?: string;
  compactProjection?: CompactFogProjection;
  /** Id of the application-prepared world data set (its folder name); absent when a developer override supplies the data. */
  worldDataId?: string;
}

/** Why the fog randomizer cannot run: a plain-language message for the person and the exact reason for diagnostics. */
export interface FogPrerequisiteFailure {
  failure: { code: string; userMessage: string; technical: string };
}

export interface FogPreparationContext {
  /** Verified clean snapshot of the installation (source of the game-derived files). */
  pristineDirectory: string;
  /** Exact, profile-derived protocol/runtime pair used for this generation profile. */
  fogRuntime: FogHostRuntimeContract;
  cacheRoot?: string;
  reconstructCompactData?: (pristineDirectory: string, outputRoot: string) => Promise<void>;
  onProgress?: (progress: WorldDataProgress) => void;
  prepare?: (options: Parameters<typeof prepareWorldData>[0]) => Promise<WorldDataResult>;
}

const execFileAsync = promisify(execFile);

async function reconstructCompactData(executablePath: string, pristineDirectory: string, outputRoot: string): Promise<void> {
  try {
    await execFileAsync(executablePath, [
      'reconstruct-compact-data', '--pristine-root', pristineDirectory, '--output-root', outputRoot,
    ], { windowsHide: true, timeout: 600_000, maxBuffer: 2 * 1024 * 1024 });
  } catch (error) {
    const detail = error !== null && typeof error === 'object' && 'stderr' in error && typeof error.stderr === 'string'
      ? error.stderr.trim() : '';
    throw new Error(detail || (error instanceof Error ? error.message : String(error)));
  }
}

export const defaultWorldDataCacheRoot = (): string => path.join(defaultStateRoot(), 'world-data');

/** Prepares the data selected by the Seed Code profile (or current profile for a new run). */
export function worldDataOptionsForProfile(
  profile: Pick<GenerationProfile, 'fogAnnotationSource' | 'fogEngine' | 'fogItemTracking'>,
  pristineDirectory: string,
  env: NodeJS.ProcessEnv = process.env,
  cacheRoot = defaultWorldDataCacheRoot(),
): WorldDataOptions {
  const compact = profile.fogAnnotationSource === 'compact-v1';
  const runtime = fogHostRuntimeForProfile(profile);
  if (!runtime) throw new Error('The selected generation profile has no supported FogHost runtime contract.');
  const executablePath = path.resolve(runtime.hostVersion === '0.5.0'
    ? env[legacyFogHostVariable] || defaultLegacyFogHostExecutable
    : env[fogHostVariable] || defaultFogHostExecutable);
  return {
    cacheRoot,
    pristineDirectory,
    ...(compact ? {
      manifest: compactRuntimeWorldDataManifest,
      id: compactRuntimeWorldDataId,
      reconstructFiles: (source: string, output: string) => reconstructCompactData(executablePath, source, output),
    } : { id: profile.fogEngine.worldDataId }),
    allowUpstreamDownloads: env.DS1TR_DISTRIBUTION === 'github',
  };
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
  const runtime = context?.fogRuntime;
  if (!runtime || !((runtime.hostVersion === '0.5.0' && runtime.schemaVersion === 2 && runtime.annotationSource === 'legacy-fogmod')
    || (runtime.hostVersion === '0.6.0' && runtime.schemaVersion === 3 && runtime.annotationSource === 'compact-v1'))) {
    return { failure: {
      code: 'FOG_PROFILE_RUNTIME_INVALID',
      userMessage: 'The fog randomizer cannot run for this generation profile. Please update or reinstall DS1 Multi Randomizer.',
      technical: 'No supported profile-derived FogHost runtime contract was supplied.',
    } };
  }
  const hostOverride = runtime.hostVersion === '0.5.0' ? legacyFogHostVariable : fogHostVariable;
  const defaultHost = runtime.hostVersion === '0.5.0' ? defaultLegacyFogHostExecutable : defaultFogHostExecutable;
  const executablePath = path.resolve(env[hostOverride] || defaultHost);
  // Progression model export was added in 0.6.0 as a read-only helper. It may read legacy data,
  // but the legacy profile's randomized output is always produced by its frozen 0.5.0 executable.
  const progressionModelExecutablePath = path.resolve(env[fogHostVariable] || defaultFogHostExecutable);
  try { await access(executablePath); } catch {
    return { failure: {
      code: 'FOG_HOST_MISSING',
      userMessage: 'A part of the fog randomizer is missing from this installation. Please download DS1 Multi Randomizer again.',
      technical: `FogHost executable not found: ${executablePath}`,
    } };
  }
  let dataRoot: string;
  let worldDataId: string | undefined;
  const annotationSource = runtime.annotationSource;
  if (configured) {
    dataRoot = path.resolve(configured);
  } else {
    const prepared = await (context!.prepare ?? prepareWorldData)({
      cacheRoot: context!.cacheRoot ?? defaultWorldDataCacheRoot(),
      pristineDirectory: context!.pristineDirectory,
      ...(annotationSource === 'compact-v1'
        ? {
          manifest: compactRuntimeWorldDataManifest,
          id: compactRuntimeWorldDataId,
          reconstructFiles: context!.reconstructCompactData
            ?? ((pristineDirectory: string, outputRoot: string) => reconstructCompactData(executablePath, pristineDirectory, outputRoot)),
        }
        : { id: worldDataId }),
      allowUpstreamDownloads: env.DS1TR_DISTRIBUTION === 'github',
      onProgress: context!.onProgress,
    });
    if (!prepared.ok) return { failure: { code: prepared.code, userMessage: prepared.userMessage, technical: prepared.technical } };
    dataRoot = prepared.root;
    worldDataId = annotationSource === 'compact-v1' ? 'compact-runtime-offline-v1' : path.basename(prepared.root);
  }
  const events = await names(path.join(dataRoot, 'dist', 'DS1R', 'event'));
  const maps = await names(path.join(dataRoot, 'dist', 'DS1R', 'map', 'MapStudio'));
  if (annotationSource === 'compact-v1') {
    if (!context?.pristineDirectory) return null;
    try {
      const compactProjection = await loadCompactFogProjection(executablePath, dataRoot, context.pristineDirectory);
      return {
        executablePath,
        progressionModelExecutablePath,
        dataRoot,
        readSet: fogReadSet(events, maps, language),
        annotationSource,
        pristineGameDirectory: path.resolve(context.pristineDirectory),
        compactProjection,
        ...(worldDataId ? { worldDataId } : {}),
      };
    } catch (error) {
      const technical = error instanceof Error ? error.message : String(error);
      return { failure: {
        code: 'FOG_COMPACT_PROJECTION_UNAVAILABLE',
        userMessage: 'The local fog randomizer catalog could not be prepared. Try again, or check the game files.',
        technical,
      } };
    }
  }
  return { executablePath, progressionModelExecutablePath, dataRoot, readSet: fogReadSet(events, maps, language), annotationSource,
    ...(worldDataId ? { worldDataId } : {}) };
}
