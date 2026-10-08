import { execFile } from 'node:child_process';
import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { worldDataManifest, worldDataSourceCommit } from './worldData.js';
import type { FogPrerequisites } from './fogPrerequisites.js';

/**
 * The progression model of the item randomizer (docs/ITEM_PROGRESSION_MODEL.md): FogMod's own world graph with every randomization option off,
 * exported by `FogHost progression-model` from the verified world data, cached next to it and handed to the combined engine, which only EVALUATES it.
 * There is one model; this file never interprets it.
 */
export const PROGRESSION_MODEL_FORMAT = 'ds1tr-progression-model-1';

/** The model variants: the vanilla world graph, and (Race Mode +) the vanilla graph plus the skips of the legacy list that FogMod models (docs/RACE_MODE.md). */
export type ProgressionModelVariant = 'vanilla' | 'race-plus';

export interface ProgressionModel {
  format: typeof PROGRESSION_MODEL_FORMAT;
  /** Absent in a model exported before the variants existed: that is the vanilla graph. */
  variant?: ProgressionModelVariant;
  fogModCommit: string;
  annotationSha256: string;
  start: string;
  areas: string[];
  edges: Array<{ from: string; to: string; when: unknown }>;
  keys: Array<{ name: string; id: string; area: string; tags: string[] }>;
  lotAreas: Record<string, string>;
  optionalAreas: string[];
  prison: { start: string; mustReach: string; areas: string[] };
}

export type ProgressionModelResult =
  | { ok: true; model: ProgressionModel; fromCache: boolean }
  | { ok: false; code: 'KEY_PLACEMENT_MODEL_UNAVAILABLE'; message: string; technical: string };

const expectedAnnotationSha256 = worldDataManifest.find((entry) => entry.path === 'dist/fog.txt')?.sha256;

export const KEY_PLACEMENT_MODEL_MESSAGE = 'Key placement needs the world data of the fog randomizer, which could not be prepared. Click Generate world again, or turn Key placement off.';

function isModel(value: unknown, variant: ProgressionModelVariant): value is ProgressionModel {
  if (!value || typeof value !== 'object') return false;
  const model = value as Partial<ProgressionModel>;
  return model.format === PROGRESSION_MODEL_FORMAT && (model.variant ?? 'vanilla') === variant && model.fogModCommit === worldDataSourceCommit && model.annotationSha256 === expectedAnnotationSha256
    && Array.isArray(model.areas) && Array.isArray(model.edges) && Array.isArray(model.keys) && typeof model.lotAreas === 'object' && model.lotAreas !== null
    && typeof model.start === 'string' && typeof model.prison === 'object';
}

export type HostRunner = (executable: string, args: string[]) => Promise<{ code: number; stderr: string }>;

const runHost: HostRunner = (executable, args) => new Promise((resolve) => {
  execFile(executable, args, { windowsHide: true, timeout: 120_000, maxBuffer: 4 * 1024 * 1024 }, (error, _stdout, stderr) => {
    resolve({ code: error ? (typeof (error as { code?: unknown }).code === 'number' ? (error as { code: number }).code : 1) : 0, stderr: String(stderr) });
  });
});

/** The model for a prepared world data root: from the cache when it matches the pinned annotation, otherwise exported once (no shell, argument array). */
export async function loadProgressionModel(prerequisites: Pick<FogPrerequisites, 'executablePath' | 'dataRoot'>, run: HostRunner = runHost, variant: ProgressionModelVariant = 'vanilla'): Promise<ProgressionModelResult> {
  // One cache file per variant: a vanilla model is never served for Race Mode + (or the reverse). The vanilla name is the one of every earlier version.
  const suffix = variant === 'vanilla' ? '' : `.${variant}`;
  const cacheFile = path.join(path.dirname(prerequisites.dataRoot), `${path.basename(prerequisites.dataRoot)}${suffix}.progression-model.json`);
  try {
    const cached = JSON.parse(await readFile(cacheFile, 'utf8')) as unknown;
    if (isModel(cached, variant)) return { ok: true, model: cached, fromCache: true };
  } catch { /* no usable cache: export it */ }
  const temporary = `${cacheFile}.${process.pid}.tmp`;
  try {
    await mkdir(path.dirname(cacheFile), { recursive: true });
    const outcome = await run(prerequisites.executablePath, ['progression-model', '--data-root', prerequisites.dataRoot, ...(variant === 'vanilla' ? [] : ['--variant', variant]), '--out', temporary]);
    if (outcome.code !== 0) return failure(`FogHost progression-model exited with ${outcome.code}: ${outcome.stderr.trim().slice(0, 400)}`);
    const exported = JSON.parse(await readFile(temporary, 'utf8')) as unknown;
    if (!isModel(exported, variant)) return failure('The exported progression model does not match the pinned world data.');
    await writeFile(temporary, `${JSON.stringify(exported)}\n`);
    await rename(temporary, cacheFile);
    return { ok: true, model: exported, fromCache: false };
  } catch (error) {
    return failure(error instanceof Error ? error.message : String(error));
  } finally {
    await rm(temporary, { force: true }).catch(() => undefined);
  }
}

function failure(technical: string): ProgressionModelResult {
  return { ok: false, code: 'KEY_PLACEMENT_MODEL_UNAVAILABLE', message: KEY_PLACEMENT_MODEL_MESSAGE, technical };
}
