import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { DEFAULT_GENERATION_SETTINGS, SMOKE_SCENARIOS, SMOKE_TEST_CHECKLIST, SMOKE_TEST_PROFILE, joinConfig, resolveScenarioSettings } from '@ds1tr/contracts';
import { validateRandomizerConfig } from '../../dist/core/configValidation.js';
import { decodeSeedCode, encodeSeedCode } from '../../dist/core/seedCode.js';
import { smokeTestWorlds } from '../../dist/core/smokeTests.js';
import { canonicalizeSettings } from '../../dist/core/canonicalSettings.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.join(here, '..', '..', '..', '..');

test('the smoke-test worlds are the eight documented scenarios with unique ids and seeds', () => {
  assert.deepEqual(SMOKE_SCENARIOS.map((scenario) => scenario.id), [
    'classic-triple', 'sampling-regular', 'second-gargoyle', 'parish-models', 'boss-sampling', 'boss-repeat', 'boss-unique', 'boss-partial',
  ]);
  assert.equal(new Set(SMOKE_SCENARIOS.map((scenario) => scenario.masterSeed)).size, SMOKE_SCENARIOS.length);
  for (const scenario of SMOKE_SCENARIOS) {
    assert.ok(scenario.observe.length > 0, `${scenario.id} says what to look at`);
    assert.match(scenario.masterSeed, /^smoke-[a-h]-[a-z]+(-\d+)?$/u);
  }
  assert.ok(SMOKE_TEST_CHECKLIST.length >= 10);
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
});

test('the documented Seed Codes in docs/SMOKE_TESTS.md are the codes the app creates (the document is generated, never hand-edited)', async () => {
  const text = await readFile(path.join(repo, 'docs', 'SMOKE_TESTS.md'), 'utf8');
  const documented = [...text.matchAll(/^(DS1TR1-[0-9A-Z-]+)$/gmu)].map((match) => match[1]);
  const created = smokeTestWorlds().worlds.map((world) => world.seedCode);
  assert.deepEqual(documented, created);
  assert.match(text, /Nothing here is verified in the game/u);
});

test('the main screen starts from the same defaults the test worlds are built from', async () => {
  const app = await readFile(path.join(repo, 'apps', 'desktop', 'src', 'App.tsx'), 'utf8');
  assert.match(app, /joinConfig\(structuredClone\(DEFAULT_GENERATION_SETTINGS\), '', '', true\)/u);
  // The defaults are a complete, valid configuration.
  validateRandomizerConfig(joinConfig(DEFAULT_GENERATION_SETTINGS, 'validation-only', 'validation-only', true));
});
