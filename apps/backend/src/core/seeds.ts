import { createHash } from 'node:crypto';
import type { DerivedSeeds } from '@ds1tr/contracts';

/**
 * Length of the hex token handed to the combined engine. Upstream requires
 * `/^[\w.-]{1,64}$/`; 32 hex characters (128 bits) satisfy it for any master
 * seed while keeping the `seed_<token>` package directory short on Windows.
 */
const combinedSeedLength = 32;

function digest(masterSeed: string, stream: string): Buffer {
  return createHash('sha256')
    .update(`ds1-triple-randomizer:v1:${masterSeed}:${stream}`)
    .digest();
}

function normalize(masterSeed: string): string {
  return masterSeed.trim() || '0';
}

function combinedToken(masterSeed: string, stream: string): string {
  return digest(masterSeed, stream).toString('hex').slice(0, combinedSeedLength);
}

export function deriveSeeds(masterSeed: string): DerivedSeeds {
  const normalized = normalize(masterSeed);
  return {
    combined: combinedToken(normalized, 'combined'),
    fog: digest(normalized, 'fog').readUInt32LE(0),
  };
}

/**
 * Seeds for one Combined -> Fog retry attempt. Attempt 0 of each stream is
 * exactly `deriveSeeds()`; attempt n > 0 uses the named streams
 * `combined-retry:<n>` and `fog-retry:<n>`. The two counters are
 * independent: the Fog seed depends only on the Fog attempt, so a combined
 * retry restarts the Fog sequence at attempt 0.
 */
export function deriveAttemptSeeds(
  masterSeed: string,
  combinedAttempt: number,
  fogAttempt: number,
): DerivedSeeds {
  for (const attempt of [combinedAttempt, fogAttempt]) {
    if (!Number.isSafeInteger(attempt) || attempt < 0) {
      throw new RangeError(`Retry attempt must be a non-negative integer: ${attempt}.`);
    }
  }
  const normalized = normalize(masterSeed);
  const base = deriveSeeds(normalized);
  return {
    combined: combinedAttempt === 0
      ? base.combined
      : combinedToken(normalized, `combined-retry:${combinedAttempt}`),
    fog: fogAttempt === 0
      ? base.fog
      : digest(normalized, `fog-retry:${fogAttempt}`).readUInt32LE(0),
  };
}
