import { execFile } from 'node:child_process';
import { realpathSync } from 'node:fs';
import { access, readFile } from 'node:fs/promises';
import path from 'node:path';
import type { InstallationValidation } from '@ds1tr/contracts';
import { validateInstallation } from './installation.js';

/** Steam's app id for Dark Souls: Remastered. */
export const dsrSteamAppId = '570940';
const defaultInstallFolder = 'DARK SOULS REMASTERED';

export interface DiscoveredInstallation {
  gamePath: string;
  /** Short text for a person: where it was found. */
  label: string;
  valid: boolean;
  /** Plain-language reasons when the folder is not usable. */
  problems: string[];
}

export interface DiscoveryEnvironment {
  platform: NodeJS.Platform;
  env: NodeJS.ProcessEnv;
  /** Reads one value of a registry key (Windows); returns null when it is missing. */
  readRegistry(key: string, value: string): Promise<string | null>;
  readText(file: string): Promise<string | null>;
  exists(target: string): Promise<boolean>;
  validate(gamePath: string): Promise<InstallationValidation>;
  /** The folder as the file system spells it (Steam stores some paths in lower case). */
  canonical?(target: string): Promise<string>;
}

async function defaultReadRegistry(key: string, value: string): Promise<string | null> {
  return new Promise((resolve) => {
    execFile('reg', ['query', key, '/v', value], { windowsHide: true, timeout: 5000 }, (error, stdout) => {
      if (error) return resolve(null);
      const match = stdout.match(new RegExp(`${value}\\s+REG_\\w+\\s+(.+)`, 'iu'));
      resolve(match ? match[1].trim() : null);
    });
  });
}

export const defaultDiscoveryEnvironment: DiscoveryEnvironment = {
  platform: process.platform,
  env: process.env,
  readRegistry: defaultReadRegistry,
  readText: async (file) => { try { return await readFile(file, 'utf8'); } catch { return null; } },
  exists: async (target) => { try { await access(target); return true; } catch { return false; } },
  validate: validateInstallation,
  canonical: async (target) => { try { return realpathSync.native(target); } catch { return target; } },
};

/** Library folders listed in Steam's libraryfolders.vdf (the `"path"` entries, with `\\` unescaped). */
export function parseLibraryFolders(text: string): string[] {
  const folders: string[] = [];
  for (const match of text.matchAll(/"path"\s+"([^"]+)"/gu)) folders.push(match[1].replace(/\\\\/gu, '\\'));
  return folders;
}

/** The `installdir` of Steam's appmanifest for the game, if the manifest exists. */
export function parseInstallDir(text: string): string | null {
  return text.match(/"installdir"\s+"([^"]+)"/u)?.[1] ?? null;
}

const normalize = (value: string): string => path.resolve(value).toLowerCase();

/**
 * Finds Dark Souls: Remastered in the Steam libraries of this computer without asking the user anything: the Steam folder
 * comes from the registry (or the usual install locations), every library from libraryfolders.vdf, and the game folder
 * from the app manifest. Read-only. Returns every installation found, valid or not, so the app can offer a plain choice.
 */
export async function discoverGameInstallations(environment: DiscoveryEnvironment = defaultDiscoveryEnvironment): Promise<DiscoveredInstallation[]> {
  const steamRoots: string[] = [];
  if (environment.platform === 'win32') {
    for (const [key, value] of [
      ['HKCU\\Software\\Valve\\Steam', 'SteamPath'],
      ['HKLM\\SOFTWARE\\WOW6432Node\\Valve\\Steam', 'InstallPath'],
      ['HKLM\\SOFTWARE\\Valve\\Steam', 'InstallPath'],
    ] as const) {
      const found = await environment.readRegistry(key, value);
      if (found) steamRoots.push(found.replace(/\//gu, '\\'));
    }
    for (const variable of ['ProgramFiles(x86)', 'ProgramFiles']) {
      const base = environment.env[variable];
      if (base) steamRoots.push(path.join(base, 'Steam'));
    }
  }

  const libraries: string[] = [];
  for (const steamRoot of steamRoots) {
    libraries.push(steamRoot);
    const listing = await environment.readText(path.join(steamRoot, 'steamapps', 'libraryfolders.vdf'));
    if (listing) libraries.push(...parseLibraryFolders(listing));
  }

  const seenLibraries = new Set<string>();
  const seenGames = new Set<string>();
  const found: DiscoveredInstallation[] = [];
  for (const library of libraries) {
    const key = normalize(library);
    if (seenLibraries.has(key)) continue;
    seenLibraries.add(key);
    const steamapps = path.join(library, 'steamapps');
    const manifest = await environment.readText(path.join(steamapps, `appmanifest_${dsrSteamAppId}.acf`));
    const folder = (manifest && parseInstallDir(manifest)) || defaultInstallFolder;
    let gamePath = path.join(steamapps, 'common', folder);
    if (!await environment.exists(path.join(gamePath, 'DarkSoulsRemastered.exe'))) continue;
    if (environment.canonical) gamePath = await environment.canonical(gamePath);
    const gameKey = normalize(gamePath);
    if (seenGames.has(gameKey)) continue;
    seenGames.add(gameKey);
    const validation = await environment.validate(gamePath);
    found.push({
      gamePath,
      label: 'Steam library',
      valid: validation.valid,
      problems: validation.valid ? [] : ['This folder does not look like a complete Dark Souls Remastered installation. Verify the game files in Steam.'],
    });
  }
  return found;
}
