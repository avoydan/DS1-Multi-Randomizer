import type { LaunchGameResult } from '@ds1tr/contracts';
import path from 'node:path';

/** Steam's id for Dark Souls: Remastered (kept in sync with DSR_STEAM_APP_ID in the shared contracts; type-only import above). */
const STEAM_APP_ID = '570940';

export interface LaunchDependencies {
  /** Opens a URL with the operating system (Electron: shell.openExternal); rejects when no program handles it. */
  openExternal(url: string): Promise<void>;
  /** Starts a program detached from the app. */
  startDetached(executable: string, directory: string): Promise<void>;
  exists(target: string): Promise<boolean>;
}

/**
 * Starts Dark Souls Remastered: through Steam (`steam://rungameid/570940`, which also keeps Steam's overlay, playtime and
 * online checks working) and, only when that fails, by running the game executable of the installation. The person never
 * browses for anything; the exact reason of a failure is returned for the log.
 */
export async function launchGame(gamePath: string, dependencies: LaunchDependencies): Promise<LaunchGameResult> {
  let steamFailure = '';
  try {
    await dependencies.openExternal(`steam://rungameid/${STEAM_APP_ID}`);
    return { ok: true, method: 'steam', message: 'Starting Dark Souls Remastered through Steam...' };
  } catch (error) {
    steamFailure = error instanceof Error ? error.message : String(error);
  }
  const executable = path.join(gamePath, 'DarkSoulsRemastered.exe');
  try {
    if (gamePath && await dependencies.exists(executable)) {
      await dependencies.startDetached(executable, gamePath);
      return { ok: true, method: 'executable', message: 'Starting Dark Souls Remastered...', technical: `Steam could not be used (${steamFailure}).` };
    }
  } catch (error) {
    return {
      ok: false, method: 'none', message: 'Dark Souls Remastered could not be started. Please start it from Steam.',
      technical: `Steam: ${steamFailure}; executable: ${error instanceof Error ? error.message : String(error)}`,
    };
  }
  return {
    ok: false, method: 'none', message: 'Dark Souls Remastered could not be started. Please start it from Steam.',
    technical: `Steam: ${steamFailure}; the game executable was not found at ${executable}.`,
  };
}
