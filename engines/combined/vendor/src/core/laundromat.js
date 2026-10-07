// DS1 Triple Randomizer, repository-authored (not part of the upstream pin; listed in the module-set test and the pin file).
//
// Laundromat Mixup (Part I, patches/0024; docs/LAUNDROMAT_MIXUP.md): the NPC characters wear armor other NPCs wear. SAFE SUBSET of the legacy option:
// everything is derived from the clean game's own CharaInitParam (the scan's `npcArmor`), nothing from the legacy's hand-made NPC and armor-set tables.
//   * an NPC character = the CharaInitParam rows from id 6000 up that share one `npcPlayerFaceGenId`;
//   * the candidate pieces are the pieces some NPC already wears in that slot (so every written id is one the game already loads on an NPC model, at the
//     level it was worn at; no id is ever computed);
//   * the hollowed variants (piece ids from 2,000,000) are left alone with every other row of their character;
//   * Fashion Souls off: one donor row's four pieces (a full set); on: each slot independently; a slot a row leaves empty stays empty.
// It touches nothing else: no item lot, no shop, no item of the pool, nothing the player can pick up. It draws only from the stream "item-laundromat".
import { createStream } from "./rng.js";

const SLOTS = ["head", "chest", "hands", "legs"];
const HOLLOW_FROM = 2_000_000;

/** The eligible characters (sorted by face id), each with its rows (sorted by id). */
export function laundromatCharacters(npcArmor) {
  const groups = new Map();
  for (const row of npcArmor ?? []) {
    if (row.id < 6000 || row.faceId < 0) continue;
    if (!groups.has(row.faceId)) groups.set(row.faceId, []);
    groups.get(row.faceId).push(row);
  }
  const characters = [];
  for (const [faceId, rows] of [...groups].sort((left, right) => left[0] - right[0])) {
    rows.sort((left, right) => left.id - right.id);
    const hollow = rows.some((row) => SLOTS.some((slot) => row[slot] >= HOLLOW_FROM));
    const dressed = rows.some((row) => SLOTS.some((slot) => row[slot] > 0));
    if (!hollow && dressed) characters.push({ faceId, rows });
  }
  return characters;
}

/**
 * The planned rows: [{ id, head, chest, hands, legs }] for every row whose armor changes. Deterministic from the seed and the scan alone.
 * `fashionSouls` on mixes the pieces; off, a character wears the four pieces of one donor row.
 */
export function planLaundromat({ seed, version, npcArmor, fashionSouls }) {
  const characters = laundromatCharacters(npcArmor);
  if (characters.length === 0) return [];
  const rng = createStream(seed, "item-laundromat", version);
  const rows = characters.flatMap((character) => character.rows);
  const pool = Object.fromEntries(SLOTS.map((slot) => [slot, [...new Set(rows.map((row) => row[slot]).filter((id) => id > 0))].sort((left, right) => left - right)]));
  const donors = rows.filter((row) => SLOTS.every((slot) => row[slot] > 0));
  const plan = [];
  for (const character of characters) {
    const wear = {};
    if (fashionSouls || donors.length === 0) {
      for (const slot of SLOTS) wear[slot] = pool[slot][rng.int(pool[slot].length)];
    } else {
      const donor = donors[rng.int(donors.length)];
      for (const slot of SLOTS) wear[slot] = donor[slot];
    }
    for (const row of character.rows) {
      const next = { id: row.id };
      for (const slot of SLOTS) next[slot] = row[slot] > 0 ? wear[slot] : row[slot];
      if (SLOTS.some((slot) => next[slot] !== row[slot])) plan.push(next);
    }
  }
  return plan;
}
