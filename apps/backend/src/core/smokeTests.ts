import { SMOKE_SCENARIOS, SMOKE_START, SMOKE_TEST_CHECKLIST, SMOKE_TEST_PROFILE, describeSmokeExpected, describeSmokeTarget, resolveScenarioSettings } from '@ds1tr/contracts';
import type { GenerationSettings, SmokeCategory } from '@ds1tr/contracts';
import { canonicalizeSettings } from './canonicalSettings.js';
import { encodeSeedCode } from './seedCode.js';

/** A smoke-test world as the Advanced panel shows it: the Seed Code that creates it and what to look at while playing. */
export interface SmokeTestWorld {
  id: string;
  title: string;
  category: SmokeCategory;
  purpose: string;
  seedCode: string;
  settings: GenerationSettings;
  observe: readonly string[];
  /** Quick worlds: where to look and what must be there (one line each), the estimated seconds to the target and the steps after the first check. */
  targets: readonly string[];
  expected: readonly string[];
  estimateSeconds: number | null;
  afterwards: readonly string[];
}

export function smokeTestWorlds(): { worlds: SmokeTestWorld[]; checklist: readonly string[]; start: string } {
  const worlds = SMOKE_SCENARIOS.map((scenario) => {
    const settings = canonicalizeSettings(resolveScenarioSettings(scenario));
    return {
      id: scenario.id, title: scenario.title, category: scenario.category, purpose: scenario.purpose, observe: scenario.observe, settings,
      seedCode: encodeSeedCode({ profile: SMOKE_TEST_PROFILE, masterSeed: scenario.masterSeed, settings }),
      targets: (scenario.expectations ?? []).map(describeSmokeTarget),
      expected: (scenario.expectations ?? []).map(describeSmokeExpected),
      estimateSeconds: scenario.estimateSeconds ?? null,
      afterwards: scenario.afterwards ?? [],
    };
  });
  return {
    worlds, checklist: SMOKE_TEST_CHECKLIST,
    start: `${SMOKE_START.mapName}, where you wake up in a new game (the cell you start in)`,
  };
}
