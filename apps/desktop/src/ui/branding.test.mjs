import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

// The product is called "DS1 Multi Randomizer" (it was "DS1 Triple Randomizer" until 0.35.1). This test guards the ACTIVE branding surfaces - what a person sees in the
// application, the installer and the generated reports, and the files that configure them - against the old name coming back as the current one. Historical documents
// (status log, decisions, audits, old patches, source-file modification notices) are deliberately outside its scope, and so are the stable internal identifiers, which are
// asserted below to be unchanged: renaming any of them would break old Seed Codes, presets, history, settings or the upgrade of an installed copy.

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..');
const read = (relative) => readFileSync(path.join(repositoryRoot, relative), 'utf8').replace(/^﻿/u, '');
const walk = (relative, accept) => {
  const absolute = path.join(repositoryRoot, relative);
  return readdirSync(absolute).flatMap((name) => {
    const child = path.join(relative, name);
    const stat = statSync(path.join(repositoryRoot, child));
    if (stat.isDirectory()) return name === 'node_modules' || name === 'dist' || name === 'release' ? [] : walk(child, accept);
    return accept(child) ? [child.replace(/\\/gu, '/')] : [];
  });
};

const NAME = 'DS1 Multi Randomizer';
const OLD = /DS1[ -]Triple[ -]Randomizer|Triple Randomizer|Triple-Randomizer/u;

const surfaces = [
  'README.md', 'THIRD_PARTY_NOTICES.md', 'apps/desktop/index.html', 'apps/desktop/electron-builder.yml', 'apps/desktop/electron/main.ts', 'apps/desktop/scripts/package-win.mjs',
  'tools/release/release-policy.json', 'tools/release/release-verify.mjs', 'tools/fog-host/FogHost.csproj', 'engines/combined/vendor/tools/DsrDataTool/DsrDataTool.csproj',
  'docs/LICENSING.md', 'docs/RELEASE_TRUST.md', 'docs/SECURITY_AND_PRIVACY.md',
  ...walk('apps/desktop/src/locales', (file) => file.endsWith('.json')),
  ...walk('apps/desktop/src', (file) => /\.tsx?$/u.test(file) && !/\.test\./u.test(file)),
  ...walk('packages/contracts/src', (file) => file.endsWith('.ts') && !/\.test\./u.test(file)),
  ...walk('apps/backend/src', (file) => file.endsWith('.ts') && !/\.test\./u.test(file)),
];

test('no active branding surface presents the old product name as the current one', () => {
  assert.ok(surfaces.length > 40, `the scan covers the surfaces (${surfaces.length})`);
  const offenders = surfaces.filter((file) => OLD.test(read(file)));
  assert.deepEqual(offenders, [], 'these files still say "DS1 Triple Randomizer": the product is "DS1 Multi Randomizer" (historical documents are not scanned)');
});

test('the application, the installer and the reports carry the new name', () => {
  assert.match(read('apps/desktop/electron-builder.yml'), /^productName: DS1 Multi Randomizer$/mu);
  assert.match(read('apps/desktop/electron-builder.yml'), /shortcutName: DS1 Multi Randomizer/u);
  assert.match(read('apps/desktop/electron-builder.yml'), /uninstallDisplayName: DS1 Multi Randomizer/u);
  assert.match(read('apps/desktop/electron-builder.yml'), /artifactName: DS1-Multi-Randomizer-\$\{version\}-Setup-x64\.\$\{ext\}/u);
  assert.match(read('apps/desktop/electron-builder.yml'), /artifactName: DS1-Multi-Randomizer-\$\{version\}-Portable-x64\.\$\{ext\}/u);
  assert.match(read('apps/desktop/index.html'), /<title>DS1 Multi Randomizer<\/title>/u);
  assert.match(read('apps/desktop/electron/main.ts'), /title: 'DS1 Multi Randomizer'/u);
  const policy = JSON.parse(read('tools/release/release-policy.json'));
  assert.equal(policy.product, NAME);
  assert.ok(policy.ds1trExecutables.some((file) => file.path === `${NAME}.exe`), 'the application executable follows productName');
  assert.ok(policy.installedExtras.some((file) => file.path === `Uninstall ${NAME}.exe`), 'the uninstaller follows productName');
  assert.deepEqual(policy.ds1trArtifacts.map((entry) => entry.pattern), ['DS1-Multi-Randomizer-*-Setup-x64.exe', 'DS1-Multi-Randomizer-*-Portable-x64.exe']);
  for (const language of ['en', 'pl']) {
    const app = JSON.parse(read(`apps/desktop/src/locales/${language}/app.json`));
    assert.equal(app.header.title, 'Multi Randomizer', `${language}: the sidebar heading (the "DS1" mark is drawn above it)`);
    for (const file of ['diagnostics', 'support', 'hints']) assert.ok(read(`apps/desktop/src/locales/${language}/${file}.json`).includes(NAME), `${language}/${file} names the product`);
  }
  // The product name is not translated: the Polish interface says the same name.
  assert.equal(read('apps/desktop/src/locales/pl/support.json').includes('Wielolosowator'), false);
});

test('the stable internal identifiers are unchanged by the rename', () => {
  // Seed Codes, the generation profile and the seed derivation: old codes keep generating the same world.
  assert.match(read('packages/contracts/src/generation.ts'), /SEED_CODE_PREFIX = 'DS1TR'/u);
  assert.match(read('packages/contracts/src/generation.ts'), /GENERATION_PROFILE_V1 = 'ds1tr-gen-v1'/u);
  assert.match(read('apps/backend/src/core/seeds.ts'), /ds1-triple-randomizer:v1:\$\{masterSeed\}:\$\{stream\}/u);
  // The installed copy keeps its identity: the application id (upgrade and uninstall) and the name of the packaged package, which is what Electron derives the user data
  // folder from (settings, history, presets, state). package-win.mjs must not add a productName to that package.json.
  assert.match(read('apps/desktop/electron-builder.yml'), /^appId: com\.ds1tr\.desktop$/mu);
  const packaging = read('apps/desktop/scripts/package-win.mjs');
  assert.match(packaging, /name: 'ds1-triple-randomizer-desktop'/u);
  const packagedManifest = /JSON\.stringify\(\{([\s\S]*?)\}, null, 2\)\);/u.exec(packaging.slice(packaging.indexOf("path.join(packageApp, 'package.json')")))?.[1] ?? '';
  assert.ok(packagedManifest.includes("name: 'ds1-triple-randomizer-desktop'"), 'the packaged manifest is the one inspected');
  assert.equal(/productName/u.test(packagedManifest), false, 'no productName in the packaged package.json: it would move the user data folder');
  assert.match(read('apps/backend/src/server.ts'), /service: 'ds1-triple-randomizer-backend'/u);
});
