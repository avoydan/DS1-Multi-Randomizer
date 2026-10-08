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
  assert.equal(en('app.run.randomize'), 'Randomize');
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
  for (const [key, label] of [['history.viewSettings', 'View settings'], ['history.viewSpoiler', 'View spoiler log'], ['history.regenerate', 'Regenerate files'], ['history.delete', 'Delete from history']]) {
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

test('the user-facing wording says Fog, and the LUDOS artwork is a small decorative accent in the header', async () => {
  const app = await read('App.tsx');
  assert.equal(en('settings.fog.title'), 'Fog randomizer settings');
  assert.ok(used(app, 'settings.fog.title'));
  assert.doesNotMatch(app, /World settings|WORLD<\/span>/u);
  assert.doesNotMatch(JSON.stringify(english.settings), /World settings/u);
  assert.match(app, /import ludos from '\.\/assets\/branding\/ludos\.png'/u);
  assert.match(app, /className="hero-ludos" src=\{ludos\} alt="" aria-hidden="true"/u);
  const css = await read('styles.css');
  assert.match(css, /\.hero-ludos \{[^}]*width: clamp\(110px, 11vw, 150px\)/u);
});

test('Boss replacement and Unique bosses are real controls (not planned placeholders) and are reset with the algorithm', async () => {
  const app = await read('App.tsx');
  assert.equal(en('settings.enemies.bossReplacement.label'), 'Boss replacement');
  assert.equal(en('settings.enemies.bossReplacement.options.bosses'), 'Random bosses');
  assert.ok(app.includes("<label className=\"field-label\">{t('settings.enemies.bossReplacement.label')}</label>"));
  assert.ok(app.includes("<option value=\"bosses\">{t('settings.enemies.bossReplacement.options.bosses')}</option>"));
  assert.ok(app.includes("label={t('settings.enemies.uniqueBosses.label')}"));
  assert.equal(en('settings.enemies.uniqueBosses.label'), 'Unique bosses');
  assert.doesNotMatch(app, /\['Boss replacement', 'NPC replacement'\]/u);
  assert.doesNotMatch(app, /'Allow T-pose behavior', 'Unique bosses'/u);
  assert.match(app, /bossReplacement: 'classic', uniqueBosses: false/u);
});

test('bosses in regular slots (Part E1): the options are real, appear only when they apply, and are reset when they stop applying', async () => {
  const app = await read('App.tsx');
  const modes = JSON.stringify(english.settings.enemies.normalReplacement.options);
  assert.doesNotMatch(modes + JSON.stringify(english.settings.enemies.bossReplacement.options), /\(planned\)/u, 'the two modes are no longer placeholders');
  assert.equal(en('settings.enemies.normalReplacement.options.bosses'), 'Only bosses');
  assert.equal(en('settings.enemies.normalReplacement.options.both'), 'Bosses or normal enemies');
  assert.ok(app.includes("<option value=\"bosses\">{t('settings.enemies.normalReplacement.options.bosses')}</option><option value=\"both\">{t('settings.enemies.normalReplacement.options.both')}</option>"));
  // Progressive disclosure: the boss chance only with "both", souls and respawn with either boss mode; nothing is shown as a dead control.
  assert.match(app, /normalReplacement === 'both' && \(\s*<RangeOption label=\{t\('settings\.enemies\.bossChanceNormal\.label'\)\}/u);
  assert.match(app, /\(config\.enemies\.placement\.normalReplacement === 'bosses' \|\| config\.enemies\.placement\.normalReplacement === 'both'\) && \(/u);
  assert.ok(app.includes("label={t('settings.enemies.roamingSouls.label')}"));
  assert.ok(app.includes("label={t('settings.enemies.roamingDead.label')}"));
  // Leaving a mode puts the settings that only it needed back to their defaults (a hidden setting never stays in the world).
  assert.match(app, /if \(mode !== 'both'\) placement\.bossChanceInNormalSlots = 10;/u);
  assert.match(app, /if \(!roaming\) \{ placement\.roamingBossSouls = 50; placement\.roamingBossesStayDead = false; \}/u);
  // No dead twin of a real control is left behind in the planned group (a disabled "Roaming boss souls", "Boss chance: ..." or "Respawning roaming bosses").
  assert.doesNotMatch(app, /<RangeOption disabled label="(Boss chance: normal slots|Boss chance: boss slots|Roaming boss souls)"/u);
  assert.doesNotMatch(app, /'Respawning roaming bosses'/u);
  assert.equal((app.match(/settings\.enemies\.roamingSouls\.label/gu) ?? []).length, 1, 'one Roaming boss souls control');
  // Plain language: no engine vocabulary in the visible labels.
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
  // It stays inside Advanced: the normal screen does not mention test worlds.
  const advancedAt = app.indexOf("<summary>{t('app.advanced.title')}</summary>");
  assert.ok(advancedAt > 0 && app.slice(advancedAt).includes('<SmokeTestPanel'), 'the panel lives in Advanced');
  assert.equal(app.slice(0, advancedAt).includes('<SmokeTestPanel'), false);
});

test('Part G: Sampling is the recommended default, the classic permutation stays selectable, and the sampling controls exist only where they apply', async () => {
  const app = await read('App.tsx');
  assert.equal(en('settings.enemies.algorithm.options.sampling-v1'), 'Sampling (recommended)');
  assert.equal(en('settings.enemies.algorithm.options.permutation-v1'), 'Classic permutation');
  assert.ok(app.includes("<option value=\"sampling-v1\">{t('settings.enemies.algorithm.options.sampling-v1')}</option><option value=\"permutation-v1\">{t('settings.enemies.algorithm.options.permutation-v1')}</option>"));
  assert.doesNotMatch(app, /[Ll]egacy-style/u, 'no implementation history in the visible text');
  assert.doesNotMatch(JSON.stringify({ ...english, smoke: undefined }), /[Ll]egacy-style/u, 'the screens say no implementation history');
  // Everything between the replacement chance and the custom pool editor is inside one sampling-only block: no disabled twin is shown for the classic permutation.
  assert.match(app, /\{config\.enemies\.placement\.algorithm === 'sampling-v1' && \(\s*<>\s*<RangeOption label=\{t\('settings\.enemies\.replacementChance\.label'\)\}/u);
  assert.match(app, /<EnemyPoolEditor[\s\S]*?<\/>\s*\)\}/u);
  // No planned placeholder is left in the enemy section (the T-pose and NPC replacement controls did nothing).
  assert.doesNotMatch(app, /Allow T-pose behavior|NPC replacement/u);
  // A fresh screen starts from the contracts default (sampling); Restore default settings returns to it, a loaded Seed Code keeps its own algorithm.
  assert.match(app, /const resetSettings = \(\) => \{\s*setConfig\(\(current\) => \(\{ \.\.\.initialConfig, gamePath: current\.gamePath \}\)\);/u);
});
