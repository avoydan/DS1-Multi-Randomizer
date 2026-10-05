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
import {
  EnemyPlacementInfeasibleError,
  MODEL_BUDGET_CAP,
  ModelBudget,
  ModelBudgetError,
  createPlacementDiagnostics,
  createSlotStream,
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
export function buildMapModelSet({ mapId, cap, fixedModels, participants, sizeOf, fitSize, preventSame, table }) {
  const base = new Set(fixedModels);
  if (base.size > cap) throw new ModelBudgetError(mapId, cap, [...base]);
  const active = [...participants].sort((left, right) => sizeOf(left) - sizeOf(right) || compare(left.id, right.id));
  const wanted = [];
  if (active.length > 0) {
    const ceiling = fitSize(active[0]);
    const needed = preventSame ? 2 : 1;
    const fitting = [...base].filter((model) => table.byModel.has(model) && table.byModel.get(model).size <= ceiling).length;
    for (const slot of active) {
      if (fitting + wanted.length >= needed) break;
      if (!base.has(slot.modelName) && !wanted.includes(slot.modelName) && table.byModel.has(slot.modelName)) wanted.push(slot.modelName);
    }
  }
  const reserve = wanted.slice(0, Math.max(0, cap - base.size));
  return {
    budget: new ModelBudget({ mapId, cap, fixedModels: [...base, ...reserve] }),
    reserve,
    trimmed: wanted.length - reserve.length,
    kept: active,
  };
}

// ---- effective budget -------------------------------------------------------------------------------------------------

/**
 * Non-worsening model budget (model document 10.2). The target is `cap` (30) distinct final models per map. When
 * the models outside the sampler's control - fixed plans plus the vanilla models of slots it leaves alone -
 * already exceed it, that baseline is the cap for the map: the sampler may keep or reduce it, never raise it.
 */
export const effectiveModelBudget = (baseline, cap = MODEL_BUDGET_CAP) => Math.max(cap, baseline);

// ---- fallback ladder -----------------------------------------------------------------------------------------------

/**
 * What a slot receives when the sampled draw found no candidate (model document 6.7, regular slots). Pure and
 * bounded: every rung is a scan over a sorted list, nothing draws again.
 *   kept-vanilla - the slot's own model is in the set or the set has room (allowed under Prevent same);
 *   in-set-fit   - the smallest in-set type that fits the slot (and is not its own model under Prevent same);
 *   infeasible   - nothing is left; the caller retries the map with attempt-specific streams, and raises
 *                  ENEMY_PLACEMENT_INFEASIBLE when every attempt fails.
 * There is deliberately no "fit relaxed" rung: with the non-worsening budget a slot can be left without any
 * fitting type only when the map has no headroom, and placing an oversized type would break the documented fit
 * invariant. `chosen` is null for kept-vanilla and infeasible. The in-set rung ignores difficulty (a counted
 * difficulty lapse): physical fit is never relaxed, difficulty may be.
 */
export function fallbackLadder({ slot, budget, table, maxSize, preventSame }) {
  if (budget.evaluate(slot.modelName).admitted) return { stage: "kept-vanilla", chosen: null };
  const inSet = budget.models().map((model) => table.byModel.get(model)).filter(Boolean)
    .filter((type) => !(preventSame && type.model === slot.modelName))
    .sort((left, right) => left.size - right.size || compare(left.model, right.model));
  const fitting = inSet.find((type) => type.size <= maxSize);
  if (fitting) return { stage: "in-set-fit", chosen: fitting };
  return { stage: "infeasible", chosen: null };
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
 * @param {{replacementChance:number, normalReplacement:string, fit:string, difficultyMode:string, difficultyStrictness:string}} input.options
 * @param {boolean} input.preventSame
 * @param {Array<object>} input.slots            regular catalog slots (class table is derived from these)
 * @param {Array<object>} [input.extraSlots]     further regular-class slots with no type of their own (Pinwheel clones)
 * @param {Map<string, Set<string>>} input.fixedModelsByMap
 * @param {object} input.diagnostics             from createPlacementDiagnostics
 */
export function planSampledRegularEnemies({
  seed, options, preventSame, slots, extraSlots = [], fixedModelsByMap, diagnostics, cap = MODEL_BUDGET_CAP, mapAttempts = MAP_ATTEMPTS,
}) {
  const settings = {
    ...options,
    difficultyMode: options.difficultyMode ?? "random",
    difficultyStrictness: options.difficultyStrictness ?? "loose",
  };
  const table = deriveRegularClassTable(slots);
  diagnostics.classTableHash = table.hash;
  diagnostics.options = { ...settings, preventSame };
  const assignment = new Map();
  const all = [...slots, ...extraSlots];
  const mapIds = [...new Set(all.map((slot) => slot.mapId))].sort(compare);

  for (const mapId of mapIds) {
    const mapSlots = all.filter((slot) => slot.mapId === mapId).sort((left, right) => compare(left.id, right.id));
    let planned = null;
    let lastError = null;
    for (let attempt = 0; attempt < mapAttempts; attempt += 1) {
      const scratch = createPlacementDiagnostics({ algorithm: "sampling-v1", level: diagnostics.level });
      try {
        planned = planMapAttempt({ seed, settings, preventSame, table, mapId, mapSlots, attempt, fixedModelsByMap, cap, diagnostics: scratch });
      } catch (error) {
        if (!(error instanceof EnemyPlacementInfeasibleError)) throw error;
        lastError = error;
        diagnostics.count("mapAttemptsFailed");
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
        + `prevent same ${preventSame}, chance ${settings.replacementChance}%, baseline ${lastError.baseline}, effective cap ${lastError.effectiveCap}`,
        { attempts: mapAttempts, baseline: lastError.baseline, effectiveCap: lastError.effectiveCap, difficultyMode: settings.difficultyMode,
          difficultyStrictness: settings.difficultyStrictness, fit: settings.fit, terminal: lastError.detail });
    }
    for (const [slotId, source] of planned.assignment) assignment.set(slotId, source);
    diagnostics.setMap(mapId, { ...planned.info, attempts: planned.attempt + 1 });
  }
  return { assignment, table };
}

/** One attempt at one map. Pure; throws EnemyPlacementInfeasibleError (with baseline/cap) when a slot cannot be placed. */
function planMapAttempt({ seed, settings, preventSame, table, mapId, mapSlots, attempt, fixedModelsByMap, cap, diagnostics }) {
  const sizeOf = (slot) => slotSizeClass(table, slot);
  const fitSize = (slot) => (settings.fit === "fits" ? sizeOf(slot) : UNKNOWN_SLOT_SIZE);
  const curve = CURVE_MODES.has(settings.difficultyMode);
  const easyAsylum = EASY_ASYLUM_MODES.has(settings.difficultyMode) && mapId === EASY_ASYLUM_MAP;
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
  const fixedModels = new Set(fixedModelsByMap.get(mapId) ?? []);
  for (const slot of mapSlots) if (!participates.get(slot.id)) fixedModels.add(slot.modelName);
  // Non-worsening budget: the models the sampler cannot remove (fixed plans and the vanilla models of slots
  // it leaves alone) are the baseline. The map may hold `cap` models, or its baseline when that is larger;
  // the sampler never takes it above that.
  const baseline = fixedModels.size;
  const mapCap = effectiveModelBudget(baseline, cap);
  if (baseline > cap) diagnostics.count("mapsAboveCap");
  const { budget, kept, trimmed } = buildMapModelSet({
    mapId, cap: mapCap, fixedModels, participants: mapSlots.filter((slot) => participates.get(slot.id)), sizeOf, fitSize, preventSame, table,
  });
  if (trimmed > 0) diagnostics.count("reserveTrimmed", trimmed);
  const active = new Set(kept.map((slot) => slot.id));
  const assignment = new Map();
  const modelOfSlot = new Map();

  for (const slot of mapSlots) {
    if (!active.has(slot.id)) {
      modelOfSlot.set(slot.id, slot.modelName);
      diagnostics.record({ slotId: slot.id, slotClass: "normal", participates: false, attempt });
      continue;
    }
    const rng = streams.get(slot.id);
    const maxSize = fitSize(slot);
    const fitPool = table.types.filter((type) => type.size <= maxSize);
    const rejected = [];
    const info = { attempt };
    let budgetBlocked = false;

    // Candidate pools, tried in order; each is drawn uniformly with list shrink (steps 5-8 of the filtering order).
    const pools = [];
    if (easyAsylum) {
      pools.push(fitPool.filter((type) => type.difficulty <= EASY_ASYLUM_MAX_CLASS));
    } else if (curve) {
      const destination = slotDifficultyClass(table, slot);
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
        if (preventSame && candidate.model === slot.modelName) {
          rejected.push({ model: candidate.model, reason: "prevent-same" });
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
    let stage = "sampled";
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
    const sizeRejections = table.types.length - fitPool.length;
    if (sizeRejections > 0) diagnostics.count("candidatesOverSize", sizeRejections);

    if (!chosen) {
      const fallback = fallbackLadder({ slot, budget, table, maxSize, preventSame });
      stage = fallback.stage;
      chosen = fallback.chosen;
      if (stage === "infeasible") {
        diagnostics.record({ slotId: slot.id, slotClass: "normal", participates: true, category: "normal", stage, rejected, ...info });
        throw new EnemyPlacementInfeasibleError(mapId, slot.id,
          `model ${slot.modelName}, in-set models ${budget.size}/${budget.cap}, fit ${settings.fit}, prevent same ${preventSame}`,
          { baseline, effectiveCap: mapCap, detail: `slot ${slot.id}, attempt ${attempt}` });
      }
      if (stage === "kept-vanilla") budget.admit(slot.modelName);
      else if (chosen && (curve || easyAsylum)) diagnostics.count("difficultyLapses");
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

  // Post-condition: the models the map really holds after the run (what DsrDataTool keeps) fit the cap.
  const finalModels = new Set(fixedModelsByMap.get(mapId) ?? []);
  for (const slot of mapSlots) finalModels.add(modelOfSlot.get(slot.id));
  if (finalModels.size > mapCap) throw new ModelBudgetError(mapId, mapCap, [...finalModels].sort(compare));
  const replaced = mapSlots.filter((slot) => assignment.has(slot.id)).length;
  return {
    assignment,
    attempt,
    info: { slots: mapSlots.length, replaced, finalModels: finalModels.size, modelSet: budget.size, baseline, effectiveCap: mapCap },
  };
}
