import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { DEFAULT_GENERATION_SETTINGS, joinConfig } from '@ds1tr/contracts';
import { describePlanBehaviors } from '../../dist/core/pipeline.js';

// The plan preview carries stable keys next to its English text, so the interface can translate it without reading a sentence (docs/I18N.md).
const locale = (language) => JSON.parse(readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'desktop', 'src', 'locales', language, 'plan.json'), 'utf8'));
const lookup = (tree, key) => key.split('.').reduce((node, part) => (node && typeof node === 'object' ? node[part] : undefined), tree);
const config = (settings) => joinConfig(structuredClone({ ...DEFAULT_GENERATION_SETTINGS, ...settings }), 'C:\game', 'seed', true);
const withItems = (items) => ({ items: { ...DEFAULT_GENERATION_SETTINGS.items, ...items } });

const variants = [
  config({}),
  config(withItems({ enabled: false })),
  config(withItems({ randomizeWorldItems: false, randomizeGifts: false, randomizeEnemyDrops: false, randomizeShops: false, laundromat: true })),
  ...['shuffled', 'race', 'race-plus'].flatMap((keyPlacement) => ['shuffled', 'replaced', 'transposed'].flatMap((soulItems) => [true, false].map((lord) => config(withItems({
    keyPlacement, soulItems, randomizeLordvessel: lord, randomizeLordSouls: lord, randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true, itemDifficulty: lord ? 'unfair' : 'very-unfair',
  }))))),
  config({ enemies: { ...DEFAULT_GENERATION_SETTINGS.enemies, enabled: false } }),
  config({ enemies: { ...DEFAULT_GENERATION_SETTINGS.enemies, placement: { ...DEFAULT_GENERATION_SETTINGS.enemies.placement, algorithm: 'permutation-v1' } }, fog: { ...DEFAULT_GENERATION_SETTINGS.fog, enabled: false } }),
];

test('every plan line has a label key and a value key, and every key has an English and a Polish text', () => {
  const english = locale('en');
  const polish = locale('pl');
  for (const variant of variants) {
    for (const line of describePlanBehaviors(variant)) {
      assert.ok(line.labelKey && line.valueKey, `${line.label}: keys present`);
      assert.equal(typeof lookup(english, `labels.${line.labelKey}`), 'string', `en label ${line.labelKey}`);
      assert.equal(typeof lookup(polish, `labels.${line.labelKey}`), 'string', `pl label ${line.labelKey}`);
      const value = lookup(english, `values.${line.valueKey}`);
      assert.equal(typeof value, 'string', `en value ${line.valueKey}`);
      assert.equal(typeof lookup(polish, `values.${line.valueKey}`), 'string', `pl value ${line.valueKey}`);
      // The English resource is the English sentence (no drift between the backend text and the resource), except for the item pool list which is a variable.
      if (!line.params) assert.equal(value, line.value, `the English text of ${line.valueKey} equals the backend text`);
      for (const pool of (line.params?.pools ?? '').split(',').filter(Boolean)) assert.equal(typeof lookup(english, `pools.${pool}`), 'string', pool);
    }
  }
});

test('the item pool list is passed as ids, not as a sentence', () => {
  const lines = describePlanBehaviors(config(withItems({ randomizeGifts: true, randomizeShops: true })));
  const items = lines.find((line) => line.labelKey === 'items');
  assert.equal(items.valueKey, 'itemsShuffled');
  assert.equal(items.params.pools, 'worldItems,npcGifts,shops');
  assert.equal(items.value, 'shuffled: world items, NPC gifts, shops');
});
