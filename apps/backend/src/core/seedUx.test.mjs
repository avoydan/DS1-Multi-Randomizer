import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { DSR_STEAM_APP_ID, OUTCOME_MESSAGES } from '@ds1tr/contracts';

// The renderer has no test runner of its own; these checks pin the user-visible contract of the Seed Code screens.
const desktop = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'desktop', 'src');
const read = (name) => readFile(path.join(desktop, name), 'utf8');

test('Apply and Restore end with the explicit success messages, and Apply offers Launch game', async () => {
  assert.equal(OUTCOME_MESSAGES.applied.text, 'Randomized game applied successfully.');
  assert.equal(OUTCOME_MESSAGES.applied.sub, 'You can now launch Dark Souls Remastered.');
  assert.equal(OUTCOME_MESSAGES.restored.text, 'Original game restored successfully.');
  const app = await read('App.tsx');
  assert.match(app, /setNotice\(\{ \.\.\.OUTCOME_MESSAGES\.applied \}\)/u);
  assert.match(app, /setNotice\(\{ \.\.\.OUTCOME_MESSAGES\.restored \}\)/u);
  assert.match(app, /Launch game/u);
  assert.equal(DSR_STEAM_APP_ID, '570940');
});

test('the master seed is not made at startup: the form starts empty and only Randomize creates one', async () => {
  const app = await read('App.tsx');
  assert.match(app, /joinConfig\(structuredClone\(DEFAULT_GENERATION_SETTINGS\), '', '', true\)/u);
  assert.doesNotMatch(app, /randomSeed|Math\.random\(\)/u, 'the renderer never invents a master seed; the backend makes it when a run starts');
  assert.match(app, />Randomize<\/button>/u);
  assert.match(app, />Load seed<\/button>/u);
  assert.doesNotMatch(await read('SeedPanel.tsx'), /Master seed/iu);
  assert.match(app, /Internal master seed: /u, 'the master seed is an Advanced detail, not part of the main seed controls');
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
  const app = await read('App.tsx');
  assert.match(app, /await generateShared\(seed\)/u);
  assert.match(app, /onLoaded=\{\(seed\) => void loadSeed\(seed\)\}/u);
});

test('Run history is collapsed by default, regenerating does not add history, and the menu holds the secondary actions', async () => {
  const panel = await read('RunHistoryPanel.tsx');
  assert.match(panel, /useState\(false\)/u);
  assert.match(panel, /Run history/u);
  for (const label of ['View settings', 'View spoiler log', 'Regenerate files', 'Delete from history']) assert.match(panel, new RegExp(label, 'u'));
  assert.doesNotMatch(panel, /Recreate/u);
  const app = await read('App.tsx');
  assert.doesNotMatch(app, /Recreate|recreate/u);
  assert.match(app, /const regenerate = async \(entry: RunHistoryEntry\) => \{\s*await generateRun\(entry\.seedCode\);/u);
});

test('the user-facing wording says Fog, and the LUDOS artwork is a small decorative accent in the header', async () => {
  const app = await read('App.tsx');
  assert.match(app, /Fog randomizer settings/u);
  assert.doesNotMatch(app, /World settings|WORLD<\/span>/u);
  assert.match(app, /import ludos from '\.\/assets\/branding\/ludos\.png'/u);
  assert.match(app, /className="hero-ludos" src=\{ludos\} alt="" aria-hidden="true"/u);
  const css = await read('styles.css');
  assert.match(css, /\.hero-ludos \{[^}]*width: clamp\(110px, 11vw, 150px\)/u);
});

test('Boss replacement and Unique bosses are real controls (not planned placeholders) and are reset with the algorithm', async () => {
  const app = await read('App.tsx');
  assert.match(app, /<label className="field-label">Boss replacement<\/label>/u);
  assert.match(app, /Random bosses \(legacy-style\)/u);
  assert.match(app, /label="Unique bosses"/u);
  assert.doesNotMatch(app, /\['Boss replacement', 'NPC replacement'\]/u);
  assert.doesNotMatch(app, /'Allow T-pose behavior', 'Unique bosses'/u);
  assert.match(app, /bossReplacement: 'classic', uniqueBosses: false/u);
});

test('bosses in regular slots (Part E1): the options are real, appear only when they apply, and are reset when they stop applying', async () => {
  const app = await read('App.tsx');
  assert.doesNotMatch(app, /Only bosses \(planned\)|Bosses or normal enemies \(planned\)/u, 'the two modes are no longer placeholders');
  assert.match(app, /<option value="bosses">Only bosses<\/option><option value="both">Bosses or normal enemies<\/option>/u);
  // Progressive disclosure: the boss chance only with "both", souls and respawn with either boss mode; nothing is shown as a dead control.
  assert.match(app, /normalReplacement === 'both' && \(\s*<RangeOption label="Boss chance in normal slots"/u);
  assert.match(app, /\(config\.enemies\.placement\.normalReplacement === 'bosses' \|\| config\.enemies\.placement\.normalReplacement === 'both'\) && \(/u);
  assert.match(app, /label="Roaming boss souls"/u);
  assert.match(app, /label="Roaming bosses stay dead"/u);
  // Leaving a mode puts the settings that only it needed back to their defaults (a hidden setting never stays in the world).
  assert.match(app, /if \(mode !== 'both'\) placement\.bossChanceInNormalSlots = 10;/u);
  assert.match(app, /if \(!roaming\) \{ placement\.roamingBossSouls = 50; placement\.roamingBossesStayDead = false; \}/u);
  // No dead twin of a real control is left behind in the planned group (a disabled "Roaming boss souls", "Boss chance: ..." or "Respawning roaming bosses").
  assert.doesNotMatch(app, /<RangeOption disabled label="(Boss chance: normal slots|Boss chance: boss slots|Roaming boss souls)"/u);
  assert.doesNotMatch(app, /'Respawning roaming bosses'/u);
  assert.equal((app.match(/label="Roaming boss souls"/gu) ?? []).length, 1, 'one Roaming boss souls control');
  // Plain language: no engine vocabulary in the visible labels.
  for (const label of ['Boss chance in normal slots', 'Roaming boss souls', 'Roaming bosses stay dead']) assert.doesNotMatch(label, /NpcParam|getSoul|entity|EMEVD/u);
});

test('the test worlds panel separates quick smoke tests from extended gameplay tests and shows the exact target, the expectation and the time', async () => {
  const panel = await read('SmokeTestPanel.tsx');
  assert.match(panel, /<optgroup label="Quick smoke tests/u);
  assert.match(panel, /<optgroup label="Extended gameplay tests/u);
  for (const label of ['Quick smoke test', 'Extended gameplay test', '<strong>Target</strong>', '<strong>Expected</strong>', 'Estimated time to the target', '<strong>Check</strong>', '<strong>Afterwards</strong>']) assert.ok(panel.includes(label), label);
  assert.match(panel, /estimated from the distance, not timed in the game/u, 'the estimate is labelled as an estimate');
  const app = await read('App.tsx');
  // It stays inside Advanced: the normal screen does not mention test worlds.
  const advanced = app.slice(app.indexOf('<summary>Advanced</summary>'));
  assert.ok(advanced.includes('<SmokeTestPanel'), 'the panel lives in Advanced');
  assert.equal(app.slice(0, app.indexOf('<summary>Advanced</summary>')).includes('<SmokeTestPanel'), false);
});
