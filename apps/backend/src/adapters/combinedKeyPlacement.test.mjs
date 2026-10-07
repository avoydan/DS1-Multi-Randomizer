import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Key placement (Part H, docs/ITEM_PROGRESSION_MODEL.md): the evaluator of the progression model and the assumed fill, on a small synthetic world.
// The real world (FogMod's graph) is covered by progressionDifferential.mjs (the evaluator against FogMod's GraphChecker) and by the placement sweeps.

const keysUrl = pathToFileURL(path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../engines/combined/vendor/src/core/keys.js')).href;
const keys = await import(keysUrl);
const { KeyPlacementError, PROGRESSION_MODEL_FORMAT, checkCompletable, coveredRowAreas, pairDisplaced, placeKeys, prepareModel, visitedAreas } = keys;

/**
 * start -> hall (free) ; hall -> tower needs key_a ; tower -> vault needs key_b ; hall -> cellar needs key_c AND tower ; cellar -> deep needs key_d.
 * Prison: cell -> yard needs cell_key (lying in the prison).
 */
const model = (overrides = {}) => ({
  format: PROGRESSION_MODEL_FORMAT, fogModCommit: 'test', annotationSha256: 'test', start: 'start',
  areas: ['start', 'hall', 'tower', 'vault', 'cellar', 'deep', 'cell', 'yard'],
  edges: [
    { from: 'start', to: 'hall', when: true },
    { from: 'hall', to: 'tower', when: 'key_a' },
    { from: 'tower', to: 'vault', when: 'key_b' },
    { from: 'hall', to: 'cellar', when: { all: ['key_c', 'tower'] } },
    { from: 'cellar', to: 'deep', when: { any: ['key_d', 'vault'] } },
    { from: 'cell', to: 'yard', when: 'cell_key' },
  ],
  keys: [
    { name: 'key_a', id: '1:1', area: 'hall', tags: [] },
    { name: 'key_b', id: '1:2', area: 'hall', tags: [] },
    { name: 'key_c', id: '1:3', area: 'tower', tags: [] },
    { name: 'key_d', id: '1:4', area: 'cellar', tags: [] },
    { name: 'cell_key', id: '1:5', area: 'cell', tags: [] },
  ],
  lotAreas: { 100: 'hall', 200: 'tower', 300: 'vault', 400: 'cellar', 500: 'deep', 600: 'start', 700: 'cell' },
  optionalAreas: ['cell', 'yard'],
  prison: { start: 'cell', mustReach: 'yard', areas: ['cell', 'yard'] },
  ...overrides,
});
const prepared = prepareModel(model());
const vanilla = () => new Map(model().keys.map((key) => [key.name, key.area]));
const destinations = ['start', 'hall', 'hall', 'tower', 'vault', 'cellar', 'deep', 'deep'].map((area, index) => ({ id: `d${index}`, area }));

test('the vanilla world of the synthetic model is completable and the prison can be left', () => {
  const result = checkCompletable(prepared, vanilla());
  assert.deepEqual(result, { completable: true, unvisited: [], prisonEscapable: true });
});

test('a key behind its own door makes the world incompletable; the check names the unvisited areas', () => {
  const placed = vanilla();
  placed.set('key_a', 'tower');
  const result = checkCompletable(prepared, placed);
  assert.equal(result.completable, false);
  assert.deepEqual(result.unvisited, ['cellar', 'deep', 'tower', 'vault']);
  const prison = vanilla();
  prison.set('cell_key', 'hall');
  assert.equal(checkCompletable(prepared, prison).prisonEscapable, false, 'the prison key must lie in the prison');
});

test('a condition naming a key counts when the key lies in a visited area, not before', () => {
  const placed = vanilla();
  assert.deepEqual([...visitedAreas(prepared, placed)].sort(), ['cellar', 'deep', 'hall', 'start', 'tower', 'vault']);
  assert.equal(visitedAreas(prepared, placed, new Set(), 'cell').has('yard'), true);
  // key_c waits in the tower and key_a opens it: with key_a assumed in hand the cellar still needs key_c (in the tower, visited) and the tower.
  assert.equal(visitedAreas(prepared, placed, new Set(['key_a'])).has('cellar'), true);
});

test('a malformed model is refused with a typed code', () => {
  const code = (build) => { try { prepareModel(build()); } catch (error) { return error.code; } return null; };
  assert.equal(code(() => ({ ...model(), format: 'other' })), 'KEY_PLACEMENT_MODEL_INVALID');
  assert.equal(code(() => null), 'KEY_PLACEMENT_MODEL_INVALID');
  assert.equal(code(() => ({ ...model(), edges: [{ from: 'start', to: 'nowhere', when: true }] })), 'KEY_PLACEMENT_MODEL_INVALID');
  assert.equal(code(() => ({ ...model(), edges: [{ from: 'start', to: 'hall', when: { none: [] } }] })), 'KEY_PLACEMENT_MODEL_INVALID');
  assert.equal(code(() => ({ ...model(), keys: [{ name: 'x', id: '1:9', area: 'nowhere', tags: [] }] })), 'KEY_PLACEMENT_MODEL_INVALID');
  const { lotAreas: _removed, ...incomplete } = model();
  assert.equal(code(() => incomplete), 'KEY_PLACEMENT_MODEL_INVALID');
});

test('the assumed fill places every key where it can be reached, on every seed, and the result is completable', () => {
  const movable = ['key_a', 'key_b', 'key_c', 'key_d'];
  const fixed = new Map([['cell_key', 'cell']]);
  const seen = new Set();
  for (let seed = 0; seed < 200; seed += 1) {
    const result = placeKeys({ prepared, seed: `seed-${seed}`, version: undefined, movable, fixed, destinations });
    assert.equal(result.placed.size, movable.length);
    assert.equal(new Set(result.placed.values()).size, movable.length, 'each pickup holds at most one key');
    const areas = new Map(fixed);
    for (const [key, id] of result.placed) areas.set(key, destinations.find((destination) => destination.id === id).area);
    const check = checkCompletable(prepared, areas);
    assert.equal(check.completable, true, `seed ${seed}: ${check.unvisited.join(',')}`);
    assert.equal(check.prisonEscapable, true);
    seen.add(JSON.stringify([...result.placed]));
  }
  assert.ok(seen.size > 10, 'the placements vary with the seed');
});

test('the fill is deterministic and its draws depend on the seed only', () => {
  const run = (seed) => JSON.stringify([...placeKeys({ prepared, seed, version: undefined, movable: ['key_d', 'key_a', 'key_c', 'key_b'], fixed: new Map([['cell_key', 'cell']]), destinations }).placed]);
  assert.equal(run('same'), run('same'));
  // The order the caller lists the movable keys in does not matter (they are sorted before the draw).
  const shuffledInput = JSON.stringify([...placeKeys({ prepared, seed: 'same', version: undefined, movable: ['key_b', 'key_c', 'key_a', 'key_d'], fixed: new Map([['cell_key', 'cell']]), destinations }).placed]);
  assert.equal(run('same'), shuffledInput);
});

test('no reachable pickup for a key ends in a typed, bounded failure (never a wrong placement)', () => {
  // Every pickup lies behind the doors that need the keys themselves: key_a has nowhere to go.
  const sealed = ['tower', 'vault', 'cellar', 'deep'].map((area, index) => ({ id: `s${index}`, area }));
  assert.throws(
    () => placeKeys({ prepared, seed: 'sealed', version: undefined, movable: ['key_a', 'key_b', 'key_c', 'key_d'], fixed: new Map([['cell_key', 'cell']]), destinations: sealed }),
    (error) => error instanceof KeyPlacementError && error.code === 'KEY_PLACEMENT_UNSATISFIABLE' && error.diagnostics.attempts === 32 && error.diagnostics.deadEnds.length === 32 && error.diagnostics.reason === 'NO_REACHABLE_DESTINATION',
  );
});

test('rows belong to the area of the nearest listed row below them in an unbroken run of known rows', () => {
  const known = new Set([100, 101, 102, 200, 205, 300, 301]);
  const areas = coveredRowAreas(prepared, known);
  assert.deepEqual([...areas].sort((left, right) => left[0] - right[0]), [[100, 'hall'], [101, 'hall'], [102, 'hall'], [200, 'tower'], [300, 'vault'], [301, 'vault']]);
  assert.equal(areas.has(205), false, 'a row after a gap is not covered');
});

test('displaced items are paired with the freed pickups by the DLC rule, deterministically, from their own stream', () => {
  const row = (rowId, dlc = false) => ({ rowId, slot: 0, mapId: dlc ? 'm12_01' : 'm10_00', dlc });
  const displaced = [row(1), row(2), row(3, true)];
  const freed = [row(10), row(11, true), row(12)];
  const accepts = { restricted: (target) => target.dlc, fits: (item, target) => !(target.dlc && !item.dlc && item.rowId === 1) };
  const first = pairDisplaced({ seed: 'pair', version: undefined, displaced, freed, accepts });
  const second = pairDisplaced({ seed: 'pair', version: undefined, displaced, freed, accepts });
  assert.deepEqual(first, second);
  assert.equal(first.length, 3);
  assert.equal(new Set(first.map((pair) => pair.source.rowId)).size, 3, 'every displaced item is used once');
  assert.notEqual(first.find((pair) => pair.target.rowId === 11).source.rowId, 1, 'the restricted row refuses the item it does not accept');
  assert.throws(
    () => pairDisplaced({ seed: 'pair', version: undefined, displaced: [row(1)], freed: [row(11, true)], accepts }),
    (error) => error.code === 'KEY_PLACEMENT_FILL_IMPOSSIBLE',
  );
});

// ---- the engine around the planner -----------------------------------------------------------------------------------------------------

const { loadCombinedRandomizerCore } = await import('../../dist/adapters/combinedRandomizerCore.js');
const { soulsCatalog } = await import('./soulsTestSupport.mjs');
const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const engineConfig = (overrides = {}) => ({
  ...defaultConfig, seed: 'keys', randomizeEnemies: false, randomizeBosses: false, randomizeItems: true, randomizeGifts: false,
  randomizeEnemyDrops: false, randomizeShops: false, generateSpoilerLog: false, ...overrides,
});

test('without the option the engine ignores a progression model: the result is byte-identical (the neutral value of the extension fields)', () => {
  for (let index = 0; index < 5; index += 1) {
    const config = engineConfig({ seed: `neutral-${index}` });
    const plain = generate(config, { gameCatalog: soulsCatalog() });
    const withModel = generate({ ...config, keyPlacement: 'vanilla', progressionModel: model() }, { gameCatalog: soulsCatalog() });
    assert.equal(plain.placementHash, withModel.placementHash, `#${index}`);
    assert.equal(JSON.stringify(plain.placements), JSON.stringify(withModel.placements));
    assert.equal(plain.validation.keyPlacement, undefined, 'no key placement section without the option');
  }
});

test('invalid key placement requests are refused with a typed code before anything is planned', () => {
  const refusal = (overrides) => { try { generate(engineConfig(overrides), { gameCatalog: soulsCatalog() }); } catch (error) { return String(error.message); } return null; };
  assert.match(refusal({ keyPlacement: 'shuffled', progressionModel: null }), /KEY_PLACEMENT_MODEL_MISSING/u);
  assert.match(refusal({ keyPlacement: 'shuffled', progressionModel: model(), randomizeItems: false, randomizeGifts: true }), /KEY_PLACEMENT_NEEDS_WORLD_ITEMS/u);
  assert.match(refusal({ keyPlacement: 'vanilla', keyRandomizeLordvessel: true }), /KEY_PLACEMENT_OFF/u);
  assert.match(refusal({ keyPlacement: 'shuffled', progressionModel: { ...model(), format: 'other' } }), /KEY_PLACEMENT_MODEL_INVALID/u);
  assert.equal(refusal({ keyPlacement: 'sideways' }), null, 'an unknown mode falls back to vanilla, like every other enumeration of the engine');
});
