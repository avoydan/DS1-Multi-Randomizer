// Engine-level sweep for roaming bosses (Part E1) on a real extracted catalog (dsr-catalog.json), without the data tool or the game.
//
//   node apps/backend/scripts/roamingSweep.mjs <dsr-catalog.json> [--seeds 100] [--sets name,name]
//
// Checks per config x seed: the engine does not throw, the result is deterministic, at most 10 roaming bosses per map, only allowed
// bosses roam and each one carries the souls/respawn decision, the final model count of every map never exceeds
// max(30, what the same world has without the roaming pass), roaming bosses never land on protected slots, and a config
// without roaming is byte-identical to the same config on the previous behavior (normals mode = no roaming keys involved).
// A failure is printed with its config and seed; nothing is retried and no limit is relaxed.

import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { loadCombinedRandomizerCore } from '../dist/adapters/combinedRandomizerCore.js';

const [catalogPath, ...rest] = process.argv.slice(2);
if (!catalogPath) { console.error('Usage: node roamingSweep.mjs <dsr-catalog.json> [--seeds N] [--sets a,b]'); process.exit(2); }
const option = (name, fallback) => { const i = rest.indexOf(name); return i >= 0 ? rest[i + 1] : fallback; };
const seeds = Number(option('--seeds', 100));
const only = option('--sets', '') ? option('--sets', '').split(',') : null;

const loaded = await loadCombinedRandomizerCore();
if (!loaded.ok) throw new Error('core does not load');
const { generate, defaultConfig } = loaded.core;
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const slotById = new Map(catalog.enemySlots.map((slot) => [slot.id, slot]));

const base = {
  ...defaultConfig, randomizeEnemies: true, randomizeBosses: false, randomizeItems: false, generateSpoilerLog: false,
  enemyPlacementAlgorithm: 'sampling-v1', placementDiagnosticsLevel: 'summary',
};
const SOULS = { 223000: 20000, 223100: 20000, 223200: 2000, 224000: 6000, 225000: 3000, 232000: 40000, 236000: 10000, 236001: 25000, 273000: 30000, 323000: 10000, 332000: 15000, 410000: 50000, 450000: 60000, 521000: 40000, 522000: 60000, 527000: 10000, 527100: 25000, 528000: 20000 };
const EXCLUDED = new Set(['c5230', 'c5401', 'c5250', 'c5320', 'c5370', 'c2730', 'c3430', 'c3471', 'c4510', 'c5200', 'c5260', 'c5290', 'c5350', 'c5351', 'c5390']);

const sets = {
  'baseline-normals': { enemyNormalReplacement: 'normals' },
  'baseline-boss-sampling': { enemyNormalReplacement: 'normals', randomizeBosses: true, enemyBossReplacement: 'bosses' },
  'bosses-fits': { enemyNormalReplacement: 'bosses', enemyPlacementFit: 'fits' },
  'bosses-anywhere': { enemyNormalReplacement: 'bosses', enemyPlacementFit: 'anywhere' },
  'both-25': { enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 25, enemyPlacementFit: 'fits' },
  'both-100': { enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 100, enemyPlacementFit: 'anywhere' },
  'bosses-chance-60-curve': { enemyNormalReplacement: 'bosses', enemyReplacementChance: 60, enemyDifficultyMode: 'curve', enemyDifficultyStrictness: 'loose', enemyPlacementFit: 'fits' },
  'bosses-random-easy-asylum': { enemyNormalReplacement: 'bosses', enemyReplacementChance: 25, enemyDifficultyMode: 'random-easy-asylum' },
  'bosses-prevent-same': { enemyNormalReplacement: 'bosses', preventSameEnemy: true },
  'bosses-type-enabled': { enemyNormalReplacement: 'bosses', enemyTypeReplacement: 'enabled', enemyPlacementFit: 'fits' },
  'both-type-except-roaming': { enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 40, enemyTypeReplacement: 'enabled-except-roaming-bosses' },
  'both-type-enabled': { enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 40, enemyTypeReplacement: 'enabled' },
  'bosses-souls-0-stay-dead': { enemyNormalReplacement: 'bosses', enemyRoamingBossSouls: 0, enemyRoamingBossesStayDead: true },
  'bosses-souls-100': { enemyNormalReplacement: 'bosses', enemyRoamingBossSouls: 100, enemyReplacementChance: 40 },
  'bosses-unique-boss-sampling': { enemyNormalReplacement: 'bosses', randomizeBosses: true, enemyBossReplacement: 'bosses', enemyUniqueBosses: true },
  'bosses-boss-sampling-gwyn-strong': { enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 30, randomizeBosses: true, enemyBossReplacement: 'bosses', gwynNerf: 'strong', enemyPlacementFit: 'fits' },
  'bosses-second-gargoyle': { enemyNormalReplacement: 'bosses', randomizeBosses: true, secondGargoyle: 'replace', enemyReplacementChance: 50 },
  'bosses-pinwheel-chaos': { enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 50, pinwheelChaos: true },
};

const finalModels = (plan) => {
  const byMap = new Map();
  const all = [...plan.placements.enemies, ...(plan.placements.bosses ?? [])];
  const placed = new Map(all.map((placement) => [placement.slot, placement]));
  for (const slot of [...catalog.enemySlots, ...catalog.bossSlots]) {
    if (!/^m1[0-8]_/u.test(slot.mapId)) continue;
    const placement = placed.get(slot.id);
    const model = placement ? placement.targetModelName : slot.modelName;
    if (!byMap.has(slot.mapId)) byMap.set(slot.mapId, new Set());
    byMap.get(slot.mapId).add(model);
  }
  for (const placement of all) {
    if (!byMap.has(placement.map)) byMap.set(placement.map, new Set());
    byMap.get(placement.map).add(placement.targetModelName);
  }
  return byMap;
};
const hash = (value) => createHash('sha256').update(JSON.stringify(value)).digest('hex').slice(0, 16);

let failures = 0;
const fail = (name, seed, message) => { failures += 1; if (failures <= 40) console.log(`FAIL ${name} #${seed}: ${message}`); };

for (const [name, overrides] of Object.entries(sets)) {
  if (only && !only.includes(name)) continue;
  const stat = { ok: 0, roamers: 0, withRoamers: 0, maxPerMap: 0, maxModels: 0, bossKinds: new Set(), soulsSeen: new Set() };
  const roamingMode = ['bosses', 'both'].includes(overrides.enemyNormalReplacement);
  for (let index = 0; index < seeds; index += 1) {
    const config = { ...base, seed: `${name}-${index}`, ...overrides };
    let plan;
    try {
      plan = generate(config, { gameCatalog: catalog });
      if (hash(plan.placements) !== hash(generate(config, { gameCatalog: catalog }).placements)) { fail(name, index, 'not deterministic'); continue; }
    } catch (error) { fail(name, index, `${error.code ?? ''} ${error.message}`.slice(0, 220)); continue; }
    const roaming = plan.placements.enemies.filter((placement) => placement.compatibility === 'sampled-roaming-boss');
    if (!roamingMode && roaming.length > 0) { fail(name, index, 'roaming bosses without roaming mode'); continue; }
    const perMap = new Map();
    let bad = null;
    for (const placement of roaming) {
      perMap.set(placement.map, (perMap.get(placement.map) ?? 0) + 1);
      const model = placement.targetModelName;
      const slot = slotById.get(placement.slot);
      const npcParam = Number(String(placement.to).match(/NPC (\d+)/u)?.[1]);
      if (EXCLUDED.has(model) || !(npcParam in SOULS)) bad = `${model} (NPC ${npcParam}) must not roam`;
      else if (placement.scaling !== 'vanilla') bad = 'roaming boss without vanilla scaling';
      else if (placement.roamingBossSouls !== Math.trunc(SOULS[npcParam] * (config.enemyRoamingBossSouls ?? 50) / 100)) bad = `souls ${placement.roamingBossSouls} for NPC ${npcParam}`;
      else if ((placement.disableRespawn === true) !== Boolean(config.enemyRoamingBossesStayDead)) bad = 'stay-dead flag does not match the option';
      else if (placement.scaledNpcParamId === null || placement.scaledNpcParamId === undefined) bad = 'roaming boss without scaled NpcParam row';
      else if (!(placement.forceCombatActivation === true || placement.entityId < 0 || placement.passiveUntilAttacked)) bad = 'roaming boss without combat activation';
      else if (slot?.eventModelLocked === true) bad = 'roaming boss on an event-locked slot';
      else if (placement.passiveUntilAttacked) bad = 'roaming boss on a passive slot';
      stat.bossKinds.add(model);
      stat.soulsSeen.add(placement.roamingBossSouls);
      if (bad) break;
    }
    if (bad) { fail(name, index, bad); continue; }
    const maxHere = Math.max(0, ...perMap.values());
    if (maxHere > 10) { fail(name, index, `${maxHere} roaming bosses in one map`); continue; }
    // Non-worsening model budget: compare with the same world without the roaming pass.
    if (roamingMode) {
      const without = generate({ ...config, enemyNormalReplacement: overrides.enemyNormalReplacement === 'both' ? 'normals' : 'none', enemyBossChanceNormalSlots: 10, enemyRoamingBossSouls: 50, enemyRoamingBossesStayDead: false }, { gameCatalog: catalog });
      const before = finalModels(without);
      const after = finalModels(plan);
      let worse = null;
      for (const [mapId, models] of after) {
        stat.maxModels = Math.max(stat.maxModels, models.size);
        if (models.size > Math.max(30, before.get(mapId)?.size ?? 0)) worse = `${mapId}: ${models.size} models, ${before.get(mapId)?.size ?? 0} without the roaming pass`;
      }
      if (worse) { fail(name, index, `model budget worsened: ${worse}`); continue; }
      // The pass only overrides slots: every slot it did not take keeps exactly the placement it had.
      const takenSlots = new Set(roaming.map((placement) => placement.slot));
      const keptBefore = JSON.stringify(without.placements.enemies.filter((placement) => !takenSlots.has(placement.slot)));
      const keptAfter = JSON.stringify(plan.placements.enemies.filter((placement) => !takenSlots.has(placement.slot)));
      if (keptBefore !== keptAfter) { fail(name, index, 'slots outside the roaming pass changed'); continue; }
    }
    stat.ok += 1;
    stat.roamers += roaming.length;
    stat.withRoamers += roaming.length > 0 ? 1 : 0;
    stat.maxPerMap = Math.max(stat.maxPerMap, maxHere);
  }
  console.log(`${name.padEnd(36)} ok ${stat.ok}/${seeds}  roaming ${stat.roamers} (worlds with any ${stat.withRoamers})  max/map ${stat.maxPerMap}  maxModels ${stat.maxModels}  kinds ${stat.bossKinds.size}  souls ${[...stat.soulsSeen].slice(0, 4).join('/')}`);
}
console.log(failures === 0 ? 'ALL OK' : `${failures} failure(s)`);
process.exit(failures === 0 ? 0 : 1);
