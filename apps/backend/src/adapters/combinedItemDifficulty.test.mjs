import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';

// Item difficulty (Part I, docs/ITEM_DIFFICULTY_MODEL.md) and the Race strategies of key placement (docs/RACE_MODE.md): the policy table, the biased assignment,
// the neutral value, the strategy table and the refusals. The real world (the game's data and FogMod's graph) is covered by placementMatrix.mjs.

const coreUrl = (name) => pathToFileURL(path.resolve(path.dirname(fileURLToPath(import.meta.url)), `../../../../engines/combined/vendor/src/core/${name}`)).href;
const difficulty = await import(coreUrl('difficulty.js'));
const keys = await import(coreUrl('keys.js'));
const { TIER, preferredGroups, preferByTier, difficultyShuffle, buildTierModel, summarizeTiers } = difficulty;

test('the policy table is the documented behavior: Fair has one group, Unfair and Very Unfair order the location tiers per item tier', () => {
  const all = [[TIER.EASY, TIER.MEDIUM, TIER.HARD]];
  for (const tier of [TIER.EASY, TIER.MEDIUM, TIER.HARD]) assert.deepEqual(preferredGroups('fair', tier), all);
  assert.deepEqual(preferredGroups('unfair', TIER.EASY), [[0], [1, 2]]);
  assert.deepEqual(preferredGroups('unfair', TIER.MEDIUM), [[1], [0, 2]]);
  assert.deepEqual(preferredGroups('unfair', TIER.HARD), [[1, 2], [0]]);
  assert.deepEqual(preferredGroups('very-unfair', TIER.EASY), [[0], [1], [2]]);
  assert.deepEqual(preferredGroups('very-unfair', TIER.MEDIUM), [[1], [0, 2]]);
  assert.deepEqual(preferredGroups('very-unfair', TIER.HARD), [[2], [1], [0]]);
});

test('preferByTier takes the first group that has a candidate and falls back to every candidate', () => {
  const candidates = [{ tier: 0, id: 'a' }, { tier: 1, id: 'b' }, { tier: 1, id: 'c' }];
  const tierOf = (candidate) => candidate.tier;
  assert.deepEqual(preferByTier('very-unfair', TIER.HARD, candidates, tierOf).map((c) => c.id), ['b', 'c'], 'no HARD location: the next group');
  assert.deepEqual(preferByTier('very-unfair', TIER.EASY, candidates, tierOf).map((c) => c.id), ['a']);
  assert.deepEqual(preferByTier('fair', TIER.HARD, candidates, tierOf).map((c) => c.id), ['a', 'b', 'c'], 'Fair keeps the whole list in its order');
  assert.deepEqual(preferByTier('unfair', TIER.HARD, [{ tier: 0, id: 'x' }], tierOf).map((c) => c.id), ['x'], 'a list with no preferred tier at all is returned whole');
});

const tiersOf = (values) => ({ itemTier: (value) => value.item, locationTier: (value) => value.place });
const world = () => Array.from({ length: 90 }, (_, index) => ({ id: index, item: index % 3, place: Math.floor(index / 30), mapId: 'm10_00_00_00', name: `n${index}` }));
const run = (mode, seed = 'seed') => difficultyShuffle({
  seed, version: undefined, values: world(), mode, tiers: tiersOf(world()), restricted: () => false, isDlcTarget: () => false,
});

test('the biased assignment is a permutation without fixed points, deterministic, and it moves the tiers in the intended direction', () => {
  const values = world();
  const share = (mode, item, place) => {
    let hit = 0; let total = 0;
    for (let seed = 0; seed < 40; seed += 1) {
      const sources = run(mode, `s${seed}`);
      assert.equal(new Set(sources.map((source) => source.id)).size, values.length, 'every item is placed exactly once');
      sources.forEach((source, target) => { assert.notEqual(source.id, target, 'no source stays on its own target'); if (source.item === item) { total += 1; if (values[target].place === place) hit += 1; } });
    }
    return hit / total;
  };
  assert.deepEqual(run('unfair', 'same').map((s) => s.id), run('unfair', 'same').map((s) => s.id), 'deterministic');
  const fairHardInHard = share('unfair', TIER.HARD, TIER.HARD);
  const veryHardInHard = share('very-unfair', TIER.HARD, TIER.HARD);
  const veryEasyInEasy = share('very-unfair', TIER.EASY, TIER.EASY);
  const veryHardInEasy = share('very-unfair', TIER.HARD, TIER.EASY);
  assert.ok(veryHardInHard > fairHardInHard + 0.15, `Very Unfair hides desirable items better than Unfair (${veryHardInHard.toFixed(2)} vs ${fairHardInHard.toFixed(2)})`);
  assert.ok(veryHardInHard > 0.8, `with as many HARD locations as HARD items, Very Unfair puts HARD items there (${veryHardInHard.toFixed(2)})`);
  assert.ok(veryEasyInEasy > 0.8, `and easy items in easy places (${veryEasyInEasy.toFixed(2)})`);
  assert.ok(veryHardInEasy < 0.05, `HARD items almost never lie in plain view (${veryHardInEasy.toFixed(2)})`);
});

test('the hard constraints hold under every mode: a restricted item never lands in a DLC target', () => {
  const values = world().map((value, index) => ({ ...value, dlc: index >= 80, restricted: index < 12 }));
  for (const mode of ['unfair', 'very-unfair']) {
    for (let seed = 0; seed < 25; seed += 1) {
      const sources = difficultyShuffle({ seed: `r${seed}`, version: undefined, values, mode, tiers: tiersOf(values), restricted: (value) => value.restricted, isDlcTarget: (value) => value.dlc });
      sources.forEach((source, target) => assert.equal(values[target].dlc && source.restricted, false, `${mode} ${seed}: DLC target ${target}`));
    }
  }
});

test('an assignment that cannot be made is a typed, retryable failure', () => {
  const values = [{ id: 0, item: 0, place: 0 }];
  assert.throws(
    () => difficultyShuffle({ seed: 'x', version: undefined, values, mode: 'unfair', tiers: tiersOf(values), restricted: () => false, isDlcTarget: () => false }),
    (error) => error.code === 'DIFFICULTY_POLICY_UNSATISFIED',
  );
});

test('the tier model follows the documented rules: keys, boss souls, big souls and transposed weapons are HARD, small souls EASY, equipment by its own parameters', () => {
  const prepared = keys.prepareModel({
    format: keys.PROGRESSION_MODEL_FORMAT, start: 'a', areas: ['a', 'b', 'c'], edges: [{ from: 'a', to: 'b', when: true }, { from: 'b', to: 'c', when: true }],
    keys: [{ name: 'k', id: '3:2001', area: 'a', tags: [] }], lotAreas: { 10: 'a', 20: 'b', 30: 'c' }, optionalAreas: [], prison: { start: 'a', mustReach: 'a', areas: ['a'] },
  });
  const entry = (equipType, itemId, extra = {}) => ({ equipType, itemId, name: `i${equipType}-${itemId}`, pool: 'world', rowId: 10, ...extra });
  const weapons = [entry(0, 100), entry(0, 200), entry(0, 300), entry(0, 400), entry(0, 500), entry(0, 600)];
  const catalog = { itemTraits: weapons.map((weapon, index) => ({ equipType: 0, id: weapon.itemId, score: (index + 1) * 10, weight: 1 })) };
  const entries = [...weapons, entry(3, 2001), entry(3, 700), entry(3, 408), entry(3, 400), entry(3, 500), entry(3, 500), entry(3, 500), entry(3, 500), entry(3, 500), entry(3, 1234), entry(0, 999, { transmuted: true })];
  const rowAreas = new Map([[10, 'a'], [20, 'b'], [30, 'c']]);
  const tiers = buildTierModel({ catalog, prepared, entries, rowAreas });
  assert.equal(tiers.itemTier(entry(3, 2001)), TIER.HARD, 'a progression key');
  assert.equal(tiers.itemTier(entry(3, 700)), TIER.HARD, 'a boss soul');
  assert.equal(tiers.itemTier(entry(3, 408)), TIER.HARD, 'a big soul');
  assert.equal(tiers.itemTier(entry(3, 400)), TIER.EASY, 'a small soul');
  assert.equal(tiers.itemTier(entry(0, 999, { transmuted: true })), TIER.HARD, 'a transposed boss weapon keeps the HARD tier of the soul');
  assert.deepEqual([100, 200, 300, 400, 500, 600].map((id) => tiers.itemTier(entry(0, id))), [0, 0, 1, 1, 2, 2], 'weapons by terciles of their attack');
  assert.equal(tiers.itemTier(entry(3, 500)), TIER.EASY, 'a repeated consumable');
  assert.equal(tiers.itemTier(entry(3, 1234)), TIER.HARD, 'a unique good');
  assert.equal(tiers.locationTier({ pool: 'boss', rowId: 99 }), TIER.HARD);
  assert.equal(tiers.locationTier({ pool: 'shop', rowId: 99 }), TIER.EASY);
  assert.equal(tiers.locationTier({ pool: 'gift', rowId: 99 }), TIER.MEDIUM);
  assert.equal(tiers.locationTier({ pool: 'world', rowId: 10 }) <= tiers.locationTier({ pool: 'world', rowId: 30 }), true, 'a deeper pickup is never an easier location');
  assert.deepEqual(summarizeTiers([[entry(3, 400), { pool: 'shop', rowId: 1 }]], tiers), { 'easy>easy': 1 });
});

test('the key placement strategies are a table, and a mode never silently becomes another', () => {
  assert.deepEqual(keys.KEY_PLACEMENT_MODES, ['vanilla', 'shuffled', 'race', 'race-plus']);
  assert.deepEqual(Object.keys(keys.KEY_PLACEMENT_STRATEGIES), ['shuffled', 'race', 'race-plus']);
  assert.deepEqual(keys.KEY_PLACEMENT_STRATEGIES.shuffled, { raceDestinations: false, graph: 'vanilla', raceItems: false });
  assert.deepEqual(keys.KEY_PLACEMENT_STRATEGIES.race, { raceDestinations: true, graph: 'vanilla', raceItems: true });
  assert.deepEqual(keys.KEY_PLACEMENT_STRATEGIES['race-plus'], { raceDestinations: true, graph: 'race-plus', raceItems: true });
  assert.ok(Object.isFrozen(keys.KEY_PLACEMENT_STRATEGIES) && Object.isFrozen(keys.KEY_PLACEMENT_STRATEGIES.race));
});

test('the model variant must match the mode: a vanilla model is refused for Race Mode +, and the reverse', () => {
  const model = (variant) => ({
    format: keys.PROGRESSION_MODEL_FORMAT, ...(variant ? { variant } : {}), start: 'a', areas: ['a'], edges: [], keys: [], lotAreas: {}, optionalAreas: [], prison: { start: 'a', mustReach: 'a', areas: ['a'] },
  });
  assert.doesNotThrow(() => keys.prepareModel(model(undefined), 'vanilla'), 'a model exported before the variants existed is the vanilla graph');
  assert.doesNotThrow(() => keys.prepareModel(model('race-plus'), 'race-plus'));
  for (const [exported, wanted] of [['vanilla', 'race-plus'], ['race-plus', 'vanilla'], [undefined, 'race-plus']]) {
    assert.throws(() => keys.prepareModel(model(exported), wanted), (error) => error.code === 'KEY_PLACEMENT_MODEL_INVALID', `${exported} for ${wanted}`);
  }
});

// ---- the engine around them ---------------------------------------------------------------------------------------------------------

const { loadCombinedRandomizerCore } = await import('../../dist/adapters/combinedRandomizerCore.js');
const { soulsCatalog } = await import('./soulsTestSupport.mjs');
const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const engineConfig = (overrides = {}) => ({
  ...defaultConfig, seed: 'difficulty', randomizeEnemies: false, randomizeBosses: false, randomizeItems: true, randomizeGifts: false,
  randomizeEnemyDrops: false, randomizeShops: false, generateSpoilerLog: false, ...overrides,
});
const minimalModel = { format: keys.PROGRESSION_MODEL_FORMAT, start: 'a', areas: ['a'], edges: [], keys: [], lotAreas: {}, optionalAreas: [], prison: { start: 'a', mustReach: 'a', areas: ['a'] } };

test('Fair is the neutral value: with and without the option (and with a model attached) the world is byte-identical, and nothing about it is reported', () => {
  for (let index = 0; index < 5; index += 1) {
    const config = engineConfig({ seed: `fair-${index}` });
    const plain = generate(config, { gameCatalog: soulsCatalog() });
    const fair = generate({ ...config, itemDifficulty: 'fair', laundromat: false, progressionModel: minimalModel }, { gameCatalog: soulsCatalog() });
    assert.equal(plain.placementHash, fair.placementHash, `#${index}`);
    assert.equal(JSON.stringify(plain.placements), JSON.stringify(fair.placements));
    assert.equal(fair.validation.itemDifficulty, undefined, 'nothing reported for Fair');
  }
});

test('Unfair and Very Unfair change the layout, report counters only, and are deterministic', () => {
  const config = engineConfig({ seed: 'unfair-1', progressionModel: minimalModel });
  const fair = generate(config, { gameCatalog: soulsCatalog() });
  for (const mode of ['unfair', 'very-unfair']) {
    const first = generate({ ...config, itemDifficulty: mode }, { gameCatalog: soulsCatalog() });
    const second = generate({ ...config, itemDifficulty: mode }, { gameCatalog: soulsCatalog() });
    assert.equal(first.placementHash, second.placementHash, `${mode} is deterministic`);
    assert.notEqual(first.placementHash, fair.placementHash, `${mode} is not the Fair layout`);
    assert.equal(first.validation.itemDifficulty.mode, mode);
    assert.ok(Object.values(first.validation.itemDifficulty.matrix).every((count) => Number.isInteger(count)), 'counters only');
  }
});

test('refusals before anything is planned: Race Mode + needs Fair, a difficulty needs the model, and an unknown mode falls back to the neutral value', () => {
  const refusal = (overrides) => { try { generate(engineConfig(overrides), { gameCatalog: soulsCatalog() }); } catch (error) { return String(error.message); } return null; };
  assert.match(refusal({ keyPlacement: 'race-plus', itemDifficulty: 'unfair', progressionModel: minimalModel }), /RACE_PLUS_DIFFICULTY/u);
  assert.match(refusal({ itemDifficulty: 'unfair', progressionModel: null }), /ITEM_DIFFICULTY_MODEL_MISSING/u);
  assert.match(refusal({ keyPlacement: 'race', progressionModel: null }), /KEY_PLACEMENT_MODEL_MISSING/u);
  assert.match(refusal({ keyPlacement: 'race', randomizeItems: false, randomizeGifts: true, progressionModel: minimalModel }), /KEY_PLACEMENT_NEEDS_WORLD_ITEMS/u);
  assert.equal(refusal({ itemDifficulty: 'brutal' }), null, 'an unknown value falls back to fair, like every enumeration of the engine');
  assert.match(refusal({ keyPlacement: 'race', progressionModel: minimalModel }) ?? 'none', /KEY_PLACEMENT_NOTHING_TO_PLACE/u, 'a model with no movable key plans nothing and says so');
});

test('a "random only" item that opens an area is never placed in that area: the areas behind a key are those no other key can reach', () => {
  const prepared = keys.prepareModel({
    format: keys.PROGRESSION_MODEL_FORMAT, start: 'a', areas: ['a', 'b', 'hidden', 'c'],
    edges: [{ from: 'a', to: 'b', when: 'door_key' }, { from: 'b', to: 'hidden', when: 'lamp' }, { from: 'b', to: 'c', when: true }],
    keys: [{ name: 'door_key', id: '3:1', area: 'a', tags: [] }, { name: 'lamp', id: '3:3500', area: 'a', tags: ['randomonly'] }],
    lotAreas: {}, optionalAreas: [], prison: { start: 'a', mustReach: 'a', areas: ['a'] },
  });
  assert.deepEqual([...keys.areasBehindKey(prepared, 'lamp')], ['hidden']);
  assert.deepEqual([...keys.areasBehindKey(prepared, 'door_key')].sort(), ['b', 'c', 'hidden'], 'without the door key nothing past the door is reachable');
});
