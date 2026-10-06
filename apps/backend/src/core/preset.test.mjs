import assert from 'node:assert/strict';
import test from 'node:test';
import { exportPreset, importPreset } from '../../dist/core/preset.js';

function config() {
  return {
    gamePath: 'C:/DSR', masterSeed: ' seed-1 ',
    items: { enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true,
      guaranteeEarlyWeapon: true, balancedEarlyLoot: true, randomizeStartingClass: false,
      randomizeStartingEquipment: false, randomizeGifts: false, randomizeEnemyDrops: false,
      guaranteedEnemyDrops: false, randomizeShops: false, fashionSouls: false, soulItems: 'replaced', startingItems: 'shield-1h' },
    enemies: { enabled: true, randomizeRegularEnemies: true, randomizeBosses: true, scaling: 'progressive', replaceMimics: false, replaceRespawningMosquitoes: true, preventSameEnemy: true, pinwheelChaos: true, gwynNerf: 'strong', secondGargoyle: 'keep', placement: { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null } },
    fog: { enabled: true, traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: true,
      minorPvpFogs: false, lordvesselGates: false, randomStart: false, disconnectedFogs: false,
      noFallBedOfChaos: false, scaling: true, glitchedLogic: false, requireLordSouls: true,
      pacifistMode: false, language: 'POLISH' },
    output: { includeDlc: true, generateSpoilerLog: false },
  };
}

test('export omits the game path and round-trips through import deterministically', () => {
  const preset = exportPreset(config());
  assert.equal('gamePath' in preset, false);
  assert.equal(preset.masterSeed, 'seed-1');
  const text = JSON.stringify(preset);
  const imported = importPreset(text);
  assert.equal(imported.ok, true);
  assert.deepEqual(imported.preset, preset);
  assert.equal(JSON.stringify(importPreset(text).preset), text);
});

test('import rejects malformed, foreign and unsupported presets', () => {
  assert.equal(importPreset('{').ok, false);
  assert.equal(importPreset('[]').ok, false);
  const preset = exportPreset(config());
  assert.match(importPreset(JSON.stringify({ ...preset, format: 'other' })).problems.join(' '), /format/);
  assert.match(importPreset(JSON.stringify({ ...preset, presetVersion: 12 })).problems.join(' '), /version/);
  assert.match(importPreset(JSON.stringify({ ...preset, gamePath: 'C:/x' })).problems.join(' '), /gamePath is not supported/);
  const unknown = { ...preset, fog: { ...preset.fog, bogus: true } };
  assert.match(importPreset(JSON.stringify(unknown)).problems.join(' '), /bogus is not supported/);
  const unsafe = { ...preset, items: { ...preset.items, randomizeProtectedItems: true } };
  assert.match(importPreset(JSON.stringify(unsafe)).problems.join(' '), /Protected-item/);
  const badLanguage = { ...preset, fog: { ...preset.fog, language: 'KLINGON' } };
  assert.equal(importPreset(JSON.stringify(badLanguage)).ok, false);
});

test('a version-1 preset is migrated with the legacy defaults', () => {
  const v2 = exportPreset(config());
  const v1 = JSON.parse(JSON.stringify(v2));
  v1.presetVersion = 1;
  delete v1.items.fashionSouls;
  for (const key of ['replaceMimics', 'replaceRespawningMosquitoes', 'preventSameEnemy']) delete v1.enemies[key];
  for (const key of ['soulItems', 'startingItems']) delete v1.items[key];
  for (const key of ['pinwheelChaos', 'gwynNerf', 'secondGargoyle', 'placement']) delete v1.enemies[key];
  const imported = importPreset(JSON.stringify(v1));
  assert.equal(imported.ok, true);
  assert.equal(imported.preset.presetVersion, 11);
  assert.equal(imported.preset.items.fashionSouls, true);
  assert.equal(imported.preset.enemies.replaceMimics, true);
  assert.equal(imported.preset.enemies.replaceRespawningMosquitoes, true);
  assert.equal(imported.preset.enemies.preventSameEnemy, false);
  // Batch 2 options migrate to the values that keep the old behavior, not the new legacy defaults.
  assert.equal(imported.preset.items.soulItems, 'shuffled');
  assert.equal(imported.preset.items.startingItems, 'mixed-pool');
  assert.equal(imported.preset.enemies.pinwheelChaos, false);
  assert.equal(imported.preset.enemies.gwynNerf, 'none');
  assert.deepEqual(imported.preset.enemies.placement, { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false });
  // A version-1 document must not already carry version-2 keys.
  const smuggled = JSON.parse(JSON.stringify(v1));
  smuggled.items.fashionSouls = false;
  assert.match(importPreset(JSON.stringify(smuggled)).problems.join(' '), /not supported in version 1/);
});

test('a version-2 preset round-trips the Batch 1 options', () => {
  const preset = exportPreset(config());
  assert.equal(preset.presetVersion, 11);
  assert.equal(preset.items.fashionSouls, false);
  assert.equal(preset.enemies.replaceMimics, false);
  assert.equal(preset.enemies.preventSameEnemy, true);
  const missing = JSON.parse(JSON.stringify(preset));
  delete missing.enemies.preventSameEnemy;
  assert.equal(importPreset(JSON.stringify(missing)).ok, false);
});

test('a version-2 preset is migrated with the behavior-preserving Batch 2 values', () => {
  const v3 = exportPreset(config());
  const v2 = JSON.parse(JSON.stringify(v3));
  v2.presetVersion = 2;
  for (const key of ['soulItems', 'startingItems']) delete v2.items[key];
  for (const key of ['pinwheelChaos', 'gwynNerf', 'secondGargoyle', 'placement']) delete v2.enemies[key];
  const imported = importPreset(JSON.stringify(v2));
  assert.equal(imported.ok, true);
  assert.equal(imported.preset.presetVersion, 11);
  assert.equal(imported.preset.items.fashionSouls, false, 'version-2 keys are kept');
  assert.equal(imported.preset.items.soulItems, 'shuffled');
  assert.equal(imported.preset.items.startingItems, 'mixed-pool');
  assert.equal(imported.preset.enemies.pinwheelChaos, false);
  assert.equal(imported.preset.enemies.gwynNerf, 'none');
  const smuggled = JSON.parse(JSON.stringify(v2));
  smuggled.enemies.gwynNerf = 'strong';
  assert.match(importPreset(JSON.stringify(smuggled)).problems.join(' '), /not supported in version 2/);
});

test('a preset that turns Include DLC off is rejected', () => {
  const preset = exportPreset(config());
  preset.output.includeDlc = false;
  assert.match(importPreset(JSON.stringify(preset)).problems.join(' '), /Excluding DLC content/);
});

test('presets of versions 1 to 3 migrate the second Gargoyle to the earlier single-boss behavior', () => {
  const v4 = exportPreset(config());
  assert.equal(v4.enemies.secondGargoyle, 'keep');
  for (const version of [1, 2, 3]) {
    const old = JSON.parse(JSON.stringify(v4));
    old.presetVersion = version;
    delete old.enemies.secondGargoyle;
    delete old.enemies.placement;
    if (version <= 2) {
      for (const key of ['soulItems', 'startingItems']) delete old.items[key];
      for (const key of ['pinwheelChaos', 'gwynNerf']) delete old.enemies[key];
    }
    if (version === 1) {
      delete old.items.fashionSouls;
      for (const key of ['replaceMimics', 'replaceRespawningMosquitoes', 'preventSameEnemy']) delete old.enemies[key];
    }
    const imported = importPreset(JSON.stringify(old));
    assert.equal(imported.ok, true, `version ${version}`);
    assert.equal(imported.preset.presetVersion, 11);
    // Not the new legacy default: the old preset keeps reproducing the old Bell Gargoyle handling.
    assert.equal(imported.preset.enemies.secondGargoyle, 'single-boss');
    const smuggled = JSON.parse(JSON.stringify(old));
    smuggled.enemies.secondGargoyle = 'replace';
    assert.match(importPreset(JSON.stringify(smuggled)).problems.join(' '), new RegExp(`not supported in version ${version}`));
  }
});

test('presets of versions 1 to 10 migrate to permutation-v1 with neutral sampling settings; version 11 states them', () => {
  const v6 = exportPreset(config());
  assert.equal(v6.presetVersion, 11);
  assert.deepEqual(v6.enemies.placement, { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false });
  for (const version of [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]) {
    const old = JSON.parse(JSON.stringify(v6));
    old.presetVersion = version;
    // Version 6 had the algorithm and the first three settings, version 5 the algorithm only; versions 1-4 no placement section.
    if (version === 10) for (const key of ['bossChanceInNormalSlots', 'roamingBossSouls', 'roamingBossesStayDead']) delete old.enemies.placement[key];
    else if (version === 9) for (const key of ['bossReplacement', 'uniqueBosses', 'bossChanceInNormalSlots', 'roamingBossSouls', 'roamingBossesStayDead']) delete old.enemies.placement[key];
    else if (version === 8) old.enemies.placement = { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled' };
    else if (version === 7) old.enemies.placement = { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose' };
    else if (version === 6) old.enemies.placement = { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere' };
    else if (version === 5) old.enemies.placement = { algorithm: 'permutation-v1' };
    else delete old.enemies.placement;
    if (version <= 3) delete old.enemies.secondGargoyle;
    if (version <= 2) {
      for (const key of ['soulItems', 'startingItems']) delete old.items[key];
      for (const key of ['pinwheelChaos', 'gwynNerf']) delete old.enemies[key];
    }
    if (version === 1) {
      delete old.items.fashionSouls;
      for (const key of ['replaceMimics', 'replaceRespawningMosquitoes', 'preventSameEnemy']) delete old.enemies[key];
    }
    const imported = importPreset(JSON.stringify(old));
    assert.equal(imported.ok, true, `version ${version}: ${(imported.problems ?? []).join(' ')}`);
    assert.deepEqual(imported.preset.enemies.placement, { algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false }, `version ${version}`);
    // An old document must not already carry the newer keys.
    const smuggled = JSON.parse(JSON.stringify(old));
    smuggled.enemies.placement = version === 10 ? { ...old.enemies.placement, roamingBossSouls: 20 } : version === 9 ? { ...old.enemies.placement, bossReplacement: 'bosses' } : version === 8 ? { algorithm: 'sampling-v1', replacementChance: 50, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'strict', typeReplacement: 'enabled', customPool: ['c2500'] } : version === 7 ? { algorithm: 'sampling-v1', replacementChance: 50, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'strict', typeReplacement: 'enabled', customPool: null } : version === 6 ? { algorithm: 'sampling-v1', replacementChance: 50, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'curve' } : { algorithm: 'sampling-v1', replacementChance: 50 };
    assert.match(importPreset(JSON.stringify(smuggled)).problems.join(' '), new RegExp(`not supported in version ${version}`));
    if (version === 5) {
      const sneaky = JSON.parse(JSON.stringify(old));
      sneaky.enemies.placement.fit = 'fits';
      assert.match(importPreset(JSON.stringify(sneaky)).problems.join(' '), /placement\.fit is not supported in version 5/);
    }
  }
});

test('a version-6 preset states every placement setting; sampling-v1 round-trips and is never implied', () => {
  const preset = exportPreset(config());
  for (const key of ['algorithm', 'replacementChance', 'normalReplacement', 'fit']) {
    const missing = JSON.parse(JSON.stringify(preset));
    delete missing.enemies.placement[key];
    const result = importPreset(JSON.stringify(missing));
    assert.equal(result.ok, false, key);
    assert.match(result.problems.join(' '), new RegExp(`placement\.${key} is required`));
  }
  const missingSection = JSON.parse(JSON.stringify(preset));
  delete missingSection.enemies.placement;
  assert.equal(importPreset(JSON.stringify(missingSection)).ok, false);

  const sampling = JSON.parse(JSON.stringify(preset));
  sampling.enemies.placement = { algorithm: 'sampling-v1', replacementChance: 35, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false };
  const text = JSON.stringify(sampling);
  const imported = importPreset(text);
  assert.equal(imported.ok, true, (imported.problems ?? []).join(' '));
  assert.deepEqual(imported.preset.enemies.placement, sampling.enemies.placement);
  assert.equal(JSON.stringify(imported.preset), text, 'export reproduces the document');

  const unknown = JSON.parse(JSON.stringify(preset));
  unknown.enemies.placement.algorithm = 'bogus';
  assert.equal(importPreset(JSON.stringify(unknown)).ok, false);
});

test('sampling-only settings are rejected under permutation-v1 and validated under sampling-v1', () => {
  const preset = exportPreset(config());
  for (const [key, value] of [['replacementChance', 80], ['normalReplacement', 'none'], ['fit', 'fits']]) {
    const document = JSON.parse(JSON.stringify(preset));
    document.enemies.placement[key] = value;
    const result = importPreset(JSON.stringify(document));
    assert.equal(result.ok, false, key);
    assert.match(result.problems.join(' '), /only applies to sampling-v1/);
  }
  for (const [key, value] of [['replacementChance', 101], ['replacementChance', -1], ['replacementChance', 12.5], ['replacementChance', '50'], ['normalReplacement', 'bosses'], ['normalReplacement', 'both'], ['fit', 'everywhere']]) {
    const document = JSON.parse(JSON.stringify(preset));
    document.enemies.placement = { algorithm: 'sampling-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'fits', [key]: value };
    assert.equal(importPreset(JSON.stringify(document)).ok, false, `${key}=${value}`);
  }
  for (const chance of [0, 1, 99, 100]) {
    const document = JSON.parse(JSON.stringify(preset));
    document.enemies.placement = { algorithm: 'sampling-v1', replacementChance: chance, normalReplacement: 'none', fit: 'anywhere-except-npcs', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null };
    assert.equal(importPreset(JSON.stringify(document)).ok, true, `chance ${chance}`);
  }
});

test('Type replacement round-trips in a sampling preset, is required in version 9 and refused under permutation-v1', () => {
  const preset = exportPreset(config());
  for (const value of ['enabled', 'enabled-except-roaming-bosses', 'disabled']) {
    const document = JSON.parse(JSON.stringify(preset));
    document.enemies.placement = { algorithm: 'sampling-v1', replacementChance: 70, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'strict', typeReplacement: value, customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false };
    const text = JSON.stringify(document);
    const imported = importPreset(text);
    assert.equal(imported.ok, true, (imported.problems ?? []).join(' '));
    assert.equal(JSON.stringify(imported.preset), text, 'export reproduces the document');
  }
  const missing = JSON.parse(JSON.stringify(preset));
  delete missing.enemies.placement.typeReplacement;
  assert.match(importPreset(JSON.stringify(missing)).problems.join(' '), /typeReplacement is required/);
  const classic = JSON.parse(JSON.stringify(preset));
  classic.enemies.placement.typeReplacement = 'enabled';
  assert.match(importPreset(JSON.stringify(classic)).problems.join(' '), /only applies to sampling-v1/);
  const bad = JSON.parse(JSON.stringify(preset));
  bad.enemies.placement = { ...bad.enemies.placement, algorithm: 'sampling-v1', typeReplacement: 'sometimes', customPool: null };
  assert.equal(importPreset(JSON.stringify(bad)).ok, false);
});

test('the custom pool exports sorted, round-trips exactly, and bad pools are refused with actionable messages', () => {
  const preset = exportPreset(config());
  const sampling = (customPool) => {
    const document = JSON.parse(JSON.stringify(preset));
    document.enemies.placement = { algorithm: 'sampling-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool };
    return document;
  };
  // Any order in, canonical (ascending) order out; the canonical document round-trips byte for byte.
  const imported = importPreset(JSON.stringify(sampling(['c2500', 'c1200', 'c3300'])));
  assert.equal(imported.ok, true, (imported.problems ?? []).join(' '));
  assert.deepEqual(imported.preset.enemies.placement.customPool, ['c1200', 'c2500', 'c3300']);
  const canonical = JSON.stringify(imported.preset);
  assert.equal(JSON.stringify(importPreset(canonical).preset), canonical);
  assert.deepEqual(exportPreset({ ...config(), enemies: { ...config().enemies, placement: sampling(['c9', 'c1']).enemies.placement } }).enemies.placement.customPool, ['c1', 'c9']);
  // null is the full pool and is the migrated value of every older preset.
  assert.equal(importPreset(JSON.stringify(sampling(null))).ok, true);
  const bad = (pool) => importPreset(JSON.stringify(sampling(pool))).problems.join(' ');
  assert.match(bad([]), /empty/);
  assert.match(bad(['c2500', 'c2500']), /more than once/);
  assert.match(bad(['not a model']), /model ids/);
  assert.match(bad('c2500'), /null or a list/);
  assert.match(bad(Array.from({ length: 513 }, (_, i) => `c${1000 + i}`)), /more than 512/);
  // permutation-v1 never accepts a pool.
  const classic = JSON.parse(JSON.stringify(preset));
  classic.enemies.placement.customPool = ['c2500'];
  assert.match(importPreset(JSON.stringify(classic)).problems.join(' '), /only applies to sampling-v1/);
  const missing = JSON.parse(JSON.stringify(preset));
  delete missing.enemies.placement.customPool;
  assert.match(importPreset(JSON.stringify(missing)).problems.join(' '), /customPool is required/);
});


test('boss sampling options round-trip in a sampling preset, are optional on input and refused under permutation-v1', () => {
  const preset = exportPreset(config());
  const document = JSON.parse(JSON.stringify(preset));
  document.enemies.placement = { ...document.enemies.placement, algorithm: 'sampling-v1', bossReplacement: 'bosses', uniqueBosses: true };
  const text = JSON.stringify(document);
  const imported = importPreset(text);
  assert.equal(imported.ok, true, (imported.problems ?? []).join(' '));
  assert.equal(JSON.stringify(imported.preset), text, 'export reproduces the document');
  // Unique bosses needs boss sampling; a bad mode is refused.
  for (const placement of [{ bossReplacement: 'classic', uniqueBosses: true }, { bossReplacement: 'everything', uniqueBosses: false }, { bossReplacement: 'bosses', uniqueBosses: 'yes' }]) {
    const bad = JSON.parse(text);
    Object.assign(bad.enemies.placement, placement);
    assert.equal(importPreset(JSON.stringify(bad)).ok, false, JSON.stringify(placement));
  }
  const classic = JSON.parse(JSON.stringify(preset));
  classic.enemies.placement.bossReplacement = 'bosses';
  assert.match(importPreset(JSON.stringify(classic)).problems.join(' '), /only applies to sampling-v1/);
  const optional = JSON.parse(JSON.stringify(preset));
  delete optional.enemies.placement.bossReplacement;
  delete optional.enemies.placement.uniqueBosses;
  const completed = importPreset(JSON.stringify(optional));
  assert.equal(completed.ok, true, 'a document without the options means the classic boss planner');
  assert.equal(completed.preset.enemies.placement.bossReplacement, 'classic');
});
