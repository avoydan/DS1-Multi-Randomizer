import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { copyFile, mkdir, mkdtemp, readFile, rm } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { EventEmitter } from 'node:events';
import os from 'node:os';
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

test('vendored Skill Issue overlay resolves the named +5 weapon identity and preserves exact equipment slots', async () => {
  const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../');
  const source = await readFile(path.join(repositoryRoot, 'engines/combined/vendor/tools/DsrDataTool/Program.cs'), 'utf8');
  for (const [name, id] of [
    ['grassCrestShield', '1_453_000'],
    ['havelsGreatshield', '1_505_000'], ['ringOfFavorAndProtection', '143'],
    ['cloranthyRing', '104'], ['havelsRing', '100'],
  ]) assert.match(source, new RegExp(`const int ${name} = ${id};`, 'u'));
  assert.match(source, /ResolveUniqueWeaponName\(level == 0\s*\? "Black Knight Halberd"\s*:\s*\$"Black Knight Halberd\+\{level\}"\)/u);
  assert.match(source, /var blackKnightHalberd = blackKnightHalberdByLevel\[5\]/u);
  assert.match(source, /id \/ 1_000 != blackKnightHalberdBaseId \/ 1_000/u);
  assert.match(source, /RequireRow\(weaponParam, blackKnightHalberdBaseId, "Black Knight Halberd base EquipParamWeapon row"\)/u);
  assert.match(source, /GetCellInt\(baseWeaponRow, "reinforceTypeId", -1\)/u);
  assert.match(source, /GetCellInt\(reinforcementRow, "reinforcementLevel", -1\)/u);
  assert.match(source, /reinforcementLevel != 5/u);
  assert.doesNotMatch(source, /const int blackKnightHalberd = 1_105_000/u);
  assert.match(source, /RequireName\(names\.Weapons, grassCrestShield, "Grass Crest Shield"\)/u);
  assert.match(source, /RequireName\(names\.Weapons, havelsGreatshield, "Havel's Greatshield"\)/u);
  assert.match(source, /RequireName\(names\.Accessories, ringOfFavorAndProtection, "Ring of Favor and Protection"\)/u);
  assert.match(source, /RequireName\(names\.Accessories, cloranthyRing, "Cloranthy Ring"\)/u);
  assert.match(source, /\("equip_Wep_Right", blackKnightHalberd\), \("equip_Wep_Left", grassCrestShield\)/u);
  assert.match(source, /\("equip_Accessory01", ringOfFavorAndProtection\),\s*\("equip_Accessory02", cloranthyRing/u);
  assert.match(source, /Havel's Greatshield was equipped/u);
  assert.match(source, /Havel's Ring was equipped/u);
});

test('Skill Issue final QA loadout: the nine consumables, the Skull Lantern, the exact goods allow-list and nothing that opens the world', async () => {
  const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../');
  const source = await readFile(path.join(repositoryRoot, 'engines/combined/vendor/tools/DsrDataTool/Program.cs'), 'utf8');
  const start = source.indexOf('static OwnerQaLoadoutReport ApplyOwnerQaLoadout');
  const end = source.indexOf('static void AssertSkillIssueGrantLots');
  assert.ok(start > 0 && end > start);
  const overlay = source.slice(start, end);

  // Existing loadout is untouched.
  for (const fragment of ['("equip_Wep_Right", blackKnightHalberd), ("equip_Wep_Left", grassCrestShield)', '("equip_Helm", havelsHelm), ("equip_Armer", havelsArmor)',
    '("equip_Accessory01", ringOfFavorAndProtection)', '("equip_Accessory02", cloranthyRing)', '("soul", 10_000)', '("baseVit", 99)', '(twinklingTitanite, 10)'])
    assert.ok(overlay.includes(fragment), `existing loadout fragment missing: ${fragment}`);

  // Skull Lantern: a weapon in the second left-hand slot, resolved by name and checked left-hand equipable.
  assert.match(overlay, /const int skullLantern = 1_396_000;/u);
  assert.match(overlay, /RequireName\(names\.Weapons, skullLantern, "Skull Lantern"\)/u);
  assert.match(overlay, /\("equip_Subwep_Left", skullLantern\)/u);
  assert.match(overlay, /leftHandEquipable/u);

  // The goods allow-list is exact: Humanity is goods 500 (the consumable), Prism Stone is the one requested item left out for slot capacity.
  const requests = [...overlay.matchAll(/\((\d+), "([^"]+)", (\d+)\)/gu)].map((match) => [Number(match[1]), match[2], Number(match[3])]);
  assert.deepEqual(requests, [
    [500, 'Humanity', 99], [501, 'Twin Humanities', 99], [240, 'Divine Blessing', 99], [330, 'Homeward Bone', 99], [260, 'Green Blossom', 99],
    [274, 'Purging Stone', 99], [272, 'Blooming Purple Moss Clump', 99], [312, 'Transient Curse', 99], [280, 'Repair Powder', 99],
  ]);
  assert.ok(requests.length + 1 <= 10, 'Titanite plus the consumables must fit the ten CharaInitParam goods slots');
  assert.match(overlay, /Math\.Min\(requested, maxNum\)/u);
  assert.match(overlay, /goodsLoadout\.Count > 10/u);
  assert.match(overlay, /Prism Stone \(goods 370\)/u);

  // Nothing that can unlock or bypass progression, and no progression flag or event is touched by the overlay.
  for (const forbidden of ['Master Key', 'Orange Charred Ring', 'Covenant of Artorias', 'Lordvessel', 'Crest of Artorias', 'Cage Key', 'Residence Key', 'Key to the Seal',
    'Archive Tower Giant Door Key', 'Peculiar Doll', 'Broken Pendant', 'Lord Soul', 'Bequeathed']) assert.ok(!requests.some(([, name]) => name.includes(forbidden)), forbidden);
  // Events, item lots and flags are touched only by the one-time grant (see skillIssueGrant.test.mjs); no map and no existing flag or progression event.
  assert.doesNotMatch(overlay, /SetEventFlag|BossFlag|MSB1|\.msb|11\d{6}/u);
  // The overlay may only change loadout fields on the class rows, and proves it against the original rows.
  assert.match(overlay, /originalCharaCells/u);
  assert.match(overlay, /which is outside the starting loadout/u);
});

test('Resistance uses DSR CHARACTER_INIT_PARAM.baseDurability, not the unrelated baseLuc field', async () => {
  const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../');
  const source = await readFile(path.join(repositoryRoot, 'engines/combined/vendor/tools/DsrDataTool/Program.cs'), 'utf8');
  assert.match(source, /DSR's displayed Resistance stat is stored in baseDurability/u);
  assert.match(source, /"baseDex", "baseDurability", "baseMag", "baseFai"/u);
  assert.match(source, /\("baseDex", 99\), \("baseDurability", 99\), \("baseMag", 99\)/u);
  assert.match(source, /foreach \(var field in new\[\] \{[^}]*baseDurability[^}]*\}\)/u);
  assert.doesNotMatch(source, /\("baseLuc", 99\)/u);
  assert.match(source, /originalBaseLuc/u);
  assert.match(source, /changed the separate baseLuc field/u);
  assert.match(source, /verified Resistance\/baseDurability and separate baseLuc fields/u);
});

test('real DSR PARAM data: serialized Skill Issue output sets all eight displayed stats and creates GameParam when absent', {
  skip: !process.env.DS1TR_QA_DSR_ROOT,
}, async () => {
  const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../../');
  const gameRoot = process.env.DS1TR_QA_DSR_ROOT;
  const tool = path.join(repositoryRoot, 'engines/combined/vendor/tools/DsrDataTool/bin/Release/net8.0/DsrDataTool.exe');
  const itemMessages = path.join(gameRoot, 'msg/ENGLISH/item.msgbnd.dcx');
  const gameParam = path.join(gameRoot, 'param/GameParam/GameParam.parambnd.dcx');
  const paramdef = path.join(gameRoot, 'paramdef/paramdef.paramdefbnd.dcx');
  const hash = (value) => createHash('sha256').update(value).digest('hex');
  const sourceHashes = await Promise.all([itemMessages, gameParam, paramdef].map(async (file) => hash(await readFile(file))));
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-skill-issue-param-'));
  try {
    const layouts = [
      { name: 'existing-mod-output', root: path.join(temp, 'with-mod'), relative: 'mod/param/GameParam/GameParam.parambnd.dcx', copy: true },
      { name: 'minimal-no-randomizer-output', root: path.join(temp, 'no-randomizers'), relative: 'param/GameParam/GameParam.parambnd.dcx', copy: false },
    ];
    for (const layout of layouts) {
      const output = path.join(layout.root, layout.relative);
      await mkdir(path.dirname(output), { recursive: true });
      if (layout.copy) await copyFile(gameParam, output);
      const result = spawnSync(tool, ['apply-owner-qa-loadout', '--game', gameRoot, '--package', layout.root], { encoding: 'utf8' });
      assert.equal(result.error, undefined, `${layout.name}: ${result.error?.message}`);
      assert.equal(result.status, 0, `${layout.name}: ${result.stderr}`);
      assert.match(result.stdout, /applied to 10 classes/u, layout.name);
      assert.match(result.stdout, /Validated DSR items: 28/u, layout.name);
      assert.match(result.stdout, /Starting inventory: Twinkling Titanite x10; Humanity x99; Twin Humanities x99; Divine Blessing x99; Homeward Bone x99; Green Blossom x99; Purging Stone x99; Blooming Purple Moss Clump x99; Transient Curse x99; Repair Powder x99\./u, layout.name);
      assert.match(result.stdout, /Second left-hand slot: Skull Lantern\./u, layout.name);
      assert.match(result.stdout, /One-time grant: event 8990, flag 6931, lots 90010 to 90090 step 10: Sunlight Maggot; Mask of the Mother; Mask of the Father; Mask of the Child; Dark Wood Grain Ring; Havel's Ring; Rusted Iron Ring; Slumbering Dragoncrest Ring; Prism Stone x99\./u, layout.name);
      assert.ok((await readFile(path.join(layout.root, path.dirname(layout.relative).startsWith('mod') ? 'mod/event/common.emevd.dcx' : 'event/common.emevd.dcx'))).byteLength > 0, `${layout.name} emitted common.emevd`);
      assert.match(result.stdout, /Black Knight Halberd \+5 item ID: 1105005; EquipParamWeapon base row: 1105000; ReinforceParamWeapon row: 1200 \(level 5\)/u, layout.name);
      assert.ok((await readFile(output)).byteLength > 0, `${layout.name} emitted GameParam`);
    }
    assert.deepEqual(await Promise.all([itemMessages, gameParam, paramdef].map(async (file) => hash(await readFile(file)))), sourceHashes,
      'the real QA source remains byte-identical');
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
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
