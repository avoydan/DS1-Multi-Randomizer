import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { createRequire } from 'node:module';
import {
  checkAsarRuntimeDependencies, checkLatestYml, checkUpdateConfiguration, findSecrets, isConfiguredSource, parseBuilderPublish, parseFlatYaml, parseUpdateSourceTs, sha512Base64, versionOfTag,
} from './update-lib.mjs';
import { repositoryRoot, windowsPowerShellEnvironment } from './release-lib.mjs';

// The update rules of the release tooling (docs/AUTO_UPDATE.md): pure checks over text and numbers, plus structural checks of the committed workflow and configuration.
// Nothing here builds, signs, uploads or contacts GitHub.

const read = (...parts) => readFile(path.join(repositoryRoot, ...parts), 'utf8');
const requireFromRoot = createRequire(path.join(repositoryRoot, 'package.json'));
const SOURCE = { provider: 'github', owner: 'ds1-owner', repo: 'DS1-Multi-Randomizer' };
const UPDATER = 'updater.autoDownload = false; updater.autoInstallOnAppQuit = false; updater.allowPrerelease = false; updater.allowDowngrade = false;';

test('the committed update source is explicit, matches electron-builder.yml, and is still the placeholder until the owner chooses (a development build warns, a publication fails)', async () => {
  const sourceTs = parseUpdateSourceTs(await read('apps', 'desktop', 'electron', 'updateSource.ts'));
  const builder = parseBuilderPublish(await read('apps', 'desktop', 'electron-builder.yml'));
  const updater = await read('apps', 'desktop', 'electron', 'updater.ts');
  assert.equal(sourceTs.provider, 'github');
  assert.deepEqual({ provider: builder.provider, owner: builder.owner, repo: builder.repo }, sourceTs, 'the code and electron-builder.yml carry the same three values');
  assert.equal(builder.releaseType, 'release');
  const development = checkUpdateConfiguration({ sourceTs, builderPublish: builder, updaterText: updater, publication: false });
  assert.deepEqual(development.filter((entry) => entry.level === 'FAIL'), [], 'a consistent configuration passes in development mode');
  if (!isConfiguredSource(sourceTs)) {
    assert.ok(development.some((entry) => entry.level === 'WARN' && /not set/u.test(entry.message)));
    const publication = checkUpdateConfiguration({ sourceTs, builderPublish: builder, updaterText: updater, publication: true });
    assert.ok(publication.some((entry) => entry.level === 'FAIL' && /cannot be published/u.test(entry.message)), 'an unset source can never be published');
  }
});

test('the configuration check refuses every way the update source can drift or leak', () => {
  const ok = { sourceTs: SOURCE, builderPublish: { provider: 'github', owner: 'ds1-owner', repo: 'DS1-Multi-Randomizer', releaseType: 'release' }, updaterText: UPDATER, publication: true };
  assert.deepEqual(checkUpdateConfiguration(ok).filter((entry) => entry.level === 'FAIL'), []);
  const failures = (patch) => checkUpdateConfiguration({ ...ok, ...patch }).filter((entry) => entry.level === 'FAIL').map((entry) => entry.message).join(' | ');
  assert.match(failures({ builderPublish: { ...ok.builderPublish, repo: 'Other' } }), /disagree on repo/u);
  assert.match(failures({ builderPublish: { ...ok.builderPublish, provider: 'generic' } }), /must be github/u);
  assert.match(failures({ builderPublish: { ...ok.builderPublish, token: 'x' } }), /publish\.token is not allowed/u);
  assert.match(failures({ builderPublish: { ...ok.builderPublish, private: 'true' } }), /publish\.private is not allowed/u);
  assert.match(failures({ builderPublish: null }), /no publish block/u);
  assert.match(failures({ updaterText: UPDATER.replace('autoInstallOnAppQuit = false', 'autoInstallOnAppQuit = true') }), /autoInstallOnAppQuit/u);
  assert.match(failures({ updaterText: UPDATER.replace('allowPrerelease = false', '') }), /allowPrerelease/u);
  assert.match(failures({ updaterText: UPDATER.replace('autoDownload = false', '') }), /autoDownload/u);
  assert.match(failures({ appUpdate: { provider: 'github', owner: 'someone-else', repo: 'DS1-Multi-Randomizer' } }), /app-update\.yml says owner/u);
  assert.match(failures({ appUpdate: { provider: 'github', owner: 'ds1-owner', repo: 'DS1-Multi-Randomizer', token: 'abc' } }), /credential field/u);
  assert.match(failures({ sourceTs: { ...SOURCE, owner: 'REPLACE_WITH_GITHUB_OWNER' }, builderPublish: { ...ok.builderPublish, owner: 'REPLACE_WITH_GITHUB_OWNER' } }), /cannot be published/u);
  // The unsigned state is stated, not hidden.
  const notes = checkUpdateConfiguration({ ...ok, appUpdate: { provider: 'github', owner: 'ds1-owner', repo: 'DS1-Multi-Randomizer' } }).map((entry) => entry.message).join(' | ');
  assert.match(notes, /no publisherName/u);
});

test('latest.yml must describe the installer it was generated with, byte for byte', async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-update-lib-'));
  try {
    const installer = path.join(directory, 'DS1-Multi-Randomizer-0.36.0-Setup-x64.exe');
    const bytes = Buffer.from('not a real installer but with stable content');
    await writeFile(installer, bytes);
    const sha512 = await sha512Base64(installer);
    assert.equal(sha512, createHash('sha512').update(bytes).digest('base64'));
    const text = [
      'version: 0.36.0', 'files:', '  - url: DS1-Multi-Randomizer-0.36.0-Setup-x64.exe', `    sha512: ${sha512}`, `    size: ${bytes.length}`,
      'path: DS1-Multi-Randomizer-0.36.0-Setup-x64.exe', `sha512: ${sha512}`, "releaseDate: '2026-10-10T10:00:00.000Z'", '',
    ].join('\n');
    const latest = parseFlatYaml(text);
    const base = { latest, version: '0.36.0', installerName: 'DS1-Multi-Randomizer-0.36.0-Setup-x64.exe', installerSha512: sha512, installerSize: bytes.length, tag: 'v0.36.0', blockmapPresent: true };
    assert.deepEqual(checkLatestYml(base), []);
    const failing = (patch) => checkLatestYml({ ...base, ...patch }).map((entry) => entry.message).join(' | ');
    assert.match(failing({ installerSha512: 'other' }), /sha512 of the installer does not match/u);
    assert.match(failing({ installerSize: bytes.length + 1 }), /size .* is not the installer size/u);
    assert.match(failing({ version: '0.36.1' }), /says version 0\.36\.0/u);
    assert.match(failing({ tag: 'v0.35.1' }), /requested tag v0\.35\.1 is not v0\.36\.0/u);
    assert.match(failing({ installerName: 'DS1-Multi-Randomizer-0.36.1-Setup-x64.exe' }), /lists no file named/u);
    assert.match(failing({ blockmapPresent: false }), /blockmap.*missing/u);
    assert.match(failing({ latest: { ...latest, releaseDate: undefined } }), /no valid releaseDate/u);
  } finally { await rm(directory, { recursive: true, force: true }); }
});

test('a tag is a plain version with an optional v; nothing else is a release', () => {
  assert.equal(versionOfTag('v0.36.0'), '0.36.0');
  assert.equal(versionOfTag('0.36.0'), '0.36.0');
  for (const bad of ['v0.36', 'v0.36.0-beta.1', 'latest', '', null, 'v0.36.0 ', 'vv0.36.0']) assert.equal(versionOfTag(bad), null, String(bad));
});

test('tokens and private keys are recognised in anything that is packaged', () => {
  assert.deepEqual(findSecrets('nothing to see'), []);
  assert.equal(findSecrets(`token = ghp_${'a'.repeat(36)}`).length, 1);
  assert.equal(findSecrets(`github_pat_${'A1_'.repeat(20)}`).length, 1);
  assert.equal(findSecrets('-----BEGIN PRIVATE KEY-----').length, 1);
  assert.equal(findSecrets('-----BEGIN RSA PRIVATE KEY-----').length, 1);
});

test('the release workflow is manual, creates a DRAFT only, asks for the least privilege and never takes untrusted text into a script', async () => {
  const yaml = requireFromRoot('js-yaml');
  const text = await read('.github', 'workflows', 'release.yml');
  const workflow = yaml.load(text);
  assert.deepEqual(Object.keys(workflow.on), ['workflow_dispatch'], 'it runs only when started by hand: not on push, pull request, tag or schedule');
  assert.deepEqual(workflow.permissions, { contents: 'read' }, 'read-only by default');
  const job = workflow.jobs.release;
  assert.deepEqual(job.permissions, { contents: 'write' }, 'the one job raises only contents: write');
  assert.equal(Object.keys(workflow.jobs).length, 1);
  assert.equal(job['runs-on'], 'windows-latest');
  const steps = job.steps;
  const runs = steps.map((step) => step.run ?? '');
  for (const run of runs) assert.doesNotMatch(run, /\$\{\{\s*(inputs|github\.event)/u, 'inputs reach scripts only through env variables');
  assert.deepEqual(steps.filter((step) => step.uses).map((step) => step.uses.split('@')[0]).sort(), ['actions/checkout', 'actions/setup-dotnet', 'actions/setup-node', 'actions/upload-artifact'], 'only first-party actions');
  assert.equal(steps.find((step) => step.uses?.startsWith('actions/checkout')).with['persist-credentials'], false, 'the checkout does not leave a token in .git/config');
  const index = (pattern) => steps.findIndex((step) => pattern.test(`${step.name ?? ''} ${step.run ?? ''}`));
  assert.ok(index(/npm ci/u) >= 0 && index(/npm test -w @ds1tr\/backend/u) > index(/npm ci/u), 'locked install, then backend tests');
  assert.ok(index(/npm run typecheck/u) > index(/npm test -w @ds1tr\/backend/u));
  assert.ok(index(/npm test -w @ds1tr\/desktop/u) > index(/npm run typecheck/u));
  assert.ok(index(/package:win/u) > index(/npm test -w @ds1tr\/desktop/u), 'the tests come before the build');
  assert.ok(index(/release-assets\.mjs/u) > index(/package:win/u));
  assert.ok(index(/gh release create/u) > index(/release-assets\.mjs/u), 'the draft is created last, after verification');
  const create = runs.find((run) => /gh release create/u.test(run));
  assert.match(create, /--draft/u);
  assert.doesNotMatch(text, /--prerelease|--latest|release edit|draft=false|--discussion/u, 'it can neither publish nor mark a release');
  assert.match(runs.find((run) => /package:win/u.test(run)), /--channel=public --unsigned-release/u);
  assert.match(runs.find((run) => /release-verify\.mjs/u.test(run)), /--publication --tag "v\$VERSION"/u);
  const secrets = [...text.matchAll(/secrets\.([A-Z_]+)/gu)].map((match) => match[1]);
  assert.deepEqual([...new Set(secrets)], ['RELEASE_REPO_TOKEN'], 'the only secret is the optional token for a separate release repository');
  assert.match(text, /update-source\.mjs/u, 'the upload target is compared with the repository the clients follow');
});

test('the application and the release metadata never carry a token or a private flag', async () => {
  const builder = await read('apps', 'desktop', 'electron-builder.yml');
  const active = builder.split(/\r?\n/u).filter((line) => !line.trim().startsWith('#')).join('\n');
  assert.doesNotMatch(active, /(token|GH_TOKEN|private:\s*true)/iu);
  const updater = await read('apps', 'desktop', 'electron', 'updater.ts');
  const main = await read('apps', 'desktop', 'electron', 'main.ts');
  assert.equal(findSecrets(`${updater}\n${main}\n${builder}`).length, 0);
  assert.doesNotMatch(`${updater}\n${main}`, /requestHeaders|setFeedURL\(\{[^}]*token/u);
});

test('the updater and everything it requires must be inside app.asar, resolved the way Node resolves them from dist-electron', () => {
  const manifests = {
    'dist-electron/node_modules/electron-updater/package.json': { main: 'out/main.js', dependencies: { 'builder-util-runtime': '1', debug: '4' } },
    'dist-electron/node_modules/builder-util-runtime/package.json': { main: 'out/index.js', dependencies: { debug: '4' } },
    'dist-electron/node_modules/debug/package.json': { main: 'src/index.js', dependencies: { ms: '2' } },
    'dist-electron/node_modules/ms/package.json': { main: 'index' },
  };
  const files = [...Object.keys(manifests), 'dist-electron/node_modules/electron-updater/out/main.js', 'dist-electron/node_modules/builder-util-runtime/out/index.js', 'dist-electron/node_modules/debug/src/index.js', 'dist-electron/node_modules/ms/index.js'];
  const readJson = (entry) => manifests[entry];
  assert.deepEqual(checkAsarRuntimeDependencies({ entries: files, readJson }), []);
  const without = (name) => files.filter((entry) => !entry.includes(name));
  assert.match(checkAsarRuntimeDependencies({ entries: without('/electron-updater/'), readJson })[0].message, /does not contain electron-updater/u);
  assert.match(checkAsarRuntimeDependencies({ entries: without('/ms/'), readJson })[0].message, /does not contain ms/u);
  assert.match(checkAsarRuntimeDependencies({ entries: files.filter((entry) => entry !== 'dist-electron/node_modules/electron-updater/out/main.js'), readJson })[0].message, /lacks its entry file/u);
  assert.match(checkAsarRuntimeDependencies({ entries: files.map((entry) => entry.replace('dist-electron/node_modules', 'node_modules')), readJson })[0].message, /does not contain electron-updater/u, 'a root node_modules is not where the shell looks');
});

test('Windows PowerShell started from PowerShell 7 gets no inherited PSModulePath (the hosted runner failed to load Get-AuthenticodeSignature with it)', () => {
  const environment = windowsPowerShellEnvironment({ Path: 'C:/x', PSModulePath: 'C:/Program Files/PowerShell/7/Modules', psmodulepath: 'again' }, { DS1TR_RELEASE_LIST: 'list.json' });
  assert.deepEqual(environment, { Path: 'C:/x', DS1TR_RELEASE_LIST: 'list.json' });
});
