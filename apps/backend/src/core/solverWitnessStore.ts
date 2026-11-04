import { access, mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { RunHistoryEntry, SolverWitnessContext, SolverWitnessDocument } from '@ds1tr/contracts';
import { isSolverWitnessDocument, renderSolverRoute, SOLVER_ROUTE_FILE_NAME, SOLVER_WITNESS_FILE_NAME } from '@ds1tr/contracts';

/**
 * Persistence of the solver witness (docs/SOLVER_WITNESS.md). FogHost records, inside the search that accepted the Fog graph, why every Area was first
 * believed reachable, and leaves `solver-witness.json` next to the attempt's log (never in the package, which is hashed and applied to the game). After a
 * successful run the ACCEPTED attempt's witness is copied to the run folder with the run context, and the human-readable `solver-route.md` is written
 * from it. Both are deterministic for the same Seed Code, profile and world data (no timestamps, no paths). Nothing here influences generation.
 */

const exists = (target: string): Promise<boolean> => access(target).then(() => true, () => false);

export interface AttemptRecord { combinedAttempt: number; fogAttempt: number; combinedSeed: string; fogSeed: number; result: string }

export interface SolverWitnessSource {
  seedCode: string;
  profile: string;
  appVersion: string;
  masterSeed: string | null;
  attempts: AttemptRecord[];
}

/** The attempt directory name that FogHost wrote into, as the pipeline names it. */
export const fogAttemptDirectory = (attempt: { combinedAttempt: number; fogAttempt: number }): string => `fog-${attempt.combinedAttempt}-${attempt.fogAttempt}`;

export function witnessContext(source: SolverWitnessSource): SolverWitnessContext {
  const accepted = [...source.attempts].reverse().find((attempt) => attempt.result === 'success') ?? null;
  return {
    seedCode: source.seedCode, generationProfile: source.profile, appVersion: source.appVersion, masterSeed: source.masterSeed,
    fogSeed: accepted?.fogSeed ?? null, combinedSeed: accepted?.combinedSeed ?? null,
    combinedAttempt: accepted?.combinedAttempt ?? null, fogAttempt: accepted?.fogAttempt ?? null,
    attempts: source.attempts.map((attempt) => ({ combinedAttempt: attempt.combinedAttempt, fogAttempt: attempt.fogAttempt, fogSeed: attempt.fogSeed, result: attempt.result })),
  };
}

export async function readWitnessDocument(file: string): Promise<SolverWitnessDocument | null> {
  try {
    const parsed: unknown = JSON.parse(await readFile(file, 'utf8'));
    return isSolverWitnessDocument(parsed) ? parsed : null;
  } catch { return null; }
}

async function writeAtomically(target: string, contents: string): Promise<void> {
  const temporary = `${target}.${process.pid}.tmp`;
  await writeFile(temporary, contents);
  await rename(temporary, target);
}

/**
 * Copies the accepted attempt's witness to the run folder (`solver-witness.json`, `solver-route.md`). Returns the witness path, or null when the run has
 * none: Fog off, a generation profile that does not record one, or a host that could not write it (generation never depends on it).
 */
export async function persistSolverWitness(workspacePath: string, source: SolverWitnessSource): Promise<string | null> {
  const accepted = [...source.attempts].reverse().find((attempt) => attempt.result === 'success');
  if (!accepted) return null;
  const hostWitness = await readWitnessDocument(path.join(workspacePath, fogAttemptDirectory(accepted), SOLVER_WITNESS_FILE_NAME));
  if (!hostWitness) return null;
  const document: SolverWitnessDocument = { ...hostWitness, context: witnessContext(source) };
  await mkdir(workspacePath, { recursive: true });
  const target = path.join(workspacePath, SOLVER_WITNESS_FILE_NAME);
  await writeAtomically(target, `${JSON.stringify(document, null, 2)}\n`);
  await writeAtomically(path.join(workspacePath, SOLVER_ROUTE_FILE_NAME), renderSolverRoute(document, { includeTree: true }));
  return target;
}

/** The run folder that holds the witness, looking upward from the package (`<run>/fog-N-M/game`), never above `runRoot`. */
export async function locateWitnessFile(packagePath: string, runRoot: string): Promise<string | null> {
  const root = path.resolve(runRoot);
  let current = path.resolve(packagePath);
  for (let depth = 0; depth < 6; depth += 1) {
    const relative = path.relative(root, current);
    if (relative === '' || relative.startsWith('..') || path.isAbsolute(relative)) return null;
    // The host's own sidecar inside an attempt folder (no run context) is not the run-level witness.
    if (!/^fog-\d+-\d+$/u.test(path.basename(current))) {
      const candidate = path.join(current, SOLVER_WITNESS_FILE_NAME);
      if (await exists(candidate)) return candidate;
    }
    current = path.dirname(current);
  }
  return null;
}

export async function solverRouteForEntry(entry: RunHistoryEntry, runRoot: string): Promise<{ document: SolverWitnessDocument; markdown: string } | null> {
  const file = await locateWitnessFile(entry.packagePath, runRoot);
  if (!file) return null;
  const document = await readWitnessDocument(file);
  if (!document || (document.context && document.context.seedCode !== entry.seedCode)) return null;
  return { document, markdown: renderSolverRoute(document) };
}

export async function solverRouteAvailableFor(entry: RunHistoryEntry, runRoot: string): Promise<boolean> {
  return (await locateWitnessFile(entry.packagePath, runRoot)) !== null;
}
