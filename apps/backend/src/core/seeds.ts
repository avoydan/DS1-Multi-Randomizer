import { createHash } from 'node:crypto';
import type { DerivedSeeds, EngineName } from '@ds1tr/contracts';

/**
 * Length of the hex token handed to the combined engine. Upstream requires
 * `/^[\w.-]{1,64}$/`; 32 hex characters (128 bits) satisfy it for any master
 * seed while keeping the `seed_<token>` package directory short on Windows.
 */
const combinedSeedLength = 32;

function digest(masterSeed: string, engine: EngineName): Buffer {
  return createHash('sha256')
    .update(`ds1-triple-randomizer:v1:${masterSeed}:${engine}`)
    .digest();
}

export function deriveSeeds(masterSeed: string): DerivedSeeds {
  const normalized = masterSeed.trim() || '0';
  return {
    combined: digest(normalized, 'combined').toString('hex').slice(0, combinedSeedLength),
    fog: digest(normalized, 'fog').readUInt32LE(0),
  };
}
