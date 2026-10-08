# Final configuration and UX consistency audit

Date: 2026-10-08 (desktop 0.33.0, backend 0.45.0, contracts 0.31.0). Everything here is **AUTOMATED VERIFIED** unless it says otherwise; nothing was played in the game.

## 1. Configuration consistency (automated)

`apps/backend/src/core/configConsistency.test.mjs` walks every leaf of `GENERATION_FIELD_POLICY` (the list of every setting and what the Seed Code does with it) and checks, for each setting that decides a world (**61 fields**):

| Question | Check |
|---|---|
| Has a default? | present in `DEFAULT_GENERATION_SETTINGS` |
| Serialized? | present in an exported preset |
| Part of the Seed Code? | survives `encodeSeedCode` -> `decodeSeedCode` with the same value |
| Visible in the interface? | the settings screens contain a control that reads or writes it (an explicit allow-list names the three whose control is not a plain read: the custom enemy pool editor, the algorithm choice, the DLC switch) |
| Translated? | the English and Polish `settings` resources have exactly the same keys (and `locales.test.mjs` checks every area, plurals and variables) |
| Neutral value? | every extension setting's neutral value equals the value an old world meant, equals a fresh installation's value, and never appears in the Seed Code text |
| Classified? | every leaf of a configuration is in `GENERATION_FIELD_POLICY` (a new setting that is not classified fails) |

Result: **no dead, missing or ambiguous setting.** Settings that are *not* part of a world and are documented as such: `gamePath`, `output.generateSpoilerLog`. The interface language, the hint-sheet level and the diagnostics are not configuration of a world at all: they are not in the Seed Code, the preset, the history identity or any seed derivation (`I18N.md`, `HINT_SHEET.md`, `DIAGNOSTICS.md`); the test also checks that the canonical text of a world contains none of them.

### Neutral-setting invariants

| Setting | Neutral value | Pinned by |
|---|---|---|
| DLC content | on (`output.includeDlc = true`) | `seedCode.test.mjs`, golden (47/47 unchanged) |
| Laundromat Mixup | off | `configConsistency`, golden `item-modes` |
| Hostile NPCs | off | `configConsistency`, golden `hostile-npcs` |
| Bosses in regular slots (E1) / normal enemies in boss slots (E2) | `bossReplacement: classic` | `configConsistency`, golden `roaming-bosses`, `encounter-bodies` |
| Race / Race + | `keyPlacement: vanilla` | `configConsistency`, golden `key-placement`, `item-modes` |
| Item difficulty | Fair (no draw) | `configConsistency`, golden `item-modes` |
| Spoiler log, game path | not part of the world | `configConsistency` (same envelope and Seed Code whatever they are) |
| Interface language, hint level, diagnostics, support bundle | not an input of any seed | `configConsistency` (no such word in the canonical settings), the packaged same-world-both-languages check of the previous phase |

## 2. Findings and fixes

| # | Finding | Fix |
|---|---|---|
| 1 | The Include DLC control was a disabled placeholder ("not implemented") | Real switch with a DLC audit behind it (`DLC_AUDIT.md`); locked on with a note while the fog randomizer is on |
| 2 | A badge in the option groups was drawn in 8 px (below the 11 px minimum) | 11 px; `locales.test.mjs` now fails on any font size under 11 px |
| 3 | 15 backend failure codes had only the generic message (data tool, game data / catalog, output write, nothing enabled, fog component missing / crashed / timed out, merge conflict, ...) | Own plain messages (what happened, what to do) in both languages; a test pins that every code in `userMessages.ts` is translated and equal to the English resource |
| 4 | The problem lines of an invalid preset were English validator sentences | 17 sentences a person can meet are translated (`errors.problems.*`, tested with their variables); an unknown sentence stays as written, with the sentence always in a tooltip |
| 5 | The world summary (chips, hint sheet, diagnostics) had no word about the DLC | A DLC chip / summary line appears only for a world without the DLC (every earlier world reads exactly as before) |

## 3. Game path (existing tests, re-run)

| Case | Covered by |
|---|---|
| Steam auto-detect (registry, extra library, app manifest, usual folder) | `gameDiscovery.test.mjs` |
| Saved manual path wins over detection after a restart | `gameSelection.test.mjs` |
| Saved path gone / no longer valid -> detection is the fallback | `gameSelection.test.mjs` |
| Manual browse is remembered only after the installation check accepted it, never an automatic one | `gameSelection.test.mjs` |
| Nothing found / incomplete folder -> plain language | `gameDiscovery.test.mjs` |

## 4. Not changed on purpose

* **Support bundle preview before saving: DEFERRED.** A preview would need a second endpoint that returns the redacted entries without writing them; the bundle is the product of the allowlist, redaction and leak scan, and a second code path that shows "what will be sent" next to the one that sends it can disagree with it. The existing behavior (nothing leaves the machine; the person saves a file and chooses whom to send it to; a bundle that fails the leak scan is refused) is kept. Listed in the deferred list.
* **Game content names stay English** (no trustworthy localized source); they are parameters of translated sentences (`I18N.md`).
