// Configuration x seed matrix for the enemy placement work (real catalog, real DsrDataTool, optionally the real FogHost).
//
// For every case and seed it runs the production pipeline (prepareRun + generateRun) against a clean copy of Dark Souls
// Remastered and records the final result, the attempts, the DsrDataTool event-window audit (DS1TR_AUDIT_EVENTS: IDs reserved
// vs IDs used per map, floor event) and, for the first seeds of each case, a rerun that must give an identical package.
// Fog packages are checked with `verify-skips` by the sweeps of fogHostE2e.mjs; this runner is about failures, retries and event windows.
//
// Not part of `npm test` (needs a clean game copy and the built helpers). Usage:
//   DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> DS1TR_DATA_TOOL_EXE=... DS1TR_FOG_HOST=... DS1TR_STATE_DIR=<state with world data> \
//     node apps/backend/scripts/placementMatrix.mjs <set> [--seeds N] [--cases a,b] [--reruns K]
//   node apps/backend/scripts/placementMatrix.mjs --list

import { cp, mkdtemp, mkdir, readdir, readFile, rm } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { GameStateAuthority, executableName, managedRoots } from '../dist/core/gameState.js';
import { generateRun } from '../dist/core/generationPipeline.js';
import { prepareRun } from '../dist/core/seededRun.js';
import { DEFAULT_GENERATION_SETTINGS } from '@ds1tr/contracts';
import { buildSettings, packageDigest } from './goldenCompatibility.mjs';

const sampling = (placement, enemies = {}) => ({ enemies: { ...enemies, placement: { algorithm: 'sampling-v1', ...placement } } });
const noItems = { items: { enabled: false } };
const merge2 = (...parts) => parts.reduce((all, part) => ({
  ...all, ...part, enemies: { ...(all.enemies ?? {}), ...(part.enemies ?? {}), placement: { ...(all.enemies?.placement ?? {}), ...(part.enemies?.placement ?? {}) } },
  fog: { ...(all.fog ?? {}), ...(part.fog ?? {}) }, items: { ...(all.items ?? {}), ...(part.items ?? {}) },
}), {});
const noFog = { fog: { enabled: false } };

/** The configuration that used to fail: Type replacement + Placement anywhere with bosses on (the Bed of Chaos floor event exists). */
const formerlyFailing = merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled', replacementChance: 100 }, { gwynNerf: 'medium', secondGargoyle: 'keep' }));

export const sets = {
  /** 100 seeds of the historically failing configuration, without and with the world randomizer. */
  'type-event-former': [
    { name: 'former-failing', patch: formerlyFailing, seeds: 100 },
    { name: 'former-failing-fog', patch: merge2(noItems, sampling({ fit: 'anywhere', typeReplacement: 'enabled', replacementChance: 100 }, { gwynNerf: 'medium', secondGargoyle: 'replace' })), seeds: 30 },
  ],
  /** The wider matrix of the event-ID fix: chance x fit x difficulty x Type replacement x Prevent same x pool x Fog. */
  'type-event-matrix': [
    ...[25, 60, 100].flatMap((chance) => [
      { name: `type-enabled-anywhere-random-c${chance}`, patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled', replacementChance: chance, difficultyMode: 'random' })) },
      { name: `type-enabled-fits-curve-c${chance}`, patch: merge2(noItems, noFog, sampling({ fit: 'fits', typeReplacement: 'enabled', replacementChance: chance, difficultyMode: 'curve', difficultyStrictness: 'strict' })) },
      { name: `type-except-roaming-anywhere-c${chance}`, patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled-except-roaming-bosses', replacementChance: chance })) },
    ]),
    { name: 'type-enabled-anywhere-prevent-same', patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled' }, { preventSameEnemy: true })) },
    { name: 'type-enabled-fits-prevent-same-curve', patch: merge2(noItems, noFog, sampling({ fit: 'fits', typeReplacement: 'enabled', difficultyMode: 'curve', difficultyStrictness: 'loose' }, { preventSameEnemy: true })) },
    { name: 'type-enabled-restrictive-pool', patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled', customPool: ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2500', 'c2711', 'c2860', 'c2940', 'c3490', 'c4171'] })) },
    { name: 'type-enabled-boss-sampling', patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled', bossReplacement: 'bosses' })) },
    { name: 'type-enabled-anywhere-fog', patch: merge2(noItems, sampling({ fit: 'anywhere', typeReplacement: 'enabled', replacementChance: 60 }, { gwynNerf: 'strong' })) },
    { name: 'type-enabled-fits-curve-fog', patch: merge2(noItems, sampling({ fit: 'fits', typeReplacement: 'enabled', difficultyMode: 'curve', difficultyStrictness: 'loose' }, { preventSameEnemy: true })) },
    { name: 'type-enabled-pool-fog', patch: merge2(noItems, sampling({ fit: 'anywhere', typeReplacement: 'enabled', customPool: ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2500', 'c2711', 'c2860', 'c2940', 'c3490', 'c4171'] })) },
    { name: 'scaling-vanilla-type', patch: merge2(noItems, noFog, sampling({ fit: 'anywhere', typeReplacement: 'enabled' }, { scaling: 'vanilla' })) },
  ],
  /** Part E1: bosses in regular enemy slots (roaming bosses) through the real data tool; the "-fog" cases also run the world randomizer. */
  'roaming-e1': [
    { name: 'roaming-bosses-fits', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', fit: 'fits' })) },
    { name: 'roaming-both-legacy-default', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', fit: 'fits', difficultyMode: 'random-easy-asylum' })) },
    { name: 'roaming-both-25-anywhere', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'anywhere' })) },
    { name: 'roaming-bosses-c60-curve', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', replacementChance: 60, fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'loose' })) },
    { name: 'roaming-bosses-c25', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', replacementChance: 25, fit: 'anywhere' })) },
    { name: 'roaming-type-enabled', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 30, typeReplacement: 'enabled', fit: 'anywhere' })) },
    { name: 'roaming-type-except-roaming', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 30, typeReplacement: 'enabled-except-roaming-bosses', fit: 'anywhere' })) },
    { name: 'roaming-prevent-same', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', fit: 'fits' }, { preventSameEnemy: true })) },
    { name: 'roaming-custom-pool', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 50, fit: 'anywhere', customPool: ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2500', 'c2711', 'c2860', 'c2940', 'c3490', 'c4171'] })) },
    { name: 'roaming-boss-sampling', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'anywhere', bossReplacement: 'bosses' })) },
    { name: 'roaming-unique-bosses', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', replacementChance: 50, fit: 'anywhere', bossReplacement: 'bosses', uniqueBosses: true })) },
    { name: 'roaming-stay-dead-souls0', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', replacementChance: 40, fit: 'fits', roamingBossSouls: 0, roamingBossesStayDead: true })) },
    { name: 'roaming-second-gargoyle', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'anywhere' }, { secondGargoyle: 'replace' })) },
    { name: 'roaming-vanilla-scaling', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', replacementChance: 50, fit: 'anywhere' }, { scaling: 'vanilla' })) },
    { name: 'roaming-both-fog', patch: merge2(noItems, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'fits' })), seeds: 20 },
    { name: 'roaming-bosses-fog', patch: merge2(noItems, sampling({ normalReplacement: 'bosses', replacementChance: 40, fit: 'anywhere', typeReplacement: 'enabled-except-roaming-bosses' })), seeds: 20 },
  ],
  /** Part G: the EXACT product default (what a fresh installation starts with), with the engines switched the way a person would switch them. */
  'product-default': [
    { name: 'default-items-enemies-bosses', patch: merge2(structuredClone(DEFAULT_GENERATION_SETTINGS), noFog), seeds: 100 },
    { name: 'default-enemies-bosses-only', patch: merge2(structuredClone(DEFAULT_GENERATION_SETTINGS), noItems, noFog), seeds: 100 },
    { name: 'default-triple-with-fog', patch: structuredClone(DEFAULT_GENERATION_SETTINGS), seeds: 100 },
  ],
  /** Event owners (docs/EVENT_ALLOCATION.md): worlds with many placements in the Darkroot Basin, which shares the Darkroot Garden's event file. Every case is also audited. */
  'event-owners': [
    { name: 'owner-e1-roaming', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'bosses', fit: 'anywhere', replacementChance: 100 })), seeds: 100 },
    { name: 'owner-e1-both-stay-dead', patch: merge2(noItems, noFog, sampling({ normalReplacement: 'both', bossChanceInNormalSlots: 30, fit: 'anywhere', roamingBossSouls: 100, roamingBossesStayDead: true })), seeds: 100 },
    { name: 'owner-hostile-npcs', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'anywhere' })), seeds: 100 },
    { name: 'owner-type-enabled', patch: merge2(noItems, noFog, sampling({ typeReplacement: 'enabled', fit: 'anywhere' })), seeds: 100 },
    { name: 'owner-type-except-roaming', patch: merge2(noItems, noFog, sampling({ typeReplacement: 'enabled-except-roaming-bosses', normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'anywhere' })), seeds: 100 },
    { name: 'owner-general-sampling', patch: merge2(noItems, noFog, sampling({ fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'loose' })), seeds: 100 },
    { name: 'owner-part-d-boss-sampling', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'bosses', fit: 'anywhere' })), seeds: 60 },
    { name: 'owner-e2-bodies', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'both', bossChanceInBossSlots: 40, fit: 'anywhere' })), seeds: 60 },
    { name: 'owner-second-gargoyle', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'bosses', fit: 'anywhere' }, { secondGargoyle: 'replace', gwynNerf: 'strong' })), seeds: 60 },
    { name: 'owner-classic-frozen', patch: merge2(noItems, noFog, { enemies: { placement: { algorithm: 'permutation-v1' } } }), seeds: 30 },
  ],
  /**
   * Part H (docs/ITEM_PROGRESSION_MODEL.md): Soul items "Transposed" through the real data tool and, where Fog is on, the real FogHost. Every run is also checked
   * by the lots verifier (the planned rows are in the package, no other item lot changed, no NpcParam names a boss reward, the reward event is the clean game's).
   */
  'souls-h': [
    { name: 'souls-items', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { soulItems: 'transposed' } }), seeds: 100 },
    { name: 'souls-all-pools', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { soulItems: 'transposed', randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true } }), seeds: 60 },
    { name: 'souls-with-enemies-bosses', patch: merge2(noFog, { items: { soulItems: 'transposed' } }), seeds: 60 },
    { name: 'souls-boss-sampling-e2', patch: merge2(noFog, sampling({ bossReplacement: 'both', bossChanceInBossSlots: 60, normalReplacement: 'both', bossChanceInNormalSlots: 20, fit: 'anywhere' }, { secondGargoyle: 'replace' }), { items: { soulItems: 'transposed' } }), seeds: 40 },
    { name: 'souls-triple-fog', patch: merge2(structuredClone(DEFAULT_GENERATION_SETTINGS), { items: { soulItems: 'transposed' } }), seeds: 100 },
    { name: 'souls-replaced-fog', patch: merge2(structuredClone(DEFAULT_GENERATION_SETTINGS), { items: { soulItems: 'replaced' } }), seeds: 20 },
  ],
  /**
   * Part H: key placement (docs/ITEM_PROGRESSION_MODEL.md) through the real data tool and the real FogHost. Every run is checked by the lots verifier and, for the
   * progression, by FogMod's own GraphChecker over the lots of the FINAL package (`FogHost progression-check --game`): completable, prison escapable, every key located.
   */
  'keys-h': [
    { name: 'keys-nofog', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'shuffled' } }), seeds: 150 },
    { name: 'keys-nofog-enemies', patch: merge2(noFog, { items: { keyPlacement: 'shuffled' } }), seeds: 60 },
    { name: 'keys-nofog-all-pools', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'shuffled', randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true, soulItems: 'transposed' } }), seeds: 60 },
    { name: 'keys-nofog-lordvessel', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'shuffled', randomizeLordvessel: true } }), seeds: 80 },
    { name: 'keys-nofog-lordsouls', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'shuffled', randomizeLordSouls: true } }), seeds: 80 },
    { name: 'keys-nofog-lordvessel-lordsouls', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'shuffled', randomizeLordvessel: true, randomizeLordSouls: true } }), seeds: 80 },
    { name: 'keys-fog', patch: merge2({ enemies: { enabled: false } }, { items: { keyPlacement: 'shuffled' } }), seeds: 150 },
    { name: 'keys-fog-lordvessel-lordsouls', patch: merge2({ enemies: { enabled: false } }, { items: { keyPlacement: 'shuffled', randomizeLordvessel: true, randomizeLordSouls: true } }), seeds: 100 },
    { name: 'keys-triple', patch: merge2(structuredClone(DEFAULT_GENERATION_SETTINGS), { items: { keyPlacement: 'shuffled', randomizeLordvessel: true, randomizeLordSouls: true, soulItems: 'transposed' } }), seeds: 120 },
    { name: 'keys-triple-vanilla-fog-options', patch: merge2(structuredClone(DEFAULT_GENERATION_SETTINGS), { items: { keyPlacement: 'shuffled' } }), seeds: 100 },
  ],
  /**
   * Part I (docs/RACE_MODE.md, docs/ITEM_DIFFICULTY_MODEL.md, docs/LAUNDROMAT_MIXUP.md): Race Mode, Race Mode +, the item difficulties and the Laundromat Mixup through the
   * real data tool and, where Fog is on, the real FogHost. Every key world is also read back by FogMod's own reader and checker (the variant of its mode), the race
   * destinations are re-derived from the scan by the script itself, and every Laundromat package is compared with the clean game cell by cell.
   */
  'item-modes-h': [
    { name: 'race', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'race' } }), seeds: 40 },
    { name: 'race-lords', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'race', randomizeLordvessel: true, randomizeLordSouls: true, soulItems: 'transposed' } }), seeds: 30 },
    { name: 'race-plus', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'race-plus' } }), seeds: 40 },
    { name: 'race-plus-lords', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'race-plus', randomizeLordvessel: true, randomizeLordSouls: true } }), seeds: 30 },
    { name: 'race-fair-all-pools', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'race', itemDifficulty: 'fair', randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true } }), seeds: 30 },
    { name: 'race-unfair', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'race', itemDifficulty: 'unfair' } }), seeds: 30 },
    { name: 'race-very-unfair-all-pools', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'race', itemDifficulty: 'very-unfair', randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true, soulItems: 'transposed' } }), seeds: 30 },
    { name: 'fair-keys-shuffled', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'shuffled', itemDifficulty: 'fair' } }), seeds: 20 },
    { name: 'unfair', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { itemDifficulty: 'unfair' } }), seeds: 40 },
    { name: 'very-unfair', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { itemDifficulty: 'very-unfair' } }), seeds: 40 },
    { name: 'unfair-keys-all-pools', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'shuffled', itemDifficulty: 'unfair', randomizeGifts: true, randomizeEnemyDrops: true, randomizeShops: true } }), seeds: 30 },
    { name: 'very-unfair-keys-lords', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { keyPlacement: 'shuffled', itemDifficulty: 'very-unfair', randomizeLordvessel: true, randomizeLordSouls: true } }), seeds: 30 },
    { name: 'laundromat', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { laundromat: true } }), seeds: 30 },
    { name: 'laundromat-fashion-off', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { laundromat: true, fashionSouls: false } }), seeds: 20 },
    { name: 'laundromat-only', patch: merge2({ enemies: { enabled: false } }, noFog, { items: { randomizeWorldItems: false, laundromat: true } }), seeds: 20 },
    { name: 'race-fog', patch: merge2({ enemies: { enabled: false } }, { items: { keyPlacement: 'race' } }), seeds: 30 },
    { name: 'race-plus-fog', patch: merge2({ enemies: { enabled: false } }, { items: { keyPlacement: 'race-plus', randomizeLordvessel: true, randomizeLordSouls: true } }), seeds: 30 },
    { name: 'difficulty-fog', patch: merge2({ enemies: { enabled: false } }, { items: { itemDifficulty: 'very-unfair' } }), seeds: 30 },
    { name: 'laundromat-fog', patch: merge2({ enemies: { enabled: false } }, { items: { laundromat: true } }), seeds: 20 },
    { name: 'triple-race', patch: merge2(structuredClone(DEFAULT_GENERATION_SETTINGS), { items: { keyPlacement: 'race', randomizeLordvessel: true, randomizeLordSouls: true, soulItems: 'transposed', itemDifficulty: 'unfair', laundromat: true } }), seeds: 25 },
    { name: 'triple-race-plus', patch: merge2(structuredClone(DEFAULT_GENERATION_SETTINGS), { items: { keyPlacement: 'race-plus', soulItems: 'transposed', laundromat: true } }), seeds: 25 },
    { name: 'triple-very-unfair', patch: merge2(structuredClone(DEFAULT_GENERATION_SETTINGS), { items: { keyPlacement: 'shuffled', itemDifficulty: 'very-unfair', laundromat: true } }), seeds: 25 },
  ],
  /** Part F1: hostile NPCs through the real data tool (Fog is covered by the fogHostE2e sweeps): events, rows and AI bundles of every placed NPC. */
  'hostile-npc-f1': [
    { name: 'npc-anywhere', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'anywhere', difficultyMode: 'random' })) },
    { name: 'npc-fits-curve-strict', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'strict' })) },
    { name: 'npc-easy-asylum-c60', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'fits', difficultyMode: 'random-easy-asylum', replacementChance: 60 })) },
    { name: 'npc-type-enabled', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'fits', typeReplacement: 'enabled' })) },
    { name: 'npc-type-except-roaming-both', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'anywhere', typeReplacement: 'enabled-except-roaming-bosses', normalReplacement: 'both', bossChanceInNormalSlots: 25 })) },
    { name: 'npc-plus-roaming', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'fits', normalReplacement: 'both', bossChanceInNormalSlots: 20 })) },
    { name: 'npc-plus-boss-bodies', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'anywhere', bossReplacement: 'normals' })) },
    { name: 'npc-plus-boss-sampling-both', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'anywhere', bossReplacement: 'both', bossChanceInBossSlots: 50, normalReplacement: 'both', bossChanceInNormalSlots: 15 })) },
    { name: 'npc-vanilla-scaling', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'anywhere' }, { scaling: 'vanilla' })) },
    { name: 'npc-prevent-same-gargoyle-gwyn', patch: merge2(noItems, noFog, sampling({ hostileNpcs: true, fit: 'fits' }, { preventSameEnemy: true, secondGargoyle: 'replace', gwynNerf: 'strong' })) },
  ],
  /** Part E2: a normal enemy as the body of a boss encounter through the real data tool (Fog is covered by the fogHostE2e sweeps). */
  'encounter-e2': [
    { name: 'body-normals-anywhere', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random' })) },
    { name: 'body-normals-fits-curve', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', fit: 'fits', difficultyMode: 'curve', difficultyStrictness: 'strict' })) },
    { name: 'body-normals-c50', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', replacementChance: 50, fit: 'fits' })) },
    { name: 'body-both-40', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'both', bossChanceInBossSlots: 40, fit: 'anywhere' })) },
    { name: 'body-both-unique', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'both', bossChanceInBossSlots: 50, uniqueBosses: true, fit: 'anywhere' })) },
    { name: 'body-plus-roaming', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', normalReplacement: 'both', bossChanceInNormalSlots: 25, fit: 'fits' })) },
    { name: 'body-type-enabled', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', typeReplacement: 'enabled', fit: 'anywhere' })) },
    { name: 'body-custom-pool', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', fit: 'anywhere', customPool: ['c1200', 'c1201', 'c1202', 'c2260', 'c2270', 'c2280', 'c2500', 'c2560', 'c2390', 'c2711', 'c2860', 'c2940'] })) },
    { name: 'body-vanilla-scaling', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', fit: 'anywhere' }, { scaling: 'vanilla' })) },
    { name: 'body-second-gargoyle-gwyn', patch: merge2(noItems, noFog, sampling({ bossReplacement: 'normals', fit: 'fits' }, { secondGargoyle: 'replace', gwynNerf: 'strong' })) },
  ],
};

const deterministicSeed = (set, name, index) => `matrix-${set}-${name}-${index}`;

function parseArguments(argv) {
  const out = { set: null, seeds: null, cases: null, reruns: 2, list: false, only: null };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === '--list') out.list = true;
    else if (arg === '--seeds') out.seeds = Number(argv[++index]);
    else if (arg === '--cases') out.cases = argv[++index].split(',');
    else if (arg === '--reruns') out.reruns = Number(argv[++index]);
    else if (arg === '--only') out.only = argv[++index].split(',').map(Number);
    else if (!arg.startsWith('--') && out.set === null) out.set = arg;
  }
  return out;
}

async function main() {
  const args = parseArguments(process.argv.slice(2));
  if (args.list) {
    for (const [name, cases] of Object.entries(sets)) console.log(`${name}: ${cases.map((entry) => `${entry.name}${entry.seeds ? `(${entry.seeds})` : ''}`).join(', ')}`);
    return;
  }
  const source = process.env.DS1TR_GOLDEN_GAME_SOURCE ?? process.env.DS1TR_E2E_GAME_SOURCE;
  const caseList = args.set ? sets[args.set] : null;
  if (!caseList || !source) {
    console.error('Usage: DS1TR_GOLDEN_GAME_SOURCE=<clean game dir> node placementMatrix.mjs <set> [--seeds N] [--cases a,b] [--reruns K]  (--list shows the sets)');
    process.exit(2);
  }
  process.env.DS1TR_AUDIT_EVENTS = '1';
  const temp = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-matrix-'));
    const summary = [];
  try {
    const game = path.join(temp, 'game');
    await cp(path.join(source, executableName), path.join(game, executableName));
    for (const managed of managedRoots) await cp(path.join(source, ...managed.split('/')), path.join(game, ...managed.split('/')), { recursive: true });
    const authority = new GameStateAuthority({ stateRoot: path.join(temp, 'state'), isGameRunning: async () => false });
    if (!(await authority.importPristine(game)).ok) throw new Error('pristine import failed');

    // Event owner audit (docs/EVENT_ALLOCATION.md, "Event owners"): every placement that needs a custom event has a NEW event naming its entity in
    // the event file of its owner map; the exe is the static verifier. A package without randomizer.json (a Fog package) is audited through its combined run.
    const verifier = process.env.DS1TR_SMOKE_VERIFY_EXE ?? fileURLToPath(new URL('../../../tools/smoke-verify/bin/Release/net8.0/SmokeVerify.exe', import.meta.url));
    const auditOwners = async (run) => {
      let dir = run.result.packagePath;
      const has = async (candidate) => readFile(path.join(candidate, 'randomizer.json')).then(() => true, () => false);
      if (!(await has(dir))) {
        const attempts = (await readdir(run.workspacePath)).filter((name) => /^combined-\d+$/u.test(name)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
        const seedDirectory = (await readdir(path.join(run.workspacePath, attempts[0]))).find((name) => name.startsWith('seed_'));
        dir = path.join(run.workspacePath, attempts[0], seedDirectory);
      }
      const algorithm = run.prepared.config.enemies.placement.algorithm === 'sampling-v1' ? 'sampling' : 'classic';
      const out = spawnSync(verifier, ['audit', path.join(dir, 'mod'), path.join(dir, 'randomizer.json'), source, algorithm], { encoding: 'utf8' });
      try { return JSON.parse(out.stdout.trim().split(/\r?\n/u).pop()); } catch { return { checkedCount: 0, missing: [`the audit did not run: ${String(out.stderr).slice(0, 200)}`], duplicateOwners: [] }; }
    };

    // Item lot rows of the FINAL package (a Fog merge included) against the plan of the combined run (SmokeVerify lots).
    const auditLots = async (run) => {
      let dir = run.result.packagePath;
      const has = async (candidate) => readFile(path.join(candidate, 'randomizer.json')).then(() => true, () => false);
      if (!(await has(dir))) {
        const attempts = (await readdir(run.workspacePath)).filter((name) => /^combined-\d+$/u.test(name)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
        const seedDirectory = (await readdir(path.join(run.workspacePath, attempts[0]))).find((name) => name.startsWith('seed_'));
        dir = path.join(run.workspacePath, attempts[0], seedDirectory);
      }
      // A combined-only package keeps its game tree under mod/; a Fog package IS the staged game tree.
      const tree = await readdir(path.join(run.result.packagePath, 'param')).then(() => run.result.packagePath, () => path.join(run.result.packagePath, 'mod'));
      const out = spawnSync(verifier, ['lots', tree, path.join(source, 'paramdef', 'paramdef.paramdefbnd.dcx'), path.join(dir, 'randomizer.json'), source], { encoding: 'utf8' });
      try { return JSON.parse(out.stdout.trim().split(/\r?\n/u).pop()); } catch { return { checkedTargets: 0, bossRewardTargets: 0, problems: [`the lots verifier did not run: ${String(out.stderr).slice(0, 200)}`] }; }
    };

    // FogMod's GraphChecker over the lots of the FINAL package (a Fog merge included): the one model of the world, asked about the written game data.
    const fogHost = process.env.DS1TR_FOG_HOST;
    const worldRoot = path.join(process.env.DS1TR_STATE_DIR ?? '', 'world-data');
    const model = await (async () => {
      try { return JSON.parse(await readFile(path.join(worldRoot, (await readdir(worldRoot)).find((name) => name.endsWith('.progression-model.json'))), 'utf8')); } catch { return { keys: [] }; }
    })();
    const auditProgression = async (run) => {
      const tree = await readdir(path.join(run.result.packagePath, 'param')).then(() => run.result.packagePath, () => path.join(run.result.packagePath, 'mod'));
      const dataRoot = path.join(worldRoot, (await readdir(worldRoot))[0]);
      const out = path.join(temp, `progression-${path.basename(run.workspacePath)}.json`);
      const proc = spawnSync(fogHost, ['progression-check', '--data-root', dataRoot, ...(run.prepared.config.items.keyPlacement === 'race-plus' && !run.prepared.config.fog.enabled ? ['--variant', 'race-plus'] : []), '--game', tree, '--out', out], { encoding: 'utf8' });
      try { return JSON.parse(await readFile(out, 'utf8')); } catch { return { worldCompletable: false, prisonEscapable: false, unvisited: [`the progression check did not run: ${String(proc.stderr).slice(0, 200)}`], keyAreas: {} }; }
    };

    // The placements of the combined run as a whole (randomizer.json): every pickup written once, every item taken once, nothing created or lost, every key placed once.
    const auditPlacements = async (run) => {
      let dir = run.result.packagePath;
      if (!(await readFile(path.join(dir, 'randomizer.json')).then(() => true, () => false))) {
        const attempts = (await readdir(run.workspacePath)).filter((name) => /^combined-\d+$/u.test(name)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
        dir = path.join(run.workspacePath, attempts[0], (await readdir(path.join(run.workspacePath, attempts[0]))).find((name) => name.startsWith('seed_')));
      }
      const document = JSON.parse(await readFile(path.join(dir, 'randomizer.json'), 'utf8'));
      const placements = document.placements ?? document;
      const all = ['items', 'gifts', 'enemyDrops', 'shops', 'bossRewards'].flatMap((list) => placements[list] ?? []);
      const problems = [];
      const targets = new Set(); const sources = new Set(); const itemCount = new Map();
      for (const entry of all) {
        const target = `${entry.targetKind}:${entry.rowId}:${entry.targetSlot}`;
        const source = `${entry.sourceRowId}:${entry.sourceSlot}`;
        if (targets.has(target)) problems.push(`pickup written twice: ${target}`);
        if (sources.has(source)) problems.push(`item taken twice: ${source}`);
        targets.add(target); sources.add(source);
        const item = `${entry.equipType}:${entry.itemId}`;
        itemCount.set(item, (itemCount.get(item) ?? 0) + (entry.itemQuantity ?? 1));
      }
      const keyNames = all.filter((entry) => entry.keyName).map((entry) => entry.keyName);
      if (new Set(keyNames).size !== keyNames.length) problems.push('a key was placed twice');
      return { count: all.length, keys: keyNames.length, problems: problems.slice(0, 3) };
    };

    // Part I audits. The race destinations are re-derived here from the scan of the run itself (the catalog the engine used), independently of the engine code.
    const catalogOf = async (run) => {
      const attempts = (await readdir(run.workspacePath)).filter((name) => /^combined-\d+$/u.test(name)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
      const base = path.join(run.workspacePath, attempts[0]);
      const candidates = [path.join(base, 'dsr-catalog.json'), ...(await readdir(base)).map((name) => path.join(base, name, 'dsr-catalog.json'))];
      for (const candidate of candidates) { try { return JSON.parse(await readFile(candidate, 'utf8')); } catch { /* next */ } }
      return null;
    };
    const embers = new Set([800, 801, 802, 806, 807, 808, 809, 810, 812, 813, 2607, 2018, 2017]);
    const auditRace = async (run, mode) => {
      const catalog = await catalogOf(run);
      const document = await placementsOf(run);
      if (!catalog) return { checked: 0, problems: ['the catalog of the run was not found'] };
      const chestRows = new Set((catalog.treasures ?? []).filter((entry) => entry.model === 'o0110').map((entry) => entry.rowId));
      const keyIds = new Set(model.keys.map((entry) => entry.id));
      const vanillaKeyRows = new Set();
      const noteRows = (rows, pool) => { for (const row of rows) for (const entry of row.entries ?? []) if (keyIds.has(`${entry.equipType}:${entry.itemId}`) || (entry.equipType === 3 && embers.has(entry.itemId))) vanillaKeyRows.add(row.rowId); void pool; };
      noteRows(catalog.worldItemLots ?? [], 'world'); noteRows(catalog.gifts ?? [], 'gift');
      for (const reward of catalog.bossRewards ?? []) for (const lot of reward.lots) noteRows(lot.chain, 'boss');
      const all = ['items', 'gifts', 'enemyDrops', 'shops', 'bossRewards'].flatMap((list) => document.placements[list] ?? []);
      const problems = [];
      let checked = 0;
      for (const placement of all.filter((entry) => entry.keyName || entry.raceItem)) {
        checked += 1;
        const ok = placement.targetPool === 'boss' || chestRows.has(placement.rowId) || vanillaKeyRows.has(placement.rowId);
        if (!ok) problems.push(`${placement.itemName} lies in row ${placement.rowId}, which is no race destination`);
        if (placement.targetPool === 'gift' || placement.targetPool === 'shop' || placement.targetPool === 'enemy') problems.push(`${placement.itemName} lies in a ${placement.targetPool}`);
      }
      const racers = all.filter((entry) => entry.raceItem).length;
      if (racers < 1) problems.push(`no race item was placed in ${mode}`);
      return { checked, raceItems: racers, problems: problems.slice(0, 3) };
    };
    const placementsOf = async (run) => {
      let dir = run.result.packagePath;
      if (!(await readFile(path.join(dir, 'randomizer.json')).then(() => true, () => false))) {
        const attempts = (await readdir(run.workspacePath)).filter((name) => /^combined-\d+$/u.test(name)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
        dir = path.join(run.workspacePath, attempts[0], (await readdir(path.join(run.workspacePath, attempts[0]))).find((name) => name.startsWith('seed_')));
      }
      return JSON.parse(await readFile(path.join(dir, 'randomizer.json'), 'utf8'));
    };
    const auditLaundromat = async (run) => {
      const document = await placementsOf(run);
      let dir = run.result.packagePath;
      if (!(await readFile(path.join(dir, 'randomizer.json')).then(() => true, () => false))) {
        const attempts = (await readdir(run.workspacePath)).filter((name) => /^combined-\d+$/u.test(name)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
        dir = path.join(run.workspacePath, attempts[0], (await readdir(path.join(run.workspacePath, attempts[0]))).find((name) => name.startsWith('seed_')));
      }
      const tree = await readdir(path.join(run.result.packagePath, 'param')).then(() => run.result.packagePath, () => path.join(run.result.packagePath, 'mod'));
      const out = spawnSync(verifier, ['laundromat', tree, path.join(source, 'paramdef', 'paramdef.paramdefbnd.dcx'), path.join(dir, 'randomizer.json'), source], { encoding: 'utf8' });
      void document;
      try { return JSON.parse(out.stdout.trim().split(/\r?\n/u).pop()); } catch { return { plannedRows: 0, problems: [`the laundromat verifier did not run: ${String(out.stderr).slice(0, 200)}`] }; }
    };
    const difficultyOf = async (run) => (await placementsOf(run)).validation?.itemDifficulty ?? null;

    const generate = async (spec, seed, label) => {
      const prepared = prepareRun({ gamePath: game, masterSeed: seed, ...buildSettings(spec.patch) });
      if (!prepared.ok) throw new Error(`${label}: ${prepared.code}: ${prepared.technical}`);
      const workspacePath = path.join(temp, `run-${label}`);
      await mkdir(workspacePath, { recursive: true });
      const audit = [];
      const details = [];
      const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode },
        (event) => {
          if (event.type !== 'log') return;
          if (event.message.includes('audit-events')) audit.push(...event.message.split(/\r?\n/u).filter((line) => line.includes('audit-events')));
          if (/Details:|failed|already used|Conflicting|Duplicate|exception|Exception/u.test(event.message)) details.push(event.message.slice(0, 500));
        },
        { getPristineSource: (p) => authority.getPristineSource(p) });
      return { prepared, workspacePath, audit, details, result };
    };

    for (const spec of caseList.filter((entry) => !args.cases || args.cases.includes(entry.name))) {
      const seeds = args.seeds ?? spec.seeds ?? 12;
      const stats = { case: spec.name, seeds, ok: 0, failed: [], retries: 0, windowOverruns: 0, windowInexact: 0, eventOwnerChecked: 0, eventOwnerFrozen: 0, eventOwnerFailures: [], lotsChecked: 0, raceChecked: 0, raceItems: 0, raceFailures: [], laundromatRows: 0, laundromatFailures: [], difficultyFailures: [], tiers: {}, progressionChecked: 0, placementsChecked: 0, keysPlaced: 0, placementFailures: [], progressionFailures: [], keyAttempts: 0, keysMoved: 0, bossRewardsChecked: 0, lotFailures: [], floorReserved: 0, windowMoved: 0, movedSeeds: [], maxEvents: 0, maxEventsMap: '', nonDeterministic: 0, maxModels: 0, started: Date.now() };
      const indices = args.only ?? Array.from({ length: seeds }, (_, offset) => offset + 1);
      stats.seeds = indices.length;
      for (const index of indices) {
        const seed = deterministicSeed(args.set, spec.name, index);
        const run = await generate(spec, seed, `${spec.name}-${index}`);
        if (!run.result.ok) {
          stats.failed.push(`#${index} ${seed}: ${run.result.code}: ${run.details.slice(-3).join(' || ') || String(run.result.technical ?? run.result.message).slice(0, 200)}`);
        } else {
          stats.ok += 1;
          stats.retries += run.result.manifest.attempts.length - 1;
          const owners = await auditOwners(run);
          stats.eventOwnerChecked += owners.checkedCount;
          stats.eventOwnerFrozen += owners.frozenWithoutEvents ?? 0;
          if (owners.missing.length > 0 || owners.duplicateOwners.length > 0) stats.eventOwnerFailures.push(`#${index}: ${[...owners.missing.slice(0, 2), ...owners.duplicateOwners.slice(0, 1)].join(' | ')}`);
          const lots = await auditLots(run);
          stats.lotsChecked += lots.checkedTargets;
          stats.bossRewardsChecked += lots.bossRewardTargets;
          if (lots.problems.length > 0) stats.lotFailures.push(`#${index}: ${lots.problems.slice(0, 2).join(' | ')}`);
          const items = run.prepared.config.items;
          if (items.laundromat) {
            const laundromat = await auditLaundromat(run);
            stats.laundromatRows += laundromat.plannedRows;
            if (laundromat.problems.length > 0 || laundromat.plannedRows === 0) stats.laundromatFailures.push(`#${index}: ${laundromat.problems.slice(0, 2).join(' | ') || 'no row was planned'}`);
          }
          if (items.itemDifficulty && items.itemDifficulty !== 'fair') {
            const tier = await difficultyOf(run);
            if (!tier) stats.difficultyFailures.push(`#${index}: no tier counters`);
            else for (const [k, v] of Object.entries(tier.matrix)) stats.tiers[k] = (stats.tiers[k] ?? 0) + v;
          }
          if (items.keyPlacement === 'race' || items.keyPlacement === 'race-plus') {
            const race = await auditRace(run, items.keyPlacement);
            stats.raceChecked += race.checked; stats.raceItems += race.raceItems ?? 0;
            if (race.problems.length > 0) stats.raceFailures.push(`#${index}: ${race.problems.join(' | ')}`);
          }
          if (items.keyPlacement && items.keyPlacement !== 'vanilla') {
            const progression = await auditProgression(run);
            stats.progressionChecked += 1;
            const written = await auditPlacements(run);
            stats.placementsChecked += written.count; stats.keysPlaced += written.keys;
            if (written.problems.length > 0) stats.placementFailures.push(`#${index}: ${written.problems.join(' | ')}`);
            if (!progression.worldCompletable || !progression.prisonEscapable) stats.progressionFailures.push(`#${index}: completable=${progression.worldCompletable} prison=${progression.prisonEscapable} unvisited=${(progression.unvisited ?? []).slice(0, 4).join(',')}`);
            const keyReport = run.result.manifest.keyPlacement?.summary;
            if (keyReport) {
              stats.keyAttempts = Math.max(stats.keyAttempts, Number(keyReport.attempts ?? 1));
              stats.keysMoved += Number(keyReport.keysMoved ?? 0);
              // The engine's area of every key against FogMod's own reading of the written lots: two independent derivations of one fact.
              const theirs = progression.keyAreas ?? {};
              // A `randomonly` key (Cast Light, Purple Coward's Crystal) is an ordinary pool item that key placement never moves: the engine keeps it at its vanilla area, FogMod follows it wherever the item shuffle put it.
              const randomOnly = new Set((model.keys ?? []).filter((entry) => (entry.tags ?? []).includes('randomonly')).map((entry) => entry.name));
              const differ = Object.entries(keyReport.keyAreas ?? {}).filter(([key, area]) => !randomOnly.has(key) && theirs[key] !== area).map(([key, area]) => `${key}: engine ${area}, FogMod ${theirs[key]}`);
              if (differ.length > 0 || Object.keys(keyReport.keyAreas ?? {}).length === 0) stats.progressionFailures.push(`#${index}: key areas differ: ${differ.slice(0, 3).join('; ') || 'no key areas reported'}`);
              if (Object.entries(keyReport.keyAreas ?? {}).every(([key, area]) => (model.keys.find((entry) => entry.name === key)?.area) === area)) stats.progressionFailures.push(`#${index}: no key moved`);
            }
          }
          const maps = Object.values(run.result.manifest.enemyPlacement.summary?.maps ?? {});
          stats.maxModels = Math.max(stats.maxModels, ...maps.map((map) => map.finalModels ?? 0), 0);
          for (const line of run.audit) {
            const match = /audit-events (\S+) count=(\d+) consumed=(\d+) base=(\d+) floor=(\S+)/u.exec(line);
            if (!match) continue;
            const [, mapId, count, consumed, base, floor] = match;
            if (Number(count) > stats.maxEvents) { stats.maxEvents = Number(count); stats.maxEventsMap = mapId; }
            // The window was moved off the preferred base because the Bed of Chaos floor event sits inside it (the former failure).
            if (floor !== '-' && Number(base) !== 11419900) { stats.windowMoved += 1; stats.movedSeeds.push(index); }
            if (Number(consumed) > Number(count)) stats.windowOverruns += 1;
            if (Number(consumed) !== Number(count)) stats.windowInexact += 1;
            if (floor !== '-') stats.floorReserved += 1;
          }
          if (index <= args.reruns) {
            const first = await packageDigest(run.result.manifest.files, run.result.packagePath);
            const again = await generate(spec, seed, `${spec.name}-${index}-rerun`);
            const second = again.result.ok ? await packageDigest(again.result.manifest.files, again.result.packagePath) : null;
            if (!second || second.digest !== first.digest) stats.nonDeterministic += 1;
            await rm(again.workspacePath, { recursive: true, force: true });
          }
        }
        if (process.env.DS1TR_MATRIX_KEEP) console.log(`kept: ${run.workspacePath}`); else await rm(run.workspacePath, { recursive: true, force: true });
      }
      stats.seconds = Math.round((Date.now() - stats.started) / 1000);
      delete stats.started;
      summary.push(stats);
      console.log(JSON.stringify(stats));
    }
    const failed = summary.reduce((total, entry) => total + entry.failed.length, 0);
    const bad = summary.reduce((total, entry) => total + entry.windowOverruns + entry.nonDeterministic + entry.eventOwnerFailures.length + entry.lotFailures.length + entry.progressionFailures.length + entry.placementFailures.length + entry.raceFailures.length + entry.laundromatFailures.length + entry.difficultyFailures.length, 0);
    console.log(`MATRIX ${args.set}: ${summary.reduce((total, entry) => total + entry.ok, 0)} ok, ${failed} failed, ${bad} invariant violations (window overruns / non-deterministic reruns / event owner audit)`);
    if (failed > 0 || bad > 0) process.exitCode = 1;
  } finally {
    if (!process.env.DS1TR_MATRIX_KEEP) await rm(temp, { recursive: true, force: true });
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
