// Engine-level sweep for a normal enemy as the body of a boss encounter (Part E2) on a real extracted catalog (dsr-catalog.json),
// without the data tool or the game.
//
//   node apps/backend/scripts/bodySweep.mjs <dsr-catalog.json> [--seeds 100] [--sets name,name]
//
// Checks per config x seed: the engine does not throw, the result is deterministic, bodies stand only in the audited slots and only
// with the allowed models, a body keeps the encounter's entity id and carries a scaled NpcParam row, the final model count of
// every map never exceeds max(30, the vanilla model count), the modes relate as they should ("both" with chance 100 is Part D's
// boss sampling, "both" with chance 0 has the bodies of "normals"). A failure is printed with its config and seed; nothing is retried and no limit is relaxed.

import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { loadCombinedRandomizerCore } from '../dist/adapters/combinedRandomizerCore.js';

const [catalogPath, ...rest] = process.argv.slice(2);
if (!catalogPath) { console.error('Usage: node bodySweep.mjs <dsr-catalog.json> [--seeds N] [--sets a,b]'); process.exit(2); }
const option = (name, fallback) => { const i = rest.indexOf(name); return i >= 0 ? rest[i + 1] : fallback; };
const seeds = Number(option('--seeds', 100));
const only = option('--sets', '') ? option('--sets', '').split(',') : null;

const loaded = await loadCombinedRandomizerCore();
if (!loaded.ok) throw new Error('core does not load');
const { generate, defaultConfig } = loaded.core;
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const bossSlotById = new Map(catalog.bossSlots.map((slot) => [slot.id, slot]));

const SUPPORTED = new Set(['m18_01_00_00:c2232_0000', 'm10_01_00_00:c2250_0000', 'm10_01_00_00:c2240_0000']);
const BODY_MODELS = new Set(['c2500', 'c2540', 'c2550', 'c2560', 'c2520', 'c2790', 'c2410', 'c2390', 'c2810', 'c2690', 'c2800']);
const poolWithBodies = ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2500', 'c2560', 'c2390', 'c2711', 'c2860', 'c2940'];
const poolWithoutBodies = ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2711', 'c2860', 'c2940'];

const base = {
  ...defaultConfig, randomizeEnemies: true, randomizeBosses: true, randomizeItems: false, generateSpoilerLog: false,
  enemyPlacementAlgorithm: 'sampling-v1', placementDiagnosticsLevel: 'summary', randomizeRegularEnemies: true,
  enemyReplacementChance: 100, enemyBossReplacement: 'normals',
};

const sets = {
  'body-basic': { enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'random' },
  'body-fits': { enemyPlacementFit: 'fits', enemyDifficultyMode: 'random' },
  'body-anywhere': { enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'random-easy-asylum' },
  'body-curve': { enemyPlacementFit: 'fits', enemyDifficultyMode: 'curve', enemyDifficultyStrictness: 'strict' },
  'body-chance-50': { enemyPlacementFit: 'fits', enemyReplacementChance: 50 },
  'body-vanilla-scaling': { enemyPlacementFit: 'anywhere', enemyScaling: 'vanilla' },
  'body-progressive-scaling': { enemyPlacementFit: 'anywhere', enemyScaling: 'progressive' },
  'body-plus-roaming': { enemyPlacementFit: 'fits', enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 20 },
  'body-plus-roaming-anywhere': { enemyPlacementFit: 'anywhere', enemyNormalReplacement: 'bosses', enemyReplacementChance: 60 },
  'both-boss-sampling-40': { enemyPlacementFit: 'anywhere', enemyBossReplacement: 'both', enemyBossChanceBossSlots: 40 },
  'both-boss-sampling-90': { enemyPlacementFit: 'fits', enemyBossReplacement: 'both' },
  'both-unique': { enemyPlacementFit: 'anywhere', enemyBossReplacement: 'both', enemyBossChanceBossSlots: 50, enemyUniqueBosses: true },
  'both-plus-roaming': { enemyPlacementFit: 'fits', enemyBossReplacement: 'both', enemyBossChanceBossSlots: 60, enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 25 },
  'body-type-enabled': { enemyPlacementFit: 'fits', enemyTypeReplacement: 'enabled' },
  'body-custom-pool': { enemyPlacementFit: 'anywhere', enemyCustomPool: poolWithBodies },
  'body-custom-pool-no-bodies': { enemyPlacementFit: 'anywhere', enemyCustomPool: poolWithoutBodies },
  'body-prevent-same': { enemyPlacementFit: 'anywhere', preventSameEnemy: true },
  'body-gwyn-strong-gargoyle': { enemyPlacementFit: 'fits', gwynNerf: 'strong', secondGargoyle: 'replace' },
};

const vanillaModelCounts = new Map();
for (const slot of catalog.enemySlots) {
  if (slot.dummy || !/^m1[0-8]_/u.test(slot.mapId) || !slot.modelName.startsWith('c')) continue;
  if (!vanillaModelCounts.has(slot.mapId)) vanillaModelCounts.set(slot.mapId, new Set());
  vanillaModelCounts.get(slot.mapId).add(slot.modelName);
}
for (const slot of catalog.bossSlots) if (vanillaModelCounts.has(slot.mapId)) vanillaModelCounts.get(slot.mapId).add(slot.modelName);

const hash = (value) => createHash('sha256').update(JSON.stringify(value)).digest('hex').slice(0, 16);
const finalModels = (plan) => {
  const byMap = new Map();
  const all = [...plan.placements.enemies, ...(plan.placements.bosses ?? [])];
  const placed = new Map(all.map((placement) => [placement.slot, placement]));
  for (const slot of [...catalog.enemySlots, ...catalog.bossSlots]) {
    if (!/^m1[0-8]_/u.test(slot.mapId) || slot.dummy) continue;
    const placement = placed.get(slot.id);
    const model = placement ? placement.targetModelName : slot.modelName;
    if (!byMap.has(slot.mapId)) byMap.set(slot.mapId, new Set());
    byMap.get(slot.mapId).add(model);
  }
  return byMap;
};

let failures = 0;
const fail = (name, seed, message) => { failures += 1; if (failures <= 40) console.log(`FAIL ${name} #${seed}: ${message}`); };

for (const [name, overrides] of Object.entries(sets)) {
  if (only && !only.includes(name)) continue;
  const stat = { ok: 0, bodies: 0, worldsWithBodies: 0, maxModels: 0, models: new Set(), perSlot: new Map() };
  for (let index = 0; index < seeds; index += 1) {
    const config = { ...base, seed: `${name}-${index}`, ...overrides };
    let plan;
    try {
      plan = generate(config, { gameCatalog: catalog });
      if (hash(plan.placements) !== hash(generate(config, { gameCatalog: catalog }).placements)) { fail(name, index, 'not deterministic'); continue; }
    } catch (error) { fail(name, index, `${error.code ?? ''} ${error.message}`.slice(0, 220)); continue; }
    const bodies = (plan.placements.bosses ?? []).filter((placement) => placement.compatibility === 'sampled-encounter-body');
    let bad = null;
    for (const placement of bodies) {
      const slot = bossSlotById.get(placement.slot);
      if (!SUPPORTED.has(placement.slot)) bad = `body in an unsupported slot ${placement.slot}`;
      else if (!BODY_MODELS.has(placement.targetModelName)) bad = `${placement.targetModelName} must not be a body`;
      else if (placement.entityId !== slot?.entityId) bad = `entity id ${placement.entityId} != ${slot?.entityId}`;
      else if (placement.encounterBody !== true) bad = 'body without the encounterBody mark';
      else if (placement.scaledNpcParamId === null || placement.scaledNpcParamId === undefined) bad = 'body without a scaled NpcParam row';
      else if (placement.scaling !== (config.enemyScaling ?? 'area')) bad = `scaling ${placement.scaling}`;
      else if (placement.originalBossName === placement.randomizedBossName) bad = 'the body keeps the boss name in the spoiler';
      else if (config.enemyCustomPool && !config.enemyCustomPool.includes(placement.targetModelName)) bad = `${placement.targetModelName} is outside the custom pool`;
      stat.models.add(placement.targetModelName);
      stat.perSlot.set(placement.slot, (stat.perSlot.get(placement.slot) ?? 0) + 1);
      if (bad) break;
    }
    if (bad) { fail(name, index, bad); continue; }
    if (new Set(bodies.map((placement) => placement.slot)).size !== bodies.length) { fail(name, index, 'a slot has two bodies'); continue; }
    if (config.enemyCustomPool === poolWithoutBodies && bodies.length > 0) { fail(name, index, 'a body from a pool without body models'); continue; }
    // Model budget: no map ends above max(30, the vanilla model count).
    let worse = null;
    for (const [mapId, models] of finalModels(plan)) {
      stat.maxModels = Math.max(stat.maxModels, models.size);
      const limit = Math.max(30, vanillaModelCounts.get(mapId)?.size ?? 0);
      if (models.size > limit) worse = `${mapId}: ${models.size} models, limit ${limit}`;
    }
    if (worse) { fail(name, index, `model budget: ${worse}`); continue; }
    // Relations between the modes (the same seed, the same options):
    //  - "both" with chance 100 draws a boss for every slot: exactly the boss sampling of Part D ("bosses");
    //  - "both" with chance 0 gives every audited slot a body: exactly the bodies of "normals" (the other bosses and, through the
    //    models they bring to a map, the regular enemies of "both" legitimately differ).
    if (config.enemyBossReplacement === 'both' && !config.enemyUniqueBosses) {
      const partD = generate({ ...config, enemyBossReplacement: 'bosses', enemyBossChanceBossSlots: 90 }, { gameCatalog: catalog });
      const allBosses = generate({ ...config, enemyBossChanceBossSlots: 100 }, { gameCatalog: catalog });
      if (hash(allBosses.placements) !== hash(partD.placements)) { fail(name, index, 'both with chance 100 differs from boss sampling'); continue; }
    }
    if (config.enemyBossReplacement === 'normals') {
      const asBoth = generate({ ...config, enemyBossReplacement: 'both', enemyBossChanceBossSlots: 0 }, { gameCatalog: catalog });
      const bodiesOf = (value) => hash((value.placements.bosses ?? []).filter((placement) => placement.compatibility === 'sampled-encounter-body'));
      if (bodiesOf(asBoth) !== bodiesOf(plan)) { fail(name, index, 'both with chance 0 differs from normals'); continue; }
    }
    stat.ok += 1;
    stat.bodies += bodies.length;
    stat.worldsWithBodies += bodies.length > 0 ? 1 : 0;
  }
  const slots = [...stat.perSlot].map(([slot, n]) => `${slot.split(':')[1].slice(0, 5)}=${n}`).join(' ');
  console.log(`${name.padEnd(30)} ok ${stat.ok}/${seeds}  bodies ${stat.bodies} (worlds with any ${stat.worldsWithBodies})  maxModels ${stat.maxModels}  body models ${stat.models.size}  per slot ${slots || '-'}`);
}
console.log(failures === 0 ? 'ALL OK' : `${failures} failure(s)`);
process.exit(failures === 0 ? 0 : 1);
