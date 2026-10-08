import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import os from 'node:os';
import path from 'node:path';
import type { GameStateReport, RunHistoryEntry } from '@ds1tr/contracts';
import type { Diagnostics, InstallMode } from '@ds1tr/contracts';
import { type BuildInfo, type ManifestLike, buildDiagnostics, pickWorld, signingStateOf } from './diagnostics.js';
import { currentGenerationProfile } from './generationProfiles.js';
import { readLogTail } from './appLog.js';
import { type OperationStore, readOperationStore } from './operations.js';
import { type RedactionContext, redactionContext } from './redaction.js';
import { type BundleResult, type BundleSource, BUNDLE_LIMITS, buildSupportBundle, readBundleFile } from './supportBundle.js';

/**
 * Gathers what the diagnostics view and the support bundle need from the places the application already keeps it (docs/DIAGNOSTICS.md, docs/SUPPORT_BUNDLE.md).
 * Nothing here reads game files or the generated package: only the run manifest, the operation records, the logs and the build information.
 */
export interface SupportDependencies {
  stateRoot: string;
  runRoot: string;
  /** The folder of the desktop's user data (for path redaction) and the desktop's own log file, when the desktop runs the backend. */
  userDataDir: string | null;
  appLogPath: string | null;
  backendLogPath: string;
  runtimeRoot: string | null;
  appVersion: string;
  env: NodeJS.ProcessEnv;
  listHistory: () => Promise<RunHistoryEntry[]>;
  detectState: (gamePath: string) => Promise<GameStateReport>;
  now?: () => Date;
}

/** `worldId`: a history entry to describe ("Diagnostics" of a world in the history); without it the applied world, else the newest. */
export interface SupportRequest { gamePath?: string; language?: string; worldId?: string }

const chooseEntry = (entries: readonly RunHistoryEntry[], worldId: string | undefined): RunHistoryEntry | null =>
  (worldId ? entries.find((candidate) => candidate.id === worldId) : undefined) ?? pickWorld(entries);

export function installModeOf(env: NodeJS.ProcessEnv): InstallMode {
  const declared = env.DS1TR_INSTALL_MODE;
  if (declared === 'installed' || declared === 'portable' || declared === 'development') return declared;
  return env.PORTABLE_EXECUTABLE_DIR ? 'portable' : 'unknown';
}

export function loadBuildInfo(runtimeRoot: string | null): BuildInfo | null {
  if (!runtimeRoot) return null;
  try {
    const parsed = JSON.parse(readFileSync(path.join(runtimeRoot, 'build-info.json'), 'utf8')) as BuildInfo;
    return parsed.schema === 'ds1tr-build-info-1' ? parsed : null;
  } catch { return null; }
}

function contractsVersion(): string | null {
  try { return (JSON.parse(readFileSync(createRequire(import.meta.url).resolve('@ds1tr/contracts/package.json'), 'utf8')) as { version?: string }).version ?? null; } catch { return null; }
}

function readManifest(file: string | null | undefined): ManifestLike | null {
  const text = readBundleFile(file, 1024 * 1024);
  if (!text) return null;
  try { return JSON.parse(text) as ManifestLike; } catch { return null; }
}

const contained = (root: string, candidate: string | null | undefined): candidate is string => {
  if (!candidate) return false;
  const relative = path.relative(path.resolve(root), path.resolve(candidate));
  return relative !== '' && !relative.startsWith('..') && !path.isAbsolute(relative);
};

/** The folders and secrets of this installation, for the redactor. */
export function supportRedaction(deps: SupportDependencies, gamePath: string | undefined): RedactionContext {
  const directories = [] as RedactionContext['directories'];
  if (gamePath) directories.push({ path: gamePath, label: '<GAME_DIR>' });
  if (deps.userDataDir) directories.push({ path: deps.userDataDir, label: '<USER_DATA>' });
  directories.push({ path: deps.stateRoot, label: '<USER_DATA>\\state' });
  if (deps.runtimeRoot) directories.push({ path: deps.runtimeRoot, label: '<APP_RUNTIME>' });
  const token = deps.env.DS1TR_API_TOKEN;
  return redactionContext({ directories, secrets: token ? [token] : [], env: deps.env });
}

export async function gatherDiagnostics(deps: SupportDependencies, request: SupportRequest): Promise<Diagnostics> {
  const profile = currentGenerationProfile();
  const entries = await deps.listHistory();
  const entry = chooseEntry(entries, request.worldId);
  const operations = readOperationStore(path.join(deps.stateRoot, 'diagnostics', 'last-run.json'));
  let state: GameStateReport | null = null;
  if (request.gamePath) { try { state = await deps.detectState(request.gamePath); } catch { state = null; } }
  const build = loadBuildInfo(deps.runtimeRoot);
  return buildDiagnostics({
    now: (deps.now ?? (() => new Date()))(),
    versions: { desktop: deps.env.DS1TR_DESKTOP_VERSION ?? build?.versions.desktop ?? null, backend: deps.appVersion, contracts: contractsVersion() },
    profile: { id: profile.id, combinedRandomizerVersions: profile.combinedEngine.randomizerVersions, fogHostVersions: profile.fogEngine.hostVersions, fogModCommit: profile.fogEngine.fogModCommit, worldDataId: profile.fogEngine.worldDataId },
    build, installMode: installModeOf(deps.env), language: request.language ?? null,
    platform: { os: `${os.platform()} ${os.release()}`, arch: os.arch(), node: process.version },
    game: { pathConfigured: Boolean(request.gamePath), state: state?.state ?? null, pristineSnapshot: Boolean(state?.snapshotId) },
    operations,
    world: entry ? { entry, manifest: readManifest(entry.manifestPath), runRoot: deps.runRoot } : null,
    historyCount: entries.length,
  });
}

/** The manifest the bundle carries: that of the last failed generation when it is newer than the newest world, else the world's own. */
function chooseManifest(operations: OperationStore, entry: RunHistoryEntry | null, runRoot: string): { manifest: ManifestLike | null; source: 'failed-generation' | 'world' | 'none' } {
  const failure = operations.lastFailure;
  if (failure?.kind === 'generate' && contained(runRoot, failure.workspacePath) && (!entry || failure.startedAt > entry.createdAt)) {
    const failed = readManifest(path.join(failure.workspacePath, 'failed-manifest.json'));
    if (failed) return { manifest: failed, source: 'failed-generation' };
  }
  if (entry) { const world = readManifest(entry.manifestPath); if (world) return { manifest: world, source: 'world' }; }
  return { manifest: null, source: 'none' };
}

export async function assembleSupportBundle(deps: SupportDependencies, request: SupportRequest & { summaryText: string; readmeText: string }): Promise<BundleResult> {
  const now = (deps.now ?? (() => new Date()))();
  const diagnostics = await gatherDiagnostics(deps, request);
  const operations = readOperationStore(path.join(deps.stateRoot, 'diagnostics', 'last-run.json'));
  const entries = await deps.listHistory();
  const entry = chooseEntry(entries, request.worldId);
  const { manifest, source } = chooseManifest(operations, entry, deps.runRoot);
  const build = loadBuildInfo(deps.runtimeRoot);
  const generateWorkspace = operations.generate?.workspacePath;
  const generationLog = contained(deps.runRoot, generateWorkspace) ? readLogTail(path.join(generateWorkspace, 'generation.log'), BUNDLE_LIMITS['logs/relevant-generation.log']) : '';
  const sources: BundleSource[] = [
    { name: 'README.txt', content: request.readmeText.slice(0, BUNDLE_LIMITS['README.txt']) },
    { name: 'summary.txt', content: request.summaryText.slice(0, BUNDLE_LIMITS['summary.txt']) },
    { name: 'diagnostics.json', content: diagnostics },
    { name: 'generation/run-manifest.json', content: manifest },
    {
      name: 'generation/generation-summary.json',
      content: {
        schemaVersion: 1, manifestSource: source, operations: {
          generate: operations.generate ?? null, apply: operations.apply ?? null, restore: operations.restore ?? null, lastFailure: operations.lastFailure ?? null,
        },
      },
    },
    {
      name: 'generation/validation-summary.json',
      content: {
        schemaVersion: 1,
        reasonCodes: [...new Set([operations.lastFailure?.code, operations.generate?.code, operations.apply?.code, operations.restore?.code].filter((code): code is string => typeof code === 'string'))],
        validation: manifest?.validation ?? [],
        attempts: manifest?.attempts ?? [],
        enemyPlacement: manifest?.enemyPlacement?.validation ?? null,
        keyPlacement: manifest?.keyPlacement?.summary ?? null,
        errors: operations.generate?.errors ?? [], warnings: operations.generate?.warnings ?? [],
      },
    },
    { name: 'logs/app.log', content: deps.appLogPath ? readLogTail(deps.appLogPath, BUNDLE_LIMITS['logs/app.log']) || null : null },
    { name: 'logs/backend.log', content: readLogTail(deps.backendLogPath, BUNDLE_LIMITS['logs/backend.log']) || null },
    { name: 'logs/relevant-generation.log', content: generationLog || null },
    {
      name: 'release/release-manifest.json',
      content: build ? { schema: 'ds1tr-release-info-1', build, signing: signingStateOf(build, installModeOf(deps.env)), installMode: installModeOf(deps.env) } : null,
    },
  ];
  return buildSupportBundle(sources, supportRedaction(deps, request.gamePath), { seedCode: entry?.seedCode ?? null, now });
}
