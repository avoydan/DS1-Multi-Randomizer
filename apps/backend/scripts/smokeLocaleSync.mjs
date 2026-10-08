// Keeps the English resource of the test worlds (apps/desktop/src/locales/en/smoke.json, "worlds" and "checklist") in step with the scenarios in the contracts.
// The contracts are the source of the English texts; the interface shows them from the resources so that they can be translated.
//
//   node apps/backend/scripts/smokeLocaleSync.mjs            check only (exit 1 when the resource is out of date)
//   node apps/backend/scripts/smokeLocaleSync.mjs --write    rewrite the English resource
//   node apps/backend/scripts/smokeLocaleSync.mjs --list     print every English text with its key (to translate)

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { smokeTestWorlds } from '../dist/core/smokeTests.js';

export const resourcePath = (language) => fileURLToPath(new URL(`../../desktop/src/locales/${language}/smoke.json`, import.meta.url));

/** The texts of every test world, keyed the way the interface looks them up (`smoke.worlds.<id>.<field>[.<index>]`). */
export function smokeTexts() {
  const { worlds, checklist, start } = smokeTestWorlds();
  const out = { worlds: {}, checklist: {}, start };
  for (const world of worlds) {
    const entry = { title: world.title, purpose: world.purpose };
    if (world.gameplay) entry.gameplayNote = world.gameplay.note;
    // targets and expectations are built from templates and the structured expectations, not stored per world.
    for (const field of ['observe', 'afterwards']) {
      if (world[field].length > 0) entry[field] = Object.fromEntries(world[field].map((text, index) => [String(index), text]));
    }
    out.worlds[world.id] = entry;
  }
  checklist.forEach((text, index) => { out.checklist[String(index)] = text; });
  return out;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const texts = smokeTexts();
  const english = JSON.parse(readFileSync(resourcePath('en'), 'utf8'));
  if (process.argv.includes('--list')) {
    for (const [id, entry] of Object.entries(texts.worlds)) {
      for (const [field, value] of Object.entries(entry)) {
        if (typeof value === 'string') console.log(`${id}.${field}\t${value}`);
        else for (const [index, text] of Object.entries(value)) console.log(`${id}.${field}.${index}\t${text}`);
      }
    }
    for (const [index, text] of Object.entries(texts.checklist)) console.log(`checklist.${index}\t${text}`);
  } else if (process.argv.includes('--write')) {
    english.worlds = texts.worlds;
    english.checklist = texts.checklist;
    english.start = texts.start;
    writeFileSync(resourcePath('en'), `${JSON.stringify(english, null, 2)}\n`);
    console.log('English test-world texts written.');
  } else {
    const same = JSON.stringify(english.worlds) === JSON.stringify(texts.worlds) && JSON.stringify(english.checklist) === JSON.stringify(texts.checklist) && english.start === texts.start;
    console.log(same ? 'The English test-world texts are in step with the contracts.' : 'The English test-world texts are OUT OF DATE: run with --write.');
    process.exitCode = same ? 0 : 1;
  }
}
