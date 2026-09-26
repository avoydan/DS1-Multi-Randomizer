import path from 'node:path';
import { fileURLToPath } from 'node:url';
import type { PipelinePlan, RandomizerConfig } from '@ds1tr/contracts';
import { deriveSeeds } from './seeds.js';
import { ExternalAdapter } from '../adapters/externalAdapter.js';

const currentDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(currentDir, '../../../../');

const adapters = {
  items: new ExternalAdapter({
    name: 'items',
    engineRoot: path.join(repoRoot, 'engines', 'item'),
    expectedSource: 'vendor/randomizer_gui.py',
    expectedExecutable: 'vendor/DarkSoulsItemRandomizer.exe',
  }),
  enemies: new ExternalAdapter({
    name: 'enemies',
    engineRoot: path.join(repoRoot, 'engines', 'enemy'),
    expectedSource: 'vendor/randomizer_rng.py',
    expectedExecutable: 'vendor/EnemyRandomizer.exe',
  }),
  fog: new ExternalAdapter({
    name: 'fog',
    engineRoot: path.join(repoRoot, 'engines', 'fog'),
    expectedSource: 'vendor/FogMod/Randomizer.cs',
    expectedExecutable: 'vendor/FogMod.exe',
  }),
};

export async function createPipelinePlan(config: RandomizerConfig): Promise<PipelinePlan> {
  const seeds = deriveSeeds(config.masterSeed);
  const engineStatus = await Promise.all([
    adapters.items.status(),
    adapters.enemies.status(),
    adapters.fog.status(),
  ]);

  return {
    seeds,
    stages: [
      { engine: 'items', enabled: config.items.enabled, seed: seeds.items },
      { engine: 'enemies', enabled: config.enemies.enabled, seed: seeds.enemies },
      { engine: 'fog', enabled: config.fog.enabled, seed: seeds.fog },
    ],
    engineStatus,
  };
}
