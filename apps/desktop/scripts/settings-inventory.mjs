// Builds the settings inventory (docs/settings-inventory.json and docs/SETTINGS_INVENTORY.md) from the registry, the translation resources and the contracts defaults.
// The inventory is generated, never typed: `node apps/desktop/scripts/settings-inventory.mjs` writes both files, `--check` fails when they are out of date, and
// src/ui/settingsCoverage.test.mjs runs the same check, so the documentation cannot drift from the code.
import { execFileSync } from 'node:child_process';
import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  BASIC_LAYOUT, DEFAULT_GENERATION_SETTINGS, GENERATION_FIELD_POLICY, NEUTRAL_ENEMY_PLACEMENT_OPTIONS, NEUTRAL_KEY_OPTIONS, SECTION_IDS, SETTINGS, applySetting, joinConfig,
  settingState, settingValue,
} from '@ds1tr/contracts';

const here = path.dirname(fileURLToPath(import.meta.url));
const repository = path.resolve(here, '..', '..', '..');
const locales = path.join(repository, 'apps', 'desktop', 'src', 'locales');
const load = (language) => Object.fromEntries(readdirSync(path.join(locales, language)).filter((name) => name.endsWith('.json'))
  .map((name) => [name.replace(/\.json$/u, ''), JSON.parse(readFileSync(path.join(locales, language, name), 'utf8'))]));
const resources = { en: load('en'), pl: load('pl') };
const get = (language, key) => key.split('.').reduce((value, part) => (value === undefined || value === null ? undefined : value[part]), resources[language]);

const BOILERPLATE = /(configValidation|seedCode|seedTestSupport|preset|diagnostics|canonicalSettings|placementDiagnostics|pipeline|generationPipeline|combinedRandomizerConfig|fogHostProcess|HostRequest|Tests)\.(ts|mjs|cs)$/u;
function implementation(setting) {
  const leaf = setting.path.split('.').at(-1);
  let files = [];
  try {
    files = execFileSync('git', ['grep', '-l', '-w', leaf, '--', 'apps/backend/src', 'engines/combined/vendor/src', 'engines/combined/vendor/tools', 'tools/fog-host/src', ':(exclude)*.test.mjs', ':(exclude)*.json', ':(exclude)*.md', ':(exclude)*.patch'],
      { cwd: repository, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).split('\n').filter(Boolean);
  } catch (error) { files = String(error.stdout ?? '').split('\n').filter(Boolean); }
  const own = files.filter((file) => !BOILERPLATE.test(file));
  return { validatedIn: 'apps/backend/src/core/configValidation.ts', effect: (own.length > 0 ? own : files).slice(0, 3) };
}

/** Which reasons a setting can show, found by walking the configuration space with the real rules (never typed by hand). */
function observedReasons() {
  const reasons = new Map(SETTINGS.map((setting) => [setting.id, new Set()]));
  let seed = 20260708;
  const random = () => { seed = (seed * 1664525 + 1013904223) % 4294967296; return seed / 4294967296; };
  const visible = SETTINGS.filter((setting) => !setting.hidden);
  for (let walk = 0; walk < 80; walk += 1) {
    let current = joinConfig(structuredClone(DEFAULT_GENERATION_SETTINGS), '', 'inventory', true);
    for (let step = 0; step < 40; step += 1) {
      const setting = visible[Math.floor(random() * visible.length)];
      const value = setting.kind === 'pool' ? (random() < 0.5 ? null : ['c2500']) : setting.kind === 'toggle' ? random() < 0.5 : setting.kind === 'range' ? Math.floor(random() * 101) : setting.choices[Math.floor(random() * setting.choices.length)];
      current = applySetting(current, setting.id, value);
      for (const entry of visible) { const state = settingState(current, entry.id); if (state.reason) reasons.get(entry.id).add(state.reason); if (state.warning) reasons.get(entry.id).add(state.warning); }
    }
  }
  return reasons;
}

export function buildInventory() {
  const defaults = joinConfig(structuredClone(DEFAULT_GENERATION_SETTINGS), '', 'inventory', true);
  const neutralOf = (setting) => {
    const leaf = setting.path.split('.').at(-1);
    if (setting.path.startsWith('items.') && Object.hasOwn(NEUTRAL_KEY_OPTIONS, leaf)) return NEUTRAL_KEY_OPTIONS[leaf];
    if (setting.path.startsWith('enemies.placement.') && Object.hasOwn(NEUTRAL_ENEMY_PLACEMENT_OPTIONS, leaf)) return NEUTRAL_ENEMY_PLACEMENT_OPTIONS[leaf];
    return null;
  };
  const reasons = observedReasons();
  const basicOf = (id) => Object.entries(BASIC_LAYOUT).find(([, ids]) => ids.includes(id))?.[0] ?? null;
  const entries = SETTINGS.map((setting) => {
    const policy = GENERATION_FIELD_POLICY[setting.path];
    const hidden = Boolean(setting.hidden);
    const value = settingValue(defaults, setting.id);
    return {
      id: setting.id,
      configKey: setting.path,
      kind: setting.kind,
      serializedValues: setting.kind === 'toggle' ? [true, false] : setting.kind === 'range' ? '0..100 (integer percent)' : setting.kind === 'pool' ? 'null, or a list of model ids' : [...setting.choices],
      default: value === undefined ? null : value,
      neutralValueOfAnOldRecord: neutralOf(setting),
      section: setting.section,
      view: hidden ? 'not drawn' : (basicOf(setting.id) ? `Basic (${basicOf(setting.id)}) and Advanced` : 'Advanced'),
      seedCode: policy === 'seed' ? 'part of the world (Seed Code, preset, history)' : 'not part of a world (output preference)',
      preset: policy === 'seed' ? 'exported and imported' : 'exported as a preference, never part of a Seed Code',
      labels: hidden ? null : { en: get('en', `settings.${setting.id}.label`), pl: get('pl', `settings.${setting.id}.label`) },
      help: hidden ? null : { en: { short: get('en', `help.settings.${setting.id}.short`), what: get('en', `help.settings.${setting.id}.what`), affects: get('en', `help.settings.${setting.id}.affects`), limits: get('en', `help.settings.${setting.id}.limits`), example: get('en', `help.settings.${setting.id}.example`) ?? null }, pl: { short: get('pl', `help.settings.${setting.id}.short`) } },
      choiceExplanations: hidden || setting.kind !== 'select' || setting.plainChoices ? null : Object.fromEntries(setting.choices.map((choice) => [choice, { label: get('en', `settings.${setting.id}.options.${choice}`), explanation: get('en', `help.settings.${setting.id}.choices.${choice}`) }])),
      dependencies: [...reasons.get(setting.id)].sort().map((reason) => ({ reason, text: get('en', `help.reasons.${reason}`) })),
      evidence: setting.evidence,
      hidden: hidden ? setting.hidden : null,
      implementation: implementation(setting),
    };
  });
  return { schema: 'ds1tr-settings-inventory-1', generationProfile: 'ds1tr-gen-v1', counts: { total: entries.length, drawn: entries.filter((entry) => !entry.hidden).length, hidden: entries.filter((entry) => entry.hidden).length, basic: Object.values(BASIC_LAYOUT).flat().length }, sections: SECTION_IDS, entries };
}

const cell = (value) => String(value ?? '').replace(/\|/gu, '\\|').replace(/\n/gu, ' ');
const json = (value) => (typeof value === 'string' ? value : JSON.stringify(value));

export function buildMarkdown(inventory) {
  const lines = [];
  const drawn = inventory.entries.filter((entry) => !entry.hidden);
  lines.push('# Settings inventory', '', 'Generated by `node apps/desktop/scripts/settings-inventory.mjs` from the settings registry (`packages/contracts/src/settingsRegistry.ts`), the translation resources and the contracts defaults; `docs/settings-inventory.json` is the machine-readable form. Do not edit by hand: `--check` (and `src/ui/settingsCoverage.test.mjs`) fail when it is out of date.', '');
  lines.push(`Generation profile \`${inventory.generationProfile}\`. ${inventory.counts.total} options in the configuration: ${inventory.counts.drawn} are drawn by the interface (${inventory.counts.basic} of them also in the Basic view), ${inventory.counts.hidden} are not drawn (see "Options that are not drawn").`, '');
  lines.push('The view (Basic or Advanced), the language and the help are interface state: they are not in a Seed Code, a preset or a history entry, they consume no randomness and they never change a value. Basic and Advanced are two views of one configuration; the Basic view never resets an advanced value (it says "Custom advanced settings are active." instead).', '');
  lines.push('## Options', '', '| Id | Config key | Values | Default | Section | View | EN label | PL label |', '|---|---|---|---|---|---|---|---|');
  for (const entry of drawn) lines.push(`| \`${entry.id}\` | \`${entry.configKey}\` | ${cell(json(entry.serializedValues))} | \`${cell(JSON.stringify(entry.default))}\` | ${entry.section} | ${entry.view} | ${cell(entry.labels.en)} | ${cell(entry.labels.pl)} |`);
  lines.push('', '## What each option does, and when it is available', '');
  for (const entry of drawn) {
    lines.push(`### ${entry.labels.en} (\`${entry.id}\`)`, '');
    lines.push(`* **Mechanic:** ${entry.help.en.what}`, `* **Affects:** ${entry.help.en.affects}`, `* **Limits:** ${entry.help.en.limits}`);
    if (entry.help.en.example) lines.push(`* **Example:** ${entry.help.en.example}`);
    if (entry.choiceExplanations) lines.push('* **Choices:**', ...Object.entries(entry.choiceExplanations).map(([value, choice]) => `  * \`${value}\` (${choice.label}): ${choice.explanation}`));
    if (entry.dependencies.length > 0) lines.push('* **Disabled or warned when:**', ...entry.dependencies.map((dependency) => `  * \`${dependency.reason}\`: ${dependency.text}`));
    lines.push(`* **Seed Code and presets:** ${entry.seedCode}; ${entry.preset}.`);
    if (entry.neutralValueOfAnOldRecord !== null) lines.push(`* **Neutral value (an old record without the key means this):** \`${JSON.stringify(entry.neutralValueOfAnOldRecord)}\`.`);
    lines.push(`* **Evidence:** ${entry.evidence === 'verified' ? 'read from the code that does it' : 'read from the code; the exact effect in the game was not observed'}.`);
    lines.push(`* **Implemented in (found by search, validated in ${entry.implementation.validatedIn}):** ${entry.implementation.effect.map((file) => `\`${file}\``).join(', ') || 'n/a'}.`, '');
  }
  lines.push('## Options that are not drawn', '', 'The field stays in the configuration, in presets and in Seed Codes (an old code keeps its meaning); only the control is not drawn, because it cannot do anything in this version.', '', '| Id | Config key | Why | Default |', '|---|---|---|---|');
  const reasonText = { 'no-effect': 'no effect, never had any with real game data: not a feature of the original randomizers; the vendored engine implements it only for its placeholder data set (docs/EARLY_OPTIONS_AUDIT.md); probed with the real pipeline: the generated files are identical with it on and off', refused: 'refused: validation rejects it until progression safety is proven' };
  for (const entry of inventory.entries.filter((candidate) => candidate.hidden)) lines.push(`| \`${entry.id}\` | \`${entry.configKey}\` | ${reasonText[entry.hidden]} | \`${JSON.stringify(entry.default)}\` |`);
  lines.push('');
  return `${lines.join('\n')}\n`;
}

const inventoryPath = path.join(repository, 'docs', 'settings-inventory.json');
const markdownPath = path.join(repository, 'docs', 'SETTINGS_INVENTORY.md');
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const inventory = buildInventory();
  const next = { json: `${JSON.stringify(inventory, null, 2)}\n`, markdown: buildMarkdown(inventory) };
  if (process.argv.includes('--check')) {
    const same = readFileSync(inventoryPath, 'utf8').replace(/\r\n/gu, '\n') === next.json && readFileSync(markdownPath, 'utf8').replace(/\r\n/gu, '\n') === next.markdown;
    if (!same) { console.error('docs/SETTINGS_INVENTORY.md or docs/settings-inventory.json is out of date: run node apps/desktop/scripts/settings-inventory.mjs'); process.exit(1); }
    console.log('settings inventory is up to date');
  } else {
    writeFileSync(inventoryPath, next.json);
    writeFileSync(markdownPath, next.markdown);
    console.log(`wrote ${path.relative(repository, inventoryPath)} and ${path.relative(repository, markdownPath)}`);
  }
}
