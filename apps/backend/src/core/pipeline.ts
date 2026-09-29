import path from 'node:path';
import { access } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import type { PipelinePlan, RandomizerConfig } from '@ds1tr/contracts';
import { deriveSeeds } from './seeds.js';
import { ExternalAdapter } from '../adapters/externalAdapter.js';
import { effectiveCombinedEnabled, validateRandomizerConfig } from './configValidation.js';

const currentDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = process.env.DS1TR_RUNTIME_ROOT ?? path.resolve(currentDir, '../../../../');

const adapters = {
  combined: new ExternalAdapter({
    name: 'combined',
    engineRoot: path.join(repoRoot, 'engines', 'combined'),
    // The vendored combined engine has no CLI entry point and no portable
    // executable: `src/cli.js` is deliberately excluded from the vendor set,
    // and `DsrDataTool.dll` is a .NET assembly run through a dotnet host.
    // See docs/COMBINED_ENGINE_INTEGRATION.md ("JS core vendor proof").
    expectedSource: 'vendor/src/core/generator.js',
    expectedExecutable: process.env.DS1TR_RUNTIME_ROOT ? 'vendor/tools/DsrDataTool/DsrDataTool.exe' : 'vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll',
  }),
  fog: new ExternalAdapter({
    name: 'fog',
    engineRoot: repoRoot,
    expectedSource: 'tools/fog-host/FogHost.csproj',
    expectedExecutable: process.env.DS1TR_RUNTIME_ROOT ? 'tools/fog-host/FogHost.exe' : 'tools/fog-host/bin/Release/net8.0/FogHost.exe',
  }),
};

export async function createPipelinePlan(config: RandomizerConfig): Promise<PipelinePlan> {
  validateRandomizerConfig(config);
  const seeds = deriveSeeds(config.masterSeed);
  const engineStatus = process.env.DS1TR_RUNTIME_ROOT
    ? await Promise.all([
      packagedStatus('combined', path.join(repoRoot, 'engines', 'combined', 'vendor', 'src', 'core', 'generator.js'),
        path.join(repoRoot, 'engines', 'combined', 'vendor', 'tools', 'DsrDataTool', 'DsrDataTool.exe')),
      packagedStatus('fog', path.join(repoRoot, 'tools', 'fog-host', 'FogHost.exe')),
    ])
    : await Promise.all([adapters.combined.status(), adapters.fog.status()]);

  return {
    seeds,
    stages: [
      { engine: 'combined', enabled: effectiveCombinedEnabled(config), seed: seeds.combined },
      { engine: 'fog', enabled: config.fog.enabled, seed: seeds.fog },
    ],
    engineStatus,
  };
}

async function packagedStatus(name: 'combined' | 'fog', ...files: string[]) {
  const present = (await Promise.all(files.map((file) => access(file).then(() => true, () => false)))).every(Boolean);
  return {
    name, ready: present, mode: present ? 'executable' as const : 'missing' as const,
    detail: present ? 'Packaged runtime files detected.' : 'Packaged runtime files are missing; reinstall the desktop application.',
  };
}
