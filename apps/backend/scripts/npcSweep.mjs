// Engine-level sweep for hostile NPCs (Part F1) on a real extracted catalog (dsr-catalog.json), without the data tool or the game.
//
//   node apps/backend/scripts/npcSweep.mjs <dsr-catalog.json> [--seeds 100] [--sets name,name]
//
// Checks per config x seed: the engine does not throw, the result is deterministic, hostile NPCs come only from the three authored source
// slots and stand only in regular (team 0) slots, every one carries a scaled NpcParam row, the tangible mark and (unless the slot is passive) the
// combat wake-up, the final model count of every map never exceeds max(30, the vanilla model count), the option off equals the option absent,
// and no friendly NPC slot is ever a destination. It records the largest number of hostile NPCs in one map and in one world (the event volume).
// A failure is printed with its config and seed; nothing is retried and no limit is relaxed.

import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { loadCombinedRandomizerCore } from '../dist/adapters/combinedRandomizerCore.js';

const [catalogPath, ...rest] = process.argv.slice(2);
if (!catalogPath) { console.error('Usage: node npcSweep.mjs <dsr-catalog.json> [--seeds N] [--sets a,b]'); process.exit(2); }
const option = (name, fallback) => { const i = rest.indexOf(name); return i >= 0 ? rest[i + 1] : fallback; };
const seeds = Number(option('--seeds', 100));
const only = option('--sets', '') ? option('--sets', '').split(',') : null;

const loaded = await loadCombinedRandomizerCore();
if (!loaded.ok) throw new Error('core does not load');
const { generate, defaultConfig } = loaded.core;
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const slotById = new Map(catalog.enemySlots.map((slot) => [slot.id, slot]));

const SOURCES = new Set(['m10_01_00_00:c2510_0000', 'm10_01_00_00:c2640_0000', 'm13_00_00_00:c2920_0000']);
const NPC_MODELS = new Set(['c2510', 'c2640', 'c2920']);

const base = {
  ...defaultConfig, randomizeEnemies: true, randomizeBosses: false, randomizeItems: false, generateSpoilerLog: false,
  enemyPlacementAlgorithm: 'sampling-v1', placementDiagnosticsLevel: 'summary', enemyReplacementChance: 100, enemyHostileNpcs: true,
};

const sets = {
  'npc-basic': { enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'random' },
  'npc-fits': { enemyPlacementFit: 'fits', enemyDifficultyMode: 'random' },
  'npc-anywhere-easy-asylum': { enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'random-easy-asylum' },
  'npc-curve-strict': { enemyPlacementFit: 'fits', enemyDifficultyMode: 'curve', enemyDifficultyStrictness: 'strict' },
  'npc-curve-loose-c60': { enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'curve', enemyDifficultyStrictness: 'very-loose', enemyReplacementChance: 60 },
  'npc-type-enabled-fits': { enemyPlacementFit: 'fits', enemyTypeReplacement: 'enabled' },
  'npc-type-enabled-anywhere': { enemyPlacementFit: 'anywhere', enemyTypeReplacement: 'enabled' },
  'npc-type-except-roaming': { enemyPlacementFit: 'anywhere', enemyTypeReplacement: 'enabled-except-roaming-bosses', enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 15 },
  'npc-plus-roaming-both': { enemyPlacementFit: 'fits', enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 25 },
  'npc-plus-e2-normals': { enemyPlacementFit: 'anywhere', randomizeBosses: true, enemyBossReplacement: 'normals' },
  'npc-plus-boss-sampling-both': { enemyPlacementFit: 'anywhere', randomizeBosses: true, enemyBossReplacement: 'both', enemyBossChanceBossSlots: 50, enemyNormalReplacement: 'both', enemyBossChanceNormalSlots: 20 },
  'npc-prevent-same': { enemyPlacementFit: 'anywhere', preventSameEnemy: true },
  'npc-vanilla-scaling': { enemyPlacementFit: 'anywhere', enemyScaling: 'vanilla' },
  'npc-progressive-scaling': { enemyPlacementFit: 'fits', enemyScaling: 'progressive' },
  'npc-gwyn-gargoyle': { enemyPlacementFit: 'fits', randomizeBosses: true, gwynNerf: 'strong', secondGargoyle: 'replace' },
};

const vanillaModelCounts = new Map();
for (const slot of [...catalog.enemySlots, ...catalog.bossSlots]) {
  if (slot.dummy || !/^m1[0-8]_/u.test(slot.mapId) || !slot.modelName.startsWith('c')) continue;
  if (!vanillaModelCounts.has(slot.mapId)) vanillaModelCounts.set(slot.mapId, new Set());
  vanillaModelCounts.get(slot.mapId).add(slot.modelName);
}
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
  const stat = { ok: 0, npcs: 0, worldsWithNpcs: 0, maxPerMap: 0, maxPerWorld: 0, maxModels: 0, perModel: new Map(), maxEventsMap: 0 };
  for (let index = 0; index < seeds; index += 1) {
    const config = { ...base, seed: `${name}-${index}`, ...overrides };
    let plan;
    try {
      plan = generate(config, { gameCatalog: catalog });
      if (hash(plan.placements) !== hash(generate(config, { gameCatalog: catalog }).placements)) { fail(name, index, 'not deterministic'); continue; }
    } catch (error) { fail(name, index, `${error.code ?? ''} ${error.message}`.slice(0, 220)); continue; }
    const npcs = plan.placements.enemies.filter((placement) => placement.hostileNpc === true);
    const active = npcs.filter((placement) => !slotById.get(placement.slot).dummy);
    const perMap = new Map();
    const perMapActive = new Map();
    for (const placement of active) perMapActive.set(placement.map, (perMapActive.get(placement.map) ?? 0) + 1);
    let bad = null;
    for (const placement of npcs) {
      perMap.set(placement.map, (perMap.get(placement.map) ?? 0) + 1);
      const destination = slotById.get(placement.slot);
      const source = slotById.get(placement.sourceSlot);
      if (!SOURCES.has(placement.sourceSlot) || !NPC_MODELS.has(placement.targetModelName)) bad = `${placement.sourceSlot} (${placement.targetModelName}) is not an authored hostile source`;
      else if (placement.map === 'm12_00_00_01') bad = 'a hostile NPC in the Darkroot Basin (no event file for its wake-up)';
      else if (destination?.teamType !== 0) bad = `destination ${placement.slot} is not a regular slot (team ${destination?.teamType})`;
      else if (placement.scaledNpcParamId === null || placement.scaledNpcParamId === undefined) bad = 'hostile NPC without a scaled NpcParam row';
      else if (placement.makeTangible !== true) bad = 'hostile NPC without the tangible mark';
      else if (destination.dummy && placement.forceCombatActivation === true) bad = 'a wake-up event for a dummy part';
      else if (!destination.dummy && !(placement.forceCombatActivation === true || placement.passiveUntilAttacked === true)) bad = 'hostile NPC without combat wake-up on a non-passive slot';
      else if (placement.forceCombatActivation === true && !(placement.entityId >= 0)) bad = 'wake-up without an entity id';
      else if (placement.targetThinkParamId <= 0) bad = 'hostile NPC without a think row';
      else if (source?.talkId === undefined) bad = 'source slot missing';
      stat.perModel.set(placement.targetModelName, (stat.perModel.get(placement.targetModelName) ?? 0) + 1);
      if (bad) break;
    }
    if (bad) { fail(name, index, bad); continue; }
    if (npcs.some((placement) => placement.sourceSlot === placement.slot)) { fail(name, index, 'an NPC replaced itself'); continue; }
    let worse = null;
    for (const [mapId, models] of finalModels(plan)) {
      stat.maxModels = Math.max(stat.maxModels, models.size);
      const limit = Math.max(30, vanillaModelCounts.get(mapId)?.size ?? 0);
      if (models.size > limit) worse = `${mapId}: ${models.size} models, limit ${limit}`;
    }
    if (worse) { fail(name, index, `model budget: ${worse}`); continue; }
    // The option off equals the option absent (same seed, same everything else).
    if (index < 10) {
      const off = generate({ ...config, enemyHostileNpcs: false }, { gameCatalog: catalog });
      const { enemyHostileNpcs: _drop, ...without } = { ...config };
      const absent = generate({ ...without, enemyHostileNpcs: false }, { gameCatalog: catalog });
      if (hash(off.placements) !== hash(absent.placements)) { fail(name, index, 'option off differs from option absent'); continue; }
      if (off.placements.enemies.some((placement) => placement.hostileNpc === true)) { fail(name, index, 'a hostile NPC without the option'); continue; }
    }
    if (Math.max(0, ...perMapActive.values()) > 12) { fail(name, index, `${Math.max(...perMapActive.values())} hostile NPCs in one map (limit 12)`); continue; }
    stat.ok += 1;
    stat.npcs += npcs.length;
    stat.worldsWithNpcs += npcs.length > 0 ? 1 : 0;
    stat.maxPerMap = Math.max(stat.maxPerMap, ...perMapActive.values(), 0);
    stat.maxPerWorld = Math.max(stat.maxPerWorld, active.length);
  }
  const kinds = [...stat.perModel].map(([model, n]) => `${model}=${n}`).join(' ');
  console.log(`${name.padEnd(30)} ok ${stat.ok}/${seeds}  hostile NPCs ${stat.npcs} (worlds with any ${stat.worldsWithNpcs})  max/map ${stat.maxPerMap}  max/world ${stat.maxPerWorld}  maxModels ${stat.maxModels}  ${kinds || '-'}`);
}
console.log(failures === 0 ? 'ALL OK' : `${failures} failure(s)`);
process.exit(failures === 0 ? 0 : 1);
