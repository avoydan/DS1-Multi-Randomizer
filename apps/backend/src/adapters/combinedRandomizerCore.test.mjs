import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import {
  combinedRandomizerCoreModulePaths,
  combinedRandomizerCoreRoot,
  loadCombinedRandomizerCore,
  resolveCombinedRandomizerCoreModulePath,
  resolveCombinedRandomizerCoreSpecifier,
} from '../../dist/adapters/combinedRandomizerCore.js';

/** Upstream `RANDOMIZER_VERSION` at pinned commit f120efe0e95b957b7e76109868c3c9966e22720c. */
const pinnedUpstreamVersion = '0.5.66';

/**
 * The complete set of module specifiers that may appear anywhere in the
 * vendored core. Everything is either a Node builtin or another vendored
 * file, which is what makes the vendored set self-contained.
 */
const allowedSpecifiers = new Set([
  'node:crypto',
  'node:fs/promises',
  'node:path',
  './rng.js',
  './config.js',
  './placement.js',
  '../data/prototype-data.js',
]);

/** Files the vendored core is expected to consist of, relative to its `src/` root. */
const expectedVendoredFiles = [
  'core/config.js',
  'core/generator.js',
  'core/output.js',
  'core/placement.js',
  'core/rng.js',
  'data/prototype-data.js',
];

async function listVendoredFiles(directory, prefix = '') {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const relative = prefix === '' ? entry.name : `${prefix}/${entry.name}`;
    if (entry.isDirectory()) {
      files.push(...(await listVendoredFiles(path.join(directory, entry.name), relative)));
    } else {
      files.push(relative);
    }
  }
  return files.sort();
}

function moduleSpecifiersOf(source) {
  const specifiers = new Set();
  const patterns = [
    /(?:^|\n)\s*(?:import|export)[^;]*?from\s*["']([^"']+)["']/g,
    /(?:^|\n)\s*import\s*["']([^"']+)["']/g,
    /import\(\s*["']([^"']+)["']/g,
    /require\(\s*["']([^"']+)["']/g,
  ];
  for (const pattern of patterns) {
    for (const match of source.matchAll(pattern)) {
      specifiers.add(match[1]);
    }
  }
  return specifiers;
}

test('the vendored core consists of exactly the minimal required module set', async () => {
  assert.deepEqual(await listVendoredFiles(combinedRandomizerCoreRoot), expectedVendoredFiles);
});

test('no vendored core module imports anything outside the vendored set or node builtins', async () => {
  const seen = new Set();
  for (const relative of expectedVendoredFiles) {
    const source = await readFile(
      resolveCombinedRandomizerCoreModulePath(relative, combinedRandomizerCoreRoot),
      'utf8',
    );
    for (const specifier of moduleSpecifiersOf(source)) {
      assert.ok(
        allowedSpecifiers.has(specifier),
        `${relative} references unexpected module specifier '${specifier}'`,
      );
      seen.add(specifier);
    }
  }
  assert.deepEqual([...seen].sort(), [...allowedSpecifiers].sort());
});

test('the vendor directory declares an ESM scope, so the core loads as modules', async () => {
  const manifest = JSON.parse(
    await readFile(path.join(path.dirname(combinedRandomizerCoreRoot), 'package.json'), 'utf8'),
  );
  assert.equal(manifest.type, 'module');
});

test('core modules are addressed as file: URLs, not bare absolute paths', () => {
  const specifier = resolveCombinedRandomizerCoreSpecifier(combinedRandomizerCoreModulePaths.generator);
  assert.ok(specifier.startsWith('file:'), `expected a file: URL, got ${specifier}`);
  assert.ok(specifier.endsWith('/core/generator.js'), `unexpected specifier ${specifier}`);
});

test('loads the vendored core and exposes its documented entry points', async () => {
  const result = await loadCombinedRandomizerCore();

  assert.equal(result.ok, true, `load failed: ${result.ok === false ? result.message : ''}`);
  assert.equal(result.coreRoot, combinedRandomizerCoreRoot);
  assert.equal(result.core.randomizerVersion, pinnedUpstreamVersion);
  assert.equal(typeof result.core.defaultConfig, 'object');
  assert.equal(typeof result.core.normalizeConfig, 'function');
  assert.equal(typeof result.core.validateConfig, 'function');
  assert.equal(typeof result.core.generate, 'function');
  assert.equal(typeof result.core.writeOutput, 'function');
});

test('normalizeConfig/validateConfig are callable without a game install or filesystem access', async () => {
  const result = await loadCombinedRandomizerCore();
  assert.equal(result.ok, true);

  const config = result.core.normalizeConfig({ seed: 'abc-123' });
  assert.equal(config.seed, 'abc-123');
  assert.equal(config.gameDirectory, '');
  assert.equal(config.version, pinnedUpstreamVersion);

  // validateConfig returns an error array; it does not throw.
  assert.deepEqual(result.core.validateConfig(config), []);
  assert.deepEqual(result.core.validateConfig({ ...config, seed: 'not a valid seed' }), [
    'The seed must contain 1-64 characters (letters, numbers, dot, hyphen, or underscore).',
  ]);
});

test('reports a typed failure when the vendored core is absent', async () => {
  const result = await loadCombinedRandomizerCore({
    coreRoot: path.join(combinedRandomizerCoreRoot, 'does-not-exist'),
  });

  assert.equal(result.ok, false);
  assert.equal(result.code, 'JS_CORE_MISSING');
  assert.ok(result.specifier.startsWith('file:'));
  assert.match(result.message, /core\/config\.js/);
});

test('reports a typed failure when the vendored core is missing documented exports', async () => {
  const result = await loadCombinedRandomizerCore({
    importModule: async () => ({}),
  });

  assert.equal(result.ok, false);
  assert.equal(result.code, 'JS_CORE_INCOMPLETE');
  assert.deepEqual(result.missingExports, [
    'core/config.js#RANDOMIZER_VERSION',
    'core/config.js#defaultConfig',
    'core/config.js#normalizeConfig',
    'core/config.js#validateConfig',
    'core/generator.js#generate',
    'core/output.js#writeOutput',
  ]);
});
