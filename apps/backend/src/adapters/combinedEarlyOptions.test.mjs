// "Guarantee early weapon" (`guaranteeEarlyWeapon`) and "Balanced early loot" (`balancedEarlyLoot`): docs/EARLY_OPTIONS_AUDIT.md.
// Neither the Item Randomizer (HotPocketRemix f7cabbd) nor the Enemy Randomizer (everKalle 83f008d) has such an option. The flags come from the vendored engine
// (kellernz1/ds-randomizer f120efe), where they are implemented ONLY for its placeholder data (`randomizeItems`, `src/data/prototype-data.js`); the real-data path
// (`randomizeExtractedItemLots`) never reads them. These tests pin that fact, so a future change that makes them work (or breaks the isolation) is a deliberate one.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { loadCombinedRandomizerCore } from '../../dist/adapters/combinedRandomizerCore.js';

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;

const generatorUrl = new URL('../../../../engines/combined/vendor/src/core/generator.js', import.meta.url);
const generatorSource = await readFile(generatorUrl, 'utf8');
const configSource = await readFile(new URL('../../../../engines/combined/vendor/src/core/config.js', import.meta.url), 'utf8');

/** The text of one top-level function of the engine (from its `function name(` to the next top-level `function` / `export function`). */
function functionBody(name) {
  const start = generatorSource.search(new RegExp(`^(export )?function ${name}\\(`, 'mu'));
  assert.ok(start >= 0, `${name} exists`);
  const rest = generatorSource.slice(start + 1);
  const next = rest.search(/^(export )?(async )?function /mu);
  return next < 0 ? rest : rest.slice(0, next);
}

const lot = (rowId, mapId, itemId, category, equipType, name) => ({
  rowId, name, mapId, protectedProgression: false, entries: [{ slot: 1, itemId, category, quantity: 1, equipType, name }],
});

/** A real-data-shaped catalog (schema 17) with weapons, shields, upgrade materials and consumables spread over maps. */
function catalog() {
  const lots = [];
  let row = 2_000_000;
  const maps = ['m10_00_00_00', 'm10_01_00_00', 'm11_00_00_00', 'm12_00_00_00', 'm13_00_00_00', 'm14_00_00_00'];
  maps.forEach((mapId, m) => {
    for (let i = 0; i < 4; i += 1) lots.push(lot(row += 1, mapId, 100000 + m * 1000 + i * 100, 0, 0, `Weapon${m}-${i}`));
    for (let i = 0; i < 3; i += 1) lots.push(lot(row += 1, mapId, 1000000 + m * 1000 + i * 100, 0, 0, `Shield${m}-${i}`));
    for (let i = 0; i < 3; i += 1) lots.push(lot(row += 1, mapId, 1000 + m * 10 + i, 1073741824, 3, `Titanite${m}-${i}`));
    for (let i = 0; i < 3; i += 1) lots.push(lot(row += 1, mapId, 200 + m * 10 + i, 1073741824, 3, `Goods${m}-${i}`));
  });
  return {
    schemaVersion: 17,
    maps: maps.map((id) => ({ id, name: id })),
    enemySlots: [{
      id: 'm10_00_00_00:c1000_0000', mapId: 'm10_00_00_00', name: 'c1000_0000', modelName: 'c1000', entityId: -1, npcParamId: 100000, thinkParamId: 100000, talkId: -1,
      charaInitId: 100000, collisionName: 'h0000B0', position: { x: 0, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, movePoints: [], dummy: false, hasEntityId: false,
      eventModelLocked: false, riskFlags: [], safeCandidate: true, teamType: 0, npcType: 0, moveType: 0, hitHeight: 1, hitRadius: 1, hitYOffset: 0, baseHp: 100,
      soulReward: 10, battleStartDistance: 20, eyeDistance: 20, earDistance: 10, disablePathMove: false, battleGoalId: 100000,
    }],
    enemyArchetypes: [{ id: 'x' }], bossSlots: [], worldItemLots: lots, gifts: [], enemyDropLots: [], shopEntries: [],
  };
}

const config = (overrides = {}) => ({
  ...defaultConfig, seed: 'early-options', randomizeEnemies: false, randomizeBosses: false, randomizeItems: true, generateSpoilerLog: false, ...overrides,
});
const COMBINATIONS = [
  { guaranteeEarlyWeapon: false, balancedEarlyLoot: false },
  { guaranteeEarlyWeapon: true, balancedEarlyLoot: false },
  { guaranteeEarlyWeapon: false, balancedEarlyLoot: true },
  { guaranteeEarlyWeapon: true, balancedEarlyLoot: true },
];

test('the engine defaults are on for both options (so every fresh configuration, preset and Seed Code carries true, true)', () => {
  assert.equal(defaultConfig.guaranteeEarlyWeapon, true);
  assert.equal(defaultConfig.balancedEarlyLoot, true);
  assert.match(configSource, /guaranteeEarlyWeapon: true,\s*balancedEarlyLoot: true,/u);
});

test('with real game data the two options change nothing: every combination writes the same placements and the same hash, for several seeds and pools', () => {
  const data = catalog();
  for (const seed of ['early-options', 'another-seed', 'third-seed-7']) {
    for (const extra of [{}, { progressionLogic: false }, { randomizeProtectedItems: true }, { randomizeGifts: true, randomizeShops: true, randomizeEnemyDrops: true }]) {
      const results = COMBINATIONS.map((flags) => generate(config({ seed, ...extra, ...flags }), { gameCatalog: data }));
      assert.equal(results[0].dataStatus, 'extracted');
      for (const result of results.slice(1)) {
        assert.deepEqual(result.placements, results[0].placements, `${seed} ${JSON.stringify(extra)}`);
        assert.equal(result.placementHash, results[0].placementHash);
      }
    }
  }
});

test('the placements really are shuffled (the comparison above is not between identical vanilla worlds)', () => {
  const result = generate(config(), { gameCatalog: catalog() });
  assert.equal(result.placements.items.length, 6 * 13);
  assert.ok(result.placements.items.filter((placement) => placement.sourceRowId !== placement.rowId).length > 40);
});

test('the flags are read in exactly one place: the placeholder-data function, never the real-data item function', () => {
  const prototype = functionBody('randomizeItems');
  const extracted = functionBody('randomizeExtractedItemLots');
  const all = [...generatorSource.matchAll(/guaranteeEarlyWeapon|balancedEarlyLoot/gu)].length;
  const inPrototype = [...prototype.matchAll(/guaranteeEarlyWeapon|balancedEarlyLoot/gu)].length;
  assert.equal(inPrototype, 2, 'both flags are read by the placeholder-data function');
  assert.equal(all, inPrototype, 'and nowhere else in the engine');
  assert.doesNotMatch(extracted, /guaranteeEarlyWeapon|balancedEarlyLoot|sphere/u);
  // The placeholder function works on `sphere` values of the toy data set; real catalogs have none.
  assert.match(prototype, /location\.sphere <= sphere/u);
  assert.match(prototype, /promoteCategory\("weapon", 0/u);
});

test('DS1TR never runs the placeholder path: the bridge refuses a result that is not extracted data', async () => {
  const text = await readFile(new URL('./combinedRandomizerBridge.ts', import.meta.url), 'utf8');
  assert.match(text, /result.dataStatus !== 'extracted'/u);
});
