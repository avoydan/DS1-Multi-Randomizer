import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { DSR_STEAM_APP_ID, OUTCOME_MESSAGES } from '@ds1tr/contracts';

// The renderer has no test runner of its own; these checks pin the user-visible contract of the Seed Code screens. Since the interface is localized, a visible
// text is pinned in two parts: the screen must use the key, and the English resource (the canonical text) must say what the product has always said.
const desktop = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'desktop', 'src');
const read = (name) => readFile(path.join(desktop, name), 'utf8');
const english = {};
for (const name of (await readdir(path.join(desktop, 'locales', 'en'))).filter((file) => file.endsWith('.json'))) {
  english[name.replace(/\.json$/u, '')] = JSON.parse(await readFile(path.join(desktop, 'locales', 'en', name), 'utf8'));
}
const en = (key) => key.split('.').reduce((node, part) => (node && typeof node === 'object' ? node[part] : undefined), english);
const used = (source, key) => source.includes(`'${key}'`) || source.includes(`\`${key}`);

test('Apply and Restore end with the explicit success messages, and Apply offers Launch game', async () => {
  // The contracts keep the English source of the two outcomes; the interface shows them from its resources, which must say the same.
  assert.equal(OUTCOME_MESSAGES.applied.text, 'Randomized game applied successfully.');
  assert.equal(OUTCOME_MESSAGES.applied.sub, 'You can now launch Dark Souls Remastered.');
  assert.equal(OUTCOME_MESSAGES.restored.text, 'Original game restored successfully.');
  for (const outcome of ['applied', 'restored']) {
    assert.equal(en(`app.outcome.${outcome}.text`), OUTCOME_MESSAGES[outcome].text);
    assert.equal(en(`app.outcome.${outcome}.sub`), OUTCOME_MESSAGES[outcome].sub);
  }
  const app = await read('App.tsx');
  assert.match(app, /setNotice\('applied'\)/u);
  assert.match(app, /setNotice\('restored'\)/u);
  assert.ok(app.includes("t(`app.outcome.${notice}.text`)"));
  assert.ok(used(app, 'common.launchGame'));
  assert.equal(en('common.launchGame'), 'Launch game');
  assert.equal(DSR_STEAM_APP_ID, '570940');
});

test('the master seed is not made at startup: the form starts empty and only Randomize creates one', async () => {
  const app = await read('App.tsx');
  assert.match(app, /joinConfig\(structuredClone\(DEFAULT_GENERATION_SETTINGS\), '', '', true\)/u);
  assert.doesNotMatch(app, /randomSeed|Math\.random\(\)/u, 'the renderer never invents a master seed; the backend makes it when a run starts');
  assert.ok(used(app, 'app.run.randomize') && used(app, 'common.loadSeed'));
  assert.equal(en('app.run.randomize'), 'Generate world');
  assert.equal(en('common.loadSeed'), 'Load seed');
  assert.doesNotMatch(await read('SeedPanel.tsx'), /Master seed/iu);
  for (const text of [english.seed, english.common]) assert.doesNotMatch(JSON.stringify(text), /master seed/iu);
  assert.match(en('app.advanced.internalSeed'), /^Internal master seed: /u, 'the master seed is an Advanced detail, not part of the main seed controls');
  assert.doesNotMatch(app, /aria-label="Master seed"/u);
});

test('the run area follows the state of the game: only the actions that apply are offered', async () => {
  const { deriveRunPhase } = await import('@ds1tr/contracts');
  assert.equal(deriveRunPhase({ busy: false, gameState: 'vanilla', hasGeneratedWorld: false }), 'ready');
  assert.equal(deriveRunPhase({ busy: false, gameState: 'vanilla', hasGeneratedWorld: true }), 'generated');
  assert.equal(deriveRunPhase({ busy: false, gameState: 'active', hasGeneratedWorld: true }), 'applied');
  assert.equal(deriveRunPhase({ busy: false, gameState: 'active', hasGeneratedWorld: false }), 'applied', 'also after a restart of the app');
  assert.equal(deriveRunPhase({ busy: false, gameState: 'interrupted', hasGeneratedWorld: false }), 'repair');
  for (const state of ['modified', 'version-changed', 'invalid', 'no-snapshot', null]) assert.equal(deriveRunPhase({ busy: false, gameState: state, hasGeneratedWorld: false }), 'blocked');
  assert.equal(deriveRunPhase({ busy: true, gameState: 'vanilla', hasGeneratedWorld: true }), 'working');
  const app = await read('App.tsx');
  assert.match(app, /phase === 'ready' && \(/u);
  assert.match(app, /phase === 'generated' && generation\?\.ok/u);
  assert.match(app, /phase === 'applied' && \(/u);
});

test('Load seed generates the shared world straight away: there is no separate "Generate this world" step', async () => {
  for (const name of ['App.tsx', 'SeedPanel.tsx', 'RunHistoryPanel.tsx']) assert.doesNotMatch(await read(name), /Generate this world/u, name);
  assert.doesNotMatch(JSON.stringify(english), /Generate this world/u);
  const app = await read('App.tsx');
  assert.match(app, /await generateShared\(seed\)/u);
  assert.match(app, /onLoaded=\{\(seed\) => void loadSeed\(seed\)\}/u);
});

test('Run history is collapsed by default, regenerating does not add history, and the menu holds the secondary actions', async () => {
  const panel = await read('RunHistoryPanel.tsx');
  assert.match(panel, /useState\(false\)/u);
  assert.equal(en('history.title'), 'Run history');
  assert.ok(used(panel, 'history.title'));
  for (const [key, label] of [['history.viewSettings', 'View settings'], ['history.viewSpoiler', 'View spoiler file'], ['history.regenerate', 'Regenerate files'], ['history.delete', 'Delete from history']]) {
    assert.ok(used(panel, key), key);
    assert.equal(en(key), label);
  }
  // The secondary actions of Part J live in the same menu: hints, diagnostics and the support bundle.
  for (const key of ['hints.action.view', 'hints.action.export', 'history.diagnostics', 'support.ui.export']) assert.ok(used(panel, key), key);
  assert.doesNotMatch(panel, /Recreate/u);
  assert.doesNotMatch(JSON.stringify(english.history), /Recreate/u);
  const app = await read('App.tsx');
  assert.doesNotMatch(app, /Recreate|recreate/u);
  assert.match(app, /const regenerate = async \(entry: RunHistoryEntry\) => \{\s*await generateRun\(entry\.seedCode\);/u);
});

test('the user-facing wording says Fog, and no withdrawn brand mark is on any screen', async () => {
  const app = await read('App.tsx');
  assert.equal(en('settings.sections.transitions.title'), 'Transitions and fog');
  assert.equal(en('settings.fog.enabled.label'), 'Transition and fog randomization');
  assert.doesNotMatch(app, /World settings|WORLD<\/span>/u);
  assert.doesNotMatch(JSON.stringify(english.settings), /World settings/u);
  for (const name of ['App.tsx', 'styles.css', 'ui/Sidebar.tsx']) assert.doesNotMatch(await read(name), /ludos/iu, `${name} does not carry the withdrawn mark`);
  assert.match(app, /import logo from '\.\/assets\/branding\/app-mark\.png'/u);
});

test('Boss replacement and Unique bosses are real controls (not planned placeholders) and are reset with the algorithm', async () => {
  const { SETTINGS, applySetting, DEFAULT_GENERATION_SETTINGS, joinConfig } = await import('@ds1tr/contracts');
  assert.equal(en('settings.enemies.bossReplacement.label'), 'Boss replacement');
  assert.equal(en('settings.enemies.bossReplacement.options.bosses'), 'Random bosses');
  assert.equal(en('settings.enemies.uniqueBosses.label'), 'Use every boss once first');
  assert.deepEqual(SETTINGS.find((setting) => setting.id === 'enemies.bossReplacement').choices, ['classic', 'bosses', 'normals', 'both']);
  assert.ok(SETTINGS.some((setting) => setting.id === 'enemies.uniqueBosses' && !setting.hidden));
  const config = joinConfig(structuredClone(DEFAULT_GENERATION_SETTINGS), '', 'seed', true);
  const set = applySetting(applySetting(config, 'enemies.bossReplacement', 'bosses'), 'enemies.uniqueBosses', true);
  assert.equal(set.enemies.placement.uniqueBosses, true);
  assert.equal(applySetting(set, 'enemies.bossReplacement', 'classic').enemies.placement.uniqueBosses, false, 'leaving the draw modes puts Unique bosses back');
  assert.equal(applySetting(set, 'enemies.algorithm', 'permutation-v1').enemies.placement.bossReplacement, 'classic');
  assert.doesNotMatch(await read('App.tsx'), /\['Boss replacement', 'NPC replacement'\]|'Allow T-pose behavior', 'Unique bosses'/u);
});

test('bosses in regular slots (Part E1): the options are real, are available only when they apply (and say why not), and are reset when they stop applying', async () => {
  const { applySetting, settingState, DEFAULT_GENERATION_SETTINGS, joinConfig } = await import('@ds1tr/contracts');
  const modes = JSON.stringify(english.settings.enemies.normalReplacement.options);
  assert.doesNotMatch(modes + JSON.stringify(english.settings.enemies.bossReplacement.options), /\(planned\)/u, 'the two modes are no longer placeholders');
  assert.equal(en('settings.enemies.normalReplacement.options.bosses'), 'Only bosses');
  assert.equal(en('settings.enemies.normalReplacement.options.both'), 'Bosses or regular enemies');
  const base = joinConfig(structuredClone(DEFAULT_GENERATION_SETTINGS), '', 'seed', true);
  // The boss chance only with "both", souls and respawn with either boss mode; otherwise the control is disabled and names the mode it needs.
  const reason = (config, id) => settingState(config, id).reason;
  assert.equal(reason(base, 'enemies.bossChanceNormal'), 'needsBothNormalMode');
  assert.equal(reason(base, 'enemies.roamingSouls'), 'needsRoamingMode');
  assert.equal(reason(base, 'enemies.roamingDead'), 'needsRoamingMode');
  const both = applySetting(base, 'enemies.normalReplacement', 'both');
  for (const id of ['enemies.bossChanceNormal', 'enemies.roamingSouls', 'enemies.roamingDead']) assert.equal(settingState(both, id).enabled, true, id);
  const bossesOnly = applySetting(base, 'enemies.normalReplacement', 'bosses');
  assert.equal(settingState(bossesOnly, 'enemies.bossChanceNormal').enabled, false);
  assert.equal(settingState(bossesOnly, 'enemies.roamingSouls').enabled, true);
  // Leaving a mode puts the settings that only it needed back to their defaults (a hidden setting never stays in the world).
  const tuned = applySetting(applySetting(applySetting(both, 'enemies.bossChanceNormal', 70), 'enemies.roamingSouls', 20), 'enemies.roamingDead', true);
  const left = applySetting(tuned, 'enemies.normalReplacement', 'normals');
  assert.deepEqual([left.enemies.placement.bossChanceInNormalSlots, left.enemies.placement.roamingBossSouls, left.enemies.placement.roamingBossesStayDead], [10, 50, false]);
  for (const key of ['bossChanceNormal', 'roamingSouls', 'roamingDead']) assert.doesNotMatch(en(`settings.enemies.${key}.label`), /NpcParam|getSoul|entity|EMEVD/u);
});

test('the test worlds panel separates quick smoke tests from extended gameplay tests and shows the exact target, the expectation and the time', async () => {
  const panel = await read('SmokeTestPanel.tsx');
  assert.ok(panel.includes("<optgroup label={t('smoke.groupQuick')}>") && panel.includes("<optgroup label={t('smoke.groupExtended')}>"));
  assert.match(en('smoke.groupQuick'), /^Quick smoke tests/u);
  assert.match(en('smoke.groupExtended'), /^Extended gameplay tests/u);
  for (const [key, label] of [['categoryQuick', 'Quick smoke test'], ['categoryExtended', 'Extended gameplay test'], ['target', 'Target'], ['expected', 'Expected'], ['estimatedTime', 'Estimated time to the target:'], ['check', 'Check'], ['afterwards', 'Afterwards']]) {
    assert.ok(panel.includes(`smoke.${key}`), key);
    assert.equal(en(`smoke.${key}`), label);
  }
  assert.match(en('smoke.estimateAfter'), /estimated from the distance, not timed in the game/u, 'the estimate is labelled as an estimate');
  const app = await read('App.tsx');
  // It is an internal tool: it lives in the tools of the app settings and only on a development build; the normal screen does not mention test worlds.
  assert.ok(app.includes('{devTools && <SmokeTestPanel'), 'the panel is shown only when the internal tools are on');
  assert.equal(app.slice(0, app.indexOf('const toolsCard')).includes('<SmokeTestPanel'), false);
});

test('Part G: Sampling is the recommended default, the classic permutation stays selectable, and the sampling controls are unavailable (with the reason) where they do not apply', async () => {
  const { SETTINGS, applySetting, settingState, DEFAULT_GENERATION_SETTINGS, joinConfig } = await import('@ds1tr/contracts');
  const app = await read('App.tsx');
  assert.equal(en('settings.enemies.algorithm.options.sampling-v1'), 'Pick a replacement for every spot');
  assert.equal(en('settings.enemies.algorithm.options.permutation-v1'), 'Shuffle existing enemies');
  assert.deepEqual(SETTINGS.find((setting) => setting.id === 'enemies.algorithm').choices, ['sampling-v1', 'permutation-v1']);
  assert.match(en('help.settings.enemies.algorithm.choices.sampling-v1'), /\(recommended\)/u);
  assert.doesNotMatch(app, /[Ll]egacy-style/u, 'no implementation history in the visible text');
  assert.doesNotMatch(JSON.stringify({ ...english, smoke: undefined }), /[Ll]egacy-style/u, 'the screens say no implementation history');
  const base = joinConfig(structuredClone(DEFAULT_GENERATION_SETTINGS), '', 'seed', true);
  assert.equal(base.enemies.placement.algorithm, 'sampling-v1', 'a fresh screen starts from the contracts default');
  const classic = applySetting(base, 'enemies.algorithm', 'permutation-v1');
  for (const id of ['enemies.replacementChance', 'enemies.normalReplacement', 'enemies.fit', 'enemies.difficultyMode', 'enemies.typeReplacement', 'enemies.customPool', 'enemies.bossReplacement']) {
    assert.deepEqual([id, settingState(classic, id).enabled, settingState(classic, id).reason], [id, false, 'samplingOnly'], 'no control is left that looks usable but is not');
  }
  // No planned placeholder is left in the enemy section (the T-pose and NPC replacement controls did nothing).
  assert.doesNotMatch(app, /Allow T-pose behavior|NPC replacement/u);
  // Restore default settings returns to the contracts defaults; a loaded Seed Code keeps its own algorithm.
  assert.match(app, /const resetSettings = \(\) => \{\s*setConfig\(\(current\) => \(\{ \.\.\.initialConfig, gamePath: current\.gamePath \}\)\);/u);
});
