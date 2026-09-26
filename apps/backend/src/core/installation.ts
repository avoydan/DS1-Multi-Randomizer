import { access } from 'node:fs/promises';
import path from 'node:path';
import type { InstallationValidation } from '@ds1tr/contracts';

async function exists(target: string): Promise<boolean> {
  try {
    await access(target);
    return true;
  } catch {
    return false;
  }
}

export async function validateInstallation(gamePath: string): Promise<InstallationValidation> {
  const root = path.resolve(gamePath || '.');
  const executableFound = await exists(path.join(root, 'DarkSoulsRemastered.exe'));
  const gameParamFound = await exists(path.join(root, 'param', 'GameParam', 'GameParam.parambnd.dcx'));
  const mapStudioFound = await exists(path.join(root, 'map', 'MapStudio'));
  const scriptFound = await exists(path.join(root, 'script'));
  const eventFound = await exists(path.join(root, 'event'));

  const problems: string[] = [];
  if (!executableFound) problems.push('DarkSoulsRemastered.exe not found.');
  if (!gameParamFound) problems.push('param/GameParam/GameParam.parambnd.dcx not found.');
  if (!mapStudioFound) problems.push('map/MapStudio directory not found.');
  if (!scriptFound) problems.push('script directory not found.');
  if (!eventFound) problems.push('event directory not found.');

  return {
    valid: problems.length === 0,
    gamePath: root,
    executableFound,
    gameParamFound,
    mapStudioFound,
    scriptFound,
    eventFound,
    problems,
  };
}
