import { GENERATION_PROFILE_V1 } from './generation.js';
import type { GenerationSettings } from './generation.js';

/** The test worlds are pinned to this generation profile: a later profile must never silently change them (docs/SMOKE_TESTS.md). */
export const SMOKE_TEST_PROFILE = GENERATION_PROFILE_V1;

/**
 * Runtime smoke tests: small, reproducible worlds for a person who plays the game to check the risky parts of the randomizer.
 * Nothing here is checked automatically - the generator, the data tool and the world randomizer can prove that the files are
 * consistent, not that a boss wakes up. Every scenario is a fixed master seed with fixed settings, so the same Seed Code can be
 * shared between testers and re-created later. The list lives in Advanced and in docs/SMOKE_TESTS.md, never in the normal flow.
 */

/** The settings of a fresh installation (the controls of the main screen before anything is changed). */
export const DEFAULT_GENERATION_SETTINGS: GenerationSettings = {
  items: {
    enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true, guaranteeEarlyWeapon: true,
    balancedEarlyLoot: true, randomizeStartingClass: false, randomizeStartingEquipment: false, randomizeGifts: false,
    randomizeEnemyDrops: false, guaranteedEnemyDrops: false, randomizeShops: false, fashionSouls: true, soulItems: 'shuffled',
    startingItems: 'shield-2h',
  },
  enemies: {
    enabled: true, randomizeRegularEnemies: true, randomizeBosses: true, scaling: 'area', replaceMimics: true,
    replaceRespawningMosquitoes: true, preventSameEnemy: false, pinwheelChaos: false, gwynNerf: 'medium', secondGargoyle: 'replace',
    placement: {
      algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random',
      difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false,
    },
  },
  fog: {
    enabled: true, traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: false, minorPvpFogs: false,
    lordvesselGates: false, randomStart: false, disconnectedFogs: false, noFallBedOfChaos: false, scaling: true, glitchedLogic: false,
    requireLordSouls: true, pacifistMode: false, language: 'ENGLISH',
  },
  output: { includeDlc: true },
};

type DeepPartial<T> = { [K in keyof T]?: T[K] extends readonly unknown[] | null ? T[K] : T[K] extends object ? DeepPartial<T[K]> : T[K] };

export interface SmokeScenario {
  id: string;
  title: string;
  /** Short plain text: what part of the randomizer this world exercises. */
  purpose: string;
  /** Fixed internal master seed (the Seed Code carries it; nobody has to type it). */
  masterSeed: string;
  /** Changes to DEFAULT_GENERATION_SETTINGS. */
  settings: DeepPartial<GenerationSettings>;
  /** Where to go and what to look at in this particular world (specific to the scenario). */
  observe: string[];
}

/** What to check in every smoke-test world. A person ticks these off while playing; nothing marks them as passed. */
export const SMOKE_TEST_CHECKLIST: readonly string[] = [
  'The area loads and the enemy or boss model appears (no invisible body, no T-pose).',
  'The enemy/boss AI wakes up: it notices you, moves and attacks.',
  'Navigation works: it follows you and does not get stuck on walls or stairs.',
  'It stands on the floor: nothing falls through the ground, floats or leaves the arena.',
  'Health behaves: a boss shows its health bar, a normal enemy does not; the amount looks reasonable for the area.',
  'It can be killed, and the death finishes the fight (no invulnerable phase that never ends).',
  'Boss rooms: the fog gate opens afterwards and the encounter does not start a second time.',
  'Progression still works: boss flag, bonfire, doors and key items that depend on this fight.',
  'Rewards are sane: souls, drops and the boss reward are neither missing nor duplicated.',
  'Respawn behaves as expected after resting at a bonfire (regular enemies come back, defeated bosses stay defeated).',
  'The game does not crash when you enter or leave the area, and the map streams in normally.',
];

const bossRoom = ['Enter the boss arena.', 'Defeat the boss and take the fog exit.'];

export const SMOKE_SCENARIOS: readonly SmokeScenario[] = [
  {
    id: 'classic-triple', title: 'Classic full Triple', masterSeed: 'smoke-a-classic',
    purpose: 'Everything on with the default classic placement: items, enemies, bosses and the fog randomizer together.',
    settings: {},
    observe: ['Start a new game and walk through the first areas.', 'Check the fog gates and warps you meet, and the first boss you reach.'],
  },
  {
    id: 'sampling-regular', title: 'Regular enemies, sampling', masterSeed: 'smoke-b-sampling',
    purpose: 'Regular enemies drawn per slot (legacy-style sampling), no bosses, no items, no fog randomizer.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'fits', difficultyMode: 'random-easy-asylum' } },
    },
    observe: ['Walk through the Undead Asylum, Undead Burg and Undead Parish.', 'Look for enemies that stand in the wrong place or do not attack.'],
  },
  {
    id: 'second-gargoyle', title: 'Second Bell Gargoyle', masterSeed: 'smoke-c-gargoyle-7',
    purpose: 'The second Bell Gargoyle is replaced by another boss (the first one also changes).',
    settings: { items: { enabled: false }, fog: { enabled: false }, enemies: { randomizeRegularEnemies: false, secondGargoyle: 'replace', gwynNerf: 'none' } },
    observe: ['Go up to the rooftop of the Undead Parish church: the Bell Gargoyles boss fight.', 'The first body starts the fight; the second body joins when the first drops to about 60% health.'],
  },
  {
    id: 'parish-models', title: 'Undead Parish with many enemy types', masterSeed: 'smoke-d-parish-1',
    purpose: 'The Undead Parish map holding one of the highest numbers of different enemy models the sampler produces.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', typeReplacement: 'disabled' } },
    },
    observe: ['Walk the whole Undead Parish, including the Sunlight Altar side and the church.', 'Look for missing or invisible enemies, textures that do not load, frame drops and crashes.'],
  },
  {
    id: 'boss-sampling', title: 'Boss sampling', masterSeed: 'smoke-e-bosses-1',
    purpose: 'Every single-body boss slot draws its own replacement (bosses can repeat).',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'strong', placement: { algorithm: 'sampling-v1', bossReplacement: 'bosses' } },
    },
    observe: bossRoom,
  },
  {
    id: 'boss-repeat', title: 'The same boss in several places', masterSeed: 'smoke-f-repeat-25',
    purpose: 'A boss that appears in more than one arena in the same world (boss sampling draws with repeats).',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'strong', placement: { algorithm: 'sampling-v1', bossReplacement: 'bosses' } },
    },
    observe: bossRoom,
  },
  {
    id: 'boss-unique', title: 'Unique bosses', masterSeed: 'smoke-g-unique-4',
    purpose: 'Boss sampling where a boss is not used a second time before every boss was used once.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'strong', placement: { algorithm: 'sampling-v1', bossReplacement: 'bosses', uniqueBosses: true } },
    },
    observe: bossRoom,
  },
  {
    id: 'boss-partial', title: 'Some bosses stay as they are', masterSeed: 'smoke-h-partial-1',
    purpose: 'Boss sampling with a 50% replacement chance: about half of the boss slots keep their own boss.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'strong', placement: { algorithm: 'sampling-v1', replacementChance: 50, bossReplacement: 'bosses' } },
    },
    observe: [...bossRoom, 'Also visit a boss that stayed vanilla: its fog gate and fight must still be normal.'],
  },
];

/** DEFAULT_GENERATION_SETTINGS with the scenario's changes applied (objects merge, everything else replaces). */
export function resolveScenarioSettings(scenario: SmokeScenario): GenerationSettings {
  const merge = (base: unknown, patch: unknown): unknown => {
    if (patch === undefined) return structuredClone(base);
    if (patch === null || typeof patch !== 'object' || Array.isArray(patch)) return structuredClone(patch);
    const result: Record<string, unknown> = { ...(base as Record<string, unknown>) };
    for (const [key, value] of Object.entries(patch as Record<string, unknown>)) result[key] = merge((base as Record<string, unknown>)[key], value);
    return result;
  };
  return merge(DEFAULT_GENERATION_SETTINGS, scenario.settings) as GenerationSettings;
}
