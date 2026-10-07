// DS1 Triple Randomizer, repository-authored (not part of the upstream pin; listed in the module-set test and the pin file).
//
// Key placement (Part H, patches/0023; docs/ITEM_PROGRESSION_MODEL.md). Keys are the progression items of FogMod's annotation
// (Basement Key, Lordvessel, Lord Souls ...). A key may leave its vanilla pickup and lie in another pickup that is awarded for certain; the world
// stays completable because every key is placed ONLY where the player can already reach with everything that is not yet placed (an "assumed fill"),
// and the finished placement is checked once more as a whole.
//
// There is exactly ONE model of the world: FogMod's own graph, built with every randomization option off (the vanilla doors, gates and warps), exported
// by `FogHost progression-model` as a list of edges between areas, each edge with the condition FogMod's checker evaluates. This file only EVALUATES that
// graph (a closure: an edge can be used when its condition holds, a condition names areas that are visited and keys that lie in a visited area). The
// result of this evaluation is cross-checked against FogMod's own `GraphChecker` by `apps/backend/scripts/progressionDifferential.mjs`, and every generated
// package is checked again by FogMod's own reader and checker (`FogHost progression-check`).
//
// Nothing here draws from a stream that another option uses: every draw comes from `item-keys:<attempt>` or `item-keys-fill`.
import { createStream } from "./rng.js";

export const KEY_PLACEMENT_MODES = ["vanilla", "shuffled"];
export const PROGRESSION_MODEL_FORMAT = "ds1tr-progression-model-1";
const MAX_ATTEMPTS = 32;

/** A typed failure: `code` is stable (it reaches the log and the user message), `diagnostics` are counters, never a location. */
export class KeyPlacementError extends Error {
  constructor(code, message, diagnostics = {}) {
    super(`${code}: ${message}`);
    this.code = code;
    this.diagnostics = diagnostics;
  }
}

// ---- the graph -----------------------------------------------------------------------------------------------------------------------

function compile(expr) {
  if (expr === true) return () => true;
  if (expr === false) return () => false;
  if (typeof expr === "string") return (has) => has(expr);
  if (expr && Array.isArray(expr.all)) {
    const parts = expr.all.map(compile);
    return (has) => parts.every((part) => part(has));
  }
  if (expr && Array.isArray(expr.any)) {
    const parts = expr.any.map(compile);
    return (has) => parts.some((part) => part(has));
  }
  throw new KeyPlacementError("KEY_PLACEMENT_MODEL_INVALID", "A condition of the progression model is not understood.");
}

/** Validates an exported model and prepares it for evaluation. */
export function prepareModel(model) {
  if (!model || model.format !== PROGRESSION_MODEL_FORMAT) {
    throw new KeyPlacementError("KEY_PLACEMENT_MODEL_INVALID", `The progression model has the wrong format (expected ${PROGRESSION_MODEL_FORMAT}).`);
  }
  for (const field of ["areas", "edges", "keys", "lotAreas", "start", "prison"]) {
    if (model[field] === undefined) throw new KeyPlacementError("KEY_PLACEMENT_MODEL_INVALID", `The progression model has no ${field}.`);
  }
  const areas = new Set(model.areas);
  const edges = model.edges.map((edge) => {
    if (!areas.has(edge.from) || !areas.has(edge.to)) throw new KeyPlacementError("KEY_PLACEMENT_MODEL_INVALID", "An edge names an unknown area.");
    return { from: edge.from, to: edge.to, test: compile(edge.when) };
  });
  const keys = new Map(model.keys.map((key) => [key.name, key]));
  for (const key of model.keys) if (!areas.has(key.area)) throw new KeyPlacementError("KEY_PLACEMENT_MODEL_INVALID", `Key ${key.name} is in an unknown area.`);
  const lotAreas = new Map(Object.entries(model.lotAreas).map(([lot, area]) => [Number(lot), area]));
  return { raw: model, areas, edges, keys, lotAreas, optional: new Set(model.optionalAreas ?? []), start: model.start, prison: model.prison };
}

/**
 * The areas visited from `start`: an edge is usable when its condition is true, where a name is true when it is a visited area, a key that is in
 * `inHand`, or a key that lies in a visited area (`keyAreas`: key name -> area). The same least fixed point as FogMod's GraphChecker.
 */
export function visitedAreas(prepared, keyAreas, inHand = new Set(), start = prepared.start, countOnly = null) {
  const visited = new Set([start]);
  const has = (name) => visited.has(name) || inHand.has(name) || (keyAreas.has(name) && visited.has(keyAreas.get(name)));
  let changed = true;
  while (changed) {
    changed = false;
    for (const edge of prepared.edges) {
      if (visited.has(edge.to) || !visited.has(edge.from)) continue;
      if (edge.test(has)) {
        visited.add(edge.to);
        changed = true;
      }
    }
  }
  return countOnly ? countOnly(visited) : visited;
}

/** The areas that must be visited for a completable world (everything but the optional areas), and the Seath capture rule. */
export function checkCompletable(prepared, keyAreas) {
  const visited = visitedAreas(prepared, keyAreas);
  const unvisited = [...prepared.areas].filter((area) => !visited.has(area) && !prepared.optional.has(area)).sort();
  // The capture: the player starts in the prison with only what the prison itself holds and must be able to leave it.
  const prisonAreas = new Set(prepared.prison.areas);
  const prisonKeys = new Map([...keyAreas].filter(([, area]) => prisonAreas.has(area)));
  const prisonVisited = visitedAreas(prepared, prisonKeys, new Set(), prepared.prison.start);
  return { completable: unvisited.length === 0, unvisited, prisonEscapable: prisonVisited.has(prepared.prison.mustReach) };
}

// ---- lots and their areas ---------------------------------------------------------------------------------------------------------------

/** The area of every item lot row the model covers: a row belongs to the area of the nearest listed row below it in an unbroken run of known rows. */
export function coveredRowAreas(prepared, knownRows) {
  const result = new Map();
  for (const [start, area] of [...prepared.lotAreas].sort((left, right) => left[0] - right[0])) {
    for (let row = start; knownRows.has(row); row += 1) if (!result.has(row)) result.set(row, area);
  }
  return result;
}

// ---- the placement -------------------------------------------------------------------------------------------------------------------

/**
 * Places the movable keys with an assumed fill.
 *   movable:      key names the planner may move
 *   fixed:        Map key name -> area for every other key (they stay where the game puts them)
 *   destinations: [{ id, area }] pickups a key may lie in (each at most once)
 * Returns { placed: Map key name -> destination id, attempts, diagnostics }. Bounded and deterministic: attempt n draws from the stream item-keys:n.
 */
export function placeKeys({ prepared, seed, version, movable, fixed, destinations }) {
  const diagnostics = { movableKeys: movable.length, destinations: destinations.length, attempts: 0, deadEnds: [] };
  for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt += 1) {
    diagnostics.attempts = attempt + 1;
    const rng = createStream(seed, `item-keys:${attempt}`, version);
    const order = rng.shuffle([...movable].sort());
    const placed = new Map();
    const used = new Set();
    const areaOf = new Map(fixed);
    let failed = null;
    for (const key of order) {
      const unplaced = new Set(order.filter((name) => name !== key && !placed.has(name)));
      const reachable = visitedAreas(prepared, areaOf, unplaced);
      const candidates = destinations.filter((destination) => !used.has(destination.id) && reachable.has(destination.area));
      if (candidates.length === 0) { failed = key; break; }
      const pick = candidates[rng.int(candidates.length)];
      placed.set(key, pick.id);
      used.add(pick.id);
      areaOf.set(key, pick.area);
    }
    if (failed !== null) {
      diagnostics.deadEnds.push(failed);
      continue;
    }
    const check = checkCompletable(prepared, areaOf);
    if (!check.completable || !check.prisonEscapable) {
      // An assumed fill cannot end here; if it does, the model and this code disagree and the world must not be written.
      throw new KeyPlacementError("KEY_PLACEMENT_INVALID", "A finished key placement failed the final check of the progression model.", { ...diagnostics, unvisited: check.unvisited, prisonEscapable: check.prisonEscapable });
    }
    return { placed, attempts: attempt + 1, areaOf, diagnostics };
  }
  // The dead ends name the keys that had no reachable destination (a key name, never a location).
  throw new KeyPlacementError("KEY_PLACEMENT_UNSATISFIABLE", `No key placement was found in ${MAX_ATTEMPTS} attempts (NO_REACHABLE_DESTINATION).`, { ...diagnostics, reason: "NO_REACHABLE_DESTINATION" });
}

/** The shuffle of the displaced items over the freed key rows, from its own stream. `accepts(item, row)` is the DLC rule of the pool. */
export function pairDisplaced({ seed, version, displaced, freed, accepts }) {
  const rng = createStream(seed, "item-keys-fill", version);
  // DLC rows (the most restricted) first, so the restricted items cannot have used up the items they accept.
  const order = [...freed].sort((left, right) => Number(accepts.restricted(right)) - Number(accepts.restricted(left)) || left.rowId - right.rowId || left.slot - right.slot);
  const pool = rng.shuffle([...displaced].sort((left, right) => left.rowId - right.rowId || left.slot - right.slot));
  const pairs = [];
  for (const row of order) {
    const index = pool.findIndex((item) => accepts.fits(item, row));
    if (index < 0) throw new KeyPlacementError("KEY_PLACEMENT_FILL_IMPOSSIBLE", "No displaced item fits a freed key pickup.", { freed: freed.length, displaced: displaced.length });
    pairs.push({ target: row, source: pool.splice(index, 1)[0] });
  }
  return pairs;
}
