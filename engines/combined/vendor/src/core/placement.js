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

/**
 * Legacy "Normal enemy replacement": none, only normal enemies, only bosses ("roaming bosses": a boss in a regular enemy slot) or
 * either (a boss with the chance of "Boss chance: normal slots", otherwise a normal enemy). Bosses need Part E1 (roaming planner).
 */
export const NORMAL_REPLACEMENT_MODES = Object.freeze(["none", "normals", "bosses", "both"]);

/** The modes in which bosses can land in regular enemy slots. */
export const ROAMING_BOSS_MODES = Object.freeze(["bosses", "both"]);

/** Legacy "Placement" values: 0 only where they fit, 1 anywhere, 2 anywhere except when replacing NPCs. */
export const PLACEMENT_FIT_MODES = Object.freeze(["fits", "anywhere", "anywhere-except-npcs"]);

/**
 * Legacy "Difficulty mode": 1 curve, 2 fully random, 3 fully random + easy Asylum, 4 curve + easy Asylum.
 * Legacy default 3. Every value is supported for regular enemies.
 */
export const DIFFICULTY_MODES = Object.freeze(["curve", "random", "random-easy-asylum", "curve-easy-asylum"]);

/** Legacy "Difficulty strictness": 0 strict, 1 a bit loose (default), 2 very loose. Only effective in the curve modes. */
export const DIFFICULTY_STRICTNESS = Object.freeze(["strict", "loose", "very-loose"]);

/**
 * Legacy "Type replacement": 0 enabled, 1 disabled (default), 2 enabled except roaming bosses. The exception only
 * concerns boss sources, which regular-enemy sampling never draws, so for regular enemies 2 behaves as 0.
 */
export const TYPE_REPLACEMENT_MODES = Object.freeze(["disabled", "enabled", "enabled-except-roaming-bosses"]);

/**
 * Boss replacement (Part D): "classic" keeps the archetype permutation of the existing boss planner; "bosses" samples every
 * single-body boss slot from the portable boss pool with replacement. Linked units (Priscilla, Kalameet, Gaping Dragon, Seath, the Sanctuary Guardian, the Bell Gargoyles, the Centipede Demon)
 * stay classic in both modes.
 */
export const BOSS_REPLACEMENT_MODES = Object.freeze(["classic", "bosses", "normals", "both"]);

/**
 * Part E2 (regular enemy in a boss slot). "normals": a participating boss slot of the supported set gets a normal enemy, every other
 * slot keeps its boss; "both": each participating slot gets a boss with `bossChanceInBossSlots`, otherwise (supported slots only) a
 * normal enemy. Together with "bosses" these are the modes of the boss sampler; "classic" is the permutation.
 */
export const BOSS_SAMPLING_MODES = Object.freeze(["bosses", "normals", "both"]);

/** Values under which permutation-v1 behaves exactly as it always did; anything else is refused there. */
export const SAMPLING_NEUTRAL_OPTIONS = Object.freeze({
  enemyBossChanceBossSlots: 90,
  enemyBossChanceNormalSlots: 10,
  enemyRoamingBossSouls: 50,
  enemyRoamingBossesStayDead: false,
  enemyBossReplacement: "classic",
  enemyUniqueBosses: false,
  enemyReplacementChance: 100,
  enemyNormalReplacement: "normals",
  enemyPlacementFit: "anywhere",
  enemyDifficultyMode: "random",
  enemyDifficultyStrictness: "loose",
  enemyTypeReplacement: "disabled",
  enemyCustomPool: null,
});

/** Typed refusal: the custom pool cannot work with the current catalog or configuration. Never retryable. */
export class EnemyPlacementCustomPoolError extends Error {
  constructor(reason, detail, context = {}) {
    super(`ENEMY_PLACEMENT_CUSTOM_POOL_INVALID: ${detail}`);
    this.name = "EnemyPlacementCustomPoolError";
    this.code = "ENEMY_PLACEMENT_CUSTOM_POOL_INVALID";
    this.reason = reason;
    Object.assign(this, context);
  }
}

const MODEL_ID = /^[A-Za-z][A-Za-z0-9_]{0,15}$/u;

/** Canonical form of a custom pool: unique model ids in ascending order, or null for the full pool. */
export function canonicalCustomPool(value) {
  if (value === null || value === undefined) return null;
  if (!Array.isArray(value)) throw new EnemyPlacementCustomPoolError("format", "enemyCustomPool must be null or a list of model ids.");
  const bad = value.filter((entry) => typeof entry !== "string" || !MODEL_ID.test(entry));
  if (bad.length > 0) throw new EnemyPlacementCustomPoolError("format", `enemyCustomPool entries must be model ids such as c2500 (got ${bad.slice(0, 5).map(String).join(", ")}).`);
  const sorted = [...value].sort();
  const duplicates = sorted.filter((entry, index) => index > 0 && entry === sorted[index - 1]);
  if (duplicates.length > 0) throw new EnemyPlacementCustomPoolError("duplicate", `enemyCustomPool lists ${[...new Set(duplicates)].join(", ")} more than once.`, { duplicates: [...new Set(duplicates)] });
  if (sorted.length === 0) throw new EnemyPlacementCustomPoolError("empty", "enemyCustomPool is empty: select at least one enemy or use the full pool (null).");
  return sorted;
}

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

function customPoolOf(config) {
  // Canonical (sorted, unique) form; a malformed, duplicate or empty list raises the typed custom-pool error.
  return canonicalCustomPool(config.enemyCustomPool);
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
  if (!TYPE_REPLACEMENT_MODES.includes(config.enemyTypeReplacement)) {
    problems.push(`enemyTypeReplacement must be ${TYPE_REPLACEMENT_MODES.join(", ")} (got ${String(config.enemyTypeReplacement)}).`);
  }
  if (!BOSS_REPLACEMENT_MODES.includes(config.enemyBossReplacement)) {
    problems.push(`enemyBossReplacement must be ${BOSS_REPLACEMENT_MODES.join(" or ")} (got ${String(config.enemyBossReplacement)}).`);
  }
  if (typeof config.enemyUniqueBosses !== "boolean") {
    problems.push(`enemyUniqueBosses must be true or false (got ${String(config.enemyUniqueBosses)}).`);
  } else if (config.enemyUniqueBosses && !["bosses", "both"].includes(config.enemyBossReplacement)) {
    problems.push("enemyUniqueBosses needs enemyBossReplacement \"bosses\" or \"both\" (it only changes how bosses are drawn).");
  }
  if (!Number.isInteger(config.enemyBossChanceBossSlots) || config.enemyBossChanceBossSlots < 0 || config.enemyBossChanceBossSlots > 100) {
    problems.push(`enemyBossChanceBossSlots must be an integer from 0 to 100 (got ${String(config.enemyBossChanceBossSlots)}).`);
  } else if (config.enemyBossChanceBossSlots !== SAMPLING_NEUTRAL_OPTIONS.enemyBossChanceBossSlots && config.enemyBossReplacement !== "both") {
    problems.push("enemyBossChanceBossSlots only applies with enemyBossReplacement \"both\" (a boss or a normal enemy per boss slot).");
  }
  const roamingMode = ROAMING_BOSS_MODES.includes(config.enemyNormalReplacement);
  if (!Number.isInteger(config.enemyBossChanceNormalSlots) || config.enemyBossChanceNormalSlots < 0 || config.enemyBossChanceNormalSlots > 100) {
    problems.push(`enemyBossChanceNormalSlots must be an integer from 0 to 100 (got ${String(config.enemyBossChanceNormalSlots)}).`);
  } else if (config.enemyBossChanceNormalSlots !== SAMPLING_NEUTRAL_OPTIONS.enemyBossChanceNormalSlots && config.enemyNormalReplacement !== "both") {
    problems.push("enemyBossChanceNormalSlots only applies with enemyNormalReplacement \"both\" (a boss or a normal enemy per slot).");
  }
  if (!Number.isInteger(config.enemyRoamingBossSouls) || config.enemyRoamingBossSouls < 0 || config.enemyRoamingBossSouls > 100) {
    problems.push(`enemyRoamingBossSouls must be an integer from 0 to 100 (got ${String(config.enemyRoamingBossSouls)}).`);
  } else if (config.enemyRoamingBossSouls !== SAMPLING_NEUTRAL_OPTIONS.enemyRoamingBossSouls && !roamingMode) {
    problems.push("enemyRoamingBossSouls only applies when bosses can appear in regular enemy slots (enemyNormalReplacement \"bosses\" or \"both\").");
  }
  if (typeof config.enemyRoamingBossesStayDead !== "boolean") {
    problems.push(`enemyRoamingBossesStayDead must be true or false (got ${String(config.enemyRoamingBossesStayDead)}).`);
  } else if (config.enemyRoamingBossesStayDead && !roamingMode) {
    problems.push("enemyRoamingBossesStayDead only applies when bosses can appear in regular enemy slots (enemyNormalReplacement \"bosses\" or \"both\").");
  }
  if (config.enemyCustomPool !== null && config.enemyCustomPool !== undefined && algorithm === "permutation-v1") {
    problems.push("enemyCustomPool only applies to sampling-v1; it must be null with permutation-v1.");
  } else if (algorithm === "sampling-v1") {
    canonicalCustomPool(config.enemyCustomPool); // typed ENEMY_PLACEMENT_CUSTOM_POOL_INVALID for a malformed, duplicate or empty list
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
    typeReplacement: config.enemyTypeReplacement,
    customPool: customPoolOf(config),
    bossReplacement: config.enemyBossReplacement,
    uniqueBosses: config.enemyUniqueBosses,
    bossChanceInBossSlots: config.enemyBossChanceBossSlots,
    bossChanceInNormalSlots: config.enemyBossChanceNormalSlots,
    roamingBossSouls: config.enemyRoamingBossSouls,
    roamingBossesStayDead: config.enemyRoamingBossesStayDead,
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

const STABLE_TYPE_MODEL = /^[A-Za-z0-9_]+$/u;

/**
 * Stream name of a Type replacement group: the original model within one map. Attempt 0 is `type:<map>:<model>`, a
 * map-local retry `n` is `attempt:<n>:type:<map>:<model>`. Disjoint from every slot stream name, so enabling Type
 * replacement never moves a draw of ordinary slot sampling.
 */
export function typeStreamName(mapId, model, attempt = 0) {
  if (!/^m\d{2}_\d{2}_\d{2}_\d{2}$/u.test(String(mapId)) || !STABLE_TYPE_MODEL.test(String(model))) {
    throw new TypeError(`Not a stable type group: ${String(mapId)} / ${String(model)}`);
  }
  if (!Number.isInteger(attempt) || attempt < 0) throw new RangeError("attempt must be a non-negative integer.");
  return attempt === 0 ? `type:${mapId}:${model}` : `attempt:${attempt}:type:${mapId}:${model}`;
}

let slotStreamDerivations = 0;

/** 32-bit seed of the stream that owns one slot: master/combined seed + "enemy-placement-v1" + "slot:<id>". */
export function deriveSlotStreamSeed(seed, slotOrId, attempt = 0) {
  slotStreamDerivations += 1;
  return deriveSeed(seed, slotStreamName(slotOrId, attempt), SAMPLING_RNG_VERSION);
}

export function createSlotStream(seed, slotOrId, attempt = 0) {
  return new DeterministicRng(deriveSlotStreamSeed(seed, slotOrId, attempt));
}

export function createTypeStream(seed, mapId, model, attempt = 0) {
  slotStreamDerivations += 1;
  return new DeterministicRng(deriveSeed(seed, typeStreamName(mapId, model, attempt), SAMPLING_RNG_VERSION));
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
 * Reference-counted model set of one map for Type replacement. Unlike {@link ModelBudget} (which only grows) a model
 * leaves the set when its last owner releases it, so a group that moves from its original model to another one frees
 * the original and takes the new one. Owners are *solid* (the fixed plans, groups left alone, groups already decided)
 * or *pending* (a group that has not decided yet and still owns its original model, the fallback that keeps it
 * possible to leave the group alone). `evaluate` admits a model that a solid owner already holds, or - below the cap -
 * any other model; a model held only by pending owners counts as new, because choosing it does not reduce the final
 * count. A set that starts above the cap (an unavoidable overflow) can therefore only shrink, through solid models,
 * until it is below the cap again.
 */
export class ModelLedger {
  #cap;
  #mapId;
  #solid = new Map();
  #pending = new Map();
  #reserve = new Set();

  constructor({ mapId, cap = MODEL_BUDGET_CAP }) {
    if (!Number.isInteger(cap) || cap < 1) throw new RangeError("cap must be a positive integer.");
    this.#mapId = mapId;
    this.#cap = cap;
  }

  get cap() { return this.#cap; }
  get size() { return new Set([...this.#solid.keys(), ...this.#pending.keys(), ...this.#reserve]).size; }
  has(model) { return this.#solid.has(model) || this.#pending.has(model) || this.#reserve.has(model); }
  /** True when something that will stay (a fixed plan, a group left alone or a decided group) owns the model. */
  isSolid(model) { return this.#solid.has(model); }
  models() { return [...new Set([...this.#solid.keys(), ...this.#pending.keys(), ...this.#reserve])].sort(); }

  /**
   * Holds a model as coverage insurance (restricted custom pool only): it occupies capacity from the start, so a later
   * group can always be given it, and choosing it costs nothing. It is not an owner: {@link dropReserve} removes what no
   * group took before the final model set is counted.
   */
  reserve(model) { this.#reserve.add(model); }
  dropReserve() { this.#reserve.clear(); }

  /** Adds one owner of a model (no cap check: the caller decides with {@link evaluate}). */
  hold(model, { pending = false } = {}) {
    const table = pending ? this.#pending : this.#solid;
    table.set(model, (table.get(model) ?? 0) + 1);
  }

  /** Removes one owner; the model leaves the set with its last owner. */
  release(model, { pending = false } = {}) {
    const table = pending ? this.#pending : this.#solid;
    const count = table.get(model);
    if (count === undefined) throw new RangeError(`${model} has no ${pending ? "pending" : "solid"} owner in ${this.#mapId}.`);
    if (count === 1) table.delete(model); else table.set(model, count - 1);
  }

  evaluate(model) {
    if (this.#solid.has(model) || this.#reserve.has(model)) return { admitted: true, alreadyPresent: true };
    if (this.size < this.#cap) return { admitted: true, alreadyPresent: this.#pending.has(model) };
    return { admitted: false, reason: "model-budget", size: this.size, cap: this.#cap, mapId: this.#mapId };
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
  "sampled", "category-fallback", "kept-vanilla", "in-set-fit", "shared-fit", "fit-relaxed", "infeasible",
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
  const groups = [];
  return {
    algorithm,
    rngVersion: algorithm === "sampling-v1" ? SAMPLING_RNG_VERSION : null,
    level,
    classTableHash: null,
    /** The effective sampling options of the run (mode, strictness, fit, chance, ...). */
    options: null,
    /** Custom pool state and coverage (sampling-v1): enabled, total, selected, hash, size/difficulty coverage. */
    customPool: null,
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
      if (level === "detail") {
        slots.push(...(json.slots ?? []));
        groups.push(...(json.groups ?? []));
      }
    },
    /** One Type replacement group decision (kept at the detail level only; counters go through count()). */
    recordGroup(entry) {
      if (level === "detail") groups.push({ ...entry });
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
        algorithm: this.algorithm, rngVersion: this.rngVersion, level, classTableHash: this.classTableHash, options: this.options, customPool: this.customPool, summary,
        ...(level === "detail" ? { slots, groups } : {}),
      };
    },
  };
}

/** The one-line identity recorded for the frozen algorithm: no counters, no streams. */
export const permutationDiagnostics = () => ({ algorithm: "permutation-v1", rngVersion: null, level: "summary", classTableHash: null, options: null, customPool: null, summary: null });

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
