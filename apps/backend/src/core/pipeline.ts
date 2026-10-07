import path from 'node:path';
import { access } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import type { PipelinePlan, PlanBehavior, RandomizerConfig } from '@ds1tr/contracts';
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

/** The plan preview in words: which behaviors the settings switch on. It names no location, so it can be shown before and without the spoiler log. */
export function describePlanBehaviors(config: RandomizerConfig): PlanBehavior[] {
  const lines: PlanBehavior[] = [];
  const items = config.items;
  if (!items.enabled) {
    lines.push({ label: 'Items', value: 'not randomized' });
  } else {
    const pools = [
      items.randomizeWorldItems && 'world items', items.randomizeGifts && 'NPC gifts', items.randomizeEnemyDrops && 'enemy drops', items.randomizeShops && 'shops',
    ].filter(Boolean);
    lines.push({ label: 'Items', value: pools.length > 0 ? `shuffled: ${pools.join(', ')}` : 'only the selected extras (no item pool is on)' });
    const soulsActive = items.randomizeWorldItems || items.randomizeGifts || items.randomizeEnemyDrops;
    const souls = soulsActive
      ? ({
        shuffled: 'Shuffled (the lesser souls move with the other items; the boss souls stay with their bosses)',
        replaced: 'Replaced (the lesser souls become other consumables)',
        transposed: 'Transposed (the boss souls join the item pool; each has a 75% chance to become a weapon made from that soul)',
      } as const)[items.soulItems]
      : 'no effect (turn on world items, NPC gifts or enemy drops)';
    lines.push({ label: 'Soul items', value: souls });
    // A configuration with key placement but no world items is refused by the validation before a plan exists.
    if ((items.keyPlacement ?? 'vanilla') === 'shuffled') {
      lines.push({ label: 'Key items', value: 'Shuffled (placed by a safety-checked fill over the world graph: every area stays reachable and Firelink Altar can be reached; gifts, shops and drops never hold a key)' });
      lines.push({ label: 'Lordvessel', value: items.randomizeLordvessel ? 'shuffled with the keys (Gwynevere then hands over another item of the pool)' : 'stays where the game puts it (given by Gwynevere)' });
      lines.push({ label: 'Lord Souls', value: items.randomizeLordSouls ? 'shuffled with the keys (the four rewards go to other pickups, and the Lords drop other items)' : 'stay where the game puts them (the rewards of the four Lords)' });
    } else {
      lines.push({ label: 'Key items', value: 'stay where the game puts them (progression items are protected)' });
      lines.push({ label: 'Lordvessel', value: 'stays where the game puts it (given by Gwynevere)' });
      lines.push({ label: 'Lord Souls', value: 'stay where the game puts them (the rewards of the four Lords)' });
    }
  }
  if (config.enemies.enabled) {
    lines.push({
      label: 'Enemy placement',
      value: config.enemies.placement.algorithm === 'sampling-v1' ? 'Sampling (each slot chooses a compatible replacement)' : 'Classic permutation (the existing enemies are shuffled)',
    });
  } else {
    lines.push({ label: 'Enemies and bosses', value: 'not randomized' });
  }
  lines.push({ label: 'Fog randomizer', value: config.fog.enabled ? 'on' : 'off' });
  return lines;
}

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
    behaviors: describePlanBehaviors(config),
  };
}

async function packagedStatus(name: 'combined' | 'fog', ...files: string[]) {
  const present = (await Promise.all(files.map((file) => access(file).then(() => true, () => false)))).every(Boolean);
  return {
    name, ready: present, mode: present ? 'executable' as const : 'missing' as const,
    detail: present ? 'Packaged runtime files detected.' : 'Packaged runtime files are missing; reinstall the desktop application.',
  };
}
