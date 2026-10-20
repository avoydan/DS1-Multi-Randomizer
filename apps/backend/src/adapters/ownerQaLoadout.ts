import { spawn } from 'node:child_process';
import type { ChildProcess, SpawnOptions } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const sourceDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = process.env.DS1TR_RUNTIME_ROOT ?? path.resolve(sourceDirectory, '../../../../');
const defaultDataTool = path.join(repositoryRoot, 'engines', 'combined', 'vendor', 'tools', 'DsrDataTool', 'bin', 'Release', 'net8.0', 'DsrDataTool.dll');
export const ownerQaLoadoutVariable = 'DS1TR_OWNER_QA_LOADOUT';

export type OwnerQaLoadoutResult =
  | { ok: true; stdout: string; stderr: string }
  | { ok: false; code: 'OWNER_QA_TOOL_FAILED' | 'OWNER_QA_TOOL_MISSING'; message: string; stdout: string; stderr: string };

export type OwnerQaSpawn = (command: string, args: string[], options: SpawnOptions) => ChildProcess;

export function ownerQaLoadoutEnabled(env: NodeJS.ProcessEnv = process.env): boolean {
  return env[ownerQaLoadoutVariable] === '1';
}

export async function applyOwnerQaLoadout(
  gameRoot: string,
  packagePath: string,
  options: {
    dotnetPath?: string;
    dataToolPath?: string;
    spawnProcess?: OwnerQaSpawn;
    onLog?: (stream: 'stdout' | 'stderr', message: string) => void;
  } = {},
): Promise<OwnerQaLoadoutResult> {
  const dotnet = options.dotnetPath ?? process.env.DS1TR_DOTNET_PATH ?? 'dotnet';
  const dataTool = options.dataToolPath ?? (process.env.DS1TR_DATA_TOOL_EXE ? '' : defaultDataTool);
  const commandArgs = [
    ...(dataTool ? [dataTool] : []),
    'apply-owner-qa-loadout',
    '--game', gameRoot,
    '--package', packagePath,
  ];
  let child: ChildProcess;
  try {
    child = (options.spawnProcess ?? spawn)(dotnet, commandArgs, { stdio: ['ignore', 'pipe', 'pipe'] });
  } catch (error) {
    return { ok: false, code: 'OWNER_QA_TOOL_MISSING', message: error instanceof Error ? error.message : String(error), stdout: '', stderr: '' };
  }
  return new Promise((resolve) => {
    let stdout = '';
    let stderr = '';
    let settled = false;
    child.stdout?.on('data', (chunk: Buffer | string) => {
      const message = chunk.toString();
      stdout += message;
      options.onLog?.('stdout', message);
    });
    child.stderr?.on('data', (chunk: Buffer | string) => {
      const message = chunk.toString();
      stderr += message;
      options.onLog?.('stderr', message);
    });
    child.once('error', (error) => {
      if (settled) return;
      settled = true;
      resolve({ ok: false, code: 'OWNER_QA_TOOL_MISSING', message: error.message, stdout, stderr });
    });
    child.once('close', (code) => {
      if (settled) return;
      settled = true;
      resolve(code === 0
        ? { ok: true, stdout, stderr }
        : { ok: false, code: 'OWNER_QA_TOOL_FAILED', message: `DsrDataTool exited with code ${code ?? 'unknown'}.`, stdout, stderr });
    });
  });
}
