// Engine-level sweep of the EXACT product default (Part G): the settings a fresh installation starts with, made into engine configurations the way the
// application does it (prepareRun + mapCombinedRandomizerConfig), on a real extracted catalog, without the data tool or the game.
//
//   node apps/backend/scripts/defaultSweep.mjs <dsr-catalog.json> [--seeds 500] [--classic]
//
// Checks per seed: the engine does not throw, the world is deterministic (the placement hash of a second run is the same), no map ends above
// max(30, the models its vanilla slots use), the placement validation summary is consistent, and every placement is slot-owned. It prints the
// aggregate of the validation summaries (retries, difficulty lapses, class fallbacks, models, events asked of the data tool, own rows).
// --classic runs the same sweep with the frozen classic permutation (the settings an older installation keeps).

import { readFileSync } from 'node:fs';
import { DEFAULT_GENERATION_SETTINGS } from '@ds1tr/contracts';
import { loadCombinedRandomizerCore } from '../dist/adapters/combinedRandomizerCore.js';
import { mapCombinedRandomizerConfig } from '../dist/adapters/combinedRandomizerConfig.js';
import { currentGenerationProfile } from '../dist/core/generationProfiles.js';
import { deriveAttemptSeeds } from '../dist/core/seeds.js';
import { prepareRun } from '../dist/core/seededRun.js';

const [catalogPath, ...rest] = process.argv.slice(2);
if (!catalogPath) { console.error('Usage: node defaultSweep.mjs <dsr-catalog.json> [--seeds N] [--classic]'); process.exit(2); }
const seeds = Number(rest.includes('--seeds') ? rest[rest.indexOf('--seeds') + 1] : 500);
const classic = rest.includes('--classic');
const loaded = await loadCombinedRandomizerCore();
if (!loaded.ok) throw new Error('the engine does not load');
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const profile = currentGenerationProfile();
const settings = structuredClone(DEFAULT_GENERATION_SETTINGS);
if (classic) settings.enemies.placement = { ...settings.enemies.placement, algorithm: 'permutation-v1', fit: 'anywhere', difficultyMode: 'random' };

const vanillaModels = new Map();
for (const slot of [...catalog.enemySlots, ...catalog.bossSlots]) {
  if (!/^m1[0-8]_/u.test(slot.mapId) || !slot.modelName.startsWith('c')) continue; // dummy parts count: the MSB model list holds their models too
  if (!vanillaModels.has(slot.mapId)) vanillaModels.set(slot.mapId, new Set());
  vanillaModels.get(slot.mapId).add(slot.modelName);
}
const finalModels = (plan) => {
  const byMap = new Map();
  const placed = new Map([...plan.placements.enemies, ...plan.placements.bosses].map((placement) => [placement.slot, placement]));
  for (const slot of [...catalog.enemySlots, ...catalog.bossSlots]) {
    if (!/^m1[0-8]_/u.test(slot.mapId)) continue;
    if (!byMap.has(slot.mapId)) byMap.set(slot.mapId, new Set());
    byMap.get(slot.mapId).add(placed.get(slot.id)?.targetModelName ?? slot.modelName);
  }
  return byMap;
};

const totals = { worlds: 0, failures: 0, nondeterministic: 0, overBudget: 0, notSlotOwned: 0, retries: 0, lapses: 0, fallbacks: 0, changed: 0, kept: 0, wakeUps: 0, passive: 0, disabled: 0, npcRows: 0, thinkRows: 0, maxModels: 0, maxWakeUpsInWorld: 0 };
const fail = (seed, message) => { totals.failures += 1; if (totals.failures <= 20) console.log(`FAIL #${seed}: ${message}`); };
for (let index = 1; index <= seeds; index += 1) {
  const masterSeed = `default-sweep-${index}`;
  const prepared = prepareRun({ gamePath: 'X:/search', masterSeed, ...structuredClone(settings), output: { ...settings.output, generateSpoilerLog: false } });
  if (!prepared.ok) throw new Error(`${prepared.code}: ${prepared.technical}`);
  const mapped = mapCombinedRandomizerConfig({
    gamePath: 'X:/search', workspacePath: 'X:/search/work', masterSeed, combinedSeed: deriveAttemptSeeds(masterSeed, 0, 0, profile.seedDerivation).combined,
    items: prepared.config.items, enemies: prepared.config.enemies, generateSpoilerLog: false, placementDiagnosticsLevel: 'summary', fogTrackedItems: null,
  });
  if (!mapped.ok) throw new Error(mapped.message);
  let plan;
  try {
    plan = loaded.core.generate(mapped.config, { gameCatalog: catalog });
    if (loaded.core.generate(mapped.config, { gameCatalog: catalog }).placementHash !== plan.placementHash) { totals.nondeterministic += 1; fail(index, 'not deterministic'); continue; }
  } catch (error) { fail(index, `${error.code ?? ''} ${String(error.message).slice(0, 200)}`); continue; }
  totals.worlds += 1;
  const summary = plan.validation.enemyPlacement;
  if (!classic && plan.placements.enemies.some((placement) => placement.changed && (placement.scaledNpcParamId === null || placement.scaledNpcParamId === undefined))) { totals.notSlotOwned += 1; fail(index, 'a changed placement keeps the replacement drops'); }
  for (const [mapId, models] of finalModels(plan)) {
    totals.maxModels = Math.max(totals.maxModels, models.size);
    if (models.size > Math.max(30, vanillaModels.get(mapId)?.size ?? 0)) { totals.overBudget += 1; fail(index, `${mapId}: ${models.size} models`); }
  }
  totals.retries += summary.retries;
  totals.lapses += summary.difficultyLapses;
  totals.fallbacks += summary.classFallbacks;
  totals.changed += summary.placements.changed;
  totals.kept += summary.placements.keptVanilla;
  totals.wakeUps += summary.events.wakeUp;
  totals.passive += summary.events.passiveUntilAttacked;
  totals.disabled += summary.events.disabledEntities;
  totals.npcRows += summary.ownRows.npcParam;
  totals.thinkRows += summary.ownRows.npcThinkParam;
  totals.maxWakeUpsInWorld = Math.max(totals.maxWakeUpsInWorld, summary.events.wakeUp);
}
const mean = (value) => (value / Math.max(1, totals.worlds)).toFixed(1);
console.log(`${classic ? 'classic permutation' : 'default (sampling)'}: ${totals.worlds}/${seeds} worlds, ${totals.failures} failures, ${totals.nondeterministic} non-deterministic, ${totals.overBudget} maps over the model limit, ${totals.notSlotOwned} not slot-owned`);
console.log(`per world (mean): changed ${mean(totals.changed)}, kept ${mean(totals.kept)}, retries ${mean(totals.retries)}, difficulty lapses ${mean(totals.lapses)}, class fallbacks ${mean(totals.fallbacks)}, wake-ups ${mean(totals.wakeUps)} (max ${totals.maxWakeUpsInWorld}), passive ${mean(totals.passive)}, disabled ${mean(totals.disabled)}, own NpcParam rows ${mean(totals.npcRows)}, own think rows ${mean(totals.thinkRows)}; max models in a map ${totals.maxModels}; total retries ${totals.retries}`);
process.exit(totals.failures === 0 ? 0 : 1);
