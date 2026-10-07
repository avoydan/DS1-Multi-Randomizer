import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { DEFAULT_GENERATION_SETTINGS, SMOKE_SCENARIOS, SMOKE_START, SMOKE_TEST_CHECKLIST, SMOKE_TEST_PROFILE, describeSmokeExpected, describeSmokeTarget, joinConfig, resolveScenarioSettings } from '@ds1tr/contracts';
import { validateRandomizerConfig } from '../../dist/core/configValidation.js';
import { decodeSeedCode, encodeSeedCode } from '../../dist/core/seedCode.js';
import { smokeTestWorlds } from '../../dist/core/smokeTests.js';
import { canonicalizeSettings } from '../../dist/core/canonicalSettings.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.join(here, '..', '..', '..', '..');

test('the smoke-test worlds are eight quick smoke tests and twenty-two extended gameplay tests with unique ids and seeds', () => {
  const quick = SMOKE_SCENARIOS.filter((scenario) => scenario.category === 'quick').map((scenario) => scenario.id);
  const extended = SMOKE_SCENARIOS.filter((scenario) => scenario.category === 'extended').map((scenario) => scenario.id);
  assert.deepEqual(quick, ['quick-roaming-basic', 'quick-roaming-souls', 'quick-encounter-body', 'quick-roaming-respawn', 'quick-roaming-repeated', 'quick-sampling-asylum', 'quick-hostile-npc', 'quick-boss-asylum-demon']);
  assert.deepEqual(extended, [
    'classic-triple', 'sampling-regular', 'second-gargoyle', 'parish-models', 'boss-sampling', 'boss-repeat', 'boss-unique', 'boss-partial',
    'roaming-repeat', 'roaming-gargoyle', 'roaming-fog', 'roaming-parish',
    'body-burg-fights', 'body-fog', 'body-roaming', 'body-both', 'body-gargoyle',
    'npc-world', 'npc-fog', 'npc-roaming', 'npc-type', 'npc-bosses',
  ]);
  assert.deepEqual(SMOKE_SCENARIOS.map((scenario) => scenario.category), [...quick.map(() => 'quick'), ...extended.map(() => 'extended')], 'quick tests come first');
  assert.equal(new Set(SMOKE_SCENARIOS.map((scenario) => scenario.masterSeed)).size, SMOKE_SCENARIOS.length);
  assert.equal(new Set(SMOKE_SCENARIOS.map((scenario) => scenario.id)).size, SMOKE_SCENARIOS.length);
  for (const scenario of SMOKE_SCENARIOS) {
    assert.ok(scenario.observe.length > 0, `${scenario.id} says what to look at`);
    assert.match(scenario.masterSeed, /^smoke-(q[1-9]|[a-o])-[a-z-]+(-\d+)?$/u);
  }
  assert.ok(SMOKE_TEST_CHECKLIST.length >= 10);
});

test('every quick smoke test states an exact target, an expected replacement and a short time, and never says "find a boss somewhere"', () => {
  for (const scenario of SMOKE_SCENARIOS.filter((entry) => entry.category === 'quick')) {
    const id = scenario.id;
    assert.ok(scenario.expectations?.length >= 1, `${id} has expectations`);
    assert.ok(scenario.estimateSeconds >= 20 && scenario.estimateSeconds <= 90, `${id}: 20-90 seconds to the target (${scenario.estimateSeconds})`);
    assert.ok(scenario.observe.length >= 3, `${id} lists specific checks`);
    for (const line of [...scenario.observe, ...(scenario.afterwards ?? []), scenario.purpose]) {
      assert.doesNotMatch(line, /find a boss|somewhere|among the regular enemies/iu, `${id}: no vague instruction ("${line}")`);
    }
    for (const entry of scenario.expectations) {
      assert.match(entry.slot, /^c\d{4}_\d{4}$/u, `${id}: an exact slot`);
      assert.ok(entry.meters > 0 && entry.meters <= 40 || entry.kind === 'boss', `${id}/${entry.slot}: close to the start (${entry.meters} m)`);
      assert.ok(entry.name && entry.replacedName && entry.model && entry.replacedModel, `${id}/${entry.slot} names both models`);
      assert.equal(entry.map, SMOKE_START.map, `${id}: the first visit of the Undead Asylum`);
      const text = `${describeSmokeTarget(entry)} | ${describeSmokeExpected(entry)}`;
      assert.ok(text.includes(entry.slot) && text.includes(String(entry.meters)) || entry.kind === 'boss', `${id}: the text names the slot and the distance`);
    }
    const settings = resolveScenarioSettings(scenario);
    for (const entry of scenario.expectations.filter((expected) => expected.kind === 'roaming-boss')) {
      assert.ok(['bosses', 'both'].includes(settings.enemies.placement.normalReplacement), `${id}: roaming bosses are switched on`);
      assert.equal(entry.disableRespawn === true, entry.disableRespawn === undefined ? false : settings.enemies.placement.roamingBossesStayDead, `${id}: the stay-dead expectation matches the setting`);
      assert.equal(entry.wakeUpEvent, true, `${id}: a roaming boss is checked for its wake-up event`);
    }
  }
  // The quick tests cover: basic roaming, souls + stay dead, respawn, a repeated boss, regular sampling and boss sampling.
  const by = (id) => resolveScenarioSettings(SMOKE_SCENARIOS.find((scenario) => scenario.id === id));
  assert.equal(by('quick-roaming-souls').enemies.placement.roamingBossSouls, 100);
  assert.equal(by('quick-roaming-souls').enemies.placement.roamingBossesStayDead, true);
  assert.equal(by('quick-roaming-repeated').enemies.placement.typeReplacement, 'enabled-except-roaming-bosses');
  assert.equal(new Set(SMOKE_SCENARIOS.find((scenario) => scenario.id === 'quick-roaming-repeated').expectations.map((entry) => entry.model)).size, 1, 'the same boss three times');
  assert.equal(by('quick-roaming-respawn').enemies.placement.roamingBossesStayDead, false);
  assert.equal(by('quick-roaming-respawn').enemies.placement.roamingBossSouls, 50);
  const respawn = SMOKE_SCENARIOS.find((scenario) => scenario.id === 'quick-roaming-respawn');
  assert.equal(respawn.expectations[0].disableRespawn, false, 'the static check asserts the respawn bit is 0');
  assert.ok([...respawn.observe, ...respawn.afterwards].some((line) => /should respawn/u.test(line)), 'the expected result is stated explicitly');
  assert.ok(respawn.estimateSeconds <= 60);
  assert.equal(by('quick-sampling-asylum').enemies.placement.normalReplacement, 'normals');
  assert.equal(by('quick-boss-asylum-demon').enemies.placement.bossReplacement, 'bosses');
});

test('only worlds that a person played in the real game carry a gameplay result; static checks never set one', () => {
  const played = SMOKE_SCENARIOS.filter((scenario) => scenario.gameplay).map((scenario) => scenario.id);
  assert.deepEqual(played.slice().sort(), ['body-burg-fights', 'quick-boss-asylum-demon', 'quick-encounter-body', 'quick-roaming-basic', 'quick-roaming-repeated', 'quick-roaming-souls', 'quick-sampling-asylum']);
  for (const scenario of SMOKE_SCENARIOS.filter((entry) => entry.gameplay)) {
    // Only the Burg fights world is partly played (Taurus Demon yes, Capra Demon not yet); every other played world passed entirely.
    assert.equal(scenario.gameplay.result, scenario.id === 'body-burg-fights' ? 'partly-passed' : 'passed', scenario.id);
    assert.match(scenario.gameplay.date, /^\d{4}-\d{2}-\d{2}$/u);
  }
  // The respawn test, every extended world (Fog, Gargoyle, Parish ...) and anything new have not been played.
  assert.equal(SMOKE_SCENARIOS.find((scenario) => scenario.id === 'quick-roaming-respawn').gameplay, undefined);
  for (const scenario of SMOKE_SCENARIOS.filter((entry) => entry.category === 'extended' && entry.id !== 'body-burg-fights')) assert.equal(scenario.gameplay, undefined, scenario.id);
  assert.match(SMOKE_SCENARIOS.find((scenario) => scenario.id === 'body-burg-fights').gameplay.note, /Capra Demon fight was not played/u, 'the unplayed part is named');
  // Two played worlds changed one cell after they were played (the respawn bit); the record says so.
  for (const id of ['quick-roaming-basic', 'quick-roaming-repeated']) assert.match(SMOKE_SCENARIOS.find((scenario) => scenario.id === id).gameplay.note, /disableRespawn = 0/u, id);
  const { worlds } = smokeTestWorlds();
  assert.deepEqual(worlds.filter((world) => world.gameplay).map((world) => world.id).sort(), played.slice().sort());
});

test('quick worlds are ordinary worlds: no override, no forced placement, nothing a Seed Code does not already say', () => {
  for (const scenario of SMOKE_SCENARIOS) {
    assert.deepEqual(Object.keys(scenario).filter((key) => /force|override/iu.test(key)), [], `${scenario.id} carries no forced placement`);
    // The same settings and master seed that the Seed Code holds are the whole definition: the Seed Code decodes to exactly them.
    const world = smokeTestWorlds().worlds.find((entry) => entry.id === scenario.id);
    const decoded = decodeSeedCode(world.seedCode);
    assert.equal(decoded.envelope.masterSeed, scenario.masterSeed);
  }
  // Part E1 settings of quick worlds sit inside what presets and Seed Codes already express.
  for (const id of ['quick-roaming-basic', 'quick-roaming-souls', 'quick-roaming-respawn', 'quick-roaming-repeated']) {
    const placement = resolveScenarioSettings(SMOKE_SCENARIOS.find((scenario) => scenario.id === id)).enemies.placement;
    assert.equal(placement.algorithm, 'sampling-v1');
    assert.equal(placement.fit, 'anywhere');
    assert.equal(placement.difficultyMode, 'random', 'Easy Asylum would keep bosses out of the Asylum');
  }
});

test('every smoke-test world is a valid configuration whose Seed Code decodes to exactly its settings, pinned to ds1tr-gen-v1', () => {
  assert.equal(SMOKE_TEST_PROFILE, 'ds1tr-gen-v1');
  const { worlds, checklist } = smokeTestWorlds();
  assert.equal(worlds.length, SMOKE_SCENARIOS.length);
  assert.equal(checklist.length, SMOKE_TEST_CHECKLIST.length);
  for (const world of worlds) {
    const scenario = SMOKE_SCENARIOS.find((entry) => entry.id === world.id);
    const settings = canonicalizeSettings(resolveScenarioSettings(scenario));
    validateRandomizerConfig(joinConfig(settings, 'validation-only', scenario.masterSeed, false));
    const decoded = decodeSeedCode(world.seedCode);
    assert.equal(decoded.ok, true, world.id);
    assert.equal(decoded.envelope.profile, 'ds1tr-gen-v1');
    assert.equal(decoded.envelope.masterSeed, scenario.masterSeed);
    assert.deepEqual(decoded.envelope.settings, settings);
    assert.equal(encodeSeedCode(decoded.envelope), world.seedCode, 'the code re-encodes identically');
  }
});

test('the scenarios exercise what they claim: boss sampling options, chance, unique bosses and the Gargoyle mode', () => {
  const by = (id) => resolveScenarioSettings(SMOKE_SCENARIOS.find((scenario) => scenario.id === id));
  assert.equal(by('classic-triple').enemies.placement.algorithm, 'permutation-v1');
  assert.equal(by('sampling-regular').enemies.placement.algorithm, 'sampling-v1');
  assert.equal(by('sampling-regular').enemies.randomizeBosses, false);
  assert.equal(by('second-gargoyle').enemies.secondGargoyle, 'replace');
  assert.equal(by('second-gargoyle').enemies.randomizeRegularEnemies, false);
  for (const id of ['boss-sampling', 'boss-repeat', 'boss-unique', 'boss-partial']) assert.equal(by(id).enemies.placement.bossReplacement, 'bosses', id);
  assert.equal(by('boss-unique').enemies.placement.uniqueBosses, true);
  assert.equal(by('boss-partial').enemies.placement.replacementChance, 50);
  assert.equal(by('parish-models').enemies.placement.fit, 'anywhere');
  for (const id of ['roaming-repeat', 'roaming-gargoyle', 'roaming-fog', 'roaming-parish']) {
    assert.equal(by(id).enemies.placement.algorithm, 'sampling-v1', id);
    assert.ok(['bosses', 'both'].includes(by(id).enemies.placement.normalReplacement), `${id} lets bosses stand in regular slots`);
  }
  assert.equal(by('roaming-repeat').enemies.placement.typeReplacement, 'enabled');
  assert.equal(by('roaming-gargoyle').enemies.secondGargoyle, 'replace');
  assert.equal(by('roaming-fog').fog.enabled, true);
  assert.equal(by('roaming-parish').enemies.placement.normalReplacement, 'bosses');
  // Part E2: a normal enemy as the body of a boss encounter.
  for (const id of ['quick-encounter-body', 'body-burg-fights', 'body-fog', 'body-roaming', 'body-gargoyle']) assert.equal(by(id).enemies.placement.bossReplacement, 'normals', id);
  assert.equal(by('body-both').enemies.placement.bossReplacement, 'both');
  assert.equal(by('body-both').enemies.placement.bossChanceInBossSlots, 40);
  assert.equal(by('body-fog').fog.enabled, true);
  assert.equal(by('body-gargoyle').enemies.secondGargoyle, 'replace');
  assert.equal(by('body-roaming').enemies.placement.normalReplacement, 'both');
  // Part F1: hostile NPCs.
  for (const id of ['quick-hostile-npc', 'npc-world', 'npc-fog', 'npc-roaming', 'npc-type', 'npc-bosses']) {
    assert.equal(by(id).enemies.placement.hostileNpcs, true, id);
    assert.ok(['normals', 'both'].includes(by(id).enemies.placement.normalReplacement), `${id} replaces normal enemies`);
    assert.equal(by(id).enemies.placement.customPool, null, `${id} has no custom pool`);
  }
  assert.equal(by('npc-fog').fog.enabled, true);
  assert.equal(by('npc-type').enemies.placement.typeReplacement, 'enabled');
  assert.equal(by('npc-roaming').enemies.placement.normalReplacement, 'both');
  assert.equal(by('npc-bosses').enemies.placement.bossReplacement, 'both');
  const hostile = SMOKE_SCENARIOS.find((scenario) => scenario.id === 'quick-hostile-npc');
  assert.equal(hostile.expectations[0].kind, 'hostile-npc');
  assert.equal(hostile.gameplay, undefined, 'nobody has played the hostile NPC world yet');
  assert.match(describeSmokeExpected(hostile.expectations[0]), /no dialogue, no shop/u);
  const encounter = SMOKE_SCENARIOS.find((scenario) => scenario.id === 'quick-encounter-body');
  assert.equal(encounter.expectations[0].kind, 'encounter-body');
  assert.equal(encounter.gameplay.result, 'passed', 'the Asylum Demon body was played in the real game');
  assert.match(describeSmokeExpected(encounter.expectations[0]), /the health bar and the name on it stay Asylum Demon's/u);
});

test('the documented Seed Codes in docs/SMOKE_TESTS.md are the codes the app creates (the document is generated, never hand-edited)', async () => {
  const text = await readFile(path.join(repo, 'docs', 'SMOKE_TESTS.md'), 'utf8');
  const documented = [...text.matchAll(/^(DS1TR1-[0-9A-Z-]+)$/gmu)].map((match) => match[1]);
  const created = smokeTestWorlds().worlds.map((world) => world.seedCode);
  assert.deepEqual(documented, created);
  assert.match(text, /Nothing here is verified in the game/u);
});

test('the main screen starts from the product default settings, a complete and valid configuration (the test worlds have their own frozen base)', async () => {
  const app = await readFile(path.join(repo, 'apps', 'desktop', 'src', 'App.tsx'), 'utf8');
  assert.match(app, /joinConfig\(structuredClone\(DEFAULT_GENERATION_SETTINGS\), '', '', true\)/u);
  // The defaults are a complete, valid configuration.
  validateRandomizerConfig(joinConfig(DEFAULT_GENERATION_SETTINGS, 'validation-only', 'validation-only', true));
});
