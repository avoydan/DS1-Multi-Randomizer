// Negative control of SmokeVerify dlc-untouched (docs/DLC_AUDIT.md): a world with the DLC ON and shops + gifts randomized MUST be reported as changing DLC shop rows (this is the gap the DLC-off exclusion closes). Needs DS1TR_GOLDEN_GAME_SOURCE and the built helpers.
import { cp, mkdir, mkdtemp, readdir, readFile, rm, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { dlcContentRows } from '../dist/adapters/dlcExclusion.js';
import { buildSettings } from './goldenCompatibility.mjs';

const source = process.env.DS1TR_GOLDEN_GAME_SOURCE;
const verifier = process.env.DS1TR_SMOKE_VERIFY_EXE ?? fileURLToPath(new URL('../../../tools/smoke-verify/bin/Release/net8.0/SmokeVerify.exe', import.meta.url));
const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-control-'));
try {
  const game = path.join(temp, 'game');
  await cp(path.join(source, executableName), path.join(game, executableName));
  for (const m of managedRoots) await cp(path.join(source, ...m.split('/')), path.join(game, ...m.split('/')), { recursive: true });
  const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
  await authority.importPristine(game);
  const prepared = prepareRun({ gamePath: game, masterSeed: 'control-1', ...buildSettings({ fog: { enabled: false }, enemies: { enabled: false }, items: { randomizeGifts: true, randomizeShops: true, randomizeEnemyDrops: true } }) });
  const workspacePath = path.join(temp, 'run'); await mkdir(workspacePath, { recursive: true });
  const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode }, () => {}, { getPristineSource: (g) => authority.getPristineSource(g) });
  if (!result.ok) throw new Error(result.message);
  const attempts = (await readdir(workspacePath)).filter((n) => /^combined-\d+$/u.test(n));
  const base = path.join(workspacePath, attempts[0]);
  const found = []; for (const f of [path.join(base, 'dsr-catalog.json'), ...(await readdir(base)).map((n) => path.join(base, n, 'dsr-catalog.json'))]) { if (await readFile(f).then(() => true, () => false)) found.push(f); }
  const rowsFile = path.join(temp, 'rows.json');
  await writeFile(rowsFile, JSON.stringify(dlcContentRows(JSON.parse(await readFile(found[0], 'utf8')))));
  const tree = await readdir(path.join(result.packagePath, 'param')).then(() => result.packagePath, () => path.join(result.packagePath, 'mod'));
  const out = spawnSync(verifier, ['dlc-untouched', tree, path.join(source, 'paramdef', 'paramdef.paramdefbnd.dcx'), found[0], source, rowsFile], { encoding: 'utf8' });
  const summary = JSON.parse(out.stdout.trim().split(/\r?\n/u).pop());
  console.log('exit', out.status, 'shop rows compared', summary.shopRowsChecked, 'problems', summary.problems.length, summary.problems.slice(0, 3));
  console.log(out.status === 1 && summary.problems.some((p) => p.startsWith('ShopLineupParam')) ? 'CONTROL OK: the verifier reports a DLC-on world as changing DLC shop rows' : 'CONTROL FAILED');
} finally { await rm(temp, { recursive: true, force: true }); }
