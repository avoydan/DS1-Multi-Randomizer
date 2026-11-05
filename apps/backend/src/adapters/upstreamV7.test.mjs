import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { checkProfileComponents, currentGenerationProfile, fogHostRuntimeForProfile, resolveGenerationProfile } from '../../dist/core/generationProfiles.js';
import { legacyFogTrackedItems } from '../../dist/adapters/legacyFogTrackedItems.js';
import { worldDataOptionsForProfile } from '../../dist/adapters/fogPrerequisites.js';
import { parseUpstreamFogProjection, trackedItemsFromUpstreamProjection, UPSTREAM_FOG_PROJECTION_FORMAT } from '../../dist/adapters/upstreamFogProjection.js';
import {
  compactRuntimeWorldDataManifest, hashWorldDataContent, prepareWorldData, upstreamV7AnnotationSha256, upstreamV7BundledAnnotationPath,
  upstreamV7WorldDataId, upstreamV7WorldDataManifest, verifyWorldDataRoot,
} from '../../dist/adapters/worldData.js';

const sha = (bytes) => createHash('sha256').update(bytes).digest('hex');
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..');
const dir = () => mkdtemp(path.join(os.tmpdir(), 'ds1tr-v7-'));

test('ds1tr-gen-v7 is registered, current, and pins the upstream annotation, the data identity and the local reconstruction version', () => {
  const v7 = resolveGenerationProfile('ds1tr-gen-v7');
  assert.ok(v7);
  assert.equal(currentGenerationProfile().id, 'ds1tr-gen-v7');
  assert.equal(v7.fogAnnotationSource, 'upstream-v7');
  assert.equal(v7.fogItemTracking, 'upstream-fog-item-tracking-v7');
  assert.deepEqual(v7.fogEngine.hostVersions, ['0.7.3']);
  assert.equal(v7.fogEngine.fogModCommit, '6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f');
  assert.equal(v7.fogEngine.upstreamAnnotationSha256, upstreamV7AnnotationSha256);
  assert.equal(v7.fogEngine.upstreamAnnotationSha256, '77a10b4015c51bdfbe1499f38344c85540a63bfa1ef6370ad9c1ebdc9a2d2af6');
  assert.equal(v7.fogEngine.worldDataId, upstreamV7WorldDataId, 'the profile pins the computed world data identity');
  assert.equal(v7.fogEngine.localReconstructionVersion, 'ds1tr-local-fog-data-v1');
  assert.deepEqual(fogHostRuntimeForProfile(v7), { hostVersion: '0.7.3', schemaVersion: 3, annotationSource: 'upstream-v7' });
  assert.equal(fogHostRuntimeForProfile({ ...v7, fogEngine: { ...v7.fogEngine, upstreamAnnotationSha256: undefined } }), null, 'without the pinned annotation there is no runtime contract');
  assert.equal(fogHostRuntimeForProfile({ ...v7, fogItemTracking: 'compact-fog-item-tracking-v1' }), null, 'upstream annotations are never paired with the Compact tracking');
  assert.match(checkProfileComponents(v7, { upstreamAnnotationSha256: 'f'.repeat(64) }), /upstream fog\.txt/u);
  assert.equal(checkProfileComponents(v7, { upstreamAnnotationSha256: upstreamV7AnnotationSha256, worldDataId: upstreamV7WorldDataId, fogHostVersion: '0.7.3' }), null);
  // v6 is published and frozen: nothing about it moved.
  const v6 = resolveGenerationProfile('ds1tr-gen-v6');
  assert.equal(v6.fogAnnotationSource, 'compact-v3');
  assert.equal(v6.fogEngine.worldDataId, 'de58b0aaa51c8ce9');
});

test('the V7 data set is the bundled upstream fog.txt plus the 58 locally rebuilt inputs: nothing is downloaded, nothing comes from a legacy cache', () => {
  assert.equal(upstreamV7WorldDataManifest.length, 59);
  const byOrigin = (origin) => upstreamV7WorldDataManifest.filter((entry) => entry.origin === origin);
  assert.equal(byOrigin('upstream').length, 0, 'no file is fetched from the FogMod repository');
  assert.equal(byOrigin('bundled').length, 1);
  assert.equal(byOrigin('game').length, 19);
  assert.equal(byOrigin('reconstructed').length, 39);
  const [bundled] = byOrigin('bundled');
  assert.equal(bundled.path, 'dist/fog.txt');
  assert.equal(bundled.bundledPath, upstreamV7BundledAnnotationPath);
  assert.equal(bundled.sha256, upstreamV7AnnotationSha256);
  assert.deepEqual(upstreamV7WorldDataManifest.filter((entry) => entry.path !== 'dist/fog.txt'), compactRuntimeWorldDataManifest, 'the 58 other inputs are exactly the locally rebuilt set of the offline profiles');
  assert.notEqual(upstreamV7WorldDataId, 'de58b0aaa51c8ce9');
  assert.match(upstreamV7WorldDataId, /^[0-9a-f]{16}$/u);
});

test('the bundled fog.txt is the pinned upstream file, byte for byte, with its provenance note', async () => {
  const bytes = await readFile(path.join(repoRoot, ...upstreamV7BundledAnnotationPath.split('/')));
  assert.equal(bytes.length, 147516);
  assert.equal(hashWorldDataContent(bytes, true), upstreamV7AnnotationSha256);
  // The git blob id GitHub reports for dist/fog.txt at the pinned commit.
  assert.equal(createHash('sha1').update(Buffer.concat([Buffer.from(`blob ${bytes.length}\0`), bytes])).digest('hex'), '83beace06434ce98e33eaeecde86f17c810dd096');
  const note = await readFile(path.join(repoRoot, 'tools', 'fog-host', 'upstream', 'PROVENANCE.md'), 'utf8');
  assert.match(note, /83beace06434ce98e33eaeecde86f17c810dd096/u);
  assert.match(note, /Apache-2\.0/u);
  assert.match(note, /6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f/u);
});

test('world data options for V7 never allow or need a download', () => {
  const v7 = resolveGenerationProfile('ds1tr-gen-v7');
  const options = worldDataOptionsForProfile(v7, 'C:/pristine', { DS1TR_DISTRIBUTION: 'nexus' }, 'C:/cache');
  assert.equal(options.id, upstreamV7WorldDataId);
  assert.equal(options.manifest, upstreamV7WorldDataManifest);
  assert.equal(options.allowUpstreamDownloads, false);
  assert.equal(typeof options.reconstructFiles, 'function');
  assert.equal(options.manifest.some((entry) => entry.origin === 'upstream'), false);
});

test('preparing a V7 data set from nothing works offline: the bundled file is verified and placed, game files are derived, a download is never attempted', async () => {
  const GAME = Buffer.from('map-a');
  const FOG = Buffer.from('Options:\n- Opt: bboc\n');
  const RECON = Buffer.from('rebuilt-event');
  const bundledRoot = await dir();
  await mkdir(path.join(bundledRoot, 'tools', 'fog-host', 'upstream'), { recursive: true });
  await writeFile(path.join(bundledRoot, 'tools', 'fog-host', 'upstream', 'fog.txt'), FOG);
  const pristine = await dir();
  await mkdir(path.join(pristine, 'map', 'MapStudio'), { recursive: true });
  await writeFile(path.join(pristine, 'map', 'MapStudio', 'm10_00_00_00.msb'), GAME);
  const cache = await dir();
  const manifest = [
    { path: 'dist/DS1R/map/MapStudio/m10_00_00_00.msb', sha256: sha(GAME), text: false, size: 5, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m10_00_00_00.msb' },
    { path: 'dist/DS1R/event/common.emevd.dcx', sha256: sha(RECON), text: false, size: 13, kind: 'event', origin: 'reconstructed' },
    { path: 'dist/fog.txt', sha256: sha(FOG), text: true, size: FOG.length, kind: 'fog-annotations', origin: 'bundled', bundledPath: 'tools/fog-host/upstream/fog.txt' },
  ];
  let fetched = 0;
  try {
    const result = await prepareWorldData({
      cacheRoot: cache, pristineDirectory: pristine, manifest, id: 'v7-test', bundledRoot, allowUpstreamDownloads: false,
      fetchBytes: async () => { fetched += 1; throw new Error('the network must not be used'); },
      reconstructFiles: async (_pristine, output) => {
        await mkdir(path.join(output, 'dist', 'DS1R', 'event'), { recursive: true });
        await writeFile(path.join(output, 'dist', 'DS1R', 'event', 'common.emevd.dcx'), RECON);
      },
    });
    assert.equal(result.ok, true, result.ok ? '' : result.technical);
    assert.equal(fetched, 0);
    assert.equal(result.downloaded, 0);
    assert.equal((await verifyWorldDataRoot(result.root, manifest)).ok, true);
    assert.equal((await readFile(path.join(result.root, 'dist', 'fog.txt'))).equals(FOG), true);
    // A second call reuses the verified cache without touching anything.
    const again = await prepareWorldData({ cacheRoot: cache, pristineDirectory: pristine, manifest, id: 'v7-test', bundledRoot, allowUpstreamDownloads: false });
    assert.equal(again.ok && again.reused, true);

    // A damaged bundled file stops the preparation with a plain message, and nothing is installed.
    await writeFile(path.join(bundledRoot, 'tools', 'fog-host', 'upstream', 'fog.txt'), Buffer.from('tampered'));
    const damaged = await prepareWorldData({ cacheRoot: await dir(), pristineDirectory: pristine, manifest, id: 'v7-damaged', bundledRoot, allowUpstreamDownloads: false,
      reconstructFiles: async () => { throw new Error('must not get this far'); } });
    assert.equal(damaged.ok, false);
    assert.equal(damaged.code, 'WORLD_DATA_RECONSTRUCTION_FAILED');
    assert.match(damaged.technical, /does not match the pinned content/u);
  } finally {
    await Promise.all([bundledRoot, pristine, cache].map((root) => rm(root, { recursive: true, force: true })));
  }
});

test('the upstream item model keeps every upstream KeyItem and LotLocations entry and equals what the legacy snapshot froze from the same fog.txt', () => {
  const lots = [1090, 1100, 2500];
  const projection = parseUpstreamFogProjection({
    format: UPSTREAM_FOG_PROJECTION_FORMAT, annotationSha256: upstreamV7AnnotationSha256,
    items: [{ equipType: 3, itemId: 2510, required: true, name: 'lordvessel' }, { equipType: 3, itemId: 3500, required: false, name: 'cast_light' }],
    lotStarts: lots,
  }, upstreamV7AnnotationSha256);
  assert.deepEqual(trackedItemsFromUpstreamProjection(projection), { items: [{ equipType: 3, itemId: 2510, required: true }, { equipType: 3, itemId: 3500, required: false }], lotStarts: lots });
  assert.throws(() => parseUpstreamFogProjection({ ...projection, annotationSha256: 'a'.repeat(64) }, upstreamV7AnnotationSha256), /does not match the pinned fog\.txt/u);
  assert.throws(() => parseUpstreamFogProjection({ ...projection, items: [{ equipType: 3, itemId: 2510, required: false, name: 'x' }] }, upstreamV7AnnotationSha256), /no required key items/u);
  assert.throws(() => parseUpstreamFogProjection({ ...projection, items: [projection.items[0], projection.items[0]] }, upstreamV7AnnotationSha256), /repeats key item/u);
  // The frozen legacy snapshot (profiles v1/v2) came from this very file: 27 key items, 25 required, 440 lots.
  const legacy = legacyFogTrackedItems();
  assert.equal(legacy.items.length, 27);
  assert.equal(legacy.items.filter((item) => item.required).length, 25);
  assert.equal(legacy.lotStarts.length, 440);
});

test('the V7 run reads the upstream item model from the host, not from a catalog: a data root is only read through the projection command', async () => {
  const source = await readFile(path.join(repoRoot, 'apps', 'backend', 'src', 'core', 'generationPipeline.ts'), 'utf8');
  assert.match(source, /upstream-v7[\s\S]{0,400}trackedItemsFromUpstreamProjection/u);
  assert.doesNotMatch(source.slice(source.indexOf("=== 'upstream-v7'"), source.indexOf("=== 'upstream-v7'") + 700), /trackedItemsFromCompactProjection/u);
  const files = await readdir(path.join(repoRoot, 'tools', 'fog-host', 'upstream'));
  assert.deepEqual(files.sort(), ['.gitattributes', 'PROVENANCE.md', 'fog.txt']);
});
