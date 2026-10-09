// The update checks of the release tooling (docs/AUTO_UPDATE.md). Pure functions over text and numbers, so every rule is tested without a build (update-lib.test.mjs);
// release-verify.mjs and release-assets.mjs do the reading of files and call these. Build-time only: nothing here ships in the application.
//
// What is checked, and why: an installed client trusts exactly two things about an update, the repository it follows and the metadata (latest.yml) of the release
// it finds there. So the release must (1) name the same repository in the code, in electron-builder.yml and in the packaged app-update.yml, (2) carry a latest.yml
// generated from the SAME build as the installer (version, file name, size and sha512 match the installer byte for byte), (3) carry the blockmap of that installer
// and (4) be the version that was asked for (the tag). None of it replaces a code-signing certificate; see docs/AUTO_UPDATE.md, "Unsigned builds".

import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';

export const UNSET_OWNER = 'REPLACE_WITH_GITHUB_OWNER';

/** `owner`/`repo` of a GitHub repository name part: the same rule the application applies (electron/updater.ts, isUpdateSourceConfigured). */
const OWNER = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$/u;
const REPO = /^[A-Za-z0-9._-]{1,100}$/u;
export const isConfiguredSource = (source) => source?.provider === 'github' && OWNER.test(source.owner ?? '') && source.owner !== UNSET_OWNER && REPO.test(source.repo ?? '');

/** The three values of electron/updateSource.ts. */
export function parseUpdateSourceTs(text) {
  const block = /export const UPDATE_SOURCE[^=]*=\s*\{([\s\S]*?)\};/u.exec(text)?.[1] ?? '';
  const field = (name) => new RegExp(`${name}:\\s*'([^']*)'`, 'u').exec(block)?.[1] ?? null;
  return { provider: field('provider'), owner: field('owner'), repo: field('repo') };
}

/** The `publish:` block of electron-builder.yml (flat keys only; anything nested or unknown is returned as a key so that the checks can refuse it). */
export function parseBuilderPublish(text) {
  const lines = text.replace(/\r\n/gu, '\n').split('\n');
  const start = lines.findIndex((line) => /^publish:\s*$/u.test(line));
  if (start < 0) return null;
  const values = {};
  for (let index = start + 1; index < lines.length; index += 1) {
    const line = lines[index];
    if (line.trim() === '' || /^\s*#/u.test(line)) continue;
    const match = /^ {2}([A-Za-z]+):\s*(.*?)\s*$/u.exec(line);
    if (!match) break;
    values[match[1]] = match[2].replace(/^['"]|['"]$/gu, '');
  }
  return values;
}

/** The parts of an app-update.yml / latest.yml that matter, from the flat YAML electron-builder writes (no YAML library: the files are tiny and flat). */
export function parseFlatYaml(text) {
  const result = {};
  const files = [];
  let current = null;
  for (const line of text.replace(/\r\n/gu, '\n').split('\n')) {
    let match = /^([A-Za-z][A-Za-z0-9]*):\s*(.*)$/u.exec(line);
    if (match && !line.startsWith(' ')) { result[match[1]] = match[2].replace(/^['"]|['"]$/gu, ''); current = null; continue; }
    match = /^\s*-\s+([A-Za-z0-9]+):\s*(.*)$/u.exec(line);
    if (match) { current = { [match[1]]: match[2].replace(/^['"]|['"]$/gu, '') }; files.push(current); continue; }
    match = /^\s+([A-Za-z0-9]+):\s*(.*)$/u.exec(line);
    if (match && current) current[match[1]] = match[2].replace(/^['"]|['"]$/gu, '');
  }
  if (files.length > 0) result.files = files;
  return result;
}

/**
 * The consistency of the update source in the three places that carry it, and the rules the updater code must keep. `publication` is true for a build that is going to
 * be published (a public release, signed or not): an unset owner is then a failure; otherwise it is a warning, because a development build is allowed to have no source.
 */
export function checkUpdateConfiguration({ sourceTs, builderPublish, updaterText, appUpdate = null, publication }) {
  const findings = [];
  const fail = (area, message) => findings.push({ level: 'FAIL', area, message });
  const warn = (area, message) => findings.push({ level: 'WARN', area, message });
  const note = (area, message) => findings.push({ level: 'INFO', area, message });
  if (!builderPublish) { fail('updates', 'electron-builder.yml has no publish block: the packaged application would have no explicit update source'); return findings; }
  if (builderPublish.provider !== 'github') fail('updates', `the update provider is "${builderPublish.provider}", it must be github`);
  for (const key of Object.keys(builderPublish)) if (!['provider', 'owner', 'repo', 'releaseType'].includes(key)) fail('updates', `electron-builder.yml publish.${key} is not allowed (a token, a host or a private flag would reach the package)`);
  if (/token/iu.test(JSON.stringify(builderPublish))) fail('updates', 'the publish block mentions a token: a client never needs one for public releases');
  for (const name of ['provider', 'owner', 'repo']) if (sourceTs[name] !== builderPublish[name]) fail('updates', `electron/updateSource.ts and electron-builder.yml disagree on ${name}: "${sourceTs[name]}" against "${builderPublish[name]}"`);
  if (!isConfiguredSource(sourceTs)) (publication ? fail : warn)('updates', `the update source is not set (owner "${sourceTs.owner}"): this build has no way to update${publication ? ' and cannot be published' : ''} (docs/AUTO_UPDATE.md, "One-time setup")`);
  else note('updates', `update source: github.com/${sourceTs.owner}/${sourceTs.repo}`);
  for (const [pattern, why] of [
    [/autoInstallOnAppQuit\s*=\s*false/u, 'autoInstallOnAppQuit is not switched off: an update could install silently when the application closes'],
    [/allowPrerelease\s*=\s*false/u, 'allowPrerelease is not switched off: a stable client could follow a prerelease'],
    [/if\s*\(this\.#options\.buildChannel\s*===\s*'internal'\)\s*updater\.allowPrerelease\s*=\s*true/u, 'the prerelease opt-in is not limited to internal builds'],
    [/autoDownload\s*=\s*false/u, 'autoDownload is not switched off: an update could download without being asked'],
    [/allowDowngrade\s*=\s*false/u, 'allowDowngrade is not switched off'],
  ]) if (!pattern.test(updaterText)) fail('updates', why);
  if (appUpdate) {
    for (const name of ['provider', 'owner', 'repo']) if (appUpdate[name] !== sourceTs[name]) fail('updates', `the packaged app-update.yml says ${name} "${appUpdate[name]}", the code says "${sourceTs[name]}"`);
    if (Object.keys(appUpdate).some((key) => /token|password|secret/iu.test(key))) fail('updates', 'the packaged app-update.yml contains a credential field');
    if (appUpdate.publisherName) note('updates', `the packaged application verifies the publisher of an update: ${appUpdate.publisherName}`);
    else note('updates', 'the packaged app-update.yml has no publisherName: an unsigned build verifies updates by their sha512 only, not by a signature (docs/AUTO_UPDATE.md, "Unsigned builds")');
  }
  return findings;
}

/**
 * latest.yml against the installer it was generated with. The numbers come from the installer file itself (sha512 as base64, the size), never from a second source.
 */
export function checkLatestYml({ latest, version, installerName, installerSha512, installerSize, tag, blockmapPresent }) {
  const findings = [];
  const fail = (message) => findings.push({ level: 'FAIL', area: 'updates', message });
  if (latest.version !== version) fail(`latest.yml says version ${latest.version}, the package is ${version}`);
  if (tag !== undefined && tag !== null && tag !== `v${version}`) fail(`the requested tag ${tag} is not v${version}`);
  const file = (latest.files ?? []).find((entry) => entry.url === installerName);
  if (!file) fail(`latest.yml lists no file named ${installerName} (it lists: ${(latest.files ?? []).map((entry) => entry.url).join(', ') || 'nothing'})`);
  else {
    if (file.sha512 !== installerSha512) fail('latest.yml: the sha512 of the installer does not match the installer file');
    if (Number(file.size) !== installerSize) fail(`latest.yml: size ${file.size} is not the installer size ${installerSize}`);
  }
  if (latest.path !== installerName) fail(`latest.yml path is "${latest.path}", it must be ${installerName}`);
  if (latest.sha512 !== installerSha512) fail('latest.yml: the top-level sha512 does not match the installer file');
  if (!blockmapPresent) fail(`the blockmap of the installer (${installerName}.blockmap) is missing: differential updates and the integrity of the download rely on it`);
  if (!latest.releaseDate || !Number.isFinite(Date.parse(latest.releaseDate))) fail('latest.yml has no valid releaseDate');
  if (/prerelease/iu.test(JSON.stringify(latest))) fail('latest.yml mentions a prerelease');
  return findings;
}

/** `ghp_`, `github_pat_`, OAuth and installation tokens, and "BEGIN ... PRIVATE KEY" blocks: nothing like it may be inside a package or a release asset. */
export const SECRET_PATTERNS = [
  /\bgh[pousr]_[A-Za-z0-9]{30,}\b/u,
  /\bgithub_pat_[A-Za-z0-9_]{40,}\b/u,
  /-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----/u,
  /\bAKIA[0-9A-Z]{16}\b/u,
];
export const findSecrets = (text) => SECRET_PATTERNS.filter((pattern) => pattern.test(text)).map((pattern) => pattern.source);

export const sha512Base64 = (file) => new Promise((resolve, reject) => {
  const hash = createHash('sha512');
  createReadStream(file).on('data', (chunk) => hash.update(chunk)).on('error', reject).on('end', () => resolve(hash.digest('base64')));
});

/** The version that was asked for (a tag such as v0.36.0 or a plain 0.36.0) as a plain version, or null when it is neither. */
export function versionOfTag(tag) {
  const match = /^v?(\d+\.\d+\.\d+)$/u.exec(String(tag ?? ''));
  return match ? match[1] : null;
}

/**
 * The packaged shell imports electron-updater at start-up, so the updater and everything it requires must be inside app.asar (staged under dist-electron/node_modules by
 * apps/desktop/scripts/package-win.mjs). `entries` are the asar paths (no leading slash, forward slashes), `readJson` returns the parsed package.json of an entry.
 * Returns FAIL findings; an empty array means every runtime package resolves the way Node resolves it from dist-electron/main.js.
 */
export function checkAsarRuntimeDependencies({ entries, readJson, roots = ['electron-updater'] }) {
  const findings = [];
  const present = new Set(entries);
  const base = 'dist-electron';
  const seen = new Set();
  const visit = (name, fromDirectory) => {
    let directory = fromDirectory;
    for (;;) {
      const candidate = `${directory}/node_modules/${name}`;
      if (present.has(`${candidate}/package.json`)) {
        if (seen.has(candidate)) return;
        seen.add(candidate);
        const manifest = readJson(`${candidate}/package.json`);
        const entry = manifest?.main ? `${candidate}/${manifest.main}`.replace(/\/\.\//gu, '/') : null;
        if (entry && !present.has(entry) && !present.has(`${entry}.js`)) findings.push({ level: 'FAIL', area: 'updates', message: `app.asar: ${candidate} lacks its entry file ${manifest.main}` });
        for (const dependency of Object.keys(manifest?.dependencies ?? {})) visit(dependency, candidate);
        return;
      }
      if (directory === base) break;
      const slash = directory.lastIndexOf('/node_modules/');
      directory = slash === -1 ? base : directory.slice(0, slash);
    }
    findings.push({ level: 'FAIL', area: 'updates', message: `app.asar does not contain ${name} (needed from ${fromDirectory}): the packaged application would fail to start` });
  };
  for (const name of roots) visit(name, base);
  return findings;
}
