import { createHash } from 'node:crypto';
import { access, copyFile, cp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { EnemyPlacementAlgorithm, FogLanguage, RandomizerConfig } from '@ds1tr/contracts';
import { executeCombinedGeneration, type CombinedGenerationResult } from '../adapters/combinedRandomizerBridge.js';
import { buildFogCompatibility, decideFogRetry, defaultFogRetryBounds, planFogStaging } from '../adapters/fogCompatibility.js';
import { FogHostProcess, type FogHostResponse } from '../adapters/fogHostProcess.js';
import { fogDataRootVariable, resolveFogPrerequisites, type FogPrerequisites } from '../adapters/fogPrerequisites.js';
import { effectiveCombinedEnabled, validateRandomizerConfig } from './configValidation.js';
import { validateInstallation } from './installation.js';
import { deriveAttemptSeeds } from './seeds.js';
import type { PlacementSummary } from './placementDiagnostics.js';

/** Set to `detail` to add one record per slot to the sampling-v1 diagnostics in randomizer.json (debugging only). */
const placementDiagnosticsVariable = 'DS1TR_PLACEMENT_DIAGNOSTICS';

export interface GenerationRequest {
  config: RandomizerConfig;
  workspacePath: string;
}

export type GenerationEvent =
  | { type: 'progress'; stage: string; progress: number; message: string }
  | { type: 'log'; stage: string; level: 'info' | 'warning' | 'error'; message: string };

export interface GenerationManifest {
  schemaVersion: 1;
  appVersion: string;
  game: 'DSR';
  masterSeed: string;
  /** Snapshot the package was generated from; activation refuses any other. */
  pristineSnapshotId: string | null;
  /** Package-relative directory mirroring the game root ('' = package root). */
  gameTreeRoot: string;
  options: Omit<RandomizerConfig, 'gamePath' | 'masterSeed'>;
  /** Which enemy placement algorithm produced the layout, and the RNG version of its own streams (null: the frozen engine version). */
  enemyPlacement: {
    algorithm: EnemyPlacementAlgorithm;
    rngVersion: string | null;
    /** Hash of the derived class table the sampler used (sampling-v1 only). */
    classTableHash: string | null;
    /** Planner counters of the last combined attempt (sampling-v1 only). */
    summary: PlacementSummary | null;
  };
  attempts: Array<{ combinedAttempt: number; fogAttempt: number; combinedSeed: string; fogSeed: number; result: string }>;
  engines: { combined?: string; fog?: string };
  validation: string[];
  files: Array<{ path: string; size: number; sha256: string }>;
}

export type GenerationResult =
  | { ok: true; packagePath: string; manifestPath: string; manifest: GenerationManifest }
  | { ok: false; code: string; message: string; stage: string; manifest: GenerationManifest };

export interface PristineSource { directory: string; snapshotId?: string }
export type { FogPrerequisites } from '../adapters/fogPrerequisites.js';

export interface GenerationDependencies {
  /** Must return an independently verified, backend-owned pristine snapshot. */
  getPristineSource: (gamePath: string) => Promise<PristineSource | null>;
  /** The read set depends on the Fog message language. */
  getFogPrerequisites: (language: FogLanguage) => Promise<FogPrerequisites | null>;
  combined: typeof executeCombinedGeneration;
  fog: FogHostProcess;
}

const defaults: GenerationDependencies = {
  // A caller-supplied directory is never accepted as proof of pristine data.
  getPristineSource: async () => null,
  // FogHost from tools/fog-host plus a user-supplied FogMod data root (DS1TR_FOG_DATA_ROOT).
  getFogPrerequisites: (language) => resolveFogPrerequisites(language),
  combined: executeCombinedGeneration,
  fog: new FogHostProcess(),
};

async function fingerprint(file: string): Promise<{ size: number; sha256: string }> {
  const content = await readFile(file);
  return { size: content.length, sha256: createHash('sha256').update(content).digest('hex') };
}

function contained(parent: string, child: string): boolean {
  const relative = path.relative(path.resolve(parent), path.resolve(child));
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

async function requireFile(file: string): Promise<void> { await access(file); }

async function collectFiles(root: string, directory = root): Promise<GenerationManifest['files']> {
  const files: GenerationManifest['files'] = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const target = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...await collectFiles(root, target));
    else if (entry.isFile() && entry.name !== 'run-manifest.json') {
      files.push({ path: path.relative(root, target).replaceAll('\\', '/'), ...await fingerprint(target) });
    } else if (!entry.isFile()) throw new Error(`Unsupported package entry: ${target}`);
  }
  return files.sort((a, b) => a.path.localeCompare(b.path, 'en'));
}

/** Generation never writes to the game installation or pristine source. */
export async function generateRun(
  request: GenerationRequest,
  onEvent: (event: GenerationEvent) => void = () => {},
  dependencies: Partial<GenerationDependencies> = {},
): Promise<GenerationResult> {
  const services = { ...defaults, ...dependencies };
  validateRandomizerConfig(request.config);
  const config = request.config;
  const workspace = typeof request.workspacePath === 'string' && request.workspacePath
    ? path.resolve(request.workspacePath) : '';
  const manifest: GenerationManifest = {
    schemaVersion: 1, appVersion: '0.27.0', game: 'DSR', masterSeed: config.masterSeed,
    pristineSnapshotId: null, gameTreeRoot: '',
    options: { items: config.items, enemies: config.enemies, fog: config.fog, output: config.output },
    enemyPlacement: {
      algorithm: config.enemies.placement.algorithm,
      rngVersion: config.enemies.placement.algorithm === 'sampling-v1' ? 'enemy-placement-v1' : null,
      classTableHash: null, summary: null,
    },
    attempts: [], engines: {}, validation: [], files: [],
  };
  const fail = (stage: string, code: string, message: string): GenerationResult => {
    onEvent({ type: 'log', stage, level: 'error', message });
    return { ok: false, stage, code, message, manifest };
  };
  const progress = (stage: string, value: number, message: string): void =>
    onEvent({ type: 'progress', stage, progress: value, message });

  if (!workspace || contained(config.gamePath, workspace)) {
    return fail('verify', 'WORKSPACE_UNSAFE', 'Choose a workspace outside the game installation.');
  }
  const installation = await validateInstallation(config.gamePath);
  if (!installation.valid) return fail('verify', 'GAME_INVALID', installation.problems.join(' '));
  progress('verify', 0.05, 'DSR installation verified.');

  const pristine = await services.getPristineSource(config.gamePath);
  if (!pristine) return fail('import', 'PRISTINE_IMPORT_REQUIRED',
    'Import and verify a clean DSR snapshot with the backend pristine-data authority before generation.');
  if (contained(config.gamePath, pristine.directory) || contained(pristine.directory, config.gamePath)
    || contained(pristine.directory, workspace) || contained(workspace, pristine.directory)) {
    return fail('import', 'PRISTINE_SOURCE_UNSAFE', 'The verified pristine source, game installation and workspace must be separate.');
  }
  const cleanInstallation = await validateInstallation(pristine.directory);
  if (!cleanInstallation.valid) return fail('import', 'PRISTINE_SOURCE_INVALID', cleanInstallation.problems.join(' '));
  manifest.pristineSnapshotId = pristine.snapshotId ?? null;
  progress('import', 0.1, 'Verified pristine source available.');

  const fogReady = config.fog.enabled ? await services.getFogPrerequisites(config.fog.language) : null;
  if (config.fog.enabled && !fogReady) return fail('fog', 'FOG_PREREQUISITES_MISSING',
    `Fog generation needs FogHost and a user-provided FogMod data root. Select the folder containing `
    + '`dist/DS1R` for FogMod commit 6e6dc78 in the desktop app (or set '
    + `${fogDataRootVariable}); FogHost verifies that data on every run.`);
  if (fogReady) {
    try { await requireFile(fogReady.executablePath); await access(fogReady.dataRoot); }
    catch { return fail('fog', 'FOG_PREREQUISITES_MISSING', 'FogHost executable or FogMod data root is unavailable.'); }
  }

  const combinedEnabled = effectiveCombinedEnabled(config);
  let combinedAttempt = 0;
  let fogAttempt = 0;
  while (combinedAttempt < defaultFogRetryBounds.maxCombinedAttempts) {
    const seeds = deriveAttemptSeeds(config.masterSeed, combinedAttempt, 0);
    const attemptWorkspace = path.join(workspace, `combined-${combinedAttempt}`);
    let combined: CombinedGenerationResult | null = null;
    if (combinedEnabled) {
      progress('combined', 0.15, 'Generating items and enemies.');
      combined = await services.combined({
        gamePath: pristine.directory, workspacePath: attemptWorkspace,
        masterSeed: config.masterSeed, combinedSeed: seeds.combined,
        items: config.items, enemies: config.enemies,
        generateSpoilerLog: config.output.generateSpoilerLog,
        placementDiagnosticsLevel: process.env[placementDiagnosticsVariable] === 'detail' ? 'detail' : 'summary',
      }, (event) => {
        if (event.type === 'log') onEvent({ ...event, stage: 'combined' });
        else progress('combined', 0.15 + event.progress * 0.4, event.message ?? 'Combined generation.');
      });
      if (!combined.ok) {
        manifest.attempts.push({ combinedAttempt, fogAttempt: 0, combinedSeed: seeds.combined, fogSeed: seeds.fog, result: combined.code });
        if (combined.retryable && combinedAttempt + 1 < defaultFogRetryBounds.maxCombinedAttempts) {
          combinedAttempt++;
          continue;
        }
        return fail('combined', combined.code, combined.message);
      }
      manifest.engines.combined = combined.randomizerVersion;
      manifest.enemyPlacement.classTableHash = combined.placementDiagnostics?.classTableHash ?? null;
      manifest.enemyPlacement.summary = combined.placementDiagnostics?.summary ?? null;
    }
    const packagePath = combined?.ok ? combined.seedDirectory : path.join(attemptWorkspace, 'fog-only');
    if (combined?.ok) {
      try {
        const patch = JSON.parse(await readFile(combined.patchManifestPath, 'utf8')) as unknown;
        const placements = JSON.parse(await readFile(combined.placementsPath, 'utf8')) as unknown;
        const base = path.join(workspace, `fog-${combinedAttempt}-0`, 'base');
        const bridge = buildFogCompatibility(patch, placements, base);
        if (!bridge.ok) return fail('validation', bridge.code, bridge.problems.join(' '));
        for (const file of bridge.value.combinedFiles) {
          const output = path.join(packagePath, 'mod', file.path);
          const actual = await fingerprint(output);
          if (actual.sha256 !== file.combinedSha256) {
            return fail('validation', 'COMBINED_OUTPUT_MISMATCH', `Combined output hash mismatch: ${file.path}`);
          }
        }
        manifest.validation.push('Combined shared output hashes verified.');
      } catch (error) {
        return fail('validation', 'COMBINED_OUTPUT_INVALID', `Cannot validate combined package: ${String(error)}`);
      }
    }
    progress('validation', 0.6, 'Combined package validated.');

    if (!config.fog.enabled) {
      manifest.attempts.push({ combinedAttempt, fogAttempt: 0, combinedSeed: seeds.combined, fogSeed: seeds.fog, result: 'success' });
      // Combined-only packages mirror the game root under mod/.
      manifest.gameTreeRoot = combined?.ok ? 'mod' : '';
      return finish(packagePath, manifest, progress, fail);
    }
    let retryCombined = false;
    for (fogAttempt = 0; fogAttempt < defaultFogRetryBounds.maxFogAttempts; fogAttempt++) {
      const attemptSeeds = deriveAttemptSeeds(config.masterSeed, combinedAttempt, fogAttempt);
      const attemptDirectory = path.join(workspace, `fog-${combinedAttempt}-${fogAttempt}`);
      let compatibility;
      try {
        const patch = combined?.ok ? JSON.parse(await readFile(combined.patchManifestPath, 'utf8')) as unknown
          : { schemaVersion: 2, patchedMaps: [], events: [], gameParam: null };
        const placements = combined?.ok ? JSON.parse(await readFile(combined.placementsPath, 'utf8')) as unknown : {};
        const bridge = buildFogCompatibility(patch, placements, path.join(attemptDirectory, 'base'));
        if (!bridge.ok) return fail('fog', bridge.code, bridge.problems.join(' '));
        compatibility = bridge.value;
      } catch (error) { return fail('fog', 'FOG_BRIDGE_INPUT_INVALID', String(error)); }
      const staging = planFogStaging({
        pristineDirectory: pristine.directory, packageDirectory: packagePath,
        attemptDirectory, readSet: fogReady!.readSet, combinedFiles: compatibility.combinedFiles,
      });
      if (!staging.ok) return fail('fog', staging.code, staging.problems.join(' '));
      try {
        if (combined?.ok) {
          // Preserve combined outputs FogMod does not read or rewrite.
          await cp(path.join(packagePath, 'mod'), staging.value.stagingDirectory, { recursive: true });
        }
        for (const directory of staging.value.directories) await mkdir(directory, { recursive: true });
        for (const file of staging.value.copies) {
          await mkdir(path.dirname(file.to), { recursive: true });
          await copyFile(file.from, file.to);
        }
        for (const file of compatibility.combinedFiles) {
          const actual = await fingerprint(path.join(staging.value.baseDirectory, file.path));
          if (actual.sha256 !== file.baseSha256) throw new Error(`Pristine merge base hash mismatch: ${file.path}`);
        }
      } catch (error) { return fail('fog', 'FOG_STAGING_FAILED', String(error)); }
      progress('fog', 0.7, 'Generating fog layout.');
      const { enabled: _enabled, language, ...options } = config.fog;
      const response: FogHostResponse = await services.fog.execute({
        executablePath: fogReady!.executablePath,
        requestPath: staging.value.requestPath, responsePath: staging.value.responsePath,
        timeoutMs: 300_000,
        request: { schemaVersion: 2, game: 'DS1R', mode: 'write', seed: attemptSeeds.fog,
          options, language, fogDataRoot: fogReady!.dataRoot,
          stagingDirectory: staging.value.stagingDirectory, logPath: staging.value.logPath,
          compatibility },
      }, (event) => {
        if (event.type === 'log') onEvent({ type: 'log', stage: 'fog', level: event.stream === 'stderr' ? 'warning' : 'info', message: event.message });
        else progress('fog', 0.75, event.message);
      });
      if (!response.ok) {
        manifest.attempts.push({ combinedAttempt, fogAttempt, combinedSeed: attemptSeeds.combined,
          fogSeed: attemptSeeds.fog, result: response.error.code });
        const decision = decideFogRetry(response.error, { combinedAttempt, fogAttempt });
        if (decision.action === 'retry-fog') continue;
        if (decision.action === 'retry-combined') { retryCombined = true; break; }
        return fail('fog', response.error.code, decision.reason);
      }
      manifest.engines.fog = `${response.hostVersion} / ${response.fogModCommit}`;
      try {
        for (const file of response.files) {
          const actual = await fingerprint(path.join(staging.value.stagingDirectory, file.path));
          if (actual.size !== file.size || actual.sha256 !== file.sha256) {
            return fail('validation', 'FOG_OUTPUT_MISMATCH', `Fog output hash mismatch: ${file.path}`);
          }
        }
        manifest.validation.push('Fog output hashes verified.');
        manifest.attempts.push({ combinedAttempt, fogAttempt, combinedSeed: attemptSeeds.combined,
          fogSeed: attemptSeeds.fog, result: 'success' });
        // The staging game tree is the final generated package. Activation is separate.
        return finish(staging.value.stagingDirectory, manifest, progress, fail);
      } catch (error) { return fail('validation', 'FOG_OUTPUT_INVALID', String(error)); }
    }
    if (!retryCombined) return fail('fog', 'FOG_RETRIES_EXHAUSTED', 'Fog retries exhausted.');
    combinedAttempt++;
  }
  return fail('combined', 'COMBINED_RETRIES_EXHAUSTED', 'Combined retries exhausted.');
}

async function finish(
  packagePath: string,
  manifest: GenerationManifest,
  progress: (stage: string, value: number, message: string) => void,
  fail: (stage: string, code: string, message: string) => GenerationResult,
): Promise<GenerationResult> {
  const manifestPath = path.join(packagePath, 'run-manifest.json');
  try {
    await mkdir(packagePath, { recursive: true });
    manifest.files = await collectFiles(packagePath);
    await writeFile(manifestPath, JSON.stringify(manifest, null, 2), { flag: 'wx' });
  } catch (error) { return fail('package', 'MANIFEST_WRITE_FAILED', String(error)); }
  progress('package', 1, 'Generated package ready.');
  return { ok: true, packagePath, manifestPath, manifest };
}
