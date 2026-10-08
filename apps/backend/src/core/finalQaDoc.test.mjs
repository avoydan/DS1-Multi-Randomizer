// docs/FINAL_GAMEPLAY_QA.md names the test worlds the way the interface does and carries the Seed Codes of its full-playthrough worlds. The document is a plan for a person, so
// this is the only thing that keeps it true: a renamed test world or a changed recipe fails here instead of misleading the owner.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { qaWorldCodes } from '../../scripts/qaWorlds.mjs';
import { smokeTestWorlds } from '../../dist/core/smokeTests.js';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const text = await readFile(path.join(repo, 'docs', 'FINAL_GAMEPLAY_QA.md'), 'utf8');
const { worlds } = smokeTestWorlds();

test('every test world the document names is a test world of the app, with its exact title, and the quick ones are all there', () => {
  const titles = new Set(worlds.map((world) => world.title));
  const rows = [...text.matchAll(/^\| \d+ \| ([^|]+?) \|/gmu)].map((match) => match[1]);
  assert.ok(rows.length >= 25);
  for (const row of rows) assert.ok(titles.has(row), `"${row}" is not the title of a test world`);
  for (const world of worlds.filter((entry) => entry.category === 'quick')) assert.ok(rows.includes(world.title), `${world.title} is listed`);
});

test('the Seed Codes of the full-playthrough worlds are the codes of their recipes', () => {
  const documented = [...text.matchAll(/^(DS1TR1-[0-9A-Z-]+)$/gmu)].map((match) => match[1]);
  assert.deepEqual(documented, qaWorldCodes().map((world) => world.seedCode));
});

test('what the document says was played is what the app records as played, and nothing else', () => {
  for (const world of worlds) {
    const row = new RegExp(`^\\| \\d+ \\| ${world.title.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&')} \\|.*\\|$`, 'mu').exec(text);
    if (!row) continue;
    assert.equal(/GAMEPLAY VERIFIED|PARTIAL/u.test(row[0]), Boolean(world.gameplay), `${world.title}: the played mark follows the record`);
  }
  assert.doesNotMatch(text, /\bRELEASE CANDIDATE\b.*GAMEPLAY VERIFIED/u);
});
