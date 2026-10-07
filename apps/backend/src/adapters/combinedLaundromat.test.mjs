import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';

// Laundromat Mixup (Part I, docs/LAUNDROMAT_MIXUP.md): the plan is built from the armor NPCs already wear, never from computed ids, and leaves hollowed and empty slots alone.
const { laundromatCharacters, planLaundromat } = await import(pathToFileURL(path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../engines/combined/vendor/src/core/laundromat.js')).href);

const row = (id, faceId, head, chest, hands, legs) => ({ id, faceId, head, chest, hands, legs });
const npcArmor = [
  row(2000, 5, 210000, 211000, 212000, 213000),                 // a player class row: never an NPC
  row(6000, 6000, 160002, 161002, 162002, 163002),              // a knight (two rows: the second is the same character at another level)
  row(6002, 6000, 160010, 161010, 162010, 163010),
  row(6010, 6010, 450005, 451005, 452005, 453005),
  row(6020, 6020, 350006, 351006, 352006, 353006),              // a character with a hollowed variant: left alone entirely
  row(6021, 6020, 2350006, 2351006, 2352006, 2353006),
  row(6080, 6080, -1, 651000, 652000, 653000),                  // no hat
  row(6090, 6090, 180010, 181010, 182010, 183010),
  row(6099, 6099, -1, -1, -1, -1),                              // naked: not dressed, left alone
];
const plan = (overrides = {}) => planLaundromat({ seed: 'laundromat', version: undefined, npcArmor, fashionSouls: true, ...overrides });
const slots = ['head', 'chest', 'hands', 'legs'];
const worn = Object.fromEntries(slots.map((slot) => [slot, new Set(npcArmor.filter((entry) => entry.id >= 6000).map((entry) => entry[slot]).filter((id) => id > 0 && id < 2_000_000))]));

test('the characters are the dressed, non-hollowed NPC rows grouped by face, in a stable order', () => {
  const characters = laundromatCharacters(npcArmor);
  assert.deepEqual(characters.map((character) => character.faceId), [6000, 6010, 6080, 6090]);
  assert.deepEqual(characters[0].rows.map((entry) => entry.id), [6000, 6002]);
});

test('every written piece is a piece an NPC already wears in that very slot; empty slots stay empty; hollowed and naked rows are never planned', () => {
  for (const fashionSouls of [true, false]) {
    for (let index = 0; index < 60; index += 1) {
      const rows = plan({ seed: `s${index}`, fashionSouls });
      const byId = new Map(npcArmor.map((entry) => [entry.id, entry]));
      for (const planned of rows) {
        const original = byId.get(planned.id);
        assert.ok(planned.id >= 6000 && ![6020, 6021, 6099].includes(planned.id), `row ${planned.id} may be changed`);
        for (const slot of slots) {
          if (original[slot] <= 0) assert.equal(planned[slot], original[slot], `${planned.id} ${slot}: an empty slot stays empty`);
          else assert.ok(worn[slot].has(planned[slot]), `${planned.id} ${slot}: ${planned[slot]} is worn by an NPC`);
        }
        assert.ok(slots.some((slot) => planned[slot] !== original[slot]), 'only rows that change are planned');
      }
    }
  }
});

test('Fashion Souls off gives a character one complete set (the four pieces of one donor row), on mixes the slots', () => {
  const donors = npcArmor.filter((entry) => entry.id >= 6000 && slots.every((slot) => entry[slot] > 0 && entry[slot] < 2_000_000));
  const isSet = (piece) => donors.some((donor) => slots.every((slot) => donor[slot] === piece[slot]));
  let mixed = 0;
  for (let index = 0; index < 80; index += 1) {
    const off = plan({ seed: `f${index}`, fashionSouls: false }).filter((entry) => entry.id !== 6080);
    for (const planned of off) assert.ok(isSet(planned), `row ${planned.id} wears a complete set`);
    for (const planned of plan({ seed: `f${index}`, fashionSouls: true }).filter((entry) => entry.id !== 6080)) if (!isSet(planned)) mixed += 1;
  }
  assert.ok(mixed > 20, `with Fashion Souls on the pieces are mixed (${mixed} mixed rows)`);
});

test('the rows of one character wear the same pieces, the plan is deterministic from the seed, and it depends on the seed', () => {
  const rows = plan({ seed: 'same' });
  const knight = rows.filter((entry) => entry.id === 6000 || entry.id === 6002);
  if (knight.length === 2) assert.deepEqual(slots.map((slot) => knight[0][slot]), slots.map((slot) => knight[1][slot]));
  assert.deepEqual(plan({ seed: 'same' }), rows);
  assert.notDeepEqual(plan({ seed: 'other' }), rows);
  assert.deepEqual(planLaundromat({ seed: 'x', version: undefined, npcArmor: [], fashionSouls: true }), [], 'nothing to plan from an empty scan');
});
