import { access, cp, mkdir, readFile } from 'node:fs/promises';
import path from 'node:path';

/**
 * Copies the production dependency closure of the named packages from the workspace `node_modules` into the staging folder of the packaged application
 * (`release/package-app/node_modules`), keeping the tree exactly as npm resolved it (a version nested under a package stays nested). The packaged application has no
 * workspace: its only runtime dependency is `electron-updater` (docs/AUTO_UPDATE.md), and what the lockfile pinned is what ships. `dependencies` only; development and
 * optional dependencies are never staged.
 */
export async function stageRuntimeDependencies({ root, names, destination }) {
  const rootModules = path.join(root, 'node_modules');
  const staged = new Map();
  const exists = (target) => access(target).then(() => true, () => false);

  async function resolveFrom(fromDirectory, name) {
    let directory = fromDirectory;
    for (;;) {
      const candidate = path.join(directory, 'node_modules', name);
      if (await exists(path.join(candidate, 'package.json'))) return candidate;
      const parent = path.dirname(directory);
      if (parent === directory || directory === path.dirname(rootModules)) return null;
      directory = parent;
    }
  }

  async function stage(name, fromDirectory) {
    const resolved = await resolveFrom(fromDirectory, name);
    if (resolved === null) throw new Error(`The runtime dependency ${name} (needed from ${fromDirectory}) is not installed. Run npm ci first.`);
    if (staged.has(resolved)) return;
    const relative = path.relative(rootModules, resolved);
    if (relative.startsWith('..')) throw new Error(`${resolved} is outside the workspace node_modules.`);
    staged.set(resolved, relative);
    const target = path.join(destination, relative);
    await mkdir(path.dirname(target), { recursive: true });
    await cp(resolved, target, { recursive: true });
    const manifest = JSON.parse(await readFile(path.join(resolved, 'package.json'), 'utf8'));
    for (const dependency of Object.keys(manifest.dependencies ?? {})) await stage(dependency, resolved);
  }

  for (const name of names) await stage(name, root);
  return [...staged.values()].sort();
}
