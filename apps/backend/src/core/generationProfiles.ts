import { CURRENT_GENERATION_PROFILE, GENERATION_PROFILE_V1 } from '@ds1tr/contracts';
import { SEED_DERIVATION_V1, type SeedDerivation } from './seeds.js';

/**
 * Generation profiles (docs/GENERATION_COMPATIBILITY.md).
 *
 * A profile is an immutable compatibility contract: the exact deterministic components that turn a master seed and a canonical
 * configuration into a world. The application version is deliberately absent. A Seed Code names its profile, and any
 * application that still lists the profile reproduces the same world.
 *
 * RULE: once a profile id has appeared in a shared Seed Code its entry below is frozen. A change that alters the output of
 * existing seeds (placement, candidate order, RNG call order, retry semantics, Fog transformation, ...) gets a NEW profile; the
 * old entry stays and keeps selecting the old behavior. Adding a version to an allow-list is only allowed after the golden
 * fixtures prove the output is identical (apps/backend/fixtures/golden).
 */
export interface GenerationProfile {
  /** Public identifier carried by Seed Codes. Never contains an application version. */
  readonly id: string;
  readonly title: string;
  /** Named seed derivation (master seed -> combined seed and Fog seed per attempt, retries included). */
  readonly seedDerivation: SeedDerivation;
  /** Bounded retries; they are part of the semantics because every retry draws different seeds. */
  readonly retryBounds: { readonly maxCombinedAttempts: number; readonly maxFogAttempts: number };
  /** Combined engine (items, enemies, bosses): the RANDOMIZER_VERSION values that produce this profile's output. */
  readonly combinedEngine: { readonly randomizerVersions: readonly string[] };
  /** Fog engine: FogHost versions with identical output, the pinned FogMod commit, and the world data set it reads. */
  readonly fogEngine: { readonly hostVersions: readonly string[]; readonly fogModCommit: string; readonly worldDataId: string };
  /** Item shuffle rules for the world randomizer (docs/FOG_ITEM_TRACKING.md) that this profile applies when Fog is on. */
  readonly fogItemTracking: 'fog-item-tracking-v1';
  /**
   * Completes the settings of a Seed Code created by this profile when the running application knows options that did not
   * exist then. Each such option must be given the value that reproduces the old behavior (never the current default).
   */
  readonly completeSettings: (raw: unknown) => unknown;
}

const v1: GenerationProfile = Object.freeze({
  id: GENERATION_PROFILE_V1,
  title: 'First generation profile (desktop 0.16.1 behavior)',
  seedDerivation: SEED_DERIVATION_V1,
  retryBounds: Object.freeze({ maxCombinedAttempts: 3, maxFogAttempts: 5 }),
  combinedEngine: Object.freeze({ randomizerVersions: Object.freeze(['0.5.66']) }),
  fogEngine: Object.freeze({
    hostVersions: Object.freeze(['0.5.0']),
    fogModCommit: '6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f',
    worldDataId: 'f394fa536ba9b66d',
  }),
  fogItemTracking: 'fog-item-tracking-v1',
  // v1 knows every option that exists today; a future option gets its v1 value here.
  completeSettings: (raw: unknown) => raw,
});

const registry: ReadonlyMap<string, GenerationProfile> = new Map([[v1.id, v1]]);

export function resolveGenerationProfile(id: string): GenerationProfile | null {
  return registry.get(id) ?? null;
}

export function supportedGenerationProfiles(): string[] {
  return [...registry.keys()];
}

/** The profile a normal new run uses. */
export function currentGenerationProfile(): GenerationProfile {
  const profile = registry.get(CURRENT_GENERATION_PROFILE);
  if (!profile) throw new Error(`The current generation profile ${CURRENT_GENERATION_PROFILE} is not registered.`);
  return profile;
}

export interface ObservedComponents {
  combinedRandomizerVersion?: string;
  fogHostVersion?: string;
  fogModCommit?: string;
  /** Folder name of the prepared world data (its id); absent when a developer override supplies the data. */
  worldDataId?: string;
}

/**
 * The guard that makes drift impossible to miss: a component that is not on the profile's list would produce a different
 * world under the same Seed Code, so the run stops instead.
 */
export function checkProfileComponents(profile: GenerationProfile, observed: ObservedComponents): string | null {
  if (observed.combinedRandomizerVersion !== undefined && !profile.combinedEngine.randomizerVersions.includes(observed.combinedRandomizerVersion)) {
    return `combined engine ${observed.combinedRandomizerVersion} is not on the list of ${profile.id} (${profile.combinedEngine.randomizerVersions.join(', ')})`;
  }
  if (observed.fogHostVersion !== undefined && !profile.fogEngine.hostVersions.includes(observed.fogHostVersion)) {
    return `FogHost ${observed.fogHostVersion} is not on the list of ${profile.id} (${profile.fogEngine.hostVersions.join(', ')})`;
  }
  if (observed.fogModCommit !== undefined && observed.fogModCommit !== profile.fogEngine.fogModCommit) {
    return `FogMod ${observed.fogModCommit} differs from ${profile.id} (${profile.fogEngine.fogModCommit})`;
  }
  if (observed.worldDataId !== undefined && observed.worldDataId !== profile.fogEngine.worldDataId) {
    return `world data ${observed.worldDataId} differs from ${profile.id} (${profile.fogEngine.worldDataId})`;
  }
  return null;
}
