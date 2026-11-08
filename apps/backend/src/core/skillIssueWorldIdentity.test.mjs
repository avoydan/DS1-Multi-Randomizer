import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { cp, mkdir, mkdtemp, readdir, readFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { GameStateAuthority, executableName, managedRoots } from '../../dist/core/gameState.js';
import { generateRun } from '../../dist/core/generationPipeline.js';
import { prepareRun } from '../../dist/core/seededRun.js';

// Skill Issue Mode is a local starting-loadout overlay outside the Seed Code. With the real helpers and a clean DSR copy
// (DS1TR_GOLDEN_GAME_SOURCE plus the helper variables of the golden recorder) the same Seed Code must make the same randomized decisions
// with the mode off and on. The package differs only by the overlay: the GameParam binder (CharaInitParam rows and the grant's item lots),
// common.emevd (the one-time grant event and its initializer) and the manifest that records the overlay.
// FogMod's route listing in the host log exists only in internal QA (docs/INTERNAL_QA.md); the test compares it as the record of Fog's decisions.
process.env.DS1TR_INTERNAL_QA = '1';
const source = process.env.DS1TR_GOLDEN_GAME_SOURCE;
const smokeCode = 'DS1TR1-06NWCQQ3-K2GXEE39-K5WRJEDT-CNKBNHDS-Z7CTKEJ6-0S36D1M6-0S3A7NEF-D3TK6PQX-HHB3ZGX2-Z80G12X8-GYBG';

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

test('Skill Issue Mode on and off: same Seed Code, same randomized decisions, only the overlay files differ', { skip: !source }, async () => {
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-skill-identity-'));
  try {
    const game = path.join(temp, 'game');
    await cp(path.join(source, executableName), path.join(game, executableName));
    for (const managed of managedRoots) await cp(path.join(source, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
    const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
    assert.equal((await authority.importPristine(game)).ok, true);

    const off = prepareRun({ gamePath: game, seedCode: smokeCode });
    const on = prepareRun({ gamePath: game, seedCode: smokeCode, localGameplay: { skillIssueMode: true } });
    assert.equal(off.ok && on.ok, true);
    assert.equal(on.seedCode, off.seedCode, 'the mode is outside the Seed Code');
    assert.deepEqual(on.config, off.config, 'the mode is outside the randomizer settings');
    assert.equal(on.profile.id, off.profile.id);

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

    assert.equal(normal.result.manifest.ownerQaLoadout, undefined, 'normal mode records no overlay');
    assert.equal(loadout.result.manifest.ownerQaLoadout, true);
    assert.deepEqual(loadout.result.manifest.attempts.map((attempt) => attempt.fogSeed), normal.result.manifest.attempts.map((attempt) => attempt.fogSeed));
    assert.deepEqual([...loadout.files.keys()].sort(), [...normal.files.keys()].sort(), 'the same files are generated');
    const different = [...normal.files].filter(([name, hash]) => loadout.files.get(name) !== hash).map(([name]) => name).sort();
    assert.deepEqual(different, ['event/common.emevd.dcx', 'param/GameParam/GameParam.parambnd.dcx', 'run-manifest.json'],
      'only GameParam (loadout rows and grant lots), common.emevd (the grant event) and the manifest that records the overlay differ');

    // Every randomized decision is the same: the combined engine's whole output tree (items, enemies, bosses: the mod tree and the item locations) is byte-identical
    // between the two workspaces, and so is Fog's graph log.
    const combinedTree = async (workspace) => {
      const [directory] = (await readdir(path.join(workspace, 'combined-0'), { withFileTypes: true })).filter((entry) => entry.isDirectory() && entry.name.startsWith('seed_'));
      const tree = await hashTree(path.join(workspace, 'combined-0', directory.name));
      return new Map([...tree].filter(([name]) => name.startsWith('mod/') || name === 'cheat-locations.txt'));
    };
    const offCombined = await combinedTree(path.join(temp, 'off'));
    assert.ok(offCombined.size > 30 && offCombined.has('cheat-locations.txt'), 'the combined engine output is present');
    assert.deepEqual([...await combinedTree(path.join(temp, 'on'))], [...offCombined], 'item, enemy and boss decisions are identical');
    const graphLog = async (workspace) => (await readFile(path.join(workspace, 'fog-0-0/foghost.log'), 'utf8')).split(/\r?\n/).filter((line) => !line.startsWith('Writing ') && !line.includes(workspace)).join('\n');
    const offLog = await graphLog(path.join(temp, 'off'));
    assert.ok(offLog.includes('From '), 'the Fog graph log is present');
    assert.equal(await graphLog(path.join(temp, 'on')), offLog, 'Fog decisions are identical');

    // The manifest differs only by the overlay's own record: the flag, its validation line and the GameParam file entry.
    const manifests = await Promise.all(['off', 'on'].map(async (label) => JSON.parse(await readFile(path.join(label === 'off' ? normal.result.packagePath : loadout.result.packagePath, 'run-manifest.json'), 'utf8'))));
    const [offManifest, onManifest] = manifests;
    const differing = Object.keys({ ...offManifest, ...onManifest }).filter((key) => JSON.stringify(offManifest[key]) !== JSON.stringify(onManifest[key])).sort();
    assert.ok(differing.every((key) => ['files', 'ownerQaLoadout', 'validation'].includes(key)), `unexpected manifest differences: ${differing.join(', ')}`);
    assert.deepEqual(onManifest.validation.slice(0, offManifest.validation.length), offManifest.validation);
    assert.equal(onManifest.validation.length, offManifest.validation.length + 1);
    const changedEntries = onManifest.files.filter((entry, index) => JSON.stringify(entry) !== JSON.stringify(offManifest.files[index])).map((entry) => entry.path);
    assert.ok(changedEntries.every((entry) => ['param/GameParam/GameParam.parambnd.dcx', 'event/common.emevd.dcx'].includes(entry)), `manifest file entries that differ: ${changedEntries.join(', ')}`);
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});
