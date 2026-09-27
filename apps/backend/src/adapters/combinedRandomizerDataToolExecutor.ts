import { spawn } from 'node:child_process';
import type { ChildProcess, SpawnOptions } from 'node:child_process';

export type DsrDataToolCommand = 'scan' | 'patch-enemies';

export interface DsrDataToolExecutionRequest {
  dotnetExecutablePath: string;
  dataToolDllPath: string;
  plannedArguments: string[];
}

export type DsrDataToolEvent =
  | { type: 'log'; stream: 'stdout' | 'stderr'; message: string }
  | { type: 'progress'; command: DsrDataToolCommand; status: 'started' | 'succeeded' | 'failed' };

export type DsrDataToolEventCallback = (event: DsrDataToolEvent) => void;

export type DsrDataToolExecutionResult =
  | { ok: true; stdout: string; stderr: string; exitCode: 0 }
  | { ok: false; type: 'process-failure'; stdout: string; stderr: string; exitCode: number | null }
  | {
    ok: false;
    type: 'spawn-failure';
    stdout: string;
    stderr: string;
    exitCode: null;
    message: string;
  };

export type SpawnProcess = (
  command: string,
  args: string[],
  options: SpawnOptions,
) => ChildProcess;

const supportedCommands = new Set<DsrDataToolCommand>(['scan', 'patch-enemies']);

function isSupportedCommand(command: string | undefined): command is DsrDataToolCommand {
  return command !== undefined && supportedCommands.has(command as DsrDataToolCommand);
}

export class DsrDataToolExecutor {
  constructor(private readonly spawnProcess: SpawnProcess = spawn) {}

  execute(
    request: DsrDataToolExecutionRequest,
    onEvent?: DsrDataToolEventCallback,
  ): Promise<DsrDataToolExecutionResult> {
    const command = request.plannedArguments[0];
    if (!isSupportedCommand(command)) {
      return Promise.resolve({
        ok: false,
        type: 'spawn-failure',
        stdout: '',
        stderr: '',
        exitCode: null,
        message: `Unsupported DsrDataTool command: ${command ?? '(missing)'}.`,
      });
    }

    return new Promise((resolve) => {
      let child: ChildProcess;
      try {
        child = this.spawnProcess(
          request.dotnetExecutablePath,
          [request.dataToolDllPath, ...request.plannedArguments],
          { stdio: ['ignore', 'pipe', 'pipe'] },
        );
      } catch (error) {
        resolve({
          ok: false,
          type: 'spawn-failure',
          stdout: '',
          stderr: '',
          exitCode: null,
          message: error instanceof Error ? error.message : String(error),
        });
        return;
      }

      onEvent?.({ type: 'progress', command, status: 'started' });
      let stdout = '';
      let stderr = '';
      let settled = false;

      child.stdout?.on('data', (chunk: Buffer | string) => {
        const message = chunk.toString();
        stdout += message;
        onEvent?.({ type: 'log', stream: 'stdout', message });
      });
      child.stderr?.on('data', (chunk: Buffer | string) => {
        const message = chunk.toString();
        stderr += message;
        onEvent?.({ type: 'log', stream: 'stderr', message });
      });

      child.once('error', (error) => {
        if (settled) return;
        settled = true;
        onEvent?.({ type: 'progress', command, status: 'failed' });
        resolve({
          ok: false,
          type: 'spawn-failure',
          stdout,
          stderr,
          exitCode: null,
          message: error.message,
        });
      });

      child.once('close', (exitCode) => {
        if (settled) return;
        settled = true;
        if (exitCode === 0) {
          onEvent?.({ type: 'progress', command, status: 'succeeded' });
          resolve({ ok: true, stdout, stderr, exitCode: 0 });
          return;
        }

        onEvent?.({ type: 'progress', command, status: 'failed' });
        resolve({ ok: false, type: 'process-failure', stdout, stderr, exitCode });
      });
    });
  }
}
