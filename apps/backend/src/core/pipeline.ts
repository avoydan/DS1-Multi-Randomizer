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

/**
 * The plan preview in words: which behaviors the settings switch on. It names no location, so it can be shown before and without the spoiler log.
 * Every line carries its English text (`label`, `value`) and stable keys (`labelKey`, `valueKey`, `params`) so the interface can show it in its own language
 * without reading the English sentence (docs/I18N.md).
 */
export function describePlanBehaviors(config: RandomizerConfig): PlanBehavior[] {
  const lines: PlanBehavior[] = [];
  const line = (labelKey: string, label: string, valueKey: string, value: string, params?: Record<string, string>): void => {
    lines.push({ label, value, labelKey, valueKey, ...(params ? { params } : {}) });
  };
  const items = config.items;
  if (!items.enabled) {
    line('items', 'Items', 'notRandomized', 'not randomized');
  } else {
    const pools = ([
      [items.randomizeWorldItems, 'worldItems', 'world items'], [items.randomizeGifts, 'npcGifts', 'NPC gifts'],
      [items.randomizeEnemyDrops, 'enemyDrops', 'enemy drops'], [items.randomizeShops, 'shops', 'shops'],
    ] as const).filter(([on]) => on);
    if (pools.length > 0) line('items', 'Items', 'itemsShuffled', `shuffled: ${pools.map(([, , text]) => text).join(', ')}`, { pools: pools.map(([, id]) => id).join(',') });
    else line('items', 'Items', 'itemsExtrasOnly', 'only the selected extras (no item pool is on)');
    const soulsActive = items.randomizeWorldItems || items.randomizeGifts || items.randomizeEnemyDrops;
    if (soulsActive) {
      line('soulItems', 'Soul items', `souls.${items.soulItems}`, ({
        shuffled: 'Shuffled (the lesser souls move with the other items; the boss souls stay with their bosses)',
        replaced: 'Replaced (the lesser souls become other consumables)',
        transposed: 'Transposed (the boss souls join the item pool; each has a 75% chance to become a weapon made from that soul)',
      } as const)[items.soulItems]);
    } else {
      line('soulItems', 'Soul items', 'souls.noEffect', 'no effect (turn on world items, NPC gifts or enemy drops)');
    }
    // A configuration with key placement but no world items is refused by the validation before a plan exists.
    const keyMode = items.keyPlacement ?? 'vanilla';
    if (keyMode !== 'vanilla') {
      line('keyItems', 'Key items', `keys.${keyMode}`, ({
        shuffled: 'Shuffled (placed by a safety-checked fill over the world graph: every area stays reachable and Firelink Altar can be reached; gifts, shops and drops never hold a key)',
        race: 'Race Mode (the keys and embers lie only in boss rewards, chests and the places where keys normally are; the same safety check applies)',
        'race-plus': 'Race Mode + (as Race Mode, but a world may need skips and glitches such as the Capra skip or the Seal skip to be completed; softlocking is possible without them)',
      } as const)[keyMode]);
      if (items.randomizeLordvessel) line('lordvessel', 'Lordvessel', 'vessel.shuffled', 'shuffled with the keys (Gwynevere then hands over another item of the pool)');
      else line('lordvessel', 'Lordvessel', 'vessel.vanilla', 'stays where the game puts it (given by Gwynevere)');
      if (items.randomizeLordSouls) line('lordSouls', 'Lord Souls', 'lordSouls.shuffled', 'shuffled with the keys (the four rewards go to other pickups, and the Lords drop other items)');
      else line('lordSouls', 'Lord Souls', 'lordSouls.vanilla', 'stay where the game puts them (the rewards of the four Lords)');
    } else {
      line('keyItems', 'Key items', 'keys.vanilla', 'stay where the game puts them (progression items are protected)');
      line('lordvessel', 'Lordvessel', 'vessel.vanilla', 'stays where the game puts it (given by Gwynevere)');
      line('lordSouls', 'Lord Souls', 'lordSouls.vanilla', 'stay where the game puts them (the rewards of the four Lords)');
    }
    const difficulty = items.itemDifficulty ?? 'fair';
    line('itemDifficulty', 'Item difficulty', `difficulty.${difficulty}`, ({
      fair: 'Fair (items have an equal chance to be placed anywhere)',
      unfair: 'Unfair (desirable items are not placed in plain view)',
      'very-unfair': 'Very Unfair (desirable items are hidden and rarely in early areas)',
    } as const)[difficulty]);
    if (items.laundromat) line('laundromat', 'Laundromat Mixup', 'laundromatOn', 'on (NPC characters wear armor pieces other NPCs wear; only their appearance and armor stats change)');
  }
  if (config.enemies.enabled) {
    if (config.enemies.placement.algorithm === 'sampling-v1') line('enemyPlacement', 'Enemy placement', 'placement.sampling', 'Sampling (each slot chooses a compatible replacement)');
    else line('enemyPlacement', 'Enemy placement', 'placement.permutation', 'Classic permutation (the existing enemies are shuffled)');
  } else {
    line('enemiesAndBosses', 'Enemies and bosses', 'notRandomized', 'not randomized');
  }
  line('fogRandomizer', 'Fog randomizer', config.fog.enabled ? 'on' : 'off', config.fog.enabled ? 'on' : 'off');
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
