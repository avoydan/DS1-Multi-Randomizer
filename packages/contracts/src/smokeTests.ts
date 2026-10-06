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
      bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false,
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

/**
 * QUICK worlds expose one feature within roughly 20-90 seconds of gaining control, at a place stated exactly (slot, distance from where the
 * player wakes up). EXTENDED worlds need progression, travel, or exercise a whole system (Fog, Bell Gargoyle, a map full of bosses).
 */
export type SmokeCategory = 'quick' | 'extended';

/** Where the player wakes up in a new game: c0000_0000 of m18_01_00_00.msb (the Undead Asylum cell). All distances below start here. */
export const SMOKE_START = { map: 'm18_01_00_00', mapName: 'Undead Asylum', x: -13.75, y: 184.75, z: -44.5 } as const;

/** One thing a quick world promises, in the words the tester reads and in the terms a static check of the generated files uses. */
export interface SmokeExpectation {
  kind: 'roaming-boss' | 'regular-enemy' | 'boss';
  map: string;
  mapName: string;
  /** The part name in the map's MSB (`c2500_0006`), the same as the slot id after the colon. */
  slot: string;
  replacedModel: string;
  replacedName: string;
  /** The model that must stand there in the generated package, and its name. */
  model: string;
  name: string;
  /** Straight-line distance on the ground from SMOKE_START in meters, and how much higher (+) or lower (-) the slot is. */
  meters: number;
  up: number;
  /** For a roaming boss: the souls its row must carry, and whether the row must carry the stay-dead bit. */
  souls?: number;
  disableRespawn?: boolean;
  /** A roaming boss gets its own wake-up event; a static check looks for it. */
  wakeUpEvent?: boolean;
}

export interface SmokeScenario {
  id: string;
  title: string;
  category: SmokeCategory;
  /** Short plain text: what part of the randomizer this world exercises. */
  purpose: string;
  /** Fixed internal master seed (the Seed Code carries it; nobody has to type it). */
  masterSeed: string;
  /** Changes to DEFAULT_GENERATION_SETTINGS. */
  settings: DeepPartial<GenerationSettings>;
  /** Extended: where to go and what to look at. Quick: the specific checks of this world, in order. */
  observe: string[];
  /** Quick worlds only: what the generated files must contain (the app shows it; `smokeTestCatalog.mjs verify` checks it). */
  expectations?: SmokeExpectation[];
  /** Quick worlds only: seconds from gaining control to seeing the target, estimated from the distance (about 4 m/s, a detour factor and the cell door), not timed in the game. */
  estimateSeconds?: number;
  /** Quick worlds only: further steps after the first check (for example dying once to reset the area). */
  afterwards?: string[];
}

const upText = (up: number) => (Math.abs(up) < 2 ? ', on the same floor as the start' : `, ${Math.round(Math.abs(up))} m ${up > 0 ? 'higher' : 'lower'} than the start`);

/** "Undead Asylum: the Hollow at slot c2500_0006, 8.7 m from where you wake up, on the same floor as the start". */
export function describeSmokeTarget(expectation: SmokeExpectation): string {
  return `${expectation.mapName}: the ${expectation.replacedName} at slot ${expectation.slot}, ${expectation.meters} m from where you wake up${upText(expectation.up)}`;
}

/** "Capra Demon (c2240) replaces the Hollow at slot c2500_0006". */
export function describeSmokeExpected(expectation: SmokeExpectation): string {
  if (expectation.kind === 'boss') return `${expectation.name} (${expectation.model}) takes the place of ${expectation.replacedName}, the first boss`;
  const extra = expectation.souls === undefined ? '' : `; it carries ${expectation.souls} souls${expectation.disableRespawn ? ' and stays dead' : ''}`;
  return `${expectation.name} (${expectation.model}) replaces the ${expectation.replacedName} at slot ${expectation.slot}${extra}`;
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
const roamingObserve = [
  'Find a boss among the regular enemies: the list below names the area and the enemy it replaced.',
  'It should wake up when you come close, fight like the boss it is (no boss health bar, no fog gate) and be killable; the souls and drops should look sane.',
];

/**
 * The settings of the QUICK smoke-test worlds, kept apart from the scenarios because two tools need them before a scenario exists:
 * `apps/backend/scripts/smokeWorldSearch.mjs` searches master seeds for these exact settings, and the scenario below pins the seed it found.
 * Every quick world is an ordinary world (normal settings, normal master seed, normal Seed Code); nothing about a quick world is forced.
 */
export const QUICK_SMOKE_RECIPES: Readonly<Record<string, { seedPrefix: string; settings: DeepPartial<GenerationSettings> }>> = {
  'quick-roaming-basic': {
    seedPrefix: 'smoke-q1-capra',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 8, normalReplacement: 'bosses' } },
    },
  },
  'quick-roaming-souls': {
    seedPrefix: 'smoke-q2-taurus',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: {
        randomizeBosses: false,
        placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 8, normalReplacement: 'bosses', roamingBossSouls: 100, roamingBossesStayDead: true },
      },
    },
  },
  'quick-roaming-repeated': {
    seedPrefix: 'smoke-q3-repeat',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: {
        randomizeBosses: false,
        placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 50, normalReplacement: 'bosses', typeReplacement: 'enabled-except-roaming-bosses' },
      },
    },
  },
  'quick-sampling-asylum': {
    seedPrefix: 'smoke-q4-sampling',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'fits', difficultyMode: 'random-easy-asylum', replacementChance: 100 } },
    },
  },
  'quick-boss-asylum-demon': {
    seedPrefix: 'smoke-q5-demon',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'strong', secondGargoyle: 'keep', placement: { algorithm: 'sampling-v1', replacementChance: 100, bossReplacement: 'bosses' } },
    },
  },
};

export const SMOKE_SCENARIOS: readonly SmokeScenario[] = [
  // ---- QUICK: the feature is in front of you within about a minute of gaining control (all in the Undead Asylum, first visit) ----------
  {
    id: 'quick-roaming-basic', category: 'quick', title: 'Roaming boss - basic', masterSeed: 'smoke-q1-capra-476',
    purpose: 'A boss stands where a regular enemy stood: the smallest check that a boss works outside its arena.',
    settings: QUICK_SMOKE_RECIPES['quick-roaming-basic'].settings,
    expectations: [{ kind: 'roaming-boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0006', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2240', name: 'Capra Demon', meters: 8.7, up: 0, souls: 3000, wakeUpEvent: true }],
    estimateSeconds: 25,
    observe: [
      'The Capra Demon is there, fully textured (no T-pose, no invisible body) - not a Hollow.',
      'It wakes up when you come close, walks to you and attacks.',
      'It has no boss health bar and there is no fog gate.',
      'You can kill it. Its souls are 3000 (50% of the 6000 that DS1TR lists for that fight).',
      'Nothing freezes or crashes when it appears, fights or dies.',
    ],
  },
  {
    id: 'quick-roaming-souls', category: 'quick', title: 'Roaming boss - souls and respawn', masterSeed: 'smoke-q2-taurus-1193',
    purpose: 'A roaming boss with 100% souls that stays dead: the reward and the "does not come back" setting in one short loop.',
    settings: QUICK_SMOKE_RECIPES['quick-roaming-souls'].settings,
    expectations: [{ kind: 'roaming-boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0006', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2250', name: 'Taurus Demon', meters: 8.7, up: 0, souls: 3000, disableRespawn: true, wakeUpEvent: true }],
    estimateSeconds: 25,
    observe: [
      'A Taurus Demon stands where the first Hollow would be; it wakes up, moves and attacks (no boss health bar, no fog gate).',
      'Note your soul count, then kill the Taurus Demon: you gain exactly 3000 souls (100% of the 3000 that DS1TR lists for that fight).',
      'Control: the other Hollows of the cell block (slots c2500_0007, 11.4 m, and c2500_0005, 33.2 m from the start) are normal Hollows.',
    ],
    afterwards: [
      'Reset the area: the Asylum has no bonfire (none in its map data), so die once (any way). Everything that respawns comes back when you wake up again.',
      'Return to the same place: the Hollows are back, the Taurus Demon is NOT (the stay-dead setting; not yet confirmed in the game).',
    ],
  },
  {
    id: 'quick-roaming-repeated', category: 'quick', title: 'Roaming boss - three at once', masterSeed: 'smoke-q3-repeat-243588',
    purpose: 'The same boss in three neighbouring slots: separate bodies, separate fights, nothing shared between them.',
    settings: QUICK_SMOKE_RECIPES['quick-roaming-repeated'].settings,
    expectations: [
      { kind: 'roaming-boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0006', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2360', name: 'Executioner Smough', meters: 8.7, up: 0, souls: 5000, wakeUpEvent: true },
      { kind: 'roaming-boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0007', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2360', name: 'Executioner Smough', meters: 11.4, up: 0, souls: 5000, wakeUpEvent: true },
      { kind: 'roaming-boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0005', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2360', name: 'Executioner Smough', meters: 33.2, up: 0.9, souls: 5000, wakeUpEvent: true },
    ],
    estimateSeconds: 35,
    observe: [
      'Three Executioner Smoughs stand in the cell block, 8.7, 11.4 and 33.2 m from the start.',
      'Each wakes up, moves and attacks on its own; killing one does not touch the others.',
      'Each gives its own 5000 souls (50% of 10000) and none has a boss bar.',
      'Nothing else in the Asylum is a boss on this first visit (other roaming bosses of this world are in the part you only reach on the second visit).',
    ],
  },
  {
    id: 'quick-sampling-asylum', category: 'quick', title: 'Regular enemies - first three change', masterSeed: 'smoke-q4-sampling-3',
    purpose: 'Legacy-style sampling at runtime: the first Hollows are different enemies and behave like enemies.',
    settings: QUICK_SMOKE_RECIPES['quick-sampling-asylum'].settings,
    expectations: [
      { kind: 'regular-enemy', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0006', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2560', name: 'Balder Knight', meters: 8.7, up: 0 },
      { kind: 'regular-enemy', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0007', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2550', name: 'Undead Soldier', meters: 11.4, up: 0 },
      { kind: 'regular-enemy', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0005', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2520', name: 'Undead Assassin', meters: 33.2, up: 0.9 },
    ],
    estimateSeconds: 35,
    observe: [
      'Where the first three Hollows stood there are a Balder Knight, an Undead Soldier and an Undead Assassin.',
      'None is in a T-pose or invisible; each wakes up, moves and attacks; none floats or sinks into the floor.',
      'Their size looks right for the cell block (the setting only allows enemies that fit).',
    ],
  },
  {
    id: 'quick-boss-asylum-demon', category: 'quick', title: 'Boss sampling - the first boss', masterSeed: 'smoke-q5-demon-9',
    purpose: 'The first boss of the game is replaced by another boss: model, health bar, AI, death and the way on.',
    settings: QUICK_SMOKE_RECIPES['quick-boss-asylum-demon'].settings,
    expectations: [{ kind: 'boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2232_0000', replacedModel: 'c2232', replacedName: 'Asylum Demon', model: 'c2250', name: 'Taurus Demon', meters: 22.5, up: 13.3 }],
    estimateSeconds: 90,
    observe: [
      'Climb to the balcony where the Asylum Demon normally drops in: a Taurus Demon arrives instead, with its own name on the boss health bar.',
      'It fights (moves, attacks, no T-pose), can be killed and its death ends the encounter.',
      'The way on opens as it does after the Asylum Demon (progression: you can leave the Asylum).',
    ],
    afterwards: ['Two moments matter: the replacement becomes visible when the encounter starts (about a minute), and the fight can be completed only once you have taken the upper route (about three more).'],
  },
  {
    id: 'classic-triple', category: 'extended', title: 'Classic full Triple', masterSeed: 'smoke-a-classic',
    purpose: 'Everything on with the default classic placement: items, enemies, bosses and the fog randomizer together.',
    settings: {},
    observe: ['Start a new game and walk through the first areas.', 'Check the fog gates and warps you meet, and the first boss you reach.'],
  },
  {
    id: 'sampling-regular', category: 'extended', title: 'Regular enemies, sampling (Asylum to Parish)', masterSeed: 'smoke-b-sampling',
    purpose: 'Regular enemies drawn per slot (legacy-style sampling), no bosses, no items, no fog randomizer.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'fits', difficultyMode: 'random-easy-asylum' } },
    },
    observe: ['Walk through the Undead Asylum, Undead Burg and Undead Parish.', 'Look for enemies that stand in the wrong place or do not attack.'],
  },
  {
    id: 'second-gargoyle', category: 'extended', title: 'Second Bell Gargoyle', masterSeed: 'smoke-c-gargoyle-7',
    purpose: 'The second Bell Gargoyle is replaced by another boss (the first one also changes).',
    settings: { items: { enabled: false }, fog: { enabled: false }, enemies: { randomizeRegularEnemies: false, secondGargoyle: 'replace', gwynNerf: 'none' } },
    observe: ['Go up to the rooftop of the Undead Parish church: the Bell Gargoyles boss fight.', 'The first body starts the fight; the second body joins when the first drops to about 60% health.'],
  },
  {
    id: 'parish-models', category: 'extended', title: 'Undead Parish with many enemy types', masterSeed: 'smoke-d-parish-1',
    purpose: 'The Undead Parish map holding one of the highest numbers of different enemy models the sampler produces.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', typeReplacement: 'disabled' } },
    },
    observe: ['Walk the whole Undead Parish, including the Sunlight Altar side and the church.', 'Look for missing or invisible enemies, textures that do not load, frame drops and crashes.'],
  },
  {
    id: 'boss-sampling', category: 'extended', title: 'Boss sampling (every arena)', masterSeed: 'smoke-e-bosses-1',
    purpose: 'Every single-body boss slot draws its own replacement (bosses can repeat).',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'strong', placement: { algorithm: 'sampling-v1', bossReplacement: 'bosses' } },
    },
    observe: bossRoom,
  },
  {
    id: 'boss-repeat', category: 'extended', title: 'The same boss in several places', masterSeed: 'smoke-f-repeat-25',
    purpose: 'A boss that appears in more than one arena in the same world (boss sampling draws with repeats).',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'strong', placement: { algorithm: 'sampling-v1', bossReplacement: 'bosses' } },
    },
    observe: bossRoom,
  },
  {
    id: 'boss-unique', category: 'extended', title: 'Unique bosses', masterSeed: 'smoke-g-unique-4',
    purpose: 'Boss sampling where a boss is not used a second time before every boss was used once.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'strong', placement: { algorithm: 'sampling-v1', bossReplacement: 'bosses', uniqueBosses: true } },
    },
    observe: bossRoom,
  },
  {
    id: 'boss-partial', category: 'extended', title: 'Some bosses stay as they are', masterSeed: 'smoke-h-partial-1',
    purpose: 'Boss sampling with a 50% replacement chance: about half of the boss slots keep their own boss.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'strong', placement: { algorithm: 'sampling-v1', replacementChance: 50, bossReplacement: 'bosses' } },
    },
    observe: [...bossRoom, 'Also visit a boss that stayed vanilla: its fog gate and fight must still be normal.'],
  },
  {
    id: 'roaming-repeat', category: 'extended', title: 'Type replacement + roaming bosses (Undead Burg and later)', masterSeed: 'smoke-j-roaming-repeat-1',
    purpose: 'With Type replacement, every enemy of one kind in a map is replaced by the same boss, so one boss stands in several places of an area.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'fits', replacementChance: 20, normalReplacement: 'bosses', typeReplacement: 'enabled' } },
    },
    observe: roamingObserve,
  },
  {
    id: 'roaming-gargoyle', category: 'extended', title: 'Roaming bosses with the second Bell Gargoyle', masterSeed: 'smoke-l-roaming-gargoyle-1',
    purpose: 'Bosses in regular slots together with a replaced second Bell Gargoyle and boss sampling in the boss arenas.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { secondGargoyle: 'replace', gwynNerf: 'strong', placement: { algorithm: 'sampling-v1', fit: 'fits', replacementChance: 30, normalReplacement: 'both', bossReplacement: 'bosses' } },
    },
    observe: ['Go up to the rooftop of the Undead Parish church: the Bell Gargoyles boss fight and the roaming bosses around it.', ...roamingObserve],
  },
  {
    id: 'roaming-fog', category: 'extended', title: 'Roaming bosses with the fog randomizer', masterSeed: 'smoke-m-roaming-fog-1',
    purpose: 'Bosses in regular slots with the fog randomizer on (fog gates, warps and boss fogs are moved too).',
    settings: {
      items: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'fits', replacementChance: 30, normalReplacement: 'both' } },
    },
    observe: [...roamingObserve, 'Check that fog gates and warps still lead where the fog randomizer says, and that no roaming boss blocks a gate.'],
  },
  {
    id: 'roaming-parish', category: 'extended', title: 'Undead Parish full of bosses', masterSeed: 'smoke-n-roaming-parish-1',
    purpose: 'Every regular slot that can take a boss gets one (at most ten per map): the highest load of different models a roaming world can put on one map.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', normalReplacement: 'bosses' } },
    },
    observe: ['Walk the whole Undead Parish, including the church and the Sunlight Altar side.', ...roamingObserve],
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
