import { access, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import type { CombinedRandomizerEvent, CombinedRandomizerRequest } from './combinedRandomizerAdapter.js';
import {
  verifyCatalogSources as defaultVerifyCatalogSources,
  type CatalogSourceVerificationResult,
} from './combinedRandomizerCatalog.js';
import { mapCombinedRandomizerConfig } from './combinedRandomizerConfig.js';
import { formatPlacementDiagnostics, summarizePlacementDiagnostics, type PlacementDiagnosticsSummary } from '../core/placementDiagnostics.js';
import {
  loadCombinedRandomizerCore,
  type CombinedRandomizerCoreLoadResult,
  type UpstreamGameCatalog,
  type UpstreamRandomizerConfig,
} from './combinedRandomizerCore.js';
import {
  DsrDataToolExecutor,
  type DsrDataToolCommand,
  type DsrDataToolEvent,
  type DsrDataToolEventCallback,
  type DsrDataToolExecutionRequest,
  type DsrDataToolExecutionResult,
} from './combinedRandomizerDataToolExecutor.js';
import {
  planCombinedRandomizerWorkspace,
  type CombinedRandomizerWorkspacePlan,
} from './combinedRandomizerWorkspace.js';

/**
 * Minimal combined-engine execution bridge: the single internal path that
 * turns a typed `CombinedRandomizerRequest` into a generated, workspace-only
 * package by chaining the pieces that already exist independently — the pure
 * config mapper, the vendored upstream JS generation core, the pure workspace
 * planner, and the `DsrDataTool` process executor.
 *
 * It only ever drives the two upstream commands that write exclusively under
 * a caller-supplied output directory (`scan`, `patch-enemies`). Upstream
 * `install`/`restore` are unreachable from here: the workspace planner has no
 * function that emits their argument vectors, and the executor rejects every
 * command outside its own two-command allowlist.
 *
 * Activation of a generated package into the live game is deliberately not
 * part of this bridge — it belongs to the backend's own single backup
 * authority, a separate and later stage. See
 * docs/COMBINED_ENGINE_INTEGRATION.md ("Execution bridge proof").
 */

const currentDirectory = path.dirname(fileURLToPath(import.meta.url));

/** Repository root, resolved identically under `tsx` and when compiled. */
const repositoryRoot = process.env.DS1TR_RUNTIME_ROOT ?? path.resolve(currentDirectory, '../../../../');

/**
 * Built `DsrDataTool` artifact produced by the documented vendored build
 * (docs/COMBINED_ENGINE_INTEGRATION.md, "Vendored build proof"). Gitignored
 * build output, so its absence is an expected, reportable condition rather
 * than a defect.
 */
export const defaultDataToolDllPath = path.join(
  repositoryRoot,
  'engines', 'combined', 'vendor', 'tools', 'DsrDataTool',
  'bin', 'Release', 'net8.0', 'DsrDataTool.dll',
);

/**
 * The .NET 8 host used to run the tool. Defaults to whatever `dotnet` is on
 * `PATH`; `DS1TR_DOTNET_PATH` exists because the documented local SDK lives
 * outside the repository tree on Windows (docs/DECISIONS.md — a short path
 * avoids the legacy `MAX_PATH` limit).
 */
export const dotnetPathEnvironmentVariable = 'DS1TR_DOTNET_PATH';

/** Catalog schema the pinned upstream `generate()` accepts, and only that one. */
export const supportedCatalogSchemaVersion = 17;

export type CombinedGenerationFailureCode =
  | 'NO_RANDOMIZATION_FEATURES_ENABLED'
  | 'ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE'
  | 'ENEMY_PLACEMENT_OPTIONS_INVALID'
  | 'ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE'
  | 'ENEMY_PLACEMENT_CLASS_UNDERIVABLE'
  | 'ENEMY_PLACEMENT_INFEASIBLE'
  | 'JS_CORE_MISSING'
  | 'JS_CORE_INCOMPLETE'
  | 'INVALID_CONFIG'
  | 'WORKSPACE_INSIDE_GAME_DIRECTORY'
  | 'CATALOG_UNAVAILABLE'
  | 'CATALOG_UNREADABLE'
  | 'CATALOG_STALE'
  | 'CATALOG_SOURCE_MISMATCH'
  | 'GENERATION_FAILED'
  | 'PROTOTYPE_RESULT_REJECTED'
  | 'OUTPUT_WRITE_FAILED'
  | 'OUTPUT_LAYOUT_MISMATCH'
  | 'DATA_TOOL_MISSING'
  | 'DATA_TOOL_FAILED';

export interface CombinedGenerationSuccess {
  ok: true;
  /** `<workspace>/seed_<safeSeed>` — the generated package directory. */
  seedDirectory: string;
  workspaceDirectory: string;
  catalogPath: string;
  /** `randomizer.json`, the placements file `patch-enemies` consumed. */
  placementsPath: string;
  /** `patch-manifest.json`, the input the later activation stage reads. */
  patchManifestPath: string;
  /** Upstream `RANDOMIZER_VERSION`, for the run manifest. */
  randomizerVersion: string;
  /** Upstream placement hash — the determinism fingerprint of this run. */
  placementHash: string;
  /** Placement algorithm identity and, for sampling-v1, its summary (never the per-slot detail). */
  placementDiagnostics: PlacementDiagnosticsSummary;
  /** The seed actually handed to upstream, after `normalizeConfig`. */
  seed: string;
  /** Whether this run had to `scan` the game directory to build a catalog. */
  catalogScanned: boolean;
}

export interface CombinedGenerationFailure {
  ok: false;
  code: CombinedGenerationFailureCode;
  message: string;
  /** True only where retrying with a different derived seed can help. */
  retryable: boolean;
  validationErrors?: string[];
  /** Per-file reasons a reused catalog no longer matches the game directory. */
  catalogProblems?: string[];
  missingExports?: string[];
  specifier?: string;
  command?: DsrDataToolCommand;
  exitCode?: number | null;
  stdout?: string;
  stderr?: string;
}

export type CombinedGenerationResult = CombinedGenerationSuccess | CombinedGenerationFailure;

/** Structural view of the executor, so tests can supply a fake process layer. */
export interface DsrDataToolRunner {
  execute(
    request: DsrDataToolExecutionRequest,
    onEvent?: DsrDataToolEventCallback,
  ): Promise<DsrDataToolExecutionResult>;
}

export interface CombinedGenerationDependencies {
  loadCore?: () => Promise<CombinedRandomizerCoreLoadResult>;
  dataTool?: DsrDataToolRunner;
  fileExists?: (target: string) => Promise<boolean>;
  readTextFile?: (target: string) => Promise<string>;
  verifyCatalogSources?: (
    gamePath: string,
    catalog: UpstreamGameCatalog,
  ) => Promise<CatalogSourceVerificationResult>;
}

export interface CombinedGenerationOptions {
  dotnetExecutablePath?: string;
  dataToolDllPath?: string;
  /** Re-`scan` even when a catalog is already present in the workspace. */
  forceCatalogRescan?: boolean;
  dependencies?: CombinedGenerationDependencies;
}

async function defaultFileExists(target: string): Promise<boolean> {
  try {
    await access(target);
    return true;
  } catch {
    return false;
  }
}

const defaultReadTextFile = (target: string): Promise<string> => readFile(target, 'utf8');

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * Stage boundaries the bridge itself controls. Upstream `DsrDataTool` reports
 * nothing until a command exits, so real percentage progress is impossible;
 * these are coarse and monotonic by design.
 */
const stageProgress = {
  configured: 0.05,
  planned: 0.1,
  scanStarted: 0.15,
  catalogReady: 0.35,
  generated: 0.5,
  packageWritten: 0.6,
  patchStarted: 0.65,
  patched: 1,
} as const;

/**
 * Executes one combined Items + Enemies generation run entirely inside
 * `request.workspacePath`, reading `request.gamePath` only through upstream's
 * two read-only commands.
 *
 * Returns typed failures rather than throwing: every stage that can fail for
 * an environmental reason (absent vendor tree, absent build output, absent or
 * stale catalog, rejected config, upstream process failure) maps to one
 * documented code.
 */
export async function executeCombinedGeneration(
  request: CombinedRandomizerRequest,
  onEvent: (event: CombinedRandomizerEvent) => void = () => {},
  options: CombinedGenerationOptions = {},
): Promise<CombinedGenerationResult> {
  const dependencies = options.dependencies ?? {};
  const loadCore = dependencies.loadCore ?? (() => loadCombinedRandomizerCore());
  const dataTool = dependencies.dataTool ?? new DsrDataToolExecutor();
  const fileExists = dependencies.fileExists ?? defaultFileExists;
  const readTextFile = dependencies.readTextFile ?? defaultReadTextFile;
  const verifyCatalogSources = dependencies.verifyCatalogSources ?? defaultVerifyCatalogSources;
  const dotnetExecutablePath = options.dotnetExecutablePath
    ?? process.env[dotnetPathEnvironmentVariable]
    ?? 'dotnet';
  const dataToolDllPath = options.dataToolDllPath ?? (process.env.DS1TR_DATA_TOOL_EXE ? '' : defaultDataToolDllPath);

  const progress = (value: number, message: string): void => {
    onEvent({ type: 'progress', progress: value, message });
  };
  const log = (level: 'info' | 'warning' | 'error', message: string): void => {
    onEvent({ type: 'log', level, message });
  };

  // 1. Pure request -> upstream config mapping, including the `enabled`
  //    AND-ing and the "nothing enabled" rejection.
  const mapping = mapCombinedRandomizerConfig(request);
  if (!mapping.ok) {
    return { ok: false, code: mapping.code, message: mapping.message, retryable: false };
  }

  // 2. Load the vendored upstream core before touching the filesystem, so an
  //    incomplete vendor tree fails before any process is spawned.
  const coreLoad = await loadCore();
  if (!coreLoad.ok) {
    return coreLoad.code === 'JS_CORE_MISSING'
      ? {
        ok: false,
        code: 'JS_CORE_MISSING',
        message: coreLoad.message,
        retryable: false,
        specifier: coreLoad.specifier,
      }
      : {
        ok: false,
        code: 'JS_CORE_INCOMPLETE',
        message: coreLoad.message,
        retryable: false,
        missingExports: coreLoad.missingExports,
      };
  }
  const core = coreLoad.core;

  // 3. Two keys upstream defaults would otherwise decide for us, pinned here
  //    rather than inherited. `useExtractedData` and `dryRun` match the
  //    documented data flow; neither is read anywhere in the vendored core or
  //    in `DsrDataTool`, so both are inert today and are set only so the
  //    recorded `config.json` states them explicitly. `generateSpoilerLog`
  //    comes from the request through the mapper. `outputDirectory` is
  //    resolved so the recorded config matches the absolute workspace the
  //    planner computes below.
  const upstreamInput: UpstreamRandomizerConfig = {
    ...mapping.config,
    outputDirectory: path.resolve(request.workspacePath),
    useExtractedData: true,
    dryRun: false,
  };

  const config = core.normalizeConfig(upstreamInput);
  const validationErrors = core.validateConfig(config, { requireGame: false });
  if (validationErrors.length > 0) {
    return {
      ok: false,
      code: 'INVALID_CONFIG',
      message: `The combined engine rejected the generation config: ${validationErrors.join(' ')}`,
      retryable: false,
      validationErrors,
    };
  }
  progress(stageProgress.configured, 'Generation config mapped and validated.');

  // 4. Plan the workspace from the *normalized* seed, never from the raw
  //    request seed. `normalizeConfig` trims and coerces `seed`, and
  //    upstream's `writeOutput` derives its own `seed_<safeSeed>` directory
  //    from that normalized value — planning from the raw value would point
  //    `--placements` at a path upstream never wrote.
  const normalizedSeed = typeof config.seed === 'string' ? config.seed : String(config.seed ?? '');
  const planResult = planCombinedRandomizerWorkspace({
    gamePath: request.gamePath,
    workspacePath: request.workspacePath,
    combinedSeed: normalizedSeed,
  });
  if (!planResult.ok) {
    return { ok: false, code: planResult.code, message: planResult.message, retryable: false };
  }
  const plan: CombinedRandomizerWorkspacePlan = planResult.plan;
  progress(stageProgress.planned, 'Workspace layout planned.');

  const forwardDataToolEvent = (event: DsrDataToolEvent): void => {
    if (event.type === 'log') {
      log(event.stream === 'stderr' ? 'warning' : 'info', event.message.trimEnd());
      return;
    }
    log('info', `DsrDataTool ${event.command} ${event.status}.`);
  };

  const runDataTool = async (
    command: DsrDataToolCommand,
    plannedArguments: string[],
  ): Promise<CombinedGenerationFailure | null> => {
    const execution = await dataTool.execute(
      { dotnetExecutablePath, dataToolDllPath, plannedArguments },
      forwardDataToolEvent,
    );
    if (execution.ok) return null;

    if (execution.type === 'spawn-failure') {
      return {
        ok: false,
        code: 'DATA_TOOL_MISSING',
        message:
          `DsrDataTool could not be started for '${command}': ${execution.message} `
          + `(dotnet: '${dotnetExecutablePath}', tool: '${dataToolDllPath}'). `
          + 'Build the vendored DsrDataTool and make a .NET 8 runtime available.',
        retryable: false,
        command,
        exitCode: null,
        stdout: execution.stdout,
        stderr: execution.stderr,
      };
    }
    return {
      ok: false,
      code: 'DATA_TOOL_FAILED',
      message:
        `DsrDataTool '${command}' failed with exit code ${execution.exitCode ?? 'unknown'}: `
        + `${execution.stderr.trim() || execution.stdout.trim() || '(no output)'}`,
      retryable: false,
      command,
      exitCode: execution.exitCode,
      stdout: execution.stdout,
      stderr: execution.stderr,
    };
  };

  // 5. Catalog: extracted game data is a hard requirement, and `scan` is the
  //    only way to produce it. Reuse an existing catalog unless a rescan was
  //    requested; a reused catalog is verified against the game directory
  //    below, after its schema check.
  const catalogPresent = !options.forceCatalogRescan && await fileExists(plan.catalogPath);
  if (!catalogPresent) {
    progress(stageProgress.scanStarted, 'Scanning the game installation to build a data catalog.');
    const scanFailure = await runDataTool('scan', plan.scanArgs);
    if (scanFailure) return scanFailure;
    if (!await fileExists(plan.catalogPath)) {
      return {
        ok: false,
        code: 'CATALOG_UNAVAILABLE',
        message:
          `DsrDataTool 'scan' reported success but no catalog exists at '${plan.catalogPath}'.`,
        retryable: false,
      };
    }
  }

  let catalog: UpstreamGameCatalog;
  try {
    catalog = JSON.parse(await readTextFile(plan.catalogPath)) as UpstreamGameCatalog;
  } catch (error) {
    return {
      ok: false,
      code: 'CATALOG_UNREADABLE',
      message: `The data catalog at '${plan.catalogPath}' could not be read: ${messageOf(error)}`,
      retryable: false,
    };
  }

  // Checked here rather than left to upstream: `generate()` throws on an
  // obsolete schema, which would otherwise surface as an opaque
  // GENERATION_FAILED instead of an actionable "import the data again".
  if (catalog.schemaVersion !== supportedCatalogSchemaVersion) {
    return {
      ok: false,
      code: 'CATALOG_STALE',
      message:
        `The data catalog at '${plan.catalogPath}' uses schema `
        + `${String(catalog.schemaVersion)}, but this engine requires schema `
        + `${supportedCatalogSchemaVersion}. Re-import the clean game data.`,
      retryable: false,
    };
  }

  // A reused catalog must still describe *this* game directory: every source
  // file it recorded must exist with the same size and SHA-256 (integration
  // Blocker 6). A mismatch is a hard failure, never an automatic rescan — a
  // rescan of a modified installation would bake modified data into the
  // catalog. A catalog scanned moments ago by this run is skipped.
  if (catalogPresent) {
    log('info', 'Verifying the reused data catalog against the game directory.');
    const verification = await verifyCatalogSources(request.gamePath, catalog);
    if (!verification.ok) {
      return {
        ok: false,
        code: 'CATALOG_SOURCE_MISMATCH',
        message:
          `The data catalog at '${plan.catalogPath}' does not match the game directory `
          + `'${request.gamePath}' (${verification.problems.length} problem(s), first: `
          + `${verification.problems[0]}). Restore the clean game files, or re-import the `
          + 'game data if this directory is the intended clean installation.',
        retryable: false,
        catalogProblems: verification.problems,
      };
    }
  }
  progress(stageProgress.catalogReady, 'Game data catalog ready.');

  // 6. Pure, synchronous generation. No I/O happens inside `generate()`.
  let result;
  try {
    result = core.generate(config, { gameCatalog: catalog });
  } catch (error) {
    // The engine refuses an algorithm that is not implemented. Another seed cannot fix that.
    const errorCode = typeof error === 'object' && error !== null ? (error as { code?: unknown }).code : undefined;
    // Deterministic structural failures: the same configuration fails the same way with any seed, so they are
    // typed and never retried. (The budget error is defensive: the non-worsening budget cannot be unsatisfiable.)
    if (errorCode === 'ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE' || errorCode === 'ENEMY_PLACEMENT_OPTIONS_INVALID'
      || errorCode === 'ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE' || errorCode === 'ENEMY_PLACEMENT_CLASS_UNDERIVABLE') {
      return { ok: false, code: errorCode, message: messageOf(error), retryable: false };
    }
    // A slot that has no placement within the map's budget depends on the participation draws, so another
    // attempt (a new combined seed) can solve it. It keeps its own code so the attempt log shows why it retried.
    if (errorCode === 'ENEMY_PLACEMENT_INFEASIBLE') {
      return { ok: false, code: 'ENEMY_PLACEMENT_INFEASIBLE', message: messageOf(error), retryable: true };
    }
    return {
      ok: false,
      code: 'GENERATION_FAILED',
      message: `The combined engine could not produce a valid layout: ${messageOf(error)}`,
      // The one genuinely retryable failure: upstream gives up on some seeds
      // (e.g. no safe boss permutation), and another derived seed can succeed.
      retryable: true,
    };
  }

  // 7. Upstream silently substitutes fabricated placements when the catalog
  //    is unusable. Rejected before anything is written and before the patch
  //    process runs, so a prototype result can never reach a package
  //    directory (integration Blocker 1).
  if (result.dataStatus !== 'extracted') {
    return {
      ok: false,
      code: 'PROTOTYPE_RESULT_REJECTED',
      message:
        `The combined engine returned '${result.dataStatus}' placements instead of extracted `
        + 'game data. The data catalog is present but unusable, so no package was written.',
      retryable: false,
    };
  }
  const placementDiagnostics = summarizePlacementDiagnostics(result.placementDiagnostics);
  const placementLine = formatPlacementDiagnostics(placementDiagnostics);
  if (placementLine) log('info', placementLine);
  progress(stageProgress.generated, 'Randomized layout generated from extracted game data.');

  // 8. Upstream owns the package layout; the planner mirrors it. Writing
  //    through `writeOutput` keeps one definition of that layout.
  let writtenDirectory: string;
  try {
    writtenDirectory = await core.writeOutput(result, plan.workspaceDirectory);
  } catch (error) {
    return {
      ok: false,
      code: 'OUTPUT_WRITE_FAILED',
      message: `The generated package could not be written to '${plan.seedDirectory}': ${messageOf(error)}`,
      retryable: false,
    };
  }

  if (path.resolve(writtenDirectory) !== path.resolve(plan.seedDirectory)) {
    return {
      ok: false,
      code: 'OUTPUT_LAYOUT_MISMATCH',
      message:
        `The combined engine wrote its package to '${writtenDirectory}', but the planned `
        + `package directory is '${plan.seedDirectory}'. The vendored core's output layout no `
        + 'longer matches the workspace planner.',
      retryable: false,
    };
  }
  progress(stageProgress.packageWritten, 'Randomizer package written to the workspace.');

  // 9. Patch the extracted game data into the workspace package. Writes only
  //    under `--output`; upstream refuses an output path inside `--game`, and
  //    the planner already refused it earlier in pure TypeScript.
  progress(stageProgress.patchStarted, 'Patching enemy and item data into the workspace package.');
  const patchFailure = await runDataTool('patch-enemies', plan.patchEnemiesArgs);
  if (patchFailure) return patchFailure;
  progress(stageProgress.patched, 'Combined items and enemies generation completed.');

  return {
    ok: true,
    seedDirectory: plan.seedDirectory,
    workspaceDirectory: plan.workspaceDirectory,
    catalogPath: plan.catalogPath,
    placementsPath: plan.randomizerPlacementsPath,
    patchManifestPath: path.join(plan.seedDirectory, 'patch-manifest.json'),
    randomizerVersion: result.randomizerVersion,
    placementHash: result.placementHash,
    placementDiagnostics,
    seed: result.seed,
    catalogScanned: !catalogPresent,
  };
}
