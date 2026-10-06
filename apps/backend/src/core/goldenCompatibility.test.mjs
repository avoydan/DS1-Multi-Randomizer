import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { canonicalSettingsJson } from '../../dist/core/canonicalSettings.js';
import { resolveGenerationProfile } from '../../dist/core/generationProfiles.js';
import { decodeSeedCode, encodeSeedCode } from '../../dist/core/seedCode.js';

/**
 * Golden compatibility fixtures (docs/GENERATION_COMPATIBILITY.md). They are permanent: a fixture is never regenerated to make
 * a test pass. If regeneration disagrees with a fixture, the change belongs in a NEW generation profile.
 *
 * Always checked here: every recorded Seed Code still decodes to the recorded profile, master seed and canonical settings, and
 * re-encodes to the identical code (so the container format and the canonical form did not drift). The package hashes are
 * checked by regenerating the worlds, which needs a clean Dark Souls Remastered copy: set DS1TR_GOLDEN_GAME_SOURCE (plus the
 * FogHost / data tool variables of the e2e environment) and the same test also runs `goldenCompatibility.mjs verify`.
 */
const directory = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'fixtures', 'golden');
const REQUIRED_CASES = [
  'items-only', 'enemies-classic-permutation', 'enemies-sampling-v1', 'items-enemies-bosses', 'items-enemies-bosses-fog',
  'broad-items-shops-fog', 'custom-enemy-pool', 'second-gargoyle-keep', 'custom-pool-model-grace', 'world-options',
];

async function fixtures() {
  const out = [];
  for (const name of (await readdir(directory)).filter((file) => file.endsWith('.json')).sort()) out.push(JSON.parse(await readFile(path.join(directory, name), 'utf8')));
  return out;
}

test('the v1 golden fixtures cover every required scenario', async () => {
  const v1 = (await fixtures()).find((fixture) => fixture.profile === 'ds1tr-gen-v1');
  assert.ok(v1, 'the ds1tr-gen-v1 fixture file exists');
  assert.deepEqual(v1.cases.map((entry) => entry.name), REQUIRED_CASES);
  for (const entry of v1.cases) {
    assert.match(entry.expected.digest, /^[0-9a-f]{64}$/u, entry.name);
    assert.ok(entry.expected.fileCount > 0 && Object.keys(entry.expected.files).length === entry.expected.fileCount, `${entry.name} records its package file hashes`);
    assert.equal(entry.expected.attempts.at(-1), 'success', entry.name);
  }
  assert.ok(v1.cases.find((entry) => entry.name === 'custom-pool-model-grace').expected.modelGraceMaps >= 1, 'the grace case really exercises the one-model grace');
  assert.ok(v1.cases.find((entry) => entry.name === 'items-enemies-bosses-fog').settings.fog.enabled);
  assert.equal(v1.cases.find((entry) => entry.name === 'world-options').settings.fog.language, 'POLISH');
});

test('every recorded Seed Code decodes to its recorded profile, master seed and canonical settings, and re-encodes identically', async () => {
  for (const fixture of await fixtures()) {
    assert.ok(resolveGenerationProfile(fixture.profile), `profile ${fixture.profile} is still supported`);
    for (const entry of fixture.cases) {
      const decoded = decodeSeedCode(entry.seedCode);
      assert.equal(decoded.ok, true, `${fixture.profile}/${entry.name}: ${decoded.message ?? ''}`);
      assert.equal(decoded.envelope.profile, entry.profile);
      assert.equal(decoded.envelope.masterSeed, entry.masterSeed);
      assert.equal(canonicalSettingsJson(decoded.envelope.settings), JSON.stringify(entry.settings), `${entry.name}: canonical settings`);
      assert.equal(encodeSeedCode(decoded.envelope), entry.seedCode, `${entry.name}: the code is byte-for-byte reproducible`);
    }
  }
});

test('regenerating the golden worlds reproduces the recorded package hashes (needs a clean game copy)', { skip: !process.env.DS1TR_GOLDEN_GAME_SOURCE && 'set DS1TR_GOLDEN_GAME_SOURCE to run the real regeneration' }, () => {
  const script = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'scripts', 'goldenCompatibility.mjs');
  const result = spawnSync(process.execPath, [script, 'verify'], { encoding: 'utf8', env: process.env, timeout: 3_000_000 });
  assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
  assert.match(result.stdout, /RESULT: PASS/u);
});
