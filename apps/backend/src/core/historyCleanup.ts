import { readdir, readFile, rm, stat } from 'node:fs/promises';
import path from 'node:path';
import type { HistoryCleanupPreview, HistoryCleanupResult, HistoryDeleteResult, RunHistoryEntry } from '@ds1tr/contracts';

/**
 * Run history cleanup (docs/RUN_HISTORY_CLEANUP.md).
 *
 * What a run owns: one folder `<state>/runs/<uuid>/` that the generation wrote into (the package, the attempt folders, the spoiler, the hint data, the solver witness and route, the manifest,
 * a failed-run manifest) plus one record in `<state>/history/runs.json`. That is everything a cleanup may delete.
 *
 * What a cleanup never touches: the pristine snapshot and the activation journal (`<state>/installs/...`, the only things Restore needs), the prepared world data
 * (`<state>/world-data`), the Dark Souls installation and its saves, the settings, the release artifacts, support bundles, and the folder of the currently applied run.
 */

/** A folder younger than this is never removed as an orphan: it may belong to a generation that is still running. */
export const ORPHAN_MINIMUM_AGE_MS = 10 * 60 * 1000;

export type CleanupPreview = HistoryCleanupPreview;
export type CleanupResult = HistoryCleanupResult;
export type DeleteRunResult = HistoryDeleteResult;

const normalize = (target: string): string => path.resolve(target).toLowerCase();

/** The package paths the activation journals of every installation name (the applied state of the game). */
export async function activePackagePaths(stateRoot: string): Promise<Set<string>> {
  const active = new Set<string>();
  let installs: string[];
  try { installs = await readdir(path.join(stateRoot, 'installs')); } catch { return active; }
  for (const install of installs) {
    try {
      const journal = JSON.parse(await readFile(path.join(stateRoot, 'installs', install, 'activation.json'), 'utf8')) as { packagePath?: unknown };
      if (typeof journal.packagePath === 'string' && journal.packagePath.length > 0) active.add(normalize(journal.packagePath));
    } catch { /* no journal: nothing is applied for this installation */ }
  }
  return active;
}

/** The run folder an entry owns: the direct child of the run root that holds its package; null when the entry points anywhere else (it is never deleted then). */
export function runFolderOf(entry: Pick<RunHistoryEntry, 'packagePath' | 'manifestPath'>, runRoot: string): string | null {
  const root = path.resolve(runRoot);
  for (const candidate of [entry.packagePath, entry.manifestPath]) {
    if (typeof candidate !== 'string' || candidate.length === 0) continue;
    const relative = path.relative(root, path.resolve(candidate));
    if (relative === '' || relative.startsWith('..') || path.isAbsolute(relative)) continue;
    const first = relative.split(path.sep)[0];
    if (first) return path.join(root, first);
  }
  return null;
}

function isInside(parent: string, child: string): boolean {
  const relative = path.relative(path.resolve(parent), path.resolve(child));
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

/** An applied run stays: its status says so, or its package (or a file inside its run folder) is what the activation journal names. */
export function isProtected(entry: RunHistoryEntry, runRoot: string, active: ReadonlySet<string>): boolean {
  if (entry.status === 'applied') return true;
  if (active.has(normalize(entry.packagePath))) return true;
  const folder = runFolderOf(entry, runRoot);
  if (folder) for (const journalPackage of active) if (isInside(folder, journalPackage)) return true;
  return false;
}

async function folderAge(folder: string, now: number): Promise<number> {
  try { return now - (await stat(folder)).mtimeMs; } catch { return Number.POSITIVE_INFINITY; }
}

async function runFolders(runRoot: string): Promise<string[]> {
  try {
    return (await readdir(runRoot, { withFileTypes: true })).filter((entry) => entry.isDirectory()).map((entry) => path.join(runRoot, entry.name));
  } catch { return []; }
}

/** Counts only; nothing is changed. */
export async function previewCleanup(entries: readonly RunHistoryEntry[], runRoot: string, stateRoot: string, currentProfile: string, now = Date.now()): Promise<CleanupPreview> {
  const active = await activePackagePaths(stateRoot);
  const protectedEntries = entries.filter((entry) => isProtected(entry, runRoot, active));
  const kept = new Set(protectedEntries.map((entry) => runFolderOf(entry, runRoot)).filter((folder): folder is string => folder !== null).map(normalize));
  for (const journalPackage of active) kept.add(normalize(journalPackage));
  const referenced = new Set(entries.map((entry) => runFolderOf(entry, runRoot)).filter((folder): folder is string => folder !== null).map(normalize));
  let orphanFolders = 0;
  for (const folder of await runFolders(runRoot)) {
    if (referenced.has(normalize(folder)) || [...active].some((journalPackage) => isInside(folder, journalPackage))) continue;
    if (await folderAge(folder, now) >= ORPHAN_MINIMUM_AGE_MS) orphanFolders += 1;
  }
  return {
    total: entries.length,
    removable: entries.length - protectedEntries.length,
    protected: protectedEntries.length,
    removableOld: entries.filter((entry) => !isProtected(entry, runRoot, active) && entry.profile !== currentProfile).length,
    orphanFolders,
  };
}

/** Deletes a run folder only when it is a direct child of the run root; returns whether it existed and was removed. */
async function removeRunFolder(folder: string, runRoot: string): Promise<boolean | null> {
  const relative = path.relative(path.resolve(runRoot), path.resolve(folder));
  if (relative === '' || relative.startsWith('..') || path.isAbsolute(relative) || relative.includes(path.sep)) return null;
  try { await stat(folder); } catch { return false; }
  try { await rm(folder, { recursive: true, force: true }); return true; } catch { return null; }
}

export interface ClearOptions {
  /** Which unprotected entries go; default: all of them. */
  select?: (entry: RunHistoryEntry) => boolean;
  /** Also delete the run folders no entry references (failed or interrupted runs). */
  includeOrphans: boolean;
  currentProfile?: string;
  now?: number;
}

/** What a history store offers to the cleanup (RunHistory implements it). */
export interface HistoryStore {
  list(): Promise<RunHistoryEntry[]>;
  removeMany(ids: readonly string[]): Promise<number>;
}

export async function clearHistory(store: HistoryStore, runRoot: string, stateRoot: string, options: ClearOptions): Promise<CleanupResult> {
  const active = await activePackagePaths(stateRoot);
  const entries = await store.list();
  const select = options.select ?? (() => true);
  const doomed = entries.filter((entry) => !isProtected(entry, runRoot, active) && select(entry));
  const doomedIds = new Set(doomed.map((entry) => entry.id));
  const kept = entries.filter((entry) => !doomedIds.has(entry.id));
  // A folder is deleted only when no entry that stays points into it (a refreshed world can share a folder with an older record).
  const keptFolders = new Set(kept.map((entry) => runFolderOf(entry, runRoot)).filter((folder): folder is string => folder !== null).map(normalize));
  const foldersToRemove = new Set<string>();
  for (const entry of doomed) {
    const folder = runFolderOf(entry, runRoot);
    if (folder && !keptFolders.has(normalize(folder)) && ![...active].some((journalPackage) => isInside(folder, journalPackage))) foldersToRemove.add(folder);
  }
  if (options.includeOrphans) {
    const referenced = new Set(entries.map((entry) => runFolderOf(entry, runRoot)).filter((folder): folder is string => folder !== null).map(normalize));
    const now = options.now ?? Date.now();
    for (const folder of await runFolders(runRoot)) {
      if (referenced.has(normalize(folder)) || [...active].some((journalPackage) => isInside(folder, journalPackage))) continue;
      if (await folderAge(folder, now) >= ORPHAN_MINIMUM_AGE_MS) foldersToRemove.add(folder);
    }
  }
  // The history record goes first: if a folder cannot be deleted the run is still gone from the list, never the other way round.
  const removedEntries = await store.removeMany(doomed.map((entry) => entry.id));
  let removedFolders = 0; let failedFolders = 0;
  for (const folder of foldersToRemove) {
    const removed = await removeRunFolder(folder, runRoot);
    if (removed === true) removedFolders += 1; else if (removed === null) failedFolders += 1;
  }
  return { ok: true, removedEntries, keptEntries: entries.length - removedEntries, removedFolders, failedFolders };
}

/** One run: refused when it is the applied one. */
export async function deleteRun(store: HistoryStore & { get(id: string): Promise<RunHistoryEntry | null> }, id: string, runRoot: string, stateRoot: string): Promise<DeleteRunResult> {
  const entry = await store.get(id);
  if (!entry) return { ok: false, code: 'RUN_NOT_FOUND', message: 'This run is no longer in the history.' };
  const active = await activePackagePaths(stateRoot);
  if (isProtected(entry, runRoot, active)) {
    return { ok: false, code: 'RUN_PROTECTED', message: 'This run is applied to the game. Restore the original files first, then delete it.' };
  }
  const all = await store.list();
  const folder = runFolderOf(entry, runRoot);
  const shared = folder !== null && all.some((other) => other.id !== entry.id && runFolderOf(other, runRoot) !== null && normalize(runFolderOf(other, runRoot)!) === normalize(folder));
  await store.removeMany([id]);
  let removedFolder = false;
  if (folder && !shared) removedFolder = (await removeRunFolder(folder, runRoot)) === true;
  return { ok: true, removedFolder };
}
