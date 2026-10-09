// Release verification: what a DS1 Multi Randomizer release contains, what is signed, and whether it is ready for the public.
//
//   node tools/release/release-verify.mjs [--unpacked <dir>] [--installer <file>] [--portable <file>] [--manifest <out.json>]
//                                          [--require-signature] [--expected-publisher <text>] [--allow-untrusted-root] [--publication] [--tag vX.Y.Z] [--json]
//
// DEVELOPMENT mode (the default): structure problems fail, an unsigned build is reported as UNSIGNED and does not fail.
// PUBLIC mode (--require-signature): every DS1TR-authored executable and artifact must carry a valid, trusted Authenticode signature with a
// timestamp whose signer matches --expected-publisher (or DS1TR_EXPECTED_PUBLISHER); anything else fails.
// --allow-untrusted-root exists to test the signing pipeline with a throwaway certificate: it accepts a signature that is cryptographically
// intact but chains to no trusted root and labels the result TEST-SIGNED, never TRUSTED SIGNED.
//
// --publication: the build is going to be PUBLISHED (unsigned or not). The update checks (docs/AUTO_UPDATE.md) then become failures: the update source must be set, the
// channel must be public, latest.yml must match the installer. --require-signature implies it. --tag vX.Y.Z fails unless the tag is the packaged version.
//
// Nothing here signs, uploads or changes anything: it reads files, hashes them and asks Windows for their Authenticode state.

import path from 'node:path';
import { createRequire } from 'node:module';
import { execFile } from 'node:child_process';
import { readdir, readFile, stat, writeFile } from 'node:fs/promises';
import {
  exists, listFiles, parseArguments, readJson, readSignatures, readVersionInfo, repositoryRoot, sha256File,
} from './release-lib.mjs';
import { checkAsarRuntimeDependencies, checkLatestYml, checkUpdateConfiguration, findSecrets, parseBuilderPublish, parseFlatYaml, parseUpdateSourceTs, sha512Base64, versionOfTag } from './update-lib.mjs';

const { flags, values } = parseArguments(process.argv.slice(2));
const requireSignature = flags.has('require-signature');
const allowUntrustedRoot = flags.has('allow-untrusted-root');
const publication = requireSignature || flags.has('publication');
const requestedTag = values.get('tag') ?? null;
const expectedPublisher = values.get('expected-publisher') ?? process.env.DS1TR_EXPECTED_PUBLISHER ?? '';
const releaseDirectory = path.join(repositoryRoot, 'apps', 'desktop', 'release');
const unpacked = path.resolve(values.get('unpacked') ?? path.join(releaseDirectory, 'win-unpacked'));
const policy = await readJson(path.join(repositoryRoot, 'tools', 'release', 'release-policy.json'));
const desktopPackage = await readJson(path.join(repositoryRoot, 'apps', 'desktop', 'package.json'));
const version = desktopPackage.version;

const findings = [];
const fail = (area, message) => findings.push({ level: 'FAIL', area, message });
const warn = (area, message) => findings.push({ level: 'WARN', area, message });
const note = (area, message) => findings.push({ level: 'INFO', area, message });

// ---- structure ---------------------------------------------------------------------------------------------------------------------
if (!(await exists(unpacked))) {
  console.error(`The unpacked app folder does not exist: ${unpacked}. Build first (npm run package:win -w @ds1tr/desktop).`);
  process.exit(2);
}
const files = await listFiles(unpacked);
const lower = new Map(files.map((file) => [file.toLowerCase(), file]));
const has = (relative) => lower.has(relative.toLowerCase());

for (const required of policy.requiredFiles) if (!has(required)) fail('structure', `a required file is missing: ${required}`);
for (const forbidden of policy.forbiddenBinaries) if (has(forbidden.path)) fail('structure', `${forbidden.path} must not be shipped: ${forbidden.why}`);
for (const pattern of policy.forbiddenPatterns) {
  const expression = new RegExp(pattern, 'iu');
  for (const file of files) if (expression.test(file)) fail('baggage', `a file that must not be shipped: ${file}`);
}
const known = new Set([...policy.ds1trExecutables, ...policy.thirdPartyBinaries].map((entry) => entry.path.toLowerCase()));
const binaries = files.filter((file) => /\.(exe|dll|node)$/iu.test(file));
for (const binary of binaries) if (!known.has(binary.toLowerCase()) && !policy.forbiddenBinaries.some((entry) => entry.path.toLowerCase() === binary.toLowerCase())) fail('structure', `an executable or library the policy does not know: ${binary}`);
for (const entry of policy.ds1trExecutables) if (!has(entry.path)) fail('structure', `a DS1TR executable is missing: ${entry.path}`);
note('structure', `${files.length} files, ${binaries.length} executables or libraries (${policy.ds1trExecutables.length} authored by DS1TR, ${policy.thirdPartyBinaries.length} third-party)`);

// ---- artifacts ---------------------------------------------------------------------------------------------------------------------
const artifactNames = await readdir(releaseDirectory);
const pick = (pattern) => artifactNames.filter((name) => new RegExp(`^${pattern.replace(/[.+^${}()|[\]\\]/gu, '\\$&').replace(/\*/gu, '.*')}$`, 'u').test(name));
const installerName = values.get('installer') ?? pick(`DS1-Multi-Randomizer-Setup-${version}.exe`)[0];
const portableName = values.get('portable') ?? pick(`DS1-Multi-Randomizer-${version}-x64.exe`)[0];
const artifacts = [];
for (const [kind, name] of [['installer', installerName], ['portable', portableName]]) {
  if (!name) { (kind === 'installer' ? fail : warn)('artifacts', `no ${kind} for version ${version} in ${releaseDirectory}`); continue; }
  const full = path.isAbsolute(name) ? name : path.join(releaseDirectory, name);
  if (!(await exists(full))) { fail('artifacts', `${kind} not found: ${full}`); continue; }
  artifacts.push({ kind, file: full, relative: path.basename(full) });
}

// ---- versions and metadata -------------------------------------------------------------------------------------------------------
const mainExe = path.join(unpacked, policy.ds1trExecutables[0].path);
const dataToolExe = path.join(unpacked, policy.ds1trExecutables[1].path);
const fogHostExe = path.join(unpacked, policy.ds1trExecutables[2].path);
const present = [mainExe, dataToolExe, fogHostExe, ...artifacts.map((artifact) => artifact.file)].filter((file) => files.length > 0);
const infos = await readVersionInfo(present.filter((file) => /\.exe$/iu.test(file)));
const infoOf = (file) => infos.find((entry) => entry.file === file);
const fogCsproj = await readFile(path.join(repositoryRoot, 'tools', 'fog-host', 'FogHost.csproj'), 'utf8');
const fogVersion = /<Version>([^<]+)<\/Version>/u.exec(fogCsproj)?.[1];
const randomizerVersion = (await readJson(path.join(repositoryRoot, 'engines', 'combined', 'vendor', 'package.json'))).version;
const fileVersionOf = (info) => (info?.fileVersion ?? '').split('.').slice(0, 3).join('.');
if (infoOf(mainExe) && fileVersionOf(infoOf(mainExe)) !== version) fail('versions', `the application executable says ${infoOf(mainExe).fileVersion}, package.json says ${version}`);
if (infoOf(fogHostExe) && fileVersionOf(infoOf(fogHostExe)) !== fogVersion) fail('versions', `FogHost.exe says ${infoOf(fogHostExe).fileVersion}, FogHost.csproj says ${fogVersion}`);
for (const [label, file] of [['application', mainExe], ['DsrDataTool', dataToolExe], ['FogHost', fogHostExe]]) {
  const info = infoOf(file);
  if (!info) continue;
  if (/GitHub, Inc\.|^$/u.test(info.company ?? '')) fail('metadata', `${label}: the company name is "${info.company ?? ''}" (it must name this project, not the toolchain)`);
  if (!info.description) fail('metadata', `${label}: no file description`);
  if (!info.copyright || !info.copyright.trim()) fail('metadata', `${label}: no copyright text`);
  if (/FromSoftware|Bandai/iu.test(`${info.company} ${info.product} ${info.copyright}`)) fail('metadata', `${label}: the metadata names the game's publisher`);
}
// The pinned world data: the commit the application fetches from must be the commit of the FogHost pin.
const pin = await readFile(path.join(repositoryRoot, 'tools', 'fog-host', 'vendor', 'UPSTREAM_PIN.md'), 'utf8');
const pinCommit = /Pinned commit: `([0-9a-f]{40})`/u.exec(pin)?.[1];
const worldData = await readFile(path.join(repositoryRoot, 'apps', 'backend', 'src', 'adapters', 'worldData.ts'), 'utf8');
const worldDataCommit = /worldDataSourceCommit = '([0-9a-f]{40})'/u.exec(worldData)?.[1];
if (!pinCommit || pinCommit !== worldDataCommit) fail('provenance', `the Fog pin (${pinCommit}) and the world data commit (${worldDataCommit}) differ`);
const shippedPin = lower.get('resources/runtime/licenses/foghost-upstream-pin.md');
if (shippedPin && (await readFile(path.join(unpacked, shippedPin), 'utf8')).replace(/\r\n/gu, '\n') !== pin.replace(/\r\n/gu, '\n')) fail('provenance', 'the pin file shipped in the package is not the pin file of the repository');
// The channel (docs/RELEASE_TRUST.md): a public release must be a public-channel build (the internal test worlds hidden); development builds report theirs.
const buildInfoFile = path.join(unpacked, 'resources', 'runtime', 'build-info.json');
let channel = null;
if (await exists(buildInfoFile)) { try { channel = JSON.parse(await readFile(buildInfoFile, 'utf8')).channel ?? null; } catch { channel = null; } }
if (requireSignature && channel !== 'public') fail('channel', `a public release must be a public-channel build; build-info.json says ${channel ?? 'nothing'}`);
// The packaged application has no menu bar and no telemetry: checked in the source it was built from.
const main = await readFile(path.join(repositoryRoot, 'apps', 'desktop', 'electron', 'main.ts'), 'utf8');
if (!/setApplicationMenu\(null\)|removeMenu\(\)|autoHideMenuBar/u.test(main)) fail('ui', 'the Electron menu is no longer removed in the packaged application (main.ts)');
const builderConfig = await readFile(path.join(repositoryRoot, 'apps', 'desktop', 'electron-builder.yml'), 'utf8');
if (!/requestedExecutionLevel:\s*asInvoker/u.test(builderConfig)) fail('privilege', 'electron-builder.yml no longer pins requestedExecutionLevel: asInvoker');
if (!/perMachine:\s*false/u.test(builderConfig)) fail('privilege', 'the installer is no longer per-user (nsis.perMachine: false)');

// ---- updates (docs/AUTO_UPDATE.md) ---------------------------------------------------------------------------------------------------
const sourceTs = parseUpdateSourceTs(await readFile(path.join(repositoryRoot, 'apps', 'desktop', 'electron', 'updateSource.ts'), 'utf8'));
const appUpdateFile = path.join(unpacked, 'resources', 'app-update.yml');
const appUpdate = (await exists(appUpdateFile)) ? parseFlatYaml(await readFile(appUpdateFile, 'utf8')) : null;
if (!appUpdate) (publication ? fail : warn)('updates', 'resources/app-update.yml is missing from the package: an installed copy would not know where to look for updates');
for (const finding of checkUpdateConfiguration({ sourceTs, builderPublish: parseBuilderPublish(builderConfig), updaterText: await readFile(path.join(repositoryRoot, 'apps', 'desktop', 'electron', 'updater.ts'), 'utf8'), appUpdate, publication })) findings.push(finding);
if (requestedTag !== null && versionOfTag(requestedTag) !== version) fail('version', `the requested tag ${requestedTag} is not v${version}: a release must carry the version that is packaged`);
if (publication && channel !== 'public') fail('channel', `a build to be published must be a public-channel build; build-info.json says ${channel ?? 'nothing'}`);
const buildInfoText = (await exists(buildInfoFile)) ? await readFile(buildInfoFile, 'utf8') : '';
try { if (buildInfoText && JSON.parse(buildInfoText).version !== version) fail('version', `build-info.json says ${JSON.parse(buildInfoText).version}, package.json says ${version}`); } catch { /* an unreadable build-info is reported by the channel check */ }

// latest.yml, the blockmap and the absence of secrets: the metadata an installed client trusts must describe THIS installer.
const updateAssets = { latest: null, blockmap: null };
const installerArtifact = artifacts.find((artifact) => artifact.kind === 'installer');
const latestFile = path.join(releaseDirectory, 'latest.yml');
if (installerArtifact && (await exists(latestFile))) {
  const latest = parseFlatYaml(await readFile(latestFile, 'utf8'));
  const installerSha512 = await sha512Base64(installerArtifact.file);
  const blockmapPresent = await exists(`${installerArtifact.file}.blockmap`);
  for (const finding of checkLatestYml({ latest, version, installerName: installerArtifact.relative, installerSha512, installerSize: (await stat(installerArtifact.file)).size, tag: requestedTag, blockmapPresent })) findings.push(finding);
  updateAssets.latest = { version: latest.version, path: latest.path, sha512: latest.sha512, releaseDate: latest.releaseDate };
  updateAssets.blockmap = blockmapPresent ? `${installerArtifact.relative}.blockmap` : null;
} else if (publication) {
  fail('updates', installerArtifact ? 'latest.yml was not produced next to the installer: installed clients could not see this release' : 'no installer, so no latest.yml to check');
} else {
  warn('updates', 'no latest.yml next to the installer (electron-builder writes it with the installer; a development run without the publish block does not)');
}
// The shell imports electron-updater at start-up: it must be inside app.asar together with everything it requires.
const asarFile = path.join(unpacked, 'resources', 'app.asar');
if (await exists(asarFile)) {
  const asar = createRequire(path.join(repositoryRoot, 'package.json'))('@electron/asar');
  const asarEntries = asar.listPackage(asarFile).map((entry) => entry.split('\\').join('/').replace(/^\//u, ''));
  for (const finding of checkAsarRuntimeDependencies({ entries: asarEntries, readJson: (entry) => JSON.parse(asar.extractFile(asarFile, entry.split('/').join(path.sep)).toString('utf8')) })) findings.push(finding);
} else fail('updates', 'resources/app.asar is missing');
const secretTargets = [appUpdateFile, buildInfoFile, latestFile, path.join(unpacked, 'resources', 'app.asar')];
for (const target of secretTargets) if (await exists(target)) {
  const hits = findSecrets((await readFile(target)).toString('latin1'));
  if (hits.length > 0) fail('secrets', `${path.basename(target)} contains something that looks like a credential (${hits.length} pattern(s)): no token or key may ever be packaged`);
}

// ---- signatures ---------------------------------------------------------------------------------------------------------------------
const toSign = [
  ...policy.ds1trExecutables.map((entry) => ({ label: entry.path, file: path.join(unpacked, entry.path) })),
  ...artifacts.map((artifact) => ({ label: artifact.relative, file: artifact.file })),
].filter((entry) => true);
const existingToSign = [];
for (const entry of toSign) if (await exists(entry.file)) existingToSign.push(entry);
const thirdParty = policy.thirdPartyBinaries.map((entry) => ({ label: entry.path, file: path.join(unpacked, entry.path), owner: entry.owner })).filter((entry) => has(entry.label));
const signatures = await readSignatures([...existingToSign.map((entry) => entry.file), ...thirdParty.map((entry) => entry.file)]);
const signatureOf = (file) => signatures.find((entry) => entry.file === file);
const trusted = (signature) => signature?.status === 'Valid';
const intact = (signature) => ['Valid', 'UnknownError', 'NotTrusted'].includes(signature?.status) && signature?.signer;
const states = new Map();
let anyUnsigned = false;
let anyTest = false;
for (const entry of existingToSign) {
  const signature = signatureOf(entry.file);
  let state;
  if (!signature || signature.status === 'NotSigned') { state = 'UNSIGNED'; anyUnsigned = true; }
  else if (trusted(signature)) state = 'TRUSTED SIGNED';
  else if (intact(signature) && allowUntrustedRoot) { state = 'TEST-SIGNED (untrusted root)'; anyTest = true; }
  else { state = `INVALID (${signature.status})`; }
  states.set(entry.label, state);
  if (requireSignature) {
    if (state !== 'TRUSTED SIGNED') fail('signature', `${entry.label}: ${state}; a public release needs a valid, trusted signature on every DS1TR executable and artifact`);
    else {
      if (expectedPublisher && !signature.signer.includes(expectedPublisher)) fail('signature', `${entry.label}: signed by "${signature.signer}", expected a subject containing "${expectedPublisher}"`);
      if (!signature.timestamp) fail('signature', `${entry.label}: signed without a trusted timestamp (the signature would stop verifying when the certificate expires)`);
    }
    if (!expectedPublisher) fail('signature', 'no expected publisher was given (--expected-publisher or DS1TR_EXPECTED_PUBLISHER): the signer cannot be checked');
  } else if (state.startsWith('INVALID')) fail('signature', `${entry.label}: ${state}`);
}
const unsignedThirdParty = thirdParty.filter((entry) => !trusted(signatureOf(entry.file))).map((entry) => entry.label);
if (unsignedThirdParty.length > 0) note('signature', `third-party binaries (Electron / Chromium) are shipped as their authors built them and are not re-signed: ${unsignedThirdParty.join(', ')}`);

// ---- report -------------------------------------------------------------------------------------------------------------------------------
const overall = [...states.values()].every((state) => state === 'TRUSTED SIGNED') && states.size > 0 ? 'TRUSTED SIGNED' : anyTest && !anyUnsigned ? 'TEST-SIGNED (untrusted root)' : anyUnsigned && [...states.values()].some((state) => state !== 'UNSIGNED') ? 'PARTLY SIGNED' : 'UNSIGNED';
const failures = findings.filter((entry) => entry.level === 'FAIL');
const missingBeforePublic = [];
if (overall !== 'TRUSTED SIGNED') missingBeforePublic.push('a trusted code-signing identity (see docs/RELEASE_TRUST.md, "What the owner has to do") and a signed build made with --release');
if (!expectedPublisher) missingBeforePublic.push('the expected publisher name (DS1TR_EXPECTED_PUBLISHER) so that the signer can be verified');
if (!installerName) missingBeforePublic.push('an installer built for this version');

const manifest = {
  schema: 'ds1tr-release-manifest-1',
  product: policy.product,
  version,
  generationProfile: /CURRENT_GENERATION_PROFILE\s*=\s*([A-Z0-9_]+)/u.test(await readFile(path.join(repositoryRoot, 'packages', 'contracts', 'src', 'generation.ts'), 'utf8')) ? 'ds1tr-gen-v1 (the profile of every Seed Code made so far; later profiles stay runnable)' : 'unknown',
  mode: requireSignature ? 'public' : publication ? 'unsigned-release' : 'development',
  channel,
  signatureState: overall,
  expectedPublisher: expectedPublisher || null,
  versions: { desktop: version, backend: (await readJson(path.join(repositoryRoot, 'apps', 'backend', 'package.json'))).version, contracts: (await readJson(path.join(repositoryRoot, 'packages', 'contracts', 'package.json'))).version, foghost: fogVersion, randomizer: randomizerVersion },
  fogPin: { foghostPinnedCommit: pinCommit, worldDataCommit },
  updates: {
    provider: sourceTs.provider, owner: sourceTs.owner, repo: sourceTs.repo, sourceConfigured: sourceTs.owner !== 'REPLACE_WITH_GITHUB_OWNER',
    tag: requestedTag, publication, latestYml: updateAssets.latest, blockmap: updateAssets.blockmap,
    appUpdate: appUpdate ? { provider: appUpdate.provider ?? null, owner: appUpdate.owner ?? null, repo: appUpdate.repo ?? null, publisherName: appUpdate.publisherName ?? null } : null,
  },
  provenance: await provenance(),
  artifacts: [],
  executables: [],
  thirdPartyBinaries: [],
};
for (const artifact of artifacts) {
  const signature = signatureOf(artifact.file);
  manifest.artifacts.push({ kind: artifact.kind, file: artifact.relative, sha256: await sha256File(artifact.file), size: (await stat(artifact.file)).size, signature: states.get(artifact.relative), signer: signature?.signer ?? null, timestamp: signature?.timestamp ?? null });
}
for (const entry of existingToSign.filter((item) => policy.ds1trExecutables.some((exe) => exe.path === item.label))) {
  const signature = signatureOf(entry.file);
  const info = infoOf(entry.file);
  manifest.executables.push({ path: entry.label, sha256: await sha256File(entry.file), size: (await stat(entry.file)).size, fileVersion: info?.fileVersion ?? null, company: info?.company ?? null, signature: states.get(entry.label), signer: signature?.signer ?? null, timestamp: signature?.timestamp ?? null });
}
for (const entry of thirdParty) manifest.thirdPartyBinaries.push({ path: entry.label, owner: entry.owner, sha256: await sha256File(entry.file), signature: trusted(signatureOf(entry.file)) ? 'signed by its author' : 'as built by its author (not re-signed)' });

if (flags.has('json')) console.log(JSON.stringify({ manifest, findings, missingBeforePublic }, null, 2));
else {
  console.log(`DS1 Multi Randomizer ${version} - release verification (${manifest.mode} mode)`);
  console.log(`Signature state: ${overall}`);
  for (const [label, state] of states) console.log(`  ${state.padEnd(30)} ${label}`);
  for (const artifact of manifest.artifacts) console.log(`  ${artifact.kind.padEnd(10)} ${artifact.file}  ${artifact.size} bytes  sha256 ${artifact.sha256}`);
  for (const level of ['FAIL', 'WARN', 'INFO']) for (const entry of findings.filter((item) => item.level === level)) console.log(`${level.padEnd(5)} [${entry.area}] ${entry.message}`);
  if (missingBeforePublic.length > 0) { console.log('Still missing before a PUBLIC SIGNED RELEASE:'); for (const item of missingBeforePublic) console.log(`  - ${item}`); }
  console.log(failures.length === 0 ? `RESULT: OK for ${manifest.mode} mode${overall === 'TRUSTED SIGNED' ? '' : ' (not signed with a trusted identity)'}` : `RESULT: FAIL (${failures.length})`);
}
if (values.get('manifest')) await writeFile(path.resolve(values.get('manifest')), `${JSON.stringify(manifest, null, 2)}\n`);
process.exitCode = failures.length === 0 ? 0 : 1;

/** Where the build came from: the commit, whether the tree was clean, the toolchain versions. Not a reproducible-build claim. */
async function provenance() {
  const git = (args) => new Promise((resolve) => execFile('git', args, { cwd: repositoryRoot, windowsHide: true }, (error, out) => resolve(error ? null : out.trim())));
  const porcelain = await git(['status', '--porcelain']);
  return {
    gitCommit: await git(['rev-parse', 'HEAD']),
    workingTreeClean: porcelain === '' ? true : porcelain === null ? null : false,
    electron: (await readJson(path.join(repositoryRoot, 'node_modules', 'electron', 'package.json'))).version,
    electronBuilder: (await readJson(path.join(repositoryRoot, 'node_modules', 'electron-builder', 'package.json'))).version,
    node: process.version,
    verifiedAt: new Date().toISOString(),
    note: 'build provenance, not a claim of reproducible builds',
  };
}
