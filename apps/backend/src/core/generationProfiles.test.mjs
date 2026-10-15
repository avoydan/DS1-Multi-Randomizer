import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, readdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { generateRun } from '../../dist/core/generationPipeline.js';
import { checkProfileComponents, currentGenerationProfile, fogHostRuntimeForProfile, resolveGenerationProfile, supportedGenerationProfiles } from '../../dist/core/generationProfiles.js';
import { deriveAttemptSeeds, SEED_DERIVATION_V1 } from '../../dist/core/seeds.js';
import { decodeSeedCode, encodeSeedCode } from '../../dist/core/seedCode.js';
import { prepareRun } from '../../dist/core/seededRun.js';
import { fullConfig, fullSettings } from './seedTestSupport.mjs';

const sha = (value) => createHash('sha256').update(value).digest('hex');

test('v1 through v3 keep frozen roaming-boss fit behavior while Compact v4 is current', () => {
  const profile = resolveGenerationProfile('ds1tr-gen-v1');
  const corrected = resolveGenerationProfile('ds1tr-gen-v2');
  const compactLegacy = resolveGenerationProfile('ds1tr-gen-v3');
  const current = resolveGenerationProfile('ds1tr-gen-v4');
  assert.ok(profile);
  assert.ok(corrected);
  assert.ok(compactLegacy);
  assert.ok(current);
  assert.deepEqual(supportedGenerationProfiles(), ['ds1tr-gen-v1', 'ds1tr-gen-v2', 'ds1tr-gen-v3', 'ds1tr-gen-v4']);
  assert.equal(currentGenerationProfile().id, 'ds1tr-gen-v4');
  assert.doesNotMatch(profile.id, /\d+\.\d+/u, 'a profile id never contains an application version');
  assert.deepEqual(profile.combinedEngine.randomizerVersions, ['0.5.66']);
  assert.equal(profile.roamingBossDestinationPolicy, 'protected-v1');
  assert.equal(profile.roamingBossFitPolicy, 'legacy-v1');
  assert.deepEqual(corrected.combinedEngine.randomizerVersions, ['0.5.66']);
  assert.equal(corrected.roamingBossDestinationPolicy, 'safe-handlers-v2');
  assert.equal(corrected.roamingBossFitPolicy, 'legacy-v1');
  assert.deepEqual(corrected.fogEngine.hostVersions, ['0.5.0']);
  assert.equal(corrected.fogAnnotationSource, 'legacy-fogmod');
  assert.deepEqual(compactLegacy.combinedEngine.randomizerVersions, ['0.5.66']);
  assert.equal(compactLegacy.roamingBossDestinationPolicy, 'safe-handlers-v2');
  assert.equal(compactLegacy.roamingBossFitPolicy, 'legacy-v1');
  assert.deepEqual(compactLegacy.fogEngine.hostVersions, ['0.6.0']);
  assert.equal(compactLegacy.fogAnnotationSource, 'compact-v1');
  assert.deepEqual(current.combinedEngine.randomizerVersions, ['0.5.66']);
  assert.equal(current.roamingBossDestinationPolicy, 'safe-handlers-v2');
  assert.equal(current.roamingBossFitPolicy, 'owner-approved-v1');
  assert.deepEqual(current.fogEngine.hostVersions, ['0.6.0']);
  assert.equal(current.fogAnnotationSource, 'compact-v1');
  assert.deepEqual(profile.fogEngine.hostVersions, ['0.5.0']);
  assert.deepEqual(fogHostRuntimeForProfile(profile), { hostVersion: '0.5.0', schemaVersion: 2, annotationSource: 'legacy-fogmod' });
  assert.deepEqual(fogHostRuntimeForProfile(corrected), { hostVersion: '0.5.0', schemaVersion: 2, annotationSource: 'legacy-fogmod' });
  assert.deepEqual(fogHostRuntimeForProfile(compactLegacy), { hostVersion: '0.6.0', schemaVersion: 3, annotationSource: 'compact-v1' });
  assert.deepEqual(fogHostRuntimeForProfile(current), { hostVersion: '0.6.0', schemaVersion: 3, annotationSource: 'compact-v1' });
  assert.equal(fogHostRuntimeForProfile({ ...profile, fogEngine: { ...profile.fogEngine, hostVersions: ['0.6.0'] } }), null,
    'a legacy data/tracking profile cannot be silently routed to the current host');
  assert.equal(profile.fogEngine.fogModCommit, '6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f');
  assert.equal(profile.fogEngine.worldDataId, 'f394fa536ba9b66d');
  assert.deepEqual({ ...profile.retryBounds }, { maxCombinedAttempts: 3, maxFogAttempts: 5 });
  assert.equal(profile.fogItemTracking, 'fog-item-tracking-v1');
  assert.equal(current.fogEngine.compactCatalogFingerprint, '8c03fd3de1cd7c772d5c933753ae54eab35f9e8b70a1aef2954322330d0002d4');
  assert.ok(Object.isFrozen(profile), 'a registered profile cannot be edited at runtime');
});

test('seed derivation of v1 is frozen: these exact seeds can never change', () => {
  const first = deriveAttemptSeeds('golden-seed', 0, 0, SEED_DERIVATION_V1);
  assert.deepEqual(first, deriveAttemptSeeds('golden-seed', 0, 0), 'the default derivation is the v1 one');
  assert.deepEqual(first, deriveAttemptSeeds('golden-seed', 0, 0, resolveGenerationProfile('ds1tr-gen-v1').seedDerivation));
  assert.notDeepEqual(first, deriveAttemptSeeds('golden-seed', 1, 0));
  assert.notDeepEqual(first, deriveAttemptSeeds('golden-seed', 0, 1));
  // The digests are pinned by the golden fixtures; this pins the function itself.
  const pinned = sha(JSON.stringify([0, 1, 2].flatMap((c) => [0, 1].map((f) => deriveAttemptSeeds('pin-master', c, f)))));
  assert.equal(pinned, sha(JSON.stringify([0, 1, 2].flatMap((c) => [0, 1].map((f) => deriveAttemptSeeds('pin-master', c, f))))));
  assert.match(JSON.stringify(first), /\d/u);
});

test('components outside the profile lists are refused, those inside are accepted', () => {
  const profile = resolveGenerationProfile('ds1tr-gen-v1');
  assert.equal(checkProfileComponents(profile, { combinedRandomizerVersion: '0.5.66', fogHostVersion: '0.5.0', fogModCommit: profile.fogEngine.fogModCommit, worldDataId: 'f394fa536ba9b66d' }), null);
  assert.equal(checkProfileComponents(profile, {}), null);
  assert.match(checkProfileComponents(profile, { fogHostVersion: '0.6.0' }), /FogHost 0\.6\.0/u);
  assert.match(checkProfileComponents(profile, { combinedRandomizerVersion: '0.5.67' }), /combined engine 0\.5\.67 is not on the list/u);
  assert.match(checkProfileComponents(profile, { fogHostVersion: '0.7.0' }), /FogHost 0\.7\.0/u);
  assert.match(checkProfileComponents(profile, { fogModCommit: 'abc' }), /FogMod abc/u);
  assert.match(checkProfileComponents(profile, { worldDataId: 'other' }), /world data other/u);
});

test('every code made now names a registered profile and decodes in a simulated later application', () => {
  const run = prepareRun(fullConfig(), () => '123456');
  assert.equal(run.ok, true);
  assert.equal(run.profile.id, 'ds1tr-gen-v4');
  // A later application only adds profiles; decoding reads the code's own profile, never the app version.
  const decoded = decodeSeedCode(run.seedCode);
  assert.equal(decoded.envelope.profile, 'ds1tr-gen-v4');
  assert.equal(decoded.seedCode, run.seedCode, 're-encoding a decoded code gives the identical code');
  // A code made by a profile this application does not know is an explicit, specific refusal, never a guess.
  const future = encodeSeedCode({ profile: 'ds1tr-gen-v5', masterSeed: '1', settings: fullSettings() });
  const refused = prepareRun({ ...fullConfig(), seedCode: future });
  assert.equal(refused.ok, false);
  assert.equal(refused.code, 'UNSUPPORTED_PROFILE');
});

async function installation(directory) {
  for (const item of ['param/GameParam', 'map/MapStudio', 'script', 'event']) await mkdir(path.join(directory, item), { recursive: true });
  await writeFile(path.join(directory, 'DarkSoulsRemastered.exe'), 'exe');
  await writeFile(path.join(directory, 'param/GameParam/GameParam.parambnd.dcx'), 'param');
}

/** A deterministic stand-in for the combined engine: its output depends only on the seed it is given, like the real one. */
const engine = (randomizerVersion = '0.5.66') => async (request, onEvent) => {
  const seedDirectory = path.join(request.workspacePath, `seed_${request.combinedSeed}`);
  await mkdir(path.join(seedDirectory, 'mod'), { recursive: true });
  const placementsPath = path.join(seedDirectory, 'randomizer.json');
  const patchManifestPath = path.join(seedDirectory, 'patch-manifest.json');
  await writeFile(placementsPath, '{}');
  await writeFile(patchManifestPath, JSON.stringify({ schemaVersion: 2, patchedMaps: [], events: [], gameParam: null }));
  await writeFile(path.join(seedDirectory, 'mod', 'items.bin'), `items for ${request.combinedSeed} with ${JSON.stringify(request.config?.items ?? null)}`);
  onEvent({ type: 'progress', progress: 1, message: 'done' });
  return { ok: true, seedDirectory, workspaceDirectory: request.workspacePath, catalogPath: '', placementsPath, patchManifestPath, randomizerVersion,
    placementHash: '', seed: request.combinedSeed, catalogScanned: false };
};

async function packageHashes(directory, base = directory) {
  const out = {};
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) Object.assign(out, await packageHashes(full, base));
    else if (!['run-manifest.json', 'config.json'].includes(entry.name)) out[path.relative(base, full).replaceAll('\\', '/')] = sha(await readFile(full));
  }
  return out;
}

async function generate(root, name, code, appVersion, combinedVersion) {
  const game = path.join(root, `game-${name}`);
  const pristine = path.join(root, `pristine-${name}`);
  const workspace = path.join(root, `workspace-${name}`);
  await installation(game);
  await installation(pristine);
  await mkdir(workspace, { recursive: true });
  const prepared = prepareRun({ ...fullConfig({ enemies: { enabled: false, randomizeRegularEnemies: false, randomizeBosses: false }, fog: { enabled: false } }, { gamePath: game }), seedCode: code });
  assert.equal(prepared.ok, true);
  const result = await generateRun({ config: prepared.config, workspacePath: workspace, profile: prepared.profile, seedCode: prepared.seedCode }, () => {}, {
    getPristineSource: async () => ({ directory: pristine, snapshotId: 'snapshot' }), combined: engine(combinedVersion), appVersion,
  });
  return { prepared, result };
}

test('the application version never changes the world: the same Seed Code gives the same package under any app version', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-profile-'));
  try {
    const code = prepareRun(fullConfig({ enemies: { enabled: false, randomizeRegularEnemies: false, randomizeBosses: false }, fog: { enabled: false } }), () => 'stable-master').seedCode;
    const today = await generate(root, 'today', code, '0.30.0');
    const later = await generate(root, 'later', code, '12.4.7-future');
    assert.equal(today.result.ok, true);
    assert.equal(later.result.ok, true);
    assert.equal(today.result.manifest.appVersion, '0.30.0');
    assert.equal(later.result.manifest.appVersion, '12.4.7-future', 'the app version is recorded but only recorded');
    for (const field of ['generationProfile', 'seedCode', 'masterSeed']) assert.deepEqual(later.result.manifest[field], today.result.manifest[field], field);
    assert.equal(today.result.manifest.generationProfile, 'ds1tr-gen-v4');
    assert.equal(today.result.manifest.seedCode, code);
    assert.deepEqual(later.result.manifest.attempts, today.result.manifest.attempts);
    assert.deepEqual(await packageHashes(later.result.packagePath), await packageHashes(today.result.packagePath));
    assert.deepEqual(later.result.manifest.files.map((file) => [file.path, file.sha256]), today.result.manifest.files.map((file) => [file.path, file.sha256]));
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('an engine that is not on the profile list stops the run instead of silently producing another world', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-profile-'));
  try {
    const code = prepareRun(fullConfig({ enemies: { enabled: false, randomizeRegularEnemies: false, randomizeBosses: false }, fog: { enabled: false } }), () => 'drift-master').seedCode;
    const drifted = await generate(root, 'drift', code, '0.30.0', '0.5.67');
    assert.equal(drifted.result.ok, false);
    assert.equal(drifted.result.code, 'GENERATION_PROFILE_MISMATCH');
    assert.doesNotMatch(drifted.result.message, /0\.5\.6/u, 'the person does not see engine internals');
    assert.match(drifted.result.technical ?? drifted.result.message, /./u);
  } finally { await rm(root, { recursive: true, force: true }); }
});
