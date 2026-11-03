import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { cp, mkdir, mkdtemp, readdir, readFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { inflateSync } from 'node:zlib';
import { GameStateAuthority, executableName, managedRoots } from '../../dist/core/gameState.js';
import { generateRun } from '../../dist/core/generationPipeline.js';
import { prepareRun } from '../../dist/core/seededRun.js';
import { decodeSeedCode, encodeSeedCode } from '../../dist/core/seedCode.js';

// The Skill Issue Mode one-time grant: a dedicated common event awards nine dedicated item lots once, behind a dedicated flag, only in the overlay.
// The structural facts are checked by the vendored tool itself on every run (it re-reads the written EMEVD and ItemLotParam and compares every other
// event and row); these tests pin the allow-list, the identifiers and their collision evidence, and the package-level behavior.
const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../');
const toolSource = () => readFile(path.join(repositoryRoot, 'engines/combined/vendor/tools/DsrDataTool/Program.cs'), 'utf8');
const smokeCode = 'DS1TR1-06NWCQQ3-K2GXEE39-K5WRJEDT-CNKBNHDS-Z7CTKEJ6-0S36D1M6-0S3A7NEF-D3TK6PQX-HHB3ZGX2-Z80G12X8-GYBG';
const qaRoot = process.env.DS1TR_QA_DSR_ROOT;
const gameSource = process.env.DS1TR_GOLDEN_GAME_SOURCE;

test('grant allow-list: exactly the nine requested items with the requested quantities, one dedicated lot each, nothing that opens the world', async () => {
  const source = await toolSource();
  const start = source.indexOf('var grantRequests = new');
  const block = source.slice(start, source.indexOf('};', start));
  const entries = [...block.matchAll(/\((0x[0-9A-F_]+), ([\d_]+), (\d+), "([^"]+)"\)/gu)].map((match) => [match[1], Number(match[2].replaceAll('_', '')), Number(match[3]), match[4]]);
  assert.deepEqual(entries, [
    ['0x1000_0000', 190000, 1, 'Sunlight Maggot'], ['0x1000_0000', 600000, 1, 'Mask of the Mother'], ['0x1000_0000', 590000, 1, 'Mask of the Father'],
    ['0x1000_0000', 610000, 1, 'Mask of the Child'], ['0x2000_0000', 128, 1, 'Dark Wood Grain Ring'], ['0x2000_0000', 100, 1, "Havel's Ring"],
    ['0x2000_0000', 125, 1, 'Rusted Iron Ring'], ['0x2000_0000', 123, 1, 'Slumbering Dragoncrest Ring'], ['0x4000_0000', 370, 99, 'Prism Stone'],
  ]);
  const forbidden = ['Key', 'Soul', 'Lordvessel', 'Orange Charred', 'Covenant', 'Crest', 'Doll', 'Pendant', 'Seal', 'Bequeathed'];
  for (const [, , , name] of entries) for (const word of forbidden) assert.ok(!name.includes(word), `${name} must not look like a progression item`);
  // Names and rows are resolved from the game data, the carry limit comes from the game (maxNum), and every lot is one guaranteed item (point 100, no pickup flag).
  assert.match(source, /RequireName\(names\.Armor, id, name\); RequireRow\(protectorParam, id, name\)/u);
  assert.match(source, /RequireName\(names\.Accessories, id, name\); RequireRow\(accessoryParam, id, name\)/u);
  assert.match(source, /quantity = Math\.Min\(requested, GetCellInt\(grantGoodsRow, "maxNum", 0\)\)/u);
  assert.match(source, /\("lotItemBasePoint01", 100\)/u);
  assert.match(source, /\("getItemFlagId", -1\)/u);
});

test('grant event: wait, end if the dedicated flag is on, nine awards, set the flag; initialized from the end of common event 0; flag and event ID collision-checked', async () => {
  const source = await toolSource();
  assert.match(source, /const int grantEventId = 8_990;/u);
  assert.match(source, /const int grantFlag = 6_931;/u);
  assert.match(source, /const int grantFirstLot = 90_010;/u);
  assert.match(source, /const int grantLotStep = 10;/u);
  // The exact instruction order: the flag is tested before any award and set after the last one, with nothing in between.
  const order = [
    'new EMEVD.Instruction(1001, 0, new object[] { 1.0f })',
    'new EMEVD.Instruction(1003, 2, new object[] { (byte)0, (byte)1, (byte)0, flag })',
    'new EMEVD.Instruction(2003, 36, new object[] { lotId })',
    'new EMEVD.Instruction(2003, 2, new object[] { flag, (byte)1 })',
  ].map((fragment) => source.indexOf(fragment));
  assert.ok(order.every((index) => index > 0) && order.every((index, position) => position === 0 || index > order[position - 1]), 'wait, end-if-flag, awards, set-flag in that order');
  assert.match(source, /new EMEVD\.Event\(eventId, EMEVD\.Event\.RestBehaviorType\.Default\)/u);
  assert.match(source, /constructor\.Instructions\.Add\(new EMEVD\.Instruction\(2000, 0, new object\[\] \{ 0, eventId, 0 \}\)\)/u);
  assert.match(source, /Common event 0 ends unconditionally/u);
  // Collision evidence at run time: event files (flag and event ID as an event ID or any int32 in any argument), every GameParam cell, and the lots and their neighbours.
  assert.match(source, /already referenced by/u);
  assert.match(source, /is a row ID of/u);
  assert.match(source, /\(or a neighbour, \{id\}\) is already used/u);
  assert.match(source, /is already used in common\.emevd/u);
  // No existing gameplay flag: the grant touches nothing but its own flag, lots and event.
  assert.doesNotMatch(source.slice(source.indexOf('static string AppendSkillIssueGrantEvent'), source.indexOf('static void UpdateSkillIssuePatchManifest')), /11\d{6}|0x00B4/u);
});

test('the grant flag and event ID are unused in every vanilla event file', { skip: !qaRoot }, async () => {
  const flag = Buffer.alloc(4); flag.writeInt32LE(6931);
  const eventId = Buffer.alloc(4); eventId.writeInt32LE(8990);
  const directory = path.join(qaRoot, 'event');
  let scanned = 0;
  for (const name of (await readdir(directory)).filter((file) => file.endsWith('.emevd.dcx'))) {
    const bytes = await readFile(path.join(directory, name));
    assert.equal(bytes.subarray(0, 4).toString('latin1'), 'DCX\0');
    const data = inflateSync(bytes.subarray(0x4c)); // DS1 DCX (DFLT): the zlib stream follows the 0x4C-byte header
    assert.equal(data.subarray(0, 4).toString('latin1'), 'EVD\0');
    assert.equal(data.indexOf(flag), -1, `flag 6931 appears in ${name}`);
    assert.equal(data.indexOf(eventId), -1, `event ID 8990 appears in ${name}`);
    scanned++;
  }
  assert.ok(scanned >= 18);
});

async function hashTree(root) {
  const files = new Map();
  async function walk(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) await walk(full);
      else files.set(path.relative(root, full).split(path.sep).join('/'), createHash('sha256').update(await readFile(full)).digest('hex'));
    }
  }
  await walk(root);
  return files;
}

test('items-only ("mod" layout) with the mode off and on: the grant exists only when on, activation accepts both, restore returns the game', { skip: !gameSource }, async () => {
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-skill-grant-'));
  try {
    const game = path.join(temp, 'game');
    await cp(path.join(gameSource, executableName), path.join(game, executableName));
    for (const managed of managedRoots) await cp(path.join(gameSource, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
    const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
    assert.equal((await authority.importPristine(game)).ok, true);

    const base = decodeSeedCode(smokeCode).envelope;
    const settings = structuredClone(base.settings);
    settings.fog.enabled = false;
    settings.enemies.enabled = false;
    const code = encodeSeedCode({ ...base, settings });
    const off = prepareRun({ gamePath: game, seedCode: code });
    const on = prepareRun({ gamePath: game, seedCode: code, localGameplay: { skillIssueMode: true } });
    assert.equal(off.ok && on.ok, true);
    assert.equal(on.seedCode, off.seedCode);
    assert.deepEqual(on.config, off.config);

    const run = async (label, prepared, skillIssueMode) => {
      const workspacePath = path.join(temp, label);
      await mkdir(workspacePath, { recursive: true });
      const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode, skillIssueMode }, () => {},
        { getPristineSource: (gamePath) => authority.getPristineSource(gamePath) });
      assert.equal(result.ok, true, result.ok ? '' : `${result.code}: ${result.message}`);
      return { result, files: await hashTree(result.packagePath) };
    };
    const normal = await run('off', off, false);
    const loadout = await run('on', on, true);
    assert.equal(normal.result.manifest.gameTreeRoot, 'mod');

    // Mode off: no event file, no overlay record. Mode on: the grant's event file is the only added file; the loadout and lots are in GameParam.
    assert.ok(![...normal.files.keys()].some((name) => name.includes('emevd')), 'the normal package has no event file at all');
    assert.deepEqual([...loadout.files.keys()].filter((name) => !normal.files.has(name)), ['mod/event/common.emevd.dcx']);
    const changedFiles = [...normal.files].filter(([name, hash]) => loadout.files.has(name) && loadout.files.get(name) !== hash).map(([name]) => name).sort();
    // config.json and randomizer.json record the workspace path of their run; apart from that path they are identical.
    assert.deepEqual(changedFiles, ['config.json', 'mod/param/GameParam/GameParam.parambnd.dcx', 'patch-manifest.json', 'randomizer.json', 'run-manifest.json'].filter((name) => changedFiles.includes(name)));
    for (const name of ['config.json', 'randomizer.json']) {
      const normalize = async (result, label) => (await readFile(path.join(result.result.packagePath, name), 'utf8')).split(path.join(temp, label).replaceAll('\\', '\\\\')).join('<workspace>').split(path.join(temp, label)).join('<workspace>');
      assert.equal(await normalize(loadout, 'on'), await normalize(normal, 'off'), `${name} differs only by the workspace path`);
    }
    assert.ok(['mod/param/GameParam/GameParam.parambnd.dcx', 'patch-manifest.json', 'run-manifest.json'].every((name) => changedFiles.includes(name)));
    assert.equal(normal.result.manifest.ownerQaLoadout, undefined);
    assert.equal(loadout.result.manifest.ownerQaLoadout, true);
    assert.deepEqual(loadout.result.manifest.attempts, normal.result.manifest.attempts);

    // The patch manifest of the overlay package lists the files it changed with their final hashes (it did not before: Skill Issue with Fog off failed to activate).
    const patch = JSON.parse(await readFile(path.join(loadout.result.packagePath, 'patch-manifest.json'), 'utf8'));
    assert.equal(patch.gameParam.outputSha256, loadout.files.get('mod/param/GameParam/GameParam.parambnd.dcx'));
    const common = patch.events.find((entry) => entry.source === 'event/common.emevd.dcx');
    assert.equal(common.output, 'mod/event/common.emevd.dcx');
    assert.equal(common.outputSha256, loadout.files.get('mod/event/common.emevd.dcx'));

    // Real activation of both, with a restore in between: nothing of the grant is left in the game after the restore.
    const offActive = await authority.activate(game, normal.result.packagePath);
    assert.equal(offActive.ok, true, offActive.ok ? '' : offActive.code);
    assert.ok(!offActive.journal.files.some((file) => file.path === 'event/common.emevd.dcx'), 'the normal activation does not touch common.emevd');
    assert.equal((await authority.restore(game)).ok, true);
    const onActive = await authority.activate(game, loadout.result.packagePath);
    assert.equal(onActive.ok, true, onActive.ok ? '' : onActive.code);
    assert.ok(onActive.journal.files.some((file) => file.path === 'event/common.emevd.dcx'));
    assert.ok(onActive.journal.files.some((file) => file.path === 'param/GameParam/GameParam.parambnd.dcx'));
    assert.equal((await authority.restore(game)).ok, true);
    const restoredCommon = createHash('sha256').update(await readFile(path.join(game, 'event', 'common.emevd.dcx'))).digest('hex');
    const pristineCommon = createHash('sha256').update(await readFile(path.join(gameSource, 'event', 'common.emevd.dcx'))).digest('hex');
    assert.equal(restoredCommon, pristineCommon);
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});
