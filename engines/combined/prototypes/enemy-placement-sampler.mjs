// DESIGN PROTOTYPE - not used by production generation, presets, FogHost or packaging.
//
// Validates the slot-by-slot sampler proposed in docs/ENEMY_PLACEMENT_MODEL.md against a real catalog
// (`DsrDataTool scan` output; game-derived, never committed). It covers regular enemies only (no bosses,
// NPCs, linked units or EMEVD): the question it answers is whether per-slot sampling with per-slot RNG
// streams, derived size/difficulty classes, a per-map model budget enforced by construction and a bounded
// fallback ladder is feasible and deterministic.
//
// Usage: node enemy-placement-sampler.mjs <catalog.json> [seeds]
// Reads only the vendored rng and generator (to learn which slots the engine treats as regular).

import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import path from 'node:path';

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const core = (file) => pathToFileURL(path.join(here, '..', 'vendor', 'src', 'core', file)).href;
const { createStream } = await import(core('rng.js'));
const { generate } = await import(core('generator.js'));

export const ALGORITHM_VERSION = 'enemy-placement-v1';
export const MODEL_CAP = 30;

// ---- derived classes (no legacy tables) -------------------------------------------------------------------

/** Progression order of the maps, an authored table (play order, not difficulty data from any tool). */
const AREA_TIER = {
  m18_01_00_00: 0, m10_01_00_00: 1, m10_02_00_00: 1, m10_00_00_00: 2, m12_00_00_00: 2, m12_00_00_01: 2,
  m13_00_00_00: 3, m11_00_00_00: 3, m14_00_00_00: 4, m16_00_00_00: 4, m15_00_00_00: 5, m15_01_00_00: 5,
  m13_02_00_00: 5, m12_01_00_00: 6, m13_01_00_00: 6, m14_01_00_00: 6, m17_00_00_00: 6, m18_00_00_00: 7,
};

const rank = (values) => {
  const sorted = [...values].sort((a, b) => a - b);
  return (v) => sorted.findIndex((x) => x >= v) / Math.max(1, sorted.length - 1);
};

/** Footprint size class 0-5 from the hit capsule: fixed log-scale edges, independent of any other table. */
export function sizeClass(archetype) {
  const footprint = Math.max(archetype.hitRadius * 2, archetype.hitHeight);
  const edges = [0.9, 1.7, 2.6, 4.2, 7];
  return edges.filter((edge) => footprint >= edge).length;
}

/** Difficulty class 0-7: blend of native-area progression and HP rank over the pool. */
export function difficultyClasses(types) {
  const hpRank = rank(types.map((t) => Math.log(Math.max(1, t.baseHp))));
  const result = new Map();
  for (const t of types) {
    const tiers = t.maps.map((m) => AREA_TIER[m] ?? 3).sort((a, b) => a - b);
    const area = tiers[Math.floor(tiers.length / 2)] / 7;
    const score = 0.6 * area + 0.4 * hpRank(Math.log(Math.max(1, t.baseHp)));
    result.set(t.modelName, Math.max(0, Math.min(7, Math.floor(score * 8))));
  }
  return result;
}

// ---- inputs from the catalog --------------------------------------------------------------------------------

export function loadInputs(catalog, seedForProbe = 'probe') {
  const probe = generate({ randomizeEnemies: true, randomizeBosses: false, randomizeItems: false, seed: seedForProbe, generateSpoilerLog: false },
    { gameCatalog: catalog });
  const regularIds = new Set(probe.placements.enemies
    .filter((p) => /^(count-preserving|passive-|grounded-new-londo|active-darkroot)/u.test(p.compatibility)).map((p) => p.slot));
  const slots = catalog.enemySlots.filter((s) => regularIds.has(s.id)).sort((a, b) => a.id.localeCompare(b.id));
  const byModel = new Map();
  for (const s of slots) {
    if (!byModel.has(s.modelName)) byModel.set(s.modelName, { modelName: s.modelName, variants: new Map(), maps: new Set() });
    const t = byModel.get(s.modelName);
    t.variants.set(`${s.npcParamId}:${s.thinkParamId}`, s);
    t.maps.add(s.mapId);
  }
  const archetypeByModel = new Map();
  for (const a of catalog.enemyArchetypes) if (!archetypeByModel.has(a.modelName)) archetypeByModel.set(a.modelName, a);
  const types = [...byModel.values()].map((t) => {
    const a = archetypeByModel.get(t.modelName);
    return { modelName: t.modelName, variants: [...t.variants.values()], maps: [...t.maps], hitHeight: a.hitHeight, hitRadius: a.hitRadius, baseHp: a.baseHp };
  }).sort((a, b) => a.modelName.localeCompare(b.modelName));
  const difficulty = difficultyClasses(types);
  for (const t of types) { t.size = sizeClass(t); t.difficulty = difficulty.get(t.modelName); }
  const typeByModel = new Map(types.map((t) => [t.modelName, t]));
  for (const s of slots) { const t = typeByModel.get(s.modelName); s.size = t.size; s.difficulty = t.difficulty; }
  const fixedModels = new Map();
  for (const s of catalog.enemySlots) {
    if (regularIds.has(s.id)) continue;
    if (!fixedModels.has(s.mapId)) fixedModels.set(s.mapId, new Set());
    fixedModels.get(s.mapId).add(s.modelName);
  }
  return { slots, types, typeByModel, fixedModels };
}

// ---- the sampler ---------------------------------------------------------------------------------------------

export const defaultOptions = Object.freeze({
  seed: 'proto', replacementChance: 100, fit: 'fit', difficultyMode: 'random', strictness: 'loose',
  preventSame: false, typeReplacement: 'disabled', cap: MODEL_CAP, reserveCoverage: true, curveInFullSet: false,
});

// Difficulty-offset distributions ([class offset, percent]); the rest of the mass stays on the desired class.
// Authored for this design and tunable; the shape (strict stays mostly on the same class, loose and very loose
// reach further, upward more often than downward) follows the option descriptions, not any legacy table.
const STRICTNESS = {
  strict: [[2, 2], [1, 12], [-1, 5]],
  loose: [[4, 2], [3, 3], [2, 4], [1, 15], [-1, 5], [-2, 2], [-3, 1]],
  'very-loose': [[5, 2], [4, 3], [3, 5], [2, 8], [1, 12], [-1, 6], [-2, 4], [-3, 2], [-4, 1]],
};

function pickClass(rng, desired, strictness) {
  const draw = rng.int(101); // 0..100
  let sum = 0;
  for (const [offset, percent] of STRICTNESS[strictness]) {
    const cls = desired + offset;
    if (cls < 0 || cls > 7) continue;
    sum += percent;
    if (sum > draw) return cls;
  }
  return desired;
}

/**
 * Plans regular-enemy replacement. Returns { placements, diagnostics }. Deterministic; bounded.
 *
 * Candidate rules follow the legacy order: while the map still has room in its model set the draw is from the
 * global pool (size-filtered, and difficulty-filtered in the curve modes); once the set is full it is from the
 * types already in the map, filtered by size only. When no candidate exists the legacy tool leaves the slot
 * vanilla; this planner does the same when the model budget allows it and otherwise falls back, in a fixed
 * bounded order, to an in-map type that still fits, then to one that does not (recorded as a diagnostic).
 */
export function plan(inputs, options) {
  const o = { ...defaultOptions, ...options };
  const { slots, types, typeByModel, fixedModels } = inputs;
  const placements = [];
  // rungs: 0 sampled by the legacy rules, 1 kept vanilla, 2 in-map type that fits, 3 in-map type, fit relaxed
  const diagnostics = { rungs: [0, 0, 0, 0], notParticipating: 0, keepVanillaBlocked: 0, errors: [] };
  const maps = [...new Set(slots.map((s) => s.mapId))].sort();
  for (const mapId of maps) {
    const mapSlots = slots.filter((s) => s.mapId === mapId); // already id-sorted
    // Each slot owns a stream, so the number of draws a slot consumes cannot shift another slot's draws.
    // Outcomes still depend on slot order within the map (model set, type-replacement map).
    const streams = new Map(mapSlots.map((s) => [s.id, createStream(o.seed, `slot:${s.id}`, ALGORITHM_VERSION)]));
    const participates = new Map(mapSlots.map((s) => [s.id, streams.get(s.id).int(100) + 1 <= o.replacementChance]));
    const palette = new Set(fixedModels.get(mapId) ?? []);
    for (const s of mapSlots) if (!participates.get(s.id)) { palette.add(s.modelName); diagnostics.notParticipating += 1; }
    // Coverage reserve: the vanilla model(s) of the map's smallest participating slots always stay in the set (two
    // distinct ones with prevent-same), so every participating slot has an in-map type that fits it and keeping
    // the budget never forces a fit violation.
    if (o.reserveCoverage) {
      const smallest = mapSlots.filter((s) => participates.get(s.id)).sort((a, b) => a.size - b.size || a.id.localeCompare(b.id));
      const wanted = o.preventSame ? 2 : 1;
      const reserve = [];
      for (const s of smallest) { if (!reserve.includes(s.modelName)) reserve.push(s.modelName); if (reserve.length === wanted) break; }
      for (const model of reserve) palette.add(model);
    }
    const typeMap = new Map();
    for (const slot of mapSlots) {
      if (!participates.get(slot.id)) continue;
      const rng = streams.get(slot.id);
      const maxSize = o.fit === 'fit' ? slot.size : 5;
      const valid = (t) => !(o.preventSame && t.modelName === slot.modelName);
      const draw = (pool) => { // bounded rejection with list shrink (legacy GetEnemyFromListWithRetry)
        const list = [...pool];
        while (list.length > 0) {
          const index = rng.int(list.length);
          if (valid(list[index])) return list[index];
          list.splice(index, 1);
        }
        return null;
      };
      let chosen = null;
      let rung = 0;
      const phase = palette.size < o.cap ? 'room' : 'full';
      let emptiedClass = false;
      if (o.typeReplacement !== 'disabled' && typeMap.has(slot.modelName)) {
        const t = typeMap.get(slot.modelName);
        if (t.size <= maxSize && valid(t)) chosen = t;
      }
      if (!chosen) {
        if (palette.size < o.cap) {
          // legacy getDifficultyList: draw a class around the slot's own class; an empty list falls back to the own class
          let pool = types.filter((t) => t.size <= maxSize);
          if (o.difficultyMode === 'curve') {
            const drawn = pickClass(rng, slot.difficulty, o.strictness);
            const byClass = (c) => pool.filter((t) => t.difficulty === c);
            emptiedClass = byClass(drawn).length === 0;
            pool = byClass(drawn).length > 0 ? byClass(drawn) : byClass(slot.difficulty);
          }
          chosen = draw(pool);
        } else {
          // Legacy: size only. Refinement (curveInFullSet): keep the class filter inside the full set when it has a candidate.
          let pool = types.filter((t) => palette.has(t.modelName) && t.size <= maxSize);
          if (o.curveInFullSet && o.difficultyMode === 'curve') {
            const drawn = pickClass(rng, slot.difficulty, o.strictness);
            const byClass = (c) => pool.filter((t) => t.difficulty === c);
            pool = byClass(drawn).length > 0 ? byClass(drawn) : byClass(slot.difficulty).length > 0 ? byClass(slot.difficulty) : pool;
          }
          chosen = draw(pool);
        }
      }
      if (!chosen) { // legacy: not replaced
        const vanilla = typeByModel.get(slot.modelName);
        if (!o.preventSame && (palette.has(slot.modelName) || palette.size < o.cap)) { chosen = vanilla; rung = 1; }
        else {
          if (!o.preventSame) diagnostics.keepVanillaBlocked += 1;
          chosen = draw(types.filter((t) => palette.has(t.modelName) && t.size <= maxSize)); rung = 2;
          if (!chosen) { chosen = draw(types.filter((t) => palette.has(t.modelName))); rung = 3; }
        }
      }
      if (!chosen) { diagnostics.errors.push(`${slot.id}: no candidate`); continue; }
      diagnostics.rungs[rung] += 1;
      palette.add(chosen.modelName);
      if (o.typeReplacement !== 'disabled' && !typeMap.has(slot.modelName)) typeMap.set(slot.modelName, chosen);
      const variant = chosen.variants[rng.int(chosen.variants.length)];
      placements.push({ slot: slot.id, map: mapId, from: slot.modelName, to: chosen.modelName, variant: `${variant.npcParamId}:${variant.thinkParamId}`, rung, phase, emptiedClass,
        slotSize: slot.size, size: chosen.size, slotDifficulty: slot.difficulty, difficulty: chosen.difficulty });
    }
    // Budget check: final models of the map (fixed + unchanged slots + placements) never exceed the cap.
    const finalModels = new Set(fixedModels.get(mapId) ?? []);
    const placed = new Map(placements.filter((p) => p.map === mapId).map((p) => [p.slot, p.to]));
    for (const s of mapSlots) finalModels.add(placed.get(s.id) ?? s.modelName);
    if (finalModels.size > o.cap) diagnostics.errors.push(`${mapId}: ${finalModels.size} models`);
  }
  const hash = createHash('sha256').update(JSON.stringify(placements.map((p) => [p.slot, p.to, p.variant]))).digest('hex');
  return { placements, diagnostics, hash };
}

// ---- CLI ---------------------------------------------------------------------------------------------------

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  const catalog = JSON.parse(readFileSync(process.argv[2], 'utf8'));
  const seeds = Number(process.argv[3] ?? 20);
  const inputs = loadInputs(catalog);
  console.log(`regular slots ${inputs.slots.length}, types ${inputs.types.length}`);
  const variants = {
    'legacy-like (100%, fit, random), NO reserve': { reserveCoverage: false },
    'legacy-like (100%, fit, random)': {},
    'curve loose': { difficultyMode: 'curve' },
    'curve strict': { difficultyMode: 'curve', strictness: 'strict' },
    'curve loose + class filter kept in the full set': { difficultyMode: 'curve', curveInFullSet: true },
    'curve strict + class filter kept in the full set': { difficultyMode: 'curve', strictness: 'strict', curveInFullSet: true },
    'fit anywhere': { fit: 'anywhere' },
    '60% chance': { replacementChance: 60 },
    'prevent same + type replacement': { preventSame: true, typeReplacement: 'enabled' },
  };
  for (const [name, options] of Object.entries(variants)) {
    let rungs = [0, 0, 0, 0], errors = 0, placedCount = 0, notPart = 0, fitViol = 0, sameViol = 0, maxShare = 0, distinctPerMap = 0, time = 0, nondet = 0, blocked = 0;
    const offsets = {};
    const phases = {};
    for (let i = 0; i < seeds; i += 1) {
      const start = Date.now();
      const a = plan(inputs, { ...options, seed: `proto-${i}` });
      time += Date.now() - start;
      if (a.hash !== plan(inputs, { ...options, seed: `proto-${i}` }).hash) nondet += 1;
      rungs = rungs.map((n, k) => n + a.diagnostics.rungs[k]);
      errors += a.diagnostics.errors.length; notPart += a.diagnostics.notParticipating; blocked += a.diagnostics.keepVanillaBlocked; placedCount += a.placements.length;
      for (const p of a.placements) { // every placement is counted, whatever rung produced it
        if ((options.fit ?? 'fit') === 'fit' && p.size > p.slotSize) fitViol += 1;
        if (options.preventSame && p.from === p.to) sameViol += 1;
        if (options.difficultyMode === 'curve') { const d = p.difficulty - p.slotDifficulty; offsets[d] = (offsets[d] || 0) + 1; const k = p.phase === 'room' ? (p.emptiedClass ? 'room-class-empty' : 'room') : 'full'; phases[k] = phases[k] || [0, 0]; phases[k][d === 0 ? 0 : 1] += 1; }
      }
      const counts = new Map();
      for (const p of a.placements) counts.set(p.to, (counts.get(p.to) || 0) + 1);
      maxShare += Math.max(...counts.values()) / a.placements.length;
      const perMap = new Map();
      for (const p of a.placements) { if (!perMap.has(p.map)) perMap.set(p.map, new Set()); perMap.get(p.map).add(p.to); }
      distinctPerMap += [...perMap.values()].reduce((x, s) => x + s.size, 0) / perMap.size;
    }
    const pct = (n) => (100 * n / (seeds * inputs.slots.length)).toFixed(2) + '%';
    console.log(`${name}: placed/seed ${Math.round(placedCount / seeds)}, left out by chance/seed ${Math.round(notPart / seeds)}`
      + ` | rungs sampled/kept-vanilla/in-map-fits/in-map-relaxed ${rungs.join('/')} (kept-vanilla ${pct(rungs[1])}, relaxed ${pct(rungs[3])}, keep-vanilla blocked by budget ${blocked})`
      + ` | violations over ALL placements: fit ${fitViol} (${pct(fitViol)}), same ${sameViol} | errors ${errors}, nondeterministic ${nondet}`
      + ` | top-model-share ${(maxShare / seeds).toFixed(3)}, types/map ${(distinctPerMap / seeds).toFixed(1)}, ${Math.round(time / seeds)} ms/seed`
      + (Object.keys(offsets).length ? ` | difficulty offsets ${JSON.stringify(offsets)} | same-class share by phase ${JSON.stringify(Object.fromEntries(Object.entries(phases).map(([k, [z, n]]) => [k, `${(100 * z / (z + n)).toFixed(0)}% of ${z + n}`])))}` : ''));
  }
}
