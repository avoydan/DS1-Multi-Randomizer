import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { decideStartupInstallation } from '@ds1tr/contracts';

const steam = 'C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS REMASTERED';
const mine = 'D:\Games\DSR copy';
const desktop = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'desktop', 'src');

test('a copy the person chose by hand wins over Steam detection after a restart', () => {
  assert.deepEqual(decideStartupInstallation({ saved: mine, savedValid: true, detected: [steam] }), { kind: 'saved', path: mine });
  assert.deepEqual(decideStartupInstallation({ saved: mine, savedValid: true, detected: [] }), { kind: 'saved', path: mine });
  assert.deepEqual(decideStartupInstallation({ saved: mine, savedValid: true, detected: [steam, 'E:\Other'] }), { kind: 'saved', path: mine });
});

test('detection is only the fallback: nothing saved, saved folder gone, or saved folder no longer valid', () => {
  assert.deepEqual(decideStartupInstallation({ saved: '', savedValid: false, detected: [steam] }), { kind: 'detected', path: steam });
  assert.deepEqual(decideStartupInstallation({ saved: mine, savedValid: false, detected: [steam] }), { kind: 'detected', path: steam });
  assert.deepEqual(decideStartupInstallation({ saved: mine, savedValid: false, detected: [steam, 'E:\Other'] }), { kind: 'choose', options: [steam, 'E:\Other'] });
  assert.deepEqual(decideStartupInstallation({ saved: '', savedValid: false, detected: [] }), { kind: 'missing', savedProblem: false });
  assert.deepEqual(decideStartupInstallation({ saved: mine, savedValid: false, detected: [] }), { kind: 'missing', savedProblem: true });
  // A whitespace-only value is no saved choice.
  assert.deepEqual(decideStartupInstallation({ saved: '   ', savedValid: true, detected: [steam] }), { kind: 'detected', path: steam });
});

test('the screen remembers a chosen folder only after the installation check accepted it, and never an automatic one', async () => {
  const readiness = await readFile(path.join(desktop, 'Readiness.tsx'), 'utf8');
  assert.match(readiness, /decideStartupInstallation\(/u);
  assert.match(readiness, /validation\.valid && manual\.current === path\) void window\.desktop\?\.rememberGamePath\(path\)/u);
  // Browse, Use this folder and "use the copy in ..." all go through pick(); automatic choices call onPath directly.
  assert.equal((readiness.match(/pick\(/gu) ?? []).length >= 3, true);
  assert.match(readiness, /if \(decision\.kind === 'saved' \|\| decision\.kind === 'detected'\) onPath\(decision\.path\)/u);
  const main = await readFile(path.join(desktop, '..', 'electron', 'main.ts'), 'utf8');
  assert.match(main, /ipcMain\.handle\('game:remember-path'/u);
  assert.doesNotMatch(main.slice(main.indexOf("'dialog:select-game-directory'"), main.indexOf("'language:get'")), /saveSettings/u, 'choosing a folder alone saves nothing');
});
