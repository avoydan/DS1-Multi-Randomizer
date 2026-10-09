// Stages the exact set of files of a GitHub release (docs/AUTO_UPDATE.md) after verifying that they belong together.
//
//   node tools/release/release-assets.mjs --tag v0.36.0 [--stage <folder>]
//
// It refuses (exit 1) unless: the tag is the packaged version; the installer, the portable build, latest.yml, the installer's blockmap and the release manifest all exist;
// latest.yml was generated from THIS installer (version, file name, size and sha512 match the file); the manifest describes these very files by sha256 and records a
// public-channel publication build with a configured update source. It then copies the six release assets into the stage folder and writes SHA256SUMS.txt.
// Nothing is signed, uploaded or published here; the workflow uploads the staged folder as a DRAFT release.

import { copyFile, mkdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { exists, parseArguments, readJson, repositoryRoot, sha256File } from './release-lib.mjs';
import { checkLatestYml, parseFlatYaml, sha512Base64, versionOfTag } from './update-lib.mjs';

const { values } = parseArguments(process.argv.slice(2));
const tag = values.get('tag');
const version = (await readJson(path.join(repositoryRoot, 'apps', 'desktop', 'package.json'))).version;
const releaseDirectory = path.join(repositoryRoot, 'apps', 'desktop', 'release');
const stage = path.resolve(values.get('stage') ?? path.join(releaseDirectory, 'assets'));

const problems = [];
const fail = (message) => problems.push(message);
if (!tag) fail('--tag vX.Y.Z is required: a release is made for a version that was asked for');
else if (versionOfTag(tag) !== version) fail(`the tag ${tag} is not v${version}, the version of the packaged application`);

const installerName = `DS1-Multi-Randomizer-${version}-Setup-x64.exe`;
const portableName = `DS1-Multi-Randomizer-${version}-Portable-x64.exe`;
const sources = {
  installer: path.join(releaseDirectory, installerName),
  blockmap: path.join(releaseDirectory, `${installerName}.blockmap`),
  portable: path.join(releaseDirectory, portableName),
  latest: path.join(releaseDirectory, 'latest.yml'),
  manifest: path.join(releaseDirectory, 'release-manifest.json'),
};
for (const [kind, file] of Object.entries(sources)) if (!(await exists(file))) fail(`missing ${kind}: ${path.relative(repositoryRoot, file)}`);

if (problems.length === 0) {
  const latest = parseFlatYaml(await readFile(sources.latest, 'utf8'));
  for (const finding of checkLatestYml({
    latest, version, installerName, installerSha512: await sha512Base64(sources.installer), installerSize: (await stat(sources.installer)).size, tag, blockmapPresent: true,
  })) fail(finding.message);

  const manifest = await readJson(sources.manifest);
  if (manifest.version !== version) fail(`the release manifest is for version ${manifest.version}, not ${version}`);
  if (manifest.channel !== 'public') fail(`the release manifest says channel "${manifest.channel}": only a public-channel build is published`);
  if (!manifest.updates?.publication) fail('the release manifest was not made with --publication: run release-verify.mjs --publication after packaging');
  if (!manifest.updates?.sourceConfigured) fail('the release manifest says the update source is not set');
  if (manifest.provenance?.workingTreeClean === false) fail('the release manifest says the working tree was not clean when the release was verified');
  for (const [kind, file] of [['installer', sources.installer], ['portable', sources.portable]]) {
    const entry = (manifest.artifacts ?? []).find((artifact) => artifact.kind === kind);
    if (!entry) fail(`the release manifest does not list the ${kind}`);
    else if (entry.file !== path.basename(file) || entry.sha256 !== await sha256File(file)) fail(`the release manifest describes another ${kind} than the file in the release folder`);
  }
}

if (problems.length > 0) {
  console.error(`RELEASE ASSETS REFUSED (${problems.length}):`);
  for (const problem of problems) console.error(`  - ${problem}`);
  process.exit(1);
}

await rm(stage, { recursive: true, force: true });
await mkdir(stage, { recursive: true });
const manifestName = `release-manifest-${version}.json`;
const staged = [
  [sources.installer, installerName], [sources.blockmap, `${installerName}.blockmap`], [sources.portable, portableName],
  [sources.latest, 'latest.yml'], [sources.manifest, manifestName],
];
for (const [source, name] of staged) await copyFile(source, path.join(stage, name));
const sums = [];
for (const [, name] of staged) sums.push(`${await sha256File(path.join(stage, name))} *${name}`);
await writeFile(path.join(stage, 'SHA256SUMS.txt'), `${sums.join('\n')}\n`);
console.log(JSON.stringify({ ok: true, tag, version, stage, files: [...staged.map(([, name]) => name), 'SHA256SUMS.txt'] }, null, 2));
