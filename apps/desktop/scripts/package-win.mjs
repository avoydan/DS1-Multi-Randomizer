import { spawn } from 'node:child_process';
import { cp, mkdir, rm, writeFile, access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

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
// Nothing at run time uses the dotnet CLI, PATH or a global runtime. Single-file with compression keeps the two helpers
// (each carries its own runtime) at about 35 MB instead of 75 MB each.
const publishArgs = ['-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
  '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false'];
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
await writeFile(path.join(notices, 'README.txt'), 'DS1 Triple Randomizer is GPL-3.0-only. Third-party notices and pinned source details are in this folder. Electron and Chromium licenses are in the application root. JuicerMV.SoulsFormats 0.1.3 declares GPL-3.0-only; ZstdSharp.Port 0.8.1, YamlDotNet 7.0.0, and Newtonsoft.Json 13.0.3 declare MIT. The static data files of FogMod (annotations, layouts, and event and talk scripts edited by its author) are never bundled: the application fetches them once, over HTTPS, from the FogMod repository (Apache-2.0, https://github.com/thefifthmatt/FogMod) at the pinned commit, verifies the SHA-256 of every file and keeps them in its own cache; the map and parameter files the world randomizer needs are copied from the own Dark Souls Remastered installation of the player (see world-randomizer-data-provenance.md). The .NET helper programs are published self-contained and include the .NET runtime (MIT, Microsoft). The public corresponding-source fork and release tag are required before publication.\n');
const packageApp = path.join(desktop, 'release', 'package-app');
await rm(packageApp, { recursive: true, force: true });
await copy(path.join(desktop, 'dist'), path.join(packageApp, 'dist'));
await copy(path.join(desktop, 'dist-electron'), path.join(packageApp, 'dist-electron'));
await writeFile(path.join(packageApp, 'package.json'), JSON.stringify({
  name: 'ds1-triple-randomizer-desktop', version: '0.22.1', main: 'dist-electron/main.js',
  type: 'module', license: 'GPL-3.0-only', description: 'Dark Souls Remastered triple randomizer',
}, null, 2));
await run(process.execPath, [path.join(root, 'node_modules', 'electron-builder', 'cli.js'), '--win', 'portable', '--x64', '--config', 'electron-builder.yml', '--publish', 'never'], desktop,
  { ...process.env, ELECTRON_BUILDER_CACHE: path.join(desktop, 'release', 'builder-cache') });
