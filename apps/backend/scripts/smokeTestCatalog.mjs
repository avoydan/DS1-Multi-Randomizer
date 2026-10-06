// Describes (and helps choose) the runtime smoke-test worlds of packages/contracts/src/smokeTests.ts.
//
//   node apps/backend/scripts/smokeTestCatalog.mjs describe [id,id]          generate each scenario's world and print what a tester will meet
//   node apps/backend/scripts/smokeTestCatalog.mjs search <id> [--from A --to B]   try master seeds `<prefix>-A..B` and print the measurements
//
// Needs a clean game copy and the built helpers like goldenCompatibility.mjs (DS1TR_GOLDEN_GAME_SOURCE, DS1TR_DATA_TOOL_EXE,
// DS1TR_FOG_HOST, DS1TR_STATE_DIR). Nothing here proves a world plays correctly; it only reports what the generated files contain.

import { cp, mkdtemp, mkdir, readFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { SMOKE_SCENARIOS, SMOKE_TEST_PROFILE, SMOKE_TEST_CHECKLIST, resolveScenarioSettings } from '@ds1tr/contracts';
import { writeFile } from 'node:fs/promises';
import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { encodeSeedCode } from '../dist/core/seedCode.js';
import { canonicalizeSettings } from '../dist/core/canonicalSettings.js';

const [mode, argument, ...rest] = process.argv.slice(2);
const source = process.env.DS1TR_GOLDEN_GAME_SOURCE;
if (!['describe', 'search', 'docs'].includes(mode) || !source) {
  console.error('Usage: DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> node smokeTestCatalog.mjs describe|search <id> [--from A --to B]');
  process.exit(2);
}
const option = (name, fallback) => { const index = rest.indexOf(name); return index >= 0 ? Number(rest[index + 1]) : fallback; };

const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-smoke-'));
try {
  const game = path.join(temp, 'game');
  await cp(path.join(source, executableName), path.join(game, executableName));
  for (const managed of managedRoots) await cp(path.join(source, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
  const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
  if (!(await authority.importPristine(game)).ok) throw new Error('pristine import failed');

  const measure = async (scenario, masterSeed) => {
    const settings = canonicalizeSettings(resolveScenarioSettings(scenario));
    const seedCode = encodeSeedCode({ profile: SMOKE_TEST_PROFILE, masterSeed, settings });
    const prepared = prepareRun({ gamePath: game, seedCode });
    if (!prepared.ok) throw new Error(`${scenario.id}: ${prepared.code}: ${prepared.technical}`);
    const workspacePath = path.join(temp, `run-${scenario.id}-${masterSeed}`);
    await mkdir(workspacePath, { recursive: true });
    const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode }, () => {},
      { getPristineSource: (p) => authority.getPristineSource(p) });
    if (!result.ok) { await rm(workspacePath, { recursive: true, force: true }); return { masterSeed, seedCode, ok: false, error: `${result.code}: ${result.message}` }; }
    // A combined-only package holds randomizer.json; for a world randomizer run it is in the last combined attempt of the run folder.
    let planFile = path.join(result.packagePath, 'randomizer.json');
    try { await readFile(planFile); } catch {
      const { readdir } = await import('node:fs/promises');
      const attempts = (await readdir(workspacePath)).filter((name) => /^combined-\d+$/u.test(name)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
      const seedDirectory = (await readdir(path.join(workspacePath, attempts[0]))).find((name) => name.startsWith('seed_'));
      planFile = path.join(workspacePath, attempts[0], seedDirectory, 'randomizer.json');
    }
    const plan = JSON.parse(await readFile(planFile, 'utf8'));
    const bosses = (plan.placements.bosses ?? []).filter((p) => p.changed !== false);
    const nameOf = new Map((plan.placements.bosses ?? []).map((p) => [p.targetModelName, p.randomizedBossName]));
    const secondGargoyle = plan.placements.enemies.find((p) => p.slot === 'm10_01_00_00:c5350_0001' && p.changed);
    const maps = result.manifest.enemyPlacement.summary?.maps ?? {};
    const models = bosses.map((p) => p.randomizedBossName ?? p.targetModelName);
    const counts = new Map();
    for (const name of models) counts.set(name, (counts.get(name) ?? 0) + 1);
    const out = {
      masterSeed, seedCode, ok: true,
      bossTable: bosses.map((p) => `${p.encounterLocation?.area ?? p.map}: ${p.originalBossName} -> ${p.randomizedBossName}`),
      bossSlotsChanged: bosses.length,
      repeated: [...counts].filter(([, n]) => n > 1).map(([name, n]) => `${name} x${n}`),
      secondGargoyle: secondGargoyle ? `${nameOf.get(secondGargoyle.targetModelName) ?? secondGargoyle.targetModelName} (${secondGargoyle.targetModelName})` : null,
      parishModels: maps['m10_01_00_00']?.finalModels ?? null,
      maxModels: Math.max(0, ...Object.values(maps).map((map) => map.finalModels ?? 0)),
      mapModels: Object.fromEntries(Object.entries(maps).map(([id, map]) => [id, map.finalModels])),
    };
    await rm(workspacePath, { recursive: true, force: true });
    return out;
  };

  if (mode === 'describe') {
    const wanted = argument ? argument.split(',') : SMOKE_SCENARIOS.map((s) => s.id);
    for (const scenario of SMOKE_SCENARIOS.filter((s) => wanted.includes(s.id))) {
      const result = await measure(scenario, scenario.masterSeed);
      console.log(`## ${scenario.id} (${scenario.masterSeed})`);
      console.log(JSON.stringify(result, null, 1));
    }
  } else if (mode === 'docs') {
    const names = { 'm10_01_00_00': 'Undead Burg / Undead Parish' };
    const lines = [
      '# Runtime smoke tests', '',
      '> Generated by `node apps/backend/scripts/smokeTestCatalog.mjs docs <file>`. The Seed Codes below are pinned to generation profile `ds1tr-gen-v1`',
      '> and never change. **Nothing here is verified in the game**: these worlds exist so a person can play them and report what they see.', '',
      'In the app: Advanced, then "Test worlds for playtesting", or paste a code into **Load seed**. Play offline.', '',
      '## What to check in every world', '', ...SMOKE_TEST_CHECKLIST.map((line) => `- [ ] ${line}`), '',
    ];
    for (const scenario of SMOKE_SCENARIOS) {
      const result = await measure(scenario, scenario.masterSeed);
      if (!result.ok) throw new Error(`${scenario.id}: ${result.error}`);
      lines.push(`## ${scenario.title}`, '', scenario.purpose, '', '```', result.seedCode, '```', '', '**In this world**', '', ...scenario.observe.map((line) => `- ${line}`));
      if (result.bossSlotsChanged > 0 && scenario.settings.enemies?.placement?.bossReplacement === 'bosses') {
        const sampled = result.bossTable.slice(0, 24);
        lines.push('', `Boss replacements of this world (${sampled.length} boss slots; the first ${sampled.length} lines are the sampled ones):`, '', ...sampled.map((line) => `- ${line}`));
        lines.push('', `Bosses that appear more than once: ${result.repeated.length ? result.repeated.join(', ') : 'none'}.`);
        const vanilla = 24 - sampled.filter((line) => { const [, rest] = line.split(': '); const [from, to] = rest.split(' -> '); return from !== to; }).length;
        lines.push(`Boss slots that keep their own boss: ${scenario.settings.enemies.placement.replacementChance === 50 ? 'about half (see the list: a slot is missing when it was left alone)' : 'none'}.`);
        void vanilla;
      }
      if (result.secondGargoyle) {
        const first = result.bossTable.find((line) => line.includes(': Bell Gargoyle -> '));
        lines.push('', `Expected: the second Bell Gargoyle of the Undead Parish fight becomes **${result.secondGargoyle}**${first ? `; the first Gargoyle body becomes ${first.split(' -> ')[1]}` : ''}. It is released when the first body drops to about 60% health, on the rooftop.`);
      }
      if (scenario.id === 'parish-models') lines.push('', `Undead Parish holds ${result.parishModels} different enemy models in this world (the limit is 30). Note: the Parish reaches this count in essentially every sampled seed; it is the map with the most enemy types, so this is the normal worst case, not a rare one.`);
      lines.push('');
    }
    await writeFile(argument, `${lines.join('\n')}\n`);
    console.log(`wrote ${argument}`);
  } else {
    const scenario = SMOKE_SCENARIOS.find((s) => s.id === argument);
    if (!scenario) throw new Error(`unknown scenario ${argument}`);
    const prefix = scenario.masterSeed.replace(/-\d+$/u, '');
    for (let n = option('--from', 1); n <= option('--to', 20); n += 1) {
      const result = await measure(scenario, `${prefix}-${n}`);
      console.log(JSON.stringify({ seed: result.masterSeed, ok: result.ok, bossSlotsChanged: result.bossSlotsChanged, repeated: result.repeated, secondGargoyle: result.secondGargoyle, parishModels: result.parishModels, maxModels: result.maxModels, error: result.error }));
    }
  }
} finally {
  await rm(temp, { recursive: true, force: true });
}
