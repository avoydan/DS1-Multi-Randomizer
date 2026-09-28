import { execFile } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { copyFile, mkdir, readFile, readdir, rename, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { validateInstallation } from './installation.js';

/**
 * The single backup/restore authority (docs/DECISIONS.md, "One backend-owned
 * activation/restore authority"). It owns the pristine snapshot, the
 * activation journal and every write into a live DSR installation. No engine
 * or legacy tool backup chain (`.bak`, DsrDataTool `install`/`restore`) is
 * ever consulted or created.
 */

/** Game-relative directories covered by the pristine snapshot. */
export const managedRoots = ['event', 'map/MapStudio', 'msg', 'param/GameParam', 'script', 'sfx'] as const;
export const executableName = 'DarkSoulsRemastered.exe';
/** Suffix of in-flight atomic copies inside the game directory. */
export const tempSuffix = '.ds1tr-tmp';

export interface SnapshotFile { path: string; size: number; sha256: string }

export interface PristineSnapshot {
  schemaVersion: 1;
  gamePath: string;
  createdAt: string;
  snapshotId: string;
  executable: SnapshotFile;
  roots: string[];
  files: SnapshotFile[];
  provenance: { legacyArtifactsChecked: true; referenceHashesVerified: boolean };
}

export interface ActivationJournal {
  schemaVersion: 1;
  status: 'activating' | 'active';
  snapshotId: string;
  packagePath: string;
  masterSeed: string;
  startedAt: string;
  completedAt: string | null;
  files: Array<{ path: string; pristineSha256: string; packageSha256: string }>;
}

export type GameStateKind =
  | 'invalid'          // not a DSR installation
  | 'no-snapshot'      // no pristine snapshot imported yet
  | 'version-changed'  // executable differs from the snapshot (game update)
  | 'vanilla'          // matches the snapshot, nothing active
  | 'active'           // a generated package is fully and exactly active
  | 'interrupted'      // an activation or restore did not finish
  | 'modified';        // changes this authority did not record

export interface GameStateReport {
  state: GameStateKind;
  gamePath: string;
  snapshotId: string | null;
  activePackage: string | null;
  problems: string[];
}

export type AuthorityResult<T = object> =
  | ({ ok: true } & T)
  | { ok: false; code: string; message: string; problems: string[] };

export interface GameStateAuthorityOptions {
  /** Backend-owned directory outside every game installation. */
  stateRoot: string;
  isGameRunning?: () => Promise<boolean>;
  /**
   * Optional verification against known vanilla hashes. Returns problems.
   * Without it a snapshot only proves the absence of known legacy artifacts.
   */
  verifyReferenceHashes?: (files: SnapshotFile[], executable: SnapshotFile) => Promise<string[]>;
  /** Injectable for fault tests; must behave like `fs.copyFile`. */
  copyFile?: (from: string, to: string) => Promise<void>;
}

interface GameInspection {
  report: GameStateReport;
  snapshot: PristineSnapshot | null;
  journal: ActivationJournal | null;
  /** Lower-cased snapshot path -> current game SHA-256 (null when missing). */
  current: Map<string, string | null>;
}

const maxListed = 20;

async function sha256File(file: string): Promise<string> {
  return createHash('sha256').update(await readFile(file)).digest('hex');
}

async function tryHash(file: string): Promise<string | null> {
  try { return await sha256File(file); } catch { return null; }
}

async function exists(target: string): Promise<boolean> {
  try { await stat(target); return true; } catch { return false; }
}

const key = (relative: string): string => relative.toLowerCase();
const toNative = (root: string, relative: string): string => path.join(root, ...relative.split('/'));

function contained(parent: string, child: string): boolean {
  const relative = path.relative(path.resolve(parent), path.resolve(child));
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

function safeRelative(value: string): boolean {
  if (!value || value.includes('\\') || value.includes(':') || value.startsWith('/')) return false;
  return value.split('/').every((part) => part !== '' && part !== '.' && part !== '..');
}

async function listFiles(root: string, relative: string): Promise<string[]> {
  const directory = toNative(root, relative);
  let entries;
  try { entries = await readdir(directory, { withFileTypes: true }); } catch { return []; }
  const files: string[] = [];
  for (const entry of entries) {
    const child = `${relative}/${entry.name}`;
    if (entry.isDirectory()) files.push(...await listFiles(root, child));
    else if (entry.isFile()) files.push(child);
    else throw new Error(`Unsupported entry in managed game data: ${child}`);
  }
  return files;
}

async function listManaged(root: string, roots: readonly string[]): Promise<string[]> {
  const files: string[] = [];
  for (const managed of roots) files.push(...await listFiles(root, managed));
  return files.sort((a, b) => a.localeCompare(b, 'en'));
}

async function writeJsonAtomic(file: string, value: unknown): Promise<void> {
  const temporary = `${file}.${randomBytes(4).toString('hex')}.tmp`;
  await writeFile(temporary, `${JSON.stringify(value, null, 2)}\n`, { flag: 'wx' });
  await rename(temporary, file);
}

async function readJson<T>(file: string): Promise<T | null> {
  try { return JSON.parse(await readFile(file, 'utf8')) as T; } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return null;
    throw error;
  }
}

function isLegacyArtifact(relative: string): boolean {
  return /\.bak$/i.test(relative);
}

function failure(code: string, message: string, problems: string[] = []): { ok: false; code: string; message: string; problems: string[] } {
  return { ok: false, code, message, problems: problems.slice(0, maxListed) };
}

async function defaultIsGameRunning(): Promise<boolean> {
  if (process.platform !== 'win32') return false;
  return new Promise((resolve) => {
    execFile('tasklist', ['/FI', `IMAGENAME eq ${executableName}`, '/NH'], { windowsHide: true },
      (error, stdout) => resolve(!error && stdout.toLowerCase().includes(executableName.toLowerCase())));
  });
}

export function defaultStateRoot(): string {
  return process.env.DS1TR_STATE_DIR
    ?? path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), '.local', 'share'), 'ds1-triple-randomizer', 'state');
}

/** Package-relative manifest fields activation relies on. */
interface PackageManifest {
  masterSeed: string;
  pristineSnapshotId: string | null;
  gameTreeRoot: string;
  files: Array<{ path: string; size: number; sha256: string }>;
}

const patchListKeys = ['patchedMaps', 'luaBundles', 'events'] as const;
const patchSingleKeys = ['ffxBundle', 'gameParam'] as const;

export class GameStateAuthority {
  private readonly stateRoot: string;
  private readonly isGameRunning: () => Promise<boolean>;
  private readonly verifyReferenceHashes?: GameStateAuthorityOptions['verifyReferenceHashes'];
  private readonly copy: (from: string, to: string) => Promise<void>;

  constructor(options: GameStateAuthorityOptions) {
    this.stateRoot = path.resolve(options.stateRoot);
    this.isGameRunning = options.isGameRunning ?? defaultIsGameRunning;
    this.verifyReferenceHashes = options.verifyReferenceHashes;
    this.copy = options.copyFile ?? ((from, to) => copyFile(from, to));
  }

  /** Per-installation state directory, keyed by the normalized game path. */
  installDirectory(gamePath: string): string {
    const id = createHash('sha256').update(path.resolve(gamePath).toLowerCase()).digest('hex').slice(0, 16);
    return path.join(this.stateRoot, 'installs', id);
  }

  private paths(gamePath: string) {
    const root = this.installDirectory(gamePath);
    return {
      root,
      snapshot: path.join(root, 'snapshot.json'),
      pristine: path.join(root, 'pristine'),
      journal: path.join(root, 'activation.json'),
      lock: path.join(root, 'lock'),
    };
  }

  private async withLock<T>(gamePath: string, action: () => Promise<AuthorityResult<T>>): Promise<AuthorityResult<T>> {
    const paths = this.paths(gamePath);
    if (contained(path.resolve(gamePath), this.stateRoot) || contained(this.stateRoot, path.resolve(gamePath))) {
      return failure('STATE_ROOT_UNSAFE', 'The backend state directory and the game installation must be separate.');
    }
    await mkdir(paths.root, { recursive: true });
    try {
      await writeFile(paths.lock, String(process.pid), { flag: 'wx' });
    } catch {
      const holder = Number(await readFile(paths.lock, 'utf8').catch(() => 'NaN'));
      let alive = holder === process.pid;
      if (Number.isInteger(holder) && holder > 0 && holder !== process.pid) {
        try { process.kill(holder, 0); alive = true; } catch { alive = false; }
      }
      if (alive) return failure('STATE_LOCKED', 'Another activation, restore or import is running for this installation.');
      await writeFile(paths.lock, String(process.pid));
    }
    try { return await action(); } finally { await rm(paths.lock, { force: true }); }
  }

  private async inspect(gamePath: string): Promise<GameInspection> {
    const root = path.resolve(gamePath);
    const paths = this.paths(root);
    const report: GameStateReport = { state: 'invalid', gamePath: root, snapshotId: null, activePackage: null, problems: [] };
    const current = new Map<string, string | null>();
    const installation = await validateInstallation(root);
    if (!installation.valid) {
      report.problems = installation.problems;
      return { report, snapshot: null, journal: null, current };
    }
    const snapshot = await readJson<PristineSnapshot>(paths.snapshot);
    if (!snapshot) {
      report.state = 'no-snapshot';
      return { report, snapshot: null, journal: null, current };
    }
    report.snapshotId = snapshot.snapshotId;
    const journal = await readJson<ActivationJournal>(paths.journal);
    report.activePackage = journal?.packagePath ?? null;
    if (await tryHash(path.join(root, executableName)) !== snapshot.executable.sha256) {
      report.state = 'version-changed';
      report.problems.push(`${executableName} differs from the pristine snapshot; the game was updated or replaced.`);
      return { report, snapshot, journal, current };
    }

    const known = new Map(snapshot.files.map((file) => [key(file.path), file]));
    const journalFiles = new Map((journal?.files ?? []).map((file) => [key(file.path), file]));
    const unexpected: string[] = [];
    let temporaries = 0;
    for (const relative of await listManaged(root, snapshot.roots)) {
      if (known.has(key(relative))) continue;
      if (relative.endsWith(tempSuffix)) temporaries++;
      else unexpected.push(`Unrecorded file: ${relative}`);
    }
    let atPackage = 0;
    let atPristine = 0;
    for (const file of snapshot.files) {
      const hash = await tryHash(toNative(root, file.path));
      current.set(key(file.path), hash);
      const activated = journalFiles.get(key(file.path));
      if (hash === file.sha256) { if (activated) atPristine++; continue; }
      if (activated && hash === activated.packageSha256) { atPackage++; continue; }
      unexpected.push(hash === null ? `Missing file: ${file.path}` : `Changed file: ${file.path}`);
    }
    report.problems = [...unexpected];
    if (journal && journal.snapshotId !== snapshot.snapshotId) {
      report.problems.push('The activation journal belongs to a different snapshot.');
      report.state = 'modified';
    } else if (unexpected.length > 0) {
      report.state = 'modified';
    } else if (!journal) {
      report.state = temporaries > 0 ? 'modified' : 'vanilla';
      if (temporaries > 0) report.problems.push(`${temporaries} leftover ${tempSuffix} file(s) without an activation journal.`);
    } else if (journal.status === 'active' && atPackage === journal.files.length && temporaries === 0) {
      report.state = 'active';
    } else {
      report.state = 'interrupted';
      report.problems.push(`Activation of ${journal.packagePath} did not complete (${atPackage}/${journal.files.length} files applied).`);
    }
    return { report, snapshot, journal, current };
  }

  /** Read-only state detection. Never writes to the game or the state root. */
  async detectState(gamePath: string): Promise<GameStateReport> {
    return (await this.inspect(gamePath)).report;
  }

  /**
   * Copies the managed game data into the backend-owned pristine store. The
   * snapshot is committed only after every copy is hash-verified.
   */
  async importPristine(gamePath: string): Promise<AuthorityResult<{ snapshot: PristineSnapshot }>> {
    return this.withLock(gamePath, async () => {
      const root = path.resolve(gamePath);
      const paths = this.paths(root);
      const installation = await validateInstallation(root);
      if (!installation.valid) return failure('GAME_INVALID', 'Not a DSR installation.', installation.problems);
      if (await exists(paths.snapshot)) {
        return failure('SNAPSHOT_EXISTS', 'A pristine snapshot already exists for this installation.');
      }
      if (await exists(paths.journal)) {
        return failure('GAME_STATE_UNKNOWN', 'An activation journal exists without a snapshot; refusing to import.');
      }
      const relatives = await listManaged(root, managedRoots);
      const artifacts = relatives.filter((relative) => isLegacyArtifact(relative) || relative.endsWith(tempSuffix));
      if (artifacts.length > 0) {
        return failure('LEGACY_ARTIFACTS_DETECTED',
          'The installation contains backup or temporary files left by other randomizers; it is probably not clean. Verify the game files in Steam first.',
          artifacts);
      }
      const partial = path.join(paths.root, `pristine.partial-${randomBytes(4).toString('hex')}`);
      await rm(paths.pristine, { recursive: true, force: true });
      try {
        const copyVerified = async (relative: string): Promise<SnapshotFile> => {
          const from = toNative(root, relative);
          const to = toNative(partial, relative);
          const sourceHash = await sha256File(from);
          await mkdir(path.dirname(to), { recursive: true });
          await this.copy(from, to);
          const copied = await sha256File(to);
          if (copied !== sourceHash) throw new Error(`Copy verification failed: ${relative}`);
          return { path: relative, size: (await stat(to)).size, sha256: copied };
        };
        for (const managed of managedRoots) await mkdir(toNative(partial, managed), { recursive: true });
        const executable = await copyVerified(executableName);
        const files: SnapshotFile[] = [];
        for (const relative of relatives) files.push(await copyVerified(relative));
        let referenceHashesVerified = false;
        if (this.verifyReferenceHashes) {
          const problems = await this.verifyReferenceHashes(files, executable);
          if (problems.length > 0) {
            await rm(partial, { recursive: true, force: true });
            return failure('NOT_VANILLA', 'The installation does not match known vanilla DSR data.', problems);
          }
          referenceHashesVerified = true;
        }
        const snapshotId = createHash('sha256')
          .update(JSON.stringify({ executable, files }))
          .digest('hex');
        const snapshot: PristineSnapshot = {
          schemaVersion: 1, gamePath: root, createdAt: new Date().toISOString(), snapshotId,
          executable, roots: [...managedRoots], files,
          provenance: { legacyArtifactsChecked: true, referenceHashesVerified },
        };
        await rename(partial, paths.pristine);
        // snapshot.json is written last: its presence commits the snapshot.
        await writeJsonAtomic(paths.snapshot, snapshot);
        return { ok: true, snapshot };
      } catch (error) {
        await rm(partial, { recursive: true, force: true });
        return failure('PRISTINE_IMPORT_FAILED', `Pristine import failed: ${String(error)}`);
      }
    });
  }

  /**
   * Trusted pristine-source provider for the generation pipeline. Returns the
   * snapshot directory only when every stored copy still verifies.
   */
  async getPristineSource(gamePath: string): Promise<{ directory: string; snapshotId: string } | null> {
    const paths = this.paths(gamePath);
    const snapshot = await readJson<PristineSnapshot>(paths.snapshot);
    if (!snapshot) return null;
    const problems = await this.verifyPristineCopies(paths.pristine, [snapshot.executable, ...snapshot.files]);
    return problems.length === 0 ? { directory: paths.pristine, snapshotId: snapshot.snapshotId } : null;
  }

  private async verifyPristineCopies(pristine: string, files: SnapshotFile[]): Promise<string[]> {
    const problems: string[] = [];
    for (const file of files) {
      if (await tryHash(toNative(pristine, file.path)) !== file.sha256) problems.push(`Pristine copy damaged: ${file.path}`);
    }
    return problems;
  }

  private async readPackage(packagePath: string, snapshot: PristineSnapshot)
    : Promise<AuthorityResult<{ manifest: PackageManifest; files: ActivationJournal['files']; sources: Map<string, string> }>> {
    const packageRoot = path.resolve(packagePath);
    const manifest = await readJson<PackageManifest>(path.join(packageRoot, 'run-manifest.json')).catch(() => null);
    if (!manifest || !Array.isArray(manifest.files) || typeof manifest.gameTreeRoot !== 'string') {
      return failure('PACKAGE_INVALID', 'The package has no readable run-manifest.json with a game tree root.');
    }
    if (manifest.pristineSnapshotId !== snapshot.snapshotId) {
      return failure('PACKAGE_SNAPSHOT_MISMATCH', 'The package was not generated from this installation\'s pristine snapshot.');
    }
    const treeRoot = manifest.gameTreeRoot;
    if (treeRoot !== '' && !safeRelative(treeRoot)) return failure('PACKAGE_INVALID', 'The package game tree root is unsafe.');
    const prefix = treeRoot === '' ? '' : `${treeRoot}/`;
    const known = new Map(snapshot.files.map((file) => [key(file.path), file]));
    const problems: string[] = [];
    const targets = new Map<string, { path: string; pristineSha256: string; packageSha256: string }>();
    const sources = new Map<string, string>();
    for (const file of manifest.files) {
      if (typeof file?.path !== 'string' || !safeRelative(file.path) || typeof file.sha256 !== 'string') {
        problems.push(`Unsafe or malformed manifest entry: ${String(file?.path)}`);
        continue;
      }
      if (await tryHash(toNative(packageRoot, file.path)) !== file.sha256) {
        problems.push(`Package file does not match its manifest hash: ${file.path}`);
        continue;
      }
      sources.set(file.path, file.sha256);
      if (!file.path.startsWith(prefix)) continue;
      const relative = file.path.slice(prefix.length);
      if (isLegacyArtifact(relative) || relative.endsWith(tempSuffix)) {
        problems.push(`Package contains a backup or temporary file: ${file.path}`);
        continue;
      }
      const pristine = known.get(key(relative));
      if (!pristine) { problems.push(`Package file is outside the pristine snapshot: ${relative}`); continue; }
      if (targets.has(key(relative))) { problems.push(`Package lists ${relative} more than once.`); continue; }
      targets.set(key(relative), { path: pristine.path, pristineSha256: pristine.sha256, packageSha256: file.sha256 });
    }
    if (problems.length > 0) return failure('PACKAGE_TAMPERED', 'The generated package failed verification.', problems);
    return { ok: true, manifest, files: [...targets.values()], sources };
  }

  /** Combined-only packages must agree exactly with DsrDataTool's patch-manifest.json. */
  private async verifyPatchManifest(packagePath: string, gameFiles: ActivationJournal['files'], sources: Map<string, string>): Promise<string[]> {
    if (!sources.has('patch-manifest.json')) return ['patch-manifest.json is missing from the package manifest.'];
    const patch = await readJson<Record<string, unknown>>(path.join(path.resolve(packagePath), 'patch-manifest.json'));
    if (!patch) return ['patch-manifest.json is unreadable.'];
    const entries: unknown[] = [];
    for (const name of patchListKeys) {
      if (Array.isArray(patch[name])) entries.push(...patch[name] as unknown[]);
      else return [`patch-manifest.json ${name} must be an array.`];
    }
    for (const name of patchSingleKeys) if (patch[name] !== null && patch[name] !== undefined) entries.push(patch[name]);
    const byPath = new Map(gameFiles.map((file) => [key(file.path), file]));
    const listed = new Set<string>();
    const problems: string[] = [];
    for (const entry of entries) {
      const item = entry as Record<string, unknown>;
      if (typeof item?.source !== 'string' || typeof item.output !== 'string'
        || typeof item.sourceSha256 !== 'string' || typeof item.outputSha256 !== 'string') {
        problems.push('patch-manifest.json contains a malformed entry.');
        continue;
      }
      const target = byPath.get(key(item.source));
      if (item.output !== `mod/${item.source}` || !target) {
        problems.push(`patch-manifest.json entry does not match the package: ${item.source}`);
        continue;
      }
      if (item.outputSha256.toLowerCase() !== target.packageSha256) problems.push(`Output hash mismatch: ${item.source}`);
      if (item.sourceSha256.toLowerCase() !== target.pristineSha256) problems.push(`Source hash differs from the snapshot: ${item.source}`);
      listed.add(key(item.source));
    }
    for (const file of gameFiles) if (!listed.has(key(file.path))) problems.push(`mod file not in patch-manifest.json: ${file.path}`);
    return problems;
  }

  private async replaceVerified(from: string, to: string, sha256: string): Promise<void> {
    const temporary = `${to}${tempSuffix}`;
    await mkdir(path.dirname(to), { recursive: true });
    await this.copy(from, temporary);
    if (await sha256File(temporary) !== sha256) {
      await rm(temporary, { force: true });
      throw new Error(`Copy verification failed: ${to}`);
    }
    await rename(temporary, to);
  }

  /** Applies a validated generated package to a vanilla installation. */
  async activate(gamePath: string, packagePath: string): Promise<AuthorityResult<{ journal: ActivationJournal }>> {
    return this.withLock(gamePath, async () => {
      if (await this.isGameRunning()) return failure('GAME_RUNNING', 'Close Dark Souls Remastered before activating.');
      const root = path.resolve(gamePath);
      if (contained(root, packagePath)) return failure('PACKAGE_INVALID', 'The package must be outside the game installation.');
      const { report, snapshot } = await this.inspect(root);
      if (!snapshot) return failure(report.state === 'invalid' ? 'GAME_INVALID' : 'PRISTINE_IMPORT_REQUIRED',
        'Import a pristine snapshot before activation.', report.problems);
      if (report.state !== 'vanilla') {
        return failure('GAME_STATE_NOT_VANILLA',
          `Activation requires a vanilla installation; current state is '${report.state}'. Restore first.`, report.problems);
      }
      const read = await this.readPackage(packagePath, snapshot);
      if (!read.ok) return read;
      if (read.manifest.gameTreeRoot === 'mod') {
        const problems = await this.verifyPatchManifest(packagePath, read.files, read.sources);
        if (problems.length > 0) return failure('PACKAGE_TAMPERED', 'The package disagrees with patch-manifest.json.', problems);
      }
      const files = read.files
        .filter((file) => file.packageSha256 !== file.pristineSha256)
        .sort((a, b) => a.path.localeCompare(b.path, 'en'));
      if (files.length === 0) return failure('PACKAGE_EMPTY', 'The package changes no game files.');
      const paths = this.paths(root);
      const damaged = await this.verifyPristineCopies(paths.pristine,
        files.map((file) => ({ path: file.path, size: 0, sha256: file.pristineSha256 })));
      if (damaged.length > 0) return failure('PRISTINE_COPY_DAMAGED', 'The pristine snapshot cannot restore these files.', damaged);

      const journal: ActivationJournal = {
        schemaVersion: 1, status: 'activating', snapshotId: snapshot.snapshotId,
        packagePath: path.resolve(packagePath), masterSeed: read.manifest.masterSeed,
        startedAt: new Date().toISOString(), completedAt: null, files,
      };
      // The journal is durable before the first game write.
      await writeJsonAtomic(paths.journal, journal);
      const prefix = read.manifest.gameTreeRoot === '' ? '' : `${read.manifest.gameTreeRoot}/`;
      const packageNames = new Map([...read.sources.keys()].map((name) => [key(name), name]));
      try {
        for (const file of files) {
          const packageName = packageNames.get(key(`${prefix}${file.path}`))!;
          await this.replaceVerified(toNative(path.resolve(packagePath), packageName), toNative(root, file.path), file.packageSha256);
        }
      } catch (error) {
        const rollback = await this.restoreFiles(root, snapshot, files.map((file) => file.path));
        if (rollback.length === 0) {
          await rm(paths.journal, { force: true });
          return failure('ACTIVATION_FAILED', `Activation failed and was rolled back: ${String(error)}`);
        }
        return failure('ACTIVATION_INTERRUPTED',
          `Activation failed and rollback was incomplete: ${String(error)}. Run restore.`, rollback);
      }
      journal.status = 'active';
      journal.completedAt = new Date().toISOString();
      await writeJsonAtomic(paths.journal, journal);
      return { ok: true, journal };
    });
  }

  /** Copies pristine files back where the game differs; returns problems. */
  private async restoreFiles(root: string, snapshot: PristineSnapshot, relatives: string[]): Promise<string[]> {
    const pristine = this.paths(root).pristine;
    const byKey = new Map(snapshot.files.map((file) => [key(file.path), file]));
    const problems: string[] = [];
    for (const relative of relatives) {
      const file = byKey.get(key(relative));
      if (!file) { problems.push(`Not in snapshot: ${relative}`); continue; }
      const target = toNative(root, file.path);
      try {
        if (await tryHash(target) !== file.sha256) {
          await this.replaceVerified(toNative(pristine, file.path), target, file.sha256);
        }
      } catch (error) { problems.push(`${file.path}: ${String(error)}`); }
    }
    for (const relative of await listManaged(root, snapshot.roots)) {
      if (relative.endsWith(tempSuffix)) await rm(toNative(root, relative), { force: true });
    }
    return problems;
  }

  /**
   * Restores every snapshot file to its pristine content. Unrecorded changes
   * are refused unless the caller explicitly accepts overwriting them.
   */
  async restore(gamePath: string, options: { overwriteUnrecordedChanges?: boolean } = {})
    : Promise<AuthorityResult<{ restored: number; state: GameStateReport }>> {
    return this.withLock(gamePath, async () => {
      if (await this.isGameRunning()) return failure('GAME_RUNNING', 'Close Dark Souls Remastered before restoring.');
      const root = path.resolve(gamePath);
      const { report, snapshot, current } = await this.inspect(root);
      if (report.state === 'invalid') return failure('GAME_INVALID', 'Not a DSR installation.', report.problems);
      if (!snapshot) return failure('PRISTINE_IMPORT_REQUIRED', 'No pristine snapshot exists for this installation.');
      if (report.state === 'version-changed') {
        return failure('SNAPSHOT_STALE', 'The game executable changed since the snapshot; restoring old data could break the game. Verify the game files in Steam.', report.problems);
      }
      if (report.state === 'vanilla') return { ok: true, restored: 0, state: report };
      if (report.state === 'modified' && !options.overwriteUnrecordedChanges) {
        return failure('GAME_STATE_UNKNOWN',
          'The installation has changes this application did not make. Confirm overwriting them to restore.', report.problems);
      }
      const paths = this.paths(root);
      const differingFiles = snapshot.files.filter((file) => current.get(key(file.path)) !== file.sha256);
      const differing = differingFiles.map((file) => file.path);
      const damaged = await this.verifyPristineCopies(paths.pristine, differingFiles);
      if (damaged.length > 0) return failure('PRISTINE_COPY_DAMAGED', 'The pristine snapshot is damaged; nothing was restored.', damaged);
      const problems = await this.restoreFiles(root, snapshot, differing);
      if (problems.length > 0) return failure('RESTORE_INCOMPLETE', 'Some files could not be restored. Run restore again.', problems);
      if (report.state === 'modified') {
        // Confirmed overwrite: files other tools added inside managed roots
        // (e.g. legacy .bak chains) are removed so the roots match the snapshot.
        const known = new Set(snapshot.files.map((file) => key(file.path)));
        for (const relative of await listManaged(root, snapshot.roots)) {
          if (!known.has(key(relative))) await rm(toNative(root, relative), { force: true });
        }
      }
      await rm(paths.journal, { force: true });
      const after = await this.detectState(root);
      if (after.state !== 'vanilla') {
        return failure('RESTORE_INCOMPLETE', `Restore finished but the state is '${after.state}'.`, after.problems);
      }
      return { ok: true, restored: differing.length, state: after };
    });
  }
}
