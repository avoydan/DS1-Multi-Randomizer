// Fog runtime parity harness (docs/V7_UPSTREAM_PARITY.md, "Fog runtime parity").
//
// For one master seed the same Fog-only settings are generated under two generation profiles and every file of the package is compared:
//   A = ds1tr-gen-v2  the frozen ORIGINAL generator: FogHost 0.5.0 running FogMod on its real upstream `dist` (needs the legacy data cache)
//   B = ds1tr-gen-v7  FogMod on the bundled upstream fog.txt and the inputs rebuilt locally from the installed game
// Both profiles use seed derivation v1, so they get the same Fog seed and options and the connector builds the same layout; the only difference between the two runs is where the
// generator's inputs come from. A file is "identical" when its bytes are equal; "identical after DCX" when only the compression layer of a DCX container differs.
//
// usage: DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> node apps/backend/scripts/fogRuntimeParity.mjs <outDir> [seeds=parity-1,parity-2,parity-3] [--wide]
//   --wide also turns on the PvP, Lordvessel and start options, which exercises more of the writer.
import { cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { inflateSync } from 'node:zlib';
import { pathToFileURL } from 'node:url';

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/u, '$1'));
const dist = (p) => import(pathToFileURL(path.join(here, '..', 'dist', p)).href);
const { GameStateAuthority, executableName, managedRoots } = await dist('core/gameState.js');
const { generateRun } = await dist('core/generationPipeline.js');
const { prepareRun } = await dist('core/seededRun.js');
const { buildSettings, seedCodeForProfile } = await import(pathToFileURL(path.join(here, 'goldenCompatibility.mjs')).href);

const PROFILE_A = 'ds1tr-gen-v2';
const PROFILE_B = 'ds1tr-gen-v7';
const [outDir, seedList = 'parity-1,parity-2,parity-3'] = process.argv.slice(2).filter((argument) => !argument.startsWith('--'));
const wide = process.argv.includes('--wide');
const gameSource = process.env.DS1TR_GOLDEN_GAME_SOURCE;
if (!outDir || !gameSource) { console.error('usage: DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> node fogRuntimeParity.mjs <outDir> [seeds] [--wide]'); process.exit(2); }

const root = path.resolve(outDir);
await mkdir(root, { recursive: true });
const game = path.join(root, 'game');
await cp(path.join(gameSource, executableName), path.join(game, executableName));
for (const managed of managedRoots) await cp(path.join(gameSource, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
const authority = new GameStateAuthority({ stateRoot: path.join(root, 'state'), isGameRunning: async () => false });
if (!(await authority.importPristine(game)).ok) throw new Error('import pristine failed');

const settings = buildSettings({ items: { enabled: false }, enemies: { enabled: false },
  ...(wide ? { fog: { majorPvpFogs: true, minorPvpFogs: true, lordvesselGates: true, disconnectedFogs: false } } : {}) });

/** The zlib stream of a DCX_DFLT container starts at 0x4C; the decompressed bytes are what the game reads. */
function dcxContent(bytes) {
  if (bytes.length < 0x50 || bytes.toString('latin1', 0, 4) !== 'DCX\0') return null;
  try { return inflateSync(bytes.subarray(0x4c)); } catch { return null; }
}

async function generate(profile, masterSeed) {
  const seedCode = seedCodeForProfile(profile, masterSeed, settings);
  const prepared = prepareRun({ gamePath: game, seedCode });
  if (!prepared.ok) throw new Error(`${profile}: ${prepared.code}`);
  const workspacePath = path.join(root, `ws-${profile}-${masterSeed}`);
  await mkdir(workspacePath, { recursive: true });
  const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode }, () => {}, { getPristineSource: (g) => authority.getPristineSource(g) });
  if (!result.ok) throw new Error(`${profile}/${masterSeed}: ${result.code}: ${result.message}`);
  const files = new Map();
  for (const file of result.manifest.files) files.set(file.path, await readFile(path.join(result.packagePath, ...file.path.split('/'))));
  return { attempts: result.manifest.attempts, files };
}

const category = (p) => p.startsWith('event/') ? 'events' : p.startsWith('script/talk/') ? 'talk / ESD' : p.startsWith('map/') ? 'maps' : p.startsWith('param/') ? 'params' : p.startsWith('msg/') ? 'messages' : 'other';
const totals = {};
const lines = [];
for (const masterSeed of seedList.split(',')) {
  const a = await generate(PROFILE_A, masterSeed);
  const b = await generate(PROFILE_B, masterSeed);
  const sameLayout = JSON.stringify(a.attempts.map((x) => [x.fogSeed, x.result])) === JSON.stringify(b.attempts.map((x) => [x.fogSeed, x.result]));
  lines.push(`seed ${masterSeed}${wide ? ' (wide options)' : ''}: Fog seed A ${a.attempts.at(-1)?.fogSeed} B ${b.attempts.at(-1)?.fogSeed}, same attempts ${sameLayout}`);
  for (const name of [...new Set([...a.files.keys(), ...b.files.keys()])].sort()) {
    const c = category(name);
    totals[c] ??= { files: 0, identical: 0, identicalAfterDcx: 0, different: 0, differing: [] };
    const row = totals[c];
    row.files += 1;
    const x = a.files.get(name); const y = b.files.get(name);
    if (x && y && x.equals(y)) { row.identical += 1; row.identicalAfterDcx += 1; continue; }
    const dx = x && dcxContent(x); const dy = y && dcxContent(y);
    if (dx && dy && dx.equals(dy)) { row.identicalAfterDcx += 1; continue; }
    row.different += 1;
    row.differing.push(`${masterSeed}:${name}`);
  }
}
let allFiles = 0; let allIdentical = 0; let allAfterDcx = 0;
for (const [c, row] of Object.entries(totals)) {
  allFiles += row.files; allIdentical += row.identical; allAfterDcx += row.identicalAfterDcx;
  lines.push(`${c.padEnd(12)} files ${String(row.files).padStart(3)}  byte-identical ${String(row.identical).padStart(3)}  identical after DCX decompression ${String(row.identicalAfterDcx).padStart(3)}  different ${row.different}${row.differing.length ? '  [' + row.differing.slice(0, 6).join(', ') + ']' : ''}`);
}
lines.push(`TOTAL        files ${allFiles}  byte-identical ${allIdentical}  identical after DCX decompression ${allAfterDcx}  different ${allFiles - allAfterDcx}`);
console.log(lines.join('\n'));
await writeFile(path.join(root, `fog-runtime-parity${wide ? '-wide' : ''}.txt`), lines.join('\n') + '\n');
