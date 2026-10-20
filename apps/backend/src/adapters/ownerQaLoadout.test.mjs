import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { PassThrough } from 'node:stream';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { applyOwnerQaLoadout, ownerQaLoadoutEnabled } from '../../dist/adapters/ownerQaLoadout.js';

function childProcess() {
  const child = new EventEmitter();
  child.stdout = new PassThrough();
  child.stderr = new PassThrough();
  return child;
}

test('owner QA mode is opt-in and accepts only the explicit local flag', () => {
  assert.equal(ownerQaLoadoutEnabled({}), false);
  assert.equal(ownerQaLoadoutEnabled({ DS1TR_OWNER_QA_LOADOUT: '0' }), false);
  assert.equal(ownerQaLoadoutEnabled({ DS1TR_OWNER_QA_LOADOUT: 'true' }), false);
  assert.equal(ownerQaLoadoutEnabled({ DS1TR_OWNER_QA_LOADOUT: '1' }), true);
});

test('vendored QA overlay pins the verified equipment identities and exact equipped slots', async () => {
  const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../');
  const source = await readFile(path.join(repositoryRoot, 'engines/combined/vendor/tools/DsrDataTool/Program.cs'), 'utf8');
  for (const [name, id] of [
    ['blackKnightHalberd', '1_105_000'], ['grassCrestShield', '1_453_000'],
    ['havelsGreatshield', '1_505_000'], ['ringOfFavorAndProtection', '143'],
    ['cloranthyRing', '104'], ['havelsRing', '100'],
  ]) assert.match(source, new RegExp(`const int ${name} = ${id};`, 'u'));
  assert.match(source, /RequireName\(names\.Weapons, grassCrestShield, "Grass Crest Shield"\)/u);
  assert.match(source, /RequireName\(names\.Weapons, havelsGreatshield, "Havel's Greatshield"\)/u);
  assert.match(source, /RequireName\(names\.Accessories, ringOfFavorAndProtection, "Ring of Favor and Protection"\)/u);
  assert.match(source, /RequireName\(names\.Accessories, cloranthyRing, "Cloranthy Ring"\)/u);
  assert.match(source, /\("equip_Wep_Right", blackKnightHalberd\), \("equip_Wep_Left", grassCrestShield\)/u);
  assert.match(source, /\("equip_Accessory01", ringOfFavorAndProtection\),\s*\("equip_Accessory02", cloranthyRing/u);
  assert.match(source, /Havel's Greatshield was equipped/u);
  assert.match(source, /Havel's Ring was equipped/u);
});

test('invokes only the owner QA package command and forwards process logs', async () => {
  const child = childProcess();
  let invocation;
  const logs = [];
  const pending = applyOwnerQaLoadout('read-only-source', 'staged-package', {
    dotnetPath: 'dotnet-custom', dataToolPath: 'tool.dll',
    spawnProcess: (command, args, options) => { invocation = { command, args, options }; return child; },
    onLog: (stream, message) => logs.push([stream, message]),
  });
  child.stdout.write('overlay applied');
  child.stderr.write('verified');
  child.emit('close', 0);
  assert.deepEqual(await pending, { ok: true, stdout: 'overlay applied', stderr: 'verified' });
  assert.equal(invocation.command, 'dotnet-custom');
  assert.deepEqual(invocation.args, ['tool.dll', 'apply-owner-qa-loadout', '--game', 'read-only-source', '--package', 'staged-package']);
  assert.deepEqual(logs, [['stdout', 'overlay applied'], ['stderr', 'verified']]);
});

test('returns typed failures for spawn and process failures', async () => {
  const missing = await applyOwnerQaLoadout('source', 'package', { spawnProcess: () => { throw new Error('no dotnet'); } });
  assert.equal(missing.ok, false);
  assert.equal(missing.code, 'OWNER_QA_TOOL_MISSING');
  const child = childProcess();
  const pending = applyOwnerQaLoadout('source', 'package', { spawnProcess: () => child });
  child.stderr.write('item validation failed');
  child.emit('close', 2);
  assert.deepEqual(await pending, {
    ok: false, code: 'OWNER_QA_TOOL_FAILED', message: 'DsrDataTool exited with code 2.', stdout: '', stderr: 'item validation failed',
  });
});
