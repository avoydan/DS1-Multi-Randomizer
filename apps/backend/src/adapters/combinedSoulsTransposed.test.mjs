import assert from 'node:assert/strict';
import test from 'node:test';
import { loadCombinedRandomizerCore } from '../../dist/adapters/combinedRandomizerCore.js';
import { BOSS_SOULS, soulsCatalog, transpositionRecipes } from './soulsTestSupport.mjs';

// Soul items "Transposed" (Part H, docs/ITEM_PROGRESSION_MODEL.md) on synthetic catalogs with the real soul ids and the shape DsrDataTool scan writes.

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;

const base = (overrides = {}) => ({
  ...defaultConfig, seed: 'souls', randomizeEnemies: false, randomizeBosses: false, randomizeItems: true, randomizeGifts: false,
  randomizeEnemyDrops: false, randomizeShops: false, generateSpoilerLog: false, ...overrides,
});
const json = (value) => JSON.stringify(value);
const isSoul = (placement) => placement.equipType === 3 && placement.itemId >= 700 && placement.itemId <= 711;
const everyPlacement = (result) => [
  ...result.placements.items, ...result.placements.gifts, ...result.placements.enemyDrops, ...result.placements.shops, ...(result.placements.bossRewards ?? []),
];
const soulRows = new Set(BOSS_SOULS.map((boss) => boss.row));

test('neutral modes are byte-identical to a catalog that has no boss data at all: the boss rewards only matter for Transposed', () => {
  const stripped = soulsCatalog({ bossRewards: undefined, transpositionRecipes: undefined });
  for (const soulItems of ['shuffled', 'replaced']) {
    for (let index = 0; index < 5; index += 1) {
      const config = base({ seed: `neutral-${index}`, soulItems });
      const withBoss = generate(config, { gameCatalog: soulsCatalog() });
      const without = generate(config, { gameCatalog: stripped });
      assert.equal(withBoss.placementHash, without.placementHash, `${soulItems} #${index}`);
      assert.equal(json(withBoss.placements), json(without.placements));
      assert.equal('bossRewards' in withBoss.placements, false, 'no boss reward key without the option');
    }
  }
});

test('Transposed is deterministic and every boss soul row is a target of the pool', () => {
  for (let index = 0; index < 10; index += 1) {
    const config = base({ seed: `t-${index}`, soulItems: 'transposed' });
    const first = generate(config, { gameCatalog: soulsCatalog() });
    assert.equal(first.placementHash, generate(config, { gameCatalog: soulsCatalog() }).placementHash, 'deterministic');
    const rewards = first.placements.bossRewards;
    assert.deepEqual(rewards.map((placement) => placement.rowId).sort((left, right) => left - right), [...soulRows].sort((left, right) => left - right),
      'exactly the twelve soul rows (the Covenant ring and the bonus lots in the same chains stay out of the pool)');
    for (const placement of rewards) {
      assert.equal(placement.targetKind, 'lot');
      assert.equal(placement.targetSlot, 1);
      assert.equal(placement.targetPool, 'boss');
    }
  }
});

test('no boss soul is duplicated and none disappears except by transposition; the item count of the pool is conserved', () => {
  const catalog = soulsCatalog();
  const targets = catalog.worldItemLots.filter((lot) => !lot.protectedProgression).length + BOSS_SOULS.length;
  let transposed = 0;
  let kept = 0;
  for (let index = 0; index < 40; index += 1) {
    const result = generate(base({ seed: `acct-${index}`, soulItems: 'transposed' }), { gameCatalog: catalog });
    const placements = everyPlacement(result);
    assert.equal(placements.length, targets, 'every target holds exactly one item');
    const souls = placements.filter(isSoul);
    assert.equal(new Set(souls.map((placement) => placement.itemId)).size, souls.length, 'no soul twice');
    const weapons = placements.filter((placement) => placement.transposedFrom);
    assert.equal(souls.length + weapons.length, 12, 'each soul exists once, either as itself or as the weapon it became');
    assert.equal(new Set(weapons.map((placement) => placement.transposedFrom)).size, weapons.length, 'a soul becomes at most one weapon');
    transposed += weapons.length;
    kept += souls.length;
    // a transposed soul became a weapon of its own recipe
    const recipes = new Map(transpositionRecipes().map((recipe) => [recipe.soulName, new Set(recipe.families.flat().map((item) => item.itemId))]));
    for (const placement of weapons) {
      assert.equal(placement.equipType, 0);
      assert.equal(placement.itemQuantity, 1);
      assert.ok(recipes.get(placement.transposedFrom).has(placement.itemId), `${placement.transposedFrom} -> ${placement.itemName}`);
    }
    assert.equal(weapons.some((placement) => placement.transposedFrom === 'Guardian Soul'), false, 'the Guardian Soul has no recipe and stays a soul');
  }
  const share = transposed / (transposed + kept);
  // legacy: 75% for the eleven souls that have a recipe; the Guardian Soul stays (11/12 x 0.75 = 0.6875)
  assert.ok(share > 0.6 && share < 0.78, `share ${share.toFixed(3)}`);
});

test('the DLC boss rewards follow the rule of the DLC world lots: no progression, Titanite or Ember lands there', () => {
  const catalog = soulsCatalog();
  const dlcRows = new Set(BOSS_SOULS.filter((boss) => boss.dlc).map((boss) => boss.row));
  let checked = 0;
  for (let index = 0; index < 60; index += 1) {
    const result = generate(base({ seed: `dlc-${index}`, soulItems: 'transposed' }), { gameCatalog: catalog });
    for (const placement of result.placements.bossRewards.filter((entry) => dlcRows.has(entry.rowId))) {
      checked += 1;
      assert.doesNotMatch(placement.itemName, /Titanite|Ember/u, placement.itemName);
      assert.equal(placement.progression, false);
    }
  }
  assert.equal(checked, 60 * 3);
});

test('Transposed with a catalog that has no boss rewards fails loudly, with the typed message', () => {
  const catalog = soulsCatalog({ bossRewards: [], transpositionRecipes: [] });
  assert.throws(() => generate(base({ soulItems: 'transposed' }), { gameCatalog: catalog }), /SOUL_ITEMS_TRANSPOSED_UNAVAILABLE/u);
});

test('Transposed uses its own stream: it never changes an item placement stream other than the layout it enlarges, and Replaced is unchanged', () => {
  // Replaced and Shuffled worlds do not know the option exists.
  const catalog = soulsCatalog();
  const shuffled = generate(base({ seed: 'streams' }), { gameCatalog: catalog });
  const replaced = generate(base({ seed: 'streams', soulItems: 'replaced' }), { gameCatalog: catalog });
  assert.equal(shuffled.placements.items.length, catalog.worldItemLots.length - 1);
  assert.equal(replaced.placements.items.length, catalog.worldItemLots.length - 1);
  // The same seed with Transposed enlarges the pool by the twelve soul rows, so the layout differs (the legacy couples them on purpose).
  const transposed = generate(base({ seed: 'streams', soulItems: 'transposed' }), { gameCatalog: catalog });
  assert.notEqual(transposed.placementHash, shuffled.placementHash);
  assert.equal(transposed.placements.items.length, catalog.worldItemLots.length - 1);
  assert.equal(transposed.placements.bossRewards.length, 12);
});

test('the soul stream does not move with unrelated options: items and boss rewards of a seed ignore the enemy options', () => {
  const catalog = soulsCatalog();
  const reference = generate(base({ seed: 'indep', soulItems: 'transposed' }), { gameCatalog: catalog });
  for (const overrides of [{ randomizeEnemies: true }, { randomizeBosses: true }, { randomizeEnemies: true, randomizeBosses: true, enemyPlacementAlgorithm: 'sampling-v1' }]) {
    const other = generate(base({ seed: 'indep', soulItems: 'transposed', ...overrides }), { gameCatalog: catalog });
    assert.equal(json(other.placements.items), json(reference.placements.items), json(overrides));
    assert.equal(json(other.placements.bossRewards), json(reference.placements.bossRewards), json(overrides));
  }
});

test('the spoiler and the hint metadata name what happened: the boss, the soul it held and the item it holds now', () => {
  const result = generate(base({ seed: 'spoiler', soulItems: 'transposed', generateSpoilerLog: true }), { gameCatalog: soulsCatalog() });
  for (const placement of result.placements.bossRewards) {
    assert.match(placement.randomizedLocation.area, /^Boss reward: /u);
    assert.ok(placement.bossReward.startsWith('boss-reward-'));
    assert.equal(typeof placement.rowId, 'number');
    assert.equal(typeof placement.itemName, 'string');
  }
  assert.ok(result.validation.notes.some((note) => /Soul items transposed/u.test(note)));
});
