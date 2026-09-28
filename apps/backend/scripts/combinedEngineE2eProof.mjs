// Combined-engine (Items + Enemies) end-to-end generation proof.
//
// Drives the real CombinedRandomizerAdapter -> bridge -> vendored JS core ->
// real DsrDataTool `scan`/`patch-enemies` against a real Dark Souls
// Remastered installation, with every workspace in the OS temp directory.
// The game directory is snapshotted (path, size, SHA-256 of every file, plus
// the directory set) before and after all runs; any difference fails.
//
// Not part of `npm test`: it needs a real installation, a built DsrDataTool,
// and a .NET 8 runtime. Game-derived output stays in the temp directory and
// is never copied into the repository (docs/LICENSING.md).
//
// Usage (after `npm run build -w @ds1tr/backend`):
//   node apps/backend/scripts/combinedEngineE2eProof.mjs "<DSR game directory>"
//   (or set DS1TR_E2E_GAME_PATH)
//
// Optional environment:
//   DS1TR_E2E_GUARD_PATHS  extra directories (path-delimiter separated) that
//                          must also stay byte-identical, e.g. the live
//                          installation when --game is a reconstructed copy.
//   DS1TR_E2E_STALE_WORKSPACE  a workspace left behind by an earlier failed
//                          run (partial seed directory, catalog from another
//                          game directory); proves recovery in place.
//   DS1TR_E2E_KEEP=1       keep the temporary workspaces for inspection.

import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { cp, mkdtemp, readdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { CombinedRandomizerAdapter } from '../dist/adapters/combinedRandomizerAdapter.js';
import { executeCombinedGeneration } from '../dist/adapters/combinedRandomizerBridge.js';
import { deriveSeeds } from '../dist/core/seeds.js';

const gamePath = process.argv[2] ?? process.env.DS1TR_E2E_GAME_PATH;
if (!gamePath) {
  console.error('Usage: combinedEngineE2eProof.mjs <DSR game directory> (or DS1TR_E2E_GAME_PATH)');
  process.exit(2);
}
const keepWorkspaces = process.env.DS1TR_E2E_KEEP === '1';
const guardPaths = (process.env.DS1TR_E2E_GUARD_PATHS ?? '').split(path.delimiter).filter(Boolean);
const staleWorkspace = process.env.DS1TR_E2E_STALE_WORKSPACE;

const results = [];
let failed = false;
function check(name, condition, detail = '') {
  results.push({ name, ok: Boolean(condition), detail });
  if (!condition) failed = true;
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${name}${detail ? ` — ${detail}` : ''}`);
}

async function hashFile(filePath) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(filePath)) hash.update(chunk);
  return hash.digest('hex');
}

/** Relative path -> { size, sha256 } for files, and a set of directories. */
async function snapshotTree(root, { hash = true } = {}) {
  const files = new Map();
  const directories = new Set();
  async function walk(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const full = path.join(directory, entry.name);
      const relative = path.relative(root, full);
      if (entry.isDirectory()) {
        directories.add(relative);
        await walk(full);
      } else {
        const info = await stat(full);
        files.set(relative, { size: info.size, sha256: hash ? await hashFile(full) : null });
      }
    }
  }
  await walk(root);
  return { files, directories };
}

function diffSnapshots(before, after) {
  const differences = [];
  for (const [relative, record] of before.files) {
    const other = after.files.get(relative);
    if (!other) differences.push(`removed file ${relative}`);
    else if (other.size !== record.size || other.sha256 !== record.sha256) differences.push(`changed file ${relative}`);
  }
  for (const relative of after.files.keys()) if (!before.files.has(relative)) differences.push(`added file ${relative}`);
  for (const relative of before.directories) if (!after.directories.has(relative)) differences.push(`removed dir ${relative}`);
  for (const relative of after.directories) if (!before.directories.has(relative)) differences.push(`added dir ${relative}`);
  return differences;
}

function isInside(parent, candidate) {
  const relative = path.relative(parent, candidate);
  return relative !== '' && !relative.startsWith('..') && !path.isAbsolute(relative);
}

function makeRequest(masterSeed, workspacePath) {
  return {
    gamePath,
    workspacePath,
    masterSeed,
    combinedSeed: deriveSeeds(masterSeed).combined,
    items: {
      enabled: true,
      randomizeWorldItems: true,
      randomizeProtectedItems: false,
      progressionLogic: true,
      guaranteeEarlyWeapon: true,
      balancedEarlyLoot: true,
      randomizeStartingClass: false,
      randomizeStartingEquipment: false,
      randomizeGifts: true,
      randomizeEnemyDrops: true,
      guaranteedEnemyDrops: false,
      randomizeShops: true,
    },
    enemies: { enabled: true, randomizeRegularEnemies: true, randomizeBosses: true, scaling: 'area' },
    generateSpoilerLog: true,
  };
}

/** Runs the bridge (full typed result) and collects events. */
async function runBridge(label, request, options = {}) {
  const events = [];
  const started = Date.now();
  const result = await executeCombinedGeneration(request, (event) => events.push(event), options);
  const seconds = ((Date.now() - started) / 1000).toFixed(1);
  console.log(`--- ${label}: ${result.ok ? 'ok' : result.code} in ${seconds}s (${events.length} events)`);
  if (!result.ok) console.log(`    ${result.message}`);
  return { result, events, seconds };
}

async function inspectPackage(label, result) {
  const seedDirectory = result.seedDirectory;
  const tree = await snapshotTree(seedDirectory);
  const placements = JSON.parse(await readFile(result.placementsPath, 'utf8'));
  const manifestText = await readFile(result.patchManifestPath, 'utf8');
  const manifest = JSON.parse(manifestText);

  check(`${label}: dataStatus is extracted`, placements.dataStatus === 'extracted', String(placements.dataStatus));
  for (const name of ['randomizer.json', 'config.json', 'patch-manifest.json']) {
    check(`${label}: ${name} exists`, tree.files.has(name));
  }

  // Every manifest entry's output must resolve inside the package and hash to
  // its recorded outputSha256; activation will trust exactly these entries.
  const entries = [
    ...(manifest.patchedMaps ?? []),
    ...(manifest.luaBundles ?? []),
    ...(manifest.events ?? []),
    ...[manifest.ffxBundle, manifest.gameParam].filter(Boolean),
  ];
  const badEntries = [];
  for (const entry of entries) {
    const output = path.resolve(seedDirectory, entry.output ?? '');
    if (!entry.output || path.isAbsolute(entry.output) || !isInside(seedDirectory, output)) {
      badEntries.push(`${entry.output}: outside package`);
    } else if (!tree.files.has(path.relative(seedDirectory, output))) {
      badEntries.push(`${entry.output}: missing`);
    } else if (tree.files.get(path.relative(seedDirectory, output)).sha256 !== entry.outputSha256) {
      badEntries.push(`${entry.output}: outputSha256 mismatch`);
    }
    if (!entry.source || path.isAbsolute(entry.source)) badEntries.push(`${entry.source}: source not game-relative`);
  }
  check(`${label}: patch-manifest entries resolve inside package and match outputSha256`,
    entries.length > 0 && badEntries.length === 0, `${entries.length} entries${badEntries.length ? `; ${badEntries.slice(0, 5).join(', ')}` : ''}`);

  const modFiles = [...tree.files.keys()].filter((relative) => relative.startsWith(`mod${path.sep}`));
  const listedOutputs = new Set(entries.map((entry) => path.normalize(entry.output)));
  const unlisted = modFiles.filter((relative) => !listedOutputs.has(relative));
  check(`${label}: every mod/ file is listed in patch-manifest`, unlisted.length === 0, unlisted.slice(0, 5).join(', '));
  const emptyModFiles = modFiles.filter((relative) => tree.files.get(relative).size === 0);
  check(`${label}: mod/ contains patched files`, modFiles.length > 0, `${modFiles.length} files`);
  check(`${label}: no empty patched files`, emptyModFiles.length === 0, emptyModFiles.slice(0, 5).join(', '));

  return { tree, placements, manifest, manifestEntries: entries.length, modFiles, digest: await packageDigest(seedDirectory, tree) };
}

// Fields that legitimately differ between otherwise identical runs: the
// absolute workspace path and the wall-clock generation time.
const volatileJsonFields = new Set(['outputDirectory', 'generatedAt']);

async function normalizedJsonHash(filePath) {
  const value = JSON.parse(await readFile(filePath, 'utf8'));
  for (const field of volatileJsonFields) {
    delete value[field];
    if (value.config && typeof value.config === 'object') delete value.config[field];
  }
  return createHash('sha256').update(JSON.stringify(value)).digest('hex');
}

/** Per-file digest; top-level JSON files are compared without volatile fields. */
async function packageDigest(root, tree) {
  const lines = [];
  for (const [relative, record] of [...tree.files.entries()].sort(([a], [b]) => a.localeCompare(b))) {
    const normalized = !relative.includes(path.sep) && relative.endsWith('.json');
    lines.push(`${relative}:${normalized ? `json:${await normalizedJsonHash(path.join(root, relative))}` : record.sha256}`);
  }
  return lines;
}

const temporaryRoots = [];
async function newWorkspace(label) {
  const root = await mkdtemp(path.join(os.tmpdir(), `ds1tr-e2e-${label}-`));
  temporaryRoots.push(root);
  return root;
}

// ---------------------------------------------------------------------------

const gameRoot = path.resolve(gamePath);
console.log(`Game directory: ${gameRoot}`);
console.log('Snapshotting game directory (full SHA-256)...');
let snapshotStarted = Date.now();
const gameBefore = await snapshotTree(gameRoot);
console.log(`  ${gameBefore.files.size} files, ${gameBefore.directories.size} directories, `
  + `${((Date.now() - snapshotStarted) / 1000).toFixed(1)}s`);

const guardsBefore = [];
for (const guardPath of guardPaths) {
  console.log(`Snapshotting guard directory ${guardPath}...`);
  guardsBefore.push({ guardPath, snapshot: await snapshotTree(path.resolve(guardPath)) });
}

const summary = { gameFiles: gameBefore.files.size, runs: {} };

try {
  // 1. Fresh workspace A: scan + generate + patch.
  const workspaceA = await newWorkspace('A');
  const requestA = makeRequest('e2e-proof-master-1', workspaceA);
  const runA = await runBridge('A (fresh, scan)', requestA);
  check('A: generation succeeds', runA.result.ok, runA.result.ok ? '' : `${runA.result.code}: ${runA.result.stderr ?? ''}`);
  if (!runA.result.ok) throw new Error('Run A failed; later runs depend on it.');
  check('A: catalog was scanned', runA.result.catalogScanned === true);
  check('A: seed is the derived combined seed', runA.result.seed === requestA.combinedSeed, runA.result.seed);
  check('A: package is inside workspace', isInside(workspaceA, runA.result.seedDirectory));
  check('A: forwards progress events', runA.events.some((event) => event.type === 'progress'));
  check('A: forwards log events', runA.events.some((event) => event.type === 'log'));
  const packageA = await inspectPackage('A', runA.result);
  const catalogA = await hashFile(runA.result.catalogPath);
  const catalogJson = JSON.parse(await readFile(runA.result.catalogPath, 'utf8'));
  summary.runs.A = {
    seconds: runA.seconds,
    placementHash: runA.result.placementHash,
    randomizerVersion: runA.result.randomizerVersion,
    catalogSha256: catalogA,
    catalogSchemaVersion: catalogJson.schemaVersion,
    catalogSourceFiles: catalogJson.sourceFiles?.length ?? null,
    packageFiles: packageA.tree.files.size,
    modFiles: packageA.modFiles.length,
    manifestTopLevelKeys: Object.keys(packageA.manifest),
    manifestEntries: packageA.manifestEntries,
    changedSlots: packageA.manifest.changedSlots,
    placementCounts: Object.fromEntries(Object.entries(packageA.placements.placements ?? {})
      .map(([key, value]) => [key, Array.isArray(value) ? value.length : typeof value])),
    upstreamWarning: packageA.placements.warning ?? null,
    validation: packageA.placements.validation ?? null,
  };

  // 2. Fresh workspace B, same request: determinism across independent scans.
  const workspaceB = await newWorkspace('B');
  const runB = await runBridge('B (fresh, same master seed)', makeRequest('e2e-proof-master-1', workspaceB));
  check('B: generation succeeds', runB.result.ok);
  if (runB.result.ok) {
    const packageB = await inspectPackage('B', runB.result);
    const catalogB = await normalizedJsonHash(runB.result.catalogPath);
    check('B: catalog identical to A except generatedAt (scan deterministic)', catalogB === await normalizedJsonHash(runA.result.catalogPath));
    check('B: placementHash equals A', runB.result.placementHash === runA.result.placementHash);
    const digestA = packageA.digest;
    const digestB = packageB.digest;
    const differing = digestA.filter((line) => !digestB.includes(line)).map((line) => line.split(':')[0]);
    check('B: every package file identical to A (JSON: except outputDirectory/generatedAt)', differing.length === 0 && digestA.length === digestB.length,
      differing.slice(0, 10).join(', '));
    summary.runs.B = { seconds: runB.seconds, differingFiles: differing };
  }

  // 3. Workspace C seeded with A's catalog: real verifyCatalogSources + reuse.
  const workspaceC = await newWorkspace('C');
  await cp(runA.result.catalogPath, path.join(workspaceC, 'dsr-catalog.json'));
  const runC = await runBridge('C (reused catalog)', makeRequest('e2e-proof-master-1', workspaceC));
  check('C: generation succeeds with reused real catalog', runC.result.ok,
    runC.result.ok ? '' : `${runC.result.code}: ${(runC.result.catalogProblems ?? []).slice(0, 3).join(' | ')}`);
  if (runC.result.ok) {
    check('C: catalog was not rescanned', runC.result.catalogScanned === false);
    check('C: placementHash equals A', runC.result.placementHash === runA.result.placementHash);
    summary.runs.C = { seconds: runC.seconds };
  }

  // 4. Different master seed: different placements.
  const workspaceD = await newWorkspace('D');
  await cp(runA.result.catalogPath, path.join(workspaceD, 'dsr-catalog.json'));
  const runD = await runBridge('D (different master seed)', makeRequest('e2e-proof-master-2', workspaceD));
  check('D: generation succeeds', runD.result.ok);
  if (runD.result.ok) {
    check('D: placementHash differs from A', runD.result.placementHash !== runA.result.placementHash);
    summary.runs.D = { seconds: runD.seconds, placementHash: runD.result.placementHash };
  }

  // 5. Recoverability: each failure, then a success in the same workspace.
  const workspaceE = await newWorkspace('E');
  const missingGame = path.join(await newWorkspace('nogame'), 'no-such-game');
  const runE1 = await runBridge('E1 (nonexistent game directory)', { ...makeRequest('e2e-proof-master-1', workspaceE), gamePath: missingGame });
  check('E1: real process failure is typed DATA_TOOL_FAILED', !runE1.result.ok && runE1.result.code === 'DATA_TOOL_FAILED',
    runE1.result.ok ? 'unexpected success' : runE1.result.code);
  check('E1: failure is not retryable', !runE1.result.ok && runE1.result.retryable === false);
  check('E1: process output captured', !runE1.result.ok && `${runE1.result.stderr ?? ''}${runE1.result.stdout ?? ''}`.trim().length > 0,
    !runE1.result.ok ? JSON.stringify(`${runE1.result.stderr ?? ''}`.trim().slice(0, 200)) : '');

  const runE2 = await runBridge('E2 (bogus DsrDataTool path)', makeRequest('e2e-proof-master-1', workspaceE),
    { dataToolDllPath: path.join(workspaceE, 'missing', 'DsrDataTool.dll') });
  // `dotnet <missing.dll>` spawns successfully, so this is expected to be a
  // process failure rather than a spawn failure; the actual code is recorded.
  check('E2: missing tool is a typed, non-retryable failure', !runE2.result.ok && runE2.result.retryable === false
    && (runE2.result.code === 'DATA_TOOL_MISSING' || runE2.result.code === 'DATA_TOOL_FAILED'),
    runE2.result.ok ? 'unexpected success' : `${runE2.result.code}: ${runE2.result.message.slice(0, 160)}`);
  summary.runs.E2 = runE2.result.ok ? null : { code: runE2.result.code };

  // Tampered catalog: flip one recorded source hash.
  const tampered = structuredClone(catalogJson);
  tampered.sourceFiles[0].sha256 = '0'.repeat(64);
  await writeFile(path.join(workspaceE, 'dsr-catalog.json'), JSON.stringify(tampered));
  const runE3 = await runBridge('E3 (tampered catalog)', makeRequest('e2e-proof-master-1', workspaceE));
  check('E3: tampered catalog rejected as CATALOG_SOURCE_MISMATCH',
    !runE3.result.ok && runE3.result.code === 'CATALOG_SOURCE_MISMATCH',
    runE3.result.ok ? 'unexpected success' : `${runE3.result.code}: ${(runE3.result.catalogProblems ?? []).join(' | ')}`);

  const runE4 = await runBridge('E4 (recovery via forced rescan, same workspace)',
    makeRequest('e2e-proof-master-1', workspaceE), { forceCatalogRescan: true });
  check('E4: same workspace recovers after failures', runE4.result.ok);
  if (runE4.result.ok) {
    check('E4: recovered run rescanned', runE4.result.catalogScanned === true);
    check('E4: recovered placementHash equals A', runE4.result.placementHash === runA.result.placementHash);
  }

  // 5b. Recovery in place from a real earlier failure that left partial output.
  if (staleWorkspace) {
    const staleSeedDirectory = path.join(staleWorkspace, path.basename(runA.result.seedDirectory));
    const runS1 = await runBridge('S1 (stale workspace, reused foreign catalog)', makeRequest('e2e-proof-master-1', staleWorkspace));
    check('S1: foreign catalog rejected as CATALOG_SOURCE_MISMATCH',
      !runS1.result.ok && runS1.result.code === 'CATALOG_SOURCE_MISMATCH',
      runS1.result.ok ? 'unexpected success' : `${runS1.result.code} (${(runS1.result.catalogProblems ?? []).length} problems)`);
    const runS2 = await runBridge('S2 (stale workspace, forced rescan)', makeRequest('e2e-proof-master-1', staleWorkspace),
      { forceCatalogRescan: true });
    check('S2: stale workspace recovers', runS2.result.ok, runS2.result.ok ? '' : runS2.result.code);
    if (runS2.result.ok) {
      check('S2: package lands in the stale seed directory', path.resolve(runS2.result.seedDirectory) === path.resolve(staleSeedDirectory));
      const staleTree = await snapshotTree(runS2.result.seedDirectory);
      const digestFresh = packageA.digest;
      const digestStale = await packageDigest(runS2.result.seedDirectory, staleTree);
      const leftovers = digestStale.filter((line) => !digestFresh.includes(line)).map((line) => line.split(':')[0]);
      check('S2: recovered package identical to a fresh one (no stale leftovers)',
        leftovers.length === 0 && digestFresh.length === digestStale.length, leftovers.slice(0, 10).join(', '));
      summary.runs.S2 = { leftovers };
    }
  }

  // 6. Public adapter surface returns the package directory.
  const workspaceF = await newWorkspace('F');
  await cp(runA.result.catalogPath, path.join(workspaceF, 'dsr-catalog.json'));
  const adapterResult = await new CombinedRandomizerAdapter()
    .generate(makeRequest('e2e-proof-master-1', workspaceF), () => {});
  check('F: CombinedRandomizerAdapter.generate succeeds', adapterResult.ok);
  if (adapterResult.ok) check('F: adapter returns package inside workspace', isInside(workspaceF, adapterResult.workspacePath));
} catch (error) {
  check('harness completed without exception', false, error instanceof Error ? error.stack : String(error));
} finally {
  console.log('Re-snapshotting game directory...');
  snapshotStarted = Date.now();
  const gameAfter = await snapshotTree(gameRoot);
  console.log(`  ${((Date.now() - snapshotStarted) / 1000).toFixed(1)}s`);
  const differences = diffSnapshots(gameBefore, gameAfter);
  check('game directory unchanged (every file path, size, SHA-256; directory set)', differences.length === 0,
    differences.slice(0, 20).join('; '));

  for (const { guardPath, snapshot } of guardsBefore) {
    const guardDifferences = diffSnapshots(snapshot, await snapshotTree(path.resolve(guardPath)));
    check(`guard directory unchanged: ${guardPath}`, guardDifferences.length === 0, guardDifferences.slice(0, 20).join('; '));
  }

  const reportPath = path.join(os.tmpdir(), 'ds1tr-e2e-report.json');
  await writeFile(reportPath, JSON.stringify({ gameRoot, summary, results, temporaryRoots }, null, 2));
  console.log(`Report: ${reportPath}`);

  if (!keepWorkspaces) {
    for (const root of temporaryRoots) await rm(root, { recursive: true, force: true });
  } else {
    console.log(`Workspaces kept: ${temporaryRoots.join(', ')}`);
  }

  console.log(failed ? 'E2E PROOF FAILED' : 'E2E PROOF PASSED');
  process.exitCode = failed ? 1 : 0;
}
