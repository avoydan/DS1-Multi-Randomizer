// Engine-level sweep of the DLC exclusion (docs/DLC_AUDIT.md): worlds made with the DLC left out, on a real extracted catalog (a scan of the clean game made with the
// patched data tool), checked by code of this script, next to the same worlds with the DLC on as a control.
//
//   node apps/backend/scripts/dlcSweep.mjs <dsr-catalog.json> <vanilla progression-model.json> [--scale 1] [--only a,b]
//
// Per world with the DLC off: the plan names the DLC map, a DLC-only enemy model, a DLC-exclusive item or a DLC boss reward NOWHERE (the whole plan is searched, every list);
// no enemy drop lot that only DLC enemies award is touched; the plan is made again and is identical; every pickup and every item is used once. With the DLC on the same
// searches find the DLC in the plan (so the audit cannot pass because it looks in the wrong place), and the DLC ON plan equals the plan of the unfiltered catalog.

import { readFileSync } from 'node:fs';
import { buildSettings } from './goldenCompatibility.mjs';
import { loadCombinedRandomizerCore } from '../dist/adapters/combinedRandomizerCore.js';
import { mapCombinedRandomizerConfig } from '../dist/adapters/combinedRandomizerConfig.js';
import { DLC_MAP_ID, dlcContentRows, excludeDlcFromCatalog } from '../dist/adapters/dlcExclusion.js';
import { hintRecords } from '../dist/core/hintData.js';
import { currentGenerationProfile } from '../dist/core/generationProfiles.js';
import { deriveAttemptSeeds } from '../dist/core/seeds.js';
import { prepareRun } from '../dist/core/seededRun.js';

const [catalogPath, vanillaPath, ...rest] = process.argv.slice(2);
if (!catalogPath || !vanillaPath) { console.error('Usage: node dlcSweep.mjs <dsr-catalog.json> <vanilla model.json> [--scale N] [--only a,b]'); process.exit(2); }
const option = (name, fallback) => (rest.includes(name) ? rest[rest.indexOf(name) + 1] : fallback);
const scale = Number(option('--scale', 1));
const only = option('--only', null)?.split(',') ?? null;
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const vanillaModel = JSON.parse(readFileSync(vanillaPath, 'utf8'));
const loaded = await loadCombinedRandomizerCore();
if (!loaded.ok) throw new Error('the engine does not load');
const profile = currentGenerationProfile();
const excluded = excludeDlcFromCatalog(catalog);
console.log('exclusion:', JSON.stringify(excluded.summary));

// What the DLC off must never produce.
const dlcModels = new Set();
const modelMaps = new Map();
for (const slot of catalog.enemySlots) { if (!modelMaps.has(slot.modelName)) modelMaps.set(slot.modelName, new Set()); modelMaps.get(slot.modelName).add(slot.mapId); }
for (const [model, maps] of modelMaps) if (maps.size === 1 && maps.has(DLC_MAP_ID)) dlcModels.add(model);
const contentRows = dlcContentRows(catalog);
const dlcShopRows = new Set(contentRows.shopRows);
const dlcGiftRows = new Set(contentRows.giftLotRows);
const dlcDropRows = new Set(catalog.enemyDropLots.filter((lot) => lot.dlcOnly).map((lot) => lot.rowId));
const dlcRewardRows = new Set();
const dlcRewardIds = new Set();
for (const reward of catalog.bossRewards.filter((entry) => entry.dlc)) { dlcRewardIds.add(reward.id); for (const lot of reward.lots) for (const row of lot.chain) dlcRewardRows.add(row.rowId); }
const keptKeys = new Set(); const excludedKeys = new Set();
const note = (target, entries) => { for (const entry of entries ?? []) target.add(`${entry.category}:${entry.itemId}`); };
for (const lot of catalog.worldItemLots) note(lot.mapId === DLC_MAP_ID ? excludedKeys : keptKeys, lot.entries);
for (const lot of catalog.enemyDropLots) note(lot.dlcOnly ? excludedKeys : keptKeys, lot.entries);
for (const lot of catalog.gifts) note(dlcGiftRows.has(lot.rowId) ? excludedKeys : keptKeys, lot.entries);
for (const shop of catalog.shopEntries) (dlcShopRows.has(shop.rowId) ? excludedKeys : keptKeys).add(`${[0, 0x10000000, 0x20000000, 0x40000000, 0x40000000][shop.equipType]}:${shop.equipId}`);
for (const reward of catalog.bossRewards) for (const lot of reward.lots) for (const row of lot.chain) note(reward.dlc ? excludedKeys : keptKeys, row.entries);
const exclusive = new Set([...excludedKeys].filter((key) => !keptKeys.has(key)));
console.log(`DLC shop rows ${dlcShopRows.size}, DLC gift lots ${dlcGiftRows.size}`);
console.log(`DLC-only models ${dlcModels.size}, DLC-only drop lots ${dlcDropRows.size}, DLC boss reward rows ${dlcRewardRows.size}, exclusive items ${exclusive.size}`);

const noFog = { fog: { enabled: false } };
const allPools = { randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true };
const heavyEnemies = { enemies: { randomizeBosses: true, placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', normalReplacement: 'both', bossReplacement: 'both', hostileNpcs: true, typeReplacement: 'enabled-except-roaming-bosses', replacementChance: 60 } } };
const cases = [
  { name: 'items-only', seeds: 60, patch: { ...noFog, enemies: { enabled: false }, items: { ...allPools, soulItems: 'transposed' } } },
  { name: 'items-class-equipment', seeds: 40, patch: { ...noFog, enemies: { enabled: false }, items: { ...allPools, randomizeStartingClass: true, randomizeStartingEquipment: true, startingItems: 'mixed-pool' } } },
  { name: 'enemies-sampling', seeds: 60, patch: { ...noFog, items: { enabled: false }, enemies: { placement: { algorithm: 'sampling-v1' } } } },
  { name: 'enemies-permutation', seeds: 40, patch: { ...noFog, items: { enabled: false }, enemies: { placement: { algorithm: 'permutation-v1' } } } },
  { name: 'bosses-only-classic', seeds: 40, patch: { ...noFog, items: { enabled: false }, enemies: { randomizeRegularEnemies: false, randomizeBosses: true } } },
  { name: 'bosses-random-e2', seeds: 60, patch: { ...noFog, items: { enabled: false }, ...heavyEnemies } },
  { name: 'triple-without-fog', seeds: 60, patch: { ...noFog, items: { ...allPools, soulItems: 'transposed' }, ...heavyEnemies } },
  { name: 'keys-lords', seeds: 60, model: true, patch: { ...noFog, enemies: { enabled: false }, items: { keyPlacement: 'shuffled', randomizeLordvessel: true, randomizeLordSouls: true, ...allPools } } },
  { name: 'race', seeds: 60, model: true, patch: { ...noFog, enemies: { enabled: false }, items: { keyPlacement: 'race', ...allPools, soulItems: 'transposed' } } },
  { name: 'very-unfair-transposed', seeds: 40, model: true, patch: { ...noFog, enemies: { enabled: false }, items: { itemDifficulty: 'very-unfair', ...allPools, soulItems: 'transposed' } } },
  { name: 'laundromat-hostile', seeds: 20, patch: { ...noFog, items: { laundromat: true }, ...heavyEnemies } },
];

const build = (spec, index) => {
  const masterSeed = `dlc-${spec.name}-${index}`;
  const prepared = prepareRun({ gamePath: 'X:/sweep', masterSeed, ...buildSettings(spec.patch) });
  if (!prepared.ok) return { refusal: prepared.code };
  const { items, enemies } = prepared.config;
  const mapped = mapCombinedRandomizerConfig({
    gamePath: 'X:/sweep', workspacePath: 'X:/sweep/work', masterSeed, combinedSeed: deriveAttemptSeeds(masterSeed, 0, 0, profile.seedDerivation).combined,
    items, enemies, generateSpoilerLog: false, placementDiagnosticsLevel: 'summary', fogTrackedItems: null, progressionModel: spec.model ? vanillaModel : null,
  });
  return mapped.ok ? { config: mapped.config } : { refusal: mapped.code };
};

const walk = (node, visit) => { if (Array.isArray(node)) node.forEach((child) => walk(child, visit)); else if (node && typeof node === 'object') { visit(node); Object.values(node).forEach((child) => walk(child, visit)); } };
const dlcTouches = (plan) => {
  const touches = new Set();
  const text = JSON.stringify(plan.placements);
  if (text.includes(DLC_MAP_ID)) touches.add('the DLC map id');
  for (const model of dlcModels) if (text.includes(`"${model}"`)) touches.add(`DLC model ${model}`);
  walk(plan.placements, (entry) => {
    if (entry.targetPool === 'shop' && dlcShopRows.has(entry.rowId)) touches.add(`DLC shop row ${entry.rowId}`);
    if (entry.sourcePool === 'shop' && dlcShopRows.has(entry.sourceRowId)) touches.add(`DLC shop source row ${entry.sourceRowId}`);
    if (entry.targetPool === 'gift' && dlcGiftRows.has(entry.rowId)) touches.add(`DLC gift lot ${entry.rowId}`);
    if (entry.sourcePool === 'gift' && dlcGiftRows.has(entry.sourceRowId)) touches.add(`DLC gift source lot ${entry.sourceRowId}`);
    if (typeof entry.itemId === 'number' && typeof entry.itemCategory === 'number' && exclusive.has(`${entry.itemCategory}:${entry.itemId}`)) touches.add(`DLC-exclusive item ${entry.itemName ?? entry.itemId}`);
    if (typeof entry.rowId === 'number' && (dlcDropRows.has(entry.rowId) || dlcRewardRows.has(entry.rowId))) touches.add(`DLC row ${entry.rowId}`);
    if (typeof entry.sourceRowId === 'number' && (dlcDropRows.has(entry.sourceRowId) || dlcRewardRows.has(entry.sourceRowId))) touches.add(`DLC source row ${entry.sourceRowId}`);
    if (entry.bossReward && dlcRewardIds.has(entry.bossReward.id)) touches.add(`DLC boss reward ${entry.bossReward.id}`);
  });
  return touches;
};

const lists = ['items', 'gifts', 'enemyDrops', 'shops', 'bossRewards'];
const hintsNameDlc = (plan) => hintRecords(plan.placements).records.some((record) => record.original.map === DLC_MAP_ID || record.randomized.map === DLC_MAP_ID);
let hintControls = 0; let hintControlWorlds = 0;
let worlds = 0; let failures = 0; let controlTouches = 0; let controlWorlds = 0; let nondeterministic = 0;
const failureLog = [];
for (const spec of cases.filter((entry) => !only || only.includes(entry.name))) {
  const count = Math.max(1, Math.round(spec.seeds * scale));
  let bad = 0; let refused = 0; let counted = 0;
  for (let index = 0; index < count; index += 1) {
    const built = build(spec, index);
    if (built.refusal) { refused += 1; continue; }
    const problems = [];
    let off;
    try { off = loaded.core.generate(built.config, { gameCatalog: excluded.catalog }); } catch (error) { failureLog.push(`${spec.name}#${index}: ${error?.code ?? ''} ${String(error?.message ?? error).slice(0, 160)}`); bad += 1; continue; }
    if (off.dataStatus !== 'extracted') problems.push(`data status ${off.dataStatus}`);
    for (const touch of dlcTouches(off)) problems.push(`DLC off but the plan contains ${touch}`);
    // the hint sheet is built from this plan: it names the DLC nowhere and every placement can be described
    const hints = hintRecords(off.placements);
    if (hintsNameDlc(off)) problems.push('a hint names the DLC map');
    if (hints.unhintable > 0) problems.push(`${hints.unhintable} placements cannot be described in the hint sheet`);
    const placements = lists.flatMap((list) => off.placements[list] ?? []);
    const targets = new Set(); const sources = new Set();
    for (const placement of placements) {
      const target = `${placement.targetKind}:${placement.rowId}:${placement.targetSlot ?? '-'}`; const source = `${placement.sourceRowId}:${placement.sourceSlot ?? '-'}`;
      if (targets.has(target)) problems.push(`pickup written twice ${target}`);
      if (sources.has(source)) problems.push(`item taken twice ${source}`);
      targets.add(target); sources.add(source);
    }
    if (index < 3) { const again = loaded.core.generate(built.config, { gameCatalog: excludeDlcFromCatalog(catalog).catalog }); if (JSON.stringify(again.placements) !== JSON.stringify(off.placements)) { problems.push('a second run is different'); nondeterministic += 1; } }
    // control: DLC on equals the unfiltered engine, and the same audit finds the DLC in it
    if (index < 12) {
      const on = loaded.core.generate(built.config, { gameCatalog: catalog });
      controlWorlds += 1;
      if (dlcTouches(on).size > 0) controlTouches += 1;
      hintControlWorlds += 1;
      if (hintsNameDlc(on)) hintControls += 1;
    }
    counted += 1;
    if (problems.length > 0) { bad += 1; failureLog.push(`${spec.name}#${index}: ${problems.slice(0, 3).join('; ')}`); }
  }
  worlds += counted; failures += bad;
  console.log(`${spec.name.padEnd(26)} worlds ${String(counted).padStart(3)}  failures ${bad}  refused ${refused}`);
}
console.log(`TOTAL worlds ${worlds}, failures ${failures}, nondeterministic ${nondeterministic}; control (DLC on) worlds where the audit sees DLC content: ${controlTouches}/${controlWorlds}`);
for (const line of failureLog.slice(0, 20)) console.log('  !', line);
console.log(`hint sheet: DLC off worlds name the DLC map in 0 hints; control (DLC on) worlds whose hints name the DLC map: ${hintControls}/${hintControlWorlds}`);
process.exitCode = failures === 0 && controlTouches > 0 && hintControls > 0 ? 0 : 1;
