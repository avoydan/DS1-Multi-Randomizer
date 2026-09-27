import path from 'node:path';

/**
 * Pure path/argument planning for the combined engine's workspace-only
 * generation path. This module performs no filesystem I/O and never
 * constructs arguments for upstream `install`/`restore` — only `scan` and
 * `patch-enemies`, the two upstream DsrDataTool commands confirmed to write
 * exclusively under a caller-supplied output directory, never under
 * `--game`. See docs/COMBINED_ENGINE_INTEGRATION.md ("Workspace execution
 * proof") for the upstream evidence behind this boundary.
 */

export interface CombinedRandomizerWorkspaceRequest {
  gamePath: string;
  workspacePath: string;
  combinedSeed: string;
}

export interface CombinedRandomizerWorkspacePlan {
  gameDirectory: string;
  workspaceDirectory: string;
  catalogPath: string;
  seedDirectory: string;
  randomizerPlacementsPath: string;
  scanArgs: string[];
  patchEnemiesArgs: string[];
}

export type CombinedRandomizerWorkspacePlanResult =
  | { ok: true; plan: CombinedRandomizerWorkspacePlan }
  | { ok: false; code: 'WORKSPACE_INSIDE_GAME_DIRECTORY'; message: string };

/** Mirrors upstream `safeSeed()` in `src/core/output.js`: used only for the
 * `seed_<...>` directory name, independent of upstream's stricter
 * `config.seed` charset validation (a separate, already-documented
 * blocker). */
export function sanitizeUpstreamSeed(seed: string): string {
  return seed.replace(/[^\w.-]/gu, '_');
}

function isInsideOrEqual(parentDirectory: string, candidate: string): boolean {
  const relative = path.relative(parentDirectory, candidate);
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

/**
 * Computes the workspace-only file layout and upstream command argument
 * vectors for one combined-engine generation run, and refuses to plan a
 * workspace located inside the live game directory. This mirrors (and
 * fails fast ahead of) the guard upstream's `patch-enemies` itself enforces
 * before writing anything.
 */
export function planCombinedRandomizerWorkspace(
  request: CombinedRandomizerWorkspaceRequest,
): CombinedRandomizerWorkspacePlanResult {
  const gameDirectory = path.resolve(request.gamePath);
  const workspaceDirectory = path.resolve(request.workspacePath);

  if (isInsideOrEqual(gameDirectory, workspaceDirectory)) {
    return {
      ok: false,
      code: 'WORKSPACE_INSIDE_GAME_DIRECTORY',
      message:
        'The workspace directory must not be the live game directory or a path inside it. '
        + 'Generation must never write into the live Dark Souls Remastered installation.',
    };
  }

  const catalogPath = path.join(workspaceDirectory, 'dsr-catalog.json');
  const seedDirectory = path.join(workspaceDirectory, `seed_${sanitizeUpstreamSeed(request.combinedSeed)}`);
  const randomizerPlacementsPath = path.join(seedDirectory, 'randomizer.json');

  return {
    ok: true,
    plan: {
      gameDirectory,
      workspaceDirectory,
      catalogPath,
      seedDirectory,
      randomizerPlacementsPath,
      scanArgs: ['scan', '--game', gameDirectory, '--output', catalogPath],
      patchEnemiesArgs: [
        'patch-enemies',
        '--game', gameDirectory,
        '--catalog', catalogPath,
        '--placements', randomizerPlacementsPath,
        '--output', seedDirectory,
      ],
    },
  };
}
