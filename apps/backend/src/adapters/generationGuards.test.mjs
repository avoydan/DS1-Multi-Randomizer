import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../../dist/adapters/combinedRandomizerCore.js';

// Tripwires for the class of defect that docs/GENERATION_GUARD_AUDIT.md was written about: a conservative limit introduced to stabilise one feature
// (ROAMING_BOSS_MAP_CAP = 10) that silently became part of the product. Two cheap checks, no framework:
//   1. every numeric limit and bounded search of the engine is registered in the guard audit, so a new one cannot appear without somebody classifying it;
//   2. the planners never fall back to another category or relax Placement fit silently (the placeholders of the fallback ladder stay unused).

const loaded = await loadCombinedRandomizerCore();
assert.equal(loaded.ok, true, 'the vendored core loads');
const { generate, defaultConfig } = loaded.core;
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..');
const audit = readFileSync(path.join(repoRoot, 'docs', 'GENERATION_GUARD_AUDIT.md'), 'utf8').replace(/\r\n/gu, '\n');

// ---- 1. the registry covers every limit -------------------------------------------------------------------------------------------------------

/** Numeric constants that are data identifiers or definitions of a scale, not limits on what the generator may do (each with the reason). */
const NOT_LIMITS = new Map([
  ['HOLLOW_FROM', 'first piece id of the hollowed NPC armor variants (a data id, laundromat.js)'],
  ['DIFFICULTY_CLASS_COUNT', 'the number of difficulty classes: the definition of the scale (sampling.js)'],
  ['AREA_WEIGHT', 'weight of the map tier in the difficulty class formula (sampling.js)'],
  ['HP_WEIGHT', 'weight of the HP rank in the difficulty class formula (sampling.js)'],
]);

/**
 * Every limit as { entry: "file: key", count }. The registry names each entry as written ("generator.js: attempt < 128") and, when the same bound occurs several times in
 * one file, says how many ("generator.js: attempt < 32 (x2)"), so that one registered occurrence cannot cover a new one.
 */
function engineLimits() {
  const directory = path.dirname(resolveCombinedRandomizerCoreModulePath('core/generator.js'));
  const counts = new Map();
  const add = (file, key) => counts.set(`${file}: ${key}`, (counts.get(`${file}: ${key}`) ?? 0) + 1);
  for (const file of readdirSync(directory).filter((name) => name.endsWith('.js')).sort()) {
    const text = readFileSync(path.join(directory, file), 'utf8').replace(/\r\n/gu, '\n');
    for (const match of text.matchAll(/^(?:export )?const ([A-Z][A-Z0-9_]*) = (-?[\d_.]+(?:\s*[-+*/]\s*[\d_.A-Z]+)?);/gmu)) {
      if (!NOT_LIMITS.has(match[1])) add(file, match[1]);
    }
    // A bounded search: `for (let attempt = 0; attempt < 128; ...)` or `while (tries++ < 100)`.
    for (const match of text.matchAll(/for \(let (\w+) = 0; \1 < (\d+); \1 \+= 1\)/gu)) add(file, `${match[1]} < ${match[2]}`);
    for (const match of text.matchAll(/while \((\w+)\+\+ < (\d+)\)/gu)) add(file, `${match[1]}++ < ${match[2]}`);
  }
  return [...counts].map(([entry, count]) => (count > 1 ? `${entry} (x${count})` : entry));
}

test('every numeric limit and bounded search in the engine is registered in docs/GENERATION_GUARD_AUDIT.md', () => {
  const limits = engineLimits();
  assert.ok(limits.length >= 13, `the scan finds the known limits (found ${limits.length})`);
  const missing = limits.filter((entry) => !audit.includes(entry));
  assert.deepEqual(missing, [], 'A limit that is not in the guard registry. Classify it in docs/GENERATION_GUARD_AUDIT.md (what it counts, why it exists, what happens when it is reached, '
    + 'whether it is a real engine constraint) before it ships; if it is only a data id or a definition of a scale, add it to NOT_LIMITS in this test with the reason.');
  for (const [name, reason] of NOT_LIMITS) assert.ok(reason.length > 10, `${name} has a stated reason`);
});

test('the retry bounds of the backend are registered and still exist under those names', () => {
  const generationProfiles = readFileSync(path.join(repoRoot, 'apps', 'backend', 'src', 'core', 'generationProfiles.ts'), 'utf8');
  const enemyPlacement = readFileSync(path.join(repoRoot, 'apps', 'backend', 'src', 'core', 'enemyPlacement.ts'), 'utf8');
  for (const [source, name] of [[generationProfiles, 'maxCombinedAttempts'], [generationProfiles, 'maxFogAttempts'], [enemyPlacement, 'MAX_CUSTOM_POOL_ENTRIES']]) {
    assert.ok(source.includes(name), `${name} still exists`);
    assert.ok(audit.includes(name), `${name} is in the guard registry`);
  }
});

// ---- 2. no silent category or fit downgrade -------------------------------------------------------------------------------------------------

const MODELS = [['c1000', 100000], ['c1010', 101000], ['c1020', 102000], ['c1030', 103000], ['c1040', 104000], ['c1050', 105000]];
let counter = 0;
function slot(mapId, name, model, npc, extra = {}) {
  counter += 1;
  return {
    id: `${mapId}:${name}`, mapId, name, modelName: model, entityId: -1, npcParamId: npc, thinkParamId: npc, talkId: -1, charaInitId: npc,
    collisionName: 'h0000B0', position: { x: counter, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, movePoints: [], dummy: false, hasEntityId: false,
    eventModelLocked: false, riskFlags: [], safeCandidate: true, teamType: 0, npcType: 0, moveType: 0, hitHeight: 1.2, hitRadius: 0.5, hitYOffset: 0,
    baseHp: 100 + (npc % 7) * 10, soulReward: 10, battleStartDistance: 20, eyeDistance: 20, earDistance: 10, disablePathMove: false, battleGoalId: npc, ...extra,
  };
}
function catalog() {
  const enemySlots = [];
  for (const mapId of ['m10_00_00_00', 'm11_00_00_00', 'm14_00_00_00']) {
    for (let i = 0; i < 60; i += 1) {
      const [model, npc] = MODELS[i % MODELS.length];
      enemySlots.push(slot(mapId, `${model}_${String(i).padStart(4, '0')}`, model, npc));
    }
  }
  const bossRow = (mapId, model, npc, size) => slot(mapId, `${model}_0000`, model, npc, { entityId: 1000000 + npc, hitHeight: size, hitRadius: size / 3, baseHp: 3000, thinkParamId: npc + 1 });
  const bossSlots = [bossRow('m10_01_00_00', 'c2240', 224000, 2.8), bossRow('m10_01_00_00', 'c2250', 225000, 3.5), bossRow('m15_00_00_00', 'c2320', 232000, 6), bossRow('m13_00_00_00', 'c3320', 332000, 2)];
  const maps = [...new Set([...enemySlots, ...bossSlots].map((entry) => entry.mapId))];
  return { schemaVersion: 17, maps: maps.map((id) => ({ id, name: id })), enemySlots: [...enemySlots, ...bossSlots], enemyArchetypes: [{ id: 'x' }], bossSlots, worldItemLots: [], gifts: [], enemyDropLots: [], shopEntries: [] };
}
const data = catalog();
const run = (overrides) => generate({
  ...defaultConfig, seed: 'guard-tripwire', randomizeEnemies: true, randomizeBosses: true, randomizeItems: false, generateSpoilerLog: false, enemyPlacementAlgorithm: 'sampling-v1',
  placementDiagnosticsLevel: 'summary', enemyReplacementChance: 100, ...overrides,
}, { gameCatalog: data });

// Scope: the SAMPLERS (regular slots and roaming bosses). The unit planners (dragon plan, Mass of Souls) ignore the regular-enemy options by design and are the documented
// exception G-03 of docs/GENERATION_GUARD_AUDIT.md; their placements are not "sampled-*" kinds and are not looked at here.
test('the samplers never fall back to another category or relax Placement fit: the placeholders of the fallback ladder are never produced', () => {
  let worlds = 0;
  for (const normalReplacement of ['none', 'normals', 'bosses', 'both']) {
    for (const enemyPlacementFit of ['fits', 'anywhere']) {
      for (const enemyReplacementChance of [100, 60]) {
        for (const enemyBossReplacement of ['classic', 'bosses', 'normals', 'both']) {
          // The two chances are refused unless their mode is "both" (never ignored): 50 only there, the neutral 10 / 90 otherwise.
          const plan = run({
            enemyNormalReplacement: normalReplacement, enemyPlacementFit, enemyReplacementChance, enemyBossReplacement,
            enemyBossChanceNormalSlots: normalReplacement === 'both' ? 50 : 10, enemyBossChanceBossSlots: enemyBossReplacement === 'both' ? 50 : 90,
          });
          const summary = plan.placementDiagnostics.summary;
          const label = `${normalReplacement}/${enemyPlacementFit}/${enemyReplacementChance}/${enemyBossReplacement}`;
          assert.equal(summary.stages['category-fallback'], 0, `${label}: no category fallback stage`);
          assert.equal(summary.stages['fit-relaxed'], 0, `${label}: Placement fit is never relaxed`);
          assert.equal(summary.categoryDowngrades, 0, `${label}: nothing is silently downgraded to another category`);
          const kinds = new Set(plan.placements.enemies.map((entry) => entry.compatibility));
          const ordinary = [...kinds].filter((kind) => /^sampled-regular|^sampled-kept/u.test(kind));
          if (normalReplacement === 'bosses') assert.deepEqual(ordinary, [], `${label}: Bosses only never places an ordinary enemy through the sampler`);
          if (normalReplacement === 'normals') assert.ok(!kinds.has('sampled-roaming-boss'), `${label}: Regular enemies only never places a boss`);
          if (normalReplacement === 'none') assert.deepEqual([...kinds].filter((kind) => /^sampled-regular|^sampled-roaming/u.test(kind)), [], `${label}: Nothing places nothing in a regular spot`);
          worlds += 1;
        }
      }
    }
  }
  assert.equal(worlds, 64);
});
