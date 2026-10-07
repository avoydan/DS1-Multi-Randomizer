// Describes (and helps choose) the runtime smoke-test worlds of packages/contracts/src/smokeTests.ts.
//
//   node apps/backend/scripts/smokeTestCatalog.mjs describe [id,id]          generate each scenario's world and print what a tester will meet
//   node apps/backend/scripts/smokeTestCatalog.mjs verify [id,id]            static check of the QUICK worlds: generate each one twice (identical package) and read the package for what it promises
//   node apps/backend/scripts/smokeTestCatalog.mjs search <id> [--from A --to B]   try master seeds `<prefix>-A..B` and print the measurements
//
// Needs a clean game copy and the built helpers like goldenCompatibility.mjs (DS1TR_GOLDEN_GAME_SOURCE, DS1TR_DATA_TOOL_EXE,
// DS1TR_FOG_HOST, DS1TR_STATE_DIR). Nothing here proves a world plays correctly; it only reports what the generated files contain.

import { cp, mkdtemp, mkdir, readFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { SMOKE_SCENARIOS, SMOKE_START, SMOKE_TEST_PROFILE, SMOKE_TEST_CHECKLIST, describeSmokeExpected, describeSmokeTarget, resolveScenarioSettings } from '@ds1tr/contracts';
import { writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { packageDigest } from './goldenCompatibility.mjs';
import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { encodeSeedCode } from '../dist/core/seedCode.js';
import { canonicalizeSettings } from '../dist/core/canonicalSettings.js';

const [mode, argument, ...rest] = process.argv.slice(2);
const source = process.env.DS1TR_GOLDEN_GAME_SOURCE;
if (!['describe', 'search', 'docs', 'verify'].includes(mode) || !source) {
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

  let runCounter = 0;
  const measure = async (scenario, masterSeed, keep = false) => {
    const settings = canonicalizeSettings(resolveScenarioSettings(scenario));
    const seedCode = encodeSeedCode({ profile: SMOKE_TEST_PROFILE, masterSeed, settings });
    const prepared = prepareRun({ gamePath: game, seedCode });
    if (!prepared.ok) throw new Error(`${scenario.id}: ${prepared.code}: ${prepared.technical}`);
    runCounter += 1;
    const workspacePath = path.join(temp, `run-${runCounter}-${scenario.id}`);
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
    // Bosses standing in regular enemy slots (Part E1): where, which boss, what it replaced.
    const BOSS_NAME = { c2230: 'Stray Demon', c2231: 'Demon Firesage', c2232: 'Asylum Demon', c2240: 'Capra Demon', c2250: 'Taurus Demon', c2320: 'Iron Golem', c2360: 'Smough', c3230: 'Moonlight Butterfly', c3320: 'Pinwheel', c4100: 'Artorias', c4500: 'Manus', c5210: 'Sif', c5220: 'Gravelord Nito', c5270: 'Ornstein', c5271: 'Super Ornstein', c5280: 'Quelaag' };
    const AREA = { m10_00_00_00: 'The Depths', m10_01_00_00: 'Undead Burg / Parish', m10_02_00_00: 'Lower Undead Burg', m11_00_00_00: 'Painted World', m12_00_00_00: 'Darkroot Garden', m12_00_00_01: 'Darkroot Basin', m12_01_00_00: 'Oolacile', m13_00_00_00: 'Catacombs', m13_01_00_00: 'Tomb of the Giants', m13_02_00_00: 'Ash Lake', m14_00_00_00: 'Blighttown', m14_01_00_00: 'Demon Ruins', m15_00_00_00: "Sen's Fortress", m15_01_00_00: 'Anor Londo', m16_00_00_00: 'New Londo Ruins', m17_00_00_00: "Duke's Archives", m18_00_00_00: 'The Kiln', m18_01_00_00: 'Undead Asylum' };
    // Hostile NPCs (Part F1): the live (not dummy) placements; the real NPCs are never destinations.
    const NPC_NAME = { c2510: 'Undead Merchant', c2640: 'Andre of Astora', c2920: 'Vamos' };
    // A live placement has its wake-up (or is passive until attacked); a dummy part is not spawned in the game and has neither.
    const hostileRows = plan.placements.enemies.filter((p) => p.hostileNpc === true && (p.forceCombatActivation === true || p.passiveUntilAttacked === true));
    const hostileCounts = new Map();
    for (const p of hostileRows) hostileCounts.set(NPC_NAME[p.targetModelName], (hostileCounts.get(NPC_NAME[p.targetModelName]) ?? 0) + 1);
    const roamingRows = plan.placements.enemies.filter((p) => p.compatibility === 'sampled-roaming-boss');
    const roamingNames = roamingRows.map((p) => BOSS_NAME[p.targetModelName] ?? p.targetModelName);
    const roamingCounts = new Map();
    for (const name of roamingNames) roamingCounts.set(name, (roamingCounts.get(name) ?? 0) + 1);
    const out = {
      masterSeed, seedCode, ok: true, ...(keep ? { packagePath: result.packagePath, manifest: result.manifest, workspacePath } : {}),
      hostileCount: hostileRows.length,
      hostileKinds: [...hostileCounts].map(([name, n]) => `${name} x${n}`),
      hostileTable: hostileRows.map((p) => `${AREA[p.map] ?? p.map}: ${p.from.split(' [')[0]} (slot ${p.slot.split(':')[1]}) -> ${NPC_NAME[p.targetModelName] ?? p.targetModelName}${p.passiveUntilAttacked ? ' (passive until attacked)' : ''}`),
      roamingCount: roamingRows.length,
      roamingTable: roamingRows.map((p) => `${AREA[p.map] ?? p.map}: a boss replaces enemy ${p.from.split(' [')[0]} (slot ${p.slot.split(':')[1]}): ${BOSS_NAME[p.targetModelName] ?? p.targetModelName}, souls ${p.roamingBossSouls}${p.disableRespawn ? ', stays dead' : ''}`),
      roamingKinds: [...roamingCounts].map(([name, n]) => `${name} x${n}`),
      bossTable: bosses.map((p) => `${p.encounterLocation?.area ?? p.map}: ${p.originalBossName} -> ${p.randomizedBossName}`),
      bossSlotsChanged: bosses.length,
      repeated: [...counts].filter(([, n]) => n > 1).map(([name, n]) => `${name} x${n}`),
      secondGargoyle: secondGargoyle ? `${nameOf.get(secondGargoyle.targetModelName) ?? secondGargoyle.targetModelName} (${secondGargoyle.targetModelName})` : null,
      parishModels: maps['m10_01_00_00']?.finalModels ?? null,
      maxModels: Math.max(0, ...Object.values(maps).map((map) => map.finalModels ?? 0)),
      mapModels: Object.fromEntries(Object.entries(maps).map(([id, map]) => [id, map.finalModels])),
    };
    if (!keep) await rm(workspacePath, { recursive: true, force: true });
    return out;
  };

  if (mode === 'describe') {
    const wanted = argument ? argument.split(',') : SMOKE_SCENARIOS.map((s) => s.id);
    for (const scenario of SMOKE_SCENARIOS.filter((s) => wanted.includes(s.id))) {
      const result = await measure(scenario, scenario.masterSeed);
      console.log(`## ${scenario.id} (${scenario.masterSeed})`);
      console.log(JSON.stringify(result, null, 1));
    }
  } else if (mode === 'verify') {
    // Static check: the generated FILES contain what a quick world promises. This proves the target is present, not that it plays.
    const exe = process.env.DS1TR_SMOKE_VERIFY_EXE ?? fileURLToPath(new URL('../../../tools/smoke-verify/bin/Release/net8.0/SmokeVerify.exe', import.meta.url));
    const paramdef = path.join(source, 'paramdef', 'paramdef.paramdefbnd.dcx');
    const wanted = argument ? argument.split(',') : null;
    let failed = 0;
    for (const scenario of SMOKE_SCENARIOS.filter((entry) => entry.category === 'quick' && (!wanted || wanted.includes(entry.id)))) {
      const first = await measure(scenario, scenario.masterSeed, true);
      if (!first.ok) { failed += 1; console.log(`FAIL ${scenario.id}: ${first.error}`); continue; }
      const digestOf = async (run) => (await packageDigest(run.manifest.files, run.packagePath)).digest;
      const digest = await digestOf(first);
      const second = await measure(scenario, scenario.masterSeed, true);
      const deterministic = second.ok && (await digestOf(second)) === digest;
      const expectations = scenario.expectations.map((entry) => ({
        id: `${scenario.id}/${entry.slot}`, map: entry.map, slot: entry.slot, model: entry.model, kind: entry.kind,
        souls: entry.souls, disableRespawn: entry.disableRespawn, wakeUpEvent: entry.wakeUpEvent,
      }));
      const expectationsFile = path.join(temp, `expect-${scenario.id}.json`);
      await writeFile(expectationsFile, JSON.stringify(expectations));
      const run = spawnSync(exe, [path.join(first.packagePath, 'mod'), paramdef, expectationsFile, game], { encoding: 'utf8' });
      let results = [];
      try { results = JSON.parse(run.stdout.trim().split('\n').pop()); } catch { /* reported below */ }
      // Event owner audit: every placement of the world that needs a custom event has one in the event file of its owner map.
      const algorithm = scenario.settings?.enemies?.placement?.algorithm === 'sampling-v1' ? 'sampling' : 'classic';
      const auditRun = spawnSync(exe, ['audit', path.join(first.packagePath, 'mod'), path.join(first.packagePath, 'randomizer.json'), source, algorithm], { encoding: 'utf8' });
      let audit = { checkedCount: 0, missing: ['the audit did not run'], duplicateOwners: [] };
      try { audit = JSON.parse(auditRun.stdout.trim().split(/\r?\n/u).pop()); } catch { /* reported below */ }
      results.push({ id: `${scenario.id}/event-owners`, ok: audit.missing.length === 0 && audit.duplicateOwners.length === 0, checks: [{ name: `event owners (${audit.checkedCount} placements checked)`, ok: audit.missing.length === 0 && audit.duplicateOwners.length === 0, detail: [...audit.missing.slice(0, 3), ...audit.duplicateOwners].join(' | ') || 'every placement that needs an event has one' }] });
      const bad = results.flatMap((result) => result.checks.filter((check) => !check.ok).map((check) => `${result.id}: ${check.name}: ${check.detail}`));
      const ok = run.status === 0 && results.length === expectations.length + 1 && bad.length === 0 && deterministic;
      if (!ok) failed += 1;
      console.log(`${ok ? 'PASS' : 'FAIL'} ${scenario.id} (${first.seedCode.slice(0, 20)}...): ${results.length - 1}/${expectations.length} expectations read from the package plus the event owner audit, ${results.reduce((n, r) => n + r.checks.length, 0)} checks, package ${deterministic ? 'identical on a second run' : 'DIFFERENT on a second run'} (${digest.slice(0, 12)})${run.status !== 0 && results.length === 0 ? ` - verifier: ${String(run.stderr).slice(0, 400)}` : ''}`);
      for (const line of bad) console.log(`   ${line}`);
      await rm(first.workspacePath, { recursive: true, force: true });
      if (second.workspacePath) await rm(second.workspacePath, { recursive: true, force: true });
    }
    console.log(failed === 0 ? 'RESULT: PASS (static only: the files contain what the worlds promise; nothing was played)' : `RESULT: FAIL (${failed})`);
    process.exitCode = failed === 0 ? 0 : 1;
  } else if (mode === 'docs') {
    const lines = [
      '# Runtime smoke tests', '',
      '> Generated by `node apps/backend/scripts/smokeTestCatalog.mjs docs <file>`. The Seed Codes below are pinned to generation profile `ds1tr-gen-v1`',
      '> and never change. **Nothing here is verified in the game**: these worlds exist so a person can play them and report what they see.', '',
      'In the app: Advanced, then "Test worlds for playtesting", or paste a code into **Load seed**. Play offline.', '',
      'There are two kinds of world.', '',
      '- **Quick smoke tests** answer one question - "does this feature work at runtime?" - within about a minute of gaining control. Each one tells you exactly where to look',
      '  and what you must see there. They are ordinary worlds (normal settings, normal master seed, normal Seed Code) whose master seed was *chosen* so that the target stands next to',
      '  where you wake up. Nothing is forced and nothing outside normal generation is involved.',
      '- **Extended gameplay tests** need travel or progression, or exercise a whole system (the fog randomizer, the Bell Gargoyle event, a map full of bosses).', '',
      '## Quick smoke tests', '',
      '| Test | What it shows | Where | Time to the target |', '|---|---|---|---|',
    ];
    const gameplayLine = (scenario) => (scenario.gameplay ? `**Gameplay result:** ${scenario.gameplay.result === 'passed' ? 'passed' : 'partly passed'} in the real game on ${scenario.gameplay.date} (played by hand). ${scenario.gameplay.note}` : '**Gameplay result:** not played in the real game yet; only the static check exists.');
    const quick = SMOKE_SCENARIOS.filter((entry) => entry.category === 'quick');
    const extended = SMOKE_SCENARIOS.filter((entry) => entry.category === 'extended');
    for (const scenario of quick) lines.push(`| \`${scenario.id}\` | ${scenario.title} | ${scenario.expectations.map((e) => `${e.slot} (${e.meters} m)`).join(', ')} | ~${scenario.estimateSeconds} s |`);
    lines.push('', '### How the targets were chosen', '',
      `All quick worlds are in the **${SMOKE_START.mapName}**, first visit. You wake up at x ${SMOKE_START.x}, y ${SMOKE_START.y}, z ${SMOKE_START.z} (the player start of \`m18_01_00_00.msb\`); every distance below is the straight-line distance on the ground from that point, and "same floor" means within 2 m in height.`,
      'The Asylum is visited twice and the map holds both sets of enemies: event `11810350` keeps the enemies with entity ids 1810200-1810213 disabled until the second visit and disables 1810101-1810113 after it. Every target here that is a regular enemy has an entity id from 1810101 to 1810113, so it exists the first time.',
      'The Asylum has **no bonfire** (the map data holds none), so there is nowhere to rest. To make the area reset, die once.',
      '**Estimated time** = about 20 seconds to take what you need from where you wake up and leave the cell, plus the distance times a detour factor of 1.6 at about 4 m/s. It is an estimate from the map data, not a time measured in the game.',
      'The master seeds were found with `apps/backend/scripts/smokeWorldSearch.mjs` (engine only, on a real catalog) and are pinned in `packages/contracts/src/smokeTests.ts`. The rest of the world is randomized as usual; ignore it.',
      '`node apps/backend/scripts/smokeTestCatalog.mjs verify` generates every quick world twice through the normal pipeline and reads the package for what is promised here (model of the part, parameter rows, souls, respawn bit, wake-up event, unique event ids, model count). That proves the files contain the target; it does not prove the game plays it.', '');
    for (const scenario of quick) {
      const result = await measure(scenario, scenario.masterSeed);
      if (!result.ok) throw new Error(`${scenario.id}: ${result.error}`);
      lines.push(`### ${scenario.title}`, '', `**Quick smoke test** - ID \`${scenario.id}\``, '', scenario.purpose, '',
        '**Target**', '', ...scenario.expectations.map((entry) => `- ${describeSmokeTarget(entry)}`), '',
        '**Expected**', '', ...scenario.expectations.map((entry) => `- ${describeSmokeExpected(entry)}`), '',
        `**Estimated time to the target:** ~${scenario.estimateSeconds} seconds after you gain control.`, '',
        `**Needs a reset or reload:** ${scenario.afterwards?.some((line) => /die once/u.test(line)) ? 'yes - die once' : 'no'}`, '',
        gameplayLine(scenario), '',
        '**Check**', '', ...scenario.observe.map((line) => `- [ ] ${line}`), '');
      if (scenario.afterwards?.length) lines.push('**Afterwards**', '', ...scenario.afterwards.map((line) => `- [ ] ${line}`), '');
      lines.push('```', result.seedCode, '```', '');
    }
    lines.push('## Extended gameplay tests', '', 'These need travel or progression, or exercise a whole system. They are not smoke tests: expect to play for a while to reach what they show.', '',
      '### What to check in every extended world', '', ...SMOKE_TEST_CHECKLIST.map((line) => `- [ ] ${line}`), '');
    for (const scenario of extended) {
      const result = await measure(scenario, scenario.masterSeed);
      if (!result.ok) throw new Error(`${scenario.id}: ${result.error}`);
      lines.push(`### ${scenario.title}`, '', `**Extended gameplay test** - ID \`${scenario.id}\``, '', scenario.purpose, '', gameplayLine(scenario), '', '```', result.seedCode, '```', '', '**In this world**', '', ...scenario.observe.map((line) => `- ${line}`));
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
      if (result.hostileCount > 0) {
        const firstNpc = result.hostileTable.slice(0, 14);
        lines.push('', `Hostile NPCs in this world: ${result.hostileCount} (${result.hostileKinds.join(', ')}). The first ${firstNpc.length} by map order:`, '', ...firstNpc.map((line) => `- ${line}`));
      }
      if (result.roamingCount > 0) {
        const first = result.roamingTable.slice(0, 14);
        lines.push('', `Bosses in regular enemy slots in this world: ${result.roamingCount} (${result.roamingKinds.join(', ')}). The first ${first.length} by map order:`, '', ...first.map((line) => `- ${line}`));
        lines.push('', `Different enemy models in the Undead Parish: ${result.parishModels} (the limit is 30; a map that bosses and dragons already push above it never gets more).`);
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
      console.log(JSON.stringify({ seed: result.masterSeed, ok: result.ok, roamingCount: result.roamingCount, roamingKinds: result.roamingKinds, bossSlotsChanged: result.bossSlotsChanged, repeated: result.repeated, secondGargoyle: result.secondGargoyle, parishModels: result.parishModels, maxModels: result.maxModels, error: result.error }));
    }
  }
} finally {
  await rm(temp, { recursive: true, force: true });
}
