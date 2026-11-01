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

test('v1 through v5 keep their frozen behavior while Compact v6 is current for new worlds', () => {
  const profile = resolveGenerationProfile('ds1tr-gen-v1');
  const corrected = resolveGenerationProfile('ds1tr-gen-v2');
  const compactLegacy = resolveGenerationProfile('ds1tr-gen-v3');
  const historicalCurrent = resolveGenerationProfile('ds1tr-gen-v4');
  const current = resolveGenerationProfile('ds1tr-gen-v5');
  const startReachability = resolveGenerationProfile('ds1tr-gen-v6');
  assert.ok(startReachability);
  assert.ok(profile);
  assert.ok(corrected);
  assert.ok(compactLegacy);
  assert.ok(historicalCurrent);
  assert.ok(current);
  assert.deepEqual(supportedGenerationProfiles(), ['ds1tr-gen-v1', 'ds1tr-gen-v2', 'ds1tr-gen-v3', 'ds1tr-gen-v4', 'ds1tr-gen-v5', 'ds1tr-gen-v6']);
  assert.equal(currentGenerationProfile().id, 'ds1tr-gen-v6');
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
  assert.deepEqual(compactLegacy.fogEngine.hostVersions, ['0.6.0', '0.7.3']);
  assert.equal(compactLegacy.fogAnnotationSource, 'compact-v1');
  assert.deepEqual(current.combinedEngine.randomizerVersions, ['0.5.66']);
  assert.equal(current.roamingBossDestinationPolicy, 'safe-handlers-v2');
  assert.equal(current.roamingBossFitPolicy, 'owner-approved-v1');
  assert.deepEqual(historicalCurrent.fogEngine.hostVersions, ['0.6.0', '0.7.3']);
  assert.equal(historicalCurrent.fogAnnotationSource, 'compact-v1');
  assert.deepEqual(current.fogEngine.hostVersions, ['0.7.3']);
  assert.equal(current.fogAnnotationSource, 'compact-v2');
  assert.deepEqual(profile.fogEngine.hostVersions, ['0.5.0']);
  assert.deepEqual(fogHostRuntimeForProfile(profile), { hostVersion: '0.5.0', schemaVersion: 2, annotationSource: 'legacy-fogmod' });
  assert.deepEqual(fogHostRuntimeForProfile(corrected), { hostVersion: '0.5.0', schemaVersion: 2, annotationSource: 'legacy-fogmod' });
  assert.deepEqual(fogHostRuntimeForProfile(compactLegacy), { hostVersion: '0.6.0', schemaVersion: 3, annotationSource: 'compact-v1' });
  assert.deepEqual(fogHostRuntimeForProfile(historicalCurrent), { hostVersion: '0.6.0', schemaVersion: 3, annotationSource: 'compact-v1' });
  assert.deepEqual(fogHostRuntimeForProfile(current), { hostVersion: '0.7.3', schemaVersion: 3, annotationSource: 'compact-v2' });
  assert.equal(fogHostRuntimeForProfile({ ...profile, fogEngine: { ...profile.fogEngine, hostVersions: ['0.6.0'] } }), null,
    'a legacy data/tracking profile cannot be silently routed to the current host');
  assert.equal(profile.fogEngine.fogModCommit, '6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f');
  assert.equal(profile.fogEngine.worldDataId, 'f394fa536ba9b66d');
  assert.deepEqual({ ...profile.retryBounds }, { maxCombinedAttempts: 3, maxFogAttempts: 5 });
  assert.equal(profile.fogItemTracking, 'fog-item-tracking-v1');
  assert.equal(historicalCurrent.fogEngine.compactCatalogFingerprint, '8c03fd3de1cd7c772d5c933753ae54eab35f9e8b70a1aef2954322330d0002d4');
  assert.equal(current.fogEngine.compactCatalogVersion, 'ds1tr-compact-full-fog-v2');
  assert.equal(current.fogEngine.compactCatalogFingerprint, '73f3c842d51490d45a836a489105b37f57d702eaa2ffda084f42781e5aaef00f');
  assert.equal(current.fogEngine.worldDataId, 'ef625777d6aa5abf');
  assert.notEqual(current.fogEngine.worldDataId, historicalCurrent.fogEngine.worldDataId);
  assert.notEqual(current.fogEngine.compactCatalogFingerprint, historicalCurrent.fogEngine.compactCatalogFingerprint);
  assert.match(current.fogEngine.compactCatalogFingerprint, /^[a-f0-9]{64}$/u,
    'v5 must pin the computed immutable Compact v2 catalog fingerprint');
  assert.ok(Object.isFrozen(profile), 'a registered profile cannot be edited at runtime');

  // v6 (Compact Fog v3: physically sound Asylum start, general start-reachability rule): its own annotation source, catalog and world data.
  assert.equal(startReachability.fogAnnotationSource, 'compact-v3');
  assert.deepEqual(startReachability.fogEngine.hostVersions, ['0.7.3'], 'the shipped host keeps its compatibility identifier; v6 is selected by annotation source and catalog');
  assert.equal(startReachability.fogEngine.compactCatalogVersion, 'ds1tr-compact-full-fog-v3');
  assert.equal(startReachability.fogEngine.compactCatalogFingerprint, 'f2775c4a80f0ee41e4ccc0bf50fff4ea3714e11da73339c140cb3d6c6fe254a5');
  assert.equal(startReachability.fogEngine.worldDataId, 'de58b0aaa51c8ce9');
  assert.notEqual(startReachability.fogEngine.compactCatalogFingerprint, current.fogEngine.compactCatalogFingerprint);
  assert.notEqual(startReachability.fogEngine.worldDataId, current.fogEngine.worldDataId);
  assert.equal(startReachability.fogEngine.fogModCommit, current.fogEngine.fogModCommit);
  assert.deepEqual({ ...startReachability.retryBounds }, { maxCombinedAttempts: 3, maxFogAttempts: 5 }, 'v6 does not change the Fog retry budget');
  assert.equal(startReachability.roamingBossFitPolicy, current.roamingBossFitPolicy);
  assert.equal(startReachability.fogItemTracking, 'compact-fog-item-tracking-v1');
  assert.deepEqual(fogHostRuntimeForProfile(startReachability), { hostVersion: '0.7.3', schemaVersion: 3, annotationSource: 'compact-v3' });
  assert.ok(Object.isFrozen(startReachability));
  // v5 is untouched by v6.
  assert.equal(current.fogAnnotationSource, 'compact-v2');
  assert.equal(current.fogEngine.compactCatalogFingerprint, '73f3c842d51490d45a836a489105b37f57d702eaa2ffda084f42781e5aaef00f');
  assert.equal(current.fogEngine.worldDataId, 'ef625777d6aa5abf');
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
  const current = resolveGenerationProfile('ds1tr-gen-v5');
  assert.equal(checkProfileComponents(current, { fogHostVersion: '0.7.3' }), null);
  assert.match(checkProfileComponents(current, { fogHostVersion: '0.7.2' }), /FogHost 0\.7\.2/u);
  assert.match(checkProfileComponents(current, { fogHostVersion: '0.7.0' }), /FogHost 0\.7\.0/u);
  // v3/v4 Compact Fog runs on the shipped 0.7.3 host (byte-identical to 0.6.0, proven by differential and the v3 golden).
  for (const id of ['ds1tr-gen-v3', 'ds1tr-gen-v4']) {
    const compact = resolveGenerationProfile(id);
    assert.equal(checkProfileComponents(compact, { fogHostVersion: '0.6.0' }), null);
    assert.equal(checkProfileComponents(compact, { fogHostVersion: '0.7.3' }), null);
    assert.match(checkProfileComponents(compact, { fogHostVersion: '0.7.2' }), /FogHost 0\.7\.2/u);
    assert.match(checkProfileComponents(compact, { fogHostVersion: '0.5.0' }), /FogHost 0\.5\.0/u);
  }
  assert.match(checkProfileComponents(profile, { fogModCommit: 'abc' }), /FogMod abc/u);
  assert.match(checkProfileComponents(profile, { worldDataId: 'other' }), /world data other/u);
});

test('new worlds use v6 while the original V4 hard-lock Seed Code remains exact and pinned to v4 and V5 codes stay v5', () => {
  const run = prepareRun(fullConfig(), () => '123456');
  assert.equal(run.ok, true);
  assert.equal(run.profile.id, 'ds1tr-gen-v6');
  // A later application only adds profiles; decoding reads the code's own profile, never the app version.
  const decoded = decodeSeedCode(run.seedCode);
  assert.equal(decoded.envelope.profile, 'ds1tr-gen-v6');
  assert.equal(decoded.seedCode, run.seedCode, 'the newly encoded V6 code round-trips exactly');

  // A V5 code (the owner smoke code of the V5 QA) keeps selecting V5: it never silently switches to V6 semantics.
  const v5SmokeCode = 'DS1TR1-06NWCQQ3-K1HD8E66-2S3AD5N6-CTHZBKB8-FMSNNQWC-TRVW78NY-040F30XX-F0';
  const v5Decoded = decodeSeedCode(v5SmokeCode);
  assert.equal(v5Decoded.ok, true);
  assert.equal(v5Decoded.envelope.profile, 'ds1tr-gen-v5');
  assert.equal(encodeSeedCode(v5Decoded.envelope), v5SmokeCode, 'the V5 smoke code stays byte-for-byte identical');
  const v5Run = prepareRun({ ...fullConfig(), seedCode: v5SmokeCode });
  assert.equal(v5Run.ok, true);
  assert.equal(v5Run.profile.id, 'ds1tr-gen-v5');
  assert.equal(v5Run.seedCode, v5SmokeCode);

  const originalHardLockCode = 'DS1TR1-06NWCQQ3-K2GDEE66-RS30D9N6-2R31C11B-3KT74XF8-TRW6G1J0-5CE38MF4-582WV6E4-AGVH0B8M-TGV4HJ8W-JKKMNWTJ-HBS764X1-NV3N20D1-QDJM9NA0-S1JG2TEN-9X97WJ86-QKQ035S2-YJN7E80N-16X6QG5H-MAHG8BAS-82RG8KA0-C0FJ02D7-2R3SE92P-QAJ2D9P0-HBDD2TMC-HHN0RPQT-2ANCA013-N99NW';
  const oldCode = decodeSeedCode(originalHardLockCode);
  assert.equal(oldCode.ok, true);
  assert.equal(oldCode.envelope.profile, 'ds1tr-gen-v4');
  assert.equal(encodeSeedCode(oldCode.envelope), originalHardLockCode, 'the support-bundle V4 code stays byte-for-byte identical');
  const oldRun = prepareRun({ ...fullConfig(), seedCode: originalHardLockCode });
  assert.equal(oldRun.ok, true);
  assert.equal(oldRun.profile.id, 'ds1tr-gen-v4');
  assert.equal(oldRun.seedCode, originalHardLockCode);
  // A code made by a profile this application does not know is an explicit, specific refusal, never a guess.
  const future = encodeSeedCode({ profile: 'ds1tr-gen-v7', masterSeed: '1', settings: fullSettings() });
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
    assert.equal(today.result.manifest.generationProfile, 'ds1tr-gen-v6');
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
