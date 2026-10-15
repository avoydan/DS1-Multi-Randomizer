import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { parseArguments, readSignatures, readJson, repositoryRoot } from './release-lib.mjs';

// The release tooling's own guarantees (docs/RELEASE_TRUST.md): none of these builds or signs anything.

const read = (...parts) => readFile(path.join(repositoryRoot, ...parts), 'utf8');

test('the release policy is well-formed: the DS1TR executables are unique, the elevation helper is forbidden, every pattern is a valid expression', async () => {
  const policy = await readJson(path.join(repositoryRoot, 'tools', 'release', 'release-policy.json'));
  const paths = policy.ds1trExecutables.map((entry) => entry.path);
  assert.equal(new Set(paths).size, paths.length);
  assert.equal(paths.length, 5, 'the application, DsrDataTool, current and legacy FogHost, and game cursor helper');
  assert.ok(paths.some((entry) => entry.endsWith('/fog-host-legacy/FogHost.exe')));
  assert.ok(policy.forbiddenBinaries.some((entry) => /elevate\.exe$/u.test(entry.path)));
  for (const pattern of policy.forbiddenPatterns) assert.doesNotThrow(() => new RegExp(pattern, 'u'));
  assert.ok(policy.forbiddenPatterns.some((pattern) => /pfx/u.test(pattern)), 'a certificate file inside a package fails the verification');
  assert.ok(policy.ds1trArtifacts.length === 2, 'installer and portable');
});

test('the free-cursor helper is staged, explicitly allowlisted, and version-checked', async () => {
  const packager = await read('apps', 'desktop', 'scripts', 'package-win.mjs');
  const verifier = await read('tools', 'release', 'release-verify.mjs');
  const helperProject = await read('tools', 'game-cursor-helper', 'GameCursorHelper.csproj');
  const policy = await readJson(path.join(repositoryRoot, 'tools', 'release', 'release-policy.json'));
  assert.ok(policy.ds1trExecutables.some((entry) => entry.path.endsWith('/game-cursor-helper/GameCursorHelper.exe')));
  assert.match(packager, /tools\/game-cursor-helper\/GameCursorHelper\.csproj/u);
  assert.match(packager, /\['--self-test'\]/u);
  assert.match(verifier, /GameCursorHelper\.csproj/u);
  assert.match(helperProject, /<TargetFramework>net8\.0<\/TargetFramework>/u);
});

test('the packager is per-user, never elevates, does not ship an elevation helper and signs only in release mode', async () => {
  const config = await read('apps', 'desktop', 'electron-builder.yml');
  assert.match(config, /requestedExecutionLevel:\s*asInvoker/u);
  assert.match(config, /perMachine:\s*false/u);
  assert.match(config, /allowElevation:\s*false/u);
  assert.match(config, /packElevateHelper:\s*false/u);
  assert.match(config, /signExecutable:\s*false/u, 'development and test builds are never signed by accident');
  assert.match(config, /signingHashAlgorithms:\s*\n\s*-\s*sha256/u, 'an array, as the electron-builder schema requires');
  const active = config.split(/\r?\n/u).filter((line) => !line.trim().startsWith('#')).join('\n');
  assert.doesNotMatch(active, /(password|CSC_|\.pfx)/iu, 'no credential material in the configuration (comments only name the variables)');
});

test('Nexus packaging is a separate unsigned local target with no inherited GitHub publish metadata', async () => {
  const packageJson = JSON.parse(await read('apps', 'desktop', 'package.json'));
  const nexusConfig = await read('apps', 'desktop', 'electron-builder.nexus.yml');
  const packager = await read('apps', 'desktop', 'scripts', 'package-win.mjs');
  const verifier = await read('tools', 'release', 'release-verify.mjs');
  assert.match(packageJson.version, /^\d+\.\d+\.\d+$/u);
  assert.equal(packageJson.scripts['package:win:github'], 'node scripts/package-win.mjs --distribution=github');
  assert.equal(packageJson.scripts['package:win:nexus'], 'node scripts/package-win.mjs --distribution=nexus');
  assert.match(nexusConfig, /^extends: electron-builder\.yml$/mu);
  assert.match(nexusConfig, /^publish: null$/mu);
  assert.match(packager, /mode: distribution === 'nexus' \? 'nexus-local-unsigned'/u);
  assert.match(packager, /release\/nexus/u);
  assert.match(packager, /Nexus-Setup-x64/u);
  assert.match(verifier, /Nexus package contains resources\/app-update\.yml/u);
  assert.match(verifier, /networkDisabled: true/u);
});

const releaseVersion = JSON.parse(await read('apps', 'desktop', 'package.json')).version;
const nexusManifestPath = path.join(repositoryRoot, 'apps', 'desktop', 'release', 'nexus', `release-manifest-${releaseVersion}-nexus.json`);
const nexusManifestBuilt = await readFile(nexusManifestPath).then(() => true, () => false);
test('a built Nexus manifest records the actual current Seed Code generation profile', { skip: !nexusManifestBuilt }, async () => {
  const source = await read('packages', 'contracts', 'src', 'generation.ts');
  const currentSymbol = /export const CURRENT_GENERATION_PROFILE\s*=\s*(GENERATION_PROFILE_[A-Z0-9_]+)/u.exec(source)?.[1];
  const currentProfile = [...source.matchAll(/export const (GENERATION_PROFILE_[A-Z0-9_]+)\s*=\s*['"]([^'"]+)['"]/gu)]
    .find(([, symbol]) => symbol === currentSymbol)?.[2];
  const manifest = JSON.parse(await readFile(nexusManifestPath, 'utf8'));
  assert.ok(currentProfile, 'the source declares a known current profile');
  assert.equal(manifest.generationProfile, currentProfile);
});

test('a public release build refuses to start without signing credentials, before it builds anything', async () => {
  const environment = { ...process.env };
  for (const name of Object.keys(environment)) if (/^(CSC_|WIN_CSC_|DS1TR_SIGN_|DS1TR_ARTIFACT_SIGNING|DS1TR_EXPECTED_PUBLISHER|AZURE_)/u.test(name)) delete environment[name];
  const result = spawnSync(process.execPath, [path.join(repositoryRoot, 'apps', 'desktop', 'scripts', 'package-win.mjs'), '--release'], { env: environment, encoding: 'utf8', timeout: 60000, windowsHide: true });
  assert.notEqual(result.status, 0);
  assert.match(`${result.stderr}${result.stdout}`, /PUBLIC RELEASE refused: no signing credentials/u);
});

test('without an expected publisher a release build refuses even with credentials', async () => {
  const environment = { ...process.env, CSC_LINK: 'C:/does/not/exist.pfx' };
  delete environment.DS1TR_EXPECTED_PUBLISHER;
  const result = spawnSync(process.execPath, [path.join(repositoryRoot, 'apps', 'desktop', 'scripts', 'package-win.mjs'), '--release'], { env: environment, encoding: 'utf8', timeout: 60000, windowsHide: true });
  assert.notEqual(result.status, 0);
  assert.match(`${result.stderr}${result.stdout}`, /DS1TR_EXPECTED_PUBLISHER is not set/u);
});

test('signing material can never be committed by accident', async () => {
  const ignore = await read('.gitignore');
  for (const pattern of ['*.pfx', '*.p12', '*.pem', '*.key', '.env']) assert.ok(ignore.split(/\r?\n/u).includes(pattern), `${pattern} is ignored`);
  assert.ok(ignore.split(/\r?\n/u).includes('!/tools/release/'), 'the release tooling itself is tracked although "release/" is ignored');
});

test('arguments: flags, valued options and positionals', () => {
  const { flags, values, positional } = parseArguments(['--public', '--expected-publisher', 'ACME', '--distribution', 'nexus', 'file.exe', '--allow-untrusted-root']);
  assert.ok(flags.has('public') && flags.has('allow-untrusted-root'));
  assert.equal(values.get('expected-publisher'), 'ACME');
  assert.equal(values.get('distribution'), 'nexus');
  assert.deepEqual(positional, ['file.exe']);
});

test('the Authenticode reader reports an unsigned file as not signed (it never says signed without a signature)', { skip: process.platform !== 'win32' }, async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-release-test-'));
  try {
    const file = path.join(directory, 'unsigned.exe');
    await writeFile(file, 'MZ not a real executable');
    const [row] = await readSignatures([file]);
    assert.equal(row.file, file);
    assert.notEqual(row.status, 'Valid');
    assert.equal(row.signer, null);
  } finally { await rm(directory, { recursive: true, force: true }); }
});

// Needs a built package: prefer the local Nexus package when present, otherwise the normal GitHub package.
const nexusUnpacked = path.join(repositoryRoot, 'apps', 'desktop', 'release', 'nexus', 'win-unpacked');
const githubUnpacked = path.join(repositoryRoot, 'apps', 'desktop', 'release', 'win-unpacked');
const isCurrentBuild = async (unpacked) => {
  try {
    const [asar, buildInfo] = await Promise.all([
      readFile(path.join(unpacked, 'resources', 'app.asar')),
      readFile(path.join(unpacked, 'resources', 'runtime', 'build-info.json'), 'utf8'),
    ]);
    return Boolean(asar && JSON.parse(buildInfo).version === releaseVersion);
  } catch {
    return false;
  }
};
const nexusBuilt = await isCurrentBuild(nexusUnpacked);
const githubBuilt = await isCurrentBuild(githubUnpacked);
const verifierTarget = nexusBuilt
  ? { distribution: 'nexus', unpacked: nexusUnpacked }
  : { distribution: 'github', unpacked: githubUnpacked };
const built = nexusBuilt || githubBuilt;
test('the verifier fails an unsigned build in public mode and passes it in development mode (the exit code is what the release script and the gate rely on)', { skip: !built }, () => {
  const verify = path.join(repositoryRoot, 'tools', 'release', 'release-verify.mjs');
  const args = ['--distribution', verifierTarget.distribution, '--unpacked', verifierTarget.unpacked];
  const development = spawnSync(process.execPath, [verify, ...args], { encoding: 'utf8', timeout: 120000, windowsHide: true });
  const strict = spawnSync(process.execPath, [verify, ...args, '--require-signature'], { encoding: 'utf8', timeout: 120000, windowsHide: true });
  assert.equal(development.status, 0, development.stdout);
  assert.notEqual(strict.status, 0, 'an unsigned build must never pass public mode');
  assert.match(strict.stdout, /UNSIGNED/u);
});
