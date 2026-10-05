import { createHash } from 'node:crypto';
import { access, mkdir, readFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import type { EnemyPoolDescription } from '@ds1tr/contracts';
import { defaultDataToolDllPath, dotnetPathEnvironmentVariable, supportedCatalogSchemaVersion } from './combinedRandomizerBridge.js';
import { loadCombinedRandomizerCore, type CombinedRandomizerCoreLoadResult, type UpstreamGameCatalog } from './combinedRandomizerCore.js';
import { DsrDataToolExecutor, type DsrDataToolExecutionRequest, type DsrDataToolExecutionResult } from './combinedRandomizerDataToolExecutor.js';

export type EnemyPoolFailureCode =
  | 'PRISTINE_IMPORT_REQUIRED'
  | 'JS_CORE_MISSING'
  | 'JS_CORE_INCOMPLETE'
  | 'DATA_TOOL_MISSING'
  | 'DATA_TOOL_FAILED'
  | 'CATALOG_UNAVAILABLE'
  | 'CATALOG_STALE'
  | 'ENEMY_POOL_UNAVAILABLE';

export type EnemyPoolResult =
  | { ok: true; pool: EnemyPoolDescription }
  | { ok: false; code: EnemyPoolFailureCode; message: string };

export interface EnemyPoolDependencies {
  loadCore?: () => Promise<CombinedRandomizerCoreLoadResult>;
  dataTool?: { execute(request: DsrDataToolExecutionRequest): Promise<DsrDataToolExecutionResult> };
  readTextFile?: (target: string) => Promise<string>;
  fileExists?: (target: string) => Promise<boolean>;
  /** Where the scanned catalog is cached; defaults to a folder in the OS temp directory. */
  cacheRoot?: string;
  dotnetExecutablePath?: string;
  dataToolDllPath?: string;
}

async function exists(target: string): Promise<boolean> {
  try {
    await access(target);
    return true;
  } catch {
    return false;
  }
}

/**
 * The regular enemies a custom pool can contain, derived from the game data with the engine's own slot filter and
 * class table (never a list kept in the renderer). The catalog is scanned from the pristine source once per snapshot and
 * cached; the renderer never reads game files.
 */
export async function describeEnemyPool(
  pristine: { directory: string; snapshotId?: string | null } | null,
  dependencies: EnemyPoolDependencies = {},
): Promise<EnemyPoolResult> {
  if (!pristine) {
    return { ok: false, code: 'PRISTINE_IMPORT_REQUIRED', message: 'Import the clean game data first; the enemy list is read from it.' };
  }
  const loadCore = dependencies.loadCore ?? (() => loadCombinedRandomizerCore());
  const dataTool = dependencies.dataTool ?? new DsrDataToolExecutor();
  const readTextFile = dependencies.readTextFile ?? ((target: string) => readFile(target, 'utf8'));
  const fileExists = dependencies.fileExists ?? exists;
  const dotnetExecutablePath = dependencies.dotnetExecutablePath ?? process.env[dotnetPathEnvironmentVariable] ?? 'dotnet';
  const dataToolDllPath = dependencies.dataToolDllPath ?? (process.env.DS1TR_DATA_TOOL_EXE ? '' : defaultDataToolDllPath);

  const core = await loadCore();
  if (!core.ok) return { ok: false, code: core.code === 'JS_CORE_MISSING' ? 'JS_CORE_MISSING' : 'JS_CORE_INCOMPLETE', message: core.message };
  if (!core.core.describeRegularEnemyPool) {
    return { ok: false, code: 'ENEMY_POOL_UNAVAILABLE', message: 'The vendored combined engine cannot describe its enemy pool.' };
  }

  const key = createHash('sha256').update(`${pristine.snapshotId ?? ''}\0${pristine.directory}`).digest('hex').slice(0, 24);
  const directory = path.join(dependencies.cacheRoot ?? path.join(os.tmpdir(), 'ds1tr-enemy-pool'), key);
  const catalogPath = path.join(directory, 'dsr-catalog.json');
  if (!await fileExists(catalogPath)) {
    await mkdir(directory, { recursive: true });
    const execution = await dataTool.execute({
      dotnetExecutablePath, dataToolDllPath, plannedArguments: ['scan', '--game', pristine.directory, '--output', catalogPath],
    });
    if (!execution.ok) {
      return execution.type === 'spawn-failure'
        ? { ok: false, code: 'DATA_TOOL_MISSING', message: `DsrDataTool could not be started: ${execution.message}` }
        : { ok: false, code: 'DATA_TOOL_FAILED', message: `DsrDataTool scan failed with exit code ${execution.exitCode ?? 'unknown'}.` };
    }
    if (!await fileExists(catalogPath)) return { ok: false, code: 'CATALOG_UNAVAILABLE', message: 'The scan produced no catalog.' };
  }

  let catalog: UpstreamGameCatalog;
  try {
    catalog = JSON.parse(await readTextFile(catalogPath)) as UpstreamGameCatalog;
  } catch (error) {
    return { ok: false, code: 'CATALOG_UNAVAILABLE', message: `The catalog could not be read: ${error instanceof Error ? error.message : String(error)}` };
  }
  if (catalog.schemaVersion !== supportedCatalogSchemaVersion) {
    return { ok: false, code: 'CATALOG_STALE', message: `The catalog uses schema ${String(catalog.schemaVersion)}; schema ${supportedCatalogSchemaVersion} is required.` };
  }
  const described = core.core.describeRegularEnemyPool({}, catalog);
  return {
    ok: true,
    pool: { source: 'pristine', classTableHash: described.classTableHash, regularSlots: described.regularSlots, entries: described.entries },
  };
}
