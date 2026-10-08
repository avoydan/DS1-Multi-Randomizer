import type { GenerationSettings, RunHistoryEntry } from './generation.js';
import type { GameStateReport } from './index.js';
import { summarizeWorld, type Translate } from './hintSheet.js';

/**
 * Diagnostics (docs/DIAGNOSTICS.md): the structured description of the application, the game, the current world and the last operations, shared by the
 * backend (which assembles it), the interface (which shows it) and the support bundle (which carries it as JSON). Field names and enum values are stable and
 * never translated; the texts a person reads are built here from an injected `translate`, with the interface's own resources.
 */
export const DIAGNOSTICS_SCHEMA_VERSION = 1;

export const LOG_STAGES = [
  'GAME_DETECTION', 'SNAPSHOT', 'ITEM_PLAN', 'ENEMY_PLAN', 'FOG_PLAN', 'DATA_TOOL', 'MERGE', 'PACKAGE', 'APPLY', 'RESTORE',
  // Not generation stages: the request itself, validation of a Seed Code or settings, the server and the interface.
  'SEED', 'VALIDATION', 'SERVER', 'APP',
] as const;
export type LogStage = (typeof LOG_STAGES)[number];

export type OperationKind = 'generate' | 'apply' | 'restore';
export type InstallMode = 'installed' | 'portable' | 'development' | 'unknown';
export type SigningState = 'signed' | 'unsigned' | 'unknown';

export interface OperationRecord {
  correlationId: string;
  kind: OperationKind;
  startedAt: string;
  finishedAt: string | null;
  ok: boolean | null;
  /** The structured result code of a failure (`FOG_RETRIES_EXHAUSTED`, `GAME_RUNNING`, ...). */
  code: string | null;
  /** The stage that failed, or null on success. */
  stage: LogStage | null;
  seedCode: string | null;
  profile: string | null;
  historyId: string | null;
  workspacePath: string | null;
  packagePath: string | null;
  /** Milliseconds spent in each stage. */
  timings: Partial<Record<LogStage, number>>;
  counts: { info: number; warning: number; error: number; events: number };
  windows: Partial<Record<LogStage, { first: string; last: string; events: number }>>;
  warnings: string[];
  errors: string[];
}

export type OperationSummary = Omit<OperationRecord, 'workspacePath' | 'packagePath'> & { runId: string | null };

export interface Diagnostics {
  format: 'ds1tr-diagnostics';
  schemaVersion: number;
  generatedAt: string;
  application: {
    desktopVersion: string | null;
    backendVersion: string;
    contractsVersion: string | null;
    generationProfile: string;
    engines: { combinedRandomizer: string | null; foghostProject: string | null; foghostHost: string | null; fogModCommit: string; worldDataId: string };
    commit: string | null;
    installMode: InstallMode;
    language: string | null;
    platform: { os: string; arch: string; node: string };
  };
  game: { pathConfigured: boolean; state: GameStateReport['state'] | null; pristineSnapshot: boolean; applied: boolean | null; restored: boolean | null };
  world: null | {
    seedCode: string;
    masterSeed: string;
    generationProfile: string;
    status: RunHistoryEntry['status'];
    createdAt: string;
    runId: string | null;
    pristineSnapshotId: string | null;
    appVersionAtGeneration: string | null;
    settings: GenerationSettings;
    package: { fileCount: number; totalBytes: number; fileListSha256: string; manifestAvailable: boolean };
    placementValidation: unknown;
    keyPlacement: { mode: string | null; effectiveMode: string | null; summary: unknown } | null;
    fog: { enabled: boolean; attempts: number; retries: { combined: number; fog: number }; engine: string | null };
    attempts: Array<{ combinedAttempt: number; fogAttempt: number; result: string }>;
    enemyPlacementSummary: unknown;
    validation: string[];
    hintsAvailable: boolean | null;
  };
  generation: {
    lastGenerate: OperationSummary | null;
    lastApply: OperationSummary | null;
    lastRestore: OperationSummary | null;
    lastFailure: OperationSummary | null;
  };
  release: { signing: SigningState; artifactVersion: string | null; mode: string | null; builtAt: string | null };
  history: { entries: number };
}

// --- texts -------------------------------------------------------------------------------------------------------------------------------------------

const line = (label: string, value: string | number | null | undefined): string => `${label}: ${value === null || value === undefined || value === '' ? '-' : value}`;
const seconds = (milliseconds: number): string => `${(milliseconds / 1000).toFixed(1)} s`;

export interface DiagnosticsTextOptions {
  /** Formats an ISO time for the reader (the interface passes its locale formatter; the default is the ISO text). */
  formatTime?: (iso: string) => string;
}

function operationLines(t: Translate, label: string, operation: OperationSummary | null, options: DiagnosticsTextOptions): string[] {
  if (!operation) return [line(label, t('diagnostics.operation.none'))];
  const when = operation.finishedAt ?? operation.startedAt;
  const result = operation.ok === null ? t('diagnostics.operation.running') : operation.ok ? t('diagnostics.operation.ok') : t('diagnostics.operation.failed');
  const out = [line(label, `${result} (${(options.formatTime ?? ((iso) => iso))(when)})`)];
  if (operation.ok === false) {
    out.push(`  ${line(t('diagnostics.field.code'), operation.code)}`);
    out.push(`  ${line(t('diagnostics.field.stage'), operation.stage)}`);
  }
  out.push(`  ${line(t('diagnostics.field.operationId'), operation.correlationId)}`);
  return out;
}

/**
 * The plain-text diagnostics: a friendly summary first, then the technical details. Labels are in the reader's language; codes, versions, ids and hashes
 * are printed as they are. It contains no folder of the person (the model has none) and is what "Copy diagnostics" puts on the clipboard.
 */
export function buildDiagnosticsText(diagnostics: Diagnostics, t: Translate, options: DiagnosticsTextOptions = {}): string {
  const formatTime = options.formatTime ?? ((iso: string) => iso);
  const out: string[] = [];
  const app = diagnostics.application;
  out.push(t('diagnostics.title'), '='.repeat(t('diagnostics.title').length), '');
  out.push(t('diagnostics.section.summary'), '-'.repeat(t('diagnostics.section.summary').length));
  out.push(line(t('diagnostics.field.application'), `DS1 Multi Randomizer ${app.desktopVersion ?? app.backendVersion}`));
  out.push(line(t('diagnostics.field.installMode'), t(`diagnostics.installMode.${app.installMode}`)));
  out.push(line(t('diagnostics.field.signing'), t(`diagnostics.signing.${diagnostics.release.signing}`)));
  out.push(line(t('diagnostics.field.game'), diagnostics.game.state ? t(`diagnostics.gameState.${diagnostics.game.state}`) : t('diagnostics.gameState.unknown')));
  out.push(line(t('diagnostics.field.snapshot'), t(diagnostics.game.pristineSnapshot ? 'diagnostics.yes' : 'diagnostics.no')));
  const world = diagnostics.world;
  if (world) {
    out.push(line(t('diagnostics.field.seedCode'), world.seedCode));
    out.push(line(t('diagnostics.field.worldStatus'), t(`diagnostics.worldStatus.${world.status}`)));
    out.push(line(t('diagnostics.field.created'), formatTime(world.createdAt)));
    for (const entry of summarizeWorld(world.settings)) {
      out.push(`  ${line(t(`hints.summary.${entry.id}`), typeof entry.value === 'boolean' ? t(entry.value ? 'hints.on' : 'hints.off') : entry.optionPath ? t(`settings.${entry.optionPath}.options.${entry.value}`) : entry.value)}`);
    }
  } else out.push(line(t('diagnostics.field.world'), t('diagnostics.noWorld')));
  out.push(...operationLines(t, t('diagnostics.field.lastGeneration'), diagnostics.generation.lastGenerate, options));
  out.push(...operationLines(t, t('diagnostics.field.lastApply'), diagnostics.generation.lastApply, options));
  out.push(...operationLines(t, t('diagnostics.field.lastRestore'), diagnostics.generation.lastRestore, options));
  out.push('', t('diagnostics.section.technical'), '-'.repeat(t('diagnostics.section.technical').length));
  out.push(line(t('diagnostics.field.versions'), `desktop ${app.desktopVersion ?? '-'}, backend ${app.backendVersion}, contracts ${app.contractsVersion ?? '-'}`));
  out.push(line(t('diagnostics.field.profile'), app.generationProfile));
  out.push(line(t('diagnostics.field.engines'), `randomizer ${app.engines.combinedRandomizer ?? '-'}, FogHost ${app.engines.foghostProject ?? '-'} (host ${app.engines.foghostHost ?? '-'}), FogMod ${app.engines.fogModCommit.slice(0, 12)}`));
  out.push(line(t('diagnostics.field.commit'), app.commit));
  out.push(line(t('diagnostics.field.platform'), `${app.platform.os} ${app.platform.arch}, Node ${app.platform.node}`));
  if (world) {
    out.push(line(t('diagnostics.field.masterSeed'), world.masterSeed));
    out.push(line(t('diagnostics.field.runId'), world.runId));
    out.push(line(t('diagnostics.field.package'), `${world.package.fileCount} / ${world.package.totalBytes} B / ${world.package.fileListSha256}`));
    out.push(line(t('diagnostics.field.fog'), world.fog.enabled ? `${world.fog.attempts} attempt(s), retries combined ${world.fog.retries.combined}, fog ${world.fog.retries.fog}` : t('hints.off')));
  }
  const last = diagnostics.generation.lastFailure ?? diagnostics.generation.lastGenerate;
  if (last) {
    out.push(line(t('diagnostics.field.timings'), (Object.entries(last.timings) as Array<[string, number]>).map(([stage, ms]) => `${stage} ${seconds(ms)}`).join(', ') || '-'));
    out.push(line(t('diagnostics.field.events'), `${last.counts.events} (${last.counts.warning} warning, ${last.counts.error} error)`));
  }
  out.push('');
  return out.join('\n');
}

export interface SupportBundleTextInput { diagnostics: Diagnostics; language: 'en' | 'pl' }

/** `summary.txt` of a support bundle: the diagnostics text in the reader's language. */
export function buildSupportSummary(input: SupportBundleTextInput, t: Translate, options: DiagnosticsTextOptions = {}): string {
  return buildDiagnosticsText(input.diagnostics, t, options);
}

/** `README.txt` of a support bundle: what it is, what is in it, what is not, and what was done to protect the person. */
export function buildSupportReadme(t: Translate): string {
  return [
    t('support.readme.title'), '='.repeat(t('support.readme.title').length), '',
    t('support.readme.what'), '',
    t('support.readme.contents'),
    `  README.txt, summary.txt - ${t('support.readme.files.summary')}`,
    `  diagnostics.json - ${t('support.readme.files.diagnostics')}`,
    `  generation/ - ${t('support.readme.files.generation')}`,
    `  logs/ - ${t('support.readme.files.logs')}`,
    `  release/ - ${t('support.readme.files.release')}`, '',
    t('support.readme.never'), '',
    t('support.readme.redaction'), '',
    t('support.readme.reproduce'), '',
  ].join('\n');
}
