// Engine-level sweep of the item modes of Part I (docs/RACE_MODE.md, docs/ITEM_DIFFICULTY_MODEL.md, docs/LAUNDROMAT_MIXUP.md): Race Mode, Race Mode +, the item
// difficulties and the Laundromat Mixup, made into engine configurations the way the application does it (prepareRun + mapCombinedRandomizerConfig), on a real
// extracted catalog, without the data tool or the game. The real data tool and FogHost are covered by placementMatrix.mjs (set item-modes-h); this sweep is the volume.
//
//   node apps/backend/scripts/itemModesSweep.mjs <dsr-catalog.json> <vanilla model.json> <race-plus model.json> [--scale 1] [--only name,name] [--fog-data-root <dir>]
//
// Checks per world, each by code of this script (nothing is taken from the engine's own verdict): the placements are a permutation (every pickup written once, every
// item taken once), every key placed once, the world is completable and the prison escapable under the model of the mode (re-derived from the placements and the catalog),
// every key and race item lies in a race destination (re-derived from the scan) in Race and Race +, no key lies in a gift, shop or enemy drop, no DLC-restricted item lies
// in the DLC, the Laundromat plan only names pieces NPCs already wear and leaves hollowed rows alone, and a second run of the first three seeds is identical.
// Difficulty modes report the share of items of each tier that end in each tier of location (after every repair ran), next to the Fair baseline.

import { readFileSync } from 'node:fs';
import { buildSettings } from './goldenCompatibility.mjs';
import { loadCombinedRandomizerCore } from '../dist/adapters/combinedRandomizerCore.js';
import { mapCombinedRandomizerConfig } from '../dist/adapters/combinedRandomizerConfig.js';
import { readFogItemRules } from '../dist/adapters/fogItemRules.js';
import { currentGenerationProfile } from '../dist/core/generationProfiles.js';
import { deriveAttemptSeeds } from '../dist/core/seeds.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { validateRandomizerConfig } from '../dist/core/configValidation.js';
import { pathToFileURL, fileURLToPath } from 'node:url';

const [catalogPath, vanillaPath, racePlusPath, ...rest] = process.argv.slice(2);
if (!catalogPath || !vanillaPath || !racePlusPath) { console.error('Usage: node itemModesSweep.mjs <dsr-catalog.json> <vanilla model.json> <race-plus model.json> [--scale N] [--only a,b] [--fog-data-root dir]'); process.exit(2); }
const option = (name, fallback) => (rest.includes(name) ? rest[rest.indexOf(name) + 1] : fallback);
const scale = Number(option('--scale', 1));
const only = option('--only', null)?.split(',') ?? null;
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const models = { vanilla: JSON.parse(readFileSync(vanillaPath, 'utf8')), 'race-plus': JSON.parse(readFileSync(racePlusPath, 'utf8')) };
const fogRules = option('--fog-data-root', null) ? await readFogItemRules(option('--fog-data-root', null)) : null;
const loaded = await loadCombinedRandomizerCore();
if (!loaded.ok) throw new Error('the engine does not load');
const keysModule = await import(pathToFileURL(fileURLToPath(new URL('../../../engines/combined/vendor/src/core/keys.js', import.meta.url))).href);
const difficultyModule = await import(pathToFileURL(fileURLToPath(new URL('../../../engines/combined/vendor/src/core/difficulty.js', import.meta.url))).href);
const profile = currentGenerationProfile();

const noEnemies = { enemies: { enabled: false } };
const noFog = { fog: { enabled: false } };
const withFog = { fog: { enabled: true } };
const pools = { randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true };
const cases = [
  { name: 'race', seeds: 300, patch: { ...noEnemies, ...noFog, items: { keyPlacement: 'race' } } },
  { name: 'race-lords-transposed', seeds: 150, patch: { ...noEnemies, ...noFog, items: { keyPlacement: 'race', randomizeLordvessel: true, randomizeLordSouls: true, soulItems: 'transposed' } } },
  { name: 'race-plus', seeds: 300, patch: { ...noEnemies, ...noFog, items: { keyPlacement: 'race-plus' } } },
  { name: 'race-plus-lords', seeds: 150, patch: { ...noEnemies, ...noFog, items: { keyPlacement: 'race-plus', randomizeLordvessel: true, randomizeLordSouls: true } } },
  { name: 'fair', seeds: 200, measure: true, patch: { ...noEnemies, ...noFog, items: { itemDifficulty: 'fair', ...pools, soulItems: 'transposed' } } },
  { name: 'unfair', seeds: 200, patch: { ...noEnemies, ...noFog, items: { itemDifficulty: 'unfair', ...pools, soulItems: 'transposed' } } },
  { name: 'very-unfair', seeds: 200, patch: { ...noEnemies, ...noFog, items: { itemDifficulty: 'very-unfair', ...pools, soulItems: 'transposed' } } },
  { name: 'race-fair', seeds: 200, patch: { ...noEnemies, ...noFog, items: { keyPlacement: 'race', itemDifficulty: 'fair', ...pools } } },
  { name: 'race-unfair', seeds: 200, patch: { ...noEnemies, ...noFog, items: { keyPlacement: 'race', itemDifficulty: 'unfair', ...pools } } },
  { name: 'race-very-unfair', seeds: 200, patch: { ...noEnemies, ...noFog, items: { keyPlacement: 'race', itemDifficulty: 'very-unfair', ...pools, soulItems: 'transposed' } } },
  { name: 'race-plus-fair', seeds: 200, patch: { ...noEnemies, ...noFog, items: { keyPlacement: 'race-plus', itemDifficulty: 'fair', ...pools } } },
  { name: 'race-plus-very-unfair-refused', seeds: 200, refused: true, patch: { ...noEnemies, ...noFog, items: { keyPlacement: 'race-plus', itemDifficulty: 'very-unfair' } } },
  { name: 'shuffled-very-unfair-lords', seeds: 200, patch: { ...noEnemies, ...noFog, items: { keyPlacement: 'shuffled', itemDifficulty: 'very-unfair', randomizeLordvessel: true, randomizeLordSouls: true, ...pools } } },
  { name: 'race-fog', seeds: 200, fog: true, patch: { ...noEnemies, ...withFog, items: { keyPlacement: 'race' } } },
  { name: 'race-plus-fog', seeds: 200, fog: true, patch: { ...noEnemies, ...withFog, items: { keyPlacement: 'race-plus', randomizeLordvessel: true, randomizeLordSouls: true } } },
  { name: 'difficulty-fog', seeds: 200, fog: true, patch: { ...noEnemies, ...withFog, items: { itemDifficulty: 'very-unfair', ...pools } } },
  { name: 'triple-modes', seeds: 200, fog: true, patch: { fog: { enabled: true }, items: { keyPlacement: 'race', randomizeLordvessel: true, randomizeLordSouls: true, itemDifficulty: 'unfair', soulItems: 'transposed', laundromat: true, ...pools } } },
  { name: 'laundromat', seeds: 200, patch: { ...noEnemies, ...noFog, items: { laundromat: true } } },
  { name: 'laundromat-fashion-off', seeds: 100, patch: { ...noEnemies, ...noFog, items: { laundromat: true, fashionSouls: false } } },
];

const pieceSlots = ['head', 'chest', 'hands', 'legs'];
const lists = ['items', 'gifts', 'enemyDrops', 'shops', 'bossRewards'];
const raceChests = new Set((catalog.treasures ?? []).filter((entry) => entry.model === 'o0110').map((entry) => entry.rowId));
const embers = new Set([800, 801, 802, 806, 807, 808, 809, 810, 812, 813, 2607, 2018, 2017]);
const keyIds = new Set(models.vanilla.keys.map((key) => key.id));
const vanillaKeyRows = new Set();
const noteRows = (rows) => { for (const row of rows) for (const entry of row.entries ?? []) if (keyIds.has(`${entry.equipType}:${entry.itemId}`) || (entry.equipType === 3 && embers.has(entry.itemId))) vanillaKeyRows.add(row.rowId); };
noteRows(catalog.worldItemLots ?? []); noteRows(catalog.gifts ?? []);
for (const reward of catalog.bossRewards ?? []) for (const lot of reward.lots) noteRows(lot.chain);
const restrictedForDlc = (placement) => /(?:Titanite|\bEmber\b)/iu.test(placement.itemName) || placement.progression === true || ['Havel\'s Ring', 'Fire Keeper Soul', 'Rusted Iron Ring'].includes(placement.itemName);
const worn = Object.fromEntries(pieceSlots.map((slot) => [slot, new Set((catalog.npcArmor ?? []).map((row) => row[slot]).filter((id) => id > 0))]));

const buildConfig = (spec, index, extra = {}) => {
  const masterSeed = `modes-${spec.name}-${index}`;
  const prepared = prepareRun({ gamePath: 'X:/sweep', masterSeed, ...buildSettings(spec.patch) });
  if (!prepared.ok) return { refusal: `${prepared.code}` };
  const { items, enemies, fog } = prepared.config;
  const mode = items.keyPlacement ?? 'vanilla';
  const engineMode = mode === 'race-plus' && fog.enabled ? 'race' : mode;
  const wantsModel = engineMode !== 'vanilla' || (items.itemDifficulty ?? 'fair') !== 'fair' || Boolean(extra.measureTiers);
  const mapped = mapCombinedRandomizerConfig({
    gamePath: 'X:/sweep', workspacePath: 'X:/sweep/work', masterSeed, combinedSeed: deriveAttemptSeeds(masterSeed, 0, 0, profile.seedDerivation).combined,
    items: engineMode === mode ? items : { ...items, keyPlacement: engineMode }, enemies, generateSpoilerLog: false, placementDiagnosticsLevel: 'summary',
    fogTrackedItems: fog.enabled ? fogRules : null,
    progressionModel: wantsModel ? models[engineMode === 'race-plus' ? 'race-plus' : 'vanilla'] : null,
  });
  if (!mapped.ok) return { refusal: mapped.code };
  return { config: { ...mapped.config, ...extra, ...(extra.measureTiers ? { progressionModel: models.vanilla } : {}) }, prepared, engineMode, masterSeed };
};

const audit = (spec, built, plan) => {
  const problems = [];
  const placements = lists.flatMap((list) => plan.placements[list] ?? []);
  const targets = new Set(); const sources = new Set();
  for (const placement of placements) {
    const target = `${placement.targetKind}:${placement.rowId}:${placement.targetSlot ?? '-'}`;
    const source = `${placement.sourceRowId}:${placement.sourceSlot ?? '-'}`;
    if (targets.has(target)) problems.push(`pickup written twice ${target}`);
    if (sources.has(source)) problems.push(`item taken twice ${source}`);
    targets.add(target); sources.add(source);
    if (placement.randomizedLocation?.map === 'm12_01_00_00' && restrictedForDlc(placement)) problems.push(`restricted item in the DLC: ${placement.itemName}`);
  }
  const keyed = placements.filter((placement) => placement.keyName);
  if (new Set(keyed.map((placement) => placement.keyName)).size !== keyed.length) problems.push('a key was placed twice');
  const mode = built.engineMode;
  if (mode !== 'vanilla') {
    const diagnostics = plan.validation.keyPlacement;
    if (diagnostics.mode !== mode) problems.push(`the mode reported is ${diagnostics.mode}, not ${mode}`);
    // The world under the model of the mode, from the placements: where every key lies by the lot area of its pickup.
    const prepared = keysModule.prepareModel(models[mode === 'race-plus' ? 'race-plus' : 'vanilla'], mode === 'race-plus' ? 'race-plus' : 'vanilla');
    const known = new Set([...(catalog.worldItemLots ?? []).map((lot) => lot.rowId), ...(catalog.gifts ?? []).map((lot) => lot.rowId), ...((catalog.bossRewards ?? []).flatMap((reward) => reward.lots.flatMap((lot) => lot.chain.map((row) => row.rowId))))]);
    const rowArea = keysModule.coveredRowAreas(prepared, known);
    const areas = new Map(prepared.raw.keys.map((key) => [key.name, key.area]));
    for (const placement of keyed) { const area = rowArea.get(placement.rowId); if (!area) problems.push(`${placement.keyName} lies in an uncovered row`); else areas.set(placement.keyName, area); }
    const check = keysModule.checkCompletable(prepared, areas);
    if (!check.completable || !check.prisonEscapable) problems.push(`not completable: ${check.unvisited.slice(0, 3).join(',')} prison=${check.prisonEscapable}`);
    for (const placement of keyed) if (['gift', 'shop', 'enemy'].includes(placement.targetPool)) problems.push(`${placement.keyName} lies in a ${placement.targetPool}`);
    if (mode === 'race' || mode === 'race-plus') {
      for (const placement of placements.filter((entry) => entry.keyName || entry.raceItem)) {
        if (!(placement.targetPool === 'boss' || raceChests.has(placement.rowId) || vanillaKeyRows.has(placement.rowId))) problems.push(`${placement.itemName} is outside the race destinations (row ${placement.rowId})`);
      }
      if (placements.filter((entry) => entry.raceItem).length < 1) problems.push('no race item was placed');
    } else if (placements.some((entry) => entry.raceItem)) problems.push('a race item in a mode without race items');
  }
  // A "random only" item that opens an area (Cast Light) must not lie in the area it opens (the guard of the modes that came with Part I).
  if ((built.config.itemDifficulty ?? 'fair') !== 'fair' || mode === 'race' || mode === 'race-plus') {
    const prepared = keysModule.prepareModel(models[mode === 'race-plus' ? 'race-plus' : 'vanilla'], mode === 'race-plus' ? 'race-plus' : 'vanilla');
    const known = new Set((catalog.worldItemLots ?? []).map((lot) => lot.rowId));
    const rowArea = keysModule.coveredRowAreas(prepared, known);
    for (const key of prepared.raw.keys.filter((entry) => (entry.tags ?? []).includes('randomonly'))) {
      const behind = keysModule.areasBehindKey(prepared, key.name);
      for (const placement of placements) if (`${placement.equipType === 4 ? 3 : placement.equipType}:${placement.itemId}` === key.id && behind.has(rowArea.get(placement.rowId))) problems.push(`${key.name} lies behind itself`);
    }
  }
  if (built.config.laundromat) {
    const rows = plan.placements.laundromat ?? [];
    if (rows.length === 0) problems.push('the Laundromat Mixup planned nothing');
    const original = new Map((catalog.npcArmor ?? []).map((row) => [row.id, row]));
    for (const row of rows) {
      const before = original.get(row.id);
      if (!before || row.id < 6000) { problems.push(`row ${row.id} is no NPC row`); continue; }
      for (const slot of pieceSlots) {
        if (before[slot] >= 2_000_000 || row[slot] >= 2_000_000) problems.push(`row ${row.id}: a hollowed piece`);
        if (before[slot] <= 0 && row[slot] !== before[slot]) problems.push(`row ${row.id}: an empty slot was filled`);
        if (row[slot] > 0 && !worn[slot].has(row[slot])) problems.push(`row ${row.id}: ${slot} ${row[slot]} is worn by no NPC`);
      }
    }
  } else if (plan.placements.laundromat) problems.push('a Laundromat plan without the option');
  return problems;
};

const summary = [];
for (const spec of cases.filter((entry) => !only || only.includes(entry.name))) {
  const seeds = Math.max(1, Math.round(spec.seeds * scale));
  const stats = { case: spec.name, seeds, ok: 0, refused: 0, failed: {}, problems: [], nonDeterministic: 0, maxAttempts: 0, keysMoved: 0, tiers: {}, laundromatRows: 0 };
  for (let index = 1; index <= seeds; index += 1) {
    const built = buildConfig(spec, index, spec.measure ? { measureTiers: true } : {});
    if (built.refusal) {
      if (spec.refused) stats.refused += 1; else stats.failed[built.refusal] = (stats.failed[built.refusal] ?? 0) + 1;
      continue;
    }
    if (spec.refused) { stats.failed['accepted but must be refused'] = (stats.failed['accepted but must be refused'] ?? 0) + 1; continue; }
    let plan;
    try { plan = loaded.core.generate(built.config, { gameCatalog: catalog }); } catch (error) { const code = error.code ?? String(error.message).slice(0, 80); stats.failed[code] = (stats.failed[code] ?? 0) + 1; continue; }
    stats.ok += 1;
    const problems = audit(spec, built, plan);
    for (const problem of problems.slice(0, 2)) if (stats.problems.length < 8) stats.problems.push(`#${index}: ${problem}`);
    stats.maxAttempts = Math.max(stats.maxAttempts, plan.validation.keyPlacement?.attempts ?? 0);
    stats.keysMoved += plan.validation.keyPlacement?.keysMoved ?? 0;
    stats.laundromatRows += plan.placements.laundromat?.length ?? 0;
    for (const [key, value] of Object.entries(plan.validation.itemDifficulty?.matrix ?? {})) stats.tiers[key] = (stats.tiers[key] ?? 0) + value;
    if (index <= 3) {
      const again = loaded.core.generate(built.config, { gameCatalog: catalog });
      if (again.placementHash !== plan.placementHash) stats.nonDeterministic += 1;
    }
  }
  const share = (item) => {
    const row = ['easy', 'medium', 'hard'].map((place) => stats.tiers[`${item}>${place}`] ?? 0);
    const total = row.reduce((sum, value) => sum + value, 0);
    return total === 0 ? null : row.map((value) => Number((100 * value / total).toFixed(1)));
  };
  if (Object.keys(stats.tiers).length > 0) stats.tierShares = { easyItems: share('easy'), mediumItems: share('medium'), hardItems: share('hard') };
  delete stats.tiers;
  summary.push(stats);
  console.log(JSON.stringify(stats));
}
void difficultyModule; void validateRandomizerConfig;
const bad = summary.reduce((total, entry) => total + Object.values(entry.failed).reduce((sum, value) => sum + value, 0) + entry.problems.length + entry.nonDeterministic, 0);
console.log(`ITEM MODES: ${summary.reduce((total, entry) => total + entry.ok + entry.refused, 0)} ok or refused as designed, ${bad} failures`);
if (bad > 0) process.exitCode = 1;
