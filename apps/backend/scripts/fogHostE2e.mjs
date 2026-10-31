// FogHost end-to-end proof: combined Items + Enemies -> FogHost -> package, on real-format data.
//
// pristine import -> generateRun (combined + Fog through the built FogHost) -> deterministic rerun
// -> activate the Fog package -> restore, through the same GameStateAuthority and pipeline the
// server uses. Boss randomization stays off (FogHost v1 rejects it together with Fog).
//
// The live installation is only read. As in releaseAuditE2e.mjs, a reconstruction of its managed
// roots (legacy `X.bak` moved over `X`) in the OS temp directory is the "game". Game-derived
// output and FogMod data never enter the repository.
//
// Not part of `npm test`: needs a real installation, built DsrDataTool and FogHost, and
// network access for the one-time world data download (or DS1TR_FOG_DATA_ROOT as a developer override). By default the
// run uses the application's own preparation (worldData.ts) in DS1TR_STATE_DIR, exactly like the packaged app.
//
// Usage (after `npm run build -w @ds1tr/backend` and building tools/fog-host in Release):
//   node apps/backend/scripts/fogHostE2e.mjs "<live DSR directory>"   (set DS1TR_STATE_DIR to an empty folder to test the first-run download)
//   DS1TR_E2E_KEEP=1 keeps the temp directory.
//   DS1TR_E2E_ENEMIES=0 runs Items + Fog only; DS1TR_E2E_SEED sets the master seed.
//   DS1TR_E2E_SWEEP=<n> runs <n> seeds (`<DS1TR_E2E_SEED>-<i>`) generation-only and prints
//   per-attempt statistics, merge conflicts and instruction-level merges instead of the full proof.
//   DS1TR_E2E_FOG=0 disables Fog (combined-only regression sweep).
//   DS1TR_E2E_BOSSES=1 enables boss randomization.
//   DS1TR_E2E_VERIFY_SKIPS=<DsrDataTool.Tests.exe> checks every combined package's EMEVD skip ranges
//   against the pristine snapshot (`verify-skips`, engines/combined/tests/DsrDataTool.Tests).

import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { cp, mkdir, mkdtemp, readFile, readdir, rename, rm, stat, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { currentGenerationProfile } from '../dist/core/generationProfiles.js';
import { defaultFogHostExecutable, defaultWorldDataCacheRoot } from '../dist/adapters/fogPrerequisites.js';
import { COMPACT_FOG_CATALOG_V2_FINGERPRINT } from '../dist/adapters/compactFogProjection.js';
import { worldDataId } from '../dist/adapters/worldData.js';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const backendPackage = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'));
const generationProfile = currentGenerationProfile();
const attestationDirectory = process.env.DS1TR_E2E_ATTEST_DIR ? path.resolve(process.env.DS1TR_E2E_ATTEST_DIR) : null;
const compactFogDoc = await readFile(path.join(repoRoot, 'docs', 'COMPACT_FULL_FOG_V2.md'), 'utf8');
const semanticFingerprintMatch = compactFogDoc.match(/old\/current semantic fingerprints are `([0-9a-f]{64})` and `([0-9a-f]{64})`/u);
if (!semanticFingerprintMatch) throw new Error('Could not resolve the current Compact Fog V2 semantic fingerprint from docs/COMPACT_FULL_FOG_V2.md.');
const compactFogSemanticFingerprint = semanticFingerprintMatch[2];
function projectVersion(text, label) {
  const match = text.match(/<Version>([^<]+)<\/Version>/u);
  if (!match) throw new Error(`Could not resolve ${label} project version.`);
  return match[1];
}
const fogHostProjectVersion = projectVersion(await readFile(path.join(repoRoot, 'tools', 'fog-host', 'FogHost.csproj'), 'utf8'), 'FogHost');
const fogReconstructionProjectVersion = projectVersion(await readFile(path.join(repoRoot, 'tools', 'fog-reconstruction', 'FogReconstruction.csproj'), 'utf8'), 'FogReconstruction');
const selectedFogHostExecutable = path.resolve(process.env.DS1TR_FOG_HOST || defaultFogHostExecutable);
const hostVersionSource = await readFile(path.join(repoRoot, 'tools', 'fog-host', 'src', 'Program.cs'), 'utf8');
const hostVersionMatch = hostVersionSource.match(/public const string HostVersion = "([^"]+)";/u);
if (!hostVersionMatch) throw new Error('Could not resolve FogHost protocol compatibility version from Program.cs.');
const fogHostHostVersion = hostVersionMatch[1];

const livePath = process.argv[2] ?? process.env.DS1TR_E2E_GAME_PATH;
if (!livePath) {
  console.error('Usage: fogHostE2e.mjs <live DSR directory>');
  process.exit(2);
}

let failed = false;
function check(name, condition, detail = '') {
  if (!condition) failed = true;
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${name}${detail ? ` — ${detail}` : ''}`);
}

async function hashFile(file) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(file)) hash.update(chunk);
  return hash.digest('hex');
}

async function listTree(root, relative) {
  const files = [];
  let entries;
  try { entries = await readdir(path.join(root, relative), { withFileTypes: true }); } catch { return files; }
  for (const entry of entries) {
    const child = `${relative}/${entry.name}`;
    if (entry.isDirectory()) files.push(...await listTree(root, child));
    else files.push(child);
  }
  return files;
}

async function treeInventory(root, relative = '') {
  const files = [];
  let entries;
  try { entries = await readdir(path.join(root, relative), { withFileTypes: true }); }
  catch (error) {
    if (error?.code === 'ENOENT') return files;
    throw error;
  }
  for (const entry of entries) {
    const child = relative ? `${relative}/${entry.name}` : entry.name;
    const target = path.join(root, child);
    if (entry.isDirectory()) files.push(...await treeInventory(root, child));
    else {
      const info = await stat(target);
      files.push({ path: child.replaceAll('\\', '/'), size: info.size, sha256: await hashFile(target) });
    }
  }
  return files.sort((a, b) => a.path.localeCompare(b.path, 'en'));
}

function inventoryDigest(files) {
  return createHash('sha256').update(files.map((file) => `${file.path}\0${file.size}\0${file.sha256}\n`).join('')).digest('hex');
}

function gitOutput(args) {
  const result = spawnSync('git', args, { cwd: repoRoot, encoding: 'utf8' });
  if (result.status !== 0) throw new Error(`git ${args.join(' ')} failed: ${result.stderr || result.error}`);
  return result.stdout.trim();
}

let attestationBase = null;

async function attestRun(name, result, workspace, runConfig, diagnostics = null) {
  if (!attestationDirectory || !attestationBase) return;
  const slug = name.replace(/[^A-Za-z0-9._-]/gu, '_');
  const caseDirectory = path.join(attestationDirectory, slug);
  await mkdir(caseDirectory, { recursive: false });
  await mkdir(workspace, { recursive: true });
  const workspaceFiles = await treeInventory(workspace);
  const packageFiles = result.ok ? await treeInventory(result.packagePath) : [];
  const fogDataRoot = process.env.DS1TR_FOG_DATA_ROOT
    ? path.resolve(process.env.DS1TR_FOG_DATA_ROOT)
    : path.join(defaultWorldDataCacheRoot(), generationProfile.fogEngine.worldDataId);
  const fogDataFiles = await treeInventory(fogDataRoot);
  attestationBase = {
    ...attestationBase,
    compactFogDataRoot: fogDataRoot,
    compactFogDataTreeSha256: inventoryDigest(fogDataFiles),
    compactFogDataFiles: fogDataFiles,
  };
  await writeFile(path.join(attestationDirectory, 'audit.json'), `${JSON.stringify(attestationBase, null, 2)}\n`);
  const runManifest = result.ok ? JSON.parse(await readFile(path.join(result.packagePath, 'run-manifest.json'), 'utf8')) : null;
  const fogAttemptLogs = [];
  for (const directory of await readdir(workspace).catch(() => [])) {
    if (!/^fog-\d+-\d+$/u.test(directory)) continue;
    const logPath = path.join(workspace, directory, 'foghost.log');
    const content = await readFile(logPath, 'utf8').catch(() => null);
    if (content !== null) fogAttemptLogs.push({
      attemptDirectory: directory,
      sha256: createHash('sha256').update(content, 'utf8').digest('hex'),
      content,
    });
  }
  const retainedPath = path.join(caseDirectory, result.ok ? 'package' : 'attempt-workspace');
  await cp(result.ok ? result.packagePath : workspace, retainedPath, { recursive: true, errorOnExist: true, force: false });
  const attestation = {
    ...attestationBase,
    attestationClass: 'CONTEMPORANEOUS_HASH_ATTESTATION',
    caseName: name,
    generation: {
      masterSeed: runConfig.masterSeed,
      options: {
        items: runConfig.items,
        enemies: runConfig.enemies,
        fog: runConfig.fog,
        output: runConfig.output,
      },
      seedCode: result.manifest.seedCode ?? null,
    },
    result: {
      ok: result.ok,
      code: result.ok ? 'success' : result.code,
      stage: result.ok ? null : result.stage,
      message: result.ok ? null : result.message,
      packagePath: result.ok ? result.packagePath : null,
      attempts: result.manifest.attempts,
      engineVersions: result.manifest.engines,
      validation: runManifest?.validation ?? null,
      packageManifestSha256: result.ok ? await hashFile(path.join(result.packagePath, 'run-manifest.json')) : null,
      packageTreeSha256: result.ok ? inventoryDigest(packageFiles) : null,
      packageFiles,
    },
    diagnostics: diagnostics ? {
      validatorResult: result.ok ? 'accepted' : result.code,
      validatorStage: result.ok ? null : result.stage,
      validatorMessage: result.ok ? null : result.message,
      skipChecks: diagnostics.skipChecks ?? [],
      fogCombinedEventMismatches: diagnostics.fogCombinedEventMismatches ?? [],
      fogModOnlyEventMismatches: diagnostics.fogModOnlyEventMismatches ?? [],
      mergeConflicts: diagnostics.conflicts ?? [],
      instructionMerges: diagnostics.merges ?? [],
      bossOverlays: diagnostics.overlays ?? [],
    } : null,
    fogAttemptLogs,
    workspace: {
      sourcePath: workspace,
      retainedPath,
      treeSha256: inventoryDigest(workspaceFiles),
      fileCount: workspaceFiles.length,
      files: workspaceFiles,
    },
  };
  await writeFile(path.join(caseDirectory, 'attestation.json'), `${JSON.stringify(attestation, null, 2)}\n`, { flag: 'wx' });
  console.log(`attested ${name}: result=${attestation.result.code}; packageFiles=${packageFiles.length}; workspaceFiles=${workspaceFiles.length}; workspaceSha256=${attestation.workspace.treeSha256}`);
}

async function hashRoots(root) {
  const result = new Map([[executableName, await hashFile(path.join(root, executableName))]]);
  for (const managed of managedRoots) {
    for (const relative of await listTree(root, managed)) result.set(relative, await hashFile(path.join(root, relative)));
  }
  return result;
}

function sameMap(a, b) {
  if (a.size !== b.size) return false;
  for (const [k, v] of a) if (b.get(k) !== v) return false;
  return true;
}

const config = (gamePath, masterSeed = process.env.DS1TR_E2E_SEED ?? 'foghost-e2e-1') => ({
  gamePath,
  masterSeed,
  items: {
    enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true,
    guaranteeEarlyWeapon: true, balancedEarlyLoot: true, randomizeStartingClass: false,
    randomizeStartingEquipment: false, randomizeGifts: false, randomizeEnemyDrops: false,
    guaranteedEnemyDrops: false, randomizeShops: false,
    fashionSouls: process.env.DS1TR_E2E_FASHION !== '0',
    soulItems: process.env.DS1TR_E2E_SOULS === 'replaced' ? 'replaced' : 'shuffled',
    startingItems: process.env.DS1TR_E2E_STARTING ?? 'shield-2h',
    // DS1TR_E2E_ITEMS='{"randomizeShops":true}' overrides item settings (category matrix).
    ...(process.env.DS1TR_E2E_ITEMS ? JSON.parse(process.env.DS1TR_E2E_ITEMS) : {}),
  },
  // DS1TR_E2E_ENEMIES=0 runs Items + Fog only.
  enemies: { enabled: process.env.DS1TR_E2E_ENEMIES !== '0', randomizeRegularEnemies: true, randomizeBosses: process.env.DS1TR_E2E_BOSSES === '1', scaling: process.env.DS1TR_E2E_SCALING ?? 'area',
    replaceMimics: process.env.DS1TR_E2E_MIMICS !== '0',
    replaceRespawningMosquitoes: process.env.DS1TR_E2E_MOSQUITOES !== '0',
    preventSameEnemy: process.env.DS1TR_E2E_PREVENT_SAME === '1',
    pinwheelChaos: process.env.DS1TR_E2E_PINWHEEL === '1',
    gwynNerf: process.env.DS1TR_E2E_GWYN ?? 'medium',
    secondGargoyle: process.env.DS1TR_E2E_GARGOYLE ?? 'replace',
    // DS1TR_E2E_PLACEMENT='{"algorithm":"sampling-v1","replacementChance":100,"normalReplacement":"normals","fit":"fits"}'
    placement: process.env.DS1TR_E2E_PLACEMENT ? JSON.parse(process.env.DS1TR_E2E_PLACEMENT)
      : { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null } },
  fog: {
    enabled: process.env.DS1TR_E2E_FOG !== '0', traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: false,
    minorPvpFogs: false, lordvesselGates: false, randomStart: false, disconnectedFogs: false,
    noFallBedOfChaos: false, scaling: true, glitchedLogic: false, requireLordSouls: true,
    pacifistMode: false, language: 'ENGLISH',
    ...(process.env.DS1TR_E2E_FOG_OPTIONS ? JSON.parse(process.env.DS1TR_E2E_FOG_OPTIONS) : {}),
  },
  output: { includeDlc: true, generateSpoilerLog: false },
});

class SweepDone extends Error {}

/** Generation-only statistics over many seeds (one reconstruction and snapshot). */
async function sweep(count, prefix) {
  const results = [];
  for (let i = 1; i <= count; i++) {
    const seed = `${prefix}-${i}`;
    const workspace = path.join(temp, `sweep-${i}`);
    // DS1TR_E2E_LOGS=1 prints every log event (diagnosing a failing seed).
    const runConfig = config(game, seed);
    const result = await generateRun({ config: runConfig, workspacePath: workspace, profile: generationProfile },
      (event) => { if (process.env.DS1TR_E2E_LOGS && event.type === 'log') console.log(`  [${event.stage}] ${event.level}: ${event.message}`); }, deps);
    const merges = [];
    const overlays = [];
    for (const directory of await readdir(workspace).catch(() => [])) {
      const log = await readFile(path.join(workspace, directory, 'foghost.log'), 'utf8').catch(() => '');
      for (const match of log.matchAll(/merged (event\/\S+) event (\d+) at instruction level/g)) merges.push(`${match[1]}:${match[2]}`);
      for (const match of log.matchAll(/boss overlay applied to (event\/\S+) event (\d+)/g)) overlays.push(`${match[1]}:${match[2]}`);
    }
    const conflicts = result.ok || result.code !== 'FOG_MERGE_CONFLICT' ? []
      : [...result.message.matchAll(/event\/(\S+?): event (\d+)[^(;]*\(([^)]*)\)/g)].map((m) => `${m[1]}:${m[2]} (${m[3]})`);
    const entry = {
      seed, ok: result.ok, code: result.ok ? 'success' : result.code,
      attempts: result.manifest.attempts.map((a) => `${a.combinedAttempt}/${a.fogAttempt}:${a.result}`),
      conflicts,
      merges: [...new Set(merges)],
      overlays: [...new Set(overlays)],
      ...(result.ok ? {} : { message: result.message.slice(0, 6000) }),
    };
    // sampling-v1: budget and drop-ownership checks on the plan of the last combined attempt.
    const placementInfo = result.manifest.enemyPlacement;
    if (placementInfo.summary) {
      const maps = Object.values(placementInfo.summary.maps);
      entry.placement = {
        classTableHash: placementInfo.classTableHash?.slice(0, 12),
        replaced: maps.reduce((total, map) => total + map.replaced, 0),
        slots: placementInfo.summary.slots,
        maxFinalModels: Math.max(0, ...maps.map((map) => map.finalModels)),
        mapsAboveCap: maps.filter((map) => map.baseline > 30).length,
        graceMaps: Object.entries(placementInfo.summary.maps).filter(([, map]) => map.customPoolModelGrace).map(([id, map]) => `${id}:${map.finalModels}`),
        // Cap invariant: the effective cap is max(30, baseline), or exactly 31 for a map on the proven one-model grace
        // (baseline <= 30); an existing overflow is never raised.
        capInvariantBad: maps.filter((map) => (map.customPoolModelGrace
          ? map.effectiveCap !== 31 || map.baseline > 30 || map.minimumFeasibleModels !== 31
          : map.effectiveCap !== Math.max(30, map.baseline)) || (map.baseline > 30 && map.finalModels > map.baseline)).length,
        samplerOverflow: maps.filter((map) => map.finalModels > map.effectiveCap).length,
        stages: placementInfo.summary.stages,
        mapsRetried: placementInfo.summary.counters.mapsRetried ?? 0,
        mapAttemptsFailed: placementInfo.summary.counters.mapAttemptsFailed ?? 0,
        difficultyLapses: placementInfo.summary.counters.difficultyLapses ?? 0,
        typeGroups: placementInfo.summary.counters.typeGroups ?? 0,
        typeGroupsShared: placementInfo.summary.counters.typeGroupsShared ?? 0,
        retries: placementInfo.summary.retries ?? [],
      };
      const last = (await readdir(workspace).catch(() => [])).filter((d) => d.startsWith('combined-')).sort().pop();
      const seedDirs = last ? (await readdir(path.join(workspace, last)).catch(() => [])).filter((d) => d.startsWith('seed_')) : [];
      if (seedDirs.length > 0) {
        const plan = JSON.parse(await readFile(path.join(workspace, last, seedDirs[0], 'randomizer.json'), 'utf8'));
        const sampled = plan.placements.enemies.filter((p) => /sampled|^pinwheel|passive-until|grounded-new-londo|active-darkroot/u.test(p.compatibility) && p.changed);
        entry.placement.sampledChanged = sampled.length;
        entry.placement.changedWithoutScaledRow = sampled.filter((p) => !Number.isInteger(p.scaledNpcParamId)).length;
        const customPool = process.env.DS1TR_E2E_PLACEMENT ? JSON.parse(process.env.DS1TR_E2E_PLACEMENT).customPool : null;
        entry.placement.poolViolations = Array.isArray(customPool) ? sampled.filter((p) => !customPool.includes(p.targetModelName)).length : 0;
      }
    }
    let combinedEdited = new Set();
    if (process.env.DS1TR_E2E_VERIFY_SKIPS) {
      const pristine = (await authority.getPristineSource(game)).directory;
      entry.skipChecks = [];
      for (const attempt of (await readdir(workspace).catch(() => [])).filter((d) => d.startsWith('combined-')).sort()) {
        for (const seedDir of (await readdir(path.join(workspace, attempt)).catch(() => [])).filter((d) => d.startsWith('seed_'))) {
          const run = spawnSync(process.env.DS1TR_E2E_VERIFY_SKIPS,
            ['verify-skips', path.join(pristine, 'event'), path.join(workspace, attempt, seedDir, 'mod', 'event'), '--list-edited'],
            { encoding: 'utf8' });
          entry.skipChecks.push(`${attempt}: exit ${run.status} ${(run.stdout.match(/verify-skips: .*/) ?? ['no output'])[0]}`);
          if (run.status !== 0) console.log(run.stdout);
          // The last combined attempt is the one the Fog run consumed.
          combinedEdited = new Set(run.stdout.split(/\r?\n/).filter((l) => l.startsWith('EDITED ')).map((l) => l.slice(7).trim()));
        }
      }
    }
    if (process.env.DS1TR_E2E_VERIFY_SKIPS && result.ok && process.env.DS1TR_E2E_FOG !== '0') {
      // Final Fog package against FogMod's own event templates: every skip the combined engine or the
      // merge touched must be intact. Mismatches in events the combined engine never edited are
      // FogMod's own runtime edits (stock FogMod behavior) and are reported separately.
      const run = spawnSync(process.env.DS1TR_E2E_VERIFY_SKIPS,
        ['verify-skips', path.join(process.env.DS1TR_FOG_DATA_ROOT || path.join(defaultWorldDataCacheRoot(), worldDataId), 'dist', 'DS1R', 'event'), path.join(result.packagePath, 'event')],
        { encoding: 'utf8' });
      const mismatched = [...new Set(run.stdout.split(/\r?\n/).filter((l) => l.startsWith('MISMATCH '))
        .map((l) => l.slice(9).split(' ')[0]))];
      entry.fogCombinedEventMismatches = mismatched.filter((id) => combinedEdited.has(id));
      entry.fogModOnlyEventMismatches = mismatched.filter((id) => !combinedEdited.has(id));
    }
    results.push(entry);
    console.log(JSON.stringify(entry));
    await attestRun(`sweep-${i}-${seed}`, result, workspace, runConfig, entry);
    await rm(workspace, { recursive: true, force: true });
  }
  const attempts = results.flatMap((r) => r.attempts);
  const tally = (list) => list.reduce((acc, key) => ({ ...acc, [key]: (acc[key] ?? 0) + 1 }), {});
  console.log(JSON.stringify({
    seeds: results.length,
    success: results.filter((r) => r.ok).length,
    finalCodes: tally(results.map((r) => r.code)),
    attemptResults: tally(attempts.map((a) => a.split(':')[1])),
    seedsNeedingCombinedRetry: results.filter((r) => r.attempts.some((a) => a.endsWith('FOG_UPSTREAM_INCOMPATIBLE'))).length,
    conflictEvents: tally(results.flatMap((r) => r.conflicts)),
    instructionMerges: tally(results.flatMap((r) => r.merges)),
    bossOverlays: tally(results.flatMap((r) => r.overlays)),
    mapsRetried: results.reduce((total, r) => total + (r.placement?.mapsRetried ?? 0), 0),
    typeGroups: results.reduce((total, r) => total + (r.placement?.typeGroups ?? 0), 0),
    typeGroupsShared: results.reduce((total, r) => total + (r.placement?.typeGroupsShared ?? 0), 0),
    seedsWithMapRetry: results.filter((r) => r.placement?.mapsRetried).length,
    samplerOverflow: results.reduce((total, r) => total + (r.placement?.samplerOverflow ?? 0), 0),
    changedWithoutScaledRow: results.reduce((total, r) => total + (r.placement?.changedWithoutScaledRow ?? 0), 0),
    maxFinalModels: Math.max(0, ...results.map((r) => r.placement?.maxFinalModels ?? 0)),
    graceMaps: results.flatMap((r) => (r.placement?.graceMaps ?? []).map((m) => `${r.seed}:${m}`)),
    capInvariantBad: results.reduce((total, r) => total + (r.placement?.capInvariantBad ?? 0), 0),
    poolViolations: results.reduce((total, r) => total + (r.placement?.poolViolations ?? 0), 0),
    mapsAboveCap: results.reduce((total, r) => total + (r.placement?.mapsAboveCap ?? 0), 0),
    skipChecksFailed: results.flatMap((r) => r.skipChecks ?? []).filter((c) => !c.includes('exit 0')).length,
    skipChecks: results.flatMap((r) => r.skipChecks ?? []).length,
    fogCombinedEventMismatches: tally(results.flatMap((r) => r.fogCombinedEventMismatches ?? [])),
    fogModOnlyEventMismatches: tally(results.flatMap((r) => r.fogModOnlyEventMismatches ?? [])),
  }, null, 2));
}

const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-fog-e2e-'));
const game = path.join(temp, 'game');
const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
const deps = {
  getPristineSource: (gamePath) => authority.getPristineSource(gamePath),
  appVersion: backendPackage.version,
};

try {
  const liveBefore = await hashRoots(livePath);
  console.log(`live read set: ${liveBefore.size} files`);

  // DS1TR_E2E_GAME_SOURCE: a clean copy of the game (for example the application's pristine snapshot) to use instead of the
  // live installation, which is not clean once the app (or another tool) has applied a randomization to it. Read only.
  const source = process.env.DS1TR_E2E_GAME_SOURCE ?? livePath;
  const sourceBefore = source === livePath ? liveBefore : await hashRoots(source);
  if (attestationDirectory) {
    const relative = path.relative(repoRoot, attestationDirectory);
    if (relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative)))
      throw new Error('DS1TR_E2E_ATTEST_DIR must be outside the repository.');
    await mkdir(attestationDirectory, { recursive: true });
    const qaInputFiles = await Promise.all([...sourceBefore].map(async ([relativePath, sha256]) => ({
      path: relativePath,
      size: (await stat(path.join(source, relativePath))).size,
      sha256,
    })));
    const fogDataRoot = process.env.DS1TR_FOG_DATA_ROOT
      ? path.resolve(process.env.DS1TR_FOG_DATA_ROOT)
      : path.join(defaultWorldDataCacheRoot(), generationProfile.fogEngine.worldDataId);
    const fogDataFiles = await treeInventory(fogDataRoot);
    attestationBase = {
      schema: 'ds1tr-ceaseless-e2e-attestation-v2',
      attestedAt: new Date().toISOString(),
      externalSignature: 'NONE; this is a local contemporaneous hash attestation, not an externally trusted signature.',
      repository: repoRoot,
      gitHead: gitOutput(['rev-parse', 'HEAD']),
      gitStatusPorcelain: gitOutput(['status', '--porcelain=v1']),
      harnessPath: path.relative(repoRoot, fileURLToPath(import.meta.url)).replaceAll('\\', '/'),
      harnessSha256: await hashFile(fileURLToPath(import.meta.url)),
      backendVersion: backendPackage.version,
      generationProfile: generationProfile.id,
      fogModCommit: generationProfile.fogEngine.fogModCommit,
      fogHostVersions: generationProfile.fogEngine.hostVersions,
      worldDataId: generationProfile.fogEngine.worldDataId,
      catalogFingerprint: COMPACT_FOG_CATALOG_V2_FINGERPRINT,
      semanticFingerprint: compactFogSemanticFingerprint,
      fogHostProjectVersion,
      fogHostHostVersion,
      fogHostCompatibleVersions: generationProfile.fogEngine.hostVersions,
      fogReconstructionProjectVersion,
      hostExecutable: selectedFogHostExecutable,
      hostExecutableSha256: await hashFile(selectedFogHostExecutable),
      qaSourceRoot: source,
      qaSourceTreeSha256: inventoryDigest(qaInputFiles),
      qaInputFiles,
      compactFogDataRoot: fogDataRoot,
      compactFogDataTreeSha256: inventoryDigest(fogDataFiles),
      compactFogDataFiles: fogDataFiles,
    };
    await writeFile(path.join(attestationDirectory, 'audit.json'), `${JSON.stringify(attestationBase, null, 2)}\n`, { flag: 'wx' });
  }
  await cp(path.join(source, executableName), path.join(game, executableName));
  for (const managed of managedRoots) {
    await cp(path.join(source, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
  }
  for (const managed of managedRoots) {
    for (const relative of await listTree(game, managed)) {
      if (!/\.bak$/i.test(relative)) continue;
      await rename(path.join(game, relative), path.join(game, relative.slice(0, -4)));
    }
  }
  const reconstructed = await hashRoots(game);

  const imported = await authority.importPristine(game);
  check('pristine import', imported.ok, imported.ok ? `${imported.snapshot.files.length} files` : imported.code);

  if (process.env.DS1TR_E2E_SWEEP) {
    await sweep(Number(process.env.DS1TR_E2E_SWEEP), process.env.DS1TR_E2E_SEED ?? 'sweep');
    check('live installation unchanged', sameMap(await hashRoots(livePath), liveBefore));
    throw new SweepDone();
  }

  const logs = [];
  const started = Date.now();
  const firstConfig = config(game);
  const first = await generateRun({ config: firstConfig, workspacePath: path.join(temp, 'run-a'), profile: generationProfile },
    (event) => { if (event.type === 'log' && event.stage === 'fog' && event.level !== 'info') logs.push(event.message); }, deps);
  await attestRun('full-run-a', first, path.join(temp, 'run-a'), firstConfig);
  check('combined + Fog generation succeeds', first.ok,
    first.ok ? `${first.packagePath} in ${Math.round((Date.now() - started) / 1000)} s` : `${first.code}: ${first.message}`);
  console.log(`attempts: ${JSON.stringify(first.manifest.attempts.map((a) => `${a.combinedAttempt}/${a.fogAttempt}:${a.result}`))}`);
  if (!first.ok) throw new Error('generation failed');
  const m = first.manifest;
  check('manifest records FogHost and pinned FogMod', /6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f/.test(m.engines.fog ?? ''), m.engines.fog);
  check('Fog output hashes verified', m.validation.includes('Fog output hashes verified.'));
  check('package has no .bak files', !m.files.some((file) => /\.bak$/i.test(file.path)));
  check('package includes Fog talk ESDs', m.files.some((file) => file.path.startsWith('script/talk/')));

  const secondConfig = config(game);
  const second = await generateRun({ config: secondConfig, workspacePath: path.join(temp, 'run-b'), profile: generationProfile }, () => {}, deps);
  await attestRun('full-run-b-determinism-repeat', second, path.join(temp, 'run-b'), secondConfig);
  check('rerun with same seed is byte-identical', second.ok && JSON.stringify(second.manifest.files) === JSON.stringify(m.files));
  check('no generation write into the game', sameMap(await hashRoots(game), reconstructed));

  const activated = await authority.activate(game, first.packagePath);
  check('Fog package activation (temp copy)', activated.ok, activated.ok ? `${activated.journal.files.length} files` : `${activated.code}: ${activated.message}`);
  const active = await authority.detectState(game);
  check('state active', active.state === 'active', active.problems.join('; '));
  const restored = await authority.restore(game);
  check('restore', restored.ok, restored.ok ? `${restored.restored} files` : `${restored.code}: ${restored.message}`);
  check('game matches reconstruction after restore', sameMap(await hashRoots(game), reconstructed));

  check('live installation unchanged', sameMap(await hashRoots(livePath), liveBefore));
} catch (error) {
  if (!(error instanceof SweepDone)) check('proof completed', false, String(error));
} finally {
  if (process.env.DS1TR_E2E_KEEP === '1') console.log(`kept ${temp}`);
  else await rm(temp, { recursive: true, force: true });
}
console.log(failed ? 'RESULT: FAIL' : 'RESULT: PASS');
process.exit(failed ? 1 : 0);
