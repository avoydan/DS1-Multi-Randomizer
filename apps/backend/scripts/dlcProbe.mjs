// Probe: what a generated plan looks like with the DLC left out (used to write dlcSweep.mjs). Usage: node dlcProbe.mjs <catalog.json>
import { readFileSync } from 'node:fs';
import { buildSettings } from './goldenCompatibility.mjs';
import { loadCombinedRandomizerCore } from '../dist/adapters/combinedRandomizerCore.js';
import { mapCombinedRandomizerConfig } from '../dist/adapters/combinedRandomizerConfig.js';
import { excludeDlcFromCatalog } from '../dist/adapters/dlcExclusion.js';
import { currentGenerationProfile } from '../dist/core/generationProfiles.js';
import { deriveAttemptSeeds } from '../dist/core/seeds.js';
import { prepareRun } from '../dist/core/seededRun.js';

const catalog = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const loaded = await loadCombinedRandomizerCore();
const profile = currentGenerationProfile();
const masterSeed = 'probe-1';
const prepared = prepareRun({ gamePath: 'X:/p', masterSeed, ...buildSettings({ fog: { enabled: false }, items: { randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true, soulItems: 'transposed' } }) });
if (!prepared.ok) throw new Error(prepared.code);
const { items, enemies } = prepared.config;
const mapped = mapCombinedRandomizerConfig({ gamePath: 'X:/p', workspacePath: 'X:/p/w', masterSeed, combinedSeed: deriveAttemptSeeds(masterSeed, 0, 0, profile.seedDerivation).combined, items, enemies, generateSpoilerLog: false, placementDiagnosticsLevel: 'summary' });
const excluded = excludeDlcFromCatalog(catalog);
console.log('excluded:', JSON.stringify(excluded.summary));
for (const [label, source] of [['ON', catalog], ['OFF', excluded.catalog]]) {
  const plan = loaded.core.generate(mapped.config, { gameCatalog: source });
  console.log(label, 'dataStatus', plan.dataStatus, 'placement lists', Object.keys(plan.placements).map((key) => `${key}:${Array.isArray(plan.placements[key]) ? plan.placements[key].length : typeof plan.placements[key]}`).join(' '));
  const sample = plan.placements.enemies?.[0] ?? plan.placements.bosses?.[0];
  console.log('  sample enemy/boss placement keys:', sample ? Object.keys(sample).join(',') : 'none');
}
