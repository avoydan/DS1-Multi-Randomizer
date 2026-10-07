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

/**
 * The base of every test world (docs/SMOKE_TESTS.md): the settings a fresh installation had until Part G, with the classic permutation. The Seed Codes
 * of the test worlds are pinned to this base and never change, so it is frozen here and no longer follows the product default below.
 */
export const SMOKE_TEST_BASE_SETTINGS: GenerationSettings = {
  items: {
    enabled: true, randomizeWorldItems: true, randomizeProtectedItems: false, progressionLogic: true, guaranteeEarlyWeapon: true,
    balancedEarlyLoot: true, randomizeStartingClass: false, randomizeStartingEquipment: false, randomizeGifts: false,
    randomizeEnemyDrops: false, guaranteedEnemyDrops: false, randomizeShops: false, fashionSouls: true, soulItems: 'shuffled',
    startingItems: 'shield-2h', keyPlacement: 'vanilla', randomizeLordvessel: false, randomizeLordSouls: false, itemDifficulty: 'fair', laundromat: false,
  },
  enemies: {
    enabled: true, randomizeRegularEnemies: true, randomizeBosses: true, scaling: 'area', replaceMimics: true,
    replaceRespawningMosquitoes: true, preventSameEnemy: false, pinwheelChaos: false, gwynNerf: 'medium', secondGargoyle: 'replace',
    placement: {
      algorithm: 'permutation-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random',
      difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false,
      bossChanceInBossSlots: 90, hostileNpcs: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false,
    },
  },
  fog: {
    enabled: true, traversableFogs: true, bossFogs: true, areaWarps: true, majorPvpFogs: false, minorPvpFogs: false,
    lordvesselGates: false, randomStart: false, disconnectedFogs: false, noFallBedOfChaos: false, scaling: true, glitchedLogic: false,
    requireLordSouls: true, pacifistMode: false, language: 'ENGLISH',
  },
  output: { includeDlc: true },
};

/**
 * The settings of a fresh installation (the controls of the main screen before anything is changed, and "Restore default settings"): legacy-style
 * sampling (Part G; its options are the legacy defaults), everything else as before. Only NEW configurations start here: a saved setting, a preset, a
 * Seed Code and a run in the history all carry their algorithm explicitly and keep it (`NEUTRAL_ENEMY_PLACEMENT_OPTIONS`, the value an older record
 * is read with, stays the classic permutation and never changes).
 */
export const DEFAULT_GENERATION_SETTINGS: GenerationSettings = {
  ...SMOKE_TEST_BASE_SETTINGS,
  enemies: {
    ...SMOKE_TEST_BASE_SETTINGS.enemies,
    placement: {
      algorithm: 'sampling-v1', replacementChance: 100, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'random-easy-asylum',
      difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false,
      bossChanceInBossSlots: 90, hostileNpcs: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false,
    },
  },
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
  kind: 'roaming-boss' | 'regular-enemy' | 'boss' | 'encounter-body' | 'hostile-npc' | 'item-lot';
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
  /**
   * `item-lot` (Part H): a pickup that holds a given item. `slot` reads "lot <row>", `replacedName` is the item that lies there in the clean game, `name` the item
   * that must lie there now, `model` its game id; `fromSoul` names the boss soul a transposed weapon was made from.
   */
  lotRow?: number;
  itemId?: number;
  itemCategory?: number;
  fromSoul?: string;
  /** `item-lot` of a key (Part H key placement): the key that was moved here from the place where the clean game has it (a text for the tester). */
  movedKeyFrom?: string;
}

/** What a person who played a world in the real game reported. Only a manual result belongs here; static checks never set it. */
export interface SmokeGameplayResult {
  /** `passed`: the whole world was played; `partly-passed`: only the parts the note names were played. */
  result: 'passed' | 'partly-passed';
  date: string;
  note: string;
}

export interface SmokeScenario {
  id: string;
  title: string;
  category: SmokeCategory;
  /** Short plain text: what part of the randomizer this world exercises. */
  purpose: string;
  /** Fixed internal master seed (the Seed Code carries it; nobody has to type it). */
  masterSeed: string;
  /** Changes to SMOKE_TEST_BASE_SETTINGS. */
  settings: DeepPartial<GenerationSettings>;
  /** Extended: where to go and what to look at. Quick: the specific checks of this world, in order. */
  observe: string[];
  /** Quick worlds only: what the generated files must contain (the app shows it; `smokeTestCatalog.mjs verify` checks it). */
  expectations?: SmokeExpectation[];
  /** Quick worlds only: seconds from gaining control to seeing the target, estimated from the distance (about 4 m/s, a detour factor and the cell door), not timed in the game. */
  estimateSeconds?: number;
  /** Present only when a person has played this world in the real game and reported the result. */
  gameplay?: SmokeGameplayResult;
  /** Quick worlds only: further steps after the first check (for example dying once to reset the area). */
  afterwards?: string[];
}

const upText = (up: number) => (Math.abs(up) < 2 ? ', on the same floor as the start' : `, ${Math.round(Math.abs(up))} m ${up > 0 ? 'higher' : 'lower'} than the start`);

/** "Undead Asylum: the Hollow at slot c2500_0006, 8.7 m from where you wake up, on the same floor as the start". */
export function describeSmokeTarget(expectation: SmokeExpectation): string {
  if (expectation.kind === 'item-lot') return `${expectation.mapName}: the pickup where the ${expectation.replacedName} lies in the clean game (item lot ${expectation.lotRow}), ${expectation.meters} m from where you wake up${upText(expectation.up)}`;
  return `${expectation.mapName}: the ${expectation.replacedName} at slot ${expectation.slot}, ${expectation.meters} m from where you wake up${upText(expectation.up)}`;
}

/** "Capra Demon (c2240) replaces the Hollow at slot c2500_0006". */
export function describeSmokeExpected(expectation: SmokeExpectation): string {
  if (expectation.kind === 'item-lot') return `${expectation.name} (${expectation.itemCategory === 0 ? 'weapon' : 'item'} id ${expectation.itemId}) lies where the ${expectation.replacedName} lay${expectation.fromSoul ? `: a weapon made from the ${expectation.fromSoul}, which no longer exists as a soul anywhere in this world` : ''}${expectation.movedKeyFrom ? `: a key that the clean game puts ${expectation.movedKeyFrom}` : ''}`;
  if (expectation.kind === 'encounter-body') return `${expectation.name} (${expectation.model}) takes the place of ${expectation.replacedName} as the body of the boss encounter; the health bar and the name on it stay ${expectation.replacedName}'s, and the fight completes when ${expectation.name} dies`;
  if (expectation.kind === 'hostile-npc') return `${expectation.name} (${expectation.model}) stands in place of the ${expectation.replacedName} at slot ${expectation.slot} as a hostile fighter: no dialogue, no shop, the drops and rewards of that slot`;
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
  'quick-encounter-body': {
    seedPrefix: 'smoke-q7-body',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'none', secondGargoyle: 'keep', placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 100, bossReplacement: 'normals' } },
    },
  },
  'quick-hostile-npc': {
    seedPrefix: 'smoke-q8-npc',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 100, normalReplacement: 'normals', hostileNpcs: true } },
    },
  },
  'quick-transposed-soul': {
    seedPrefix: 'smoke-q9-soul',
    settings: {
      items: { soulItems: 'transposed' }, enemies: { enabled: false }, fog: { enabled: false },
    },
  },
  'quick-key-placement': {
    seedPrefix: 'smoke-q10-keys',
    settings: {
      items: { keyPlacement: 'shuffled' }, enemies: { enabled: false }, fog: { enabled: false },
    },
  },
  'quick-lordvessel': {
    seedPrefix: 'smoke-q11-vessel',
    settings: {
      items: { keyPlacement: 'shuffled', randomizeLordvessel: true }, enemies: { enabled: false }, fog: { enabled: false },
    },
  },
  'quick-lord-soul': {
    seedPrefix: 'smoke-q12-lordsoul',
    settings: {
      items: { keyPlacement: 'shuffled', randomizeLordSouls: true }, enemies: { enabled: false }, fog: { enabled: false },
    },
  },
  'quick-race': {
    seedPrefix: 'smoke-q13-race',
    settings: { items: { keyPlacement: 'race' }, enemies: { enabled: false }, fog: { enabled: false } },
  },
  'quick-race-plus': {
    seedPrefix: 'smoke-q14-raceplus',
    settings: { items: { keyPlacement: 'race-plus' }, enemies: { enabled: false }, fog: { enabled: false } },
  },
  'quick-fair': {
    seedPrefix: 'smoke-q15-diff',
    settings: { items: { itemDifficulty: 'fair' }, enemies: { enabled: false }, fog: { enabled: false } },
  },
  'quick-unfair': {
    seedPrefix: 'smoke-q15-diff',
    settings: { items: { itemDifficulty: 'unfair' }, enemies: { enabled: false }, fog: { enabled: false } },
  },
  'quick-very-unfair': {
    seedPrefix: 'smoke-q15-diff',
    settings: { items: { itemDifficulty: 'very-unfair' }, enemies: { enabled: false }, fog: { enabled: false } },
  },
  'quick-roaming-respawn': {
    seedPrefix: 'smoke-q6-respawn',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: {
        randomizeBosses: false,
        placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 8, normalReplacement: 'bosses', roamingBossesStayDead: false },
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
    expectations: [{ kind: 'roaming-boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0006', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2240', name: 'Capra Demon', meters: 8.7, up: 0, souls: 3000, disableRespawn: false, wakeUpEvent: true }],
    gameplay: { result: 'passed', date: '2026-10-06', note: "The replacement appeared, rendered, woke up, fought and died, it had no boss bar and the souls were as expected. Played before the respawn fix (backend 0.35.1). That fix changed this world's package in one cell only: the roaming rows now carry disableRespawn = 0 where they carried an inherited 1. The world has not been played again." },
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
    gameplay: { result: 'passed', date: '2026-10-06', note: "The souls were as expected and the boss stayed dead after the reset. The respawn fix leaves this world's package byte-identical." },
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
    id: 'quick-encounter-body', category: 'quick', title: 'First boss fight - a normal enemy', masterSeed: 'smoke-q7-body-43',
    purpose: 'A normal enemy takes the place of the first boss: the fight must still start, show its health bar, end when it dies and open the way on.',
    settings: QUICK_SMOKE_RECIPES['quick-encounter-body'].settings,
    expectations: [{ kind: 'encounter-body', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2232_0000', replacedModel: 'c2232', replacedName: 'Asylum Demon', model: 'c2560', name: 'Balder Knight', meters: 22.5, up: 13.3 }],
    gameplay: { result: 'passed', date: '2026-10-07', note: 'Reported by the person who played it: the Asylum Demon encounter with a normal enemy as its body worked in the real game.' },
    estimateSeconds: 90,
    observe: [
      'A Balder Knight stands in the arena where the Asylum Demon normally appears; the Asylum Demon is not there.',
      'The fight starts as the Asylum Demon fight does: the Balder Knight wakes up, moves and attacks (no T-pose, no frozen idle).',
      'The boss health bar appears and it says "Asylum Demon" (the fight keeps the boss name on purpose). The knight has the health of the Asylum Demon (area scaling), so it should take noticeably more hits than a Balder Knight normally does.',
      'Kill it: the bar disappears, the fight is over and the way on opens as after the Asylum Demon (you can leave the Asylum). The rewards of the fight are those of the boss (the drops of the knight itself do not appear).',
      'Nothing freezes or crashes when the fight starts, runs or ends.',
    ],
    afterwards: [
      'Quit to the main menu and continue: the arena stays clear and the fight does not start again.',
      'The time above is for reaching the arena and seeing the replacement; the fight itself is not estimated.',
    ],
  },
  {
    id: 'quick-roaming-respawn', category: 'quick', title: 'Roaming boss - respawns', masterSeed: 'smoke-q6-respawn-331',
    purpose: 'A roaming boss with stay dead switched OFF: after the area is reset it must be back. The counterpart of the souls test, where it must stay dead.',
    settings: QUICK_SMOKE_RECIPES['quick-roaming-respawn'].settings,
    expectations: [{ kind: 'roaming-boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0007', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2240', name: 'Capra Demon', meters: 11.4, up: 0, souls: 3000, disableRespawn: false, wakeUpEvent: true }],
    estimateSeconds: 30,
    observe: [
      'A Capra Demon stands where a Hollow would be; it wakes up, moves and attacks (no boss health bar, no fog gate).',
      'Kill it: you gain 3000 souls (50% of the 6000 that DS1TR lists for that fight).',
      'Control: the other Hollows of the cell block (slots c2500_0006, 8.7 m, and c2500_0005, 33.2 m from the start) are normal Hollows.',
    ],
    afterwards: [
      'Reset the area: the Asylum has no bonfire (none in its map data), so die once (any way). Everything that respawns comes back when you wake up again.',
      'Return to the same place. After resetting the area, this boss should respawn: the Capra Demon MUST be alive again, together with the Hollows.',
    ],
  },
  {
    id: 'quick-roaming-repeated', category: 'quick', title: 'Roaming boss - three at once', masterSeed: 'smoke-q3-repeat-243588',
    purpose: 'The same boss in three neighbouring slots: separate bodies, separate fights, nothing shared between them.',
    settings: QUICK_SMOKE_RECIPES['quick-roaming-repeated'].settings,
    expectations: [
      { kind: 'roaming-boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0006', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2360', name: 'Executioner Smough', meters: 8.7, up: 0, souls: 5000, disableRespawn: false, wakeUpEvent: true },
      { kind: 'roaming-boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0007', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2360', name: 'Executioner Smough', meters: 11.4, up: 0, souls: 5000, disableRespawn: false, wakeUpEvent: true },
      { kind: 'roaming-boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0005', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2360', name: 'Executioner Smough', meters: 33.2, up: 0.9, souls: 5000, disableRespawn: false, wakeUpEvent: true },
    ],
    gameplay: { result: 'passed', date: '2026-10-06', note: "Three independent bosses. Played before the respawn fix (backend 0.35.1). That fix changed this world's package in one cell only: the roaming rows now carry disableRespawn = 0 where they carried an inherited 1. The world has not been played again." },
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
    gameplay: { result: 'passed', date: '2026-10-06', note: "The three replacements appeared, rendered, woke up and fought. The respawn fix leaves this world's package byte-identical." },
    estimateSeconds: 35,
    observe: [
      'Where the first three Hollows stood there are a Balder Knight, an Undead Soldier and an Undead Assassin.',
      'None is in a T-pose or invisible; each wakes up, moves and attacks; none floats or sinks into the floor.',
      'Their size looks right for the cell block (the setting only allows enemies that fit).',
    ],
  },
  {
    id: 'quick-hostile-npc', category: 'quick', title: 'Hostile NPC - Andre as an enemy', masterSeed: 'smoke-q8-npc-562',
    purpose: 'A hostile NPC combatant stands where a regular enemy stood: the real NPCs are untouched, this one only borrows the body and the fighting.',
    settings: QUICK_SMOKE_RECIPES['quick-hostile-npc'].settings,
    expectations: [{ kind: 'hostile-npc', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2500_0006', replacedModel: 'c2500', replacedName: 'Hollow', model: 'c2640', name: 'Andre of Astora', meters: 8.7, up: 0 }],
    estimateSeconds: 30,
    observe: [
      'Andre of Astora (the blacksmith model with his hammer) stands where the first Hollow of the cell block stands; he is not at a forge and he does not talk.',
      'He attacks you as soon as you come near (he is a hostile fighter; there is no talk prompt and no shop) and fights with his own moves: no T-pose, no frozen idle.',
      'His health is that of a Hollow of this area, not that of the real Andre; he dies like a regular enemy.',
      'He gives what a Hollow of that slot gives (a few souls, perhaps a drop); no blacksmith items or unique rewards appear.',
      'Die once or rest and come back: he is there again like every regular enemy (he is not a stay-dead NPC).',
    ],
    afterwards: [
      'The real Andre of Astora in the Undead Parish is not part of this test and is untouched: he still sells and upgrades there.',
      'The other first-visit Hollows are ordinary enemies of this world.',
    ],
  },
  {
    id: 'quick-transposed-soul', category: 'quick', title: 'Soul items Transposed - a boss weapon in your cell', masterSeed: 'smoke-q9-soul-33',
    purpose: 'Soul items "Transposed": the boss souls join the item pool and some become the weapons they can be turned into; here the weapon made from the Soul of Smough lies in the first cell.',
    settings: QUICK_SMOKE_RECIPES['quick-transposed-soul'].settings,
    expectations: [{
      kind: 'item-lot', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'lot 1810080', replacedModel: 'item', replacedName: 'Peculiar Doll', model: '856400', name: "Smough's Hammer",
      meters: 2.8, up: 0.2, lotRow: 1810080, itemId: 856400, itemCategory: 0, fromSoul: 'Soul of Smough',
    }],
    estimateSeconds: 30,
    observe: [
      "You wake up in the cell. The Peculiar Doll lies in the clean game on the floor of this cell; here the same pickup holds Smough's Hammer (a reinforced copy: the weapon id is 856400).",
      "Pick it up: it is a normal weapon item named Smough's Hammer, not a soul and not the Doll. (Do not equip it: it needs far more strength than a new character has.)",
      'The Peculiar Doll is not in the cell any more: it is an ordinary item of the pool and lies in another place of this world.',
    ],
    afterwards: [
      'Nothing else is to be checked in this world. Where the Soul of Smough itself would lie, a weapon (or another pool item) lies now: the soul of this seed no longer exists.',
      'To see the whole picture turn on the spoiler log: the section BOSS REWARDS names every boss soul and what lies in its place.',
    ],
  },
  {
    id: 'quick-key-placement', category: 'quick', title: 'Key placement - a key in your cell', masterSeed: 'smoke-q10-keys-93',
    purpose: 'Key placement "Shuffled": the keys of the world move to other pickups, with a check that every area stays reachable; here the Cage Key lies in the first cell, where the clean game has the Peculiar Doll.',
    settings: QUICK_SMOKE_RECIPES['quick-key-placement'].settings,
    expectations: [{
      kind: 'item-lot', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'lot 1810080', replacedModel: 'item', replacedName: 'Peculiar Doll', model: '2003', name: 'Cage Key',
      meters: 2.8, up: 0.2, lotRow: 1810080, itemId: 2003, itemCategory: 1073741824, movedKeyFrom: "in the upper part of Sen's Fortress",
    }],
    estimateSeconds: 30,
    observe: [
      'You wake up in the cell. In the clean game the Peculiar Doll lies on the floor of this cell; here the same pickup holds the Cage Key.',
      "Pick it up: it is a normal key item named Cage Key (it opens the cage in Sen's Fortress; you will not need it in the Asylum).",
      'The Peculiar Doll is not in the cell any more: it lies in another place of this world, reachable before anything that needs it.',
    ],
    afterwards: [
      'Nothing else is to be checked in this world. The other keys moved too, and the placement guarantees that every area can be reached; to see where every key lies turn on the spoiler log: the section KEY PLACEMENT names each key and the pickup it lies in.',
      'The full check is a run through the game with the keys where the spoiler says: this world only shows that a moved key can be picked up and used.',
    ],
  },
  {
    id: 'quick-lordvessel', category: 'quick', title: 'Lordvessel placement - the Lordvessel in your cell', masterSeed: 'smoke-q11-vessel-354',
    purpose: 'Key placement "Shuffled" with Lordvessel placement on: the Lordvessel joins the key shuffle; here it lies in the first cell, where the clean game has the Peculiar Doll.',
    settings: QUICK_SMOKE_RECIPES['quick-lordvessel'].settings,
    expectations: [{
      kind: 'item-lot', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'lot 1810080', replacedModel: 'item', replacedName: 'Peculiar Doll', model: '2510', name: 'Lordvessel',
      meters: 2.8, up: 0.2, lotRow: 1810080, itemId: 2510, itemCategory: 1073741824, movedKeyFrom: "as the gift of Gwynevere in Anor Londo",
    }],
    estimateSeconds: 30,
    observe: [
      'You wake up in the cell. In the clean game the Peculiar Doll lies on the floor of this cell; here the same pickup holds the Lordvessel.',
      'Pick it up: it is a normal key item named Lordvessel. (It is used at the Firelink Altar; you cannot do that here, and this test does not need it.)',
      'The Peculiar Doll is not in the cell any more: it lies in another place of this world.',
    ],
    afterwards: [
      "Nothing else is to be checked in this world. In Anor Londo, Gwynevere hands over another item of the pool instead of the Lordvessel; that part of the world is far from the start and is not part of this test.",
      'To see every key turn on the spoiler log: the section KEY PLACEMENT names each key and the pickup it lies in.',
    ],
  },
  {
    id: 'quick-lord-soul', category: 'quick', title: 'Lord Soul placement - a Lord Soul in your cell', masterSeed: 'smoke-q12-lordsoul-29',
    purpose: 'Key placement "Shuffled" with Lord Soul placement on: the four Lord Souls join the key shuffle; here the Lord Soul that Gravelord Nito carries lies in the first cell, where the clean game has the Peculiar Doll.',
    settings: QUICK_SMOKE_RECIPES['quick-lord-soul'].settings,
    expectations: [{
      kind: 'item-lot', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'lot 1810080', replacedModel: 'item', replacedName: 'Peculiar Doll', model: '2500', name: 'Lord Soul',
      meters: 2.8, up: 0.2, lotRow: 1810080, itemId: 2500, itemCategory: 1073741824, movedKeyFrom: "as the reward of Gravelord Nito in the Tomb of the Giants",
    }],
    estimateSeconds: 30,
    observe: [
      'You wake up in the cell. In the clean game the Peculiar Doll lies on the floor of this cell; here the same pickup holds a Lord Soul.',
      'Pick it up: it is a normal item named Lord Soul. (Do not use it before you need it at the Kiln.)',
      'The Peculiar Doll is not in the cell any more: it lies in another place of this world.',
    ],
    afterwards: [
      'Nothing else is to be checked in this world. Gravelord Nito, when you reach him, drops another item of the pool instead of the Lord Soul: the Lord Soul itself exists once in this world, in your cell.',
      'To see every key turn on the spoiler log: the section KEY PLACEMENT names each key and the pickup it lies in.',
    ],
  },
  {
    id: 'quick-race', category: 'quick', title: 'Race Mode - a key in your cell', masterSeed: 'smoke-q13-race-4',
    purpose: 'Key placement "Race Mode": the keys and the embers lie only in boss rewards, chests and where keys normally are; here the Blighttown Key lies in the first cell, where the clean game has the Peculiar Doll (the Doll is one of those key places).',
    settings: QUICK_SMOKE_RECIPES['quick-race'].settings,
    expectations: [{
      kind: 'item-lot', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'lot 1810080', replacedModel: 'item', replacedName: 'Peculiar Doll', model: '2007', name: 'Blighttown Key',
      meters: 2.8, up: 0.2, lotRow: 1810080, itemId: 2007, itemCategory: 1073741824, movedKeyFrom: "as the reward of the Gaping Dragon in the Depths",
    }],
    estimateSeconds: 30,
    observe: [
      'You wake up in the cell. In the clean game the Peculiar Doll lies on the floor of this cell; here the same pickup holds the Blighttown Key.',
      'Pick it up: it is a normal key item named Blighttown Key (it opens the way from the Depths to Blighttown; you will not need it in the Asylum).',
      'The Peculiar Doll is not in the cell any more: it lies in another place of this world, in one of the Race Mode places.',
    ],
    afterwards: [
      'Nothing else is to be checked in this world. In Race Mode every key and every ember lies in a boss reward, a chest or a place where a key normally lies; to see where, turn on the spoiler log: the section KEY PLACEMENT names each one.',
    ],
  },
  {
    id: 'quick-race-plus', category: 'quick', title: 'Race Mode + - a key in your cell, a world that needs a skip', masterSeed: 'smoke-q14-raceplus-239',
    purpose: 'Key placement "Race Mode +": as Race Mode, but the world may need skips and glitches; here the Basement Key lies in the first cell, and this world is one whose keys are placed so that a skip of the list (the Capra skip, the Lower Burg skip, the Annex jump or the Seal skip) is needed to complete it.',
    settings: QUICK_SMOKE_RECIPES['quick-race-plus'].settings,
    expectations: [{
      kind: 'item-lot', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'lot 1810080', replacedModel: 'item', replacedName: 'Peculiar Doll', model: '2001', name: 'Basement Key',
      meters: 2.8, up: 0.2, lotRow: 1810080, itemId: 2001, itemCategory: 1073741824, movedKeyFrom: "in the Undead Parish",
    }],
    estimateSeconds: 30,
    observe: [
      'You wake up in the cell. In the clean game the Peculiar Doll lies on the floor of this cell; here the same pickup holds the Basement Key.',
      'Pick it up: it is a normal key item named Basement Key (it opens the Undead Burg basement; you will not need it in the Asylum).',
      'The Peculiar Doll is not in the cell any more: it lies in another place of this world.',
    ],
    afterwards: [
      'Nothing else is to be checked in this world. It exists to show that Race Mode + makes a different world from Race Mode: at least one key lies where only a skip reaches it (the spoiler log names every key). Whether the skips work in the game is not part of this test.',
    ],
  },
  {
    id: 'quick-fair', category: 'quick', title: 'Item difficulty Fair - what lies in your cell', masterSeed: 'smoke-q15-diff-5',
    purpose: 'Item difficulty "Fair": items have an equal chance anywhere. In this world the first cell, an easy place, holds a weapon, the Mace, where the clean game has the Peculiar Doll (one sample of what Fair does; Unfair and Very Unfair have their own tests with the same master seed).',
    settings: QUICK_SMOKE_RECIPES['quick-fair'].settings,
    expectations: [{
      kind: 'item-lot', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'lot 1810080', replacedModel: 'item', replacedName: 'Peculiar Doll', model: '801000', name: 'Mace',
      meters: 2.8, up: 0.2, lotRow: 1810080, itemId: 801000, itemCategory: 0,
    }],
    estimateSeconds: 30,
    observe: [
      'You wake up in the cell. In the clean game the Peculiar Doll lies on the floor of this cell; here the same pickup holds a Mace (a weapon).',
      'Pick it up: it is a normal weapon item named Mace. (You can equip it; it needs only modest strength.)',
      'The Peculiar Doll is not in the cell any more: it is an ordinary item of the pool and lies in another place of this world.',
    ],
    afterwards: [
      'This is one sample. With the same master seed, Unfair and Very Unfair put a Soul of a Lost Undead in this cell instead: an easy place gets an easy item. See the tests quick-unfair and quick-very-unfair.',
    ],
  },
  {
    id: 'quick-unfair', category: 'quick', title: 'Item difficulty Unfair - what lies in your cell', masterSeed: 'smoke-q15-diff-5',
    purpose: 'Item difficulty "Unfair": desirable items are not placed in plain view. The same master seed as the Fair test: the first cell, an easy place, now holds a Soul of a Lost Undead instead of the Mace (one sample of the bias).',
    settings: QUICK_SMOKE_RECIPES['quick-unfair'].settings,
    expectations: [{
      kind: 'item-lot', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'lot 1810080', replacedModel: 'item', replacedName: 'Peculiar Doll', model: '400', name: 'Soul of a Lost Undead',
      meters: 2.8, up: 0.2, lotRow: 1810080, itemId: 400, itemCategory: 1073741824,
    }],
    estimateSeconds: 30,
    observe: [
      'You wake up in the cell. In the clean game the Peculiar Doll lies on the floor of this cell; here the same pickup holds a Soul of a Lost Undead.',
      'Pick it up: it is a normal consumable item named Soul of a Lost Undead.',
      'The Peculiar Doll is not in the cell any more: it lies in another place of this world.',
    ],
    afterwards: [
      'This is one sample of an easy place: the Mace of the Fair test is not here. A single world proves nothing about the distribution; the distribution is measured by the automated sweeps.',
    ],
  },
  {
    id: 'quick-very-unfair', category: 'quick', title: 'Item difficulty Very Unfair - what lies in your cell', masterSeed: 'smoke-q15-diff-5',
    purpose: 'Item difficulty "Very Unfair": desirable items are hidden and rarely in early areas. The same master seed as the Fair test: the first cell, the easiest place of the world, holds a Soul of a Lost Undead.',
    settings: QUICK_SMOKE_RECIPES['quick-very-unfair'].settings,
    expectations: [{
      kind: 'item-lot', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'lot 1810080', replacedModel: 'item', replacedName: 'Peculiar Doll', model: '400', name: 'Soul of a Lost Undead',
      meters: 2.8, up: 0.2, lotRow: 1810080, itemId: 400, itemCategory: 1073741824,
    }],
    estimateSeconds: 30,
    observe: [
      'You wake up in the cell. In the clean game the Peculiar Doll lies on the floor of this cell; here the same pickup holds a Soul of a Lost Undead.',
      'Pick it up: it is a normal consumable item named Soul of a Lost Undead.',
      'The Peculiar Doll is not in the cell any more: it lies in another place of this world.',
    ],
    afterwards: [
      'This is one sample of an easy place. Very Unfair keeps desirable items (good weapons, keys, big souls) out of early places like this one; the early game of this world is poorer than a Fair world of the same master seed.',
    ],
  },
  {
    id: 'quick-boss-asylum-demon', category: 'quick', title: 'Boss sampling - the first boss', masterSeed: 'smoke-q5-demon-9',
    purpose: 'The first boss of the game is replaced by another boss: model, health bar, AI, death and the way on.',
    settings: QUICK_SMOKE_RECIPES['quick-boss-asylum-demon'].settings,
    expectations: [{ kind: 'boss', map: 'm18_01_00_00', mapName: 'Undead Asylum', slot: 'c2232_0000', replacedModel: 'c2232', replacedName: 'Asylum Demon', model: 'c2250', name: 'Taurus Demon', meters: 22.5, up: 13.3 }],
    gameplay: { result: 'passed', date: '2026-10-06', note: "The Asylum Demon encounter completed with the replacement boss. The respawn fix leaves this world's package byte-identical." },
    estimateSeconds: 90,
    observe: [
      'Go to the arena where the Asylum Demon normally appears: a Taurus Demon appears instead, and the boss health bar names it.',
      'It fights (moves, attacks, no T-pose), can be killed and its death ends the encounter.',
      'The way on opens as it does after the Asylum Demon (progression: you can leave the Asylum).',
    ],
    afterwards: ['The time above is for seeing the replacement when the encounter starts. The engine holds this boss back while the side gate is closed and wakes it after the upper boss fog; the whole fight therefore takes longer, and that part is not estimated.'],
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
  {
    id: 'body-burg-fights', category: 'extended', title: 'A normal enemy in the Taurus Demon and Capra Demon fights', masterSeed: 'smoke-o-body-burg-1',
    purpose: 'The second and third early boss fights with a normal enemy as the body: the Taurus Demon bridge and the Capra Demon room in the Undead Burg.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'none', secondGargoyle: 'keep', placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 100, bossReplacement: 'normals' } },
    },
    gameplay: { result: 'partly-passed', date: '2026-10-07', note: 'The Taurus Demon fight with a normal enemy as its body was played and worked. The Capra Demon fight was not played yet.' },
    observe: ['Fight the Taurus Demon on the bridge of the Undead Burg and the Capra Demon below: the bodies of this world are listed in docs/SMOKE_TESTS.md, the health bars keep the boss names, each fight ends when its body dies and its fog gate opens.', 'Both fights must not start again after a reload.'],
  },
  {
    id: 'body-fog', category: 'extended', title: 'A normal enemy as a boss body with the fog randomizer', masterSeed: 'smoke-o-body-fog-1',
    purpose: 'Boss bodies together with the fog randomizer: the boss fog gates, the warps and the boss events are rearranged by the fog randomizer and must still complete.',
    settings: {
      items: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'none', secondGargoyle: 'keep', placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 100, bossReplacement: 'normals' } },
    },
    observe: ['Find the Asylum Demon, Taurus Demon and Capra Demon fights wherever the fog randomizer put them: each must start, show its health bar, end when the body dies and open its fog gate.'],
  },
  {
    id: 'body-roaming', category: 'extended', title: 'Boss bodies and roaming bosses together', masterSeed: 'smoke-o-body-roaming-1',
    purpose: 'Normal enemies in the early boss fights and bosses in regular enemy slots in the same world (the two directions of replacement).',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: {
        randomizeRegularEnemies: true, gwynNerf: 'none', secondGargoyle: 'keep',
        placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 100, normalReplacement: 'both', bossChanceInNormalSlots: 5, bossReplacement: 'normals' },
      },
    },
    observe: ['The first boss fight has a normal enemy as its body, and some regular enemies of the world are bosses.', 'Neither kind interferes with the other: each boss fight completes, each roaming boss fights on its own.'],
  },
  {
    id: 'body-both', category: 'extended', title: 'Bosses or normal enemies in the boss fights', masterSeed: 'smoke-o-body-both-1',
    purpose: 'Boss sampling together with normal-enemy bodies: some fights get a boss, the three early ones may get a normal enemy (Boss replacement: Bosses or normal enemies).',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'strong', secondGargoyle: 'keep', placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 100, bossReplacement: 'both', bossChanceInBossSlots: 40 } },
    },
    observe: ['Visit the boss fights: the three early ones may have a normal enemy as the body (listed in docs/SMOKE_TESTS.md), every other fight has a boss.'],
  },
  {
    id: 'body-gargoyle', category: 'extended', title: 'Boss bodies with the second Bell Gargoyle', masterSeed: 'smoke-o-body-gargoyle-1',
    purpose: 'Normal enemies in the early boss fights together with the second Bell Gargoyle replaced by a boss (the Gargoyles are never given a normal enemy).',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: false, gwynNerf: 'none', secondGargoyle: 'replace', placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 100, bossReplacement: 'normals' } },
    },
    observe: ['Go up to the rooftop of the Undead Parish church: the Bell Gargoyles fight is the one listed in docs/SMOKE_TESTS.md (the classic shuffle still moves the multi-body bosses around), with its second body replaced by a boss as in the Second Bell Gargoyle test. The Gargoyles never get a normal enemy.', 'The Asylum Demon, Taurus Demon and Capra Demon fights have normal enemies as bodies (listed in docs/SMOKE_TESTS.md).'],
  },
  {
    id: 'npc-world', category: 'extended', title: 'Hostile NPCs across the world', masterSeed: 'smoke-o-npc-world-1',
    purpose: 'Hostile Merchants, Andre and Vamos among the regular enemies of the whole game (at most twelve per map), with the real NPCs left alone.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'fits', difficultyMode: 'random-easy-asylum', replacementChance: 100, normalReplacement: 'normals', hostileNpcs: true } },
    },
    observe: ['Walk the Undead Burg and Parish and the Depths: the hostile NPCs listed in docs/SMOKE_TESTS.md are enemies; they wake up, fight and die like regular enemies.', 'The real Undead Merchant, Andre of Astora and the other friendly characters are where they always are and still talk and sell.', 'No more than twelve hostile NPCs stand in one map.'],
  },
  {
    id: 'npc-fog', category: 'extended', title: 'Hostile NPCs with the fog randomizer', masterSeed: 'smoke-o-npc-fog-1',
    purpose: 'Hostile NPCs together with the fog randomizer: the friendly NPCs, their dialogue and the fog gates must all keep working.',
    settings: {
      items: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 60, normalReplacement: 'normals', hostileNpcs: true } },
    },
    observe: ['Play the first area: hostile NPCs are enemies, the fog gates and warps work as in any fog world.', 'Talk to the real Andre and the real Undead Merchant if the fog randomizer lets you reach them: dialogue and shops are unchanged.'],
  },
  {
    id: 'npc-roaming', category: 'extended', title: 'Hostile NPCs and roaming bosses together', masterSeed: 'smoke-o-npc-roaming-1',
    purpose: 'Both kinds of crossing in one world: bosses in regular slots and hostile NPCs in regular slots.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'fits', difficultyMode: 'random', replacementChance: 40, normalReplacement: 'both', bossChanceInNormalSlots: 10, hostileNpcs: true } },
    },
    observe: ['Some regular slots hold a roaming boss, others a hostile NPC; neither interferes with the other and both wake up and fight.'],
  },
  {
    id: 'npc-type', category: 'extended', title: 'Hostile NPCs with Type replacement', masterSeed: 'smoke-o-npc-type-1',
    purpose: 'Type replacement turns every enemy of one kind in a map into one replacement: a hostile NPC may take a whole group (at most twelve per map).',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeBosses: false, placement: { algorithm: 'sampling-v1', fit: 'fits', difficultyMode: 'random-easy-asylum', replacementChance: 100, normalReplacement: 'normals', typeReplacement: 'enabled', hostileNpcs: true } },
    },
    observe: ['Where a whole kind of enemy became one hostile NPC, each of them wakes up and fights; none is passive or stuck.', 'Every one of them gives the drops of its own slot: a kind that was one NPC does not drop one NPC reward per body.'],
  },
  {
    id: 'npc-bosses', category: 'extended', title: 'Hostile NPCs with boss bodies and boss sampling', masterSeed: 'smoke-o-npc-bosses-1',
    purpose: 'Hostile NPCs next to boss sampling and normal enemies as bodies of the early boss fights.',
    settings: {
      items: { enabled: false }, fog: { enabled: false },
      enemies: { randomizeRegularEnemies: true, gwynNerf: 'strong', secondGargoyle: 'keep', placement: { algorithm: 'sampling-v1', fit: 'anywhere', difficultyMode: 'random', replacementChance: 100, normalReplacement: 'normals', bossReplacement: 'both', bossChanceInBossSlots: 60, hostileNpcs: true } },
    },
    observe: ['The boss fights work as in the boss tests; the hostile NPCs around them are ordinary enemies and do not take part in any boss fight.'],
  },
  {
    id: 'laundromat-npcs', category: 'extended', title: 'Laundromat Mixup - NPCs in other armor', masterSeed: 'smoke-p-laundromat-1',
    purpose: 'The NPC characters wear armor pieces other NPCs wear (only their look and armor stats change); nothing you can pick up changes.',
    settings: { items: { laundromat: true }, enemies: { enabled: false }, fog: { enabled: false } },
    observe: [
      'Play the first areas as usual and look at every NPC you meet: each wears armor that some other NPC wears in the normal game, not its own set.',
      'Nothing breaks: the armor shows on the NPC model, the NPC talks, trades and fights as always (a hostile NPC fights in its new armor).',
      'Hollowed NPCs (the undead versions) keep their normal look.',
    ],
    afterwards: ['The exact armor of every NPC row is listed by the static check (SmokeVerify laundromat); what is judged here is only how it looks in the game.'],
  },
];

/** SMOKE_TEST_BASE_SETTINGS with the scenario's changes applied (objects merge, everything else replaces). */
export function resolveScenarioSettings(scenario: SmokeScenario): GenerationSettings {
  const merge = (base: unknown, patch: unknown): unknown => {
    if (patch === undefined) return structuredClone(base);
    if (patch === null || typeof patch !== 'object' || Array.isArray(patch)) return structuredClone(patch);
    const result: Record<string, unknown> = { ...(base as Record<string, unknown>) };
    for (const [key, value] of Object.entries(patch as Record<string, unknown>)) result[key] = merge((base as Record<string, unknown>)[key], value);
    return result;
  };
  return merge(SMOKE_TEST_BASE_SETTINGS, scenario.settings) as GenerationSettings;
}
