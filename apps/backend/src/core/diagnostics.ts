import { createHash } from 'node:crypto';
import path from 'node:path';
import type { Diagnostics, GameStateReport, InstallMode, OperationRecord, OperationSummary, RunHistoryEntry, SigningState } from '@ds1tr/contracts';
import { DIAGNOSTICS_SCHEMA_VERSION } from '@ds1tr/contracts';
import type { OperationStore } from './operations.js';

/**
 * The diagnostics model (docs/DIAGNOSTICS.md): one structured, machine-readable description of the application, the game, the current world and the last
 * operations. It is assembled from data the application already holds (the run manifest the generation wrote, the operation records, the build information);
 * it never reads game files. The interface shows it as a friendly summary with expandable technical details; the support bundle carries it as
 * `diagnostics.json`. Field names and enum values are stable and never translated. No folder of the person appears here: the game is
 * described by its state, not its path.
 */
/** `build-info.json`, written next to the packaged runtime by the packaging script. */
export interface BuildInfo {
  schema: 'ds1tr-build-info-1';
  version: string;
  commit: string | null;
  mode: 'development' | 'public';
  versions: { desktop: string; backend: string; contracts: string; foghostProject: string | null };
  builtAt: string;
}

export interface ManifestLike {
  appVersion?: string;
  generationProfile?: string;
  seedCode?: string;
  masterSeed?: string;
  pristineSnapshotId?: string;
  attempts?: Array<{ combinedAttempt: number; fogAttempt: number; result: string }>;
  engines?: { combined?: string; fog?: string };
  validation?: string[];
  enemyPlacement?: { algorithm?: string; summary?: unknown; validation?: unknown; classTableHash?: string | null } | null;
  keyPlacement?: { mode?: string; effectiveMode?: string; summary?: unknown } | null;
  files?: Array<{ path: string; size: number; sha256: string }>;
}

export interface DiagnosticsInput {
  now: Date;
  versions: { desktop: string | null; backend: string; contracts: string | null };
  profile: { id: string; combinedRandomizerVersions: readonly string[]; fogHostVersions: readonly string[]; fogModCommit: string; worldDataId: string };
  build: BuildInfo | null;
  installMode: InstallMode;
  /** The interface language at the time: a diagnostic fact, never an input of generation. */
  language: string | null;
  platform: { os: string; arch: string; node: string };
  game: { pathConfigured: boolean; state: GameStateReport['state'] | null; pristineSnapshot: boolean };
  operations: OperationStore;
  /** The world to describe: the applied one, else the newest. */
  world: { entry: RunHistoryEntry; manifest: ManifestLike | null; runRoot: string } | null;
  historyCount: number;
}

const MAX_JSON = 24 * 1024;

/** Keeps a structure whose serialization would be large down to its top level (counts instead of contents). */
function bounded(value: unknown): unknown {
  if (value === null || value === undefined) return null;
  const text = JSON.stringify(value);
  if (text.length <= MAX_JSON) return value;
  if (Array.isArray(value)) return { truncated: true, items: value.length };
  if (typeof value === 'object') return Object.fromEntries(Object.entries(value as Record<string, unknown>).map(([key, child]) => [key, JSON.stringify(child).length <= 2048 ? child : { truncated: true }]));
  return { truncated: true };
}

const runIdOf = (file: string | null, runRoot: string): string | null => {
  if (!file) return null;
  const relative = path.relative(runRoot, file);
  if (relative.startsWith('..') || path.isAbsolute(relative)) return null;
  return relative.split(path.sep)[0] || null;
};

function summarize(record: OperationRecord | undefined, runRoot: string): OperationSummary | null {
  if (!record) return null;
  const { workspacePath, packagePath: _package, ...rest } = record;
  return { ...rest, runId: runIdOf(workspacePath, runRoot) };
}

export function signingStateOf(build: BuildInfo | null, mode: InstallMode): SigningState {
  if (!build || mode === 'development') return mode === 'development' ? 'unsigned' : 'unknown';
  // A public build is only produced after the release verification required a trusted signature on every executable and artifact (tools/release).
  return build.mode === 'public' ? 'signed' : 'unsigned';
}

export function buildDiagnostics(input: DiagnosticsInput): Diagnostics {
  const runRoot = input.world?.runRoot ?? '';
  const manifest = input.world?.manifest ?? null;
  const entry = input.world?.entry ?? null;
  const attempts = manifest?.attempts ?? [];
  const files = manifest?.files ?? [];
  const failedCombined = attempts.reduce((highest, attempt) => Math.max(highest, attempt.combinedAttempt), 0);
  const fogRetries = attempts.filter((attempt) => attempt.result.startsWith('FOG_')).length;
  const operations = input.operations;
  const fogEngine = manifest?.engines?.fog ?? null;
  return {
    format: 'ds1tr-diagnostics',
    schemaVersion: DIAGNOSTICS_SCHEMA_VERSION,
    generatedAt: input.now.toISOString(),
    application: {
      desktopVersion: input.versions.desktop,
      backendVersion: input.versions.backend,
      contractsVersion: input.versions.contracts,
      generationProfile: input.profile.id,
      engines: {
        combinedRandomizer: manifest?.engines?.combined ?? input.profile.combinedRandomizerVersions[0] ?? null,
        foghostProject: input.build?.versions.foghostProject ?? null,
        foghostHost: fogEngine ? fogEngine.split(' / ')[0] : input.profile.fogHostVersions[0] ?? null,
        fogModCommit: input.profile.fogModCommit,
        worldDataId: input.profile.worldDataId,
      },
      commit: input.build?.commit ?? null,
      installMode: input.installMode,
      language: input.language,
      platform: input.platform,
    },
    game: {
      pathConfigured: input.game.pathConfigured,
      state: input.game.state,
      pristineSnapshot: input.game.pristineSnapshot,
      applied: input.game.state === null ? null : input.game.state === 'active',
      restored: input.game.state === null ? null : input.game.state === 'vanilla',
    },
    world: entry === null ? null : {
      seedCode: entry.seedCode,
      masterSeed: entry.masterSeed,
      generationProfile: entry.profile,
      status: entry.status,
      createdAt: entry.createdAt,
      runId: runIdOf(entry.packagePath, runRoot),
      pristineSnapshotId: manifest?.pristineSnapshotId ?? null,
      appVersionAtGeneration: manifest?.appVersion ?? null,
      settings: entry.settings,
      package: {
        fileCount: files.length,
        totalBytes: files.reduce((sum, file) => sum + (file.size ?? 0), 0),
        fileListSha256: createHash('sha256').update(files.map((file) => `${file.path}:${file.sha256}`).join('\n')).digest('hex'),
        manifestAvailable: manifest !== null,
      },
      placementValidation: bounded(manifest?.enemyPlacement?.validation ?? null),
      keyPlacement: manifest?.keyPlacement ? { mode: manifest.keyPlacement.mode ?? null, effectiveMode: manifest.keyPlacement.effectiveMode ?? null, summary: bounded(manifest.keyPlacement.summary) } : null,
      fog: { enabled: entry.settings.fog.enabled, attempts: attempts.length, retries: { combined: failedCombined, fog: fogRetries }, engine: fogEngine },
      attempts: attempts.map(({ combinedAttempt, fogAttempt, result }) => ({ combinedAttempt, fogAttempt, result })),
      enemyPlacementSummary: bounded(manifest?.enemyPlacement?.summary ?? null),
      validation: (manifest?.validation ?? []).slice(0, 50),
      hintsAvailable: entry.hintsAvailable ?? null,
    },
    generation: {
      lastGenerate: summarize(operations.generate, runRoot),
      lastApply: summarize(operations.apply, runRoot),
      lastRestore: summarize(operations.restore, runRoot),
      lastFailure: summarize(operations.lastFailure, runRoot),
    },
    release: {
      signing: signingStateOf(input.build, input.installMode),
      artifactVersion: input.build?.version ?? input.versions.desktop,
      mode: input.build?.mode ?? null,
      builtAt: input.build?.builtAt ?? null,
    },
    history: { entries: input.historyCount },
  };
}

/** Which world to describe: the applied one, else the newest. */
export function pickWorld(entries: readonly RunHistoryEntry[]): RunHistoryEntry | null {
  return entries.find((entry) => entry.status === 'applied') ?? entries[0] ?? null;
}
