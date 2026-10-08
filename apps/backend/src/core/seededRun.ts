import { randomInt } from 'node:crypto';
import { joinConfig, splitConfig } from '@ds1tr/contracts';
import type { RandomizerConfig, SeedEnvelope } from '@ds1tr/contracts';
import { canonicalizeSettings } from './canonicalSettings.js';
import { InvalidConfigurationError, validateRandomizerConfig } from './configValidation.js';
import { currentGenerationProfile, resolveGenerationProfile, type GenerationProfile } from './generationProfiles.js';
import { decodeSeedCode, encodeSeedCode } from './seedCode.js';

/**
 * Turns a run request into a frozen, validated run: the profile, the internal master seed, the canonical settings and the
 * Seed Code that names this exact world. This is where "Randomize" and "Load seed" meet:
 *   - a request with a Seed Code is authoritative: its settings and master seed win over whatever the request carries;
 *   - otherwise the settings of the request are frozen with a fresh master seed made now (an explicit master seed, as older
 *     configurations carry, is honored).
 * The machine's game path and the spoiler preference come from the request in both cases; they are not part of the world.
 */
export type RunRequestBody = Partial<RandomizerConfig> & { seedCode?: string };

export type PreparedRun =
  | { ok: true; config: RandomizerConfig; profile: GenerationProfile; seedCode: string; envelope: Pick<SeedEnvelope, 'profile' | 'masterSeed' | 'settings'>; fromSeedCode: boolean }
  | { ok: false; code: string; message: string; technical: string; problems?: string[] };

export function newMasterSeed(): string {
  return String(randomInt(1, 2 ** 32));
}

export function prepareRun(body: RunRequestBody, makeSeed: () => string = newMasterSeed): PreparedRun {
  const gamePath = typeof body.gamePath === 'string' ? body.gamePath : '';
  const spoiler = body.output?.generateSpoilerLog === true;
  if (typeof body.seedCode === 'string' && body.seedCode.trim() !== '') {
    const decoded = decodeSeedCode(body.seedCode);
    if (!decoded.ok) return { ok: false, code: decoded.code, message: decoded.message, technical: decoded.technical };
    const { envelope } = decoded;
    const profile = resolveGenerationProfile(envelope.profile);
    if (!profile) return { ok: false, code: 'UNSUPPORTED_PROFILE', message: 'This seed needs a newer version of DS1 Multi Randomizer.', technical: envelope.profile };
    return {
      ok: true, config: joinConfig(envelope.settings, gamePath, envelope.masterSeed, spoiler), profile, seedCode: decoded.seedCode,
      envelope, fromSeedCode: true,
    };
  }
  const requested = typeof body.masterSeed === 'string' ? body.masterSeed.trim() : '';
  const masterSeed = requested || makeSeed();
  try {
    const full = { gamePath, masterSeed, items: body.items, enemies: body.enemies, fog: body.fog, output: body.output };
    validateRandomizerConfig(full);
    const { settings } = splitConfig(full as RandomizerConfig);
    const canonical = canonicalizeSettings(settings);
    const profile = currentGenerationProfile();
    const envelope = { profile: profile.id, masterSeed, settings: canonical };
    return {
      ok: true, config: joinConfig(canonical, gamePath, masterSeed, spoiler), profile, seedCode: encodeSeedCode(envelope),
      envelope, fromSeedCode: false,
    };
  } catch (error) {
    if (error instanceof InvalidConfigurationError) {
      return { ok: false, code: 'INVALID_SETTINGS', message: 'These settings cannot be used. Change them and try again.', technical: error.message, problems: error.problems };
    }
    throw error;
  }
}
