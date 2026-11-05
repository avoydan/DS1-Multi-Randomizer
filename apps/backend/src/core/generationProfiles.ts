import { CURRENT_GENERATION_PROFILE, GENERATION_PROFILE_V1, GENERATION_PROFILE_V2, GENERATION_PROFILE_V3, GENERATION_PROFILE_V4, GENERATION_PROFILE_V5, GENERATION_PROFILE_V6, GENERATION_PROFILE_V7 } from '@ds1tr/contracts';
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
  /** Which audited destination handling the combined engine applies to roaming bosses. */
  readonly roamingBossDestinationPolicy: 'protected-v1' | 'safe-handlers-v2';
  /** Frozen roaming-boss candidate and fit-class rules; not a user setting. */
  readonly roamingBossFitPolicy: 'legacy-v1' | 'owner-approved-v1';
  /** Whether this profile selects the legacy pinned FogMod annotation source or the project Compact catalog. */
  readonly fogAnnotationSource: 'legacy-fogmod' | 'compact-v1' | 'compact-v2' | 'compact-v3' | 'upstream-v7';
  /** Fog engine: FogHost versions with identical output, the pinned FogMod commit, and the world data set it reads. */
  readonly fogEngine: {
    readonly hostVersions: readonly string[];
    readonly fogModCommit: string;
    readonly worldDataId: string;
    readonly compactCatalogVersion?: string;
    readonly compactCatalogFingerprint?: string;
    /** ds1tr-gen-v7 only: SHA-256 (LF-normalized) of the bundled upstream fog.txt that defines the whole physical Fog model. */
    readonly upstreamAnnotationSha256?: string;
    /** ds1tr-gen-v7 only: the version of the local rebuild of the events, talk scripts and layouts from the installed game. */
    readonly localReconstructionVersion?: string;
  };
  /**
   * Item shuffle rules for the world randomizer (docs/FOG_ITEM_TRACKING.md) that this profile applies when Fog is on. `upstream-fog-item-tracking-v7` is derived at run time from the
   * upstream fog.txt itself (every KeyItem and every LotLocation), never from a catalog or a frozen snapshot.
   */
  readonly fogItemTracking: 'fog-item-tracking-v1' | 'compact-fog-item-tracking-v1' | 'upstream-fog-item-tracking-v7';
  /**
   * Completes the settings of a Seed Code created by this profile when the running application knows options that did not
   * exist then. Each such option must be given the value that reproduces the old behavior (never the current default).
   */
  readonly completeSettings: (raw: unknown) => unknown;
}

/**
 * Wire/runtime contract selected solely from a validated generation profile.
 * The schema is deliberately kept separate from the application's own version.
 */
export interface FogHostRuntimeContract {
  readonly hostVersion: '0.5.0' | '0.6.0' | '0.7.3';
  readonly schemaVersion: 2 | 3;
  readonly annotationSource: 'legacy-fogmod' | 'compact-v1' | 'compact-v2' | 'compact-v3' | 'upstream-v7';
}

const v1: GenerationProfile = Object.freeze({
  id: GENERATION_PROFILE_V1,
  title: 'First generation profile (desktop 0.16.1 behavior)',
  seedDerivation: SEED_DERIVATION_V1,
  retryBounds: Object.freeze({ maxCombinedAttempts: 3, maxFogAttempts: 5 }),
  combinedEngine: Object.freeze({ randomizerVersions: Object.freeze(['0.5.66']) }),
  roamingBossDestinationPolicy: 'protected-v1',
  roamingBossFitPolicy: 'legacy-v1',
  fogAnnotationSource: 'legacy-fogmod',
  fogEngine: Object.freeze({
    // Legacy profiles remain pinned to the FogHost version verified by their frozen outputs.
    hostVersions: Object.freeze(['0.5.0']),
    fogModCommit: '6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f',
    worldDataId: 'f394fa536ba9b66d',
  }),
  fogItemTracking: 'fog-item-tracking-v1',
  // v1 knows every option that exists today; a future option gets its v1 value here.
  completeSettings: (raw: unknown) => raw,
});

const v2: GenerationProfile = Object.freeze({
  id: GENERATION_PROFILE_V2,
  title: 'Roaming boss destination safety fixes',
  seedDerivation: SEED_DERIVATION_V1,
  retryBounds: Object.freeze({ maxCombinedAttempts: 3, maxFogAttempts: 5 }),
  combinedEngine: Object.freeze({ randomizerVersions: Object.freeze(['0.5.66']) }),
  roamingBossDestinationPolicy: 'safe-handlers-v2',
  roamingBossFitPolicy: 'legacy-v1',
  fogAnnotationSource: 'legacy-fogmod',
  fogEngine: v1.fogEngine,
  fogItemTracking: v1.fogItemTracking,
  completeSettings: v1.completeSettings,
});

const v3: GenerationProfile = Object.freeze({
  id: GENERATION_PROFILE_V3,
  title: 'Compact Fog catalog and item tracking',
  seedDerivation: SEED_DERIVATION_V1,
  retryBounds: Object.freeze({ maxCombinedAttempts: 3, maxFogAttempts: 5 }),
  combinedEngine: Object.freeze({ randomizerVersions: Object.freeze(['0.5.66']) }),
  roamingBossDestinationPolicy: 'safe-handlers-v2',
  roamingBossFitPolicy: 'legacy-v1',
  fogAnnotationSource: 'compact-v1',
  fogEngine: Object.freeze({
    // 0.7.3 is the only Compact-capable host the application ships since the V5 host replaced 0.6.0. Its compact-v1 output
    // was proven byte-identical to 0.6.0 (docs/GENERATION_COMPATIBILITY.md, "V3/V4 Fog host allow-list"); without it every
    // v3/v4 Seed Code with Fog enabled is refused with GENERATION_PROFILE_MISMATCH.
    hostVersions: Object.freeze(['0.6.0', '0.7.3']),
    fogModCommit: '6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f',
    worldDataId: 'compact-runtime-offline-v1',
    compactCatalogVersion: 'ds1tr-compact-full-fog-v1',
    compactCatalogFingerprint: '8c03fd3de1cd7c772d5c933753ae54eab35f9e8b70a1aef2954322330d0002d4',
  }),
  fogItemTracking: 'compact-fog-item-tracking-v1',
  completeSettings: v2.completeSettings,
});

const v4: GenerationProfile = Object.freeze({
  id: GENERATION_PROFILE_V4,
  title: 'Owner-approved roaming boss fit and Compact Fog',
  seedDerivation: SEED_DERIVATION_V1,
  retryBounds: Object.freeze({ maxCombinedAttempts: 3, maxFogAttempts: 5 }),
  combinedEngine: Object.freeze({ randomizerVersions: Object.freeze(['0.5.66']) }),
  roamingBossDestinationPolicy: 'safe-handlers-v2',
  roamingBossFitPolicy: 'owner-approved-v1',
  fogAnnotationSource: 'compact-v1',
  fogEngine: v3.fogEngine,
  fogItemTracking: 'compact-fog-item-tracking-v1',
  completeSettings: v3.completeSettings,
});

const v5: GenerationProfile = Object.freeze({
  id: GENERATION_PROFILE_V5,
  title: 'Corrected Compact Fog and verified Ceaseless landing',
  seedDerivation: SEED_DERIVATION_V1,
  retryBounds: Object.freeze({ maxCombinedAttempts: 3, maxFogAttempts: 5 }),
  combinedEngine: Object.freeze({ randomizerVersions: Object.freeze(['0.5.66']) }),
  roamingBossDestinationPolicy: 'safe-handlers-v2',
  roamingBossFitPolicy: 'owner-approved-v1',
  fogAnnotationSource: 'compact-v2',
  fogEngine: Object.freeze({
    hostVersions: Object.freeze(['0.7.3']),
    fogModCommit: v4.fogEngine.fogModCommit,
    worldDataId: 'ef625777d6aa5abf',
    compactCatalogVersion: 'ds1tr-compact-full-fog-v2',
    compactCatalogFingerprint: '73f3c842d51490d45a836a489105b37f57d702eaa2ffda084f42781e5aaef00f',
  }),
  fogItemTracking: 'compact-fog-item-tracking-v1',
  completeSettings: v4.completeSettings,
});

const v6: GenerationProfile = Object.freeze({
  id: GENERATION_PROFILE_V6,
  title: 'Start-reachability Fog: physically sound Asylum start, no Ceaseless post-boss check',
  seedDerivation: SEED_DERIVATION_V1,
  retryBounds: Object.freeze({ maxCombinedAttempts: 3, maxFogAttempts: 5 }),
  combinedEngine: Object.freeze({ randomizerVersions: Object.freeze(['0.5.66']) }),
  roamingBossDestinationPolicy: 'safe-handlers-v2',
  roamingBossFitPolicy: 'owner-approved-v1',
  fogAnnotationSource: 'compact-v3',
  fogEngine: Object.freeze({
    // The shipped host keeps HostVersion 0.7.3; v6 is selected by its own annotation source and catalog fingerprint, so no earlier profile's
    // allow-list changes (a v6 request is refused by any older host as an unknown annotation source).
    hostVersions: Object.freeze(['0.7.3']),
    fogModCommit: v5.fogEngine.fogModCommit,
    worldDataId: 'de58b0aaa51c8ce9',
    compactCatalogVersion: 'ds1tr-compact-full-fog-v3',
    compactCatalogFingerprint: 'f2775c4a80f0ee41e4ccc0bf50fff4ea3714e11da73339c140cb3d6c6fe254a5',
  }),
  fogItemTracking: 'compact-fog-item-tracking-v1',
  completeSettings: v5.completeSettings,
});

const UPSTREAM_V7_WORLD_DATA_ID = 'bb35e3e0b8feeb75';

const v7: GenerationProfile = Object.freeze({
  id: GENERATION_PROFILE_V7,
  title: 'Upstream FogMod world model: original fog.txt semantics, no inferred routes, offline in every distribution',
  seedDerivation: SEED_DERIVATION_V1,
  retryBounds: Object.freeze({ maxCombinedAttempts: 3, maxFogAttempts: 5 }),
  combinedEngine: Object.freeze({ randomizerVersions: Object.freeze(['0.5.66']) }),
  roamingBossDestinationPolicy: 'safe-handlers-v2',
  roamingBossFitPolicy: 'owner-approved-v1',
  fogAnnotationSource: 'upstream-v7',
  fogEngine: Object.freeze({
    // HostVersion stays 0.7.3: v7 is selected by its own annotation source, so no earlier profile's allow-list changes (an older host refuses it as an unknown source).
    hostVersions: Object.freeze(['0.7.3']),
    fogModCommit: v6.fogEngine.fogModCommit,
    // Identity of the prepared data set (profile, upstream fog.txt hash, local reconstruction version, every file hash); apps/backend/src/adapters/worldData.ts computes it and a test pins it.
    worldDataId: UPSTREAM_V7_WORLD_DATA_ID,
    upstreamAnnotationSha256: '77a10b4015c51bdfbe1499f38344c85540a63bfa1ef6370ad9c1ebdc9a2d2af6',
    localReconstructionVersion: 'ds1tr-local-fog-data-v1+upstream-layout',
  }),
  fogItemTracking: 'upstream-fog-item-tracking-v7',
  completeSettings: v6.completeSettings,
});

const registry: ReadonlyMap<string, GenerationProfile> = new Map([[v1.id, v1], [v2.id, v2], [v3.id, v3], [v4.id, v4], [v5.id, v5], [v6.id, v6], [v7.id, v7]]);

export function resolveGenerationProfile(id: string): GenerationProfile | null {
  return registry.get(id) ?? null;
}

export function supportedGenerationProfiles(): string[] {
  return [...registry.keys()];
}

/** Returns null for a profile whose frozen Fog components do not form a supported runtime contract. */
export function fogHostRuntimeForProfile(
  profile: Pick<GenerationProfile, 'fogEngine' | 'fogAnnotationSource' | 'fogItemTracking'>,
): FogHostRuntimeContract | null {
  const versions = profile.fogEngine.hostVersions;
  if (profile.fogAnnotationSource === 'legacy-fogmod'
    && profile.fogItemTracking === 'fog-item-tracking-v1'
    && versions.includes('0.5.0')) {
    return Object.freeze({ hostVersion: '0.5.0', schemaVersion: 2, annotationSource: 'legacy-fogmod' });
  }
  if (profile.fogAnnotationSource === 'compact-v1'
    && profile.fogItemTracking === 'compact-fog-item-tracking-v1'
    && versions.includes('0.6.0')
    && typeof profile.fogEngine.compactCatalogVersion === 'string'
    && typeof profile.fogEngine.compactCatalogFingerprint === 'string') {
    return Object.freeze({ hostVersion: '0.6.0', schemaVersion: 3, annotationSource: 'compact-v1' });
  }
  if (profile.fogAnnotationSource === 'compact-v3'
    && profile.fogItemTracking === 'compact-fog-item-tracking-v1'
    && versions.includes('0.7.3')
    && typeof profile.fogEngine.compactCatalogVersion === 'string'
    && typeof profile.fogEngine.compactCatalogFingerprint === 'string') {
    return Object.freeze({ hostVersion: '0.7.3', schemaVersion: 3, annotationSource: 'compact-v3' });
  }
  if (profile.fogAnnotationSource === 'upstream-v7'
    && profile.fogItemTracking === 'upstream-fog-item-tracking-v7'
    && versions.includes('0.7.3')
    && typeof profile.fogEngine.upstreamAnnotationSha256 === 'string'
    && typeof profile.fogEngine.localReconstructionVersion === 'string') {
    return Object.freeze({ hostVersion: '0.7.3', schemaVersion: 3, annotationSource: 'upstream-v7' });
  }
  if (profile.fogAnnotationSource === 'compact-v2'
    && profile.fogItemTracking === 'compact-fog-item-tracking-v1'
    && versions.includes('0.7.3')
    && typeof profile.fogEngine.compactCatalogVersion === 'string'
    && typeof profile.fogEngine.compactCatalogFingerprint === 'string') {
    return Object.freeze({ hostVersion: '0.7.3', schemaVersion: 3, annotationSource: 'compact-v2' });
  }
  return null;
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
  compactCatalogVersion?: string;
  compactCatalogFingerprint?: string;
  /** ds1tr-gen-v7: the hash of the upstream fog.txt that was actually read. */
  upstreamAnnotationSha256?: string;
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
  if (observed.compactCatalogVersion !== undefined
    && observed.compactCatalogVersion !== profile.fogEngine.compactCatalogVersion) {
    return `Compact catalog ${observed.compactCatalogVersion} differs from ${profile.id} (${profile.fogEngine.compactCatalogVersion ?? 'not used'}).`;
  }
  if (observed.compactCatalogFingerprint !== undefined
    && observed.compactCatalogFingerprint !== profile.fogEngine.compactCatalogFingerprint) {
    return `Compact catalog fingerprint ${observed.compactCatalogFingerprint} differs from ${profile.id} (${profile.fogEngine.compactCatalogFingerprint ?? 'not used'}).`;
  }
  if (observed.upstreamAnnotationSha256 !== undefined
    && observed.upstreamAnnotationSha256 !== profile.fogEngine.upstreamAnnotationSha256) {
    return `upstream fog.txt ${observed.upstreamAnnotationSha256} differs from ${profile.id} (${profile.fogEngine.upstreamAnnotationSha256 ?? 'not used'}).`;
  }
  return null;
}
