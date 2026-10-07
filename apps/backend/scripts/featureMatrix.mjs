// Engine-level feature matrix (Part G): every combination of the placement features that can interact, on a real extracted catalog.
//
//   node apps/backend/scripts/featureMatrix.mjs <dsr-catalog.json> [--seeds 12]
//
// Factors (each a way the planners can touch the same slot or the same map budget):
//   type replacement        disabled | enabled
//   custom pool             none | a pool of 45 enemies
//   normal replacement      normals | bosses | both            (roaming bosses)
//   boss replacement        classic | bosses | normals | both  (boss sampling, boss bodies)
//   hostile NPCs            off | on
//   placement fit           fits | anywhere
//   difficulty              random + Easy Asylum | curve
// Every combination (384) is generated for `seeds` seeds. A combination the options refuse (hostile NPCs with a custom pool or without normal
// enemies) must be refused with the typed options error; a combination the pool cannot satisfy must fail with the typed custom pool error. Everything
// else must generate, deterministically, inside the model limit, slot-owned, with at most 12 hostile NPCs and 10 roaming bosses per map, with boss
// bodies only in the audited slots, and with the placement validation summary consistent with the placements.

import { readFileSync } from 'node:fs';
import { loadCombinedRandomizerCore } from '../dist/adapters/combinedRandomizerCore.js';

const [catalogPath, ...rest] = process.argv.slice(2);
if (!catalogPath) { console.error('Usage: node featureMatrix.mjs <dsr-catalog.json> [--seeds N]'); process.exit(2); }
const seeds = Number(rest.includes('--seeds') ? rest[rest.indexOf('--seeds') + 1] : 12);
const loaded = await loadCombinedRandomizerCore();
if (!loaded.ok) throw new Error('core does not load');
const { generate, defaultConfig, describeRegularEnemyPool } = loaded.core;
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const pool = describeRegularEnemyPool({ ...defaultConfig }, catalog).entries.map((entry) => entry.model).sort().filter((_, index) => index % 2 === 0).slice(0, 45);
const SUPPORTED_BODY_SLOTS = new Set(['m18_01_00_00:c2232_0000', 'm10_01_00_00:c2250_0000', 'm10_01_00_00:c2240_0000']);

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

const base = { ...defaultConfig, randomizeEnemies: true, randomizeBosses: true, randomizeItems: false, generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1', placementDiagnosticsLevel: 'summary', enemyReplacementChance: 100, secondGargoyle: 'replace', gwynNerf: 'medium' };
const stats = { combinations: 0, worlds: 0, refusedOptions: 0, refusedPool: 0, failures: 0, maxHostilePerMap: 0, maxRoamingPerMap: 0, maxModels: 0, hostile: 0, roaming: 0, bodies: 0, hostileAndRoamingWorlds: 0, bodiesAndHostileWorlds: 0 };
const failures = [];
const fail = (name, seed, message) => { stats.failures += 1; if (failures.length < 25) failures.push(`${name} #${seed}: ${message}`); };
const typed = (error, code) => error?.code === code;

for (const type of ['disabled', 'enabled'])
  for (const customPool of [null, pool])
    for (const normal of ['normals', 'bosses', 'both'])
      for (const bossMode of ['classic', 'bosses', 'normals', 'both'])
        for (const hostile of [false, true])
          for (const fit of ['fits', 'anywhere'])
            for (const difficulty of ['random-easy-asylum', 'curve']) {
              const name = `type=${type} pool=${customPool ? 'yes' : 'no'} normal=${normal} boss=${bossMode} hostile=${hostile ? 'on' : 'off'} fit=${fit} diff=${difficulty}`;
              const overrides = { enemyTypeReplacement: type, enemyCustomPool: customPool, enemyNormalReplacement: normal, enemyBossReplacement: bossMode, enemyHostileNpcs: hostile, enemyPlacementFit: fit, enemyDifficultyMode: difficulty };
              stats.combinations += 1;
              const mustRefuse = hostile && (customPool !== null || normal === 'bosses');
              for (let index = 0; index < seeds; index += 1) {
                const config = { ...base, ...overrides, seed: `matrix-${stats.combinations}-${index}` };
                let plan;
                try {
                  plan = generate(config, { gameCatalog: catalog });
                  if (generate(config, { gameCatalog: catalog }).placementHash !== plan.placementHash) { fail(name, index, 'not deterministic'); continue; }
                } catch (error) {
                  if (mustRefuse && typed(error, 'ENEMY_PLACEMENT_OPTIONS_INVALID')) { stats.refusedOptions += 1; continue; }
                  if (!mustRefuse && customPool !== null && (typed(error, 'ENEMY_PLACEMENT_CUSTOM_POOL_INVALID') || String(error.code).startsWith('ENEMY_PLACEMENT_CUSTOM_POOL') || typed(error, 'ENEMY_PLACEMENT_INFEASIBLE'))) { stats.refusedPool += 1; continue; }
                  fail(name, index, `${error.code ?? ''} ${String(error.message).slice(0, 160)}`);
                  continue;
                }
                if (mustRefuse) { fail(name, index, 'a combination the options must refuse was generated'); continue; }
                stats.worlds += 1;
                const roaming = plan.placements.enemies.filter((placement) => placement.compatibility === 'sampled-roaming-boss');
                const hostileNpcs = plan.placements.enemies.filter((placement) => placement.hostileNpc === true);
                const bodies = plan.placements.bosses.filter((placement) => placement.encounterBody === true);
                const summary = plan.validation.enemyPlacement;
                stats.hostile += hostileNpcs.length; stats.roaming += roaming.length; stats.bodies += bodies.length;
                if (roaming.length > 0 && hostileNpcs.length > 0) stats.hostileAndRoamingWorlds += 1;
                if (bodies.length > 0 && hostileNpcs.length > 0) stats.bodiesAndHostileWorlds += 1;
                if (roaming.length > 0 && normal === 'normals') fail(name, index, 'roaming bosses without the mode');
                if (hostileNpcs.length > 0 && !hostile) fail(name, index, 'hostile NPCs without the option');
                if (bodies.some((placement) => !SUPPORTED_BODY_SLOTS.has(placement.slot))) fail(name, index, 'a boss body outside the audited slots');
                if (bodies.length > 0 && !['normals', 'both'].includes(bossMode)) fail(name, index, 'boss bodies without the mode');
                const perMap = (list) => { const counts = new Map(); for (const placement of list) counts.set(placement.map, (counts.get(placement.map) ?? 0) + 1); return counts; };
                const hostileLive = hostileNpcs.filter((placement) => placement.forceCombatActivation === true || placement.passiveUntilAttacked === true);
                for (const count of perMap(hostileLive).values()) { stats.maxHostilePerMap = Math.max(stats.maxHostilePerMap, count); if (count > 12) fail(name, index, `${count} hostile NPCs in a map`); }
                for (const count of perMap(roaming).values()) { stats.maxRoamingPerMap = Math.max(stats.maxRoamingPerMap, count); if (count > 10) fail(name, index, `${count} roaming bosses in a map`); }
                for (const placement of [...hostileNpcs, ...roaming, ...bodies]) if (!Number.isInteger(placement.scaledNpcParamId)) fail(name, index, `${placement.slot} has no scaled row`);
                // The non-worsening rule: no map ends above max(30, the models it holds without the roaming pass) (the world planned without the pass is the
                // reference: the baseline of a map is what bosses, dragons and the slots that stay hold, and the Undead Parish starts above 30). A custom pool may use the one-model grace (10.5).
                const reference = normal === 'normals' ? plan : generate({ ...config, enemyNormalReplacement: normal === 'both' ? 'normals' : 'none', enemyBossChanceNormalSlots: 10, enemyRoamingBossSouls: 50, enemyRoamingBossesStayDead: false }, { gameCatalog: catalog });
                const before = finalModels(reference);
                for (const [mapId, models] of finalModels(plan)) {
                  stats.maxModels = Math.max(stats.maxModels, models.size);
                  if (models.size > Math.max(30, before.get(mapId)?.size ?? 0) + (customPool ? 1 : 0)) fail(name, index, `${mapId}: ${models.size} models, ${before.get(mapId)?.size ?? 0} without the roaming pass`);
                }
                if (summary.hostileNpcs !== hostileNpcs.length || summary.roamingBosses !== roaming.length || summary.bossBodies !== bodies.length) fail(name, index, 'the validation summary disagrees with the placements');
              }
            }
for (const line of failures) console.log(`FAIL ${line}`);
console.log(`${stats.combinations} combinations x ${seeds} seeds: ${stats.worlds} worlds generated, ${stats.refusedOptions} refused by the options (typed), ${stats.refusedPool} refused by the custom pool (typed), ${stats.failures} failures`);
console.log(`totals: hostile NPCs ${stats.hostile} (max ${stats.maxHostilePerMap} in a map), roaming bosses ${stats.roaming} (max ${stats.maxRoamingPerMap} in a map), boss bodies ${stats.bodies}, max models in a map ${stats.maxModels}; worlds with roaming bosses and hostile NPCs ${stats.hostileAndRoamingWorlds}, with boss bodies and hostile NPCs ${stats.bodiesAndHostileWorlds}`);
process.exit(stats.failures === 0 ? 0 : 1);
