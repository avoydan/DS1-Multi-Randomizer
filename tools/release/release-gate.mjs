// The hard-fail checklist of a PUBLIC release (docs/RELEASE_TRUST.md, "Release gate").
//
//   node tools/release/release-gate.mjs --public --expected-publisher "<signer subject text>"     all checks, signature required
//   node tools/release/release-gate.mjs                                                           development: the same checks, signature not required
//
// Checks, in order; the first failure does not stop the others, so one run reports everything that is wrong:
//   1. the working tree is clean and the commit is recorded (provenance)
//   2. the desktop typecheck, the desktop tests and the backend tests pass
//   3. golden compatibility passes (needs DS1TR_GOLDEN_GAME_SOURCE and the built helpers; a public release cannot skip it)
//   4. release-verify: expected executables, versions (application, FogHost, Fog pin), icon, no Electron menu, no baggage, licenses,
//      per-user installer without elevation, and, in public mode, a valid trusted signature with timestamp on every DS1TR executable and artifact
//   5. the release manifest is written next to the artifacts
// It builds nothing, signs nothing and publishes nothing.

import { spawn } from 'node:child_process';
import path from 'node:path';
import { parseArguments, repositoryRoot } from './release-lib.mjs';

const { flags, values } = parseArguments(process.argv.slice(2));
const publicMode = flags.has('public');
const npmCli = process.env.npm_execpath ?? path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npm-cli.js');
const run = (command, args, env = process.env) => new Promise((resolve) => {
  const child = spawn(command, args, { cwd: repositoryRoot, env, stdio: 'inherit', shell: false, windowsHide: true });
  child.on('error', () => resolve(1));
  child.on('exit', (code) => resolve(code ?? 1));
});
const capture = (command, args) => new Promise((resolve) => {
  const child = spawn(command, args, { cwd: repositoryRoot, shell: false, windowsHide: true });
  let out = '';
  child.stdout.on('data', (chunk) => { out += chunk; });
  child.on('error', () => resolve(null));
  child.on('exit', (code) => resolve(code === 0 ? out.trim() : null));
});

const results = [];
const record = (name, ok, detail = '') => { results.push({ name, ok, detail }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ` - ${detail}` : ''}`); };

const status = await capture('git', ['status', '--porcelain']);
const commit = await capture('git', ['rev-parse', 'HEAD']);
if (publicMode) record('working tree is clean (a release is built from a committed tree)', status === '', status ? 'uncommitted changes exist' : '');
else console.log(`INFO  working tree ${status === '' ? 'clean' : 'has uncommitted changes (allowed in development mode)'}; commit ${commit}`);
record('commit is known', Boolean(commit), commit ?? 'git is unavailable');

record('desktop typecheck', await run(process.execPath, [npmCli, 'run', 'typecheck', '-w', '@ds1tr/desktop']) === 0);
record('desktop tests', await run(process.execPath, [npmCli, 'test', '-w', '@ds1tr/desktop']) === 0);
record('backend tests', await run(process.execPath, [npmCli, 'test', '-w', '@ds1tr/backend']) === 0);

if (process.env.DS1TR_GOLDEN_GAME_SOURCE) {
  record('golden compatibility (every earlier world still byte for byte)', await run(process.execPath, ['apps/backend/scripts/goldenCompatibility.mjs', 'verify']) === 0);
} else if (publicMode) record('golden compatibility', false, 'DS1TR_GOLDEN_GAME_SOURCE is not set: a public release cannot skip it');
else console.log('SKIP  golden compatibility (DS1TR_GOLDEN_GAME_SOURCE is not set; required in public mode)');

const manifest = values.get('manifest') ?? path.join('apps', 'desktop', 'release', 'release-manifest.json');
const verifyArguments = ['tools/release/release-verify.mjs', '--manifest', manifest];
if (publicMode) verifyArguments.push('--require-signature');
if (values.get('expected-publisher')) verifyArguments.push('--expected-publisher', values.get('expected-publisher'));
record(publicMode ? 'release verification (signatures required)' : 'release verification', await run(process.execPath, verifyArguments) === 0);

const failed = results.filter((entry) => !entry.ok);
console.log(failed.length === 0
  ? `RELEASE GATE: PASS (${publicMode ? 'public' : 'development'} mode). The manifest is ${manifest}.`
  : `RELEASE GATE: FAIL (${failed.length}): ${failed.map((entry) => entry.name).join('; ')}`);
process.exitCode = failed.length === 0 ? 0 : 1;
