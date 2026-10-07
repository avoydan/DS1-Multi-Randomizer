// Isolated experiment: how should the two .NET helpers (DsrDataTool, FogHost) be published?
//
//   node tools/release/helper-format-experiment.mjs <output folder> [--scan]
//
// Builds each helper in three formats into <output folder> (never into the release folder) and compares them:
//   A  single file, compressed          (what the application ships today)
//   B  single file, not compressed
//   C  self-contained folder (many files)
// For each: total size, number of files, mean start time of 5 runs with every .NET location hidden, and (--scan) what the local Microsoft
// Defender says. Nothing is signed, uploaded or installed, and the application's own format is NOT changed by this script: it produces numbers
// for a decision (docs/RELEASE_TRUST.md, "Helper packaging experiment").

import { spawn } from 'node:child_process';
import { access, mkdir, readdir, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { listFiles, parseArguments, repositoryRoot } from './release-lib.mjs';

const { flags, positional } = parseArguments(process.argv.slice(2));
if (positional.length !== 1) { console.error('Usage: node tools/release/helper-format-experiment.mjs <output folder> [--scan]'); process.exit(2); }
const output = path.resolve(positional[0]);
const localSdk = path.join(process.env.USERPROFILE ?? '', 'dn8sdk', 'dotnet.exe');
const dotnet = process.env.DS1TR_DOTNET_SDK ?? (await access(localSdk).then(() => localSdk, () => 'dotnet'));
const run = (command, args, env = process.env) => new Promise((resolve, reject) => {
  const child = spawn(command, args, { cwd: repositoryRoot, env, stdio: 'ignore', shell: false, windowsHide: true });
  child.on('error', reject);
  child.on('exit', (code) => (code === 0 ? resolve() : reject(new Error(`${command} ${args[0]} exited with ${code}`))));
});
const timeStart = (exe, args, env) => new Promise((resolve) => {
  const started = process.hrtime.bigint();
  const child = spawn(exe, args, { env, stdio: 'ignore', windowsHide: true });
  child.on('error', () => resolve(null));
  child.on('exit', (code) => resolve({ code, ms: Number(process.hrtime.bigint() - started) / 1e6 }));
});

const nuget = path.join(output, 'NuGet.Config');
await mkdir(output, { recursive: true });
await writeFile(nuget, '<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>');
const appData = path.join(output, 'appdata');
await mkdir(appData, { recursive: true });
const buildEnv = { ...process.env, APPDATA: appData };
const hidden = { SystemRoot: process.env.SystemRoot, PATH: path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32'), DOTNET_MULTILEVEL_LOOKUP: '0' };
const helpers = [
  { name: 'DsrDataTool', project: 'engines/combined/vendor/tools/DsrDataTool/DsrDataTool.csproj', probe: ['--help'] },
  { name: 'FogHost', project: 'tools/fog-host/FogHost.csproj', probe: [] },
];
const formats = [
  { id: 'A single file, compressed', args: ['-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true'] },
  { id: 'B single file, uncompressed', args: ['-p:PublishSingleFile=true'] },
  { id: 'C self-contained folder', args: ['-p:PublishSingleFile=false'] },
];
const rows = [];
for (const helper of helpers) {
  await run(dotnet, ['restore', helper.project, '-r', 'win-x64', '--configfile', nuget, '--ignore-failed-sources'], buildEnv);
  for (const format of formats) {
    const folder = path.join(output, helper.name, format.id[0]);
    await rm(folder, { recursive: true, force: true });
    await run(dotnet, ['publish', helper.project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', ...format.args, '-p:DebugType=None', '-p:DebugSymbols=false', '-o', folder], buildEnv);
    const files = await listFiles(folder);
    let total = 0;
    for (const file of files) total += (await stat(path.join(folder, file))).size;
    const exe = path.join(folder, `${helper.name}.exe`);
    const times = [];
    for (let index = 0; index < 5; index += 1) { const result = await timeStart(exe, helper.probe, hidden); if (result) times.push(result.ms); }
    rows.push({ helper: helper.name, format: format.id, folder, files: files.length, megabytes: Number((total / 1048576).toFixed(1)), exeMegabytes: Number(((await stat(exe)).size / 1048576).toFixed(1)), startMs: times.length ? Math.round(times.reduce((a, b) => a + b, 0) / times.length) : null });
  }
}
console.log('helper'.padEnd(12), 'format'.padEnd(30), 'files'.padStart(6), 'total MB'.padStart(9), 'exe MB'.padStart(8), 'start ms'.padStart(9));
for (const row of rows) console.log(row.helper.padEnd(12), row.format.padEnd(30), String(row.files).padStart(6), String(row.megabytes).padStart(9), String(row.exeMegabytes).padStart(8), String(row.startMs).padStart(9));
await writeFile(path.join(output, 'experiment.json'), JSON.stringify(rows, null, 2));
if (flags.has('scan')) {
  console.log('--- local Microsoft Defender scan (read-only; see tools/release/defender-scan.mjs) ---');
  await new Promise((resolve) => {
    const child = spawn(process.execPath, [path.join(repositoryRoot, 'tools', 'release', 'defender-scan.mjs'), output], { cwd: repositoryRoot, stdio: 'inherit', shell: false, windowsHide: true });
    child.on('exit', resolve);
    child.on('error', resolve);
  });
}
