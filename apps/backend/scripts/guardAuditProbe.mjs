// Engine-level measurements behind docs/GENERATION_GUARD_AUDIT.md, on a real extracted catalog (dsr-catalog.json), without the data tool or the game.
//
//   node apps/backend/scripts/guardAuditProbe.mjs <dsr-catalog.json> [--only units,hostile,budget,pool,modes] [--seeds 6]
//
// Read-only: nothing is written except a temporary copy of the vendored engine (with one constant changed) that the "hostile" probe deletes again.
// The numbers it prints are the ones quoted in the guard registry; none of them is an assertion, they are evidence for decisions that are the owner's.

import { cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { loadCombinedRandomizerCore, resolveCombinedRandomizerCoreModulePath } from '../dist/adapters/combinedRandomizerCore.js';

const [catalogPath, ...rest] = process.argv.slice(2);
if (!catalogPath) { console.error('Usage: node guardAuditProbe.mjs <dsr-catalog.json> [--only a,b] [--seeds N]'); process.exit(2); }
const option = (name, fallback) => { const i = rest.indexOf(name); return i >= 0 ? rest[i + 1] : fallback; };
const only = option('--only', '') ? option('--only', '').split(',') : null;
const seedCount = Number(option('--seeds', 6));
const wants = (name) => only === null || only.includes(name);

const loaded = await loadCombinedRandomizerCore();
if (!loaded.ok) throw new Error('the vendored core does not load');
const { generate, defaultConfig } = loaded.core;
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));

const baseConfig = {
  ...defaultConfig, randomizeEnemies: true, randomizeBosses: false, randomizeItems: false, generateSpoilerLog: false,
  enemyPlacementAlgorithm: 'sampling-v1', placementDiagnosticsLevel: 'summary',
};
const plan = (overrides, seed = 'guard-audit-1', core = { generate, defaultConfig }) => core.generate({ ...core.defaultConfig, ...baseConfig, ...overrides, seed }, { gameCatalog: catalog });
const kindsOf = (result) => result.placements.enemies.reduce((counts, entry) => ({ ...counts, [entry.compatibility]: (counts[entry.compatibility] ?? 0) + 1 }), {});
const heading = (text) => console.log(`\n== ${text}`);

// 1. The dragon-class units and the Mass of Souls group do not read the replacement chance or "Replace regular enemies with: Nothing".
if (wants('units')) {
  heading('units: placements of the unit planners (dragon plan, Mass of Souls) whatever the regular-enemy options say');
  const unitKinds = (kinds) => Object.entries(kinds).filter(([kind]) => /dragon|linked|hydra|mass-of-souls|parish-bonfire/u.test(kind));
  for (const [label, overrides] of [
    ['chance 0%, normals', { enemyReplacementChance: 0 }],
    ['chance 100%, Nothing', { enemyReplacementChance: 100, enemyNormalReplacement: 'none' }],
    ['chance 0%, boss randomization classic', { enemyReplacementChance: 0, randomizeBosses: true }],
  ]) {
    const kinds = kindsOf(plan(overrides));
    const unit = unitKinds(kinds);
    console.log(`${label.padEnd(40)} unit placements ${unit.reduce((sum, [, count]) => sum + count, 0)} (${unit.map(([kind, count]) => `${kind}:${count}`).join(' ')})`);
  }
}

// 2. HOSTILE_NPC_MAP_CAP: capped as shipped against a temporary copy of the engine with the cap lifted.
if (wants('hostile')) {
  heading('hostile: Hostile NPCs, capped (12 per map) against uncapped');
  const sourceDir = path.dirname(path.dirname(resolveCombinedRandomizerCoreModulePath('core/generator.js')));
  const scratch = mkdtempSync(path.join(tmpdir(), 'ds1tr-guard-probe-'));
  try {
    const copy = path.join(scratch, 'src');
    cpSync(sourceDir, copy, { recursive: true });
    const samplingPath = path.join(copy, 'core', 'sampling.js');
    const patched = readFileSync(samplingPath, 'utf8').replace('export const HOSTILE_NPC_MAP_CAP = 12;', 'export const HOSTILE_NPC_MAP_CAP = 1000000;');
    if (patched === readFileSync(samplingPath, 'utf8')) throw new Error('HOSTILE_NPC_MAP_CAP was not found: update this probe');
    writeFileSync(samplingPath, patched);
    const lifted = { generate: (await import(pathToFileURL(path.join(copy, 'core', 'generator.js')).href)).generate, defaultConfig: (await import(pathToFileURL(path.join(copy, 'core', 'config.js')).href)).defaultConfig };
    const measure = (result) => {
      const hostile = result.placements.enemies.filter((entry) => entry.hostileNpc === true);
      const perMap = new Map();
      for (const entry of hostile) perMap.set(entry.map, (perMap.get(entry.map) ?? 0) + 1);
      return { total: hostile.length, largest: Math.max(0, ...perMap.values()) };
    };
    for (const [label, overrides] of [
      ['fits, Random + Easy Asylum, 100%', { enemyPlacementFit: 'fits', enemyDifficultyMode: 'random-easy-asylum' }],
      ['anywhere, Random', { enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'random' }],
      ['fits, curve strict', { enemyPlacementFit: 'fits', enemyDifficultyMode: 'curve', enemyDifficultyStrictness: 'strict' }],
      ['anywhere, Type replacement enabled', { enemyPlacementFit: 'anywhere', enemyTypeReplacement: 'enabled' }],
    ]) {
      let capped = 0; let free = 0; let largestCapped = 0; let largestFree = 0;
      for (let seed = 1; seed <= seedCount; seed += 1) {
        const configuration = { enemyHostileNpcs: true, enemyReplacementChance: 100, ...overrides };
        const a = measure(plan(configuration, `hostile-${seed}`));
        const b = measure(plan(configuration, `hostile-${seed}`, lifted));
        capped += a.total; free += b.total; largestCapped = Math.max(largestCapped, a.largest); largestFree = Math.max(largestFree, b.largest);
      }
      console.log(`${label.padEnd(40)} mean per world capped ${(capped / seedCount).toFixed(1)}, uncapped ${(free / seedCount).toFixed(1)}; largest map capped ${largestCapped}, uncapped ${largestFree}`);
    }
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
}

// 3. The model budget counts every model a map holds after the run (fixed, kept and placed); the legacy counts only NEW models (30, Blighttown 50).
if (wants('budget')) {
  heading('budget: room for NEW models per map (cap minus the models that stay), DS1TR against the legacy MAX_UNIQUE');
  for (const [label, overrides] of [
    ['100%, fits, Random + Easy Asylum', { enemyReplacementChance: 100, enemyPlacementFit: 'fits', enemyDifficultyMode: 'random-easy-asylum', randomizeBosses: true }],
    ['100%, anywhere, Random', { enemyReplacementChance: 100, enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'random', randomizeBosses: true }],
    ['50%, fits, Random', { enemyReplacementChance: 50, enemyPlacementFit: 'fits', enemyDifficultyMode: 'random', randomizeBosses: true }],
  ]) {
    const result = plan(overrides);
    const summary = result.placementDiagnostics.summary;
    const maps = Object.entries(summary.maps);
    const room = maps.map(([, entry]) => Math.max(0, entry.effectiveCap - entry.baseline));
    const atCap = maps.filter(([, entry]) => entry.finalModels >= entry.effectiveCap).length;
    const blighttown = summary.maps.m14_00_00_00;
    console.log(`${label.padEnd(36)} maps at the cap ${atCap}/${maps.length}; mean room for new models ${(room.reduce((a, b) => a + b, 0) / room.length).toFixed(1)} (legacy 30); Blighttown ${blighttown ? Math.max(0, blighttown.effectiveCap - blighttown.baseline) : '?'} (legacy 50); candidates rejected by the budget ${summary.rejections['model-budget']}`);
  }
  const strict = plan({ enemyReplacementChance: 100, enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'curve', enemyDifficultyStrictness: 'strict' });
  console.log(`curve strict: difficulty lapses (a class widened because the budget removed every candidate) ${strict.placementDiagnostics.summary.counters.difficultyLapses ?? 0} of ${strict.placementDiagnostics.summary.slots} slots`);
}

// 4. The candidate pools: roaming bosses against the legacy boss pool.
if (wants('pool')) {
  heading('pool: roaming boss archetypes (DS1TR) and what the legacy pool has that DS1TR leaves out');
  const result = plan({ enemyNormalReplacement: 'bosses', enemyReplacementChance: 100, enemyPlacementFit: 'anywhere' });
  console.log(`roaming candidates ${result.placementDiagnostics.summary.counters.roamingCandidates}; roaming bosses ${result.placementDiagnostics.summary.counters.roamingBosses}`);
  console.log('legacy (valid_new.txt, type 1, not ignored) but never roaming in DS1TR: Crossbreed Priscilla c2730, Sanctuary Guardian c3471, Black Dragon Kalameet c4510, Centipede Demon c5200, Gaping Dragon c5260, Seath c5290, Bell Gargoyles c5350, Lightning Gargoyles c5351, Four Kings c5390, Gwyn c5370');
}

// 5. What the difficulty and fit labels do to Bosses only (counted reasons, not silent).
if (wants('modes')) {
  heading('modes: Bosses only at 100% under the difficulty and fit options (destinations that stay, by counted reason)');
  const reasons = (result) => {
    const total = { eligible: 0, boss: 0 };
    const unchanged = {};
    for (const entry of Object.values(result.placementDiagnostics.summary.roaming)) {
      total.eligible += entry.eligible; total.boss += entry.boss;
      for (const [reason, count] of Object.entries(entry.unchanged)) if (count > 0) unchanged[reason] = (unchanged[reason] ?? 0) + count;
    }
    return `${total.boss} of ${total.eligible} take a boss; unchanged ${JSON.stringify(unchanged)}`;
  };
  for (const [label, overrides] of [
    ['anywhere, Random', { enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'random' }],
    ['anywhere, curve strict', { enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'curve', enemyDifficultyStrictness: 'strict' }],
    ['anywhere, curve very loose', { enemyPlacementFit: 'anywhere', enemyDifficultyMode: 'curve', enemyDifficultyStrictness: 'very-loose' }],
    ['only where they fit, Random', { enemyPlacementFit: 'fits', enemyDifficultyMode: 'random' }],
  ]) {
    console.log(`${label.padEnd(30)} ${reasons(plan({ enemyNormalReplacement: 'bosses', enemyReplacementChance: 100, ...overrides }))}`);
  }
}
