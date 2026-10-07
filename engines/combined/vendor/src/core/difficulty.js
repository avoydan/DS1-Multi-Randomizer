// DS1 Triple Randomizer, repository-authored (not part of the upstream pin; listed in the module-set test and the pin file).
//
// Item difficulty (Part I, patches/0024; docs/ITEM_DIFFICULTY_MODEL.md): "Fair" is the neutral value (the current uniform permutation, no draw); "Unfair" and
// "Very Unfair" ORDER the candidate locations of every item by tier. The policy below is the behavior the legacy tool documents (an ordered list of groups
// of location tiers for each item tier). The tiers themselves are DS1TR's own and are derived from the game's data (the legacy's hand-rated tables are
// under no license and cannot be copied), see buildTierModel. Nothing here changes which items or which locations exist, and nothing here is a
// progression rule: the key validity check does not depend on the difficulty, so no mode can make a world unwinnable.
import { createStream } from "./rng.js";

export const ITEM_DIFFICULTY_MODES = ["fair", "unfair", "very-unfair"];
export const TIER = Object.freeze({ EASY: 0, MEDIUM: 1, HARD: 2 });
const TIER_NAMES = ["easy", "medium", "hard"];

// The ordered groups of location tiers an item of a tier prefers. Fair has no groups at all (it is never asked).
const POLICY = Object.freeze({
  unfair: Object.freeze({
    [TIER.EASY]: [[TIER.EASY], [TIER.MEDIUM, TIER.HARD]],
    [TIER.MEDIUM]: [[TIER.MEDIUM], [TIER.EASY, TIER.HARD]],
    [TIER.HARD]: [[TIER.MEDIUM, TIER.HARD], [TIER.EASY]],
  }),
  "very-unfair": Object.freeze({
    [TIER.EASY]: [[TIER.EASY], [TIER.MEDIUM], [TIER.HARD]],
    [TIER.MEDIUM]: [[TIER.MEDIUM], [TIER.EASY, TIER.HARD]],
    [TIER.HARD]: [[TIER.HARD], [TIER.MEDIUM], [TIER.EASY]],
  }),
});

export function preferredGroups(mode, itemTier) {
  const table = POLICY[mode];
  if (!table) return [[TIER.EASY, TIER.MEDIUM, TIER.HARD]];
  return table[itemTier];
}

/** The candidates of the first group that has one (every candidate has a `tier`); the whole list when nothing matches. */
export function preferByTier(mode, itemTier, candidates, tierOf) {
  for (const group of preferredGroups(mode, itemTier)) {
    const preferred = candidates.filter((candidate) => group.includes(tierOf(candidate)));
    if (preferred.length > 0) return preferred;
  }
  return candidates;
}

const SPELL_NAME = /^(Sorcery|Pyromancy|Miracle):/u;
const BOSS_SOUL_GOODS = (id) => id >= 700 && id <= 711;
const BIG_SOUL_GOODS = (id) => id >= 407 && id <= 409;
const SMALL_SOUL_GOODS = (id) => id >= 400 && id <= 406;

function terciles(scores) {
  // scores: Map id -> number; the ids ranked by score (ties by id), split in three equal parts.
  const ranked = [...scores].sort((left, right) => left[1] - right[1] || left[0] - right[0]);
  const tiers = new Map();
  ranked.forEach(([id], rank) => tiers.set(id, rank < ranked.length / 3 ? TIER.EASY : rank < (2 * ranked.length) / 3 ? TIER.MEDIUM : TIER.HARD));
  return tiers;
}

/**
 * The tiers (docs/ITEM_DIFFICULTY_MODEL.md section 2). `entries` are the pool items; `prepared` the progression model (keys and depths).
 * ITEM tier: a progression key, a boss soul, a big soul (407-409) or a transposed boss weapon is HARD; a small soul (400-406) is EASY; weapons are ranked by the sum
 * of their base attack values and armor by defense per weight, each split into terciles over the pool; rings, spells and the rest are ranked by how many
 * copies the pool holds (one = HARD, two to four = MEDIUM for goods, five or more = EASY; rings and spells: one = HARD, else MEDIUM).
 * LOCATION tier: boss rewards HARD, shops EASY, gifts and enemy drops MEDIUM, world pickups by depth in the world graph (terciles over the pool).
 */
export function buildTierModel({ catalog, prepared, entries, rowAreas }) {
  const keyIds = new Set(prepared ? prepared.raw.keys.map((key) => key.id) : []);
  const copies = new Map();
  const signatureOf = (entry) => `${entry.equipType}:${entry.itemId}`;
  for (const entry of entries) copies.set(signatureOf(entry), (copies.get(signatureOf(entry)) ?? 0) + 1);
  const traits = new Map((catalog?.itemTraits ?? []).map((trait) => [`${trait.equipType}:${trait.id}`, trait]));
  const weaponScores = new Map();
  const armorScores = new Map();
  for (const entry of entries) {
    const trait = traits.get(signatureOf(entry));
    if (!trait) continue;
    if (entry.equipType === 0) weaponScores.set(entry.itemId, trait.score);
    else if (entry.equipType === 1) armorScores.set(entry.itemId, trait.score / Math.max(0.5, trait.weight));
  }
  const weaponTier = terciles(weaponScores);
  const armorTier = terciles(armorScores);

  const itemTier = (entry) => {
    if (entry.keyName || entry.progression || keyIds.has(signatureOf(entry))) return TIER.HARD;
    if (entry.transmuted || entry.transposedFrom) return TIER.HARD;
    if (entry.equipType === 3 && entry.itemId < 3000) {
      if (BOSS_SOUL_GOODS(entry.itemId) || BIG_SOUL_GOODS(entry.itemId)) return TIER.HARD;
      if (SMALL_SOUL_GOODS(entry.itemId)) return TIER.EASY;
    }
    if (entry.equipType === 0) return weaponTier.get(entry.itemId) ?? TIER.MEDIUM;
    if (entry.equipType === 1) return armorTier.get(entry.itemId) ?? TIER.MEDIUM;
    const count = copies.get(signatureOf(entry)) ?? 1;
    if (entry.equipType === 2 || SPELL_NAME.test(entry.name ?? "")) return count === 1 ? TIER.HARD : TIER.MEDIUM;
    return count >= 5 ? TIER.EASY : count >= 2 ? TIER.MEDIUM : TIER.HARD;
  };

  // Depth: the number of areas between the start and the area of a pickup (every edge counted as open: how far into the world it lies).
  const depth = new Map();
  if (prepared) {
    const outgoing = new Map();
    for (const edge of prepared.edges) {
      if (!outgoing.has(edge.from)) outgoing.set(edge.from, []);
      outgoing.get(edge.from).push(edge.to);
    }
    depth.set(prepared.start, 0);
    const queue = [prepared.start];
    for (let head = 0; head < queue.length; head += 1) {
      for (const next of outgoing.get(queue[head]) ?? []) if (!depth.has(next)) { depth.set(next, depth.get(queue[head]) + 1); queue.push(next); }
    }
  }
  const worldDepths = entries.filter((entry) => entry.pool === "world" && rowAreas?.has(entry.rowId)).map((entry) => depth.get(rowAreas.get(entry.rowId)) ?? null).filter((value) => value !== null).sort((left, right) => left - right);
  const lowCut = worldDepths.length ? worldDepths[Math.floor(worldDepths.length / 3)] : 0;
  const highCut = worldDepths.length ? worldDepths[Math.floor((2 * worldDepths.length) / 3)] : 0;
  const locationTier = (entry) => {
    if (entry.pool === "boss") return TIER.HARD;
    if (entry.pool === "shop") return TIER.EASY;
    if (entry.pool === "gift" || entry.pool === "enemy") return TIER.MEDIUM;
    const area = rowAreas?.get(entry.rowId);
    const value = area === undefined ? undefined : depth.get(area);
    if (value === undefined) return TIER.MEDIUM;
    return value < lowCut ? TIER.EASY : value < highCut ? TIER.MEDIUM : TIER.HARD;
  };
  return { itemTier, locationTier, cuts: { lowCut, highCut }, tierNames: TIER_NAMES };
}

/**
 * The item permutation under Unfair / Very Unfair: the sources are taken in a seeded order and each takes a free target from the first group of its
 * policy that has one (uniformly inside the group). The hard constraints of the plain permutation hold: no source stays on its own target, and no
 * DLC-restricted item goes to a DLC target. Returns `sources` aligned with `values` (sources[j] is the item placed at target j). Bounded and deterministic.
 */
export function difficultyShuffle({ seed, version, values, mode, tiers, restricted, isDlcTarget }) {
  const count = values.length;
  const targetTier = values.map((value) => tiers.locationTier(value));
  const sourceTier = values.map((value) => tiers.itemTier(value));
  const sourceRestricted = values.map((value) => restricted(value));
  const dlc = values.map((value) => isDlcTarget(value));
  for (let attempt = 0; attempt < 32; attempt += 1) {
    const rng = createStream(seed, `item-difficulty:${attempt}`, version);
    const order = rng.shuffle(values.map((_, index) => index));
    const free = [new Set(), new Set(), new Set()];
    targetTier.forEach((tier, index) => free[tier].add(index));
    const sources = new Array(count);
    let failed = false;
    for (const source of order) {
      const allowed = (target) => target !== source && !(dlc[target] && sourceRestricted[source]);
      let chosen = -1;
      for (const group of preferredGroups(mode, sourceTier[source])) {
        const candidates = [];
        for (const tier of group) for (const target of free[tier]) if (allowed(target)) candidates.push(target);
        if (candidates.length > 0) { candidates.sort((left, right) => left - right); chosen = candidates[rng.int(candidates.length)]; break; }
      }
      if (chosen < 0) { failed = true; break; }
      free[targetTier[chosen]].delete(chosen);
      sources[chosen] = values[source];
    }
    if (!failed) return sources;
  }
  // A typed failure (retryable with another combined seed), like the key placement failures.
  throw Object.assign(new Error("DIFFICULTY_POLICY_UNSATISFIED: Could not assign the items under the selected item difficulty."), { code: "DIFFICULTY_POLICY_UNSATISFIED" });
}

/** Counters of the finished placement: how many items of each tier ended in a location of each tier ("item>location"). Never a location or an item name. */
export function summarizeTiers(placements, tiers) {
  const matrix = {};
  for (const [source, target] of placements) {
    const key = `${TIER_NAMES[tiers.itemTier(source)]}>${TIER_NAMES[tiers.locationTier(target)]}`;
    matrix[key] = (matrix[key] ?? 0) + 1;
  }
  return matrix;
}
