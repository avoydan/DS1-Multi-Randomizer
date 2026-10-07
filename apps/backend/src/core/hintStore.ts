import { access, mkdir, readdir, readFile, rename, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { GenerationSettings, HintDocument, RunHistoryEntry } from '@ds1tr/contracts';
import { HINT_DATA_SCHEMA_VERSION } from '@ds1tr/contracts';
import { hintRecords, type PlacementLike } from './hintData.js';

/**
 * Persistence of the hint data (docs/HINT_SHEET.md). Generation produces the placement document once; the hint data is built from it right after
 * a successful run and written NEXT TO the run (never into the package, which is hashed and applied to the game). The viewer, the exports, the
 * diagnostics and the support bundle all read this one file; nothing re-reads modified game files.
 */

export const HINT_FILE_NAME = 'hints.json';

const exists = (target: string): Promise<boolean> => access(target).then(() => true, () => false);

/** The highest combined attempt of a run folder that holds a placements document (combined-N, then the seed folder, then randomizer.json). */
export async function findPlacementsFile(workspacePath: string): Promise<string | null> {
  try {
    const attempts = (await readdir(workspacePath)).filter((name) => /^combined-\d+$/u.test(name)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
    for (const attempt of attempts) {
      const seeds = (await readdir(path.join(workspacePath, attempt))).filter((name) => name.startsWith('seed_'));
      for (const seed of seeds) {
        const candidate = path.join(workspacePath, attempt, seed, 'randomizer.json');
        if (await exists(candidate)) return candidate;
      }
    }
  } catch { /* no combined attempt */ }
  return null;
}

export interface HintSource {
  seedCode: string;
  profile: string;
  createdAt: string;
  appVersion: string;
  settings: GenerationSettings;
}

/** Builds the hint data from a placements document (`randomizer.json`); null when the document has no placement lists. */
export function buildHintDocument(source: HintSource, placementsDocument: unknown): HintDocument | null {
  const placements = (placementsDocument as { placements?: Record<string, PlacementLike[]> } | null)?.placements;
  if (!placements || typeof placements !== 'object') return null;
  const { records, unhintable } = hintRecords(placements);
  return {
    format: 'ds1tr-hint-data', schemaVersion: HINT_DATA_SCHEMA_VERSION, seedCode: source.seedCode, generationProfile: source.profile,
    createdAt: source.createdAt, appVersion: source.appVersion, settings: source.settings, records, unhintable,
  };
}

/** Writes `hints.json` into the run folder; returns its path, or null when the run has no placements document. */
export async function persistHints(workspacePath: string, source: HintSource): Promise<string | null> {
  const placementsFile = await findPlacementsFile(workspacePath);
  if (!placementsFile) return null;
  let document: HintDocument | null;
  try { document = buildHintDocument(source, JSON.parse(await readFile(placementsFile, 'utf8'))); } catch { return null; }
  if (!document) return null;
  const target = path.join(workspacePath, HINT_FILE_NAME);
  const temporary = `${target}.${process.pid}.tmp`;
  await mkdir(workspacePath, { recursive: true });
  await writeFile(temporary, JSON.stringify(document, null, 2));
  await rename(temporary, target);
  return target;
}

export async function readHintDocument(file: string): Promise<HintDocument | null> {
  try {
    const parsed = JSON.parse(await readFile(file, 'utf8')) as Partial<HintDocument>;
    if (parsed.format !== 'ds1tr-hint-data' || parsed.schemaVersion !== HINT_DATA_SCHEMA_VERSION || !Array.isArray(parsed.records) || typeof parsed.seedCode !== 'string') return null;
    return parsed as HintDocument;
  } catch { return null; }
}

/** The run folder (the one with the `combined-<n>` attempts) that a package belongs to, looking upward from the package, never above `runRoot`. */
export async function locateRunFolder(packagePath: string, runRoot: string): Promise<string | null> {
  const root = path.resolve(runRoot);
  let current = path.resolve(packagePath);
  for (let depth = 0; depth < 6; depth += 1) {
    const relative = path.relative(root, current);
    if (relative === '' || relative.startsWith('..') || path.isAbsolute(relative)) return null;
    try {
      if ((await readdir(current)).some((name) => /^combined-\d+$/u.test(name))) return current;
    } catch { /* not a folder */ }
    current = path.dirname(current);
  }
  return null;
}

/**
 * The hint data of a history entry: the file written at generation, or, for an entry from before the hint sheet existed, built now from the
 * placements document the run folder still holds. Nothing is invented: without that document the hint sheet is simply not available.
 */
export async function hintsForEntry(entry: RunHistoryEntry, runRoot: string, appVersion: string): Promise<HintDocument | null> {
  if (entry.hintsPath && await exists(entry.hintsPath)) {
    const stored = await readHintDocument(entry.hintsPath);
    if (stored) return stored;
  }
  const folder = await locateRunFolder(entry.packagePath, runRoot);
  if (!folder) return null;
  const stored = await readHintDocument(path.join(folder, HINT_FILE_NAME));
  if (stored && stored.seedCode === entry.seedCode) return stored;
  const placementsFile = await findPlacementsFile(folder);
  if (!placementsFile) return null;
  let manifestVersion = appVersion;
  try { manifestVersion = (JSON.parse(await readFile(entry.manifestPath, 'utf8')) as { appVersion?: string }).appVersion ?? appVersion; } catch { /* keep the running version */ }
  try {
    return buildHintDocument({ seedCode: entry.seedCode, profile: entry.profile, createdAt: entry.createdAt, appVersion: manifestVersion, settings: entry.settings }, JSON.parse(await readFile(placementsFile, 'utf8')));
  } catch { return null; }
}

export async function hintsAvailableFor(entry: RunHistoryEntry, runRoot: string): Promise<boolean> {
  if (entry.hintsPath && await exists(entry.hintsPath)) return true;
  const folder = await locateRunFolder(entry.packagePath, runRoot);
  return folder !== null && await findPlacementsFile(folder) !== null;
}
