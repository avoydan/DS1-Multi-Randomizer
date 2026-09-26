import { createHash } from 'node:crypto';
import type { DerivedSeeds, EngineName } from '@ds1tr/contracts';

function derive(masterSeed: string, engine: EngineName): number {
  const digest = createHash('sha256')
    .update(`ds1-triple-randomizer:v1:${masterSeed}:${engine}`)
    .digest();

  return digest.readUInt32LE(0);
}

export function deriveSeeds(masterSeed: string): DerivedSeeds {
  const normalized = masterSeed.trim() || '0';
  return {
    items: derive(normalized, 'items'),
    enemies: derive(normalized, 'enemies'),
    fog: derive(normalized, 'fog'),
  };
}
