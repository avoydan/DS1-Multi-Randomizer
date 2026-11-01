import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { buildSettings, seedCodeForProfile, v5ExtraCaseSets, v6ExtraCaseSets } from '../../scripts/goldenCompatibility.mjs';
import { canonicalSettingsJson } from '../../dist/core/canonicalSettings.js';
import { currentGenerationProfile, resolveGenerationProfile } from '../../dist/core/generationProfiles.js';
import { decodeSeedCode, encodeSeedCode } from '../../dist/core/seedCode.js';
import { deriveSeeds } from '../../dist/core/seeds.js';

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
  for (const name of (await readdir(directory)).filter((file) => file.endsWith('.json')).sort()) out.push({ ...JSON.parse(await readFile(path.join(directory, name), 'utf8')), file: name });
  return out;
}

test('the v1 golden fixtures cover every required scenario', async () => {
  const v1 = (await fixtures()).find((fixture) => fixture.file === 'ds1tr-gen-v1.json');
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

test('the v3 golden fixture pins a production Compact Fog full-triple run', async () => {
  const v3 = (await fixtures()).find((fixture) => fixture.file === 'ds1tr-gen-v3.compact-fog.json');
  assert.ok(v3, 'the ds1tr-gen-v3 Compact Fog fixture file exists');
  assert.deepEqual(v3.cases.map((entry) => entry.name), ['v3-compact-full-triple']);
  const [entry] = v3.cases;
  assert.equal(entry.profile, 'ds1tr-gen-v3');
  assert.equal(entry.settings.fog.enabled, true);
  assert.ok(entry.expected.fileCount > 0 && Object.keys(entry.expected.files).length === entry.expected.fileCount);
  assert.equal(entry.expected.attempts.at(-1), 'success');
});

test('the v4 golden fixture pins owner-approved roaming-boss fit behavior', async () => {
  const v4 = (await fixtures()).find((fixture) => fixture.file === 'ds1tr-gen-v4.roaming-boss-fit.json');
  assert.ok(v4, 'the ds1tr-gen-v4 roaming-boss fit fixture file exists');
  assert.deepEqual(v4.cases.map((entry) => entry.name), [
    'v4-bosses-only-100-anywhere', 'v4-both-100-boss-share', 'v4-bosses-only-100-stay-dead',
  ]);
  for (const entry of v4.cases) {
    assert.equal(entry.profile, 'ds1tr-gen-v4');
    assert.ok(entry.expected.fileCount > 0 && Object.keys(entry.expected.files).length === entry.expected.fileCount);
    assert.equal(entry.expected.attempts.at(-1), 'success');
  }
});

test('the v5 golden fixtures pin the Compact Fog hard-lock reproduction and full-triple output', async () => {
  const v5 = (await fixtures()).find((fixture) => fixture.file === 'ds1tr-gen-v5.compact-fog.json');
  assert.ok(v5, 'the ds1tr-gen-v5 Compact Fog fixture file exists');
  assert.deepEqual(v5.cases.map((entry) => entry.name), ['v5-support-reproduction', 'v5-compact-full-triple']);
  const reproduction = v5.cases[0];
  assert.equal(reproduction.profile, 'ds1tr-gen-v5');
  assert.equal(reproduction.masterSeed, '332055808');
  assert.equal(deriveSeeds(reproduction.masterSeed).fog, 4009337993);
  // Recorded after the 0.7.3/0.7.4 Compact V2 changes: the same Fog seed now yields a different, validator-accepted layout.
  assert.deepEqual(reproduction.expected.fogSeeds, [4009337993]);
  assert.deepEqual(reproduction.expected.attempts, ['success']);
  const fullTriple = v5.cases[1];
  assert.equal(fullTriple.masterSeed, 'golden-v5-route-search-7');
  // Accepted on the fifth (last) Fog attempt after four deterministic FOG_SEED_UNSOLVABLE rejections.
  assert.deepEqual(fullTriple.expected.fogSeeds, [3755285091, 3373530305, 7572447, 2839939825, 2331940042]);
  assert.deepEqual(fullTriple.expected.attempts, ['FOG_SEED_UNSOLVABLE', 'FOG_SEED_UNSOLVABLE', 'FOG_SEED_UNSOLVABLE', 'FOG_SEED_UNSOLVABLE', 'success']);
  assert.equal(reproduction.settings.fog.language, 'POLISH');
  assert.equal(reproduction.settings.items.enabled, false);
  assert.equal(reproduction.settings.enemies.enabled, false);
  for (const entry of v5.cases) {
    assert.equal(entry.profile, 'ds1tr-gen-v5');
    assert.ok(entry.expected.fileCount > 0 && Object.keys(entry.expected.files).length === entry.expected.fileCount);
    assert.equal(entry.expected.attempts.at(-1), 'success');
    assert.ok(entry.expected.fogSeeds.every((seed) => Number.isInteger(seed) && seed >= 0 && seed <= 0xffffffff));
  }
  assert.equal(v5.cases[1].settings.items.enabled, true);
  assert.equal(v5.cases[1].settings.enemies.enabled, true);
  assert.equal(v5.cases[1].settings.fog.enabled, true);
});

test('the v6 golden fixtures pin the start-reachability regressions: original hard-lock, V5 smoke start-trap, a Ceaseless-rejected world, full triple', async () => {
  const v6 = (await fixtures()).find((fixture) => fixture.file === 'ds1tr-gen-v6.compact-fog.json');
  assert.ok(v6, 'the ds1tr-gen-v6 Compact Fog fixture file exists');
  assert.deepEqual(v6.cases.map((entry) => entry.name), [
    'v6-original-hardlock-reproduction', 'v6-owner-smoke-start-trap', 'v6-v5-rejected-route-search-2', 'v6-compact-full-triple',
  ]);
  const [original, smoke, rejectedByV5, fullTriple] = v6.cases;
  // A: the original confirmed hard-lock: same master seed and settings as the V4 support bundle, so the same Fog seed that paired the Asylum hallway with Ceaseless.
  assert.equal(original.masterSeed, '332055808');
  assert.equal(deriveSeeds(original.masterSeed).fog, 4009337993);
  assert.deepEqual(original.expected.fogSeeds, [4009337993]);
  assert.deepEqual(original.expected.attempts, ['success']);
  assert.equal(original.settings.fog.language, 'POLISH');
  // B: the owner V5 smoke code (master 13825956): its first Fog seed is accepted on v6 at once.
  assert.equal(smoke.masterSeed, '13825956');
  assert.deepEqual(smoke.expected.attempts, ['success']);
  assert.equal(smoke.expected.fogSeeds.length, 1);
  // C: v5 exhausted all five Fog attempts for this master seed (golden-v5-route-search-2: 2098234880, 3667276361, 1192827970, 1573785486, 1444496450); v6 accepts the first.
  assert.equal(rejectedByV5.masterSeed, 'golden-v5-route-search-2');
  assert.deepEqual(rejectedByV5.expected.fogSeeds, [2098234880]);
  assert.deepEqual(rejectedByV5.expected.attempts, ['success']);
  assert.equal(fullTriple.settings.items.enabled, true);
  assert.equal(fullTriple.settings.enemies.enabled, true);
  assert.equal(fullTriple.settings.fog.enabled, true);
  for (const entry of v6.cases) {
    assert.equal(entry.profile, 'ds1tr-gen-v6');
    assert.ok(entry.expected.fileCount > 0 && Object.keys(entry.expected.files).length === entry.expected.fileCount);
    assert.equal(entry.expected.attempts.at(-1), 'success');
    assert.equal(entry.expected.attempts.length, 1, 'no Fog retry was needed: the retry budget and the acceptance rule do not hide anything');
  }
});

test('the V5 recorder preserves its declared profile and new worlds use the promoted profile', () => {
  const [spec] = v5ExtraCaseSets['compact-fog'];
  const seedCode = seedCodeForProfile('ds1tr-gen-v5', spec.masterSeed, buildSettings(spec.patch));
  const decoded = decodeSeedCode(seedCode);
  assert.equal(decoded.ok, true, decoded.message ?? '');
  assert.equal(decoded.envelope.profile, 'ds1tr-gen-v5');
  assert.equal(resolveGenerationProfile('ds1tr-gen-v5')?.id, 'ds1tr-gen-v5');
  assert.equal(currentGenerationProfile().id, 'ds1tr-gen-v6');
  const [v6Spec] = v6ExtraCaseSets['compact-fog'];
  const v6Decoded = decodeSeedCode(seedCodeForProfile('ds1tr-gen-v6', v6Spec.masterSeed, buildSettings(v6Spec.patch)));
  assert.equal(v6Decoded.ok, true, v6Decoded.message ?? '');
  assert.equal(v6Decoded.envelope.profile, 'ds1tr-gen-v6');
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
