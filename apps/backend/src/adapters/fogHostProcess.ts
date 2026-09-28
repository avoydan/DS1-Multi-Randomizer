import { spawn } from 'node:child_process';
import type { ChildProcess, SpawnOptions } from 'node:child_process';
import { readFile, stat, writeFile } from 'node:fs/promises';
import type { FogLanguage, FogOptions } from '@ds1tr/contracts';

export type FogHostMode = 'dryRun' | 'write';
export type FogHostOptions = Omit<FogOptions, 'enabled' | 'language'>;

export interface FogHostRequest {
  schemaVersion: 1;
  game: 'DS1R';
  mode: FogHostMode;
  seed: number;
  options: FogHostOptions;
  language: FogLanguage;
  fogDataRoot: string;
  stagingDirectory: string;
  logPath: string;
  compatibility: Record<string, never>;
}

export type FogHostErrorCode =
  | 'FOG_REQUEST_INVALID' | 'FOG_DATA_MISSING' | 'FOG_STAGING_UNSAFE'
  | 'FOG_UPSTREAM_INCOMPATIBLE' | 'FOG_SEED_UNSOLVABLE'
  | 'FOG_IO_FAILED' | 'FOG_ENGINE_FAILED'
  | 'FOG_HOST_CRASHED' | 'FOG_HOST_TIMEOUT';

export interface FogHostError {
  code: FogHostErrorCode;
  stage?: string;
  retryable: boolean;
  retryScope: 'combined' | 'fog' | null;
  message: string;
}

export interface FogHostFile {
  path: string;
  size: number;
  sha256: string;
}

export type FogHostResponse =
  | {
    schemaVersion: 1;
    ok: true;
    hostVersion: string;
    fogModCommit: string;
    annotationSha256: string;
    mode: FogHostMode;
    seed: number;
    fogOptions: string;
    keyItems: { randomized: boolean; hash: string };
    files: FogHostFile[];
    error: null;
  }
  | { schemaVersion: 1; ok: false; error: FogHostError };

export interface FogHostExecutionRequest {
  executablePath: string;
  requestPath: string;
  responsePath: string;
  timeoutMs: number;
  request: FogHostRequest;
}

export type FogHostEvent =
  | { type: 'progress'; stage: string; message: string }
  | { type: 'log'; stream: 'stdout' | 'stderr'; message: string };

export type FogHostSpawn = (command: string, args: string[], options: SpawnOptions) => ChildProcess;

const hostErrors = new Set<FogHostErrorCode>([
  'FOG_REQUEST_INVALID', 'FOG_DATA_MISSING', 'FOG_STAGING_UNSAFE',
  'FOG_UPSTREAM_INCOMPATIBLE', 'FOG_SEED_UNSOLVABLE', 'FOG_IO_FAILED',
  'FOG_ENGINE_FAILED', 'FOG_HOST_CRASHED', 'FOG_HOST_TIMEOUT',
]);
const optionKeys = [
  'traversableFogs', 'bossFogs', 'areaWarps', 'majorPvpFogs', 'minorPvpFogs',
  'lordvesselGates', 'randomStart', 'disconnectedFogs', 'noFallBedOfChaos',
  'scaling', 'glitchedLogic', 'requireLordSouls', 'pacifistMode',
] as const;
const languages = new Set<FogLanguage>([
  'ENGLISH', 'FRENCH', 'GERMAN', 'ITALIAN', 'JAPANESE', 'KOREAN',
  'NSPANISH', 'POLISH', 'PORTUGUESE', 'RUSSIAN', 'SCHINESE', 'SPANISH', 'TCHINESE',
]);

function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function uint32(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 0xffffffff;
}

function parseResponse(value: unknown, request: FogHostRequest): FogHostResponse | null {
  if (!record(value) || value.schemaVersion !== 1 || typeof value.ok !== 'boolean') return null;
  if (!value.ok) {
    const error = value.error;
    if (!record(error) || !hostErrors.has(error.code as FogHostErrorCode)
      || typeof error.message !== 'string' || typeof error.retryable !== 'boolean'
      || ![null, 'combined', 'fog'].includes(error.retryScope as string | null)
      || (error.stage !== undefined && typeof error.stage !== 'string')) return null;
    return value as unknown as FogHostResponse;
  }
  if (typeof value.hostVersion !== 'string' || typeof value.fogModCommit !== 'string'
    || typeof value.annotationSha256 !== 'string' || value.mode !== request.mode
    || value.seed !== request.seed || typeof value.fogOptions !== 'string'
    || value.error !== null || !record(value.keyItems)
    || typeof value.keyItems.randomized !== 'boolean'
    || typeof value.keyItems.hash !== 'string' || !Array.isArray(value.files)
    || !value.files.every((file: unknown) => record(file)
      && typeof file.path === 'string' && !file.path.startsWith('/')
      && !file.path.includes('\\') && !file.path.split('/').includes('..')
      && typeof file.size === 'number' && Number.isSafeInteger(file.size) && file.size >= 0
      && typeof file.sha256 === 'string' && /^[a-f0-9]{64}$/.test(file.sha256))) return null;
  if (request.mode === 'dryRun' && value.files.length !== 0) return null;
  return value as unknown as FogHostResponse;
}

function failure(code: FogHostErrorCode, message: string): FogHostResponse {
  return { schemaVersion: 1, ok: false, error: { code, message, retryable: false, retryScope: null } };
}

export class FogHostProcess {
  constructor(private readonly spawnProcess: FogHostSpawn = spawn) {}

  async execute(
    execution: FogHostExecutionRequest,
    onEvent?: (event: FogHostEvent) => void,
  ): Promise<FogHostResponse> {
    if (!uint32(execution.request.seed) || execution.request.schemaVersion !== 1
      || execution.request.game !== 'DS1R' || !['dryRun', 'write'].includes(execution.request.mode)
      || !Number.isSafeInteger(execution.timeoutMs) || execution.timeoutMs <= 0
      || !record(execution.request.compatibility)
      || Object.keys(execution.request.compatibility).length !== 0
      || !record(execution.request.options)
      || Object.keys(execution.request.options).length !== optionKeys.length
      || !optionKeys.every((key) => typeof execution.request.options[key] === 'boolean')
      || !languages.has(execution.request.language)
      || ![execution.request.fogDataRoot, execution.request.stagingDirectory,
        execution.request.logPath, execution.requestPath, execution.responsePath,
        execution.executablePath].every((path) => typeof path === 'string' && path.length > 0)) {
      return failure('FOG_REQUEST_INVALID', 'Invalid FogHost request or timeout.');
    }

    try {
      await stat(execution.responsePath);
      return failure('FOG_REQUEST_INVALID', 'FogHost response path must be unused.');
    } catch (error) {
      if (record(error) && error.code !== 'ENOENT') {
        return failure('FOG_IO_FAILED', `Cannot inspect FogHost response path: ${String(error)}`);
      }
    }

    try {
      await writeFile(execution.requestPath, JSON.stringify(execution.request), { flag: 'wx' });
    } catch (error) {
      return failure('FOG_IO_FAILED', `Cannot write FogHost request: ${String(error)}`);
    }

    return new Promise((resolve) => {
      let child: ChildProcess;
      try {
        child = this.spawnProcess(execution.executablePath,
          ['generate', '--request', execution.requestPath, '--response', execution.responsePath],
          { stdio: ['ignore', 'pipe', 'pipe'] });
      } catch (error) {
        resolve(failure('FOG_HOST_CRASHED', `Cannot start FogHost: ${String(error)}`));
        return;
      }

      let settled = false;
      let stdoutBuffer = '';
      const finish = (result: FogHostResponse) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        resolve(result);
      };
      const emitLine = (line: string) => {
        try {
          const event: unknown = JSON.parse(line);
          if (record(event) && event.type === 'progress'
            && typeof event.stage === 'string' && typeof event.message === 'string') {
            onEvent?.({ type: 'progress', stage: event.stage, message: event.message });
            return;
          }
        } catch { /* Unknown stdout is log text. */ }
        onEvent?.({ type: 'log', stream: 'stdout', message: `${line}\n` });
      };
      child.stdout?.on('data', (chunk: Buffer | string) => {
        stdoutBuffer += chunk.toString();
        let newline = stdoutBuffer.indexOf('\n');
        while (newline >= 0) {
          emitLine(stdoutBuffer.slice(0, newline).replace(/\r$/, ''));
          stdoutBuffer = stdoutBuffer.slice(newline + 1);
          newline = stdoutBuffer.indexOf('\n');
        }
      });
      child.stderr?.on('data', (chunk: Buffer | string) => {
        onEvent?.({ type: 'log', stream: 'stderr', message: chunk.toString() });
      });
      child.once('error', (error) => finish(failure('FOG_HOST_CRASHED', error.message)));
      child.once('close', (exitCode) => {
        if (settled) return;
        if (stdoutBuffer) onEvent?.({ type: 'log', stream: 'stdout', message: stdoutBuffer });
        if (exitCode !== 0) {
          finish(failure('FOG_HOST_CRASHED', `FogHost exited with code ${exitCode}.`));
          return;
        }
        void readFile(execution.responsePath, 'utf8').then((contents) => {
          let value: unknown;
          try { value = JSON.parse(contents); }
          catch { finish(failure('FOG_HOST_CRASHED', 'FogHost returned invalid response JSON.')); return; }
          finish(parseResponse(value, execution.request)
            ?? failure('FOG_HOST_CRASHED', 'FogHost returned an invalid response contract.'));
        }).catch(() => finish(failure('FOG_HOST_CRASHED', 'FogHost exited without a response file.')));
      });
      const timer = setTimeout(() => {
        child.kill();
        finish(failure('FOG_HOST_TIMEOUT', `FogHost exceeded ${execution.timeoutMs} ms.`));
      }, execution.timeoutMs);
    });
  }
}
