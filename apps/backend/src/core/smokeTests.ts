import { SMOKE_SCENARIOS, SMOKE_TEST_CHECKLIST, SMOKE_TEST_PROFILE, resolveScenarioSettings } from '@ds1tr/contracts';
import type { GenerationSettings } from '@ds1tr/contracts';
import { canonicalizeSettings } from './canonicalSettings.js';
import { encodeSeedCode } from './seedCode.js';

/** A smoke-test world as the Advanced panel shows it: the Seed Code that creates it and what to look at while playing. */
export interface SmokeTestWorld {
  id: string;
  title: string;
  purpose: string;
  seedCode: string;
  settings: GenerationSettings;
  observe: readonly string[];
}

export function smokeTestWorlds(): { worlds: SmokeTestWorld[]; checklist: readonly string[] } {
  const worlds = SMOKE_SCENARIOS.map((scenario) => {
    const settings = canonicalizeSettings(resolveScenarioSettings(scenario));
    return {
      id: scenario.id, title: scenario.title, purpose: scenario.purpose, observe: scenario.observe, settings,
      seedCode: encodeSeedCode({ profile: SMOKE_TEST_PROFILE, masterSeed: scenario.masterSeed, settings }),
    };
  });
  return { worlds, checklist: SMOKE_TEST_CHECKLIST };
}
