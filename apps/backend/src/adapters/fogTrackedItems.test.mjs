import assert from 'node:assert/strict';
import test from 'node:test';
import { loadCombinedRandomizerCore } from '../../dist/adapters/combinedRandomizerCore.js';
import { trackedItemsFromCompactProjection } from '../../dist/adapters/fogItemRules.js';
import { legacyFogTrackedItems } from '../../dist/adapters/legacyFogTrackedItems.js';

// Items the world randomizer (FogMod) follows by lot may only be placed in lots it annotates (docs/FOG_ITEM_TRACKING.md).

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;

const RING = { equipType: 2, itemId: 149, category: 0x20000000, name: 'Darkmoon Seance Ring' };
const DOLL = { equipType: 3, itemId: 384, category: 0x40000000, name: 'Peculiar Doll' };
// The shops sell a second copy of the Crest of Artorias (FogMod finds the first in a protected lot outside the shuffle) and
// two of Cast Light (an item FogMod does not require): extra copies that must never end in an annotated lot.
const CREST = { equipType: 3, itemId: 2002, name: 'Crest of Artorias' };
const CAST = { equipType: 3, itemId: 3500, name: 'Cast Light' };
const entry = (item, quantity = 1) => ({ slot: 1, itemId: item.itemId, category: item.category, quantity, equipType: item.equipType, name: item.name });
const goods = (i) => ({ equipType: 3, itemId: 500 + i, category: 0x40000000, name: `Goods${i}` });
const lot = (rowId, item, mapId = 'm10_00_00_00') => ({ rowId, name: item.name, mapId, protectedProgression: false, entries: [entry(item)] });

// World lots: 25 annotated (starts 1_000_000, 1_000_010 and 1_000_020; the rest follow without a gap up to row 1_000_024) and
// 15 that are not (rows 1_000_100 and up, after a gap). The ring and the doll start in annotated lots.
const ANNOTATED_STARTS = [1_000_000, 1_000_010, 1_000_020];
const isAnnotated = (row) => (row >= 1_000_000 && row <= 1_000_024) || row === 5000 || row === 5001;

// One enemy slot makes the catalog "extracted data", so the engine takes the real (catalog) item path.
const enemySlot = {
  id: 'm10_00_00_00:c1000_0000', mapId: 'm10_00_00_00', name: 'c1000_0000', modelName: 'c1000', entityId: -1,
  npcParamId: 100000, thinkParamId: 100000, talkId: -1, charaInitId: 100000, collisionName: 'h0000B0',
  position: { x: 1, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, movePoints: [], dummy: false, hasEntityId: false,
  eventModelLocked: false, riskFlags: [], safeCandidate: true, teamType: 0, npcType: 0, moveType: 0, hitHeight: 1,
  hitRadius: 1, hitYOffset: 0, baseHp: 100, soulReward: 10, battleStartDistance: 20, eyeDistance: 20, earDistance: 10,
  disablePathMove: false, battleGoalId: 100000,
};

function catalog() {
  const world = [lot(1_000_003, RING), lot(1_000_007, DOLL)];
  for (let i = 0; i < 40; i += 1) {
    // Rows 1_000_000..1_000_024 are annotated; 1_000_100..1_000_114 are not (a gap separates them).
    const row = i < 25 ? 1_000_000 + i : 1_000_100 + (i - 25);
    if (row !== 1_000_003 && row !== 1_000_007) world.push(lot(row, goods(i)));
  }
  world.sort((a, b) => a.rowId - b.rowId);
  const gifts = [lot(5000, goods(100)), lot(5001, goods(101)), lot(5010, goods(102)), lot(5011, goods(103))];
  const drops = Array.from({ length: 12 }, (_, i) => lot(6000 + i, goods(200 + i)));
  const shops = Array.from({ length: 12 }, (_, i) => ({ rowId: 7000 + i, name: `Shop${i}`, equipId: 900 + i, equipType: 3, eventFlag: -1 }));
  shops.push({ rowId: 7100, name: CREST.name, equipId: CREST.itemId, equipType: 3, eventFlag: -1 });
  shops.push({ rowId: 7200, name: CAST.name, equipId: CAST.itemId, equipType: 3, eventFlag: -1 });
  shops.push({ rowId: 7201, name: CAST.name, equipId: CAST.itemId, equipType: 3, eventFlag: -1 });
  return {
    schemaVersion: 17, maps: [{ id: 'm10_00_00_00', name: 'm10_00_00_00' }],
    enemySlots: [enemySlot], enemyArchetypes: [{ id: 'x' }], bossSlots: [],
    worldItemLots: world, gifts, enemyDropLots: drops, shopEntries: shops,
  };
}

const TRACKED = {
  items: [
    { equipType: RING.equipType, itemId: RING.itemId, required: true },
    { equipType: DOLL.equipType, itemId: DOLL.itemId, required: true },
    { equipType: CREST.equipType, itemId: CREST.itemId, required: true },
    { equipType: CAST.equipType, itemId: CAST.itemId, required: false },
  ],
  lotStarts: [...ANNOTATED_STARTS, 5000, 5001],
};

const config = (overrides = {}) => ({
  ...defaultConfig, seed: 'fog-items', randomizeEnemies: false, randomizeBosses: false, randomizeItems: true,
  progressionLogic: true, guaranteeEarlyWeapon: false, balancedEarlyLoot: false, generateSpoilerLog: false, ...overrides,
});
const CATEGORIES = {
  'World items': {},
  'NPC gifts': { randomizeGifts: true },
  'Enemy drops': { randomizeEnemyDrops: true },
  Shops: { randomizeShops: true },
  'all categories together': { randomizeStartingClass: true, randomizeStartingEquipment: true, randomizeGifts: true, randomizeEnemyDrops: true, guaranteedEnemyDrops: true, randomizeShops: true },
};

function placementsOf(result) {
  return [
    ...(result.placements.items ?? []).map((p) => ({ ...p, pool: 'world' })),
    ...(result.placements.gifts ?? []).map((p) => ({ ...p, pool: 'gift' })),
    ...(result.placements.enemyDrops ?? []).map((p) => ({ ...p, pool: 'drop' })),
    ...(result.placements.shops ?? []).map((p) => ({ ...p, pool: 'shop' })),
  ];
}
const where = (result, item) => placementsOf(result).filter((p) => p.equipType === item.equipType && p.itemId === item.itemId);
const allowed = (placement) => placement.pool !== 'shop' && isAnnotated(placement.rowId);

test('without the rule the tracked items do leave the annotated lots (the failure this fixes)', () => {
  let outside = 0;
  for (let i = 0; i < 40; i += 1) {
    const result = generate(config({ seed: `plain-${i}`, ...CATEGORIES['all categories together'], fogTrackedItems: null }), { gameCatalog: catalog() });
    if ([RING, DOLL].some((item) => where(result, item).some((p) => !allowed(p)))) outside += 1;
  }
  assert.ok(outside > 10, `most seeds lose a tracked item (${outside}/40)`);
});

for (const [name, extra] of Object.entries(CATEGORIES)) {
  test(`Fog + ${name}: tracked items stay in annotated lots, appear exactly once, deterministically`, () => {
    for (let i = 0; i < 40; i += 1) {
      const settings = config({ seed: `rule-${name.replaceAll(' ', '-')}-${i}`, ...extra, fogTrackedItems: TRACKED });
      const result = generate(settings, { gameCatalog: catalog() });
      for (const item of [RING, DOLL]) {
        const hits = where(result, item);
        assert.equal(hits.length, 1, `${item.name} appears exactly once (seed ${i})`);
        assert.ok(allowed(hits[0]), `${item.name} is in an annotated lot (seed ${i}): ${hits[0].pool} ${hits[0].rowId}`);
      }
      // No item is duplicated or lost anywhere in the plan.
      const counts = new Map();
      for (const p of placementsOf(result)) counts.set(`${p.equipType}:${p.itemId}`, (counts.get(`${p.equipType}:${p.itemId}`) ?? 0) + 1);
      for (const [key, count] of counts) assert.equal(count, key === `${CAST.equipType}:${CAST.itemId}` ? 2 : 1, `${key} appears as often as in the game`);
      if (i < 6) assert.equal(generate(settings, { gameCatalog: catalog() }).placementHash, result.placementHash, 'deterministic rerun');
    }
  });
}

test('extra copies (the shop copy of a key item FogMod finds elsewhere, or an item it does not require) never enter an annotated lot', () => {
  let crestMoved = 0;
  for (let i = 0; i < 60; i += 1) {
    const result = generate(config({ seed: `extra-${i}`, ...CATEGORIES.Shops, fogTrackedItems: TRACKED }), { gameCatalog: catalog() });
    const crest = where(result, CREST);
    const cast = where(result, CAST);
    assert.equal(crest.length, 1);
    assert.equal(cast.length, 2);
    for (const placement of [...crest, ...cast]) assert.equal(placement.pool, 'shop', `${placement.itemName} stays in a shop (seed ${i}): ${placement.pool} ${placement.rowId}`);
    if (crest[0].rowId !== 7100) crestMoved += 1;
    for (const item of [RING, DOLL]) assert.ok(allowed(where(result, item)[0]));
  }
  assert.ok(crestMoved > 30, `the shops still shuffle among themselves (${crestMoved}/60)`);
  // Without the rule they do end up in lots: that is the second failure FogMod reports ("found in both").
  let inLots = 0;
  for (let i = 0; i < 60; i += 1) {
    const result = generate(config({ seed: `extra-${i}`, ...CATEGORIES.Shops, fogTrackedItems: null }), { gameCatalog: catalog() });
    if (where(result, CREST).some((p) => p.pool !== 'shop')) inLots += 1;
  }
  assert.ok(inLots > 10, `without the rule the extra copy reaches a lot (${inLots}/60)`);
});

test('the tracked items still move: the rule limits where, not whether', () => {
  let ringMoved = 0;
  for (let i = 0; i < 30; i += 1) {
    const result = generate(config({ seed: `move-${i}`, fogTrackedItems: TRACKED }), { gameCatalog: catalog() });
    if (where(result, RING)[0].rowId !== 1_000_003) ringMoved += 1;
  }
  assert.ok(ringMoved > 15, `the ring is randomized (${ringMoved}/30)`);
});

test('a seed that already satisfied the rule keeps its exact placement', () => {
  let compared = 0;
  for (let i = 0; i < 60; i += 1) {
    const settings = config({ seed: `same-${i}`, ...CATEGORIES['World items'] });
    const plain = generate({ ...settings, fogTrackedItems: null }, { gameCatalog: catalog() });
    const satisfied = [RING, DOLL].every((item) => where(plain, item).every(allowed))
      && [CREST, CAST].every((item) => where(plain, item).every((p) => p.pool === 'shop'));
    if (satisfied) {
      assert.equal(generate({ ...settings, fogTrackedItems: TRACKED }, { gameCatalog: catalog() }).placementHash, plain.placementHash);
      compared += 1;
    }
  }
  assert.ok(compared > 3, `some seeds were comparable (${compared})`);
});

test('a lot covers the rows after its start without a gap, never more', () => {
  const small = {
    ...catalog(), worldItemLots: [lot(100, RING), lot(101, goods(1)), lot(102, goods(2)), lot(104, goods(4)), lot(200, DOLL), lot(300, goods(3)), lot(301, goods(5)), lot(302, goods(6))],
    gifts: [], enemyDropLots: [], shopEntries: [],
  };
  for (let i = 0; i < 40; i += 1) {
    const result = generate(config({ seed: `gap-${i}`, fogTrackedItems: { items: [{ equipType: 2, itemId: 149, required: true }], lotStarts: [100] } }), { gameCatalog: small });
    // Row 104 follows a gap after 102 and is not covered.
    assert.ok([100, 101, 102].includes(where(result, RING)[0].rowId), `ring lands in a covered row (${where(result, RING)[0].rowId})`);
  }
});

test('when no annotated lot can take a tracked item the plan fails early with a typed, plain error', () => {
  const tiny = { ...catalog(), worldItemLots: [lot(100, RING), lot(101, goods(1)), lot(102, goods(2))], gifts: [], enemyDropLots: [], shopEntries: [] };
  const rule = { items: [{ equipType: 2, itemId: 149, required: true }, { equipType: 3, itemId: 501, required: true }, { equipType: 3, itemId: 502, required: true }], lotStarts: [100] };
  // Every item is tracked and only rows 100-102 are annotated: the shuffle is a permutation of them, which is fine.
  assert.ok(generate(config({ seed: 'tiny', fogTrackedItems: rule }), { gameCatalog: tiny }).placementHash);
  const strict = { items: [{ equipType: 2, itemId: 149, required: true }, { equipType: 3, itemId: 501, required: true }], lotStarts: [100] };
  // Row 102 is outside the annotation but must hold Goods2: only possible for untracked items, and it is.
  assert.ok(generate(config({ seed: 'tiny2', fogTrackedItems: strict }), { gameCatalog: tiny }).placementHash);
  const impossible = {
    ...tiny, worldItemLots: [lot(100, RING), lot(101, DOLL), lot(102, goods(2)), lot(103, goods(3))],
  };
  assert.throws(
    () => generate(config({ seed: 'impossible', fogTrackedItems: { items: [{ equipType: 2, itemId: 149, required: true }, { equipType: 3, itemId: 384, required: true }], lotStarts: [] } }), { gameCatalog: impossible }),
    (error) => /FOG_TRACKED_ITEM_UNPLACEABLE/u.test(error.message),
  );
});

// ---- the rules come from the Compact source-of-truth projection --------------------------------------------------------

test('Compact tracking projection is copied without reinterpretation', () => {
  const source = { items: [{ equipType: 3, itemId: 2510, required: true }], lotStarts: [1090, 2500] };
  const tracked = trackedItemsFromCompactProjection(source);
  assert.deepEqual(tracked, source);
  tracked.items[0].required = false;
  tracked.lotStarts.push(6190);
  assert.deepEqual(source, { items: [{ equipType: 3, itemId: 2510, required: true }], lotStarts: [1090, 2500] });
});

test('historical profiles use frozen tracking data without reading fog.txt', () => {
  const first = legacyFogTrackedItems();
  const second = legacyFogTrackedItems();
  assert.equal(first.items.length, 27);
  assert.equal(first.lotStarts.length, 440);
  assert.ok(first.items.some((item) => item.equipType === 3 && item.itemId === 2510 && item.required));
  assert.ok(first.items.some((item) => item.equipType === 3 && item.itemId === 3500 && !item.required));
  assert.deepEqual(second, first);
  first.items.pop();
  first.lotStarts.pop();
  assert.equal(second.items.length, 27);
  assert.equal(second.lotStarts.length, 440);
});
