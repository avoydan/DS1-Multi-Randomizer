// DS1 Triple Randomizer - repository-authored module (GPL-3.0-only); not part of upstream ds-randomizer.
//
// Infrastructure for the future `sampling-v1` enemy placement algorithm (docs/ENEMY_PLACEMENT_MODEL.md).
// Part 0 adds the vocabulary only: algorithm identity and dispatch errors, stable slot identity and RNG substreams,
// the per-map model budget, diagnostics and drop-ownership helpers. Nothing in this file runs for `permutation-v1`
// (the production algorithm): it consumes no stream from here, it does not use the budget, and it records only a
// one-line identity in the result. The sampler itself is not implemented.

import { DeterministicRng, deriveSeed } from "./rng.js";

// ---- algorithm identity ---------------------------------------------------------------------------------------

export const ENEMY_PLACEMENT_ALGORITHMS = Object.freeze(["permutation-v1", "sampling-v1"]);

/** Algorithms that can run. "sampling-v1" was enabled by Part A (regular enemy slots only). */
export const IMPLEMENTED_ENEMY_PLACEMENT_ALGORITHMS = Object.freeze(["permutation-v1", "sampling-v1"]);

export const DEFAULT_ENEMY_PLACEMENT_ALGORITHM = "permutation-v1";

/**
 * RNG version of every `sampling-v1` stream. Independent of RANDOMIZER_VERSION (which salts the frozen
 * permutation streams and must not change) and of every product version.
 */
export const SAMPLING_RNG_VERSION = "enemy-placement-v1";

/** Typed refusal: the algorithm is unknown or not implemented. Never retryable; there is no silent fallback. */
export class EnemyPlacementUnavailableError extends Error {
  constructor(algorithm, known) {
    super(
      `ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE: enemy placement algorithm "${algorithm}" is ` +
        (known ? "not implemented yet" : "unknown") +
        `. Use "${DEFAULT_ENEMY_PLACEMENT_ALGORITHM}".`,
    );
    this.name = "EnemyPlacementUnavailableError";
    this.code = "ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE";
    this.algorithm = algorithm;
  }
}

/** Returns the algorithm to run, or throws {@link EnemyPlacementUnavailableError}. */
export function resolveEnemyPlacementAlgorithm(config) {
  const algorithm = config.enemyPlacementAlgorithm;
  const known = ENEMY_PLACEMENT_ALGORITHMS.includes(algorithm);
  if (!known || !IMPLEMENTED_ENEMY_PLACEMENT_ALGORITHMS.includes(algorithm)) {
    throw new EnemyPlacementUnavailableError(algorithm, known);
  }
  resolveEnemyPlacementOptions(config, algorithm);
  return algorithm;
}

// ---- sampling-only options (Part A: regular enemy slots) -----------------------------------------------------------

/** Legacy "Normal enemy replacement" values the regular-slot sampler can honor (the others need boss slots). */
export const NORMAL_REPLACEMENT_MODES = Object.freeze(["none", "normals"]);

/** Legacy "Placement" values: 0 only where they fit, 1 anywhere, 2 anywhere except when replacing NPCs. */
export const PLACEMENT_FIT_MODES = Object.freeze(["fits", "anywhere", "anywhere-except-npcs"]);

/**
 * Legacy "Difficulty mode": 1 curve, 2 fully random, 3 fully random + easy Asylum, 4 curve + easy Asylum.
 * Legacy default 3. Every value is supported for regular enemies.
 */
export const DIFFICULTY_MODES = Object.freeze(["curve", "random", "random-easy-asylum", "curve-easy-asylum"]);

/** Legacy "Difficulty strictness": 0 strict, 1 a bit loose (default), 2 very loose. Only effective in the curve modes. */
export const DIFFICULTY_STRICTNESS = Object.freeze(["strict", "loose", "very-loose"]);

/** Values under which permutation-v1 behaves exactly as it always did; anything else is refused there. */
export const SAMPLING_NEUTRAL_OPTIONS = Object.freeze({
  enemyReplacementChance: 100,
  enemyNormalReplacement: "normals",
  enemyPlacementFit: "anywhere",
  enemyDifficultyMode: "random",
  enemyDifficultyStrictness: "loose",
});

/** Typed refusal: a sampling-only option has an invalid value, or a non-neutral one under permutation-v1. */
export class EnemyPlacementOptionsError extends Error {
  constructor(problems) {
    super(`ENEMY_PLACEMENT_OPTIONS_INVALID: ${problems.join(" ")}`);
    this.name = "EnemyPlacementOptionsError";
    this.code = "ENEMY_PLACEMENT_OPTIONS_INVALID";
    this.problems = problems;
  }
}

/** Typed failure: no placement exists for a slot after the whole fallback ladder. Retryable with another seed. */
export class EnemyPlacementInfeasibleError extends Error {
  constructor(mapId, slotId, detail, context = {}) {
    super(`ENEMY_PLACEMENT_INFEASIBLE: no placement for ${slotId} in ${mapId} (${detail}).`);
    this.name = "EnemyPlacementInfeasibleError";
    this.code = "ENEMY_PLACEMENT_INFEASIBLE";
    this.mapId = mapId;
    this.slotId = slotId;
    // Set when every map-local attempt failed: attempts, baseline, effectiveCap, difficultyMode, fit, ...
    Object.assign(this, context);
  }
}

/** Validates the sampling-only options of a normalized config; never replaces a value silently. */
export function resolveEnemyPlacementOptions(config, algorithm = config.enemyPlacementAlgorithm) {
  const problems = [];
  const chance = config.enemyReplacementChance;
  if (!Number.isInteger(chance) || chance < 0 || chance > 100) {
    problems.push(`enemyReplacementChance must be an integer from 0 to 100 (got ${String(chance)}).`);
  }
  if (!NORMAL_REPLACEMENT_MODES.includes(config.enemyNormalReplacement)) {
    problems.push(`enemyNormalReplacement must be ${NORMAL_REPLACEMENT_MODES.join(" or ")} (got ${String(config.enemyNormalReplacement)}).`);
  }
  if (!PLACEMENT_FIT_MODES.includes(config.enemyPlacementFit)) {
    problems.push(`enemyPlacementFit must be ${PLACEMENT_FIT_MODES.join(", ")} (got ${String(config.enemyPlacementFit)}).`);
  }
  if (!DIFFICULTY_MODES.includes(config.enemyDifficultyMode)) {
    problems.push(`enemyDifficultyMode must be ${DIFFICULTY_MODES.join(", ")} (got ${String(config.enemyDifficultyMode)}).`);
  }
  if (!DIFFICULTY_STRICTNESS.includes(config.enemyDifficultyStrictness)) {
    problems.push(`enemyDifficultyStrictness must be ${DIFFICULTY_STRICTNESS.join(", ")} (got ${String(config.enemyDifficultyStrictness)}).`);
  }
  if (problems.length === 0 && algorithm === "permutation-v1") {
    for (const [key, neutral] of Object.entries(SAMPLING_NEUTRAL_OPTIONS)) {
      if (config[key] !== neutral) problems.push(`${key} only applies to sampling-v1; it must be ${JSON.stringify(neutral)} with permutation-v1.`);
    }
  }
  if (problems.length > 0) throw new EnemyPlacementOptionsError(problems);
  return {
    replacementChance: chance,
    normalReplacement: config.enemyNormalReplacement,
    fit: config.enemyPlacementFit,
    difficultyMode: config.enemyDifficultyMode,
    difficultyStrictness: config.enemyDifficultyStrictness,
  };
}

// ---- stable slot identity and RNG substreams ----------------------------------------------------------------------

/**
 * A catalog slot id: map id, a colon and the MSB part name. Stable across catalog order, array position and
 * option changes, so it is the only identity a per-slot stream may be derived from.
 */
const STABLE_SLOT_ID = /^m\d{2}_\d{2}_\d{2}_\d{2}:[A-Za-z0-9_]+$/u;

export function stableSlotId(slotOrId) {
  const id = typeof slotOrId === "string" ? slotOrId : slotOrId?.id;
  if (typeof id !== "string" || !STABLE_SLOT_ID.test(id)) {
    throw new TypeError(`Not a stable slot id: ${String(id)}`);
  }
  return id;
}

/**
 * Stream name of a slot. Attempt 0 is the original `slot:<id>` (so it never changes an earlier output); a map-local
 * retry `n` uses `attempt:<n>:slot:<id>`. The slot id already carries the stable map id, so the namespace is
 * enemy-placement-v1 / map / attempt / slot without any array index.
 */
export const slotStreamName = (slotOrId, attempt = 0) => {
  if (!Number.isInteger(attempt) || attempt < 0) throw new RangeError("attempt must be a non-negative integer.");
  return attempt === 0 ? `slot:${stableSlotId(slotOrId)}` : `attempt:${attempt}:slot:${stableSlotId(slotOrId)}`;
};

let slotStreamDerivations = 0;

/** 32-bit seed of the stream that owns one slot: master/combined seed + "enemy-placement-v1" + "slot:<id>". */
export function deriveSlotStreamSeed(seed, slotOrId, attempt = 0) {
  slotStreamDerivations += 1;
  return deriveSeed(seed, slotStreamName(slotOrId, attempt), SAMPLING_RNG_VERSION);
}

export function createSlotStream(seed, slotOrId, attempt = 0) {
  return new DeterministicRng(deriveSlotStreamSeed(seed, slotOrId, attempt));
}

/** Diagnostic counter used by tests to prove that `permutation-v1` never derives a sampling stream. */
export const slotStreamDerivationCount = () => slotStreamDerivations;

// ---- classification vocabulary ------------------------------------------------------------------------------------

/** What a slot is in the game. */
export const SLOT_CLASSES = Object.freeze(["normal", "boss", "npc"]);

/** What the planner decided to put there (a normal slot can ask for a boss, and the reverse). */
export const REPLACEMENT_CATEGORIES = Object.freeze(["normal", "boss"]);

/**
 * @typedef {object} SlotRecord
 * @property {string} id            stable slot id
 * @property {string} mapId
 * @property {string} model         vanilla model name
 * @property {"normal"|"boss"|"npc"} slotClass
 * @property {number} size          derived size class, 0-5 (Part A)
 * @property {number} difficulty    derived difficulty class, 0-7 (Part B)
 *
 * @typedef {object} SamplingCandidate
 * @property {string} model
 * @property {"normal"|"boss"} category
 * @property {number} npcParamId
 * @property {number} thinkParamId
 */

// ---- model budget ---------------------------------------------------------------------------------------------------

/** Final distinct enemy models per map. Safety first: the real map data stays below it (maximum 28, Undead Parish). */
export const MODEL_BUDGET_CAP = 30;

export class ModelBudgetError extends Error {
  constructor(mapId, cap, models) {
    super(
      `ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE: ${mapId} already holds ${models.length} fixed models (cap ${cap}).`,
    );
    this.name = "ModelBudgetError";
    this.code = "ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE";
    this.mapId = mapId;
  }
}

/**
 * The set of enemy models a map will contain after the run. Models that stay in the map (special plans, slots that
 * keep their vanilla enemy) are admitted first, in sorted order and unconditionally; a map that is already over the
 * cap through them fails with {@link ModelBudgetError} (the permutation engine fails in the same situation).
 * Candidates are then admitted one at a time in the caller's deterministic order. Fixed models are never evicted.
 * There is no per-map exception: Blighttown has the same cap.
 */
export class ModelBudget {
  #cap;
  #mapId;
  #order = [];
  #members = new Set();

  constructor({ mapId, cap = MODEL_BUDGET_CAP, fixedModels = [] }) {
    if (!Number.isInteger(cap) || cap < 1) throw new RangeError("cap must be a positive integer.");
    this.#mapId = mapId;
    this.#cap = cap;
    const fixed = [...new Set(fixedModels)].sort();
    if (fixed.length > cap) throw new ModelBudgetError(mapId, cap, fixed);
    for (const model of fixed) this.#add(model);
  }

  #add(model) {
    this.#members.add(model);
    this.#order.push(model);
  }

  get cap() { return this.#cap; }
  get size() { return this.#members.size; }
  has(model) { return this.#members.has(model); }
  /** Admission order: fixed models (sorted) first, then admitted candidates. */
  admissionOrder() { return [...this.#order]; }
  models() { return [...this.#members].sort(); }

  /** Pure check. `reason: "model-budget"` is the diagnostic reason for a candidate the budget rejects. */
  evaluate(model) {
    if (this.#members.has(model)) return { admitted: true, alreadyPresent: true };
    if (this.#members.size < this.#cap) return { admitted: true, alreadyPresent: false };
    return { admitted: false, reason: "model-budget", size: this.#members.size, cap: this.#cap, mapId: this.#mapId };
  }

  /** Admits a model that {@link evaluate} accepted; refuses anything else instead of exceeding the cap. */
  admit(model) {
    const verdict = this.evaluate(model);
    if (!verdict.admitted) throw new ModelBudgetError(this.#mapId, this.#cap, this.models());
    if (!verdict.alreadyPresent) this.#add(model);
    return verdict;
  }
}

/**
 * Checked precondition of the coverage reserve (model document, 6.6): fixed models, the vanilla models of slots
 * that do not participate, and the reserve models together must fit the cap.
 */
export function checkCoverageReserve({ cap = MODEL_BUDGET_CAP, fixedModels = [], nonParticipantModels = [], reserveModels = [] }) {
  const union = new Set([...fixedModels, ...nonParticipantModels, ...reserveModels]);
  return { ok: union.size <= cap, size: union.size, cap, over: Math.max(0, union.size - cap) };
}

// ---- diagnostics --------------------------------------------------------------------------------------------------

/** Where a slot ended up on the fallback ladder (model document, 6.7). */
export const FALLBACK_STAGES = Object.freeze([
  "sampled", "category-fallback", "kept-vanilla", "in-set-fit", "fit-relaxed", "infeasible",
]);

/** Why a candidate was rejected. */
export const REJECTION_REASONS = Object.freeze([
  "model-budget", "size", "difficulty-empty", "prevent-same", "pair-exclusion", "gwyn-nerf",
  "type-injectivity", "unique-boss", "custom-pool",
]);

const hasPathLikeText = (value) => typeof value === "string" && (/^[A-Za-z]:[\\/]/u.test(value) || value.includes("\\") || value.startsWith("/"));

/**
 * Structured diagnostics for the sampler. The summary is always kept (counters only); per-slot detail is kept
 * only at level "detail", so normal runs do not produce large records. Slot ids and model names are the only
 * strings stored, and path-like text is refused so no absolute path can leak into a package.
 */
export function createPlacementDiagnostics({ algorithm, level = "summary" }) {
  if (!ENEMY_PLACEMENT_ALGORITHMS.includes(algorithm)) throw new TypeError(`Unknown algorithm ${algorithm}.`);
  if (level !== "summary" && level !== "detail") throw new TypeError(`Unknown diagnostics level ${level}.`);
  const summary = {
    slots: 0, participating: 0, notParticipating: 0,
    stages: Object.fromEntries(FALLBACK_STAGES.map((stage) => [stage, 0])),
    rejections: Object.fromEntries(REJECTION_REASONS.map((reason) => [reason, 0])),
    categoryDowngrades: 0,
    terminalFailures: [],
    counters: {},
    maps: {},
    retries: [],
  };
  const slots = [];
  return {
    algorithm,
    rngVersion: algorithm === "sampling-v1" ? SAMPLING_RNG_VERSION : null,
    level,
    classTableHash: null,
    /** The effective sampling options of the run (mode, strictness, fit, chance, ...). */
    options: null,
    /** Highest value seen for a named quantity (for example the highest map attempt used). */
    peak(name, value) {
      summary.counters[name] = Math.max(summary.counters[name] ?? 0, value);
    },
    /** A failed map attempt that was retried. */
    addRetry(entry) {
      summary.retries.push({ ...entry });
    },
    /**
     * Adds the summary (and detail) of another diagnostics object, for a successful map attempt planned with a
     * scratch object. The scratch object of a failed attempt is simply dropped.
     */
    absorb(other) {
      const json = other.toJSON();
      const from = json.summary;
      summary.slots += from.slots;
      summary.participating += from.participating;
      summary.notParticipating += from.notParticipating;
      for (const key of Object.keys(from.stages)) summary.stages[key] += from.stages[key];
      for (const key of Object.keys(from.rejections)) summary.rejections[key] += from.rejections[key];
      summary.categoryDowngrades += from.categoryDowngrades;
      summary.terminalFailures.push(...from.terminalFailures);
      for (const [key, value] of Object.entries(from.counters)) summary.counters[key] = (summary.counters[key] ?? 0) + value;
      Object.assign(summary.maps, from.maps);
      if (level === "detail") slots.push(...(json.slots ?? []));
    },
    /** Named counters of the planner (for example how many slots the replacement chance left alone). */
    count(name, amount = 1) {
      summary.counters[name] = (summary.counters[name] ?? 0) + amount;
    },
    /** Per-map totals (model count, replaced slots, ...); keys are map ids. */
    setMap(mapId, values) {
      summary.maps[mapId] = { ...values };
    },
    /**
     * @param {object} record
     * @param {string} record.slotId
     * @param {string} record.slotClass
     * @param {boolean} record.participates
     * @param {string} [record.category]
     * @param {string} [record.selected]            selected model
     * @param {string} [record.stage]               one of FALLBACK_STAGES
     * @param {boolean} [record.categoryDowngraded]
     * @param {Array<{model: string, reason: string}>} [record.rejected]
     */
    record(record) {
      const id = stableSlotId(record.slotId);
      if (!SLOT_CLASSES.includes(record.slotClass)) throw new TypeError(`Unknown slot class ${record.slotClass}.`);
      if (record.stage !== undefined && !FALLBACK_STAGES.includes(record.stage)) throw new TypeError(`Unknown stage ${record.stage}.`);
      if (record.category !== undefined && !REPLACEMENT_CATEGORIES.includes(record.category)) throw new TypeError(`Unknown category ${record.category}.`);
      for (const text of [record.selected, ...(record.rejected ?? []).map((entry) => entry.model)]) {
        if (hasPathLikeText(text)) throw new TypeError("Diagnostics must not contain paths.");
      }
      for (const entry of record.rejected ?? []) {
        if (!REJECTION_REASONS.includes(entry.reason)) throw new TypeError(`Unknown rejection reason ${entry.reason}.`);
        summary.rejections[entry.reason] += 1;
      }
      summary.slots += 1;
      if (record.participates) summary.participating += 1; else summary.notParticipating += 1;
      if (record.stage !== undefined) summary.stages[record.stage] += 1;
      if (record.categoryDowngraded) summary.categoryDowngrades += 1;
      if (record.stage === "infeasible") summary.terminalFailures.push(id);
      if (level === "detail") slots.push({ ...record, slotId: id });
    },
    toJSON() {
      return {
        algorithm: this.algorithm, rngVersion: this.rngVersion, level, classTableHash: this.classTableHash, options: this.options, summary,
        ...(level === "detail" ? { slots } : {}),
      };
    },
  };
}

/** The one-line identity recorded for the frozen algorithm: no counters, no streams. */
export const permutationDiagnostics = () => ({ algorithm: "permutation-v1", rngVersion: null, level: "summary", classTableHash: null, options: null, summary: null });

// ---- drop ownership -----------------------------------------------------------------------------------------------

/**
 * Who owns an enemy's NpcParam drops (model document, 6.9). With a scaled NpcParam row the destination slot's item
 * lots and team are copied onto the placed enemy: the drops follow the slot. Without one the placed enemy keeps its
 * own row and its own drops, which is what `permutation-v1` does under Enemy scaling "vanilla" (a documented,
 * unchanged behavior that duplicates single-owner drops). Every `sampling-v1` placement must be slot-owned.
 */
export const DROP_OWNERSHIP = Object.freeze({ SLOT: "slot", REPLACEMENT: "replacement" });

export function describeDropOwnership(placement) {
  return placement.scaledNpcParamId === null || placement.scaledNpcParamId === undefined
    ? { owner: DROP_OWNERSHIP.REPLACEMENT, scaledNpcParamId: null }
    : { owner: DROP_OWNERSHIP.SLOT, scaledNpcParamId: placement.scaledNpcParamId };
}

/** The placement `extra` that makes a placement slot-owned whatever the global scaling mode is. */
export function slotOwnedDrops(scaledNpcParamId) {
  if (!Number.isInteger(scaledNpcParamId) || scaledNpcParamId <= 0) throw new RangeError("A scaled NpcParam id is required.");
  return { scaledNpcParamId };
}

/** Throws unless every changed placement of a sampling plan is slot-owned. */
export function assertSlotOwnedDrops(placements) {
  const offending = placements.filter((p) => p.changed && describeDropOwnership(p).owner !== DROP_OWNERSHIP.SLOT).map((p) => p.slot);
  if (offending.length > 0) {
    throw new Error(`ENEMY_PLACEMENT_DROP_OWNERSHIP: ${offending.length} changed placement(s) keep the replacement's own drops (first: ${offending[0]}).`);
  }
}
