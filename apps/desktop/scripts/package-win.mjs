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
const buildArgs = ['build', '-c', 'Release', '--no-restore'];
const dataTool = path.join(runtime, 'engines', 'combined', 'vendor', 'tools', 'DsrDataTool');
const fogHost = path.join(runtime, 'tools', 'fog-host');
for (const [project, output] of [
  ['engines/combined/vendor/tools/DsrDataTool/DsrDataTool.csproj', dataTool],
  ['tools/fog-host/FogHost.csproj', fogHost],
]) {
  await run(dotnet, ['restore', project, '--configfile', nugetConfig, '--ignore-failed-sources'], root, dotnetEnv);
  await run(dotnet, [...buildArgs, project], root, dotnetEnv);
  await copy(path.join(root, path.dirname(project), 'bin', 'Release', 'net8.0'), output);
}
await copy(path.join(root, 'apps/backend/dist'), path.join(runtime, 'apps/backend/dist'));
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
  ['node_modules/react/LICENSE', 'React-MIT.txt'],
  ['node_modules/react-dom/LICENSE', 'React-DOM-MIT.txt'],
  ['node_modules/scheduler/LICENSE', 'Scheduler-MIT.txt'],
  ['apps/desktop/licenses/YamlDotNet-MIT.txt', 'YamlDotNet-MIT.txt'],
  ['apps/desktop/licenses/Newtonsoft.Json-MIT.md', 'Newtonsoft.Json-MIT.md'],
  ['apps/desktop/licenses/ZstdSharp.Port-MIT.txt', 'ZstdSharp.Port-MIT.txt'],
]) await copy(path.join(root, source), path.join(notices, target));
await copy(path.join(root, 'engines/combined/vendor/UPSTREAM_PIN.md'), path.join(notices, 'combined-engine-upstream-pin.md'));
await writeFile(path.join(notices, 'README.txt'), 'DS1 Triple Randomizer is GPL-3.0-only. Third-party notices and pinned source details are in this folder. Electron and Chromium licenses are in the application root. JuicerMV.SoulsFormats 0.1.3 declares GPL-3.0-only; ZstdSharp.Port 0.8.1, YamlDotNet 7.0.0, and Newtonsoft.Json 13.0.3 declare MIT. FogMod game-derived dist/DS1R data is never bundled. The public corresponding-source fork and release tag are required before publication.\n');
const packageApp = path.join(desktop, 'release', 'package-app');
await rm(packageApp, { recursive: true, force: true });
await copy(path.join(desktop, 'dist'), path.join(packageApp, 'dist'));
await copy(path.join(desktop, 'dist-electron'), path.join(packageApp, 'dist-electron'));
await writeFile(path.join(packageApp, 'package.json'), JSON.stringify({
  name: 'ds1-triple-randomizer-desktop', version: '0.8.0', main: 'dist-electron/main.js',
  type: 'module', license: 'GPL-3.0-only', description: 'Dark Souls Remastered triple randomizer',
}, null, 2));
await run(process.execPath, [path.join(root, 'node_modules', 'electron-builder', 'cli.js'), '--win', 'portable', '--x64', '--config', 'electron-builder.yml', '--publish', 'never'], desktop,
  { ...process.env, ELECTRON_BUILDER_CACHE: path.join(desktop, 'release', 'builder-cache') });
