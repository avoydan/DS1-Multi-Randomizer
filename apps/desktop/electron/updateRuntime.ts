import type { BuildDistribution } from './channel.js';
import type { UpdateErrorCode, UpdateState } from './updater.js';

export const UPDATE_STATE_EVENT = 'updates:state' as const;

/** Only a positively identified GitHub build is allowed to load the GitHub updater implementation. */
export async function initializeGithubUpdates<T>(distribution: BuildDistribution, initialize: () => Promise<T>): Promise<T | null> {
  if (distribution !== 'github') return null;
  return initialize();
}

export function shouldScheduleUpdateChecks(distribution: BuildDistribution, hasUpdater: boolean): boolean {
  return distribution === 'github' && hasUpdater;
}

/** Local, inert state used by Nexus and malformed metadata. It keeps existing update preferences visible to the main process without modifying them. */
export function unavailableUpdateState(input: {
  distribution: Exclude<BuildDistribution, 'github'>;
  currentVersion: string;
  autoCheck: boolean;
  nexusPageUrl?: string | null;
}): UpdateState {
  return {
    phase: 'unsupported',
    currentVersion: input.currentVersion,
    availableVersion: null,
    releaseDate: null,
    progress: null,
    error: null,
    capability: 'none',
    unsupportedReason: null,
    availabilityReason: input.distribution === 'nexus' ? 'nexus-distribution' : 'invalid-build-info',
    distribution: input.distribution,
    nexusPageUrl: input.distribution === 'nexus' ? input.nexusPageUrl ?? null : null,
    autoCheck: input.autoCheck,
    lastCheckAt: null,
    releaseUrl: null,
    installBlocked: false,
  };
}

/** The Nexus and fail-closed IPC surface is deliberately local: even direct renderer IPC calls cannot start a request. */
export function createInertUpdateOperations(state: UpdateState) {
  return {
    getState: () => ({ ...state }),
    check: async () => ({ ...state }),
    maybeAutoCheck: async () => ({ ...state }),
    download: async () => ({ ...state }),
    install: async (): Promise<{ ok: false; code: UpdateErrorCode; state: UpdateState }> => ({ ok: false, code: 'NOT_SUPPORTED', state: { ...state } }),
    setAutoCheck: async (_enabled: boolean) => ({ ...state }),
    openReleasePage: async () => false,
  };
}
