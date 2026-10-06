// DS1 Triple Randomizer - repository-authored module (GPL-3.0-only); not part of upstream ds-randomizer.
//
// The `sampling-v1` enemy placement planner for REGULAR enemy slots (docs/ENEMY_PLACEMENT_MODEL.md, Parts A and B).
// It is a pure function of the slots it is given: no I/O, no catalog access, no global state. Bosses, NPCs,
// linked units, special entities and every placement decoration stay with the callers in generator.js.
//
// Per slot (in stable id order inside a stable map order), the filtering order is:
//   1. replacement chance      - the slot's first draw from its own stream (`slot:<id>`, "enemy-placement-v1");
//   2. replacement mode        - `none` leaves every regular slot alone;
//   3. Placement fit           - types no larger than the slot (never relaxed);
//   4. difficulty              - Easy Asylum cap, or the curve class (second draw), or nothing (fully random);
//   5. Prevent same            - the slot's own model is rejected;
//   6. special exclusions      - none for regular-to-regular replacement (Mimic/mosquito slots never enter);
//   7. model budget            - a model is admitted only while the map stays within its effective cap;
//   8. candidate sampling      - uniform draw over the surviving TYPES with list shrink;
//   then the bounded fallback ladder (keep vanilla, in-set fitting type, failure) and the variant draw.
// A map whose slots cannot all be placed is planned again from scratch with attempt-specific streams (at most
// MAP_ATTEMPTS attempts); other maps are never touched. The model set of a map is a ModelBudget: fixed and
// vanilla-kept models are in it from the start, together with a coverage reserve that gives every participating
// slot an in-set type that fits it.

import { createHash } from "node:crypto";
import { DeterministicRng, deriveSeed } from "./rng.js";
import {
  EnemyPlacementInfeasibleError,
  MODEL_BUDGET_CAP,
  SAMPLING_RNG_VERSION,
  ModelBudget,
  EnemyPlacementCustomPoolError,
  ModelBudgetError,
  ModelLedger,
  createPlacementDiagnostics,
  createSlotStream,
  createTypeStream,
} from "./placement.js";

const compare = (left, right) => (left < right ? -1 : left > right ? 1 : 0);

// ---- derived classes ----------------------------------------------------------------------------------------------

export const CLASS_TABLE_VERSION = "regular-class-v2";

/** Footprint edges (game units) between the size classes 0-5. Derived scale, not a copy of any tool's table. */
export const SIZE_EDGES = Object.freeze([0.9, 1.7, 2.6, 4.2, 7]);

/** Size class given to a slot whose capsule is unknown (it accepts any type). */
export const UNKNOWN_SLOT_SIZE = SIZE_EDGES.length;

/** Difficulty classes are 0-7. */
export const DIFFICULTY_CLASS_COUNT = 8;
const MAX_DIFFICULTY_CLASS = DIFFICULTY_CLASS_COUNT - 1;

/**
 * Play-order progression of the maps, 0 (Asylum) to 7 (Kiln): an authored DS1TR table that says where in the game
 * a map sits, not how hard any enemy is. A model's difficulty is derived from where it natively appears.
 */
export const MAP_PROGRESSION_TIER = Object.freeze({
  m18_01_00_00: 0, m10_01_00_00: 1, m10_02_00_00: 1, m10_00_00_00: 2, m12_00_00_00: 2, m12_00_00_01: 2,
  m13_00_00_00: 3, m11_00_00_00: 3, m14_00_00_00: 4, m16_00_00_00: 4, m15_00_00_00: 5, m15_01_00_00: 5,
  m13_02_00_00: 5, m12_01_00_00: 6, m13_01_00_00: 6, m14_01_00_00: 6, m17_00_00_00: 6, m18_00_00_00: 7,
});

/** Weights of the difficulty score: where the model lives, and how much HP it has (rank among regular models). */
const AREA_WEIGHT = 0.6;
const HP_WEIGHT = 0.4;

export class EnemyPlacementClassError extends Error {
  constructor(subject, reason = "no usable hit capsule for any regular slot") {
    super(`ENEMY_PLACEMENT_CLASS_UNDERIVABLE: ${reason} (${subject}).`);
    this.name = "EnemyPlacementClassError";
    this.code = "ENEMY_PLACEMENT_CLASS_UNDERIVABLE";
    this.model = subject;
  }
}

/** Footprint of a catalog slot: the larger of the capsule diameter and height; null when the capsule is unusable. */
export function slotFootprint(slot) {
  const height = slot.hitHeight;
  const radius = slot.hitRadius;
  if (!(Number.isFinite(height) && height > 0 && Number.isFinite(radius) && radius > 0)) return null;
  return Math.max(2 * radius, height);
}

/** Size class 0-5: the number of edges the footprint reaches. */
export function sizeClassOfFootprint(footprint) {
  return SIZE_EDGES.filter((edge) => footprint >= edge).length;
}

/** 0..1 rank of a log-HP value among the sorted log-HP values of the regular models. */
function hpRank(sortedLogHp, logHp) {
  const index = sortedLogHp.findIndex((value) => value >= logHp);
  return (index === -1 ? sortedLogHp.length - 1 : index) / Math.max(1, sortedLogHp.length - 1);
}

function difficultyOfScore(tier, rank) {
  const score = AREA_WEIGHT * (tier / MAX_DIFFICULTY_CLASS) + HP_WEIGHT * rank;
  return Math.max(0, Math.min(MAX_DIFFICULTY_CLASS, Math.floor(score * DIFFICULTY_CLASS_COUNT)));
}

function tierOfMap(mapId, subject) {
  const tier = MAP_PROGRESSION_TIER[mapId];
  if (tier === undefined) throw new EnemyPlacementClassError(subject, `map ${mapId} has no progression tier`);
  return tier;
}

const lowerMedian = (sortedAscending) => sortedAscending[Math.floor((sortedAscending.length - 1) / 2)];
const upperMedian = (sortedAscending) => sortedAscending[Math.floor(sortedAscending.length / 2)];

/**
 * The effective class table of one run: for every model that occurs in a regular slot, its size class, its
 * difficulty class and its variants. Nothing depends on catalog order.
 *
 * - size: the model's footprint is the largest usable footprint among its regular slots; class = edges reached;
 * - difficulty: `floor(8 * (0.6 * tier / 7 + 0.4 * hpRank))`, where `tier` is the upper median of the progression
 *   tiers of the maps the model occurs in and `hpRank` is the rank of the model's median log HP among all regular
 *   models (0..1). A model without a usable HP or in an unknown map raises ENEMY_PLACEMENT_CLASS_UNDERIVABLE.
 *
 * The hash covers the version, the edges, the progression table and every (model, size, difficulty).
 */
export function deriveRegularClassTable(slots) {
  const byModel = new Map();
  for (const slot of slots) {
    if (!byModel.has(slot.modelName)) byModel.set(slot.modelName, []);
    byModel.get(slot.modelName).push(slot);
  }
  const draft = [];
  for (const model of [...byModel.keys()].sort(compare)) {
    const members = byModel.get(model).sort((left, right) => compare(left.id, right.id));
    const footprints = members.map(slotFootprint).filter((value) => value !== null);
    if (footprints.length === 0) throw new EnemyPlacementClassError(model);
    const hp = members.map((member) => member.baseHp).filter((value) => Number.isFinite(value) && value > 0).sort((a, b) => a - b);
    if (hp.length === 0) throw new EnemyPlacementClassError(model, "no usable base HP for any regular slot");
    const tiers = members.map((member) => tierOfMap(member.mapId, model)).sort((a, b) => a - b);
    const variants = new Map();
    for (const member of members) {
      const key = `${member.npcParamId}:${member.thinkParamId}:${member.battleGoalId}`;
      if (!variants.has(key)) variants.set(key, { key, npcParamId: member.npcParamId, thinkParamId: member.thinkParamId, source: member });
    }
    draft.push({
      model,
      size: sizeClassOfFootprint(Math.max(...footprints)),
      footprint: Math.max(...footprints),
      logHp: Math.log(lowerMedian(hp)),
      tier: upperMedian(tiers),
      variants: [...variants.values()].sort((left, right) => compare(left.key, right.key)),
    });
  }
  const sortedLogHp = draft.map((type) => type.logHp).sort((a, b) => a - b);
  const types = draft.map((type) => ({
    model: type.model,
    size: type.size,
    footprint: type.footprint,
    difficulty: difficultyOfScore(type.tier, hpRank(sortedLogHp, type.logHp)),
    variants: type.variants,
  }));
  const hash = createHash("sha256")
    .update(JSON.stringify({
      version: CLASS_TABLE_VERSION,
      edges: SIZE_EDGES,
      tiers: Object.entries(MAP_PROGRESSION_TIER).sort(([a], [b]) => compare(a, b)),
      types: types.map((type) => [type.model, type.size, type.difficulty]),
    }))
    .digest("hex");
  return {
    version: CLASS_TABLE_VERSION, hash, types, sortedLogHp, byModel: new Map(types.map((type) => [type.model, type])),
  };
}

/** Size class of a slot: its model's class, or its own capsule (any type fits a slot whose capsule is unknown). */
export function slotSizeClass(table, slot) {
  const type = table.byModel.get(slot.modelName);
  if (type) return type.size;
  const footprint = slotFootprint(slot);
  return footprint === null ? UNKNOWN_SLOT_SIZE : sizeClassOfFootprint(footprint);
}

/**
 * Destination difficulty of a slot: the class of its own model; a slot whose model is not a regular type (a
 * Pinwheel clone) uses its map tier and its own HP with the same formula. Never an arbitrary default.
 */
export function slotDifficultyClass(table, slot) {
  const type = table.byModel.get(slot.modelName);
  if (type) return type.difficulty;
  if (!(Number.isFinite(slot.baseHp) && slot.baseHp > 0)) throw new EnemyPlacementClassError(slot.id, "no usable base HP for a slot without a regular type");
  return difficultyOfScore(tierOfMap(slot.mapId, slot.id), hpRank(table.sortedLogHp, Math.log(slot.baseHp)));
}

// ---- difficulty modes, strictness and the eligibility matrix -----------------------------------------------------------

/** The Asylum map. Easy Asylum applies to every regular slot in it. */
export const EASY_ASYLUM_MAP = "m18_01_00_00";

/** Highest candidate difficulty class an Easy Asylum slot accepts. */
export const EASY_ASYLUM_MAX_CLASS = 1;

const CURVE_MODES = new Set(["curve", "curve-easy-asylum"]);
const EASY_ASYLUM_MODES = new Set(["random-easy-asylum", "curve-easy-asylum"]);

/**
 * Authored offset distributions of the difficulty curve ([class offset, percent]; the rest of the 100 stays on the
 * destination's own class). They follow the documented behavior of the three settings - strict keeps to the own
 * class and rarely goes harder, a bit loose goes harder more often and up to a few classes, very loose can reach
 * any class from any class - and are DS1TR values, not a copy of any tool's table.
 */
export const STRICTNESS_OFFSETS = Object.freeze({
  strict: [[2, 4], [1, 11], [-1, 5]],
  loose: [[5, 1], [4, 2], [3, 4], [2, 8], [1, 14], [-1, 6], [-2, 3], [-3, 2]],
  "very-loose": [[7, 1], [6, 2], [5, 4], [4, 6], [3, 8], [2, 10], [1, 13], [-1, 7], [-2, 4], [-3, 3], [-4, 2]],
});

/**
 * Probability (percent, integers summing to 100) of each target class for a destination class under a strictness.
 * An offset that leaves 0-7 is skipped and its mass stays on the destination's own class.
 */
export function difficultyDistribution(destination, strictness) {
  const offsets = STRICTNESS_OFFSETS[strictness];
  if (!offsets) throw new RangeError(`Unknown difficulty strictness ${String(strictness)}.`);
  if (!Number.isInteger(destination) || destination < 0 || destination > MAX_DIFFICULTY_CLASS) throw new RangeError(`Destination class ${String(destination)} is outside 0-${MAX_DIFFICULTY_CLASS}.`);
  const distribution = new Array(DIFFICULTY_CLASS_COUNT).fill(0);
  let moved = 0;
  for (const [offset, percent] of offsets) {
    const target = destination + offset;
    if (target < 0 || target > MAX_DIFFICULTY_CLASS) continue;
    distribution[target] += percent;
    moved += percent;
  }
  distribution[destination] += 100 - moved;
  return distribution;
}

/** The class a draw in 0..99 selects: cumulative over the classes in ascending order. */
export function difficultyTargetClass(destination, strictness, draw) {
  const distribution = difficultyDistribution(destination, strictness);
  let sum = 0;
  for (let cls = 0; cls < DIFFICULTY_CLASS_COUNT; cls += 1) {
    sum += distribution[cls];
    if (draw < sum) return cls;
  }
  return destination;
}

/**
 * The eligibility matrix: which candidate difficulty classes can replace a destination of class `destination`.
 * Fully random: all classes. Curve: the classes with a non-zero chance. Easy Asylum slots (modes with easy Asylum,
 * in the Asylum map): classes 0..EASY_ASYLUM_MAX_CLASS whatever the mode. This is eligibility before the physical
 * and budget filters; the planner's fallbacks (own class, difficulty lapse under budget pressure) are separate.
 */
export function eligibleDifficultyClasses({ mode, strictness, destination, mapId }) {
  if (EASY_ASYLUM_MODES.has(mode) && mapId === EASY_ASYLUM_MAP) {
    return Array.from({ length: EASY_ASYLUM_MAX_CLASS + 1 }, (_, cls) => cls);
  }
  if (CURVE_MODES.has(mode)) {
    return difficultyDistribution(destination, strictness).map((percent, cls) => (percent > 0 ? cls : -1)).filter((cls) => cls >= 0);
  }
  return Array.from({ length: DIFFICULTY_CLASS_COUNT }, (_, cls) => cls);
}

// ---- model budget with a coverage reserve ----------------------------------------------------------------------------

/**
 * The initial model set of one map (model document 6.6): fixed models, vanilla models of slots that do not
 * participate, and a coverage reserve of vanilla models of the smallest participating slots - enough that every
 * participating slot has an in-set type that fits it (two distinct ones under Prevent same). A reserve entry is
 * skipped when the set already holds a fitting type. If the reserve would push the set over the cap, the entries
 * that do not fit are dropped (`trimmed`) and the fallback ladder is the safety net; demoting slots to
 * non-participants cannot help, because a demoted slot keeps its vanilla model in the set. A map whose fixed and
 * non-participating models alone exceed the cap cannot be repaired and fails with
 * ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE.
 */
export function buildMapModelSet({ mapId, cap, fixedModels, participants, sizeOf, fitSize, preventSame, table, pool = null, nominalCap = cap }) {
  const base = new Set(fixedModels);
  if (base.size > cap) throw new ModelBudgetError(mapId, cap, [...base]);
  const active = [...participants].sort((left, right) => sizeOf(left) - sizeOf(right) || compare(left.id, right.id));
  if (pool !== null && pool.excluded > 0) {
    // A restricted custom pool: the reserve is a coverage witness (see coverWitness). It is trimmed to the room under the cap
    // like any reserve, unless the one-model grace of resolveModelCap proves it fits at nominal + 1. The full pool takes the
    // original path below unchanged.
    const { witness, exact } = coverWitnessSearch({ base, subjects: active.map((slot) => ({ own: slot.modelName, maxSize: fitSize(slot) })), pool, preventSame });
    const model = resolveModelCap({ nominalCap, baseSize: base.size, coverSize: witness.length, exact });
    const reserve = witness.slice(0, Math.max(0, model.effectiveCap - base.size));
    return {
      budget: new ModelBudget({ mapId, cap: model.effectiveCap, fixedModels: [...base, ...reserve] }),
      reserve, trimmed: witness.length - reserve.length, kept: active, cap: model.effectiveCap, model,
    };
  }
  const wanted = [];
  const isCandidate = (model) => table.byModel.has(model);
  if (active.length > 0) {
    const ceiling = fitSize(active[0]);
    const needed = preventSame ? 2 : 1;
    const fitting = [...base].filter((model) => isCandidate(model) && table.byModel.get(model).size <= ceiling).length;
    for (const slot of active) {
      if (fitting + wanted.length >= needed) break;
      if (!base.has(slot.modelName) && !wanted.includes(slot.modelName) && isCandidate(slot.modelName)) wanted.push(slot.modelName);
    }
  }
  const reserve = wanted.slice(0, Math.max(0, cap - base.size));
  return {
    budget: new ModelBudget({ mapId, cap, fixedModels: [...base, ...reserve] }),
    reserve,
    trimmed: wanted.length - reserve.length,
    kept: active,
    cap,
  };
}

// ---- coverage witness (restricted custom pool) ------------------------------------------------------------------------

/** Bound on the exact search of coverWitness; the greedy cover is the answer when it is reached. Deterministic. */
const WITNESS_NODE_LIMIT = 20000;

/**
 * The models a map must hold so that every participating subject (a slot, or a Type replacement group) can always be
 * served from inside the set: each keeps its own model or takes a pool model that fits it (and is not its own under
 * Prevent same). A subject is served when one of those options is already in `base` (fixed models, models of subjects
 * left alone). The result is a minimal set of further models (an exact cover search, bounded; the greedy cover when the
 * bound is hit), ordered by how many subjects each model serves. With the full pool a fitting in-set model always
 * exists once the first one does, so the plain coverage reserve is enough; a restricted pool can leave a subject with
 * nothing in the set, which is what this prevents. The caller trims it to the room under the effective cap like any
 * reserve - the non-worsening cap is never lifted for it. Pure and deterministic.
 */
export function coverWitness(input) {
  return coverWitnessSearch(input).witness;
}

/** coverWitness plus whether the cover is proven minimal (the bounded search finished). */
export function coverWitnessSearch({ base, subjects, pool, preventSame }) {
  const lists = new Map();
  const subjectOwn = new Map();
  for (const subject of subjects) {
    const key = `${subject.own}:${subject.maxSize}`;
    if (lists.has(key)) continue;
    const options = new Set([subject.own]);
    for (const type of pool.types) {
      if (type.size <= subject.maxSize && !(preventSame && type.model === subject.own)) options.add(type.model);
    }
    lists.set(key, [...options]);
    subjectOwn.set(lists.get(key), subject.own);
  }
  const open = [...lists.values()].filter((options) => !options.some((model) => base.has(model)));
  if (open.length === 0) return { witness: [], exact: true };
  const coverage = new Map();
  for (const options of open) for (const model of options) coverage.set(model, (coverage.get(model) ?? 0) + 1);
  const rank = (left, right) => coverage.get(right) - coverage.get(left)
    || (pool.models.has(right) ? 1 : 0) - (pool.models.has(left) ? 1 : 0) || compare(left, right);
  for (const options of open) options.sort(rank);

  let best = [];
  for (let rest = open; rest.length > 0;) {
    const counts = new Map();
    for (const options of rest) for (const model of options) counts.set(model, (counts.get(model) ?? 0) + 1);
    const pick = [...counts.keys()].sort((left, right) => counts.get(right) - counts.get(left)
      || (pool.models.has(right) ? 1 : 0) - (pool.models.has(left) ? 1 : 0) || compare(left, right))[0];
    best.push(pick);
    rest = rest.filter((options) => !options.includes(pick));
  }
  let nodes = 0;
  const search = (chosen, rest) => {
    if (rest.length === 0) {
      if (chosen.length < best.length) best = [...chosen];
      return;
    }
    if (chosen.length + 1 >= best.length || nodes >= WITNESS_NODE_LIMIT) return;
    nodes += 1;
    let target = rest[0];
    for (const options of rest) if (options.length < target.length) target = options;
    for (const model of target) {
      chosen.push(model);
      search(chosen, rest.filter((options) => !options.includes(model)));
      chosen.pop();
    }
  };
  search([], open);
  // Never more than every subject keeping its own model (the vanilla map).
  const keepAll = [...new Set(open.map((options) => subjectOwn.get(options)))];
  if (keepAll.length < best.length) best = keepAll;
  return { witness: [...best].sort(rank), exact: nodes < WITNESS_NODE_LIMIT };
}

// ---- effective budget -------------------------------------------------------------------------------------------------

/**
 * Non-worsening model budget (model document 10.2). The target is `cap` (30) distinct final models per map. When
 * the models outside the sampler's control - fixed plans plus the vanilla models of slots it leaves alone -
 * already exceed it, that baseline is the cap for the map: the sampler may keep or reduce it, never raise it.
 */
export const effectiveModelBudget = (baseline, cap = MODEL_BUDGET_CAP) => Math.max(cap, baseline);

/**
 * The cap of one map for a restricted custom pool (Part C2 decision: controlled model grace). `baseSize` is the models
 * that stay whatever happens and `coverSize` the fewest further models that let every participating subject keep its
 * model or take a fitting pool model, so `baseSize + coverSize` is the smallest final model count the map can have.
 *   - it fits the base cap max(nominal, baseSize): the cap is that base cap (the grace is not used);
 *   - it is exactly nominal + 1 on a map whose base cap is the nominal target (baseSize <= nominal), and the cover is
 *     proven minimal (`exact`): the cap is nominal + 1, a one-model grace. 31 is a count permutation-v1 already produces;
 *   - anything else: the cap stays the base cap. A map whose baseline is already above nominal gets no grace at all - the
 *     pool may keep or reduce that overflow, never add to it - and no map ever gets more than nominal + 1.
 * `feasible` is false only when the shortfall is proven (exact cover).
 */
export function resolveModelCap({ nominalCap, baseSize, coverSize, exact }) {
  const baseCap = effectiveModelBudget(baseSize, nominalCap);
  const minimumFeasible = baseSize + coverSize;
  if (minimumFeasible <= baseCap) return { effectiveCap: baseCap, baseCap, minimumFeasible, grace: false, feasible: true };
  if (exact && baseCap === nominalCap && minimumFeasible === nominalCap + 1) {
    return { effectiveCap: nominalCap + 1, baseCap, minimumFeasible, grace: true, feasible: true };
  }
  return { effectiveCap: baseCap, baseCap, minimumFeasible, grace: false, feasible: !exact };
}

// ---- candidate pool ----------------------------------------------------------------------------------------------------

/**
 * The replacement types the sampler may draw from. Today every regular model qualifies; a custom enemy pool only has
 * to pass `accept` (a predicate over a class-table type) and nothing downstream - slot sampling, Type replacement
 * groups, the in-set fallback - changes.
 */
export function createCandidatePool(table, accept = () => true) {
  const types = table.types.filter(accept);
  return Object.freeze({
    types, models: new Set(types.map((type) => type.model)), total: types.length, excluded: table.types.length - types.length,
  });
}

// ---- custom pool ---------------------------------------------------------------------------------------------------------

export const CUSTOM_POOL_HASH_VERSION = "custom-pool-v1";

/** Stable fingerprint of a canonical (sorted, unique) custom pool; null for the full pool. */
export function customPoolHash(models) {
  if (models === null) return null;
  return createHash("sha256").update(JSON.stringify({ version: CUSTOM_POOL_HASH_VERSION, models })).digest("hex");
}

/**
 * Coverage of a candidate pool: how many selected types exist per size class (0-5) and per difficulty class (0-7),
 * and how many of the given slots have no candidate that fits them under Placement fit. Deterministic, static; the
 * planner remains the authority for map-level feasibility.
 */
export function analyzePoolCoverage({ table, pool, slots, fit, preventSame = false }) {
  const sizeCoverage = new Array(SIZE_EDGES.length + 1).fill(0);
  const difficultyCoverage = new Array(DIFFICULTY_CLASS_COUNT).fill(0);
  for (const type of pool.types) {
    sizeCoverage[type.size] += 1;
    difficultyCoverage[type.difficulty] += 1;
  }
  const smallest = pool.types.reduce((min, type) => Math.min(min, type.size), Infinity);
  // Slots smaller than every selected enemy: under Placement fit "only where they fit" nothing can ever replace them.
  const tooSmall = fit === "fits" ? slots.filter((slot) => slotSizeClass(table, slot) < smallest) : [];
  // A slot can take a candidate that fits it (Placement fit) and, under Prevent same, is not its own model.
  const slotsWithoutFit = slots.filter((slot) => {
    const size = slotSizeClass(table, slot);
    return !pool.types.some((type) => (fit !== "fits" || type.size <= size) && !(preventSame && type.model === slot.modelName));
  }).length;
  return {
    total: table.types.length,
    selected: pool.total,
    sizeCoverage,
    difficultyCoverage,
    smallestSize: pool.total === 0 ? null : smallest,
    slotsTotal: slots.length,
    slotsWithoutFit,
    slotsBelowSmallest: tooSmall.length,
    sizeClassesBelowSmallest: [...new Set(tooSmall.map((slot) => slotSizeClass(table, slot)))].sort((left, right) => left - right),
  };
}

/**
 * Validates a custom pool against the class table of the run and the configuration. Structural problems raise the
 * typed, never-retryable ENEMY_PLACEMENT_CUSTOM_POOL_INVALID: an unknown or non-regular model (a stale selection),
 * and - when the pool will be consulted - a pool no regular slot can take under Placement fit and Prevent same. Gaps that only make
 * some slots unreplaceable are reported by analyzePoolCoverage, not raised.
 */
export function resolveCustomPool({ models, table, slots, settings, preventSame = false }) {
  if (models === null) return null;
  const unknown = models.filter((model) => !table.byModel.has(model));
  if (unknown.length > 0) {
    throw new EnemyPlacementCustomPoolError("unknown",
      `${unknown.length} selected model(s) are not regular enemy candidates of this game data: ${unknown.slice(0, 10).join(", ")}${unknown.length > 10 ? ", ..." : ""}.`,
      { unknown });
  }
  const set = new Set(models);
  const pool = createCandidatePool(table, (type) => set.has(type.model));
  const consulted = settings.normalReplacement !== "none" && settings.replacementChance > 0;
  if (consulted) {
    const coverage = analyzePoolCoverage({ table, pool, slots, fit: settings.fit, preventSame });
    if (coverage.slotsWithoutFit === coverage.slotsTotal && coverage.slotsTotal > 0) {
      throw new EnemyPlacementCustomPoolError("no-candidate",
        `no regular slot can take any selected enemy (${settings.fit === "fits" ? `Placement fit "only where they fit", smallest selected size class ${coverage.smallestSize}` : "Placement fit anywhere"}${preventSame ? ", Prevent same enemy on" : ""}); `
        + "select more or different enemies, or relax Placement fit or Prevent same.",
        { smallestSize: coverage.smallestSize });
    }
  }
  return models;
}

// ---- fallback ladder -----------------------------------------------------------------------------------------------

/**
 * What a slot (or a Type replacement group) receives when the sampled draw found no candidate (model document 6.7,
 * regular slots). Pure and bounded: every rung is a scan over a sorted list, nothing draws again.
 *   kept-vanilla - the subject's own model is in the set or the set has room (allowed under Prevent same);
 *   in-set-fit   - the smallest in-set type that fits (and is not its own model under Prevent same, nor in `excluded`);
 *   infeasible   - nothing is left; the caller retries the map with attempt-specific streams, and raises
 *                  ENEMY_PLACEMENT_INFEASIBLE when every attempt fails.
 * There is deliberately no "fit relaxed" rung: with the non-worsening budget a subject can be left without any
 * fitting type only when the map has no headroom, and placing an oversized type would break the documented fit
 * invariant. `chosen` is null for kept-vanilla and infeasible. The in-set rung ignores difficulty (a counted
 * difficulty lapse): physical fit is never relaxed, difficulty may be.
 */
export function fallbackLadder({ slot, budget, table, pool = null, maxSize, preventSame, excluded = new Set() }) {
  if (budget.evaluate(slot.modelName).admitted) return { stage: "kept-vanilla", chosen: null };
  const inSet = budget.models().map((model) => table.byModel.get(model)).filter(Boolean)
    .filter((type) => !pool || pool.models.has(type.model))
    .filter((type) => !(preventSame && type.model === slot.modelName) && !excluded.has(type.model))
    .filter((type) => budget.evaluate(type.model).admitted)
    .sort((left, right) => left.size - right.size || compare(left.model, right.model));
  const fitting = inSet.find((type) => type.size <= maxSize);
  if (fitting) return { stage: "in-set-fit", chosen: fitting };
  return { stage: "infeasible", chosen: null };
}

/**
 * Last rung of a Type replacement group (after sampling, keeping the original model and the injective in-set scan):
 * give the group a model that is already in the map even if another group received it, i.e. relax the legacy rule that
 * two kinds in one map never get the same replacement. The rule is a variety preference of the original tool; the model
 * budget is a safety limit, and with a fixed cap and many groups the preference cannot always hold. Only models that
 * fit every member (and are not the group's own under Prevent same) qualify; models held by something that stays
 * (solid) come first because they shrink the map, then the nearest difficulty class, then the model id; ties are
 * broken by one draw. Bounded and never relaxes fit or the budget.
 */
export function sharedFallback({ group, budget, pool, table, maxSize, preventSame, destination, rng }) {
  const candidates = budget.models()
    .map((model) => table.byModel.get(model))
    .filter((type) => type && pool.models.has(type.model) && type.size <= maxSize && !(preventSame && type.model === group.modelName))
    .map((type) => ({ type, solid: budget.isSolid(type.model) ? 0 : 1, distance: Math.abs(type.difficulty - destination) }))
    .sort((a, b) => a.solid - b.solid || a.distance - b.distance || compare(a.type.model, b.type.model));
  if (candidates.length === 0) return null;
  const best = candidates.filter((c) => c.solid === candidates[0].solid && c.distance === candidates[0].distance);
  return best[rng.int(best.length)].type;
}

/**
 * True when no type of a restricted pool can ever replace this subject (a slot, or a Type replacement group with its
 * smallest member fit): nothing in the pool fits it, or the only fitting type is its own model under Prevent same. Such a
 * subject stays as it is whatever the draws say, so its model belongs to the baseline of the map - the models the sampler
 * cannot remove. Difficulty is not consulted: it can lapse, fit cannot. Only evaluated for a restricted pool; the full pool
 * is treated as in Part C1.
 */
function cannotBeReplaced({ modelName, maxSize, pool, preventSame }) {
  return !pool.types.some((type) => type.size <= maxSize && !(preventSame && type.model === modelName));
}

/**
 * Structural check for one map at a replacement chance of 100%, where every replaceable subject participates and the
 * outcome does not depend on a draw: the models that stay whatever happens (fixed plans and the subjects the pool can
 * never replace) plus the least a cover of the remaining subjects needs must fit the map's cap. When the cover is proven
 * minimal and still does not fit, no placement exists for the map under this pool and configuration - retrying cannot
 * help. Below 100% a participation draw decides, so the same shortfall is only a stochastic per-attempt failure.
 */
function staticMapVerdict({ subjects, fixed, pool, preventSame, cap }) {
  const stays = new Set(fixed);
  const open = [];
  for (const subject of subjects) {
    if (cannotBeReplaced({ modelName: subject.own, maxSize: subject.maxSize, pool, preventSame })) stays.add(subject.own);
    else open.push(subject);
  }
  const { witness, exact } = coverWitnessSearch({ base: stays, subjects: open, pool, preventSame });
  const model = resolveModelCap({ nominalCap: cap, baseSize: stays.size, coverSize: witness.length, exact });
  return { feasible: model.feasible, baseline: stays.size, effectiveCap: model.effectiveCap, needed: witness.length, grace: model.grace };
}

// ---- candidate selection (shared by slots and Type replacement groups) ----------------------------------------------------

/**
 * Steps 3-8 of the filtering order for one subject: a slot, or a Type replacement group (id, modelName). Draws only
 * from `rng`. `excluded` holds the models already given to other groups of the map (Type replacement injectivity);
 * it is empty for ordinary slots.
 */
function sampleCandidate({ rng, subject, mapId, maxSize, destinationOf, settings, pool, budget, preventSame, excluded, diagnostics }) {
  const curve = CURVE_MODES.has(settings.difficultyMode);
  const easyAsylum = EASY_ASYLUM_MODES.has(settings.difficultyMode) && mapId === EASY_ASYLUM_MAP;
  const fitPool = pool.types.filter((type) => type.size <= maxSize);
  const rejected = [];
  const info = {};
  let budgetBlocked = false;

  // Candidate pools, tried in order; each is drawn uniformly with list shrink (steps 5-8 of the filtering order).
  const pools = [];
  if (easyAsylum) {
    pools.push(fitPool.filter((type) => type.difficulty <= EASY_ASYLUM_MAX_CLASS));
  } else if (curve) {
    const destination = destinationOf();
    const target = difficultyTargetClass(destination, settings.difficultyStrictness, rng.int(100));
    info.destinationClass = destination;
    info.targetClass = target;
    const targetPool = fitPool.filter((type) => type.difficulty === target);
    if (targetPool.length === 0) {
      diagnostics.count("classFallbacks");
      rejected.push({ model: `class-${target}`, reason: "difficulty-empty" });
    } else {
      pools.push(targetPool);
    }
    if (target !== destination) pools.push(fitPool.filter((type) => type.difficulty === destination));
  } else {
    pools.push(fitPool);
  }
  if (pools.length > 0) diagnostics.count("candidatesOutOfClass", Math.max(0, fitPool.length - pools[0].length));

  const draw = (poolList) => {
    const list = [...poolList];
    while (list.length > 0) {
      const index = rng.int(list.length);
      const candidate = list[index];
      if (preventSame && candidate.model === subject.modelName) {
        rejected.push({ model: candidate.model, reason: "prevent-same" });
      } else if (excluded.has(candidate.model)) {
        rejected.push({ model: candidate.model, reason: "type-injectivity" });
      } else if (!budget.evaluate(candidate.model).admitted) {
        rejected.push({ model: candidate.model, reason: "model-budget" });
        budgetBlocked = true;
      } else {
        return candidate;
      }
      list.splice(index, 1);
    }
    return null;
  };

  let chosen = null;
  for (let i = 0; i < pools.length && !chosen; i += 1) {
    chosen = draw(pools[i]);
    if (!chosen && i > 0) diagnostics.count("classFallbacks");
  }
  // Difficulty lapse (model document D-1): a full map keeps the class filter while it can and, only when the budget
  // has removed every candidate of the wanted classes, widens it class by class away from the destination. The
  // physical fit is never relaxed.
  if (!chosen && curve && !easyAsylum && budgetBlocked) {
    // Nearest classes first: the classes at distance 1 from the destination (either side), then 2, ... 7.
    const destination = info.destinationClass;
    for (let distance = 1; distance <= MAX_DIFFICULTY_CLASS && !chosen; distance += 1) {
      chosen = draw(fitPool.filter((type) => Math.abs(type.difficulty - destination) === distance));
    }
    if (chosen) diagnostics.count("difficultyLapses");
  }
  const sizeRejections = pool.total - fitPool.length;
  if (sizeRejections > 0) diagnostics.count("candidatesOverSize", sizeRejections);
  if (pool.excluded > 0) diagnostics.count("candidatesOutsideCustomPool", pool.excluded);
  return { chosen, rejected, info, curve, easyAsylum, sizeRejections };
}

// ---- the planner ------------------------------------------------------------------------------------------------------

/** Total attempts per map (the first one plus two map-local retries). */
export const MAP_ATTEMPTS = 3;

/**
 * Plans sampling-v1 for regular slots. Returns `assignment`: slot id -> chosen variant source (a catalog slot)
 * for slots that receive a placement; a slot that stays vanilla has no entry.
 *
 * @param {object} input
 * @param {string} input.seed                    combined seed (the stream seed of the whole run)
 * @param {{replacementChance:number, normalReplacement:string, fit:string, difficultyMode:string, difficultyStrictness:string, typeReplacement:string}} input.options
 * @param {boolean} input.preventSame
 * @param {Array<object>} input.slots            regular catalog slots (class table is derived from these)
 * @param {Array<object>} [input.extraSlots]     further regular-class slots with no type of their own (Pinwheel clones)
 * @param {Map<string, Set<string>>} input.fixedModelsByMap
 * @param {object} input.diagnostics             from createPlacementDiagnostics
 * @param {(type: object) => boolean} [input.candidateFilter]  restricts the replacement types (custom pool hook)
 */
export function planSampledRegularEnemies({
  seed, options, preventSame, slots, extraSlots = [], fixedModelsByMap, diagnostics, cap = MODEL_BUDGET_CAP,
  mapAttempts = MAP_ATTEMPTS, candidateFilter = undefined,
}) {
  // The boss options belong to planSampledBosses; they never reach the regular-slot settings (and so not their diagnostics).
  // The roaming-boss options (Part E1) belong to planRoamingBosses in the same way.
  const {
    bossReplacement: _bossReplacement, uniqueBosses: _uniqueBosses, bossChanceInNormalSlots: _bossChance, roamingBossSouls: _roamingSouls,
    roamingBossesStayDead: _roamingStayDead, bossChanceInBossSlots: _bossChanceBossSlots, ...regularOptions
  } = options;
  const settings = {
    ...regularOptions,
    difficultyMode: options.difficultyMode ?? "random",
    difficultyStrictness: options.difficultyStrictness ?? "loose",
    typeReplacement: options.typeReplacement ?? "disabled",
  };
  const table = deriveRegularClassTable(slots);
  const customModels = resolveCustomPool({ models: settings.customPool ?? null, table, slots: [...slots, ...extraSlots], settings, preventSame });
  const customSet = customModels === null ? null : new Set(customModels);
  const pool = createCandidatePool(table, (type) => (customSet === null || customSet.has(type.model)) && (candidateFilter === undefined || candidateFilter(type)));
  diagnostics.classTableHash = table.hash;
  diagnostics.options = { ...settings, customPool: undefined, preventSame };
  diagnostics.customPool = {
    enabled: customModels !== null, hash: customPoolHash(customModels),
    ...analyzePoolCoverage({ table, pool, slots: [...slots, ...extraSlots], fit: settings.fit, preventSame }),
  };
  const assignment = new Map();
  const all = [...slots, ...extraSlots];
  const mapIds = [...new Set(all.map((slot) => slot.mapId))].sort(compare);
  const planMap = settings.typeReplacement === "disabled" ? planMapAttempt : planMapByType;

  if (pool.excluded > 0 && settings.normalReplacement !== "none" && settings.replacementChance === 100) {
    for (const mapId of mapIds) {
      const mapSlots = all.filter((slot) => slot.mapId === mapId);
      const subjects = settings.typeReplacement === "disabled"
        ? mapSlots.map((slot) => ({ own: slot.modelName, maxSize: settings.fit === "fits" ? slotSizeClass(table, slot) : UNKNOWN_SLOT_SIZE }))
        : [...new Set(mapSlots.map((slot) => slot.modelName))].map((model) => ({
          own: model,
          maxSize: Math.min(...mapSlots.filter((slot) => slot.modelName === model).map((slot) => (settings.fit === "fits" ? slotSizeClass(table, slot) : UNKNOWN_SLOT_SIZE))),
        }));
      const verdict = staticMapVerdict({ subjects, fixed: fixedModelsByMap.get(mapId) ?? [], pool, preventSame, cap });
      if (!verdict.feasible) {
        throw new EnemyPlacementCustomPoolError("map-infeasible",
          `no placement exists for map ${mapId}: with a replacement chance of 100% (every replaceable enemy takes part) the ${verdict.baseline} models that stay `
          + `(fixed plans and enemies the ${pool.total} selected enemies cannot replace under Placement fit "${settings.fit === "fits" ? "only where they fit" : "anywhere"}"${preventSame ? " and Prevent same enemy" : ""}) `
          + `plus ${verdict.needed} more that the rest would need exceed the map's limit of ${verdict.effectiveCap}; `
          + "select more enemies (small ones, or ones the map already holds), lower the replacement chance, or relax Placement fit.",
          { mapId, baseline: verdict.baseline, effectiveCap: verdict.effectiveCap, neededModels: verdict.needed, selected: pool.total, fit: settings.fit, typeReplacement: settings.typeReplacement });
      }
    }
  }

  for (const mapId of mapIds) {
    const mapSlots = all.filter((slot) => slot.mapId === mapId).sort((left, right) => compare(left.id, right.id));
    let planned = null;
    let lastError = null;
    for (let attempt = 0; attempt < mapAttempts; attempt += 1) {
      const scratch = createPlacementDiagnostics({ algorithm: "sampling-v1", level: diagnostics.level });
      try {
        planned = planMap({ seed, settings, preventSame, table, pool, mapId, mapSlots, attempt, fixedModelsByMap, cap, diagnostics: scratch });
      } catch (error) {
        if (!(error instanceof EnemyPlacementInfeasibleError)) throw error;
        lastError = error;
        diagnostics.count("mapAttemptsFailed");
        if (settings.typeReplacement !== "disabled") diagnostics.count("typeMapAttemptsFailed");
        diagnostics.addRetry({ mapId, attempt, slotId: error.slotId, baseline: error.baseline, effectiveCap: error.effectiveCap });
        continue;
      }
      diagnostics.absorb(scratch);
      if (attempt > 0) {
        diagnostics.count("mapsRetried");
        diagnostics.peak("maxMapAttempt", attempt);
      }
      break;
    }
    if (!planned) {
      diagnostics.peak("maxMapAttempt", mapAttempts - 1);
      throw new EnemyPlacementInfeasibleError(mapId, lastError.slotId,
        `${mapAttempts} map attempts failed; last: ${lastError.detail}; difficulty ${settings.difficultyMode}/${settings.difficultyStrictness}, fit ${settings.fit}, `
        + `type replacement ${settings.typeReplacement}, ${customModels === null ? 'full pool' : `custom pool ${customModels.length} of ${table.types.length} (${customPoolHash(customModels).slice(0, 12)})`}, prevent same ${preventSame}, chance ${settings.replacementChance}%, `
        + `baseline ${lastError.baseline}, effective cap ${lastError.effectiveCap}`,
        { attempts: mapAttempts, baseline: lastError.baseline, effectiveCap: lastError.effectiveCap, difficultyMode: settings.difficultyMode,
          difficultyStrictness: settings.difficultyStrictness, fit: settings.fit, typeReplacement: settings.typeReplacement, customPoolHash: customPoolHash(customModels), terminal: lastError.detail });
    }
    for (const [slotId, source] of planned.assignment) assignment.set(slotId, source);
    diagnostics.setMap(mapId, { ...planned.info, attempts: planned.attempt + 1 });
  }
  return { assignment, table };
}

// ---- boss sampling (Part D) -------------------------------------------------------------------------------------------

/**
 * Plans sampling-v1 for SINGLE-BODY boss slots (docs/ENEMY_PLACEMENT_MODEL.md, Part D). Linked units (dragons, Gargoyles, Four
 * Kings, Bed of Chaos, ...) are not slots of this planner: they stay with their own planners in both boss modes.
 *
 * Per slot, in stable slot-id order, each with its own stream (`slot:<id>`, "enemy-placement-v1"; boss slot ids never
 * collide with regular slot ids):
 *   1. replacement chance - the first draw of the slot's stream; a slot that fails it stays vanilla (no placement);
 *   2. candidates         - the portable boss archetypes except the slot's own boss and the authored unsafe pairs;
 *   3. draw               - uniform over the surviving archetypes with list shrink; a candidate is rejected
 *                           a. Gwyn nerf: Lord Gwyn with the legacy probability (a draw per evaluation, same stream),
 *                           b. Unique bosses: an already used boss while an unused candidate remains;
 *   4. nothing left       - the slot stays vanilla (recorded as kept-vanilla).
 * Unique bosses uses one list for the whole run (slot order); it starts again once every boss of the pool was used.
 * A boss may otherwise repeat any number of times. There is no model budget here: the fixed models of the boss
 * placements are what the regular-enemy planner then respects.
 *
 * Part E2 (a regular enemy in a boss slot) extends the same loop. With `options.bossReplacement` "normals" or "both" and a `bodies`
 * description, a participating slot of the supported set may take a normal enemy as the BODY of its encounter: the slot, its entity
 * and its events stay the boss's, only the body in the arena changes. "normals" always does, "both" draws once from its own stream
 * (`category:slot:<id>`) against `bossChanceInBossSlots`; a slot that is not supported keeps its boss in "normals" and can only
 * get a boss in "both". The body is drawn from its own stream (`body:slot:<id>`), so the boss draws of every other mode and slot are
 * untouched (with the share at 100 "both" is exactly boss sampling); it never takes part in the Unique bosses cycle; it must keep the map within `max(30, the map's vanilla model count)`.
 * Neither mode changes anything for "bosses" (no extra draw, no extra stream).
 *
 * @param {object} input
 * @param {string} input.seed
 * @param {{replacementChance:number, uniqueBosses:boolean, bossReplacement?:string, bossChanceInBossSlots?:number, fit?:string, difficultyMode?:string, difficultyStrictness?:string}} input.options
 * @param {Array<object>} input.slots                 single-body boss slots (catalog slots)
 * @param {Array<object>} input.pool                  portable boss archetypes (catalog slots, one per archetype)
 * @param {(slot: object) => string} input.keyOf      archetype key of a slot or archetype
 * @param {number} input.gwynRejection                probability that Lord Gwyn is rejected as a candidate (0 = nerf off)
 * @param {string} input.gwynModel
 * @param {(slot: object, candidate: object) => boolean} input.isUnsafePair
 * @param {object} input.diagnostics
 * @param {null | {supported: Set<string>, types: Array<object>, destination: (slot: object) => {size: number, difficulty: number}, modelCounts: Map<string, Map<string, number>>, vanillaSizes: Map<string, number>}} [input.bodies]
 * @returns {{assignment: Map<string, object>, bodySlots: Set<string>}}
 */
export function planSampledBosses({ seed, options, slots, pool, keyOf, gwynRejection, gwynModel, isUnsafePair, diagnostics, bodies = null }) {
  const byKey = new Map();
  for (const archetype of pool) if (!byKey.has(keyOf(archetype))) byKey.set(keyOf(archetype), archetype);
  const archetypes = [...byKey.entries()].sort(([a], [b]) => compare(a, b)).map(([, archetype]) => archetype);
  const assignment = new Map();
  const bodySlots = new Set();
  const used = new Set();
  const ordered = [...slots].sort((left, right) => compare(left.id, right.id));
  const mode = options.bossReplacement ?? "bosses";
  const bodyMode = bodies !== null && (mode === "normals" || mode === "both");
  // Running model counts of each map (a copy: the planner changes them as it decides), so a body is admitted against what the
  // earlier decisions of this same pass already did to the map.
  const counts = bodyMode ? new Map([...bodies.modelCounts].map(([mapId, models]) => [mapId, new Map(models)])) : null;
  const swap = (mapId, leaving, arriving) => {
    const models = counts.get(mapId) ?? new Map();
    counts.set(mapId, models);
    const left = (models.get(leaving) ?? 0) - 1;
    if (left <= 0) models.delete(leaving); else models.set(leaving, left);
    models.set(arriving, (models.get(arriving) ?? 0) + 1);
  };
  diagnostics.count("bossSlots", ordered.length);

  for (const slot of ordered) {
    const rng = createSlotStream(seed, slot);
    const participates = rng.int(100) + 1 <= options.replacementChance;
    if (!participates) {
      diagnostics.count("bossSlotsNotParticipating");
      continue;
    }
    const supported = bodyMode && bodies.supported.has(slot.id);
    let wantsBody = false;
    if (mode === "normals") {
      if (!supported) { diagnostics.count("bossSlotsKeptUnsupported"); continue; }
      wantsBody = true;
    } else if (mode === "both" && supported) {
      // Its own stream: with the share at 100 every slot draws its boss from the slot stream exactly as boss sampling does.
      const category = new DeterministicRng(deriveSeed(seed, `category:slot:${slot.id}`, SAMPLING_RNG_VERSION));
      wantsBody = category.int(100) + 1 > (options.bossChanceInBossSlots ?? 90);
    }
    if (wantsBody) {
      const chosen = chooseBody({ seed, slot, options, bodies, counts, diagnostics });
      if (chosen) {
        swap(slot.mapId, slot.modelName, chosen.model);
        assignment.set(slot.id, chosen.source);
        bodySlots.add(slot.id);
        diagnostics.count("bodiesPlaced");
        continue;
      }
      diagnostics.count("bodiesNoCandidate");
      if (mode === "normals") continue; // nothing safe to put there: the boss stays
    }
    const slotKey = keyOf(slot);
    const rejected = [];
    const list = archetypes.filter((candidate) => {
      if (keyOf(candidate) === slotKey) return false; // a boss is never replaced by itself
      if (isUnsafePair(slot, candidate)) { rejected.push({ model: candidate.modelName, reason: "pair-exclusion" }); return false; }
      return true;
    });
    let chosen = null;
    while (list.length > 0 && !chosen) {
      const index = rng.int(list.length);
      const candidate = list[index];
      if (gwynRejection > 0 && candidate.modelName === gwynModel && rng.next() < gwynRejection) {
        rejected.push({ model: candidate.modelName, reason: "gwyn-nerf" });
      } else if (options.uniqueBosses && used.has(keyOf(candidate)) && list.some((other) => !used.has(keyOf(other)))) {
        rejected.push({ model: candidate.modelName, reason: "unique-boss" });
      } else {
        chosen = candidate;
        break;
      }
      list.splice(index, 1);
    }
    if (chosen) {
      assignment.set(slot.id, chosen);
      if (bodyMode) swap(slot.mapId, slot.modelName, chosen.modelName);
      if (options.uniqueBosses) {
        used.add(keyOf(chosen));
        if (used.size >= archetypes.length) used.clear();
      }
      diagnostics.count("bossesReplaced");
    } else {
      diagnostics.count("bossesKeptVanilla");
    }
    // Boss counters stay apart from the regular-slot summary (slots, stages, rejections describe regular enemies only).
    for (const entry of rejected) diagnostics.count(`bossRejections:${entry.reason}`);
  }
  return { assignment, bodySlots };
}

/**
 * The body for one boss slot (Part E2): the candidates are the allowed normal enemies that fit the slot (Placement fit), are of a
 * difficulty the mode allows (the curve modes look for the nearest class the pool has) and keep the map inside its model limit.
 * Uniform draw with list shrink from the slot's own `body:slot:<id>` stream.
 */
function chooseBody({ seed, slot, options, bodies, counts, diagnostics }) {
  const rng = new DeterministicRng(deriveSeed(seed, `body:slot:${slot.id}`, SAMPLING_RNG_VERSION));
  const destination = bodies.destination(slot);
  const fitPool = bodies.types.filter((type) => options.fit !== "fits" || type.size <= destination.size);
  let candidates = fitPool;
  if (CURVE_MODES.has(options.difficultyMode ?? "random") && fitPool.length > 0) {
    const target = difficultyTargetClass(destination.difficulty, options.difficultyStrictness ?? "loose", rng.int(100));
    const classes = [...new Set(fitPool.map((type) => type.difficulty))].sort((a, b) => Math.abs(a - target) - Math.abs(b - target) || a - b);
    candidates = fitPool.filter((type) => type.difficulty === classes[0]);
  }
  const limit = Math.max(MODEL_BUDGET_CAP, bodies.vanillaSizes.get(slot.mapId) ?? 0);
  const list = [...candidates];
  let budgetBlocked = false;
  while (list.length > 0) {
    const index = rng.int(list.length);
    const candidate = list[index];
    const models = new Map(counts.get(slot.mapId) ?? []);
    const left = (models.get(slot.modelName) ?? 0) - 1;
    if (left <= 0) models.delete(slot.modelName); else models.set(slot.modelName, left);
    models.set(candidate.model, (models.get(candidate.model) ?? 0) + 1);
    if (models.size <= limit) return candidate;
    budgetBlocked = true;
    list.splice(index, 1);
  }
  if (budgetBlocked) diagnostics.count("bodiesBudgetRejected");
  return null;
}

/**
 * The normal enemies that may be the body of a boss encounter, typed like the regular models (size and difficulty class of the
 * class table). `sources` maps an allowed model to the catalog slot that stands for its body (its base combat variant).
 */
export function deriveBodyTypes(sources, table) {
  const types = [];
  for (const [model, source] of sources) {
    const type = table.byModel.get(model);
    if (!type) continue;
    types.push({ key: `${model}:${source.npcParamId}`, model, size: type.size, difficulty: type.difficulty, source });
  }
  return types.sort((left, right) => compare(left.key, right.key));
}

/** Size and difficulty class of a boss slot as a destination: its capsule, and its map tier and HP with the formula of the regular classes. */
export function bossDestinationClass(table, slot) {
  const footprint = slotFootprint(slot);
  return {
    size: footprint === null ? UNKNOWN_SLOT_SIZE : sizeClassOfFootprint(footprint),
    difficulty: Number.isFinite(slot.baseHp) && slot.baseHp > 0 ? difficultyOfScore(tierOfMap(slot.mapId, slot.id), hpRank(table.sortedLogHp, Math.log(slot.baseHp))) : MAX_DIFFICULTY_CLASS,
  };
}

// ---- roaming bosses (Part E1) ---------------------------------------------------------------------------------------------

/**
 * "Roaming bosses" are bosses that stand in regular enemy slots (legacy: Normal enemy replacement "bosses" / "both"). They are decided in a
 * pass AFTER the regular sampler, which is unchanged: the pass only overrides slots, so enabling roaming bosses never reshuffles the
 * normal enemies that stay. Everything about a roaming boss that is not a draw (its stats, souls, respawn, activation event) is
 * decided in generator.js; this module only chooses which slot gets which boss, deterministically, inside the model budget.
 */

/** Safety limit (DS1TR, not a legacy value): roaming bosses per map. Bounds the activation events of a map and keeps a map playable. */
export const ROAMING_BOSS_MAP_CAP = 10;

/**
 * The roaming candidates as typed like the regular models: size class from the capsule, difficulty class from where the boss lives and its
 * HP with the same formula as regular enemies (a boss's HP is above every regular enemy, so the HP rank is 1 and the map decides).
 * A candidate without a usable capsule or HP is not a roaming candidate. `sizeClassOverrides` (model -> size class) replaces the capsule class of a boss whose hit
 * capsule does not describe its body (the Moonlight Butterfly flies; its capsule is 0.4 x 0.2).
 */
export function deriveRoamingBossTypes(candidates, table, sizeClassOverrides = new Map()) {
  const types = [];
  for (const source of candidates) {
    const footprint = slotFootprint(source);
    if (footprint === null || !(Number.isFinite(source.baseHp) && source.baseHp > 0)) continue;
    types.push({
      key: `${source.modelName}:${source.npcParamId}`,
      model: source.modelName,
      size: sizeClassOverrides.get(source.modelName) ?? sizeClassOfFootprint(footprint),
      difficulty: difficultyOfScore(tierOfMap(source.mapId, source.id), hpRank(table.sortedLogHp, Math.log(source.baseHp))),
      source,
    });
  }
  return types.sort((left, right) => compare(left.key, right.key));
}

const countOf = (counts, model, delta) => {
  const next = (counts.get(model) ?? 0) + delta;
  if (next <= 0) counts.delete(model); else counts.set(model, next);
};

/**
 * Chooses the roaming bosses.
 *
 * Per map, in stable slot order (a Type replacement group when Type replacement is "enabled", a single slot otherwise - including
 * "enabled, except roaming bosses", which is exactly the legacy distinction: a boss is then never propagated to the other slots of its
 * kind):
 *   1. participation - the same first draw the regular sampler makes for that slot / group (replacement chance);
 *   2. category      - "bosses": always a boss; "both": a boss with `bossChanceInNormalSlots`, otherwise the normal enemy the sampler chose;
 *   3. map limit     - at most ROAMING_BOSS_MAP_CAP roaming bosses per map (a group counts all its slots);
 *   4. candidates    - bosses that fit (Placement fit), are not the slot's own model under Prevent same, and the difficulty class(es) of the
 *                      curve / Easy Asylum rule; drawn uniformly with list shrink;
 *   5. model budget  - the boss is admitted only if the final model set of the map (fixed models and every slot's final model) stays within
 *                      max(30, the size it had before this pass): the non-worsening rule, no Custom Pool grace.
 * A slot that gets no boss keeps what it has: the sampler's normal enemy ("both") or its own enemy ("bosses").
 *
 * @returns {Map<string, object>} slot id -> the boss catalog slot used as the source
 */
export function planRoamingBosses({
  seed, options, slots, allSlots, assignment, fixedModelsByMap, table, bossTypes, preventSame, excludedSlotIds = new Set(), diagnostics, cap = MODEL_BUDGET_CAP,
}) {
  const roaming = new Map();
  if (!["bosses", "both"].includes(options.normalReplacement) || bossTypes.length === 0) return roaming;
  const grouped = options.typeReplacement === "enabled";
  const curve = CURVE_MODES.has(options.difficultyMode);
  const easyAsylumMode = EASY_ASYLUM_MODES.has(options.difficultyMode);
  const occupant = (slot) => (roaming.get(slot.id) ?? assignment.get(slot.id) ?? slot).modelName;
  const mapIds = [...new Set(allSlots.map((slot) => slot.mapId))].sort(compare);

  for (const mapId of mapIds) {
    const mapAll = allSlots.filter((slot) => slot.mapId === mapId);
    const counts = new Map();
    for (const slot of mapAll) countOf(counts, occupant(slot), 1);
    const fixed = fixedModelsByMap.get(mapId) ?? new Set();
    const setSize = (map) => new Set([...fixed, ...map.keys()]).size;
    const limit = Math.max(cap, setSize(counts));
    let used = 0;

    const eligible = slots.filter((slot) => slot.mapId === mapId && !excludedSlotIds.has(slot.id)).sort((a, b) => compare(a.id, b.id));
    const subjects = [];
    if (grouped) {
      const blocked = new Set(slots.filter((slot) => slot.mapId === mapId && excludedSlotIds.has(slot.id)).map((slot) => slot.modelName));
      const byModel = new Map();
      for (const slot of eligible) {
        if (blocked.has(slot.modelName)) continue; // a group with a slot that cannot take a boss stays whole
        if (!byModel.has(slot.modelName)) byModel.set(slot.modelName, []);
        byModel.get(slot.modelName).push(slot);
      }
      for (const [model, members] of byModel) subjects.push({ members, name: `roaming:type:${mapId}:${model}`, first: () => createTypeStream(seed, mapId, model).int(100) + 1 });
    } else {
      for (const slot of eligible) subjects.push({ members: [slot], name: `roaming:slot:${slot.id}`, first: () => createSlotStream(seed, slot).int(100) + 1 });
    }
    subjects.sort((left, right) => compare(left.members[0].id, right.members[0].id));

    for (const subject of subjects) {
      if (subject.first() > options.replacementChance) { diagnostics.count("roamingNotParticipating"); continue; }
      const rng = new DeterministicRng(deriveSeed(seed, subject.name, SAMPLING_RNG_VERSION));
      if (options.normalReplacement === "both" && rng.int(100) + 1 > options.bossChanceInNormalSlots) { diagnostics.count("roamingNormalDraw"); continue; }
      if (used + subject.members.length > ROAMING_BOSS_MAP_CAP) { diagnostics.count("roamingMapLimitReached"); continue; }

      const maxSize = options.fit === "fits" ? Math.min(...subject.members.map((slot) => slotSizeClass(table, slot))) : UNKNOWN_SLOT_SIZE;
      const fitPool = bossTypes.filter((type) => type.size <= maxSize && !(preventSame && subject.members.some((slot) => slot.modelName === type.model)));
      const pools = [];
      if (easyAsylumMode && mapId === EASY_ASYLUM_MAP) {
        pools.push(fitPool.filter((type) => type.difficulty <= EASY_ASYLUM_MAX_CLASS));
      } else if (curve) {
        const destination = slotDifficultyClass(table, subject.members[0]);
        const target = difficultyTargetClass(destination, options.difficultyStrictness, rng.int(100));
        pools.push(fitPool.filter((type) => type.difficulty === target));
        if (target !== destination) pools.push(fitPool.filter((type) => type.difficulty === destination));
      } else {
        pools.push(fitPool);
      }

      let chosen = null;
      let budgetBlocked = false;
      for (const poolList of pools) {
        const list = [...poolList];
        while (list.length > 0 && !chosen) {
          const index = rng.int(list.length);
          const candidate = list[index];
          const trial = new Map(counts);
          for (const slot of subject.members) { countOf(trial, occupant(slot), -1); countOf(trial, candidate.model, 1); }
          if (setSize(trial) <= limit) chosen = { candidate, trial };
          else { budgetBlocked = true; list.splice(index, 1); }
        }
        if (chosen) break;
      }
      if (!chosen) { diagnostics.count(budgetBlocked ? "roamingBudgetRejected" : "roamingNoCandidate"); continue; }
      for (const slot of subject.members) roaming.set(slot.id, chosen.candidate.source);
      counts.clear();
      for (const [model, count] of chosen.trial) counts.set(model, count);
      used += subject.members.length;
      diagnostics.count("roamingBosses", subject.members.length);
      if (subject.members.length > 1) diagnostics.count("roamingGroups");
    }
    if (used > 0) diagnostics.count("roamingMaps");
  }
  return roaming;
}

/** Per-map diagnostics of the custom pool model cap (restricted pools only; nothing is added for the full pool). */
function modelInfo(model, nominalCap) {
  if (!model) return {};
  return { nominalCap, minimumFeasibleModels: model.minimumFeasible, customPoolModelGrace: model.grace };
}

/** Steps shared by both modes once participation is decided: baseline, effective cap, model set. */
function mapBudget({ mapId, fixedModelsByMap, nonParticipantModels, cap, participants, sizeOf, fitSize, preventSame, table, pool, diagnostics }) {
  const fixedModels = new Set(fixedModelsByMap.get(mapId) ?? []);
  for (const model of nonParticipantModels) fixedModels.add(model);
  // Non-worsening budget: the models the sampler cannot remove (fixed plans and the vanilla models of slots
  // it leaves alone) are the baseline. The map may hold `cap` models, or its baseline when that is larger;
  // the sampler never takes it above that.
  const baseline = fixedModels.size;
  const mapCap = effectiveModelBudget(baseline, cap);
  if (baseline > cap) diagnostics.count("mapsAboveCap");
  const { budget, kept, trimmed, cap: setCap, model } = buildMapModelSet({ mapId, cap: mapCap, fixedModels, participants, sizeOf, fitSize, preventSame, table, pool, nominalCap: cap });
  if (trimmed > 0) diagnostics.count("reserveTrimmed", trimmed);
  if (model?.grace) diagnostics.count("customPoolModelGraceMaps");
  return { budget, kept, baseline, mapCap: setCap, modelInfo: modelInfo(model, cap) };
}

/** Post-condition: the models the map really holds after the run (what DsrDataTool keeps) fit the cap. */
function finalizeMap({ mapId, fixedModelsByMap, mapSlots, modelOfSlot, mapCap, assignment, budget, baseline, attempt, extra = {} }) {
  const finalModels = new Set(fixedModelsByMap.get(mapId) ?? []);
  for (const slot of mapSlots) finalModels.add(modelOfSlot.get(slot.id));
  if (finalModels.size > mapCap) throw new ModelBudgetError(mapId, mapCap, [...finalModels].sort(compare));
  const replaced = mapSlots.filter((slot) => assignment.has(slot.id)).length;
  return {
    assignment,
    attempt,
    info: { slots: mapSlots.length, replaced, finalModels: finalModels.size, modelSet: budget.size, baseline, effectiveCap: mapCap, ...extra },
  };
}

/** One attempt at one map, slot by slot (Type replacement disabled). Throws EnemyPlacementInfeasibleError (with baseline/cap). */
function planMapAttempt({ seed, settings, preventSame, table, pool, mapId, mapSlots, attempt, fixedModelsByMap, cap, diagnostics }) {
  const sizeOf = (slot) => slotSizeClass(table, slot);
  const fitSize = (slot) => (settings.fit === "fits" ? sizeOf(slot) : UNKNOWN_SLOT_SIZE);
  const streams = new Map(mapSlots.map((slot) => [slot.id, createSlotStream(seed, slot.id, attempt)]));
  const participates = new Map();
  for (const slot of mapSlots) {
    if (settings.normalReplacement === "none") {
      participates.set(slot.id, false);
      diagnostics.count("keptByMode");
    } else {
      // The replacement chance is always the slot's first draw: 1..100 <= chance, so 100 always passes.
      const draw = streams.get(slot.id).int(100) + 1;
      const passes = draw <= settings.replacementChance;
      participates.set(slot.id, passes);
      if (!passes) diagnostics.count("keptByChance");
    }
  }
  if (pool.excluded > 0) {
    for (const slot of mapSlots) {
      if (participates.get(slot.id) && cannotBeReplaced({ modelName: slot.modelName, maxSize: fitSize(slot), pool, preventSame })) {
        participates.set(slot.id, false);
        diagnostics.count("keptByPool");
      }
    }
  }
  const { budget, kept, baseline, mapCap, modelInfo: capInfo } = mapBudget({
    mapId, fixedModelsByMap, nonParticipantModels: mapSlots.filter((slot) => !participates.get(slot.id)).map((slot) => slot.modelName),
    cap, participants: mapSlots.filter((slot) => participates.get(slot.id)), sizeOf, fitSize, preventSame, table, pool, diagnostics,
  });
  const active = new Set(kept.map((slot) => slot.id));
  const assignment = new Map();
  const modelOfSlot = new Map();
  const noExclusions = new Set();

  for (const slot of mapSlots) {
    if (!active.has(slot.id)) {
      modelOfSlot.set(slot.id, slot.modelName);
      diagnostics.record({ slotId: slot.id, slotClass: "normal", participates: false, attempt });
      continue;
    }
    const rng = streams.get(slot.id);
    const maxSize = fitSize(slot);
    const sampled = sampleCandidate({
      rng, subject: slot, mapId, maxSize, destinationOf: () => slotDifficultyClass(table, slot), settings, pool, budget, preventSame,
      excluded: noExclusions, diagnostics,
    });
    const rejected = sampled.rejected;
    const info = { attempt, ...sampled.info };
    let chosen = sampled.chosen;
    let stage = "sampled";

    if (!chosen) {
      const fallback = fallbackLadder({ slot, budget, table, pool, maxSize, preventSame });
      stage = fallback.stage;
      chosen = fallback.chosen;
      if (stage === "infeasible") {
        diagnostics.record({ slotId: slot.id, slotClass: "normal", participates: true, category: "normal", stage, rejected, ...info });
        throw new EnemyPlacementInfeasibleError(mapId, slot.id,
          `model ${slot.modelName}, in-set models ${budget.size}/${budget.cap}, fit ${settings.fit}, prevent same ${preventSame}`,
          { baseline, effectiveCap: mapCap, detail: `slot ${slot.id}, attempt ${attempt}` });
      }
      if (stage === "kept-vanilla") budget.admit(slot.modelName);
      else if (chosen && (sampled.curve || sampled.easyAsylum)) diagnostics.count("difficultyLapses");
    }

    if (chosen) {
      const variant = chosen.variants[rng.int(chosen.variants.length)];
      budget.admit(chosen.model);
      assignment.set(slot.id, variant.source);
      modelOfSlot.set(slot.id, chosen.model);
      diagnostics.record({
        slotId: slot.id, slotClass: "normal", participates: true, category: "normal", selected: chosen.model, stage, rejected,
        candidateClass: chosen.difficulty, ...info,
      });
    } else {
      modelOfSlot.set(slot.id, slot.modelName);
      diagnostics.record({ slotId: slot.id, slotClass: "normal", participates: true, category: "normal", selected: slot.modelName, stage, rejected, ...info });
    }
  }
  return finalizeMap({ mapId, fixedModelsByMap, mapSlots, modelOfSlot, mapCap, assignment, budget, baseline, attempt, extra: capInfo });
}

/**
 * One attempt at one map with Type replacement (legacy semantics, model document 10.4). The group is the original
 * model within the map (`map:<map>:model:<model>`); it is decided once, from the group's own stream: the replacement
 * chance, the difficulty class and the candidate draw all belong to the group, every member slot then receives the
 * same replacement model (each slot draws only its variant from its own stream). A candidate must satisfy every member
 * (the smallest member fit), must not be a model another group of the map already received (injectivity) and obeys
 * Prevent same, difficulty and the model budget (a reference-counted set: see ModelLedger). A group that gets no
 * candidate stays vanilla as a whole - the legacy outcome - or, when its model cannot stay, takes the in-set fallback
 * and then, as the last rung, a model another group already has (sharedFallback); a group with none fails the attempt. There is no coverage reserve: every group keeps its original model as the
 * fallback until it is decided.
 */
function planMapByType({ seed, settings, preventSame, table, pool, mapId, mapSlots, attempt, fixedModelsByMap, cap, diagnostics }) {
  const sizeOf = (slot) => slotSizeClass(table, slot);
  const fitSize = (slot) => (settings.fit === "fits" ? sizeOf(slot) : UNKNOWN_SLOT_SIZE);
  const byModel = new Map();
  for (const slot of mapSlots) {
    if (!byModel.has(slot.modelName)) byModel.set(slot.modelName, []);
    byModel.get(slot.modelName).push(slot);
  }
  const groups = [...byModel.keys()].sort(compare).map((model) => ({
    id: `map:${mapId}:model:${model}`, mapId, modelName: model, members: byModel.get(model).sort((a, b) => compare(a.id, b.id)),
  }));
  const groupSize = (group) => Math.min(...group.members.map(sizeOf));
  const groupFit = (group) => Math.min(...group.members.map(fitSize));
  const typeStreams = new Map(groups.map((group) => [group.id, createTypeStream(seed, mapId, group.modelName, attempt)]));
  const slotStreams = new Map(mapSlots.map((slot) => [slot.id, createSlotStream(seed, slot.id, attempt)]));
  diagnostics.count("typeGroups", groups.length);

  const participates = new Map();
  for (const group of groups) {
    if (settings.normalReplacement === "none") {
      participates.set(group.id, false);
      diagnostics.count("keptByMode", group.members.length);
    } else {
      // Legacy: the roll that counts is the first slot's of the type; here it is the group's first draw.
      const passes = typeStreams.get(group.id).int(100) + 1 <= settings.replacementChance;
      participates.set(group.id, passes);
      if (!passes) {
        diagnostics.count("keptByChance", group.members.length);
        diagnostics.count("typeGroupsKeptByChance");
      }
    }
  }
  if (pool.excluded > 0) {
    for (const group of groups) {
      if (participates.get(group.id) && cannotBeReplaced({ modelName: group.modelName, maxSize: groupFit(group), pool, preventSame })) {
        participates.set(group.id, false);
        diagnostics.count("keptByPool", group.members.length);
        diagnostics.count("typeGroupsKeptByPool");
      }
    }
  }
  // Reference-counted model set. The models the sampler cannot remove (fixed plans, groups left alone) are pinned; every
  // participating group still owns its original model until it is decided, so keeping it is always possible while the
  // map is within its cap, and a replaced group frees its original model (unless something else still owns it).
  const pinned = new Set(fixedModelsByMap.get(mapId) ?? []);
  for (const group of groups) if (!participates.get(group.id)) pinned.add(group.modelName);
  const baseline = pinned.size;
  let mapCap = effectiveModelBudget(baseline, cap);
  if (baseline > cap) diagnostics.count("mapsAboveCap");
  const active = new Set(groups.filter((group) => participates.get(group.id)).map((group) => group.id));
  // A restricted custom pool gets a coverage witness (see coverWitness): models held as insurance so that every group can
  // always keep its model or take a pool model that fits it, however the draws before it went. The one-model grace of
  // resolveModelCap lifts the cap to nominal + 1 only when that is proven to be the least the map can hold.
  let witness = [];
  let capInfo = {};
  if (pool.excluded > 0) {
    const cover = coverWitnessSearch({
      base: pinned, subjects: groups.filter((group) => active.has(group.id)).map((group) => ({ own: group.modelName, maxSize: groupFit(group) })), pool, preventSame,
    });
    const model = resolveModelCap({ nominalCap: cap, baseSize: pinned.size, coverSize: cover.witness.length, exact: cover.exact });
    mapCap = model.effectiveCap;
    witness = cover.witness.slice(0, Math.max(0, mapCap - pinned.size));
    capInfo = modelInfo(model, cap);
    if (model.grace) diagnostics.count("customPoolModelGraceMaps");
  }
  const budget = new ModelLedger({ mapId, cap: mapCap });
  for (const model of pinned) budget.hold(model);
  for (const model of witness) budget.reserve(model);
  for (const group of groups) if (active.has(group.id)) budget.hold(group.modelName, { pending: true });
  if (budget.size > mapCap) diagnostics.count("typeMapsStartedOverCap");
  const assignment = new Map();
  const modelOfSlot = new Map();
  const used = new Set();

  for (const group of groups) {
    const subject = { id: group.id, modelName: group.modelName, mapId };
    if (!active.has(group.id)) {
      for (const member of group.members) {
        modelOfSlot.set(member.id, member.modelName);
        diagnostics.record({ slotId: member.id, slotClass: "normal", participates: false, attempt, groupId: group.id });
      }
      diagnostics.recordGroup({ groupId: group.id, model: group.modelName, members: group.members.length, participates: false, attempt });
      continue;
    }
    const rng = typeStreams.get(group.id);
    const maxSize = groupFit(group);
    budget.release(group.modelName, { pending: true }); // tentatively free the original model; a group that stays vanilla takes it back
    const sampled = sampleCandidate({
      rng, subject, mapId, maxSize, destinationOf: () => slotDifficultyClass(table, group.members[0]), settings, pool, budget, preventSame,
      excluded: used, diagnostics,
    });
    const rejected = sampled.rejected;
    const info = { attempt, groupId: group.id, ...sampled.info };
    let chosen = sampled.chosen;
    let stage = "sampled";
    for (const entry of rejected) {
      if (entry.reason === "prevent-same") diagnostics.count("typePreventSameRejections");
      else if (entry.reason === "type-injectivity") diagnostics.count("typeInjectivityRejections");
      else if (entry.reason === "difficulty-empty") diagnostics.count("typeDifficultyRejections");
    }
    if (sampled.sizeRejections > 0) diagnostics.count("typeFitRejections", sampled.sizeRejections);

    if (!chosen) {
      const fallback = fallbackLadder({ slot: subject, budget, table, pool, maxSize, preventSame, excluded: used });
      stage = fallback.stage;
      chosen = fallback.chosen;
      if (stage === "infeasible") {
        const shared = sharedFallback({ group, budget, pool, table, maxSize, preventSame, destination: slotDifficultyClass(table, group.members[0]), rng });
        if (shared) {
          chosen = shared;
          stage = "shared-fit";
          diagnostics.count("typeGroupsShared");
        }
      }
      if (stage === "infeasible") {
        budget.hold(group.modelName);
        diagnostics.record({ slotId: group.members[0].id, slotClass: "normal", participates: true, category: "normal", stage, rejected, ...info });
        throw new EnemyPlacementInfeasibleError(mapId, group.members[0].id,
          `type group ${group.id} (${group.members.length} slots), in-set models ${budget.size}/${budget.cap}, fit ${settings.fit}, prevent same ${preventSame}`,
          { baseline, effectiveCap: mapCap, detail: `group ${group.id}, attempt ${attempt}` });
      }
      diagnostics.count("typeGroupsFallback");
      if (stage === "kept-vanilla") {
        budget.hold(group.modelName);
        diagnostics.count("typeGroupsKeptVanilla");
      } else if (chosen && (sampled.curve || sampled.easyAsylum)) {
        diagnostics.count("difficultyLapses");
      }
    }

    if (chosen) {
      budget.hold(chosen.model);
      used.add(chosen.model);
      diagnostics.count("typeGroupsReplaced");
    }
    group.members.forEach((member, index) => {
      if (chosen) {
        const variant = chosen.variants[slotStreams.get(member.id).int(chosen.variants.length)];
        assignment.set(member.id, variant.source);
        modelOfSlot.set(member.id, chosen.model);
      } else {
        modelOfSlot.set(member.id, member.modelName);
      }
      diagnostics.record({
        slotId: member.id, slotClass: "normal", participates: true, category: "normal", selected: chosen ? chosen.model : member.modelName,
        stage, rejected: index === 0 ? rejected : [], ...(chosen ? { candidateClass: chosen.difficulty } : {}), ...info,
      });
    });
    diagnostics.recordGroup({
      groupId: group.id, model: group.modelName, members: group.members.length, participates: true, selected: chosen ? chosen.model : group.modelName,
      stage, maxSize, ...sampled.info, rejected: rejected.length, attempt,
    });
  }
  budget.dropReserve();
  if (budget.size > mapCap) {
    throw new EnemyPlacementInfeasibleError(mapId, groups[0].members[0].id,
      `${budget.size} models remain, above the effective cap ${mapCap}`, { baseline, effectiveCap: mapCap, detail: `map ${mapId}, attempt ${attempt}` });
  }
  return finalizeMap({ mapId, fixedModelsByMap, mapSlots, modelOfSlot, mapCap, assignment, budget, baseline, attempt, extra: { typeGroups: groups.length, ...capInfo } });
}
