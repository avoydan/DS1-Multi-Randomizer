// Finds master seeds for the QUICK smoke-test worlds (docs/SMOKE_TESTS.md): worlds in which the thing under test stands within a few
// meters of where the player wakes up in the Undead Asylum. Nothing here forces a placement: every candidate is a normal world made
// from normal settings and a normal master seed; the search only reads what the engine produced and keeps the seeds that put the
// target where a tester finds it at once. Run on a real extracted catalog (dsr-catalog.json), engine only, no game and no data tool:
//
//   node apps/backend/scripts/smokeWorldSearch.mjs <catalog.json> <test> [--from 1] [--to 2000] [--prefix smoke-q1-capra]
//
// <test>: any key of QUICK_SMOKE_RECIPES (packages/contracts/src/smokeTests.ts), for example quick-roaming-basic or quick-hostile-npc.
// Prints one JSON line per accepted seed. Several processes with disjoint --from/--to ranges can run side by side.

import { readFileSync } from 'node:fs';
import { loadCombinedRandomizerCore } from '../dist/adapters/combinedRandomizerCore.js';
import { mapCombinedRandomizerConfig } from '../dist/adapters/combinedRandomizerConfig.js';
import { currentGenerationProfile } from '../dist/core/generationProfiles.js';
import { deriveAttemptSeeds } from '../dist/core/seeds.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { QUICK_SMOKE_RECIPES, resolveScenarioSettings } from '@ds1tr/contracts';


// ---- what a tester finds at once ---------------------------------------------------------------------------------------------
// Where the player wakes up in the Undead Asylum (c0000_0000 of m18_01_00_00.msb) and the first-visit enemies: event 11810350 of
// the Asylum disables the entities 1810200-1810213 until the second visit and the entities 1810101-1810113 after it, so only an
// enemy with an entity id from 1810101 to 1810113 exists the first time (docs/SMOKE_TESTS.md, "How the targets were chosen").
const ASYLUM = 'm18_01_00_00';
const SPAWN = { x: -13.75, y: 184.75, z: -44.5 };
const metersFromSpawn = (slot) => Math.hypot(slot.position.x - SPAWN.x, slot.position.z - SPAWN.z);
const firstVisit = (slot) => slot.entityId >= 1810101 && slot.entityId <= 1810113;
const short = (id) => id.split(':')[1];
const NEAR = ['c2500_0005', 'c2500_0006', 'c2500_0007'];

function asylum({ plan, slotById }) {
  const rows = plan.placements.enemies.filter((entry) => entry.map === ASYLUM).map((entry) => {
    const slot = slotById.get(entry.slot);
    return { slot: short(entry.slot), model: entry.targetModelName, npc: entry.targetNpcParamId, meters: Number(metersFromSpawn(slot).toFixed(1)), firstVisit: firstVisit(slot), roaming: entry.compatibility === 'sampled-roaming-boss', souls: entry.roamingBossSouls ?? null, entity: slot.entityId };
  });
  return rows;
}

const ACCEPT = {
  'quick-roaming-basic': (context) => {
    const rows = asylum(context).filter((row) => row.roaming);
    if (rows.length !== 1 || !['c2500_0006', 'c2500_0007'].includes(rows[0].slot) || rows[0].model !== 'c2240') return null;
    return { roaming: rows };
  },
  'quick-roaming-souls': (context) => {
    const rows = asylum(context).filter((row) => row.roaming);
    if (rows.length !== 1 || !NEAR.includes(rows[0].slot) || rows[0].model !== 'c2250') return null;
    return { roaming: rows };
  },
  'quick-roaming-repeated': (context) => {
    const rows = asylum(context).filter((row) => row.roaming);
    const trio = NEAR.map((name) => rows.find((row) => row.slot === name));
    if (trio.some((row) => !row) || new Set(trio.map((row) => `${row.model}:${row.npc}`)).size !== 1 || !['c2240', 'c2250', 'c4100', 'c5270', 'c2360', 'c5280', 'c2231', 'c4500'].includes(trio[0].model)) return null;
    const others = rows.filter((row) => !NEAR.includes(row.slot));
    // A dummy slot (c2500_0004 / c2500_0008) is not a live enemy; a boss on one is invisible and says nothing.
    if (others.some((row) => ['c2500_0004', 'c2500_0008'].includes(row.slot)) || others.filter((row) => row.firstVisit).length > 1) return null;
    return { roaming: rows, othersFirstVisit: others.filter((row) => row.firstVisit).length, othersTotal: others.length };
  },
  'quick-sampling-asylum': (context) => {
    const rows = asylum(context).filter((row) => NEAR.includes(row.slot));
    const allowed = ['c3340', 'c2550', 'c2560', 'c2520', 'c2540']; // the legacy Easy Asylum list: what production puts in the Asylum
    if (rows.length !== 3 || new Set(rows.map((row) => row.model)).size !== 3 || rows.some((row) => !allowed.includes(row.model))) return null;
    return { near: rows };
  },
  'quick-encounter-body': ({ plan }) => {
    const body = plan.placements.bosses.find((entry) => entry.slot === `${ASYLUM}:c2232_0000` && entry.compatibility === 'sampled-encounter-body');
    if (!body || body.targetModelName !== 'c2560') return null;
    const others = plan.placements.bosses.filter((entry) => entry.compatibility === 'sampled-encounter-body').map((entry) => `${entry.slot.split(':')[1]}>${entry.targetModelName}`);
    return { bodies: others, source: body.sourceSlot, scaledNpcParamId: body.scaledNpcParamId, think: body.targetThinkParamId };
  },
  'quick-hostile-npc': ({ plan, slotById }) => {
    // Part F1: Andre of Astora as a hostile fighter in one of the two nearest first-visit Hollow slots; no other hostile NPC around it.
    const hostile = plan.placements.enemies.filter((entry) => entry.map === ASYLUM && entry.hostileNpc === true);
    const live = hostile.filter((entry) => !slotById.get(entry.slot).dummy);
    if (live.length !== 1) return null;
    const [entry] = live;
    const slot = slotById.get(entry.slot);
    if (!['c2500_0006', 'c2500_0007'].includes(short(entry.slot)) || entry.targetModelName !== 'c2640' || entry.passiveUntilAttacked || !firstVisit(slot) || entry.forceCombatActivation !== true) return null;
    return { slot: short(entry.slot), model: entry.targetModelName, meters: Number(metersFromSpawn(slot).toFixed(1)), entityId: entry.entityId, source: entry.sourceSlot, scaledNpcParamId: entry.scaledNpcParamId, think: entry.targetThinkParamId };
  },
  'quick-boss-asylum-demon': ({ plan }) => {
    const placement = plan.placements.bosses.find((entry) => entry.slot === `${ASYLUM}:c2232_0000`);
    if (!placement || !['c2250', 'c2240'].includes(placement.targetModelName)) return null;
    return { demonBecomes: placement.targetModelName, randomizedBossName: placement.randomizedBossName };
  },
};

const [catalogPath, testId, ...rest] = process.argv.slice(2);
const option = (name, fallback) => { const index = rest.indexOf(name); return index >= 0 ? rest[index + 1] : fallback; };
const recipe = QUICK_SMOKE_RECIPES[testId];
const accept = ACCEPT[testId];
if (!catalogPath || !recipe || !accept) { console.error(`Usage: node smokeWorldSearch.mjs <catalog.json> <${Object.keys(QUICK_SMOKE_RECIPES).join('|')}> [--from N --to N --prefix p]`); process.exit(2); }

const loaded = await loadCombinedRandomizerCore();
if (!loaded.ok) throw new Error('the engine does not load');
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const slotById = new Map([...catalog.enemySlots, ...catalog.bossSlots].map((slot) => [slot.id, slot]));
const profile = currentGenerationProfile();
const from = Number(option('--from', 1));
const to = Number(option('--to', 2000));
const prefix = option('--prefix', recipe.seedPrefix);
const settings = resolveScenarioSettings({ settings: recipe.settings });

let failures = 0;
for (let index = from; index <= to; index += 1) {
  const masterSeed = `${prefix}-${index}`;
  const prepared = prepareRun({ gamePath: 'X:/search', masterSeed, ...structuredClone(settings), output: { ...settings.output, generateSpoilerLog: false } });
  if (!prepared.ok) throw new Error(`${prepared.code}: ${prepared.technical}`);
  const { items, enemies } = prepared.config;
  const mapped = mapCombinedRandomizerConfig({
    gamePath: 'X:/search', workspacePath: 'X:/search/work', masterSeed, combinedSeed: deriveAttemptSeeds(masterSeed, 0, 0, profile.seedDerivation).combined,
    items, enemies, generateSpoilerLog: false, placementDiagnosticsLevel: 'summary', fogTrackedItems: null,
  });
  if (!mapped.ok) throw new Error(mapped.message);
  let plan;
  try { plan = loaded.core.generate(mapped.config, { gameCatalog: catalog }); } catch (error) { failures += 1; if (failures <= 3) console.error(`seed ${masterSeed}: ${error.code ?? ''} ${String(error.message).slice(0, 160)}`); continue; }
  const accepted = accept({ plan, slotById });
  if (accepted) console.log(JSON.stringify({ test: testId, masterSeed, ...accepted }));
}
console.error(`searched ${to - from + 1} seeds, ${failures} generation failures`);
