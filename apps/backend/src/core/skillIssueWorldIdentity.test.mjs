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
// (DS1TR_GOLDEN_GAME_SOURCE plus the helper variables of the golden recorder) the same Seed Code must generate the same world with
// the mode off and on: every package file is byte-identical except the GameParam binder that holds CharaInitParam.
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

test('Skill Issue Mode on and off: same Seed Code, same world, only the GameParam starting loadout differs', { skip: !source }, async () => {
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
    assert.deepEqual(different, ['param/GameParam/GameParam.parambnd.dcx', 'run-manifest.json'], 'only the GameParam binder (CharaInitParam) and the manifest that records the overlay differ');

    // The manifest differs only by the overlay's own record: the flag, its validation line and the GameParam file entry.
    const manifests = await Promise.all(['off', 'on'].map(async (label) => JSON.parse(await readFile(path.join(label === 'off' ? normal.result.packagePath : loadout.result.packagePath, 'run-manifest.json'), 'utf8'))));
    const [offManifest, onManifest] = manifests;
    const differing = Object.keys({ ...offManifest, ...onManifest }).filter((key) => JSON.stringify(offManifest[key]) !== JSON.stringify(onManifest[key])).sort();
    assert.ok(differing.every((key) => ['files', 'ownerQaLoadout', 'validation'].includes(key)), `unexpected manifest differences: ${differing.join(', ')}`);
    assert.deepEqual(onManifest.validation.slice(0, offManifest.validation.length), offManifest.validation);
    assert.equal(onManifest.validation.length, offManifest.validation.length + 1);
    const changedEntries = onManifest.files.filter((entry, index) => JSON.stringify(entry) !== JSON.stringify(offManifest.files[index])).map((entry) => entry.path);
    assert.ok(changedEntries.every((entry) => entry === 'param/GameParam/GameParam.parambnd.dcx'), `manifest file entries that differ: ${changedEntries.join(', ')}`);
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});
