import path from 'node:path';
import { fileURLToPath } from 'node:url';
import type { PipelinePlan, RandomizerConfig } from '@ds1tr/contracts';
import { deriveSeeds } from './seeds.js';
import { ExternalAdapter } from '../adapters/externalAdapter.js';
import { effectiveCombinedEnabled, validateRandomizerConfig } from './configValidation.js';

const currentDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(currentDir, '../../../../');

const adapters = {
  combined: new ExternalAdapter({
    name: 'combined',
    engineRoot: path.join(repoRoot, 'engines', 'combined'),
    // The vendored combined engine has no CLI entry point and no portable
    // executable: `src/cli.js` is deliberately excluded from the vendor set,
    // and `DsrDataTool.dll` is a .NET assembly run through a dotnet host.
    // See docs/COMBINED_ENGINE_INTEGRATION.md ("JS core vendor proof").
    expectedSource: 'vendor/src/core/generator.js',
    expectedExecutable: 'vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.dll',
  }),
  fog: new ExternalAdapter({
    name: 'fog',
    engineRoot: path.join(repoRoot, 'engines', 'fog'),
    expectedSource: 'vendor/FogMod/Randomizer.cs',
    expectedExecutable: 'vendor/FogMod.exe',
  }),
};

export async function createPipelinePlan(config: RandomizerConfig): Promise<PipelinePlan> {
  validateRandomizerConfig(config);
  const seeds = deriveSeeds(config.masterSeed);
  const engineStatus = await Promise.all([
    adapters.combined.status(),
    adapters.fog.status(),
  ]);

  return {
    seeds,
    stages: [
      { engine: 'combined', enabled: effectiveCombinedEnabled(config), seed: seeds.combined },
      { engine: 'fog', enabled: config.fog.enabled, seed: seeds.fog },
    ],
    engineStatus,
  };
}
