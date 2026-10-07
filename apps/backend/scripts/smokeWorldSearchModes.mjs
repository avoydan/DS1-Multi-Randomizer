// Finds master seeds for the quick smoke-test worlds of Part I (docs/SMOKE_TESTS.md): Race Mode, Race Mode + and the three item difficulties. Like smokeWorldSearch.mjs
// it works on a real extracted catalog with the engine only, and nothing is forced: every candidate is a normal world made from normal settings and a normal master
// seed, the search only reads what the engine produced. The target is the pickup of the first cell of the Undead Asylum (item lot 1810080, where the clean game has the
// Peculiar Doll), 2.8 m from where the player wakes up.
//
//   node apps/backend/scripts/smokeWorldSearchModes.mjs <catalog.json> <vanilla model.json> <race-plus model.json> <race|race-plus|difficulty> [--from 1 --to 800 --prefix p]
//
// race:        a key or an ember (a race item) lies in the cell under Race Mode.
// race-plus:   the same under Race Mode +, and the world needs a skip of the list (it is not completable on the vanilla graph).
// difficulty:  ONE master seed for the three difficulties: Fair puts a HARD-tier item in the cell, Very Unfair an EASY-tier item, Unfair a non-HARD item (a single sample of one
//              EASY-tier location: the tier of what lies in the cell, nothing more).

import { readFileSync } from 'node:fs';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { buildSettings } from './goldenCompatibility.mjs';
import { loadCombinedRandomizerCore } from '../dist/adapters/combinedRandomizerCore.js';
import { mapCombinedRandomizerConfig } from '../dist/adapters/combinedRandomizerConfig.js';
import { currentGenerationProfile } from '../dist/core/generationProfiles.js';
import { deriveAttemptSeeds } from '../dist/core/seeds.js';
import { prepareRun } from '../dist/core/seededRun.js';

const [catalogPath, vanillaPath, racePlusPath, family, ...rest] = process.argv.slice(2);
const option = (name, fallback) => (rest.includes(name) ? rest[rest.indexOf(name) + 1] : fallback);
if (!catalogPath || !vanillaPath || !racePlusPath || !['race', 'race-plus', 'difficulty'].includes(family)) { console.error('Usage: node smokeWorldSearchModes.mjs <catalog> <vanilla model> <race-plus model> <race|race-plus|difficulty> [--from N --to N --prefix p]'); process.exit(2); }
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const models = { vanilla: JSON.parse(readFileSync(vanillaPath, 'utf8')), 'race-plus': JSON.parse(readFileSync(racePlusPath, 'utf8')) };
const loaded = await loadCombinedRandomizerCore();
const keys = await import(pathToFileURL(fileURLToPath(new URL('../../../engines/combined/vendor/src/core/keys.js', import.meta.url))).href);
const difficulty = await import(pathToFileURL(fileURLToPath(new URL('../../../engines/combined/vendor/src/core/difficulty.js', import.meta.url))).href);
const profile = currentGenerationProfile();
const CELL = 1810080;
const base = { enemies: { enabled: false }, fog: { enabled: false } };
const prefix = option('--prefix', { race: 'smoke-q13-race', 'race-plus': 'smoke-q14-raceplus', difficulty: 'smoke-q15-diff' }[family]);
const from = Number(option('--from', 1)); const to = Number(option('--to', 800));

const plan = (patch, masterSeed) => {
  const prepared = prepareRun({ gamePath: 'X:/search', masterSeed, ...buildSettings(patch) });
  if (!prepared.ok) throw new Error(`${prepared.code}: ${prepared.technical}`);
  const { items, enemies } = prepared.config;
  const mode = items.keyPlacement ?? 'vanilla';
  const needsModel = mode !== 'vanilla' || (items.itemDifficulty ?? 'fair') !== 'fair';
  const mapped = mapCombinedRandomizerConfig({
    gamePath: 'X:/search', workspacePath: 'X:/search/work', masterSeed, combinedSeed: deriveAttemptSeeds(masterSeed, 0, 0, profile.seedDerivation).combined,
    items, enemies, generateSpoilerLog: false, placementDiagnosticsLevel: 'summary', fogTrackedItems: null, progressionModel: needsModel ? models[mode === 'race-plus' ? 'race-plus' : 'vanilla'] : null,
  });
  if (!mapped.ok) throw new Error(mapped.message);
  return loaded.core.generate(mapped.config, { gameCatalog: catalog });
};
const cellOf = (result) => result.placements.items.find((entry) => entry.rowId === CELL);

// the tiers of the pool, as the engine builds them (the entries of the clean game's pool)
const prepared = keys.prepareModel(models.vanilla);
const entries = [];
for (const lot of catalog.worldItemLots) for (const entry of lot.entries) entries.push({ ...entry, rowId: lot.rowId, mapId: lot.mapId, pool: 'world', progression: lot.protectedProgression });
const tiers = difficulty.buildTierModel({ catalog, prepared, entries, rowAreas: keys.coveredRowAreas(prepared, new Set(catalog.worldItemLots.map((lot) => lot.rowId))) });
const tierOfCell = (cell) => tiers.itemTier({ equipType: cell.equipType, itemId: cell.itemId, name: cell.itemName, progression: cell.progression, keyName: cell.keyName, pool: 'world' });

for (let index = from; index <= to; index += 1) {
  const masterSeed = `${prefix}-${index}`;
  if (family === 'race' || family === 'race-plus') {
    const result = plan({ ...base, items: { keyPlacement: family } }, masterSeed);
    const cell = cellOf(result);
    if (!cell || !(cell.keyName || cell.raceItem)) continue;
    if (family === 'race-plus') {
      // the world needs a skip: its keys are not placed so that the vanilla graph completes
      const vanilla = keys.prepareModel(models.vanilla);
      const known = new Set([...catalog.worldItemLots.map((lot) => lot.rowId), ...catalog.gifts.map((lot) => lot.rowId), ...catalog.bossRewards.flatMap((reward) => reward.lots.flatMap((lot) => lot.chain.map((row) => row.rowId)))]);
      const rowArea = keys.coveredRowAreas(vanilla, known);
      const areas = new Map(vanilla.raw.keys.map((key) => [key.name, key.area]));
      for (const placement of [...result.placements.items, ...result.placements.bossRewards, ...result.placements.gifts]) if (placement.keyName) areas.set(placement.keyName, rowArea.get(placement.rowId));
      if (keys.checkCompletable(vanilla, areas).completable) continue;
    }
    console.log(JSON.stringify({ test: `quick-${family}`, masterSeed, item: cell.itemName, itemId: cell.itemId, itemCategory: cell.itemCategory, equipType: cell.equipType, key: cell.keyName ?? null, raceItem: Boolean(cell.raceItem), replacedItem: cell.from }));
  } else {
    const fair = cellOf(plan({ ...base, items: { itemDifficulty: 'fair' } }, masterSeed));
    const unfair = cellOf(plan({ ...base, items: { itemDifficulty: 'unfair' } }, masterSeed));
    const veryUnfair = cellOf(plan({ ...base, items: { itemDifficulty: 'very-unfair' } }, masterSeed));
    if (!fair || !unfair || !veryUnfair) continue;
    if (tierOfCell(fair) !== difficulty.TIER.HARD || tierOfCell(veryUnfair) !== difficulty.TIER.EASY || tierOfCell(unfair) === difficulty.TIER.HARD) continue;
    const row = (cell) => ({ item: cell.itemName, itemId: cell.itemId, itemCategory: cell.itemCategory, equipType: cell.equipType, tier: ['easy', 'medium', 'hard'][tierOfCell(cell)] });
    console.log(JSON.stringify({ test: 'quick-difficulty', masterSeed, fair: row(fair), unfair: row(unfair), veryUnfair: row(veryUnfair) }));
  }
}
