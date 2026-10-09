import { execFileSync, spawn } from 'node:child_process';
import { cp, mkdir, rm, writeFile, access, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { stageRuntimeDependencies } from './stage-dependencies.mjs';

// Modes (docs/RELEASE_TRUST.md):
//   npm run package:win -w @ds1tr/desktop                 development / test build: the installer and the portable build, NOT signed
//   npm run package:win -w @ds1tr/desktop -- --release    PUBLIC release: signing is mandatory and the credentials come from the environment
//                                                          (CSC_LINK + CSC_KEY_PASSWORD, or DS1TR_SIGN_CERT_SHA1 / DS1TR_SIGN_CERT_SUBJECT for a certificate in the
//                                                          Windows store such as a hardware token, or DS1TR_ARTIFACT_SIGNING=1 with the Azure variables); without
//                                                          them the build stops before it starts. Nothing is ever read from the repository.
//   npm run package:win -w @ds1tr/desktop -- --channel=public --unsigned-release
//                                                          an UNSIGNED build meant to be published (docs/AUTO_UPDATE.md): public channel, the update source must be set, and the
//                                                          release verification runs with --publication. It is never reported as signed; the owner decides whether to publish it.
const releaseMode = process.argv.includes('--release');
const unsignedRelease = process.argv.includes('--unsigned-release');
// The CHANNEL decides what internal tools the interface shows and which GitHub releases the updater can inspect (docs/AUTO_UPDATE.md): `public` hides internal test worlds and ignores prereleases; `internal` shows them and opts into prerelease checks for QA. It is independent of signing: a --release build is always
// public; unsigned publication builds must also pass --channel=public. The channel never reaches a world.
const channelFlag = process.argv.find((argument) => argument.startsWith('--channel='))?.slice('--channel='.length);
if (channelFlag !== undefined && channelFlag !== 'public' && channelFlag !== 'internal') throw new Error('--channel must be public or internal.');
if (releaseMode && channelFlag === 'internal') throw new Error('A --release build is a public build: it cannot use the internal channel.');
if (unsignedRelease && releaseMode) throw new Error('--unsigned-release and --release exclude each other: --release signs, --unsigned-release is the honest name of a public build without a signature.');
if (unsignedRelease && channelFlag !== 'public') throw new Error('--unsigned-release is a public build: pass --channel=public.');
const channel = releaseMode ? 'public' : (channelFlag ?? 'internal');
const appVersion = JSON.parse(await readFile(path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'package.json'), 'utf8')).version;
const desktop = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = path.resolve(desktop, '../..');
const runtime = path.join(desktop, 'release', 'runtime');
const npmCli = process.env.npm_execpath ?? path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npm-cli.js');
const localSdk = path.join(process.env.USERPROFILE ?? '', 'dn8sdk', 'dotnet.exe');
const dotnet = process.env.DS1TR_DOTNET_SDK ?? (await access(localSdk).then(() => localSdk, () => 'dotnet'));
const run = (command, args, cwd = root, env = process.env) => new Promise((resolve, reject) => {
  const child = spawn(command, args, { cwd, env, stdio: 'inherit', shell: false, windowsHide: true });
  child.on('error', reject);
  child.on('exit', (code) => code === 0 ? resolve() : reject(new Error(`${command} exited with ${code}`)));
});
const copy = async (source, destination) => {
  await mkdir(path.dirname(destination), { recursive: true });
  await cp(source, destination, { recursive: true });
};

if (process.platform !== 'win32') throw new Error('Windows packaging must run on Windows.');
if (releaseMode) {
  const certificate = process.env.CSC_LINK || process.env.WIN_CSC_LINK;
  const artifactSigning = process.env.DS1TR_ARTIFACT_SIGNING === '1' && process.env.AZURE_TENANT_ID && process.env.AZURE_CLIENT_ID && process.env.AZURE_CLIENT_SECRET
    && process.env.DS1TR_ARTIFACT_SIGNING_ENDPOINT && process.env.DS1TR_ARTIFACT_SIGNING_ACCOUNT && process.env.DS1TR_ARTIFACT_SIGNING_PROFILE;
  const storeCertificate = process.env.DS1TR_SIGN_CERT_SHA1 || process.env.DS1TR_SIGN_CERT_SUBJECT;
  if (!certificate && !storeCertificate && !artifactSigning) {
    throw new Error('PUBLIC RELEASE refused: no signing credentials in the environment. Set CSC_LINK (and CSC_KEY_PASSWORD) for a code-signing certificate file, DS1TR_SIGN_CERT_SHA1 or DS1TR_SIGN_CERT_SUBJECT for a certificate in the Windows certificate store (a hardware token), or DS1TR_ARTIFACT_SIGNING=1 with the Azure Artifact Signing variables (docs/RELEASE_TRUST.md). A public release is never built unsigned.');
  }
  if (!process.env.DS1TR_EXPECTED_PUBLISHER) {
    throw new Error('PUBLIC RELEASE refused: DS1TR_EXPECTED_PUBLISHER is not set (the text the certificate subject must contain), so the signer could not be verified after the build.');
  }
}
await rm(runtime, { recursive: true, force: true });
await mkdir(runtime, { recursive: true });
const buildAppData = path.join(desktop, 'release', 'build-appdata');
await mkdir(buildAppData, { recursive: true });
const dotnetEnv = { ...process.env, APPDATA: buildAppData };
const nugetConfig = path.join(desktop, 'release', 'NuGet.Config');
await writeFile(nugetConfig, '<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>');
await run(process.execPath, [npmCli, 'run', 'build', '-w', '@ds1tr/contracts']);
await run(process.execPath, [npmCli, 'run', 'build', '-w', '@ds1tr/backend']);
await rm(path.join(desktop, 'dist-electron'), { recursive: true, force: true });
await run(process.execPath, [npmCli, 'run', 'build', '-w', '@ds1tr/desktop']);
// The helpers are published self-contained for win-x64, so a computer without any .NET installation can run them.
// Nothing at run time uses the dotnet CLI, PATH or a global runtime. Single file, NOT compressed: a compressed single-file bundle is one
// 34 MB block of 7.8 bits-per-byte data, the profile antivirus heuristics associate with packed executables, and it is decompressed at every start (DsrDataTool started in 1,054 ms against 373 ms, FogHost in 315 ms against 384 ms). The uncompressed bundle (6.7 bits per byte, like the Electron executable itself) is 32 MB larger on disk and signs the same
// (docs/RELEASE_TRUST.md, "Helper packaging experiment"; tools/release/helper-format-experiment.mjs reproduces the numbers).
const publishArgs = ['-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
  '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false'];
const dataTool = path.join(runtime, 'engines', 'combined', 'vendor', 'tools', 'DsrDataTool');
const fogHost = path.join(runtime, 'tools', 'fog-host');
for (const [project, output, probe] of [
  ['engines/combined/vendor/tools/DsrDataTool/DsrDataTool.csproj', dataTool, ['--help']],
  ['tools/fog-host/FogHost.csproj', fogHost, []],
]) {
  await run(dotnet, ['restore', project, '-r', 'win-x64', '--configfile', nugetConfig, '--ignore-failed-sources'], root, dotnetEnv);
  await run(dotnet, ['publish', project, ...publishArgs, '-o', output], root, dotnetEnv);
  // Smoke test with every .NET location hidden: the helper must start from its own bundled runtime.
  const hidden = { SystemRoot: process.env.SystemRoot, PATH: path.join(process.env.SystemRoot ?? 'C:\Windows', 'System32'), DOTNET_MULTILEVEL_LOOKUP: '0' };
  const exe = path.join(output, path.basename(project, '.csproj') + '.exe');
  await new Promise((resolve, reject) => {
    const child = spawn(exe, probe, { env: hidden, stdio: 'ignore', windowsHide: true });
    child.on('error', reject);
    child.on('exit', (code) => (code === 0 || code === 2 ? resolve() : reject(new Error(`${exe} did not start on its own (exit ${code}).`))));
  });
}
await copy(path.join(root, 'apps/backend/dist'), path.join(runtime, 'apps/backend/dist'));
// The backend imports @ds1tr/contracts at run time; the packaged runtime has no workspace node_modules, so it is staged where Node resolves it.
await copy(path.join(root, 'packages/contracts/dist'), path.join(runtime, 'node_modules/@ds1tr/contracts/dist'));
await copy(path.join(root, 'packages/contracts/package.json'), path.join(runtime, 'node_modules/@ds1tr/contracts/package.json'));
await copy(path.join(root, 'engines/combined/vendor/src'), path.join(runtime, 'engines/combined/vendor/src'));
await copy(path.join(root, 'engines/combined/vendor/package.json'), path.join(runtime, 'engines/combined/vendor/package.json'));
const notices = path.join(runtime, 'licenses');
await mkdir(notices, { recursive: true });
for (const [source, target] of [
  ['engines/combined/vendor/LICENSE', 'combined-engine-GPL-3.0.txt'],
  ['engines/combined/vendor/THIRD_PARTY_NOTICES.md', 'combined-engine-THIRD_PARTY_NOTICES.md'],
  ['tools/fog-host/vendor/FogMod/LICENSE', 'FogMod-Apache-2.0.txt'],
  ['tools/fog-host/vendor/SoulsIds/LICENSE', 'SoulsIds-license.txt'],
  ['tools/fog-host/vendor/UPSTREAM_PIN.md', 'FogHost-upstream-pin.md'],
  ['docs/LICENSING.md', 'distribution-policy.md'],
  ['docs/FOG_DATA_PROVENANCE.md', 'world-randomizer-data-provenance.md'],
  ['node_modules/react/LICENSE', 'React-MIT.txt'],
  ['node_modules/react-dom/LICENSE', 'React-DOM-MIT.txt'],
  ['node_modules/scheduler/LICENSE', 'Scheduler-MIT.txt'],
  ['apps/desktop/licenses/YamlDotNet-MIT.txt', 'YamlDotNet-MIT.txt'],
  ['apps/desktop/licenses/Newtonsoft.Json-MIT.md', 'Newtonsoft.Json-MIT.md'],
  ['apps/desktop/licenses/ZstdSharp.Port-MIT.txt', 'ZstdSharp.Port-MIT.txt'],
]) await copy(path.join(root, source), path.join(notices, target));
await copy(path.join(root, 'engines/combined/vendor/UPSTREAM_PIN.md'), path.join(notices, 'combined-engine-upstream-pin.md'));
await writeFile(path.join(notices, 'README.txt'), 'DS1 Multi Randomizer is GPL-3.0-only. Third-party notices and pinned source details are in this folder. Electron and Chromium licenses are in the application root. JuicerMV.SoulsFormats 0.1.3 declares GPL-3.0-only; ZstdSharp.Port 0.8.1, YamlDotNet 7.0.0, and Newtonsoft.Json 13.0.3 declare MIT. The static data files of FogMod (annotations, layouts, and event and talk scripts edited by its author) are never bundled: the application fetches them once, over HTTPS, from the FogMod repository (Apache-2.0, https://github.com/thefifthmatt/FogMod) at the pinned commit, verifies the SHA-256 of every file and keeps them in its own cache; the map and parameter files the world randomizer needs are copied from the own Dark Souls Remastered installation of the player (see world-randomizer-data-provenance.md). The .NET helper programs are published self-contained and include the .NET runtime (MIT, Microsoft). The public corresponding-source fork and release tag are required before publication.\n');
// Build information the running application can report in its diagnostics and support bundle (docs/DIAGNOSTICS.md). The signing state is NOT recorded as a
// fact here: a public build (--release) is only accepted after tools/release/release-verify.mjs required a trusted signature, a development build is unsigned.
const readVersion = async (file) => JSON.parse(await readFile(path.join(root, file), 'utf8')).version;
const fogHostProject = /<Version>([^<]+)<\/Version>/u.exec(await readFile(path.join(root, 'tools/fog-host/FogHost.csproj'), 'utf8'))?.[1] ?? null;
let commit = null;
try { commit = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, windowsHide: true }).toString().trim(); } catch { commit = null; }
await writeFile(path.join(runtime, 'build-info.json'), `${JSON.stringify({
  schema: 'ds1tr-build-info-1', version: appVersion, commit, mode: releaseMode ? 'public' : unsignedRelease ? 'unsigned-release' : 'development', channel,
  versions: { desktop: appVersion, backend: await readVersion('apps/backend/package.json'), contracts: await readVersion('packages/contracts/package.json'), foghostProject: fogHostProject },
  builtAt: new Date().toISOString(),
}, null, 2)}\n`);
const packageApp = path.join(desktop, 'release', 'package-app');
await rm(packageApp, { recursive: true, force: true });
await copy(path.join(desktop, 'dist'), path.join(packageApp, 'dist'));
await copy(path.join(desktop, 'dist-electron'), path.join(packageApp, 'dist-electron'));
// The one runtime dependency of the desktop shell is the updater (docs/AUTO_UPDATE.md): its closure is staged from the workspace as the lockfile pinned it, so the packaged
// application never installs anything from the network. They live in dist-electron/node_modules because electron-builder never walks a node_modules folder at the root of the app
// (beforeBuild returns false: the dependencies are handled here), while a nested one is copied by the dist-electron/** pattern; Node resolves them from main.js upwards.
const desktopManifest = JSON.parse(await readFile(path.join(desktop, 'package.json'), 'utf8'));
const runtimeDependencies = { 'electron-updater': desktopManifest.dependencies['electron-updater'] };
const stagedDependencies = await stageRuntimeDependencies({ root, names: Object.keys(runtimeDependencies), destination: path.join(packageApp, 'dist-electron', 'node_modules') });
console.log(`staged ${stagedDependencies.length} runtime packages for the updater`);
// No productName here on purpose: Electron derives the user data folder (settings, history, presets, state) from this package name, and it must not move (docs/RELEASE_TRUST.md).
await writeFile(path.join(packageApp, 'package.json'), JSON.stringify({
  name: 'ds1-triple-randomizer-desktop', version: appVersion, main: 'dist-electron/main.js', author: { name: 'DS1 Multi Randomizer project' },
  type: 'module', license: 'GPL-3.0-only', description: 'Dark Souls Remastered multi randomizer', dependencies: runtimeDependencies,
}, null, 2));
const signingOverrides = !releaseMode ? [] : [
  '-c.win.signExecutable=true', '-c.win.forceCodeSigning=true',
  ...(process.env.DS1TR_SIGN_CERT_SHA1 ? [`-c.win.signtoolOptions.certificateSha1=${process.env.DS1TR_SIGN_CERT_SHA1}`] : []),
  ...(process.env.DS1TR_SIGN_CERT_SUBJECT ? [`-c.win.signtoolOptions.certificateSubjectName=${process.env.DS1TR_SIGN_CERT_SUBJECT}`] : []),
  ...(process.env.DS1TR_TIMESTAMP_URL ? [`-c.win.signtoolOptions.rfc3161TimeStampServer=${process.env.DS1TR_TIMESTAMP_URL}`] : []),
  ...(process.env.DS1TR_ARTIFACT_SIGNING === '1' ? [
    `-c.win.azureSignOptions.endpoint=${process.env.DS1TR_ARTIFACT_SIGNING_ENDPOINT}`, `-c.win.azureSignOptions.codeSigningAccountName=${process.env.DS1TR_ARTIFACT_SIGNING_ACCOUNT}`,
    `-c.win.azureSignOptions.certificateProfileName=${process.env.DS1TR_ARTIFACT_SIGNING_PROFILE}`] : []),
];
await run(process.execPath, [path.join(root, 'node_modules', 'electron-builder', 'cli.js'), '--win', '--x64', '--config', 'electron-builder.yml', '--publish', 'never', ...signingOverrides], desktop,
  { ...process.env, ELECTRON_BUILDER_CACHE: path.join(desktop, 'release', 'builder-cache') });
// Whatever was built is verified at once: structure, versions, metadata, and (public mode) the signatures. Development builds report UNSIGNED and pass.
await run(process.execPath, [path.join(root, 'tools', 'release', 'release-verify.mjs'), '--manifest', path.join(desktop, 'release', 'release-manifest.json'), ...(releaseMode ? ['--require-signature'] : []), ...(unsignedRelease ? ['--publication'] : [])], root);
