import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/**
 * Loading boundary for the vendored upstream JavaScript generation core
 * (`kellernz1/ds-randomizer`, pinned in
 * `engines/combined/vendor/UPSTREAM_PIN.md`).
 *
 * The vendored files are byte-identical to their pinned upstream commit and
 * must stay that way, so the type information for their exports lives here
 * rather than in `.d.ts` files placed next to them. Loading uses a computed
 * dynamic `import()` of a `file:` URL: the vendor tree sits outside the
 * backend's `rootDir`, so a static TypeScript import of it cannot be
 * compiled, and on Windows a bare absolute path handed to `import()` is
 * rejected with `ERR_UNSUPPORTED_ESM_URL_SCHEME`.
 *
 * This module only loads and shape-checks the core. It does not call
 * `generate()` or `writeOutput()`; see
 * docs/COMBINED_ENGINE_INTEGRATION.md ("JS core vendor proof").
 */

const currentDirectory = path.dirname(fileURLToPath(import.meta.url));

/**
 * Repository root, resolved identically from `src/adapters` (tsx) and
 * `dist/adapters` (compiled) — the same four-level pattern
 * `core/pipeline.ts` already relies on.
 */
const repositoryRoot = process.env.DS1TR_RUNTIME_ROOT ?? path.resolve(currentDirectory, '../../../../');

/** Vendored upstream `src/` root inside this repository. */
export const combinedRandomizerCoreRoot = path.join(
  repositoryRoot,
  'engines',
  'combined',
  'vendor',
  'src',
);

/**
 * The three vendored modules that carry the whole callable surface, relative
 * to `combinedRandomizerCoreRoot`. `core/rng.js` and `data/prototype-data.js`
 * are vendored too because `core/generator.js` statically imports them, but
 * they are never imported directly by this repository.
 */
export const combinedRandomizerCoreModulePaths = {
  config: 'core/config.js',
  generator: 'core/generator.js',
  output: 'core/output.js',
} as const;

/**
 * Upstream config object. Upstream ignores unknown keys and fills missing
 * ones from `defaultConfig`, so this stays an open record rather than a
 * mirrored interface that would drift from the pinned source.
 */
export type UpstreamRandomizerConfig = Record<string, unknown>;

/** Catalog object produced by `DsrDataTool scan`, passed through unchanged. */
export type UpstreamGameCatalog = Record<string, unknown>;

/**
 * The subset of `generate()`'s result this repository reads. `dataStatus` is
 * load-bearing: upstream silently falls back to `"prototype"` with fabricated
 * placements when the catalog is missing or incomplete, and that must be
 * rejected rather than written out.
 */
export interface UpstreamGenerationResult extends Record<string, unknown> {
  schemaVersion: number;
  randomizerVersion: string;
  dataStatus: 'extracted' | 'prototype';
  seed: string;
  config: UpstreamRandomizerConfig;
  placementHash: string;
  /** Which planner produced the enemies (and, for sampling-v1, its summary). Not part of `placementHash`. */
  placementDiagnostics?: unknown;
}

/** Typed view of the vendored core's callable surface. */
export interface CombinedRandomizerCore {
  /** `core/config.js` — upstream generator version string. */
  readonly randomizerVersion: string;
  /** `core/config.js` — frozen default config. */
  readonly defaultConfig: Readonly<UpstreamRandomizerConfig>;
  /** `core/config.js` — fills defaults and drops unknown keys. */
  normalizeConfig(input?: UpstreamRandomizerConfig): UpstreamRandomizerConfig;
  /**
   * `core/config.js` — returns an array of human-readable validation errors
   * and **never throws**; an empty array means the config is acceptable.
   * Callers must check the returned array rather than relying on an
   * exception.
   */
  validateConfig(
    config: UpstreamRandomizerConfig,
    options?: { requireGame?: boolean },
  ): string[];
  /** `core/generator.js` — the replacement types of a catalog for the custom-pool editor. Optional: older cores lack it. */
  describeRegularEnemyPool?(config: UpstreamRandomizerConfig, gameCatalog: UpstreamGameCatalog): {
    classTableHash: string;
    regularSlots: number;
    entries: Array<{ model: string; size: number; difficulty: number; slots: number; maps: string[] }>;
  };
  /** `core/generator.js` — the pure generation entry point. No I/O. */
  generate(
    config: UpstreamRandomizerConfig,
    options?: { gameCatalog?: UpstreamGameCatalog | null },
  ): UpstreamGenerationResult;
  /** `core/output.js` — writes `<baseDirectory>/seed_<safeSeed>/…`, returns that directory. */
  writeOutput(result: UpstreamGenerationResult, baseDirectory: string): Promise<string>;
}

export type CombinedRandomizerCoreLoadResult =
  | { ok: true; core: CombinedRandomizerCore; coreRoot: string }
  | { ok: false; code: 'JS_CORE_MISSING'; message: string; specifier: string }
  | { ok: false; code: 'JS_CORE_INCOMPLETE'; message: string; missingExports: string[] };

export type ModuleImporter = (specifier: string) => Promise<Record<string, unknown>>;

const defaultImportModule: ModuleImporter = (specifier) =>
  import(specifier) as Promise<Record<string, unknown>>;

export interface CombinedRandomizerCoreLoadOptions {
  /** Overrides the vendored `src/` root; intended for tests and fixtures. */
  coreRoot?: string;
  /** Overrides the dynamic import, so load failures can be exercised in tests. */
  importModule?: ModuleImporter;
}

/** Absolute filesystem path of one vendored core module. */
export function resolveCombinedRandomizerCoreModulePath(
  modulePath: string,
  coreRoot: string = combinedRandomizerCoreRoot,
): string {
  return path.join(coreRoot, modulePath);
}

/**
 * `import()` specifier for one vendored core module. Always a `file:` URL,
 * because Windows absolute paths are not valid ESM specifiers.
 */
export function resolveCombinedRandomizerCoreSpecifier(
  modulePath: string,
  coreRoot: string = combinedRandomizerCoreRoot,
): string {
  return pathToFileURL(resolveCombinedRandomizerCoreModulePath(modulePath, coreRoot)).href;
}

function collectMissingExports(
  modules: { config: Record<string, unknown>; generator: Record<string, unknown>; output: Record<string, unknown> },
): string[] {
  const expected: Array<[string, unknown]> = [
    ['core/config.js#RANDOMIZER_VERSION', modules.config.RANDOMIZER_VERSION],
    ['core/config.js#defaultConfig', modules.config.defaultConfig],
    ['core/config.js#normalizeConfig', modules.config.normalizeConfig],
    ['core/config.js#validateConfig', modules.config.validateConfig],
    ['core/generator.js#generate', modules.generator.generate],
    ['core/output.js#writeOutput', modules.output.writeOutput],
  ];
  return expected.filter(([, value]) => value === undefined).map(([name]) => name);
}

/**
 * Loads the vendored upstream generation core and verifies its documented
 * export surface. Returns a typed failure instead of throwing when the
 * vendor tree is absent or incomplete, so a missing vendor directory is a
 * reportable condition rather than a crash.
 */
export async function loadCombinedRandomizerCore(
  options: CombinedRandomizerCoreLoadOptions = {},
): Promise<CombinedRandomizerCoreLoadResult> {
  const coreRoot = options.coreRoot ?? combinedRandomizerCoreRoot;
  const importModule = options.importModule ?? defaultImportModule;

  const loaded: Partial<Record<keyof typeof combinedRandomizerCoreModulePaths, Record<string, unknown>>> = {};
  for (const [name, modulePath] of Object.entries(combinedRandomizerCoreModulePaths)) {
    const specifier = resolveCombinedRandomizerCoreSpecifier(modulePath, coreRoot);
    try {
      loaded[name as keyof typeof combinedRandomizerCoreModulePaths] = await importModule(specifier);
    } catch (error) {
      return {
        ok: false,
        code: 'JS_CORE_MISSING',
        message:
          `Could not load the vendored combined-engine core module '${modulePath}': `
          + `${error instanceof Error ? error.message : String(error)}`,
        specifier,
      };
    }
  }

  const modules = {
    config: loaded.config ?? {},
    generator: loaded.generator ?? {},
    output: loaded.output ?? {},
  };
  const missingExports = collectMissingExports(modules);
  if (missingExports.length > 0) {
    return {
      ok: false,
      code: 'JS_CORE_INCOMPLETE',
      message:
        'The vendored combined-engine core does not expose its documented entry points: '
        + `${missingExports.join(', ')}. The vendor tree may not match the pinned upstream commit.`,
      missingExports,
    };
  }

  return {
    ok: true,
    coreRoot,
    core: {
      randomizerVersion: modules.config.RANDOMIZER_VERSION as string,
      defaultConfig: modules.config.defaultConfig as Readonly<UpstreamRandomizerConfig>,
      normalizeConfig: modules.config.normalizeConfig as CombinedRandomizerCore['normalizeConfig'],
      validateConfig: modules.config.validateConfig as CombinedRandomizerCore['validateConfig'],
      generate: modules.generator.generate as CombinedRandomizerCore['generate'],
      describeRegularEnemyPool: modules.generator.describeRegularEnemyPool as CombinedRandomizerCore['describeRegularEnemyPool'],
      writeOutput: modules.output.writeOutput as CombinedRandomizerCore['writeOutput'],
    },
  };
}
