// DS1 Triple Randomizer - repository-authored module (GPL-3.0-only); not part of upstream ds-randomizer.
//
// The `sampling-v1` enemy placement planner for REGULAR enemy slots (docs/ENEMY_PLACEMENT_MODEL.md, Part A).
// It is a pure function of the slots it is given: no I/O, no catalog access, no global state. Bosses, NPCs,
// linked units, special entities and every placement decoration stay with the callers in generator.js.
//
// Per slot (in stable id order inside a stable map order):
//   1. replacement chance - the slot's first draw from its own stream (`slot:<id>`, "enemy-placement-v1");
//   2. candidate = uniform draw over the surviving TYPES (size fit, Prevent same, model budget) with list shrink;
//   3. bounded fallback ladder when nothing survives: keep vanilla, in-set fitting type, relaxed fit, failure;
//   4. variant = uniform draw among the type's (NpcParam, think-param) variants.
// The model set of a map is a ModelBudget: fixed and vanilla-kept models are in it from the start, together with
// a coverage reserve that gives every participating slot an in-set type that fits it.

import { createHash } from "node:crypto";
import {
  EnemyPlacementInfeasibleError,
  MODEL_BUDGET_CAP,
  ModelBudget,
  ModelBudgetError,
  createSlotStream,
} from "./placement.js";

const compare = (left, right) => (left < right ? -1 : left > right ? 1 : 0);

// ---- derived classes ----------------------------------------------------------------------------------------------

export const CLASS_TABLE_VERSION = "regular-size-v1";

/** Footprint edges (game units) between the size classes 0-5. Derived scale, not a copy of any tool's table. */
export const SIZE_EDGES = Object.freeze([0.9, 1.7, 2.6, 4.2, 7]);

/** Size class given to a slot whose capsule is unknown (it accepts any type). */
export const UNKNOWN_SLOT_SIZE = SIZE_EDGES.length;

export class EnemyPlacementClassError extends Error {
  constructor(model) {
    super(`ENEMY_PLACEMENT_CLASS_UNDERIVABLE: no usable hit capsule for any regular slot of ${model}.`);
    this.name = "EnemyPlacementClassError";
    this.code = "ENEMY_PLACEMENT_CLASS_UNDERIVABLE";
    this.model = model;
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

/**
 * The effective class table of one run: for every model that occurs in a regular slot, its size class and its
 * variants. A model's footprint is the largest usable footprint among its regular slots, so the result does not
 * depend on catalog order. The hash covers the derivation version, the edges and (model, size) pairs.
 */
export function deriveRegularClassTable(slots) {
  const byModel = new Map();
  for (const slot of slots) {
    if (!byModel.has(slot.modelName)) byModel.set(slot.modelName, []);
    byModel.get(slot.modelName).push(slot);
  }
  const types = [];
  for (const model of [...byModel.keys()].sort(compare)) {
    const members = byModel.get(model).sort((left, right) => compare(left.id, right.id));
    const footprints = members.map(slotFootprint).filter((value) => value !== null);
    if (footprints.length === 0) throw new EnemyPlacementClassError(model);
    const footprint = Math.max(...footprints);
    const variants = new Map();
    for (const member of members) {
      const key = `${member.npcParamId}:${member.thinkParamId}:${member.battleGoalId}`;
      if (!variants.has(key)) variants.set(key, { key, npcParamId: member.npcParamId, thinkParamId: member.thinkParamId, source: member });
    }
    types.push({
      model,
      size: sizeClassOfFootprint(footprint),
      footprint,
      variants: [...variants.values()].sort((left, right) => compare(left.key, right.key)),
    });
  }
  const hash = createHash("sha256")
    .update(JSON.stringify({ version: CLASS_TABLE_VERSION, edges: SIZE_EDGES, types: types.map((type) => [type.model, type.size]) }))
    .digest("hex");
  return { version: CLASS_TABLE_VERSION, hash, types, byModel: new Map(types.map((type) => [type.model, type])) };
}

/** Size class of a slot: its model's class, or its own capsule (any type fits a slot whose capsule is unknown). */
export function slotSizeClass(table, slot) {
  const type = table.byModel.get(slot.modelName);
  if (type) return type.size;
  const footprint = slotFootprint(slot);
  return footprint === null ? UNKNOWN_SLOT_SIZE : sizeClassOfFootprint(footprint);
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

// ---- fallback ladder -----------------------------------------------------------------------------------------------

/**
 * What a slot receives when the sampled draw found no candidate (model document 6.7, regular slots). Pure and
 * bounded: every rung is a scan over a sorted list, nothing draws again.
 *   kept-vanilla - the slot's own model is in the set or the set has room (allowed under Prevent same);
 *   in-set-fit   - the smallest in-set type that fits the slot (and is not its own model under Prevent same);
 *   fit-relaxed  - with Placement fit "fits" only: the smallest in-set type whatever its size;
 *   infeasible   - nothing is left; the caller raises ENEMY_PLACEMENT_INFEASIBLE.
 * `chosen` is null for kept-vanilla and infeasible.
 */
export function fallbackLadder({ slot, budget, table, maxSize, fit, preventSame }) {
  if (budget.evaluate(slot.modelName).admitted) return { stage: "kept-vanilla", chosen: null };
  const inSet = budget.models().map((model) => table.byModel.get(model)).filter(Boolean)
    .filter((type) => !(preventSame && type.model === slot.modelName))
    .sort((left, right) => left.size - right.size || compare(left.model, right.model));
  const fitting = inSet.find((type) => type.size <= maxSize);
  if (fitting) return { stage: "in-set-fit", chosen: fitting };
  if (fit === "fits" && inSet.length > 0) return { stage: "fit-relaxed", chosen: inSet[0] };
  return { stage: "infeasible", chosen: null };
}

// ---- the planner ------------------------------------------------------------------------------------------------------

/**
 * Plans sampling-v1 for regular slots. Returns `assignment`: slot id -> chosen variant source (a catalog slot)
 * for slots that receive a placement; a slot that stays vanilla has no entry.
 *
 * @param {object} input
 * @param {string} input.seed                    combined seed (the stream seed of the whole run)
 * @param {{replacementChance:number, normalReplacement:string, fit:string}} input.options
 * @param {boolean} input.preventSame
 * @param {Array<object>} input.slots            regular catalog slots (class table is derived from these)
 * @param {Array<object>} [input.extraSlots]     further regular-class slots with no type of their own (Pinwheel clones)
 * @param {Map<string, Set<string>>} input.fixedModelsByMap
 * @param {object} input.diagnostics             from createPlacementDiagnostics
 */
export function planSampledRegularEnemies({
  seed, options, preventSame, slots, extraSlots = [], fixedModelsByMap, diagnostics, cap = MODEL_BUDGET_CAP,
}) {
  const table = deriveRegularClassTable(slots);
  diagnostics.classTableHash = table.hash;
  const sizeOf = (slot) => slotSizeClass(table, slot);
  const fitSize = (slot) => (options.fit === "fits" ? sizeOf(slot) : UNKNOWN_SLOT_SIZE);
  const assignment = new Map();
  const all = [...slots, ...extraSlots];
  const mapIds = [...new Set(all.map((slot) => slot.mapId))].sort(compare);

  for (const mapId of mapIds) {
    const mapSlots = all.filter((slot) => slot.mapId === mapId).sort((left, right) => compare(left.id, right.id));
    const streams = new Map(mapSlots.map((slot) => [slot.id, createSlotStream(seed, slot.id)]));
    const participates = new Map();
    for (const slot of mapSlots) {
      if (options.normalReplacement === "none") {
        participates.set(slot.id, false);
        diagnostics.count("keptByMode");
      } else {
        // The replacement chance is always the slot's first draw: 1..100 <= chance, so 100 always passes.
        const draw = streams.get(slot.id).int(100) + 1;
        const passes = draw <= options.replacementChance;
        participates.set(slot.id, passes);
        if (!passes) diagnostics.count("keptByChance");
      }
    }
    const fixedModels = new Set(fixedModelsByMap.get(mapId) ?? []);
    for (const slot of mapSlots) if (!participates.get(slot.id)) fixedModels.add(slot.modelName);
    const { budget, kept, trimmed } = buildMapModelSet({
      mapId, cap, fixedModels, participants: mapSlots.filter((slot) => participates.get(slot.id)), sizeOf, fitSize, preventSame, table,
    });
    if (trimmed > 0) diagnostics.count("reserveTrimmed", trimmed);
    const active = new Set(kept.map((slot) => slot.id));
    const modelOfSlot = new Map();

    for (const slot of mapSlots) {
      if (!active.has(slot.id)) {
        modelOfSlot.set(slot.id, slot.modelName);
        diagnostics.record({ slotId: slot.id, slotClass: "normal", participates: false });
        continue;
      }
      const rng = streams.get(slot.id);
      const maxSize = fitSize(slot);
      const pool = table.types.filter((type) => type.size <= maxSize);
      const rejected = [];
      let chosen = null;
      let stage = "sampled";
      const list = [...pool];
      while (list.length > 0) {
        const index = rng.int(list.length);
        const candidate = list[index];
        if (preventSame && candidate.model === slot.modelName) {
          rejected.push({ model: candidate.model, reason: "prevent-same" });
        } else if (!budget.evaluate(candidate.model).admitted) {
          rejected.push({ model: candidate.model, reason: "model-budget" });
        } else {
          chosen = candidate;
          break;
        }
        list.splice(index, 1);
      }
      const sizeRejections = table.types.length - pool.length;
      if (sizeRejections > 0) diagnostics.count("candidatesOverSize", sizeRejections);

      if (!chosen) {
        const fallback = fallbackLadder({ slot, budget, table, maxSize, fit: options.fit, preventSame });
        stage = fallback.stage;
        chosen = fallback.chosen;
        if (stage === "infeasible") {
          diagnostics.record({ slotId: slot.id, slotClass: "normal", participates: true, category: "normal", stage, rejected });
          throw new EnemyPlacementInfeasibleError(mapId, slot.id, `model ${slot.modelName}, in-set models ${budget.size}/${budget.cap}, fit ${options.fit}, prevent same ${preventSame}`);
        }
        if (stage === "kept-vanilla") budget.admit(slot.modelName);
      }

      if (chosen) {
        const variant = chosen.variants[rng.int(chosen.variants.length)];
        budget.admit(chosen.model);
        assignment.set(slot.id, variant.source);
        modelOfSlot.set(slot.id, chosen.model);
        diagnostics.record({ slotId: slot.id, slotClass: "normal", participates: true, category: "normal", selected: chosen.model, stage, rejected });
      } else {
        modelOfSlot.set(slot.id, slot.modelName);
        diagnostics.record({ slotId: slot.id, slotClass: "normal", participates: true, category: "normal", selected: slot.modelName, stage, rejected });
      }
    }

    // Post-condition: the models the map really holds after the run (what DsrDataTool keeps) fit the cap.
    const finalModels = new Set(fixedModelsByMap.get(mapId) ?? []);
    for (const slot of mapSlots) finalModels.add(modelOfSlot.get(slot.id));
    if (finalModels.size > cap) throw new ModelBudgetError(mapId, cap, [...finalModels].sort(compare));
    const replaced = mapSlots.filter((slot) => assignment.has(slot.id)).length;
    diagnostics.setMap(mapId, {
      slots: mapSlots.length, replaced, finalModels: finalModels.size, modelSet: budget.size,
    });
  }
  return { assignment, table };
}
