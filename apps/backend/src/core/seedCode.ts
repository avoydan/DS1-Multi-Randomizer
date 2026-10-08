import { createHash } from 'node:crypto';
import { deflateRawSync, inflateRawSync } from 'node:zlib';
import {
  GENERATION_PROFILE_V1, SEED_CODE_PREFIX, SEED_ENVELOPE_FORMAT, SEED_FORMAT_VERSION, joinConfig,
} from '@ds1tr/contracts';
import type { DecodedSeed, GenerationSettings, SeedEnvelope } from '@ds1tr/contracts';
import { canonicalizeSettings, sparseCanonicalSettings } from './canonicalSettings.js';
import { InvalidConfigurationError, validateRandomizerConfig } from './configValidation.js';
import { resolveGenerationProfile } from './generationProfiles.js';

/**
 * Seed Code, format 1 (docs/SEED_FORMAT.md). Entirely offline and self-contained:
 *
 *   DS1TR1-XXXXXXXX-XXXXXXXX-...
 *
 * The text after the prefix is Crockford base32 of: [format version byte] [deflate-raw(canonical envelope JSON) with the fixed
 * dictionary below] [first 4 bytes of SHA-256 over everything before]. The envelope carries the generation profile id, the
 * internal master seed and the complete canonical settings. NOTHING in this file may change once codes exist: the dictionary, the
 * envelope layout, the alphabet and the checksum are part of format 1. A different layout is format 2 with its own prefix.
 */

/** Fixed deflate dictionary of format 1: it only makes codes shorter and must stay byte-for-byte identical forever. */
const SEED_DICTIONARY = Buffer.from([
  '{"profile":"ds1tr-gen-v1","masterSeed":"1234567890","settings":{"items":{"enabled":true,"randomizeWorldItems":true,',
  '"randomizeProtectedItems":false,"progressionLogic":true,"guaranteeEarlyWeapon":true,"balancedEarlyLoot":true,',
  '"randomizeStartingClass":false,"randomizeStartingEquipment":false,"randomizeGifts":false,"randomizeEnemyDrops":false,',
  '"guaranteedEnemyDrops":false,"randomizeShops":false,"fashionSouls":true,"soulItems":"shuffled","startingItems":"shield-2h"},',
  '"enemies":{"enabled":true,"randomizeRegularEnemies":true,"randomizeBosses":true,"scaling":"area","replaceMimics":true,',
  '"replaceRespawningMosquitoes":true,"preventSameEnemy":false,"pinwheelChaos":false,"gwynNerf":"medium","secondGargoyle":"replace",',
  '"placement":{"algorithm":"permutation-v1","replacementChance":100,"normalReplacement":"normals","fit":"anywhere",',
  '"difficultyMode":"random","difficultyStrictness":"loose","typeReplacement":"disabled","customPool":null}},',
  '"fog":{"enabled":true,"traversableFogs":true,"bossFogs":true,"areaWarps":true,"majorPvpFogs":false,"minorPvpFogs":false,',
  '"lordvesselGates":false,"randomStart":false,"disconnectedFogs":false,"noFallBedOfChaos":false,"scaling":true,"glitchedLogic":false,',
  '"requireLordSouls":true,"pacifistMode":false,"language":"ENGLISH"},"output":{"includeDlc":true}}}',
  '"vanilla""progressive""strong""none""keep""single-boss""replaced""shield-1h""mixed-pool""sampling-v1""fits""anywhere-except-npcs"',
  '"curve""random-easy-asylum""curve-easy-asylum""strict""very-loose""enabled""enabled-except-roaming-bosses""FRENCH""GERMAN"',
  '"ITALIAN""JAPANESE""KOREAN""NSPANISH""POLISH""PORTUGUESE""RUSSIAN""SCHINESE""SPANISH""TCHINESE"["c1000","c2500","c3200"]',
].join(''), 'utf8');

const ALPHABET = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
const GROUP = 8;
const CHECKSUM_BYTES = 4;
/** Upper bound for the inflated envelope; a real one is around 1 KB. */
const MAX_ENVELOPE_BYTES = 64 * 1024;

export function base32Encode(bytes: Uint8Array): string {
  let bits = 0;
  let value = 0;
  let out = '';
  for (const byte of bytes) {
    value = (value << 8) | byte;
    bits += 8;
    while (bits >= 5) {
      out += ALPHABET[(value >>> (bits - 5)) & 31];
      bits -= 5;
    }
    value &= (1 << bits) - 1;
  }
  if (bits > 0) out += ALPHABET[(value << (5 - bits)) & 31];
  return out;
}

export function base32Decode(text: string): Uint8Array | null {
  let bits = 0;
  let value = 0;
  const out: number[] = [];
  for (const raw of text) {
    // Crockford: O reads as 0, I and L as 1; case does not matter.
    const char = raw === 'O' ? '0' : raw === 'I' || raw === 'L' ? '1' : raw;
    const index = ALPHABET.indexOf(char);
    if (index < 0) return null;
    value = (value << 5) | index;
    bits += 5;
    if (bits >= 8) {
      out.push((value >>> (bits - 8)) & 255);
      bits -= 8;
    }
    value &= (1 << bits) - 1;
  }
  return Uint8Array.from(out);
}

function checksum(bytes: Uint8Array): Buffer {
  return createHash('sha256').update(bytes).digest().subarray(0, CHECKSUM_BYTES);
}

/** Stable JSON of an envelope: fixed top-level order, canonical settings. */
export function serializeEnvelope(envelope: Pick<SeedEnvelope, 'profile' | 'masterSeed' | 'settings'>): string {
  return JSON.stringify({ profile: envelope.profile, masterSeed: envelope.masterSeed, settings: sparseCanonicalSettings(envelope.settings) });
}

export function encodeSeedCode(envelope: Pick<SeedEnvelope, 'profile' | 'masterSeed' | 'settings'>): string {
  const payload = deflateRawSync(Buffer.from(serializeEnvelope(envelope), 'utf8'), { level: 9, dictionary: SEED_DICTIONARY });
  const body = Buffer.concat([Buffer.from([SEED_FORMAT_VERSION]), payload]);
  const encoded = base32Encode(Buffer.concat([body, checksum(body)]));
  const groups = encoded.match(new RegExp(`.{1,${GROUP}}`, 'gu')) ?? [];
  return `${SEED_CODE_PREFIX}${SEED_FORMAT_VERSION}-${groups.join('-')}`;
}

const damaged = (technical: string): DecodedSeed => ({
  ok: false, code: 'CHECKSUM', message: 'This seed code is invalid or damaged. Copy it again from the person who shared it.', technical,
});
const malformed = (technical: string): DecodedSeed => ({
  ok: false, code: 'MALFORMED', message: 'This is not a valid seed code. Copy it again from the person who shared it.', technical,
});

/** Decodes and fully validates a Seed Code for this application. */
export function decodeSeedCode(input: string): DecodedSeed {
  const text = String(input ?? '').trim().toUpperCase();
  // The format number is followed by a dash (or the whitespace a dash turned into); without any separator it is one digit,
  // because the digits that follow belong to the code itself.
  const prefix = new RegExp(`^${SEED_CODE_PREFIX}(\\d+)[-\\s]+([\\s\\S]*)$`, 'u').exec(text)
    ?? new RegExp(`^${SEED_CODE_PREFIX}(\\d)([\\s\\S]*)$`, 'u').exec(text);
  if (!prefix) return malformed('The text does not start with DS1TR<format>-.');
  prefix[2] = prefix[2].replace(/\s+/gu, '');
  const formatVersion = Number(prefix[1]);
  if (formatVersion !== SEED_FORMAT_VERSION) {
    return {
      ok: false, code: 'UNSUPPORTED_FORMAT',
      message: 'This seed code was made by a newer version of DS1 Multi Randomizer. Update the app to use it.',
      technical: `Seed code format ${formatVersion} is not supported (this application reads format ${SEED_FORMAT_VERSION}).`,
    };
  }
  const bytes = base32Decode(prefix[2].replace(/-/gu, ''));
  if (!bytes || bytes.length <= 1 + CHECKSUM_BYTES) return malformed('The code contains characters outside the Seed Code alphabet or is too short.');
  const body = Buffer.from(bytes.subarray(0, bytes.length - CHECKSUM_BYTES));
  const expected = checksum(body);
  if (!expected.equals(Buffer.from(bytes.subarray(bytes.length - CHECKSUM_BYTES)))) {
    return damaged(`Checksum mismatch (expected ${expected.toString('hex')}).`);
  }
  if (body[0] !== SEED_FORMAT_VERSION) return malformed(`Header format ${body[0]} does not match the prefix.`);

  let parsed: unknown;
  try {
    const json = inflateRawSync(body.subarray(1), { dictionary: SEED_DICTIONARY, maxOutputLength: MAX_ENVELOPE_BYTES }).toString('utf8');
    parsed = JSON.parse(json);
  } catch (error) {
    return malformed(`The payload cannot be read: ${error instanceof Error ? error.message : String(error)}`);
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return malformed('The payload is not an object.');
  const envelope = parsed as Record<string, unknown>;
  const extra = Object.keys(envelope).filter((key) => !['profile', 'masterSeed', 'settings'].includes(key));
  if (typeof envelope.profile !== 'string' || typeof envelope.masterSeed !== 'string' || extra.length > 0) {
    return malformed('The envelope does not have the expected fields.');
  }

  const profile = resolveGenerationProfile(envelope.profile);
  if (!profile) {
    return {
      ok: false, code: 'UNSUPPORTED_PROFILE',
      message: 'This seed needs a newer version of DS1 Multi Randomizer. Update the app to generate this world.',
      technical: `Generation profile ${envelope.profile} is not supported by this application.`,
    };
  }
  if (envelope.masterSeed.trim().length === 0 || envelope.masterSeed.length > 128) return malformed('The master seed is empty or too long.');

  let settings: GenerationSettings;
  try {
    settings = canonicalizeSettings(profile.completeSettings(envelope.settings));
    validateRandomizerConfig(joinConfig(settings, 'validation-only', envelope.masterSeed, false));
  } catch (error) {
    const problems = error instanceof InvalidConfigurationError ? error.problems.join(' ') : error instanceof Error ? error.message : String(error);
    return {
      ok: false, code: 'INVALID_SETTINGS',
      message: 'The settings inside this seed code cannot be used by this version of the app.', technical: problems,
    };
  }
  const canonical = { format: SEED_ENVELOPE_FORMAT, formatVersion, profile: profile.id, masterSeed: envelope.masterSeed, settings } as SeedEnvelope;
  return { ok: true, envelope: canonical, seedCode: encodeSeedCode(canonical) };
}

export { GENERATION_PROFILE_V1 };
