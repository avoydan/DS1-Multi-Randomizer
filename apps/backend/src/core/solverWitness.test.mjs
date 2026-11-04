import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { cp, mkdir, mkdtemp, readdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { isSolverWitnessDocument, renderSolverRoute, renderSolverSteps, renderSolverTree } from '@ds1tr/contracts';
import { GameStateAuthority, executableName, managedRoots } from '../../dist/core/gameState.js';
import { generateRun } from '../../dist/core/generationPipeline.js';
import { prepareRun } from '../../dist/core/seededRun.js';
import { BUNDLE_ALLOWLIST, buildSupportBundle } from '../../dist/core/supportBundle.js';
import { fogAttemptDirectory, locateWitnessFile, persistSolverWitness, readWitnessDocument, solverRouteAvailableFor, solverRouteForEntry, witnessContext } from '../../dist/core/solverWitnessStore.js';

// The solver witness (docs/SOLVER_WITNESS.md): what FogHost records inside the accepting search, how the backend keeps it next to the run (never in the
// package) and how it is rendered for a person. The graph itself is tested in tools/fog-host/tests/SolverWitnessTest.cs; these tests cover everything after it.
const smokeCode = 'DS1TR1-06NWCQQ3-K2GXEE39-K5WRJEDT-CNKBNHDS-Z7CTKEJ6-0S36D1M6-0S3A7NEF-D3TK6PQX-HHB3ZGX2-Z80G12X8-GYBG';
const gameSource = process.env.DS1TR_GOLDEN_GAME_SOURCE;

const gate = (area, object, text, extra = {}) => ({ area, object, text, tags: [], sideCondition: null, otherSideArea: null, areaIsBossArena: false, bossTriggerSide: false, ...extra });
const edge = (over) => ({
  kind: 'fixed-route', label: 'fixed route (in-map)', randomized: false, pairing: null, glitched: false, oneWay: true, reverse: 'none', reverseCondition: null, reverseVia: null, qualifiers: ['in-map link'],
  exit: gate('a', null, 'in map'), arrival: gate('b', null, 'in map'), requires: null, requirements: [], satisfiedBy: [], ...over,
});
const item = { name: 'cage_key_goods_2003', gameName: 'Cage Key', gameItemId: '4:2003', defaultArea: 'sens_upper', solverArea: 'sens_upper', moved: false, lots: [1500150], tags: [] };

/** start -> randomized boss fog (two different objects, boss-encounter side) -> arena; acquire the key; the key opens the cage; a glitched link. */
function fixtureDocument() {
  const fog = edge({
    kind: 'randomized-fog', label: 'randomized boss fog', randomized: true, pairing: 'shuffled', oneWay: false, reverse: 'unconditional', reverseVia: 'o4950_0000', qualifiers: ['boss'],
    exit: gate('asylum', 'o8952_0000', 'asylum-oscar-cell-wall', { otherSideArea: 'asylum_oscar' }),
    arrival: gate('blighttown_quelaag', 'o4950_0000', 'blighttown-quelaag-boundary', { otherSideArea: 'blighttown_bell', bossTriggerSide: true, areaIsBossArena: true }),
  });
  const cage = edge({
    exit: gate('sens_mid', null, 'in map'), arrival: gate('sens_cage', null, 'in map'), requires: 'cage_key_goods_2003',
    requirements: [{ name: 'cage_key_goods_2003', kind: 'key-item', reachedInArea: 'sens_upper', satisfiedAtSequence: 3, item: 'cage_key_goods_2003' }], satisfiedBy: ['cage_key_goods_2003'],
  });
  const hard = edge({ kind: 'glitched-route', label: 'glitched-logic route', glitched: true, exit: gate('depths', null, 'hard skip'), arrival: gate('depths_gaping', null, 'hard skip') });
  const door = edge({
    exit: gate('blighttown_quelaag', 'o4950_0000', 'blighttown-quelaag-boundary', { areaIsBossArena: true, bossTriggerSide: false, otherSideArea: 'blighttown_bell' }),
    arrival: gate('sens_mid', 'o5150_0000', 'sens-roof-wall'), kind: 'randomized-fog', label: 'randomized boss fog', randomized: true, pairing: 'shuffled', oneWay: false, reverse: 'conditional', reverseCondition: 'cage_key_goods_2003', reverseVia: 'o5150_0000', qualifiers: ['boss'],
    requires: 'blighttown_quelaag', requirements: [{ name: 'blighttown_quelaag', kind: 'area', reachedInArea: 'blighttown_quelaag', satisfiedAtSequence: 1, item: null }], satisfiedBy: ['blighttown_quelaag'],
  });
  const mainRoute = [
    { number: 1, type: 'start', area: 'asylum', sequence: 0, returnTo: null, edge: null, item: null, completion: null, notes: ['The player starts here.'] },
    { number: 2, type: 'cross', area: 'blighttown_quelaag', sequence: 1, returnTo: null, edge: fog, item: null, completion: null, notes: [] },
    { number: 3, type: 'acquire', area: 'sens_upper', sequence: 3, returnTo: null, edge: null, item, completion: null, notes: [] },
    { number: 4, type: 'cross', area: 'sens_mid', sequence: 2, returnTo: 'blighttown_quelaag', edge: door, item: null, completion: null, notes: [] },
    { number: 5, type: 'cross', area: 'sens_cage', sequence: 4, returnTo: 'sens_mid', edge: cage, item: null, completion: null, notes: [] },
    { number: 6, type: 'cross', area: 'depths_gaping', sequence: 5, returnTo: null, edge: hard, item: null, completion: null, notes: ['GLITCHED LOGIC REQUIRED'] },
  ];
  return {
    format: 'ds1tr-solver-witness', schemaVersion: 1, hostVersion: '0.7.3', annotationSource: 'compact-v3',
    claim: { statement: 'Every non-optional Area is reachable.', startArea: 'asylum', startName: 'Asylum', areasTotal: 6, areasReached: 6, optionalAreasNotRequired: [], unreached: [], fogOptions: ['boss', 'world'], glitchedLogicEnabled: true },
    assumptions: ['Every Area the solver has already reached can be entered again.'],
    target: { area: 'depths_gaping', reason: 'fixture' }, keyItems: [item],
    reachTree: [
      { sequence: 0, pass: 0, area: 'asylum', areaTags: [], fromArea: null, edge: null, keyItemsGained: [] },
      { sequence: 1, pass: 1, area: 'blighttown_quelaag', areaTags: ['boss'], fromArea: 'asylum', edge: fog, keyItemsGained: [] },
    ],
    bossCompletions: [], mainRoute,
    context: { seedCode: 'DS1TR1-TEST', generationProfile: 'ds1tr-gen-v6', appVersion: '1.2.3', masterSeed: 'm', fogSeed: 99, combinedSeed: 'c', combinedAttempt: 0, fogAttempt: 2, attempts: [] },
  };
}

test('rendering: both sides, their objects and descriptions, the boss-encounter side, direction and pairing are all in the text', () => {
  const text = renderSolverSteps(fixtureDocument()).join('\n\n');
  assert.match(text, /2\. RANDOMIZED BOSS FOG/u);
  assert.match(text, /FROM SIDE: asylum — object o8952_0000 "asylum-oscar-cell-wall"\s+\[other side of this gate: asylum_oscar\]/u);
  assert.match(text, /TO SIDE:\s+blighttown_quelaag — object o4950_0000 "blighttown-quelaag-boundary"\s+\[boss-encounter side; other side of this gate: blighttown_bell\]/u);
  assert.match(text, /DIRECTION: TWO-WAY \(the graph also has blighttown_quelaag -> asylum via o4950_0000, always; randomized, shuffled pairing\)/u);
  // The same object seen from the ARENA side (the Quelaag reverse-fog class of defect) is shown as leaving a boss arena.
  assert.match(text, /FROM SIDE: blighttown_quelaag — object o4950_0000 "blighttown-quelaag-boundary"\s+\[boss arena; other side of this gate: blighttown_bell\]/u);
  assert.match(text, /DIRECTION: ONE-WAY \(no edge back in the graph; fixed\)/u);
  // A way back that needs something says so; an in-map way back is named as such.
  assert.match(text, /DIRECTION: TWO-WAY \(the graph also has sens_mid -> blighttown_quelaag via o5150_0000, only when cage_key_goods_2003; randomized, shuffled pairing\)/u);
  const walkable = fixtureDocument();
  Object.assign(walkable.mainRoute[3].edge, { reverse: 'unconditional', reverseVia: 'in map', oneWay: false, randomized: false, pairing: null });
  assert.match(renderSolverSteps(walkable).join('\n'), /DIRECTION: TWO-WAY \(the graph also has sens_mid -> blighttown_quelaag by an in-map link, always; fixed\)/u);
  assert.match(text, /\(in-map link\)/u);
});

test('rendering: conditions, where each is satisfied, item acquisition with its lot, and the return assumption', () => {
  const text = renderSolverSteps(fixtureDocument()).join('\n\n');
  assert.match(text, /SOLVER REQUIREMENTS: none/u);
  assert.match(text, /SOLVER REQUIREMENTS: blighttown_quelaag\n\s+- Area blighttown_quelaag reached — step 2/u);
  assert.match(text, /SOLVER REQUIREMENTS: cage_key_goods_2003\n\s+- key item cage_key_goods_2003 \(held by Area sens_upper\) — step 3/u);
  assert.match(text, /3\. ACQUIRE — Cage Key \(cage_key_goods_2003\)\n\s+SOURCE: item lot 1500150, in Area sens_upper/u);
  assert.match(text, /The solver considers it obtained once this Area has been reached\./u);
  assert.match(text, /First be in: blighttown_quelaag \(the solver assumes any Area it already reached can be entered again\)/u);
  // An item the solver could not place by lot says so instead of inventing a source.
  const noLot = fixtureDocument();
  noLot.mainRoute[2].item = { ...item, lots: [] };
  assert.match(renderSolverSteps(noLot).join('\n'), /no item lot was found for it; the solver uses its default Area sens_upper/u);
});

test('rendering: a boss-completion requirement points at the step that assumes the completion', () => {
  const document = fixtureDocument();
  const completion = { sequence: 2, name: 'boss_defeated_1', encounterArea: 'blighttown_quelaag', eventId: 1, triggerEntrance: 'o4950_0000', triggerEdgeFrom: 'asylum' };
  document.mainRoute.splice(2, 0, { number: 3, type: 'boss-completion', area: 'blighttown_quelaag', sequence: 1, returnTo: null, edge: null, item: null, completion, notes: [] });
  document.mainRoute.forEach((step, index) => { step.number = index + 1; });
  const gated = document.mainRoute.find((step) => step.edge?.exit.object === 'o4950_0000' && step.number > 3);
  gated.edge = { ...gated.edge, requires: 'boss_defeated_1', requirements: [{ name: 'boss_defeated_1', kind: 'boss-completion', reachedInArea: 'blighttown_quelaag', satisfiedAtSequence: 2, item: null }] };
  const text = renderSolverSteps(document).join('\n\n');
  assert.match(text, /3\. BOSS COMPLETION ASSUMED — boss_defeated_1/u);
  assert.match(text, /- boss completion boss_defeated_1 \(encounter Area blighttown_quelaag\) — step 3/u);
  assert.doesNotMatch(text, /step \?/u);
});

test('rendering: a glitched-logic step carries a visible warning, and no other step does', () => {
  const lines = renderSolverSteps(fixtureDocument());
  assert.equal(lines.filter((block) => block.includes('⚠ GLITCHED LOGIC REQUIRED')).length, 1);
  assert.match(lines[5], /^6\. GLITCHED-LOGIC ROUTE\n\s+⚠ GLITCHED LOGIC REQUIRED/u);
  assert.match(renderSolverRoute(fixtureDocument()), /GLITCHED LOGIC IS ON/u);
  const off = fixtureDocument();
  off.claim.glitchedLogicEnabled = false;
  assert.match(renderSolverRoute(off), /glitched logic is off/u);
});

test('rendering: the document is deterministic, names the run context, and carries the tree only on request', () => {
  const document = fixtureDocument();
  assert.equal(renderSolverRoute(document), renderSolverRoute(structuredClone(document)));
  const route = renderSolverRoute(document);
  assert.match(route, /- Seed Code: DS1TR1-TEST/u);
  assert.match(route, /- Generation profile: ds1tr-gen-v6/u);
  assert.match(route, /- Fog seed: 99 \(Fog attempt 3, combined attempt 1/u);
  assert.ok(!route.includes('Full proof tree'));
  assert.match(renderSolverRoute(document, { includeTree: true }), /Full proof tree/u);
  assert.equal(renderSolverTree(document).length, 2);
  assert.match(renderSolverTree(document)[1], /blighttown_quelaag {2}<- asylum via randomized boss fog \(o8952_0000 -> o4950_0000; two-way\)/u);
});

test('JSON: the document survives serialization and is recognized; anything else is not', () => {
  const round = JSON.parse(JSON.stringify(fixtureDocument()));
  assert.equal(isSolverWitnessDocument(round), true);
  assert.equal(isSolverWitnessDocument({ ...round, format: 'other' }), false);
  assert.equal(isSolverWitnessDocument({ ...round, schemaVersion: 2 }), false);
  assert.equal(isSolverWitnessDocument(null), false);
  assert.deepEqual(round, fixtureDocument());
});

test('store: the ACCEPTED attempt\'s witness is kept next to the run with the context; a failed attempt\'s file is ignored; the output is deterministic', async () => {
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-witness-'));
  try {
    const workspace = path.join(temp, 'run');
    const failed = { ...fixtureDocument(), target: { area: 'failed-attempt', reason: 'must be ignored' } };
    delete failed.context;
    const accepted = fixtureDocument();
    delete accepted.context;
    for (const [attempt, document] of [[{ combinedAttempt: 0, fogAttempt: 0 }, failed], [{ combinedAttempt: 0, fogAttempt: 1 }, accepted]]) {
      await mkdir(path.join(workspace, fogAttemptDirectory(attempt), 'game'), { recursive: true });
      await writeFile(path.join(workspace, fogAttemptDirectory(attempt), 'solver-witness.json'), JSON.stringify(document));
    }
    const attempts = [
      { combinedAttempt: 0, fogAttempt: 0, combinedSeed: 'c', fogSeed: 11, result: 'FOG_SEED_UNSOLVABLE' },
      { combinedAttempt: 0, fogAttempt: 1, combinedSeed: 'c', fogSeed: 22, result: 'success' },
    ];
    const source = { seedCode: 'DS1TR1-TEST', profile: 'ds1tr-gen-v6', appVersion: '9.9.9', masterSeed: 'master', attempts };
    const target = await persistSolverWitness(workspace, source);
    assert.equal(target, path.join(workspace, 'solver-witness.json'));
    const stored = await readWitnessDocument(target);
    assert.equal(stored.target.area, 'depths_gaping', 'the accepted attempt, not the rejected one');
    assert.deepEqual(stored.context, witnessContext(source));
    assert.equal(stored.context.fogSeed, 22);
    assert.equal(stored.context.fogAttempt, 1);
    assert.equal(stored.context.attempts.length, 2);
    const markdown = await readFile(path.join(workspace, 'solver-route.md'), 'utf8');
    assert.match(markdown, /Fog seed: 22 \(Fog attempt 2/u);
    assert.match(markdown, /## Full proof tree/u);
    // Same input, same bytes: no timestamp and no path.
    const first = [await readFile(target), await readFile(path.join(workspace, 'solver-route.md'))];
    await persistSolverWitness(workspace, source);
    assert.deepEqual([await readFile(target), await readFile(path.join(workspace, 'solver-route.md'))], first);
    assert.ok(!first[0].toString('utf8').includes(temp.replaceAll('\\', '\\\\')) && !first[0].toString('utf8').includes(temp));

    // Availability and retrieval from a history entry whose package is `<run>/fog-0-1/game`.
    const entry = { id: 'e', seedCode: 'DS1TR1-TEST', packagePath: path.join(workspace, 'fog-0-1', 'game') };
    assert.equal(await locateWitnessFile(entry.packagePath, temp), target);
    assert.equal(await solverRouteAvailableFor(entry, temp), true);
    const route = await solverRouteForEntry(entry, temp);
    assert.equal(route.document.context.seedCode, 'DS1TR1-TEST');
    assert.match(route.markdown, /FROM SIDE:/u);
    assert.equal(await solverRouteForEntry({ ...entry, seedCode: 'DS1TR1-OTHER' }, temp), null, 'a witness of another world is never shown');
    assert.equal(await solverRouteAvailableFor({ ...entry, packagePath: path.join(temp, 'elsewhere', 'game') }, temp), false);
    assert.equal(await locateWitnessFile(path.join(temp, '..', 'outside'), temp), null, 'never looks above the run root');
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});

test('store: a run without a witness (Fog off, an older run, a host that could not write it) yields nothing and never an error', async () => {
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-witness-none-'));
  try {
    await mkdir(path.join(temp, 'fog-0-0', 'game'), { recursive: true });
    const attempts = [{ combinedAttempt: 0, fogAttempt: 0, combinedSeed: 'c', fogSeed: 1, result: 'success' }];
    assert.equal(await persistSolverWitness(temp, { seedCode: 's', profile: 'p', appVersion: 'v', masterSeed: null, attempts }), null);
    assert.equal(await persistSolverWitness(temp, { seedCode: 's', profile: 'p', appVersion: 'v', masterSeed: null, attempts: [] }), null);
    await writeFile(path.join(temp, 'fog-0-0', 'solver-witness.json'), '{ not json');
    assert.equal(await persistSolverWitness(temp, { seedCode: 's', profile: 'p', appVersion: 'v', masterSeed: null, attempts }), null);
    assert.equal((await readdir(temp)).includes('solver-route.md'), false);
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});

test('support bundle: the route and the witness are allowlisted entries and are carried unchanged', () => {
  assert.ok(BUNDLE_ALLOWLIST.includes('generation/solver-route.md') && BUNDLE_ALLOWLIST.includes('generation/solver-witness.json'));
  const document = fixtureDocument();
  const bundle = buildSupportBundle([
    { name: 'summary.txt', content: 'x' },
    { name: 'generation/solver-route.md', content: renderSolverRoute(document) },
    { name: 'generation/solver-witness.json', content: JSON.stringify(document) },
  ], { directories: [], secrets: [] }, { seedCode: document.context.seedCode });
  assert.equal(bundle.ok, true, bundle.ok ? '' : `${bundle.code}: ${bundle.detail}`);
  assert.deepEqual(bundle.report.entries.map((entry) => entry.name).sort(), ['generation/solver-route.md', 'generation/solver-witness.json', 'summary.txt']);
});

async function hashTree(root) {
  const files = new Map();
  async function walk(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) await walk(full);
      else files.set(path.relative(root, full).split(path.sep).join('/'), createHash('sha256').update(await readFile(full)).digest('hex'));
    }
  }
  await walk(root);
  return files;
}

test('real generation: the same Seed Code twice gives the same witness, route and package; the witness is outside the package', { skip: !gameSource }, async () => {
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-witness-real-'));
  try {
    const game = path.join(temp, 'game');
    await cp(path.join(gameSource, executableName), path.join(game, executableName));
    for (const managed of managedRoots) await cp(path.join(gameSource, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
    const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
    assert.equal((await authority.importPristine(game)).ok, true);
    const run = async (label) => {
      const prepared = prepareRun({ gamePath: game, seedCode: smokeCode });
      assert.equal(prepared.ok, true);
      const workspacePath = path.join(temp, label);
      await mkdir(workspacePath, { recursive: true });
      const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode }, () => {},
        { getPristineSource: (gamePath) => authority.getPristineSource(gamePath) });
      assert.equal(result.ok, true, result.ok ? '' : `${result.code}: ${result.message}`);
      const witnessPath = await persistSolverWitness(workspacePath, { seedCode: prepared.seedCode, profile: prepared.profile.id, appVersion: 'test', masterSeed: prepared.config.masterSeed, attempts: result.manifest.attempts });
      return { result, workspacePath, witnessPath, package: await hashTree(result.packagePath) };
    };
    const first = await run('one');
    const second = await run('two');
    assert.ok(first.witnessPath && second.witnessPath, 'a V6 world with Fog on has a witness');
    assert.deepEqual([await readFile(first.witnessPath), await readFile(path.join(first.workspacePath, 'solver-route.md'))],
      [await readFile(second.witnessPath), await readFile(path.join(second.workspacePath, 'solver-route.md'))], 'same Seed Code, same proof route');
    // The witness never enters the package: it is hashed and applied to the game.
    assert.ok(![...first.package.keys()].some((name) => name.includes('solver-')), 'no witness file inside the package');
    for (const name of ['config.json', 'randomizer.json', 'run-manifest.json']) { first.package.delete(name); second.package.delete(name); }
    assert.deepEqual([...first.package], [...second.package], 'the package itself is identical');
    const document = await readWitnessDocument(first.witnessPath);
    assert.equal(document.claim.areasReached, document.claim.areasTotal);
    assert.equal(document.claim.unreached.length, 0);
    assert.equal(document.target.area, 'kiln_gwyn');
    assert.equal(document.mainRoute[0].type, 'start');
    assert.equal(document.mainRoute.at(-1).area, 'kiln_gwyn');
    assert.equal(document.context.seedCode, smokeCode);
    assert.equal(document.context.fogSeed, first.result.manifest.attempts.at(-1).fogSeed);
    // Every cross step shows both gate sides.
    for (const step of document.mainRoute.filter((candidate) => candidate.type === 'cross')) {
      assert.ok(step.edge.exit.area && step.edge.arrival.area, `step ${step.number} has both sides`);
    }
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});
