import path from 'node:path';
import type { FogLanguage } from '@ds1tr/contracts';

/**
 * Combined -> Fog compatibility bridge (Task 11). Pure and deterministic:
 * it derives the FogHost `compatibility` object from the combined package's
 * `patch-manifest.json` and placements, plans the per-attempt staging
 * directory, and decides retries. Host-side merge rules are specified in
 * `docs/FOGHOST_INTEGRATION.md` ("Compatibility bridge").
 */

/** A combined-patched file FogMod also reads or writes (MSB, EMEVD, GameParam). */
export interface FogCompatibilityFile {
  /** Game-relative path, forward slashes, e.g. `event/m10_00_00_00.emevd.dcx`. */
  path: string;
  /** SHA-256 of the pristine file the combined engine patched. */
  baseSha256: string;
  /** SHA-256 of the combined output staged for FogHost. */
  combinedSha256: string;
}

/** A combined NpcParam row FogMod must keep, and the vanilla row it stands in for. */
export interface FogPreservedNpcParam {
  id: number;
  /** Vanilla NpcParam row of the destination enemy slot. */
  baseId: number;
}

export interface FogCompatibility {
  /** Directory holding the pristine merge base of every `combinedFiles` entry. */
  baseDirectory: string;
  combinedFiles: FogCompatibilityFile[];
  preservedNpcParams: FogPreservedNpcParam[];
}

export type FogBridgeResult<T> =
  | { ok: true; value: T }
  | { ok: false; code: 'FOG_BRIDGE_INPUT_INVALID'; problems: string[] };

type JsonObject = Record<string, unknown>;

const sha256Pattern = /^[a-f0-9]{64}$/;
const sharedFilePattern = /^(map\/MapStudio\/[^/]+\.msb|event\/[^/]+\.emevd\.dcx|param\/GameParam\/GameParam\.parambnd\.dcx)$/;

function isObject(value: unknown): value is JsonObject {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function ordinal(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

function isSafeRelative(value: string): boolean {
  return value.length > 0 && !value.startsWith('/') && !value.includes('\\')
    && !/^[a-zA-Z]:/.test(value) && !value.split('/').some((part) => part === '' || part === '.' || part === '..');
}

function isNpcParamId(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 0x7fffffff;
}

export function validateFogCompatibility(value: unknown): string[] {
  const problems: string[] = [];
  if (!isObject(value)) return ['compatibility must be an object.'];
  const keys = Object.keys(value).sort();
  if (keys.join(',') !== 'baseDirectory,combinedFiles,preservedNpcParams') {
    problems.push('compatibility must contain exactly baseDirectory, combinedFiles and preservedNpcParams.');
    return problems;
  }
  if (typeof value.baseDirectory !== 'string' || value.baseDirectory.length === 0) {
    problems.push('compatibility.baseDirectory must be a non-empty string.');
  }
  if (!Array.isArray(value.combinedFiles)) {
    problems.push('compatibility.combinedFiles must be an array.');
  } else {
    let previous = '';
    for (const file of value.combinedFiles as unknown[]) {
      if (!isObject(file) || Object.keys(file).length !== 3
        || typeof file.path !== 'string' || !sharedFilePattern.test(file.path) || !isSafeRelative(file.path)
        || typeof file.baseSha256 !== 'string' || !sha256Pattern.test(file.baseSha256)
        || typeof file.combinedSha256 !== 'string' || !sha256Pattern.test(file.combinedSha256)) {
        problems.push('compatibility.combinedFiles contains an invalid entry.');
        break;
      }
      if (ordinal(previous, file.path) >= 0) {
        problems.push('compatibility.combinedFiles must be sorted ordinally by unique path.');
        break;
      }
      previous = file.path;
    }
  }
  if (!Array.isArray(value.preservedNpcParams)) {
    problems.push('compatibility.preservedNpcParams must be an array.');
  } else {
    let previous = -1;
    for (const row of value.preservedNpcParams as unknown[]) {
      if (!isObject(row) || Object.keys(row).length !== 2
        || !isNpcParamId(row.id) || !isNpcParamId(row.baseId)) {
        problems.push('compatibility.preservedNpcParams contains an invalid entry.');
        break;
      }
      if (row.id <= previous) {
        problems.push('compatibility.preservedNpcParams must be sorted by unique id.');
        break;
      }
      previous = row.id;
    }
  }
  return problems;
}

function readPatchedFiles(manifest: JsonObject, problems: string[]): FogCompatibilityFile[] {
  const entries: unknown[] = [];
  if (Array.isArray(manifest.patchedMaps)) entries.push(...manifest.patchedMaps);
  else problems.push('patch manifest patchedMaps must be an array.');
  if (Array.isArray(manifest.events)) entries.push(...manifest.events);
  else problems.push('patch manifest events must be an array.');
  if (manifest.gameParam !== null && manifest.gameParam !== undefined) entries.push(manifest.gameParam);

  const files: FogCompatibilityFile[] = [];
  for (const entry of entries) {
    if (!isObject(entry) || typeof entry.source !== 'string' || typeof entry.output !== 'string'
      || typeof entry.sourceSha256 !== 'string' || typeof entry.outputSha256 !== 'string') {
      problems.push('patch manifest contains a malformed patched file entry.');
      continue;
    }
    const baseSha256 = entry.sourceSha256.toLowerCase();
    const combinedSha256 = entry.outputSha256.toLowerCase();
    if (!isSafeRelative(entry.source) || !sharedFilePattern.test(entry.source)) {
      problems.push(`patch manifest source is not a shared MSB/EMEVD/GameParam path: ${entry.source}`);
    } else if (entry.output !== `mod/${entry.source}`) {
      problems.push(`patch manifest output does not mirror its source: ${entry.output}`);
    } else if (!sha256Pattern.test(baseSha256) || !sha256Pattern.test(combinedSha256)) {
      problems.push(`patch manifest hashes are invalid for ${entry.source}`);
    } else {
      files.push({ path: entry.source, baseSha256, combinedSha256 });
    }
  }
  files.sort((left, right) => ordinal(left.path, right.path));
  for (let index = 1; index < files.length; index++) {
    if (files[index].path === files[index - 1].path) {
      problems.push(`patch manifest lists ${files[index].path} more than once.`);
    }
  }
  return files;
}

/**
 * `randomizer` is the combined engine's `randomizer.json`: its placement lists live under
 * `placements` (`placements.enemies`, `placements.bosses`). A document without `placements`
 * (Fog-only runs) has no preserved rows. Lists at the top level are rejected so a layout
 * mismatch can never silently yield an empty list.
 */
function readPreservedNpcParams(randomizer: JsonObject, problems: string[]): FogPreservedNpcParam[] {
  const byId = new Map<number, number>();
  if (randomizer.enemies !== undefined || randomizer.bosses !== undefined) {
    problems.push('placement lists must be nested under randomizer.json "placements".');
    return [];
  }
  if (randomizer.placements === undefined) return [];
  if (!isObject(randomizer.placements)) {
    problems.push('randomizer.json "placements" must be an object.');
    return [];
  }
  const placements = randomizer.placements as JsonObject;
  for (const listName of ['enemies', 'bosses']) {
    const list = placements[listName];
    if (list === undefined) continue;
    if (!Array.isArray(list)) {
      problems.push(`placements ${listName} must be an array.`);
      continue;
    }
    for (const placement of list as unknown[]) {
      if (!isObject(placement)) {
        problems.push(`placements ${listName} contains a non-object entry.`);
        continue;
      }
      const scaled = placement.scaledNpcParamId;
      if (scaled === null || scaled === undefined) continue;
      if (!isNpcParamId(scaled) || !isNpcParamId(placement.sourceNpcParamId)) {
        problems.push(`placements ${listName} slot ${String(placement.slot)} has invalid NpcParam IDs.`);
        continue;
      }
      const known = byId.get(scaled);
      if (known !== undefined && known !== placement.sourceNpcParamId) {
        problems.push(`scaled NpcParam ${scaled} maps to more than one base row.`);
        continue;
      }
      byId.set(scaled, placement.sourceNpcParamId);
    }
  }
  return [...byId.entries()]
    .sort(([left], [right]) => left - right)
    .map(([id, baseId]) => ({ id, baseId }));
}

/**
 * Builds the FogHost `compatibility` object. `patchManifest` and
 * `placements` are the parsed JSON files of one combined package. Volatile
 * manifest fields (`generatedAt`, `outputDirectory`) are never copied.
 */
export function buildFogCompatibility(
  patchManifest: unknown,
  placements: unknown,
  baseDirectory: string,
): FogBridgeResult<FogCompatibility> {
  const problems: string[] = [];
  if (!isObject(patchManifest) || patchManifest.schemaVersion !== 2) {
    problems.push('patch manifest must be a schema-2 object.');
  }
  if (!isObject(placements)) problems.push('placements must be an object.');
  if (typeof baseDirectory !== 'string' || baseDirectory.length === 0) {
    problems.push('baseDirectory must be a non-empty string.');
  }
  if (problems.length > 0) return { ok: false, code: 'FOG_BRIDGE_INPUT_INVALID', problems };

  const combinedFiles = readPatchedFiles(patchManifest as JsonObject, problems);
  const preservedNpcParams = readPreservedNpcParams(placements as JsonObject, problems);
  if (problems.length > 0) return { ok: false, code: 'FOG_BRIDGE_INPUT_INVALID', problems };
  return { ok: true, value: { baseDirectory, combinedFiles, preservedNpcParams } };
}

/** Game-relative files FogMod reads from `gameDir` (see FOGHOST_INTEGRATION.md). */
export function fogReadSet(
  distEventFiles: readonly string[],
  distMapFiles: readonly string[],
  language: FogLanguage,
): string[] {
  const paths = new Set<string>([
    'param/GameParam/GameParam.parambnd.dcx',
    `msg/${language}/menu.msgbnd.dcx`,
    ...distEventFiles.map((name) => `event/${name}`),
    ...distMapFiles.map((name) => `map/MapStudio/${name}`),
  ]);
  return [...paths].sort(ordinal);
}

export interface FogStagingCopy {
  from: string;
  to: string;
}

export interface FogStagingPlan {
  stagingDirectory: string;
  baseDirectory: string;
  logPath: string;
  requestPath: string;
  responsePath: string;
  /** Directories that must exist (empty) before FogHost runs. */
  directories: string[];
  copies: FogStagingCopy[];
}

export interface FogStagingInput {
  /** Verified pristine game data (never the live installation being modified). */
  pristineDirectory: string;
  /** Combined package directory (`seed_<token>`). */
  packageDirectory: string;
  /** Fresh, unused directory for this Fog attempt. */
  attemptDirectory: string;
  /** Result of `fogReadSet`. */
  readSet: readonly string[];
  combinedFiles: readonly FogCompatibilityFile[];
}

function isInside(parent: string, child: string): boolean {
  const relative = path.relative(path.resolve(parent), path.resolve(child));
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

function resolveRelative(root: string, relative: string): string {
  return path.join(root, ...relative.split('/'));
}

/**
 * Plans one Fog attempt's staging directory: FogMod's read set from pristine
 * data, overlaid with the combined output of every shared file, plus the
 * pristine merge base of each combined file. Performs no I/O.
 */
export function planFogStaging(input: FogStagingInput): FogBridgeResult<FogStagingPlan> {
  const problems: string[] = [];
  const roots = [input.pristineDirectory, input.packageDirectory, input.attemptDirectory];
  if (!roots.every((root) => typeof root === 'string' && root.length > 0)) {
    problems.push('pristineDirectory, packageDirectory and attemptDirectory are required.');
  } else {
    if (isInside(input.pristineDirectory, input.attemptDirectory)
      || isInside(input.attemptDirectory, input.pristineDirectory)) {
      problems.push('attemptDirectory must not overlap the pristine directory.');
    }
    if (isInside(input.packageDirectory, input.attemptDirectory)
      || isInside(input.attemptDirectory, input.packageDirectory)) {
      problems.push('attemptDirectory must not overlap the combined package.');
    }
  }
  for (const relative of [...input.readSet, ...input.combinedFiles.map((file) => file.path)]) {
    if (!isSafeRelative(relative)) problems.push(`unsafe staging path: ${relative}`);
  }
  if (problems.length > 0) return { ok: false, code: 'FOG_BRIDGE_INPUT_INVALID', problems };

  const stagingDirectory = path.join(input.attemptDirectory, 'game');
  const baseDirectory = path.join(input.attemptDirectory, 'base');
  const combined = new Set(input.combinedFiles.map((file) => file.path));
  const staged = [...new Set([...input.readSet, ...combined])].sort(ordinal);
  const copies: FogStagingCopy[] = staged.map((relative) => ({
    from: combined.has(relative)
      ? resolveRelative(input.packageDirectory, `mod/${relative}`)
      : resolveRelative(input.pristineDirectory, relative),
    to: resolveRelative(stagingDirectory, relative),
  }));
  for (const relative of [...combined].sort(ordinal)) {
    copies.push({
      from: resolveRelative(input.pristineDirectory, relative),
      to: resolveRelative(baseDirectory, relative),
    });
  }
  return {
    ok: true,
    value: {
      stagingDirectory,
      baseDirectory,
      logPath: path.join(input.attemptDirectory, 'foghost.log'),
      requestPath: path.join(input.attemptDirectory, 'request.json'),
      responsePath: path.join(input.attemptDirectory, 'response.json'),
      directories: [resolveRelative(stagingDirectory, 'script/talk')],
      copies,
    },
  };
}

export interface FogRetryState {
  combinedAttempt: number;
  fogAttempt: number;
}

export interface FogRetryBounds {
  maxCombinedAttempts: number;
  maxFogAttempts: number;
}

/** Suggested bounds; the pipeline (Task 12) owns the final values. */
export const defaultFogRetryBounds: FogRetryBounds = { maxCombinedAttempts: 3, maxFogAttempts: 5 };

export type FogRetryDecision =
  | { action: 'retry-fog'; next: FogRetryState; reason: string }
  | { action: 'retry-combined'; next: FogRetryState; reason: string }
  | { action: 'fail'; reason: string };

/**
 * Retry scope per FogHost error code. The backend table is the single
 * authority; the host's own `retryScope` field is informational. Only
 * failures a new seed can change are retryable: merge conflicts and
 * post-write violations are deterministic for a given base install.
 *
 * FOG_UPSTREAM_INCOMPATIBLE (a key item outside FogMod's annotated item lots) used to be retried with a new combined seed.
 * It is structural now: the item shuffle keeps the tracked items inside those lots (fogItemRules.ts), so a repeat means
 * the configuration or the game data cannot work, and a new seed would only burn the attempts.
 */
const retryScopeByCode: Readonly<Record<string, 'combined' | 'fog'>> = {
  FOG_SEED_UNSOLVABLE: 'fog',
};

export function decideFogRetry(
  error: { code: string; message: string },
  state: FogRetryState,
  bounds: FogRetryBounds = defaultFogRetryBounds,
): FogRetryDecision {
  const scope = retryScopeByCode[error.code];
  if (scope === 'fog') {
    if (state.fogAttempt + 1 < bounds.maxFogAttempts) {
      return {
        action: 'retry-fog',
        next: { combinedAttempt: state.combinedAttempt, fogAttempt: state.fogAttempt + 1 },
        reason: `${error.code}: ${error.message}`,
      };
    }
    return { action: 'fail', reason: `Fog retries exhausted (${bounds.maxFogAttempts}): ${error.code}: ${error.message}` };
  }
  if (scope === 'combined') {
    if (state.combinedAttempt + 1 < bounds.maxCombinedAttempts) {
      return {
        action: 'retry-combined',
        next: { combinedAttempt: state.combinedAttempt + 1, fogAttempt: 0 },
        reason: `${error.code}: ${error.message}`,
      };
    }
    return { action: 'fail', reason: `Combined retries exhausted (${bounds.maxCombinedAttempts}): ${error.code}: ${error.message}` };
  }
  return { action: 'fail', reason: `${error.code} is not retryable: ${error.message}` };
}
