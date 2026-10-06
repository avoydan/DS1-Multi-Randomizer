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
  assert.match(app, /masterSeed: ''/u);
  assert.doesNotMatch(app, /randomSeed|Math\.random\(\)/u, 'the renderer never invents a master seed; the backend makes it when a run starts');
  const panel = await read('SeedPanel.tsx');
  assert.match(panel, /Randomize/u);
  assert.match(panel, /Load seed/u);
  assert.doesNotMatch(panel, /Master seed/u, 'the master seed is an Advanced detail, not part of the main seed controls');
});
